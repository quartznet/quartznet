using System.Collections.Concurrent;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A continuation scheduled on one node, released by a parent that completes on another, and run by
/// a third party to both decisions — the store.
/// </summary>
/// <remarks>
/// <para>
/// This is the property the whole design exists for. <c>JobChainingJobListener</c> triggers the
/// follow-up from the node that ran the first job, out of an in-memory dictionary, so a chain
/// survives neither a restart nor a node that dies between the two. A continuation is a row: the
/// parent's completion settles it inside the parent's own transaction, and whichever node comes to
/// acquire next is the node that runs it.
/// </para>
/// <para>
/// The two triggers are pinned to different nodes, so "node B ran the continuation the completion on
/// node A released" is observed rather than hoped for. Without the pins the assertion would be
/// "somebody ran it", which the single-node contract suite already makes.
/// </para>
/// </remarks>
public abstract class ClusteredContinuationTestBase : ClusteredJobStoreTestBase
{
    private const string Group = "clusterContinuation";
    private const string NodeA = "continuationNodeA";
    private const string NodeB = "continuationNodeB";

    protected ClusteredContinuationTestBase(string provider) : base(provider)
    {
    }

    protected override string SchedulerName => "ClusterContinuationTest";

    [SetUp]
    public void ResetNodeRecorder() => NodeRecordingJob.Reset();

    [Test]
    public async Task AContinuationReleasedByOneNodeIsRunByAnother()
    {
        IScheduler nodeA = await CreateScheduler(NodeA);
        IScheduler nodeB = await CreateScheduler(NodeB);

        try
        {
            await nodeA.Start();
            await nodeB.Start();

            IJobDetail parentJob = JobBuilder.Create<NodeRecordingJob>()
                .WithIdentity("parent", Group)
                .StoreDurably()
                .Build();

            IJobDetail continuationJob = JobBuilder.Create<NodeRecordingJob>()
                .WithIdentity("continuation", Group)
                .StoreDurably()
                .Build();

            await nodeA.AddJob(parentJob, new AddJobOptions { Replace = true });
            await nodeA.AddJob(continuationJob, new AddJobOptions { Replace = true });

            TriggerKey parentKey = new TriggerKey("parent", Group);

            // Both scheduled before either is due, and each pinned to a node, so the firing and the
            // settlement happen on one node and the continuation can only be acquired by the other.
            await nodeA.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(parentKey)
                .ForJob(parentJob)
                .WithPreferredNode(PreferredNode.For(NodeA))
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(5))
                .Build());

            await nodeA.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity("continuation", Group)
                .ForJob(continuationJob)
                .WithPreferredNode(PreferredNode.For(NodeB))
                // In the past, so that the release's max(now, START_TIME) is "now" and the trigger is
                // due the instant it stops awaiting.
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(-5))
                .StartAfter(parentKey, ContinuationCondition.OnSuccess)
                .Build());

            (await nodeB.GetTriggerState(new TriggerKey("continuation", Group))).Should().Be(TriggerState.Awaiting,
                "the continuation is due by its own schedule and pinned to a running node, so only the wait is "
                + "keeping it from firing");

            await WaitForCondition(
                () => Task.FromResult(NodeRecordingJob.NodeFor("continuation") is not null),
                timeoutMs: 120_000,
                async () => "the continuation to run once its parent had completed. "
                            + $"Ran so far: {string.Join(", ", NodeRecordingJob.Executions)}. State:\n{await DumpDatabaseState()}");

            NodeRecordingJob.NodeFor("parent").Should().Be(NodeA,
                "the parent is pinned there, and the pin is what makes the rest of this assertion mean anything");

            NodeRecordingJob.NodeFor("continuation").Should().Be(NodeB,
                "the completion that released the continuation happened on the other node — the store is what "
                + "carried it across, so nothing was lost by the two nodes sharing only rows");
        }
        finally
        {
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// Records which node ran which job, so that "the other node ran it" is an observation.
    /// </summary>
    public sealed class NodeRecordingJob : IJob
    {
        private static volatile ConcurrentDictionary<string, string> executions = new(StringComparer.Ordinal);

        public static IEnumerable<string> Executions => executions.Select(x => $"{x.Key}@{x.Value}");

        public static string NodeFor(string jobName) => executions.GetValueOrDefault(jobName);

        public static void Reset() => Interlocked.Exchange(ref executions, new ConcurrentDictionary<string, string>(StringComparer.Ordinal));

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            executions[context.JobDetail.Key.Name] = context.Scheduler.SchedulerInstanceId;
            return default;
        }
    }
}
