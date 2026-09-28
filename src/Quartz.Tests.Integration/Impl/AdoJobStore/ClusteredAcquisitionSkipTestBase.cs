using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A node acquiring one trigger at a time keeps firing its ordinary triggers on time while a
/// <see cref="DisallowConcurrentExecutionAttribute" /> job runs on the other node (#3926).
/// </summary>
/// <remarks>
/// <para>
/// The scenario the G8 mixed-version gate (#3925) found: with <c>MaxBatchSize</c> 1 — 4.2's default,
/// and a setting on 4.3 — the earliest due row a node read belonged to a serial job executing on the
/// other node. That row was <c>WAITING</c> because the node's own reservation of it had been released
/// to <c>WAITING</c> after the other node's fire had blocked it; every acquisition then read that one
/// row, skipped it as executing, and gave up, and the node idled for its whole idle wait while its
/// own triggers came due behind it. Firings arrived seconds late and then in a burst.
/// </para>
/// <para>
/// <b>Both nodes are in this process</b>, so a firing on either is seen by the same static record.
/// The contention is induced: a concurrent job's triggers are due every fifth of a second, which keeps
/// both loops acquiring rather than sleeping between firings, and several triggers of one short
/// serial job are due every second, so whenever the job is between executions both nodes reserve one
/// of them and one of the two loses to the other's fire — the interleaving the issue describes, many
/// times over a run. Each node also owns a trigger pinned to it and due every second, which is what is
/// measured: how late each of its firings was against the time it was scheduled for.
/// </para>
/// <para>
/// The idle wait is set long enough that a node idling behind a skipped row is unmistakable, and the
/// tolerance is a second; with the fix reverted every firing after the first collision is late by the
/// idle wait, on both nodes.
/// </para>
/// </remarks>
public abstract class ClusteredAcquisitionSkipTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterAcquisitionSkip";
    private const string NodeA = "skip-node-a";
    private const string NodeB = "skip-node-b";
    private const int SerialTriggerCount = 6;
    private const int BusyTriggerCount = 4;

    /// <summary>How long both nodes run once everything is scheduled.</summary>
    private static readonly TimeSpan observation = TimeSpan.FromSeconds(15);

    /// <summary>How long a node sleeps when an acquisition comes back empty.</summary>
    private static readonly TimeSpan idleWait = TimeSpan.FromSeconds(10);

    /// <summary>
    /// How late a pinned firing may be and still count as on time: half the idle wait. A node that idles
    /// behind a skipped row is late by most of it (7.6-9.2 s with the fix reverted), while a shared CI
    /// database under the busy triggers' load puts a single firing well over a second late now and then
    /// (1.8 s on a SQL Server leg, with every firing made). The firing count below is the other half of
    /// the check: an idling node also makes fewer of them.
    /// </summary>
    private static readonly TimeSpan lateAllowed = idleWait / 2;

    /// <summary>
    /// How many firings of a trigger due every second each node has to show for the observation: the
    /// rest is start-up, the first acquisition and the shutdown.
    /// </summary>
    private static readonly int firingsExpected = (int) observation.TotalSeconds - 4;

    protected ClusteredAcquisitionSkipTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterAcquisitionSkipTest";

    [SetUp]
    public void ResetRecords()
    {
        PinnedJob.Reset();
        SerialJob.Reset();
    }

    [Test]
    public async Task ANodeAcquiringOneTriggerAtATimeFiresItsOwnTriggersOnTimeWhileASerialJobRunsElsewhere()
    {
        static void ConfigureNode(NameValueCollection properties)
        {
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = "1";
            // Long enough that idling behind a skipped row shows as seconds of lateness rather than
            // the base fixture's two, and short enough that the run is not.
            properties["quartz.scheduler.idleWaitTime"] = ((int) idleWait.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
            properties["quartz.threadPool.maxConcurrency"] = "4";
        }

        IScheduler nodeA = await CreateScheduler(NodeA, configure: ConfigureNode);
        IScheduler nodeB = await CreateScheduler(NodeB, configure: ConfigureNode);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail serialJob = JobBuilder.Create<SerialJob>()
                .WithIdentity("serial", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(serialJob, new AddJobOptions { Replace = true });

            IJobDetail pinnedJob = JobBuilder.Create<PinnedJob>()
                .WithIdentity("pinned", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(pinnedJob, new AddJobOptions { Replace = true });

            // One trigger per node, pinned to it, due every second: the firings whose lateness is
            // measured. Each is scheduled through its own node, because a scheduling change only wakes
            // the node it was made on: a node started with nothing due sleeps out its idle wait, and
            // that would be measured here as ten seconds of lateness that has nothing to do with the
            // serial job.
            foreach ((IScheduler node, string instanceId) in ((IScheduler, string)[]) [(nodeA, NodeA), (nodeB, NodeB)])
            {
                await node.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity("pinned-" + instanceId, Group)
                    .ForJob(pinnedJob)
                    .WithPreferredNode(PreferredNode.For(instanceId))
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                    .Build());
            }

            // Work that keeps both loops acquiring: a concurrent job due every fifth of a second, on
            // whichever node gets there first. A loop that only woke for its pinned trigger would be
            // asleep for the few milliseconds in which the serial job's rows are there to be reserved.
            IJobDetail busyJob = JobBuilder.Create<BusyJob>()
                .WithIdentity("busy", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(busyJob, new AddJobOptions { Replace = true });
            for (int i = 0; i < BusyTriggerCount; i++)
            {
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity($"busy-{i}", Group)
                    .ForJob(busyJob)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromMilliseconds(200)).RepeatForever())
                    .Build());
            }

            // Several triggers of the serial job, all due every second and all overdue from the moment
            // the job's execution unblocks them, so that both nodes reserve one whenever the job is
            // between executions and one of them loses to the other's fire.
            for (int i = 0; i < SerialTriggerCount; i++)
            {
                await nodeA.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity($"serial-{i}", Group)
                    .ForJob(serialJob)
                    .StartNow()
                    .WithSimpleSchedule(schedule => schedule.WithInterval(TimeSpan.FromSeconds(1)).RepeatForever())
                    .Build());
            }

            await Task.Delay(observation);
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }

        List<PinnedFiring> firings = [.. PinnedJob.Firings];
        string report = Report(firings);
        TestContext.Out.WriteLine(report);
        TestContext.Out.WriteLine($"Serial job: {SerialJob.Completed} completed, peak concurrency {SerialJob.PeakConcurrency}, "
                                  + "on " + string.Join(", ", SerialJob.Nodes.GroupBy(x => x, StringComparer.Ordinal).Select(x => $"{x.Key}={x.Count()}")));

        SerialJob.Completed.Should().BeGreaterThan(1, "the premise: the serial job was running throughout");
        SerialJob.PeakConcurrency.Should().Be(1, "the fix must not buy liveness by letting the serial job overlap itself");

        foreach (string node in (string[]) [NodeA, NodeB])
        {
            List<PinnedFiring> onNode = firings.FindAll(x => x.Node == node);

            onNode.Count.Should().BeGreaterThanOrEqualTo(firingsExpected,
                "'{0}' owns a trigger due every second, and a node that idles behind a row it skipped fires nothing while it idles:\n{1}",
                node, report);
            onNode.Max(x => x.Late).Should().BeLessThanOrEqualTo(lateAllowed,
                "a node reading one trigger at a time must read past the serial job's rows while the job runs elsewhere, "
                + "rather than read the first of them until its retries run out and wait out its idle time:\n{0}",
                report);
        }
    }

    private static string Report(List<PinnedFiring> firings)
    {
        return string.Join("\n", firings
            .GroupBy(x => x.Node, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => string.Create(CultureInfo.InvariantCulture,
                $"{x.Key}: {x.Count()} firings, {x.Average(f => f.Late.TotalMilliseconds):F0} ms late on average, {x.Max(f => f.Late.TotalMilliseconds):F0} ms at worst")));
    }

    public sealed record PinnedFiring(string Node, TimeSpan Late);

    /// <summary>
    /// Records, for every firing, the node it ran on and how far behind its scheduled time it was.
    /// </summary>
    public sealed class PinnedJob : IJob
    {
        private static volatile ConcurrentQueue<PinnedFiring> firings = new();

        public static ConcurrentQueue<PinnedFiring> Firings => firings;

        public static void Reset() => Interlocked.Exchange(ref firings, new ConcurrentQueue<PinnedFiring>());

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            TimeSpan late = DateTimeOffset.UtcNow - (context.ScheduledFireTimeUtc ?? context.FireTimeUtc);
            Firings.Enqueue(new PinnedFiring(context.Scheduler.SchedulerInstanceId, late));
            return default;
        }
    }

    /// <summary>
    /// Does nothing, on either node; the churn that keeps both loops awake.
    /// </summary>
    public sealed class BusyJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }

    /// <summary>
    /// Runs long enough that a node which lost its reservation to this execution reads the store again
    /// while it is still running, and short enough that its triggers are reserved many times over the
    /// run; records that it never ran twice at once.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        private static int running;
        private static int peak;
        private static int completed;
        private static volatile ConcurrentQueue<string> nodes = new();

        public static int PeakConcurrency => Volatile.Read(ref peak);

        public static int Completed => Volatile.Read(ref completed);

        public static ConcurrentQueue<string> Nodes => nodes;

        public static void Reset()
        {
            Interlocked.Exchange(ref running, 0);
            Interlocked.Exchange(ref peak, 0);
            Interlocked.Exchange(ref completed, 0);
            Interlocked.Exchange(ref nodes, new ConcurrentQueue<string>());
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            int inside = Interlocked.Increment(ref running);
            RecordPeak(inside);
            Nodes.Enqueue(context.Scheduler.SchedulerInstanceId);

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref running);
                Interlocked.Increment(ref completed);
            }
        }

        private static void RecordPeak(int inside)
        {
            int observed = Volatile.Read(ref peak);
            while (inside > observed)
            {
                int previous = Interlocked.CompareExchange(ref peak, inside, observed);
                if (previous == observed)
                {
                    return;
                }

                observed = previous;
            }
        }
    }
}
