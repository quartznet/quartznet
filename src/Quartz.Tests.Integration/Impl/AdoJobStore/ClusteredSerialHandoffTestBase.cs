using System.Collections.Concurrent;
using System.Collections.Specialized;
using System.Globalization;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Each of two clustered nodes owns a trigger of one <see cref="DisallowConcurrentExecutionAttribute" />
/// job, and fires it soon after the other node's run of the job ends rather than an idle wait later
/// (#3988).
/// </summary>
/// <remarks>
/// <para>
/// A node's trigger of a serial job is held behind a run on the other node in one of two ways. Its fire
/// is refused because the other node's fire blocked it, and the release leaves it <c>BLOCKED</c>; or it
/// is <c>BLOCKED</c> already when the node looks, so the acquisition never reads it. Either way the
/// round acquires nothing, and the only thing that lets the trigger go is the other node's completion,
/// which wakes the other node alone. A node that then waits out its idle wait fires its trigger that
/// late, and with a run every second it is held again by the time it looks.
/// </para>
/// <para>
/// The triggers are pinned, so only their own node can fire them: the other node, which is awake when
/// the job ends, cannot take them instead. Each is due every second, and the job runs for a quarter of
/// one, so the two are held behind each other throughout. A firing is late by up to a run when it is held
/// and the node looks again soon; by most of the idle wait when the node waits that out.
/// </para>
/// </remarks>
public abstract class ClusteredSerialHandoffTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterSerialHandoff";
    private const string NodeA = "handoff-node-a";
    private const string NodeB = "handoff-node-b";

    /// <summary>How long both nodes run once everything is scheduled.</summary>
    private static readonly TimeSpan observation = TimeSpan.FromSeconds(15);

    /// <summary>
    /// How long a node sleeps when an acquisition comes back empty: as long as the observation, so a node
    /// that waits it out once has nothing to show for the rest of the run.
    /// </summary>
    private static readonly TimeSpan idleWait = TimeSpan.FromSeconds(15);

    /// <summary>How long one run of the serial job takes.</summary>
    private static readonly TimeSpan runTime = TimeSpan.FromMilliseconds(250);

    /// <summary>
    /// How late a firing may be and still count as on time: half the idle wait, as in
    /// <see cref="ClusteredAcquisitionSkipTestBase" />. A node idling behind the other's run is late by
    /// most of the idle wait, and one looking again soon by a run or two, plus whatever a shared CI
    /// database adds: 4.7 s once on a local SQL Server under other load, with every firing made.
    /// </summary>
    private static readonly TimeSpan lateAllowed = idleWait / 2;

    protected ClusteredSerialHandoffTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterSerialHandoffTest";

    [SetUp]
    public void ResetRecords() => SerialJob.Reset();

    /// <param name="maxBatchSize">
    /// One: the lock-free acquisition, whose fire is a transaction of its own. Two: a round under the lock,
    /// which fires what is due as it acquires it (#3864).
    /// </param>
    /// <param name="offsetMilliseconds">
    /// How long after node A's trigger node B's is due. Zero: both are due at once, and the node that fires
    /// first blocks the other's. A tenth of a second: node B's is due while node A's run holds it.
    /// </param>
    [TestCase(2, 0)]
    [TestCase(2, 100)]
    [TestCase(1, 0)]
    [TestCase(1, 100)]
    public async Task EachNodeFiresItsTriggerOfASerialJobSoonAfterTheOtherNodesRunEnds(int maxBatchSize, int offsetMilliseconds)
    {
        void ConfigureNode(NameValueCollection properties)
        {
            properties["quartz.scheduler.batchTriggerAcquisitionMaxCount"] = maxBatchSize.ToString(CultureInfo.InvariantCulture);
            properties["quartz.scheduler.idleWaitTime"] = ((int) idleWait.TotalMilliseconds).ToString(CultureInfo.InvariantCulture);
            properties["quartz.threadPool.maxConcurrency"] = "2";
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

            // Far enough out that both are stored before either is due. Each is scheduled through its own
            // node, because a scheduling change only wakes the node it was made on.
            DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(2);
            foreach ((IScheduler node, string instanceId, TimeSpan offset) in ((IScheduler, string, TimeSpan)[])
                     [(nodeA, NodeA, TimeSpan.Zero), (nodeB, NodeB, TimeSpan.FromMilliseconds(offsetMilliseconds))])
            {
                await node.ScheduleJob(TriggerBuilder.Create()
                    .WithIdentity("serial-" + instanceId, Group)
                    .ForJob(serialJob)
                    .WithPreferredNode(PreferredNode.For(instanceId))
                    .StartAt(start + offset)
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

        List<SerialFiring> firings = [.. SerialJob.Firings];
        string report = Report(firings);
        TestContext.Out.WriteLine(report);

        SerialJob.PeakConcurrency.Should().Be(1, "the job disallows concurrent execution, on either node");

        foreach (string node in (string[]) [NodeA, NodeB])
        {
            List<SerialFiring> onNode = firings.FindAll(x => x.Node == node);
            onNode.Should().NotBeEmpty("'{0}' owns a trigger of the job due every second:\n{1}", node, report);
            onNode.Max(x => x.Late).Should().BeLessThanOrEqualTo(lateAllowed,
                "a node whose trigger is held behind the other node's run must look again soon after that run can have "
                + "ended, rather than wait out its idle time:\n{0}",
                report);
        }
    }

    private static string Report(List<SerialFiring> firings)
    {
        return string.Join("\n", firings
            .GroupBy(x => x.Node, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => string.Create(CultureInfo.InvariantCulture,
                $"{x.Key}: {x.Count()} firings, {x.Average(f => f.Late.TotalMilliseconds):F0} ms late on average, {x.Max(f => f.Late.TotalMilliseconds):F0} ms at worst")));
    }

    public sealed record SerialFiring(string Node, TimeSpan Late);

    /// <summary>
    /// Records, for every firing, the node it ran on and how far behind its scheduled time it started,
    /// and that it never ran twice at once.
    /// </summary>
    [DisallowConcurrentExecution]
    public sealed class SerialJob : IJob
    {
        private static int running;
        private static int peak;
        private static volatile ConcurrentQueue<SerialFiring> firings = new();

        public static int PeakConcurrency => Volatile.Read(ref peak);

        public static ConcurrentQueue<SerialFiring> Firings => firings;

        public static void Reset()
        {
            Interlocked.Exchange(ref running, 0);
            Interlocked.Exchange(ref peak, 0);
            Interlocked.Exchange(ref firings, new ConcurrentQueue<SerialFiring>());
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            TimeSpan late = DateTimeOffset.UtcNow - (context.ScheduledFireTimeUtc ?? context.FireTimeUtc);
            int inside = Interlocked.Increment(ref running);
            RecordPeak(inside);
            Firings.Enqueue(new SerialFiring(context.Scheduler.SchedulerInstanceId, late));

            try
            {
                await Task.Delay(runTime, cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                Interlocked.Decrement(ref running);
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
