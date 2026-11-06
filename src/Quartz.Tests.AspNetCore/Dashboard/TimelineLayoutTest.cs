using Quartz.Dashboard.Components.Pages;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// The Timeline's geometry: which window a query names, where a run is drawn, and how far a live axis
/// looks past now.
/// </summary>
public class TimelineLayoutTest
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public void AnUnknownWindowIsTheDefaultQuarterHour()
    {
        TimelineWindow.Parse("nope").Should().BeSameAs(TimelineWindow.Last15Minutes);
        TimelineWindow.Parse("1H").Should().BeSameAs(TimelineWindow.LastHour,
            "the window key in a shared link is matched regardless of case");
    }

    [Test]
    public void ALiveHourLooksFifteenMinutesPastNow()
    {
        TimelineBounds bounds = Timeline.BoundsFor(TimelineWindow.LastHour, TimeSpan.Zero, Noon);

        bounds.End.Should().Be(Noon);
        bounds.Start.Should().Be(Noon.AddHours(-1));
        bounds.AxisEnd.Should().Be(Noon.AddMinutes(15),
            "a quarter of the hour, so upcoming fires have room past now");
        bounds.NowFraction.Should().BeApproximately(0.8, 0.0001);
    }

    [Test]
    public void PanningDropsTheLookAhead()
    {
        TimelineBounds bounds = Timeline.BoundsFor(TimelineWindow.LastHour, TimeSpan.FromMinutes(30), Noon);

        bounds.End.Should().Be(Noon.AddMinutes(-30));
        bounds.AxisEnd.Should().Be(bounds.End);
        bounds.NowFraction.Should().Be(1);
    }

    [Test]
    public void AShortRunStaysVisible()
    {
        TimelineBounds bounds = Timeline.BoundsFor(TimelineWindow.LastHour, TimeSpan.FromMinutes(30), Noon);
        TimelineBar bar = Timeline.CreateBar(
            bounds,
            bounds.Start,
            bounds.Start.AddMilliseconds(1),
            "qz-timeline-bar-succeeded",
            "title",
            "entry-1");

        bar.Width.Should().BeGreaterThanOrEqualTo(0.0025, "a run shorter than a pixel still has a mark");
        (bar.Left + bar.Width).Should().BeLessThanOrEqualTo(1);
    }

    [Test]
    public void NeighbouringBarsKeepAGap()
    {
        TimelineBounds bounds = Timeline.BoundsFor(TimelineWindow.LastHour, TimeSpan.Zero, Noon);
        List<TimelineBar> bars =
        [
            new(0.10, 0.20, "a", "first", "1"),
            new(0.15, 0.10, "b", "second", "2")
        ];

        Timeline.ApplyBarGaps(bars);

        (bars[0].Left + bars[0].Width).Should().BeLessThanOrEqualTo(bars[1].Left);
    }

    [Test]
    public void OnlyOfferedPageSizesAreKept()
    {
        Timeline.NormalizePageSize(50).Should().Be(50);
        Timeline.NormalizePageSize(7).Should().Be(Timeline.DefaultPageSize);
        Timeline.NormalizePan(15).Should().Be(TimeSpan.FromMinutes(15));
        Timeline.NormalizePan(0).Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void TheMinuteGridNamesBothOffsets()
    {
        TimelineBounds bounds = Timeline.BoundsFor(TimelineWindow.Last15Minutes, TimeSpan.Zero, Noon);

        bounds.MinuteGridStyle.Should().Contain("--qz-timeline-minute-pct:");
        bounds.MinuteGridStyle.Should().Contain("--qz-timeline-minute-offset:");
    }
}
