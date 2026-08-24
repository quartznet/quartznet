namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/simpletriggers.md.
/// </summary>
public static class SimpleTriggersSamples
{
    public static void OneShot(DateTimeOffset myStartTime)
    {
        #region sample_simpletriggers_one_shot

        // trigger builder creates simple trigger by default
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger1", "group1")
            .StartAt(myStartTime) // some Date
            .ForJob("job1", "group1") // identify job with name, group strings
            .Build();

        #endregion
    }

    public static void RepeatTenTimes(DateTimeOffset myTimeToStartFiring, IJobDetail myJob)
    {
        #region sample_simpletriggers_repeat_ten_times

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger3", "group1")
            .StartAt(myTimeToStartFiring) // if a start time is not given (if this line were omitted), "now" is implied
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromSeconds(10))
                .WithRepeatCount(10)) // note that 10 repeats will give a total of 11 firings
            .ForJob(myJob) // identify job with handle to its JobDetail itself
            .Build();

        #endregion
    }

    public static void FiveMinutesFromNow(JobKey myJobKey)
    {
        #region sample_simpletriggers_five_minutes_from_now

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger5", "group1")
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
            .ForJob(myJobKey) // identify job with its JobKey
            .Build();

        #endregion
    }

    public static void EveryFiveMinutesUntilTen()
    {
        #region sample_simpletriggers_repeat_until_end_time

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger7", "group1")
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromMinutes(5))
                .RepeatForever())
            .EndAt(DateBuilder.Create().AtHourMinuteAndSecond(22, 0, 0).Build())
            .Build();

        #endregion
    }

    public static async ValueTask EveryTwoHoursForever(IScheduler scheduler, IJobDetail job)
    {
        #region sample_simpletriggers_every_two_hours

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger8") // because group is not specified, "trigger8" will be in the default group
            .StartAt(DateBuilder.Create().AtMinute(0).AtSecond(0).Build().AddHours(1)) // the next even hour
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromHours(2))
                .RepeatForever())
            // note that in this example, 'ForJob(..)' is not called
            //  - which is valid if the trigger is passed to the scheduler along with the job
            .Build();

        await scheduler.ScheduleJob(job, trigger);

        #endregion
    }

    public static void MisfireInstruction()
    {
        #region sample_simpletriggers_misfire_instruction

        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("trigger7", "group1")
            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromMinutes(5))
                .RepeatForever()
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount))
            .Build();

        #endregion
    }
}
