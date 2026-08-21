---

title: Microsoft DI Integration
---

[Quartz](https://www.nuget.org/packages/Quartz)
provides integration with [Microsoft Dependency Injection](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/dependency-injection).

::: tip
Quartz 3.1 or later required.
:::

## Using

You can add Quartz configuration by invoking an extension method `AddQuartz` on `IServiceCollection`.
The configuration building wraps various [configuration properties](../configuration/reference) with strongly-typed API.
You can also configure properties using standard .NET Core `appsettings.json` inside configuration section `Quartz`.

::: tip
[Quartz](hosted-services-integration.md) allows you to have a background service for your application that handles starting and stopping the scheduler.

Need multiple independent schedulers in one application? See [Multiple Schedulers](multiple-schedulers.md).
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

Quartz uses Microsoft's DI construction by default and the jobs produced by the default job factory are scoped jobs.

### Job instance construction

A job is resolved from the container. `AddJob<T>()`, `AddJob(type, …)` and `ScheduleJob<T>()` register
the job type for you, as a **scoped** service — the job factory opens a dependency injection scope per
fire, resolves the job from it, and disposes the scope when the job returns, so a job can take scoped
dependencies such as a database context. A job type the container has no registration for at all is
still built with `ActivatorUtilities`, which is what makes a job scheduled from an XML or JSON file
work. A job should have only one public constructor.

The registration is a `TryAdd`, so your own registration always wins:

```csharp
// your lifetime, your factory, your implementation type - kept
services.AddSingleton<SendReportsJob>(_ => SendReportsJob.ForTenant("acme"));

services.AddQuartz(q =>
{
    q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports"));
});
```

::: warning
A singleton job serves every fire from one instance, so it must be thread-safe and it cannot take
scoped dependencies. Prefer scoped, which is what `AddJob` registers.
:::

### Failing fast when job dependencies cannot be resolved

Because the job type is registered, `ValidateOnBuild` — which the host enables by default in the
Development environment — sees it and checks that its constructor can be satisfied. A job asking for
something nobody registered therefore fails when the container is built, naming the job and the
dependency:

```csharp
services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));

// throws: Unable to resolve service for type 'IReportStore' while attempting to activate 'SendReportsJob'
```

Before 4.0 the job type was not registered, so validation never saw it and the failure arrived at fire
time instead: the trigger had already fired, the job never ran, and every trigger of that job was
moved to `TriggerState.Error`, where it stayed until `IScheduler.ResetTriggerFromErrorState` was
called.

Jobs that are not registered — those named by an XML or JSON schedule, or built by a job factory of
your own — can still fail that way. If you need to react to such a failure at fire time rather than
prevent it — to fail whatever scheduled the work, for instance — `ISchedulerListener.SchedulerError`
receives a `JobInstantiationException` naming the trigger, the job and the fire instance:

```csharp
public sealed class InstantiationFailureListener : ISchedulerListener
{
    public ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default)
    {
        if (exception is JobInstantiationException failure)
        {
            logger.LogError(failure, "Job {Job} could not be built for trigger {Trigger}, fire {FireInstanceId}",
                failure.JobDetail.Key, failure.Trigger.Key, failure.FireInstanceId);
        }

        return default;
    }
}
```

`ISchedulerListener.TriggersInError` is raised alongside it, and reports the same thing from the job
store's side: every trigger of that job is now in the error state.

To take part in construction itself — to record the failure, or to add context to it — derive from
`MicrosoftDependencyInjectionJobFactory` and override `CreateJobInstance`. The `TriggerFiredBundle` it
receives carries the trigger, the job detail and `bundle.Trigger.FireInstanceId`.

### Persistent job stores

The scheduling configuration will be checked against database and updated accordingly every time your application starts and schedule is being evaluated.

::: warning
When using persistent job store, make sure you define job and trigger names for your scheduling so that existence checks work correctly against
the data you already have in your database.

Using API to configure triggers and jobs without explicit job identity configuration will cause jobs and triggers to have different generated name each time configuration is being evaluated.

With persistent job stores it's best practice to always declare at least job and trigger name. Omitting the group for them will produce same default group value for every invocation.
:::

**Example Startup.ConfigureServices configuration**

```csharp
public void ConfigureServices(IServiceCollection services)
{
    // if you are using persistent job store, you might want to alter some options
    services.Configure<QuartzOptions>(options =>
    {
        options.Scheduling.IgnoreDuplicates = true; // default: false
        options.Scheduling.OverwriteExistingData = true; // default: true
    });

    // base configuration from appsettings.json, plus configuration in code
    services.AddQuartz(Configuration.GetSection("Quartz"), q =>
    {
        // handy when part of cluster or you want to otherwise identify multiple schedulers
        q.ConfigureScheduler(options => options.InstanceId = "Scheduler-Core");

        // we take this from appsettings.json, just show it's possible
        // q.ConfigureScheduler(options => options.InstanceName = "Quartz ASP.NET Core Sample Scheduler");

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
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(7))
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
        q.AddJob<ExampleJob>(j => j
            .WithIdentity(jobKey)
            .WithDescription("my awesome job")
            // job data can name the job property it is meant for instead of spelling its key,
            // which makes a mistyped key or a wrong-typed value a compile error
            .UsingJobData(j2 => j2.InjectedString, "Hello")
            .UsingJobData(j2 => j2.InjectedBool, true)
        );

        q.AddTrigger<IJob>(t => t
            .WithIdentity("Simple Trigger")
            .ForJob(jobKey)
            .StartNow()
            .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever())
            .WithDescription("my awesome simple trigger")
        );

        q.AddTrigger<IJob>(t => t
            .WithIdentity("Cron Trigger")
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(3))
            .WithCronSchedule("0/3 * * * * ?")
            .WithDescription("my awesome cron trigger")
        );

        // use H (hash) to spread trigger fire times based on trigger identity
        q.AddTrigger<IJob>(t => t
            .WithIdentity("Spread Cron Trigger")
            .ForJob(jobKey)
            .WithCronSchedule("H * * * * ?")
            .WithDescription("fires once per minute at a hash-derived second")
        );

        // you can add calendars too (requires version 3.2)
        const string calendarName = "myHolidayCalendar";
        q.AddCalendar<HolidayCalendar>(
            name: calendarName,
            options: new AddCalendarOptions { Replace = true, UpdateTriggers = true },
            configure: x => x.AddExcludedDay(new DateOnly(2020, 5, 15))
        );

        q.AddTrigger<IJob>(t => t
            .WithIdentity("Daily Trigger")
            .ForJob(jobKey)
            .StartAt(DateTimeOffset.UtcNow.AddSeconds(5))
            .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))
            .WithDescription("my awesome daily time interval trigger")
            .WithCalendarName(calendarName)
        );

        // also add XML configuration and poll it for changes
        q.UseXmlSchedulingConfiguration(x =>
        {
            x.Files.Add("~/quartz_jobs.config");
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
                .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever()),
            jobConfigurator => jobConfigurator
                .WithIdentity("slowJob")
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
                // allow only five seconds for this job, overriding default configuration
                .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, TimeSpan.FromSeconds(5).TotalMilliseconds.ToString(CultureInfo.InvariantCulture)));

        // add some listeners
        q.AddSchedulerListener<SampleSchedulerListener>();
        q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals(jobKey.Group));
        q.AddTriggerListener<SampleTriggerListener>();

        // your own configuration can decide what gets scheduled: whether there is a schedule at all is
        // decided here, and the schedule itself is read from the container when the trigger is built
        if (!string.IsNullOrWhiteSpace(Configuration.GetSection("Sample")["CronSchedule"]))
        {
            var customJobKey = new JobKey("options-custom-job", "custom");
            q.AddJob<ExampleJob>(j => j.WithIdentity(customJobKey));
            q.AddTrigger<IJob>((serviceProvider, trigger) => trigger
                .WithIdentity("options-custom-trigger", "custom")
                .ForJob(customJobKey)
                .WithCronSchedule(serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule));
        }

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
            s.UseSystemTextJsonSerializer();
            s.UseClustering(c =>
            {
                c.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
                c.CheckinInterval = TimeSpan.FromSeconds(10);
            });
        });
        */
    });

    // your own options, read by the trigger registered above
    services.Configure<SampleOptions>(Configuration.GetSection("Sample"));

    // Quartz allows you to fire background service that handles scheduler lifecycle
    services.AddQuartzHostedService(options =>
    {
        // when shutting down we want jobs to complete gracefully
        options.WaitForJobsToComplete = true;
    });
}
```
