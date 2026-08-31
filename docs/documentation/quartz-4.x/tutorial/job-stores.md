---

title: 'Job Stores'
---

# Job Stores

JobStore's are responsible for keeping track of all the "work data" that you give to the scheduler:
jobs, triggers, calendars, etc. Selecting the appropriate `IJobStore` implementation for your Quartz scheduler instance is an important step.
Luckily, the choice should be a very easy one once you understand the differences between them.
You declare which JobStore your scheduler should use, and how it is configured, where you configure the
scheduler itself — `q.UseInMemoryStore()` or `q.UsePersistentStore(…)` inside `AddQuartz`.

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

<!-- snippet: sample_job_stores_in_memory -->
```csharp
builder.Services.AddQuartz(q =>
{
    // this is the default, so the call is only needed to change one of its settings
    q.UseInMemoryStore(options => options.MisfireThreshold = TimeSpan.FromSeconds(30));
});
```
<!-- endSnippet -->

To use `RAMJobStore` you don't need to do anything special. Default configuration
of Quartz.NET uses `RAMJobStore` as job store implementation.

## ADO.NET Job Store (AdoJobStore)

AdoJobStore is also aptly named - it keeps all of its data in a database via ADO.NET.
Because of this it is a bit more complicated to configure than `RAMJobStore`, and it also is not as fast.
However, the performance draw-back is not terribly bad, especially if you build the database tables with indexes on the primary keys.

AdoJobStore needs its tables to exist before it will start, and there are two ways to get them there. You
can have the store create them for you — [Creating the schema](#creating-the-schema) below — or you can
run the DDL yourself, which is what a production database usually wants.
You can find table-creation SQL scripts in the "[database/tables](https://github.com/quartznet/quartznet/tree/main/database/tables)" directory of the Quartz.NET distribution.
Each script drops an existing Quartz schema before recreating it, so read its header first — it says how to decline that if you are running it against a database you care about.
If there is not already a script for your database type, just look at one of the existing ones, and modify it in any way necessary for your DB.
One thing to note is that in these scripts, all the the tables start with the prefix `QRTZ_`
(such as the tables `QRTZ_TRIGGERS`, and `QRTZ_JOB_DETAIL`). This prefix can actually be anything you'd like, as long as you inform AdoJobStore
what the prefix is (in your Quartz.NET properties). Using different prefixes may be useful for creating multiple sets of tables,
for multiple scheduler instances, within the same database.

`LocalTransactionJobStore` creates transactions by itself and is the implementation you normally want — it is
what `UsePersistentStore` registers. If you need scheduling to commit together with your application's own
database work, it can also be told to use a connection you own; see
[Joining an existing transaction](#joining-an-existing-transaction) below. `ExternalTransactionJobStore` is the
other one, for a container that manages the ambient transaction itself.

### Configuring a persistent store

Everything the store needs is one call. Naming the database also selects the driver delegate that speaks its
SQL dialect and the ADO.NET provider that talks to it, so a connection string is usually all you supply:

<!-- snippet: sample_job_stores_persistent -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UseSqlServer("Server=localhost;Database=quartz;Trusted_Connection=True;Encrypt=False");
        store.UseSystemTextJsonSerializer();

        store.ConfigureStore(options =>
        {
            options.TablePrefix = "QRTZ_";
            options.StoreJobDataAsStrings = true;
        });
    });
});
```
<!-- endSnippet -->

One method per database:

| Method | Database | Driver |
|---|---|---|
| `UseSqlServer` | Microsoft SQL Server | Microsoft.Data.SqlClient |
| `UsePostgres` | PostgreSQL | Npgsql |
| `UseMySql` | MySQL | MySql.Data |
| `UseMySqlConnector` | MySQL | MySqlConnector |
| `UseOracle` | Oracle | Oracle's managed driver |
| `UseFirebird` | Firebird | FirebirdSql.Data.FirebirdClient |
| `UseSqlite` | SQLite | Microsoft.Data.Sqlite |
| `UseSystemDataSqlite` | SQLite | the legacy System.Data.SQLite |
| `UseGenericDatabase` | anything else, using the generic SQL dialect | one you describe |

The driver package itself is yours to reference: Quartz names the types, your project brings them. Each method
also takes a callback over `DataSourceOptions` instead of a connection string, for a named connection string
(`db => db.ConnectionStringName = "Scheduler"`) or for connecting through a `DbDataSource` the container already
holds (`db => db.UseRegisteredDataSource = true`).

::: warning
SQLite cannot take part in a [cluster](advanced-enterprise-features.md): it locks in process rather than in the
database, so the row locks a cluster coordinates through do not hold between nodes. Configuring `UseSqlite` or
`UseSystemDataSqlite` together with `UseClustering()` therefore fails as the store initializes, with a
`Quartz.Impl.AdoJobStore.InvalidConfigurationException` — a startup failure, not something that surfaces later
under load.
:::

::: tip
`UseGenericDatabase` uses `StdAdoDelegate`, which writes portable SQL and therefore cannot limit a result set —
it reads every candidate trigger and discards the surplus in memory. The database-specific delegates page their
queries (`TOP n`, `LIMIT n`, `FETCH FIRST n ROWS ONLY`), which is the difference that shows up once a scheduler
has a lot of triggers. Prefer a specific one, and
[describe your driver](../configuration/reference.md#describing-a-driver-quartz-does-not-know) rather than
falling back to the generic dialect if you can.
:::

If your scheduler is very busy — nearly always executing as many jobs as the thread pool allows — set the
maximum pool size of the data source to around `ThreadPool:MaxConcurrency` plus one. That is a setting of the
ADO.NET connection string, not of Quartz.

Every setting of the store is on `AdoJobStoreOptions`, reached through `store.ConfigureStore(...)` or bound from the
`Quartz:JobStore` configuration section; they are all tabulated in the
[configuration reference](../configuration/reference.md#persistent-job-store).

### Creating the schema

A store can create its own tables rather than being handed them. `ProvisionSchema()` asks for it:

<!-- snippet: sample_job_stores_provision_schema -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store =>
    {
        store.UsePostgres(connectionString);
        store.UseSystemTextJsonSerializer();

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

It is one setting with three positions — `AdoJobStoreOptions.SchemaProvisioning`, bound from
`Quartz:JobStore:SchemaProvisioning`, or the flat key `quartz.jobStore.schemaProvisioning`:

| Value | What the store does as it initializes |
|---|---|
| `None` | Nothing. A missing table surfaces as the first statement that names it, whenever that happens to run. |
| `Validate` | **The default.** Issues a `SELECT 1` against the store's tables and refuses to start if one is missing, naming it. |
| `CreateIfMissing` | Runs the DDL for the configured database first, then validates. `ProvisionSchema()` sets this. |

What it runs is not the script you would run by hand. Quartz embeds a second set, one per dialect,
written for an ADO.NET provider rather than for a command-line client — the table prefix is a
placeholder rather than a literal `QRTZ_`, and there is no `GO`, no lone `/` and no `SET TERM`, because
each statement is sent on its own. The build parses the two sets with one parser and compares the
tables, columns and indexes they name, and the integration tests provision a real database of every
dialect and compare its catalog with a fresh install's — so the tables a scheduler creates for itself
are the tables `database/tables/` creates.
[`database/README.md`](https://github.com/quartznet/quartznet/blob/main/database/README.md#what-the-scheduler-runs)
has the detail.

Four things are worth knowing before you turn it on.

**It only ever creates.** Every statement in those scripts is guarded and none of them drops or alters
anything, so it is safe against a database that already has the schema, and safe to run twice. It is
also safe under a cluster whose nodes all start at once: only one of them can create any given object,
and a node whose create fails asks the schema rather than the error whether it lost the race — a
validation that passes means another node got there first. Because the script is guarded throughout, a
node that arrives while another is half-way through fills in the gaps rather than waiting, and a brief
retry converges the two. It cannot turn a mis-typed `TablePrefix` into data loss either — but it will
cheerfully build a second, empty table set under the mis-typed prefix, where refusing to start would
have told you sooner. Read the prefix twice, and see
[Shared database](../multi-tenancy.md#shared-database) for what is and is not reported.

**It is not an upgrade.** A schema that has every table but is missing a column a later release added is
left exactly as it is, because a guarded `CREATE TABLE` skips a table that exists without looking inside
it. Moving a schema forward is
[`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations) and
nothing else, and the 3.x → 4.0 upgrade is still mandatory — see
[Database Schema Changes](../../database/schema-changes.md).

**It is not the default**, because creating tables needs a permission a production database is usually
right not to grant. That is the case for the shape in the sample above: provision in development and in
tests, where a database is disposable and nobody wants a DDL step in the way, and let whatever applies
the rest of your schema apply this one in production. If the account has no such permission, startup
fails naming the fresh-install script for that database and the setting to drop back to.

**Not every configuration can provision, and one of them can provision the wrong thing.** The six
databases with a dialect of their own — SQL Server, PostgreSQL, MySQL, Oracle, SQLite and Firebird —
each carry a script. `UseGenericDatabase` does not, because `StdAdoDelegate` writes portable SQL and
cannot know what DDL your database accepts; asking it for `CreateIfMissing` throws as the store
initializes, naming the delegate and the script to run by hand, rather than quietly creating nothing. A
driver delegate of your own joins in by overriding `StdAdoDelegate.SchemaResourceName` with the name of
a script embedded in its own assembly. The case to watch is SQL Server's two variant schemas, the
[memory-optimized one and the pre-2016 one](https://github.com/quartznet/quartznet/tree/main/database/tables):
they have no delegate of their own, so a store configured for either still provisions the *standard*
schema, which is not what you asked for. Both are deliberate departures a person chose for a particular
deployment. Run those by hand and leave `SchemaProvisioning` at `Validate`.

Validation, whichever position you leave the setting at, checks *tables* — all twelve of them, and not
their columns. A database missing only a column gets past startup and fails on the first statement that
names it, which is the other half of why the 4.0 migration is mandatory rather than merely recommended.

### Storing job data as strings

`StoreJobDataAsStrings` instructs the store that all values in JobDataMaps will be strings, and therefore can be
stored as name-value pairs, rather than storing more complex objects in their serialized form in the BLOB column.
This is much safer in the long term, as you avoid the class versioning issues that come with serializing your own
types into a BLOB.

::: tip
This is the recommended configuration, because it greatly decreases the possibility of type serialization issues.
:::

<!-- snippet: sample_job_stores_store_job_data_as_strings -->
```csharp
store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
```
<!-- endSnippet -->

The flat key for the same setting is `quartz.jobStore.useProperties`, which is the name it had in 3.x.

### Choosing a serializer

Whatever is not stored as a string — a calendar, a trigger's own state, a job data value under
`StoreJobDataAsStrings = false` — is written through an `IObjectSerializer`, and a store has to be told which
one. There are two, both JSON:

* System.Text.Json, built into Quartz: `store.UseSystemTextJsonSerializer()`
* Newtonsoft.Json, from the separate
  [Quartz.Serialization.Newtonsoft](../packages/json-serialization.md) package:
  `store.UseNewtonsoftJsonSerializer()`

Reach for the Newtonsoft one only when you have data written by 3.x's Newtonsoft serializer, whose format it
reads. New applications want System.Text.Json.

::: warning
Binary serialization is gone. 3.x could write job data as a `BinaryFormatter` blob, and .NET has since removed
the formatter itself. A database that holds such blobs has to be converted while still on 3.x, before the
upgrade — see
[Migrating from binary serialization](../packages/json-serialization.md#migrating-from-binary-serialization).
:::

### Joining an existing transaction

Normally AdoJobStore opens a connection of its own and commits as soon as the scheduling operation is done. That means
saving your own data and scheduling the job that acts on it are two separate transactions, and one can succeed while the
other fails.

Telling the store to accept enlisted transactions lets it use a connection your application already owns instead, so
scheduling commits with the rest of your work or not at all.

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

You then hand your connection and transaction to the scheduler for the duration of a scope:

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

Nothing about this is specific to Entity Framework Core - any `DbConnection` and `DbTransaction` will do, whether they
come from EF Core, Dapper or plain ADO.NET.

::: warning
Handing over a connection is the only way to take part. An ambient `TransactionScope` on its own is **not** enough: a
connection the job store opens for itself is deliberately kept out of it, so that scheduling would commit separately.
Open the connection inside the scope and enlist that one.
:::

Inside a `TransactionScope` the shape is the same, except that the connection carries the transaction for you:

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

None of the stores Quartz ships can be derived from - `RAMJobStore` is sealed and the ADO.NET stores are
internal - so this is also how you build on one: construct it and hand it to the base constructor, as above. Your store's own constructor arguments come from the container, so it can
take whatever else it needs. `Quartz.Examples.AspNetCore`'s `CustomJobStore` shows the same shape.

**Storing scheduling data somewhere new** implements `IJobStore` directly. It is a large interface with real
concurrency requirements - trigger acquisition has to be atomic against other scheduler instances, and misfire
handling has to be idempotent - so start from [A Job Store of Your Own](../how-tos/custom-job-store.md), which
writes the contract out, rather than from the method signatures alone.

A store that keeps job details as objects rather than as rows should re-store the data of a
`[PersistJobDataAfterExecution]` job with `jobDetail.WithJobData(newData)`, not by rebuilding the detail through
`JobBuilder`. An application may have supplied an [`IJobDetail` of its own](more-about-jobs.md#a-jobdetail-of-your-own),
and rebuilding one silently swaps it for Quartz's implementation on the job's first completion.

Either kind is registered the same way, by type:

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
