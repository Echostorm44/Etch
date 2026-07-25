using System;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace Etch.TaskVerifier;

public sealed class BenchAllocCheck : Check
{
    public override string Verb => "bench-alloc";

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

        if (!args.TryGetValue("max", out var maxStr) || string.IsNullOrEmpty(maxStr))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'max' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!long.TryParse(maxStr, out long maxBytes))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, $"Invalid max value: {maxStr}", sw.ElapsedMilliseconds, taskFile, lineNumber);
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

            long allocatedBytes = ParseAllocatedBytes(output);

            sw.Stop();

            if (allocatedBytes <= maxBytes)
            {
                return CheckResult.Pass(verb, args, $"Allocated {allocatedBytes} B (max {maxBytes} B)", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
            else
            {
                return CheckResult.Fail(verb, args, $"Allocated {allocatedBytes} B exceeds max {maxBytes} B", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CheckResult.Error(verb, args, $"Exception: {ex.Message}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }
    }

    private static long ParseAllocatedBytes(string output)
    {
        string[] lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains('|') && line.Contains("Allocated"))
            {
                var parts = line.Split('|');
                foreach (var part in parts)
                {
                    var trimmed = part.Trim();
                    if (trimmed == "-" || trimmed == "0")
                        return 0;
                    if (long.TryParse(trimmed, out long bytes))
                        return bytes;
                }
            }
        }
        return -1;
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
