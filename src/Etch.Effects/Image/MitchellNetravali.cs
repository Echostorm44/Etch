namespace Etch.Effects.Image;

public static class MitchellNetravali
{
    public const float B = 1f / 3f;
    public const float C = 1f / 3f;

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public static float Weight(float t)
    {
        float absT = t < 0 ? -t : t;

        if (absT < 1f)
        {
            float t2 = absT * absT;
            float t3 = t2 * absT;
            return (12f - 9f * B - 6f * C) * t3 + (-18f + 12f * B + 6f * C) * t2 + (6f - 2f * B);
        }

        if (absT < 2f)
        {
            float t2 = absT * absT;
            float t3 = t2 * absT;
            return (-B - 6f * C) * t3 + (6f * B + 30f * C) * t2 + (-12f * B - 48f * C) * absT + (8f * B + 24f * C);
        }

        return 0f;
    }

    public static float Weight2D(float x, float y)
    {
        return Weight(x) * Weight(y);
    }
}
