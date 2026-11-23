using System.Net;

using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;

using Quartz.Dashboard.Services;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// The HTTP API serving a scheduler whose process dialled in (#4218): <c>?target=w1</c> reaches the agent
/// through the <c>HttpScheduler</c> the dashboard drives it with, and the agent's own decisions and its
/// connection's state are answered as the target's.
/// </summary>
public sealed class GatewayTargetOverAgentTest
{
    private static readonly JobKey Job = new("nightly", "gateway");
    private const string SchedulerUrl = "schedulers/" + AgentDashboard.SchedulerName;

    [Test]
    public async Task AnAgentIsReadPausedTriggeredAndItsRefusalIsTheTargetsOwn()
    {
        await using AgentDashboard dashboard = await StartDashboard();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.MaxAccess = SchedulerAccess.Operate);
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build());
        await worker.Scheduler.ScheduleJob(TriggerBuilder.Create()
            .WithIdentity("at-midnight", "gateway")
            .ForJob(Job)
            .StartAt(DateTimeOffset.UtcNow.AddYears(1))
            .Build());
        using HttpClient api = Api(dashboard);

        using HttpResponseMessage jobs = await api.GetAsync($"{SchedulerUrl}/jobs?target=w1");
        jobs.StatusCode.Should().Be(HttpStatusCode.OK, await jobs.Content.ReadAsStringAsync());
        jobs.Headers.GetValues(HttpApiConstants.TargetHeader).Should().Equal(["w1"]);
        (await jobs.Content.ReadAsStringAsync()).Should().Contain("nightly", "the jobs are the worker's, read over its own connection");

        using HttpResponseMessage paused = await api.PostAsync($"{SchedulerUrl}/triggers/gateway/at-midnight/pause?target=w1", content: null);
        paused.StatusCode.Should().Be(HttpStatusCode.OK);
        (await worker.Scheduler.GetTriggerState(new TriggerKey("at-midnight", "gateway"))).Should().Be(TriggerState.Paused);

        using HttpResponseMessage triggered = await api.PostAsync($"{SchedulerUrl}/jobs/gateway/nightly/trigger?target=w1", content: null);
        triggered.StatusCode.Should().Be(HttpStatusCode.OK, "a trigger-now without data is Operate, which the agent accepts");

        using HttpResponseMessage history = await api.GetAsync($"{SchedulerUrl}/history/executions?target=w1");
        history.StatusCode.Should().Be(HttpStatusCode.OK, "the agent's history is read over its connection, as the dashboard reads it");

        using HttpResponseMessage unscheduled = await api.PostAsync($"{SchedulerUrl}/triggers/gateway/at-midnight/unschedule?target=w1", content: null);
        unscheduled.StatusCode.Should().Be(HttpStatusCode.Forbidden,
            "the agent accepts at most Operate, and its refusal is a rule at the target, not a scheduler's failure");
        (await unscheduled.Content.ReadAsStringAsync()).Should()
            .Contain("Target 'w1' refused: ")
            .And.Contain(AgentCarrier.MaxAccessDetail("UnscheduleJob", SchedulerAccess.Administer, SchedulerAccess.Operate),
                "the target's detail names the operation and the level it needs");
        (await worker.Scheduler.GetTrigger(new TriggerKey("at-midnight", "gateway"))).Should().NotBeNull();
    }

    [Test]
    public async Task AnAgentThatIsOfflineIsUnavailable()
    {
        await using AgentDashboard dashboard = await StartDashboard();
        AgentWorker worker = await dashboard.StartAgent("w1");
        await worker.Vanish();
        using HttpClient api = Api(dashboard);

        using HttpResponseMessage response = await api.GetAsync($"{SchedulerUrl}?target=w1");

        response.StatusCode.Should().Be(HttpStatusCode.ServiceUnavailable, "nothing answers for an agent that is not connected");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Target 'w1' is not reachable.");
        dashboard.Logs.WithEventId(9010).Should().ContainSingle();
    }

    [Test]
    public async Task AnAgentThatDoesNotAnswerInTimeIsAGatewayTimeoutAndTheChangeMayHaveHappened()
    {
        HoldingListener holding = new();
        await using AgentDashboard dashboard = await StartDashboard(TimeSpan.FromSeconds(5));
        AgentWorker worker = await dashboard.StartAgent("w1", configureQuartz: quartz => quartz.AddSchedulerListener(holding));
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build());
        using HttpClient api = Api(dashboard);

        Task<HttpResponseMessage> running = api.PostAsync($"{SchedulerUrl}/jobs/gateway/nightly/trigger?target=w1", content: null);
        await holding.Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        dashboard.Clock.Advance(TimeSpan.FromSeconds(6));

        using HttpResponseMessage response = await running;
        holding.Release.TrySetResult();

        response.StatusCode.Should().Be(HttpStatusCode.GatewayTimeout, "the agent's OperationTimeout is the gateway's, on the dashboard's clock");
        (await response.Content.ReadAsStringAsync()).Should().Contain("Target 'w1' did not answer in time.")
            .And.Contain(TargetFaultException.MayHaveAppliedSentence, "the trigger-now reached the agent before the answer was lost");
        dashboard.Logs.WithEventId(9011).Should().ContainSingle();
    }

    private static Task<AgentDashboard> StartDashboard(TimeSpan? operationTimeout = null)
    {
        return AgentDashboard.Start(
            configureAgents: agents =>
            {
                if (operationTimeout is { } timeout)
                {
                    agents.OperationTimeout = timeout;
                }
            },
            configureServices: services => services.AddQuartzHttpApi(),
            configureApp: app => app.MapQuartzHttpApi("/quartz-api").AllowAnonymous());
    }

    private static HttpClient Api(AgentDashboard dashboard)
    {
        HttpClient client = dashboard.App.GetTestClient();
        client.BaseAddress = new Uri(client.BaseAddress!, "quartz-api/");
        return client;
    }

    /// <summary>
    /// Holds the first <c>JobScheduled</c> it sees until released, so the agent cannot answer the request
    /// that scheduled it in time.
    /// </summary>
    private sealed class HoldingListener : ISchedulerListener
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            if (Entered.TrySetResult())
            {
                try
                {
                    await Release.Task.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    // The dashboard gave up and cancelled the operation on the agent, which is what this waits for.
                }
            }
        }
    }
}
