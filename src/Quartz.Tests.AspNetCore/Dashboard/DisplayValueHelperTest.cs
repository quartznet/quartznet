using Quartz.Dashboard.Components.Shared;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// How the pages that list fire times say how far away each one is.
/// </summary>
public sealed class DisplayValueHelperTest
{
    private static readonly DateTimeOffset now = new(2026, 10, 5, 14, 23, 0, TimeSpan.Zero);

    [TestCase(-90, "in under a minute")]
    [TestCase(0, "in under a minute")]
    [TestCase(59, "in under a minute")]
    [TestCase(60, "in 1m")]
    [TestCase(59 * 60 + 59, "in 59m")]
    [TestCase(60 * 60, "in 1h")]
    [TestCase(23 * 60 * 60 + 59 * 60, "in 23h")]
    [TestCase(24 * 60 * 60, "in 1d")]
    [TestCase(3 * 24 * 60 * 60 + 5 * 60 * 60, "in 3d")]
    public void AFireTimeIsSaidAsHowLongUntilItInItsLargestWholeUnit(int secondsAhead, string expected)
    {
        DisplayValueHelper.FormatUntil(now.AddSeconds(secondsAhead), now).Should().Be(expected,
            "a fire time already due, or due within a minute, is not worth counting down in seconds");
    }

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
