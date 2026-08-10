---

title: Migration Guide
---

*This document outlines changes needed when upgrading from Quartz.NET 3.x to 4.x. You should also check [the release notes](https://github.com/quartznet/quartznet/releases) for each version.*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)
:::

## The road from 3.x, phase by phase

The 4.0 API is the result of six passes over the public surface, each with its own theme. The guide
below is organised by topic rather than by pass, so this is the map: what changed, in the order it is
worth working through when migrating.

**1. The extensibility contracts.** The interfaces you implement rather than call. `Quartz.Spi` is
[`Quartz.Extensibility` and `Quartz.Simpl` is `Quartz.Impl`](#quartz-spi-and-quartz-simpl-were-renamed);
every asynchronous member returns [`ValueTask`](#tasks-changed-to-valuetask) and ends with a
`CancellationToken`; [jobs take that token as a parameter](#jobs-take-a-cancellationtoken); the
[job factory hands out a scope](#the-job-factory-hands-out-a-scope) instead of an instance; the
[thread pool is asynchronous](#the-thread-pool-is-asynchronous); and
[trigger fire times are properties](#trigger-fire-times-are-properties) rather than getter/setter
pairs. Start here: everything else assumes these signatures.

**2. The vocabulary and the surface.** One word per concept, and nothing public that was never a
contract. [Names that were normalized](#names-that-were-normalized),
[the scheduler and the job store speak the same verbs](#the-scheduler-and-the-job-store-speak-the-same-verbs),
[matchers moved to `Quartz`](#matchers-moved-to-quartz),
[`Key<T>` moved to `Quartz` and is immutable](#key-t-moved-to-quartz-and-is-immutable),
[`SchedulerMetadata` replaces `SchedulerMetaData`](#schedulermetadata-replaces-schedulermetadata),
[the listener API](#listener-api-changes), and
[sealed and internalized types](#sealed-and-internalized-types).

**3. Listings, schedules and dates.** [Job store listings became queries](#job-store-listings-became-queries)
returning `PagedResult<T>` of headers; [misfire instructions are enums](#misfire-instructions-are-enums);
[intervals are said once per builder](#intervals-are-said-once-per-builder);
[`TimeOfDay` became `TimeOnly`](#timeofday-became-timeonly) and
[`DateBuilder`'s static factories are gone](#datebuilder-s-static-factories-are-gone);
[`Executing` is a trigger state](#executing-is-a-trigger-state); and
[the preferred node is a value](#the-preferred-node-is-a-value).

**4. The ADO.NET job store.** [Trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate);
[the stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use);
[nine `Execute…Lock` overloads became four](#nine-execute-lock-overloads-became-four-members);
[locks are a `SchedulerLock`](#locks-are-a-schedulerlock-not-a-string);
[the job store configuration is read-only](#the-job-store-configuration-is-read-only);
[the driver delegate speaks in records](#the-driver-delegate-speaks-in-records);
[the optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone)
and the [schema migration](#database-schema-migration) that goes with that is mandatory;
[`RAMJobStore` is sealed](#ramjobstore-is-sealed); and
[a job store of your own can join your transaction](#a-job-store-of-your-own-can-join-your-transaction).

**5. Configuration and hosting.** The container builds the scheduler:
[`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone),
[the standalone builder is the same builder](#the-standalone-builder-is-the-same-builder),
[there is no process-global scheduler state](#no-process-global-scheduler-or-connection-state),
[`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container),
[one shape per registration method](#one-shape-per-registration-method),
[clustering is configured in one place](#clustering-is-configured-in-one-place),
[the hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler), and
[job execution metrics](#job-execution-metrics) are published by every scheduler.

**6. Serialization policy and the last edges.**
[`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it);
[the two exceptions moved out of `Quartz.Core`](#the-two-exceptions-moved-out-of-quartz-core);
[execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen);
[interruption has two names, not three](#interruption-has-two-names-not-three); and the naming and
visibility odds and ends gathered in [Other Breaking Changes](#other-breaking-changes) at the end of
this guide.

Two differences run through all six and are not called out case by case, because they are almost
universal: `Task` became `ValueTask` on nearly every member, and the namespaces moved as described in
pass 1. If a type you used does not appear in this guide at all, check
[Package Changes](#package-changes) first — it may have moved packages rather than changed.

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
embedded resource. A scheduler is configured by the properties you hand to
`QuartzSchedulerBuilder.UseProperties`, by an `IConfiguration` section passed to `AddQuartz`, or in code
through the container.

The three settings the embedded file supplied are not defaults any more either. They were seeded by
`StdSchedulerFactory.Initialize()`, which is the only entry point that ever read the file, and which is
gone with the factory — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone):

| Setting | Old embedded value | Default now |
|---|---|---|
| `quartz.scheduler.instanceName` | `DefaultQuartzScheduler` | `QuartzScheduler` (`QuartzSchedulerOptions.InstanceName`) |
| `quartz.threadPool.threadCount` | 10 | 10 (`ThreadPoolOptions.MaxConcurrency`) |
| `quartz.jobStore.misfireThreshold` | 60000 | 5 seconds (`InMemoryJobStoreOptions.MisfireThreshold`) |

Note these were never the defaults for `AddQuartz` or for `new StdSchedulerFactory(properties)` in the
first place: handing the factory properties always bypassed the file, so those paths fell back — and
still fall back — to the typed option defaults. Set them explicitly if you want the other values.

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
the callback above, once per driver.
### `QuartzOptions` is no longer a dictionary

`QuartzOptions` used to derive from `Dictionary<string, string?>`, and to hold a scheduler's jobs and
triggers as well as its flat keys. It was the pivot the whole configuration model turned on; now that
settings are typed options, the only things left in it were the legacy keys and the one thing that was
never configuration at all. The keys moved to a `Properties` dictionary, and jobs and triggers became a
per-scheduler registration like every other part of a scheduler.

```diff
  services.Configure<QuartzOptions>(options =>
  {
-     options["quartz.plugin.jobHistory.type"] = "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins";
+     options.Properties["quartz.plugin.jobHistory.type"] = "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins";
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

`QuartzOptions.SchedulerName` used to read and write `schedName` — an ADO.NET column key that nothing
reads — so a scheduler name set through it was accepted and then silently ignored. It is gone along with
`SchedulerId` and `MisfireThreshold`; see
[`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings).

### Removed

| Removed | Use instead |
|---|---|
| `QuartzOptions : Dictionary<string, string?>` | `QuartzOptions.Properties` |
| `QuartzOptions.JobDetails`, `.Triggers`, `.AddJob`, `.AddTrigger` | `AddQuartz(q => q.AddJob(…))` / `q.AddTrigger(…)` |
| `StdSchedulerFactory` and its 47 constants | `QuartzSchedulerBuilder.UseProperties(properties)` — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone) |
| `StdSchedulerFactory.PropertySchedulerName` | nothing; it named an ADO.NET column, not a setting |
| `SchedulerBuilder` | `QuartzSchedulerBuilder` for standalone use, `AddQuartz` under a host |
| `DirectSchedulerFactory` | `QuartzSchedulerBuilder`, with `UseThreadPool(IThreadPool)` / `UseJobStore(IJobStore)` for pre-built parts |
| `IPropertyConfigurer`, `IPropertySetter`, `IPropertyConfigurationRoot`, `PropertiesHolder`, `PropertiesSetter` | typed options |
| `AddQuartz(Action<configurator, IServiceProvider>)` | see below |
| `quartz.config` file discovery, `StdSchedulerFactory.PropertiesFile` | `IConfiguration`, or properties passed to `QuartzSchedulerBuilder.UseProperties` |
| `DbProvider.RegisterDbMetadata` | the metadata callback on `UseGenericDatabase` |
| `quartz.scheduler.proxy*`, `quartz.scheduler.exporter*` | nothing; remoting is not supported on modern .NET |
| `QuartzOptions.SchedulerName`, `.SchedulerId`, `.MisfireThreshold` | the typed options — see [`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings) |
| `IPersistentStoreBuilder.UseDataSourceConnectionProvider()` | `DataSourceOptions.UseRegisteredDataSource` |
| `AdoJobStoreOptions.Clustered`, `.ClusterCheckinInterval`, `.ClusterCheckinMisfireThreshold` | `ClusteringOptions` — see [Clustering is configured in one place](#clustering-is-configured-in-one-place) |
| `SchedulerRepository.Instance` | `ISchedulerRepository` resolved from the container |
| `DBConnectionManager.Instance` | `IDbConnectionManager` resolved from the container |
| `StdSchedulerFactory.GetDbConnectionManager()`, `.GetSchedulerRepository()` | `IDbConnectionManager` / `ISchedulerRepository` resolved from the container |

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

Options arrive as the scheduler's own, whichever of the three interfaces you ask for.
`IOptionsMonitor<QuartzSchedulerOptions>` and `IOptionsSnapshot<QuartzSchedulerOptions>` work as well
as `IOptions<>`: `CurrentValue` and `Value` are the options of the scheduler your component belongs
to, `Get(name)` answers for the name you pass, and `OnChange` reports your scheduler's changes and
not another's.

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
+ serviceProvider.GetRequiredService<IDbConnectionManager>().AddDbProvider("default", myProvider);
```

The observable consequence is that schedulers built different ways no longer find each other. Given a
scheduler registered with `AddQuartz` and another built by a `QuartzSchedulerBuilder` in the same
process:

* `ISchedulerFactory.GetAllSchedulers()` on either one lists only its own schedulers.
* `ISchedulerFactory.LookupScheduler(name)` returns `null` for the other one's name.
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

A `QuartzSchedulerBuilder` owns the container it creates, so its repository holds only the schedulers it
built. `StdSchedulerFactory.GetSchedulerRepository()` and `GetDbConnectionManager()` went with the class;
resolve `ISchedulerRepository` or `IDbConnectionManager` from the container instead.

## `StdSchedulerFactory` is gone

The properties-based factory has been removed. It was the last construction path that was not the
container: it parsed `quartz.*` strings, loaded types by name, set properties by reflection, and built a
scheduler of its own. Since 4.0 it did none of that itself — it built a service collection, handed the
properties to the same translation layer `AddQuartz` uses, and resolved the scheduler from the container
like everything else. What was left was a second front door onto one hallway, plus 47 public constants
whose only purpose was to spell keys that a configuration file spells anyway.

Flat `quartz.*` keys are **not** going away. What changed is which type you hand them to:

```diff
- ISchedulerFactory factory = new StdSchedulerFactory(properties);
- IScheduler scheduler = await factory.GetScheduler();
+ IScheduler scheduler = await QuartzSchedulerBuilder.Create()
+     .UseProperties(properties)
+     .BuildScheduler();
```

`UseProperties` feeds the same translator, so every key means exactly what it always did, including the
`quartz.checkConfiguration` check that rejects a misspelled key. Configuration written in code wins over
the properties whichever order the two are applied in: property-derived options are applied before
anything the builder was told, and implementations the properties name are registered after — options
being last-wins and registrations first-wins, the same rule `AddQuartz` follows.

Two behaviors did not survive, because they lived in `Initialize()` rather than in the properties:

* **The `quartz.*` environment-variable overlay.** `new StdSchedulerFactory()` with no arguments read
  every `quartz.*` environment variable. Nothing does that now. Use `IConfiguration` with
  `AddEnvironmentVariables()`, which is the ordinary way to say it, and pass the section to `AddQuartz`
  or flatten it yourself into the `NameValueCollection` you hand to `UseProperties`.
* **The embedded `quartz.config` defaults.** A factory constructed with no properties used to start from
  `instanceName = DefaultQuartzScheduler`, `threadCount = 10` and `misfireThreshold = 60000`. A builder
  given no properties starts from the typed option defaults instead — `InstanceName` `QuartzScheduler`,
  `MaxConcurrency` 10, and an in-memory `MisfireThreshold` of 5 seconds. Set them explicitly if you were
  relying on the old values. Note that this was never the behavior of `new StdSchedulerFactory(properties)`
  either, which always bypassed the file.

`IsSupportedConfigurationKey` is gone with the class, so a configuration carrying keys of your own can no
longer be allowed by subclassing the factory. Set `quartz.checkConfiguration` to `false` instead.

`GetDefaultScheduler()` is gone too. It returned a process-wide scheduler from a process-wide factory,
which stopped being a coherent idea when the repository became the container's — see
[No process-global scheduler or connection state](#no-process-global-scheduler-or-connection-state).
Build one scheduler where the application starts and hold it, or register it with `AddQuartz` and inject
`IScheduler`.

### Every removed constant

The strings are unchanged. This table is here so a `Ctrl+F` for the constant you used finds the key to
write instead, and the typed option that is usually the better answer.

| Removed constant | Key | Typed equivalent |
|---|---|---|
| `PropertySchedulerInstanceName` | `quartz.scheduler.instanceName` | `QuartzSchedulerOptions.InstanceName` |
| `PropertySchedulerInstanceId` | `quartz.scheduler.instanceId` | `QuartzSchedulerOptions.InstanceId` |
| `PropertySchedulerInstanceIdGeneratorPrefix` | `quartz.scheduler.instanceIdGenerator` | constructor injection into your `IInstanceIdGenerator` |
| `PropertySchedulerInstanceIdGeneratorType` | `quartz.scheduler.instanceIdGenerator.type` | register `IInstanceIdGenerator` in the container |
| `PropertySchedulerThreadName` | `quartz.scheduler.threadName` | `QuartzSchedulerOptions.ThreadName` |
| `PropertySchedulerBatchTimeWindow` | `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow` | `QuartzSchedulerOptions.BatchTriggerAcquisitionFireAheadTimeWindow` |
| `PropertySchedulerMaxBatchSize` | `quartz.scheduler.batchTriggerAcquisitionMaxCount` | `QuartzSchedulerOptions.MaxBatchSize` |
| `PropertySchedulerExporterPrefix` | `quartz.scheduler.exporter` | nothing; remoting is not supported on modern .NET |
| `PropertySchedulerExporterType` | `quartz.scheduler.exporter.type` | nothing; see above |
| `PropertySchedulerProxy` | `quartz.scheduler.proxy` | `Quartz.HttpClient` for talking to a remote scheduler |
| `PropertySchedulerProxyType` | `quartz.scheduler.proxy.type` | `Quartz.HttpClient`; the key is now rejected rather than ignored |
| `PropertySchedulerIdleWaitTime` | `quartz.scheduler.idleWaitTime` | `QuartzSchedulerOptions.IdleWaitTime` |
| `PropertySchedulerMakeSchedulerThreadDaemon` | `quartz.scheduler.makeSchedulerThreadDaemon` | `QuartzSchedulerOptions.MakeSchedulerThreadDaemon` |
| `PropertySchedulerTypeLoadHelperType` | `quartz.scheduler.typeLoadHelper.type` | `UseTypeLoader<T>()`, or `UseSimpleTypeLoader()` |
| `PropertySchedulerJobFactoryPrefix` | `quartz.scheduler.jobFactory` | constructor injection into your `IJobFactory` |
| `PropertySchedulerJobFactoryType` | `quartz.scheduler.jobFactory.type` | `UseJobFactory<T>()` |
| `PropertySchedulerInterruptJobsOnShutdown` | `quartz.scheduler.interruptJobsOnShutdown` | `QuartzSchedulerOptions.InterruptJobsOnShutdown` |
| `PropertySchedulerInterruptJobsOnShutdownWithWait` | `quartz.scheduler.interruptJobsOnShutdownWithWait` | `QuartzSchedulerOptions.InterruptJobsOnShutdownWithWait` |
| `PropertySchedulerContextPrefix` | `quartz.context.key` | `QuartzSchedulerOptions.Context` |
| `PropertyThreadPoolPrefix` | `quartz.threadPool` | `ThreadPoolOptions` |
| `PropertyThreadPoolType` | `quartz.threadPool.type` | `UseThreadPool<T>()`, or `UseThreadPool(instance)` |
| `PropertyTimeProviderType` | `quartz.timeProvider.type` | `UseTimeProvider(TimeProvider)` |
| `PropertyJobStorePrefix` | `quartz.jobStore` | `AdoJobStoreOptions` / `InMemoryJobStoreOptions` |
| `PropertyJobStoreType` | `quartz.jobStore.type` | `UseInMemoryStore()`, `UsePersistentStore<T>()`, or `UseJobStore(instance)` |
| `PropertyJobStoreDbRetryInterval` | `quartz.jobStore.dbRetryInterval` | `AdoJobStoreOptions.DbRetryInterval` |
| `PropertyJobStoreLockHandlerPrefix` | `quartz.jobStore.lockHandler` | constructor injection into your `ISemaphore` |
| `PropertyJobStoreLockHandlerType` | `quartz.jobStore.lockHandler.type` | `UseLockHandler<T>()` |
| `PropertyTablePrefix` | `tablePrefix` (under `quartz.jobStore`) | `AdoJobStoreOptions.TablePrefix` |
| `PropertyDataSourcePrefix` | `quartz.dataSource` | `DataSourceOptions`, bound from `Quartz:DataSource:<name>` |
| `PropertyDataSourceProvider` | `provider` (under a data source) | `DataSourceOptions.Provider` |
| `PropertyDataSourceConnectionString` | `connectionString` (under a data source) | `DataSourceOptions.ConnectionString` |
| `PropertyDataSourceConnectionStringName` | `connectionStringName` (under a data source) | `DataSourceOptions.ConnectionStringName` |
| `PropertyDbProvider` | `quartz.dbprovider` | the metadata callback on `UseGenericDatabase` |
| `PropertyDbProviderType` | `connectionProvider.type` (under a data source) | register `IDbProvider`, or `DataSourceOptions.UseRegisteredDataSource` |
| `PropertyExecutionLimitPrefix` | `quartz.executionLimit` | `UseExecutionLimits(limits => …)` |
| `PropertyPluginPrefix` | `quartz.plugin` | `AddPlugin<T>()` |
| `PropertyPluginType` | `type` (under a plugin) | `AddPlugin<T>()` |
| `PropertyJobListenerPrefix` | `quartz.jobListener` | `AddJobListener<T>(matchers)` |
| `PropertyTriggerListenerPrefix` | `quartz.triggerListener` | `AddTriggerListener<T>(matchers)` |
| `PropertyListenerType` | `type` (under a listener) | the two `Add*Listener<T>` methods above |
| `PropertyCheckConfiguration` | `quartz.checkConfiguration` | still read, by `QuartzSchedulerBuilder.UseProperties` |
| `PropertyObjectSerializer` | `quartz.serializer` | `UseSerializer<T>()`, `UseSystemTextJsonSerializer()` |
| `PropertyThreadExecutor` | `quartz.threadExecutor` | nothing; there is no thread executor since the thread pool became asynchronous |
| `PropertyThreadExecutorType` | `quartz.threadExecutor.type` | nothing; see above |
| `DefaultInstanceId` | `NON_CLUSTERED` | `QuartzSchedulerOptions.DefaultInstanceId`, which is the same string |
| `AutoGenerateInstanceId` | `AUTO` | `QuartzSchedulerOptions.GenerateInstanceId` |
| `SystemPropertyAsInstanceId` | `SYS_PROP` | keep setting `quartz.scheduler.instanceId` to `SYS_PROP`: it still selects the generator that reads the id from the `quartz.scheduler.instanceId` environment variable. That generator is internal and cannot be named directly, so to vary the behaviour, register an `IInstanceIdGenerator` of your own in the container as described above |

Three more constants existed in 3.x and had already gone before this release, listed here because a 3.x
configuration is the one most likely to still name them:

| Removed constant | Value | Replacement |
|---|---|---|
| `ConfigurationSectionName` | `quartz` | the `<quartz>` Full Framework configuration section is not read; use `IConfiguration` |
| `PropertiesFile` | `quartz.config` | nothing is read from disk — see "The quartz.config file is no longer read" above |
| `PropertySchedulerName` | `schedName` | nothing; it named an ADO.NET column rather than a setting |

### Every removed member

The factory was also a set of override points, and a 3.x application that subclassed it was usually
reaching for one of them. Each had a reason that the container now covers directly.

| Removed member | Replacement |
|---|---|
| `StdSchedulerFactory()` | `QuartzSchedulerBuilder.Create()` |
| `StdSchedulerFactory(NameValueCollection)` | `QuartzSchedulerBuilder.Create().UseProperties(properties)` |
| `Initialize(NameValueCollection)` | `UseProperties(properties)` |
| `Initialize()` | nothing; there is no file and no environment overlay left to read |
| `GetScheduler()`, `LookupScheduler(name)`, `GetAllSchedulers()` | unchanged apart from the by-name rename — they are `ISchedulerFactory`, which `Build()` returns |
| `static GetDefaultScheduler()` | build a scheduler where the application starts and hold it, or inject `IScheduler` |
| `Dispose()`, `Dispose(bool)` | the factory `Build()` returns owns its container and implements `IDisposable` and `IAsyncDisposable`; cast to dispose it |
| `GetSchedulerRepository()` | `ISchedulerRepository`, resolved from the container |
| `GetDBConnectionManager()` (3.x) | `IDbConnectionManager`, resolved from the container |
| `GetNamedConnectionString(string)` (3.x) | `DataSourceOptions.ConnectionStringName`, resolved from `IConfiguration`'s connection strings |
| `Instantiate(QuartzSchedulerResources, QuartzScheduler)` (3.x) | nothing; both types are internal and the container builds the graph |
| `InstantiateType<T>(Type?)` (3.x) | register the implementation in the container — this was the seam a container had to patch, and it is the container now |
| `IsSupportedConfigurationKey(string)` | set `quartz.checkConfiguration` to `false` to allow keys of your own |
| `LoadType(string?)` | `ITypeLoadHelper`, selected with `UseTypeLoader<T>()` |
| `ValidateConfiguration()` (3.x) | `quartz.checkConfiguration` for the keys, and `IValidateOptions<T>` for the typed options |

## The standalone builder is the same builder

`QuartzSchedulerBuilder` now implements `IQuartzBuilder` — the interface `AddQuartz` hands out — rather
than offering five methods of its own that happened to have the same names, plus a
`Configure(Action<IQuartzBuilder>)` hatch for everything else. There is one configuration API with two
front doors: `AddQuartz` for an application that has a container, `QuartzSchedulerBuilder` for one that
does not.

```diff
- var scheduler = await QuartzSchedulerBuilder.Create()
-     .Configure(q =>
-     {
-         q.UsePersistentStore(store => store.UseSqlServer(connectionString));
-         q.AddJob<ReportJob>(j => j.WithIdentity("report"));
-     })
-     .UseDefaultThreadPool(maxConcurrency: 20)
-     .BuildScheduler();
+ var builder = QuartzSchedulerBuilder.Create();
+ builder.UseDefaultThreadPool(maxConcurrency: 20)
+     .UsePersistentStore(store => store.UseSqlServer(connectionString))
+     .AddJob<ReportJob>(j => j.WithIdentity("report"));
+
+ var scheduler = await builder.BuildScheduler();
```

Everything on `IQuartzBuilder` — jobs, triggers, calendars, listeners, plugins, execution limits — is
now available on the standalone builder without a wrapper, which is the point. The cost is that
`Build()` and `BuildScheduler()` cannot be reached by chaining: the configuration members are declared
to return `IQuartzBuilder`, and C# has no covariant returns for interface implementations, so hold the
builder in a variable and build from it. That is how `WebApplicationBuilder` is used, and it is why
`Create()` is a separate statement in every sample above.

`UseProperties(NameValueCollection)` is the exception — it belongs to the standalone builder only, so it
returns `QuartzSchedulerBuilder` and still chains into `BuildScheduler()`.

### `Build()` returns something you can dispose

`Build()` used to return `ISchedulerFactory` while handing back an object that owned a container, so
every caller that wanted to shut it down had to cast to a type the type system never mentioned. It now
returns `StandaloneSchedulerFactory`, which *is* an `ISchedulerFactory` and is also `IAsyncDisposable`
and `IDisposable`:

```diff
- ISchedulerFactory factory = builder.Build();
- using IDisposable container = (IDisposable) factory;
+ await using StandaloneSchedulerFactory factory = builder.Build();
```

Prefer `await using`: disposal shuts the scheduler down, which is asynchronous work that `Dispose()`
can only block on. A caller that never disposes behaves as it always did — the scheduler runs until the
process ends.

Two members moved the other way, from the standalone builder onto `IQuartzBuilder`, so that a scheduler
registered with `AddQuartz` can also be given a part that was built rather than configured:

| Member | Meaning |
|---|---|
| `UseThreadPool(IThreadPool)` | uses a pool the caller constructed |
| `UseJobStore(IJobStore)` | uses a store the caller constructed |

| Removed from `QuartzSchedulerBuilder` | Use instead |
|---|---|
| `Configure(Action<IQuartzBuilder>)` | call the members directly on the builder |
| `ConfigureScheduler`, `UseDefaultThreadPool` ×2, `UseJobFactory(IJobFactory)`, `UseInMemoryStore` | the identical `IQuartzBuilder` members, which the builder now implements |

## Clustering is configured in one place

`AdoJobStoreOptions` no longer carries `Clustered`, `ClusterCheckinInterval` and
`ClusterCheckinMisfireThreshold`. Those three said the same thing as `UseClustering(…)` and
`ClusteringOptions`, so a scheduler had two places to be clustered from and they could disagree.
`ClusteringOptions` is now the one place, and whether a store is clustered is something it *reports*
rather than something you can also set on it.

| Removed | Use instead |
|---|---|
| `AdoJobStoreOptions.Clustered` | `ClusteringOptions.Enabled` |
| `AdoJobStoreOptions.ClusterCheckinInterval` | `ClusteringOptions.CheckinInterval` |
| `AdoJobStoreOptions.ClusterCheckinMisfireThreshold` | `ClusteringOptions.CheckinMisfireThreshold` |

`ClusteringOptions`' two intervals are no longer nullable — `UseClustering()` with no arguments turns
clustering on without touching them, because it configures the options object rather than assigning a
copy over it.

Code-first configuration is unchanged:

```csharp
store.UseClustering(cluster =>
{
    cluster.CheckinInterval = TimeSpan.FromSeconds(10);
    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
});
```

The flat keys are unchanged as well — `quartz.jobStore.clustered`,
`quartz.jobStore.clusterCheckinInterval` and `quartz.jobStore.clusterCheckinMisfireThreshold` all still
work, and so does the `JobStore:Clustered` spelling in `appsettings.json`, because `AddQuartz` reads
every section as flat keys too. What is new is the hierarchical spelling that matches the options type:

```json
{
  "Quartz": {
    "JobStore": {
      "Clustering": {
        "Enabled": true,
        "CheckinInterval": "00:00:10",
        "CheckinMisfireThreshold": "00:00:20"
      }
    }
  }
}
```

`AdoJobStoreOptions` validation lost the rule that `UseDbLocks` must be on when `Clustered` is: it can
no longer see both settings, and it never needed to, since every path that enables clustering enables
database locking with it and a store with an explicit lock handler of its own — a Redis semaphore, say —
was never wrong to leave `UseDbLocks` off.

## The SQLite extension methods swapped names

::: warning
`UseSqlite` did not exist in 3.x and now means **Microsoft.Data.Sqlite**. The method that used to be
called `UseSQLite` — the legacy **System.Data.SQLite** driver — is now `UseSystemDataSqlite`. Changing
`UseSQLite` to `UseSqlite` compiles and runs, and silently swaps your ADO.NET provider. Read the table
before doing a case-insensitive find and replace.
:::

| 3.x / 4.0 preview | 4.0 | ADO.NET driver | Provider name |
|---|---|---|---|
| `UseSQLite` | `UseSystemDataSqlite` | System.Data.SQLite | `SQLite` |
| `UseMicrosoftSQLite` | `UseSqlite` | Microsoft.Data.Sqlite | `SQLite-Microsoft` |

```diff
- store.UseSQLite(connectionString);          // System.Data.SQLite
+ store.UseSystemDataSqlite(connectionString);

- store.UseMicrosoftSQLite(connectionString); // Microsoft.Data.Sqlite
+ store.UseSqlite(connectionString);
```

The short name goes to the driver you should reach for, which is the same rule `UseMySql` and
`UseMySqlConnector` already followed, and the same spelling Entity Framework Core uses for the same
choice. The provider names themselves — what a `quartz.dataSource.<name>.provider` key says — are
unchanged, so nothing in a configuration file has to move.

## A data source is defined, referred to, or handed over

There were five ways to say where a job store's connections come from, and two of them said the same
thing. `UseDataSourceConnectionProvider()` is gone; it existed only to set
`DataSourceOptions.UseRegisteredDataSource`, and being a method it also had to be called in the right
order to take effect.

```diff
  q.UsePersistentStore(store =>
  {
-     store.UsePostgres(db => db.Provider = "Npgsql");
-     store.UseDataSourceConnectionProvider();
+     store.UsePostgres(db => db.UseRegisteredDataSource = true);
  });
```

What is left says three different things:

| Member | Role |
|---|---|
| `UseDataSource(configure)` | **defines** a data source — which driver, and how to reach the database. The database methods such as `UseSqlServer` are shorthands for it |
| `UseDataSourceName(name)` | **refers to** a data source by name, picking up settings registered elsewhere, such as a `Quartz:DataSource:<name>` section |
| `DataSourceOptions.UseRegisteredDataSource` | takes connections from a `DbDataSource` the application registered in the container, instead of from a connection string |

Where connections come from is a property of the data source, so it is said in `DataSourceOptions`
alongside `ConnectionString` and `ConnectionStringName`, and it wins over both.

### `AddDataSourceProvider()` went with it

The two always travelled together. `AddDataSourceProvider()`, on the configurator, registered
`DataSourceDbProvider` in the container as the `IDbProvider` to use; `UseDataSourceConnectionProvider()`,
on the store, then named it as that data source's connection provider. Two calls, in two different
places, that had to agree with each other to do anything. Both are gone, and
`UseRegisteredDataSource` does the whole job — the data source builds its own `DataSourceDbProvider`
around the `DbDataSource` it resolves from the container:

```diff
  services.AddQuartz(q =>
  {
-     q.AddDataSourceProvider();
      q.UsePersistentStore(store =>
      {
-         store.UsePostgres(db => db.Provider = "Npgsql");
-         store.UseDataSourceConnectionProvider();
+         store.UsePostgres(db => db.UseRegisteredDataSource = true);
      });
  });
```

Registering the `DbDataSource` itself is still yours to do — `services.AddNpgsqlDataSource(…)`, or
whatever your provider offers. What is no longer yours to do is wiring it up to Quartz.

## `QuartzOptions` lost its three typed settings

`QuartzOptions` is the flat `quartz.*` property bag. Three of its members were typed settings that
existed nowhere else in it, each reading and writing a key that has a typed option of its own, so they
were a third spelling of a setting that already had two.

| Removed | Use instead |
|---|---|
| `QuartzOptions.SchedulerName` | `QuartzSchedulerOptions.InstanceName`, or `Properties["quartz.scheduler.instanceName"]` |
| `QuartzOptions.SchedulerId` | `QuartzSchedulerOptions.InstanceId`, or `Properties["quartz.scheduler.instanceId"]` |
| `QuartzOptions.MisfireThreshold` | `InMemoryJobStoreOptions.MisfireThreshold` / `AdoJobStoreOptions.MisfireThreshold` |

```diff
- services.Configure<QuartzOptions>(options => options.SchedulerName = "core");
+ services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "core"));
```

`MisfireThreshold` also had a round-trip problem of its own: it stored a `TimeSpan` as a string of
whole milliseconds, so a value with sub-millisecond precision did not read back as it was written.

`Properties`, `ToNameValueCollection()` and `Scheduling` stay. `Scheduling` is the exception that proves
the rule — its three directives say how a configured schedule is applied to a scheduler rather than how
a component is configured, and they have no options type of their own to bind onto, so this is where
they live.

## `AddJob` registers the job with the container

`AddJob<T>()`, `AddJob(type, …)` and `ScheduleJob<T>()` now register the job type as a **scoped**
service, with `TryAdd` semantics.

Before, they described the job to the scheduler and nothing else. The job factory resolved the job
from the container and fell back to `ActivatorUtilities` when it found no registration, so a job whose
constructor the container could not satisfy was never noticed at startup: `ValidateOnBuild` — which the
host turns on by default in the Development environment — had never heard of the type. The failure
arrived at fire time instead, as a `JobInstantiationException`, by which point the trigger had fired,
the job had not run, and every trigger of that job had been moved to `TriggerState.Error` (discussion
[#3211](https://github.com/quartznet/quartznet/discussions/3211)).

```csharp
services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));

// now throws when the container is built:
// Unable to resolve service for type 'IReportStore' while attempting to activate 'SendReportsJob'
```

Two things follow from this.

**A job you register yourself keeps your registration.** The lifetime, factory or implementation type
you chose wins, because Quartz's registration is a `TryAdd`. Registering the same job with `AddJob`
twice is harmless for the same reason. If you were registering your jobs explicitly to get startup
validation, that line is now redundant, but it is not wrong:

```diff
- services.AddScoped<SendReportsJob>();   // no longer needed for validation
  services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));
```

**Scoped is the lifetime the job factory uses.** It opens a dependency injection scope per fire,
resolves the job from it, and disposes the scope when the job returns — so a job may take scoped
dependencies such as a database context. If a job of yours has to be a singleton, register it as one
yourself; it must then be thread-safe and must not capture scoped dependencies.

A job type that is an interface or an abstract class is not registered, since the container could not
construct it. Jobs named by an XML or JSON schedule are not registered either — nothing describes them
to the container — so those still fail at fire time if their dependencies are missing.

One case changes shape rather than merely failing earlier. A job that injects one of a *named*
scheduler's own parts — `ISchedulerFactory`, `IJobStore`, `IThreadPool` — used to be activated through
that scheduler's view of the container and was handed its parts. It is now resolved from the container
like any other service, which resolves those unkeyed: with a default scheduler present it gets the
default scheduler's, and with only named schedulers it fails validation. Take the scheduler from
`IJobExecutionContext.Scheduler`, which is the scheduler that is actually running the job, or register
the job yourself with a factory that resolves what it needs by key.

## One shape per registration method

The `AddJob` / `AddTrigger` / `AddCalendar` grid had overloads that said the same thing twice, and
optional parameters that made the no-argument calls ambiguous. Each method now has one pair of shapes:
one taking a configurator, one taking a configurator and the `IServiceProvider`.

| Removed | Use instead |
|---|---|
| `AddJob<T>(JobKey?, …)`, `AddJob(Type, JobKey?, …)` | `WithIdentity(jobKey)` inside the configurator |
| `AddJob<T>()`, `AddJob<T>(JobKey)` with no configurator | `AddJob<T>(j => j.WithIdentity(…))` |
| `AddTrigger(Action<ITriggerConfigurator<IJob>>)` and its `IServiceProvider` twin | `AddTrigger<IJob>(…)`, which is the same method said once |

```diff
  var jobKey = new JobKey("awesome job", "awesome group");
- q.AddJob<ExampleJob>(jobKey, j => j.WithDescription("my awesome job"));
+ q.AddJob<ExampleJob>(j => j.WithIdentity(jobKey).WithDescription("my awesome job"));

- q.AddTrigger(t => t.WithIdentity("Simple Trigger").ForJob(jobKey).StartNow());
+ q.AddTrigger<IJob>(t => t.WithIdentity("Simple Trigger").ForJob(jobKey).StartNow());
```

The job type on `AddTrigger<TJob>` is what lets the trigger's job data name the job's properties;
`AddTrigger<IJob>` is the "this trigger's data names nothing" spelling, and it is what the removed
overloads did.

The interface these methods extend is `IQuartzBuilder`, which is what 3.x called
`IServiceCollectionQuartzConfigurator`. The `AddQuartz` overloads that handed the callback an
`IServiceProvider` alongside it are gone with it: a container built while the container is still being
described could only be a second, throwaway one. Ask for the service provider where it is actually
used — each registration method has a shape that is given one, at the point where the container really
exists:

```diff
- services.AddQuartz((q, serviceProvider) =>
- {
-     var schedule = serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule;
-     q.AddTrigger(t => t.WithIdentity("custom").ForJob(jobKey).WithCronSchedule(schedule));
- });
+ services.AddQuartz(q =>
+ {
+     q.AddTrigger<IJob>((serviceProvider, t) => t
+         .WithIdentity("custom")
+         .ForJob(jobKey)
+         .WithCronSchedule(serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule));
+ });
```

`AddCalendar` takes the same `AddCalendarOptions` record that `IScheduler.AddCalendar` does, instead of
two adjacent bools whose order was impossible to remember, and its first parameter is `name` rather
than `calendarName`:

```diff
- q.AddCalendar<HolidayCalendar>("holidays", replace: true, updateTriggers: true,
-     cal => cal.AddExcludedDay(new DateOnly(2025, 12, 25)));
+ q.AddCalendar<HolidayCalendar>("holidays", new AddCalendarOptions { Replace = true, UpdateTriggers = true },
+     cal => cal.AddExcludedDay(new DateOnly(2025, 12, 25)));
```

Both the options and the configurator are optional in the type-based overload, so
`q.AddCalendar<HolidayCalendar>("holidays")` is a valid registration of an empty calendar.

## Plugins are registered like listeners

`AddPlugin` had four shapes, one of which took the plugin's name first and the rest of which could not
take a name at all. It now has the same three shapes as the listener registrations, each with an
optional trailing name:

| Shape | Meaning |
|---|---|
| `AddPlugin<T>(string? name = null)` | the container constructs the plugin |
| `AddPlugin<T>(Func<IServiceProvider, T> factory, string? name = null)` | you construct it |
| `AddPlugin<T, TOptions>(Action<TOptions>? configure = null, string? name = null)` | it is given options of its own |

```diff
- q.AddPlugin("xml", provider => new XmlSchedulingDataProcessorPlugin());
+ q.AddPlugin(provider => new XmlSchedulingDataProcessorPlugin(), "xml");
```

The name is how the scheduler refers to the plugin and the name a `quartz.plugin.<name>.*` key
configures it under — some plugins derive persisted job and trigger keys from it — so it is part of the
deployment's identity rather than a label. Left unset, the plugin's type name is used, exactly as
before.

## Several schedulers are registered explicitly

`AddQuartz(IConfiguration)` used to look for a `Schedulers` sub-section and, if it found one, register
one named scheduler per child instead of the single scheduler it otherwise registers. One call did two
different things depending on the shape of a file. The fan-out has a name of its own now:

```diff
- services.AddQuartz(builder.Configuration.GetSection("Quartz"));   // with a Schedulers section
+ services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));
```

`AddQuartz(configuration)` throws a `SchedulerConfigException` naming `AddQuartzSchedulers` when it is
handed a section with a `Schedulers` sub-section, and `AddQuartzSchedulers` throws when handed one
without. `AddQuartz(name, configuration)`, which registers one of the schedulers a `Schedulers` section
describes, is unchanged.

The six phases that decide which of a scheduler's descriptions wins — configuration is last-wins,
registration is first-wins — are now documented on `AddQuartz` itself rather than in comments inside it.

## The hosted service starts every scheduler

`AddQuartzHostedService()` registered the hosted service only if an unkeyed `ISchedulerFactory` was
already in the service collection. Calling it before `AddQuartz()` therefore registered nothing for the
default scheduler and said nothing about it — the application started, and no job ever ran.

The hosted service is now registered unconditionally and resolves its schedulers when the host starts,
so the two calls can be made in either order. It starts every scheduler in the container, the default
one and each named one, which is what the pair of services it replaces did between them.

```diff
- services.AddQuartz(q => …);            // had to come first
  services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
+ services.AddQuartz(q => …);            // either order now
```

A container with no scheduler in it at all is a `SchedulerConfigException` at startup: the hosted
service was asked for, so something was meant to run.

`QuartzHostedServiceOptions` are now named options, keyed by scheduler name. Options passed to
`AddQuartzHostedService(configure)` apply to every scheduler, which is what one shared options object
meant before; a scheduler that has to differ is configured by name, and its settings are applied after
the shared ones whichever order the calls are made in:

```csharp
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
services.AddQuartzHostedService("Reporting", options => options.StartDelay = TimeSpan.FromMinutes(2));
```

`QuartzHostedService`'s constructor changed to match: it takes the `IServiceProvider` it resolves the
schedulers from and an `IOptionsMonitor<QuartzHostedServiceOptions>` instead of one factory and one
options object. Subclasses registered with `AddQuartzHostedService<T>()` need their constructors
updated; the `Starting`/`Started`/`Stopping`/`Stopped` overrides are unchanged. The internal
`NamedSchedulerHostedService` is gone, its work having moved into the one service.

## The ASP.NET Core methods say Quartz once

| Old | New |
|---|---|
| `IQuartzBuilder.AddHttpApi(…)` | `AddQuartzHttpApi(…)` |
| `IEndpointRouteBuilder.MapQuartzApi()` | `MapQuartzHttpApi()` |

```diff
- services.AddQuartz(q => q.AddHttpApi());
+ services.AddQuartz(q => q.AddQuartzHttpApi());

- app.MapQuartzApi().RequireAuthorization();
+ app.MapQuartzHttpApi().RequireAuthorization();
```

`AddQuartzHealthChecks` gained an `IQuartzBuilder` overload, so a named scheduler can register a health
check that reports on *its* scheduler rather than the default one. It is named
`quartz-scheduler-<scheduler name>` unless you say otherwise:

```csharp
services.AddQuartz("Reporting", q => q.AddQuartzHealthChecks(options => options.Tags = ["ready"]));
```

`QuartzHealthCheckOptions.Tags` is a settable `IReadOnlyCollection<string>` rather than a get-only
`List<string>`, so assign a collection instead of calling `Add`:

```diff
- options.Tags.Add("ready");
- options.Tags.Add("live");
+ options.Tags = ["ready", "live"];
```

## The OpenAPI calendar schema names the properties the payload actually uses

The HTTP API's endpoints handle `ICalendar`, which OpenAPI cannot describe, so the published document has
always been shaped by a stand-in type. Nothing compiled against that stand-in, so two of its property names
were simply wrong, and a client generated from the document did not round-trip a calendar at all:

| Schema said | Server sends |
|---|---|
| `calendarType` | `type` |
| `calendarBase` | `baseCalendar` |

Nothing about the wire format changed — the server has always written `type` and `baseCalendar`. What changes
is generated clients: regenerate against 4.x and the two properties on your calendar model are renamed, which
is a compile error at each use rather than a silent one. Hand-written clients were already having to send
`type` and `baseCalendar` to be understood, so they need no change.

The rest of the schema was already right: `description`, `timeZoneId`, `excludedDays`, `excludedDates`,
`cronExpressionString`, `rangeStart`, `rangeEnd` and `invertTimeRange` are what a calendar of each built-in
type serializes to. A test in `Quartz.Tests.AspNetCore` now compares the stand-in against those names in both
directions, so the schema cannot quietly drift again.

## Remoting a scheduler is not a Quartz concern

`ISchedulerProxyFactory` and `HttpSchedulerProxyFactory` are removed, and the `quartz.scheduler.proxy*`
and `quartz.scheduler.exporter*` keys are now rejected rather than silently accepted and ignored.

Nothing read them. `ISchedulerProxyFactory` had no caller inside Quartz once .NET Remoting went away —
no builder member reached it, no configuration key selected it — while the key validator still
whitelisted `quartz.scheduler.proxy`, so a configuration file could carry a proxy setting that changed
nothing. A configuration that still carries one now gets a `SchedulerConfigException` saying what to
use instead:

```diff
- quartz.scheduler.proxy = true
- quartz.scheduler.proxy.type = Quartz.Impl.HttpSchedulerProxyFactory, Quartz.HttpClient
```

```csharp
// talk to a remote scheduler over HTTP, from Quartz.HttpClient
services.AddQuartzHttpClient("Quartz ASP.NET Core Sample Scheduler", "QuartzHttpClient");

// or serve one over HTTP: AddQuartzHttpApi + MapQuartzHttpApi, from Quartz.AspNetCore
```

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

`Quartz.OpenTracing` is **dropped** and has no 4.x release. It consumed the `DiagnosticSource` events that
4.x replaced with `System.Diagnostics.Activity`, and the OpenTracing project itself is archived. Remove the
package reference and the `AddQuartzOpenTracing` call, and instrument with
[OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz)
instead — see [OpenTelemetry Integration](packages/opentelemetry-integration.md#coming-from-quartz-opentracing).

```diff
- <PackageReference Include="Quartz.OpenTracing" Version="3.*" />
+ <PackageReference Include="OpenTelemetry.Instrumentation.Quartz" Version="1.*" />
```

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
var builder = QuartzSchedulerBuilder.Create();
builder.UseTimeProvider(new FakeTimeProvider());
var scheduler = await builder.BuildScheduler();
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

## Job execution metrics

The `Quartz` meter publishes the same four instruments under the same names, and every scheduler now
publishes them — configuring the meter used to be wired to `StdSchedulerFactory`, so a scheduler
registered with `AddQuartz` emitted nothing at all. Two further things changed, and both are visible to
anything already charting them:

| Instrument | 4.x type | Tags |
|---|---|---|
| `scheduling.quartz.execute` | `Counter<long>` | `trigger.group`, `trigger.name`, `job.group`, `job.name` |
| `scheduling.quartz.execute.errors` | `Counter<long>` | the four identity tags **+ `error.type`** |
| `scheduling.quartz.execute.active` | **`UpDownCounter<long>`** (was `Counter<long>`) | the four identity tags |
| `scheduling.quartz.execute.duration` | `Histogram<double>` | the four identity tags, **+ `error.type`** when the execution failed |

**`scheduling.quartz.execute.active` is an up-down counter.** The number of jobs running goes down as
often as it goes up, and Quartz has always measured the decrement — but a `Counter` is monotonic by
OpenTelemetry's definition, so an exporter aggregating one is entitled to drop or mis-render a negative
measurement, leaving a "jobs currently running" chart that only ever climbs. The name, the unit and the
meaning are unchanged; the instrument type an exporter sees is not, so a dashboard or an alert built on
the old series has to be rebuilt on a non-monotonic one — a `Sum` with `IsMonotonic = false` in the
OpenTelemetry SDK, which Prometheus renders as a gauge rather than a counter.

**`scheduling.quartz.exception_type` is `error.type`, and it names the exception the job threw.** Two
things were wrong with it. The tag was added to a copy of the tag list and thrown away, so the errors
counter carried the four identity tags and nothing else: an exporter could see that executions failed, but
never what failed. And the type it named was the `JobExecutionException` that the job run shell wraps
anything a job throws in — the same answer for very nearly every failure there is.

The tag now arrives, and it reports the exception an application would recognise. A job that throws
`InvalidOperationException` is reported as `System.InvalidOperationException`, not as the
`JobExecutionException` -> `JobExecutionProcessException` -> cause chain the run shell built around it. A
job that raises a `JobExecutionException` itself has no such wrapper underneath and is reported as
`Quartz.JobExecutionException`, which is what it chose to say. The value is a fully-qualified type name
and nothing else: a message would be unbounded, and an attribute an exporter aggregates on has to have
bounded cardinality.

The name is OpenTelemetry's. [`error.type`](https://opentelemetry.io/docs/specs/semconv/registry/attributes/error/)
is the semantic convention's attribute for what an operation failed with, so these series line up with
every other instrumented failure in the same dashboard; the Quartz-specific spelling is gone rather than
emitted alongside it, since two names for one attribute doubles the series and settles nothing. **A query
or an alert matching on `scheduling.quartz.exception_type` has to be rewritten**, and one that expected
the value `JobExecutionException` now sees the type the job threw.

The tag is on the errors counter and on the duration histogram, so a failed run's duration can be told
apart from a successful one's. It is deliberately *not* on `scheduling.quartz.execute.active`, whose
increment and decrement have to carry identical attributes or the series never comes back to zero.

The execution's span carries the same `error.type` with the same value, so one attribute finds a failure
in a trace and in a metric alike. The span also still records the exception as an event, with the whole
wrapper chain, because that is where the stack traces are.

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

### Deserialization failures surface as `Quartz.JsonSerializationException`

| 3.x | 4.x |
|---|---|
| `IObjectSerializer.DeSerialize` could let `Newtonsoft.Json.JsonSerializationException` and `Newtonsoft.Json.JsonReaderException` escape | Every deserialization failure arrives as `Quartz.JsonSerializationException` — a `SchedulerException` — from **both** serializers, with the payload in the message and the underlying parse failure as `InnerException` |

`Quartz.JsonSerializationException` shadows Newtonsoft's type of the same name inside the
`Quartz.Serialization.Newtonsoft` package, because every file in it sits under the `Quartz`
namespace and the enclosing namespace wins over a `using`. The wrapping `catch` therefore never
matched Newtonsoft's parse failures and they escaped raw. Code that catches
`Newtonsoft.Json.JsonSerializationException` around `IObjectSerializer.Deserialize` — or around any
job store read that goes through it — has to catch `Quartz.JsonSerializationException` instead:

```csharp
// 3.x
catch (Newtonsoft.Json.JsonSerializationException e)

// 4.x — the same failure, whichever serializer is configured
catch (Quartz.JsonSerializationException e)
```

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

### A serialized trigger carries its preferred node

Both JSON trigger serializers write a trigger's [node affinity](tutorial/node-affinity.md) pin, as the same
pair the `QRTZ_TRIGGERS` row stores:

```json
"PreferredNode": "node-1",
"PreferredNodeAuto": false
```

`PreferredNode` is the node name — `null` when the trigger is unpinned, `*` for an auto pin no node has
claimed yet — and `PreferredNodeAuto` says whether the node holding the pin claimed it automatically. It
takes both halves: a claimed auto pin and one the caller named look identical from the node name alone, yet
only the automatic one is released when its node stops checking in.

Payloads written before the fields existed, 3.x's included, have neither and read back as
`PreferredNode.None` — an unpinned trigger, which is what they always were.

This matters wherever a whole trigger travels as JSON. The HTTP API, `Quartz.HttpClient` and the dashboard
dropped the pin in both directions, so scheduling or rescheduling a pinned trigger over HTTP quietly
unpinned it, and the same held for a custom `IJobStore` that persists serialized triggers. The ADO.NET job
store was never affected: it keeps the pin in the `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` columns and
reapplies them to every trigger it reads, blob triggers included.

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

### The OpenAPI trigger schema describes the whole payload

The HTTP API's endpoints handle `ITrigger`, which OpenAPI cannot describe, so the published document has always
been shaped by a stand-in type. Nothing compiled against that stand-in, so it fell behind the payload it was
meant to describe, and five properties the server has been sending were missing from the schema:

| Property | Present on |
|---|---|
| `nextFireTimeUtc` | every trigger |
| `previousFireTimeUtc` | every trigger |
| `executionGroup` | every trigger |
| `timesTriggered` | every trigger type except `CronTrigger` |
| `recurrenceRule` | `RecurrenceTrigger` |

`RecurrenceTrigger` itself was also missing from the list of trigger types the `triggerType` discriminator can
carry.

Nothing about the wire format changed — those fields were always on it. What changes is generated clients: a
client regenerated against 4.x gains the five properties, and code that had been reading them through an
escape hatch (a raw `JsonElement`, an extra partial-class member) can read them from the generated model
instead. The two fire times are computed by the scheduler, so a value sent with a trigger being scheduled is
overwritten; `executionGroup`, `timesTriggered` and `recurrenceRule` are read from what you send.

A test in `Quartz.Tests.AspNetCore` now compares the stand-in against the property names a trigger of each
built-in type actually serializes to, in both directions, so the schema cannot quietly fall behind again.

## Sealed and Internalized Types

Many types have been sealed and/or internalized to minimize the API surface that needs to be maintained. If you were extending a type that is now sealed or internal, file an issue to request it be reopened.

The ones most likely to be visible in existing code:

**`QuartzScheduler` and `QuartzSchedulerResources` are internal.** `QuartzScheduler` is the implementation
behind `IScheduler` and was only reachable through `StdScheduler`'s constructor, which is internal too now that
the container builds the scheduler. Resolve `IScheduler` or `ISchedulerFactory`; the settings that used to live
on `QuartzSchedulerResources` are `QuartzSchedulerOptions`.

**`StdAdoConstants` and `IAdoUtil` are internal, and constants are no longer inherited.** `AdoConstants` stays
public — table, column and state names are a real contract for delegate authors — but it is a `static class`
now, and `JobStoreSupport`, `StdAdoDelegate` and `DbSemaphore` no longer derive from it or from
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

`DbSemaphore.AdoUtil` is `private protected` for the same reason, so a semaphore written outside Quartz no
longer sees it — derive from `DbSemaphore` and use `IDbProvider`, or implement `ISemaphore` directly.

**Three trigger persistence delegates became public**, so a custom delegate list can name all five built-ins:
`CronTriggerPersistenceDelegate`, `SimpleTriggerPersistenceDelegate` and
`DailyTimeIntervalTriggerPersistenceDelegate` join `CalendarIntervalTriggerPersistenceDelegate` and
`RecurrenceTriggerPersistenceDelegate`. All five are `sealed`; write your own against
`SimplePropertiesTriggerPersistenceDelegateSupport` or `ITriggerPersistenceDelegate`.

**`SchedulerConstants` and `MisfireInstruction` are static classes** rather than structs, and `QuartzOptions`,
`SchedulingOptions` and `QuartzHostedServiceOptions` are `sealed`. Referring to the constants is unchanged;
only `new MisfireInstruction()`, which never meant anything, stops compiling.

**`HttpScheduler` is `sealed`.** It is a wire client — every member turns a call into an HTTP request — so
deriving from it and overriding half of them produces a scheduler that is partly remote and partly not. Wrap
it in an `IScheduler` of your own if you need to intercept calls.

**Quartz.Dashboard's Blazor components are not API.** They are `public` because the Razor compiler makes them
so, but they are UI and are excluded from the dashboard's public-API baseline. Build against
`QuartzDashboardOptions`, `AddQuartzDashboard` and the model types.

**The `Quartz.Xml.JobSchedulingData20` namespace is gone.** It held the fourteen classes that `xsd.exe`
generated from `job_scheduling_data_2_0.xsd` — `QuartzXmlConfiguration20`, `abstractTriggerType`,
`calendarIntervalTriggerType`, `cronTriggerType`, `entryType`, `jobdatamapType`, `jobdetailType`,
`jobschedulingdataSchedule`, `preprocessingcommandsType`, `preprocessingcommandsTypeDeletejob`,
`preprocessingcommandsTypeDeletetrigger`, `processingdirectivesType`, `simpleTriggerType` and
`triggerType`. They were public only because `XmlSerializer` cannot serialize internal types; nothing
in Quartz took or returned one, and their names never followed .NET conventions because a code
generator picked them. `XMLSchedulingDataProcessor` reads the document itself now, so the model is
internal.

**The XML format has not changed** — the schema, the file, and every element and attribute in it are
exactly as they were, and `job_scheduling_data_2_0.xsd` still validates the document before it is
read. Only two failures report differently:

| Input | 3.x / earlier 4.x | 4.0 |
|---|---|---|
| A file that is not well-formed XML | `InvalidOperationException`, "There is an error in XML document (3, 13)", wrapping an `XmlException` | the `XmlException` itself, naming the line, the position and the unclosed elements |
| A file whose elements are not in the `http://quartznet.sourceforge.net/JobSchedulingData` namespace | `InvalidOperationException`, "&lt;job-scheduling-data xmlns=''&gt; was not expected" | `SchedulerConfigException` naming the namespace that was expected |

A schema violation still throws `SchedulingDataValidationException` carrying every error found, and
`XmlSchedulingDataProcessorPlugin` still wraps whatever surfaces in a `SchedulerException`, so a
plugin-based setup sees no change at all.

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

### The three `*Support` base classes are gone

`JobListenerSupport`, `TriggerListenerSupport` and `SchedulerListenerSupport` existed so that a listener could
implement only the notifications it cared about. Every member of `IJobListener`, `ITriggerListener` and
`ISchedulerListener` is now a default interface member — the notifications do nothing, and `Name` returns
`GetType().Name` — so the base classes had nothing left to add, and they cost you your one base class.

Implement the interface instead, and drop `override`:

```diff
- public sealed class MyListener : JobListenerSupport
+ public sealed class MyListener : IJobListener
  {
-     public override string Name => "MyListener";
-     public override ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
+     public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
      {
          ...
      }
  }
```

`Name` can go: it defaults to the type's name. Keep it only when several instances of one type are registered
with the same scheduler, since the later registration would otherwise replace the earlier one.

One consequence is easy to miss: **a default interface member is not a class member**. Code that reads `Name`
off the concrete type no longer compiles unless the listener declares `Name` itself, so read it through the
interface:

```diff
- var listener = new MyListener();
- scheduler.ListenerManager.RemoveSchedulerListener(listener.Name);
+ ISchedulerListener listener = new MyListener();
+ scheduler.ListenerManager.RemoveSchedulerListener(listener.Name);
```

`JobInterruptMonitorPlugin` is a public, unsealed `ITriggerListener`; its `Name`, `TriggerFired` and
`TriggerComplete` are declared `virtual` so that a plugin deriving from it can still override them.

### Scheduler listeners are identified by name

`ISchedulerListener` has a `Name`, a default interface member returning `GetType().Name`. Registering two
scheduler listeners whose `Name` matches replaces the first, exactly as it has always worked for job and
trigger listeners. Override `Name` if you register several instances of one type with the same scheduler:

```diff
- scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener);
+ scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener.Name);
```

A test double does not run a default interface member, so a faked `ISchedulerListener` needs its `Name`
configured before `AddSchedulerListener` will accept it:

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
* `H` (hash) tokens for [load distribution](cron-expressions.md#h-hash-for-load-distribution) across triggers

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

`DbSemaphore.ExecuteSql` still receives the stored name as a `string` — that parameter really is the value
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
`AdoJobStoreOptions.DontSetAutoCommitFalse` went with the store property: no code path ever read it and no
configuration key ever set it, so an application that set it was configuring nothing.
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
* `DbSemaphore.Sql` and `.InsertSql` are get-only and arrive through the constructor. They were
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

## The driver delegate speaks in records

The five types `IDriverDelegate` hands back or takes in were mutable classes with settable properties, loose
`string` pairs where a key belongs, and — in one case — properties that were non-nullable but unassigned. They
are records now, and say what they hold.

| Type | What changed |
|---|---|
| `FiredTriggerRecord` | `sealed record`, `[Serializable]` dropped, `FireInstanceState` is a `StoredTriggerState` |
| `RecoverMisfiredJobsResult` | `sealed record`; the property is `EarliestNewTimeUtc`, matching its constructor argument |
| `DelegateInitializationArgs` | `sealed record` with `required` / `init` members |
| `TriggerAcquireResult` | carries a `TriggerKey` instead of `TriggerName` + `TriggerGroup` |
| `TriggerStatus` | replaced by `StoredTriggerHeader`, returned by `SelectTriggerHeader` |

`FiredTriggerRecord.FireInstanceState` was the last place raw `AdoConstants.State*` string comparisons
survived after the delegate's states were typed, in stale-acquired recovery and in cluster recovery. Its
always-populated members are `required` and non-nullable now — `FireInstanceId`, `FireInstanceState`,
`TriggerKey` and `SchedulerInstanceId` — and `JobKey` stays nullable because an ACQUIRED row is written
before the job has been loaded. `[Serializable]` went with the class: nothing has serialized this record
since binary serialization was dropped.

`TriggerStatus` was a mutable class with a settable key, a `string` state and a name that said nothing.
`StoredTriggerHeader` is the storage-side counterpart of `TriggerHeader`:

```diff
- TriggerStatus? status = await Delegate.SelectTriggerStatus(conn, triggerKey, cancellationToken);
- bool blocked = AdoConstants.StatePausedBlocked == status.Status;
+ StoredTriggerHeader? status = await Delegate.SelectTriggerHeader(conn, triggerKey, cancellationToken);
+ bool blocked = status.State == StoredTriggerState.PausedBlocked;
```

It speaks `StoredTriggerState` rather than the reported `TriggerState`, because resuming a trigger has to
tell `PausedBlocked` from `Paused` and the reported state does not.

`FiredTriggerQuery` stays unpaged, deliberately, and now says so in its own doc comment: FIRED_TRIGGERS holds
one row per firing in flight, and every caller is a maintenance pass — recovery, cluster failover, blocked
state checks — that has to see the whole set. Handing one of those a page would leave the rest unrecovered.

## `ValidateSchema` is part of `IDriverDelegate`

Startup schema validation was a `StdAdoDelegate` method the job store reached by type test, so a delegate
that was not a `StdAdoDelegate` silently skipped the check that `quartz.jobStore.performSchemaValidation`
had asked for. It is an interface member now, and every delegate participates:

```csharp
ValueTask<int> ValidateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);
```

A delegate of your own that derives from `StdAdoDelegate` inherits the implementation and can extend it to
cover tables of its own. One written against the interface directly has to implement it; returning `0`
without checking anything restores the old skip.

## The optional columns are required, so the probes are gone

3.x asked the database at startup whether `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP` and
`PREFERRED_NODE` were present, and switched the matching feature off when they were not. **4.x
requires all of them**, so there is no question left to ask, and the members that asked it are gone
from `StdAdoDelegate`:

| Removed | What it did |
|---|---|
| `HasMisfireOriginalFireTimeColumn`, `HasExecutionGroupColumn`, `HasPreferredNodeColumn` | Reported the result of the matching probe |
| `SupportsMisfireOriginalFireTimeColumn`, `SupportsExecutionGroupColumn`, `SupportsPreferredNodeColumn` | Ran the probe, swallowing the provider error that meant "no such column" |
| `VerifyTriggersTableReachable` | Told a genuinely missing column apart from a database that was momentarily unreachable, so a transient failure did not disable a feature for the life of the process |

**The upgrade path is the schema migration, and it is mandatory.** Run
`schema_30_to_40_upgrade_<dialect>.sql` from
[database/migrations/4.0/](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0)
before pointing 4.x at a 3.x database — [Database Schema Migration](#database-schema-migration) lists
the columns and what each script does. Skipping it no longer degrades gracefully, and it does not
fail tidily either: [`ValidateSchema`](#validateschema-is-part-of-idriverdelegate) only checks that
each table can be queried, not which columns it has, so what you reach instead is a provider-level
missing-column error from the first statement that names one. That happens almost immediately —
loading a trigger projects all four columns, and storing one names three of them.

A delegate of your own has nothing to override here any more. If it derived from `StdAdoDelegate` and
overrode a probe — to hard-code `true` against a schema you knew was current, say — delete the
override; the base class no longer declares it.

### The three extra acquisition SQL hooks went with them

Because the columns were optional, 3.x carried four versions of the trigger acquisition statement and
chose between them at run time from the probe results: plain, with `EXECUTION_GROUP`, with
`PREFERRED_NODE`, and with both. Each had its own `protected virtual` hook, and all six dialect
delegates overrode all four to hang their own row-limiting clause off the end. Three of the four are
gone from `StdAdoDelegate` and from `FirebirdDelegate`, `MySQLDelegate`, `OracleDelegate`,
`PostgreSQLDelegate`, `SQLiteDelegate` and `SqlServerDelegate`:

* `GetSelectNextTriggerToAcquireWithExecutionGroupSql(int maxCount)`
* `GetSelectNextTriggerToAcquireWithPreferredNodeSql(int maxCount)`
* `GetSelectNextTriggerToAcquireWithPreferredNodeOnlySql(int maxCount)`

With the columns required there is one statement — `StdAdoConstants.SqlSelectNextTriggerToAcquire`,
which always projects `EXECUTION_GROUP` and always carries the preferred-node filter — and one hook,
which is the one that was already there:

```csharp
protected virtual string GetSelectNextTriggerToAcquireSql(int maxCount)
```

It is unchanged, and it is still the only thing the dialects differed in: `FirebirdDelegate` appends
`ROWS n`, `SqlServerDelegate` splices in `SELECT TOP n`, `OracleDelegate` wraps the statement in a
`rownum` filter, and the rest append `LIMIT n`. **A dialect delegate of your own should keep its
`GetSelectNextTriggerToAcquireSql` override and delete the other three.**

The node-affinity parameters the statement now always carries are bound for you by the protected
`AddPreferredNodeParameters(cmd, liveNodeCutoff)`, so an override that rewrites the statement text
still does not have to know their names or the order they are bound in.

## The connection manager lives with the other ADO.NET types

`IDbConnectionManager` and `DbConnectionManager` were in `Quartz.Util`, two namespaces away from the
`IDbProvider` instances they hold. They are in `Quartz.Impl.AdoJobStore.Common` now, next to `IDbProvider`,
`DbProvider` and `DbMetadata`, and the two members that said "connection provider" say `DbProvider`, which
is the type they actually take:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `Quartz.Util.IDbConnectionManager` | `Quartz.Impl.AdoJobStore.Common.IDbConnectionManager` |
| `Quartz.Util.DbConnectionManager` | `Quartz.Impl.AdoJobStore.Common.DbConnectionManager` |
| `AddConnectionProvider(name, provider)` | `AddDbProvider(name, provider)` |
| `GetConnectionProvider(name)` | `GetDbProvider(name)` |

```diff
- using Quartz.Util;
+ using Quartz.Impl.AdoJobStore.Common;

- serviceProvider.GetRequiredService<IDbConnectionManager>().AddConnectionProvider("default", myProvider);
+ serviceProvider.GetRequiredService<IDbConnectionManager>().AddDbProvider("default", myProvider);
```

## `RAMJobStore` is sealed

Every public method of the in-memory store was `virtual`, which invited overriding operations that hold its
lock, mutate its indexes in a fixed order and raise listener notifications after releasing the lock — none of
which is a documented contract, and all of which the overriding code has to preserve. A job store is written
against `IJobStore`; the implementations Quartz ships are not base classes.

`RAMJobStore` is `sealed`, its `virtual`s are gone, and `GetFiredTriggerRecordId` is private. A store that
wants the in-memory behaviour plus something of its own wraps it, deriving from the new
`Quartz.Impl.DelegatingJobStore`:

```diff
- public class SlowJobStore : RAMJobStore
+ public sealed class SlowJobStore : DelegatingJobStore
  {
      public SlowJobStore(ILoggerFactory loggerFactory, ISchedulerSignaler signaler, TimeProvider timeProvider)
-         : base(loggerFactory, signaler, timeProvider)
+         : base(new RAMJobStore(loggerFactory, signaler, timeProvider))
      {
      }

      public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
          TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
      {
          List<IOperableTrigger> triggers = await base.AcquireNextTriggers(request, cancellationToken);
          await Task.Delay(10, cancellationToken);
          return triggers;
      }
  }
```

`UsePersistentStore<TStore>()` and `quartz.jobStore.type` take the wrapping store exactly as they took the
derived one; the `Quartz.Examples.AspNetCore` sample's `CustomJobStore` shows the whole shape.

`MisfireThreshold` keeps its setter here as it does on `JobStoreSupport`: it is read on every misfire pass
rather than only at startup.

### `DelegatingJobStore` decorates a store

`IJobStore` has around fifty members, so hand-writing a forwarder for the sake of one of them is a lot of
code that only has to be revisited every time the interface changes. `Quartz.Impl.DelegatingJobStore` is the
store-level counterpart of `DelegatingScheduler`: a `public class` that takes the store to wrap as its
constructor argument, forwards every `IJobStore` member to it, and declares each one `virtual` so a derived
store overrides only what it changes. The wrapped store is available to derived types as `InnerJobStore`.

It is the supported way to add logging, metrics, tenant routing or fault injection to a store — including a
sealed one such as `RAMJobStore`. A store that keeps scheduling data somewhere new implements `IJobStore`
directly instead; nothing forces it through this base.

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

`UseJobFactory(IJobFactory)` is new on `IQuartzBuilder` — the generic `UseJobFactory<T>()` overload has
always been there, but an already-constructed factory had nowhere to go:

```csharp
// standalone
var builder = QuartzSchedulerBuilder.Create();
builder.UseJobFactory(new MyJobFactory());
var scheduler = await builder.BuildScheduler();
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

Configuration is not the only place a type is named by string. `JOB_CLASS_NAME` holds whatever spelling the
version that wrote the row used, so a database carried over from 2.x or 3.x names jobs by namespaces and
assemblies that have since moved. **Those stored names now resolve through the same fallback**, with the same
warning naming both spellings, rather than through the runtime's lookup alone — a `Quartz.Job.NoOpJob, Quartz`
written years ago finds the type in `Quartz.Jobs` today. The column itself is left alone: reading a job never
rewrites what is persisted for it, so the fallback stays visible until you migrate the data. Previously such a
job started up and listed perfectly well, because a job's type is resolved lazily, and then failed with a
`TypeLoadException` the first time it fired.

### Other namespaces that moved

`Quartz.Spi` and `Quartz.Simpl` were not the only ones. 4.0 also settles the singular/plural split between a
namespace, its assembly and its package — where the package is `Quartz.Jobs`, the namespace is `Quartz.Jobs`
too — and empties the namespaces that held a single type or a handful of them. For source code each row is a
`using` directive; where a row says the old spelling still resolves, the same fallback and the same warning as
above cover configuration strings.

| 3.x namespace | 4.x namespace | Notes |
|---|---|---|
| `Quartz.Job` | `Quartz.Jobs` | Namespace, assembly and package now agree. A configuration string or a stored `JOB_CLASS_NAME` naming the old spelling still resolves, with a warning |
| `Quartz.Extensibility.IDirectoryProvider` | `Quartz.Jobs.IDirectoryProvider` | It exists for `DirectoryScanJob` alone, so it lives with it. It is resolved from `SchedulerContext` by key, never by type name |
| `Quartz.Plugin.History` <br> `Quartz.Plugin.Interrupt` <br> `Quartz.Plugin.Json` <br> `Quartz.Plugin.Management` <br> `Quartz.Plugin.Xml` <br> `Quartz.Plugin.TimeZoneConverter` | `Quartz.Plugins.*` | Same rule as the jobs: the packages are `Quartz.Plugins` and `Quartz.Plugins.TimeZoneConverter`. A `quartz.plugin.<name>.type` naming the old spelling still resolves, with a warning. The **configuration key** prefix is still `quartz.plugin.`, singular — it is not a namespace |
| `Quartz.Listener` | `Quartz.Listeners` | A `quartz.jobListener.<name>.type` or `quartz.triggerListener.<name>.type` naming the old spelling still resolves, with a warning — but see [The three `*Support` base classes are gone](#the-three-support-base-classes-are-gone): three of the seven types are not there under either name |
| `Quartz.Impl.Matchers` | `Quartz` | See [Matchers moved to `Quartz`](#matchers-moved-to-quartz). No shim is needed: a matcher is passed as an object and is never named by a configuration string |
| `Quartz.AspNetCore` <br> `Quartz.AspNetCore.HealthChecks` <br> `Quartz.AspNetCore.HttpApi` | `Quartz` | The package is still `Quartz.AspNetCore`; only the namespaces are gone. `AddQuartzHealthChecks`, `AddQuartzHttpApi` and `MapQuartzHttpApi` are extension methods and resolve through the `Quartz` you already have, so a `using Quartz.AspNetCore;` can simply be deleted. The class that hosts them is `QuartzAspNetCoreConfigurationExtensions`, renamed from `QuartzServiceCollectionExtensions` because the core package now has a class of that name in the same namespace |
| `Quartz.HttpClient` | `Quartz` | `HttpScheduler` and `HttpClientException`; the package is still `Quartz.HttpClient`. The namespace had to go because it shadowed `System.Net.Http.HttpClient` for every file under `Quartz.*`, including Quartz's own. `HttpScheduler` is also `sealed` now |
| `Quartz.Serialization.Json` <br> `Quartz.Serialization.Json.Calendars` <br> `Quartz.Serialization.Json.Triggers` | `Quartz.Serialization.SystemTextJson[.Calendars\|.Triggers]` | These are the System.Text.Json types, which merged into the core package; the namespace was named after the *retired 3.x Newtonsoft package*. `Quartz.JsonConfigurationExtensions` is `Quartz.SystemTextJsonConfigurationExtensions` to match — the extension methods on it are unaffected. **Read the warning below before changing a `using` on a ported serializer** |
| `Quartz.Impl.Redis` | `Quartz.Extensions.Redis` | One type, `RedisSemaphore`, filed under `Impl` as if it were part of the core. Namespace, assembly and package are the same string now; the **package id is unchanged**. A `quartz.jobStore.lockHandler.type` naming the old namespace still resolves, with a warning |

::: warning Porting a 3.x Newtonsoft serializer
In 3.x, `Quartz.Serialization.Json.Triggers` and `Quartz.Serialization.Json.Calendars` were the
**Newtonsoft** package's namespaces. In 4.x the same spellings, minus `.Json`, plus `.SystemTextJson`,
belong to System.Text.Json — and the Newtonsoft package's are `Quartz.Serialization.Newtonsoft.Triggers`
and `Quartz.Calendars`.

So a 3.x custom Newtonsoft serializer that is ported by changing its `using` to
`Quartz.Serialization.SystemTextJson.Triggers` compiles against the *System.Text.Json* base class, and then
fails on the overrides: the abstract members take a `Utf8JsonWriter` and a `JsonElement`, not a
`JsonWriter` and a `JObject`. The signature mismatch is the symptom; the wrong base class is the bug.
A Newtonsoft serializer stays on the Newtonsoft base — see
[Newtonsoft types moved out of the core namespaces](#newtonsoft-types-moved-out-of-the-core-namespaces).
:::

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
| `Quartz.Util.DBConnectionManager` | `Quartz.Impl.AdoJobStore.Common.DbConnectionManager` |
| `DbMetadata.Init()` | `Initialize()` |
| `AdoConstants.ColumnMifireInstruction` | `ColumnMisfireInstruction` (a typo; the column name is unchanged) |
| `SchedulerConstants.FailedJobOriginalTriggerFiretime`, `…ScheduledFiretime` | `…TriggerFireTime`, `…ScheduledFireTime` (the string values are unchanged) |
| `XMLSchedulingDataProcessor.OverWriteExistingData`, `SchedulingOptions.OverWriteExistingData` | `OverwriteExistingData`. The configuration key is spelled `Quartz:Scheduling:OverwriteExistingData` now; keys are matched case-insensitively, so an existing file keeps binding, but code assigning the property has to change |
| `XMLSchedulingDataProcessor.PrepForProcessing`, `.BuildTriggersByFQJobNameMap` | `PrepareForProcessing`, `BuildTriggersByFullyQualifiedJobNameMap` |
| `RedisSemaphore.LockTtlMilliseconds`, `.LockRetryIntervalMilliseconds` | `LockTimeToLive`, `LockRetryInterval`, both `TimeSpan` — **also the config keys `lockTtlMilliseconds` → `lockTimeToLive` and `lockRetryIntervalMilliseconds` → `lockRetryInterval`** |
| `IObjectSerializer.DeSerialize` | `Deserialize` |
| `TriggerFiredBundle.PrevFireTimeUtc` | `PreviousFireTimeUtc`, matching the spelling used everywhere else |
| `XMLSchedulingDataProcessor.OverWriteExistingJobs` argument `overWriteExistingJobs` | `overwriteExistingJobs` |
| `Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin` | `Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin` — the namespace moved and the casing follows .NET rules. A `quartz.plugin.<name>.type` naming either old spelling still resolves, with a warning. Its nested `JobFile` class and its `JobFiles` property are internal now: they are how the plugin tracks what it has read, not something to call |
| `Quartz.Xml.ValidationException` | `Quartz.SchedulingDataValidationException`. The old name collided with `System.ComponentModel.DataAnnotations.ValidationException` in any file that used both, and it was never XML-specific — the JSON processor throws it too. Its `ValidationExceptions` is an `IReadOnlyList<Exception>`; it was a `List<Exception>` a caller could add to |

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

## Matchers moved to `Quartz`

A matcher is something you hand to `IScheduler`, not an implementation detail of one, and every signature that
takes one already lives in `Quartz`, so the types moved there rather than into a namespace of their own:

```diff
- using Quartz.Impl.Matchers;
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
`Quartz.Serialization.SystemTextJson.Triggers` and `Quartz.Serialization.Newtonsoft.Triggers`. `CronTriggerSerializer`
is unchanged — it has no fire-count state to restore.

## The trigger family interfaces are read models

The same reasoning removes the rest of the schedule setters from the five family interfaces. Setting one on a
trigger you got back from the scheduler compiled, ran, and changed nothing: `RAMJobStore.GetTrigger` hands out
`Trigger.Clone()` and the ADO.NET store materializes a fresh instance per read, so every trigger the scheduler
gives you is a detached copy.

| 3.x | 4.x |
|---|---|
| `int ISimpleTrigger.RepeatCount { get; set; }` | `{ get; }` |
| `TimeSpan ISimpleTrigger.RepeatInterval { get; set; }` | `{ get; }` |
| `string? ICronTrigger.CronExpressionString { get; set; }` | `{ get; }` |
| `TimeZoneInfo ICronTrigger.TimeZone { get; set; }` | `{ get; }` |
| `IntervalUnit ICalendarIntervalTrigger.RepeatIntervalUnit { get; set; }` | `{ get; }` |
| `int ICalendarIntervalTrigger.RepeatInterval { get; set; }` | `{ get; }` |
| `IReadOnlyCollection<DayOfWeek> IDailyTimeIntervalTrigger.DaysOfWeek { get; set; }` | `{ get; }` |
| `string IRecurrenceTrigger.RecurrenceRule { get; set; }` | `{ get; }` |
| `TimeZoneInfo IRecurrenceTrigger.TimeZone { get; set; }` | `{ get; }` |

There are two sanctioned ways to change a stored trigger, and both were already public:

```diff
  ICronTrigger t = (ICronTrigger) await scheduler.GetTrigger(key);

- t.CronExpressionString = "0 0 12 * * ?";                 // compiled, did nothing
+ ITrigger updated = t.GetTriggerBuilder()
+     .WithCronSchedule("0 0 12 * * ?")
+     .Build();
+ await scheduler.RescheduleJob(key, updated);             // reshape the schedule

+ await scheduler.UpdateTriggerDetails(key,                // edit metadata in place
+     new TriggerDetailsUpdate().WithDescription("noon"));
```

Code that owns a concrete `CronTriggerImpl` / `SimpleTriggerImpl` / … is unaffected: `AbstractTrigger` and the
concrete triggers keep their public setters, and `IMutableTrigger` — the contract job stores and custom
trigger authors write through — is unchanged.

`ICalendar` deliberately keeps its two setters (`Description`, `CalendarBase`). It is an implementable SPI:
the built-in calendar serializers assign through the interface while rebuilding a calendar, so they are part
of its contract in a way the trigger setters never were.

If you author an `ITriggerPersistenceDelegate` of your own, note that `ObjectUtils.SetPropertyValue` falls
back to writable *interface* properties when the concrete type has none, and that fallback is now narrower.
No built-in delegate depends on it — all five write only `timesTriggered`, which the concrete triggers expose.

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
`WithMisfireInstruction` taking its family's enum.

```diff
  .WithSimpleSchedule(x => x
      .WithInterval(TimeSpan.FromMinutes(5))
      .RepeatForever()
-     .WithMisfireHandlingInstructionNextWithExistingCount())
+     .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount))
```

Earlier 4.0 previews spelled that method `WithMisfireHandlingInstruction`. It is `WithMisfireInstruction` now,
on all five builders, matching `TriggerDetailsUpdate.WithMisfireInstruction` and the XML element name.

### SimpleScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireNow()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)` |
| `WithMisfireHandlingInstructionNowWithExistingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithExistingCount)` |
| `WithMisfireHandlingInstructionNowWithRemainingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithRemainingCount)` |
| `WithMisfireHandlingInstructionNextWithRemainingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount)` |
| `WithMisfireHandlingInstructionNextWithExistingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount)` |
| (call nothing) | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.SmartPolicy)`, still the default |

### CronScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(CronTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |

### CalendarIntervalScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.DoNothing)` |

### DailyTimeIntervalScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.DoNothing)` |

### RecurrenceScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.DoNothing)` |

### `TriggerDetailsUpdate` takes the same enums

The update object had a single `WithMisfireInstruction(int)`, which let a simple trigger's code be applied to
a cron trigger: the number is in range for both families and means a different policy in each. It now has one
overload per family, plus a code form for callers that genuinely have a number and no family — a value read
off the wire, from configuration, or from a trigger.

| 4.0 preview | 4.x |
|---|---|
| `.WithMisfireInstruction(2)` | `.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |
| `.WithMisfireInstruction(MisfireInstruction.CronTrigger.DoNothing)` | `.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |
| `.WithMisfireInstruction(someInt)` | `.WithMisfireInstructionCode(someInt)` |

The typed overloads are the taught path: the store rejects an update whose family is not the stored trigger's,
so a cron policy sent to a simple trigger is now an error rather than a silently different policy.
`WithMisfireInstructionCode` keeps only the range check the trigger itself applies.

### The enums are the vocabulary

An enum member's underlying value *is* the misfire code a trigger stores, so the two convert freely:

```csharp
CronTriggerMisfireInstruction policy = (CronTriggerMisfireInstruction) trigger.MisfireInstruction;
int stored = (int) CronTriggerMisfireInstruction.DoNothing;   // 2
```

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

## `[Serializable]` survives only where a database blob needs it

`BinaryFormatter` is obsolete on .NET 8 (SYSLIB0051) and throws on .NET 9 and later, and Quartz 4 ships no
binary serializer. The attributes that only `BinaryFormatter` ever read were still on 49 types — every
exception, every matcher, the job execution context, the job detail. Nineteen of them keep the attributes;
the other 30 lost them.

The line is drawn at the database. A type keeps `[Serializable]`, `ISerializable` and `GetObjectData` when a
job store blob can be made of it, so that a 3.x database whose blobs were written by `BinaryFormatter` stays
readable while you migrate it to JSON — see
[Migrating from binary serialization](packages/json-serialization.md#migrating-from-binary-serialization).

| Blob column | Types that keep the attributes |
|---|---|
| `JOB_DETAILS.JOB_DATA`, `TRIGGERS.JOB_DATA` | `JobDataMap`, `StringKeyDirtyFlagMap`, `DirtyFlagMap<TKey, TValue>`, `Key<T>`, `JobKey`, `TriggerKey` |
| `CALENDARS.CALENDAR` | `BaseCalendar`, `AnnualCalendar`, `CronCalendar`, `DailyCalendar`, `HolidayCalendar`, `MonthlyCalendar`, `WeeklyCalendar`, `CronExpression` |
| `BLOB_TRIGGERS.BLOB_DATA` | `AbstractTrigger`, `SimpleTriggerImpl`, `CronTriggerImpl`, `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` |

A trigger reaches that third row when no trigger persistence delegate handles it — a type of your own, or one
deriving from a built-in trigger with `HasAdditionalProperties` returning `true`. The store writes the whole
object into `BLOB_TRIGGERS`, so the trigger class hierarchy is part of the blob graph.

Everything else lost `[Serializable]`:

| Where | Types |
|---|---|
| Exceptions | `SchedulerException`, `JobExecutionException`, `JobPersistenceException`, `ObjectAlreadyExistsException`, `SchedulerConfigException`, `UnableToInterruptJobException`, `JsonSerializationException`, `LockException`, `NoSuchDelegateException`, `SchedulingDataValidationException` |
| Matchers | `AndMatcher<TKey>`, `GroupMatcher<TKey>`, `KeyMatcher<TKey>`, `NameMatcher<TKey>`, `NotMatcher<TKey>`, `OrMatcher<TKey>`, `StringMatcher<TKey>`, `StringOperator` |
| Everything else | `JobType`, `SchedulerContext`, `JobExecutionContextImpl` |

The `protected` / `public` `(SerializationInfo, StreamingContext)` constructors went with them, on
`SchedulerException`, `JobPersistenceException`, `SchedulerConfigException`, `UnableToInterruptJobException`
and `HttpClientException`. If you derive from one of those and forward a `SerializationInfo` to the base,
delete your constructor — the base class library's `Exception(SerializationInfo, StreamingContext)` is
obsolete too, and nothing calls yours.

`Key<T>` and its two subclasses are on the keep side because a key can be a *value* inside a job data map. Quartz
never puts one there itself — the recovery entries it writes are strings, and both `AbstractTrigger` and
`JobDetailImpl` deliberately mark their key fields `[NonSerialized]` and serialize the name and group as separate
strings — but a job data map holds arbitrary `object` values and serializes them all, so an application that did
`jobDataMap.Put("parent", jobKey)` on 3.x has a `JobKey` sitting in its `JOB_DATA`. `BinaryFormatter` refuses to
deserialize an instance whose type is not marked serializable, so removing the attribute would make that blob
unreadable even through the compatibility package. Blob-reachable means what the graph *can* contain, not only
what Quartz itself puts there.

## The two exceptions moved out of `Quartz.Core`

`Quartz.Core` held exactly two public types, and both were exceptions: `JobExecutionProcessException` and
`JobInstantiationException`. Every one of their siblings — `SchedulerException`, which they both derive from,
`JobExecutionException`, `SchedulerConfigException`, `UnableToInterruptJobException` — lives in `Quartz`. The two
moved there too, and `Quartz.Core` is now internal from top to bottom: nothing in it is a type you can name.

```diff
- using Quartz.Core;
-
  public ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken ct = default)
  {
      if (cause is JobInstantiationException failure) { … }
      return default;
  }
```

Catching or type-testing either exception needs no change beyond deleting the `using Quartz.Core;`, which the
compiler flags as unused. `JobInstantiationException` is new in 4.0 and has never shipped, so nobody is catching it
under the old namespace yet.

## Execution limits are built once, then frozen

`ExecutionLimits` used to be two things at the same time. It was a mutable fluent builder — `ForGroup`,
`ForDefaultGroup`, `ForOtherGroups` and `Unlimited` all mutated `this` and returned `this` — and it was an
`IReadOnlyDictionary<string, int?>`, a collection whose name had to suppress CA1710 to survive analysis. The
scheduler defended itself against the mutable half by snapshotting whatever it was handed, which is a hint that the
type was doing one job too many. It is now two types: `ExecutionLimitsBuilder` mutates, `ExecutionLimits` is the
immutable snapshot that `Build()` returns and that the scheduler thread reads.

```diff
- await scheduler.SetExecutionLimits(new ExecutionLimits()
+ await scheduler.SetExecutionLimits(new ExecutionLimitsBuilder()
      .ForGroup("batch-jobs", 2)
      .ForDefaultGroup(10)
-     .ForOtherGroups(5));
+     .ForOtherGroups(5)
+     .Build());
```

`IQuartzBuilder.UseExecutionLimits` takes an `Action<ExecutionLimitsBuilder>`, so a configuration lambda is
unchanged and still reads the same way:

```csharp
q.UseExecutionLimits(limits => limits.ForGroup("high-cpu", 3));
```

Reading limits back is what changed shape, because the snapshot is not a dictionary:

| Before | 4.0 |
|---|---|
| `limits["heavy"]` | `limits.TryGetLimit("heavy", out int? maxConcurrent)` |
| `limits[ExecutionLimits.DefaultGroupKey]` | `limits.TryGetLimit(null, out int? maxConcurrent)` |
| `limits.ContainsKey("heavy")` | `limits.TryGetLimit("heavy", out _)` |
| `limits.Count` | `limits.Groups.Count`, or `limits.IsEmpty` for the question actually being asked |
| `foreach (KeyValuePair<string, int?> pair in limits)` | `foreach (ExecutionGroupLimit limit in limits.Groups)` |

`TryGetLimit` returning `false` is not the same as unlimited: it means the group has no entry of its own, and a
named group without one still falls back to `OtherGroups`. That distinction was invisible when the type was a
dictionary, and it is the reason the lookup is a `TryGet` rather than an indexer.

The sentinel keys are named instead of spelled. `ExecutionLimits.OtherGroups` (`"*"`) stays public, because it is a
key you can write in configuration. The empty-string key for the default group is internal now: an
`ExecutionGroupLimit.Group` is `null` for it, matching `ITrigger.ExecutionGroup` being `null` for a trigger with no
execution group, and `ForDefaultGroup` is how you configure it. `"_"` and `"null"` are still accepted as aliases for
it in `quartz.executionLimit.*` keys and in the HTTP API, because neither a property key nor a JSON object key can
be empty. All three remain reserved: a trigger cannot have `"*"`, `"_"` or `"null"` as its execution group.

### A job store is handed the limits, and a way to spend them

`TriggerAcquisitionRequest.ExecutionLimits` and `TriggerAcquisitionCriteria.ExecutionLimits` carry
`ExecutionLimits` rather than `IReadOnlyDictionary<string, int?>`. What they carry is still the *available* slots —
the configured limits less what is already running on this node — not the configuration.

A store acquiring triggers has to count those slots down as it takes them, and the rule for doing so is not
obvious: a group's own entry wins, a named group without one falls back to `OtherGroups`, a trigger with no
execution group never does, and an unlisted group that borrows from `OtherGroups` gets its own allowance rather
than sharing one. That rule used to live inside Quartz where only the two built-in stores could reach it, so a
third-party store either reimplemented it or ignored execution groups. It is now `ExecutionSlots`:

```csharp
public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
    TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
{
    ExecutionSlots? slots = request.ExecutionLimits?.CreateSlots();

    foreach (IOperableTrigger candidate in Candidates(request))
    {
        if (slots is not null && !slots.TryTake(candidate.ExecutionGroup))
        {
            continue; // this group is forbidden here, or has run out for this pass
        }
        …
    }
}
```

Create the ledger per acquisition attempt, not per store: it is mutable and not thread-safe by design, and a
retried acquisition has to start from the limits again rather than from what the failed attempt had counted down.
`CreateSlots()` leaves the snapshot untouched, so the same `ExecutionLimits` can produce as many as you need.

## Interruption has two names, not three

Stopping a running job had three names in the public API. `IScheduler.Interrupt(JobKey)` and
`IScheduler.InterruptFireInstance(fireInstanceId)` requested it, `IJobExecutionContext.CancellationToken` — the same
token a job receives as the `cancellationToken` parameter of `IJob.Execute` — observed it, and
`ICancellableJobExecutionContext.Cancel()` sat between them looking like a third, supported way to do it. It was
not: calling it bypassed the scheduler, so no `ISchedulerListener.JobInterrupted` was raised, and it worked only for
a context you happened to be holding in the same process.

`ICancellableJobExecutionContext` is gone from the public API, and `JobExecutionContextImpl.Cancel()` with it. The
plumbing still exists inside Quartz under the name the public API already uses for the concept. Ask the scheduler
instead:

```diff
- ((ICancellableJobExecutionContext) context).Cancel();
+ await scheduler.InterruptFireInstance(context.FireInstanceId);
```

`IScheduler.GetCurrentlyExecutingJobs()` returns `List<IJobExecutionContext>` as before — the element type never
was the cancellable interface, only the documentation said so.

## `JobDataMap`'s typed accessors are the ones it inherits

`JobDataMap` declared sixty typed accessors of its own — `GetIntValue`, `TryGetIntValue`,
`GetIntValueFromString`, `TryGetIntValueFromString`, and the same four for `bool`, `char`, `double`,
`float`, `long`, `Guid`, `TimeSpan`, `DateTime` and `DateTimeOffset` — while the
`StringKeyDirtyFlagMap` it derives from declared a second, shorter set doing the same job. Two names
for one lookup, differing only in a suffix. The `…Value` set is gone; the inherited one stays:

```diff
- int retries = context.JobDetail.JobDataMap.GetIntValue("retries");
+ int retries = context.JobDetail.JobDataMap.GetInt("retries");

- if (map.TryGetTimeSpanValue("timeout", out TimeSpan timeout)) { }
+ if (map.TryGetTimeSpan("timeout", out TimeSpan timeout)) { }
```

| 3.x `JobDataMap` | 4.x, inherited from `StringKeyDirtyFlagMap` |
|---|---|
| `GetBooleanValue`, `GetBooleanValueFromString` | `GetBoolean` |
| `GetCharFromString` | `GetChar` |
| `GetDateTimeValue`, `GetDateTimeValueFromString` | `GetDateTime` |
| `GetDateTimeOffsetValue`, `GetDateTimeOffsetValueFromString` | `GetDateTimeOffset` |
| `GetDoubleValue`, `GetDoubleValueFromString` | `GetDouble` |
| `GetFloatValue`, `GetFloatValueFromString` | `GetFloat` |
| `GetGuidValue`, `GetGuidValueFromString` | `GetGuid` |
| `GetIntValue`, `GetIntValueFromString` | `GetInt` |
| `GetLongValue`, `GetLongValueFromString` | `GetLong` |
| `GetTimeSpanValue`, `GetTimeSpanValueFromString` | `GetTimeSpan` |
| every `TryGet…Value` / `TryGet…ValueFromString` | the matching `TryGet…` |
| `GetNullableGuidValue` | `TryGetGuid`, or read the entry and test it yourself |
| (none) | `GetString`, `TryGetString`, `GetDecimal`, `TryGetDecimal` |

The `…FromString` half collapses because the retained accessors already convert: a value written as a
string — which is what `UseProperties` forces, and what `PutAsString` writes — is parsed on the way
out, so one accessor covers both. `GetNullableGuidValue` is the only one without a direct
replacement; it returned `null` both for "absent" and for "present but not a `Guid`", which
`TryGetGuid` distinguishes.

`PutAsString`'s eleven overloads are one generic `PutAsString<T>(string key, T value) where T :
IConvertible`, plus the four the constraint cannot express (`DateTimeOffset`, `Guid`, `Guid?`,
`TimeSpan`). Call sites are unchanged.

## `AddQuartzServer` is `AddQuartzHostedService`

`Quartz.AspNetCore.AddQuartzServer` did two unrelated things behind one name: it registered the
hosted service that starts the scheduler, and — on frameworks that had them — an ASP.NET Core health
check. Both are separately available, and the hosted service was never specific to ASP.NET Core, so
the combined method is gone and each half is called by its own name:

```diff
  services.AddQuartz(q => { /* ... */ });

- services.AddQuartzServer(options => options.WaitForJobsToComplete = true);
+ services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
+ services.AddQuartzHealthChecks();
```

`AddQuartzHostedService` lives in the core `Quartz` package, so an application that only wants the
scheduler started with the host no longer needs `Quartz.AspNetCore` at all — see
[The hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler) for what it
now starts, and [The ASP.NET Core methods say Quartz once](#the-asp-net-core-methods-say-quartz-once)
for the rest of that package.

The health-check overload that took `IEnumerable<string> healthCheckTags` is gone with it; tags are
`QuartzHealthCheckOptions.Tags`, which is assigned rather than added to:

```diff
- services.AddQuartzServer(configure, healthCheckTags: ["ready", "live"]);
+ services.AddQuartzHealthChecks(options => options.Tags = ["ready", "live"]);
```

## The ambient logger factory stays ambient

`LogProvider.SetLogProvider(ILoggerFactory)` is the one piece of mutable process-wide state left in
Quartz, and it is deliberate rather than overlooked.

Almost everything the scheduler is made of is built by a container and is injected an `ILogger` the
ordinary way. What is left over cannot be: static helpers such as `TimeZoneUtil`, types a caller
constructs directly — triggers, calendars, plugins, the jobs in `Quartz.Jobs` — and anything that
runs while the container is still being built. A type cannot be handed a logger by a container that
does not exist yet, so those sites read the ambient factory instead of going unlogged.

Nor is it seeded from the container, which would be the obvious convenience. The slot outlives any
one container: a process that builds a host, disposes it and builds another — every integration test
suite, and every application that reloads configuration — would be left holding a disposed
`ILoggerFactory`, and the next logger created anywhere in Quartz would throw
`ObjectDisposedException` from somewhere unrelated to logging. Whoever sets the factory owns its
lifetime, and only the application can make that call. The same applies to a hand-written
`LogProvider.SetLogProvider(host.Services.GetRequiredService<ILoggerFactory>())`: it is correct as
long as the host outlives the schedulers.

`TimeZoneUtil.CustomResolver` is ambient for the same reason and is now nullable, `null` meaning
"no custom resolver", so a resolver can be removed again rather than replaced with a lambda that
returns `null`. `FindTimeZoneById` is reached from parsing a `CronExpression` and from deserializing
a trigger out of a job store blob, neither of which has a scheduler in scope — which is why
installing `Quartz.Plugins.TimeZoneConverter` in one scheduler changes id resolution for the whole
process.

## `TriggerUtils` moved to `Quartz.Extensibility`

It computes fire times by advancing a copy of a trigger through its schedule, applying the calendar
at each step, which is exactly what `IOperableTrigger` adds over `ITrigger` — so it belongs with that
contract rather than in the root namespace next to `IScheduler`. The methods are unchanged:

```diff
+ using Quartz.Extensibility;

  var times = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, calendar, 10);
```

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
| Setter-only members gained getters | `DbMetadata.DbBinaryTypeName` (now nullable) and `.ParameterDbTypePropertyName` |
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
| `StdSchedulerFactory` removed | `QuartzSchedulerBuilder.Create().UseProperties(properties)` — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone) for every removed constant |
| `QuartzSchedulerBuilder` implements `IQuartzBuilder` | Its five duplicated members and `Configure(Action<IQuartzBuilder>)` are gone, and configuration members return `IQuartzBuilder`, so `Build()` is called on a builder held in a variable — see [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder) |
| `IQuartzBuilder` gained `UseThreadPool(IThreadPool)` and `UseJobStore(IJobStore)` | A pre-built part can be handed to a scheduler registered with `AddQuartz`, not only to a standalone one |
| Clustering settings moved to `ClusteringOptions` | `AdoJobStoreOptions.Clustered` and the two `ClusterCheckin*` settings are gone; `IJobStore.Clustered` reports the state rather than setting it — see [Clustering is configured in one place](#clustering-is-configured-in-one-place) |
| `JobStoreSupport`'s constructor takes `IOptions<ClusteringOptions>` | Between `storeOptions` and `objectSerializer`; a job store deriving from it has to pass one on |
| `UseSQLite` is `UseSystemDataSqlite`, `UseMicrosoftSQLite` is `UseSqlite` | **The short name changed meaning** — see [The SQLite extension methods swapped names](#the-sqlite-extension-methods-swapped-names) |
| `UseDataSourceConnectionProvider()` removed | `DataSourceOptions.UseRegisteredDataSource`, which is what it set |
| `AddDataSourceProvider()` removed | Its other half. It registered `DataSourceDbProvider` in the container for `UseDataSourceConnectionProvider()` to name; `UseRegisteredDataSource` builds the provider itself from the registered `DbDataSource` — see [`AddDataSourceProvider()` went with it](#adddatasourceprovider-went-with-it) |
| `QuartzOptions.SchedulerName`, `.SchedulerId`, `.MisfireThreshold` removed | Each duplicated a typed option — see [`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings) |
| Job execution metrics are published by every scheduler | The meters were configured only by `StdSchedulerFactory`, so a scheduler registered with `AddQuartz` published none |
| `scheduling.quartz.execute.active` is an `UpDownCounter<long>` | It was a `Counter<long>` receiving the `-1` that ends an execution, which an exporter aggregating a monotonic sum may drop — see [Job execution metrics](#job-execution-metrics) |
| `scheduling.quartz.exception_type` is `error.type`, naming the exception the job threw | The tag was added to a copy of the tag list and discarded, so the counter said only that something failed — and the type it named was the `JobExecutionException` the run shell wraps everything in. It is OpenTelemetry's conventional name now, it is on the duration histogram and the execution's span too, and a query matching the old name or expecting the old value has to be rewritten — see [Job execution metrics](#job-execution-metrics) |
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
| `AdoJobStoreOptions.DontSetAutoCommitFalse` removed | The option the deleted store property mirrored. No code path read it and no `quartz.*` key set it, so setting it configured nothing |
| `JobStoreSupport.LastCheckin` is internal, `LogWarnIfNonZero` is private | Cluster check-in bookkeeping and a logging helper, neither of them an extension point |
| `JobStoreSupport.RecoverJobs(CancellationToken)` returns `ValueTask` | The `bool` it returned was the constant `true` |
| `DbSemaphore.Sql` and `.InsertSql` are get-only, fed by the constructor | Assigning one after construction left it un-prefixed relative to its pair — see [The semaphores were tidied](#the-semaphores-were-tidied) |
| Row-lock semaphore SQL fields are `protected` and consistently named | `UpdateLockRowSemaphore.SqlUpdateForLock` / `.SqlInsertLock` are `UpdateForLock` / `InsertLock`; `StdRowLockSemaphore.SelectForLock` / `.InsertLock` keep their names |
| `JobStoreSupport.GetEnlistedConnection` is `protected` | So a job store outside the core assembly can honour an enlisted transaction rather than silently opening its own connection |
| `ConnectionAndTransactionHolder` gained an ownership-aware constructor and `OwnsResources` | `(connection, transaction, ownsResources)` for a store running on a connection it did not open |
| `FiredTriggerRecord`, `RecoverMisfiredJobsResult`, `DelegateInitializationArgs` are `sealed record`s | Immutable, with `required` / `init` members instead of settable ones — see [The driver delegate speaks in records](#the-driver-delegate-speaks-in-records) |
| `FiredTriggerRecord.FireInstanceState` is a `StoredTriggerState` | The last raw `AdoConstants.State*` comparisons in the store; `[Serializable]` is gone with it, and the always-populated members are non-nullable |
| `RecoverMisfiredJobsResult.EarliestNewTime` is `EarliestNewTimeUtc` | The property and its constructor argument disagreed about the `Utc` suffix |
| `TriggerAcquireResult` carries a `TriggerKey` | It carried `TriggerName` and `TriggerGroup`, which every caller immediately paired back up |
| `TriggerStatus` removed, `IDriverDelegate.SelectTriggerStatus` is `SelectTriggerHeader` | It returns `StoredTriggerHeader`, an immutable record whose state is a `StoredTriggerState` — see [The driver delegate speaks in records](#the-driver-delegate-speaks-in-records) |
| `IDriverDelegate.ValidateSchema` added | Schema validation was a `StdAdoDelegate` method reached by type test, so a delegate of your own silently skipped it — see [`ValidateSchema` is part of `IDriverDelegate`](#validateschema-is-part-of-idriverdelegate) |
| `StdAdoDelegate`'s column probes removed | The three `Has*Column` properties, the three `Supports*Column` probes and `VerifyTriggersTableReachable`. The columns they probed for are required on 4.x, so the schema migration replaces them — see [The optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone) |
| `GetSelectNextTriggerToAcquireWith*Sql` removed | The `…WithExecutionGroupSql`, `…WithPreferredNodeSql` and `…WithPreferredNodeOnlySql` hooks, on `StdAdoDelegate` and all six dialect delegates. One statement covers every case now, so a dialect delegate keeps only its `GetSelectNextTriggerToAcquireSql` override — see [The three extra acquisition SQL hooks went with them](#the-three-extra-acquisition-sql-hooks-went-with-them) |
| `IDbConnectionManager` / `DbConnectionManager` moved to `Quartz.Impl.AdoJobStore.Common` | And `AddConnectionProvider` / `GetConnectionProvider` are `AddDbProvider` / `GetDbProvider` — see [The connection manager lives with the other ADO.NET types](#the-connection-manager-lives-with-the-other-ado-net-types) |
| `DbMetadataFactory` is internal | Every implementation was already internal and no public member accepted one; describe a driver through `UseGenericDatabase`'s metadata callback |
| `DbProvider.PropertyDbProvider` and `.DbProviderResourceName` removed | Two `protected const`s nothing read, left over from the process-wide provider registry |
| `SimplePropertiesTriggerPersistenceDelegateSupport`'s four SQL statements are private | `SelectSimplePropsTrigger`, `DeleteSimplePropsTrigger`, `InsertSimplePropsTrigger` and `UpdateSimplePropsTrigger` name every column the base class binds, so replacing one could not work. The table and column name constants stay `protected` — they are the schema contract |
| `RAMJobStore` is `sealed` and has no `virtual` members | Wrap it in a store deriving from the new `DelegatingJobStore` instead of deriving from it — see [`RAMJobStore` is sealed](#ramjobstore-is-sealed) |
| `Quartz.Impl.DelegatingJobStore` added | Forwards every `IJobStore` member to a wrapped store, each one `virtual`, so a decorating store overrides only what it changes — see [`DelegatingJobStore` decorates a store](#delegatingjobstore-decorates-a-store) |
| `HostnameInstanceIdGenerator` is `HostNameInstanceIdGenerator` | Casing matched to `HostNameBasedIdGenerator`. The type is internal; a `quartz.scheduler.instanceIdGenerator.type` still naming the old spelling resolves, with a warning |
| `AddJob<T>` and `ScheduleJob<T>` register the job type | Scoped, with `TryAdd`, so an unresolvable job fails `ValidateOnBuild` instead of at fire time — see [`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container) |
| The `JobKey`-taking `AddJob` overloads removed | Identity is set inside the configurator with `WithIdentity` — see [One shape per registration method](#one-shape-per-registration-method) |
| The non-generic `AddTrigger` pair removed | `AddTrigger<IJob>(…)` is the same registration, said once |
| `IServiceCollectionQuartzConfigurator` is `IQuartzBuilder` | And the `AddQuartz` overloads taking an `(configurator, IServiceProvider)` callback are gone; use the `(IServiceProvider, configurator)` shape of `AddJob` / `AddTrigger` / `ScheduleJob` |
| DI `AddCalendar` takes `AddCalendarOptions` | The two adjacent bools are gone, and `calendarName` is `name` |
| `AddPlugin` shapes aligned to the listener trio | The name is an optional trailing argument on all three — see [Plugins are registered like listeners](#plugins-are-registered-like-listeners) |
| `AddQuartzSchedulers(IConfiguration, …)` added | `AddQuartz(configuration)` no longer fans out over a `Schedulers` section; it throws and points here |
| `QuartzHostedService` takes an `IServiceProvider` and an `IOptionsMonitor` | It resolves every scheduler in the container when the host starts — see [The hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler) |
| `AddQuartzHostedService(string schedulerName, …)` added | `QuartzHostedServiceOptions` are named options; the unnamed call still configures every scheduler |
| `IQuartzBuilder.AddHttpApi` / `MapQuartzApi` renamed | `AddQuartzHttpApi` / `MapQuartzHttpApi`; `AddQuartzHealthChecks` gained an `IQuartzBuilder` overload |
| `QuartzHealthCheckOptions.Tags` is a settable `IReadOnlyCollection<string>` | Assign `["ready", "live"]` rather than calling `Add` twice |
| `QuartzSchedulerBuilder.Build()` returns `StandaloneSchedulerFactory` | It is an `ISchedulerFactory` that is also `IAsyncDisposable` and `IDisposable`, so disposing the container needs no cast |
| `JobBuilder<TJob>.Key` is public | Reports the identity the builder was given, or `null` when none was set, so a trigger registered alongside a job can agree with it |
| `ISchedulerProxyFactory` and `HttpSchedulerProxyFactory` removed | Nothing read them — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `quartz.scheduler.proxy*` and `quartz.scheduler.exporter*` are rejected | They were whitelisted but read by nobody; the exception names the replacement |
| `[Serializable]` removed from 30 types | It stays only on the types a job store blob can be made of — see [`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it) |
| The `(SerializationInfo, StreamingContext)` constructors removed | On `SchedulerException`, `JobPersistenceException`, `SchedulerConfigException`, `UnableToInterruptJobException` and `HttpClientException`. `BinaryFormatter` was their only caller, and the base class library's equivalent is obsolete |
| `[Serializable]` removed from `JobExecutionContextImpl` and `SchedulerContext` | Neither is persisted; `SchedulerContext` also lost its private deserialization constructor |
| `JobExecutionProcessException` and `JobInstantiationException` moved to `Quartz` | `Quartz.Core` is internal now — see [The two exceptions moved out of `Quartz.Core`](#the-two-exceptions-moved-out-of-quartz-core) |
| `ExecutionLimits` split into a builder and a snapshot | `ExecutionLimitsBuilder` mutates, `ExecutionLimits` is immutable and is no longer an `IReadOnlyDictionary<string, int?>` — see [Execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen) |
| `IQuartzBuilder.UseExecutionLimits` takes an `Action<ExecutionLimitsBuilder>` | The lambda body is unchanged |
| `TriggerAcquisitionRequest.ExecutionLimits` and `TriggerAcquisitionCriteria.ExecutionLimits` are `ExecutionLimits?` | They were `IReadOnlyDictionary<string, int?>?`; spend the slots through `CreateSlots()` — see [A job store is handed the limits, and a way to spend them](#a-job-store-is-handed-the-limits-and-a-way-to-spend-them) |
| `Quartz.ExecutionSlots` and `Quartz.ExecutionGroupLimit` added | The slot-counting rule and one group's entry in a snapshot, both of which a job store outside Quartz needs to honour execution groups |
| `ICancellableJobExecutionContext` removed | Interruption is `IScheduler.Interrupt` / `InterruptFireInstance` to request and `IJobExecutionContext.CancellationToken` to observe — see [Interruption has two names, not three](#interruption-has-two-names-not-three) |
| `Quartz.Diagnostics.IJobDiagnosticData` removed | It was the payload contract of the `DiagnosticSource` events `Quartz.OpenTracing` consumed. Both the package and the events are gone; job execution is on `Activity` through `QuartzActivitySource`, and `IJobExecutionContext` is what a listener reads |
| `CronExpression.Clone()` returns `CronExpression` | It returned `object`, unlike `ITrigger.Clone`, `IJobDetail.Clone` and `ICalendar.Clone`; the casts at the call sites can go |
| `IJobExecutionContext.Put` / `.Get` take a `string` key | They took `object`. The volatile per-execution map keys by name, like `JobDataMap`, and `Put`'s value is `object?` and its parameter is `value` rather than `objectValue` |
| `JobDataMap`'s sixty typed accessors removed | The inherited `StringKeyDirtyFlagMap` set does the same job — see [`JobDataMap`'s typed accessors are the ones it inherits](#jobdatamap-s-typed-accessors-are-the-ones-it-inherits) |
| `Quartz.AspNetCore.AddQuartzServer` removed | `AddQuartzHostedService` starts the scheduler and `AddQuartzHealthChecks` registers the check — see [`AddQuartzServer` is `AddQuartzHostedService`](#addquartzserver-is-addquartzhostedservice) |
| `ISchedulerFactory.GetScheduler(name)` is `LookupScheduler(name)` | Two members named `GetScheduler` differed only in nullability. `GetScheduler()` builds this factory's scheduler and cannot return null; `LookupScheduler(name)` looks one up in the container's repository and can, which is what the verb now says. `Lookup` matches `ISchedulerRepository.Lookup` |
| `TriggerUtils` moved to `Quartz.Extensibility` | It is a helper over `IOperableTrigger`, not part of the scheduling API — see [`TriggerUtils` moved to `Quartz.Extensibility`](#triggerutils-moved-to-quartz-extensibility) |
| `Quartz.Util.ObjectExtensions` is internal | `AssemblyQualifiedNameWithoutVersion()` is how Quartz spells a type name into a blob or onto the wire, not a general-purpose helper |
| `TimeZoneUtil.CustomResolver` is nullable | `null` means there is no custom resolver, which is how one is removed; it defaulted to a lambda returning `null` — see [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient) |
| `Quartz.Diagnostics.ActivityOptions` is `ActivityTags` | It holds `Activity` tag names, not options, and `*Options` names an options type everywhere else. It replaced 3.x's `DiagnosticHeaders`; the tag names and values are unchanged |
| `DBSemaphore` is `DbSemaphore` | The last `DB` spelling, with `DBConnectionManager` → `DbConnectionManager`. The type is abstract and is never named in configuration |
| `StartingDailyAt` / `EndingDailyAt` take a `timeOfDay` | The parameter was `timeOfDayUtc`, and the value is wall-clock in the trigger's time zone rather than UTC — the property it sets, `StartTimeOfDay`, never claimed otherwise. `DailyTimeIntervalTriggerImpl`'s five constructors say `startTimeOfDay` / `endTimeOfDay` for the same reason |
| `ITriggerSerializer.TriggerTypeForJson` is `TriggerTypeName` | `ICalendarSerializer.CalendarTypeName` names the same concept; both JSON serializers changed |
| `IDriverDelegate.SelectNumTriggersForJob` is `CountTriggersForJob` | Matching `CountMisfiredTriggersInState`, and spelling out the last `Num` |
| `SimpleTriggerImpl.ComputeNumTimesFiredBetween` is `ComputeNumberOfTimesFiredBetween` | As above |
| `TriggerAcquireResult.JobType` is `JobTypeName` | It holds `JOB_CLASS_NAME` — a type name, the same thing `JobHeader.JobTypeName` carries — and was documented as a discriminator, which it is not. `TriggerHeader.TriggerType` really is a discriminator and keeps its name |
| Parameter names spelled out on the ADO.NET surface | `IDriverDelegate.SelectJobDetail`'s `classLoadHelper` is `loadHelper` (the implementation already called it that, so named arguments disagreed with the interface); `ts` is `misfireTime`; `JobStoreSupport.ReleaseLock`'s `doIt` is `shouldRelease`; `StdAdoDelegate.AddTriggerPersistenceDelegate`'s `del` is `persistenceDelegate`; `TriggerPropertyBundle`'s `sb` is `scheduleBuilder`; `CronTriggerImpl.WillFireOn`'s `test` is `timeUtc`; `TriggerUtils.ComputeFireTimes`'s `numTimes` is `numberOfTimes` |

## Appendix: what happened to a name

The guide above is organised by topic, which is the right shape for reading it and the wrong shape
for the question a broken build actually asks — *what happened to this one type?* This appendix is
the index for that question, and it links back to the section that explains each entry rather than
explaining it a second time.

It is derived mechanically, by diffing the public API baselines both branches keep under
`src/Quartz.Tests.Unit/Verify/` and `src/Quartz.Tests.AspNetCore/Verify/`, so it names **every**
public type 3.x had and 4.0 does not — all 92, across every package — rather than the ones that came
to mind.

Two whole-surface changes are deliberately left out, because repeating them per type would bury
everything else: `Task` became [`ValueTask`](#tasks-changed-to-valuetask) on nearly every member, and
the namespaces moved — [`Quartz.Spi` is `Quartz.Extensibility` and `Quartz.Simpl` is
`Quartz.Impl`](#quartz-spi-and-quartz-simpl-were-renamed), and three packages folded into `Quartz`
(see [Package Changes](#package-changes)). **A type that only changed namespace keeps its name and is
not listed here**, so if you cannot find one below, that is the first thing to check.

*Internal* below means the type is still there and still doing its job — it is simply no longer part
of the contract. If you were deriving from one of them, please
[open an issue](https://github.com/quartznet/quartznet/issues) rather than working around it.

Both tables are ordered by the name you would have typed — the type's own name, ignoring its
namespace, and `Type.Member` for the second one.

### Types that were removed, internalized or renamed

| 3.x type | What happened | What to use instead |
|---|---|---|
| `Quartz.Impl.AdoJobStore.AdoJobStoreUtil` | Internal | Nothing; it built statement text, which is not a contract — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.AdoProviderExtensions` | Renamed `PersistentStoreBuilderExtensions` | Same `Use*` methods, extending `IPersistentStoreBuilder` instead of `SchedulerBuilder.PersistentStoreOptions`. Two of them swapped meaning — see [The SQLite extension methods swapped names](#the-sqlite-extension-methods-swapped-names) |
| `Quartz.SchedulerBuilder.AdoProviderOptions` | Removed | `DataSourceOptions` — see [A data source is defined, referred to, or handed over](#a-data-source-is-defined-referred-to-or-handed-over) |
| `Quartz.AdoProviderOptionsExtensions` | Removed | It held only `UseDataSourceConnectionProvider()`; set `DataSourceOptions.UseRegisteredDataSource` — see [`AddDataSourceProvider()` went with it](#adddatasourceprovider-went-with-it) |
| `Quartz.Impl.AdoJobStore.AdoUtil` | Internal, with `IAdoUtil` | Nothing; parameter binding is not an extension point — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Simpl.BinaryObjectSerializer` | Removed | `SystemTextJsonObjectSerializer` or `NewtonsoftJsonObjectSerializer`; there is no binary serializer, because `BinaryFormatter` throws on .NET 9 — see [`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it) |
| `Quartz.CalendarIntervalTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.SchedulerBuilder.ClusterOptions` | Removed | `ClusteringOptions` — see [Clustering is configured in one place](#clustering-is-configured-in-one-place) |
| `Quartz.Impl.AdoJobStore.Common.ConfigurationBasedDbMetadataFactory` | Internal | The metadata callback on `UseGenericDatabase` |
| `Quartz.CronScheduleTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.DailyTimeIntervalTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Util.DataReaderExtensions` | Internal | No replacement; they were `IDataReader` conveniences Quartz used on its own reads |
| `Quartz.Util.DBConnectionManager` | Renamed `Quartz.Impl.AdoJobStore.Common.DbConnectionManager` | Resolve `IDbConnectionManager` from the container; `.Instance` is gone — see [The connection manager lives with the other ADO.NET types](#the-connection-manager-lives-with-the-other-ado-net-types) |
| `Quartz.Impl.AdoJobStore.Common.DbMetadataFactory` | Internal | The metadata callback on `UseGenericDatabase` |
| `Quartz.Impl.AdoJobStore.DBSemaphore` | Renamed `DbSemaphore` | Same abstract base, still public — see [The semaphores were tidied](#the-semaphores-were-tidied) |
| `Quartz.Simpl.DedicatedThreadPool` | Internal | `IQuartzBuilder.UseThreadPool(IThreadPool)` for a pool of your own — see [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `Quartz.Logging.DiagnosticHeaders` | Renamed `Quartz.Diagnostics.ActivityTags` | The tag names and values are unchanged — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Util.DictionaryExtensions` | Removed | No replacement — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Impl.DirectSchedulerFactory` | Removed | `QuartzSchedulerBuilder`, with `UseThreadPool(IThreadPool)` / `UseJobStore(IJobStore)` for pre-built parts — see [Removed](#removed) |
| `Quartz.Impl.AdoJobStore.Common.EmbeddedAssemblyResourceDbMetadataFactory` | Internal | The metadata callback on `UseGenericDatabase` |
| `Quartz.Util.FileUtil` | Internal | No replacement; it resolved a path relative to the base directory |
| `Quartz.Simpl.HostnameInstanceIdGenerator` | Renamed `HostNameInstanceIdGenerator`, and internal | A `quartz.scheduler.instanceIdGenerator.type` naming the old spelling still resolves, with a warning; in code, register your own `IInstanceIdGenerator` — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.ICancellableJobExecutionContext` | Removed | `IScheduler.Interrupt` to request, `IJobExecutionContext.CancellationToken` to observe — see [Interruption has two names, not three](#interruption-has-two-names-not-three) |
| `Quartz.IDashboardAuthorizationFilter` | Removed | `QuartzDashboardOptions.AuthorizationPolicy`; nothing ever invoked the filter — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Logging.IJobDiagnosticData` | Removed | `IJobExecutionContext`, read from a listener; the `DiagnosticSource` events it was the payload of are gone — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Core.IJobRunShellFactory` | Internal | No replacement; how a fire is wrapped is not a contract |
| `Quartz.IJobWrapper` | Removed | Per-fire state rides in `JobScope.State` — see [The job factory hands out a scope](#the-job-factory-hands-out-a-scope) |
| `Quartz.Logging.ILogProvider` | Removed with LibLog | `ILoggerFactory` — see [Logging](#logging) |
| `Quartz.SchedulerBuilder.InMemoryStoreOptions` | Removed | `InMemoryJobStoreOptions`, through `UseInMemoryStore(configure)` |
| `Quartz.Dashboard.Services.InProcessQuartzApiClient` | Internal | Resolve `IQuartzApiClient` — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Simpl.InternalTriggerState` | Internal | No replacement; it is `RAMJobStore`'s own bookkeeping — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.IPropertyConfigurationRoot` | Removed | Typed options — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.IPropertyConfigurer` | Removed | Typed options — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.IPropertySetter` | Removed | Typed options — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.Dashboard.Services.IQuartzApiClientExecutionLimits` | Removed | `IQuartzApiClient`, which carries `GetExecutionLimits` itself |
| `Quartz.Simpl.IRemotableQuartzScheduler` | Removed | Nothing; .NET Remoting is not supported — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Spi.IRemotableSchedulerProxyFactory` | Removed | Nothing; `Quartz.HttpClient` talks to a remote scheduler over HTTP — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Spi.ISchedulerExporter` | Removed | Nothing; `AddQuartzHttpApi` / `MapQuartzHttpApi` serve a scheduler over HTTP — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.IServiceCollectionQuartzConfigurator` | Renamed `IQuartzBuilder` | The same members, on one interface shared with the standalone builder — see [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder) |
| `Quartz.Impl.JobDetailImpl` | Internal | `JobBuilder.Create<TJob>()`; read an `IJobDetail` |
| `Quartz.JobFactoryOptions` | Removed | Nothing; both of its properties were already `[Obsolete]` no-ops in 3.x — see [The job factory hands out a scope](#the-job-factory-hands-out-a-scope) |
| `Quartz.Core.JobRunShell` | Internal | No replacement; use `IJobListener` to observe a fire |
| `Quartz.Impl.AdoJobStore.JobStoreCMT` | Renamed `ExternalTransactionJobStore` | The old spelling still resolves in configuration, with a warning — see [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use) |
| `Quartz.Impl.AdoJobStore.JobStoreTX` | Renamed `LocalTransactionJobStore` | As above — see [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use) |
| `Quartz.Simpl.JsonObjectSerializer` | Renamed `Quartz.Serialization.Newtonsoft.NewtonsoftJsonObjectSerializer` | `UseNewtonsoftJsonSerializer()` registers it — see [JSON Serialization](#json-serialization) |
| `Quartz.JsonSchedulingOptions` | Merged into `FileSchedulingOptions` | It was byte-for-byte identical to `XmlSchedulingOptions` — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.JsonSerializerOptions` | Renamed `Quartz.Serialization.Newtonsoft.NewtonsoftJsonSerializerOptions` | See [Newtonsoft types moved out of the core namespaces](#newtonsoft-types-moved-out-of-the-core-namespaces) |
| `Quartz.Logging.LogProviders.LibLogException` | Removed with LibLog | No replacement — see [Logging](#logging) |
| `Quartz.Core.ListenerManagerImpl` | Internal | `IScheduler.ListenerManager`, typed `IListenerManager` — see [Listener API Changes](#listener-api-changes) |
| `Quartz.Logging.LogContext` | Removed with LibLog | `LogProvider.SetLogProvider(ILoggerFactory)` — see [Logging](#logging) |
| `Quartz.Logging.Logger` (delegate) | Removed with LibLog | `ILogger` — see [Logging](#logging) |
| `Quartz.Logging.LogLevel` | Removed with LibLog | `Microsoft.Extensions.Logging.LogLevel` — see [Logging](#logging) |
| `Quartz.Util.ObjectExtensions` | Internal | No replacement — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Util.ObjectUtils` | Internal | No replacement; it set properties reflectively from strings, which typed options removed the need for — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.SchedulerBuilder.PersistentStoreOptions` | Removed | `IPersistentStoreBuilder`, through `UsePersistentStore(configure)` |
| `Quartz.PropertiesHolder` | Removed | Typed options — see [Removed](#removed) |
| `Quartz.Util.PropertiesParser` | Internal | No replacement; `QuartzPropertyBridge` is the only reader of flat `quartz.*` keys now — see [Flat keys still work](#flat-keys-still-work) |
| `Quartz.PropertiesSetter` | Removed | Typed options — see [Removed](#removed) |
| `Quartz.QuartzConfiguratorExecutionLimitsExtensions` | Removed | `IQuartzBuilder.UseExecutionLimits(Action<ExecutionLimitsBuilder>)` — see [Execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen) |
| `Quartz.OpenTracing.QuartzDiagnosticOptions` | Removed with its package | [OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz); job execution is on `Activity` through `QuartzActivitySource` |
| `Quartz.Util.QuartzEnvironment` | Internal | `System.Environment`, or `IConfiguration` for settings |
| `Quartz.Core.QuartzRandom` | Internal | `System.Random` |
| `Quartz.Core.QuartzScheduler` | Internal | Resolve `IScheduler` or `ISchedulerFactory` — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Core.QuartzSchedulerResources` | Internal | `QuartzSchedulerOptions` — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Core.QuartzSchedulerThread` | Internal | No replacement; the scheduling loop is not an extension point |
| `Quartz.RecurrenceTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.RemoteScheduler` | Removed | `Quartz.HttpClient` — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Simpl.RemotingSchedulerProxyFactory` | Removed | As above — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.ScheduleBuilder<T>` | Removed | Implement `IScheduleBuilder` directly — see [`ScheduleBuilder<T>` is gone](#schedulebuilder-t-is-gone) |
| `Quartz.SchedulerBuilder` | Renamed `QuartzSchedulerBuilder` | And it implements `IQuartzBuilder`, so its configuration members return that — see [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder) |
| `Quartz.SchedulerExtensions` | Removed | Its three members are on `IScheduler` itself: `GetExecutionLimits`, `SetExecutionLimits`, `UpdateTriggerDetails` |
| `Quartz.SchedulerMetaData` | Renamed `SchedulerMetadata` | A `sealed record`, returned by `IScheduler.GetMetadata()` — see [`SchedulerMetadata` replaces `SchedulerMetaData`](#schedulermetadata-replaces-schedulermetadata) |
| `Quartz.SchedulerPluginConfigurationExtensions` | Removed | `IQuartzBuilder.AddPlugin<T>()` — see [Plugins are registered like listeners](#plugins-are-registered-like-listeners) |
| `Quartz.Core.SchedulerSignalerImpl` | Internal | Take `ISchedulerSignaler` through your constructor — see [SPI changes](#spi-changes) |
| `Quartz.Simpl.SimpleInstanceIdGenerator` | Internal | It is still the default; register your own `IInstanceIdGenerator` to replace it |
| `Quartz.SimpleScheduleTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.AdoJobStore.SimpleSemaphore` | Internal | It is the in-process lock the ADO.NET store falls back to when database locking is off; implement `ISemaphore` for a lock of your own — see [Locks are a `SchedulerLock`, not a string](#locks-are-a-schedulerlock-not-a-string) |
| `Quartz.Simpl.SimpleTypeLoadHelper` | Internal | Register your own `ITypeLoadHelper` |
| `Quartz.Impl.AdoJobStore.StdAdoConstants` | Internal | `AdoConstants` for table, column and state names; statement text is not a contract — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Impl.StdJobRunShellFactory` | Internal | No replacement; see `IJobRunShellFactory` above |
| `Quartz.Impl.StdScheduler` | Internal | Resolve `IScheduler` — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Impl.StdSchedulerFactory` | Removed, with all 47 constants | `QuartzSchedulerBuilder.Create().UseProperties(properties)` — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone) for every constant and member |
| `Quartz.SchedulerBuilder.StoreOptions` | Removed | Nothing; it was the base of the two store option classes, which are now `InMemoryJobStoreOptions` and `IPersistentStoreBuilder` |
| `Quartz.Util.StringExtensions` | Internal | No replacement |
| `Quartz.Simpl.SystemPropertyInstanceIdGenerator` | Internal | `quartz.scheduler.instanceId = SYS_PROP` still selects it; in code, register your own `IInstanceIdGenerator` |
| `Quartz.SystemTime` | Removed | `TimeProvider` — see [SystemTime Replaced with TimeProvider](#systemtime-replaced-with-timeprovider) |
| `Quartz.TimeOfDay` | Removed | `TimeOnly` — see [`TimeOfDay` became `TimeOnly`](#timeofday-became-timeonly) |
| `Quartz.TriggerExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.AdoJobStore.TriggerStatus` | Removed | `StoredTriggerHeader`, returned by `IDriverDelegate.SelectTriggerHeader` — see [The driver delegate speaks in records](#the-driver-delegate-speaks-in-records) |
| `Quartz.TriggerTimeComparator` | Internal | No replacement; it ordered by next fire time, then priority descending, then key — write that inline if you need it |
| `Quartz.Simpl.TriggerWrapper` | Internal | No replacement; it is `RAMJobStore`'s per-trigger state — see [`RAMJobStore` is sealed](#ramjobstore-is-sealed) |
| `Quartz.XmlSchedulingOptions` | Merged into `FileSchedulingOptions` | See [Other Breaking Changes](#other-breaking-changes) |

### Members that were removed

A member whose own type is listed above is not repeated here, and neither is one that went with a
sealing — a type that became `sealed` took its `protected` surface with it. What is left is the
removals on types that are still public and still open, which no section above names.

| 3.x member | What happened | What to use instead |
|---|---|---|
| `AbstractTrigger.CompareTo(ITrigger)` | Removed; `AbstractTrigger` no longer implements `IComparable<ITrigger>` | It compared keys — `trigger.Key.CompareTo(other.Key)` |
| `AbstractTrigger.FullJobName` | Removed | `JobKey.ToString()`, alongside the four in [AbstractTrigger Property Removals](#abstracttrigger-property-removals) |
| `CronExpression`'s `protected` constants and fields | Gone with the type, which is `sealed` now | No replacement; the parsed sets were never a contract — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `CronTriggerImpl.GetTimeAfter(DateTimeOffset)` | Removed (it was `protected`) | `GetFireTimeAfter(DateTimeOffset?)`, or `CronExpression.GetTimeAfter` for the expression on its own |
| `CronTriggerImpl.YearToGiveupSchedulingAt` | Removed (a `protected const`) | No replacement; where the search stops is the expression's business |
| `DateBuilder.ValidateDayOfMonth`, `.ValidateHour`, `.ValidateMinute`, `.ValidateMonth`, `.ValidateSecond`, `.ValidateYear` | Removed | No replacement; the builder validates its own arguments, and it is `sealed` — see [`DateBuilder`'s static factories are gone](#datebuilder-s-static-factories-are-gone) |
| `DbProvider.CreateParameter()` | Removed | `CreateCommand().CreateParameter()` |
| `DbProvider.DbProviderSectionName`, `.GenerateValidProviderNamesInfo()` | Removed (`protected`) | No replacement; leftovers of the process-wide provider registry, like the two named in [Other Breaking Changes](#other-breaking-changes) |
| `DirtyFlagMap.Clone()` | Removed | Construct a new map from the old one |
| `DirtyFlagMap.EntrySet()` | Removed | `GetEnumerator()`, or `foreach` over the map |
| `DirtyFlagMap.KeySet()` | Removed | `Keys` |
| `DirtyFlagMap.Put()`, `.PutAll()` | Removed; both were `[Obsolete]` in 3.x | `map[key] = value`, in a loop for `PutAll` — see [DirtyFlagMap Changes](#dirtyflagmap-changes) |
| `DirtyFlagMap.WrappedMap` | Removed | No replacement; the map *is* the dictionary, and handing out the inner one let a caller write past the dirty flag |
| `IDriverDelegate.UpdateTriggerPreferredNode`, `StdAdoDelegate.UpdateTriggerPreferredNode` | Removed | `UpdateTriggerPreferredNodeConditional`, which is a compare-and-swap, or `IScheduler.UpdateTriggerDetails` from outside the store — see [The preferred node is a value](#the-preferred-node-is-a-value) |
| `JobBuilder.CreateForAsync<T>()` | Removed | `JobBuilder.Create<T>()`; every job has been asynchronous since 3.0 |
| `JobStoreSupport.calendarCache`, `.delegateType`, `.firstCheckIn` | Removed (`protected` fields) | No replacement; they are the base class's own bookkeeping |
| `JobStoreSupport.GetTriggerNames(conn, matcher, ct)` | Removed (`protected`) | The listing members became queries — see [Job store listings became queries](#job-store-listings-became-queries) |
| `LogProvider.IsDisabled` | Removed | No replacement; filter through the `ILoggerFactory` — see [Logging](#logging) |
| `LogProvider.SetCurrentLogProvider(ILogProvider)` | Removed with LibLog | `LogProvider.SetLogProvider(ILoggerFactory)` — see [Logging](#logging) |
| `SimplePropertiesTriggerPersistenceDelegateSupport.SchedNameLiteral`, and the same member on `DbSemaphore` | Removed; both were `[Obsolete]` in 3.x | No replacement; the scheduler name is a SQL parameter, not literal text |
| `StdAdoDelegate.GetStorableJobTypeName(Type)` | Removed (`protected`) | `new JobType(type).FullName`, which is the spelling the `JOB_CLASS_NAME` column holds |
| `StdAdoDelegate.SchedulerNameLiteral` | Removed; it was `[Obsolete]` in 3.x | No replacement; as above |
| `StringKeyDirtyFlagMap.GetKeys()` | Removed | `Keys` |
| `StringKeyDirtyFlagMap.GetNullableGuid()`, `.TryGetNullableGuid()` | Removed | `TryGetGuid(key, out var value)`, whose `false` says the same thing as a `null` did — see [`JobDataMap`'s typed accessors are the ones it inherits](#jobdatamap-s-typed-accessors-are-the-ones-it-inherits) |
| `StringKeyDirtyFlagMap.Put()` (eight overloads), `.PutAll()` | Removed; all were `[Obsolete]` in 3.x | `map[key] = value` |
| `TaskSchedulingThreadPool.ThreadPriority` | Removed | No replacement; work runs on a `TaskScheduler`, which has no thread to prioritise — see [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `ZeroSizeThreadPool.AvailableThreadCount` | Removed | `PoolSize`, which is `0` — the pool never had a thread to report |
