using System;
using System.Collections.Generic;
using System.IO;

namespace Etch.Fuzz.Shared;

public static class CorpusSeedLoader
{
    public static IEnumerable<string> Load(string corpusDir)
    {
        if (!Directory.Exists(corpusDir))
            yield break;

        foreach (var file in Directory.EnumerateFiles(corpusDir, "*.bin", SearchOption.TopDirectoryOnly))
        {
            yield return file;
        }
    }

    public static IEnumerable<string> LoadAll(string targetName)
    {
        string repoRoot = FindRepoRoot();
        string corpusDir = Path.Combine(repoRoot, "fuzz", targetName, "corpus");

        foreach (var file in Load(corpusDir))
        {
            yield return file;
        }
    }

    private static string FindRepoRoot()
    {
        string dir = Directory.GetCurrentDirectory();
        while (!string.IsNullOrEmpty(dir))
        {
            if (File.Exists(Path.Combine(dir, "Etch.sln")))
                return dir;
            var parent = Directory.GetParent(dir);
            dir = parent?.FullName ?? "";
        }
        return Directory.GetCurrentDirectory();
    }
}
