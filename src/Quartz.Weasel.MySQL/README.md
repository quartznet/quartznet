# Quartz.Weasel.MySQL

[Quartz.Weasel.MySQL](https://www.nuget.org/packages/Quartz.Weasel.MySQL) hands a MySQL job store's schema to
[Weasel](https://weasel.jasperfx.net/). Weasel then creates and migrates Quartz's tables at startup and from
JasperFx's `db-apply`, `db-assert`, `db-patch` and `resources` commands.

## Installation

```shell
dotnet add package Quartz.Weasel.MySQL
```

## Usage

<!-- snippet: sample_readme_weasel_mysql -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseMySqlConnector(connectionString);
    store.UseWeaselForMySql();
}));
```
<!-- endSnippet -->

* MySQL 8.0 or later, through MySqlConnector.
* The tables are add-only: columns, indexes and foreign keys you added to them are kept.
* Every apply takes the MySQL user lock `quartz:migrate` first, unless you set another.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/weasel.html>
