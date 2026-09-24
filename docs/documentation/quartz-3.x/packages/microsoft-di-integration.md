---

title: Microsoft DI Integration
---

[Quartz.Extensions.DependencyInjection](https://www.nuget.org/packages/Quartz.Extensions.DependencyInjection)
integrates Quartz with [Microsoft Dependency Injection](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection).

::: tip
Quartz 3.1 or later required.
:::

## Installation

```shell
Install-Package Quartz.Extensions.DependencyInjection
```

## Using

Call the `AddQuartz` extension method on `IServiceCollection`. It wraps the [configuration properties](../configuration/reference) in a strongly-typed API. You can also set properties in the `Quartz` section of `appsettings.json`.

::: tip
Bind the section to `QuartzOptions` yourself with `AddOptions` or `Configure`, as in [this example](https://github.com/quartznet/quartznet/blob/a4511ef0703206cf483c6331d5b2ac7fb69d26d3/src/Quartz.Examples.AspNetCore/Startup.cs#L71).
:::

::: tip
[Quartz.Extensions.Hosting](hosted-services-integration.md) adds a background service that starts and stops the scheduler.

For several independent schedulers in one application, see [Multiple Schedulers](multiple-schedulers.md).
:::

**Example appsettings.json**

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft": "Warning",
      "Microsoft.Hosting.Lifetime": "Information"
    }
  },
  "Quartz": {
    "quartz.scheduler.instanceName": "Quartz ASP.NET Core Sample Scheduler"
  }
}
````

## DI aware job factories

Quartz has two built-in job factories, set with `UseMicrosoftDependencyInjectionJobFactory` or `UseMicrosoftDependencyInjectionScopedJobFactory` (deprecated).

::: tip
Since Quartz.NET 3.3.2 the default job factory produces only scoped jobs; do not use `UseMicrosoftDependencyInjectionJobFactory` or `UseMicrosoftDependencyInjectionScopedJobFactory` any more.
:::

### Job instance construction

Quartz resolves the job's type from the container. Without an explicit registration, it constructs the job with `ActivatorUtilities`, injecting dependencies through the constructor. A job should have only one public constructor.

### Failing fast when job dependencies cannot be resolved

`AddJob<T>()` does **not** register the job type with the container; it only describes the job to the
scheduler. So `ValidateOnBuild`, which the host enables by default in the Development environment,
never checks that the job's constructor can be satisfied.

An unresolvable dependency then fails at fire time, not at startup, as a failure to instantiate the
job. The job never runs, and every trigger of that job moves to `TriggerState.Error` until
`IScheduler.ResetTriggerFromErrorState` is called.

Register your job types explicitly so startup validation covers them:

```csharp
services.AddScoped<SendReportsJob>();   // now ValidateOnBuild checks its constructor

services.AddQuartz(q =>
{
    q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports"));
});
```

To react to such a failure at fire time instead, for example to fail whatever scheduled the work,
handle `ISchedulerListener.SchedulerError`. It receives a `JobInstantiationException` naming the
trigger, the job and the fire instance:

```csharp
public class InstantiationFailureListener : SchedulerListenerSupport
{
    public override Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
    {
        if (cause is JobInstantiationException failure)
        {
            logger.LogError(failure, "Job {Job} could not be built for trigger {Trigger}, fire {FireInstanceId}",
                failure.JobDetail.Key, failure.Trigger.Key, failure.FireInstanceId);
        }

        return Task.CompletedTask;
    }
}
```

To take part in construction itself, for example to record the failure or add context to it, derive
from `MicrosoftDependencyInjectionJobFactory` and override `InstantiateJob`. Its `TriggerFiredBundle`
carries the trigger, the job detail and `bundle.Trigger.FireInstanceId`.

### Persistent job stores

Every time your application starts and evaluates the schedule, the scheduling configuration is checked against the database and updated.

::: warning
With a persistent job store, always name your jobs and triggers, so that existence checks match the data already in your database.

Jobs and triggers configured without an explicit identity get a different generated name each time the configuration is evaluated. An omitted group gets the same default group every time.
:::

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // base configuration from appsettings.json
    services.Configure<QuartzOptions>(Configuration.GetSection("Quartz"));

    // if you are using persistent job store, you might want to alter some options
    services.Configure<QuartzOptions>(options =>
    {
        options.Scheduling.IgnoreDuplicates = true; // default: false
        options.Scheduling.OverWriteExistingData = true; // default: true
    });

    services.AddQuartz(q =>
    {
        // handy when part of cluster or you want to otherwise identify multiple schedulers
        q.SchedulerId = "Scheduler-Core";

        // we take this from appsettings.json, just show it's possible
        // q.SchedulerName = "Quartz ASP.NET Core Sample Scheduler";

        // these are the defaults
        q.UseSimpleTypeLoader();
        q.UseInMemoryStore();
        q.UseDefaultThreadPool(tp =>
        {
            tp.MaxConcurrency = 10;
        });

        // quickest way to create a job with single trigger is to use ScheduleJob
        // (requires version 3.2)
        q.ScheduleJob<ExampleJob>(trigger => trigger
            .WithIdentity("Combined Configuration Trigger")
            .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(7)))
            .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
            .WithDescription("my awesome trigger configured for a job with single call")
        );

        // you can also configure individual jobs and triggers with code
        // this allows you to associated multiple triggers with same job
        // (if you want to have different job data map per trigger for example)
        q.AddJob<ExampleJob>(j => j
            .StoreDurably() // we need to store durably if no trigger is associated
            .WithDescription("my awesome job")
        );

        // here's a known job for triggers
        var jobKey = new JobKey("awesome job", "awesome group");
        q.AddJob<ExampleJob>(jobKey, j => j
            .WithDescription("my awesome job")
        );

        q.AddTrigger(t => t
            .WithIdentity("Simple Trigger")
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
            .WithDescription("my awesome simple trigger")
        );

        q.AddTrigger(t => t
            .WithIdentity("Cron Trigger")
            .ForJob(jobKey)
            .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(3)))
            .WithCronSchedule("0/3 * * * * ?")
            .WithDescription("my awesome cron trigger")
        );

        // use H (hash) to spread trigger fire times based on trigger identity
        q.AddTrigger(t => t
            .WithIdentity("Spread Cron Trigger")
            .ForJob(jobKey)
            .WithCronSchedule("H * * * * ?")
            .WithDescription("fires once per minute at a hash-derived second")
        );

        // you can add calendars too (requires version 3.2)
        const string calendarName = "myHolidayCalendar";
        q.AddCalendar<HolidayCalendar>(
            name: calendarName,
            replace: true,
            updateTriggers: true,
            x => x.AddExcludedDate(new DateTime(2020, 5, 15))
        );

        q.AddTrigger(t => t
            .WithIdentity("Daily Trigger")
            .ForJob(jobKey)
            .StartAt(DateBuilder.EvenSecondDate(DateTimeOffset.UtcNow.AddSeconds(5)))
            .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
            .WithDescription("my awesome daily time interval trigger")
            .ModifiedByCalendar(calendarName)
        );

        // also add XML configuration and poll it for changes
        q.UseXmlSchedulingConfiguration(x =>
        {
            x.Files = new[] { "~/quartz_jobs.config" };
            x.ScanInterval = TimeSpan.FromSeconds(2);
            x.FailOnFileNotFound = true;
            x.FailOnSchedulingError = true;
        });

        // convert time zones using converter that can handle Windows/Linux differences
        q.UseTimeZoneConverter();

        // auto-interrupt long-running job
        q.UseJobAutoInterrupt(options =>
        {
            // this is the default
            options.DefaultMaxRunTime = TimeSpan.FromMinutes(5);
        });
        q.ScheduleJob<SlowJob>(
            triggerConfigurator => triggerConfigurator
                .WithIdentity("slowJobTrigger")
                .StartNow()
                .WithSimpleSchedule(x => x.WithIntervalInSeconds(5).RepeatForever()),
            jobConfigurator => jobConfigurator
                .WithIdentity("slowJob")
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
                // allow only five seconds for this job, overriding default configuration
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, TimeSpan.FromSeconds(5).TotalMilliseconds.ToString(CultureInfo.InvariantCulture)));

        // add some listeners
        q.AddSchedulerListener<SampleSchedulerListener>();
        q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals(jobKey.Group));
        q.AddTriggerListener<SampleTriggerListener>();

        // example of persistent job store using JSON serializer as an example
        /*
        q.UsePersistentStore(s =>
        {
            s.PerformSchemaValidation = true; // default
            s.UseProperties = true; // preferred, but not default
            s.RetryInterval = TimeSpan.FromSeconds(15);
            s.UseSqlServer(sqlServer =>
            {
                sqlServer.ConnectionString = "some connection string";
                // this is the default
                sqlServer.TablePrefix = "QRTZ_";
            });
            s.UseJsonSerializer();
            s.UseClustering(c =>
            {
                c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                c.CheckinInterval = TimeSpan.FromSeconds(10);
            });
        });
        */
    });

 // we can use options pattern to support hooking your own configuration
 // because we don't use service registration api,
 // we need to manually ensure the job is present in DI
 services.AddTransient<ExampleJob>();

 services.Configure<SampleOptions>(Configuration.GetSection("Sample"));
 services.AddOptions<QuartzOptions>()
  .Configure<IOptions<SampleOptions>>((options, dep) =>
  {
   if (!string.IsNullOrWhiteSpace(dep.Value.CronSchedule))
   {
    var jobKey = new JobKey("options-custom-job", "custom");
    options.AddJob<ExampleJob>(j => j.WithIdentity(jobKey));
    options.AddTrigger(trigger => trigger
     .WithIdentity("options-custom-trigger", "custom")
     .ForJob(jobKey)
     .WithCronSchedule(dep.Value.CronSchedule));
   }
  });

    // Quartz.Extensions.Hosting allows you to fire background service that handles scheduler lifecycle
    services.AddQuartzHostedService(options =>
    {
        // when shutting down we want jobs to complete gracefully
        options.WaitForJobsToComplete = true;
    });
}
```
