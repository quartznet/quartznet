---

title: Multiple Schedulers with Microsoft DI
---

Quartz.NET has always supported running multiple schedulers in a single process — each `QuartzSchedulerBuilder` builds an independent scheduler, and an `ISchedulerRepository` tracks by name the schedulers built alongside it. However, configuring multiple schedulers through the Microsoft DI `AddQuartz()` API required workarounds because the registration model was designed around a single scheduler per container.

The named `AddQuartz(string name, ...)` overload makes this first-class: each named scheduler gets its own isolated configuration, jobs, triggers, listeners, and calendars, all managed through the familiar DI fluent API.

::: tip
If you are not using Microsoft DI, you can create multiple schedulers from separate `QuartzSchedulerBuilder`s, each given its own `ConfigureScheduler(options => options.InstanceName = ...)`, and call `BuildScheduler()` on each.
:::

## When to Use Named Schedulers

- **Different job stores** — one scheduler uses in-memory storage for transient jobs, another uses a persistent database store for durable jobs
- **Workload isolation** — separate critical jobs from background maintenance tasks with independent thread pools
- **Different configurations** — schedulers with different misfire thresholds, batch sizes, or clustering settings

## Basic Configuration

Register each scheduler with a unique name using the `AddQuartz(string name, ...)` overload:

```csharp
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
```

## Per-Scheduler Listeners and Calendars

Listeners and calendars registered within a named `AddQuartz` call are scoped to that scheduler only:

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

## Injecting a Named Scheduler

A scheduler's name is the service key it is registered under, so a named scheduler is injected the way
any other keyed service is:

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

Resolved directly, it is the same thing:

```csharp
var fast = provider.GetRequiredKeyedService<IScheduler>("FastScheduler");
var standard = provider.GetRequiredService<IScheduler>();   // the default scheduler, if one is registered
```

Everything a named scheduler is built from is registered under that key, so `ISchedulerFactory` and the
rest are reachable the same way — `GetRequiredKeyedService<ISchedulerFactory>("FastScheduler")` — while
the unkeyed registrations belong to the default scheduler.

::: warning
What is injected is a handle that builds the scheduler on first use, because building one is
asynchronous and a container constructs synchronously. Every asynchronous member awaits it being built,
so they are always safe. The synchronous ones — `IsStarted`, `InStandbyMode`, `IsShutdown`,
`SchedulerInstanceId`, `Context` and `ListenerManager` — can only answer once the scheduler exists, and
throw `InvalidOperationException` if reading one would have to build it. Under
`AddQuartzHostedService()` that cannot happen: every scheduler in the container is built and started
before the application runs. `SchedulerName` never builds anything.
:::

### Finding a scheduler at runtime

Where the name is not known until runtime — a dashboard listing what is running, a request naming the
scheduler it is for — the container's `ISchedulerRepository` holds every scheduler that has been built:

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

::: warning
The repository holds schedulers that have been *built*, so during application startup it may not yet
hold them all — injecting them by key does not have that problem, since the handle builds the scheduler
it names.

The repository is scoped to the container, not the process. A scheduler built by a
`QuartzSchedulerBuilder` of its own is not in it — see
[the migration guide](../migration-guide.md#no-process-global-scheduler-or-connection-state).
:::

## Mixing Default and Named Schedulers

You can combine the traditional unnamed `AddQuartz()` with named schedulers:

```csharp
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
```

::: tip
The order of the calls does not matter. The hosted service resolves the schedulers when the host
starts, so it starts every scheduler registered in the container whether `AddQuartz` was called
before it or after. A container with no scheduler at all is reported at startup rather than starting
nothing silently.
:::

## Configuration via appsettings.json

A named scheduler's configuration can come from a section. Pass the root `Quartz` section and the
scheduler's own settings are resolved out of `Schedulers:{name}`:

```csharp
builder.AddQuartz("DurableScheduler");
// or, naming the section yourself:
builder.Services.AddQuartz("DurableScheduler", builder.Configuration.GetSection("Quartz"));
```

To register every scheduler the section describes rather than one of them, call
`AddQuartzSchedulers`, which registers one named scheduler per child of `Schedulers`:

```csharp
builder.AddQuartzSchedulers();
// or:
builder.Services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));
```

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

Individual flat keys that no typed option covers can still be set on the named options directly, through
the `Properties` dictionary:

```csharp
builder.Services.Configure<QuartzOptions>("DurableScheduler",
    options => options.Properties["quartz.jobStore.someThirdPartySetting"] = "value");
```

## Per-Scheduler Startup and Shutdown

`AddQuartzHostedService(configure)` configures every scheduler, which is what it has always meant. A
scheduler that has to differ says so by name, and its settings refine the shared ones whichever order
the two calls are made in:

```csharp
// shared by every scheduler
builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

// ...except this one, which waits longer before its first fire
builder.Services.AddQuartzHostedService("DurableScheduler", options =>
{
    options.StartDelay = TimeSpan.FromMinutes(2);
});
```

## Limitations

- **Job types are shared** — job classes are resolved from the shared DI container. The same job type can be used across multiple schedulers.
- **Scheduler names must be unique** — each call to `AddQuartz(name, ...)` must use a distinct name.
