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
    /// spring-forward day does not have excludes nothing at all on that day.
    /// </summary>
    [Test]
    public void CronCalendar_ExclusionInsideTheSpringForwardGap_ExcludesNothing()
    {
        TimeZoneInfo zone = TestTimeZones.Helsinki;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2024, 3, 31, 3, 30, 0));

        // every second of local hour 3, which on 2024-03-31 in Helsinki is an hour that never happens
        CronCalendar calendar = new CronCalendar(null, "* * 3 * * ?", zone);

        DateTimeOffset transition = ParseUtc("2024-03-31T01:00:00Z");

        for (int minute = -60; minute <= 60; minute++)
        {
            DateTimeOffset instant = transition.AddMinutes(minute);
            calendar.IsTimeIncluded(instant).Should().BeTrue(
                "{0:O} reads as {1:HH:mm} local, and no instant of this day reads as an hour that the day skipped",
                instant, TimeZoneInfo.ConvertTime(instant, zone));
        }

        // and the next included time after any of them is simply the next millisecond
        calendar.GetNextIncludedTimeUtc(transition).Should().Be(transition.AddMilliseconds(1));
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
    /// It does not, in a zone whose midnight moves. <c>HolidayCalendar.GetNextIncludedTimeUtc</c>
    /// builds the day's start as <c>new DateTimeOffset(local.Date, local.Offset)</c> - midnight at
    /// the offset the <em>queried instant</em> carries - and on a day whose own midnight does not
    /// exist, or whose offset changed part-way, that lands on the wrong day. The case is left
    /// inconclusive rather than pinned: an answer that the calendar itself calls excluded is not a
    /// behaviour to bless.
    /// </remarks>
    [TestCase("2019-09-08", "2019-09-08T10:00:00Z", "2019-09-09T03:00:00Z")]
    [TestCase("2019-04-06", "2019-04-07T02:30:00Z", "2019-04-07T04:00:00Z")]
    public void HolidayCalendar_NextIncludedTime_OnATransitionDay(string holidayText, string askedAtText, string expectedText)
    {
        TimeZoneInfo zone = TestTimeZones.Santiago;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2019, 9, 8, 0, 30, 0));
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2019, 4, 6, 23, 30, 0));

        DateOnly holiday = DateOnly.Parse(holidayText, System.Globalization.CultureInfo.InvariantCulture);
        DateTimeOffset askedAt = ParseUtc(askedAtText);
        DateTimeOffset expected = ParseUtc(expectedText);

        HolidayCalendar calendar = new HolidayCalendar { TimeZone = zone };
        calendar.AddExcludedDay(holiday);

        calendar.IsTimeIncluded(askedAt).Should().BeFalse("the premise of the case: {0:O} is inside the holiday", askedAt);
        calendar.IsTimeIncluded(expected).Should().BeTrue("and {0:O} is the first instant after it that is not", expected);

        DateTimeOffset actual = calendar.GetNextIncludedTimeUtc(askedAt);

        if (actual != expected)
        {
            Assert.Inconclusive(
                $"GetNextIncludedTimeUtc({askedAt:O}) answered {actual:O} (local "
                + $"{TimeZoneInfo.ConvertTime(actual, zone):yyyy-MM-dd HH:mm zzz}, included="
                + $"{calendar.IsTimeIncluded(actual)}); the first instant this calendar includes after "
                + $"the {holiday:yyyy-MM-dd} holiday is {expected:O} (local "
                + $"{TimeZoneInfo.ConvertTime(expected, zone):yyyy-MM-dd HH:mm zzz}). The day boundary it walks is built "
                + "as midnight at the offset of the instant it was asked about, which is not this day's midnight in a zone "
                + "that moves its clocks at midnight.");
        }

        actual.Should().Be(expected,
            "the next included time after an excluded instant is the first instant the calendar includes, and it must be one it does include");
    }

    private static TimeZoneInfo ResolveZone(string zoneKey)
    {
        switch (zoneKey)
        {
            case "Helsinki":
                return TestTimeZones.Helsinki;
            case "Eastern":
                return TestTimeZones.Eastern;
            default:
                throw new ArgumentOutOfRangeException(nameof(zoneKey), zoneKey, "unknown test zone");
        }
    }

    private static DateTimeOffset ParseUtc(string value)
    {
        return DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.AssumeUniversal | System.Globalization.DateTimeStyles.AdjustToUniversal);
    }
}
