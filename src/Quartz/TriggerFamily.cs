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

namespace Quartz;

/// <summary>
/// The schedule families a trigger can belong to. A misfire instruction is only meaningful
/// within one of them: the same number means a different policy in each.
/// </summary>
internal enum TriggerFamily
{
    Simple,
    Cron,
    CalendarInterval,
    DailyTimeInterval,
    Recurrence,
}

internal static class TriggerFamilyExtensions
{
    /// <summary>
    /// The family the given trigger belongs to, or <see langword="null" /> when it belongs to
    /// none of the built-in ones.
    /// </summary>
    internal static TriggerFamily? Family(this ITrigger trigger)
    {
        return trigger switch
        {
            ISimpleTrigger => TriggerFamily.Simple,
            ICronTrigger => TriggerFamily.Cron,
            ICalendarIntervalTrigger => TriggerFamily.CalendarInterval,
            IDailyTimeIntervalTrigger => TriggerFamily.DailyTimeInterval,
            IRecurrenceTrigger => TriggerFamily.Recurrence,
            _ => null
        };
    }

    /// <summary>
    /// The name of the family as it reads in an error message: the interface a caller would cast to.
    /// </summary>
    internal static string DisplayName(this TriggerFamily family)
    {
        return family switch
        {
            TriggerFamily.Simple => "simple",
            TriggerFamily.Cron => "cron",
            TriggerFamily.CalendarInterval => "calendar interval",
            TriggerFamily.DailyTimeInterval => "daily time interval",
            TriggerFamily.Recurrence => "recurrence",
            _ => family.ToString()
        };
    }
}
