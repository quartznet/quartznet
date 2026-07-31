---

title: Migration Guide
---

*This document outlines changes needed when upgrading from Quartz.NET 3.x to 4.x. You should also check [the release notes](https://github.com/quartznet/quartznet/releases) for each version.*

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

Quartz 4.x requires four columns on `QRTZ_TRIGGERS` (and one on `QRTZ_FIRED_TRIGGERS`) that were
**optional** in 3.x:

| Column | Table(s) | Optional since |
|---|---|---|
| `MISFIRE_ORIG_FIRE_TIME` | `QRTZ_TRIGGERS` | 3.17 |
| `EXECUTION_GROUP` | `QRTZ_TRIGGERS`, `QRTZ_FIRED_TRIGGERS` | 3.18 |
| `PREFERRED_NODE` | `QRTZ_TRIGGERS` | 3.19 |
| `PREFERRED_NODE_AUTO` | `QRTZ_TRIGGERS` | 3.19 |

3.x probed for each of these at startup and disabled the corresponding feature when it was
missing. **4.x removed those probes** and assumes all of them exist, so this migration is
mandatory even if you never used misfire reporting, execution groups or node affinity.

::: warning
Always run migration scripts in a test environment against a copy of your production database first.
:::

Apply the script for your database from
[database/migrations/4.0/](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) —
`schema_30_to_40_upgrade_sqlServer.sql`, `_postgres`, `_mysql_innodb`, `_oracle`, `_sqlite` or
`_firebird`. Every statement checks first, so it is safe to run whether or not you already applied
the optional 3.x migrations, and safe to run twice.

For SQL Server the column additions look like this:

```sql
IF COL_LENGTH('QRTZ_TRIGGERS','MISFIRE_ORIG_FIRE_TIME') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [MISFIRE_ORIG_FIRE_TIME] bigint NULL;
END
```

Replace `QRTZ_` with your configured table prefix if different.

See [Database Schema Changes](../database/schema-changes.md) for the full version-by-version
history, including what each optional 3.x migration does and what skipping it costs.

### Listing indexes (optional)

The same script aligns the index set with the statements 4.x issues. Two of the additions matter
most for the [job and trigger listings](#job-store-listings-became-queries):

| Index | Table and columns |
|---|---|
| `IDX_QRTZ_J_G_N` | `QRTZ_JOB_DETAILS(SCHED_NAME, JOB_GROUP, JOB_NAME)` |
| `IDX_QRTZ_T_G_N` | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME)` |

Listings page with `ORDER BY JOB_GROUP, JOB_NAME` and `ORDER BY TRIGGER_GROUP, TRIGGER_NAME`, and the primary
keys are name-before-group, so no existing index serves those ordered scans. **These are optional** — the
queries work without them, but each page becomes a scan plus a sort. Add them if you list jobs or triggers
from a large schema. They are in the fresh-install scripts for every dialect already.

The same section drops indexes that are a leftmost prefix of a wider one, or that no 4.x statement
can drive a scan from. PostgreSQL gets the largest change: several of its indexes omitted
`SCHED_NAME`, which is the leading column of every predicate Quartz issues, so they could not serve
a single-scheduler lookup at all.

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

`IsReadOnly` is an explicit interface implementation and cannot be accessed directly on a `DirtyFlagMap` instance. `IsFixedSize`, `SyncRoot` and `IsSynchronized` are gone with the non-generic interfaces — see [`DirtyFlagMap` dropped the non-generic collection interfaces](#dirtyflagmap-dropped-the-non-generic-collection-interfaces).

## Listener API Changes

The three kinds of listener are managed identically now: a listener is registered under a name, registering the
same name again replaces it, and it is removed by that name.

| 3.x | 4.x |
|---|---|
| `AddJobListener(l, params IMatcher<JobKey>[])` and `AddJobListener(l, IReadOnlyCollection<…>)` | one `AddJobListener(l, params IReadOnlyCollection<IMatcher<JobKey>>)` |
| `AddTriggerListener(l, params IMatcher<TriggerKey>[])` and the collection overload | one `AddTriggerListener(l, params IReadOnlyCollection<IMatcher<TriggerKey>>)` |
| `GetJobListeners()`, `GetTriggerListeners()`, `GetSchedulerListeners()` → arrays | → `IReadOnlyList<T>` |
| `GetJobListenerMatchers(name)`, `GetTriggerListenerMatchers(name)` → array or `null` | → `IReadOnlyList<…>`, empty and never null |
| `GetJobListener(name)`, `GetTriggerListener(name)` throw `KeyNotFoundException` | return null |
| `RemoveSchedulerListener(ISchedulerListener)` | `RemoveSchedulerListener(string name)` |
| — | `GetSchedulerListener(string name)` → `ISchedulerListener?` |

C# 13 params collections turn the two `Add*Listener` overloads into a single member that accepts both call
shapes, so existing calls compile unchanged:

```csharp
// both still work
scheduler.ListenerManager.AddJobListener(myJobListener, matcherA, matcherB);
scheduler.ListenerManager.AddJobListener(myJobListener, listOfMatchers);
```

The matcher listings returned `null` both for "this listener registered no matchers" and for "there is no such
listener". Both mean the same thing to the scheduler — no matchers means every event matches — so they are
empty lists now:

```diff
- var matchers = listenerManager.GetJobListenerMatchers(name);
- if (matchers is null) { /* matches everything */ }
+ var matchers = listenerManager.GetJobListenerMatchers(name);
+ if (matchers.Count == 0) { /* matches everything */ }
```

Asking for a listener that is not registered returns null instead of throwing, so checking is no longer an
exception:

```diff
- try { var listener = listenerManager.GetJobListener(name); } catch (KeyNotFoundException) { }
+ IJobListener? listener = listenerManager.GetJobListener(name);
```

### Scheduler listeners are identified by name

`ISchedulerListener` has a `Name`, a default interface member returning `GetType().Name`. Registering two
scheduler listeners whose `Name` matches replaces the first, exactly as it has always worked for job and
trigger listeners. Override `Name` if you register several instances of one type with the same scheduler:

```diff
- scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener);
+ scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener.Name);
```

`SchedulerListenerSupport` implements `Name` as a `virtual` property, so a listener deriving from it can read
and override it. A test double does not run a default interface member, so a faked `ISchedulerListener` needs
its `Name` configured before `AddSchedulerListener` will accept it:

```csharp
ISchedulerListener listener = A.Fake<ISchedulerListener>();
A.CallTo(() => listener.Name).Returns("myListener");
```

### `JobsPaused` and `JobsResumed` take a nullable group

```diff
- ValueTask JobsPaused(string jobGroup, CancellationToken cancellationToken = default);
- ValueTask JobsResumed(string jobGroup, CancellationToken cancellationToken = default);
+ ValueTask JobsPaused(string? jobGroup, CancellationToken cancellationToken = default);
+ ValueTask JobsResumed(string? jobGroup, CancellationToken cancellationToken = default);
```

Null means every group, matching `TriggersPaused` and `TriggersResumed`, which were already nullable. The
scheduler's own pause-all path still raises the job events once per group; the signature simply stops claiming
a group name is guaranteed where the trigger twins admit it is not. Implementations with a non-nullable
parameter produce a nullability warning (an error under `TreatWarningsAsErrors`) until the `?` is added.

`JobScheduled(ITrigger)` and `JobUnscheduled(TriggerKey)` remain asymmetric on purpose: once a trigger is
unscheduled there is no trigger left to hand out.

### Two members were renamed

```diff
- ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default);
+ ValueTask SchedulerShuttingDown(CancellationToken cancellationToken = default);

- ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default);
+ ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default);
```

### Instantiation failures name the trigger

When `IJobFactory` cannot produce a job — a constructor dependency the container cannot resolve is the usual
reason — the trigger has already fired, but there is no `IJobExecutionContext` yet, so no `ITriggerListener` or
`IJobListener` callback can be raised. `SchedulerError` is the only notification, and it used to carry the job
key as interpolated message text and the trigger nowhere at all.

It now receives a `JobInstantiationException`:

```csharp
public override ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default)
{
    if (exception is JobInstantiationException failure)
    {
        logger.LogError(failure, "Job {Job} could not be built for trigger {Trigger}, fire {FireInstanceId}",
            failure.JobDetail.Key, failure.Trigger.Key, failure.FireInstanceId);
    }

    return default;
}
```

Additive — `SchedulerError` already took a `SchedulerException` — and it mirrors what
`JobExecutionProcessException` carries for execution-time failures. Both factory paths raise it: the container's,
where `ActivatorUtilities` throws, and `SimpleJobFactory`'s, where the original failure arrives as the
`InnerException`.

The two messages reporting this also had their closing quote moved to where it belongs, from after the inner
exception's message to after the type name: `Problem instantiating type 'MyNamespace.MyJob': message`. Code
matching on that text should read the exception instead.

To prevent the failure rather than observe it, see [failing fast when job dependencies cannot be
resolved](packages/microsoft-di-integration.md#failing-fast-when-job-dependencies-cannot-be-resolved).

### Triggers entering the error state are reported

A trigger could be parked in `TriggerState.Error` with nothing observing it. The stores logged a line and moved
on, so a trigger simply stopped working and the only way to find out was to poll `GetTriggerState` or read the
log — and two of the ADO store's transitions, a job type that will not load while acquiring and a job that
cannot be read back in `TriggersFired`, did not reach the scheduler at all.

```csharp
ValueTask TriggerInError(TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;
ValueTask TriggersInError(JobKey jobKey, CancellationToken cancellationToken = default) => default;
```

Following the singular/plural pair `ISchedulerListener` already has for pause and resume. Both are
default-implemented, as are the matching `ISchedulerSignaler.NotifySchedulerListenersTriggerInError` and
`NotifySchedulerListenersTriggersInError`, so an existing listener or signaler still compiles and behaves
exactly as it does today — no notification.

The plural is keyed by `JobKey` rather than by the individual triggers because `SetAllJobTriggersError` is one
bulk statement in the persistent store, and enumerating the affected keys would mean an extra query on a failure
path. Ask `GetTriggersOfJob` where the keys themselves matter.

Neither carries a cause. The stores raise these and receive only a `SchedulerInstruction`; `SchedulerError` says
*why* and these say *what changed*, and where both apply they arrive together. Recover a trigger with
`IScheduler.ResetTriggerFromErrorState`.

### The broadcast listeners have one shape

`BroadcastSchedulerListener.GetListeners()` is a `Listeners` property, matching `BroadcastJobListener` and
`BroadcastTriggerListener`, and all three constructors take an `IReadOnlyCollection<T>`. All three now take the
same arguments: a name, and optionally the listeners to start with. `BroadcastJobListener` no longer asks for
an `ILogger` — it resolves its own, as the other two always did — and `BroadcastSchedulerListener` takes a name,
which it needs now that scheduler listeners are name-identified.

| 3.x | 4.x |
|---|---|
| `new BroadcastJobListener(logger, name)` | `new BroadcastJobListener(name)` |
| `new BroadcastJobListener(logger, name, listeners)` | `new BroadcastJobListener(name, listeners)` |
| `new BroadcastSchedulerListener()` | `new BroadcastSchedulerListener(name)` |
| `new BroadcastSchedulerListener(listeners)` | `new BroadcastSchedulerListener(name, listeners)` |

`BroadcastSchedulerListener` also gained `RemoveListener(string)`, which the job and trigger ones already had.

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

## Daylight saving time

Two schedules fire at different times than they did on 3.x. Neither needs a code change, but both change
*when* existing triggers run, so review any schedule that crosses a transition.

**Interval cron expressions fire through both halves of a fall-back hour.** A cron expression whose second,
minute or hour field uses a wildcard, a step or a range — `0 * * * * ?`, `0 0/30 * * * ?` — now fires through
**both** occurrences of the wall-clock window that repeats when the clock goes back. Previously the repeated
window fired only once, so an "every minute" schedule silently skipped an hour of real time. Fixed-time
expressions such as `0 30 2 * * ?`, including comma lists like `0 0,30 2 * * ?`, are unchanged: they still fire
once per day, at the first occurrence of an ambiguous wall-clock time.

**`CalendarIntervalTrigger` with `PreserveHourOfDayAcrossDaylightSavings` steps in local wall-clock time.**
Fire times are now always exactly on schedule and strictly increasing; the previous implementation could return
times the schedule never specified when its daylight-saving adjustments failed to make progress. In time zones
whose daylight delta is not a whole hour — Australia/Lord_Howe — the scheduled local time no longer drifts by
the sub-hour part of the delta across a transition, so the flag preserves the full time of day as its
documentation always promised.

## New Features

* **[RecurrenceTrigger (RRULE)](tutorial/recurrencetrigger.md)** — schedule jobs using RFC 5545 recurrence rules for complex patterns like "every 2nd Monday of the month" or "last weekday of March each year"
* **H (hash) token in cron expressions** — deterministic load distribution across triggers using the trigger identity as seed
* **HTTP API** — optional REST API for managing the scheduler remotely (see [HTTP API](packages/http-api.md))
* **Paged, projected job store queries** — list and count jobs, triggers, groups and calendars a page at a time, with the metadata a listing needs already in the row (see [Job store listings became queries](#job-store-listings-became-queries))
* **Job data by property name** — bind job data to the job property it is meant for instead of spelling its key (see [Job data can name the property](#job-data-can-name-the-property))
* **`TriggerState.Executing`** — tell whether a trigger's job is running, across the whole cluster (see [Executing is a trigger state](#executing-is-a-trigger-state))
* **`JobInstantiationException`** — a job that could not be built names the trigger, the job and the fire instance instead of only interpolating the job key into a message (see [Instantiation failures name the trigger](#instantiation-failures-name-the-trigger))
* **`ISchedulerListener.TriggerInError` / `TriggersInError`** — observe a trigger being moved to `TriggerState.Error`, including two ADO store transitions that reached nothing at all before (see [Triggers entering the error state are reported](#triggers-entering-the-error-state-are-reported))
* **Joining a transaction the application owns** — the ADO job store can take part in a transaction you started, so saving your own data and scheduling the job that acts on it commit together or not at all. Turn it on with `AcceptEnlistedTransactions()` on the persistent store builder, `JobStore:AcceptEnlistedTransactions`, or `quartz.jobStore.acceptEnlistedTransactions`, then hand the store a connection for the duration of a scope with `IScheduler.EnlistTransaction` / `EnlistConnection`. Handing over a connection is the only way to take part: a connection the job store opens for itself is deliberately kept out of any ambient `TransactionScope`, since a second connection in that transaction would require promoting it to a distributed one. See [Joining an existing transaction](tutorial/job-stores.md#joining-an-existing-transaction)
* **Builder methods for three more plugins** — `UseJobHistoryLogging()`, `UseTriggerHistoryLogging()` and `UseShutdownHook()`. Only the structured-logging variants had one, so the classic history plugins and the shutdown hook could previously be reached only through `quartz.plugin.*` property keys

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
| `ITriggerConfigurator` | `ITriggerConfigurator<TJob>` — the 4.x `ITriggerConfigurator` is a new, much smaller base holding only `WithSchedule`, see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
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
| `IsJobGroupPaused(group)` | `QueryJobGroups(new JobGroupQuery { Name = group, Paused = true, Take = 1 })` |
| `IsTriggerGroupPaused(group)` | `QueryTriggerGroups(new TriggerGroupQuery { Name = group, Paused = true, Take = 1 })` |

`IJobStore` loses the same members plus the counting and existence ones, and has no extension methods to
soften it — if you implement a job store, you implement the query members:

| Removed from `IJobStore` | Use instead |
|---|---|
| `GetJobKeys`, `GetTriggerKeys` | `QueryJobs`, `QueryTriggers` |
| `GetJobGroupNames`, `GetTriggerGroupNames`, `GetPausedTriggerGroups` | `QueryJobGroups`, `QueryTriggerGroups` |
| `GetCalendarNames` | `QueryCalendarNames` |
| `IsJobGroupPaused`, `IsTriggerGroupPaused` | the matching `Query*Groups` with `Name` and `Paused = true` |
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
List<IJobDetail> details = await scheduler.GetJobDetails(page.Items.Select(x => x.Key).ToList());
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
| two `SelectJobForTrigger` overloads | one, with a required `bool loadJobType` |
| `DeletePausedTriggerGroup(conn, string, ct)` | the `GroupMatcher<TriggerKey>` overload |

`FiredTriggerQuery` carries an optional `Trigger`, `Job` and `InstanceId` combined with AND — all null
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

## Trigger states are typed on the driver delegate

Eighteen members of `IDriverDelegate` took a trigger state as a `string` whose only legal values were the
`AdoConstants.State*` constants. A typo, a stale spelling, or a transposed `newState`/`oldState` pair
compiled and then quietly matched no row. `Quartz.Impl.AdoJobStore.StoredTriggerState` is now that type.

**Nothing changes in the database.** The columns still hold the same strings; the conversion happens at the
delegate boundary, and `AdoConstants.State*` stays public because the strings are the schema contract. A 4.0
scheduler reads and writes rows a 3.x one wrote, and the two can share a cluster.

| `AdoConstants` constant | Stored value | `StoredTriggerState` member |
|---|---|---|
| `StateWaiting` | `WAITING` | `StoredTriggerState.Waiting` |
| `StateAcquired` | `ACQUIRED` | `StoredTriggerState.Acquired` |
| `StateExecuting` | `EXECUTING` | `StoredTriggerState.Executing` |
| `StateComplete` | `COMPLETE` | `StoredTriggerState.Complete` |
| `StateBlocked` | `BLOCKED` | `StoredTriggerState.Blocked` |
| `StateError` | `ERROR` | `StoredTriggerState.Error` |
| `StatePaused` | `PAUSED` | `StoredTriggerState.Paused` |
| `StatePausedBlocked` | `PAUSED_BLOCKED` | `StoredTriggerState.PausedBlocked` |
| `StateDeleted` | `DELETED` | `StoredTriggerState.Deleted` |

Both directions are public, because a custom delegate binds the string into its own statements:

```csharp
string stored = StoredTriggerState.PausedBlocked.ToStoredValue();   // "PAUSED_BLOCKED"
StoredTriggerState state = StoredTriggerStates.FromStoredValue(stored);
```

`FromStoredValue` is deliberately lenient in exactly the way the store always was: a value this version does
not recognise — left by a third-party delegate, a migration, or a hand-repaired row — reads as `Waiting`
(schedulable, reported as a normal trigger), and a `null` column value reads as `Deleted`, the sentinel a
missing trigger already reported. `SelectTriggerState` returns `StoredTriggerState` for the same reason its
result flows straight into `AddTrigger` and `UpdateTrigger`.

One behavior follows from that, and only for a row whose stored state no Quartz version writes: the
mutation side now agrees with the listing side about it. A listing has always reported such a trigger as
`Normal`, while `PauseTrigger` matched neither `WAITING` nor `ACQUIRED` and silently did nothing; now it
pauses it. Storing such a trigger back writes `WAITING` rather than preserving the unrecognised value.

`MisfiredTriggerUpdate.NewState` and `TriggerExecutionState.State` (and its constructor) carry the enum too.
On `JobStoreSupport`, the protected members that pass a state through follow: `AddTrigger`,
`UpdateMisfiredTrigger` and `CheckBlockedState`, the last of which now returns
`ValueTask<StoredTriggerState>`.

### The `…FromOtherStates` members take a collection

Three members hard-coded two or three old states. A caller that wanted one repeated a value; one that wanted
four could not say so. The predicate is now generated for the length of the set, with duplicates folded away.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `UpdateTriggerStatesFromOtherStates(conn, newState, oldState1, oldState2, ct)` | `(conn, StoredTriggerState newState, IReadOnlyCollection<StoredTriggerState> oldStates, ct)` |
| `UpdateTriggerStateFromOtherStates(conn, key, newState, oldState1, oldState2, oldState3, ct)` | `(conn, key, StoredTriggerState newState, IReadOnlyCollection<StoredTriggerState> oldStates, ct)` |
| `UpdateTriggerGroupStateFromOtherStates(conn, matcher, newState, oldState1, oldState2, oldState3, ct)` | `(conn, matcher, StoredTriggerState newState, IReadOnlyCollection<StoredTriggerState> oldStates, ct)` |

```diff
- await Delegate.UpdateTriggerStatesFromOtherStates(conn, AdoConstants.StateWaiting,
-     AdoConstants.StateAcquired, AdoConstants.StateBlocked, cancellationToken);
+ await Delegate.UpdateTriggerStatesFromOtherStates(conn, StoredTriggerState.Waiting,
+     [StoredTriggerState.Acquired, StoredTriggerState.Blocked], cancellationToken);
```

An empty set throws `ArgumentException` rather than building a statement that matches nothing. The parameter
is a plain collection rather than a `params` one because every async member of this codebase ends with its
cancellation token, and a `params` parameter has to come last.

### One term for a scheduler instance

The value these members carry has always been the scheduler *instance id*
(`quartz.scheduler.instanceId`), not the scheduler name — `SchedulerStateRecord.SchedulerInstanceId` already
said so, and the interface said `instanceId` while the implementation said `instanceName`.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `FiredTriggerQuery.InstanceName` | `FiredTriggerQuery.InstanceId` |
| `InsertSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `UpdateSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `DeleteSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `SelectSchedulerStateRecords(conn, string? instanceName, …)` | `instanceId` |

**Column names are unchanged**: `SCHED_NAME` still holds the scheduler name and `INSTANCE_NAME` the
instance id. `ITriggerPersistenceDelegate.Initialize`'s `schedulerName` keeps its name — that one really is
the scheduler name.

### Three parameter shapes were fixed

* **`IsJobCurrentlyExecuting(conn, JobKey jobKey, ct)`** — it took `(string jobName, string jobGroup)`, the
  last member of the interface splitting a key into two transposable strings.
* **`SelectJobForTrigger(conn, key, loadHelper, bool loadJobType, ct)`** — `loadJobType` defaulted to `true`
  while sitting in front of the cancellation token, so a call passing a token positionally bound it to the
  wrong parameter. Both values are genuinely used, so the parameter stays; pass `loadJobType: true` for the
  previous default.
* **`UpdateTriggerPreferredNodeConditional(conn, key, PreferredNodeTransition transition, ct)`** — the
  compare-and-swap took `(node, auto, expectedNode, expectedAuto)`, four loose values in two pairs that are
  trivial to transpose and impossible for the compiler to check. The record names both sides and carries
  [`PreferredNode`](#the-preferred-node-is-a-value) values rather than the raw column pair:

  ```csharp
  await Delegate.UpdateTriggerPreferredNodeConditional(conn, trigger.Key, new PreferredNodeTransition
  {
      Expected = PreferredNode.Auto,
      New = PreferredNode.For(InstanceId)
  }, cancellationToken);
  ```

### The matcher-based selects stay, deliberately

`SelectTriggerGroups(matcher)`, `SelectJobsInGroup`, `SelectTriggersInGroup` and `SelectTriggerNamesForJob`
look like leftovers next to the paged `SelectJobHeaders` / `SelectTriggerHeaders` / `SelectTriggerGroups(query)`
members, and they are not. They are not listings: they serve the pause/resume and removal mutation paths,
which have to move every matching row under one lock and therefore must not be paged. Their doc comments now
say so, and they are not going away.

## The ADO.NET job stores are named for whose transaction they use

`JobStoreTX` and `JobStoreCMT` were named after a Java EE distinction — "TX" versus
"container-managed transactions" — that says nothing in .NET, and neither name says which one commits.
They now say it:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `Quartz.Impl.AdoJobStore.JobStoreTX` | `Quartz.Impl.AdoJobStore.LocalTransactionJobStore` |
| `Quartz.Impl.AdoJobStore.JobStoreCMT` | `Quartz.Impl.AdoJobStore.ExternalTransactionJobStore` |

`LocalTransactionJobStore` begins the transaction each operation runs in and commits or rolls it back
itself; it stays the default and the one nearly everybody wants. `ExternalTransactionJobStore` runs
inside a transaction somebody else owns and neither commits nor rolls back.

**Configuration naming either as a string keeps working.** `quartz.jobStore.type` is the one type name
almost every persistent configuration spells out, so both old names resolve through the same fallback as
the [renamed namespaces](#quartz-spi-and-quartz-simpl-were-renamed), with a warning telling you what to
write instead:

```text
# both of these resolve, the first with a warning
quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz
quartz.jobStore.type = Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz
```

Code that names the type — `UsePersistentStore<JobStoreTX>()`, a subclass, a `typeof` — has to be updated.
`UsePersistentStore()` with no type argument already picks the right store and needs no change.

### The vocabulary follows

The "non-managed TX" phrasing went with the old names. The members it appeared in are protected, so this
only reaches a `JobStoreSupport` subclass:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `GetNonManagedTXConnection` | `GetLocalTransactionConnection` |
| `ExecuteInNonManagedTXLock` | `ExecuteInLocalTransactionLock` |
| `RetryExecuteInNonManagedTXLock` | `RetryExecuteInLocalTransactionLock` |

`ExternalTransactionJobStore.OpenConnection` is a normal `{ get; set; }`. It was
`{ protected get; set; }` — writable from anywhere and readable only from inside, which is not a shape
anything needs.

## Nine `Execute…Lock` overloads became four members

`JobStoreSupport` had nine overlapping ways to run a callback under a lock, three of which existed only to
adapt a `void` callback and did so by returning `object` — a value that was always `null` and was never
read. Optional parameters replace the ladder:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `ExecuteWithoutLock<T>(txCallback, ct)` | unchanged |
| `abstract ExecuteInLock<T>(lockName, txCallback, ct)` | `ExecuteInLock<T>(SchedulerLock? lockKind, txCallback, ct)` |
| `ExecuteInLock(lockName, txCallback, ct)` → `ValueTask<object>` | `ExecuteInLock(SchedulerLock? lockKind, txCallback, ct)` → `ValueTask` |
| `ExecuteInNonManagedTXLock` ×4 | `ExecuteInLocalTransactionLock<T>(SchedulerLock? lockKind, txCallback, txValidator = null, requestorId = null, ct)` plus one `ValueTask`-returning convenience |
| `RetryExecuteInNonManagedTXLock` ×2 | `RetryExecuteInLocalTransactionLock<T>(SchedulerLock? lockKind, txCallback, requestorId = null, ct)` plus one `ValueTask`-returning convenience |

A call that passed a cancellation token positionally after the validator now has to name it
(`cancellationToken: cancellationToken`), because the parameters between them became optional.
`RecoverJobs(CancellationToken)` returns `ValueTask` rather than `ValueTask<bool>` — the `bool` was the
constant `true` that the old void adapter produced.

## Locks are a `SchedulerLock`, not a string

`ISemaphore` took the lock as a `string` whose only two legal values were `"TRIGGER_ACCESS"` and
`"STATE_ACCESS"`; anything else threw at run time. `Quartz.Impl.AdoJobStore.SchedulerLock` is now that
type, with members `TriggerAccess` and `StateAccess`.

```diff
- ValueTask<bool> ObtainLock(Guid requestorId, ConnectionAndTransactionHolder? conn, string lockName, CancellationToken ct = default);
- ValueTask ReleaseLock(Guid requestorId, string lockName, CancellationToken ct = default);
+ ValueTask<bool> ObtainLock(Guid requestorId, ConnectionAndTransactionHolder? conn, SchedulerLock lockKind, CancellationToken ct = default);
+ ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken ct = default);
```

`JobStoreSupport.LockTriggerAccess` and `LockStateAccess` are gone with the strings they held. **Nothing
changes in the database**: the `LOCK_NAME` column still holds `TRIGGER_ACCESS` and `STATE_ACCESS`, the
conversion happens where the row is written, and a 4.0 node contends for the same rows as a 3.x one. The
same applies to `Quartz.Extensions.Redis`, whose keys keep their `…:TRIGGER_ACCESS` spelling.

`DBSemaphore.ExecuteSql` still receives the stored name as a `string` — that parameter really is the value
bound into the statement.

## The job store configuration is read-only

Twenty-odd `JobStoreSupport` properties duplicated `AdoJobStoreOptions` and `QuartzSchedulerOptions` with
a public setter. Writing one after the store had started did nothing useful in most cases and quietly
diverged from the options everything else reads, so they are now `{ get; }` and sourced from the injected
options: `AcceptEnlistedTransactions`, `AcquireTriggersWithinLock`, `ClusterCheckinInterval`,
`ClusterCheckinMisfireThreshold`, `Clustered`, `ConnectionManager`, `DataSource`, `DbRetryInterval`,
`DoubleCheckLockMisfireHandler`, `DriverDelegateInitString`, `InstanceId`, `InstanceName`, `LockOnInsert`,
`MakeThreadsDaemons`, `MaxMisfiresToHandleAtATime`, `MaxTransientRetries`, `MisfireHandlerFrequency`,
`ObjectSerializer`, `PerformSchemaValidation`, `RetryableActionErrorLogThreshold`, `SelectWithLockSql`,
`TablePrefix`, `TransientRetryInterval`, `TxIsolationLevelSerializable` and `UseDbLocks`.

Configure them where they are configured now:

```diff
- var store = new JobStoreTX(...) { Clustered = true, MaxTransientRetries = 5 };
+ services.AddQuartz(q => q.UsePersistentStore(store => store.Configure(options =>
+ {
+     options.Clustered = true;
+     options.MaxTransientRetries = 5;
+ })));
```

`MisfireThreshold` deliberately keeps its setter on both `JobStoreSupport` and `RAMJobStore`: it is read on
every misfire pass rather than only at startup.

Two properties that nothing read are gone rather than made read-only: `DriverDelegateType` (the delegate is
injected, not loaded from a type name here) and `DontSetAutoCommitFalse` (never consulted).
`LastCheckin` and `LogWarnIfNonZero` are internal and private respectively — cluster check-in bookkeeping
and a log helper, neither of which a subclass has any business in. The `[TimeSpanParseRule]` attributes on
these properties are gone too; they are read only when a component's settings arrive as strings, which for
this store they no longer do.

## The semaphores were tidied

* `UpdateRowLockSemaphore.cs` defined `UpdateLockRowSemaphore`, and `UpdateRowLockSemaphoreMOT.cs` defined
  `UpdateLockRowSemaphoreMOT`. The files are named for their types now; no code changes.
* The public static SQL fields settled on one convention and became `protected`, because nothing outside
  the class hierarchy that owns them ever read them: `StdRowLockSemaphore.SelectForLock` /
  `.InsertLock` keep their names, and `UpdateLockRowSemaphore.SqlUpdateForLock` / `.SqlInsertLock` are
  `UpdateForLock` / `InsertLock`.
* `DBSemaphore.Sql` and `.InsertSql` are get-only and arrive through the constructor. They were
  `protected` settable, which let a subclass swap a statement after the table prefix had already been
  folded into it — the select and the insert backing the same lock could end up naming different tables.
  A subclass that needs its own insert statement passes it up:

  ```diff
    public MyRowLockSemaphore(string tablePrefix, string schedulerName, string? selectWithLockSql, IDbProvider dbProvider)
  -     : base(tablePrefix, schedulerName, selectWithLockSql, dbProvider)
  - {
  -     InsertSql = MyInsertLock;
  - }
  +     : base(tablePrefix, schedulerName, selectWithLockSql, MyInsertLock, dbProvider)
  + {
  + }
  ```

## A job store of your own can join your transaction

`JobStoreSupport` is public and abstract, but everything needed to honour an enlisted transaction was
`private protected`, so a store outside this assembly could not take part in one — it could only open a
connection of its own while the caller believed the scheduling was inside their transaction.

`GetEnlistedConnection` is now `protected`. A `GetLocalTransactionConnection` override starts with it, and
gets `null` back when enlisted transactions are not accepted or nothing is enlisted:

```csharp
protected override async ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(
    CancellationToken cancellationToken = default)
{
    ConnectionAndTransactionHolder? enlisted = await GetEnlistedConnection(cancellationToken);
    if (enlisted is not null)
    {
        return enlisted;
    }

    return await GetConnection(cancellationToken);
}
```

Everything that makes an enlistment safe to use happens inside it: the transaction is checked to be alive
and still current, the provider is checked to match, the connection is opened if it is not, and it is
booked out for the operation so two concurrent scheduler calls cannot share it. Cleaning the holder up
through `CleanupConnection` hands the booking back.

`ConnectionAndTransactionHolder` gained a matching public constructor,
`(DbConnection connection, DbTransaction? transaction, bool ownsResources)`, and a public `OwnsResources`,
for a store that borrows a connection from somewhere else entirely. Owning nothing means committing
nothing — `Commit`, `Rollback`, `Close` and `Dispose` all return without touching a borrowed connection.

`Commit(bool)` and `Rollback(bool)` are internal: when the unit of work commits is the job store's
decision, and `JobStoreSupport.CommitConnection` / `.RollbackConnection` are the seams a subclass
overrides. `Close` stays public.

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
| `IObjectSerializer.DeSerialize` | `Deserialize` |
| `TriggerFiredBundle.PrevFireTimeUtc` | `PreviousFireTimeUtc`, matching the spelling used everywhere else |
| `XMLSchedulingDataProcessor.OverWriteExistingJobs` argument `overWriteExistingJobs` | `overwriteExistingJobs` |

### Abbreviated parameter names were spelled out

Parameter names inherited from the Java port were spelled out across the public surface. Only named
arguments and overriding signatures are affected — a positional call site compiles unchanged.

`cal` → `calendar`, `sched` → `scheduler`, `schedName` → `schedulerName`, `calName` → `calendarName`,
`schedInstId` → `schedulerInstanceId`, `triggerInstCode` / `instCode` → `triggerInstructionCode` /
`instructionCode`, `jec` → `context`, `prevFireTimeUtc` → `previousFireTimeUtc`, `tz` / `timezone` →
`timeZone` on the `InTimeZone` schedule-builder methods, and `je` → `jobExecutionException`.

`ITablePrefixAware.SchedName` is `SchedulerName`, and both of its properties are readable rather than
setter-only.

Every abbreviated constructor parameter of `SchedulerMetaData` was spelled out as well, on what is now
[`SchedulerMetadata`](#schedulermetadata-replaces-schedulermetadata): `schedInst` → `schedulerInstanceId`,
`schedType` → `schedulerType`, `numberOfJobsExec` → `numberOfJobsExecuted`, `jsType` → `jobStoreType`,
`jsPersistent` → `jobStoreSupportsPersistence`, `jsClustered` → `jobStoreClustered`, `tpType` →
`threadPoolType`, `tpSize` → `threadPoolSize`.

## Matchers moved to `Quartz.Matchers`

A matcher is something you hand to `IScheduler`, not an implementation detail of one, so the whole namespace
moved out of `Impl`:

```diff
- using Quartz.Impl.Matchers;
+ using Quartz.Matchers;
```

`GroupMatcher<T>`, `NameMatcher<T>`, `KeyMatcher<T>`, `EverythingMatcher<T>`, `AndMatcher<T>`, `OrMatcher<T>`,
`NotMatcher<T>`, `StringMatcher<T>` and `StringOperator` all moved; every factory method keeps its name
(`GroupEquals`, `NameStartsWith`, `KeyEquals`, `AnyGroup`, …).

`IMatcher<T>` no longer redeclares `Equals(object)` and `GetHashCode()`. They are `object`'s own members, so
declaring them on the interface added no requirement and told an implementer nothing — but a matcher is still
expected to behave as a value, because `RemoveJobListenerMatcher` finds the matcher to remove by equality.

`NameMatcher<TKey>.AnyName()` is new, the counterpart of `GroupMatcher<TKey>.AnyGroup()`.

## `Key<T>` moved to `Quartz` and is immutable

`JobKey` and `TriggerKey` are part of the public model, and their base type sat in a utility namespace:

```diff
- using Quartz.Util;   // for Key<T>
+ // Key<T> is in Quartz, alongside JobKey and TriggerKey
```

The `Name` and `Group` setters are gone. A key is a value, and it is a dictionary key throughout the job
stores — mutating one in place could move an entry out of reach of its own hash bucket. Build a new key
instead:

```diff
- jobKey.Group = "reports";
+ jobKey = new JobKey(jobKey.Name, "reports");
```

`JobKey.Create` is gone too; use the constructors, which is what `TriggerKey` always offered:

```diff
- JobKey key = JobKey.Create("myJob", "reports");
+ JobKey key = new JobKey("myJob", "reports");
```

Payloads written by earlier versions still read: both serializers build a key through its
`(name, group)` constructor now, and the JSON itself is unchanged.

`JobType`, the type of `IJobDetail.JobType`, moved from `Quartz.Impl` to `Quartz` for the same reason.

## Listing queries can filter by name

| Query | New property |
|---|---|
| `JobQuery` | `NameMatcher<JobKey>? Name` |
| `TriggerQuery` | `NameMatcher<TriggerKey>? Name` |
| `JobGroupQuery` | `string? Name` — one group, matched exactly |
| `TriggerGroupQuery` | `string? Name` — one group, matched exactly |

```csharp
PagedResult<JobHeader> nightly = await scheduler.QueryJobs(new JobQuery
{
    Group = GroupMatcher<JobKey>.GroupEquals("reports"),
    Name = NameMatcher<JobKey>.NameStartsWith("nightly")
});
```

The filters combine with AND. `RAMJobStore` and `StdAdoDelegate` both honor them; the ADO store escapes the
matcher's own wildcards in the LIKE it generates, so a job literally named `50%` is matched literally and is
not a pattern. Over HTTP the job and trigger listings take `nameEquals`, `nameStartsWith`, `nameEndsWith` or
`nameContains` (at most one), and the group listings take `name`.

`IsJobGroupPaused` and `IsTriggerGroupPaused` are built on the group-name filter now, so they ask the store
about the one group instead of listing every paused group and searching it.

`PagedResult<T>.Items` is an `IReadOnlyList<T>` rather than a `List<T>` — a page is a result to read, not a
list to mutate. The two `List<T>` members a caller is most likely to have used:

```diff
- IReadOnlyList<JobKey> keys = result.Items.ConvertAll(x => x.Key);
+ List<JobKey> keys = result.Items.Select(x => x.Key).ToList();

- bool found = result.Items.Exists(x => x.Name == group);
+ bool found = result.Items.Any(x => x.Name == group);
```

## `ISchedulerRepository` overloads collapsed

| 3.x | 4.x |
|---|---|
| `Bind(scheduler)`, `Bind(scheduler, instanceId)` | `Bind(IScheduler scheduler, string? instanceId = null)` |
| `Lookup(name)`, `Lookup(name, instanceId)` | `Lookup(string schedulerName, string? instanceId = null)` |
| `Remove(name)` → `void`, `Remove(name, instanceId)` → `bool` | `Remove(string schedulerName, string? instanceId = null)` → `bool` |

Existing calls compile unchanged. A null instance ID means the same as the old one-argument overload: bind
under the scheduler's own `SchedulerInstanceId`, and look up or remove the first scheduler registered under the
name. `Remove` returns `bool` in both shapes now, where the one-argument form used to return nothing.
`LookupAll` and `LookupByName` are unchanged — cluster scenarios disambiguate through them.

## Trigger fire state is read-only on the interfaces

```diff
- int Priority { get; set; }   // ITrigger
+ int Priority { get; }

- int TimesTriggered { get; set; }   // ISimpleTrigger, ICalendarIntervalTrigger,
+ int TimesTriggered { get; }        // IDailyTimeIntervalTrigger, IRecurrenceTrigger
```

`IMutableTrigger.Priority` still has its setter, which is what `TriggerBuilder.WithPriority` and the job stores
go through. `TimesTriggered` is fire-count state the scheduler maintains; the concrete `SimpleTriggerImpl`,
`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl` keep their settable
property. A trigger handed back by `IScheduler.GetTrigger` is a snapshot, so writing to either never reached
the store to begin with — to change a stored trigger, build a new one and reschedule.

The built-in JSON trigger serializers are typed on the concrete triggers now, because they restore
`TimesTriggered` when deserializing. A custom serializer deriving from one of them has to follow:

```diff
- public class MySimpleTriggerSerializer : TriggerSerializer<ISimpleTrigger>
+ public class MySimpleTriggerSerializer : TriggerSerializer<SimpleTriggerImpl>
```

This applies to `SimpleTriggerSerializer`, `CalendarIntervalTriggerSerializer`,
`DailyTimeIntervalTriggerSerializer` and `RecurrenceTriggerSerializer`, in both
`Quartz.Serialization.Json.Triggers` and `Quartz.Serialization.Newtonsoft.Triggers`. `CronTriggerSerializer`
is unchanged — it has no fire-count state to restore.

## Nine `UsingJobData` overloads became one

`JobDataMap`'s indexer takes an `object?`, and every one of the nine primitive `UsingJobData` overloads had
the same one-line body writing through it. The overload set decided nothing except which of nine identical
methods the compiler picked, and it cost forty-four declarations across `IJobConfigurator<TJob>`,
`JobBuilder<TJob>`, `ITriggerConfigurator<TJob>` and `TriggerBuilder<TJob>`. There are twelve now.

| 3.x | 4.x |
|---|---|
| `UsingJobData(string key, string? value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, int value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, long value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, float value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, double value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, decimal value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, bool value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, Guid value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, char value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(JobDataMap newJobDataMap)` | unchanged — merges into what the builder already holds |
| `UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value)` | unchanged |
| `SetJobData(JobDataMap newJobDataMap)` | removed |

**Existing calls compile and store exactly what they stored before.** An `int` argument still lands in the map
boxed as an `int`, a `Guid` as a `Guid`, a `null` as a `null`. Nothing is converted on the way in, and what a
persistent store can hold is still whatever its serializer round-trips — AdoJobStore's `UseProperties` mode,
strings only.

To store a value in a job property's own type — an `int` literal narrowed to the `byte` the property
declares, an enum written as its name — name the property instead of its key:

```csharp
JobBuilder.Create<MyJob>().UsingJobData(job => job.RetryCount, 3)
```

### `SetJobData` is gone

`SetJobData` *replaced* the builder's map where `UsingJobData(JobDataMap)` merges into it: one character of
difference in the call, the opposite meaning. Replacing is only what a job store rebuilding a stored job
wants, so it is internal now.

```diff
- JobBuilder.Create<MyJob>().UsingJobData("a", 1).SetJobData(map)   // "a" silently discarded
+ JobBuilder.Create<MyJob>().UsingJobData(map)                      // merge, or start from a fresh builder
```

## One family of `WithXSchedule` extensions

Attaching a schedule to a trigger was spread over six static extension classes and twenty-nine methods, half
of them existing only because `TriggerBuilder<TJob>` and `ITriggerConfigurator<TJob>` each needed their own
copy of the same body. `Quartz.TriggerConfiguratorExtensions` replaces all six with ten methods that are
generic in the receiver and return it unchanged, so one method serves both and the chain keeps its type.

Deleted: `SimpleScheduleTriggerBuilderExtensions`, `CronScheduleTriggerBuilderExtensions`,
`CalendarIntervalTriggerBuilderExtensions`, `DailyTimeIntervalTriggerBuilderExtensions`,
`RecurrenceTriggerBuilderExtensions`, `TriggerExtensions`.

| 3.x | 4.x |
|---|---|
| `WithSimpleSchedule()` | `WithSimpleSchedule()` |
| `WithSimpleSchedule(Action<SimpleScheduleBuilder>)` | `WithSimpleSchedule(Action<SimpleScheduleBuilder>? configure = null)` |
| `WithSimpleSchedule(SimpleScheduleBuilder)` | `WithSimpleSchedule(SimpleScheduleBuilder schedule)` |
| `WithCronSchedule(string)` | `WithCronSchedule(string cronExpression, Action<CronScheduleBuilder>? configure = null)` |
| `WithCronSchedule(string, Action<CronScheduleBuilder>)` | same member |
| `WithCronSchedule(string expr, string hashKey)` | `WithCronSchedule(CronScheduleBuilder.CronSchedule(new CronExpression(expr, hashKey)))` |
| `WithCronSchedule(string expr, string hashKey, Action<CronScheduleBuilder>)` | build the `CronScheduleBuilder`, configure it, pass it |
| `WithCronSchedule(CronScheduleBuilder)` | `WithCronSchedule(CronScheduleBuilder schedule)` |
| `WithCalendarIntervalSchedule()` | `WithCalendarIntervalSchedule(Action<CalendarIntervalScheduleBuilder>? configure = null)` |
| `WithCalendarIntervalSchedule(Action<CalendarIntervalScheduleBuilder>)` | same member |
| `WithCalendarIntervalSchedule(CalendarIntervalScheduleBuilder)` | `WithCalendarIntervalSchedule(CalendarIntervalScheduleBuilder schedule)` |
| `WithDailyTimeIntervalSchedule()` | `WithDailyTimeIntervalSchedule(Action<DailyTimeIntervalScheduleBuilder>? configure = null)` |
| `WithDailyTimeIntervalSchedule(Action<DailyTimeIntervalScheduleBuilder>)` | same member |
| `WithDailyTimeIntervalSchedule(int interval, IntervalUnit unit, Action<…>? action = null)` | `WithDailyTimeIntervalSchedule(x => x.WithInterval(interval, unit))` |
| `WithDailyTimeIntervalSchedule(DailyTimeIntervalScheduleBuilder)` | `WithDailyTimeIntervalSchedule(DailyTimeIntervalScheduleBuilder schedule)` |
| `WithRecurrenceSchedule(string)` | `WithRecurrenceSchedule(string recurrenceRule, Action<RecurrenceScheduleBuilder>? configure = null)` |
| `WithRecurrenceSchedule(string, Action<RecurrenceScheduleBuilder>)` | same member |
| `WithRecurrenceSchedule(RecurrenceScheduleBuilder)` | `WithRecurrenceSchedule(RecurrenceScheduleBuilder schedule)` |

Only two call shapes need editing:

```diff
- .WithDailyTimeIntervalSchedule(interval: 10, intervalUnit: IntervalUnit.Second)
+ .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))

- .WithCronSchedule("0 H H(0-7) * * ?", "nightly-cleanup")
+ .WithCronSchedule(CronScheduleBuilder.CronSchedule(new CronExpression("0 H H(0-7) * * ?", "nightly-cleanup")))
```

The hash-key overloads went because a hash key belongs to the expression, not to the way the expression is
attached to a trigger — `new CronExpression(expr, hashKey)` takes it, and one builder-taking overload carries
the result. Without a key, `H` tokens still hash on the trigger's identity.

### `ITriggerConfigurator` gained a non-generic base

The extensions are written against a new non-generic `ITriggerConfigurator`, which holds the one member they
need:

```csharp
public interface ITriggerConfigurator
{
    ITriggerConfigurator WithSchedule(IScheduleBuilder scheduleBuilder);
}

public interface ITriggerConfigurator<TJob> : ITriggerConfigurator where TJob : IJob { … }
```

The generic interface redeclares `WithSchedule` with its own return type, so a chain there keeps `TJob` and
the job-property overload of `UsingJobData` that comes with it. Code implementing `ITriggerConfigurator<TJob>`
is unaffected; `TriggerBuilder<TJob>` implements both.

## `ModifiedByCalendar` is `WithCalendarName`

```diff
  ITrigger trigger = TriggerBuilder.Create()
      .WithIdentity("trigger1")
-     .ModifiedByCalendar("myHolidays")
+     .WithCalendarName("myHolidays")
      .Build();
```

It sets `ITrigger.CalendarName` and every other setter on the builder is named for the property it sets. The
rename applies to `TriggerBuilder<TJob>` and `ITriggerConfigurator<TJob>` alike. The old name also read as
though it modified the calendar.

## The preferred node is a value

Node affinity was two properties that only made sense read together: a `string?` in which `"*"` meant
something other than a node name, and a `bool` that meant nothing unless the string was one. Copying a pin
from one trigger to another through the setter silently dropped the flag, turning an auto-claim into a named
pin that would never fail over. `Quartz.PreferredNode` — a `readonly record struct` — carries both.

| 3.x | 4.x |
|---|---|
| `string? ITrigger.PreferredNode` | `PreferredNode ITrigger.PreferredNode` |
| `bool ITrigger.IsPreferredNodeAuto` | `trigger.PreferredNode.IsAutomatic` |
| `trigger.PreferredNode` (the node name) | `trigger.PreferredNode.Node` — null for no pin *and* for an unclaimed auto-pin |
| `trigger.PreferredNode is null` | `trigger.PreferredNode.IsNone` |
| `trigger.PreferredNode == "*"` | `trigger.PreferredNode == PreferredNode.Auto` |
| `WithPreferredNode(null)` | `WithPreferredNode(PreferredNode.None)` |
| `WithPreferredNode("*")` | `WithPreferredNode(PreferredNode.Auto)` |
| `WithPreferredNode("node-1")` | `WithPreferredNode(PreferredNode.For("node-1"))` |
| `new TriggerDetailsUpdate().WithPreferredNode(string?)` | `.WithPreferredNode(PreferredNode)` |
| `IMutableTrigger.PreferredNode { get; set; }` → `string?` | → `PreferredNode` |

```diff
- .WithPreferredNode("production-node-1")
+ .WithPreferredNode(PreferredNode.For("production-node-1"))

- string? node = t.PreferredNode;
- bool auto = t.IsPreferredNodeAuto;
+ string? node = t.PreferredNode.Node;
+ bool auto = t.PreferredNode.IsAutomatic;
```

`PreferredNode.For` trims its argument and rejects a blank one and the pinning protocol's own markers (`*`,
`_`, `null`) — names that could never identify a node, and which used to be accepted and then quietly mean
something else. Any other name is legal; the node name is stored verbatim, so `auto:thing` or `*-west` is
fine. Assigning a pin records it as the pin it was, auto-claim included, so copying one between triggers is
lossless.

**Storage is unchanged.** `QRTZ_TRIGGERS.PREFERRED_NODE` and `PREFERRED_NODE_AUTO` still hold the string and
the flag; the mapping happens at the store boundary and the sentinel is an internal constant rather than
something a caller spells. Databases written by 3.19's node-affinity migration or by an earlier 4.0 preview
read back identically, and the JSON trigger payloads never carried the pin.

## Misfire instructions are enums

Each schedule builder had one no-argument method per misfire policy — eighteen of them across five builders,
with a slightly different vocabulary each. A method name is a poor place to keep a value: it cannot be read
from configuration, cannot be switched on, and cannot be defaulted. Every builder now has one
`WithMisfireHandlingInstruction` taking its family's enum.

```diff
  .WithSimpleSchedule(x => x
      .WithInterval(TimeSpan.FromMinutes(5))
      .RepeatForever()
-     .WithMisfireHandlingInstructionNextWithExistingCount())
+     .WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount))
```

### SimpleScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireNow()` | `WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.FireNow)` |
| `WithMisfireHandlingInstructionNowWithExistingCount()` | `WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.NowWithExistingCount)` |
| `WithMisfireHandlingInstructionNowWithRemainingCount()` | `WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.NowWithRemainingCount)` |
| `WithMisfireHandlingInstructionNextWithRemainingCount()` | `WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount)` |
| `WithMisfireHandlingInstructionNextWithExistingCount()` | `WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount)` |
| (call nothing) | `WithMisfireHandlingInstruction(SimpleTriggerMisfireInstruction.SmartPolicy)`, still the default |

### CronScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireHandlingInstruction(CronTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireHandlingInstruction(CronTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireHandlingInstruction(CronTriggerMisfireInstruction.DoNothing)` |

### CalendarIntervalScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireHandlingInstruction(CalendarIntervalTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireHandlingInstruction(CalendarIntervalTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireHandlingInstruction(CalendarIntervalTriggerMisfireInstruction.DoNothing)` |

### DailyTimeIntervalScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireHandlingInstruction(DailyTimeIntervalTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireHandlingInstruction(DailyTimeIntervalTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireHandlingInstruction(DailyTimeIntervalTriggerMisfireInstruction.DoNothing)` |

### RecurrenceScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireHandlingInstruction(RecurrenceTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireHandlingInstruction(RecurrenceTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireHandlingInstruction(RecurrenceTriggerMisfireInstruction.DoNothing)` |

### The enums and the constants are the same numbers

An enum member's underlying value *is* the `MisfireInstruction` constant it replaces, so the two convert
freely:

```csharp
CronTriggerMisfireInstruction policy = (CronTriggerMisfireInstruction) trigger.MisfireInstruction;
int stored = (int) CronTriggerMisfireInstruction.DoNothing;   // MisfireInstruction.CronTrigger.DoNothing
```

`ITrigger.MisfireInstruction` and `TriggerDetailsUpdate.WithMisfireInstruction(int)` stay `int`, and the
`MisfireInstruction` static class stays as the storage-level reference. Neither a trigger's own storage nor an
update object knows which family it belongs to. The enums are the same values offered where the family *is*
known — on a schedule builder.

## Intervals are said once per builder

Every schedule builder had a `WithIntervalIn<Unit>` method per unit alongside a general `WithInterval`. The
per-unit methods are gone; the general one stays.

### SimpleScheduleBuilder — `WithInterval(TimeSpan)`

| 3.x | 4.x |
|---|---|
| `WithIntervalInSeconds(n)` | `WithInterval(TimeSpan.FromSeconds(n))` |
| `WithIntervalInMinutes(n)` | `WithInterval(TimeSpan.FromMinutes(n))` |
| `WithIntervalInHours(n)` | `WithInterval(TimeSpan.FromHours(n))` |

### CalendarIntervalScheduleBuilder — `WithInterval(int, IntervalUnit)`

| 3.x | 4.x |
|---|---|
| `WithIntervalInSeconds(n)` | `WithInterval(n, IntervalUnit.Second)` |
| `WithIntervalInMinutes(n)` | `WithInterval(n, IntervalUnit.Minute)` |
| `WithIntervalInHours(n)` | `WithInterval(n, IntervalUnit.Hour)` |
| `WithIntervalInDays(n)` | `WithInterval(n, IntervalUnit.Day)` |
| `WithIntervalInWeeks(n)` | `WithInterval(n, IntervalUnit.Week)` |
| `WithIntervalInMonths(n)` | `WithInterval(n, IntervalUnit.Month)` |
| `WithIntervalInYears(n)` | `WithInterval(n, IntervalUnit.Year)` |

### DailyTimeIntervalScheduleBuilder — `WithInterval(int, IntervalUnit)`

| 3.x | 4.x |
|---|---|
| `WithIntervalInSeconds(n)` | `WithInterval(n, IntervalUnit.Second)` |
| `WithIntervalInMinutes(n)` | `WithInterval(n, IntervalUnit.Minute)` |
| `WithIntervalInHours(n)` | `WithInterval(n, IntervalUnit.Hour)` |

A calendar interval and a simple interval are genuinely different things — a `TimeSpan` is a fixed amount of
time, while `1, IntervalUnit.Month` is however long the next month happens to be — so the two shapes differ on
purpose.

## `SimpleScheduleBuilder`'s twelve `Repeat*` factories are gone

Three units × forever-or-a-count × with-or-without an explicit count, twelve static factories in all, each of
which built a `TimeSpan` and set a repeat count.

| 3.x | 4.x |
|---|---|
| `SimpleScheduleBuilder.RepeatSecondlyForever()` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(1)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatSecondlyForever(n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(n)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatMinutelyForever()` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatMinutelyForever(n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(n)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatHourlyForever()` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(1)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatHourlyForever(n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(n)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatSecondlyForTotalCount(c)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(1)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatSecondlyForTotalCount(c, n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(n)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatMinutelyForTotalCount(c)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(1)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatMinutelyForTotalCount(c, n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(n)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatHourlyForTotalCount(c)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatHourlyForTotalCount(c, n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(n)).WithRepeatCount(c - 1)` |

::: warning
Mind the `- 1` in the `ForTotalCount` rows. The repeat count is one fewer than the number of firings, because
the trigger also fires at its start time — `RepeatMinutelyForTotalCount(3)` fires three times, and the
equivalent repeat count is 2. This subtraction is the only thing the old factories said that `WithInterval`
does not, and it is now said on `WithRepeatCount`, where the trigger says it.
:::

Inside a `WithSimpleSchedule` delegate you never needed the factories at all:

```diff
- .WithSchedule(SimpleScheduleBuilder.RepeatMinutelyForever(5))
+ .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())
```

## `CronScheduleBuilder`'s convenience factories are gone

`CronSchedule(string)` and `CronSchedule(CronExpression)` stay. The six factories that assembled an expression
from numbers are replaced by `CronExpressionBuilder`, which names each field instead of relying on argument
order — the old set used three different orders for the same three numbers.

| 3.x | 4.x |
|---|---|
| `CronScheduleBuilder.DailyAtHourAndMinute(h, m)` | `CronScheduleBuilder.CronSchedule($"0 {m} {h} ? * *")` |
| `CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(h, m, days)` | `CronScheduleBuilder.CronSchedule(CronExpressionBuilder.Create().WithSecond(0).WithMinute(m).WithHour(h).OnDaysOfWeek(days).Build())` |
| `CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(day, h, m)` | `CronScheduleBuilder.CronSchedule(CronExpressionBuilder.Create().WithSecond(0).WithMinute(m).WithHour(h).OnDaysOfWeek(day).Build())` |
| `CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(dom, h, m)` | `CronScheduleBuilder.CronSchedule(CronExpressionBuilder.Create().WithSecond(0).WithMinute(m).WithHour(h).WithDayOfMonth(dom).Build())` |
| `CronScheduleBuilder.CronScheduleWithHash(expr, hashKey)` | `CronScheduleBuilder.CronSchedule(new CronExpression(expr, hashKey))` |
| `CronScheduleBuilder.CronScheduleWithHash(expr, hashSeed)` | `CronScheduleBuilder.CronSchedule(new CronExpression(expr, hashSeed))` |

```diff
- .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(9, 30))
+ .WithCronSchedule("0 30 9 ? * *")
```

## Day selection on `DailyTimeIntervalScheduleBuilder`

The two `OnDaysOfTheWeek` overloads are one C# 13 params collection, so both old call shapes still compile:

```csharp
x.OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Wednesday);   // still fine
x.OnDaysOfTheWeek(daysFromConfiguration);                   // still fine
```

The three `public static readonly` day sets are gone. They existed only to be handed straight back to
`OnDaysOfTheWeek`, which the named methods already do:

| 3.x | 4.x |
|---|---|
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek)` | `OnEveryDay()` — also the default when you say nothing |
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.MondayThroughFriday)` | `OnMondayThroughFriday()` |
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.SaturdayAndSunday)` | `OnSaturdayAndSunday()` |

If you used a set for something other than the builder, `Enum.GetValues<DayOfWeek>()` is the whole week.

## `InTimeZone` is nullable everywhere

`CronScheduleBuilder` and `DailyTimeIntervalScheduleBuilder` declared `InTimeZone(TimeZoneInfo)` while
`CalendarIntervalScheduleBuilder` and `RecurrenceScheduleBuilder` declared `InTimeZone(TimeZoneInfo?)`, so
passing a zone that may be absent needed a `!` on two of the four. All four — and `DateBuilder.InTimeZone` —
now take `TimeZoneInfo?`. `null` means what it always meant: the system's local time zone.

```diff
- .InTimeZone(configuredZone!)
+ .InTimeZone(configuredZone)
```

## `ScheduleBuilder<T>` is gone

The five schedule builders implement `IScheduleBuilder` directly. The abstract base declared one member that
`IScheduleBuilder` already declares, and nothing ever used its type parameter. A schedule builder of your own
implements the interface and drops the `override`:

```diff
- private sealed class MyScheduleBuilder : ScheduleBuilder<MyTrigger>
+ private sealed class MyScheduleBuilder : IScheduleBuilder
  {
-     public override IMutableTrigger Build() => new MyTrigger();
+     public IMutableTrigger Build() => new MyTrigger();
  }
```

## `DateBuilder`'s static factories are gone

The fluent API is unchanged: `DateBuilder.NewDate()`, `NewDateInTimeZone()`, the `At*`/`On*`/`In*` setters and
`Build()`. The seventeen statics were doing two unrelated jobs under one name — naming a specific date, which
the fluent API does, and arithmetic on a `DateTimeOffset`, which `DateTimeOffset` does.

### Naming a date

| 3.x | 4.x |
|---|---|
| `DateBuilder.DateOf(h, m, s)` | `DateBuilder.NewDate().AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.TodayAt(h, m, s)` | `DateBuilder.NewDate().AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.DateOf(h, m, s, day, month)` | `DateBuilder.NewDate().InMonthOnDay(month, day).AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.DateOf(h, m, s, day, month, year)` | `DateBuilder.NewDate().InYear(year).InMonthOnDay(month, day).AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.TomorrowAt(h, m, s)` | `DateBuilder.NewDate().AtHourMinuteAndSecond(h, m, s).Build().AddDays(1)` |

Note that `InMonthOnDay` takes the month first, where `DateOf` took the day first.

### Now plus something

| 3.x | 4.x |
|---|---|
| `DateBuilder.FutureDate(n, IntervalUnit.Second)` | `DateTimeOffset.UtcNow.AddSeconds(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Minute)` | `DateTimeOffset.UtcNow.AddMinutes(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Hour)` | `DateTimeOffset.UtcNow.AddHours(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Day)` | `DateTimeOffset.UtcNow.AddDays(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Week)` | `DateTimeOffset.UtcNow.AddDays(n * 7)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Month)` | `DateTimeOffset.UtcNow.AddMonths(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Year)` | `DateTimeOffset.UtcNow.AddYears(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Millisecond)` | `DateTimeOffset.UtcNow.AddMilliseconds(n)` |

### Rounding

Each of these was one line of `DateTimeOffset` construction:

| 3.x | 4.x |
|---|---|
| `DateBuilder.EvenSecondDateBefore(d)` | `new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Offset)` |
| `DateBuilder.EvenSecondDate(d)` | the same, on `d.AddSeconds(1)` |
| `DateBuilder.EvenSecondDateAfterNow()` | the same, on `DateTimeOffset.Now.AddSeconds(1)` |
| `DateBuilder.EvenMinuteDateBefore(d)` | `new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset)` |
| `DateBuilder.EvenMinuteDate(d)` | the same, on `d.AddMinutes(1)` |
| `DateBuilder.EvenMinuteDateAfterNow()` | the same, on `DateTimeOffset.Now.AddMinutes(1)` |
| `DateBuilder.EvenHourDateBefore(d)` | `new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, d.Offset)` |
| `DateBuilder.EvenHourDate(d)` | the same, on `d.AddHours(1)` |
| `DateBuilder.EvenHourDateAfterNow()` | the same, on `DateTimeOffset.Now.AddHours(1)` |
| `DateBuilder.NextGivenMinuteDate(d, minuteBase)` | round `d` up to the next multiple of `minuteBase` minutes |
| `DateBuilder.NextGivenSecondDate(d, secondBase)` | round `d` up to the next multiple of `secondBase` seconds |

Most start times do not need the rounding at all. A trigger that starts at an arbitrary instant and repeats
every minute keeps the same schedule as one whose start was rounded up first — it just begins a fraction of a
second earlier. Reach for rounding when you want the *displayed* times to be tidy, and write the line where
you want it.

## `TimeOfDay` became `TimeOnly`

The hand-written `TimeOfDay` class predates `System.TimeOnly`. It is gone, and `IDailyTimeIntervalTrigger`
and `DailyTimeIntervalScheduleBuilder` speak `TimeOnly`.

| 3.x | 4.x |
|---|---|
| `TimeOfDay.HourAndMinuteOfDay(8, 0)` | `new TimeOnly(8, 0)` |
| `TimeOfDay.HourMinuteAndSecondOfDay(8, 0, 30)` | `new TimeOnly(8, 0, 30)` |
| `new TimeOfDay(8, 0)` / `new TimeOfDay(8, 0, 30)` | `new TimeOnly(8, 0)` / `new TimeOnly(8, 0, 30)` |
| `IDailyTimeIntervalTrigger.StartTimeOfDay` returning `TimeOfDay` | returning `TimeOnly` |
| `IDailyTimeIntervalTrigger.EndTimeOfDay` returning `TimeOfDay` | returning `TimeOnly` |
| `StartingDailyAt(TimeOfDay)` / `EndingDailyAt(TimeOfDay)` | `StartingDailyAt(TimeOnly)` / `EndingDailyAt(TimeOnly)` |
| `a.Before(b)` | `a < b` |
| `timeOfDay.GetTimeOfDayForDate(date)` | `new DateTimeOffset(date.Date, date.Offset).Add(timeOfDay.ToTimeSpan())` |

```csharp
// 3.x
.WithDailyTimeIntervalSchedule(x => x
    .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
    .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 0)))

// 4.x
.WithDailyTimeIntervalSchedule(x => x
    .StartingDailyAt(new TimeOnly(8, 0))
    .EndingDailyAt(new TimeOnly(17, 0)))
```

Two things to know:

* The two properties are non-nullable now. A `TimeOnly` is a struct, and the defaults are the ones the
  builder always applied anyway — `00:00:00` and `23:59:59`.
* A value with sub-second precision is rejected with an `ArgumentException`. The job store keeps the window
  in hour, minute and second columns, so `TimeOnly.FromDateTime(DateTime.Now)` would lose its fractional part
  the moment the trigger were persisted. Round it yourself if that is what you meant.

Nothing about storage changed: the `SIMPROP_INT_PROP` columns are the same, and the JSON
`StartTimeOfDay`/`EndTimeOfDay` objects keep their `{ Hour, Minute, Second }` shape, so existing triggers
load unchanged.

## `DailyCalendar` takes two `TimeOnly` values

Eight constructors and four `SetTimeRange` overloads described one pair of times four different ways. One
constructor and one property replace them.

| 3.x | 4.x |
|---|---|
| `new DailyCalendar("08:00", "17:00")` | `new DailyCalendar(new TimeOnly(8, 0), new TimeOnly(17, 0))` |
| `new DailyCalendar("08:00:00:500", "17:00:00:000")` | `new DailyCalendar(new TimeOnly(8, 0, 0, 500), new TimeOnly(17, 0))` |
| `new DailyCalendar(baseCal, "08:00", "17:00")` | `new DailyCalendar(new TimeOnly(8, 0), new TimeOnly(17, 0), baseCal)` |
| `new DailyCalendar(8, 0, 0, 0, 17, 0, 0, 0)` | `new DailyCalendar(new TimeOnly(8, 0), new TimeOnly(17, 0))` |
| `new DailyCalendar(startDateTime, endDateTime)` | `new DailyCalendar(TimeOnly.FromDateTime(startDateTime), TimeOnly.FromDateTime(endDateTime))` |
| `new DailyCalendar(startTicks, endTicks)` | `new DailyCalendar(new TimeOnly(startTicks), new TimeOnly(endTicks))` |
| `calendar.SetTimeRange(...)` (four overloads) | `calendar.TimeRange = (start, end)` |
| `calendar.RangeStartingTime` (a string) | `calendar.TimeRange.Start` |
| `calendar.RangeEndingTime` (a string) | `calendar.TimeRange.End` |

The `"HH:MM:SS:mmm"` string form — note the colon before the milliseconds — was a format nothing else in
.NET parses. `InvertTimeRange`, `GetTimeRangeStartingTimeUtc` and `GetTimeRangeEndingTimeUtc` are unchanged.
The constructor no longer takes a `TimeProvider`; it only ever used one to check that the range starts
before it ends, which two `TimeOnly` values answer directly. Precision finer than a millisecond is rejected,
matching what the calendar's serialized form can carry.

Persisted `DailyCalendar` blobs load unchanged: the serializers write `RangeStart`/`RangeEnd` now but still
read the old `RangeStartingTime`/`RangeEndingTime` strings.

## Excluded days are a read-only set

The four day-excluding calendars had four idioms for one idea. They now share one: a read-only set of the
thing being excluded, plus `AddExcludedDay` and `RemoveExcludedDay`, which return whether the set changed.

| 3.x | 4.x |
|---|---|
| `AnnualCalendar.DaysExcluded` as a settable `IReadOnlyCollection<DateTime>` | `IReadOnlySet<DateOnly>`, get-only |
| `annual.SetDayExcluded(day, true)` | `annual.AddExcludedDay(DateOnly)` |
| `annual.SetDayExcluded(day, false)` | `annual.RemoveExcludedDay(DateOnly)` |
| `annual.IsDayExcluded(DateTimeOffset)` | `annual.IsDayExcluded(DateOnly)` |
| `HolidayCalendar.ExcludedDates` as a `List<DateTime>` copy | `HolidayCalendar.DaysExcluded` as `IReadOnlySet<DateOnly>` |
| `holiday.AddExcludedDate(DateTime)` | `holiday.AddExcludedDay(DateOnly)` |
| `holiday.RemoveExcludedDate(DateTime)` | `holiday.RemoveExcludedDay(DateOnly)` |
| (nothing) | `holiday.IsDayExcluded(DateOnly)` |
| `MonthlyCalendar.DaysExcluded` as a settable `bool[31]` | `IReadOnlySet<int>`, get-only, days 1 through 31 |
| `monthly.SetDayExcluded(15, true)` / `(15, false)` | `monthly.AddExcludedDay(15)` / `monthly.RemoveExcludedDay(15)` |
| `WeeklyCalendar.DaysExcluded` as a settable `bool[7]` | `IReadOnlySet<DayOfWeek>`, get-only |
| `weekly.SetDayExcluded(DayOfWeek.Friday, true)` / `(…, false)` | `weekly.AddExcludedDay(DayOfWeek.Friday)` / `weekly.RemoveExcludedDay(DayOfWeek.Friday)` |
| `CronCalendar.SetCronExpressionString(expr)` | `cron.CronExpression = new CronExpression(expr)` |

```csharp
// 3.x
var holidays = new HolidayCalendar();
holidays.AddExcludedDate(new DateTime(2025, 12, 25));

var weekends = new WeeklyCalendar();
weekends.SetDayExcluded(DayOfWeek.Friday, true);

// 4.x
var holidays = new HolidayCalendar();
holidays.AddExcludedDay(new DateOnly(2025, 12, 25));

var weekends = new WeeklyCalendar();
weekends.AddExcludedDay(DayOfWeek.Friday);
```

Two behaviors worth knowing:

* `AnnualCalendar` still only cares about the month and the day. It normalizes what you give it onto a fixed
  year, so `DaysExcluded` reads back with that year rather than the one you passed, and `IsDayExcluded`
  answers the same for every year.
* `AnnualCalendar.IsDayExcluded` now answers only about the calendar's own set. The base calendar is
  consulted by `IsTimeIncluded`, which is the member that asks a question about an instant.

`MonthlyCalendar.AreAllDaysExcluded` and `WeeklyCalendar.AreAllDaysExcluded` are unchanged, and a fresh
`WeeklyCalendar` still starts out excluding Saturday and Sunday.

Existing calendar blobs load unchanged. Both serializers write the new shapes and read the old ones: an
`ExcludedDays`/`ExcludedDates` array may hold timestamps or dates, and per-day booleans or day numbers or
day names.

## `DirtyFlagMap` dropped the non-generic collection interfaces

`DirtyFlagMap<TKey, TValue>` no longer implements `System.Collections.IDictionary` or
`System.Collections.ICollection`. Those duplicated the generic interfaces with untyped members that cast at
runtime — `Add(object, object)` and the `object` indexer threw `InvalidCastException` for a key of the wrong
type instead of `ArgumentException` (#1417), and `SyncRoot` handed out a lock object the map never took.

| 3.x | 4.x |
|---|---|
| `((IDictionary) map).Add(key, value)` | `map.Add(key, value)` |
| `((IDictionary) map)[key]` | `map[key]` |
| `((IDictionary) map).Contains(key)` | `map.ContainsKey(key)` |
| `((IDictionary) map).Remove(key)` | `map.Remove(key)` |
| `map.CopyTo(array, index)` (`Array`) | `map.CopyTo(KeyValuePair<TKey, TValue?>[], index)` |
| `new JobDataMap(someIDictionary)` | `new JobDataMap(someIDictionaryOfStringToObject)` |

`ISerializable` is untouched, so persisted maps still load. The generic
`JobDataMap(IDictionary<string, object?>)` constructor also took over what the removed non-generic one did
with a `QRTZ_FORCE_JOB_DATAMAP_DIRTY` entry: the entry is not copied, and the new map is left flagged dirty.

`StringKeyDirtyFlagMap` gained `GetDecimal` and `TryGetDecimal`, so a `decimal` in a job data map can now be
read back the way every other primitive can.

## Other Breaking Changes

| Change | Details |
|--------|---------|
| `SimpleTriggerImpl` `endUtc` no longer nullable | The constructor argument is now required |
| `QuartzScheduler` and `QuartzSchedulerResources` are internal | Resolve `IScheduler` / `ISchedulerFactory`; scheduler-wide settings are `QuartzSchedulerOptions` |
| `JobType` introduced | Stores job type info without requiring an actual `Type` instance |
| `RecoveringTriggerKey` behavior | `IJobExecutionContext.RecoveringTriggerKey` now returns `null` when not recovering instead of throwing |
| `DictionaryExtensions` removed | `Quartz.Util.DictionaryExtensions` type was removed |
| `JobStoreSupport` connection methods | `GetLocalTransactionConnection` (was `GetNonManagedTXConnection`) and `GetConnection` now return `ValueTask<ConnectionAndTransactionHolder>` |
| `JobStoreSupport.UseProperties` `string` setter removed | The `bool` `AdoJobStoreOptions.UseProperties` option and the read-only `CanUseProperties` remain; the property bridge parses the key |
| Protected `JobStoreSupport` / `StdAdoDelegate` members take a `CancellationToken` | Overrides have to add the parameter; callers do not |
| `ConnectionAndTransactionHolder.Close` takes a `CancellationToken` | `.Commit` and `.Rollback` took one too, and are now internal — see [A job store of your own can join your transaction](#a-job-store-of-your-own-can-join-your-transaction) |
| `IJobConfigurator<TJob>` members return `IJobConfigurator<TJob>` | `JobBuilder<TJob>` implements them explicitly and keeps its own `JobBuilder<TJob>`-returning members, so `JobBuilder.Create()…` chains are unaffected — see [Job data can name the property](#job-data-can-name-the-property) for the type parameter |
| `UsingJobData` takes an `object?` | The nine primitive overloads collapsed into one — see [Nine `UsingJobData` overloads became one](#nine-usingjobdata-overloads-became-one) |
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
| `ScheduleBuilder<T>` removed | The five schedule builders implement `IScheduleBuilder` directly — see [`ScheduleBuilder<T>` is gone](#schedulebuilder-t-is-gone) |
| `DailyTimeIntervalScheduleBuilder`'s day-set fields are internal | `AllDaysOfTheWeek`, `MondayThroughFriday` and `SaturdayAndSunday` are reached through `OnEveryDay()`, `OnMondayThroughFriday()` and `OnSaturdayAndSunday()` |
| `PreserveHourOfDayAcrossDaylightSavings` and `SkipDayIfHourDoesNotExist` default to `true` | Turning the flag on reads as a call with no argument; passing the value still works |
| `TimeOfDay` removed | `TimeOnly` replaces it — see [`TimeOfDay` became `TimeOnly`](#timeofday-became-timeonly) |
| `DailyCalendar` has one constructor | Two `TimeOnly` values and an optional base calendar — see [`DailyCalendar` takes two `TimeOnly` values](#dailycalendar-takes-two-timeonly-values) |
| Calendar `SetDayExcluded` / `AddExcludedDate` removed | `AddExcludedDay` / `RemoveExcludedDay` over a read-only set — see [Excluded days are a read-only set](#excluded-days-are-a-read-only-set) |
| `CronCalendar.SetCronExpressionString` removed | Assign `CronExpression` instead; the property already accepted a parsed expression |
| `JobDataMap(IDictionary)` removed | `JobDataMap(IDictionary<string, object?>)` remains and absorbed the dirty-marker handling |
| `StringKeyDirtyFlagMap.GetDecimal` / `TryGetDecimal` added | A `decimal` could be written but not read back |
| `ISchedulerFactory.GetAllSchedulers` returns `ValueTask<List<IScheduler>>` | Quartz returns concrete collection types from its query members for allocation and enumeration cost; this was the one that did not |
| `IInstanceIdGenerator.GenerateInstanceId` returns `ValueTask<string>` | It never returned null, and a null instance id is not a usable one |
| An `IJobStore` that implements `IJobListener` no longer receives events automatically | Register it as a job listener through the scheduler's `IListenerManager` |
| `[Serializable]` removed from `TriggerFiredBundle` and `TriggerFiredResult` | It has meant nothing since binary serialization was dropped |
| `XmlSchedulingOptions` and `JsonSchedulingOptions` merged | They were byte-for-byte identical and are now one type |
| Constructing a scheduler no longer starts a thread | `QuartzScheduler` starts its scheduler thread from `Start()` rather than its constructor, so resolving the service graph, running a `ValidateOnBuild` pass or asserting on registrations no longer spins one up. The thread always started paused, so this changes when the thread exists, not when jobs run |
| `IPersistentStoreBuilder.AcceptEnlistedTransactions()` added | A breaking addition for anyone implementing the interface themselves — see [Joining an existing transaction](tutorial/job-stores.md#joining-an-existing-transaction) |
| Group matchers translate to SQL correctly | `SelectTriggerGroups`, `DeletePausedTriggerGroup` and both `UpdateTriggerGroupStateFromOtherState(s)` members always built a `LIKE`, even for an equality matcher; they take the `=` path now, which is exact and index-friendly. `LIKE` patterns escape `%`, `_` and the escape character in the matcher's own text with an explicit `ESCAPE` clause, so a group literally named `50%` matches itself. The escape character is `!` rather than a backslash, because MySQL applies C-style escaping inside string literals and `ESCAPE '\'` is a syntax error there |
| `StdAdoConstants` group and fired-trigger statements were split | `SqlDeletePausedTriggerGroup`, `SqlSelectTriggerGroupsFiltered`, `SqlUpdateTriggerGroupStateFromState` and `SqlUpdateTriggerGroupStateFromStates` are `…Equals` / `…Like` pairs, and the FIRED_TRIGGERS statements are one `SqlSelectFiredTriggers` / `SqlDeleteFiredTriggers` base plus `SqlFiredTrigger*Predicate` fragments. The type is internal |
| `IDashboardAuthorizationFilter` and `QuartzDashboardOptions.AuthorizationFilter` removed | Nothing ever invoked the filter, so setting it bought a false sense of security. Use `AuthorizationPolicy`, which is enforced |
| `IDashboardHistoryStore` is asynchronous | `ValueTask Add`, `ValueTask<DashboardHistoryPage> GetPage`, so a store can talk to a database. `SearchFilter.DebounceMilliseconds` is a `TimeSpan Debounce`, and `QuartzApiClient` / `InProcessQuartzApiClient` are internal — resolve `IQuartzApiClient` |
| Serializers outside a scheduler read a container-wide registry | Because the serializer maps are per-serializer, the HTTP API, the dashboard and `Quartz.HttpClient` read a `SystemTextJsonSerializerRegistry` registered in the container. Register it as a singleton to make a custom serializer visible to them |
| `IDriverDelegate` trigger states are `StoredTriggerState` | Eighteen members took the state as a `string`; the database still stores the same values — see [Trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate) |
| The `…FromOtherStates` members take a state collection | Two or three fixed old-state parameters became one `IReadOnlyCollection<StoredTriggerState>` |
| `FiredTriggerQuery.InstanceName` is `InstanceId` | With the `instanceName` parameters of the scheduler-state members; the `INSTANCE_NAME` column is unchanged |
| `IDriverDelegate.IsJobCurrentlyExecuting` takes a `JobKey` | It took `(string jobName, string jobGroup)` |
| `IDriverDelegate.SelectJobForTrigger`'s `loadJobType` is required | It defaulted in front of the cancellation token; pass `loadJobType: true` for the old default |
| `IDriverDelegate.UpdateTriggerPreferredNodeConditional` takes a `PreferredNodeTransition` | Four loose compare-and-swap parameters became one record naming `Expected` and `New` |
| `JobStoreTX` is `LocalTransactionJobStore`, `JobStoreCMT` is `ExternalTransactionJobStore` | The names now say whose transaction the store uses. `quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz` and the `JobStoreCMT` spelling still resolve, with a warning — see [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use) |
| `GetNonManagedTXConnection`, `ExecuteInNonManagedTXLock`, `RetryExecuteInNonManagedTXLock` renamed | `GetLocalTransactionConnection`, `ExecuteInLocalTransactionLock`, `RetryExecuteInLocalTransactionLock`; protected, so only a `JobStoreSupport` subclass sees them |
| `JobStoreSupport`'s nine `Execute…Lock` overloads became four members | Optional parameters replace the ladder, and no member returns `object` as a stand-in for `void` any more — see [Nine `Execute…Lock` overloads became four members](#nine-execute-lock-overloads-became-four-members) |
| `ExternalTransactionJobStore.OpenConnection` is `{ get; set; }` | It was `{ protected get; set; }`: writable from anywhere, readable only from inside |
| `ISemaphore` takes a `SchedulerLock` | The `string lockName` had two legal values. The `LOCK_NAME` column and the Redis keys are unchanged — see [Locks are a `SchedulerLock`, not a string](#locks-are-a-schedulerlock-not-a-string) |
| `JobStoreSupport.LockTriggerAccess` / `.LockStateAccess` removed | `SchedulerLock.TriggerAccess` / `.StateAccess` replace the two protected constants |
| ~25 `JobStoreSupport` configuration properties are read-only | They duplicated `AdoJobStoreOptions` / `QuartzSchedulerOptions`; configure the options instead. `MisfireThreshold` deliberately stays settable — see [The job store configuration is read-only](#the-job-store-configuration-is-read-only) |
| `JobStoreSupport.DriverDelegateType` and `.DontSetAutoCommitFalse` removed | Nothing read either one; the driver delegate is injected |
| `JobStoreSupport.LastCheckin` is internal, `LogWarnIfNonZero` is private | Cluster check-in bookkeeping and a logging helper, neither of them an extension point |
| `JobStoreSupport.RecoverJobs(CancellationToken)` returns `ValueTask` | The `bool` it returned was the constant `true` |
| `DBSemaphore.Sql` and `.InsertSql` are get-only, fed by the constructor | Assigning one after construction left it un-prefixed relative to its pair — see [The semaphores were tidied](#the-semaphores-were-tidied) |
| Row-lock semaphore SQL fields are `protected` and consistently named | `UpdateLockRowSemaphore.SqlUpdateForLock` / `.SqlInsertLock` are `UpdateForLock` / `InsertLock`; `StdRowLockSemaphore.SelectForLock` / `.InsertLock` keep their names |
| `JobStoreSupport.GetEnlistedConnection` is `protected` | So a job store outside the core assembly can honour an enlisted transaction rather than silently opening its own connection |
| `ConnectionAndTransactionHolder` gained an ownership-aware constructor and `OwnsResources` | `(connection, transaction, ownsResources)` for a store running on a connection it did not open |
