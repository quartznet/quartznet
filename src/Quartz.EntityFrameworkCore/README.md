# Quartz.EntityFrameworkCore

[Quartz.EntityFrameworkCore](https://www.nuget.org/packages/Quartz.EntityFrameworkCore) adds the ADO.NET job
store's tables to your own Entity Framework Core `DbContext`. `dotnet ef migrations add` then creates them,
and after a Quartz upgrade adds what the release added.

## Installation

```shell
dotnet add package Quartz.EntityFrameworkCore
```

## Usage

<!-- snippet: sample_readme_efcore -->
```csharp
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // the dialect follows the provider; then: dotnet ef migrations add AddQuartz
        modelBuilder.AddQuartzTables(Database.ProviderName);
    }
}
```
<!-- endSnippet -->

* PostgreSQL, SQL Server, MySQL and SQLite, chosen by the context's provider.
* End the migration that creates the tables with `migrationBuilder.CompleteQuartzSchema()`.
* Moving from AppAny.Quartz.EntityFrameworkCore.Migrations, which this package succeeds on Quartz 4.x with
  thanks to its authors: see the documentation.

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/entity-framework-core.html>
