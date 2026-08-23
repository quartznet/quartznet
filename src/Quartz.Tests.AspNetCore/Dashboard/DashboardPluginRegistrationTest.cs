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
public class DashboardPluginRegistrationTest
{
    [Test]
    public void DashboardPluginsShouldReachTheSchedulerUnderTheirOwnNames()
    {
        var services = new ServiceCollection();
        services.AddQuartzDashboard();
        services.AddQuartz();

        using var provider = services.BuildServiceProvider();

        var plugins = SchedulerPluginFactory.Create(
            provider, provider.GetServices<ISchedulerPlugin>(), [], new SchedulerKey(null));

        plugins.Should().ContainSingle(x => x.Name == "quartzDashboardLiveEvents" && x.Plugin is DashboardLiveEventsPlugin);
        plugins.Should().ContainSingle(x => x.Name == "quartzDashboardHistory" && x.Plugin is DashboardHistoryPlugin);
    }
}
