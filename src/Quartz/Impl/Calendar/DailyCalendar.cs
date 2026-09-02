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

using System.Diagnostics.CodeAnalysis;
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

    /// <summary>
    /// Writes this calendar's fields into a serialization payload.
    /// </summary>
    /// <param name="info">The payload being written.</param>
    /// <param name="context">The serialization context.</param>
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
    public override bool IsTimeIncluded(DateTimeOffset timeUtc)
    {
        if (CalendarBase is not null
            && CalendarBase.IsTimeIncluded(timeUtc) == false)
        {
            return false;
        }

        //Before we start, apply the correct timezone offsets.
        return IsInsideTheOpenPartOfTheDay(TimeZones.ConvertTime(timeUtc, TimeZone));
    }

    /// <summary>
    /// The calendar's own rule with no base calendar in it: whether the given instant, already
    /// expressed in <see cref="BaseCalendar.TimeZone" />, reads as a wall clock this calendar
    /// includes.
    /// </summary>
    /// <remarks>
    /// Every value it compares against carries the same offset as <paramref name="timeInZone" />, so
    /// every comparison it makes is a wall-clock one. That is what makes the window mean the same
    /// thing on a day that is 23 or 25 hours long as it does on any other day: an hour of exclusion
    /// as written can be no elapsed time at all, or two hours of it.
    /// </remarks>
    private bool IsInsideTheOpenPartOfTheDay(DateTimeOffset timeInZone)
    {
        DateTimeOffset startOfDayInMillis = GetStartOfDay(timeInZone);
        DateTimeOffset endOfDayInMillis = GetEndOfDay(timeInZone);
        DateTimeOffset timeRangeStartingTimeInMillis = GetTimeRangeStartingTimeUtc(timeInZone);
        DateTimeOffset timeRangeEndingTimeInMillis = GetTimeRangeEndingTimeUtc(timeInZone);
        if (!InvertTimeRange)
        {
            return timeInZone >= startOfDayInMillis &&
                   timeInZone < timeRangeStartingTimeInMillis ||
                   timeInZone > timeRangeEndingTimeInMillis &&
                   timeInZone <= endOfDayInMillis;
        }

        return timeInZone >= timeRangeStartingTimeInMillis &&
               timeInZone <= timeRangeEndingTimeInMillis;
    }

    /// <summary>
    /// Determine the next time (in milliseconds) that is 'included' by the
    /// Calendar after the given time. Return the original value if timeStamp is
    /// included. Return 0 if all days are excluded.
    /// </summary>
    /// <remarks>
    /// The question is answered in <see cref="BaseCalendar.TimeZone" /> whatever offset it is asked
    /// in, because the window is a wall-clock window of the calendar's own zone: the argument is
    /// converted first, exactly as <see cref="IsTimeIncluded" /> converts it, and the day's edges are
    /// named as wall-clock times on the local date and resolved back to instants there. Computing
    /// them at the offset the argument happened to carry made the two methods disagree whenever those
    /// offsets differed, and the walk then crept forward a millisecond at a time through a stretch it
    /// had already been told was excluded - minutes of spinning for an answer months out of place
    /// (#3466).
    /// </remarks>
    /// <seealso cref="ICalendar.GetNextIncludedTimeUtc"/>
    public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        DateTimeOffset nextIncludedTime = timeUtc.AddMilliseconds(OneMillis);

        while (!IsTimeIncluded(nextIncludedTime))
        {
            DateTimeOffset candidate;
            if (!IsInsideTheOpenPartOfTheDay(TimeZones.ConvertTime(nextIncludedTime, TimeZone)))
            {
                // The calendar's own window is what holds this time back, and the window's own edges
                // say when it lets go, so jump there rather than testing what lies between.
                candidate = NextTimeThisCalendarIncludes(nextIncludedTime);
            }
            else if (CalendarBase is not null &&
                     !CalendarBase.IsTimeIncluded(nextIncludedTime))
            {
                // The window is open and the base calendar is what holds the time back; it knows when
                // it lets go, so ask it and carry on testing from there.
                candidate = CalendarBase.GetNextIncludedTimeUtc(nextIncludedTime);
            }
            else
            {
                candidate = nextIncludedTime.AddMilliseconds(OneMillis);
            }

            // Both jumps move forward by construction; the millisecond step is what keeps a base
            // calendar that answers with a time of its own choosing from spinning the loop.
            nextIncludedTime = candidate > nextIncludedTime
                ? candidate
                : nextIncludedTime.AddMilliseconds(OneMillis);
        }

        return nextIncludedTime;
    }

    /// <summary>
    /// The first instant after <paramref name="time" /> that this calendar's own window rule
    /// includes, with no base calendar in it.
    /// </summary>
    /// <remarks>
    /// The answer is named rather than walked up to. While the zone's offset holds still the wall
    /// clock only enters the open part of a day at an edge of it, so the edges of the local date the
    /// query lands in - and of the date after it, once that one has no open time left - are the only
    /// instants worth asking about. Each is checked against the rule before it is taken, so an edge
    /// the zone makes meaningless drops out on its own, and the earliest survivor is the answer.
    /// </remarks>
    private DateTimeOffset NextTimeThisCalendarIncludes(DateTimeOffset time)
    {
        DateOnly date = DateOnly.FromDateTime(TimeZones.ConvertTime(time, TimeZone).DateTime);

        while (true)
        {
            DateTimeOffset? included = FirstIncludedInstantOnLocalDate(date, time);
            if (included is not null)
            {
                return included.Value;
            }

            // A calendar whose window leaves no open time on any day never has an answer to give;
            // the walk then runs out of dates, which is the end the millisecond walk came to as well.
            date = date.AddDays(1);
        }
    }

    /// <summary>
    /// The first instant of the given local date that is both past <paramref name="after" /> and
    /// inside the open part of the day, or <see langword="null" /> when that date has none.
    /// </summary>
    private DateTimeOffset? FirstIncludedInstantOnLocalDate(DateOnly date, DateTimeOffset after)
    {
        TimeZoneInfo timeZone = TimeZone;

        // The wall clock at which the day opens, and the one at which it closes again. An ordinary
        // calendar opens a millisecond past the window's end; an inverted one opens at its start.
        DateTime opens = InvertTimeRange
            ? date.ToDateTime(rangeStart)
            : date.ToDateTime(rangeEnd).AddMilliseconds(OneMillis);

        DateTime closes = InvertTimeRange
            ? date.ToDateTime(rangeEnd).AddMilliseconds(OneMillis)
            : date.ToDateTime(rangeStart);

        // The day's own first instant, which is neither always midnight nor always at the offset the
        // rest of the day carries.
        DateTimeOffset? earliest = EarliestIncluded(soFar: null, TimeZones.StartOfLocalDay(date, timeZone), after);

        // The instant the day opens at: the first instant the clock reads that edge or later. One
        // that happens twice resolves to the first of the two, that being the first instant the day
        // is open at, and one that never happens at all - the edge fell in a spring-forward gap - to
        // the instant the clocks moved.
        earliest = EarliestIncluded(earliest, TimeZones.FirstInstantAtOrAfterLocal(opens, timeZone), after);

        // ...and, when that wall clock happens twice, the second of the two, for a query that already
        // stands past the first.
        if (TimeZones.TryResolveSecondPass(opens, timeZone, out DateTimeOffset opensAgain))
        {
            earliest = EarliestIncluded(earliest, opensAgain, after);
        }

        // A fall-back does not merely repeat a wall clock, it takes the clock back into a part of the
        // day that was already over. That happens exactly when the edge the day closes at is inside
        // the repeated hour: the transition lands on the wall clock that hour began with, which is
        // before the closing edge and so back inside the open part of the day.
        if (TimeZones.TryGetAmbiguousWindow(closes, timeZone, out DateTime repeatedFrom, out _)
            && TimeZones.TryResolveSecondPass(repeatedFrom, timeZone, out DateTimeOffset clockGoesBack))
        {
            earliest = EarliestIncluded(earliest, clockGoesBack, after);
        }

        return earliest;
    }

    /// <summary>
    /// The earlier of <paramref name="soFar" /> and <paramref name="candidate" />, keeping only a
    /// candidate that lies past <paramref name="after" /> and that the calendar's own rule includes.
    /// </summary>
    private DateTimeOffset? EarliestIncluded(DateTimeOffset? soFar, DateTimeOffset candidate, DateTimeOffset after)
    {
        if (candidate <= after || !IsInsideTheOpenPartOfTheDay(TimeZones.ConvertTime(candidate, TimeZone)))
        {
            return soFar;
        }

        return soFar is null || candidate < soFar.Value ? candidate : soFar;
    }

    /// <inheritdoc />
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
    /// <remarks>
    /// The day is the local day of <see cref="BaseCalendar.TimeZone" />, so the argument is converted
    /// into the zone before its date is read: asked in UTC about a calendar that keeps another zone's
    /// hours, the answer is about the local date the instant falls in rather than the UTC one. The
    /// value is a wall clock paired with the offset that instant carries, which is what makes it
    /// comparable with the instant it was derived from; around a transition that offset need not be
    /// the one the edge itself would be resolved at, and
    /// <see cref="GetNextIncludedTimeUtc" />, which needs instants rather than comparands, resolves
    /// the edges through the zone instead.
    /// </remarks>
    /// <returns>
    ///     a DateTime representing the start time of the
    ///     time range for the specified date.
    /// </returns>
    public DateTimeOffset GetTimeRangeStartingTimeUtc(DateTimeOffset timeUtc)
    {
        return rangeStart.OnDate(TimeZones.ConvertTime(timeUtc, TimeZone));
    }

    /// <summary>
    /// Returns the end time of the time range of the day
    /// specified in <paramref name="timeUtc" />
    /// </summary>
    /// <remarks>
    /// The day is the local day of <see cref="BaseCalendar.TimeZone" />; see
    /// <see cref="GetTimeRangeStartingTimeUtc" />.
    /// </remarks>
    /// <returns>
    /// A DateTime representing the end time of the
    /// time range for the specified date.
    /// </returns>
    public DateTimeOffset GetTimeRangeEndingTimeUtc(DateTimeOffset timeUtc)
    {
        return rangeEnd.OnDate(TimeZones.ConvertTime(timeUtc, TimeZone));
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
    public TimeRange TimeRange
    {
        get => new(rangeStart, rangeEnd);
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
    /// <remarks>
    /// A wall-clock comparand, not an instant: it is midnight at whatever offset
    /// <paramref name="time" /> carries, which is the day's first instant only on a day whose offset
    /// never changes. That is all <see cref="IsInsideTheOpenPartOfTheDay" /> asks of it, since it
    /// compares it with a value carrying that same offset. Code that needs the instant a local day
    /// begins at uses <see cref="TimeZones.StartOfLocalDay" />.
    /// </remarks>
    /// <param name="time">The time, already expressed in the calendar's zone.</param>
    private static DateTimeOffset GetStartOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(time.Date, time.Offset);
    }

    /// <summary>
    /// Gets the end of day, practically sets time parts to maximum allowed values.
    /// </summary>
    /// <remarks>
    /// A wall-clock comparand; see <see cref="GetStartOfDay" />.
    /// </remarks>
    /// <param name="time">The time, already expressed in the calendar's zone.</param>
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

    /// <inheritdoc />
    [SuppressMessage("Sonar", "S2328:GetHashCode should not reference mutable fields", Justification = "Content equality over mutable state is what a calendar is; see BaseCalendar.GetHashCode.")]
    public override int GetHashCode()
    {
        int baseHash = 0;
        if (CalendarBase is not null)
        {
            baseHash = CalendarBase.GetHashCode();
        }

        return HashCode.Combine(rangeStart, rangeEnd, baseHash);
    }

    /// <summary>
    /// Whether this calendar and <paramref name="other" /> exclude the same times.
    /// </summary>
    /// <param name="other">The calendar to compare with.</param>
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

    /// <inheritdoc />
    public override bool Equals(object? obj)
    {
        if (obj is not DailyCalendar other)
        {
            return false;
        }

        return Equals(other);
    }
}
