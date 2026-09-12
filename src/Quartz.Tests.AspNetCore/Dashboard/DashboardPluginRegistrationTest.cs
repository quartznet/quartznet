using FakeItEasy;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

using Quartz.Configuration;
using Quartz.Dashboard.Plugins;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
using Quartz.HttpApiContract;
using Quartz.Impl;
using Quartz.Tests.AspNetCore.Support;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The dashboard's own plugins are registered as services rather than named by
/// <c>quartz.plugin.&lt;name&gt;.type</c> keys, so resolving them is what proves they still reach a
/// scheduler — and under the names they have always had, since a plugin is told its name when it is
/// initialized and history rows are keyed by it.
/// </summary>
/// <remarks>
/// The scheduler they have to reach is <em>every</em> scheduler in the container. The dashboard renders
/// whatever the container holds, and a named scheduler's parts are keyed by its name, so plugins added
/// only to the unkeyed registration left a scheduler registered with <c>AddQuartz(name, …)</c> showing
/// pages whose live view and history were always empty. Both keys are asserted here, which is what the
/// previous version of this test — hard-coded to the default scheduler's <c>null</c> key — could not see.
/// </remarks>
public class DashboardPluginRegistrationTest
{
    [Test]
    public void DashboardPluginsShouldReachTheDefaultSchedulerUnderTheirOwnNames()
    {
        var services = new ServiceCollection();
        services.AddQuartzDashboard();
        services.AddQuartz();

        using var provider = services.BuildServiceProvider();

        DashboardPlugins(provider, schedulerKey: null).Should().BeEquivalentTo(
            [("quartzSchedulerEvents", typeof(SchedulerEventPlugin)),
             ("quartzExecutionHistory", typeof(ExecutionHistoryPlugin))]);
    }

    [Test]
    public void DashboardPluginsShouldReachANamedSchedulerRegisteredAfterTheDashboard()
    {
        var services = new ServiceCollection();
        services.AddQuartzDashboard();
        services.AddQuartz("acme");

        using var provider = services.BuildServiceProvider();

        DashboardPlugins(provider, "acme").Should().BeEquivalentTo(
            [("quartzSchedulerEvents", typeof(SchedulerEventPlugin)),
             ("quartzExecutionHistory", typeof(ExecutionHistoryPlugin))],
            "a named scheduler resolves its plugins by service key, so plugins registered unkeyed never "
            + "reached it and its live view and history were silently always empty");
    }

    [Test]
    public void DashboardPluginsShouldReachANamedSchedulerRegisteredBeforeTheDashboard()
    {
        var services = new ServiceCollection();
        services.AddQuartz("acme");
        services.AddQuartzDashboard();

        using var provider = services.BuildServiceProvider();

        DashboardPlugins(provider, "acme").Should().BeEquivalentTo(
            [("quartzSchedulerEvents", typeof(SchedulerEventPlugin)),
             ("quartzExecutionHistory", typeof(ExecutionHistoryPlugin))],
            "an application is free to register its schedulers on either side of AddQuartzDashboard");
    }

    [Test]
    public void EverySchedulerShouldGetItsOwnDashboardPluginInstances()
    {
        var services = new ServiceCollection();
        services.AddQuartzDashboard();
        services.AddQuartz();
        services.AddQuartz("acme");
        services.AddQuartz("initech");

        using var provider = services.BuildServiceProvider();

        List<ISchedulerPlugin> publishers =
        [
            .. new object?[] { null, "acme", "initech" }
                .Select(key => Plugins(provider, key).OfType<SchedulerEventPlugin>().Single())
        ];

        publishers.Distinct().Should().HaveCount(3,
            "a plugin is told which scheduler it extends when it is initialized, so one instance shared "
            + "between three schedulers would publish every scheduler's events under the last name");
    }

    /// <summary>
    /// The recorder a named scheduler's container builds records into the
    /// <see cref="IDashboardHistoryStore" /> the application registered.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The 4.0 recipe, unchanged: register a history store of your own before
    /// <c>AddQuartzDashboard()</c> and what the schedulers run lands in it. What changed underneath is
    /// which recorder writes — Quartz's own now, against an adapter over that store — and this is what
    /// says the recipe still works.
    /// </para>
    /// <para>
    /// Two things at once, because they fail in the same place. The recorder takes the container by
    /// constructor, and a constructor the container cannot satisfy shows up nowhere until the first
    /// <c>GetScheduler()</c>, which no registration test reaches. For a named scheduler the parameter is
    /// resolved through the scheduler-scoped provider rather than the container itself, so this is also
    /// what says that wrapper answers a request for <see cref="IServiceProvider" />.
    /// </para>
    /// </remarks>
    [Test]
    public async Task TheRecorderBuiltForANamedSchedulerRecordsToTheApplicationsOwnStore()
    {
        // the execution below is recorded at a constant fire time, so the store needs a clock standing
        // beside it rather than the wall's — it forgets by age as well as by count
        IDashboardHistoryStore store = TestData.Dashboard.HistoryStore(
            new FakeTimeProvider(new DateTimeOffset(2025, 1, 1, 0, 0, 30, TimeSpan.Zero)));

        ServiceCollection services = new();
        // registered before the dashboard, which is the documented order: the dashboard then makes
        // Quartz's history store an adapter over this one rather than replacing it
        services.AddSingleton<IDashboardHistoryStore>(store);
        services.AddQuartzDashboard();
        services.AddQuartz("acme");

        await using ServiceProvider provider = services.BuildServiceProvider();

        ExecutionHistoryPlugin plugin = Plugins(provider, "acme").OfType<ExecutionHistoryPlugin>().Single();
        IScheduler scheduler = FakeScheduler("acme");

        await plugin.Initialize("quartzExecutionHistory", scheduler);
        await plugin.JobWasExecuted(ExecutionContext(scheduler), jobException: null);

        PagedResult<DashboardHistoryEntry> page = await store.QueryExecutions(new DashboardHistoryQuery { SchedulerName = "acme" });
        page.Items.Should().ContainSingle("the recorder resolves its store from the container it was built with")
            .Which.JobName.Should().Be("DummyJob");
    }

    /// <summary>
    /// The dashboard's own history plugin is not registered any more, so nothing records twice.
    /// </summary>
    /// <remarks>
    /// Two recorders writing the same events would double every row, and a history that counts one run
    /// as two is worse than no history. The type stays public and functional for an application that
    /// registered it by name.
    /// </remarks>
    [Test]
    public void TheDashboardsOwnHistoryPluginIsNoLongerRegistered()
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz();

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugins(provider, schedulerKey: null).OfType<DashboardHistoryPlugin>().Should().BeEmpty(
            "Quartz's own recorder writes the rows the dashboard reads, and a second one would write them again");
    }

    /// <summary>
    /// The dashboard's own live-events plugin is not registered any more either, so nothing pushes twice.
    /// </summary>
    /// <remarks>
    /// Quartz's publisher puts every event on a stream the dashboard's pages read directly and an internal
    /// forwarder feeds the hub from, so a second publisher writing into the same hub would show every
    /// browser each event twice. The type stays public and functional for an application that registered it
    /// by name — <c>DashboardSchedulerStateEventsTest</c> is what says it still works.
    /// </remarks>
    [Test]
    public void TheDashboardsOwnLiveEventsPluginIsNoLongerRegistered()
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz();

        using ServiceProvider provider = services.BuildServiceProvider();

        Plugins(provider, schedulerKey: null).OfType<DashboardLiveEventsPlugin>().Should().BeEmpty(
            "Quartz's own publisher puts the events on the stream the pages and the hub both read");
    }

    /// <summary>
    /// The publisher a named scheduler's container builds publishes into that container's broker.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="TheRecorderBuiltForANamedSchedulerRecordsToTheApplicationsOwnStore" path="/remarks" />
    /// </remarks>
    [Test]
    public async Task ThePublisherBuiltForANamedSchedulerPublishesIntoThatContainersBroker()
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard();
        services.AddQuartz("acme");

        await using ServiceProvider provider = services.BuildServiceProvider();

        SchedulerEventPlugin plugin = Plugins(provider, "acme").OfType<SchedulerEventPlugin>().Single();
        SchedulerEventBroker broker = provider.GetRequiredService<SchedulerEventBroker>();
        IScheduler scheduler = FakeScheduler("acme");

        await plugin.Initialize(SchedulerEventPlugin.PluginName, scheduler);

        List<SchedulerEvent> published = [];
        using CancellationTokenSource subscription = new();
        Task reading = Task.Run(async () =>
        {
            await foreach (SchedulerEvent published_ in broker.Subscribe("acme", subscription.Token))
            {
                lock (published)
                {
                    published.Add(published_);
                }
            }
        });

        using CancellationTokenSource waiting = new(TimeSpan.FromSeconds(10));
        while (!broker.HasSubscribers("acme"))
        {
            await Task.Delay(5, waiting.Token);
        }

        await plugin.SchedulerStarted(scheduler);

        while (true)
        {
            lock (published)
            {
                if (published.Count > 0)
                {
                    break;
                }
            }

            await Task.Delay(5, waiting.Token);
        }

        await subscription.CancelAsync();
        await reading;

        lock (published)
        {
            published.Should().ContainSingle("the publisher resolves its broker from the container it was built with")
                .Which.Should().BeEquivalentTo(new
                {
                    Kind = SchedulerEventKind.SchedulerStateChanged,
                    SchedulerName = "acme",
                    SchedulerInstanceId = "acme-node",
                    Status = SchedulerStatus.Running
                });
        }
    }

    private static IScheduler FakeScheduler(string name)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
        A.CallTo(() => scheduler.SchedulerInstanceId).Returns(name + "-node");
        A.CallTo(() => scheduler.ListenerManager).Returns(A.Fake<IListenerManager>());
        return scheduler;
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
        A.CallTo(() => context.FireTimeUtc).Returns(new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        A.CallTo(() => context.JobRunTime).Returns(TimeSpan.FromSeconds(1));
        A.CallTo(() => context.FireInstanceId).Returns("fire-1");
        return context;
    }

    private static List<(string Name, Type Type)> DashboardPlugins(IServiceProvider provider, object? schedulerKey)
    {
        return
        [
            .. SchedulerPluginFactory
                .Create(provider, Plugins(provider, schedulerKey), [], new SchedulerKey(schedulerKey))
                .Select(x => (x.Name, x.Plugin.GetType()))
        ];
    }

    private static IEnumerable<ISchedulerPlugin> Plugins(IServiceProvider provider, object? schedulerKey)
    {
        return schedulerKey is null
            ? provider.GetServices<ISchedulerPlugin>()
            : provider.GetKeyedServices<ISchedulerPlugin>(schedulerKey);
    }
}
