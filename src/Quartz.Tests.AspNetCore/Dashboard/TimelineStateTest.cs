using FakeItEasy;
using Microsoft.Extensions.Time.Testing;

using Quartz.Dashboard.Models;
using Quartz.Dashboard.Services;
using Quartz.Dashboard.Services.Timeline;

namespace Quartz.Tests.AspNetCore.Dashboard;

/// <summary>
/// TimelineState load/reload orchestration: queued reloads and Triggers ticks.
/// </summary>
public class TimelineStateTest
{
    private static readonly DateTimeOffset Noon = new(2026, 11, 6, 12, 0, 0, TimeSpan.Zero);

    [Test]
    public async Task QueuedReloadUsesTheSchedulerNameAskedForDuringTheInFlightLoad()
    {
        // A switch during an in-flight load only queues Full; the loop must then ask for B, not keep A's name.
        // A's late completion must not ReplaceAllRows with A's jobs under B before the queue drains.
        FakeTimeProvider clock = new(Noon);
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = new(new TimelineService(api), clock);

        TaskCompletionSource firstQueryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstQueryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        List<string?> queriedSchedulers = [];
        List<string> jobNamesSeenWhenQueryingB = [];

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .ReturnsLazily((DashboardHistoryQuery query, CancellationToken _) =>
            {
                queriedSchedulers.Add(query.SchedulerName);
                if (queriedSchedulers.Count == 1)
                {
                    firstQueryEntered.TrySetResult();
                    return new ValueTask<PagedResult<DashboardHistoryEntry>>(
                        firstQueryRelease.Task.ContinueWith(
                            static completed =>
                            {
                                completed.GetAwaiter().GetResult();
                                return HistoryFor("A", "job-from-A");
                            },
                            TaskScheduler.Default));
                }

                // B's queued reload starts after A's in-flight core returns: A's rows must already be gone.
                foreach (TimelineSection section in state.Sections)
                {
                    foreach (TimelineRow row in section.Rows)
                    {
                        jobNamesSeenWhenQueryingB.Add(row.JobName);
                    }
                }

                return new ValueTask<PagedResult<DashboardHistoryEntry>>(HistoryFor("B", "job-from-B"));
            });
        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .ReturnsLazily((string schedulerName, DashboardFireInstanceQuery _, CancellationToken _) =>
                new ValueTask<PagedResult<FireInstanceDto>>(
                    new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0)));
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns([]);
        A.CallTo(() => api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([], HasMore: false, TotalCount: 0));

        Task firstLoad = state.LoadTimelineAsync("A", FormatInstant, FormatTick);
        await firstQueryEntered.Task.ConfigureAwait(false);

        // Match OnSchedulerChanged: clear then ask for B while A's probe is still in flight.
        state.ClearRows();
        await state.LoadTimelineAsync("B", FormatInstant, FormatTick).ConfigureAwait(false);
        state.Sections.Should().BeEmpty("after the switch, A's rows stay cleared");

        firstQueryRelease.SetResult();
        await firstLoad.ConfigureAwait(false);

        queriedSchedulers.Should().Contain("B",
            "a scheduler switch during an in-flight load must make the queued reload ask for B");
        queriedSchedulers[0].Should().Be("A",
            "the in-flight probe still asked for the scheduler it started with");
        jobNamesSeenWhenQueryingB.Should().NotContain("job-from-A",
            "A's late snapshot must not put A's rows back before B's reload runs");
        state.Sections.SelectMany(static section => section.Rows).Select(static row => row.JobName)
            .Should().NotContain("job-from-A")
            .And.Contain("job-from-B");
    }

    [Test]
    public async Task QueuedReloadClearsPreviousSchedulerErrorAndHistoryUnavailable()
    {
        // A Full reload drained after A fails (or serves no history) must not keep A's banners once B loads.
        FakeTimeProvider clock = new(Noon);
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = new(new TimelineService(api), clock);

        TaskCompletionSource firstQueryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource firstQueryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int queryCount = 0;

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .ReturnsLazily((DashboardHistoryQuery query, CancellationToken _) =>
            {
                int n = Interlocked.Increment(ref queryCount);
                if (n == 1)
                {
                    firstQueryEntered.TrySetResult();
                    return new ValueTask<PagedResult<DashboardHistoryEntry>>(
                        firstQueryRelease.Task.ContinueWith<PagedResult<DashboardHistoryEntry>>(
                            static completed =>
                            {
                                completed.GetAwaiter().GetResult();
                                throw new InvalidOperationException("scheduler A failed");
                            },
                            TaskScheduler.Default));
                }

                query.SchedulerName.Should().Be("B");
                return new ValueTask<PagedResult<DashboardHistoryEntry>>(EmptyHistory());
            });
        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns([]);
        A.CallTo(() => api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([], HasMore: false, TotalCount: 0));

        Task firstLoad = state.LoadTimelineAsync("A", FormatInstant, FormatTick);
        await firstQueryEntered.Task.ConfigureAwait(false);

        await state.LoadTimelineAsync("B", FormatInstant, FormatTick).ConfigureAwait(false);
        firstQueryRelease.SetResult();
        await firstLoad.ConfigureAwait(false);

        state.Error.Should().BeNull(
            "B's queued Full reload must clear A's error once B has loaded");
        state.HistoryUnavailable.Should().BeFalse();

        // Same race with a store that serves no history: A's NotSupported must not stick after B loads.
        firstQueryEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        firstQueryRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        Interlocked.Exchange(ref queryCount, 0);

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .ReturnsLazily((DashboardHistoryQuery query, CancellationToken _) =>
            {
                int n = Interlocked.Increment(ref queryCount);
                if (n == 1)
                {
                    firstQueryEntered.TrySetResult();
                    return new ValueTask<PagedResult<DashboardHistoryEntry>>(
                        firstQueryRelease.Task.ContinueWith<PagedResult<DashboardHistoryEntry>>(
                            static completed =>
                            {
                                completed.GetAwaiter().GetResult();
                                throw new NotSupportedException("A has no execution history");
                            },
                            TaskScheduler.Default));
                }

                query.SchedulerName.Should().Be("B");
                return new ValueTask<PagedResult<DashboardHistoryEntry>>(EmptyHistory());
            });

        firstLoad = state.LoadTimelineAsync("A", FormatInstant, FormatTick);
        await firstQueryEntered.Task.ConfigureAwait(false);

        await state.LoadTimelineAsync("B", FormatInstant, FormatTick).ConfigureAwait(false);
        firstQueryRelease.SetResult();
        await firstLoad.ConfigureAwait(false);

        state.HistoryUnavailable.Should().BeFalse(
            "B's queued Full reload must clear A's history-unavailable once B has loaded");
        state.Error.Should().BeNull();
    }

    [Test]
    public async Task ClearRowsDropsLoadedSectionsSoASwitchCannotActOnStaleKeys()
    {
        // OnSchedulerChanged clears before B's load lands; rows must not keep A's keys under B's name.
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = CreateLoadedState(api, withExecutingFire: false);

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        state.Sections.Should().NotBeEmpty();

        state.ClearRows();

        state.Sections.Should().BeEmpty(
            "a scheduler switch must clear rows before B loads so Run now cannot target A's keys on B");
        state.BarCount.Should().Be(0);
    }

    [Test]
    public async Task TriggersTickReusesHydratedRowsWithoutGetTriggersOfJob()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = CreateLoadedState(api, withExecutingFire: false);

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        await state.RefreshTriggersAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);

        // A Triggers tick must reuse ApplyPreviousTriggersOntoRows instead of re-reading every row.
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        TimelineTriggerRow trigger = state.Sections.Should().ContainSingle()
            .Which.Rows.Should().ContainSingle()
            .Which.Triggers.Should().ContainSingle().Subject;
        trigger.ScheduleSummary.Should().Be("every 30s",
            "trigger metadata from the initial hydrate must survive the cheap Triggers tick");
        trigger.Marks.Should().NotBeEmpty("upcoming marks from the initial hydrate must survive the tick");
    }

    [Test]
    public async Task TriggersTickReReadsHeadersOnlyWhenATriggerLeavesExecuting()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = CreateLoadedState(api, withExecutingFire: true);

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        // Still executing: cheap path only.
        await state.RefreshTriggersAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>([], HasMore: false, TotalCount: 0));

        await state.RefreshTriggersAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);

        // Headers are re-read only after a trigger leaves the executing set.
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Test]
    public async Task TriggersTickReReadsHeadersWhenNextFireTimeHasPassed()
    {
        // A fire that started and ended between ticks never appears in QueryFireInstances; a due
        // NextFireTimeUtc must still force a header re-read on the Triggers tick.
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        FakeTimeProvider clock = new(Noon);
        TimelineState state = CreateLoadedState(api, withExecutingFire: false, clock);

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();

        clock.Advance(TimeSpan.FromMinutes(6));
        await state.RefreshTriggersAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);

        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Test]
    public async Task TriggersTickReReadsADueTriggerOnlyOnceWhileNextFireStaysTheSame()
    {
        // Standby / misfire backlog / blocked serial triggers keep the same past NextFireTimeUtc;
        // that must not re-read headers on every Triggers tick.
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        FakeTimeProvider clock = new(Noon);
        TimelineState state = CreateLoadedState(api, withExecutingFire: false, clock);

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);

        clock.Advance(TimeSpan.FromMinutes(6));
        await state.RefreshTriggersAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();

        clock.Advance(TimeSpan.FromSeconds(5));
        await state.RefreshTriggersAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);

        // A due NextFireTimeUtc that does not change is re-read once, not on every tick.
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedTwiceExactly();
    }

    [Test]
    public async Task StaleTriggersHeaderRefreshDoesNotRememberDueNextAfterSwitch()
    {
        // A Triggers tick in flight across a switch must not write A's keys into the due-next map
        // after ClearRows: B with the same key and past next would otherwise skip its first re-read.
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        FakeTimeProvider clock = new(Noon);
        TimelineState state = CreateLoadedState(api, withExecutingFire: false, clock);
        // Keep history in-window after the clock advances past NextFireTimeUtc.
        state.ApplyQuery("1h", groupQuery: null, pageQuery: null, pageSizeQuery: null, panSecondsQuery: null);

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

        TaskCompletionSource headerEntered = new(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskCompletionSource headerRelease = new(TaskCreationOptions.RunContinuationsAsynchronously);
        int getTriggersCount = 0;

        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .ReturnsLazily((string _, JobKeyDto __, CancellationToken _) =>
            {
                int n = Interlocked.Increment(ref getTriggersCount);
                if (n == 2)
                {
                    headerEntered.TrySetResult();
                    return new ValueTask<List<TriggerHeaderDto>>(
                        headerRelease.Task.ContinueWith(
                            completed =>
                            {
                                completed.GetAwaiter().GetResult();
                                return new List<TriggerHeaderDto> { header };
                            },
                            TaskScheduler.Default));
                }

                return new ValueTask<List<TriggerHeaderDto>>([header]);
            });

        await state.LoadTimelineAsync("A", FormatInstant, FormatTick).ConfigureAwait(false);
        clock.Advance(TimeSpan.FromMinutes(6));

        Task triggersTick = state.RefreshTriggersAsync("A", FormatInstant, FormatTick);
        await headerEntered.Task.ConfigureAwait(false);

        state.ClearRows();
        await state.LoadTimelineAsync("B", FormatInstant, FormatTick).ConfigureAwait(false);
        headerRelease.SetResult();
        await triggersTick.ConfigureAwait(false);

        Fake.ClearRecordedCalls(api);
        await state.RefreshTriggersAsync("B", FormatInstant, FormatTick).ConfigureAwait(false);

        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    [Test]
    public async Task TriggersTickDoesNotSkipHydrationForCollapsedGroups()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = CreateLoadedState(api, withExecutingFire: false);
        state.ApplyCollapsedPreferences("""["demo"]""");

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        // Collapsed groups are not hydrated on the initial load.
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        await state.RefreshTriggersAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);
        // A Triggers tick must not mark collapsed rows as loaded via header refresh.
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustNotHaveHappened();

        state.Sections.Should().ContainSingle()
            .Which.Rows.Should().ContainSingle()
            .Which.TriggersLoaded.Should().BeFalse();

        await state.SetGroupCollapsedAsync("demo", collapsed: false, "Default", FormatInstant, FormatTick)
            .ConfigureAwait(false);

        // Expanding after a tick must still hydrate upcoming marks.
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
        A.CallTo(() => api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
            .MustHaveHappened();
        state.Sections.Should().ContainSingle()
            .Which.Rows.Should().ContainSingle()
            .Which.Triggers.Should().ContainSingle()
            .Which.Marks.Should().NotBeEmpty();
    }

    [Test]
    public async Task ACollapsedJobOutsideTheLoadedRowsStaysCollapsed()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = CreateLoadedState(api, withExecutingFire: false);
        TimelineEntityKey elsewhere = new("other", "job-with-no-runs-in-this-window");
        state.ApplyCollapsedPreferences(rawGroups: null, rawJobs: """[["other","job-with-no-runs-in-this-window"]]""");

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);

        state.IsJobCollapsed(elsewhere).Should().BeTrue(
            "the rows hold only the jobs with runs in this window on this scheduler, so a narrower window or another scheduler must not cost a job its preference");
    }

    [Test]
    public async Task CollapsedJobsStillHydrateSoTheStatusSquareHasData()
    {
        IQuartzApiClient api = A.Fake<IQuartzApiClient>();
        TimelineState state = CreateLoadedState(api, withExecutingFire: false);
        TimelineEntityKey jobKey = new("demo", "demo-job");
        state.ApplyCollapsedPreferences(rawGroups: null, rawJobs: """[["demo","demo-job"]]""");

        await state.LoadTimelineAsync("Default", FormatInstant, FormatTick).ConfigureAwait(false);

        state.IsJobCollapsed(jobKey).Should().BeTrue();
        TimelineRow row = state.Sections.Should().ContainSingle()
            .Which.Rows.Should().ContainSingle().Subject;
        row.TriggersLoaded.Should().BeTrue(
            "the collapsed heading still needs trigger state for the job status square");
        row.Triggers.Should().ContainSingle();
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .MustHaveHappenedOnceExactly();
    }

    private static TimelineState CreateLoadedState(
        IQuartzApiClient api,
        bool withExecutingFire,
        FakeTimeProvider? clock = null)
    {
        clock ??= new FakeTimeProvider(Noon);
        TimelineState state = new(new TimelineService(api), clock);

        DashboardHistoryEntry history = new(
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
            EntryId = "entry-1",
            Result = JobRunResult.Succeeded
        };

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

        List<FireInstanceDto> fires = [];
        if (withExecutingFire)
        {
            fires.Add(new FireInstanceDto(
                FireInstanceId: "fire-1",
                TriggerKey: new TriggerKeyDto("demo", "demo-trigger"),
                JobKey: new JobKeyDto("demo", "demo-job"),
                SchedulerInstanceId: "instance-1",
                State: FireInstanceState.Executing,
                FireTimeUtc: Noon.AddMinutes(-1),
                ScheduledFireTimeUtc: Noon.AddMinutes(-1),
                ExecutionGroup: null));
        }

        A.CallTo(() => api.QueryExecutions(A<DashboardHistoryQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<DashboardHistoryEntry>([history], HasMore: false, TotalCount: 1));
        A.CallTo(() => api.QueryFireInstances(A<string>._, A<DashboardFireInstanceQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<FireInstanceDto>(fires, HasMore: false, TotalCount: fires.Count));
        A.CallTo(() => api.GetTriggersOfJob(A<string>._, A<JobKeyDto>._, A<CancellationToken>._))
            .Returns([header]);
        A.CallTo(() => api.GetUpcomingFireTimes(A<string>._, A<TriggerKeyDto>._, A<int>._, A<CancellationToken>._))
            .Returns([Noon.AddMinutes(2)]);
        A.CallTo(() => api.QueryTriggers(A<string>._, A<DashboardTriggerQuery>._, A<CancellationToken>._))
            .Returns(new PagedResult<TriggerHeaderDto>([], HasMore: false, TotalCount: 0));

        return state;
    }

    private static PagedResult<DashboardHistoryEntry> EmptyHistory() =>
        new([], HasMore: false, TotalCount: 0);

    private static PagedResult<DashboardHistoryEntry> HistoryFor(string schedulerName, string jobName) =>
        new(
            [
                new DashboardHistoryEntry(
                    schedulerName,
                    "instance-1",
                    JobGroup: "demo",
                    JobName: jobName,
                    TriggerGroup: "demo",
                    TriggerName: "demo-trigger",
                    FiredAtUtc: Noon.AddMinutes(-10),
                    Duration: TimeSpan.FromMinutes(1),
                    Succeeded: true,
                    ExceptionMessage: null)
                {
                    EntryId = schedulerName + "-" + jobName,
                    Result = JobRunResult.Succeeded
                }
            ],
            HasMore: false,
            TotalCount: 1);

    private static string FormatInstant(DateTimeOffset instant) => instant.ToString("HH:mm");

    private static string FormatTick(DateTimeOffset instant, string format) => instant.ToString(format);
}
