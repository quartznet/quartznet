using FluentAssertions;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components;

using Pages = Quartz.Dashboard.Components.Pages;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class DashboardRouteTableTest
{
    [TestCase("", typeof(Pages.Dashboard))]
    [TestCase("/", typeof(Pages.Dashboard))]
    [TestCase("jobs", typeof(Pages.Jobs))]
    [TestCase("JOBS", typeof(Pages.Jobs))]
    [TestCase("jobs?page=2", typeof(Pages.Jobs))]
    [TestCase("triggers", typeof(Pages.Triggers))]
    [TestCase("calendars", typeof(Pages.Calendars))]
    [TestCase("executing", typeof(Pages.CurrentlyExecuting))]
    [TestCase("history", typeof(Pages.History))]
    [TestCase("history?page=2&job=x", typeof(Pages.History))]
    [TestCase("live", typeof(Pages.LiveLogs))]
    [TestCase("actions", typeof(Pages.ActionLog))]
    public void ShouldMatchDashboardRelativeRoutes(string dashboardRelativePath, Type expectedPageType)
    {
        RouteData? routeData = DashboardRouteTable.Match(dashboardRelativePath);

        routeData.Should().NotBeNull();
        routeData!.PageType.Should().Be(expectedPageType);
    }

    [Test]
    public void ShouldExtractRouteParameterValues()
    {
        RouteData? routeData = DashboardRouteTable.Match("jobs/DEFAULT/my%20job");

        routeData.Should().NotBeNull();
        routeData!.PageType.Should().Be<Pages.JobDetail>();
        routeData.RouteValues["Group"].Should().Be("DEFAULT");
        routeData.RouteValues["Name"].Should().Be("my job");
    }

    [TestCase("unknown")]
    [TestCase("jobs/too/many/segments")]
    [TestCase("quartz/jobs")]
    public void ShouldNotMatchUnknownRoutes(string dashboardRelativePath)
    {
        DashboardRouteTable.Match(dashboardRelativePath).Should().BeNull();
    }

    [Test]
    public void MatchWithOptionsShouldRetryWithPrefixStrippedWhenDirectMatchFails()
    {
        // static SSR under an application path base that ends with the custom dashboard path
        // produces dashboard-prefixed relative paths that the base URI shape check cannot
        // distinguish from an interactive leaf; the failed direct match retries stripped
        var options = new QuartzDashboardOptions { DashboardPath = "/ops" };

        DashboardRouteTable.Match("ops/jobs", options)!.PageType.Should().Be<Pages.Jobs>();
        DashboardRouteTable.Match("ops", options)!.PageType.Should().Be<Pages.Dashboard>();
        DashboardRouteTable.Match("ops/nope", options).Should().BeNull();
    }

    [Test]
    public void MatchWithOptionsShouldPreferTheDirectMatch()
    {
        // an interactive leaf that begins with a route-colliding dashboard path name must not be stripped
        var options = new QuartzDashboardOptions { DashboardPath = "/jobs" };

        DashboardRouteTable.Match("jobs/DEFAULT/x", options)!.PageType.Should().Be<Pages.JobDetail>();
        DashboardRouteTable.Match("jobs", options)!.PageType.Should().Be<Pages.Jobs>();
    }

    [Test]
    public void MatchWithOptionsShouldNotFallBackWithDefaultPath()
    {
        DashboardRouteTable.Match("quartz/jobs", new QuartzDashboardOptions()).Should().BeNull();
    }

    [Test]
    public void ResolveLeafShouldStripAmbiguousPrefixOnlyWhenItYieldsARoute()
    {
        // used by NavMenu for active-link highlighting; must mirror Match's retry
        var options = new QuartzDashboardOptions { DashboardPath = "/ops" };

        // path base ends with the dashboard path → static SSR relative carries the prefix
        DashboardRouteTable.ResolveLeaf("ops/jobs", options).Should().Be("jobs");
        DashboardRouteTable.ResolveLeaf("ops", options).Should().Be("");

        // a route-colliding leaf resolves directly and is not stripped
        var collide = new QuartzDashboardOptions { DashboardPath = "/jobs" };
        DashboardRouteTable.ResolveLeaf("jobs", collide).Should().Be("jobs");

        // no route either way → returns the direct form (no highlight)
        DashboardRouteTable.ResolveLeaf("ops/nope", options).Should().Be("ops/nope");
    }
}
