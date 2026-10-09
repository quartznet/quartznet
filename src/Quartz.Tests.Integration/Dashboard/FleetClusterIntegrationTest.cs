#nullable enable

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.Dashboard.Services;
using Quartz.Tests.Integration.Impl.AdoJobStore;

namespace Quartz.Tests.Integration.Dashboard;

/// <summary>
/// The cluster rule over a real cluster: two nodes of one scheduler on one PostgreSQL database, each
/// serving the HTTP API from its own host, fronted by two HTTP targets of one dashboard.
/// </summary>
/// <remarks>
/// <para>
/// Two hosts rather than two schedulers in one container, because a container holds one scheduler per
/// name. SQLite refuses to cluster, so the shared database is PostgreSQL.
/// </para>
/// <para>
/// What the fakes in <c>ClusterMergeTest</c> say in a line each, this says with real check-in rows:
/// the dashboard lists one row with two nodes, a verb that belongs to one node reaches that node through
/// the member's key, and an interruption through the cluster's key reaches the node running the job.
/// </para>
/// </remarks>
[Category("db-postgres")]
[NonParallelizable]
public sealed class FleetClusterIntegrationTest : ClusteredPostgresTestBase
{
    protected override string SchedulerName => "FleetClusterTest";

    [SetUp]
    public void ResetJob() => BlockingJob.Reset();

    [Test]
    public async Task TwoTargetsOnOneClusterAreOneRowAndNodeLocalVerbsReachOneNode()
    {
        await using Node node1 = await Node.Start(NodeProperties("fleet-1", 1000, 2000, null));
        await using Node node2 = await Node.Start(NodeProperties("fleet-2", 1000, 2000, null));

        // Both nodes have written their check-in rows before the dashboard looks: the detection asks each
        // target which nodes it sees, and a node that has not checked in yet is not seen by the other.
        await WaitForCondition(
            async () => (await node1.Scheduler.QueryClusterNodes()).Count == 2,
            timeoutMs: 15_000,
            "both nodes to have checked in");

        HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = SchedulerName;
            options.Target = "node1";
            options.CreateHttpClient = _ => new HttpClient { BaseAddress = node1.ApiAddress };
        });
        builder.Services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = SchedulerName;
            options.Target = "node2";
            options.CreateHttpClient = _ => new HttpClient { BaseAddress = node2.ApiAddress };
        });
        builder.Services.AddQuartzDashboard(options => options.ClusterDetectionInterval = TimeSpan.FromMinutes(1));

        using IHost dashboard = builder.Build();

        // Starting the host binds the two targets and runs the first detection round.
        await dashboard.StartAsync();
        try
        {
            using IServiceScope scope = dashboard.Services.CreateScope();
            IQuartzApiClient client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();

            List<SchedulerHeaderDto> schedulers = await client.GetSchedulers();
            SchedulerHeaderDto cluster = schedulers.Should().ContainSingle(x => x.SchedulerName == SchedulerName,
                "two targets fronting the nodes of one cluster are one row").Subject;
            cluster.Origin.Should().Be(SchedulerOrigin.Cluster);
            cluster.Key.Should().Be("node1+node2/" + SchedulerName);
            cluster.Members.Should().Equal(["node1", "node2"]);
            cluster.Status.Should().Be(SchedulerStatus.Running);

            (await client.QueryClusterNodes(cluster.Key)).Select(x => x.InstanceId).Should().BeEquivalentTo(["fleet-1", "fleet-2"],
                "the cluster's nodes are read through the preferred member, whose store every node shares");

            // A verb that belongs to one node goes through the member's key and reaches that node only.
            await client.Standby("node2/" + SchedulerName);
            (await node2.Scheduler.GetStatus()).Should().Be(SchedulerStatus.Standby);
            (await node1.Scheduler.GetStatus()).Should().Be(SchedulerStatus.Running, "the other node was not asked");

            Func<Task> standbyCluster = () => client.Standby(cluster.Key).AsTask();
            await standbyCluster.Should().ThrowAsync<NotSupportedException>("a cluster is every node at once");

            // A job that blocks until it is cancelled, run by node 1 - the only node still firing - and
            // interrupted through the cluster's key, which routes to the node that owns the firing.
            await node1.Scheduler.ScheduleJob(
                JobBuilder.Create<BlockingJob>().WithIdentity("blocking", "fleet").Build(),
                TriggerBuilder.Create().WithIdentity("now", "fleet").StartNow().Build());

            (string node, string fireInstanceId) = await BlockingJob.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
            node.Should().Be("fleet-1");

            (await client.InterruptFireInstance(cluster.Key, fireInstanceId)).Should().BeTrue(
                "the store names the node that owns the firing, and the cluster sends the interruption there");
            await BlockingJob.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20));
        }
        finally
        {
            await dashboard.StopAsync();
        }
    }

    /// <summary>
    /// One node: a web host serving the HTTP API over the fixture's database.
    /// </summary>
    private sealed class Node : IAsyncDisposable
    {
        private readonly WebApplication application;

        private Node(WebApplication application, IScheduler scheduler, Uri apiAddress)
        {
            this.application = application;
            Scheduler = scheduler;
            ApiAddress = apiAddress;
        }

        public IScheduler Scheduler { get; }

        public Uri ApiAddress { get; }

        public static async Task<Node> Start(System.Collections.Specialized.NameValueCollection properties)
        {
            WebApplicationBuilder builder = WebApplication.CreateSlimBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });

            // A port the operating system picks, on loopback. The only caller is this process.
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();

            builder.Services.AddQuartz(properties);
            builder.Services.AddQuartzHostedService(options => options.AwaitApplicationStarted = false);
            builder.Services.AddQuartzHttpApi(options => options.ApiPath = "/");

            WebApplication application = builder.Build();

            // The only caller is this process, over loopback.
            application.MapQuartzHttpApi().AllowAnonymous();

            await application.StartAsync();

            IServerAddressesFeature addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Kestrel reported no addresses.");

            IScheduler scheduler = await application.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            return new Node(application, scheduler, new Uri(addresses.Addresses.Single() + "/"));
        }

        public async ValueTask DisposeAsync()
        {
            await application.StopAsync();
            await application.DisposeAsync();
        }
    }

    /// <summary>
    /// Records which node runs it, then waits to be cancelled.
    /// </summary>
    public sealed class BlockingJob : IJob
    {
        public static TaskCompletionSource<(string Node, string FireInstanceId)> Started { get; private set; } = New<(string, string)>();

        public static TaskCompletionSource Cancelled { get; private set; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public static void Reset()
        {
            Started = New<(string, string)>();
            Cancelled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        }

        private static TaskCompletionSource<T> New<T>() => new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult((context.Scheduler.SchedulerInstanceId, context.FireInstanceId));
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(5), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Cancelled.TrySetResult();
            }
        }
    }
}
