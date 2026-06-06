using FluentAssertions;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components;

using Pages = Quartz.Dashboard.Components.Pages;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class DashboardRouteTableTest
{
    private static QuartzDashboardOptions DefaultOptions => new();

    private static QuartzDashboardOptions CustomPathOptions => new() { DashboardPath = "/my-api/quartz" };

    [TestCase("quartz", typeof(Pages.Dashboard))]
    [TestCase("quartz/", typeof(Pages.Dashboard))]
    [TestCase("quartz/jobs", typeof(Pages.Jobs))]
    [TestCase("QUARTZ/JOBS", typeof(Pages.Jobs))]
    [TestCase("quartz/triggers", typeof(Pages.Triggers))]
    [TestCase("quartz/calendars", typeof(Pages.Calendars))]
    [TestCase("quartz/executing", typeof(Pages.CurrentlyExecuting))]
    [TestCase("quartz/history", typeof(Pages.History))]
    [TestCase("quartz/history?page=2&job=x", typeof(Pages.History))]
    [TestCase("quartz/live", typeof(Pages.LiveLogs))]
    [TestCase("quartz/actions", typeof(Pages.ActionLog))]
    public void ShouldMatchDefaultPathRoutes(string relativePath, Type expectedPageType)
    {
        RouteData? routeData = DashboardRouteTable.Match(relativePath, DefaultOptions);

        routeData.Should().NotBeNull();
        routeData!.PageType.Should().Be(expectedPageType);
    }

    [Test]
    public void ShouldExtractRouteParameterValues()
    {
        RouteData? routeData = DashboardRouteTable.Match("quartz/jobs/DEFAULT/my%20job", DefaultOptions);

        routeData.Should().NotBeNull();
        routeData!.PageType.Should().Be<Pages.JobDetail>();
        routeData.RouteValues["Group"].Should().Be("DEFAULT");
        routeData.RouteValues["Name"].Should().Be("my job");
    }

    [TestCase("quartz/unknown")]
    [TestCase("quartzx")]
    [TestCase("other/path")]
    [TestCase("")]
    public void ShouldNotMatchUnknownRoutes(string relativePath)
    {
        DashboardRouteTable.Match(relativePath, DefaultOptions).Should().BeNull();
    }

    [Test]
    public void ShouldMatchRoutesUnderCustomDashboardPath()
    {
        RouteData? routeData = DashboardRouteTable.Match("my-api/quartz/triggers/g/n", CustomPathOptions);

        routeData.Should().NotBeNull();
        routeData!.PageType.Should().Be<Pages.TriggerDetail>();
        routeData.RouteValues["Group"].Should().Be("g");
        routeData.RouteValues["Name"].Should().Be("n");
    }

    [Test]
    public void ShouldNotMatchDefaultPathWhenCustomPathConfigured()
    {
        DashboardRouteTable.Match("quartz/jobs", CustomPathOptions).Should().BeNull();
    }

    [TestCase("quartz/jobs?page=2", "quartz/jobs")]
    [TestCase("/quartz/jobs/#fragment", "quartz/jobs")]
    [TestCase("quartz/", "quartz")]
    [TestCase("", "")]
    public void NormalizeRelativePathShouldStripQueryFragmentAndSlashes(string input, string expected)
    {
        DashboardLink.NormalizeRelativePath(input).Should().Be(expected);
    }

    [TestCase("/quartz", "/quartz")]
    [TestCase("/quartz/", "/quartz")]
    [TestCase("/my-api/quartz", "/my-api/quartz")]
    [TestCase("/my-api/quartz/", "/my-api/quartz")]
    [TestCase("my-api/quartz", "/my-api/quartz")]
    [TestCase("/", "/quartz")]
    [TestCase("", "/quartz")]
    [TestCase("   ", "/quartz")]
    public void TrimmedDashboardPathShouldNormalize(string configured, string expected)
    {
        new QuartzDashboardOptions { DashboardPath = configured }.TrimmedDashboardPath.Should().Be(expected);
    }
}
