using System;
using System.IO;

namespace Etch.Testing;

public static class SceneRunner
{
    private static readonly bool s_regenGoldens = Environment.GetEnvironmentVariable("ETCH_REGEN_GOLDENS") == "1";
    private static readonly bool s_softwareGpu = Environment.GetEnvironmentVariable("ETCH_SOFTWARE_GPU") == "1";

    public static bool RegenerateGoldens => s_regenGoldens;

    /// <summary>
    /// True when <c>ETCH_SOFTWARE_GPU=1</c> is set in the environment.
    /// GPU rendering paths should skip hardware-only validation checks
    /// (e.g. timestamp queries) when this flag is set.
    /// </summary>
    public static bool IsSoftwareGpu => s_softwareGpu;

    public static byte[] RunCpu(Etch.Scene.SceneBuffer scene, int w, int h)
    {
        return SceneCpuRenderer.RenderToRgba8(scene, w, h);
    }

    public static byte[] RunGpu(Etch.Scene.SceneBuffer scene, int w, int h)
    {
        return SceneGpuRenderer.RenderToRgba8(scene, w, h);
    }

    public static bool WriteGolden(string path, byte[] pngData)
    {
        try
        {
            var directory = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            File.WriteAllBytes(path, pngData);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static byte[]? ReadGolden(string path)
    {
        try
        {
            if (File.Exists(path))
                return File.ReadAllBytes(path);
            return null;
        }
        catch
        {
            return null;
        }
    }

    public static void ProcessGoldenRegen(string goldenPath, byte[] actual, int w, int h, DiffTolerance tolerance, Func<byte[], byte[], int, int, DiffTolerance, DiffResult>? compare)
    {
        if (compare == null || !s_regenGoldens)
        {
            return;
        }

        var golden = ReadGolden(goldenPath);
        if (golden == null)
        {
            return;
        }

        var result = compare(golden, actual, w, h, tolerance);
        if (!result.Pass)
        {
            WriteGolden(goldenPath, actual);
        }
    }
}