using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Impl.Calendar;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/microsoft-di-integration.md.
/// </summary>
public static class MicrosoftDiIntegrationSamples
{
    public static void RegisteringAScheduler(string[] args)
    {
        #region sample_di_registering_a_scheduler

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        builder.AddQuartz(q =>
        {
            q.ScheduleJob<ExampleJob>(trigger => trigger
                .WithIdentity("example")
                .WithCronSchedule("0 0/5 * * * ?"));
        });

        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void BindingAConfigurationSection(IServiceCollection services, IConfiguration configuration)
    {
        #region sample_di_configuration_section

        services.AddQuartz(configuration.GetSection("Quartz"), q =>
        {
            // code configuration on top of what the file says; code wins
            q.ConfigureScheduler(options => options.InstanceId = "Scheduler-Core");
        });

        #endregion
    }

    public static void YourOwnRegistrationWins(IServiceCollection services)
    {
        #region sample_di_registration_wins

        // your lifetime, your factory, your implementation type - kept
        services.AddSingleton<SendReportsJob>(_ => SendReportsJob.ForTenant("acme"));

        services.AddQuartz(q =>
        {
            q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports"));
        });

        #endregion
    }

    public static void ValidateOnBuildSeesTheJob(IServiceCollection services)
    {
        #region sample_di_validate_on_build

        services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));

        // throws: Unable to resolve service for type 'IReportStore' while attempting to activate 'SendReportsJob'

        #endregion
    }

    #region sample_di_instantiation_failure_listener

    public sealed class InstantiationFailureListener(ILogger<InstantiationFailureListener> logger) : ISchedulerListener
    {
        public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
        {
            if (errorContext.Exception is JobInstantiationException failure)
            {
                logger.LogError(failure, "Job {Job} could not be built for trigger {Trigger}, fire {FireInstanceId}, on scheduler {SchedulerName}",
                    errorContext.JobKey, errorContext.TriggerKey, errorContext.FireInstanceId, scheduler.SchedulerName);
            }

            return default;
        }
    }

    #endregion

    public static void PersistentStore(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_di_persistent_store

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(store =>
            {
                store.UseSqlServer(connectionString);
                store.UseSystemTextJsonSerializer();

                store.ConfigureStore(options =>
                {
                    options.TablePrefix = "QRTZ_";        // the default
                    options.StoreJobDataAsStrings = true; // preferred, but not the default
                    options.SchemaProvisioning = SchemaProvisioning.Validate; // the default
                });

                store.UseClustering(cluster =>
                {
                    cluster.CheckinInterval = TimeSpan.FromSeconds(10);
                    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                });
            });
        });

        #endregion
    }

    public static void DuplicateSchedulingData(IServiceCollection services)
    {
        #region sample_di_duplicate_scheduling_data

        services.Configure<QuartzOptions>(options =>
        {
            options.Scheduling.OverwriteExistingData = true; // default: true
            options.Scheduling.IgnoreDuplicates = false;     // default: false
        });

        #endregion
    }

    public static void JobsAndTriggers(IHostApplicationBuilder builder)
    {
        #region sample_di_jobs_and_triggers

        builder.Services.AddQuartz(q =>
        {
            q.ScheduleJob<ExampleJob>(trigger => trigger
                .WithIdentity("Combined Configuration Trigger")
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(7))
                .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
                .WithDescription("my awesome trigger configured for a job with single call"));

            JobKey jobKey = new("awesome job", "awesome group");

            q.AddJob<ExampleJob>(j => j
                .WithIdentity(jobKey)
                .WithDescription("my awesome job")
                // job data can name the job property it is meant for instead of spelling its key,
                // which makes a mistyped key or a wrong-typed value a compile error
                .UsingJobData(x => x.InjectedString, "Hello")
                .UsingJobData(x => x.InjectedBool, true));

            q.AddTrigger<ExampleJob>(t => t
                .WithIdentity("Simple Trigger")
                .ForJob(jobKey)
                .StartNow()
                .WithSimpleSchedule(TimeSpan.FromSeconds(10)));

            q.AddTrigger<ExampleJob>(t => t
                .WithIdentity("Cron Trigger")
                .ForJob(jobKey)
                .StartAt(DateTimeOffset.UtcNow.AddSeconds(3))
                .WithCronSchedule("0/3 * * * * ?"));

            // use H (hash) to spread trigger fire times based on trigger identity
            q.AddTrigger<ExampleJob>(t => t
                .WithIdentity("Spread Cron Trigger")
                .ForJob(jobKey)
                .WithCronSchedule("H * * * * ?")
                .WithDescription("fires once per minute at a hash-derived second"));
        });

        #endregion
    }

    public static void TheRestOfTheWorkedConfiguration(IHostApplicationBuilder builder)
    {
        builder.Services.AddQuartz(q =>
        {
            JobKey jobKey = new("awesome job", "awesome group");

            #region sample_di_calendars

            const string calendarName = "myHolidayCalendar";

            q.AddCalendar<HolidayCalendar>(
                name: calendarName,
                options: new AddCalendarOptions { Replace = true, UpdateTriggers = true },
                configure: calendar => calendar.AddExcludedDay(new DateOnly(2026, 5, 15)));

            q.AddTrigger<ExampleJob>(t => t
                .WithIdentity("Daily Trigger")
                .ForJob(jobKey)
                .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
                .WithCalendarName(calendarName));

            #endregion

            #region sample_di_calendar_factory

            // A calendar that needs a dependency cannot be built by the generic overloads, which
            // construct it with new T(). This one is handed the scheduler's service provider.
            q.AddCalendar("businessDays", serviceProvider =>
            {
                HolidayCalendar calendar = new() { TimeZone = TimeZoneInfo.Utc };
                foreach (DateOnly day in serviceProvider.GetRequiredService<IHolidayList>().Days)
                {
                    calendar.AddExcludedDay(day);
                }

                return calendar;
            });

            #endregion

            #region sample_di_plugins

            q.UseJsonSchedulingConfiguration(x =>
            {
                x.Files.Add("~/quartz_jobs.json");
                x.ScanInterval = TimeSpan.FromSeconds(2);
                x.FailOnSchedulingError = true;
            });

            // resolve Windows and IANA time zone ids on either operating system
            q.UseTimeZoneConverter();

            #endregion

            #region sample_di_job_timeout

            // interrupt a job that runs longer than it should; a job saying [JobTimeout("00:00:05")]
            // gets five seconds instead
            q.AddJobTimeout(TimeSpan.FromMinutes(5));

            q.ScheduleJob<SlowJob>(
                trigger => trigger
                    .WithIdentity("slowJobTrigger")
                    .StartNow()
                    .WithSimpleSchedule(TimeSpan.FromSeconds(5)),
                job => job.WithIdentity("slowJob"));

            #endregion

            #region sample_di_listeners

            q.AddSchedulerListener<SampleSchedulerListener>();
            q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals("awesome group"));
            q.AddTriggerListener<SampleTriggerListener>();

            #endregion
        });
    }

    public static void RegistrationThatDependsOnConfiguration(IServiceCollection services, IConfiguration configuration)
    {
        #region sample_di_registration_from_options

        services.Configure<SampleOptions>(configuration.GetSection("Sample"));

        services.AddQuartz(q =>
        {
            if (!string.IsNullOrWhiteSpace(configuration.GetSection("Sample")["CronSchedule"]))
            {
                JobKey customJobKey = new("options-custom-job", "custom");

                q.AddJob<ExampleJob>(j => j.WithIdentity(customJobKey));

                q.AddTrigger<ExampleJob>((serviceProvider, trigger) => trigger
                    .WithIdentity("options-custom-trigger", "custom")
                    .ForJob(customJobKey)
                    .WithCronSchedule(serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule));
            }
        });

        #endregion
    }
}
