using System;
using System.IO;

namespace Etch.Ci;

/// <summary>
/// Detects whether the current process is running on a software (CPU) GPU
/// implementation such as Lavapipe (Mesa) or SwiftShader (Google).
/// </summary>
public static class SoftwareGpuDetector
{
    private static readonly string? s_vkIcd = Environment.GetEnvironmentVariable("VK_ICD_FILENAMES");
    private static readonly bool s_softwareFlag = Environment.GetEnvironmentVariable("ETCH_SOFTWARE_GPU") == "1";

    /// <summary>
    /// Returns true when a software GPU is detected via environment variables.
    /// </summary>
    public static bool IsSoftwareGpu => DetectDriver() != SoftwareGpuDriver.None;

    /// <summary>
    /// Returns the detected software GPU driver, or <see cref="SoftwareGpuDriver.None"/>
    /// if no software GPU is detected.
    /// </summary>
    public static SoftwareGpuDriver DetectDriver()
    {
        if (string.IsNullOrEmpty(s_vkIcd))
        {
            return s_softwareFlag ? SoftwareGpuDriver.Unknown : SoftwareGpuDriver.None;
        }

        string icdUpper = s_vkIcd.ToUpperInvariant();

        if (icdUpper.Contains("LVP_ICD", StringComparison.Ordinal) || icdUpper.Contains("LAVAPIPE", StringComparison.Ordinal))
        {
            return SoftwareGpuDriver.Lavapipe;
        }

        if (icdUpper.Contains("SWIFTSHADER", StringComparison.Ordinal))
        {
            return SoftwareGpuDriver.SwiftShader;
        }

        return s_softwareFlag ? SoftwareGpuDriver.Unknown : SoftwareGpuDriver.None;
    }

    /// <summary>
    /// Returns a human-readable name of the detected driver, or "hardware" if none.
    /// </summary>
    public static string DriverName()
    {
        return DetectDriver() switch
        {
            SoftwareGpuDriver.Lavapipe => "Lavapipe",
            SoftwareGpuDriver.SwiftShader => "SwiftShader",
            SoftwareGpuDriver.Unknown => "UnknownSoftware",
            _ => "hardware",
        };
    }

    /// <summary>
    /// Throws <see cref="InvalidOperationException"/> if the current environment
    /// does not match the expected driver. Use in tests that assert a specific
    /// software GPU is in use.
    /// </summary>
    public static void AssertDriver(SoftwareGpuDriver expected)
    {
        var actual = DetectDriver();
        if (actual != expected)
        {
            throw new InvalidOperationException(
                $"Expected software GPU driver '{expected}' but detected '{actual}' (VK_ICD_FILENAMES={s_vkIcd ?? "(null)"}).");
        }
    }
}

public enum SoftwareGpuDriver
{
    None,
    Unknown,
    Lavapipe,
    SwiftShader,
}
