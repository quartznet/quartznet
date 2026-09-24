---

title: 'Job Stores'
---

# Job Stores

A job store keeps track of the scheduler's work data: jobs, triggers, calendars and so on. Choose the `IJobStore` implementation, and its settings, in the properties file (or object) you give the SchedulerFactory.

::: warning
Never use a JobStore instance directly in your code. Quartz uses it behind the scenes: configure which JobStore to use, then work only with the Scheduler interface.
:::

## RAMJobStore

`RAMJobStore` keeps all its data in RAM. It is the simplest job store to configure and the fastest (in CPU time). When your application ends or crashes, all scheduling information is lost, so RAMJobStore cannot honor "non-volatility" on jobs and triggers. For some applications that is acceptable or even desired; for others it is disastrous.

**Configuring Quartz to use RAMJobStore**

```text
 // this is actually the default, so you don't need to explicitly set this
 quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
```

`RAMJobStore` is the default with `StdSchedulerFactory`, so nothing needs configuring.

## ADO.NET Job Store (AdoJobStore)

AdoJobStore keeps all its data in a database via ADO.NET. It is harder to configure than `RAMJobStore` and slower, though not by much if the tables have indexes on the primary keys.

1. Create the Quartz.NET tables. Table-creation SQL scripts are in the [database/tables](https://github.com/quartznet/quartznet/tree/main/database/tables) directory of the Quartz.NET distribution. If there is no script for your database, adapt an existing one.
2. Configure the job store type, driver delegate, table prefix and data source, as below.

The scripts prefix every table with `QRTZ_` (such as `QRTZ_TRIGGERS` and `QRTZ_JOB_DETAIL`). The prefix can be anything, as long as you tell AdoJobStore what it is. Different prefixes let several sets of tables, for several scheduler instances, share one database.

`JobStoreTX` creates its own transactions and is the implementation you normally want. To commit scheduling together with your application's own database work, `JobStoreTX` can also join a transaction you own: see [Joining an existing transaction](#joining-an-existing-transaction).

A data source, defined in your Quartz.NET properties, supplies AdoJobStore's database connections. It holds the connection string and ADO.NET delegate information.

### Configuring Quartz to use JobStoreTx

```text
    quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz
```

Next, select the `IDriverDelegate` implementation, which does the ADO.NET work for your specific database. `StdAdoDelegate` uses "vanilla" ADO.NET code and SQL. Use it only if there is no delegate for your database: specific delegates usually perform better or work around database-specific issues. Other delegates are in the `Quartz.Impl.AdoJobStore` namespace or its sub-namespaces.

::: tip
Quartz.NET warns if you use the default StdAdoDelegate, because it performs poorly with many triggers to select from. Specific delegates have SQL that limits result set length (SqlServerDelegate uses `TOP n`, PostgreSQLDelegate `LIMIT n`, OracleDelegate `ROWCOUNT() <= n` etc.).
:::

Set the delegate's class name:

**Configuring AdoJobStore to use a DriverDelegate**

```text
    quartz.jobStore.driverDelegateType = Quartz.Impl.AdoJobStore.StdAdoDelegate, Quartz
```

Set the table prefix:

**Configuring AdoJobStore with the Table Prefix**

```text
    quartz.jobStore.tablePrefix = QRTZ_
```

Set the data source to use. It must also be defined in your Quartz properties; here it is "myDS":

**Configuring AdoJobStore with the name of the data source to use**

```text
    quartz.jobStore.dataSource = myDS
```

Set the data source's connection string and database provider. The connection string is the driver's standard ADO.NET connection string. The provider abstracts the database driver, so Quartz is loosely coupled to it.

**Setting Data Source's Connection String And Database Provider**

```text
     quartz.dataSource.myDS.connectionString = Server=localhost;Database=quartz;Uid=quartznet;Pwd=quartznet
     quartz.dataSource.myDS.provider = MySql
```

Supported database providers:

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
The community contributes many other providers, for example for NoSQL databases. The Quartz.NET project does not support them.
:::

**Use the latest driver version when one is available; add an assembly binding redirect.**

If your scheduler is very busy (nearly always running as many jobs as the thread pool size), set the data source's connection count to about the thread pool size + 1. This is usually set in the ADO.NET connection string; see your driver's documentation.

`quartz.jobStore.useProperties` set to "true" (default false) tells AdoJobStore that all JobDataMap values are strings. They are then stored as name-value pairs instead of serialized objects in the BLOB column. This is safer in the long term, because it avoids the class versioning issues of serializing non-String classes into a BLOB.

### Configuring AdoJobStore to use strings as JobDataMap values

::: tip
Recommended: it greatly reduces the risk of type serialization issues.
:::

```text
    quartz.jobStore.useProperties = true
```

### Choosing a serializer

Quartz.NET supports binary and JSON serialization. Binary serialization is discouraged: future versions will not support it.

 * JSON serialization based on System.Text.Json: the [Quartz.Serialization.SystemTextJson](../packages/system-text-json) NuGet package
 * JSON serialization based on Newtonsoft.Json: the [Quartz.Serialization.Json](../packages/json-serialization) NuGet package

 ::: tip
 JSON is the recommended persistent format for greenfield projects.
 Also strongly consider setting useProperties to true, to restrict key-values to strings.
 :::

#### Using code

```csharp
var config = SchedulerBuilder.Create();
config.UsePersistentStore(store =>
{
    // it's generally recommended to stick with
    // string property keys and values when serializing
    store.UseProperties = true;

    ....

    store.UseSystemTextJsonSerializer();
});
ISchedulerFactory schedulerFactory = config.Build();
```

#### Using properties

```csharp
    // "stj" is an alias for "Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson"
    // "newtonsoft" and "json" are aliases for "Quartz.Simpl.JsonObjectSerializer, Quartz.Serialization.Json"
    quartz.serializer.type = stj
```

### Joining an existing transaction

By default AdoJobStore opens its own connection and commits as soon as the scheduling operation is done. Saving your data and scheduling the job that acts on it are then two transactions, and one can succeed while the other fails.

`quartz.jobStore.acceptEnlistedTransactions` set to `true` lets the job store join a transaction your application owns, so scheduling commits with the rest of your work or not at all.

```csharp
var config = SchedulerBuilder.Create();
config.UsePersistentStore(store =>
{
    store.UsePostgres(connectionString);
    store.AcceptEnlistedTransactions();
});
```

Then hand your connection and transaction to the scheduler for the duration of a scope:

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

Any `DbConnection` and `DbTransaction` works, whether from EF Core, Dapper or plain ADO.NET.

::: warning
Handing over a connection is the only way to take part. An ambient `TransactionScope` alone is **not** enough: a connection the job store opens for itself is kept out of it on purpose, so scheduling would commit separately. Open the connection inside the scope and enlist that one.
:::

Inside a `TransactionScope` the shape is the same, except that the connection carries the transaction:

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

Sharing one connection also keeps the transaction from being promoted to a distributed one, which is unavailable outside Windows and unsupported by providers such as Npgsql.

Before you enable this:

* **The enlistment flows with the current asynchronous context.** Establish it in the same scope as the scheduler calls it covers, for the same reason `TransactionScope` needs `TransactionScopeAsyncFlowOption.Enabled`. Enlisting inside an `async` helper does not carry the enlistment back to the caller.
* **Locks are held until you commit or roll back**, because the job store takes them in your transaction. Keep enlisted transactions short: a long one blocks trigger acquisition, the misfire handler and cluster check-in. For the same reason, starting a scheduler for the first time inside an enlistment scope is refused with an error. Resuming one from standby is not refused, so avoid that too.
* **With your own `DbTransaction`, dispose the enlistment scope after committing.** Disposal signals a pending scheduling change to the scheduler; earlier, it would point the scheduler at rows it cannot see yet. Under a `TransactionScope` the scope reports the outcome, so the enlistment can close first (as in the sample above), and nothing is signalled if the transaction rolls back.
* **Await scheduler calls one at a time inside a scope.** A connection carries one transaction and cannot serve two operations at once.
* **Transient database errors are not retried inside your transaction.** On most providers the first failure has already doomed it, so the error, and any of your own work in that transaction, is yours to handle.
* **An operation that fails halfway leaves its statements in your transaction**; there is no savepoint to roll back to.
* **This mode uses database locks even when the scheduler is not clustered**, because your transaction outlives the scheduling operation and an in-process lock would be released before you commit. SQLite is the exception: it always locks in process, so a concurrent scheduler operation can fail with "database is locked" until your transaction completes. Quartz logs a warning about it at startup.
* **The scheduler's own work** (acquiring triggers, handling misfires, cluster check-in) always uses its own connections and is unaffected.
* **`JobStoreCMT` is the exception** to the previous point: running inside a container-managed transaction is that store's contract, so its own connections enlist in an ambient transaction as they always have.
