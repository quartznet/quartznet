using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Plugins;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// What the history plugin records, and which node it says recorded it.
/// </summary>
/// <remarks>
/// A cluster runs one scheduler in several processes. Each keeps its own history of its own work, so
/// until a row names the node it came from a reader cannot tell one machine's executions from another's
/// — and a store an application shares across the cluster cannot tell them apart at all.
/// </remarks>
public class DashboardHistoryPluginTest
{
    private static readonly DateTimeOffset Now = new(2026, 8, 26, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AnExecutionIsRecordedAgainstTheNodeThatRanIt()
    {
        DashboardHistoryStore store = TestData.Dashboard.HistoryStore(new FakeTimeProvider(Now));
        IScheduler scheduler = FakeScheduler();

        DashboardHistoryPlugin plugin = new(ProviderWith(store), new FakeTimeProvider(Now));
        await plugin.Initialize("history", scheduler);
        await plugin.JobWasExecuted(ExecutionContext(scheduler), jobException: null);

        PagedResult<DashboardHistoryEntry> page = await store.GetPage(
            new DashboardHistoryQuery { SchedulerName = "TestScheduler" });

        page.Items.Should().ContainSingle().Which.SchedulerInstanceId.Should().Be("node-a",
            "every node keeps its own history, so a row that does not name one cannot be attributed");
    }

    [Test]
    public async Task AMisfireIsRecorded()
    {
        DashboardHistoryStore store = TestData.Dashboard.HistoryStore(new FakeTimeProvider(Now));
        IScheduler scheduler = FakeScheduler();

        DashboardHistoryPlugin plugin = new(ProviderWith(store), new FakeTimeProvider(Now));
        await plugin.Initialize("history", scheduler);
        await plugin.TriggerMisfired(scheduler, MisfiringTrigger());

        PagedResult<DashboardMisfireEntry> misfires = await store.GetMisfires(
            new DashboardMisfireQuery { SchedulerName = "TestScheduler" });

        DashboardMisfireEntry entry = misfires.Items.Should().ContainSingle().Subject;
        entry.SchedulerInstanceId.Should().Be("node-a");
        entry.TriggerGroup.Should().Be("DummyTriggerGroup");
        entry.TriggerName.Should().Be("DummyTrigger");
        entry.JobKey.Should().Be(new JobKeyDto("DummyGroup", "DummyJob"));
        entry.MisfiredAtUtc.Should().Be(Now,
            "the scheduler's clock says when the misfire was noticed; nothing here reads the wall");
        entry.ScheduledFireTimeUtc.Should().Be(Now.AddMinutes(-10),
            "the scheduler notifies before it applies the misfire instruction, so the trigger still "
            + "names the firing that was missed rather than the one it was moved to");
    }

    [Test]
    public async Task AMisfireIsNotAnExecution()
    {
        DashboardHistoryStore store = TestData.Dashboard.HistoryStore(new FakeTimeProvider(Now));
        IScheduler scheduler = FakeScheduler();

        DashboardHistoryPlugin plugin = new(ProviderWith(store), new FakeTimeProvider(Now));
        await plugin.Initialize("history", scheduler);
        await plugin.TriggerMisfired(scheduler, MisfiringTrigger());

        PagedResult<DashboardHistoryEntry> page = await store.GetPage(
            new DashboardHistoryQuery { SchedulerName = "TestScheduler" });

        page.Items.Should().BeEmpty("nothing ran, so there is no execution to show");
    }

    [Test]
    public async Task AMisfireWithoutADashboardRegisteredIsNotWorthFailingTheSchedulerOver()
    {
        IScheduler scheduler = FakeScheduler();
        DashboardHistoryPlugin plugin = new(new ServiceCollection().BuildServiceProvider(), new FakeTimeProvider(Now));
        await plugin.Initialize("history", scheduler);

        Func<Task> misfired = async () => await plugin.TriggerMisfired(scheduler, MisfiringTrigger());

        await misfired.Should().NotThrowAsync(
            "an application with no history store has nothing to record to, which is not an error");
    }

    [Test]
    public async Task ThePluginListensToTriggersAsWellAsJobs()
    {
        IScheduler scheduler = FakeScheduler();
        IListenerManager listeners = scheduler.ListenerManager;

        DashboardHistoryPlugin plugin = new(new ServiceCollection().BuildServiceProvider(), new FakeTimeProvider(Now));
        await plugin.Initialize("history", scheduler);

        A.CallTo(() => listeners.AddTriggerListener(plugin, A<IReadOnlyCollection<IMatcher<TriggerKey>>>._))
            .MustHaveHappened();
        A.CallTo(() => listeners.AddJobListener(plugin, A<IReadOnlyCollection<IMatcher<JobKey>>>._))
            .MustHaveHappened();
    }

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
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns("node-a");
        A.CallTo(() => scheduler.ListenerManager).Returns(A.Fake<IListenerManager>());
        return scheduler;
    }

    /// <summary>
    /// A trigger as the scheduler hands it to a listener at the moment it misfires: still due at the
    /// firing it missed, because the misfire instruction has not been applied yet.
    /// </summary>
    private static ITrigger MisfiringTrigger()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("DummyTrigger", "DummyTriggerGroup")
            .ForJob("DummyJob", "DummyGroup")
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)))
            .Build();

        ((IMutableTrigger) trigger).NextFireTimeUtc = Now.AddMinutes(-10);
        return trigger;
    }

    private static IJobExecutionContext ExecutionContext(IScheduler scheduler)
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
        A.CallTo(() => context.FireTimeUtc).Returns(Now);
        A.CallTo(() => context.JobRunTime).Returns(TimeSpan.FromMilliseconds(12));
        A.CallTo(() => context.FireInstanceId).Returns("fire-1");
        return context;
    }
}
