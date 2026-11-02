using System;
using System.Runtime.InteropServices;
using System.Threading;
using Etch.Text.HarfBuzz;

namespace Etch.Text.Shape;

/// <summary>
/// A design-axis setting of a variable font: an OpenType axis tag (such as <c>wght</c>) and a value
/// in that axis's design units (wght: 100 thin to 900 black). Values are clamped to the font's range.
/// </summary>
public readonly record struct FontVariation(uint Tag, float Value)
{
    /// <summary>The weight axis tag, <c>wght</c>.</summary>
    public const uint WeightAxis = ('w' << 24) | ('g' << 16) | ('h' << 8) | 't';

    /// <summary>A weight setting (100 thin, 400 regular, 700 bold, 900 black).</summary>
    public static FontVariation Weight(float weight) => new(WeightAxis, weight);
}

public sealed class FontFace : IDisposable
{
    private static int s_nextId = 1;

    private readonly Blob _blob;
    private readonly Face _face;
    private readonly Font _font;
    private readonly ThreadLocal<HarfBuzz.Buffer> _buffer;
    private readonly int _unitsPerEm;
    private readonly float _pointSize;
    private readonly int _id;
    private readonly bool _ownsFaceBlob;
    private FontFace? _subpixelVariant;
    private bool _disposed;
    private FontHinting _hinting;

    // FreeType integration
    private readonly GCHandle? _dataHandle;
    private readonly nint _ftFace;
    private FontVariation[] _variations = [];

    internal event Action<FontFace>? OnDisposed;

    private FontFace(Blob blob, Face face, Font font, int unitsPerEm, float pointSize, int id, GCHandle? dataHandle, nint ftFace, bool ownsFaceBlob = true)
    {
        _blob = blob;
        _face = face;
        _font = font;
        _buffer = new ThreadLocal<HarfBuzz.Buffer>(() => new HarfBuzz.Buffer());
        _unitsPerEm = unitsPerEm;
        _pointSize = pointSize;
        _id = id;
        _ownsFaceBlob = ownsFaceBlob;
        _dataHandle = dataHandle;
        _ftFace = ftFace;
        _hinting = FontHinting.Normal;
    }

    public static FontFace Load(byte[] fontBlob, int unitsPerEm, float pointSize)
        => Load(fontBlob, unitsPerEm, pointSize, []);

    /// <summary>
    /// Loads a face at <paramref name="pointSize"/> with the design-axis settings in
    /// <paramref name="variations"/> applied to both shaping (HarfBuzz) and rasterizing
    /// (FreeType). A static font ignores them.
    /// </summary>
    public static FontFace Load(byte[] fontBlob, int unitsPerEm, float pointSize, ReadOnlySpan<FontVariation> variations)
    {
        GCHandle? handle = null;
        nint ftFace = 0;
        Blob? blob = null;
        Face? face = null;
        Font? font = null;

        try
        {
            handle = GCHandle.Alloc(fontBlob, GCHandleType.Pinned);
            IntPtr ptr = handle.Value.AddrOfPinnedObject();

            blob = new Blob(ptr, fontBlob.Length, MemoryMode.ReadOnly, null!);

            face = new Face(blob, 0);
            face.UnitsPerEm = unitsPerEm;
            face.MakeImmutable();

            font = new Font(face);
            font.SetScale((int)(pointSize * 64), (int)(pointSize * 64));
            HbAxisInfo[] axes = variations.IsEmpty ? [] : face.GetVariationAxes();
            if (axes.Length > 0)
            {
                font.SetVariations(variations);
            }

            unsafe
            {
                FT_Error err = FreeTypeLibrary.NewMemoryFace(
                    (nint)ptr,
                    fontBlob.Length,
                    0,
                    out ftFace);
                if (err == FT_Error.FT_Err_Ok)
                {
                    FreeTypeNative.FT_Set_Char_Size(ftFace, 0, (nint)(pointSize * 64), 96, 96);
                    if (axes.Length > 0)
                    {
                        SetFreeTypeVariations(ftFace, axes, variations);
                    }
                }
                else
                {
                    ftFace = 0;
                }
            }

            int id = Interlocked.Increment(ref s_nextId);
            return new FontFace(blob, face, font, unitsPerEm, pointSize, id, handle, ftFace, ownsFaceBlob: true)
            {
                _variations = axes.Length > 0 ? variations.ToArray() : [],
            };
        }
        catch
        {
            if (handle.HasValue)
            {
                handle.Value.Free();
            }
            if (ftFace != 0)
            {
                FreeTypeLibrary.DoneFace(ftFace);
            }
            font?.Dispose();
            face?.Dispose();
            blob?.Dispose();
            throw;
        }
    }

    /// <summary>
    /// FreeType takes every axis in fvar order: each requested axis gets its value clamped to the
    /// font's range, the others their defaults.
    /// </summary>
    private static unsafe void SetFreeTypeVariations(nint ftFace, HbAxisInfo[] axes, ReadOnlySpan<FontVariation> variations)
    {
        Span<nint> coords = axes.Length <= 16 ? stackalloc nint[axes.Length] : new nint[axes.Length];
        for (int i = 0; i < axes.Length; i++)
        {
            var axis = axes[i];
            float value = axis.DefaultValue;
            foreach (var variation in variations)
            {
                if (variation.Tag == axis.Tag && float.IsFinite(variation.Value))
                {
                    value = Math.Clamp(variation.Value, axis.MinValue, axis.MaxValue);
                }
            }
            coords[(int)axis.AxisIndex < axes.Length ? (int)axis.AxisIndex : i] = (nint)MathF.Round(value * 65536f);
        }
        fixed (nint* pointer = coords)
        {
            FreeTypeNative.FT_Set_Var_Design_Coordinates(ftFace, (uint)coords.Length, pointer);
        }
    }

    public static FontFace Load(ReadOnlySpan<byte> fontBlob, int unitsPerEm, float pointSize)
    {
        // Fallback for non-array spans: no FreeType face
        var blob = BlobFromBytes(fontBlob);

        var face = new Face(blob, 0);
        face.UnitsPerEm = unitsPerEm;
        face.MakeImmutable();

        var font = new Font(face);
        font.SetScale((int)(pointSize * 64), (int)(pointSize * 64));

        int id = Interlocked.Increment(ref s_nextId);
        return new FontFace(blob, face, font, unitsPerEm, pointSize, id, null, 0, ownsFaceBlob: true);
    }

    /// <summary>
    /// Returns a cached FontFace with 3× horizontal scale for subpixel rendering.
    /// Shares the immutable Face/Blob. Do not dispose the returned instance —
    /// it is owned by this FontFace and disposed along with it.
    /// </summary>
    public FontFace GetSubpixelVariant()
    {
        var variant = _subpixelVariant;
        if (variant != null)
        {
            return variant;
        }

        var font = new Font(_face);
        font.SetScale((int)(_pointSize * 64 * 3), (int)(_pointSize * 64));
        if (_variations.Length > 0)
        {
            font.SetVariations(_variations);
        }
        int id = Interlocked.Increment(ref s_nextId);
        var newVariant = new FontFace(_blob, _face, font, _unitsPerEm, _pointSize, id, null, 0, ownsFaceBlob: false)
        {
            _variations = _variations,
        };

        var existing = Interlocked.CompareExchange(ref _subpixelVariant, newVariant, null);
        if (existing != null)
        {
            newVariant.Dispose();
            return existing;
        }
        return newVariant;
    }

    private static Blob BlobFromBytes(ReadOnlySpan<byte> data)
    {
        unsafe
        {
            fixed (byte* ptr = data)
            {
                return new Blob((IntPtr)ptr, data.Length, MemoryMode.ReadOnly, null!);
            }
        }
    }

    internal Font Handle => _font;

    /// <summary>Looks up the nominal glyph id for a Unicode codepoint via the shaper font.</summary>
    public bool TryGetGlyph(uint codepoint, out uint glyphId)
    {
        return _font.TryGetGlyph(codepoint, out glyphId);
    }

    public int Id => _id;

    /// <summary>The design-axis settings applied to this face (empty for a static font or none set).</summary>
    public ReadOnlySpan<FontVariation> Variations => _variations;

    public int UnitsPerEm => _unitsPerEm;

    public float PointSize => _pointSize;

    public FontHinting Hinting
    {
        get => _hinting;
        set => _hinting = value;
    }

    internal HarfBuzz.Buffer GetBuffer()
    {
        return _buffer.Value!;
    }

    internal nint FreeTypeFace => _ftFace;

    /// <summary>The HarfBuzz face (shared with this face's subpixel variant).</summary>
    internal Face HarfBuzzFace => _face;

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        OnDisposed?.Invoke(this);
        _subpixelVariant?.Dispose();
        _buffer.Dispose();
        _font.Dispose();
        if (_ownsFaceBlob)
        {
            _face.Dispose();
            _blob.Dispose();
        }
        if (_ftFace != 0)
        {
            FreeTypeLibrary.DoneFace(_ftFace);
        }
        if (_dataHandle.HasValue)
        {
            _dataHandle.Value.Free();
        }
    }
}
