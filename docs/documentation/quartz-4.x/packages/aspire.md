---
title: Aspire Integration
---

# Aspire Integration

[Quartz.Aspire](https://www.nuget.org/packages/Quartz.Aspire) is the Quartz.NET **client integration** for
[Aspire](https://aspire.dev/). One call turns an Aspire connection name into a persistent job store:

<!-- snippet: sample_aspire_add_persistent_store -->
```csharp
builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

The call:

1. reads the connection string the AppHost injected under `quartz`;
2. infers the database and chooses its driver delegate;
3. takes connections from a `DbDataSource` already in the container, if there is one;
4. registers the scheduler's health check;
5. adds Quartz's activity source and meter to the OpenTelemetry pipeline `AddServiceDefaults()` built.

The recipe (the AppHost, the dashboard, and each step written out by hand) is
[Running Quartz under Aspire](../how-tos/aspire.md). Every store setting is in
[Job Stores](../tutorial/job-stores.md) and the [Configuration Reference](../configuration/reference.md#persistent-job-store).

::: tip
Quartz 4.0 or later required. Documented against **Aspire 13.5**.
:::

## Installation

```shell
dotnet add package Quartz.Aspire
```

The application still references the ADO.NET driver for its database, as without Aspire: `Npgsql` for
PostgreSQL, `Microsoft.Data.SqlClient` for SQL Server, and so on
([the full table](../tutorial/job-stores.md#configuring-a-persistent-store)). An Aspire client integration such
as `Aspire.Npgsql` brings its driver; a worker that only reads a connection string from configuration needs it
added:

```shell
dotnet add package Npgsql
```

The package has **no `Aspire.*` package reference**; `IHostApplicationBuilder` is its whole contract. It is not
tied to Aspire's release cadence and works in any generic-host application with a `ConnectionStrings:` entry.
`Aspire.Npgsql` 13.5.3 has no `Aspire.*` dependency either.

## The call

```csharp
public static IHostApplicationBuilder AddQuartzPersistentStore(
    this IHostApplicationBuilder builder,
    string connectionName,
    Action<QuartzAspireSettings>? configureSettings = null);
```

`connectionName` is the AppHost's name for the database resource. The connection string arrives under
`ConnectionStrings:<connectionName>`, and a keyed `DbDataSource` would use it as its service key.

- **Additive and order-independent.** The store is contributed through `ConfigureAllQuartzSchedulers`, so the
  call can come before or after `AddQuartz`. `AddQuartz` still reads the `Quartz` configuration section and
  configures the scheduler.
- **One ordering matters: call a client integration that registers a `DbDataSource` first.** The connection
  source is decided at this call, against the service collection as it is then.

## Settings

`QuartzAspireSettings` covers only what follows from an Aspire connection. `Quartz:Scheduler`, `Quartz:JobStore`
and the rest still apply through `AddQuartz`.

| Setting | Type | Default | What it decides |
|---|---|---|---|
| `ConnectionString` | `string?` | `ConnectionStrings:<name>` | The connection string, when not the one Aspire injected |
| `Provider` | `string?` | inferred from the connection string | Which ADO.NET driver reaches the database |
| `SchedulerName` | `string?` | every scheduler in the container | Which scheduler this store belongs to |
| `TablePrefix` | `string?` | whatever `AdoJobStoreOptions.TablePrefix` already had | Prefix on the Quartz table names |
| `SchemaProvisioning` | `SchemaProvisioning?` | unset: creates under `Development`, nothing elsewhere | What the store does about its schema at start |
| `Clustered` | `bool` | `false` | Join a cluster on the database, deriving an instance id |
| `DisableHealthChecks` | `bool` | `false` | Leave the scheduler's health check unregistered |
| `DisableTracing` | `bool` | `false` | Leave the `Quartz` activity source unsubscribed |
| `DisableMetrics` | `bool` | `false` | Leave the `Quartz` meter unsubscribed |

- The flags are `Disable*` because Aspire requires a fresh settings instance (what an absent section binds to)
  to hold the recommended values, and an unbound `bool` is `false`. First-party integrations have used this
  spelling since Aspire 8.0.
- `SchemaProvisioning` is nullable because the recommended value depends on the environment. Unset means "ask
  the environment". It is the same enum as `AdoJobStoreOptions.SchemaProvisioning`.
- No setting undoes existing configuration. `TablePrefix` unset keeps what `Quartz:JobStore:TablePrefix` or an
  earlier `ConfigureStore` set. `Clustered = false` does not un-cluster a scheduler that
  `Quartz:JobStore:Clustering:Enabled` or `UseClustering()` already clustered.

### Where the settings come from

Each source overrides the one before:

1. `Aspire:Quartz`: every Quartz connection in the application.
2. `Aspire:Quartz:<connectionName>`: that connection only.
3. `ConnectionStrings:<connectionName>`: supplies `ConnectionString`, when present.
4. The `configureSettings` callback.

Every first-party client integration uses this order. `ConnectionStrings:<name>` is what the AppHost's
`WithReference` injected, so it beats a stale `ConnectionString` in `appsettings.json`.

With one database, only the outer section is needed:

```json
{
  "Aspire": {
    "Quartz": {
      "Provider": "Npgsql",
      "Clustered": true
    }
  }
}
```

With two, the inner section wins where they overlap:

```json
{
  "Aspire": {
    "Quartz": {
      "Clustered": true,
      "TablePrefix": "QRTZ_",
      "orders-db": { "SchedulerName": "orders" },
      "billing-db": { "SchedulerName": "billing", "TablePrefix": "BILLING_QRTZ_" }
    }
  }
}
```

Both stores are clustered; only billing reads `BILLING_QRTZ_` tables.

Code overrides both:

<!-- snippet: sample_aspire_settings -->
```csharp
builder.AddQuartzPersistentStore("quartz", settings =>
{
    settings.Provider = DataSourceOptions.Providers.Npgsql;
    settings.Clustered = true;
});
```
<!-- endSnippet -->

The package ships a `ConfigurationSchema.json` at its root, wired up by a `buildTransitive` targets file, so an
IDE completes and validates the `Aspire` section of `appsettings.json`. It is hand-written and held to the
settings type by a test, because Aspire's generator for it
([microsoft/aspire#3309](https://github.com/microsoft/aspire/issues/3309)) has never shipped.

## Which database the connection string is for

Unset, `Provider` is inferred from the connection string's **keywords**. They are parsed with
`DbConnectionStringBuilder` (never substring-matched) and compared without spaces and underscores, so
`User ID`, `userid` and `User_Id` are one keyword.

| Provider | Inferred when the connection string |
|---|---|
| `Npgsql` | has `Host` and `Database`, and no `Uid` |
| `SqlServer` | has a server keyword¹ and one of `Initial Catalog`, `Database`, `Integrated Security`, `Trusted_Connection`; and no `Port` or `Host` |
| `MySqlConnector` | has a server keyword¹ but not `Host`, plus `Port` and one of `Uid`, `User Id`, `Username`, `User` |
| `SQLite-Microsoft` | has `Data Source` of `:memory:` or ending in `.db`, `.db3`, `.sqlite`, `.sqlite3`; or has `Mode=Memory` |
| `OracleODPManaged` | has `Data Source` holding a TNS descriptor or EZ-connect `host:port/service`, and no `Host` or `Port` |

¹ `Server`, `Data Source`, `Address`, `Addr` or `Network Address`. `Microsoft.Data.SqlClient` accepts neither
`Port` nor `Host`.

::: warning An ambiguous or unrecognised string throws
Zero or two matches throw a `SchedulerConfigException` at startup, naming `QuartzAspireSettings.Provider` and
`DataSourceOptions.Providers`. A guess would instead start a scheduler that issues SQL the database cannot run,
found only at the first trigger acquisition.
:::

- Three of the eight shipped provider names are **never inferred**. `MySql` and `SQLite` accept the same strings
  as `MySqlConnector` and `SQLite-Microsoft`, and a Firebird string looks like several others. Name them in
  `Provider`.
- A provider name Quartz has no description for goes to `UseGenericDatabase`: the generic SQL dialect, with the
  description from the application's registered `DbMetadataFactory`. See
  [A Driver Delegate for a New Database](../how-tos/dialect-delegate.md).
- Not recognised (set `Provider`): a MySQL string written with `Host=` and `Username=`, and a bare Oracle TNS
  alias (`Data Source=orcl`), which looks like a SQL Server instance name.

## Where connections come from

The package sets one connection setting on `DataSourceOptions`. The first matching row wins:

| Condition | The store is configured with |
|---|---|
| The provider is `SqlServer` | `ConnectionString` and `ConnectionStringName` (no probe) |
| A keyed `DbDataSource` under `connectionName` | `DataSourceServiceKey = connectionName` |
| An unkeyed `DbDataSource` | `UseRegisteredDataSource = true` |
| Anything else | `ConnectionString` and `ConnectionStringName` |

`builder.AddKeyedNpgsqlDataSource("quartz")` gives the second row and `builder.AddNpgsqlDataSource("quartz")` the
third. Both register a singleton `System.Data.Common.DbDataSource`, the service type probed for. A keyed source
is used because two databases cannot both be the container's one unkeyed data source.

- A data source is preferred: commands are made by its connection, so its type mappers, logging and connection
  multiplexing apply to Quartz's statements.
- The connection-string path names the driver's connection, command and parameter types as strings, which is
  why `AddQuartzPersistentStore` carries `[RequiresUnreferencedCode]`.
- **SQL Server never uses a data source.** `Microsoft.Data.SqlClient` has no `DbDataSource` implementation, and
  Aspire's SQL Server integration registers a scoped `SqlConnection`. A probe would find another database's
  data source, or none, and fail at first use.

## What happens to the schema

| `SchemaProvisioning` | `AdoJobStoreOptions.SchemaProvisioning` becomes |
|---|---|
| unset (the default) | `CreateIfMissing` under `Development`; untouched elsewhere |
| `CreateIfMissing` | `CreateIfMissing`, in any environment |
| `Validate` | `Validate`, in any environment (already the default, so nothing is set) |
| `None` | `None`, in any environment; the startup check is skipped |

- `builder.Environment` is read at the `AddQuartzPersistentStore` call, not at scheduler start.
- Why by environment: an AppHost's database container starts empty with a new volume, while a production
  account usually has no DDL permission.
- A `SchemaProvisioning` the application set (through `ConfigureStore` or `Quartz:JobStore:SchemaProvisioning`)
  is left alone. The exception is `Validate`, which cannot be told apart from the default: in `Development`,
  set `SchemaProvisioning = SchemaProvisioning.Validate` here instead.

What provisioning runs, why it is safe when a cluster starts at once, and the two configurations that cannot
provision or would provision the wrong schema are in
[Creating the schema](../tutorial/job-stores.md#creating-the-schema). The production recipe is in
[Running Quartz under Aspire](../how-tos/aspire.md#getting-the-tables-there).

## Clustering

`Clustered = true` calls `UseClustering()`, which also turns on database locking, **and makes the scheduler
derive its `InstanceId`**.

Cluster nodes recognise their own check-in row and fired triggers by `InstanceId`. Every scheduler starts with
`QuartzSchedulerOptions.DefaultInstanceId`, which is `NON_CLUSTERED`, and an Aspire replica set
(`WithReplicas(2)`) gives replicas no identity of their own. Without a derived id, all nodes would share one,
the worst failure a cluster can have.

It never overrides: an application that set `GenerateInstanceId`, or set `InstanceId` in code or in
`Quartz:Scheduler:InstanceId`, keeps it.

## Health and telemetry

- **Health check:** `IQuartzBuilder.AddQuartzHealthChecks()` from the core `Quartz` package, on the same
  `IHealthChecksBuilder` as ServiceDefaults' `self` check, so `MapDefaultEndpoints()` serves both. One check per
  scheduler, each under its own name. What it reports and what survives an HTTP probe:
  [the how-to's Health section](../how-tos/aspire.md#health).
- **Telemetry:** `AddSource("Quartz")` and `AddMeter("Quartz")` on the existing `AddOpenTelemetry()` builder.
- **No exporter is added.** `AddServiceDefaults()` calls `UseOtlpExporter()` when `OTEL_EXPORTER_OTLP_ENDPOINT`
  is set (the AppHost sets it). `UseOtlpExporter` may be called only once and cannot be combined with a
  signal-specific `AddOtlpExporter()`; either mistake throws `NotSupportedException`. Aspire also rules
  exporters out of a client integration's scope.

Turn any of the three off:

<!-- snippet: sample_aspire_disable_signals -->
```csharp
builder.AddQuartzPersistentStore("quartz", settings =>
{
    settings.DisableTracing = true;
    settings.DisableMetrics = true;
    settings.DisableHealthChecks = true;
});
```
<!-- endSnippet -->

[Observability](opentelemetry-integration.md) lists every span, instrument and attribute.

## More than one scheduler

Unset, `SchedulerName` gives the store to every scheduler in the container. With two schedulers on two
databases, set it to the name `AddQuartz(name, …)` registered:

<!-- snippet: sample_aspire_two_schedulers -->
```csharp
builder.AddQuartz("orders");
builder.AddQuartz("billing");

builder.AddQuartzPersistentStore("orders-db", settings => settings.SchedulerName = "orders");
builder.AddQuartzPersistentStore("billing-db", settings => settings.SchedulerName = "billing");
```
<!-- endSnippet -->

Named schedulers and how their parts are keyed: [Multiple Schedulers](multiple-schedulers.md).

## What this package deliberately does not do

- **No `Quartz.Aspire.Hosting`.** A hosting integration adds AppHost resources (an `AddQuartz()` resource, a
  `WithQuartzDashboard()`). Quartz runs *inside* an existing project resource, so there is nothing to
  orchestrate; the AppHost already declares the database and passes it on.
- **No `AddKeyedQuartzPersistentStore`.** A second database goes to a second, named scheduler, selected with
  `SchedulerName`. Aspire's guidance makes a keyed form "consider, if applicable", not a requirement.
- **No health-check status codes.** `HealthCheckOptions.ResultStatusCodes` is an ASP.NET Core type and the
  application's decision. [The how-to](../how-tos/aspire.md#health) explains why the default mapping loses a
  standby scheduler, and what to write instead.
- **No tables in production.** The store creates its schema under `Development` and validates it elsewhere. It
  never migrates a schema, because nothing in a Quartz schema records its version.
  [The how-to](../how-tos/aspire.md#getting-the-tables-there) has the migration-service recipe for production.
- **No OpenTelemetry exporter**, for the reason above.

Aspire's contributor guidance asks client integrations to support every supported .NET version at the Aspire
release, which for 13.x means `net8.0`. `Quartz.Aspire` targets `net10.0` only, like
[every Quartz 4.0 package](../migration-guide.md).

`AddQuartzHostedService`, and the health check outside Aspire, are in
[Hosted Services Integration](hosted-services-integration.md).
