namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/crontriggers.md.
/// </summary>
public static class CronTriggersSamples
{
    public static void EveryOtherMinuteDuringBusinessHours()
    {
        #region sample_crontriggers_every_other_minute

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .WithCronSchedule("0 0/2 8-17 * * ?")
            .ForJob("myJob", "group1")
            .Build();

        #endregion
    }

    public static void AssembledInline()
    {
        #region sample_crontriggers_inline

        ITrigger weekdays = TriggerBuilder.Create()
            .WithIdentity("nightly", "group1")
            .WithCronSchedule(cron => cron.AtTime(new TimeOnly(3, 0)).OnWeekdays())  // "0 0 3 ? * MON-FRI"
            .ForJob("myJob", "group1")
            .Build();

        ITrigger everyTenMinutes = TriggerBuilder.Create()
            .WithIdentity("poll", "group1")
            .WithCronSchedule(cron => cron.Every(TimeSpan.FromMinutes(10)))          // "0 0/10 * ? * *"
            .ForJob("myJob", "group1")
            .Build();

        #endregion
    }

    public static void DailyAtTenFortyTwo(JobKey myJobKey)
    {
        #region sample_crontriggers_daily_question_mark_in_day_of_week

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .WithCronSchedule("0 42 10 ? * *")
            .ForJob(myJobKey)
            .Build();

        #endregion
    }

    public static void DailyAtTenFortyTwoQuestionMarkInDayOfMonth()
    {
        #region sample_crontriggers_daily_question_mark_in_day_of_month

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .WithCronSchedule("0 42 10 * * ?")
            .ForJob("myJob", "group1")
            .Build();

        #endregion
    }

    public static void InATimeZone(JobKey myJobKey)
    {
        #region sample_crontriggers_in_time_zone

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .WithCronSchedule("0 42 10 ? * WED", x => x
                .InTimeZone(TimeZones.FindById("Central America Standard Time")))
            .ForJob(myJobKey)
            .Build();

        #endregion
    }

    public static void WithAScheduleBuiltSeparately(JobKey myJobKey)
    {
        #region sample_crontriggers_schedule_built_separately

        CronScheduleBuilder schedule = CronScheduleBuilder
            .Create("0 42 10 ? * WED")
            .InTimeZone(TimeZones.FindById("Central America Standard Time"));

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .WithCronSchedule(schedule)
            .ForJob(myJobKey)
            .Build();

        #endregion
    }

    public static void HashedFireTime()
    {
        #region sample_crontriggers_hashed_fire_time

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("nightly-cleanup", "maintenance")
            .WithCronSchedule("0 H H(0-7) * * ?")
            .ForJob("cleanupJob", "maintenance")
            .Build();

        #endregion
    }

    public static void MisfireInstruction()
    {
        #region sample_crontriggers_misfire_instruction

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .WithCronSchedule("0 0/2 8-17 * * ?", x => x
                .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
            .ForJob("myJob", "group1")
            .Build();

        #endregion
    }
}
