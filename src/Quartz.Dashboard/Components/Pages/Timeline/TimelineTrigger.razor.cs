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

using Microsoft.AspNetCore.Components;

using Quartz;
using Quartz.Dashboard.Services;

namespace Quartz.Dashboard.Components.Pages;

public partial class TimelineTrigger
{
    private TimelineRow? _row;
    private TimelineTriggerRow? _trigger;

    [Parameter]
    public Timeline Page { get; set; } = null!;

    [Parameter]
    public int ViewRevision { get; set; }

    [Parameter]
    public string JobGroup { get; set; } = string.Empty;

    [Parameter]
    public string JobName { get; set; } = string.Empty;

    [Parameter]
    public string TriggerGroup { get; set; } = string.Empty;

    [Parameter]
    public string TriggerName { get; set; } = string.Empty;

    protected override void OnParametersSet()
    {
        if (!Page.TryFindTrigger(JobGroup, JobName, TriggerGroup, TriggerName, out _row, out _trigger)
            || _trigger is null)
        {
            return;
        }
    }

    private string Schedule => _trigger is null || string.IsNullOrWhiteSpace(_trigger.ScheduleSummary)
        ? "—"
        : _trigger.ScheduleSummary;

    private string TintClass
    {
        get
        {
            if (_trigger is null)
            {
                return string.Empty;
            }

            if (_trigger.IsExecuting)
            {
                return "qz-timeline-row-state-executing";
            }

            return _trigger.State switch
            {
                TriggerState.Paused => "qz-timeline-row-state-paused",
                TriggerState.Error => "qz-timeline-row-state-error",
                TriggerState.Blocked => "qz-timeline-row-state-blocked",
                TriggerState.Complete => "qz-timeline-row-state-complete",
                TriggerState.Awaiting => "qz-timeline-row-state-awaiting",
                TriggerState.Executing => "qz-timeline-row-state-executing",
                _ => string.Empty
            };
        }
    }

    private string FormatOptionalInstant(DateTimeOffset? instant) =>
        instant is { } value ? Scheduler.FormatInSelectedTimeZone(value, "MMM d HH:mm") : "—";

    private TimelineTriggerRef? Target => _row is null || _trigger is null ? null : new TimelineTriggerRef(_row, _trigger);

    private TimelineTriggerMenu MenuFor(TimelineTriggerRow trigger)
    {
        return new TimelineTriggerMenu(
            Can(Quartz.HttpApiContract.SchedulerRoutes.TriggerJob),
            trigger.State != TriggerState.Paused && Can(Quartz.HttpApiContract.SchedulerRoutes.PauseTrigger),
            trigger.State == TriggerState.Paused && Can(Quartz.HttpApiContract.SchedulerRoutes.ResumeTrigger),
            trigger.State == TriggerState.Error && Can(Quartz.HttpApiContract.SchedulerRoutes.ResetTriggerFromErrorState),
            Can(Quartz.HttpApiContract.SchedulerRoutes.UnscheduleJob));
    }

    private bool Can(Quartz.HttpApiContract.WireRoute route) => Authorization.Allows(Scheduler.ActiveSchedulerName, route);

    private void ToggleMenu()
    {
        if (Target is { } target)
        {
            Page.ToggleTriggerMenu(target);
        }
    }

    private Task Run() => Target is { } target ? Page.RunNowAsync(target) : Task.CompletedTask;

    private Task Resume() => Target is { } target ? Page.ResumeTriggerAsync(target) : Task.CompletedTask;

    private void Pause()
    {
        if (Target is { } target)
        {
            Page.PromptPause(target);
        }
    }

    private Task Reset() => Target is { } target ? Page.ResetTriggerAsync(target) : Task.CompletedTask;

    private void Unschedule()
    {
        if (Target is { } target)
        {
            Page.PromptUnschedule(target);
        }
    }
}
