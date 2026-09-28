# Quartz.Weasel.PostgreSQL

[Quartz.Weasel.PostgreSQL](https://www.nuget.org/packages/Quartz.Weasel.PostgreSQL) hands a PostgreSQL job
store's schema to [Weasel](https://weasel.jasperfx.net/). Weasel then creates and migrates Quartz's tables
at startup and from JasperFx's `db-apply`, `db-assert`, `db-patch` and `resources` commands.

## Installation

```shell
dotnet add package Quartz.Weasel.PostgreSQL
```

## Usage

<!-- snippet: sample_readme_weasel_postgres -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UsePostgres(connectionString);
    store.UseWeaselForPostgres(weasel => weasel.LockId = PostgresWeaselOptions.DefaultLockId);
}));
```
<!-- endSnippet -->

* The tables are add-only: columns, indexes and foreign keys you added to them are kept.
* Every apply takes a PostgreSQL advisory lock first, `0x5152545A` unless you set another.
* To let a Marten store own the tables instead, add `QuartzPostgresFeatureSchema.ForScheduler(services)` to
  `opts.Storage` and leave `UseWeaselForPostgres()` out.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/weasel.html>
