---
title: 'Building a Scheduler Without a Host'
---

Not every scheduler lives in a web application. A console tool, a library, a test, a worker that does
its own lifecycle management — all of them want a scheduler and none of them has an
`IServiceCollection` to hang it on.

`QuartzSchedulerBuilder` is for those. It creates a container of its own, configures the scheduler with
the *same* API `AddQuartz` uses, and hands back something you can dispose.

<!-- snippet: sample_standalone_scheduler -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .ConfigureScheduler(o => o.InstanceName = "reporting")
    .UseDefaultThreadPool(maxConcurrency: 20)
    .UseInMemoryStore()
    .BuildScheduler();

await scheduler.Start();
```
<!-- endSnippet -->

## One configuration API, two entry points

`QuartzSchedulerBuilder` implements `IQuartzBuilder`, which is the interface the `AddQuartz` callback
gives you. Every configuration verb is therefore the same verb:

`ConfigureScheduler`, `ConfigureOptions<TOptions>`, `UseDefaultThreadPool`, `UseThreadPool<T>`,
`UseInMemoryStore`, `UsePersistentStore`, `UseJobStore<T>`, `UseJobFactory<T>`, `UseTypeLoader<T>`,
`UseInstanceIdGenerator<T>`, `UseTimeProvider`, `UseExecutionLimits`, `AddPlugin<T>`,
`AddSchedulerListener<T>`, `AddJobListener<T>`, `AddTriggerListener<T>` — plus the extension methods
`AddJob<T>`, `AddTrigger<TJob>`, `ScheduleJob<T>` and `AddCalendar`.

Learn the configuration API once and it works in both places. Only three members are the builder's own:
`Build()`, `BuildScheduler()`, `UseConfiguration(IConfiguration)` and the two `UseProperties` overloads.

Every configuration member returns `QuartzSchedulerBuilder`, so the whole thing is one expression:

<!-- snippet: sample_standalone_one_expression -->
```csharp
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
    .UseInMemoryStore()
    .UseDefaultThreadPool(10)
    .Build();
```
<!-- endSnippet -->

::: warning Changed in 4.x
In the 4.0 previews the configuration members returned `IQuartzBuilder`, so a chain had to be broken up
and the builder held in a variable to reach `Build()`. The returns are covariant now and
`Create()…Build()` is one statement.
:::

## Build, or BuildScheduler

Two endings, for two different needs:

<!-- snippet: sample_standalone_build_scheduler_ending -->
```csharp
// I want the scheduler
IScheduler scheduler = await QuartzSchedulerBuilder.Create().UseInMemoryStore().BuildScheduler();
```
<!-- endSnippet -->

<!-- snippet: sample_standalone_build_ending -->
```csharp
// I want to own the lifetime
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create().UseInMemoryStore().Build();
IScheduler scheduler = await factory.GetScheduler();
```
<!-- endSnippet -->

`BuildScheduler()` is `Build().GetScheduler()`, and it drops the factory on the floor — which is fine
for a process whose scheduler lives as long as the process, and wrong for anything that has to clean
up.

`Build()` also validates the container it creates (`ValidateOnBuild`, `ValidateScopes`), so a
registration mistake surfaces there rather than at the first job execution.

## The factory owns the container

`StandaloneSchedulerFactory` is an `ISchedulerFactory` that also implements `IDisposable` and
`IAsyncDisposable`. Disposing it shuts the scheduler down and *then* disposes the service provider it
was built with, along with everything that container created — the job store, the thread pool, your own
registered services. That is the order the hosted service uses when an application stops, and it is that
way round for a reason: a container disposed underneath a running scheduler leaves it firing triggers
whose jobs it can no longer build.

<!-- snippet: sample_standalone_factory_owns_the_container -->
```csharp
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
    .UseInMemoryStore()
    .Build();

IScheduler scheduler = await factory.GetScheduler();
await scheduler.Start();

// ... do work ...

// leaving the scope shuts the scheduler down, then disposes the container
```
<!-- endSnippet -->

Prefer `await using`. The synchronous `Dispose()` exists so the type fits `using` in code that cannot
be async; it blocks on the same shutdown, which is all a synchronous door onto asynchronous work can do.

The shutdown does not wait for running jobs to finish, which is the default that
`QuartzHostedServiceOptions.WaitForJobsToComplete` and `IScheduler.Shutdown()` both carry. Say so
yourself when you want to wait, and dispose afterwards — disposal then finds nothing left to shut down:

<!-- snippet: sample_standalone_wait_for_jobs -->
```csharp
await scheduler.Shutdown(waitForJobsToComplete: true);
```
<!-- endSnippet -->

Disposing twice does nothing the second time, and disposing a factory whose `GetScheduler()` was never
called does nothing at all: a scheduler is never built merely to be torn down.

::: warning Fixed in 4.0.0-alpha.2
In 4.0.0-alpha.1 disposing the factory disposed only the container. The scheduler stayed running and
kept firing, and where something had injected `IScheduler` the synchronous `Dispose()` threw
`InvalidOperationException` instead of shutting anything down
([#3380](https://github.com/quartznet/quartznet/issues/3380)).
:::

**Never disposing is a supported choice.** A console application whose scheduler runs until the process
ends behaves exactly as it did with the process-lifetime scheduler of earlier versions. The dispose
story exists for the cases where a scheduler is *shorter*-lived than the process: a test, a CLI
subcommand, a plug-in host.

::: warning
A scheduler that has been shut down cannot be restarted. The container owns its parts' lifetimes, so
`GetScheduler()` after a `Shutdown()` throws rather than quietly handing back a dead instance — build a
new factory instead. `Standby()` / `Start()` is the pause-and-resume pair.
:::

## Jobs, triggers and calendars

The `IQuartzBuilder` extension methods work here unchanged:

<!-- snippet: sample_standalone_jobs_triggers_and_calendars -->
```csharp
QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create().UseInMemoryStore();

builder
    .AddJob<ReportJob>(j => j.WithIdentity("nightly", "reports").StoreDurably())
    .AddTrigger<ReportJob>(t => t
        .ForJob("nightly", "reports")
        .WithIdentity("nightly-trigger", "reports")
        .WithCronSchedule("0 30 2 * * ?"))
    .AddCalendar<HolidayCalendar>("holidays", configure: c => c.AddExcludedDay(new DateOnly(2026, 12, 25)));

await using StandaloneSchedulerFactory factory = builder.Build();
```
<!-- endSnippet -->

`AddJob`, `AddTrigger`, `ScheduleJob` and `AddCalendar` are extension methods over `IQuartzBuilder`, and
they return `IQuartzBuilder` rather than the builder's own type — so `Build()` comes off the variable
rather than off the end of that chain. The interface's own members are covariant, so a chain made only
of those still ends in `Build()`.

Jobs declared this way are registered with the container, so they can take constructor dependencies:

<!-- snippet: sample_standalone_registering_services -->
```csharp
QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create();
builder.Services.AddSingleton<IReportRenderer, PdfReportRenderer>();
builder.Services.AddHttpClient();
builder.UseInMemoryStore().AddJob<ReportJob>(j => j.WithIdentity("nightly"));
```
<!-- endSnippet -->

`Services` is a real `IServiceCollection`. Anything you would register in an application container you
can register here.

## Configuration from a file

`UseConfiguration` is the standalone counterpart of `AddQuartz(configuration)`, and reads the section
exactly the way a host does — hierarchical `Scheduler` and `ThreadPool` sections bind onto the typed
options, a `Schedule` section becomes jobs and triggers, and flat `quartz.*` keys still mean what they
always meant:

<!-- snippet: sample_standalone_configuration -->
```csharp
IConfiguration configuration = new ConfigurationBuilder()
    .AddJsonFile("appsettings.json")
    .AddEnvironmentVariables()
    .Build();

await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create()
    .UseConfiguration(configuration.GetSection("Quartz"))
    .Build();
```
<!-- endSnippet -->

`UseProperties` is the flat-key path, for a properties file or an environment-derived bag — the shape
`StdSchedulerFactory` took:

<!-- snippet: sample_standalone_properties -->
```csharp
NameValueCollection properties = new()
{
    ["quartz.scheduler.instanceName"] = "reporting",
    ["quartz.threadPool.maxConcurrency"] = "20",
};

QuartzSchedulerBuilder.Create().UseProperties(properties);
```
<!-- endSnippet -->

There is also an overload taking `IEnumerable<KeyValuePair<string, string?>>`, which is the shape a
`Dictionary<string, string?>` and `QuartzOptions.Properties` already have.

**Code wins, whichever order the two are written in.** Values from configuration and properties are
applied *before* anything the builder was told, and implementations they name are registered *after* —
because options are last-wins and registrations are first-wins. Applying them where the call happened to
appear would make precedence depend on the order you happened to type things.

Property keys are checked against the ones Quartz reads, so a misspelling is reported rather than
silently ignored. Set `quartz.checkConfiguration` to `false` when you keep keys of your own in the same
bag.

## Persistent and clustered, standalone

Nothing about persistence needs a host:

<!-- snippet: sample_standalone_persistent_and_clustered -->
```csharp
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
        s.Configure(o => o.TablePrefix = "QRTZ_");
    })
    .Build();
```
<!-- endSnippet -->

The dialect methods — `UseSqlServer`, `UsePostgres`, `UseMySql`, `UseMySqlConnector`, `UseSqlite`,
`UseSystemDataSqlite`, `UseOracle`, `UseFirebird`, `UseGenericDatabase` — each take either a connection
string or an `Action<DataSourceOptions>`.

::: tip
Options validation behaves slightly differently without a host. `ValidateOnStart` is wired up either
way, but the startup validator that runs it is a hosted service — with no host it never runs, so a bad
option value is reported the first time the options are read, during `GetScheduler()`, instead of at
application start. It is still reported; it is just reported a moment later.
:::

## Scheduler isolation

Each `Build()` creates its own container, and each container has its own `ISchedulerRepository`. Two
standalone factories therefore never see each other's schedulers: `GetAllSchedulers()` on one returns
only what it built, and `LookupScheduler(name)` cannot find the other's.

That is what makes parallel tests safe, and it is occasionally not what you want. When several entry
points genuinely must share one repository, register a shared instance before building — Quartz's own
registration is `TryAdd`, so yours wins:

<!-- snippet: sample_standalone_shared_repository -->
```csharp
ISchedulerRepository shared = new SchedulerRepository();

QuartzSchedulerBuilder first = QuartzSchedulerBuilder.Create();
first.Services.AddSingleton(shared);

QuartzSchedulerBuilder second = QuartzSchedulerBuilder.Create();
second.Services.AddSingleton(shared);
```
<!-- endSnippet -->

## What the container-first path adds

Standalone gives you a scheduler. `AddQuartz` in an application container gives you a scheduler plus
everything a host makes possible:

| Container-first | Standalone |
|---|---|
| `AddQuartzHostedService` — start, graceful shutdown, `WaitForJobsToComplete`, `StartDelay` | you call `Start()` and `Shutdown()` |
| `AddQuartz(name, …)` — several named schedulers, keyed by name | one scheduler per builder |
| `AddQuartzHealthChecks` | — |
| `AddQuartzHttpApi` / the dashboard | — |
| Options validated at application start | validated on first use |
| Configuration bound by the host | `UseConfiguration(section)` |
| Application services already registered | register them on `Services` yourself |

`SchedulerName` on the builder is `""` — the standalone builder configures the default scheduler, and
the instance name comes from `ConfigureScheduler(o => o.InstanceName = …)`.

If a process is already a `HostApplicationBuilder` or a `WebApplicationBuilder`, use `AddQuartz`. The
standalone builder is for the processes that are not.

## Coming from 3.x

| 3.x | 4.x |
|---|---|
| `StdSchedulerFactory.GetDefaultScheduler()` | `QuartzSchedulerBuilder.Create().UseInMemoryStore().BuildScheduler()` |
| `new StdSchedulerFactory(properties)` | `QuartzSchedulerBuilder.Create().UseProperties(properties)` |
| `DirectSchedulerFactory.Instance.CreateScheduler(…)` | the `Use…` members — the builder *is* the direct path |
| `SchedulerBuilder.Create()` | `QuartzSchedulerBuilder.Create()` |
| `quartz.config` picked up implicitly | `UseConfiguration` or `UseProperties`, explicitly |

The big change is ownership: 3.x's factory was a process-wide singleton handing out schedulers that
lived forever, and 4.x's factory is an object you hold and dispose. Everything else is a rename.

## See also

- [Configuration, Resource Usage and Building a Scheduler](configuration-resource-usage-and-scheduler-factory.md) — the container-first path
- [Testing](testing.md) — the standalone builder is the entry point every test uses
- [Configuration Reference](../configuration/reference.md) — every option, typed and legacy
- [Microsoft DI Integration](../packages/microsoft-di-integration.md) — `AddQuartz` in depth
