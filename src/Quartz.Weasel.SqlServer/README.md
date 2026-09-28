# Quartz.Weasel.SqlServer

[Quartz.Weasel.SqlServer](https://www.nuget.org/packages/Quartz.Weasel.SqlServer) hands a SQL Server job
store's schema to [Weasel](https://weasel.jasperfx.net/). Weasel then creates and migrates Quartz's tables
at startup and from JasperFx's `db-apply`, `db-assert`, `db-patch` and `resources` commands.

## Installation

```shell
dotnet add package Quartz.Weasel.SqlServer
```

## Usage

<!-- snippet: sample_readme_weasel_sqlserver -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.UseWeaselForSqlServer();
}));
```
<!-- endSnippet -->

* The tables are add-only: columns, indexes and foreign keys you added to them are kept.
* Every apply takes the SQL Server application lock `quartz:migrate` first, unless you set another.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/weasel.html>
