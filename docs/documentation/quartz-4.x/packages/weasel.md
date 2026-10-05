---
title: Weasel Schema Management
---

The Quartz.Weasel packages put the ADO.NET job store's tables under [Weasel](https://weasel.jasperfx.net/),
the schema tool behind Marten and Wolverine. An application already on that stack then creates and migrates
Quartz's tables with the workflow it runs for its own: `db-apply`, `db-assert`, `db-patch`,
`resources setup`, and AutoCreate at startup. Requires Quartz 4.3 or later; 4.4 for MySQL, Oracle and Firebird.

An application not using Weasel keeps [`ProvisionSchema()`](../tutorial/job-stores.md#creating-the-schema) and
the scripts under `database/migrations/`.

## Packages

| Package | Database |
|---|---|
| `Quartz.Weasel.PostgreSQL` | PostgreSQL, standalone or inside a Marten store |
| `Quartz.Weasel.SqlServer` | SQL Server 2016 or later, disk-based tables |
| `Quartz.Weasel.MySQL` | MySQL 8.0 or later, through MySqlConnector |
| `Quartz.Weasel.Oracle` | Oracle, in the session's current schema |
| `Quartz.Weasel.Firebird` | Firebird 3, 4 and 5 |
| `Quartz.Weasel.SQLite` | SQLite, through Microsoft.Data.Sqlite |
| `Quartz.Weasel` | the shared glue; installed by any of the above |

| Dependency | Version |
|---|---|
| Weasel | `[9.39.0, 10.0.0)` from Quartz 4.4; `[9.35.1, 10.0.0)` in 4.3 |
| JasperFx | 2.76.0 or later, through Weasel 9.39.0 |

An application beside Marten or Wolverine resolves both at least that high.

```shell
dotnet add package Quartz.Weasel.PostgreSQL
```

## Registering

Call the dialect's method on the same store that chose the database:

<!-- snippet: sample_weasel_postgres -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UsePostgres(connectionString);
    store.UseSystemTextJsonSerializer();
    store.UseWeaselForPostgres();
}));
builder.Services.AddQuartzHostedService();

IHost host = builder.Build();

// db-apply, db-assert, db-patch, resources: JasperFx's command line, over this host
return await host.RunJasperFxCommands(args);
```
<!-- endSnippet -->

<!-- snippet: sample_weasel_sqlserver -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.UseWeaselForSqlServer();
}));
```
<!-- endSnippet -->

<!-- snippet: sample_weasel_mysql -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseMySqlConnector(connectionString);
    store.UseWeaselForMySql();
}));
```
<!-- endSnippet -->

<!-- snippet: sample_weasel_oracle -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseOracle(connectionString);
    store.UseWeaselForOracle();
}));
```
<!-- endSnippet -->

<!-- snippet: sample_weasel_firebird -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseFirebird(connectionString);
    store.UseWeaselForFirebird();
}));
```
<!-- endSnippet -->

<!-- snippet: sample_weasel_sqlite -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlite(connectionString);
    store.UseWeaselForSqlite();
}));
```
<!-- endSnippet -->

The dialect is in the method name, so an application with several packages never has an ambiguous call.

Checked at startup:

* The store's driver matches the package: Npgsql for PostgreSQL, Microsoft.Data.SqlClient for SQL Server,
  MySqlConnector for MySQL (`UseMySqlConnector`, not `UseMySql`), Oracle.ManagedDataAccess for Oracle,
  FirebirdSql.Data.FirebirdClient for Firebird, Microsoft.Data.Sqlite for SQLite.
* The store does not also call `ProvisionSchema()`. A schema has one owner.

Each scheduler is one Weasel database. Its identifier is the scheduler name and its subject URI is
`quartz://scheduler/<name>`, so `db-patch -d <name>` picks it. Weasel connects through the store's own
connection provider.

## Startup

The schema is applied before any scheduler is built, so the store's validation sees the result.

| `AutoCreate` | Source |
|---|---|
| the value you set | `UseWeaselForPostgres(w => w.AutoCreate = …)`, and the same on the other dialects |
| the active JasperFx profile's `ResourceAutoCreate` | when JasperFx is registered, as Marten and Wolverine read it |
| `CreateOrUpdate` | otherwise |

`None` applies nothing at startup; the store's validation then reports anything missing. To apply at deploy
time only:

<!-- snippet: sample_weasel_jasperfx_profile -->
```csharp
// Weasel applies nothing at startup in Production; db-apply does it at deploy time instead.
services.AddJasperFx(options => options.Production.ResourceAutoCreate = AutoCreate.None);
```
<!-- endSnippet -->

A failed apply fails the startup, unless the profile's `ResourceMigrationFailureMode` is `ContinueOnFailures`:
then it is logged (event 10003) and the store's validation decides.

## Commands

With the host's `Main` ending in `await host.RunJasperFxCommands(args)`:

| Command | Does |
|---|---|
| `db-apply` | applies every difference |
| `db-assert` | fails when the database differs from the model |
| `db-patch <file>` | writes the DDL to a file, and a matching `.drop.sql` |
| `db-list` | lists the databases, one per scheduler |
| `resources setup` | the same apply as `db-apply` |
| `resources check` | the same check as `db-assert` |

`resources clear` and `resources teardown` do nothing to Quartz's tables: they hold the schedule, not state that
can be rebuilt.

## What Weasel changes

| Object | Weasel |
|---|---|
| a missing Quartz table, column, index or key | creates it |
| a Quartz index whose columns changed | drops and recreates it |
| an index name 3.x created and 4.x retired | drops it, as `database/migrations/4.0/` does |
| a column, index or foreign key you added to a Quartz table | keeps it (tables are add-only) |
| your own tables, and other Weasel models' | never looks at them |
| a change SQLite makes only by rebuilding the table | rebuilds it, keeping your objects and the rows |
| a change only possible by dropping a table | refuses, even under `AutoCreate.All` |

The model is generated from the same source as the store's own scripts, and names every object the way the
database's catalog does. A database created by `database/tables/`, by `ProvisionSchema()` or by the
migrations therefore reads as unchanged. The execution history tables and `QRTZ_JOB_STATUS` are always part of
it, as they are of a fresh install.

On a 4.3 database an apply only adds: five nullable columns and `IDX_QRTZ_EH_JOB_TIME` on
`QRTZ_EXECUTION_HISTORY`, and the `QRTZ_JOB_STATUS` table. Nothing is rebuilt.

## PostgreSQL

The schema is the table prefix's: `quartz.qrtz_` puts the tables in schema `quartz`. Without one it is
`public`.

<!-- snippet: sample_weasel_schema_prefix -->
```csharp
// tables quartz.qrtz_job_details, quartz.qrtz_triggers, ...
store.ConfigureStore(options => options.TablePrefix = "quartz.qrtz_");
store.UseWeaselForPostgres();
```
<!-- endSnippet -->

Every apply takes a session advisory lock first, so nodes starting together, or a node starting during
`db-apply`, take turns. The others then find nothing left to do.

| Lock id | Owner |
|---|---|
| `0x5152545A` (1364350042) | Quartz, `PostgresWeaselOptions.DefaultLockId` |
| 4004 | Marten |
| 4006 | Wolverine |

<!-- snippet: sample_weasel_postgres_options -->
```csharp
store.UseWeaselForPostgres(weasel =>
{
    // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
    weasel.AutoCreate = AutoCreate.CreateOrUpdate;
    weasel.LockId = PostgresWeaselOptions.DefaultLockId;
    weasel.LockTimeout = TimeSpan.FromMinutes(1);
});
```
<!-- endSnippet -->

`LockTimeout` bounds the wait; an apply that times out fails like any other.

### Inside a Marten store

To have Marten own the tables, add them as a feature schema and leave `UseWeaselForPostgres()` out:

```csharp
builder.Services.AddMarten(connectionString).ApplyAllDatabaseChangesOnStartup();

// the same tables, with the scheduler's table prefix
builder.Services.ConfigureMarten((services, opts) =>
    opts.Storage.Add(QuartzPostgresFeatureSchema.ForScheduler(services)));
```

* Marten applies them with its own schema: at startup, `db-apply` and `resources setup`. Marten's `AutoCreate`,
  migrator and lock (4004) apply; `PostgresWeaselOptions` does not.
* `CompletelyRemoveAllAsync` leaves them alone. `Storage.ExtendedSchemaObjects` would drop them with
  `CASCADE`.
* `ApplyAllDatabaseChangesOnStartup()` is required, or `db-apply` / `resources setup` before the first start.
  Without either, Marten creates nothing at startup and the store's schema validation fails on the missing
  tables.
* `ForScheduler` refuses a scheduler that also calls `UseWeaselForPostgres()`. Constructing
  `new QuartzPostgresFeatureSchema(prefix)` directly is not checked.
* One Quartz feature per Marten store: Marten keeps one feature per type.

## SQL Server

The schema is the table prefix's: `quartz.QRTZ_` or `[quartz].QRTZ_` puts the tables in schema `quartz`.
Without one it is `dbo`. Every name is the script's: `PK_QRTZ_TRIGGERS`, `FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS`,
`IDX_QRTZ_T_NFT_ST`.

Every apply takes a session application lock (`sp_getapplock`) first, so nodes starting together, or a node
starting during `db-apply`, take turns. The lock is scoped to the database.

| Lock resource | Owner |
|---|---|
| `quartz:migrate` | Quartz, `SqlServerWeaselOptions.DefaultLockResource` |
| `4006` | Wolverine's message store |
| `polecat:migrate:<schema>` | Polecat |

<!-- snippet: sample_weasel_sqlserver_options -->
```csharp
// tables quartz.QRTZ_JOB_DETAILS, quartz.QRTZ_TRIGGERS, ...
store.ConfigureStore(options => options.TablePrefix = "quartz.QRTZ_");
store.UseWeaselForSqlServer(weasel =>
{
    // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
    weasel.AutoCreate = AutoCreate.CreateOrUpdate;
    weasel.LockResource = SqlServerWeaselOptions.DefaultLockResource;
    weasel.LockTimeout = TimeSpan.FromMinutes(1);
});
```
<!-- endSnippet -->

`LockTimeout` bounds the wait, even past the connection's command timeout; an apply that times out fails like
any other.

| Case | Weasel |
|---|---|
| memory-optimized tables (`tables_sqlServerMOT.sql`) | refused before anything runs; keep that script |
| SQL Server before 2016 (`tables_sqlServer_Below2016.sql`) | not supported |
| `FK_QRTZ_BLOB_TRIGGERS_QRTZ_TRIGGERS` | not modelled: `tables_sqlServer.sql` never created it, nor does `ProvisionSchema()` since 4.4; an older provisioned one is kept |
| a `numeric` column's precision | not compared |

## MySQL

The database is the table prefix's: `quartz.QRTZ_` puts the tables in database `quartz`. Without one it is the
connection string's `Database`; a connection that names none is refused. Weasel's own default, `public`, is
never used. Every name is the script's, and the foreign keys are the ones InnoDB names: `QRTZ_TRIGGERS_ibfk_1`.

Every apply takes a user lock (`GET_LOCK`) first, on a connection of its own, so nodes starting together, or a
node starting during `db-apply`, take turns. It needs no privilege. The lock is scoped to the server, not the
database: schedulers on other databases of one server take turns too, unless each has its own `LockName`.

<!-- snippet: sample_weasel_mysql_options -->
```csharp
store.UseWeaselForMySql(weasel =>
{
    // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
    weasel.AutoCreate = AutoCreate.CreateOrUpdate;
    // server-wide: give each database its own name to keep their applies apart
    weasel.LockName = MySqlWeaselOptions.DefaultLockName;
    weasel.LockTimeout = TimeSpan.FromMinutes(1);
});
```
<!-- endSnippet -->

`LockTimeout` bounds the wait, even past the connection's command timeout; an apply that times out fails like
any other.

| Case | Weasel |
|---|---|
| MySQL 5.7 | not supported: it ignores `PRIORITY DESC`, so `IDX_QRTZ_T_NFT_ST` reads as changed on every apply |
| MySql.Data (`UseMySql`) | refused at startup: Weasel speaks MySqlConnector only |
| a prefix naming a database that does not exist | created, which needs the `CREATE` privilege |
| a `DECIMAL`'s precision, a column default | not compared |

## Oracle

The schema is the table prefix's: `quartz.QRTZ_` puts the tables in schema `QUARTZ`. Without one it is the
session's current schema, `SYS_CONTEXT('USERENV', 'CURRENT_SCHEMA')`: the login's own, or the schema a logon
trigger moves the session to. Weasel's own default, `WEASEL`, is never used. Every name is the script's:
`QRTZ_TRIGGERS_PK`, `QRTZ_TRIGGER_TO_JOBS_FK`, `IDX_QRTZ_T_NFT_ST`.

No lock is taken, because `DBMS_LOCK` needs an `EXECUTE` grant the store never needs. Appliers race instead, as
`ProvisionSchema()` does: an apply that fails reads the schema again, and stops when another process has
finished it (event 10009). It gives up after 10 attempts.

| Privilege | Needed for |
|---|---|
| `CREATE SESSION`, `CREATE TABLE`, quota on the tablespace | the tables in the login's own schema |
| `CREATE ANY TABLE`, `ALTER ANY TABLE`, `CREATE ANY INDEX`, `SELECT ANY TABLE`, `SELECT ANY DICTIONARY`; quota for that schema | the tables in another schema |

| Case | Weasel |
|---|---|
| an index's tablespace | kept; not compared |
| a `NUMBER`'s precision, a column default | not compared |

## Firebird

Firebird 3, 4 and 5. Firebird has no schemas, so a table prefix with a dot is refused. Every name is the
script's: `PK_QRTZ_TRIGGERS`, `FK_QRTZ_TRIGGERS_1`, `IDX_QRTZ_T_NFT_ST`.

No lock is taken: Weasel.Firebird has none. Every statement it runs is guarded, so a statement that loses a race
to another applier runs again and finds the object there. An apply that still fails reads the schema again, and
stops when another process has finished it (event 10009).

| Firebird | Longest name | Table prefix |
|---|---|---|
| 3 | 31 bytes, the default | at most 6 characters |
| 4 and 5 | 63 characters, with `MaxIdentifierLength = 63` | at most 38 characters |

A prefix too long for the limit is refused before anything runs. Names are never truncated.

<!-- snippet: sample_weasel_firebird_options -->
```csharp
// IDX_QRTZ_REPORTING_FT_INST_JOB_REQ_RCVRY is 40 characters, past Firebird 3's 31 bytes
store.ConfigureStore(options => options.TablePrefix = "QRTZ_REPORTING_");
store.UseWeaselForFirebird(weasel =>
{
    // unset: the active JasperFx profile's ResourceAutoCreate, else CreateOrUpdate
    weasel.AutoCreate = AutoCreate.CreateOrUpdate;
    // only for a database Firebird 3 never opens
    weasel.MaxIdentifierLength = 63;
});
```
<!-- endSnippet -->

| Case | Weasel |
|---|---|
| a UTF8 database | needs a page size of 16384 for the keys over Quartz's `VARCHAR` columns |
| a column you widened | refused before anything runs: Firebird cannot narrow it back |
| a foreign key's delete rule | none, as in the script; read back as `RESTRICT` |

Weasel.Firebird was contributed to Weasel in [JasperFx/weasel#666](https://github.com/JasperFx/weasel/pull/666).
Thanks to the JasperFx maintainers for merging it.

## SQLite

* The store must use `UseSqlite`: Weasel speaks Microsoft.Data.Sqlite only.
* No lock is taken. The file serializes writers, every `CREATE` is guarded, and an `ADD COLUMN` that loses a
  race is re-read and found done.
* SQLite makes some changes by rebuilding the table. The rebuild keeps your own columns, indexes and foreign
  keys, with their rows, and so does rolling it back with `db-patch`'s `.drop.sql`.
* A rebuild that fails rolls back and changes nothing. The usual cause is a row that the restored foreign key
  rejects, such as a trigger whose job is gone.

## Moving off Weasel.Quartz.Postgres

[Weasel.Quartz.Postgres](https://github.com/Hawxy/Weasel.Quartz) was the first Weasel integration for
Quartz.NET — thanks to Jaedyn for building it. The Quartz.Weasel packages need Quartz 4.3; an application
still on Quartz 3.x keeps Weasel.Quartz.Postgres until it upgrades.

Its table and constraint names are PostgreSQL's defaults, as here, so switching renames nothing and rebuilds
no table. The first apply is a 3.x-to-4.x upgrade: `idx_qrtz_t_nft_st` is dropped and recreated in its 4.x
shape, the 4.x columns and tables are added, and any retired 3.x index names are dropped.

1. Remove the `Weasel.Quartz.Postgres` package.
2. Replace `QuartzSchema.Create(...)` with `UseWeaselForPostgres()` on the store.
3. With Marten, replace `options.Storage.ExtendedSchemaObjects.AddRange(QuartzSchema.AllTables())` with the
   feature schema above.

::: warning Every node in one deployment
Remove Weasel.Quartz.Postgres from every node at once. Its tables are not add-only and it applies with
`CreateOrUpdate`, so a node still running it drops the 4.x columns its model does not know.
:::

## See also

* [Wolverine](../how-tos/wolverine.md) — Quartz beside Wolverine
* [Job stores](../tutorial/job-stores.md) — the ADO.NET store and its schema
* [Schema changes](../../database/schema-changes.md) — every schema version and its migration
