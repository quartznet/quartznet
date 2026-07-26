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

### Removed

| Removed | Use instead |
|---|---|
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

`Quartz`, `Quartz`, and `Quartz.Serialization.SystemTextJson` have been merged into the main `Quartz` package. You can remove these package references from your project:

```diff
- <PackageReference Include="Quartz" Version="3.*" />
- <PackageReference Include="Quartz" Version="3.*" />
- <PackageReference Include="Quartz.Serialization.SystemTextJson" Version="3.*" />
+ <PackageReference Include="Quartz" Version="4.*" />
```

If you use Newtonsoft.Json serialization, reference `Quartz.Serialization.Newtonsoft` instead of the old `Quartz.Serialization.Json`.

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
public async ValueTask Execute(IJobExecutionContext context)
```

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
var builder = SchedulerBuilder.Create();
builder.UseTimeProvider<FakeTimeProvider>();
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

An alternative approach is to configure the `LoggerFactory` via a `HostBuilder`:

```csharp
Host.CreateDefaultBuilder(args)
.ConfigureServices((hostContext, services) =>
{
  services.AddQuartz(q =>
        {
          q.SetLoggerFactory(loggerFactory);
        });
});
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

An `IJobStore` that implements `IJobListener` no longer automatically receives all events. Register it explicitly as a job listener using `ListenerManager`:

```csharp
scheduler.ListenerManager.AddJobListener(myJobStoreListener);
```

## Scheduler Configuration Validation

* `IdleWaitTime` values less than or equal to zero are no longer silently replaced with a 30-second default — they now throw.
* Negative values for `IdleWaitTime` or `BatchTimeWindow` are rejected.
* `MaxBatchSize` values less than or equal to zero are rejected.
* `DirectSchedulerFactory.CreateScheduler` must now be `await`ed.

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

## Other Breaking Changes

| Change | Details |
|--------|---------|
| `SimpleTriggerImpl` `endUtc` no longer nullable | The constructor argument is now required |
| `QuartzScheduler` ctor change | No longer takes `idleWaitTime`; use `QuartzSchedulerResources.IdleWaitTime` |
| `JobType` introduced | Stores job type info without requiring an actual `Type` instance |
| `RecoveringTriggerKey` behavior | `IJobExecutionContext.RecoveringTriggerKey` now returns `null` when not recovering instead of throwing |
| `DictionaryExtensions` removed | `Quartz.Util.DictionaryExtensions` type was removed |
| `JobStoreSupport` connection methods | `GetNonManagedTXConnection` and `GetConnection` now return `ValueTask<ConnectionAndTransactionHolder>` |
