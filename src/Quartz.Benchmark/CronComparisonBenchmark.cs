using System;
using System.Collections.Generic;

using BenchmarkDotNet.Attributes;

using CronosCron = Cronos.CronExpression;
using QuartzCron = Quartz.CronExpression;

namespace Quartz.Benchmark;

/// <summary>
/// Head-to-head against the Cronos library as an external yardstick for cron
/// parsing and next-occurrence performance.
/// </summary>
/// <remarks>
/// Restricted to a 6-field, year-free subset of features both libraries support
/// with identical semantics (simple, step, range, day-of-week, specific days).
/// Quartz's year field and the L / W / # modifiers are intentionally excluded —
/// Cronos either lacks them or interprets them differently, so including them
/// would compare apples to oranges.
/// </remarks>
[MemoryDiagnoser]
public class CronComparisonBenchmark
{
    /// <summary>Equivalent Quartz and Cronos expressions for one comparison row.</summary>
    public sealed class CronCase
    {
        public CronCase(string label, string quartz, string cronos)
        {
            Label = label;
            Quartz = quartz;
            Cronos = cronos;
        }

        public string Label { get; }
        public string Quartz { get; }
        public string Cronos { get; }

        public override string ToString() => Label;
    }

    public IEnumerable<CronCase> Cases =>
    [
        new CronCase("every-minute", "0 * * * * ?", "0 * * * * *"),
        new CronCase("daily-noon", "0 0 12 * * ?", "0 0 12 * * *"),
        new CronCase("every-5-min", "0 0/5 * * * ?", "0 0/5 * * * *"),
        new CronCase("hours-range", "0 0 0-8 * * ?", "0 0 0-8 * * *"),
        new CronCase("weekdays", "0 0 12 ? * MON-FRI", "0 0 12 * * 1-5"),
        new CronCase("specific-days", "0 0 12 1,15 * ?", "0 0 12 1,15 * *")
    ];

    [ParamsSource(nameof(Cases))]
    public CronCase Case { get; set; } = null!;

    private QuartzCron quartzExpression = null!;
    private CronosCron cronosExpression = null!;

    private static readonly DateTime StartUtc = new(2024, 6, 1, 22, 15, 0, DateTimeKind.Utc);

    [GlobalSetup]
    public void Setup()
    {
        quartzExpression = new QuartzCron(Case.Quartz) { TimeZone = TimeZoneInfo.Utc };
        cronosExpression = CronosCron.Parse(Case.Cronos, Cronos.CronFormat.IncludeSeconds);
    }

    [Benchmark]
    public QuartzCron Quartz_Parse()
    {
        return new QuartzCron(Case.Quartz);
    }

    [Benchmark]
    public CronosCron Cronos_Parse()
    {
        return CronosCron.Parse(Case.Cronos, Cronos.CronFormat.IncludeSeconds);
    }

    [Benchmark]
    public DateTimeOffset? Quartz_GetNext()
    {
        return quartzExpression.GetNextValidTimeAfter(StartUtc);
    }

    [Benchmark]
    public DateTime? Cronos_GetNext()
    {
        return cronosExpression.GetNextOccurrence(StartUtc);
    }
}
