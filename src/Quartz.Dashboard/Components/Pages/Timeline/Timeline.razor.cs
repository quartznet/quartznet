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

using System.Text.Json;

using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;

using Quartz.Dashboard.Components.Shared;
using Quartz.Dashboard.Models;
using Quartz.Dashboard.Services;
using Quartz.Dashboard.Services.Timeline;

namespace Quartz.Dashboard.Components.Pages;

public partial class Timeline
{
    private const string JobsRefreshStorageKey = "qz-timeline-jobs-refresh";
    private const string TriggersRefreshStorageKey = "qz-timeline-triggers-refresh";

    internal const string CollapsedGroupsStorageKey = "qz-timeline-collapsed";
    internal const string CollapsedJobsStorageKey = "qz-timeline-collapsed-jobs";

    private TriggerActionTarget? _menuTarget;
    private TimelineJobRef? _jobMenuTarget;
    private TriggerActionTarget? _actionTarget;
    private TimelineJobRef? _jobActionTarget;
    private bool _showPausePrompt;
    private bool _pauseJob;
    private bool _showUnscheduleConfirm;
    private bool _showDeleteJobConfirm;
    private PeriodicTimer? _refreshTimer;
    private CancellationTokenSource? _refreshLoopCancellation;
    private string _jobsRefreshKey = TimelineService.DefaultJobsRefreshKey;
    private string _triggersRefreshKey = TimelineService.DefaultTriggersRefreshKey;
    private TimeSpan? _jobsRefreshInterval = TimeSpan.FromSeconds(30);
    private TimeSpan? _triggersRefreshInterval = TimeSpan.FromSeconds(5);
    private TimeSpan _elapsedSinceJobsRefresh = TimeSpan.FromHours(24);
    private TimeSpan _elapsedSinceTriggersRefresh = TimeSpan.FromHours(24);
    private bool _refreshPrefsReady;
    private bool _collapsedPrefsReady;
    private TimelineState? _state;
    private TimelineActions? _actions;

    [Inject]
    private IServiceProvider Services { get; set; } = null!;

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

    private TimeProvider Clock => Services.GetService<TimeProvider>() ?? TimeProvider.System;

    internal TimelineState State => _state ??= new TimelineState(new TimelineService(Api), Clock);

    private TimelineActions Actions => _actions ??= new TimelineActions(
        Api,
        Toasts,
        ActionLog,
        Options.Value,
        LoadTimelineAsync);

    internal string JobsRefreshKey => _jobsRefreshKey;

    internal string TriggersRefreshKey => _triggersRefreshKey;

    internal string TriggersLiveBadge => _triggersRefreshKey == "Off"
        ? "Live · Off"
        : "Live · " + _triggersRefreshKey.Replace("min", " min");

    protected override void OnInitialized()
    {
        Scheduler.OnSchedulerChanged += OnSchedulerChanged;
        _refreshLoopCancellation = new CancellationTokenSource();
        _refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(1), Clock);
        _ = RunRefreshLoopAsync(_refreshLoopCancellation.Token);
    }

    protected override async Task OnParametersSetAsync()
    {
        State.ApplyQuery(WindowQuery, GroupQuery, PageQuery, PageSizeQuery, PanQuery);
        await EnsureCollapsedPreferencesLoadedAsync().ConfigureAwait(true);
        await LoadTimelineAsync().ConfigureAwait(true);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && !_refreshPrefsReady)
        {
            await LoadRefreshPreferencesAsync().ConfigureAwait(true);
        }

        if (firstRender && !_collapsedPrefsReady)
        {
            await EnsureCollapsedPreferencesLoadedAsync().ConfigureAwait(true);
            StateHasChanged();
        }

        if (!State.PendingUrlSync)
        {
            return;
        }

        State.PendingUrlSync = false;
        string path = BuildTimelinePath(State.Window, State.GroupFilter, State.CurrentPage, State.PageSize, State.PanOffset);
        string current = (DashboardLink.ToBaseRelative(Navigation.Uri, Navigation.BaseUri) ?? string.Empty).TrimStart('/');
        if (!PathsEqual(path.TrimStart('/'), current))
        {
            Navigation.NavigateTo(path, replace: true);
        }
    }

    public void Dispose()
    {
        Scheduler.OnSchedulerChanged -= OnSchedulerChanged;
        _refreshLoopCancellation?.Cancel();
        _refreshLoopCancellation?.Dispose();
        _refreshTimer?.Dispose();
        GC.SuppressFinalize(this);
    }

    internal async Task OnGroupCollapsedChangedAsync(string jobGroup, bool collapsed)
    {
        await State.SetGroupCollapsedAsync(
            jobGroup,
            collapsed,
            Scheduler.ActiveSchedulerName,
            FormatInstant,
            FormatTick).ConfigureAwait(true);
        _collapsedPrefsReady = true;
        await PersistCollapsedPreferencesAsync().ConfigureAwait(true);
        StateHasChanged();
    }

    internal async Task OnJobCollapsedChangedAsync(TimelineEntityKey jobKey, bool collapsed)
    {
        await State.SetJobCollapsedAsync(
            jobKey,
            collapsed,
            Scheduler.ActiveSchedulerName,
            FormatInstant,
            FormatTick).ConfigureAwait(true);
        _collapsedPrefsReady = true;
        await PersistCollapsedPreferencesAsync().ConfigureAwait(true);
        StateHasChanged();
    }

    internal async Task SetJobsRefreshAsync(string key)
    {
        TimeSpan? interval = TimelineService.ParseJobsRefreshInterval(key);
        if (interval is null && key is not ("Off" or "off"))
        {
            key = TimelineService.DefaultJobsRefreshKey;
            interval = TimelineService.ParseJobsRefreshInterval(key);
        }

        _jobsRefreshKey = string.Equals(key, "off", StringComparison.OrdinalIgnoreCase) ? "Off" : key;
        _jobsRefreshInterval = interval;
        try
        {
            await JsRuntime.InvokeVoidAsync("quartzDashboardPrefs.set", new object?[] { JobsRefreshStorageKey, key }).ConfigureAwait(true);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        if (_jobsRefreshInterval is not null)
        {
            await LoadTimelineAsync().ConfigureAwait(true);
        }
        else
        {
            _elapsedSinceJobsRefresh = TimeSpan.Zero;
        }

        StateHasChanged();
    }

    internal async Task SetTriggersRefreshAsync(string key)
    {
        // Coerce old "1s" floor to the new minimum "5s".
        if (string.Equals(key.Trim(), "1s", StringComparison.OrdinalIgnoreCase))
        {
            key = TimelineService.DefaultTriggersRefreshKey;
        }

        TimeSpan? interval = TimelineService.ParseTriggersRefreshInterval(key);
        if (interval is null && key is not ("Off" or "off"))
        {
            key = TimelineService.DefaultTriggersRefreshKey;
            interval = TimelineService.ParseTriggersRefreshInterval(key);
        }

        _triggersRefreshKey = string.Equals(key, "off", StringComparison.OrdinalIgnoreCase) ? "Off" : key;
        _triggersRefreshInterval = interval;
        try
        {
            await JsRuntime.InvokeVoidAsync("quartzDashboardPrefs.set", new object?[] { TriggersRefreshStorageKey, key }).ConfigureAwait(true);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }

        if (_triggersRefreshInterval is not null)
        {
            await RefreshTriggersAsync().ConfigureAwait(true);
        }
        else
        {
            _elapsedSinceTriggersRefresh = TimeSpan.Zero;
        }

        StateHasChanged();
    }

    internal async Task SetNameFilterAsync(string name)
    {
        await State.SetNameFilterAsync(name, Scheduler.ActiveSchedulerName, FormatInstant, FormatTick).ConfigureAwait(true);
        StateHasChanged();
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
            State.ApplyNavigation(window, groupFilter, page, pageSize, panOffset);
            await LoadTimelineAsync().ConfigureAwait(true);
            StateHasChanged();
            return;
        }

        Navigation.NavigateTo(path, replace: true);
    }

    internal string ExecutionPath(string entryId) =>
        WithTimelineOrigin(DashboardLink.To(Options.Value, "history/" + Uri.EscapeDataString(entryId)));

    internal string JobPath(string group, string name) =>
        WithTimelineOrigin(DashboardLink.To(Options.Value, "jobs/" + Uri.EscapeDataString(group) + "/" + Uri.EscapeDataString(name)));

    internal string TriggerPath(string group, string name) =>
        WithTimelineOrigin(DashboardLink.To(Options.Value, "triggers/" + Uri.EscapeDataString(group) + "/" + Uri.EscapeDataString(name)));

    internal bool IsMenuOpen(TimelineRow row, TimelineTriggerRow trigger) =>
        _menuTarget is not null && SameTrigger(_menuTarget, row, trigger);

    internal bool IsJobMenuOpen(TimelineRow row) =>
        _jobMenuTarget is not null
        && string.Equals(_jobMenuTarget.Row.JobGroup, row.JobGroup, StringComparison.Ordinal)
        && string.Equals(_jobMenuTarget.Row.JobName, row.JobName, StringComparison.Ordinal);

    internal void ToggleTriggerMenu(TimelineTriggerRef target)
    {
        _jobMenuTarget = null;
        _menuTarget = IsMenuOpen(target.Row, target.Trigger) ? null : TriggerActionTarget.From(target.Row, target.Trigger);
        StateHasChanged();
    }

    internal void ToggleJobMenu(TimelineJobRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = IsJobMenuOpen(target.Row) ? null : target;
        StateHasChanged();
    }

    internal void PromptPause(TimelineTriggerRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        _actionTarget = TriggerActionTarget.From(target.Row, target.Trigger);
        _jobActionTarget = null;
        _pauseJob = false;
        _showPausePrompt = true;
        StateHasChanged();
    }

    internal void PromptPauseJob(TimelineJobRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        _actionTarget = null;
        _jobActionTarget = target;
        _pauseJob = true;
        _showPausePrompt = true;
        StateHasChanged();
    }

    internal async Task ResumeTriggerAsync(TimelineTriggerRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            await Actions.ResumeTriggerAsync(schedulerName, target.Trigger.Group, target.Trigger.Name).ConfigureAwait(true);
        }

        StateHasChanged();
    }

    internal async Task ResumeJobAsync(TimelineJobRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            await Actions.ResumeJobAsync(schedulerName, target.Row.JobGroup, target.Row.JobName).ConfigureAwait(true);
        }

        StateHasChanged();
    }

    internal async Task ResetTriggerAsync(TimelineTriggerRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            await Actions.ResetTriggerAsync(schedulerName, target.Trigger.Group, target.Trigger.Name).ConfigureAwait(true);
        }

        StateHasChanged();
    }

    internal async Task RunNowAsync(TimelineTriggerRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            await Actions.RunNowAsync(schedulerName, target.Row.JobGroup, target.Row.JobName).ConfigureAwait(true);
            // TriggerJob can return before the fire is listed; a second triggers pass catches it without
            // waiting for the interval.
            await RefreshTriggersAsync().ConfigureAwait(true);
        }

        StateHasChanged();
    }

    internal async Task RunJobNowAsync(TimelineJobRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            await Actions.RunNowAsync(schedulerName, target.Row.JobGroup, target.Row.JobName).ConfigureAwait(true);
            await RefreshTriggersAsync().ConfigureAwait(true);
        }

        StateHasChanged();
    }

    internal void PromptUnschedule(TimelineTriggerRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        _actionTarget = TriggerActionTarget.From(target.Row, target.Trigger);
        _showUnscheduleConfirm = true;
        StateHasChanged();
    }

    internal void PromptDeleteJob(TimelineJobRef target)
    {
        _menuTarget = null;
        _jobMenuTarget = null;
        _jobActionTarget = target;
        _showDeleteJobConfirm = true;
        StateHasChanged();
    }

    private async void OnSchedulerChanged(object? sender, EventArgs e)
    {
        // Drop A's rows and menus before LoadOperations / the new load await: ActiveSchedulerName
        // is already B, so a Run now (or tick) in that gap must not still see A's keys.
        _menuTarget = null;
        _jobMenuTarget = null;
        _actionTarget = null;
        _jobActionTarget = null;
        _showPausePrompt = false;
        _showUnscheduleConfirm = false;
        _showDeleteJobConfirm = false;
        State.ResetPage();
        State.ClearRows();
        await LoadTimelineAsync().ConfigureAwait(true);
        await InvokeAsync(StateHasChanged).ConfigureAwait(true);
    }

    private Task OnPageChangedAsync(int page) =>
        NavigateAsync(State.Window, State.GroupFilter, page, State.PageSize, State.PanOffset);

    private void OnMenuBackdropClicked()
    {
        _menuTarget = null;
        _jobMenuTarget = null;
    }

    private Task CancelPauseAsync()
    {
        _showPausePrompt = false;
        return Task.CompletedTask;
    }

    private Task CancelUnscheduleAsync()
    {
        _showUnscheduleConfirm = false;
        return Task.CompletedTask;
    }

    private Task CancelDeleteJobAsync()
    {
        _showDeleteJobConfirm = false;
        return Task.CompletedTask;
    }

    private async Task PauseConfirmedAsync(string? reason)
    {
        bool pauseJob = _pauseJob;
        TriggerActionTarget? triggerTarget = _actionTarget;
        TimelineJobRef? jobTarget = _jobActionTarget;
        _showPausePrompt = false;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            PauseDetails details = ActionLog.PauseDetailsFor(reason);
            if (pauseJob && jobTarget is not null)
            {
                await Actions.PauseJobAsync(schedulerName, jobTarget.Row.JobGroup, jobTarget.Row.JobName, details)
                    .ConfigureAwait(true);
            }
            else if (triggerTarget is not null)
            {
                await Actions.PauseTriggerAsync(schedulerName, triggerTarget.TriggerGroup, triggerTarget.TriggerName, details)
                    .ConfigureAwait(true);
            }
        }

        StateHasChanged();
    }

    private async Task UnscheduleTriggerAsync()
    {
        TriggerActionTarget? target = _actionTarget;
        _showUnscheduleConfirm = false;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (target is not null && !string.IsNullOrWhiteSpace(schedulerName))
        {
            await Actions.UnscheduleTriggerAsync(schedulerName, target.TriggerGroup, target.TriggerName).ConfigureAwait(true);
        }

        StateHasChanged();
    }

    private async Task DeleteJobAsync()
    {
        TimelineJobRef? target = _jobActionTarget;
        _showDeleteJobConfirm = false;
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (target is not null && !string.IsNullOrWhiteSpace(schedulerName))
        {
            await Actions.DeleteJobAsync(schedulerName, target.Row.JobGroup, target.Row.JobName).ConfigureAwait(true);
        }

        StateHasChanged();
    }

    private string PauseDialogTitle => _pauseJob ? "Pause job" : "Pause trigger";

    private string PauseDialogMessage
    {
        get
        {
            if (_pauseJob && _jobActionTarget is { } job)
            {
                return $"Pause {DisplayValueHelper.FormatKey(job.Row.JobGroup, job.Row.JobName)}?";
            }

            if (_actionTarget is { } trigger)
            {
                return $"Pause {DisplayValueHelper.FormatKey(trigger.TriggerGroup, trigger.TriggerName)}?";
            }

            return string.Empty;
        }
    }

    private string DeleteJobDialogMessage =>
        _jobActionTarget is { } job
            ? $"Delete {DisplayValueHelper.FormatKey(job.Row.JobGroup, job.Row.JobName)}? This removes the job and its triggers and cannot be undone."
            : string.Empty;

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
                        _elapsedSinceJobsRefresh = AdvanceElapsed(_elapsedSinceJobsRefresh);
                        _elapsedSinceTriggersRefresh = AdvanceElapsed(_elapsedSinceTriggersRefresh);

                        bool jobsDue = _jobsRefreshInterval is TimeSpan jobsInterval
                            && _elapsedSinceJobsRefresh >= jobsInterval;
                        bool triggersDue = _triggersRefreshInterval is TimeSpan triggersInterval
                            && _elapsedSinceTriggersRefresh >= triggersInterval;

                        if (jobsDue)
                        {
                            await LoadTimelineAsync().ConfigureAwait(true);
                            StateHasChanged();
                        }
                        else if (triggersDue)
                        {
                            await RefreshTriggersAsync().ConfigureAwait(true);
                            StateHasChanged();
                        }
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

    private async Task LoadRefreshPreferencesAsync()
    {
        try
        {
            string? rawJobs = await JsRuntime.InvokeAsync<string?>("quartzDashboardPrefs.get", new object?[] { JobsRefreshStorageKey }).ConfigureAwait(true);
            string? rawTriggers = await JsRuntime.InvokeAsync<string?>("quartzDashboardPrefs.get", new object?[] { TriggersRefreshStorageKey }).ConfigureAwait(true);
            ApplyRefreshPreferences(rawJobs, rawTriggers);
            _refreshPrefsReady = true;
            StateHasChanged();
        }
        catch (JSException)
        {
            _refreshPrefsReady = true;
        }
        catch (InvalidOperationException)
        {
            _refreshPrefsReady = true;
        }
    }

    private async Task EnsureCollapsedPreferencesLoadedAsync()
    {
        if (_collapsedPrefsReady)
        {
            return;
        }

        if (JsRuntime is IJSInProcessRuntime runtime)
        {
            try
            {
                string? rawGroups = runtime.Invoke<string?>("quartzDashboardPrefs.get", CollapsedGroupsStorageKey);
                string? rawJobs = runtime.Invoke<string?>("quartzDashboardPrefs.get", CollapsedJobsStorageKey);
                State.ApplyCollapsedPreferences(rawGroups, rawJobs);
                _collapsedPrefsReady = true;
                return;
            }
            catch (JSException)
            {
                _collapsedPrefsReady = true;
                return;
            }
            catch (InvalidOperationException)
            {
            }
        }

        try
        {
            string? rawGroups = await JsRuntime.InvokeAsync<string?>("quartzDashboardPrefs.get", new object?[] { CollapsedGroupsStorageKey }).ConfigureAwait(true);
            string? rawJobs = await JsRuntime.InvokeAsync<string?>("quartzDashboardPrefs.get", new object?[] { CollapsedJobsStorageKey }).ConfigureAwait(true);
            State.ApplyCollapsedPreferences(rawGroups, rawJobs);
            _collapsedPrefsReady = true;
        }
        catch (JSException)
        {
            _collapsedPrefsReady = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task PersistCollapsedPreferencesAsync()
    {
        try
        {
            string groupsPayload = JsonSerializer.Serialize(
                State.CollapsedGroups.OrderBy(static group => group, StringComparer.OrdinalIgnoreCase).ToArray());
            string jobsPayload = TimelineState.FormatCollapsedJobs(State.CollapsedJobs);
            await JsRuntime.InvokeVoidAsync("quartzDashboardPrefs.set", new object?[] { CollapsedGroupsStorageKey, groupsPayload }).ConfigureAwait(true);
            await JsRuntime.InvokeVoidAsync("quartzDashboardPrefs.set", new object?[] { CollapsedJobsStorageKey, jobsPayload }).ConfigureAwait(true);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ApplyRefreshPreferences(string? rawJobs, string? rawTriggers)
    {
        if (!string.IsNullOrWhiteSpace(rawJobs) && TimelineService.ParseJobsRefreshInterval(rawJobs) is { } jobsInterval)
        {
            _jobsRefreshKey = rawJobs.Trim();
            _jobsRefreshInterval = jobsInterval == TimeSpan.Zero ? null : jobsInterval;
        }
        else if (rawJobs is "Off" or "off")
        {
            _jobsRefreshKey = "Off";
            _jobsRefreshInterval = null;
        }
        else if (!string.IsNullOrWhiteSpace(rawJobs))
        {
            _jobsRefreshKey = TimelineService.DefaultJobsRefreshKey;
            _jobsRefreshInterval = TimelineService.ParseJobsRefreshInterval(TimelineService.DefaultJobsRefreshKey);
        }

        if (!string.IsNullOrWhiteSpace(rawTriggers) && TimelineService.ParseTriggersRefreshInterval(rawTriggers) is { } triggersInterval)
        {
            // Coerce old "1s" stored key to the new minimum floor "5s".
            string trimmedTriggers = rawTriggers.Trim();
            _triggersRefreshKey = string.Equals(trimmedTriggers, "1s", StringComparison.OrdinalIgnoreCase)
                ? TimelineService.DefaultTriggersRefreshKey
                : trimmedTriggers;
            _triggersRefreshInterval = triggersInterval == TimeSpan.Zero ? null : triggersInterval;
        }
        else if (rawTriggers is "Off" or "off")
        {
            _triggersRefreshKey = "Off";
            _triggersRefreshInterval = null;
        }
        else if (!string.IsNullOrWhiteSpace(rawTriggers))
        {
            _triggersRefreshKey = TimelineService.DefaultTriggersRefreshKey;
            _triggersRefreshInterval = TimelineService.ParseTriggersRefreshInterval(TimelineService.DefaultTriggersRefreshKey);
        }
    }

    private async Task LoadTimelineAsync()
    {
        string? schedulerName = Scheduler.ActiveSchedulerName;
        if (!string.IsNullOrWhiteSpace(schedulerName))
        {
            await Authorization.LoadOperations(schedulerName).ConfigureAwait(true);
        }

        await State.LoadTimelineAsync(schedulerName, FormatInstant, FormatTick).ConfigureAwait(true);
        _elapsedSinceJobsRefresh = TimeSpan.Zero;
        _elapsedSinceTriggersRefresh = TimeSpan.Zero;
    }

    private async Task RefreshTriggersAsync()
    {
        await State.RefreshTriggersAsync(Scheduler.ActiveSchedulerName, FormatInstant, FormatTick).ConfigureAwait(true);
        _elapsedSinceTriggersRefresh = TimeSpan.Zero;
    }

    private string FormatInstant(DateTimeOffset instant) => Scheduler.FormatInSelectedTimeZone(instant, "HH:mm");

    private string FormatTick(DateTimeOffset instant, string format) => Scheduler.FormatInSelectedTimeZone(instant, format);

    private string BuildTimelinePath(TimelineWindow window, string groupFilter, int page, int pageSize, TimeSpan panOffset)
    {
        int panSeconds = (int) Math.Round(panOffset.TotalSeconds);
        string? group = string.IsNullOrWhiteSpace(groupFilter) ? null : groupFilter.Trim();
        return DashboardLink.Timeline(
            Options.Value,
            window.Key,
            group,
            page > 1 ? page : null,
            pageSize == TimelineService.DefaultPageSize ? null : pageSize,
            panSeconds > 0 ? panSeconds : null);
    }

    private string WithTimelineOrigin(string path)
    {
        string timeline = BuildTimelinePath(State.Window, State.GroupFilter, State.CurrentPage, State.PageSize, State.PanOffset);
        int queryIndex = timeline.IndexOf('?');
        string timelineQuery = queryIndex >= 0 ? timeline[(queryIndex + 1)..] : string.Empty;
        string origin = path + "?from=timeline";
        return timelineQuery.Length == 0 ? origin : origin + "&" + timelineQuery;
    }

    private static TimeSpan AdvanceElapsed(TimeSpan elapsed)
    {
        if (elapsed >= TimeSpan.FromDays(1))
        {
            return TimeSpan.FromDays(1);
        }

        return elapsed + TimeSpan.FromSeconds(1);
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

    private static bool SameTrigger(TriggerActionTarget target, TimelineRow row, TimelineTriggerRow trigger) =>
        string.Equals(target.JobGroup, row.JobGroup, StringComparison.Ordinal)
        && string.Equals(target.JobName, row.JobName, StringComparison.Ordinal)
        && string.Equals(target.TriggerGroup, trigger.Group, StringComparison.Ordinal)
        && string.Equals(target.TriggerName, trigger.Name, StringComparison.Ordinal);

    private sealed record TriggerActionTarget(string JobGroup, string JobName, string TriggerGroup, string TriggerName)
    {
        public static TriggerActionTarget From(TimelineRow row, TimelineTriggerRow trigger) =>
            new(row.JobGroup, row.JobName, trigger.Group, trigger.Name);
    }
}
