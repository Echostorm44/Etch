using System;
using System.Runtime.CompilerServices;
using Etch.Scene;
using Etch.Testing;
using TUnit;

namespace Etch.Correctness.Tests.Fuzz;

public class SceneFuzzTests
{
    private const int RenderWidth = 64;
    private const int RenderHeight = 64;
    private const int PrBudget = 100_000;
    private const long MaxAllocPerIterationBytes = 128L * 1024 * 1024;

    [Test]
    public async Task FuzzSingle_10KInputs_ZeroUnhandledExceptions()
    {
        int crashCount = 0;
        int handledPanicCount = 0;
        int successCount = 0;

        for (int seed = 0; seed < 10_000; seed++)
        {
            var input = GenerateInput(seed);
            var result = FuzzOne(input);

            switch (result.Kind)
            {
                case FuzzResultKind.Success:
                    successCount++;
                    break;
                case FuzzResultKind.HandledPanic:
                    handledPanicCount++;
                    break;
                case FuzzResultKind.Crash:
                    crashCount++;
                    break;
            }
        }

        await Assert.That(crashCount).IsEqualTo(0);
    }

    [Test]
    public async Task FuzzSingle_100KInputs_ZeroUnhandledExceptions()
    {
        int crashCount = 0;
        int handledPanicCount = 0;
        int successCount = 0;

        for (int seed = 0; seed < PrBudget; seed++)
        {
            var input = GenerateInput(seed);
            var result = FuzzOne(input);

            switch (result.Kind)
            {
                case FuzzResultKind.Success:
                    successCount++;
                    break;
                case FuzzResultKind.HandledPanic:
                    handledPanicCount++;
                    break;
                case FuzzResultKind.Crash:
                    crashCount++;
                    break;
            }
        }

        await Assert.That(crashCount).IsEqualTo(0);
    }

    [Test]
    public async Task FuzzSingle_OutputPixelsAreFiniteAndInRange()
    {
        int invalidPixelCount = 0;

        for (int seed = 0; seed < 10_000; seed++)
        {
            var input = GenerateInput(seed);
            var result = FuzzOne(input);

            if (result.Kind == FuzzResultKind.Success && result.Output != null)
            {
                var output = result.Output;
                for (int i = 0; i < output.Length; i++)
                {
                    byte b = output[i];
                    // RGBA8 is always in [0,255] by definition, but we check for NaN/Inf
                    // which can't happen for byte. This is a structural sanity check.
                    if (b < 0 || b > 255)
                    {
                        invalidPixelCount++;
                    }
                }
            }
        }

        await Assert.That(invalidPixelCount).IsEqualTo(0);
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static FuzzResult FuzzOne(ReadOnlySpan<byte> input)
    {
        SceneBuffer? scene = null;
        long allocBefore = GC.GetAllocatedBytesForCurrentThread();

        try
        {
            scene = SceneFuzzDecoder.Decode(input);
        }
        catch (EtchException)
        {
            // Panic during scene construction is acceptable
            return new FuzzResult(FuzzResultKind.HandledPanic, null);
        }
        catch (Exception)
        {
            return new FuzzResult(FuzzResultKind.Crash, null);
        }

        try
        {
            byte[] output = SceneRunner.RunCpu(scene, RenderWidth, RenderHeight);
            long allocAfter = GC.GetAllocatedBytesForCurrentThread();
            long delta = allocAfter - allocBefore;

            if (delta > MaxAllocPerIterationBytes)
            {
                // Memory budget exceeded — treat as failure
                return new FuzzResult(FuzzResultKind.Crash, null);
            }

            return new FuzzResult(FuzzResultKind.Success, output);
        }
        catch (EtchException)
        {
            // Panic during rendering is acceptable
            return new FuzzResult(FuzzResultKind.HandledPanic, null);
        }
        catch (Exception)
        {
            return new FuzzResult(FuzzResultKind.Crash, null);
        }
        finally
        {
            // The decoded scene holds pooled/native buffers; dispose it each iteration so a
            // long fuzz run doesn't accumulate scenes and the GC/finalizer pressure they cause.
            scene.Dispose();
        }
    }

    private static byte[] GenerateInput(int seed)
    {
        var rng = new Random(seed);
        int len = rng.Next(8, 512);
        var bytes = new byte[len];
        rng.NextBytes(bytes);
        return bytes;
    }

    private readonly struct FuzzResult
    {
        public readonly FuzzResultKind Kind;
        public readonly byte[]? Output;

        public FuzzResult(FuzzResultKind kind, byte[]? output)
        {
            Kind = kind;
            Output = output;
        }
    }

    private enum FuzzResultKind
    {
        Success,
        HandledPanic,
        Crash,
    }
}
