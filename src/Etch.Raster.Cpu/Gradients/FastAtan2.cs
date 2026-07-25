using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu.Gradients;

public static class FastAtan2
{
    private const float Pi = MathF.PI;
    private const float TwoPi = 2.0f * Pi;
    private const float PiOverTwo = Pi / 2.0f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Atan2(float y, float x)
    {
        if (x == 0.0f)
        {
            if (y > 0.0f) return PiOverTwo;
            if (y < 0.0f) return -PiOverTwo;
            return 0.0f;
        }

        float atan;
        float z = y / x;

        if (MathF.Abs(z) < 1.0f)
        {
            atan = z / (1.0f + 0.28f * z * z);
            if (x < 0.0f)
            {
                if (y < 0.0f) return atan - Pi;
                return atan + Pi;
            }
        }
        else
        {
            atan = PiOverTwo - z / (z * z + 0.28f);
            if (y < 0.0f) return atan - Pi;
        }

        return atan;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float AngleFromZeroToTwoPi(float y, float x)
    {
        float angle = Atan2(y, x);
        if (angle < 0.0f)
            return angle + TwoPi;
        return angle;
    }
}
