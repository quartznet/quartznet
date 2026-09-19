using System.Globalization;

using BenchmarkDotNet.Attributes;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Triggers;
using Quartz.Jobs;
using Quartz.Tests;

namespace Quartz.Benchmark;

/// <summary>
/// What one trigger costs to add and to remove when a durable job already has many others behind it.
/// That is the shape <c>ScheduleJob&lt;TJob, TInput&gt;</c> produces — a durable job per job type and a
/// trigger per call, each trigger going away when it has fired — so it is the one-off API's steady
/// state under load rather than an odd arrangement (#3823).
/// </summary>
/// <remarks>
/// <para>
/// One operation adds a trigger and removes it again, so the store is in the same state at the end of
/// every invocation and the parameter really is "how many other triggers the job has". A store whose
/// per-job bookkeeping is a list walks it to find the trigger it is removing and then builds an array
/// of the remaining keys to ask whether the job is orphaned: both columns then grow with the parameter,
/// and the <c>Allocated</c> column grows by eight bytes per other trigger. A store that keys the
/// bookkeeping reads the same at every parameter value, which is the whole reading.
/// </para>
/// <para>
/// The base store is built once per parameter value in <c>[GlobalSetup]</c>, so nothing but the add and
/// the remove is measured.
/// </para>
/// </remarks>
[MemoryDiagnoser]
public class OneOffTriggerChurnBenchmark
{
    /// <summary>
    /// How many triggers the one job already has. Two thousand is the count the fire-throughput suite
    /// uses; twenty thousand is what #3823 measured at 83 KB a firing.
    /// </summary>
    [Params(2_000, 20_000)]
    public int TriggersBehindTheJob { get; set; }

    private const string Group = "oneOff";

    private RAMJobStore store = null!;
    private IOperableTrigger oneMore = null!;

    [GlobalSetup]
    public async Task Setup()
    {
        store = TestJobStores.Ram();
        await store.Initialize(TestJobStores.Identity()).ConfigureAwait(false);

        IJobDetail job = JobBuilder.Create<NoOpJob>()
            .WithIdentity("job", Group)
            .StoreDurably()
            .Build();

        await store.AddJob(job).ConfigureAwait(false);

        for (int i = 0; i < TriggersBehindTheJob; i++)
        {
            await store.AddTrigger(NewTrigger(job.Key, "trigger-" + i.ToString(CultureInfo.InvariantCulture))).ConfigureAwait(false);
        }

        oneMore = NewTrigger(job.Key, "one-more");
    }

    /// <summary>One more trigger for the job, and then that trigger gone again.</summary>
    [Benchmark]
    public async Task AddAndRemoveOneTrigger()
    {
        await store.AddTrigger(oneMore, AddTriggerOptions.Replacing).ConfigureAwait(false);
        await store.DeleteTrigger(oneMore.Key).ConfigureAwait(false);
    }

    private static IOperableTrigger NewTrigger(JobKey jobKey, string name)
    {
        SimpleTriggerImpl trigger = new()
        {
            Key = new TriggerKey(name, Group),
            JobKey = jobKey,
            StartTimeUtc = DateTimeOffset.UtcNow.AddHours(1),
        };
        trigger.ComputeFirstFireTimeUtc(calendar: null);
        return trigger;
    }
}
