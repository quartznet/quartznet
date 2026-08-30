---
title: Aspire Integration
---

# Aspire Integration

[Quartz.Aspire](https://www.nuget.org/packages/Quartz.Aspire) is the Quartz.NET **client integration** for
[Aspire](https://aspire.dev/). It turns an Aspire connection name into a persistent job store, in one call:

<!-- snippet: sample_aspire_add_persistent_store -->
```csharp
builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

That reads the connection string the AppHost injected under `quartz`, works out which database it is for,
chooses the driver delegate that speaks that database's SQL, takes connections from a `DbDataSource` the
container already holds, registers the scheduler's health check, and names Quartz's activity source and
meter to the OpenTelemetry pipeline `AddServiceDefaults()` built.

This page is the reference: every setting, where it is read from, and every rule the package decides
something by. [Running Quartz under Aspire](../how-tos/aspire.md) is the recipe — the AppHost beside this
worker, what the dashboard shows, and how to write any part of this call out by hand instead.

::: tip
Quartz 4.0 or later required. Documented against **Aspire 13.5**.
:::

## Installation

```shell
dotnet add package Quartz.Aspire
```

The package takes **no `Aspire.*` package reference**. `IHostApplicationBuilder` is the whole of its
contract, so it is not tied to Aspire's release cadence and works in any generic-host application that has
a `ConnectionStrings:` entry — an AppHost is where the string usually comes from, not something the package
requires. That is the ordinary shape for a client integration rather than a peculiarity of this one:
`Aspire.Npgsql` 13.5.3, the first-party integration this package sits beside, has no `Aspire.*` dependency
either.

## The call

```csharp
public static IHostApplicationBuilder AddQuartzPersistentStore(
    this IHostApplicationBuilder builder,
    string connectionName,
    Action<QuartzAspireSettings>? configureSettings = null);
```

`connectionName` is the name the AppHost gave the database resource. It is the name its connection string
arrives under (`ConnectionStrings:<connectionName>`), and the service key a keyed `DbDataSource` would be
registered with — so naming the resource once in the AppHost names it everywhere.

The call is **additive and order-independent**. The store is contributed through
`ConfigureAllQuartzSchedulers`, so it may be written before or after `AddQuartz` and the container comes out
the same. `AddQuartz` still reads the `Quartz` configuration section and still configures the scheduler;
nothing here replaces it. What this call decides is only what an Aspire *connection* is evidence of.

One ordering does matter, and only one: **a client integration that registers a `DbDataSource` has to be
called first.** Where connections come from is decided at this call site, against the service collection as
it stands, rather than inside the per-scheduler callback — because when that callback runs depends on
whether `AddQuartz` has already been called, which is exactly the ordering everything else here is
indifferent to.

## Settings

`QuartzAspireSettings` is deliberately small. It is not a second spelling of Quartz's own configuration:
`Quartz:Scheduler`, `Quartz:JobStore` and the rest still say what they always said, and `AddQuartz` still
reads them. What these settings decide is the handful of things that follow from an Aspire connection.

| Setting | Type | Default | What it decides |
|---|---|---|---|
| `ConnectionString` | `string?` | `ConnectionStrings:<name>` | The connection string, when it is not the one Aspire injected |
| `Provider` | `string?` | inferred from the connection string | Which ADO.NET driver reaches the database |
| `SchedulerName` | `string?` | every scheduler in the container | Which scheduler this store belongs to |
| `TablePrefix` | `string?` | whatever `AdoJobStoreOptions.TablePrefix` already had | The prefix on the Quartz table names |
| `Clustered` | `bool` | `false` | Whether this scheduler joins a cluster on the database, deriving an instance id to do it with |
| `DisableHealthChecks` | `bool` | `false` | Leaves `AddQuartzHealthChecks()` unregistered |
| `DisableTracing` | `bool` | `false` | Leaves the `Quartz` activity source unsubscribed |
| `DisableMetrics` | `bool` | `false` | Leaves the `Quartz` meter unsubscribed |

The three flags are spelled `Disable*` rather than `Enable*` because Aspire's rule for a settings type is
that a fresh instance — which is what binding an absent section produces — must already hold the
recommended values. A `bool` bound from nothing is `false`, so the useful default has to be the `false` one.
Every first-party integration has been spelled this way since Aspire 8.0.

Every setting here says something or says nothing; none of them says *no*. `TablePrefix` left unset keeps
whatever `Quartz:JobStore:TablePrefix` or an earlier `ConfigureStore` said, rather than resetting it, and
`Clustered = false` means "this call is not what makes it a cluster" rather than "un-cluster it" — a
scheduler that `Quartz:JobStore:Clustering:Enabled` or a `UseClustering()` call already clustered stays
clustered.

### Where the settings come from

Four sources, each more specific than the one before it:

1. `Aspire:Quartz` — what is true of every Quartz connection in the application.
2. `Aspire:Quartz:<connectionName>` — bound over it, for that connection alone.
3. `ConnectionStrings:<connectionName>` — supplies `ConnectionString` when there is one.
4. The `configureSettings` callback, which has the last word.

This is the order every first-party client integration uses, and step 3 sitting where it does is deliberate:
`ConnectionStrings:<name>` is what the AppHost's `WithReference` actually injected, so a stale
`ConnectionString` left in an `appsettings.json` should not beat it.

An application with a single database never writes the inner section:

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

An application with two writes both, and the inner one wins where they overlap:

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

Both stores are clustered; the billing one alone reads `BILLING_QRTZ_` tables.

Code beats both:

<!-- snippet: sample_aspire_settings -->
```csharp
builder.AddQuartzPersistentStore("quartz", settings =>
{
    settings.Provider = DataSourceOptions.Providers.Npgsql;
    settings.Clustered = true;
});
```
<!-- endSnippet -->

The package ships a `ConfigurationSchema.json` at its root, wired up by a `buildTransitive` targets file, so
an IDE completes and validates the `Aspire` section in `appsettings.json`. That file is written by hand and
held to the settings type by a test: the generator Aspire uses for its own integrations
([microsoft/aspire#3309](https://github.com/microsoft/aspire/issues/3309)) has never shipped.

## Which database the connection string is for

Left unset, `Provider` is inferred from the connection string's **keywords** — parsed with
`DbConnectionStringBuilder`, never substring-matched, and compared with spaces and underscores removed so
that `User ID`, `userid` and `User_Id` are one keyword.

| Provider | Inferred when the connection string |
|---|---|
| `Npgsql` | has `Host` and `Database`, and no `Uid` |
| `SqlServer` | has `Server`, `Data Source`, `Address`, `Addr` or `Network Address`, together with `Initial Catalog`, `Database`, `Integrated Security` or `Trusted_Connection` — and has no `Port` and no `Host`, neither of which `Microsoft.Data.SqlClient` accepts |
| `MySqlConnector` | has `Server`, `Data Source`, `Address`, `Addr` or `Network Address` but not `Host`, together with `Port` and one of `Uid`, `User Id`, `Username` or `User` |
| `SQLite-Microsoft` | has `Data Source` whose value is `:memory:` or ends in `.db`, `.db3`, `.sqlite` or `.sqlite3`, or has `Mode=Memory` |
| `OracleODPManaged` | has `Data Source` holding a TNS descriptor or an EZ-connect `host:port/service` string, and no `Host` or `Port` |

::: warning An ambiguous or unrecognised string throws
Zero matches and two matches are both a `SchedulerConfigException` at startup, naming
`QuartzAspireSettings.Provider` and `DataSourceOptions.Providers`. The failure a guess produces instead is a
scheduler that starts, connects, and then issues SQL the database cannot run — discovered at the first
trigger acquisition rather than at startup, and not obviously about the connection string when it is.
:::

Three of the eight shipped provider names are **never inferred**, because nothing in a connection string
distinguishes them: `MySql` and `SQLite` accept the same strings as `MySqlConnector` and `SQLite-Microsoft`
above them, so which of each pair to use is the application's choice rather than the string's, and a
Firebird string looks like several of the others. Naming one in `Provider` is how an application chooses.

A name Quartz ships no description for is still usable. It reaches `UseGenericDatabase`, which selects the
generic SQL dialect and leaves the description to whatever `DbMetadataFactory` the application registered —
so a driver Quartz has never heard of is a configuration this supports rather than an error it reports.
[A Driver Delegate for a New Database](../how-tos/dialect-delegate.md) is the rest of that story.

Two blind spots are worth knowing, and both are refusals rather than wrong answers: a MySQL connection
string written with `Host=` and `Username=` matches nothing, and a bare Oracle TNS alias
(`Data Source=orcl`) is indistinguishable from a SQL Server instance name and so is not recognised. Set
`Provider` in either case.

## Where connections come from

Once the database is known, the store still needs a connection. The package sets one of the settings on
`DataSourceOptions`, choosing by what the service collection already holds. The rows are tried in order, and
the first that matches wins:

| Condition | The store is configured with | Why |
|---|---|---|
| The provider is `SqlServer` | `ConnectionString` and `ConnectionStringName` | See below — the probe is skipped entirely |
| A keyed `DbDataSource` under `connectionName` | `DataSourceServiceKey = connectionName` | Two databases cannot both be the container's one unkeyed data source |
| An unkeyed `DbDataSource` | `UseRegisteredDataSource = true` | The application registered exactly one, and this is it |
| Anything else | `ConnectionString` and `ConnectionStringName` | Nothing to take a connection from, so open one |

`builder.AddKeyedNpgsqlDataSource("quartz")` produces the second row and `builder.AddNpgsqlDataSource("quartz")`
the third — both register a singleton `System.Data.Common.DbDataSource`, which is the service type this
probes for, keyed in the first case and unkeyed in the second.

A data source is preferred because whatever it was built with is then in play for Quartz's own statements —
its type mappers, its logging, its connection multiplexing — since commands are made by the connection
rather than from a driver description. It is also the answer that keeps a trimmed application honest: the
connection-string path is what makes `AddQuartzPersistentStore` carry `[RequiresUnreferencedCode]`, because
that path names the driver's connection, command and parameter types as strings.

**SQL Server never takes the data-source path.** `Microsoft.Data.SqlClient` ships no `DbDataSource`
implementation at all, and Aspire's SQL Server client integration registers a scoped `SqlConnection`
instead — so probing for an unkeyed `DbDataSource` would find some *other* database's, or nothing, and would
fail at first use rather than at startup.

## Clustering

`Clustered = true` calls `UseClustering()`, which turns database locking on with it, **and makes the
scheduler derive its `InstanceId`**.

The second half is not a convenience. A cluster's nodes recognise their own check-in row and their own fired
triggers by `InstanceId`; every scheduler starts life carrying `QuartzSchedulerOptions.DefaultInstanceId`,
which is `NON_CLUSTERED`; and under Aspire a replica set is one call — `WithReplicas(2)` — with no identity
of its own to borrow. A cluster whose nodes all answer to one id is the worst failure this area has, so the
setting supplies both halves rather than documenting the trap beside one of them.

It fills a gap and never overrides. An application that already set `GenerateInstanceId`, or that named its
nodes by setting `InstanceId` — from code or from `Quartz:Scheduler:InstanceId` — keeps what it said.

## Health and telemetry

The health check is `AddQuartzHealthChecks()` from the core `Quartz` package, registered on the same
`IHealthChecksBuilder` an Aspire ServiceDefaults project put its own `self` check on, so
`MapDefaultEndpoints()` serves both. It is registered per scheduler, so two schedulers get two checks under
two names. What the check reports, and what survives an HTTP probe, is
[the how-to's Health section](../how-tos/aspire.md#health).

Telemetry is `AddSource("Quartz")` and `AddMeter("Quartz")` on the application's existing
`AddOpenTelemetry()` builder. **No exporter is ever added**, and that is not tidiness: `AddServiceDefaults()`
calls `UseOtlpExporter()` whenever `OTEL_EXPORTER_OTLP_ENDPOINT` is set, the AppHost sets it, and
OpenTelemetry's `UseOtlpExporter` may be called only once and cannot be combined with a signal-specific
`AddOtlpExporter()` — either mistake throws `NotSupportedException`. Aspire says the same thing more
generally: defining exporters is outside a client integration's scope.

Turn any of the three off individually:

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

Left unset, `SchedulerName` gives the store to every scheduler in the container — right for the single
scheduler an application normally has, and wrong the moment two of them talk to two databases. Naming it
scopes the call to one scheduler, by the name `AddQuartz(name, …)` registered:

<!-- snippet: sample_aspire_two_schedulers -->
```csharp
builder.AddQuartz("orders");
builder.AddQuartz("billing");

builder.AddQuartzPersistentStore("orders-db", settings => settings.SchedulerName = "orders");
builder.AddQuartzPersistentStore("billing-db", settings => settings.SchedulerName = "billing");
```
<!-- endSnippet -->

See [Multiple Schedulers](multiple-schedulers.md) for what a named scheduler is and how its parts are keyed.

## What this package deliberately does not do

* **There is no `Quartz.Aspire.Hosting`.** A hosting integration would add resources to the AppHost — an
  `AddQuartz()` resource, a `WithQuartzDashboard()` — and there is nothing for one to orchestrate: Quartz
  runs *inside* an existing project resource rather than as a process of its own. The AppHost declares the
  database and hands it over, which it can already do.
* **There is no `AddKeyedQuartzPersistentStore`.** Every other client integration has a keyed form, and it
  exists so an application can hold two of a thing. Quartz already has an axis for that which is not the
  container's — a second scheduler, registered by name — so `SchedulerName` is how a second call says which
  one it means, and two databases end up on two schedulers rather than on two keyed copies of one. Aspire's
  own guidance makes the keyed form a "consider, if applicable" rather than a requirement.
* **It maps no health-check status codes.** `HealthCheckOptions.ResultStatusCodes` is an ASP.NET Core type
  and a decision about *this application's* probe, not about Quartz;
  [the how-to](../how-tos/aspire.md#health) explains why the default mapping loses a standby scheduler and
  what to write instead.
* **It creates no tables.** Quartz does not provision its schema under Aspire or anywhere else — see
  [the how-to](../how-tos/aspire.md#the-tables-are-still-yours-to-create) for the migration-service recipe,
  and [#3531](https://github.com/quartznet/quartznet/issues/3531) for the store learning to do it itself.
* **It adds no OpenTelemetry exporter**, for the reason above.

One convention this package knowingly diverges from: Aspire's contributor guidance asks a client
integration to support every supported .NET version at the time of the Aspire release it targets, which for
13.x means `net8.0`. `Quartz.Aspire` targets `net10.0` alone, because
[every Quartz 4.0 package does](../migration-guide.md).

## See also

* [Running Quartz under Aspire](../how-tos/aspire.md) — the AppHost, the worker, the dashboard, and every
  line of this call written out by hand
* [Observability](opentelemetry-integration.md) — the spans, instruments and attributes the package
  subscribes
* [Hosted Services Integration](hosted-services-integration.md) — `AddQuartzHostedService`, and the health
  check outside Aspire
* [Job Stores](../tutorial/job-stores.md) and
  [Configuration Reference](../configuration/reference.md#persistent-job-store) — every store setting this
  call sets, and the ones it does not touch
