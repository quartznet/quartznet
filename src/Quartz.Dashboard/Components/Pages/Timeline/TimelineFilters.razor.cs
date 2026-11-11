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

using Microsoft.AspNetCore.Components;

using Quartz.Dashboard.Models;
using Quartz.Dashboard.Services.Timeline;

namespace Quartz.Dashboard.Components.Pages;

public partial class TimelineFilters
{
    [Parameter]
    public Timeline Page { get; set; } = null!;

    private TimelineState State => Page.State;

    private string FormatBound(DateTimeOffset instant)
    {
        string format = State.Window.TickFormat.Contains("dd", StringComparison.Ordinal)
            ? "MM-dd HH:mm"
            : "HH:mm";
        return Scheduler.FormatInSelectedTimeZone(instant, format);
    }

    private Task OnWindowChangedAsync(ChangeEventArgs args) =>
        Page.NavigateAsync(
            TimelineWindow.Parse(args.Value?.ToString()),
            State.GroupFilter,
            1,
            State.PageSize,
            TimeSpan.Zero);

    private Task OnGroupChangedAsync(ChangeEventArgs args) =>
        Page.NavigateAsync(State.Window, args.Value?.ToString() ?? string.Empty, 1, State.PageSize, State.PanOffset);

    private Task OnPageSizeChangedAsync(ChangeEventArgs args)
    {
        int size = TimelineService.NormalizePageSize(TimelineService.ParseInteger(args.Value?.ToString()));
        return Page.NavigateAsync(State.Window, State.GroupFilter, 1, size, State.PanOffset);
    }

    private Task OnJobsRefreshChangedAsync(ChangeEventArgs args) =>
        Page.SetJobsRefreshAsync(args.Value?.ToString() ?? TimelineService.DefaultJobsRefreshKey);

    private Task OnTriggersRefreshChangedAsync(ChangeEventArgs args) =>
        Page.SetTriggersRefreshAsync(args.Value?.ToString() ?? TimelineService.DefaultTriggersRefreshKey);

    private Task OnNameFilterChangedAsync(string name) =>
        Page.SetNameFilterAsync(name);

    private Task OnPanOlderClickedAsync() =>
        Page.NavigateAsync(
            State.Window,
            State.GroupFilter,
            State.CurrentPage,
            State.PageSize,
            State.PanOffset + TimelineService.CreatePanStep(State.Window));

    private Task OnPanNewerClickedAsync()
    {
        TimeSpan next = State.PanOffset - TimelineService.CreatePanStep(State.Window);
        if (next < TimeSpan.Zero)
        {
            next = TimeSpan.Zero;
        }

        return Page.NavigateAsync(State.Window, State.GroupFilter, State.CurrentPage, State.PageSize, next);
    }

    private static string FormatPanOffset(TimeSpan offset)
    {
        if (offset.TotalDays >= 1)
        {
            return offset.TotalDays.ToString("0.#", CultureInfo.InvariantCulture) + "d";
        }

        if (offset.TotalHours >= 1)
        {
            return offset.TotalHours.ToString("0.#", CultureInfo.InvariantCulture) + "h";
        }

        if (offset.TotalMinutes >= 1 && offset.Seconds == 0 && offset.Milliseconds == 0)
        {
            return ((int) Math.Round(offset.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "m";
        }

        if (offset.TotalMinutes >= 1)
        {
            return offset.TotalMinutes.ToString("0.#", CultureInfo.InvariantCulture) + "m";
        }

        return ((int) Math.Round(offset.TotalSeconds)).ToString(CultureInfo.InvariantCulture) + "s";
    }
}
