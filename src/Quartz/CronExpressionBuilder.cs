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

using System;

namespace Quartz;

/// <summary>
/// CronExpressionBuilder provides a fluent API for composing cron expression
/// strings programmatically, one field at a time - useful when a schedule is
/// assembled from user input (e.g. a scheduling UI) rather than hand-written.
/// </summary>
/// <remarks>
/// <para>
/// Unconfigured fields default to every value ('*'). The day-of-month and
/// day-of-week fields are mutually exclusive per cron syntax; the unused one
/// renders as '?'. Each field can be configured only once. Day-of-week values
/// are emitted using their textual names (SUN-SAT) so that the produced
/// expressions stay unambiguous across cron dialects that use different
/// day-of-week numbering.
/// </para>
/// <para>
/// Client code can use the builder to write code such as this:
/// </para>
/// <code>
/// CronExpression expression = CronExpressionBuilder.Create()
///     .WithSecond(0)
///     .WithMinuteIncrements(0, 15)
///     .WithHourRange(8, 17)
///     .OnWeekdays()
///     .Build(); // "0 0/15 8-17 ? * MON-FRI"
///
/// ITrigger trigger = TriggerBuilder.Create()
///     .WithIdentity("myTrigger")
///     .WithSchedule(CronScheduleBuilder.CronSchedule(expression))
///     .Build();
/// </code>
/// </remarks>
/// <seealso cref="CronExpression" />
/// <seealso cref="CronScheduleBuilder" />
public sealed class CronExpressionBuilder
{
    private static readonly string[] dayNames = ["SUN", "MON", "TUE", "WED", "THU", "FRI", "SAT"];

    private string? second;
    private string? minute;
    private string? hour;
    private string? dayOfMonth;
    private string? month;
    private string? dayOfWeek;
    private string? year;

    private static int MaxYear => CronExpression.MaxYear;

    private CronExpressionBuilder()
    {
    }

    /// <summary>
    /// Create a new CronExpressionBuilder with all fields unconfigured ("* * * ? * *").
    /// </summary>
    /// <returns>the new CronExpressionBuilder</returns>
    public static CronExpressionBuilder Create()
    {
        return new CronExpressionBuilder();
    }

    /// <summary>
    /// Set the second field to a single value.
    /// </summary>
    /// <param name="second">the second to fire on (0-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithSecond(int second)
    {
        ValidateRange(second, 0, 59, "second", nameof(second));
        return SetSecond($"{second}");
    }

    /// <summary>
    /// Set the second field to a list of values, e.g. "0,30". The values are
    /// emitted in the given order.
    /// </summary>
    /// <param name="seconds">the seconds to fire on (each 0-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithSeconds(params int[] seconds)
    {
        return SetSecond(JoinValues(seconds, 0, 59, "second", nameof(seconds)));
    }

    /// <summary>
    /// Set the second field to a range of values, e.g. "20-30". The range may
    /// wrap around, e.g. "55-5".
    /// </summary>
    /// <param name="start">the first second of the range (0-59)</param>
    /// <param name="end">the last second of the range (0-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithSecondRange(int start, int end)
    {
        ValidateRange(start, 0, 59, "second", nameof(start));
        ValidateRange(end, 0, 59, "second", nameof(end));
        return SetSecond($"{start}-{end}");
    }

    /// <summary>
    /// Set the second field to an incremental list of values, e.g. "0/15"
    /// (every 15 seconds starting at second 0).
    /// </summary>
    /// <param name="start">the second to start at (0-59)</param>
    /// <param name="increment">the number of seconds between values (1-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithSecondIncrements(int start, int increment)
    {
        ValidateRange(start, 0, 59, "second", nameof(start));
        ValidateIncrement(increment, 59);
        return SetSecond($"{start}/{increment}");
    }

    /// <summary>
    /// Set the minute field to a single value.
    /// </summary>
    /// <param name="minute">the minute to fire on (0-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMinute(int minute)
    {
        ValidateRange(minute, 0, 59, "minute", nameof(minute));
        return SetMinute($"{minute}");
    }

    /// <summary>
    /// Set the minute field to a list of values, e.g. "0,30". The values are
    /// emitted in the given order.
    /// </summary>
    /// <param name="minutes">the minutes to fire on (each 0-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMinutes(params int[] minutes)
    {
        return SetMinute(JoinValues(minutes, 0, 59, "minute", nameof(minutes)));
    }

    /// <summary>
    /// Set the minute field to a range of values, e.g. "20-30". The range may
    /// wrap around, e.g. "55-5".
    /// </summary>
    /// <param name="start">the first minute of the range (0-59)</param>
    /// <param name="end">the last minute of the range (0-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMinuteRange(int start, int end)
    {
        ValidateRange(start, 0, 59, "minute", nameof(start));
        ValidateRange(end, 0, 59, "minute", nameof(end));
        return SetMinute($"{start}-{end}");
    }

    /// <summary>
    /// Set the minute field to an incremental list of values, e.g. "0/15"
    /// (every 15 minutes starting at minute 0).
    /// </summary>
    /// <param name="start">the minute to start at (0-59)</param>
    /// <param name="increment">the number of minutes between values (1-59)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMinuteIncrements(int start, int increment)
    {
        ValidateRange(start, 0, 59, "minute", nameof(start));
        ValidateIncrement(increment, 59);
        return SetMinute($"{start}/{increment}");
    }

    /// <summary>
    /// Set the hour field to a single value.
    /// </summary>
    /// <param name="hour">the hour to fire on (0-23)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithHour(int hour)
    {
        ValidateRange(hour, 0, 23, "hour", nameof(hour));
        return SetHour($"{hour}");
    }

    /// <summary>
    /// Set the hour field to a list of values, e.g. "8,12,16". The values are
    /// emitted in the given order.
    /// </summary>
    /// <param name="hours">the hours to fire on (each 0-23)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithHours(params int[] hours)
    {
        return SetHour(JoinValues(hours, 0, 23, "hour", nameof(hours)));
    }

    /// <summary>
    /// Set the hour field to a range of values, e.g. "8-17". The range may
    /// wrap around, e.g. "22-2".
    /// </summary>
    /// <param name="start">the first hour of the range (0-23)</param>
    /// <param name="end">the last hour of the range (0-23)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithHourRange(int start, int end)
    {
        ValidateRange(start, 0, 23, "hour", nameof(start));
        ValidateRange(end, 0, 23, "hour", nameof(end));
        return SetHour($"{start}-{end}");
    }

    /// <summary>
    /// Set the hour field to an incremental list of values, e.g. "0/6"
    /// (every 6 hours starting at hour 0).
    /// </summary>
    /// <param name="start">the hour to start at (0-23)</param>
    /// <param name="increment">the number of hours between values (1-23)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithHourIncrements(int start, int increment)
    {
        ValidateRange(start, 0, 23, "hour", nameof(start));
        ValidateIncrement(increment, 23);
        return SetHour($"{start}/{increment}");
    }

    /// <summary>
    /// Set the day-of-month field to a single value. Cannot be combined with
    /// configuring the day-of-week field.
    /// </summary>
    /// <param name="day">the day of month to fire on (1-31)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithDayOfMonth(int day)
    {
        ValidateRange(day, 1, 31, "day-of-month", nameof(day));
        return SetDayOfMonth($"{day}");
    }

    /// <summary>
    /// Set the day-of-month field to a list of values, e.g. "1,15". The values
    /// are emitted in the given order. Cannot be combined with configuring the
    /// day-of-week field.
    /// </summary>
    /// <param name="days">the days of month to fire on (each 1-31)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithDaysOfMonth(params int[] days)
    {
        return SetDayOfMonth(JoinValues(days, 1, 31, "day-of-month", nameof(days)));
    }

    /// <summary>
    /// Set the day-of-month field to a range of values, e.g. "20-25". The range
    /// may wrap around, e.g. "28-3". Cannot be combined with configuring the
    /// day-of-week field.
    /// </summary>
    /// <param name="start">the first day of the range (1-31)</param>
    /// <param name="end">the last day of the range (1-31)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithDayOfMonthRange(int start, int end)
    {
        ValidateRange(start, 1, 31, "day-of-month", nameof(start));
        ValidateRange(end, 1, 31, "day-of-month", nameof(end));
        return SetDayOfMonth($"{start}-{end}");
    }

    /// <summary>
    /// Set the day-of-month field to an incremental list of values, e.g. "1/5"
    /// (every 5 days starting on the 1st). Cannot be combined with configuring
    /// the day-of-week field.
    /// </summary>
    /// <param name="start">the day of month to start at (1-31)</param>
    /// <param name="increment">the number of days between values (1-31)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithDayOfMonthIncrements(int start, int increment)
    {
        ValidateRange(start, 1, 31, "day-of-month", nameof(start));
        ValidateIncrement(increment, 31);
        return SetDayOfMonth($"{start}/{increment}");
    }

    /// <summary>
    /// Set the day-of-month field to the last day of the month ("L"). Cannot be
    /// combined with configuring the day-of-week field.
    /// </summary>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnLastDayOfMonth()
    {
        return SetDayOfMonth("L");
    }

    /// <summary>
    /// Set the day-of-month field to the weekday (Monday-Friday) nearest to the
    /// given day, e.g. "15W". Cannot be combined with configuring the
    /// day-of-week field.
    /// </summary>
    /// <param name="day">the day of month to fire nearest to (1-31)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnNearestWeekdayOfMonth(int day)
    {
        ValidateRange(day, 1, 31, "day-of-month", nameof(day));
        return SetDayOfMonth($"{day}W");
    }

    /// <summary>
    /// Set the month field to a single value.
    /// </summary>
    /// <param name="month">the month to fire on (1-12)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMonth(int month)
    {
        ValidateRange(month, 1, 12, "month", nameof(month));
        return SetMonth($"{month}");
    }

    /// <summary>
    /// Set the month field to a list of values, e.g. "3,6,9,12". The values are
    /// emitted in the given order.
    /// </summary>
    /// <param name="months">the months to fire on (each 1-12)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMonths(params int[] months)
    {
        return SetMonth(JoinValues(months, 1, 12, "month", nameof(months)));
    }

    /// <summary>
    /// Set the month field to a range of values, e.g. "2-8". The range may wrap
    /// around, e.g. "11-2".
    /// </summary>
    /// <param name="start">the first month of the range (1-12)</param>
    /// <param name="end">the last month of the range (1-12)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMonthRange(int start, int end)
    {
        ValidateRange(start, 1, 12, "month", nameof(start));
        ValidateRange(end, 1, 12, "month", nameof(end));
        return SetMonth($"{start}-{end}");
    }

    /// <summary>
    /// Set the month field to an incremental list of values, e.g. "3/4"
    /// (every 4 months starting in March).
    /// </summary>
    /// <param name="start">the month to start at (1-12)</param>
    /// <param name="increment">the number of months between values (1-12)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithMonthIncrements(int start, int increment)
    {
        ValidateRange(start, 1, 12, "month", nameof(start));
        ValidateIncrement(increment, 12);
        return SetMonth($"{start}/{increment}");
    }

    /// <summary>
    /// Set the day-of-week field to the given days, e.g. "MON,THU,FRI". The
    /// values are emitted in the given order. Cannot be combined with
    /// configuring the day-of-month field.
    /// </summary>
    /// <param name="daysOfWeek">the days of the week to fire on</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnDaysOfWeek(params DayOfWeek[] daysOfWeek)
    {
        if (daysOfWeek is null || daysOfWeek.Length == 0)
        {
            throw new ArgumentException("At least one day-of-week must be specified.", nameof(daysOfWeek));
        }

        string[] names = new string[daysOfWeek.Length];
        for (int i = 0; i < daysOfWeek.Length; i++)
        {
            names[i] = GetDayName(daysOfWeek[i], nameof(daysOfWeek));
        }

        return SetDayOfWeek(string.Join(",", names));
    }

    /// <summary>
    /// Set the day-of-week field to a range of days, e.g. "MON-FRI". The range
    /// may wrap around, e.g. "FRI-MON". Cannot be combined with configuring the
    /// day-of-month field.
    /// </summary>
    /// <param name="start">the first day of the range</param>
    /// <param name="end">the last day of the range</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnDayOfWeekRange(DayOfWeek start, DayOfWeek end)
    {
        return SetDayOfWeek($"{GetDayName(start, nameof(start))}-{GetDayName(end, nameof(end))}");
    }

    /// <summary>
    /// Set the day-of-week field to every <paramref name="increment"/>th day
    /// starting from the given day, emitted as an explicit list of day names,
    /// e.g. Monday with increment 2 produces "MON,WED,FRI". Cannot be combined
    /// with configuring the day-of-month field.
    /// </summary>
    /// <remarks>
    /// The list stops at Saturday and does not wrap around, matching the
    /// semantics of a numeric cron day-of-week increment. Note that this
    /// differs from Quartz's textual "MON/2" syntax, which means every second
    /// week.
    /// </remarks>
    /// <param name="start">the day of the week to start at</param>
    /// <param name="increment">the number of days between values (&gt;= 1)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnDayOfWeekIncrements(DayOfWeek start, int increment)
    {
        if (increment < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(increment), "Invalid increment (must be >= 1).");
        }

        GetDayName(start, nameof(start));

        int startIndex = (int) start;
        string[] names = new string[(6 - startIndex) / increment + 1];
        for (int i = 0; i < names.Length; i++)
        {
            names[i] = dayNames[startIndex + i * increment];
        }

        return SetDayOfWeek(string.Join(",", names));
    }

    /// <summary>
    /// Set the day-of-week field to the nth occurrence of the given day in the
    /// month, e.g. "FRI#3" for the third Friday. Cannot be combined with
    /// configuring the day-of-month field.
    /// </summary>
    /// <param name="dayOfWeek">the day of the week</param>
    /// <param name="nth">the occurrence of the day within the month (1-5)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnNthDayOfWeekOfMonth(DayOfWeek dayOfWeek, int nth)
    {
        ValidateRange(nth, 1, 5, "nth occurrence", nameof(nth));
        return SetDayOfWeek($"{GetDayName(dayOfWeek, nameof(dayOfWeek))}#{nth}");
    }

    /// <summary>
    /// Set the day-of-week field to the last occurrence of the given day in the
    /// month, e.g. "FRIL" for the last Friday. Cannot be combined with
    /// configuring the day-of-month field.
    /// </summary>
    /// <param name="dayOfWeek">the day of the week</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnLastDayOfWeekOfMonth(DayOfWeek dayOfWeek)
    {
        return SetDayOfWeek($"{GetDayName(dayOfWeek, nameof(dayOfWeek))}L");
    }

    /// <summary>
    /// Set the day-of-week field to the last day of the cron week ("L"), which
    /// is Saturday. Cannot be combined with configuring the day-of-month field.
    /// </summary>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnLastDayOfWeek()
    {
        return SetDayOfWeek("L");
    }

    /// <summary>
    /// Set the day-of-week field to weekdays ("MON-FRI"). Cannot be combined
    /// with configuring the day-of-month field.
    /// </summary>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder OnWeekdays()
    {
        return SetDayOfWeek("MON-FRI");
    }

    /// <summary>
    /// Set the year field to a single value.
    /// </summary>
    /// <param name="year">the year to fire on (1970 to circa 100 years from now)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithYear(int year)
    {
        ValidateYear(year, nameof(year));
        return SetYear($"{year}");
    }

    /// <summary>
    /// Set the year field to a list of values, e.g. "2030,2032". The values are
    /// emitted in the given order.
    /// </summary>
    /// <param name="years">the years to fire on</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithYears(params int[] years)
    {
        if (years is null || years.Length == 0)
        {
            throw new ArgumentException("At least one year must be specified.", nameof(years));
        }

        foreach (int value in years)
        {
            ValidateYear(value, nameof(years));
        }

        return SetYear(string.Join(",", years));
    }

    /// <summary>
    /// Set the year field to a range of values, e.g. "2030-2035". Unlike the
    /// other fields, the year range cannot wrap around.
    /// </summary>
    /// <param name="start">the first year of the range</param>
    /// <param name="end">the last year of the range</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithYearRange(int start, int end)
    {
        ValidateYear(start, nameof(start));
        ValidateYear(end, nameof(end));
        if (start > end)
        {
            throw new ArgumentException("Start year must be less than or equal to end year.", nameof(start));
        }

        return SetYear($"{start}-{end}");
    }

    /// <summary>
    /// Set the year field to an incremental list of values, e.g. "2030/2"
    /// (every second year starting in 2030).
    /// </summary>
    /// <param name="start">the year to start at</param>
    /// <param name="increment">the number of years between values (&gt;= 1)</param>
    /// <returns>the updated CronExpressionBuilder</returns>
    public CronExpressionBuilder WithYearIncrements(int start, int increment)
    {
        ValidateYear(start, nameof(start));
        if (increment < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(increment), "Invalid increment (must be >= 1).");
        }

        return SetYear($"{start}/{increment}");
    }

    /// <summary>
    /// Build a validated <see cref="CronExpression" /> from the configured fields.
    /// </summary>
    /// <returns>the new CronExpression</returns>
    /// <exception cref="FormatException">if the composed expression fails final validation</exception>
    public CronExpression Build()
    {
        return new CronExpression(ToString());
    }

    /// <summary>
    /// Returns the cron expression string composed from the configured fields.
    /// </summary>
    public override string ToString()
    {
        string dayOfMonthPart = dayOfMonth ?? "?";
        string dayOfWeekPart = dayOfWeek ?? (dayOfMonth is not null ? "?" : "*");
        string expression = $"{second ?? "*"} {minute ?? "*"} {hour ?? "*"} {dayOfMonthPart} {month ?? "*"} {dayOfWeekPart}";
        return year is null ? expression : $"{expression} {year}";
    }

    private CronExpressionBuilder SetSecond(string value)
    {
        if (second is not null)
        {
            throw new InvalidOperationException("Second has already been configured.");
        }

        second = value;
        return this;
    }

    private CronExpressionBuilder SetMinute(string value)
    {
        if (minute is not null)
        {
            throw new InvalidOperationException("Minute has already been configured.");
        }

        minute = value;
        return this;
    }

    private CronExpressionBuilder SetHour(string value)
    {
        if (hour is not null)
        {
            throw new InvalidOperationException("Hour has already been configured.");
        }

        hour = value;
        return this;
    }

    private CronExpressionBuilder SetDayOfMonth(string value)
    {
        if (dayOfMonth is not null)
        {
            throw new InvalidOperationException("Day-of-month has already been configured.");
        }

        if (dayOfWeek is not null)
        {
            throw new InvalidOperationException("Day-of-month cannot be configured because day-of-week has already been configured. Cron expressions do not support specifying both day-of-month and day-of-week.");
        }

        dayOfMonth = value;
        return this;
    }

    private CronExpressionBuilder SetMonth(string value)
    {
        if (month is not null)
        {
            throw new InvalidOperationException("Month has already been configured.");
        }

        month = value;
        return this;
    }

    private CronExpressionBuilder SetDayOfWeek(string value)
    {
        if (dayOfWeek is not null)
        {
            throw new InvalidOperationException("Day-of-week has already been configured.");
        }

        if (dayOfMonth is not null)
        {
            throw new InvalidOperationException("Day-of-week cannot be configured because day-of-month has already been configured. Cron expressions do not support specifying both day-of-month and day-of-week.");
        }

        dayOfWeek = value;
        return this;
    }

    private CronExpressionBuilder SetYear(string value)
    {
        if (year is not null)
        {
            throw new InvalidOperationException("Year has already been configured.");
        }

        year = value;
        return this;
    }

    private static string GetDayName(DayOfWeek dayOfWeek, string paramName)
    {
        int index = (int) dayOfWeek;
        if (index < 0 || index > 6)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Invalid day-of-week value: {dayOfWeek}.");
        }

        return dayNames[index];
    }

    private static void ValidateRange(int value, int min, int max, string description, string paramName)
    {
        if (value < min || value > max)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Invalid {description} (must be >= {min} and <= {max}).");
        }
    }

    private static void ValidateIncrement(int increment, int max)
    {
        if (increment < 1 || increment > max)
        {
            throw new ArgumentOutOfRangeException(nameof(increment), $"Invalid increment (must be >= 1 and <= {max}).");
        }
    }

    private static void ValidateYear(int year, string paramName)
    {
        if (year < TriggerConstants.EarliestYear || year > MaxYear)
        {
            throw new ArgumentOutOfRangeException(paramName, $"Invalid year (must be >= {TriggerConstants.EarliestYear} and <= {MaxYear}).");
        }
    }

    private static string JoinValues(int[] values, int min, int max, string description, string paramName)
    {
        if (values is null || values.Length == 0)
        {
            throw new ArgumentException($"At least one {description} must be specified.", paramName);
        }

        foreach (int value in values)
        {
            ValidateRange(value, min, max, description, paramName);
        }

        return string.Join(",", values);
    }
}
