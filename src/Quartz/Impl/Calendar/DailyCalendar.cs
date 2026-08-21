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

using System.Runtime.Serialization;
using System.Text;

using Quartz.Util;

namespace Quartz.Impl.Calendar;

/// <summary>
/// This implementation of the Calendar excludes (or includes - see below) a
/// specified time range each day.
/// </summary>
/// <remarks>
/// For example, you could use this calendar to
/// exclude business hours (8AM - 5PM) every day. Each <see cref="DailyCalendar" />
/// only allows a single time range to be specified, and that time range may not
/// cross daily boundaries (i.e. you cannot specify a time range from 8PM - 5AM).
/// If the property <see cref="InvertTimeRange" /> is <see langword="false" /> (default),
/// the time range defines a range of times in which triggers are not allowed to
/// fire. If <see cref="InvertTimeRange" /> is <see langword="true" />, the time range
/// is inverted: that is, all times <i>outside</i> the defined time range
/// are excluded.
/// <para>
/// Note when using <see cref="DailyCalendar" />, it behaves on the same principals
/// as, for example, WeeklyCalendar defines a set of days that are
/// excluded <i>every week</i>. Likewise, <see cref="DailyCalendar" /> defines a
/// set of times that are excluded <i>every day</i>.
/// </para>
/// </remarks>
/// <author>Mike Funk</author>
/// <author>Aaron Craven</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public sealed class DailyCalendar : BaseCalendar, IEquatable<DailyCalendar>
{
    private const long OneMillis = 1;

    private TimeOnly rangeStart;
    private TimeOnly rangeEnd;

    /// <summary>
    /// Create a <see cref="DailyCalendar" /> excluding (or, with <see cref="InvertTimeRange" />,
    /// including) the given time range of every day.
    /// </summary>
    /// <remarks>
    /// The range may not cross a daily boundary, so <paramref name="rangeStart" /> must come
    /// before <paramref name="rangeEnd" />. Both are kept with one-millisecond resolution, which
    /// is what the calendar's serialized form carries.
    /// </remarks>
    /// <param name="rangeStart">The time of day the range starts at.</param>
    /// <param name="rangeEnd">The time of day the range ends at.</param>
    /// <param name="baseCalendar">
    /// The base calendar for this calendar instance, see <see cref="BaseCalendar" /> for more
    /// information on base calendar functionality.
    /// </param>
    /// <exception cref="ArgumentException">
    /// A bound carries precision finer than a whole millisecond, or the range does not start
    /// before it ends.
    /// </exception>
    public DailyCalendar(TimeOnly rangeStart, TimeOnly rangeEnd, ICalendar? baseCalendar = null)
        : base(baseCalendar)
    {
        ValidateRange(rangeStart, rangeEnd);
        this.rangeStart = rangeStart;
        this.rangeEnd = rangeEnd;
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    private DailyCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
    {
        int version;
        try
        {
            version = info.GetInt32("version");
        }
        catch
        {
            version = 0;
        }

        switch (version)
        {
            case 0:
            case 1:
                // The range has always been stored as eight separate integer fields; keep reading
                // them and fold them back into the two TimeOnly values.
                rangeStart = new TimeOnly(
                    info.GetInt32("rangeStartingHourOfDay"),
                    info.GetInt32("rangeStartingMinute"),
                    info.GetInt32("rangeStartingSecond"),
                    info.GetInt32("rangeStartingMillis"));

                rangeEnd = new TimeOnly(
                    info.GetInt32("rangeEndingHourOfDay"),
                    info.GetInt32("rangeEndingMinute"),
                    info.GetInt32("rangeEndingSecond"),
                    info.GetInt32("rangeEndingMillis"));

                InvertTimeRange = info.GetBoolean("invertTimeRange");
                break;
            default:
                Throw.NotSupportedException("Unknown serialization version");
                break;
        }
    }

    [System.Security.SecurityCritical]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);

        // Keep the eight-integer field layout so a payload written here stays readable by the
        // versions that only know that shape.
        info.AddValue("version", 1);
        info.AddValue("rangeStartingHourOfDay", rangeStart.Hour);
        info.AddValue("rangeStartingMinute", rangeStart.Minute);
        info.AddValue("rangeStartingSecond", rangeStart.Second);
        info.AddValue("rangeStartingMillis", rangeStart.Millisecond);

        info.AddValue("rangeEndingHourOfDay", rangeEnd.Hour);
        info.AddValue("rangeEndingMinute", rangeEnd.Minute);
        info.AddValue("rangeEndingSecond", rangeEnd.Second);
        info.AddValue("rangeEndingMillis", rangeEnd.Millisecond);

        info.AddValue("invertTimeRange", InvertTimeRange);
    }

    /// <summary>
    /// Determine whether the given time  is 'included' by the
    /// Calendar.
    /// </summary>
    /// <param name="timeUtc"></param>
    /// <returns></returns>
    public override bool IsTimeIncluded(DateTimeOffset timeUtc)
    {
        if (CalendarBase is not null
            && CalendarBase.IsTimeIncluded(timeUtc) == false)
        {
            return false;
        }

        //Before we start, apply the correct timezone offsets.
        timeUtc = TimeZones.ConvertTime(timeUtc, TimeZone);

        DateTimeOffset startOfDayInMillis = GetStartOfDay(timeUtc);
        DateTimeOffset endOfDayInMillis = GetEndOfDay(timeUtc);
        DateTimeOffset timeRangeStartingTimeInMillis = GetTimeRangeStartingTimeUtc(timeUtc);
        DateTimeOffset timeRangeEndingTimeInMillis = GetTimeRangeEndingTimeUtc(timeUtc);
        if (!InvertTimeRange)
        {
            if (timeUtc >= startOfDayInMillis &&
                timeUtc < timeRangeStartingTimeInMillis ||
                timeUtc > timeRangeEndingTimeInMillis &&
                timeUtc <= endOfDayInMillis)
            {
                return true;
            }
            return false;
        }
        if (timeUtc >= timeRangeStartingTimeInMillis &&
            timeUtc <= timeRangeEndingTimeInMillis)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// Determine the next time (in milliseconds) that is 'included' by the
    /// Calendar after the given time. Return the original value if timeStamp is
    /// included. Return 0 if all days are excluded.
    /// </summary>
    /// <param name="timeUtc"></param>
    /// <returns></returns>
    /// <seealso cref="ICalendar.GetNextIncludedTimeUtc"/>
    public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        DateTimeOffset nextIncludedTime = timeUtc.AddMilliseconds(OneMillis);

        while (!IsTimeIncluded(nextIncludedTime))
        {
            if (!InvertTimeRange)
            {
                //If the time is in a range excluded by this calendar, we can
                // move to the end of the excluded time range and continue
                // testing from there. Otherwise, if nextIncludedTime is
                // excluded by the baseCalendar, ask it the next time it
                // includes and begin testing from there. Failing this, add one
                // millisecond and continue testing.
                if (nextIncludedTime >=
                    GetTimeRangeStartingTimeUtc(nextIncludedTime) &&
                    nextIncludedTime <=
                    GetTimeRangeEndingTimeUtc(nextIncludedTime))
                {
                    nextIncludedTime =
                        GetTimeRangeEndingTimeUtc(nextIncludedTime).AddMilliseconds(OneMillis);
                }
                else if (CalendarBase is not null &&
                         !CalendarBase.IsTimeIncluded(nextIncludedTime))
                {
                    nextIncludedTime =
                        CalendarBase.GetNextIncludedTimeUtc(nextIncludedTime);
                }
                else
                {
                    nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
                }
            }
            else
            {
                //If the time is in a range excluded by this calendar, we can
                // move to the end of the excluded time range and continue
                // testing from there. Otherwise, if nextIncludedTime is
                // excluded by the baseCalendar, ask it the next time it
                // includes and begin testing from there. Failing this, add one
                // millisecond and continue testing.
                if (nextIncludedTime <
                    GetTimeRangeStartingTimeUtc(nextIncludedTime))
                {
                    nextIncludedTime =
                        GetTimeRangeStartingTimeUtc(nextIncludedTime);
                }
                else if (nextIncludedTime >
                         GetTimeRangeEndingTimeUtc(nextIncludedTime))
                {
                    //(move to start of next day)
                    nextIncludedTime = GetEndOfDay(nextIncludedTime);
                    nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
                }
                else if (CalendarBase is not null &&
                         !CalendarBase.IsTimeIncluded(nextIncludedTime))
                {
                    nextIncludedTime =
                        CalendarBase.GetNextIncludedTimeUtc(nextIncludedTime);
                }
                else
                {
                    nextIncludedTime = nextIncludedTime.AddMilliseconds(1);
                }
            }
        }

        return nextIncludedTime;
    }

    public override ICalendar Clone()
    {
        var clone = new DailyCalendar(rangeStart, rangeEnd, CalendarBase)
        {
            InvertTimeRange = InvertTimeRange
        };
        CloneFields(clone);
        return clone;
    }

    /// <summary>
    /// Returns the start time of the time range of the day
    /// specified in <paramref name="timeUtc" />.
    /// </summary>
    /// <returns>
    ///     a DateTime representing the start time of the
    ///     time range for the specified date.
    /// </returns>
    public DateTimeOffset GetTimeRangeStartingTimeUtc(DateTimeOffset timeUtc)
    {
        return rangeStart.OnDate(timeUtc);
    }

    /// <summary>
    /// Returns the end time of the time range of the day
    /// specified in <paramref name="timeUtc" />
    /// </summary>
    /// <returns>
    /// A DateTime representing the end time of the
    /// time range for the specified date.
    /// </returns>
    public DateTimeOffset GetTimeRangeEndingTimeUtc(DateTimeOffset timeUtc)
    {
        return rangeEnd.OnDate(timeUtc);
    }

    /// <summary>
    /// Indicates whether the time range represents an inverted time range (see
    /// class description).
    /// </summary>
    /// <value><c>true</c> if invert time range; otherwise, <c>false</c>.</value>
    public bool InvertTimeRange { get; set; }

    /// <summary>
    /// The time range this calendar excludes (or, with <see cref="InvertTimeRange" />, includes)
    /// every day.
    /// </summary>
    /// <remarks>
    /// The range may not cross a daily boundary, so <c>Start</c> must come before <c>End</c>.
    /// Both bounds are kept with one-millisecond resolution.
    /// </remarks>
    /// <exception cref="ArgumentException">
    /// A bound carries precision finer than a whole millisecond, or the range does not start
    /// before it ends.
    /// </exception>
    public (TimeOnly Start, TimeOnly End) TimeRange
    {
        get => (rangeStart, rangeEnd);
        set
        {
            ValidateRange(value.Start, value.End);
            rangeStart = value.Start;
            rangeEnd = value.End;
        }
    }

    /// <summary>
    /// Returns a <see cref="System.String"></see> that represents the current <see cref="System.Object"></see>.
    /// </summary>
    /// <returns>
    /// A <see cref="System.String"></see> that represents the current <see cref="System.Object"></see>.
    /// </returns>
    public override string ToString()
    {
        StringBuilder buffer = new StringBuilder();
        buffer.Append("base calendar: [");
        if (CalendarBase is not null)
        {
            buffer.Append(CalendarBase);
        }
        else
        {
            buffer.Append("null");
        }

        buffer.Append("], time range: '");
        buffer.Append(rangeStart.ToString("HH:mm:ss.fff"));
        buffer.Append(" - ");
        buffer.Append(rangeEnd.ToString("HH:mm:ss.fff"));
        buffer.AppendFormat("', inverted: {0}", InvertTimeRange);
        return buffer.ToString();
    }

    /// <summary>
    /// Gets the start of day, practically zeroes time part.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns></returns>
    private static DateTimeOffset GetStartOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(time.Date, time.Offset);
    }

    /// <summary>
    /// Gets the end of day, practically sets time parts to maximum allowed values.
    /// </summary>
    /// <param name="time">The time.</param>
    /// <returns></returns>
    private static DateTimeOffset GetEndOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(time.Date.AddDays(1).AddMilliseconds(-1), time.Offset);
    }

    private static void ValidateRange(TimeOnly rangeStart, TimeOnly rangeEnd)
    {
        TimeOnlyExtensions.ValidateWholeMilliseconds(rangeStart, nameof(rangeStart));
        TimeOnlyExtensions.ValidateWholeMilliseconds(rangeEnd, nameof(rangeEnd));

        if (rangeStart >= rangeEnd)
        {
            Throw.ArgumentException($"Invalid time range: {rangeStart:HH:mm:ss.fff} - {rangeEnd:HH:mm:ss.fff}; the range must start before it ends and may not cross a daily boundary.");
        }
    }

    public override int GetHashCode()
    {
        int baseHash = 0;
        if (CalendarBase is not null)
        {
            baseHash = CalendarBase.GetHashCode();
        }

        return HashCode.Combine(rangeStart, rangeEnd, baseHash);
    }

    public bool Equals(DailyCalendar? other)
    {
        if (other is null)
        {
            return false;
        }
        bool baseEqual = CalendarBase is null || CalendarBase.Equals(other.CalendarBase);

        return baseEqual
               && InvertTimeRange == other.InvertTimeRange
               && rangeStart == other.rangeStart
               && rangeEnd == other.rangeEnd;
    }

    public override bool Equals(object? obj)
    {
        if (obj is not DailyCalendar other)
        {
            return false;
        }

        return Equals(other);
    }
}
