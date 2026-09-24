---
title: 'Building a Scheduler Without a Host'
---

Use `QuartzSchedulerBuilder` when there is no `IServiceCollection` to register a scheduler with: a
console tool, a library, a test, a worker that manages its own lifecycle. It creates its own container,
configures the scheduler with the *same* API as `AddQuartz`, and returns something you can dispose.

<!-- snippet: sample_standalone_scheduler -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q
        .ConfigureScheduler(o => o.InstanceName = "reporting")
        .UseDefaultThreadPool(maxConcurrency: 20)
        .UseInMemoryStore())
    .BuildScheduler();

await scheduler.Start();
```
<!-- endSnippet -->

## One configuration API, two entry points

`Create` passes the callback an `IQuartzBuilder`, the same interface the `AddQuartz` callback gets, so
`q => q.ConfigureScheduler(…)` is the same method in both places. That covers:

`ConfigureScheduler`, `ConfigureOptions<TOptions>`, `UseDefaultThreadPool`, `UseThreadPool<T>`,
`UseInMemoryStore`, `UsePersistentStore`, `UseJobStore<T>`, `UseJobFactory<T>`, `UseTypeLoader<T>`,
`UseInstanceIdGenerator<T>`, `UseTimeProvider`, `UseExecutionLimits`, `AddPlugin<T>`,
`AddSchedulerListener<T>`, `AddJobListener<T>`, `AddTriggerListener<T>`, the extension methods
`AddJob<T>`, `AddTrigger<TJob>`, `ScheduleJob<T>` and `AddCalendar`, and every extension a package of
your own contributes.

The builder's own members are only `Create(configure)`, `Build()`, `BuildScheduler()`,
`UseConfiguration(IConfiguration)` and the two `UseProperties` overloads. It declares no configuration
member of its own, so it cannot fall behind `AddQuartz`.

The callback runs immediately, and the terminal methods are on the builder it returns, so the whole thing
is one expression:

<!-- snippet: sample_standalone_one_expression -->
```csharp
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q => q
        .UseInMemoryStore()
        .UseDefaultThreadPool(10))
    .Build();
```
<!-- endSnippet -->

## Build, or BuildScheduler

<!-- snippet: sample_standalone_build_scheduler_ending -->
```csharp
// I want the scheduler
IScheduler scheduler = await QuartzSchedulerBuilder.Create(q => q.UseInMemoryStore()).BuildScheduler();
```
<!-- endSnippet -->

<!-- snippet: sample_standalone_build_ending -->
```csharp
// I want to own the lifetime
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder.Create(q => q.UseInMemoryStore()).Build();
IScheduler scheduler = await factory.GetScheduler();
```
<!-- endSnippet -->

* `BuildScheduler()` is `Build().GetScheduler()` and discards the factory. Use it when the scheduler
  lives as long as the process, not when something must clean up.
* `Build()` validates the container it creates (`ValidateOnBuild`, `ValidateScopes`), so a registration
  mistake fails there rather than at the first job execution.

## The factory owns the container

`StandaloneSchedulerFactory` is an `ISchedulerFactory` that also implements `IDisposable` and
`IAsyncDisposable`. Disposing it shuts the scheduler down, *then* disposes the service provider and
everything that container created: the job store, the thread pool, your registered services. The hosted
service uses the same order, because a container disposed under a running scheduler leaves it firing
triggers whose jobs it can no longer build.

<!-- snippet: sample_standalone_factory_owns_the_container -->
```csharp
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q => q.UseInMemoryStore())
    .Build();

IScheduler scheduler = await factory.GetScheduler();
await scheduler.Start();

// ... do work ...

// leaving the scope shuts the scheduler down, then disposes the container
```
<!-- endSnippet -->

* Prefer `await using`. The synchronous `Dispose()` exists for code that cannot be async, and blocks on
  the same shutdown.
* The shutdown does not wait for running jobs, the same default as
  `QuartzHostedServiceOptions.WaitForJobsToComplete` and `IScheduler.Shutdown()`. To wait, shut down
  yourself first; disposal then finds nothing left to shut down:

<!-- snippet: sample_standalone_wait_for_jobs -->
```csharp
await scheduler.Shutdown(waitForJobsToComplete: true);
```
<!-- endSnippet -->

* Disposing twice does nothing the second time. Disposing a factory whose `GetScheduler()` was never
  called does nothing; no scheduler is built just to be torn down.
* **Never disposing is a supported choice.** A console application whose scheduler runs until the
  process ends behaves as the process-lifetime scheduler of earlier versions did. Disposal is for a
  scheduler *shorter*-lived than the process: a test, a CLI subcommand, a plug-in host.

::: warning
A scheduler that has been shut down is not restarted in place. The container owns its parts' lifetimes,
so `GetScheduler()` after a `Shutdown()` throws instead of returning a dead instance; build a new factory.
`Standby()` / `Start()` is the pause-and-resume pair. In an application with a container,
[`ISchedulerRuntime.Restart`](../multi-tenancy.md#restarting-a-scheduler) builds the new one for you from
the recipe the old one was built with.
:::

## Jobs, triggers and calendars

The `IQuartzBuilder` extension methods work unchanged and chain inside the callback:

<!-- snippet: sample_standalone_jobs_triggers_and_calendars -->
```csharp
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q => q
        .UseInMemoryStore()
        .AddJob<ReportJob>(j => j.WithIdentity("nightly", "reports").StoreDurably())
        .AddTrigger<ReportJob>(t => t
            .ForJob("nightly", "reports")
            .WithIdentity("nightly-trigger", "reports")
            .WithCronSchedule("0 30 2 * * ?"))
        .AddCalendar<HolidayCalendar>("holidays", configure: c => c.AddExcludedDay(new DateOnly(2026, 12, 25))))
    .Build();
```
<!-- endSnippet -->

Jobs declared this way are registered with the container, so they can take constructor dependencies.
`q.Services` is a real `IServiceCollection`; register anything there you would register in an
application container:

<!-- snippet: sample_standalone_registering_services -->
```csharp
QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create(q =>
{
    q.Services.AddSingleton<IReportRenderer, PdfReportRenderer>();
    q.Services.AddHttpClient();
    q.UseInMemoryStore().AddJob<ReportJob>(j => j.WithIdentity("nightly"));
});
```
<!-- endSnippet -->

## Configuration from a file

`UseConfiguration` is the standalone counterpart of `AddQuartz(configuration)` and reads the section as
a host does: hierarchical `Scheduler` and `ThreadPool` sections bind to the typed options, a `Schedule`
section becomes jobs and triggers, and flat `quartz.*` keys keep their meaning.

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

`UseProperties` takes flat keys, from a properties file or an environment-derived bag, as
`StdSchedulerFactory` did:

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

Another overload takes `IEnumerable<KeyValuePair<string, string?>>`, the shape of a
`Dictionary<string, string?>` and of `QuartzOptions.Properties`.

* **Code wins, whichever order the calls are written in.** Values from configuration and properties are
  applied *before* anything the builder was told, and implementations they name are registered *after*,
  because options are last-wins and registrations first-wins.
* Property keys are checked against the ones Quartz reads, so a misspelling is reported. Set
  `quartz.checkConfiguration` to `false` when you keep keys of your own in the same bag.

## Persistent and clustered, standalone

Persistence needs no host:

<!-- snippet: sample_standalone_persistent_and_clustered -->
```csharp
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q => q
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
        }))
    .Build();
```
<!-- endSnippet -->

The dialect methods (`UseSqlServer`, `UsePostgres`, `UseMySql`, `UseMySqlConnector`, `UseSqlite`,
`UseSystemDataSqlite`, `UseOracle`, `UseFirebird`, `UseGenericDatabase`) each take a connection string or
an `Action<DataSourceOptions>`.

::: tip
Without a host, options validation runs later. `ValidateOnStart` is wired up either way, but the
validator that runs it is a hosted service, so without a host a bad option value is reported the first
time the options are read, during `GetScheduler()`, instead of at application start.
:::

## Scheduler isolation

Each `Build()` creates its own container with its own `ISchedulerRepository`. Two standalone factories
never see each other's schedulers: `GetAllSchedulers()` on one returns only what it built, and
`LookupScheduler(name)` cannot find the other's. That keeps parallel tests safe.

To share one repository between several entry points, register a shared instance before building.
Quartz's own registration is `TryAdd`, so yours wins:

<!-- snippet: sample_standalone_shared_repository -->
```csharp
ISchedulerRepository shared = new SchedulerRepository();

QuartzSchedulerBuilder first = QuartzSchedulerBuilder.Create(q => q.Services.AddSingleton(shared));
QuartzSchedulerBuilder second = QuartzSchedulerBuilder.Create(q => q.Services.AddSingleton(shared));
```
<!-- endSnippet -->

## What the container-first path adds

| Container-first | Standalone |
|---|---|
| `AddQuartzHostedService` — start, graceful shutdown, `WaitForJobsToComplete`, `StartDelay` | you call `Start()` and `Shutdown()` |
| `AddQuartz(name, …)` — several named schedulers, keyed by name | one scheduler per builder |
| `AddHealthChecks().AddQuartz()` / `q.AddQuartzHealthChecks()` | — |
| `AddQuartzHttpApi` / the dashboard | — |
| Options validated at application start | validated on first use |
| Configuration bound by the host | `UseConfiguration(section)` |
| Application services already registered | register them on `q.Services` yourself |

* `q.SchedulerName` inside the callback is `""`: the standalone builder configures the default
  scheduler. The instance name comes from `ConfigureScheduler(o => o.InstanceName = …)`.
* If the process already has a `HostApplicationBuilder` or `WebApplicationBuilder`, use `AddQuartz`
  ([Configuration, Resource Usage and Building a Scheduler](configuration-resource-usage-and-scheduler-factory.md),
  [Microsoft DI Integration](../packages/microsoft-di-integration.md)).

The standalone builder is the entry point for tests; see [Testing](testing.md). Every option, typed and
legacy, is in the [Configuration Reference](../configuration/reference.md).

## Coming from 3.x

| 3.x | 4.x |
|---|---|
| `StdSchedulerFactory.GetDefaultScheduler()` | `QuartzSchedulerBuilder.Create(q => q.UseInMemoryStore()).BuildScheduler()` |
| `new StdSchedulerFactory(properties)` | `QuartzSchedulerBuilder.Create().UseProperties(properties)` |
| `DirectSchedulerFactory.Instance.CreateScheduler(…)` | the `Use…` members in the callback — that *is* the direct path |
| `SchedulerBuilder.Create()` | `QuartzSchedulerBuilder.Create(q => …)` |
| `quartz.config` picked up implicitly | `UseConfiguration` or `UseProperties`, explicitly |

The main change is ownership: 3.x's factory was a process-wide singleton handing out schedulers that
lived forever; 4.x's factory is an object you hold and dispose. The rest are renames.
