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

### Listing indexes (optional)

The same script adds two indexes that the [job and trigger listings](#job-store-listings-became-queries)
benefit from:

| Index | Table and columns |
|---|---|
| `IDX_QRTZ_J_G_N` | `QRTZ_JOB_DETAILS(SCHED_NAME, JOB_GROUP, JOB_NAME)` |
| `IDX_QRTZ_T_G_N` | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME)` |

Listings page with `ORDER BY JOB_GROUP, JOB_NAME` and `ORDER BY TRIGGER_GROUP, TRIGGER_NAME`, and the primary
keys are name-before-group, so no existing index serves those ordered scans. **These are optional** — the
queries work without them, but each page becomes a scan plus a sort. Add them if you list jobs or triggers
from a large schema. They are in the fresh-install scripts for every dialect already.

PostgreSQL users should also take the corrected index definitions from
[database/tables/tables_postgres.sql](https://github.com/quartznet/quartznet/blob/main/database/tables/tables_postgres.sql).
Several indexes in that script omitted `SCHED_NAME`, which is the leading column of every predicate Quartz
issues, so they could not serve a single-scheduler lookup.

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

The ones most likely to be visible in existing code:

**`QuartzScheduler` and `QuartzSchedulerResources` are internal.** `QuartzScheduler` is the implementation
behind `IScheduler` and was only reachable through `StdScheduler`'s constructor, which is internal too now that
the container builds the scheduler. Resolve `IScheduler` or `ISchedulerFactory`; the settings that used to live
on `QuartzSchedulerResources` are `QuartzSchedulerOptions`.

**`StdAdoConstants` and `IAdoUtil` are internal, and constants are no longer inherited.** `AdoConstants` stays
public — table, column and state names are a real contract for delegate authors — but it is a `static class`
now, and `JobStoreSupport`, `StdAdoDelegate` and `DBSemaphore` no longer derive from it or from
`StdAdoConstants`:

```diff
  public class MyDelegate : StdAdoDelegate
  {
-     private string CountRows() => $"SELECT COUNT(*) FROM {TablePrefixSubst}{TableTriggers}";
+     private string CountRows() => $"SELECT COUNT(*) FROM {{0}}{AdoConstants.TableTriggers}";
  }
```

The `Sql*` statement templates on `StdAdoConstants` are not visible at all any more — the exact text of a
statement is not a contract. Build the statement your dialect needs, or override the `GetSelect*Sql` hooks,
which are unchanged.

`DBSemaphore.AdoUtil` is `private protected` for the same reason, so a semaphore written outside Quartz no
longer sees it — derive from `DBSemaphore` and use `IDbProvider`, or implement `ISemaphore` directly.

**Three trigger persistence delegates became public**, so a custom delegate list can name all five built-ins:
`CronTriggerPersistenceDelegate`, `SimpleTriggerPersistenceDelegate` and
`DailyTimeIntervalTriggerPersistenceDelegate` join `CalendarIntervalTriggerPersistenceDelegate` and
`RecurrenceTriggerPersistenceDelegate`. All five are `sealed`; write your own against
`SimplePropertiesTriggerPersistenceDelegateSupport` or `ITriggerPersistenceDelegate`.

**`SchedulerConstants` and `MisfireInstruction` are static classes** rather than structs, and `QuartzOptions`,
`SchedulingOptions` and `QuartzHostedServiceOptions` are `sealed`. Referring to the constants is unchanged;
only `new MisfireInstruction()`, which never meant anything, stops compiling.

**Quartz.Dashboard's Blazor components are not API.** They are `public` because the Razor compiler makes them
so, but they are UI and are excluded from the dashboard's public-API baseline. Build against
`QuartzDashboardOptions`, `AddQuartzDashboard` and the model types.

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
* **Paged, projected job store queries** — list and count jobs, triggers, groups and calendars a page at a time, with the metadata a listing needs already in the row (see [Job store listings became queries](#job-store-listings-became-queries))
* **Job data by property name** — bind job data to the job property it is meant for instead of spelling its key (see [Job data can name the property](#job-data-can-name-the-property))
* **`TriggerState.Executing`** — tell whether a trigger's job is running, across the whole cluster (see [Executing is a trigger state](#executing-is-a-trigger-state))

## Job data can name the property

`UsingJobData` has an overload that takes the job property rather than its key:

```diff
  q.AddJob<ExampleJob>(jobKey, j => j
-     .UsingJobData(nameof(ExampleJob.InjectedString), "Hello")
-     .UsingJobData(nameof(ExampleJob.InjectedBool), true)
+     .UsingJobData(x => x.InjectedString, "Hello")
+     .UsingJobData(x => x.InjectedBool, true)
  );
```

The key is the property's name and the value has to be of the property's type, so a value that would have
been silently coerced — an `int` written to a `string` property, say — no longer compiles, and neither does
a property of an unrelated job.

Everything the compiler cannot rule out is rejected where the job data is written, by asking the job
factory's own lookup whether the key this property's name becomes leads back to this same property:

- a property with no public setter, or a nested path;
- one reached by casting the lambda parameter to another job;
- one the factory cannot find — a name starting with a lowercase letter (keys are looked up upper-cased),
  or a property that implements an interface explicitly, which is not public on the job class;
- one whose name resolves to a *different* property of another type, which is what a `new` member that
  hides a base property does;
- a value that will not convert to the property's type, or that would lose information doing so — a
  `double` rounded into an `int`, or saturated into a `float`;
- `null` for a property that cannot hold one. Type inference widens `TValue` to the nullable form, so
  `int? retries = …; UsingJobData(j => j.RetryCount, retries)` compiles and is rejected here rather than
  quietly becoming `0` when the job runs.

Enums are stored by name. Nothing is instantiated: the expression is read, never run.

Existing string-keyed `UsingJobData` calls are unaffected.

### The builders carry the job type

Inferring `x` needs the builder to know the job type, so the builders and the configurator interfaces are
generic in it. `JobBuilder` and `TriggerBuilder` are now static classes holding the `Create` methods, and
the builder itself is `JobBuilder<TJob>` / `TriggerBuilder<TJob>`:

| 4.0 preview | 4.0 |
|---|---|
| `JobBuilder` | `JobBuilder<TJob>`, from `JobBuilder.Create<TJob>()` — `JobBuilder.Create()` gives `JobBuilder<IJob>` |
| `TriggerBuilder` | `TriggerBuilder<TJob>`, from `TriggerBuilder.Create<TJob>()` — `TriggerBuilder.Create()` gives `TriggerBuilder<IJob>` |
| `IJobConfigurator` | `IJobConfigurator<TJob>` |
| `ITriggerConfigurator` | `ITriggerConfigurator<TJob>` |
| `IJobDetail.GetJobBuilder()` | returns `JobBuilder<IJob>` |
| `ITrigger.GetTriggerBuilder()` | returns `TriggerBuilder<IJob>` |

Chained code is unaffected — `JobBuilder.Create<MyJob>().WithIdentity("x").Build()` reads the same, and so
do the `AddJob<T>` / `ScheduleJob<T>` lambdas, whose parameter type is now inferred as the generic
configurator. What breaks is naming the builder as a type:

```diff
- TriggerBuilder builder = trigger.GetTriggerBuilder();
+ var builder = trigger.GetTriggerBuilder();
```

The configurator interfaces are **invariant** in `TJob`, so a configuration delegate shared across job
types no longer type-checks. `TJob` appears both as an input (`Expression<Func<TJob, TValue>>`) and in the
returned interface, so no variance annotation can recover it — make the helper generic instead:

```diff
- Action<ITriggerConfigurator> common = t => t.StartNow().WithSimpleSchedule();
- q.ScheduleJob<JobA>(common);
- q.ScheduleJob<JobB>(common);
+ static void Common<TJob>(ITriggerConfigurator<TJob> t) where TJob : IJob => t.StartNow().WithSimpleSchedule();
+ q.ScheduleJob<JobA>(Common);
+ q.ScheduleJob<JobB>(Common);
```

`AddTrigger` gained an `AddTrigger<TJob>` overload, because a trigger added on its own has no job type to
infer from otherwise. The internal `TriggerConfigurator` is gone: `TriggerBuilder<TJob>` implements
`ITriggerConfigurator<TJob>` itself, which also gets `WithExecutionGroup` and `WithPreferredNode` onto the
DI configurator for the first time.

That deletion also changed which clock a DI-registered trigger is born with. `TriggerConfigurator` always
built with `TimeProvider.System`; `AddTrigger` and `ScheduleJob` now use the container's registered
`TimeProvider`. If you register a non-system one — a `FakeTimeProvider` in a test, say — a trigger without
an explicit `StartAt` now starts at that provider's now rather than at wall clock, which is almost
certainly what you wanted, but it does change when such triggers first fire.

Three runtime checks come with the type parameter, all only on a builder whose `TJob` is not `IJob`:

* `JobBuilder.Create<TJob>().OfType(type)` and `OfType<T>()` throw `ArgumentException` **at the `OfType`
  call** when the type is not a `TJob`. If you resolve a job type at runtime — from configuration, or a
  decorator type — build it with `JobBuilder.Create(type)` rather than the generic overload.
* `JobBuilder.Create<TJob>().OfType(typeName)` throws `InvalidOperationException` on `Build()` instead,
  because a type named by string is only known once it resolves.
* `TriggerBuilder.Create<TJob>().ForJob(jobDetail)` throws `ArgumentException` when the detail is not for a
  `TJob`. `ForJob(JobKey)` carries no type, and a detail whose type name does not resolve in this process
  cannot be checked either — both are accepted.

## Trimming annotations

`ScheduleJob<T>`, `AddTrigger<TJob>` and `TriggerBuilder.Create<TJob>()` gained
`[DynamicallyAccessedMembers]` on their type parameter, matching `AddJob<T>` and `JobBuilder.Create<T>()` —
they build job details or bind job data now, so the job type's members have to survive trimming. If you
wrap them in your own generic method and build with the trim analyzer on, the forwarding type parameter
needs the same annotation:

```diff
- public static void Register<TJob>(IQuartzBuilder q) where TJob : IJob
+ public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TJob>(IQuartzBuilder q) where TJob : IJob
      => q.ScheduleJob<TJob>(t => t.StartNow());
```

## Executing is a trigger state

There was no way to ask whether a trigger's job is running right now.
`IScheduler.GetCurrentlyExecutingJobs` only ever sees the node it is called on, so a process that schedules
and observes triggers but does not execute them — a dashboard, an admin UI, a separate control
application — could not answer the question at all.

`TriggerState` now has an `Executing` member, reported by both `IScheduler.GetTriggerState` and trigger
listings. With a persistent job store it is visible from every node, because it is established from the
fired-triggers table rather than from process-local state.

### What changed in what you get back

A trigger with an execution in flight previously reported `Normal`, `Complete`, or `Blocked` depending on
its schedule; it now reports `Executing`. States are resolved in this order:

```
None > Error > Paused > Executing > Blocked > Complete > Normal
```

So a trigger that is paused, or in the error state, still reports that even while its job runs — those are
the facts an operator has to act on. Two consequences worth calling out:

* `Blocked` now means a **different** trigger of the same `[DisallowConcurrentExecution]` job is running,
  so this one cannot fire. The trigger that is actually running reports `Executing`. Previously both
  reported `Blocked` and nothing could tell them apart.
* A trigger with no fire times left whose final execution is still running reports `Executing` rather than
  `Complete`. In 3.x this case reported `Blocked`, which was a stand-in for a state that did not exist yet.

Note that executing is not exclusive with being scheduled: a trigger whose job allows concurrent execution
can be running several jobs and still be due to fire again. It reports `Executing` until the last of them
finishes.

### What to check in your own code

* **Health checks and guards of the form `if (state == TriggerState.Normal)`** will now see `Executing` for
  a trigger that is simply busy. Treat `Executing` as healthy.
* **Watchdogs of the form `if (state != TriggerState.Normal) await ResumeTrigger(key)`** deserve a closer
  look. `ResumeTrigger` applies the trigger's misfire policy to any trigger whose next fire time has
  passed, regardless of the state it is currently in — and for a long-running job on a short interval, the
  next fire time is in the past for the whole execution. Such a watchdog can therefore alter the schedule
  of a perfectly healthy trigger. This is not new in 4.x, but `Executing` routes more triggers into it, so
  gate the watchdog on the states you actually mean to repair (`Error`, `Paused`).
* **Alerting that treats `Blocked` as "a job is running"** should move to `Executing`.

### Filtering a listing by state

`TriggerQuery.State` accepts `Executing` like any other state. Because the filter and the reported state
are derived together, a listing filtered by `Normal` no longer returns triggers it would then report as
`Executing`.

### A note on stale executions

Executing is established from the fired-triggers table, which is the only durable record that a job is
running. If a node dies mid-execution, its rows stay behind until another node's cluster recovery clears
them, so during that window a trigger reports `Executing` although nothing is running — and, because the
filter and the reported state agree by construction, it is also absent from a listing filtered by
`Normal`. The same window already existed for `Blocked`; it is now visible for more states.

How long that lasts depends on the job. For an ordinary job the rows are cleared as soon as another node
detects the failure, roughly `clusterCheckinInterval` + `clusterCheckinMisfireThreshold`. For a
`[DisallowConcurrentExecution]` job, cluster recovery deliberately *preserves* the executing rows on first
detection — the node may still be alive and running the job, and reviving it elsewhere would break the
concurrency guarantee — and only cleans them up on a later pass, once the elapsed time exceeds
`2 × clusterCheckinInterval + clusterCheckinMisfireThreshold`. So with a 15-second interval and a
60-second threshold, expect up to about 90 seconds plus one more check-in cycle before such a trigger
stops reporting `Executing`.

### If you implement IDriverDelegate

`IsTriggerCurrentlyExecuting` was replaced by `SelectTriggerStateWithExecuting`, which returns the stored
state and whether an execution is in flight from a single statement, so reporting a trigger's state stays
one round trip. Subclasses of `StdAdoDelegate` get it for free. There is no schema change.

Note that `GetTriggerState` now calls this method instead of `SelectTriggerState`. If you override
`SelectTriggerState` to handle a vendor quirk or a legacy state value, override
`SelectTriggerStateWithExecuting` as well — the compiler cannot tell you, because the old method is still
on the interface and still used elsewhere.

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
row-limiting syntax should also override `GetSelectMisfiredTriggersToRecoverSql`.

One behavioral note: `ITriggerListener.TriggerMisfired` is now raised for every trigger in a batch before
any of that batch's database updates are written, where previously the notification and the update were
interleaved per trigger. Everything still happens inside the same transaction and under the same lock, so
what other nodes observe is unchanged.

## Job store listings became queries

Listing jobs or triggers meant reading every key and then spending one round trip per key on anything more
than the key — the job's type, the trigger's state or next fire time. Nothing could ask for a page, and
nothing could ask for a count without materializing what it counted.

Those members are replaced by query members that take a query record and return one page of projected
results. **Existing code keeps compiling**: every removed `IScheduler` member comes back as an extension
method in `SchedulerQueryExtensions`, with the same name and signature.

### What replaced what

`IScheduler` — the left column still works, now as an extension method:

| Removed from `IScheduler` | Query member |
|---|---|
| `GetJobKeys(matcher)` | `QueryJobs(new JobQuery { Group = matcher })` |
| `GetTriggerKeys(matcher)` | `QueryTriggers(new TriggerQuery { Group = matcher })` |
| `GetJobGroupNames()` | `QueryJobGroups(new JobGroupQuery())` |
| `GetTriggerGroupNames()` | `QueryTriggerGroups(new TriggerGroupQuery())` |
| `GetPausedTriggerGroups()` | `QueryTriggerGroups(new TriggerGroupQuery { Paused = true })` |
| `GetCalendarNames()` | `QueryCalendarNames(new CalendarQuery())` |
| `IsJobGroupPaused(group)` | `QueryJobGroups(new JobGroupQuery { Paused = true })` |
| `IsTriggerGroupPaused(group)` | `QueryTriggerGroups(new TriggerGroupQuery { Paused = true })` |

`IJobStore` loses the same members plus the counting and existence ones, and has no extension methods to
soften it — if you implement a job store, you implement the query members:

| Removed from `IJobStore` | Use instead |
|---|---|
| `GetJobKeys`, `GetTriggerKeys` | `QueryJobs`, `QueryTriggers` |
| `GetJobGroupNames`, `GetTriggerGroupNames`, `GetPausedTriggerGroups` | `QueryJobGroups`, `QueryTriggerGroups` |
| `GetCalendarNames` | `QueryCalendarNames` |
| `IsJobGroupPaused`, `IsTriggerGroupPaused` | the matching `Query*Groups` with `Paused = true` |
| `GetNumberOfJobs`, `GetNumberOfTriggers`, `GetNumberOfCalendars` | the matching query with `Take = 0, IncludeTotalCount = true` |
| `CalendarExists(name)` | `GetCalendar(name)` returning non-null |

Two members are new on both interfaces: **`GetJobDetails(jobKeys)`** and **`GetTriggers(triggerKeys)`**
retrieve many by key in one round trip. Keys that do not exist are simply absent, duplicates fold away, and
results come back in the order the keys were asked for.

### Paging and projection

Every query derives from `PagedQuery`, which carries `Skip`, `Take` (default `int.MaxValue` — a query that
sets neither returns everything) and `IncludeTotalCount`. The result is a `PagedResult<T>` with `Items`,
`HasMore` and a nullable `TotalCount`. `HasMore` is exact and costs nothing: stores read one item past
`Take` rather than running a second query.

Because the query types are records, walk a result by `with`-ing the next `Skip`:

```csharp
// Before
IReadOnlyCollection<JobKey> keys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
foreach (JobKey key in keys)
{
    IJobDetail? detail = await scheduler.GetJobDetail(key); // one round trip each
    Console.WriteLine($"{key} -> {detail?.JobType.Name}");
}
```

```csharp
// After — one round trip per page, and the type name is already there
JobQuery query = new() { Group = GroupMatcher<JobKey>.AnyGroup(), Take = 100 };
while (true)
{
    PagedResult<JobHeader> page = await scheduler.QueryJobs(query);
    foreach (JobHeader job in page.Items)
    {
        Console.WriteLine($"{job.Key} -> {job.JobTypeName}");
    }

    if (!page.HasMore)
    {
        break;
    }

    query = query with { Skip = query.Skip + page.Items.Count };
}
```

`JobHeader` is a job's metadata *without* its `JobDataMap`, so listing never loads or deserializes job data.
`TriggerHeader` carries the state, fire times, priority, calendar name and execution group that previously
cost an extra round trip per trigger each. When you do need the whole thing, follow a page with one bulk
fetch:

```csharp
List<IJobDetail> details = await scheduler.GetJobDetails(page.Items.ConvertAll(x => x.Key));
```

Counting is a query that reads no rows:

```csharp
// Before
int total = await jobStore.GetNumberOfTriggers();

// After
PagedResult<TriggerHeader> count = await scheduler.QueryTriggers(
    new TriggerQuery { Take = 0, IncludeTotalCount = true });
int total = count.TotalCount!.Value;
```

Unlike the old counting members, this counts what a filter selects rather than a whole table — how many
triggers are in the error state, for instance:

```csharp
PagedResult<TriggerHeader> failed = await scheduler.QueryTriggers(
    new TriggerQuery { State = TriggerState.Error, Take = 0, IncludeTotalCount = true });
Console.WriteLine($"{failed.TotalCount} triggers need attention");
```

`TriggerQuery` also filters on `Job`, `CalendarName` and `Group`; the filters combine with AND, and a null
`Group` matches every group.

### Behavior worth knowing

* **Ordering is group first, then name**, and every page uses the same ordering, so paging is consistent.
  `RAMJobStore` compares ordinal. The ADO job store sorts in the database, so both the group order and the
  order within a group follow the **server's collation** — for most collations that differs from ordinal only
  in case and accent handling, but it is the database's decision, not Quartz's. Sort the page yourself if you
  need one specific culture's ordering.
* **A null matcher now throws.** `scheduler.GetJobKeys(null)` and `GetTriggerKeys(null)` raise
  `ArgumentNullException` instead of silently narrowing the listing to the `DEFAULT` group.
* **The extension methods enumerate everything.** They preserve the old semantics — and the old cost. Use the
  query member with `Skip`/`Take` wherever the result can be large.
* **Job group pause state is not persisted by the ADO job store**, so `JobGroup.Paused` is always false
  there. This is what `IsJobGroupPaused` always did on that store; the query type just makes it visible.
* **Two indexes were added** to support the ordered scans — see [Database Schema Migration](#database-schema-migration).

### If you implement `IDriverDelegate`

Beyond the two batched-misfire members above, the query work adds, removes and consolidates a fair amount.
New members to implement:

| Member | Purpose |
|--------|---------|
| `SelectJobHeaders`, `SelectTriggerHeaders` | One page of projected job/trigger listing rows |
| `SelectJobGroups(conn, JobGroupQuery, ct)`, `SelectTriggerGroups(conn, TriggerGroupQuery, ct)` | One page of groups, with pause state |
| `SelectCalendarNames` | One page of calendar names |
| `SelectJobDetails`, `SelectTriggers` | Bulk fetch by key set |

Deleted, having had no caller: `SelectMisfiredTriggers`, both `HasMisfiredTriggersInState` overloads,
`SelectMisfiredTriggersInGroupInState`, `IsExistingTriggerGroup`, `SelectJobExecutionCount`,
`SelectTriggerForFireTime`, `SelectNumJobs`, `SelectNumTriggers`, `SelectNumCalendars`, `SelectCalendars`,
`SelectPausedTriggerGroups`, `SelectJobGroups(conn, ct)` and `DeleteAllPausedTriggerGroups`. The
`GetSelectNextMisfiredTriggersInStateToAcquireSql` hook went with them, so a dialect delegate that overrode
it should delete that override.

Consolidated into records rather than overload families:

| Was | Is |
|---|---|
| `SelectFiredTriggerRecords`, `SelectFiredTriggerRecordsByJob`, `SelectInstancesFiredTriggerRecords` | `SelectFiredTriggerRecords(conn, FiredTriggerQuery, ct)` |
| four `DeleteFiredTriggers` overloads | `DeleteFiredTriggers(conn, FiredTriggerQuery, ct)` |
| two `SelectTriggerToAcquire` overloads | `SelectTriggersToAcquire(conn, TriggerAcquisitionCriteria, ct)` |
| two `SelectJobForTrigger` overloads | one, with `bool loadJobType = true` |
| `DeletePausedTriggerGroup(conn, string, ct)` | the `GroupMatcher<TriggerKey>` overload |

`FiredTriggerQuery` carries an optional `Trigger`, `Job` and `InstanceName` combined with AND — all null
selects or deletes every fired trigger. `TriggerAcquisitionCriteria` carries `NoLaterThan`, `NoEarlierThan`,
`MaxCount`, `ExecutionLimits` and `LiveNodeCutoff`, and is the extension point for future acquisition
filtering: another way of narrowing what a node picks up is another optional property, not another overload.

Subclassing `StdAdoDelegate` gets you all of it. A database whose row-limiting syntax is not the ANSI
`OFFSET … FETCH NEXT` should override the paging seam — **`ApplyPaging(sql, takeLimited)`** appends the
clause and **`AddPagingParameters(cmd, skip, take, takeLimited)`** binds it. Override both together when your
clause names the two parameters in the other order, because providers that bind positionally take parameters
in the order the statement mentions them. `MySQLDelegate` and `SQLiteDelegate` do exactly this for
`LIMIT … OFFSET`.

Finally, `ITriggerPersistenceDelegate` gained a batch `LoadExtendedTriggerProperties` taking several trigger
keys. It is a **default interface method** that loops the single-key overload, so a third-party trigger
persistence delegate needs no change; override it only to turn a batch into one round trip.

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

There is no by-hand path left: `QuartzScheduler` is internal, so the job factory is always configured through
the builder or the container.

## Trigger fire times are properties

```diff
- DateTimeOffset? next = trigger.GetNextFireTimeUtc();
+ DateTimeOffset? next = trigger.NextFireTimeUtc;

- operableTrigger.SetNextFireTimeUtc(value);
+ operableTrigger.NextFireTimeUtc = value;

- if (trigger.GetMayFireAgain()) { … }
+ if (trigger.MayFireAgain) { … }
```

The three `Get` methods are gone — they spent a while as `[Obsolete]` forwarders on both `ITrigger` and
`AbstractTrigger` and have now been removed — so fix the call by deleting `Get` and `()`. The `Set` methods
likewise have no stand-in, because a method and a property setter cannot share a name.

A **custom trigger deriving from `AbstractTrigger`** overrides the `MayFireAgain` property now, because that
is the abstract member:

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

## The scheduler and the job store speak the same verbs

`IJobStore` had its own vocabulary — Store/Remove/Retrieve — for the operations `IScheduler` calls
Schedule/Add/Delete/Get, so the two halves of one operation had different names, and a stack trace had to be
translated on the way down. The store now uses the scheduler's words. If you implement a job store, rename;
if you only call `IScheduler`, nothing here affects you.

| `IJobStore` in 3.x | `IJobStore` in 4.x |
|---|---|
| `StoreJobAndTrigger(job, trigger)` | `ScheduleJob(job, trigger)` |
| `StoreJobsAndTriggers(triggersAndJobs, replace)` | `ScheduleJobs(triggersAndJobs, replace)` |
| `StoreJob(job, replaceExisting)` | `AddJob(job, replace)` |
| `StoreTrigger(trigger, replaceExisting)` | `AddTrigger(trigger, replace)` |
| `RemoveJob(key)`, `RemoveJobs(keys)` | `DeleteJob(key)`, `DeleteJobs(keys)` |
| `RemoveTrigger(key)`, `RemoveTriggers(keys)` | `DeleteTrigger(key)`, `DeleteTriggers(keys)` |
| `RetrieveJob(key)` | `GetJob(key)` |
| `RetrieveTrigger(key)` | `GetTrigger(key)` |
| `StoreCalendar(name, cal, replaceExisting, updateTriggers)` | `AddCalendar(name, cal, AddCalendarOptions?)` |
| `RemoveCalendar(name)` | `DeleteCalendar(name)` |
| `RetrieveCalendar(name)` | `GetCalendar(name)` |
| `ClearAllSchedulingData()` | `Clear()` |
| `AcquireNextTriggers(noLaterThan, maxCount, timeWindow, executionLimits)` | `AcquireNextTriggers(TriggerAcquisitionRequest)` |

The `bool` that decides whether an existing item is over-written is called `replace` on every member; it was
`replaceExisting` on some. The `protected` `JobStoreSupport` members that mirror these — the
`ConnectionAndTransactionHolder` overloads, and `AcquireNextTrigger` — were renamed with them.

The activity names in `Quartz.Diagnostics.OperationName.JobStore` follow the methods they name, so a trace
filter matching `"Quartz.JobStore.StoreJob"` now needs `"Quartz.JobStore.AddJob"`, and so on for every row
above.

### Acquisition takes a request record

```diff
- await store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow, executionLimits, ct);
+ await store.AcquireNextTriggers(new TriggerAcquisitionRequest
+ {
+     NoLaterThan = noLaterThan,
+     MaxCount = maxCount,
+     TimeWindow = timeWindow,
+     ExecutionLimits = executionLimits,
+ }, ct);
```

`TriggerAcquisitionRequest` lives in `Quartz.Extensibility`. Acquisition keeps growing dimensions — the
batching window, per-execution-group limits, node affinity — and each one used to be another parameter on the
hot path of every store. As a record, the next one is an added optional property a store can ignore. It is the
store-level counterpart of the delegate-level `TriggerAcquisitionCriteria`, which is unchanged. `TimeWindow`
rejects a negative value at construction, where `JobStoreSupport` used to throw from inside acquisition.

## Options records replace boolean parameters

`AddJob` and `AddCalendar` took bare booleans that say nothing at the call site about which is which, and
`AddJob` had a second overload only because its second boolean was added later. Both are one member now,
taking an optional record whose defaults are the conservative choice (`Replace = false`,
`StoreNonDurableWhileAwaitingScheduling = false`, `UpdateTriggers = false`).

| 3.x | 4.x |
|---|---|
| `AddJob(job, replace: false)` | `AddJob(job)` |
| `AddJob(job, replace: true)` | `AddJob(job, new AddJobOptions { Replace = true })` |
| `AddJob(job, true, true)` | `AddJob(job, new AddJobOptions { Replace = true, StoreNonDurableWhileAwaitingScheduling = true })` |
| `AddCalendar(name, cal, false, false)` | `AddCalendar(name, cal)` |
| `AddCalendar(name, cal, true, true)` | `AddCalendar(name, cal, new AddCalendarOptions { Replace = true, UpdateTriggers = true })` |

`AddJobOptions` and `AddCalendarOptions` are both in the `Quartz` namespace. `IJobStore.AddCalendar` takes
`AddCalendarOptions` too; `IJobStore.AddJob` keeps its single `bool replace`, because durability is a
scheduler-level rule the store never sees.

The DI-time builders — `q.AddJob<T>(…)` and `q.AddCalendar<T>(…)` on `IQuartzBuilder` — are unchanged.

## Overloads that differed only by a default

| 3.x | 4.x |
|---|---|
| `Shutdown()`, `Shutdown(bool waitForJobsToComplete)` | `Shutdown(bool waitForJobsToComplete = false, …)` |
| `TriggerJob(jobKey)`, `TriggerJob(jobKey, data)` | `TriggerJob(JobKey jobKey, JobDataMap? data = null, …)` |
| `Interrupt(string fireInstanceId)` | `InterruptFireInstance(string fireInstanceId)` |
| `GetMetaData()` | `GetMetadata()`, returning `SchedulerMetadata` |
| `GetTriggersOfJob(jobKey)` | extension method over `QueryTriggers(new TriggerQuery { Job = jobKey })` |

Calls to `Shutdown` and `TriggerJob` compile unchanged unless they passed a `CancellationToken` positionally
into the short overload, which now needs to be named:

```diff
- await scheduler.Shutdown(cancellationToken);
+ await scheduler.Shutdown(cancellationToken: cancellationToken);
```

`Interrupt` overloaded on `JobKey` versus `string` hid two different operations behind one name — cancel every
execution of a job, versus cancel one specific fire — so picking the wrong one was a silent, type-driven
mistake. `Interrupt(JobKey)` keeps its name.

`GetTriggersOfJob` is an extension method on `SchedulerQueryExtensions` now, so existing call sites still
compile. It runs `QueryTriggers` and then `GetTriggers` on the keys it found; when the state and fire times a
listing needs are enough, call `QueryTriggers` with `TriggerQuery.Job` yourself and save the second round trip.

### `SchedulerMetadata` replaces `SchedulerMetaData`

```diff
- SchedulerMetaData metaData = await scheduler.GetMetaData();
- Console.WriteLine($"Executed {metaData.NumberOfJobsExecuted} jobs.");
- Console.WriteLine(metaData.GetSummary());
+ SchedulerMetadata metadata = await scheduler.GetMetadata();
+ Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
+ Console.WriteLine(metadata);
```

The old type was built through a fifteen-parameter constructor of which six were adjacent booleans. It is a
`sealed record` with `init` properties now, so a snapshot is built and read by name. Two properties were
renamed: `SchedulerRemote` → `IsRemote` and `NumberOfJobsExecuted` → `JobsExecuted`. `GetSummary()` is gone —
the record's `ToString()` prints every value, which is what the hand-written summary was for. Over HTTP,
`SchedulerStatisticsDto.NumberOfJobsExecuted` is `JobsExecuted` to match.

## Names that were normalized

Renames only — the behavior behind each is unchanged, and a rename that also changes a configuration key is
called out.

| 3.x | 4.x |
|---|---|
| `QuartzScheduler.NumJobsExecuted` | `NumberOfJobsExecuted` (the type is internal now — read `IScheduler.GetMetadata()`) |
| `QuartzScheduler.JobStoreClass`, `.ThreadPoolClass` | `JobStoreType`, `ThreadPoolType` (they return a `Type`; the type is internal now) |
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
| `QuartzScheduler` and `QuartzSchedulerResources` are internal | Resolve `IScheduler` / `ISchedulerFactory`; scheduler-wide settings are `QuartzSchedulerOptions` |
| `JobType` introduced | Stores job type info without requiring an actual `Type` instance |
| `RecoveringTriggerKey` behavior | `IJobExecutionContext.RecoveringTriggerKey` now returns `null` when not recovering instead of throwing |
| `DictionaryExtensions` removed | `Quartz.Util.DictionaryExtensions` type was removed |
| `JobStoreSupport` connection methods | `GetNonManagedTXConnection` and `GetConnection` now return `ValueTask<ConnectionAndTransactionHolder>` |
| `JobStoreSupport.UseProperties` `string` setter removed | The `bool` `AdoJobStoreOptions.UseProperties` option and the read-only `CanUseProperties` remain; the property bridge parses the key |
| Protected `JobStoreSupport` / `StdAdoDelegate` members take a `CancellationToken` | Overrides have to add the parameter; callers do not |
| `ConnectionAndTransactionHolder.Close`, `.Commit`, `.Rollback` take a `CancellationToken` | Same |
| `IJobConfigurator<TJob>` members return `IJobConfigurator<TJob>` | `JobBuilder<TJob>` implements them explicitly and keeps its own `JobBuilder<TJob>`-returning members, so `JobBuilder.Create()…` chains are unaffected — see [Job data can name the property](#job-data-can-name-the-property) for the type parameter |
| `IJobConfigurator<TJob>` / `JobBuilder<TJob>` gained `UsingJobData(string, decimal)` | And `UsingJobData(string, string?)` accepts null |
| `IDirectoryScanListener` is asynchronous | `FilesUpdatedOrAdded` and `FilesDeleted` return `ValueTask` and take a `CancellationToken` |
| `LoggingJobHistoryPlugin.Name`, `LoggingTriggerHistoryPlugin.Name` are get-only | The name is handed to a plugin by `Initialize`; writing it afterwards did nothing |
| `TimeSpanParseRuleAttribute` is public | It says how a bare number in configuration is read as a `TimeSpan`, which a component configured by the same keys needs to be able to say |
| `TimeZoneUtil.CustomResolver` is a property | It was a public mutable field |
| Setter-only members gained getters | `DbMetadata.DbBinaryTypeName` (now nullable) and `.ParameterDbTypePropertyName`, `HttpSchedulerProxyFactory.Address` |
| `TriggerState.Executing` added | Reported where `Normal`, `Complete` or `Blocked` used to be, and `Blocked` narrowed to mean a sibling trigger is running (see [Executing is a trigger state](#executing-is-a-trigger-state)) |
| `IDriverDelegate.IsTriggerCurrentlyExecuting` removed | Replaced by `SelectTriggerStateWithExecuting`, which reads the state and the execution in one statement and returns `TriggerExecutionState` |
| `StdAdoConstants.SqlSelectCountExecutingFiredTriggersOfTrigger` removed | Removed with the method that used it; the per-job `SqlSelectCountExecutingFiredTriggersOfJob` remains — both on what is now an internal type |
| `StdAdoConstants` and `IAdoUtil` are internal | Statement text is not a contract; the schema names stay public on `AdoConstants`, which is a static class rather than a base class |
| Trigger persistence delegates are all public and `sealed` | `CronTriggerPersistenceDelegate`, `SimpleTriggerPersistenceDelegate` and `DailyTimeIntervalTriggerPersistenceDelegate` were internal; derive from `SimplePropertiesTriggerPersistenceDelegateSupport` for a delegate of your own |
| `SchedulerConstants` and `MisfireInstruction` are `static class`es | They were `struct`s holding only `const`s; constant references are unchanged |
| `QuartzOptions`, `SchedulingOptions`, `QuartzHostedServiceOptions` are `sealed` | `QuartzHostedService` itself stays open for `AddQuartzHostedService<T>` |
| `InternalTriggerState.Executing` removed | It was never assigned or read; RAMJobStore counts executions separately from the state that drives scheduling |
