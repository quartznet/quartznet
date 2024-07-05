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
/// Represents a time in hour, minute and second of any given day.
/// <remarks>
/// <para>
/// The hour is in 24-hour convention, meaning values are from 0 to 23.
/// </para>
/// </remarks>
/// </summary>
/// <seealso cref="IDailyTimeIntervalTrigger"/>
/// <author>James House</author>
/// <author>Zemian Deng saltnlight5@gmail.com</author>
/// <author>Nuno Maia (.NET)</author>
[Serializable]
public sealed class TimeOfDay
{
    /// <summary>
    /// Create a TimeOfDay instance for the given hour, minute and second.
    /// </summary>
    /// <param name="hour">The hour of day, between 0 and 23.</param>
    /// <param name="minute">The minute of the hour, between 0 and 59.</param>
    /// <param name="second">The second of the minute, between 0 and 59.</param>
    public TimeOfDay(int hour, int minute, int second)
    {
        Hour = hour;
        Minute = minute;
        Second = second;
        Validate();
    }

    /// <summary>
    /// Create a TimeOfDay instance for the given hour, minute (at the zero second of the minute).
    /// </summary>
    /// <param name="hour">The hour of day, between 0 and 23.</param>
    /// <param name="minute">The minute of the hour, between 0 and 59.</param>
    public TimeOfDay(int hour, int minute)
    {
        Hour = hour;
        Minute = minute;
        Second = 0;
        Validate();
    }

    private void Validate()
    {
        if (Hour < 0 || Hour > 23)
        {
            ThrowHelper.ThrowArgumentException("Hour must be from 0 to 23");
        }

        if (Minute < 0 || Minute > 59)
        {
            ThrowHelper.ThrowArgumentException("Minute must be from 0 to 59");
        }

        if (Second < 0 || Second > 59)
        {
            ThrowHelper.ThrowArgumentException("Second must be from 0 to 59");
        }
    }

    /// <summary>
    /// Create a TimeOfDay instance for the given hour, minute and second.
    /// </summary>
    /// <param name="hour">The hour of day, between 0 and 23.</param>
    /// <param name="minute">The minute of the hour, between 0 and 59.</param>
    /// <param name="second">The second of the minute, between 0 and 59.</param>
    /// <returns></returns>
    public static TimeOfDay HourMinuteAndSecondOfDay(int hour, int minute, int second)
    {
        return new TimeOfDay(hour, minute, second);
    }

    /// <summary>
    /// Create a TimeOfDay instance for the given hour, minute (at the zero second of the minute)..
    /// </summary>
    /// <param name="hour">The hour of day, between 0 and 23.</param>
    /// <param name="minute">The minute of the hour, between 0 and 59.</param>
    /// <returns>The newly instantiated TimeOfDay</returns>
    public static TimeOfDay HourAndMinuteOfDay(int hour, int minute)
    {
        return new TimeOfDay(hour, minute);
    }

    /// <summary>
    /// The hour of the day (between 0 and 23).
    /// </summary>
    public int Hour { get; }

    /// <summary>
    /// The minute of the hour (between 0 and 59).
    /// </summary>
    public int Minute { get; }

    /// <summary>
    /// The second of the minute (between 0 and 59).
    /// </summary>
    public int Second { get; }

    /// <summary>
    /// Determine with this time of day is before the given time of day.
    /// </summary>
    /// <param name="timeOfDay"></param>
    /// <returns>True this time of day is before the given time of day.</returns>
    public bool Before(TimeOfDay timeOfDay)
    {
        if (timeOfDay.Hour > Hour)
        {
            return true;
        }
        if (timeOfDay.Hour < Hour)
        {
            return false;
        }

        if (timeOfDay.Minute > Minute)
        {
            return true;
        }
        if (timeOfDay.Minute < Minute)
        {
            return false;
        }

        if (timeOfDay.Second > Second)
        {
            return true;
        }
        if (timeOfDay.Second < Second)
        {
            return false;
        }

        return false; // must be equal...
    }

    public override bool Equals(object? obj)
    {
        if (obj is not TimeOfDay timeOfDay)
        {
            return false;
        }

        return timeOfDay.Hour == Hour && timeOfDay.Minute == Minute && timeOfDay.Second == Second;
    }

    public override int GetHashCode()
    {
        return (Hour + 1) ^ (Minute + 1) ^ (Second + 1);
    }

    /// <summary>
    /// Return a date with time of day reset to this object values. The millisecond value will be zero.
    /// </summary>
    /// <param name="dateTime"></param>
    public DateTimeOffset? GetTimeOfDayForDate(DateTimeOffset? dateTime)
    {
        if (dateTime is null)
        {
            return null;
        }

        return GetTimeOfDayForDate(dateTime.Value);
    }

    /// <summary>
    /// Return a date with time of day reset to this object values. The millisecond value will be zero.
    /// </summary>
    /// <param name="dateTime"></param>
    public DateTimeOffset GetTimeOfDayForDate(DateTimeOffset dateTime)
    {
        DateTimeOffset cal = new DateTimeOffset(dateTime.Date, dateTime.Offset);
        TimeSpan t = new TimeSpan(0, Hour, Minute, Second);
        return cal.Add(t);
    }

    public override string ToString()
    {
        return "TimeOfDay[" + Hour + ":" + Minute + ":" + Second + "]";
    }
}