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

namespace Quartz.Dashboard.Services;

/// <summary>
/// How the dashboard names a trigger's kind.
/// </summary>
/// <remarks>
/// One place, so that the listing and the detail page cannot name the same trigger differently. The schedule
/// is <see cref="TriggerScheduleDescriptionExtensions.DescribeSchedule" />'s, for the same reason.
/// </remarks>
internal static class TriggerDisplay
{
    public static string TypeName(ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        return trigger switch
        {
            ICronTrigger => "Cron",
            ISimpleTrigger => "Simple",
            ICalendarIntervalTrigger => "Calendar interval",
            IDailyTimeIntervalTrigger => "Daily time interval",
            IRecurrenceTrigger => "Recurrence",
            _ => trigger.GetType().Name
        };
    }
}
