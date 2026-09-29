# Quartz.Weasel

[Quartz.Weasel](https://www.nuget.org/packages/Quartz.Weasel) puts Quartz.NET's ADO.NET job store schema
under [Weasel](https://weasel.jasperfx.net/), the schema tool behind Marten and Wolverine. An application
already on that stack creates and migrates Quartz's tables the way it migrates its own: `db-apply`,
`db-assert`, `db-patch`, `resources setup`, and AutoCreate at startup.

This package is the shared glue. Install the one for your database, which brings it along:

| Package | Database |
|---|---|
| [Quartz.Weasel.PostgreSQL](https://www.nuget.org/packages/Quartz.Weasel.PostgreSQL) | PostgreSQL, standalone or inside a Marten store |
| [Quartz.Weasel.SqlServer](https://www.nuget.org/packages/Quartz.Weasel.SqlServer) | SQL Server 2016 or later, disk-based tables |
| [Quartz.Weasel.MySQL](https://www.nuget.org/packages/Quartz.Weasel.MySQL) | MySQL 8.0 or later, through MySqlConnector |
| [Quartz.Weasel.Oracle](https://www.nuget.org/packages/Quartz.Weasel.Oracle) | Oracle, in the session's current schema |
| [Quartz.Weasel.SQLite](https://www.nuget.org/packages/Quartz.Weasel.SQLite) | SQLite |

The packages depend on Weasel `[9.36.0, 10.0.0)`, and through it on JasperFx 2.76.0 or later.

An application not using Weasel keeps `ProvisionSchema()` and the scripts under `database/migrations/`.

## Usage

<!-- snippet: sample_readme_weasel -->
```csharp
HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

builder.Services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UsePostgres(connectionString);
    store.UseWeaselForPostgres(); // or UseWeaselForSqlServer/MySql/Oracle/Sqlite(), from their packages
}));
builder.Services.AddQuartzHostedService();

// dotnet run -- db-apply | db-assert | db-patch <file> | resources setup
return await builder.Build().RunJasperFxCommands(args);
```
<!-- endSnippet -->

## Documentation

<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/weasel.html>
