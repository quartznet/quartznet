using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

using SchedulerEvent = Quartz.HttpApiContract.SchedulerEvent;
using SchedulerEventKind = Quartz.HttpApiContract.SchedulerEventKind;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// Two workers whose schedulers share a name, each dialling the dashboard: two rows, two keys, and each
/// one's history and events are its own.
/// </summary>
public sealed class DashboardAgentTest
{
    [Test]
    public async Task TwoAgentsWithOneSchedulerNameAreTwoRowsReachedByTheirOwnKeys()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker w1 = await dashboard.StartAgent("w1");
        AgentWorker w2 = await dashboard.StartAgent("w2");

        List<SchedulerHeaderDto> schedulers = await dashboard.Client.GetSchedulers();

        schedulers.Select(x => x.Key).Should().Equal([w1.Key, w2.Key],
            "every worker's default scheduler is QuartzScheduler, and the target is what tells them apart");
        schedulers.Should().OnlyContain(x => x.Origin == SchedulerOrigin.Agent && x.Status == SchedulerStatus.Running);
        schedulers.Select(x => x.SchedulerInstanceId).Should().Equal([w1.Scheduler.SchedulerInstanceId, w2.Scheduler.SchedulerInstanceId]);

        (await dashboard.Client.GetScheduler(w2.Key)).SchedulerInstanceId.Should().Be(w2.Scheduler.SchedulerInstanceId,
            "a key reaches the process that registered under its target");
        dashboard.Logs.WithEventId(9112).Should().HaveCount(2, "each registration is logged");
    }

    [Test]
    public async Task AJobRunOnOneAgentShowsInItsOwnHistoryAndEventsAndNotTheOthers()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker w1 = await dashboard.StartAgent("w1");
        AgentWorker w2 = await dashboard.StartAgent("w2");

        ISchedulerEventSource? source = SchedulerEventSources.For(dashboard.App.Services, w1.Key);
        source.Should().NotBeNull("an agent's events are read through the tunnel");
        ISchedulerEventSource? other = SchedulerEventSources.For(dashboard.App.Services, w2.Key);

        using CancellationTokenSource subscription = new();
        IAsyncEnumerator<SchedulerEvent> events = source!.Subscribe(w1.Scheduler.SchedulerName, subscription.Token).GetAsyncEnumerator(CancellationToken.None);
        IAsyncEnumerator<SchedulerEvent> otherEvents = other!.Subscribe(w2.Scheduler.SchedulerName, subscription.Token).GetAsyncEnumerator(CancellationToken.None);
        Task<bool> reading = events.MoveNextAsync().AsTask();
        Task<bool> otherReading = otherEvents.MoveNextAsync().AsTask();

        // The subscription is what asks the agent to stream, so nothing is published until it has.
        await Eventually.Until(() => w1.Broker.HasSubscribers(w1.Scheduler.SchedulerName));

        JobKey job = new("watched", "agents");
        await w1.Scheduler.AddJob(JobBuilder.Create<RecordingJob>().WithIdentity(job).StoreDurably().Build());
        await w1.Scheduler.TriggerJob(job);

        (await reading.WaitAsync(TimeSpan.FromSeconds(30))).Should().BeTrue("the agent streamed the event up");
        events.Current.SchedulerInstanceId.Should().Be(w1.Scheduler.SchedulerInstanceId);
        events.Current.Kind.Should().BeOneOf(SchedulerEventKind.TriggerFired, SchedulerEventKind.JobExecuting);

        await Eventually.Until(() => !RecordingJob.Ran.IsEmpty);

        PagedResult<DashboardHistoryEntry> w1History = await Eventually.Value(
            () => dashboard.Client.QueryExecutions(new DashboardHistoryQuery { SchedulerName = w1.Key }).AsTask(),
            page => page.Items.Count > 0);
        w1History.Items.Should().ContainSingle().Which.JobName.Should().Be("watched",
            "the history is read from the worker that ran the job, through the tunnel");

        PagedResult<DashboardHistoryEntry> w2History = await dashboard.Client.QueryExecutions(new DashboardHistoryQuery { SchedulerName = w2.Key });
        w2History.Items.Should().BeEmpty("the other worker ran nothing");
        otherReading.IsCompleted.Should().BeFalse("the other worker streamed nothing");

        // Leaving is disposing the enumeration, which is what a page navigating away does.
        await subscription.CancelAsync();
        await events.DisposeAsync();
        await otherEvents.DisposeAsync();

        await Eventually.Until(() => !w1.Broker.HasSubscribers(w1.Scheduler.SchedulerName));
        w1.Plugin.Connection!.IsWatching.Should().BeFalse("the last subscriber leaving is what tells the agent to stop its stream");
    }

    public sealed class RecordingJob : IJob
    {
        public static readonly System.Collections.Concurrent.ConcurrentQueue<string> Ran = new();

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            Ran.Enqueue(context.FireInstanceId);
            return default;
        }
    }
}
