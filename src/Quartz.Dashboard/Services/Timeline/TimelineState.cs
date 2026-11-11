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

using System.Text.Json;

using Quartz.Dashboard.Models;

namespace Quartz.Dashboard.Services.Timeline;

/// <summary>
/// In-memory Timeline view: filters, pagination, collapse, and load/refresh orchestration over
/// <see cref="TimelineService"/>. The page owns URL, dialogs and JS prefs.
/// </summary>
internal sealed class TimelineState
{
    private enum ReloadKind
    {
        None,
        Triggers,
        Full
    }

    private readonly List<TimelineRow> _allRows = [];
    private readonly List<TimelineRow> _groupRows = [];
    private readonly List<TimelineRow> _filteredRows = [];
    private readonly List<TimelineRow> _pageRows = [];
    private readonly List<TimelineSection> _sections = [];
    private readonly List<string> _groupOptions = [];
    private readonly List<TimelineTick> _ticks = [];
    private readonly HashSet<string> _collapsedGroups = new(StringComparer.Ordinal);
    private readonly HashSet<TimelineEntityKey> _nameMatchedTriggerKeys = new();
    private readonly TimelineService _service;
    private readonly TimeProvider _timeProvider;

    private TimelineWindow _window = TimelineWindow.Last15Minutes;
    private bool _windowFromQuery;
    private string _groupFilter = string.Empty;
    private string _nameFilter = string.Empty;
    private TimeSpan _panOffset;
    private TimelineBounds _bounds;
    private int _page = 1;
    private int _pageSize = TimelineService.DefaultPageSize;
    private int _totalPages = 1;
    private int _barCount;
    private bool _isLoading = true;
    private bool _chromeReady;
    private bool _loadingInProgress;
    private bool _historyUnavailable;
    private bool _historyHasMore;
    private bool _pendingUrlSync;
    private ReloadKind _reloadQueued;
    private string? _requestedSchedulerName;
    private HashSet<TimelineEntityKey> _lastExecutingTriggerKeys = [];
    private readonly Dictionary<TimelineEntityKey, DateTimeOffset> _lastDueHeaderRefreshNextFireTimes = new();
    private string? _error;

    public TimelineState(TimelineService service, TimeProvider timeProvider)
    {
        _service = service;
        _timeProvider = timeProvider;
    }

    public bool CanPanNewer => _panOffset > TimeSpan.Zero;

    public TimelineWindow Window => _window;

    public string GroupFilter => _groupFilter;

    public IReadOnlyList<string> GroupOptions => _groupOptions;

    public int PageSize => _pageSize;

    public string NameFilter => _nameFilter;

    public int ShownCount => _pageRows.Count;

    public int FilteredRowCount => _filteredRows.Count;

    public int BarCount => _barCount;

    public TimelineBounds Bounds => _bounds;

    public TimeSpan PanOffset => _panOffset;

    public IReadOnlyList<TimelineSection> Sections => _sections;

    public IReadOnlyList<TimelineTick> Ticks => _ticks;

    public int CurrentPage => _page;

    public int AllRowCount => _allRows.Count;

    public int GroupRowCount => _groupRows.Count;

    public bool IsLoading => _isLoading;

    public bool ChromeReady => _chromeReady;

    public bool HistoryUnavailable => _historyUnavailable;

    public bool HistoryHasMore => _historyHasMore;

    public bool PendingUrlSync
    {
        get => _pendingUrlSync;
        set => _pendingUrlSync = value;
    }

    public string? Error => _error;

    public bool IsGroupCollapsed(string jobGroup) => _collapsedGroups.Contains(jobGroup);

    public IReadOnlyCollection<string> CollapsedGroups => _collapsedGroups;

    public static HashSet<string> ParseCollapsedGroups(string? raw)
    {
        HashSet<string> collapsed = new(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(raw))
        {
            return collapsed;
        }

        try
        {
            string[]? groups = JsonSerializer.Deserialize<string[]>(raw);
            if (groups is null)
            {
                return collapsed;
            }

            foreach (string group in groups)
            {
                if (!string.IsNullOrWhiteSpace(group))
                {
                    collapsed.Add(group);
                }
            }
        }
        catch (JsonException)
        {
        }

        return collapsed;
    }

    /// <summary>
    /// Rows that should hit GetTriggersOfJob / upcoming fires: expanded groups on the current page.
    /// </summary>
    public static List<TimelineRow> SelectRowsForTriggerHydration(
        IReadOnlyList<TimelineRow> rows,
        IReadOnlySet<string> collapsedGroups)
    {
        if (collapsedGroups.Count == 0)
        {
            return rows.ToList();
        }

        List<TimelineRow> selected = [];
        foreach (TimelineRow row in rows)
        {
            if (!collapsedGroups.Contains(row.JobGroup))
            {
                selected.Add(row);
            }
        }

        return selected;
    }

    /// <summary>
    /// Expanded, already-hydrated rows that need a header re-read: a trigger left the executing set,
    /// or an unpaused trigger's <see cref="TimelineTriggerRow.NextFireTimeUtc"/> has passed without
    /// ever appearing in the live fire list (a fire that started and ended between ticks).
    /// </summary>
    /// <remarks>
    /// A due <see cref="TimelineTriggerRow.NextFireTimeUtc"/> that does not advance (standby, misfire
    /// backlog, or a blocked serial trigger) must not re-read on every tick: pass the next-fire values
    /// already refreshed in <paramref name="lastDueHeaderRefreshNextFireTimes"/>.
    /// </remarks>
    public static List<TimelineRow> SelectRowsNeedingTriggerHeaderRefresh(
        IReadOnlyList<TimelineRow> rows,
        IReadOnlySet<string> collapsedGroups,
        IReadOnlySet<TimelineEntityKey> previousExecutingTriggerKeys,
        IReadOnlySet<TimelineEntityKey> currentExecutingTriggerKeys,
        IReadOnlyDictionary<TimelineEntityKey, DateTimeOffset> lastDueHeaderRefreshNextFireTimes,
        DateTimeOffset now)
    {
        List<TimelineRow> selected = [];
        foreach (TimelineRow row in SelectRowsForTriggerHydration(rows, collapsedGroups))
        {
            if (!row.TriggersLoaded)
            {
                continue;
            }

            foreach (TimelineTriggerRow trigger in row.Triggers)
            {
                TimelineEntityKey key = TimelineService.CreateTriggerLookupKey(trigger.Group, trigger.Name);
                if (previousExecutingTriggerKeys.Contains(key) && !currentExecutingTriggerKeys.Contains(key))
                {
                    selected.Add(row);
                    break;
                }

                // Still listed as live: the leave-executing path refreshes when it ends. Skip so a
                // long run does not re-read headers on every Triggers tick.
                if (currentExecutingTriggerKeys.Contains(key))
                {
                    continue;
                }

                if (trigger.State != TriggerState.Paused
                    && trigger.NextFireTimeUtc is DateTimeOffset next
                    && next <= now
                    && !(lastDueHeaderRefreshNextFireTimes.TryGetValue(key, out DateTimeOffset remembered)
                         && remembered == next))
                {
                    selected.Add(row);
                    break;
                }
            }
        }

        return selected;
    }

    private void RememberDueHeaderRefreshNextFireTimes(IReadOnlyList<TimelineRow> rows, DateTimeOffset now)
    {
        foreach (TimelineRow row in rows)
        {
            foreach (TimelineTriggerRow trigger in row.Triggers)
            {
                TimelineEntityKey key = TimelineService.CreateTriggerLookupKey(trigger.Group, trigger.Name);
                if (trigger.State != TriggerState.Paused
                    && trigger.NextFireTimeUtc is DateTimeOffset next
                    && next <= now)
                {
                    _lastDueHeaderRefreshNextFireTimes[key] = next;
                }
                else
                {
                    _lastDueHeaderRefreshNextFireTimes.Remove(key);
                }
            }
        }
    }

    public void ApplyQuery(string? windowQuery, string? groupQuery, int? pageQuery, int? pageSizeQuery, int? panSecondsQuery)
    {
        _windowFromQuery = !string.IsNullOrWhiteSpace(windowQuery);
        _window = _windowFromQuery ? TimelineWindow.Parse(windowQuery) : TimelineWindow.Last15Minutes;
        _groupFilter = (groupQuery ?? string.Empty).Trim();
        _pageSize = TimelineService.NormalizePageSize(pageSizeQuery);
        _page = Math.Max(1, pageQuery ?? 1);
        _panOffset = TimelineService.NormalizePanOffset(panSecondsQuery);
    }

    public void ApplyNavigation(TimelineWindow window, string groupFilter, int page, int pageSize, TimeSpan panOffset)
    {
        if (panOffset < TimeSpan.Zero)
        {
            panOffset = TimeSpan.Zero;
        }

        _window = window;
        _windowFromQuery = true;
        _groupFilter = groupFilter.Trim();
        _pageSize = pageSize;
        _page = Math.Max(1, page);
        _panOffset = TimelineService.ClampPanOffset(panOffset);
    }

    public void ApplyCollapsedPreferences(string? raw)
    {
        _collapsedGroups.Clear();
        foreach (string group in ParseCollapsedGroups(raw))
        {
            _collapsedGroups.Add(group);
        }
    }

    public async Task SetNameFilterAsync(
        string name,
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        _nameFilter = name;
        _page = 1;
        await RefreshNameFilterAsync(schedulerName, formatInstant, formatTick).ConfigureAwait(true);
    }

    public async Task SetGroupCollapsedAsync(
        string jobGroup,
        bool collapsed,
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        if (string.IsNullOrWhiteSpace(jobGroup))
        {
            return;
        }

        if (collapsed)
        {
            _collapsedGroups.Add(jobGroup);
            return;
        }

        _collapsedGroups.Remove(jobGroup);
        await HydrateExpandedGroupAsync(jobGroup, schedulerName, formatInstant, formatTick).ConfigureAwait(true);
    }

    public IEnumerable<TimelineTriggerRow> VisibleTriggers(TimelineRow row)
    {
        if (!HasNameFilter || row.JobName.Contains(_nameFilter.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return row.Triggers;
        }

        string query = _nameFilter.Trim();
        return row.Triggers.Where(trigger =>
            trigger.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
            || _nameMatchedTriggerKeys.Contains(TimelineService.CreateTriggerLookupKey(trigger.Group, trigger.Name)));
    }

    public async Task LoadTimelineAsync(
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        // Keep the latest asked-for scheduler even when we only queue a reload: the in-flight
        // loop must not keep using the name it was first called with after a scheduler switch.
        _requestedSchedulerName = schedulerName;
        if (_loadingInProgress)
        {
            _reloadQueued = ReloadKind.Full;
            return;
        }

        _loadingInProgress = true;
        _error = null;
        _historyUnavailable = false;

        try
        {
            _reloadQueued = ReloadKind.None;
            await LoadTimelineCoreAsync(_requestedSchedulerName, formatInstant, formatTick).ConfigureAwait(true);
            await DrainReloadQueueAsync(formatInstant, formatTick).ConfigureAwait(true);
        }
        finally
        {
            _isLoading = false;
            _loadingInProgress = false;
        }
    }

    public async Task RefreshTriggersAsync(
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        _requestedSchedulerName = schedulerName;
        if (_loadingInProgress)
        {
            if (_reloadQueued != ReloadKind.Full)
            {
                _reloadQueued = ReloadKind.Triggers;
            }

            return;
        }

        _loadingInProgress = true;
        _error = null;

        try
        {
            _reloadQueued = ReloadKind.None;
            await RefreshTriggersCoreAsync(_requestedSchedulerName, formatInstant, formatTick).ConfigureAwait(true);
            await DrainReloadQueueAsync(formatInstant, formatTick).ConfigureAwait(true);
        }
        finally
        {
            _loadingInProgress = false;
        }
    }

    public void ResetPage() => _page = 1;

    /// <summary>
    /// Drops loaded rows immediately. A scheduler switch calls this before the new load so the
    /// previous scheduler's keys cannot stay on screen (or in menus) under the new name.
    /// </summary>
    public void ClearRows()
    {
        _allRows.Clear();
        _groupRows.Clear();
        _filteredRows.Clear();
        _pageRows.Clear();
        _sections.Clear();
        _groupOptions.Clear();
        _ticks.Clear();
        _nameMatchedTriggerKeys.Clear();
        _lastExecutingTriggerKeys = [];
        _lastDueHeaderRefreshNextFireTimes.Clear();
        _barCount = 0;
        _totalPages = 1;
    }

    /// <summary>
    /// Runs every reload queued while a load was already in flight, always against
    /// <see cref="_requestedSchedulerName"/>.
    /// </summary>
    private async Task DrainReloadQueueAsync(
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        while (_reloadQueued != ReloadKind.None)
        {
            ReloadKind next = _reloadQueued;
            _reloadQueued = ReloadKind.None;
            if (next == ReloadKind.Full)
            {
                await LoadTimelineCoreAsync(_requestedSchedulerName, formatInstant, formatTick).ConfigureAwait(true);
            }
            else
            {
                await RefreshTriggersCoreAsync(_requestedSchedulerName, formatInstant, formatTick).ConfigureAwait(true);
            }
        }
    }

    private async Task LoadTimelineCoreAsync(
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        // Queued Full reloads enter here without LoadTimelineAsync's entry clear; drop the previous
        // scheduler's error / history-unavailable so B's success cannot keep showing A's banners.
        _error = null;
        _historyUnavailable = false;

        try
        {
            if (string.IsNullOrWhiteSpace(schedulerName))
            {
                ClearRows();
                RebuildAxisTicks(formatTick);
                return;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            TimelineSnapshot snapshot;

            if (!_windowFromQuery)
            {
                TimelineWindowProbe probe = await _service.ProbeInitialWindowAsync(schedulerName, now).ConfigureAwait(true);
                if (IsStaleSchedulerLoad(schedulerName))
                {
                    // A finished after a switch to B: do not put A's window / rows back on screen.
                    return;
                }

                _window = probe.Window;
                _windowFromQuery = true;
                _panOffset = TimeSpan.Zero;
                _pendingUrlSync = true;
                SetWindowBounds(now);

                if (probe.Sample.HistoryUnavailable)
                {
                    _historyUnavailable = true;
                    _historyHasMore = false;
                    ClearRows();
                    RebuildAxisTicks(formatTick);
                    _chromeReady = true;
                    return;
                }

                snapshot = TimelineService.AssembleJobBarsFrom(probe.Sample, _bounds, now, formatInstant);
            }
            else
            {
                SetWindowBounds(now);
                snapshot = await _service.AssembleJobBarsAsync(
                    schedulerName,
                    _bounds,
                    now,
                    formatInstant).ConfigureAwait(true);
                if (IsStaleSchedulerLoad(schedulerName))
                {
                    return;
                }
            }

            if (snapshot.HistoryUnavailable)
            {
                _historyUnavailable = true;
                _historyHasMore = false;
                ClearRows();
                RebuildAxisTicks(formatTick);
                _chromeReady = true;
                return;
            }

            _historyHasMore = snapshot.HistoryHasMore;
            ReplaceAllRows(snapshot.Rows);
            BuildGroupRows();
            await ApplyNameFilterAndHydrateAsync(schedulerName, snapshot.ExecutingTriggerKeys, formatInstant, formatTick).ConfigureAwait(true);
            if (IsStaleSchedulerLoad(schedulerName))
            {
                // Switch during hydrate: drop A's rows again before B's queued reload applies.
                ClearRows();
                return;
            }

            _lastExecutingTriggerKeys = snapshot.ExecutingTriggerKeys;
            _chromeReady = true;
        }
        catch (Exception ex)
        {
            if (IsStaleSchedulerLoad(schedulerName))
            {
                return;
            }

            _error = ex.Message;
            ClearRows();
            _chromeReady = true;
        }
    }

    private bool IsStaleSchedulerLoad(string? schedulerName) =>
        !string.Equals(schedulerName, _requestedSchedulerName, StringComparison.Ordinal);

    private async Task RefreshTriggersCoreAsync(
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(schedulerName))
            {
                ClearRows();
                RebuildAxisTicks(formatTick);
                return;
            }

            DateTimeOffset now = _timeProvider.GetUtcNow();
            SetWindowBounds(now);

            TimelineSnapshot snapshot = await _service.AssembleJobBarsAsync(
                schedulerName,
                _bounds,
                now,
                formatInstant).ConfigureAwait(true);
            if (IsStaleSchedulerLoad(schedulerName))
            {
                return;
            }

            if (snapshot.HistoryUnavailable)
            {
                _historyUnavailable = true;
                _historyHasMore = false;
                ClearRows();
                RebuildAxisTicks(formatTick);
                return;
            }

            TimelineService.PatchPageRowBars(_pageRows, snapshot.Rows);

            // Cheap path: reuse hydrated trigger metadata and only refresh bars / executing / marks.
            // Full GetTriggersOfJob on every page row every 5s is too expensive (two scheduler reads
            // per row in-process, and that many round trips over HTTP), and it also marked collapsed
            // rows TriggersLoaded without upcoming marks.
            List<TimelineRow> headerRefreshRows = SelectRowsNeedingTriggerHeaderRefresh(
                _pageRows,
                _collapsedGroups,
                _lastExecutingTriggerKeys,
                snapshot.ExecutingTriggerKeys,
                _lastDueHeaderRefreshNextFireTimes,
                now);

            HashSet<TimelineEntityKey> headerRefreshKeys = new(headerRefreshRows.Count);
            foreach (TimelineRow row in headerRefreshRows)
            {
                headerRefreshKeys.Add(row.Key);
            }

            List<TimelineRow> reusedRows = [];
            foreach (TimelineRow row in _pageRows)
            {
                if (!headerRefreshKeys.Contains(row.Key))
                {
                    reusedRows.Add(row);
                }
            }

            if (reusedRows.Count > 0)
            {
                TimelineService.ApplyPreviousTriggersOntoRows(
                    _pageRows,
                    reusedRows,
                    snapshot.ExecutingTriggerKeys,
                    now);
            }

            // Re-read headers where a trigger left executing or its next fire is already due, so a
            // finished Run-now (or a fire missed between ticks) drops off without waiting for the
            // slower Jobs refresh. Never for collapsed rows. A due next that does not advance is
            // remembered so standby / misfire / blocked serial triggers are not re-read every tick.
            if (headerRefreshRows.Count > 0)
            {
                await _service.RefreshTriggerHeadersForRowsAsync(
                    schedulerName,
                    headerRefreshRows,
                    snapshot.ExecutingTriggerKeys,
                    now).ConfigureAwait(true);
                RememberDueHeaderRefreshNextFireTimes(headerRefreshRows, now);
            }

            _lastExecutingTriggerKeys = snapshot.ExecutingTriggerKeys;
            FinalizeSections();
            RebuildAxisTicks(formatTick);
        }
        catch (Exception ex)
        {
            _error = ex.Message;
        }
    }

    private async Task RefreshNameFilterAsync(
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        if (_loadingInProgress)
        {
            _reloadQueued = ReloadKind.Full;
            return;
        }

        _loadingInProgress = true;
        _requestedSchedulerName = schedulerName;
        try
        {
            await ApplyNameFilterAndHydrateAsync(
                schedulerName,
                CollectExecutingTriggerKeys(_pageRows),
                formatInstant,
                formatTick).ConfigureAwait(true);
        }
        finally
        {
            try
            {
                // A scheduler switch during the filter already updated _requestedSchedulerName.
                await DrainReloadQueueAsync(formatInstant, formatTick).ConfigureAwait(true);
            }
            finally
            {
                _loadingInProgress = false;
            }
        }
    }

    private async Task HydrateExpandedGroupAsync(
        string jobGroup,
        string? schedulerName,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        if (string.IsNullOrWhiteSpace(schedulerName) || _loadingInProgress)
        {
            if (_loadingInProgress)
            {
                _reloadQueued = ReloadKind.Full;
            }

            return;
        }

        List<TimelineRow> pending = [];
        foreach (TimelineRow row in _pageRows)
        {
            if (!string.Equals(row.JobGroup, jobGroup, StringComparison.Ordinal) || row.TriggersLoaded)
            {
                continue;
            }

            pending.Add(row);
        }

        if (pending.Count == 0)
        {
            return;
        }

        _loadingInProgress = true;
        _requestedSchedulerName = schedulerName;
        try
        {
            await _service.HydrateTriggersForRowsAsync(
                schedulerName,
                _bounds,
                pending,
                CollectExecutingTriggerKeys(_pageRows),
                formatInstant).ConfigureAwait(true);
            FinalizeSections();
            RebuildAxisTicks(formatTick);
        }
        finally
        {
            try
            {
                await DrainReloadQueueAsync(formatInstant, formatTick).ConfigureAwait(true);
            }
            finally
            {
                _loadingInProgress = false;
            }
        }
    }

    private async Task ApplyNameFilterAndHydrateAsync(
        string? schedulerName,
        HashSet<TimelineEntityKey> executingTriggerKeys,
        Func<DateTimeOffset, string> formatInstant,
        Func<DateTimeOffset, string, string> formatTick)
    {
        await MatchNameFilterAsync(schedulerName).ConfigureAwait(true);
        PaginateFilteredRows();
        List<TimelineRow> rowsToHydrate = SelectRowsForTriggerHydration(_pageRows, _collapsedGroups);
        if (rowsToHydrate.Count > 0 && !string.IsNullOrWhiteSpace(schedulerName))
        {
            await _service.HydrateTriggersForRowsAsync(
                schedulerName,
                _bounds,
                rowsToHydrate,
                executingTriggerKeys,
                formatInstant).ConfigureAwait(true);
        }

        FinalizeSections();
        RebuildAxisTicks(formatTick);
    }

    private async Task MatchNameFilterAsync(string? schedulerName)
    {
        _nameMatchedTriggerKeys.Clear();
        _filteredRows.Clear();

        if (!HasNameFilter)
        {
            _filteredRows.AddRange(_groupRows);
            return;
        }

        string query = _nameFilter.Trim();
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            foreach (TimelineEntityKey key in await _service.FindTriggerLookupKeysByNameAsync(schedulerName, query).ConfigureAwait(true))
            {
                _nameMatchedTriggerKeys.Add(key);
            }
        }

        foreach (TimelineRow row in _groupRows)
        {
            if (RowMatchesName(row))
            {
                _filteredRows.Add(row);
            }
        }
    }

    private static HashSet<TimelineEntityKey> CollectExecutingTriggerKeys(IEnumerable<TimelineRow> rows)
    {
        HashSet<TimelineEntityKey> executingTriggerKeys = new();
        foreach (TimelineRow row in rows)
        {
            foreach (TimelineTriggerRow trigger in row.Triggers)
            {
                if (trigger.IsExecuting)
                {
                    executingTriggerKeys.Add(TimelineService.CreateTriggerLookupKey(trigger.Group, trigger.Name));
                }
            }
        }

        return executingTriggerKeys;
    }

    private void ReplaceAllRows(IReadOnlyList<TimelineRow> rows)
    {
        _allRows.Clear();
        _allRows.AddRange(rows);
    }

    private void BuildGroupRows()
    {
        _groupOptions.Clear();
        _groupOptions.AddRange(_allRows.Select(static r => r.JobGroup).Distinct(StringComparer.Ordinal).OrderBy(static g => g, StringComparer.Ordinal));

        _groupRows.Clear();
        if (string.IsNullOrWhiteSpace(_groupFilter))
        {
            _groupRows.AddRange(_allRows);
            return;
        }

        foreach (TimelineRow row in _allRows)
        {
            if (string.Equals(row.JobGroup, _groupFilter, StringComparison.Ordinal))
            {
                _groupRows.Add(row);
            }
        }
    }

    private void PaginateFilteredRows()
    {
        _totalPages = Math.Max(1, (int) Math.Ceiling(_filteredRows.Count / (double) _pageSize));
        if (_page > _totalPages)
        {
            _page = _totalPages;
        }

        _pageRows.Clear();
        int skip = Math.Max(0, (_page - 1) * _pageSize);
        _pageRows.AddRange(_filteredRows.Skip(skip).Take(_pageSize));
    }

    private void FinalizeSections()
    {
        _sections.Clear();
        foreach (IGrouping<string, TimelineRow> group in _pageRows.GroupBy(static r => r.JobGroup, StringComparer.Ordinal))
        {
            _sections.Add(new TimelineSection(group.Key, group.ToList()));
        }

        _barCount = _pageRows.Sum(row => VisibleTriggers(row).Sum(static t => t.Bars.Count));
    }

    private bool HasNameFilter => !string.IsNullOrWhiteSpace(_nameFilter);

    private bool RowMatchesName(TimelineRow row)
    {
        string query = _nameFilter.Trim();
        if (row.JobName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (TimelineEntityKey triggerKey in row.BarsByTrigger.Keys)
        {
            if (_nameMatchedTriggerKeys.Contains(triggerKey))
            {
                return true;
            }
        }

        foreach (TimelineTriggerRow trigger in row.Triggers)
        {
            if (trigger.Name.Contains(query, StringComparison.OrdinalIgnoreCase)
                || _nameMatchedTriggerKeys.Contains(TimelineService.CreateTriggerLookupKey(trigger.Group, trigger.Name)))
            {
                return true;
            }
        }

        return false;
    }

    private void SetWindowBounds(DateTimeOffset now) =>
        _bounds = TimelineService.CreateBounds(_window, _panOffset, now);

    private void RebuildAxisTicks(Func<DateTimeOffset, string, string> formatTick)
    {
        if (_bounds.Start == default || _bounds.AxisEnd == default)
        {
            SetWindowBounds(_timeProvider.GetUtcNow());
        }

        TimelineService.FillAxisTicks(_ticks, _bounds, _window, formatTick);
    }
}
