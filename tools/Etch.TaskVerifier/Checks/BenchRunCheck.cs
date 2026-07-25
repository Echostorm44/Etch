using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Etch.TaskVerifier;

public sealed class BenchRunCheck : Check
{
    public override string Verb => "bench-run";

    public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
    {
        var sw = Stopwatch.StartNew();

        if (!args.TryGetValue("project", out var project) || string.IsNullOrEmpty(project))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'project' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!args.TryGetValue("method", out var method) || string.IsNullOrEmpty(method))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'method' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!Path.IsPathRooted(project))
        {
            var repoRoot = FindRepoRoot(taskFile);
            project = Path.Combine(repoRoot, project);
        }

        if (!File.Exists(project))
        {
            sw.Stop();
            return CheckResult.Skipped(verb, args, $"Benchmark project not found: {project}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"run -c Release --project \"{project}\" -- --filter \"*{method}*\"",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                sw.Stop();
                return CheckResult.Error(verb, args, "Failed to start benchmark process", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            sw.Stop();

            if (process.ExitCode == 0)
            {
                return CheckResult.Pass(verb, args, "Benchmark completed successfully", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
            else
            {
                return CheckResult.Fail(verb, args, $"Benchmark failed with exit code {process.ExitCode}", sw.ElapsedMilliseconds, taskFile, lineNumber);
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
            if (File.Exists(Path.Combine(dir, "Etch.sln")))
                return dir;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        return Path.GetDirectoryName(taskFile) ?? "";
    }
}
