using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard.Agents;

/// <summary>
/// The rule R5 states: an agent refuses every job type until <c>DashboardAgentOptions.IsJobTypeAllowed</c>
/// says which it accepts, because the connection crosses a trust boundary and a job type is a string the
/// dashboard sends.
/// </summary>
public sealed class AgentJobTypeAllowListTest
{
    private static readonly JobKey ExistingJob = new("existing", "allow-list");

    [Test]
    public async Task WithThePredicateUnsetEveryJobTypeIsRefusedAndTheRefusalNamesTheRemedy()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1");

        Func<Task> schedule = () => dashboard.Client.ScheduleJob(worker.Key, new ScheduleJobRequest(Trigger("one"), Job("one"))).AsTask();
        (await schedule.Should().ThrowAsync<HttpClientException>("the agent answers 403, which the client raises as its refusal"))
            .Which.Message.Should().Contain("DashboardAgentOptions.IsJobTypeAllowed", "the refusal says what to set");

        Func<Task> add = () => dashboard.Client.AddJob(worker.Key, new AddJobRequest(Job("two"), Replace: true, StoreNonDurableWhileAwaitingScheduling: null)).AsTask();
        (await add.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain("DashboardAgentOptions.IsJobTypeAllowed");

        (await worker.Scheduler.GetJobKeys(GroupMatcher<JobKey>.GroupEquals("allow-list"))).Should().BeEmpty(
            "nothing was stored on the worker");
    }

    /// <summary>
    /// Only storing a job by type name is refused: the operations on jobs the scheduler already holds
    /// work with the predicate unset.
    /// </summary>
    [Test]
    public async Task EveryOtherOperationWorksWithThePredicateUnset()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1");
        await worker.Scheduler.AddJob(JobBuilder.Create<DummyJob>().WithIdentity(ExistingJob).StoreDurably().Build());

        // A trigger scheduled on its own names no job type, so it is stored.
        await dashboard.Client.ScheduleJob(worker.Key, new ScheduleJobRequest(Trigger("existing-trigger"), Job: null));
        (await worker.Scheduler.GetTriggersOfJob(ExistingJob)).Should().ContainSingle();

        (await dashboard.Client.PauseJob(worker.Key, new JobKeyDto(ExistingJob.Group, ExistingJob.Name))).Should().BeTrue();
        (await dashboard.Client.ResumeJob(worker.Key, new JobKeyDto(ExistingJob.Group, ExistingJob.Name))).Should().BeTrue();
        await dashboard.Client.TriggerJob(worker.Key, new JobKeyDto(ExistingJob.Group, ExistingJob.Name));
        (await dashboard.Client.DeleteJob(worker.Key, new JobKeyDto(ExistingJob.Group, ExistingJob.Name))).Should().BeTrue();
    }

    [Test]
    public async Task APredicateThatAllowsTheTypeLetsTheJobThrough()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options =>
            options.IsJobTypeAllowed = name => name.StartsWith("Quartz.Tests.AspNetCore.", StringComparison.Ordinal));

        await dashboard.Client.ScheduleJob(worker.Key, new ScheduleJobRequest(Trigger("one"), Job("one")));

        (await worker.Scheduler.Exists(new JobKey("one", "allow-list"))).Should().BeTrue("the type passed the agent's own predicate");
    }

    [Test]
    public async Task APredicateThatRefusesTheTypeAnswersForbiddenNamingIt()
    {
        await using AgentDashboard dashboard = await AgentDashboard.Start();
        AgentWorker worker = await dashboard.StartAgent("w1", options =>
            options.IsJobTypeAllowed = name => name.StartsWith("Acme.Jobs.", StringComparison.Ordinal));

        Func<Task> schedule = () => dashboard.Client.ScheduleJob(worker.Key, new ScheduleJobRequest(Trigger("one"), Job("one"))).AsTask();

        (await schedule.Should().ThrowAsync<HttpClientException>())
            .Which.Message.Should().Contain(typeof(DummyJob).FullName!, "the refusal names the type the request asked for, which is the caller's own input");
        worker.Logs.WithEventId(9304).Should().NotBeEmpty("the agent logs what it turned away");
    }

    private static ITrigger Trigger(string name) => TriggerBuilder.Create()
        .WithIdentity(name, "allow-list")
        .ForJob(name is "existing-trigger" ? ExistingJob : new JobKey(name, "allow-list"))
        .StartAt(DateTimeOffset.UtcNow.AddDays(1))
        .Build();

    private static JobDetailDto Job(string name) => new(
        Name: name,
        Group: "allow-list",
        JobType: typeof(DummyJob).FullName!,
        Description: null,
        Durable: true,
        RequestsRecovery: false,
        ConcurrentExecutionDisallowed: null,
        PersistJobDataAfterExecution: null,
        JobDataMap: new JobDataMap());
}
