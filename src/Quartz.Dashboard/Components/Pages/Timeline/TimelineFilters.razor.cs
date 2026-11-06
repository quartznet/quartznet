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

using Quartz.Dashboard.Services;

namespace Quartz.Dashboard.Components.Pages;

public partial class TimelineFilters
{
    [Parameter]
    public Timeline Page { get; set; } = null!;

    [Parameter]
    public int ViewRevision { get; set; }

    private string FormatInstant(DateTimeOffset instant) => Scheduler.FormatInSelectedTimeZone(instant, "HH:mm");

    private Task OnWindowChangedAsync(ChangeEventArgs args)
    {
        return Page.NavigateAsync(
            TimelineWindow.Parse(args.Value?.ToString()),
            Page.GroupFilter,
            1,
            Page.PageSize,
            TimeSpan.Zero);
    }

    private Task OnGroupChangedAsync(ChangeEventArgs args)
    {
        return Page.NavigateAsync(Page.Window, args.Value?.ToString() ?? string.Empty, 1, Page.PageSize, Page.PanOffset);
    }

    private Task OnPageSizeChangedAsync(ChangeEventArgs args)
    {
        int size = Timeline.NormalizePageSize(Timeline.ParseInt(args.Value?.ToString()));
        return Page.NavigateAsync(Page.Window, Page.GroupFilter, 1, size, Page.PanOffset);
    }

    private Task OnNameInputAsync(ChangeEventArgs args)
    {
        return Page.SetNameFilterAsync(args.Value?.ToString() ?? string.Empty);
    }

    private Task PanOlderAsync()
    {
        return Page.NavigateAsync(
            Page.Window,
            Page.GroupFilter,
            Page.CurrentPage,
            Page.PageSize,
            Page.PanOffset + Timeline.PanStep(Page.Window));
    }

    private Task PanNewerAsync()
    {
        TimeSpan next = Page.PanOffset - Timeline.PanStep(Page.Window);
        if (next < TimeSpan.Zero)
        {
            next = TimeSpan.Zero;
        }

        return Page.NavigateAsync(Page.Window, Page.GroupFilter, Page.CurrentPage, Page.PageSize, next);
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

        return ((int) Math.Round(offset.TotalMinutes)).ToString(CultureInfo.InvariantCulture) + "m";
    }
}
