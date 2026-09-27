using BenchmarkDotNet.Attributes;

using Quartz.Benchmark.Competitors.Engines;

namespace Quartz.Benchmark.Competitors;

/// <summary>Which call an S5 row measures.</summary>
public enum S5Arm
{
    /// <summary><c>IScheduler.ScheduleJob(job, trigger)</c> with a simple trigger.</summary>
    QuartzSimple,

    /// <summary><c>IScheduler.ScheduleJob(job, trigger)</c> with a cron trigger.</summary>
    QuartzCron,

    /// <summary><c>ITimeTickerManager.AddAsync</c>.</summary>
    TickerQTime,

    /// <summary><c>ICronTickerManager.AddAsync</c>.</summary>
    TickerQCron,

    /// <summary>What <c>BackgroundJob.Schedule</c> calls.</summary>
    HangfireSchedule,

    /// <summary>What <c>RecurringJob.AddOrUpdate</c> calls.</summary>
    HangfireRecurring,

    /// <summary>
    /// The job detail and trigger <see cref="QuartzSimple" /> builds, and nothing stored: its builder
    /// half.
    /// </summary>
    QuartzSimpleBuildOnly,

    /// <summary>
    /// <see cref="QuartzSimple" />'s <c>ScheduleJob</c> alone, over job details and triggers built before
    /// the measurement: its scheduler half.
    /// </summary>
    QuartzSimplePrebuilt,
}

/// <summary>
/// S5 — what it costs to put one schedule into an empty store, through the API each library teaches.
/// </summary>
/// <remarks>
/// <para>
/// One invocation is fifty thousand schedules into a store that started empty, which is
/// <c>ScheduleJobBenchmark</c>'s arrangement and is what makes the row comparable with the numbers
/// already in <c>Quartz.Benchmark/README.md</c>. Each schedule is due an hour out, so nothing fires
/// while the measurement is running and what is measured is the write rather than the firing.
/// </para>
/// <para>
/// The engine is rebuilt between iterations rather than cleared, on every arm, because the three
/// libraries' clears cost very different amounts and an unmeasured rebuild is the same for all of
/// them.
/// </para>
/// <para>
/// <b>This row is published whether or not Quartz wins it</b>, which is the point of it: a harness that
/// only shows the tables its author wins is the thing this one exists to answer.
/// </para>
/// <para>
/// <b>The last two arms split the Quartz simple row</b> into what building the job detail and trigger
/// costs and what <c>ScheduleJob</c> costs once they exist, because the row measures both and the two
/// are cut in different places (#3865). They are Quartz's own and are not in the comparison table.
/// </para>
/// </remarks>
[MemoryDiagnoser]
[Config(typeof(ScheduleCostConfig))]
public class S5ScheduleCostBenchmark
{
    /// <summary>How many schedules one invocation writes.</summary>
    public const int Schedules = 50_000;

    private IEngine engine = null!;
    private DateTimeOffset dueAt;
    private (IJobDetail Job, ITrigger Trigger)[] prebuilt = [];
    private (IJobDetail Job, ITrigger Trigger) lastBuilt;

    [Params(
        S5Arm.QuartzSimple,
        S5Arm.QuartzCron,
        S5Arm.TickerQTime,
        S5Arm.TickerQCron,
        S5Arm.HangfireSchedule,
        S5Arm.HangfireRecurring,
        S5Arm.QuartzSimpleBuildOnly,
        S5Arm.QuartzSimplePrebuilt)]
    public S5Arm Arm { get; set; }

    [IterationSetup]
    public void IterationSetup()
    {
        engine = Create(Arm);
        engine.Start(Harness.MaxConcurrency).AsTask().GetAwaiter().GetResult();
        dueAt = DateTimeOffset.UtcNow.AddHours(1);

        if (Arm == S5Arm.QuartzSimplePrebuilt)
        {
            prebuilt = new (IJobDetail Job, ITrigger Trigger)[Schedules];
            for (int i = 0; i < Schedules; i++)
            {
                prebuilt[i] = QuartzEngine.BuildOne(i, cron: false, dueAt);
            }
        }
    }

    [IterationCleanup]
    public void IterationCleanup()
    {
        engine.DisposeAsync().AsTask().GetAwaiter().GetResult();
        prebuilt = [];
    }

    [Benchmark(OperationsPerInvoke = Schedules)]
    public void Schedule()
    {
        switch (Arm)
        {
            case S5Arm.QuartzSimpleBuildOnly:
            {
                for (int i = 0; i < Schedules; i++)
                {
                    // Kept in a field so that nothing built here can be proven unused and elided.
                    lastBuilt = QuartzEngine.BuildOne(i, cron: false, dueAt);
                }

                break;
            }

            case S5Arm.QuartzSimplePrebuilt:
            {
                QuartzEngine quartz = (QuartzEngine) engine;
                for (int i = 0; i < Schedules; i++)
                {
                    quartz.ScheduleBuilt(prebuilt[i].Job, prebuilt[i].Trigger).AsTask().GetAwaiter().GetResult();
                }

                break;
            }

            case S5Arm.QuartzSimple:
            case S5Arm.QuartzCron:
            {
                QuartzEngine quartz = (QuartzEngine) engine;
                bool cron = Arm == S5Arm.QuartzCron;
                for (int i = 0; i < Schedules; i++)
                {
                    quartz.ScheduleOne(i, cron, dueAt).AsTask().GetAwaiter().GetResult();
                }

                break;
            }

            case S5Arm.TickerQTime:
            case S5Arm.TickerQCron:
            {
                TickerQEngine ticker = (TickerQEngine) engine;
                bool cron = Arm == S5Arm.TickerQCron;
                for (int i = 0; i < Schedules; i++)
                {
                    ticker.ScheduleOne(i, cron, dueAt).AsTask().GetAwaiter().GetResult();
                }

                break;
            }

            default:
            {
                HangfireEngine hangfire = (HangfireEngine) engine;
                bool recurring = Arm == S5Arm.HangfireRecurring;
                for (int i = 0; i < Schedules; i++)
                {
                    hangfire.ScheduleOne(i, recurring, dueAt);
                }

                break;
            }
        }
    }

    internal static IEngine Create(S5Arm arm) => arm switch
    {
        S5Arm.QuartzSimple => new QuartzEngine(QuartzProfile.Defaults, "S5Simple", quartz => quartz.UseInMemoryStore()),
        S5Arm.QuartzCron => new QuartzEngine(QuartzProfile.Defaults, "S5Cron", quartz => quartz.UseInMemoryStore()),
        S5Arm.QuartzSimpleBuildOnly => new QuartzEngine(QuartzProfile.Defaults, "S5BuildOnly", quartz => quartz.UseInMemoryStore()),
        S5Arm.QuartzSimplePrebuilt => new QuartzEngine(QuartzProfile.Defaults, "S5Prebuilt", quartz => quartz.UseInMemoryStore()),
        S5Arm.TickerQTime or S5Arm.TickerQCron => new TickerQEngine("TickerQ", TimeSpan.FromSeconds(1)),

        // Hangfire's own default poll interval: nothing is due for an hour, so the poll is not part of
        // what this row measures and there is no reason to move it off the shipped value.
        _ => new HangfireEngine("Hangfire", HangfireEngine.InMemory, TimeSpan.FromSeconds(15)),
    };
}
