namespace Etch.Shaders.Tests;

internal sealed class AllShadersValidateTests
{
    [Test]
    public async Task ValidateAllWgslShadersWithNaga()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslFiles = Directory.GetFiles(shaderDir, "*.wgsl", SearchOption.AllDirectories);

        await Assert.That(wgslFiles.Length).IsGreaterThan(0);

        foreach (var wgslFile in wgslFiles)
        {
            var result = RunNaga(wgslFile, Array.Empty<string>());
            await Assert.That(result.ExitCode).IsEqualTo(0);
        }
    }

    [Test]
    public async Task CrossCompileAllShadersToSpv()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslFiles = Directory.GetFiles(shaderDir, "*.wgsl", SearchOption.AllDirectories);

        foreach (var wgslFile in wgslFiles)
        {
            var tempOutput = Path.Combine(Path.GetTempPath(), $"naga_test_{Guid.NewGuid()}.spv");
            try
            {
                var result = RunNaga(wgslFile, [tempOutput]);
                await Assert.That(result.ExitCode).IsEqualTo(0);
            }
            finally
            {
                if (File.Exists(tempOutput))
                    File.Delete(tempOutput);
            }
        }
    }

    [Test]
    public async Task CrossCompileAllShadersToMsl()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslFiles = Directory.GetFiles(shaderDir, "*.wgsl", SearchOption.AllDirectories);

        foreach (var wgslFile in wgslFiles)
        {
            var tempOutput = Path.Combine(Path.GetTempPath(), $"naga_test_{Guid.NewGuid()}.metal");
            try
            {
                // Target Metal 2.1 (macOS 10.14+/iOS 12+): naga's default MSL version rejects
                // the instance_index builtin (used by strip_coverage.wgsl). wgpu-native picks a
                // suitable version at runtime; this only pins the CLI cross-compile check.
                var result = RunNaga(wgslFile, ["--metal-version", "2.1", tempOutput]);
                await Assert.That(result.ExitCode).IsEqualTo(0);
            }
            finally
            {
                if (File.Exists(tempOutput))
                    File.Delete(tempOutput);
            }
        }
    }

    [Test]
    public async Task CrossCompileAllShadersToHlsl()
    {
        var shaderDir = Path.Combine(GetRepoRoot(), "shaders");
        var wgslFiles = Directory.GetFiles(shaderDir, "*.wgsl", SearchOption.AllDirectories);

        foreach (var wgslFile in wgslFiles)
        {
            var tempOutput = Path.Combine(Path.GetTempPath(), $"naga_test_{Guid.NewGuid()}.hlsl");
            try
            {
                var result = RunNaga(wgslFile, [tempOutput]);
                await Assert.That(result.ExitCode).IsEqualTo(0);
            }
            finally
            {
                if (File.Exists(tempOutput))
                    File.Delete(tempOutput);
            }
        }
    }

    private static string GetRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "Etch.sln")))
        {
            dir = Directory.GetParent(dir)?.FullName;
        }
        return dir ?? throw new InvalidOperationException("Could not find repo root");
    }

    private static (int ExitCode, string StdOut, string StdErr) RunNaga(string inputFile, string[] outputFiles)
    {
        var nagaPath = GetNagaPath();
        var args = new List<string> { inputFile };
        args.AddRange(outputFiles);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = nagaPath,
            Arguments = string.Join(" ", args.Select(a => $"\"{a}\"")),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        var process = System.Diagnostics.Process.Start(psi);
        if (process == null)
            return (-1, "", "Failed to start naga");

        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit(30000);

        return (process.ExitCode, stdout, stderr);
    }

    private static string GetNagaPath()
    {
        var paths = new[]
        {
            "naga",
            "naga.exe",
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "naga"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cargo", "bin", "naga.exe")
        };

        foreach (var path in paths)
        {
            if (File.Exists(path))
                return path;
        }

        return "naga";
    }
}