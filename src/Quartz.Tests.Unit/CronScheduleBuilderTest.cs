using System;

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

        Assert.AreEqual("0 0 10 ? * MON,THU,FRI", trigger.CronExpressionString);

        trigger = (ICronTrigger) TriggerBuilder.Create().WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(10, 0, DayOfWeek.Wednesday))
            .Build();

        Assert.AreEqual("0 0 10 ? * WED", trigger.CronExpressionString);
    }

    [Test]
    public void TestAtHourAndMinuteOnGivenDaysOfWeekRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(25, 0, DayOfWeek.Monday));
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(13, 68, DayOfWeek.Monday));
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(13, 25));
    }

    [Test]
    public void TestDailyAtHourAndMinute()
    {
        var trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(10, 20))
            .Build();

        Assert.AreEqual("0 20 10 ? * *", trigger.CronExpressionString);
    }

    [Test]
    public void TestDailyAtHourAndMinuteRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.DailyAtHourAndMinute(26, 23));
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.DailyAtHourAndMinute(11, 78));
    }

    [Test]
    public void TestWeeklyOnDayAndHourAndMinute()
    {
        var trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Saturday, 11, 41))
            .Build();

        Assert.AreEqual("0 41 11 ? * SAT", trigger.CronExpressionString);
    }

    [Test]
    public void TestWeeklyOnDayAndHourAndMinuteRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Monday, 25, 2));
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(DayOfWeek.Monday, 2, 62));

        var exception = Assert.Throws<ArgumentOutOfRangeException>(() => CronScheduleBuilder.WeeklyOnDayAndHourAndMinute((DayOfWeek) 8, 10, 0));
        Assert.AreEqual("dayOfWeek", exception.ParamName);
    }

    [Test]
    public void TestMonthlyOnDayAndHourAndMinute()
    {
        var trigger = (ICronTrigger) TriggerBuilder.Create()
            .WithIdentity("test")
            .WithSchedule(CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(6, 18, 30))
            .Build();

        Assert.AreEqual("0 30 18 6 * ?", trigger.CronExpressionString);
    }

    [Test]
    public void TestMonthlyOnDayAndHourAndMinuteRejectsInvalidArguments()
    {
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(32, 18, 30));
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(18, 25, 1));
        Assert.Throws<ArgumentException>(() => CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(16, 19, 61));
    }
}
