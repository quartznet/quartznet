# Quartz.Weasel.Firebird

[Quartz.Weasel.Firebird](https://www.nuget.org/packages/Quartz.Weasel.Firebird) hands a Firebird job store's schema
to [Weasel](https://weasel.jasperfx.net/). Weasel then creates and migrates Quartz's tables at startup and from
JasperFx's `db-apply`, `db-assert`, `db-patch` and `resources` commands.

## Installation

```shell
dotnet add package Quartz.Weasel.Firebird
```

## Usage

<!-- snippet: sample_readme_weasel_firebird -->
```csharp
builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseFirebird(connectionString);
    store.UseWeaselForFirebird();
}));
```
<!-- endSnippet -->

* Firebird 3, 4 and 5, through FirebirdSql.Data.FirebirdClient (`UseFirebird`).
* The tables are add-only: columns, indexes and foreign keys you added to them are kept.
* No lock: every statement is guarded, and nodes applying at once read the schema again.
* Firebird has no schemas, so a table prefix with a dot is refused.
* Names are at most 31 bytes on Firebird 3, so the table prefix is at most 6 characters. For a database only
  Firebird 4 or later opens, set `MaxIdentifierLength = 63`.
* A UTF8 database needs a page size of 16384.

Weasel.Firebird was contributed to Weasel in [JasperFx/weasel#666](https://github.com/JasperFx/weasel/pull/666).
Thanks to the JasperFx maintainers for merging it.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/weasel.html>
