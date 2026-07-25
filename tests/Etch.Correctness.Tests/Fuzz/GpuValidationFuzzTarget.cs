using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using Etch.Gpu.Validation;
using Etch.Scene;
using Etch.Testing;

namespace Etch.Correctness.Tests.Fuzz;

/// <summary>
/// GPU validation-layer fuzz target (COR-009).
/// Feeds COR-008's scene grammar through the real GPU path and asserts
/// zero wgpu validation messages. Any validation failure is saved to
/// <c>crash-corpus/gpu-validation/</c>.
/// </summary>
public class GpuValidationFuzzTarget
{
    private const int RenderWidth = 8;
    private const int RenderHeight = 8;
    private const int PrBudget = 10_000;
    private const int NightlyBudget = 1_000_000;
    private const string CrashCorpusDir = "tests/Etch.Correctness.Tests/Fuzz/crash-corpus/gpu-validation";

    /// <summary>
    /// PR gate: 10 K iterations, zero validation errors.
    /// </summary>
    [Test]
#pragma warning disable CA1508 // Analyzer can't see that cache is null when constructor throws
#pragma warning disable CA2000 // Dispose ownership transferred to try-finally
    public async Task FuzzGpuValidation_PrBudget_ZeroValidationMessages()
    {
        await RunFuzzBudget(PrBudget);
    }
#pragma warning restore CA2000
#pragma warning restore CA1508

    /// <summary>
    /// Nightly gate: 1 M iterations, zero validation errors.
    /// Skipped unless <c>ETCH_FUZZ_NIGHTLY=1</c> is set.
    /// </summary>
    [Test]
#pragma warning disable CA1508
#pragma warning disable CA2000
    public async Task FuzzGpuValidation_NightlyBudget_ZeroValidationMessages()
    {
        string? nightly = Environment.GetEnvironmentVariable("ETCH_FUZZ_NIGHTLY");
        if (nightly != "1")
        {
            await Task.CompletedTask;
            return;
        }

        await RunFuzzBudget(NightlyBudget);
    }
#pragma warning restore CA2000
#pragma warning restore CA1508

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Reliability", "CA2000:DisposeObjectsBeforeLosingScope", Justification = "GpuRenderCache is disposed in the try-finally block below.")]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Microsoft.Maintainability", "CA1508:AvoidDeadConditionalCode", Justification = "Analyzer cannot see that cache is non-null when finally is reached.")]
    private static async Task RunFuzzBudget(int budget)
    {
        SceneGpuRenderer.GpuRenderCache cache;
        try
        {
            cache = new SceneGpuRenderer.GpuRenderCache(RenderWidth, RenderHeight);
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuAdapterUnavailable ||
                                         ex.Code == Etch.PanicCodes.GpuDeviceCreationFailed)
        {
            // No GPU on this machine — not a failure.
            await Task.CompletedTask;
            return;
        }

        try
        {
            int validationErrorCount = 0;
            string? reproPath = null;

            for (int seed = 0; seed < budget; seed++)
            {
                var input = GenerateInput(seed);
                var result = RenderOneGpu(cache, input);

                if (result.ValidationError)
                {
                    validationErrorCount++;
                    reproPath = SaveReproducer(input, seed);
                    // Exit on first failure with minimized reproducer (per spec).
                    break;
                }
            }

            if (validationErrorCount > 0)
            {
                throw new InvalidOperationException(
                    $"GPU validation fuzz found {validationErrorCount} validation errors. " +
                    $"Reproducer: {reproPath}");
            }
        }
        finally
        {
            cache.Dispose();
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static GpuFuzzResult RenderOneGpu(SceneGpuRenderer.GpuRenderCache cache, ReadOnlySpan<byte> input)
    {
        SceneBuffer? scene = null;

        try
        {
            scene = SceneFuzzDecoder.Decode(input);
        }
        catch (EtchException)
        {
            // Invalid scene construction is acceptable — not a GPU validation issue.
            return new GpuFuzzResult(false);
        }
        catch (Exception)
        {
            // Unexpected crash during decode — report as validation error to be safe.
            return new GpuFuzzResult(true);
        }

        try
        {
            // GpuRenderCache wires the validation callback at device creation and
            // calls ThrowIfValidationErrorsPresent after Queue.Submit, so a
            // validation error surfaces as EtchException with code GpuValidation.
            _ = cache.Render(scene);
            return new GpuFuzzResult(false);
        }
        catch (EtchException ex) when (ex.Code == Etch.PanicCodes.GpuValidation)
        {
            return new GpuFuzzResult(true);
        }
        catch (EtchException)
        {
            // Other Etch panics (e.g. invalid scene during render) are acceptable.
            return new GpuFuzzResult(false);
        }
        catch (Exception)
        {
            return new GpuFuzzResult(true);
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

    private static string SaveReproducer(byte[] input, int seed)
    {
        string hash = ComputeHash(input);
        string fileName = $"validation-{hash}-{seed}.bin";

        string dir = Path.Combine(AppContext.BaseDirectory, CrashCorpusDir);
        Directory.CreateDirectory(dir);

        string path = Path.Combine(dir, fileName);
        File.WriteAllBytes(path, input);
        return path;
    }

    private static string ComputeHash(byte[] input)
    {
        Span<byte> hash = stackalloc byte[32];
        SHA256.HashData(input, hash);
        return Convert.ToHexString(hash.Slice(0, 8));
    }

    private readonly struct GpuFuzzResult
    {
        public readonly bool ValidationError;
        public GpuFuzzResult(bool validationError) => ValidationError = validationError;
    }
}
