using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Plugins;
using Quartz.Dashboard.Services;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// How long a job took, from the execution context to the two places the dashboard reports it.
/// </summary>
/// <remarks>
/// Both used to count whole milliseconds, so an execution shorter than one was recorded as having
/// taken no time at all. They carry <see cref="IJobExecutionContext.JobRunTime" /> as a
/// <see cref="TimeSpan" /> now, and these tests are what says the sub-millisecond part survives.
/// </remarks>
public class DashboardDurationTest
{
    private static readonly TimeSpan SubMillisecond = TimeSpan.FromTicks(4_567);

    /// <summary>
    /// The instant everything here is recorded at. The store forgets by age as well as by count, so a
    /// test whose fire times are constants — as they must be, to be asserted on — needs a clock standing
    /// beside them rather than the wall's.
    /// </summary>
    private static readonly DateTimeOffset TestTime = new(2025, 1, 1, 0, 0, 30, TimeSpan.Zero);

    private static DashboardHistoryStore StoreAtTestTime() =>
        TestData.Dashboard.HistoryStore(new FakeTimeProvider(TestTime));

    [Test]
    public async Task HistoryPluginRecordsTheRunTimeItWasGiven()
    {
        DashboardHistoryStore store = StoreAtTestTime();
        IScheduler scheduler = FakeScheduler();

        DashboardHistoryPlugin plugin = new(ProviderWith(store), TimeProvider.System);
        await plugin.Initialize("history", scheduler);
        await plugin.JobWasExecuted(ExecutionContext(scheduler, SubMillisecond), jobException: null);

        PagedResult<DashboardHistoryEntry> page = await store.QueryExecutions(new DashboardHistoryQuery { SchedulerName = "TestScheduler" });

        DashboardHistoryEntry entry = page.Items.Should().ContainSingle().Subject;
        entry.Duration.Should().Be(SubMillisecond,
            "a whole-millisecond count recorded this execution as having taken no time at all");
        entry.Succeeded.Should().BeTrue();
        entry.JobName.Should().Be("DummyJob");
        entry.TriggerGroup.Should().Be("DummyTriggerGroup");
    }

    [Test]
    public async Task HistoryPluginRecordsTheFailureAndItsMessage()
    {
        DashboardHistoryStore store = StoreAtTestTime();
        IScheduler scheduler = FakeScheduler();

        DashboardHistoryPlugin plugin = new(ProviderWith(store), TimeProvider.System);
        await plugin.Initialize("history", scheduler);
        await plugin.JobWasExecuted(
            ExecutionContext(scheduler, TimeSpan.FromSeconds(2)),
            new JobExecutionException("the job threw"));

        DashboardHistoryEntry entry = (await store.QueryExecutions(new DashboardHistoryQuery { SchedulerName = "TestScheduler" }))
            .Items.Should().ContainSingle().Subject;

        entry.Succeeded.Should().BeFalse();
        entry.ExceptionMessage.Should().Contain("the job threw");
        entry.Duration.Should().Be(TimeSpan.FromSeconds(2));
    }

    /// <summary>
    /// The live-events plugin builds its payload before it looks for a hub, so a scheduler with no hub
    /// registered still exercises the mapping — which is the part that changed.
    /// </summary>
    [Test]
    public async Task LiveEventsPluginSurvivesHavingNoHubToBroadcastTo()
    {
        IScheduler scheduler = FakeScheduler();

        DashboardLiveEventsPlugin plugin = new(ProviderWith(StoreAtTestTime()));
        await plugin.Initialize("live", scheduler);

        Func<Task> executed = async () =>
            await plugin.JobWasExecuted(ExecutionContext(scheduler, SubMillisecond), jobException: null);
        Func<Task> vetoed = async () =>
            await plugin.JobExecutionVetoed(ExecutionContext(scheduler, SubMillisecond));

        await executed.Should().NotThrowAsync("dashboard events are not worth failing a job execution over");
        await vetoed.Should().NotThrowAsync();
    }

    [Test]
    public void JobExecutionResultCarriesTheRunTimeAsATimeSpan()
    {
        JobExecutionResultDto result = new(
            SchedulerInstanceId: TestData.SchedulerInstanceId,
            JobKey: new JobKeyDto("group", "job"),
            TriggerKey: new TriggerKeyDto("group", "trigger"),
            FireTimeUtc: new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RunTime: SubMillisecond,
            Vetoed: false,
            ExceptionMessage: null);

        result.RunTime.Should().Be(SubMillisecond);
    }

    [Test]
    public async Task HistoryPageIsNewestFirstAndCountsTheWholeMatch()
    {
        DashboardHistoryStore store = StoreAtTestTime();
        for (int i = 0; i < 5; i++)
        {
            await store.AddExecution(EntryAt(new DateTimeOffset(2025, 1, 1, 0, 0, i, TimeSpan.Zero), "job" + i));
        }

        PagedResult<DashboardHistoryEntry> page = await store.QueryExecutions(
            new DashboardHistoryQuery { SchedulerName = "TestScheduler", Take = 2 });

        page.Items.Select(x => x.JobName).Should().Equal(["job4", "job3"], "history reads newest first");
        page.HasMore.Should().BeTrue();
        page.TotalCount.Should().Be(5, "the total counts the whole match, not the page");

        PagedResult<DashboardHistoryEntry> filtered = await store.QueryExecutions(
            new DashboardHistoryQuery { SchedulerName = "TestScheduler", JobFilter = "job2" });

        filtered.Items.Should().ContainSingle().Which.JobName.Should().Be("job2");
    }

    private static DashboardHistoryEntry EntryAt(DateTimeOffset firedAt, string jobName) => new(
        SchedulerName: "TestScheduler",
        SchedulerInstanceId: TestData.SchedulerInstanceId,
        JobGroup: "DummyGroup",
        JobName: jobName,
        TriggerGroup: "DummyTriggerGroup",
        TriggerName: "DummyTrigger",
        FiredAtUtc: firedAt,
        Duration: SubMillisecond,
        Succeeded: true,
        ExceptionMessage: null);

    /// <remarks>
    /// The plugins take the container by constructor, the way the dashboard's own registration builds
    /// them.
    /// </remarks>
    private static ServiceProvider ProviderWith(IDashboardHistoryStore store)
    {
        ServiceCollection services = new();
        services.AddSingleton(store);
        return services.BuildServiceProvider();
    }

    private static IScheduler FakeScheduler()
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns("TestScheduler");
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns(TestData.SchedulerInstanceId);
        A.CallTo(() => scheduler.ListenerManager).Returns(A.Fake<IListenerManager>());
        return scheduler;
    }

    private static IJobExecutionContext ExecutionContext(IScheduler scheduler, TimeSpan runTime)
    {
        IJobExecutionContext context = A.Fake<IJobExecutionContext>();
        A.CallTo(() => context.Scheduler).Returns(scheduler);
        A.CallTo(() => context.JobDetail).Returns(
            JobBuilder.Create<DummyJob>().WithIdentity("DummyJob", "DummyGroup").Build());
        A.CallTo(() => context.Trigger).Returns(
            TriggerBuilder.Create()
                .WithIdentity("DummyTrigger", "DummyTriggerGroup")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
                .Build());
        A.CallTo(() => context.FireTimeUtc).Returns(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        A.CallTo(() => context.JobRunTime).Returns(runTime);
        A.CallTo(() => context.FireInstanceId).Returns("fire-1");
        return context;
    }
}
