# Quartz.Weasel.Oracle

[Quartz.Weasel.Oracle](https://www.nuget.org/packages/Quartz.Weasel.Oracle) hands an Oracle job store's schema
to [Weasel](https://weasel.jasperfx.net/). Weasel then creates and migrates Quartz's tables at startup and from
JasperFx's `db-apply`, `db-assert`, `db-patch` and `resources` commands.

## Installation

```shell
dotnet add package Quartz.Weasel.Oracle
```

## Usage

<!-- snippet: sample_readme_weasel_oracle -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseOracle(connectionString);
    store.UseWeaselForOracle();
}));
```
<!-- endSnippet -->

* The tables go in the session's current schema, unless the table prefix names another.
* The tables are add-only: columns, indexes and foreign keys you added to them are kept.
* No lock and no extra grant: nodes applying at once read the schema again and stop when it is done.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/weasel.html>
