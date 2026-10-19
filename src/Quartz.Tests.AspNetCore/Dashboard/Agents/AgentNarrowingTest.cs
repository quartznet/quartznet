using System.Collections.Concurrent;

using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// The narrowings an agent decides for itself, in its own process, which is what makes them hold against
/// a dashboard that has been taken over: read-only, and the operation allow-list.
/// </summary>
public sealed class AgentNarrowingTest
{
    private static readonly JobKey Job = new("nightly", "narrowing");

    [Test]
    public async Task AReadOnlyAgentRefusesEveryMutationAndServesEveryRead()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.ReadOnly = true);
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build());

        Func<Task> pause = () => dashboard.Client.PauseJob(worker.Key, new JobKeyDto(Job.Group, Job.Name)).AsTask();
        (await pause.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain(AgentCarrier.ReadOnlyDetail, "the refusal is the agent's own words, which the page shows");

        (await dashboard.Client.GetScheduler(worker.Key)).SchedulerInstanceId.Should().Be(worker.Scheduler.SchedulerInstanceId);
        (await dashboard.Client.GetJobDetail(worker.Key, new JobKeyDto(Job.Group, Job.Name))).Name.Should().Be(Job.Name);
        (await dashboard.Client.QueryJobs(worker.Key, new DashboardJobQuery())).Items.Should().ContainSingle();

        worker.Logs.WithEventId(9304).Should().ContainSingle("the agent logs what it turned away");
        dashboard.Registry.Find("w1")!.ReadOnly.Should().BeTrue("the registration says so, for the listing to show");
    }

    [Test]
    public async Task AnOperationOutsideTheAllowListIsRefusedByNameAndTheRestWork()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.IsOperationAllowed = route => route != "Shutdown");

        Func<Task> shutdown = () => dashboard.Client.Shutdown(worker.Key).AsTask();
        (await shutdown.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain("does not accept Shutdown");
        (await worker.Scheduler.GetStatus()).Should().Be(SchedulerStatus.Running, "the refusal came before anything reached the scheduler");

        await dashboard.Client.Standby(worker.Key);
        (await worker.Scheduler.GetStatus()).Should().Be(SchedulerStatus.Standby, "an operation the list allows is carried out on the worker");
    }

    /// <summary>
    /// A trigger's upcoming fire times are one operation the worker computes, allowed by default as every read
    /// is: the trigger and its calendar are not read back for the dashboard to compute.
    /// </summary>
    [Test]
    public async Task TheUpcomingFireTimesAreOneOperationTheWorkerComputes()
    {
        ConcurrentQueue<string> asked = new();
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.IsOperationAllowed = route =>
        {
            asked.Enqueue(route);
            return true;
        });
        TriggerKey trigger = await ScheduleDaily(worker.Scheduler);
        asked.Clear();

        List<DateTimeOffset> fireTimes = await dashboard.Client.GetUpcomingFireTimes(worker.Key, new TriggerKeyDto(trigger.Group, trigger.Name), 2);

        fireTimes.Should().Equal([Noon, Noon.AddDays(1)]);
        asked.Should().Contain("GetTriggerFireTimes", "the worker answers the route, which IsOperationAllowed sees by name")
            .And.NotContain(["GetTrigger", "GetCalendar"], "the trigger and its calendar are not read back for the dashboard to compute");
    }

    /// <summary>
    /// The refusal is the worker's decision, and holds: the dashboard does not get round it by reading the
    /// trigger and its calendar, which only a route the worker does not serve at all is answered with.
    /// </summary>
    [Test]
    public async Task AWorkerThatRefusesTheFireTimesOperationIsNotWorkedAround()
    {
        ConcurrentQueue<string> asked = new();
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.IsOperationAllowed = route =>
        {
            asked.Enqueue(route);
            return route != "GetTriggerFireTimes";
        });
        TriggerKey trigger = await ScheduleDaily(worker.Scheduler);
        asked.Clear();

        Func<Task> read = () => dashboard.Client.GetUpcomingFireTimes(worker.Key, new TriggerKeyDto(trigger.Group, trigger.Name), 2).AsTask();

        (await read.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain("does not accept GetTriggerFireTimes", "the page shows the worker's own words");
        asked.Should().Contain("GetTriggerFireTimes")
            .And.NotContain(["GetTrigger", "GetCalendar"], "the dashboard does not read what it needs to compute the refused answer itself");
    }

    private static readonly DateTimeOffset Noon = new(2030, 1, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// A trigger firing at noon UTC every day from 2030, years past the worker's clock, so it never fires
    /// while the test runs.
    /// </summary>
    private static async Task<TriggerKey> ScheduleDaily(IScheduler scheduler)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("noon", "narrowing")
            .ForJob(Job)
            .StartAt(Noon)
            .WithCronSchedule("0 0 12 * * ?", cron => cron.InTimeZone(TimeZoneInfo.Utc))
            .Build();
        await scheduler.ScheduleJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).Build(), trigger);
        return trigger.Key;
    }
}
