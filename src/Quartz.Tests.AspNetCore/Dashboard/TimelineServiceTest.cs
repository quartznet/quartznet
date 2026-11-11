using FakeItEasy;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Models;
using Quartz.Dashboard.Services;
using Quartz.Dashboard.Services.Timeline;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// TimelineService load paths: history and fires are assembled against a frozen clock.
/// </summary>
public class TimelineServiceTest
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task AssembleJobBarsUsesTheProvidedClockForBoundsAndExecutingBars()
    {
        FakeTimeProvider clock = new(Noon);
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineService service = new(api);

        DashboardHistoryEntry history = new(
            "Default",
            "instance-1",
            JobGroup: "demo",
            JobName: "demo-job",
            TriggerGroup: "demo",
            TriggerName: "demo-trigger",
            FiredAtUtc: Noon.AddMinutes(-10),
            Duration: TimeSpan.FromMinutes(2),
            Succeeded: true,
            ExceptionMessage: null)
        {
            EntryId = "entry-1",
            Result = JobRunResult.Succeeded
        };

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardHistoryEntry>([history], HasMore: false, TotalCount: 1));
        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));

        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, clock.GetUtcNow());
        TimelineSnapshot snapshot = await service.AssembleJobBarsAsync(
            "Default",
            bounds,
            clock.GetUtcNow(),
            static instant => instant.ToString("HH:mm")).ConfigureAwait(false);

        snapshot.HistoryUnavailable.Should().BeFalse();
        snapshot.Rows.Should().ContainSingle();
        TimelineRow row = snapshot.Rows[0];
        row.JobName.Should().Be("demo-job");
        row.BarsByTrigger.Should().ContainKey(TimelineService.CreateTriggerLookupKey("demo", "demo-trigger"));
    }

    [Test]
    public async Task AssembleJobBarsReportsHistoryUnavailableWhenQueryExecutionsIsUnsupported()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineService service = new(api);

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Throws(new NotSupportedException("history not served"));

        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);
        TimelineSnapshot snapshot = await service.AssembleJobBarsAsync(
            "Default",
            bounds,
            Noon,
            static _ => "00:00").ConfigureAwait(false);

        snapshot.HistoryUnavailable.Should().BeTrue();
        snapshot.Rows.Should().BeEmpty();
    }

    [Test]
    public void ParseTriggersRefreshIntervalCoercesStoredOneSecondToFiveSeconds()
    {
        TimelineService.ParseTriggersRefreshInterval("1s").Should().Be(TimeSpan.FromSeconds(5),
            "the old 1s floor was removed; stored values coerce to the new 5s floor");
        TimelineService.ParseTriggersRefreshInterval("5s").Should().Be(TimeSpan.FromSeconds(5));
    }

    [Test]
    public void AssembleJobBarsFromDoesNotCarrySevenDayHasMoreOntoANarrowerWindow()
    {
        // First visit probes the last week (HasMore) then draws a 15-minute window from that sample.
        // "latest 500 runs" must reflect the visible window, not the week-long probe.
        TimelineBounds narrow = TimelineService.CreateBounds(TimelineWindow.Last15Minutes, TimeSpan.Zero, Noon);
        TimelineActivitySample sample = new(
            [
                new DashboardHistoryEntry(
                    "Default",
                    "instance-1",
                    JobGroup: "demo",
                    JobName: "demo-job",
                    TriggerGroup: "demo",
                    TriggerName: "demo-trigger",
                    FiredAtUtc: Noon.AddHours(-6),
                    Duration: TimeSpan.FromMinutes(1),
                    Succeeded: true,
                    ExceptionMessage: null)
                {
                    EntryId = "old",
                    Result = JobRunResult.Succeeded
                },
                new DashboardHistoryEntry(
                    "Default",
                    "instance-1",
                    JobGroup: "demo",
                    JobName: "demo-job",
                    TriggerGroup: "demo",
                    TriggerName: "demo-trigger",
                    FiredAtUtc: Noon.AddMinutes(-5),
                    Duration: TimeSpan.FromMinutes(1),
                    Succeeded: true,
                    ExceptionMessage: null)
                {
                    EntryId = "recent",
                    Result = JobRunResult.Succeeded
                }
            ],
            [],
            HistoryUnavailable: false,
            HistoryHasMore: true);

        TimelineSnapshot snapshot = TimelineService.AssembleJobBarsFrom(
            sample,
            narrow,
            Noon,
            static instant => instant.ToString("HH:mm"));

        snapshot.HistoryHasMore.Should().BeFalse(
            "the week was capped, but the oldest probe run is older than the 15-minute window");
        snapshot.Rows.Should().ContainSingle();
    }

    [Test]
    public void AssembleJobBarsFromKeepsHasMoreWhenTheWindowItselfIsCapped()
    {
        TimelineBounds narrow = TimelineService.CreateBounds(TimelineWindow.Last15Minutes, TimeSpan.Zero, Noon);
        TimelineActivitySample sample = new(
            [
                new DashboardHistoryEntry(
                    "Default",
                    "instance-1",
                    JobGroup: "demo",
                    JobName: "demo-job",
                    TriggerGroup: "demo",
                    TriggerName: "demo-trigger",
                    FiredAtUtc: Noon.AddMinutes(-10),
                    Duration: TimeSpan.FromMinutes(1),
                    Succeeded: true,
                    ExceptionMessage: null)
                {
                    EntryId = "oldest-in-window",
                    Result = JobRunResult.Succeeded
                },
                new DashboardHistoryEntry(
                    "Default",
                    "instance-1",
                    JobGroup: "demo",
                    JobName: "demo-job",
                    TriggerGroup: "demo",
                    TriggerName: "demo-trigger",
                    FiredAtUtc: Noon.AddMinutes(-2),
                    Duration: TimeSpan.FromMinutes(1),
                    Succeeded: true,
                    ExceptionMessage: null)
                {
                    EntryId = "newest-in-window",
                    Result = JobRunResult.Succeeded
                }
            ],
            [],
            HistoryUnavailable: false,
            HistoryHasMore: true);

        TimelineSnapshot snapshot = TimelineService.AssembleJobBarsFrom(
            sample,
            narrow,
            Noon,
            static instant => instant.ToString("HH:mm"));

        snapshot.HistoryHasMore.Should().BeTrue(
            "the oldest returned run still sits inside the window, so the window itself is truncated");
    }

    [Test]
    public async Task HydrateTriggersAttachesBarsAndUpcomingMarks()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineService service = new(api);

        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);
        Dictionary<TimelineEntityKey, TimelineRowBuilder> builders = new();
        TimelineRowBuilder builder = TimelineService.GetOrAddRowBuilder(builders, "demo", "demo-job");
        TimelineBar bar = TimelineService.CreateBar(
            bounds,
            Noon.AddMinutes(-10),
            Noon.AddMinutes(-8),
            "qz-timeline-bar-succeeded",
            "ok",
            "entry-1");
        TimelineService.AddBarForTrigger(builder, "demo", "demo-trigger", bar);
        TimelineRow row = new(builder.Key, builder.JobGroup, builder.JobName, builder.BarsByTrigger, []);

        TriggerHeaderDto header = new(
            Group: "demo",
            Name: "demo-trigger",
            TriggerType: "simple",
            ScheduleSummary: "every 30s",
            State: TriggerState.Normal,
            ExecutionGroup: null)
        {
            NextFireTimeUtc = Noon.AddMinutes(5)
        };

        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns([header]);
        A.CallTo(() => api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
            .Returns([Noon.AddMinutes(5)]);

        await service.HydrateTriggersForRowsAsync(
            "Default",
            bounds,
            [row],
            [],
            static instant => instant.ToString("HH:mm")).ConfigureAwait(false);

        row.TriggersLoaded.Should().BeTrue();
        row.Triggers.Should().ContainSingle();
        TimelineTriggerRow trigger = row.Triggers[0];
        trigger.Bars.Should().ContainSingle();
        trigger.Marks.Should().ContainSingle();
        trigger.NextFireTimeUtc.Should().Be(Noon.AddMinutes(5));
    }

    [Test]
    public async Task HydrateTriggersResumesOnTheCapturedSynchronizationContext()
    {
        // GetTriggersOfJob completes on the thread pool; ConfigureAwait(true) must post the
        // continuation back so GetUpcomingFireTimes (and the later Clear/AddRange) still see
        // SynchronizationContext.Current — without it a Blazor render can hit "Collection was modified".
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineService service = new(api);

        TimelineBounds bounds = TimelineService.CreateBounds(TimelineWindow.LastHour, TimeSpan.Zero, Noon);
        TimelineRow row = new(new TimelineEntityKey("demo", "demo-job"), "demo", "demo-job", [], []);

        TriggerHeaderDto header = new(
            Group: "demo",
            Name: "demo-trigger",
            TriggerType: "simple",
            ScheduleSummary: "every 30s",
            State: TriggerState.Normal,
            ExecutionGroup: null);

        TaskCompletionSource<List<TriggerHeaderDto>> triggersReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns(new ValueTask<List<TriggerHeaderDto>>(triggersReady.Task));

        SynchronizationContext? observed = null;
        A.CallTo(() => api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
            .Invokes(() => observed = SynchronizationContext.Current)
            .Returns([]);

        SynchronizationContext? previous = SynchronizationContext.Current;
        TestSynchronizationContext context = new();
        SynchronizationContext.SetSynchronizationContext(context);
        try
        {
            Task hydrate = service.HydrateTriggersForRowsAsync(
                "Default",
                bounds,
                [row],
                [],
                static instant => instant.ToString("HH:mm"));

            await Task.Run(() => triggersReady.SetResult([header])).ConfigureAwait(false);
            await hydrate.ConfigureAwait(false);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }

        observed.Should().NotBeNull(
            "after a late GetTriggersOfJob, hydrate must resume on the captured SynchronizationContext");
        row.TriggersLoaded.Should().BeTrue();
        row.Triggers.Should().ContainSingle();
    }

    [Test]
    public async Task ProbeInitialWindowPicksTheNarrowestWindowThatStillContainsARun()
    {
        FakeTimeProvider clock = new(Noon);
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineService service = new(api);

        DashboardHistoryEntry recent = new(
            "Default",
            "instance-1",
            JobGroup: "demo",
            JobName: "demo-job",
            TriggerGroup: "demo",
            TriggerName: "demo-trigger",
            FiredAtUtc: Noon.AddMinutes(-8),
            Duration: TimeSpan.FromMinutes(1),
            Succeeded: true,
            ExceptionMessage: null);

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardHistoryEntry>([recent], HasMore: false, TotalCount: 1));
        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));

        TimelineWindowProbe probe = await service.ProbeInitialWindowAsync("Default", clock.GetUtcNow()).ConfigureAwait(false);

        probe.Window.Should().BeSameAs(TimelineWindow.Last10Minutes);
    }

    [Test]
    public async Task ProbeInitialWindowReusesTheSampleWhenAssemblingBars()
    {
        FakeTimeProvider clock = new(Noon);
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineService service = new(api);

        DashboardHistoryEntry recent = new(
            "Default",
            "instance-1",
            JobGroup: "demo",
            JobName: "demo-job",
            TriggerGroup: "demo",
            TriggerName: "demo-trigger",
            FiredAtUtc: Noon.AddMinutes(-8),
            Duration: TimeSpan.FromMinutes(1),
            Succeeded: true,
            ExceptionMessage: null)
        {
            EntryId = "entry-1",
            Result = JobRunResult.Succeeded
        };

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardHistoryEntry>([recent], HasMore: false, TotalCount: 1));
        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));

        TimelineWindowProbe probe = await service.ProbeInitialWindowAsync("Default", clock.GetUtcNow()).ConfigureAwait(false);
        TimelineBounds bounds = TimelineService.CreateBounds(probe.Window, TimeSpan.Zero, clock.GetUtcNow());
        TimelineSnapshot snapshot = TimelineService.AssembleJobBarsFrom(
            probe.Sample,
            bounds,
            clock.GetUtcNow(),
            static instant => instant.ToString("HH:mm"));

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        snapshot.Rows.Should().ContainSingle();
        probe.Window.Should().BeSameAs(TimelineWindow.Last10Minutes);
    }

    /// <summary>
    /// Posts work onto the thread pool while restoring this context as Current, so ConfigureAwait(true)
    /// continuations can observe a non-null SynchronizationContext.
    /// </summary>
    private sealed class TestSynchronizationContext : SynchronizationContext
    {
        public override void Post(SendOrPostCallback d, object? state)
        {
            ThreadPool.QueueUserWorkItem(static payload =>
            {
                (TestSynchronizationContext context, SendOrPostCallback callback, object? callbackState) =
                    ((TestSynchronizationContext, SendOrPostCallback, object?)) payload!;
                SynchronizationContext.SetSynchronizationContext(context);
                callback(callbackState);
            }, (this, d, state));
        }
    }
}
