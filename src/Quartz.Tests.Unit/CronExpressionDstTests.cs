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
using System.Globalization;

namespace Quartz.Tests.Unit;

/// <summary>
/// Daylight saving time corner cases for <see cref="CronExpression" /> itself, exercised through
/// <see cref="CronExpression.GetTimeAfter" />, <see cref="CronExpression.GetPreviousValidTimeBefore" /> and
/// <see cref="CronExpression.IsSatisfiedBy" /> across zones with differing transition shapes
/// (northern and southern hemisphere, a midnight gap, and a 30 minute delta).
/// </summary>
/// <remarks>
/// <para>
/// The mechanics being pinned live at the end of <see cref="CronExpression.GetTimeAfter" />: the
/// search converts the "after" instant into the target zone once, then advances purely in wall
/// clock terms with the offset frozen, and only at the very end re-resolves the offset for the
/// resulting local date and time. That re-resolution prefers the DAYLIGHT (first) occurrence of an
/// ambiguous local time, and demotes to the standard offset only when the daylight interpretation
/// would land at or before the "after" instant. Local times that fall inside a spring-forward gap
/// do not exist, so the re-resolution answers with the instant the clocks moved — the end of the
/// gap — and the walk's own starting wall clock is rewound into the gap whenever the wall clock a
/// second before it is one the gap swallowed. That rewind is what keeps a fire the gap produced a
/// fire the expression still matches.
/// </para>
/// <para>
/// These tests never touch <c>SystemTime</c>, so they are safe to run in parallel with the rest of
/// the suite.
/// </para>
/// </remarks>
public class CronExpressionDstTests
{
    /// <summary>
    /// Attributes cannot carry a <see cref="TimeZoneInfo" />, so test case grids name the zone and
    /// resolve it here. Zones that may be missing from an old OS install ignore the test from
    /// inside <see cref="TestTimeZones" /> rather than failing it.
    /// </summary>
    private static TimeZoneInfo ResolveZone(string zoneKey)
    {
        switch (zoneKey)
        {
            case "Eastern":
                return TestTimeZones.Eastern;
            case "CentralEuropean":
                return TestTimeZones.CentralEuropean;
            case "Santiago":
                return TestTimeZones.Santiago;
            case "Sydney":
                return TestTimeZones.Sydney;
            case "LordHowe":
                return TestTimeZones.LordHowe;
            default:
                throw new ArgumentOutOfRangeException(nameof(zoneKey), zoneKey, "unknown test time zone key");
        }
    }

    private static CronExpression CronIn(string expression, TimeZoneInfo zone)
    {
        return new CronExpression(expression, zone);
    }

    /// <summary>
    /// Parses a wall clock time with no offset, for stating an invalid or ambiguous premise.
    /// </summary>
    private static DateTime WallClock(string value)
    {
        return DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.None);
    }

    /// <summary>
    /// A daily trigger whose fire time falls inside the spring-forward gap fires exactly once on
    /// the transition day, at the END of the gap: the non-existent local time is answered with the
    /// instant the clocks moved, so 02:30 fires at 03:00 -04:00. The following day is back to the
    /// nominal wall clock time, which shows the move is confined to the transition day.
    /// </summary>
    /// <remarks>
    /// This is the Cronos rule, and it is the only in-gap resolution that can be self-consistent: a
    /// spring-forward gap takes no real time, so the gap's start and the gap's end are one instant,
    /// and that instant is therefore the one an <see cref="CronExpression.IsSatisfiedBy" /> probe
    /// starting a second earlier can reach. The delta shift this replaced (03:30 in the Eastern
    /// case) could never be, whatever the search was taught. The Lord Howe case is the one that
    /// tells the two rules apart beyond doubt: its delta is 30 minutes, so the gap-end rule fires at
    /// 02:30 where the delta shift fired at 02:45. Santiago is the case where the two agree — its
    /// scheduled 00:00 is the FIRST wall clock of the gap, and for that one reading the delta shift
    /// and the gap's end name the same instant.
    /// </remarks>
    [TestCase("0 30 2 * * ?", "Eastern", "2024-03-10 02:30", "2024-03-10 00:00 -05:00", "2024-03-10 03:00 -04:00", "2024-03-11 02:30 -04:00")]
    [TestCase("0 15 2 * * ?", "LordHowe", "2019-10-06 02:15", "2019-10-06 00:00 +10:30", "2019-10-06 02:30 +11:00", "2019-10-07 02:15 +11:00")]
    [TestCase("0 0 0 * * ?", "Santiago", "2019-09-08 00:00", "2019-09-07 12:00 -04:00", "2019-09-08 01:00 -03:00", "2019-09-09 00:00 -03:00")]
    public void GetTimeAfter_FixedTimeInsideGap_FiresOnceAtTheEndOfTheGap(
        string cronExpression,
        string zoneKey,
        string gapLocalTime,
        string fromLocal,
        string expectedFire,
        string expectedNextDayFire)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock(gapLocalTime));

        CronExpression cron = CronIn(cronExpression, zone);

        DateTimeOffset? fire = cron.GetTimeAfter(TestTimeZones.Local(fromLocal));

        fire.Should().NotBeNull();
        fire!.Value.Should().Be(TestTimeZones.Local(expectedFire));

        DateTimeOffset? nextFire = cron.GetTimeAfter(fire.Value);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(TestTimeZones.Local(expectedNextDayFire), "the trigger fires only once on the transition day");
    }

    /// <summary>
    /// A daily trigger whose fire time is repeated by the fall-back transition fires once, at the
    /// FIRST (daylight) occurrence, and not again at the second (standard) occurrence of the same
    /// wall clock time. Southern hemisphere zones and a transition that crosses backwards over the
    /// date boundary behave the same way.
    /// </summary>
    [TestCase("0 30 1 * * ?", "Eastern", "2024-11-03 01:30", "2024-11-03 00:00 -04:00", "2024-11-03 01:30 -04:00", "2024-11-04 01:30 -05:00")]
    [TestCase("0 30 2 * * ?", "CentralEuropean", "2018-10-28 02:30", "2018-10-28 00:00 +02:00", "2018-10-28 02:30 +02:00", "2018-10-29 02:30 +01:00")]
    [TestCase("0 30 2 * * ?", "Sydney", "2024-04-07 02:30", "2024-04-07 00:00 +11:00", "2024-04-07 02:30 +11:00", "2024-04-08 02:30 +10:00")]
    [TestCase("0 30 23 * * ?", "Santiago", "2019-04-06 23:30", "2019-04-06 00:00 -03:00", "2019-04-06 23:30 -03:00", "2019-04-07 23:30 -04:00")]
    public void GetTimeAfter_DailyFixedTime_FallBackDay_FiresOnlyOnce_AtDaylightOccurrence(
        string cronExpression,
        string zoneKey,
        string ambiguousLocalTime,
        string fromLocal,
        string expectedFire,
        string expectedNextDayFire)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        TestTimeZones.AssumeAmbiguousLocalTime(zone, WallClock(ambiguousLocalTime));

        CronExpression cron = CronIn(cronExpression, zone);

        DateTimeOffset? fire = cron.GetTimeAfter(TestTimeZones.Local(fromLocal));

        fire.Should().NotBeNull();
        fire!.Value.Should().Be(TestTimeZones.Local(expectedFire), "the daylight occurrence comes first and time moves forward");

        DateTimeOffset? nextFire = cron.GetTimeAfter(fire.Value);

        nextFire.Should().NotBeNull();
        nextFire!.Value.Should().Be(TestTimeZones.Local(expectedNextDayFire), "the repeated wall clock time must not fire a second time");
    }

    /// <summary>
    /// Real time never stops, so a minutely trigger keeps firing every minute right through the
    /// spring-forward transition. What disappears is the local hour 02, not the fires: the walk
    /// produces an unbroken minutely sequence whose local reading jumps from 01:59 straight to
    /// 03:00, and the UTC hour that contains the transition is fully populated.
    /// </summary>
    [Test]
    public void SequentialWalk_MinutelyCron_SpringForwardDay_FireCountAndNoLocalGapHour()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2024-03-10 02:30"));

        CronExpression cron = CronIn("0 * * * * ?", zone);

        DateTimeOffset dayStart = TestTimeZones.Local("2024-03-10 00:00 -05:00");
        DateTimeOffset dayEnd = TestTimeZones.Local("2024-03-11 00:00 -04:00");

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(cron.GetTimeAfter, dayStart, dayEnd);

        // The spring-forward day is 23 real hours long; the walk excludes both boundary instants,
        // so a fire on every minute boundary in between is 23 * 60 - 1.
        fireTimes.Should().HaveCount(1379, "the day is 23 real hours long and every minute boundary fires");

        fireTimes.Should().NotContain(
            fire => TimeZoneInfo.ConvertTime(fire, zone).Hour == 2,
            "local hour 02 does not exist on the spring-forward day");

        DateTimeOffset gapHourStart = TestTimeZones.Local("2024-03-10 07:00 +00:00");
        DateTimeOffset gapHourEnd = TestTimeZones.Local("2024-03-10 08:00 +00:00");

        fireTimes.Should().Contain(
            fire => fire >= gapHourStart && fire < gapHourEnd,
            "real time does not stop during the gap; those fires simply read as local 03:xx");
    }

    /// <summary>
    /// <see cref="CronExpression.GetPreviousValidTimeBefore" /> is a binary search layered on top of
    /// <see cref="CronExpression.GetTimeAfter" />, so it must agree with it around transitions.
    /// Asserted as properties rather than pinned instants: the returned time is strictly before the
    /// probe, it is a genuine fire time (asking for the next fire one second earlier reproduces it),
    /// and asking for the next fire from it makes progress rather than wedging.
    /// </summary>
    /// <remarks>
    /// The -5 minute probes on the fall-back transitions land just after a fire inside the repeated
    /// hour; they used to break the round trip until the sub-second demotion defect was fixed (see
    /// <see cref="GetPreviousValidTimeBefore_ProbeJustAfterFireTimeInRepeatedHour_ReturnsRealPrecedingFire" />).
    /// The fixed-time spring-forward rows are the ones the gap's end has to satisfy: the +5 minute
    /// probe returns the in-gap fire, and "asking one second earlier reproduces it" is exactly the
    /// property the delta shift could not have.
    /// </remarks>
    [TestCase("0 30 2 * * ?", "Eastern", "2024-03-10 07:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0,30 2 * * ?", "Eastern", "2024-03-10 07:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 15 2 * * ?", "LordHowe", "2019-10-05 15:30 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 0 * * ?", "Santiago", "2019-09-08 04:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 * * * * ?", "Eastern", "2024-03-10 07:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "Eastern", "2024-03-10 07:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 * * * * ?", "Eastern", "2024-11-03 06:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "Eastern", "2024-11-03 06:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 * * * * ?", "CentralEuropean", "2018-03-25 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "CentralEuropean", "2018-03-25 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 * * * * ?", "CentralEuropean", "2018-10-28 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "CentralEuropean", "2018-10-28 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    public void GetPreviousValidTimeBefore_RoundTripsWithGetTimeAfter_AroundTransitions(
        string cronExpression,
        string zoneKey,
        string transitionUtc,
        int[] probeOffsetsInMinutes)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        CronExpression cron = CronIn(cronExpression, zone);

        DateTimeOffset transition = TestTimeZones.Local(transitionUtc);

        foreach (int probeOffsetInMinutes in probeOffsetsInMinutes)
        {
            DateTimeOffset probe = transition.AddMinutes(probeOffsetInMinutes);
            string context = $"probe {probe:O} ({probeOffsetInMinutes:+#;-#;0} min from the transition)";

            DateTimeOffset? previous = cron.GetPreviousValidTimeBefore(probe);

            previous.Should().NotBeNull(context);
            previous!.Value.Should().BeBefore(probe, "GetPreviousValidTimeBefore must return a time strictly before the probe; " + context);

            cron.GetTimeAfter(previous.Value.AddSeconds(-1))
                .Should().Be(previous.Value, "the time before must itself be a fire time; " + context);

            cron.GetTimeAfter(previous.Value)
                .Should().BeAfter(previous.Value, "asking again from a fire time must make progress; " + context);
        }
    }

    /// <summary>
    /// Spring-forward counterpart to the fall-back <c>IsSatisfiedBy</c> coverage: the instants that
    /// make up the "missing" hour are ordinary instants that read as local 03:xx, and a minutely
    /// expression matches them.
    /// </summary>
    [Test]
    public void IsSatisfiedBy_MinutelyCron_TrueForInstantsInsideGapHourUtc()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2024-03-10 02:30"));

        CronExpression cron = CronIn("0 * * * * ?", zone);

        DateTimeOffset insideGapHour = TestTimeZones.Local("2024-03-10 07:15 +00:00");

        cron.IsSatisfiedBy(insideGapHour).Should().BeTrue("07:15Z is local 03:15 -04:00, an ordinary matching minute");
    }

    /// <summary>
    /// Interval expressions fire through BOTH occurrences of the repeated fall-back window: an
    /// "every minute" schedule means 1500 minute fires in a 25 hour day (1499 in this walk, which
    /// excludes both boundary instants).
    /// </summary>
    /// <remarks>
    /// The wall-clock walk steps from 02:59 +02:00 to 03:00, so on its own it would skip the
    /// standard-offset repeat of 02:xx entirely; <c>ApplySecondAmbiguousPassIfNeeded</c> re-enters
    /// the window at the transition instant and the fall-back demotion then walks the second pass.
    /// </remarks>
    [Test]
    public void SequentialWalk_MinutelyCron_FallBackDay_FiresThroughBothPassesOfRepeatedHour()
    {
        TimeZoneInfo zone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, WallClock("2018-10-28 02:30"));

        CronExpression cron = CronIn("0 * * * * ?", zone);

        DateTimeOffset dayStart = TestTimeZones.Local("2018-10-28 00:00 +02:00");
        DateTimeOffset dayEnd = TestTimeZones.Local("2018-10-29 00:00 +01:00");

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(cron.GetTimeAfter, dayStart, dayEnd);

        fireTimes.Should().HaveCount(1499, "the 25 hour day fires every real minute, both walk boundaries excluded");

        DateTimeOffset repeatedHourStart = TestTimeZones.Local("2018-10-28 01:00 +00:00");
        DateTimeOffset repeatedHourEnd = TestTimeZones.Local("2018-10-28 02:00 +00:00");

        fireTimes.Count(fire => fire >= repeatedHourStart && fire < repeatedHourEnd)
            .Should().Be(60, "the standard pass over local 02:00-02:59 fires every minute");
    }

    /// <summary>
    /// <see cref="CronExpression.IsSatisfiedBy" /> and the sequential walk agree inside the
    /// repeated standard-pass hour: the instants the walk schedules are the instants the
    /// expression matches.
    /// </summary>
    [Test]
    public void IsSatisfiedBy_RepeatedHourStandardPass_AgreesWithSequentialWalk()
    {
        TimeZoneInfo zone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, WallClock("2018-10-28 02:30"));

        CronExpression cron = CronIn("0 * * * * ?", zone);

        DateTimeOffset standardPassInstant = TestTimeZones.Local("2018-10-28 01:30 +00:00");

        cron.IsSatisfiedBy(standardPassInstant).Should().BeTrue("01:30Z is local 02:30 +01:00, which the expression matches");

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(
            cron.GetTimeAfter,
            TestTimeZones.Local("2018-10-28 00:55 +02:00"),
            TestTimeZones.Local("2018-10-28 04:00 +01:00"));

        fireTimes.Should().Contain(standardPassInstant, "the walk schedules the instant IsSatisfiedBy accepts");
    }

    /// <summary>
    /// The interval-vs-fixed-time distinction is decided at parse time, from the second, minute
    /// and hour fields only: a wildcard, step or range means "fire every interval"; plain values
    /// and comma lists mean "fire at these times of day". Day, month and year fields never
    /// contribute.
    /// </summary>
    [TestCase("0 * * * * ?", true)]
    [TestCase("*/30 0 2 * * ?", true)]
    [TestCase("0 0/30 2 * * ?", true)]
    [TestCase("0 30 1-9 * * ?", true)]
    [TestCase("0 15,45 2-4 * * ?", true)]
    [TestCase("0 30 2 * * ?", false)]
    [TestCase("0 0,30 2 * * ?", false)]
    [TestCase("0 30 2 1-15 * ?", false)]
    [TestCase("0 30 2 ? * MON-FRI", false)]
    public void HasIntervalSemantics_SetForWildcardStepAndRangeInTimeFields(string expression, bool expected)
    {
        CronExpression cron = new CronExpression(expression);

        cron.HasIntervalSemantics.Should().Be(expected);
    }

    /// <summary>
    /// An hourly interval expression fires every real hour of the 25 hour fall-back day - the
    /// 02:00 wall clock runs at both of its occurrences.
    /// </summary>
    [Test]
    public void SequentialWalk_HourlyCron_FallBackDay_FiresEveryRealHour()
    {
        TimeZoneInfo zone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, WallClock("2018-10-28 02:30"));

        CronExpression cron = CronIn("0 0 * * * ?", zone);

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(
            cron.GetTimeAfter,
            TestTimeZones.Local("2018-10-28 00:00 +02:00"),
            TestTimeZones.Local("2018-10-29 00:00 +01:00"));

        fireTimes.Should().HaveCount(24, "25 hourly instants in the local day, both walk boundaries excluded");
        fireTimes.Should().Contain(TestTimeZones.Local("2018-10-28 02:00 +02:00"), "the first occurrence of 02:00 fires");
        fireTimes.Should().Contain(TestTimeZones.Local("2018-10-28 02:00 +01:00"), "the second occurrence of 02:00 fires too");
    }

    /// <summary>
    /// A comma list of plain values is a fixed-time expression: each listed time fires once, at
    /// its first (daylight) occurrence, and the second pass of the repeated window is skipped.
    /// </summary>
    [Test]
    public void FixedTimeCommaList_FallBackDay_FiresOnlyFirstPass()
    {
        TimeZoneInfo zone = TestTimeZones.CentralEuropean;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, WallClock("2018-10-28 02:30"));

        CronExpression cron = CronIn("0 0,30 2 * * ?", zone);

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(
            cron.GetTimeAfter,
            TestTimeZones.Local("2018-10-28 00:00 +02:00"),
            TestTimeZones.Local("2018-10-29 00:00 +01:00"));

        fireTimes.Should().Equal(
            TestTimeZones.Local("2018-10-28 02:00 +02:00"),
            TestTimeZones.Local("2018-10-28 02:30 +02:00"));
    }

    /// <summary>
    /// The repeated window on Lord Howe Island is only 30 minutes wide (the daylight delta is half
    /// an hour), and a minutely interval expression fires through both passes of it.
    /// </summary>
    [Test]
    public void SequentialWalk_MinutelyCron_LordHoweFallBack_FiresBothHalfHourPasses()
    {
        TimeZoneInfo zone = TestTimeZones.LordHowe;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, WallClock("2019-04-07 01:45"));

        CronExpression cron = CronIn("0 * * * * ?", zone);

        // 2019-04-07 02:00 +11:00 becomes 01:30 +10:30; the walk covers 14:00Z-16:00Z around it
        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(
            cron.GetTimeAfter,
            TestTimeZones.Local("2019-04-07 01:00 +11:00"),
            TestTimeZones.Local("2019-04-07 02:30 +10:30"));

        fireTimes.Should().HaveCount(119, "two real hours of minute fires, both walk boundaries excluded");
        fireTimes.Should().Contain(TestTimeZones.Local("2019-04-07 01:45 +11:00"), "first pass of the repeated half hour");
        fireTimes.Should().Contain(TestTimeZones.Local("2019-04-07 01:45 +10:30"), "second pass of the repeated half hour");
    }

    /// <summary>
    /// A trigger never fires at an instant its own expression rejects, not even for a wall clock
    /// the spring-forward gap swallowed.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <see cref="CronExpression.IsSatisfiedBy" /> is defined as "the next fire one second earlier
    /// is this instant", so it holds only for an in-gap fire whose resolution a one-second-earlier
    /// search can reproduce. It can: <see cref="CronExpression.GetTimeAfter" /> converts that
    /// earlier instant to wall clock 03:00, sees that 02:59:59 is a reading the gap swallowed, and
    /// rewinds its walk to the gap's start — arriving back at 02:30 and resolving it to the gap's
    /// end, the same instant.
    /// </para>
    /// <para>
    /// This is why the gap's END is the only resolution that can work. The delta shift this
    /// replaced returned 03:30 -04:00, which no earlier probe could ever reach, so the trigger
    /// fired at an instant its own expression did not match. <c>IsSatisfiedBy</c> needs no
    /// knowledge of daylight saving time to reach this answer.
    /// </para>
    /// </remarks>
    [Test]
    public void IsSatisfiedBy_InstantReturnedForInGapFixedTime_IsTrue()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2024-03-10 02:30"));

        CronExpression cron = CronIn("0 30 2 * * ?", zone);

        DateTimeOffset? fire = cron.GetTimeAfter(TestTimeZones.Local("2024-03-10 00:00 -05:00"));

        fire.Should().NotBeNull();
        fire!.Value.Should().Be(TestTimeZones.Local("2024-03-10 03:00 -04:00"));

        cron.IsSatisfiedBy(fire.Value).Should().BeTrue(
            "the gap's end is the instant the schedule's wall clock was reached, and it is the one instant a one-second-earlier probe can find");
    }

    /// <summary>
    /// Every wall clock a gap swallowed names the same instant, so an expression matching several
    /// of them fires once rather than once per match.
    /// </summary>
    /// <remarks>
    /// This expression fired once before the gap-end rule too, because 02:00 is the FIRST reading of
    /// the gap and the delta shift moved it to exactly the gap's end; it is pinned so that a later
    /// change cannot quietly turn one fire into two. What the rule did move is covered by
    /// <see cref="GetTimeAfter_MatchBetweenGapEndAndTheOldDeltaShift_IsNoLongerSwallowed" />.
    /// </remarks>
    [Test]
    public void GetTimeAfter_SeveralMatchesInsideOneGap_FireOnceAtTheGapsEnd()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2024-03-10 02:00"));
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2024-03-10 02:30"));

        CronExpression cron = CronIn("0 0,30 2 * * ?", zone);

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(
            after => cron.GetTimeAfter(after),
            TestTimeZones.Local("2024-03-10 00:00 -05:00"),
            TestTimeZones.Local("2024-03-11 00:00 -04:00"));

        fireTimes.Should().Equal(
            [TestTimeZones.Local("2024-03-10 03:00 -04:00")],
            "both 02:00 and 02:30 name the instant the clocks moved, and one instant is one fire");
    }

    /// <summary>
    /// A match that falls between the gap's end and where the delta shift used to land is no longer
    /// swallowed: the walk resumes from the gap's end, which is earlier, so it still sees 02:40.
    /// </summary>
    [Test]
    public void GetTimeAfter_MatchBetweenGapEndAndTheOldDeltaShift_IsNoLongerSwallowed()
    {
        TimeZoneInfo zone = TestTimeZones.LordHowe;
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2019-10-06 02:15"));

        CronExpression cron = CronIn("0 15,40 2 * * ?", zone);

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(
            after => cron.GetTimeAfter(after),
            TestTimeZones.Local("2019-10-06 00:00 +10:30"),
            TestTimeZones.Local("2019-10-07 00:00 +11:00"));

        fireTimes.Should().Equal(
            [TestTimeZones.Local("2019-10-06 02:30 +11:00"), TestTimeZones.Local("2019-10-06 02:40 +11:00")],
            "02:15 is answered with the gap's end 02:30, and 02:40 exists a quarter of an hour later; the delta shift used to answer 02:45 and lose 02:40 behind it");
    }

    /// <summary>
    /// An hourly-at-half-past schedule gains a fire on the spring-forward day: the occurrence the
    /// gap swallowed runs when the clocks move, and the next hour's occurrence still runs at its own
    /// wall clock half an hour later.
    /// </summary>
    /// <remarks>
    /// The interval-expression face of the same rule, and the shape most likely to be noticed in
    /// production. The delta shift moved the 02:30 occurrence onto 03:30, where the next hour's
    /// occurrence already stood, and the two collided into a single fire. The gap's end is earlier,
    /// so 03:30 is still ahead of the search when it resumes and both occurrences survive. Two fires
    /// half an hour apart is what never losing an occurrence costs, and it is confined to the
    /// transition.
    /// </remarks>
    [Test]
    public void GetTimeAfter_HourlyAtHalfPast_FiresAtTheGapsEndAndAgainAtTheNextHalfHour()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2024-03-10 02:30"));

        CronExpression cron = CronIn("0 30 * * * ?", zone);

        List<DateTimeOffset> fireTimes = TestTimeZones.Walk(
            after => cron.GetTimeAfter(after),
            TestTimeZones.Local("2024-03-10 01:30 -05:00"),
            TestTimeZones.Local("2024-03-10 04:30 -04:00"));

        fireTimes.Should().Equal(
            [
                TestTimeZones.Local("2024-03-10 03:00 -04:00"),
                TestTimeZones.Local("2024-03-10 03:30 -04:00")
            ],
            "02:30 is reached the moment the clocks move, and 03:30 is an ordinary reading half an hour later");
    }

    /// <summary>
    /// The search makes strict progress and stays monotone in its input across a spring-forward
    /// transition, including at the transition second itself where the walk rewinds into the gap.
    /// </summary>
    /// <remarks>
    /// Both properties are load-bearing. A result at or before the input would wedge
    /// <see cref="CronExpression.GetNextValidTimeAfter" /> into an endless fire; a result that fell
    /// as the input rose would break the binary search
    /// <see cref="CronExpression.GetPreviousValidTimeBefore" /> layers on top of it. The rewind fires exactly
    /// when the probe's whole-second floor is the transition instant, so this sweeps every second
    /// on either side of it.
    /// </remarks>
    [TestCase("0 30 2 * * ?", "Eastern", "2024-03-10 07:00 +00:00")]
    [TestCase("0 0,30 2 * * ?", "Eastern", "2024-03-10 07:00 +00:00")]
    [TestCase("0 * * * * ?", "Eastern", "2024-03-10 07:00 +00:00")]
    [TestCase("0 15 2 * * ?", "LordHowe", "2019-10-05 15:30 +00:00")]
    [TestCase("0 0 0 * * ?", "Santiago", "2019-09-08 04:00 +00:00")]
    public void GetTimeAfter_AcrossSpringForward_MakesStrictProgressAndStaysMonotone(
        string cronExpression,
        string zoneKey,
        string transitionUtc)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        CronExpression cron = CronIn(cronExpression, zone);

        DateTimeOffset transition = TestTimeZones.Local(transitionUtc);
        DateTimeOffset? previousAnswer = null;

        for (int offsetInSeconds = -90; offsetInSeconds <= 90; offsetInSeconds++)
        {
            DateTimeOffset probe = transition.AddSeconds(offsetInSeconds);
            DateTimeOffset? answer = cron.GetTimeAfter(probe);

            answer.Should().NotBeNull($"probe {probe:O}");
            answer!.Value.Should().BeAfter(probe, $"the next fire must be strictly after the probe; probe {probe:O}");

            if (previousAnswer is not null)
            {
                answer.Value.Should().BeOnOrAfter(previousAnswer.Value,
                    $"a later probe may never answer earlier, or GetPreviousValidTimeBefore's binary search loses its predicate; probe {probe:O}");
            }

            previousAnswer = answer;
        }
    }

    /// <summary>
    /// Regression guard for the walk-start rewind: the wall clock a second before the search's
    /// start is only asked about when there is one. A caller probing from the minimum instant would
    /// otherwise underflow, because a zone west of UTC clamps that conversion to the very first
    /// representable wall clock.
    /// </summary>
    [Test]
    public void GetTimeAfter_ProbeAtTheMinimumInstant_DoesNotThrow()
    {
        CronExpression cron = CronIn("0 30 2 * * ?", TestTimeZones.Eastern);

        Func<DateTimeOffset?> act = () => cron.GetTimeAfter(DateTimeOffset.MinValue);

        act.Should().NotThrow();
    }

    /// <summary>
    /// Regression test: a sub-second after-time inside the repeated fall-back hour must return the
    /// same fire as its whole-second floor.
    /// </summary>
    /// <remarks>
    /// The fall-back demotion in <see cref="CronExpression.GetTimeAfter" /> used to compare the
    /// truncated candidate fire against the UNTRUNCATED after-time, so any sub-second remainder made
    /// a perfectly good fire look "too early" while the local time was ambiguous and demoted it a
    /// whole hour forward: the next fire after 05:53:59.000Z was 05:54:00Z, but after 05:53:59.500Z
    /// — half a second later — it was 06:54:00Z, making the result non-monotonic in the input. The
    /// comparison now uses the whole-second floor the search starts from.
    /// </remarks>
    [Test]
    public void GetTimeAfter_SubSecondInstantInsideRepeatedHour_ReturnsSameFireAsWholeSecond()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeAmbiguousLocalTime(zone, WallClock("2024-11-03 01:30"));

        CronExpression cron = CronIn("0 * * * * ?", zone);

        DateTimeOffset wholeSecond = new DateTimeOffset(2024, 11, 3, 5, 53, 59, 0, TimeSpan.Zero);
        DateTimeOffset withMilliseconds = new DateTimeOffset(2024, 11, 3, 5, 53, 59, 500, TimeSpan.Zero);

        DateTimeOffset expected = TestTimeZones.Local("2024-11-03 05:54 +00:00");

        cron.GetTimeAfter(wholeSecond).Should().Be(expected, "local 01:54 -04:00 is the very next minute");
        cron.GetTimeAfter(withMilliseconds).Should().Be(expected, "sub-second ticks on the after-time must not demote the fire to the standard pass");
    }

    /// <summary>
    /// Regression test: <see cref="CronExpression.GetPreviousValidTimeBefore" /> must return a real fire time
    /// for probes just after a fire inside the repeated fall-back hour.
    /// </summary>
    /// <remarks>
    /// Its binary search probes arbitrary tick values, and the sub-second demotion described in
    /// <see cref="GetTimeAfter_SubSecondInstantInsideRepeatedHour_ReturnsSameFireAsWholeSecond" />
    /// used to make the search predicate non-monotonic in the second preceding such a fire, so the
    /// search converged one second early on a value the expression never fires at (:59 results for
    /// expressions that only fire at second 0).
    /// </remarks>
    [TestCase("0 * * * * ?", "Eastern", "2024-11-03 05:55 +00:00", "2024-11-03 05:54:00 +00:00")]
    [TestCase("0 0 * * * ?", "Eastern", "2024-11-03 05:55 +00:00", "2024-11-03 05:00:00 +00:00")]
    [TestCase("0 * * * * ?", "CentralEuropean", "2018-10-28 00:55 +00:00", "2018-10-28 00:54:00 +00:00")]
    [TestCase("0 0 * * * ?", "CentralEuropean", "2018-10-28 00:55 +00:00", "2018-10-28 00:00:00 +00:00")]
    public void GetPreviousValidTimeBefore_ProbeJustAfterFireTimeInRepeatedHour_ReturnsRealPrecedingFire(
        string cronExpression,
        string zoneKey,
        string probeUtc,
        string realPrecedingFire)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        CronExpression cron = CronIn(cronExpression, zone);

        DateTimeOffset probe = TestTimeZones.Local(probeUtc);

        // The real preceding fire is what a forward walk produces, and it is a genuine fire time.
        DateTimeOffset expectedFire = TestTimeZones.Local(realPrecedingFire);
        cron.GetTimeAfter(expectedFire.AddSeconds(-1)).Should().Be(expectedFire);
        cron.IsSatisfiedBy(expectedFire).Should().BeTrue();

        DateTimeOffset? previous = cron.GetPreviousValidTimeBefore(probe);

        previous.Should().NotBeNull();
        previous!.Value.Should().Be(expectedFire);
        cron.IsSatisfiedBy(previous.Value).Should().BeTrue("GetPreviousValidTimeBefore must return a time the expression fires at");
    }
}
