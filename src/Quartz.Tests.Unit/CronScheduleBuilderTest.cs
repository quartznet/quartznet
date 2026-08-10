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
            .OnDaysOfWeek(DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Friday)
            .Build();

        ICronTrigger trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.Create(expression))
            .Build();

        trigger.CronExpressionString.Should().Be("0 0 10 ? * MON,THU,FRI");
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
