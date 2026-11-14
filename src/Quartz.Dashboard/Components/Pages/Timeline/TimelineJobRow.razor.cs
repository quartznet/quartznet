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

public partial class TimelineJobRow
{
    [Parameter]
    public Timeline Page { get; set; } = null!;

    [CascadingParameter]
    private TimelineRow? Row { get; set; }

    private TimelineState State => Page.State;

    private TimelineJobRef? Target => Row is null ? null : new TimelineJobRef(Row);

    private TimelineJobMenu MenuFor(TimelineRow row) =>
        TimelineJobMenu.For(
            row.Triggers,
            Can(Quartz.HttpApiContract.SchedulerRoutes.TriggerJob),
            Can(Quartz.HttpApiContract.SchedulerRoutes.PauseJob),
            Can(Quartz.HttpApiContract.SchedulerRoutes.ResumeJob),
            Can(Quartz.HttpApiContract.SchedulerRoutes.DeleteJob),
            row.JobMissing);

    private bool Can(Quartz.HttpApiContract.WireRoute route) => Authorization.Allows(Scheduler.ActiveSchedulerName, route);

    private Task OnJobToggledAsync() =>
        Row is null
            ? Task.CompletedTask
            : Page.OnJobCollapsedChangedAsync(Row.Key, !State.IsJobCollapsed(Row.Key));

    private void OnMenuToggled()
    {
        if (Target is { } target)
        {
            Page.ToggleJobMenu(target);
        }
    }

    private Task OnRunClickedAsync() => Target is { } target ? Page.RunJobNowAsync(target) : Task.CompletedTask;

    private Task OnResumeClickedAsync() => Target is { } target ? Page.ResumeJobAsync(target) : Task.CompletedTask;

    private void OnPauseClicked()
    {
        if (Target is { } target)
        {
            Page.PromptPauseJob(target);
        }
    }

    private void OnDeleteClicked()
    {
        if (Target is { } target)
        {
            Page.PromptDeleteJob(target);
        }
    }
}
