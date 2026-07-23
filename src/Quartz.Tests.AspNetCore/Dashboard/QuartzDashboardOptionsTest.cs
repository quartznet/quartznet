
namespace Quartz.Tests.AspNetCore.Dashboard;

public class QuartzDashboardOptionsTest
{
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

    [TestCase("/quartz", "/quartz")]
    [TestCase("/my-api/quartz", "/my-api/quartz")]
    [TestCase("/my path", "/my%20path")]
    [TestCase("/työt", "/ty%C3%B6t")]
    public void EscapedDashboardPathShouldPercentEncode(string configured, string expected)
    {
        new QuartzDashboardOptions { DashboardPath = configured }.EscapedDashboardPath.Should().Be(expected);
    }

    [Test]
    public void DerivedPathValuesShouldFollowDashboardPathChanges()
    {
        // the derived values are cached; the cache must track option mutations during configuration
        var options = new QuartzDashboardOptions();
        options.HasCustomDashboardPath.Should().BeFalse();

        options.DashboardPath = "/ops";
        options.TrimmedDashboardPath.Should().Be("/ops");
        options.EscapedDashboardPath.Should().Be("/ops");
        options.HasCustomDashboardPath.Should().BeTrue();
    }
}
