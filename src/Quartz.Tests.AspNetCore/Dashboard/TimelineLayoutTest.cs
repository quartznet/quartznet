using Quartz.Dashboard.Models;
using Quartz.Dashboard.Services.Timeline;

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
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);

        bounds.End.Should().Be(Noon);
        bounds.Start.Should().Be(Noon.AddHours(-1));
        bounds.AxisEnd.Should().Be(Noon.AddMinutes(15),
            "a quarter of the hour, so upcoming fires have room past now");
        bounds.NowFraction.Should().BeApproximately(0.8, 0.0001);
    }

    [Test]
    public void PanningDropsTheLookAhead()
    {
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.FromMinutes(30), Noon);

        bounds.End.Should().Be(Noon.AddMinutes(-30));
        bounds.AxisEnd.Should().Be(bounds.End);
        bounds.NowFraction.Should().Be(1);
    }

    [Test]
    public void AShortRunStaysVisible()
    {
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.FromMinutes(30), Noon);
        TimelineBar bar = TimelineService.CreateBar(
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
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);
        List<TimelineBar> bars =
        [
            new(0.10, 0.20, "a", "first", "1"),
            new(0.15, 0.10, "b", "second", "2")
        ];

        TimelineService.ApplyBarGaps(bars);

        (bars[0].Left + bars[0].Width).Should().BeLessThanOrEqualTo(bars[1].Left);
    }

    [Test]
    public void OnlyOfferedPageSizesAreKept()
    {
        TimelineService.NormalizePageSize(50).Should().Be(50);
        TimelineService.NormalizePageSize(7).Should().Be(TimelineService.DefaultPageSize);
        TimelineService.NormalizePanOffset(900).Should().Be(TimeSpan.FromMinutes(15));
        TimelineService.NormalizePanOffset(0).Should().Be(TimeSpan.Zero);
    }

    [Test]
    public void TheMinuteGridNamesBothOffsets()
    {
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.Last15Minutes, TimeSpan.Zero, Noon);

        bounds.MinuteGridStyle.Should().Contain("--qz-timeline-minute-pct:");
        bounds.MinuteGridStyle.Should().Contain("--qz-timeline-minute-offset:");
    }

    /// <summary>
    /// An older remote agent or an older HTTP API may return a <see cref="TriggerHeaderDto" /> whose
    /// <c>StartTimeUtc</c>, <c>EndTimeUtc</c>, <c>NextFireTimeUtc</c> and <c>PreviousFireTimeUtc</c> are
    /// all <see langword="null" />. The page must build trigger rows and rematched bars without throwing.
    /// </summary>
    [Test]
    public void NullFireTimesFromOlderAgentAreHandledGracefully()
    {
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);

        Dictionary<TimelineEntityKey, TimelineRowBuilder> builders = new();
        TimelineRowBuilder builder = TimelineService.GetOrAddRowBuilder(builders, "demo", "failing-job");
        TimelineBar bar = TimelineService.CreateBar(bounds, Noon.AddMinutes(-10), Noon.AddMinutes(-8), "qz-timeline-bar-failed", "failed", "entry-1");
        TimelineService.AddBarForTrigger(builder, "demo", "failing-trigger", bar);

        TimelineTriggerRow nullTimesRow = new(
            Group: "demo",
            Name: "failing-trigger",
            State: TriggerState.Normal,
            ScheduleSummary: "every 30s",
            NextFireTimeUtc: null,
            PreviousFireTimeUtc: null,
            StartTimeUtc: null,
            EndTimeUtc: null,
            IsExecuting: false,
            Bars: [],
            Marks: []);

        TimelineEntityKey triggerKey = TimelineService.CreateTriggerLookupKey("demo", "failing-trigger");
        TimelineRow row = new(builder.Key, "demo", "failing-job", builder.BarsByTrigger, []);
        List<TimelineBar> rematched = TimelineService.TakePendingBars(row.BarsByTrigger, triggerKey);
        TimelineTriggerRow updated = nullTimesRow with { Bars = rematched };

        updated.Bars.Should().ContainSingle("the bar from history is rematched onto the trigger row");
        updated.NextFireTimeUtc.Should().BeNull("null fire times survive the rematch unchanged");
        updated.PreviousFireTimeUtc.Should().BeNull();
        updated.StartTimeUtc.Should().BeNull();
        updated.EndTimeUtc.Should().BeNull();
        // Rendering of null fire times as em dashes is covered by
        // TimelinePageTest.ANullFireTimeRendersAsAnEmDash.
    }

    [Test]
    public void TriggerHydrationSkipsCollapsedGroups()
    {
        List<TimelineRow> rows =
        [
            new(new TimelineEntityKey("alpha", "job"), "alpha", "job", new Dictionary<TimelineEntityKey, PendingTriggerBars>(), []),
            new(new TimelineEntityKey("beta", "job"), "beta", "job", new Dictionary<TimelineEntityKey, PendingTriggerBars>(), []),
        ];
        HashSet<string> collapsed = new(StringComparer.Ordinal) { "beta" };

        TimelineState.SelectRowsForTriggerHydration(rows, collapsed)
            .Should().ContainSingle().Which.JobGroup.Should().Be("alpha");
    }

    [Test]
    public void TriggerHeaderRefreshSelectsOnlyExpandedRowsThatLeftExecuting()
    {
        TimelineEntityKey leaving = TimelineService.CreateTriggerLookupKey("demo", "leaving");
        TimelineEntityKey staying = TimelineService.CreateTriggerLookupKey("demo", "staying");

        TimelineRow expandedLeaving = new(
            new TimelineEntityKey("alpha", "job"),
            "alpha",
            "job",
            [],
            [
                new TimelineTriggerRow(
                    "demo",
                    "leaving",
                    TriggerState.Executing,
                    "every 30s",
                    Noon,
                    null,
                    null,
                    null,
                    IsExecuting: true,
                    Bars: [],
                    Marks: [])
            ])
        {
            TriggersLoaded = true
        };
        TimelineRow collapsedLeaving = new(
            new TimelineEntityKey("beta", "job"),
            "beta",
            "job",
            [],
            [
                new TimelineTriggerRow(
                    "demo",
                    "leaving",
                    TriggerState.Executing,
                    "every 30s",
                    Noon,
                    null,
                    null,
                    null,
                    IsExecuting: true,
                    Bars: [],
                    Marks: [])
            ])
        {
            TriggersLoaded = true
        };
        TimelineRow expandedStaying = new(
            new TimelineEntityKey("gamma", "job"),
            "gamma",
            "job",
            [],
            [
                new TimelineTriggerRow(
                    "demo",
                    "staying",
                    TriggerState.Executing,
                    "every 30s",
                    Noon,
                    null,
                    null,
                    null,
                    IsExecuting: true,
                    Bars: [],
                    Marks: [])
            ])
        {
            TriggersLoaded = true
        };

        HashSet<string> collapsed = new(StringComparer.Ordinal) { "beta" };
        HashSet<TimelineEntityKey> previous = [leaving, staying];
        HashSet<TimelineEntityKey> current = [staying];

        TimelineState.SelectRowsNeedingTriggerHeaderRefresh(
                [expandedLeaving, collapsedLeaving, expandedStaying],
                collapsed,
                previous,
                current,
                lastDueHeaderRefreshNextFireTimes: new Dictionary<TimelineEntityKey, DateTimeOffset>(),
                now: Noon.AddMinutes(-1))
            .Should().ContainSingle()
            .Which.JobGroup.Should().Be("alpha");
    }

    [Test]
    public void TriggerHeaderRefreshSelectsExpandedRowsWhoseNextFireHasPassed()
    {
        // A fire that started and ended between ticks never enters the executing set; due NextFireTime
        // must still force a header re-read. Paused / future next / still-executing must not.
        TimelineEntityKey liveKey = TimelineService.CreateTriggerLookupKey("demo", "live");

        TimelineRow due = CreateHydratedRow("alpha", "due-job", "demo", "due", TriggerState.Normal, Noon.AddSeconds(-1));
        TimelineRow paused = CreateHydratedRow("beta", "paused-job", "demo", "paused", TriggerState.Paused, Noon.AddSeconds(-1));
        TimelineRow future = CreateHydratedRow("gamma", "future-job", "demo", "future", TriggerState.Normal, Noon.AddMinutes(5));
        TimelineRow live = CreateHydratedRow("delta", "live-job", "demo", "live", TriggerState.Normal, Noon.AddSeconds(-1));

        TimelineState.SelectRowsNeedingTriggerHeaderRefresh(
                [due, paused, future, live],
                collapsedGroups: new HashSet<string>(StringComparer.Ordinal),
                previousExecutingTriggerKeys: new HashSet<TimelineEntityKey>(),
                currentExecutingTriggerKeys: new HashSet<TimelineEntityKey> { liveKey },
                lastDueHeaderRefreshNextFireTimes: new Dictionary<TimelineEntityKey, DateTimeOffset>(),
                now: Noon)
            .Should().ContainSingle(
                "a due NextFireTimeUtc must select the row even when the fire was never listed as executing")
            .Which.JobGroup.Should().Be("alpha");
    }

    [Test]
    public void TriggerHeaderRefreshSkipsADueNextFireAlreadyRefreshed()
    {
        DateTimeOffset dueNext = Noon.AddSeconds(-1);
        TimelineEntityKey dueKey = TimelineService.CreateTriggerLookupKey("demo", "due");
        TimelineRow due = CreateHydratedRow("alpha", "due-job", "demo", "due", TriggerState.Normal, dueNext);

        TimelineState.SelectRowsNeedingTriggerHeaderRefresh(
                [due],
                collapsedGroups: new HashSet<string>(StringComparer.Ordinal),
                previousExecutingTriggerKeys: new HashSet<TimelineEntityKey>(),
                currentExecutingTriggerKeys: new HashSet<TimelineEntityKey>(),
                lastDueHeaderRefreshNextFireTimes: new Dictionary<TimelineEntityKey, DateTimeOffset>
                {
                    [dueKey] = dueNext
                },
                now: Noon)
            .Should().BeEmpty(
                "a due NextFireTimeUtc already re-read must not select the row again until it changes");
    }

    private static TimelineRow CreateHydratedRow(
        string jobGroup,
        string jobName,
        string triggerGroup,
        string triggerName,
        TriggerState state,
        DateTimeOffset? nextFire)
    {
        return new TimelineRow(
            new TimelineEntityKey(jobGroup, jobName),
            jobGroup,
            jobName,
            [],
            [
                new TimelineTriggerRow(
                    triggerGroup,
                    triggerName,
                    state,
                    "every 30s",
                    nextFire,
                    null,
                    null,
                    null,
                    IsExecuting: false,
                    Bars: [],
                    Marks: [])
            ])
        {
            TriggersLoaded = true
        };
    }

    [Test]
    public void ApplyPreviousTriggersKeepsMetadataWhenRowInstancesAreShared()
    {
        DateTimeOffset start = Noon.AddHours(-1);
        DateTimeOffset next = Noon.AddMinutes(5);
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);
        TimelineBar bar = TimelineService.CreateBar(
            bounds,
            Noon.AddMinutes(-10),
            Noon.AddMinutes(-8),
            "qz-timeline-bar-succeeded",
            "ok",
            "entry-1");

        TimelineTriggerRow trigger = new(
            Group: "demo",
            Name: "demo-trigger",
            State: TriggerState.Normal,
            ScheduleSummary: "every 30s",
            NextFireTimeUtc: next,
            PreviousFireTimeUtc: Noon.AddMinutes(-10),
            StartTimeUtc: start,
            EndTimeUtc: null,
            IsExecuting: false,
            Bars: [],
            Marks: []);

        Dictionary<TimelineEntityKey, PendingTriggerBars> barsByTrigger = new()
        {
            [TimelineService.CreateTriggerLookupKey("demo", "demo-trigger")] =
                new PendingTriggerBars("demo", "demo-trigger", [bar])
        };

        TimelineRow row = new(new TimelineEntityKey("demo", "demo-job"), "demo", "demo-job", barsByTrigger, [trigger])
        {
            TriggersLoaded = true
        };

        TimelineService.ApplyPreviousTriggersOntoRows([row], [row], [], Noon);

        TimelineTriggerRow kept = row.Triggers.Should().ContainSingle().Subject;
        kept.StartTimeUtc.Should().Be(start);
        kept.NextFireTimeUtc.Should().Be(next);
        kept.ScheduleSummary.Should().Be("every 30s");
        kept.Bars.Should().ContainSingle();
    }

    [Test]
    public void ApplyPreviousTriggersClearsStaleExecutingStateWithoutALiveFire()
    {
        TimelineTriggerRow trigger = new(
            Group: "demo",
            Name: "demo-trigger",
            State: TriggerState.Executing,
            ScheduleSummary: "every 3s",
            NextFireTimeUtc: Noon.AddSeconds(3),
            PreviousFireTimeUtc: Noon.AddSeconds(-3),
            StartTimeUtc: Noon.AddHours(-1),
            EndTimeUtc: null,
            IsExecuting: true,
            Bars: [],
            Marks: []);

        TimelineRow row = new(
            new TimelineEntityKey("demo", "demo-job"),
            "demo",
            "demo-job",
            new Dictionary<TimelineEntityKey, PendingTriggerBars>(),
            [trigger])
        {
            TriggersLoaded = true
        };

        TimelineService.ApplyPreviousTriggersOntoRows([row], [row], [], Noon);

        TimelineTriggerRow kept = row.Triggers.Should().ContainSingle().Subject;
        kept.IsExecuting.Should().BeFalse();
        kept.State.Should().Be(TriggerState.Normal);
    }

    [Test]
    public void ApplyPreviousTriggersMarksLiveFiresAsExecutingEvenWhenHeaderWasNormal()
    {
        TimelineTriggerRow trigger = new(
            Group: "demo",
            Name: "demo-trigger",
            State: TriggerState.Normal,
            ScheduleSummary: "every 3s",
            NextFireTimeUtc: Noon.AddSeconds(3),
            PreviousFireTimeUtc: Noon.AddSeconds(-3),
            StartTimeUtc: Noon.AddHours(-1),
            EndTimeUtc: null,
            IsExecuting: false,
            Bars: [],
            Marks: []);

        TimelineRow row = new(
            new TimelineEntityKey("demo", "demo-job"),
            "demo",
            "demo-job",
            new Dictionary<TimelineEntityKey, PendingTriggerBars>(),
            [trigger])
        {
            TriggersLoaded = true
        };

        HashSet<TimelineEntityKey> live = [TimelineService.CreateTriggerLookupKey("demo", "demo-trigger")];

        TimelineService.ApplyPreviousTriggersOntoRows([row], [row], live, Noon);

        TimelineTriggerRow kept = row.Triggers.Should().ContainSingle().Subject;
        kept.IsExecuting.Should().BeTrue();
        kept.State.Should().Be(TriggerState.Executing);
    }

    [Test]
    public void ApplyPreviousTriggersDropsUpcomingMarksThatHaveAlreadyPassed()
    {
        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);
        TimelineBar bar = TimelineService.CreateBar(
            bounds,
            Noon.AddSeconds(-3),
            Noon.AddSeconds(-2),
            "qz-timeline-bar-succeeded",
            "ok",
            "entry-1");

        TimelineTriggerRow trigger = new(
            Group: "demo",
            Name: "demo-trigger",
            State: TriggerState.Normal,
            ScheduleSummary: "every 3s",
            NextFireTimeUtc: Noon.AddSeconds(3),
            PreviousFireTimeUtc: Noon.AddSeconds(-3),
            StartTimeUtc: Noon.AddHours(-1),
            EndTimeUtc: null,
            IsExecuting: false,
            Bars: [],
            Marks:
            [
                new TimelineMark(Noon.AddSeconds(-3), "Upcoming · past"),
                new TimelineMark(Noon.AddSeconds(3), "Upcoming · future"),
            ]);

        Dictionary<TimelineEntityKey, PendingTriggerBars> barsByTrigger = new()
        {
            [TimelineService.CreateTriggerLookupKey("demo", "demo-trigger")] =
                new PendingTriggerBars("demo", "demo-trigger", [bar])
        };

        TimelineRow row = new(new TimelineEntityKey("demo", "demo-job"), "demo", "demo-job", barsByTrigger, [trigger])
        {
            TriggersLoaded = true
        };

        TimelineService.ApplyPreviousTriggersOntoRows([row], [row], [], Noon);

        TimelineTriggerRow kept = row.Triggers.Should().ContainSingle().Subject;
        kept.Marks.Should().ContainSingle().Which.Instant.Should().Be(Noon.AddSeconds(3));
        kept.Bars.Should().ContainSingle();
    }

    [Test]
    public void PanOffsetIsClampedAndAcceptsSeconds()
    {
        TimelineService.NormalizePanOffset(150).Should().Be(TimeSpan.FromSeconds(150));
        TimelineService.NormalizePanOffset(0).Should().Be(TimeSpan.Zero);
        TimelineService.ClampPanOffset(TimeSpan.FromDays(30))
            .Should().Be(TimelineWindow.LastWeek.Span);
    }
}
