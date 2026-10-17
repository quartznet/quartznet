namespace Quartz.Tests.Integration.Impl.AdoJobStore;

/// <summary>
/// Two nodes configured the quick start's way — <c>UseClustering()</c> and no instance id — check in as
/// two nodes (#4091).
/// </summary>
/// <remarks>
/// Before #4091 both answered to <c>NON_CLUSTERED</c>, so they wrote one <c>QRTZ_SCHEDULER_STATE</c> row
/// between them and the cluster listed one node. The unit fixture <c>ClusteredInstanceIdTest</c> covers
/// the option; this is the same promise held by a real database, which SQLite cannot give: it refuses
/// clustering as the store initializes.
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class ClusteredInstanceIdPostgresTest : ClusteredPostgresTestBase
{
    protected override string SchedulerName => "ClusteredInstanceIdTest";

    [Test]
    public async Task TwoNodesWithNoInstanceIdCheckInAsTwoNodes()
    {
        IScheduler first = await QuickStartNode();
        IScheduler second = await QuickStartNode();
        try
        {
            await first.Start();
            await second.Start();

            first.SchedulerInstanceId.Should().NotBe(QuartzSchedulerOptions.DefaultInstanceId,
                "UseClustering() generates an id for a node that chose none");
            first.SchedulerInstanceId.Should().NotBe(second.SchedulerInstanceId,
                "two nodes on one id read each other's fired triggers as their own");

            List<ClusterNode> nodes = [];
            await WaitForCondition(
                async () =>
                {
                    nodes = await first.QueryClusterNodes();
                    return nodes.Count == 2;
                },
                timeoutMs: 15_000,
                () => Task.FromResult($"the cluster listed {nodes.Count} node(s): {string.Join(", ", nodes.Select(node => node.InstanceId))}"));

            nodes.Select(node => node.InstanceId).Should().BeEquivalentTo(
                [first.SchedulerInstanceId, second.SchedulerInstanceId],
                "each node writes a check-in row of its own, which is what lets a peer see it fail");
        }
        finally
        {
            await first.Shutdown(false);
            await second.Shutdown(false);
        }
    }

    /// <summary>
    /// A node built as <c>quick-start.md</c> builds one, with a short check-in so the test need not wait.
    /// </summary>
    /// <remarks>
    /// Each <see cref="QuartzSchedulerBuilder" /> owns its own container and repository, so two calls build
    /// two nodes rather than one looked up twice by name.
    /// </remarks>
    private async Task<IScheduler> QuickStartNode()
    {
        ISchedulerFactory factory = QuartzSchedulerBuilder.Create(q =>
        {
            q.ConfigureScheduler(options => options.InstanceName = SchedulerName);
            q.UsePersistentStore(store =>
            {
                store.UsePostgres(Database.ConnectionString);
                store.UseClustering(cluster => cluster.CheckinInterval = TimeSpan.FromSeconds(1));
            });
        }).Build();

        return await factory.GetScheduler();
    }
}
