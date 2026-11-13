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

using System.Globalization;

using Quartz.Impl.Recurrence;

namespace Quartz.Impl.Triggers;

/// <summary>
/// Says an RFC 5545 recurrence rule in words: <c>Monthly on the second Tuesday (UTC)</c>.
/// </summary>
/// <remarks>
/// It phrases <c>FREQ</c>, <c>INTERVAL</c>, <c>BYDAY</c> (with or without ordinals), <c>BYMONTHDAY</c>,
/// <c>BYMONTH</c>, <c>BYHOUR</c> with <c>BYMINUTE</c>, <c>COUNT</c> and <c>UNTIL</c>. A rule that uses
/// <c>BYSETPOS</c>, <c>BYWEEKNO</c> or <c>BYYEARDAY</c>, or combines parts this does not phrase together,
/// answers <see langword="null" />, and the caller shows the rule itself.
/// </remarks>
internal static class RecurrenceScheduleDescriber
{
    /// <summary>
    /// The rule in words with its zone, or the rule itself with its zone.
    /// </summary>
    public static string Describe(string rule, TimeZoneInfo timeZone)
    {
        return (Phrase(rule) ?? rule.Trim()) + ScheduleWords.Zone(timeZone);
    }

    /// <summary>
    /// The rule in words, without a zone, or <see langword="null" /> when this has no sentence for it.
    /// </summary>
    internal static string? Phrase(string rule)
    {
        RecurrenceRule parsed;
        try
        {
            parsed = RecurrenceRule.Parse(rule);
        }
        catch (FormatException)
        {
            return null;
        }
        catch (OverflowException)
        {
            return null;
        }

        if (parsed.BySetPos is not null || parsed.ByWeekNo is not null || parsed.ByYearDay is not null)
        {
            return null;
        }

        string text = Frequency(parsed.Frequency, parsed.Interval);

        List<string> monthDays = [];
        if (parsed.ByDay is { Length: > 0 } byDay)
        {
            // Both together narrow each other (a Friday that is the 13th), which is no list of days.
            if (parsed.ByMonthDay is not null)
            {
                return null;
            }

            if (Array.TrueForAll(byDay, static x => x.Ordinal == 0))
            {
                List<DayOfWeek> days = byDay.Select(static x => x.Day).Distinct().ToList();
                text += " on " + (ScheduleWords.IsWorkWeek(days) ? "weekdays" : ScheduleWords.Days(days) ?? "every day");
            }
            else if (Array.TrueForAll(byDay, static x => x.Ordinal != 0))
            {
                monthDays.AddRange(byDay.Select(static x => "the " + ScheduleWords.SignedOrdinalWord(x.Ordinal) + " " + x.Day));
            }
            else
            {
                return null;
            }
        }

        if (parsed.ByMonthDay is { Length: > 0 } byMonthDay)
        {
            monthDays.AddRange(byMonthDay.Select(static day => day switch
            {
                -1 => "the last day",
                < 0 => "the " + ScheduleWords.SignedOrdinalWord(day) + " day",
                _ => "the " + ScheduleWords.Ordinal(day)
            }));
        }

        if (parsed.ByMonth is { Length: > 0 } byMonth)
        {
            string months = ScheduleWords.JoinList(byMonth.Order().Select(ScheduleWords.MonthName).ToList());
            text += monthDays.Count > 0 ? " on " + ScheduleWords.JoinList(monthDays) + " of " + months : " in " + months;
        }
        else if (monthDays.Count > 0)
        {
            text += " on " + ScheduleWords.JoinList(monthDays);
        }

        if (parsed.ByHour is not null || parsed.ByMinute is not null || parsed.BySecond is not null)
        {
            if (Times(parsed) is not { } times)
            {
                return null;
            }

            text += " at " + times;
        }

        if (parsed.Count is { } count)
        {
            text += ", " + ScheduleWords.Times(count);
        }
        else if (parsed.Until is { } until)
        {
            string format = until.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-dd HH:mm";
            text += ", until " + until.ToString(format, CultureInfo.InvariantCulture) + (parsed.UntilIsUtc ? " UTC" : string.Empty);
        }

        return text;
    }

    private static string Frequency(RecurrenceFrequency frequency, int interval)
    {
        if (interval == 1)
        {
            return frequency switch
            {
                RecurrenceFrequency.Secondly => "Every second",
                RecurrenceFrequency.Minutely => "Every minute",
                RecurrenceFrequency.Hourly => "Hourly",
                RecurrenceFrequency.Daily => "Daily",
                RecurrenceFrequency.Weekly => "Weekly",
                RecurrenceFrequency.Monthly => "Monthly",
                _ => "Yearly"
            };
        }

        string unit = frequency switch
        {
            RecurrenceFrequency.Secondly => "second",
            RecurrenceFrequency.Minutely => "minute",
            RecurrenceFrequency.Hourly => "hour",
            RecurrenceFrequency.Daily => "day",
            RecurrenceFrequency.Weekly => "week",
            RecurrenceFrequency.Monthly => "month",
            _ => "year"
        };

        return ScheduleWords.Capitalize(ScheduleWords.Every(interval, unit));
    }

    /// <summary>
    /// <c>09:00 and 17:00</c>: the times <c>BYHOUR</c> and <c>BYMINUTE</c> name, when there are a handful.
    /// Without both, the rest of the time comes from the trigger's start, which the rule does not say.
    /// </summary>
    private static string? Times(RecurrenceRule rule)
    {
        if (rule.ByHour is not { Length: > 0 } hours || rule.ByMinute is not { Length: > 0 } minutes)
        {
            return null;
        }

        int[] seconds = rule.BySecond is { Length: > 0 } bySecond ? bySecond : [0];
        if (hours.Length * minutes.Length * seconds.Length > 6)
        {
            return null;
        }

        bool withSeconds = Array.Exists(seconds, static x => x != 0);
        List<string> times = [];
        foreach (int hour in hours.Order())
        {
            foreach (int minute in minutes.Order())
            {
                foreach (int second in seconds.Order())
                {
                    times.Add(ScheduleWords.Clock(hour, minute, second, withSeconds));
                }
            }
        }

        return ScheduleWords.JoinList(times);
    }
}
