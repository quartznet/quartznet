using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// A node shuts down without waiting for its jobs while one of them is inside <c>Execute</c>, and the
/// schedule it was halfway through still comes out having fired every instant exactly once — promptly,
/// rather than after a peer has waited out the leaver's check-in (#3746).
/// </summary>
/// <remarks>
/// <para>
/// The check-in misfire threshold is deliberately enormous — thirty seconds against a one-second
/// check-in interval — so that cluster recovery cannot settle the leaver's residue within the window
/// this fixture asserts over. Everything asserted below therefore has to be the leaving node's own
/// doing: what it leaves behind when it goes is what the surviving node has to work with, and a
/// fixture whose peer could recover in two seconds would pass whether or not the leaver settled
/// anything.
/// </para>
/// <para>
/// The trigger carries <see cref="SimpleTriggerMisfireInstruction.IgnoreMisfires"/>, which makes
/// "fired exactly once" a statement about a fixed set of instants rather than about a count: a trigger
/// with that instruction is exempt from the misfire handler and from acquisition's <c>NoEarlierThan</c>
/// bound, so however far behind the nodes fall its fire times stay the series it was scheduled with.
/// </para>
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class UnwaitedShutdownClusteredPostgresTest : ClusteredPostgresTestBase
{
    private const string Group = "unwaitedShutdown";
    private const string TriggerName = "unwaitedShutdownTrigger";
    private const int FiringCount = 5;

    private static readonly TimeSpan Interval = TimeSpan.FromSeconds(1);

    private static volatile ConcurrentBag<Firing> firings = new();
    private static SemaphoreSlim jobParked = new(0);
    private static SemaphoreSlim jobMayFinish = new(0);
    private static SemaphoreSlim jobFinished = new(0);
    private static int executions;

    protected override string SchedulerName => "UnwaitedShutdownTest";

    [SetUp]
    public void ResetSignals()
    {
        Interlocked.Exchange(ref firings, new ConcurrentBag<Firing>());
        jobParked = new SemaphoreSlim(0);
        jobMayFinish = new SemaphoreSlim(0);
        jobFinished = new SemaphoreSlim(0);
        Interlocked.Exchange(ref executions, 0);
    }

    [TearDown]
    public void DisposeSignals()
    {
        jobParked.Dispose();
        jobMayFinish.Dispose();
        jobFinished.Dispose();
    }

    [Test]
    public async Task ANodeLeavingWithoutWaitingForItsJobsCostsTheClusterNoFiring()
    {
        IScheduler nodeA = await CreateScheduler("unwaitedNodeA", checkinMisfireThresholdMs: 30_000);
        IScheduler nodeB = await CreateScheduler("unwaitedNodeB", checkinMisfireThresholdMs: 30_000);

        TriggerKey triggerKey = new(TriggerName, Group);

        try
        {
            // Only node A is running, so the firing that is interrupted is deterministically its.
            await nodeA.Start();

            IJobDetail job = JobBuilder.Create<ParkingJob>()
                .WithIdentity("unwaitedShutdownJob", Group)
                .StoreDurably()
                .Build();
            await nodeA.AddJob(job, new AddJobOptions { Replace = true });

            DateTimeOffset start = DateTimeOffset.UtcNow.AddSeconds(2);
            DateTimeOffset[] expected = new DateTimeOffset[FiringCount];
            for (int i = 0; i < FiringCount; i++)
            {
                expected[i] = start + i * Interval;
            }

            await nodeA.ScheduleJob(TriggerBuilder.Create()
                .WithIdentity(triggerKey)
                .ForJob(job)
                .StartAt(start)
                .WithSimpleSchedule(schedule => schedule
                    .WithInterval(Interval)
                    .WithRepeatCount(FiringCount - 1)
                    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires))
                .Build());

            (await jobParked.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the first firing has to be inside Execute before its node leaves");

            // The peer joins while node A is holding the job, so the cluster never stops having a node
            // that could fire the rest of the schedule.
            await nodeB.Start();

            TestContext.Out.WriteLine("State with the first firing parked on node A:\n" + await DumpDatabaseState());

            // The firing ends of its own accord a fraction of a second after the node is told to come
            // down, which is the ordinary shape of this: an execution in flight when the shutdown was
            // asked for, finishing while it is still under way. Half a second is comfortably longer than
            // the leaver's own teardown — a shutdown that does not wait for its jobs is over in tens of
            // milliseconds, and on the unfixed path the store is closed by the time this fires — and
            // comfortably shorter than the window the shutdown gives an execution to report itself.
            Task release = Task.Run(async () =>
            {
                await Task.Delay(500);
                jobMayFinish.Release();
            });

            // The case this fixture is about: the node comes down without waiting for the job it is
            // running, so the completion that firing owes the store is issued by a scheduler that has
            // already stopped.
            long shutdownStarted = Stopwatch.GetTimestamp();
            await nodeA.Shutdown(waitForJobsToComplete: false);
            TimeSpan shutdownTook = Stopwatch.GetElapsedTime(shutdownStarted);

            TestContext.Out.WriteLine(
                $"Shutdown(waitForJobsToComplete: false) returned after {shutdownTook.TotalMilliseconds:F0} ms. "
                + $"State:\n{await DumpDatabaseState()}");

            await release;
            (await jobFinished.WaitAsync(TimeSpan.FromSeconds(30)))
                .Should().BeTrue("the interrupted execution still runs to the end on the leaver's own thread");

            // The completion the leaver owed the store. It is issued the instant the job body returns, so
            // ten seconds is a round trip's worth of slack — and it is a third of the check-in misfire
            // threshold, so nothing a peer does can be what satisfies it.
            await WaitForCondition(
                async () => await CountFiredTriggerRows("unwaitedNodeA") == 0,
                timeoutMs: 10_000,
                async () => "the leaver's fired-trigger row to be gone. A firing that ran to completion has "
                            + "to be recorded as complete by the node that ran it, whether or not that node "
                            + "waited for it, or the row sits there until a peer's cluster recovery — which "
                            + $"here is thirty seconds away. State:\n{await DumpDatabaseState()}");

            // Asked as "is it BLOCKED" rather than "is it WAITING": the survivor is running, so by the
            // time this reads the row it may legitimately have acquired or even fired the trigger again.
            // BLOCKED is the one answer that can only mean the leaver's execution is still recorded as
            // holding the job.
            (await CountBlockedTriggerRows(triggerKey)).Should().Be(0,
                "the execution that was holding the trigger has finished, so nothing is holding it any "
                + "more — a trigger left BLOCKED by a departed node is a schedule that stops dead until a "
                + "peer times the leaver out");

            // Everything from here on is the survivor's, and it has to be all of it.
            await WaitForCondition(
                () => Task.FromResult(firings.Count >= FiringCount),
                timeoutMs: 20_000,
                async () => $"the survivor to finish the schedule alone; {firings.Count} of {FiringCount} "
                            + $"firings arrived ({NodeFiringCounts()}). State:\n{await DumpDatabaseState()}");

            Firing[] recorded = firings.ToArray();
            TestContext.Out.WriteLine("Firings: " + string.Join(", ", recorded
                .OrderBy(x => x.ScheduledFireTimeUtc)
                .Select(x => $"{x.ScheduledFireTimeUtc:O}@{x.InstanceId}")));

            DateTimeOffset[] missing = expected.Except(recorded.Select(x => x.ScheduledFireTimeUtc)).OrderBy(x => x).ToArray();
            missing.Should().BeEmpty(
                "a node that leaves without waiting costs the cluster no firing — the trigger has {0} "
                + "scheduled instants whether or not the node that started the schedule saw it through; "
                + "missing: [{1}]", FiringCount, Format(missing));

            DateTimeOffset[] doubled = recorded
                .GroupBy(x => x.ScheduledFireTimeUtc)
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .OrderBy(x => x)
                .ToArray();
            doubled.Should().BeEmpty(
                "and it gains the cluster none either: a scheduled instant belongs to exactly one node, "
                + "and a second firing of one would mean the survivor re-fired what the leaver had already "
                + "run; doubled: [{0}]", Format(doubled));

            recorded.Should().ContainSingle(x => string.Equals(x.InstanceId, "unwaitedNodeA", StringComparison.Ordinal),
                "the leaver ran exactly the one firing it was parked on, and a scheduler that has shut "
                + "down acquires nothing");

            (await CountFiredTriggerRows(instanceId: null)).Should().Be(0,
                "every firing of the run has been accounted for, so nothing is left claiming to be in "
                + "flight");
        }
        finally
        {
            jobMayFinish.Release();
            await nodeA.Shutdown(waitForJobsToComplete: false);
            await nodeB.Shutdown(waitForJobsToComplete: false);
        }
    }

    /// <summary>
    /// How many fired-trigger rows this fixture's scheduler has, optionally for one node only.
    /// </summary>
    private async Task<int> CountFiredTriggerRows(string instanceId)
    {
        if (instanceId is null)
        {
            return await CountRows(
                "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName",
                ("schedulerName", SchedulerName));
        }

        return await CountRows(
            "SELECT COUNT(*) FROM QRTZ_FIRED_TRIGGERS WHERE SCHED_NAME = @schedulerName AND INSTANCE_NAME = @instanceName",
            ("schedulerName", SchedulerName),
            ("instanceName", instanceId));
    }

    /// <summary>
    /// Whether the trigger's row says BLOCKED, asked of the row rather than through
    /// <c>GetTriggerState</c>, which maps several stored states onto one answer and so cannot tell
    /// BLOCKED from WAITING.
    /// </summary>
    private async Task<int> CountBlockedTriggerRows(TriggerKey triggerKey)
    {
        return await CountRows(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName "
            + "AND TRIGGER_NAME = @triggerName AND TRIGGER_GROUP = @triggerGroup AND TRIGGER_STATE = 'BLOCKED'",
            ("schedulerName", SchedulerName),
            ("triggerName", triggerKey.Name),
            ("triggerGroup", triggerKey.Group));
    }

    private static string NodeFiringCounts()
        => string.Join(", ", firings
            .GroupBy(x => x.InstanceId, StringComparer.Ordinal)
            .OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(x => $"{x.Key}={x.Count()}"));

    private static string Format(IEnumerable<DateTimeOffset> instants)
        => string.Join(", ", instants.Select(x => x.ToString("O", CultureInfo.InvariantCulture)));

    private sealed record Firing(string InstanceId, DateTimeOffset ScheduledFireTimeUtc);

    /// <summary>
    /// Parks its first execution until the test lets it go, so that the shutdown under test always
    /// lands mid-execution; every later firing records itself and returns.
    /// </summary>
    /// <remarks>
    /// <see cref="DisallowConcurrentExecutionAttribute"/> is what makes the interrupted firing hold the
    /// trigger: the store blocks the job's triggers for as long as an execution is inside, and the
    /// completion that never arrived is the only thing that would have unblocked them.
    /// </remarks>
    [DisallowConcurrentExecution]
    public sealed class ParkingJob : IJob
    {
        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref executions) == 1)
            {
                jobParked.Release();

                // Deliberately not the job's own token: the node shutting down mid-run is what is under
                // test, and a firing that turned into a cancellation here would be one the totals could
                // not count.
                await jobMayFinish.WaitAsync(TimeSpan.FromSeconds(60), CancellationToken.None).ConfigureAwait(false);
                firings.Add(new Firing(context.Scheduler.SchedulerInstanceId, context.ScheduledFireTimeUtc!.Value));
                jobFinished.Release();
                return;
            }

            firings.Add(new Firing(context.Scheduler.SchedulerInstanceId, context.ScheduledFireTimeUtc!.Value));
        }
    }
}
