using System;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Etch.TaskVerifier;

public sealed class FileExistsCheck : Check
{
    public override string Verb => "file-exists";

    public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
    {
        var sw = Stopwatch.StartNew();

        if (!args.TryGetValue("path", out var path) || string.IsNullOrEmpty(path))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'path' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!Path.IsPathRooted(path))
        {
            var repoRoot = FindRepoRoot(taskFile);
            path = Path.Combine(repoRoot, path);
        }

        bool exists = File.Exists(path) || Directory.Exists(path);
        sw.Stop();

        if (exists)
            return CheckResult.Pass(verb, args, $"Path exists: {path}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        else
            return CheckResult.Fail(verb, args, $"Path not found: {path}", sw.ElapsedMilliseconds, taskFile, lineNumber);
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
