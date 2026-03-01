# Copilot Instructions for Quartz.NET

## Overview

Quartz.NET is an enterprise job scheduling library for .NET. It provides a robust framework for scheduling jobs (tasks) to run at specific times or intervals. The library supports:
- Cron-based scheduling with flexible trigger types
- Persistent job stores (ADO.NET) and in-memory stores (RAM)
- Dependency injection integration for ASP.NET Core
- Clustering and high-availability scenarios
- Multiple database backends (SQL Server, PostgreSQL, MySQL, Oracle, SQLite, Firebird)

The project targets .NET 8.0 and .NET 9.0, uses C# with nullable reference types enabled, and follows strict code style conventions enforced at build time.

## Build & Test

Build the solution (uses [NUKE](https://nuke.build/) build system):

```shell
# Windows
build.cmd

# Linux/macOS
./build.sh
```

Run unit tests:

```shell
dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj
```

Run a single test by fully-qualified name:

```shell
dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj --filter "FullyQualifiedName~CronExpressionTest.TestIsSatisfiedBy"
```

Integration tests require Docker services (`docker compose up -d`) and are run via:

```shell
.\build.cmd Compile UnitTest IntegrationTest
```

The test framework is **NUnit** with **FluentAssertions** and **FakeItEasy** for mocking. Some tests use **Verify.NUnit** for snapshot testing.

**Note**: When running unit tests directly with `dotnet test`, do NOT use `--no-build` flag as the build output paths differ. The NUKE build system uses `/release/` paths while `dotnet test` uses `/debug/` paths.

Run with verbose output to see individual test results:

```shell
dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj -v n
```

Expected test results: ~1,270+ passing tests (some may be skipped in certain environments).

## Project Layout

The repository is organized as follows:

- **`src/Quartz/`** — Core scheduling library (multi-targeted to net8.0 and net9.0)
- **`src/Quartz.Extensions.DependencyInjection/`** — DI integration (part of Quartz project)
- **`src/Quartz.Extensions.Hosting/`** — IHostedService integration (part of Quartz project)
- **`src/Quartz.AspNetCore/`** — ASP.NET Core health checks
- **`src/Quartz.Jobs/`** — Built-in job implementations
- **`src/Quartz.Plugins/`** — Plugin framework and built-in plugins
- **`src/Quartz.Serialization.Newtonsoft/`** — JSON.NET serialization support
- **`src/Quartz.HttpClient/`** — HTTP client integration
- **`src/Quartz.Tests.Unit/`** — Unit tests (NUnit)
- **`src/Quartz.Tests.Integration/`** — Integration tests (require Docker)
- **`database/tables/`** — SQL schema definitions for supported databases
- **`docs/`** — Documentation site (separate versions for Quartz 1.x, 2.x, 3.x, 4.x)
- **`.github/workflows/`** — CI/CD pipelines (build.yml, pr-tests-*.yml, squad-*.yml)
- **`build/`** — NUKE build system implementation
- **`Directory.Build.props`** — Common MSBuild properties
- **`Directory.Packages.props`** — Central package version management
- **`global.json`** — .NET SDK version (10.0.100 with latestMinor rollForward)

### CI/CD Workflows

- **`build.yml`** — Main CI build on push/PR
- **`pr-tests-unit.yml`** — Unit test validation on PRs
- **`pr-tests-integration-*.yml`** — Integration tests (PostgreSQL, SQL Server, MySQL, etc.)
- **`squad-*.yml`** — Automated squad workflows (triage, labeling, release management)

The build enforces warnings as errors, so all warnings must be addressed before merging.

## Architecture

Quartz.NET is an enterprise job scheduling library. The core domain model:

- **`IScheduler`** → main entry point; schedules jobs with triggers. Implemented by `StdScheduler` which delegates to `QuartzScheduler`.
- **`IJob`** → user-implemented interface with a single `ValueTask Execute(IJobExecutionContext context)` method (no CancellationToken parameter).
- **`IJobDetail`** → metadata about a job (type, key, JobDataMap). Built via `JobBuilder`.
- **`ITrigger`** → defines when a job fires (cron, simple interval, daily time interval, calendar interval). Built via `TriggerBuilder` + schedule builders (`CronScheduleBuilder`, `SimpleScheduleBuilder`, etc.).
- **`JobKey` / `TriggerKey`** → identity objects composed of name + group.

### Job Stores

- **`RAMJobStore`** (`Quartz.Simpl`) — in-memory, volatile. Default.
- **`JobStoreSupport`** → `JobStoreTX` / `JobStoreCMT` (`Quartz.Impl.AdoJobStore`) — ADO.NET-based persistence with database-specific delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `OracleDelegate`, `MySQLDelegate`, `SQLiteDelegate`, `FirebirdDelegate`).

Database schemas live in `database/tables/`.

### Scheduler Thread

`QuartzSchedulerThread` is the core scheduling loop. `JobRunShell` wraps job execution, handling exceptions and trigger completion. After `TriggersFired`, always use `TriggeredJobComplete` (not `ReleaseAcquiredTrigger`) to clean up — `ReleaseAcquiredTrigger` doesn't unblock sibling triggers for `[DisallowConcurrentExecution]` jobs.

### Hosting & DI

- `Quartz.Extensions.DependencyInjection` — `IServiceCollection.AddQuartz()` configuration.
- `Quartz.Extensions.Hosting` — `AddQuartzHostedService()` for `IHostedService` integration.
- `Quartz.AspNetCore` — ASP.NET Core health checks integration.

### Serialization

Pluggable serialization for job store persistence:
- `Quartz.Serialization.SystemTextJson` (built into core as `SystemTextJsonObjectSerializer`)
- `Quartz.Serialization.Newtonsoft`

### Observability

- `Quartz.Diagnostics` — `System.Diagnostics.Activity` support via `QuartzActivitySource`.
- `Quartz.OpenTelemetry.Instrumentation` — **OBSOLETE** (incompatible with .NET 10+). Use the official `OpenTelemetry.Instrumentation.Quartz` package from NuGet instead.
- Logging uses `Microsoft.Extensions.Logging` via `Quartz.Diagnostics.LogProvider`.

## Key Conventions

- **File-scoped namespaces** — enforced as error (`csharp_style_namespace_declarations = file_scoped:error`).
- **Explicit types over `var`** — prefer explicit types everywhere (`csharp_style_var_for_built_in_types = false`).
- **Nullable enabled** globally; test projects may disable it.
- **Warnings as errors** — `TreatWarningsAsErrors` is true; code style is enforced in build.
- **Allman brace style** — braces on new lines for methods, types, control blocks, properties, accessors, lambdas.
- **No `DateTime.Now`/`DateTimeOffset.Now`** — banned via Roslyn analyzer (`BannedSymbols.txt`). Use `TimeProvider` instead.
- **No implicit `DateTime` → `DateTimeOffset` cast** — also banned.
- **All public APIs return `ValueTask`** rather than `Task` (e.g., `IJob.Execute`, `IScheduler` methods).
- **Strong-named assemblies** — signed with `quartz.net.snk` (except examples).
- **Central package management** — package versions in `Directory.Packages.props`.
- **Multi-targeting** — core library targets `net8.0;net9.0`. Tests target `net8.0`.
- **SDK**: .NET 10 SDK (see `global.json`), with `rollForward: latestMinor`.
- **License headers** — source files include Apache 2.0 license region at the top.
