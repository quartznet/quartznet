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

// A component's public members exist for the renderer. They are not a documented contract.
#pragma warning disable CS1591

using System.Globalization;
using System.Runtime.InteropServices;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.Options;

using Quartz;
using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Services;

namespace Quartz.Dashboard.Components.Pages;

public partial class Timeline
{
    #region State

    private const int UpcomingFireCount = 8;

    private readonly List<TimelineRow> _allRows = [];
    private readonly List<TimelineRow> _groupRows = [];
    private readonly List<TimelineRow> _filteredRows = [];
    private readonly List<TimelineRow> _pageRows = [];
    private readonly List<TimelineSection> _sections = [];
    private readonly List<string> _groupOptions = [];
    private readonly List<TimelineTick> _ticks = [];

    private TimelineWindow _window = TimelineWindow.Last15Minutes;
    private bool _windowFromQuery;
    private string _groupFilter = string.Empty;
    private string _nameFilter = string.Empty;
    private TriggerActionTarget? _menuTarget;
    private TriggerActionTarget? _actionTarget;
    private bool _showPausePrompt;
    private bool _showUnscheduleConfirm;
    private TimeSpan _panOffset;
    private TimelineBounds _bounds;
    private int _page = 1;
    private int _pageSize = DefaultPageSize;
    private int _totalPages = 1;
    private int _barCount;
    private bool _isLoading = true;
    private bool _chromeReady;
    private bool _loadingInProgress;
    private bool _historyUnavailable;
    private bool _historyTruncated;
    private bool _pendingUrlSync;
    private string? _error;
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshLoopCancellation;

    // Filters, the chart and each trigger row render from this page's fields. Blazor
    // re-renders a child only when one of its parameters changes, and the page reference
    // stays the same for the life of the circuit, so the revision is the parameter that
    // moves on every render and carries the live window down to them.
    private int _viewRevision;

    private readonly int _historyTake = 500;

    [Parameter]
    [SupplyParameterFromQuery(Name = "window")]
    public string? WindowQuery { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "group")]
    public string? GroupQuery { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "page")]
    public int? PageQuery { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "pageSize")]
    public int? PageSizeQuery { get; set; }

    [Parameter]
    [SupplyParameterFromQuery(Name = "pan")]
    public int? PanQuery { get; set; }

    internal bool CanPanNewer => _panOffset > TimeSpan.Zero;

    internal TimelineWindow Window => _window;

    internal string GroupFilter => _groupFilter;

    internal IReadOnlyList<string> GroupOptions => _groupOptions;

    internal int PageSize => _pageSize;

    internal string NameFilter => _nameFilter;

    internal int ShownCount => _pageRows.Count;

    internal int MatchCount => _filteredRows.Count;

    internal int BarCount => _barCount;

    internal TimelineBounds Bounds => _bounds;

    internal TimeSpan PanOffset => _panOffset;

    internal IReadOnlyList<TimelineSection> Sections => _sections;

    internal IReadOnlyList<TimelineTick> Ticks => _ticks;

    internal int CurrentPage => _page;

    internal int AllRowCount => _allRows.Count;

    internal int GroupRowCount => _groupRows.Count;

    internal int FilteredRowCount => _filteredRows.Count;

    #endregion

    #region Lifecycle

    protected override bool ShouldRender()
    {
        _viewRevision++;
        return true;
    }

    protected override void OnInitialized()
    {
        Scheduler.OnSchedulerChanged += OnSchedulerChanged;
        _refreshLoopCancellation = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        _ = RunRefreshLoopAsync(_refreshLoopCancellation.Token);
    }

    protected override async Task OnParametersSetAsync()
    {
        _windowFromQuery = !string.IsNullOrWhiteSpace(WindowQuery);
        _window = _windowFromQuery ? TimelineWindow.Parse(WindowQuery) : TimelineWindow.Last15Minutes;
        _groupFilter = (GroupQuery ?? string.Empty).Trim();
        _pageSize = NormalizePageSize(PageSizeQuery);
        _page = Math.Max(1, PageQuery ?? 1);
        _panOffset = NormalizePan(PanQuery);
        await LoadDataAsync().ConfigureAwait(true);
    }

    private async void OnSchedulerChanged(object? sender, EventArgs e)
    {
        _page = 1;
        await LoadDataAsync().ConfigureAwait(true);
        await InvokeAsync(StateHasChanged).ConfigureAwait(true);
    }

    private async Task RunRefreshLoopAsync(CancellationToken cancellationToken)
    {
        if (_refreshTimer is null)
        {
            return;
        }

        try
        {
            while (await _refreshTimer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(true))
            {
                try
                {
                    await InvokeAsync(async () =>
                    {
                        await LoadDataAsync().ConfigureAwait(true);
                        StateHasChanged();
                    }).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (InvalidOperationException)
                {
                }
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!_pendingUrlSync)
        {
            return;
        }

        _pendingUrlSync = false;
        string path = BuildTimelinePath(_window, _groupFilter, _page, _pageSize, _panOffset);
        string current = (DashboardLink.ToBaseRelative(Navigation.Uri, Navigation.BaseUri) ?? string.Empty).TrimStart('/');
        if (!PathsEqual(path.TrimStart('/'), current))
        {
            Navigation.NavigateTo(path, replace: true);
        }

        await Task.CompletedTask.ConfigureAwait(true);
    }

    public void Dispose()
    {
        Scheduler.OnSchedulerChanged -= OnSchedulerChanged;
        _refreshLoopCancellation?.Cancel();
        _refreshLoopCancellation?.Dispose();
        _refreshTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    #endregion

    #region Address

    internal async Task SetNameFilterAsync(string name)
    {
        _nameFilter = name;
        _page = 1;
        await RefreshNameFilterAsync().ConfigureAwait(true);
        StateHasChanged();
    }

    private Task OnPageChangedAsync(int page)
    {
        return NavigateAsync(_window, _groupFilter, page, _pageSize, _panOffset);
    }

    internal void OnPanOffsetChanged(TimeSpan offset)
    {
        _panOffset = offset;
        SetWindowBounds(DateTimeOffset.UtcNow);
        RebuildTicks();
        StateHasChanged();
    }

    internal Task OnPanCommittedAsync()
    {
        return NavigateAsync(_window, _groupFilter, _page, _pageSize, _panOffset);
    }

    internal async Task NavigateAsync(TimelineWindow window, string groupFilter, int page, int pageSize, TimeSpan panOffset)
    {
        if (panOffset < TimeSpan.Zero)
        {
            panOffset = TimeSpan.Zero;
        }

        string path = BuildTimelinePath(window, groupFilter, page, pageSize, panOffset);
        string current = (DashboardLink.ToBaseRelative(Navigation.Uri, Navigation.BaseUri) ?? string.Empty).TrimStart('/');
        if (PathsEqual(path.TrimStart('/'), current))
        {
            _window = window;
            _windowFromQuery = true;
            _groupFilter = groupFilter.Trim();
            _pageSize = pageSize;
            _page = Math.Max(1, page);
            _panOffset = panOffset;
            await LoadDataAsync().ConfigureAwait(true);
            StateHasChanged();
            return;
        }

        Navigation.NavigateTo(path, replace: true);
    }

    private string BuildTimelinePath(TimelineWindow window, string groupFilter, int page, int pageSize, TimeSpan panOffset)
    {
        int panMinutes = (int) Math.Round(panOffset.TotalMinutes);
        string? group = string.IsNullOrWhiteSpace(groupFilter) ? null : groupFilter.Trim();
        return DashboardLink.Timeline(
            Options.Value,
            window.Key,
            group,
            page > 1 ? page : null,
            pageSize == DefaultPageSize ? null : pageSize,
            panMinutes > 0 ? panMinutes : null);
    }

    private static bool PathsEqual(string left, string right)
    {
        int leftQueryIndex = left.IndexOf('?');
        int rightQueryIndex = right.IndexOf('?');
        ReadOnlySpan<char> leftPath = leftQueryIndex >= 0 ? left.AsSpan(0, leftQueryIndex) : left.AsSpan();
        ReadOnlySpan<char> rightPath = rightQueryIndex >= 0 ? right.AsSpan(0, rightQueryIndex) : right.AsSpan();
        ReadOnlySpan<char> leftQuery = leftQueryIndex >= 0 ? left.AsSpan(leftQueryIndex) : default;
        ReadOnlySpan<char> rightQuery = rightQueryIndex >= 0 ? right.AsSpan(rightQueryIndex) : default;

        return leftPath.Equals(rightPath, StringComparison.OrdinalIgnoreCase)
            && leftQuery.SequenceEqual(rightQuery);
    }

    #endregion

    #region Loading

    private async Task LoadDataAsync()
    {
        if (_loadingInProgress)
        {
            return;
        }

        _loadingInProgress = true;
        _error = null;
        _historyUnavailable = false;
        _historyTruncated = false;

        try
        {
            string? schedulerName = Scheduler.ActiveSchedulerName;
            if (string.IsNullOrWhiteSpace(schedulerName))
            {
                ClearRows();
                RebuildTicks();
                return;
            }

            await Authorization.LoadOperations(schedulerName).ConfigureAwait(true);

            DateTimeOffset now = DateTimeOffset.UtcNow;

            if (!_windowFromQuery)
            {
                _window = await AutoPickWindowAsync(schedulerName, now).ConfigureAwait(true);
                _windowFromQuery = true;
                _panOffset = TimeSpan.Zero;
                _pendingUrlSync = true;
            }

            SetWindowBounds(now);
            await PopulateAsync(schedulerName, now).ConfigureAwait(true);
            _chromeReady = true;
        }
        catch (Exception ex)
        {
            _error = ex.Message;
            ClearRows();
            _chromeReady = true;
        }
        finally
        {
            _isLoading = false;
            _loadingInProgress = false;
        }
    }

    private async Task<TimelineWindow> AutoPickWindowAsync(string schedulerName, DateTimeOffset now)
    {
        List<DashboardHistoryEntry> history = [];
        try
        {
            PagedResult<DashboardHistoryEntry> page = await Api.QueryExecutions(new DashboardHistoryQuery
            {
                SchedulerName = schedulerName,
                FiredFrom = now - TimelineWindow.LastWeek.Span,
                FiredBefore = now,
                Skip = 0,
                Take = _historyTake,
                IncludeTotalCount = false
            }).ConfigureAwait(true);
            history.AddRange(page.Items);
        }
        catch (NotSupportedException)
        {
            _historyUnavailable = true;
            return TimelineWindow.Last15Minutes;
        }

        List<FireInstanceDto> executing = [];
        try
        {
            PagedResult<FireInstanceDto> fires = await Api.QueryFireInstances(
                schedulerName,
                new DashboardFireInstanceQuery { Take = 200 }).ConfigureAwait(true);
            executing.AddRange(fires.Items);
        }
        catch (Exception)
        {
        }

        foreach (TimelineWindow candidate in TimelineWindow.Offered)
        {
            DateTimeOffset start = now - candidate.Span;
            if (HasActivity(history, executing, start, now))
            {
                return candidate;
            }
        }

        return TimelineWindow.Last15Minutes;
    }

    private static bool HasActivity(
        List<DashboardHistoryEntry> history,
        List<FireInstanceDto> executing,
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

    private async Task PopulateAsync(string schedulerName, DateTimeOffset now)
    {
        List<DashboardHistoryEntry> history = [];
        try
        {
            PagedResult<DashboardHistoryEntry> page = await Api.QueryExecutions(new DashboardHistoryQuery
            {
                SchedulerName = schedulerName,
                FiredFrom = _bounds.Start,
                FiredBefore = _bounds.End,
                Skip = 0,
                Take = _historyTake,
                IncludeTotalCount = true
            }).ConfigureAwait(true);
            history.AddRange(page.Items);
            int total = page.TotalCount ?? page.Items.Count;
            _historyTruncated = total > page.Items.Count || page.HasMore;
        }
        catch (NotSupportedException)
        {
            _historyUnavailable = true;
            ClearRows();
            RebuildTicks();
            return;
        }

        List<FireInstanceDto> executing = [];
        try
        {
            PagedResult<FireInstanceDto> fires = await Api.QueryFireInstances(
                schedulerName,
                new DashboardFireInstanceQuery { Take = 200 }).ConfigureAwait(true);
            executing.AddRange(fires.Items);
        }
        catch (Exception)
        {
        }

        Dictionary<string, TimelineRowBuilder> builders = new(StringComparer.OrdinalIgnoreCase);

        foreach (DashboardHistoryEntry entry in history)
        {
            DateTimeOffset start = entry.FiredAtUtc;
            DateTimeOffset end = entry.FiredAtUtc + (entry.Duration > TimeSpan.Zero ? entry.Duration : TimeSpan.FromMilliseconds(1));
            if (end <= _bounds.Start || start >= _bounds.End)
            {
                continue;
            }

            TimelineRowBuilder builder = GetOrAdd(builders, entry.JobGroup, entry.JobName);
            AddBarForTrigger(
                builder,
                entry.TriggerGroup,
                entry.TriggerName,
                CreateBar(
                    _bounds,
                    start,
                    end,
                    BarClass(entry.EffectiveResult),
                    BuildHistoryTitle(entry),
                    entry.EntryId));
        }

        foreach (FireInstanceDto fire in executing)
        {
            if (fire.JobKey is null)
            {
                continue;
            }

            DateTimeOffset start = fire.FireTimeUtc;
            DateTimeOffset barEnd = now < _bounds.End ? now : _bounds.End;
            if (barEnd <= _bounds.Start || start >= _bounds.End)
            {
                continue;
            }

            TimelineRowBuilder builder = GetOrAdd(builders, fire.JobKey.Group, fire.JobKey.Name);
            AddBarForTrigger(
                builder,
                fire.TriggerKey.Group,
                fire.TriggerKey.Name,
                CreateBar(
                    _bounds,
                    start,
                    barEnd,
                    "qz-timeline-bar-executing",
                    BuildExecutingTitle(fire),
                    entryId: null));
        }

        HashSet<string> executingTriggers = new(StringComparer.OrdinalIgnoreCase);
        foreach (FireInstanceDto fire in executing)
        {
            executingTriggers.Add(TriggerLookupKey(fire.TriggerKey.Group, fire.TriggerKey.Name));
        }

        _allRows.Clear();
        foreach (TimelineRowBuilder builder in builders.Values.OrderBy(static b => b.JobGroup, StringComparer.OrdinalIgnoreCase)
                     .ThenBy(static b => b.JobName, StringComparer.OrdinalIgnoreCase))
        {
            _allRows.Add(new TimelineRow(builder.Key, builder.JobGroup, builder.JobName, builder.BarsByTrigger, []));
        }

        RebuildDerivedViews(HasNameFilter);
        await LoadTriggersForPageAsync(schedulerName, executingTriggers).ConfigureAwait(true);
        FinalizeRows();
        RebuildTicks();
    }

    private void RebuildDerivedViews(bool loadTriggersForNameSearch)
    {
        _groupOptions.Clear();
        _groupOptions.AddRange(_allRows.Select(static r => r.JobGroup).Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(static g => g, StringComparer.OrdinalIgnoreCase));

        _groupRows.Clear();
        if (string.IsNullOrWhiteSpace(_groupFilter))
        {
            _groupRows.AddRange(_allRows);
        }
        else
        {
            foreach (TimelineRow row in _allRows)
            {
                if (string.Equals(row.JobGroup, _groupFilter, StringComparison.OrdinalIgnoreCase))
                {
                    _groupRows.Add(row);
                }
            }
        }

        _pageRows.Clear();
        if (HasNameFilter && loadTriggersForNameSearch)
        {
            _pageRows.AddRange(_groupRows);
            return;
        }

        Paginate(_groupRows);
    }

    private void FinalizeRows()
    {
        if (!HasNameFilter)
        {
            Paginate(_groupRows);
            return;
        }

        List<TimelineRow> matched = [];
        foreach (TimelineRow row in _groupRows)
        {
            if (RowMatchesName(row))
            {
                matched.Add(row);
            }
        }

        Paginate(matched);
    }

    private async Task RefreshNameFilterAsync()
    {
        if (HasNameFilter)
        {
            string? schedulerName = Scheduler.ActiveSchedulerName;
            if (!string.IsNullOrWhiteSpace(schedulerName))
            {
                List<TimelineRow> pending = _groupRows.Where(static row => !row.TriggersLoaded).ToList();
                if (pending.Count > 0)
                {
                    HashSet<string> executingTriggers = [];
                    foreach (TimelineRow row in _groupRows)
                    {
                        foreach (TimelineTriggerRow trigger in row.Triggers)
                        {
                            if (trigger.IsExecuting)
                            {
                                executingTriggers.Add(TriggerLookupKey(trigger.Group, trigger.Name));
                            }
                        }
                    }

                    List<TimelineRow> previousPage = _pageRows.ToList();
                    _pageRows.Clear();
                    _pageRows.AddRange(pending);
                    await LoadTriggersForPageAsync(schedulerName, executingTriggers).ConfigureAwait(true);
                    _pageRows.Clear();
                    _pageRows.AddRange(previousPage);
                }
            }
        }

        FinalizeRows();
    }

    private bool HasNameFilter => !string.IsNullOrWhiteSpace(_nameFilter);

    private bool RowMatchesName(TimelineRow row)
    {
        string query = _nameFilter.Trim();
        if (row.JobName.Contains(query, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        foreach (TimelineTriggerRow trigger in row.Triggers)
        {
            if (trigger.Name.Contains(query, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    internal IEnumerable<TimelineTriggerRow> VisibleTriggers(TimelineRow row)
    {
        if (!HasNameFilter || row.JobName.Contains(_nameFilter.Trim(), StringComparison.OrdinalIgnoreCase))
        {
            return row.Triggers;
        }

        string query = _nameFilter.Trim();
        return row.Triggers.Where(trigger => trigger.Name.Contains(query, StringComparison.OrdinalIgnoreCase));
    }

    private void Paginate(List<TimelineRow> source)
    {
        _filteredRows.Clear();
        _filteredRows.AddRange(source);

        _totalPages = Math.Max(1, (int) Math.Ceiling(_filteredRows.Count / (double) _pageSize));
        if (_page > _totalPages)
        {
            _page = _totalPages;
        }

        _pageRows.Clear();
        int skip = Math.Max(0, (_page - 1) * _pageSize);
        _pageRows.AddRange(_filteredRows.Skip(skip).Take(_pageSize));

        _sections.Clear();
        foreach (IGrouping<string, TimelineRow> group in _pageRows.GroupBy(static r => r.JobGroup, StringComparer.OrdinalIgnoreCase))
        {
            _sections.Add(new TimelineSection(group.Key, group.ToList()));
        }

        _barCount = _pageRows.Sum(row => VisibleTriggers(row).Sum(static t => t.Bars.Count));
    }

    private async Task LoadTriggersForPageAsync(string schedulerName, HashSet<string> executingTriggers)
    {
        foreach (TimelineRow row in _pageRows)
        {
            row.Triggers.Clear();
            Dictionary<string, PendingTriggerBars> pending = new(row.BarsByTrigger, StringComparer.OrdinalIgnoreCase);

            try
            {
                List<TriggerHeaderDto> triggers = await Api.GetTriggersOfJob(
                    schedulerName,
                    new JobKeyDto(row.JobGroup, row.JobName)).ConfigureAwait(true);

                foreach (TriggerHeaderDto header in triggers.OrderBy(static t => t.Group, StringComparer.OrdinalIgnoreCase)
                             .ThenBy(static t => t.Name, StringComparer.OrdinalIgnoreCase))
                {
                    List<TimelineMark> marks = [];
                    try
                    {
                        List<DateTimeOffset> fires = await Api.GetUpcomingFireTimes(
                            schedulerName,
                            new TriggerKeyDto(header.Group, header.Name),
                            UpcomingFireCount).ConfigureAwait(true);

                        foreach (DateTimeOffset fire in fires)
                        {
                            if (fire < _bounds.Start || fire > _bounds.AxisEnd)
                            {
                                continue;
                            }

                            marks.Add(new TimelineMark(
                                Fraction(_bounds, fire),
                                "Upcoming · " + FormatInstant(fire) + " · " + DisplayValueHelper.FormatKey(header.Group, header.Name)));
                        }
                    }
                    catch (Exception)
                    {
                        // Trigger row still useful without marks.
                    }

                    string triggerKey = TriggerLookupKey(header.Group, header.Name);
                    bool isExecuting = header.State == TriggerState.Executing
                                       || executingTriggers.Contains(triggerKey);

                    List<TimelineBar> bars = TakePendingBars(pending, triggerKey);

                    row.Triggers.Add(new TimelineTriggerRow(
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
            catch (Exception)
            {
                // Job may have been deleted since the history row was written; still show orphan bars.
            }

            foreach (PendingTriggerBars orphan in pending.Values
                         .OrderBy(static p => p.Group, StringComparer.OrdinalIgnoreCase)
                         .ThenBy(static p => p.Name, StringComparer.OrdinalIgnoreCase))
            {
                string triggerKey = TriggerLookupKey(orphan.Group, orphan.Name);
                FinalizeTriggerBars(orphan.Bars);
                row.Triggers.Add(new TimelineTriggerRow(
                    orphan.Group,
                    orphan.Name,
                    State: null,
                    ScheduleSummary: null,
                    NextFireTimeUtc: null,
                    PreviousFireTimeUtc: null,
                    StartTimeUtc: null,
                    EndTimeUtc: null,
                    IsExecuting: executingTriggers.Contains(triggerKey),
                    Bars: orphan.Bars,
                    Marks: []));
            }

            row.TriggersLoaded = true;
        }
    }

    private void SetWindowBounds(DateTimeOffset now)
    {
        _bounds = BoundsFor(_window, _panOffset, now);
    }

    private void ClearRows()
    {
        _allRows.Clear();
        _groupRows.Clear();
        _filteredRows.Clear();
        _pageRows.Clear();
        _sections.Clear();
        _groupOptions.Clear();
        _ticks.Clear();
        _barCount = 0;
        _totalPages = 1;
    }

    private void RebuildTicks()
    {
        if (_bounds.Start == default || _bounds.AxisEnd == default)
        {
            SetWindowBounds(DateTimeOffset.UtcNow);
        }

        FillTicks(
            _ticks,
            _bounds,
            _window,
            (instant, format) => Scheduler.FormatInSelectedTimeZone(instant, format));
    }

    private static string BarClass(JobRunResult result) => result switch
    {
        JobRunResult.Succeeded => "qz-timeline-bar-succeeded",
        JobRunResult.Skipped => "qz-timeline-bar-skipped",
        JobRunResult.Cancelled => "qz-timeline-bar-cancelled",
        _ => "qz-timeline-bar-failed"
    };

    private string BuildHistoryTitle(DashboardHistoryEntry entry)
    {
        string result = RunResultDisplay.Label(entry);
        string start = FormatInstant(entry.FiredAtUtc);
        string duration = DisplayValueHelper.FormatDuration(entry.Duration);
        string trigger = DisplayValueHelper.FormatKey(entry.TriggerGroup, entry.TriggerName);
        return $"{result} · {start} · {duration} · {trigger}";
    }

    private string BuildExecutingTitle(FireInstanceDto fire)
    {
        string start = FormatInstant(fire.FireTimeUtc);
        string trigger = DisplayValueHelper.FormatKey(fire.TriggerKey.Group, fire.TriggerKey.Name);
        string progress = fire.Progress is int percent ? $" · {percent}%" : string.Empty;
        return $"Executing · since {start} · {trigger}{progress}";
    }

    private string FormatInstant(DateTimeOffset instant) => Scheduler.FormatInSelectedTimeZone(instant, "HH:mm");

    #endregion

    #region Links

    internal bool TryFindTrigger(
        string jobGroup,
        string jobName,
        string triggerGroup,
        string triggerName,
        out TimelineRow? row,
        out TimelineTriggerRow? trigger)
    {
        foreach (TimelineSection section in _sections)
        {
            foreach (TimelineRow candidate in section.Rows)
            {
                if (!string.Equals(candidate.JobGroup, jobGroup, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(candidate.JobName, jobName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                foreach (TimelineTriggerRow item in candidate.Triggers)
                {
                    if (string.Equals(item.Group, triggerGroup, StringComparison.OrdinalIgnoreCase)
                        && string.Equals(item.Name, triggerName, StringComparison.OrdinalIgnoreCase))
                    {
                        row = candidate;
                        trigger = item;
                        return true;
                    }
                }
            }
        }

        row = null;
        trigger = null;
        return false;
    }

    internal string ExecutionPath(string entryId) =>
        WithTimelineOrigin(DashboardLink.To(Options.Value, "history/" + Uri.EscapeDataString(entryId)));

    internal string JobPath(string group, string name) =>
        WithTimelineOrigin(DashboardLink.To(Options.Value, "jobs/" + Uri.EscapeDataString(group) + "/" + Uri.EscapeDataString(name)));

    internal string TriggerPath(string group, string name) =>
        WithTimelineOrigin(DashboardLink.To(Options.Value, "triggers/" + Uri.EscapeDataString(group) + "/" + Uri.EscapeDataString(name)));

    /// <summary>
    /// Appends the Timeline view the visitor is looking at, so the page they open can link back to it.
    /// </summary>
    private string WithTimelineOrigin(string path)
    {
        string timeline = BuildTimelinePath(_window, _groupFilter, _page, _pageSize, _panOffset);
        int queryIndex = timeline.IndexOf('?');
        string timelineQuery = queryIndex >= 0 ? timeline[(queryIndex + 1)..] : string.Empty;
        string origin = path + "?from=timeline";
        return timelineQuery.Length == 0 ? origin : origin + "&" + timelineQuery;
    }

    #endregion

    #region Trigger actions

    internal bool IsMenuOpen(TimelineRow row, TimelineTriggerRow trigger)
    {
        return _menuTarget is not null && SameTrigger(_menuTarget, row, trigger);
    }

    internal void ToggleTriggerMenu(TimelineTriggerRef target)
    {
        _menuTarget = IsMenuOpen(target.Row, target.Trigger) ? null : TriggerActionTarget.From(target.Row, target.Trigger);
        StateHasChanged();
    }

    private void CloseTriggerMenu()
    {
        _menuTarget = null;
    }

    internal void PromptPause(TimelineTriggerRef target)
    {
        _menuTarget = null;
        _actionTarget = TriggerActionTarget.From(target.Row, target.Trigger);
        _showPausePrompt = true;
        StateHasChanged();
    }

    private Task CancelPauseAsync()
    {
        _showPausePrompt = false;
        return Task.CompletedTask;
    }

    private Task PauseTriggerAsync(string? reason)
    {
        TriggerActionTarget? target = _actionTarget;
        _showPausePrompt = false;
        if (target is null)
        {
            return Task.CompletedTask;
        }

        PauseDetails details = ActionLog.PauseDetailsFor(reason);
        return MutateAsync(
            () => Api.PauseTriggerWith(Scheduler.ActiveSchedulerName!, target.Key, details),
            "Paused trigger " + target.Label + ".",
            "Trigger " + target.Label + " was not paused - it no longer exists.",
            "PauseTrigger",
            target.Label,
            DashboardActionLog.PauseNote(details));
    }

    internal Task ResumeTriggerAsync(TimelineTriggerRef target)
    {
        TriggerActionTarget action = TriggerActionTarget.From(target.Row, target.Trigger);
        _menuTarget = null;
        return MutateAsync(
            () => Api.ResumeTrigger(Scheduler.ActiveSchedulerName!, action.Key),
            "Resumed trigger " + action.Label + ".",
            "Trigger " + action.Label + " was not resumed - it no longer exists or was not paused.",
            "ResumeTrigger",
            action.Label);
    }

    internal Task ResetTriggerAsync(TimelineTriggerRef target)
    {
        TriggerActionTarget action = TriggerActionTarget.From(target.Row, target.Trigger);
        _menuTarget = null;
        return MutateAsync(
            () => Api.ResetTriggerFromErrorState(Scheduler.ActiveSchedulerName!, action.Key),
            "Reset error state for trigger " + action.Label + ".",
            "Trigger " + action.Label + " was not reset - it no longer exists or is not in the error state.",
            "ResetTriggerFromErrorState",
            action.Label);
    }

    internal Task RunNowAsync(TimelineTriggerRef target)
    {
        TriggerActionTarget action = TriggerActionTarget.From(target.Row, target.Trigger);
        _menuTarget = null;
        return MutateAsync(
            () => Api.TriggerJob(Scheduler.ActiveSchedulerName!, new JobKeyDto(action.JobGroup, action.JobName)),
            "Triggered job " + DisplayValueHelper.FormatKey(action.JobGroup, action.JobName) + ".",
            "TriggerJob",
            DisplayValueHelper.FormatKey(action.JobGroup, action.JobName));
    }

    internal void PromptUnschedule(TimelineTriggerRef target)
    {
        _menuTarget = null;
        _actionTarget = TriggerActionTarget.From(target.Row, target.Trigger);
        _showUnscheduleConfirm = true;
        StateHasChanged();
    }

    private Task CancelUnscheduleAsync()
    {
        _showUnscheduleConfirm = false;
        return Task.CompletedTask;
    }

    private async Task UnscheduleTriggerAsync()
    {
        TriggerActionTarget? target = _actionTarget;
        _showUnscheduleConfirm = false;
        if (target is null)
        {
            return;
        }

        await MutateAsync(
            () => Api.UnscheduleJob(Scheduler.ActiveSchedulerName!, target.Key),
            "Unscheduled trigger " + target.Label + ".",
            "Trigger " + target.Label + " was not unscheduled - it no longer exists.",
            "UnscheduleTrigger",
            target.Label).ConfigureAwait(true);
    }

    private async Task MutateAsync(
        Func<ValueTask<bool>> operation,
        string successMessage,
        string noOpMessage,
        string actionName,
        string target,
        string? note = null)
    {
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (Options.Value.ReadOnly || string.IsNullOrWhiteSpace(schedulerName))
        {
            return;
        }

        try
        {
            bool changed = await operation().ConfigureAwait(true);
            if (changed)
            {
                ActionLog.Record(schedulerName, actionName, target, succeeded: true, message: note);
                Toasts.Success(successMessage);
                await LoadDataAsync().ConfigureAwait(true);
            }
            else
            {
                Toasts.Error(noOpMessage);
            }
        }
        catch (Exception ex)
        {
            ActionLog.Failed(schedulerName, actionName, target, ex);
            Toasts.Error(ex.Message);
        }
        finally
        {
            StateHasChanged();
        }
    }

    private async Task MutateAsync(
        Func<ValueTask> operation,
        string successMessage,
        string actionName,
        string target)
    {
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (Options.Value.ReadOnly || string.IsNullOrWhiteSpace(schedulerName))
        {
            return;
        }

        try
        {
            await operation().ConfigureAwait(true);
            ActionLog.Record(schedulerName, actionName, target, succeeded: true);
            Toasts.Success(successMessage);
            await LoadDataAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            ActionLog.Failed(schedulerName, actionName, target, ex);
            Toasts.Error(ex.Message);
        }
        finally
        {
            StateHasChanged();
        }
    }

    private static bool SameTrigger(TriggerActionTarget target, TimelineRow row, TimelineTriggerRow trigger)
    {
        return string.Equals(target.JobGroup, row.JobGroup, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.JobName, row.JobName, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.TriggerGroup, trigger.Group, StringComparison.OrdinalIgnoreCase)
            && string.Equals(target.TriggerName, trigger.Name, StringComparison.OrdinalIgnoreCase);
    }

    private sealed record TriggerActionTarget(string JobGroup, string JobName, string TriggerGroup, string TriggerName)
    {
        public TriggerKeyDto Key => new(TriggerGroup, TriggerName);

        public string Label => DisplayValueHelper.FormatKey(TriggerGroup, TriggerName);

        public static TriggerActionTarget From(TimelineRow row, TimelineTriggerRow trigger)
        {
            return new(row.JobGroup, row.JobName, trigger.Group, trigger.Name);
        }
    }


    #endregion

    #region Geometry

    internal static readonly int[] PageSizes = [10, 25, 50, 100];

    internal const int DefaultPageSize = 25;

    /// <summary>Floor for a zero-duration run as a fraction of the track (~2px on a ~800px track).</summary>
    private const double MinBarPixelFraction = 0.0025;

    /// <summary>Visual gap between neighbouring bars in one row.</summary>
    private const double BarGapFraction = 0.002;

    internal static TimelineBounds BoundsFor(TimelineWindow window, TimeSpan panOffset, DateTimeOffset now)
    {
        DateTimeOffset end = now - panOffset;
        DateTimeOffset start = end - window.Span;
        // When live (pan == 0), leave a short look-ahead so upcoming fires are visible past "now".
        DateTimeOffset axisEnd = panOffset <= TimeSpan.Zero
            ? now + LookAhead(window)
            : end;
        return new TimelineBounds(start, end, axisEnd);
    }

    internal static void FillTicks(
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

    internal static TimelineBar CreateBar(
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

    internal static double Fraction(TimelineBounds bounds, DateTimeOffset instant)
    {
        return Clamp01((instant - bounds.Start).Ticks / bounds.SpanTicks);
    }

    /// <summary>
    /// Shortens bars that would touch or overlap the next one so neighbouring runs stay visually separate.
    /// </summary>
    internal static void ApplyBarGaps(List<TimelineBar> bars)
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
                // Keep a hairline if there is any room before the next bar; otherwise accept a tiny overlap.
                width = next.Left > current.Left ? Math.Max(next.Left - current.Left - BarGapFraction, MinBarPixelFraction * 0.5) : MinBarPixelFraction * 0.5;
                if (width <= 0)
                {
                    width = MinBarPixelFraction * 0.5;
                }
            }

            bars[i] = current with { Width = width };
        }
    }

    internal static List<TimelineBar> TakePendingBars(Dictionary<string, PendingTriggerBars> pending, string triggerKey)
    {
        if (!pending.Remove(triggerKey, out PendingTriggerBars? bucket))
        {
            return [];
        }

        FinalizeTriggerBars(bucket.Bars);
        return bucket.Bars;
    }

    internal static void FinalizeTriggerBars(List<TimelineBar> bars)
    {
        bars.Sort(static (a, b) => a.Left.CompareTo(b.Left));
        ApplyBarGaps(bars);
    }

    internal static void AddBarForTrigger(TimelineRowBuilder builder, string triggerGroup, string triggerName, TimelineBar bar)
    {
        string key = TriggerLookupKey(triggerGroup, triggerName);
        if (!builder.BarsByTrigger.TryGetValue(key, out PendingTriggerBars? bucket))
        {
            bucket = new PendingTriggerBars(triggerGroup, triggerName, []);
            builder.BarsByTrigger[key] = bucket;
        }

        bucket.Bars.Add(bar);
    }

    internal static TimelineRowBuilder GetOrAdd(Dictionary<string, TimelineRowBuilder> builders, string group, string name)
    {
        string key = group + "." + name;
        if (!builders.TryGetValue(key, out TimelineRowBuilder? builder))
        {
            builder = new TimelineRowBuilder(key, group, name);
            builders[key] = builder;
        }

        return builder;
    }

    internal static string TriggerLookupKey(string group, string name) => group + "." + name;

    internal static string Pct(double fraction) =>
        (Math.Clamp(fraction, 0, 1) * 100).ToString("0.###", CultureInfo.InvariantCulture) + "%";

    internal static TimeSpan PanStep(TimelineWindow window) => TimeSpan.FromTicks(window.Span.Ticks / 4);

    internal static TimeSpan NormalizePan(int? panMinutes)
    {
        if (panMinutes is int minutes && minutes > 0)
        {
            return TimeSpan.FromMinutes(minutes);
        }

        return TimeSpan.Zero;
    }

    internal static int NormalizePageSize(int? value)
    {
        if (value is int size && PageSizes.Contains(size))
        {
            return size;
        }

        return DefaultPageSize;
    }

    internal static int? ParseInt(string? value)
    {
        return int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed) ? parsed : null;
    }

    private static double Clamp01(double value) => Math.Clamp(value, 0, 1);

    private static TimeSpan LookAhead(TimelineWindow window)
    {
        long quarter = window.Span.Ticks / 4;
        long capped = TimeSpan.FromHours(1).Ticks;
        return TimeSpan.FromTicks(Math.Min(quarter, capped));
    }

    #endregion
}

#region View

/// <summary>The drawn axis: the window the visitor asked for, and the right edge which may look past now.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct TimelineBounds(DateTimeOffset Start, DateTimeOffset End, DateTimeOffset AxisEnd)
{
    public double SpanTicks => Math.Max(1, (AxisEnd - Start).Ticks);

    public double NowFraction => Math.Clamp((End - Start).Ticks / SpanTicks, 0, 1);

    /// <summary>
    /// CSS vars so trigger tracks paint one vertical rule per clock minute across the drawn axis.
    /// </summary>
    public string MinuteGridStyle
    {
        get
        {
            double axisMs = Math.Max(1, (AxisEnd - Start).TotalMilliseconds);
            double minutePct = TimeSpan.FromMinutes(1).TotalMilliseconds / axisMs * 100.0;

            DateTimeOffset truncated = new(
                Start.Year,
                Start.Month,
                Start.Day,
                Start.Hour,
                Start.Minute,
                0,
                Start.Offset);
            DateTimeOffset firstMinute = truncated <= Start ? truncated.AddMinutes(1) : truncated;
            double offsetPct = firstMinute >= AxisEnd
                ? 0
                : (firstMinute - Start).TotalMilliseconds / axisMs * 100.0;

            return "--qz-timeline-minute-pct: "
                   + minutePct.ToString("0.####", CultureInfo.InvariantCulture) + "%; "
                   + "--qz-timeline-minute-offset: "
                   + offsetPct.ToString("0.####", CultureInfo.InvariantCulture) + "%;";
        }
    }
}

internal sealed record TimelineWindow(string Key, string Label, string ShortLabel, TimeSpan Span, int TickCount, string TickFormat)
{
    public static readonly TimelineWindow Last10Minutes = new("10m", "Last 10 minutes", "10 min", TimeSpan.FromMinutes(10), 5, "HH:mm");
    public static readonly TimelineWindow Last15Minutes = new("15m", "Last 15 minutes", "15 min", TimeSpan.FromMinutes(15), 5, "HH:mm");
    public static readonly TimelineWindow Last30Minutes = new("30m", "Last 30 minutes", "30 min", TimeSpan.FromMinutes(30), 6, "HH:mm");
    public static readonly TimelineWindow LastHour = new("1h", "Last hour", "1 hour", TimeSpan.FromHours(1), 6, "HH:mm");
    public static readonly TimelineWindow LastSixHours = new("6h", "Last 6 hours", "6 hours", TimeSpan.FromHours(6), 6, "HH:mm");
    public static readonly TimelineWindow LastDay = new("24h", "Last 24 hours", "24 hours", TimeSpan.FromHours(24), 8, "HH:mm");
    public static readonly TimelineWindow LastWeek = new("7d", "Last 7 days", "7 days", TimeSpan.FromDays(7), 7, "MM-dd");

    public static readonly TimelineWindow[] Offered =
    [
        Last10Minutes,
        Last15Minutes,
        Last30Minutes,
        LastHour,
        LastSixHours,
        LastDay,
        LastWeek
    ];

    public static TimelineWindow Parse(string? key)
    {
        string trimmed = key?.Trim() ?? string.Empty;
        foreach (TimelineWindow window in Offered)
        {
            if (string.Equals(window.Key, trimmed, StringComparison.OrdinalIgnoreCase))
            {
                return window;
            }
        }

        return Last15Minutes;
    }
}

internal sealed record TimelineTick(double Fraction, string Label);

internal sealed record TimelineBar(double Left, double Width, string CssClass, string Title, string? EntryId);

internal sealed record TimelineMark(double Left, string Title);

internal sealed record TimelineTriggerRow(
    string Group,
    string Name,
    TriggerState? State,
    string? ScheduleSummary,
    DateTimeOffset? NextFireTimeUtc,
    DateTimeOffset? PreviousFireTimeUtc,
    DateTimeOffset? StartTimeUtc,
    DateTimeOffset? EndTimeUtc,
    bool IsExecuting,
    List<TimelineBar> Bars,
    List<TimelineMark> Marks)
{
    public string Key => Group + "." + Name;
}

internal sealed record TimelineRow(
    string Key,
    string JobGroup,
    string JobName,
    Dictionary<string, PendingTriggerBars> BarsByTrigger,
    List<TimelineTriggerRow> Triggers)
{
    public bool TriggersLoaded { get; set; }
}

internal sealed record TimelineSection(string JobGroup, List<TimelineRow> Rows);

internal sealed record PendingTriggerBars(string Group, string Name, List<TimelineBar> Bars);

internal sealed class TimelineRowBuilder(string key, string jobGroup, string jobName)
{
    public string Key { get; } = key;
    public string JobGroup { get; } = jobGroup;
    public string JobName { get; } = jobName;
    public Dictionary<string, PendingTriggerBars> BarsByTrigger { get; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One trigger on the chart, so a menu action knows which row it belongs to.</summary>
internal sealed record TimelineTriggerRef(TimelineRow Row, TimelineTriggerRow Trigger);

/// <summary>Which trigger commands the visitor may use. The row draws a menu only when <see cref="Any"/> is set.</summary>
internal sealed record TimelineTriggerMenu(bool Run, bool Pause, bool Resume, bool Reset, bool Unschedule)
{
    public bool Any => Run || Pause || Resume || Reset || Unschedule;
}
#endregion
