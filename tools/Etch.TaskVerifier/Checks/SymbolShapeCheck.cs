using System;
using System.IO;
using Mono.Cecil;

namespace Etch.TaskVerifier;

public sealed class SymbolShapeCheck : Check
{
    public override string Verb => "symbol-shape";

    public override CheckResult Run(string verb, Dictionary<string, string> args, string taskFile, int lineNumber)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        if (!args.TryGetValue("assembly", out var assembly) || string.IsNullOrEmpty(assembly))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'assembly' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
        }

        if (!args.TryGetValue("type", out var typeName) || string.IsNullOrEmpty(typeName))
        {
            sw.Stop();
            return CheckResult.Fail(verb, args, "Missing required 'type' argument", sw.ElapsedMilliseconds, taskFile, lineNumber);
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

        args.TryGetValue("sealed", out var sealedStr);
        args.TryGetValue("abstract", out var abstractStr);
        args.TryGetValue("public", out var publicStr);

        try
        {
            using var assemblyDef = AssemblyDefinition.ReadAssembly(assembly, new ReaderParameters(ReadingMode.Immediate));

            TypeDefinition? typeDef = null;
            foreach (var type in assemblyDef.MainModule.Types)
            {
                if (type.FullName == typeName || type.Name == typeName)
                {
                    typeDef = type;
                    break;
                }
            }

            sw.Stop();

            if (typeDef == null)
            {
                return CheckResult.Fail(verb, args, $"Type not found: {typeName}", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }

            var failures = new System.Collections.Generic.List<string>();

            if (sealedStr != null && bool.TryParse(sealedStr, out bool shouldBeSealed))
            {
                if (shouldBeSealed && !typeDef.IsSealed)
                    failures.Add($"expected sealed");
                else if (!shouldBeSealed && typeDef.IsSealed)
                    failures.Add($"expected not sealed");
            }

            if (abstractStr != null && bool.TryParse(abstractStr, out bool shouldBeAbstract))
            {
                if (shouldBeAbstract && !typeDef.IsAbstract)
                    failures.Add($"expected abstract");
                else if (!shouldBeAbstract && typeDef.IsAbstract)
                    failures.Add($"expected not abstract");
            }

            if (publicStr != null && bool.TryParse(publicStr, out bool shouldBePublic))
            {
                if (shouldBePublic && !typeDef.IsPublic)
                    failures.Add($"expected public");
                else if (!shouldBePublic && typeDef.IsPublic)
                    failures.Add($"expected not public");
            }

            if (failures.Count > 0)
            {
                return CheckResult.Fail(verb, args, $"Type {typeName}: {string.Join(", ", failures)}", sw.ElapsedMilliseconds, taskFile, lineNumber);
            }

            return CheckResult.Pass(verb, args, $"Type {typeName} has expected shape", sw.ElapsedMilliseconds, taskFile, lineNumber);
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
