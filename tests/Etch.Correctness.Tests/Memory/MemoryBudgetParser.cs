using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace Etch.Correctness.Tests.Memory;

internal readonly struct MemoryBudgetRow
{
    public readonly string Scenario;
    public readonly long BudgetBytes;
    public readonly string Notes;

    public MemoryBudgetRow(string scenario, long budgetBytes, string notes)
    {
        Scenario = scenario;
        BudgetBytes = budgetBytes;
        Notes = notes;
    }
}

internal static class MemoryBudgetParser
{
    private static readonly Regex s_rowRegex = new(
        @"^\|\s*(.+?)\s*\|\s*\*{0,2}(?:<\s*)?(\d+(?:,\d{3})*(?:\.\d+)?)\s*(MB|bytes|GB)\s*(?:[^*]*?)\*{0,2}\s*\|",
        RegexOptions.Compiled);

    public static List<MemoryBudgetRow> ParseProjectPlan(string projectRoot)
    {
        var planPath = Path.Combine(projectRoot, "ProjectPlan.md");
        if (!File.Exists(planPath))
            return new List<MemoryBudgetRow>();

        var rows = new List<MemoryBudgetRow>();
        var lines = File.ReadAllLines(planPath);
        bool inMemorySection = false;

        foreach (var line in lines)
        {
            if (line.StartsWith("### Memory", StringComparison.Ordinal))
            {
                inMemorySection = true;
                continue;
            }

            if (inMemorySection && line.StartsWith("###", StringComparison.Ordinal))
                break;

            if (!inMemorySection)
                continue;

            if (!line.StartsWith('|') || line.Contains("---|---", StringComparison.Ordinal))
                continue;

            var match = s_rowRegex.Match(line);
            if (match.Success)
            {
                string scenario = match.Groups[1].Value.Trim();
                string value = match.Groups[2].Value.Replace(",", "", StringComparison.Ordinal);
                string unit = match.Groups[3].Value;
                string notes = ExtractNotes(line);

                long bytes = unit switch
                {
                    "bytes" => long.Parse(value),
                    "MB" => long.Parse(value) * 1024 * 1024,
                    "GB" => long.Parse(value) * 1024 * 1024 * 1024,
                    _ => 0,
                };

                rows.Add(new MemoryBudgetRow(scenario, bytes, notes));
            }
        }

        return rows;
    }

    private static string ExtractNotes(string line)
    {
        int lastPipe = line.LastIndexOf('|');
        if (lastPipe < 0)
            return string.Empty;
        return line.Substring(lastPipe + 1).Trim();
    }
}
