using System.Globalization;

using Quartz.Extensibility;

namespace Quartz.Tests.Unit;

/// <summary>
/// Tests for Jenkins-style H (hash) token support in CronExpression.
/// </summary>
public class CronExpressionHashTest
{
    // --- Determinism tests ---

    [Test]
    public void ResolveHash_SameKeySameExpression_ReturnsSameResult()
    {
        string result1 = CronExpression.ResolveHash("H H * * * ?", "myTrigger");
        string result2 = CronExpression.ResolveHash("H H * * * ?", "myTrigger");

        Assert.AreEqual(result1, result2);
    }

    [Test]
    public void ResolveHash_DifferentKeys_ReturnsDifferentResults()
    {
        string result1 = CronExpression.ResolveHash("H H * * * ?", "trigger-A");
        string result2 = CronExpression.ResolveHash("H H * * * ?", "trigger-B");

        // Extremely unlikely to collide for two different keys
        Assert.AreNotEqual(result1, result2);
    }

    [Test]
    public void ResolveHash_IntSeedSameAsStringHashSeed()
    {
        int seed = CronExpression.HashStringToSeed("myTrigger");
        string fromString = CronExpression.ResolveHash("H H * * * ?", "myTrigger");
        string fromInt = CronExpression.ResolveHash("H H * * * ?", seed);

        Assert.AreEqual(fromString, fromInt);
    }

    // --- Per-field range tests ---

    [Test]
    public void ResolveHash_SecondsField_InRange0To59()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("H * * * * ?", seed);
            string[] fields = resolved.Split(' ');
            int seconds = int.Parse(fields[0], CultureInfo.InvariantCulture);
            seconds.Should().BeInRange(0, 59);
        }
    }

    [Test]
    public void ResolveHash_MinutesField_InRange0To59()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("* H * * * ?", seed);
            string[] fields = resolved.Split(' ');
            int minutes = int.Parse(fields[1], CultureInfo.InvariantCulture);
            minutes.Should().BeInRange(0, 59);
        }
    }

    [Test]
    public void ResolveHash_HoursField_InRange0To23()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("* * H * * ?", seed);
            string[] fields = resolved.Split(' ');
            int hours = int.Parse(fields[2], CultureInfo.InvariantCulture);
            hours.Should().BeInRange(0, 23);
        }
    }

    [Test]
    public void ResolveHash_DayOfMonthField_InRange1To31()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("* * * H * ?", seed);
            string[] fields = resolved.Split(' ');
            int day = int.Parse(fields[3], CultureInfo.InvariantCulture);
            day.Should().BeInRange(1, 31);
        }
    }

    [Test]
    public void ResolveHash_MonthField_InRange1To12()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("* * * ? H *", seed);
            string[] fields = resolved.Split(' ');
            int month = int.Parse(fields[4], CultureInfo.InvariantCulture);
            month.Should().BeInRange(1, 12);
        }
    }

    [Test]
    public void ResolveHash_DayOfWeekField_InRange1To7()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("* * * ? * H", seed);
            string[] fields = resolved.Split(' ');
            int dow = int.Parse(fields[5], CultureInfo.InvariantCulture);
            dow.Should().BeInRange(1, 7);
        }
    }

    // --- Field independence ---

    [Test]
    public void ResolveHash_DifferentFieldsGetDifferentValues()
    {
        // Per-field mixing should produce different values for most seeds,
        // but modulo 60 can legitimately collide for some seeds
        int differentCount = 0;
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("H H * * * ?", seed);
            string[] fields = resolved.Split(' ');
            int seconds = int.Parse(fields[0], CultureInfo.InvariantCulture);
            int minutes = int.Parse(fields[1], CultureInfo.InvariantCulture);
            if (seconds != minutes)
            {
                differentCount++;
            }
        }

        differentCount.Should().BeGreaterThan(90, "seconds and minutes should differ for most seeds");
    }

    [Test]
    public void ResolveHash_ThreeFields_AllDifferent()
    {
        string resolved = CronExpression.ResolveHash("H H H * * ?", 12345);
        string[] fields = resolved.Split(' ');
        int sec = int.Parse(fields[0], CultureInfo.InvariantCulture);
        int min = int.Parse(fields[1], CultureInfo.InvariantCulture);
        int hour = int.Parse(fields[2], CultureInfo.InvariantCulture);

        // All three should be different (extremely likely with different field mixing)
        bool allSame = sec == min && min == hour;
        Assert.IsFalse(allSame, "Three H fields should not all resolve to the same value");
    }

    // --- Range (min-max) tests ---

    [Test]
    public void ResolveHash_WithRange_ValueInRange()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("* * H(9-17) * * ?", seed);
            string[] fields = resolved.Split(' ');
            int hours = int.Parse(fields[2], CultureInfo.InvariantCulture);
            hours.Should().BeInRange(9, 17);
        }
    }

    [Test]
    public void ResolveHash_WithNarrowRange_ValueInRange()
    {
        for (int seed = 0; seed < 100; seed++)
        {
            string resolved = CronExpression.ResolveHash("* H(0-5) * * * ?", seed);
            string[] fields = resolved.Split(' ');
            int minutes = int.Parse(fields[1], CultureInfo.InvariantCulture);
            minutes.Should().BeInRange(0, 5);
        }
    }

    // --- Step tests ---

    [Test]
    public void ResolveHash_WithStep_ProducesValidStepExpression()
    {
        string resolved = CronExpression.ResolveHash("* H/15 * * * ?", 42);
        string[] fields = resolved.Split(' ');
        string minuteField = fields[1];

        // Should be "offset/15" where offset is 0-14
        minuteField.Should().Contain("/15");
        string offsetStr = minuteField.Split('/')[0];
        int offset = int.Parse(offsetStr, CultureInfo.InvariantCulture);
        offset.Should().BeInRange(0, 14);
    }

    [Test]
    public void ResolveHash_WithStep_ProducesValidCronExpression()
    {
        string resolved = CronExpression.ResolveHash("H/15 * * * * ?", 42);

        // The resolved expression should be parseable
        CronExpression expr = new CronExpression(resolved);
        DateTimeOffset? next = expr.GetNextValidTimeAfter(DateTimeOffset.UtcNow);
        Assert.IsNotNull(next);
    }

    // --- Range + Step tests ---

    [Test]
    public void ResolveHash_WithRangeAndStep_ProducesValidExpression()
    {
        string resolved = CronExpression.ResolveHash("* H(0-29)/10 * * * ?", 42);
        string[] fields = resolved.Split(' ');
        string minuteField = fields[1];

        // Should be "offset-29/10" where offset is 0-9 (offset = hash % min(10, 30))
        minuteField.Should().Contain("/10");
        minuteField.Should().Contain("-29");

        string startStr = minuteField.Split('-')[0];
        int start = int.Parse(startStr, CultureInfo.InvariantCulture);
        start.Should().BeInRange(0, 9);

        // And it should be parseable
        CronExpression expr = new CronExpression(resolved);
        Assert.IsNotNull(expr.GetNextValidTimeAfter(DateTimeOffset.UtcNow));
    }

    // --- Comma list tests ---

    [Test]
    public void ResolveHash_InCommaList_ResolvesOnlyHToken()
    {
        string resolved = CronExpression.ResolveHash("* H,30,45 * * * ?", 42);
        string[] fields = resolved.Split(' ');
        string minuteField = fields[1];

        string[] parts = minuteField.Split(',');
        parts.Should().HaveCount(3);

        int hashPart = int.Parse(parts[0], CultureInfo.InvariantCulture);
        hashPart.Should().BeInRange(0, 59);
        Assert.AreEqual("30", parts[1]);
        Assert.AreEqual("45", parts[2]);
    }

    // --- No H passthrough test ---

    [Test]
    public void ResolveHash_NoHTokens_ReturnsUnchanged()
    {
        string resolved = CronExpression.ResolveHash("0 15 10 * * ?", "anyKey");
        Assert.AreEqual("0 15 10 * * ?", resolved);
    }

    // --- ContainsHashToken tests ---

    [Test]
    public void ContainsHashToken_WithH_ReturnsTrue()
    {
        Assert.IsTrue(CronExpression.ContainsHashToken("H * * * * ?"));
        Assert.IsTrue(CronExpression.ContainsHashToken("* H * * * ?"));
        Assert.IsTrue(CronExpression.ContainsHashToken("* * * ? * H"));
        Assert.IsTrue(CronExpression.ContainsHashToken("H H H * * ?"));
        Assert.IsTrue(CronExpression.ContainsHashToken("* H(0-29)/10 * * * ?"));
        Assert.IsTrue(CronExpression.ContainsHashToken("h h * * * ?"));  // lowercase
    }

    [Test]
    public void ContainsHashToken_WithoutH_ReturnsFalse()
    {
        Assert.IsFalse(CronExpression.ContainsHashToken("0 15 10 * * ?"));
        Assert.IsFalse(CronExpression.ContainsHashToken("0 0 12 ? * MON-FRI"));
        Assert.IsFalse(CronExpression.ContainsHashToken("0 0 12 ? * THU"));  // THU contains H but doesn't start with H
    }

    // --- ParseWithHash tests ---

    [Test]
    public void ParseWithHash_WithHashKey_ProducesValidExpression()
    {
        CronExpression expr = CronExpression.ParseWithHash("H H * * * ?", "myTrigger");

        Assert.IsNotNull(expr.CronExpressionString);
        expr.CronExpressionString.Should().NotContain("H");

        DateTimeOffset? next = expr.GetNextValidTimeAfter(DateTimeOffset.UtcNow);
        Assert.IsNotNull(next);
    }

    [Test]
    public void ParseWithHash_WithIntSeed_ProducesValidExpression()
    {
        CronExpression expr = CronExpression.ParseWithHash("H H(0-7) * * * ?", 42);

        Assert.IsNotNull(expr.CronExpressionString);
        expr.CronExpressionString.Should().NotContain("H");

        DateTimeOffset? next = expr.GetNextValidTimeAfter(DateTimeOffset.UtcNow);
        Assert.IsNotNull(next);
    }

    [Test]
    public void ParseWithHash_WithTimeZone_PreservesResolvedExpression()
    {
        CronExpression original = CronExpression.ParseWithHash("H H * * * ?", "myTrigger");
        CronExpression copy = original.WithTimeZone(TimeZoneInfo.Utc);

        copy.CronExpressionString.Should().Be(original.CronExpressionString,
            "H tokens resolve once, at parse time, and a re-timed copy must not re-resolve them");
        copy.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    // --- ParseWithHash in another cron format ---

    [Test]
    public void ParseWithHash_WithoutAFormat_RejectsAFiveFieldExpression()
    {
        Action parse = () => CronExpression.ParseWithHash("H 4 * * 1", "myTrigger");

        parse.Should().Throw<FormatException>(
            "five fields is not a Quartz expression, and that is why resolving an H token in a Unix "
            + "expression needs an overload that says which dialect it is reading");
    }

    [Test]
    public void ParseWithHash_WithHashKeyAndFormat_ResolvesAgainstTheRewrittenExpression()
    {
        CronExpression viaUnix = CronExpression.ParseWithHash("H 4 * * 1", CronFormat.Unix, "myTrigger");

        viaUnix.CronExpressionString.Should().Be(
            CronExpression.ParseWithHash("0 H 4 ? * MON", "myTrigger").CronExpressionString,
            "the expression is read as the Quartz expression that says the same thing before its H "
            + "tokens are resolved, so the two routes are one schedule spread the same way");
    }

    [Test]
    public void ParseWithHash_WithIntSeedAndFormat_ResolvesAgainstTheRewrittenExpression()
    {
        CronExpression viaUnix = CronExpression.ParseWithHash("H 4 * * 1", CronFormat.Unix, 42);

        viaUnix.CronExpressionString.Should().Be(
            CronExpression.ParseWithHash("0 H 4 ? * MON", 42).CronExpressionString,
            "a seed spreads the rewritten expression exactly as a key does");
    }

    [Test]
    public void ParseWithHash_WithFormat_HashesADayOfWeekOverQuartzsRange()
    {
        CronExpression viaUnix = CronExpression.ParseWithHash("0 0 * * H", CronFormat.Unix, "myTrigger");

        viaUnix.CronExpressionString.Should().Be(
            CronExpression.ParseWithHash("0 0 0 ? * H", "myTrigger").CronExpressionString,
            "the rewrite runs first, so an H in a five-field day-of-week is hashed over Quartz's 1-7 "
            + "rather than crontab's 0-7 and never lands on a day the renumbering would have moved");
    }

    [Test]
    public void TryParseWithHash_WithFormat_ReadsTheDialectItIsGiven()
    {
        CronExpression.TryParseWithHash("H 4 * * 1", CronFormat.Unix, "myTrigger", out CronExpression unix).Should().BeTrue();
        unix!.CronExpressionString.Should().NotContain("H");

        CronExpression.TryParseWithHash("H 4 * * 1", CronFormat.Quartz, "myTrigger", out CronExpression quartz).Should().BeFalse(
            "the same five fields are not a Quartz expression, and the format is stated rather than sniffed");
        quartz.Should().BeNull();
    }

    [Test]
    public void TryParseWithHash_WithFormat_RejectsANullExpression()
    {
        CronExpression.TryParseWithHash(null, CronFormat.Unix, "myTrigger", out CronExpression result).Should().BeFalse(
            "the non-throwing form takes an expression from somewhere the caller does not control, and "
            + "null is the shape that comes back from a configuration key nobody set");
        result.Should().BeNull();

        Action nullKey = () => CronExpression.TryParseWithHash("H 4 * * 1", CronFormat.Unix, null, out _);

        nullKey.Should().Throw<ArgumentNullException>(
            "a missing hash key is the caller's own bug rather than bad input, so it throws where a bad "
            + "expression returns false");
    }

    [Test]
    public void ParseWithHash_WithFormat_RejectsANullExpression()
    {
        Action key = () => CronExpression.ParseWithHash(null, CronFormat.Unix, "myTrigger");
        Action seed = () => CronExpression.ParseWithHash(null, CronFormat.Unix, 42);

        key.Should().Throw<ArgumentNullException>();
        seed.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void TryParseWithHash_WithFormat_RejectsAMalformedHashToken()
    {
        CronExpression.TryParseWithHash("H(0-99) 4 * * 1", CronFormat.Unix, "myTrigger", out CronExpression result).Should().BeFalse(
            "an H token can be malformed in its own right, and the rewrite does not make it valid");
        result.Should().BeNull();
    }

    // --- Builder integration tests ---

    [Test]
    public void TriggerBuilder_WithIdentityAndH_Succeeds()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test-trigger")
            .WithCronSchedule("H H * * * ?")
            .Build();

        Assert.IsNotNull(trigger);
        ICronTrigger cronTrigger = (ICronTrigger) trigger;
        cronTrigger.CronExpressionString.Should().NotContain("H");
    }

    [Test]
    public void TriggerBuilder_WithIdentityAndGroup_UsesGroupInHash()
    {
        ITrigger trigger1 = TriggerBuilder.Create()
            .WithIdentity("test", "groupA")
            .WithCronSchedule("H H * * * ?")
            .Build();

        ITrigger trigger2 = TriggerBuilder.Create()
            .WithIdentity("test", "groupB")
            .WithCronSchedule("H H * * * ?")
            .Build();

        ICronTrigger cron1 = (ICronTrigger) trigger1;
        ICronTrigger cron2 = (ICronTrigger) trigger2;

        // Same name but different group should produce different hash
        Assert.AreNotEqual(cron1.CronExpressionString, cron2.CronExpressionString);
    }

    [Test]
    public void TriggerBuilder_DefaultGroup_UsesNameOnly()
    {
        // With default group, the hash key is ":name" (colon prefix discriminator)
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("myTrigger")
            .WithCronSchedule("H H * * * ?")
            .Build();

        // Compare with explicit key matching the internal encoding
        CronExpression explicit_ = CronExpression.ParseWithHash("H H * * * ?", ":myTrigger");

        ICronTrigger cronTrigger = (ICronTrigger) trigger;
        Assert.AreEqual(explicit_.CronExpressionString, cronTrigger.CronExpressionString);
    }

    [Test]
    public void TriggerBuilder_WithoutIdentity_ThrowsOnH()
    {
        Action act = () => TriggerBuilder.Create()
            .WithCronSchedule("H H * * * ?")
            .Build();

        act.Should().Throw<FormatException>()
            .WithMessage("*WithIdentity*");
    }

    [Test]
    public void TriggerBuilder_WithoutH_DoesNotRequireIdentity()
    {
        // Normal cron expressions should still work without explicit identity
        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule("0 15 10 * * ?")
            .Build();

        Assert.IsNotNull(trigger);
    }

    [Test]
    public void TriggerBuilder_ExplicitHashKey_DoesNotRequireIdentity()
    {
        // When using explicit hash key, no trigger identity is required
        ITrigger trigger = TriggerBuilder.Create()
            .WithCronSchedule(CronScheduleBuilder.Create(CronExpression.ParseWithHash("H H * * * ?", "custom-key")))
            .Build();

        Assert.IsNotNull(trigger);
        ICronTrigger cronTrigger = (ICronTrigger) trigger;
        cronTrigger.CronExpressionString.Should().NotContain("H");
    }

    [Test]
    public void TriggerBuilder_WithHAndTimezone_PreservesTimezone()
    {
        TimeZoneInfo tz = TimeZoneInfo.Utc;

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("tz-test")
            .WithCronSchedule("H H * * * ?", x => x.InTimeZone(tz))
            .Build();

        ICronTrigger cronTrigger = (ICronTrigger) trigger;
        Assert.AreEqual(tz, cronTrigger.TimeZone);
    }

    [Test]
    public void TriggerBuilder_WithHAndMisfireInstruction_PreservesMisfire()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("misfire-test")
            .WithCronSchedule("H H * * * ?", x => x.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing))
            .Build();

        Assert.AreEqual(MisfireInstruction.CronTrigger.DoNothing, trigger.MisfireInstructionCode);
    }

    [Test]
    public void CronScheduleFromHashedExpression_ProducesValidSchedule()
    {
        CronScheduleBuilder builder = CronScheduleBuilder.Create(CronExpression.ParseWithHash("H H(0-7) * * * ?", "nightly"));
        IMutableTrigger trigger = builder.Build();
        Assert.IsNotNull(trigger);
    }

    // --- Error cases ---

    [Test]
    public void ResolveHash_NullExpression_Throws()
    {
        Action act = () => CronExpression.ResolveHash(null!, "key");
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ResolveHash_NullHashKey_Throws()
    {
        Action act = () => CronExpression.ResolveHash("H * * * * ?", (string) null!);
        act.Should().Throw<ArgumentException>();
    }

    [Test]
    public void ResolveHash_HInYearField_Throws()
    {
        Action act = () => CronExpression.ResolveHash("0 0 0 * * ? H", "key");
        act.Should().Throw<FormatException>()
            .WithMessage("*year*");
    }

    [Test]
    public void ResolveHash_InvalidRange_MinGreaterThanMax_Throws()
    {
        Action act = () => CronExpression.ResolveHash("H(30-5) * * * * ?", "key");
        act.Should().Throw<FormatException>()
            .WithMessage("*min > max*");
    }

    [Test]
    public void ResolveHash_RangeExceedsFieldBounds_Throws()
    {
        Action act = () => CronExpression.ResolveHash("H(0-60) * * * * ?", "key");
        act.Should().Throw<FormatException>()
            .WithMessage("*exceeds field bounds*");
    }

    [Test]
    public void ResolveHash_MissingCloseParen_Throws()
    {
        Action act = () => CronExpression.ResolveHash("H(0-5 * * * * ?", "key");
        act.Should().Throw<FormatException>()
            .WithMessage("*parenthesis*");
    }

    [Test]
    public void ResolveHash_InvalidStepValue_Throws()
    {
        Action act = () => CronExpression.ResolveHash("H/0 * * * * ?", "key");
        act.Should().Throw<FormatException>()
            .WithMessage("*step*");
    }

    [Test]
    public void ResolveHash_TrailingCharacters_Throws()
    {
        Action act = () => CronExpression.ResolveHash("HX * * * * ?", "key");
        act.Should().Throw<FormatException>()
            .WithMessage("*Unexpected*");
    }

    [TestCase("H H * * * ?")]
    [TestCase("H H(0-7) * * * ?")]
    [TestCase("H/15 * * * * ?")]
    public void TryParseWithHash_ValidExpression_ReturnsTrueAndTheResolvedExpression(string expression)
    {
        CronExpression.TryParseWithHash(expression, "key", out CronExpression result).Should().BeTrue();

        result.CronExpressionString.Should().NotContain("H",
            "a true return means the H tokens resolved, so none can be left in the parsed expression");
    }

    [TestCase("H(0-60) * * * * ?", "the hash range names a second that does not exist, which the resolver rejects")]
    [TestCase("H", "too few fields, which the parser rejects")]
    [TestCase(null, "a null expression is not a parse failure to report, it is simply not an expression")]
    public void TryParseWithHash_InvalidExpression_ReturnsFalseAndNoExpression(string expression, string reason)
    {
        CronExpression.TryParseWithHash(expression, "key", out CronExpression result).Should().BeFalse(reason);

        result.Should().BeNull("a failed attempt must not hand back a half-built expression");
    }

    [Test]
    public void TryParseWithHash_NullHashKey_Throws()
    {
        Action act = () => CronExpression.TryParseWithHash("H H * * * ?", null!, out _);

        act.Should().Throw<ArgumentNullException>(
            "the key comes from the program rather than from user input, so a missing one is a bug, not a parse failure");
    }

    [Test]
    public void ParseWithHash_InvalidExpression_Throws()
    {
        Action act = () => CronExpression.ParseWithHash("H(0-60) * * * * ?", "key");

        act.Should().Throw<FormatException>("resolving H is a parse, and a parse that cannot finish throws");
    }

    [Test]
    public void ParseWithHash_NullArguments_Throw()
    {
        Action nullExpression = () => CronExpression.ParseWithHash(null!, "key");
        nullExpression.Should().Throw<ArgumentNullException>();

        Action nullHashKey = () => CronExpression.ParseWithHash("H H * * * ?", null!);
        nullHashKey.Should().Throw<ArgumentNullException>();

        Action nullExpressionWithSeed = () => CronExpression.ParseWithHash(null!, 42);
        nullExpressionWithSeed.Should().Throw<ArgumentNullException>();
    }

    [Test]
    public void ParseWithHash_IsResolveHashFollowedByAParse()
    {
        const string Expression = "H H(0-7) * * * ?";

        CronExpression.ParseWithHash(Expression, "nightly").CronExpressionString
            .Should().Be(CronExpression.ResolveHash(Expression, "nightly"),
                "the two-step recipe and the one-call form must resolve H to the same values");

        CronExpression.ParseWithHash(Expression, 42).CronExpressionString
            .Should().Be(CronExpression.ResolveHash(Expression, 42),
                "and the same holds for a seed");
    }

    // --- Macros on the hash path ---

    /// <summary>
    /// A macro names a whole schedule rather than a field, so it carries no <c>H</c> token and has
    /// nothing to resolve - but it still has to survive the hash path. Issue #3626: hash resolution
    /// counts fields before anything is parsed, and it ran ahead of the constructor's macro expansion,
    /// so a one-field macro was rejected as malformed before anything looked at what it said.
    /// </summary>
    [TestCase("@yearly", "0 0 0 1 1 ?")]
    [TestCase("@annually", "0 0 0 1 1 ?")]
    [TestCase("@monthly", "0 0 0 1 * ?")]
    [TestCase("@weekly", "0 0 0 ? * SUN")]
    [TestCase("@daily", "0 0 0 * * ?")]
    [TestCase("@midnight", "0 0 0 * * ?")]
    [TestCase("@hourly", "0 0 * * * ?")]
    public void ResolveHash_Macro_ExpandsItRatherThanCountingItsFields(string macro, string expanded)
    {
        CronExpression.ResolveHash(macro, "key").Should().Be(expanded,
            "a macro is expanded wherever an expression string is read, the hash path included");

        CronExpression.ResolveHash(macro, 42).Should().Be(expanded,
            "and the seeded overload reads the same expression as the keyed one");
    }

    [TestCase("@daily", "0 0 0 * * ?")]
    [TestCase("@weekly", "0 0 0 ? * SUN")]
    public void ParseWithHash_Macro_ParsesToTheScheduleTheMacroNames(string macro, string expanded)
    {
        CronExpression.ParseWithHash(macro, "nightly").CronExpressionString.Should().Be(expanded);
        CronExpression.ParseWithHash(macro, 42).CronExpressionString.Should().Be(expanded);
    }

    [TestCase("@daily", "0 0 0 * * ?")]
    [TestCase("@weekly", "0 0 0 ? * SUN")]
    public void TryParseWithHash_Macro_SucceedsRatherThanReportingItInvalid(string macro, string expanded)
    {
        CronExpression.TryParseWithHash(macro, "nightly", out CronExpression result).Should().BeTrue(
            "a validator that rejects what its own parser accepts sends the caller to fix an expression that was never wrong");

        result.CronExpressionString.Should().Be(expanded);
    }

    /// <summary>
    /// The round trip the fix exists for: every door into the parser has to answer the same thing for
    /// the same string. The constructor expands macros, so the hash path has to agree with it.
    /// </summary>
    [TestCase("@yearly")]
    [TestCase("@annually")]
    [TestCase("@monthly")]
    [TestCase("@weekly")]
    [TestCase("@daily")]
    [TestCase("@midnight")]
    [TestCase("@hourly")]
    public void AMacroMeansTheSameThroughTheHashPathAsThroughTheConstructor(string macro)
    {
        string throughTheConstructor = new CronExpression(macro).CronExpressionString;

        CronExpression.ParseWithHash(macro, "nightly").CronExpressionString
            .Should().Be(throughTheConstructor, "ParseWithHash is a parse, and a parse reads the macro");
        CronExpression.ParseWithHash(macro, 42).CronExpressionString
            .Should().Be(throughTheConstructor);
        CronExpression.ResolveHash(macro, "nightly")
            .Should().Be(throughTheConstructor, "and the resolved string is what the constructor would have held");

        CronExpression.TryParseWithHash(macro, "nightly", out CronExpression tried).Should().BeTrue();
        tried.CronExpressionString.Should().Be(throughTheConstructor);

        CronExpression.ParseWithHash(macro, "nightly")
            .Should().Be(CronExpression.Parse(macro), "the two entry points build equal expressions, not merely equal strings");
    }

    [Test]
    public void ResolveHash_UnsupportedMacro_ExplainsTheMacroRatherThanTheFieldCount()
    {
        Action reboot = () => CronExpression.ResolveHash("@reboot", "key");
        reboot.Should().Throw<FormatException>()
            .WithMessage("*reboot*", "the macro's own rejection is the one that names the fix; a field count teaches nothing here");

        Action unknown = () => CronExpression.ResolveHash("@nightly", "key");
        unknown.Should().Throw<FormatException>()
            .WithMessage("*@daily*", "an unknown macro is answered with the set that does exist");
    }

    [Test]
    public void TryParseWithHash_UnsupportedMacro_ReturnsFalse()
    {
        CronExpression.TryParseWithHash("@reboot", "key", out CronExpression result).Should().BeFalse(
            "an unsupported macro is an expression the caller has to fix, which is what the attempt reports");

        result.Should().BeNull();
    }

    [Test]
    public void ResolveHash_MacroWithSurroundingWhitespace_IsStillAMacro()
    {
        CronExpression.ResolveHash("  @daily  ", "key").Should().Be("0 0 0 * * ?",
            "the constructor trims before it expands, so the hash path has to trim too or the two disagree");
    }

    // --- Fire time validation ---

    [Test]
    public void HashExpression_ProducesValidFireTimes()
    {
        CronExpression expr = CronExpression.ParseWithHash("H H H * * ?", "test-job").WithTimeZone(TimeZoneInfo.Utc);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        DateTimeOffset? next = expr.GetNextValidTimeAfter(now);
        Assert.IsNotNull(next);
        next!.Value.Should().BeAfter(now);

        // Get several fire times to verify they're consistent
        DateTimeOffset? second = expr.GetNextValidTimeAfter(next.Value);
        Assert.IsNotNull(second);
        second!.Value.Should().BeAfter(next.Value);

        // Should fire once per day, so ~24 hours apart
        TimeSpan diff = second.Value - next.Value;
        diff.TotalHours.Should().BeApproximately(24, 0.1);
    }

    [Test]
    public void HashExpression_WithHourRange_FiresInRange()
    {
        CronExpression expr = CronExpression.ParseWithHash("0 H H(9-17) * * ?", "work-hours").WithTimeZone(TimeZoneInfo.Utc);

        // Get next fire time
        DateTimeOffset now = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset? next = expr.GetNextValidTimeAfter(now);
        Assert.IsNotNull(next);

        // The hour should be in the range 9-17
        next!.Value.Hour.Should().BeInRange(9, 17);
    }

    // --- Distribution test ---

    [Test]
    public void HashDistribution_SpreadsAcrossRange()
    {
        int[] hourCounts = new int[24];

        for (int i = 0; i < 240; i++)
        {
            string resolved = CronExpression.ResolveHash("* * H * * ?", $"trigger-{i}");
            string[] fields = resolved.Split(' ');
            int hour = int.Parse(fields[2], CultureInfo.InvariantCulture);
            hourCounts[hour]++;
        }

        // With 240 samples across 24 buckets, expect ~10 per bucket
        // Allow wide margin but ensure no bucket is empty and none has > 25% of samples
        int nonEmpty = 0;
        for (int i = 0; i < 24; i++)
        {
            if (hourCounts[i] > 0)
            {
                nonEmpty++;
            }
            hourCounts[i].Should().BeLessThan(60, $"Hour {i} has too many hits ({hourCounts[i]}), distribution is poor");
        }

        // At least half the buckets should have values
        nonEmpty.Should().BeGreaterThan(12, "Hash distribution should use most of the range");
    }

    // --- FNV-1a determinism test ---

    [Test]
    public void HashStringToSeed_IsDeterministic()
    {
        int seed1 = CronExpression.HashStringToSeed("hello");
        int seed2 = CronExpression.HashStringToSeed("hello");
        Assert.AreEqual(seed1, seed2);

        int seed3 = CronExpression.HashStringToSeed("world");
        Assert.AreNotEqual(seed1, seed3);
    }

    [Test]
    public void HashStringToSeed_AlwaysPositive()
    {
        string[] keys = { "", "a", "test", "nightly-cleanup", "very-long-trigger-name-that-goes-on-and-on" };
        foreach (string key in keys)
        {
            int seed = CronExpression.HashStringToSeed(key);
            seed.Should().BeGreaterThanOrEqualTo(0);
        }
    }
}
