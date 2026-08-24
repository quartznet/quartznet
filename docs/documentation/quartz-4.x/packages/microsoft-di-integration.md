---

title: Microsoft DI Integration
---

The scheduler is built by [Microsoft's dependency injection container](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection).
There is no reflective assembly of a scheduler from type names any more: `AddQuartz` registers the object graph,
and everything in it — the job store, the thread pool, listeners, plugins, your jobs — is resolved from the
container like any other service.

This is part of the core [Quartz](https://www.nuget.org/packages/Quartz) package; 3.x had it in the separate
`Quartz.Extensions.DependencyInjection` package.

::: tip
[The hosted service](hosted-services-integration.md) starts and stops the scheduler with the application.

Need several independent schedulers in one application? See [Multiple Schedulers](multiple-schedulers.md).
:::

## Registering a scheduler

<!-- snippet: sample_di_registering_a_scheduler -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddQuartz(q =>
{
    q.ScheduleJob<ExampleJob>(trigger => trigger
        .WithIdentity("example")
        .WithCronSchedule("0 0/5 * * * ?"));
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

`AddQuartz` and `AddQuartzHostedService` hang off `IHostApplicationBuilder`, so the same two calls work in a
web application built with `WebApplication.CreateBuilder(args)`. Both are also available on
`IServiceCollection` — `builder.Services.AddQuartz(q => …)` — for registration code that only has the
collection to work with.

## Configuration from appsettings.json

Everything configurable in code is bindable from the `Quartz` configuration section, under the option's own
name:

```json
{
  "Quartz": {
    "Scheduler": {
      "InstanceName": "Sample Scheduler",
      "MaxBatchSize": 5
    },
    "ThreadPool": {
      "MaxConcurrency": 20
    }
  }
}
```

The `Quartz` section is bound automatically when the scheduler is registered through `IHostApplicationBuilder`.
On `IServiceCollection`, where there is no configuration to hand, pass the section:

<!-- snippet: sample_di_configuration_section -->
```csharp
services.AddQuartz(configuration.GetSection("Quartz"), q =>
{
    // code configuration on top of what the file says; code wins
    q.ConfigureScheduler(options => options.InstanceId = "Scheduler-Core");
});
```
<!-- endSnippet -->

The section names, the options under each and the whole schedule-in-configuration format are in
[Configuration Reference](../configuration/reference.md) and
[JSON configuration](../configuration/json.md). The flat `quartz.scheduler.instanceName` style keys 3.x used
still work, and are translated to the same options, but they are not the spelling to reach for in a new
application.

## How jobs are constructed

A job is resolved from the container. `AddJob<T>()`, `AddJob(type, …)` and `ScheduleJob<T>()` register
the job type for you, as a **scoped** service — the job factory opens a dependency injection scope per
fire, resolves the job from it, and disposes the scope when the job returns, so a job can take scoped
dependencies such as a database context. A job type the container has no registration for at all is
still built with `ActivatorUtilities`, which is what makes a job scheduled from an XML or JSON file
work. A job should have only one public constructor.

The registration is a `TryAdd`, so your own registration always wins:

<!-- snippet: sample_di_registration_wins -->
```csharp
// your lifetime, your factory, your implementation type - kept
services.AddSingleton<SendReportsJob>(_ => SendReportsJob.ForTenant("acme"));

services.AddQuartz(q =>
{
    q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports"));
});
```
<!-- endSnippet -->

::: warning
A singleton job serves every fire from one instance, so it must be thread-safe and it cannot take
scoped dependencies. Prefer scoped, which is what `AddJob` registers.
:::

To add to the scope the factory opens rather than to replace the factory — to seed an ambient tenant, say —
use `q.ConfigureJobScope((scope, bundle, scheduler) => …)`.

### Failing fast when job dependencies cannot be resolved

Because the job type is registered, `ValidateOnBuild` — which the host enables by default in the
Development environment — sees it and checks that its constructor can be satisfied. A job asking for
something nobody registered therefore fails when the container is built, naming the job and the
dependency:

<!-- snippet: sample_di_validate_on_build -->
```csharp
services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));

// throws: Unable to resolve service for type 'IReportStore' while attempting to activate 'SendReportsJob'
```
<!-- endSnippet -->

Before 4.0 the job type was not registered, so validation never saw it and the failure arrived at fire
time instead: the trigger had already fired, the job never ran, and every trigger of that job was
moved to `TriggerState.Error`, where it stayed until `IScheduler.ResetTriggerFromErrorState` was
called.

Jobs that are not registered — those named by an XML or JSON schedule, or built by a job factory of
your own — can still fail that way. If you need to react to such a failure at fire time rather than
prevent it — to fail whatever scheduled the work, for instance — `ISchedulerListener.SchedulerError`
receives a `SchedulerErrorContext` naming the trigger, the job and the fire instance, wrapped around a
`JobInstantiationException` that carries the same three:

<!-- snippet: sample_di_instantiation_failure_listener -->
```csharp
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
```
<!-- endSnippet -->

`ISchedulerListener.TriggersInError` is raised alongside it, and reports the same thing from the job
store's side: every trigger of that job is now in the error state.

To take part in construction itself — to record the failure, or to add context to it — derive from
`MicrosoftDependencyInjectionJobFactory` and override `CreateJobInstance`. The `TriggerFiredBundle` it
receives carries the trigger, the job detail and `bundle.Trigger.FireInstanceId`.

## Persistent job stores

What you register is evaluated against the database every time the application starts, and the stored
schedule is updated to match.

::: warning
With a persistent job store, always give your jobs and triggers explicit names. Configuring them without an
identity gives each one a freshly generated name on every start, so the existence check finds nothing and the
schedule accumulates duplicates. Naming only the job and the trigger is enough — the group then defaults to
the same value every time.
:::

<!-- snippet: sample_di_persistent_store -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UseSqlServer(connectionString);
        store.UseSystemTextJsonSerializer();

        store.Configure(options =>
        {
            options.TablePrefix = "QRTZ_";        // the default
            options.StoreJobDataAsStrings = true; // preferred, but not the default
            options.PerformSchemaValidation = true;
        });

        store.UseClustering(cluster =>
        {
            cluster.CheckinInterval = TimeSpan.FromSeconds(10);
            cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
        });
    });
});
```
<!-- endSnippet -->

Settings of the store itself go through `store.Configure(...)`, which configures `AdoJobStoreOptions`; the
database call and the clustering call are the ones that hang off the builder. How duplicate scheduling data is
treated is a setting of its own:

<!-- snippet: sample_di_duplicate_scheduling_data -->
```csharp
services.Configure<QuartzOptions>(options =>
{
    options.Scheduling.OverwriteExistingData = true; // default: true
    options.Scheduling.IgnoreDuplicates = false;     // default: false
});
```
<!-- endSnippet -->

## A worked configuration

The rest of this page is one registration, broken into the things you might want from it.

**Jobs and triggers.** `ScheduleJob<T>` is a job and its one trigger; `AddJob` plus `AddTrigger` is a job that
several triggers share, each able to carry its own data.

<!-- snippet: sample_di_jobs_and_triggers -->
```csharp
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
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever()));

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
```
<!-- endSnippet -->

**Calendars**, to exclude days from a schedule:

<!-- snippet: sample_di_calendars -->
```csharp
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
```
<!-- endSnippet -->

**Plugins**, including a schedule kept in a file and watched for changes:

<!-- snippet: sample_di_plugins -->
```csharp
q.UseXmlSchedulingConfiguration(x =>
{
    x.Files.Add("~/quartz_jobs.config");
    x.ScanInterval = TimeSpan.FromSeconds(2);
    x.FailOnFileNotFound = true;
    x.FailOnSchedulingError = true;
});

// resolve Windows and IANA time zone ids on either operating system
q.UseTimeZoneConverter();

// interrupt a job that runs longer than it should
q.UseJobAutoInterrupt(options => options.DefaultMaxRunTime = TimeSpan.FromMinutes(5));

q.ScheduleJob<SlowJob>(
    trigger => trigger
        .WithIdentity("slowJobTrigger")
        .StartNow()
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(5)).RepeatForever()),
    job => job
        .WithIdentity("slowJob")
        .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)
        // allow only five seconds for this job, overriding the plugin's default.
        // the value is milliseconds, and either a number or a string holding one works
        .UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "5000"));
```
<!-- endSnippet -->

Every plugin Quartz ships has an extension like these; they are listed in
[Plugins](quartz-plugins.md).

**Listeners**, constructed from the container and in place before the scheduler starts:

<!-- snippet: sample_di_listeners -->
```csharp
q.AddSchedulerListener<SampleSchedulerListener>();
q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals("awesome group"));
q.AddTriggerListener<SampleTriggerListener>();
```
<!-- endSnippet -->

**Registration that depends on your own configuration.** Whether something is scheduled at all is decided
here, in ordinary code; a value needed to build the trigger is read from the container when the trigger is
built:

<!-- snippet: sample_di_registration_from_options -->
```csharp
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
```
<!-- endSnippet -->
