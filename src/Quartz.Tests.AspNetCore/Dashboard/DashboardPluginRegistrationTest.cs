using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Dashboard.Plugins;
using Quartz.Extensibility;

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
