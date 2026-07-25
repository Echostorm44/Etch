namespace Etch.Text;

/// <summary>
/// Quantizes a fractional pixel X offset to a small finite set of subpixel
/// positions. This bounds the glyph-cache key space while preserving smooth
/// horizontal spacing.
/// </summary>
public enum SubpixelQuant
{
    None = 0,
    Quarter = 1,
    Eighth = 2,
    Sixteenth = 3,
}

public static class QuantizedSubpixel
{
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static int StepCount(SubpixelQuant q) => q switch
    {
        SubpixelQuant.Quarter => 4,
        SubpixelQuant.Eighth => 8,
        SubpixelQuant.Sixteenth => 16,
        _ => 1,
    };

    /// <summary>
    /// Returns the quantized subpixel bucket for <paramref name="x"/>.
    /// For <see cref="SubpixelQuant.Quarter"/> the result is 0..3;
    /// for <see cref="SubpixelQuant.Eighth"/> it is 0..7.
    /// Negative values wrap correctly (e.g. –0.3 → same bucket as 0.7).
    /// </summary>
    public static byte QuantizeX(float x, SubpixelQuant q)
    {
        float frac = x - (float)Math.Floor(x);
        int steps = StepCount(q);
        // Banker's rounding: halves round to the nearest even integer.
        int bucket = (int)Math.Round(frac * steps, MidpointRounding.ToEven);
        // Clamp to handle the exact 1.0 case (frac == 0 after floor, but
        // rounding can produce 'steps' when frac is extremely close to 1).
        if (bucket >= steps) bucket = 0;
        return (byte)bucket;
    }

    /// <summary>
    /// Converts a quantized bucket back to a pixel offset.
    /// </summary>
    public static float Dequantize(byte bucket, SubpixelQuant q)
    {
        int steps = StepCount(q);
        return bucket / (float)steps;
    }
}
