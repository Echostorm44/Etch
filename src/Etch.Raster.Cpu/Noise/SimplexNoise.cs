using System;
using System.Runtime.CompilerServices;

namespace Etch.Raster.Cpu.Noise;

public static class SimplexNoise
{
    internal static readonly float[] Grad3 =
    {
        1f, 1f, 0f, -1f, 1f, 0f, 1f, -1f, 0f, -1f, -1f, 0f,
        1f, 0f, 1f, -1f, 0f, 1f, 1f, 0f, -1f, -1f, 0f, -1f,
        0f, 1f, 1f, 0f, -1f, 1f, 0f, 1f, -1f, 0f, -1f, -1f,
    };

    private static readonly float F2 = 0.5f * (MathF.Sqrt(3f) - 1f);
    private static readonly float G2 = (3f - MathF.Sqrt(3f)) / 6f;

    public static byte[] CreatePermutation(uint seed)
    {
        var p = new byte[512];
        var source = new byte[256];
        for (int i = 0; i < 256; i++)
            source[i] = (byte)i;

        uint s = seed;
        for (int i = 255; i > 0; i--)
        {
            s = s * 1664525u + 1013904223u;
            int j = (int)(s % (uint)(i + 1));
            (source[i], source[j]) = (source[j], source[i]);
        }

        for (int i = 0; i < 512; i++)
            p[i] = (byte)(source[i & 255] % 12);

        return p;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Noise2D(float x, float y, byte[] perm)
    {
        ArgumentNullException.ThrowIfNull(perm);
        float s = (x + y) * F2;
        int i = FloorF(x + s);
        int j = FloorF(y + s);
        float t = (i + j) * G2;
        float X0 = i - t;
        float Y0 = j - t;
        float x0 = x - X0;
        float y0 = y - Y0;

        int i1, j1;
        if (x0 > y0) { i1 = 1; j1 = 0; }
        else { i1 = 0; j1 = 1; }

        float x1 = x0 - i1 + G2;
        float y1 = y0 - j1 + G2;
        float x2 = x0 - 1f + 2f * G2;
        float y2 = y0 - 1f + 2f * G2;

        int ii = i & 255;
        int jj = j & 255;

        float n0 = CornerContrib(x0, y0, perm[ii + perm[jj]]);
        float n1 = CornerContrib(x1, y1, perm[ii + i1 + perm[jj + j1]]);
        float n2 = CornerContrib(x2, y2, perm[ii + 1 + perm[jj + 1]]);

        return 70f * (n0 + n1 + n2);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float CornerContrib(float x, float y, int gradIndex)
    {
        float t = 0.5f - x * x - y * y;
        if (t < 0f)
            return 0f;
        t *= t;
        return t * t * DotGradient(gradIndex, x, y);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float DotGradient(int gradIndex, float x, float y)
    {
        int baseIdx = gradIndex * 3;
        return Grad3[baseIdx] * x + Grad3[baseIdx + 1] * y;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int FloorF(float v)
    {
        int iv = (int)v;
        return v < iv ? iv - 1 : iv;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Fbm2D(float x, float y, int octaves, float persistence, byte[] perm)
    {
        float value = 0f;
        float amplitude = 1f;
        float frequency = 1f;
        float maxValue = 0f;

        for (int i = 0; i < octaves; i++)
        {
            value += amplitude * Noise2D(x * frequency, y * frequency, perm);
            maxValue += amplitude;
            amplitude *= persistence;
            frequency *= 2f;
        }

        return value / maxValue;
    }
}
