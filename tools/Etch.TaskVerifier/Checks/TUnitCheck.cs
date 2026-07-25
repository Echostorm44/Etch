using System;
using System.Diagnostics;

namespace Etch.TaskVerifier;

public sealed class TUnitCheck : Check
{
    public override string Verb => "tunit";

    public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
    {
        var sw = Stopwatch.StartNew();

        if (!args.TryGetValue("class", out var testClass) || string.IsNullOrEmpty(testClass))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'class' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        var repoRoot = FindRepoRoot(taskFile);
        var testProject = FindTestProject(repoRoot, testClass);

        if (testProject == null)
        {
            sw.Stop();
            return CheckResult.Skipped(verb, args, $"Test project not found for class: {testClass}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"test \"{testProject}\" --filter \"FullyQualifiedName~{testClass}\" --no-build",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                WorkingDirectory = repoRoot
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                sw.Stop();
                return CheckResult.Error(verb, args, "Failed to start dotnet test process", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            sw.Stop();

            if (process.ExitCode == 0)
            {
                return CheckResult.Pass(verb, args, "All tests passed", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
            else
            {
                bool buildFailed = output.Contains("Build FAILED") || error.Contains("Build FAILED");
                if (buildFailed)
                {
                    return CheckResult.Fail(verb, args, "Build failed", sw.ElapsedMilliseconds, taskFile, lineNumber);
                }
                return CheckResult.Fail(verb, args, $"Tests failed with exit code {process.ExitCode}", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CheckResult.Error(verb, args, $"Exception: {ex.Message}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }
    }

    private static string FindRepoRoot(string taskFile)
    {
        var dir = Path.GetDirectoryName(taskFile);
        while (dir != null)
        {
            if (System.IO.File.Exists(System.IO.Path.Combine(dir, "Etch.sln")))
                return dir;
            var parent = System.IO.Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        return System.IO.Path.GetDirectoryName(taskFile) ?? "";
    }

    private static string? FindTestProject(string repoRoot, string testClass)
    {
        var testsDir = System.IO.Path.Combine(repoRoot, "tests");
        if (!System.IO.Directory.Exists(testsDir))
            return null;

        foreach (var dir in System.IO.Directory.EnumerateDirectories(testsDir, "Etch.*.Tests"))
        {
            var csproj = System.IO.Path.Combine(dir, System.IO.Path.GetFileName(dir) + ".csproj");
            if (System.IO.File.Exists(csproj))
                return csproj;
        }

        return null;
    }
}
