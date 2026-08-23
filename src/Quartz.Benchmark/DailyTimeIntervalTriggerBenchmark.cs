using BenchmarkDotNet.Attributes;

using Quartz.Impl.Triggers;

namespace Quartz.Benchmark;

/// <summary>
/// Computing the next fire times of a daily-time-interval trigger, which is where the day-of-week set
/// the schedule builder produced is actually read.
/// </summary>
/// <remarks>
/// <para>
/// The set itself is built once, when the trigger is built, so what a different collection type there
/// could be worth depends entirely on how the set is consulted afterwards. That is this benchmark:
/// <c>AdvanceToNextDayOfWeekIfNecessary</c> runs on the way to every fire time, and on a schedule that
/// does not run every day it walks up to seven days asking the set about each.
/// </para>
/// <para>
/// <see cref="BuildTrigger" /> is the other side of the same question — how often the build-time cost
/// is paid — and is here so the two can be weighed against each other rather than guessed at.
/// </para>
/// <para>
/// Measures production code, so a before/after is two runs rather than two arms.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class DailyTimeIntervalTriggerBenchmark
{
    /// <summary>
    /// Every day is the case that never has to walk; Monday-to-Friday walks over the weekend; a single
    /// day walks up to six days out of every seven.
    /// </summary>
    [Params("EveryDay", "WeekDays", "MondayOnly")]
    public string Days { get; set; } = "EveryDay";

    private static readonly DateTimeOffset start = new DateTime(2024, 3, 1, 0, 0, 0, DateTimeKind.Utc);

    private DailyTimeIntervalTriggerImpl trigger = null!;

    [GlobalSetup]
    public void Setup()
    {
        trigger = (DailyTimeIntervalTriggerImpl) Builder().Build();
        trigger.ComputeFirstFireTimeUtc(null);
    }

    private DailyTimeIntervalScheduleBuilder Schedule()
    {
        DailyTimeIntervalScheduleBuilder schedule = DailyTimeIntervalScheduleBuilder.Create()
            .StartingDailyAt(new TimeOnly(9, 0))
            .EndingDailyAt(new TimeOnly(17, 0))
            .WithInterval(2, IntervalUnit.Hour);

        return Days switch
        {
            "EveryDay" => schedule.OnEveryDay(),
            "WeekDays" => schedule.OnMondayThroughFriday(),
            _ => schedule.OnDaysOfTheWeek(DayOfWeek.Monday),
        };
    }

    private TriggerBuilder<IJob> Builder()
    {
        return TriggerBuilder.Create()
            .WithIdentity("daily", "group")
            .StartAt(start)
            .WithDailyTimeIntervalSchedule(Schedule());
    }

    /// <summary>A hundred consecutive fire times, which is what a running schedule asks for.</summary>
    [Benchmark]
    public DateTimeOffset? NextOccurrences100()
    {
        DateTimeOffset? next = start;
        for (int i = 0; i < 100; i++)
        {
            next = trigger.GetFireTimeAfter(next);
            if (next is null)
            {
                break;
            }
        }

        return next;
    }

    /// <summary>Building the trigger, which is where the day set is created.</summary>
    [Benchmark]
    public ITrigger BuildTrigger()
    {
        return Builder().Build();
    }
}
