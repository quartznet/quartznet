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

using Quartz.Util;

namespace Quartz;

/// <summary>
/// DateBuilder is used to conveniently create
/// <see cref="DateTimeOffset" /> instances that meet particular criteria.
/// </summary>
/// <remarks>
/// <para>
/// Quartz provides a builder-style API for constructing scheduling-related
/// entities via a Domain-Specific Language (DSL).  The DSL can best be
/// utilized through the usage of static imports of the methods on the classes
/// <see cref="TriggerBuilder" />, <see cref="JobBuilder" />,
/// <see cref="DateBuilder" />, <see cref="JobKey" />, <see cref="TriggerKey" />
/// and the various <see cref="IScheduleBuilder" /> implementations.
/// </para>
/// <para>Client code can then use the DSL to write code such as this:</para>
/// <code>
/// IJobDetail job = JobBuilder.Create&lt;MyJob>()
///     .WithIdentity("myJob")
///     .Build();
/// ITrigger trigger = newTrigger()
///     .WithIdentity(triggerKey("myTrigger", "myTriggerGroup"))
///     .WithSimpleSchedule(x => x
///         .WithIntervalInHours(1)
///         .RepeatForever())
///     .StartAt(DateBuilder.FutureDate(10, IntervalUnit.Minutes))
///     .Build();
/// scheduler.scheduleJob(job, trigger);
/// </code>
/// </remarks>
/// <seealso cref="TriggerBuilder" />
/// <seealso cref="JobBuilder" />
public sealed class DateBuilder
{
    private int month;
    private int day;
    private int year;
    private int hour;
    private int minute;
    private int second;
    private TimeZoneInfo? tz;

    /// <summary>
    /// Create a DateBuilder, with initial settings for the current date and time in the given timezone.
    /// </summary>
    private DateBuilder(TimeProvider timeProvider, TimeZoneInfo? tz = null)
    {
        if (tz is not null)
        {
            this.tz = tz;
        }

        DateTime now = timeProvider.GetLocalNow().DateTime;

        month = now.Month;
        day = now.Day;
        year = now.Year;
        hour = now.Hour;
        minute = now.Minute;
        second = now.Second;
    }

    /// <summary>
    /// Create a DateBuilder, with initial settings for the current date and time in the system default timezone.
    /// </summary>
    /// <param name="timeProvider"></param>
    /// <returns></returns>
    public static DateBuilder NewDate(TimeProvider? timeProvider = null)
    {
        return new DateBuilder(timeProvider ?? TimeProvider.System);
    }

    /// <summary>
    /// Create a DateBuilder, with initial settings for the current date and time in the given timezone.
    /// </summary>
    /// <param name="tz">Time zone to use.</param>
    /// <param name="timeProvider"></param>
    /// <returns></returns>
    public static DateBuilder NewDateInTimeZone(TimeZoneInfo tz, TimeProvider? timeProvider = null)
    {
        return new DateBuilder(timeProvider ?? TimeProvider.System, tz);
    }

    /// <summary>
    /// Build the <see cref="DateTimeOffset" /> defined by this builder instance.
    /// </summary>
    /// <returns>New date time based on builder parameters.</returns>
    public DateTimeOffset Build()
    {
        DateTime dt = new DateTime(year, month, day, hour, minute, second);
        TimeSpan offset = TimeZoneUtil.GetUtcOffset(dt, tz ?? TimeZoneInfo.Local);
        return new DateTimeOffset(dt, offset);
    }

    /// <summary>
    /// Set the hour (0-23) for the Date that will be built by this builder.
    /// </summary>
    /// <param name="hour"></param>
    /// <returns></returns>
    public DateBuilder AtHourOfDay(int hour)
    {
        ValidateHour(hour);

        this.hour = hour;
        return this;
    }

    /// <summary>
    /// Set the minute (0-59) for the Date that will be built by this builder.
    /// </summary>
    /// <param name="minute"></param>
    /// <returns></returns>
    public DateBuilder AtMinute(int minute)
    {
        ValidateMinute(minute);

        this.minute = minute;
        return this;
    }

    /// <summary>
    /// Set the second (0-59) for the Date that will be built by this builder, and truncate the milliseconds to 000.
    /// </summary>
    /// <param name="second"></param>
    /// <returns></returns>
    public DateBuilder AtSecond(int second)
    {
        ValidateSecond(second);

        this.second = second;
        return this;
    }

    public DateBuilder AtHourMinuteAndSecond(int hour, int minute, int second)
    {
        ValidateHour(hour);
        ValidateMinute(minute);
        ValidateSecond(second);

        this.hour = hour;
        this.second = second;
        this.minute = minute;
        return this;
    }

    /// <summary>
    /// Set the day of month (1-31) for the Date that will be built by this builder.
    /// </summary>
    /// <param name="day"></param>
    /// <returns></returns>
    public DateBuilder OnDay(int day)
    {
        ValidateDayOfMonth(day);

        this.day = day;
        return this;
    }

    /// <summary>
    /// Set the month (1-12) for the Date that will be built by this builder.
    /// </summary>
    /// <param name="month"></param>
    /// <returns></returns>
    public DateBuilder InMonth(int month)
    {
        ValidateMonth(month);

        this.month = month;
        return this;
    }

    public DateBuilder InMonthOnDay(int month, int day)
    {
        ValidateMonth(month);
        ValidateDayOfMonth(day);

        this.month = month;
        this.day = day;
        return this;
    }

    /// <summary>
    /// Set the year for the Date that will be built by this builder.
    /// </summary>
    /// <param name="year"></param>
    /// <returns></returns>
    public DateBuilder InYear(int year)
    {
        ValidateYear(year);

        this.year = year;
        return this;
    }

    /// <summary>
    /// Set the TimeZoneInfo for the Date that will be built by this builder (if "null", system default will be used)
    /// </summary>
    /// <param name="tz"></param>
    /// <returns></returns>
    public DateBuilder InTimeZone(TimeZoneInfo tz)
    {
        this.tz = tz;
        return this;
    }

    public static DateTimeOffset FutureDate(int interval, IntervalUnit unit, TimeProvider? timeProvider = null)
    {
        return TranslatedAdd((timeProvider ?? TimeProvider.System).GetLocalNow(), unit, interval);
    }

    /// <summary>
    /// Get a <see cref="DateTimeOffset" /> object that represents the given time, on tomorrow's date.
    /// </summary>
    public static DateTimeOffset TomorrowAt(int hour, int minute, int second, TimeProvider? timeProvider = null)
    {
        ValidateSecond(second);
        ValidateMinute(minute);
        ValidateHour(hour);

        DateTimeOffset now = (timeProvider ?? TimeProvider.System).GetLocalNow();
        DateTimeOffset c = new DateTimeOffset(
            now.Year,
            now.Month,
            now.Day,
            hour,
            minute,
            second,
            0,
            now.Offset);

        // advance one day
        c = c.AddDays(1);

        return c;
    }

    /// <summary>
    /// Get a <see cref="DateTimeOffset" /> object that represents the given time, on today's date (equivalent to <see cref="DateOf(int,int,int,TimeProvider)" />).
    /// </summary>
    public static DateTimeOffset TodayAt(int hour, int minute, int second, TimeProvider? timeProvider = null)
    {
        return DateOf(hour, minute, second, timeProvider);
    }

    private static DateTimeOffset TranslatedAdd(DateTimeOffset date, IntervalUnit unit, int amountToAdd)
    {
        switch (unit)
        {
            case IntervalUnit.Day:
                return date.AddDays(amountToAdd);
            case IntervalUnit.Hour:
                return date.AddHours(amountToAdd);
            case IntervalUnit.Minute:
                return date.AddMinutes(amountToAdd);
            case IntervalUnit.Month:
                return date.AddMonths(amountToAdd);
            case IntervalUnit.Second:
                return date.AddSeconds(amountToAdd);
            case IntervalUnit.Millisecond:
                return date.AddMilliseconds(amountToAdd);
            case IntervalUnit.Week:
                return date.AddDays(amountToAdd * 7);
            case IntervalUnit.Year:
                return date.AddYears(amountToAdd);
            default:
                ThrowHelper.ThrowArgumentException("Unknown IntervalUnit");
                return default;
        }
    }

    /// <summary>
    /// Get a <see cref="DateTimeOffset" /> object that represents the given time, on today's date.
    /// </summary>
    /// <param name="second">The value (0-59) to give the seconds field of the date</param>
    /// <param name="minute">The value (0-59) to give the minutes field of the date</param>
    /// <param name="hour">The value (0-23) to give the hours field of the date</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new date</returns>
    public static DateTimeOffset DateOf(int hour, int minute, int second, TimeProvider? timeProvider = null)
    {
        ValidateSecond(second);
        ValidateMinute(minute);
        ValidateHour(hour);

        DateTimeOffset c = (timeProvider ?? TimeProvider.System).GetLocalNow();
        DateTime dt = new DateTime(c.Year, c.Month, c.Day, hour, minute, second);
        return new DateTimeOffset(dt, TimeZoneUtil.GetUtcOffset(dt, TimeZoneInfo.Local));
    }

    /// <summary>
    /// Get a <see cref="DateTimeOffset" /> object that represents the given time, on the
    /// given date.
    /// </summary>
    /// <param name="second">The value (0-59) to give the seconds field of the date</param>
    /// <param name="minute">The value (0-59) to give the minutes field of the date</param>
    /// <param name="hour">The value (0-23) to give the hours field of the date</param>
    /// <param name="dayOfMonth">The value (1-31) to give the day of month field of the date</param>
    /// <param name="month">The value (1-12) to give the month field of the date</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new date</returns>
    public static DateTimeOffset DateOf(int hour, int minute, int second, int dayOfMonth, int month, TimeProvider? timeProvider = null)
    {
        ValidateSecond(second);
        ValidateMinute(minute);
        ValidateHour(hour);
        ValidateDayOfMonth(dayOfMonth);
        ValidateMonth(month);

        DateTimeOffset c = (timeProvider ?? TimeProvider.System).GetLocalNow();
        DateTime dt = new DateTime(c.Year, month, dayOfMonth, hour, minute, second);
        return new DateTimeOffset(dt, TimeZoneUtil.GetUtcOffset(dt, TimeZoneInfo.Local));
    }

    /// <summary>
    /// Get a <see cref="DateTimeOffset" /> object that represents the given time, on the
    /// given date.
    /// </summary>
    /// <remarks>
    /// </remarks>
    /// <param name="second">The value (0-59) to give the seconds field of the date</param>
    /// <param name="minute">The value (0-59) to give the minutes field of the date</param>
    /// <param name="hour">The value (0-23) to give the hours field of the date</param>
    /// <param name="dayOfMonth">The value (1-31) to give the day of month field of the date</param>
    /// <param name="month">The value (1-12) to give the month field of the date</param>
    /// <param name="year">The value (1970-2099) to give the year field of the date</param>
    /// <returns>the new date</returns>
    public static DateTimeOffset DateOf(int hour, int minute, int second, int dayOfMonth, int month, int year)
    {
        ValidateSecond(second);
        ValidateMinute(minute);
        ValidateHour(hour);
        ValidateDayOfMonth(dayOfMonth);
        ValidateMonth(month);
        ValidateYear(year);

        DateTime dt = new DateTime(year, month, dayOfMonth, hour, minute, second);
        return new DateTimeOffset(dt, TimeZoneUtil.GetUtcOffset(dt, TimeZoneInfo.Local));
    }

    /// <summary>
    /// Returns a date that is rounded to the next even hour after the current time.
    /// </summary>
    /// <remarks>
    /// For example a current time of 08:13:54 would result in a date
    /// with the time of 09:00:00. If the date's time is in the 23rd hour, the
    /// date's 'day' will be promoted, and the time will be set to 00:00:00.
    /// </remarks>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenHourDateAfterNow(TimeProvider? timeProvider = null)
    {
        return EvenHourDate(date: null, timeProvider);
    }

    /// <summary>
    /// Returns a date that is rounded to the next even hour above the given date.
    /// </summary>
    /// <remarks>
    /// For example an input date with a time of 08:13:54 would result in a date
    /// with the time of 09:00:00. If the date's time is in the 23rd hour, the
    /// date's 'day' will be promoted, and the time will be set to 00:00:00.
    /// </remarks>
    /// <param name="date">the Date to round, if <see langword="null" /> the current time will
    /// be used</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenHourDate(DateTimeOffset? date, TimeProvider? timeProvider = null)
    {
        if (!date.HasValue)
        {
            date = (timeProvider ?? TimeProvider.System).GetLocalNow();
        }
        DateTimeOffset d = date.Value.AddHours(1);
        return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, d.Offset);
    }

    /// <summary>
    /// Returns a date that is rounded to the previous even hour below the given date.
    /// </summary>
    /// <remarks>
    /// For example an input date with a time of 08:13:54 would result in a date
    /// with the time of 08:00:00.
    /// </remarks>
    /// <param name="date">the Date to round, if <see langword="null" /> the current time will be used</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenHourDateBefore(DateTimeOffset? date, TimeProvider? timeProvider = null)
    {
        if (!date.HasValue)
        {
            date = (timeProvider ?? TimeProvider.System).GetLocalNow();
        }
        return new DateTimeOffset(date.Value.Year, date.Value.Month, date.Value.Day, date.Value.Hour, 0, 0, date.Value.Offset);
    }

    /// <summary>
    /// <para>
    /// Returns a date that is rounded to the next even minute after the current time.
    /// </para>
    /// </summary>
    /// <remarks>
    /// For example a current time of 08:13:54 would result in a date
    /// with the time of 08:14:00. If the date's time is in the 59th minute,
    /// then the hour (and possibly the day) will be promoted.
    /// </remarks>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenMinuteDateAfterNow(TimeProvider? timeProvider = null)
    {
        return EvenMinuteDate((timeProvider ?? TimeProvider.System).GetLocalNow());
    }

    /// <summary>
    /// Returns a date that is rounded to the next even minute above the given date.
    /// </summary>
    /// <remarks>
    /// For example an input date with a time of 08:13:54 would result in a date
    /// with the time of 08:14:00. If the date's time is in the 59th minute,
    /// then the hour (and possibly the day) will be promoted.
    /// </remarks>
    /// <param name="date">The Date to round, if <see langword="null" /> the current time will  be used</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>The new rounded date</returns>
    public static DateTimeOffset EvenMinuteDate(DateTimeOffset? date, TimeProvider? timeProvider = null)
    {
        if (!date.HasValue)
        {
            date = (timeProvider ?? TimeProvider.System).GetLocalNow();
        }

        DateTimeOffset d = date.Value;
        d = d.AddMinutes(1);
        return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
    }

    /// <summary>
    /// Returns a date that is rounded to the previous even minute below the given date.
    /// </summary>
    /// <remarks>
    /// For example an input date with a time of 08:13:54 would result in a date
    /// with the time of 08:13:00.
    /// </remarks>
    /// <param name="date">the Date to round, if <see langword="null" /> the current time will be used</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenMinuteDateBefore(DateTimeOffset? date, TimeProvider? timeProvider = null)
    {
        if (!date.HasValue)
        {
            date = (timeProvider ?? TimeProvider.System).GetLocalNow();
        }

        DateTimeOffset d = date.Value;
        return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
    }

    /// <summary>
    /// Returns a date that is rounded to the next even second after the current time.
    /// </summary>
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenSecondDateAfterNow(TimeProvider? timeProvider = null)
    {
        return EvenSecondDate((timeProvider ?? TimeProvider.System).GetLocalNow());
    }

    /// <summary>
    /// Returns a date that is rounded to the next even second above the given date.
    /// </summary>
    /// <param name="date"></param>
    /// the Date to round, if <see langword="null" /> the current time will
    /// be used
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenSecondDate(DateTimeOffset date)
    {
        date = date.AddSeconds(1);
        return new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0, date.Offset);
    }

    /// <summary>
    /// Returns a date that is rounded to the previous even second below the
    /// given date.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For example an input date with a time of 08:13:54.341 would result in a
    /// date with the time of 08:13:00.000.
    /// </para>
    /// </remarks>
    /// <param name="date"></param>
    /// the Date to round, if <see langword="null" /> the current time will
    /// be used
    /// <returns>the new rounded date</returns>
    public static DateTimeOffset EvenSecondDateBefore(DateTimeOffset date)
    {
        return new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0, date.Offset);
    }

    /// <summary>
    /// Returns a date that is rounded to the next even multiple of the given
    /// minute.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For example an input date with a time of 08:13:54, and an input
    /// minute-base of 5 would result in a date with the time of 08:15:00. The
    /// same input date with an input minute-base of 10 would result in a date
    /// with the time of 08:20:00. But a date with the time 08:53:31 and an
    /// input minute-base of 45 would result in 09:00:00, because the even-hour
    /// is the next 'base' for 45-minute intervals.
    /// </para>
    /// <para>
    /// More examples: <table>
    /// <tr>
    /// <th>Input Time</th>
    /// <th>Minute-Base</th>
    /// <th>Result Time</th>
    /// </tr>
    /// <tr>
    /// <td>11:16:41</td>
    /// <td>20</td>
    /// <td>11:20:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:36:41</td>
    /// <td>20</td>
    /// <td>11:40:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:46:41</td>
    /// <td>20</td>
    /// <td>12:00:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:26:41</td>
    /// <td>30</td>
    /// <td>11:30:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:36:41</td>
    /// <td>30</td>
    /// <td>12:00:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:16:41</td>
    /// <td>17</td>
    /// <td>11:17:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:17:41</td>
    /// <td>17</td>
    /// <td>11:34:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:52:41</td>
    /// <td>17</td>
    /// <td>12:00:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:52:41</td>
    /// <td>5</td>
    /// <td>11:55:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:57:41</td>
    /// <td>5</td>
    /// <td>12:00:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:17:41</td>
    /// <td>0</td>
    /// <td>12:00:00</td>
    /// </tr>
    /// <tr>
    /// <td>11:17:41</td>
    /// <td>1</td>
    /// <td>11:08:00</td>
    /// </tr>
    /// </table>
    /// </para>
    /// </remarks>
    /// <param name="date">the Date to round, if <see langword="null" /> the current time will be used</param>
    /// <param name="minuteBase"> the base-minute to set the time on</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new rounded date</returns>
    /// <seealso cref="NextGivenSecondDate(DateTimeOffset?, int, TimeProvider)" />
    public static DateTimeOffset NextGivenMinuteDate(DateTimeOffset? date, int minuteBase, TimeProvider? timeProvider = null)
    {
        if (minuteBase < 0 || minuteBase > 59)
        {
            ThrowHelper.ThrowArgumentException("minuteBase must be >=0 and <= 59");
        }

        DateTimeOffset c = date ?? (timeProvider ?? TimeProvider.System).GetLocalNow();

        if (minuteBase == 0)
        {
            return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, 0, 0, 0, c.Offset).AddHours(1);
        }

        int minute = c.Minute;

        int arItr = minute / minuteBase;

        int nextMinuteOccurance = minuteBase * (arItr + 1);

        if (nextMinuteOccurance < 60)
        {
            return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, nextMinuteOccurance, 0, 0, c.Offset);
        }
        return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, 0, 0, 0, c.Offset).AddHours(1);
    }

    /// <summary>
    /// Returns a date that is rounded to the next even multiple of the given
    /// second.
    /// </summary>
    /// <remarks>
    /// The rules for calculating the second are the same as those for
    /// calculating the minute in the method <see cref="NextGivenMinuteDate" />.
    /// </remarks>
    /// <param name="date">the Date to round, if <see langword="null" /> the current time will</param>
    /// be used
    /// <param name="secondBase">the base-second to set the time on</param>
    /// <returns>the new rounded date</returns>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <seealso cref="NextGivenMinuteDate(DateTimeOffset?, int, TimeProvider)" />
    public static DateTimeOffset NextGivenSecondDate(DateTimeOffset? date, int secondBase, TimeProvider? timeProvider = null)
    {
        if (secondBase < 0 || secondBase > 59)
        {
            ThrowHelper.ThrowArgumentException("secondBase must be >=0 and <= 59");
        }

        DateTimeOffset c = date ?? (timeProvider ?? TimeProvider.System).GetLocalNow();

        if (secondBase == 0)
        {
            return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, c.Minute, 0, 0, c.Offset).AddMinutes(1);
        }

        int second = c.Second;

        int arItr = second / secondBase;

        int nextSecondOccurance = secondBase * (arItr + 1);

        if (nextSecondOccurance < 60)
        {
            return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, c.Minute, nextSecondOccurance, 0, c.Offset);
        }
        return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, c.Minute, 0, 0, c.Offset).AddMinutes(1);
    }

    internal static void ValidateHour(int hour)
    {
        if (hour is < 0 or > 23)
        {
            ThrowHelper.ThrowArgumentException("Invalid hour (must be >= 0 and <= 23).");
        }
    }

    internal static void ValidateMinute(int minute)
    {
        if (minute is < 0 or > 59)
        {
            ThrowHelper.ThrowArgumentException("Invalid minute (must be >= 0 and <= 59).");
        }
    }

    private static void ValidateSecond(int second)
    {
        if (second is < 0 or > 59)
        {
            ThrowHelper.ThrowArgumentException("Invalid second (must be >= 0 and <= 59).");
        }
    }

    internal static void ValidateDayOfMonth(int day)
    {
        if (day is < 1 or > 31)
        {
            ThrowHelper.ThrowArgumentException("Invalid day of month.");
        }
    }

    private static void ValidateMonth(int month)
    {
        if (month is < 1 or > 12)
        {
            ThrowHelper.ThrowArgumentException("Invalid month (must be >= 1 and <= 12).");
        }
    }

    private static void ValidateYear(int year)
    {
        if (year is < 1970 or > 2099)
        {
            ThrowHelper.ThrowArgumentException("Invalid year (must be >= 1970 and <= 2099).");
        }
    }
}