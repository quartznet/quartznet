using System;
using System.Collections.Generic;

using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

/// <summary>
/// Measures cron parsing and next-fire-time computation separately so the
/// effect of the bitmask fast path is visible on each. Run on a baseline commit
/// and again after the change, then compare the Mean and Allocated columns.
/// </summary>
[MemoryDiagnoser]
public class CronExpressionBenchmark
{
    [ParamsSource(nameof(CronExpressionValues))]
    public string CronExpression { get; set; } = "";

    public IEnumerable<string> CronExpressionValues =>
    [
        // simple / single value
        "0 15 10 * * ?",
        "0 0 12 * * ?",
        // every-N / step
        "0 0/5 * * * ?",
        "0/15 * * * * ?",
        // ranges
        "0 0-30 9-17 * * ?",
        "0 0 8-18 ? * MON-FRI",
        // many values (where the SortedSet GetViewBetween path hurt most)
        "0 0,10,20,30,40,50 * * * ?",
        "0 15 10 1,2,3,4,5,10,15,20,25 * ? *",
        // last day / nearest weekday / nth weekday
        "0 15 10 L * ?",
        "0 15 10 L-2 * ?",
        "0 15 10 LW * ?",
        "0 15 10 ? * 6#3 *",
        "0 15 10 ? * 6L",
        // year-constrained (exercises the year field, intentionally kept on the set path)
        "0 15 10 * * ? 2005-2025"
    ];

    private CronExpression expression = null!;

    // Mid-year start; the next occurrence usually lies a short distance away.
    private static readonly DateTimeOffset Start = new DateTime(2005, 6, 1, 22, 15, 0);

    [GlobalSetup]
    public void Setup()
    {
        expression = new CronExpression(CronExpression);
    }

    [Benchmark]
    public CronExpression Parse()
    {
        return new CronExpression(CronExpression);
    }

    [Benchmark]
    public DateTimeOffset? NextOccurrence()
    {
        return expression.GetNextValidTimeAfter(Start);
    }

    /// <summary>
    /// Chains 100 next-fire computations, the most representative of real
    /// scheduler load and the clearest amplifier of the per-call win and the
    /// per-call allocations.
    /// </summary>
    [Benchmark]
    public DateTimeOffset? NextOccurrences100()
    {
        DateTimeOffset? next = Start;
        for (int i = 0; i < 100; i++)
        {
            next = expression.GetNextValidTimeAfter(next.Value);
            if (next is null)
            {
                break;
            }
        }

        return next;
    }
}
