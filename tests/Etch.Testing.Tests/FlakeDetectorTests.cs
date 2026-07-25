using System;
using System.Collections.Generic;
using Etch.FlakeDetector;
using TUnit;

namespace Etch.Testing.Tests;

internal sealed class FlakeDetectorTests
{
    [Test]
    public async Task IsFlaky_LessThanThresholdRuns_ReturnsFalse()
    {
        var history = new List<TestRunRecord>
        {
            new(false, DateTimeOffset.UtcNow),
        };

        bool result = Detector.IsFlaky(history, thresholdFailures: 2, thresholdRuns: 10);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsFlaky_ExactlyThresholdFailures_ReturnsTrue()
    {
        var history = new List<TestRunRecord>
        {
            new(true, DateTimeOffset.UtcNow.AddMinutes(-10)),
            new(false, DateTimeOffset.UtcNow.AddMinutes(-9)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-8)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-7)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-6)),
            new(false, DateTimeOffset.UtcNow.AddMinutes(-5)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-4)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-3)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-2)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-1)),
        };

        bool result = Detector.IsFlaky(history, thresholdFailures: 2, thresholdRuns: 10);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task IsFlaky_BelowThresholdFailures_ReturnsFalse()
    {
        var history = new List<TestRunRecord>
        {
            new(false, DateTimeOffset.UtcNow.AddMinutes(-9)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-8)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-7)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-6)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-5)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-4)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-3)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-2)),
            new(true, DateTimeOffset.UtcNow.AddMinutes(-1)),
            new(true, DateTimeOffset.UtcNow),
        };

        bool result = Detector.IsFlaky(history, thresholdFailures: 2, thresholdRuns: 10);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsDeadlineBreached_AtExactlyMaxDays_ReturnsFalse()
    {
        var now = new DateTimeOffset(2026, 4, 30, 0, 0, 0, TimeSpan.Zero);
        bool result = Detector.IsDeadlineBreached("2026-04-23", now, maxDays: 7);

        await Assert.That(result).IsFalse();
    }

    [Test]
    public async Task IsDeadlineBreached_OneDayOver_ReturnsTrue()
    {
        var now = new DateTimeOffset(2026, 5, 1, 0, 0, 0, TimeSpan.Zero);
        bool result = Detector.IsDeadlineBreached("2026-04-23", now, maxDays: 7);

        await Assert.That(result).IsTrue();
    }

    [Test]
    public async Task Scan_TwoFlakyTests_NoMergeFreeze()
    {
        var histories = new Dictionary<string, IReadOnlyList<TestRunRecord>>
        {
            ["testA"] = CreateHistory(3, 10),
            ["testB"] = CreateHistory(2, 10),
            ["testC"] = CreateHistory(0, 10),
        };

        var report = Detector.Scan(histories, DateTimeOffset.UtcNow);

        await Assert.That(report.OffenderCount).IsEqualTo(2);
        await Assert.That(report.MergeFreeze).IsFalse();
    }

    [Test]
    public async Task Scan_ThreeFlakyTests_TriggersMergeFreeze()
    {
        var histories = new Dictionary<string, IReadOnlyList<TestRunRecord>>
        {
            ["testA"] = CreateHistory(3, 10),
            ["testB"] = CreateHistory(2, 10),
            ["testC"] = CreateHistory(4, 10),
        };

        var report = Detector.Scan(histories, DateTimeOffset.UtcNow);

        await Assert.That(report.OffenderCount).IsEqualTo(3);
        await Assert.That(report.MergeFreeze).IsTrue();
    }

    [Test]
    public async Task Scan_NoFlakyTests_ReturnsEmpty()
    {
        var histories = new Dictionary<string, IReadOnlyList<TestRunRecord>>
        {
            ["testA"] = CreateHistory(0, 10),
            ["testB"] = CreateHistory(1, 10),
        };

        var report = Detector.Scan(histories, DateTimeOffset.UtcNow);

        await Assert.That(report.OffenderCount).IsEqualTo(0);
        await Assert.That(report.MergeFreeze).IsFalse();
    }

    [Test]
    public async Task Scan_WithDeadlineBreach_ReportsBreach()
    {
        var now = new DateTimeOffset(2026, 5, 10, 0, 0, 0, TimeSpan.Zero);
        var histories = new Dictionary<string, IReadOnlyList<TestRunRecord>>
        {
            ["testA"] = CreateHistory(3, 10),
        };

        var report = Detector.Scan(histories, now);

        await Assert.That(report.OffenderCount).IsEqualTo(1);
    }

    private static List<TestRunRecord> CreateHistory(int failureCount, int totalCount)
    {
        var list = new List<TestRunRecord>(totalCount);
        for (int i = 0; i < totalCount; i++)
        {
            list.Add(new TestRunRecord(i >= failureCount, DateTimeOffset.UtcNow.AddMinutes(-totalCount + i)));
        }
        return list;
    }
}
