using System;
using System.Buffers;
using System.Runtime.CompilerServices;

namespace Etch.Testing;

public readonly struct DiffTolerance
{
    public float MeanMaxChannel { get; init; }
    public float P95MaxChannel { get; init; }
    public float AbsMaxChannel { get; init; }

    public DiffTolerance(float meanMaxChannel, float p95MaxChannel, float absMaxChannel)
    {
        MeanMaxChannel = meanMaxChannel;
        P95MaxChannel = p95MaxChannel;
        AbsMaxChannel = absMaxChannel;
    }
}

public readonly struct DiffResult
{
    public bool Pass { get; init; }
    public float MeanError { get; init; }
    public float P95Error { get; init; }
    public float MaxError { get; init; }
    public int PixelCount { get; init; }
    public int FailingPixelCount { get; init; }

    public DiffResult(bool pass, float meanError, float p95Error, float maxError, int pixelCount, int failingPixelCount)
    {
        Pass = pass;
        MeanError = meanError;
        P95Error = p95Error;
        MaxError = maxError;
        PixelCount = pixelCount;
        FailingPixelCount = failingPixelCount;
    }
}

public static class PixelDiff
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static DiffResult Compare(
        ReadOnlySpan<byte> actual,
        ReadOnlySpan<byte> reference,
        int w,
        int h,
        DiffTolerance tolerance)
    {
        if (actual.Length != reference.Length || actual.Length != w * h * 4)
        {
            return default;
        }

        int pixelCount = w * h;
        long totalError = 0;
        float maxError = 0;
        int failingPixels = 0;

        int totalChannels = pixelCount * 3;
        int p95Index = (int)(totalChannels * 0.95);

        var histogram = ArrayPool<int>.Shared.Rent(256 * 3);
        Array.Clear(histogram, 0, 256 * 3);

        for (int i = 0; i < actual.Length; i += 4)
        {
            int errR = Math.Abs(actual[i] - reference[i]);
            int errG = Math.Abs(actual[i + 1] - reference[i + 1]);
            int errB = Math.Abs(actual[i + 2] - reference[i + 2]);

            totalError += errR + errG + errB;

            int pixelMax = errR >= errG ? errR : errG;
            pixelMax = pixelMax >= errB ? pixelMax : errB;

            if (pixelMax > maxError)
                maxError = pixelMax;

            if (pixelMax > tolerance.AbsMaxChannel)
                failingPixels++;

            histogram[errR]++;
            histogram[256 + errG]++;
            histogram[512 + errB]++;
        }

        int cumulative = 0;
        float p95Error = 0;
        for (int err = 255; err >= 0; err--)
        {
            cumulative += histogram[err] + histogram[256 + err] + histogram[512 + err];
            if (cumulative >= p95Index)
            {
                p95Error = err;
                break;
            }
        }

        float meanError = (float)totalError / totalChannels;

        bool pass = meanError <= tolerance.MeanMaxChannel &&
                    p95Error <= tolerance.P95MaxChannel &&
                    maxError <= tolerance.AbsMaxChannel;

        ArrayPool<int>.Shared.Return(histogram);
        return new DiffResult(pass, meanError, p95Error, maxError, pixelCount, failingPixels);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeMeanError(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> reference)
    {
        if (actual.Length != reference.Length)
            return float.MaxValue;

        long totalError = 0;
        int count = actual.Length / 4;

        for (int i = 0; i < actual.Length; i += 4)
        {
            totalError += Math.Abs(actual[i] - reference[i]);
            totalError += Math.Abs(actual[i + 1] - reference[i + 1]);
            totalError += Math.Abs(actual[i + 2] - reference[i + 2]);
        }

        return (float)totalError / (count * 3);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float ComputeMaxError(ReadOnlySpan<byte> actual, ReadOnlySpan<byte> reference)
    {
        if (actual.Length != reference.Length)
            return float.MaxValue;

        float maxError = 0;

        for (int i = 0; i < actual.Length; i += 4)
        {
            int errR = Math.Abs(actual[i] - reference[i]);
            int errG = Math.Abs(actual[i + 1] - reference[i + 1]);
            int errB = Math.Abs(actual[i + 2] - reference[i + 2]);
            int pixelMax = errR >= errG ? errR : errG;
            pixelMax = pixelMax >= errB ? pixelMax : errB;
            if (pixelMax > maxError)
                maxError = pixelMax;
        }

        return maxError;
    }
}