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
        CronExpressionBuilder.Create().OnDaysOfWeek(DayOfWeek.Thursday).ToString().Should().Be("* * * ? * THU");
        CronExpressionBuilder.Create().OnDaysOfWeek(DayOfWeek.Sunday, DayOfWeek.Wednesday).ToString().Should().Be("* * * ? * SUN,WED");
        CronExpressionBuilder.Create().OnDayOfWeekRange(DayOfWeek.Monday, DayOfWeek.Friday).ToString().Should().Be("* * * ? * MON-FRI");
        CronExpressionBuilder.Create().OnDayOfWeekRange(DayOfWeek.Thursday, DayOfWeek.Sunday).ToString().Should().Be("* * * ? * THU-SUN");
        CronExpressionBuilder.Create().OnNthDayOfWeekOfMonth(DayOfWeek.Sunday, 3).ToString().Should().Be("* * * ? * SUN#3");
        CronExpressionBuilder.Create().OnLastDayOfWeekOfMonth(DayOfWeek.Thursday).ToString().Should().Be("* * * ? * THUL");
        CronExpressionBuilder.Create().OnLastDayOfWeek().ToString().Should().Be("* * * ? * L");
        CronExpressionBuilder.Create().OnWeekdays().ToString().Should().Be("* * * ? * MON-FRI");
    }

    [Test]
    public void TestDayOfWeekIncrementsExpandToExplicitList()
    {
        CronExpressionBuilder.Create().OnDayOfWeekIncrements(DayOfWeek.Monday, 2).ToString().Should().Be("* * * ? * MON,WED,FRI");
        CronExpressionBuilder.Create().OnDayOfWeekIncrements(DayOfWeek.Sunday, 3).ToString().Should().Be("* * * ? * SUN,WED,SAT");
        CronExpressionBuilder.Create().OnDayOfWeekIncrements(DayOfWeek.Friday, 2).ToString().Should().Be("* * * ? * FRI");
        CronExpressionBuilder.Create().OnDayOfWeekIncrements(DayOfWeek.Sunday, 1).ToString().Should().Be("* * * ? * SUN,MON,TUE,WED,THU,FRI,SAT");
    }

    [Test]
    public void TestDayOfWeekIncrementsMatchNumericIncrementSemantics()
    {
        // numeric "2/2" means day 2 (MON) through SAT stepping by 2; the builder emits
        // the equivalent explicit day name list instead, since textual "MON/2" would
        // mean Quartz's every-second-week feature
        CronExpression expanded = CronExpressionBuilder.Create()
            .WithSecond(0)
            .WithMinute(0)
            .WithHour(12)
            .OnDayOfWeekIncrements(DayOfWeek.Monday, 2)
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
        Invoking(x => x.OnDaysOfWeek((DayOfWeek) 7)).Should().Throw<ArgumentOutOfRangeException>();
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
        Invoking(x => x.OnDayOfWeekIncrements(DayOfWeek.Monday, 0)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.OnDayOfWeekIncrements(DayOfWeek.Monday, 8)).Should().Throw<ArgumentOutOfRangeException>();
        Invoking(x => x.WithYearIncrements(2030, 0)).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void TestEmptyListsAreRejected()
    {
        Invoking(x => x.WithSeconds()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithMinutes()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithHours()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithDaysOfMonth()).Should().Throw<ArgumentException>();
        Invoking(x => x.WithMonths()).Should().Throw<ArgumentException>();
        Invoking(x => x.OnDaysOfWeek()).Should().Throw<ArgumentException>();
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
        Invoking(x => x.OnWeekdays().OnDaysOfWeek(DayOfWeek.Sunday)).Should().Throw<InvalidOperationException>().WithMessage("Day-of-week has already been configured.");
        Invoking(x => x.WithYear(2030).WithYearRange(2031, 2032)).Should().Throw<InvalidOperationException>().WithMessage("Year has already been configured.");
    }

    [Test]
    public void TestDayOfMonthAndDayOfWeekAreMutuallyExclusive()
    {
        Invoking(x => x.WithDayOfMonth(10).OnDaysOfWeek(DayOfWeek.Monday)).Should().Throw<InvalidOperationException>().WithMessage("*both day-of-month and day-of-week*");
        Invoking(x => x.OnDaysOfWeek(DayOfWeek.Monday).WithDayOfMonth(10)).Should().Throw<InvalidOperationException>().WithMessage("*both day-of-month and day-of-week*");
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
            CronExpressionBuilder.Create().OnDayOfWeekRange(DayOfWeek.Thursday, DayOfWeek.Sunday),
            CronExpressionBuilder.Create().WithSecondRange(55, 5).WithHourRange(22, 2),
            CronExpressionBuilder.Create().WithYearRange(2030, 2035),
            CronExpressionBuilder.Create().WithYearIncrements(2030, 2)
        ];

        foreach (CronExpressionBuilder builder in builders)
        {
            CronExpression.IsValidExpression(builder.ToString()).Should().BeTrue("expression '{0}' should be valid", builder.ToString());
        }
    }

    private static Action Invoking(Action<CronExpressionBuilder> action)
    {
        return () => action(CronExpressionBuilder.Create());
    }
}
