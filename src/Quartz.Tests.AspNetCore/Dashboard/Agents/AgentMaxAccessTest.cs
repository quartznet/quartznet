using Microsoft.Extensions.DependencyInjection;

using Quartz.Dashboard.Services;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

using Routes = Quartz.HttpApiContract.SchedulerRoutes;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// <see cref="DashboardAgentOptions.MaxAccess" />: the most an agent lets the dashboard do, decided in the
/// worker's process after the allow-list and before read-only, and told to the dashboard, which hides what
/// the agent would refuse.
/// </summary>
public sealed class AgentMaxAccessTest
{
    private static readonly JobKey Job = new("nightly", "max-access");
    private static readonly JobKeyDto JobDto = new(Job.Group, Job.Name);

    [Test]
    public async Task AnOperateAgentKeepsTheWorkerRunningAndRefusesChangesToWhatItHolds()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.MaxAccess = SchedulerAccess.Operate);
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build());

        (await dashboard.Client.PauseJob(worker.Key, JobDto)).Should().BeTrue("pausing is Operate");
        await dashboard.Client.TriggerJob(worker.Key, JobDto);

        Func<Task> delete = () => dashboard.Client.DeleteJob(worker.Key, JobDto).AsTask();
        (await delete.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain(AgentCarrier.MaxAccessDetail("DeleteJob", SchedulerAccess.Administer, SchedulerAccess.Operate));
        (await worker.Scheduler.GetJobDetail(Job)).Should().NotBeNull("the refusal came before anything reached the scheduler");

        Func<Task> triggerWithData = () => dashboard.Client.TriggerJob(worker.Key, JobDto, new JobDataMap { ["command"] = "rm" }).AsTask();
        (await triggerWithData.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain("TriggerJob with job data needs Administer",
                "a stored NativeJob runs the command its job data names, so data is Administer");

        worker.Logs.WithEventId(9304).Should().HaveCount(2, "the agent logs what it turned away");
    }

    [Test]
    public async Task AViewAgentServesReadsAndRefusesEveryChange()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.MaxAccess = SchedulerAccess.View);
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build());

        Func<Task> pause = () => dashboard.Client.PauseJob(worker.Key, JobDto).AsTask();
        (await pause.Should().ThrowAsync<HttpClientException>()).Which.Message.Should().Contain("PauseJob needs Operate");

        (await dashboard.Client.GetJobDetail(worker.Key, JobDto)).Name.Should().Be(Job.Name);
        (await dashboard.Client.QueryJobs(worker.Key, new DashboardJobQuery())).Items.Should().ContainSingle();
    }

    /// <summary>
    /// The order the options are documented in: the allow-list first, then <c>MaxAccess</c>, then read-only.
    /// </summary>
    [Test]
    public async Task TheAllowListIsAskedFirstAndReadOnlyLast()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options =>
        {
            options.IsOperationAllowed = route => route != "Shutdown";
            options.MaxAccess = SchedulerAccess.Operate;
            options.ReadOnly = true;
        });
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build());

        Func<Task> shutdown = () => dashboard.Client.Shutdown(worker.Key).AsTask();
        Func<Task> delete = () => dashboard.Client.DeleteJob(worker.Key, JobDto).AsTask();
        Func<Task> pause = () => dashboard.Client.PauseJob(worker.Key, JobDto).AsTask();

        (await shutdown.Should().ThrowAsync<HttpClientException>()).Which.Message.Should().Contain("does not accept Shutdown");
        (await delete.Should().ThrowAsync<HttpClientException>()).Which.Message.Should().Contain("DeleteJob needs Administer");
        (await pause.Should().ThrowAsync<HttpClientException>()).Which.Message.Should().Contain(AgentCarrier.ReadOnlyDetail);
    }

    /// <summary>
    /// The registration says what the agent allows, and the dashboard's controls are the lower of a
    /// visitor's role and that: what the agent would refuse is not offered.
    /// </summary>
    [Test]
    public async Task TheDashboardHidesWhatTheAgentWouldRefuse()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options => options.MaxAccess = SchedulerAccess.Operate);

        dashboard.Registry.Find("w1")!.MaxAccess.Should().Be(SchedulerAccess.Operate);

        using IServiceScope scope = dashboard.App.Services.CreateScope();
        SchedulerAuthorization authorization = scope.ServiceProvider.GetRequiredService<SchedulerAuthorization>();

        authorization.Allows(worker.Key, Routes.PauseJob).Should().BeTrue();
        authorization.Allows(worker.Key, Routes.TriggerJob).Should().BeTrue();
        authorization.Allows(worker.Key, Routes.TriggerJob, carriesJobData: true).Should().BeFalse();
        authorization.Allows(worker.Key, Routes.DeleteJob).Should().BeFalse();
        authorization.Allows(worker.Key, Routes.Shutdown).Should().BeFalse();
    }

    [Test]
    public void ALevelThatIsNoneOfTheThreeIsRefused()
    {
        DashboardAgentOptions options = new()
        {
            Endpoint = new Uri("https://ops.example.com/quartz/agents"),
            Token = AgentDashboard.Token,
            MaxAccess = (SchedulerAccess) 7,
        };

        Microsoft.Extensions.Options.ValidateOptionsResult result = new DashboardAgentOptionsValidator().Validate(name: null, options);

        result.Failed.Should().BeTrue();
        result.FailureMessage.Should().Be("MaxAccess must be View, Operate or Administer, was 7.");
    }

    /// <summary>
    /// The default is Administer, which is every agent before 4.8: nothing is refused for a level.
    /// </summary>
    [Test]
    public async Task ByDefaultTheAgentAllowsEverything()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1");
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(Job).StoreDurably().Build());

        dashboard.Registry.Find("w1")!.MaxAccess.Should().Be(SchedulerAccess.Administer);
        await dashboard.Client.TriggerJob(worker.Key, JobDto, new JobDataMap { ["key"] = "value" });
        (await dashboard.Client.DeleteJob(worker.Key, JobDto)).Should().BeTrue();
        worker.Logs.WithEventId(9304).Should().BeEmpty();
    }
}
