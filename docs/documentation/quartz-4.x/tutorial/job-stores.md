---

title: 'Job Stores'
---

# Job Stores

A job store keeps the scheduler's work data: jobs, triggers, calendars. Choose it where you configure the
scheduler, with `q.UseInMemoryStore()` or `q.UsePersistentStore(…)` inside `AddQuartz`.

| Store | Keeps data | Speed | Survives a restart |
|---|---|---|---|
| `RAMJobStore` | in memory | fastest | no |
| AdoJobStore | in a database, through ADO.NET | slower | yes |

::: warning
Never use a JobStore instance directly in your code. The job store is for Quartz's own use: tell Quartz
which one to use in configuration, then work only with the `IScheduler` interface.
:::

## RAMJobStore

`RAMJobStore` keeps all its data in memory. It is the simplest store to configure and the fastest (in CPU
time). When the application ends or crashes, all scheduling information is lost, so `RAMJobStore` cannot
honour "non-volatility" on jobs and triggers. Whether that is acceptable depends on the application.

`RAMJobStore` is the default; you only call `UseInMemoryStore` to change one of its settings:

<!-- snippet: sample_job_stores_in_memory -->
```csharp
builder.Services.AddQuartz(q =>
{
    // this is the default, so the call is only needed to change one of its settings
    q.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(30));
});
```
<!-- endSnippet -->

## ADO.NET Job Store (AdoJobStore)

AdoJobStore keeps all its data in a database through ADO.NET. It takes more configuration than
`RAMJobStore` and is slower, though not by much, especially with indexes on the primary keys.

Its tables must exist before it starts. Either let the store create them (see
[Creating the schema](#creating-the-schema)), or run the DDL yourself, as a production database usually
wants:

* The table-creation scripts are in
  "[database/tables](https://github.com/quartznet/quartznet/tree/main/database/tables)".
* Each script drops an existing Quartz schema before recreating it. Read its header first; it says how to
  prevent that on a database you care about.
* The SQL Server scripts begin `USE [enter_db_name_here];`. Put your database name there first, or the
  script stops on `Msg 911` before creating anything.
* For a database with no script, adapt an existing one.
* The tables are prefixed `QRTZ_` (`QRTZ_TRIGGERS`, `QRTZ_JOB_DETAIL`). Any prefix works if you tell
  AdoJobStore what it is. Different prefixes let several scheduler instances keep separate table sets in
  one database.

| Store | Transactions | Selected by |
|---|---|---|
| `LocalTransactionJobStore` | creates its own; can also use a connection you own (see [Joining an existing transaction](#joining-an-existing-transaction)) | `UsePersistentStore` (the one you normally want) |
| `ExternalTransactionJobStore` | the container manages the ambient transaction; the store neither commits nor rolls back | `UsePersistentStore(store => store.UseAmbientTransactions())` |

Both types are internal; you choose by the call, not a type argument. `quartz.jobStore.type` still names
either as a string, including under the 3.x `JobStoreTX` / `JobStoreCMT` spellings.

### Configuring a persistent store

Naming the database selects the driver delegate for its SQL dialect and the ADO.NET provider, so a
connection string is usually all you supply:

<!-- snippet: sample_job_stores_persistent -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UseSqlServer("Server=localhost;Database=quartz;Trusted_Connection=True;Encrypt=False");

        store.ConfigureStore(options =>
        {
            options.TablePrefix = "QRTZ_";
            options.StoreJobDataAsStrings = true;
        });
    });
});
```
<!-- endSnippet -->

| Method | Database | Driver package |
|---|---|---|
| `UseSqlServer` | Microsoft SQL Server | `dotnet add package Microsoft.Data.SqlClient` |
| `UsePostgres` | PostgreSQL | `dotnet add package Npgsql` |
| `UseMySql` | MySQL | `dotnet add package MySql.Data` |
| `UseMySqlConnector` | MySQL | `dotnet add package MySqlConnector` |
| `UseOracle` | Oracle | `dotnet add package Oracle.ManagedDataAccess.Core` |
| `UseFirebird` | Firebird | `dotnet add package FirebirdSql.Data.FirebirdClient` |
| `UseSqlite` | SQLite | `dotnet add package Microsoft.Data.Sqlite` |
| `UseSystemDataSqlite` | SQLite, legacy driver | `dotnet add package System.Data.SQLite.Core` |
| `UseGenericDatabase` | anything else, using the generic SQL dialect | one you describe |

**Reference the driver package yourself; nothing checks for it until the scheduler starts.** Quartz loads
the driver's types by name, so a project that calls `UsePostgres` without referencing Npgsql compiles and
then fails as the store initializes:

```text
System.ArgumentException: Error while reading metadata information for provider 'Npgsql' (Parameter 'providerName')
 ---> System.IO.FileNotFoundException: Could not load file or assembly 'Npgsql, ...'
```

Each method also takes a callback over `DataSourceOptions` instead of a connection string:

* a named connection string: `db => db.ConnectionStringName = "Scheduler"`;
* a `DbDataSource` the container already holds: `db => db.UseRegisteredDataSource = true`.

::: warning
SQLite cannot take part in a [cluster](advanced-enterprise-features.md): it locks in process rather than in the
database, so the row locks a cluster coordinates through do not hold between nodes. `UseSqlite` or
`UseSystemDataSqlite` together with `UseClustering()` fails as the store initializes, with a
`Quartz.Impl.AdoJobStore.InvalidConfigurationException`.
:::

::: tip
`UseGenericDatabase` uses `StdAdoDelegate`, which writes portable SQL and so cannot limit a result set: it
reads every candidate trigger and discards the surplus in memory. The database-specific delegates page
their queries (`TOP n`, `LIMIT n`, `FETCH FIRST n ROWS ONLY`), which matters once a scheduler has many
triggers. Prefer a specific delegate, and
[describe your driver](../configuration/reference.md#describing-a-driver-quartz-does-not-know) rather
than fall back to the generic dialect.
:::

For a very busy scheduler, nearly always running as many jobs as the thread pool allows, set the data
source's maximum pool size to about `ThreadPool:MaxConcurrency` plus one. That is an ADO.NET
connection-string setting, not a Quartz one.

Every store setting is on `AdoJobStoreOptions`, set through `store.ConfigureStore(...)` or bound from the
`Quartz:JobStore` configuration section. See the
[configuration reference](../configuration/reference.md#persistent-job-store).

### Creating the schema

`ProvisionSchema()` makes the store create its own tables:

<!-- snippet: sample_job_stores_provision_schema -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UsePostgres(connectionString);

        // outside production, where whatever applies the rest of the database's
        // schema applies this one too
        if (builder.Environment.IsDevelopment())
        {
            store.ProvisionSchema();
        }
    });
});
```
<!-- endSnippet -->

The setting is `AdoJobStoreOptions.SchemaProvisioning`, bound from `Quartz:JobStore:SchemaProvisioning`,
or the flat key `quartz.jobStore.schemaProvisioning`:

| Value | What the store does as it initializes |
|---|---|
| `None` | nothing; a missing table fails the first statement that names it |
| `Validate` | **the default**; checks the tables and the columns 4.x added, and refuses to start if one is missing |
| `CreateIfMissing` | runs the DDL for the configured database, then validates; `ProvisionSchema()` sets this |

* `Validate` issues a `SELECT 1` against the store's tables and a `SELECT <column> … WHERE 1 = 0` for each
  column 4.x added to a table 3.x already had, and names what is missing.
* `CreateIfMissing` does not run the DDL if a table it needs exists but lacks a column it needs; that
  means the schema is not 4.x's.

The DDL is an embedded set of scripts, one per dialect, written for an ADO.NET provider: the table prefix
is a placeholder rather than a literal `QRTZ_`, and there is no `GO`, lone `/` or `SET TERM`, because each
statement is sent on its own. The build and the integration tests check that it creates the same tables,
columns and indexes as `database/tables/`. See
[`database/README.md`](https://github.com/quartznet/quartznet/blob/main/database/README.md#what-the-scheduler-runs).

**It only ever creates.** Every statement is guarded; none drops or alters anything.

* It is safe against a database that already has the schema, and safe to run twice.
* It is safe in a cluster whose nodes start at once. Only one node can create a given object. A node
  whose create fails runs validation: if it passes, another node got there first. A node that arrives
  half-way through another's run fills in the gaps, and a brief retry converges the two.
* A mistyped `TablePrefix` cannot lose data, but it builds a second, empty table set under that prefix
  instead of refusing to start. Check the prefix; see
  [Shared database](../multi-tenancy.md#shared-database) for what is and is not reported.

**It is not an upgrade, and it will not start one.** A guarded `CREATE TABLE` skips an existing table
without looking inside it.

* If a table it needs exists without a column it needs, the schema is not 4.x's (in practice, 3.x's).
  The database is refused, nothing is created, and the message names the migration. Otherwise the
  scheduler would start, report itself validated, and fire nothing.
* The check is by column, not table count: a table 4.x created has every column 4.x needs, so a node can
  also finish a schema whose creator died half-way.
* Only [`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations)
  moves a schema forward, and the 3.x → 4.0 upgrade is mandatory. See
  [Database Schema Changes](../../database/schema-changes.md).

**It is not the default**, because creating tables needs a permission production databases usually do
not grant. Provision in development and tests, as in the sample, and apply the schema in production with
whatever applies the rest of yours. Without the permission, startup fails naming the fresh-install script
for that database and the setting to fall back to.

**Not every configuration can provision:**

| Configuration | Provisions |
|---|---|
| SQL Server, PostgreSQL, MySQL, Oracle, SQLite, Firebird | its dialect's script |
| `UseGenericDatabase` | nothing; `CreateIfMissing` throws as the store initializes, naming the delegate and the script to run by hand |
| a driver delegate of your own | the script named by its `StdAdoDelegate.SchemaResourceName` override, embedded in its own assembly |
| a delegate derived from a shipped dialect | the dialect's script, with no override (the lookup walks the base chain, nearest first) |
| SQL Server's [memory-optimized or pre-2016 schema](https://github.com/quartznet/quartznet/tree/main/database/tables) | the *standard* schema, which is not what you asked for |

`StdAdoDelegate` writes portable SQL, so it cannot know what DDL your database accepts. The two SQL Server
variants have no delegate of their own; run them by hand and leave `SchemaProvisioning` at `Validate`.

Validation checks every table and every column 4.x added to a table 3.x already had. An unmigrated 3.x
database is refused at startup, with a message naming the missing column and the migration script that
adds it.

### The cheapest persistent store to try

SQLite needs no database server: it is a file, its driver is one package, and the store can create its
own tables.

```shell
dotnet add package Microsoft.Data.Sqlite
```

<!-- snippet: sample_job_stores_sqlite_file -->
```csharp
builder.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        // a file beside the application; "Data Source=:memory:" would not survive a restart,
        // which is the whole point of a persistent store
        store.UseSqlite("Data Source=quartz.db");

        // let the store create the twelve tables on first start
        store.ProvisionSchema();

        store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
    });

    q.ScheduleJob<HelloJob>(trigger => trigger
        .WithIdentity("helloTrigger")
        .StartNow()
        .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromSeconds(10)).RepeatForever()));
});

// ScheduleJob declares HelloJob and its trigger on every start, and by default a declaration
// replaces what the store holds, StartNow() included. This keeps the stored trigger instead, so a
// restart carries on from the file rather than scheduling afresh.
builder.Services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

Start it, watch the job fire, stop it, and start it again: the trigger's next fire time comes back out of
`quartz.db` instead of being scheduled afresh. That is the difference from `UseInMemoryStore()`.

* The `IgnoreDuplicates` line makes this work: without it, every restart replaces the stored trigger,
  `StartNow()` included. [Persistent job stores](../packages/microsoft-di-integration.md#persistent-job-stores)
  on the DI page explains the two settings.
* `Data Source=quartz.db` is relative to the working directory. Use an absolute path if the process
  might start from somewhere else.
* It cannot [cluster](advanced-enterprise-features.md) (see the warning above).
* It is not a production store for a busy scheduler. SQLite serializes writers, so trigger acquisition,
  misfire handling and your own scheduling calls queue behind each other. Use it for a first persistent
  store, a single-node deployment, and tests; see [Testing](testing.md).

### Storing job data as strings

`StoreJobDataAsStrings` tells the store that every `JobDataMap` value is a string, so it stores them as
name-value pairs instead of serializing objects into the BLOB column. That avoids the class-versioning
problems of serializing your own types.

::: tip
This is the recommended configuration, because it greatly decreases the possibility of type serialization issues.
:::

<!-- snippet: sample_job_stores_store_job_data_as_strings -->
```csharp
store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
```
<!-- endSnippet -->

The flat key is `quartz.jobStore.useProperties`, its 3.x name.

### Choosing a serializer

Anything not stored as a string (a calendar, a trigger's own state, a job data value under
`StoreJobDataAsStrings = false`) is written through an `IObjectSerializer`. Both are JSON:

| Serializer | Package | Select with |
|---|---|---|
| System.Text.Json | built into Quartz | `store.UseSystemTextJsonSerializer()` |
| Newtonsoft.Json | [Quartz.Serialization.Newtonsoft](../packages/json-serialization.md) | `store.UseNewtonsoftJsonSerializer()` |

* **A store that names neither gets System.Text.Json**, registered as the fallback like the driver
  delegate. The argumentless `UseSystemTextJsonSerializer()` changes nothing, so the samples above omit
  it. Use `UseSystemTextJsonSerializer(json => …)` to register serializers for
  [trigger and calendar types of your own](../packages/system-text-json.md).
* Use Newtonsoft only to read data written by 3.x's Newtonsoft serializer. New applications use
  System.Text.Json.

::: warning
Binary serialization is gone. 3.x could write job data as a `BinaryFormatter` blob, and .NET has since
removed the formatter. Convert a database holding such blobs while still on 3.x, before the upgrade. See
[Migrating from binary serialization](../packages/json-serialization.md#migrating-from-binary-serialization).
:::

### Execution history in the database

A job store holds what is *scheduled*, not what *happened*. By default Quartz keeps execution history per
process, in memory, so a dashboard attached to a cluster through a shared store shows an empty History
page: no node wrote to anything it reads.

`UseExecutionHistory()` moves both feeds into the scheduler's own database, so the cluster has one
history and any node can read all of it:

<!-- snippet: sample_job_stores_execution_history -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UsePostgres(connectionString);
        store.UseExecutionHistory();
    });
});

// The bounds the history is kept under, which the store applies itself. Optional: these are
// the defaults.
builder.Services.AddQuartzExecutionHistory(options =>
{
    options.Retention = TimeSpan.FromHours(24);
    options.MaxEntriesPerScheduler = 2000;
});
```
<!-- endSnippet -->

The flat key is `quartz.jobStore.executionHistory`.

**The schema needs two tables**, `QRTZ_EXECUTION_HISTORY` and `QRTZ_MISFIRE_HISTORY`.

* A fresh install from `database/tables/` and `ProvisionSchema()` create them.
* A database created by 4.0 or 4.1 needs
  [`database/migrations/4.2/add_execution_history_<db>.sql`](../../database/schema-changes.md#version-4-2).
* A store configured this way refuses to start without them and names the script to run.
* Nothing else needs this migration; skip it if you do not call `UseExecutionHistory()`.

**The store trims the history itself**, with a sweep on its own timer:

| Sweep property | Value |
|---|---|
| interval | every `Retention / 10`, never more often than once a minute |
| batch | at most 20 batches of 1,000 rows per bound and per feed per pass, then the connection is returned |
| backlog | a pass that stopped on its budget brings the next pass forward to a minute later; the first pass that finishes restores the long interval |
| throughput | keeps up with anything short of 20,000 executions a minute (over 300 a second) |
| nodes | every node sweeps independently; deletes are idempotent, so two nodes at once do the work twice at worst |

* Bounded batches mean a store that was down for a week does not lock the table while catching up.
* Between passes the tables can hold more than `MaxEntriesPerScheduler` rows. Reads apply the age bound
  themselves; the count bound is the sweep's.
* A history write never runs inside the job's transaction or under the trigger lock. A failed write is
  logged and dropped; it never fails the firing.

**It is one scheduler's choice.** In a container with several schedulers, a
[named scheduler](../packages/multiple-schedulers.md) that does not call `UseExecutionHistory()` records
into the container's shared history store. That is the in-memory one, unless the *default* scheduler
called `UseExecutionHistory()`; then it is the default scheduler's database, where the named scheduler's
executions land under its own scheduler name and are swept. Call `UseExecutionHistory()` on each
scheduler whose history belongs in its own database.

::: tip
A mixed cluster needs no coordination. A 4.1 node cannot see these tables, and a 4.2 node that does not
call `UseExecutionHistory()` neither writes nor reads them; it keeps its in-memory history. Nodes that do
call it share one history. Every row carries the instance id that produced it, which the dashboard's node
filter reads.
:::

Differences from the in-memory history:

* A node filter compares as the database compares strings, not case-insensitively. An instance id is
  generated, not typed, and comparing it as written lets the node index answer the filter with a seek.
* The count bound is applied by the sweep, not on every read, because "the newest 2,000 rows" of a whole
  cluster's feed is not a property of one page.
* The age bound *is* applied on read, as in memory. Otherwise a scheduler that stopped running jobs, and
  so never writes again, would keep showing days-old executions.

### Joining an existing transaction

By default AdoJobStore opens its own connection and commits as soon as the scheduling operation is done.
Saving your data and scheduling the job that acts on it are then two transactions, and one can succeed
while the other fails. With `AcceptEnlistedTransactions`, the store uses a connection your application
owns, so scheduling commits with your work or not at all:

<!-- snippet: sample_job_stores_accept_enlisted_transactions -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UsePostgres(connectionString);
        store.ConfigureStore(options => options.AcceptEnlistedTransactions = true);
    });
});
```
<!-- endSnippet -->

Then hand your connection and transaction to the scheduler for the duration of a scope:

<!-- Not a compiled sample: it is written against Entity Framework Core, which this repository does not
     reference, and a NuGet dependency taken purely for a documentation sample is not worth it. -->

```csharp
await using var tx = await dbContext.Database.BeginTransactionAsync();

dbContext.Add(entity);
await dbContext.SaveChangesAsync();

using (scheduler.EnlistTransaction(tx.GetDbTransaction()))
{
    await scheduler.ScheduleJob(job, trigger);
    await tx.CommitAsync();
}
```

Any `DbConnection` and `DbTransaction` work, from EF Core, Dapper or plain ADO.NET.

::: warning
Handing over a connection is the only way to take part. An ambient `TransactionScope` on its own is
**not** enough: a connection the job store opens for itself is kept out of it, so scheduling would commit
separately. Open the connection inside the scope and enlist that one.
:::

Inside a `TransactionScope` the shape is the same, but the connection carries the transaction:

<!-- Not a compiled sample: it names Npgsql, which this repository does not reference, and naming the
     provider is the point — a `DbConnection` would not say which one cannot promote a transaction. -->

```csharp
var options = new TransactionOptions { IsolationLevel = IsolationLevel.ReadCommitted };
using var scope = new TransactionScope(TransactionScopeOption.Required, options, TransactionScopeAsyncFlowOption.Enabled);

using var connection = new NpgsqlConnection(connectionString);
await connection.OpenAsync();

// ... your own work on this connection ...

using (scheduler.EnlistConnection(connection))
{
    await scheduler.ScheduleJob(job, trigger);
}

scope.Complete();
```

Sharing one connection also keeps the transaction from being promoted to a distributed one, which is
unavailable outside Windows and unsupported by providers such as Npgsql.

Before you enable this:

* The enlistment flows with the current asynchronous context. Establish it in the same scope as the
  scheduler calls it covers, as `TransactionScope` needs `TransactionScopeAsyncFlowOption.Enabled`.
  Enlisting inside an `async` helper does not carry back out to the caller.
* The job store takes its locks in your transaction, so they are released only when you commit or roll
  back. Keep enlisted transactions short: a long one blocks trigger acquisition, the misfire handler and
  cluster check-in. Starting a scheduler for the first time inside an enlistment scope is refused, with a
  message; resuming one from standby is not refused, but avoid it too.
* With your own `DbTransaction`, dispose the enlistment scope after committing. That is when a pending
  scheduling change is signalled to the scheduler; earlier, it would point at rows it cannot see yet.
  Under a `TransactionScope` the scope reports the outcome, so the enlistment can close first (as in the
  sample above), and nothing is signalled if the transaction rolls back.
* Await scheduler calls one at a time inside a scope. A connection carries one transaction and cannot
  serve two operations at once.
* Automatic retries of transient database errors are skipped inside your transaction. On most providers
  the first failure has already doomed it, so handle the error yourself, including your own work in that
  transaction.
* Because your transaction outlives the scheduling operation, this mode uses database locks even when the
  scheduler is not clustered; an in-process lock would be released before you commit. SQLite is the
  exception: it always locks in process, so a concurrent scheduler operation can fail with "database is
  locked" until your transaction completes. Quartz logs a warning about it at startup.
* On SQLite, enlist a `DbTransaction`, not a `TransactionScope`. `Microsoft.Data.Sqlite` implements no
  `DbConnection.EnlistTransaction`, so a connection opened inside a scope never joins it.
  `EnlistConnection` refuses such a connection with a `SchedulerException` that names the driver:

  ```text
  Scheduler 'MyScheduler' cannot take part in the ambient transaction through a Microsoft.Data.Sqlite.SqliteConnection
  (Microsoft.Data.Sqlite): the driver implements no DbConnection.EnlistTransaction, so the connection never joined the
  TransactionScope and every statement the job store issued on it would commit on the spot - a scope that rolled back
  would leave the schedule behind. Begin a transaction on the connection and enlist that instead:
  scheduler.EnlistTransaction(connection.BeginTransaction()).
  ```

  `EnlistTransaction(connection.BeginTransaction())` works, because the transaction is the connection's
  own and Quartz uses it directly. Whether a driver can join an ambient transaction depends on the
  driver; the other five can. Quartz asks every connection to join, so an unknown provider is held to the
  same check.
* An operation that fails half-way leaves its statements in your transaction; there is no savepoint to
  roll back to.
* Work the scheduler does on its own (acquiring triggers, handling misfires, cluster check-in) always uses
  its own connections and is unaffected.
* `ExternalTransactionJobStore` is the exception to the previous point: running inside a
  container-managed transaction is its whole contract, so its own connections enlist in an ambient
  transaction as they always have.

## Writing your own job store

| You want to | Do |
|---|---|
| add behaviour around an existing store: logging, metrics, tenant routing, fault injection | derive from `Quartz.Impl.DelegatingJobStore` |
| store scheduling data somewhere new | implement `IJobStore` directly |

`DelegatingJobStore` takes the store to wrap as its constructor argument and forwards every `IJobStore`
member to it, so you override only what you change. The wrapped store is `InnerJobStore`.

<!-- snippet: sample_job_stores_delegating_store -->
```csharp
public sealed class LoggingJobStore : DelegatingJobStore
{
    private readonly ILogger<LoggingJobStore> logger;

    public LoggingJobStore(
        ILoggerFactory loggerFactory,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider,
        ILogger<LoggingJobStore> logger)
        : base(new RAMJobStore(loggerFactory, signaler, timeProvider))
    {
        this.logger = logger;
    }

    public override async ValueTask ScheduleJob(
        IJobDetail job,
        IOperableTrigger trigger,
        CancellationToken cancellationToken = default)
    {
        await base.ScheduleJob(job, trigger, cancellationToken: cancellationToken);
        logger.LogInformation("Scheduled {JobKey} on {TriggerKey}", job.Key, trigger.Key);
    }
}
```
<!-- endSnippet -->

* None of the shipped stores can be derived from (`RAMJobStore` is sealed, the ADO.NET stores are
  internal). To build on one, construct it and pass it to the base constructor, as above.
* Your store's constructor arguments come from the container, so it can take whatever else it needs.
  `Quartz.Examples.AspNetCore`'s `CustomJobStore` has the same shape.

`IJobStore` is a large interface with real concurrency requirements: trigger acquisition must be atomic
against other scheduler instances, and misfire handling must be idempotent. Start from
[A Job Store of Your Own](../how-tos/custom-job-store.md), which writes out the contract.

A store that keeps job details as objects rather than rows should re-store the data of a
`[PersistJobDataAfterExecution]` job with `jobDetail.WithJobData(newData)`, not by rebuilding the detail
through `JobBuilder`. An application may supply an
[`IJobDetail` of its own](more-about-jobs.md#a-jobdetail-of-your-own), and rebuilding silently replaces
it with Quartz's implementation on the job's first completion.

Either kind is registered by type:

<!-- snippet: sample_job_stores_registering_your_own -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore<LoggingJobStore>(options =>
    {
        // … store options
    });
});
```
<!-- endSnippet -->
