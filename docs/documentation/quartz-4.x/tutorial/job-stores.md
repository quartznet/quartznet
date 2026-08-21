---

title: 'Job Stores'
---

# Job Stores

JobStore's are responsible for keeping track of all the "work data" that you give to the scheduler:
jobs, triggers, calendars, etc. Selecting the appropriate `IJobStore` implementation for your Quartz scheduler instance is an important step.
Luckily, the choice should be a very easy one once you understand the differences between them.
You declare which JobStore your scheduler should use (and it's configuration settings) in the properties file (or object) that
you provide to the SchedulerFactory that you use to produce your scheduler instance.

::: warning
Never use a JobStore instance directly in your code. For some reason many people attempt to do this.
The JobStore is for behind-the-scenes use of Quartz itself. You have to tell Quartz (through configuration) which JobStore to use,
but then you should only work with the Scheduler interface in your code.
:::

## RAMJobStore

`RAMJobStore` is the simplest JobStore to use, it is also the most performant (in terms of CPU time).
`RAMJobStore` gets its name in the obvious way: it keeps all of its data in RAM. This is why it's lightning-fast,
and also why it's so simple to configure. The drawback is that when your application ends (or crashes) all of
the scheduling information is lost - this means RAMJobStore cannot honor the setting of "non-volatility" on jobs and triggers.
For some applications this is acceptable - or even the desired behavior, but for other applications, this may be disastrous.

**Configuring Quartz to use RAMJobStore**

```text
 // this is actually the default, so you don't need to explicitly set this
 quartz.jobStore.type = Quartz.Impl.RAMJobStore, Quartz
```

To use `RAMJobStore` you don't need to do anything special. Default configuration
of Quartz.NET uses `RAMJobStore` as job store implementation.

## ADO.NET Job Store (AdoJobStore)

AdoJobStore is also aptly named - it keeps all of its data in a database via ADO.NET.
Because of this it is a bit more complicated to configure than `RAMJobStore`, and it also is not as fast.
However, the performance draw-back is not terribly bad, especially if you build the database tables with indexes on the primary keys.

To use AdoJobStore, you must first create a set of database tables for Quartz.NET to use.
You can find table-creation SQL scripts in the "[database/tables](https://github.com/quartznet/quartznet/tree/main/database/tables)" directory of the Quartz.NET distribution.
If there is not already a script for your database type, just look at one of the existing ones, and modify it in any way necessary for your DB.
One thing to note is that in these scripts, all the the tables start with the prefix `QRTZ_`
(such as the tables `QRTZ_TRIGGERS`, and `QRTZ_JOB_DETAIL`). This prefix can actually be anything you'd like, as long as you inform AdoJobStore
what the prefix is (in your Quartz.NET properties). Using different prefixes may be useful for creating multiple sets of tables,
for multiple scheduler instances, within the same database.

`LocalTransactionJobStore` creates transactions by itself and is the implementation you normally want. If you need
scheduling to commit together with your application's own database work, `LocalTransactionJobStore` can also be told to
use a connection you own - see [Joining an existing transaction](#joining-an-existing-transaction) below.

The last piece of the puzzle is setting up a data source from which AdoJobStore can get connections to your database.
Data sources are defined in your Quartz.NET properties. Data source information contains the connection string
and ADO.NET delegate information.

### Configuring Quartz to use LocalTransactionJobStore

```text
    quartz.jobStore.type = Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz
```

Next, you need to select a `IDriverDelegate` implementation for the JobStore to use.
The DriverDelegate is responsible for doing any ADO.NET work that may be needed for your specific database.
`StdAdoDelegate` is a delegate that uses "vanilla" ADO.NET code (and SQL statements) to do its work.
If there isn't another delegate made specifically for your database, try using this delegate -
special delegates usually have better performance or workarounds for database specific issues.
Other delegates can be found in the `Quartz.Impl.AdoJobStore` namespace, or in its sub-namespaces.

::: tip
Quartz.NET will issue warning if you are using the default StdAdoDelegate as it has poor performance
when you have a lot of triggers to select from. Specific delegates have special SQL to limit result
set length (SqlServerDelegate uses `TOP n`, PostgreSQLDelegate `LIMIT n`, OracleDelegate `ROWCOUNT() <= n` etc.).
:::

Once you've selected your delegate, set its class name as the delegate for AdoJobStore to use.

**Configuring AdoJobStore to use a DriverDelegate**

```text
    quartz.jobStore.driverDelegateType = Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz
```

Next, you need to inform the JobStore what table prefix (discussed above) you are using.

**Configuring AdoJobStore with the Table Prefix**

```text
    quartz.jobStore.tablePrefix = QRTZ_
```

And finally, you need to set which data source should be used by the JobStore. The named data source must also be defined in your Quartz properties.
In this case, we're specifying that Quartz should use the data source name "myDS" (that is defined elsewhere in the configuration properties).

**Configuring AdoJobStore with the name of the data source to use**

```text
    quartz.jobStore.dataSource = myDS
```

One last thing that is needed for the configuration is to set data source connection string information and database provider. Connection
string is the standard ADO.NET connection which is driver specific. Database provider is an abstraction of database drivers to create
loose coupling between database drivers and Quartz.

**Setting Data Source's Connection String And Database Provider**

```text
     quartz.dataSource.myDS.connectionString = Server=localhost;Database=quartz;Uid=quartznet;Pwd=quartznet
     quartz.dataSource.myDS.provider = MySql
```

Currently following database providers are supported:

* `SqlServer` - SQL Server driver
    * For full framework this is by default System.Data.SqlClient (except in Quartz 3.1)
    * From Quartz 3.2 onwards for .NET Core this is by default Microsoft.Data.SqlClient
* `SystemDataSqlClient` - Available separately on .NET Core (default for full framework)
* `MicrosoftDataSqlClient` - Available separately on full framework (default for .NET Core)
* `OracleODP` - Oracle's Oracle Driver
* `OracleODPManaged` - Oracle's managed driver for Oracle 11
* `MySql` - MySQL Connector/.NET
* `SQLite` - SQLite ADO.NET Provider
* `SQLite-Microsoft` - Microsoft SQLite ADO.NET Provider
* `Firebird` - Firebird ADO.NET Provider
* `Npgsql` - PostgreSQL Npgsql

::: tip
There are many community contributed providers, like for NoSQL databases.

They are not supported by Quartz.NET project though.
:::

**You can and should use latest version of driver if newer is available, just create an assembly binding redirect**

If your Scheduler is very busy (i.e. nearly always executing the same number of jobs as the size of the thread pool, then you should
probably set the number of connections in the data source to be the about the size of the thread pool + 1. This is commonly configured
in the ADO.NET connection string - see your driver implementation for details.

The `quartz.jobStore.useProperties` config parameter can be set to "true" (defaults to false) in order to instruct AdoJobStore that all values in JobDataMaps will be strings,
and therefore can be stored as name-value pairs, rather than storing more complex objects in their serialized form in the BLOB column. This is much safer in the long term,
as you avoid the class versioning issues that there are with serializing your non-String classes into a BLOB.

### Configuring AdoJobStore to use strings as JobDataMap values

::: tip
This is recommended configuration because it greatly decreases the possibility of type serialization issues.
:::

```text
    quartz.jobStore.useProperties = true
```

### Choosing a serializer

Quartz.NET supports both binary and JSON serialization. Using binary serialization is discouraged as it will no longer be supported in future versions.

* JSON serialization based on System.Text.Json comes bundled with Quartz
* JSON serialization based on Newtonsoft.Json comes from separate [Quartz.Serialization.Newtonsoft](../packages/json-serialization) NuGet package

::: tip
JSON is recommended persistent format to store data in database for greenfield projects.
You should also strongly consider setting useProperties to true to restrict key-values to be strings.
:::

#### Using code

```csharp
var builder = QuartzSchedulerBuilder.Create();
builder.UsePersistentStore(store =>
{
    // it's generally recommended to stick with
    // string property keys and values when serializing
    store.Configure(options => options.StoreJobDataAsStrings = true);

    ....

    store.UseSystemTextJsonSerializer();
});
ISchedulerFactory schedulerFactory = builder.Build();
```

#### Using properties

```csharp
    // "stj" is an alias for "Quartz.Impl.SystemTextJsonObjectSerializer, Quartz"
    // "newtonsoft" is alias for "Quartz.Impl.NewtonsoftJsonObjectSerializer, Quartz.Serialization.Newtonsoft"
    quartz.serializer.type = stj
```

### Joining an existing transaction

Normally AdoJobStore opens a connection of its own and commits as soon as the scheduling operation is done. That means
saving your own data and scheduling the job that acts on it are two separate transactions, and one can succeed while the
other fails.

Telling the store to accept enlisted transactions lets it use a connection your application already owns instead, so
scheduling commits with the rest of your work or not at all.

```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UsePostgres(connectionString);
        store.Configure(options => options.AcceptEnlistedTransactions = true);
    });
});
```

You then hand your connection and transaction to the scheduler for the duration of a scope:

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

Nothing about this is specific to Entity Framework Core - any `DbConnection` and `DbTransaction` will do, whether they
come from EF Core, Dapper or plain ADO.NET.

::: warning
Handing over a connection is the only way to take part. An ambient `TransactionScope` on its own is **not** enough: a
connection the job store opens for itself is deliberately kept out of it, so that scheduling would commit separately.
Open the connection inside the scope and enlist that one.
:::

Inside a `TransactionScope` the shape is the same, except that the connection carries the transaction for you:

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

Sharing the one connection is also what keeps the transaction from having to be promoted to a distributed one, which is
unavailable outside Windows and unsupported by providers such as Npgsql.

Things worth knowing before you enable this:

* The enlistment flows with the current asynchronous context, so establish it in the same scope as the scheduler calls it
  should cover. This is the same rule that makes `TransactionScope` need `TransactionScopeAsyncFlowOption.Enabled`. In
  particular, enlisting inside an `async` helper does not carry the enlistment back out to the caller.
* The job store takes its locks in your transaction, so they are only released once you commit or roll back. Keep enlisted
  transactions short - a long running one blocks trigger acquisition, the misfire handler and cluster check-in. For the
  same reason starting a scheduler for the first time from inside an enlistment scope is refused, and says so; resuming
  one that was in standby is not, so avoid that too.
* With a `DbTransaction` of your own, dispose the enlistment scope after committing: that is when a pending scheduling
  change is signalled to the scheduler, and doing it earlier would point it at rows it cannot see yet. Under a
  `TransactionScope` the scope itself reports the outcome, so the enlistment can close first - as in the sample above -
  and nothing is signalled if the transaction rolls back.
* Await scheduler calls one at a time inside a scope. A connection carries a single transaction and cannot serve two
  operations at once.
* Automatic retries of transient database errors are skipped inside your transaction. On most providers the first failure
  has already doomed it, so the error is yours to handle - including any of your own work in that transaction.
* Because your transaction outlives the scheduling operation, this mode uses database locks even when the scheduler is not
  clustered - an in-process lock would be released before you commit. SQLite is the exception: it always locks in
  process, so a concurrent scheduler operation there can fail with "database is locked" until your transaction
  completes. Quartz logs a warning about it at startup.
* An operation that fails halfway leaves its statements in your transaction; there is no savepoint to roll back to.
* Work the scheduler does on its own - acquiring triggers, handling misfires, cluster check-in - always uses its own
  connections and is unaffected.
* `ExternalTransactionJobStore` is the exception to the previous point: running inside a transaction its container
  manages is that store's whole contract, so its own connections enlist in an ambient transaction as they always have.

## Writing your own job store

If neither bundled store fits, you can supply your own. There are two shapes, and which one you want depends on
whether you are changing *where* scheduling data lives or only what happens around it.

**Adding behaviour around an existing store** - logging, metrics, tenant routing, fault injection - derives from
`Quartz.Impl.DelegatingJobStore`. It takes the store to wrap as its constructor argument and forwards every
`IJobStore` member to it, so you override only the operations you actually change. The wrapped store is available
to your code as `InnerJobStore`.

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
        await base.ScheduleJob(job, trigger, cancellationToken);
        logger.LogInformation("Scheduled {JobKey} on {TriggerKey}", job.Key, trigger.Key);
    }
}
```

The stores Quartz ships are sealed, so this is also how you build on `RAMJobStore` - construct one and hand it
to the base constructor, as above. Your store's own constructor arguments come from the container, so it can
take whatever else it needs. `Quartz.Examples.AspNetCore`'s `CustomJobStore` shows the same shape.

**Storing scheduling data somewhere new** implements `IJobStore` directly. It is a large interface with real
concurrency requirements - trigger acquisition has to be atomic against other scheduler instances, and misfire
handling has to be idempotent - so start from the semantics `RAMJobStore` and `AdoJobStoreBase` document rather
than from the method signatures alone.

Either kind is registered the same way, by type:

```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore<LoggingJobStore>(options =>
    {
        // …store options
    });
});
```

