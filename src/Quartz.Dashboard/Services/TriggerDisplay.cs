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
/// How the dashboard names a trigger's kind and summarises its schedule.
/// </summary>
/// <remarks>
/// One place, so that the listing and the detail page cannot describe the same trigger differently.
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
            _ => trigger.GetType().Name
        };
    }

    public static string? ScheduleSummary(ITrigger trigger)
    {
        ArgumentNullException.ThrowIfNull(trigger);

        switch (trigger)
        {
            case ICronTrigger cron:
                return cron.CronExpressionString;
            case ISimpleTrigger simple:
                string summary = "Every " + simple.RepeatInterval;
                return summary + (simple.RepeatCount < 0 ? ", repeat forever" : ", " + simple.RepeatCount + " time(s)");
            default:
                return null;
        }
    }
}
