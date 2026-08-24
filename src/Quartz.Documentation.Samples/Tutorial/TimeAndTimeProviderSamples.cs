using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/time-and-timeprovider.md.
/// </summary>
public static class TimeAndTimeProviderSamples
{
    public static void RegisteringATimeProvider(IHostApplicationBuilder builder, TimeProvider myTimeProvider)
    {
        #region sample_time_provider_registration

        builder.Services.AddQuartz(q =>
        {
            q.UseTimeProvider(myTimeProvider);
        });

        #endregion
    }

    public static async ValueTask RegisteringATimeProviderStandalone(TimeProvider myTimeProvider)
    {
        #region sample_time_provider_standalone

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .UseTimeProvider(myTimeProvider)
            .BuildScheduler();

        #endregion
    }

    public static void ScheduleBuildersReadNoClock(string cronExpression, string recurrenceRule)
    {
        #region sample_time_provider_schedule_builders

        CronScheduleBuilder.Create(cronExpression);
        SimpleScheduleBuilder.Create();
        CalendarIntervalScheduleBuilder.Create();
        DailyTimeIntervalScheduleBuilder.Create();
        RecurrenceScheduleBuilder.Create(recurrenceRule);

        #endregion
    }

    public static void DateBuilderReadsTheClock(TimeProvider timeProvider, TimeZoneInfo tz)
    {
        #region sample_time_provider_date_builder

        DateTimeOffset when = DateBuilder.Create(timeProvider).InYear(2027).InMonthOnDay(3, 15).AtHourOfDay(9).Build();
        DateTimeOffset local = DateBuilder.CreateInTimeZone(tz, timeProvider).AtHourMinuteAndSecond(9, 30, 0).Build();

        #endregion
    }

    public static void ConfiguredTriggersSeeTheFakeClock(IHostApplicationBuilder builder, TimeProvider fakeClock)
    {
        #region sample_time_provider_configured_trigger

        builder.Services.AddQuartz(q =>
        {
            q.UseTimeProvider(fakeClock);

            // this trigger's implicit start time is the fake clock's now
            q.AddTrigger<ReportJob>(t => t
                .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
        });

        #endregion
    }

    public static void ABuilderWithNoClockUsesTheWallClock()
    {
        #region sample_time_provider_wall_clock_trigger

        // StartTimeUtc is the WALL CLOCK, whatever the scheduler's TimeProvider says
        ITrigger trigger = TriggerBuilder.Create()
            .WithIdentity("hourly")
            .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        #endregion
    }

    public static void HandingTheBuilderTheClock(TimeProvider fakeClock)
    {
        #region sample_time_provider_trigger_builder_clock

        ITrigger trigger = TriggerBuilder.Create(fakeClock)
            .WithIdentity("hourly")
            .StartAt(fakeClock.GetUtcNow())
            .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
            .Build();

        #endregion
    }

    public static void TimeZoneOnTheSchedule()
    {
        #region sample_time_provider_time_zone

        TriggerBuilder.Create()
            .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(TimeZones.FindById("Europe/Helsinki")))
            .Build();

        #endregion
    }
}
