using BenchmarkDotNet.Attributes;

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Benchmark;

/// <summary>
/// What <c>RAMJobStore.TriggersFired</c> spends advancing one firing's two trigger instances - its own
/// and the caller's copy - under the store's lock.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="TwoComputations" /> is what the store did before #3866: <c>Triggered</c> on each
/// instance. <see cref="OneComputation" /> is what it does now, through <c>TriggerCopyFiring</c>: a cron
/// trigger's stored instance is fired and the copy is handed the fields the call wrote, once the two
/// have been checked to hold the same state. Both arms are in one build, so the difference is read from
/// one run rather than from two builds.
/// </para>
/// <para>
/// The simple trigger is here as the reason it is left out: its advance is a division, which costs what
/// the comparison would, so <c>TriggerCopyFiring</c> fires it twice and this row measures that fallback.
/// </para>
/// <para>
/// Each operation advances both instances by one occurrence, so the pairs walk forward through their
/// schedules for the whole run - a minute at a time, or a cron second at a time, which a run cannot
/// exhaust.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class TriggerCopyFiringBenchmark
{
    /// <summary>The trigger type the shared computation covers, and the one it deliberately does not.</summary>
    [Params("Simple", "Cron")]
    public string Trigger { get; set; } = "Simple";

    private IOperableTrigger twiceStored = null!;
    private IOperableTrigger twiceCopy = null!;
    private IOperableTrigger onceStored = null!;
    private IOperableTrigger onceCopy = null!;

    [GlobalSetup]
    public void GlobalSetup()
    {
        IOperableTrigger template = Trigger == "Cron" ? Cron() : Simple();

        twiceStored = (IOperableTrigger) template.Clone();
        twiceCopy = (IOperableTrigger) template.Clone();
        onceStored = (IOperableTrigger) template.Clone();
        onceCopy = (IOperableTrigger) template.Clone();
    }

    [Benchmark(Baseline = true)]
    public void TwoComputations()
    {
        twiceStored.Triggered(null);
        twiceCopy.Triggered(null);
    }

    [Benchmark]
    public bool OneComputation()
    {
        return TriggerCopyFiring.Triggered(onceStored, onceCopy, calendar: null);
    }

    private static SimpleTriggerImpl Simple()
    {
        SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey("simple"),
            JobKey = new JobKey("job"),
            StartTimeUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RepeatCount = SimpleTriggerImpl.RepeatIndefinitely,
            RepeatInterval = TimeSpan.FromMinutes(1)
        };

        trigger.ComputeFirstFireTimeUtc(null);
        return trigger;
    }

    private static CronTriggerImpl Cron()
    {
        CronTriggerImpl trigger = new()
        {
            Key = new TriggerKey("cron"),
            JobKey = new JobKey("job"),
            CronExpression = new CronExpression("0/1 * * * * ?", TimeZoneInfo.Utc),
            StartTimeUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
        };

        trigger.NextFireTimeUtc = trigger.GetFireTimeAfter(trigger.StartTimeUtc);
        return trigger;
    }
}
