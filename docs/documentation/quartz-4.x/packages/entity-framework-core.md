---
title: Entity Framework Core
---

`Quartz.EntityFrameworkCore` puts the ADO.NET job store's tables in your own `DbContext`. `dotnet ef migrations add`
then creates them, and after a Quartz upgrade adds what the release added. Requires Quartz 4.9 and EF Core 10
or later.

Without EF Core migrations, use [`ProvisionSchema()`](../tutorial/job-stores.md#creating-the-schema), the
[Weasel packages](weasel.md), or the scripts under `database/`.

## Package

| Database | EF Core provider |
|---|---|
| PostgreSQL | `Npgsql.EntityFrameworkCore.PostgreSQL` |
| SQL Server | `Microsoft.EntityFrameworkCore.SqlServer` |
| MySQL | `MySql.EntityFrameworkCore` (Oracle's); `Pomelo.EntityFrameworkCore.MySql` once it supports EF Core 10 |
| SQLite | `Microsoft.EntityFrameworkCore.Sqlite` |
| Oracle | `Oracle.EntityFrameworkCore` |
| Firebird 3, 4 and 5 | `FirebirdSql.EntityFrameworkCore.Firebird` |

The package depends on `Microsoft.EntityFrameworkCore.Relational` 10.0.0 or later and on no provider: the
dialect is read from the provider's name.

```shell
dotnet add package Quartz.EntityFrameworkCore
```

## Adding the tables

<!-- snippet: sample_efcore_dbcontext -->
```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // your own entities, then Quartz's tables: the dialect follows the context's provider
        modelBuilder.AddQuartzTables(Database.ProviderName);
    }
}
```
<!-- endSnippet -->

The tables are the ones `ProvisionSchema()` creates, generated from the same model: every column with its
store type, nullability and default, the keys, the foreign keys with their delete rules, and the indexes
with their directions. They are entity types without classes, there for the migrations: the store reads and
writes the tables, EF Core never does.

| `QuartzTablesOptions` | Default | Is |
|---|---|---|
| `TablePrefix` | `QRTZ_` | what every table and index name starts with, without a schema |
| `Schema` | unset: the model's default schema | PostgreSQL and SQL Server only |
| `MaxIdentifierLength` | unset: the database's limit, 31 bytes on Firebird | Firebird only: up to 63 for a database only Firebird 4 or later opens. See [Limits](#limits) |

<!-- snippet: sample_efcore_schema -->
```csharp
modelBuilder.AddQuartzTables(Database.ProviderName, tables =>
{
    tables.Schema = "quartz";      // PostgreSQL and SQL Server; unset: the model's default schema
    tables.TablePrefix = "QRTZ_";  // the default
});
```
<!-- endSnippet -->

The store's `TablePrefix` names the same tables: the schema, a dot, the prefix.

<!-- snippet: sample_efcore_schema_store -->
```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    // the same tables: the schema, a dot, the prefix
    store.ConfigureStore(options => options.TablePrefix = "quartz.QRTZ_");
}));
```
<!-- endSnippet -->

* Every name is folded as the database folds the store's unquoted ones: to lower case on PostgreSQL, to upper
  case on Oracle and Firebird.
* A prefix that makes a name longer than the database takes is refused before anything runs. Firebird's limit
  depends on its version: see [Limits](#limits).
* Two schedulers with two prefixes can share one model: call `AddQuartzTables` once for each.

## The first migration

```shell
dotnet ef migrations add AddQuartz
```

End its `Up` with `CompleteQuartzSchema()`, given the same options as `AddQuartzTables`:

<!-- snippet: sample_efcore_first_migration -->
```csharp
public partial class AddQuartz : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // ... the CreateTable and CreateIndex calls dotnet ef wrote ...

        // what the model cannot say: SQLite's delete triggers, MySQL's descending index column
        migrationBuilder.CompleteQuartzSchema();
    }
}
```
<!-- endSnippet -->

| Database | `CompleteQuartzSchema()` adds |
|---|---|
| SQLite | the four delete triggers that clear a trigger's detail rows on a connection that does not enforce foreign keys |
| MySQL | `IDX_QRTZ_T_NFT_ST` again, with `PRIORITY DESC`, which Oracle's provider leaves out |
| PostgreSQL, SQL Server, Oracle, Firebird | nothing |

It is safe to run again: the triggers are created only where missing, and the index is replaced by itself.

## A database the scripts created

The tables already exist, from `database/tables/`, `ProvisionSchema()` or Weasel. Record the first migration
without running it:

1. Bring the database to this release's schema with `database/migrations/`. See
   [Database Schema Changes](../../database/schema-changes.md).
2. Run `dotnet ef migrations add AddQuartz` with no other model change pending, so that Quartz's tables are
   all the migration holds.
3. Empty its `Up` and its `Down`, which would drop tables this migration did not create:

   <!-- snippet: sample_efcore_baseline -->
   ```csharp
   public partial class AddQuartz : Migration
   {
       // the tables are there already, from database/tables or ProvisionSchema();
       // dotnet ef database update records this migration and runs nothing
       protected override void Up(MigrationBuilder migrationBuilder)
       {
       }
   
       // emptied too: the tables were not this migration's to drop
       protected override void Down(MigrationBuilder migrationBuilder)
       {
       }
   }
   ```
   <!-- endSnippet -->

4. Run `dotnet ef database update`. It records the migration and runs nothing.

From then on, an upgrade's migration only adds.

## Upgrading Quartz

A release that changes the schema changes the model. Until you add a migration, `Migrate()` throws EF Core's
pending model changes error, `PendingModelChangesWarning`, and `dotnet ef migrations has-pending-model-changes`
reports it.

```shell
dotnet ef migrations add UpgradeQuartz
```

* The migration adds nullable columns, tables and indexes only, so the previous minor's nodes run beside it.
* [Database Schema Changes](../../database/schema-changes.md) lists what each release adds.
* Keep this package on the same version as `Quartz`: the model is the schema of its own release.

## Limits

* Constraint names are EF Core's on PostgreSQL, SQL Server, MySQL and SQLite: `PK_<table>` and
  `FK_<table>_<referenced>_<columns>`, cut to the database's identifier length. Oracle and Firebird get the
  scripts' own, such as `QRTZ_TRIGGER_TO_JOBS_FK` and `FK_QRTZ_TRIGGERS_1`. The store never names a constraint.
* On the first four, a database the scripts created keeps the scripts' constraint names, such as
  `FK_QRTZ_TRIGGERS_QRTZ_JOB_DETAILS` on SQL Server and `qrtz_triggers_pkey` on PostgreSQL, which differ from the
  snapshot's. Harmless while Quartz's migrations only add.
* SQL Server writes a default as `CAST(0 AS bit)` where the script writes `0`. Same value.
* On SQLite, a migration of yours that changes a column of a Quartz table rebuilds the table, which drops
  its triggers. End it with `CompleteQuartzSchema()`. Quartz's own migrations never rebuild a table.
* Oracle's MySQL provider is the only one for EF Core 10. It writes a `bool` with `(1)` after its type, which is
  why the model says `tinyint(1)`, and leaves an index column's direction out, which `CompleteQuartzSchema()`
  puts back.
* EF Core 11 targets `net11.0` only. The package is built against EF Core 10 and runs on 11.

| Oracle | |
|---|---|
| `Migrate()` takes no lock | The provider's migration lock does nothing, so nothing stops two nodes from migrating at once. Apply migrations from one process: a deployment step, `dotnet ef database update` or a migration bundle, not `Migrate()` in every node's startup |
| The schema | The tables go in the schema of the user the migration logs in as, and so does `__EFMigrationsHistory`; `Schema` is refused |
| A store whose `TablePrefix` names another owner, `OWNER.QRTZ_` | Keep the model's `TablePrefix` as `QRTZ_`, and apply the migrations connected as that owner. Not through `ALTER SESSION SET CURRENT_SCHEMA`: the provider looks for its history table in the login user's `USER_TABLES` |
| Flags | `VARCHAR2(1)`, as in the script, so the provider's 23ai `BOOLEAN` is never used. Oracle 21c needs no `UseOracleSQLCompatibility` for these tables |

| Firebird | |
|---|---|
| Names | 31 bytes by default, Firebird 3's limit: a prefix of at most 6 bytes of UTF-8, 6 characters of ASCII |
| Names on Firebird 4 and 5 | 63 bytes with `MaxIdentifierLength = 63`: a prefix of at most 38 bytes. Only for a database Firebird 3 never opens: Firebird 3 refuses the migration and keeps nothing of it |
| `migrations script --idempotent` | Not supported by the provider. Script a range of migrations, `script <from> <to>`, or use a migration bundle |
| Index directions | One per index, so `IDX_QRTZ_T_NFT_ST` is the script's three columns without `PRIORITY DESC`; the provider refuses mixed directions |
| The schema | The tables go in the connection's database; `Schema` is refused |

<!-- snippet: sample_efcore_firebird_names -->
```csharp
// IDX_QRTZ_REPORTING_FT_INST_JOB_REQ_RCVRY is 40 bytes, past Firebird 3's 31
modelBuilder.AddQuartzTables(Database.ProviderName, tables =>
{
    tables.TablePrefix = "QRTZ_REPORTING_";
    // only for a database Firebird 3 never opens
    tables.MaxIdentifierLength = 63;
});
```
<!-- endSnippet -->

## Moving off AppAny

[AppAny.Quartz.EntityFrameworkCore.Migrations](https://github.com/appany/AppAny.Quartz.EntityFrameworkCore.Migrations)
brought EF Core migrations to Quartz.NET first; thanks to its authors for building and keeping it. This
package succeeds it on Quartz 4.9. An application on Quartz 3.x keeps AppAny.

The tables have the same names, so the move renames nothing, and it rebuilds no table. It is one migration.
AppAny never had Oracle or Firebird, so `MoveQuartzTablesFromAppAny` refuses them.

1. Upgrade AppAny to 0.6.3 and migrate. Earlier versions have other indexes.
2. Replace AppAny's `modelBuilder.AddQuartz(q => q.UseSqlServer())` with `AddQuartzTables`, keeping AppAny's
   prefix and schema. AppAny puts the tables in schema `quartz` on PostgreSQL and SQL Server:

   <!-- snippet: sample_efcore_schema -->
   ```csharp
   modelBuilder.AddQuartzTables(Database.ProviderName, tables =>
   {
       tables.Schema = "quartz";      // PostgreSQL and SQL Server; unset: the model's default schema
       tables.TablePrefix = "QRTZ_";  // the default
   });
   ```
   <!-- endSnippet -->

3. Remove the AppAny packages.
4. Run `dotnet ef migrations add MoveToQuartzEntityFrameworkCore`, replace the body of `Up`, and empty `Down`:

   <!-- snippet: sample_efcore_move -->
   ```csharp
   public partial class MoveToQuartzEntityFrameworkCore : Migration
   {
       protected override void Up(MigrationBuilder migrationBuilder)
       {
           // replaces everything dotnet ef wrote here; the options are AddQuartzTables's,
           // and Schema is for PostgreSQL and SQL Server only
           migrationBuilder.MoveQuartzTablesFromAppAny(tables => tables.Schema = "quartz");
       }
   
       // one-way: what dotnet ef wrote here would undo the repairs, and rebuild every table on SQLite
       protected override void Down(MigrationBuilder migrationBuilder)
       {
       }
   }
   ```
   <!-- endSnippet -->

   The move is one-way. The `Down` EF Core wrote reverses the whole difference: on SQL Server it makes
   `STR_PROP_1` to `STR_PROP_3` `NOT NULL` again, which fails once a trigger has stored a `NULL` there, and
   narrows `INSTANCE_NAME` back to 150; on SQLite it rebuilds every table. Emptied, a rollback leaves the moved
   schema in place.

5. Run `dotnet ef database update` with the schedulers stopped.

The snapshot then holds this package's model, and every later migration only adds.

| What the move does | PostgreSQL | SQL Server | MySQL | SQLite |
|---|---|---|---|---|
| **Repairs**: AppAny has it wrong | the 3 history indexes AppAny never created | the same; `STR_PROP_1` to `STR_PROP_3` nullable; `QRTZ_FIRED_TRIGGERS.INSTANCE_NAME` 150 to 200 wide | the same indexes; `IDX_QRTZ_T_NFT_ST` with `PRIORITY DESC` | the same indexes; the delete triggers |
| **Converges**: harmless, changed in place | `QRTZ_TRIGGERS`' foreign key stops cascading | the same; `QRTZ_BLOB_TRIGGERS`' foreign key dropped; `IS_NONCONCURRENT` nullable | — | — |
| **Leaves**: harmless, a spelling of the same type, or not changeable in place | `boolean` for `bool` on two columns | `CRON_TRIGGERS.TIME_ZONE_ID` 120 wide; `REPEAT_COUNT`, `TIMES_TRIGGERED` `bigint`; `MISFIRE_INSTR` `smallint` | 33 spellings such as `bigint(19)`; `IS_NONCONCURRENT` `NOT NULL`; five cascading foreign keys | every column's spelling, such as `text`; `QRTZ_TRIGGERS`' cascading foreign key |
| **Costs**: what the database does | builds 3 indexes; re-adding the foreign key reads `QRTZ_TRIGGERS` once | the same; the column changes are metadata only, but EF Core rebuilds `IDX_QRTZ_FT_INST_JOB_REQ_RCVRY` over `INSTANCE_NAME` | builds 3 indexes; rebuilds `IDX_QRTZ_T_NFT_ST` | builds 3 indexes; creates 4 triggers |

* On SQL Server, AppAny's `NOT NULL` string properties fail every calendar-interval and recurrence trigger,
  which leave `STR_PROP_2` and `STR_PROP_3` empty: "Cannot insert the value NULL into column 'STR_PROP_2'".
* What it leaves stays in the database as AppAny made it, for no difference the store sees. Changing it would
  narrow a column; rewrite every row of `QRTZ_TRIGGERS` and rebuild its acquisition index (`MISFIRE_INSTR` on
  SQL Server); rebuild `QRTZ_FIRED_TRIGGERS`, since MySQL makes a column nullable only by rebuilding the table;
  copy a table to change a foreign key on MySQL; or rebuild every table on SQLite.

## See also

* [Job stores](../tutorial/job-stores.md): the ADO.NET store and its schema
* [Database Schema Changes](../../database/schema-changes.md): every schema version and its migration
* [Weasel](weasel.md): the same tables under Weasel, as Marten and Wolverine manage theirs
