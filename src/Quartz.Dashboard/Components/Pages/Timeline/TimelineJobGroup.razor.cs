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

public partial class TimelineJobGroup
{
    [Parameter]
    public Timeline Page { get; set; } = null!;

    [CascadingParameter]
    private TimelineSection? Section { get; set; }

    private TimelineState State => Page.State;

    private bool Collapsed => Section is not null && State.IsGroupCollapsed(Section.JobGroup);

    private Task OnGroupToggledAsync() =>
        Section is null
            ? Task.CompletedTask
            : Page.OnGroupCollapsedChangedAsync(Section.JobGroup, !Collapsed);
}
