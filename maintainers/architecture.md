# Architecture

The scheduler loop, hosting and DI, serialization, observability and trimming. The domain model is in
`AGENTS.md`; job stores and the database are in [persistence.md](persistence.md).

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

## Scheduler Thread

`QuartzSchedulerThread` is the core scheduling loop. `JobRunShell` wraps job execution, handling exceptions and trigger completion. After `TriggersFired`, always use `TriggeredJobComplete` (not `ReleaseAcquiredTrigger`) to clean up — `ReleaseAcquiredTrigger` doesn't unblock sibling triggers for `[DisallowConcurrentExecution]` jobs.

## Hosting & DI

DI and hosting live in the core `Quartz` package, under `src/Quartz/Configuration/` and `src/Quartz/Hosting/`;
they are no longer separate `Quartz.Extensions.*` packages. Directory is not namespace here: the hosting types
are all in the `Quartz` namespace, and so are the `AddQuartz` extensions — `Quartz.Configuration` holds the
internals behind them.

- `IServiceCollection.AddQuartz()` — registers a scheduler's object graph. `AddQuartz(name, ...)`
  registers a named scheduler, whose parts are keyed by that name.
- `AddQuartzHostedService()` — `IHostedService` integration.
- `QuartzSchedulerBuilder` — builds a scheduler with no application container, by creating its own.
  `Create(Action<IQuartzBuilder>)` hands the callback the same `IQuartzBuilder` `AddQuartz` does, so
  there is one configuration API rather than two that resemble each other. The type re-declares none of
  it: it adds only `Create`, `Build()` / `BuildScheduler()`, `UseConfiguration` and `UseProperties` ×2.
  Do not give it a member that `IQuartzBuilder` or a `QuartzBuilderExtensions` extension already has —
  #3597 deleted a ~60-signature covariant facade that existed to do exactly that.
- `AddHealthChecks().AddQuartz()` / `IQuartzBuilder.AddQuartzHealthChecks()` — the scheduler health
  check, and the only two receivers it has: the ecosystem's idiom, and a scheduler's own builder, which
  knows the name so the caller need not repeat it. There is deliberately no
  `IServiceCollection.AddQuartzHealthChecks`; #3598 cut it as shorthand for the first. It is in
  `src/Quartz/Diagnostics/HealthChecks/`. It needs only `Microsoft.Extensions.Diagnostics.HealthChecks`,
  so it is core rather than `Quartz.AspNetCore`, whose `FrameworkReference` a `dotnet/runtime` image
  cannot satisfy (#3532).
- `Quartz.AspNetCore` — the HTTP API, and `MapHealthChecks` territory such as `ResultStatusCodes`.
- `Quartz.Aspire` — `builder.AddQuartzPersistentStore(connectionName)`, which turns an Aspire connection
  name into a persistent store, its telemetry subscriptions and its health check. It takes **no `Aspire.*`
  package dependency** — `IHostApplicationBuilder` is the whole contract — and contributes through
  `ConfigureAllQuartzSchedulers`, so it is order-independent with `AddQuartz`. Its hand-written
  `ConfigurationSchema.json` is held to `QuartzAspireSettings` by a test, because Aspire's generator for
  that file has never shipped.
- `Quartz.Dashboard.Host` — the dashboard as a program (4.8): the `quartz-dashboard` tool and the
  `ghcr.io/quartznet/quartz-dashboard` image, one project. Internal `Program`, no public API; its contract is
  the `Quartz:Dashboard` keys. It refuses to start on any mistake in them, logging `9402`, never logs a token,
  and is not trimmable (Blazor Server).
- `Quartz.Aspire.Hosting` — the AppHost resource that runs that image. It references `Aspire.Hosting` and
  **nothing of Quartz**: the environment variables it writes are the contract, and `Quartz.Tests.Aspire`
  feeds them to the host's own reader and the agent's.
- `UseDashboardAgent` reads `Quartz:DashboardAgent` (a named scheduler's own section first) through the
  binding generator, before any callback.

The container constructs the scheduler; there is no reflective instantiation from type-name strings, and
there is no properties-based `StdSchedulerFactory` any more. Legacy flat `quartz.*` keys are translated
to typed options and registrations by `QuartzPropertyBridge`, which is the only place that understands
them; `LegacyPropertyKeys` holds the key strings and rejects a misspelled one. A new setting is therefore
a typed option plus a bridge entry, never a string read somewhere else.

## Serialization

Pluggable serialization for job store persistence:

- `Quartz.Serialization.SystemTextJson` (built into core as `SystemTextJsonObjectSerializer`)
- `Quartz.Serialization.Newtonsoft`

## Observability

- `Quartz.Diagnostics` — spans on `QuartzActivitySource`, metrics on `Meters`.
- OpenTelemetry: `AddSource(QuartzInstrumentation.ActivitySourceName)`, `AddMeter(QuartzInstrumentation.MeterName)`; the contrib package emits nothing.
- Logging: `Microsoft.Extensions.Logging` via `LogProvider`, same namespace.

## Trimming and AOT

- **`Quartz` builds with the trim, AOT and single-file analyzers on**, so an `IL2xxx` or `IL3xxx` is an error.
  Nothing is recorded or suppressed: the last reflection sits behind the three switches in
  `src/Quartz/FeatureSwitches.cs`, which `PublishTrimmed` turns off, and the canary legs fail on any
  warning against a Quartz type. A new one is new reflection — fix it; that file's header gives the order
  to try fixes in. The package **does** say `IsAotCompatible`: Quartz produces no `IL3050` at all.
  `src/Quartz/Quartz.csproj` spells all of this out.
