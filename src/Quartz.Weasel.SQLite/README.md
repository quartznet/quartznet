# Quartz.Weasel.SQLite

[Quartz.Weasel.SQLite](https://www.nuget.org/packages/Quartz.Weasel.SQLite) hands a SQLite job store's
schema to [Weasel](https://weasel.jasperfx.net/). Weasel then creates and migrates Quartz's tables at
startup and from JasperFx's `db-apply`, `db-assert`, `db-patch` and `resources` commands.

## Installation

```shell
dotnet add package Quartz.Weasel.SQLite
```

## Usage

<!-- snippet: sample_readme_weasel_sqlite -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlite(connectionString);
    store.UseWeaselForSqlite();
}));
```
<!-- endSnippet -->

* The store must use Microsoft.Data.Sqlite (`UseSqlite`), the driver Weasel speaks.
* The tables are add-only: columns, indexes and foreign keys you added to them are kept, with their rows,
  even when SQLite has to rebuild a table.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/weasel.html>
