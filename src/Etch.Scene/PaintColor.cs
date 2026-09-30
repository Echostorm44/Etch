using System;

namespace Etch.Scene;

/// <summary>
/// The colour encoding of <see cref="Paint"/> ARGB values and gradient stops: sRGB-encoded
/// channels with straight (not premultiplied) alpha — the encoding of CSS colours and image files.
/// 8-bit sRGB spends its levels where the eye can tell them apart; 8-bit linear does not (it has
/// one step between sRGB 0 and 13, so dark colours snapped). Renderers blend in linear light:
/// decode with <see cref="ToLinear"/> first.
/// </summary>
public static class PaintColor
{
    private static readonly float[] DecodeLut = BuildDecodeLut();

    /// <summary>Packs linear-light channels (straight alpha, 0..1) as sRGB-encoded ARGB.</summary>
    public static uint FromLinear(float r, float g, float b, float a)
    {
        return ((uint)ToByte(a) << 24) | ((uint)Encode(r) << 16) | ((uint)Encode(g) << 8) | Encode(b);
    }

    /// <summary>Unpacks sRGB-encoded ARGB into linear-light channels with straight alpha (0..1).</summary>
    public static (float R, float G, float B, float A) ToLinear(uint argb)
    {
        return (
            DecodeLut[(argb >> 16) & 0xFF],
            DecodeLut[(argb >> 8) & 0xFF],
            DecodeLut[argb & 0xFF],
            ((argb >> 24) & 0xFF) * (1f / 255f));
    }

    /// <summary>Decodes one sRGB-encoded channel byte to linear light.</summary>
    public static float DecodeChannel(byte srgb) => DecodeLut[srgb];

    private static byte Encode(float linear)
    {
        float c = Math.Clamp(linear, 0f, 1f);
        float s = c <= 0.0031308f ? c * 12.92f : 1.055f * MathF.Pow(c, 1f / 2.4f) - 0.055f;
        return ToByte(s);
    }

    private static byte ToByte(float unit) => (byte)(Math.Clamp(unit, 0f, 1f) * 255f + 0.5f);

    private static float[] BuildDecodeLut()
    {
        var lut = new float[256];
        for (int i = 0; i < 256; i++)
        {
            float c = i / 255f;
            lut[i] = c <= 0.04045f ? c / 12.92f : MathF.Pow((c + 0.055f) / 1.055f, 2.4f);
        }
        return lut;
    }
}
