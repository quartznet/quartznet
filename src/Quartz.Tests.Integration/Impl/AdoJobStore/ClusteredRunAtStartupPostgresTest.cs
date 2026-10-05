namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// <see cref="QuartzBuilderExtensions.RunAtStartup" /> on two nodes of one cluster: each node that starts
/// runs the job once, on itself (#4019).
/// </summary>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredRunAtStartupPostgresTest : ClusteredPostgresTestBase
{
    private static readonly JobKey warmKey = new("warm-cache", "startup");

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

            (await CountRows(
                "SELECT COUNT(*) FROM QRTZ_TRIGGERS WHERE SCHED_NAME = @schedulerName AND TRIGGER_GROUP = @group",
                ("schedulerName", SchedulerName),
                ("group", SchedulerConstants.StartupGroup))).Should().Be(0, "each one-shot trigger is deleted once it has fired");
        }
        finally
        {
            await nodeA.Shutdown(false);
            await nodeB.Shutdown(false);
        }
    }

    private static void Register(IQuartzBuilder quartz) => quartz
        .AddJob<RecordingJob>(job => job.WithIdentity(warmKey).StoreDurably())
        .RunAtStartup(warmKey);
}
