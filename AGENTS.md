# Quartz.NET

Instructions for AI coding agents working in this repository.

This is the single source of truth, and it is the whole of it — there are no per-area instruction
files, so a tool that reads only the repository root still gets everything. `AGENTS.md` is read
directly by Codex, Cursor, Amp, Windsurf, Devin and Copilot's cloud agent, CLI and VS Code
integrations. The rest arrive through a file that exists only to route them here:

| File | Exists for |
|---|---|
| `CLAUDE.md` | Claude Code, which does not read `AGENTS.md` (anthropics/claude-code#6235). It imports this file with `@AGENTS.md` |
| `.github/copilot-instructions.md` | Copilot in Visual Studio and JetBrains, which read that path only and have no `AGENTS.md` support |
| `.gemini/settings.json` | Gemini CLI, whose context file is `GEMINI.md` until `context.fileName` names another |
| `.aider.conf.yml` | Aider, which loads no instruction file on its own; `read:` puts this one in every session |

**Edit this file; the other four stay pointers.** A pointer that grows instructions of its own drifts
from this file, and Copilot combines the instruction files it finds rather than picking one, so
anything duplicated is applied twice. `AgentInstructionsTest` fails a pointer that stops naming
`AGENTS.md`, and any instruction file over 32,768 bytes — Codex's `project_doc_max_bytes`, which is a
running budget across the whole root-to-working-directory chain and the only documented cap that
binds this repository.


## Key Conventions

- **File-scoped namespaces** — enforced as error (`csharp_style_namespace_declarations = file_scoped:error`).
- **Explicit types over `var`** — prefer explicit types everywhere (`csharp_style_var_for_built_in_types = false`).
- **Nullable enabled** globally; test projects may disable it.
- **Warnings as errors** — `TreatWarningsAsErrors` is true; code style is enforced in build.
- **`Quartz` builds with the trim, AOT and single-file analyzers on**, so an `IL2xxx` or `IL3xxx` is an error.
  The known-reflective types are recorded in `src/Quartz/TrimAnalysisBaseline.cs` (and mirrored for ILLink in
  `src/Quartz/ILLink.Suppressions.xml`, which the worker example's trimmed publish applies; ILCompiler takes no
  such file, so a native AOT publish still reports them). A warning in a type not listed there means new
  reflection — fix it rather than adding a line; that file explains the order to try fixes in. Neither file
  ships, so consumers still see every warning. The package **does** say `IsAotCompatible`, and the claim is
  narrow: Quartz produces no `IL3050` at all, so nothing it does needs code generated at run time, while the
  `IL2xxx` that remain are the string-named paths #3341 tracks and are unaffected by it. The property lands
  in the assembly as `[AssemblyMetadata("IsAotCompatible", "True")]` and puts nothing in the nuspec, so its
  audience is the analyzers and a reader of `src/Quartz/Quartz.csproj`, which spells all of this out.
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
- **UTF-8 without a byte-order mark** — everything under `src/`, whatever its extension;
  `SourceEncodingTest` fails a file that starts with one. `*.verified.*` snapshots are the exemption,
  because Verify writes the mark and the next regeneration would put it straight back. A script that
  writes a source file uses Python's `encoding="utf-8"` — never `utf-8-sig` — or PowerShell's
  `-Encoding utf8NoBOM`.

### Naming decisions that are settled

These spots look inconsistent on purpose. Each was examined and ratified in the 4.0 API-finalization
pass; do not "finish" any of them.

- **The scheduler's noun is `JobDetail`; the store's noun is `Job`.** `IScheduler` hands users
  `IJobDetail`, so it says `GetJobDetail`/`GetJobDetails`. `IJobStore` speaks in storage terms, so it
  says `GetJob`/`GetJobs` (beside `GetTrigger`/`GetTriggers`). Singular/plural pairs are consistent
  *within* each interface; the two interfaces deliberately differ, and aligning one with the other
  would break the consistent pairs on whichever side got "fixed".
- **`StdAdoDelegate` and the `*Delegate` dialect family keep their names.** "A class named Delegate
  that isn't a delegate" is regrettable in .NET, but this vocabulary is Quartz's cross-ecosystem
  identity: Java parity, twenty years of Stack Overflow answers, `quartz.jobStore.driverDelegateType`
  spelled in countless configuration files, `database/README.md`, and the dialect docs all teach
  against `IDriverDelegate`/`SqlServerDelegate`/`PostgreSQLDelegate`/…. The `Std` prefix was retired
  everywhere else (the semaphore renames finished that); `StdAdoDelegate` is the sole deliberate
  survivor, because renaming it would orphan the pedagogy without helping anyone.
- **`RAMJobStore` keeps its name**, for the reason `StdAdoDelegate` does: it is a configuration-key
  identity. `quartz.jobStore.type` names the type, and twenty years of configuration files, tutorials
  and Stack Overflow answers spell it. Its options type is `InMemoryJobStoreOptions` and the builder
  method is `UseInMemoryStore`, because *those* are new names with no history to keep faith with — the
  mismatch between them and the type is deliberate, not an unfinished rename.
- **The `*Utc` suffix on `DateTimeOffset` members stays.** `StartTimeUtc`, `EndTimeUtc`,
  `NextFireTimeUtc` and the rest carry an offset and so cannot be anything but unambiguous, which makes
  the suffix redundant on its face. It is Java parity, it is what every Quartz tutorial teaches, and it
  is twelve members against roughly 1,400 call sites in this repository alone. The names say which
  reading of the clock the value is, and nobody is confused by them.
- **`Use*` is the verb for an extension that registers a plugin** — `UseStructuredJobLogging`,
  `UseJobHistoryLogging`, `UseXmlSchedulingConfiguration` — because a plugin is middleware over a
  scheduler's lifecycle and that is how middleware reads. `AddPlugin<T>` is the generic form, for a
  plugin with no extension of its own. `Add*` stays for things a scheduler *contains*: jobs, triggers,
  calendars, listeners. `UseTimeZoneConverter` keeps the verb although it no longer registers a plugin:
  what it installs is still a scheduler-wide capability rather than something the scheduler holds.
- **Four history-logging plugins ship, not two.** `LoggingJobHistoryPlugin` /
  `LoggingTriggerHistoryPlugin` log through numbered format strings and
  `StructuredLoggingJobHistoryPlugin` / `StructuredLoggingTriggerHistoryPlugin` log the same events
  through named templates. The structured pair is the better default and is documented as such; the
  classic pair stays because a deployment's log pipeline is matched against the 3.x message shape, and
  a message template is a contract to whatever parses it. Retiring the classic pair is a break with no
  migration to offer, so it is not on the table. The plugin sweep of #3593 retired the plugins whose
  job the host already does — these are not those.
- **`services.AddHealthChecks().AddQuartz()` keeps that name.** Read on its own it says nothing about
  health, but it is the `AspNetCore.HealthChecks.*` idiom — every check in that ecosystem is
  `AddHealthChecks().AddX()` — and the receiver is what supplies the noun.
- **The `AddQuartz(NameValueCollection, …)` overloads stay beside their dictionary twins.** They look
  like a duplicate pair; they are the 3.x on-ramp, because a `NameValueCollection` is what an
  application migrating from `StdSchedulerFactory` already holds.
- **Reading has two altitudes, and both stay.** `IScheduler`'s `Query*` members take a query record —
  filter, page, optional total — and answer with headers; `SchedulerQueryExtensions`' `Get*` conveniences
  take none of that and answer with bare keys and names. Neither is the other's leftovers, and a
  shorthand saving only the `new` earns nothing. The pause/resume matcher members are `*Groups` because
  the group set is what they write and answer with: a paused group survives a restart and binds what is
  added to it next, which no list of keys can say.
- **`ISchedulerRepository` and `ISchedulerRegistry` answer different questions.** The repository is the
  live-instance directory — bind, remove, look up. The registry lists what a container has *registered*,
  built or not, so an operator can enumerate tenants without starting every one. Both stay.
- **`NameMatcher` — the arity-free one — sits outside `IMatcher<T>` on purpose.** That interface and the
  `Matchers.And`/`Or`/`Not` combinators are constrained to `Key<T>`, which is what lets a matcher reach
  the scheduler members that take one; a calendar's or a group's name is not a key. It was
  `CalendarNameMatcher`: it took the family's name without taking the family's interface.

### Overload sets audited and frozen in #3598

Counted, argued and left as they are. Each looks like a set to thin; the reason it is not is on the
members themselves, so read the XML docs before reopening one.

- **Job-store selection keeps all seven members**, because each says something the others cannot:
  the two shipped stores (`UseInMemoryStore`, `UsePersistentStore`), a persistent store of another type
  (`UsePersistentStore<T>`), a store of your own built by the container (`UseJobStore<T>`, plus
  `<T, TOptions>` for one with named options, which `custom-job-store.md` teaches), one you built
  (`UseJobStore(IJobStore)`), and one a factory builds over the scheduler's own parts
  (`UseJobStore(Func<…>)`).
- **Hosted-service registration keeps all six**: three shapes — the ordinary one, `<T>` for a subclass,
  and `(schedulerName, …)` for one scheduler's settings — on each of the two receivers every
  registration API here has. C# has no default type argument, so the first shape is not the second one
  written shorter.
- **`IThreadPool` keeps all six members.** It is not an interface guarding one integer: each member has
  exactly one caller in the scheduler, and `ZeroSizeThreadPool` stays public because
  `UseThreadPool<ZeroSizeThreadPool>()` needs it reachable.
- **`JobBuilder`/`TriggerBuilder` keep their companion classes while the schedule builders carry their
  own `Create`.** Generic inference forces it — `JobBuilder<MyJob>.Create()` would name the job type
  twice — and no schedule builder is generic.
- **`IScheduleBuilder.Build()` keeps returning `IMutableTrigger`,** and `ConfigureJobScope` keeps taking
  `TriggerFiredBundle`. Both are `Quartz.Extensibility` types on a mainstream path, and both are the
  only type that says the thing: the trigger builder must write onto what it is handed, and a firing
  before its job exists has no `IJobExecutionContext` yet.

### Examined in the alpha.5 audit and kept (#3603)

The rule above, for the things that are not names. The reason is on the member too.

- **The two day fields follow Vixie, not Cronos.** A field written exactly `*` or `?` restricts nothing
  and defers to the other; when both name days the expression fires on their *union*, so
  `0 0 0 13 * FRI` is every Friday **and** the 13th. Cronos ANDs them and would fire only on Friday the
  13th. The union is `crontab(5)`'s rule and what `cron-expressions.md` has always taught,
  `UnixCronFormatTest` pins it, and "finishing" the alignment would halve every schedule that names
  both fields. `*/n` is restricted here, unlike Vixie, whose parser reads the leading `*` first.
- **`CronFormat` is stated, never sniffed.** A five-field string throws in the default `Quartz` format,
  and the message names `CronFormat.Unix` and the rewritten expression. Auto-detect was rejected: the
  suite already uses "five fields" to mean "invalid expression", a dropped middle field would change a
  schedule in silence, and the same digit is a different day in each dialect. Detection can be added
  later; removing it could not.
- **Wrapping ranges and `H` are Quartz supersets over standard cron, and are identity.** A range whose
  end is below its start wraps instead of sorting its endpoints — `22-2` is five hours, `FRI-MON` a long
  weekend — and `H` spreads a firing deterministically from the trigger key or an explicit one.
  `CronExpressionWrappingRangeTest` pins the wrap, and the Unix rewrite carries both into five fields.
- **Every `ISchedulerListener` member leads with `IScheduler`.** A job or trigger listener reaches its
  scheduler through the execution context; no scheduler-listener notification has one, so the scheduler
  is the first argument instead — which is also what lets one instance serve several schedulers and say
  which of them paused a trigger. `ITriggerListener` leads with the trigger, for the mirror reason.
- **`SchedulerContext` and `JobDataMap` are not twins to align.** The map is persisted, dirty-tracked
  and equatable, and its `PutAsString` writers are instance members because they take part in that
  change tracking. The context is never persisted, is read and written concurrently, and has nothing to
  track. Only the typed *read* accessors are shared, in `DataMapExtensions`.
- **`LogProvider.SetLogProvider` is the one static escape hatch, kept knowingly.** Plenty of things that
  log are never handed a logger by a container: a listener you constructed, a trigger deserialized out of
  a job store, the static helpers, a standalone `QuartzSchedulerBuilder`. Nor is it seeded from the
  container: the slot outlives any one container, and a host built, disposed and built again would leave
  it pointing at a disposed `ILoggerFactory`.
- **The built-in trigger serializers are public and unsealed, and so are all five `*TriggerImpl` types.**
  That pairing is the subclassed-trigger seam: derive from the trigger, derive from its serializer, call
  `base.SerializeFields`/`base.DeserializeFields`. Three of the trigger types were sealed during 4.x's
  development and reopened for it; `BuiltInTriggerSerializerDerivationTest` fails if either half closes.
- **A job's timeout is `[JobTimeout]`, not a builder member.** How long the work may take is a property
  of the code that does it, and an attribute travels with the type through every job store, wire format
  and way of scheduling — a builder value would have to be persisted, migrated and round-tripped to reach
  the same places, or squat on a reserved data-map key. As with `[DisallowConcurrentExecution]`. Nothing
  enforces it until `AddJobTimeout` registers the middleware.

### Promises the beta makes (#3647)

Extension policy, not taste. `how-tos/extending-quartz.md` is the reader-facing form.

- **A collaborator is handed a context object** — parameterless ctor, `init` properties — never a
  parameter list: `DriverDelegateContext`, `LockHandlerContext`, `TriggerFiredBundle` and the rest. A
  new datum is a non-`required` property, so it is source- and binary-compatible.
- **A member added to a public interface lands as a default interface member.** `IJobStore` has nine,
  `IScheduler` one, `ILockHandler` and `ITriggerPersistenceDelegate` one each; that is what makes
  freezing `IQuartzApiClient` and `ITriggerSerializer` without default bodies safe, and the baselines
  mark DIMs so the promise is checkable. A forwarder must *declare* every such member — an omitted one
  runs the default on the forwarder; `DelegatingForwardingTest` sweeps both delegating types for it.
- **Read-replica routing, if ever, is a DIM `IDbProvider.CreateReadConnection()`** — never a `readOnly`
  parameter on `CreateConnection`, which would break both public `IDbProvider` implementations.
- **`MON/2` stays rejected** (ratified 2026-09-01); 4.1 may accept it additively.

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
- **`AdoJobStoreBase`** → `LocalTransactionJobStore` / `ExternalTransactionJobStore` (`Quartz.Impl.AdoJobStore`) — ADO.NET-based persistence with database-specific delegates (`SqlServerDelegate`, `PostgreSQLDelegate`, `OracleDelegate`, `MySQLDelegate`, `SQLiteDelegate`, `FirebirdDelegate`). On 3.x these are `JobStoreSupport` → `JobStoreTX` / `JobStoreCMT`. All three are **internal**: `UsePersistentStore` builds the local one and `UsePersistentStore(s => s.UseAmbientTransactions())` the other, the driver delegate is the seam, and `quartz.jobStore.type` still names either by string.

### Database scripts

- `database/tables/tables_<dialect>.sql` — fresh-install DDL, one per database.
- `database/migrations/<version>/<name>_<dialect>.sql` — schema changes, grouped by the Quartz.NET version that introduced them. One directly-runnable file per database; no commented-out dialect blocks. **Generated** — describe the change in `build/Build.DatabaseMigrations.Scripts.cs` and run `dotnet fallout GenerateMigrations`; never hand-edit the output. The `2.0` and `3.0` folders are the exception: hand-written, SQL Server-only historical scripts. `VerifyMigrations` runs in CI and fails when the two are out of step.
- `database/README.md` — the index: run order, per-version status, and old→new path mapping.

Rules when touching any of this:

- **A migration both branches can run must stay byte-identical on `main` and `3.x`.** It lands on both in a companion PR pair, or a documented path 404s on whichever branch lacks it — which is what #3218 reported. **`database/migrations/4.0/` is the exception and lives only here**: it is the 3.x → 4.0 upgrade path, its content is decided by 4.x's schema, and `main` is its single maintained home — `3.x` links to it rather than mirroring it, because a mirrored copy goes stale silently the next time 4.x's schema moves, and a confidently wrong upgrade script is worse than a missing one. `database/README.md` carries a **Branch** column saying which versions are on both; keep it accurate, since nothing in CI checks cross-branch identity (`VerifyMigrations` only compares a branch's scripts with its own generator). Each branch's `database/README.md` indexes what that branch carries. `database/tables/` is the *current* schema and differs by design.
- Every migration ships a file for **every** supported dialect (`sqlServer`, `postgres`, `mysql_innodb`, `oracle`, `sqlite`, `firebird`), guarded so it is safe to re-run. SQLite `ADD COLUMN` is the one exception — it has no conditional DDL.
- 4.x has no `Supports*Column` probes, so anything **optional on 3.x is required on 4.x**. Fold every 3.x column migration into `database/migrations/4.0/schema_30_to_40_upgrade_<dialect>.sql` — the fold happens here even when the migration itself was written on `3.x`, because that script is generated on `main` alone.
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

The container constructs the scheduler; there is no reflective instantiation from type-name strings, and
there is no properties-based `StdSchedulerFactory` any more. Legacy flat `quartz.*` keys are translated
to typed options and registrations by `QuartzPropertyBridge`, which is the only place that understands
them; `LegacyPropertyKeys` holds the key strings and rejects a misspelled one. A new setting is therefore
a typed option plus a bridge entry, never a string read somewhere else.

### Serialization

Pluggable serialization for job store persistence:
- `Quartz.Serialization.SystemTextJson` (built into core as `SystemTextJsonObjectSerializer`)
- `Quartz.Serialization.Newtonsoft`

### Observability

- `Quartz.Diagnostics` — spans on `QuartzActivitySource`, metrics on `Meters`.
- OpenTelemetry: `AddSource(QuartzInstrumentation.ActivitySourceName)`, `AddMeter(QuartzInstrumentation.MeterName)`; the contrib package emits nothing.
- Logging: `Microsoft.Extensions.Logging` via `LogProvider`, same namespace.

## Documentation and generated artifacts

- **`docs/documentation/quartz-3.x/` must keep the old names.** Only update `quartz-4.x/`.
- **Heading fragments are checked with the site's own slugger, not GitHub's.** `npm run docs:check-links`
  parses the docs with the markdown-it instance VuePress renders them with, so the ids it validates
  against are the ids the published pages carry — `Quartz.Core` is `#quartz-core`, not `#quartzcore`.
  It runs on every docs pull request. markdownlint's MD051 is off for exactly this reason; do not
  take its fragment suggestions, they 404. `npm run docs:check-links-test` is the checker's control.
- **C# in a documentation page is generated, not typed.** Samples live as `#region sample_*` blocks in
  `src/Quartz.Documentation.Samples` — an ordinary project in the solution, so a rotted sample fails
  `Compile` — and a page carries `<!-- snippet: name -->` / `<!-- endSnippet -->` markers that
  `dotnet fallout DocsSnippets` fills in. `VerifyDocsSnippets` fails a pull request whose markdown is
  stale, whose marker names nothing, or whose marker came out empty. The convention, and the handful of
  blocks deliberately left as plain fences, are written up in `CONTRIBUTING.md` under "Code samples in
  the documentation". Pages outside `docs/documentation/quartz-4.x/packages/` still carry hand-written
  fences; converting one is welcome, converting it halfway is not.
- **A package's readme is `src/<Project>/README.md`, never a documentation page.** That file is what
  nuget.org renders, and nuget.org renders CommonMark with none of VuePress's extensions — frontmatter
  becomes a horizontal rule and a literal `title:`, a `::: tip` becomes literal text, a relative link
  404s. `PackageReadmeTest` fails on each of those, on a packable project with no readme, and on a csproj
  that packs anything out of `docs/`. The readmes carry the same `<!-- snippet: … -->` markers as the
  documentation, so keep them short and let the site hold the prose.
- **`src/Quartz.Tests.Unit/Verify/PublicApiTest_*.verified.txt` are the public API baselines**, and
  `src/Quartz.Tests.AspNetCore/Verify/PublicApiTest_*.verified.txt` are the same thing for
  `Quartz.AspNetCore` and `Quartz.Dashboard`, whose dependencies only that project has.
  Any change to public API fails those tests; review the diff, and if the change is intended,
  accept the new baseline and carry the same diff into
  `docs/documentation/quartz-4.x/migration-guide.md`. Never hand-edit them.
- **`3.x` carries the same baselines, so the 4.0 API delta is a `git diff`.** Reach for it when
  writing the migration guide, reviewing API ergonomics, or checking whether a 3.x change has a
  counterpart here — it is exhaustive and always current, which no prose summary of the delta can be.
  `git diff` takes `<rev>:<path>` blob arguments, so this is the same command in PowerShell and bash:

  ```shell
  git fetch origin

  # one assembly
  git diff origin/3.x:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt \
           origin/main:src/Quartz.Tests.Unit/Verify/PublicApiTest_Quartz.verified.txt

  # everything both branches snapshot
  git diff origin/3.x origin/main -- src/Quartz.Tests.Unit/Verify src/Quartz.Tests.AspNetCore/Verify
  ```

  Package boundaries moved between the two, so match the files up first — the migration guide's
  appendix says which 3.x baseline became which. Two differences are systematic and are **not**
  deltas worth reporting: `Task` → `ValueTask` on nearly every member, and the namespace moves.
- **Release notes live in GitHub releases, not in the repository.** There is no changelog file on
  either branch; the tag's release is the record. Unreleased 4.x notes accumulate in the `v4.0.0`
  draft release.

## Porting changes between 3.x and main

`3.x` is the maintenance branch and `main` is 4.x, and a change written against one usually needs
relocating for the other. **The map is `docs/documentation/quartz-4.x/migration-guide.md`** — every
namespace move, every contract that changed shape, and an appendix indexed by the name you would have
typed. Ported code that fails to build is usually a rename, not a missing feature, so look there
before assuming the feature is missing.
