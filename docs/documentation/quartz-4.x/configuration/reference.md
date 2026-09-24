---

title: Configuration Reference
---

# Quartz.NET Configuration Reference

[[toc]]

Options are strongly typed, and each has the same name in code and in a configuration file:

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
reported against the setting that is wrong. The binding is source-generated, so configuring from a file
is as safe under `PublishTrimmed` and `PublishAot` as configuring in code, with nothing asked of your
application.

| Section | Options | |
|---|---|---|
| `Scheduler` | `QuartzSchedulerOptions` | [below](#scheduler) |
| `ThreadPool` | `ThreadPoolOptions` | [below](#thread-pool) |
| `JobStore` | `InMemoryJobStoreOptions` or `AdoJobStoreOptions` | [below](#in-memory-job-store) |
| `JobStore:Clustering` | `ClusteringOptions` | [below](#clustering) |
| `DataSource` | `DataSourceOptions`, one per named data source | [below](#data-source) |
| `Scheduling` | `SchedulingOptions`: when registered jobs and triggers already exist in the store | [below](#scheduling) |
| `TypeLoader` | `TypeLoaderOptions`, the container's rather than a scheduler's | [below](#type-loader) |
| `Schedulers` | one sub-section per named scheduler | [below](#several-schedulers) |
| `Schedule`, `ProcessingDirectives` | jobs and triggers declared in configuration | [JSON configuration](json.md) |

::: tip
Everything on this page can also be written as the flat `quartz.*` keys earlier versions used; Quartz
still accepts them. See [Legacy property keys](#legacy-property-keys).
:::

## Scheduler

`QuartzSchedulerOptions`, bound from `Quartz:Scheduler`.

| Option | Type | Default | Description |
|---|---|---|---|
| `InstanceName` | string | `QuartzScheduler` | Distinguishes schedulers in one process. Every node in a cluster must share it. |
| `InstanceId` | string | `NON_CLUSTERED` | Must be unique among the nodes of a cluster. |
| `GenerateInstanceId` | bool | `false` | Derives `InstanceId` at startup from the registered `IInstanceIdGenerator`. |
| `IdleWaitTime` | TimeSpan | `00:00:30` | How long to wait before re-querying the job store when nothing is due. At least one second. |
| `MaxBatchSize` | int | `1` | Upper bound on triggers acquired at once; may not exceed `ThreadPool:MaxConcurrency`. See [Batching trigger acquisition](../tutorial/advanced-enterprise-features.md#batching-trigger-acquisition). |
| `BatchTriggerAcquisitionFireAheadTimeWindow` | TimeSpan | `00:00:00` | How far ahead of its fire time a trigger may join the current batch. At zero, nothing batches. |
| `ShutdownJobInterruption` | `ShutdownJobInterruption` | `Never` | When a shutting-down scheduler signals cancellation to running jobs. |
| `PropagateTraceContext` | bool | `true` | Stores the ambient trace context on a trigger scheduled inside an `Activity`, so the firing's span links back. See below. |
| `Context` | dictionary | empty | Values seeded into `SchedulerContext`. Get-only: add to it (`options.Context["environment"] = "staging"`). |

- `MaxBatchSize` is only an upper bound: `BatchTriggerAcquisitionFireAheadTimeWindow` decides how many
  triggers are actually taken.
- `PropagateTraceContext` writes two reserved job-data keys. They are visible wherever trigger data is:
  `MergedJobDataMap`, the dashboard, `GET /triggers`, `QRTZ_TRIGGERS.JOB_DATA`. Turn it off to keep
  them out of the store. See [OpenTelemetry integration](../packages/opentelemetry-integration.md).

`ShutdownJobInterruption` values:

| Value | Meaning |
|---|---|
| `Never` | Running jobs are never interrupted. |
| `WhenNotWaitingForJobs` | Interrupted only on a shutdown that does not wait for them. |
| `WhenWaitingForJobs` | Interrupted only on a shutdown that waits. The wait still happens, so a job that checks its cancellation token can unwind cleanly. |
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

Your own implementation:

<!-- snippet: sample_reference_thread_pool_of_your_own -->
```csharp
services.AddQuartz(q => q.UseThreadPool<MyThreadPool>());
```
<!-- endSnippet -->

Two shipped pools are reached through `UseThreadPool<T>()`:

- **`Quartz.Impl.ZeroSizeThreadPool`** — for a scheduler that only *writes* the schedule and is never
  started. It creates no threads, and the two members a running scheduler calls throw
  `NotSupportedException`, so `Start()` fails loudly. Such a process needs no reference to the job
  classes' assemblies and no `ITypeLoader` of its own: the persistent store edits the schedule by stored
  type name alone.
- **`Quartz.Impl.TaskSchedulingThreadPool`** — the open base of `DefaultThreadPool`. Derive from it and
  override one member, `GetDefaultScheduler()`, instead of implementing all six of `IThreadPool`'s.

`ThreadPoolOptions` belongs to the built-in pools (they read `MaxConcurrency`), so `UseThreadPool<T>()`
takes no callback for it. Your own pool has its own options:

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
type as the scheduler's own. A component that takes `IOptions<MyThreadPoolOptions>` then gets the values
configured for *its* scheduler, not the unnamed instance. It works for any container-built component — a
job store, a lock handler, a listener, a job factory — and `AddPlugin<T, TOptions>()` builds on it.

## In-memory job store

`InMemoryJobStoreOptions`, bound from `Quartz:JobStore`. The default store; it does not survive a
process restart.

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
for its SQL dialect, so a connection string is usually all you supply:

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
| `StoreJobDataAsStrings` | bool | `false` | Persists job data as name/value strings, not serialized objects, so it stays readable and version tolerant. |
| `MisfireThreshold` | TimeSpan | `00:01:00` | How late a trigger may fire before it counts as misfired. |
| `MisfireHandlerFrequency` | TimeSpan? | `MisfireThreshold` | How often misfires are handled. |
| `MaxMisfiresToHandleAtATime` | int | `20` | Misfired triggers handled per pass. |
| `CommandTimeout` | TimeSpan? | provider default | Timeout for every statement the store issues, the lock handler's included. See below. |
| `LockWaitWarningThreshold` | TimeSpan? | `00:00:30` | How long one attempt to take a job store lock may wait before warning **3716** is logged. See below. |
| `DbRetryInterval` | TimeSpan | `00:00:15` | Back-off after a database failure, for the misfire loop and the check-in loop. See below. |
| `MaxTransientRetries` | int | `3` | How many times a transient failure, such as a deadlock, is retried. See below. |
| `TransientRetryInterval` | TimeSpan | `00:00:01` | Delay between transient retries. |
| `RetryableActionErrorLogThreshold` | int | `4` | Consecutive failures before they are logged as errors. |
| `IsTransient` | `Func<Exception, bool>?` | `null` | Extra transient test for a driver the built-in list misses. Code only. See below. |
| `UseDbLocks` | bool | `false` | Uses database row locks. Required for clustering, and implied by `UseClustering()`. |
| `LockOnInsert` | bool | `true` | Takes a lock when inserting rows. |
| `AcquireTriggersWithinLock` | bool | `false` | Acquires triggers inside the database lock. |
| `TransactionIsolationLevel` | IsolationLevel? | none | Isolation level of the store's own transactions. Unset: `ReadCommitted`. See below. |
| `AcceptEnlistedTransactions` | bool | `false` | Lets the store use a connection the application enlisted with `SchedulerEnlistmentExtensions.EnlistTransaction`, so scheduling commits with the application's work. See [Joining an existing transaction](../tutorial/job-stores.md#joining-an-existing-transaction). |
| `DoubleCheckLockMisfireHandler` | bool | `true` | Re-checks the lock before handling misfires. |
| `UseBackgroundThreads` | bool | `false` | Runs the misfire handler and cluster manager on background threads, which do not keep the process alive. They are the only real threads Quartz creates. |
| `SchemaProvisioning` | `SchemaProvisioning` | `Validate` | At startup: `None` does nothing, `Validate` checks the expected tables exist, `CreateIfMissing` creates what is missing and then checks. |
| `SelectWithLockSql` | string? | none | Overrides the row-lock statement; defaults to SQL Server's `WITH (UPDLOCK,ROWLOCK)` form on SQL Server. See [Locking](#locking). |
| `OpenConnection` | bool | `false` | Whether the ambient-transaction store opens the connections it creates. See below. |

- **`CommandTimeout`**: unset leaves each provider's default, usually 30 seconds. ADO.NET counts whole
  seconds, so the value is rounded **up** — `00:00:01.500` becomes 2 seconds — because rounding down
  would turn a sub-second value into `0`, which means "no timeout".
- **`LockWaitWarningThreshold`**: logged once per acquisition, naming the lock and the wait so far. It is
  the only report of a node stalled on a lock, since a blocked statement neither returns nor throws. It
  does not end the wait; `CommandTimeout` or a wait timeout in the lock statement does. `null` turns it
  off; zero is refused. See
  [A Lock Held by a Connection That Is Gone](../../troubleshooting.md#a-lock-held-by-a-connection-that-is-gone).
- **`DbRetryInterval`**: the check-in loop waits this long once a failed check-in has spent the window its
  peers give it (`CheckinInterval` + `CheckinMisfireThreshold`). Inside that window it retries sooner,
  and this only caps the wait.
- **`MaxTransientRetries`**: transient means the driver's own `DbException.IsTransient`; a SQLSTATE in
  class `40` ("transaction rollback": a serialization failure or deadlock, whichever provider reports
  it), except `40002`, a deferred constraint violation that fails the same way every time; SQL Server's
  transient error numbers; SQLite's busy and locked codes; or a timeout.
- **`IsTransient`**: consulted first and only additive — `false` falls through to the built-in list, so
  it cannot stop a retry Quartz already makes. It receives the store's own exception; reach the driver's
  with `GetBaseException()`. There is no `quartz.*` key for a delegate.
- **`TransactionIsolationLevel`**: the `ReadCommitted` default is Quartz's, not the provider's (providers
  vary). Forced to `Serializable` on SQLite. Ignored for a connection the application enlisted, which
  runs at the application's level.
- **`SelectWithLockSql`** is read only when the store builds a database-locking handler for itself.
- **`OpenConnection`** is read only by `ExternalTransactionJobStore`, the store
  `UsePersistentStore(store => store.UseAmbientTransactions())` selects. It is written by
  `quartz.jobStore.openConnection` as well as by the section entry.

A custom trigger persistence delegate is registered with
`UsePersistentStore(s => s.UseTriggerPersistenceDelegate<T>())`, not an option; the legacy
`quartz.jobStore.driverDelegateInitString` key translates to the same registrations.

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
| `UseGenericDatabase` | Anything else, with the generic SQL dialect; the only one that can [describe its own driver](#describing-a-driver-quartz-does-not-know) | the one your driver ships in |

Quartz references none of these packages; the application adds its own. A method called without its
driver present compiles, then fails at startup with `Could not load file or assembly`. See
[Job Stores](../tutorial/job-stores.md#configuring-a-persistent-store).

Each takes a connection string or a callback over `DataSourceOptions`:

<!-- snippet: sample_reference_connection_string -->
```csharp
store.UseSqlServer(connectionString);
store.UseSqlServer(db => db.ConnectionStringName = "Scheduler");
```
<!-- endSnippet -->

To connect through a `DbDataSource` registered in the container instead, set
`store.UseSqlServer(db => db.UseRegisteredDataSource = true)`.

#### Naming a driver, or handing over its factory

Both spellings above name the driver. `SqlServer` names a description of which connection, command and
parameter types to instantiate, and Quartz resolves those types from strings, since it references no
driver package. Most applications want this.

A **trimmed or native AOT** application cannot rely on it: the trimmer does not follow a type name and
removes what it pointed at, and the registration fails while the container is built with
`Cannot instantiate type which has no empty constructor`. Every method above also takes the driver's
`DbProviderFactory`:

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

The factory creates the connection, the connection the command, and the command its parameters, so
nothing is named or built by reflection. The provider name is still chosen for you, since it decides how
the driver spells a parameter, but only the part of its description that names no type is read.

| Registration | When |
|---|---|
| `Use<Db>(connectionString)` | The ordinary case. Carries `[RequiresUnreferencedCode]`, so a trimmed publish reports it. |
| `Use<Db>(factory, connectionString)` | `PublishTrimmed` or `PublishAot`, or anywhere you do not want a type resolved from a string. |
| `db.UseRegisteredDataSource = true` | A `DbDataSource` in the container already holds the connection details — pooling, type mappers, logging. Also free of type names. |

Oracle needs more than a factory, because Quartz reaches two things on its types by reflection and a
factory names neither. Say both in code:

<!-- Not a compiled sample, for the same reason as the one above. -->

```csharp
store.UseOracle(
    OracleClientFactory.Instance,
    connectionString,
    configureCommand: command => ((OracleCommand) command).BindByName = true,
    configureBinaryParameter: parameter => ((OracleParameter) parameter).OracleDbType = OracleDbType.Blob);
```

- Without `configureCommand`, ODP.NET binds parameters by position and the store reads the wrong columns.
- Without `configureBinaryParameter`, a job data map over two kilobytes will not fit, because that driver
  maps `DbType.Binary` to `OracleDbType.Raw`, not `Blob`.

Naming the driver instead of passing its factory sets both for you.

The factory overloads take the connection string directly, so `ConnectionStringName` does not apply; read
the connection string from `IConfiguration` and pass it in.

#### PostgreSQL: `DISCARD ALL` on every connection return

Npgsql resets a pooled connection on return by sending `DISCARD ALL`: one round trip per return, recorded
by the server as a transaction of its own, so PostgreSQL counts about twice the transactions Quartz
opens. At the shipped defaults a firing borrows three connections (acquisition, fire and completion are
separate transactions), so it costs three `DISCARD ALL` round trips and three extra commits — most of a
`pg_stat_database` reading of a Quartz deployment.

`No Reset On Close=true` on the connection string turns it off:

```text
Host=db;Database=quartznet;Username=quartz;Password=…;No Reset On Close=true
```

Measured on a drain of 500 one-off firings against PostgreSQL 15.1 with `fsync=on`, pool size 10
(`dotnet run -c Release --project src/Quartz.Benchmark -- --one-off-census`):

| | commits per firing | statements per firing |
|---|---:|---:|
| shipped defaults | 6.01 | 23.0 |
| with `No Reset On Close=true` | 3.01 | 20.0 |

**It is a connection-string decision, and Quartz does not change it for you.**

- Over loopback, firings per second did not move beyond run-to-run variance. Across a network, or behind
  a managed database's connection proxy, measure what three fewer round trips per firing buy.
- A connection returns to the pool carrying its session state: `SET` statements, prepared statements,
  listen/notify registrations, temporary tables. Quartz sets none, so it is safe for a data source only
  Quartz uses — not for one shared with application code that sets session state.

#### Describing a driver Quartz does not know

A provider name (`SqlServer`, `Npgsql`, …) names a description of an ADO.NET driver: which connection,
command and parameter types to instantiate, how parameters are named, and which enum value means "binary
column". Quartz ships descriptions for every database above. For anything else, describe the driver in
the `UseGenericDatabase` call:

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

A four-argument overload takes a `DataSourceOptions` callback instead of a connection string, for a
described driver with a named connection string.

A driver reached through its own factory is described the same way and needs no provider name:

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

- `ConfigureCommand` and `ConfigureBinaryParameter` are the two typed seams on `DbMetadata`. They say what
  the name path reaches by reflecting over `CommandType` and `ParameterType`, so a description that names
  no type is still complete. Either may be unset: a binary parameter with neither a seam nor a described
  parameter type is bound as `DbType.Binary`, which every driver that ships a factory maps itself.
- A description is a container registration, not process-wide state, so two containers in one process
  need not agree on a provider name. Within one container a name means one thing; two schedulers needing
  two drivers use two names.
- Describing a name Quartz already ships replaces it. A description registered in code wins over
  `quartz.dbprovider.*` keys. Several drivers mean several calls, one per name.

The same description as properties, the 3.x form, now read through `IConfiguration`:

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

### Locking

Leave the lock handler unset and the store picks one once it knows the database: database row locks when
clustered or when `UseDbLocks` is on, an in-process monitor otherwise. `UseLockHandler<T>()` overrides
that; `UseLockHandler(factory)` does the same for a handler that needs building, as
`UseRedisLockHandler()` does.

- `SelectWithLockSql` belongs to the handler the store builds for itself. A handler from `UseLockHandler`
  takes its statement through its own constructor, so setting both leaves the option unused, and the
  store logs a warning at startup.
- `SelectWithLockSql` is also where a server-side lock wait timeout goes — on Oracle, the only place. A
  statement ending `FOR UPDATE WAIT 20` fails with `ORA-30006` instead of waiting behind a lock nobody
  will release — see [A Lock Held by a Connection That Is Gone](../../troubleshooting.md#a-lock-held-by-a-connection-that-is-gone).
- This and `UseSerializer` register against the scheduler that owns the store. `ILockHandler` or
  `IObjectSerializer` registered directly on `Services` is registered for the container, and a named
  scheduler does not see it.
- The store tells any handler which scheduler it locks for, and its clock and `CommandTimeout`, through
  `ILockHandler.Initialize(LockHandlerContext)` before the first lock; a handler of your own needs no
  configuring for them.
- Set the timeout on a clustered store: a node waiting on `QRTZ_LOCKS` behind a peer that stopped
  without releasing the row schedules nothing until the lock statement gives up.

### Data source

`DataSourceOptions`, bound from `Quartz:DataSource`.

| Option | Type | Description |
|---|---|---|
| `Provider` | string | The driver description to use. Set by the database methods; see the constants below, and [Describing a driver Quartz does not know](#describing-a-driver-quartz-does-not-know). |
| `ConnectionString` | string? | The connection string. Takes precedence over `ConnectionStringName`. |
| `ConnectionStringName` | string? | A connection string to resolve from `IConfiguration`. |
| `UseRegisteredDataSource` | bool | Connections come from the container's unkeyed `DbDataSource`. Wins over both connection string settings. |
| `DataSourceServiceKey` | object? | The service key of the `DbDataSource`, for a container holding several. Implies `UseRegisteredDataSource`. Code only. |
| `DataSourceFactory` | Func&lt;IServiceProvider, DbDataSource&gt;? | Supplies the `DbDataSource` directly. Wins over both of the above. Code only. |

The provider names Quartz ships a description for, as constants on `DataSourceOptions.Providers`:

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

`Provider` is a string, not an enum, because the set is open: `UseGenericDatabase` describes drivers
Quartz does not know.

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

`UseRegisteredDataSource` asks for the container's one unkeyed `DbDataSource`, which fits an application
with one database. A container with several (a scheduler per tenant, or a reporting scheduler beside the
application's) keys them, and `DataSourceServiceKey` names this store's key:

<!-- Not a compiled sample, for the same reason as the one above: `AddNpgsqlDataSource` is Npgsql's. -->

```csharp
services.AddNpgsqlDataSource(tenantA, serviceKey: "tenant-a");
services.AddNpgsqlDataSource(tenantB, serviceKey: "tenant-b");

services.AddQuartz("tenant-a", q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "tenant-a")));
services.AddQuartz("tenant-b", q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "tenant-b")));
```

A data source you build rather than register goes in `DataSourceFactory`, which wins over both:

<!-- snippet: sample_reference_data_source_factory -->
```csharp
store.UsePostgres(db => db.DataSourceFactory = _ => BuildDataSource());
```
<!-- endSnippet -->

- Both are set in code: a service key can be any object and a factory is a delegate, neither of which a
  configuration binder can produce. With either, Quartz needs no connection string.
- On this path the connection makes the commands, not the driver description, so whatever the data source
  configured — an `NpgsqlDataSource`'s type mappers, logging, composite type registrations — applies to
  Quartz's statements too.

A store's data source is named after the scheduler that owns it, or `quartz` for the default scheduler.
To name it explicitly, call `store.UseDataSource("reporting-db")` **before** choosing the database (the
name is fixed once the data source is configured) — when two stores should read the same
`Quartz:DataSource:<name>` settings, or the settings live under a name of your choosing.

The three data-source entry points:

- `UseDataSource(configure)` **defines** one: which driver, and how to reach the database. The database
  methods are shorthands for it.
- `UseDataSource(name)` **refers to** one by name, to pick up settings registered elsewhere, such as a
  `Quartz:DataSource:<name>` section.
- Where the connection comes from is set on `DataSourceOptions`, not by another method.

#### Bringing your own connection provider

When connections cannot be described — a pooled or credential-rotating factory, or a driver whose
connections need setting up after creation — hand Quartz the object that makes them:

<!-- snippet: sample_reference_connection_provider -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);          // still selects the driver delegate
    store.UseConnectionProvider<MyDbProvider>();   // …but connections come from here
}));
```
<!-- endSnippet -->

- `UseConnectionProvider(factory)` does the same for a provider that needs building.
- It is the one builder method that **replaces** rather than defers: it wins over the database method's
  provider in either order.
- It also names the store's data source, so it is a complete configuration on its own; the database
  method only selects the driver delegate.
- The provider belongs to the scheduler that owns the store. `IDbProvider` registered on `Services` is
  the container's, and a named scheduler does not see it.

As properties, the 3.x spelling, still read:

```json
{
  "Quartz": {
    "quartz.jobStore.dataSource": "myDs",
    "quartz.dataSource.myDs.connectionProvider.type": "MyNamespace.MyDbProvider, MyAssembly"
  }
}
```

### Clustering

Several schedulers share one database, and a node's triggers are recovered by another if it dies. Every
node uses the same `InstanceName` and a different `InstanceId`.

`ClusteringOptions`, bound from `Quartz:JobStore:Clustering` — the only place clustering is configured.

| Option | Type | Default | Description |
|---|---|---|---|
| `Enabled` | bool | `false` | Takes part in a cluster sharing this database. `UseClustering()` sets it. |
| `CheckinInterval` | TimeSpan | `00:00:07.5` | How often a node records that it is alive. |
| `CheckinMisfireThreshold` | TimeSpan | `00:00:07.5` | Grace period before a node is treated as failed. The node's own failed check-in is retried inside it, so a shorter database blip does not get the node written off. |

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

`UseClustering()` also enables database locking; clustering does not work without it.

## Serialization

What a persistent store cannot write as a string goes through an `IObjectSerializer`. **A store that
names none gets `SystemTextJsonObjectSerializer`**, registered as a fallback like the driver delegate, so
`UseSystemTextJsonSerializer()` with no argument changes nothing. Two calls do change something:

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

`SchedulingOptions`, bound from `Quartz:Scheduling`: what happens when jobs and triggers registered in
code, or declared in a file, already exist in the store under the same names.

| Option | Type | Default | Description |
|---|---|---|---|
| `OverwriteExistingData` | bool | `true` | A registration replaces the stored job or trigger of the same name. Setting `IgnoreDuplicates` turns this default off. |
| `IgnoreDuplicates` | bool | `false` | An existing name is skipped instead of throwing. Turns `OverwriteExistingData` off; setting both explicitly is refused at startup. |
| `ScheduleTriggerRelativeToReplacedTrigger` | bool | `false` | A replaced trigger's next fire time is computed from the old trigger's last fire time, not from now. |

These compare a file or registration with the store. None of them covers one scheduling data file that
declares the same job or trigger key twice, which is always an error — see
[ProcessingDirectives](json.md#processingdirectives).

<!-- snippet: sample_reference_scheduling_options -->
```csharp
services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);
```
<!-- endSnippet -->

## Job factory

By default jobs are resolved from the container, in a scope per firing, so a job may take scoped
dependencies. To replace the factory:

<!-- snippet: sample_reference_job_factory -->
```csharp
services.AddQuartz(q => q.UseJobFactory<MyJobFactory>());
```
<!-- endSnippet -->

To keep it and add to the scope it opens:

<!-- snippet: sample_reference_job_scope -->
```csharp
services.AddQuartz(q => q.ConfigureJobScope((scope, bundle, scheduler) => { /* … */ }));
```
<!-- endSnippet -->

## Type loader

`TypeLoaderOptions`, bound from `Quartz:TypeLoader`. The one options type that is the **container's**,
because one `ITypeLoader` serves every scheduler in the container.

| Option | Type | Default | Description |
|---|---|---|---|
| `Aliases` | `Dictionary<string, string>` | empty | Maps a type name that no longer exists, as stored or configured, to the type that replaced it. |

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

- An alias applies wherever Quartz turns a string into a type at run time: a stored `JOB_CLASS_NAME`, a
  job named in XML or JSON scheduling data, a `quartz.plugin.<name>.type` key.
- Flat keys naming a scheduler's own components are read while the service collection is built, before
  options exist, and are not aliased.
- An alias whose target names no loadable type fails options validation at startup.
- Nothing is written back, so retiring an alias still needs the SQL `UPDATE` — see
  [Job deserialization failures after refactoring](../../troubleshooting.md#job-deserialization-failures-after-refactoring).

## The other seams

Each replaces one collaborator of the scheduler. All are `IQuartzBuilder` members, so they work the same
under `AddQuartz` and `QuartzSchedulerBuilder.Create(q => …)`.

| Method | Replaces | Default |
|---|---|---|
| `UseTimeProvider(timeProvider)` | the clock every trigger, store and misfire calculation reads | `TimeProvider.System`, or the container's registration if any |
| `UseTypeLoader<T>()` | how a type named by a string (a stored `JOB_CLASS_NAME`, a `.type` key) is resolved | resolution through the container's assemblies, with the 3.x namespace fallbacks |
| `UseTypeLoader(configure)` | *configures* the loader instead: `loader.Map(oldName, typeof(NewType))`, the same map as `Quartz:TypeLoader:Aliases` | no aliases |
| `UseSimpleTypeLoader()` | selects the built-in loader, which is internal, so there is no `UseTypeLoader<SimpleTypeLoader>()`. Matters only if something else registered a loader first | this is the default |
| `UseInstanceIdGenerator<T>()` | how `InstanceId` is derived when `GenerateInstanceId` is on | `SimpleInstanceIdGenerator`: host name plus a timestamp |
| `UseJobStore<T>()`, `UseJobStore<T, TOptions>()` | the job store, for one Quartz does not ship | the in-memory store |
| `UseDriverDelegate<T>()`, `UseDriverDelegate(factory)` (persistent store builder) | the SQL dialect of the ADO.NET store | chosen by the database method: `UseSqlServer` picks `SqlServerDelegate`, and so on |

`UseTimeProvider` is for tests: a `FakeTimeProvider` makes `TriggerBuilder`, `GetFireTimeAfter` and
misfire calculations see the time you set. The scheduler's own waiting still uses the real clock.

## Health check

`QuartzHealthCheckOptions`, set by `AddHealthChecks().AddQuartz(configure)` or the scheduler's own
`AddQuartzHealthChecks(configure)`. Options are per scheduler name.

| Option | Type | Default | Description |
|---|---|---|---|
| `Name` | string? | `quartz-scheduler`, or `quartz-scheduler-<scheduler name>` | The check's registered name. |
| `Tags` | List&lt;string&gt; | empty | What a probe filters on. Add to it; a blank or repeated tag is refused at startup. |
| `FailureStatus` | HealthStatus? | `Unhealthy` | What a failed check reports. |
| `StandbyStatus` | HealthStatus? | `Degraded` | What a scheduler in standby reports. Standby only: a scheduler waiting for the application to start it stays degraded. |
| `ClusterCheckinTolerance` | double? | `3` | How many of its own check-in intervals a **clustered** node may miss before the check reports degraded. `null` or `0`: no query. Unclustered schedulers read nothing. |
| `StaleFiringTolerance` | double? | `null` | How many of the store's misfire thresholds a schedulable trigger may be overdue before degraded; twice that is unhealthy. `null` or `0`: no query. `3` is a starting value. |

`StaleFiringTolerance` is off by default because what counts as overdue is the application's call.
Standby and paused schedulers report as usual with it on. See
[Health checks and probes](../operations.md#health-checks-and-probes) for what each verdict means to a
probe and what the check does not assert.

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

Listeners and plugins are ordinary services and take dependencies through their constructors.

## Several schedulers

A scheduler registered under a name has its own job store, thread pool, jobs and configuration. The name
is its instance name, the key its services are registered under, and the name of its options.

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

Console applications and tests without a host use `QuartzSchedulerBuilder`. `Create` hands the callback
the same `IQuartzBuilder` that `AddQuartz(q => …)` does, over a container it creates itself:

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

It adds only the terminal methods: `Build()` for the factory and `BuildScheduler()` for the scheduler.
`AddJob`, `AddTrigger`, `ScheduleJob`, `AddCalendar` and every package's extensions go inside the
callback, as under a host.

A scheduler configured entirely by flat `quartz.*` keys:

<!-- snippet: sample_reference_from_flat_properties -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .UseProperties(properties)
    .BuildScheduler();
```
<!-- endSnippet -->

`UseProperties` checks keys against the ones Quartz reads and reports a misspelling; set
`quartz.checkConfiguration` to `false` to allow keys of your own. Code wins over properties, whichever
is applied first.

**The check covers a property bag you wrote, not `appsettings.json`.**

- It runs for `UseProperties` and the `AddQuartz(services, properties, …)` overloads, the shape a 3.x
  application migrates in.
- Keys from an `IConfiguration` section are not checked: every key under `Quartz:` becomes a `quartz.*`
  key whether Quartz reads it or not, so your own settings there would be rejected.
- So a misspelled key in `appsettings.json` is silently ignored: `Quartz:Scheduler:IdelWaitTime` leaves
  the default thirty seconds in force. Check a new key against the tables above.
- Casing is not the risk: configuration keys are case-insensitive, so `Quartz:Jobstore:TablePrefix` is
  `Quartz:JobStore:TablePrefix`.

**The job store is checked either way.** A persistent store refuses a `quartz.jobStore.*` key that
nothing reads, written flat or as `Quartz:JobStore:TabelPrefix` — see
[Unknown job store keys](#unknown-job-store-keys). The keys under that prefix are known exhaustively: the
store's options, its clustering sub-section, its lock handler's keys, and the two keys that select a type.

## Legacy property keys

Earlier versions used flat `quartz.*` string keys. They still work: each is translated into the option
above and produces the same result.

- Flat-format durations are integer **milliseconds** (`quartz.scheduler.idleWaitTime = 30000`); typed
  options are `TimeSpan` (`"00:00:30"`).
- A `.type` key names an implementation. In code, use the matching `Use*` method, which the compiler
  checks.

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
| `quartz.jobStore.type` | `UseInMemoryStore()` / `UsePersistentStore()`, with `UseAmbientTransactions()` inside it for the store 3.x called `JobStoreCMT`; `UsePersistentStore<T>()` for a persistent store of your own |
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
| `quartz.jobStore.driverDelegateType` | `JobStore:DriverDelegateType`; the `UseSqlServer()` family sets it |
| `quartz.jobStore.schemaProvisioning` | `JobStore:SchemaProvisioning` — `None`, `Validate` or `Create` |
| `quartz.jobStore.performSchemaValidation` | `JobStore:SchemaProvisioning` — `true` means `Validate`, `false` means `None`; the key above says all three |
| `quartz.jobStore.executionHistory` | `UseExecutionHistory()` — keeps the execution history in this scheduler's database. Needs the two tables `database/migrations/4.2/add_execution_history_<db>.sql` creates; the store refuses to start without them |
| `quartz.jobStore.useDBLocks` | `JobStore:UseDbLocks` |
| `quartz.jobStore.lockOnInsert` | `JobStore:LockOnInsert` |
| `quartz.jobStore.acquireTriggersWithinLock` | `JobStore:AcquireTriggersWithinLock` |
| `quartz.jobStore.selectWithLockSQL` | `JobStore:SelectWithLockSql` |
| `quartz.jobStore.txIsolationLevelSerializable` | `JobStore:TransactionIsolationLevel` — `true` means `Serializable`; unset says nothing, not `ReadCommitted` |
| `quartz.jobStore.misfireHandlerFrequency` | `JobStore:MisfireHandlerFrequency` |
| `quartz.jobStore.maxMisfiresToHandleAtATime` | `JobStore:MaxMisfiresToHandleAtATime` |
| `quartz.jobStore.doubleCheckLockMisfireHandler` | `JobStore:DoubleCheckLockMisfireHandler` |
| `quartz.jobStore.maxTransientRetries` | `JobStore:MaxTransientRetries` |
| `quartz.jobStore.transientRetryInterval` | `JobStore:TransientRetryInterval` |
| `quartz.jobStore.dbRetryInterval` | `JobStore:DbRetryInterval` |
| `quartz.jobStore.commandTimeout` | `JobStore:CommandTimeout` — added in 3.22, in milliseconds; `0` means the provider's default, i.e. the option left unset |
| `quartz.jobStore.retryableActionErrorLogThreshold` | `JobStore:RetryableActionErrorLogThreshold` |
| `quartz.jobStore.dataSource` | set by the database methods |
| `quartz.dataSource.NAME.provider` | `DataSource:NAME:Provider` |
| `quartz.dataSource.NAME.connectionString` | `DataSource:NAME:ConnectionString` |
| `quartz.dataSource.NAME.connectionStringName` | `DataSource:NAME:ConnectionStringName` |
| `quartz.dbprovider.NAME.*` | the metadata factory on `UseGenericDatabase`; the keys still work |
| `quartz.serializer.type` | `UseSystemTextJsonSerializer()` / `UseNewtonsoftJsonSerializer()` |
| `quartz.serializer.PROPERTY` | sets that property on the serializer — e.g. `quartz.serializer.RegisterTriggerConverters = true` |
| `quartz.plugin.NAME.type` | `AddPlugin<T>()` or the plugin's own `Use*` method |
| `quartz.jobStore.lockHandler.type` | `UseLockHandler<T>()` |
| `quartz.scheduler.jobFactory.type` | `UseJobFactory<T>()` |
| `quartz.scheduler.typeLoadHelper.type` | `UseTypeLoader<T>()` |
| `quartz.scheduler.instanceIdGenerator.type` | `UseInstanceIdGenerator<T>()`; other `quartz.scheduler.instanceIdGenerator.*` keys configure it |
| `quartz.timeProvider.type` | `UseTimeProvider(timeProvider)` |

**Nine key prefixes are rejected rather than ignored**, because they no longer configure anything. Each
is reported by name with its replacement, not as an unknown property, since a configuration carrying one
was configuring something real:

| Rejected key | Why | Instead |
|---|---|---|
| `quartz.scheduler.threadName`, `quartz.scheduler.makeSchedulerThreadDaemon` | the scheduling loop is a `Task`, not a `Thread`: it has no name and never held a process open | remove them; for the misfire and cluster threads, which are real threads, use `quartz.jobStore.makeThreadsDaemons` / `JobStore:UseBackgroundThreads` |
| `quartz.jobListener.NAME.type`, `quartz.triggerListener.NAME.type` | a listener named by properties had no matchers, so it heard everything, and its type was found by reflection | `AddJobListener<T>(matchers)` and `AddTriggerListener<T>(matchers)`, which take the matchers and build the listener through the container |
| `quartz.jobStore.lockHandler.tablePrefix`, `quartz.jobStore.lockHandler.schedName`, `quartz.jobStore.lockHandler.schedulerName` | the job store tells the lock handler its table prefix and scheduler name through `ILockHandler.Initialize` | set `quartz.jobStore.tablePrefix` and `quartz.scheduler.instanceName`; a 3.x configuration that sets these fails at startup |
| `quartz.scheduler.proxy*`, `quartz.scheduler.exporter*` | remoting, which .NET no longer supports | nothing |

**Removed in 4.x**, with no replacement: `quartz.scheduler.proxy*` and `quartz.scheduler.exporter*`
(rejected with an exception, as above, not ignored) and `quartz.threadExecutor*` (no implementation left
to choose).

- **Every key has both spellings**, including those that select an implementation: `JobStore:Type`,
  `JobStore:DriverDelegateType`, `JobStore:LockHandler:Type`, `ThreadPool:Type` and the rest. A file never
  has to mix the forms, and a component with no options type of its own is still configurable, since its
  settings are read as flat keys either way.
- **Durations** may be `00:00:30` or a bare `30000`, read as milliseconds for 3.x configuration files.
- **Code wins** when a setting is given twice: a `UsePersistentStore` in code beats a leftover
  `quartz.jobStore.type`, and `ConfigureScheduler` beats `appsettings.json`. The built-in fallbacks (the
  driver delegate and the serializer) are registered last, so they apply only when nothing else claimed
  the slot.

### Unknown job store keys

A persistent store refuses, by name, a key under `quartz.jobStore` that nothing reads, when it resolves
its settings (in the startup validation `UsePersistentStore` declares, or when the store is built):

```text
Unknown configuration property 'quartz.jobStore.dbRetryIntreval'. It is not a setting of the ADO.NET
job store, and no other reader consults it. Set 'quartz.checkConfiguration' to false to allow keys
Quartz does not read.
```

(3.x failed on such a key too. 4.0 ignored it, so a typo, or a key a newer 3.x line added that 4.x did
not yet translate, left the default in force silently.)

Counted as read:

- every flat key in the tables above;
- every property of `AdoJobStoreOptions` and its `Clustering` sub-section, spelled as the options type
  spells it;
- every `quartz.jobStore.lockHandler.*` key, written onto the lock handler by name, which reports an
  unknown one itself;
- `quartz.jobStore.driverDelegateInitString`, whose contents are checked as they are parsed.

Case does not matter: `quartz.jobstore.tableprefix` is `quartz.jobStore.tablePrefix`.

Not covered:

- A store with no options type of its own (yours, or another package's) still has leftover keys under
  this prefix written onto it by name, so an unknown one fails there, with the same message.
- An in-memory store refuses nothing: it reads one key under this prefix, and the others are not its
  settings.
