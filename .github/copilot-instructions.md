# Copilot Instructions for Quartz.NET

## Build & Test

Build the solution (uses the [Fallout](https://fallout.build/) build system):

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

Integration tests require a running Docker daemon (containers are provisioned by Testcontainers for .NET) and are run via:

```shell
.\build.cmd Compile UnitTest IntegrationTest
```

The test framework is **NUnit** with **AwesomeAssertions** and **FakeItEasy** for mocking. Some tests use **Verify.NUnit** for snapshot testing.

### Assertions

**Use AwesomeAssertions (`.Should()`) rather than NUnit's `Assert`.** `AwesomeAssertions` is a
global using in the test projects, so no `using` is needed. It produces far better failure messages,
which is most of the value of a test that has just failed.

```csharp
// Preferred
scheduler.SchedulerName.Should().Be("core");
options.Context.Should().ContainKey("environment").WhoseValue.Should().Be("staging");
threadPool.Should().BeOfType<DefaultThreadPool>();
limits.Should().NotBeNull();
schedulers.Should().NotContain(x => x.IsShutdown);

// Reason strings explain *why*, and show up in the failure message
reporting.Should().NotBeSameAs(defaultStore,
    "each scheduler must own its job store, otherwise they share trigger state");

// Exceptions
var act = async () => await factory.GetScheduler();
await act.Should().ThrowAsync<SchedulerConfigException>()
    .WithMessage("*IdleWaitTime*");

// Avoid
Assert.That(scheduler.SchedulerName, Is.EqualTo("core"));
Assert.Multiple(() => { ... });
Assert.ThrowsAsync<SchedulerConfigException>(async () => await factory.GetScheduler());
```

`Assert.Multiple` is unnecessary — AwesomeAssertions reports each failed assertion, and
`AssertionScope` is available when several assertions really must be evaluated as a group.

NUnit's `Assert` still appears in older tests; leave it alone unless you are already editing that
code, and use AwesomeAssertions for anything new.

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

DI and hosting live in the core `Quartz` package (`Quartz/Configuration`, `Quartz/Hosting`); they are
no longer separate `Quartz.Extensions.*` packages.

- `IServiceCollection.AddQuartz()` — registers a scheduler's object graph. `AddQuartz(name, ...)`
  registers a named scheduler, whose parts are keyed by that name.
- `AddQuartzHostedService()` — `IHostedService` integration.
- `QuartzSchedulerBuilder` — builds a scheduler with no application container, by creating its own.
- `Quartz.AspNetCore` — ASP.NET Core health checks and HTTP API.

The container constructs the scheduler; there is no reflective instantiation from type-name strings.
Legacy flat `quartz.*` keys are translated to typed options and registrations by
`QuartzPropertyBridge`, which is the only place that understands them.

### Serialization

Pluggable serialization for job store persistence:
- `Quartz.Serialization.SystemTextJson` (built into core as `SystemTextJsonObjectSerializer`)
- `Quartz.Serialization.Newtonsoft`

### Observability

- `Quartz.Diagnostics` — `System.Diagnostics.Activity` support via `QuartzActivitySource`.
- For OpenTelemetry, use [OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz).
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
- **Single target** — everything targets `net10.0`.
- **SDK**: .NET 10 SDK (see `global.json`), with `rollForward: latestMinor`.
- **License headers** — source files include Apache 2.0 license region at the top.
