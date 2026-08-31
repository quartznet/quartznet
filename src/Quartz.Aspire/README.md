# Quartz.Aspire

[Quartz.Aspire](https://www.nuget.org/packages/Quartz.Aspire) is the Quartz.NET client integration for
[Aspire](https://aspire.dev/). It turns an Aspire connection name into a persistent job store: the driver
delegate that speaks that database's SQL, connections taken from the `DbDataSource` the container already
holds, the scheduler's health check, and Quartz's two telemetry signals named to the OpenTelemetry pipeline
your ServiceDefaults project built.

It has **no `Aspire.*` package dependency**. `IHostApplicationBuilder` is the whole of the contract, so the
package is not tied to Aspire's release cadence and works in any generic-host application.

## Installation

```shell
dotnet add package Quartz.Aspire
```

## Usage

The AppHost declares the database and hands it to the worker, exactly as it would for any other client
integration:

```csharp
var postgres = builder.AddPostgres("postgres").WithDataVolume();
var quartzDb = postgres.AddDatabase("quartz");

builder.AddProject<Projects.Orders_Worker>("orders")
    .WithReference(quartzDb)
    .WaitFor(quartzDb);
```

The worker names it once:

<!-- snippet: sample_aspire_add_persistent_store -->
```csharp
builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

That is the whole integration. `AddQuartzPersistentStore` may be called before or after `AddQuartz` — the
store is contributed to every scheduler in the container through `ConfigureAllQuartzSchedulers`, so the
order does not matter. `AddQuartz` still reads the `Quartz` configuration section and still configures the
scheduler; nothing here replaces it.

If the application registers a data source of its own — `builder.AddNpgsqlDataSource("quartz")` — register
it **before** this call, and Quartz will take its connections from it rather than from the connection
string.

## Settings

`QuartzAspireSettings` is bound from `Aspire:Quartz`, then from `Aspire:Quartz:<connection name>` over it,
and a callback has the last word:

```csharp
builder.AddQuartzPersistentStore("quartz", settings =>
{
    settings.Provider = "Npgsql";
    settings.Clustered = true;
});
```

| Setting | Default | What it decides |
|---|---|---|
| `ConnectionString` | `ConnectionStrings:<name>` | The connection string, when it is not the one Aspire injected |
| `Provider` | inferred | Which ADO.NET driver reaches the database |
| `SchedulerName` | every scheduler | Which scheduler this store belongs to |
| `TablePrefix` | `QRTZ_` | The prefix on the Quartz table names |
| `SchemaProvisioning` | unset | What the store does about its schema as it starts |
| `Clustered` | `false` | Whether this scheduler joins a cluster on the database, deriving an instance id to do it with |
| `DisableHealthChecks` | `false` | Leaves the scheduler's health check unregistered |
| `DisableTracing` | `false` | Leaves the `Quartz` activity source unsubscribed |
| `DisableMetrics` | `false` | Leaves the `Quartz` meter unsubscribed |

No exporter is ever added. `AddServiceDefaults()` owns that, and `UseOtlpExporter()` may be called only
once.

`Clustered` turns on database locking and makes the scheduler derive its `InstanceId`, because a cluster
needs distinct ids and an Aspire replica set has none of its own to borrow — every replica otherwise starts
life as `NON_CLUSTERED`, and a cluster whose nodes share one id is the worst failure this area has. An
application that already set `GenerateInstanceId`, or that named its nodes with `InstanceId`, keeps what it
said.

## Which database the connection string is for

Left unset, `Provider` is inferred from the connection string's keywords. **An ambiguous or unrecognised
string throws**, naming `QuartzAspireSettings.Provider`: a wrong driver delegate writes SQL the database
cannot run, and it fails at the first trigger acquisition rather than at startup.

| Provider | Inferred when the connection string |
|---|---|
| `Npgsql` | has `Host` and `Database`, and no `Uid` |
| `SqlServer` | has `Server`, `Data Source`, `Address`, `Addr` or `Network Address`, together with `Initial Catalog`, `Database`, `Integrated Security` or `Trusted_Connection` — and has no `Port` and no `Host`, neither of which `Microsoft.Data.SqlClient` accepts |
| `MySqlConnector` | has `Server`, `Data Source`, `Address`, `Addr` or `Network Address` but not `Host`, together with `Port` and one of `Uid`, `User Id`, `Username` or `User` |
| `SQLite-Microsoft` | has `Data Source` whose value is `:memory:` or ends in `.db`, `.db3`, `.sqlite` or `.sqlite3`, or has `Mode=Memory` |
| `OracleODPManaged` | has `Data Source` holding a TNS descriptor or an EZ-connect `host:port/service` string, and no `Host` or `Port` |

`MySql`, `SQLite` and `Firebird` are never inferred. The first two accept the same connection strings as
the drivers above them, so which of each pair to use is the application's choice; name one in `Provider`.
A name Quartz ships no description for works there too — it selects the generic SQL dialect and whatever
`DbMetadataFactory` you registered.

## Getting the tables there

Under `Development` the store creates whatever its schema is missing as it starts, because an AppHost's
database container comes up empty whenever its volume is new. Everywhere else it validates the schema and
creates nothing, because creating tables needs DDL permission a production account is usually right not to
hold. `SchemaProvisioning` names one of the store option's three values to say it outright, and a
`SchemaProvisioning` the application set on the store itself is kept either way.

Nothing migrates a schema, here or anywhere else. In production apply it the way you would apply an Entity
Framework Core migration — a small project the worker waits for with `WaitForCompletion` — from the scripts
in [`database/tables`](https://github.com/quartznet/quartznet/tree/main/database/tables); neither
`WithInitFiles` nor `WithCreationScript` closes that gap, as both run against the server rather than the
database.

## Documentation

Every setting, the provider-inference table and the connection ladder in full:
<https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/aspire.html>

The AppHost beside this worker, the health probe, the schema and the dashboard:
<https://www.quartz-scheduler.net/documentation/quartz-4.x/how-tos/aspire.html>
