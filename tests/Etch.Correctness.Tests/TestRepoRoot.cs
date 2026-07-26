using System;
using System.IO;

namespace Etch.Correctness.Tests;

/// <summary>
/// Locates the repository root by walking up from the test binary directory until it finds
/// Etch.sln. Robust to build-output depth (bin/Release/&lt;tfm&gt;/[rid]/...), unlike counting a
/// fixed number of "../" segments.
/// </summary>
internal static class TestRepoRoot
{
    public static string Path
    {
        get
        {
            var dir = AppContext.BaseDirectory;
            while (!string.IsNullOrEmpty(dir))
            {
                if (File.Exists(System.IO.Path.Combine(dir, "Etch.sln")))
                    return dir;
                dir = Directory.GetParent(dir)?.FullName;
            }
            throw new InvalidOperationException(
                "Could not locate the repo root (Etch.sln) from " + AppContext.BaseDirectory);
        }
    }
}
