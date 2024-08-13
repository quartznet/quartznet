namespace Quartz.Tests.Unit;

public class CronScheduleBuilderTest
{
    [Test]
    public void TestAtHourAndMinuteOnGivenDaysOfWeek()
    {
        var trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(10, 0, DayOfWeek.Monday, DayOfWeek.Thursday, DayOfWeek.Friday))
            .Build();

        Assert.That(trigger.CronExpressionString, Is.EqualTo("0 0 10 ? * 2,5,6"));

        trigger = (ICronTrigger) TriggerBuilder.Create().WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(10, 0, DayOfWeek.Wednesday))
            .Build();

        Assert.That(trigger.CronExpressionString, Is.EqualTo("0 0 10 ? * 4"));
    }
}