using System;
using System.Runtime.InteropServices;
using System.Threading;
using Etch.Text.HarfBuzz;

namespace Etch.Text.Shape;

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

            unsafe
            {
                FT_Error err = FreeTypeNative.FT_New_Memory_Face(
                    FreeTypeLibrary.Instance,
                    (nint)ptr,
                    fontBlob.Length,
                    0,
                    out ftFace);
                if (err == FT_Error.FT_Err_Ok)
                {
                    FreeTypeNative.FT_Set_Char_Size(ftFace, 0, (nint)(pointSize * 64), 96, 96);
                }
                else
                {
                    ftFace = 0;
                }
            }

            int id = Interlocked.Increment(ref s_nextId);
            return new FontFace(blob, face, font, unitsPerEm, pointSize, id, handle, ftFace, ownsFaceBlob: true);
        }
        catch
        {
            if (handle.HasValue)
            {
                handle.Value.Free();
            }
            if (ftFace != 0)
            {
                FreeTypeNative.FT_Done_Face(ftFace);
            }
            font?.Dispose();
            face?.Dispose();
            blob?.Dispose();
            throw;
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
        int id = Interlocked.Increment(ref s_nextId);
        var newVariant = new FontFace(_blob, _face, font, _unitsPerEm, _pointSize, id, null, 0, ownsFaceBlob: false);

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
            FreeTypeNative.FT_Done_Face(_ftFace);
        }
        if (_dataHandle.HasValue)
        {
            _dataHandle.Value.Free();
        }
    }
}
