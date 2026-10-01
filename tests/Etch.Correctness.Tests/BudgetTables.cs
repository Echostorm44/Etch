using System;
using System.Collections.Generic;
using System.IO;
using Etch.Correctness.Tests.Memory;
using Etch.Correctness.Tests.Perf;
using TUnit;

namespace Etch.Correctness.Tests;

/// <summary>
/// Where the memory and performance budget tests read their budgets from. ProjectPlan.md owns the
/// numbers but is gitignored, so on CI (or any fresh clone) it does not exist and every parser test
/// saw zero rows. The tests parse a tracked copy of its budget tables instead.
/// </summary>
internal static class BudgetTables
{
    public static string TrackedCopyPath => Path.Combine(TestRepoRoot.Path, "tests", "Etch.Correctness.Tests", "ProjectPlanBudgets.md");

    public static string ProjectPlanPath => Path.Combine(TestRepoRoot.Path, "ProjectPlan.md");
}

public class BudgetTablesTests
{
    [Test]
    public async Task TrackedCopy_MatchesProjectPlan()
    {
        if (!File.Exists(BudgetTables.ProjectPlanPath))
        {
            Skip.Test("ProjectPlan.md is not part of this checkout (it is gitignored); nothing to compare the tracked copy against.");
        }

        string planMemory = DescribeMemoryRows(MemoryBudgetParser.Parse(BudgetTables.ProjectPlanPath));
        string copyMemory = DescribeMemoryRows(MemoryBudgetParser.Parse(BudgetTables.TrackedCopyPath));
        await Assert.That(copyMemory).IsEqualTo(planMemory);

        string planPerf = DescribePerfRows(PerfRegressionParser.Parse(BudgetTables.ProjectPlanPath));
        string copyPerf = DescribePerfRows(PerfRegressionParser.Parse(BudgetTables.TrackedCopyPath));
        await Assert.That(copyPerf).IsEqualTo(planPerf);
    }

    private static string DescribeMemoryRows(List<MemoryBudgetRow> rows)
    {
        var described = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            described.Add($"{row.Scenario} | {row.BudgetBytes} | {row.Notes}");
        }
        return string.Join(Environment.NewLine, described);
    }

    private static string DescribePerfRows(List<PerfBudgetRow> rows)
    {
        var described = new List<string>(rows.Count);
        foreach (var row in rows)
        {
            described.Add($"{row.Section} | {row.Scenario} | {row.Target} | {row.Notes}");
        }
        return string.Join(Environment.NewLine, described);
    }
}
