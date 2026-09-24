---

title: Multiple Schedulers with Microsoft DI
---

The named `AddQuartz(string name, ...)` overload registers several schedulers in one Microsoft DI container. Each named scheduler has its own configuration, jobs, triggers, listeners and calendars.

::: tip
Without Microsoft DI, create one `StdSchedulerFactory` per scheduler, each with a different `quartz.scheduler.instanceName`, and call `GetScheduler()` on each. The `SchedulerRepository` tracks them by name.
:::

## When to Use Named Schedulers

- **Different job stores:** in-memory storage for transient jobs, a persistent database store for durable jobs.
- **Workload isolation:** separate thread pools for critical jobs and background maintenance.
- **Different configurations:** different misfire thresholds, batch sizes or clustering settings.

## Basic Configuration

Register each scheduler under a unique name with `AddQuartz(string name, ...)`:

```csharp
var builder = Host.CreateApplicationBuilder(args);

// First scheduler: fast in-memory jobs
builder.Services.AddQuartz("FastScheduler", q =>
{
    q.UseInMemoryStore();
    q.UseDefaultThreadPool(tp => tp.MaxConcurrency = 5);

    q.ScheduleJob<NotificationJob>(trigger => trigger
        .WithIdentity("notify-trigger")
        .WithSimpleSchedule(x => x.WithIntervalInSeconds(30).RepeatForever()));
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

## Per-Scheduler Listeners and Calendars

Listeners and calendars registered in a named `AddQuartz` call apply to that scheduler only:

```csharp
builder.Services.AddQuartz("Scheduler1", q =>
{
    q.AddSchedulerListener<AuditSchedulerListener>();
    q.AddJobListener<LoggingJobListener>();
    q.AddTriggerListener<MetricsTriggerListener>();

    q.AddCalendar<HolidayCalendar>("holidays", replace: true, updateTriggers: true,
        cal => cal.AddExcludedDate(new DateTime(2025, 12, 25)));
    // These listeners and calendars only apply to Scheduler1
});

builder.Services.AddQuartz("Scheduler2", q =>
{
    // Scheduler2 has no listeners or calendars unless explicitly added here
});
```

## Accessing Named Schedulers Programmatically

Schedulers created through DI are in the container's `ISchedulerRepository`. Look them up by name:

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
        // Get a specific named scheduler
        var scheduler = schedulerRepository.Lookup("FastScheduler");
        if (scheduler != null)
        {
            await scheduler.TriggerJob(new JobKey("my-job"));
        }

        // Or get all schedulers
        var all = schedulerRepository.LookupAll();
    }
}
```

If you also have a default scheduler (unnamed `AddQuartz()`), you can inject `ISchedulerFactory` and call `GetScheduler(name)`:

```csharp
public class MyService
{
    private readonly ISchedulerFactory schedulerFactory;

    public MyService(ISchedulerFactory schedulerFactory)
    {
        this.schedulerFactory = schedulerFactory;
    }

    public async Task DoWork()
    {
        var scheduler = await schedulerFactory.GetScheduler("FastScheduler");
    }
}
```

::: warning
Named schedulers are available only after the hosted service has created and started them; during application startup they may not be in the repository yet.

`ISchedulerFactory` is in the container only after a default (unnamed) `AddQuartz()` call. With only named schedulers, inject `ISchedulerRepository`.

There are **two** repositories. The DI integration registers its own `ISchedulerRepository` singleton,
while a bare `StdSchedulerFactory` or `DirectSchedulerFactory` binds into the process-wide static
`SchedulerRepository.Instance`. A scheduler created outside the container is therefore missing from the
injected repository and from the Dashboard, which reads it. To join them, subclass
`StdSchedulerFactory` and override `GetSchedulerRepository()` to return the container's instance.
:::

## Mixing Default and Named Schedulers

The unnamed `AddQuartz()` and named schedulers can be combined:

```csharp
// Default scheduler (traditional single-scheduler usage)
builder.Services.AddQuartz(q =>
{
    q.ScheduleJob<MainJob>(trigger => trigger
        .WithIdentity("main-trigger")
        .WithSimpleSchedule(x => x.WithIntervalInMinutes(1).RepeatForever()));
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

::: warning
With the unnamed default scheduler, call `services.AddQuartz(...)` before `services.AddQuartzHostedService(...)`.
`AddQuartzHostedService()` registers the default hosted service only when `ISchedulerFactory` is already in the service collection; in the reverse order the default scheduler is not started.
:::

## Configuration via appsettings.json

Supply named scheduler properties through the options pattern:

```csharp
builder.Services.Configure<QuartzOptions>("DurableScheduler",
    builder.Configuration.GetSection("Quartz:DurableScheduler"));
```

```json
{
  "Quartz": {
    "DurableScheduler": {
      "quartz.scheduler.instanceId": "AUTO",
      "quartz.jobStore.type": "Quartz.Impl.AdoJobStore.JobStoreTX, Quartz"
    }
  }
}
```

## Limitations

- **Hosted service options are global:** `QuartzHostedServiceOptions` (such as `WaitForJobsToComplete`, `StartDelay`, `AwaitApplicationStarted`) apply to all schedulers.
- **Job types are shared:** job classes are resolved from the shared DI container, so one job type can be used by several schedulers.
- **Scheduler names must be unique:** each `AddQuartz(name, ...)` call needs a distinct name.

## See Also

A scheduler per tenant is one of three ways to partition tenants.

- [Multi-Tenancy](../multi-tenancy.md): the three separations 3.x offers, and their limits
- [Tenancy Patterns](../../tenancy-patterns.md): how other schedulers partition tenants, and how to choose
