namespace Quartz.Tests.Unit;

public class DateBuilderTest
{
    [Test]
    public void BuildsTheDateItWasTold()
    {
        DateTimeOffset date = DateBuilder.Create()
            .InYear(2011)
            .InMonthOnDay(11, 14)
            .AtHourMinuteAndSecond(21, 59, 0)
            .Build();

        date.Year.Should().Be(2011);
        date.Month.Should().Be(11);
        date.Day.Should().Be(14);
        date.Hour.Should().Be(21);
        date.Minute.Should().Be(59);
        date.Second.Should().Be(0);
    }

    [Test]
    public void DefaultsUnsetFieldsToNow()
    {
        DateTimeOffset now = DateTimeOffset.Now;

        DateTimeOffset date = DateBuilder.Create().AtHourMinuteAndSecond(3, 4, 5).Build();

        date.Year.Should().Be(now.Year);
        date.Month.Should().Be(now.Month);
        date.Hour.Should().Be(3);
        date.Minute.Should().Be(4);
        date.Second.Should().Be(5);
    }

    [Test]
    public void UsesTheOffsetOfTheGivenTimeZone()
    {
        TimeZoneInfo timeZone = TestTimeZones.CentralEuropean;

        DateTimeOffset date = DateBuilder.CreateInTimeZone(timeZone)
            .InYear(2011)
            .InMonthOnDay(11, 14)
            .AtHourMinuteAndSecond(21, 59, 0)
            .Build();

        date.Offset.Should().Be(timeZone.GetUtcOffset(new DateTime(2011, 11, 14, 21, 59, 0)));
    }

    [TestCase(24)]
    [TestCase(-1)]
    public void RejectsAnHourOutsideTheDay(int hour)
    {
        Action act = () => DateBuilder.Create().AtHourOfDay(hour);

        act.Should().Throw<ArgumentException>().WithMessage("*hour*");
    }

    [TestCase(0)]
    [TestCase(32)]
    public void RejectsADayOutsideTheMonth(int day)
    {
        Action act = () => DateBuilder.Create().OnDay(day);

        act.Should().Throw<ArgumentException>().WithMessage("*day of month*");
    }
}
