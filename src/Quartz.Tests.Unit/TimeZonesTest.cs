using System.Globalization;

using Quartz.Impl.Calendar;

namespace Quartz.Tests.Unit;

public class TimeZonesTest
{
    [Test]
    public void ShouldBeAbleToFindWithAlias()
    {
        var infoWithUtc = TimeZones.FindById("UTC");
        var infoWithUniversalCoordinatedTime = TimeZones.FindById("Coordinated Universal Time");

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

        TimeZones.GetUtcOffset(local, zone).Should().Be(TimeSpan.FromHours(expectedOffsetHours));
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

        TimeZones.GetUtcOffset(local, zone).Should().Be(TimeSpan.FromHours(expectedOffsetHours));
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

        TimeZones.GetUtcOffset(standardPassInstant, eastern).Should().Be(TimeSpan.FromHours(-5));
        TimeZones.GetUtcOffset(standardPassInstant.DateTime, eastern).Should().Be(TimeSpan.FromHours(-4));
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

        DateTimeOffset resolved = TimeZones.ResolveLocal(local, zone);

        resolved.DateTime.Should().Be(local, "the wall clock must be kept as given");
        resolved.Offset.Should().Be(TimeZones.GetUtcOffset(local, zone), "an ambiguous time resolves to the daylight/first occurrence");
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

        DateTimeOffset resolved = TimeZones.ResolveLocal(local, zone);

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
            DateTimeOffset legacy = new DateTimeOffset(probe, TimeZones.GetUtcOffset(probe, zone));
            TimeZones.ResolveLocal(probe, zone).Should().Be(legacy, $"probe {probe:yyyy-MM-dd HH:mm} must resolve exactly as the previous inline logic did");
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

        TimeZones.TryGetAmbiguousWindow(local, zone, out DateTime windowStart, out DateTime windowEnd).Should().BeTrue();

        windowStart.Should().Be(DateTime.Parse(expectedStart, CultureInfo.InvariantCulture));
        windowEnd.Should().Be(DateTime.Parse(expectedEnd, CultureInfo.InvariantCulture));

        // pairing the window start with the standard (smaller) offset yields the transition instant,
        // which renders in the zone as the window start itself - the first instant of the second pass
        TimeSpan standardOffset = zone.GetAmbiguousTimeOffsets(local).Min();
        DateTimeOffset transition = new DateTimeOffset(windowStart, standardOffset);
        TimeZoneInfo.ConvertTime(transition, zone).DateTime.Should().Be(windowStart);

        TimeZones.TryGetAmbiguousWindow(local.Date.AddHours(12), zone, out _, out _)
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

        TimeZones.WalkToGapEnd(local, zone).Should().Be(DateTime.Parse(expectedGapEnd, CultureInfo.InvariantCulture));

        // noon, not midnight - in a midnight-gap zone like Santiago the date's own 00:00 is invalid
        DateTime alreadyValid = local.Date.AddHours(12);
        TimeZones.WalkToGapEnd(alreadyValid, zone).Should().Be(alreadyValid, "a valid time is returned unchanged");
    }

    // A boundary inside a spring-forward gap is crossed the moment the clocks move, which is earlier
    // than the instant ResolveLocal hands a trigger for the same wall clock. The second case is why
    // the gap is walked from the whole minute: a boundary a millisecond into the gap is still crossed
    // at the transition, not a millisecond after it.
    [TestCase("Eastern", "2024-03-10 02:30", "2024-03-10 03:00")]
    [TestCase("Eastern", "2024-03-10 02:30:00.001", "2024-03-10 03:00")]
    [TestCase("LordHowe", "2019-10-06 02:15", "2019-10-06 02:30")]
    [TestCase("Santiago", "2019-09-08 00:30", "2019-09-08 01:00")]
    public void FirstInstantAtOrAfterLocal_InvalidLocalTime_IsTheInstantTheClocksMoved(string zoneKey, string invalidLocal, string expectedRenderedLocal)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(invalidLocal, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeInvalidLocalTime(zone, local);

        DateTimeOffset first = TimeZones.FirstInstantAtOrAfterLocal(local, zone);

        TimeZoneInfo.ConvertTime(first, zone).DateTime
            .Should().Be(DateTime.Parse(expectedRenderedLocal, CultureInfo.InvariantCulture),
                "the clock reads the end of the gap the moment it passes a boundary inside it");

        first.Should().BeBefore(TimeZones.ResolveLocal(local, zone),
            "ResolveLocal answers a trigger's question - when does this wall clock happen - by shifting past the gap, which is later than the moment the clock passed the boundary");
    }

    [Test]
    public void FirstInstantAtOrAfterLocal_LocalTimeThatExists_IsWhereResolveLocalPutsIt()
    {
        TimeZoneInfo zone = TestTimeZones.Eastern;

        DateTime ambiguous = new DateTime(2024, 11, 3, 1, 30, 0);
        TestTimeZones.AssumeAmbiguousLocalTime(zone, ambiguous);
        TimeZones.FirstInstantAtOrAfterLocal(ambiguous, zone).Should().Be(TimeZones.ResolveLocal(ambiguous, zone),
            "a wall clock that happens twice is first read at the first of the two");

        DateTime plain = new DateTime(2024, 6, 15, 12, 0, 0);
        TimeZones.FirstInstantAtOrAfterLocal(plain, zone).Should().Be(TimeZones.ResolveLocal(plain, zone),
            "and one that happens once is read then");
    }

    [TestCase("Eastern", "2024-11-03 01:30", "2024-11-03T06:30:00Z")]
    [TestCase("Santiago", "2019-04-06 23:30", "2019-04-07T03:30:00Z")]
    [TestCase("LordHowe", "2019-04-07 01:45", "2019-04-06T15:15:00Z")]
    public void TryResolveSecondPass_AmbiguousLocalTime_IsTheOccurrenceAfterTheTransition(string zoneKey, string ambiguousLocal, string expectedUtc)
    {
        TimeZoneInfo zone = ResolveZone(zoneKey);
        DateTime local = DateTime.Parse(ambiguousLocal, CultureInfo.InvariantCulture);
        TestTimeZones.AssumeAmbiguousLocalTime(zone, local);

        TimeZones.TryResolveSecondPass(local, zone, out DateTimeOffset second).Should().BeTrue();

        second.Should().Be(DateTimeOffset.Parse(expectedUtc, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal));
        second.Should().BeAfter(TimeZones.ResolveLocal(local, zone), "the second pass of a repeated wall clock comes after the first");
        TimeZoneInfo.ConvertTime(second, zone).DateTime.Should().Be(local, "and it reads as the same wall clock");

        TimeZones.TryResolveSecondPass(local.Date.AddHours(12), zone, out _).Should().BeFalse("noon is not ambiguous");
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
            dublin = TimeZones.FindById("Europe/Dublin");
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
        DateTimeOffset resolved = TimeZones.ResolveLocal(inGap, dublin);

        resolved.Should().BeAfter(justBeforeGap, "an in-gap time must resolve forward across the gap, never backwards");
        TimeZoneInfo.ConvertTime(resolved, dublin).DateTime.Should().Be(new DateTime(2024, 3, 31, 2, 30, 0), "the instant renders at the delta-shifted wall clock after the gap");
    }

    [TestCase("UTC", "Coordinated Universal Time")]
    [TestCase("CET", "Central European Standard Time")]
    [TestCase("US/Eastern", "Eastern Standard Time")]
    [TestCase("US/Central", "Central Standard Time")]
    [TestCase("US/Mountain", "Mountain Standard Time")]
    [TestCase("US/Arizona", "US Mountain Standard Time")]
    [TestCase("US/Pacific", "Pacific Standard Time")]
    [TestCase("US/Alaska", "Alaskan Standard Time")]
    [TestCase("US/Hawaii", "Hawaiian Standard Time")]
    [TestCase("Asia/Shanghai", "China Standard Time")]
    [TestCase("Asia/Karachi", "Pakistan Standard Time")]
    public void FindById_ResolvesAliasPairsOnAnyPlatform(string first, string second)
    {
        TimeZoneInfo firstZone = TimeZones.FindById(first);
        TimeZoneInfo secondZone = TimeZones.FindById(second);

        firstZone.BaseUtcOffset.Should().Be(secondZone.BaseUtcOffset);
    }

    [TestCase("Coordinated Universal Time")]
    [TestCase("CET")]
    public void FindById_RescuesAliasEntriesTheBclCannotResolve(string id)
    {
        // On Windows with ICU these ids fail TimeZoneInfo.FindSystemTimeZoneById AND both
        // TryConvert* conversions - they are why the alias table survives 4.0. On a platform
        // whose tzdata resolves them directly (e.g. "CET" on Linux) the lookup passes trivially.
        TimeZones.FindById(id).Should().NotBeNull();
    }

    [TestCase("US Central Standard Time")]
    [TestCase("US/Indiana-Stark")]
    public void FindById_PrunedDeadAliasPair_FailsWithGuidance(string id)
    {
        // The two ids aliased each other, but neither is a system id on Windows and neither is
        // known to the BCL conversions or to TimeZoneConverter, so the alias never rescued
        // anything - the pair was pruned in 4.0. A platform whose tzdata still ships the id
        // resolves it directly, and this test self-skips there.
        try
        {
            TimeZoneInfo.FindSystemTimeZoneById(id);
            Assert.Ignore($"'{id}' is a system time zone on this platform");
        }
        catch (TimeZoneNotFoundException)
        {
        }

        Func<TimeZoneInfo> act = () => TimeZones.FindById(id);

        act.Should().Throw<TimeZoneNotFoundException>()
            .WithMessage("*Quartz.Plugins.TimeZoneConverter*", "the failure should point at the plugin that resolves more ids");
    }

    [Test]
    public void NotFoundMessage_NamesTheAliasThatWasTriedToo()
    {
        string message = TimeZones.NotFoundMessage("CET", attemptedAlias: "Central European Standard Time");

        message.Should().Contain("CET").And.Contain("Central European Standard Time",
            "an alias lookup that fails used to be logged, and the message is the only place that signal "
            + "can live now that TimeZones carries no logger - without it a reader of the failure cannot "
            + "tell that a second id was tried");
    }

    [Test]
    public void NotFoundMessage_WithNoAliasTried_NamesOnlyTheIdAsked()
    {
        string message = TimeZones.NotFoundMessage("Quartz/Test-Unknown-Zone", attemptedAlias: null);

        message.Should().Contain("Quartz/Test-Unknown-Zone").And.NotContain("alias",
            "an id the alias table does not know was never looked up under a second name, so there is no "
            + "second id to report");
    }

    [Test]
    public void TimeZones_CarriesNoLoggingDependency()
    {
        // TimeZones is reached from CronExpression and from trigger deserialization, neither of which
        // needs a scheduler; keeping it on the BCL alone is what lets that code be split into a package
        // of its own later without taking Microsoft.Extensions.Logging along.
        string source = File.ReadAllText(Path.Combine(RepositoryRoot.Find().FullName, "src", "Quartz", "TimeZones.cs"));

        source.Should().NotContain("Microsoft.Extensions.Logging").And.NotContain("LogProvider").And.NotContain("ILogger",
            "TimeZones.cs is deliberately free of any tie to logging, and one call site is all it takes to put it back");
    }

    [Test]
    public void AddResolver_MostRecentlyAddedWins_AndDisposalUnregisters()
    {
        const string id = "Quartz/Test-Resolver-Ordering";
        TimeZoneInfo earlierZone = TimeZoneInfo.CreateCustomTimeZone(id + "-earlier", TimeSpan.FromMinutes(30), null, null);
        TimeZoneInfo laterZone = TimeZoneInfo.CreateCustomTimeZone(id + "-later", TimeSpan.FromMinutes(45), null, null);

        IDisposable earlier = TimeZones.AddResolver(x => x == id ? earlierZone : null);
        try
        {
            TimeZones.FindById(id).Should().BeSameAs(earlierZone);

            IDisposable later = TimeZones.AddResolver(x => x == id ? laterZone : null);
            try
            {
                TimeZones.FindById(id).Should().BeSameAs(laterZone,
                    "resolvers are consulted most recently added first, preserving the last-write-wins semantics CustomResolver had");
            }
            finally
            {
                later.Dispose();
            }

            TimeZones.FindById(id).Should().BeSameAs(earlierZone,
                "a disposed resolver must no longer shadow the one registered before it");
        }
        finally
        {
            earlier.Dispose();
        }

        Func<TimeZoneInfo> act = () => TimeZones.FindById(id);
        act.Should().Throw<TimeZoneNotFoundException>("every registration was disposed");
    }

    [Test]
    public void AddResolver_DisposingTwice_IsANoOpAndRemovesNothingElse()
    {
        const string id = "Quartz/Test-Resolver-Double-Dispose";
        TimeZoneInfo earlierZone = TimeZoneInfo.CreateCustomTimeZone(id + "-earlier", TimeSpan.FromMinutes(30), null, null);
        TimeZoneInfo laterZone = TimeZoneInfo.CreateCustomTimeZone(id + "-later", TimeSpan.FromMinutes(45), null, null);

        IDisposable earlier = TimeZones.AddResolver(x => x == id ? earlierZone : null);
        try
        {
            IDisposable later = TimeZones.AddResolver(x => x == id ? laterZone : null);
            later.Dispose();
            later.Dispose();

            TimeZones.FindById(id).Should().BeSameAs(earlierZone,
                "disposing a registration twice must not remove another resolver");
        }
        finally
        {
            earlier.Dispose();
        }
    }

    [Test]
    public void AddResolver_ResolverThrowingTimeZoneNotFound_FallsThroughToTheNextOne()
    {
        const string id = "Quartz/Test-Resolver-Throwing";
        TimeZoneInfo zone = TimeZoneInfo.CreateCustomTimeZone(id + "-zone", TimeSpan.FromMinutes(15), null, null);

        using IDisposable quiet = TimeZones.AddResolver(x => x == id ? zone : null);
        using IDisposable loud = TimeZones.AddResolver(
            x => x == id ? throw new TimeZoneNotFoundException("declining loudly") : null);

        TimeZones.FindById(id).Should().BeSameAs(zone,
            "a resolver throwing TimeZoneNotFoundException declines the id and the search continues with the next resolver");
    }

    [Test]
    public void AddResolver_NullResolver_Throws()
    {
        Action act = () => TimeZones.AddResolver(null!);
        act.Should().Throw<ArgumentNullException>();
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