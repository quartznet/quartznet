#region License
/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */
#endregion

using System.Globalization;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Models;

namespace Quartz.Dashboard.Services.Timeline;

/// <summary>
/// History and live fires loaded once for initial window choice, then reused to draw bars.
/// </summary>
internal sealed record TimelineActivitySample(
    IReadOnlyList<DashboardHistoryEntry> History,
    IReadOnlyList<FireInstanceDto> Executing,
    bool HistoryUnavailable,
    bool HistoryHasMore);

/// <summary>
/// Result of probing the last week so the page can pick a window without a second history round-trip.
/// </summary>
internal sealed record TimelineWindowProbe(TimelineWindow Window, TimelineActivitySample Sample);

/// <summary>
/// Loads Timeline rows from the scheduler API and owns the axis/bar geometry the page draws.
/// </summary>
internal sealed class TimelineService
{
    public const int DefaultPageSize = 25;
    public const string DefaultJobsRefreshKey = "30s";
    public const string DefaultTriggersRefreshKey = "5s";
    public const int DefaultUpcomingFireCount = 8;
    public const int DefaultHistoryTake = 500;

    public static readonly int[] PageSizes = [10, 25, 50, 100];

    /// <summary>
    /// Jobs refresh options (30s and slower). Keys are stored in localStorage.
    /// </summary>
    public static readonly (string Key, string Label, TimeSpan? Interval)[] JobsRefreshOptions =
    [
        ("30s", "30s", TimeSpan.FromSeconds(30)),
        ("1min", "1 min", TimeSpan.FromMinutes(1)),
        ("5min", "5 min", TimeSpan.FromMinutes(5)),
        ("Off", "Off", null),
    ];

    /// <summary>
    /// Triggers refresh options (5s and slower). Keys are stored in localStorage.
    /// "1s" was removed in 4.x; stored values are coerced to "5s" by <see cref="ParseTriggersRefreshInterval"/>.
    /// </summary>
    public static readonly (string Key, string Label, TimeSpan? Interval)[] TriggersRefreshOptions =
    [
        ("5s", "5s", TimeSpan.FromSeconds(5)),
        ("10s", "10s", TimeSpan.FromSeconds(10)),
        ("30s", "30s", TimeSpan.FromSeconds(30)),
        ("1min", "1 min", TimeSpan.FromMinutes(1)),
        ("5min", "5 min", TimeSpan.FromMinutes(5)),
        ("Off", "Off", null),
    ];

    private static readonly TimeSpan MaxPanOffset = TimelineWindow.LastWeek.Span;

    private const double MinBarPixelFraction = 0.0025;
    private const double BarGapFraction = 0.002;

    private readonly IQuartzApiClient _api;

    public TimelineService(IQuartzApiClient api)
    {
        _api = api;
    }

    /// <summary>
    /// Reads history and executing fires for <paramref name="bounds"/> and builds job rows with bars
    /// (triggers still empty until <see cref="HydrateTriggersForRowsAsync"/>).
    /// </summary>
    public async Task<TimelineSnapshot> AssembleJobBarsAsync(
        string schedulerName,
        TimelineBounds bounds,
        DateTimeOffset now,
        Func<DateTimeOffset, string> formatInstant,
        int historyTake = DefaultHistoryTake,
        CancellationToken cancellationToken = default)
    {
        TimelineActivitySample sample = await LoadActivitySampleAsync(
            schedulerName,
            bounds.Start,
            bounds.End,
            historyTake,
            cancellationToken).ConfigureAwait(true);

        if (sample.HistoryUnavailable)
        {
            return new TimelineSnapshot([], new HashSet<TimelineEntityKey>(), HistoryUnavailable: true);
        }

        return AssembleJobBarsFrom(sample, bounds, now, formatInstant);
    }

    /// <summary>
    /// Replaces <see cref="TimelineRow.BarsByTrigger"/> on each page row with the fresh sample, clearing
    /// bars when a job has no activity in the window.
    /// </summary>
    public static void PatchPageRowBars(
        IReadOnlyList<TimelineRow> pageRows,
        IReadOnlyList<TimelineRow> freshRows)
    {
        Dictionary<TimelineEntityKey, TimelineRow> freshByKey = new(freshRows.Count);
        foreach (TimelineRow fresh in freshRows)
        {
            freshByKey[fresh.Key] = fresh;
        }

        foreach (TimelineRow row in pageRows)
        {
            row.BarsByTrigger.Clear();
            if (!freshByKey.TryGetValue(row.Key, out TimelineRow? fresh))
            {
                continue;
            }

            foreach (KeyValuePair<TimelineEntityKey, PendingTriggerBars> pair in fresh.BarsByTrigger)
            {
                row.BarsByTrigger[pair.Key] = pair.Value;
            }
        }
    }

    /// <summary>
    /// Builds job rows from an already-loaded history/fires sample (no further API calls).
    /// </summary>
    public static TimelineSnapshot AssembleJobBarsFrom(
        TimelineActivitySample sample,
        TimelineBounds bounds,
        DateTimeOffset now,
        Func<DateTimeOffset, string> formatInstant)
    {
        if (sample.HistoryUnavailable)
        {
            return new TimelineSnapshot([], new HashSet<TimelineEntityKey>(), HistoryUnavailable: true);
        }

        Dictionary<TimelineEntityKey, TimelineRowBuilder> builders = new();
        HashSet<TimelineEntityKey> executingTriggerKeys = new();

        foreach (DashboardHistoryEntry entry in sample.History)
        {
            DateTimeOffset start = entry.FiredAtUtc;
            DateTimeOffset end = entry.FiredAtUtc + (entry.Duration > TimeSpan.Zero ? entry.Duration : TimeSpan.FromMilliseconds(1));
            if (end <= bounds.Start || start >= bounds.End)
            {
                continue;
            }

            TimelineRowBuilder builder = GetOrAddRowBuilder(builders, entry.JobGroup, entry.JobName);
            AddBarForTrigger(
                builder,
                entry.TriggerGroup,
                entry.TriggerName,
                CreateBar(
                    bounds,
                    start,
                    end,
                    CssClassForRunResult(entry.EffectiveResult),
                    BuildHistoryTitle(entry, formatInstant),
                    entry.EntryId));
        }

        foreach (FireInstanceDto fire in sample.Executing)
        {
            if (fire.JobKey is null)
            {
                continue;
            }

            DateTimeOffset start = fire.FireTimeUtc;
            DateTimeOffset barEnd = now < bounds.End ? now : bounds.End;
            if (barEnd <= bounds.Start || start >= bounds.End)
            {
                continue;
            }

            TimelineRowBuilder builder = GetOrAddRowBuilder(builders, fire.JobKey.Group, fire.JobKey.Name);
            AddBarForTrigger(
                builder,
                fire.TriggerKey.Group,
                fire.TriggerKey.Name,
                CreateBar(
                    bounds,
                    start,
                    barEnd,
                    "qz-timeline-bar-executing",
                    BuildExecutingTitle(fire, formatInstant),
                    entryId: null));

            executingTriggerKeys.Add(CreateTriggerLookupKey(fire.TriggerKey.Group, fire.TriggerKey.Name));
        }

        List<TimelineRow> rows = [];
        foreach (TimelineRowBuilder builder in builders.Values
                     .OrderBy(static b => b.JobGroup, StringComparer.Ordinal)
                     .ThenBy(static b => b.JobName, StringComparer.Ordinal))
        {
            rows.Add(new TimelineRow(builder.Key, builder.JobGroup, builder.JobName, builder.BarsByTrigger, []));
        }

        return new TimelineSnapshot(
            rows,
            executingTriggerKeys,
            HistoryUnavailable: false,
            HistoryHasMoreForBounds(sample, bounds));
    }

    /// <summary>
    /// Whether the chosen window itself is capped at <see cref="DefaultHistoryTake"/>.
    /// </summary>
    /// <remarks>
    /// A first-visit probe loads the last week once and reuses that sample for a narrower window.
    /// <see cref="TimelineActivitySample.HistoryHasMore"/> then means the week was capped, not the
    /// visible window. The newest-N sample truncates the window only when its oldest run still falls
    /// inside <paramref name="bounds"/>; otherwise the window is fully covered.
    /// </remarks>
    public static bool HistoryHasMoreForBounds(TimelineActivitySample sample, TimelineBounds bounds)
    {
        if (!sample.HistoryHasMore || sample.History.Count == 0)
        {
            return false;
        }

        DateTimeOffset oldest = sample.History[0].FiredAtUtc;
        for (int i = 1; i < sample.History.Count; i++)
        {
            DateTimeOffset firedAt = sample.History[i].FiredAtUtc;
            if (firedAt < oldest)
            {
                oldest = firedAt;
            }
        }

        return oldest >= bounds.Start;
    }

    /// <summary>
    /// Trigger lookup keys whose names contain <paramref name="nameContains"/> (one QueryTriggers call).
    /// </summary>
    /// <remarks>
    /// The query takes a maximum of 500 triggers. When <c>HasMore</c> is true the result set is
    /// capped and some name matches may be missing; the job-name filter still runs locally on all rows.
    /// </remarks>
    public async Task<HashSet<TimelineEntityKey>> FindTriggerLookupKeysByNameAsync(
        string schedulerName,
        string nameContains,
        CancellationToken cancellationToken = default)
    {
        HashSet<TimelineEntityKey> keys = new();
        if (string.IsNullOrWhiteSpace(nameContains))
        {
            return keys;
        }

        try
        {
            PagedResult<TriggerHeaderDto> page = await _api.QueryTriggers(
                schedulerName,
                new DashboardTriggerQuery
                {
                    NameContains = nameContains.Trim(),
                    Skip = 0,
                    Take = 500,
                    IncludeTotalCount = false
                },
                cancellationToken).ConfigureAwait(true);

            // If HasMore is true, the 500-trigger cap was hit; some matches may be absent but the
            // job-name filter runs locally over all rows so visible jobs are still found.
            foreach (TriggerHeaderDto header in page.Items)
            {
                keys.Add(CreateTriggerLookupKey(header.Group, header.Name));
            }
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Name search can still match job names locally.
        }

        return keys;
    }

    /// <summary>
    /// Loads trigger headers and upcoming marks for each row, attaching pending bars.
    /// </summary>
    public async Task HydrateTriggersForRowsAsync(
        string schedulerName,
        TimelineBounds bounds,
        IReadOnlyList<TimelineRow> rows,
        HashSet<TimelineEntityKey> executingTriggerKeys,
        Func<DateTimeOffset, string> formatInstant,
        int upcomingFireCount = DefaultUpcomingFireCount,
        CancellationToken cancellationToken = default)
    {
        foreach (TimelineRow row in rows)
        {
            // Build into a local list before touching row.Triggers so the live list is never
            // empty across an await — a renderer seeing it empty mid-hydrate would drop the row.
            Dictionary<TimelineEntityKey, PendingTriggerBars> pending = new(row.BarsByTrigger);
            List<TimelineTriggerRow> built = [];
            bool triggersOfJobAnswered = false;

            try
            {
                List<TriggerHeaderDto> triggers = await _api.GetTriggersOfJob(
                    schedulerName,
                    new JobKeyDto(row.JobGroup, row.JobName),
                    cancellationToken).ConfigureAwait(true);
                triggersOfJobAnswered = true;

                foreach (TriggerHeaderDto header in triggers
                             .OrderBy(static t => t.Group, StringComparer.Ordinal)
                             .ThenBy(static t => t.Name, StringComparer.Ordinal))
                {
                    List<TimelineMark> marks = [];
                    try
                    {
                        List<DateTimeOffset> fires = await _api.GetUpcomingFireTimes(
                            schedulerName,
                            new TriggerKeyDto(header.Group, header.Name),
                            upcomingFireCount,
                            cancellationToken).ConfigureAwait(true);

                        foreach (DateTimeOffset fire in fires)
                        {
                            if (fire < bounds.Start || fire > bounds.AxisEnd)
                            {
                                continue;
                            }

                            marks.Add(new TimelineMark(
                                fire,
                                "Upcoming · " + formatInstant(fire) + " · " + DisplayValueHelper.FormatKey(header.Group, header.Name)));
                        }
                    }
                    catch (Exception) when (!cancellationToken.IsCancellationRequested)
                    {
                        // Upcoming marks are optional for this trigger row.
                    }

                    TimelineEntityKey triggerKey = CreateTriggerLookupKey(header.Group, header.Name);
                    bool isExecuting = header.State == TriggerState.Executing
                                       || executingTriggerKeys.Contains(triggerKey);

                    List<TimelineBar> bars = TakePendingBars(pending, triggerKey);

                    built.Add(new TimelineTriggerRow(
                        header.Group,
                        header.Name,
                        header.State,
                        header.ScheduleSummary,
                        header.NextFireTimeUtc,
                        header.PreviousFireTimeUtc,
                        header.StartTimeUtc,
                        header.EndTimeUtc,
                        isExecuting,
                        bars,
                        marks));
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Keep history bars for the job when trigger headers cannot be loaded.
            }

            bool jobMissing = await ResolveJobMissingAfterTriggersAsync(
                schedulerName,
                row,
                built.Count,
                triggersOfJobAnswered,
                cancellationToken).ConfigureAwait(true);

            AppendOrphanTriggerRowsToList(built, pending, executingTriggerKeys);
            // Atomically replace after all awaits so the live list is never transiently empty.
            row.Triggers.Clear();
            row.Triggers.AddRange(built);
            row.TriggersLoaded = true;
            row.JobMissing = jobMissing;
        }
    }

    /// <summary>
    /// Re-reads trigger headers for each row (no upcoming fires) so a finished Run-now trigger drops off
    /// instead of keeping a stale Normal/Executing state until the slower Jobs refresh.
    /// </summary>
    public async Task RefreshTriggerHeadersForRowsAsync(
        string schedulerName,
        IReadOnlyList<TimelineRow> rows,
        HashSet<TimelineEntityKey> executingTriggerKeys,
        DateTimeOffset now,
        CancellationToken cancellationToken = default)
    {
        foreach (TimelineRow row in rows)
        {
            // Snapshot marks before any await; build into a local list to avoid clearing live list early.
            Dictionary<TimelineEntityKey, List<TimelineMark>> previousMarks = new();
            foreach (TimelineTriggerRow previous in row.Triggers)
            {
                previousMarks[CreateTriggerLookupKey(previous.Group, previous.Name)] = previous.Marks;
            }

            Dictionary<TimelineEntityKey, PendingTriggerBars> pending = new(row.BarsByTrigger);
            List<TimelineTriggerRow> built = [];
            bool triggersOfJobAnswered = false;

            try
            {
                List<TriggerHeaderDto> triggers = await _api.GetTriggersOfJob(
                    schedulerName,
                    new JobKeyDto(row.JobGroup, row.JobName),
                    cancellationToken).ConfigureAwait(true);
                triggersOfJobAnswered = true;

                foreach (TriggerHeaderDto header in triggers
                             .OrderBy(static t => t.Group, StringComparer.Ordinal)
                             .ThenBy(static t => t.Name, StringComparer.Ordinal))
                {
                    TimelineEntityKey triggerKey = CreateTriggerLookupKey(header.Group, header.Name);
                    bool isExecuting = executingTriggerKeys.Contains(triggerKey);
                    TriggerState? state = header.State;
                    if (isExecuting)
                    {
                        state = TriggerState.Executing;
                    }
                    else if (state == TriggerState.Executing)
                    {
                        state = TriggerState.Normal;
                    }

                    List<TimelineMark> marks = previousMarks.TryGetValue(triggerKey, out List<TimelineMark>? kept)
                        ? KeepFutureMarks(kept, now)
                        : [];

                    built.Add(new TimelineTriggerRow(
                        header.Group,
                        header.Name,
                        state,
                        header.ScheduleSummary,
                        header.NextFireTimeUtc,
                        header.PreviousFireTimeUtc,
                        header.StartTimeUtc,
                        header.EndTimeUtc,
                        isExecuting,
                        TakePendingBars(pending, triggerKey),
                        marks));
                }
            }
            catch (Exception) when (!cancellationToken.IsCancellationRequested)
            {
                // Keep history bars when headers cannot be refreshed.
            }

            bool jobMissing = await ResolveJobMissingAfterTriggersAsync(
                schedulerName,
                row,
                built.Count,
                triggersOfJobAnswered,
                cancellationToken).ConfigureAwait(true);

            AppendOrphanTriggerRowsToList(built, pending, executingTriggerKeys);
            // Atomically replace after all awaits.
            row.Triggers.Clear();
            row.Triggers.AddRange(built);
            row.TriggersLoaded = true;
            row.JobMissing = jobMissing;
        }
    }

    /// <summary>
    /// After <c>GetTriggersOfJob</c>, decides whether the job is gone. Live headers clear the flag.
    /// A failed triggers read keeps the previous flag (do not claim Missing from a transport error).
    /// An empty answer re-checks existence unless the row is already Missing (skip the extra
    /// <c>GetJobDetail</c> on every header refresh until a Jobs reload).
    /// </summary>
    private async Task<bool> ResolveJobMissingAfterTriggersAsync(
        string schedulerName,
        TimelineRow row,
        int liveTriggerCount,
        bool triggersOfJobAnswered,
        CancellationToken cancellationToken)
    {
        if (liveTriggerCount > 0)
        {
            return false;
        }

        if (!triggersOfJobAnswered)
        {
            return row.JobMissing;
        }

        if (row.JobMissing)
        {
            return true;
        }

        return await IsJobMissingAsync(schedulerName, row, cancellationToken).ConfigureAwait(true);
    }

    /// <summary>
    /// When hydrate found no live triggers, asks whether the job itself is gone. A durable job with
    /// every trigger unscheduled still exists and must not be marked missing.
    /// </summary>
    private async Task<bool> IsJobMissingAsync(
        string schedulerName,
        TimelineRow row,
        CancellationToken cancellationToken)
    {
        try
        {
            _ = await _api.GetJobDetail(
                schedulerName,
                new JobKeyDto(row.JobGroup, row.JobName),
                cancellationToken).ConfigureAwait(true);
            return false;
        }
        catch (KeyNotFoundException)
        {
            return true;
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Existence is unknown; keep the row without claiming the job is gone.
            return false;
        }
    }

    /// <summary>
    /// Merges previously loaded trigger metadata onto the current page rows, replacing bars with fresh data.
    /// </summary>
    /// <remarks>
    /// Upcoming marks that have already passed <paramref name="now" /> are dropped: Triggers refresh
    /// reuses marks from the last hydrate while bars catch up from history, and a mark that has fired
    /// would otherwise sit on top of its green bar until the slower Jobs refresh. Live executing is
    /// taken only from <paramref name="executingTriggerKeys" />: a stale <see cref="TriggerState.Executing" />
    /// on the reused header would keep the purple track until the slower Jobs refresh.
    /// </remarks>
    public static void ApplyPreviousTriggersOntoRows(
        IReadOnlyList<TimelineRow> previousRows,
        IReadOnlyList<TimelineRow> currentPageRows,
        HashSet<TimelineEntityKey> executingTriggerKeys,
        DateTimeOffset now)
    {
        Dictionary<TimelineEntityKey, TimelineRow> previousByKey = new(previousRows.Count);
        foreach (TimelineRow previous in previousRows)
        {
            previousByKey[previous.Key] = previous;
        }

        foreach (TimelineRow row in currentPageRows)
        {
            if (!previousByKey.TryGetValue(row.Key, out TimelineRow? previousRow) || !previousRow.TriggersLoaded)
            {
                continue;
            }

            // Copy BarsByTrigger so the live dict is not drained; PatchPageRowBars repopulates it next tick.
            Dictionary<TimelineEntityKey, PendingTriggerBars> pending = new(row.BarsByTrigger);
            // Snapshot first: Triggers refresh patches the same row instances, so Clear would empty the
            // list we are about to walk and leave only orphan rows with no Start/Next/Previous times.
            List<TimelineTriggerRow> previousTriggers = previousRow.Triggers.ToList();
            row.Triggers.Clear();
            foreach (TimelineTriggerRow previousTrigger in previousTriggers)
            {
                TimelineEntityKey triggerKey = CreateTriggerLookupKey(previousTrigger.Group, previousTrigger.Name);
                List<TimelineBar> bars = TakePendingBars(pending, triggerKey);
                bool isExecuting = executingTriggerKeys.Contains(triggerKey);
                TriggerState? state = previousTrigger.State;
                if (isExecuting)
                {
                    // Run now / a live fire: QueryFireInstances knows before the header says Executing.
                    state = TriggerState.Executing;
                }
                else if (state == TriggerState.Executing)
                {
                    // Header still says Executing from the last hydrate; live fires no longer list it.
                    state = TriggerState.Normal;
                }

                List<TimelineMark> marks = KeepFutureMarks(previousTrigger.Marks, now);
                row.Triggers.Add(previousTrigger with
                {
                    State = state,
                    Bars = bars,
                    IsExecuting = isExecuting,
                    Marks = marks
                });
            }

            AppendOrphanTriggerRowsToList(row.Triggers, pending, executingTriggerKeys);
            row.TriggersLoaded = true;
            row.JobMissing = previousRow.JobMissing;
        }
    }

    /// <summary>Upcoming marks that are still strictly after <paramref name="now" />.</summary>
    public static List<TimelineMark> KeepFutureMarks(IReadOnlyList<TimelineMark> marks, DateTimeOffset now)
    {
        if (marks.Count == 0)
        {
            return marks as List<TimelineMark> ?? [];
        }

        List<TimelineMark> kept = [];
        foreach (TimelineMark mark in marks)
        {
            if (mark.Instant > now)
            {
                kept.Add(mark);
            }
        }

        return kept;
    }

    /// <summary>
    /// Loads the last week of history once, picks the narrowest window that still shows activity,
    /// and returns both so the page can draw bars without a second history round-trip.
    /// </summary>
    public async Task<TimelineWindowProbe> ProbeInitialWindowAsync(
        string schedulerName,
        DateTimeOffset now,
        int historyTake = DefaultHistoryTake,
        CancellationToken cancellationToken = default)
    {
        TimelineActivitySample sample = await LoadActivitySampleAsync(
            schedulerName,
            now - TimelineWindow.LastWeek.Span,
            now,
            historyTake,
            cancellationToken).ConfigureAwait(true);

        if (sample.HistoryUnavailable)
        {
            return new TimelineWindowProbe(TimelineWindow.Last15Minutes, sample);
        }

        foreach (TimelineWindow candidate in TimelineWindow.Offered)
        {
            DateTimeOffset start = now - candidate.Span;
            if (HasActivityInWindow(sample.History, sample.Executing, start, now))
            {
                return new TimelineWindowProbe(candidate, sample);
            }
        }

        return new TimelineWindowProbe(TimelineWindow.Last15Minutes, sample);
    }

    private async Task<TimelineActivitySample> LoadActivitySampleAsync(
        string schedulerName,
        DateTimeOffset firedFrom,
        DateTimeOffset firedBefore,
        int historyTake,
        CancellationToken cancellationToken)
    {
        List<DashboardHistoryEntry> history = [];
        bool historyHasMore = false;
        try
        {
            PagedResult<DashboardHistoryEntry> page = await _api.QueryExecutions(new DashboardHistoryQuery
            {
                SchedulerName = schedulerName,
                FiredFrom = firedFrom,
                FiredBefore = firedBefore,
                Skip = 0,
                Take = historyTake,
                IncludeTotalCount = false
            }, cancellationToken).ConfigureAwait(true);
            history.AddRange(page.Items);
            historyHasMore = page.HasMore;
        }
        catch (NotSupportedException)
        {
            return new TimelineActivitySample([], [], HistoryUnavailable: true, HistoryHasMore: false);
        }

        List<FireInstanceDto> executing = [];
        try
        {
            PagedResult<FireInstanceDto> fires = await _api.QueryFireInstances(
                schedulerName,
                new DashboardFireInstanceQuery { Take = 200 },
                cancellationToken).ConfigureAwait(true);
            executing.AddRange(fires.Items);
        }
        catch (Exception) when (!cancellationToken.IsCancellationRequested)
        {
            // Live fires are optional: history alone is enough for bars / window choice.
        }

        return new TimelineActivitySample(history, executing, HistoryUnavailable: false, historyHasMore);
    }

    public static TimelineBounds CreateBounds(TimelineWindow window, TimeSpan panOffset, DateTimeOffset now)
    {
        DateTimeOffset end = now - panOffset;
        DateTimeOffset start = end - window.Span;
        DateTimeOffset axisEnd = panOffset <= TimeSpan.Zero
            ? now + LookAhead(window)
            : end;
        return new TimelineBounds(start, end, axisEnd);
    }

    public static void FillAxisTicks(
        List<TimelineTick> ticks,
        TimelineBounds bounds,
        TimelineWindow window,
        Func<DateTimeOffset, string, string> format)
    {
        ticks.Clear();
        long axisTicks = Math.Max(1, (bounds.AxisEnd - bounds.Start).Ticks);
        int divisions = window.TickCount;
        for (int i = 0; i <= divisions; i++)
        {
            double fraction = (double) i / divisions;
            DateTimeOffset instant = bounds.Start + TimeSpan.FromTicks((long) (axisTicks * fraction));
            ticks.Add(new TimelineTick(fraction, format(instant, window.TickFormat)));
        }
    }

    public static TimelineBar CreateBar(
        TimelineBounds bounds,
        DateTimeOffset start,
        DateTimeOffset end,
        string cssClass,
        string title,
        string? entryId)
    {
        double left = Clamp01((start - bounds.Start).Ticks / bounds.SpanTicks);
        double right = Clamp01((end - bounds.Start).Ticks / bounds.SpanTicks);
        double width = right - left;
        if (width < MinBarPixelFraction)
        {
            width = MinBarPixelFraction;
        }

        if (left + width > 1.0)
        {
            width = 1.0 - left;
        }

        return new TimelineBar(left, width, cssClass, title, entryId);
    }

    public static double ComputeFraction(TimelineBounds bounds, DateTimeOffset instant) =>
        Clamp01((instant - bounds.Start).Ticks / bounds.SpanTicks);

    public static void ApplyBarGaps(List<TimelineBar> bars)
    {
        for (int i = 0; i < bars.Count - 1; i++)
        {
            TimelineBar current = bars[i];
            TimelineBar next = bars[i + 1];
            double maxRight = next.Left - BarGapFraction;
            double maxWidth = maxRight - current.Left;
            if (current.Width <= maxWidth)
            {
                continue;
            }

            double width = maxWidth;
            if (width < MinBarPixelFraction)
            {
                width = next.Left > current.Left
                    ? Math.Max(next.Left - current.Left - BarGapFraction, MinBarPixelFraction * 0.5)
                    : MinBarPixelFraction * 0.5;
                if (width <= 0)
                {
                    width = MinBarPixelFraction * 0.5;
                }
            }

            bars[i] = current with { Width = width };
        }
    }

    public static List<TimelineBar> TakePendingBars(Dictionary<TimelineEntityKey, PendingTriggerBars> pending, TimelineEntityKey triggerKey)
    {
        if (!pending.Remove(triggerKey, out PendingTriggerBars? bucket))
        {
            return [];
        }

        List<TimelineBar> bars = [.. bucket.Bars];
        FinalizeTriggerBars(bars);
        return bars;
    }

    private static void FinalizeTriggerBars(List<TimelineBar> bars)
    {
        bars.Sort(static (a, b) => a.Left.CompareTo(b.Left));
        ApplyBarGaps(bars);
    }

    public static void AddBarForTrigger(TimelineRowBuilder builder, string triggerGroup, string triggerName, TimelineBar bar)
    {
        TimelineEntityKey key = CreateTriggerLookupKey(triggerGroup, triggerName);
        if (!builder.BarsByTrigger.TryGetValue(key, out PendingTriggerBars? bucket))
        {
            bucket = new PendingTriggerBars(triggerGroup, triggerName, []);
            builder.BarsByTrigger[key] = bucket;
        }

        bucket.Bars.Add(bar);
    }

    public static TimelineRowBuilder GetOrAddRowBuilder(Dictionary<TimelineEntityKey, TimelineRowBuilder> builders, string group, string name)
    {
        TimelineEntityKey key = new(group, name);
        if (!builders.TryGetValue(key, out TimelineRowBuilder? builder))
        {
            builder = new TimelineRowBuilder(key, group, name);
            builders[key] = builder;
        }

        return builder;
    }

    public static TimelineEntityKey CreateTriggerLookupKey(string group, string name) => new(group, name);

    public static string FormatPercent(double fraction) =>
        (Math.Clamp(fraction, 0, 1) * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    public static TimeSpan CreatePanStep(TimelineWindow window) => TimeSpan.FromTicks(window.Span.Ticks / 4);

    public static TimeSpan NormalizePanOffset(int? panSeconds)
    {
        if (panSeconds is int seconds && seconds > 0)
        {
            return ClampPanOffset(TimeSpan.FromSeconds(seconds));
        }

        return TimeSpan.Zero;
    }

    public static TimeSpan ClampPanOffset(TimeSpan offset)
    {
        if (offset <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        return offset > MaxPanOffset ? MaxPanOffset : offset;
    }

    public static TimeSpan? ParseJobsRefreshInterval(string? key) =>
        ParseRefreshInterval(key, JobsRefreshOptions);

    public static TimeSpan? ParseTriggersRefreshInterval(string? key)
    {
        // "1s" was the old minimum floor; coerce stored values to the new floor of "5s".
        if (string.Equals((key ?? string.Empty).Trim(), "1s", StringComparison.OrdinalIgnoreCase))
        {
            return TimeSpan.FromSeconds(5);
        }

        return ParseRefreshInterval(key, TriggersRefreshOptions);
    }

    public static int NormalizePageSize(int? value)
    {
        if (value is int size && PageSizes.Contains(size))
        {
            return size;
        }

        return DefaultPageSize;
    }

    public static int? ParseInteger(string? value) =>
        int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;

    private static TimeSpan? ParseRefreshInterval(
        string? key,
        (string Key, string Label, TimeSpan? Interval)[] options)
    {
        string trimmed = (key ?? string.Empty).Trim();
        foreach ((string optionKey, _, TimeSpan? interval) in options)
        {
            if (string.Equals(optionKey, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return interval;
            }
        }

        return null;
    }

    private static string CssClassForRunResult(JobRunResult result) => result switch
    {
        JobRunResult.Succeeded => "qz-timeline-bar-succeeded",
        JobRunResult.Skipped => "qz-timeline-bar-skipped",
        JobRunResult.Cancelled => "qz-timeline-bar-cancelled",
        _ => "qz-timeline-bar-failed"
    };

    private static string BuildHistoryTitle(DashboardHistoryEntry entry, Func<DateTimeOffset, string>? formatInstant)
    {
        string result = RunResultDisplay.Label(entry);
        string start = formatInstant?.Invoke(entry.FiredAtUtc) ?? entry.FiredAtUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
        string duration = DisplayValueHelper.FormatDuration(entry.Duration);
        string trigger = DisplayValueHelper.FormatKey(entry.TriggerGroup, entry.TriggerName);
        return $"{result} · {start} · {duration} · {trigger}";
    }

    private static string BuildExecutingTitle(FireInstanceDto fire, Func<DateTimeOffset, string>? formatInstant)
    {
        string start = formatInstant?.Invoke(fire.FireTimeUtc) ?? fire.FireTimeUtc.ToString("HH:mm", CultureInfo.InvariantCulture);
        string trigger = DisplayValueHelper.FormatKey(fire.TriggerKey.Group, fire.TriggerKey.Name);
        string progress = fire.Progress is int percent ? $" · {percent}%" : string.Empty;
        return $"Executing · since {start} · {trigger}{progress}";
    }

    private static void AppendOrphanTriggerRowsToList(
        List<TimelineTriggerRow> target,
        Dictionary<TimelineEntityKey, PendingTriggerBars> pending,
        HashSet<TimelineEntityKey> executingTriggerKeys)
    {
        foreach (PendingTriggerBars orphan in pending.Values
                     .OrderBy(static p => p.Group, StringComparer.Ordinal)
                     .ThenBy(static p => p.Name, StringComparer.Ordinal))
        {
            TimelineEntityKey triggerKey = CreateTriggerLookupKey(orphan.Group, orphan.Name);
            FinalizeTriggerBars(orphan.Bars);
            target.Add(new TimelineTriggerRow(
                orphan.Group,
                orphan.Name,
                State: null,
                ScheduleSummary: null,
                NextFireTimeUtc: null,
                PreviousFireTimeUtc: null,
                StartTimeUtc: null,
                EndTimeUtc: null,
                IsExecuting: executingTriggerKeys.Contains(triggerKey),
                Bars: orphan.Bars,
                Marks: []));
        }

        pending.Clear();
    }

    private static bool HasActivityInWindow(
        IReadOnlyList<DashboardHistoryEntry> history,
        IReadOnlyList<FireInstanceDto> executing,
        DateTimeOffset windowStart,
        DateTimeOffset windowEnd)
    {
        foreach (DashboardHistoryEntry entry in history)
        {
            DateTimeOffset end = entry.FiredAtUtc + (entry.Duration > TimeSpan.Zero ? entry.Duration : TimeSpan.FromMilliseconds(1));
            if (end > windowStart && entry.FiredAtUtc < windowEnd)
            {
                return true;
            }
        }

        foreach (FireInstanceDto fire in executing)
        {
            if (fire.JobKey is null)
            {
                continue;
            }

            if (fire.FireTimeUtc < windowEnd && windowEnd > windowStart)
            {
                return true;
            }
        }

        return false;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static TimeSpan LookAhead(TimelineWindow window)
    {
        long quarter = window.Span.Ticks / 4;
        long capped = TimeSpan.FromHours(1).Ticks;
        return TimeSpan.FromTicks(Math.Min(quarter, capped));
    }
}
