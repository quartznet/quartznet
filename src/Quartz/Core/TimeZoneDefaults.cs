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

using Quartz.Extensibility;
using Quartz.Impl.Triggers;

namespace Quartz.Core;

/// <summary>
/// Gives a trigger whose time zone nobody stated a scheduler's
/// <see cref="QuartzSchedulerOptions.DefaultTimeZone" />.
/// </summary>
internal static class TimeZoneDefaults
{
    /// <summary>
    /// Gives <paramref name="trigger" /> <paramref name="defaultTimeZone" /> when its zone was never
    /// stated, which states it.
    /// </summary>
    /// <remarks>
    /// A <see langword="null" /> default changes nothing: an unstated zone already reads as
    /// <see cref="TimeZoneInfo.Local" />. A trigger with no zone - a simple one, or a type of somebody
    /// else's - is left alone, and so is one outside <see cref="TriggerBase" />, which cannot say whether
    /// its zone was stated.
    /// </remarks>
    /// <returns>
    /// Whether the zone the trigger reads as changed, which leaves any fire time it already carries worked
    /// out in a zone it no longer has.
    /// </returns>
    internal static bool Apply(IMutableTrigger trigger, TimeZoneInfo? defaultTimeZone)
    {
        if (defaultTimeZone is null || trigger is not TriggerBase { TimeZoneStated: false })
        {
            return false;
        }

        TimeZoneInfo before;
        switch (trigger)
        {
            case CronTriggerImpl cron:
                before = cron.TimeZone;
                cron.TimeZone = defaultTimeZone;
                break;
            case CalendarIntervalTriggerImpl calendarInterval:
                before = calendarInterval.TimeZone;
                calendarInterval.TimeZone = defaultTimeZone;
                break;
            case DailyTimeIntervalTriggerImpl dailyTimeInterval:
                before = dailyTimeInterval.TimeZone;
                dailyTimeInterval.TimeZone = defaultTimeZone;
                break;
            case RecurrenceTriggerImpl recurrence:
                before = recurrence.TimeZone;
                recurrence.TimeZone = defaultTimeZone;
                break;
            default:
                return false;
        }

        return !before.Equals(defaultTimeZone);
    }
}
