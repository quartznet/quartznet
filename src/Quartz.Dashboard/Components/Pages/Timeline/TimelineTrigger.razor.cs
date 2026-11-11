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

using Quartz.Dashboard.Models;
using Quartz.Dashboard.Services.Timeline;

namespace Quartz.Dashboard.Components.Pages;

public partial class TimelineTrigger
{
    [Parameter]
    public Timeline Page { get; set; } = null!;

    [CascadingParameter]
    private TimelineRow? Row { get; set; }

    [CascadingParameter]
    private TimelineTriggerRow? Trigger { get; set; }

    private TimelineState State => Page.State;

    private string Schedule => Trigger is null || string.IsNullOrWhiteSpace(Trigger.ScheduleSummary)
        ? "—"
        : Trigger.ScheduleSummary;

    private string TintClass
    {
        get
        {
            if (Trigger is null)
            {
                return string.Empty;
            }

            if (Trigger.IsExecuting)
            {
                return "qz-timeline-row-state-executing";
            }

            return Trigger.State switch
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

    private TimelineTriggerRef? Target => Row is null || Trigger is null ? null : new TimelineTriggerRef(Row, Trigger);

    private TimelineTriggerMenu MenuFor(TimelineTriggerRow trigger)
    {
        // Orphan history bars (no live header) keep Resume/Reset only when state says so; mutating
        // Pause / Unschedule / Run now needs a real trigger.
        bool exists = trigger.State is not null;
        return new TimelineTriggerMenu(
            exists && Can(Quartz.HttpApiContract.SchedulerRoutes.TriggerJob),
            exists && trigger.State != TriggerState.Paused && Can(Quartz.HttpApiContract.SchedulerRoutes.PauseTrigger),
            trigger.State == TriggerState.Paused && Can(Quartz.HttpApiContract.SchedulerRoutes.ResumeTrigger),
            trigger.State == TriggerState.Error && Can(Quartz.HttpApiContract.SchedulerRoutes.ResetTriggerFromErrorState),
            exists && Can(Quartz.HttpApiContract.SchedulerRoutes.UnscheduleJob));
    }

    private bool Can(Quartz.HttpApiContract.WireRoute route) => Authorization.Allows(Scheduler.ActiveSchedulerName, route);

    private void OnMenuToggled()
    {
        if (Target is { } target)
        {
            Page.ToggleTriggerMenu(target);
        }
    }

    private Task OnRunClickedAsync() => Target is { } target ? Page.RunNowAsync(target) : Task.CompletedTask;

    private Task OnResumeClickedAsync() => Target is { } target ? Page.ResumeTriggerAsync(target) : Task.CompletedTask;

    private void OnPauseClicked()
    {
        if (Target is { } target)
        {
            Page.PromptPause(target);
        }
    }

    private Task OnResetClickedAsync() => Target is { } target ? Page.ResetTriggerAsync(target) : Task.CompletedTask;

    private void OnUnscheduleClicked()
    {
        if (Target is { } target)
        {
            Page.PromptUnschedule(target);
        }
    }
}
