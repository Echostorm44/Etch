using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Mono.Cecil;

namespace Etch.TaskVerifier;

public sealed class SymbolAbsentCheck : Check
{
    public override string Verb => "symbol-absent";

    public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (!args.TryGetValue("assembly", out var assembly) || string.IsNullOrEmpty(assembly))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'assembly' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!args.TryGetValue("pattern", out var pattern) || string.IsNullOrEmpty(pattern))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'pattern' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!Path.IsPathRooted(assembly))
        {
            var repoRoot = FindRepoRoot(taskFile);
            assembly = Path.Combine(repoRoot, assembly);
        }

        if (!File.Exists(assembly))
        {
            sw.Stop();
            return CheckResult.Skipped(verb, args, $"Assembly not found: {assembly}", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        try
        {
            using var assemblyDef = AssemblyDefinition.ReadAssembly(assembly, new ReaderParameters(ReadingMode.Immediate));

            bool found = false;
            string foundIn = "";

            foreach (var type in assemblyDef.MainModule.Types)
            {
                if (type.Namespace != null && type.Namespace.Contains(pattern))
                {
                    found = true;
                    foundIn = $"namespace: {type.Namespace}";
                    break;
                }
                if (type.Name != null && type.Name.Contains(pattern))
                {
                    found = true;
                    foundIn = $"type: {type.Name}";
                    break;
                }
            }

            sw.Stop();

            if (found)
            {
                return CheckResult.Fail(verb, args, $"Pattern '{pattern}' found in {foundIn}", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }
            else
            {
                return CheckResult.Pass(verb, args, $"Pattern '{pattern}' not found", sw.ElapsedMilliseconds, taskFile, lineNumber);
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
