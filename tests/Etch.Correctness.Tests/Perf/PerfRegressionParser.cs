using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;

namespace Etch.Correctness.Tests.Perf;

internal readonly struct PerfBudgetRow
{
    public readonly string Scenario;
    public readonly string Target;
    public readonly string Section;
    public readonly string Notes;

    public PerfBudgetRow(string scenario, string target, string section, string notes)
    {
        Scenario = scenario;
        Target = target;
        Section = section;
        Notes = notes;
    }

    public long? ParseTargetMs()
    {
        var match = Regex.Match(Target, @"<\s*(\d+(?:\.\d+)?)\s*ms", RegexOptions.CultureInvariant);
        if (match.Success)
            return (long)(double.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture) * 1000);
        return null;
    }

    public long? ParseTargetFps()
    {
        var match = Regex.Match(Target, @"(\d+)\s*fps", RegexOptions.CultureInvariant);
        if (match.Success)
            return long.Parse(match.Groups[1].Value, CultureInfo.InvariantCulture);
        return null;
    }
}

internal static class PerfRegressionParser
{
    public static List<PerfBudgetRow> ParseProjectPlan(string projectRoot)
    {
        var planPath = Path.Combine(projectRoot, "ProjectPlan.md");
        if (!File.Exists(planPath))
            return new List<PerfBudgetRow>();

        var rows = new List<PerfBudgetRow>();
        var lines = File.ReadAllLines(planPath);
        string currentSection = "";

        for (int i = 0; i < lines.Length; i++)
        {
            string line = lines[i];

            if (line.StartsWith("### Performance", StringComparison.Ordinal))
            {
                currentSection = "General";
                continue;
            }

            if (line.StartsWith("### CPU-only", StringComparison.Ordinal))
            {
                currentSection = "CPU";
                continue;
            }

            if (line.StartsWith("### GPU", StringComparison.Ordinal))
            {
                currentSection = "GPU";
                continue;
            }

            if (line.StartsWith("###", StringComparison.Ordinal) && currentSection.Length > 0)
            {
                currentSection = "";
                continue;
            }

            if (currentSection.Length == 0)
                continue;

            if (!line.StartsWith('|') || line.Contains("---|---", StringComparison.Ordinal))
                continue;

            if (line.Contains("2026-", StringComparison.Ordinal))
                continue;

            string[] cells = line.Split('|', StringSplitOptions.TrimEntries);
            if (cells.Length < 3)
                continue;

            string scenario = cells[1].Trim();
            if (scenario.Length == 0)
                continue;

            string target = cells[2].Trim();
            string notes = cells.Length > 3 ? cells[3].Trim() : "";

            rows.Add(new PerfBudgetRow(scenario, target, currentSection, notes));
        }

        return rows;
    }
}
