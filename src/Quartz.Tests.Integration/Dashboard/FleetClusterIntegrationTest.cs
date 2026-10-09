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
    /// The same rule with the nodes dialling in: three agents, one per node of one cluster, give one row
    /// with three nodes; an interruption through the cluster's key reaches the node running the job; and
    /// a stand-by through a member's key stops that node and no other. The dashboard listens on Kestrel
    /// and the agents reach it over WebSockets, the transport a deployment uses.
    /// </summary>
    [Test]
    public async Task ThreeAgentsOnOneClusterAreOneRowAndNodeLocalVerbsReachOneNode()
    {
        await using AgentDashboardHost dashboard = await AgentDashboardHost.Start();
        await using AgentNode node1 = await AgentNode.Start(NodeProperties("fleet-a1", 1000, 2000, null), dashboard, "agent1");
        await using AgentNode node2 = await AgentNode.Start(NodeProperties("fleet-a2", 1000, 2000, null), dashboard, "agent2");
        await using AgentNode node3 = await AgentNode.Start(NodeProperties("fleet-a3", 1000, 2000, null), dashboard, "agent3");

        using IServiceScope scope = dashboard.Application.Services.CreateScope();
        IQuartzApiClient client = scope.ServiceProvider.GetRequiredService<IQuartzApiClient>();

        // Three registrations, three check-in rows, and a detection round that sees all three: the
        // monitor runs a round on every registration and every two seconds, and a node that had not
        // checked in when the round ran joins at the next.
        SchedulerHeaderDto? cluster = null;
        await WaitForCondition(
            async () =>
            {
                cluster = (await client.GetSchedulers()).SingleOrDefault(x => x.Origin == SchedulerOrigin.Cluster && x.Members.Length == 3);
                return cluster is not null;
            },
            timeoutMs: 60_000,
            "the three agents to be one cluster row");

        cluster!.Key.Should().Be("agent1+agent2+agent3/" + SchedulerName);
        cluster.Status.Should().Be(SchedulerStatus.Running);
        (await client.GetSchedulers()).Should().ContainSingle(x => x.SchedulerName == SchedulerName,
            "the members are hidden behind the cluster row");

        (await client.QueryClusterNodes(cluster.Key)).Select(x => x.InstanceId).Should().BeEquivalentTo(["fleet-a1", "fleet-a2", "fleet-a3"],
            "the cluster's nodes are read through the preferred member's connection");

        // A verb that belongs to one node goes through the member's key, down that member's connection.
        await client.Standby("agent3/" + SchedulerName);
        (await node3.Scheduler.GetStatus()).Should().Be(SchedulerStatus.Standby);
        (await node1.Scheduler.GetStatus()).Should().Be(SchedulerStatus.Running, "the other nodes were not asked");
        (await node2.Scheduler.GetStatus()).Should().Be(SchedulerStatus.Running);

        Func<Task> standbyCluster = () => client.Standby(cluster.Key).AsTask();
        await standbyCluster.Should().ThrowAsync<NotSupportedException>("a cluster is every node at once");

        // A job that blocks until it is cancelled, picked up by one of the two nodes still firing, and
        // interrupted through the cluster's key, which routes to the agent of the node that owns it.
        await node1.Scheduler.ScheduleJob(
            JobBuilder.Create<BlockingJob>().WithIdentity("blocking", "fleet-agents").Build(),
            TriggerBuilder.Create().WithIdentity("now", "fleet-agents").StartNow().Build());

        (string node, string fireInstanceId) = await BlockingJob.Started.Task.WaitAsync(TimeSpan.FromSeconds(20));
        node.Should().BeOneOf("fleet-a1", "fleet-a2");

        (await client.InterruptFireInstance(cluster.Key, fireInstanceId)).Should().BeTrue(
            "the store names the node that owns the firing, and the cluster sends the interruption down that node's connection");
        await BlockingJob.Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(20));
    }

    /// <summary>
    /// A dashboard that accepts agents, on a loopback port Kestrel picks.
    /// </summary>
    private sealed class AgentDashboardHost : IAsyncDisposable
    {
        public const string Token = "fleet-agent-token";

        private AgentDashboardHost(WebApplication application, Uri hubUri)
        {
            Application = application;
            HubUri = hubUri;
        }

        public WebApplication Application { get; }

        public Uri HubUri { get; }

        public static async Task<AgentDashboardHost> Start()
        {
            WebApplicationBuilder builder = WebApplication.CreateBuilder(new WebApplicationOptions
            {
                ContentRootPath = AppContext.BaseDirectory
            });

            builder.WebHost.UseUrls("http://127.0.0.1:0");
            builder.Logging.ClearProviders();
            builder.Services.AddAuthorization();
            builder.Services.AddQuartzDashboard(options =>
            {
                options.ClusterDetectionInterval = TimeSpan.FromSeconds(2);
                options.AcceptAgents(agents => agents.Tokens.Primary = Token);
            });

            WebApplication application = builder.Build();

            // The only visitor is this process, over loopback; the hub checks its own token.
            application.MapQuartzDashboard().AllowAnonymous();
            await application.StartAsync();

            IServerAddressesFeature addresses = application.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
                ?? throw new InvalidOperationException("Kestrel reported no addresses.");

            return new AgentDashboardHost(application, new Uri(addresses.Addresses.Single() + "/quartz/agents"));
        }

        public async ValueTask DisposeAsync()
        {
            await Application.StopAsync();
            await Application.DisposeAsync();
        }
    }

    /// <summary>
    /// One node: a plain host over the fixture's database whose scheduler dials the dashboard. No HTTP
    /// API, no port.
    /// </summary>
    private sealed class AgentNode : IAsyncDisposable
    {
        private readonly IHost host;

        private AgentNode(IHost host, IScheduler scheduler)
        {
            this.host = host;
            Scheduler = scheduler;
        }

        public IScheduler Scheduler { get; }

        public static async Task<AgentNode> Start(System.Collections.Specialized.NameValueCollection properties, AgentDashboardHost dashboard, string target)
        {
            HostApplicationBuilder builder = Microsoft.Extensions.Hosting.Host.CreateApplicationBuilder();
            builder.Logging.ClearProviders();

            builder.Services.AddQuartz(properties, quartz => quartz.UseDashboardAgent(agent =>
            {
                agent.Endpoint = dashboard.HubUri;
                agent.Token = AgentDashboardHost.Token;
                agent.Target = target;
            }));
            builder.Services.AddQuartzHostedService(options => options.AwaitApplicationStarted = false);

            IHost host = builder.Build();
            await host.StartAsync();

            IScheduler scheduler = await host.Services.GetRequiredService<ISchedulerFactory>().GetScheduler();
            return new AgentNode(host, scheduler);
        }

        public async ValueTask DisposeAsync()
        {
            await host.StopAsync();
            host.Dispose();
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
