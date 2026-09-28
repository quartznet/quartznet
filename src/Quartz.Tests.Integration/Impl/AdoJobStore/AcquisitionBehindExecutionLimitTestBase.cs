using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A node acquiring one trigger at a time keeps firing its ordinary trigger on time while the earliest
/// due row belongs to an execution group at its limit (#3928).
/// </summary>
/// <remarks>
/// <para>
/// With <c>MaxBatchSize</c> 1 the acquisition statement returns one row, and the delegate refused it
/// for its group's limit after the statement had already limited the rows. The store saw an empty
/// read, took it for nothing due, and idled for its whole idle wait while the ordinary trigger came
/// due behind the refused row — every round, for as long as the group stayed full. The delegate now
/// returns the refused row flagged and the store reads past it.
/// </para>
/// <para>
/// One node. A job in the limited group parks inside <c>Execute</c> until the test opens its gate, so
/// the group's one slot stays taken for the whole observation, while its second trigger sits due and
/// refused at the head of the fire-time order. The ticking trigger due every second beside it is what
/// is measured: how late each of its firings was. The idle wait is long enough that idling behind the
/// refused row is unmistakable, and the tolerance is a second.
/// </para>
/// </remarks>
public abstract class AcquisitionBehindExecutionLimitTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "acquisitionBehindLimit";
    private const string Tenant = "tenant-acme";
    private const string Node = "behind-limit-node";

    /// <summary>How long the node runs once everything is scheduled.</summary>
    private static readonly TimeSpan observation = TimeSpan.FromSeconds(12);

    /// <summary>How late a ticking firing may be and still count as on time.</summary>
    private static readonly TimeSpan lateAllowed = TimeSpan.FromSeconds(1);

    /// <summary>
    /// How many firings of a trigger due every second the node has to show for the observation: the
    /// rest is start-up, the first acquisition and the shutdown.
    /// </summary>
    private static readonly int firingsExpected = (int) observation.TotalSeconds - 4;

    protected AcquisitionBehindExecutionLimitTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "AcquisitionBehindLimitTest";

    [SetUp]
    public void ResetRecords()
    {
        TickingJob.Reset();
        HeldJob.Reset();
    }

    [TearDown]
    public void OpenGate() => HeldJob.Open();

    [Test]
    public async Task ANodeAcquiringOneTriggerAtATimeFiresOnTimeBehindAGroupAtItsLimit()
    {
        static void ConfigureNode(NameValueCollection properties)
        {
            // One slot for the tenant on this node, which the held job takes for the whole run.
            properties["quartz.executionLimit." + Tenant] = "1";
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "1";
            // Long enough that idling behind a refused row shows as seconds of lateness rather than the
            // base fixture's two, and short enough that the run is not.
            properties["quartz.scheduler.idleWaitTime"] = "10000";
            properties["quartz.threadPool.maxConcurrency"] = "4";
        }

        IScheduler node = await CreateScheduler(Node, configure: ConfigureNode);

        try
        {
            IJobDetail heldJob = JobBuilder.Create<HeldJob>()
                .WithIdentity("held", Group)
                .StoreDurably()
                .Build();
            await node.AddJob(heldJob, new AddJobOptions { Replace = true });

            IJobDetail tickingJob = JobBuilder.Create<TickingJob>()
                .WithIdentity("ticking", Group)
                .StoreDurably()
                .Build();
            await node.AddJob(tickingJob, new AddJobOptions { Replace = true });

            // Two triggers of the held job, both due now: the first takes the tenant's slot and keeps
            // it, the second stays due and refused at the head of the order for the whole run.
            for (int i = 0; i < 2; i++)
            {
                await node.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity("held-" + i.ToString(CultureInfo.InvariantCulture), Group)
                    .ForJob(heldJob)
                    .WithExecutionGroup(Tenant)
                    .StartNow()
                    .Build());
            }

            await node.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity("ticking", Group)
                .ForJob(tickingJob)
                .StartNow()
                .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                .Build());

            await node.Start();

            await WaitForCondition(
                () => Task.FromResult(HeldJob.InFlight == 1),
                timeoutMs: 15_000,
                async () => $"the held job to take the tenant's slot. State:\n{await DumpDatabaseState()}");

            await Task.Delay(observation);
        }
        finally
        {
            HeldJob.Open();
            await node.Shutdown(waitForJobsToComplete: false);
        }

        List<TimeSpan> lateness = [.. TickingJob.Lateness];
        string report = string.Create(CultureInfo.InvariantCulture,
            $"{lateness.Count} firings, {(lateness.Count == 0 ? 0 : lateness.Average(x => x.TotalMilliseconds)):F0} ms late on average, "
            + $"{(lateness.Count == 0 ? 0 : lateness.Max(x => x.TotalMilliseconds)):F0} ms at worst");
        TestContext.Out.WriteLine(report);

        HeldJob.Peak.Should().Be(1, "the premise: the tenant's one slot was taken throughout, and the limit held");
        lateness.Count.Should().BeGreaterThanOrEqualTo(firingsExpected,
            "a trigger due every second, on a node that idles behind a refused row for its whole idle wait, fires nothing while it idles: {0}",
            report);
        lateness.Max().Should().BeLessThanOrEqualTo(lateAllowed,
            "a node reading one trigger at a time must read past a group at its limit to the trigger due behind it, "
            + "rather than take the refused row for the end of what is due and wait out its idle time: {0}",
            report);
    }

    /// <summary>
    /// Records how far behind its scheduled time each firing was.
    /// </summary>
    public sealed class TickingJob : IJob
    {
        private static volatile ConcurrentQueue<TimeSpan> lateness = new();

        public static ConcurrentQueue<TimeSpan> Lateness => lateness;

        public static void Reset() => Interlocked.Exchange(ref lateness, new ConcurrentQueue<TimeSpan>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Lateness.Enqueue(DateTimeOffset.UtcNow - (context.ScheduledFireTimeUtc ?? context.FireTimeUtc));
            return default;
        }
    }

    /// <summary>
    /// Parks inside <c>Execute</c> until the test opens the gate, holding its execution group's slot.
    /// </summary>
    public sealed class HeldJob : IJob
    {
        private static volatile TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private static int inFlight;
        private static int peak;

        public static int InFlight => Volatile.Read(ref inFlight);

        public static int Peak => Volatile.Read(ref peak);

        public static void Reset()
        {
            Interlocked.Exchange(ref gate, new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously));
            Interlocked.Exchange(ref inFlight, 0);
            Interlocked.Exchange(ref peak, 0);
        }

        public static void Open() => gate.TrySetResult();

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            int inside = Interlocked.Increment(ref inFlight);
            int observed = Volatile.Read(ref peak);
            while (inside > observed && Interlocked.CompareExchange(ref peak, inside, observed) != observed)
            {
                observed = Volatile.Read(ref peak);
            }

            try
            {
                await gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref inFlight);
            }
        }
    }
}
