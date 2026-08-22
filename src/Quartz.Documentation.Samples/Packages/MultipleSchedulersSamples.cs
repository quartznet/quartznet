using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;
using Quartz.Impl.Calendar;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/multiple-schedulers.md.
/// </summary>
public static class MultipleSchedulersSamples
{
    public static void TwoSchedulers(string[] args)
    {
        #region sample_multiple_two_schedulers

        var builder = Host.CreateApplicationBuilder(args);

        // First scheduler: fast in-memory jobs
        builder.Services.AddQuartz("FastScheduler", q =>
        {
            q.UseInMemoryStore();
            q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 5);

            q.ScheduleJob<NotificationJob>(trigger => trigger
                .WithIdentity("notify-trigger")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(30)).RepeatForever()));
        });

        // Second scheduler: persistent database jobs
        builder.Services.AddQuartz("DurableScheduler", q =>
        {
            q.UsePersistentStore(s =>
            {
                s.UseSqlServer(sqlServer =>
                {
                    sqlServer.ConnectionString = "your connection string";
                });
                s.UseSystemTextJsonSerializer();
            });

            q.ScheduleJob<ReportJob>(trigger => trigger
                .WithIdentity("report-trigger")
                .WithCronSchedule("0 0 2 * * ?"));
        });

        // Single call starts all named schedulers
        builder.Services.AddQuartzHostedService(options =>
        {
            options.WaitForJobsToComplete = true;
        });

        builder.Build().Run();

        #endregion
    }

    public static void PerSchedulerListenersAndCalendars(IHostApplicationBuilder builder)
    {
        #region sample_multiple_per_scheduler_listeners

        builder.Services.AddQuartz("Scheduler1", q =>
        {
            q.AddSchedulerListener<AuditSchedulerListener>();
            q.AddJobListener<LoggingJobListener>();
            q.AddTriggerListener<MetricsTriggerListener>();

            q.AddCalendar<HolidayCalendar>("holidays", new AddCalendarOptions { Replace = true, UpdateTriggers = true },
                cal => cal.AddExcludedDay(new DateOnly(2025, 12, 25)));
            // These listeners and calendars only apply to Scheduler1
        });

        builder.Services.AddQuartz("Scheduler2", q =>
        {
            // Scheduler2 has no listeners or calendars unless explicitly added here
        });

        #endregion
    }

    /// <summary>
    /// Container for the keyed-service sample, so that the repository sample below can show a class of
    /// the same name — as the page does.
    /// </summary>
    public static class KeyedService
    {
        #region sample_multiple_keyed_service

        public class MyService
        {
            private readonly IScheduler scheduler;

            public MyService([FromKeyedServices("FastScheduler")] IScheduler scheduler)
            {
                this.scheduler = scheduler;
            }

            public async Task DoWork()
            {
                await scheduler.TriggerJob(new JobKey("my-job"));
            }
        }

        #endregion
    }

    public static void ResolvingSchedulers(IServiceProvider provider)
    {
        #region sample_multiple_resolving

        var fast = provider.GetRequiredKeyedService<IScheduler>("FastScheduler");
        var standard = provider.GetRequiredService<IScheduler>();   // the default scheduler, if one is registered

        #endregion
    }

    public static class Repository
    {
        #region sample_multiple_scheduler_repository

        public class MyService
        {
            private readonly ISchedulerRepository schedulerRepository;

            public MyService(ISchedulerRepository schedulerRepository)
            {
                this.schedulerRepository = schedulerRepository;
            }

            public async Task DoWork()
            {
                var scheduler = schedulerRepository.Lookup("FastScheduler");
                if (scheduler != null)
                {
                    await scheduler.TriggerJob(new JobKey("my-job"));
                }

                // Or every scheduler this container has built
                var all = schedulerRepository.LookupAll();
            }
        }

        #endregion
    }

    public static void DefaultAndNamed(IHostApplicationBuilder builder)
    {
        #region sample_multiple_default_and_named

        // Default scheduler (traditional single-scheduler usage)
        builder.Services.AddQuartz(q =>
        {
            q.ScheduleJob<MainJob>(trigger => trigger
                .WithIdentity("main-trigger")
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()));
        });

        // Additional named scheduler
        builder.Services.AddQuartz("Auxiliary", q =>
        {
            q.ScheduleJob<CleanupJob>(trigger => trigger
                .WithIdentity("cleanup-trigger")
                .WithCronSchedule("0 0 3 * * ?"));
        });

        // Starts both the default and the named scheduler
        builder.Services.AddQuartzHostedService();

        #endregion
    }

    public static void NamedSchedulerFromConfiguration(HostApplicationBuilder builder)
    {
        #region sample_multiple_named_from_configuration

        builder.AddQuartz("DurableScheduler");
        // or, naming the section yourself:
        builder.Services.AddQuartz("DurableScheduler", builder.Configuration.GetSection("Quartz"));

        #endregion
    }

    public static void EverySchedulerFromConfiguration(HostApplicationBuilder builder)
    {
        #region sample_multiple_all_from_configuration

        builder.AddQuartzSchedulers();
        // or:
        builder.Services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));

        #endregion
    }

    public static void NamedOptions(IHostApplicationBuilder builder)
    {
        #region sample_multiple_named_options

        builder.Services.Configure<QuartzOptions>("DurableScheduler",
            options => options.Properties["quartz.jobStore.someThirdPartySetting"] = "value");

        #endregion
    }

    public static void PerSchedulerHostedService(IHostApplicationBuilder builder)
    {
        #region sample_multiple_hosted_services

        // shared by every scheduler
        builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        // ...except this one, which waits longer before its first fire
        builder.Services.AddQuartzHostedService("DurableScheduler", options =>
        {
            options.StartDelay = TimeSpan.FromMinutes(2);
        });

        #endregion
    }
}
