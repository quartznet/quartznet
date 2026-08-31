using BenchmarkDotNet.Attributes;

namespace Quartz.Benchmark;

/// <summary>
/// Building a job detail and a trigger, without storing either.
/// </summary>
/// <remarks>
/// <para>
/// These are the halves of <see cref="ScheduleJobBenchmark" />'s cases that happen before the
/// scheduler is called at all, and they are here rather than beside them because that class empties
/// its store between iterations and these are nanosecond-scale: a per-iteration setup would be most of
/// what they measured.
/// </para>
/// <para>
/// The pair is worth having because the published comparison's simple-versus-cron gap is mostly
/// decided here. A cron trigger parses its expression while it is being built and computes a first
/// fire time from it; a simple one does neither. Reading <see cref="BuildCronTrigger" /> against
/// <see cref="BuildSimpleTrigger" /> says how much of the gap the schedule costs, and what is left
/// belongs to the scheduler and the store.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class JobAndTriggerBuilderBenchmark
{
    /// <summary>Every five minutes, the schedule the published comparison uses.</summary>
    private const string CronSchedule = "0 0/5 * * * ?";

    private const string Group = "bench";

    private ITrigger cronTrigger = null!;

    [GlobalSetup(Target = nameof(ReadCronScheduleBackOffTrigger))]
    public void BuildTheTriggerToReadBack()
    {
        cronTrigger = TriggerBuilder.Create()
            .WithIdentity("trigger-read-back", Group)
            .WithCronSchedule(CronSchedule)
            .Build();
    }

    [Benchmark]
    public IJobDetail BuildJobDetail()
    {
        return JobBuilder.Create<NoOpJob>()
            .WithIdentity("job-build", Group)
            .UsingJobData("message", "hello")
            .UsingJobData("count", 42)
            .Build();
    }

    [Benchmark]
    public ITrigger BuildSimpleTrigger()
    {
        return TriggerBuilder.Create()
            .WithIdentity("trigger-build", Group)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(30))
            .Build();
    }

    [Benchmark]
    public ITrigger BuildCronTrigger()
    {
        return TriggerBuilder.Create()
            .WithIdentity("trigger-build", Group)
            .WithCronSchedule(CronSchedule)
            .Build();
    }

    /// <summary>
    /// Taking a cron trigger's schedule back off it, which is the first thing every reschedule does -
    /// <c>ITrigger.GetTriggerBuilder</c> is this call plus the trigger's identity.
    /// </summary>
    [Benchmark]
    public IScheduleBuilder ReadCronScheduleBackOffTrigger()
    {
        return cronTrigger.GetScheduleBuilder();
    }

    private sealed class NoOpJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            return default;
        }
    }
}
