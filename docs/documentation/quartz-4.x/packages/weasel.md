---
title: Weasel Schema Management
---

The Quartz.Weasel packages put the ADO.NET job store's tables under [Weasel](https://weasel.jasperfx.net/),
the schema tool behind Marten and Wolverine. An application already on that stack then creates and migrates
Quartz's tables with the workflow it runs for its own: `db-apply`, `db-assert`, `db-patch`,
`resources setup`, and AutoCreate at startup. Requires Quartz 4.3 or later.

An application not using Weasel keeps [`ProvisionSchema()`](../tutorial/job-stores.md#creating-the-schema) and
the scripts under `database/migrations/`.

## Packages

| Package | Database |
|---|---|
| `Quartz.Weasel.PostgreSQL` | PostgreSQL, standalone or inside a Marten store |
| `Quartz.Weasel.SqlServer` | SQL Server 2016 or later, disk-based tables |
| `Quartz.Weasel.SQLite` | SQLite, through Microsoft.Data.Sqlite |
| `Quartz.Weasel` | the shared glue; installed by any of the above |

MySQL and Oracle wait for fixes in Weasel itself. Firebird is not planned: Weasel has no Firebird provider.

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
  Microsoft.Data.Sqlite for SQLite.
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
| a change only possible by dropping a table | refuses, even under `AutoCreate.All` |

The model is generated from the same source as the store's own scripts, and names every object the way the
database's catalog does. A database created by `database/tables/`, by `ProvisionSchema()` or by the
migrations therefore reads as unchanged. The execution history tables and `QRTZ_JOB_STATUS` are always part of
it, as they are of a fresh install.

On a 4.3 database an apply only adds: five nullable columns and `IDX_QRTZ_EH_JOB_TIME` on
`QRTZ_EXECUTION_HISTORY`, and the `QRTZ_JOB_STATUS` table. Nothing is rebuilt, so your own columns and indexes
on the history table stay.

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
| `FK_QRTZ_BLOB_TRIGGERS_QRTZ_TRIGGERS` | not modelled: `tables_sqlServer.sql` never created it; `ProvisionSchema()`'s is kept |
| a `numeric` column's precision | not compared |

## SQLite

* The store must use `UseSqlite`: Weasel speaks Microsoft.Data.Sqlite only.
* No lock is taken. The file serializes writers, every `CREATE` is guarded, and an `ADD COLUMN` that loses a
  race is re-read and found done.
* SQLite makes some changes by rebuilding the table, and the rebuild keeps only what the model declares. A
  rebuild of a Quartz table that carries your own columns, indexes or keys is refused before anything runs,
  naming them.

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
