using System;
using System.Runtime.InteropServices;

// hb_* interop keeps the native C entry-point names (underscores) and mirrors native struct
// layouts (fields read from native memory, not assigned in managed code).
#pragma warning disable CA1707 // underscores in member names — native hb_* entry points
#pragma warning disable CS0649 // field never assigned — read from native buffers
#pragma warning disable IDE0044
// CA5392: the native library is resolved through the shared NativeLibraryResolver
// (SetDllImportResolver, absolute-path probing) instead of DefaultDllImportSearchPaths.
#pragma warning disable CA5392
// CA2216: these thin handle wrappers are internal and always disposed deterministically by their
// owner (FontFace owns Blob/Face/Font/Buffer; reference-table blobs are freed in try/finally). A
// finalizer would only add GC pressure to hot shaping objects, so we omit it deliberately.
#pragma warning disable CA2216

namespace Etch.Text.HarfBuzz;

/// <summary>
/// In-house HarfBuzz interop. Replaces the former third-party managed binding + its NativeAssets
/// packages; the native library load path is owned by <see cref="Etch.Text.NativeLibraryResolver"/>
/// (a single <see cref="NativeLibrary.SetDllImportResolver"/> shared with FreeType). Only the small
/// shaping subset Etch.Text uses is bound. The thin <see cref="Blob"/>/<see cref="Face"/>/
/// <see cref="Font"/>/<see cref="Buffer"/> wrappers below preserve the call-site shape so the
/// shaping and glyph-outline code is unchanged.
/// </summary>
internal static unsafe partial class HarfBuzzNative
{
    private const string LibraryName = "harfbuzz";

    [LibraryImport(LibraryName)]
    internal static partial nint hb_blob_create(void* data, uint length, MemoryMode mode, nint userData, nint destroy);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint hb_blob_create_from_file(string fileName);

    [LibraryImport(LibraryName)]
    internal static partial void hb_blob_destroy(nint blob);

    [LibraryImport(LibraryName)]
    internal static partial void* hb_blob_get_data(nint blob, uint* length);

    [LibraryImport(LibraryName)]
    internal static partial nint hb_face_create(nint blob, uint index);

    [LibraryImport(LibraryName)]
    internal static partial void hb_face_set_upem(nint face, uint upem);

    [LibraryImport(LibraryName)]
    internal static partial uint hb_face_get_upem(nint face);

    [LibraryImport(LibraryName)]
    internal static partial void hb_face_make_immutable(nint face);

    [LibraryImport(LibraryName)]
    internal static partial void hb_face_destroy(nint face);

    [LibraryImport(LibraryName)]
    internal static partial nint hb_face_reference_table(nint face, uint tag);

    [LibraryImport(LibraryName)]
    internal static partial nint hb_font_create(nint face);

    [LibraryImport(LibraryName)]
    internal static partial void hb_font_set_scale(nint font, int xScale, int yScale);

    [LibraryImport(LibraryName)]
    internal static partial int hb_font_get_glyph(nint font, uint unicode, uint variationSelector, out uint glyph);

    [LibraryImport(LibraryName)]
    internal static partial void hb_ot_font_set_funcs(nint font);

    [LibraryImport(LibraryName)]
    internal static partial int hb_font_get_h_extents(nint font, out FontExtents extents);

    [LibraryImport(LibraryName)]
    internal static partial int hb_font_get_glyph_extents(nint font, uint glyph, out GlyphExtents extents);

    [LibraryImport(LibraryName)]
    internal static partial void hb_font_destroy(nint font);

    [LibraryImport(LibraryName)]
    internal static partial void hb_shape(nint font, nint buffer, Feature* features, uint numFeatures);

    [LibraryImport(LibraryName)]
    internal static partial nint hb_buffer_create();

    [LibraryImport(LibraryName)]
    internal static partial void hb_buffer_clear_contents(nint buffer);

    [LibraryImport(LibraryName)]
    internal static partial void hb_buffer_guess_segment_properties(nint buffer);

    [LibraryImport(LibraryName)]
    internal static partial void hb_buffer_set_direction(nint buffer, Direction direction);

    [LibraryImport(LibraryName)]
    internal static partial void hb_buffer_set_script(nint buffer, uint script);

    [LibraryImport(LibraryName)]
    internal static partial void hb_buffer_set_language(nint buffer, nint language);

    [LibraryImport(LibraryName)]
    internal static partial void hb_buffer_add_utf16(nint buffer, ushort* text, int textLength, uint itemOffset, int itemLength);

    [LibraryImport(LibraryName)]
    internal static partial uint hb_buffer_get_length(nint buffer);

    [LibraryImport(LibraryName)]
    internal static partial HbGlyphInfo* hb_buffer_get_glyph_infos(nint buffer, uint* length);

    [LibraryImport(LibraryName)]
    internal static partial HbGlyphPosition* hb_buffer_get_glyph_positions(nint buffer, uint* length);

    [LibraryImport(LibraryName)]
    internal static partial void hb_buffer_destroy(nint buffer);

    [LibraryImport(LibraryName, StringMarshalling = StringMarshalling.Utf8)]
    internal static partial nint hb_language_from_string(string str, int len);
}

// ---- Enums (values match the native hb_*_t) ----

internal enum MemoryMode
{
    Duplicate = 0,
    ReadOnly = 1,
    Writeable = 2,
    ReadOnlyMayMakeWriteable = 3,
}

internal enum Direction
{
    Invalid = 0,
    LeftToRight = 4,
    RightToLeft = 5,
    TopToBottom = 6,
    BottomToTop = 7,
}

/// <summary>hb_script_t is a tag (ISO 15924). Only the scripts Etch.Text sets explicitly appear.</summary>
internal enum Script : uint
{
    Arabic = 0x41726162, // 'Arab'
}

// ---- Value types ----

/// <summary>An OpenType/HarfBuzz 4-byte tag (hb_tag_t).</summary>
internal readonly struct Tag
{
    public readonly uint Value;

    public Tag(char a, char b, char c, char d)
    {
        Value = ((uint)a << 24) | ((uint)b << 16) | ((uint)c << 8) | (uint)d;
    }

    public static implicit operator uint(Tag tag) => tag.Value;
}

/// <summary>Mirrors hb_feature_t: { tag, value, start, end }.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct Feature
{
    public Tag Tag;
    public uint Value;
    public uint Start;
    public uint End;

    public Feature(Tag tag, uint value, uint start, uint end)
    {
        Tag = tag;
        Value = value;
        Start = start;
        End = end;
    }
}

/// <summary>Mirrors hb_glyph_info_t; Etch reads Codepoint and Cluster.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HbGlyphInfo
{
    public uint Codepoint;
    public uint Mask;
    public uint Cluster;
    public int Var1;
    public int Var2;
}

/// <summary>Mirrors hb_glyph_position_t; Etch reads XAdvance, XOffset, YOffset.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct HbGlyphPosition
{
    public int XAdvance;
    public int YAdvance;
    public int XOffset;
    public int YOffset;
    public int Var;
}

/// <summary>Mirrors hb_font_extents_t (ascender/descender/line_gap + 9 reserved positions).</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct FontExtents
{
    public int Ascender;
    public int Descender;
    public int LineGap;
    public int Reserved9;
    public int Reserved8;
    public int Reserved7;
    public int Reserved6;
    public int Reserved5;
    public int Reserved4;
    public int Reserved3;
    public int Reserved2;
    public int Reserved1;
}

/// <summary>Mirrors hb_glyph_extents_t.</summary>
[StructLayout(LayoutKind.Sequential)]
internal struct GlyphExtents
{
    public int XBearing;
    public int YBearing;
    public int Width;
    public int Height;
}

// ---- Thin managed wrappers over the hb handles (preserve the previous call-site API) ----

/// <summary>A block of native memory referenced by HarfBuzz (hb_blob_t).</summary>
internal sealed class Blob : IDisposable
{
    private nint handle;

    /// <summary>Optional finalizer signature the caller may pass; the previous binding always
    /// passed null (the caller owns the memory), so it is accepted and ignored here.</summary>
    internal delegate void ReleaseDelegate();

    public Blob(nint data, int length, MemoryMode mode, ReleaseDelegate? release)
    {
        // No hb destroy callback: the caller pins/owns the underlying bytes for the blob's lifetime
        // (matching the previous binding, which passed a null release with MemoryMode.ReadOnly).
        _ = release;
        unsafe
        {
            handle = HarfBuzzNative.hb_blob_create((void*)data, (uint)length, mode, 0, 0);
        }
    }

    /// <summary>Wraps a blob HarfBuzz already owns (e.g. from hb_face_reference_table).</summary>
    internal Blob(nint existingHandle)
    {
        handle = existingHandle;
    }

    /// <summary>Creates a blob that reads a font file directly (HarfBuzz owns the mapping).</summary>
    public static Blob FromFile(string fileName)
    {
        return new Blob(HarfBuzzNative.hb_blob_create_from_file(fileName));
    }

    public nint Handle => handle;

    public Span<byte> AsSpan()
    {
        if (handle == 0)
        {
            return Span<byte>.Empty;
        }
        unsafe
        {
            uint length;
            void* data = HarfBuzzNative.hb_blob_get_data(handle, &length);
            if (data == null || length == 0)
            {
                return Span<byte>.Empty;
            }
            return new Span<byte>(data, (int)length);
        }
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            HarfBuzzNative.hb_blob_destroy(handle);
            handle = 0;
        }
    }
}

/// <summary>A HarfBuzz face (hb_face_t).</summary>
internal sealed class Face : IDisposable
{
    private nint handle;

    public Face(Blob blob, uint index)
    {
        handle = HarfBuzzNative.hb_face_create(blob.Handle, index);
    }

    public nint Handle => handle;

    public int UnitsPerEm
    {
        get { return (int)HarfBuzzNative.hb_face_get_upem(handle); }
        set { HarfBuzzNative.hb_face_set_upem(handle, (uint)value); }
    }

    public void MakeImmutable()
    {
        HarfBuzzNative.hb_face_make_immutable(handle);
    }

    public Blob ReferenceTable(Tag tag)
    {
        return new Blob(HarfBuzzNative.hb_face_reference_table(handle, tag));
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            HarfBuzzNative.hb_face_destroy(handle);
            handle = 0;
        }
    }
}

/// <summary>A HarfBuzz font (hb_font_t).</summary>
internal sealed class Font : IDisposable
{
    private nint handle;

    public Font(Face face)
    {
        handle = HarfBuzzNative.hb_font_create(face.Handle);
    }

    public nint Handle => handle;

    public void SetScale(int xScale, int yScale)
    {
        HarfBuzzNative.hb_font_set_scale(handle, xScale, yScale);
    }

    /// <summary>Looks up the nominal glyph for a Unicode codepoint (no variation selector).</summary>
    public bool TryGetGlyph(uint codepoint, out uint glyph)
    {
        return HarfBuzzNative.hb_font_get_glyph(handle, codepoint, 0, out glyph) != 0;
    }

    /// <summary>Installs the OpenType font funcs (metrics/glyphs read from the OT tables).</summary>
    public void SetFunctionsOpenType()
    {
        HarfBuzzNative.hb_ot_font_set_funcs(handle);
    }

    public bool TryGetHorizontalFontExtents(out FontExtents extents)
    {
        return HarfBuzzNative.hb_font_get_h_extents(handle, out extents) != 0;
    }

    public bool TryGetGlyphExtents(uint glyph, out GlyphExtents extents)
    {
        return HarfBuzzNative.hb_font_get_glyph_extents(handle, glyph, out extents) != 0;
    }

    public void Shape(Buffer buffer, Feature[]? features)
    {
        unsafe
        {
            if (features is { Length: > 0 })
            {
                fixed (Feature* f = features)
                {
                    HarfBuzzNative.hb_shape(handle, buffer.Handle, f, (uint)features.Length);
                }
            }
            else
            {
                HarfBuzzNative.hb_shape(handle, buffer.Handle, null, 0);
            }
        }
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            HarfBuzzNative.hb_font_destroy(handle);
            handle = 0;
        }
    }
}

/// <summary>A HarfBuzz shaping buffer (hb_buffer_t).</summary>
internal sealed class Buffer : IDisposable
{
    private nint handle;

    public Buffer()
    {
        handle = HarfBuzzNative.hb_buffer_create();
    }

    public nint Handle => handle;

    public void ClearContents()
    {
        HarfBuzzNative.hb_buffer_clear_contents(handle);
    }

    public void GuessSegmentProperties()
    {
        HarfBuzzNative.hb_buffer_guess_segment_properties(handle);
    }

    public Direction Direction
    {
        set { HarfBuzzNative.hb_buffer_set_direction(handle, value); }
    }

    public Script Script
    {
        set { HarfBuzzNative.hb_buffer_set_script(handle, (uint)value); }
    }

    public Language Language
    {
        set { HarfBuzzNative.hb_buffer_set_language(handle, value.Handle); }
    }

    public void AddUtf16(ReadOnlySpan<char> text)
    {
        unsafe
        {
            fixed (char* p = text)
            {
                HarfBuzzNative.hb_buffer_add_utf16(handle, (ushort*)p, text.Length, 0, text.Length);
            }
        }
    }

    public int Length => (int)HarfBuzzNative.hb_buffer_get_length(handle);

    public ReadOnlySpan<HbGlyphInfo> GlyphInfos
    {
        get
        {
            unsafe
            {
                uint length;
                HbGlyphInfo* p = HarfBuzzNative.hb_buffer_get_glyph_infos(handle, &length);
                return new ReadOnlySpan<HbGlyphInfo>(p, (int)length);
            }
        }
    }

    public ReadOnlySpan<HbGlyphPosition> GlyphPositions
    {
        get
        {
            unsafe
            {
                uint length;
                HbGlyphPosition* p = HarfBuzzNative.hb_buffer_get_glyph_positions(handle, &length);
                return new ReadOnlySpan<HbGlyphPosition>(p, (int)length);
            }
        }
    }

    public void Dispose()
    {
        if (handle != 0)
        {
            HarfBuzzNative.hb_buffer_destroy(handle);
            handle = 0;
        }
    }
}

/// <summary>An interned HarfBuzz language (hb_language_t); the handle is process-global and not freed.</summary>
internal sealed class Language
{
    private readonly nint handle;

    public Language(string language)
    {
        handle = HarfBuzzNative.hb_language_from_string(language, -1);
    }

    public nint Handle => handle;
}
