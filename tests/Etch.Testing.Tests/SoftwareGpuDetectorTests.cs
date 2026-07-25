using System;
using Etch.Ci;
using TUnit;

namespace Etch.Testing.Tests;

internal sealed class SoftwareGpuDetectorTests
{
    // The expected driver depends on BOTH VK_ICD_FILENAMES (a specific software ICD like
    // Lavapipe/SwiftShader) and the ETCH_SOFTWARE_GPU flag — the CI software-GPU jobs set
    // both, so derive the expectation from the actual environment rather than assuming a
    // clean one.
    private static SoftwareGpuDriver ExpectedDriver()
    {
        string vkIcd = Environment.GetEnvironmentVariable("VK_ICD_FILENAMES") ?? "";
        if (vkIcd.Contains("lvp", StringComparison.OrdinalIgnoreCase))
        {
            return SoftwareGpuDriver.Lavapipe;
        }
        if (vkIcd.Contains("swiftshader", StringComparison.OrdinalIgnoreCase))
        {
            return SoftwareGpuDriver.SwiftShader;
        }
        return Environment.GetEnvironmentVariable("ETCH_SOFTWARE_GPU") == "1"
            ? SoftwareGpuDriver.Unknown
            : SoftwareGpuDriver.None;
    }

    private static string ExpectedDriverName() => ExpectedDriver() switch
    {
        SoftwareGpuDriver.Lavapipe => "Lavapipe",
        SoftwareGpuDriver.SwiftShader => "SwiftShader",
        SoftwareGpuDriver.Unknown => "UnknownSoftware",
        _ => "hardware",
    };

    [Test]
    public async Task DetectDriver_MatchesEnvironment()
    {
        await Assert.That(SoftwareGpuDetector.DetectDriver()).IsEqualTo(ExpectedDriver());
    }

    [Test]
    public async Task DriverName_MatchesEnvironment()
    {
        await Assert.That(SoftwareGpuDetector.DriverName()).IsEqualTo(ExpectedDriverName());
    }

    [Test]
    public async Task AssertDriver_Mismatch_Throws()
    {
        try
        {
            SoftwareGpuDetector.AssertDriver(SoftwareGpuDriver.Lavapipe);
            // If no exception was thrown, we must actually be on Lavapipe.
            await Assert.That(SoftwareGpuDetector.DetectDriver()).IsEqualTo(SoftwareGpuDriver.Lavapipe);
        }
        catch (InvalidOperationException)
        {
            // Expected when not running on Lavapipe.
            await Assert.That(SoftwareGpuDetector.DetectDriver()).IsNotEqualTo(SoftwareGpuDriver.Lavapipe);
        }
    }

    [Test]
    public async Task IsSoftwareGpu_MatchesDetectDriver()
    {
        var isSoftware = SoftwareGpuDetector.IsSoftwareGpu;
        var driver = SoftwareGpuDetector.DetectDriver();

        await Assert.That(isSoftware).IsEqualTo(driver != SoftwareGpuDriver.None);
    }
}
