# Quartz.NET

Instructions for AI coding agents working in this repository.

This is the single source of truth. `AGENTS.md` is read directly by most agents — GitHub Copilot,
Codex, Cursor, Aider, Gemini CLI, Windsurf and others. Claude Code reads `CLAUDE.md`, which does
nothing but import this file, and `.github/copilot-instructions.md` is a pointer for the same
reason. **Edit this file; the other two should stay one-liners.**

## Build & Test Commands

- **Build:** `dotnet build Quartz.slnx` (solution uses modern `.slnx` format)
- **Full build (Fallout):** `build.cmd` (Windows) or `build.sh` (Linux/macOS) — thin shims that restore the pinned Fallout CLI (`.config/dotnet-tools.json`) and forward to `dotnet fallout <targets>`
- **Run all unit tests:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj`
- **Run single test:** `dotnet test src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj --filter "FullyQualifiedName~TestName"`
- **Target framework:** Use `-f net10.0` (or `net472` for .NET Framework; non-Windows only supports `net10.0`)
- **Integration tests:** `dotnet test src/Quartz.Tests.Integration/Quartz.Tests.Integration.csproj -f net10.0` (databases are provisioned by Testcontainers for .NET, so Docker must be running)
- **Fallout targets:** `Clean`, `Restore`, `Compile`, `UnitTest`, `IntegrationTest`, `Pack`, `ApiDoc` (`build/Build.cs`), `Publish` (`build/Build.Publish.cs`), `GenerateMigrations` / `VerifyMigrations` (`build/Build.DatabaseMigrations.cs`)
- **Warnings are errors** globally via `src/Directory.Build.props`

### Assertions

**Use AwesomeAssertions (`.Should()`) rather than NUnit's `Assert`.** `AwesomeAssertions` is a global
using in the test projects, so no `using` is needed. It produces far better failure messages, which is
most of the value of a test that has just failed.

```csharp
// Preferred
scheduler.SchedulerName.Should().Be("core");
threadPool.Should().BeOfType<DefaultThreadPool>();

// Reason strings explain *why*, and show up in the failure message
reporting.Should().NotBeSameAs(defaultStore,
    "each scheduler must own its job store, otherwise they share trigger state");

// Exceptions
var act = async () => await factory.GetScheduler();
await act.Should().ThrowAsync<SchedulerConfigException>().WithMessage("*IdleWaitTime*");
```

`GlobalUsings.cs` aliases `Assert` to `NUnit.Framework.Legacy.ClassicAssert`, so there is no
`Assert.That` on this branch. NUnit's `Assert` still appears in older tests; leave it alone unless you
are already editing that code, and use AwesomeAssertions for anything new.

### Release notes

There is no changelog file on either branch — the tag's GitHub release is the record.

## Documentation

The documentation website is built and published from the **`main`** branch only. The full Quartz 3.x docs live on `main` under `docs/documentation/quartz-3.x/`; this `3.x` branch deliberately does **not** contain the docs site.

The only documentation kept on this branch is the per-package NuGet README at `src/<Project>/README.md`, packed into each package via `<PackageReadmeFile>README.md</PackageReadmeFile>` + `<None Include="README.md" Pack="true" PackagePath="\" />`. Each README is a compact, NuGet-rendered (CommonMark) mirror of the matching page under `docs/documentation/quartz-3.x/` on `main`. Keep the two consistent in substance — change one, change the other in a companion PR. Keep READMEs NuGet-friendly: no VuePress frontmatter / `:::` containers / components, absolute links only, concise, and link out to the full page on `main`.

## Public API baselines

`src/Quartz.Tests.Unit/PublicApiTest.cs` and `src/Quartz.Tests.AspNetCore/PublicApiTest.cs` snapshot the public surface of every packable assembly with [PublicApiGenerator](https://github.com/PublicApiGenerator/PublicApiGenerator) + Verify. The snapshots live next to each test project as `Verify/PublicApiTest_<AssemblyName>.verified.txt` and ride along in the normal `UnitTest` run — no separate target.

- **Never hand-edit a `.verified.txt`.** They are generated. To accept a change, run the test, then rename the emitted `*.received.txt` to `*.verified.txt` and re-run. Set `DiffEngine_Disabled=true` first unless you want a diff tool to open per file.
- **A failure is a report, not a bug.** Read the diff. If the change is deliberate, accept the new baseline and say so in the PR. If it is not, the diff is the bug report.
- **This is a maintenance branch.** A removed member or a changed signature in one of these files is a breaking change for 3.x users, and needs an explicit justification — not a quiet re-baseline.
- **Every packable project gets a baseline.** That is the 13 projects in `packTargetProjects` in `build/Build.cs`; add a test case when a package is added. The one deliberate exception is `Quartz.OpenTelemetry.Instrumentation`, whose `OpenTelemetry 0.6.0-beta.1` dependency cannot be referenced under warnings-as-errors (NU1608 ×2 and the NU1902 advisory GHSA-g94r-2vxg-569j).
- **Baselines are taken on `net10.0` only** — the file is behind `#if NETCORE`. The `net472` build has a different surface (`REMOTING` adds `RemoteScheduler` and friends) and is deliberately not snapshotted.
- They run on every CI leg. `UnitTest` runs each test project once per target framework it declares, skipping `net4x` off Windows — so the baselines are taken on `net10.0` on Windows, Ubuntu and macOS alike, and the generated output is identical on all three.

### Comparing 3.x against main

`main` carries the same baselines, so the 4.0 API delta is a `git diff`. Use it when writing the 4.x migration guide, reviewing API ergonomics, or checking whether a 3.x change has a counterpart on main. `git diff` takes `<rev>:<path>` blob arguments, so this works the same in PowerShell and bash:

```shell
git fetch origin

# one assembly
git diff origin/3.x:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt \
         origin/main:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt

# everything both branches snapshot
git diff origin/3.x origin/main -- src/Quartz.Tests.Unit/Verify src/Quartz.Tests.AspNetCore/Verify
```

The package boundaries moved in 4.0, so match the files up first:

| 3.x baseline | main baseline |
|---|---|
| `PublicApiTest_Quartz` | `PublicApiTest_Quartz` — main's also absorbs DI, Hosting and SystemTextJson |
| `PublicApiTest_Quartz.Extensions.DependencyInjection` | folded into `Quartz` (namespace `Quartz.Configuration`) |
| `PublicApiTest_Quartz.Extensions.Hosting` | folded into `Quartz` (`src/Quartz/Hosting/`) |
| `PublicApiTest_Quartz.Serialization.SystemTextJson` | folded into `Quartz` (`SystemTextJsonObjectSerializer`) |
| `PublicApiTest_Quartz.Serialization.Json` | `PublicApiTest_Quartz.Serialization.Newtonsoft` |
| `Quartz.Jobs`, `Quartz.Plugins`, `Quartz.Plugins.TimeZoneConverter`, `Quartz.Extensions.Redis`, `Quartz.AspNetCore`, `Quartz.Dashboard` | same name on both sides |
| `PublicApiTest_Quartz.OpenTracing` | dropped on main |
| (no baseline — see above) | dropped on main; use `OpenTelemetry.Instrumentation.Quartz` |
| — | `PublicApiTest_Quartz.HttpClient` — new on main |

Two differences are systematic and are **not** API deltas worth reporting: `Task` → `ValueTask` on nearly every member, and `Quartz.Spi` → `Quartz.Extensibility` / `Quartz.Simpl` → `Quartz.Impl`. `AGENTS.md` on main tabulates the rest of the renames.

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

### Shipped packages

Thirteen, listed authoritatively in `packTargetProjects` in `build/Build.cs`:

- `Quartz` — the core library
- `Quartz.Extensions.DependencyInjection` — `IServiceCollection.AddQuartz()`
- `Quartz.Extensions.Hosting` — `IHostedService` via `QuartzHostedService`
- `Quartz.AspNetCore` — `AddQuartzServer` (hosted service + health checks)
- `Quartz.Dashboard` — Blazor dashboard UI (`net8.0` only)
- `Quartz.Serialization.Json` (Newtonsoft) / `Quartz.Serialization.SystemTextJson` — job data serialization
- `Quartz.Jobs` / `Quartz.Plugins` / `Quartz.Plugins.TimeZoneConverter` — built-in jobs and plugins
- `Quartz.Extensions.Redis` — Redis-backed lock handler
- `Quartz.OpenTelemetry.Instrumentation` / `Quartz.OpenTracing` — legacy tracing shims, both dropped in 4.x

### ADO.NET job store

`JobStoreSupport` is the base class for persistent storage. Database-specific SQL delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `MySQLDelegate`, etc.) live in `Impl/AdoJobStore/`.

### Database scripts

- `database/tables/tables_<dialect>.sql` — fresh-install DDL, one per database.
- `database/migrations/<version>/<name>_<dialect>.sql` — schema changes, grouped by the Quartz.NET version that introduced them. One directly-runnable file per database; no commented-out dialect blocks. **Generated** — describe the change in `build/Build.DatabaseMigrations.Scripts.cs` and run `dotnet fallout GenerateMigrations`; never hand-edit the output. The `2.0` and `3.0` folders are the exception: hand-written, SQL Server-only historical scripts. `VerifyMigrations` runs in CI and fails when the two are out of step.
- `database/README.md` — the index: run order, per-version status, and old→new path mapping.

Rules when touching any of this:

- **A migration both branches can run must stay byte-identical on `3.x` and `main`.** It lands on both in a companion PR pair, or a documented path 404s on whichever branch lacks it — which is what #3218 reported. **`database/migrations/4.0/` is not on this branch**: it is the 3.x → 4.0 upgrade path, it changes whenever 4.x's schema changes, so `main` is its single maintained home and `database/README.md` here links there rather than mirroring it — a mirrored copy goes stale silently, and a confidently wrong upgrade script is worse than a missing one. Each branch's `database/README.md` indexes what that branch carries. `database/tables/` is the *current* schema and differs by design.
- Every migration ships a file for **every** supported dialect (`sqlServer`, `postgres`, `mysql_innodb`, `oracle`, `sqlite`, `firebird`), guarded so it is safe to re-run. SQLite `ADD COLUMN` is the one exception — it has no conditional DDL.
- 4.x has no `Supports*Column` probes, so anything **optional on 3.x is required on 4.x**. Every 3.x column migration still has to be folded into `database/migrations/4.0/schema_30_to_40_upgrade_<dialect>.sql` — that script is generated on `main`, so the fold belongs to the companion pull request there, not to a change here.
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

- **Async throughout:** All public APIs are `async Task` with `CancellationToken cancellationToken = default`. Always use `.ConfigureAwait(false)` on awaited calls (enforced by `ConfigureAwaitChecker.Analyzer`).
- **File-scoped namespaces:** The `SPI/` directory maps to `Quartz.Spi` namespace (note casing difference).
- **Nullable enabled** in library projects, disabled in test projects.
- **Explicit types preferred over `var`** per `.editorconfig`.
- **Allman brace style** — opening braces on new lines.
- **Test framework:** NUnit 4 with `AwesomeAssertions` and `FakeItEasy` — see [Assertions](#assertions).
- **Test parallelization:** Unit tests run fixtures in parallel (`[assembly: Parallelizable(ParallelScope.Fixtures)]`). Fixtures with shared static state are marked `[NonParallelizable]`. Do **not** add `[TestFixture]` to test classes — NUnit 4 discovers them automatically. Only use `[TestFixture(...)]` with parameters for parameterized fixtures.
- **Multi-targeting:** Core library targets `net462`, `net472`, `net8.0`, `net9.0`, `net10.0`, `netstandard2.0`. Tests target `net10.0` and `net472`.
- **Strong naming:** Assemblies signed with `quartz.net.snk`.
- **Conditional compilation:** `REMOTING` defined for `net462`/`net472`; `DIAGNOSTICS_SOURCE` for everything except `net462`.
