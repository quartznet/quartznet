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
using System.Collections.Generic;
using System.Linq;

using Quartz.Impl.Triggers;

namespace Quartz.Tests.Unit;

/// <summary>
/// Daylight saving time coverage for <see cref="CalendarIntervalTriggerImpl" /> around fall-back
/// transitions and midnight gaps. The existing <see cref="CalendarIntervalTriggerTest" /> only covers
/// spring-forward, so these tests deliberately stay on the other side of the year: the repeated hour,
/// the midnight gap, the sub-day units that never see a transition at all, and the behaviour of the
/// default (non preserving) configuration.
///
/// Every expectation here pins the behaviour that ships today. Where that behaviour is arguably the
/// wrong answer it lives in the "current-behavior pins" region at the bottom with the reasoning
/// spelled out, so that a deliberate change shows up as a failing pin rather than a silent shift.
///
/// These tests never touch <see cref="SystemTime" />, so the fixture is safe to run in parallel.
/// </summary>
public class CalendarIntervalTriggerDstTests
{
    /// <summary>
    /// Builds a trigger with every DST relevant knob stated explicitly. <c>StartTimeUtc</c> is always
    /// supplied by the caller: the implementation defaults it to the current time, which would silently
    /// turn every expectation in this file into a vacuous assertion about "now".
    /// </summary>
    private static CalendarIntervalTriggerImpl CreateTrigger(
        TimeZoneInfo zone,
        IntervalUnit unit,
        int interval,
        DateTimeOffset startTimeUtc,
        bool preserveHourOfDay,
        bool skipDayIfHourDoesNotExist)
    {
        return new CalendarIntervalTriggerImpl
        {
            StartTimeUtc = startTimeUtc,
            RepeatIntervalUnit = unit,
            RepeatInterval = interval,
            TimeZone = zone,
            PreserveHourOfDayAcrossDaylightSavings = preserveHourOfDay,
            SkipDayIfHourDoesNotExist = skipDayIfHourDoesNotExist
        };
    }

    /// <summary>
    /// Collects the fire times in <c>[start, untilExclusive)</c>. Walking from one second before the
    /// start makes the start time itself the first collected fire, and <see cref="TestTimeZones.Walk" />
    /// fails the test if the trigger ever stops moving forward.
    /// </summary>
    private static List<DateTimeOffset> WalkFrom(
        CalendarIntervalTriggerImpl trigger,
        DateTimeOffset start,
        DateTimeOffset untilExclusive)
    {
        return TestTimeZones.Walk(after => trigger.GetFireTimeAfter(after), start.AddSeconds(-1), untilExclusive);
    }

    /// <summary>
    /// With the hour preserved, a fall-back day must produce exactly one fire even though the scheduled
    /// wall-clock time happens twice, and the next occurrence must be back at the same wall-clock time
    /// under the new (standard) offset.
    /// </summary>
    [TestCase(IntervalUnit.Day, "2024-11-01 01:30 -04:00", "2024-11-05 00:00 -05:00", "2024-11-04 01:30 -05:00")]
    [TestCase(IntervalUnit.Week, "2024-10-27 01:30 -04:00", "2024-11-11 00:00 -05:00", "2024-11-10 01:30 -05:00")]
    [TestCase(IntervalUnit.Month, "2024-10-03 01:30 -04:00", "2024-12-04 00:00 -05:00", "2024-12-03 01:30 -05:00")]
    [TestCase(IntervalUnit.Year, "2023-11-03 01:30 -04:00", "2025-11-04 00:00 -05:00", "2025-11-03 01:30 -05:00")]
    public void PreserveHour_FallBack_FiresOnceAtScheduledLocalHour(
        IntervalUnit unit,
        string start,
        string untilExclusive,
        string followingOccurrence)
    {
        AssertFallBackFiresOnceAtScheduledLocalHour(unit, start, untilExclusive, followingOccurrence, skipDayIfHourDoesNotExist: false);
    }

    /// <summary>
    /// <c>SkipDayIfHourDoesNotExist</c> is about spring-forward gaps only. On a fall-back day the
    /// scheduled hour exists twice over, so turning the flag on must change nothing at all: the results
    /// are identical to <see cref="PreserveHour_FallBack_FiresOnceAtScheduledLocalHour" />.
    /// </summary>
    [TestCase(IntervalUnit.Day, "2024-11-01 01:30 -04:00", "2024-11-05 00:00 -05:00", "2024-11-04 01:30 -05:00")]
    [TestCase(IntervalUnit.Week, "2024-10-27 01:30 -04:00", "2024-11-11 00:00 -05:00", "2024-11-10 01:30 -05:00")]
    [TestCase(IntervalUnit.Month, "2024-10-03 01:30 -04:00", "2024-12-04 00:00 -05:00", "2024-12-03 01:30 -05:00")]
    [TestCase(IntervalUnit.Year, "2023-11-03 01:30 -04:00", "2025-11-04 00:00 -05:00", "2025-11-03 01:30 -05:00")]
    public void PreserveHour_SkipDayFlag_FallBack_IsNoOp(
        IntervalUnit unit,
        string start,
        string untilExclusive,
        string followingOccurrence)
    {
        AssertFallBackFiresOnceAtScheduledLocalHour(unit, start, untilExclusive, followingOccurrence, skipDayIfHourDoesNotExist: true);
    }

    private static void AssertFallBackFiresOnceAtScheduledLocalHour(
        IntervalUnit unit,
        string start,
        string untilExclusive,
        string followingOccurrence,
        bool skipDayIfHourDoesNotExist)
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2024, 11, 3, 1, 30, 0));

        // each start is chosen so that one occurrence lands exactly on the fall-back day 2024-11-03:
        // daily from two days before, weekly from the previous Sunday, monthly/yearly from the same
        // day-of-month one period earlier. All of them are still on -04:00 (2023 fell back on 11-05).
        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            zone,
            unit,
            interval: 1,
            TestTimeZones.Local(start),
            preserveHourOfDay: true,
            skipDayIfHourDoesNotExist);

        List<DateTimeOffset> fires = WalkFrom(trigger, TestTimeZones.Local(start), TestTimeZones.Local(untilExclusive));

        List<DateTimeOffset> onTransitionDay = fires
            .Where(fireTime => TimeZoneInfo.ConvertTime(fireTime, zone).Date == new DateTime(2024, 11, 3))
            .ToList();

        // 01:30 happens twice on 2024-11-03. The trigger fires once, and on the first (daylight, -04:00)
        // occurrence, because the re-anchoring goes through TimeZones.GetUtcOffset(DateTime, ...)
        // which resolves an ambiguous wall-clock time to the daylight offset.
        onTransitionDay.Should().Equal(TestTimeZones.Local("2024-11-03 01:30 -04:00"));

        int transitionIndex = fires.IndexOf(onTransitionDay[0]);
        fires.Should().HaveCountGreaterThan(transitionIndex + 1, "the walk window must extend past the transition day");

        DateTimeOffset next = fires[transitionIndex + 1];
        next.Should().Be(TestTimeZones.Local(followingOccurrence));

        DateTimeOffset nextLocal = TimeZoneInfo.ConvertTime(next, zone);
        nextLocal.TimeOfDay.Should().Be(new TimeSpan(1, 30, 0), "the scheduled wall-clock time is preserved across the transition");
        nextLocal.Offset.Should().Be(TimeSpan.FromHours(-5), "the zone is on standard time after the fall back");
    }

    /// <summary>
    /// The spring-forward counterpart of <see cref="PreserveHour_FallBack_FiresOnceAtScheduledLocalHour" />,
    /// which the fall-back grid above covers and nothing covered here until now: on a day the scheduled
    /// wall clock does not have, the trigger crawls forward a minute at a time to the first instant the
    /// day does have, and the occurrence after that is back at the scheduled wall clock under the new
    /// (daylight) offset.
    /// </summary>
    [TestCase(IntervalUnit.Day, "2024-03-08 02:30 -05:00", "2024-03-12 00:00 -04:00", "2024-03-11 02:30 -04:00")]
    [TestCase(IntervalUnit.Week, "2024-03-03 02:30 -05:00", "2024-03-18 00:00 -04:00", "2024-03-17 02:30 -04:00")]
    [TestCase(IntervalUnit.Month, "2024-02-10 02:30 -05:00", "2024-04-11 00:00 -04:00", "2024-04-10 02:30 -04:00")]
    [TestCase(IntervalUnit.Year, "2023-03-10 02:30 -05:00", "2025-03-11 00:00 -04:00", "2025-03-10 02:30 -04:00")]
    public void PreserveHour_SpringForward_CrawlsToTheFirstMinuteTheDayHas(
        IntervalUnit unit,
        string start,
        string untilExclusive,
        string followingOccurrence)
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2024, 3, 10, 2, 30, 0));

        // each start is chosen so that one occurrence lands on the spring-forward day 2024-03-10:
        // daily from two days before, weekly from the previous Sunday, monthly/yearly from the same
        // day-of-month one period earlier. All of them are on -05:00, the pre-transition offset.
        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            zone,
            unit,
            interval: 1,
            TestTimeZones.Local(start),
            preserveHourOfDay: true,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> fires = WalkFrom(trigger, TestTimeZones.Local(start), TestTimeZones.Local(untilExclusive));

        List<DateTimeOffset> onTransitionDay = fires
            .Where(fireTime => TimeZoneInfo.ConvertTime(fireTime, zone).Date == new DateTime(2024, 3, 10))
            .ToList();

        // 02:30 does not exist on 2024-03-10, so the trigger crawls forward minute by minute and lands
        // on 03:00, the first wall clock the day actually has.
        onTransitionDay.Should().Equal(TestTimeZones.Local("2024-03-10 03:00 -04:00"));

        int transitionIndex = fires.IndexOf(onTransitionDay[0]);
        fires.Should().HaveCountGreaterThan(transitionIndex + 1, "the walk window must extend past the transition day");

        DateTimeOffset next = fires[transitionIndex + 1];
        next.Should().Be(TestTimeZones.Local(followingOccurrence));

        DateTimeOffset nextLocal = TimeZoneInfo.ConvertTime(next, zone);
        nextLocal.TimeOfDay.Should().Be(new TimeSpan(2, 30, 0),
            "the crawl applies to the transition day alone; the scheduled wall-clock time is preserved after it");
        nextLocal.Offset.Should().Be(TimeSpan.FromHours(-4), "the zone is on daylight time after the spring forward");
    }

    /// <summary>
    /// The flag that the fall-back grid proves is a no-op is exactly what this direction is for: with
    /// <c>SkipDayIfHourDoesNotExist</c> set, the day whose scheduled wall clock does not exist produces
    /// no fire at all, and the schedule resumes at the next period.
    /// </summary>
    [TestCase(IntervalUnit.Day, "2024-03-08 02:30 -05:00", "2024-03-12 00:00 -04:00", "2024-03-11 02:30 -04:00")]
    [TestCase(IntervalUnit.Week, "2024-03-03 02:30 -05:00", "2024-03-18 00:00 -04:00", "2024-03-17 02:30 -04:00")]
    [TestCase(IntervalUnit.Month, "2024-02-10 02:30 -05:00", "2024-04-11 00:00 -04:00", "2024-04-10 02:30 -04:00")]
    [TestCase(IntervalUnit.Year, "2023-03-10 02:30 -05:00", "2025-03-11 00:00 -04:00", "2025-03-10 02:30 -04:00")]
    public void PreserveHour_SkipDayFlag_SpringForward_DropsTheDay(
        IntervalUnit unit,
        string start,
        string untilExclusive,
        string followingOccurrence)
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2024, 3, 10, 2, 30, 0));

        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            zone,
            unit,
            interval: 1,
            TestTimeZones.Local(start),
            preserveHourOfDay: true,
            skipDayIfHourDoesNotExist: true);

        List<DateTimeOffset> fires = WalkFrom(trigger, TestTimeZones.Local(start), TestTimeZones.Local(untilExclusive));

        fires.Should().NotContain(
            fireTime => TimeZoneInfo.ConvertTime(fireTime, zone).Date == new DateTime(2024, 3, 10),
            "the flag says to skip a day whose scheduled hour does not exist rather than to crawl past the gap");

        fires[^1].Should().Be(TestTimeZones.Local(followingOccurrence),
            "the schedule resumes at its next period, at the scheduled wall clock under the daylight offset");
    }

    /// <summary>
    /// Sub-day units are pure elapsed-time arithmetic off the start instant, so a DST transition is
    /// simply not visible to them: the spacing between consecutive fires stays exactly the configured
    /// interval in both directions, and the repeated hour shows up only as a 25 hour local day.
    /// </summary>
    [TestCase(IntervalUnit.Hour, 1)]
    [TestCase(IntervalUnit.Minute, 90)]
    [TestCase(IntervalUnit.Second, 3600)]
    public void SubDayUnits_AreDstAgnostic(IntervalUnit unit, int interval)
    {
        TimeZoneInfo zone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2018, 3, 25, 2, 30, 0));
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2018, 10, 28, 2, 30, 0));

        TimeSpan expectedInterval = unit switch
        {
            IntervalUnit.Hour => TimeSpan.FromHours(interval),
            IntervalUnit.Minute => TimeSpan.FromMinutes(interval),
            _ => TimeSpan.FromSeconds(interval)
        };

        // spring forward: 2018-03-25 02:00 +01:00 becomes 03:00 +02:00
        AssertUniformSpacing(zone, unit, interval, "2018-03-24 23:00 +01:00", "2018-03-25 08:00 +02:00", expectedInterval);

        // fall back: 2018-10-28 03:00 +02:00 becomes 02:00 +01:00
        AssertUniformSpacing(zone, unit, interval, "2018-10-28 00:00 +02:00", "2018-10-28 07:00 +01:00", expectedInterval);

        if (unit == IntervalUnit.Hour && interval == 1)
        {
            // the fall-back local day is 25 hours long, so an hourly trigger fires 25 times on it
            CalendarIntervalTriggerImpl hourly = CreateTrigger(
                zone,
                IntervalUnit.Hour,
                interval: 1,
                TestTimeZones.Local("2018-10-28 00:00 +02:00"),
                preserveHourOfDay: true,
                skipDayIfHourDoesNotExist: false);

            List<DateTimeOffset> fires = WalkFrom(
                hourly,
                TestTimeZones.Local("2018-10-28 00:00 +02:00"),
                TestTimeZones.Local("2018-10-29 00:00 +01:00"));

            fires
                .Count(fireTime => TimeZoneInfo.ConvertTime(fireTime, zone).Date == new DateTime(2018, 10, 28))
                .Should().Be(25, "the repeated hour makes the fall-back local day 25 hours long");
        }
    }

    private static void AssertUniformSpacing(
        TimeZoneInfo zone,
        IntervalUnit unit,
        int interval,
        string start,
        string untilExclusive,
        TimeSpan expectedInterval)
    {
        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            zone,
            unit,
            interval,
            TestTimeZones.Local(start),
            preserveHourOfDay: true,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> fires = WalkFrom(trigger, TestTimeZones.Local(start), TestTimeZones.Local(untilExclusive));

        fires.Should().HaveCountGreaterThan(1, "the walk window must span the transition");

        for (int i = 1; i < fires.Count; i++)
        {
            (fires[i] - fires[i - 1]).Should().Be(
                expectedInterval,
                $"fire {i} at {fires[i]:O} must be exactly one interval after {fires[i - 1]:O}");
        }
    }

    /// <summary>
    /// When the preserved wall-clock time is itself the ambiguous one, the trigger fires on the first
    /// (daylight) occurrence of it, not the second. The re-anchoring resolves the local time through
    /// <c>TimeZones.GetUtcOffset(DateTime, ...)</c>, which prefers the daylight offset.
    /// </summary>
    [Test]
    public void PreserveHour_AmbiguousScheduledHour_Sydney_PicksFirstOccurrence()
    {
        TimeZoneInfo zone = TestTimeZones.Sydney;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2024, 4, 7, 2, 30, 0));

        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            zone,
            IntervalUnit.Day,
            interval: 1,
            TestTimeZones.Local("2024-04-05 02:30 +11:00"),
            preserveHourOfDay: true,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> fires = WalkFrom(
            trigger,
            TestTimeZones.Local("2024-04-05 02:30 +11:00"),
            TestTimeZones.Local("2024-04-09 00:00 +10:00"));

        fires.Should().Equal(
            TestTimeZones.Local("2024-04-05 02:30 +11:00"),
            TestTimeZones.Local("2024-04-06 02:30 +11:00"),
            // 02:30 exists twice on 2024-04-07; the daylight (+11:00) one is chosen
            TestTimeZones.Local("2024-04-07 02:30 +11:00"),
            TestTimeZones.Local("2024-04-08 02:30 +10:00"));
    }

    /// <summary>
    /// Santiago moves its clocks at midnight, so on 2019-09-08 the date's own 00:00 does not exist and
    /// the day starts at 01:00 -03:00. This pins both halves of the gap handling: crawl forward to the
    /// first valid minute, or skip the day entirely.
    /// </summary>
    [Test]
    public void PreserveHour_MidnightGap_Santiago_Daily()
    {
        TimeZoneInfo zone = TestTimeZones.Santiago;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2019, 9, 8, 0, 0, 0));

        DateTimeOffset start = TestTimeZones.Local("2019-09-06 00:00 -04:00");
        DateTimeOffset untilExclusive = TestTimeZones.Local("2019-09-11 00:00 -03:00");

        CalendarIntervalTriggerImpl crawling = CreateTrigger(
            zone, IntervalUnit.Day, interval: 1, start, preserveHourOfDay: true, skipDayIfHourDoesNotExist: false);

        WalkFrom(crawling, start, untilExclusive).Should().Equal(
            TestTimeZones.Local("2019-09-06 00:00 -04:00"),
            TestTimeZones.Local("2019-09-07 00:00 -04:00"),
            // 00:00 does not exist on the transition day, so the trigger crawls forward a minute at a
            // time until it lands on the first instant the day actually has
            TestTimeZones.Local("2019-09-08 01:00 -03:00"),
            TestTimeZones.Local("2019-09-09 00:00 -03:00"),
            TestTimeZones.Local("2019-09-10 00:00 -03:00"));

        CalendarIntervalTriggerImpl skipping = CreateTrigger(
            zone, IntervalUnit.Day, interval: 1, start, preserveHourOfDay: true, skipDayIfHourDoesNotExist: true);

        WalkFrom(skipping, start, untilExclusive).Should().Equal(
            TestTimeZones.Local("2019-09-06 00:00 -04:00"),
            TestTimeZones.Local("2019-09-07 00:00 -04:00"),
            // the transition day is dropped and the schedule resumes on the following day at 00:00
            TestTimeZones.Local("2019-09-09 00:00 -03:00"),
            TestTimeZones.Local("2019-09-10 00:00 -03:00"));
    }

    /// <summary>
    /// <c>ComputeFirstFireTimeUtc</c> applies no DST correction: the first fire is the start instant
    /// itself, whichever side of a transition it sits on and regardless of the preserve flag. Note that
    /// <c>StartTimeUtc</c> is an instant rather than a wall-clock time, so it can never be invalid or
    /// ambiguous - there is nothing for the trigger to correct here in the first place.
    /// </summary>
    [TestCase("2024-03-10 06:59 +00:00", false)]
    [TestCase("2024-03-10 06:59 +00:00", true)]
    [TestCase("2024-03-10 07:01 +00:00", false)]
    [TestCase("2024-03-10 07:01 +00:00", true)]
    [TestCase("2024-11-03 05:59 +00:00", false)]
    [TestCase("2024-11-03 05:59 +00:00", true)]
    [TestCase("2024-11-03 06:01 +00:00", false)]
    [TestCase("2024-11-03 06:01 +00:00", true)]
    public void FirstFireTime_EqualsStartInstant_AdjacentToTransitions(string startTimeUtc, bool preserveHourOfDay)
    {
        // Eastern springs forward at 2024-03-10 07:00Z and falls back at 2024-11-03 06:00Z, so each of
        // these start instants is a minute either side of a transition.
        DateTimeOffset start = TestTimeZones.Local(startTimeUtc);

        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            TestTimeZones.Eastern,
            IntervalUnit.Day,
            interval: 1,
            start,
            preserveHourOfDay,
            skipDayIfHourDoesNotExist: false);

        trigger.ComputeFirstFireTimeUtc(null).Should().Be(start);
    }

    #region Current-behavior pins (decision points)

    /// <summary>
    /// PINNED DELIBERATELY - this is the behaviour of the DEFAULT configuration and it is the opposite
    /// of what cron and daily-time-interval triggers do.
    ///
    /// With <c>PreserveHourOfDayAcrossDaylightSavings</c> left at its default of <c>false</c>, a day (or
    /// larger) interval is pure instant arithmetic: consecutive fires stay exactly 24 hours apart in UTC
    /// and the local wall-clock time therefore slides by an hour when the zone falls back. Cron and
    /// <c>DailyTimeIntervalTrigger</c> anchor to the wall clock instead and would keep firing at 01:30
    /// local. Whether a calendar-interval trigger should default to instant spacing or to wall-clock
    /// spacing is a genuine design question; this test exists so that changing the answer is a conscious
    /// act rather than an accident.
    /// </summary>
    [Test]
    public void NoPreserveHour_FallBack_LocalTimeDriftsOneHour_UtcStable()
    {
        TimeZoneInfo eastern = TestTimeZones.Eastern;
        TestTimeZones.AssumeAmbiguousLocalTime(eastern, new DateTime(2024, 11, 3, 1, 30, 0));

        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            eastern,
            IntervalUnit.Day,
            interval: 1,
            TestTimeZones.Local("2024-11-01 01:30 -04:00"),
            preserveHourOfDay: false,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> fires = WalkFrom(
            trigger,
            TestTimeZones.Local("2024-11-01 01:30 -04:00"),
            TestTimeZones.Local("2024-11-06 00:00 -05:00"));

        // Note where the drift actually lands. The transition is at 2024-11-03 06:00Z, and the trigger
        // fires at 05:30Z, so the 2024-11-03 fire is still on daylight time at 01:30 -04:00 - it is the
        // FOLLOWING day, 2024-11-04, that first shows the drifted 00:30 -05:00 wall-clock time.
        fires.Should().Equal(
            TestTimeZones.Local("2024-11-01 01:30 -04:00"),
            TestTimeZones.Local("2024-11-02 01:30 -04:00"),
            TestTimeZones.Local("2024-11-03 01:30 -04:00"),
            TestTimeZones.Local("2024-11-04 00:30 -05:00"),
            TestTimeZones.Local("2024-11-05 00:30 -05:00"));

        AssertConstantUtcSpacing(fires, TimeSpan.FromHours(24));

        // the whole run sits on the same UTC time of day, which is exactly the point
        fires.Should().OnlyContain(fireTime => fireTime.UtcDateTime.TimeOfDay == new TimeSpan(5, 30, 0));

        // Southern hemisphere mirror: Sydney falls back on 2024-04-07 and the same drift appears one day
        // later, on 2024-04-08, for the same reason.
        TimeZoneInfo sydney = TestTimeZones.Sydney;
        TestTimeZones.AssumeAmbiguousLocalTime(sydney, new DateTime(2024, 4, 7, 2, 30, 0));

        CalendarIntervalTriggerImpl sydneyTrigger = CreateTrigger(
            sydney,
            IntervalUnit.Day,
            interval: 1,
            TestTimeZones.Local("2024-04-05 02:30 +11:00"),
            preserveHourOfDay: false,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> sydneyFires = WalkFrom(
            sydneyTrigger,
            TestTimeZones.Local("2024-04-05 02:30 +11:00"),
            TestTimeZones.Local("2024-04-10 00:00 +10:00"));

        sydneyFires.Should().Equal(
            TestTimeZones.Local("2024-04-05 02:30 +11:00"),
            TestTimeZones.Local("2024-04-06 02:30 +11:00"),
            TestTimeZones.Local("2024-04-07 02:30 +11:00"),
            TestTimeZones.Local("2024-04-08 01:30 +10:00"),
            TestTimeZones.Local("2024-04-09 01:30 +10:00"));

        AssertConstantUtcSpacing(sydneyFires, TimeSpan.FromHours(24));
    }

    /// <summary>
    /// PINNED DELIBERATELY - the default configuration in a zone whose delta is not a whole hour.
    ///
    /// The drift the test above pins is a drift of exactly the transition delta, not of an hour: on
    /// Lord Howe Island a daily schedule left at the default <c>PreserveHourOfDayAcrossDaylightSavings
    /// = false</c> slides by the thirty minutes the island's clocks move, in whichever direction they
    /// move. Consecutive fires stay exactly 24 hours apart in UTC either way, which is the property
    /// that produces the drift.
    /// </summary>
    [TestCase("2024-10-04 02:01 +10:30", "2024-10-09 00:00 +11:00", "2024-10-06 02:31 +11:00", "2024-10-07 02:31 +11:00")]
    [TestCase("2024-04-05 02:01 +11:00", "2024-04-10 00:00 +10:30", "2024-04-07 01:31 +10:30", "2024-04-08 01:31 +10:30")]
    public void NoPreserveHour_LordHoweHalfHourDelta_DriftsByTheHalfHour(
        string start,
        string untilExclusive,
        string transitionDayFire,
        string dayAfterFire)
    {
        TimeZoneInfo zone = TestTimeZones.LordHowe;
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2024, 10, 6, 2, 15, 0));
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2024, 4, 7, 1, 45, 0));

        CalendarIntervalTriggerImpl trigger = CreateTrigger(
            zone,
            IntervalUnit.Day,
            interval: 1,
            TestTimeZones.Local(start),
            preserveHourOfDay: false,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> fires = WalkFrom(trigger, TestTimeZones.Local(start), TestTimeZones.Local(untilExclusive));

        fires.Should().Contain(TestTimeZones.Local(transitionDayFire),
            "the fire on the transition day itself is 24 UTC hours after the one before it, so it reads half an hour off the scheduled wall clock");
        fires.Should().Contain(TestTimeZones.Local(dayAfterFire),
            "and the drifted wall-clock time is what the schedule keeps from then on");

        AssertConstantUtcSpacing(fires, TimeSpan.FromHours(24));

        fires.Should().OnlyContain(fireTime => fireTime.UtcDateTime.TimeOfDay == TestTimeZones.Local(start).UtcDateTime.TimeOfDay,
            "the whole run sits on the same UTC time of day, which is exactly why the wall clock moves");
    }

    /// <summary>
    /// Lord Howe Island's daylight delta is 30 minutes, so preserving only the hour of day is not
    /// enough: a daily 02:01 schedule must stay at 02:01 across the fall-back transition
    /// (02:00 +11:00 becomes 01:30 +10:30), not drift to 02:31. On the spring-forward day the
    /// 02:00-02:30 gap swallows 02:01 and the fire moves to the gap end.
    /// </summary>
    [Test]
    public void PreserveHour_LordHoweHalfHourDelta_KeepsScheduledTimeOfDay()
    {
        TimeZoneInfo zone = TestTimeZones.LordHowe;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, new DateTime(2024, 4, 7, 1, 45, 0));
        TestTimeZones.AssumeInvalidLocalTime(zone, new DateTime(2024, 10, 6, 2, 1, 0));

        CalendarIntervalTriggerImpl fallBackTrigger = CreateTrigger(
            zone,
            IntervalUnit.Day,
            interval: 1,
            TestTimeZones.Local("2024-04-05 02:01 +11:00"),
            preserveHourOfDay: true,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> fallBackFires = WalkFrom(
            fallBackTrigger,
            TestTimeZones.Local("2024-04-05 02:01 +11:00"),
            TestTimeZones.Local("2024-04-10 00:00 +10:30"));

        fallBackFires.Should().Equal(
            TestTimeZones.Local("2024-04-05 02:01 +11:00"),
            TestTimeZones.Local("2024-04-06 02:01 +11:00"),
            TestTimeZones.Local("2024-04-07 02:01 +10:30"),
            TestTimeZones.Local("2024-04-08 02:01 +10:30"),
            TestTimeZones.Local("2024-04-09 02:01 +10:30"));

        CalendarIntervalTriggerImpl springTrigger = CreateTrigger(
            zone,
            IntervalUnit.Day,
            interval: 1,
            TestTimeZones.Local("2024-10-04 02:01 +10:30"),
            preserveHourOfDay: true,
            skipDayIfHourDoesNotExist: false);

        List<DateTimeOffset> springFires = WalkFrom(
            springTrigger,
            TestTimeZones.Local("2024-10-04 02:01 +10:30"),
            TestTimeZones.Local("2024-10-09 00:00 +11:00"));

        springFires.Should().Equal(
            TestTimeZones.Local("2024-10-04 02:01 +10:30"),
            TestTimeZones.Local("2024-10-05 02:01 +10:30"),
            TestTimeZones.Local("2024-10-06 02:30 +11:00"),
            TestTimeZones.Local("2024-10-07 02:01 +11:00"),
            TestTimeZones.Local("2024-10-08 02:01 +11:00"));
    }

    private static void AssertConstantUtcSpacing(List<DateTimeOffset> fires, TimeSpan expectedInterval)
    {
        for (int i = 1; i < fires.Count; i++)
        {
            (fires[i] - fires[i - 1]).Should().Be(
                expectedInterval,
                $"fire {i} at {fires[i]:O} must be exactly one interval after {fires[i - 1]:O}");
        }
    }

    #endregion
}
