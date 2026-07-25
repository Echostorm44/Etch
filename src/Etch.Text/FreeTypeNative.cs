using System;
using System.Runtime.InteropServices;

// The interop structs below are cast over native FreeType memory and never assigned
// from managed code, so the "field is never assigned" / "never used" warnings are expected.
#pragma warning disable CS0649
#pragma warning disable IDE0044
// CA1712: interop enum members keep their native FT_* names (match FreeType docs and call sites).
#pragma warning disable CA1712
// CA5392: the native library is resolved through the shared NativeLibraryResolver
// (SetDllImportResolver, absolute-path probing) instead of DefaultDllImportSearchPaths.
#pragma warning disable CA5392

namespace Etch.Text;

/// <summary>
/// In-house FreeType interop. Replaces the former third-party managed binding package; the native
/// library load path is owned by <see cref="NativeLibraryResolver"/> (a single
/// <see cref="NativeLibrary.SetDllImportResolver"/> shared with HarfBuzz), which resolves
/// <c>freetype</c> deterministically from the app base dir / <c>runtimes/&lt;rid&gt;/native</c> —
/// single-file / self-extract / AOT friendly.
///
/// The struct layouts mirror the exact fields the shipped <c>freetype.dll</c> uses (native
/// <c>long</c>-typed fields are <see cref="nint"/>, matching that binary on 64-bit); only the
/// subset of the API Etch.Text actually calls is bound.
/// </summary>
internal static partial class FreeTypeNative
{
    private const string LibraryName = "freetype";

    // ---- Load flags (FT_LOAD_*) ----
    internal const int FT_LOAD_DEFAULT = 0x0;
    internal const int FT_LOAD_NO_HINTING = 0x2;
    internal const int FT_LOAD_TARGET_NORMAL = 0x0;    // FT_LOAD_TARGET_(FT_RENDER_MODE_NORMAL)
    internal const int FT_LOAD_TARGET_LIGHT = 0x10000; // FT_LOAD_TARGET_(FT_RENDER_MODE_LIGHT)

    // ---- Functions (subset Etch.Text uses) ----

    [LibraryImport(LibraryName)]
    internal static partial FT_Error FT_Init_FreeType(out nint alibrary);

    [LibraryImport(LibraryName)]
    internal static partial FT_Error FT_New_Memory_Face(nint library, nint fileBase, nint fileSize, nint faceIndex, out nint aface);

    [LibraryImport(LibraryName)]
    internal static partial FT_Error FT_Set_Char_Size(nint face, nint charWidth, nint charHeight, uint horzResolution, uint vertResolution);

    [LibraryImport(LibraryName)]
    internal static partial FT_Error FT_Load_Glyph(nint face, uint glyphIndex, int loadFlags);

    [LibraryImport(LibraryName)]
    internal static partial FT_Error FT_Render_Glyph(nint slot, FT_Render_Mode renderMode);

    [LibraryImport(LibraryName)]
    internal static partial void FT_Set_Transform(nint face, nint matrix, nint delta);

    [LibraryImport(LibraryName)]
    internal static partial FT_Error FT_Done_Face(nint face);
}

// ---- Enums ----

internal enum FT_Error
{
    FT_Err_Ok = 0,
}

internal enum FT_Render_Mode
{
    FT_RENDER_MODE_NORMAL = 0,
    FT_RENDER_MODE_LIGHT = 1,
    FT_RENDER_MODE_MONO = 2,
    FT_RENDER_MODE_LCD = 3,
    FT_RENDER_MODE_LCD_V = 4,
}

internal enum FT_Pixel_Mode
{
    FT_PIXEL_MODE_NONE = 0,
    FT_PIXEL_MODE_MONO = 1,
    FT_PIXEL_MODE_GRAY = 2,
    FT_PIXEL_MODE_GRAY2 = 3,
    FT_PIXEL_MODE_GRAY4 = 4,
    FT_PIXEL_MODE_LCD = 5,
    FT_PIXEL_MODE_LCD_V = 6,
    FT_PIXEL_MODE_BGRA = 7,
}

// ---- Structs (layout mirrors the shipped freetype.dll; native long-typed fields are nint) ----

[StructLayout(LayoutKind.Sequential)]
internal struct FT_Matrix
{
    public nint xx;
    public nint xy;
    public nint yx;
    public nint yy;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_Vector
{
    public nint x;
    public nint y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_Generic
{
    public nint data;
    public nint finalizer;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_Glyph_Metrics
{
    public nint width;
    public nint height;
    public nint horiBearingX;
    public nint horiBearingY;
    public nint horiAdvance;
    public nint vertBearingX;
    public nint vertBearingY;
    public nint vertAdvance;
}

[StructLayout(LayoutKind.Sequential)]
internal struct FT_Bitmap
{
    public uint rows;
    public uint width;
    public int pitch;
    public nint buffer;
    public ushort num_grays;
    public byte pixel_mode;
    public byte palette_mode;
    public nint palette;
}

/// <summary>
/// Partial mirror of <c>FT_FaceRec</c> up to and including <c>glyph</c> — the only field Etch.Text
/// reads from the face record. Fields before it must be present so <c>glyph</c> lands at the native
/// offset; nothing after it is needed.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_FaceRec
{
    public nint num_faces;
    public nint face_index;
    public nint face_flags;
    public nint style_flags;
    public nint num_glyphs;
    public nint family_name;
    public nint style_name;
    public int num_fixed_sizes;
    public nint available_sizes;
    public int num_charmaps;
    public nint charmaps;
    public FT_Generic generic;
    public nint bbox_xMin;
    public nint bbox_yMin;
    public nint bbox_xMax;
    public nint bbox_yMax;
    public ushort units_per_EM;
    public short ascender;
    public short descender;
    public short height;
    public short max_advance_width;
    public short max_advance_height;
    public short underline_position;
    public short underline_thickness;
    public FT_GlyphSlotRec* glyph;
}

/// <summary>
/// Partial mirror of <c>FT_GlyphSlotRec</c> up to and including <c>bitmap_top</c> — the fields
/// Etch.Text reads (<c>bitmap</c>, <c>bitmap_left</c>, <c>bitmap_top</c>). Fields before them must
/// be present so the offsets match; nothing after is needed.
/// </summary>
[StructLayout(LayoutKind.Sequential)]
internal unsafe struct FT_GlyphSlotRec
{
    public nint library;
    public nint face;
    public nint next;
    public uint reserved;
    public FT_Generic generic;
    public FT_Glyph_Metrics metrics;
    public nint linearHoriAdvance;
    public nint linearVertAdvance;
    public FT_Vector advance;
    public uint format;
    public FT_Bitmap bitmap;
    public int bitmap_left;
    public int bitmap_top;
}
