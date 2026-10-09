using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;

using Microsoft.Extensions.Options;

using Quartz.Dashboard.Services;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

using AgentRequest = Quartz.HttpApiContract.AgentRequest;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// What the agent does with the dashboard's requests before it runs them: a request is in flight from the
/// moment it arrives, so a cancel or a timeout for one still waiting its turn lands, and a queue that is
/// full says so at once rather than holding the connection.
/// </summary>
/// <remarks>
/// Every case holds the agent's one worker on a <c>TriggerJob</c> whose scheduler listener waits at a
/// gate, which is what makes "still queued" a state a test can be in rather than a race it has to win.
/// </remarks>
public sealed class AgentQueueTest
{
    private static readonly JobKey First = new("first", "queue");
    private static readonly JobKey Second = new("second", "queue");

    [SetUp]
    public void Reset() => MarkerJob.Fired.Clear();

    /// <summary>
    /// A cancel for a request the agent has queued but not started cancels it there: when its turn comes
    /// the worker skips it, and the mutation the dashboard already reported as abandoned never runs.
    /// </summary>
    [Test]
    public async Task ACancelForARequestStillQueuedLandsAndTheRequestNeverRuns()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        GateListener gate = new();
        AgentWorker worker = await dashboard.StartAgent(
            "w1",
            options => options.MaxConcurrentOperations = 1,
            configureQuartz: quartz => quartz.AddSchedulerListener(gate));
        await AddJobs(worker);

        // The one worker is inside the first request, held at the gate.
        gate.Hold(First);
        Task running = dashboard.Client.TriggerJob(worker.Key, new JobKeyDto(First.Group, First.Name)).AsTask();
        await gate.Entered(First);

        // The second request waits behind it, with an id this test chose.
        AgentEntry entry = dashboard.Registry.Find("w1")!;
        WireRequest trigger = SchedulerRoutes.TriggerJob.For(worker.Scheduler.SchedulerName, Second.Group, Second.Name);
        await dashboard.Registry.Execute(entry, new AgentRequest { Id = "queued", Method = trigger.Route.Method, Path = trigger.Path }, CancellationToken.None);
        await Eventually.Until(() => worker.Plugin.Connection!.Holds("queued"));

        // The cancel reaches a request that has not started, not only one that has.
        await dashboard.Registry.Cancel(entry, "queued");
        await Eventually.Until(() => worker.Plugin.Connection!.IsCancelled("queued"));

        gate.Release(First);
        await running;

        // A third request answered proves the worker moved on past the cancelled one.
        await dashboard.Client.GetScheduler(worker.Key);
        await Eventually.Until(() => !worker.Plugin.Connection!.Holds("queued"));

        gate.Scheduled.Should().Equal([First], "the cancelled request was skipped when its turn came, not run late");
        MarkerJob.Fired.Should().NotContain(Second.Name);
    }

    /// <summary>
    /// The queue holds <see cref="AgentConnection.QueueFactor" /> requests per worker; one more is
    /// answered <c>503</c> the moment it arrives, and the connection stays up for everything else.
    /// </summary>
    [Test]
    public async Task ARequestThatFindsTheQueueFullIsAnsweredServiceUnavailableAtOnce()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        GateListener gate = new();
        AgentWorker worker = await dashboard.StartAgent(
            "w1",
            options => options.MaxConcurrentOperations = 1,
            configureQuartz: quartz => quartz.AddSchedulerListener(gate));
        await AddJobs(worker);

        gate.Hold(First);
        Task running = dashboard.Client.TriggerJob(worker.Key, new JobKeyDto(First.Group, First.Name)).AsTask();
        await gate.Entered(First);

        List<Task<SchedulerDetailDto>> waiting = [];
        for (int i = 0; i < AgentConnection.QueueFactor; i++)
        {
            waiting.Add(dashboard.Client.GetScheduler(worker.Key).AsTask());
        }

        // One request is running and the queue behind it is full.
        await Eventually.Until(() => worker.Plugin.Connection!.PendingCount == AgentConnection.QueueFactor + 1);

        Func<Task> oneTooMany = () => dashboard.Client.GetScheduler(worker.Key).AsTask();
        (await oneTooMany.Should().ThrowAsync<HttpClientException>("the agent refuses it rather than parking the receive loop"))
            .Which.Message.Should().Contain(AgentConnection.QueueFullDetail);
        worker.Logs.WithEventId(9304).Should().ContainSingle().Which.Message.Should().Contain(AgentConnection.QueueFullDetail);

        gate.Release(First);
        await running;
        await Task.WhenAll(waiting);
        waiting.Should().OnlyContain(answered => answered.Result.SchedulerInstanceId == worker.Scheduler.SchedulerInstanceId,
            "the queued requests were answered in their turn");
    }

    /// <summary>
    /// An operation the agent does not answer within <see cref="DashboardAgentHubOptions.OperationTimeout" />
    /// is cancelled on both sides, on the dashboard's clock: the caller hears which operation and how long,
    /// and the agent stops the work rather than finishing it for nobody.
    /// </summary>
    [Test]
    public async Task AnOperationTheAgentDoesNotAnswerInTimeIsCancelledOnBothSidesOnTheDashboardsClock()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start(agents => agents.OperationTimeout = TimeSpan.FromSeconds(5));
        GateListener gate = new();
        AgentWorker worker = await dashboard.StartAgent("w1", configureQuartz: quartz => quartz.AddSchedulerListener(gate));
        await AddJobs(worker);

        gate.Hold(First);
        Task running = dashboard.Client.TriggerJob(worker.Key, new JobKeyDto(First.Group, First.Name)).AsTask();
        await gate.Entered(First);

        Stopwatch waited = Stopwatch.StartNew();
        dashboard.Clock.Advance(TimeSpan.FromSeconds(6));

        Func<Task> outcome = () => running;
        (await outcome.Should().ThrowAsync<OperationCanceledException>("the timeout is the dashboard's, and it ran on the dashboard's clock"))
            .WithMessage("*TriggerJob*00:00:05*");
        waited.Stop();
        waited.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "the timeout fired when the dashboard's clock moved, not when the wall clock did");

        await gate.Cancelled(First);
        gate.Release(First);
    }

    /// <summary>
    /// A timeout above what a timer can wait would throw out of the <c>CancellationTokenSource</c> on the
    /// receive loop, with the request never answered; the options refuse it before any agent is dialled.
    /// </summary>
    [Test]
    public async Task AnOperationTimeoutLongerThanATimerCanWaitIsRefusedAtStartup()
    {
        Func<Task> start = () => AgentDashboard.Start(agents => agents.OperationTimeout = TimeSpan.FromDays(50));

        (await start.Should().ThrowAsync<OptionsValidationException>())
            .WithMessage("*OperationTimeout*at most*");
    }

    /// <summary>
    /// The same bound on the agent's side, for a dashboard that sent one anyway: answered <c>400</c> from
    /// the receive loop, which then goes on to the next message.
    /// </summary>
    [Test]
    public async Task ARequestWhoseTimeoutIsLongerThanATimerCanWaitIsAnsweredBadRequest()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1");

        AgentEntry entry = dashboard.Registry.Find("w1")!;
        TaskCompletionSource<WireResponse> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Pending["too-long"] = answer;
        WireRequest details = SchedulerRoutes.GetSchedulerDetails.For(worker.Scheduler.SchedulerName);
        await dashboard.Registry.Execute(
            entry,
            new AgentRequest { Id = "too-long", Method = details.Route.Method, Path = details.Path, Timeout = TimeSpan.FromDays(50) },
            CancellationToken.None);

        WireResponse refused = await answer.Task.WaitAsync(TimeSpan.FromSeconds(10));
        refused.Status.Should().Be(HttpStatusCode.BadRequest);
        System.Text.Encoding.UTF8.GetString(refused.Body).Should().Contain("longer than a timer can wait");
        worker.Plugin.Connection!.Holds("too-long").Should().BeFalse("nothing was queued");
        worker.Logs.WithEventId(9304).Should().ContainSingle();

        (await dashboard.Client.GetScheduler(worker.Key)).SchedulerInstanceId.Should().Be(worker.Scheduler.SchedulerInstanceId,
            "the receive loop survived the refusal");
    }

    /// <summary>
    /// A request under an id still in flight is answered <c>400</c> and not queued: a second under the
    /// same id would orphan the first and run both against the second's cancellation.
    /// </summary>
    [Test]
    public async Task ARequestWhoseIdIsAlreadyInFlightIsAnsweredBadRequestAndTheFirstKeepsRunning()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        GateListener gate = new();
        AgentWorker worker = await dashboard.StartAgent("w1", configureQuartz: quartz => quartz.AddSchedulerListener(gate));
        await AddJobs(worker);

        AgentEntry entry = dashboard.Registry.Find("w1")!;
        WireRequest trigger = SchedulerRoutes.TriggerJob.For(worker.Scheduler.SchedulerName, First.Group, First.Name);
        AgentRequest request = new() { Id = "twice", Method = trigger.Route.Method, Path = trigger.Path };

        gate.Hold(First);
        await dashboard.Registry.Execute(entry, request, CancellationToken.None);
        await gate.Entered(First);

        TaskCompletionSource<WireResponse> answer = new(TaskCreationOptions.RunContinuationsAsynchronously);
        entry.Pending["twice"] = answer;
        await dashboard.Registry.Execute(entry, request, CancellationToken.None);

        WireResponse refused = await answer.Task.WaitAsync(TimeSpan.FromSeconds(10));
        refused.Status.Should().Be(HttpStatusCode.BadRequest);
        System.Text.Encoding.UTF8.GetString(refused.Body).Should().Contain("already in flight");
        worker.Plugin.Connection!.Holds("twice").Should().BeTrue("the first request is still running");
        worker.Plugin.Connection!.IsCancelled("twice").Should().BeFalse("and was not touched by the refusal");

        gate.Release(First);
        await Eventually.Until(() => !worker.Plugin.Connection!.Holds("twice"));
        gate.Scheduled.Should().Equal([First], "the first ran once, and the duplicate never ran");
    }

    private static async Task AddJobs(AgentWorker worker)
    {
        await worker.Scheduler.AddJob(JobBuilder.Create<MarkerJob>().WithIdentity(First).StoreDurably().Build());
        await worker.Scheduler.AddJob(JobBuilder.Create<MarkerJob>().WithIdentity(Second).StoreDurably().Build());
    }

    /// <summary>
    /// A scheduler listener that holds <c>JobScheduled</c> for one job at a gate, and records every job it
    /// saw scheduled and every hold that was cancelled rather than released.
    /// </summary>
    private sealed class GateListener : ISchedulerListener
    {
        private readonly ConcurrentDictionary<JobKey, Gate> holds = new();

        public ConcurrentQueue<JobKey> Scheduled { get; } = new();

        public void Hold(JobKey job) => holds[job] = new Gate();

        public Task Entered(JobKey job) => holds[job].Entered.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public Task Cancelled(JobKey job) => holds[job].Cancelled.Task.WaitAsync(TimeSpan.FromSeconds(10));

        public void Release(JobKey job) => holds[job].Released.TrySetResult();

        public async ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
        {
            Scheduled.Enqueue(trigger.JobKey);

            if (!holds.TryGetValue(trigger.JobKey, out Gate? hold))
            {
                return;
            }

            hold.Entered.TrySetResult();
            try
            {
                await hold.Released.Task.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                hold.Cancelled.TrySetResult();
                throw;
            }
        }

        private sealed class Gate
        {
            public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Released { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

            public TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        }
    }

    public sealed class MarkerJob : IJob
    {
        public static readonly ConcurrentBag<string> Fired = [];

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Fired.Add(context.JobDetail.Key.Name);
            return default;
        }
    }
}
