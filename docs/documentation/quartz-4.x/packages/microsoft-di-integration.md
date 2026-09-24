---

title: Microsoft DI Integration
---

[Microsoft's dependency injection container](https://learn.microsoft.com/dotnet/core/extensions/dependency-injection)
builds the scheduler. `AddQuartz` registers the object graph; the job store, thread pool, listeners, plugins and
your jobs are resolved from the container. Nothing is built reflectively from type names.

This is in the core [Quartz](https://www.nuget.org/packages/Quartz) package; 3.x had it in the separate
`Quartz.Extensions.DependencyInjection` package.

::: tip
[The hosted service](hosted-services-integration.md) starts and stops the scheduler with the application.

Need several independent schedulers in one application? See [Multiple Schedulers](multiple-schedulers.md).
:::

## Registering a scheduler

Install Quartz:

```shell
dotnet add package Quartz
```

`Host.CreateApplicationBuilder` and `WebApplication.CreateBuilder` come from `Microsoft.Extensions.Hosting`. The
`worker` and `web` templates reference it; a plain `console` project needs it added:

```shell
dotnet add package Microsoft.Extensions.Hosting
```

`ExampleJob`, used throughout this page, is an ordinary class:

<!-- snippet: sample_di_example_job -->
```csharp
public sealed class ExampleJob : IJob
{
    private readonly ILogger<ExampleJob> logger;

    public ExampleJob(ILogger<ExampleJob> logger)
    {
        this.logger = logger;
    }

    // Job data is applied onto properties of matching name and type before Execute runs,
    // so each one needs a setter. Nothing here reads the job data map.
    public string InjectedString { get; set; } = "";

    public bool InjectedBool { get; set; }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "ExampleJob fired: InjectedString={InjectedString} InjectedBool={InjectedBool}",
            InjectedString,
            InjectedBool);

        return default;
    }
}
```
<!-- endSnippet -->

<!-- snippet: sample_di_registering_a_scheduler -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.AddQuartz(q =>
{
    q.ScheduleJob<ExampleJob>(trigger => trigger
        .WithIdentity("example")
        // every ten seconds, so a first run reports itself quickly;
        // "0 0/5 * * * ?" is every five minutes
        .WithCronSchedule("0/10 * * * * ?"));
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

The file needs `using Microsoft.Extensions.Hosting;` and `using Quartz;`. The ten-second schedule shows quickly
whether a first registration works.

`AddQuartz` and `AddQuartzHostedService` are on `IHostApplicationBuilder`, so they also work with
`WebApplication.CreateBuilder(args)`. Both are on `IServiceCollection` too (`builder.Services.AddQuartz(q => …)`),
for code that has only the collection.

## Configuration from appsettings.json

Everything configurable in code binds from the `Quartz` configuration section, under the option's own name:

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

Registration through `IHostApplicationBuilder` binds the `Quartz` section automatically. On `IServiceCollection`,
pass the section:

<!-- snippet: sample_di_configuration_section -->
```csharp
services.AddQuartz(configuration.GetSection("Quartz"), q =>
{
    // code configuration on top of what the file says; code wins
    q.ConfigureScheduler(options => options.InstanceId = "Scheduler-Core");
});
```
<!-- endSnippet -->

Section names, their options and the schedule-in-configuration format are in
[Configuration Reference](../configuration/reference.md) and [JSON configuration](../configuration/json.md). The
3.x flat keys (`quartz.scheduler.instanceName` style) still work and map to the same options; do not use them in
a new application.

## How jobs are constructed

Jobs are resolved from the container.

- `AddJob<T>()`, `AddJob(type, …)` and `ScheduleJob<T>()` register the job type as a **scoped** service.
- The job factory opens a scope per fire, resolves the job from it, and disposes the scope when the job
  returns, so a job can take scoped dependencies such as a database context.
- A job type with no registration is built with `ActivatorUtilities`, so jobs from XML or JSON files work.
- A job should have only one public constructor.

The registration is a `TryAdd`, so your own registration wins:

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
A singleton job serves every fire from one instance: it must be thread-safe and cannot take scoped
dependencies. Prefer scoped, which is what `AddJob` registers.
:::

::: warning A registered job may not take a scheduler's parts by constructor
`IScheduler`, `ISchedulerFactory`, `IJobStore`, `IThreadPool` and `IOptions<QuartzSchedulerOptions>` belong to
one scheduler, but the container resolves them unkeyed, without knowing which scheduler fires the job. Startup
refuses such a constructor, naming the job and the parameter. Instead:

- use `IJobExecutionContext.Scheduler` for the scheduler running the fire;
- read the firing from `IJobExecutionContextAccessor` where the context is not passed in;
- register a job that must be *constructed* with a scheduler part with `AddJobType<T>(provider => …)`, which
  resolves the part by key and is not checked.

See [which scheduler's parts a job is built from](../multi-tenancy.md#which-scheduler-s-parts-a-job-is-built-from).
:::

To add to the factory's scope without replacing the factory (to seed an ambient tenant, say), use
`q.ConfigureJobScope((scope, bundle, scheduler) => …)`.

### Failing fast when job dependencies cannot be resolved

The job type is registered, so `ValidateOnBuild` (on by default in the Development environment) checks its
constructor. A job with an unregistered dependency fails when the container is built, naming the job and the
dependency:

<!-- snippet: sample_di_validate_on_build -->
```csharp
services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));

// throws: Unable to resolve service for type 'IReportStore' while attempting to activate 'SendReportsJob'
```
<!-- endSnippet -->

Before 4.0 the job type was not registered, so the failure came at fire time: the trigger fired, the job never
ran, and every trigger of the job moved to `TriggerState.Error` until `IScheduler.ResetTriggerFromErrorState`
was called.

Unregistered jobs (named by an XML or JSON schedule, or built by your own job factory) can still fail that way.
To react at fire time, for example to fail whatever scheduled the work, handle
`ISchedulerListener.SchedulerError`. Its `SchedulerErrorContext` names the trigger, the job and the fire
instance, and wraps a `JobInstantiationException` carrying the same three:

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

`ISchedulerListener.TriggersInError` is raised too: every trigger of that job is now in the error state.

To take part in construction (to record the failure or add context), derive from
`MicrosoftDependencyInjectionJobFactory` and override `CreateJobInstance`. Its `TriggerFiredBundle` carries the
trigger, the job detail and `bundle.Trigger.FireInstanceId`.

## Persistent job stores

On every start, what you register is compared with the database and the stored schedule is updated to match.

::: warning
With a persistent job store, always give jobs and triggers explicit names. Without one, each gets a new
generated name on every start, the existence check finds nothing, and the schedule accumulates duplicates. A
name is enough; the group defaults to the same value every time.
:::

<!-- snippet: sample_di_persistent_store -->
```csharp
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
```
<!-- endSnippet -->

Store settings go through `store.ConfigureStore(...)`, which configures `AdoJobStoreOptions`; the database and
clustering calls are on the builder. Duplicate scheduling data has its own setting:

<!-- snippet: sample_di_duplicate_scheduling_data -->
```csharp
services.Configure<QuartzOptions>(options =>
{
    // Pass over a declared job or trigger whose key is already stored, rather than replacing it.
    // OverwriteExistingData defaults to true, and this turns that default off; writing both down
    // is asking for opposite things and is refused at startup.
    options.Scheduling.IgnoreDuplicates = true; // default: false
});
```
<!-- endSnippet -->

## A worked configuration

One registration, in parts.

**Jobs and triggers.** `ScheduleJob<T>` registers a job with one trigger. `AddJob` plus `AddTrigger` registers a
job shared by several triggers, each with its own data.

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
```
<!-- endSnippet -->

Before each fire, `MicrosoftDependencyInjectionJobFactory` (derived from `PropertySettingJobFactory`) copies each
entry of the merged job data map onto the job property of the same name. `ExampleJob` sees `InjectedString` set
without reading `context.MergedJobDataMap`; `UsingJobData(x => x.InjectedString, "Hello")` writes that entry.

- A property needs a **setter**, and its type must match the value.
- An entry that matches no property is ignored by default;
  [`PropertyMismatchBehavior`](../tutorial/job-data-map.md#property-injection-the-other-read-side) makes it a
  warning or a failure.
- Reading the map directly still works, for values with no property.

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

The generic overloads construct the calendar with `new T()`. For a calendar with a dependency (a holiday list
from a database, a clock), pass a factory. It receives the scheduler-scoped service provider, so a named
scheduler's calendar gets that scheduler's parts:

<!-- snippet: sample_di_calendar_factory -->
```csharp
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
```
<!-- endSnippet -->

**Plugins**, including a schedule kept in a file and watched for changes:

<!-- snippet: sample_di_plugins -->
```csharp
q.UseJsonSchedulingConfiguration(x =>
{
    x.Files.Add("~/quartz_jobs.json");
    x.ScanInterval = TimeSpan.FromSeconds(2);
    x.FailOnSchedulingError = true;
});

// resolve Windows and IANA time zone ids on either operating system
q.UseTimeZoneConverter();
```
<!-- endSnippet -->

Every shipped plugin has such an extension; see [Plugins](quartz-plugins.md).

**A timeout**, a middleware in the core package:

<!-- snippet: sample_di_job_timeout -->
```csharp
// interrupt a job that runs longer than it should; a job saying [JobTimeout("00:00:05")]
// gets five seconds instead
q.AddJobTimeout(TimeSpan.FromMinutes(5));

q.ScheduleJob<SlowJob>(
    trigger => trigger
        .WithIdentity("slowJobTrigger")
        .StartNow()
        .WithSimpleSchedule(TimeSpan.FromSeconds(5)),
    job => job.WithIdentity("slowJob"));
```
<!-- endSnippet -->

[Job Execution Middleware](../tutorial/job-execution-middleware.md#timing-a-job-out) covers what a timeout does to
the trigger and how a timed-out firing becomes a retryable failure.

**Listeners**, built by the container and in place before the scheduler starts:

<!-- snippet: sample_di_listeners -->
```csharp
q.AddSchedulerListener<SampleSchedulerListener>();
q.AddJobListener<SampleJobListener>(GroupMatcher<JobKey>.GroupEquals("awesome group"));
q.AddTriggerListener<SampleTriggerListener>();
```
<!-- endSnippet -->

**Registration that depends on your own configuration.** Decide whether to schedule in ordinary code; read
values the trigger needs from the container when the trigger is built:

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

**Configuration that depends on a service.** The builder callback runs before the container exists. These
members have an overload that receives the service provider when the scheduler is built: `AddJob`, `AddTrigger`,
`ScheduleJob`, `AddCalendar`, `UseJobStore`, `AddPlugin`, the three `Add*Listener` methods, `AddJobMiddleware`,
`UseExecutionLimits` and `AddJobTimeout`.

<!-- snippet: sample_di_configuration_from_services -->
```csharp
services.AddOptions<SampleOptions>()
    .Bind(configuration.GetSection("Sample"))
    .Validate(options => options.MaxConcurrent > 0, "Sample:MaxConcurrent must be positive")
    .ValidateOnStart();

services.AddQuartz(q =>
{
    // Read when the scheduler is built, so the options have been bound, post-configured and
    // validated by the time the number is asked for.
    q.UseExecutionLimits((serviceProvider, limits) => limits.ForGroup(
        "reports",
        serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.MaxConcurrent));

    q.AddJobTimeout(serviceProvider =>
        serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.JobTimeout);
});
```
<!-- endSnippet -->

Reading the section yourself (`configuration.GetSection("Sample").Get<SampleOptions>()`) would skip every
`Configure`, `PostConfigure` and validation registered for those options.

For any other setting, use the options pattern with the service it depends on. A scheduler's options are a named
instance whose name is the scheduler's name:

<!-- snippet: sample_di_quartz_option_from_service -->
```csharp
services.AddQuartz("reporting", q => q.UsePersistentStore(store => store.UseSqlServer("...")));

// The scheduler's name is its options name, so a named scheduler is configured under it.
services.AddOptions<AdoJobStoreOptions>("reporting")
    .Configure<ITablePrefixSource>((options, source) => options.TablePrefix = source.TablePrefix);
```
<!-- endSnippet -->
