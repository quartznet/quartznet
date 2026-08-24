---
title: 'Running Quartz under Aspire'
---

# Running Quartz under Aspire

[Aspire](https://aspire.dev/) — formerly ".NET Aspire", renamed in Aspire 13 — is an orchestrator for a
local application and the shared configuration that goes with it. Two of its pieces matter to a scheduler:
the **AppHost**, which declares the databases and projects and wires them to each other, and
**ServiceDefaults**, a project every service references that turns on OpenTelemetry, health checks, service
discovery and HTTP resilience in one call.

A Quartz scheduler needs almost nothing to fit. Quartz already publishes on a `System.Diagnostics`
`ActivitySource` and `Meter`, its health check already registers on the standard `IHealthChecksBuilder`, its
components already take their loggers from the container's `ILoggerFactory`, and its store already takes
connections from a `DbDataSource` the container holds. What is left is naming the two telemetry signals to
the pipeline ServiceDefaults built, and pointing the store at the database the AppHost started. This page is
exactly that list, and it is short.

There is no Quartz integration for Aspire — no `Aspire.Hosting.Quartz` package, no `AddQuartz` resource for
the AppHost. Everything here is ordinary Quartz configuration in an application that Aspire happens to be
running.

## The two lines that do the integration

`AddServiceDefaults()` builds an OpenTelemetry pipeline and then exports it. What the template subscribes to
is a fixed list: runtime, ASP.NET Core and `HttpClient` metrics; ASP.NET Core and `HttpClient` traces; and
one `ActivitySource` named after the application itself. Quartz is not on it, and could not be — the file is
generated before anyone knows what the project will reference.

So name it. Anywhere in the application's own startup will do:

<!-- snippet: sample_aspire_subscribe -->
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
```
<!-- endSnippet -->

Both constants are in `Quartz.Diagnostics` and both are `"Quartz"`. A second `AddOpenTelemetry()` composes
with the first rather than replacing it — OpenTelemetry keeps one `TracerProvider` and one `MeterProvider`
per container — so this can go before or after `AddServiceDefaults()`.

::: warning Do not add an exporter here
ServiceDefaults calls `UseOtlpExporter()` whenever `OTEL_EXPORTER_OTLP_ENDPOINT` is set, and the AppHost
sets it. That method may be called only once, and it cannot be combined with a signal-specific
`AddOtlpExporter()` — either mistake throws `NotSupportedException`. The
[Observability](../packages/opentelemetry-integration.md) page shows the same subscription *with* an
exporter, for an application that has no ServiceDefaults to inherit one from.
:::

That is the whole of the telemetry integration. Everything below is about the database, the health probe and
the logs.

## The AppHost

A Postgres server, a database on it, and the worker that uses them:

<!-- Not a compiled sample: `Aspire.Hosting.PostgreSQL` and the generated `Projects` class come from the
     AppHost project, neither of which this repository references. -->

```csharp
var builder = DistributedApplication.CreateBuilder(args);

var postgres = builder.AddPostgres("postgres")
    .WithDataVolume()
    .WithLifetime(ContainerLifetime.Persistent);

var quartzDb = postgres.AddDatabase("quartz");

builder.AddProject<Projects.Orders_Worker>("orders")
    .WithReference(quartzDb)
    .WaitFor(quartzDb);

builder.Build().Run();
```

`AddDatabase("quartz")` names both the Aspire resource and, because the second argument was left out, the
database itself. `WithReference` hands the worker that resource's connection string as the environment
variable `ConnectionStrings__quartz`, which the default configuration builder reads back as
`ConnectionStrings:quartz` — so the name in the AppHost is the name the worker asks for, on every path
below. `WaitFor` holds the worker until Postgres reports healthy, which spares the scheduler a first
connection attempt against a container that is still starting.

`WithDataVolume()` and `WithLifetime(ContainerLifetime.Persistent)` are worth having for a scheduler in
particular: without them every restart is a fresh database, and a persistent job store whose rows disappear
between runs is an in-memory store with more moving parts.

## The worker

Aspire's side is two calls:

<!-- Not a compiled sample: `AddServiceDefaults` comes from the generated ServiceDefaults project and
     `AddNpgsqlDataSource` from `Aspire.Npgsql`, neither of which this repository references. -->

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("quartz");
```

`AddServiceDefaults` is generic over `IHostApplicationBuilder`, so a worker gets it as readily as a web
application does. `AddNpgsqlDataSource("quartz")` reads the connection string that `WithReference` injected
and registers Npgsql's data source; among the services it registers is an unkeyed `DbDataSource`, which is
the one Quartz asks for.

Quartz's side is then a persistent store that takes its connections from the container:

<!-- snippet: sample_aspire_persistent_store -->
```csharp
builder.AddQuartz(q =>
{
    q.ConfigureScheduler(options =>
    {
        options.InstanceName = "orders";
        options.GenerateInstanceId = true;
    });

    q.UsePersistentStore(store =>
    {
        store.UsePostgres(db => db.UseRegisteredDataSource = true);
        store.UseClustering();
    });
});

builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

`UseRegisteredDataSource` is a setting on `DataSourceOptions` rather than a method, because it answers the
same question `ConnectionString` and `ConnectionStringName` answer — where connections come from — and it
wins over both. It resolves the container's one unkeyed `DbDataSource`. The dialect call is still needed and
is not redundant with it: `UsePostgres` selects `PostgreSQLDelegate` and the Npgsql driver description,
which decide the SQL and the parameter shape, while the data source decides the connection.

Whatever the data source was built with is in play for Quartz's own statements — its type mappers, its
logging, its connection multiplexing — because commands are made by the connection rather than from a driver
description. That is the practical reason to prefer this over a connection string when the application
already has an `NpgsqlDataSource`.

`GenerateInstanceId` and `UseClustering` are here because Aspire makes replicas cheap; see
[Clustering](../tutorial/advanced-enterprise-features.md) for what they mean. A single-node scheduler needs
neither.

### More than one database

An application that talks to two databases cannot have one unkeyed data source stand for both.
`AddKeyedNpgsqlDataSource("quartz")` registers its `DbDataSource` under that string as a service key rather
than unkeyed, and `DataSourceServiceKey` says which key is this store's. Setting it implies
`UseRegisteredDataSource`, so the two are never written together:

<!-- snippet: sample_aspire_keyed_data_source -->
```csharp
builder.AddQuartz(q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "quartz")));
```
<!-- endSnippet -->

### SQL Server takes the connection-string path

`Microsoft.Data.SqlClient` ships no `DbDataSource` implementation, and Aspire's SQL Server client
integration registers a scoped `SqlConnection` rather than a data source. `UseRegisteredDataSource` has
nothing to resolve there, and fails at first use rather than at startup validation. Read the injected
connection string by name instead — which needs no client integration at all, only `WithReference` in the
AppHost:

<!-- snippet: sample_aspire_sql_server -->
```csharp
builder.AddQuartz(q => q.UsePersistentStore(store =>
    store.UseSqlServer(db => db.ConnectionStringName = "quartz")));
```
<!-- endSnippet -->

The same shape works for Postgres, and is the right one whenever the application has no data source of its
own to share.

## The tables are still yours to create

Quartz does not create or migrate its schema, under Aspire or anywhere else — see
[Database Schema](../db/) for what has to exist and
[`database/tables`](https://github.com/quartznet/quartznet/tree/main/database/tables) for the scripts.
Aspire does not close that gap either. `AddDatabase` creates the *database* when the server becomes ready,
and the two hooks that look like they would run the DDL do not:

* `WithInitFiles` copies files into the container's `/docker-entrypoint-initdb.d`, which Postgres runs while
  first initializing its data directory — before the `AddDatabase` database exists, and against the server's
  default database.
* `WithCreationScript` runs against the server too. Its own documentation says it is for statements that
  apply to the default database, such as `CREATE DATABASE`, and that table creation is not supported
  "since they require a distinct connection to the newly created database".

What does work is the shape Aspire already teaches for Entity Framework Core migrations: a small project
that connects to the database, applies the schema and exits, which the worker waits for with
`WaitForCompletion`. Whatever applies your schema in production applies it here.

## Health

`AddQuartzHealthChecks` registers the scheduler's check with the same `IHealthChecksBuilder` that
ServiceDefaults put its own `self` check on, so `MapDefaultEndpoints()` serves both from `/health` with no
further wiring:

<!-- snippet: sample_aspire_health_checks -->
```csharp
builder.Services.AddQuartzHealthChecks();
```
<!-- endSnippet -->

It comes from the `Quartz.AspNetCore` package; see
[ASP.NET Core Integration](../packages/aspnet-core-integration.md). Point the AppHost at the endpoint to
have the result reach the dashboard — which means a web project, since the probe is an HTTP request:

<!-- Not a compiled sample: `WithHttpHealthCheck` is an `Aspire.Hosting` method. -->

```csharp
builder.AddProject<Projects.Orders_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithReference(quartzDb)
    .WaitFor(quartzDb);
```

The check reports the scheduler's own state, and a scheduler has more than two. What survives the trip to
the dashboard is less:

| Scheduler | Quartz reports | `/health` answers | Dashboard shows |
|---|---|---|---|
| Running, and its store answers | Healthy | 200 | Running |
| In standby | Degraded | **200** | Running |
| Running, but the store threw `SchedulerException` | Unhealthy | 503 | Running (Unhealthy) |
| Created but never started, shutting down, or shut down | Unhealthy | 503 | Running (Unhealthy) |

Standby is the interesting state and the one that gets lost. A scheduler deliberately put in standby is
alive, reachable and firing nothing: reporting it healthy would hide an application that never started its
scheduler, and reporting it unhealthy would take a node out of rotation for doing what it was told, so the
check reports degraded. But ASP.NET Core's health middleware maps `Degraded` to **200** by default, exactly
as it maps `Healthy`, and Aspire's `WithHttpHealthCheck` compares the response to a single status code and
reports anything else as unhealthy. So an HTTP probe cannot produce the dashboard's `Running (Degraded)`
rendering at all — a degraded scheduler simply looks healthy.

If that distinction matters, it has to be made deliberately. Mapping `Degraded` to 503 in
`HealthCheckOptions.ResultStatusCodes` puts a standby scheduler in the dashboard's unhealthy set, which is a
different claim from "degraded" but at least a visible one; the alternative is to read `/health`'s body,
where the default response writer already writes the aggregate status as text.

Three more limits worth knowing before you rely on any of this:

* **The Quartz check carries no tags**, so it is part of `/health` but not of `/alive`, whose predicate
  keeps only checks tagged `live`. That is the right default — a scheduler in standby should not fail a
  liveness probe — and `options.Tags.Add("live")` changes it if you disagree.
* **A worker project has no health endpoint at all.** `MapDefaultEndpoints` takes a `WebApplication`. Aspire
  has no documented recipe for exposing one from a non-web worker; the check is still registered and still
  resolvable from the container, but nothing serves it, so `WithHttpHealthCheck` has nothing to poll.
* **ServiceDefaults maps both endpoints only when `IsDevelopment()`**, on the grounds that exposing them
  elsewhere has security implications. Running under the AppHost locally is development, so this is a
  deployment concern rather than a local one — but a `WithHttpHealthCheck("/health")` that works on your
  machine will poll a 404 wherever that guard holds.

## Logs

Nothing. The container's logging *is* Quartz's logging: everything a scheduler is built from takes its
logger from the `ILoggerFactory` that built it — the scheduling loop, the job store with its cluster
manager, misfire handler, driver delegate and lock handler, and the thread pool, job factory, type loader
and instance id generator that `Use*<T>()` chose. ServiceDefaults has already put an OpenTelemetry logging
provider on that factory, so those lines reach the dashboard because the application configured logging at
all, not because it said anything about Quartz.

`LogProvider.SetLogProvider` is still in the API, and under Aspire it is worth writing for one narrower
reason: the handful of types no container ever builds. A listener or a trigger you constructed and handed
over, `CronTriggerImpl`, the static helpers such as `TimeZones`, and the jobs in `Quartz.Jobs` cannot be
injected anything, so they read the ambient factory instead of going unlogged. Forwarding the host's factory
to it once brings them onto the same pipeline as everything else. The
[migration guide](../migration-guide.md#the-ambient-logger-factory-stays-ambient) has the complete list and
the reason the slot stays ambient.

The scheduling loop also opens one logging scope for the lifetime of its run, carrying
`quartz.scheduler.name` and `quartz.scheduler.id` — the same attribute names the spans and the measurements
use. ServiceDefaults sets `IncludeScopes = true` on the OpenTelemetry logging provider, so those become
attributes on every line the loop writes, and the dashboard's advanced log filter can select on them. Job
log lines inherit the scope through the execution context their thread-pool dispatch captured, which is why
the loop opens it once rather than the run shell opening one per firing.

## What the dashboard shows

**Traces.** One `Quartz.Job.Execute` span per firing, `ActivityKind.Internal`, covering the whole fire; a
`Quartz.Job.Veto` span where a trigger listener said no. Selecting **View details** on a span opens a
property grid of its attributes, which is where `quartz.job.name`, `quartz.job.group`,
`quartz.trigger.name` and `quartz.trigger.group` appear. `quartz.scheduler.name`, `quartz.scheduler.id`,
`quartz.job.type` and `quartz.fire.instance.id` are added only when the span is sampled for full data, so a
sampler that records without collecting all data will not show them. A failed firing sets the span's status
to error, attaches an exception event and tags it `error.type` with the exception the job threw.

A persistent store adds one `Quartz.JobStore.*` span per operation —
`Quartz.JobStore.AcquireNextTriggers`, `.TriggersFired`, `.ScheduleJob` and the rest. The in-memory store
emits none of them, so a scheduler on `UseInMemoryStore` shows firings and nothing underneath.

**Metrics.** The `Quartz` meter appears in the Metrics page's selection pane with two instruments under it.
`quartz.job.execution.duration` is a histogram in **seconds**, which the dashboard charts as P50, P90 and
P99; `quartz.job.execution.active` is an up-down counter of jobs currently running. Both carry
`quartz.scheduler.name` and the four job and trigger identity attributes, and the histogram additionally
carries `error.type` on a failed execution — so the failure rate is the part of the histogram's count that
has that attribute, and it says which exception. The metric attributes are deliberately a smaller set than
the span attributes: `quartz.fire.instance.id` is unique per firing and has no business on a time series.

The full attribute and instrument tables are on the [Observability](../packages/opentelemetry-integration.md)
page rather than repeated here.

**Structured logs.** The loop's lines carry `quartz.scheduler.name`, which the advanced filter dialog can
select on — useful the moment a process runs more than one scheduler, since the logger category is a type
name and says nothing about which.

## What Aspire does not do for you

Aspire orchestrates processes and hands them configuration. It has no view of what a scheduler is, so
everything that makes one correct is still Quartz's to say:

* **Clustering.** `WithReplicas(2)` in the AppHost starts two copies of the worker; it does not make them a
  cluster. `UseClustering()` and a distinct `InstanceId` per node do — `GenerateInstanceId = true` derives
  one from host name and a timestamp, which stays distinct across replicas on one machine. Without it every
  replica keeps the default id, `NON_CLUSTERED`, and the id is how a node recognises its own check-in row
  and its own fired triggers.
* **The table prefix, the serializer, the misfire threshold and the lock strategy.** All store settings,
  all in [Configuration Reference](../configuration/reference.md#persistent-job-store).
* **Startup order beyond the container.** `WaitFor` waits on the database resource's health check, not on
  the Quartz tables existing. If the schema is applied by a migration step, the worker has to wait for
  that step, not for the database.
* **The dashboard's data.** Aspire's dashboard is a local OTLP viewer; it holds telemetry in memory for the
  session. It is not a place to look for last week's failed job. Point the same OTLP export at a collector
  for that — nothing about the Quartz side changes, because ServiceDefaults owns the exporter.

::: tip
None of this is Aspire-specific. `AddServiceDefaults()` is a project template you own, not a framework
call — everything on this page works in any generic-host application that configures OpenTelemetry, and the
[Observability](../packages/opentelemetry-integration.md) page is the version without Aspire in the way.
:::
