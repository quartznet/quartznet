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
/// ITrigger trigger = TriggerBuilder.Create()
///     .WithIdentity("myTrigger", "myTriggerGroup")
///     .WithSimpleSchedule(x => x
///         .WithInterval(TimeSpan.FromHours(1))
///         .RepeatForever())
///     .StartAt(DateBuilder.Create().AtHourMinuteAndSecond(10, 0, 0).Build())
///     .Build();
/// await scheduler.ScheduleJob(job, trigger);
/// </code>
/// <para>
/// For dates that are simply "now plus something", or a rounding of an existing date, use
/// <see cref="DateTimeOffset" /> arithmetic directly — <c>DateTimeOffset.UtcNow.AddMinutes(10)</c>
/// says what it does without a builder in the way.
/// </para>
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
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new DateBuilder</returns>
    public static DateBuilder Create(TimeProvider? timeProvider = null)
    {
        return new DateBuilder(timeProvider ?? TimeProvider.System);
    }

    /// <summary>
    /// Create a DateBuilder seeded with the machine's current local date and time, whose result is
    /// built in the given time zone.
    /// </summary>
    /// <remarks>
    /// The seed values come from the clock's local time, not from the given zone's wall clock; the
    /// zone decides the offset the built <see cref="DateTimeOffset" /> carries. Set the fields that
    /// matter explicitly rather than relying on the seed.
    /// </remarks>
    /// <param name="timeZone">Time zone to use.</param>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    /// <returns>the new DateBuilder</returns>
    public static DateBuilder CreateInTimeZone(TimeZoneInfo timeZone, TimeProvider? timeProvider = null)
    {
        return new DateBuilder(timeProvider ?? TimeProvider.System, timeZone);
    }

    /// <summary>
    /// Build the <see cref="DateTimeOffset" /> defined by this builder instance.
    /// </summary>
    /// <returns>New date time based on builder parameters.</returns>
    public DateTimeOffset Build()
    {
        DateTime dt = new DateTime(year, month, day, hour, minute, second);
        TimeSpan offset = TimeZones.GetUtcOffset(dt, tz ?? TimeZoneInfo.Local);
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

    /// <summary>
    /// Set the hour (0-23), minute (0-59) and second (0-59) for the date that will be built by this
    /// builder.
    /// </summary>
    /// <param name="hour">The hour of the day.</param>
    /// <param name="minute">The minute of the hour.</param>
    /// <param name="second">The second of the minute.</param>
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

    /// <summary>
    /// Set the month (1-12) and the day of the month (1-31) for the date that will be built by this
    /// builder.
    /// </summary>
    /// <param name="month">The month of the year.</param>
    /// <param name="day">The day of the month.</param>
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
    /// <param name="timeZone"></param>
    /// <returns></returns>
    public DateBuilder InTimeZone(TimeZoneInfo? timeZone)
    {
        tz = timeZone;
        return this;
    }

    private static void ValidateHour(int hour)
    {
        if (hour is < 0 or > 23)
        {
            Throw.ArgumentException("Invalid hour (must be >= 0 and <= 23).");
        }
    }

    private static void ValidateMinute(int minute)
    {
        if (minute is < 0 or > 59)
        {
            Throw.ArgumentException("Invalid minute (must be >= 0 and <= 59).");
        }
    }

    private static void ValidateSecond(int second)
    {
        if (second is < 0 or > 59)
        {
            Throw.ArgumentException("Invalid second (must be >= 0 and <= 59).");
        }
    }

    private static void ValidateDayOfMonth(int day)
    {
        if (day is < 1 or > 31)
        {
            Throw.ArgumentException("Invalid day of month.");
        }
    }

    private static void ValidateMonth(int month)
    {
        if (month is < 1 or > 12)
        {
            Throw.ArgumentException("Invalid month (must be >= 1 and <= 12).");
        }
    }

    private static void ValidateYear(int year)
    {
        if (year is < 1970 or > 2099)
        {
            Throw.ArgumentException("Invalid year (must be >= 1970 and <= 2099).");
        }
    }
}
