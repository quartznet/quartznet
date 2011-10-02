#region License

/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

namespace Quartz
{
    /// <summary>
    /// <code>DateBuilder</code> is used to conveniently create
    /// <code>java.util.Date</code> instances that meet particular criteria.
    /// </summary>
    /// <remarks>
    /// <para>Quartz provides a builder-style API for constructing scheduling-related
    /// entities via a Domain-Specific Language (DSL).  The DSL can best be
    /// utilized through the usage of static imports of the methods on the classes
    /// <code>TriggerBuilder</code>, <code>JobBuilder</code>,
    /// <code>DateBuilder</code>, <code>JobKey</code>, <code>TriggerKey</code>
    /// and the various <code>ScheduleBuilder</code> implementations.</para>
    /// <para>Client code can then use the DSL to write code such as this:</para>
    /// <pre>
    /// JobDetail job = newJob(MyJob.class)
    /// .withIdentity("myJob")
    /// .build();
    /// Trigger trigger = newTrigger()
    /// .withIdentity(triggerKey("myTrigger", "myTriggerGroup"))
    /// .withSchedule(simpleSchedule()
    /// .withIntervalInHours(1)
    /// .repeatForever())
    /// .startAt(futureDate(10, MINUTES))
    /// .build();
    /// scheduler.scheduleJob(job, trigger);
    /// </pre>
    /// </remarks>
    /// <seealso cref="TriggerBuilder" />
    /// <seealso cref="JobBuilder" />
    public class DateBuilder
    {
        public enum IntervalUnit
        {
            Millisecond,
            Second,
            Minute,
            Hour,
            Day,
            Week,
            Month,
            Year
        } ;

        public const int Sunday = 1;
        public const int Monday = 2;
        public const int Tuesday = 3;
        public const int Wednesday = 4;
        public const int Thursday = 5;
        public const int Friday = 6;
        public const int Saturday = 7;
        public const long SecondsInMostDays = 24L*60L*60L;

        private int month;
        private int day;
        private int year;
        private int hour;
        private int minute;
        private int second;
        private TimeZoneInfo tz;

        /// <summary>
        /// Create a DateBuilder, with initial settings for the current date 
        /// and time in the system default timezone.
        /// </summary>
        private DateBuilder()
        {
            DateTime now = DateTime.Now;

            month = now.Month;
            day = now.Day;
            year = now.Year;
            hour = now.Hour;
            minute = now.Minute;
            second = now.Second;
        }


        /// <summary>
        /// Create a DateBuilder, with initial settings for the current date and time in the given timezone.
        /// </summary>
        /// <param name="tz"></param>
        private DateBuilder(TimeZoneInfo tz)
        {
            DateTime now = DateTime.Now;

            month = now.Month;
            day = now.Day;
            year = now.Year;
            hour = now.Hour;
            minute = now.Minute;
            second = now.Second;

            this.tz = tz;
        }

        /// <summary>
        /// Create a DateBuilder, with initial settings for the current date and time in the system default timezone.
        /// </summary>
        /// <returns></returns>
        public static DateBuilder NewDate()
        {
            return new DateBuilder();
        }

        /// <summary>
        /// Create a DateBuilder, with initial settings for the current date and time in the given timezone.
        /// </summary>
        /// <param name="tz">Time zone to use.</param>
        /// <returns></returns>
        public static DateBuilder NewDateInTimeZone(TimeZoneInfo tz)
        {
            return new DateBuilder(tz);
        }

        /// <summary>
        /// Build the <see cref="DateTimeOffset" /> defined by this builder instance. 
        /// </summary>
        /// <returns>New date time based on builder parameters.</returns>
        public DateTimeOffset Build()
        {
            DateTimeOffset cal;

            if (tz != null)
            {
                cal = new DateTimeOffset(year, month, day, hour, minute, second, 0, tz.BaseUtcOffset);
            }
            else
            {
                cal = new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);
            }

            return cal;
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

        public static DateTimeOffset FutureDate(int interval, IntervalUnit unit)
        {
            return TranslatedAdd(SystemTime.UtcNow(), unit, interval);
        }


        /// <summary>
        /// Get a <code>Date</code> object that represents the given time, on
        /// tomorrow's date.
        /// </summary>
        /// <param name="hour"></param>
        /// <param name="minute"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        public static DateTimeOffset TomorrowAt(int hour, int minute, int second)
        {
            ValidateSecond(second);
            ValidateMinute(minute);
            ValidateHour(hour);

            DateTimeOffset c = new DateTimeOffset(
                DateTimeOffset.Now.Year,
                DateTimeOffset.Now.Month,
                DateTimeOffset.Now.Day,
                hour,
                minute,
                second,
                0,
                TimeSpan.Zero);

            // advance one day
            c = c.AddDays(1);

            return c;
        }

        /// <summary>
        /// Get a <see cref="DateTimeOffset" /> object that represents the given time, on
        /// today's date (equivalent to <see cref="DateOf(int,int,int)" />.
        /// </summary>
        /// <param name="hour"></param>
        /// <param name="minute"></param>
        /// <param name="second"></param>
        /// <returns></returns>
        public static DateTimeOffset TodayAt(int hour, int minute, int second)
        {
            return DateOf(hour, minute, second);
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
                    return date.AddDays(amountToAdd*7);
                case IntervalUnit.Year:
                    return date.AddYears(amountToAdd);
                default:
                    throw new ArgumentException("Unknown IntervalUnit");
            }
        }

        /// <summary>
        /// <para>
        /// Get a <code>Date</code> object that represents the given time, on
        /// today's date.
        /// </para>
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="second"></param>
        /// The value (0-59) to give the seconds field of the date
        /// <param name="minute"></param>
        /// The value (0-59) to give the minutes field of the date
        /// <param name="hour"></param>
        /// The value (0-23) to give the hours field of the date
        /// <returns>the new date</returns>
        public static DateTimeOffset DateOf(int hour, int minute, int second)
        {
            ValidateSecond(second);
            ValidateMinute(minute);
            ValidateHour(hour);

            DateTimeOffset c = SystemTime.UtcNow();

            return new DateTimeOffset(c.Year, c.Month, c.Day, hour, minute, second, TimeSpan.Zero);
        }

        /// <summary>
        /// Get a <code>Date</code> object that represents the given time, on the
        /// given date.
        /// </summary>
        /// <param name="second"></param>
        /// The value (0-59) to give the seconds field of the date
        /// <param name="minute"></param>
        /// The value (0-59) to give the minutes field of the date
        /// <param name="hour"></param>
        /// The value (0-23) to give the hours field of the date
        /// <param name="dayOfMonth"></param>
        /// The value (1-31) to give the day of month field of the date
        /// <param name="month"></param>
        /// The value (1-12) to give the month field of the date
        /// <returns>the new date</returns>
        public static DateTimeOffset DateOf(int hour, int minute, int second,
                                            int dayOfMonth, int month)
        {
            ValidateSecond(second);
            ValidateMinute(minute);
            ValidateHour(hour);
            ValidateDayOfMonth(dayOfMonth);
            ValidateMonth(month);

            DateTimeOffset c = SystemTime.UtcNow();

            return new DateTimeOffset(c.Year, month, dayOfMonth, hour, minute, second, TimeSpan.Zero);
        }

        /// <summary>
        /// <para>
        /// Get a <code>Date</code> object that represents the given time, on the
        /// given date.
        /// </para>
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="second"></param>
        /// The value (0-59) to give the seconds field of the date
        /// <param name="minute"></param>
        /// The value (0-59) to give the minutes field of the date
        /// <param name="hour"></param>
        /// The value (0-23) to give the hours field of the date
        /// <param name="dayOfMonth"></param>
        /// The value (1-31) to give the day of month field of the date
        /// <param name="month"></param>
        /// The value (1-12) to give the month field of the date
        /// <param name="year"></param>
        /// The value (1970-2099) to give the year field of the date
        /// <returns>the new date</returns>
        public static DateTimeOffset DateOf(int hour, int minute, int second,
                                            int dayOfMonth, int month, int year)
        {
            ValidateSecond(second);
            ValidateMinute(minute);
            ValidateHour(hour);
            ValidateDayOfMonth(dayOfMonth);
            ValidateMonth(month);
            ValidateYear(year);

            DateTimeOffset c = SystemTime.UtcNow();

            return new DateTimeOffset(year, month, dayOfMonth, hour, minute, second, TimeSpan.Zero);
        }

        /// <summary>
        /// <para>
        /// Returns a date that is rounded to the next even hour after the current time.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// For example a current time of 08:13:54 would result in a date
        /// with the time of 09:00:00. If the date's time is in the 23rd hour, the
        /// date's 'day' will be promoted, and the time will be set to 00:00:00.
        /// </para>
        /// </remarks>
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenHourDateAfterNow()
        {
            return EvenHourDate(null);
        }

        /// <summary>
        /// Returns a date that is rounded to the next even hour above the given
        /// date.
        /// <para>
        /// For example an input date with a time of 08:13:54 would result in a date
        /// with the time of 09:00:00. If the date's time is in the 23rd hour, the
        /// date's 'day' will be promoted, and the time will be set to 00:00:00.
        /// </para>
        /// </summary>
        /// <param name="dateUtc">the Date to round, if <see langword="null" /> the current time will
        /// be used</param>
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenHourDate(DateTimeOffset? dateUtc)
        {
            if (!dateUtc.HasValue)
            {
                dateUtc = SystemTime.UtcNow();
            }
            DateTimeOffset d = dateUtc.Value.AddHours(1);
            return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, d.Offset);
        }

        /// <summary>
        /// Returns a date that is rounded to the previous even hour below the given
        /// date.
        /// <para>
        /// For example an input date with a time of 08:13:54 would result in a date
        /// with the time of 08:00:00.
        /// </para>
        /// </summary>
        /// <param name="dateUtc">the Date to round, if <see langword="null" /> the current time will
        /// be used</param>
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenHourDateBefore(DateTimeOffset? dateUtc)
        {
            if (!dateUtc.HasValue)
            {
                dateUtc = SystemTime.UtcNow();
            }
            return new DateTimeOffset(dateUtc.Value.Year, dateUtc.Value.Month, dateUtc.Value.Day, dateUtc.Value.Hour, 0, 0, dateUtc.Value.Offset);
        }

        /// <summary>
        /// <para>
        /// Returns a date that is rounded to the next even minute after the current time.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// For example a current time of 08:13:54 would result in a date
        /// with the time of 08:14:00. If the date's time is in the 59th minute,
        /// then the hour (and possibly the day) will be promoted.
        /// </para>
        /// </remarks>
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenMinuteDateAfterNow()
        {
            return EvenMinuteDate(SystemTime.UtcNow());
        }

        /// <summary>
        /// Returns a date that is rounded to the next even minute above the given
        /// date.
        /// <para>
        /// For example an input date with a time of 08:13:54 would result in a date
        /// with the time of 08:14:00. If the date's time is in the 59th minute,
        /// then the hour (and possibly the day) will be promoted.
        /// </para>
        /// </summary>
        /// <param name="dateUtc">The Date to round, if <see langword="null" /> the current time will  be used</param>
        /// <returns>The new rounded date</returns>
        public static DateTimeOffset EvenMinuteDate(DateTimeOffset? dateUtc)
        {
            if (!dateUtc.HasValue)
            {
                dateUtc = SystemTime.UtcNow();
            }

            DateTimeOffset d = dateUtc.Value;
            d = d.AddMinutes(1);
            return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
        }

        /// <summary>
        /// Returns a date that is rounded to the previous even minute below the
        /// given date.
        /// <para>
        /// For example an input date with a time of 08:13:54 would result in a date
        /// with the time of 08:13:00.
        /// </para>
        /// </summary>
        /// <param name="dateUtc">the Date to round, if <see langword="null" /> the current time will
        /// be used</param>
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenMinuteDateBefore(DateTimeOffset? dateUtc)
        {
            if (!dateUtc.HasValue)
            {
                dateUtc = SystemTime.UtcNow();
            }

            DateTimeOffset d = dateUtc.Value;
            return new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset);
        }

        /// <summary>
        /// <para>
        /// Returns a date that is rounded to the next even second after the current time.
        /// </para>
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenSecondDateAfterNow()
        {
            return EvenSecondDate(SystemTime.UtcNow());
        }

        /// <summary>
        /// <para>
        /// Returns a date that is rounded to the next even second above the given
        /// date.
        /// </para>
        /// </summary>
        /// <remarks>
        /// </remarks>
        /// <param name="date"></param>
        /// the Date to round, if <code>null</code> the current time will
        /// be used
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenSecondDate(DateTimeOffset date)
        {
            date = date.AddSeconds(1);
            return new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0, date.Offset);
        }

        /// <summary>
        /// <para>
        /// Returns a date that is rounded to the previous even second below the
        /// given date.
        /// </para>
        /// </summary>
        /// <remarks>
        /// <para>
        /// For example an input date with a time of 08:13:54.341 would result in a
        /// date with the time of 08:13:00.000.
        /// </para>
        /// </remarks>
        /// <param name="date"></param>
        /// the Date to round, if <code>null</code> the current time will
        /// be used
        /// <returns>the new rounded date</returns>
        public static DateTimeOffset EvenSecondDateBefore(DateTimeOffset date)
        {
            return new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, 0, date.Offset);
        }

        /// <summary>
        /// <para>
        /// Returns a date that is rounded to the next even multiple of the given
        /// minute.
        /// </para>
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
        /// <param name="date"></param>
        /// the Date to round, if <code>null</code> the current time will
        /// be used
        /// <param name="minuteBase"></param>
        /// the base-minute to set the time on
        /// <returns>the new rounded date</returns>
        /// <seealso cref="NextGivenSecondDate(DateTimeOffset?, int)" />
        public static DateTimeOffset NextGivenMinuteDate(DateTimeOffset? date, int minuteBase)
        {
            if (minuteBase < 0 || minuteBase > 59)
            {
                throw new ArgumentException("minuteBase must be >=0 and <= 59");
            }

            DateTimeOffset c = date ?? SystemTime.UtcNow();

            if (minuteBase == 0)
            {
                return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour + 1, 0, 0, 0, TimeSpan.Zero);
            }

            int minute = c.Minute;

            int arItr = minute/minuteBase;

            int nextMinuteOccurance = minuteBase*(arItr + 1);

            if (nextMinuteOccurance < 60)
            {
                return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, nextMinuteOccurance, 0, 0, TimeSpan.Zero);
            }
            else
            {
                return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour + 1, 0, 0, 0, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// <para>
        /// Returns a date that is rounded to the next even multiple of the given
        /// minute.
        /// </para>
        /// </summary>
        /// <remarks>
        /// The rules for calculating the second are the same as those for
        /// calculating the minute in the method
        /// <code>getNextGivenMinuteDate(..)</code>.
        /// </remarks>
        /// <param name="date">the Date to round, if <code>null</code> the current time will</param>
        /// be used
        /// <param name="secondBase">the base-second to set the time on</param>
        /// <returns>the new rounded date</returns>
        /// <seealso cref="NextGivenMinuteDate(DateTimeOffset?, int)" />
        public static DateTimeOffset NextGivenSecondDate(DateTimeOffset? date, int secondBase)
        {
            if (secondBase < 0 || secondBase > 59)
            {
                throw new ArgumentException("secondBase must be >=0 and <= 59");
            }

            DateTimeOffset c = date ?? SystemTime.UtcNow();

            if (secondBase == 0)
            {
                return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, c.Minute + 1, 0, 0, TimeSpan.Zero);
            }

            int second = c.Second;

            int arItr = second/secondBase;

            int nextSecondOccurance = secondBase*(arItr + 1);

            if (nextSecondOccurance < 60)
            {
                return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, c.Minute, nextSecondOccurance, 0, TimeSpan.Zero);
            }
            else
            {
                return new DateTimeOffset(c.Year, c.Month, c.Day, c.Hour, c.Minute + 1, 0, 0, TimeSpan.Zero);
            }
        }

        /// <summary>
        /// Translate a date and time from a users time zone to the another
        /// (probably server) timezone to assist in creating a simple trigger with
        /// the right date and time.
        /// </summary>
        /// <param name="date">the date to translate</param>
        /// <param name="src">the original time-zone</param>
        /// <param name="dest">the destination time-zone</param>
        /// <returns>the translated UTC date</returns>
        public static DateTimeOffset TranslateTime(DateTimeOffset date, TimeZoneInfo src, TimeZoneInfo dest)
        {
            DateTimeOffset newDate = SystemTime.UtcNow();
            double offset = (GetOffset(date, dest) - GetOffset(date, src));

            newDate = newDate.AddMilliseconds(-1*offset);

            return newDate;
        }

        /// <summary>
        /// Gets the offset from UT for the given date in the given time zone,
        /// taking into account daylight savings.
        /// </summary>
        /// <param name="date">the date that is the base for the offset</param>
        /// <param name="tz">the time-zone to calculate to offset to</param>
        /// <returns>the offset</returns>
        private static double GetOffset(DateTimeOffset date, TimeZoneInfo tz)
        {
            if (tz.IsDaylightSavingTime(date))
            {
                // TODO
                return tz.BaseUtcOffset.TotalMilliseconds + 0;
            }

            return tz.BaseUtcOffset.TotalMilliseconds;
        }

        ////////////////////////////////////////////////////////////////////////////////////////////////////

        public static void ValidateDayOfWeek(int dayOfWeek)
        {
            if (dayOfWeek < Sunday || dayOfWeek > Saturday)
            {
                throw new ArgumentException("Invalid day of week.");
            }
        }

        public static void ValidateHour(int hour)
        {
            if (hour < 0 || hour > 23)
            {
                throw new ArgumentException("Invalid hour (must be >= 0 and <= 23).");
            }
        }

        public static void ValidateMinute(int minute)
        {
            if (minute < 0 || minute > 59)
            {
                throw new ArgumentException("Invalid minute (must be >= 0 and <= 59).");
            }
        }

        public static void ValidateSecond(int second)
        {
            if (second < 0 || second > 59)
            {
                throw new ArgumentException("Invalid second (must be >= 0 and <= 59).");
            }
        }

        public static void ValidateDayOfMonth(int day)
        {
            if (day < 1 || day > 31)
            {
                throw new ArgumentException("Invalid day of month.");
            }
        }

        public static void ValidateMonth(int month)
        {
            if (month < 1 || month > 12)
            {
                throw new ArgumentException("Invalid month (must be >= 1 and <= 12.");
            }
        }

        public static void ValidateYear(int year)
        {
            if (year < 1970 || year > 2099)
            {
                throw new ArgumentException("Invalid year (must be >= 1970 and <= 2099.");
            }
        }
    }
}