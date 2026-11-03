# Quartz.NET

Instructions for AI coding agents working in this repository. This file is the single source of truth.
`CLAUDE.md`, `.github/copilot-instructions.md`, `.gemini/settings.json` and `.aider.conf.yml` only point
here: edit this file, never those.

This file states each rule in brief. The full text is in `maintainers/`: read the document your area's
row names before writing code there. Where the two differ, the document wins.

## Where to read first

| You are working on | Read first |
|---|---|
| A public member: adding, renaming or removing one, an overload, an interface member, a name that looks inconsistent | [api-decisions.md](maintainers/api-decisions.md) |
| Cron parsing, `CronFormat`, day-of-week fields | [api-decisions.md](maintainers/api-decisions.md): "Examined in the alpha.5 audit" and "Promises 4.0 makes" |
| `QuartzSchedulerThread`, `JobRunShell`, hosting and DI, `AddQuartz`, `QuartzSchedulerBuilder`, health checks, Aspire, legacy `quartz.*` keys, serializers, telemetry | [architecture.md](maintainers/architecture.md) |
| An `IL2xxx` or `IL3xxx` warning, reflection, feature switches | [architecture.md](maintainers/architecture.md): "Trimming and AOT" |
| Job stores, ADO.NET delegates, the database scripts, migrations | [persistence.md](maintainers/persistence.md) |
| Tests, coverage, benchmarks | [testing.md](maintainers/testing.md) |
| Documentation pages and samples, package readmes, generated pages, the public API baselines, the migration guide | [documentation.md](maintainers/documentation.md), and `CONTRIBUTING.md`, "Writing style", for prose |
| Porting between `3.x` and `main`, a `4.N.x` patch, release notes, tags | [branches-and-releases.md](maintainers/branches-and-releases.md) |
| This file, the pointer files, `.claude/agents/`, delegating to a subagent | [agent-tooling.md](maintainers/agent-tooling.md) |

## Key Conventions

- **File-scoped namespaces** — enforced as error (`csharp_style_namespace_declarations = file_scoped:error`).
- **Explicit types over `var`** — prefer explicit types everywhere (`csharp_style_var_for_built_in_types = false`).
- **Nullable enabled** globally; test projects may disable it.
- **Warnings as errors** — `TreatWarningsAsErrors` is true; code style is enforced in build.
- **`Quartz` builds with the trim, AOT and single-file analyzers on**, so an `IL2xxx` or `IL3xxx` is an
  error. A new one is new reflection: fix it, never suppress it.
- **Allman brace style** — braces on new lines for methods, types, control blocks, properties, accessors, lambdas.
- **No `DateTime.Now`/`DateTimeOffset.Now`** — banned via Roslyn analyzer (`BannedSymbols.txt`). Use `TimeProvider` instead.
- **No implicit `DateTime` → `DateTimeOffset` cast** — also banned.
- **All public APIs return `ValueTask`** rather than `Task` (e.g., `IJob.Execute`, `IScheduler` methods),
  classes too. The one exception is SignalR's `IQuartzDashboardHubClient`.
- **No `Async` suffix** on Quartz-authored members; the bare verb *is* the async one. Only names dictated by a BCL interface (`IHostedService`, `IAsyncDisposable`, `IHealthCheck`) carry it.
- **Every async member ends with `CancellationToken cancellationToken = default`.** There are no exceptions left.
- **Return concrete collection types, accept abstractions** — `List<T>` or `T[]` out, `IReadOnlyCollection<T>`/`IReadOnlyList<T>` in.
- **No setter-only properties on interfaces.** Identity and configuration arrive by constructor or an explicit context parameter.
- **Strong-named assemblies** — signed with `quartz.net.snk` (except examples).
- **Central package management** — package versions in `Directory.Packages.props`.
- **Single target** — everything targets `net10.0`. `Quartz.Analyzers` is the one exception: an analyzer
  runs inside the compiler host, so it is `netstandard2.0`, links the cron parser's sources rather than
  referencing `Quartz.dll`, and ships inside `Quartz.nupkg` under `analyzers/dotnet/cs`.
- **SDK**: .NET 10 SDK (see `global.json`), with `rollForward: latestMinor`.
- **License headers** — source files include Apache 2.0 license region at the top.
- **UTF-8 without a byte-order mark** — everything under `src/`, whatever its extension;
  `SourceEncodingTest` fails a file that starts with one, and `*.verified.*` snapshots are the exemption.
  A script that writes a source file uses Python's `encoding="utf-8"` — never `utf-8-sig` — or
  PowerShell's `-Encoding utf8NoBOM`.

## Rules with a long reason

Each is in brief here and in full in the document named.

- **An inconsistency in the public API is usually deliberate.** Names, overload sets and designs that
  were examined and kept are listed with their reasons; do not "finish" one (api-decisions.md).
- **A 4.x patch is additive**, its baseline diff additions only, and a member added to a public interface
  lands as a default interface member (api-decisions.md, "Promises 4.0 makes").
- **Never hand-edit a public API baseline** (`PublicApiTest_*.verified.txt`). Review the diff, accept it,
  and carry it into `docs/documentation/quartz-4.x/migration-guide.md` (documentation.md).
- **`docs/documentation/quartz-3.x/` keeps the old names.** Only `quartz-4.x/` is updated
  (documentation.md).
- **C# in a documentation page is generated** from `src/Quartz.Documentation.Samples` by
  `dotnet fallout DocsSnippets`, never typed (documentation.md).
- **Database migrations are generated** from `build/Build.DatabaseMigrations.Scripts.cs` by
  `dotnet fallout GenerateMigrations`, never hand-edited, and ship for every dialect (persistence.md).
- **A workflow with an `<auto-generated>` header is generated** from `build/Build.CI.GitHubActions.cs`:
  change the attribute and regenerate. `docs.yml`, `docs-pr.yml`, `sonar.yml` and `publish-container.yml`
  are hand-written.
- **After `TriggersFired`, clean up with `TriggeredJobComplete`**, never `ReleaseAcquiredTrigger`
  (architecture.md).
- **A new setting is a typed option plus a `QuartzPropertyBridge` entry**, never a `quartz.*` string read
  somewhere else (architecture.md).
- **Release notes are the GitHub release.** There is no changelog file (branches-and-releases.md).

## Build & Test

Build the solution (uses the [Fallout](https://fallout.build/) build system):

```shell
# Windows
build.cmd

# Linux/macOS
./build.sh
```

Run unit tests, or one by name. `global.json` selects Microsoft.Testing.Platform, which has no `--logger`, `--collect` or `--blame-*`:

```shell
dotnet test --project src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj
dotnet test --project src/Quartz.Tests.Unit/Quartz.Tests.Unit.csproj --filter "FullyQualifiedName~CronExpressionTest.TestIsSatisfiedBy"
```

Integration tests need a running Docker daemon (Testcontainers provisions the databases):

```shell
.\build.cmd Compile UnitTest IntegrationTest
```

The unit workflow runs `dotnet fallout VerifyMigrations VerifySchema Compile UnitTest BenchmarkSmoke WolverineSmoke ExamplesSmoke ContainerSmoke PublishTrimmed PublishAot`; run the whole list before a pull request that touches a project file, a migration or a benchmark, with `PublishAot` from a shell whose `PATH` has the Visual Studio installer folder (ILCompiler shells out to `vswhere`).

The test framework is **NUnit** with **AwesomeAssertions** and **FakeItEasy** for mocking. Some tests use **Verify.NUnit** for snapshot testing. A bunit read after `.Click()` or `.Change()` is a `WaitForAssertion`. SQL is a `static readonly` constant built from `StdAdoConstants.TablePrefixSubst` and substituted at the command site — Sonar fails the gate (S2077) on interpolation there.

**Use AwesomeAssertions (`.Should()`) rather than NUnit's `Assert`** in anything new; testing.md shows the
forms. Sonar's coverage gate counts the unit projects only (testing.md).

## Architecture

Quartz.NET is an enterprise job scheduling library. The core domain model:

- **`IScheduler`** → main entry point; schedules jobs with triggers. Implemented by `StdScheduler` which delegates to `QuartzScheduler`.
- **`IJob`** → user-implemented interface with a single `ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)` method. The token is the *same* one as `IJobExecutionContext.CancellationToken`; it is a parameter so that `CA2016` can flag jobs that fail to forward it.
- **`IJobDetail`** → metadata about a job (type, key, JobDataMap). Built via `JobBuilder`.
- **`ITrigger`** → defines when a job fires (cron, simple interval, daily time interval, calendar interval). Built via `TriggerBuilder` + schedule builders (`CronScheduleBuilder`, `SimpleScheduleBuilder`, etc.).
- **`JobKey` / `TriggerKey`** → identity objects composed of name + group.

DI and hosting are in the core `Quartz` package (`src/Quartz/Configuration/`, `src/Quartz/Hosting/`), and the
container constructs the scheduler: there is no properties-based `StdSchedulerFactory` and no reflective
instantiation from a type name. The stores are `RAMJobStore`, the default, and the ADO.NET store behind
`UsePersistentStore` (persistence.md).
