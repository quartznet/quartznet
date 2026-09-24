---

title: Multiple Schedulers with Microsoft DI
---

`AddQuartz(string name, ...)` registers a named scheduler. Each gets its own configuration, jobs, triggers,
listeners and calendars, through the usual DI fluent API. `ISchedulerRepository` tracks built schedulers by name.

::: tip
Without Microsoft DI, build each scheduler from its own `QuartzSchedulerBuilder`, with
`Create(q => q.ConfigureScheduler(options => options.InstanceName = ...))`, and call `BuildScheduler()` on each.
:::

## When to Use Named Schedulers

- **Different job stores**: in-memory for transient jobs, a persistent store for durable jobs.
- **Workload isolation**: critical jobs and background maintenance on independent thread pools.
- **Different configurations**: misfire thresholds, batch sizes or clustering settings.

## Basic Configuration

Give each `AddQuartz(string name, ...)` call a unique name:

<!-- snippet: sample_multiple_two_schedulers -->
```csharp
var builder = Host.CreateApplicationBuilder(args);

// First scheduler: fast in-memory jobs
builder.Services.AddQuartz("FastScheduler", q =>
{
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 5);

    q.ScheduleJob<NotificationJob>(trigger => trigger
        .WithIdentity("notify-trigger")
        .WithSimpleSchedule(TimeSpan.FromSeconds(30)));
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
```
<!-- endSnippet -->

## Per-Scheduler Listeners and Calendars

Listeners and calendars registered inside a named `AddQuartz` call apply to that scheduler only:

<!-- snippet: sample_multiple_per_scheduler_listeners -->
```csharp
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
```
<!-- endSnippet -->

## Injecting a Named Scheduler

The scheduler's name is its service key, so inject it as a keyed service:

<!-- snippet: sample_multiple_keyed_service -->
```csharp
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
```
<!-- endSnippet -->

Or resolve it directly:

<!-- snippet: sample_multiple_resolving -->
```csharp
var fast = provider.GetRequiredKeyedService<IScheduler>("FastScheduler");
var standard = provider.GetRequiredService<IScheduler>();   // the default scheduler, if one is registered
```
<!-- endSnippet -->

Every part of a named scheduler is registered under its key, for example
`GetRequiredKeyedService<ISchedulerFactory>("FastScheduler")`. Unkeyed registrations belong to the default
scheduler.

::: warning
The injected `IScheduler` is a handle that builds the scheduler on first use, because building is asynchronous
and a container constructs synchronously.

- Asynchronous members await the build and are always safe.
- `Status`, `SchedulerInstanceId`, `Context` and `ListenerManager` throw `InvalidOperationException` if reading
  them would have to build the scheduler.
- `SchedulerName` never builds anything.

Under `AddQuartzHostedService()` the synchronous members are safe once the host has started: the hosted
service builds every scheduler while the host starts, before your code runs. It *starts* them later, once
`ApplicationStarted` fires, unless `AwaitApplicationStarted` is off.
:::

### Finding a scheduler at runtime

When the name is known only at runtime, for example a dashboard or a request naming its scheduler, use the
container's `ISchedulerRepository`. It holds every scheduler that has been built:

<!-- snippet: sample_multiple_scheduler_repository -->
```csharp
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
```
<!-- endSnippet -->

::: warning

- The repository holds only *built* schedulers, so during startup it may not hold them all. Injecting by key
  does not have this problem: the handle builds the scheduler it names.
- The repository is per container, not per process. A scheduler built by its own `QuartzSchedulerBuilder` is
  not in it; see [the migration guide](../migration-guide.md#no-process-global-scheduler-or-connection-state).

:::

For what the container has *registered*, built or not, resolve `ISchedulerRegistry` and call
`QuerySchedulers()`. It returns one `SchedulerRegistration` per registration, plus one per scheduler bound into
the repository without a registration. A scheduler not yet created has a `null` `Status` and is not created.
Use it for an inventory; use `LookupAll` for the live schedulers.

## Mixing Default and Named Schedulers

The unnamed `AddQuartz()` and named schedulers can be combined:

<!-- snippet: sample_multiple_default_and_named -->
```csharp
// Default scheduler (traditional single-scheduler usage)
builder.Services.AddQuartz(q =>
{
    q.ScheduleJob<MainJob>(trigger => trigger
        .WithIdentity("main-trigger")
        .WithSimpleSchedule(TimeSpan.FromMinutes(1)));
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
```
<!-- endSnippet -->

::: tip
Call order does not matter. The hosted service resolves schedulers when the host starts, so it starts every
registered scheduler whether `AddQuartz` was called before or after it. A container with no scheduler is
reported at startup.
:::

## Configuration via appsettings.json

Pass the root `Quartz` section; a named scheduler's settings are read from `Schedulers:{name}`:

<!-- snippet: sample_multiple_named_from_configuration -->
```csharp
builder.AddQuartz("DurableScheduler");
// or, naming the section yourself:
builder.Services.AddQuartz("DurableScheduler", builder.Configuration.GetSection("Quartz"));
```
<!-- endSnippet -->

`AddQuartzSchedulers` registers one named scheduler per child of `Schedulers`:

<!-- snippet: sample_multiple_all_from_configuration -->
```csharp
builder.AddQuartzSchedulers();
// or:
builder.Services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));
```
<!-- endSnippet -->

```json
{
  "Quartz": {
    "Schedulers": {
      "DurableScheduler": {
        "Scheduler": {
          "InstanceId": "AUTO"
        },
        "JobStore": {
          "Type": "Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz"
        }
      }
    }
  }
}
```

Flat keys with no typed option, such as a plugin's own settings, go in the named options' `Properties`
dictionary:

<!-- snippet: sample_multiple_named_options -->
```csharp
builder.Services.Configure<QuartzOptions>("DurableScheduler",
    options => options.Properties["quartz.plugin.myPlugin.someSetting"] = "value");
```
<!-- endSnippet -->

## Per-Scheduler Startup and Shutdown

`AddQuartzHostedService(configure)` configures every scheduler. A named call overrides it for one scheduler,
in either call order:

<!-- snippet: sample_multiple_hosted_services -->
```csharp
// shared by every scheduler
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// ...except this one, which waits longer before its first fire
builder.Services.AddQuartzHostedService("DurableScheduler", options =>
{
    options.StartDelay = TimeSpan.FromMinutes(2);
});
```
<!-- endSnippet -->

## Configuring every scheduler at once

`ConfigureAllQuartzSchedulers(configure)` applies a builder callback to every scheduler registered through
`AddQuartz`, `AddQuartz(name, …)` or `AddQuartzSchedulers`, before or after the call.

- Each scheduler gets its own instance of what the callback adds: a plugin added to three schedulers is three
  plugin instances.
- Remote schedulers from `AddQuartzHttpClient` have no builder and are skipped.

See [Multi-Tenancy](../multi-tenancy.md#giving-every-scheduler-the-same-thing).

## Limitations

- **Scheduler names must be unique**, compared ignoring case.

Job types are not a limitation. `AddJob<T>` registers the type unkeyed, so one job class can serve every
scheduler. `AddJobType<TJob, TImplementation>()`, `AddJobType<TJob>(lifetime)` and `AddJobType<TJob>(factory)`
register under one scheduler's key; the job factory checks that key before the container's unkeyed
registration. Two schedulers can therefore build the same job type differently. See
[Multi-Tenancy](../multi-tenancy.md#job-types).
