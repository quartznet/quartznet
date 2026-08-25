using FakeItEasy;

using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Dashboard.Hubs;
using Quartz.Dashboard.Plugins;
using Quartz.Dashboard.Services;
using Quartz.Extensibility;
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
            [("quartzDashboardLiveEvents", typeof(DashboardLiveEventsPlugin)),
             ("quartzDashboardHistory", typeof(DashboardHistoryPlugin))]);
    }

    [Test]
    public void DashboardPluginsShouldReachANamedSchedulerRegisteredAfterTheDashboard()
    {
        var services = new ServiceCollection();
        services.AddQuartzDashboard();
        services.AddQuartz("acme");

        using var provider = services.BuildServiceProvider();

        DashboardPlugins(provider, "acme").Should().BeEquivalentTo(
            [("quartzDashboardLiveEvents", typeof(DashboardLiveEventsPlugin)),
             ("quartzDashboardHistory", typeof(DashboardHistoryPlugin))],
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
            [("quartzDashboardLiveEvents", typeof(DashboardLiveEventsPlugin)),
             ("quartzDashboardHistory", typeof(DashboardHistoryPlugin))],
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

        List<ISchedulerPlugin> live =
        [
            .. new object?[] { null, "acme", "initech" }
                .Select(key => Plugins(provider, key).OfType<DashboardLiveEventsPlugin>().Single())
        ];

        live.Distinct().Should().HaveCount(3,
            "a plugin is told which scheduler it extends when it is initialized, so one instance shared "
            + "between three schedulers would broadcast every scheduler's events under the last name");
    }

    /// <summary>
    /// The history plugin a named scheduler's container builds records to that container's store.
    /// </summary>
    /// <remarks>
    /// Two things at once, because they fail in the same place. The plugin takes the container by
    /// constructor now — it used to read it back out of
    /// <c>scheduler.Context["Quartz.ServiceProvider"]</c> — and a constructor the container cannot
    /// satisfy shows up nowhere until the first <c>GetScheduler()</c>, which no registration test
    /// reaches. For a named scheduler the parameter is resolved through the scheduler-scoped provider
    /// rather than the container itself, so this is also what says that wrapper answers a request for
    /// <see cref="IServiceProvider" />.
    /// </remarks>
    [Test]
    public async Task TheHistoryPluginBuiltForANamedSchedulerRecordsToThatContainersStore()
    {
        DashboardHistoryStore store = new();

        ServiceCollection services = new();
        // registered before the dashboard, whose own store registration is a TryAdd, so this is the
        // store the plugin has to find
        services.AddSingleton<IDashboardHistoryStore>(store);
        services.AddQuartzDashboard();
        services.AddQuartz("acme");

        await using ServiceProvider provider = services.BuildServiceProvider();

        DashboardHistoryPlugin plugin = Plugins(provider, "acme").OfType<DashboardHistoryPlugin>().Single();
        IScheduler scheduler = FakeScheduler("acme");

        await plugin.Initialize("quartzDashboardHistory", scheduler);
        await plugin.JobWasExecuted(ExecutionContext(scheduler), jobException: null);

        PagedResult<DashboardHistoryEntry> page = await store.GetPage(new DashboardHistoryQuery { SchedulerName = "acme" });
        page.Items.Should().ContainSingle("the plugin resolves its store from the container it was built with")
            .Which.JobName.Should().Be("DummyJob");
    }

    /// <summary>
    /// The live-events plugin a named scheduler's container builds broadcasts through that container's
    /// hub.
    /// </summary>
    /// <remarks>
    /// <inheritdoc cref="TheHistoryPluginBuiltForANamedSchedulerRecordsToThatContainersStore" path="/remarks" />
    /// </remarks>
    [Test]
    public async Task TheLiveEventsPluginBuiltForANamedSchedulerBroadcastsThroughThatContainersHub()
    {
        List<SchedulerStateDto> pushed = [];

        IQuartzDashboardHubClient client = A.Fake<IQuartzDashboardHubClient>();
        A.CallTo(() => client.SchedulerStateChanged(A<SchedulerStateDto>._))
            .Invokes((SchedulerStateDto state) => pushed.Add(state))
            .Returns(Task.CompletedTask);

        IHubClients<IQuartzDashboardHubClient> clients = A.Fake<IHubClients<IQuartzDashboardHubClient>>();
        A.CallTo(() => clients.Group(A<string>._)).Returns(client);

        ServiceCollection services = new();
        // an exact registration wins over the open generic AddSignalR registers, so the plugin's lazy
        // hub lookup finds this one
        services.AddSingleton<IHubContext<QuartzDashboardHub, IQuartzDashboardHubClient>>(new CapturingHubContext(clients));
        services.AddQuartzDashboard();
        services.AddQuartz("acme");

        await using ServiceProvider provider = services.BuildServiceProvider();

        DashboardLiveEventsPlugin plugin = Plugins(provider, "acme").OfType<DashboardLiveEventsPlugin>().Single();
        IScheduler scheduler = FakeScheduler("acme");

        await plugin.Initialize("quartzDashboardLiveEvents", scheduler);
        await plugin.SchedulerStarted(scheduler);

        pushed.Should().ContainSingle("the plugin resolves its hub from the container it was built with")
            .Which.Should().BeEquivalentTo(new SchedulerStateDto("acme", SchedulerStatus.Running));
    }

    private static IScheduler FakeScheduler(string name)
    {
        IScheduler scheduler = A.Fake<IScheduler>();
        A.CallTo(() => scheduler.SchedulerName).Returns(name);
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
