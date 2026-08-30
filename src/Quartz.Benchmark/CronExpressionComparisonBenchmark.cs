using BenchmarkDotNet.Attributes;

using NCrontab;

namespace Quartz.Benchmark;

/// <summary>
/// The cron cases a published cross-library comparison reports Quartz numbers for, measured on this
/// branch and beside the library it was compared against.
/// </summary>
/// <remarks>
/// <para>
/// TickerQ's <c>CronExpressionComparison</c> puts <see cref="Quartz.CronExpression.GetNextValidTimeAfter" />
/// at roughly 1.2 µs and 3 KB a call against NCrontab's 13 ns and nothing, and construction at 3-4 µs
/// and 9-13 KB. That run was taken against Quartz 3.14, which predates the bitmask cron fields
/// (#3126-#3129) and 4.0's rebuilt <see cref="Quartz.CronExpression" />, and nobody had re-measured. This
/// is that measurement, kept deliberately close to the published one so the two tables can be read
/// against each other: the same three schedules in both dialects, the same fixed instant, the same
/// operations, and NCrontab running in the same process so both rows come off one table on one machine
/// rather than off two tables on two.
/// </para>
/// <para>
/// Neither library is handed a time zone, exactly as the published comparison leaves them, so
/// <see cref="Quartz.CronExpression" /> resolves against the machine's local zone while NCrontab has no
/// notion of one. Whether that zone observes daylight saving decides whether the interval expressions
/// take the fall-back pass, so the zone belongs beside any number taken from this;
/// <c>src/Quartz.Benchmark/README.md</c> records it with the rest of the machine.
/// </para>
/// <para>
/// This measures production code against a reference implementation rather than two arms of ours, so
/// there is nothing to switch on: the comparison is between the rows of a single run.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class CronExpressionComparisonBenchmark
{
    /// <summary>Every five minutes, in NCrontab's five-field form.</summary>
    private const string SimpleNCrontab = "*/5 * * * *";

    /// <summary>Every five minutes, in Quartz's seven-field form.</summary>
    private const string SimpleQuartz = "0 0/5 * * * ?";

    /// <summary>On the hour through the working day, Monday to Friday.</summary>
    private const string ComplexNCrontab = "0 9-17 * * 1-5";

    /// <inheritdoc cref="ComplexNCrontab" />
    private const string ComplexQuartz = "0 0 9-17 ? * MON-FRI";

    /// <summary>Every thirty seconds, which is the finest resolution either dialect reaches.</summary>
    private const string SecondLevelNCrontab = "*/30 * * * * *";

    /// <inheritdoc cref="SecondLevelNCrontab" />
    private const string SecondLevelQuartz = "0/30 * * * * ?";

    /// <summary>
    /// NCrontab reads a five-field expression by default; the second-level one is six, and says so.
    /// </summary>
    private static readonly CrontabSchedule.ParseOptions secondLevelOptions = new CrontabSchedule.ParseOptions { IncludingSeconds = true };

    /// <summary>
    /// The instant the published comparison searches from. It is a Monday noon, so the weekday
    /// expression's next fire is later the same day and no case is measuring a walk over a weekend.
    /// </summary>
    private static readonly DateTime baseTime = new DateTime(2026, 3, 16, 12, 0, 0, DateTimeKind.Utc);

    /// <inheritdoc cref="baseTime" />
    private static readonly DateTimeOffset baseTimeOffset = new DateTimeOffset(baseTime, TimeSpan.Zero);

    private CronExpression quartzSimple = null!;
    private CronExpression quartzComplex = null!;
    private CronExpression quartzSecondLevel = null!;
    private CrontabSchedule ncrontabSimple = null!;
    private CrontabSchedule ncrontabComplex = null!;
    private CrontabSchedule ncrontabSecondLevel = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        quartzSimple = new CronExpression(SimpleQuartz);
        quartzComplex = new CronExpression(ComplexQuartz);
        quartzSecondLevel = new CronExpression(SecondLevelQuartz);

        ncrontabSimple = CrontabSchedule.Parse(SimpleNCrontab);
        ncrontabComplex = CrontabSchedule.Parse(ComplexNCrontab);
        ncrontabSecondLevel = CrontabSchedule.Parse(SecondLevelNCrontab, secondLevelOptions);
    }

    [Benchmark]
    public CronExpression Parse_Simple() => new CronExpression(SimpleQuartz);

    [Benchmark]
    public CrontabSchedule Parse_Simple_NCrontab() => CrontabSchedule.Parse(SimpleNCrontab);

    [Benchmark]
    public CronExpression Parse_Complex() => new CronExpression(ComplexQuartz);

    [Benchmark]
    public CrontabSchedule Parse_Complex_NCrontab() => CrontabSchedule.Parse(ComplexNCrontab);

    [Benchmark]
    public CronExpression Parse_SecondLevel() => new CronExpression(SecondLevelQuartz);

    [Benchmark]
    public CrontabSchedule Parse_SecondLevel_NCrontab() => CrontabSchedule.Parse(SecondLevelNCrontab, secondLevelOptions);

    [Benchmark]
    public DateTimeOffset? Next_Simple() => quartzSimple.GetNextValidTimeAfter(baseTimeOffset);

    [Benchmark]
    public DateTime Next_Simple_NCrontab() => ncrontabSimple.GetNextOccurrence(baseTime);

    [Benchmark]
    public DateTimeOffset? Next_Complex() => quartzComplex.GetNextValidTimeAfter(baseTimeOffset);

    [Benchmark]
    public DateTime Next_Complex_NCrontab() => ncrontabComplex.GetNextOccurrence(baseTime);

    [Benchmark]
    public DateTimeOffset? Next_SecondLevel() => quartzSecondLevel.GetNextValidTimeAfter(baseTimeOffset);

    [Benchmark]
    public DateTime Next_SecondLevel_NCrontab() => ncrontabSecondLevel.GetNextOccurrence(baseTime);

    /// <summary>
    /// A hundred fires chained off each other, which is the shape a running trigger has and the case
    /// where a per-call allocation shows up as something an application would notice.
    /// </summary>
    [Benchmark]
    public DateTimeOffset? Next100()
    {
        DateTimeOffset current = baseTimeOffset;

        for (int i = 0; i < 100; i++)
        {
            DateTimeOffset? next = quartzSimple.GetNextValidTimeAfter(current);
            if (next is null)
            {
                return null;
            }

            current = next.Value;
        }

        return current;
    }

    /// <inheritdoc cref="Next100" />
    [Benchmark]
    public DateTime Next100_NCrontab()
    {
        DateTime current = baseTime;

        for (int i = 0; i < 100; i++)
        {
            current = ncrontabSimple.GetNextOccurrence(current);
        }

        return current;
    }
}
