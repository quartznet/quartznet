# Quartz.NET - Copilot Instructions

## Build & Test

- **Full build:** `build.cmd` (Windows) or `build.sh` (Linux/macOS) — builds everything and runs tests
- **Build solution:** `dotnet build Quartz.sln`
- **Run all unit tests:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj`
- **Run a single test:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj --filter "FullyQualifiedName~YourTestName"`
- **Target framework for tests:** Use `-f net10.0` (or `net472` for .NET Framework). On non-Windows, only `net10.0` is used.
- **Build system:** [Nuke](https://nuke.build/) — targets defined in `build/Build.cs`. Key targets: `Clean`, `Restore`, `Compile`, `UnitTest`, `IntegrationTest`, `Pack`.
- **Warnings are errors** — `TreatWarningsAsErrors` is enabled globally via `src/Directory.Build.props`.

## Architecture

Quartz.NET is a .NET port of the Java Quartz scheduler. The core scheduling loop lives in `QuartzSchedulerThread`, which acquires triggers from a job store, fires them, and delegates job execution to `JobRunShell`.

### Key abstractions (all in `src/Quartz/`)

| Concept | Interface | Key Implementations |
|---|---|---|
| Scheduler | `IScheduler` | `StdScheduler` → delegates to `QuartzScheduler` (in `Core/`) |
| Scheduler factory | `ISchedulerFactory` | `StdSchedulerFactory` (property-based config), `ServiceCollectionSchedulerFactory` (DI) |
| Job | `IJob` | User-implemented; single `Execute(IJobExecutionContext)` method returning `Task` |
| Trigger | `ITrigger` | `CronTriggerImpl`, `SimpleTriggerImpl`, `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` (all in `Impl/Triggers/`) |
| Job store | `IJobStore` (in `SPI/`) | `RAMJobStore` (in-memory, in `Simpl/`), `JobStoreTX`/`JobStoreCMT` (ADO.NET, in `Impl/AdoJobStore/`) |
| Thread pool | `IThreadPool` (in `SPI/`) | `DefaultThreadPool`, `DedicatedThreadPool` (in `Simpl/`) |

### Fluent builders

Jobs and triggers are created via `JobBuilder` and `TriggerBuilder` with schedule builders (`SimpleScheduleBuilder`, `CronScheduleBuilder`, `CalendarIntervalScheduleBuilder`, `DailyTimeIntervalScheduleBuilder`). `SchedulerBuilder` configures the scheduler itself.

### Extension packages

- `Quartz.Extensions.DependencyInjection` — `IServiceCollection.AddQuartz()` integration
- `Quartz.Extensions.Hosting` — `IHostedService` via `QuartzHostedService`
- `Quartz.AspNetCore` — ASP.NET Core health checks and startup integration
- `Quartz.Serialization.Json` / `Quartz.Serialization.SystemTextJson` — JSON serialization for job data
- `Quartz.Jobs` / `Quartz.Plugins` — built-in job and plugin implementations
- `Quartz.OpenTelemetry.Instrumentation` — OpenTelemetry support

### ADO.NET job store

`JobStoreSupport` is the large base class for persistent storage. Database-specific SQL delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `MySQLDelegate`, etc.) live in `Impl/AdoJobStore/`. Schema scripts are in `database/tables/`.

### Trigger state management

After `TriggersFired`, always use `TriggeredJobComplete` (not `ReleaseAcquiredTrigger`) to clean up trigger state. `ReleaseAcquiredTrigger` doesn't unblock sibling triggers for `[DisallowConcurrentExecution]` jobs.

## Conventions

- **Async throughout:** All public APIs are `async Task` with `CancellationToken cancellationToken = default` parameters. Always use `.ConfigureAwait(false)` on awaited calls (enforced by `ConfigureAwaitChecker.Analyzer`).
- **File-scoped namespaces:** e.g., `namespace Quartz.Core;` — the `SPI/` directory maps to `Quartz.Spi` namespace (note casing difference).
- **Nullable enabled** in library projects (`<Nullable>enable</Nullable>`), disabled in test project.
- **Explicit types preferred over `var`** per `.editorconfig`.
- **Allman brace style** — opening braces on new lines for methods, types, control blocks, etc.
- **Test framework:** NUnit 4 with `FluentAssertions` and `FakeItEasy`. Legacy assert aliases are set up via `GlobalUsings.cs`.
- **Integration test databases:** Provisioned via Testcontainers for .NET. Ensure Docker is running before executing integration tests.
- **Multi-targeting:** Core `Quartz` library targets `net462`, `net472`, `net8.0`, `net9.0`, `net10.0`, and `netstandard2.0`. Test project targets `net10.0` and `net472`.
- **Strong naming:** Assemblies are signed with `quartz.net.snk`.
- **Conditional compilation:** `REMOTING` is defined for `net462`/`net472`; `DIAGNOSTICS_SOURCE` for everything except `net462`.
