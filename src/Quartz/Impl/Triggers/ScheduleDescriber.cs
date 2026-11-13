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

namespace Quartz.Impl.Triggers;

/// <summary>
/// Says a trigger's schedule in words: what <see cref="TriggerScheduleDescriptionExtensions.DescribeSchedule" />
/// answers, and the expression a schedule is written in where it has one.
/// </summary>
internal static class ScheduleDescriber
{
    /// <summary>
    /// The default end of a daily time interval's window: the last second of the day, which says nothing.
    /// </summary>
    private static readonly TimeOnly EndOfDay = new(23, 59, 59);

    /// <summary>
    /// The trigger's schedule in words, or <see langword="null" /> for a kind this does not know and for a
    /// cron or recurrence trigger that has no expression yet. Never throws for one it knows.
    /// </summary>
    public static string? Describe(ITrigger trigger)
    {
        string? schedule = trigger switch
        {
            ICronTrigger cron => string.IsNullOrWhiteSpace(cron.CronExpressionString)
                ? null
                : CronScheduleDescriber.Describe(cron.CronExpressionString, cron.TimeZone),
            ISimpleTrigger simple => DescribeSimple(simple),
            ICalendarIntervalTrigger calendarInterval => DescribeCalendarInterval(calendarInterval),
            IDailyTimeIntervalTrigger dailyTimeInterval => DescribeDailyTimeInterval(dailyTimeInterval),
            IRecurrenceTrigger recurrence => string.IsNullOrWhiteSpace(recurrence.RecurrenceRule)
                ? null
                : RecurrenceScheduleDescriber.Describe(recurrence.RecurrenceRule, recurrence.TimeZone),
            _ => null
        };

        if (schedule is not null && !string.IsNullOrEmpty(trigger.CalendarName))
        {
            schedule += ", excluding calendar '" + trigger.CalendarName + "'";
        }

        return schedule;
    }

    /// <summary>
    /// The text the trigger's schedule is written in: a cron trigger's expression and a recurrence trigger's
    /// rule. <see langword="null" /> for the kinds that are built from numbers rather than written.
    /// </summary>
    public static string? Expression(ITrigger trigger)
    {
        return trigger switch
        {
            ICronTrigger cron => cron.CronExpressionString,
            IRecurrenceTrigger recurrence => recurrence.RecurrenceRule,
            _ => null
        };
    }

    /// <summary>
    /// <c>Every 30 seconds</c>, <c>30 seconds after each run completes</c>, <c>Every 5 minutes, 3 times</c>,
    /// <c>Once</c>. No zone: a simple trigger counts elapsed time and has none.
    /// </summary>
    private static string DescribeSimple(ISimpleTrigger trigger)
    {
        if (trigger.RepeatCount == 0 || trigger.RepeatInterval <= TimeSpan.Zero)
        {
            return "Once";
        }

        (long count, string unit) = ScheduleWords.Duration(trigger.RepeatInterval);
        string text = trigger.MeasureIntervalFromCompletion
            ? ScheduleWords.Count(count, unit) + " after each run completes"
            : ScheduleWords.Capitalize(ScheduleWords.Every(count, unit));

        if (trigger.RepeatCount > 0)
        {
            // A repeat count is the repeats after the first firing, so the trigger fires once more than it.
            text += ", " + ScheduleWords.Times(trigger.RepeatCount + 1L);
        }

        return text;
    }

    /// <summary>
    /// <c>Every 2 months (UTC)</c>.
    /// </summary>
    private static string DescribeCalendarInterval(ICalendarIntervalTrigger trigger)
    {
        return ScheduleWords.Capitalize(ScheduleWords.Every(trigger.RepeatInterval, ScheduleWords.UnitName(trigger.RepeatIntervalUnit)))
               + ScheduleWords.Zone(trigger.TimeZone);
    }

    /// <summary>
    /// <c>Every 15 minutes from 09:00 to 17:00, Monday to Friday (UTC)</c>.
    /// </summary>
    private static string DescribeDailyTimeInterval(IDailyTimeIntervalTrigger trigger)
    {
        string text = ScheduleWords.Capitalize(ScheduleWords.Every(trigger.RepeatInterval, ScheduleWords.UnitName(trigger.RepeatIntervalUnit)));

        TimeOnly start = trigger.StartTimeOfDay;
        TimeOnly end = trigger.EndTimeOfDay;
        if (start != TimeOnly.MinValue || end != EndOfDay)
        {
            bool withSeconds = start.Second != 0 || end.Second != 0;
            text += " from " + ScheduleWords.Clock(start, withSeconds) + " to " + ScheduleWords.Clock(end, withSeconds);
        }

        if (ScheduleWords.Days(trigger.DaysOfWeek) is { } days)
        {
            text += ", " + days;
        }

        if (trigger.RepeatCount >= 0)
        {
            // Per day, and the first firing of the day is not a repeat.
            text += ", " + ScheduleWords.Times(trigger.RepeatCount + 1L) + " a day";
        }

        return text + ScheduleWords.Zone(trigger.TimeZone);
    }
}
