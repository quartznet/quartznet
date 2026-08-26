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
using System.Globalization;
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
/// * cross daily boundaries (i.e. you cannot specify a time range from 8PM - 5AM).
/// If the property <see cref="InvertTimeRange" /> is <see langword="false" /> (default),
/// the time range defines a range of times in which triggers are not allowed to
/// * fire. If <see cref="InvertTimeRange" /> is <see langword="true" />, the time range
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
public class DailyCalendar : BaseCalendar
{
    private const string InvalidHourOfDay = "Invalid hour of day: ";
    private const string InvalidMinute = "Invalid minute: ";
    private const string InvalidSecond = "Invalid second: ";
    private const string InvalidMillis = "Invalid millis: ";
    private const string InvalidTimeRange = "Invalid time range: ";
    private const string Separator = " - ";
    private const long OneMillis = 1;
    private const char Colon = ':';

    private const string TwoDigitFormat = "00";
    private const string ThreeDigitFormat = "000";

    private int rangeStartingHourOfDay;
    private int rangeStartingMinute;
    private int rangeStartingSecond;
    private int rangeStartingMillis;
    private int rangeEndingHourOfDay;
    private int rangeEndingMinute;
    private int rangeEndingSecond;
    private int rangeEndingMillis;

    private DailyCalendar()
    {
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar" /> with a time range defined by the
    /// specified strings and no baseCalendar.
    ///	<paramref name="rangeStartingTime" /> and <paramref name="rangeEndingTime" />
    /// must be in the format &quot;HH:MM[:SS[:mmm]]&quot; where:
    /// <ul>
    ///     <li>
    ///         HH is the hour of the specified time. The hour should be
    ///          specified using military (24-hour) time and must be in the range
    ///          0 to 23.
    ///     </li>
    ///     <li>
    ///         MM is the minute of the specified time and must be in the range
    ///         0 to 59.
    ///     </li>
    ///     <li>
    ///         SS is the second of the specified time and must be in the range
    ///         0 to 59.
    ///     </li>
    ///     <li>
    ///         mmm is the millisecond of the specified time and must be in the
    ///         range 0 to 999.
    ///     </li>
    ///     <li>items enclosed in brackets ('[', ']') are optional.</li>
    ///     <li>
    ///         The time range starting time must be before the time range ending
    ///         time. Note this means that a time range may not cross daily
    ///         boundaries (10PM - 2AM)
    ///     </li>
    /// </ul>
    /// </summary>
    /// <param name="rangeStartingTime">The range starting time in millis.</param>
    /// <param name="rangeEndingTime">The range ending time in millis.</param>
    public DailyCalendar(string rangeStartingTime, string rangeEndingTime)
    {
        SetTimeRange(rangeStartingTime, rangeEndingTime);
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar"/> with a time range defined by the
    /// specified strings and the specified baseCalendar.
    /// <paramref name="rangeStartingTime"/> and <paramref name="rangeEndingTime"/>
    /// must be in the format "HH:MM[:SS[:mmm]]" where:
    /// <ul>
    /// 		<li>
    /// HH is the hour of the specified time. The hour should be
    /// specified using military (24-hour) time and must be in the range
    /// 0 to 23.
    /// </li>
    /// 		<li>
    /// MM is the minute of the specified time and must be in the range
    /// 0 to 59.
    /// </li>
    /// 		<li>
    /// SS is the second of the specified time and must be in the range
    /// 0 to 59.
    /// </li>
    /// 		<li>
    /// mmm is the millisecond of the specified time and must be in the
    /// range 0 to 999.
    /// </li>
    /// 		<li>
    /// items enclosed in brackets ('[', ']') are optional.
    /// </li>
    /// 		<li>
    /// The time range starting time must be before the time range ending
    /// time. Note this means that a time range may not cross daily
    /// boundaries (10PM - 2AM)
    /// </li>
    /// 	</ul>
    /// </summary>
    /// <param name="baseCalendar">The base calendar for this calendar instance see BaseCalendar for more
    /// information on base calendar functionality.</param>
    /// <param name="rangeStartingTime">The range starting time in millis.</param>
    /// <param name="rangeEndingTime">The range ending time in millis.</param>
    public DailyCalendar(ICalendar? baseCalendar, string rangeStartingTime, string rangeEndingTime) : base(baseCalendar)
    {
        SetTimeRange(rangeStartingTime, rangeEndingTime);
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar" /> with a time range defined by the
    /// specified values and no baseCalendar. Values are subject to
    /// the following validations:
    /// <ul>
    ///     <li>
    ///         Hours must be in the range 0-23 and are expressed using military
    ///		    (24-hour) time.
    ///     </li>
    ///		<li>Minutes must be in the range 0-59</li>
    ///		<li>Seconds must be in the range 0-59</li>
    ///		<li>Milliseconds must be in the range 0-999</li>
    ///		<li>
    ///         The time range starting time must be before the time range ending
    ///		    time. Note this means that a time range may not cross daily
    ///		    boundaries (10PM - 2AM)
    ///     </li>
    /// </ul>
    /// </summary>
    /// <param name="rangeStartingHourOfDay">The range starting hour of day.</param>
    /// <param name="rangeStartingMinute">The range starting minute.</param>
    /// <param name="rangeStartingSecond">The range starting second.</param>
    /// <param name="rangeStartingMillis">The range starting millis.</param>
    /// <param name="rangeEndingHourOfDay">The range ending hour of day.</param>
    /// <param name="rangeEndingMinute">The range ending minute.</param>
    /// <param name="rangeEndingSecond">The range ending second.</param>
    /// <param name="rangeEndingMillis">The range ending millis.</param>
    public DailyCalendar(int rangeStartingHourOfDay,
        int rangeStartingMinute,
        int rangeStartingSecond,
        int rangeStartingMillis,
        int rangeEndingHourOfDay,
        int rangeEndingMinute,
        int rangeEndingSecond,
        int rangeEndingMillis)
    {
        SetTimeRange(rangeStartingHourOfDay,
            rangeStartingMinute,
            rangeStartingSecond,
            rangeStartingMillis,
            rangeEndingHourOfDay,
            rangeEndingMinute,
            rangeEndingSecond,
            rangeEndingMillis);
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar"/> with a time range defined by the
    /// specified values and the specified <paramref name="baseCalendar"/>. Values are
    /// subject to the following validations:
    /// <ul>
    /// 		<li>
    /// Hours must be in the range 0-23 and are expressed using military
    /// (24-hour) time.
    /// </li>
    /// 		<li>Minutes must be in the range 0-59</li>
    /// 		<li>Seconds must be in the range 0-59</li>
    /// 		<li>Milliseconds must be in the range 0-999</li>
    /// 		<li>
    /// The time range starting time must be before the time range ending
    /// time. Note this means that a time range may not cross daily
    /// boundaries (10PM - 2AM)
    /// </li>
    /// 	</ul>
    /// </summary>
    /// <param name="baseCalendar">The base calendar for this calendar instance see BaseCalendar for more
    /// information on base calendar functionality.</param>
    /// <param name="rangeStartingHourOfDay">The range starting hour of day.</param>
    /// <param name="rangeStartingMinute">The range starting minute.</param>
    /// <param name="rangeStartingSecond">The range starting second.</param>
    /// <param name="rangeStartingMillis">The range starting millis.</param>
    /// <param name="rangeEndingHourOfDay">The range ending hour of day.</param>
    /// <param name="rangeEndingMinute">The range ending minute.</param>
    /// <param name="rangeEndingSecond">The range ending second.</param>
    /// <param name="rangeEndingMillis">The range ending millis.</param>
    public DailyCalendar(ICalendar baseCalendar,
        int rangeStartingHourOfDay,
        int rangeStartingMinute,
        int rangeStartingSecond,
        int rangeStartingMillis,
        int rangeEndingHourOfDay,
        int rangeEndingMinute,
        int rangeEndingSecond,
        int rangeEndingMillis) : base(baseCalendar)
    {
        SetTimeRange(rangeStartingHourOfDay,
            rangeStartingMinute,
            rangeStartingSecond,
            rangeStartingMillis,
            rangeEndingHourOfDay,
            rangeEndingMinute,
            rangeEndingSecond,
            rangeEndingMillis);
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar" /> with a time range defined by the
    ///	specified <see cref="DateTime" />s and no
    ///	baseCalendar. The Calendars are subject to the following
    ///	considerations:
    ///	<ul>
    ///     <li>
    ///         Only the time-of-day fields of the specified Calendars will be
    ///		    used (the date fields will be ignored)
    ///     </li>
    ///		<li>
    ///         The starting time must be before the ending time of the defined
    ///		    time range. Note this means that a time range may not cross
    ///		    daily boundaries (10PM - 2AM). <i>(because only time fields are
    ///		    are used, it is possible for two Calendars to represent a valid
    ///		    time range and
    ///		    <c>rangeStartingCalendar.after(rangeEndingCalendar) ==  true</c>)
    ///			</i>
    ///     </li>
    /// </ul>
    /// </summary>
    /// <param name="rangeStartingCalendarUtc">The range starting calendar.</param>
    /// <param name="rangeEndingCalendarUtc">The range ending calendar.</param>
    public DailyCalendar(DateTime rangeStartingCalendarUtc, DateTime rangeEndingCalendarUtc)
    {
        SetTimeRange(rangeStartingCalendarUtc, rangeEndingCalendarUtc);
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar"/> with a time range defined by the
    /// specified <see cref="DateTime"/>s and the specified
    /// <paramref name="baseCalendar"/>. The Calendars are subject to the following
    /// considerations:
    /// <ul>
    /// 		<li>
    /// Only the time-of-day fields of the specified Calendars will be
    /// used (the date fields will be ignored)
    /// </li>
    /// 		<li>
    /// The starting time must be before the ending time of the defined
    /// time range. Note this means that a time range may not cross
    /// daily boundaries (10PM - 2AM). <i>(because only time fields are
    /// are used, it is possible for two Calendars to represent a valid
    /// time range and
    /// <c>rangeStartingCalendarUtc > rangeEndingCalendarUtc == true</c>)</i>
    /// 		</li>
    /// 	</ul>
    /// </summary>
    /// <param name="baseCalendar">The base calendar for this calendar instance see BaseCalendar for more
    /// information on base calendar functionality.</param>
    /// <param name="rangeStartingCalendarUtc">The range starting calendar.</param>
    /// <param name="rangeEndingCalendarUtc">The range ending calendar.</param>
    public DailyCalendar(ICalendar baseCalendar,
        DateTime rangeStartingCalendarUtc,
        DateTime rangeEndingCalendarUtc) : base(baseCalendar)
    {
        SetTimeRange(rangeStartingCalendarUtc, rangeEndingCalendarUtc);
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar" /> with a time range defined by the
    /// specified values and no baseCalendar. The values are
    ///	subject to the following considerations:
    ///	<ul>
    ///     <li>
    ///         Only the time-of-day portion of the specified values will be
    ///		    used
    ///     </li>
    ///		<li>
    ///         The starting time must be before the ending time of the defined
    ///		    time range. Note this means that a time range may not cross
    ///		    daily boundaries (10PM - 2AM). <i>(because only time value are
    ///		    are used, it is possible for the two values to represent a valid
    ///		    time range and <c>rangeStartingTime &gt; rangeEndingTime</c>)</i>
    ///     </li>
    /// </ul>
    /// </summary>
    /// <param name="rangeStartingTimeInMillis">The range starting time in millis.</param>
    /// <param name="rangeEndingTimeInMillis">The range ending time in millis.</param>
    public DailyCalendar(long rangeStartingTimeInMillis, long rangeEndingTimeInMillis)
    {
        SetTimeRange(rangeStartingTimeInMillis, rangeEndingTimeInMillis);
    }

    /// <summary>
    /// Create a <see cref="DailyCalendar"/> with a time range defined by the
    /// specified values and the specified <paramref name="baseCalendar"/>. The values
    /// are subject to the following considerations:
    /// <ul>
    /// 		<li>
    /// Only the time-of-day portion of the specified values will be
    /// used
    /// </li>
    /// 		<li>
    /// The starting time must be before the ending time of the defined
    /// time range. Note this means that a time range may not cross
    /// daily boundaries (10PM - 2AM). <i>(because only time value are
    /// are used, it is possible for the two values to represent a valid
    /// time range and <c>rangeStartingTime &gt; rangeEndingTime</c>)</i>
    /// 		</li>
    /// 	</ul>
    /// </summary>
    /// <param name="baseCalendar">The base calendar for this calendar instance see BaseCalendar for more
    /// information on base calendar functionality.</param>
    /// <param name="rangeStartingTimeInMillis">The range starting time in millis.</param>
    /// <param name="rangeEndingTimeInMillis">The range ending time in millis.</param>
    public DailyCalendar(ICalendar baseCalendar,
        long rangeStartingTimeInMillis,
        long rangeEndingTimeInMillis) : base(baseCalendar)
    {
        SetTimeRange(rangeStartingTimeInMillis,
            rangeEndingTimeInMillis);
    }

    /// <summary>
    /// Serialization constructor.
    /// </summary>
    /// <param name="info"></param>
    /// <param name="context"></param>
    protected DailyCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
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
                rangeStartingHourOfDay = info.GetInt32("rangeStartingHourOfDay");
                rangeStartingMinute = info.GetInt32("rangeStartingMinute");
                rangeStartingSecond = info.GetInt32("rangeStartingSecond");
                rangeStartingMillis = info.GetInt32("rangeStartingMillis");

                rangeEndingHourOfDay = info.GetInt32("rangeEndingHourOfDay");
                rangeEndingMinute = info.GetInt32("rangeEndingMinute");
                rangeEndingSecond = info.GetInt32("rangeEndingSecond");
                rangeEndingMillis = info.GetInt32("rangeEndingMillis");

                InvertTimeRange = info.GetBoolean("invertTimeRange");
                break;
            default:
                throw new NotSupportedException("Unknown serialization version");
        }
    }

    [System.Security.SecurityCritical]
    public override void GetObjectData(SerializationInfo info, StreamingContext context)
    {
        base.GetObjectData(info, context);

        info.AddValue("version", 1);
        info.AddValue("rangeStartingHourOfDay", rangeStartingHourOfDay);
        info.AddValue("rangeStartingMinute", rangeStartingMinute);
        info.AddValue("rangeStartingSecond", rangeStartingSecond);
        info.AddValue("rangeStartingMillis", rangeStartingMillis);

        info.AddValue("rangeEndingHourOfDay", rangeEndingHourOfDay);
        info.AddValue("rangeEndingMinute", rangeEndingMinute);
        info.AddValue("rangeEndingSecond", rangeEndingSecond);
        info.AddValue("rangeEndingMillis", rangeEndingMillis);

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
        if (CalendarBase != null
            && CalendarBase.IsTimeIncluded(timeUtc) == false)
        {
            return false;
        }

        //Before we start, apply the correct timezone offsets.
        return IsInsideTheOpenPartOfTheDay(TimeZoneUtil.ConvertTime(timeUtc, TimeZone));
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
    /// offsets differed, and the walk then crept forward a step at a time through a stretch it had
    /// already been told was excluded - minutes of spinning for an answer months out of place
    /// (#3466).
    /// </remarks>
    /// <param name="timeUtc"></param>
    /// <returns></returns>
    /// <seealso cref="ICalendar.GetNextIncludedTimeUtc"/>
    public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
    {
        DateTimeOffset nextIncludedTime = timeUtc.AddMilliseconds(OneMillis);

        while (!IsTimeIncluded(nextIncludedTime))
        {
            DateTimeOffset candidate;
            if (!IsInsideTheOpenPartOfTheDay(TimeZoneUtil.ConvertTime(nextIncludedTime, TimeZone)))
            {
                // The calendar's own window is what holds this time back, and the window's own edges
                // say when it lets go, so jump there rather than testing what lies between.
                candidate = NextTimeThisCalendarIncludes(nextIncludedTime);
            }
            else if (CalendarBase != null &&
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
        DateTime date = TimeZoneUtil.ConvertTime(time, TimeZone).Date;

        while (true)
        {
            DateTimeOffset? included = FirstIncludedInstantOnLocalDate(date, time);
            if (included != null)
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
    private DateTimeOffset? FirstIncludedInstantOnLocalDate(DateTime date, DateTimeOffset after)
    {
        TimeZoneInfo timeZone = TimeZone;

        // The wall clock at which the day opens, and the one at which it closes again. An ordinary
        // calendar opens a millisecond past the window's end; an inverted one opens at its start.
        DateTime opens = InvertTimeRange
            ? RangeStartOnLocalDate(date)
            : RangeEndOnLocalDate(date).AddMilliseconds(OneMillis);

        DateTime closes = InvertTimeRange
            ? RangeEndOnLocalDate(date).AddMilliseconds(OneMillis)
            : RangeStartOnLocalDate(date);

        // The day's own first instant, which is neither always midnight nor always at the offset the
        // rest of the day carries.
        DateTimeOffset? earliest = EarliestIncluded(soFar: null, TimeZoneUtil.StartOfLocalDay(date, timeZone), after);

        // The instant the day opens at: the first instant the clock reads that edge or later. One
        // that happens twice resolves to the first of the two, that being the first instant the day
        // is open at, and one that never happens at all - the edge fell in a spring-forward gap - to
        // the instant the clocks moved.
        earliest = EarliestIncluded(earliest, TimeZoneUtil.FirstInstantAtOrAfterLocal(opens, timeZone), after);

        // ...and, when that wall clock happens twice, the second of the two, for a query that already
        // stands past the first.
        if (TimeZoneUtil.TryResolveSecondPass(opens, timeZone, out DateTimeOffset opensAgain))
        {
            earliest = EarliestIncluded(earliest, opensAgain, after);
        }

        // A fall-back does not merely repeat a wall clock, it takes the clock back into a part of the
        // day that was already over. That happens exactly when the edge the day closes at is inside
        // the repeated hour: the transition lands on the wall clock that hour began with, which is
        // before the closing edge and so back inside the open part of the day.
        if (TimeZoneUtil.TryGetAmbiguousWindow(closes, timeZone, out DateTime repeatedFrom, out _)
            && TimeZoneUtil.TryResolveSecondPass(repeatedFrom, timeZone, out DateTimeOffset clockGoesBack))
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
        if (candidate <= after || !IsInsideTheOpenPartOfTheDay(TimeZoneUtil.ConvertTime(candidate, TimeZone)))
        {
            return soFar;
        }

        return soFar == null || candidate < soFar.Value ? candidate : soFar;
    }

    /// <summary>
    /// The wall clock at which the time range starts on the given local date.
    /// </summary>
    private DateTime RangeStartOnLocalDate(DateTime date)
    {
        return new DateTime(date.Year, date.Month, date.Day,
            rangeStartingHourOfDay, rangeStartingMinute,
            rangeStartingSecond, rangeStartingMillis, DateTimeKind.Unspecified);
    }

    /// <summary>
    /// The wall clock at which the time range ends on the given local date.
    /// </summary>
    private DateTime RangeEndOnLocalDate(DateTime date)
    {
        return new DateTime(date.Year, date.Month, date.Day,
            rangeEndingHourOfDay, rangeEndingMinute,
            rangeEndingSecond, rangeEndingMillis, DateTimeKind.Unspecified);
    }

    public override ICalendar Clone()
    {
        var clone = new DailyCalendar(CalendarBase, RangeStartingTime, RangeEndingTime)
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
        DateTimeOffset timeInZone = TimeZoneUtil.ConvertTime(timeUtc, TimeZone);
        DateTimeOffset rangeStartingTime = new DateTimeOffset(timeInZone.Year, timeInZone.Month, timeInZone.Day,
            rangeStartingHourOfDay, rangeStartingMinute,
            rangeStartingSecond, rangeStartingMillis, timeInZone.Offset);
        return rangeStartingTime;
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
        DateTimeOffset timeInZone = TimeZoneUtil.ConvertTime(timeUtc, TimeZone);
        DateTimeOffset rangeEndingTime = new DateTimeOffset(timeInZone.Year, timeInZone.Month, timeInZone.Day,
            rangeEndingHourOfDay, rangeEndingMinute,
            rangeEndingSecond, rangeEndingMillis, timeInZone.Offset);
        return rangeEndingTime;
    }

    /// <summary>
    /// Indicates whether the time range represents an inverted time range (see
    /// class description).
    /// </summary>
    /// <value><c>true</c> if invert time range; otherwise, <c>false</c>.</value>
    public bool InvertTimeRange { get; set; }

    public string RangeStartingTime => FormatTimeRange(rangeStartingHourOfDay, rangeStartingMinute, rangeStartingSecond, rangeStartingMillis);
    public string RangeEndingTime => FormatTimeRange(rangeEndingHourOfDay, rangeEndingMinute, rangeEndingSecond, rangeEndingMillis);

    private static string FormatTimeRange(int hourOfDay, int minute, int seconds, int milliseconds)
    {
        return $"{hourOfDay.ToString(TwoDigitFormat, CultureInfo.InvariantCulture)}:{minute.ToString(TwoDigitFormat, CultureInfo.InvariantCulture)}:{seconds.ToString(TwoDigitFormat, CultureInfo.InvariantCulture)}:{milliseconds.ToString(ThreeDigitFormat, CultureInfo.InvariantCulture)}";
    }

    /// <summary>
    /// Returns a <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </summary>
    /// <returns>
    /// A <see cref="T:System.String"></see> that represents the current <see cref="T:System.Object"></see>.
    /// </returns>
    public override string ToString()
    {
        StringBuilder buffer = new StringBuilder();
        buffer.Append("base calendar: [");
        if (CalendarBase != null)
        {
            buffer.Append(CalendarBase);
        }
        else
        {
            buffer.Append("null");
        }

        buffer.Append("], time range: '");
        buffer.Append(RangeStartingTime);
        buffer.Append(" - ");
        buffer.Append(RangeEndingTime);
        buffer.AppendFormat("', inverted: {0}", InvertTimeRange);
        return buffer.ToString();
    }

    /// <summary>
    /// Sets the time range for the <see cref="DailyCalendar" /> to the times
    /// represented in the specified Strings.
    /// </summary>
    /// <param name="rangeStartingTimeString">The range starting time string.</param>
    /// <param name="rangeEndingTimeString">The range ending time string.</param>
    public void SetTimeRange(string rangeStartingTimeString,
        string rangeEndingTimeString)
    {
        int rangeStartingSecond;
        int rangeStartingMillis;

        int rangeEndingSecond;
        int rangeEndingMillis;

        var rangeStartingTime = rangeStartingTimeString.Split(Colon);

        if (rangeStartingTime.Length < 2 || rangeStartingTime.Length > 4)
        {
            throw new ArgumentException($"Invalid time string '{rangeStartingTimeString}'");
        }

        int rangeStartingHourOfDay = Convert.ToInt32(rangeStartingTime[0], CultureInfo.InvariantCulture);
        int rangeStartingMinute = Convert.ToInt32(rangeStartingTime[1], CultureInfo.InvariantCulture);

        if (rangeStartingTime.Length > 2)
        {
            rangeStartingSecond = Convert.ToInt32(rangeStartingTime[2], CultureInfo.InvariantCulture);
        }
        else
        {
            rangeStartingSecond = 0;
        }
        if (rangeStartingTime.Length == 4)
        {
            rangeStartingMillis = Convert.ToInt32(rangeStartingTime[3], CultureInfo.InvariantCulture);
        }
        else
        {
            rangeStartingMillis = 0;
        }

        var rangeEndingTime = rangeEndingTimeString.Split(Colon);

        if (rangeEndingTime.Length < 2 || rangeEndingTime.Length > 4)
        {
            throw new ArgumentException($"Invalid time string '{rangeEndingTimeString}'");
        }

        int rangeEndingHourOfDay = Convert.ToInt32(rangeEndingTime[0], CultureInfo.InvariantCulture);
        int rangeEndingMinute = Convert.ToInt32(rangeEndingTime[1], CultureInfo.InvariantCulture);
        if (rangeEndingTime.Length > 2)
        {
            rangeEndingSecond = Convert.ToInt32(rangeEndingTime[2], CultureInfo.InvariantCulture);
        }
        else
        {
            rangeEndingSecond = 0;
        }
        if (rangeEndingTime.Length == 4)
        {
            rangeEndingMillis = Convert.ToInt32(rangeEndingTime[3], CultureInfo.InvariantCulture);
        }
        else
        {
            rangeEndingMillis = 0;
        }

        SetTimeRange(rangeStartingHourOfDay,
            rangeStartingMinute,
            rangeStartingSecond,
            rangeStartingMillis,
            rangeEndingHourOfDay,
            rangeEndingMinute,
            rangeEndingSecond,
            rangeEndingMillis);
    }

    /// <summary>
    /// Sets the time range for the <see cref="DailyCalendar" /> to the times
    /// represented in the specified values.
    /// </summary>
    /// <param name="rangeStartingHourOfDay">The range starting hour of day.</param>
    /// <param name="rangeStartingMinute">The range starting minute.</param>
    /// <param name="rangeStartingSecond">The range starting second.</param>
    /// <param name="rangeStartingMillis">The range starting millis.</param>
    /// <param name="rangeEndingHourOfDay">The range ending hour of day.</param>
    /// <param name="rangeEndingMinute">The range ending minute.</param>
    /// <param name="rangeEndingSecond">The range ending second.</param>
    /// <param name="rangeEndingMillis">The range ending millis.</param>
    public void SetTimeRange(int rangeStartingHourOfDay,
        int rangeStartingMinute,
        int rangeStartingSecond,
        int rangeStartingMillis,
        int rangeEndingHourOfDay,
        int rangeEndingMinute,
        int rangeEndingSecond,
        int rangeEndingMillis)
    {
        Validate(rangeStartingHourOfDay,
            rangeStartingMinute,
            rangeStartingSecond,
            rangeStartingMillis);

        Validate(rangeEndingHourOfDay,
            rangeEndingMinute,
            rangeEndingSecond,
            rangeEndingMillis);

        DateTimeOffset startCal = SystemTime.UtcNow();
        startCal =
            new DateTimeOffset(startCal.Year, startCal.Month, startCal.Day, rangeStartingHourOfDay, rangeStartingMinute,
                rangeStartingSecond, rangeStartingMillis, TimeSpan.Zero);

        DateTimeOffset endCal = SystemTime.UtcNow();
        endCal =
            new DateTimeOffset(endCal.Year, endCal.Month, endCal.Day, rangeEndingHourOfDay, rangeEndingMinute,
                rangeEndingSecond, rangeEndingMillis, TimeSpan.Zero);

        if (!(startCal < endCal))
        {
            throw new ArgumentException($"{InvalidTimeRange}{rangeStartingHourOfDay}:{rangeStartingMinute}:{rangeStartingSecond}:{rangeStartingMillis}{Separator}{rangeEndingHourOfDay}:{rangeEndingMinute}:{rangeEndingSecond}:{rangeEndingMillis}");
        }

        this.rangeStartingHourOfDay = rangeStartingHourOfDay;
        this.rangeStartingMinute = rangeStartingMinute;
        this.rangeStartingSecond = rangeStartingSecond;
        this.rangeStartingMillis = rangeStartingMillis;
        this.rangeEndingHourOfDay = rangeEndingHourOfDay;
        this.rangeEndingMinute = rangeEndingMinute;
        this.rangeEndingSecond = rangeEndingSecond;
        this.rangeEndingMillis = rangeEndingMillis;
    }

    /// <summary>
    /// Sets the time range for the <see cref="DailyCalendar" /> to the times
    /// represented in the specified <see cref="DateTime" />s.
    /// </summary>
    /// <param name="rangeStartingCalendarUtc">The range starting calendar.</param>
    /// <param name="rangeEndingCalendarUtc">The range ending calendar.</param>
    public void SetTimeRange(DateTime rangeStartingCalendarUtc,
        DateTime rangeEndingCalendarUtc)
    {
        SetTimeRange(
            rangeStartingCalendarUtc.Hour,
            rangeStartingCalendarUtc.Minute,
            rangeStartingCalendarUtc.Second,
            rangeStartingCalendarUtc.Millisecond,
            rangeEndingCalendarUtc.Hour,
            rangeEndingCalendarUtc.Minute,
            rangeEndingCalendarUtc.Second,
            rangeEndingCalendarUtc.Millisecond);
    }

    /// <summary>
    /// Sets the time range for the <see cref="DailyCalendar" /> to the times
    /// represented in the specified values.
    /// </summary>
    /// <param name="rangeStartingTime">The range starting time.</param>
    /// <param name="rangeEndingTime">The range ending time.</param>
    public void SetTimeRange(long rangeStartingTime,
        long rangeEndingTime)
    {
        SetTimeRange(new DateTime(rangeStartingTime), new DateTime(rangeEndingTime));
    }

    /// <summary>
    /// Gets the start of day, practically zeroes time part.
    /// </summary>
    /// <remarks>
    /// A wall-clock comparand, not an instant: it is midnight at whatever offset
    /// <paramref name="time" /> carries, which is the day's first instant only on a day whose offset
    /// never changes. That is all <see cref="IsInsideTheOpenPartOfTheDay" /> asks of it, since it
    /// compares it with a value carrying that same offset. Code that needs the instant a local day
    /// begins at uses <see cref="TimeZoneUtil.StartOfLocalDay" />.
    /// </remarks>
    /// <param name="time">The time, already expressed in the calendar's zone.</param>
    /// <returns></returns>
    private static DateTimeOffset GetStartOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(new DateTime(time.Year, time.Month, time.Day, 0, 0, 0, 0), time.Offset);
    }

    /// <summary>
    /// Gets the end of day, practically sets time parts to maximum allowed values.
    /// </summary>
    /// <remarks>
    /// A wall-clock comparand; see <see cref="GetStartOfDay" />.
    /// </remarks>
    /// <param name="time">The time, already expressed in the calendar's zone.</param>
    /// <returns></returns>
    private static DateTimeOffset GetEndOfDay(DateTimeOffset time)
    {
        return new DateTimeOffset(new DateTime(time.Year, time.Month, time.Day, 23, 59, 59, 999), time.Offset);
    }

    /// <summary>
    /// Checks the specified values for validity as a set of time values.
    /// </summary>
    /// <param name="hourOfDay">The hour of day.</param>
    /// <param name="minute">The minute.</param>
    /// <param name="second">The second.</param>
    /// <param name="millis">The millis.</param>
    private static void Validate(int hourOfDay, int minute, int second, int millis)
    {
        if (hourOfDay < 0 || hourOfDay > 23)
        {
            throw new ArgumentException(InvalidHourOfDay + hourOfDay);
        }
        if (minute < 0 || minute > 59)
        {
            throw new ArgumentException(InvalidMinute + minute);
        }
        if (second < 0 || second > 59)
        {
            throw new ArgumentException(InvalidSecond + second);
        }
        if (millis < 0 || millis > 999)
        {
            throw new ArgumentException(InvalidMillis + millis);
        }
    }

    public override int GetHashCode()
    {
        int baseHash = 0;
        if (CalendarBase != null)
            baseHash = CalendarBase.GetHashCode();

        return rangeStartingHourOfDay.GetHashCode() + rangeEndingHourOfDay.GetHashCode() +
               2*(rangeStartingMinute.GetHashCode() + rangeEndingMinute.GetHashCode()) +
               3*(rangeStartingSecond.GetHashCode() + rangeEndingSecond.GetHashCode()) +
               4*(rangeStartingMillis.GetHashCode() + rangeEndingMillis.GetHashCode())
               + 5*baseHash;
    }

    public bool Equals(DailyCalendar obj)
    {
        if (obj == null)
        {
            return false;
        }
        bool baseEqual = CalendarBase == null || CalendarBase.Equals(obj.CalendarBase);

        return baseEqual && InvertTimeRange == obj.InvertTimeRange &&
               rangeStartingHourOfDay == obj.rangeStartingHourOfDay &&
               rangeStartingMinute == obj.rangeStartingMinute &&
               rangeStartingSecond == obj.rangeStartingSecond &&
               rangeStartingMillis == obj.rangeStartingMillis &&
               rangeEndingHourOfDay == obj.rangeEndingHourOfDay &&
               rangeEndingMinute == obj.rangeEndingMinute &&
               rangeEndingSecond == obj.rangeEndingSecond &&
               rangeEndingMillis == obj.rangeEndingMillis;
    }

    public override bool Equals(object? obj)
    {
        if (!(obj is DailyCalendar))
            return false;
        return Equals((DailyCalendar) obj);
    }
}