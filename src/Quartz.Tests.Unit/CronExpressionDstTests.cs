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
/// <see cref="CronExpression.GetTimeAfter" />, <see cref="CronExpression.GetTimeBefore" /> and
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
/// do not exist, so the re-resolution yields the pre-transition offset and the fire lands shifted
/// forward in real time by the transition delta.
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
    /// A daily trigger whose fire time falls inside the spring-forward gap still fires exactly once
    /// on the transition day, but shifted forward in real time by the transition delta: the
    /// non-existent local time is resolved with the pre-transition offset, so 02:30 -05:00 becomes
    /// the instant that reads 03:30 -04:00. The following day is back to the nominal wall clock
    /// time, which shows the shift is confined to the transition day.
    /// </summary>
    /// <remarks>
    /// This is a deliberate deviation from Cronos-style semantics, which fire such a trigger at the
    /// END of the gap (03:00 in the Eastern case, i.e. the moment the skipped wall clock time would
    /// have been reached). Quartz.NET instead fires delta-shifted (03:30) to keep parity with Java
    /// Quartz, whose <c>CronExpression</c> performs the same "advance in wall clock, resolve the
    /// offset last" walk. The Lord Howe case is the one that tells the two rules apart beyond
    /// doubt: its delta is 30 minutes, so a gap-END rule would fire at 02:30 while the delta-shift
    /// rule fires at 02:45.
    /// </remarks>
    [TestCase("0 30 2 * * ?", "Eastern", "2024-03-10 02:30", "2024-03-10 00:00 -05:00", "2024-03-10 03:30 -04:00", "2024-03-11 02:30 -04:00")]
    [TestCase("0 15 2 * * ?", "LordHowe", "2019-10-06 02:15", "2019-10-06 00:00 +10:30", "2019-10-06 02:45 +11:00", "2019-10-07 02:15 +11:00")]
    [TestCase("0 0 0 * * ?", "Santiago", "2019-09-08 00:00", "2019-09-07 12:00 -04:00", "2019-09-08 01:00 -03:00", "2019-09-09 00:00 -03:00")]
    public void GetTimeAfter_FixedTimeInsideGap_FiresOnceShiftedByTransitionDelta(
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
    /// <see cref="CronExpression.GetTimeBefore" /> is a binary search layered on top of
    /// <see cref="CronExpression.GetTimeAfter" />, so it must agree with it around transitions.
    /// Asserted as properties rather than pinned instants: the returned time is strictly before the
    /// probe, it is a genuine fire time (asking for the next fire one second earlier reproduces it),
    /// and asking for the next fire from it makes progress rather than wedging.
    /// </summary>
    /// <remarks>
    /// The -5 minute probes on the fall-back transitions land just after a fire inside the repeated
    /// hour; they used to break the round trip until the sub-second demotion defect was fixed (see
    /// <see cref="GetTimeBefore_ProbeJustAfterFireTimeInRepeatedHour_ReturnsRealPrecedingFire" />).
    /// </remarks>
    [TestCase("0 * * * * ?", "Eastern", "2024-03-10 07:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "Eastern", "2024-03-10 07:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 * * * * ?", "Eastern", "2024-11-03 06:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "Eastern", "2024-11-03 06:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 * * * * ?", "CentralEuropean", "2018-03-25 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "CentralEuropean", "2018-03-25 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 * * * * ?", "CentralEuropean", "2018-10-28 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    [TestCase("0 0 * * * ?", "CentralEuropean", "2018-10-28 01:00 +00:00", new int[] { -60, -5, 5, 60 })]
    public void GetTimeBefore_RoundTripsWithGetTimeAfter_AroundTransitions(
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

            DateTimeOffset? previous = cron.GetTimeBefore(probe);

            previous.Should().NotBeNull(context);
            previous!.Value.Should().BeBefore(probe, "GetTimeBefore must return a time strictly before the probe; " + context);

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

    #region Current-behavior pins (decision points)

    /// <summary>
    /// CURRENT BEHAVIOR PIN — known internal inconsistency, expected to change on main/4.0.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The mirror image of the fall-back inconsistency, on the spring-forward side. For a fixed
    /// time inside the gap, <see cref="CronExpression.GetTimeAfter" /> returns the delta-shifted
    /// instant 03:30 -04:00 (07:30Z), yet <see cref="CronExpression.IsSatisfiedBy" /> returns FALSE
    /// for that very instant, because its local reading is hour 03 and the expression asks for hour
    /// 02. The trigger therefore fires at an instant its own expression does not match.
    /// </para>
    /// <para>
    /// Whatever rule replaces the delta shift on main/4.0 should make these two agree: either the
    /// fire moves to the gap END (03:00, which the expression still does not match literally, so
    /// <c>IsSatisfiedBy</c> would need to special-case the gap), or <c>IsSatisfiedBy</c> learns to
    /// accept the shifted instant. Flip target: this assertion becomes <c>BeTrue</c>.
    /// </para>
    /// </remarks>
    [Test]
    public void IsSatisfiedBy_InstantReturnedForInGapFixedTime_IsCurrentlyFalse()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;
        TestTimeZones.AssumeInvalidLocalTime(zone, WallClock("2024-03-10 02:30"));

        CronExpression cron = CronIn("0 30 2 * * ?", zone);

        DateTimeOffset? fire = cron.GetTimeAfter(TestTimeZones.Local("2024-03-10 00:00 -05:00"));

        fire.Should().NotBeNull();
        fire!.Value.Should().Be(TestTimeZones.Local("2024-03-10 03:30 -04:00"));

        cron.IsSatisfiedBy(fire.Value).Should().BeFalse("the fire instant reads as local 03:30, and the expression asks for hour 02");
    }

    #endregion

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
    /// Regression test: <see cref="CronExpression.GetTimeBefore" /> must return a real fire time
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
    public void GetTimeBefore_ProbeJustAfterFireTimeInRepeatedHour_ReturnsRealPrecedingFire(
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

        DateTimeOffset? previous = cron.GetTimeBefore(probe);

        previous.Should().NotBeNull();
        previous!.Value.Should().Be(expectedFire);
        cron.IsSatisfiedBy(previous.Value).Should().BeTrue("GetTimeBefore must return a time the expression fires at");
    }
}
