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
using Microsoft.JSInterop;

using Quartz.Dashboard.Models;
using Quartz.Dashboard.Services.Timeline;

namespace Quartz.Dashboard.Components.Pages;

public partial class TimelineChart : IAsyncDisposable
{
    private ElementReference _timelineRoot;
    private ElementReference _axisTrack;
    private DotNetObjectReference<TimelineChart>? _dotNetRef;
    private bool _axisPanAttached;
    private TimeSpan _lastWindowSpan;
    private TimeSpan _lastPanOffset;
    private TimeSpan _lastMaxPan;

    [Parameter]
    public Timeline Page { get; set; } = null!;

    private TimelineState State => Page.State;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        TimeSpan windowSpan = State.Window.Span;
        TimeSpan panOffset = State.PanOffset;
        TimeSpan maxPan = TimelineWindow.LastWeek.Span;
        object jsOptions = new
        {
            windowSpanSeconds = windowSpan.TotalSeconds,
            panSeconds = panOffset.TotalSeconds,
            maxPanSeconds = maxPan.TotalSeconds
        };

        try
        {
            if (!_axisPanAttached)
            {
                _dotNetRef ??= DotNetObjectReference.Create(this);
                await JsRuntime.InvokeVoidAsync(
                    "quartzDashboardDom.attachTimelineAxisPan",
                    _axisTrack,
                    _timelineRoot,
                    _dotNetRef,
                    jsOptions).ConfigureAwait(true);
                _axisPanAttached = true;
                RememberAxisPanOptions(windowSpan, panOffset, maxPan);
                return;
            }

            if (windowSpan == _lastWindowSpan
                && panOffset == _lastPanOffset
                && maxPan == _lastMaxPan)
            {
                return;
            }

            await JsRuntime.InvokeVoidAsync(
                "quartzDashboardDom.updateTimelineAxisPan",
                _axisTrack,
                jsOptions).ConfigureAwait(true);
            RememberAxisPanOptions(windowSpan, panOffset, maxPan);
        }
        catch (JSException)
        {
        }
        catch (InvalidOperationException)
        {
        }
        catch (JSDisconnectedException)
        {
        }
    }

    /// <summary>
    /// Called from JS once when the visitor finishes dragging the axis (not on every pointermove).
    /// </summary>
    [JSInvokable]
    public Task OnAxisPanCommitted(double panSeconds)
    {
        TimeSpan offset = TimelineService.ClampPanOffset(TimeSpan.FromSeconds(Math.Max(0, panSeconds)));
        return Page.NavigateAsync(State.Window, State.GroupFilter, State.CurrentPage, State.PageSize, offset);
    }

    public async ValueTask DisposeAsync()
    {
        if (_axisPanAttached)
        {
            try
            {
                await JsRuntime.InvokeVoidAsync("quartzDashboardDom.detachTimelineAxisPan", _axisTrack)
                    .ConfigureAwait(true);
            }
            catch (JSException)
            {
            }
            catch (InvalidOperationException)
            {
            }
            catch (JSDisconnectedException)
            {
            }

            _axisPanAttached = false;
        }

        _dotNetRef?.Dispose();
        _dotNetRef = null;
        GC.SuppressFinalize(this);
    }

    private void RememberAxisPanOptions(TimeSpan windowSpan, TimeSpan panOffset, TimeSpan maxPan)
    {
        _lastWindowSpan = windowSpan;
        _lastPanOffset = panOffset;
        _lastMaxPan = maxPan;
    }
}
