---

title: Migration Guide
---

*This document outlines changes needed when upgrading from Quartz.NET 3.x to 4.x. You should also check [the complete change log](https://raw.github.com/quartznet/quartznet/master/changelog.md).*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)
:::

## Target Framework

Quartz.NET 4.x targets `net10.0`. There is no `netstandard2.0` build and no support for Full Framework
style `.config` files.

If you are running on an older .NET version, you will need to upgrade your application to .NET 10
before upgrading to Quartz 4.x.

## Configuration

This is the largest change in 4.x. Configuration is now strongly typed options and service
registrations rather than a bag of `quartz.*` strings, and the dependency injection container builds
the scheduler.

### Flat keys still work

If you configure Quartz from `appsettings.json` or a `NameValueCollection` of `quartz.*` keys, that
keeps working. The keys are translated into the typed options, and both spellings of a setting always
produce the same result. You do not have to migrate configuration files to move to 4.x.

### Code-first configuration is typed

Settings that used to be write-only properties on the configurator are now options:

```diff
  services.AddQuartz(q =>
  {
-     q.ConfigureScheduler(options => options.InstanceName = "core");
-     q.ConfigureScheduler(options => options.InstanceId = "node-1");
-     q.MaxBatchSize = 5;
-     q.InterruptJobsOnShutdown = true;
+     q.ConfigureScheduler(options =>
+     {
+         options.InstanceName = "core";
+         options.InstanceId = "node-1";
+         options.MaxBatchSize = 5;
+         options.InterruptJobsOnShutdown = true;
+     });
  });
```

The option names are the same words as the configuration keys, so `Quartz:Scheduler:MaxBatchSize` and
`options.MaxBatchSize` are the same setting said two ways.

### Data sources no longer need a name

A scheduler has one job store and therefore one database, so there is no name to invent:

```diff
  q.UsePersistentStore(store =>
  {
-     store.UseProperties = true;
      store.UseClustering();
-     store.UseSqlServer("sql-server-01", connectionString);
+     store.UseSqlServer(connectionString);
      store.UseSystemTextJsonSerializer();
+     store.Configure(options => options.UseProperties = true);
  });
```

Schedulers that need different databases are registered under different names, and each gets its own
services.

### The quartz.config file is no longer read

Nothing is loaded from disk any more. A `quartz.config` file next to your application — or named by the
`quartz.config` environment variable — is ignored, and so is the copy of it Quartz used to ship as an
embedded resource. `StdSchedulerFactory` reads only the properties you hand it plus any `quartz.*`
environment variables; everything else configures a scheduler through the container.

**No defaults change.** The three settings the embedded file supplied are now seeded by
`StdSchedulerFactory.Initialize()`, which is the only entry point that ever read the file:

| Setting | Value |
|---|---|
| `quartz.scheduler.instanceName` | `DefaultQuartzScheduler` |
| `quartz.threadPool.threadCount` | 10 |
| `quartz.jobStore.misfireThreshold` | 60000 |

Environment variables still override them, and anything you pass to `Initialize(NameValueCollection)`
replaces them, exactly as the file behaved.

Note these were never the defaults for `AddQuartz` or for `new StdSchedulerFactory(properties)`: handing
the factory properties always bypassed the file, so those paths fell back — and still fall back — to
`QuartzSchedulerOptions.InstanceName` (`QuartzScheduler`) and
`InMemoryJobStoreOptions.MisfireThreshold` (5 seconds). Set them explicitly if you want the other values.

The one thing the file was still needed for was describing an ADO.NET driver Quartz ships no metadata
for. That now has a code-first form, and the `quartz.dbprovider.*` keys themselves still work — they just
arrive through `IConfiguration` or a `NameValueCollection` like every other key:

```csharp
q.UsePersistentStore(store => store.UseGenericDatabase("MyDatabase", connectionString, metadata =>
{
    metadata.ProductName = "My Database";
    metadata.ConnectionType = typeof(MyConnection);
    metadata.CommandType = typeof(MyCommand);
    metadata.ParameterType = typeof(MyParameter);
    metadata.ParameterDbType = typeof(MyDbType);
    metadata.ParameterDbTypePropertyName = nameof(MyParameter.MyDbType);
    metadata.ParameterNamePrefix = "@";
    metadata.DbBinaryTypeName = "VarBinary";
}));
```

See [the configuration reference](configuration/reference.md#describing-a-driver-quartz-does-not-know) for the
full description. `DbProvider.RegisterDbMetadata` is gone with the process-wide lookup it wrote into; use
the callback above, or register a `DbMetadataFactory` in the container.
### `QuartzOptions` is no longer a dictionary

`QuartzOptions` used to derive from `Dictionary<string, string?>`, and to hold a scheduler's jobs and
triggers as well as its flat keys. It was the pivot the whole configuration model turned on; now that
settings are typed options, the only things left in it were the legacy keys and the one thing that was
never configuration at all. The keys moved to a `Properties` dictionary, and jobs and triggers became a
per-scheduler registration like every other part of a scheduler.

```diff
  services.Configure<QuartzOptions>(options =>
  {
-     options["quartz.plugin.jobHistory.type"] = "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins";
+     options.Properties["quartz.plugin.jobHistory.type"] = "Quartz.Plugin.History.LoggingJobHistoryPlugin, Quartz.Plugins";
  });
```

A plugin whose type you know is better added as one, which also gives it constructor injection:

```csharp
services.AddQuartz(q => q.AddPlugin<LoggingJobHistoryPlugin>());
```

Because the keys are a property rather than the object itself, a section of flat keys no longer binds
onto `QuartzOptions` directly — it would bind `Quartz:Properties:*`. Pass the section to `AddQuartz`,
which reads the keys where they have always been and binds the typed options from the same section:

```diff
- services.Configure<QuartzOptions>(configuration.GetSection("Quartz"));
  services.AddQuartz(configuration.GetSection("Quartz"), q => { /* ... */ });
```

Jobs and triggers are added through the builder. The overloads taking an `IServiceProvider` cover the
case the options callback used to be needed for:

```diff
- services.AddOptions<QuartzOptions>()
-     .Configure<IOptions<SampleOptions>>((options, sample) =>
-     {
-         options.AddJob<ExampleJob>(j => j.WithIdentity("job", "group"));
-         options.AddTrigger(t => t
-             .ForJob("job", "group")
-             .WithCronSchedule(sample.Value.CronSchedule));
-     });
+ services.AddQuartz(q =>
+ {
+     q.AddJob<ExampleJob>(new JobKey("job", "group"));
+     q.AddTrigger((provider, t) => t
+         .ForJob("job", "group")
+         .WithCronSchedule(provider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule));
+ });
```

`QuartzOptions.SchedulerName` also used to read and write `schedName` — an ADO.NET column key that
nothing reads — so a scheduler name set through it was accepted and then silently ignored. It now reads
and writes `quartz.scheduler.instanceName`, which is the key the rest of the model uses.

### Removed

| Removed | Use instead |
|---|---|
| `QuartzOptions : Dictionary<string, string?>` | `QuartzOptions.Properties` |
| `QuartzOptions.JobDetails`, `.Triggers`, `.AddJob`, `.AddTrigger` | `AddQuartz(q => q.AddJob(…))` / `q.AddTrigger(…)` |
| `StdSchedulerFactory.PropertySchedulerName` | nothing; it named an ADO.NET column, not a setting |
| `SchedulerBuilder` | `QuartzSchedulerBuilder` for standalone use, `AddQuartz` under a host |
| `DirectSchedulerFactory` | `QuartzSchedulerBuilder`, with `UseThreadPool` / `UseJobStore` for pre-built parts |
| `IPropertyConfigurer`, `IPropertySetter`, `IPropertyConfigurationRoot`, `PropertiesHolder`, `PropertiesSetter` | typed options |
| `AddQuartz(Action<configurator, IServiceProvider>)` | see below |
| `quartz.config` file discovery, `StdSchedulerFactory.PropertiesFile` | `IConfiguration`, or properties passed to `StdSchedulerFactory` |
| `DbProvider.RegisterDbMetadata` | the metadata callback on `UseGenericDatabase`, or a `DbMetadataFactory` registration |
| `quartz.scheduler.proxy*`, `quartz.scheduler.exporter*` | nothing; remoting is not supported on modern .NET |
| `quartz.checkConfiguration` | configuration is validated by the options system |
| `SchedulerRepository.Instance` | `ISchedulerRepository` resolved from the container |
| `DBConnectionManager.Instance` | `IDbConnectionManager` resolved from the container |
| `StdSchedulerFactory.GetDbConnectionManager()` | nothing; it had no callers |

### Deferred configuration

The `AddQuartz` overloads taking an `IServiceProvider` are gone. They existed to reach services while
configuring, which the options pattern already does:

```diff
- services.AddQuartz((q, provider) =>
- {
-     var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Scheduler");
-     q.UsePersistentStore(store => store.UseSqlServer("default", connectionString));
- });
+ var connectionString = builder.Configuration.GetConnectionString("Scheduler");
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(connectionString)));
```

For an option that genuinely depends on a service, use the options pattern directly:

```csharp
services.AddOptions<AdoJobStoreOptions>()
    .Configure<IMyService>((options, service) => options.TablePrefix = service.TablePrefix);
```

Listeners and plugins do not need this at all: they are registered services, so the container injects
their dependencies.

### SPI changes

If you implement `IJobStore` or `ISchedulerPlugin` yourself, they take their collaborators through
constructors now instead of being handed them afterwards.

`IJobStore` loses `InstanceId`, `InstanceName`, `ThreadPoolSize` and `TimeProvider`, and `Initialize`
loses its parameters:

```diff
- ValueTask Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default);
+ ValueTask Initialize(CancellationToken cancellationToken = default);
```

Take what you need — `ISchedulerSignaler`, `ITypeLoadHelper`, `TimeProvider`,
`IOptions<QuartzSchedulerOptions>` — through your constructor. What remains in `Initialize` is work
that has to happen before the scheduler runs and cannot be done while constructing, such as verifying
a database schema.

Plugin configuration extension methods now extend `IQuartzBuilder` and register the plugin as a
service, rather than deriving from `PropertiesSetter` to write string keys.

### No process-global scheduler or connection state

`SchedulerRepository.Instance` and `DBConnectionManager.Instance` are gone. Both are ordinary container
registrations now, which means **a scheduler is only visible in the repository belonging to the container
that built it**:

```diff
- var scheduler = SchedulerRepository.Instance.Lookup("reporting");
+ var scheduler = serviceProvider.GetRequiredService<ISchedulerRepository>().Lookup("reporting");
```

```diff
- DBConnectionManager.Instance.AddConnectionProvider("default", myProvider);
+ serviceProvider.GetRequiredService<IDbConnectionManager>().AddConnectionProvider("default", myProvider);
```

The observable consequence is that schedulers built different ways no longer find each other. Given a
scheduler registered with `AddQuartz` and another created by `StdSchedulerFactory` in the same process:

* `ISchedulerFactory.GetAllSchedulers()` on either one lists only its own schedulers.
* `ISchedulerFactory.GetScheduler(name)` returns `null` for the other one's name.
* `ISchedulerRepository.Lookup(name)` likewise sees only its own container's schedulers.

If you were relying on that reach — typically to find a scheduler from code that had no reference to the
factory that created it — register the scheduler with `AddQuartz` and inject `IScheduler`,
`ISchedulerFactory` or `ISchedulerRepository` instead. If you genuinely need one repository across
several entry points, register your own instance before calling `AddQuartz`; every Quartz registration
is `TryAdd`, so yours wins:

```csharp
var repository = new SchedulerRepository();
services.AddSingleton<ISchedulerRepository>(repository);
services.AddQuartz(/* ... */);
```

`StdSchedulerFactory.GetSchedulerRepository()` is still an override point, but it now returns the
repository of the factory's own container. `StdSchedulerFactory.GetDbConnectionManager()` was removed; it
had no callers.

## Package Changes

`Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting`, and `Quartz.Serialization.SystemTextJson` have been merged into the main `Quartz` package. You can remove these package references from your project:

```diff
- <PackageReference Include="Quartz.Extensions.DependencyInjection" Version="3.*" />
- <PackageReference Include="Quartz.Extensions.Hosting" Version="3.*" />
- <PackageReference Include="Quartz.Serialization.SystemTextJson" Version="3.*" />
+ <PackageReference Include="Quartz" Version="4.*" />
```

If you use Newtonsoft.Json serialization, reference `Quartz.Serialization.Newtonsoft` instead of the old `Quartz.Serialization.Json`.

Configuration that names a type from one of the merged assemblies as a string keeps working: a name that fails to
resolve is retried against `Quartz`, with a warning naming both spellings.

## Database Schema Migration

Quartz 4.x requires the `MISFIRE_ORIG_FIRE_TIME` column in the `QRTZ_TRIGGERS` table. This column stores the original scheduled fire time before misfire handling changes it.

::: warning
Always run migration scripts in a test environment against a copy of your production database first.
:::

Apply the migration script from [database/schema_30_to_40_upgrade.sql](https://github.com/quartznet/quartznet/blob/main/database/schema_30_to_40_upgrade.sql). The script includes existence checks, so it is safe to run even if you already have the column (it was added as optional in Quartz 3.17).

For SQL Server:

```sql
IF COL_LENGTH('QRTZ_TRIGGERS','MISFIRE_ORIG_FIRE_TIME') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [MISFIRE_ORIG_FIRE_TIME] bigint NULL;
END
```

See the migration script for PostgreSQL, MySQL, Oracle, SQLite, and Firebird equivalents. Replace `QRTZ_` with your configured table prefix if different.

Full table creation scripts for fresh installations are available in [database/tables/](https://github.com/quartznet/quartznet/tree/main/database/tables).

## Tasks Changed to ValueTask

In a majority of interfaces that previously returned or took a `Task` or `Task<T>` parameter, these have been changed to `ValueTask` or `ValueTask<T>`.

In most cases, all you will need to do is adjust the signature from `Task` to `ValueTask`.

For example, to migrate jobs:

```csharp
// 3.x
public async Task Execute(IJobExecutionContext context)

// 4.x
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
```

`Execute` also gained a `CancellationToken`. See [Jobs take a CancellationToken](#jobs-take-a-cancellationtoken)
below for why, and for what to do with it.

::: warning
The following operations should never be performed on a `ValueTask<TResult>` instance:

* Awaiting the instance multiple times.
* Calling `AsTask` multiple times.
* Using `.Result` or `.GetAwaiter().GetResult()` when the operation hasn't yet completed, or using them multiple times.
* Using more than one of these techniques to consume the instance.

If you need `Task` semantics (e.g., to await multiple times), call `.AsTask()` on the `ValueTask` once and work with the resulting `Task`.
:::

For more information on `ValueTask` please see [Microsoft docs](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1).

## SystemTime Replaced with TimeProvider

`SystemTime` has been removed. To provide a custom time source (e.g., for testing), inject a `TimeProvider` via configuration:

```csharp
// 3.x
SystemTime.UtcNow = () => new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

// 4.x — use TimeProvider
var scheduler = await QuartzSchedulerBuilder.Create()
    .Configure(q => q.UseTimeProvider(new FakeTimeProvider()))
    .BuildScheduler();
```

Under a host, the same call goes on the `AddQuartz` builder:

```csharp
services.AddQuartz(q => q.UseTimeProvider(new FakeTimeProvider()));
```

## Logging

LibLog has been replaced with `Microsoft.Extensions.Logging.Abstractions`.
Reconfigure logging using an `ILoggerFactory`. Example with a simple console logger:

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddSimpleConsole();
    });
LogProvider.SetLogProvider(loggerFactory);
```

See the Quartz.Examples project for examples on setting up [Serilog](https://serilog.net/) and Microsoft.Logging with Quartz.

Under a host, the `ILoggerFactory` the host already builds is the one Quartz uses — hand it to `LogProvider`
once the host is built:

```csharp
var host = Host.CreateDefaultBuilder(args)
    .ConfigureServices((hostContext, services) => services.AddQuartz(q => { /* ... */ }))
    .Build();

LogProvider.SetLogProvider(host.Services.GetRequiredService<ILoggerFactory>());
```

Further information on configuring Microsoft.Logging can be found [at Microsoft docs](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging).

## JSON Serialization

To configure JSON serialization to be used in job store, instead of the old `UseJsonSerializer` you should now use either `UseSystemTextJsonSerializer` or `UseNewtonsoftJsonSerializer`:

```csharp
// 3.x
q.UseJsonSerializer();

// 4.x — System.Text.Json (included in main package)
q.UseSystemTextJsonSerializer();

// 4.x — Newtonsoft.Json (requires Quartz.Serialization.Newtonsoft package)
q.UseNewtonsoftJsonSerializer();
```

Remove the old `Quartz.Serialization.Json` package reference.

### Custom trigger and calendar serializers are no longer static

The static registration methods have been removed, because they wrote into process-global dictionaries: two
schedulers in one process could not have different custom serializers, and registration order silently
decided which one won.

```csharp
// 3.x
SystemTextJsonObjectSerializer.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
NewtonsoftJsonObjectSerializer.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());

// 4.x — register through the store builder; what the callback registers belongs to that scheduler alone
q.UsePersistentStore(store => store.UseSystemTextJsonSerializer(json =>
{
    json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
    json.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());
}));
```

If you construct a serializer yourself, pass it a registry instead:

```csharp
var registry = new SystemTextJsonSerializerRegistry()
    .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());

var serializer = new SystemTextJsonObjectSerializer(registry);
```

The registries start out knowing every built-in trigger and calendar type, so a custom registration adds to
that set. The parameterless serializer constructors still exist and use the built-ins only.

One consequence worth checking: the HTTP API, the dashboard and `Quartz.HttpClient` also serialize triggers,
and none of them belongs to a single scheduler, so they no longer inherit a scheduler's custom serializers
for free. They read a container-wide registry — register it as a singleton to make a custom serializer
visible there:

```csharp
services.AddSingleton(new SystemTextJsonSerializerRegistry()
    .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer()));
```

See [Serialization (System.Text.Json)](packages/system-text-json) for the full picture.

### Newtonsoft types moved out of the core namespaces

The `Quartz.Serialization.Newtonsoft` package used to put types in namespaces that read as if they came from the
core package, and one of its types collided outright: both packages had a `Quartz.JsonConfigurationExtensions`,
which made `UseNewtonsoftJsonSerializer` ambiguous when both were referenced.

| 3.x | 4.x |
|---|---|
| `Quartz.JsonConfigurationExtensions` (Newtonsoft) | `Quartz.NewtonsoftJsonConfigurationExtensions` |
| `Quartz.Triggers.ITriggerSerializer`, `TriggerSerializer<T>`, the built-in trigger serializers | `Quartz.Serialization.Newtonsoft.Triggers.*` |
| `Quartz.Converters.NameValueCollectionConverter` | `Quartz.Serialization.Newtonsoft.NameValueCollectionConverter` |

`UseNewtonsoftJsonSerializer` itself is unchanged — only the `using` on a file that names one of these types.
`AddCalendarSerializer<TCalendar>` is now constrained to `ICalendar`, matching the trigger side; a call that
passed something else was never going to work at runtime.

## Sealed and Internalized Types

Many types have been sealed and/or internalized to minimize the API surface that needs to be maintained. If you were extending a type that is now sealed or internal, file an issue to request it be reopened.

## AbstractTrigger Property Removals

The following properties have been removed from `AbstractTrigger` as they are redundant with the `Key` and `JobKey` properties:

| Removed Property | Replacement |
|-----------------|-------------|
| `Name` | `Key.Name` |
| `GroupName` | `Key.Group` |
| `JobName` | `JobKey.Name` |
| `JobGroup` | `JobKey.Group` |
| `FullName` | `Key.ToString()` |

## JobKey and TriggerKey Null Validation

`JobKey` and `TriggerKey` now throw `ArgumentNullException` when you specify `null` for `name` or `group`. Triggers can no longer be constructed with a null group name. If your code was relying on null group names, switch to an explicit group name.

## DirtyFlagMap Changes

The `Get(TKey key)` method has been removed. Use the indexer or `TryGetValue` instead:

```csharp
// 3.x
var value = map.Get("key");

// 4.x
var value = map["key"];
// or
if (map.TryGetValue("key", out var value)) { ... }
```

The following properties are now explicit interface implementations and cannot be accessed directly on `DirtyFlagMap` instances: `IsReadOnly`, `IsFixedSize`, `SyncRoot`, `IsSynchronized`.

## Listener API Changes

`IListenerManager.GetJobListeners()` and `GetTriggerListeners()` now return arrays instead of `IReadOnlyCollection<T>` for improved performance and reduced allocations.

`IListenerManager.GetSchedulerListeners()` returns an array, like its job and trigger counterparts.

`GetJobListenerMatchers()` and `GetTriggerListenerMatchers()` return arrays too, and still return `null` for a
listener that is not registered.

Two members were renamed:

```diff
- ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default);
+ ValueTask SchedulerShuttingDown(CancellationToken cancellationToken = default);

- ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default);
+ ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default);
```

The broadcast listeners line up with each other: `BroadcastSchedulerListener.GetListeners()` is a `Listeners`
property, matching `BroadcastJobListener` and `BroadcastTriggerListener`, and all three constructors take an
`IReadOnlyCollection<T>`.

An `IJobStore` that implements `IJobListener` no longer automatically receives all events. Register it explicitly as a job listener using `ListenerManager`:

```csharp
scheduler.ListenerManager.AddJobListener(myJobStoreListener);
```

## Scheduler Configuration Validation

* `IdleWaitTime` values less than or equal to zero are no longer silently replaced with a 30-second default — they now throw.
* Negative values for `IdleWaitTime` or `BatchTimeWindow` are rejected.
* `MaxBatchSize` values less than or equal to zero are rejected.

## Cron Parser Enhancements

The cron expression parser now supports additional syntax:

* `L` and `LW` combinations in day-of-month expressions (e.g., `LW` for last weekday of the month)
* `LW-<OFFSET>` for offset from the last weekday (e.g., `LW-2` for two days before the last weekday). If the calculated day crosses a month boundary, it resets to the 1st.
* Day-of-month and day-of-week can now be specified together in the same expression
* `H` (hash) tokens for [load distribution](tutorial/crontrigger#h-hash-for-load-distribution) across triggers

## New Features

* **[RecurrenceTrigger (RRULE)](tutorial/recurrencetrigger.md)** — schedule jobs using RFC 5545 recurrence rules for complex patterns like "every 2nd Monday of the month" or "last weekday of March each year"
* **H (hash) token in cron expressions** — deterministic load distribution across triggers using the trigger identity as seed
* **HTTP API** — optional REST API for managing the scheduler remotely (see [HTTP API](packages/http-api.md))

## Batched Misfire Recovery

Misfire recovery now handles a whole batch of misfired triggers with a handful of statements instead of a
few per trigger. Nothing needs configuring — the read side is a single set-based query, and the write side
uses ADO.NET batching automatically on providers that support it (`DbConnection.CanCreateBatch`), falling
back to individual statements on those that do not.

This matters if you implement `IDriverDelegate` yourself, which now has two more members:

| Member | Purpose |
|--------|---------|
| `SelectMisfiredTriggersToRecover` | Reads a whole misfire batch as populated triggers in one round-trip |
| `UpdateMisfiredTriggers` | Applies a batch of misfire updates, batching the statements where supported |

If you subclass `StdAdoDelegate` you get both for free. A driver delegate for a database with its own
row-limiting syntax should also override `GetSelectMisfiredTriggersToRecoverSql`, alongside the existing
`GetSelectNextMisfiredTriggersInStateToAcquireSql`.

One behavioral note: `ITriggerListener.TriggerMisfired` is now raised for every trigger in a batch before
any of that batch's database updates are written, where previously the notification and the update were
interleaved per trigger. Everything still happens inside the same transaction and under the same lock, so
what other nodes observe is unchanged.

## Jobs take a CancellationToken

`IJob.Execute` now takes the cancellation token as a parameter alongside the context:

```diff
- public async ValueTask Execute(IJobExecutionContext context)
+ public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
```

It is the *same* token as `IJobExecutionContext.CancellationToken`, which still works — so a job body that already
reads the token off the context needs no further change. The parameter exists because a token on a context property
is easy to never notice, and a job that never notices it cannot be interrupted by `IScheduler.Interrupt` and will
hold up shutdown until it finishes on its own.

The practical benefit is that the compiler now helps. With the token as a parameter, the built-in `CA2016` analyzer
flags every `await` inside a job that fails to pass it on:

```diff
  public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
  {
-     await httpClient.GetAsync(url);              // CA2016: forward the cancellationToken parameter
+     await httpClient.GetAsync(url, cancellationToken);
  }
```

When adding this to Quartz's own jobs it found nine places that were silently ignoring interruption, including the
sample that exists to demonstrate interruption.

## The job factory hands out a scope

`IJobFactory` is built around a `JobScope` rather than a bare `IJob`, and `NewJob` is now `CreateJob`:

```diff
- ValueTask<IJob> NewJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default);
- ValueTask ReturnJob(IJob job);
+ ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default);
+ ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default);
```

`JobScope` is a readonly struct holding the job plus an opaque `State` object. If your factory allocates something
in order to build a job — a DI scope, a connection, a tenant context — put it in `State` and you get it back in
`ReturnJob` instead of having to hide it inside the job instance:

```diff
- protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
- {
-     var scope = serviceProvider.CreateScope();
-     return new MyWrapperJob(scope, scope.ServiceProvider.GetRequiredService<MyJob>());
- }
+ protected override ValueTask<JobScope> CreateJobInstance(
+     TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
+ {
+     var scope = serviceProvider.CreateScope();
+     var job = ActivatorUtilities.CreateInstance<MyJob>(scope.ServiceProvider);
+     return new ValueTask<JobScope>(new JobScope(job, scope));
+ }
```

::: warning
Note that this example *activates* the job rather than resolving it from the scope. `SimpleJobFactory.ReturnJob`
disposes the job and then the state, so if you resolve the job from the scope — `GetRequiredService<MyJob>()` — the
scope disposes it too and your job's `Dispose` is called twice. Either activate it as above, or override `ReturnJob`
to skip the job when the container owns it, which is what `MicrosoftDependencyInjectionJobFactory` does.
:::

Keep `CreateJobInstance` non-`async` when its body is synchronous. An async state machine restores the caller's
execution context when its synchronous part returns, which would discard any `AsyncLocal` you set while building the
job — including anything `ConfigureScope` establishes for the job to read.

Because of that, **`IJobWrapper` has been removed** and `MicrosoftDependencyInjectionJobFactory` no longer wraps
your job. `IJobExecutionContext.JobInstance` and every listener now see the type you actually wrote.

`PropertySettingJobFactory.InstantiateJob` — the hook derived factories override — was replaced by the asynchronous
`CreateJobInstance` shown above. The old hook was synchronous even after `NewJob` became asynchronous, so a factory
that needed to do real work had to override `NewJob` outright and reimplement the property setting.

`SimpleJobFactory`'s `protected static Dispose(object?)` helper is `DisposeIfDisposable(object?, CancellationToken)`,
which is what it has always done: it disposes the argument only when the argument is disposable.

### The factory is set where the scheduler is built

`IScheduler.JobFactory` was a setter-only property, and it is gone from `IScheduler`, `StdScheduler`,
`DelegatingScheduler` and `HttpScheduler`. A job factory is part of how a scheduler is built, not something to swap
underneath a running one — and on `HttpScheduler` the setter only ever threw, since the jobs run in another process.

```diff
- scheduler.JobFactory = new MyJobFactory();
+ services.AddQuartz(q => q.UseJobFactory(new MyJobFactory()));
```

`UseJobFactory(IJobFactory)` is new on both `IQuartzBuilder` and `QuartzSchedulerBuilder` — the generic
`UseJobFactory<T>()` overloads have always been there, but an already-constructed factory had nowhere to go:

```csharp
// standalone
var scheduler = await QuartzSchedulerBuilder.Create()
    .UseJobFactory(new MyJobFactory())
    .BuildScheduler();
```

If you build a `QuartzScheduler` by hand, its `JobFactory` property is still settable.

## Trigger fire times are properties

```diff
- DateTimeOffset? next = trigger.GetNextFireTimeUtc();
+ DateTimeOffset? next = trigger.NextFireTimeUtc;

- operableTrigger.SetNextFireTimeUtc(value);
+ operableTrigger.NextFireTimeUtc = value;

- if (trigger.GetMayFireAgain()) { … }
+ if (trigger.MayFireAgain) { … }
```

The three `Get` methods still work — they are `[Obsolete]` forwarders on both `ITrigger` and `AbstractTrigger` — so
this shows up as a warning rather than an error and you can fix it by deleting `Get` and `()`. The `Set` methods
have no stand-in, because a method and a property setter cannot share a name; those are a compile error.

One case is an error rather than a warning: a **custom trigger deriving from `AbstractTrigger`** overrides the
`MayFireAgain` property now, because that is the abstract member. `GetMayFireAgain` is a non-virtual forwarder,
so there is nothing left to override:

```diff
- public override bool GetMayFireAgain() => NextFireTimeUtc is not null;
+ public override bool MayFireAgain => NextFireTimeUtc is not null;
```

`CronTriggerImpl.CronExpression` also gained a getter — it was setter-only — and is typed `CronExpression?`,
which is what it always was underneath.

## The thread pool is asynchronous

Only relevant if you implement `IThreadPool` yourself:

```diff
- bool RunInThread(Func<Task> runnable);
- int BlockForAvailableThreads();
- void Initialize();
- void Shutdown(bool waitForJobsToComplete = true);
- string InstanceId { set; }
- string InstanceName { set; }
+ ValueTask<bool> TryRun(Func<Task> action, CancellationToken cancellationToken = default);
+ ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default);
+ ValueTask Initialize(CancellationToken cancellationToken = default);
+ ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default);
```

The two renamed methods used to block the calling thread on a semaphore, and the caller is the scheduler's own
asynchronous loop, so waiting for pool capacity tied up a thread. Use `WaitAsync` in your implementation.

`InstanceId` and `InstanceName` are gone rather than moved: Quartz set them and nothing ever read them. If your
pool wants the scheduler's identity, take `IOptions<QuartzSchedulerOptions>` from the container.

`TaskSchedulingThreadPool.ThreadCount` was removed as well; it read and wrote `MaxConcurrency`, so use that
directly. **The `quartz.threadPool.threadCount` configuration key is unaffected** and still sets `MaxConcurrency`.

## Quartz.Spi and Quartz.Simpl were renamed

`Quartz.Spi` is now `Quartz.Extensibility`, and `Quartz.Simpl` merged into the existing `Quartz.Impl`. Both old
names were transliterations of `org.quartz.spi` and `org.quartz.simpl`. For source code this is a find-and-replace
over `using` directives that the compiler will walk you through.

Configuration is the part that would not have failed loudly, because it names types by string:

```diff
- quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
+ quartz.jobStore.type = Quartz.Impl.RAMJobStore, Quartz
```

**Existing configuration keeps working.** A type name naming a pre-4.0 namespace that no longer resolves is retried
under the new one, and a warning is logged naming both spellings. Treat that as a grace period rather than a promise.

The same fallback covers the assemblies that were merged into the core package, and composes with the namespace
rename, so a string naming both still resolves:

```diff
- quartz.serializer.type = Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson
+ quartz.serializer.type = Quartz.Impl.SystemTextJsonObjectSerializer, Quartz
```

## Names that were normalized

Renames only — the behavior behind each is unchanged, and a rename that also changes a configuration key is
called out.

| 3.x | 4.x |
|---|---|
| `QuartzScheduler.NumJobsExecuted` | `NumberOfJobsExecuted` |
| `QuartzScheduler.JobStoreClass`, `.ThreadPoolClass` | `JobStoreType`, `ThreadPoolType` (they return a `Type`) |
| `JobStoreSupport.UseDBLocks`, `.SelectWithLockSQL` | `UseDbLocks`, `SelectWithLockSql` |
| `DBSemaphore.SQL`, `.InsertSQL`, `.ExecuteSQL` | `Sql`, `InsertSql` (both readable now), `ExecuteSql` |
| `Quartz.Util.DBConnectionManager` | `DbConnectionManager` |
| `DbMetadata.Init()` | `Initialize()` |
| `AdoConstants.ColumnMifireInstruction` | `ColumnMisfireInstruction` (a typo; the column name is unchanged) |
| `SchedulerConstants.FailedJobOriginalTriggerFiretime`, `…ScheduledFiretime` | `…TriggerFireTime`, `…ScheduledFireTime` (the string values are unchanged) |
| `XMLSchedulingDataProcessor.OverWriteExistingData`, `SchedulingOptions.OverWriteExistingData` | `OverwriteExistingData`. The configuration key is spelled `Quartz:Scheduling:OverwriteExistingData` now; keys are matched case-insensitively, so an existing file keeps binding, but code assigning the property has to change |
| `XMLSchedulingDataProcessor.PrepForProcessing`, `.BuildTriggersByFQJobNameMap` | `PrepareForProcessing`, `BuildTriggersByFullyQualifiedJobNameMap` |
| `RedisSemaphore.LockTtlMilliseconds`, `.LockRetryIntervalMilliseconds` | `LockTimeToLive`, `LockRetryInterval`, both `TimeSpan` — **also the config keys `lockTtlMilliseconds` → `lockTimeToLive` and `lockRetryIntervalMilliseconds` → `lockRetryInterval`** |

## Other Breaking Changes

| Change | Details |
|--------|---------|
| `SimpleTriggerImpl` `endUtc` no longer nullable | The constructor argument is now required |
| `QuartzScheduler` ctor change | No longer takes `idleWaitTime`; use `QuartzSchedulerResources.IdleWaitTime` |
| `JobType` introduced | Stores job type info without requiring an actual `Type` instance |
| `RecoveringTriggerKey` behavior | `IJobExecutionContext.RecoveringTriggerKey` now returns `null` when not recovering instead of throwing |
| `DictionaryExtensions` removed | `Quartz.Util.DictionaryExtensions` type was removed |
| `JobStoreSupport` connection methods | `GetNonManagedTXConnection` and `GetConnection` now return `ValueTask<ConnectionAndTransactionHolder>` |
| `JobStoreSupport.UseProperties` `string` setter removed | The `bool` `AdoJobStoreOptions.UseProperties` option and the read-only `CanUseProperties` remain; the property bridge parses the key |
| Protected `JobStoreSupport` / `StdAdoDelegate` members take a `CancellationToken` | Overrides have to add the parameter; callers do not |
| `ConnectionAndTransactionHolder.Close`, `.Commit`, `.Rollback` take a `CancellationToken` | Same |
| `IJobConfigurator` members return `IJobConfigurator` | `JobBuilder` implements them explicitly and keeps its own `JobBuilder`-returning members, so `JobBuilder.Create()…` chains are unaffected |
| `IJobConfigurator` / `JobBuilder` gained `UsingJobData(string, decimal)` | And `UsingJobData(string, string?)` accepts null |
| `IDirectoryScanListener` is asynchronous | `FilesUpdatedOrAdded` and `FilesDeleted` return `ValueTask` and take a `CancellationToken` |
| `LoggingJobHistoryPlugin.Name`, `LoggingTriggerHistoryPlugin.Name` are get-only | The name is handed to a plugin by `Initialize`; writing it afterwards did nothing |
| `TimeSpanParseRuleAttribute` is public | It says how a bare number in configuration is read as a `TimeSpan`, which a component configured by the same keys needs to be able to say |
| `TimeZoneUtil.CustomResolver` is a property | It was a public mutable field |
| Setter-only members gained getters | `DbMetadata.DbBinaryTypeName` (now nullable) and `.ParameterDbTypePropertyName`, `HttpSchedulerProxyFactory.Address` |
