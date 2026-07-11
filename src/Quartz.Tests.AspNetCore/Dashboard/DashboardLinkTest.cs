using FluentAssertions;

using Quartz.Dashboard.Components;

namespace Quartz.Tests.AspNetCore.Dashboard;

public class DashboardLinkTest
{
    private static QuartzDashboardOptions DefaultOptions => new();

    private static QuartzDashboardOptions CustomPathOptions => new() { DashboardPath = "/my-api/quartz" };

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
    // the application root itself (with or without the trailing slash) is not a dashboard location
    [TestCase("https://host", "https://host/", null)]
    [TestCase("https://host/app", "https://host/app/", null)]
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

    // a custom DashboardPath whose name collides with a dashboard page route must not be
    // prefix-stripped on the interactive circuit, where the base URI is already dashboard-rooted
    [TestCase("https://host/jobs/jobs", "https://host/jobs/", "jobs")]
    [TestCase("https://host/jobs/jobs/DEFAULT/x", "https://host/jobs/", "jobs/DEFAULT/x")]
    [TestCase("https://host/jobs", "https://host/jobs/", "")]
    [TestCase("https://host/jobs/jobs", "https://host/", "jobs")]
    [TestCase("https://host/jobs", "https://host/", "")]
    public void ToDashboardRelativePathShouldResolveRouteNameCollidingCustomPath(string uri, string baseUri, string? expected)
    {
        var options = new QuartzDashboardOptions { DashboardPath = "/jobs" };
        DashboardLink.ToDashboardRelativePath(uri, baseUri, options).Should().Be(expected);
    }

    // browsers emit percent-encoded URIs; the configured path must be compared in encoded form
    [TestCase("https://host/my%20path/jobs", "https://host/", "jobs")]
    [TestCase("https://host/my%20path/jobs", "https://host/my%20path/", "jobs")]
    [TestCase("https://host/my%20path", "https://host/my%20path/", "")]
    public void ToDashboardRelativePathShouldCompareEncodedForms(string uri, string baseUri, string? expected)
    {
        var options = new QuartzDashboardOptions { DashboardPath = "/my path" };
        DashboardLink.ToDashboardRelativePath(uri, baseUri, options).Should().Be(expected);
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
}
