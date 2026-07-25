using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Etch.TaskVerifier.Reporting;

namespace Etch.TaskVerifier;

public static class Program
{
    private static readonly ConsoleColor _passColor = ConsoleColor.Green;
    private static readonly ConsoleColor _failColor = ConsoleColor.Red;
    private static readonly ConsoleColor _skipColor = ConsoleColor.Yellow;
    private static readonly ConsoleColor _errorColor = ConsoleColor.Magenta;

    public static int Main(string[] args)
    {
        if (args.Length == 0)
        {
            PrintUsage();
            return 0;
        }

        string command = args[0].ToLowerInvariant();
        bool dryRun = args.Contains("--dry-run");
        bool json = args.Contains("--json");

        var remainingArgs = args.Skip(1).ToArray();
        if (dryRun) remainingArgs = remainingArgs.Where(a => a != "--dry-run").ToArray();
        if (json) remainingArgs = remainingArgs.Where(a => a != "--json").ToArray();

        int exitCode = command switch
        {
            "task" => RunTaskCommand(remainingArgs, dryRun, json),
            "track" => RunTrackCommand(remainingArgs, dryRun, json),
            "all" => RunAllCommand(dryRun, json),
            _ => RunUnknownCommand(command)
        };

        return exitCode;
    }

    private static int RunTaskCommand(string[] args, bool dryRun, bool json)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: task command requires a path argument");
            return 1;
        }

        string path = args[0];
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(Directory.GetCurrentDirectory(), path);
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"Error: file not found: {path}");
            return 1;
        }

        var results = ProcessFile(path, dryRun);

        if (json)
        {
            var report = JsonReport.FromResults($"task {args[0]}", results);
            Console.WriteLine(report.ToJson());
        }
        else
        {
            PrintResults(results);
        }

        return results.Any(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Error) ? 1 : 0;
    }

    private static int RunTrackCommand(string[] args, bool dryRun, bool json)
    {
        if (args.Length == 0)
        {
            Console.Error.WriteLine("Error: track command requires a directory argument");
            return 1;
        }

        string trackDir = args[0];
        if (!Path.IsPathRooted(trackDir))
        {
            trackDir = Path.Combine(Directory.GetCurrentDirectory(), trackDir);
        }

        if (!Directory.Exists(trackDir))
        {
            Console.Error.WriteLine($"Error: directory not found: {trackDir}");
            return 1;
        }

        var allResults = new List<CheckResult>();
        foreach (var file in Directory.EnumerateFiles(trackDir, "*.md", SearchOption.AllDirectories))
        {
            var results = ProcessFile(file, dryRun);
            allResults.AddRange(results);
        }

        if (json)
        {
            var report = JsonReport.FromResults($"track {args[0]}", allResults);
            Console.WriteLine(report.ToJson());
        }
        else
        {
            Console.WriteLine($"\n=== Track: {trackDir} ===");
            PrintResults(allResults);
        }

        return allResults.Any(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Error) ? 1 : 0;
    }

    private static int RunAllCommand(bool dryRun, bool json)
    {
        string repoRoot = FindRepoRoot(Directory.GetCurrentDirectory());
        var allResults = new List<CheckResult>();

        string[] tracks = { "01-foundations", "02-ffi", "03-geometry", "04-scene", "05-tiling",
                           "06-shaders", "07-cpu-raster", "08-gpu-compositor", "09-clip-blend-gradient",
                           "10-strokes", "11-text", "12-effects", "13-correctness", "14-samples", "15-docs" };

        foreach (var track in tracks)
        {
            var trackDir = Path.Combine(repoRoot, "docs", track);
            if (!Directory.Exists(trackDir))
                continue;

            Console.WriteLine($"Scanning {track}...");

            foreach (var file in Directory.EnumerateFiles(trackDir, "*.md", SearchOption.TopDirectoryOnly))
            {
                var results = ProcessFile(file, dryRun);
                allResults.AddRange(results);
            }
        }

        if (json)
        {
            var report = JsonReport.FromResults("all", allResults);
            Console.WriteLine(report.ToJson());
        }
        else
        {
            Console.WriteLine($"\n=== All Tasks ===");
            PrintResults(allResults);
        }

        return allResults.Any(r => r.Status == CheckStatus.Fail || r.Status == CheckStatus.Error) ? 1 : 0;
    }

    private static int RunUnknownCommand(string command)
    {
        Console.Error.WriteLine($"Error: unknown command '{command}'");
        PrintUsage();
        return 1;
    }

    private static List<CheckResult> ProcessFile(string path, bool dryRun)
    {
        var results = new List<CheckResult>();

        foreach (var directive in DirectiveParser.ParseFile(path))
        {
            var check = CheckRegistry.Get(directive.Verb);
            if (check == null)
            {
                var result = CheckResult.Fail(
                    directive.Verb,
                    directive.Args,
                    $"Unknown verify verb '{directive.Verb}' in {Path.GetFileName(path)}:{directive.LineNumber}",
                    0,
                    path,
                    directive.LineNumber);
                results.Add(result);
                continue;
            }

            if (dryRun)
            {
                var argsStr = string.Join(", ", directive.Args.Select(kv => $"{kv.Key}={kv.Value}"));
                Console.WriteLine($"[DRY] {directive.Verb} {argsStr} ({path}:{directive.LineNumber})");
                continue;
            }

            var checkResult = check.Run(directive.Verb, directive.Args, path, directive.LineNumber);
            results.Add(checkResult);
        }

        return results;
    }

    private static void PrintResults(IEnumerable<CheckResult> results)
    {
        int passCount = 0, failCount = 0, skipCount = 0, errorCount = 0;

        foreach (var result in results)
        {
            string statusStr;
            ConsoleColor color;

            switch (result.Status)
            {
                case CheckStatus.Pass:
                    statusStr = "PASS";
                    color = _passColor;
                    passCount++;
                    break;
                case CheckStatus.Fail:
                    statusStr = "FAIL";
                    color = _failColor;
                    failCount++;
                    break;
                case CheckStatus.Skipped:
                    statusStr = "SKIP";
                    color = _skipColor;
                    skipCount++;
                    break;
                case CheckStatus.Error:
                    statusStr = "ERROR";
                    color = _errorColor;
                    errorCount++;
                    break;
                default:
                    statusStr = "???";
                    color = ConsoleColor.White;
                    break;
            }

            var originalColor = Console.ForegroundColor;
            Console.ForegroundColor = color;

            string argsStr = string.Join(" ", result.Args.Select(kv => $"{kv.Key}={kv.Value}"));
            Console.WriteLine($"[{statusStr}] {result.Verb,-20} {argsStr,-40} {result.Detail}");

            Console.ForegroundColor = originalColor;
        }

        Console.WriteLine();
        int total = passCount + failCount + skipCount + errorCount;
        Console.Write($"Results: {passCount}/{total} passed");
        if (failCount > 0) Console.Write($", {failCount} failed");
        if (skipCount > 0) Console.Write($", {skipCount} skipped");
        if (errorCount > 0) Console.Write($", {errorCount} errors");
        Console.WriteLine();
    }

    private static void PrintUsage()
    {
        Console.WriteLine(@"
Etch.TaskVerifier - Parse and execute verify directives from task files

Usage:
  etch-verify task <path-to-task.md> [options]
  etch-verify track <track-dir> [options]
  etch-verify all [options]

Commands:
  task <path>    Run all directives in a single task file
  track <dir>    Run all task directives in a track directory
  all            Run all tasks under docs/**

Options:
  --dry-run      Print what would run without executing
  --json         Emit JSON result report

Examples:
  etch-verify task docs/01-foundations/FND-004.md
  etch-verify track docs/01-foundations --dry-run
  etch-verify all --json
");
    }

    private static string FindRepoRoot(string startDir)
    {
        var dir = startDir;
        while (dir != null)
        {
            if (File.Exists(Path.Combine(dir, "Etch.sln")))
                return dir;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName;
        }
        return startDir;
    }
}
