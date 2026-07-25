using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace Etch.FlakeDetector;

internal static class Program
{
    internal static int Main(string[] args)
    {
        ArgumentNullException.ThrowIfNull(args);

        if (args.Length > 0 && args[0] == "scan")
        {
            int days = 30;
            for (int i = 1; i < args.Length; i++)
            {
                if (args[i] == "--days" && i + 1 < args.Length && int.TryParse(args[i + 1], out int d))
                {
                    days = d;
                    i++;
                }
            }

            var report = Detector.Scan(days);
            Console.WriteLine(report.ToJson());
            return report.BreachCount > 0 ? 1 : 0;
        }

        Console.WriteLine(GetUsage());
        return 0;
    }

    private static string GetUsage() => "Usage: Etch.FlakeDetector scan [--days N]";
}

/// <summary>
/// Analyzes test run history to detect flaky tests and quarantine deadline breaches.
/// </summary>
internal static class Detector
{
    /// <summary>
    /// A test is flaky when it fails at least <paramref name="thresholdFailures"/>
    /// times in the last <paramref name="thresholdRuns"/> runs.
    /// </summary>
    internal static bool IsFlaky(IReadOnlyList<TestRunRecord> history, int thresholdFailures = 2, int thresholdRuns = 10)
    {
        ArgumentNullException.ThrowIfNull(history);

        if (history.Count < thresholdRuns)
            return false;

        var recent = history.TakeLast(thresholdRuns);
        int failures = recent.Count(r => !r.Passed);
        return failures >= thresholdFailures;
    }

    /// <summary>
    /// Returns true when the quarantine date is more than <paramref name="maxDays"/>
    /// days in the past.
    /// </summary>
    internal static bool IsDeadlineBreached(string quarantinedSinceIso, DateTimeOffset now, int maxDays = 7)
    {
        if (!DateTimeOffset.TryParseExact(quarantinedSinceIso, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var since))
            return false;

        return (now.Date - since.Date).TotalDays > maxDays;
    }

    /// <summary>
    /// Produces a report from the given test histories.
    /// </summary>
    internal static FlakeReport Scan(IReadOnlyDictionary<string, IReadOnlyList<TestRunRecord>> testHistories, DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(testHistories);

        var offenders = new List<FlakyTestEntry>();
        var breaches = new List<DeadlineBreachEntry>();

        foreach (var (testName, history) in testHistories)
        {
            if (IsFlaky(history))
            {
                offenders.Add(new FlakyTestEntry(testName, history.Count(r => !r.Passed), history.Count));
            }
        }

        return new FlakeReport(offenders, breaches, offenders.Count >= 3);
    }

    /// <summary>
    /// Stub scan that produces an empty report. A real implementation would query CI history
    /// (GitHub Actions API, test-result artifacts, etc.), group runs per test, and use
    /// <see cref="IsFlaky"/> to populate offenders. Until that source is wired up, it reports
    /// no offenders/breaches so the tool builds and runs deterministically.
    /// </summary>
    internal static FlakeReport Scan(int daysBack)
    {
        _ = daysBack;
        return new FlakeReport(
            Array.Empty<FlakyTestEntry>(),
            Array.Empty<DeadlineBreachEntry>(),
            mergeFreeze: false);
    }
}

internal readonly record struct TestRunRecord(bool Passed, DateTimeOffset Timestamp);

internal readonly record struct FlakyTestEntry(string TestName, int Failures, int TotalRuns);

internal readonly record struct DeadlineBreachEntry(string TestName, string QuarantinedSince, int DaysOverdue);

internal sealed class FlakeReport
{
    public IReadOnlyList<FlakyTestEntry> Offenders { get; }
    public IReadOnlyList<DeadlineBreachEntry> Breaches { get; }
    public bool MergeFreeze { get; }
    public int OffenderCount => Offenders.Count;
    public int BreachCount => Breaches.Count;

    public FlakeReport(IReadOnlyList<FlakyTestEntry> offenders, IReadOnlyList<DeadlineBreachEntry> breaches, bool mergeFreeze)
    {
        Offenders = offenders;
        Breaches = breaches;
        MergeFreeze = mergeFreeze;
    }

    public string ToJson()
    {
        var lines = new List<string>
        {
            "{",
            $"  \"mergeFreeze\": {(MergeFreeze ? "true" : "false")},",
            $"  \"offenderCount\": {OffenderCount},",
            $"  \"breachCount\": {BreachCount},",
            "  \"offenders\": ["
        };

        for (int i = 0; i < Offenders.Count; i++)
        {
            var o = Offenders[i];
            string comma = i < Offenders.Count - 1 ? "," : "";
            lines.Add($"    {{ \"test\": \"{o.TestName}\", \"failures\": {o.Failures}, \"totalRuns\": {o.TotalRuns} }}{comma}");
        }

        lines.Add("  ],");
        lines.Add("  \"breaches\": [");

        for (int i = 0; i < Breaches.Count; i++)
        {
            var b = Breaches[i];
            string comma = i < Breaches.Count - 1 ? "," : "";
            lines.Add($"    {{ \"test\": \"{b.TestName}\", \"since\": \"{b.QuarantinedSince}\", \"daysOverdue\": {b.DaysOverdue} }}{comma}");
        }

        lines.Add("  ]");
        lines.Add("}");

        return string.Join("\n", lines);
    }
}
