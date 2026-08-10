namespace Quartz.Tests.Unit;

public class CronScheduleBuilderTest
{
    [Test]
    public void TakesACronExpressionAsAString()
    {
        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *"))
            .Build();

        trigger.CronExpressionString.Should().Be("0 20 10 ? * *");
    }

    [Test]
    public void TakesACronExpressionBuiltElsewhere()
    {
        CronExpression expression = CronExpressionBuilder.Create()
            .WithSecond(0)
            .WithMinute(0)
            .WithHour(10)
            .WithDaysOfWeek(DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Friday)
            .Build();

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create(expression))
            .Build();

        trigger.CronExpressionString.Should().Be("0 0 10 ? * MON,THU,FRI");
    }

    [Test]
    public void WithCronScheduleTakesACronExpressionDirectly()
    {
        CronExpression expression = new CronExpression("0 0 10 ? * MON", TimeZoneInfo.Utc);

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithCronSchedule(expression)
            .Build();

        trigger.CronExpressionString.Should().Be("0 0 10 ? * MON");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Test]
    public void WithCronScheduleTakesACronExpressionBuilderDirectly()
    {
        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithCronSchedule(
                CronExpressionBuilder.Create()
                    .WithSecond(0)
                    .WithMinuteIncrements(0, 15)
                    .WithHourRange(8, 17)
                    .OnWeekdays(),
                x => x.InTimeZone(TimeZoneInfo.Utc))
            .Build();

        trigger.CronExpressionString.Should().Be("0 0/15 8-17 ? * MON-FRI");
        trigger.TimeZone.Should().Be(TimeZoneInfo.Utc);
    }

    [Test]
    public void RejectsAnExpressionItCannotParse()
    {
        Action act = () => CronScheduleBuilder.Create("not a cron expression");

        act.Should().Throw<FormatException>();
    }

    [Test]
    public void CarriesTheTimeZoneOntoTheTrigger()
    {
        TimeZoneInfo timeZone = TestTimeZones.CentralEuropean;

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *").InTimeZone(timeZone))
            .Build();

        trigger.TimeZone.Should().Be(timeZone);
    }

    [Test]
    public void FallsBackToTheLocalTimeZoneWhenGivenNull()
    {
        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *").InTimeZone(null))
            .Build();

        trigger.TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    [Test]
    public void ChangingTheTimeZoneDoesNotRetimeAlreadyBuiltTriggers()
    {
        CronScheduleBuilder schedule = CronScheduleBuilder.Create("0 20 10 ? * *");

        ICronTrigger first = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("first")
            .WithSchedule(schedule)
            .Build();

        schedule.InTimeZone(TestTimeZones.CentralEuropean);

        ICronTrigger second = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("second")
            .WithSchedule(schedule)
            .Build();

        first.TimeZone.Should().Be(TimeZoneInfo.Local,
            "the builder used to hand every trigger the same mutable CronExpression, so a later InTimeZone silently retimed triggers that were already built");
        second.TimeZone.Should().Be(TestTimeZones.CentralEuropean);
    }

    [Test]
    public void ChangingTheTimeZoneDoesNotRetimeTheCallersExpression()
    {
        CronExpression expression = new CronExpression("0 20 10 ? * *");

        TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create(expression).InTimeZone(TestTimeZones.CentralEuropean))
            .Build();

        expression.TimeZone.Should().Be(TimeZoneInfo.Local,
            "InTimeZone must reshape the builder's copy, not write through the instance the caller still holds");
    }

    [Test]
    public void DefaultsToTheSmartMisfirePolicy()
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create("0 20 10 ? * *"))
            .Build();

        trigger.MisfireInstructionCode.Should().Be(MisfireInstruction.SmartPolicy);
    }

    [TestCase(CronTriggerMisfireInstruction.IgnoreMisfires, MisfireInstruction.IgnoreMisfirePolicy)]
    [TestCase(CronTriggerMisfireInstruction.DoNothing, MisfireInstruction.CronTrigger.DoNothing)]
    [TestCase(CronTriggerMisfireInstruction.FireAndProceed, MisfireInstruction.CronTrigger.FireOnceNow)]
    public void StoresTheMisfireInstructionAsItsConstant(CronTriggerMisfireInstruction instruction, int stored)
    {
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("test")
            .WithCronSchedule("0 20 10 ? * *", x => x.WithMisfireInstruction(instruction))
            .Build();

        trigger.MisfireInstructionCode.Should().Be(stored);
    }
}
