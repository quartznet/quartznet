using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/recurrencetrigger.md.
/// </summary>
public static class RecurrenceTriggerSamples
{
    public static void SecondMondayOfEveryMonth()
    {
        #region sample_recurrencetrigger_second_monday

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("monthlyTrigger", "group1")
            .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
            .StartAt(DateBuilder.Create().InYear(2025).InMonthOnDay(1, 1).AtHourMinuteAndSecond(9, 0, 0).Build())
            .Build();

        #endregion
    }

    public static void EveryOtherWeek()
    {
        #region sample_recurrencetrigger_every_other_week

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("weeklyTrigger", "group1")
            .WithRecurrenceSchedule("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR")
            .StartNow()
            .Build();

        #endregion
    }

    public static void LastWeekdayOfMarch()
    {
        #region sample_recurrencetrigger_last_weekday_of_march

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("yearlyTrigger", "group1")
            .WithRecurrenceSchedule("FREQ=YEARLY;BYMONTH=3;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1")
            .StartNow()
            .Build();

        #endregion
    }

    public static void EveryWeekday()
    {
        #region sample_recurrencetrigger_every_weekday

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("weekdayTrigger", "group1")
            .WithRecurrenceSchedule("FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR")
            .StartNow()
            .Build();

        #endregion
    }

    public static void LastDayOfEveryMonth()
    {
        #region sample_recurrencetrigger_last_day_of_month

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("lastDayTrigger", "group1")
            .WithRecurrenceSchedule("FREQ=MONTHLY;BYMONTHDAY=-1")
            .StartNow()
            .Build();

        #endregion
    }

    public static void QuarterlyWithACount()
    {
        #region sample_recurrencetrigger_quarterly

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("quarterlyTrigger", "group1")
            .WithRecurrenceSchedule("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=1,15;COUNT=10")
            .StartNow()
            .Build();

        #endregion
    }

    public static void InATimeZone()
    {
        #region sample_recurrencetrigger_in_time_zone

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO", b => b
                .InTimeZone(TimeZones.FindById("Eastern Standard Time")))
            .StartNow()
            .Build();

        #endregion
    }

    public static void UnderDependencyInjection(IServiceCollection services)
    {
        #region sample_recurrencetrigger_under_di

        services.AddQuartz(q =>
        {
            q.AddJob<MyJob>(j => j.WithIdentity("myJob"));
            q.AddTrigger(t => t
                .ForJob("myJob")
                .WithIdentity("myTrigger")
                .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
                .StartNow());
        });

        #endregion
    }

    public static void MisfireInstruction()
    {
        #region sample_recurrencetrigger_misfire_instruction

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .WithRecurrenceSchedule("FREQ=WEEKLY;BYDAY=MO", b => b
                .WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.DoNothing))
            .Build();

        #endregion
    }
}
