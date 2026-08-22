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

namespace Quartz.Dashboard.Components.Shared;

/// <summary>
/// Formatting the dashboard's pages share.
/// </summary>
/// <remarks>
/// This used to read values out of whatever the API client returned by walking JSON properties and
/// reflecting over properties, because the client's trigger, calendar and job-data members were
/// untyped. They are <see cref="ITrigger" />, <see cref="ICalendar" /> and <see cref="JobDataMap" />
/// now, so the pages read them as properties and none of that is needed.
/// </remarks>
internal static class DisplayValueHelper
{
    public static string FormatKey(string? group, string? name)
    {
        string safeGroup = string.IsNullOrWhiteSpace(group) ? "DEFAULT" : group;
        string safeName = string.IsNullOrWhiteSpace(name) ? "(unknown)" : name;
        return safeGroup + "." + safeName;
    }
}
