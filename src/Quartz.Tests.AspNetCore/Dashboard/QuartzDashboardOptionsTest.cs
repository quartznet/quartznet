
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

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

    [Test]
    public void HistoryIsBoundedByAgeAndByCountOutOfTheBox()
    {
        var options = new QuartzDashboardOptions();

        options.HistoryRetention.Should().Be(TimeSpan.FromHours(24),
            "an application that configures nothing still has to stop showing executions from an "
            + "arbitrary distance in the past");
        options.HistoryMaxEntriesPerScheduler.Should().Be(2000, "the count bound is what it has always been");
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ARetentionWindowThatIsNotPositiveIsRejectedAtStartup(int hours)
    {
        var act = () => Build(options => options.HistoryRetention = TimeSpan.FromHours(hours));

        act.Should().Throw<OptionsValidationException>().WithMessage("*HistoryRetention*",
            "a window of zero forgets every execution the moment it is recorded, which looks exactly "
            + "like a history plugin that was never installed");
    }

    [Test]
    public void ACapOfZeroIsRejectedAtStartup()
    {
        var act = () => Build(options => options.HistoryMaxEntriesPerScheduler = 0);

        act.Should().Throw<OptionsValidationException>().WithMessage("*HistoryMaxEntriesPerScheduler*");
    }

    [Test]
    public void TheDefaultsPassValidation()
    {
        var act = () => Build(_ => { });

        act.Should().NotThrow();
    }

    private static QuartzDashboardOptions Build(Action<QuartzDashboardOptions> configure)
    {
        ServiceCollection services = new();
        services.AddQuartzDashboard(configure);

        using ServiceProvider provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<QuartzDashboardOptions>>().Value;
    }
}
