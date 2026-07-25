using System;
using System.Runtime.InteropServices;

namespace Etch.Text.Atlas;

[StructLayout(LayoutKind.Sequential, Size = 12)]
public readonly struct GlyphCacheKey : IEquatable<GlyphCacheKey>
{
    public static int SizeOf => 12;

    public readonly int FaceId;
    public readonly ushort SizeQuantUnits;
    public readonly ushort GlyphId;
    public readonly byte SubpixelX;
    // Trailing bytes (9 → 12) are alignment padding for the 4-byte FaceId.

    public GlyphCacheKey(int faceId, ushort sizeQuantUnits, ushort glyphId, byte subpixelX)
    {
        FaceId = faceId;
        SizeQuantUnits = sizeQuantUnits;
        GlyphId = glyphId;
        SubpixelX = subpixelX;
    }

    public static GlyphCacheKey FromSizeAndSubpixel(float pointSize, int faceId, ushort glyphId, byte subpixelX)
    {
        int quantUnits = (int)System.Math.Round(pointSize * 64f, MidpointRounding.ToEven);
        return new GlyphCacheKey(faceId, (ushort)quantUnits, glyphId, subpixelX);
    }

    public readonly bool Equals(GlyphCacheKey other)
    {
        return FaceId == other.FaceId
            && SizeQuantUnits == other.SizeQuantUnits
            && GlyphId == other.GlyphId
            && SubpixelX == other.SubpixelX;
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
        return hash;
    }

    public static bool operator ==(GlyphCacheKey left, GlyphCacheKey right) => left.Equals(right);
    public static bool operator !=(GlyphCacheKey left, GlyphCacheKey right) => !left.Equals(right);
}
