using Quartz.Dashboard.Components.Shared;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// How the pages write the values they show.
/// </summary>
public sealed class DisplayValueHelperTest
{
    [TestCase(0, "00:00:00")]
    [TestCase(1 * 60 * 60 + 2 * 60 + 3, "01:02:03")]
    [TestCase(23 * 60 * 60 + 59 * 60 + 59, "23:59:59")]
    [TestCase(24 * 60 * 60, "1.00:00:00")]
    [TestCase(26 * 60 * 60, "1.02:00:00")]
    [TestCase(3 * 24 * 60 * 60 + 4 * 60 * 60 + 5 * 60 + 6, "3.04:05:06")]
    public void AClockShowsItsDaysFromADayUp(int seconds, string expected)
    {
        DisplayValueHelper.FormatClock(TimeSpan.FromSeconds(seconds)).Should().Be(expected,
            "hh:mm:ss alone writes the hours component, so 26 hours would read as 2");
    }
}
