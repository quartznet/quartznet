using System.Globalization;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit;

public class TimeZoneUtilTest
{
    [Test]
    public void ShouldBeAbleToFindWithAlias()
    {
        var infoWithUtc = TimeZoneUtil.FindTimeZoneById("UTC");
        var infoWithUniversalCoordinatedTime = TimeZoneUtil.FindTimeZoneById("Coordinated Universal Time");

        Assert.That(infoWithUniversalCoordinatedTime, Is.EqualTo(infoWithUtc));
    }

    [Test]
    public void GetNextIncludedTimeUtc_CrashOriginal2270()
    {
        TimeZoneInfo timeZone = TimeZoneInfo.FindSystemTimeZoneById("Pacific Standard Time");

        var weeklyCalendar = new WeeklyCalendar() { TimeZone = timeZone, };

        var dailyCalendar = new DailyCalendar(new TimeOnly(6, 0), new TimeOnly(22, 0), weeklyCalendar) { TimeZone = timeZone, InvertTimeRange = true, };

        var holidayCalendar = new HolidayCalendar(dailyCalendar) { TimeZone = timeZone, };
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 2, 19));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 5, 27));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 6, 19));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 7, 4));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 9, 2));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 10, 14));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 11, 11));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 11, 28));
        holidayCalendar.AddExcludedDay(new DateOnly(2024, 12, 25));

        var time = new DateTime(2024, 2, 5, 10, 6, 0, DateTimeKind.Utc);
        var expected = new DateTime(2024, 2, 5, 14, 0, 0, DateTimeKind.Utc);

        var d = holidayCalendar.GetNextIncludedTimeUtc(time);
        d.Should().Be(expected);
    }

    // The wall-clock overload GetUtcOffset(DateTime, ..) implements the trigger-wide DST policy:
    // an ambiguous local time resolves to the DAYLIGHT offset, i.e. the first of the two occurrences.
    [TestCase("Eastern", "2024-11-03 01:30", -4.0)]
    [TestCase("CentralEuropean", "2018-10-28 02:30", 2.0)]
    [TestCase("LordHowe", "2019-04-07 01:45", 11.0)]
    [TestCase("Santiago", "2019-04-06 23:30", -3.0)]
    public void GetUtcOffset_AmbiguousLocalTime_ReturnsDaylightOffset(string zoneKey, string localTime, double expectedOffsetHours)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(localTime, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeAmbiguousLocalTime(zone, local);

        TimeZoneUtil.GetUtcOffset(local, zone).Should().Be(TimeSpan.FromHours(expectedOffsetHours));
    }

    // An invalid (spring-forward gap) local time is not special-cased: it resolves to the
    // pre-transition offset, which places the resulting instant at the first wall-clock time that
    // does exist, shifted forward by the transition delta. Every trigger type inherits this rule.
    [TestCase("Eastern", "2024-03-10 02:30", -5.0)]
    [TestCase("LordHowe", "2019-10-06 02:15", 10.5)]
    [TestCase("Santiago", "2019-09-08 00:30", -4.0)]
    public void GetUtcOffset_InvalidLocalTime_ReturnsPreTransitionOffset(string zoneKey, string localTime, double expectedOffsetHours)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(localTime, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeInvalidLocalTime(zone, local);

        TimeZoneUtil.GetUtcOffset(local, zone).Should().Be(TimeSpan.FromHours(expectedOffsetHours));
    }

    [Test]
    public void GetUtcOffset_InstantOverload_AppliesNoAmbiguityPolicy()
    {
        // The DateTimeOffset overload resolves from the instant and never consults the ambiguity
        // policy, so for the second (standard) occurrence of an ambiguous wall-clock time the two
        // overloads disagree. Call sites that mean "resolve this wall-clock time" must pass
        // .DateTime explicitly or they silently lose the policy.
        TimeZoneInfo eastern = TestTimeZones.Eastern;
        DateTimeOffset standardPassInstant = TestTimeZones.Local("2024-11-03 01:30 -05:00");
        TestTimeZones.AssumeAmbiguousLocalTime(eastern, standardPassInstant.DateTime);

        TimeZoneUtil.GetUtcOffset(standardPassInstant, eastern).Should().Be(TimeSpan.FromHours(-5));
        TimeZoneUtil.GetUtcOffset(standardPassInstant.DateTime, eastern).Should().Be(TimeSpan.FromHours(-4));
    }

    // ResolveLocal must agree with the wall-clock GetUtcOffset policy for every time that exists
    [TestCase("Eastern", "2024-11-03 01:30")]
    [TestCase("CentralEuropean", "2018-10-28 02:30")]
    [TestCase("LordHowe", "2019-04-07 01:45")]
    [TestCase("Santiago", "2019-04-06 23:30")]
    public void ResolveLocal_AmbiguousLocalTime_MatchesDaylightPolicy(string zoneKey, string localTime)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(localTime, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeAmbiguousLocalTime(zone, local);

        DateTimeOffset resolved = TimeZoneUtil.ResolveLocal(local, zone);

        resolved.DateTime.Should().Be(local, "the wall clock must be kept as given");
        resolved.Offset.Should().Be(TimeZoneUtil.GetUtcOffset(local, zone), "an ambiguous time resolves to the daylight/first occurrence");
    }

    // An in-gap time pairs with the pre-transition offset, which renders in the zone as the same
    // wall clock shifted forward by the transition delta
    [TestCase("Eastern", "2024-03-10 02:30", -5.0, "2024-03-10 03:30")]
    [TestCase("LordHowe", "2019-10-06 02:15", 10.5, "2019-10-06 02:45")]
    [TestCase("Santiago", "2019-09-08 00:30", -4.0, "2019-09-08 01:30")]
    public void ResolveLocal_InvalidLocalTime_ShiftsForwardByTransitionDelta(string zoneKey, string localTime, double expectedOffsetHours, string expectedRenderedLocal)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(localTime, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeInvalidLocalTime(zone, local);

        DateTimeOffset resolved = TimeZoneUtil.ResolveLocal(local, zone);

        resolved.Offset.Should().Be(TimeSpan.FromHours(expectedOffsetHours), "an in-gap time pairs with the offset in effect just before the gap");
        TimeZoneInfo.ConvertTime(resolved, zone).DateTime
            .Should().Be(DateTime.Parse(expectedRenderedLocal, CultureInfo.InvariantCulture), "the instant renders in the zone at the delta-shifted wall clock");
    }

    // Differential guard: for every wall-clock minute around each transition of the test zones,
    // ResolveLocal must produce exactly the instant the trigger code produced before it existed
    // (pairing the local time with the wall-clock GetUtcOffset policy). Positive-daylight-delta
    // zones must be bit-identical, gaps included.
    [TestCase("Eastern", "2024-03-10 02:00")]
    [TestCase("Eastern", "2024-11-03 01:30")]
    [TestCase("CentralEuropean", "2018-03-25 02:30")]
    [TestCase("CentralEuropean", "2018-10-28 02:30")]
    [TestCase("Santiago", "2019-09-08 00:30")]
    [TestCase("Santiago", "2019-04-06 23:30")]
    [TestCase("LordHowe", "2019-10-06 02:15")]
    [TestCase("LordHowe", "2019-04-07 01:45")]
    public void ResolveLocal_MatchesLegacyResolution_AroundTransition(string zoneKey, string transitionLocal)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime center = DateTime.Parse(transitionLocal, CultureInfo.InvariantCulture);

        for (int minute = -180; minute <= 180; minute++)
        {
            DateTime probe = center.AddMinutes(minute);
            DateTimeOffset legacy = new DateTimeOffset(probe, TimeZoneUtil.GetUtcOffset(probe, zone));
            TimeZoneUtil.ResolveLocal(probe, zone).Should().Be(legacy, $"probe {probe:yyyy-MM-dd HH:mm} must resolve exactly as the previous inline logic did");
        }
    }

    [TestCase("Eastern", "2024-11-03 01:30", "2024-11-03 01:00", "2024-11-03 02:00")]
    [TestCase("CentralEuropean", "2018-10-28 02:30", "2018-10-28 02:00", "2018-10-28 03:00")]
    [TestCase("LordHowe", "2019-04-07 01:45", "2019-04-07 01:30", "2019-04-07 02:00")]
    [TestCase("Santiago", "2019-04-06 23:30", "2019-04-06 23:00", "2019-04-07 00:00")]
    public void TryGetAmbiguousWindow_ReturnsWallClockWindow(string zoneKey, string ambiguousLocal, string expectedStart, string expectedEnd)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(ambiguousLocal, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeAmbiguousLocalTime(zone, local);

        TimeZoneUtil.TryGetAmbiguousWindow(local, zone, out DateTime windowStart, out DateTime windowEnd).Should().BeTrue();

        windowStart.Should().Be(DateTime.Parse(expectedStart, CultureInfo.InvariantCulture));
        windowEnd.Should().Be(DateTime.Parse(expectedEnd, CultureInfo.InvariantCulture));

        // pairing the window start with the standard (smaller) offset yields the transition instant,
        // which renders in the zone as the window start itself - the first instant of the second pass
        TimeSpan standardOffset = zone.GetAmbiguousTimeOffsets(local).Min();
        DateTimeOffset transition = new DateTimeOffset(windowStart, standardOffset);
        TimeZoneInfo.ConvertTime(transition, zone).DateTime.Should().Be(windowStart);

        TimeZoneUtil.TryGetAmbiguousWindow(local.Date.AddHours(12), zone, out _, out _)
            .Should().BeFalse("noon is not ambiguous");
    }

    [TestCase("Eastern", "2024-03-10 02:30", "2024-03-10 03:00")]
    [TestCase("LordHowe", "2019-10-06 02:15", "2019-10-06 02:30")]
    [TestCase("Santiago", "2019-09-08 00:30", "2019-09-08 01:00")]
    public void WalkToGapEnd_ReturnsFirstValidWallClockTime(string zoneKey, string invalidLocal, string expectedGapEnd)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(invalidLocal, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeInvalidLocalTime(zone, local);

        TimeZoneUtil.WalkToGapEnd(local, zone).Should().Be(DateTime.Parse(expectedGapEnd, CultureInfo.InvariantCulture));

        // noon, not midnight - in a midnight-gap zone like Santiago the date's own 00:00 is invalid
        DateTime alreadyValid = local.Date.AddHours(12);
        TimeZoneUtil.WalkToGapEnd(alreadyValid, zone).Should().Be(alreadyValid, "a valid time is returned unchanged");
    }

    [Test]
    public void ResolveLocal_NegativeDaylightDeltaZone_DoesNotMoveBackwards()
    {
        // Europe/Dublin as modeled by TZif data (Linux/macOS) flags WINTER as the daylight period
        // with a negative delta, so TimeZoneInfo.GetUtcOffset for an in-gap time returns the
        // POST-gap offset there; pairing with it would produce an instant before the gap. On
        // Windows the zone is modeled with a positive delta and this test self-skips.
        TimeZoneInfo dublin;
        try
        {
            dublin = TimeZoneUtil.FindTimeZoneById("Europe/Dublin");
        }
        catch (TimeZoneNotFoundException)
        {
            Assert.Ignore("Europe/Dublin is not available on this system");
            return;
        }

        DateTime inGap = new DateTime(2024, 3, 31, 1, 30, 0);
        Assume.That(dublin.IsInvalidTime(inGap), "test premise: 2024-03-31 01:30 should not exist in Europe/Dublin");

        bool negativeDelta = dublin.GetAdjustmentRules()
            .Any(rule => rule.DateStart <= inGap && inGap <= rule.DateEnd && rule.DaylightDelta < TimeSpan.Zero);
        Assume.That(negativeDelta, "test premise: the zone data models Dublin with a negative daylight delta (TZif); on Windows this is positive and the hazard does not exist");

        DateTimeOffset justBeforeGap = new DateTimeOffset(new DateTime(2024, 3, 31, 0, 59, 0), dublin.GetUtcOffset(new DateTime(2024, 3, 31, 0, 59, 0)));
        DateTimeOffset resolved = TimeZoneUtil.ResolveLocal(inGap, dublin);

        resolved.Should().BeAfter(justBeforeGap, "an in-gap time must resolve forward across the gap, never backwards");
        TimeZoneInfo.ConvertTime(resolved, dublin).DateTime.Should().Be(new DateTime(2024, 3, 31, 2, 30, 0), "the instant renders at the delta-shifted wall clock after the gap");
    }

    [TestCase("US/Eastern", "Eastern Standard Time")]
    [TestCase("CET", "Central European Standard Time")]
    public void FindTimeZoneById_ResolvesAliasPairsOnAnyPlatform(string first, string second)
    {
        TimeZoneInfo firstZone = TimeZoneUtil.FindTimeZoneById(first);
        TimeZoneInfo secondZone = TimeZoneUtil.FindTimeZoneById(second);

        firstZone.BaseUtcOffset.Should().Be(secondZone.BaseUtcOffset);
    }

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
            case "LordHowe":
                return TestTimeZones.LordHowe;
            default:
                throw new ArgumentOutOfRangeException(nameof(zoneKey), zoneKey, "unknown test zone");
        }
    }
}