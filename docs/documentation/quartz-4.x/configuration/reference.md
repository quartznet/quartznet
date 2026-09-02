---

title: Configuration Reference
---

# Quartz.NET Configuration Reference

[[toc]]

Quartz is configured with strongly typed options. Every option has the same name whether you set it in
code or in a configuration file, so there is one vocabulary to learn rather than two:

<!-- snippet: sample_reference_one_option -->
```csharp
services.AddQuartz(q => q.ConfigureScheduler(options => options.MaxBatchSize = 5));
```
<!-- endSnippet -->

```json
{
  "Quartz": {
    "Scheduler": { "MaxBatchSize": 5 }
  }
}
```

Options are bound from the `Quartz` section by section name and validated at startup, so a bad value is
reported against the setting that is wrong rather than failing later during scheduling. The binding is
source-generated — the compiler writes a binder for each options type rather than reflecting over it at
startup — which is what makes configuring from a file as safe under `PublishTrimmed` and `PublishAot`
as configuring in code. Nothing is asked of your application for that; it is how `Quartz` is built. The
sections are:

| Section | Options | |
|---|---|---|
| `Scheduler` | `QuartzSchedulerOptions` | [below](#scheduler) |
| `ThreadPool` | `ThreadPoolOptions` | [below](#thread-pool) |
| `JobStore` | `InMemoryJobStoreOptions` or `AdoJobStoreOptions` | [below](#in-memory-job-store) |
| `JobStore:Clustering` | `ClusteringOptions` | [below](#clustering) |
| `DataSource` | `DataSourceOptions`, one per named data source | [below](#data-source) |
| `Scheduling` | `SchedulingOptions` — what happens when registered jobs and triggers already exist in the store | [below](#scheduling) |
| `TypeLoader` | `TypeLoaderOptions` — the container's, not a scheduler's | [below](#type-loader) |
| `Schedulers` | one sub-section per named scheduler | [below](#several-schedulers) |
| `Schedule`, `ProcessingDirectives` | jobs and triggers declared in configuration | [JSON configuration](json.md) |

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
| `MaxBatchSize` | int | `1` | How many triggers may be acquired at once. Only an upper bound — `BatchTriggerAcquisitionFireAheadTimeWindow` decides how many are actually taken — and it may not exceed `ThreadPool:MaxConcurrency`. See [Batching trigger acquisition](../tutorial/advanced-enterprise-features.md#batching-trigger-acquisition). |
| `BatchTriggerAcquisitionFireAheadTimeWindow` | TimeSpan | `00:00:00` | How far ahead of its fire time a trigger may be included in the current batch. The other half of `MaxBatchSize`: at the default of zero, neither batches anything. |
| `ShutdownJobInterruption` | `ShutdownJobInterruption` | `Never` | When a shutting-down scheduler signals cancellation to the jobs still executing. |
| `PropagateTraceContext` | bool | `true` | Leaves the ambient trace context on a trigger scheduled inside an `Activity`, under two reserved job-data keys, so the firing's span links back to the call that scheduled it. The two entries are visible wherever trigger data is — `MergedJobDataMap`, the dashboard, `GET /triggers`, `QRTZ_TRIGGERS.JOB_DATA` — so turn it off to keep them out of the store. See [OpenTelemetry integration](../packages/opentelemetry-integration.md). |
| `Context` | dictionary | empty | Values seeded into `SchedulerContext`. Get-only: add to it (`options.Context["environment"] = "staging"`) rather than assigning a new dictionary. |

`ShutdownJobInterruption` has four values, because a shutdown either waits for running jobs or it
does not and interrupting them is a reasonable thing to want in either case, or in only one of them:

| Value | Meaning |
|---|---|
| `Never` | Running jobs are never interrupted. |
| `WhenNotWaitingForJobs` | Interrupted only on a shutdown that does not wait for them. |
| `WhenWaitingForJobs` | Interrupted only on a shutdown that waits — the wait still happens, so a job that checks its cancellation token gets to unwind cleanly. |
| `Always` | Interrupted on every shutdown. |

<!-- snippet: sample_reference_scheduler_options -->
```csharp
services.AddQuartz(q => q.ConfigureScheduler(options =>
{
    options.InstanceName = "core";
    options.InstanceId = "node-1";
    options.MaxBatchSize = 5;
    options.ShutdownJobInterruption = ShutdownJobInterruption.Always;
}));
```
<!-- endSnippet -->

## Thread pool

`ThreadPoolOptions`, bound from `Quartz:ThreadPool`.

| Option | Type | Default | Description |
|---|---|---|---|
| `MaxConcurrency` | int | `10` | How many jobs may run at once. |

<!-- snippet: sample_reference_default_thread_pool -->
```csharp
services.AddQuartz(q => q.UseDefaultThreadPool(maxConcurrency: 20));
```
<!-- endSnippet -->

To supply your own implementation:

<!-- snippet: sample_reference_thread_pool_of_your_own -->
```csharp
services.AddQuartz(q => q.UseThreadPool<MyThreadPool>());
```
<!-- endSnippet -->

Two shipped pools are worth knowing by name, because `UseThreadPool<T>()` is how you reach either:

| Type | For |
|---|---|
| `Quartz.Impl.ZeroSizeThreadPool` | a scheduler that exists only to *write* the schedule and is never started. `UseThreadPool<ZeroSizeThreadPool>()` creates no worker threads at all, and both of the members a running scheduler would call throw `NotSupportedException` — so calling `Start()` on such a scheduler fails loudly rather than sitting there firing nothing |
| `Quartz.Impl.TaskSchedulingThreadPool` | the open base of `DefaultThreadPool`. A pool of your own derives from it and overrides one member, `GetDefaultScheduler()`, rather than implementing `IThreadPool`'s six from scratch |

`ThreadPoolOptions` belongs to the built-in pools — they are what read `MaxConcurrency` — so
`UseThreadPool<T>()` takes no callback for it. A pool of your own has options of its own:

<!-- snippet: sample_reference_thread_pool_options -->
```csharp
services.AddQuartz(q =>
{
    q.ConfigureOptions<MyThreadPoolOptions>(options => options.Slots = 20);
    q.UseThreadPool<MyThreadPool>();
});
```
<!-- endSnippet -->

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

<!-- snippet: sample_reference_in_memory_store -->
```csharp
services.AddQuartz(q => q.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(30)));
```
<!-- endSnippet -->

## Persistent job store

`AdoJobStoreOptions`, bound from `Quartz:JobStore`. Choosing a database also selects the driver delegate
that speaks its SQL dialect, so a connection string is all you normally supply:

<!-- snippet: sample_reference_persistent_store -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
}));
```
<!-- endSnippet -->

| Option | Type | Default | Description |
|---|---|---|---|
| `TablePrefix` | string | `QRTZ_` | Prefix on every Quartz table name. |
| `StoreJobDataAsStrings` | bool | `false` | Persists job data as name/value strings rather than serialized objects, which keeps stored data readable and version tolerant. |
| `MisfireThreshold` | TimeSpan | `00:01:00` | How late a trigger may fire before it counts as misfired. |
| `MisfireHandlerFrequency` | TimeSpan? | `MisfireThreshold` | How often misfires are handled. |
| `MaxMisfiresToHandleAtATime` | int | `20` | How many misfired triggers are handled per pass. |
| `CommandTimeout` | TimeSpan? | provider default | How long a statement may run before the provider cancels it, applied to every statement the store issues including the lock handler's. Unset leaves each provider's own default, usually 30 seconds. ADO.NET counts whole seconds, so the value is rounded **up** — `00:00:01.500` is applied as 2 seconds, because rounding down would turn a sub-second value into `0`, which means "no timeout". |
| `DbRetryInterval` | TimeSpan | `00:00:15` | How long to wait before retrying after a database failure. |
| `MaxTransientRetries` | int | `3` | How many times a transient failure such as a deadlock is retried. Transient means the driver's own `DbException.IsTransient`, a SQLSTATE in class `40` — the standard's "transaction rollback", covering a serialization failure or a deadlock whichever provider reports it, with `40002` excepted because a deferred constraint violation fails identically on every retry — SQL Server's transient error numbers, SQLite's busy and locked codes, or a timeout. |
| `TransientRetryInterval` | TimeSpan | `00:00:01` | Delay between transient retries. |
| `RetryableActionErrorLogThreshold` | int | `4` | How many consecutive failures before they are logged as errors. |
| `IsTransient` | `Func<Exception, bool>?` | `null` | An extra answer to the question above, for a driver that reports a retryable condition none of those signals recognise. Consulted first and only additive — returning `false` falls through to the built-in list, so it cannot make Quartz stop retrying something it already retries. The exception handed over is the store's own, so reach the driver's with `GetBaseException()`. Code only; there is no `quartz.*` key for a delegate. |
| `UseDbLocks` | bool | `false` | Uses database row locks. Required for clustering, and implied by `UseClustering()`. |
| `LockOnInsert` | bool | `true` | Takes a lock when inserting rows. |
| `AcquireTriggersWithinLock` | bool | `false` | Acquires triggers inside the database lock. |
| `TransactionIsolationLevel` | IsolationLevel? | none | The isolation level the store begins its own transactions at. Unset means `ReadCommitted` — Quartz's default rather than the provider's, which vary. Forced to `Serializable` on SQLite, and ignored for a connection the application enlisted, which was begun at whatever level the application chose. |
| `AcceptEnlistedTransactions` | bool | `false` | Lets the job store use a connection the application enlisted with `SchedulerEnlistmentExtensions.EnlistTransaction`, so scheduling commits with the application's own work. See [Joining an existing transaction](../tutorial/job-stores.md#joining-an-existing-transaction). |
| `DoubleCheckLockMisfireHandler` | bool | `true` | Re-checks the lock before handling misfires. |
| `UseBackgroundThreads` | bool | `false` | Runs the misfire handler and cluster manager on background threads, which do not keep the process alive. These two are the only real threads Quartz creates. |
| `SchemaProvisioning` | `SchemaProvisioning` | `Validate` | What the store does about its schema at startup: `None` does nothing, `Validate` verifies the expected tables exist, `CreateIfMissing` creates whatever is missing and then verifies. |
| `SelectWithLockSql` | string? | none | Overrides the row-lock statement, defaulted to SQL Server's `WITH (UPDLOCK,ROWLOCK)` form when that is the database. Read only when the store builds a database-locking handler for itself — see [Locking](#locking). |
| `OpenConnection` | bool | `false` | Whether the ambient-transaction store — `ExternalTransactionJobStore`, the one `UsePersistentStore(store => store.UseAmbientTransactions())` selects — opens the connections it creates. Read only by that store, and written by `quartz.jobStore.openConnection` as well as by the section entry. |

A custom trigger persistence delegate is registered with
`UsePersistentStore(s => s.UseTriggerPersistenceDelegate<T>())` rather than through an option; the
legacy `quartz.jobStore.driverDelegateInitString` key still translates to the same registrations.

### Databases

| Method | Database | Driver package your project references |
|---|---|---|
| `UseSqlServer` | Microsoft SQL Server | `Microsoft.Data.SqlClient` |
| `UsePostgres` | PostgreSQL | `Npgsql` |
| `UseMySql` | MySQL, using the MySql.Data driver | `MySql.Data` |
| `UseMySqlConnector` | MySQL, using the MySqlConnector driver | `MySqlConnector` |
| `UseOracle` | Oracle | `Oracle.ManagedDataAccess.Core` |
| `UseFirebird` | Firebird | `FirebirdSql.Data.FirebirdClient` |
| `UseSqlite` | SQLite, using the Microsoft.Data.Sqlite driver | `Microsoft.Data.Sqlite` |
| `UseSystemDataSqlite` | SQLite, using the legacy System.Data.SQLite driver | `System.Data.SQLite.Core` |
| `UseGenericDatabase` | Anything else, using the generic SQL dialect — and the only one that can [describe its own driver](#describing-a-driver-quartz-does-not-know) | the one your driver ships in |

Quartz references none of them, so the package is `dotnet add package`'d by the application; a method
called without its driver present compiles and fails at startup with
`Could not load file or assembly`. [Job Stores](../tutorial/job-stores.md#configuring-a-persistent-store)
has the whole story.

Each takes either a connection string or a callback over `DataSourceOptions`:

<!-- snippet: sample_reference_connection_string -->
```csharp
store.UseSqlServer(connectionString);
store.UseSqlServer(db => db.ConnectionStringName = "Scheduler");
```
<!-- endSnippet -->

Where the connection comes from is the data source's own setting, so to connect through a
`DbDataSource` registered in the container rather than a connection string of Quartz's own, say
`store.UseSqlServer(db => db.UseRegisteredDataSource = true)`.

#### Naming a driver, or handing over its factory

Both spellings above name the driver: `SqlServer` names a description that says which connection,
command and parameter types to instantiate, and Quartz resolves those types from strings, because it
references no driver package. That is how it has always worked and it is what most applications want.

A **trimmed or native AOT** application cannot rely on it. The trimmer does not follow a type name, so
it removes what the name pointed at, and the registration fails while the container is being built with
`Cannot instantiate type which has no empty constructor`. Every method above therefore also takes the
`DbProviderFactory` the driver ships:

<!-- Not a compiled sample: the driver factories below come from packages this repository's samples
     project does not reference, and naming the real ones is the point. -->

```csharp
store.UseSqlServer(SqlClientFactory.Instance, connectionString);
store.UsePostgres(NpgsqlFactory.Instance, connectionString);
store.UseMySqlConnector(MySqlConnectorFactory.Instance, connectionString);
store.UseSqlite(SqliteFactory.Instance, connectionString);
store.UseSystemDataSqlite(SQLiteFactory.Instance, connectionString);
store.UseFirebird(FirebirdClientFactory.Instance, connectionString);
store.UseMySql(MySqlClientFactory.Instance, connectionString);
```

A factory hands back an instance of every type the store uses — a connection, and the connection makes
the command, and the command makes its parameters — so nothing is named and nothing is constructed by
reflection. The provider name is still chosen for you, because it decides how the driver spells a
parameter, but only the half of its description that names no type is read.

Which to use:

| Registration | When |
|---|---|
| `Use<Db>(connectionString)` | The ordinary case. Carries `[RequiresUnreferencedCode]`, so a trimmed publish reports it. |
| `Use<Db>(factory, connectionString)` | Publishing `PublishTrimmed` or `PublishAot`, or anywhere you would rather not have a type resolved from a string. |
| `db.UseRegisteredDataSource = true` | A `DbDataSource` in the container already carries the connection details — pooling, type mappers, logging. Equally free of type names. |

Oracle is the one driver that needs more than a factory, because Quartz reaches two things on its own
types by reflection and a factory names neither. Both are said in code, by the application, which does
reference the driver:

<!-- Not a compiled sample, for the same reason as the one above. -->

```csharp
store.UseOracle(
    OracleClientFactory.Instance,
    connectionString,
    configureCommand: command => ((OracleCommand) command).BindByName = true,
    configureBinaryParameter: parameter => ((OracleParameter) parameter).OracleDbType = OracleDbType.Blob);
```

Without the first, ODP.NET binds every statement's parameters by position and the store reads the wrong
columns. Without the second, a job data map larger than two kilobytes will not go in, because
`DbType.Binary` means `OracleDbType.Raw` to that driver and not `Blob`. Naming the driver instead of
handing over its factory says both of these for you.

The factory overloads take the connection string directly, so `ConnectionStringName` does not apply to
them; read the connection string from `IConfiguration` where you have it and pass it in.

#### Describing a driver Quartz does not know

The provider name each method passes — `SqlServer`, `Npgsql` and so on — names a description of an
ADO.NET driver: which connection, command and parameter types to instantiate, how parameters are named,
and which enum value means "binary column". Quartz ships descriptions for the drivers of every database
listed above. For anything else, describe the driver in the `UseGenericDatabase` call:

<!-- snippet: sample_reference_generic_database -->
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
<!-- endSnippet -->

There is a four-argument overload taking a `DataSourceOptions` callback instead of a connection string,
for a driver described in code that also uses a named connection string.

A driver reached through its own factory is described the same way, and needs no provider name at all —
there is nothing left to look a description up by:

<!-- Not a compiled sample: `MyFactory` stands in for a driver's own `DbProviderFactory`. -->

```csharp
store.UseGenericDatabase(MyFactory.Instance, connectionString, new DbMetadata
{
    ProductName = "My Database",
    ParameterNamePrefix = "@",
    UseParameterNamePrefixInParameterCollection = true,
    BindByName = true,
    ConfigureBinaryParameter = parameter => ((MyParameter) parameter).MyDbType = MyDbType.Blob,
});
```

`ConfigureCommand` and `ConfigureBinaryParameter` are the two typed seams on `DbMetadata`: they say what
the name path would otherwise reach by reflecting over `CommandType` and `ParameterType`, and they are
how a description that names no type stays complete. Either may be left unset — a binary parameter with
neither a seam nor a described parameter type is bound as `DbType.Binary`, which every driver that ships
a factory maps for itself.

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

A store's data source is named after the scheduler that owns it, or `quartz` for the default scheduler,
so the name never has to be invented or kept in step by hand. Name one explicitly with
`store.UseDataSource("reporting-db")` — before choosing the database, since the name is fixed once
the data source is configured — when two stores should read the same `Quartz:DataSource:<name>`
settings, or when the settings live under a name of the application's choosing.

### Locking

Leave the lock handler unset and the store chooses one for itself once it knows which database it is
talking to: database row locks when clustered or when `UseDbLocks` is on, and an in-process monitor
otherwise. `UseLockHandler<T>()` overrides that choice, and `UseLockHandler(factory)` does the same for a
handler that needs building — as `UseRedisLockHandler()` does.

`SelectWithLockSql` belongs to the handler the store builds for itself. A handler chosen with
`UseLockHandler` takes its statement through its own constructor instead, so setting both leaves the
option doing nothing — the store logs a warning at startup when it finds that combination.

Both this and `UseSerializer` register against the scheduler that owns the store. Registering
`ILockHandler` or `IObjectSerializer` directly against `Services` registers it for the container, which a
named scheduler will not see.

Whichever handler is in use, the store tells it which scheduler it locks for and the environment it
locks in — its clock and the `CommandTimeout` above — through `ILockHandler.Initialize(LockHandlerContext)`,
before the first lock is taken. A handler of your own does not need configuring for any of it. The
timeout is worth setting for a clustered store: a node waiting on `QRTZ_LOCKS` behind a peer that
stopped without releasing the row cannot schedule anything until the lock statement gives up.

### Data source

`DataSourceOptions`, bound from `Quartz:DataSource`.

| Option | Type | Description |
|---|---|---|
| `Provider` | string | Names the description of the ADO.NET driver to use. Set for you by the database methods above; the names Quartz ships a description for are constants on `DataSourceOptions.Providers`, and see [Describing a driver Quartz does not know](#describing-a-driver-quartz-does-not-know) for anything else. |
| `ConnectionString` | string? | The connection string. Takes precedence over `ConnectionStringName`. |
| `ConnectionStringName` | string? | A connection string to resolve from `IConfiguration`. |
| `UseRegisteredDataSource` | bool | Connections come from the container's unkeyed `DbDataSource`. Wins over both connection string settings. |
| `DataSourceServiceKey` | object? | The service key the `DbDataSource` is registered under, for a container that holds more than one. Implies `UseRegisteredDataSource`. Code only — a binder cannot produce a service key. |
| `DataSourceFactory` | Func&lt;IServiceProvider, DbDataSource&gt;? | Supplies the `DbDataSource` directly. Wins over both of the above. Code only. |

The provider names Quartz ships a description for, as constants rather than as strings to copy out of
this table:

| Constant | Value | Driver |
|---|---|---|
| `DataSourceOptions.Providers.SqlServer` | `SqlServer` | `Microsoft.Data.SqlClient` |
| `DataSourceOptions.Providers.Npgsql` | `Npgsql` | `Npgsql` |
| `DataSourceOptions.Providers.MySql` | `MySql` | `MySql.Data` |
| `DataSourceOptions.Providers.MySqlConnector` | `MySqlConnector` | `MySqlConnector` |
| `DataSourceOptions.Providers.Oracle` | `OracleODPManaged` | managed ODP.NET |
| `DataSourceOptions.Providers.Sqlite` | `SQLite-Microsoft` | `Microsoft.Data.Sqlite` |
| `DataSourceOptions.Providers.SystemDataSqlite` | `SQLite` | `System.Data.SQLite` |
| `DataSourceOptions.Providers.Firebird` | `Firebird` | `FirebirdSql.Data.FirebirdClient` |

`Provider` stays a string rather than becoming an enum because the set is not closed: a driver Quartz
knows nothing about is describable, which is what `UseGenericDatabase` is for.

To connect through a `DbDataSource` registered in the container, for example by `AddNpgsqlDataSource`:

<!-- Not a compiled sample: `AddNpgsqlDataSource` comes from `Npgsql.DependencyInjection`, which this
     repository does not reference, and naming a real provider is the point. -->

```csharp
services.AddNpgsqlDataSource(connectionString);
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UsePostgres(db => db.UseRegisteredDataSource = true);
}));
```

`UseRegisteredDataSource` asks for the container's one unkeyed `DbDataSource`, which is exactly right
for an application with one database. A container that holds several — a scheduler per tenant, or a
reporting scheduler beside the application's own — keys them apart, and `DataSourceServiceKey` says
which key is this store's:

<!-- Not a compiled sample, for the same reason as the one above: `AddNpgsqlDataSource` is Npgsql's. -->

```csharp
services.AddNpgsqlDataSource(tenantA, serviceKey: "tenant-a");
services.AddNpgsqlDataSource(tenantB, serviceKey: "tenant-b");

services.AddQuartz("tenant-a", q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "tenant-a")));
services.AddQuartz("tenant-b", q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "tenant-b")));
```

A data source that is built rather than registered goes in `DataSourceFactory`, which wins over both:

<!-- snippet: sample_reference_data_source_factory -->
```csharp
store.UsePostgres(db => db.DataSourceFactory = _ => BuildDataSource());
```
<!-- endSnippet -->

Both are set from code rather than from configuration, because a service key can be any object and a
factory is a delegate — neither is something a configuration binder can produce. Either one means Quartz
needs no connection string of its own, so neither is asked for.

Commands on this path are made by the connection rather than from the driver description, so whatever
the data source configured on its connections — an `NpgsqlDataSource`'s type mappers, its logging, its
composite type registrations — is in play for Quartz's statements too.

There are three entry points for a data source and they say different things.
`UseDataSource(configure)` **defines** one — which driver, and how to reach the database — and the
database methods above are shorthands for it. `UseDataSource(name)` **refers to** one by name, which is
how a store picks up settings registered elsewhere, such as a `Quartz:DataSource:<name>` section — one
concept, so one name, told apart by whether it is handed a name or a callback. Where the connection
itself comes from is `DataSourceOptions`' to say, not a fourth method's.

#### Bringing your own connection provider

When connections cannot be described at all — a pooled or credential-rotating factory, or a driver
whose connections need setting up after they are created — hand Quartz the object that makes them:

<!-- snippet: sample_reference_connection_provider -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);          // still selects the driver delegate
    store.UseConnectionProvider<MyDbProvider>();   // …but connections come from here
}));
```
<!-- endSnippet -->

`UseConnectionProvider(factory)` does the same for a provider that needs building first. This is the
one method on the builder that **replaces** rather than defers: it wins over the provider the database
method registered, in either order, so there is no call sequence to get right. It also names this
store's data source, so `UseConnectionProvider` on its own is a complete configuration — the database
method above it is only there to select the driver delegate.

The provider belongs to the scheduler that owns the store. Registering `IDbProvider` against `Services`
instead registers it for the container, which a named scheduler will not see.

The same thing as properties, which is the 3.x spelling and still read:

```json
{
  "Quartz": {
    "quartz.jobStore.dataSource": "myDs",
    "quartz.dataSource.myDs.connectionProvider.type": "MyNamespace.MyDbProvider, MyAssembly"
  }
}
```

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

<!-- snippet: sample_reference_clustering -->
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
    });
});
```
<!-- endSnippet -->

`UseClustering()` enables database locking as well, because clustering has never worked without it.

## Serialization

Whatever a persistent store cannot write as a string goes through an `IObjectSerializer`.
**A store that names none gets `SystemTextJsonObjectSerializer`**, registered as the fallback the way
the driver delegate is — so `UseSystemTextJsonSerializer()` with no argument selects what the store
already had, and only two things are worth writing:

<!-- snippet: sample_reference_serializers -->
```csharp
// Newtonsoft.Json, from the Quartz.Serialization.Newtonsoft package: what reads data
// a 3.x scheduler's Newtonsoft serializer wrote
store.UseNewtonsoftJsonSerializer();

// System.Text.Json with something to say about it — a trigger or calendar type of
// your own that the built-in serializers do not know
store.UseSystemTextJsonSerializer(json =>
    json.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer()));
```
<!-- endSnippet -->

## Scheduling

`SchedulingOptions`, bound from `Quartz:Scheduling`. These decide what happens when the jobs and triggers
registered in code, or declared in a file, already exist in the store under the same names.

| Option | Type | Default | Description |
|---|---|---|---|
| `OverwriteExistingData` | bool | `true` | A registration replaces the stored job or trigger of the same name. The default is a default rather than a statement: setting `IgnoreDuplicates` turns it off. |
| `IgnoreDuplicates` | bool | `false` | A name that already exists is skipped instead of throwing. Enough on its own — it turns `OverwriteExistingData` off. Setting both explicitly is refused at startup. |
| `ScheduleTriggerRelativeToReplacedTrigger` | bool | `false` | A replaced trigger's next fire time is computed from the old trigger's last fire time rather than from now. |

All three are about a *file or registration versus the store*. None of them can say anything about one
scheduling data file that declares the same job or trigger key twice, so none of them suppresses the
error that file gets — see [ProcessingDirectives](json.md#processingdirectives).

<!-- snippet: sample_reference_scheduling_options -->
```csharp
services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);
```
<!-- endSnippet -->

## Job factory

By default jobs are resolved from the container, in a scope created per firing, so a job may take scoped
dependencies. To replace it:

<!-- snippet: sample_reference_job_factory -->
```csharp
services.AddQuartz(q => q.UseJobFactory<MyJobFactory>());
```
<!-- endSnippet -->

To keep it and only add to the scope it opens:

<!-- snippet: sample_reference_job_scope -->
```csharp
services.AddQuartz(q => q.ConfigureJobScope((scope, bundle, scheduler) => { /* … */ }));
```
<!-- endSnippet -->

## Type loader

`TypeLoaderOptions`, bound from `Quartz:TypeLoader`. This is the one options type that is the
**container's** rather than a scheduler's, because the loader it configures is: one `ITypeLoader` serves
every scheduler in the container.

| Option | Type | Default | Description |
|---|---|---|---|
| `Aliases` | `Dictionary<string, string>` | empty | What a type name that no longer names anything means today: the name as it was stored or configured, mapped to the name of the type that replaced it. |

<!-- snippet: sample_reference_type_loader_aliases -->
```csharp
services.AddQuartz(q => q.UseTypeLoader(loader =>
    loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob))));
```
<!-- endSnippet -->

```json
{
  "Quartz": {
    "TypeLoader": {
      "Aliases": {
        "Acme.Jobs.NightlyReport, Acme.Jobs": "Acme.Jobs.NightlyRollupJob, Acme.Jobs"
      }
    }
  }
}
```

An alias applies wherever Quartz turns a string into a type at run time — a stored `JOB_CLASS_NAME`, a
job named in XML or JSON scheduling data, a `quartz.plugin.<name>.type` key. The flat keys naming a
scheduler's own components are read while
the service collection is still being built, before any options exist, and are not aliased. An alias
whose target names no type this application can load fails options validation at startup, and nothing is
ever written back, so retiring one is still the SQL `UPDATE`; see [Job deserialization failures after
refactoring](../../troubleshooting.md#job-deserialization-failures-after-refactoring) for the whole
story.

## The other seams

Each of these replaces one collaborator of the scheduler. All are `IQuartzBuilder` members, so they work
identically under `AddQuartz` and inside `QuartzSchedulerBuilder.Create(q => …)`.

| Method | Replaces | Default |
|---|---|---|
| `UseTimeProvider(timeProvider)` | the clock every trigger, store and misfire calculation reads | `TimeProvider.System`, or the container's registration when there is one |
| `UseTypeLoader<T>()` | how a type named by a string — a stored `JOB_CLASS_NAME`, a `.type` key — is resolved | resolution through the container's assemblies, with the 3.x namespace fallbacks |
| `UseTypeLoader(configure)` | *configures* that loader rather than replacing it: `loader.Map(oldName, typeof(NewType))` declares what a renamed type is called now, and `Quartz:TypeLoader:Aliases` is the same map from configuration. See [Job deserialization failures after refactoring](../../troubleshooting.md#job-deserialization-failures-after-refactoring) | no aliases |
| `UseSimpleTypeLoader()` | asks for the built-in loader by name. `SimpleTypeLoader` is internal — a type-loading strategy is not something to derive from — so there is no `UseTypeLoader<SimpleTypeLoader>()` to write; this is it. It is already the default, so it matters only where something else registered a loader first | this is the default |
| `UseInstanceIdGenerator<T>()` | how `InstanceId` is derived when `GenerateInstanceId` is on | `SimpleInstanceIdGenerator`: host name plus a timestamp |
| `UseJobStore<T>()`, `UseJobStore<T, TOptions>()` | the job store, for one that is neither of the two Quartz ships | the in-memory store |
| `UseDriverDelegate<T>()`, `UseDriverDelegate(factory)` (on the persistent store builder) | the SQL dialect the ADO.NET store speaks | selected by the database method — `UseSqlServer` picks `SqlServerDelegate`, and so on |

`UseTimeProvider` is the one to reach for in a test: a `FakeTimeProvider` makes `TriggerBuilder`,
`GetFireTimeAfter` and misfire calculations see the time you set. It does not drive the scheduler's own
waiting, which is on the real clock.

## Listeners, calendars and plugins

<!-- snippet: sample_reference_listeners_and_plugins -->
```csharp
services.AddQuartz(q =>
{
    q.AddSchedulerListener<MySchedulerListener>();
    q.AddJobListener<MyJobListener>(GroupMatcher<JobKey>.GroupEquals("reports"));
    q.AddTriggerListener<MyTriggerListener>();
    q.AddPlugin<MyPlugin>();
});
```
<!-- endSnippet -->

Listeners and plugins are ordinary services, so they take their dependencies through their constructors.

## Several schedulers

Registering a scheduler under a name gives it its own job store, thread pool, jobs and configuration.
The name is the scheduler's instance name, the key its services are registered under, and the name of
its options.

<!-- snippet: sample_reference_named_schedulers -->
```csharp
services.AddQuartz("reporting", q => q.UsePersistentStore(store => store.UseSqlServer(reportingDb)));
services.AddQuartz("ingest", q => q.UseInMemoryStore());
```
<!-- endSnippet -->

Resolve them by name:

<!-- snippet: sample_reference_resolving_a_named_scheduler -->
```csharp
var reporting = await serviceProvider
    .GetRequiredKeyedService<ISchedulerFactory>("reporting")
    .GetScheduler();
```
<!-- endSnippet -->

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
does not take *a second* configuration API: `Create` hands the callback an `IQuartzBuilder`, the very
one `AddQuartz(q => …)` hands out, over a container it creates itself.

<!-- snippet: sample_reference_without_a_container -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q
        .ConfigureScheduler(options => options.InstanceName = "reporting")
        .UseDefaultThreadPool(maxConcurrency: 20)
        .UseInMemoryStore())
    .BuildScheduler();
```
<!-- endSnippet -->

What it adds is the two terminal methods a standalone caller needs, `Build()` for the factory and
`BuildScheduler()` for the scheduler. Everything else — `AddJob`, `AddTrigger`, `ScheduleJob`,
`AddCalendar`, every extension a package contributes — is written inside the callback, where it is
the same call it would be under a host.

A scheduler configured entirely by flat `quartz.*` keys is built the same way:

<!-- snippet: sample_reference_from_flat_properties -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .BuildScheduler();
```
<!-- endSnippet -->

`UseProperties` checks the keys against the ones Quartz reads, so a misspelling is reported rather
than silently ignored; set `quartz.checkConfiguration` to `false` to allow keys of your own.
Configuration written in code wins over the properties whichever order the two are applied in.

**The check applies to a property bag you wrote, not to `appsettings.json`.** It runs for
`UseProperties` and the `AddQuartz(services, properties, …)` overloads — the shape a 3.x application
migrates in, and the one the removed-key advice is written for. Keys that came out of an
`IConfiguration` section are deliberately not checked, because there every key under `Quartz:` becomes
a `quartz.*` key whether Quartz reads it or not, so a section holding your own settings would be
rejected. A misspelled key in `appsettings.json` is therefore read by nobody and reported by nothing —
`Quartz:JobStore:TabelPrefix` configures no table prefix and says so nowhere — so check a key you have
just typed against the tables above. Casing is not the risk: configuration keys are matched
case-insensitively, so `Quartz:Jobstore:TablePrefix` is the same key as `Quartz:JobStore:TablePrefix`.

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
| `quartz.scheduler.interruptJobsOnShutdown` | `Scheduler:ShutdownJobInterruption` — `true` alone means `WhenNotWaitingForJobs` |
| `quartz.scheduler.interruptJobsOnShutdownWithWait` | `Scheduler:ShutdownJobInterruption` — `true` alone means `WhenWaitingForJobs`; both keys `true` means `Always` |
| `quartz.context.key.NAME` | `Scheduler:Context:NAME` |
| `quartz.threadPool.maxConcurrency` (or `threadCount`) | `ThreadPool:MaxConcurrency` |
| `quartz.threadPool.type` | `UseThreadPool<T>()` |
| `quartz.jobStore.type` | `UseInMemoryStore()` / `UsePersistentStore()`, with `UseAmbientTransactions()` inside it for the store 3.x called `JobStoreCMT`; `UsePersistentStore<T>()` takes a persistent store of your own |
| `quartz.jobStore.misfireThreshold` | `JobStore:MisfireThreshold` |
| `quartz.jobStore.tablePrefix` | `JobStore:TablePrefix` |
| `quartz.jobStore.useProperties` | `JobStore:StoreJobDataAsStrings` |
| `quartz.jobStore.makeThreadsDaemons` | `JobStore:UseBackgroundThreads` |
| `quartz.jobStore.clustered` | `JobStore:Clustering:Enabled`, or `UseClustering()` |
| `quartz.jobStore.acceptEnlistedTransactions` | `JobStore:AcceptEnlistedTransactions` |
| `quartz.jobStore.openConnection` | `JobStore:OpenConnection`, read only by the ambient-transaction store |
| `quartz.jobStore.clusterCheckinInterval` | `JobStore:Clustering:CheckinInterval` |
| `quartz.jobStore.clusterCheckinMisfireThreshold` | `JobStore:Clustering:CheckinMisfireThreshold` |
| `quartz.jobStore.clustering.enabled` | `JobStore:Clustering:Enabled` — the hierarchical spelling of `quartz.jobStore.clustered` |
| `quartz.jobStore.clustering.checkinInterval` | `JobStore:Clustering:CheckinInterval` |
| `quartz.jobStore.clustering.checkinMisfireThreshold` | `JobStore:Clustering:CheckinMisfireThreshold` |
| `quartz.jobStore.driverDelegateType` | `JobStore:DriverDelegateType`; the `UseSqlServer()` family sets it for you |
| `quartz.jobStore.schemaProvisioning` | `JobStore:SchemaProvisioning` — `None`, `Validate` or `Create` |
| `quartz.jobStore.performSchemaValidation` | `JobStore:SchemaProvisioning` — `true` means `Validate`, `false` means `None`; the key above says all three |
| `quartz.jobStore.useDBLocks` | `JobStore:UseDbLocks` |
| `quartz.jobStore.lockOnInsert` | `JobStore:LockOnInsert` |
| `quartz.jobStore.acquireTriggersWithinLock` | `JobStore:AcquireTriggersWithinLock` |
| `quartz.jobStore.selectWithLockSQL` | `JobStore:SelectWithLockSql` |
| `quartz.jobStore.txIsolationLevelSerializable` | `JobStore:TransactionIsolationLevel` — `true` means `Serializable`; unset says nothing rather than `ReadCommitted` |
| `quartz.jobStore.misfireHandlerFrequency` | `JobStore:MisfireHandlerFrequency` |
| `quartz.jobStore.maxMisfiresToHandleAtATime` | `JobStore:MaxMisfiresToHandleAtATime` |
| `quartz.jobStore.doubleCheckLockMisfireHandler` | `JobStore:DoubleCheckLockMisfireHandler` |
| `quartz.jobStore.maxTransientRetries` | `JobStore:MaxTransientRetries` |
| `quartz.jobStore.transientRetryInterval` | `JobStore:TransientRetryInterval` |
| `quartz.jobStore.dbRetryInterval` | `JobStore:DbRetryInterval` |
| `quartz.jobStore.retryableActionErrorLogThreshold` | `JobStore:RetryableActionErrorLogThreshold` |
| `quartz.jobStore.dataSource` | set for you by the database methods |
| `quartz.dataSource.NAME.provider` | `DataSource:NAME:Provider` |
| `quartz.dataSource.NAME.connectionString` | `DataSource:NAME:ConnectionString` |
| `quartz.dataSource.NAME.connectionStringName` | `DataSource:NAME:ConnectionStringName` |
| `quartz.dbprovider.NAME.*` | the metadata factory on `UseGenericDatabase`; the keys still work |
| `quartz.serializer.type` | `UseSystemTextJsonSerializer()` / `UseNewtonsoftJsonSerializer()` |
| `quartz.serializer.PROPERTY` | any other key under this prefix sets that property on the serializer — `quartz.serializer.RegisterTriggerConverters = true`, for instance |
| `quartz.plugin.NAME.type` | `AddPlugin<T>()` or the plugin's own `Use*` method |
| `quartz.jobStore.lockHandler.type` | `UseLockHandler<T>()` |
| `quartz.scheduler.jobFactory.type` | `UseJobFactory<T>()` |
| `quartz.scheduler.typeLoadHelper.type` | `UseTypeLoader<T>()` |
| `quartz.scheduler.instanceIdGenerator.type` | `UseInstanceIdGenerator<T>()`; other `quartz.scheduler.instanceIdGenerator.*` keys configure it |
| `quartz.timeProvider.type` | `UseTimeProvider(timeProvider)` |

Nine key prefixes are rejected rather than ignored, because they no longer configure anything. Each is
reported by name, with the replacement, instead of as an unknown property — a configuration still
carrying one of these was configuring something real, and "unknown property" reads like a typo.

`quartz.scheduler.threadName` and `quartz.scheduler.makeSchedulerThreadDaemon`: the scheduling loop is
a `Task` rather than a `Thread`, so it has no name to set and never held a process open. Remove them;
for the job store's misfire and cluster threads, which are real threads, use
`quartz.jobStore.makeThreadsDaemons` / `JobStore:UseBackgroundThreads`.

`quartz.jobListener.NAME.type` and `quartz.triggerListener.NAME.type`: a listener named by properties
could carry no matchers, so it heard everything, and the type it named had to be found by reflection.
`AddJobListener<T>(matchers)` and `AddTriggerListener<T>(matchers)` take the matchers *and* build the
listener through the container. Registration is where a listener's matchers belong, so there is nothing
the keys said that the registration cannot.

`quartz.jobStore.lockHandler.tablePrefix`, `quartz.jobStore.lockHandler.schedName` and
`quartz.jobStore.lockHandler.schedulerName`: a lock handler is told its table prefix and its scheduler's
name by the job store, through `ILockHandler.Initialize`. Set `quartz.jobStore.tablePrefix` and
`quartz.scheduler.instanceName` and remove these — a 3.x configuration that sets them meets a startup
exception rather than being ignored.

`quartz.scheduler.proxy*` and `quartz.scheduler.exporter*` are the remaining two, described at the end
of this section.

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
