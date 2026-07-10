using FluentAssertions;

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Components;

using Pages = Quartz.Dashboard.Components.Pages;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class DashboardRouteTableTest
{
    private static QuartzDashboardOptions DefaultOptions => new();

    private static QuartzDashboardOptions CustomPathOptions => new() { DashboardPath = "/my-api/quartz" };

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

    // default path: the base URI is the application root (or path base); the base-relative
    // path carries the "quartz" prefix
    [TestCase("https://host/quartz", "https://host/", "")]
    [TestCase("https://host/quartz/", "https://host/", "")]
    [TestCase("https://host/quartz/jobs", "https://host/", "jobs")]
    [TestCase("https://host/QUARTZ/JOBS", "https://host/", "JOBS")]
    [TestCase("https://host/quartz/jobs?page=2", "https://host/", "jobs")]
    [TestCase("https://host/quartz/jobs#fragment", "https://host/", "jobs")]
    [TestCase("https://host/app/quartz/jobs", "https://host/app/", "jobs")]
    [TestCase("https://host/other", "https://host/", null)]
    [TestCase("https://host/quartzx", "https://host/", null)]
    [TestCase("https://elsewhere/quartz", "https://host/", null)]
    // default path with UsePathBase("/quartz"): the dashboard lives at /quartz/quartz, a bare
    // /quartz/jobs is a host application URL and must not resolve as a dashboard location
    [TestCase("https://host/quartz/quartz/jobs", "https://host/quartz/", "jobs")]
    [TestCase("https://host/quartz/jobs", "https://host/quartz/", null)]
    public void ToDashboardRelativePathShouldResolveDefaultPathUris(string uri, string baseUri, string? expected)
    {
        DashboardLink.ToDashboardRelativePath(uri, baseUri, DefaultOptions).Should().Be(expected);
    }

    // custom path, static SSR shape: the base URI is the application root (or path base); the
    // base-relative path carries the full dashboard path prefix
    [TestCase("https://host/my-api/quartz", "https://host/", "")]
    [TestCase("https://host/my-api/quartz/", "https://host/", "")]
    [TestCase("https://host/my-api/quartz/jobs?page=2", "https://host/", "jobs")]
    [TestCase("https://host/my-api/quartz/triggers/g/n", "https://host/", "triggers/g/n")]
    [TestCase("https://host/app/my-api/quartz/jobs", "https://host/app/", "jobs")]
    [TestCase("https://host/quartz/jobs", "https://host/", null)]
    [TestCase("https://host/other", "https://host/", null)]
    // custom path, interactive circuit shape: the base URI is the rendered dashboard-rooted
    // <base href>, so the base-relative path is already dashboard-rooted
    [TestCase("https://host/my-api/quartz/jobs", "https://host/my-api/quartz/", "jobs")]
    [TestCase("https://host/my-api/quartz/jobs/DEFAULT/x", "https://host/my-api/quartz/", "jobs/DEFAULT/x")]
    [TestCase("https://host/my-api/quartz/", "https://host/my-api/quartz/", "")]
    [TestCase("https://host/my-api/quartz", "https://host/my-api/quartz/", "")]
    [TestCase("https://host/my-api/quartz?page=2", "https://host/my-api/quartz/", "")]
    [TestCase("https://host/app/my-api/quartz/jobs", "https://host/app/my-api/quartz/", "jobs")]
    [TestCase("https://host/elsewhere", "https://host/my-api/quartz/", null)]
    public void ToDashboardRelativePathShouldResolveCustomPathUris(string uri, string baseUri, string? expected)
    {
        DashboardLink.ToDashboardRelativePath(uri, baseUri, CustomPathOptions).Should().Be(expected);
    }

    [Test]
    public void LinksShouldBeDashboardRelativeWithCustomPath()
    {
        DashboardLink.To(CustomPathOptions, "").Should().Be("");
        DashboardLink.To(CustomPathOptions, "jobs").Should().Be("jobs");
    }

    [Test]
    public void LinksShouldCarryDashboardRootWithDefaultPath()
    {
        DashboardLink.To(DefaultOptions, "").Should().Be("quartz");
        DashboardLink.To(DefaultOptions, "jobs").Should().Be("quartz/jobs");
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

    [TestCase("/quartz", false)]
    [TestCase("/QUARTZ/", false)]
    [TestCase("quartz", false)]
    [TestCase("/", false)]
    [TestCase("", false)]
    [TestCase("/my-api/quartz", true)]
    [TestCase("/scheduler", true)]
    public void HasCustomDashboardPathShouldCompareAgainstDefault(string configured, bool expected)
    {
        new QuartzDashboardOptions { DashboardPath = configured }.HasCustomDashboardPath.Should().Be(expected);
    }
}
