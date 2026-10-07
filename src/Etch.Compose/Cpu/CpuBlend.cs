namespace Etch.Compose.Cpu;

/// <summary>Span blends with one source colour and alpha.</summary>
internal static class CpuBlend
{
    /// <summary>
    /// <see cref="CpuShading.BlendStraight"/> of one colour over every pixel of
    /// <paramref name="span"/>. The result depends only on the destination value, and UI spans are
    /// long runs of equal pixels, so each distinct run is blended once.
    /// </summary>
    public static void BlendConstantStraight(Span<uint> span, float r, float g, float b, float a)
    {
        if (span.IsEmpty)
        {
            return;
        }
        uint lastDst = span[0];
        uint lastOut = CpuShading.BlendStraight(lastDst, r, g, b, a);
        for (int i = 0; i < span.Length; i++)
        {
            uint dst = span[i];
            if (dst != lastDst)
            {
                lastDst = dst;
                lastOut = CpuShading.BlendStraight(dst, r, g, b, a);
            }
            span[i] = lastOut;
        }
    }
}
