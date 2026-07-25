using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Etch.Text.Tests.TextParity;

/// <summary>
/// WP-3527: a DirectWrite reference glyph-coverage probe — the Windows
/// best-in-class engine, used alongside Skia to grade Etch's text quality.
///
/// Bound by hand with no NuGet dependency: <c>DWriteCreateFactory</c> via
/// <c>[LibraryImport]</c>, and every COM call made directly through the
/// object's vtable by slot index using calling-convention function pointers. This
/// is fully AOT/trim-clean (no <c>[ComImport]</c>, no reflection) and references
/// only the exact methods used. Windows-only; callers must check
/// <see cref="IsSupported"/> and skip gracefully elsewhere (CI/Linux/macOS).
///
/// Coverage path: CreateFontFileReference(temp .ttf) → CreateFontFace →
/// GetGlyphIndices → CreateGlyphRunAnalysis(NATURAL_SYMMETRIC) →
/// CreateAlphaTexture(CLEARTYPE_3x1), then the three subpixel channels are
/// averaged to a single grayscale coverage byte for an apples-to-apples
/// comparison with the grayscale Etch/Skia masks. Output is top-down, tight to
/// the glyph's alpha bounds (matching <see cref="Etch.SkiaRef.SkiaGlyphProbe"/>).
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class DirectWriteGlyphProbe : IDisposable
{
    public static bool IsSupported => OperatingSystem.IsWindows();

    // ── COM constants ────────────────────────────────────────────────
    // IID_IDWriteFactory {b859ee5a-d838-4b5b-a2e8-1adc7d93db48}
    private static readonly Guid IID_IDWriteFactory =
        new(0xb859ee5a, 0xd838, 0x4b5b, 0xa2, 0xe8, 0x1a, 0xdc, 0x7d, 0x93, 0xdb, 0x48);

    private const int DWRITE_FACTORY_TYPE_ISOLATED = 1;
    // DWRITE_FONT_FACE_TYPE: CFF=0, TRUETYPE=1. Our test fonts are TrueType.
    private const int DWRITE_FONT_FACE_TYPE_TRUETYPE = 1;
    private const int DWRITE_RENDERING_MODE_NATURAL_SYMMETRIC = 5;
    private const int DWRITE_MEASURING_MODE_NATURAL = 0;
    private const int DWRITE_TEXTURE_CLEARTYPE_3x1 = 1;

    // IDWriteFactory vtable slots (after the 3 IUnknown slots).
    private const int Factory_CreateFontFileReference = 7;
    private const int Factory_CreateFontFace = 9;
    private const int Factory_CreateGlyphRunAnalysis = 23;
    // IDWriteFontFace vtable slots.
    private const int FontFace_GetMetrics = 8;
    private const int FontFace_GetDesignGlyphMetrics = 10;
    private const int FontFace_GetGlyphIndices = 11;
    // IDWriteGlyphRunAnalysis vtable slots.
    private const int Analysis_GetAlphaTextureBounds = 3;
    private const int Analysis_CreateAlphaTexture = 4;
    // IUnknown.
    private const int IUnknown_Release = 2;

    [LibraryImport("dwrite.dll")]
    [DefaultDllImportSearchPaths(DllImportSearchPath.System32)]
    private static partial int DWriteCreateFactory(int factoryType, in Guid iid, out IntPtr factory);

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphRun
    {
        public IntPtr FontFace;
        public float FontEmSize;
        public uint GlyphCount;
        public IntPtr GlyphIndices;   // ushort*
        public IntPtr GlyphAdvances;  // float*
        public IntPtr GlyphOffsets;   // null
        public int IsSideways;
        public uint BidiLevel;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Rect { public int Left, Top, Right, Bottom; }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphOffset { public float AdvanceOffset, AscenderOffset; }

    [StructLayout(LayoutKind.Sequential)]
    private struct FontMetrics
    {
        public ushort DesignUnitsPerEm;
        public ushort Ascent, Descent;
        public short LineGap;
        public ushort CapHeight, XHeight;
        public short UnderlinePosition;
        public ushort UnderlineThickness;
        public short StrikethroughPosition;
        public ushort StrikethroughThickness;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct GlyphMetrics
    {
        public int LeftSideBearing;
        public uint AdvanceWidth;
        public int RightSideBearing;
        public int TopSideBearing;
        public uint AdvanceHeight;
        public int BottomSideBearing;
        public int VerticalOriginY;
    }

    private readonly IntPtr _factory;
    private readonly IntPtr _fontFace;
    private readonly string _tempFontPath;
    private readonly ushort _unitsPerEm;
    private bool _disposed;

    /// <summary>Diagnostic: the stage + HRESULT of the most recent render failure.</summary>
    public string? LastError { get; private set; }

    private DirectWriteGlyphProbe(IntPtr factory, IntPtr fontFace, string tempFontPath, ushort unitsPerEm)
    {
        _factory = factory;
        _fontFace = fontFace;
        _tempFontPath = tempFontPath;
        _unitsPerEm = unitsPerEm;
    }

    /// <summary>Reads a vtable slot of a COM object as a calling-convention function pointer.</summary>
    private static void* Vt(IntPtr obj, int slot)
    {
        IntPtr* vtable = *(IntPtr**)obj;
        return (void*)vtable[slot];
    }

    /// <summary>
    /// Loads <paramref name="fontData"/> as a DirectWrite font face, or returns null
    /// if DirectWrite is unavailable / the font cannot be loaded.
    /// </summary>
    public static DirectWriteGlyphProbe? TryCreate(ReadOnlySpan<byte> fontData)
    {
        if (!IsSupported)
        {
            return null;
        }

        string tempPath = Path.Combine(Path.GetTempPath(), "etch-dwref-" + Guid.NewGuid().ToString("N") + ".ttf");
        IntPtr factory = IntPtr.Zero;
        IntPtr fontFile = IntPtr.Zero;
        IntPtr fontFace = IntPtr.Zero;
        bool transferred = false;
        try
        {
            File.WriteAllBytes(tempPath, fontData.ToArray());

            if (DWriteCreateFactory(DWRITE_FACTORY_TYPE_ISOLATED, in IID_IDWriteFactory, out factory) < 0 || factory == IntPtr.Zero)
            {
                return null;
            }

            // CreateFontFileReference(WCHAR* path, FILETIME* lastWrite=null, IDWriteFontFile** out)
            fixed (char* pPath = tempPath)
            {
                var create = (delegate* unmanaged[Stdcall]<IntPtr, char*, void*, IntPtr*, int>)Vt(factory, Factory_CreateFontFileReference);
                IntPtr ff;
                if (create(factory, pPath, null, &ff) < 0 || ff == IntPtr.Zero)
                {
                    return null;
                }
                fontFile = ff;
            }

            // CreateFontFace(type, numFiles, IDWriteFontFile** files, faceIndex, simFlags, IDWriteFontFace** out)
            {
                var create = (delegate* unmanaged[Stdcall]<IntPtr, int, uint, IntPtr*, uint, int, IntPtr*, int>)Vt(factory, Factory_CreateFontFace);
                IntPtr files = fontFile;
                IntPtr face;
                if (create(factory, DWRITE_FONT_FACE_TYPE_TRUETYPE, 1, &files, 0, 0, &face) < 0 || face == IntPtr.Zero)
                {
                    return null;
                }
                fontFace = face;
            }

            // GetMetrics(DWRITE_FONT_METRICS* out) — returns void.
            FontMetrics fm;
            {
                var getMetrics = (delegate* unmanaged[Stdcall]<IntPtr, FontMetrics*, void>)Vt(fontFace, FontFace_GetMetrics);
                getMetrics(fontFace, &fm);
            }

            // The font file ref is no longer needed once the face exists.
            Release(fontFile);
            fontFile = IntPtr.Zero;

            var probe = new DirectWriteGlyphProbe(factory, fontFace, tempPath, fm.DesignUnitsPerEm == 0 ? (ushort)2048 : fm.DesignUnitsPerEm);
            factory = fontFace = IntPtr.Zero; // ownership transferred
            transferred = true;
            return probe;
        }
        catch (DllNotFoundException)
        {
            return null;
        }
        catch (EntryPointNotFoundException)
        {
            return null;
        }
        finally
        {
            if (fontFile != IntPtr.Zero) Release(fontFile);
            if (fontFace != IntPtr.Zero) Release(fontFace);
            if (factory != IntPtr.Zero) Release(factory);
            if (!transferred)
            {
                TryDeleteTemp(tempPath);
            }
        }
    }

    /// <summary>
    /// Renders a single character to a grayscale coverage buffer (top-down, tight
    /// to the glyph). Returns false (empty) for missing/whitespace glyphs.
    /// <paramref name="pixelSize"/> is device px-per-em (pixelsPerDip = 1).
    /// </summary>
    public bool TryRenderChar(string character, float pixelSize, Span<byte> dest, out int width, out int height, out float advancePx)
    {
        width = 0;
        height = 0;
        advancePx = 0;
        if (string.IsNullOrEmpty(character) || pixelSize <= 0)
        {
            return false;
        }

        LastError = null;
        uint codePoint = (uint)char.ConvertToUtf32(character, 0);
        ushort glyphIndex;
        {
            var getIndices = (delegate* unmanaged[Stdcall]<IntPtr, uint*, uint, ushort*, int>)Vt(_fontFace, FontFace_GetGlyphIndices);
            int hr = getIndices(_fontFace, &codePoint, 1, &glyphIndex);
            if (hr < 0 || glyphIndex == 0)
            {
                LastError = $"GetGlyphIndices hr=0x{hr:X8} gid={glyphIndex}";
                return false;
            }
        }

        // Advance (design units → px at this em size).
        int gdmHr = -1;
        {
            var getGm = (delegate* unmanaged[Stdcall]<IntPtr, ushort*, uint, GlyphMetrics*, int, int>)Vt(_fontFace, FontFace_GetDesignGlyphMetrics);
            GlyphMetrics gm;
            ushort gi = glyphIndex;
            gdmHr = getGm(_fontFace, &gi, 1, &gm, 0);
            if (gdmHr >= 0)
            {
                advancePx = gm.AdvanceWidth * pixelSize / _unitsPerEm;
            }
        }

        float advance = 0f;
        var offset = default(GlyphOffset);
        var run = new GlyphRun
        {
            FontFace = _fontFace,
            FontEmSize = pixelSize,
            GlyphCount = 1,
            GlyphIndices = (IntPtr)(&glyphIndex),
            GlyphAdvances = (IntPtr)(&advance),
            GlyphOffsets = (IntPtr)(&offset),
            IsSideways = 0,
            BidiLevel = 0,
        };

        IntPtr analysis = IntPtr.Zero;
        try
        {
            var createAnalysis = (delegate* unmanaged[Stdcall]<IntPtr, GlyphRun*, float, void*, int, int, float, float, IntPtr*, int>)Vt(_factory, Factory_CreateGlyphRunAnalysis);
            IntPtr a;
            int hrA = createAnalysis(_factory, &run, 1.0f, null, DWRITE_RENDERING_MODE_NATURAL_SYMMETRIC, DWRITE_MEASURING_MODE_NATURAL, 0f, 0f, &a);
            if (hrA < 0 || a == IntPtr.Zero)
            {
                LastError = $"CreateGlyphRunAnalysis hr=0x{hrA:X8}";
                return false;
            }
            analysis = a;

            Rect bounds;
            {
                var getBounds = (delegate* unmanaged[Stdcall]<IntPtr, int, Rect*, int>)Vt(analysis, Analysis_GetAlphaTextureBounds);
                int hrB = getBounds(analysis, DWRITE_TEXTURE_CLEARTYPE_3x1, &bounds);
                if (hrB < 0)
                {
                    LastError = $"GetAlphaTextureBounds hr=0x{hrB:X8}";
                    return false;
                }
            }

            int w = bounds.Right - bounds.Left;
            int h = bounds.Bottom - bounds.Top;
            if (w <= 0 || h <= 0)
            {
                LastError = $"empty bounds (no ink) for gid={glyphIndex}";
                return false; // whitespace / no ink
            }

            int bufLen = w * h * 3;
            byte[] cleartype = new byte[bufLen];
            {
                var createTex = (delegate* unmanaged[Stdcall]<IntPtr, int, Rect*, byte*, uint, int>)Vt(analysis, Analysis_CreateAlphaTexture);
                fixed (byte* pTex = cleartype)
                {
                    if (createTex(analysis, DWRITE_TEXTURE_CLEARTYPE_3x1, &bounds, pTex, (uint)bufLen) < 0)
                    {
                        return false;
                    }
                }
            }

            int pixelCount = w * h;
            if (dest.Length < pixelCount)
            {
                return false;
            }
            // Average the R/G/B subpixel coverage to a single grayscale byte.
            for (int i = 0; i < pixelCount; i++)
            {
                int s = cleartype[i * 3] + cleartype[i * 3 + 1] + cleartype[i * 3 + 2];
                dest[i] = (byte)((s + 1) / 3);
            }

            width = w;
            height = h;
            return true;
        }
        finally
        {
            if (analysis != IntPtr.Zero)
            {
                Release(analysis);
            }
        }
    }

    private static void Release(IntPtr obj)
    {
        if (obj == IntPtr.Zero)
        {
            return;
        }
        var release = (delegate* unmanaged[Stdcall]<IntPtr, uint>)Vt(obj, IUnknown_Release);
        release(obj);
    }

    private static void TryDeleteTemp(string path)
    {
        try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        if (_fontFace != IntPtr.Zero) Release(_fontFace);
        if (_factory != IntPtr.Zero) Release(_factory);
        TryDeleteTemp(_tempFontPath);
    }
}
