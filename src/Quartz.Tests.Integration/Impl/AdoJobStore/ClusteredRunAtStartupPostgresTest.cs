using Quartz.Configuration;
using Quartz.Extensibility;

namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// <see cref="QuartzBuilderExtensions.RunAtStartup" /> on two nodes of one cluster: each node that starts
/// runs the job once, on itself, and replaces only its own leftovers (#4019).
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredRunAtStartupPostgresTest : ClusteredPostgresTestBase
{
    private static readonly JobKey warmKey = new("warm-cache", "startup");

    /// <summary>
    /// Long enough that no node is taken for dead while the test runs, so no leftover fails over: what is
    /// asserted is what each node's own start does.
    /// </summary>
    private const int CheckinMisfireThresholdMs = 60_000;

    protected override string SchedulerName => "RunAtStartupClusterTest";

    [Test]
    public async Task EachNodeThatStartsRunsTheJobOnceOnItself()
    {
        IScheduler nodeA = await CreateScheduler("nodeA", configureBuilder: Register);
        IScheduler nodeB = await CreateScheduler("nodeB", configureBuilder: Register);
        try
        {
            await nodeA.Start();
            await nodeB.Start();

            await WaitForExecutionCount(2, 20_000);

            // Past two of the nodes' two-second idle waits, so a third run would have happened by now.
            await Task.Delay(TimeSpan.FromSeconds(5));

            RecordingJob.Executions.Should().BeEquivalentTo(["nodeA", "nodeB"],
                "a startup run is each node's own: pinned to the node that started, it runs there once, "
                + "where an unpinned one would run wherever acquisition happened to take it");

            (await CountStartupTriggers()).Should().Be(0, "each one-shot trigger is deleted once it has fired");
        }
        finally
        {
            await nodeA.Shutdown(false);
            await nodeB.Shutdown(false);
        }
    }

    [Test]
    public async Task ARestartedNodeReplacesItsOwnLeftoverAndNoOtherNodes()
    {
        IScheduler nodeA = await CreateScheduler("nodeA", checkinMisfireThresholdMs: CheckinMisfireThresholdMs, configureBuilder: Register);
        IScheduler nodeB = await CreateScheduler("nodeB", checkinMisfireThresholdMs: CheckinMisfireThresholdMs, configureBuilder: Register);
        IScheduler restartedA = null;
        try
        {
            await nodeA.Start();
            await nodeB.Start();
            await WaitForExecutionCount(2, 20_000);
            await nodeA.Shutdown(true);

            // What a crash of nodeA before its run fired would have left: its trigger, due, pinned to it.
            // nodeB takes nodeA for alive within the threshold, so it leaves the trigger alone.
            ITrigger leftoverA = StartupRunPlugin.CreateTrigger(warmKey, "nodeA", clustered: true, TimeProvider.System);
            await nodeB.ScheduleJob(leftoverA);

            // And one of nodeB's own, not due yet, which nodeA's start has no business with.
            ITrigger leftoverB = StartupRunPlugin.CreateTrigger(warmKey, "nodeB", clustered: true, TimeProvider.System);
            ((IMutableTrigger) leftoverB).StartTimeUtc = DateTimeOffset.UtcNow.AddHours(1);
            await nodeB.ScheduleJob(leftoverB);

            RecordingJob.Reset();
            restartedA = await CreateScheduler("nodeA", checkinMisfireThresholdMs: CheckinMisfireThresholdMs, configureBuilder: Register);
            await restartedA.Start();

            await WaitForExecutionCount(1, 20_000);
            await Task.Delay(TimeSpan.FromSeconds(5));

            RecordingJob.Executions.Should().Equal(["nodeA"],
                "the restart's run replaces the run its earlier start never made, rather than adding a second");
            (await restartedA.Exists(leftoverA.Key)).Should().BeFalse("nodeA unscheduled its own leftover");
            (await restartedA.Exists(leftoverB.Key)).Should().BeTrue("nodeB's leftover is nodeB's to replace when it starts");
        }
        finally
        {
            if (restartedA is not null)
            {
                await restartedA.Shutdown(false);
            }

            await nodeA.Shutdown(false);
            await nodeB.Shutdown(false);
        }
    }

    private Task<int> CountStartupTriggers()
    {
        return CountRows(
            "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @group",
            ("schedulerName", SchedulerName),
            ("group", SchedulerConstants.StartupGroup));
    }

    private static void Register(IQuartzBuilder quartz) => quartz
        .AddJob<RecordingJob>(job => job.WithIdentity(warmKey).StoreDurably())
        .RunAtStartup(warmKey);
}
