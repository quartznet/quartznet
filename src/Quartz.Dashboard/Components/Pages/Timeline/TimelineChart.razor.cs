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
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;

namespace Quartz.Dashboard.Components.Pages;

public partial class TimelineChart
{
    private const string CollapsedPrefsKey = "qz-timeline-collapsed";

    private readonly HashSet<string> _collapsedGroups = new(StringComparer.OrdinalIgnoreCase);

    private ElementReference _axisTrack;
    private bool _isPanning;
    private double _panPointerStartX;
    private TimeSpan _panOffsetAtPointerDown;
    private double _axisTrackWidthPx;

    [Parameter]
    public Timeline Page { get; set; } = null!;

    [Parameter]
    public int ViewRevision { get; set; }

    private bool _collapsedReady;

    protected override void OnParametersSet()
    {
        if (_collapsedReady || JsRuntime is not IJSInProcessRuntime runtime)
        {
            return;
        }

        try
        {
            ApplyCollapsedGroups(runtime.Invoke<string?>("quartzDashboardPrefs.get", CollapsedPrefsKey));
            _collapsedReady = true;
        }
        catch (JSException)
        {
            _collapsedReady = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender || _collapsedReady)
        {
            return;
        }

        try
        {
            string? raw = await JsRuntime.InvokeAsync<string?>("quartzDashboardPrefs.get", new object?[] { CollapsedPrefsKey }).ConfigureAwait(true);
            ApplyCollapsedGroups(raw);
            _collapsedReady = true;
            StateHasChanged();
        }
        catch (JSException)
        {
            _collapsedReady = true;
        }
        catch (InvalidOperationException)
        {
        }
    }

    private async Task ToggleGroupAsync(string jobGroup)
    {
        if (!_collapsedGroups.Add(jobGroup))
        {
            _collapsedGroups.Remove(jobGroup);
        }

        try
        {
            string payload = JsonSerializer.Serialize(_collapsedGroups.OrderBy(static group => group, StringComparer.OrdinalIgnoreCase).ToArray());
            await JsRuntime.InvokeVoidAsync("quartzDashboardPrefs.set", new object?[] { CollapsedPrefsKey, payload }).ConfigureAwait(true);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
    }

    private void ApplyCollapsedGroups(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            return;
        }

        try
        {
            string[]? groups = JsonSerializer.Deserialize<string[]>(raw);
            if (groups is null)
            {
                return;
            }

            _collapsedGroups.Clear();
            foreach (string group in groups)
            {
                if (!string.IsNullOrWhiteSpace(group))
                {
                    _collapsedGroups.Add(group);
                }
            }
        }
        catch (JsonException)
        {
        }
    }

    private async Task OnAxisPointerDown(PointerEventArgs args)
    {
        _isPanning = true;
        _panPointerStartX = args.ClientX;
        _panOffsetAtPointerDown = Page.PanOffset;
        _axisTrackWidthPx = await MeasureAxisWidthAsync().ConfigureAwait(true);
    }

    private async Task OnAxisPointerMove(PointerEventArgs args)
    {
        if (!_isPanning || _axisTrackWidthPx <= 1)
        {
            return;
        }

        double deltaFraction = (_panPointerStartX - args.ClientX) / _axisTrackWidthPx;
        TimeSpan delta = TimeSpan.FromTicks((long) (Page.Window.Span.Ticks * deltaFraction));
        TimeSpan next = _panOffsetAtPointerDown + delta;
        if (next < TimeSpan.Zero)
        {
            next = TimeSpan.Zero;
        }

        if (next == Page.PanOffset)
        {
            return;
        }

        Page.OnPanOffsetChanged(next);
    }

    private async Task OnAxisPointerUp(PointerEventArgs args)
    {
        if (!_isPanning)
        {
            return;
        }

        _isPanning = false;
        await Page.OnPanCommittedAsync().ConfigureAwait(true);
    }

    private async Task<double> MeasureAxisWidthAsync()
    {
        try
        {
            double width = await JsRuntime.InvokeAsync<double>("quartzDashboardDom.width", _axisTrack).ConfigureAwait(true);
            return width > 1 ? width : 600;
        }
        catch (JSException)
        {
            return 600;
        }
        catch (InvalidOperationException)
        {
            return 600;
        }
    }
}
