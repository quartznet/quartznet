using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/standalone-scheduler.md.
/// </summary>
public static class StandaloneSchedulerSamples
{
    public static async ValueTask TheWholeThing()
    {
        #region sample_standalone_scheduler

        IScheduler scheduler = await QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(o => o.InstanceName = "reporting")
            .UseDefaultThreadPool(maxConcurrency: 20)
            .UseInMemoryStore()
            .BuildScheduler();

        await scheduler.Start();

        #endregion
    }

    public static async ValueTask OneExpression()
    {
        #region sample_standalone_one_expression

        await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
            .UseInMemoryStore()
            .UseDefaultThreadPool(10)
            .Build();

        #endregion
    }

    public static async ValueTask BuildSchedulerEnding()
    {
        #region sample_standalone_build_scheduler_ending

        // I want the scheduler
        IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseInMemoryStore().BuildScheduler();

        #endregion
    }

    public static async ValueTask BuildEnding()
    {
        #region sample_standalone_build_ending

        // I want to own the lifetime
        await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseInMemoryStore().Build();
        IScheduler scheduler = await factory.GetScheduler();

        #endregion
    }

    public static async ValueTask TheFactoryOwnsTheContainer()
    {
        #region sample_standalone_factory_owns_the_container

        await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
            .UseInMemoryStore()
            .Build();

        IScheduler scheduler = await factory.GetScheduler();
        await scheduler.Start();

        // ... do work ...

        // leaving the scope shuts the scheduler down, then disposes the container

        #endregion
    }

    public static async ValueTask WaitingForJobs(IScheduler scheduler)
    {
        #region sample_standalone_wait_for_jobs

        await scheduler.Shutdown(waitForJobsToComplete: true);

        #endregion
    }

    public static async ValueTask JobsTriggersAndCalendars()
    {
        #region sample_standalone_jobs_triggers_and_calendars

        await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
            .UseInMemoryStore()
            .AddJob<ReportJob>(j => j.WithIdentity("nightly", "reports").StoreDurably())
            .AddTrigger<ReportJob>(t => t
                .ForJob("nightly", "reports")
                .WithIdentity("nightly-trigger", "reports")
                .WithCronSchedule("0 30 2 * * ?"))
            .AddCalendar<HolidayCalendar>("holidays", configure: c => c.AddExcludedDay(new DateOnly(2026, 12, 25)))
            .Build();

        #endregion
    }

    public static void RegisteringYourOwnServices()
    {
        #region sample_standalone_registering_services

        QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
        builder.Services.AddSingleton<IReportRenderer, PdfReportRenderer>();
        builder.Services.AddHttpClient();
        builder.UseInMemoryStore().AddJob<ReportJob>(j => j.WithIdentity("nightly"));

        #endregion
    }

    public static async ValueTask ConfigurationFromAFile()
    {
        #region sample_standalone_configuration

        IConfiguration configuration = new ConfigurationBuilder()
            .AddJsonFile("appsettings.json")
            .AddEnvironmentVariables()
            .Build();

        await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
            .UseConfiguration(configuration.GetSection("Quartz"))
            .Build();

        #endregion
    }

    public static void FlatProperties()
    {
        #region sample_standalone_properties

        NameValueCollection properties = new()
        {
            ["quartz.scheduler.instanceName"] = "reporting",
            ["quartz.threadPool.maxConcurrency"] = "20",
        };

        QuartzSchedulerBuilder.Create().UseProperties(properties);

        #endregion
    }

    public static async ValueTask PersistentAndClustered(string connectionString)
    {
        #region sample_standalone_persistent_and_clustered

        await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
            .ConfigureScheduler(o =>
            {
                o.InstanceName = "orders";
                o.InstanceId = Environment.MachineName;
            })
            .UsePersistentStore(s =>
            {
                s.UseSqlServer(connectionString);
                s.UseClustering(c => c.CheckinInterval = TimeSpan.FromSeconds(10));
                s.ConfigureStore(o => o.TablePrefix = "QRTZ_");
            })
            .Build();

        #endregion
    }

    public static void SharingASchedulerRepository()
    {
        #region sample_standalone_shared_repository

        ISchedulerRepository shared = new SchedulerRepository();

        QuartzSchedulerBuilder first = QuartzSchedulerBuilder.Create();
        first.Services.AddSingleton(shared);

        QuartzSchedulerBuilder second = QuartzSchedulerBuilder.Create();
        second.Services.AddSingleton(shared);

        #endregion
    }
}
