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

The package depends on `Microsoft.EntityFrameworkCore.Relational` 10.0.0 or later and on no provider: the
dialect is read from the provider's name. Oracle and Firebird have no model yet.

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

* On PostgreSQL every name is folded to lower case, as PostgreSQL folds the store's unquoted ones.
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
| PostgreSQL, SQL Server | nothing |

It is safe to run again: the triggers are created only where missing, and the index is replaced by itself.

## A database the scripts created

The tables already exist, from `database/tables/`, `ProvisionSchema()` or Weasel. Record the first migration
without running it:

1. Bring the database to this release's schema with `database/migrations/`. See
   [Database Schema Changes](../../database/schema-changes.md).
2. Run `dotnet ef migrations add AddQuartz` with no other model change pending, so that Quartz's tables are
   all the migration holds.
3. Empty its `Up`:

   <!-- snippet: sample_efcore_baseline -->
   ```csharp
   public partial class AddQuartz : Migration
   {
       // the tables are there already, from database/tables or ProvisionSchema();
       // dotnet ef database update records this migration and runs nothing
       protected override void Up(MigrationBuilder migrationBuilder)
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

* Constraint names are EF Core's: `PK_<table>` and `FK_<table>_<referenced>_<columns>`, cut to the database's
  identifier length. The store never names a constraint.
* SQL Server writes a default as `CAST(0 AS bit)` where the script writes `0`. Same value.
* On SQLite, a migration of yours that changes a column of a Quartz table rebuilds the table, which drops
  its triggers. End it with `CompleteQuartzSchema()`. Quartz's own migrations never rebuild a table.
* Oracle's MySQL provider is the only one for EF Core 10. It writes a `bool` with `(1)` after its type, which is
  why the model says `tinyint(1)`, and leaves an index column's direction out, which `CompleteQuartzSchema()`
  puts back.
* EF Core 11 targets `net11.0` only. The package is built against EF Core 10 and runs on 11.

## Moving off AppAny

[AppAny.Quartz.EntityFrameworkCore.Migrations](https://github.com/appany/AppAny.Quartz.EntityFrameworkCore.Migrations)
brought EF Core migrations to Quartz.NET first; thanks to its authors for building and keeping it. This
package succeeds it on Quartz 4.9. An application on Quartz 3.x keeps AppAny.

The tables have the same names, so the move renames nothing and rebuilds no table. It is one migration.

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
4. Run `dotnet ef migrations add MoveToQuartzEntityFrameworkCore`, and replace the body of `Up`:

   <!-- snippet: sample_efcore_move -->
   ```csharp
   public partial class MoveToQuartzEntityFrameworkCore : Migration
   {
       protected override void Up(MigrationBuilder migrationBuilder)
       {
           // replaces everything dotnet ef wrote here; the options are AddQuartzTables's
           migrationBuilder.MoveQuartzTablesFromAppAny(tables => tables.Schema = "quartz");
       }
   }
   ```
   <!-- endSnippet -->

5. Run `dotnet ef database update` with the schedulers stopped: on SQL Server and MySQL it rebuilds indexes on
   `QRTZ_TRIGGERS` and `QRTZ_FIRED_TRIGGERS`.

The snapshot then holds this package's model, and every later migration only adds.

| What the move does | PostgreSQL | SQL Server | MySQL | SQLite |
|---|---|---|---|---|
| **Repairs**: AppAny has it wrong | the 3 history indexes AppAny never created | the same; `STR_PROP_1` to `STR_PROP_3` nullable; `QRTZ_FIRED_TRIGGERS.INSTANCE_NAME` 150 to 200 wide | the same indexes; `IDX_QRTZ_T_NFT_ST` with `PRIORITY DESC` | the same indexes; the delete triggers |
| **Converges**: harmless, cheap, never narrows | `QRTZ_TRIGGERS`' foreign key stops cascading | the same; `QRTZ_BLOB_TRIGGERS`' foreign key dropped; `IS_NONCONCURRENT` nullable; `MISFIRE_INSTR` `int` | `IS_NONCONCURRENT` nullable | — |
| **Leaves**: harmless, or the same type spelled otherwise | `boolean` for `bool` on two columns | `CRON_TRIGGERS.TIME_ZONE_ID` 120 wide; `REPEAT_COUNT`, `TIMES_TRIGGERED` `bigint` | 33 spellings such as `bigint(19)`; five cascading foreign keys | every column's spelling, such as `text`; `QRTZ_TRIGGERS`' cascading foreign key |

* On SQL Server, AppAny's `NOT NULL` string properties fail every calendar-interval and recurrence trigger,
  which leave `STR_PROP_2` and `STR_PROP_3` empty: "Cannot insert the value NULL into column 'STR_PROP_2'".
* What it leaves stays in the database as AppAny made it. Changing it would narrow a column, copy a table on
  MySQL or rebuild every table on SQLite, for no difference the store sees.

## See also

* [Job stores](../tutorial/job-stores.md): the ADO.NET store and its schema
* [Database Schema Changes](../../database/schema-changes.md): every schema version and its migration
* [Weasel](weasel.md): the same tables under Weasel, as Marten and Wolverine manage theirs
