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
}
