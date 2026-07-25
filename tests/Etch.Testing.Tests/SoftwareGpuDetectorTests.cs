using System;
using Etch.Ci;
using TUnit;

namespace Etch.Testing.Tests;

internal sealed class SoftwareGpuDetectorTests
{
    [Test]
    public async Task DetectDriver_NoEnv_ReturnsNone()
    {
        // Note: this test assumes VK_ICD_FILENAMES is not set in the test environment.
        // If it is set, the test will see the real value.
        var driver = SoftwareGpuDetector.DetectDriver();
        var expected = Environment.GetEnvironmentVariable("ETCH_SOFTWARE_GPU") == "1"
            ? SoftwareGpuDriver.Unknown
            : SoftwareGpuDriver.None;

        await Assert.That(driver).IsEqualTo(expected);
    }

    [Test]
    public async Task DriverName_NoEnv_ReturnsHardware()
    {
        var name = SoftwareGpuDetector.DriverName();

        if (Environment.GetEnvironmentVariable("ETCH_SOFTWARE_GPU") == "1")
        {
            await Assert.That(name).IsEqualTo("UnknownSoftware");
        }
        else
        {
            await Assert.That(name).IsEqualTo("hardware");
        }
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
