using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl.Calendar;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/more-about-triggers.md.
/// </summary>
public static class MoreAboutTriggersSamples
{
    public static void MisfireInstructionOnTheScheduleBuilder()
    {
        ITrigger trigger = TriggerBuilder.Create()
            #region sample_more_about_triggers_misfire_instruction

            .WithSimpleSchedule(x => x
                .WithInterval(TimeSpan.FromMinutes(5))
                .RepeatForever()
                .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount))

            #endregion
            .Build();
    }

    public static void ExecutionGroup()
    {
        #region sample_more_about_triggers_execution_group

        TriggerBuilder.Create()
            .WithIdentity("myTrigger")
            .WithExecutionGroup("batch-jobs")
            // ...
            .Build();

        #endregion
    }

    public static async ValueTask Calendars(IScheduler scheduler)
    {
        #region sample_more_about_triggers_calendar

        HolidayCalendar holidays = new();
        holidays.AddExcludedDay(new DateOnly(2026, 12, 24));

        await scheduler.AddCalendar("myHolidays", holidays);

        ITrigger t = TriggerBuilder.Create()
            .WithIdentity("myTrigger")
            .ForJob("myJob")
            .WithCronSchedule("0 30 9 ? * *")  // execute job daily at 9:30
            .WithCalendarName("myHolidays")    // but not on holidays
            .Build();

        ITrigger t2 = TriggerBuilder.Create()
            .WithIdentity("myTrigger2")
            .ForJob("myJob2")
            .WithCronSchedule("0 30 11 ? * *") // execute job daily at 11:30
            .WithCalendarName("myHolidays")    // but not on holidays
            .Build();

        // Use H (hash) to spread triggers across time instead of a fixed schedule.
        // The trigger identity is used as the hash seed, so each trigger fires at a unique time.
        ITrigger t3 = TriggerBuilder.Create()
            .WithIdentity("myTrigger3")
            .ForJob("myJob3")
            .WithCronSchedule("0 H H(9-17) * * ?") // a hash-derived time during business hours
            .WithCalendarName("myHolidays")
            .Build();

        // .. schedule jobs with triggers

        #endregion
    }

    public static async ValueTask ReplacingACalendar(IScheduler scheduler, HolidayCalendar holidays)
    {
        #region sample_more_about_triggers_replace_calendar

        await scheduler.AddCalendar("myHolidays", holidays, new AddCalendarOptions
        {
            Replace = true,        // there is already a calendar under this name
            UpdateTriggers = true, // recompute the next fire time of every trigger using it
        });

        #endregion
    }

    public static void RegisteringACalendarAtConfigurationTime(IServiceCollection services)
    {
        services.AddQuartz(q =>
        {
            #region sample_more_about_triggers_add_calendar_at_configuration_time

            q.AddCalendar<HolidayCalendar>("myHolidays", new AddCalendarOptions { Replace = true }, calendar =>
            {
                calendar.AddExcludedDay(new DateOnly(2026, 12, 24));
            });

            #endregion
        });
    }
}
