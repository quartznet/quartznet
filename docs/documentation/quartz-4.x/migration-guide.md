---

title: Migration Guide
---

*This document outlines changes needed when upgrading from Quartz.NET 3.x to 4.x. You should also check [the complete change log](https://raw.github.com/quartznet/quartznet/master/changelog.md).*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)
:::

## Target Framework

Quartz.NET 4.x targets `net8.0` and `net9.0`. The `netstandard2.0` build no longer references `System.Configuration.ConfigurationManager`, so there is no support for Full Framework style `.config` files.

If you are running on an older .NET version, you will need to upgrade your application to at least .NET 8.0 before upgrading to Quartz 4.x.

## Package Changes

`Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting`, and `Quartz.Serialization.SystemTextJson` have been merged into the main `Quartz` package. You can remove these package references from your project:

```diff
- <PackageReference Include="Quartz.Extensions.DependencyInjection" Version="3.*" />
- <PackageReference Include="Quartz.Extensions.Hosting" Version="3.*" />
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

## Other Breaking Changes

| Change | Details |
|--------|---------|
| `SimpleTriggerImpl` `endUtc` no longer nullable | The constructor argument is now required |
| `QuartzScheduler` ctor change | No longer takes `idleWaitTime`; use `QuartzSchedulerResources.IdleWaitTime` |
| `JobType` introduced | Stores job type info without requiring an actual `Type` instance |
| `RecoveringTriggerKey` behavior | `IJobExecutionContext.RecoveringTriggerKey` now returns `null` when not recovering instead of throwing |
| `DictionaryExtensions` removed | `Quartz.Util.DictionaryExtensions` type was removed |
| `JobStoreSupport` connection methods | `GetNonManagedTXConnection` and `GetConnection` now return `ValueTask<ConnectionAndTransactionHolder>` |
