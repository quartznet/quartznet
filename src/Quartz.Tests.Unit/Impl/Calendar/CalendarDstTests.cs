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
using System.Diagnostics;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <summary>
/// Calendars across daylight saving transitions. A calendar is a predicate over instants written in
/// wall-clock terms, so on a 23 or 25 hour day the wall clock it is written in and the elapsed time
/// it covers stop being the same thing - an hour of exclusion becomes two, or none at all.
/// </summary>
/// <remarks>
/// Every expectation here is a measured fact about the shipped behaviour, with the reasoning spelled
/// out beside it, so that a deliberate change shows up as a failing test rather than a silent shift.
/// The <see cref="TestTimeZones" /> Assume helpers state each transition premise, so a moved zone
/// skips a case instead of failing it.
/// </remarks>
public class CalendarDstTests
{
    /// <summary>
    /// An excluded window that covers a spring-forward gap excludes almost nothing: no instant on
    /// that day reads as a wall clock inside the gap, so the only instant the window can catch is the
    /// one that reads exactly as its (inclusive) end - the transition instant itself.
    /// </summary>
    /// <remarks>
    /// This is worth pinning because it is the opposite of the intuition. "Do not run between 03:00
    /// and 04:00" reads like an hour of quiet; on the day the clocks go forward it is a single
    /// instant of quiet, and a trigger due at any other point of that hour is not held back at all -
    /// there being no such point.
    /// </remarks>
    [TestCase("Helsinki", 3, 4, "2024-03-31T01:00:00Z", "2024-03-31 03:30")]
    [TestCase("Eastern", 2, 3, "2024-03-10T07:00:00Z", "2024-03-10 02:30")]
    public void DailyCalendar_WindowOverASpringForwardGap_ExcludesOnlyTheTransitionInstant(
        string zoneKey,
        int fromHour,
        int toHour,
        string transitionText,
        string gapLocalTime)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        TestTimeZones.AssumeInvalidLocalTime(zone, DateTime.Parse(gapLocalTime, System.Globalization.CultureInfo.InvariantCulture));

        DateTimeOffset transition = ParseUtc(transitionText);
        DailyCalendar calendar = new DailyCalendar(new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0)) { TimeZone = zone };

        calendar.IsTimeIncluded(transition.AddMilliseconds(-1)).Should().BeTrue(
            "the last instant before the transition reads as {0:HH:mm:ss.fff}, which is before the window opens",
            TimeZoneInfo.ConvertTime(transition.AddMilliseconds(-1), zone));

        calendar.IsTimeIncluded(transition).Should().BeFalse(
            "the transition instant reads as {0:HH:mm} local, which is the window's end - and the end is inclusive",
            TimeZoneInfo.ConvertTime(transition, zone));

        calendar.IsTimeIncluded(transition.AddMilliseconds(1)).Should().BeTrue(
            "one millisecond later the wall clock has already passed the end of the window");

        calendar.GetNextIncludedTimeUtc(transition).Should().Be(transition.AddMilliseconds(1),
            "the whole excluded stretch is one instant wide on this day, so the next included time is the very next millisecond");
    }

    /// <summary>
    /// The mirror image: an excluded window that covers a fall-back hour excludes it twice over,
    /// because the wall clock inside it happens twice. One hour of exclusion as written is two hours
    /// of elapsed time.
    /// </summary>
    [TestCase("Helsinki", 3, 4, "2024-10-27T00:00:00Z", "2024-10-27T02:00:00Z", "2024-10-27 03:30")]
    [TestCase("Eastern", 1, 2, "2024-11-03T05:00:00Z", "2024-11-03T07:00:00Z", "2024-11-03 01:30")]
    public void DailyCalendar_WindowOverAFallBackHour_ExcludesBothPasses(
        string zoneKey,
        int fromHour,
        int toHour,
        string windowOpensText,
        string windowClosesText,
        string ambiguousLocalTime)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        TestTimeZones.AssumeAmbiguousLocalTime(zone, DateTime.Parse(ambiguousLocalTime, System.Globalization.CultureInfo.InvariantCulture));

        DateTimeOffset windowOpens = ParseUtc(windowOpensText);
        DateTimeOffset windowCloses = ParseUtc(windowClosesText);

        DailyCalendar calendar = new DailyCalendar(new TimeOnly(fromHour, 0), new TimeOnly(toHour, 0)) { TimeZone = zone };

        DateTimeOffset firstPass = windowOpens.AddMinutes(30);
        DateTimeOffset secondPass = firstPass.AddHours(1);

        TimeZoneInfo.ConvertTime(secondPass, zone).DateTime.Should().Be(
            TimeZoneInfo.ConvertTime(firstPass, zone).DateTime,
            "the two passes are an hour of elapsed time apart and read as the same wall clock, which is what makes them ambiguous");

        calendar.IsTimeIncluded(windowOpens.AddMilliseconds(-1)).Should().BeTrue("the window has not opened yet");
        calendar.IsTimeIncluded(firstPass).Should().BeFalse("the daylight-time pass of the excluded hour is excluded");
        calendar.IsTimeIncluded(secondPass).Should().BeFalse("so is the standard-time pass, which reads as the same wall clock");
        calendar.IsTimeIncluded(windowCloses).Should().BeFalse("the end of the window is inclusive");
        calendar.IsTimeIncluded(windowCloses.AddMilliseconds(1)).Should().BeTrue("and one millisecond later the day is open again");

        calendar.GetNextIncludedTimeUtc(firstPass).Should().Be(windowCloses.AddMilliseconds(1),
            "an hour of wall clock is two hours of elapsed time on the fall-back day, so a trigger held back at {0:HH:mm} is held for both passes",
            TimeZoneInfo.ConvertTime(firstPass, zone));

        (windowCloses - windowOpens).Should().Be(TimeSpan.FromHours(2),
            "the hour written into the calendar covers two hours of the fall-back day");
    }

    /// <summary>
    /// A cron exclusion is wall-clock matching, so an expression that names an hour the
    /// spring-forward day does not have excludes exactly the one instant that hour collapsed onto:
    /// the moment the clocks moved.
    /// </summary>
    /// <remarks>
    /// <see cref="CronCalendar" /> answers through <see cref="CronExpression.IsSatisfiedBy" />, so
    /// it says whatever the expression says. Under the delta-shift rule that was "nothing on this
    /// day is excluded", while a trigger written with the same expression still fired — a calendar
    /// meant to hold a job back over the skipped hour held nothing back. The gap's end is the one
    /// instant every wall clock the gap swallowed names, so it is both when the trigger fires and
    /// when the calendar excludes, and the two agree again.
    /// </remarks>
    [Test]
    public void CronCalendar_ExclusionInsideTheSpringForwardGap_ExcludesTheInstantTheClocksMoved()
    {
        TimeZoneInfo zone = TestTimeZones.Helsinki;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2024, 3, 31, 3, 30, 0));

        // every second of local hour 3, which on 2024-03-31 in Helsinki is an hour that never happens
        CronCalendar calendar = new CronCalendar(null, "* * 3 * * ?", zone);

        DateTimeOffset transition = ParseUtc("2024-03-31T01:00:00Z");

        calendar.IsTimeIncluded(transition).Should().BeFalse(
            "the instant the clocks moved is the first instant the zone's clock reads past local 03:00, so that is where the excluded hour landed");

        for (int minute = -60; minute <= 60; minute++)
        {
            if (minute == 0)
            {
                continue;
            }

            DateTimeOffset instant = transition.AddMinutes(minute);
            calendar.IsTimeIncluded(instant).Should().BeTrue(
                "{0:O} reads as {1:HH:mm} local, and no other instant of this day reads as an hour that the day skipped",
                instant, TimeZoneInfo.ConvertTime(instant, zone));
        }

        // a cron match ignores milliseconds, so the whole of the transition second is excluded
        calendar.GetNextIncludedTimeUtc(transition).Should().Be(transition.AddSeconds(1));
    }

    /// <summary>
    /// The same expression on the fall-back day excludes two hours of elapsed time, for the same
    /// reason the daily calendar does.
    /// </summary>
    [Test]
    public void CronCalendar_ExclusionOverAFallBackHour_ExcludesBothPasses()
    {
        TimeZoneInfo zone = TestTimeZones.Helsinki;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2024, 10, 27, 3, 30, 0));

        CronCalendar calendar = new CronCalendar(null, "* * 3 * * ?", zone);

        DateTimeOffset firstPassStart = ParseUtc("2024-10-27T00:00:00Z");
        DateTimeOffset secondPassEnd = ParseUtc("2024-10-27T02:00:00Z");

        calendar.IsTimeIncluded(firstPassStart.AddSeconds(-1)).Should().BeTrue("local 02:59:59 is not in hour 3");
        calendar.IsTimeIncluded(firstPassStart).Should().BeFalse("local 03:00:00 +03:00 is");
        calendar.IsTimeIncluded(firstPassStart.AddMinutes(90)).Should().BeFalse("and so is local 03:30:00 +02:00, an hour of elapsed time later");
        calendar.IsTimeIncluded(secondPassEnd).Should().BeTrue("local 04:00:00 +02:00 is past the excluded hour, both times over");

        calendar.GetNextIncludedTimeUtc(firstPassStart.AddMinutes(30)).Should().Be(secondPassEnd,
            "a trigger held back inside the repeated hour waits out both passes of it");
    }

    /// <summary>
    /// A holiday excludes a local day, and Santiago's local days are not all 24 hours long: the
    /// clocks move at midnight there, so the spring-forward day begins at 01:00 and lasts 23 hours
    /// while the fall-back day lasts 25. Whichever it is, the holiday covers exactly the instants
    /// that read as that date.
    /// </summary>
    [TestCase("2019-09-08", "2019-09-08T04:00:00Z", "2019-09-09T03:00:00Z", 23)]
    [TestCase("2019-04-06", "2019-04-06T03:00:00Z", "2019-04-07T04:00:00Z", 25)]
    public void HolidayCalendar_OnATransitionDay_ExcludesTheWholeLocalDay(
        string holidayText,
        string dayStartsText,
        string dayEndsText,
        int hoursInTheDay)
    {
        TimeZoneInfo zone = TestTimeZones.Santiago;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2019, 9, 8, 0, 30, 0));
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2019, 4, 6, 23, 30, 0));

        DateOnly holiday = DateOnly.Parse(holidayText, System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset dayStarts = ParseUtc(dayStartsText);
        DateTimeOffset dayEnds = ParseUtc(dayEndsText);

        HolidayCalendar calendar = new HolidayCalendar { TimeZone = zone };
        calendar.AddExcludedDay(holiday);

        (dayEnds - dayStarts).Should().Be(TimeSpan.FromHours(hoursInTheDay),
            "the premise of the case: the two instants it names are the first of {0:yyyy-MM-dd} and the first of the day after, and they are {1} hours apart in {2}",
            holiday, hoursInTheDay, zone.Id);

        // A minute rather than a millisecond, because the exact instant a zone changes offset is not
        // agreed on to the millisecond: Windows cannot express a rule at local midnight and writes
        // 23:59:59.999 of the day before instead, so on Windows data this local day begins one
        // millisecond earlier than IANA data says it does. A minute is clear of the disagreement.
        calendar.IsTimeIncluded(dayStarts.AddMinutes(-1)).Should().BeTrue(
            "the minute before the holiday begins reads as {0:yyyy-MM-dd HH:mm}, which is the day before",
            TimeZoneInfo.ConvertTime(dayStarts.AddMinutes(-1), zone));

        calendar.IsTimeIncluded(dayStarts).Should().BeFalse(
            "the holiday's own first instant reads as {0:yyyy-MM-dd HH:mm}, and a holiday excludes its whole local day however that day begins",
            TimeZoneInfo.ConvertTime(dayStarts, zone));

        calendar.IsTimeIncluded(dayEnds.AddMinutes(-1)).Should().BeFalse("the last minute of the local day is still the holiday");
        calendar.IsTimeIncluded(dayEnds).Should().BeTrue("and the first instant of the next local day is not");

        // spot-check the middle, so that the assertion is about a day rather than about four instants
        for (int hour = 1; hour < hoursInTheDay; hour++)
        {
            calendar.IsTimeIncluded(dayStarts.AddHours(hour)).Should().BeFalse(
                "hour {0} of the holiday reads as {1:yyyy-MM-dd HH:mm} and is part of it",
                hour, TimeZoneInfo.ConvertTime(dayStarts.AddHours(hour), zone));
        }
    }

    /// <summary>
    /// The other half of the calendar contract on the same day: <c>GetNextIncludedTimeUtc</c> has to
    /// answer with a time the calendar includes, and with the first one.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It did not, on any day whose clocks move (#3457). The day boundary was built as
    /// <c>new DateTimeOffset(local.Date, local.Offset)</c> - midnight at the offset the
    /// <em>queried instant</em> carries - which is a different instant from the day's own first one
    /// whenever the offset changed between the two: an hour out in a zone that moves its clocks at
    /// midnight, and on the neighbouring local day in a zone that moves them later.
    /// </para>
    /// <para>
    /// The two Santiago cases are the ones #3455 found and left inconclusive, because an answer the
    /// calendar itself calls excluded was not a behaviour to bless. The rest say the same thing for
    /// the transition shapes the other zones carry.
    /// </para>
    /// </remarks>
    [TestCase("SantiagoSpring", "2019-09-08", "2019-09-08T10:00:00Z", "2019-09-09T03:00:00Z")]
    [TestCase("SantiagoFall", "2019-04-06", "2019-04-07T02:30:00Z", "2019-04-07T04:00:00Z")]
    [TestCase("AmmanSpring", "2017-03-31", "2017-03-31T10:00:00Z", "2017-03-31T21:00:00Z")]
    [TestCase("AmmanFall", "2017-10-26", "2017-10-26T10:00:00Z", "2017-10-26T21:00:00Z")]
    [TestCase("HelsinkiSpring", "2024-03-31", "2024-03-31T10:00:00Z", "2024-03-31T21:00:00Z")]
    [TestCase("HelsinkiFall", "2024-10-27", "2024-10-27T10:00:00Z", "2024-10-27T22:00:00Z")]
    [TestCase("EasternSpring", "2024-03-10", "2024-03-10T16:00:00Z", "2024-03-11T04:00:00Z")]
    [TestCase("EasternFall", "2024-11-03", "2024-11-03T16:00:00Z", "2024-11-04T05:00:00Z")]
    public void HolidayCalendar_NextIncludedTime_OnATransitionDay(string dayKey, string holidayText, string askedAtText, string expectedText)
    {
        TimeZoneInfo zone = ZoneForTransitionDay(dayKey);

        DateOnly holiday = DateOnly.Parse(holidayText, System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset askedAt = ParseUtc(askedAtText);
        DateTimeOffset expected = ParseUtc(expectedText);

        HolidayCalendar calendar = new HolidayCalendar { TimeZone = zone };
        calendar.AddExcludedDay(holiday);

        AssertNextIncludedTime(calendar, zone, askedAt, expected);
    }

    /// <summary>
    /// The control for the case above: on a day with no transition in it the answer is plain local
    /// midnight, in each of the zones the transition cases use.
    /// </summary>
    [TestCase("Helsinki", "2024-06-15", "2024-06-15T10:00:00Z", "2024-06-15T21:00:00Z")]
    [TestCase("Eastern", "2024-01-15", "2024-01-15T16:00:00Z", "2024-01-16T05:00:00Z")]
    [TestCase("Santiago", "2019-06-15", "2019-06-15T15:00:00Z", "2019-06-16T04:00:00Z")]
    public void HolidayCalendar_NextIncludedTime_OnAnOrdinaryDay(string zoneKey, string holidayText, string askedAtText, string expectedText)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);

        DateOnly holiday = DateOnly.Parse(holidayText, System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset askedAt = ParseUtc(askedAtText);
        DateTimeOffset expected = ParseUtc(expectedText);

        Assume.That(
            zone.GetUtcOffset(TimeZoneInfo.ConvertTime(askedAt, zone).Date) == zone.GetUtcOffset(expected),
            $"test premise: the clocks do not move on {holiday:yyyy-MM-dd} in zone {zone.Id}");

        HolidayCalendar calendar = new HolidayCalendar { TimeZone = zone };
        calendar.AddExcludedDay(holiday);

        AssertNextIncludedTime(calendar, zone, askedAt, expected);
    }

    /// <summary>
    /// A run of holidays is walked a day at a time, and the walk crosses the transition rather than
    /// starting on it. Adding a day to a <see cref="DateTimeOffset" /> keeps the offset it already
    /// had, so a walk that begins before the clocks move carries the old offset over them and lands
    /// an hour out on the far side: a holiday that ends on a Sunday releases the scheduler at 01:00
    /// on the Monday when the clocks went forward, and an hour before the holiday is over when they
    /// went back.
    /// </summary>
    [TestCase("HelsinkiSpring", "2024-03-29,2024-03-30,2024-03-31", "2024-03-29T10:00:00Z", "2024-03-31T21:00:00Z")]
    [TestCase("HelsinkiFall", "2024-10-25,2024-10-26,2024-10-27", "2024-10-25T10:00:00Z", "2024-10-27T22:00:00Z")]
    [TestCase("SantiagoSpring", "2019-09-06,2019-09-07,2019-09-08", "2019-09-06T15:00:00Z", "2019-09-09T03:00:00Z")]
    [TestCase("SantiagoFall", "2019-04-04,2019-04-05,2019-04-06", "2019-04-04T15:00:00Z", "2019-04-07T04:00:00Z")]
    public void HolidayCalendar_NextIncludedTime_WalkingAcrossATransition(string dayKey, string holidaysText, string askedAtText, string expectedText)
    {
        TimeZoneInfo zone = ZoneForTransitionDay(dayKey);

        DateTimeOffset askedAt = ParseUtc(askedAtText);
        DateTimeOffset expected = ParseUtc(expectedText);

        HolidayCalendar calendar = new HolidayCalendar { TimeZone = zone };
        foreach (string holidayText in holidaysText.Split(','))
        {
            calendar.AddExcludedDay(DateOnly.Parse(holidayText, System.Globalization.CultureInfo.InvariantCulture));
        }

        AssertNextIncludedTime(calendar, zone, askedAt, expected);
    }

    /// <summary>
    /// An annual calendar excludes a date of every year, and walks days the same way a holiday
    /// calendar does, so it had the same boundary.
    /// </summary>
    [TestCase("SantiagoSpring", 9, 8, "2019-09-08T10:00:00Z", "2019-09-09T03:00:00Z")]
    [TestCase("SantiagoFall", 4, 6, "2019-04-06T12:00:00Z", "2019-04-07T04:00:00Z")]
    public void AnnualCalendar_NextIncludedTime_OnATransitionDay(string dayKey, int month, int day, string askedAtText, string expectedText)
    {
        TimeZoneInfo zone = ZoneForTransitionDay(dayKey);

        AnnualCalendar calendar = new AnnualCalendar { TimeZone = zone };
        calendar.AddExcludedDay(new MonthDay(month, day));

        AssertNextIncludedTime(calendar, zone, ParseUtc(askedAtText), ParseUtc(expectedText));
    }

    /// <summary>
    /// So did a monthly calendar, which excludes a day of the month.
    /// </summary>
    [TestCase("SantiagoSpring", 8, "2019-09-08T10:00:00Z", "2019-09-09T03:00:00Z")]
    [TestCase("SantiagoFall", 6, "2019-04-06T12:00:00Z", "2019-04-07T04:00:00Z")]
    public void MonthlyCalendar_NextIncludedTime_OnATransitionDay(string dayKey, int day, string askedAtText, string expectedText)
    {
        TimeZoneInfo zone = ZoneForTransitionDay(dayKey);

        MonthlyCalendar calendar = new MonthlyCalendar { TimeZone = zone };
        calendar.AddExcludedDay(day);

        AssertNextIncludedTime(calendar, zone, ParseUtc(askedAtText), ParseUtc(expectedText));
    }

    /// <summary>
    /// And so did a weekly calendar, which excludes a day of the week.
    /// </summary>
    [TestCase("SantiagoSpring", DayOfWeek.Sunday, "2019-09-08T10:00:00Z", "2019-09-09T03:00:00Z")]
    [TestCase("SantiagoFall", DayOfWeek.Saturday, "2019-04-06T12:00:00Z", "2019-04-07T04:00:00Z")]
    public void WeeklyCalendar_NextIncludedTime_OnATransitionDay(string dayKey, DayOfWeek excluded, string askedAtText, string expectedText)
    {
        TimeZoneInfo zone = ZoneForTransitionDay(dayKey);

        WeeklyCalendar calendar = new WeeklyCalendar { TimeZone = zone };
        calendar.RemoveExcludedDay(DayOfWeek.Saturday);
        calendar.RemoveExcludedDay(DayOfWeek.Sunday);
        calendar.AddExcludedDay(excluded);

        AssertNextIncludedTime(calendar, zone, ParseUtc(askedAtText), ParseUtc(expectedText));
    }

    /// <summary>
    /// A daily calendar's day boundary was already right on these days, and this says why. It builds
    /// its day boundaries at the queried instant's offset too, but compares them only with values
    /// carrying that same offset, so every comparison it makes is a wall-clock one and stays correct
    /// whatever the offset is. The window therefore lands where the wall clock says even on the day
    /// whose midnight never happens, and on the day whose last hour happens twice it catches both
    /// passes.
    /// </summary>
    /// <remarks>
    /// These are <see cref="ICalendar.IsTimeIncluded" /> cases only.
    /// <see cref="DailyCalendar.GetNextIncludedTimeUtc" /> had a defect of its own, which the cases
    /// below this one are about: it computed the window and the day boundaries in the offset the
    /// <em>argument</em> carried rather than in the calendar's zone (#3466).
    /// </remarks>
    [TestCase("SantiagoSpring", "01:00", "02:00", "2019-09-08T03:30:00Z", true)]
    [TestCase("SantiagoSpring", "01:00", "02:00", "2019-09-08T04:30:00Z", false)]
    [TestCase("SantiagoSpring", "01:00", "02:00", "2019-09-08T05:30:00Z", true)]
    [TestCase("SantiagoFall", "23:00", "23:30", "2019-04-07T02:15:00Z", false)]
    [TestCase("SantiagoFall", "23:00", "23:30", "2019-04-07T02:45:00Z", true)]
    [TestCase("SantiagoFall", "23:00", "23:30", "2019-04-07T03:15:00Z", false)]
    [TestCase("SantiagoFall", "23:00", "23:30", "2019-04-07T03:45:00Z", true)]
    [TestCase("SantiagoFall", "23:00", "23:30", "2019-04-07T04:00:00Z", true)]
    public void DailyCalendar_OnADayThatIsNot24HoursLong_ExcludesTheWallClockWindow(
        string dayKey,
        string fromText,
        string toText,
        string instantText,
        bool expectedIncluded)
    {
        TimeZoneInfo zone = ZoneForTransitionDay(dayKey);

        TimeOnly from = TimeOnly.Parse(fromText, System.Globalization.CultureInfo.InvariantCulture);
        TimeOnly to = TimeOnly.Parse(toText, System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset instant = ParseUtc(instantText);

        DailyCalendar calendar = new DailyCalendar(from, to) { TimeZone = zone };

        calendar.IsTimeIncluded(instant).Should().Be(expectedIncluded,
            "{0:O} reads as {1:yyyy-MM-dd HH:mm zzz} local and the excluded window is {2:HH:mm}-{3:HH:mm} of every local day",
            instant, TimeZoneInfo.ConvertTime(instant, zone), from, to);
    }

    /// <summary>
    /// The other half of the daily calendar's contract, on the days where the wall clock and elapsed
    /// time part company: the next included time is the first instant the window lets go of, and it
    /// is the same instant however the question is phrased.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It was not (#3466). The window's edges were named on the date the <em>argument</em> fell on
    /// and paired with the offset it carried, while <see cref="ICalendar.IsTimeIncluded" /> converted
    /// into the calendar's zone first, so a question asked in UTC about a calendar keeping another
    /// zone's hours was answered against a window an offset away from the one being tested. The walk
    /// then fell through to its last resort, a millisecond at a time, and could step clean over an
    /// included stretch on the way.
    /// </para>
    /// <para>
    /// Each case states the answer it expects, and
    /// <see cref="AssertDailyCalendarNextIncludedTime" /> holds it to the rest of the contract:
    /// the answer is included, nothing between the question and the answer is, and asking in the
    /// calendar's own zone rather than in UTC gives the same instant back.
    /// </para>
    /// </remarks>
    // The reproduction from #3466: this one spun for some two and a half minutes and answered five
    // months late, because 21:00-22:00 read as UTC is never 21:00-22:00 read in Santiago.
    [TestCase("SantiagoFall", "21:00", "22:00", true, "2019-04-07T01:30:00Z", "2019-04-08T01:00:00Z")]
    // The window's edges are inside the hour that happens twice, so the day opens twice as well: once
    // on each pass, and a question standing in the second pass is answered from the second.
    [TestCase("SantiagoFall", "23:00", "23:30", false, "2019-04-07T02:15:00Z", "2019-04-07T02:30:00.001Z")]
    [TestCase("SantiagoFall", "23:00", "23:30", false, "2019-04-07T03:15:00Z", "2019-04-07T03:30:00.001Z")]
    // A window that runs to the end of the local day opens again at the next local midnight, which on
    // the 25 hour day is two hours further off than the wall clock makes it look...
    [TestCase("SantiagoFall", "22:00", "23:59:59.999", false, "2019-04-07T01:30:00Z", "2019-04-07T04:00:00Z")]
    // ...and on the 23 hour day is a midnight that never happens, so the day opens at the end of the
    // gap instead.
    [TestCase("SantiagoSpring", "22:00", "23:59:59.999", false, "2019-09-08T02:00:00Z", "2019-09-08T04:00:00Z")]
    // An inverted window that begins at that same missing midnight is one instant wide: only its
    // inclusive end survives the gap.
    [TestCase("SantiagoSpring", "00:00", "01:00", true, "2019-09-07T20:00:00Z", "2019-09-08T04:00:00Z")]
    // The clock going back does not only repeat an hour, it takes the day back to before the window
    // opened, so the calendar lets go at the transition itself rather than at the window's end.
    [TestCase("HelsinkiFall", "03:30", "04:30", false, "2024-10-27T00:40:00Z", "2024-10-27T01:00:00Z")]
    [TestCase("HelsinkiFall", "03:00", "03:15", true, "2024-10-27T00:20:00Z", "2024-10-27T01:00:00Z")]
    // An edge inside the spring-forward gap is no instant at all; the day turns over at the end of
    // the gap, not an hour past it.
    [TestCase("HelsinkiSpring", "02:30", "03:30", false, "2024-03-31T00:45:00Z", "2024-03-31T01:00:00Z")]
    [TestCase("HelsinkiSpring", "03:30", "04:30", true, "2024-03-31T00:30:00Z", "2024-03-31T01:00:00Z")]
    // The controls: the same calendars on a day whose clocks do not move.
    [TestCase("Santiago", "21:00", "22:00", true, "2019-06-15T22:00:00Z", "2019-06-16T01:00:00Z")]
    [TestCase("Helsinki", "03:30", "04:30", false, "2024-06-15T01:00:00Z", "2024-06-15T01:30:00.001Z")]
    public void DailyCalendar_NextIncludedTime_IsTheFirstInstantTheWindowLetsGoOf(
        string zoneKey,
        string fromText,
        string toText,
        bool inverted,
        string askedAtText,
        string expectedText)
    {
        TimeZoneInfo zone = ZoneForCase(zoneKey);

        DailyCalendar calendar = new DailyCalendar(
            TimeOnly.Parse(fromText, System.Globalization.CultureInfo.InvariantCulture),
            TimeOnly.Parse(toText, System.Globalization.CultureInfo.InvariantCulture))
        {
            InvertTimeRange = inverted,
            TimeZone = zone
        };

        AssertDailyCalendarNextIncludedTime(calendar, zone, ParseUtc(askedAtText), ParseUtc(expectedText));
    }

    /// <summary>
    /// Asked about an instant it already includes, a calendar answers with the very next millisecond
    /// - there being nothing to wait for. The days these are asked on are the awkward ones, because
    /// the shape of the answer must not depend on where in a transition the question lands.
    /// </summary>
    [TestCase("SantiagoFall", "21:00", "22:00", true, "2019-04-08T01:30:00Z")]
    [TestCase("HelsinkiFall", "03:30", "04:30", false, "2024-10-27T03:00:00Z")]
    [TestCase("SantiagoSpring", "22:00", "23:59:59.999", false, "2019-09-08T04:00:00Z")]
    public void DailyCalendar_NextIncludedTime_WhenTheDayIsAlreadyOpen_IsTheNextMillisecond(
        string zoneKey,
        string fromText,
        string toText,
        bool inverted,
        string askedAtText)
    {
        TimeZoneInfo zone = ZoneForCase(zoneKey);
        DateTimeOffset askedAt = ParseUtc(askedAtText);

        DailyCalendar calendar = new DailyCalendar(
            TimeOnly.Parse(fromText, System.Globalization.CultureInfo.InvariantCulture),
            TimeOnly.Parse(toText, System.Globalization.CultureInfo.InvariantCulture))
        {
            InvertTimeRange = inverted,
            TimeZone = zone
        };

        calendar.IsTimeIncluded(askedAt).Should().BeTrue(
            "the premise of the case: {0:O} reads as {1:yyyy-MM-dd HH:mm zzz} local, which this calendar includes",
            askedAt, TimeZoneInfo.ConvertTime(askedAt, zone));

        calendar.GetNextIncludedTimeUtc(askedAt).Should().Be(askedAt.AddMilliseconds(1),
            "the next included time after an included instant is the one right after it");
    }

    /// <summary>
    /// The measured symptom of #3466, which is what made it a bug worth a number rather than an
    /// inaccuracy: the answer was not merely wrong, it was arrived at a millisecond at a time.
    /// </summary>
    /// <remarks>
    /// Two bounds, because each says something the other does not. The call count is exact and does
    /// not care how fast the machine is: a base calendar that includes everything is asked once per
    /// pass of the loop, so counting its calls counts the passes. The elapsed time is what a user
    /// noticed, and its bound is loose enough that only a walk could break it - the run this replaced
    /// took about 150 seconds.
    /// </remarks>
    [Test]
    public void DailyCalendar_NextIncludedTime_IsJumpedToRatherThanWalkedUpTo()
    {
        TimeZoneInfo zone = ZoneForTransitionDay("SantiagoFall");

        CountingCalendar passes = new CountingCalendar();
        DailyCalendar calendar = new DailyCalendar(new TimeOnly(21, 0), new TimeOnly(22, 0), passes)
        {
            InvertTimeRange = true,
            TimeZone = zone
        };

        DateTimeOffset askedAt = ParseUtc("2019-04-07T01:30:00Z");

        Stopwatch stopwatch = Stopwatch.StartNew();
        DateTimeOffset actual = calendar.GetNextIncludedTimeUtc(askedAt);
        stopwatch.Stop();

        actual.Should().Be(ParseUtc("2019-04-08T01:00:00Z"),
            "the window is 21:00-22:00 in Santiago, and the first of those hours after {0:O} begins at {1:O}",
            askedAt, ParseUtc("2019-04-08T01:00:00Z"));

        passes.Calls.Should().BeLessThan(10,
            "the answer is named from the window's own edges, where the walk it replaced tested every one of the 84.6 million milliseconds in between");

        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5),
            "a bound no jump can miss and no millisecond walk can meet");
    }

    /// <summary>
    /// A base calendar that includes every instant and counts the times it was asked. Attaching one
    /// counts the passes <see cref="DailyCalendar.GetNextIncludedTimeUtc" /> makes without the
    /// calendar having to carry a counter of its own, since the loop asks its base calendar once per
    /// pass and this one never changes the answer.
    /// </summary>
    private sealed class CountingCalendar : BaseCalendar
    {
        public int Calls { get; private set; }

        public override bool IsTimeIncluded(DateTimeOffset timeStampUtc)
        {
            Calls++;
            return true;
        }
    }

    /// <summary>
    /// Asserts the whole of the daily calendar's <c>GetNextIncludedTimeUtc</c> contract for one case:
    /// the instant asked about is excluded, the answer is the expected one, the answer is included,
    /// nothing between the two is, and the same question asked in the calendar's own zone rather than
    /// in UTC is answered with the same instant.
    /// </summary>
    private static void AssertDailyCalendarNextIncludedTime(
        DailyCalendar calendar,
        TimeZoneInfo zone,
        DateTimeOffset askedAt,
        DateTimeOffset expected)
    {
        calendar.IsTimeIncluded(askedAt).Should().BeFalse(
            "the premise of the case: {0:O} reads as {1:yyyy-MM-dd HH:mm:ss.fff zzz} local, which this calendar excludes",
            askedAt, TimeZoneInfo.ConvertTime(askedAt, zone));

        calendar.IsTimeIncluded(expected).Should().BeTrue(
            "and {0:O}, which reads as {1:yyyy-MM-dd HH:mm:ss.fff zzz} local, is not excluded",
            expected, TimeZoneInfo.ConvertTime(expected, zone));

        // How close to the answer the walk may come. Usually the millisecond before it, but where the
        // answer is the instant the clocks moved, only to the minute: the exact instant a zone
        // changes offset is not agreed on to the millisecond, because Windows cannot express a rule
        // at local midnight and writes 23:59:59.999 of the day before instead, which puts the end of
        // Santiago's spring-forward gap a millisecond earlier there than on IANA data.
        TimeSpan slack = zone.GetUtcOffset(expected) == zone.GetUtcOffset(expected.AddMinutes(-1))
            ? TimeSpan.FromMilliseconds(1)
            : TimeSpan.FromMinutes(1);

        calendar.IsTimeIncluded(expected - slack).Should().BeFalse(
            "and it is the first such instant - {0} earlier reads as {1:yyyy-MM-dd HH:mm:ss.fff zzz} local, which is still excluded",
            slack, TimeZoneInfo.ConvertTime(expected - slack, zone));

        AssertNothingIncludedBetween(calendar, zone, askedAt, expected - slack);

        DateTimeOffset askedInUtc = calendar.GetNextIncludedTimeUtc(askedAt);
        askedInUtc.Should().Be(expected,
            "the next included time after an excluded instant is the first instant the calendar includes, and {0:O} reads as {1:yyyy-MM-dd HH:mm:ss.fff zzz} local (included={2})",
            askedInUtc, TimeZoneInfo.ConvertTime(askedInUtc, zone), calendar.IsTimeIncluded(askedInUtc));

        DateTimeOffset askedInZone = calendar.GetNextIncludedTimeUtc(TimeZoneInfo.ConvertTime(askedAt, zone));
        askedInZone.Should().Be(expected,
            "the same instant asked about in the calendar's own zone rather than in UTC is the same question, so it has the same answer");
    }

    /// <summary>
    /// Walks the span between a question and its answer a second at a time, so that an answer which
    /// stepped over an included stretch fails rather than passes. A second rather than a millisecond
    /// because every window in this fixture opens and closes on a whole minute; how close to the
    /// answer the walk comes is the caller's business.
    /// </summary>
    private static void AssertNothingIncludedBetween(ICalendar calendar, TimeZoneInfo zone, DateTimeOffset after, DateTimeOffset until)
    {
        (until - after).Should().BeLessThan(TimeSpan.FromDays(2),
            "the scan below walks the whole span, so a case has to keep it short enough to walk");

        DateTimeOffset? included = null;
        for (DateTimeOffset instant = after.AddSeconds(1); instant <= until; instant = instant.AddSeconds(1))
        {
            if (calendar.IsTimeIncluded(instant))
            {
                included = instant;
                break;
            }
        }

        included.Should().BeNull(
            "nothing between {0:O} and the answer {1:O} may be included, or the answer is not the first one; this instant reads as {2} local",
            after,
            until,
            included is null ? "-" : TimeZoneInfo.ConvertTime(included.Value, zone).ToString("yyyy-MM-dd HH:mm:ss zzz", System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Asserts the whole of the <c>GetNextIncludedTimeUtc</c> contract for one case: the instant it
    /// is asked about is excluded, the expected answer is included, nothing between the two is, and
    /// the answer is that instant.
    /// </summary>
    private static void AssertNextIncludedTime(ICalendar calendar, TimeZoneInfo zone, DateTimeOffset askedAt, DateTimeOffset expected)
    {
        calendar.IsTimeIncluded(askedAt).Should().BeFalse(
            "the premise of the case: {0:O} reads as {1:yyyy-MM-dd HH:mm zzz} local, which this calendar excludes",
            askedAt, TimeZoneInfo.ConvertTime(askedAt, zone));

        calendar.IsTimeIncluded(expected).Should().BeTrue(
            "and {0:O}, which reads as {1:yyyy-MM-dd HH:mm zzz} local, is not excluded",
            expected, TimeZoneInfo.ConvertTime(expected, zone));

        // A minute rather than a millisecond, because the exact instant a zone changes offset is not
        // agreed on to the millisecond: Windows cannot express a rule at local midnight and writes
        // 23:59:59.999 of the day before instead. A minute is clear of the disagreement.
        calendar.IsTimeIncluded(expected.AddMinutes(-1)).Should().BeFalse(
            "and it is the first such instant - a minute earlier reads as {0:yyyy-MM-dd HH:mm zzz} local, which is still excluded",
            TimeZoneInfo.ConvertTime(expected.AddMinutes(-1), zone));

        DateTimeOffset actual = calendar.GetNextIncludedTimeUtc(askedAt);

        actual.Should().Be(expected,
            "the next included time after an excluded instant is the first instant the calendar includes, and {0:O} reads as {1:yyyy-MM-dd HH:mm zzz} local (included={2})",
            actual, TimeZoneInfo.ConvertTime(actual, zone), calendar.IsTimeIncluded(actual));
    }

    private static TimeZoneInfo ResolveZone(string zoneKey)
    {
        switch (zoneKey)
        {
            case "Helsinki":
                return TestTimeZones.Helsinki;
            case "Eastern":
                return TestTimeZones.Eastern;
            case "Santiago":
                return TestTimeZones.Santiago;
            default:
                throw new ArgumentOutOfRangeException(nameof(zoneKey), zoneKey, "unknown test zone");
        }
    }

    /// <summary>
    /// The zone a case runs in, whether it names a transition day or a plain zone. A case grid that
    /// mixes the two - a transition day and the ordinary day that controls it - names them the same
    /// way and lets this tell them apart.
    /// </summary>
    private static TimeZoneInfo ZoneForCase(string key)
    {
        return key.EndsWith("Spring", StringComparison.Ordinal) || key.EndsWith("Fall", StringComparison.Ordinal)
            ? ZoneForTransitionDay(key)
            : ResolveZone(key);
    }

    /// <summary>
    /// The zone a transition-day case runs in, with that day's premise stated. Resolved per case
    /// rather than from a shared table, because <see cref="TestTimeZones.Amman" /> ignores the test
    /// when the zone is missing from the system.
    /// </summary>
    private static TimeZoneInfo ZoneForTransitionDay(string dayKey)
    {
        switch (dayKey)
        {
            case "SantiagoSpring":
                // The clocks move at midnight: 2019-09-08 has no 00:00 local, it starts at 01:00 and
                // is 23 hours long.
                TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Santiago, new DateTime(2019, 9, 8, 0, 30, 0));
                return TestTimeZones.Santiago;

            case "SantiagoFall":
                // The same transition the other way: the hour before midnight happens twice, so
                // 2019-04-06 is 25 hours long and 2019-04-07 begins an hour later than the arithmetic
                // of the day before it says.
                TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Santiago, new DateTime(2019, 4, 6, 23, 30, 0));
                return TestTimeZones.Santiago;

            case "AmmanSpring":
                // Midnight moves here too: 2017-03-31 has no 00:00 local. Jordan abolished DST in
                // 2022, so this is frozen history rather than a rule that can move under the test.
                TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Amman, new DateTime(2017, 3, 31, 0, 30, 0));
                return TestTimeZones.Amman;

            case "AmmanFall":
                // The one zone here whose own midnight happens twice: 00:00-00:59 on 2017-10-27 is
                // the repeated hour, so that day starts at the first of two midnights.
                TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Amman, new DateTime(2017, 10, 27, 0, 30, 0));
                return TestTimeZones.Amman;

            case "HelsinkiSpring":
                // Not at midnight: 03:00 EET becomes 04:00 EEST on 2024-03-31, so the day begins at
                // +02:00 and every instant after the transition carries +03:00.
                TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Helsinki, new DateTime(2024, 3, 31, 3, 30, 0));
                return TestTimeZones.Helsinki;

            case "HelsinkiFall":
                // 04:00 EEST becomes 03:00 EET on 2024-10-27.
                TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Helsinki, new DateTime(2024, 10, 27, 3, 30, 0));
                return TestTimeZones.Helsinki;

            case "EasternSpring":
                // 02:00 EST becomes 03:00 EDT on 2024-03-10.
                TestTimeZones.AssumeInvalidLocalTime(TestTimeZones.Eastern, new DateTime(2024, 3, 10, 2, 30, 0));
                return TestTimeZones.Eastern;

            case "EasternFall":
                // 02:00 EDT becomes 01:00 EST on 2024-11-03.
                TestTimeZones.AssumeAmbiguousLocalTime(TestTimeZones.Eastern, new DateTime(2024, 11, 3, 1, 30, 0));
                return TestTimeZones.Eastern;

            default:
                throw new ArgumentOutOfRangeException(nameof(dayKey), dayKey, "unknown transition day");
        }
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
    }
}
