---
title: 'Running Quartz under Aspire'
---

# Running Quartz under Aspire

[Aspire](https://aspire.dev/) — formerly ".NET Aspire", renamed in Aspire 13 — is an orchestrator for a
local application and the shared configuration that goes with it. Two of its pieces matter to a scheduler:
the **AppHost**, which declares the databases and projects and wires them to each other, and
**ServiceDefaults**, a project every service references that turns on OpenTelemetry, health checks, service
discovery and HTTP resilience in one call.

[`Quartz.Aspire`](https://www.nuget.org/packages/Quartz.Aspire) is the client integration that joins them to
a scheduler, and the whole of it is one call:

<!-- snippet: sample_aspire_add_persistent_store -->
```csharp
builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

This page is what that line does, and what is still yours after it: the AppHost that declares the database,
the health probe, the schema, and what the dashboard ends up showing. Every rule the call decides something
by — the settings, the provider table, the connection ladder — is on the
[Aspire Integration](../packages/aspire.md) reference page instead of repeated here. The version documented
against is **Aspire 13.5**.

There is no *hosting* integration — no `Aspire.Hosting.Quartz` package, no `AddQuartz` resource for the
AppHost. Everything below is ordinary Quartz configuration in an application that Aspire happens to be
running.

## What the package subscribes for you

`AddServiceDefaults()` builds an OpenTelemetry pipeline and then exports it. What the template subscribes to
is a fixed list: runtime, ASP.NET Core and `HttpClient` metrics; ASP.NET Core and `HttpClient` traces; and
one `ActivitySource` named after the application itself. Quartz is not on it, and could not be — the file is
generated before anyone knows what the project will reference.

`AddQuartzPersistentStore` names it, so nothing on this page has to. It calls `AddOpenTelemetry()` and adds
Quartz's activity source and meter to it — both constants are in `Quartz.Diagnostics` and both are
`"Quartz"` — and it registers the scheduler's health check on the same `IHealthChecksBuilder` ServiceDefaults
put its own `self` check on. A second `AddOpenTelemetry()` composes with the first rather than replacing it,
since OpenTelemetry keeps one `TracerProvider` and one `MeterProvider` per container, so the call works
before or after `AddServiceDefaults()`.

An application that turns those off with `DisableTracing` and `DisableMetrics`, or that has no `Quartz.Aspire`
reference at all, writes the same subscription by hand:

<!-- snippet: sample_aspire_subscribe -->
```csharp
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
```
<!-- endSnippet -->

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

Aspire's two calls, and then Quartz's:

<!-- Not a compiled sample: `AddServiceDefaults` comes from the generated ServiceDefaults project and
     `AddNpgsqlDataSource` from `Aspire.Npgsql`, neither of which this repository references. -->

```csharp
var builder = Host.CreateApplicationBuilder(args);

builder.AddServiceDefaults();
builder.AddNpgsqlDataSource("quartz");

builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```

`AddServiceDefaults` is generic over `IHostApplicationBuilder`, so a worker gets it as readily as a web
application does. `AddNpgsqlDataSource("quartz")` reads the connection string that `WithReference` injected
and registers Npgsql's data source; among the services it registers is an unkeyed `DbDataSource`, and that
is the one `AddQuartzPersistentStore` looks for.

The order of those last three lines does not matter — the store is contributed to every scheduler in the
container, so it can be named before or after `AddQuartz`. The order of the *data source* does:
`AddQuartzPersistentStore` decides where connections come from against the services registered so far, so a
data source registered afterwards is one it never sees. Register it first, as Aspire's own samples do.

::: tip A working copy of all of this
`src/Quartz.Examples.Aspire.AppHost` and `src/Quartz.Examples.Aspire.Worker` in the
[Quartz.NET repository](https://github.com/quartznet/quartznet/tree/main/src) are this page as two projects
that build. They are in the solution, so a call on this page that stops compiling fails the build.
:::

### What the call chose, and how to choose it yourself

Nothing above is special to `Quartz.Aspire`. It is ordinary Quartz configuration, and an application that
wants to say part of it itself still can:

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
already has an `NpgsqlDataSource`, and it is why the package prefers it too.

`GenerateInstanceId` and `UseClustering` are here because Aspire makes replicas cheap; see
[Clustering](../tutorial/advanced-enterprise-features.md) for what they mean. A single-node scheduler needs
neither. `settings.Clustered = true` is the one-line form, and it sets both — a cluster whose nodes all keep
the default `NON_CLUSTERED` id is not a cluster.

### More than one database

An application that talks to two databases cannot have one unkeyed data source stand for both.
`AddKeyedNpgsqlDataSource("quartz")` registers its `DbDataSource` under that string as a service key rather
than unkeyed, and the package finds it there: `AddQuartzPersistentStore("quartz")` looks for a keyed data
source under `"quartz"` before it looks for an unkeyed one. Naming the resource once in the AppHost is the
whole of the wiring.

Written by hand, the keyed form is `DataSourceServiceKey`. Setting it implies `UseRegisteredDataSource`, so
the two are never written together:

<!-- snippet: sample_aspire_keyed_data_source -->
```csharp
builder.AddQuartz(q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "quartz")));
```
<!-- endSnippet -->

Two databases usually means two schedulers, since one scheduler has one store.
`QuartzAspireSettings.SchedulerName` is how a second `AddQuartzPersistentStore` says which scheduler it is
for; see [More than one scheduler](../packages/aspire.md#more-than-one-scheduler).

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

`AddQuartzPersistentStore` does exactly this whenever the database turns out to be SQL Server, and skips the
data-source probe entirely — otherwise it would resolve some other database's data source, or nothing at
all, and only find out at the first query. The same shape works for Postgres, and is the right one whenever
the application has no data source of its own to share.

## Getting the tables there

Quartz never migrates its schema, and under Aspire as anywhere else the fresh-install scripts in
[`database/tables`](https://github.com/quartznet/quartznet/tree/main/database/tables) are what a
production database runs — see [Database Schema](../db/) for what has to exist. What is new in 4.0 is
that a *development* database need not: `store.ProvisionSchema()` has the store create whatever is
missing as it starts, which is exactly the case an AppHost tearing a Postgres container up and down
represents. `AddQuartzPersistentStore` asks for it on your behalf, reading `builder.Environment` at the
call: `CreateIfMissing` under `Development`, and the `Validate` default in every other environment,
where the account the scheduler connects with usually cannot run DDL and is right not to be able to.
[Creating the schema](../tutorial/job-stores.md#creating-the-schema) has the setting and its limits.

`QuartzAspireSettings.ProvisionSchema` says it outright when the environment is not the right thing to
ask — `true` creates in every environment, and `false` in none:

<!-- snippet: sample_aspire_provision_schema -->
```csharp
builder.AddQuartzPersistentStore("quartz", settings => settings.ProvisionSchema = true);
```
<!-- endSnippet -->

The store still has the last word. A `SchemaProvisioning` the application set itself — through
`ConfigureStore`, or through `Quartz:JobStore:SchemaProvisioning` — is a decision about this store in
particular, and this call fills the gap rather than overruling it:

<!-- snippet: sample_aspire_schema_by_hand -->
```csharp
builder.AddQuartz(q => q.UsePersistentStore(store =>
    store.ConfigureStore(options => options.SchemaProvisioning = SchemaProvisioning.None)));
```
<!-- endSnippet -->

The one position that cannot be said that way is `Validate`, which is what an unconfigured store already
holds and so cannot be told apart from having said nothing. `ProvisionSchema = false` is how to say it.

That leaves the production question, and Aspire's own hooks mostly do not close it. `AddDatabase` does
create the *database* — on the server resource's `ResourceReadyEvent`, once its health check passes —
but the two hooks that look like they would then run the DDL are narrower than they appear:

* `WithCreationScript` runs one command against the **server's** default database, on a connection whose
  `Database=postgres`. Its own documentation says it is for statements that apply to that database, such as
  `CREATE DATABASE`, and that table creation is not supported "since they require a distinct connection to
  the newly created database". A script that fails for any reason other than "database already exists" is
  logged rather than thrown, so a broken one leaves the AppHost green and the tables missing.
* `WithInitFiles` copies files into the container's `/docker-entrypoint-initdb.d`, which the Postgres image
  runs while first initializing its data directory — before Aspire's `CREATE DATABASE` runs, and inside
  whatever `POSTGRES_DB` names. It *can* apply a schema, if you set `POSTGRES_DB` to the same string you
  pass to `AddDatabase` so that the two are one database. The catch is the word *first*: with
  `WithDataVolume()`, which a scheduler wants, the scripts run once and never again, so the schema is frozen
  at whatever the first run created.

What works whatever the database is, and whatever changes next, is the shape Aspire already teaches for
[Entity Framework Core migrations](https://aspire.dev/integrations/databases/efcore/migrations/): a small
project that connects to the database, applies the schema and exits, which the worker waits for with
`WaitForCompletion`. Whatever applies your schema in production applies it here. (Aspire's newer
`AddEFMigrations` shortcut collapses that project into one AppHost call, but it shells out to
`dotnet ef database update`, so it has nothing to offer a hand-written `tables_postgres.sql`.)

<!-- Not a compiled sample: `Aspire.Hosting` and the generated `Projects` class come from the AppHost
     project, which this repository does not reference. -->

```csharp
var migrations = builder.AddProject<Projects.Orders_Migrations>("migrations")
    .WithReference(quartzDb)
    .WaitFor(quartzDb);

builder.AddProject<Projects.Orders_Worker>("orders")
    .WithReference(quartzDb)
    .WaitForCompletion(migrations);
```

`WaitFor` is not enough on its own: it waits on the database resource's health check, which says the server
is up, not that `QRTZ_TRIGGERS` exists. `WaitForCompletion` waits for the migration project to exit, which
is the thing the scheduler actually depends on.

Provisioning does not replace that recipe in production, for the two reasons it is opt-in everywhere:
the scheduler's account would need permission to create tables, and creating a missing schema is not the
same as moving an existing one forward. `database/migrations/` is still what does that, and the 3.x →
4.0 upgrade is still mandatory.

## Health

`AddQuartzPersistentStore` registers the scheduler's check with the same `IHealthChecksBuilder` that
ServiceDefaults put its own `self` check on, so `MapDefaultEndpoints()` serves both from `/health` with no
further wiring. An application that set `DisableHealthChecks`, or that has no `Quartz.Aspire` reference,
registers the same check from the core package:

<!-- snippet: sample_aspire_health_checks -->
```csharp
builder.Services.AddQuartzHealthChecks();
```
<!-- endSnippet -->

It comes from the core `Quartz` package, so a worker needs no web reference to register it; see
[Hosted Services Integration](../packages/hosted-services-integration.md#health-checks). Point the AppHost
at the endpoint to have the result reach the dashboard — which means the project has to serve HTTP, since
the probe is an HTTP request:

<!-- Not a compiled sample: `WithHttpHealthCheck` is an `Aspire.Hosting` method. -->

```csharp
builder.AddProject<Projects.Orders_Api>("api")
    .WithHttpHealthCheck("/health")
    .WithReference(quartzDb)
    .WaitFor(quartzDb);
```

`WithHttpHealthCheck` needs the resource to have an `http` or `https` endpoint and throws while the AppHost
is being built when it does not — so a project with no launch profile and no `WithHttpEndpoint()` fails at
that line rather than at the first probe.

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
check reports degraded. Two things then flatten it. ASP.NET Core's health middleware maps `Degraded` to
**200** by default, exactly as it maps `Healthy`; and Aspire's `WithHttpHealthCheck` compares the response
to a single status code — 200 unless you pass another — by exact equality, reporting anything else as
unhealthy. Its probe has no third answer to give. So an HTTP probe cannot produce the dashboard's
`Running (Degraded)` rendering at all, whatever you map: a degraded scheduler looks healthy, and a
degraded scheduler you have made visible looks unhealthy.

If that distinction matters, it has to be made deliberately. Mapping `Degraded` to 503 puts a standby
scheduler in the dashboard's unhealthy set, which is a different claim from "degraded" but at least a
visible one:

<!-- snippet: sample_aspire_degraded_is_503 -->
```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResultStatusCodes =
    {
        [HealthStatus.Healthy] = StatusCodes.Status200OK,
        [HealthStatus.Degraded] = StatusCodes.Status503ServiceUnavailable,
        [HealthStatus.Unhealthy] = StatusCodes.Status503ServiceUnavailable,
    },
});
```
<!-- endSnippet -->

The alternative is to read `/health`'s body, where the default response writer already writes the aggregate
status as text. `Quartz.Aspire` maps nothing itself: `ResultStatusCodes` is a decision about this
application's probe, and an ASP.NET Core type a client integration has no business reaching for.

Three more limits are worth knowing before you rely on any of this.

### The check carries no tags

So it is part of `/health` but not of `/alive`, whose predicate keeps only checks tagged `live`. That is the
right default — a scheduler in standby should not fail a liveness probe. To disagree, configure the options
the registration reads, rather than registering the check a second time:

<!-- snippet: sample_aspire_health_check_tags -->
```csharp
builder.Services.Configure<QuartzHealthCheckOptions>(options => options.Tags.Add("live"));
```
<!-- endSnippet -->

The registration is built when the health-check service is, and it reads
`IOptionsMonitor<QuartzHealthCheckOptions>` at that moment — so a `Configure` call anywhere in startup
reaches it, including the registration `AddQuartzPersistentStore` made. A named scheduler's check reads the
options registered under that scheduler's name, so configure `QuartzHealthCheckOptions` by name for one of
those.

### A worker project has no health endpoint at all

`MapDefaultEndpoints` takes a `WebApplication`, and a `Microsoft.NET.Sdk.Worker` project has no HTTP server
to give it. The check is still registered and still resolvable from the container, but nothing serves it, so
`WithHttpHealthCheck` has nothing to poll — Aspire's own worker samples, its migration service included,
call `AddServiceDefaults()` and no `MapDefaultEndpoints()` for exactly this reason.

Aspire documents no recipe for closing that, so this one is ours rather than theirs. It is a project-file
change rather than a Quartz one: switch the SDK to `Microsoft.NET.Sdk.Web`, start from
`WebApplication.CreateBuilder`, and call `MapDefaultEndpoints()`. The application still hosts nothing but
the scheduler.

<!-- snippet: sample_aspire_health_endpoint -->
```csharp
WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

WebApplication app = builder.Build();

app.MapHealthChecks("/health");

app.Run();
```
<!-- endSnippet -->

`MapDefaultEndpoints()` is the ServiceDefaults call that maps `/health` and `/alive` together; the single
`MapHealthChecks` above is what it does for the first of them. Aspire's lead gave the same answer when this
was asked on [microsoft/aspire#4045](https://github.com/microsoft/aspire/issues/4045) — "they're ultimately
going to need to have to host a web app anyway" — and the issue was closed without a code change.

If hosting Kestrel for one endpoint is more than you want, the other way round is a custom `IHealthCheck`
registered in the **AppHost** and attached with `.WithHealthCheck("quartz-ready")`. It queries the database
directly, needs nothing of the worker, and is the only route that can report a resource as `Degraded` at
all.

### ServiceDefaults maps both endpoints only in development

`MapDefaultEndpoints` guards its two `MapHealthChecks` calls with `IsDevelopment()`, on the grounds that
exposing them elsewhere has security implications. Running under the AppHost locally is development, so this
is a deployment concern rather than a local one — but a `WithHttpHealthCheck("/health")` that works on your
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

Underneath the firings sits one `Quartz.JobStore.*` span per store operation —
`Quartz.JobStore.AcquireNextTriggers`, `.TriggersFired`, `.ScheduleJob` and the rest, all
`ActivityKind.Client`. Whichever store the scheduler was built with produces them, in-memory included, so
the acquisition round that preceded a firing is always there to select.

**Metrics.** The `Quartz` meter appears in the Metrics page's selection pane.
`quartz.job.execution.duration` is a histogram in **seconds**, which the dashboard charts as P50, P90 and
P99; `quartz.job.execution.active` is an up-down counter of jobs currently running. Both carry
`quartz.scheduler.name`, `quartz.scheduler.id` and the four job and trigger identity attributes, and the
histogram additionally carries `error.type` on a failed execution — so the failure rate is the part of the
histogram's count that has that attribute, and it says which exception. The metric attributes are
deliberately a smaller set than the span attributes: `quartz.fire.instance.id` is unique per firing and has
no business on a time series.

Beside them are `quartz.trigger.misfire`, `quartz.trigger.acquisition.duration`, `quartz.trigger.acquired`
and `quartz.jobstore.operation.duration`, and — on a clustered persistent store —
`quartz.cluster.checkin.duration` and `quartz.cluster.recovery.trigger`. Every one of them carries
`quartz.scheduler.id`, so a dashboard filtered to one node reads that node alone.

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
  and its own fired triggers. `QuartzAspireSettings.Clustered` is the setting that says both at once.
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
`Quartz.Aspire` itself takes no `Aspire.*` reference, so `AddQuartzPersistentStore` works anywhere a
`ConnectionStrings:` entry does.
:::

## See also

* [Aspire Integration](../packages/aspire.md) — every setting, the provider-inference table, and the
  connection ladder in full
* [Observability](../packages/opentelemetry-integration.md) — the spans, instruments and attributes
* [Operations](../operations.md) — the health check outside Aspire, and what it does and does not assert
