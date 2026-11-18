# Architecture

The scheduler loop, hosting and DI, serialization, observability and trimming. The domain model is in
`AGENTS.md`; job stores and the database are in [persistence.md](persistence.md).

`AGENTS.md` states each rule here in brief and routes to this document, which holds the full text
and wins where the two differ.

## Scheduler Thread

`QuartzSchedulerThread` is the core scheduling loop. `JobRunShell` wraps job execution, handling exceptions and trigger completion. After `TriggersFired`, always use `TriggeredJobComplete` (not `ReleaseAcquiredTrigger`) to clean up. `ReleaseAcquiredTrigger` gives back a reservation; it does not complete a firing. A spent trigger stays `COMPLETE`, and no job data is written. It lets go of a `[DisallowConcurrentExecution]` job's other triggers in one case only: the fire it releases committed, and no other firing of the job is executing. That is the ADO.NET store's fallback for a commit it could not confirm (#4259). The commit check comes first. It writes a trigger row of the commit it is checking, so a commit still in flight settles before the check reads. An acquisition is the node's own while its `ACQUIRED` fired row is there: a replace of the trigger deletes that row, the fire of a trigger acquired earlier answers "not fired" without it, and a release whose row is gone moves nothing (#4323).

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

## Target frameworks

- **Every project targets `net10.0`, and `Quartz` also targets `net11.0`** (#4341). The `net11.0` asset is
  compiled with `<Features>runtime-async=on</Features>`, so the runtime suspends and resumes its async methods
  with no compiler state machines. Runtime async is opt-in per assembly and needs `net11.0`. `Quartz` holds the
  whole fire path, so it is the one package that gains from it. The other packages stay `net10.0`, and a
  `net11.0` application runs them on `Quartz`'s `net11.0` asset, NuGet's nearest match.
- **What runtime async costs and gains, and why it stays on** (#4214; measured 2026-11-17 and 2026-11-18,
  kept on by the maintainer's decision of 2026-11-18). Against a state-machine build of the same source on
  .NET 11, the `net11.0` asset fires a job that completes synchronously at the same speed, within the spread, and
  with the same allocations. A job that suspends fires about 3 % faster and allocates about 450 B more per firing
  (+15 %), because runtime async allocates more when a firing suspends. The assembly is about 20 % smaller, and a
  job's `Execute` runs 11 frames deep instead of 17. It stays on because the time is what an application pays,
  and the extra bytes are collected within the measured time. Revisit it when the runtime changes what a
  suspension allocates, or when an 11.0.x defect in runtime async reaches Quartz. Turning it off means removing
  the `Features` line from `Quartz.csproj` and turning `RuntimeAsyncTest`'s assertion round.
  `-p:UseRuntimeAsync=false` doesn't do it, because the explicit `Features` wins. The numbers are in
  `src/Quartz.Benchmark/README.md`.
- **One source and one public API.** No call site has an `#if`. A call is written against the `net11.0` API,
  and what `net10.0` lacks is backfilled once as a C# 14 extension member: `JsonSerializerOptionsPolyfills`
  backfills `GetTypeInfo<T>()`, which CA2263 asks for on `net11.0`. `PublicApiTest_Quartz.verified.txt` holds
  both assets to one surface. There is no per-target baseline.
- **The internal surface is the same in both assets too.** Eight shipped packages see Quartz's internals
  (`AssemblyInfoExtras.cs`), compile against the `net10.0` asset, and in a `net11.0` application run on the
  `net11.0` one. An internal member only one asset has throws `MissingMethodException` on .NET 11 alone. So a
  backfill is never guarded on the member, only on its body. On `net11.0` the BCL's own member binds first at
  Quartz's call sites, and the backfill's body forwards to it. `JsonSerializerOptionsPolyfillsTest` names the
  member, so a build of the tests against either asset fails without it.
- **Dependencies.** The `Microsoft.Extensions.*` floors are 10.0.0 for both targets. .NET 11's shared
  framework carries nine of the abstractions, `Options`, `Primitives` and `Logging.Abstractions` among them.
  NuGet prunes those for `net11.0`, so the `net11.0` asset compiles against the framework's copies. No NU1510
  is raised. Pack leaves the pruned two, `Hosting.Abstractions` and `Logging.Abstractions`, out of the
  `net11.0` dependency group, and the `net10.0` group is unchanged.
- **Which asset a test loads.** `Quartz.Tests.Unit` is built for `net10.0`, or for `net11.0` when
  `UnitTestRuntime` is set, as on the .NET 11 leg ([testing.md](testing.md)). `RuntimeAsyncTest` fails when a
  run loads an asset other than the one its framework gets, and when `JobRunShell.Run` in the `net11.0` asset
  has a state machine.
- **Packaging.** `buildTransitive/net10.0/Quartz.targets` and the analyzer under `analyzers/dotnet/cs` serve
  both assets: NuGet picks the `net10.0` build folder as the nearest one for `net11.0`. docfx documents the
  `net10.0` asset.

## Trimming and AOT

- **`Quartz` builds with the trim, AOT and single-file analyzers on**, so an `IL2xxx` or `IL3xxx` is an error.
  Nothing is recorded or suppressed: the last reflection sits behind the three switches in
  `src/Quartz/FeatureSwitches.cs`, which `PublishTrimmed` turns off, and the canary legs fail on any
  warning against a Quartz type. A new one is new reflection — fix it; that file's header gives the order
  to try fixes in. The package **does** say `IsAotCompatible`: Quartz produces no `IL3050` at all.
  `src/Quartz/Quartz.csproj` spells all of this out.
- **Both assets are analyzed and published.** The analyzers run on each target. `PublishTrimmed` and
  `PublishAot` publish the canaries as `net10.0` applications from the repository. `Net11ConsumerSmoke`
  publishes them as `net11.0` applications from the packages, which is how the `net11.0` asset is published.
