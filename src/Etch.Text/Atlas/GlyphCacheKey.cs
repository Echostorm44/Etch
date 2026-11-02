using System;
using System.Runtime.InteropServices;

namespace Etch.Text.Atlas;

[StructLayout(LayoutKind.Sequential, Size = 12)]
public readonly struct GlyphCacheKey : IEquatable<GlyphCacheKey>
{
    public static int SizeOf => 12;

    /// <summary>The widest outline a key can hold, in pixels (255 quarter pixels).</summary>
    public const float MaxOutlineWidth = byte.MaxValue / 4f;

    public readonly int FaceId;
    public readonly ushort SizeQuantUnits;
    public readonly ushort GlyphId;
    public readonly byte SubpixelX;

    /// <summary>
    /// The outline width in quarter pixels: 0 for the glyph itself, otherwise the glyph's outline
    /// (see <see cref="Rasterize.GlyphRasterizer.Rasterize(Shape.FontFace, ushort, float, Span{byte}, out int, out int, out int, out int, float)"/>).
    /// </summary>
    public readonly byte OutlineQuarterPixels;
    // Trailing bytes (10 → 12) are alignment padding for the 4-byte FaceId.

    public GlyphCacheKey(int faceId, ushort sizeQuantUnits, ushort glyphId, byte subpixelX, byte outlineQuarterPixels = 0)
    {
        FaceId = faceId;
        SizeQuantUnits = sizeQuantUnits;
        GlyphId = glyphId;
        SubpixelX = subpixelX;
        OutlineQuarterPixels = outlineQuarterPixels;
    }

    /// <summary>The outline width this key's bitmap is rasterized with, in pixels.</summary>
    public float OutlineWidth => OutlineQuarterPixels / 4f;

    public static GlyphCacheKey FromSizeAndSubpixel(float pointSize, int faceId, ushort glyphId, byte subpixelX, float outlineWidth = 0f)
    {
        int quantUnits = (int)System.Math.Round(pointSize * 64f, MidpointRounding.ToEven);
        return new GlyphCacheKey(faceId, (ushort)quantUnits, glyphId, subpixelX, QuantizeOutlineWidth(outlineWidth));
    }

    /// <summary>
    /// An outline width in quarter pixels, clamped to [0, <see cref="MaxOutlineWidth"/>]. A positive
    /// width never quantizes to 0, which would mean the glyph itself.
    /// </summary>
    public static byte QuantizeOutlineWidth(float outlineWidth)
    {
        if (!(outlineWidth > 0f))
        {
            return 0;
        }
        float quarters = MathF.Round(Math.Min(outlineWidth, MaxOutlineWidth) * 4f, MidpointRounding.ToEven);
        return (byte)Math.Max(1f, quarters);
    }

    public readonly bool Equals(GlyphCacheKey other)
    {
        return FaceId == other.FaceId
            && SizeQuantUnits == other.SizeQuantUnits
            && GlyphId == other.GlyphId
            && SubpixelX == other.SubpixelX
            && OutlineQuarterPixels == other.OutlineQuarterPixels;
    }

    public override readonly bool Equals(object? obj)
    {
        return obj is GlyphCacheKey other && Equals(other);
    }

    public override readonly int GetHashCode()
    {
        int hash = FaceId;
        hash = hash * 31 + SizeQuantUnits;
        hash = hash * 31 + GlyphId;
        hash = hash * 31 + SubpixelX;
        hash = hash * 31 + OutlineQuarterPixels;
        return hash;
    }

    public static bool operator ==(GlyphCacheKey left, GlyphCacheKey right) => left.Equals(right);
    public static bool operator !=(GlyphCacheKey left, GlyphCacheKey right) => !left.Equals(right);
}
