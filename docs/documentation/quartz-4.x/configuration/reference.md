---

title: Configuration Reference
---

# Quartz.NET Configuration Reference

[[toc]]

Quartz is configured with strongly typed options. Every option has the same name whether you set it in
code or in a configuration file, so there is one vocabulary to learn rather than two:

```csharp
services.AddQuartz(q => q.ConfigureScheduler(options => options.MaxBatchSize = 5));
```

```json
{
  "Quartz": {
    "Scheduler": { "MaxBatchSize": 5 }
  }
}
```

Options are bound from the `Quartz` section by section name — `Scheduler`, `ThreadPool`, `JobStore`,
`DataSource` — and validated at startup, so a bad value is reported against the setting that is wrong
rather than failing later during scheduling.

::: tip
Everything on this page can also be written as flat `quartz.*` keys, which earlier versions used and
which Quartz still accepts. See [Legacy property keys](#legacy-property-keys).
:::

## Scheduler

`QuartzSchedulerOptions`, bound from `Quartz:Scheduler`.

| Option | Type | Default | Description |
|---|---|---|---|
| `InstanceName` | string | `QuartzScheduler` | Distinguishes schedulers in the same process. Every node in a cluster must share one name. |
| `InstanceId` | string | `NON_CLUSTERED` | Must be unique among the nodes of a cluster. |
| `GenerateInstanceId` | bool | `false` | Derives `InstanceId` at startup from the registered `IInstanceIdGenerator` instead of using the literal value. |
| `IdleWaitTime` | TimeSpan | `00:00:30` | How long to wait before re-querying the job store when nothing is due. Must be at least one second. |
| `MaxBatchSize` | int | `1` | How many triggers may be acquired at once. |
| `BatchTriggerAcquisitionFireAheadTimeWindow` | TimeSpan | `00:00:00` | How far ahead of its fire time a trigger may be included in the current batch. |
| `InterruptJobsOnShutdown` | bool | `false` | Signals cancellation to running jobs on shutdown. |
| `InterruptJobsOnShutdownWithWait` | bool | `false` | Signals cancellation on a shutdown that waits for jobs to finish. |
| `Context` | dictionary | empty | Values seeded into `SchedulerContext`. |

```csharp
services.AddQuartz(q => q.ConfigureScheduler(options =>
{
    options.InstanceName = "core";
    options.InstanceId = "node-1";
    options.MaxBatchSize = 5;
    options.InterruptJobsOnShutdown = true;
}));
```

## Thread pool

`ThreadPoolOptions`, bound from `Quartz:ThreadPool`.

| Option | Type | Default | Description |
|---|---|---|---|
| `MaxConcurrency` | int | `10` | How many jobs may run at once. |

```csharp
services.AddQuartz(q => q.UseDefaultThreadPool(maxConcurrency: 20));
```

To supply your own implementation:

```csharp
services.AddQuartz(q => q.UseThreadPool<MyThreadPool>());
```

`ThreadPoolOptions` belongs to the built-in pools — they are what read `MaxConcurrency` — so
`UseThreadPool<T>()` takes no callback for it. A pool of your own has options of its own:

```csharp
services.AddQuartz(q =>
{
    q.ConfigureOptions<MyThreadPoolOptions>(options => options.Slots = 20);
    q.UseThreadPool<MyThreadPool>();
});
```

`ConfigureOptions<TOptions>` registers the callback under this scheduler's options name and declares the
type as the scheduler's own, so a component that takes `IOptions<MyThreadPoolOptions>` through its
constructor is handed what was configured for *its* scheduler rather than the unnamed instance. It works
for any container-built component — a job store, a lock handler, a listener, a job factory — and
`AddPlugin<T, TOptions>()` is sugar over it.

## In-memory job store

`InMemoryJobStoreOptions`, bound from `Quartz:JobStore`. The in-memory store is the default and does not
survive process restarts.

| Option | Type | Default | Description |
|---|---|---|---|
| `MisfireThreshold` | TimeSpan | `00:00:05` | How late a trigger may fire before it counts as misfired. |

```csharp
services.AddQuartz(q => q.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(30)));
```

## Persistent job store

`AdoJobStoreOptions`, bound from `Quartz:JobStore`. Choosing a database also selects the driver delegate
that speaks its SQL dialect, so a connection string is all you normally supply:

```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.UseSystemTextJsonSerializer();
}));
```

| Option | Type | Default | Description |
|---|---|---|---|
| `TablePrefix` | string | `QRTZ_` | Prefix on every Quartz table name. |
| `UseProperties` | bool | `false` | Persists job data as name/value strings rather than serialized objects, which keeps stored data readable and version tolerant. |
| `MisfireThreshold` | TimeSpan | `00:01:00` | How late a trigger may fire before it counts as misfired. |
| `MisfireHandlerFrequency` | TimeSpan? | `MisfireThreshold` | How often misfires are handled. |
| `MaxMisfiresToHandleAtATime` | int | `20` | How many misfired triggers are handled per pass. |
| `DbRetryInterval` | TimeSpan | `00:00:15` | How long to wait before retrying after a database failure. |
| `MaxTransientRetries` | int | `3` | How many times a transient failure such as a deadlock is retried. |
| `TransientRetryInterval` | TimeSpan | `00:00:01` | Delay between transient retries. |
| `RetryableActionErrorLogThreshold` | int | `4` | How many consecutive failures before they are logged as errors. |
| `UseDbLocks` | bool | `false` | Uses database row locks. Required for clustering, and implied by `UseClustering()`. |
| `LockOnInsert` | bool | `true` | Takes a lock when inserting rows. |
| `AcquireTriggersWithinLock` | bool | `false` | Acquires triggers inside the database lock. |
| `TxIsolationLevelSerializable` | bool | `false` | Uses the serializable isolation level. |
| `AcceptEnlistedTransactions` | bool | `false` | Lets the job store use a connection the application enlisted with `SchedulerEnlistmentExtensions.EnlistTransaction`, so scheduling commits with the application's own work. See [Joining an existing transaction](../tutorial/job-stores.md#joining-an-existing-transaction). |
| `DoubleCheckLockMisfireHandler` | bool | `true` | Re-checks the lock before handling misfires. |
| `UseBackgroundThreads` | bool | `false` | Runs the misfire handler and cluster manager on background threads, which do not keep the process alive. These two are the only real threads Quartz creates. |
| `PerformSchemaValidation` | bool | `true` | Verifies the expected tables exist at startup. |
| `SelectWithLockSql` | string? | none | Overrides the row-lock statement. |
| `OpenConnection` | bool | `false` | Whether `ExternalTransactionJobStore` opens the connections it creates; read only by that store. |

A custom trigger persistence delegate is registered with
`UsePersistentStore(s => s.UseTriggerPersistenceDelegate<T>())` rather than through an option; the
legacy `quartz.jobStore.driverDelegateInitString` key still translates to the same registrations.

### Databases

| Method | Database |
|---|---|
| `UseSqlServer` | Microsoft SQL Server |
| `UsePostgres` | PostgreSQL |
| `UseMySql` | MySQL, using the MySql.Data driver |
| `UseMySqlConnector` | MySQL, using the MySqlConnector driver |
| `UseOracle` | Oracle |
| `UseFirebird` | Firebird |
| `UseSqlite` | SQLite, using the Microsoft.Data.Sqlite driver |
| `UseSystemDataSqlite` | SQLite, using the legacy System.Data.SQLite driver |
| `UseGenericDatabase` | Anything else, using the generic SQL dialect — and the only one that can [describe its own driver](#describing-a-driver-quartz-does-not-know) |

Each takes either a connection string or a callback over `DataSourceOptions`:

```csharp
store.UseSqlServer(connectionString);
store.UseSqlServer(db => db.ConnectionStringName = "Scheduler");
```

Where the connection comes from is the data source's own setting, so to connect through a
`DbDataSource` registered in the container rather than a connection string of Quartz's own, say
`store.UseSqlServer(db => db.UseRegisteredDataSource = true)`.

#### Describing a driver Quartz does not know

The provider name each method passes — `SqlServer`, `Npgsql` and so on — names a description of an
ADO.NET driver: which connection, command and parameter types to instantiate, how parameters are named,
and which enum value means "binary column". Quartz ships descriptions for the drivers of every database
listed above. For anything else, describe the driver in the `UseGenericDatabase` call:

```csharp
store.UseGenericDatabase("MyDatabase", connectionString, () => new DbMetadata
{
    ProductName = "My Database",
    AssemblyName = typeof(MyConnection).Assembly.FullName,
    ConnectionType = typeof(MyConnection),
    CommandType = typeof(MyCommand),
    ParameterType = typeof(MyParameter),
    ParameterDbType = typeof(MyDbType),
    ParameterDbTypePropertyName = nameof(MyParameter.MyDbType),
    ParameterNamePrefix = "@",
    ExceptionType = typeof(MyException),
    UseParameterNamePrefixInParameterCollection = true,
    BindByName = true,
    DbBinaryTypeName = "VarBinary",
});
```

There is a four-argument overload taking a `DataSourceOptions` callback instead of a connection string,
for a driver described in code that also uses a named connection string.

A description is a registration in the container rather than process-wide state, so two containers in one
process no longer have to agree on what a provider name means. Within one container a provider name means
one thing, since a name is what a data source points at — two schedulers that need two different drivers
give them two different names.

Describing a name Quartz already ships a description for replaces it, and a description registered in code
wins over one written as `quartz.dbprovider.*` keys. Several drivers means several calls, one per name.

The same thing can be said as properties, which is the form 3.x used and which now arrives through
`IConfiguration` like everything else:

```json
{
  "Quartz": {
    "quartz.dbprovider.MyDatabase.productName": "My Database",
    "quartz.dbprovider.MyDatabase.connectionType": "MyNamespace.MyConnection, MyDriver",
    "quartz.dbprovider.MyDatabase.commandType": "MyNamespace.MyCommand, MyDriver",
    "quartz.dbprovider.MyDatabase.parameterType": "MyNamespace.MyParameter, MyDriver",
    "quartz.dbprovider.MyDatabase.parameterDbType": "MyNamespace.MyDbType, MyDriver",
    "quartz.dbprovider.MyDatabase.parameterDbTypePropertyName": "MyDbType",
    "quartz.dbprovider.MyDatabase.parameterNamePrefix": "@",
    "quartz.dbprovider.MyDatabase.exceptionType": "MyNamespace.MyException, MyDriver",
    "quartz.dbprovider.MyDatabase.useParameterNamePrefixInParameterCollection": "true",
    "quartz.dbprovider.MyDatabase.bindByName": "true",
    "quartz.dbprovider.MyDatabase.dbBinaryTypeName": "VarBinary"
  }
}
```

A store's data source is named after the scheduler that owns it, or `quartz` for the default scheduler.
Connection providers are held per process, so if you run two default schedulers in one process — two
standalone `QuartzSchedulerBuilder`s against different databases — name them apart with
`store.UseDataSourceName("reporting-db")` before choosing the database. Otherwise the second replaces
the first's connection provider and both end up talking to the same database.

### Locking

Leave the lock handler unset and the store chooses one for itself once it knows which database it is
talking to: database row locks when clustered or when `UseDbLocks` is on, and an in-process monitor
otherwise. `UseLockHandler<T>()` overrides that choice, and `UseLockHandler(factory)` does the same for a
handler that needs building — as `UseRedisLockHandler()` does.

Both this and `UseSerializer` register against the scheduler that owns the store. Registering
`ISemaphore` or `IObjectSerializer` directly against `Services` registers it for the container, which a
named scheduler will not see.

### Data source

`DataSourceOptions`, bound from `Quartz:DataSource`.

| Option | Type | Description |
|---|---|---|
| `Provider` | string | Names the description of the ADO.NET driver to use. Set for you by the database methods above; see [Describing a driver Quartz does not know](#describing-a-driver-quartz-does-not-know) for a driver Quartz ships no description for. |
| `ConnectionString` | string? | The connection string. Takes precedence over `ConnectionStringName`. |
| `ConnectionStringName` | string? | A connection string to resolve from `IConfiguration`. |
| `UseRegisteredDataSource` | bool | Connections come from a `DbDataSource` in the container. Wins over both connection string settings. |

To connect through a `DbDataSource` registered in the container, for example by `AddNpgsqlDataSource`:

```csharp
services.AddNpgsqlDataSource(connectionString);
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UsePostgres(db => db.UseRegisteredDataSource = true);
}));
```

There are three entry points for a data source and they say different things.
`UseDataSource(configure)` **defines** one — which driver, and how to reach the database — and the
database methods above are shorthands for it. `UseDataSourceName(name)` **refers to** one by name,
which is how a store picks up settings registered elsewhere, such as a `Quartz:DataSource:<name>`
section. Where the connection itself comes from is `DataSourceOptions`' to say, not a fourth method's.

### Clustering

Clustering lets several schedulers share one database, so that if a node dies its triggers are recovered
by another. Every node must use the same `InstanceName` and a different `InstanceId`.

`ClusteringOptions`, bound from `Quartz:JobStore:Clustering`. This is the only place clustering is
configured: the job store reports whether it is clustered, it does not offer a second place to say so.

| Option | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `false` | Takes part in a cluster sharing this database. `UseClustering()` sets it. |
| `CheckinInterval` | TimeSpan | `00:00:07.5` | How often a node records that it is alive. |
| `CheckinMisfireThreshold` | TimeSpan | `00:00:07.5` | Grace period before a node is treated as failed. |

```csharp
services.AddQuartz(q =>
{
    q.ConfigureScheduler(options =>
    {
        options.InstanceName = "core";
        options.GenerateInstanceId = true;
    });

    q.UsePersistentStore(store =>
    {
        store.UseSqlServer(connectionString);
        store.UseClustering(cluster =>
        {
            cluster.CheckinInterval = TimeSpan.FromSeconds(10);
            cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
        });
        store.UseSystemTextJsonSerializer();
    });
});
```

`UseClustering()` enables database locking as well, because clustering has never worked without it.

## Serialization

A persistent store must be told how to serialize job data.

```csharp
store.UseSystemTextJsonSerializer();
store.UseNewtonsoftJsonSerializer();   // Quartz.Serialization.Newtonsoft
```

## Job factory

By default jobs are resolved from the container, in a scope created per firing, so a job may take scoped
dependencies. To replace it:

```csharp
services.AddQuartz(q => q.UseJobFactory<MyJobFactory>());
```

## Listeners, calendars and plugins

```csharp
services.AddQuartz(q =>
{
    q.AddSchedulerListener<MySchedulerListener>();
    q.AddJobListener<MyJobListener>(GroupMatcher<JobKey>.GroupEquals("reports"));
    q.AddTriggerListener<MyTriggerListener>();
    q.AddPlugin<MyPlugin>();
});
```

Listeners and plugins are ordinary services, so they take their dependencies through their constructors.

## Several schedulers

Registering a scheduler under a name gives it its own job store, thread pool, jobs and configuration.
The name is the scheduler's instance name, the key its services are registered under, and the name of
its options.

```csharp
services.AddQuartz("reporting", q => q.UsePersistentStore(store => store.UseSqlServer(reportingDb)));
services.AddQuartz("ingest", q => q.UseInMemoryStore());
```

Resolve them by name:

```csharp
var reporting = await serviceProvider
    .GetRequiredKeyedService<ISchedulerFactory>("reporting")
    .GetScheduler();
```

In configuration, use a `Schedulers` section:

```json
{
  "Quartz": {
    "Schedulers": {
      "reporting": { "ThreadPool": { "MaxConcurrency": 5 } },
      "ingest":    { "ThreadPool": { "MaxConcurrency": 20 } }
    }
  }
}
```

## Without a container

Console applications and tests that have no host build a scheduler with `QuartzSchedulerBuilder`. It
does not take *the same* configuration API — it **is** the configuration API: `QuartzSchedulerBuilder`
implements `IQuartzBuilder`, the interface `AddQuartz` hands out, over a container it creates itself.

```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .ConfigureScheduler(options => options.InstanceName = "reporting")
    .UseDefaultThreadPool(maxConcurrency: 20)
    .UseInMemoryStore()
    .BuildScheduler();
```

What it adds is the two terminal methods a standalone caller needs, `Build()` for the factory and
`BuildScheduler()` for the scheduler. Every configuration member returns `QuartzSchedulerBuilder`, so
the chain reaches them. The `IQuartzBuilder` extension methods — `AddJob`, `AddTrigger`,
`ScheduleJob`, `AddCalendar` — return the interface, so put those last or in a statement of their own.

A scheduler configured entirely by flat `quartz.*` keys is built the same way:

```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .BuildScheduler();
```

`UseProperties` checks the keys against the ones Quartz reads, so a misspelling is reported rather
than silently ignored; set `quartz.checkConfiguration` to `false` to allow keys of your own.
Configuration written in code wins over the properties whichever order the two are applied in.

## Legacy property keys

Earlier versions configured Quartz with flat `quartz.*` string keys. They still work, and mean exactly
the same as the options above — they are translated into them. Both spellings of a setting always
produce the same result.

Two differences are worth knowing:

- Durations in the flat format are integer **milliseconds** (`quartz.scheduler.idleWaitTime = 30000`).
  As typed options they are `TimeSpan` (`"00:00:30"`).
- A `.type` key names an implementation. In code you select implementations with the matching `Use*`
  method instead, which is checked at compile time.

| Flat key | Option |
|---|---|
| `quartz.scheduler.instanceName` | `Scheduler:InstanceName` |
| `quartz.scheduler.instanceId` | `Scheduler:InstanceId` (`AUTO` and `SYS_PROP` set `GenerateInstanceId`) |
| `quartz.scheduler.idleWaitTime` | `Scheduler:IdleWaitTime` |
| `quartz.scheduler.batchTriggerAcquisitionMaxCount` | `Scheduler:MaxBatchSize` |
| `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow` | `Scheduler:BatchTriggerAcquisitionFireAheadTimeWindow` |
| `quartz.scheduler.interruptJobsOnShutdown` | `Scheduler:InterruptJobsOnShutdown` |
| `quartz.scheduler.interruptJobsOnShutdownWithWait` | `Scheduler:InterruptJobsOnShutdownWithWait` |
| `quartz.context.key.NAME` | `Scheduler:Context:NAME` |
| `quartz.threadPool.maxConcurrency` (or `threadCount`) | `ThreadPool:MaxConcurrency` |
| `quartz.threadPool.type` | `UseThreadPool<T>()` |
| `quartz.jobStore.type` | `UseInMemoryStore()` / `UsePersistentStore<T>()` |
| `quartz.jobStore.misfireThreshold` | `JobStore:MisfireThreshold` |
| `quartz.jobStore.tablePrefix` | `JobStore:TablePrefix` |
| `quartz.jobStore.useProperties` | `JobStore:UseProperties` |
| `quartz.jobStore.makeThreadsDaemons` | `JobStore:UseBackgroundThreads` |
| `quartz.jobStore.clustered` | `JobStore:Clustering:Enabled`, or `UseClustering()` |
| `quartz.jobStore.acceptEnlistedTransactions` | `JobStore:AcceptEnlistedTransactions` |
| `quartz.jobStore.clusterCheckinInterval` | `JobStore:Clustering:CheckinInterval` |
| `quartz.jobStore.clusterCheckinMisfireThreshold` | `JobStore:Clustering:CheckinMisfireThreshold` |
| `quartz.jobStore.dataSource` | set for you by the database methods |
| `quartz.dataSource.NAME.provider` | `DataSource:NAME:Provider` |
| `quartz.dataSource.NAME.connectionString` | `DataSource:NAME:ConnectionString` |
| `quartz.dataSource.NAME.connectionStringName` | `DataSource:NAME:ConnectionStringName` |
| `quartz.dbprovider.NAME.*` | the metadata factory on `UseGenericDatabase`; the keys still work |
| `quartz.serializer.type` | `UseSystemTextJsonSerializer()` / `UseNewtonsoftJsonSerializer()` |
| `quartz.plugin.NAME.type` | `AddPlugin<T>()` or the plugin's own `Use*` method |
| `quartz.jobStore.lockHandler.type` | `UseLockHandler<T>()` |
| `quartz.scheduler.jobFactory.type` | `UseJobFactory<T>()` |
| `quartz.scheduler.typeLoadHelper.type` | `UseTypeLoader<T>()` |
| `quartz.jobListener.NAME.type` | `AddJobListener<T>(matchers)` |
| `quartz.triggerListener.NAME.type` | `AddTriggerListener<T>(matchers)` |

A listener named by properties has no matchers to carry, so it listens to everything. The code-first
methods take matchers, which is the reason to prefer them.

Two `quartz.scheduler.*` keys are rejected rather than ignored, because they no longer configure
anything: `quartz.scheduler.threadName` and `quartz.scheduler.makeSchedulerThreadDaemon`. The
scheduling loop is a `Task` rather than a `Thread`, so it has no name to set and never held a
process open. Remove them; for the job store's misfire and cluster threads, which are real threads,
use `quartz.jobStore.makeThreadsDaemons` / `JobStore:UseBackgroundThreads`.

Every key has both spellings. `quartz.jobStore.tablePrefix` and `JobStore:TablePrefix` are the same
setting said two ways, and so are the ones that select an implementation rather than set a value —
`JobStore:Type`, `JobStore:DriverDelegateType`, `JobStore:LockHandler:Type`, `ThreadPool:Type` and the
rest. A configuration file never has to mix the two forms, and a component with no options type of its
own is still configurable, because its settings are read as flat keys whichever way they were written.

Durations may be written either way too: `00:00:30` or a bare `30000`, which is read as milliseconds
for the sake of configuration files carried forward from 3.x.

Where the same setting is said twice, code wins. A `UsePersistentStore` in code beats a leftover
`quartz.jobStore.type` in a configuration file, and a value set through `ConfigureScheduler` beats the
same value in `appsettings.json`. Built-in fallbacks — the driver delegate and the serializer — are
registered after everything explicit, so they only apply when nothing else claimed the slot.

Removed in 4.x, with no replacement: `quartz.scheduler.proxy*` and `quartz.scheduler.exporter*`
(remoting, which .NET no longer supports) — these two are rejected with an exception naming the
replacement, rather than accepted and ignored — plus `quartz.threadExecutor*`, which had no
implementation left to choose between.
