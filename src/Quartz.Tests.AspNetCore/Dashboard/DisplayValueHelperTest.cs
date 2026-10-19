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
}
