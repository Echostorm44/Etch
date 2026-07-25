using System;
using System.Diagnostics;

namespace Etch.TaskVerifier;

public sealed class TrimWarningCountCheck : Check
{
    public override string Verb => "trim-warning-count";

    public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
    {
        var sw = Stopwatch.StartNew();

        if (!args.TryGetValue("project", out var project) || string.IsNullOrEmpty(project))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'project' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!args.TryGetValue("max", out var maxStr) || string.IsNullOrEmpty(maxStr))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'max' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!int.TryParse(maxStr, out int maxWarnings))
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
            return CheckResult.Skipped(verb, args, $"Project not found: {project}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        string outputDir = Path.Combine(Path.GetDirectoryName(project)!, "bin", "trim-warn-temp", GetRandomDirName());

        try
        {
            Directory.CreateDirectory(outputDir);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{project}\" -c Release -r win-x64 --self-contained true -p:PublishAot=true -o \"{outputDir}\" 2>&1",
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process == null)
            {
                sw.Stop();
                return CheckResult.Error(verb, args, "Failed to start dotnet process", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }

            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            process.WaitForExit();

            int warningCount = 0;
            warningCount += CountTrimWarnings(output);
            warningCount += CountTrimWarnings(error);

            sw.Stop();

            if (warningCount <= maxWarnings)
            {
                return CheckResult.Pass(verb, args, $"{warningCount} warnings (max {maxWarnings})", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
            else
            {
                return CheckResult.Fail(verb, args, $"{warningCount} warnings exceeds max {maxWarnings}", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            return CheckResult.Error(verb, args, $"Exception: {ex.Message}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }
        finally
        {
            try
            {
                if (Directory.Exists(outputDir))
                    Directory.Delete(outputDir, recursive: true);
            }
            catch { }
        }
    }

    private static int CountTrimWarnings(string output)
    {
        int count = 0;
        string[] lines = output.Split('\n');
        foreach (var line in lines)
        {
            if ((line.Contains("IL3000") || line.Contains("IL3050") ||
                line.Contains("IL3053") || line.Contains("IL2104") ||
                line.Contains("trim warning") || line.Contains("AOT warning")) &&
                line.Contains("warning"))
            {
                count++;
            }
        }
        return count;
    }

    private static string GetRandomDirName()
    {
        return Guid.NewGuid().ToString("N")[..8];
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
