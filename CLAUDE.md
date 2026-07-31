# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

- **Build:** `dotnet build Quartz.slnx` (solution uses modern `.slnx` format)
- **Full build (Fallout):** `build.cmd` (Windows) or `build.sh` (Linux/macOS) — thin shims that restore the pinned Fallout CLI (`.config/dotnet-tools.json`) and forward to `dotnet fallout <targets>`
- **Run all unit tests:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj`
- **Run single test:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj --filter "FullyQualifiedName~TestName"`
- **Target framework:** Use `-f net10.0` (or `net472` for .NET Framework; non-Windows only supports `net10.0`)
- **Integration tests:** `dotnet test src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj -f net10.0` (requires Docker for Testcontainers)
- **Fallout targets:** `Clean`, `Restore`, `Compile`, `UnitTest`, `IntegrationTest`, `Pack`, `Publish` (defined in `build/Build.cs`)
- **Warnings are errors** globally via `src/Directory.Build.props`

## Documentation

The documentation website is built and published from the **`main`** branch only. The full Quartz 3.x docs live on `main` under `docs/documentation/quartz-3.x/`; this `3.x` branch deliberately does **not** contain the docs site.

The only documentation kept on this branch is the per-package NuGet README at `src/<Project>/README.md`, packed into each package via `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="\" />`. Each README is a compact, NuGet-rendered (CommonMark) mirror of the matching page under `docs/documentation/quartz-3.x/` on `main`. Keep the two consistent in substance — change one, change the other in a companion PR. Keep READMEs NuGet-friendly: no VuePress frontmatter / `:::` containers / components, absolute links only, concise, and link out to the full page on `main`.

## Architecture

Quartz.NET is a .NET port of the Java Quartz scheduler. The core scheduling loop lives in `QuartzSchedulerThread`, which acquires triggers from a job store, fires them, and delegates job execution to `JobRunShell` via `IThreadPool`.

### Key abstractions (all in `src/Quartz/`)

| Concept | Interface | Implementations |
|---|---|---|
| Scheduler | `IScheduler` | `StdScheduler` -> `QuartzScheduler` (in `Core/`) |
| Scheduler factory | `ISchedulerFactory` | `StdSchedulerFactory` (property config), `ServiceCollectionSchedulerFactory` (DI) |
| Job | `IJob` | User-implemented; single `Execute(IJobExecutionContext)` returning `Task` |
| Trigger | `ITrigger` | `CronTriggerImpl`, `SimpleTriggerImpl`, `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` (in `Impl/Triggers/`) |
| Job store | `IJobStore` (in `SPI/`) | `RAMJobStore` (in `Simpl/`), `JobStoreTX`/`JobStoreCMT` (in `Impl/AdoJobStore/`) |
| Thread pool | `IThreadPool` (in `SPI/`) | `DefaultThreadPool`, `DedicatedThreadPool` (in `Simpl/`) |

### Extension packages

- `Quartz.Extensions.DependencyInjection` — `IServiceCollection.AddQuartz()`
- `Quartz.Extensions.Hosting` — `IHostedService` via `QuartzHostedService`
- `Quartz.AspNetCore` — ASP.NET Core health checks and startup
- `Quartz.Serialization.Json` / `Quartz.Serialization.SystemTextJson` — JSON serialization for job data
- `Quartz.Jobs` / `Quartz.Plugins` — built-in job and plugin implementations
- `Quartz.OpenTelemetry.Instrumentation` — OpenTelemetry support

### ADO.NET job store

`JobStoreSupport` is the base class for persistent storage. Database-specific SQL delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `MySQLDelegate`, etc.) live in `Impl/AdoJobStore/`.

### Database scripts

- `database/tables/tables_<dialect>.sql` — fresh-install DDL, one per database.
- `database/migrations/<version>/<name>_<dialect>.sql` — schema changes, grouped by the Quartz.NET version that introduced them. One directly-runnable file per database; no commented-out dialect blocks. **Generated** — describe the change in `build/Build.DatabaseMigrations.Scripts.cs` and run `dotnet fallout GenerateMigrations`; never hand-edit the output. The `2.0` and `3.0` folders are the exception: hand-written, SQL Server-only historical scripts. `VerifyMigrations` runs in CI and fails when the two are out of step.
- `database/README.md` — the index: run order, per-version status, and old→new path mapping.

Rules when touching any of this:

- **`database/migrations/` and `database/README.md` must stay byte-identical on `3.x` and `main`.** A schema change lands on both branches in a companion PR pair, even when the feature itself is branch-specific — otherwise a documented path 404s on whichever branch lacks it, which is what #3218 reported. `database/tables/` is the *current* schema and differs by design.
- Every migration ships a file for **every** supported dialect (`sqlServer`, `postgres`, `mysql_innodb`, `oracle`, `sqlite`, `firebird`), guarded so it is safe to re-run. SQLite `ADD COLUMN` is the one exception — it has no conditional DDL.
- 4.x has no `Supports*Column` probes, so anything **optional on 3.x is required on 4.x**. Fold every 3.x column migration into `database/migrations/4.0/schema_30_to_40_upgrade_<dialect>.sql`.
- Adding a migration also needs a section on the schema-changes documentation page. The docs site lives on `main` only.

### Fluent builders

Jobs and triggers are created via `JobBuilder` and `TriggerBuilder` with schedule builders (`SimpleScheduleBuilder`, `CronScheduleBuilder`, `CalendarIntervalScheduleBuilder`, `DailyTimeIntervalScheduleBuilder`). `SchedulerBuilder` configures the scheduler itself.

### Trigger state management

After `TriggersFired`, always use `TriggeredJobComplete` (not `ReleaseAcquiredTrigger`) to clean up trigger state. `ReleaseAcquiredTrigger` doesn't unblock sibling triggers for `[DisallowConcurrentExecution]` jobs.

### Property-based configuration and `StdSchedulerFactory`

`StdSchedulerFactory` configures components (plugins, job stores, thread pools, etc.) by reading `quartz.*` properties and injecting values into public setters via `ObjectUtils.SetObjectProperties()` using reflection. The property key `quartz.plugin.myPlugin.someProperty` maps to a public `SomeProperty` setter on the plugin instance (case-insensitive match). This means any public settable property on a plugin, job store, or thread pool class is automatically configurable through the standard property system — no special registration needed. When adding configurable options to these components, expose them as public properties with sensible defaults rather than requiring constructor parameters or DI.

### Adding new delegate methods on the 3.x branch

`IDriverDelegate` is a public SPI interface — adding methods would break external implementations. Use the `INextVersionDelegate` pattern instead:

1. Add the method to `internal interface INextVersionDelegate` in `IDriverDelegate.cs`
2. Implement it in `StdAdoDelegate` (which already implements `INextVersionDelegate`)
3. In `JobStoreSupport`, check `if (Delegate is INextVersionDelegate nvd)` and call the new method, with an `else` fallback using existing `IDriverDelegate` methods

This gives built-in delegates the efficient path while keeping the public API stable. All methods on `INextVersionDelegate` should be promoted to `IDriverDelegate` in 4.x.

## Code Conventions

- **Async throughout:** All public APIs are `async Task` with `CancellationToken cancellationToken = default`. Always use `.ConfigureAwait(false)` on awaited calls (enforced by analyzer).
- **File-scoped namespaces:** The `SPI/` directory maps to `Quartz.Spi` namespace (note casing difference).
- **Nullable enabled** in library projects, disabled in test projects.
- **Explicit types preferred over `var`** per `.editorconfig`.
- **Allman brace style** — opening braces on new lines.
- **Test framework:** NUnit 4 with `FluentAssertions` and `FakeItEasy`. Legacy assert aliases via `GlobalUsings.cs`.
- **Test parallelization:** Unit tests run fixtures in parallel (`[assembly: Parallelizable(ParallelScope.Fixtures)]`). Fixtures with shared static state are marked `[NonParallelizable]`. Do **not** add `[TestFixture]` to test classes — NUnit 4 discovers them automatically. Only use `[TestFixture(...)]` with parameters for parameterized fixtures.
- **Multi-targeting:** Core library targets `net462`, `net472`, `net8.0`, `net9.0`, `net10.0`, `netstandard2.0`. Tests target `net10.0` and `net472`.
- **Strong naming:** Assemblies signed with `quartz.net.snk`.
- **Conditional compilation:** `REMOTING` defined for `net462`/`net472`; `DIAGNOSTICS_SOURCE` for everything except `net462`.
