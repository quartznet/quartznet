using System.Runtime.InteropServices;

namespace Quartz.Tests.Unit;

public class CronExpressionBuilderTest
{
    [Test]
    public void TestDefaultExpression()
    {
        CronExpressionBuilder builder = CronExpressionBuilder.Create();

        builder.ToString().Should().Be("* * * ? * *");
        builder.Build().CronExpressionString.Should().Be("* * * ? * *");
    }

    [Test]
    public void TestSecondField()
    {
        CronExpressionBuilder.Create().WithSecond(10).ToString().Should().Be("10 * * ? * *");
        CronExpressionBuilder.Create().WithSeconds(30, 0).ToString().Should().Be("30,0 * * ? * *");
        CronExpressionBuilder.Create().WithSecondRange(20, 30).ToString().Should().Be("20-30 * * ? * *");
        CronExpressionBuilder.Create().WithSecondRange(55, 5).ToString().Should().Be("55-5 * * ? * *");
        CronExpressionBuilder.Create().WithSecondIncrements(0, 15).ToString().Should().Be("0/15 * * ? * *");
    }

    [Test]
    public void TestMinuteField()
    {
        CronExpressionBuilder.Create().WithMinute(10).ToString().Should().Be("* 10 * ? * *");
        CronExpressionBuilder.Create().WithMinutes(17, 51).ToString().Should().Be("* 17,51 * ? * *");
        CronExpressionBuilder.Create().WithMinuteRange(20, 30).ToString().Should().Be("* 20-30 * ? * *");
        CronExpressionBuilder.Create().WithMinuteIncrements(0, 10).ToString().Should().Be("* 0/10 * ? * *");
    }

    [Test]
    public void TestHourField()
    {
        CronExpressionBuilder.Create().WithHour(10).ToString().Should().Be("* * 10 ? * *");
        CronExpressionBuilder.Create().WithHours(1, 5).ToString().Should().Be("* * 1,5 ? * *");
        CronExpressionBuilder.Create().WithHourRange(8, 17).ToString().Should().Be("* * 8-17 ? * *");
        CronExpressionBuilder.Create().WithHourRange(22, 2).ToString().Should().Be("* * 22-2 ? * *");
        CronExpressionBuilder.Create().WithHourIncrements(0, 6).ToString().Should().Be("* * 0/6 ? * *");
    }

    [Test]
    public void TestDayOfMonthField()
    {
        CronExpressionBuilder.Create().WithDayOfMonth(10).ToString().Should().Be("* * * 10 * ?");
        CronExpressionBuilder.Create().WithDaysOfMonth(1, 15).ToString().Should().Be("* * * 1,15 * ?");
        CronExpressionBuilder.Create().WithDayOfMonthRange(20, 22).ToString().Should().Be("* * * 20-22 * ?");
        CronExpressionBuilder.Create().WithDayOfMonthIncrements(1, 5).ToString().Should().Be("* * * 1/5 * ?");
        CronExpressionBuilder.Create().OnLastDayOfMonth().ToString().Should().Be("* * * L * ?");
        CronExpressionBuilder.Create().OnNearestWeekdayOfMonth(15).ToString().Should().Be("* * * 15W * ?");
    }

    [Test]
    public void TestMonthField()
    {
        CronExpressionBuilder.Create().WithMonth(2).ToString().Should().Be("* * * ? 2 *");
        CronExpressionBuilder.Create().WithMonths(3, 12).ToString().Should().Be("* * * ? 3,12 *");
        CronExpressionBuilder.Create().WithMonthRange(2, 8).ToString().Should().Be("* * * ? 2-8 *");
        CronExpressionBuilder.Create().WithMonthIncrements(3, 4).ToString().Should().Be("* * * ? 3/4 *");
    }

    [Test]
    public void TestDayOfWeekField()
    {
        CronExpressionBuilder.Create().WithDaysOfWeek(DayOfWeek.Thursday).ToString().Should().Be("* * * ? * THU");
        CronExpressionBuilder.Create().WithDaysOfWeek(DayOfWeek.Sunday, DayOfWeek.Wednesday).ToString().Should().Be("* * * ? * SUN,WED");
        CronExpressionBuilder.Create().WithDayOfWeekRange(DayOfWeek.Monday, DayOfWeek.Friday).ToString().Should().Be("* * * ? * MON-FRI");
        CronExpressionBuilder.Create().WithDayOfWeekRange(DayOfWeek.Thursday, DayOfWeek.Sunday).ToString().Should().Be("* * * ? * THU-SUN");
        CronExpressionBuilder.Create().OnNthDayOfWeekOfMonth(DayOfWeek.Sunday, 3).ToString().Should().Be("* * * ? * SUN#3");
        CronExpressionBuilder.Create().OnLastDayOfWeekOfMonth(DayOfWeek.Thursday).ToString().Should().Be("* * * ? * THUL");
        CronExpressionBuilder.Create().OnLastDayOfWeek().ToString().Should().Be("* * * ? * L");
        CronExpressionBuilder.Create().OnWeekdays().ToString().Should().Be("* * * ? * MON-FRI");
    }

    [Test]
    public void TestDayOfWeekIncrementsExpandToExplicitList()
    {
        CronExpressionBuilder.Create().WithDayOfWeekIncrements(DayOfWeek.Monday, 2).ToString().Should().Be("* * * ? * MON,WED,FRI");
        CronExpressionBuilder.Create().WithDayOfWeekIncrements(DayOfWeek.Sunday, 3).ToString().Should().Be("* * * ? * SUN,WED,SAT");
        CronExpressionBuilder.Create().WithDayOfWeekIncrements(DayOfWeek.Friday, 2).ToString().Should().Be("* * * ? * FRI");
        CronExpressionBuilder.Create().WithDayOfWeekIncrements(DayOfWeek.Sunday, 1).ToString().Should().Be("* * * ? * SUN,MON,TUE,WED,THU,FRI,SAT");
    }

    [Test]
    public void TestDayOfWeekIncrementsMatchNumericIncrementSemantics()
    {
        // numeric "2/2" means day 2 (MON) through SAT stepping by 2; the builder emits
        // the equivalent explicit day name list instead, since a textual "MON/2" is
        // rejected by the parser
        CronExpression expanded = CronExpressionBuilder.Create()
            .WithSecond(0)
            .WithMinute(0)
            .WithHour(12)
            .WithDayOfWeekIncrements(DayOfWeek.Monday, 2)
            .Build();
        CronExpression numeric = new CronExpression("0 0 12 ? * 2/2");

        DateTimeOffset after = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        for (int i = 0; i < 20; i++)
        {
            DateTimeOffset? expandedNext = expanded.GetNextValidTimeAfter(after);
            DateTimeOffset? numericNext = numeric.GetNextValidTimeAfter(after);

            expandedNext.Should().NotBeNull();
            expandedNext.Should().Be(numericNext);

            after = expandedNext.GetValueOrDefault();
        }
    }

    [Test]
    public void TestYearField()
    {
        CronExpressionBuilder.Create().WithYear(2030).ToString().Should().Be("* * * ? * * 2030");
        CronExpressionBuilder.Create().WithYears(2030, 2032).ToString().Should().Be("* * * ? * * 2030,2032");
        CronExpressionBuilder.Create().WithYearRange(2030, 2035).ToString().Should().Be("* * * ? * * 2030-2035");
        CronExpressionBuilder.Create().WithYearIncrements(2030, 2).ToString().Should().Be("* * * ? * * 2030/2");
    }

    [Test]
    public void TestListFieldsAcceptCollectionArguments()
    {
        // the params overloads take a ReadOnlySpan, but an already-materialised array still binds
        int[] seconds = [30, 0];
        DayOfWeek[] days = [DayOfWeek.Sunday, DayOfWeek.Wednesday];
        List<int> hours = [8, 16];

        CronExpressionBuilder.Create().WithSeconds(seconds).ToString().Should().Be("30,0 * * ? * *");
        CronExpressionBuilder.Create().WithDaysOfWeek(days).ToString().Should().Be("* * * ? * SUN,WED");
        CronExpressionBuilder.Create().WithHours(CollectionsMarshal.AsSpan(hours)).ToString().Should().Be("* * 8,16 ? * *");

        Invoking(x => x.WithSeconds((int[]) null)).Should().Throw<ArgumentException>();
        Invoking(x => x.WithDaysOfWeek((DayOfWeek[]) null)).Should().Throw<ArgumentException>();
    }

    [Test]
    public void TestOutOfRangeValuesAreRejected()
    {
        Invoking(x => x.WithSecond(60)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithSeconds(17, 61)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithSecondRange(20, 60)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithMinute(-1)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithHour(24)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithDayOfMonth(0)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithDayOfMonth(32)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.OnNearestWeekdayOfMonth(32)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithMonth(0)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithMonth(13)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithYear(1969)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithDaysOfWeek((DayOfWeek) 7)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 0)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 6)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void TestInvalidIncrementsAreRejected()
    {
        Invoking(x => x.WithSecondIncrements(0, 0)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithSecondIncrements(0, 60)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithMinuteIncrements(0, 60)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithHourIncrements(0, 24)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithDayOfMonthIncrements(1, 32)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithMonthIncrements(1, 13)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithDayOfWeekIncrements(DayOfWeek.Monday, 0)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithDayOfWeekIncrements(DayOfWeek.Monday, 8)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithYearIncrements(2030, 0)).Should().Throw<ArgumentOutOfRangeException>();
        // An unbounded year increment would overflow the "i += incr" loop in CronExpression.AddToSet
        // and silently produce a wrong schedule; the builder must reject it up front like every other field.
        Invoking(x => x.WithYearIncrements(2030, int.MaxValue)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void TestEmptyListsAreRejected()
    {
        Invoking(x => x.WithSeconds()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithMinutes()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithHours()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithDaysOfMonth()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithMonths()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithDaysOfWeek()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithYears()).Should().Throw<ArgumentException>();
    }

    [Test]
    public void TestYearRangeCannotWrap()
    {
        Invoking(x => x.WithYearRange(2035, 2030)).Should().Throw<ArgumentException>();
    }

    [Test]
    public void TestFieldCannotBeConfiguredTwice()
    {
        Invoking(x => x.WithSecond(1).WithSecond(2)).Should().Throw<InvalidOperationException>().WithMessage("Second has already been configured.");
        Invoking(x => x.WithSecond(1).WithSecondRange(2, 5)).Should().Throw<InvalidOperationException>().WithMessage("Second has already been configured.");
        Invoking(x => x.WithMinute(1).WithMinuteIncrements(0, 5)).Should().Throw<InvalidOperationException>().WithMessage("Minute has already been configured.");
        Invoking(x => x.WithHour(1).WithHours(2, 3)).Should().Throw<InvalidOperationException>().WithMessage("Hour has already been configured.");
        Invoking(x => x.OnLastDayOfMonth().WithDayOfMonth(3)).Should().Throw<InvalidOperationException>().WithMessage("Day-of-month has already been configured.");
        Invoking(x => x.WithMonth(1).WithMonthRange(2, 5)).Should().Throw<InvalidOperationException>().WithMessage("Month has already been configured.");
        Invoking(x => x.OnWeekdays().WithDaysOfWeek(DayOfWeek.Sunday)).Should().Throw<InvalidOperationException>().WithMessage("Day-of-week has already been configured.");
        Invoking(x => x.WithYear(2030).WithYearRange(2031, 2032)).Should().Throw<InvalidOperationException>().WithMessage("Year has already been configured.");
    }

    /// <summary>
    /// <c>AtTime</c> is the whole of a "daily at 09:30" schedule, and is what replaces the 3.x
    /// <c>DailyAtHourAndMinute</c> — one argument that says which number is which, rather than three
    /// that only their order distinguishes.
    /// </summary>
    [Test]
    public void TestAtTimeSetsSecondMinuteAndHour()
    {
        CronExpressionBuilder.Create().AtTime(new TimeOnly(9, 30)).ToString().Should().Be("0 30 9 ? * *");
        CronExpressionBuilder.Create().AtTime(new TimeOnly(23, 59, 59)).ToString().Should().Be("59 59 23 ? * *");
        CronExpressionBuilder.Create().AtTime(TimeOnly.MinValue).ToString().Should().Be("0 0 0 ? * *");
    }

    /// <summary>
    /// The two 3.x factories that took a time and a day are the two calls composed.
    /// </summary>
    [Test]
    public void TestAtTimeComposesWithDaySelection()
    {
        CronExpressionBuilder.Create()
            .AtTime(new TimeOnly(9, 30))
            .WithDaysOfWeek(DayOfWeek.Monday)
            .ToString().Should().Be("0 30 9 ? * MON");

        CronExpressionBuilder.Create()
            .AtTime(new TimeOnly(9, 30))
            .WithDaysOfWeek(DayOfWeek.Monday, DayOfWeek.Thursday)
            .ToString().Should().Be("0 30 9 ? * MON,THU");

        CronExpressionBuilder.Create()
            .AtTime(new TimeOnly(9, 30))
            .WithDayOfMonth(15)
            .ToString().Should().Be("0 30 9 15 * ?");
    }

    /// <summary>
    /// Cron resolves to a whole second, so a time carrying more than that keeps only the second.
    /// </summary>
    [Test]
    public void TestAtTimeIgnoresSubSecondPrecision()
    {
        CronExpressionBuilder.Create().AtTime(new TimeOnly(9, 30, 15, 500)).ToString().Should().Be("15 30 9 ? * *");
    }

    /// <summary>
    /// The three fields are checked together, so a builder that already carries one of them is left as
    /// it was rather than half-updated by the throw.
    /// </summary>
    [Test]
    public void TestAtTimeCannotOverwriteAFieldThatIsAlreadyConfigured()
    {
        Invoking(x => x.WithSecond(0).AtTime(new TimeOnly(9, 30))).Should().Throw<InvalidOperationException>().WithMessage("Second has already been configured.");
        Invoking(x => x.WithMinute(0).AtTime(new TimeOnly(9, 30))).Should().Throw<InvalidOperationException>().WithMessage("Minute has already been configured.");
        Invoking(x => x.WithHour(0).AtTime(new TimeOnly(9, 30))).Should().Throw<InvalidOperationException>().WithMessage("Hour has already been configured.");
        Invoking(x => x.AtTime(new TimeOnly(9, 30)).AtTime(new TimeOnly(10, 0))).Should().Throw<InvalidOperationException>().WithMessage("Second has already been configured.");

        CronExpressionBuilder builder = CronExpressionBuilder.Create().WithHour(0);
        Action act = () => builder.AtTime(new TimeOnly(9, 30));

        act.Should().Throw<InvalidOperationException>();
        builder.ToString().Should().Be("* * 0 ? * *",
            "the hour was rejected before the second and minute were written, so nothing was left half-applied");
    }

    [Test]
    public void TestDayOfMonthAndDayOfWeekAreMutuallyExclusive()
    {
        Invoking(x => x.WithDayOfMonth(10).WithDaysOfWeek(DayOfWeek.Monday)).Should().Throw<InvalidOperationException>().WithMessage("*both day-of-month and day-of-week*");
        Invoking(x => x.WithDaysOfWeek(DayOfWeek.Monday).WithDayOfMonth(10)).Should().Throw<InvalidOperationException>().WithMessage("*both day-of-month and day-of-week*");
        Invoking(x => x.OnLastDayOfMonth().OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 3)).Should().Throw<InvalidOperationException>().WithMessage("*both day-of-month and day-of-week*");
    }

    [Test]
    public void TestBuildRoundTrip()
    {
        CronExpressionBuilder builder = CronExpressionBuilder.Create()
            .WithSecond(0)
            .WithMinuteIncrements(0, 15)
            .WithHourRange(8, 17)
            .OnWeekdays();

        builder.ToString().Should().Be("0 0/15 8-17 ? * MON-FRI");
        builder.Build().CronExpressionString.Should().Be(builder.ToString());
    }

    [Test]
    public void TestAllSpecialFormsProduceValidExpressions()
    {
        CronExpressionBuilder[] builders =
        [
            CronExpressionBuilder.Create().OnLastDayOfMonth(),
            CronExpressionBuilder.Create().OnNearestWeekdayOfMonth(15),
            CronExpressionBuilder.Create().OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 3),
            CronExpressionBuilder.Create().OnLastDayOfWeekOfMonth(DayOfWeek.Thursday),
            CronExpressionBuilder.Create().OnLastDayOfWeek(),
            CronExpressionBuilder.Create().OnWeekdays(),
            CronExpressionBuilder.Create().WithDayOfWeekRange(DayOfWeek.Thursday, DayOfWeek.Sunday),
            CronExpressionBuilder.Create().WithSecondRange(55, 5).WithHourRange(22, 2),
            CronExpressionBuilder.Create().WithYearRange(2030, 2035),
            CronExpressionBuilder.Create().WithYearIncrements(2030, 2)
        ];

        foreach (CronExpressionBuilder builder in builders)
        {
            CronExpression.IsValidExpression(builder.ToString()).Should().BeTrue("expression '{0}' should be valid", builder.ToString());
        }
    }

    /// <summary>
    /// The round trip that matters: what the builder was asked for is what the parser resolves the
    /// built expression to. Everything above is a worked example checking the text the builder renders;
    /// this checks that the text means what the call meant, over randomly drawn values in every field
    /// and every shape - a single value, a list, a range that may run backwards, and a step.
    /// </summary>
    /// <remarks>
    /// A builder that renders a token the parser reads as something else passes every ToString
    /// assertion in this file and still schedules the wrong days. The expected sets here are this
    /// test's own arithmetic rather than anything read back out of <see cref="CronExpression" />.
    /// </remarks>
    /// <param name="seed">Fixed, and named in the case, so a failure is reproducible.</param>
    [TestCase(11)]
    [TestCase(22)]
    [TestCase(33)]
    [TestCase(44)]
    public void TestBuiltExpressionParsesBackToTheValuesTheBuilderWasGiven(int seed)
    {
        Random random = new Random(seed);

        for (int iteration = 0; iteration < 100; iteration++)
        {
            CronExpressionBuilder builder = CronExpressionBuilder.Create();

            HashSet<int> seconds = ConfigureField(random, 0, 59,
                value => builder.WithSecond(value),
                values => builder.WithSeconds(values),
                (from, to) => builder.WithSecondRange(from, to),
                (from, increment) => builder.WithSecondIncrements(from, increment));

            HashSet<int> minutes = ConfigureField(random, 0, 59,
                value => builder.WithMinute(value),
                values => builder.WithMinutes(values),
                (from, to) => builder.WithMinuteRange(from, to),
                (from, increment) => builder.WithMinuteIncrements(from, increment));

            HashSet<int> hours = ConfigureField(random, 0, 23,
                value => builder.WithHour(value),
                values => builder.WithHours(values),
                (from, to) => builder.WithHourRange(from, to),
                (from, increment) => builder.WithHourIncrements(from, increment));

            HashSet<int> months = ConfigureField(random, 1, 12,
                value => builder.WithMonth(value),
                values => builder.WithMonths(values),
                (from, to) => builder.WithMonthRange(from, to),
                (from, increment) => builder.WithMonthIncrements(from, increment));

            // The builder refuses to configure both day fields, exactly as the parser refuses to read both.
            bool useDayOfWeek = random.Next(2) == 0;
            HashSet<int> daysOfMonth = [];
            HashSet<int> daysOfWeek = [];

            if (useDayOfWeek)
            {
                daysOfWeek = ConfigureField(random, 1, 7,
                    value => builder.WithDaysOfWeek(ToDayOfWeek(value)),
                    values => builder.WithDaysOfWeek(values.Select(ToDayOfWeek).ToArray()),
                    (from, to) => builder.WithDayOfWeekRange(ToDayOfWeek(from), ToDayOfWeek(to)),
                    (from, increment) => builder.WithDayOfWeekIncrements(ToDayOfWeek(from), increment));
            }
            else
            {
                daysOfMonth = ConfigureField(random, 1, 31,
                    value => builder.WithDayOfMonth(value),
                    values => builder.WithDaysOfMonth(values),
                    (from, to) => builder.WithDayOfMonthRange(from, to),
                    (from, increment) => builder.WithDayOfMonthIncrements(from, increment));
            }

            int firstYear = random.Next(2030, 2041);
            int lastYear = firstYear + random.Next(0, 6);
            builder.WithYearRange(firstYear, lastYear);
            HashSet<int> years = [];
            for (int year = firstYear; year <= lastYear; year++)
            {
                years.Add(year);
            }

            string text = builder.ToString();

            builder.Build().CronExpressionString.Should().Be(text,
                "Build and ToString have to render the same expression, or one of them is a lie");

            CronExpression parsed = new CronExpression(text);

            parsed.GetSet(CronExpressionConstants.Second).Should().BeEquivalentTo(seconds, "expression '{0}'", text);
            parsed.GetSet(CronExpressionConstants.Minute).Should().BeEquivalentTo(minutes, "expression '{0}'", text);
            parsed.GetSet(CronExpressionConstants.Hour).Should().BeEquivalentTo(hours, "expression '{0}'", text);
            parsed.GetSet(CronExpressionConstants.Month).Should().BeEquivalentTo(months, "expression '{0}'", text);
            parsed.GetSet(CronExpressionConstants.Year).Should().BeEquivalentTo(years, "expression '{0}'", text);

            if (useDayOfWeek)
            {
                parsed.GetSet(CronExpressionConstants.DayOfWeek).Should().BeEquivalentTo(daysOfWeek, "expression '{0}'", text);
                parsed.GetSet(CronExpressionConstants.DayOfMonth).Should().Equal([CronExpressionConstants.NoSpec], "expression '{0}'", text);
            }
            else
            {
                parsed.GetSet(CronExpressionConstants.DayOfMonth).Should().BeEquivalentTo(daysOfMonth, "expression '{0}'", text);
                parsed.GetSet(CronExpressionConstants.DayOfWeek).Should().Equal([CronExpressionConstants.NoSpec], "expression '{0}'", text);
            }
        }
    }

    /// <summary>
    /// The special day forms have no value set to compare, so the round trip for them is the day they
    /// pick out of a month.
    /// </summary>
    [Test]
    public void TestSpecialDayFormsParseBackToTheDayTheyName()
    {
        // March 2024: 31 days, the 31st is a Sunday, and the last Thursday is the 28th.
        DateTimeOffset march = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);

        FirstFireAfter(CronExpressionBuilder.Create().WithSecond(0).WithMinute(0).WithHour(12).OnLastDayOfMonth(), march)
            .Should().Be(new DateTimeOffset(2024, 3, 31, 12, 0, 0, TimeSpan.Zero));

        FirstFireAfter(CronExpressionBuilder.Create().WithSecond(0).WithMinute(0).WithHour(12).OnNearestWeekdayOfMonth(16), march)
            .Should().Be(new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero), "the 16th is a Saturday, so the nearest weekday is the Friday before it");

        FirstFireAfter(CronExpressionBuilder.Create().WithSecond(0).WithMinute(0).WithHour(12).OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 3), march)
            .Should().Be(new DateTimeOffset(2024, 3, 15, 12, 0, 0, TimeSpan.Zero));

        FirstFireAfter(CronExpressionBuilder.Create().WithSecond(0).WithMinute(0).WithHour(12).OnLastDayOfWeekOfMonth(DayOfWeek.Thursday), march)
            .Should().Be(new DateTimeOffset(2024, 3, 28, 12, 0, 0, TimeSpan.Zero));

        FirstFireAfter(CronExpressionBuilder.Create().WithSecond(0).WithMinute(0).WithHour(12).OnLastDayOfWeek(), march)
            .Should().Be(new DateTimeOffset(2024, 3, 2, 12, 0, 0, TimeSpan.Zero), "'L' alone in the day-of-week field is Saturday");

        FirstFireAfter(CronExpressionBuilder.Create().WithSecond(0).WithMinute(0).WithHour(12).OnWeekdays(), march)
            .Should().Be(new DateTimeOffset(2024, 3, 1, 12, 0, 0, TimeSpan.Zero), "the 1st is a Friday");
    }

    private static DateTimeOffset? FirstFireAfter(CronExpressionBuilder builder, DateTimeOffset after)
    {
        return new CronExpression(builder.ToString(), TimeZoneInfo.Utc).GetNextValidTimeAfter(after);
    }

    private static DayOfWeek ToDayOfWeek(int quartzDayOfWeek) => (DayOfWeek) (quartzDayOfWeek - 1);

    /// <summary>
    /// Draws one of the four shapes a value field can take, configures the builder with it, and returns
    /// the values it stands for.
    /// </summary>
    private static HashSet<int> ConfigureField(
        Random random,
        int min,
        int max,
        Action<int> single,
        Action<int[]> list,
        Action<int, int> range,
        Action<int, int> increments)
    {
        int span = max - min + 1;

        switch (random.Next(4))
        {
            case 0:
            {
                int value = random.Next(min, max + 1);
                single(value);
                return [value];
            }

            case 1:
            {
                HashSet<int> values = [];
                int count = random.Next(1, 5);
                for (int i = 0; i < count; i++)
                {
                    values.Add(random.Next(min, max + 1));
                }

                list(values.ToArray());
                return values;
            }

            case 2:
            {
                int from = random.Next(min, max + 1);
                int to = random.Next(min, max + 1);
                range(from, to);

                // A range that runs backwards wraps through the top of the field and carries on from its bottom.
                HashSet<int> values = [];
                for (int value = from; value <= (from <= to ? to : to + span); value++)
                {
                    values.Add(value > max ? value - span : value);
                }

                return values;
            }

            default:
            {
                int from = random.Next(min, max + 1);
                int increment = random.Next(1, span);
                increments(from, increment);

                HashSet<int> values = [];
                for (int value = from; value <= max; value += increment)
                {
                    values.Add(value);
                }

                return values;
            }
        }
    }

    private static Action Invoking(Action<CronExpressionBuilder> action)
    {
        return () => action(CronExpressionBuilder.Create());
    }
}
