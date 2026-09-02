# The Aspire example's worker

What `Quartz.Aspire` is for, in three lines: an Aspire connection name becomes a persistent job store,
its telemetry subscriptions and the scheduler's health check. The reference is
[Aspire Integration](https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/aspire.html)
and the recipe is
[Running Quartz under Aspire](https://www.quartz-scheduler.net/documentation/quartz-4.x/how-tos/aspire.html).

## Running it

Normally through the AppHost, which starts the database and injects its connection string:

```shell
dotnet run --project src/Quartz.Examples.Aspire.AppHost
```

See [that project's readme](../Quartz.Examples.Aspire.AppHost/README.md) for what it needs.

**It also runs without an AppHost.** `Quartz.Aspire` takes no `Aspire.*` package reference —
`IHostApplicationBuilder` is the whole of its contract — so all it wants is a connection string under
the name it was given. Put one in `appsettings.json` and run the worker on its own against any
PostgreSQL you have:

```json
{
  "ConnectionStrings": {
    "quartz": "Host=localhost;Database=quartz;Username=postgres;Password=postgres"
  }
}
```

```shell
dotnet run --project src/Quartz.Examples.Aspire.Worker
```

## What it shows

- **The provider is inferred from the connection string**, by keyword rather than by substring, so
  nothing declares that this is PostgreSQL.
- **The ordering that matters.** `builder.AddNpgsqlDataSource("quartz")` comes first, so Quartz takes
  its connections from that data source rather than opening its own; a data source registered
  afterwards is one `AddQuartzPersistentStore` never sees. That is the *only* ordering constraint —
  `AddQuartzPersistentStore` and `AddQuartz` may be written in either order.
- **Schema provisioning follows the environment.** In `Development` the store creates whatever it is
  missing; anywhere else it validates and refuses to start against an empty database, and something
  else applies the schema. `QuartzAspireSettings.SchemaProvisioning` says which without asking.
- **`Microsoft.NET.Sdk.Web` rather than `Microsoft.NET.Sdk.Worker`**, because `WithHttpHealthCheck` in
  the AppHost needs something serving `/health`. The application still hosts nothing but the scheduler.
- **No `AddServiceDefaults()`.** A generated Aspire solution has a ServiceDefaults project and calls it
  here; this repository carries no such project, and nothing on either Aspire page needs one —
  `AddQuartzPersistentStore` subscribes Quartz's activity source and meter whether it ran or not.
