using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

using AgentProtocol = Quartz.HttpApiContract.AgentProtocol;
using AgentRequest = Quartz.HttpApiContract.AgentRequest;
using SchedulerEvent = Quartz.HttpApiContract.SchedulerEvent;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// What becomes of an agent's row when its process goes: unknown while it is away, the same row when the
/// same instance comes back, shut down when it said goodbye, and gone once it has been away for
/// <see cref="DashboardAgentHubOptions.ForgetAfter" />.
/// </summary>
public sealed class AgentDisconnectTest
{
    [Test]
    public async Task AVanishedAgentIsUnknownTheSameInstanceTakesItsRowBackAndAnAbsentOneIsForgotten()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker first = await dashboard.StartAgent("w1", instanceId: "node-1");

        await first.Vanish();
        await Eventually.Until(() => dashboard.Registry.Find("w1") is { ConnectionId: null });

        SchedulerHeaderDto away = (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == first.Key).Subject;
        away.Status.Should().Be(SchedulerStatus.Unknown, "a disconnected agent stays listed, as unreachable, until it is forgotten");
        away.LastSeenUtc.Should().NotBeNull();
        dashboard.Logs.WithEventId(9114).Should().ContainSingle("the disconnect is logged once");

        Func<Task> ask = () => dashboard.Client.GetScheduler(first.Key).AsTask();
        await ask.Should().ThrowAsync<HttpRequestException>("a page asking the agent itself is told it is offline, at once, rather than waiting out a timeout");

        // The same instance back on a new connection takes its own row over.
        AgentWorker second = await dashboard.StartAgent("w1", instanceId: "node-1");

        SchedulerHeaderDto back = (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == second.Key).Subject;
        back.Status.Should().Be(SchedulerStatus.Running);
        back.Key.Should().Be(first.Key, "the same target and the same instance are the same row");

        await second.Vanish();
        await Eventually.Until(() => dashboard.Registry.Find("w1") is { ConnectionId: null });

        // An hour and a sweep later, with nobody back, the row is gone and the target with it.
        dashboard.Clock.Advance(TimeSpan.FromHours(1) + TimeSpan.FromSeconds(16));

        (await dashboard.Client.GetSchedulers()).Should().NotContain(x => x.Key == first.Key, "the agent has been gone for ForgetAfter");
        dashboard.App.Services.GetRequiredService<SchedulerTargets>().Snapshot().Should().BeEmpty("the target went with the row");
        dashboard.App.Services.GetRequiredService<ISchedulerRepository>().LookupAll().Should().BeEmpty("and so did the proxy");
        dashboard.Logs.WithEventId(9115).Should().ContainSingle();
    }

    /// <summary>
    /// A transport that drops under the connection — a proxy restarting, a network blip — is SignalR's
    /// own reconnect, not a new dial: the client comes back with a new connection id, and the agent has
    /// to register again on it, end what the old session was running, and still shut down cleanly after.
    /// </summary>
    /// <remarks>
    /// The worker's clock is never advanced, so its first session's heartbeat loop is parked in a delay
    /// that only cancellation ends. A reconnect that did not cancel that session would wait on it for
    /// ever, never re-register, and hang the scheduler's shutdown behind it.
    /// </remarks>
    [Test]
    public async Task ADroppedTransportReconnectsAndRegistersAgainOnTheSameRowAndTheWorkerStillShutsDown()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", instanceId: "node-1");
        AgentEntry entry = dashboard.Registry.Find("w1")!;
        string firstConnection = entry.ConnectionId!;

        // Long polling has no keep-alive of its own, so a client learns its transport is gone on its next
        // request: a request down the connection makes it answer, and the answer is what fails.
        dashboard.TransportDown = true;
        await dashboard.Registry.Execute(entry, new AgentRequest { Id = "poke", Method = "GET", Path = "nowhere" }, CancellationToken.None);
        await Eventually.Until(() => !worker.Plugin.Connection!.IsRegistered);
        dashboard.TransportDown = false;

        await Eventually.Until(() => dashboard.Registry.Find("w1") is { ConnectionId: { } id }
            && !string.Equals(id, firstConnection, StringComparison.Ordinal)
            && worker.Plugin.Connection!.IsRegistered);

        (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == worker.Key)
            .Which.Status.Should().Be(SchedulerStatus.Running, "the same instance on a new connection took its own row over");
        dashboard.Logs.WithEventId(9112).Should().HaveCount(2, "the agent registered at start and again after the reconnect");
        worker.Logs.WithEventId(9300).Should().HaveCount(2);

        Func<Task> shutdown = () => worker.Scheduler.Shutdown(waitForJobsToComplete: false).AsTask();
        await shutdown.Should().CompleteWithinAsync(TimeSpan.FromSeconds(20),
            "the reconnected session's loops are cancelled by the shutdown, not waited out on a clock nobody advances");
    }

    /// <summary>
    /// A Live Logs page open across a reconnect keeps getting events: the dashboard asks the new
    /// connection to stream again before it answers the registration, and the agent keeps that stream
    /// rather than stopping the one it just opened.
    /// </summary>
    [Test]
    public async Task ALiveLogsSubscriptionSurvivesTheAgentReconnecting()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", instanceId: "node-1");

        ISchedulerEventSource source = SchedulerEventSources.For(dashboard.App.Services, worker.Key)!;
        using CancellationTokenSource subscription = new();
        IAsyncEnumerator<SchedulerEvent> events = source.Subscribe(worker.Scheduler.SchedulerName, subscription.Token).GetAsyncEnumerator(CancellationToken.None);
        Task<bool> reading = events.MoveNextAsync().AsTask();
        await Eventually.Until(() => worker.Plugin.Connection!.IsWatching);

        AgentEntry entry = dashboard.Registry.Find("w1")!;
        string firstConnection = entry.ConnectionId!;
        dashboard.TransportDown = true;
        await dashboard.Registry.Execute(entry, new AgentRequest { Id = "poke", Method = "GET", Path = "nowhere" }, CancellationToken.None);
        await Eventually.Until(() => !worker.Plugin.Connection!.IsRegistered);
        dashboard.TransportDown = false;

        await Eventually.Until(() => dashboard.Registry.Find("w1") is { ConnectionId: { } id }
            && !string.Equals(id, firstConnection, StringComparison.Ordinal)
            && worker.Plugin.Connection!.IsRegistered);

        JobKey job = new("after-reconnect", "agents");
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(job).StoreDurably().Build());
        await worker.Scheduler.TriggerJob(job);

        (await reading.WaitAsync(TimeSpan.FromSeconds(10))).Should().BeTrue("the stream was reopened on the new connection and the event came up it");
        events.Current.SchedulerInstanceId.Should().Be(worker.Scheduler.SchedulerInstanceId);
        worker.Plugin.Connection!.IsWatching.Should().BeTrue("the page is still looking");

        await subscription.CancelAsync();
        await events.DisposeAsync();
    }

    [Test]
    public async Task AnAgentThatSaysGoodbyeIsListedAsShutDownUntilItIsForgotten()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1");
        string key = worker.Key;

        await worker.Scheduler.Shutdown(waitForJobsToComplete: false);
        await Eventually.Until(() => dashboard.Registry.Find("w1") is { Unregistered: true });

        SchedulerHeaderDto gone = (await dashboard.Client.GetSchedulers()).Should().ContainSingle(x => x.Key == key).Subject;
        gone.Status.Should().Be(SchedulerStatus.Shutdown, "a goodbye is a fact, where a dropped connection is a question");
        dashboard.Logs.WithEventId(9114).Should().BeEmpty("a goodbye is not a disconnect to report");
    }

    /// <summary>
    /// The goodbye is heard before the connection goes (#4176). A message the SignalR client has only
    /// queued is not yet sent, and stopping the connection cancels the transport's read of its outgoing
    /// pipe, which drops whatever it had not read. So the agent waits for the hub's answer to
    /// <see cref="AgentProtocol.Unregister" /> and only then stops the connection.
    /// </summary>
    /// <remarks>
    /// The hub holds the goodbye until the test releases it. An agent that only queues its goodbye has
    /// nothing to wait for and finishes shutting down while the hub still holds it. An agent that waits for
    /// the answer cannot finish until the hold is lifted, or until its own five-second limit runs out.
    /// The one-second window is shorter than that limit.
    /// </remarks>
    [TestCase(false, TestName = "AShutdownLetsTheConnectionGoOnlyOnceTheHubHasHeardTheGoodbye(long polling)")]
    [TestCase(true, TestName = "AShutdownLetsTheConnectionGoOnlyOnceTheHubHasHeardTheGoodbye(WebSockets)")]
    public async Task AShutdownLetsTheConnectionGoOnlyOnceTheHubHasHeardTheGoodbye(bool kestrel)
    {
        HeldGoodbye goodbye = new();
        await using AgentDashboard dashboard = await AgentDashboard.Start(
            configureServices: services => services.Configure<HubOptions<DashboardAgentHub>>(hub => hub.AddFilter(goodbye)),
            kestrel: kestrel);
        AgentWorker worker = await dashboard.StartAgent("w1");

        Task running = worker.Scheduler.Shutdown(waitForJobsToComplete: false).AsTask();
        Func<Task> shutdown = () => running;
        Func<Task> arrival = () => goodbye.Arrived;

        try
        {
            await arrival.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10), "the agent says goodbye while its connection is up");
            await shutdown.Should().NotCompleteWithinAsync(TimeSpan.FromSeconds(1),
                "the agent stops its connection only once the hub has answered the goodbye, and the hub is still holding it");
        }
        finally
        {
            // Whatever the assertions said, so that the dashboard's own shutdown does not wait on a hub
            // invocation nobody will release.
            goodbye.Release();
        }

        await shutdown.Should().CompleteWithinAsync(TimeSpan.FromSeconds(10), "the hub answered the goodbye once it was released");
        dashboard.Registry.Find("w1")!.Unregistered.Should().BeTrue("the hub had handled the goodbye before the shutdown returned");

        await Eventually.Until(() => dashboard.Registry.Find("w1") is { ConnectionId: null });
        dashboard.Logs.WithEventId(9114).Should().BeEmpty("the connection went after the goodbye, so it is not a disconnect to report");
    }

    /// <summary>
    /// Holds the agent hub's <see cref="AgentProtocol.Unregister" /> until released, and records when one
    /// arrives.
    /// </summary>
    private sealed class HeldGoodbye : IHubFilter
    {
        private readonly TaskCompletionSource arrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Arrived => arrived.Task;

        public void Release() => released.TrySetResult();

        public async ValueTask<object?> InvokeMethodAsync(HubInvocationContext invocationContext, Func<HubInvocationContext, ValueTask<object?>> next)
        {
            if (string.Equals(invocationContext.HubMethodName, AgentProtocol.Unregister, StringComparison.Ordinal))
            {
                arrived.TrySetResult();
                await released.Task;
            }

            return await next(invocationContext);
        }
    }
}
