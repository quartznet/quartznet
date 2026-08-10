# Quartz.NET

Instructions for AI coding agents working in this repository.

This is the single source of truth. `AGENTS.md` is read directly by most agents — GitHub Copilot,
Codex, Cursor, Aider, Gemini CLI, Windsurf and others. Claude Code reads `CLAUDE.md`, which does
nothing but import this file, and `.github/copilot-instructions.md` is a pointer for the same
reason. **Edit this file; the other two should stay one-liners.**


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
- **`IJob`** → user-implemented interface with a single `ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)` method. The token is the *same* one as `IJobExecutionContext.CancellationToken`; it is a parameter so that `CA2016` can flag jobs that fail to forward it.
- **`IJobDetail`** → metadata about a job (type, key, JobDataMap). Built via `JobBuilder`.
- **`ITrigger`** → defines when a job fires (cron, simple interval, daily time interval, calendar interval). Built via `TriggerBuilder` + schedule builders (`CronScheduleBuilder`, `SimpleScheduleBuilder`, etc.).
- **`JobKey` / `TriggerKey`** → identity objects composed of name + group.

### Job Stores

- **`RAMJobStore`** (`Quartz.Impl`) — in-memory, volatile. Default.
- **`JobStoreSupport`** → `JobStoreTX` / `JobStoreCMT` (`Quartz.Impl.AdoJobStore`) — ADO.NET-based persistence with database-specific delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `OracleDelegate`, `MySQLDelegate`, `SQLiteDelegate`, `FirebirdDelegate`).

### Database scripts

- `database/tables/tables_<dialect>.sql` — fresh-install DDL, one per database.
- `database/migrations/<version>/<name>_<dialect>.sql` — schema changes, grouped by the Quartz.NET version that introduced them. One directly-runnable file per database; no commented-out dialect blocks. **Generated** — describe the change in `build/Build.DatabaseMigrations.Scripts.cs` and run `dotnet fallout GenerateMigrations`; never hand-edit the output. The `2.0` and `3.0` folders are the exception: hand-written, SQL Server-only historical scripts. `VerifyMigrations` runs in CI and fails when the two are out of step.
- `database/README.md` — the index: run order, per-version status, and old→new path mapping.

Rules when touching any of this:

- **`database/migrations/` and `database/README.md` must stay byte-identical on `main` and `3.x`.** A schema change lands on both branches in a companion PR pair, even when the feature itself is branch-specific — otherwise a documented path 404s on whichever branch lacks it, which is what #3218 reported. `database/tables/` is the *current* schema and differs by design.
- Every migration ships a file for **every** supported dialect (`sqlServer`, `postgres`, `mysql_innodb`, `oracle`, `sqlite`, `firebird`), guarded so it is safe to re-run. SQLite `ADD COLUMN` is the one exception — it has no conditional DDL.
- 4.x has no `Supports*Column` probes, so anything **optional on 3.x is required on 4.x**. Fold every 3.x column migration into `database/migrations/4.0/schema_30_to_40_upgrade_<dialect>.sql`.
- Adding a migration also needs a version section in `docs/documentation/database/schema-changes.md`. The docs site is built from `main` only, and it documents both the 3.x and 4.x trees.

### Scheduler Thread

`QuartzSchedulerThread` is the core scheduling loop. `JobRunShell` wraps job execution, handling exceptions and trigger completion. After `TriggersFired`, always use `TriggeredJobComplete` (not `ReleaseAcquiredTrigger`) to clean up — `ReleaseAcquiredTrigger` doesn't unblock sibling triggers for `[DisallowConcurrentExecution]` jobs.

### Hosting & DI

DI and hosting live in the core `Quartz` package, under `src/Quartz/Configuration/` and `src/Quartz/Hosting/`;
they are no longer separate `Quartz.Extensions.*` packages. Directory is not namespace here: the hosting types
are all in the `Quartz` namespace, and so are the `AddQuartz` extensions — `Quartz.Configuration` holds the
internals behind them.

- `IServiceCollection.AddQuartz()` — registers a scheduler's object graph. `AddQuartz(name, ...)`
  registers a named scheduler, whose parts are keyed by that name.
- `AddQuartzHostedService()` — `IHostedService` integration.
- `QuartzSchedulerBuilder` — builds a scheduler with no application container, by creating its own. It
  implements `IQuartzBuilder`, so there is one configuration API rather than two that resemble each
  other; it adds only `Build()` / `BuildScheduler()` and `UseProperties(NameValueCollection)`.
- `Quartz.AspNetCore` — ASP.NET Core health checks and HTTP API.

The container constructs the scheduler; there is no reflective instantiation from type-name strings, and
there is no properties-based `StdSchedulerFactory` any more. Legacy flat `quartz.*` keys are translated
to typed options and registrations by `QuartzPropertyBridge`, which is the only place that understands
them; `LegacyPropertyKeys` holds the key strings and rejects a misspelled one.

### Serialization

Pluggable serialization for job store persistence:
- `Quartz.Serialization.SystemTextJson` (built into core as `SystemTextJsonObjectSerializer`)
- `Quartz.Serialization.Newtonsoft`

### Observability

- `Quartz.Diagnostics` — `System.Diagnostics.Activity` support via `QuartzActivitySource`.
- For OpenTelemetry, use [OpenTelemetry.Instrumentation.Quartz](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz).
- Logging uses `Microsoft.Extensions.Logging` via `Quartz.Diagnostics.LogProvider`.

## Porting changes between 3.x and main

`3.x` is the maintenance branch and `main` is 4.x. A change written against one usually needs
relocating for the other. This maps where things moved; when a port does not compile, check here
before assuming the code is missing.

### Namespaces

| 3.x | main |
|-----|------|
| `Quartz.Spi` | `Quartz.Extensibility` |
| `Quartz.Simpl` | `Quartz.Impl` (merged into the one that already existed) |
| `Quartz.Extensions.DependencyInjection` | `Quartz.Configuration` in the core package (the `AddQuartz` extensions stay in `Quartz`) |
| `Quartz.Extensions.Hosting` | `src/Quartz/Hosting/` in the core package (types are in the `Quartz` namespace) |
| `Quartz.Serialization.SystemTextJson` | core package (`SystemTextJsonObjectSerializer`) |

Directory layout follows: `src/Quartz/SPI/` → `src/Quartz/Extensibility/`, `src/Quartz/Simpl/` →
`src/Quartz/Impl/`. String-typed configuration naming the old namespaces still resolves through a
fallback in `SimpleTypeLoadHelper`, with a warning.

### Contracts that changed shape

| 3.x | main |
|-----|------|
| `IJob.Execute(context)` | `Execute(context, cancellationToken)` — same token as `context.CancellationToken` |
| `IJobFactory.NewJob(...)` → `IJob` | `CreateJob(...)` → `ValueTask<JobScope>` |
| `IJobFactory.ReturnJob(IJob)` | `ReturnJob(JobScope, CancellationToken)` |
| `internal IJobWithAsyncReturnFactory` | gone — merged into `IJobFactory` |
| `IJobWrapper` | gone — per-fire state rides in `JobScope.State` |
| `PropertySettingJobFactory.InstantiateJob` (sync) | `CreateJobInstance` → `ValueTask<JobScope>` |
| `ITrigger.GetNextFireTimeUtc()` | `ITrigger.NextFireTimeUtc` (method kept as `[Obsolete]` forwarder) |
| `IOperableTrigger.SetNextFireTimeUtc(v)` | `NextFireTimeUtc = v` on `IMutableTrigger` (no forwarder) |
| `IThreadPool.RunInThread` / `BlockForAvailableThreads` | `TryRun` / `WaitForAvailableThreads`, both `ValueTask` |
| `IThreadPool.InstanceId` / `InstanceName` | removed — nothing read them |
| `IObjectSerializer.DeSerialize` | `Deserialize`; `Initialize()` gone (options built on first use) |
| `ITypeLoadHelper.Initialize()` | gone |
| `IInstanceIdGenerator` → `ValueTask<string?>` | `ValueTask<string>` |
| `IRemotableSchedulerProxyFactory` | `ISchedulerProxyFactory` |
| `ISchedulerListener.SchedulerShuttingdown` | `SchedulerShuttingDown` |
| `IListenerManager.GetSchedulerListeners()` → `IReadOnlyCollection<T>` | `ISchedulerListener[]` |
| `IJobStore.EstimatedTimeToReleaseAndAcquireTrigger` (`long` ms) | `TimeSpan` |
| two `IJobStore.AcquireNextTriggers` overloads | one, with optional `executionLimits` |
| `IScheduler`/`IJobStore` `GetJobKeys` / `GetTriggerKeys` | `QueryJobs` / `QueryTriggers` → `PagedResult<JobHeader\|TriggerHeader>` |
| `Get{Job,Trigger}GroupNames`, `GetPausedTriggerGroups`, `Is{Job,Trigger}GroupPaused` | `Query{Job,Trigger}Groups` with `JobGroupQuery`/`TriggerGroupQuery.Paused` |
| `GetCalendarNames` | `QueryCalendarNames(CalendarQuery)` |
| `IJobStore.GetNumberOf{Jobs,Triggers,Calendars}`, `CalendarExists` | a query with `Take = 0, IncludeTotalCount = true`; `RetrieveCalendar` non-null |
| the eight removed `IScheduler` listing members | back as extension methods on `SchedulerQueryExtensions` — old call shapes still compile, but a null matcher now throws |
| (new) | `IScheduler`/`IJobStore.GetJobDetails(keys)` / `GetTriggers(keys)` — bulk fetch by key |
| `IDriverDelegate.Select{Calendars,JobGroups(conn,ct),PausedTriggerGroups,Num*}` | `Select{CalendarNames,JobHeaders,TriggerHeaders,JobGroups,TriggerGroups}` taking a query record |
| three `SelectFiredTriggerRecords*` + four `DeleteFiredTriggers` overloads | one of each, taking `FiredTriggerQuery` |
| two `IDriverDelegate.SelectTriggerToAcquire` overloads | `SelectTriggersToAcquire(conn, TriggerAcquisitionCriteria, ct)` |
| `GetSelectNextMisfiredTriggersInStateToAcquireSql` + dialect overrides | gone; dialect paging is `ApplyPaging` / `AddPagingParameters` |
| `JobRunShell`, `IJobRunShellFactory` (public) | `internal` |

### Configuration

3.x configures from flat `quartz.*` strings and reflective instantiation. On main the container
builds the scheduler; flat keys still work but are translated by `QuartzPropertyBridge`, which is
the only place that understands them. A 3.x change that adds a property key needs a typed option
plus a bridge entry on main.

### Practical notes

- **Ported code that fails to build is usually a rename, not a missing feature.** Check the tables
  above and `docs/documentation/quartz-4.x/migration-guide.md`, which explains the reasoning for each.
- **`docs/documentation/quartz-3.x/` must keep the old names.** Only update `quartz-4.x/`.
- **`src/Quartz.Tests.Unit/Verify/PublicApiTest_*.verified.txt` are the public API baselines.**
  Any change to public API fails those tests; review the diff, and if the change is intended,
  accept the new baseline and carry the same diff into
  `docs/documentation/quartz-4.x/migration-guide.md`. Never hand-edit them.
- **`3.x` carries the same baselines, so the 4.0 API delta is a `git diff`.** Reach for it when
  writing the migration guide, reviewing API ergonomics, or checking whether a 3.x change has a
  counterpart here — it is exhaustive and always current, unlike the tables above. `git diff` takes
  `<rev>:<path>` blob arguments, so this is the same command in PowerShell and bash:

  ```shell
  git fetch origin

  # one assembly
  git diff origin/3.x:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt \
           origin/main:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt

  # everything both branches snapshot
  git diff origin/3.x origin/main -- src/Quartz.Tests.Unit/Verify src/Quartz.Tests.AspNetCore/Verify
  ```

  Package boundaries moved, so match the files up first:

  | 3.x baseline | main baseline |
  |---|---|
  | `PublicApiTest_Quartz` | `PublicApiTest_Quartz` — ours also absorbs DI, Hosting and SystemTextJson |
  | `PublicApiTest_Quartz.Extensions.DependencyInjection` | folded into `Quartz` (`Quartz.Configuration`) |
  | `PublicApiTest_Quartz.Extensions.Hosting` | folded into `Quartz` (`src/Quartz/Hosting/`) |
  | `PublicApiTest_Quartz.Serialization.SystemTextJson` | folded into `Quartz` (`SystemTextJsonObjectSerializer`) |
  | `PublicApiTest_Quartz.Serialization.Json` | `PublicApiTest_Quartz.Serialization.Newtonsoft` |
  | `Quartz.Jobs`, `Quartz.Plugins`, `Quartz.Plugins.TimeZoneConverter`, `Quartz.Extensions.Redis`, `Quartz.AspNetCore`, `Quartz.Dashboard` | same name on both sides |
  | `PublicApiTest_Quartz.OpenTracing` | dropped here |
  | (no 3.x baseline — its ancient `OpenTelemetry` dependency fails restore there) | dropped here; use `OpenTelemetry.Instrumentation.Quartz` |
  | — | `PublicApiTest_Quartz.HttpClient` — new here |

  Two differences are systematic and are **not** deltas worth reporting: `Task` → `ValueTask` on
  nearly every member, and the namespace moves tabulated above. 3.x snapshots on `net10.0` only, so
  its `net472`/`REMOTING` surface never appears in the diff.
- **Release notes live in GitHub releases, not in the repository.** There is no changelog file on
  either branch; the tag's release is the record. Unreleased 4.x notes accumulate in the `v4.0.0`
  draft release.

## Key Conventions

- **File-scoped namespaces** — enforced as error (`csharp_style_namespace_declarations = file_scoped:error`).
- **Explicit types over `var`** — prefer explicit types everywhere (`csharp_style_var_for_built_in_types = false`).
- **Nullable enabled** globally; test projects may disable it.
- **Warnings as errors** — `TreatWarningsAsErrors` is true; code style is enforced in build.
- **Allman brace style** — braces on new lines for methods, types, control blocks, properties, accessors, lambdas.
- **No `DateTime.Now`/`DateTimeOffset.Now`** — banned via Roslyn analyzer (`BannedSymbols.txt`). Use `TimeProvider` instead.
- **No implicit `DateTime` → `DateTimeOffset` cast** — also banned.
- **All public APIs return `ValueTask`** rather than `Task` (e.g., `IJob.Execute`, `IScheduler` methods). This
  holds for classes too. The single exception is `Quartz.Dashboard.Hubs.IQuartzDashboardHubClient`, whose shape
  SignalR dictates: its typed-client proxy only implements `Task`-returning members, and it is emitted into a
  dynamic assembly, so the interface and its DTOs cannot be internal either — a strong-named assembly can only
  grant `InternalsVisibleTo` to a friend it names by public key. `QuartzDashboardHubClientProxyTest` is the guard.
- **No `Async` suffix** on Quartz-authored members; the bare verb *is* the async one. Only names dictated by a BCL interface (`IHostedService`, `IAsyncDisposable`, `IHealthCheck`) carry it.
- **Every async member ends with `CancellationToken cancellationToken = default`.** There are no exceptions left.
- **Return concrete collection types, accept abstractions** — `List<T>` or `T[]` out, `IReadOnlyCollection<T>`/`IReadOnlyList<T>` in.
- **No setter-only properties on interfaces.** Identity and configuration arrive by constructor or an explicit context parameter.
- **Strong-named assemblies** — signed with `quartz.net.snk` (except examples).
- **Central package management** — package versions in `Directory.Packages.props`.
- **Single target** — everything targets `net10.0`.
- **SDK**: .NET 10 SDK (see `global.json`), with `rollForward: latestMinor`.
- **License headers** — source files include Apache 2.0 license region at the top.
