using System.Net.Http;
using System.Text.Json;

using FakeItEasy;

using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using ModelContextProtocol.Client;

using Quartz.AspNetCore;
using Quartz.Configuration;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Mcp;

/// <summary>
/// M5 and M6: one endpoint for the fleet. A key names a scheduler behind an HTTP target, an agent or an attached store,
/// resolved by the HTTP API's rule; a target that does not serve a call is a tool error with the fault's class; and
/// every answer says how fresh it is.
/// </summary>
/// <remarks>
/// The HTTP target is a second application serving the HTTP API, reached through the <c>HttpScheduler</c>
/// <c>AddQuartzHttpClient</c> registers, as the dashboard host reaches one. The agent is the shape the dashboard's
/// registry gives one — a proxy bound under the agent's target, and a target whose liveness is heard and whose ceiling is
/// its <c>MaxAccess</c> — over a fake, so a case can make it unreachable or slow; the agent transport's own faults are
/// <c>GatewayTargetOverAgentTest</c>'s. The window is a SQLite database another process's scheduler runs on.
/// </remarks>
[NonParallelizable]
public sealed class McpFleetTest
{
    private McpTestHost? host;
    private WebApplicationFactory<Program>? worker;
    private IScheduler workerScheduler = null!;
    private SqliteTestDatabase? database;
    private ServiceProvider? node;
    private AttachedStore? store;
    private readonly FakeTimeProvider time = new(DateTimeOffset.UtcNow);

    [TearDown]
    public async Task TearDown()
    {
        if (store is not null)
        {
            await store.DisposeAsync();
            store = null;
        }

        if (host is not null)
        {
            await host.DisposeAsync();
            host = null;
        }

        try
        {
            if (node is not null)
            {
                await node.DisposeAsync();
            }
        }
        finally
        {
            node = null;
        }

        if (worker is not null)
        {
            await workerScheduler.DisposeAsync();
            await worker.DisposeAsync();
            worker = null;
        }

        database?.Dispose();
        database = null;
    }

    [Test]
    public async Task AnHttpTargetServesReadsAndAPause()
    {
        McpClient @operator = await StartWithHttpTarget();

        JsonElement details = McpTestHost.Answer(await McpTestHost.Call(@operator, "get_scheduler", new { scheduler = "w1/" + TestData.SchedulerName }));
        JsonElement paused = McpTestHost.Answer(await McpTestHost.Call(@operator, "pause_trigger", new
        {
            scheduler = "w1/" + TestData.SchedulerName,
            group = "reports",
            name = "nightly",
            reason = "paused from the fleet",
        }));

        details.GetProperty("schedulerInstanceId").GetString().Should().Be(TestData.Wire.Metadata.SchedulerInstanceId, "read through the worker's HTTP API");
        details.GetProperty("scheduler").GetProperty("origin").GetString().Should().Be(nameof(SchedulerOrigin.Remote));
        details.GetProperty("scheduler").TryGetProperty("lastSeenUtc", out _).Should().BeFalse("an HTTP target answered live");
        paused.GetProperty("applied").GetBoolean().Should().BeTrue();
        A.CallTo(() => workerScheduler.PauseTriggerWith(new TriggerKey("nightly", "reports"), A<PauseDetails>.That.Matches(x => x.RequestedBy == "quartz-operator-user via MCP"), A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task AnAgentServesReadsAndAPauseAndSaysWhenItWasLastHeardFrom()
    {
        host = await McpTestHost.Start(services: services => services.AddSingleton<TimeProvider>(time));
        DateTimeOffset heartbeat = time.GetUtcNow().AddSeconds(-4);
        IScheduler agent = BindAgent("a1", heartbeat);
        A.CallTo(() => agent.PauseTriggerWith(A<TriggerKey>._, A<PauseDetails>._, A<CancellationToken>._)).Returns(true);
        McpClient @operator = await host.Connect(RoleAuthenticationExtensions.Operator);

        JsonElement details = McpTestHost.Answer(await McpTestHost.Call(@operator, "get_scheduler", new { scheduler = "a1/AgentScheduler" }));
        JsonElement paused = McpTestHost.Answer(await McpTestHost.Call(@operator, "pause_trigger", new { scheduler = "a1/AgentScheduler", group = "g", name = "t", reason = "through the agent" }));

        details.GetProperty("scheduler").GetProperty("origin").GetString().Should().Be(nameof(SchedulerOrigin.Agent));
        details.GetProperty("scheduler").GetProperty("lastSeenUtc").GetDateTimeOffset().Should().Be(heartbeat, "the agent's heartbeat, heard rather than asked");
        details.GetProperty("asOfUtc").GetDateTimeOffset().Should().Be(time.GetUtcNow(), "when the read completed, on this server's clock");
        paused.GetProperty("applied").GetBoolean().Should().BeTrue();
    }

    [Test]
    public async Task AnAgentThatIsNotConnectedIsAToolErrorThatSaysSo()
    {
        host = await McpTestHost.Start();
        IScheduler agent = BindAgent("a1", lastSeen: null);
        A.CallTo(() => agent.GetMetadata(A<CancellationToken>._))
            .ThrowsAsync(new HttpRequestException(HttpRequestError.ConnectionError, "Agent 'a1' is not connected (10.0.0.7:5000)."));
        McpClient viewer = await host.Connect(RoleAuthenticationExtensions.Viewer);

        JsonElement error = McpTestHost.Error(await McpTestHost.Call(viewer, "get_scheduler", new { scheduler = "a1/AgentScheduler" }));

        error.GetProperty("code").GetString().Should().Be("target_fault");
        error.GetProperty("fault").GetString().Should().Be("unavailable");
        error.GetProperty("message").GetString().Should().Be("Target 'a1' is not reachable.").And.NotContain("10.0.0.7", "never the target's address");
        host.Logs.WithEventId(9010).Should().ContainSingle();
    }

    [Test]
    public async Task AChangeThatTimedOutSaysItMayOrMayNotHaveBeenApplied()
    {
        host = await McpTestHost.Start();
        IScheduler agent = BindAgent("a1", lastSeen: null);
        A.CallTo(() => agent.PauseTriggerWith(A<TriggerKey>._, A<PauseDetails>._, A<CancellationToken>._)).ThrowsAsync(new OperationCanceledException());
        A.CallTo(() => agent.GetTriggerState(A<TriggerKey>._, A<CancellationToken>._)).ThrowsAsync(new OperationCanceledException());
        McpClient @operator = await host.Connect(RoleAuthenticationExtensions.Operator);

        JsonElement change = McpTestHost.Error(await McpTestHost.Call(@operator, "pause_trigger", new { scheduler = "a1/AgentScheduler", group = "g", name = "t", reason = "slow agent" }));

        change.GetProperty("fault").GetString().Should().Be("timedOut");
        change.GetProperty("mayHaveApplied").GetBoolean().Should().BeTrue();
        change.GetProperty("message").GetString().Should().Contain(TargetFaultException.MayHaveAppliedSentence);

        JsonElement read = McpTestHost.Error(await McpTestHost.Call(@operator, "get_trigger", new { scheduler = "a1/AgentScheduler", group = "g", name = "t" }));
        read.GetProperty("fault").GetString().Should().Be("timedOut");
        read.GetProperty("message").GetString().Should().NotContain("may or may not", "a read that timed out changed nothing");
    }

    [Test]
    public async Task AWindowOntoAnAttachedStoreServesReadsAndAPauseAndCountsItsLiveNodes()
    {
        await StartWithWindow();
        McpClient @operator = await host!.Connect(RoleAuthenticationExtensions.Operator);

        JsonElement jobs = McpTestHost.Answer(await McpTestHost.Call(@operator, "list_jobs", new { scheduler = "prod/alpha" }));
        JsonElement paused = McpTestHost.Answer(await McpTestHost.Call(@operator, "pause_trigger", new { scheduler = "prod/alpha", group = "nightly", name = "at-midnight", reason = "on the shared tables" }));

        jobs.GetProperty("jobs")[0].GetProperty("name").GetString().Should().Be("nightly");
        JsonElement freshness = jobs.GetProperty("scheduler");
        freshness.GetProperty("key").GetString().Should().Be("prod/alpha");
        freshness.GetProperty("origin").GetString().Should().Be(nameof(SchedulerOrigin.Window));
        freshness.GetProperty("liveNodes").GetInt32().Should().Be(1, "one node is still checking in");
        freshness.GetProperty("nodes").GetInt32().Should().Be(2, "and two have a check-in row");

        paused.GetProperty("applied").GetBoolean().Should().BeTrue();
        IScheduler theNode = await node!.GetRequiredService<ISchedulerFactory>().GetScheduler();
        (await theNode.GetTriggerState(new TriggerKey("at-midnight", "nightly"))).Should().Be(TriggerState.Paused, "a window writes the store the node reads");
    }

    /// <summary>
    /// A window is registered under its bare name, and a call naming it so reaches the scheduler behind its store's target:
    /// the policies are asked about that target, so a policy that refuses <c>prod</c> refuses <c>alpha</c> as it refuses
    /// <c>prod/alpha</c> (#4245 review).
    /// </summary>
    [Test]
    public async Task AWindowNamedByItsBareNameIsAskedAboutWithItsStoresTarget()
    {
        await StartWithWindow(
            mcp: options => options.SchedulerAuthorizationPolicy = NotProd,
            services: services => services.AddAuthorizationBuilder().AddPolicy(NotProd, policy => policy.RequireAssertion(static context =>
                context.Resource is not SchedulerResource { Target: "prod" })));
        McpClient @operator = await host!.Connect(RoleAuthenticationExtensions.Operator);

        JsonElement targeted = McpTestHost.Error(await McpTestHost.Call(@operator, "list_jobs", new { scheduler = "prod/alpha" }));
        JsonElement bareRead = McpTestHost.Error(await McpTestHost.Call(@operator, "list_jobs", new { scheduler = "alpha" }));
        JsonElement bareChange = McpTestHost.Error(await McpTestHost.Call(@operator, "pause_trigger", new { scheduler = "alpha", group = "nightly", name = "at-midnight", reason = "around the policy" }));

        targeted.GetProperty("code").GetString().Should().Be("forbidden");
        bareRead.GetProperty("code").GetString().Should().Be("forbidden", "the bare name reaches prod's scheduler, so the policy is asked about prod");
        bareChange.GetProperty("code").GetString().Should().Be("forbidden");
        IScheduler theNode = await node!.GetRequiredService<ISchedulerFactory>().GetScheduler();
        (await theNode.GetTriggerState(new TriggerKey("at-midnight", "nightly"))).Should().Be(TriggerState.Normal, "a refused change reaches no store");
    }

    private const string NotProd = "NotProd";

    [Test]
    public async Task ListSchedulersNamesEveryKeyOfTheFleet()
    {
        host = await McpTestHost.Start();
        BindAgent("a1", lastSeen: DateTimeOffset.UtcNow);

        JsonElement listing = McpTestHost.Answer(await McpTestHost.Call(await host.Connect(RoleAuthenticationExtensions.Viewer), "list_schedulers"));

        listing.GetProperty("schedulers").EnumerateArray().Select(static x => x.GetProperty("key").GetString())
            .Should().Contain([host.Scheduler.SchedulerName, "a1/AgentScheduler"]);
    }

    /// <summary>
    /// The check-in row a clustered node writes: the timestamp as UTC ticks, the interval as whole milliseconds.
    /// </summary>
    private void WriteCheckIn(string schedulerName, string instanceId, DateTimeOffset checkedInAt)
    {
        using SqliteConnection connection = new(database!.ConnectionString);
        connection.Open();

        using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "INSERT INTO QRTZ_SCHEDULER_STATE (SCHED_NAME, INSTANCE_NAME, LAST_CHECKIN_TIME, CHECKIN_INTERVAL) "
                              + "VALUES (@scheduler, @instance, @checkin, @interval)";
        command.Parameters.AddWithValue("@scheduler", schedulerName);
        command.Parameters.AddWithValue("@instance", instanceId);
        command.Parameters.AddWithValue("@checkin", checkedInAt.UtcTicks);
        command.Parameters.AddWithValue("@interval", 7500L);
        command.ExecuteNonQuery();
    }

    private IScheduler BindAgent(string target, DateTimeOffset? lastSeen)
    {
        IScheduler agent = A.Fake<IScheduler>();
        A.CallTo(() => agent.SchedulerName).Returns("AgentScheduler");
        A.CallTo(() => agent.GetMetadata(A<CancellationToken>._)).Returns(TestData.Wire.Metadata);
        host!.Services.GetRequiredService<SchedulerTargets>().Add(new SchedulerTarget
        {
            Name = target,
            Origin = SchedulerOrigin.Agent,
            Liveness = () => new TargetLiveness(Status: null, SchedulerInstanceId: "agent-node", LastSeenUtc: lastSeen),
        });
        host.Services.GetRequiredService<ISchedulerRepository>().Bind(new FakeProxyScheduler(agent, target, SchedulerOrigin.Agent), target);
        return agent;
    }

    private async Task<McpClient> StartWithHttpTarget()
    {
        TestContentRoot.Apply();
        worker = new WebApplicationFactory<Program>();
        ISchedulerRepository repository = worker.Services.GetRequiredService<ISchedulerRepository>();
        foreach (IScheduler bound in repository.LookupAll())
        {
            repository.Remove(bound.SchedulerName);
        }

        workerScheduler = A.Fake<IScheduler>();
        A.CallTo(() => workerScheduler.SchedulerName).Returns(TestData.SchedulerName);
        A.CallTo(() => workerScheduler.GetMetadata(A<CancellationToken>._)).Returns(TestData.Wire.Metadata);
        A.CallTo(() => workerScheduler.PauseTriggerWith(A<TriggerKey>._, A<PauseDetails>._, A<CancellationToken>._)).Returns(true);
        repository.Bind(workerScheduler);
        HttpClient toWorker = worker.CreateClient();

        host = await McpTestHost.Start(services: services => services.AddQuartzHttpClient(options =>
        {
            options.SchedulerName = TestData.SchedulerName;
            options.Target = "w1";
            options.CreateHttpClient = _ => toWorker;
        }));

        return await host.Connect(RoleAuthenticationExtensions.Operator);
    }

    private async Task StartWithWindow(Action<QuartzMcpOptions>? mcp = null, Action<IServiceCollection>? services = null)
    {
        database = new SqliteTestDatabase("mcp-window");

        ServiceCollection nodeServices = new();
        nodeServices.AddLogging();
        nodeServices.AddQuartz(quartz =>
        {
            quartz.ConfigureScheduler(options => options.InstanceName = "alpha");
            quartz.UsePersistentStore(persistent =>
            {
                persistent.UseSqlite(SqliteFactory.Instance, database.ConnectionString);
                persistent.ProvisionSchema();
            });
        });
        node = nodeServices.BuildServiceProvider();
        IScheduler theNode = await node.GetRequiredService<ISchedulerFactory>().GetScheduler();
        await theNode.ScheduleJob(
            JobBuilder.Create<DummyJob>().WithIdentity("nightly", "batch").StoreDurably().Build(),
            TriggerBuilder.Create().WithIdentity("at-midnight", "nightly").StartAt(DateTimeOffset.UtcNow.AddYears(1)).Build());
        // SQLite cannot run clustered, so the cluster's check-ins are written as a clustered store writes them: one node
        // live, one convicted long ago.
        WriteCheckIn("alpha", "node-live", DateTimeOffset.UtcNow);
        WriteCheckIn("alpha", "node-gone", DateTimeOffset.UtcNow.AddHours(-1));

        host = await McpTestHost.Start(mcp: mcp, services: services);
        store = new AttachedStore("prod", recipe => recipe.UseSqlite(SqliteFactory.Instance, database.ConnectionString), host.Services);
        (await store.Synchronize()).Should().Equal(["alpha"]);
    }
}
