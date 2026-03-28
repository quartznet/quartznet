# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Test Commands

- **Build:** `dotnet build Quartz.slnx` (solution uses modern `.slnx` format)
- **Full build (Nuke):** `build.cmd` (Windows) or `build.sh` (Linux/macOS)
- **Run all unit tests:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj`
- **Run single test:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj --filter "FullyQualifiedName~TestName"`
- **Target framework:** Use `-f net10.0` (or `net472` for .NET Framework; non-Windows only supports `net10.0`)
- **Integration tests:** `dotnet test src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj -f net10.0` (requires Docker for Testcontainers)
- **Nuke targets:** `Clean`, `Restore`, `Compile`, `UnitTest`, `IntegrationTest`, `Pack` (defined in `build/Build.cs`)
- **Warnings are errors** globally via `src/Directory.Build.props`

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

`JobStoreSupport` is the base class for persistent storage. Database-specific SQL delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `MySQLDelegate`, etc.) live in `Impl/AdoJobStore/`. Schema scripts are in `database/tables/`.

### Fluent builders

Jobs and triggers are created via `JobBuilder` and `TriggerBuilder` with schedule builders (`SimpleScheduleBuilder`, `CronScheduleBuilder`, `CalendarIntervalScheduleBuilder`, `DailyTimeIntervalScheduleBuilder`). `SchedulerBuilder` configures the scheduler itself.

### Trigger state management

After `TriggersFired`, always use `TriggeredJobComplete` (not `ReleaseAcquiredTrigger`) to clean up trigger state. `ReleaseAcquiredTrigger` doesn't unblock sibling triggers for `[DisallowConcurrentExecution]` jobs.

## Code Conventions

- **Async throughout:** All public APIs are `async Task` with `CancellationToken cancellationToken = default`. Always use `.ConfigureAwait(false)` on awaited calls (enforced by analyzer).
- **File-scoped namespaces:** The `SPI/` directory maps to `Quartz.Spi` namespace (note casing difference).
- **Nullable enabled** in library projects, disabled in test projects.
- **Explicit types preferred over `var`** per `.editorconfig`.
- **Allman brace style** — opening braces on new lines.
- **Test framework:** NUnit 4 with `FluentAssertions` and `FakeItEasy`. Legacy assert aliases via `GlobalUsings.cs`.
- **Multi-targeting:** Core library targets `net462`, `net472`, `net8.0`, `net9.0`, `net10.0`, `netstandard2.0`. Tests target `net10.0` and `net472`.
- **Strong naming:** Assemblies signed with `quartz.net.snk`.
- **Conditional compilation:** `REMOTING` defined for `net462`/`net472`; `DIAGNOSTICS_SOURCE` for everything except `net462`.
