using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Etch.TaskVerifier;

public sealed class AotPublishCheck : Check
{
    public override string Verb => "aot-publish";

    public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
    {
        var sw = Stopwatch.StartNew();

        if (!args.TryGetValue("project", out var project) || string.IsNullOrEmpty(project))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'project' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!args.TryGetValue("rid", out var rid) || string.IsNullOrEmpty(rid))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'rid' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
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

        string outputDir = Path.Combine(Path.GetDirectoryName(project)!, "bin", "aot-publish-temp");
        string publishDir = Path.Combine(outputDir, rid);

        try
        {
            Directory.CreateDirectory(outputDir);

            var psi = new ProcessStartInfo
            {
                FileName = "dotnet",
                Arguments = $"publish \"{project}\" -c Release -r {rid} --self-contained true -p:PublishAot=true -o \"{publishDir}\"",
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
            warningCount += CountAotWarnings(output);
            warningCount += CountAotWarnings(error);

            sw.Stop();

            args.TryGetValue("maxMB", out var maxMbStr);
            string sizeInfo = "";
            if (warningCount == 0)
            {
                long sizeBytes = GetPublishSize(publishDir);
                double sizeMb = sizeBytes / (1024.0 * 1024.0);
                sizeInfo = $", {sizeMb:F1} MB";

                if (!string.IsNullOrEmpty(maxMbStr) && double.TryParse(maxMbStr, out double maxMb) && sizeMb > maxMb)
                {
                    return CheckResult.Fail(verb, args, $"Binary size {sizeMb:F1} MB exceeds max {maxMb} MB", sw.ElapsedMilliseconds, taskFile, lineNumber);
                }

                return CheckResult.Pass(verb, args, $"0 warnings{sizeInfo}", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
            else
            {
                return CheckResult.Fail(verb, args, $"{warningCount} AOT/trim warnings", sw.ElapsedMilliseconds, taskFile, lineNumber);
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

    private static int CountAotWarnings(string output)
    {
        int count = 0;
        string[] lines = output.Split('\n');
        foreach (var line in lines)
        {
            if (line.Contains("IL3000") || line.Contains("IL3050") ||
                line.Contains("IL3053") || line.Contains("IL2104") ||
                line.Contains("AOT") || line.Contains("trim") && line.Contains("warning"))
            {
                count++;
            }
        }
        return count;
    }

    private static long GetPublishSize(string publishDir)
    {
        if (!Directory.Exists(publishDir))
            return 0;

        long totalSize = 0;
        foreach (var file in Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories))
        {
            try
            {
                totalSize += new FileInfo(file).Length;
            }
            catch { }
        }
        return totalSize;
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
