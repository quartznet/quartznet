---

title: Migration Guide
---

*This document outlines changes needed when upgrading from Quartz.NET 3.x to 4.x. You should also check [the release notes](https://github.com/quartznet/quartznet/releases) for each version.*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)
:::

## The road from 3.x, phase by phase

The 4.0 API is the result of six passes over the public surface, each with its own theme. The guide
below is organised by topic rather than by pass, so this is the map: what changed, in the order it is
worth working through when migrating.

**1. The extensibility contracts.** The interfaces you implement rather than call. `Quartz.Spi` is
[`Quartz.Extensibility` and `Quartz.Simpl` is `Quartz.Impl`](#quartz-spi-and-quartz-simpl-were-renamed);
every asynchronous member returns [`ValueTask`](#tasks-changed-to-valuetask) and ends with a
`CancellationToken`; [jobs take that token as a parameter](#jobs-take-a-cancellationtoken); the
[job factory hands out a scope](#the-job-factory-hands-out-a-scope) instead of an instance; the
[thread pool is asynchronous](#the-thread-pool-is-asynchronous); and
[trigger fire times are properties](#trigger-fire-times-are-properties) rather than getter/setter
pairs; and [a job detail of your own](#an-ijobdetail-of-your-own) is finally implementable, because
the one member no implementation could write is gone from `IJobDetail`. Start here: everything else
assumes these signatures.

**2. The vocabulary and the surface.** One word per concept, and nothing public that was never a
contract. [Names that were normalized](#names-that-were-normalized),
[the scheduler and the job store speak the same verbs](#the-scheduler-and-the-job-store-speak-the-same-verbs),
[matchers moved to `Quartz`](#matchers-moved-to-quartz),
[`Key<T>` moved to `Quartz` and is immutable](#key-t-moved-to-quartz-and-is-immutable),
[`SchedulerMetadata` replaces `SchedulerMetaData`](#schedulermetadata-replaces-schedulermetadata),
[the listener API](#listener-api-changes), and
[sealed and internalized types](#sealed-and-internalized-types).

**3. Listings, schedules and dates.** [Job store listings became queries](#job-store-listings-became-queries)
returning `PagedResult<T>` of headers; [misfire instructions are enums](#misfire-instructions-are-enums);
[intervals are said once per builder](#intervals-are-said-once-per-builder);
[`TimeOfDay` became `TimeOnly`](#timeofday-became-timeonly) and
[`DateBuilder`'s static factories are gone](#datebuilder-s-static-factories-are-gone);
[`Executing` is a trigger state](#executing-is-a-trigger-state); and
[the preferred node is a value](#the-preferred-node-is-a-value).

**4. The ADO.NET job store.** [Trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate);
[the stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use);
[nine `Execute…Lock` overloads became four](#nine-execute-lock-overloads-became-four-members);
[locks are a `SchedulerLock`](#locks-are-a-schedulerlock-not-a-string);
[the job store configuration is read-only](#the-job-store-configuration-is-read-only-and-no-longer-a-public-currency);
[the driver delegate speaks in records](#the-driver-delegate-speaks-in-records);
[the optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone)
and the [schema migration](#database-schema-migration) that goes with that is mandatory;
[`RAMJobStore` is sealed](#ramjobstore-is-sealed);
[a job store of your own can join your transaction](#a-job-store-of-your-own-can-join-your-transaction);
and the two stores, held to one contract test, now
[answer the same way](#the-two-job-stores-answer-the-same-way) where they used to disagree.

**5. Configuration and hosting.** The container builds the scheduler:
[`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone),
[the standalone builder is the same builder](#the-standalone-builder-is-the-same-builder),
[there is no process-global scheduler state](#no-process-global-scheduler-or-connection-state),
[`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container),
[one shape per registration method](#one-shape-per-registration-method),
[clustering is configured in one place](#clustering-is-configured-in-one-place),
[the hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler), and
[job execution metrics](#job-execution-metrics) are published by every scheduler.

**6. Serialization policy and the last edges.**
[`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it);
[the two exceptions moved out of `Quartz.Core`](#the-two-exceptions-moved-out-of-quartz-core);
[execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen);
[interruption has two names, not three](#interruption-has-two-names-not-three); and the naming and
visibility odds and ends gathered in [Other Breaking Changes](#other-breaking-changes) at the end of
this guide.

Two differences run through all six and are not called out case by case, because they are almost
universal: `Task` became `ValueTask` on nearly every member, and the namespaces moved as described in
pass 1. If a type you used does not appear in this guide at all, check
[Package Changes](#package-changes) first — it may have moved packages rather than changed.

## Target Framework

Quartz.NET 4.x targets `net10.0`. There is no `netstandard2.0` build and no support for Full Framework
style `.config` files.

If you are running on an older .NET version, you will need to upgrade your application to .NET 10
before upgrading to Quartz 4.x.

### If you are a library rather than an application

An application can be told to upgrade. An integration package that serves `net8.0` and `net9.0`
consumers cannot, and has to reference Quartz 3.x for those targets and Quartz 4 for `net10.0`. That is
a conditional `PackageReference` plus `#if NET10_0_OR_GREATER` at the call sites that differ, and it
works only while the difference stays *inside* the library — the moment your own public surface would
differ per target framework, a separate `net10.0`-only package or dropping the old targets is the
answer, not a deeper `#if`.

[Embedding Quartz in a Library](how-tos/embedding-quartz-in-a-library.md#shipping-a-package-that-multi-targets)
has the csproj fragment, the honest limit, and the table of renames a real port hit.

## Package Changes

`Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting`, and `Quartz.Serialization.SystemTextJson` have been merged into the main `Quartz` package. You can remove these package references from your project:

```diff
- <PackageReference Include="Quartz.Extensions.DependencyInjection" Version="3.*" />
- <PackageReference Include="Quartz.Extensions.Hosting" Version="3.*" />
- <PackageReference Include="Quartz.Serialization.SystemTextJson" Version="3.*" />
+ <PackageReference Include="Quartz" Version="4.*" />
```

If you use Newtonsoft.Json serialization, reference `Quartz.Serialization.Newtonsoft` instead of the old `Quartz.Serialization.Json`.

Configuration that names a type from one of the merged assemblies as a string keeps working: a name that fails to
resolve is retried against `Quartz`, with a warning naming both spellings.

`Quartz.OpenTracing` is **dropped** and has no 4.x release. It consumed the `DiagnosticSource` events that
4.x replaced with `System.Diagnostics.Activity`, and the OpenTracing project itself is archived. Remove the
package reference and the `AddQuartzOpenTracing` call, and subscribe to Quartz's activity source and meter
directly — see [OpenTelemetry Integration](packages/opentelemetry-integration.md#coming-from-quartz-opentracing).

```diff
- <PackageReference Include="Quartz.OpenTracing" Version="3.*" />
```

**`OpenTelemetry.Instrumentation.Quartz` is not the replacement.** It reads the same `DiagnosticSource`
events, so on 4.x it produces no spans at all — silently. Remove that reference too, and add
`AddSource(QuartzInstrumentation.ActivitySourceName)`; see
[OpenTelemetry.Instrumentation.Quartz](packages/opentelemetry-integration.md#opentelemetry-instrumentation-quartz).

```diff
- <PackageReference Include="OpenTelemetry.Instrumentation.Quartz" Version="1.*" />
```

### `Quartz.Aspire` is new

Nothing to migrate: it is a new package with no 3.x counterpart, listed here because this is where the
package list lives.

`Quartz.Aspire` is the client integration for [Aspire](https://aspire.dev/). It turns an Aspire connection
name into a persistent job store: the driver delegate for the database behind that name, connections taken
from a `DbDataSource` the container already holds, the scheduler's health check, and Quartz's two telemetry
signals named to the OpenTelemetry pipeline your ServiceDefaults project built.

```csharp
builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
```

It takes **no `Aspire.*` package dependency** — `IHostApplicationBuilder` is the whole of its contract — so
it works in any generic-host application and is not tied to Aspire's release cadence. There is no hosting
integration: the AppHost still declares its database resources itself. See
[Aspire Integration](packages/aspire.md) for every setting it reads from `Aspire:Quartz`, and
[Running Quartz under Aspire](how-tos/aspire.md) for what the call expands to.

One of those settings decides something the rest of Quartz leaves off by default, so it is worth reading
before the first run:

| Setting | Default | What it decides |
|---|---|---|
| `QuartzAspireSettings.ProvisionSchema` | unset — `SchemaProvisioning.CreateIfMissing` under `Development`, the `Validate` default in every other environment | Whether the store creates whatever its schema is missing as it starts. `true` creates in every environment and `false` in none; a `SchemaProvisioning` the application set on the store itself is kept either way. See [`PerformSchemaValidation` became `SchemaProvisioning`](#performschemavalidation-became-schemaprovisioning-which-has-a-third-position) |

### The health check is in `Quartz`, not `Quartz.AspNetCore`

`Quartz.AspNetCore` carries `<FrameworkReference Include="Microsoft.AspNetCore.App" />` for the HTTP API,
a framework reference reaches the nuspec, and so a worker on a `dotnet/runtime` image that referenced the
package for the health check alone failed to start
([#3532](https://github.com/quartznet/quartznet/issues/3532)). The check reads `IScheduler.Status` and
probes the job store, and never wanted ASP.NET Core for either, so it moved into the core package. It
registers on the standard `IHealthChecksBuilder`, as it always did.

This is a break between one 4.0 alpha and the next — through `4.0.0-alpha.3` everything below was in
`Quartz.AspNetCore`, and from `4.0.0-alpha.4` it is in `Quartz`:

| Member | Through alpha.3 | From alpha.4 |
|---|---|---|
| `QuartzHealthCheckOptions` | `Quartz.AspNetCore` | `Quartz` |
| `IServiceCollection.AddQuartzHealthChecks(Action<QuartzHealthCheckOptions>?)` | `QuartzAspNetCoreConfigurationExtensions` | `QuartzHealthCheckExtensions` |
| `IQuartzBuilder.AddQuartzHealthChecks(Action<QuartzHealthCheckOptions>?)` | `QuartzAspNetCoreConfigurationExtensions` | `QuartzHealthCheckExtensions` |
| `IHealthChecksBuilder.AddQuartz(Action<QuartzHealthCheckOptions>?)` | `QuartzAspNetCoreConfigurationExtensions` | `QuartzHealthCheckExtensions` |
| `IHealthChecksBuilder.AddQuartz(string, Action<QuartzHealthCheckOptions>?)` | `QuartzAspNetCoreConfigurationExtensions` | `QuartzHealthCheckExtensions` |
| `QuartzHealthCheck`, `SchedulerHealthCheckTarget` | internal to `Quartz.AspNetCore` | internal to `Quartz` |

Nothing at a call site changes: the namespace is `Quartz` on both sides, and the four methods are
extension methods, so the class holding them is not named where they are called. What changes is which
package has to be referenced — and if the check was the only reason for `Quartz.AspNetCore`, that
reference can go:

```diff
- <PackageReference Include="Quartz.AspNetCore" Version="4.0.0-alpha.3" />
+ <PackageReference Include="Quartz" Version="4.0.0-alpha.4" />
```

Keep `Quartz.AspNetCore` if you also serve the [HTTP API](packages/http-api.md) or the
[dashboard](packages/dashboard.md); it keeps its framework reference for those. `MapHealthChecks`, and the
`HealthCheckOptions.ResultStatusCodes` mapping that decides what a *degraded* scheduler answers over HTTP,
are ASP.NET Core's and stay documented on
[ASP.NET Core Integration](packages/aspnet-core-integration.md#health-checks).

## Documentation pages that moved

The documentation was reorganised alongside the API. The old URLs redirect, so an existing bookmark still
arrives somewhere useful, but if you keep links to these pages in a wiki or a runbook, retarget them:

| Old URL | Where it is now |
|---|---|
| `documentation/quartz-4.x/tutorial/crontrigger.html` | [`cron-expressions.html`](cron-expressions.md) — cron syntax is a reference, not a lesson, so it left the tutorial |
| `documentation/quartz-4.x/how-tos/crontrigger.html` | [`cron-expressions.html`](cron-expressions.md) — a stale second copy of the same material, deleted |
| `documentation/quartz-4.x/packages/json-configuration.html` | [`configuration/json.html`](configuration/json.md) — it configures a scheduler rather than describing a package |
| `documentation/quartz-4.x/packages/opentracing-integration.html` | [`packages/opentelemetry-integration.html`](packages/opentelemetry-integration.md) — the package is gone, as above |
| `documentation/quartz-4.x/tutorial/miscellaneous-features.html` | [`packages/quartz-plugins.html`](packages/quartz-plugins.md) — the grab-bag was split, and plug-ins were the largest part of it |

The two anchors that were linked from outside the site — `#h-hash-for-load-distribution` and
`#building-cron-expressions-programmatically` — travelled with the cron material and resolve on
[Cron Expressions](cron-expressions.md).

## Database Schema Migration

Quartz 4.x requires four columns on `QRTZ_TRIGGERS` (and one on `QRTZ_FIRED_TRIGGERS`) that were
**optional** in 3.x:

| Column | Table(s) | Optional since |
|---|---|---|
| `MISFIRE_ORIG_FIRE_TIME` | `QRTZ_TRIGGERS` | 3.17 |
| `EXECUTION_GROUP` | `QRTZ_TRIGGERS`, `QRTZ_FIRED_TRIGGERS` | 3.18 |
| `PREFERRED_NODE` | `QRTZ_TRIGGERS` | 3.19 |
| `PREFERRED_NODE_AUTO` | `QRTZ_TRIGGERS` | 3.19 |

3.x probed for each of these at startup and disabled the corresponding feature when it was
missing. **4.x removed those probes** and assumes all of them exist, so this migration is
mandatory even if you never used misfire reporting, execution groups or node affinity.

4.x also adds two columns 3.x never had:

| Column | Table | Holds |
|---|---|---|
| `RETRY_POLICY` | `QRTZ_TRIGGERS` | A trigger's retry policy, as its stored string form |
| `RETRY_ATTEMPT` | `QRTZ_TRIGGERS` | How many retries of the occurrence being executed have already been made |

Both are nullable with no default, so every row an upgrade brings across reads as "no retry policy"
and nothing has to be backfilled. They are in the 4.0 script because 4.x no longer probes for
columns: a column added after 4.0 ships would be a required column, and so a mandatory migration in
the middle of a major version. The names are on `AdoConstants` as `ColumnRetryPolicy` and
`ColumnRetryAttempt`.

4.x also adds a table 3.x never had:

| Table | Holds |
|---|---|
| `QRTZ_PAUSED_JOB_GRPS` | One row per paused job group — `SCHED_NAME`, `JOB_GROUP` |

It mirrors `QRTZ_PAUSED_TRIGGER_GRPS`, and it is what makes
[`JobGroup.Paused` truthful on the ADO store](#job-store-listings-became-queries). A group can be
paused while it holds no jobs, so there is no row on `QRTZ_JOB_DETAILS` to hang a flag on — the
trigger side made the same call for the same reason. 4.x validates the whole schema at startup, so
**this migration is mandatory even for a 3.x database that took every optional migration going**.

::: warning
Always run migration scripts in a test environment against a copy of your production database first.
:::

Apply the script for your database from
[database/migrations/4.0/](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) —
`schema_30_to_40_upgrade_sqlServer.sql`, `_postgres`, `_mysql_innodb`, `_oracle`, `_sqlite` or
`_firebird`. Every statement checks first, so it is safe to run whether or not you already applied
the optional 3.x migrations, and safe to run twice.

For SQL Server the column additions look like this:

```sql
IF COL_LENGTH('QRTZ_TRIGGERS','MISFIRE_ORIG_FIRE_TIME') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [MISFIRE_ORIG_FIRE_TIME] bigint NULL;
END
```

Replace `QRTZ_` with your configured table prefix if different.

See [Database Schema Changes](../database/schema-changes.md) for the full version-by-version
history, including what each optional 3.x migration does and what skipping it costs.

### Listing indexes (optional)

The same script aligns the index set with the statements 4.x issues. Two of the additions matter
most for the [job and trigger listings](#job-store-listings-became-queries):

| Index | Table and columns |
|---|---|
| `IDX_QRTZ_J_G_N` | `QRTZ_JOB_DETAILS(SCHED_NAME, JOB_GROUP, JOB_NAME)` |
| `IDX_QRTZ_T_G_N` | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME)` |

Listings page with `ORDER BY JOB_GROUP, JOB_NAME` and `ORDER BY TRIGGER_GROUP, TRIGGER_NAME`, and the primary
keys are name-before-group, so no existing index serves those ordered scans. **These are optional** — the
queries work without them, but each page becomes a scan plus a sort. Add them if you list jobs or triggers
from a large schema. They are in the fresh-install scripts for every dialect already.

The same section drops indexes that are a leftmost prefix of a wider one, or that no 4.x statement
can drive a scan from. PostgreSQL gets the largest change: several of its indexes omitted
`SCHED_NAME`, which is the leading column of every predicate Quartz issues, so they could not serve
a single-scheduler lookup at all.

### The acquisition index is reshaped (optional)

The same section also drops and recreates `IDX_QRTZ_T_NFT_ST`, the index acquisition runs on
([#3510](https://github.com/quartznet/quartznet/issues/3510)):

| | Table and columns |
|---|---|
| 3.x | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)` |
| 4.x | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)` |
| 4.x, Firebird | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)` — unchanged |

Acquisition orders by `NEXT_FIRE_TIME ASC, PRIORITY DESC`, so an index carrying both directions lets
the engine take the first entry instead of reading every due trigger and sorting it: against 100,000
triggers with 5,000 due, one acquisition on SQL Server goes from 21.6 ms and 20,395 logical reads to
0.6 ms and 8. `MISFIRE_INSTR` keeps a backlog of misfired triggers from costing a table lookup per
row skipped. The statement, and every acquisition contract around it, is unchanged — this is DDL
alone. Firebird's indexes take one direction for the whole index, so it keeps the 3.x shape; see
[Database Schema](db/index.md#indexes-and-the-acquisition-index-in-particular) for the numbers and
the MySQL and Oracle footnotes.

**If you built a 4.0 preview schema before this landed**, re-run
`schema_30_to_40_upgrade_<database>.sql` — or at least its section 6. The index name did not change,
so the script drops it before recreating it; a guarded `CREATE INDEX` on its own would find the name
taken and keep the old three columns.

Full table creation scripts for fresh installations are available in [database/tables/](https://github.com/quartznet/quartznet/tree/main/database/tables).

## Defaults that changed

Almost none of them did. Every option that carried a default on 3.x carries the same one on 4.x — the
thread pool's size, the ADO store's misfire threshold and misfire batch, the retry intervals, the table
prefix, the cluster check-in interval, the hosted service's two switches. If a value matters to you and
you set it, nothing here applies.

Three effective defaults did change, and they change for one group of users only: those who never
configured Quartz at all. `StdSchedulerFactory.Initialize()` seeded them from an embedded `quartz.config`
that 4.x does not read, so a scheduler built by `new StdSchedulerFactory()` or
`GetDefaultScheduler()` — and nothing else — was running on them.

| Setting | 3.x, unconfigured | 4.x |
|---|---|---|
| `quartz.scheduler.instanceName` | `DefaultQuartzScheduler` | `QuartzScheduler` (`QuartzSchedulerOptions.InstanceName`). **Persistent stores key every row on this**, so an upgraded application that never named its scheduler stops seeing its own jobs and triggers until it sets the old name back |
| `quartz.jobStore.misfireThreshold` | 60 seconds | 5 seconds (`InMemoryJobStoreOptions.MisfireThreshold`). This was always the in-memory store's *code* default on both branches; the embedded file was what raised it |
| `quartz.threadPool.threadCount` | 10 | 10 (`ThreadPoolOptions.MaxConcurrency`) — unchanged, listed so the table is the whole of it |

Handing the 3.x factory properties always bypassed the file, so `new StdSchedulerFactory(properties)`
and `AddQuartz` already fell back to the typed defaults. See
[The `quartz.config` file is no longer read](#the-quartz-config-file-is-no-longer-read).

One default is new rather than changed: `QuartzSchedulerOptions.PropagateTraceContext` is on, so a
trigger scheduled inside an `Activity` carries two extra entries in its data map. See
[Silent behaviour changes](#silent-behaviour-changes).

## Silent behaviour changes

Most of this guide is about code that stops compiling, which is the easy half: the compiler says where to
look. This section is the other half — **calls that still compile and now do something different**. Read
it before the first run of the upgraded application rather than after it.

### Reading job data

Five of these are in one place, because 3.x had two overlapping accessor sets and 4.x kept the shorter
names with the longer set's semantics. If you called `GetIntValue`, `GetBooleanValue` and friends, nothing
here moved for you; if you called `GetInt` and `GetBoolean`, all of it did.

* **The indexer throws on a missing key.** `map["absent"]` returned `null` on 3.x. On 4.x it goes straight
  to the backing dictionary and throws `KeyNotFoundException`, on both `JobDataMap` and `SchedulerContext`.
  The "read an optional entry" idiom has to become `TryGetValue` or a `TryGet…` accessor.
* **`GetString` no longer throws.** 3.x threw `KeyNotFoundException` for an absent key and
  `InvalidCastException` for a value that was not a string. 4.x returns `null` for both, so a mistyped key
  and a wrongly-typed value are indistinguishable from "no value".
* **The typed accessors throw a different exception for an absent key.** `GetInt`, `GetDouble`,
  `GetBoolean` and the rest threw `KeyNotFoundException` on 3.x and throw `InvalidCastException` on 4.x.
  A `catch (KeyNotFoundException)` around one stops catching.
* **`GetBoolean` reads anything that is not `"true"` as `false`.** 3.x used `Convert.ToBoolean`, so a
  stored `"1"` or `"yes"` threw `InvalidCastException`. 4.x compares against `"true"` case-insensitively
  and *succeeds*, so a flag stored as `"1"` used to fail loudly and now quietly reads as off.
  `TryGetBoolean` reports success for it too.
* **Strings are parsed with the invariant culture.** `GetInt`, `GetDouble`, `GetFloat` and `GetDecimal`
  used the current culture when the stored value was a string. On a comma-decimal culture `"3.14"` used to
  read as `314` and now reads as `3.14`; `"3,14"` used to read as `3.14` and now throws. The new reading is
  the correct one, and it is still a different number from the one the job saw yesterday.
* **`TryGetDateTime` parses with `DateTimeStyles.RoundtripKind`**, so a stored string ending in `Z` comes
  back as `Kind=Utc` rather than shifted to local time — see
  [`PutAsString` writes round-trip formats now](#putasstring-writes-round-trip-formats-now).
* **The scheduler context is no longer merged into job properties** — see
  [Scheduler context entries are no longer injected into job properties](#scheduler-context-entries-are-no-longer-injected-into-job-properties).

### Serialization and the store

* **`quartz.serializer.type = json` means System.Text.Json now.** On 3.x the `json` alias expanded to the
  Newtonsoft serializer, with a logged warning that the name was ambiguous. On 4.x `json` and `stj` both
  resolve to `SystemTextJsonObjectSerializer`, and nothing warns. A configuration file carried over
  verbatim therefore changes which serializer writes and reads the store's blobs, which is a different
  payload shape and a different set of registered trigger and calendar serializers. Spell it `newtonsoft`
  if Newtonsoft is what you meant.
* **Both serializers refuse a job data value they cannot read back**, at write time rather than at the next
  read, and both refuse the same set — see
  [Both serializers refuse a job data value they cannot read back](#both-serializers-refuse-a-job-data-value-they-cannot-read-back).
* **A `Dictionary<string, string>` job data value is written the same way by both serializers**, which is
  a change to what Newtonsoft writes — see
  [A string dictionary is written the same way by both serializers](#a-string-dictionary-is-written-the-same-way-by-both-serializers).
* **Five calendar types serialize to a new shape.** 4.x reads both forms and 3.x reads only its own, so
  this is invisible on a clean cut-over and fatal in a mixed window — see
  [A mixed 3.x and 4.0 window](operations.md#a-mixed-3-x-and-4-0-window).
* **The formerly optional columns are required**, and an unmigrated schema fails at the first trigger load
  or store with a provider-level missing-column error rather than at startup — see
  [The optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone).
* **The two stores now answer the same way** where they used to disagree, which changes what the ADO store
  does in six places — see [The two job stores answer the same way](#the-two-job-stores-answer-the-same-way).

### Lifecycle and telemetry

* **`Standby()` after `Shutdown()` throws** instead of silently pausing something already stopped — see
  [The transitions are honest about themselves](#the-transitions-are-honest-about-themselves).
* **A trigger scheduled inside an `Activity` carries two extra job-data entries.**
  `QuartzSchedulerOptions.PropagateTraceContext` is on by default, and the two reserved keys are visible
  wherever trigger data is: `MergedJobDataMap`, the dashboard, `GET /triggers`. Turn it off with
  `q.ConfigureScheduler(options => options.PropagateTraceContext = false)`.

### The end time is the last instant at which a trigger may fire

`EndTimeUtc` means one thing on 4.x, for every trigger type: it is the last instant at which the trigger
may fire, so a fire time exactly equal to it is one the trigger fires and the first instant after it is
where the schedule stops. That is what Java Quartz's `AbstractTrigger` has always documented — "the time
after which the trigger will not fire" — and on 3.x the types disagreed about it. Three of them change:

| Trigger | 3.x | 4.x |
| --- | --- | --- |
| `SimpleTriggerImpl` | A fire time equal to `EndTimeUtc` is dropped | It fires |
| `CalendarIntervalTriggerImpl` | A fire time equal to `EndTimeUtc` is dropped | It fires |
| `DailyTimeIntervalTriggerImpl` | Fires *past* `EndTimeUtc` until the daily window closes, when the end time falls between two fire times; `FinalFireTimeUtc` reports the close of the daily window even when that is past `EndTimeUtc` | Firing stops at the end time, and `FinalFireTimeUtc` is never past it |
| `CronTriggerImpl` | A fire time equal to `EndTimeUtc` fires | Unchanged |
| `RecurrenceTriggerImpl` | New in 4.x | A fire time equal to `EndTimeUtc` fires |

A simple or calendar-interval trigger whose `EndAt` lands exactly on one of its fire times therefore fires
one more time than it did on 3.x, and a daily-time-interval trigger whose `EndAt` lands between two of them
fires fewer. Move the end time a second off the boundary to keep the old count.

`TriggerFireTimes.ComputeBetween` inherits the rule, because it bounds the walk by handing the window's end
to the trigger as its `EndTimeUtc`: its `to` is inclusive, for every trigger type, and a fire time landing
exactly on it is listed.

### Ordering

* **`JobKey` and `TriggerKey` compare ordinally**, where 3.x compared with the current culture, so any
  sorted listing of keys comes out in a different order — see
  [`Key<T>` moved to `Quartz` and is immutable](#key-t-moved-to-quartz-and-is-immutable).

### What is on nobody's list because it did not change

Worth stating, because the reasonable fear is that everything moved. These were checked against both
branches and are identical: every misfire instruction's numeric value, so a `MISFIRE_INSTR` column carried
over still means what it meant; `TriggerState`'s existing values, with `Executing` appended; the misfire
computation of every trigger family; the merge order and content of `MergedJobDataMap`; the rule that
`[PersistJobDataAfterExecution]` writes back only a map that was modified; the lock handler a store picks
for itself; `StringOperator`'s matching; and the effective default of `Shutdown(waitForJobsToComplete)`.

## Configuration

This is the largest change in 4.x. Configuration is now strongly typed options and service
registrations rather than a bag of `quartz.*` strings, and the dependency injection container builds
the scheduler.

### Flat keys still work

If you configure Quartz from `appsettings.json` or a `NameValueCollection` of `quartz.*` keys, that
keeps working. The keys are translated into the typed options, and both spellings of a setting always
produce the same result. You do not have to migrate configuration files to move to 4.x.

### A flat property bag is checked for keys nobody reads

`AddQuartz(services, properties)` and `AddQuartz(services, name, properties)` check the bag the same
way `QuartzSchedulerBuilder.UseProperties` always has: a `quartz.*` key Quartz does not read throws
`SchedulerConfigException` at registration instead of being silently ignored, and a key 4.0 stopped
reading is reported by name with the replacement. That is the commonest shape a 3.x application
arrives in, so it is the one the advice is written for — `quartz.jobstore.type` differs from
`quartz.jobStore.type` by one letter and used to turn a database-backed scheduler into an in-memory
one without a word.

Set `quartz.checkConfiguration` to `false` to allow keys of your own. The `IConfiguration` overloads
are still unchecked, because flattening a section invents `quartz.*` keys whether Quartz reads them
or not.

### A property key without the `quartz.` prefix is refused

The check above is on the bag handed to `AddQuartz`. The other way into that bag is
`services.Configure<QuartzOptions>(…)`, and it was not checked at all. `QuartzOptions.Properties` holds
the flat `quartz.*` keys and `QuartzPropertyBridge` is its only reader, so a key without that prefix is
provably read by nobody — and unlike a misspelling, it has no symptom at all: the scheduler runs as if
the application had said nothing. An `IValidateOptions<QuartzOptions>` now refuses one, at host startup
along with the rest of the options validation:

```diff
  services.Configure<QuartzOptions>(options =>
  {
-     options.Properties["scheduler.instanceName"] = "svc";
+     options.Properties["quartz.scheduler.instanceName"] = "svc";
  });
```

The commonest way to produce such a key is `services.Configure<QuartzOptions>(section)` against a
section written for the 3.x `QuartzOptions`, which *was* a dictionary — a section already named
`Quartz` does not supply the prefix, so `Quartz:Properties:scheduler.instanceName` lands as
`scheduler.instanceName`. Note that the check cannot catch the whole of that mis-bind: a section whose
keys match no property on `QuartzOptions` binds to nothing at all, and an options instance that came
out empty is indistinguishable from one nobody configured. What is decidable is the keys that did
arrive, and that is what is checked. Bind the section with `AddQuartz(configuration)` — the call that
understands its shape — rather than with `Configure<QuartzOptions>`.

Unlike the registration-time check, this one does **not** compare the key against the ones Quartz
reads: a configuration section is flattened into this bag with every sub-section turned into a
`quartz.*` key whether Quartz reads it or not, so the stricter check would fail an `appsettings.json`
holding settings for something else. `quartz.checkConfiguration = false` turns it off too.

### A property bag is any dictionary

`NameValueCollection` is a .NET Framework type from the `<appSettings>` era, and requiring it at the
front door made every caller who already had a dictionary build one to get in. The parameter is now
`IEnumerable<KeyValuePair<string, string?>>` — the shape `Dictionary<string, string?>`,
`IReadOnlyDictionary<string, string?>` and `QuartzOptions.Properties` all already have, and the one
`AddInMemoryCollection` takes.

```csharp
services.AddQuartz(new Dictionary<string, string?>
{
    ["quartz.scheduler.instanceName"] = "core",
    ["quartz.threadPool.maxConcurrency"] = "10"
});
```

A `NameValueCollection` still goes in unchanged, in one call. It is exactly what a 3.x application
holds — `StdSchedulerFactory` took one — so both `UseProperties` and `AddQuartz` keep an overload for
it that forwards to the primary shape.

| Member | 3.x / earlier 4.0 preview | 4.0 |
|---|---|---|
| `QuartzSchedulerBuilder.UseProperties` | `UseProperties(NameValueCollection)` | `UseProperties(IEnumerable<KeyValuePair<string, string?>>)`, plus the `NameValueCollection` overload |
| `AddQuartz(services, properties, …)` | `NameValueCollection` | `IEnumerable<KeyValuePair<string, string?>>`, plus the `NameValueCollection` overload |
| `AddQuartz(services, name, properties, …)` | `NameValueCollection` | `IEnumerable<KeyValuePair<string, string?>>`, plus the `NameValueCollection` overload |
| `QuartzOptions.ToNameValueCollection()` | returned a `NameValueCollection` | `QuartzOptions.ToProperties()`, returning a `Dictionary<string, string?>` that goes straight back into either of the above |

`KeyValuePair<TKey, TValue>` is a struct, so `IEnumerable<KeyValuePair<string, string>>` is not
convertible to `IEnumerable<KeyValuePair<string, string?>>`. A `Dictionary<string, string>` whose
values are genuinely never null therefore needs one conversion:

```diff
- services.AddQuartz(settings);
+ services.AddQuartz(settings.ToDictionary(x => x.Key, x => (string?) x.Value));
```

`AddQuartz(services, properties)` also copies the bag now, as `UseProperties` always has. The
registration phases read it from closures that run later, some only when the container resolves
options, so a caller that reused its own collection could reconfigure the scheduler long after
`AddQuartz` returned.

### Interrupting jobs on shutdown is one setting with four answers

`QuartzSchedulerOptions.InterruptJobsOnShutdown` and `InterruptJobsOnShutdownWithWait` were two
booleans, one for a shutdown that waits for running jobs and one for a shutdown that does not. They
were never independent — together they answered a single four-way question, and setting one without
thinking about the other was the easy mistake, because `InterruptJobsOnShutdown = true` reads as "yes,
interrupt" and means "only when not waiting".

They are replaced by `ShutdownJobInterruption`, an enum with all four answers spelled out:

| Value | Old pair |
|---|---|
| `Never` (default) | both `false` |
| `WhenNotWaitingForJobs` | `InterruptJobsOnShutdown = true` |
| `WhenWaitingForJobs` | `InterruptJobsOnShutdownWithWait = true` |
| `Always` | both `true` |

```diff
  q.ConfigureScheduler(options =>
  {
-     options.InterruptJobsOnShutdown = true;
-     options.InterruptJobsOnShutdownWithWait = true;
+     options.ShutdownJobInterruption = ShutdownJobInterruption.Always;
  });
```

Configuration files need no change. Both flat keys still work and map onto the enum by the table above,
and `Scheduler:InterruptJobsOnShutdown: true` in `appsettings.json` still means what it meant: the
typed binder no longer knows that name, but every section is also flattened onto its `quartz.*` key,
and the property bridge reads it there. `Scheduler:ShutdownJobInterruption: "Always"` is the new
spelling, and binds directly.

### A setting stops wearing a verb

`IPersistentStoreBuilder.AcceptEnlistedTransactions()` was exactly
`ConfigureStore(options => options.AcceptEnlistedTransactions = true)` — one assignment, no side effect —
while the other nineteen `AdoJobStoreOptions` settings, `UseDbLocks` and `StoreJobDataAsStrings` among them,
are set through `ConfigureStore` like everything else. A reader of the interface reasonably concluded that
the settings with verbs were the important ones.

```diff
  q.UsePersistentStore(store =>
  {
      store.UsePostgres(connectionString);
-     store.AcceptEnlistedTransactions();
+     store.ConfigureStore(options => options.AcceptEnlistedTransactions = true);
  });
```

The option, the configuration key `quartz.jobStore.acceptEnlistedTransactions` and the
`JobStore:AcceptEnlistedTransactions` section entry are all unchanged. `UseClustering()` keeps its
verb: it genuinely does more than one assignment, setting `Enabled` on `ClusteringOptions` and
`UseDbLocks` on `AdoJobStoreOptions`.

### Two names that said the wrong thing

| Was | Is | Why |
|---|---|---|
| `AdoJobStoreOptions.UseProperties` | `AdoJobStoreOptions.StoreJobDataAsStrings` | `UseProperties` reads as a verb and collided with `QuartzSchedulerBuilder.UseProperties` and `AddQuartz(properties)`, which are about flat `quartz.*` configuration keys and have nothing to do with how job data is persisted |
| `Matchers.Group<TKey>(@operator, …)` / `Matchers.Name<TKey>(@operator, …)` | the parameter is `matchOperator` | `operator` is a C# keyword, so naming the argument meant writing `@operator:` at the call site |

```diff
  q.UsePersistentStore(store =>
  {
      store.UseSqlServer(connectionString);
-     store.Configure(options => options.UseProperties = true);
+     store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
  });

- Matchers.Group<JobKey>(@operator: StringOperator.StartsWith, compareTo: "reports");
+ Matchers.Group<JobKey>(matchOperator: StringOperator.StartsWith, compareTo: "reports");
```

The flat key is unchanged: `quartz.jobStore.useProperties` still sets `StoreJobDataAsStrings`. In
`appsettings.json` the section entry follows the option, so `JobStore:UseProperties` becomes
`JobStore:StoreJobDataAsStrings` — the old spelling still works, because every section is also flattened
onto its `quartz.*` key and read there.

### The persistent store builder says which thing it configures

Three members of `IPersistentStoreBuilder` did not follow the conventions the rest of it set.

| 4.0 preview | 4.0 |
|---|---|
| `store.Configure(options => …)` | `store.ConfigureStore(options => …)` |
| `store.UseDataSourceName(name)` | `store.UseDataSource(name)` |
| — | `store.UseDriverDelegate(factory)`, new |

`Configure` was a bare verb inside a callback that already has `ConfigureScheduler` and
`ConfigureOptions<TOptions>` in scope, so it said nothing about which of the things in reach it
configured. `UseDataSourceName` and `UseDataSource` were two names for one concept, and naming a data
source is the string overload of defining one. `UseDriverDelegate` was the only registration on the
builder without a `Func<IServiceProvider, T>` overload — `UseConnectionProvider`, `UseSerializer`,
`UseLockHandler` and `UseTriggerPersistenceDelegate` all had one — which made a dialect that needs
anything from the container the one part of the store that could not be built from it.

```diff
  q.UsePersistentStore(store =>
  {
-     store.UseDataSourceName("reporting-db");
+     store.UseDataSource("reporting-db");
      store.UseSqlServer(connectionString);
-     store.Configure(options => options.TablePrefix = "RPT_QRTZ_");
+     store.ConfigureStore(options => options.TablePrefix = "RPT_QRTZ_");
  });
```

No configuration key moved. `quartz.jobStore.*`, `Quartz:JobStore:*` and `quartz.jobStore.dataSource`
all say what they said, so only code that calls these methods has anything to change.

### The hosted service's extension point is its four hooks

`QuartzHostedService.StartAsync` and `StopAsync` are no longer `virtual`. They maintain a private list
of schedulers and an internal startup task, so an override that did not call base left the schedulers
bound to the repository with nothing able to shut them down — a failure that surfaces at process exit,
long after the code that caused it.

The four lifecycle hooks `StartingAsync`, `StartedAsync`, `StoppingAsync` and `StoppedAsync` stay
`virtual`; they are no-ops that exist for nothing else, and they are what #2386 and #2522 asked for.
Work in an overridden `StartAsync` moves into `StartingAsync` or `StartedAsync`, and work in
`StopAsync` into `StoppingAsync` or `StoppedAsync`. What a subclass was reaching into `StartAsync` for
is now a member of its own: `protected IReadOnlyList<IScheduler> Schedulers`, a snapshot of the
schedulers the service is running.

### A shipped component is configured through its options type, and only there

Every configurable component Quartz ships published its settings twice: once on an options type bound
from configuration and validated, and once as public settable properties on the component itself,
assignable on a live instance with nothing saying which wins or when. The component-side setters are
`internal` now, leaving the options type as the one public way:

| Component | Configure through |
|---|---|
| `RAMJobStore.MisfireThreshold` | `UseInMemoryStore(o => o.MisfireThreshold = …)` |
| `AdoJobStoreBase.MisfireThreshold` | `UsePersistentStore(store => store.ConfigureStore(o => o.MisfireThreshold = …))` |
| `TaskSchedulingThreadPool.MaxConcurrency`, `.Scheduler` | `UseDefaultThreadPool(maxConcurrency: …)`; both setters are `protected internal`, so a pool of your own can still set them |
| `RedisLockHandler.RedisConfiguration`, `.KeyPrefix`, `.LockTimeToLive`, `.LockRetryInterval` | `UseRedisLockHandler(o => …)` |
| The history plugins' message templates | `UseJobHistoryLogging(o => …)`, `UseTriggerHistoryLogging(o => …)`, and now `UseStructuredJobLogging(o => …)` / `UseStructuredTriggerLogging(o => …)` |
| The scheduling-data plugins' `FileNames`, `ScanInterval`, `FailOn*` (internal outright — see [one surface](#the-two-scheduling-data-plugins-have-one-surface)) | `UseXmlSchedulingConfiguration(o => …)` / `UseJsonSchedulingConfiguration(o => …)` |
| `ShutdownHookPlugin.CleanShutdown` | `UseShutdownHook(o => o.CleanShutdown = …)` |

The two shipped row-lock handlers keep their retry knobs, but as `init`-only properties rather than
setters: `SelectForUpdateLockHandler` has `MaxRetry` and `RetryPeriod`, and `UpdateRowLockHandler` has
`RetryPeriod` — it gained one, having previously hard-coded a second. Set them at construction, or through
`quartz.jobStore.lockHandler.maxRetry` / `.retryPeriod`, which the binder still writes.

**Flat-key configuration is unaffected.** `quartz.plugin.<name>.<property>` and
`quartz.jobStore.lockHandler.<property>` write the component directly through reflection, and that
binder now binds non-public setters — so every string configuration that worked keeps working, and
only the code path that bypassed the options type is closed.

`UseStructuredJobLogging` and `UseStructuredTriggerLogging` gain the `configure` delegate their
non-structured siblings always had.

Two collections stop being assignable, for the reason `QuartzOptions.Properties` never was — one
`configure` callback must not be able to discard what another added, and the configuration binder needs
no setter to bind into a non-null collection:

```diff
- x.Files = ["~/quartz_jobs.xml"];
+ x.Files.Add("~/quartz_jobs.xml");

- options.Tags = ["ready", "live"];
+ options.Tags.AddRange(["ready", "live"]);
```

`FileSchedulingOptions.Files` is a `List<string>` rather than a `string[]`, and
`QuartzHealthCheckOptions.Tags` a `List<string>` rather than an `IReadOnlyCollection<string>`.

### The provider names have constants

`DataSourceOptions.Provider` is a string naming an ADO.NET driver description, and the eight names
Quartz ships one for were discoverable only from a documentation table or by reading a property file.
They are constants now — `DataSourceOptions.Providers.SqlServer`, `.Npgsql`, `.MySql`,
`.MySqlConnector`, `.Oracle`, `.Sqlite`, `.SystemDataSqlite`, `.Firebird` — and the `UseSqlServer` /
`UsePostgres` / … extensions pass them rather than repeating literals.

The property stays a `string`: the set is not closed, since a driver Quartz knows nothing about is
describable through `UseGenericDatabase`. The values are unchanged, so a configuration file naming one
of them is unaffected.

### The standalone builder reads a configuration section

`QuartzSchedulerBuilder.UseConfiguration(IConfiguration)` is new, and is the standalone counterpart of
`AddQuartz(configuration)`: it binds the typed options, reads a `Schedule` section, and translates the
flat `quartz.*` keys, exactly as the hosted path does.

```diff
- var properties = QuartzConfigurationHelper.ToNameValueCollection(configuration.GetSection("Quartz"));
- var factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
+ var factory = QuartzSchedulerBuilder.Create().UseConfiguration(configuration.GetSection("Quartz")).Build();
```

`QuartzConfigurationHelper` — whose one public method existed for that flatten-then-configure sample —
is internal. `UseProperties(NameValueCollection)` is unchanged and is still the way in for a bag you
built yourself, from a properties file or from environment variables.

### Two scheduler thread settings were dead and are gone

The scheduling loop is a long-running `Task`, not a `Thread`, so neither of these ever did anything
in 4.0 — one named a thread that does not exist, the other tried to stop a thread that does not exist
from keeping the process alive.

| Removed | Use instead |
|---|---|
| `QuartzSchedulerOptions.ThreadName` / `quartz.scheduler.threadName` | nothing |
| `QuartzSchedulerOptions.MakeSchedulerThreadDaemon` / `quartz.scheduler.makeSchedulerThreadDaemon` | nothing for the scheduler; `AdoJobStoreOptions.UseBackgroundThreads` for the store's threads |

Both keys are in the removed-key table, so a properties bag that still carries one is told why rather
than told it is unknown.

The setting that does still matter is the job store's, and it is renamed to say what .NET calls it:

| Before | After |
|---|---|
| `AdoJobStoreOptions.MakeThreadsDaemons` | `AdoJobStoreOptions.UseBackgroundThreads` |

The flat key `quartz.jobStore.makeThreadsDaemons` is unchanged and still sets it. It governs the
misfire handler and the cluster manager, which are the only real threads Quartz creates — so it is now
the whole answer to "do Quartz's threads hold my console application open".

### `PerformSchemaValidation` became `SchemaProvisioning`, which has a third position

A persistent store can now create its own schema ([#3531](https://github.com/quartznet/quartznet/issues/3531)),
which the old flag had nowhere to say. The check and the creation are one decision — a store that may
create its schema obviously validates it too — so they are one setting with three positions rather than
two flags that can contradict each other.

| Before | After |
|---|---|
| `AdoJobStoreOptions.PerformSchemaValidation = true` | `AdoJobStoreOptions.SchemaProvisioning = SchemaProvisioning.Validate` (the default) |
| `AdoJobStoreOptions.PerformSchemaValidation = false` | `AdoJobStoreOptions.SchemaProvisioning = SchemaProvisioning.None` |
| — | `AdoJobStoreOptions.SchemaProvisioning = SchemaProvisioning.CreateIfMissing`, or `store.ProvisionSchema()` |

The flat key `quartz.jobStore.performSchemaValidation` still works and still means what it meant:
`true` bridges to `Validate` and `false` to `None`. `quartz.jobStore.schemaProvisioning` is the new key
and takes a member name — `None`, `Validate` or `CreateIfMissing`, case-insensitively. A file carrying
both is decided by the new key, since it is the only one of the two that can express the third position.

`CreateIfMissing` only ever creates: no object is altered and none is dropped, so it is safe to leave on
against a schema that already exists and cannot turn a mis-typed `TablePrefix` into data loss — though it
will happily create a second, empty schema under the mis-typed one. It is equally **not** an upgrade: a
schema that has every table but is missing a column a later release added is left exactly as it is.
`database/migrations/` is still what moves a schema forward, and the 3.x → 4.0 upgrade is still mandatory.

It is not the default, because creating tables needs DDL permission that a production database is usually
right not to grant. When the account has none, the failure names the fresh-install script for your
database and says to run it and drop back to `Validate`.

`IDriverDelegate` gained the member behind it:

```csharp
ValueTask CreateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);
```

A delegate deriving from `StdAdoDelegate` inherits an implementation that runs an embedded script, and
names the script by overriding `protected virtual string? SchemaResourceName`. The six shipped delegates
each name their own; one that names none throws when asked to provision, saying so, rather than reporting
success and creating nothing.

### Code-first configuration is typed

Settings that used to be write-only properties on the configurator are now options:

```diff
  services.AddQuartz(q =>
  {
-     q.ConfigureScheduler(options => options.InstanceName = "core");
-     q.ConfigureScheduler(options => options.InstanceId = "node-1");
-     q.MaxBatchSize = 5;
-     q.InterruptJobsOnShutdown = true;
+     q.ConfigureScheduler(options =>
+     {
+         options.InstanceName = "core";
+         options.InstanceId = "node-1";
+         options.MaxBatchSize = 5;
+         options.ShutdownJobInterruption = ShutdownJobInterruption.WhenNotWaitingForJobs;
+     });
  });
```

The option names are the same words as the configuration keys, so `Quartz:Scheduler:MaxBatchSize` and
`options.MaxBatchSize` are the same setting said two ways.

### Data sources no longer need a name

A scheduler has one job store and therefore one database, so there is no name to invent:

```diff
  q.UsePersistentStore(store =>
  {
-     store.UseProperties = true;
      store.UseClustering();
-     store.UseSqlServer("sql-server-01", connectionString);
+     store.UseSqlServer(connectionString);
      store.UseSystemTextJsonSerializer();
+     store.ConfigureStore(options => options.StoreJobDataAsStrings = true);
  });
```

Schedulers that need different databases are registered under different names, and each gets its own
services.

### The quartz.config file is no longer read

Nothing is loaded from disk any more. A `quartz.config` file next to your application — or named by the
`quartz.config` environment variable — is ignored, and so is the copy of it Quartz used to ship as an
embedded resource. A scheduler is configured by the properties you hand to
`QuartzSchedulerBuilder.UseProperties`, by an `IConfiguration` section passed to `AddQuartz`, or in code
through the container.

The three settings the embedded file supplied are not defaults any more either. They were seeded by
`StdSchedulerFactory.Initialize()`, which is the only entry point that ever read the file, and which is
gone with the factory — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone):

| Setting | Old embedded value | Default now |
|---|---|---|
| `quartz.scheduler.instanceName` | `DefaultQuartzScheduler` | `QuartzScheduler` (`QuartzSchedulerOptions.InstanceName`) |
| `quartz.threadPool.threadCount` | 10 | 10 (`ThreadPoolOptions.MaxConcurrency`) |
| `quartz.jobStore.misfireThreshold` | 60000 | 5 seconds (`InMemoryJobStoreOptions.MisfireThreshold`) |

Note these were never the defaults for `AddQuartz` or for `new StdSchedulerFactory(properties)` in the
first place: handing the factory properties always bypassed the file, so those paths fell back — and
still fall back — to the typed option defaults. Set them explicitly if you want the other values.

The one thing the file was still needed for was describing an ADO.NET driver Quartz ships no metadata
for. That now has a code-first form, and the `quartz.dbprovider.*` keys themselves still work — they just
arrive through `IConfiguration` or a `NameValueCollection` like every other key:

```csharp
q.UsePersistentStore(store => store.UseGenericDatabase("MyDatabase", connectionString, () => new DbMetadata
{
    ProductName = "My Database",
    ConnectionType = typeof(MyConnection),
    CommandType = typeof(MyCommand),
    ParameterType = typeof(MyParameter),
    ParameterDbType = typeof(MyDbType),
    ParameterDbTypePropertyName = nameof(MyParameter.MyDbType),
    ParameterNamePrefix = "@",
    DbBinaryTypeName = "VarBinary",
}));
```

See [the configuration reference](configuration/reference.md#describing-a-driver-quartz-does-not-know) for the
full description. `DbProvider.RegisterDbMetadata` is gone with the process-wide lookup it wrote into; use
the callback above, once per driver.

### `QuartzOptions` is no longer a dictionary

`QuartzOptions` used to derive from `Dictionary<string, string?>`, and to hold a scheduler's jobs and
triggers as well as its flat keys. It was the pivot the whole configuration model turned on; now that
settings are typed options, the only things left in it were the legacy keys and the one thing that was
never configuration at all. The keys moved to a `Properties` dictionary, and jobs and triggers became a
per-scheduler registration like every other part of a scheduler.

```diff
  services.Configure<QuartzOptions>(options =>
  {
-     options["quartz.plugin.jobHistory.type"] = "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins";
+     options.Properties["quartz.plugin.jobHistory.type"] = "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins";
  });
```

A plugin whose type you know is better added as one, which also gives it constructor injection:

```csharp
services.AddQuartz(q => q.AddPlugin<LoggingJobHistoryPlugin>());
```

Because the keys are a property rather than the object itself, a section of flat keys no longer binds
onto `QuartzOptions` directly — it would bind `Quartz:Properties:*`. Pass the section to `AddQuartz`,
which reads the keys where they have always been and binds the typed options from the same section:

```diff
- services.Configure<QuartzOptions>(configuration.GetSection("Quartz"));
  services.AddQuartz(configuration.GetSection("Quartz"), q => { /* ... */ });
```

Jobs and triggers are added through the builder. The overloads taking an `IServiceProvider` cover the
case the options callback used to be needed for:

```diff
- services.AddOptions<QuartzOptions>()
-     .Configure<IOptions<SampleOptions>>((options, sample) =>
-     {
-         options.AddJob<ExampleJob>(j => j.WithIdentity("job", "group"));
-         options.AddTrigger(t => t
-             .ForJob("job", "group")
-             .WithCronSchedule(sample.Value.CronSchedule));
-     });
+ services.AddQuartz(q =>
+ {
+     q.AddJob<ExampleJob>(j => j.WithIdentity("job", "group"));
+     q.AddTrigger((provider, t) => t
+         .ForJob("job", "group")
+         .WithCronSchedule(provider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule));
+ });
```

`QuartzOptions.SchedulerName` used to read and write `schedName` — an ADO.NET column key that nothing
reads — so a scheduler name set through it was accepted and then silently ignored. It is gone along with
`SchedulerId` and `MisfireThreshold`; see
[`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings).

### Removed

| Removed | Use instead |
|---|---|
| `QuartzOptions : Dictionary<string, string?>` | `QuartzOptions.Properties` |
| `QuartzOptions.JobDetails`, `.Triggers`, `.AddJob`, `.AddTrigger` | `AddQuartz(q => q.AddJob(…))` / `q.AddTrigger(…)` |
| `StdSchedulerFactory` and its 47 constants | `QuartzSchedulerBuilder.UseProperties(properties)` — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone) |
| `StdSchedulerFactory.PropertySchedulerName` | nothing; it named an ADO.NET column, not a setting |
| `SchedulerBuilder` | `QuartzSchedulerBuilder` for standalone use, `AddQuartz` under a host |
| `DirectSchedulerFactory` | `QuartzSchedulerBuilder`, with `UseThreadPool(IThreadPool)` / `UseJobStore(IJobStore)` for pre-built parts |
| `IPropertyConfigurer`, `IPropertySetter`, `IPropertyConfigurationRoot`, `PropertiesHolder`, `PropertiesSetter` | typed options |
| `SchedulerBuilder.UseZeroSizeThreadPool()` | `UseThreadPool<ZeroSizeThreadPool>()` — the pool type is still public, only the shorthand is gone |
| `SchedulerBuilder.UseDedicatedThreadPool()` | `UseDefaultThreadPool(…)`; `DedicatedThreadPool` is internal, because a dedicated-thread `TaskScheduler` is no longer how a thread pool is written — see [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `SchedulerPluginConfigurationExtensions.UsePlugin<T>(name)` | `AddPlugin<T>(name)` on the builder — see [Plugins are registered like listeners](#plugins-are-registered-like-listeners) |
| `SchedulerPluginConfigurationExtensions.TryRegisterSingleton<TService, TImplementation>()` | `builder.Services.TryAddSingleton<TService, TImplementation>()`; the builder no longer wraps the container's own verbs |
| `AddQuartz(Action<configurator, IServiceProvider>)` | see below |
| `quartz.config` file discovery, `StdSchedulerFactory.PropertiesFile` | `IConfiguration`, or properties passed to `QuartzSchedulerBuilder.UseProperties` |
| `DbProvider.RegisterDbMetadata` | the metadata factory on `UseGenericDatabase` |
| `quartz.scheduler.proxy*`, `quartz.scheduler.exporter*` | nothing; remoting is not supported on modern .NET |
| `QuartzOptions.SchedulerName`, `.SchedulerId`, `.MisfireThreshold` | the typed options — see [`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings) |
| `IPersistentStoreBuilder.UseDataSourceConnectionProvider()` | `DataSourceOptions.UseRegisteredDataSource` |
| `AdoJobStoreOptions.Clustered`, `.ClusterCheckinInterval`, `.ClusterCheckinMisfireThreshold` | `ClusteringOptions` — see [Clustering is configured in one place](#clustering-is-configured-in-one-place) |
| `SchedulerRepository.Instance` | `ISchedulerRepository` resolved from the container |
| `DBConnectionManager.Instance` | nothing; register a provider with `UseConnectionProvider` and resolve `IDbProvider` from the container — see [The connection manager is gone](#the-connection-manager-is-gone) |
| `StdSchedulerFactory.GetDbConnectionManager()`, `.GetSchedulerRepository()` | `IDbProvider`, keyed by scheduler name / `ISchedulerRepository`, both resolved from the container |

### Deferred configuration

The `AddQuartz` overloads taking an `IServiceProvider` are gone. They existed to reach services while
configuring, which the options pattern already does:

```diff
- services.AddQuartz((q, provider) =>
- {
-     var connectionString = provider.GetRequiredService<IConfiguration>().GetConnectionString("Scheduler");
-     q.UsePersistentStore(store => store.UseSqlServer("default", connectionString));
- });
+ var connectionString = builder.Configuration.GetConnectionString("Scheduler");
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(connectionString)));
```

For an option that genuinely depends on a service, use the options pattern directly:

```csharp
services.AddOptions<AdoJobStoreOptions>()
    .Configure<IMyService>((options, service) => options.TablePrefix = service.TablePrefix);
```

Listeners and plugins do not need this at all: they are registered services, so the container injects
their dependencies.

### SPI changes

If you implement `IJobStore` or `ISchedulerPlugin` yourself, they take their collaborators through
constructors now instead of being handed them afterwards.

`IJobStore` loses `InstanceId`, `InstanceName`, `ThreadPoolSize` and `TimeProvider`, and `Initialize`
takes the scheduler's identity in place of its collaborators:

```diff
- ValueTask Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default);
+ ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default);
```

Take what you need — `ISchedulerSignaler`, `ITypeLoader`, `TimeProvider`,
`IOptions<QuartzSchedulerOptions>` — through your constructor. The identity is the one thing a constructor
cannot carry, for the reason [If you implement `IJobStore`](#if-you-implement-ijobstore) gives. What
remains in `Initialize` is work that has to happen before the scheduler runs and cannot be done while
constructing, such as verifying a database schema.

Options arrive as the scheduler's own, whichever of the three interfaces you ask for.
`IOptionsMonitor<QuartzSchedulerOptions>` and `IOptionsSnapshot<QuartzSchedulerOptions>` work as well
as `IOptions<>`: `CurrentValue` and `Value` are the options of the scheduler your component belongs
to, `Get(name)` answers for the name you pass, and `OnChange` reports your scheduler's changes and
not another's.

Plugin configuration extension methods now extend `IQuartzBuilder` and register the plugin as a
service, rather than deriving from `PropertiesSetter` to write string keys.

### No process-global scheduler or connection state

`SchedulerRepository.Instance` and `DBConnectionManager.Instance` are gone. The repository is an ordinary
container registration now, which means **a scheduler is only visible in the repository belonging to the
container that built it**:

```diff
- var scheduler = SchedulerRepository.Instance.Lookup("reporting");
+ var scheduler = serviceProvider.GetRequiredService<ISchedulerRepository>().Lookup("reporting");
```

The connection manager did not become a container registration — it *was* one, holding a copy of what the
container already knew, and it is removed outright. Registering a connection provider is a store
configuration call, and reading one back is container resolution:

```diff
- DBConnectionManager.Instance.AddConnectionProvider("default", myProvider);
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider(_ => myProvider)));
```

See [The connection manager is gone](#the-connection-manager-is-gone) for the full picture.

The observable consequence is that schedulers built different ways no longer find each other. Given a
scheduler registered with `AddQuartz` and another built by a `QuartzSchedulerBuilder` in the same
process:

* `ISchedulerFactory.GetAllSchedulers()` on either one lists only its own schedulers.
* `ISchedulerFactory.LookupScheduler(name)` returns `null` for the other one's name.
* `ISchedulerRepository.Lookup(name)` likewise sees only its own container's schedulers.

If you were relying on that reach — typically to find a scheduler from code that had no reference to the
factory that created it — register the scheduler with `AddQuartz` and inject `IScheduler`,
`ISchedulerFactory` or `ISchedulerRepository` instead. If you genuinely need one repository across
several entry points, register your own instance before calling `AddQuartz`; every Quartz registration
is `TryAdd`, so yours wins:

```csharp
var repository = new SchedulerRepository();
services.AddSingleton<ISchedulerRepository>(repository);
services.AddQuartz(/* ... */);
```

A `QuartzSchedulerBuilder` owns the container it creates, so its repository holds only the schedulers it
built. `StdSchedulerFactory.GetSchedulerRepository()` and `GetDbConnectionManager()` went with the class;
resolve `ISchedulerRepository` from the container instead, and a scheduler's `IDbProvider` from the same
container under the scheduler's name as the service key.

## `StdSchedulerFactory` is gone

The properties-based factory has been removed. It was the last construction path that was not the
container: it parsed `quartz.*` strings, loaded types by name, set properties by reflection, and built a
scheduler of its own. Since 4.0 it did none of that itself — it built a service collection, handed the
properties to the same translation layer `AddQuartz` uses, and resolved the scheduler from the container
like everything else. What was left was a second front door onto one hallway, plus 47 public constants
whose only purpose was to spell keys that a configuration file spells anyway.

Flat `quartz.*` keys are **not** going away. What changed is which type you hand them to:

```diff
- ISchedulerFactory factory = new StdSchedulerFactory(properties);
- IScheduler scheduler = await factory.GetScheduler();
+ IScheduler scheduler = await QuartzSchedulerBuilder.Create()
+     .UseProperties(properties)
+     .BuildScheduler();
```

`UseProperties` feeds the same translator, so every key means exactly what it always did, including the
`quartz.checkConfiguration` check that rejects a misspelled key. Configuration written in code wins over
the properties whichever order the two are applied in: property-derived options are applied before
anything the builder was told, and implementations the properties name are registered after — options
being last-wins and registrations first-wins, the same rule `AddQuartz` follows.

Two behaviors did not survive, because they lived in `Initialize()` rather than in the properties:

* **The `quartz.*` environment-variable overlay.** `new StdSchedulerFactory()` with no arguments read
  every `quartz.*` environment variable. Nothing does that now. Use `IConfiguration` with
  `AddEnvironmentVariables()`, which is the ordinary way to say it, and pass the section to `AddQuartz`
  or flatten it yourself into the `NameValueCollection` you hand to `UseProperties`.
* **The embedded `quartz.config` defaults.** A factory constructed with no properties used to start from
  `instanceName = DefaultQuartzScheduler`, `threadCount = 10` and `misfireThreshold = 60000`. A builder
  given no properties starts from the typed option defaults instead — `InstanceName` `QuartzScheduler`,
  `MaxConcurrency` 10, and an in-memory `MisfireThreshold` of 5 seconds. Set them explicitly if you were
  relying on the old values. Note that this was never the behavior of `new StdSchedulerFactory(properties)`
  either, which always bypassed the file.

`IsSupportedConfigurationKey` is gone with the class, so a configuration carrying keys of your own can no
longer be allowed by subclassing the factory. Set `quartz.checkConfiguration` to `false` instead.

`GetDefaultScheduler()` is gone too. It returned a process-wide scheduler from a process-wide factory,
which stopped being a coherent idea when the repository became the container's — see
[No process-global scheduler or connection state](#no-process-global-scheduler-or-connection-state).
Build one scheduler where the application starts and hold it, or register it with `AddQuartz` and inject
`IScheduler`.

### Every removed constant

The strings are unchanged. This table is here so a `Ctrl+F` for the constant you used finds the key to
write instead, and the typed option that is usually the better answer.

| Removed constant | Key | Typed equivalent |
|---|---|---|
| `PropertySchedulerInstanceName` | `quartz.scheduler.instanceName` | `QuartzSchedulerOptions.InstanceName` |
| `PropertySchedulerInstanceId` | `quartz.scheduler.instanceId` | `QuartzSchedulerOptions.InstanceId` |
| `PropertySchedulerInstanceIdGeneratorPrefix` | `quartz.scheduler.instanceIdGenerator` | constructor injection into your `IInstanceIdGenerator` |
| `PropertySchedulerInstanceIdGeneratorType` | `quartz.scheduler.instanceIdGenerator.type` | register `IInstanceIdGenerator` in the container |
| `PropertySchedulerThreadName` | `quartz.scheduler.threadName` | nothing; the key is rejected rather than ignored. The scheduling loop is a `Task`, not a `Thread`, so there is no thread of its own to name |
| `PropertySchedulerBatchTimeWindow` | `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow` | `QuartzSchedulerOptions.BatchTriggerAcquisitionFireAheadTimeWindow` |
| `PropertySchedulerMaxBatchSize` | `quartz.scheduler.batchTriggerAcquisitionMaxCount` | `QuartzSchedulerOptions.MaxBatchSize` |
| `PropertySchedulerExporterPrefix` | `quartz.scheduler.exporter` | nothing; remoting is not supported on modern .NET |
| `PropertySchedulerExporterType` | `quartz.scheduler.exporter.type` | nothing; see above |
| `PropertySchedulerProxy` | `quartz.scheduler.proxy` | `Quartz.HttpClient` for talking to a remote scheduler |
| `PropertySchedulerProxyType` | `quartz.scheduler.proxy.type` | `Quartz.HttpClient`; the key is now rejected rather than ignored |
| `PropertySchedulerIdleWaitTime` | `quartz.scheduler.idleWaitTime` | `QuartzSchedulerOptions.IdleWaitTime` |
| `PropertySchedulerMakeSchedulerThreadDaemon` | `quartz.scheduler.makeSchedulerThreadDaemon` | nothing; the key is rejected rather than ignored. The scheduling loop is a `Task`, not a `Thread`, so it never held a process open. For the store's misfire and cluster threads use `quartz.jobStore.makeThreadsDaemons` / `AdoJobStoreOptions.UseBackgroundThreads` |
| `PropertySchedulerTypeLoadHelperType` | `quartz.scheduler.typeLoadHelper.type` | `UseTypeLoader<T>()`, or `UseSimpleTypeLoader()` |
| `PropertySchedulerJobFactoryPrefix` | `quartz.scheduler.jobFactory` | constructor injection into your `IJobFactory` |
| `PropertySchedulerJobFactoryType` | `quartz.scheduler.jobFactory.type` | `UseJobFactory<T>()` |
| `PropertySchedulerInterruptJobsOnShutdown` | `quartz.scheduler.interruptJobsOnShutdown` | `QuartzSchedulerOptions.ShutdownJobInterruption` |
| `PropertySchedulerInterruptJobsOnShutdownWithWait` | `quartz.scheduler.interruptJobsOnShutdownWithWait` | `QuartzSchedulerOptions.ShutdownJobInterruption` |
| `PropertySchedulerContextPrefix` | `quartz.context.key` | `QuartzSchedulerOptions.Context` |
| `PropertyThreadPoolPrefix` | `quartz.threadPool` | `ThreadPoolOptions` |
| `PropertyThreadPoolType` | `quartz.threadPool.type` | `UseThreadPool<T>()`, or `UseThreadPool(instance)` |
| `PropertyTimeProviderType` | `quartz.timeProvider.type` | `UseTimeProvider(TimeProvider)` |
| `PropertyJobStorePrefix` | `quartz.jobStore` | `AdoJobStoreOptions` / `InMemoryJobStoreOptions` |
| `PropertyJobStoreType` | `quartz.jobStore.type` | `UseInMemoryStore()`, `UsePersistentStore<T>()`, or `UseJobStore(instance)` |
| `PropertyJobStoreDbRetryInterval` | `quartz.jobStore.dbRetryInterval` | `AdoJobStoreOptions.DbRetryInterval` |
| `PropertyJobStoreLockHandlerPrefix` | `quartz.jobStore.lockHandler` | constructor injection into your `ILockHandler` |
| `PropertyJobStoreLockHandlerType` | `quartz.jobStore.lockHandler.type` | `UseLockHandler<T>()` |
| `PropertyTablePrefix` | `tablePrefix` (under `quartz.jobStore`) | `AdoJobStoreOptions.TablePrefix` |
| `PropertyDataSourcePrefix` | `quartz.dataSource` | `DataSourceOptions`, bound from `Quartz:DataSource:<name>` |
| `PropertyDataSourceProvider` | `provider` (under a data source) | `DataSourceOptions.Provider` |
| `PropertyDataSourceConnectionString` | `connectionString` (under a data source) | `DataSourceOptions.ConnectionString` |
| `PropertyDataSourceConnectionStringName` | `connectionStringName` (under a data source) | `DataSourceOptions.ConnectionStringName` |
| `PropertyDbProvider` | `quartz.dbprovider` | the metadata factory on `UseGenericDatabase` |
| `PropertyDbProviderType` | `connectionProvider.type` (under a data source) | `UseConnectionProvider<T>()`, or `DataSourceOptions.UseRegisteredDataSource` — the key itself is still read |
| `PropertyExecutionLimitPrefix` | `quartz.executionLimit` | `UseExecutionLimits(limits => …)` |
| `PropertyPluginPrefix` | `quartz.plugin` | `AddPlugin<T>()` |
| `PropertyPluginType` | `type` (under a plugin) | `AddPlugin<T>()` |
| `PropertyJobListenerPrefix` | `quartz.jobListener` | `AddJobListener<T>(matchers)` |
| `PropertyTriggerListenerPrefix` | `quartz.triggerListener` | `AddTriggerListener<T>(matchers)` |
| `PropertyListenerType` | `type` (under a listener) | the two `Add*Listener<T>` methods above |
| `PropertyCheckConfiguration` | `quartz.checkConfiguration` | still read, by `QuartzSchedulerBuilder.UseProperties` |
| `PropertyObjectSerializer` | `quartz.serializer` | `UseSerializer<T>()`, `UseSystemTextJsonSerializer()` |
| `PropertyThreadExecutor` | `quartz.threadExecutor` | nothing; there is no thread executor since the thread pool became asynchronous |
| `PropertyThreadExecutorType` | `quartz.threadExecutor.type` | nothing; see above |
| `DefaultInstanceId` | `NON_CLUSTERED` | `QuartzSchedulerOptions.DefaultInstanceId`, which is the same string |
| `AutoGenerateInstanceId` | `AUTO` | `QuartzSchedulerOptions.GenerateInstanceId` |
| `SystemPropertyAsInstanceId` | `SYS_PROP` | keep setting `quartz.scheduler.instanceId` to `SYS_PROP`: it still selects the generator that reads the id from the `quartz.scheduler.instanceId` environment variable. That generator is internal and cannot be named directly, so to vary the behaviour, register an `IInstanceIdGenerator` of your own in the container as described above |

Three more constants existed in 3.x and had already gone before this release, listed here because a 3.x
configuration is the one most likely to still name them:

| Removed constant | Value | Replacement |
|---|---|---|
| `ConfigurationSectionName` | `quartz` | the `<quartz>` Full Framework configuration section is not read; use `IConfiguration` |
| `PropertiesFile` | `quartz.config` | nothing is read from disk — see "The quartz.config file is no longer read" above |
| `PropertySchedulerName` | `schedName` | nothing; it named an ADO.NET column rather than a setting |

### Every removed member

The factory was also a set of override points, and a 3.x application that subclassed it was usually
reaching for one of them. Each had a reason that the container now covers directly.

| Removed member | Replacement |
|---|---|
| `StdSchedulerFactory()` | `QuartzSchedulerBuilder.Create()` |
| `StdSchedulerFactory(NameValueCollection)` | `QuartzSchedulerBuilder.Create().UseProperties(properties)` |
| `Initialize(NameValueCollection)` | `UseProperties(properties)` |
| `Initialize()` | nothing; there is no file and no environment overlay left to read |
| `GetScheduler()`, `LookupScheduler(name)`, `GetAllSchedulers()` | unchanged apart from the by-name rename — they are `ISchedulerFactory`, which `Build()` returns |
| `static GetDefaultScheduler()` | build a scheduler where the application starts and hold it, or inject `IScheduler` |
| `Dispose()`, `Dispose(bool)` | the factory `Build()` returns owns its container and implements `IDisposable` and `IAsyncDisposable`; cast to dispose it |
| `GetSchedulerRepository()` | `ISchedulerRepository`, resolved from the container |
| `GetDBConnectionManager()` (3.x) | nothing; the container is the provider registry — see [The connection manager is gone](#the-connection-manager-is-gone) |
| `GetNamedConnectionString(string)` (3.x) | `DataSourceOptions.ConnectionStringName`, resolved from `IConfiguration`'s connection strings |
| `Instantiate(QuartzSchedulerResources, QuartzScheduler)` (3.x) | nothing; both types are internal and the container builds the graph |
| `InstantiateType<T>(Type?)` (3.x) | register the implementation in the container — this was the seam a container had to patch, and it is the container now |
| `IsSupportedConfigurationKey(string)` | set `quartz.checkConfiguration` to `false` to allow keys of your own |
| `LoadType(string?)` | `ITypeLoader`, selected with `UseTypeLoader<T>()` |
| `ValidateConfiguration()` (3.x) | `quartz.checkConfiguration` for the keys, and `IValidateOptions<T>` for the typed options |

## The standalone builder is the same builder

`QuartzSchedulerBuilder` now implements `IQuartzBuilder` — the interface `AddQuartz` hands out — rather
than offering five methods of its own that happened to have the same names, plus a
`Configure(Action<IQuartzBuilder>)` hatch for everything else. There is one configuration API with two
front doors: `AddQuartz` for an application that has a container, `QuartzSchedulerBuilder` for one that
does not.

```diff
- var scheduler = await QuartzSchedulerBuilder.Create()
-     .Configure(q =>
-     {
-         q.UsePersistentStore(store => store.UseSqlServer(connectionString));
-         q.AddJob<ReportJob>(j => j.WithIdentity("report"));
-     })
-     .UseDefaultThreadPool(maxConcurrency: 20)
-     .BuildScheduler();
+ var scheduler = await QuartzSchedulerBuilder.Create()
+     .UseDefaultThreadPool(maxConcurrency: 20)
+     .UsePersistentStore(store => store.UseSqlServer(connectionString))
+     .BuildScheduler();
```

Everything on `IQuartzBuilder` — jobs, triggers, calendars, listeners, plugins, execution limits — is
now available on the standalone builder without a wrapper, which is the point.

Each of those members is declared on `QuartzSchedulerBuilder` returning `QuartzSchedulerBuilder`, with
`IQuartzBuilder` implemented explicitly underneath — which is how C# spells a covariant return on an
interface implementation. So the chain keeps its concrete type and reaches `Build()` and
`BuildScheduler()`, and a standalone scheduler is one expression. A local typed `IQuartzBuilder` still
works; a `var` local now holds the concrete type, which is strictly more useful.

The `IQuartzBuilder` **extension** methods — `AddJob`, `AddTrigger`, `ScheduleJob`, `AddCalendar`,
`AddJobType`, `ConfigureJobScope`, `UseSimpleTypeLoader` — are mirrored on `QuartzSchedulerBuilder` as
instance methods returning `QuartzSchedulerBuilder`, so they chain too:

```csharp
var scheduler = await QuartzSchedulerBuilder.Create()
    .UseDefaultThreadPool(maxConcurrency: 20)
    .UseInMemoryStore()
    .AddJob<ReportJob>(j => j.WithIdentity("report").StoreDurably())
    .AddTrigger(t => t.ForJob("report").WithCronSchedule("0 0 2 * * ?"))
    .BuildScheduler();
```

An extension method cannot be covariant in its receiver, and making it generic in the receiver is not
open to us either: `AddJob<MyJob>(…)` names one type argument, and C# has no partial type-argument
inference, so neither `AddJob<TBuilder, TJob>` nor an `extension<TBuilder>` block can be called that
way. The mirrors are how the chain keeps its type; an extension of your own over `IQuartzBuilder`
behaves as before, and goes in a statement of its own.

### `Build()` returns something you can dispose

`Build()` used to return `ISchedulerFactory` while handing back an object that owned a container, so
every caller that wanted to shut it down had to cast to a type the type system never mentioned. It now
returns `StandaloneSchedulerFactory`, which *is* an `ISchedulerFactory` and is also `IAsyncDisposable`
and `IDisposable`:

```diff
- ISchedulerFactory factory = builder.Build();
- using IDisposable container = (IDisposable) factory;
+ await using StandaloneSchedulerFactory factory = builder.Build();
```

Prefer `await using`: disposal shuts the scheduler down, which is asynchronous work that `Dispose()`
can only block on. A caller that never disposes behaves as it always did — the scheduler runs until the
process ends.

Two members moved the other way, from the standalone builder onto `IQuartzBuilder`, so that a scheduler
registered with `AddQuartz` can also be given a part that was built rather than configured:

| Member | Meaning |
|---|---|
| `UseThreadPool(IThreadPool)` | uses a pool the caller constructed |
| `UseJobStore(IJobStore)` | uses a store the caller constructed |

| Removed from `QuartzSchedulerBuilder` | Use instead |
|---|---|
| `Configure(Action<IQuartzBuilder>)` | call the members directly on the builder |
| `ConfigureScheduler`, `UseDefaultThreadPool` ×2, `UseJobFactory(IJobFactory)`, `UseInMemoryStore` | the identical `IQuartzBuilder` members, which the builder now implements |

## `IScheduler` is `IAsyncDisposable`

`IScheduler` now implements `IAsyncDisposable`, so a scheduler can be scoped with `await using` instead
of a `try`/`finally` that remembers to shut it down:

```diff
  IScheduler scheduler = await factory.GetScheduler();
- try
- {
-     await scheduler.Start();
-     …
- }
- finally
- {
-     await scheduler.Shutdown(waitForJobsToComplete: false);
- }
+ await using (scheduler)
+ {
+     await scheduler.Start();
+     …
+ }
```

Disposing releases what **that instance** owns, which is not the same thing for every scheduler:

| Instance | Disposing it |
|---|---|
| A local scheduler | `Shutdown(waitForJobsToComplete: false)`. It owns the execution it drives. |
| The `IScheduler` a container injects | disposes the scheduler it built, and does nothing at all if it never built one. |
| A `DelegatingScheduler` | forwards to the scheduler it wraps. |
| `HttpScheduler` | releases its own resources and **never** shuts the remote scheduler down. A client going away is not an instruction to stop scheduling for everybody else — call `Shutdown` for that. |

Disposal is idempotent: disposing a scheduler that is already shut down does nothing. It is deliberately
the non-waiting shutdown — `await using` means "stop this when the block ends", not "drain gracefully".
Call `Shutdown(waitForJobsToComplete: true)` yourself when running jobs should be allowed to finish;
disposing afterwards is then a no-op.

`IScheduler` only *inherits* `IAsyncDisposable`; it does not redeclare `DisposeAsync`. An earlier 4.0
preview did, with `new ValueTask DisposeAsync()`, which forced every implementation of the interface to
carry a member the base interface had already asked for. An `IScheduler` of your own implements
`IAsyncDisposable.DisposeAsync` and nothing else changes — the method that satisfied the redeclared
one satisfies this too.

### A container holding a scheduler is disposed asynchronously

This is the one change that can surface as a runtime error. `ServiceProvider.Dispose()` throws when a
singleton it created implements only `IAsyncDisposable`, so a container that resolved an `IScheduler`
has to be disposed with `await using`:

```diff
- using var provider = services.BuildServiceProvider();
+ await using var provider = services.BuildServiceProvider();
```

Applications hosted by `IHost` need no change — the host already disposes its container asynchronously,
and `AddQuartzHostedService` shuts the schedulers down before that anyway. This is about a container
built by hand, which is mostly tests.

## A scheduler's lifecycle is one value

`IScheduler.IsStarted`, `IScheduler.InStandbyMode` and `IScheduler.IsShutdown` are gone. One
`SchedulerStatus Status` replaces all three:

```csharp
public enum SchedulerStatus
{
    Unknown,        // a scheduler that could not be asked - a remote one that did not answer
    Created,        // built, never started
    Running,        // firing triggers
    Standby,        // started once, stood down, can be started again
    ShuttingDown,   // Shutdown() is running
    Shutdown        // down, and not restartable
}
```

The three booleans mapped onto it exactly, so every call site has a mechanical replacement:

| 3.x | 4.x |
|---|---|
| `scheduler.IsStarted` | `scheduler.Status is not SchedulerStatus.Created` |
| `scheduler.InStandbyMode` | `scheduler.Status is SchedulerStatus.Created or SchedulerStatus.Standby or SchedulerStatus.ShuttingDown` |
| `scheduler.IsShutdown` | `scheduler.Status is SchedulerStatus.Shutdown` |

Those rows are the *faithful* translations, and two of them are worth reading twice. `IsStarted` stayed
`true` after a shutdown — it meant "`Start` has been called at some point", not "is running now" — so
code that used it as "is running" was already wrong and should say `Status is SchedulerStatus.Running`.
And `InStandbyMode` was `true` for a scheduler that had never been started at all, which is why
`Created` exists: both fire nothing, but only one of them has ever run and has a `RunningSince`.

`SchedulerMetadata` follows, for the same reason:

```diff
- if (metadata.Shutdown) { … }
- else if (metadata.InStandbyMode) { … }
- else if (metadata.Started) { … }
+ switch (metadata.Status) { … }
```

| 3.x | 4.x |
|---|---|
| `SchedulerMetadata.Started`, `.InStandbyMode`, `.Shutdown` | `required SchedulerStatus Status` |

The two producers derive it the same way now. They did not before: `StdScheduler.GetMetadata` set
`Started` from "has ever been started" while `HttpScheduler.GetMetadata` set it from "is running now", so
the same scheduler described itself differently depending on which side of an HTTP call you asked from.
`RunningSince` is unchanged.

### The transitions are honest about themselves

Reducing the state to one value made four behaviours that were only true of some of the booleans have
to become true of the value. A transition that does not happen is not announced:

| Call | 3.x | 4.x |
|---|---|---|
| `Start()` on a scheduler that is already running | re-emits `SchedulerStarting` and `SchedulerStarted`, and tells the job store the scheduler resumed | does nothing at all |
| `Standby()` on a scheduler that is not running | emits `SchedulerInStandbyMode` and tells the job store it paused, for a scheduler that was already firing nothing | does nothing at all; a never-started scheduler stays `Created` |
| `Standby()` on a scheduler that has shut down | silently pauses a scheduler that is already down | throws `SchedulerException` |
| `Shutdown()` | calls `Standby()` on the way down, so listeners hear `SchedulerInStandbyMode` before `SchedulerShuttingDown` | goes `Running`/`Standby`/`Created` → `ShuttingDown` → `Shutdown`, and no listener is told about a standby that never happened |

The last one is a listener-visible change, inherited from Java, and it is the one to check for. A
listener that counted `SchedulerInStandbyMode` to detect "the scheduler stopped firing" should be
counting `SchedulerShuttingDown` as well — it is raised by every shutdown, as it always was. The full
sequence over a start, a standby and a shutdown is now exactly:

`SchedulerStarting` → `SchedulerStarted` → `SchedulerInStandbyMode` → `SchedulerShuttingDown` →
`SchedulerShutdown`

`Status` becomes `Shutdown` at the *end* of the shutdown, once the plugins and the job store are down,
so a scheduler that is draining its running jobs reads `ShuttingDown` for as long as it takes — and it
reads `Shutdown` even when a plugin or job store threw on the way down, because the shutdown is claimed
once and cannot be run again to finish the job.

A scheduler that has begun shutting down refuses work exactly as a shut-down one does, and
`ISchedulerFactory.GetScheduler()` refuses to hand it back for the same reason. It is still *listed* by
`ISchedulerRepository` and by the dashboard while it drains, which is what `ShuttingDown` is for.

### The health check reports the state it found

`AddQuartzHealthChecks()` read `IsStarted` alone, which made a scheduler in standby **healthy** (it had
been started once) and a shut-down one fall through to the store probe, where the failure was reported
as a connectivity problem. It reports the state it actually found instead:

| `Status` | Health |
|---|---|
| `Running` | the store probe decides: `Healthy`, or `Unhealthy` when the store cannot be reached |
| `Standby` | `Degraded` — deliberate and reversible, so neither healthy nor a reason to take a node out of rotation |
| `Created`, `ShuttingDown`, `Shutdown`, `Unknown` | `Unhealthy`, with a message naming the scheduler and the state |

The check also no longer throws when it is registered for a default scheduler in a container that has
only named ones: it reports `Unhealthy` and says to call `AddQuartzHealthChecks()` on the scheduler's own
builder.

## Clustering is configured in one place

`AdoJobStoreOptions` no longer carries `Clustered`, `ClusterCheckinInterval` and
`ClusterCheckinMisfireThreshold`. Those three said the same thing as `UseClustering(…)` and
`ClusteringOptions`, so a scheduler had two places to be clustered from and they could disagree.
`ClusteringOptions` is now the one place, and whether a store is clustered is something it *reports*
rather than something you can also set on it.

| Removed | Use instead |
|---|---|
| `AdoJobStoreOptions.Clustered` | `ClusteringOptions.Enabled` |
| `AdoJobStoreOptions.ClusterCheckinInterval` | `ClusteringOptions.CheckinInterval` |
| `AdoJobStoreOptions.ClusterCheckinMisfireThreshold` | `ClusteringOptions.CheckinMisfireThreshold` |

`ClusteringOptions`' two intervals are no longer nullable — `UseClustering()` with no arguments turns
clustering on without touching them, because it configures the options object rather than assigning a
copy over it.

Code-first configuration is unchanged:

```csharp
store.UseClustering(cluster =>
{
    cluster.CheckinInterval = TimeSpan.FromSeconds(10);
    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
});
```

The flat keys are unchanged as well — `quartz.jobStore.clustered`,
`quartz.jobStore.clusterCheckinInterval` and `quartz.jobStore.clusterCheckinMisfireThreshold` all still
work, and so does the `JobStore:Clustered` spelling in `appsettings.json`, because `AddQuartz` reads
every section as flat keys too. What is new is the hierarchical spelling that matches the options type:

```json
{
  "Quartz": {
    "JobStore": {
      "Clustering": {
        "Enabled": true,
        "CheckinInterval": "00:00:10",
        "CheckinMisfireThreshold": "00:00:20"
      }
    }
  }
}
```

`AdoJobStoreOptions` validation lost the rule that `UseDbLocks` must be on when `Clustered` is: it can
no longer see both settings, and it never needed to, since every path that enables clustering enables
database locking with it and a store with an explicit lock handler of its own — the Redis one, say —
was never wrong to leave `UseDbLocks` off.

## The SQLite extension methods swapped names

::: warning
`UseSqlite` did not exist in 3.x and now means **Microsoft.Data.Sqlite**. The method that used to be
called `UseSQLite` — the legacy **System.Data.SQLite** driver — is now `UseSystemDataSqlite`. Changing
`UseSQLite` to `UseSqlite` compiles and runs, and silently swaps your ADO.NET provider. Read the table
before doing a case-insensitive find and replace.
:::

| 3.x / 4.0 preview | 4.0 | ADO.NET driver | Provider name |
|---|---|---|---|
| `UseSQLite` | `UseSystemDataSqlite` | System.Data.SQLite | `SQLite` |
| `UseMicrosoftSQLite` | `UseSqlite` | Microsoft.Data.Sqlite | `SQLite-Microsoft` |

```diff
- store.UseSQLite(connectionString);          // System.Data.SQLite
+ store.UseSystemDataSqlite(connectionString);

- store.UseMicrosoftSQLite(connectionString); // Microsoft.Data.Sqlite
+ store.UseSqlite(connectionString);
```

The short name goes to the driver you should reach for, which is the same rule `UseMySql` and
`UseMySqlConnector` already followed, and the same spelling Entity Framework Core uses for the same
choice. The provider names themselves — what a `quartz.dataSource.<name>.provider` key says — are
unchanged, so nothing in a configuration file has to move.

## A data source is defined, referred to, or handed over

There were five ways to say where a job store's connections come from, and two of them said the same
thing. `UseDataSourceConnectionProvider()` is gone; it existed only to set
`DataSourceOptions.UseRegisteredDataSource`, and being a method it also had to be called in the right
order to take effect.

```diff
  q.UsePersistentStore(store =>
  {
-     store.UsePostgres(db => db.Provider = "Npgsql");
-     store.UseDataSourceConnectionProvider();
+     store.UsePostgres(db => db.UseRegisteredDataSource = true);
  });
```

What is left says four different things:

| Member | Role |
|---|---|
| `UseDataSource(configure)` | **defines** a data source — which driver, and how to reach the database. The database methods such as `UseSqlServer` are shorthands for it |
| `UseDataSource(name)` | **refers to** a data source by name, picking up settings registered elsewhere, such as a `Quartz:DataSource:<name>` section |
| `DataSourceOptions.UseRegisteredDataSource` | takes connections from the container's unkeyed `DbDataSource`, instead of from a connection string |
| `DataSourceOptions.DataSourceServiceKey` / `.DataSourceFactory` | the same, for a `DbDataSource` registered under a key of its own or built by the caller. Set from code — neither a service key nor a delegate is something a configuration binder can produce |
| `UseConnectionProvider<T>()` / `UseConnectionProvider(factory)` | **replaces** the connection provider outright, for connections Quartz cannot describe. The code spelling of `quartz.dataSource.<name>.connectionProvider.type` |

Where connections come from is a property of the data source, so the first three are said in
`DataSourceOptions` alongside `ConnectionString` and `ConnectionStringName`, and
`UseRegisteredDataSource` wins over both. `UseConnectionProvider` is the one that is not a data source
setting, because it does not describe a database — it hands over the object that reaches one. It is
therefore also the one method here that replaces rather than defers: it beats whatever `UseSqlServer`
and its siblings registered, whichever order the two were called in, and names this store's data
source so it needs no `UseDataSource` call of its own.

### `AddDataSourceProvider()` went with it

The two always travelled together. `AddDataSourceProvider()`, on the configurator, registered
`DataSourceDbProvider` in the container as the `IDbProvider` to use; `UseDataSourceConnectionProvider()`,
on the store, then named it as that data source's connection provider. Two calls, in two different
places, that had to agree with each other to do anything. Both are gone, and
`UseRegisteredDataSource` does the whole job — the data source builds its own `DataSourceDbProvider`
around the `DbDataSource` it resolves from the container:

```diff
  services.AddQuartz(q =>
  {
-     q.AddDataSourceProvider();
      q.UsePersistentStore(store =>
      {
-         store.UsePostgres(db => db.Provider = "Npgsql");
-         store.UseDataSourceConnectionProvider();
+         store.UsePostgres(db => db.UseRegisteredDataSource = true);
      });
  });
```

Registering the `DbDataSource` itself is still yours to do — `services.AddNpgsqlDataSource(…)`, or
whatever your provider offers. What is no longer yours to do is wiring it up to Quartz.

`UseRegisteredDataSource` asks for the container's one unkeyed `DbDataSource`, which was the only shape
3.x's `AddDataSourceProvider()` could express either. A container holding several — a scheduler per
tenant — says which one it means with `DataSourceServiceKey`, and a data source built rather than
registered goes in `DataSourceFactory`:

```csharp
services.AddNpgsqlDataSource(tenantA, serviceKey: "tenant-a");
services.AddQuartz("tenant-a", q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "tenant-a")));
```

Both are settable from code only — a service key is any object and a factory is a delegate, so a
configuration binder can produce neither. Both imply `UseRegisteredDataSource`, so neither needs a
connection string.

Commands on this path are now made by the connection rather than built from the driver description.
A `DbDataSource` configures the connections it hands out — `NpgsqlDataSource` attaches its type mappers,
its logging and its composite type registrations — and a command reaches those through the connection it
belongs to, which a command constructed by reflection and given a connection afterwards does not. The
connection-string path is unchanged.

## `QuartzOptions` lost its three typed settings

`QuartzOptions` is the flat `quartz.*` property bag. Three of its members were typed settings that
existed nowhere else in it, each reading and writing a key that has a typed option of its own, so they
were a third spelling of a setting that already had two.

| Removed | Use instead |
|---|---|
| `QuartzOptions.SchedulerName` | `QuartzSchedulerOptions.InstanceName`, or `Properties["quartz.scheduler.instanceName"]` |
| `QuartzOptions.SchedulerId` | `QuartzSchedulerOptions.InstanceId`, or `Properties["quartz.scheduler.instanceId"]` |
| `QuartzOptions.MisfireThreshold` | `InMemoryJobStoreOptions.MisfireThreshold` / `AdoJobStoreOptions.MisfireThreshold` |

```diff
- services.Configure<QuartzOptions>(options => options.SchedulerName = "core");
+ services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "core"));
```

`MisfireThreshold` also had a round-trip problem of its own: it stored a `TimeSpan` as a string of
whole milliseconds, so a value with sub-millisecond precision did not read back as it was written.

`Properties`, `ToProperties()` — which is what `ToNameValueCollection()` became, see "A property bag is
any dictionary" — and `Scheduling` stay. `Scheduling` is the exception that proves
the rule — its three directives say how a configured schedule is applied to a scheduler rather than how
a component is configured, and they have no options type of their own to bind onto, so this is where
they live.

`Scheduling` is get-only, like `Properties`. Options callbacks run in order over one instance, so
assigning a fresh `SchedulingOptions` threw away whatever `Quartz:Scheduling` — or an earlier callback —
had already set, with nothing to show for it. Set the properties instead; the configuration binder needs
no setter to bind into a non-null complex property.

```diff
- services.Configure<QuartzOptions>(options => options.Scheduling = new SchedulingOptions { IgnoreDuplicates = true });
+ services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);
```

## `AddJob` registers the job with the container

`AddJob<T>()`, `AddJob(type, …)` and `ScheduleJob<T>()` now register the job type as a **scoped**
service, with `TryAdd` semantics.

Before, they described the job to the scheduler and nothing else. The job factory resolved the job
from the container and fell back to `ActivatorUtilities` when it found no registration, so a job whose
constructor the container could not satisfy was never noticed at startup: `ValidateOnBuild` — which the
host turns on by default in the Development environment — had never heard of the type. The failure
arrived at fire time instead, as a `JobInstantiationException`, by which point the trigger had fired,
the job had not run, and every trigger of that job had been moved to `TriggerState.Error` (discussion
[#3211](https://github.com/quartznet/quartznet/discussions/3211)).

```csharp
services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));

// now throws when the container is built:
// Unable to resolve service for type 'IReportStore' while attempting to activate 'SendReportsJob'
```

Two things follow from this.

**A job you register yourself keeps your registration.** The lifetime, factory or implementation type
you chose wins, because Quartz's registration is a `TryAdd`. Registering the same job with `AddJob`
twice is harmless for the same reason. If you were registering your jobs explicitly to get startup
validation, that line is now redundant, but it is not wrong:

```diff
- services.AddScoped<SendReportsJob>();   // no longer needed for validation
  services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));
```

**Scoped is the lifetime the job factory uses.** It opens a dependency injection scope per fire,
resolves the job from it, and disposes the scope when the job returns — so a job may take scoped
dependencies such as a database context. If a job of yours has to be a singleton, register it as one
yourself; it must then be thread-safe and must not capture scoped dependencies.

A job type that is an interface or an abstract class is not registered, since the container could not
construct it. Jobs named by an XML or JSON schedule are not registered either — nothing describes them
to the container — so those still fail at fire time if their dependencies are missing.

**A registered job may not take a scheduler's own parts by constructor.** `IScheduler`,
`ISchedulerFactory`, `IJobStore`, `IThreadPool` and `IOptions<QuartzSchedulerOptions>` belong to one
scheduler. A job that injected one of them used to be activated through its scheduler's view of the
container and was handed that scheduler's; a registered job is now resolved from the container like any
other service, which resolves them unkeyed — the default scheduler's, or nothing at all in a container
holding only named schedulers. Rather than leave a job silently holding another scheduler's
collaborators, the constructor is refused when the host starts, with a message naming the job and the
parameter:

```text
Job type ArchiveJob is registered on scheduler 'acme', and its constructor takes ISchedulerFactory
schedulerFactory — a part that belongs to one scheduler. …
```

Take the scheduler from `IJobExecutionContext.Scheduler`, which is the scheduler actually running the
job; take `IJobExecutionContextAccessor` in code the context is not handed to; or, where the job really
has to be *constructed* with something of its scheduler's, register it with `AddJobType<T>(factory)` —
below — and resolve that part by key inside the factory, which is not examined. A job type the container
does not hold is unaffected: it is still activated by the job factory and still receives its own
scheduler's parts.

### `AddJobType` gives one scheduler its own build of a job type

New in 4.0, and only interesting when a container holds more than one scheduler. `AddJob<T>`'s
registration is unkeyed and `TryAdd`, so under a scheduler per tenant the first registration is what
every scheduler gets — different implementations or different lifetimes for one job type were not
expressible. `AddJobType` says the registration belongs to *this* scheduler:

```csharp
services.AddQuartz("acme", q =>
{
    q.AddJobType<ReportJob, AcmeReportJob>();                        // a different implementation
    q.AddJobType<AuditJob>(ServiceLifetime.Singleton);               // a different lifetime
    q.AddJobType<ExportJob>(sp => new ExportJob(sp.GetRequiredKeyedService<IExportSink>("acme")));
    q.AddJob<ReportJob>(j => j.WithIdentity("report"));
});
```

The registration is made under the scheduler's service key, or unkeyed for the default scheduler —
whose registrations *are* the unkeyed ones, so an empty key would not be the same thing. The job
factory looks there first and falls back to the container's unkeyed registration, so a scheduler that
was given nothing of its own resolves exactly as before, and the default scheduler resolves in one
lookup as it always has. Nothing about a single-scheduler application changes.

The lifetime defaults to `ServiceLifetime.Scoped` — the lifetime the job factory is built around —
and is named through an overload rather than an optional parameter. That is not a style choice: a
default value that is an enum from an assembly which only ships in a shared framework is a metadata
constant whose type coverlet's Cecil resolver cannot resolve, and it silently drops the *entire*
containing assembly from the coverage report.

## Naming a job type by string says so under trimming

`Quartz` is marked trimmable in 4.0, and the job-type APIs say what they need. Nothing here is a
source or binary break — the attributes are annotations — but an application published with
`PublishTrimmed` now gets told things 3.x never mentioned.

**The typed spelling carries the requirement.** `JobBuilder.Create<T>()`, `JobBuilder<TJob>.OfType<T>()`
and `OfType(Type)`, `AddJob<T>()`, `AddJob(Type, …)`, `AddJobType<T>()`, `AddJobType<TJob, TImpl>()`,
`AddTrigger<TJob>()`, `ScheduleJob<T>()`, `TriggerBuilder.Create<TJob>()`, `new JobType(Type)` and the
implicit `Type` → `JobType` conversion all declare
`[DynamicallyAccessedMembers(PublicConstructors | PublicProperties | Interfaces)]` on the job type —
what a job factory constructs, what a `JobDataMap` binds onto, and where
`[DisallowConcurrentExecution]` may be inherited from. The generic `JobBuilder<TJob>`,
`TriggerBuilder<TJob>`, `IJobConfigurator<TJob>` and `ITriggerConfigurator<TJob>` declare the same on
their type parameter. `PublicMethods`, which some of these declared in earlier 4.0 previews, is gone:
a kept property keeps its accessors, and `IJob.Execute` is reached through the interface.

If you pass a `Type` variable rather than a `typeof` or a generic argument, the compiler now asks the
same of *your* code, which is the annotation doing its job:

```diff
- static IJobDetail Build(Type jobType) => JobBuilder.Create().OfType(jobType).Build();
+ static IJobDetail Build(
+     [DynamicallyAccessedMembers(
+         DynamicallyAccessedMemberTypes.PublicConstructors
+         | DynamicallyAccessedMemberTypes.PublicProperties
+         | DynamicallyAccessedMemberTypes.Interfaces)] Type jobType)
+     => JobBuilder.Create().OfType(jobType).Build();
```

**The name-taking spelling is `[RequiresUnreferencedCode]`.** `JobBuilder<TJob>.OfType(string)`, the
`JobType(string)` constructor and the explicit `string` → `JobType` cast report `IL2026` in a trimmed
build, because a type resolved from a string cannot be proven reachable. So does the
`job_scheduling_data` XML loader. Prefer the typed spelling, or root the type yourself with a
[trimmer root descriptor](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options#root-descriptors).
See [Publishing Trimmed and Native AOT](how-tos/trimming-and-native-aot.md) for what a trimmed
application has to do.

`DbMetadata.ConnectionType`, `.CommandType` and `.ParameterType` carry annotations too, saying what
`DbProvider` does with each: constructs connections and commands, and reads the properties the metadata
names. A `UseGenericDatabase` callback that sets them from `typeof(...)` already satisfies that.

**One behaviour change came with it.** An ADO.NET store's trigger acquisition decided
`[DisallowConcurrentExecution]` with a check that did not look at interfaces, while
`IJobDetail.ConcurrentExecutionDisallowed` did. A job that inherited the attribute from an interface
was therefore serialized when it fired but not when it was acquired, so a batch could hold two of its
triggers and the second was released again. Both now ask the same question.

## A job type rename is declared as a map

A persistent job store keeps the job's type as text, in `QRTZ_JOB_DETAILS.JOB_CLASS_NAME`. Renaming the
class, moving its namespace or moving it to another assembly therefore breaks every row that names it,
and on 3.x the supported answer was an `ITypeLoadHelper` of your own: a class to write, register and
keep, for a one-line fact.

4.0 lets the fact be stated as a fact. `TypeLoaderOptions.Aliases` maps the name as it was stored to the
name the type has now, and `UseTypeLoader(configure)` is the typed way to add one:

<!-- snippet: sample_reference_type_loader_aliases -->
```csharp
services.AddQuartz(q => q.UseTypeLoader(loader =>
    loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob))));
```
<!-- endSnippet -->

The same map binds from configuration, so the rename can ship in `appsettings.json` with the deployment
that performs it:

```json
{
  "Quartz": {
    "TypeLoader": {
      "Aliases": {
        "Acme.Jobs.NightlyReport, Acme.Jobs": "Acme.Jobs.NightlyRollupJob, Acme.Jobs"
      }
    }
  }
}
```

The map is read by the loader Quartz ships, which is the one place a type name is resolved at run time —
so it applies to `JOB_CLASS_NAME`, to jobs named in XML or JSON scheduling data, and to the
`quartz.plugin.<name>.type` and `quartz.jobListener.<name>.type` keys. The flat keys naming a scheduler's
own components are read while the service collection is still being built, before any options exist, and
are not aliased. Keys are compared the way that loader already compares its own table of Quartz's
3.x → 4.0 renames: ordinal, matching either the whole name or the part of it before the comma that
starts the assembly.

Two things are deliberate. An alias whose target names no loadable type **fails options validation at
startup**, naming both halves of the entry, rather than surfacing later as a `TypeLoadException` that
names the dead name and nothing about the mapping meant to save it. And nothing is ever written back: a
job read under an aliased name still has the old spelling in its row, which is what makes an alias safe
during a rolling deployment — the nodes still running the old build keep writing the old name while the
new ones read it. Retiring an alias is still the SQL `UPDATE` in
[Job deserialization failures after
refactoring](../troubleshooting.md#job-deserialization-failures-after-refactoring), run once nothing is
hitting it any more.

`UseTypeLoader<T>()` is unchanged and still replaces the loader outright, for a scheme rather than a
list — a name resolved out of a plugin's `AssemblyLoadContext`, say. Replacing the loader takes the map
with it, since the map is the shipped loader's.

## The job scope is prepared without writing a job factory

`ConfigureScope` — the hook that prepares the dependency injection scope a job is built in, and the place
an `AsyncLocal` is set for the job's dependencies to read — was reachable only by deriving from
`MicrosoftDependencyInjectionJobFactory`, overriding a protected method, and registering the derived
factory. That is a class and a registration to set one ambient value:

```diff
- public sealed class TenantJobFactory : MicrosoftDependencyInjectionJobFactory
- {
-     public TenantJobFactory(IServiceProvider serviceProvider) : base(serviceProvider) { }
-
-     protected override void ConfigureScope(IServiceScope scope, TriggerFiredBundle bundle, IScheduler scheduler)
-         => Tenant.Current.Value = bundle.JobDetail.JobDataMap.GetString("tenant");
- }
- services.AddQuartz(q => q.UseJobFactory<TenantJobFactory>());
+ services.AddQuartz(q => q.ConfigureJobScope(
+     (scope, bundle, scheduler) => Tenant.Current.Value = bundle.JobDetail.JobDataMap.GetString("tenant")));
```

The contract is the one the virtual method always had: it runs before the job is resolved, and it is
synchronous so that an `AsyncLocal` set in it survives into `Execute`. Callbacks combine rather than
replace, and the same delegate is `JobFactoryOptions.ConfigureScope`, which is per-scheduler like every
other component's options. Overriding the protected method still works and still takes the delegate's
place if the override does not call base.

### The firing can be read without being handed it

New in 4.0: `IJobExecutionContextAccessor`, registered by `AddQuartz` as a singleton, is the firing the
calling code is part of. It is for code that is not the job and cannot be handed an
`IJobExecutionContext` — a scoped service, a logging enricher, a repository three calls below
`Execute`. 3.x had no such thing, so an application that needed one wrote its own `AsyncLocal` and set
it from a job factory:

```diff
- public static class Tenant { public static readonly AsyncLocal<string?> Current = new(); }
- services.AddQuartz(q => q.ConfigureJobScope(
-     (scope, bundle, scheduler) => Tenant.Current.Value = bundle.Trigger.Key.Group));
+ public sealed class TenantConnectionFactory(IJobExecutionContextAccessor accessor)
+ {
+     public string ConnectionString => connectionStrings[accessor.Current!.Trigger.Key.Group];
+ }
```

`Current` is set from the moment the execution context exists — before the trigger and job listeners are
notified — until the job has been returned to the job factory, and is `null` at every other time,
including on the scheduling thread. It travels with the `ExecutionContext`, so it survives `await` and
is captured by `Task.Run`, and it can never be another firing's. Work a job leaves running past the end
of its execution reads `null` rather than the finished context, since by then the job's scope has been
disposed. There is no setter.

It does **not** replace `ConfigureJobScope`. The execution context takes the job instance, so it does
not exist while the job is being constructed: anything that needs the tenant *at construction time*
still gets it from the hook.

## A job can take a typed input

New in 4.0, and purely additive: `IJob<TInput>` lets a job declare the type of the payload it is
scheduled with, instead of digging it out of a stringly `JobDataMap`. 3.x had no such thing, so every
framework that embedded Quartz wrote the same adapter job and payload-carrying context by hand.

```diff
- public sealed class SendEmailJob : IJob
- {
-     public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
-     {
-         SendEmail input = JsonSerializer.Deserialize<SendEmail>(context.MergedJobDataMap.GetString("payload")!)!;
-         // ...
-     }
- }
- trigger.UsingJobData("payload", JsonSerializer.Serialize(new SendEmail(to, subject)));
+ public sealed class SendEmailJob : IJob<SendEmail>
+ {
+     public ValueTask Execute(IJobExecutionContext context, SendEmail input, CancellationToken cancellationToken = default)
+     {
+         // ...
+     }
+ }
+ trigger.UsingInput(new SendEmail(to, subject));
```

The whole of the dispatch is `IJob<TInput>`'s default implementation of `IJob.Execute`, so the job
factory, `JobRunShell` and the listeners still see an `IJob` and nothing closes a generic method over a
runtime type — which is what keeps this working in a trimmed or native-AOT publish.

**The API added.** In `Quartz`: `IJob<TInput>`;
`SchedulerConstants.JobInput` (`"QRTZ_JOB_INPUT"`);
`JobExecutionContextInputExtensions.GetInput<TInput>(this IJobExecutionContext)` and
`TryGetInput<TInput>(this IJobExecutionContext, out TInput?)`;
and `JobInputBuilderExtensions.UsingInput<TJob, TInput>` on `JobBuilder<TJob>`, `TriggerBuilder<TJob>`,
`IJobConfigurator<TJob>` and `ITriggerConfigurator<TJob>`. In `Quartz.Extensibility`:
`IJobInputSerializer`. In `Quartz.Impl`: `SystemTextJsonJobInputSerializer`, and a fourth optional
`IJobInputSerializer? inputSerializer = null` parameter on the `JobExecutionContextImpl` constructor —
the one existing signature that moved, and source-compatible because it is optional.

**Where the payload lives.** The value goes into the ordinary `JobDataMap` under
`SchedulerConstants.JobInput`, and the scheduler serializes it to a **string** as the job or trigger is
stored — so it survives `StoreJobDataAsStrings`, the System.Text.Json write gate, the Newtonsoft
serializer, the binary blob column and the HTTP wire without any of them knowing about it. A trigger's
input overrides a job's, which is the `MergedJobDataMap` rule everything else follows.

`UsingInput` is offered only on a builder for a job that declares an input; it is an extension method
rather than a builder member because a method cannot constrain its own type's type parameter (CS0699).
`GetInput<TInput>()` is an extension on `IJobExecutionContext` rather than a member for the same kind of
reason: a member would break every hand-written context in the wild.

**Two things to know.** The input type is inferred from the argument's static type, so pass the type
argument explicitly — `UsingInput<SendEmailJob, SendEmail>(payload)` — when the payload is held as a
base type. And a `[PersistJobDataAfterExecution]` job whose *detail* carries the input writes it back
after every firing; that is harmless, since it is already the string it will be read as, but an input
that differs per firing belongs on the trigger.

**Converting a job over a store that predates the input.** An `IJob<TInput>` fails a firing that carries no
input, by name, rather than running it with a default payload — right for a fresh application, and a trap
for one whose store already holds triggers written by its 3.x self, whose payload is spread over flat
`JobDataMap` keys with nothing under `QRTZ_JOB_INPUT`. A job that has to serve both shapes while those
triggers drain stays an `IJob` and reads them apart with `TryGetInput`:

```csharp
public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    SendEmail input = context.TryGetInput(out SendEmail? typed) && typed is not null
        ? typed
        : new SendEmail(context.MergedJobDataMap.GetString("to")!, context.MergedJobDataMap.GetString("subject")!);

    // ...
}
```

`TryGetInput` answers `false` only when the key is absent; a value that is present and unreadable still
throws, because corruption is not compatibility. `GetInput<TInput>()` cannot tell the two apart — a payload
that read back as `null` and no payload at all are the same answer there.

**Serialization.** `IJobInputSerializer` is registered per scheduler, keyed by name like every other
component, and defaults to `SystemTextJsonJobInputSerializer` built from the same
`SystemTextJsonSerializerRegistry` as the store's `IObjectSerializer`. An application published trimmed
or natively declares its payload types with `SystemTextJsonSerializerRegistry.AddTypeInfoResolver`,
exactly as it declares a job data value type — there is no second registration to learn. See
[Job Data](tutorial/job-data-map.md#a-typed-input-the-third-read-side).

## Scheduling a trigger can replace one atomically

New in 4.0, and purely additive: `IScheduler.ScheduleJob` has two more overloads, which take a
`ScheduleJobOptions` and can therefore replace what is already stored.

```csharp
ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, ScheduleJobOptions options, CancellationToken cancellationToken = default);
ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, ScheduleJobOptions options, CancellationToken cancellationToken = default);
```

On 3.x — and on the two existing overloads, which are unchanged — rescheduling under a key you may or may
not already hold meant three round trips and a window:

```diff
- if (await scheduler.CheckExists(trigger.Key, cancellationToken))
- {
-     await scheduler.UnscheduleJob(trigger.Key, cancellationToken);
- }
- await scheduler.ScheduleJob(trigger, cancellationToken);
+ await scheduler.ScheduleJob(trigger, new ScheduleJobOptions { Replace = true }, cancellationToken);
```

The new form is one store operation under the store's own lock — `SchedulerLock.TriggerAccess` on an
ADO store, the single lock on the in-memory one — so two nodes doing it at once cannot interleave into a
schedule with the trigger deleted and not replaced. The job-and-trigger overload goes through the store's
`ScheduleJobs`, so the job and its trigger are written together rather than under two locks.

`options` deliberately has **no default** on either overload. Giving it one would make
`scheduler.ScheduleJob(trigger)` and `scheduler.ScheduleJob(job, trigger)` ambiguous between the pair.

**A replaced trigger keeps its previous fire time.** The ADO store has done this since #1834; the in-memory
store cloned the incoming trigger and lost it, so the same code reported "never fired" on one store and a
real time on the other. The in-memory store now carries the value over, and `JobStoreContractTest` pins it
on every store — along with the rule that a trigger replaced into a paused group is still born paused, so
an upsert is not a way to escape a pause. A caller who *means* to reset the history sets
`PreviousFireTimeUtc` on the incoming trigger, which is honoured.

**Over HTTP**, `POST /triggers/schedule` takes a `replace` flag, `HttpScheduler` implements both new
overloads, and the flag reaches the scheduler unchanged. A request that omits it behaves exactly as before.

## A scheduler says which clock it keeps

New in 4.0: `IScheduler.TimeProvider`, a **default interface member** answering `TimeProvider.System`.
Nothing has to implement it — a scheduler written outside this repository keeps compiling and reports
what its triggers would have used anyway — and the schedulers Quartz ships answer better:

| Scheduler | Answers |
|---|---|
| the local scheduler | the `TimeProvider` it was configured with |
| `DelegatingScheduler` | whatever it decorates |
| the deferred scheduler `AddQuartz` registers | the built scheduler's, or the system clock while there is no scheduler yet |
| `HttpScheduler` | the system clock — the clock that decides when a trigger fires is the remote scheduler's, and a `TimeProvider` cannot be fetched over a wire |

3.x had no way to ask. Code that builds a trigger *for* a scheduler had to reach for
`DateTimeOffset.UtcNow`, so an application or a test running the scheduler on a clock of its own
computed "in ten minutes" against a different clock from the one the scheduling loop compares the
answer to:

```diff
- ITrigger trigger = TriggerBuilder.Create()
-     .StartAt(DateTimeOffset.UtcNow.AddMinutes(10))
-     .Build();
+ ITrigger trigger = TriggerBuilder.Create(scheduler.TimeProvider)
+     .StartAt(scheduler.TimeProvider.GetUtcNow().AddMinutes(10))
+     .Build();
```

It is also what a job rescheduling itself from inside `Execute` reads, as
`context.Scheduler.TimeProvider`, and it is what the one-call `ScheduleJob<TJob, TInput>` overloads
below use for their `TimeSpan` form and for the trigger they build.

## A typed job can be scheduled in one call

Also new, on top of [typed inputs](#a-job-can-take-a-typed-input): two extension methods on `IScheduler`
that schedule one firing of a job with a payload and a time, and nothing else to build.

```csharp
ValueTask<ScheduledOneOffJob> ScheduleJob<TJob, TInput>(this IScheduler scheduler, TInput input, DateTimeOffset at,
    OneOffJobOptions options = default, CancellationToken cancellationToken = default) where TJob : IJob<TInput>;

ValueTask<ScheduledOneOffJob> ScheduleJob<TJob, TInput>(this IScheduler scheduler, TInput input, TimeSpan delay,
    OneOffJobOptions options = default, CancellationToken cancellationToken = default) where TJob : IJob<TInput>;
```

They are the imperative twin of `IQuartzBuilder.ScheduleJob<TJob>`, which only ever existed for the firings
an application declares at start-up. 3.x had nothing of the kind.

```csharp
ScheduledOneOffJob firing = await scheduler.ScheduleJob<SendInvoiceJob, SendInvoice>(
    invoice, TimeSpan.FromDays(7), new OneOffJobOptions { Name = $"invoice-{invoice.CustomerId}", Group = invoice.CustomerId, Replace = true });

logger.LogInformation("Reminder {Trigger} scheduled for {At}", firing.TriggerKey, firing.FirstFireTimeUtc);

await scheduler.UnscheduleJob(firing.TriggerKey);
```

**What the call answers with** is `ScheduledOneOffJob`, a two-member `readonly record struct`: the
`TriggerKey` of the firing — the handle to cancel it with, or to replace it by scheduling the same name
again — and the `FirstFireTimeUtc` the store computed, which is what the
`ScheduleJob(trigger, options)` overload it wraps returns and what a caller logging "scheduled for X"
would otherwise have to infer from the time it asked for. It stays two members: everything else about the
firing is a property of the trigger the key names.

**One durable job per job type, many triggers.** The job is stored once under
`SchedulerJobExtensions.ScheduledJobKey<TJob>()`, which is
`(typeof(TJob).Name, SchedulerConstants.ScheduledJobGroup)` — a new reserved constant spelled
`"QRTZ_SCHEDULED"` — and every call adds a trigger to it. That is the shape a message bus's Quartz
integration converges on: a scheduled message is a trigger, so there is no job churn to pay for. The job is
ensured with `AddJob` and `Replace`, which is idempotent and cluster-safe, and remembered per scheduler
instance; a store that reports the job missing gets it put back and the firing retried once, so a cluster
restore or an operator's delete does not turn into a permanent failure. The key is public because an
integration with a second scheduling path — a recurring trigger it builds itself for the same job — has to
point that trigger at the same job rather than add one of its own to a group it is told to stay out of.

**`OneOffJobOptions` is named for what the call creates**, not for the call: one off, one firing, one
trigger. It carries what would otherwise be `TriggerBuilder` calls — `Name`, `Group`, `Description`,
`Priority`, `ExecutionGroup`, `MisfireInstruction` — plus `Replace`, which it passes on to the store, and
`RequestRecovery`, the one member that describes the ensured *job*: set it and that job is marked
`RequestsRecovery`, so a firing interrupted by a hard shutdown is re-executed when the scheduler comes back.
Because the job is ensured once per scheduler instance, the first call's value wins for the lifetime of the
process — which is why it is a named boolean rather than a configuration delegate that would look as though
it varied per call. Every member is optional, and `default` is "a one-shot trigger with a generated name, in
this job type's own group". An overload that schedules a *recurring* job would have something else to say — a
schedule, an end time, a calendar — and will get an options type of its own rather than nullable members here
that mean nothing to a single firing. It is a different thing from `ScheduleJobOptions`, which describes a
*store* operation and says only whether it may over-write.

The trigger group is the correlation axis: everything scheduled for one saga, one tenant or one
conversation can share a group and be listed, paused or unscheduled together. Cancelling is
`UnscheduleJob(key)`; the durable job stays behind, one row per job type whatever the traffic. Mind the
default when adopting these overloads inside something that already has a trigger-key contract: `Group`
defaults to the job type's name, so an integration whose callers cancel with `new TriggerKey(id)` — the
*default* group — must say `Group = TriggerKey.DefaultGroup` or its cancellation silently stops matching.

See [One-Off Job](how-tos/one-off-job.md#a-payload-and-a-time-in-one-call).

## A scheduled job links back to the trace that scheduled it

New in 4.0, and on by default: when a scheduling call is made inside an `Activity`, the scheduler
records that activity's W3C trace context on the trigger, and the firing's `Quartz.Job.Execute` span
carries an `ActivityLink` back to it. Nothing has to be configured, and no code changes.

3.x propagated nothing, so every integrator that cared reached into the trigger's `JobDataMap` and
invented a key of its own:

```diff
- trigger.UsingJobData("traceparent", Activity.Current?.Id);   // and a job that read it back by hand
+ // nothing — the scheduler writes it, and the execute span links to it
```

**A link, not a parent.** A job scheduled for next Tuesday runs a week after the request that asked for
it, quite possibly on another node. Making the firing a *child* of the scheduling span would produce a
trace that stays open for a week, which no backend can render and no operator can read. The firing is
its own trace root and the link is how you walk back — the shape OpenTelemetry gives an asynchronous
producer and the consumer that eventually picks the work up. The span name is unchanged.

**The API added.** In `Quartz`: `SchedulerConstants.TraceParent` (`"QRTZ_TRACEPARENT"`),
`SchedulerConstants.TraceState` (`"QRTZ_TRACESTATE"`), and
`QuartzSchedulerOptions.PropagateTraceContext`, which defaults to `true`. There is no flat `quartz.*`
key for it: nothing on 3.x did this, so there is no configuration to migrate — and `LegacyPropertyKeys`
correctly rejects an invented one.

```csharp
q.ConfigureScheduler(options => options.PropagateTraceContext = false);
```

**Where it is written.** On the **trigger's** data map, by the scheduler, at every entry point that
stores a trigger — both `ScheduleJob` overload pairs, `ScheduleJobs`, both `TriggerJob` forms and
`RescheduleJob`. A reschedule counts because it is the call that decided when the firing happens, so the
link follows the move: a replacement trigger rebuilt from the original carries the original's data map,
reserved keys included, and the new context overwrites the old one rather than being kept beside it.

`UpdateTriggerDetails` deliberately writes **no** trace context. It changes metadata on a trigger that
stays scheduled and keeps its fire times, so the trace that asked for the firing is still the one that
scheduled it — re-pointing the link at whoever later edited a description would make it say something
false. (It does normalize a typed input in the map it is given, because that is about what a store can
hold rather than about what scheduled anything.)

Two consequences worth knowing:

- **Two more entries per trigger row.** They are ordinary string job data, so they travel through
  `StoreJobDataAsStrings`, the System.Text.Json write gate, the Newtonsoft serializer, the binary blob
  column and the HTTP wire without any of them knowing about it — and they are visible in
  `MergedJobDataMap`, in the dashboard and in `GET /triggers`, like every other `QRTZ_*` reserved key.
  Turn the option off if that is not wanted.
- **The trigger's map, never the job's.** `[PersistJobDataAfterExecution]` writes back only the job's
  map, so the two never meet and a persisted job cannot carry a `traceparent` forward into its next
  firing.

**A stale context is removed rather than kept.** The scheduler stores the trigger object it was handed
rather than a copy of it, so a trigger scheduled once inside a request and again outside one has the key
*removed* on the second call. A stale link reads exactly like a true one, which makes leaving it the
worst of the three answers.

**Over HTTP it is free.** `POST /jobs/{group}/{name}/trigger` and the scheduling endpoints run inside
ASP.NET Core's own server span, so the request's trace reaches the trigger without `Quartz.AspNetCore`
mentioning tracing at all.

See [Observability](packages/opentelemetry-integration.md#linking-a-firing-to-what-scheduled-it).

## A component of your own is chosen the same way a shipped one is

Three seams that had no code-first spelling at all, and one that only worked through a type-name string:

```csharp
q.UseJobStore<MyJobStore>();                                   // built by the container
q.UseJobStore<MyJobStore, MyJobStoreOptions>(o => o.X = 1);     // with options of its own
q.UseJobStore(provider => new MyJobStore(provider.GetRequiredService<ISchedulerSignaler>()));

q.UseInstanceIdGenerator<HostNameInstanceIdGenerator>();
```

`UseJobStore<T>` is the seam for a store that keeps scheduling data somewhere Quartz has never heard of;
`UseInMemoryStore` and `UsePersistentStore<T>` remain the way to select the stores Quartz ships, since
they configure them as well as choose them. `UseJobStore(IJobStore)`, which takes a store you built
yourself, is unchanged.

`UseInstanceIdGenerator<T>()` replaces the pair of keys `quartz.scheduler.instanceId = AUTO` and
`quartz.scheduler.instanceIdGenerator.type`, and it says both: choosing a generator sets
`GenerateInstanceId`, because a generator that was chosen and then never called is configuration that
says nothing. Only a clustered scheduler generates an id — the generator is not called otherwise — which
is unchanged.

The `<T, TOptions>` shapes are sugar over `ConfigureOptions<TOptions>`: the options are declared as that
scheduler's, so a component that takes `IOptions<TOptions>` under `AddQuartz("reporting", …)` is handed
what was configured for `reporting` rather than the unnamed instance.

## Listener matchers are a collection

The builder's nine listener overloads took `params IMatcher<T>[]`; they take
`params IReadOnlyCollection<IMatcher<T>>`, which is what `IListenerManager.AddJobListener` and
`AddTriggerListener` already took. Existing call sites are unaffected — loose arguments still bind, and
an array is still a `IReadOnlyCollection<T>` — and a caller that holds a `List<IMatcher<JobKey>>` no
longer has to call `ToArray()` on the way in.

## `AddQuartzHttpApi` is registered on the service collection

The HTTP API serves every scheduler in the container through one set of endpoints, so it was never a
scheduler's own setting; it just had nowhere else to be written:

```diff
- services.AddQuartz(q => q.AddQuartzHttpApi());
+ services.AddQuartzHttpApi();
```

The `IQuartzBuilder` form is gone rather than kept as a synonym: written inside `AddQuartz(name, …)` it
read as that scheduler's API while configuring everybody's, and two of them with different `ApiPath`s
were last-writer-wins with nothing to say so. `QuartzHttpApiOptions` stays singular for the same reason —
`ApiPath` is a property of the process.

## The route is named where the endpoints are mapped

`MapQuartzHttpApi` and `MapQuartzDashboard` take a route pattern, which is how the rest of ASP.NET Core
reads — `MapHealthChecks("/health")` — and it puts the path with the application's other routes instead
of in a registration callback somewhere else:

```diff
- services.AddQuartzHttpApi(options => options.ApiPath = "/ops/api");
- services.AddQuartzDashboard(options => options.DashboardPath = "/ops/quartz");
  …
- endpoints.MapQuartzHttpApi();
- endpoints.MapQuartzDashboard();
+ endpoints.MapQuartzHttpApi("/ops/api");
+ endpoints.MapQuartzDashboard("/ops/quartz");
```

Nothing is removed. `QuartzHttpApiOptions.ApiPath` and `QuartzDashboardOptions.DashboardPath` are still
there and still the shape to use when the path comes from configuration, and the parameterless overloads
read them. When both are given the pattern at the map site wins, being the more specific of the two. A
pattern given there is held to the same rule as the option it overrides, and a bad one is an
`ArgumentException` naming the parameter rather than an options-validation failure — by the time the
endpoints are mapped, the validators have already run.

There is no `MapQuartzDashboard(existingComponents, pattern)`: the dashboard page routes are fixed at
`/quartz` when integrating with an application's own Blazor root, which is the same reason a custom
`DashboardPath` is rejected in that mode.

## One shape per registration method

The `AddJob` / `AddTrigger` / `AddCalendar` grid had overloads that said the same thing twice, and
optional parameters that made the no-argument calls ambiguous. Each method now has one pair of shapes:
one taking a configurator, one taking a configurator and the `IServiceProvider`.

| Removed | Use instead |
|---|---|
| `AddJob<T>(JobKey?, …)`, `AddJob(Type, JobKey?, …)` | `WithIdentity(jobKey)` inside the configurator |
| `AddJob<T>()`, `AddJob<T>(JobKey)` with no configurator | `AddJob<T>(j => j.WithIdentity(…))` |

```diff
  var jobKey = new JobKey("awesome job", "awesome group");
- q.AddJob<ExampleJob>(jobKey, j => j.WithDescription("my awesome job"));
+ q.AddJob<ExampleJob>(j => j.WithIdentity(jobKey).WithDescription("my awesome job"));
```

The job type on `AddTrigger<TJob>` is what lets the trigger's job data name the job's properties. A
trigger that only points at its job with `ForJob` has nothing to name, and `AddTrigger` with no type
argument is that call — the 3.x spelling, unchanged.

The interface these methods extend is `IQuartzBuilder`, which is what 3.x called
`IServiceCollectionQuartzConfigurator`. The `AddQuartz` overloads that handed the callback an
`IServiceProvider` alongside it are gone with it: a container built while the container is still being
described could only be a second, throwaway one. Ask for the service provider where it is actually
used — each registration method has a shape that is given one, at the point where the container really
exists:

```diff
- services.AddQuartz((q, serviceProvider) =>
- {
-     var schedule = serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule;
-     q.AddTrigger(t => t.WithIdentity("custom").ForJob(jobKey).WithCronSchedule(schedule));
- });
+ services.AddQuartz(q =>
+ {
+     q.AddTrigger((serviceProvider, t) => t
+         .WithIdentity("custom")
+         .ForJob(jobKey)
+         .WithCronSchedule(serviceProvider.GetRequiredService<IOptions<SampleOptions>>().Value.CronSchedule));
+ });
```

`AddCalendar` takes the same `AddCalendarOptions` record that `IScheduler.AddCalendar` does, instead of
two adjacent bools whose order was impossible to remember, and its first parameter is `name` rather
than `calendarName`:

```diff
- q.AddCalendar<HolidayCalendar>("holidays", replace: true, updateTriggers: true,
-     cal => cal.AddExcludedDay(new DateOnly(2025, 12, 25)));
+ q.AddCalendar<HolidayCalendar>("holidays", new AddCalendarOptions { Replace = true, UpdateTriggers = true },
+     cal => cal.AddExcludedDay(new DateOnly(2025, 12, 25)));
```

Both the options and the configurator are optional in the type-based overload, so
`q.AddCalendar<HolidayCalendar>("holidays")` is a valid registration of an empty calendar.

### A calendar can be built from the container

Both generic `AddCalendar<T>` overloads are constrained `where T : ICalendar, new()` and construct the
calendar themselves, so a calendar that needs a dependency — a holiday list read from a database, a
clock, an options snapshot — could not be registered at all. There is a factory overload for that:

```csharp
q.AddCalendar("holidays", serviceProvider =>
{
    AnnualCalendar calendar = new() { TimeZone = TimeZoneInfo.Utc };
    foreach (MonthDay day in serviceProvider.GetRequiredService<IHolidayList>().Days)
    {
        calendar.AddExcludedDay(day);
    }

    return calendar;
});
```

The factory is handed the scheduler-scoped `IServiceProvider`, so a calendar registered for a named
scheduler is given that scheduler's parts rather than the default scheduler's, and it runs once, when
the scheduler's content is resolved. `AddCalendarOptions` is the optional third parameter as it is on
the other overloads. `QuartzSchedulerBuilder` mirrors it, so a standalone chain stays a chain.

## Plugins are registered like listeners

`AddPlugin` had four shapes, one of which took the plugin's name first and the rest of which could not
take a name at all. It now has the same three shapes as the listener registrations, each with an
optional trailing name:

| Shape | Meaning |
|---|---|
| `AddPlugin<T>(string? name = null)` | the container constructs the plugin |
| `AddPlugin<T>(Func<IServiceProvider, T> factory, string? name = null)` | you construct it |
| `AddPlugin<T, TOptions>(Action<TOptions>? configure = null, string? name = null)` | it is given options of its own |

```diff
- q.AddPlugin("xml", provider => new XmlSchedulingDataProcessorPlugin());
+ q.AddPlugin(provider => new XmlSchedulingDataProcessorPlugin(), "xml");
```

The name is how the scheduler refers to the plugin and the name a `quartz.plugin.<name>.*` key
configures it under — some plugins derive persisted job and trigger keys from it — so it is part of the
deployment's identity rather than a label. Left unset, the plugin's type name is used, exactly as
before.

### A plugin implements only what it has to say

`ISchedulerPlugin.Start` and `ISchedulerPlugin.Shutdown` have default implementations that do nothing.
Most plugins do all their work in `Initialize` — attaching a listener, registering a resolver — and had
to write two empty members to say so:

```diff
  public sealed class MyPlugin : ISchedulerPlugin
  {
      public ValueTask Initialize(string name, IScheduler scheduler, CancellationToken ct = default) { … }
-
-     public ValueTask Start(CancellationToken ct = default) => default;
-
-     public ValueTask Shutdown(CancellationToken ct = default) => default;
  }
```

Nothing about the lifecycle changes: the scheduler still calls both, through the interface, at the same
two moments. An existing plugin that declares them keeps working and needs no change. The plugins
shipped here dropped every such member they had, which is the only visible effect in the public API —
a member that did nothing is no longer on the concrete type. Call the plugin through
`ISchedulerPlugin` if you were invoking one of those directly.

### The two scheduling-data plugins have one surface

`XmlSchedulingDataProcessorPlugin` and `JsonSchedulingDataProcessorPlugin` read one schedule out of two
file formats, and had drifted into two different types: one published `ProcessFile` and no `Shutdown`,
the other a do-nothing `Shutdown` and no `ProcessFile`, and both published the settings that configure
them as get-only properties. Their surface is now the same, and it is the two constructors plus what
`ISchedulerPlugin` and `IFileScanListener` ask for:

| Member | Was | Now |
|---|---|---|
| `XmlSchedulingDataProcessorPlugin.ProcessFile(string, CancellationToken)` | public on the XML plugin, private on the JSON one | private on both. `IFileScanListener.FileUpdated(fileName)` says a file changed, and it is what `FileScanJob` calls — reading a file on demand is that call, made yourself |
| `JsonSchedulingDataProcessorPlugin.Shutdown(CancellationToken)` | public on the JSON plugin, absent on the XML one | absent on both: `ISchedulerPlugin.Shutdown` does nothing by default, and neither plugin has anything to release |
| `FileNames`, `ScanInterval`, `FailOnFileNotFound`, `FailOnSchedulingError` | public get-only on both, with internal setters | internal, like every other shipped component's settings. `FileSchedulingOptions` is what says them, and `quartz.plugin.<name>.fileNames` and friends still write them |
| `Name`, `Scheduler` | public get-only on both | internal. They are what `Initialize` was handed, and the scheduler that handed them over is the one asking |

Nothing here was writable from outside, so nothing that *configured* a plugin stops compiling. Code
that *read* one of these properties is what changes, and the answer is the options rather than the
plugin: `IOptionsFactory<FileSchedulingOptions>.Create(schedulerName)` is the same value from the
place that decided it.

The `FileNames`/`Files` split stays, on purpose: `FileSchedulingOptions.Files` is a `List<string>`
because that is what code and a configuration binder say naturally, and the plugin keeps a delimited
`FileNames` string because `quartz.plugin.<name>.fileNames` is one. The join happens in
`UseXmlSchedulingConfiguration` / `UseJsonSchedulingConfiguration`, and both plugins now agree about it.

### The container is not in the scheduler context

3.x's `ServiceCollectionSchedulerFactory` put the `IServiceProvider` into
`scheduler.Context["Quartz.ServiceProvider"]`, and a plugin that needed the container read it back out
of there. 4.0 writes no such entry. Plugins and listeners are constructed by the container, so they
take what they need by constructor — and a job takes its dependencies the same way:

```diff
- private IServiceProvider? services;
-
- public ValueTask Initialize(string name, IScheduler scheduler, CancellationToken cancellationToken = default)
- {
-     services = (IServiceProvider) scheduler.Context["Quartz.ServiceProvider"]!;
+ private readonly IMyService service;
+
+ public MyPlugin(IMyService service) => this.service = service;
```

The entry was Quartz's plumbing in a map that belongs to the application, and the HTTP API's
`GET …/schedulers/{name}/context` answered `500` for every container-built scheduler because of it
([#3408](https://github.com/quartznet/quartznet/issues/3408)). That endpoint renders every value as
text now, so a context entry of any type reads back.

## Cross-cutting concerns run as middleware

A log scope, a tenant context, a metric or a translation of what a library throws has to *surround* the
call to the job, and on 3.x nothing could. `IJobListener` is notification-only — it is told a job is
about to run and told what it did, with the execution happening between the two notifications rather
than inside them — so the only place left was a job that wrapped another job. That adapter is what ABP,
Elsa and Brighter each ship, and asking for the seam has been open since 2021
([#988](https://github.com/quartznet/quartznet/discussions/988)).

4.0 adds it:

```csharp
public delegate ValueTask JobExecutionDelegate(IJobExecutionContext context, CancellationToken cancellationToken);

public interface IJobExecutionMiddleware
{
    ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default);
}
```

registered on the scheduler's builder in the three shapes listeners already have:

```csharp
q.AddJobMiddleware<LogScopeMiddleware>();
q.AddJobMiddleware(provider => new MeteredMiddleware(provider.GetRequiredService<IMeterFactory>()));
q.AddJobMiddleware(new TenantScopeMiddleware());
```

Middleware is keyed per scheduler, so a named scheduler's is its own; it runs in registration order,
outermost first; and the chain is composed once when the scheduler is built, so one instance serves
every firing and per-firing state belongs in an `AsyncLocal<T>` or in the job's scope rather than in a
field. A scheduler with no middleware has no pipeline at all, so nothing changes for an application
that adds none.

It runs inside the execution span and the duration measurement — what a middleware costs is part of
what the firing cost — and outside the run shell's exception handling, so a `JobExecutionException` a
middleware throws is honoured exactly like one the job raised, `RefireImmediately` and all.

**Nothing about listeners changed.** They stay notification-only, `VetoJobExecution` stays the way to
refuse a fire, and matchers stay a listener's way of choosing which jobs it hears about. The tutorial's
[Job Execution Middleware](tutorial/job-execution-middleware.md) lesson has the whole of it, including
which of the two a given concern belongs in.

### `JobInterruptMonitorPlugin` is retired; a job timeout is middleware

The plugin's opt-in surface was two `JobDataMap` strings the compiler never saw — `"AutoInterruptable"`
to enable it at all, `"MaxRunTime"` for a number of milliseconds whose unit was documented only by the
parse — and behind them a monitor task scheduled per firing. The middleware seam is where that belongs,
so `JobInterruptMonitorPlugin`, `UseJobAutoInterrupt`, `JobAutoInterruptOptions` and both keys are gone.
The replacement is in the core `Quartz` package and is typed end to end:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `q.UseJobAutoInterrupt(o => o.DefaultMaxRunTime = TimeSpan.FromMinutes(5))` | `q.AddJobTimeout(TimeSpan.FromMinutes(5))` |
| `q.UseJobAutoInterrupt()` and no per-job data | `q.AddJobTimeout()`, which bounds only the jobs that declare a budget |
| `.UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)` | nothing — every job the scheduler runs is bounded by the default |
| `.UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "5000")` | `[JobTimeout("00:00:05")]` on the job class |
| no way to exempt one job from the default | `[JobTimeout("00:00:00")]` on the job class |
| `quartz.plugin.jobAutoInterrupt.type = Quartz.Plugin.Interrupt.JobInterruptMonitorPlugin, Quartz.Plugins` | remove the keys; the type no longer exists, so a configuration still naming it fails to load |
| `JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable` / `.JobDataMapKeyMaxRunTime` | removed |

The budget travels with the job type rather than with its stored data, which is why it moved to an
attribute: `[JobTimeout]` is read the same way `[DisallowConcurrentExecution]` is, is inherited from a
base class or an interface, and needs no schema column, no wire format and no migration of existing job
data. Delete the two keys from any job or trigger data map that carries them — they mean nothing now.

**One behaviour is deliberately different, and it is the point of the change.** The plugin interrupted
the firing and stopped there, which the run shell classifies as a *completed* execution: no
`JobExecutionException` reached a job listener, and a trigger's retry policy was never consulted. The
middleware interrupts through the same public `IScheduler.InterruptFireInstance` path and then raises a
`JobExecutionException` naming the budget, so a timeout is an ordinary failure — visible to listeners,
counted as an error, and retried by the trigger's `RetryPolicy` if it has one. A job that swallows its
cancellation and returns normally is reported as timed out too.

What has *not* changed is that a job which ignores its `CancellationToken` cannot be stopped. That was
the plugin's limit as well; `CA2016` is the mitigation.

## Registered schedulers can be listed without being started

`ISchedulerFactory.GetAllSchedulers()` — `GetAllSchedulers()` on 3.x too — lists the schedulers
*something has already created*. It reads `ISchedulerRepository`, and a repository holds instances, so a
scheduler nobody has asked for is not in it. Under a scheduler-per-tenant registration that meant there
was no way to ask a container which tenants it knows about short of building every one of them, which is
the opposite of what an operator wanted when they asked.

`ISchedulerRegistry` answers from the registrations instead, and is registered by `AddQuartz`, so any
container with Quartz in it has one:

```csharp
foreach (SchedulerRegistration registration in await registry.QuerySchedulers())
{
    Console.WriteLine($"{registration.Name}: {registration.Status?.ToString() ?? "not created"}");
}
```

```csharp
public sealed record SchedulerRegistration(string Name, SchedulerOrigin Origin, SchedulerStatus? Status)
{
    public bool IsCreated { get; }   // Status is not null
}

public enum SchedulerOrigin { Container, Runtime }
```

- **`Status` is `null` exactly when nothing has been built under that name.** Asking does not build it —
  that is the whole point.
- **`Origin.Container`** is a scheduler `AddQuartz()` or `AddQuartz(name, …)` registered.
  **`Origin.Runtime`** is one that is in the repository without a registration behind it: a
  `QuartzSchedulerBuilder` scheduler bound by hand, or a remote scheduler from `AddQuartzHttpClient`.
  Nothing in the container owns a runtime scheduler's lifetime.
- The default scheduler is listed under its configured `InstanceName`, which is the one name that is not
  the name it was registered under — it has no service key at all.

Nothing is removed: `GetAllSchedulers()` still means what it always meant, and it is still the call to
make when you want the live schedulers themselves rather than an inventory.

### `GET /schedulers` answers from the registry

The HTTP API's scheduler listing reads `ISchedulerRegistry` too, so it carries the registrations rather
than only what has been built, and each entry says which it is:

```json
{ "name": "acme", "schedulerInstanceId": null, "status": null, "origin": "Container" }
```

| | 3.x and the 4.0 previews | 4.0.0-alpha.3 |
|---|---|---|
| Source | `ISchedulerRepository.LookupAll()` | `ISchedulerRegistry.QuerySchedulers()` |
| `status` | always a name | `null` when nothing has built the scheduler |
| `schedulerInstanceId` | always present | `null` when nothing has built the scheduler |
| `origin` | — | `Container` or `Runtime` |

A reader that assumed `status` and `schedulerInstanceId` are always present has to handle `null`, and
one that took the listing to mean "the schedulers that are running" should filter on `status`. Nothing
else moved: a scheduler's own routes still resolve through the repository, so `GET /schedulers/{name}`
answers `404` for a registration nothing has built.

`Quartz.Dashboard`'s own projection changed with it — `SchedulerHeaderDto.Status` and
`SchedulerInstanceId` are nullable, it gained `Origin` and `IsCreated`, and `SchedulerDetailDto` grew the
`SchedulerMetadata` the dashboard used to drop: `Clustered`, `Persistent`, `JobStoreTypeName`,
`ThreadPoolTypeName`, `ThreadPoolSize`, `RunningSince`, `JobsExecuted` and `Version`. Both types are
public because `IQuartzApiClient` is replaceable; an application that implements that interface itself
has to fill the new members.

`ISchedulerRegistry` is deliberately the *narrow* half of the API [#3338](https://github.com/quartznet/quartznet/issues/3338)
sketches for runtime tenant lifecycle. Adding and removing schedulers at runtime is a 4.1 concern; when it
lands, its manager interface extends this one rather than replacing it.

## A shared database says so when two schedulers disagree about the table prefix

Two schedulers sharing a database are told apart by `SCHED_NAME` and share one table prefix. Pointing one
of them at a different prefix by accident used to be silent in the worst possible way: the scheduler
connects, schema validation passes against the tables it *was* pointed at, it starts, it reports healthy,
and it never sees its tenant's data. `SchemaProvisioning.CreateIfMissing` makes that easier to reach
rather than harder, since it will create the mis-typed table set rather than needing one to be there.

Creating a scheduler now records its database and table prefix, and a scheduler that shares a database
with one already created but disagrees about the prefix is reported at `Warning`, naming both schedulers
and both prefixes. It is a warning rather than an error because separate table sets in one database are
legal and occasionally deliberate; the message says which two registrations to look at and leaves the
decision where it belongs.

The check sees what one container can see, which is its own schedulers. Two processes, or two containers
in one process, sharing a database cannot be compared this way, and a database reached through a provider
that reports no connection string and no `DbDataSource` is not guessed about. Nothing about the check is
configurable, and nothing fails because of it.

## Several schedulers are registered explicitly

`AddQuartz(IConfiguration)` used to look for a `Schedulers` sub-section and, if it found one, register
one named scheduler per child instead of the single scheduler it otherwise registers. One call did two
different things depending on the shape of a file. The fan-out has a name of its own now:

```diff
- services.AddQuartz(builder.Configuration.GetSection("Quartz"));   // with a Schedulers section
+ services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));
```

`AddQuartz(configuration)` throws a `SchedulerConfigException` naming `AddQuartzSchedulers` when it is
handed a section with a `Schedulers` sub-section, and `AddQuartzSchedulers` throws when handed one
without. `AddQuartz(name, configuration)`, which registers one of the schedulers a `Schedulers` section
describes, is unchanged.

The six phases that decide which of a scheduler's descriptions wins — configuration is last-wins,
registration is first-wins — are now documented on `AddQuartz` itself rather than in comments inside it.

## Every scheduler in the container can be configured at once

`ConfigureAllQuartzSchedulers(Action<IQuartzBuilder>)` is new. It is the options pattern's `ConfigureAll`,
for schedulers: the delegate is applied to every scheduler `AddQuartz()`, `AddQuartz(name, …)` or
`AddQuartzSchedulers(…)` registers in the container.

```csharp
services.AddQuartz();
services.AddQuartz("acme", q => q.UseInMemoryStore());
services.AddQuartz("initech", q => q.UseInMemoryStore());

// Both named schedulers and the default one get their own instance of each.
services.ConfigureAllQuartzSchedulers(q =>
{
    q.AddPlugin<AuditPlugin>("audit");
    q.AddJobListener<TenantMetricsListener>();
});
```

**The order of the calls does not matter.** Schedulers already registered are configured where this is
called; schedulers registered afterwards are configured by their own `AddQuartz`. That is the point: a
library that adds something to every scheduler cannot know whether the application registers its
schedulers before or after calling it.

The delegate is handed a builder *per scheduler*, so everything it registers lands under that scheduler's
own service key — exactly as if it had been written inside that scheduler's `AddQuartz(name, q => …)`
callback. A plugin or listener added this way is therefore **one instance per scheduler**, each
initialized with the name of the scheduler it belongs to, rather than one instance shared between them.

It runs after each scheduler's own configuration callback, which is what makes the order immaterial. The
usual precedence follows: registration is first-wins, so a job store or thread pool a scheduler chose for
itself is not replaced by one chosen here; options are last-wins, so a value set here overrides the same
option set on one scheduler, exactly as `ConfigureAll<TOptions>` overrides an earlier named `Configure`.

Remote schedulers registered with `AddQuartzHttpClient` are not built by a builder and are skipped.
Calling it when no scheduler is registered at all is not an error.

`AddQuartzDashboard()` is its first caller: it installs its live-events and history plugins this way, so
a scheduler registered with `AddQuartz("acme", …)` finally has a populated live view and execution
history rather than two silently empty pages.

## `IScheduler` is a service, keyed by the scheduler's name

`AddQuartz()` now registers `IScheduler` as well as `ISchedulerFactory`. The default scheduler is
registered unkeyed and a named scheduler under its own name, so a scheduler is injected the way any other
service is:

```csharp
public sealed class ReportRunner(
    IScheduler scheduler,                                       // the default scheduler
    [FromKeyedServices("reporting")] IScheduler reporting)      // AddQuartz("reporting", …)
```

```csharp
var scheduler = provider.GetRequiredService<IScheduler>();
var reporting = provider.GetRequiredKeyedService<IScheduler>("reporting");
```

Resolving `ISchedulerFactory` and awaiting `GetScheduler()` still works and is unchanged; it is no longer
the only way.

What is registered is a handle rather than the scheduler itself, because building a scheduler is
asynchronous and a container constructs synchronously. Every asynchronous member awaits the scheduler
being built, so they are always safe. The synchronous ones — `SchedulerInstanceId`, `Status`, `Context`
and `ListenerManager` — can only answer once the scheduler exists, and throw
`InvalidOperationException` if reading one would have to build it. Under `AddQuartzHostedService()` that
cannot happen: every scheduler in the container is built while the host starts, which is all those
members need — starting them is a separate step, and by default waits until the application has
started. `SchedulerName` is answered from the registration and never builds anything.

## A remote scheduler is registered by name, not by a marker interface

`AddQuartzHttpClient<TScheduler>(…)` is removed, along with the runtime type generation behind it. A
second remote scheduler needed a marker interface only because the container had no other way to tell two
`IScheduler` registrations apart; the service key says it directly:

```diff
- services.AddQuartzHttpClient<IMyScheduler>("MyScheduler", "QuartzHttpClient");
- services.AddQuartzHttpClient<IMySecondScheduler>("MySecondScheduler", "QuartzHttpClient");
+ services.AddQuartzHttpClient("MyScheduler", "QuartzHttpClient");
+ services.AddQuartzHttpClient("MySecondScheduler", "QuartzHttpClient");

- var mine = provider.GetRequiredService<IMyScheduler>();
+ var mine = provider.GetRequiredKeyedService<IScheduler>("MyScheduler");
```

The marker interfaces themselves can be deleted, and the first remote scheduler registered is still the
unkeyed `IScheduler` for a container that holds only one.

In a container that also holds a local scheduler, **`AddQuartz()` goes first**:

| Order | What happens |
|---|---|
| `AddQuartz()` then `AddQuartzHttpClient(…)` | The local default scheduler owns `GetRequiredService<IScheduler>()`; the remote one is reached by name. This is the arrangement to write. |
| `AddQuartzHttpClient(…)` then `AddQuartz()` | `AddQuartz()` throws `InvalidOperationException` at registration, naming `AddQuartzHttpClient`. Registration is first-wins, so this used to make "the scheduler" the remote one with no error at all — a program that thought it held its own scheduler was scheduling jobs in somebody else's process. |
| `AddQuartzHttpClient(…)` then `AddQuartz("Local", …)` | Fine either way round. A named scheduler is keyed by its name and never wanted the unkeyed slot. |

### A client is named or built, never handed over

`HttpClientOptions.HttpClient` and the `AddQuartzHttpClient(schedulerName, HttpClient, …)` overload are
removed. An options object is bound from configuration, cached and shared; a live `HttpClient` sitting
in one is a disposable resource with no owner, unreachable from `appsettings.json`, and it goes around
`IHttpClientFactory` — which is what keeps a long-lived client from pinning stale DNS.

There are two shapes, both of which say who made the client:

| Shape | How |
|---|---|
| A named `IHttpClientFactory` client — prefer this | `AddQuartzHttpClient(name, "QuartzHttpClient")`, or `options.HttpClientName` |
| A factory of your own | `AddQuartzHttpClient(name, provider => …)`, or `options.CreateHttpClient` |

```diff
- var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000/quartz-api/") };
- services.AddQuartzHttpClient("MyScheduler", client);
+ services.AddHttpClient("QuartzHttpClient", c => c.BaseAddress = new Uri("http://localhost:5000/quartz-api/"));
+ services.AddQuartzHttpClient("MyScheduler", "QuartzHttpClient");
```

To keep building the client yourself, wrap it in a factory — it runs once, when the scheduler is first
resolved, and is handed the container:

```diff
- services.AddQuartzHttpClient("MyScheduler", client);
+ services.AddQuartzHttpClient("MyScheduler", _ => client);
```

Either way the client belongs to whoever made it. The scheduler never disposes it.

This removes `Quartz.HttpClient`'s only use of `System.Reflection.Emit`, so the package no longer emits a
type at runtime for a scheduler interface — one fewer thing standing between it and ahead-of-time
compilation.

Remote schedulers are also bound into `ISchedulerRepository` when the host starts, rather than the first
time one is injected. A dashboard or an HTTP API listing the container's schedulers now shows them
without something else having had to use them first. They are bound under their own name; reading a
remote scheduler's instance id costs a request, and a name identifies one registration on its own. A
container with no host is unaffected — nothing runs the binder, and the scheduler is built when it is
first used, exactly as before.

## Quartz can be added to the host application builder

`AddQuartz` and `AddQuartzHostedService` have `IHostApplicationBuilder` overloads, which is how an
application built by `Host.CreateApplicationBuilder` or `WebApplication.CreateBuilder` adds everything
else. A builder has both halves of what the configuration overload needs, so the section is found rather
than handed over:

```diff
- services.AddQuartz(builder.Configuration.GetSection("Quartz"), q => { });
- services.AddQuartzHostedService();
+ builder.AddQuartz(q => { });
+ builder.AddQuartzHostedService();
```

The section it reads is `Quartz`, which is the name every sample and documentation page uses. An
application whose Quartz configuration lives elsewhere passes the section explicitly, through the
`IServiceCollection` overloads — nothing was removed, and they mean exactly what they always did.

A string is a scheduler's name here as it is everywhere else in Quartz: `builder.AddQuartz("reporting")`
registers a scheduler called `reporting` and reads its settings from `Quartz:Schedulers:reporting`, and
`builder.AddQuartzSchedulers()` registers one scheduler per child of that sub-section.

## The hosted service starts every scheduler

`AddQuartzHostedService()` registered the hosted service only if an unkeyed `ISchedulerFactory` was
already in the service collection. Calling it before `AddQuartz()` therefore registered nothing for the
default scheduler and said nothing about it — the application started, and no job ever ran.

The hosted service is now registered unconditionally and resolves its schedulers when the host starts,
so the two calls can be made in either order. It starts every scheduler in the container, the default
one and each named one, which is what the pair of services it replaces did between them.

```diff
- services.AddQuartz(q => …);            // had to come first
  services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
+ services.AddQuartz(q => …);            // either order now
```

A container with no scheduler in it at all is a `SchedulerConfigException` at startup: the hosted
service was asked for, so something was meant to run.

`QuartzHostedServiceOptions` are now named options, keyed by scheduler name. Options passed to
`AddQuartzHostedService(configure)` apply to every scheduler, which is what one shared options object
meant before; a scheduler that has to differ is configured by name, and its settings are applied after
the shared ones whichever order the calls are made in:

```csharp
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
services.AddQuartzHostedService("Reporting", options => options.StartDelay = TimeSpan.FromMinutes(2));
```

`QuartzHostedService`'s constructor changed to match: it takes the `IServiceProvider` it resolves the
schedulers from and an `IOptionsMonitor<QuartzHostedServiceOptions>` instead of one factory and one
options object. Subclasses registered with `AddQuartzHostedService<T>()` need their constructors
updated; the `Starting`/`Started`/`Stopping`/`Stopped` overrides are unchanged. The internal
`NamedSchedulerHostedService` is gone, its work having moved into the one service.

### A hosted scheduler can be started by the application

New in 4.x, with nothing to migrate. `QuartzHostedServiceOptions.AutoStart` defaults to `true`, which is
what the hosted service has always done; setting it to `false` has the scheduler resolved, initialized
and bound with the host but left in `SchedulerStatus.Created` for the application to start:

```csharp
services.AddQuartzHostedService("Reporting", options => options.AutoStart = false);
```

On 3.x the only way not to start a scheduler was not to register the hosted service, which lost the
shutdown handling with it. The scheduler is bound, so `ISchedulerRegistry`, the dashboard and
`GET /schedulers` all report it, and it is still shut down when the host stops — opting out of the start
is not opting out of the stop. `AutoStart` wins over `AwaitApplicationStarted` and `StartDelay`, both of
which say *when* the hosted service starts a scheduler that it does not start at all.

The health check follows: a scheduler in `Created` whose `AutoStart` is `false` is reported **degraded**
rather than **unhealthy**, on the same argument as standby — it is doing what it was told, and failing
the probe would take a correctly configured node out of rotation. A `Created` scheduler that nothing
opted out of is unhealthy as before.

A library embedding Quartz in someone else's application is what this is for, and the how-to page on
embedding Quartz in a library covers the whole pattern.

## The ASP.NET Core methods say Quartz once

| Before | After |
|---|---|
| `IQuartzBuilder.AddHttpApi(…)` | `IServiceCollection.AddQuartzHttpApi(…)` |
| `IEndpointRouteBuilder.MapQuartzApi()` | `MapQuartzHttpApi()` |

```diff
- services.AddQuartz(q => q.AddHttpApi());
+ services.AddQuartzHttpApi();

- app.MapQuartzApi().RequireAuthorization();
+ app.MapQuartzHttpApi().RequireAuthorization();
```

The health check is no longer one of them — it ships in the core `Quartz` package (see
[The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore)),
and it composes with an application's other checks rather than needing a call of its own:

```csharp
services.AddHealthChecks()
    .AddSqlServer(connectionString)
    .AddQuartz()
    .AddQuartz("Reporting");
```

`services.AddQuartzHealthChecks()` still works and is now shorthand for
`services.AddHealthChecks().AddQuartz()`. A named scheduler can also ask for its own check from inside
`AddQuartz`, and it reports on *its* scheduler rather than the default one; either way it is named
`quartz-scheduler-<scheduler name>` unless you say otherwise:

```csharp
services.AddQuartz("Reporting", q => q.AddQuartzHealthChecks(options => options.Tags.Add("ready")));
```

`QuartzHealthCheckOptions` goes through the options pipeline. It used to be constructed and read inside
the registration call, so `services.Configure<QuartzHealthCheckOptions>(...)` and a configuration section
bound to the type silently did nothing; both now apply, whichever order they are written in. The options
belong to the scheduler being checked, so a named scheduler's are configured under its name:

```csharp
services.Configure<QuartzHealthCheckOptions>("Reporting", options => options.Tags.Add("ready"));
```

`QuartzHealthCheckOptions.Name` is nullable, and left unset the check is named after the scheduler it
reports on. Assigning a name still overrides that.

## `AddQuartzServer` is `AddQuartzHostedService`

`Quartz.AspNetCore.AddQuartzServer` did two unrelated things behind one name: it registered the
hosted service that starts the scheduler, and — on frameworks that had them — a health check. Both
are separately available, and neither was ever specific to ASP.NET Core, so the combined method is
gone and each half is called by its own name:

```diff
  services.AddQuartz(q => { /* ... */ });

- services.AddQuartzServer(options => options.WaitForJobsToComplete = true);
+ services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
+ services.AddQuartzHealthChecks();
```

Both halves live in the core `Quartz` package, so an application replacing `AddQuartzServer` needs no
`Quartz.AspNetCore` reference at all — see
[The hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler) for what the
hosted service now starts,
[The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore)
for where the check went, and
[The ASP.NET Core methods say Quartz once](#the-asp-net-core-methods-say-quartz-once) for what is left
in that package.

The health-check overload that took `IEnumerable<string> healthCheckTags` is gone with it; tags are
`QuartzHealthCheckOptions.Tags`, which is added to rather than assigned:

```diff
- services.AddQuartzServer(configure, healthCheckTags: ["ready", "live"]);
+ services.AddQuartzHealthChecks(options => options.Tags.AddRange(["ready", "live"]));
```

## The OpenAPI calendar schema names the properties the payload actually uses

The HTTP API's endpoints handle `ICalendar`, which OpenAPI cannot describe, so the published document has
always been shaped by a stand-in type. Nothing compiled against that stand-in, so two of its property names
were simply wrong, and a client generated from the document did not round-trip a calendar at all:

| Schema said | Server sends |
|---|---|
| `calendarType` | `type` |
| `calendarBase` | `baseCalendar` |

Nothing about the wire format changed — the server has always written `type` and `baseCalendar`. What changes
is generated clients: regenerate against 4.x and the two properties on your calendar model are renamed, which
is a compile error at each use rather than a silent one. Hand-written clients were already having to send
`type` and `baseCalendar` to be understood, so they need no change.

The rest of the schema was already right: `description`, `timeZoneId`, `excludedDays`, `excludedDates`,
`cronExpressionString`, `rangeStart`, `rangeEnd` and `invertTimeRange` are what a calendar of each built-in
type serializes to. A test in `Quartz.Tests.AspNetCore` now compares the stand-in against those names in both
directions, so the schema cannot quietly drift again.

## Remoting a scheduler is not a Quartz concern

`ISchedulerProxyFactory` and `HttpSchedulerProxyFactory` are removed, and the `quartz.scheduler.proxy*`
and `quartz.scheduler.exporter*` keys are now rejected rather than silently accepted and ignored.

Nothing read them. `ISchedulerProxyFactory` had no caller inside Quartz once .NET Remoting went away —
no builder member reached it, no configuration key selected it — while the key validator still
whitelisted `quartz.scheduler.proxy`, so a configuration file could carry a proxy setting that changed
nothing. A configuration that still carries one now gets a `SchedulerConfigException` saying what to
use instead:

```diff
- quartz.scheduler.proxy = true
- quartz.scheduler.proxy.type = Quartz.Impl.HttpSchedulerProxyFactory, Quartz.HttpClient
```

```csharp
// talk to a remote scheduler over HTTP, from Quartz.HttpClient
services.AddQuartzHttpClient("Quartz ASP.NET Core Sample Scheduler", "QuartzHttpClient");

// or serve one over HTTP: AddQuartzHttpApi + MapQuartzHttpApi, from Quartz.AspNetCore
```

## Tasks Changed to ValueTask

In a majority of interfaces that previously returned or took a `Task` or `Task<T>` parameter, these have been changed to `ValueTask` or `ValueTask<T>`.

In most cases, all you will need to do is adjust the signature from `Task` to `ValueTask`.

For example, to migrate jobs:

```csharp
// 3.x
public async Task Execute(IJobExecutionContext context)

// 4.x
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
```

`Execute` also gained a `CancellationToken`. See [Jobs take a CancellationToken](#jobs-take-a-cancellationtoken)
below for why, and for what to do with it.

Listeners are the one place where a missed `Task` → `ValueTask` does not fail the build. Every member of
`IJobListener`, `ITriggerListener` and `ISchedulerListener` has a default implementation, so a 3.x signature
still compiles and simply stops implementing anything — the default runs, and the method is never called.
Quartz refuses a listener in that shape when it is registered, naming the member; see
[The compiler will not point at the callbacks you have to change, but the registration will](#the-compiler-will-not-point-at-the-callbacks-you-have-to-change-but-the-registration-will).

::: warning
The following operations should never be performed on a `ValueTask<TResult>` instance:

* Awaiting the instance multiple times.
* Calling `AsTask` multiple times.
* Using `.Result` or `.GetAwaiter().GetResult()` when the operation hasn't yet completed, or using them multiple times.
* Using more than one of these techniques to consume the instance.

If you need `Task` semantics (e.g., to await multiple times), call `.AsTask()` on the `ValueTask` once and work with the resulting `Task`.
:::

For more information on `ValueTask` please see [Microsoft docs](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1).

## SystemTime Replaced with TimeProvider

`SystemTime` has been removed. To provide a custom time source (e.g., for testing), inject a `TimeProvider` via configuration:

```csharp
// 3.x
SystemTime.UtcNow = () => new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

// 4.x — use TimeProvider
var builder = QuartzSchedulerBuilder.Create();
builder.UseTimeProvider(new FakeTimeProvider());
var scheduler = await builder.BuildScheduler();
```

Under a host, the same call goes on the `AddQuartz` builder:

```csharp
services.AddQuartz(q => q.UseTimeProvider(new FakeTimeProvider()));
```

### The clock is a construction parameter, which is an API-shape change

`SystemTime.UtcNow` was a public mutable field: anything could assign it, from anywhere, at any time,
and every scheduler in the process saw the result. `TimeProvider` is handed to a scheduler when it is
built. That is the point — but it means anything of yours that *wraps* scheduler construction has to
grow a way to be told which clock to use, because there is no longer a hook to reach in afterwards.
Builders are the same story: a trigger built by `TriggerBuilder.Create()` with no argument reads
`TimeProvider.System`, so a component that builds triggers outside the container needs the clock passed
to it. See [The trap: triggers built outside the container](tutorial/time-and-timeprovider.md#the-trap-triggers-built-outside-the-container).

### Replacing a `SystemTime` *offset*, not a freeze

`FakeTimeProvider` — from `Microsoft.Extensions.TimeProvider.Testing` — is the right answer when a test
owns the whole clock: it starts stopped and only moves when you advance it. It is the wrong answer when
something else in the process still computes from the real clock, because the two then disagree by
however long the test has been running, and it cannot be moved backwards at all.

3.x code that assigned an *offset* — `SystemTime.UtcNow = () => DateTimeOffset.UtcNow + skew` — wants a
clock that still runs, and that is about thirty lines:

<!-- snippet: sample_migration_offset_time_provider -->
```csharp
/// <summary>
/// A clock that runs at the system's speed, shifted by an offset that can be moved at will —
/// forwards or backwards — without stopping.
/// </summary>
public sealed class OffsetTimeProvider(TimeProvider inner) : TimeProvider
{
    private long offsetTicks;

    public TimeSpan Offset
    {
        get => TimeSpan.FromTicks(Interlocked.Read(ref offsetTicks));
        set => Interlocked.Exchange(ref offsetTicks, value.Ticks);
    }

    public override DateTimeOffset GetUtcNow() => inner.GetUtcNow() + Offset;

    public override TimeZoneInfo LocalTimeZone => inner.LocalTimeZone;

    public override long GetTimestamp() => inner.GetTimestamp();

    public override long TimestampFrequency => inner.TimestampFrequency;

    // Left to the real clock deliberately: a timer that only fires when something advances the
    // offset would deadlock every wait inside the scheduler.
    public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        => inner.CreateTimer(callback, state, dueTime, period);
}
```
<!-- endSnippet -->

<!-- snippet: sample_migration_offset_time_provider_use -->
```csharp
OffsetTimeProvider clock = new(TimeProvider.System);
services.AddQuartz(q => q.UseTimeProvider(clock));

// ... and later, from the test or the diagnostic endpoint that owns it:
clock.Offset = TimeSpan.FromHours(26);
```
<!-- endSnippet -->

`CreateTimer` deliberately delegates to the real clock. A timer that only fires when something advances
the offset would stall every wait inside the scheduler — which is exactly what `FakeTimeProvider` does,
and why advancing it is not enough on its own. When you *do* want a frozen clock, advance it and then
wait for the scheduler to react rather than sleeping; see
[Controlling time](tutorial/testing.md#controlling-time).

### A clock belongs to one scheduler

`UseTimeProvider` used to replace the container's `TimeProvider` registration, so calling it on one named
scheduler re-timed every scheduler in the container. It now registers at the scheduler's own slot, and one
scheduler can be driven by a fake clock while the rest keep real time:

```csharp
services.AddQuartz("reporting", q => q.UseTimeProvider(new FakeTimeProvider()));
services.AddQuartz("billing", q => …);   // still on the system clock
```

A scheduler that was not given a clock of its own asks the container, so an application-wide
`TimeProvider` registration is inherited by every scheduler exactly as it was. In full, most specific
first:

| Where the clock comes from | Beats |
|---|---|
| `UseTimeProvider(...)` on that scheduler's builder | everything below |
| a `TimeProvider` registered in the container | the key and the default |
| `quartz.timeProvider.type` | the default |
| `TimeProvider.System` | — |

The third row is the fix to a precedence inversion: `quartz.timeProvider.type` was applied by replacing
the registration *after* the configuration callback had run, so a leftover key in a configuration file
silently overrode the clock the application had chosen in code. It is now tried rather than replaced, like
every other implementation a flat key names — code beats strings in both directions, opposite orders
notwithstanding.

Triggers built by `q.AddTrigger(...)` and `q.ScheduleJob(...)` are built with their scheduler's clock, so
a trigger given no start time starts at the time that scheduler thinks it is.

### The scanning jobs take a clock and speak `DateTimeOffset`

`DirectoryScanJob` and `FileScanJob` compare a file's last write time against "now" to decide whether a
file has settled enough to report. That "now" came from the system clock directly, which no test could
move. Both take a `TimeProvider` now:

```csharp
public DirectoryScanJob(TimeProvider? timeProvider = null);
public DirectoryScanJob(IServiceProvider serviceProvider, TimeProvider? timeProvider = null);
public FileScanJob(TimeProvider? timeProvider = null);
```

The job factory hands them the `TimeProvider` in the container — including a scheduler's own, per the
table above. The parameter is optional so that `new DirectoryScanJob()` still compiles, and `null`
means `TimeProvider.System`.

The times they work in are `DateTimeOffset` rather than local `DateTime`, which is the same instant said
unambiguously, and is what the rest of the API has spoken since 3.0:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `protected virtual DateTime FileScanJob.GetLastModifiedDate(string)`, returning `DateTime.MinValue` for a missing file | `protected virtual DateTimeOffset? GetLastModifiedTime(string fileName)`, returning `null` for a missing file |
| `protected void DirectoryScanJob.GetUpdatedOrNewFiles(string, DateTime, DateTime, IReadOnlyCollection<FileInfo>, out List<FileInfo>, out List<FileInfo>, out List<FileInfo>, string, bool)` | private, along with the `DirectoryScanResult` it returns |

`GetUpdatedOrNewFiles` was `protected` but never `virtual`, and `DirectoryScanJob.Execute` is not
virtual either, so no subclass could take part in the scan or do anything with a result it computed.
`IDirectoryScanListener` is the seam, and it is handed the files themselves.

The `LAST_MODIFIED_TIME` entry these jobs keep in their own job data is written as a `DateTimeOffset`
now. A `DateTime` written by an earlier version is still read, as the instant it denoted, so an upgraded
scheduler does not re-report every file it has already seen.

### The shipped jobs are configured by name

Every job in `Quartz.Jobs` was configured by `const string` keys read out of the merged `JobDataMap`,
which is a configuration model with no compiler in it: the key can be misspelled, the value can be of
the wrong type, and a setting the job honours can simply be missing from the documentation — as
`SEARCH_PATTERN` and `INCLUDE_SUB_DIRECTORIES` were, `internal const`s that `DirectoryScanJob` read on
every fire.

Each job now has an options record that maps onto its keys in one place, and an extension that writes
it:

| Job | Options | Extension |
|---|---|---|
| `DirectoryScanJob` | `DirectoryScanOptions` | `UsingDirectoryScanOptions(…)` |
| `FileScanJob` | `FileScanOptions` | `UsingFileScanOptions(…)` |
| `NativeJob` | `NativeJobOptions` | `UsingNativeJobOptions(…)` |
| `SendMailJob` | `SendMailOptions` | `UsingSendMailOptions(…)` |

```diff
  IJobDetail job = JobBuilder.Create<DirectoryScanJob>()
      .WithIdentity("inboxScan")
-     .UsingJobData(DirectoryScanJob.DirectoryNames, "/var/spool/inbox")
-     .UsingJobData(DirectoryScanJob.DirectoryScanListenerName, nameof(InboxListener))
-     .UsingJobData("SEARCH_PATTERN", "*.csv")
-     .UsingJobData(DirectoryScanJob.MinimumUpdateAge, 30000L)
+     .UsingDirectoryScanOptions(new DirectoryScanOptions
+     {
+         Directories = ["/var/spool/inbox"],
+         ScanListenerName = nameof(InboxListener),
+         SearchPattern = "*.csv",
+         MinimumUpdateAge = TimeSpan.FromSeconds(30),
+     })
      .Build();
```

Nothing about what is stored changes. The extensions write the same keys, `FromJobData` reads them
back, and a job scheduled by 3.x reads identically — including a value a job store in
`StoreJobDataAsStrings` mode left behind as a string. Configuring key by key still works, and
`SEARCH_PATTERN` and `INCLUDE_SUB_DIRECTORIES` are `public const`s now, so the literal is no longer
the only way to reach them. `MinimumUpdateAge` is a `TimeSpan` in the options and a millisecond count
in the map, as it has always been.

The extensions are generic in the configurator, so both configuration surfaces keep their type: a
`JobBuilder<TJob>` chain still ends in `Build()`, and the `IJobConfigurator<TJob>` that `AddJob` hands
you still chains its own members.

### The SMTP password does not belong in job data

`SendMailJob` read its credential from the `smtp_username` and `smtp_password` job data entries. Job
data is durable: a persistent job store writes it to `QRTZ_JOB_DETAILS`, every node in the cluster
reads it, the dashboard shows it, and a support-bundle export of that table carries it. `SendMailJob`
takes the credential from the container instead:

```csharp
services.AddSingleton<ICredentialsByHost>(new NetworkCredential("mailer", smtpPassword));
```

`ICredentialsByHost` is what `SmtpClient.Credentials` takes, so a `CredentialCache` covers several
servers. `SendMailOptions` deliberately has no user name or password on it.

The two keys are still read when nothing is registered, so a job scheduled by an earlier version keeps
sending; the job logs a warning saying where the credential now lives. A credential from the container
wins over one in job data.

| 3.x | 4.x |
|---|---|
| `SendMailJob()` | `SendMailJob(ICredentialsByHost? credentials = null)`, which the job factory fills from the container |
| `MailInfo.SmtpUserName` / `MailInfo.SmtpPassword` | `MailInfo.Credentials`, an `ICredentialsByHost?`. An override of `Send` routing mail through another transport gets whichever credential applied |
| `protected virtual MailMessage BuildMessageFromParameters(JobDataMap data)` | `protected virtual MailMessage BuildMessage(SendMailOptions options)` — the same override point, reading a value instead of a bag |
| `protected virtual string GetRequiredParameter(JobDataMap, string)`, `GetOptionalParameter(JobDataMap, string)` | removed; `SendMailOptions.FromJobData` is the one reader, and it reports the same missing key |

## Logging

LibLog has been replaced with `Microsoft.Extensions.Logging.Abstractions`.
Reconfigure logging using an `ILoggerFactory`. Example with a simple console logger:

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddSimpleConsole();
    });
LogProvider.SetLogProvider(loggerFactory);
```

The console tour's [`Logging.cs`](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Examples/Logging.cs)
sets up [Serilog](https://serilog.net/), NLog and Microsoft.Logging behind Quartz, all three in one file.

The example above is the standalone shape. **Under a host there is nothing to do**: `AddQuartz`
registers the scheduler's parts in your container, and they are injected the host's `ILoggerFactory`
like every other service. A `LogProvider.SetLogProvider` call is not needed to see the scheduler, its
loop, its job store, its cluster manager or its thread pool — only for the handful of types nothing can
inject, which the next section lists.

Further information on configuring Microsoft.Logging can be found [at Microsoft docs](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging).

### Every message carries an event id

Every shipped package logs through source-generated `[LoggerMessage]` methods rather than
`logger.LogInformation(…)` and its siblings. Two things follow. Nothing is formatted or
boxed when the level is off, including on the scheduling loop. And every message has a stable event id,
so an operator can filter or alert on the event rather than on its text.

Ids are allocated in ranges by area, and are stable from 4.0 onwards:

| Range | Area |
|---|---|
| 1000–1999 | Scheduler core — the scheduler, its firing loop, the job run shell, the signaler, the error listener |
| 2000–2999 | `RAMJobStore` |
| 3000–3499 | ADO.NET store, its connections and its driver delegate |
| 3500–3599 | Clustering — check-in, failed-instance detection and recovery |
| 3600–3699 | Misfire handling |
| 3700–3799 | Lock handlers |
| 4000–4999 | Configuration, dependency injection and hosting, including the thread pools and job factories |
| 5000–5999 | Serialization, type loading, triggers, calendars, XML scheduling data and the utilities |
| 6000–6199 | The history plugins — `LoggingJobHistoryPlugin` and `LoggingTriggerHistoryPlugin` |
| 6200–6299 | The XML scheduling data plugin |
| 6300–6399 | The JSON scheduling data plugin and its processor |
| 6400–6499 | The job interrupt monitor plugin |
| 6500–6599 | The management plugins — the shutdown hook |
| 7000–7399 | `Quartz.Jobs` — the directory scan job (7000–7099), the file scan job (7100–7199), the native job (7200–7299) and the send mail job (7300–7399) |
| 8000–8099 | `Quartz.Extensions.Redis` — the Redis lock handler |
| 9000–9099 | `Quartz.AspNetCore` — the HTTP API |

Levels and message templates are otherwise **unchanged from 3.x**, so a log query that matched a
message before still matches it. The exceptions:

* `JobStoreSupport.LogWarnIfNonZero` wrote the cluster recovery counts at Information when the count
  was non-zero and at Debug when it was zero, whatever its name said. Those six messages are Warning
  events now, raised only when the count is non-zero. If you filtered them at Information, filter them
  at Warning.
* Four messages carried a typo, corrected while their ids were new: `Removed  {Count} 'complete'
  triggers.` lost a double space, `complete triggers(s)` became `complete trigger(s)`, `Found
  {TriggerGroupDeleteCount}delete trigger group commands.` gained the missing space, and the message
  about a trigger that already existed — spelled with double spaces in one place and single spaces in
  the other — is one event with the single-space spelling. A query matching those on text needs
  updating; one matching on event id does not, which is rather the point.
* One message in `Quartz.Plugins` carried a typo, corrected while its id was new: the interrupt
  monitor's `…scheduled to interrupt with the delay :{Delay}` had the space before the colon rather
  than after it.
* `NativeJob` relayed each line of the spawned process's output through one template, `{Type}>{Line}`,
  with `Type` holding `stdout` or `stderr`. It is now one event per stream — `stdout>{Line}` at
  Information (7201) and `stderr>{Line}` at Warning (7202) — so the text a sink renders is unchanged
  and which stream a line came from is an event id rather than a property. A structured sink that
  indexed the `Type` property no longer receives one; filter on the id instead. The levels are the ones
  the two streams already logged at.

`LoggingJobHistoryPlugin` and `LoggingTriggerHistoryPlugin` are a special case, because the message
they log is a template *you* configure — `JobSuccessMessage` and its siblings, with `{0}`-style
placeholders — and a template only known at run time cannot be a compile-time one. They format it
exactly as before and pass the result through an event whose own template is `{Message}`: the text a
sink renders is unchanged, and each occurrence has an id of its own (6000–6003 for the job plugin,
6010–6012 for the trigger plugin). `StructuredLoggingJobHistoryPlugin` and
`StructuredLoggingTriggerHistoryPlugin` keep plain `ILogger` calls and have no event ids, because their
configured templates carry *named* placeholders that a structured sink resolves into properties for
itself — which is the whole reason those two plugins exist, and which no fixed template can preserve.

## The ambient logger factory stays ambient

`LogProvider.SetLogProvider(ILoggerFactory)` is the one piece of mutable process-wide state left in
Quartz, and it is deliberate rather than overlooked.

Everything the scheduler is made of is built by a container and is injected an `ILogger` the ordinary
way: `QuartzScheduler` and its scheduling loop, the signaler and the error listener, the ADO.NET job
store together with its cluster manager, its misfire handler, its units of work, its driver delegate
and its lock handler, and the components `Use*<T>()` chooses — the thread pool, the job factory, the
type loader, the instance id generator. **Under `AddQuartz`, none of that needs this slot set.**

What is left over cannot be injected anything, and this is the whole of it:

| Type | Why it cannot be injected |
|---|---|
| `JobChainingJobListener` | Constructed by the caller and handed over already built |
| `CronTriggerImpl` | A trigger, which may have been deserialized out of a job store rather than constructed at all |
| `TimeZones`, `MisfireInstructionNames`, `FileUtil`, `QuartzEnvironment` | Static helpers, reached from parsing and deserialization with no scheduler in scope |
| The jobs in `Quartz.Jobs`, and anything else a caller constructs directly | Same reason as the listeners |

A type cannot be handed a logger by a container it never meets, so those sites read the ambient factory
instead of going unlogged. Setting it is how you see *them* — not how you see the scheduler.

A standalone `QuartzSchedulerBuilder` is the one place where setting it still configures everything.
The builder creates a container of its own, which has no logging providers in it unless you register
some on `builder.Services`, so that container's `ILoggerFactory` forwards to this slot. Either way of
saying it works; registering a provider takes precedence.

Nor is the slot seeded from the container, which would be the obvious convenience. It outlives any
one container: a process that builds a host, disposes it and builds another — every integration test
suite, and every application that reloads configuration — would be left holding a disposed
`ILoggerFactory`, and the next logger created anywhere in Quartz would throw
`ObjectDisposedException` from somewhere unrelated to logging. Whoever sets the factory owns its
lifetime, and only the application can make that call. The same applies to a hand-written
`LogProvider.SetLogProvider(host.Services.GetRequiredService<ILoggerFactory>())`: it is correct as
long as the host outlives the schedulers, and under a host it is now only worth writing for the types
in the table above.

`TimeZones.AddResolver` is ambient for the same reason. `FindById` is reached from
parsing a `CronExpression` and from deserializing a trigger out of a job store blob, neither of
which has a scheduler in scope — which is why installing `Quartz.Plugins.TimeZoneConverter` in one
scheduler changes id resolution for the whole process, and why each registration is undone by
disposing it rather than by anyone owning the slot — see
[`TimeZoneUtil` became `Quartz.TimeZones`](#timezoneutil-became-quartz-timezones).

## Job execution metrics

::: danger EVERY NAME CHANGED
Every instrument and every attribute Quartz publishes was renamed in 4.0, and two of the four
instruments were removed. **Dashboards, alerts and recording rules built on the 3.x names all break.**
The [old → new table](#old-and-new-telemetry-names) below is the complete mapping; nothing in it is emitted
under both names, because two names for one series doubles the cardinality and settles nothing.
:::

Three things were wrong at once. The instruments were named `scheduling.quartz.*`, which is neither the
package's name nor OpenTelemetry's convention; the attributes were unprefixed (`job.name`,
`trigger.group`), so any other instrumented library in the process could claim the same names; and the
duration was recorded in milliseconds, into a histogram whose default bucket boundaries assume seconds.
Every scheduler also now publishes them at all — configuring the meter used to be wired to
`StdSchedulerFactory`, so a scheduler registered with `AddQuartz` emitted nothing.

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `quartz.job.execution.duration` | `Histogram<double>` | `s` | `quartz.scheduler.name`, `quartz.scheduler.id`, `quartz.trigger.group`, `quartz.trigger.name`, `quartz.job.group`, `quartz.job.name`, `quartz.execution.group` when the trigger names one, **+ `error.type`** when the execution failed |
| `quartz.job.execution.active` | `UpDownCounter<long>` | `{job}` | the same identity attributes |

Five more instruments cover misfires, acquisition, cluster check-in and recovery, and store round
trips; the [observability page](packages/opentelemetry-integration.md#metrics) has the whole set.

### Old and new telemetry names

| 3.x | 4.x | Notes |
|---|---|---|
| *(none)* | `quartz.trigger.misfire` | New in 4.0. A `Counter<long>` of firings that were owed and did not happen on time |
| *(none)* | `quartz.trigger.acquisition.duration` | New in 4.0. A `Histogram<double>` of what the scheduling loop waits on its store for |
| *(none)* | `quartz.trigger.acquired` | New in 4.0. A `Counter<long>` of what those rounds came back with |
| *(none)* | `quartz.cluster.checkin.duration` | New in 4.0. A `Histogram<double>` of how long a cluster check-in takes, per attempt |
| *(none)* | `quartz.cluster.recovery.trigger` | New in 4.0. A `Counter<long>` of fired-trigger rows recovered from a failed node |
| *(none)* | `quartz.jobstore.operation.duration` | New in 4.0. A `Histogram<double>` of every round trip to the store, tagged with which operation |
| `scheduling.quartz.execute` | *removed* | The histogram's own **count** is the number of executions: `sum(rate(quartz_job_execution_duration_count[5m]))` in Prometheus, or whatever your backend calls a histogram's count |
| `scheduling.quartz.execute.errors` | *removed* | The **`error.type`-tagged subset** of the same count is the number of failures — and it now says *what* failed, which the counter never did |
| `scheduling.quartz.execute.active` | `quartz.job.execution.active` | Also an `UpDownCounter<long>` now (was `Counter<long>`), unit `{job}` (was `ea`) |
| `scheduling.quartz.execute.duration` | `quartz.job.execution.duration` | **Unit `s`, not `ms`** — a chart with a hard-coded millisecond axis reads 1000× low until it is changed |
| `job.name` | `quartz.job.name` | |
| `job.group` | `quartz.job.group` | |
| `job.type` | `quartz.job.type` | Span attribute |
| `trigger.name` | `quartz.trigger.name` | |
| `trigger.group` | `quartz.trigger.group` | |
| `scheduler.name` | `quartz.scheduler.name` | |
| `scheduler.id` | `quartz.scheduler.id` | Span attribute |
| `fire.instance.id` | `quartz.fire.instance.id` | Span attribute |
| `jobstore.trigger.count` | `quartz.jobstore.trigger.count` | Job store span attribute |
| `jobstore.batch.size` | `quartz.jobstore.batch.size` | Job store span attribute |
| *(none)* | `quartz.execution.group` | New in 4.0. On the execution span and both execution instruments, and only when the trigger names a group |
| *(none)* | `quartz.jobstore.operation` | New in 4.0. Which store operation a `quartz.jobstore.operation.duration` measurement is about |
| *(none)* | `quartz.cluster.recovered.instance.id` | New in 4.0. The failed node a `quartz.cluster.recovery.trigger` measurement is about — not the node reporting it, which is `quartz.scheduler.id` |
| `scheduling.quartz.exception_type` | `error.type` | The one attribute that is *not* namespaced, and its value changed too — see below |
| `Quartz.Job.Vetoed` | `Quartz.Job.Veto` | Span name for a vetoed fire. `Quartz.Job.Execute` and the 29 `Quartz.JobStore.*` span names are unchanged — though every store emits the latter now, not only the ADO.NET one |

Renaming a series is not something a backend can do for you: a Prometheus recording rule bridging the
old name to the new one, or a dashboard variable, is the usual way to keep history readable across the
upgrade.

**The constants did not move.** `Quartz.Diagnostics.ActivityTags.JobName` is still `ActivityTags.JobName`;
its *value* changed. Code that reads a tag name through the constants compiles and runs unchanged — it is
the queries written against the strings that need rewriting.

### The two strings you subscribe with are constants now

`Quartz.Diagnostics.QuartzInstrumentation` publishes the `ActivitySource` and `Meter` names, which used
to be internal — so wiring Quartz into OpenTelemetry began by typing `"Quartz"` twice from memory:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
```

Both are still `"Quartz"`, so an existing `AddSource("Quartz")` keeps working.

### Two instruments, not four

`scheduling.quartz.execute` and `scheduling.quartz.execute.errors` are gone. A histogram already carries
its own count, so the first was a second instrument write per fire for a number every exporter derives
from the duration series; and with `error.type` on the histogram, the failures are that count's
`error.type`-tagged subset, which is what the second reported — less precisely, since a counter cannot
say what failed.

**The duration is in seconds.** OpenTelemetry records durations in seconds, and a histogram's default
bucket boundaries are chosen for them: recording milliseconds put every execution longer than ten
seconds into the top bucket, alongside every other duration series in the application, so "how long do
jobs take" was unanswerable from the buckets. `quartz.job.execution.active` is `{job}` rather than `ea`,
which is not a UCUM unit at all.

**Every measurement is tagged with `quartz.scheduler.name`.** A process can run several schedulers — named
registrations, or a host and a test harness side by side — and their measurements used to arrive as one
undifferentiated series. Tagging them apart is a cardinality change: a backend stores one series per
scheduler where it stored one in total, and a query that aggregated across everything now needs to say so
(`sum without (quartz_scheduler_name)` in Prometheus, or the equivalent grouping in whatever reads
these). One extra series per scheduler is the whole of the increase; the tag's values are the scheduler
names an application configured, which is a fixed, small set.

**And with `quartz.scheduler.id`.** A cluster is several schedulers sharing one name, so the name alone
answers "which application" and never "which node" — and the id was on spans only, which is where the
4.0 story stopped in the alphas. Every measurement carries both now, so a slow node, a node that stopped
firing, or a node whose check-ins are taking longer than the rest can be seen without a trace. It is the
same cardinality trade: one series per node where there was one per scheduler, and the values are the
instance ids in the cluster.

The instruments themselves are also created through the container's `IMeterFactory` when it has one,
which every application on the generic host does. The meter used to be a process-wide static, so two
containers in one process published to the same instruments; they now publish to their own, which is what
makes `MetricCollector` — the `Microsoft.Extensions.Diagnostics.Testing` reader that collects one
factory's instruments — able to see them at all. The meter's name and what an exporter subscribes to are
unchanged, and an application that never calls `AddMetrics()` still gets a meter, created directly as
before. Quartz does not register an `IMeterFactory` of its own: doing so would put Quartz's factory in
the way of the application's wherever `AddMetrics()` happened to be called after `AddQuartz`.

**`quartz.job.execution.active` is an up-down counter.** The number of jobs running goes down as
often as it goes up, and Quartz has always measured the decrement — but a `Counter` is monotonic by
OpenTelemetry's definition, so an exporter aggregating one is entitled to drop or mis-render a negative
measurement, leaving a "jobs currently running" chart that only ever climbs. The meaning is unchanged;
the instrument type an exporter sees is not, so a dashboard or an alert built on the old series has to be
rebuilt on a non-monotonic one — a `Sum` with `IsMonotonic = false` in the OpenTelemetry SDK, which
Prometheus renders as a gauge rather than a counter.

**`scheduling.quartz.exception_type` is `error.type`, and it names the exception the job threw.** Two
things were wrong with it. The tag was added to a copy of the tag list and thrown away, so the errors
counter carried the four identity tags and nothing else: an exporter could see that executions failed, but
never what failed. And the type it named was the `JobExecutionException` that the job run shell wraps
anything a job throws in — the same answer for very nearly every failure there is.

The tag now arrives, and it reports the exception an application would recognise. A job that throws
`InvalidOperationException` is reported as `System.InvalidOperationException`, not as the
`JobExecutionException` -> `JobExecutionProcessException` -> cause chain the run shell built around it. A
job that raises a `JobExecutionException` itself has no such wrapper underneath and is reported as
`Quartz.JobExecutionException`, which is what it chose to say. The value is a fully-qualified type name
and nothing else: a message would be unbounded, and an attribute an exporter aggregates on has to have
bounded cardinality.

The name is OpenTelemetry's. [`error.type`](https://opentelemetry.io/docs/specs/semconv/registry/attributes/error/)
is the semantic convention's attribute for what an operation failed with, so these series line up with
every other instrumented failure in the same dashboard; the Quartz-specific spelling is gone rather than
emitted alongside it, since two names for one attribute doubles the series and settles nothing. **A query
or an alert matching on `scheduling.quartz.exception_type` has to be rewritten**, and one that expected
the value `JobExecutionException` now sees the type the job threw.

The tag is on `quartz.job.execution.duration`, so a failed run's duration can be told apart from a
successful one's — and, with the errors counter gone, so that the failures can be counted at all. It is
deliberately *not* on `quartz.job.execution.active`, whose increment and decrement have to carry
identical attributes or the series never comes back to zero.

A vetoed fire is not an execution and appears in neither instrument: the job never ran, so there is no
duration to record. Vetoes are visible in traces, as a `Quartz.Job.Veto` span.

The execution's span carries the same `error.type` with the same value, so one attribute finds a failure
in a trace and in a metric alike. The span also still records the exception as an event, with the whole
wrapper chain, because that is where the stack traces are.

## JSON Serialization

To configure JSON serialization to be used in job store, instead of the old `UseJsonSerializer` you should now use either `UseSystemTextJsonSerializer` or `UseNewtonsoftJsonSerializer`:

```csharp
// 3.x
q.UseJsonSerializer();

// 4.x — System.Text.Json (included in main package)
q.UseSystemTextJsonSerializer();

// 4.x — Newtonsoft.Json (requires Quartz.Serialization.Newtonsoft package)
q.UseNewtonsoftJsonSerializer();
```

Remove the old `Quartz.Serialization.Json` package reference.

### Deserialization failures surface as `Quartz.JsonSerializationException`

| 3.x | 4.x |
|---|---|
| `IObjectSerializer.DeSerialize` could let `Newtonsoft.Json.JsonSerializationException` and `Newtonsoft.Json.JsonReaderException` escape | Every deserialization failure arrives as `Quartz.JsonSerializationException` — a `SchedulerException` — from **both** serializers, with the payload in the message and the underlying parse failure as `InnerException` |

`Quartz.JsonSerializationException` shadows Newtonsoft's type of the same name inside the
`Quartz.Serialization.Newtonsoft` package, because every file in it sits under the `Quartz`
namespace and the enclosing namespace wins over a `using`. The wrapping `catch` therefore never
matched Newtonsoft's parse failures and they escaped raw. Code that catches
`Newtonsoft.Json.JsonSerializationException` around `IObjectSerializer.Deserialize` — or around any
job store read that goes through it — has to catch `Quartz.JsonSerializationException` instead:

```csharp
// 3.x
catch (Newtonsoft.Json.JsonSerializationException e)

// 4.x — the same failure, whichever serializer is configured
catch (Quartz.JsonSerializationException e)
```

### Both serializers refuse a job data value they cannot read back

The System.Text.Json read side is closed on purpose — a stored job data value comes back as a string, a
bool, an int, a long, a double, null or a `Dictionary<string, string>`, and there is no polymorphic
`object` deserialization, which is what keeps a trimmed publish honest. The write side was not closed to
match, so a `JobDataMap` holding a `List<string>` or a `Dictionary<string, object>` serialized without
complaint and threw `JsonSerializationException` on read. The blob was in the database by then, and the
failure belonged to whoever next ran the job.

Writing now refuses such a value, naming the entry and the type:

```
Job data entry 'recipients' holds a System.Collections.Generic.List`1[[System.String, …]], which Quartz's
JSON format cannot read back. A job data value has to be one of the types JobDataMap declares an accessor
for (string, bool, char, int, long, float, double, decimal, DateTime, DateTimeOffset, TimeSpan, Guid,
DateOnly, TimeOnly or an enum), a Dictionary<string, string>, or a type the application declares through
SystemTextJsonSerializerRegistry.AddTypeInfoResolver. Anything with structure of its own has to be
serialized by the job and stored as a string.
```

The set is the one `DataMapExtensions` declares an accessor for, plus `Dictionary<string, string>`, plus
enums by rule. A type of your own is declared through `AddTypeInfoResolver` — the same registration a
trimmed or native-AOT publish already needs — and is then written like any other, coming back as the
string map the store format gives for any object.

The check lives in the job data map converter, which the HTTP API shares with the store serializer, so
it applies there too: a job whose map holds a `List<string>` — in a `RAMJobStore` as much as a
persistent one — now fails to serialize into a `GET` response with the message above, where before the
server sent a payload `Quartz.HttpClient` threw on reading. Both ends use the same closed reader, so a
value the server cannot read back is one the client cannot either, and `AddTypeInfoResolver` is the
answer on both.

**Newtonsoft refuses the same set.** It had the same asymmetry, and one type of its own: a `TimeZoneInfo`
in a job data map was written as `{"$type":"System.TimeZoneInfo, …","Id":"Tokyo Standard Time", …}` and
then read back by nothing — the contract resolver keeps Json.NET off `ISerializable`, and every public
member of a zone is read-only, so there was nothing to rebuild it from. It now refuses against the very
same declaration System.Text.Json refuses against — not a copy of that list, that list — so the two
cannot come to accept different things, and a value written by one serializer is a value the other's
reader has an answer for. The message is the same but for the way out it names:

```
Job data entry 'zone' holds a System.TimeZoneInfo, which Quartz's JSON format cannot read back. … or a
type the application declares through NewtonsoftJsonSerializerRegistry.AddJobDataValueType. …
```

`AddJobDataValueType<T>()` is this package's counterpart to `AddTypeInfoResolver`. Json.NET needs no
metadata handed to it, so the call registers nothing but your word that the type reads back:

```csharp
store.UseNewtonsoftJsonSerializer(json => json.AddJobDataValueType<ReportOptions>());
```

Three things used to round-trip through Newtonsoft and now need that declaration, because the other
serializer never accepted them and a value only one reader can load is a one-way door: a class of your
own, a `JobKey` or `TriggerKey` held as a job data value, and a collection. A `TimeZoneInfo` and a nested
`JobDataMap` are past declaring — Json.NET could not read either back — so store a zone's `Id`, and
serialize a nested structure in the job and store the result as a string. `StoreJobDataAsStrings` was
never affected by any of this.

### A string dictionary is written the same way by both serializers

A `Dictionary<string, string>` is the one job data value with structure that both formats carry, and until
4.0 they did not write it the same way. Json.NET names the type of any value an `object`-typed slot cannot,
so the Newtonsoft serializer wrote the map with the type beside it:

```json
{"headers":{"$type":"System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[System.String, System.Private.CoreLib]], System.Private.CoreLib","tenant":"acme"}}
```

System.Text.Json wrote the plain object, and its reader takes every property of an object as an entry of
the map — so a value written by a Newtonsoft-configured scheduler and read by a System.Text.Json-configured
one came back carrying an assembly-qualified type name under a `$type` key, in the same key space as the
application's own data, with nothing said about it.

4.0's Newtonsoft serializer writes the plain object, which is byte for byte what System.Text.Json writes:

```json
{"headers":{"tenant":"acme"}}
```

**This is a change to the bytes Newtonsoft writes, and both readers read both forms.** A blob already in
the database keeps loading: Json.NET still builds the map out of the type it named, and the
System.Text.Json reader now treats a `$type` property as the metadata it always was rather than handing it
back as an entry. Nothing has to be rewritten, and there is no migration to run.

The one direction that does not hold is backwards: 3.x's Newtonsoft reader has no answer for an object with
no type beside it, and hands back a Json.NET `JObject` instead of the dictionary. That matters only while a
3.x node is reading what a 4.0 node wrote — see
[A mixed 3.x and 4.0 window](operations.md#a-mixed-3-x-and-4-0-window).

**`$type` is now a reserved name inside a stored string map.** Both serializers refuse a map that stores an
entry under it, the same way and at the same moment as any other value they cannot read back:

```
Job data entry 'headers' holds a string map with an entry named '$type'. '$type' is the name Json.NET
writes a value's type under, so Quartz's JSON format reads it as metadata rather than as an entry and no
reader can hand it back. Store that entry under a name of its own.
```

Nothing is lost by that refusal, because such a map never survived a round trip: Json.NET wrote the name
twice and then failed to read its own blob, and a map System.Text.Json wrote was one Json.NET could not
load at all. The refusal is what turns a blob in the column that nobody can read into a failure the
application that wrote it hears about.

### Custom trigger and calendar serializers are no longer static

The static registration methods have been removed, because they wrote into process-global dictionaries: two
schedulers in one process could not have different custom serializers, and registration order silently
decided which one won.

```csharp
// 3.x
SystemTextJsonObjectSerializer.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
NewtonsoftJsonObjectSerializer.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());

// 4.x — register through the store builder; what the callback registers belongs to that scheduler alone
q.UsePersistentStore(store => store.UseSystemTextJsonSerializer(json =>
{
    json.AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());
    json.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());
}));
```

If you construct a serializer yourself, pass it a registry instead:

```csharp
var registry = new SystemTextJsonSerializerRegistry()
    .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());

var serializer = new SystemTextJsonObjectSerializer(registry);
```

The registries start out knowing every built-in trigger and calendar type, so a custom registration adds to
that set. The parameterless serializer constructors still exist and use the built-ins only.

Each package has exactly one registration type: the callback parameter *is* the registry — the same
`SystemTextJsonSerializerRegistry` / `NewtonsoftJsonSerializerRegistry` the serializer constructors
take — rather than an `…SerializerOptions` wrapper that forwarded to it. A lambda body like the one
above compiles unchanged; only an explicitly typed lambda parameter or a variable of the removed
`SystemTextJsonSerializerOptions` / `NewtonsoftJsonSerializerOptions` types needs retyping. The one
member that was not a registration, Newtonsoft's `RegisterTriggerConverters`, is a parameter of the
extension method:

```diff
- store.UseNewtonsoftJsonSerializer(json =>
- {
-     json.RegisterTriggerConverters = true;
-     json.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer());
- });
+ store.UseNewtonsoftJsonSerializer(
+     json => json.AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer()),
+     registerTriggerConverters: true);
```

One consequence worth checking: the HTTP API, the dashboard and `Quartz.HttpClient` also serialize triggers,
and none of them belongs to a single scheduler, so they no longer inherit a scheduler's custom serializers
for free. They read a container-wide registry — register it as a singleton to make a custom serializer
visible there:

```csharp
services.AddSingleton(new SystemTextJsonSerializerRegistry()
    .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer()));
```

See [Serialization (System.Text.Json)](packages/system-text-json) for the full picture.

### A serialized trigger carries its preferred node

Both JSON trigger serializers write a trigger's [node affinity](tutorial/node-affinity.md) pin, as the same
pair the `QRTZ_TRIGGERS` row stores:

```json
"PreferredNode": "node-1",
"PreferredNodeAuto": false
```

`PreferredNode` is the node name — `null` when the trigger is unpinned, `*` for an auto pin no node has
claimed yet — and `PreferredNodeAuto` says whether the node holding the pin claimed it automatically. It
takes both halves: a claimed auto pin and one the caller named look identical from the node name alone, yet
only the automatic one is released when its node stops checking in.

Payloads written before the fields existed, 3.x's included, have neither and read back as
`PreferredNode.None` — an unpinned trigger, which is what they always were.

This matters wherever a whole trigger travels as JSON. The HTTP API, `Quartz.HttpClient` and the dashboard
dropped the pin in both directions, so scheduling or rescheduling a pinned trigger over HTTP quietly
unpinned it, and the same held for a custom `IJobStore` that persists serialized triggers. The ADO.NET job
store was never affected: it keeps the pin in the `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` columns and
reapplies them to every trigger it reads, blob triggers included.

### A Newtonsoft-serialized trigger keeps its time zone

`UseNewtonsoftJsonSerializer` leaves `registerTriggerConverters` off by default, and with it off a trigger is
written as a plain object graph. A `TimeZoneInfo` written that way came out as its whole public surface —
`Id`, `DisplayName`, `BaseUtcOffset` and the rest, every one of them read-only — so reading the object back
set nothing, and the trigger's getter fell through to `TimeZoneInfo.Local`. A trigger stored under
`Tokyo Standard Time` fired on whichever zone the reading machine was in, and nothing said so.

A `TimeZoneInfo` converter is now attached to every property *typed* as one, so a zone on a trigger travels
as its id — the same spelling the trigger and calendar serializers have always used:

```diff
- "TimeZone": { "Id": "Tokyo Standard Time", "DisplayName": "(UTC+09:00) Osaka, Sapporo, Tokyo", … }
+ "TimeZone": "Tokyo Standard Time"
```

Both forms are read, so blobs already written in the object form still load and pick their zone out of `Id`.
`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl` were affected;
`CronTriggerImpl` was not, because its zone rides on the `CronExpression`, which has always had a converter
of its own.

The converter is attached per property rather than registered on the serializer, so it changes typed members
only. A `TimeZoneInfo` stored as a *value* in a `JobDataMap` sits in an `object`-typed slot and is written
exactly as before, `$type` and all — that payload has never been readable back (a `TimeZoneInfo` has no
constructor Json.NET can call), and this change neither introduces that nor repairs it. Store the zone's id
if a job needs one in its data.

The ADO.NET job store persists every trigger type Quartz ships through a persistence delegate rather than as
a blob, so this reaches a scheduler only through a trigger type the store has to serialize — a custom one —
or through code that hands a trigger to `IObjectSerializer` itself. The private `timeZoneInfoId` helpers on
those trigger implementations, which had been meant for exactly this and never worked because Json.NET's
default contract does not serialize private members, are gone.

### Newtonsoft types moved out of the core namespaces

The `Quartz.Serialization.Newtonsoft` package used to put types in namespaces that read as if they came from the
core package, and one of its types collided outright: both packages had a `Quartz.JsonConfigurationExtensions`,
which made `UseNewtonsoftJsonSerializer` ambiguous when both were referenced.

| 3.x | 4.x |
|---|---|
| `Quartz.JsonConfigurationExtensions` (Newtonsoft) | `Quartz.NewtonsoftJsonConfigurationExtensions` |
| `Quartz.Serialization.Json.Triggers.ITriggerSerializer`, `TriggerSerializer<T>`, the built-in trigger serializers | `Quartz.Serialization.Newtonsoft.Triggers.*` — this is the namespace a ported file actually names, and in 4.x the *core* package owns the `Quartz.Serialization.SystemTextJson.Triggers` spelling nearest to it |
| `Quartz.ICalendarSerializer`, `Quartz.CalendarSerializer<T>` | `Quartz.Serialization.Newtonsoft.Calendars.*` — the same namespace shape as the System.Text.Json package's `Quartz.Serialization.SystemTextJson.Calendars` |
| `Quartz.Converters.NameValueCollectionConverter` | internal — the serializer registers it itself, and the System.Text.Json package's converter of the same name has always been internal |

`UseNewtonsoftJsonSerializer` itself is unchanged — only the `using` on a file that names one of these types.
`AddCalendarSerializer<TCalendar>` is now constrained to `ICalendar`, matching the trigger side; a call that
passed something else was never going to work at runtime.

Its *argument* is constrained too, in both packages: it takes `CalendarSerializer<TCalendar>` rather than
`ICalendarSerializer`, so the calendar type and the serializer that reads it have to agree.

```diff
- // compiled fine, then threw InvalidCastException on the first calendar that round-tripped
- json.AddCalendarSerializer<HolidayCalendar>(new AnnualCalendarSerializer());
+ json.AddCalendarSerializer(new HolidayCalendarSerializer());   // TCalendar is inferred
```

Existing calls that pair correctly — including ones that spell the type argument out — compile unchanged.
The type argument is now inferable from the serializer, so it can be dropped.

`AddTriggerSerializer<TTrigger>` deliberately keeps `ITriggerSerializer`. The documented way to serialize
a trigger that derives from a built-in one is a serializer that derives from the built-in serializer —
`class ReportTriggerSerializer : CronTriggerSerializer` for a `ReportTrigger : CronTriggerImpl`. That
serializer is a `TriggerSerializer<CronTriggerImpl>`, not a `TriggerSerializer<ReportTrigger>`, and
`TriggerSerializer<T>` is an invariant class, so tightening the argument the same way would reject exactly
the pattern the package recommends. Calendars have no equivalent pattern.

`ICalendarSerializer` also gained `CalendarTypeName`, closing the last contract gap between the two
packages. It is a default interface member returning empty, so an existing implementation compiles
unchanged and keeps its 3.x behavior: matched by the calendar's assembly-qualified type name. When a
serializer provides a name, the registry indexes it under both — the assembly-qualified key always
stays registered, because that is what `CALENDARS.CALENDAR` payloads written by 3.x carry. Calendar
lookups are case-insensitive now, matching the trigger side and the System.Text.Json package.

### The OpenAPI trigger schema describes the whole payload

The HTTP API's endpoints handle `ITrigger`, which OpenAPI cannot describe, so the published document has always
been shaped by a stand-in type. Nothing compiled against that stand-in, so it fell behind the payload it was
meant to describe, and five properties the server has been sending were missing from the schema:

| Property | Present on |
|---|---|
| `nextFireTimeUtc` | every trigger |
| `previousFireTimeUtc` | every trigger |
| `executionGroup` | every trigger |
| `timesTriggered` | every trigger type except `CronTrigger` |
| `recurrenceRule` | `RecurrenceTrigger` |

`RecurrenceTrigger` itself was also missing from the list of trigger types the `triggerType` discriminator can
carry.

Nothing about the wire format changed — those fields were always on it. What changes is generated clients: a
client regenerated against 4.x gains the five properties, and code that had been reading them through an
escape hatch (a raw `JsonElement`, an extra partial-class member) can read them from the generated model
instead. The two fire times are computed by the scheduler, so a value sent with a trigger being scheduled is
overwritten; `executionGroup`, `timesTriggered` and `recurrenceRule` are read from what you send.

A test in `Quartz.Tests.AspNetCore` now compares the stand-in against the property names a trigger of each
built-in type actually serializes to, in both directions, so the schema cannot quietly fall behind again.

## The ADO.NET store is a store, not a base class

`AdoJobStoreBase`, `LocalTransactionJobStore`, `ExternalTransactionJobStore` and
`AdoJobStoreDependencies` are internal. They still do exactly what they did — `quartz.jobStore.type =
Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz` resolves and constructs the same store, and so
do the `JobStoreTX` and `JobStoreCMT` spellings the bridge translates — but the hierarchy is no longer
something to derive from. That takes 162 members and roughly a hundred `protected` hooks out of the
contract, which was the largest single thing in it and the least used.

The reason is that deriving from it was never the seam it looked like. Every `protected` member below
the two abstract ones is the connection-taking twin of the public member above it — `AddJob(conn, …)`
beside `AddJob(job, …)` — because the public one takes the lock and the twin does the work. Overriding
one of those changes half of an operation.

**The seam for a relational database Quartz does not ship a dialect for is, and always was, the driver
delegate.** `IDriverDelegate`, `StdAdoDelegate` and the six shipped dialects are unchanged and stay
public, as do `ITriggerPersistenceDelegate`, `ILockHandler`, `IDbProvider`, `AdoConstants`,
`ConnectionAndTransactionHolder` and every record they name — `TriggerAcquisitionCriteria`,
`TriggerAcquireResult`, `MisfiredTriggerBatch`, `SchedulerStateRecord` and the rest are delegate
contract rather than store internals, and were never candidates for this. See
[A Driver Delegate for a New Database](how-tos/dialect-delegate.md).

**If you derived from `JobStoreSupport`, `JobStoreTX` or `JobStoreCMT` on 3.x**, or from
`AdoJobStoreBase` on a 4.0 alpha, there are four answers depending on what the override did:

| What the override did | What to do now |
|---|---|
| Narrowed acquisition — `CreateAcquisitionCriteria`, `MaxCount`, `ExcludedJobTypeNames` | Derive from `DelegatingJobStore` and rewrite the request: `base.AcquireNextTriggers(request with { MaxCount = …, ExcludedJobTypeNames = … })`. `TriggerAcquisitionRequest` is a record and the store copies both fields straight into its delegate criteria, so the exclusion is still applied in SQL rather than after the read |
| Added logging, metrics, tenant routing, fault injection | `DelegatingJobStore`, which is what it was always for — every member is `virtual`. See [A Job Store of Your Own](how-tos/custom-job-store.md) |
| Classified one more of a driver's failures as retryable | `AdoJobStoreOptions.IsTransient`, a predicate consulted before the built-in list. See [What counts as transient](operations.md#what-counts-as-transient) |
| Changed *how a transaction is managed* | Implement `IJobStore`, which is public and stays public. If your transaction model is one the two shipped stores nearly fit, [open an issue](https://github.com/quartznet/quartznet/issues) — that is a gap worth hearing about rather than working around |

`RecoverMisfiredJobsResult` went with them; it was the return type of a `protected` method and nothing
else.

## Sealed and Internalized Types

Many types have been sealed and/or internalized to minimize the API surface that needs to be maintained. If you were extending a type that is now sealed or internal, file an issue to request it be reopened.

The ones most likely to be visible in existing code:

**`QuartzScheduler` and `QuartzSchedulerResources` are internal.** `QuartzScheduler` is the implementation
behind `IScheduler` and was only reachable through `StdScheduler`'s constructor, which is internal too now that
the container builds the scheduler. Resolve `IScheduler` or `ISchedulerFactory`; the settings that used to live
on `QuartzSchedulerResources` are `QuartzSchedulerOptions`.

**`StdAdoConstants` and `IAdoUtil` are internal, and constants are no longer inherited.** `AdoConstants` stays
public — table, column and state names are a real contract for delegate authors — but it is a `static class`
now, and `AdoJobStoreBase`, `StdAdoDelegate` and `DbLockHandler` no longer derive from it or from
`StdAdoConstants`:

```diff
  public class MyDelegate : StdAdoDelegate
  {
-     private string CountRows() => $"SELECT COUNT(*) FROM {TablePrefixSubst}{TableTriggers}";
+     private string CountRows() => $"SELECT COUNT(*) FROM {{0}}{AdoConstants.TableTriggers}";
  }
```

The `Sql*` statement templates on `StdAdoConstants` are not visible at all any more — the exact text of a
statement is not a contract. Build the statement your dialect needs, or override the `GetSelect*Sql` hooks,
which are unchanged.

`AdoConstants` names **every** table the store reads or writes, which in 3.x it did not:
`TableSimplePropertiesTriggers` (`"SIMPROP_TRIGGERS"`) was declared `protected` on
`SimplePropertiesTriggerPersistenceDelegateSupport` and nowhere else. The startup schema validation walks
the names on `AdoConstants`, so it never asked whether `QRTZ_SIMPROP_TRIGGERS` was there, and a database
missing only that table started and failed later — on the first calendar-interval, daily-time-interval or
recurrence trigger written to it. It is `public const` on `AdoConstants` in 4.x and validated with the other
eleven. The `protected` spelling stays on `SimplePropertiesTriggerPersistenceDelegateBase` for a delegate
that derives from it, so a subclass compiles unchanged.

`DbLockHandler.AdoUtil` is `private protected` for the same reason, so a lock handler written outside Quartz
no longer sees it. What such a handler needs from it — preparing a statement and binding a parameter — is
`protected` on `DbLockHandler` itself:

```csharp
protected DbCommand PrepareCommand(ConnectionAndTransactionHolder conn, string commandText);
protected void AddCommandParameter(DbCommand command, string paramName, object? paramValue);
```

`ExecuteSql` is the one method a `DbLockHandler` subclass exists to implement, and it could not be
implemented without these; both shipped row-lock handlers now issue their statements through them, so
`SelectForUpdateLockHandler` and `UpdateRowLockHandler` are literal example code for one of your own. There
is no overload taking a provider-specific data type or a size, because a lock statement binds a scheduler
name and a lock name and both are strings. A handler that does not lock in a database implements
`ILockHandler` directly instead.

**Three trigger persistence delegates became public**, so a custom delegate list can name all five built-ins:
`CronTriggerPersistenceDelegate`, `SimpleTriggerPersistenceDelegate` and
`DailyTimeIntervalTriggerPersistenceDelegate` join `CalendarIntervalTriggerPersistenceDelegate` and
`RecurrenceTriggerPersistenceDelegate`. All five are `sealed`; write your own against
`SimplePropertiesTriggerPersistenceDelegateBase` or `ITriggerPersistenceDelegate`.

**`SchedulerConstants` is a static class** rather than a struct, and every public options type is
`sealed` — `QuartzOptions`, `SchedulingOptions`, `QuartzHostedServiceOptions`, `QuartzHttpApiOptions`
and `HttpClientOptions` among them. None ever had a virtual member, and Quartz constructs each of them
itself inside a `configure` callback, so a derived type was never going to be seen. **`MisfireInstruction` is internal** — see
[the enums are the vocabulary](#the-enums-are-the-vocabulary).

**`HttpScheduler` is `sealed`.** It is a wire client — every member turns a call into an HTTP request — so
deriving from it and overriding half of them produces a scheduler that is partly remote and partly not. Wrap
it in an `IScheduler` of your own if you need to intercept calls.

**Quartz.Dashboard's Blazor components are not API.** They are `public` because the Razor compiler makes them
so, but they are UI and are excluded from the dashboard's public-API baseline. Build against
`QuartzDashboardOptions`, `AddQuartzDashboard` and the model types.

**The `Quartz.Xml.JobSchedulingData20` namespace is gone.** It held the fourteen classes that `xsd.exe`
generated from `job_scheduling_data_2_0.xsd` — `QuartzXmlConfiguration20`, `abstractTriggerType`,
`calendarIntervalTriggerType`, `cronTriggerType`, `entryType`, `jobdatamapType`, `jobdetailType`,
`jobschedulingdataSchedule`, `preprocessingcommandsType`, `preprocessingcommandsTypeDeletejob`,
`preprocessingcommandsTypeDeletetrigger`, `processingdirectivesType`, `simpleTriggerType` and
`triggerType`. They were public only because `XmlSerializer` cannot serialize internal types; nothing
in Quartz took or returned one, and their names never followed .NET conventions because a code
generator picked them. The processor reads the document itself now, so the model is internal — and so,
from this release, is the processor.

**The XML format has not changed** — the schema, the file, and every element and attribute in it are
exactly as they were, and `job_scheduling_data_2_0.xsd` still validates the document before it is
read. Only two failures report differently:

| Input | 3.x / earlier 4.0 preview | 4.0 |
|---|---|---|
| A file that is not well-formed XML | `InvalidOperationException`, "There is an error in XML document (3, 13)", wrapping an `XmlException` | the `XmlException` itself, naming the line, the position and the unclosed elements |
| A file whose elements are not in the `http://quartznet.sourceforge.net/JobSchedulingData` namespace | `InvalidOperationException`, "&lt;job-scheduling-data xmlns=''&gt; was not expected" | `SchedulerConfigException` naming the namespace that was expected |

A schema violation still throws `SchedulingDataValidationException` carrying every error found, and
`XmlSchedulingDataProcessorPlugin` still wraps whatever surfaces in a `SchedulerException`, so a
plugin-based setup sees no change at all.

**Nor will it change.** The schema is frozen at what it already says: `simple`, `cron` and
`calendar-interval` triggers, and none of the trigger kinds or trigger settings written since. A
daily time interval trigger, a [retry policy](#a-trigger-can-carry-a-retry-policy) and an execution
group are expressible in the JSON format and are not expressible in XML, now or later — two file
formats that both grow is two parsers and two schemas for one feature, and the XML one is the one that
has to keep loading files written twenty years ago. It does: **XML scheduling is not deprecated and is
not going away in 4.x**, and a schedule that only needs the three trigger kinds above never has to
move. Write a new schedule as JSON, and move an XML one when it needs something the schema cannot
spell. `UseJsonSchedulingConfiguration` takes the same `FileSchedulingOptions` as its XML twin, so the
registration is one word different.

### The shipped plugins are sealed

`LoggingJobHistoryPlugin`, `LoggingTriggerHistoryPlugin`, `StructuredLoggingJobHistoryPlugin`,
`StructuredLoggingTriggerHistoryPlugin`, `ShutdownHookPlugin`,
`XmlSchedulingDataProcessorPlugin`, `JsonSchedulingDataProcessorPlugin` and `TimeZoneConverterPlugin`
are `sealed`, and so is `NoOpJob`. A plugin is an `ISchedulerPlugin` — four members, all of which a
plugin of your own implements directly — so deriving from a shipped one only ever inherited a
`Name` property and a scheduler reference. Write the plugin against the interface; every shipped
plugin is example code for one.

Two consequences are worth calling out:

* `XmlSchedulingDataProcessorPlugin.TypeLoader` was `protected` and is now private. It was there for a
  subclass to read; the constructor that takes an `ITypeLoader` is how the type loader gets in.
* `LoggingJobHistoryPlugin` and `LoggingTriggerHistoryPlugin` no longer have the `IsInfoEnabled` /
  `IsWarnEnabled` / `WriteInfo` / `WriteWarning` protected hooks. They predate
  `Microsoft.Extensions.Logging` and duplicated `ILogger.IsEnabled`: pass an `ILogger` to the
  constructor and route it wherever you want the history to land, which is what the constructor
  taking `(ILogger<T>, TimeProvider)` is for.

### The shipped implementations are sealed

Where the extension point is an interface or an abstract base, the concrete class beside it is closed.
Each of these was a `public class` with `virtual` members in 3.x:

| Sealed in 4.x | Derive from this instead |
|---|---|
| `AnnualCalendar`, `CronCalendar`, `DailyCalendar`, `HolidayCalendar`, `MonthlyCalendar`, `WeeklyCalendar` | `BaseCalendar`, which stays open, or `ICalendar` directly. Every shipped calendar is a short `BaseCalendar` — the `virtual` `IsDayExcluded` / `SetDayExcluded` / `AreAllDaysExcluded` overrides they offered only made sense for that one calendar's day set anyway |
| `JobExecutionContextImpl`, and its nine `virtual` members with it | `IJobExecutionContext`. A test double implements the interface; a decorator wraps one |
| `DefaultThreadPool`, `ZeroSizeThreadPool` | `IThreadPool`, or `TaskSchedulingThreadPool`, which stays open — see [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `JobChainingJobListener` | The three listener interfaces. Every notification member is a default interface member now, so implementing one directly costs a `Name` at most — see [The three `*Support` base classes are gone](#the-three-support-base-classes-are-gone) |

**The five `*TriggerImpl` types are deliberately not in that list.** A trigger of your own derives from
`TriggerBase` or from any of the five, `HasAdditionalProperties` is `virtual` on `TriggerBase`, and
deriving from a built-in trigger pairs with deriving from that trigger's built-in JSON serializer —
which is why those serializers are public and unsealed in both packages, so a subclass can call
`base.SerializeFields` / `base.DeserializeFields` and keep the built-in fields' stored shape.
`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl` were sealed
during 4.x's development and were reopened for exactly that pairing — see
[Other Breaking Changes](#other-breaking-changes).

`JobChainingJobListener` also changed base. It derived from `Quartz.Listener.JobListenerSupport`, which is
gone, and implements `IJobListener` directly, so its `Name` and `JobWasExecuted` are no longer `override`s.
A chain you customized by deriving from it becomes an `IJobListener` that holds one and forwards.

`RAMJobStore` is sealed too, and has its own section because it comes with a replacement seam — see
[`RAMJobStore` is sealed](#ramjobstore-is-sealed).

## TriggerBase Property Removals

`AbstractTrigger` — the abstract base every trigger implementation derives from — is **`TriggerBase`**,
the .NET spelling of the same idea. It is abstract, so it is never a configuration string and never a
JSON `$type` value; a custom trigger updates its base list and recompiles. (One consequence for the
*binary* escape hatch: a `BinaryFormatter` payload names a private base-class field as
`AbstractTrigger+field`, so reading a 3.x `BLOB_TRIGGERS` payload with the compatibility package on
4.x does not survive the rename — do that part of a binary migration on 3.x, which is the
recommended path anyway; see
[Migrating from binary serialization](packages/json-serialization.md#migrating-from-binary-serialization).)

The following properties have been removed from `TriggerBase` as they are redundant with the `Key` and `JobKey` properties:

| Removed property | Replacement |
|---|---|
| `Name` | `Key.Name` |
| `Group` | `Key.Group` |
| `JobName` | `JobKey.Name` |
| `JobGroup` | `JobKey.Group` |
| `FullName` | `Key.ToString()` |
| `FullJobName` | `JobKey.ToString()` |

`HasMillisecondPrecision` left `ITrigger` and is `protected abstract` on `TriggerBase`. It is how a
trigger describes its own schedule to the base class — which rounds the start time down to the second when it
is false — and nothing outside the trigger acted on it. A custom trigger changes `public override` to
`protected override`; to test the behaviour, assert on `StartTimeUtc.Millisecond` instead of on the flag.

## A blank calendar name is no calendar name

`TriggerBase.CalendarName` stores an empty or whitespace-only name as `null`, and so do
`TriggerBuilder.WithCalendarName` and `TriggerDetailsUpdate.WithCalendarName`, which assign through
it. The name is not trimmed — a calendar is looked up by the exact name it was registered under —
only blanks collapse.

This closes a trap rather than changing a working behaviour. Every job store reads a non-null
calendar name as "this trigger observes a calendar", looks it up, and drops the fire when it is not
found. A trigger holding `""` therefore never fired again, and said so only through a single
`Couldn't find calendar with name ''` line from the ADO delegate — nothing at all under
`RAMJobStore`. Oracle hid the problem entirely, since `''` is `NULL` there.

If a database already holds `CALENDAR_NAME = ''` rows — the dashboard's reschedule wrote them before
[#3294](https://github.com/quartznet/quartznet/issues/3294) was fixed — **no migration script is
needed**. Such a row rehydrates as "no calendar", so the trigger starts firing again on the next
acquisition, and the column is written back as `NULL` the next time the trigger is persisted.

The normalization and the missing-calendar warning both job stores now log are not 4.x-only: they
ship on 3.x as well, so upgrading is not what fixes them. One part of #3294 *is* specific to 4.x:

* `IScheduler.RescheduleJob` throws `SchedulerException` when the new trigger names a calendar that
  does not exist, the way `ScheduleJob` always has. On 3.x it stores the trigger and leaves it
  permanently unfireable, and that was left alone there rather than turning a long-standing silent
  success into a throw on a released branch. If you reschedule onto a calendar you intend to add
  afterwards, add the calendar first.

## The two job stores answer the same way

`RAMJobStore` and the ADO.NET job store implement one interface, and on 3.x they quietly disagreed
about a handful of answers — each had its own tests, written against whatever the store in front of
the author happened to do. `JobStoreContractTest` runs one set of assertions against both stores, and
closing the disagreements it found changed behaviour a 3.x application could have depended on. Each
of these is a 3.x behaviour, not a 4.x regression:

* **A prefix `PauseTriggers` pauses every group it matches.** `RAMJobStore` recorded the *matcher's
  text* rather than the group it matched, so `PauseTriggers(GroupMatcher<TriggerKey>.GroupStartsWith("report"))`
  paused the triggers of only the first matching group, returned that one group, and left a phantom
  entry named `report` in the paused-group set — which then paused anything later added to a group
  literally called `report`. It now records each matched group and pauses all of their triggers, which
  is what the ADO store always did. `ResumeTriggers` with a non-equality matcher forgets those groups
  again; it previously only ever cleared an exact-name pause.

  The semantics both stores now share: a pause remembers the **groups the matcher matched**, not the
  pattern. A trigger added afterwards to a matched group is born paused; a trigger added to a group
  that *would* have matched but did not exist at pause time is not. To pause a group before it
  exists, pause it by exact name — `GroupEquals` deliberately records a group that holds nothing yet.

* **Pausing no longer discards an error.** `RAMJobStore` moved a trigger to `Paused` from every state
  but `Complete`, so pausing a trigger — or the group or job it belongs to — silently overwrote
  `TriggerState.Error`: the failure disappeared from listings and `ResetTriggerFromErrorState` found
  nothing left to reset. Only `Normal` (waiting), acquired and blocked triggers are pausable now,
  matching what the ADO store writes `PAUSED` over. A trigger in error keeps its state, and
  `PauseTrigger` returns false for it, because nothing moved.

  Resetting such a trigger while its group is paused lands it in `Paused` rather than `Normal` on
  both stores, which is what the ADO store always did — the group is still paused, so the trigger
  comes out of error into the pause rather than past it. If you have code that pauses a group to
  suppress a failing trigger, pause it and then reset it: the pause no longer does both.

* **`ResumeAll` on the ADO store now resumes groups that hold no triggers.** It walked the groups it
  found in `QRTZ_TRIGGERS` and cleared only those, plus its own all-groups marker. A group paused
  while empty — `PauseTriggers(GroupEquals("nightly"))` before anything is scheduled into `nightly`,
  which is a supported thing to do — is in no trigger row, so its `QRTZ_PAUSED_TRIGGER_GRPS` row
  survived the resume and went on pausing everything scheduled into that group afterwards, with no
  way to see why short of reading the table. `ResumeAll` clears the whole table for the scheduler
  now, which is what `RAMJobStore` always did. The 3.x row can be removed by hand with
  `DELETE FROM QRTZ_PAUSED_TRIGGER_GRPS WHERE SCHED_NAME = '…'` if a database carries one.

* **The all-groups-paused marker is no longer listed as a group.** The ADO store records `PauseAll`
  as a row named `_$_ALL_GROUPS_PAUSED_$_` in the same table it lists paused groups from, so
  `GetPausedTriggerGroups()` — and `QueryTriggerGroups(new TriggerGroupQuery { Paused = true })` —
  handed back a group name no trigger can ever belong to. Code that displayed the list showed it to
  operators, and code that looped it called `ResumeTriggers` on a group that does not exist. The
  listing and its count filter the marker out now; the marker itself is unchanged, so nothing about
  the schema or about how a pause is recorded moves. `RAMJobStore` never had such a row.

* **A duplicate says so on both stores.** `AddCalendar` over an existing name without
  `Replace = true`, and `AddJob` over an existing key without `replace: true`, raise
  `ObjectAlreadyExistsException` on the ADO store as they always did on `RAMJobStore`. Both calls
  passed through a blanket `catch` that re-wrapped it as a plain `JobPersistenceException` with the
  real exception in `InnerException`, so `catch (ObjectAlreadyExistsException)` worked against one
  store and not the other. `ObjectAlreadyExistsException` derives from `JobPersistenceException`, so
  code catching the base type is unaffected.

* **The threshold instant itself is a misfire on the ADO store too.** A trigger is late once its fire
  time is *at or before* `now - MisfireThreshold`, and that is now one comparison wherever the
  question is asked. The ADO store's periodic sweep asked for `NEXT_FIRE_TIME < @nextFireTime` while
  everything else — `RAMJobStore`, and the ADO store's own single-trigger path that a resumed or
  unblocked trigger goes through — used `<=`, so the ADO store disagreed with the in-memory one and
  with itself about one tick. The acquisition statement moved with it, from
  `NEXT_FIRE_TIME >= @noEarlierThan` to `>`, so that a waiting trigger belongs to acquisition or to
  the misfire handler and never to both. It is a change to the SQL only — no schema migration — and
  the practical effect is that a trigger due at exactly that instant misfires rather than being fired
  late without its policy. 3.x behaves the old way.

* **An unblocked trigger's misfire policy is applied as it is unblocked, in memory too.** When a
  `[DisallowConcurrentExecution]` job finishes, the triggers of that job that sat `Blocked` behind it
  go back to `Waiting` — and a trigger that passed its fire time while it waited has its misfire
  policy applied there and then, so its state and `NextFireTimeUtc` are settled by the time
  `TriggeredJobComplete` returns. The ADO store has always done this; `RAMJobStore` returned the
  trigger to the acquisition set and left the policy to the next acquisition, so a `GetTrigger`
  in between reported the fire time the trigger had already missed, and a trigger past its end time
  read as waiting on one store and finished on the other. As on the ADO store, an unblocked trigger
  whose policy leaves it with nothing to fire is removed rather than kept in `Complete`, so
  `GetTrigger` answers `null` for it. 3.x behaves the old way.

## JobKey and TriggerKey Null Validation

`JobKey` and `TriggerKey` now throw `ArgumentNullException` when you specify `null` for `name` or `group`. Triggers can no longer be constructed with a null group name. If your code was relying on null group names, switch to an explicit group name.

## JobDataMap and SchedulerContext stand alone

On 3.x, `JobDataMap` and `SchedulerContext` both derived from
`Quartz.Util.StringKeyDirtyFlagMap : DirtyFlagMap<string, object>` — three public types across two
namespaces for one concept, and the base chain published two members that could silently destroy
data. The chain is internal now: `JobDataMap` and `SchedulerContext` are sealed, self-contained
dictionaries implementing `IDictionary<string, object?>` and `IReadOnlyDictionary<string, object?>`.

Almost nothing changes at a call site:

* The typed read accessors — `GetInt`, `TryGetDateTime`, `GetString` and the rest — are extension
  members in the `Quartz` namespace (declared in `DataMapExtensions`), on both `JobDataMap` and
  `SchedulerContext`. `map.GetInt("retries")` compiles unchanged. They are deliberately declared for
  the two concrete types, not for `IReadOnlyDictionary<string, object?>`, so they do not appear on
  every string-keyed dictionary in scope. A variable typed as the removed base class is the one
  thing that no longer compiles — type it `JobDataMap` (or `IDictionary<string, object?>` when only
  the dictionary surface matters).
* `Dirty` and `ClearDirtyFlag()` are internal. Calling `ClearDirtyFlag()` from a job made
  `[PersistJobDataAfterExecution]` silently skip re-storing the data — nothing else on the type
  could destroy data like that. To force a data blob rewrite, put a
  `SchedulerConstants.ForceJobDataMapDirty` entry in the source dictionary when constructing the
  map, which remains the supported mechanism (the binary-to-JSON migration recipe uses it).
* The `Get(TKey key)` method 3.x had is gone with the base chain. Use the indexer or `TryGetValue`.
* `JobDataMap.Equals`/`GetHashCode` compare content now. The inherited comparison looked at key sets
  only, so two maps with the same keys but different values counted as equal — and assigning such a
  map as a nested value did not mark the outer map dirty, silently skipping the job store rewrite.
  Equal maps also hash equally now. `SchedulerContext` compares by reference.
* `SchedulerContext` is backed by a `ConcurrentDictionary<string, object?>` and is safe to read and
  write concurrently — plugins write to it while jobs read it, and enumerating it during a write no
  longer races.

The binary-serialized shape of `JobDataMap` is unchanged: the type keeps `[Serializable]`, its
serialization constructor and the `version`/`dirty`/`map` entries, so `JOB_DATA` and `BLOB_TRIGGERS`
blobs written by 3.x still load — see
[`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it).

`IsReadOnly` is an explicit interface implementation and cannot be accessed directly on the maps. `IsFixedSize`, `SyncRoot` and `IsSynchronized` are gone with the non-generic interfaces — see [`JobDataMap` dropped the non-generic collection interfaces](#jobdatamap-dropped-the-non-generic-collection-interfaces).

## `JobDataMap` dropped the non-generic collection interfaces

`JobDataMap` and `SchedulerContext` (on 3.x, their `DirtyFlagMap<TKey, TValue>` base) no longer
implement `System.Collections.IDictionary` or `System.Collections.ICollection`. Those duplicated the
generic interfaces with untyped members that cast at runtime — `Add(object, object)` and the
`object` indexer threw `InvalidCastException` for a key of the wrong type instead of
`ArgumentException` (#1417), and `SyncRoot` handed out a lock object the map never took.

| 3.x | 4.x |
|---|---|
| `((IDictionary) map).Add(key, value)` | `map.Add(key, value)` |
| `((IDictionary) map)[key]` | `map[key]` |
| `((IDictionary) map).Contains(key)` | `map.ContainsKey(key)` |
| `((IDictionary) map).Remove(key)` | `map.Remove(key)` |
| `map.CopyTo(array, index)` (`Array`) | `map.CopyTo(KeyValuePair<string, object?>[], index)` |
| `new JobDataMap(someIDictionary)` | `new JobDataMap(someIDictionaryOfStringToObject)` |

`ISerializable` is untouched, so persisted maps still load. The generic
`JobDataMap(IDictionary<string, object?>)` constructor also took over what the removed non-generic one did
with a `QRTZ_FORCE_JOB_DATAMAP_DIRTY` entry: the entry is not copied, and the new map is left flagged dirty.

The accessor set gained `GetDecimal` and `TryGetDecimal`, so a `decimal` in a job data map can now be
read back the way every other primitive can.

## `JobDataMap`'s typed accessors are extension members

`JobDataMap` declared sixty typed accessors of its own — `GetIntValue`, `TryGetIntValue`,
`GetIntValueFromString`, `TryGetIntValueFromString`, and the same four for `bool`, `char`, `double`,
`float`, `long`, `Guid`, `TimeSpan`, `DateTime` and `DateTimeOffset` — while the
`StringKeyDirtyFlagMap` it derived from declared a second, shorter set doing the same job. Two names
for one lookup, differing only in a suffix. The `…Value` set is gone; the shorter set survives as
extension members in the `Quartz` namespace (declared in `DataMapExtensions`, for both `JobDataMap`
and `SchedulerContext`), so the call sites read the same:

```diff
- int retries = context.JobDetail.JobDataMap.GetIntValue("retries");
+ int retries = context.JobDetail.JobDataMap.GetInt("retries");

- if (map.TryGetTimeSpanValue("timeout", out TimeSpan timeout)) { }
+ if (map.TryGetTimeSpan("timeout", out TimeSpan timeout)) { }
```

| 3.x `JobDataMap` | 4.x extension members |
|---|---|
| `GetBooleanValue`, `GetBooleanValueFromString` | `GetBoolean` |
| `GetCharFromString` | `GetChar` |
| `GetDateTimeValue`, `GetDateTimeValueFromString` | `GetDateTime` |
| `GetDateTimeOffsetValue`, `GetDateTimeOffsetValueFromString` | `GetDateTimeOffset` |
| `GetDoubleValue`, `GetDoubleValueFromString` | `GetDouble` |
| `GetFloatValue`, `GetFloatValueFromString` | `GetFloat` |
| `GetGuidValue`, `GetGuidValueFromString` | `GetGuid` |
| `GetIntValue`, `GetIntValueFromString` | `GetInt` |
| `GetLongValue`, `GetLongValueFromString` | `GetLong` |
| `GetTimeSpanValue`, `GetTimeSpanValueFromString` | `GetTimeSpan` |
| every `TryGet…Value` / `TryGet…ValueFromString` | the matching `TryGet…` |
| `GetNullableGuidValue` | `TryGetGuid`, or read the entry and test it yourself |
| (none) | `GetString`, `TryGetString`, `GetDecimal`, `TryGetDecimal` |
| your own `TryGetValue<T>` extension | **keep it.** 3.x's `JobDataMap` had no `TryGetValue<T>` either, so such a call was always the caller's own extension on `IDictionary<string, object>` — and `JobDataMap` still implements that interface, so it still binds. Do not substitute `TryGet<T>`, which is a different thing: see below |

The `…FromString` half collapses because the retained accessors already convert: a value written as a
string — which is what `StoreJobDataAsStrings` forces, and what `PutAsString` writes — is parsed on the way
out, so one accessor covers both. `GetNullableGuidValue` is the only one without a direct
replacement; it returned `null` both for "absent" and for "present but not a `Guid`", which
`TryGetGuid` distinguishes.

`PutAsString`'s eleven overloads are one generic `PutAsString<T>(string key, T value) where T :
IConvertible`, plus the ones the constraint cannot express (`DateTime`, `DateTimeOffset`,
`DateOnly`, `TimeOnly`, `Guid`, `TimeSpan`). Call sites are unchanged, with two exceptions
described below.

The accessor set also grew: `GetDateOnly`/`TryGetDateOnly`, `GetTimeOnly`/`TryGetTimeOnly`,
`GetEnum<TEnum>`/`TryGetEnum<TEnum>` (an enum written through `PutAsString` stores its name, and
the reader also accepts the underlying number a JSON round trip can produce), and a generic
`TryGet<T>` that is a pure type test over the stored object — no string parsing. `Get<T>` is
the same test said as a question with one answer — it throws a `KeyNotFoundException` naming the key
when there is no entry, and an `InvalidCastException` naming the key, the stored type and the
requested one when there is one of the wrong type — and `GetValueOrDefault<T>(key, defaultValue)` is
the same test with a fallback.

::: warning `TryGet<T>` is not a converting reader
The name invites it, so say it plainly: `TryGet<T>` is `stored is T` and nothing else. It does not parse,
it does not convert, and it returns `false` rather than throwing when the type is wrong. A value written
through `PutAsString`, or by any store running with `StoreJobDataAsStrings = true`, is a `string` — so
`TryGet<int>` on it returns `false` where `TryGetInt` parses it and succeeds. If you are replacing a
`TryGetValue<T>` extension of your own that used `Convert.ChangeType`, the named accessor is the
equivalent; `TryGet<T>` is not.
:::

### `PutAsString` writes round-trip formats now

`PutAsString(key, dateTimeOffset)` used to write the invariant general form — no fractional
seconds — and a `DateTime` argument bound to the `IConvertible` overload, which erased sub-second
precision *and* `DateTimeKind`. Both now write the round-trip ("O") format: what you read back is
what you stored, to the tick, Kind and offset included. Reading is unaffected for existing data —
the accessors parse both the old general form and "O" — but two edges are observable:

* **Overload rebinding**: `map.PutAsString(key, someDateTime)` used to bind to the generic
  `IConvertible` overload and store `"01/02/2026 15:04:05"`; it now binds to the dedicated
  `DateTime` overload and stores `"2026-01-02T15:04:05.0000000"`. Anything *outside* Quartz that
  reads `JOB_DATA` strings and expects the old shape sees the new one after the value is next
  written.
* **`TryGetDateTime` parses with `DateTimeStyles.RoundtripKind`** — its own behavioral change,
  independent of what was written: a stored string ending in `Z` used to come back shifted to the
  reader's local time with `Kind=Local`; it now comes back with the UTC clock reading and
  `Kind=Utc`. That is the correct reading, but a job computing e.g. `DateTime.Now - map.GetDateTime(key)`
  on such a value shifts by the local UTC offset. Such `Z` strings exist in real stores — the
  System.Text.Json serializer writes a boxed UTC `DateTime` as `"…Z"` and hands it back as a raw
  string.

### `PutAsString<T>` is constrained to `IFormattable`

The generic overload asked for `IConvertible`, the legacy conversion interface, and then only ever
called its formatting member. It asks for `IFormattable` now, which is what it actually uses. The
practical effect is a wider set of types, not a narrower one: `Int128`, `Half`, `BigInteger`, `Complex`
and any formattable type of your own were never `IConvertible` and could not be written this way.

`bool` and `char` are `IConvertible` but not `IFormattable` — neither has anything to format for a
culture — so they gained dedicated overloads and keep writing exactly what they wrote before
(`"True"` / `"False"`, and the single character). The six round-trip overloads are unchanged.

`string` is the one type that loses the call: `map.PutAsString(key, someString)` no longer compiles.
Write `map[key] = someString`, which is what it did.

### `PutAsString(string, Guid?)` is gone

Passing `null` stored a present-but-null entry that nothing could read back — `TryGetGuid`
returned `false`, `GetGuid` threw, and under `StoreJobDataAsStrings = true` the null was coerced to an
empty string with the same outcome. An unreadable entry is worse than a missing key, so the
overload is gone rather than fixed: call `PutAsString(key, value.Value)` when there is a value,
and decide explicitly — usually `map.Remove(key)` — when there is not.

## Listener API Changes

The three kinds of listener are managed identically now: a listener is registered under a name, registering the
same name again replaces it, and it is removed by that name.

| 3.x | 4.x |
|---|---|
| `AddJobListener(l, params IMatcher<JobKey>[])` and `AddJobListener(l, IReadOnlyCollection<…>)` | one `AddJobListener(l, params IReadOnlyCollection<IMatcher<JobKey>>)` |
| `AddTriggerListener(l, params IMatcher<TriggerKey>[])` and the collection overload | one `AddTriggerListener(l, params IReadOnlyCollection<IMatcher<TriggerKey>>)` |
| `GetJobListeners()`, `GetTriggerListeners()`, `GetSchedulerListeners()` → arrays | → `IReadOnlyList<T>` |
| `AddJobListenerMatcher`, `RemoveJobListenerMatcher`, `SetJobListenerMatchers`, `GetJobListenerMatchers` | gone — see [Matchers are given at registration](#matchers-are-given-at-registration) |
| `AddTriggerListenerMatcher`, `RemoveTriggerListenerMatcher`, `SetTriggerListenerMatchers`, `GetTriggerListenerMatchers` | gone — same |
| `GetJobListener(name)`, `GetTriggerListener(name)` throw `KeyNotFoundException` | return null |
| `RemoveSchedulerListener(ISchedulerListener)` | `RemoveSchedulerListener(string name)` |
| — | `GetSchedulerListener(string name)` → `ISchedulerListener?` |

C# 13 params collections turn the two `Add*Listener` overloads into a single member that accepts both call
shapes, so existing calls compile unchanged:

```csharp
// both still work
scheduler.ListenerManager.AddJobListener(myJobListener, matcherA, matcherB);
scheduler.ListenerManager.AddJobListener(myJobListener, listOfMatchers);
```

Asking for a listener that is not registered returns null instead of throwing, so checking is no longer an
exception:

```diff
- try { var listener = listenerManager.GetJobListener(name); } catch (KeyNotFoundException) { }
+ IJobListener? listener = listenerManager.GetJobListener(name);
```

### Matchers are given at registration

Eight members let an application edit a registered listener's matcher set at run time, one matcher at a
time, by listener name: `AddJobListenerMatcher`, `RemoveJobListenerMatcher`, `SetJobListenerMatchers`,
`GetJobListenerMatchers`, and their trigger twins. They are gone. Matchers are part of registration,
which is when anyone knows them, and `Add*Listener(listener, params matchers)` is where they are given —
including through the container, as `AddJobListener<T>(matchers)`.

A listener whose matching has to change is registered again under the same name. That replaces the
listener and its matchers together, so the two can never be out of step — which is what the
remove-then-re-add dance these members existed to avoid was actually about:

```diff
- listenerManager.SetJobListenerMatchers("audit", [GroupMatcher<JobKey>.GroupEquals("reports")]);
+ listenerManager.AddJobListener(audit, GroupMatcher<JobKey>.GroupEquals("reports"));
```

```diff
- listenerManager.AddJobListenerMatcher("audit", GroupMatcher<JobKey>.GroupEquals("ingest"));
+ // name every matcher the listener is to have, since the registration replaces the whole set
+ listenerManager.AddJobListener(audit,
+     GroupMatcher<JobKey>.GroupEquals("reports"),
+     GroupMatcher<JobKey>.GroupEquals("ingest"));
```

Reading a listener's matchers back had one real caller — the notification path's own matching — and the
scheduler now reads them from the listener's registration rather than by looking the name up. There is
no replacement for `GetJobListenerMatchers` on `IListenerManager`: hold on to the matchers you registered
with if you need them.

### The broadcast listeners are gone

`BroadcastJobListener`, `BroadcastTriggerListener` and `BroadcastSchedulerListener` fanned one
notification out to several listeners — which is what the listener manager does for every registered
listener already. Register the listeners individually:

```diff
- scheduler.ListenerManager.AddJobListener(new BroadcastJobListener("audit", [first, second]));
+ scheduler.ListenerManager.AddJobListener(first);
+ scheduler.ListenerManager.AddJobListener(second);
```

The one thing a broadcast did that individual registration does not is group several listeners under a
single name, so that one `RemoveJobListener` detached all of them; remove them by their own names
instead. A broadcast was also a public type that had to track every default-implemented member of its
interface forever, and it swallowed what a listener behind it threw, which the manager does not.

### Listeners are told which scheduler is calling

The listener callbacks that run inside a firing have always been able to say who was calling:
`IJobExecutionContext.Scheduler`. The rest could not. A listener registered with two schedulers in one
host — which `AddQuartz("acme", …)` and `AddQuartz("initech", …)` make an ordinary thing to do — was told
that *a* trigger had been paused, or that *a* scheduler had failed, with no way to ask which one (#3063).

There is one rule now, and it holds for all three listener interfaces: **a listener reaches the scheduler
it serves through its execution context, or as an argument of the callback when there is no execution.**
That makes `IScheduler` a parameter of every one of `ISchedulerListener`'s twenty-three members, and of
`ITriggerListener.TriggerMisfired` — the one trigger callback with no context to carry it, because a
misfire is noticed rather than executed.

Where that parameter goes differs between the two interfaces, and the difference is settled rather than
unfinished. `ISchedulerListener` takes the scheduler **first**, because its notifications are about the
scheduler: they have no other subject to lead with, and the scheduler is a scheduler listener's only
channel to the thing it is listening to. `ITriggerListener` leads with the **trigger** on all four of its
members, and `TriggerMisfired` takes the scheduler second, where its siblings take an
`IJobExecutionContext` — see [`TriggerMisfired` takes the trigger
first](#triggermisfired-takes-the-trigger-first).

```diff
- public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
+ public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken = default)
  {
-     logger.LogInformation("Scheduler started");
+     logger.LogInformation("Scheduler {SchedulerName} started", scheduler.SchedulerName);
      return default;
  }
```

It is the `IScheduler` rather than a name or an identity record, because a listener that knows *which*
scheduler usually wants to *act* on it — pause the trigger it was just told about, read `Status`. Identity
is `SchedulerName` and `SchedulerInstanceId` on the same object.

The instance handed over is the one `ISchedulerFactory.GetScheduler()` returns, and the very one
`IJobExecutionContext.Scheduler` carries, so those two compare by reference. An `IScheduler` injected from
the container does not: that is a proxy that resolves the scheduler on first use, so compare a
`SchedulerName` there rather than the objects.

#### `SchedulerError` says what the error was about

`SchedulerError` took a message and an exception, which said what happened but never what it happened to.
Most of the places that raise it know: a job that threw is reported from its execution context, a job that
could not be built from the bundle that was fired. Those facts now travel in a `SchedulerErrorContext`:

```diff
- ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default);
+ ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default);
```

```csharp
public sealed record SchedulerErrorContext
{
    public required string Message { get; init; }
    public required SchedulerException Exception { get; init; }
    public TriggerKey? TriggerKey { get; init; }
    public JobKey? JobKey { get; init; }
    public string? FireInstanceId { get; init; }
}
```

`Message` and `Exception` are the two old parameters, so a listener that only logged needs one rename:

```diff
- public ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default)
+ public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
  {
-     logger.LogError(exception, "{Message}", message);
+     logger.LogError(errorContext.Exception, "{Message}", errorContext.Message);
      return default;
  }
```

The three keys are nullable because some errors genuinely have no subject: a scan for the next trigger to
fire that never reached a trigger, a job store retrying a failed connection, a schedule file that names
many jobs. Read a null as "the scheduler could not say", not as "this concerns no trigger". Where the
scheduler does know — every failure inside a firing, and a misfire notification that a listener broke —
all three are filled in, which is what discussion #3211 asked for: the trigger behind a `SchedulerError`.

`ISchedulerSignaler.NotifySchedulerListenersError` takes the record for the same reason, so a job store of
your own can report the trigger it failed for:

```diff
- ValueTask NotifySchedulerListenersError(string message, SchedulerException exception, CancellationToken cancellationToken = default);
+ ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default);
```

#### The compiler will not point at the callbacks you have to change, but the registration will

Every member of these interfaces has a default implementation, which is what lets a listener implement only
the notifications it cares about — and it is also why a listener that keeps an old signature still compiles.
It simply stops being an implementation of anything: the default runs instead, and the method becomes dead
code that is never called.

So the build does not fail. Registering the listener does. Quartz reads the shape of a listener as it is
registered and refuses one that carries a public method with a notification's name but not its signature,
naming both signatures in a `SchedulerConfigException` (#3398):

```text
MyListener declares 'ValueTask SchedulerError(String, SchedulerException, CancellationToken)', which does
not implement ISchedulerListener.SchedulerError. The interface member is
'ValueTask SchedulerError(IScheduler, SchedulerErrorContext, CancellationToken)': the names match but the
signatures do not, and every member of ISchedulerListener has a default implementation, so this compiles
and the default runs instead. The scheduler never calls SchedulerError. Listener callbacks take
IScheduler scheduler first since 4.0.0-alpha.2. Correct the signature, or rename the method if it is not
meant to be that notification. See
https://www.quartz-scheduler.net/documentation/quartz-4.x/migration-guide.html#listeners-are-told-which-scheduler-is-calling.
```

Where that lands depends on how the listener was registered. `q.AddJobListener<MyListener>()` and the
instance overload throw from the `AddQuartz` call itself, since the listener's type is known while the
configuration is still being written. Everything else — a factory overload declared as the interface, a
listener registered as a plain service, a `quartz.jobListener.*` key, and
`scheduler.ListenerManager.AddJobListener(…)` — throws when the listener is attached to a scheduler, which
for a hosted application is host start.

A method that deliberately overloads a notification's name for an unrelated purpose is refused as well.
Rename it: the alternative is letting the far commoner stale signature through in silence. A listener that
implements the interface explicitly is never examined, because an explicit implementation is not a public
method of the class.

The table below is the whole list of what moved. The compile-time hints are worth knowing too: a callback
that no longer overrides anything and does not touch instance state trips CA1822 ("can be marked as
static"), and an `<inheritdoc />` on it trips MA0196. Both fired on Quartz's own listeners while this change
was made.

#### Every signature that changed

| 3.x | 4.x |
|---|---|
| `ISchedulerListener.JobScheduled(ITrigger, ct)` | `JobScheduled(IScheduler, ITrigger, ct)` |
| `ISchedulerListener.JobUnscheduled(TriggerKey, ct)` | `JobUnscheduled(IScheduler, TriggerKey, ct)` |
| `ISchedulerListener.TriggerFinalized(ITrigger, ct)` | `TriggerFinalized(IScheduler, ITrigger, ct)` |
| `ISchedulerListener.TriggerPaused(TriggerKey, ct)` | `TriggerPaused(IScheduler, TriggerKey, ct)` |
| `ISchedulerListener.TriggersPaused(string?, ct)` | `TriggersPaused(IScheduler, string?, ct)` |
| `ISchedulerListener.TriggerResumed(TriggerKey, ct)` | `TriggerResumed(IScheduler, TriggerKey, ct)` |
| `ISchedulerListener.TriggersResumed(string?, ct)` | `TriggersResumed(IScheduler, string?, ct)` |
| `ISchedulerListener.TriggerInError(TriggerKey, ct)` | `TriggerInError(IScheduler, TriggerKey, ct)` |
| `ISchedulerListener.TriggersInError(JobKey, ct)` | `TriggersInError(IScheduler, JobKey, ct)` |
| `ISchedulerListener.JobAdded(IJobDetail, ct)` | `JobAdded(IScheduler, IJobDetail, ct)` |
| `ISchedulerListener.JobDeleted(JobKey, ct)` | `JobDeleted(IScheduler, JobKey, ct)` |
| `ISchedulerListener.JobPaused(JobKey, ct)` | `JobPaused(IScheduler, JobKey, ct)` |
| `ISchedulerListener.JobsPaused(string?, ct)` | `JobsPaused(IScheduler, string?, ct)` |
| `ISchedulerListener.JobResumed(JobKey, ct)` | `JobResumed(IScheduler, JobKey, ct)` |
| `ISchedulerListener.JobsResumed(string?, ct)` | `JobsResumed(IScheduler, string?, ct)` |
| `ISchedulerListener.JobInterrupted(JobKey, ct)` | `JobInterrupted(IScheduler, JobKey, ct)` |
| `ISchedulerListener.SchedulerError(string, SchedulerException, ct)` | `SchedulerError(IScheduler, SchedulerErrorContext, ct)` |
| `ISchedulerListener.SchedulerStarting(ct)` | `SchedulerStarting(IScheduler, ct)` |
| `ISchedulerListener.SchedulerStarted(ct)` | `SchedulerStarted(IScheduler, ct)` |
| `ISchedulerListener.SchedulerInStandbyMode(ct)` | `SchedulerInStandbyMode(IScheduler, ct)` |
| `ISchedulerListener.SchedulerShuttingDown(ct)` | `SchedulerShuttingDown(IScheduler, ct)` |
| `ISchedulerListener.SchedulerShutdown(ct)` | `SchedulerShutdown(IScheduler, ct)` |
| `ISchedulerListener.SchedulingDataCleared(ct)` | `SchedulingDataCleared(IScheduler, ct)` |
| `ITriggerListener.TriggerMisfired(ITrigger, ct)` | `TriggerMisfired(ITrigger, IScheduler, ct)` |

`IJobListener`'s members and `ITriggerListener`'s other three are unchanged: they all receive an
`IJobExecutionContext`, which is how they reach the scheduler already.

#### `TriggerMisfired` takes the trigger first

`TriggerMisfired` gained its scheduler parameter in 4.0.0-alpha.2 and took it in first position, which
made it the only member of `ITriggerListener` not to lead with the trigger it is about. It leads with the
trigger now, and the scheduler follows it in the position `TriggerFired`, `VetoJobExecution` and
`TriggerComplete` give the `IJobExecutionContext`:

```diff
- public ValueTask TriggerMisfired(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
+ public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
  {
      logger.LogWarning("{SchedulerName} missed {TriggerKey}", scheduler.SchedulerName, trigger.Key);
      return default;
  }
```

Both parameters are still there, so a listener that keeps the old order still compiles — and quietly stops
implementing the interface, with the do-nothing default running in its place. Quartz refuses a listener in
that shape as it is registered, and says the parameters are the notification's own, in the wrong order.

### The three `*Support` base classes are gone

`JobListenerSupport`, `TriggerListenerSupport` and `SchedulerListenerSupport` existed so that a listener could
implement only the notifications it cared about. Every member of `IJobListener`, `ITriggerListener` and
`ISchedulerListener` is now a default interface member — the notifications do nothing, and `Name` returns
`GetType().Name` — so the base classes had nothing left to add, and they cost you your one base class.

Implement the interface instead, and drop `override`:

```diff
- public sealed class MyListener : JobListenerSupport
+ public sealed class MyListener : IJobListener
  {
-     public override string Name => "MyListener";
-     public override ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
+     public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
      {
          ...
      }
  }
```

`Name` can go: it defaults to the type's name. Keep it only when several instances of one type are registered
with the same scheduler, since the later registration would otherwise replace the earlier one.

A listener that derived from one of these base classes fails to compile, which is the outcome you want. A 3.x
listener that implemented the interface directly has the silent version of the same problem: its `Task`-returning
members compile against 4.x and implement nothing. Quartz refuses such a listener when it is registered and
names the member — see
[The compiler will not point at the callbacks you have to change, but the registration will](#the-compiler-will-not-point-at-the-callbacks-you-have-to-change-but-the-registration-will).

One consequence is easy to miss: **a default interface member is not a class member**. Code that reads `Name`
off the concrete type no longer compiles unless the listener declares `Name` itself, so read it through the
interface:

```diff
- var listener = new MyListener();
- scheduler.ListenerManager.RemoveSchedulerListener(listener.Name);
+ ISchedulerListener listener = new MyListener();
+ scheduler.ListenerManager.RemoveSchedulerListener(listener.Name);
```

### Scheduler listeners are identified by name

`ISchedulerListener` has a `Name`, a default interface member returning `GetType().Name`. Registering two
scheduler listeners whose `Name` matches replaces the first, exactly as it has always worked for job and
trigger listeners. Override `Name` if you register several instances of one type with the same scheduler:

```diff
- scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener);
+ scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener.Name);
```

A test double does not run a default interface member, so a faked `ISchedulerListener` needs its `Name`
configured before `AddSchedulerListener` will accept it:

```csharp
ISchedulerListener listener = A.Fake<ISchedulerListener>();
A.CallTo(() => listener.Name).Returns("myListener");
```

### `JobsPaused` and `JobsResumed` take a nullable group

```diff
- ValueTask JobsPaused(string jobGroup, CancellationToken cancellationToken = default);
- ValueTask JobsResumed(string jobGroup, CancellationToken cancellationToken = default);
+ ValueTask JobsPaused(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default);
+ ValueTask JobsResumed(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default);
```

Null means every group, matching `TriggersPaused` and `TriggersResumed`, which were already nullable. The
scheduler's own pause-all path still raises the job events once per group; the signature simply stops claiming
a group name is guaranteed where the trigger twins admit it is not. Implementations with a non-nullable
parameter produce a nullability warning (an error under `TreatWarningsAsErrors`) until the `?` is added.

`JobScheduled(ITrigger)` and `JobUnscheduled(TriggerKey)` remain asymmetric on purpose: once a trigger is
unscheduled there is no trigger left to hand out.

### Two members were renamed

```diff
- ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default);
+ ValueTask SchedulerShuttingDown(CancellationToken cancellationToken = default);

- ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default);
+ ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default);
```

`SchedulerError`'s two parameters became one record on the way — see [Listeners are told which scheduler is
calling](#listeners-are-told-which-scheduler-is-calling).

### Instantiation failures name the trigger

When `IJobFactory` cannot produce a job — a constructor dependency the container cannot resolve is the usual
reason — the trigger has already fired, but there is no `IJobExecutionContext` yet, so no `ITriggerListener` or
`IJobListener` callback can be raised. `SchedulerError` is the only notification, and it used to carry the job
key as interpolated message text and the trigger nowhere at all.

It now receives a `JobInstantiationException`, and the `SchedulerErrorContext` around it names the same
firing without any unwrapping:

```csharp
public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
{
    if (errorContext.Exception is JobInstantiationException failure)
    {
        logger.LogError(failure, "Job {Job} could not be built for trigger {Trigger}, fire {FireInstanceId}",
            errorContext.JobKey, errorContext.TriggerKey, errorContext.FireInstanceId);
    }

    return default;
}
```

Additive — `SchedulerError` already took a `SchedulerException` — and it mirrors what
`JobExecutionProcessException` carries for execution-time failures. Both factory paths raise it: the container's,
where `ActivatorUtilities` throws, and `SimpleJobFactory`'s, where the original failure arrives as the
`InnerException`.

The two messages reporting this also had their closing quote moved to where it belongs, from after the inner
exception's message to after the type name: `Problem instantiating type 'MyNamespace.MyJob': message`. Code
matching on that text should read the exception instead.

To prevent the failure rather than observe it, see [failing fast when job dependencies cannot be
resolved](packages/microsoft-di-integration.md#failing-fast-when-job-dependencies-cannot-be-resolved).

### Triggers entering the error state are reported

A trigger could be parked in `TriggerState.Error` with nothing observing it. The stores logged a line and moved
on, so a trigger simply stopped working and the only way to find out was to poll `GetTriggerState` or read the
log — and two of the ADO store's transitions, a job type that will not load while acquiring and a job that
cannot be read back in `TriggersFired`, did not reach the scheduler at all.

```csharp
ValueTask TriggerInError(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;
ValueTask TriggersInError(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default) => default;
```

Following the singular/plural pair `ISchedulerListener` already has for pause and resume. Both are
default-implemented, as are the matching `ISchedulerSignaler.NotifySchedulerListenersTriggerInError` and
`NotifySchedulerListenersTriggersInError`, so an existing listener or signaler still compiles and behaves
exactly as it does today — no notification.

The plural is keyed by `JobKey` rather than by the individual triggers because `SetAllJobTriggersError` is one
bulk statement in the persistent store, and enumerating the affected keys would mean an extra query on a failure
path. Ask `GetTriggersOfJob` where the keys themselves matter.

Neither carries a cause. The stores raise these and receive only a `SchedulerInstruction`; `SchedulerError` says
*why* and these say *what changed*, and where both apply they arrive together. Recover a trigger with
`IScheduler.ResetTriggerFromErrorState`.

`JobChainingJobListener` is `sealed` — see
[The shipped implementations are sealed](#the-shipped-implementations-are-sealed).

An `IJobStore` that implements `IJobListener` no longer automatically receives all events. Register it explicitly as a job listener using `ListenerManager`:

```csharp
scheduler.ListenerManager.AddJobListener(myJobStoreListener);
```

## Scheduler Configuration Validation

* `IdleWaitTime` values less than or equal to zero are no longer silently replaced with a 30-second default — they now throw.
* Negative values for `IdleWaitTime` or `BatchTimeWindow` are rejected.
* `MaxBatchSize` values less than or equal to zero are rejected.
* `MaxBatchSize` may not exceed `ThreadPoolOptions.MaxConcurrency`. The two are configured through different builder methods and different configuration sections, so the pair is only ever wrong by accident — and triggers acquired beyond the number of threads available to run them are held by this node, unfireable by any other, until the pool drains. See [Batching trigger acquisition](tutorial/advanced-enterprise-features.md#batching-trigger-acquisition).

### Validation happens at startup, through one mechanism

Options validation used to fire when the options were first resolved — which is inside a scheduler
factory, so the stack trace pointed at Quartz internals rather than at the registration line — and only
if something resolved them at all. Every Quartz options type is now registered with `ValidateOnStart`,
so a bad value fails `Host.Build()` with every failure listed.

Which types are checked follows what the scheduler actually reads. `QuartzSchedulerOptions` and
`ThreadPoolOptions` always; `InMemoryJobStoreOptions` when `UseInMemoryStore` was called;
`AdoJobStoreOptions` and `ClusteringOptions` when `UsePersistentStore` was; `DataSourceOptions` when a
data source was configured. Validating the ADO options for an in-memory scheduler would have turned an
unset `DataSource` into a startup failure for a configuration nobody wrote.

The two satellite packages that validated differently now use `IValidateOptions<T>` like everything
else, so one exception type — `OptionsValidationException` — reports every configuration mistake:

| Before | After |
|---|---|
| `AddOptions<QuartzHttpApiOptions>().Validate(lambda, message)` | `QuartzHttpApiOptionsValidator`, with `ValidateOnStart()` |
| `HttpClientOptions.AssertValid()` throwing `InvalidOperationException` | `HttpClientOptionsValidator`, throwing `OptionsValidationException` from `AddQuartzHttpClient` |

`ValidateOnStart` is inert in the container `QuartzSchedulerBuilder` builds, because nothing there plays
the host's part of resolving `IStartupValidator`. `Build()` passes `ValidateOnBuild`, which checks the
object graph rather than the values, so a bad value there still surfaces when the component reading it
is built.

### `UseClustering(c => c.Enabled = false)` is now an error

It used to be undefined: the builder turned clustering on, the callback turned it off, and the store was
left with database locking on, no cluster manager and no check-in row — with nothing said. It fails
validation now. A scheduler that should not cluster does not call `UseClustering`.

## Cron Parser Enhancements

The cron expression parser now supports additional syntax:

* `L` and `LW` combinations in day-of-month expressions (e.g., `LW` for last weekday of the month)
* `LW-<OFFSET>` for offset from the last weekday (e.g., `LW-2` for two days before the last weekday). If the calculated day crosses a month boundary, it resets to the 1st.
* Day-of-month and day-of-week can be specified together. A field written `*` or `?` **restricts
  nothing**, so the other field decides: `0 15 10 1 * *` is the 1st of the month, and
  `0 15 10 * * MON` is every Monday. When **both** fields name days, the expression fires on the union
  of the two — `0 15 10 1,2,3 * MON,FRI` is the 1st, 2nd and 3rd **and** every Monday and Friday.
  This is the Unix `crontab(5)` rule. It is deliberately **not** Cronos's, which ANDs even when both
  fields are restricted; the union is what Quartz has always documented and what 4.0 alpha shipped.
  `?` and `*` are now full synonyms in a day field, so `? * ?` is legal and means every day.
  **Changed since alpha.4:** an expression with a wildcard in one day field and values in the other
  fired on every day of the month; it now fires only on the days the restricted field names.
* `H` (hash) tokens for [load distribution](cron-expressions.md#h-hash-for-load-distribution) across triggers

Parse errors name the fix instead of only the constraint. A 5-field Unix/crontab expression — the shape every
online cron generator emits — is not Quartz's own form (Quartz cron puts seconds first), so reading one *as
Quartz* is still an error; but the error now shows the corrected 6-field expression to use, names
`CronFormat.Unix` as the way to read it as written instead, and the day-of-week range error explains that
Quartz numbers days 1-7 starting at Sunday where Unix cron uses 0-6, recommending names (`SUN`, `MON`, …) as
the unambiguous spelling. When the 5-field expression's day-of-week is a bare number the error goes one step
further and shows the renumbered expression too: prepending a seconds field to `30 4 * * 1` keeps the `1`, and
`1` is Monday in crontab but Sunday in Quartz, so the message names `"0 30 4 ? * MON"` as the schedule that was
meant.

A day name in the **month** field gets the same advice, because that is what a pasted crontab line looks like
from the parser's side: `0 12 * * MON` never reaches the field count, since crontab's fifth field is
day-of-week and Quartz's fifth is month, so `MON` is rejected as a month name first.

### A cron expression can be written in the Unix five-field form

`CronFormat.Quartz` is the default and nothing changes without asking for it. `CronFormat.Unix` reads the
five-field crontab form, and there are exactly three doors:

```csharp
CronExpression.Parse("30 4 * * 1", CronFormat.Unix);
CronExpression.TryParse(candidate, CronFormat.Unix, out CronExpression? expression);
CronScheduleBuilder.Create("30 4 * * 1", CronFormat.Unix);
```

`WithCronSchedule` has no format overload; compose one —
`WithCronSchedule(CronExpression.Parse(s, CronFormat.Unix))`. A time zone composes the same way:
`CronExpression.Parse(s, CronFormat.Unix).WithTimeZone(tz)`.

The format decides two things and nothing else. **The field layout**: five fields, minutes first, with no
seconds and no year. **The day-of-week numbering**: 0-7, with both 0 and 7 meaning Sunday, so `1-5` is Monday
to Friday as it is in crontab. Everything else is one grammar — `L`, `W`, `#` and `H` all work inside the
five-field layout, and `L` alone in day-of-week still means Saturday, because it is not a number and so has
nothing to renumber. When both day fields name days the expression fires on the union, which is
`crontab(5)`'s rule and the same one Quartz applies to its own expressions.

**The expression is normalised to the canonical Quartz form, and the original is not recoverable.**
`CronExpression.Parse("30 4 * * 1", CronFormat.Unix).CronExpressionString` is `"0 30 4 ? * MON"`, and that is
what `QRTZ_CRON_TRIGGERS.CRON_EXPRESSION`, the dashboard and the HTTP API all show. That is deliberate: a
stored string that parses only under a flag the store does not persist would be a trap, so there is no
`CRON_FORMAT` column and there will not be one. It is the same trade as the uppercasing that has always
turned `mon-fri` into `MON-FRI`.

| crontab | stored, and shown, as |
|---|---|
| `30 4 * * 1` | `0 30 4 ? * MON` |
| `0 12 1 * *` | `0 0 12 1 * ?` |
| `* * * * *` | `0 * * * * ?` |
| `0 0 13 * 5` | `0 0 0 13 * FRI` — the 13th **and** every Friday, by the union rule |
| `0 0 * * 0-6` | `0 0 0 * * ?` |
| `0 0 * * 5-1` | `0 0 0 ? * FRI-MON` |
| `0 0 * * 1/2` | `0 0 0 ? * 2/2` — numeric, because `MON/2` is [rejected](#the-parser-refuses-what-it-used-to-ignore) |

Because the format is an argument to the parse, **the XML schema, the HTTP API and the dashboard do not take
one**: a five-field expression is something you write in C#, not something you store. The `@` macros below
have no such limit.

### `@daily` and the rest of the macros work everywhere

The `@` macros need no format, because they name the same schedule in every cron there is. They are expanded
by the `CronExpression` constructor, which is what every entry point goes through, so they are read wherever
an expression string is — `<cron-expression>@daily</cron-expression>` in an XML scheduling file, the
dashboard's expression box and the HTTP API included.

| macro | expands to |
|---|---|
| `@yearly`, `@annually` | `0 0 0 1 1 ?` |
| `@monthly` | `0 0 0 1 * ?` |
| `@weekly` | `0 0 0 ? * SUN` |
| `@daily`, `@midnight` | `0 0 0 * * ?` |
| `@hourly` | `0 0 * * * ?` |

This is Vixie's set and only Vixie's set: there is no `@every_minute` or `@every_second`, and no jittered
variant, because `0 * * * * ?` is already short and `H` spreads load deterministically. `@reboot` is rejected
by name — a scheduler has no reboot to fire on — and any other `@name` is rejected with the supported list.
The expansion is what gets stored, so a trigger written with `@daily` shows `0 0 0 * * ?`.

### The parser refuses what it used to ignore

A handful of expressions parsed, and then meant something other than what they said. Each is now a
`FormatException` whose message names the expression that says what the author meant.

| Expression | 3.x / 4.0 alpha | 4.0 |
|---|---|---|
| `1-5W` | the `W` was dropped; meant `1-5` | `FormatException` — write `1W,2W,3W,4W,5W` or drop the `W` |
| `? * L-3`, `? * LW` | the suffix was dropped; meant Saturday | `FormatException` — those forms belong to day-of-month. A bare `L` in day-of-week is still Saturday |
| `MON,FRI#3` | fired on the third **Monday**; the Friday was ignored | `FormatException` — `#` cannot appear beside other days |
| `5C`, `1C` | parsed, meant `5` / `1`; `C` was never implemented | `FormatException` — use `ModifiedByCalendar` |
| `*/0`, `5/0`, `0-10/0` | a step of 0 degenerated to no step | `FormatException` |
| `0-10/120` | the step was never range-checked | `FormatException` — `Increment > 59 : 120`, as `0/120` already did |
| `MON/2` | every second week — a fortnight with **no stable phase** | `FormatException` — `MON,WED,FRI` for a step through the week, or `RecurrenceScheduleBuilder.Create("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")` for every second Monday |

`MON/2` is the one that changes a schedule rather than only a spelling, so it is worth the paragraph. A
textual day-of-week followed by `/` meant "every N weeks", and the numeric `2/2` beside it meant an ordinary
step — two readings of the same grammar. The fortnight also had no stable phase: it counted whole weeks from
wherever the search started, so a misfire, a restart, a failover or a dashboard query recomputed it from a
different day and moved it. It is rejected rather than quietly re-read as a step, because re-reading it would
turn a fortnightly job into a thrice-weekly one — 26 fires a year become 156 — with nothing logged. Every
second Monday is `RecurrenceScheduleBuilder.Create("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")`, which anchors the
interval on the trigger's start time and so keeps its phase.

### Before you upgrade

Every row in that table is a string 3.x stored happily. The exception is thrown from
`CronTriggerImpl.CronExpressionString`'s setter, which the ADO.NET job store calls while materialising a row,
so a surviving expression fails the *read* rather than only the trigger. Audit the stored expressions first:

```sql
-- run against every scheduler's tables before upgrading; substitute your table prefix
SELECT SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, CRON_EXPRESSION
FROM   QRTZ_CRON_TRIGGERS
WHERE  CRON_EXPRESSION LIKE '%C%'    -- 'C' (calendar), never implemented
   OR  CRON_EXPRESSION LIKE '%/0%'   -- step of zero
   OR  CRON_EXPRESSION LIKE '%#%,%'  -- '#' beside other days
   OR  CRON_EXPRESSION LIKE '%,%#%'
   OR  CRON_EXPRESSION LIKE '%/%'    -- then eyeball for a textual day-of-week step
   OR  CRON_EXPRESSION LIKE '%W%';   -- then eyeball for 'W' after a range
```

Several of those clauses are deliberately over-wide and need reading by eye: most `/` is an ordinary step, most
`W` is a legitimate `15W` or `LW`, and `%C%` also matches the month names `DEC` and `OCT`, because expressions
are stored upper-cased. `CronExpression.TryParse` against the 4.0 assembly is the authoritative check once you
have a candidate list.

Two sources are **not** reachable from this query. `CronCalendar` expressions live inside the serialized blob
in `QRTZ_CALENDARS` and surface only at deserialization, and expressions built in code or held in
configuration files never reach the database at all.

## Daylight saving time

Three schedules fire at different times than they did on 3.x. None needs a code change, but all three change
*when* existing triggers run, so review any schedule that crosses a transition.

**Interval cron expressions fire through both halves of a fall-back hour.** A cron expression whose second,
minute or hour field uses a wildcard, a step or a range — `0 * * * * ?`, `0 0/30 * * * ?` — now fires through
**both** occurrences of the wall-clock window that repeats when the clock goes back. Previously the repeated
window fired only once, so an "every minute" schedule silently skipped an hour of real time. Over a fall-back
hour, fixed-time expressions such as `0 30 2 * * ?` — including comma lists like `0 0,30 2 * * ?` — are
unchanged: they still fire once per day, at the first occurrence of an ambiguous wall-clock time.

**A cron time that does not exist fires when the gap ends, not shifted by the transition delta.** A daily
`0 30 2 * * ?` over a 02:00–03:00 spring-forward gap now fires at **03:00**, the instant the clocks moved,
where 3.x and 4.0 alpha fired at 03:30. In a zone whose daylight delta is not a whole hour —
Australia/Lord_Howe — the fire reads 02:30 rather than 02:45. Every wall clock the gap swallowed names that
one instant, so an expression matching several of them still fires once: `0 0,30 2 * * ?` fires once, as it
did before. What *can* change a fire count is a match between the gap's end and where the delta shift used to
land, which the search now reaches instead of resuming past it. An hourly `0 30 * * * ?` is the common shape:
it fires at 03:00 for the occurrence the gap swallowed and again at 03:30 for the next hour's, where the delta
shift moved the first onto 03:30 and the two collided into a single fire. `IsSatisfiedBy` now agrees with the
fire time, which it did not before: a spring-forward gap takes no real time, so its start and its end are the
same instant, and that makes the gap's end the one in-gap answer a search starting a second earlier can
reproduce. A trigger therefore no longer fires at an instant its own expression rejects. For the same reason a
`CronCalendar` written over the skipped hour now excludes that instant, where it used to exclude nothing at
all on the transition day. Fall-back behaviour is untouched.

**`CalendarIntervalTrigger` with `PreserveHourOfDayAcrossDaylightSavings` steps in local wall-clock time.**
Fire times are now always exactly on schedule and strictly increasing; the previous implementation could return
times the schedule never specified when its daylight-saving adjustments failed to make progress. In time zones
whose daylight delta is not a whole hour — Australia/Lord_Howe — the scheduled local time no longer drifts by
the sub-hour part of the delta across a transition, so the flag preserves the full time of day as its
documentation always promised.

## New Features

Everything 4.x can do that a 3.x application probably is not doing yet. A few of these were backported
to a late 3.x release after they were written here, so if you are upgrading from the newest 3.x you may
already have them.

* **[RecurrenceTrigger (RRULE)](tutorial/recurrencetrigger.md)** — schedule jobs using RFC 5545 recurrence rules for complex patterns like "every 2nd Monday of the month" or "last weekday of March each year"
* **H (hash) token in cron expressions** — deterministic load distribution across triggers using the trigger identity as seed
* **HTTP API** — optional REST API for managing the scheduler remotely (see [HTTP API](packages/http-api.md))
* **`Quartz.HttpClient`** — the other end of that API: `HttpScheduler` is an `IScheduler` that speaks to a remote scheduler over HTTP, which is what replaces .NET Remoting (see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern))
* **Paged, projected job store queries** — list and count jobs, triggers, groups and calendars a page at a time, with the metadata a listing needs already in the row (see [Job store listings became queries](#job-store-listings-became-queries))
* **Bulk fetch by key** — `GetJobDetails(keys)` and `GetTriggers(keys)` turn a page of keys into one round trip, over ADO.NET and over HTTP alike (see [Job store listings became queries](#job-store-listings-became-queries))
* **Fire instances are a listing** — `QueryFireInstances` answers what is running as a paged, projected query that a persistent store answers for the whole cluster, where `GetCurrentlyExecutingJobs` could only speak for this process (see [What is running is a listing, not a list of contexts](#what-is-running-is-a-listing-not-a-list-of-contexts))
* **The nodes of a cluster can be listed** — `QueryClusterNodes` reports every node the store knows about, with the same `Alive`/`Overdue`/`Failed` verdict the failover sweep decides recovery by. On 3.x nothing in the product read `QRTZ_SCHEDULER_STATE`, so "which of my four nodes is alive" was a question answered by hand-written SQL (see [The nodes of a cluster are a listing](#the-nodes-of-a-cluster-are-a-listing))
* **Job data by property name** — bind job data to the job property it is meant for instead of spelling its key (see [Job data can name the property](#job-data-can-name-the-property))
* **A trigger can carry a retry policy** — `WithRetryPolicy(RetryPolicy.Exponential(3, TimeSpan.FromSeconds(30)))` re-fires a trigger whose job threw, on a fixed, exponential or explicitly tabulated schedule of waits, persisted and survivable across a restart or a failover. 3.x had nothing: a failed job either asked for `RefireImmediately` — an in-process loop with no delay and no ceiling — or scheduled a fresh trigger for itself from inside `Execute` (see [A trigger can carry a retry policy](#a-trigger-can-carry-a-retry-policy) and [Retrying Failed Jobs](how-tos/retrying-failed-jobs.md))
* **`TriggerState.Executing`** — tell whether a trigger's job is running, across the whole cluster (see [Executing is a trigger state](#executing-is-a-trigger-state))
* **`JobInstantiationException`** — a job that could not be built names the trigger, the job and the fire instance instead of only interpolating the job key into a message (see [Instantiation failures name the trigger](#instantiation-failures-name-the-trigger))
* **`ISchedulerListener.TriggerInError` / `TriggersInError`** — observe a trigger being moved to `TriggerState.Error`, including two ADO store transitions that reached nothing at all before (see [Triggers entering the error state are reported](#triggers-entering-the-error-state-are-reported))
* **Every listener callback names its scheduler** — one listener can serve several schedulers in one host and still say which of them paused a trigger or failed, and `SchedulerError` carries the trigger, job and firing it was raised for. A listener still carrying a signature from 3.x, from alpha.1, or from before `TriggerMisfired` led with the trigger is refused when it is registered, with a message naming the member, rather than being attached and never called (see [Listeners are told which scheduler is calling](#listeners-are-told-which-scheduler-is-calling))
* **Joining a transaction the application owns** — the ADO job store can take part in a transaction you started, so saving your own data and scheduling the job that acts on it commit together or not at all. Turn it on with `store.ConfigureStore(o => o.AcceptEnlistedTransactions = true)`, `JobStore:AcceptEnlistedTransactions`, or `quartz.jobStore.acceptEnlistedTransactions`, then hand the store a connection for the duration of a scope with `IScheduler.EnlistTransaction` / `EnlistConnection`. Handing over a connection is the only way to take part: a connection the job store opens for itself is deliberately kept out of any ambient `TransactionScope`, since a second connection in that transaction would require promoting it to a distributed one. See [Joining an existing transaction](tutorial/job-stores.md#joining-an-existing-transaction)
* **Job execution middleware** — `IJobExecutionMiddleware` wraps every firing a scheduler performs, which is where a log scope, a tenant context, a metric or a translation of a library's exceptions belongs. A listener could never do it: it is notified before and after the execution rather than around it. Registered with `AddJobMiddleware<T>()` and its factory and instance overloads (see [Cross-cutting concerns run as middleware](#cross-cutting-concerns-run-as-middleware))
* **Builder methods for three more plugins** — `UseJobHistoryLogging()`, `UseTriggerHistoryLogging()` and `UseShutdownHook()`. Only the structured-logging variants had one, so the classic history plugins and the shutdown hook could previously be reached only through `quartz.plugin.*` property keys
* **A plugin implements only what it has to say** — `ISchedulerPlugin.Start` and `Shutdown` have default implementations, so a plugin that does its work in `Initialize` writes one member rather than three (see [A plugin implements only what it has to say](#a-plugin-implements-only-what-it-has-to-say))
* **A calendar can have dependencies** — `AddCalendar(name, serviceProvider => …)` builds the calendar from the container, which the `where T : ICalendar, new()` generic overloads cannot (see [A calendar can be built from the container](#a-calendar-can-be-built-from-the-container))
* **A renamed job type is declared, not coded around** — `q.UseTypeLoader(loader => loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob)))`, or the same map under `Quartz:TypeLoader:Aliases` in configuration, keeps every row still carrying the old `JOB_CLASS_NAME` firing. On 3.x the only answer was an `ITypeLoadHelper` of your own (see [A job type rename is declared as a map](#a-job-type-rename-is-declared-as-a-map))
* **A `quartz.*` key that lost its prefix is refused** — `QuartzOptions.Properties` is read only through the `quartz.` prefix, so a key without one produced no symptom at all; it now fails options validation at startup (see [A property key without the `quartz.` prefix is refused](#a-property-key-without-the-quartz-prefix-is-refused))
* **An `IJobDetail` of your own** — the interface no longer declares a member only Quartz can implement, so an application can supply its own job detail type and have `RAMJobStore` hand it back rather than quietly swapping it for Quartz's (see [An `IJobDetail` of your own](#an-ijobdetail-of-your-own))
* **A one-word question needs no query object** — `QueryJobs()`, `QueryTriggers()`, `QueryFireInstances()`, `QueryFireInstancesOfJob(jobKey)` and `QueryTriggersInError()` are the query members with the query record filled in, `ResetTriggersFromErrorState(GroupMatcher<TriggerKey>)` resets the failed triggers of a group, `Exists(string calendarName)` answers whether a calendar is there without deserializing it, and `ISchedulerFactory.GetRequiredScheduler(name)` throws where `LookupScheduler` returns null (see [Asking a one-word question does not need a query object](#asking-a-one-word-question-does-not-need-a-query-object))
* **Pause, resume and reset a set of keys in one call** — `PauseTriggers`, `ResumeTriggers`, `PauseJobs`, `ResumeJobs` and `ResetTriggersFromErrorState` take a key collection, do the whole set in one lock and one transaction, and answer with the keys they applied to (see [A set of keys pauses, resumes or resets in one call](#a-set-of-keys-pauses-resumes-or-resets-in-one-call))
* **`TriggerDetailsUpdate.WithExecutionGroup`** — move a stored trigger into an execution group, or out of every group, without rescheduling it. `QRTZ_TRIGGERS.EXECUTION_GROUP` was already written by the generic trigger update, and `RAMJobStore` applies it in place the same way it applies a preferred node, so both stores behave alike
* **A chain can fan out** — `JobChainingJobListener` takes more than one follow-up for a job, either by calling `AddJobChainLink` again with the same first job or with the new `AddJobChainLinks(firstJob, followUpJobs)`. Each follow-up is triggered as its own firing, so they run concurrently rather than one after another, and one that cannot be triggered is logged without costing its siblings theirs. On 3.x the second link threw, so a chain could only be sequential (see [How do I chain Job execution?](../faq.md#how-do-i-chain-job-execution-or-how-do-i-create-a-workflow))

## Job data can name the property

`UsingJobData` has an overload that takes the job property rather than its key:

```diff
  q.AddJob<ExampleJob>(jobKey, j => j
-     .UsingJobData(nameof(ExampleJob.InjectedString), "Hello")
-     .UsingJobData(nameof(ExampleJob.InjectedBool), true)
+     .UsingJobData(x => x.InjectedString, "Hello")
+     .UsingJobData(x => x.InjectedBool, true)
  );
```

The key is the property's name and the value has to be of the property's type, so a value that would have
been silently coerced — an `int` written to a `string` property, say — no longer compiles, and neither does
a property of an unrelated job.

Everything the compiler cannot rule out is rejected where the job data is written, by asking the job
factory's own lookup whether the key this property's name becomes leads back to this same property:

* a property with no public setter, or a nested path;
* one reached by casting the lambda parameter to another job;
* one the factory cannot find — a name starting with a lowercase letter (keys are looked up upper-cased),
  or a property that implements an interface explicitly, which is not public on the job class;
* one whose name resolves to a *different* property of another type, which is what a `new` member that
  hides a base property does;
* a value that will not convert to the property's type, or that would lose information doing so — a
  `double` rounded into an `int`, or saturated into a `float`;
* `null` for a property that cannot hold one. Type inference widens `TValue` to the nullable form, so
  `int? retries = …; UsingJobData(j => j.RetryCount, retries)` compiles and is rejected here rather than
  quietly becoming `0` when the job runs.

Enums are stored by name. Nothing is instantiated: the expression is read, never run.

Existing string-keyed `UsingJobData` calls are unaffected.

### The builders carry the job type

Inferring `x` needs the builder to know the job type, so the builders and the configurator interfaces are
generic in it. `JobBuilder` and `TriggerBuilder` are now static classes holding the `Create` methods, and
the builder itself is `JobBuilder<TJob>` / `TriggerBuilder<TJob>`:

| 4.0 preview | 4.0 |
|---|---|
| `JobBuilder` | `JobBuilder<TJob>`, from `JobBuilder.Create<TJob>()` — `JobBuilder.Create()` gives `JobBuilder<IJob>` |
| `TriggerBuilder` | `TriggerBuilder<TJob>`, from `TriggerBuilder.Create<TJob>()` — `TriggerBuilder.Create()` gives `TriggerBuilder<IJob>` |
| `IJobConfigurator` | `IJobConfigurator<TJob>` |
| `ITriggerConfigurator` | `ITriggerConfigurator<TJob>` — the 4.x `ITriggerConfigurator` is a new, much smaller base holding only `WithSchedule`, see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `IJobDetail.GetJobBuilder()` | an extension method returning `JobBuilder<IJob>` — see [An `IJobDetail` of your own](#an-ijobdetail-of-your-own) |
| `ITrigger.GetTriggerBuilder()` | returns `TriggerBuilder<IJob>` |

Chained code is unaffected — `JobBuilder.Create<MyJob>().WithIdentity("x").Build()` reads the same, and so
do the `AddJob<T>` / `ScheduleJob<T>` lambdas, whose parameter type is now inferred as the generic
configurator. What breaks is naming the builder as a type:

```diff
- TriggerBuilder builder = trigger.GetTriggerBuilder();
+ var builder = trigger.GetTriggerBuilder();
```

The configurator interfaces are **invariant** in `TJob`, so a configuration delegate shared across job
types no longer type-checks. `TJob` appears both as an input (`Expression<Func<TJob, TValue>>`) and in the
returned interface, so no variance annotation can recover it — make the helper generic instead:

```diff
- Action<ITriggerConfigurator> common = t => t.StartNow().WithSimpleSchedule();
- q.ScheduleJob<JobA>(common);
- q.ScheduleJob<JobB>(common);
+ static void Common<TJob>(ITriggerConfigurator<TJob> t) where TJob : IJob => t.StartNow().WithSimpleSchedule();
+ q.ScheduleJob<JobA>(Common);
+ q.ScheduleJob<JobB>(Common);
```

`AddTrigger` gained an `AddTrigger<TJob>` overload, because a trigger added on its own has no job type to
infer from otherwise. The internal `TriggerConfigurator` is gone: `TriggerBuilder<TJob>` implements
`ITriggerConfigurator<TJob>` itself, which also gets `WithExecutionGroup` and `WithPreferredNode` onto the
DI configurator for the first time.

That deletion also changed which clock a DI-registered trigger is born with. `TriggerConfigurator` always
built with `TimeProvider.System`; `AddTrigger` and `ScheduleJob` now use the container's registered
`TimeProvider`. If you register a non-system one — a `FakeTimeProvider` in a test, say — a trigger without
an explicit `StartAt` now starts at that provider's now rather than at wall clock, which is almost
certainly what you wanted, but it does change when such triggers first fire.

Three runtime checks come with the type parameter, all only on a builder whose `TJob` is not `IJob`:

* `JobBuilder.Create<TJob>().OfType(type)` and `OfType<T>()` throw `ArgumentException` **at the `OfType`
  call** when the type is not a `TJob`. If you resolve a job type at runtime — from configuration, or a
  decorator type — build it with `JobBuilder.Create().OfType(type)` rather than the generic overload.
* `JobBuilder.Create<TJob>().OfType(typeName)` throws `InvalidOperationException` on `Build()` instead,
  because a type named by string is only known once it resolves.
* `TriggerBuilder.Create<TJob>().ForJob(jobDetail)` throws `ArgumentException` when the detail is not for a
  `TJob`. `ForJob(JobKey)` carries no type, and a detail whose type name does not resolve in this process
  cannot be checked either — both are accepted.

## An `IJobDetail` of your own

`IJobDetail` declared `GetJobBuilder()`, which nobody outside Quartz could implement.
`JobBuilder<TJob>` is sealed with an internal constructor and builds Quartz's own detail, so an
implementation of the interface had to return a builder that produces somebody else's type
([#1143](https://github.com/quartznet/quartznet/issues/1143)). That was not only awkward to write
around: `RAMJobStore` called the member to re-store the job data of a
`[PersistJobDataAfterExecution]` job, so the first completion of such a job silently replaced the
caller's detail with Quartz's.

The unimplementable member is gone from the interface, and the one a job store actually needs
replaces it:

| 4.0 preview | 4.0 |
|---|---|
| `IJobDetail.GetJobBuilder()` | `JobDetailExtensions.GetJobBuilder(this IJobDetail)`, in the `Quartz` namespace |
| — | `IJobDetail.WithJobData(JobDataMap)` — a copy of the detail carrying the given data |

**Calling code changes nothing.** `detail.GetJobBuilder()` still compiles, still resolves without a
new `using`, and still returns `JobBuilder<IJob>`; it is now filled in from the detail's public state
rather than by the detail itself. It carries the detail's `JobType` across as it is, so a detail read
from a job store whose stored type name names nothing in this process rebuilds, and keeps the stored
spelling, rather than throwing.

**An implementation writes `WithJobData` instead of `GetJobBuilder`.** It returns a copy of the
detail carrying the given map, leaving the instance it was called on alone; the map is taken as
given, not copied.

```diff
- public JobBuilder<IJob> GetJobBuilder() => /* nothing you can write */;
+ public IJobDetail WithJobData(JobDataMap jobDataMap) => new MyJobDetail(Key, JobType, …, jobDataMap);
```

How far your own detail travels depends on what holds it:

* `RAMJobStore` keeps the instances it is given and hands back `Clone()`s of them, so a detail of
  your own round-trips as itself — including across the re-store of a `[PersistJobDataAfterExecution]`
  job, which is what `WithJobData` is for.
* Anything that keeps a detail as data does not. The ADO.NET job store writes the columns of
  `QRTZ_JOB_DETAILS` and rebuilds every detail it reads through `JobBuilder`, so what comes back is
  Quartz's own implementation; `HttpScheduler` rebuilds one the same way from its wire payload.
  Whatever your type carries beyond the interface's members is gone by then, so anything that has to
  survive a persistent store belongs in the `JobDataMap`.

## A trigger can carry a retry policy

3.x had no retry policy. A job that failed either asked for `RefireImmediately` — an in-process loop
with no delay and no ceiling — or scheduled a fresh trigger for itself from inside `Execute`. 4.x adds
`Quartz.RetryPolicy`, a value describing how many times and how far apart a failed job's trigger is
re-fired.

| Member | What it is |
|---|---|
| `RetryPolicy.Fixed(maxAttempts, delay)` | The same wait before every retry |
| `RetryPolicy.Exponential(maxAttempts, initialDelay, factor = 2, maxDelay = null)` | A wait multiplied by `factor` each time, optionally clamped |
| `RetryPolicy.Explicit(params delays)` | A table of waits; `MaxAttempts` is its length and the last entry repeats |
| `RetryPolicy.DelayFor(attempt)` | The wait before the given retry, counting from one |
| `RetryPolicy.ToStoredString()` / `Parse` / `TryParse` | The single-string form the `RETRY_POLICY` column, the JSON payload and a serialized trigger are to carry |

There is no public constructor, so a policy that could not be honoured — no attempts, a negative wait,
a backoff that shrinks — cannot be built. A policy carries which of the three factories made it, so
`Exponential(3, delay, factor: 1)` waits exactly as `Fixed(3, delay)` does but is a different value and
stores as `exp;3;…;1`: the shape says what the trigger's author chose, and turning the rate up is a
change to make on one and not on the other. Equality is written out by hand rather than left to a
record, because the delay table is a collection and reference equality on it would make two policies
built from the same numbers different values; the backoff factor is compared bit for bit, which is what
round-tripping it through the `"R"` format guarantees.

`MaxAttempts` counts retries *after* the first failure, not fires: `Fixed(2, …)` runs a persistently
failing job three times in total.

### Where a policy is set and read

| Member | What it is |
|---|---|
| `ITrigger.RetryPolicy` | The trigger's policy, or `null` — the default — when it does not retry |
| `ITrigger.RetryAttempt` | How many times the occurrence being executed has already been retried; `0` on a regular fire |
| `IMutableTrigger.RetryPolicy` / `RetryAttempt` | The setters, for a job store restoring a trigger from its row |
| `TriggerBuilder<TJob>.WithRetryPolicy(RetryPolicy?)` | Give a trigger a policy while building it |
| `ITriggerConfigurator<TJob>.WithRetryPolicy(RetryPolicy?)` | The same, in the DI configuration API |
| `TriggerDetailsUpdate.WithRetryPolicy(RetryPolicy?)` | Change a stored trigger's policy without rescheduling it |
| `TriggerHeader.RetryPolicy` (string) / `RetryAttempt` | What a trigger listing reports |

A builder-built trigger fully defines its retry policy, so a definition that says nothing clears a
stored one when it replaces an existing trigger — the same rule `WithExecutionGroup` and
`WithPreferredNode` follow. `TriggerDetailsUpdate` deliberately has no way to set the *attempt*: the
policy is configuration, while the attempt belongs to the occurrence in flight, and setting it would
either grant a running job extra attempts or take away ones it has already spent.

`TriggerHeader` carries the policy as its stored string rather than as a `RetryPolicy`, because a
listing reports the column: a row whose policy a newer node wrote in a shape this one cannot read still
has to list. That makes `TriggerHeader`'s constructor two parameters longer, which is a breaking change
for anything constructing one by hand.

### Where a policy is stored

The policy travels as its stored string everywhere: the `RETRY_POLICY` column, the `retryPolicy`
property of a serialized trigger, and the `retryPolicy` field of a JSON scheduling file or
configuration section. A payload written before 4.0 has neither property, and reads back as no policy
and no attempt.

```json
{
  "Name": "nightly-import",
  "JobName": "import",
  "RetryPolicy": "exp;3;00:00:30;2;00:10:00",
  "Cron": { "Expression": "0 0 2 * * ?" }
}
```

A string that is not a policy is refused where the file is read, rather than scheduling a trigger that
would never retry.

### What happens when a job fails

A job fails, for retry purposes, when `Execute` throws anything other than a `JobExecutionException`
that asks for something itself. On failure with attempts left the scheduler sets the trigger's next
fire time to `now + policy.DelayFor(attempt + 1)`, increments the attempt and hands the job store a new
instruction, `SchedulerInstruction.RetryTrigger`. Everything else about the trigger is untouched.

| Member | What it is |
|---|---|
| `SchedulerInstruction.RetryTrigger` | New enum member, appended — no existing value moved. `ITriggerListener.TriggerComplete` sees it; there is no new listener member |
| `IJobExecutionContext.RetryAttempt` | `0` on a regular fire, *n* on the *n*-th retry |
| `TriggerBase.RetryFired(ICalendar?)` | `public virtual`, and only on `TriggerBase` — `IOperableTrigger` is unchanged |
| `IDriverDelegate.UpdateTriggerForRetry` / `ClearTriggerRetryAttempt` | **Breaking for anything implementing `IDriverDelegate`**; `StdAdoDelegate` implements both `virtual` |

Four rules are worth knowing:

* **A retry never displaces the next scheduled occurrence.** If `retryAt + 1s >= NextFireTimeUtc` the
  retry is dropped and the occurrence supersedes it, attempt reset. The one-second margin exists because
  `CalendarIntervalTriggerImpl.GetFireTimeAfter` and `DailyTimeIntervalTriggerImpl.GetFireTimeAfter` add
  a second before searching. A retry is also never scheduled past the trigger's `EndTimeUtc`.
* **A retry fire is not a `Triggered()` call.** `RetryFired` advances past the retry instant with the
  same calendar-skip loop, but touches no counter and leaves `PreviousFireTimeUtc` alone — so a retry
  burns no repeat count and no RRULE `COUNT` slot, and reports the *original* occurrence as its
  `ScheduledFireTimeUtc`.
* **Exhausted attempts go back to the ordinary schedule, not to `TriggerState.Error`.** One bad hour
  must not kill a cron trigger. A misfired retry gets the trigger's own misfire instruction and the
  attempt is cleared; there is no retry-specific misfire policy.
* **`RefireImmediately` is not a zero-delay retry.** It re-runs the job on the same thread inside the
  same firing, with no delay, no ceiling, nothing persisted and no slot released, and it does not touch
  `RetryAttempt`. An explicit `RefireImmediately` or `Unschedule*` wins over the trigger's policy. An
  `OperationCanceledException` from the scheduler's own token never retries — shutdown and interrupt are
  operator decisions, and a node vanishing mid-execution is what `RequestsRecovery` is for.

One meter, `quartz.trigger.retry`, counts each retry the scheduler schedules, tagged like
`quartz.trigger.misfire`. Log event `1056` reports the trigger, the attempt and the retry instant at
`Information`.

### If you implement `IDriverDelegate`: the retry members

Two new members, neither with a default implementation — the same way
`UpdateTriggerPreferredNodeConditional` and the listing members were added. A delegate deriving from
`StdAdoDelegate` inherits both as `virtual` and needs no change; one implementing the interface from
scratch has to supply them.

| Member | Purpose |
|--------|---------|
| `UpdateTriggerForRetry(conn, trigger, newState, ct)` | Write the retry a completing trigger scheduled: `NEXT_FIRE_TIME`, `RETRY_ATTEMPT` and `TRIGGER_STATE` in one narrow statement |
| `ClearTriggerRetryAttempt(conn, triggerKey, ct)` | Put `RETRY_ATTEMPT` back to zero and touch nothing else |

The pair is deliberately narrow rather than folded into `UpdateTrigger`: a retry moves one instant, and
the generic trigger update would write back a preferred node and a job data map that this path has no
business touching. Clearing is issued only when the completing trigger actually carried a non-zero
attempt, so an ordinary completion still costs no extra round trip.

`UpdateMisfiredTrigger` and `UpdateMisfiredTriggers` did not change shape, but the statements behind
them now also write `RETRY_ATTEMPT`: misfire handling recomputes a trigger from its schedule, so the
occurrence that was waiting to be retried is gone and the attempt goes with it. A delegate that spells
those statements itself needs the same column.

## Trimming annotations

`ScheduleJob<T>`, `AddTrigger<TJob>` and `TriggerBuilder.Create<TJob>()` gained
`[DynamicallyAccessedMembers]` on their type parameter, matching `AddJob<T>` and `JobBuilder.Create<T>()` —
they build job details or bind job data now, so the job type's members have to survive trimming. If you
wrap them in your own generic method and build with the trim analyzer on, the forwarding type parameter
needs the same annotation:

```diff
- public static void Register<TJob>(IQuartzBuilder q) where TJob : IJob
+ public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TJob>(IQuartzBuilder q) where TJob : IJob
      => q.ScheduleJob<TJob>(t => t.StartNow());
```

`Quartz` is also marked `IsTrimmable` now, which changes what a trimmed publish tells you about it.
Under `TrimMode=full` the assembly is member-trimmed either way; what the mark changes is the reporting.
With `TrimmerSingleWarn` on, which is the default, an assembly's trim-analysis warnings collapse into one
`IL2104: Assembly 'Quartz' produced trim warnings`, and `IsTrimmable` exempts exactly its `IL2026`s from
that collapse. So on 4.0 you see every `[RequiresUnreferencedCode]` call site individually, and
`<TrimmerSingleWarn>false</TrimmerSingleWarn>` shows the rest — around fifty in all, at the reflective
call sites listed in `src/Quartz/TrimAnalysisBaseline.cs`.

That is not a regression: the same reflection was there before and the same code could break under
trimming; you can now see which parts. None of those warnings is suppressed in the shipped assembly,
deliberately — suppressing them would hide a real risk from you. Configuration by flat `quartz.*` keys,
jobs named as strings (`job_scheduling_data` XML, a persisted `JOB_CLASS_NAME`), and `JobDataMap` values
bound onto job properties are the paths that need reflection; an application that configures in code,
references its job types statically and keeps job data to primitives exercises far less of it. A
**persistent job store** publishes trimmed, and publishes native AOT, when it is told how to reach its
driver without naming one — `UseSqlServer(SqlClientFactory.Instance, connectionString)`, or a registered
`DbDataSource`: the default System.Text.Json serializer carries a source-generated contract for every
blob a store writes, and a custom trigger or calendar type is answered by the registry it was registered
with. The one thing left open is a job-data value of a type of your own, which the registry is handed
metadata for through `SystemTextJsonSerializerRegistry.AddTypeInfoResolver` — see
[Publishing Trimmed and Native AOT](how-tos/trimming-and-native-aot.md) for the shape of that.

`Quartz` also declares **`IsAotCompatible`** on 4.0, which 3.x does not. What it claims is narrow and
checkable: nothing Quartz does needs code generated at run time, so a native AOT publish reports no
`IL3050` against it at all. The last pair belonged to configuration binding, and binding the `Quartz`
section is source-generated now — the compiler writes a binder for each options type — so configuring
from `appsettings.json` is as ahead-of-time-safe as configuring in code, with nothing asked of your
application. The `IL2xxx` above are unaffected by the claim and still reported; `Quartz.Trimming.Canary`
is published by ILCompiler and **run** on Windows, Linux and macOS on every pull request, scheduling
and firing over a real SQLite store and binding a whole scheduler out of an `IConfiguration`. The rest
of the track is [#3341](https://github.com/quartznet/quartznet/issues/3341).

**Every other shipped package now answers the question too**, which on 3.x none of them does. On 3.x an
application that adds any plugin to a trimmed publish gets one `IL2104` per unmarked assembly and no
detail; on 4.0, `Quartz.Jobs`, `Quartz.Plugins`, `Quartz.HttpClient`, `Quartz.AspNetCore`,
`Quartz.Extensions.Redis` and `Quartz.Plugins.TimeZoneConverter` are marked trimmable and report their
individual call sites — between none and two each, all of them a job type spelled as a string, and all of
them an `IL2026`, which is the code `IsTrimmable` exempts from the collapse. No public
member of any of them gained `[RequiresUnreferencedCode]` or `[DynamicallyAccessedMembers]`, so nothing
you call changes shape. `Quartz.Serialization.Newtonsoft` and `Quartz.Dashboard` say the opposite
deliberately, in their csproj and in their nuget.org readme: Json.NET's contract is reflection, and
Blazor Server binds components and their parameters by name. The table is in
[Which packages say whether they can be trimmed](how-tos/trimming-and-native-aot.md#which-packages-say-whether-they-can-be-trimmed).

## Executing is a trigger state

There was no way to ask whether a trigger's job is running right now. 3.x's
`IScheduler.GetCurrentlyExecutingJobs` only ever saw the node it was called on, so a process that schedules
and observes triggers but does not execute them — a dashboard, an admin UI, a separate control
application — could not answer the question at all. (That member is gone in 4.0; see
[what is running is a listing](#what-is-running-is-a-listing-not-a-list-of-contexts).)

`TriggerState` now has an `Executing` member, reported by both `IScheduler.GetTriggerState` and trigger
listings. With a persistent job store it is visible from every node, because it is established from the
fired-triggers table rather than from process-local state.

### What changed in what you get back

A trigger with an execution in flight previously reported `Normal`, `Complete`, or `Blocked` depending on
its schedule; it now reports `Executing`. States are resolved in this order:

```text
None > Error > Paused > Executing > Blocked > Complete > Normal
```

So a trigger that is paused, or in the error state, still reports that even while its job runs — those are
the facts an operator has to act on. Two consequences worth calling out:

* `Blocked` now means a **different** trigger of the same `[DisallowConcurrentExecution]` job is running,
  so this one cannot fire. The trigger that is actually running reports `Executing`. Previously both
  reported `Blocked` and nothing could tell them apart.
* A trigger with no fire times left whose final execution is still running reports `Executing` rather than
  `Complete`. In 3.x this case reported `Blocked`, which was a stand-in for a state that did not exist yet.

Note that executing is not exclusive with being scheduled: a trigger whose job allows concurrent execution
can be running several jobs and still be due to fire again. It reports `Executing` until the last of them
finishes.

### What to check in your own code

* **Health checks and guards of the form `if (state == TriggerState.Normal)`** will now see `Executing` for
  a trigger that is simply busy. Treat `Executing` as healthy.
* **Watchdogs of the form `if (state != TriggerState.Normal) await ResumeTrigger(key)`** deserve a closer
  look. `ResumeTrigger` applies the trigger's misfire policy to any trigger whose next fire time has
  passed, regardless of the state it is currently in — and for a long-running job on a short interval, the
  next fire time is in the past for the whole execution. Such a watchdog can therefore alter the schedule
  of a perfectly healthy trigger. This is not new in 4.x, but `Executing` routes more triggers into it, so
  gate the watchdog on the states you actually mean to repair (`Error`, `Paused`).
* **Alerting that treats `Blocked` as "a job is running"** should move to `Executing`.

### Filtering a listing by state

`TriggerQuery.State` accepts `Executing` like any other state. Because the filter and the reported state
are derived together, a listing filtered by `Normal` no longer returns triggers it would then report as
`Executing`.

### A note on stale executions

Executing is established from the fired-triggers table, which is the only durable record that a job is
running. If a node dies mid-execution, its rows stay behind until another node's cluster recovery clears
them, so during that window a trigger reports `Executing` although nothing is running — and, because the
filter and the reported state agree by construction, it is also absent from a listing filtered by
`Normal`. The same window already existed for `Blocked`; it is now visible for more states.

How long that lasts depends on the job. For an ordinary job the rows are cleared as soon as another node
detects the failure, roughly `clusterCheckinInterval` + `clusterCheckinMisfireThreshold`. For a
`[DisallowConcurrentExecution]` job, cluster recovery deliberately *preserves* the executing rows on first
detection — the node may still be alive and running the job, and reviving it elsewhere would break the
concurrency guarantee — and only cleans them up on a later pass, once the elapsed time exceeds
`2 × clusterCheckinInterval + clusterCheckinMisfireThreshold`. So with a 15-second interval and a
60-second threshold, expect up to about 90 seconds plus one more check-in cycle before such a trigger
stops reporting `Executing`.

### If you implement `IDriverDelegate`: the executing state

`IsTriggerCurrentlyExecuting` was replaced by `SelectTriggerStateWithExecuting`, which returns the stored
state and whether an execution is in flight from a single statement, so reporting a trigger's state stays
one round trip. Subclasses of `StdAdoDelegate` get it for free. There is no schema change.

Note that `GetTriggerState` now calls this method instead of `SelectTriggerState`. If you override
`SelectTriggerState` to handle a vendor quirk or a legacy state value, override
`SelectTriggerStateWithExecuting` as well — the compiler cannot tell you, because the old method is still
on the interface and still used elsewhere.

## What is running is a listing, not a list of contexts

`IScheduler.GetCurrentlyExecutingJobs()` is **removed**. It read a list of live `IJobExecutionContext`s
that this process happened to be holding, so it could only ever answer for one node — the same gap that
`TriggerState.Executing` above works around from the other side.

```diff
- List<IJobExecutionContext> running = await scheduler.GetCurrentlyExecutingJobs();
+ PagedResult<FireInstance> running = await scheduler.QueryFireInstances();
```

With a persistent job store the answer now covers the whole cluster, because a firing is a row rather
than a field. `FireInstance` is what a store can say about one firing from anywhere:

| Member | Meaning |
|---|---|
| `FireInstanceId` | identifies this firing; what `InterruptFireInstance` takes |
| `TriggerKey` | the trigger that fired |
| `JobKey` | the job — `null` while the firing is only `Acquired` |
| `SchedulerInstanceId` | the node that reserved or is running it |
| `State` | `FireInstanceState.Acquired` or `FireInstanceState.Executing` |
| `FireTimeUtc` | when the owning node recorded the firing |
| `ScheduledFireTimeUtc` | the fire time the schedule called for |
| `ExecutionGroup` | the execution group the trigger carried when it fired |

`FireInstanceQuery` derives from `PagedQuery` and filters by trigger group and name matchers, job key,
scheduler instance id, and state. **`State` defaults to `Executing`**, so a query that says nothing lists
what is running, which is what the old member meant; set it to `null` to include reservations. Results
are ordered by trigger group, then trigger name, then fire instance id — one trigger can have several
firings at once, so the fire instance id is what makes a page deterministic.

### What a fire instance deliberately does not carry

The job instance, the merged job data map, `Result`, `JobRunTime` and the cancellation handle exist only
inside the process running the job. If you were reaching for those — a progress endpoint, a "cancel this
one" button implemented in-process — keep the contexts yourself. That is about thirty lines:

```csharp
public sealed class RunningJobs : IJobListener
{
    private readonly ConcurrentDictionary<string, IJobExecutionContext> running = new();

    public string Name => nameof(RunningJobs);

    public IReadOnlyCollection<IJobExecutionContext> Current => running.Values.ToArray();

    public ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        running[context.FireInstanceId] = context;
        return default;
    }

    public ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default)
    {
        running.TryRemove(context.FireInstanceId, out _);
        return default;
    }
}

// registered like any other listener
scheduler.ListenerManager.AddJobListener(runningJobs);
```

Note the key: `FireInstanceId` is what ties a context you are holding to a row `QueryFireInstances`
returned, so the two can be used together — list across the cluster, then reach for the live context when
the firing turns out to be yours.

### Three things to know about the numbers

* **A vetoed firing does not linger.** Applying an `ITriggerListener` veto completes the firing, which
  removes it. It can be listed for the instant between the store recording the firing and the veto being
  decided, and never after. `GetCurrentlyExecutingJobs` never showed one at all, because it only recorded
  a job that had started.
* **Elapsed time is `yourClock.UtcNow - FireTimeUtc`**, and `FireTimeUtc` was written by the firing node's
  clock. In a cluster with skewed clocks the subtraction carries the skew and can come out negative;
  clamp it at zero, as the dashboard does.
* **`ScheduledFireTimeUtc` is the schedule after a misfire, not before it.** It is what the owning node
  recorded, so for a misfired trigger it is the rescheduled time rather than the one that was missed —
  it can differ from `IJobExecutionContext.ScheduledFireTimeUtc`, and `FireTimeUtc - ScheduledFireTimeUtc`
  is not misfire lateness.

### `SchedulerMetadata` gained a node-local count

`SchedulerMetadata.LocalExecutingJobs` is the number of executions running in *this* process. It is named
for its scope on purpose: it and a cluster-wide `QueryFireInstances` count answer different questions and
will differ on a cluster.

### The HTTP endpoint changed shape (breaking)

`GET …/jobs/currently-executing` is replaced by `GET …/jobs/fire-instances`, which is paged like the other
listings. The body was a bare array of contexts; it is now the usual `{ items, hasMore, totalCount }`
envelope.

| Old field | New field |
|---|---|
| `jobDetail.name` / `jobDetail.group` | `jobName` / `jobGroup` — and they are `null` for a reserved firing |
| `jobDetail.*` (type, durability, recovery, concurrency flags, job data map) | gone; fetch the job detail by key if you need it |
| `trigger` (the whole serialized trigger) | `triggerName` / `triggerGroup` |
| `trigger.executionGroup` | `executionGroup` |
| `calendar` | gone |
| `recovering` | gone |
| `fireTime` | `fireTimeUtc` |
| `scheduledFireTime` | `scheduledFireTimeUtc` |
| `previousFireTime`, `nextFireTime` | gone; they describe the trigger, not the firing |
| — | `fireInstanceId` (new — the old DTO never carried it, although clients read for it) |
| — | `schedulerInstanceId` (new) |
| — | `state` (new; `"Acquired"` or `"Executing"`, a name like every other enum on the wire) |

Query parameters are `skip`, `take`, `includeTotalCount`, the `group*`/`name*` matcher forms the other
listings use, `jobName`+`jobGroup`, `schedulerInstanceId`, and `state`. A request naming no `state` gets
the query record's default of `Executing`; `state=Any` asks for every state.

The scheduler body's `statistics` object gained `localExecutingJobs`, mirroring the metadata member.

### If you implement `IJobStore`

Four members. `QueryFireInstances(FireInstanceQuery, CancellationToken)` is the listing, abstract like
the rest of the query family; `QueryClusterNodes(CancellationToken)` is the node listing described in
[The nodes of a cluster are a listing](#the-nodes-of-a-cluster-are-a-listing);
`Exists(string calendarName, CancellationToken)` is what replaced `CalendarExists` and is meant to be
answered without materializing the calendar. And `Initialize` takes a
`SchedulerIdentity`, which is the whole of what it takes — see
[SPI changes](#spi-changes) for what its 3.x parameters became:

```csharp
ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default);
```

The identity — the scheduler's name and this node's instance id — could not be a constructor argument,
because with `GenerateInstanceId` the id is produced by an `IInstanceIdGenerator` that runs after the
container has built the object graph. Initialization is the first moment it is settled, which is the same
reasoning as `LockHandlerContext` on `ILockHandler.Initialize`. A store records it against the firings it
owns, so that a listing can say which node is running what.

The ADO.NET store previously learned a generated id through a special case in the scheduler factory,
which is gone: every store is told, the same way, at the same moment.

### If you implement `IDriverDelegate`: fire instances

`SelectFireInstances(conn, FireInstanceQuery, CancellationToken)` is new. Subclasses of `StdAdoDelegate`
get it for free. It is a separate member rather than paging on `SelectFiredTriggerRecords`, because every
caller of that one is a recovery pass that has to see all the rows — handing one a page would leave the
rest unrecovered.

`QRTZ_FIRED_TRIGGERS.EXECUTION_GROUP` is now live: written by the fired-trigger insert and update, read
back into `FireInstance.ExecutionGroup`. **No schema change** — the column has shipped on every dialect
since 3.18 — but rows written by an earlier 4.0 preview hold `NULL`.

## The nodes of a cluster are a listing

Nothing in 3.x read `QRTZ_SCHEDULER_STATE`. The table was written on every check-in and swept by the
failover pass, and that was the whole of it: an operator asking which of four nodes was alive ran SQL by
hand, because `SchedulerMetadata.JobStoreClustered` — a `bool` saying clustering is *on* — was the only
cluster fact the API exposed.

```csharp
List<ClusterNode> nodes = await scheduler.QueryClusterNodes();
```

`ClusterNode` is `InstanceId`, `LastCheckInUtc`, `CheckInInterval`, a `ClusterNodeState` of `Alive`,
`Overdue` or `Failed`, and `IsCurrentNode`. The node answering is first, always present — even before
its first check-in has written a row — and the only one whose `IsCurrentNode` is `true`; the rest follow
by instance id. The state is decided by the same predicate the recovery sweep applies, so the listing
and the failover it predicts cannot disagree, and the two times are `null` rather than zero when the
store keeps no check-in history. **`null` is not the epoch**: a reader that falls back to
`DateTimeOffset.MinValue` will draw a node that has been dead since year one.

A scheduler that is not clustered answers with the one node it is, `Alive` and with no times, rather
than with an empty list, so a caller need not branch on whether clustering is on. See
[Seeing the cluster](tutorial/advanced-enterprise-features.md#seeing-the-cluster).

`SchedulerStateRecord` is unchanged and stays the ADO.NET store's own row shape, the way
`FiredTriggerRecord` sits beside `FireInstance`; `ClusterNode` is the store-neutral projection.

| Where | What is new |
|---|---|
| `IScheduler` | `ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)` — implement it if you have an `IScheduler` of your own; `DelegatingScheduler` forwards it for you |
| `IJobStore` | `ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)` — a plain member, so a store of your own must implement it. A store with no cluster state answers with one node: itself, `Alive`, both times `null` |
| HTTP API | `GET {ApiPath}/schedulers/{name}/nodes`, unpaged, and `HttpScheduler.QueryClusterNodes` reads it (see [Cluster nodes](packages/http-api.md#cluster-nodes)) |
| Dashboard | `IQuartzApiClient.QueryClusterNodes(name, CancellationToken)`, and a **Cluster** page at `/quartz/cluster` |

Neither member touches the schema, and neither writes anything: `QueryClusterNodes` is a read of the
rows the check-in loop already keeps.

## An unset execution group can be the trigger's group

`ExecutionLimitsBuilder.UseTriggerGroupWhenUnset()` limits a trigger that carries no execution group as
though it belonged to a group named after its own `TriggerKey.Group`. It is opt-in and additive; nothing
changes unless you call it. See
[Execution Groups](/documentation/quartz-4.x/tutorial/execution-groups.html#letting-the-trigger-group-stand-in).

One signature moved with it: `ExecutionSlots.TryTake` takes the trigger group as a second argument, so a
job store of your own cannot silently opt out of the derivation by not passing it. Whether the second
argument is used at all is `ExecutionLimits.UsesTriggerGroupWhenUnset`, which is what the builder method
sets.

```diff
- if (!slots.TryTake(candidate.ExecutionGroup))
+ if (!slots.TryTake(candidate.ExecutionGroup, candidate.Key.Group))
```

## An execution limit can be cluster-wide

An execution limit now says what it is counted against. `ExecutionLimitScope.Node`, the default and the
only behaviour 3.x and earlier 4.0 previews had, is what *this* node may run — so an N-node cluster runs
up to N times the number. `ExecutionLimitScope.Cluster` is what every node sharing the job store may run
between them, which is what a per-tenant quota actually means:

```csharp
q.UseExecutionLimits(limits => limits
    .ForGroup("high-cpu", 2)                                  // per node, as before
    .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster)  // per cluster
    .ForOtherGroups(1));
```

Nothing changes for a configuration that does not ask for it, and both scopes can appear in one set of
limits. `quartz.clusterExecutionLimit.<group>` is the property spelling, taking the same group keys and
values as `quartz.executionLimit.<group>`.

**No schema change.** The count is an aggregate over `QRTZ_FIRED_TRIGGERS.EXECUTION_GROUP`, which has
shipped on every dialect since 3.18 and has been written since the fired-trigger insert started carrying
it. A row there already has a reservation's lifetime — inserted on acquisition, updated when the trigger
fires, deleted on completion or by cluster recovery — so there is no new table, no new column, and no
migration on either branch.

Read [Execution Groups](/documentation/quartz-4.x/tutorial/execution-groups.html#clustering-considerations)
before relying on it: the ceiling is **approximate with a bounded overshoot** unless
`AcquireTriggersWithinLock` is on, it **fails closed** (a node that cannot reach the store fires nothing
rather than firing unmetered), and work held at a ceiling for longer than `MisfireThreshold` goes to
misfire handling.

### What moved

| Before | 4.0 |
|---|---|
| `ExecutionGroupLimit(ExecutionGroupScope Scope, int? MaxConcurrent)` | `ExecutionGroupLimit(ExecutionGroupScope Group, int? MaxConcurrent, ExecutionLimitScope Scope = Node)` |
| `ExecutionLimits.TryGetLimit(ExecutionGroupScope scope, out int?)` | `TryGetLimit(ExecutionGroupScope group, out int?)` — parameter renamed; the number is still all it returns |
| `ExecutionLimits.CreateSlots()` | `CreateSlots(IReadOnlyCollection<ExecutionGroupInFlight>? clusterInFlight = null)` |
| — | `ExecutionLimits.HasClusterScopedLimits` |
| — | `ExecutionGroupInFlight(string? ExecutionGroup, string TriggerGroup, int Count)` |
| — | `TriggerAcquisitionCriteria.ClusterInFlight` |
| — | `IDriverDelegate.SelectExecutionGroupsInFlight(conn, cancellationToken)` |
| `ExecutionLimitsResponse` / `SetExecutionLimitsRequest` carrying `Dictionary<string, int?>` | carrying `Dictionary<string, ExecutionLimitDto>`, where the DTO is `(int? MaxConcurrent, ExecutionLimitScope Scope)` |
| `ExecutionLimitsDto(Dictionary<string, int?> Limits)` (dashboard) | `ExecutionLimitsDto(Dictionary<string, DashboardExecutionLimit> Limits, bool UsesTriggerGroupWhenUnset = false, bool CanReport = true)` — see [The dashboard's client speaks one currency](#the-dashboard-s-client-speaks-one-currency) |

If you implement `IDriverDelegate` from scratch rather than deriving from `StdAdoDelegate`, the new
member is a compile break — deliberately, because returning "nothing in flight" from a stub would fail
the ceiling open. `StdAdoDelegate` implements it for every dialect with one statement and no overrides.

If you implement `IJobStore`, nothing is required: a store that reports `Clustered == false` has one
node, so a cluster-scoped limit and a node-scoped one are the same number, and the limits it is handed
already say what that number is. A store that *is* clustered honours the scope by passing its own
in-flight counts to `CreateSlots`, and must **not** expect the scheduler thread to have subtracted this
node's running work from a cluster-scoped limit — those firings are already reservations in its own
ledger, and subtracting them twice halves the quota.

## Batched Misfire Recovery

Misfire recovery now handles a whole batch of misfired triggers with a handful of statements instead of a
few per trigger. Nothing needs configuring — the read side is a single set-based query, and the write side
uses ADO.NET batching automatically on providers that support it (`DbConnection.CanCreateBatch`), falling
back to individual statements on those that do not.

This matters if you implement `IDriverDelegate` yourself, which now has two more members:

| Member | Purpose |
|--------|---------|
| `SelectMisfiredTriggersToRecover` | Reads a whole misfire batch as populated triggers in one round-trip |
| `UpdateMisfiredTriggers` | Applies a batch of misfire updates, batching the statements where supported |

If you subclass `StdAdoDelegate` you get both for free. A driver delegate for a database with its own
row-limiting syntax should also override `GetSelectMisfiredTriggersToRecoverSql`.

One behavioral note: `ITriggerListener.TriggerMisfired` is now raised for every trigger in a batch before
any of that batch's database updates are written, where previously the notification and the update were
interleaved per trigger. Everything still happens inside the same transaction and under the same lock, so
what other nodes observe is unchanged.

## Batched trigger fire

Firing one trigger used to take between six and nine round trips inside the `TRIGGER_ACCESS` lock, and
completing the job that fired took a read per trigger of that job on top. Every one of those is time every
other node in the cluster spends waiting for the lock. The fire path now reads the trigger's row once and
writes everything in one batch, and completion asks one question instead of one per trigger.

Nothing needs configuring. Batching is used where `DbConnection.CanCreateBatch` says the provider supports
it, and the same statements go out one command at a time where it does not — a provider that cannot batch
issues exactly the statements it always did, in the same order.

This matters if you implement `IDriverDelegate` yourself, which has three more members:

| Member | Purpose |
|--------|---------|
| `ApplyTriggerFired` | Every write one fire makes — the fired-trigger row, the misfire original fire time, the sibling states of a serial job, the trigger's row and its schedule — described by `TriggerFiredUpdate` and issued as one batch |
| `UpdateTriggerStatesForJobFromOtherState(conn, jobKey, IReadOnlyList<TriggerStateTransition>, ct)` | Several conditional state changes for one job in one round trip, beside the single-transition overload that is still there |
| `SelectTriggerKeysForJob(conn, jobKey, StoredTriggerState, ct)` | The keys of a job's triggers in one state, beside the unfiltered overload |

`ITriggerPersistenceDelegate` gains `TryDescribeUpdateExtendedTriggerProperties`, which appends the
statement `UpdateExtendedTriggerProperties` would have issued to a `List<SqlStatement>` rather than issuing
it, so that a trigger's schedule travels in the same round trip as its row. It is a default interface
member returning `false`, so a persistence delegate written before this keeps being given a round trip of
its own and behaves exactly as it did. `SqlStatement` and `SqlStatementParameter` are public for this
reason.

Two contracts moved to make it possible:

| Was | Is |
|-----|-----|
| `StoredTriggerHeader(Key, JobKey, State, NextFireTimeUtc)` | `StoredTriggerHeader(Key, JobKey, State, NextFireTimeUtc, TriggerType)` — the discriminator comes off the same row, and reading it there is what removes the separate `SELECT TRIGGER_TYPE` |
| `SqlSelectTriggerHeader` projects four columns | It projects `TRIGGER_TYPE` as well |

Behavioral notes:

- The fired-trigger row is written from the scheduled fire time the store hands `ApplyTriggerFired`, not
  from the trigger's own next fire time. It used to be read off the trigger, which meant the row had to be
  written before `Triggered()` advanced it — an ordering constraint a batch cannot honour, and one that was
  never written down anywhere.
- A batch that fails is replayed statement by statement so the exception names the statement that failed —
  unless the failure was transient, in which case it surfaces as itself. A replay against a connection that
  has just dropped, or a transaction the server has already doomed, produces a different and unrecognisable
  failure, and the store's retry only recognises a transient failure from the exception it is handed. This
  applies to the batched misfire writes above as well.
- The exception a failed fire raises now names the fire rather than the individual statement:
  `Couldn't record the fire of trigger '…' for '…' job`, where it used to be one of `Couldn't update fired
  trigger`, `Couldn't update states of blocked triggers` or `Couldn't store trigger '…'`.
- **`IDriverDelegate.UpdateFiredTrigger` is gone.** It was the fire path's only caller, and `ApplyTriggerFired`
  writes that row as one command of its batch instead. A delegate that overrode it to change what a fire
  records has to override `ApplyTriggerFired` — which is why the member was removed rather than left in
  place: an override that is never invoked fails silently, and a customisation discovered to have stopped
  working in production is worse than one that fails to compile at upgrade time. The statement itself is
  unchanged; `StdAdoConstants.SqlUpdateFiredTrigger` still spells it.
- The fire path also no longer calls `TriggerExists` or `UpdateTrigger`, both of which stay because other
  paths still use them. **A delegate that overrode either of those to change what a fire stores has to move
  that override to `ApplyTriggerFired` as well** — they still compile and still work, they are simply no
  longer on this path.
- Completion no longer loads a job's triggers to ask the database for each one's state. It asks for the
  keys in the state it cares about, loads those in one read, and applies their misfire policies as one
  batched write, which is what misfire recovery already did. A trigger that runs out of fire times while
  blocked is still stored `COMPLETE`, still finalized to the scheduler listeners, and still deleted.

## Batched cluster recovery

Recovering a failed node used to issue up to ten statements per fired-trigger row it left behind, each
its own round trip, all of them inside the `TRIGGER_ACCESS` lock every other node is waiting for. The
statements are the same; they now travel a set at a time.

Nothing needs configuring, and the same `DbBatch` support decides it as everywhere else: providers that
cannot batch issue exactly the statements they always did, one command at a time, in the same order.

This matters if you implement `IDriverDelegate` yourself, which has three more members:

| Member | Purpose |
|--------|---------|
| `UpdateTriggerStatesForJobsFromOtherState(conn, IReadOnlyCollection<JobKey>, newState, oldState, ct)` | The single-job overload's statement, once per job key, in one batch — unblocking the siblings of every interrupted execution |
| `DeleteFiredTriggers(conn, IReadOnlyCollection<string> entryIds, ct)` | The single-entry delete, once per id, in one batch — clearing a dead node's rows while holding some of them back |
| `SelectTriggerKeysInState(conn, IReadOnlyCollection<TriggerKey>, state, ct)` | One read saying which of the given triggers are in a state, where `SelectTriggerState` per key was a round trip per key — deciding which of a dead node's triggers had run to COMPLETE |

The first two return `ValueTask` rather than a row count: a batch does not report one per command in any
portable way, and the store counts the rows it asked about rather than the rows that moved. Subclassing
`StdAdoDelegate` gets all three for free.

Releasing what a dead node had reserved is the fourth such step, and it uses
`UpdateTriggerStatesFromOtherStates(conn, IReadOnlyCollection<TriggerKey>, newState, oldStates, ct)` —
described under [Batched trigger acquisition, and the sets pause and resume work
on](#batched-trigger-acquisition-and-the-sets-pause-and-resume-work-on), because pause and resume want
the same shape. It is one `UPDATE` with a key-set predicate rather than a command per key, so it *does*
report a row count; a caller wanting one old state passes a set of one.

Behavioural notes:

- `ClusterRecover` is a sequence of named steps — release, unblock, reschedule, delete the fired rows,
  delete the triggers those rows were the last of, give up the failed node's registration — rather than
  one pass that did all of it per row. The steps are grouped by what they do instead of issued row by
  row, which is only safe because they run in one transaction under one lock and touch disjoint rows.
  What another node can observe is unchanged.
- Scheduling a replacement firing is still row by row: each one reads whether its job still exists and
  what its trigger's data map holds, and both answers decide whether anything is written at all.
- **A node that finds its own `QRTZ_SCHEDULER_STATE` row gone now handles it.** On 3.x it logged
  `This scheduler instance (…) is still active but was recovered by another instance in the cluster` and
  carried on. It now writes its row back, names the peer that recovered it where the remaining rows say
  which one that must have been, counts the event on `quartz.cluster.recovery.trigger` under its own
  instance id, and does not recover its own fired triggers — the peer already did, and doing it again
  would replay a firing that is already being replayed. See
  [When the node that was taken over is still running](operations.md#when-the-node-that-was-taken-over-is-still-running).

## Batched trigger acquisition, and the sets pause and resume work on

Acquiring a trigger used to cost three statements per candidate on top of the read that found it: the
acquisition `SELECT` named the batch, and then each candidate was read back on its own, compare-and-swapped
into the acquired state, and written to the fired-triggers table. Pausing or resuming a set of triggers —
or a matcher's worth of jobs — walked the set and called the single-key member, which is two or three
statements per key. All of it runs inside the `TRIGGER_ACCESS` lock, so all of it is time the other nodes
of a cluster spend waiting.

A round now reads its candidates in one statement and writes their fired-trigger rows in one batch; the
compare-and-swap stays per candidate, because its result is what decides whether that candidate was
acquired at all. Pause and resume read the stored states of a whole set once and then issue one statement
per transition the set turns out to need. Nothing needs configuring, and nothing about the rows written or
the answers returned has changed.

This matters if you implement `IDriverDelegate` yourself. Every member below is a **default interface
member** whose default is the per-key loop the store used to make, so a delegate written before this keeps
compiling and keeps behaving exactly as it did — it simply keeps paying the round trips. `StdAdoDelegate`
overrides all of them, so a delegate deriving from it gets the batched forms for free.

| Member | Purpose |
|--------|---------|
| `InsertFiredTriggers(conn, IReadOnlyList<IOperableTrigger>, state, jobDetail, ct)` | An acquisition round's fired-trigger rows as one batch |
| `SelectStoredTriggerHeaders(conn, IReadOnlyCollection<TriggerKey>, ct)` | The plural of `SelectTriggerHeader`; what pause and resume decide a whole set's transitions from. Not the listing `SelectTriggerHeaders`, which pages over `TriggerHeader` |
| `UpdateTriggerStatesFromOtherStates(conn, IReadOnlyCollection<TriggerKey>, newState, oldStates, ct)` | One conditional transition for a whole key set — one `UPDATE` with a key-set predicate — beside the store-wide overload that is still there. Cluster recovery releases a dead node's reservations through it as well, with a set of one old state |
| `SelectTriggerKeysForJobs(conn, IReadOnlyCollection<JobKey>, ct)` | Every trigger key of a set of jobs in one read, for pausing and resuming a job matcher |
| `SelectPausedJobGroups(conn, IReadOnlyCollection<string>, ct)` | Which of a set of job groups already have a paused row — the set form of `IsJobGroupPaused` |
| `InsertPausedJobGroups(conn, IReadOnlyCollection<string>, ct)` | The missing paused-job-group rows, together |

One new property comes with them:

| Member | Purpose |
|--------|---------|
| `FiltersAcquisitionJobTypeExclusions` | Whether `SelectTriggersToAcquire` already keeps `TriggerAcquisitionCriteria.ExcludedJobTypeNames` out of the rows it returns. It defaults to `false`, and `StdAdoDelegate` answers `true` |

`ExcludedJobTypeNames` is the one acquisition criterion a delegate cannot safely ignore: ignoring it means
the node *runs* job types the deployment excluded. `AdoJobStoreBase` therefore keeps a backstop, which now
drops an excluded candidate on the job type name the acquisition read already returned — before the
candidate costs a read and a type resolution. A delegate that answers `true` is taken at its word and the
backstop is skipped entirely, so the shipped dialects pay nothing per candidate for a promise their own
`NOT IN` clause already keeps. **If you subclass `StdAdoDelegate` and override
`GetSelectNextTriggerToAcquireSql` with a statement that does not splice in the exclusion terms, override
`FiltersAcquisitionJobTypeExclusions` back to `false`**, or the exclusions stop being enforced anywhere.

Behavioral notes:

- `StdAdoDelegate.SelectTriggersForJob` is now the job's trigger keys followed by one `SelectTriggers` for
  the set, where it used to read each trigger back on its own. The triggers and their order are the same.
- `StdAdoDelegate.SelectTriggers` and `InsertFiredTriggers` answer a one-element set through
  `SelectTrigger` and `InsertFiredTrigger`. One key is one statement either way, and the set forms pay for
  a key-set predicate and a `DbBatch` to say the same thing — which matters because the scheduler's
  default acquisition batch size is one. An override of either singular member therefore also takes
  effect for a set of one.
- Pausing or resuming a job no longer materializes that job's triggers to reach their keys. The schedule in
  a trigger's type table was read and never looked at.
- `PauseAll` and `ResumeAll` pass the any-group matcher once rather than naming each trigger group in turn.
  The statements are the same ones; there is one set of them instead of one set per group.
- A resume still applies each overdue trigger's misfire policy with that trigger's own write, because
  recovering a misfire recomputes one trigger's schedule and cannot be expressed as a set operation.

## Job store listings became queries

Listing jobs or triggers meant reading every key and then spending one round trip per key on anything more
than the key — the job's type, the trigger's state or next fire time. Nothing could ask for a page, and
nothing could ask for a count without materializing what it counted.

Those members are replaced by query members that take a query record and return one page of projected
results. **Existing code keeps compiling**: every removed `IScheduler` member comes back as an extension
method in `SchedulerQueryExtensions`, with the same name and signature.

### What replaced what

`IScheduler` — the left column still works, now as an extension method:

| Removed from `IScheduler` | Query member |
|---|---|
| `GetJobKeys(matcher)` | `QueryJobs(new JobQuery { Group = matcher })` |
| `GetTriggerKeys(matcher)` | `QueryTriggers(new TriggerQuery { Group = matcher })` |
| `GetJobGroupNames()` | `QueryJobGroups(new JobGroupQuery())` |
| `GetTriggerGroupNames()` | `QueryTriggerGroups(new TriggerGroupQuery())` |
| `GetPausedTriggerGroups()` | `QueryTriggerGroups(new TriggerGroupQuery { Paused = true })` |
| `GetCalendarNames()` | `QueryCalendarNames(new CalendarQuery())` |
| `IsJobGroupPaused(group)` | `QueryJobGroups(new JobGroupQuery { Name = group, Paused = true, Take = 1 })` |
| `IsTriggerGroupPaused(group)` | `QueryTriggerGroups(new TriggerGroupQuery { Name = group, Paused = true, Take = 1 })` |

`IJobStore` loses the same members plus the counting and existence ones, and has no extension methods to
soften it — if you implement a job store, you implement the query members:

| Removed from `IJobStore` | Use instead |
|---|---|
| `GetJobKeys`, `GetTriggerKeys` | `QueryJobs`, `QueryTriggers` |
| `GetJobGroupNames`, `GetTriggerGroupNames`, `GetPausedTriggerGroups` | `QueryJobGroups`, `QueryTriggerGroups` |
| `GetCalendarNames` | `QueryCalendarNames` |
| `IsJobGroupPaused`, `IsTriggerGroupPaused` | the matching `Query*Groups` with `Name` and `Paused = true` |
| `GetNumberOfJobs`, `GetNumberOfTriggers`, `GetNumberOfCalendars` | the matching query with `Take = 0, IncludeTotalCount = true` |
| `CalendarExists(name)` | `Exists(string calendarName)`, which is the same question under the name the job and trigger overloads already use — see [Asking a one-word question does not need a query object](#asking-a-one-word-question-does-not-need-a-query-object) |

Two members are new on both interfaces: **`GetJobDetails(jobKeys)`** and **`GetTriggers(triggerKeys)`**
retrieve many by key in one round trip. Keys that do not exist are simply absent, duplicates fold away, and
results come back in the order the keys were asked for.

On `IJobStore` the bulk job fetch is spelled **`GetJobs(jobKeys)`**, completing the store's own pairs:
`GetJob`/`GetJobs` beside `GetTrigger`/`GetTriggers`. The scheduler's noun is `JobDetail` — it hands
users `IJobDetail`, so `IScheduler` keeps `GetJobDetail`/`GetJobDetails` — while the store speaks in
storage terms. Singular/plural pairs are consistent within each interface; the two interfaces
deliberately differ.

### Paging and projection

Every query derives from `PagedQuery`, which carries `Skip`, `Take` and `IncludeTotalCount`. The result
is a `PagedResult<T>` with `Items`, `HasMore` and a nullable `TotalCount`. `HasMore` is exact and costs
nothing: stores read one item past `Take` rather than running a second query.

`Take` defaults to **250** (`PagedQuery.DefaultTake`) — a query that sets nothing returns the first
bounded page, and `HasMore` says whether anything was left out. Earlier 4.0 previews defaulted to
`int.MaxValue`. The bound is the one value in one place: the HTTP endpoints apply the same default when a
request names no `take`, the `HttpScheduler` always puts `take` on the wire (earlier previews omitted the
parameter for `int.MaxValue`, which after this change would have silently handed the decision to the
server), and the compat extension methods below pin `Take = int.MaxValue` so their 3.x semantics — return
everything — cannot silently narrow. Asking for everything is an explicit opt-in:

```csharp
PagedResult<JobHeader> everything = await scheduler.QueryJobs(new JobQuery { Take = int.MaxValue });
```

The count idiom — `Take = 0, IncludeTotalCount = true` — is recognized by the stores, which then run only
the count and skip the page select entirely.

Because the query types are records, walk a result by `with`-ing the next `Skip`:

```csharp
// Before
IReadOnlyCollection<JobKey> keys = await scheduler.GetJobKeys(GroupMatcher<JobKey>.AnyGroup());
foreach (JobKey key in keys)
{
    IJobDetail? detail = await scheduler.GetJobDetail(key); // one round trip each
    Console.WriteLine($"{key} -> {detail?.JobType.FullName}");
}
```

```csharp
// After — one round trip per page, and the type name is already there
JobQuery query = new() { Group = GroupMatcher<JobKey>.AnyGroup(), Take = 100 };
while (true)
{
    PagedResult<JobHeader> page = await scheduler.QueryJobs(query);
    foreach (JobHeader job in page.Items)
    {
        Console.WriteLine($"{job.Key} -> {job.JobTypeName}");
    }

    if (!page.HasMore)
    {
        break;
    }

    query = query with { Skip = query.Skip + page.Items.Count };
}
```

`JobHeader` is a job's metadata *without* its `JobDataMap`, so listing never loads or deserializes job data.
`TriggerHeader` carries the state, fire times, priority, calendar name and execution group that previously
cost an extra round trip per trigger each. When you do need the whole thing, follow a page with one bulk
fetch:

```csharp
List<IJobDetail> details = await scheduler.GetJobDetails(page.Items.Select(x => x.Key).ToList());
```

Counting is a query that reads no rows:

```csharp
// Before
int total = await jobStore.GetNumberOfTriggers();

// After
PagedResult<TriggerHeader> count = await scheduler.QueryTriggers(
    new TriggerQuery { Take = 0, IncludeTotalCount = true });
int total = count.TotalCount!.Value;
```

Unlike the old counting members, this counts what a filter selects rather than a whole table — how many
triggers are in the error state, for instance:

```csharp
PagedResult<TriggerHeader> failed = await scheduler.QueryTriggers(
    new TriggerQuery { State = TriggerState.Error, Take = 0, IncludeTotalCount = true });
Console.WriteLine($"{failed.TotalCount} triggers need attention");
```

`TriggerQuery` also filters on `Job`, `CalendarName` and `Group`; the filters combine with AND, and a null
`Group` matches every group.

### Behavior worth knowing

* **Ordering is group first, then name**, and every page uses the same ordering, so paging is consistent.
  `RAMJobStore` compares ordinal. The ADO job store sorts in the database, so both the group order and the
  order within a group follow the **server's collation** — for most collations that differs from ordinal only
  in case and accent handling, but it is the database's decision, not Quartz's. Sort the page yourself if you
  need one specific culture's ordering.
* **A null matcher now throws.** `scheduler.GetJobKeys(null)` and `GetTriggerKeys(null)` raise
  `ArgumentNullException` instead of silently narrowing the listing to the `DEFAULT` group.
* **The extension methods enumerate everything.** They preserve the old semantics — and the old cost. Use the
  query member with `Skip`/`Take` wherever the result can be large.
* **`JobGroup.Paused` is real on both stores now.** On 3.x the ADO store had nowhere to record a paused job
  group, so `IsJobGroupPaused` answered `false` for every group and the pause was lost on restart. 4.x stores
  the group names in `QRTZ_PAUSED_JOB_GRPS`, so a paused job group survives a restart, reaches the other nodes
  of a cluster, and is listed by `QueryJobGroups(new JobGroupQuery { Paused = true })`. **That table is new,
  which is what makes the 4.0 migration mandatory even for a database that took every optional 3.x
  migration** — see [Database Schema Migration](#database-schema-migration).
* **A group can be paused while it holds no jobs**, and `Paused = true` reports it. The unfiltered listing
  does not: it enumerates the groups jobs are in, and an empty group is not one of them. Trigger groups have
  always behaved this way; job groups now match. `PauseJobs(GroupMatcher<JobKey>.GroupEquals(g))` therefore
  answers `[g]` on the ADO store where 3.x answered `[]` for a group with no jobs.
* **What the recorded pause does *not* do on the ADO store** is impose itself on jobs added to the group
  afterwards. Pausing a job group pauses the triggers of the jobs in it at that moment, and the row records
  which groups are paused; `RAMJobStore` additionally starts a later trigger paused if its job's group is
  paused, and the ADO store does not. Pause by *trigger* group where you need the pause to reach what is
  added next — that behaves identically on both stores.
* **Two indexes were added** to support the ordered scans — see [Database Schema Migration](#database-schema-migration).

### If you implement `IDriverDelegate`: the listing members

Beyond the two batched-misfire members above, the query work adds, removes and consolidates a fair amount.
New members to implement:

| Member | Purpose |
|--------|---------|
| `SelectJobHeaders`, `SelectTriggerHeaders` | One page of projected job/trigger listing rows |
| `SelectJobGroups(conn, JobGroupQuery, ct)`, `SelectTriggerGroups(conn, TriggerGroupQuery, ct)` | One page of groups, with pause state |
| `SelectCalendarNames` | One page of calendar names |
| `SelectJobDetails`, `SelectTriggers` | Bulk fetch by key set |
| `InsertPausedJobGroup`, `DeletePausedJobGroup`, `IsJobGroupPaused` | Read and write `QRTZ_PAUSED_JOB_GRPS`, mirroring the three `…PausedTriggerGroup` members |

Deleted, having had no caller: `SelectMisfiredTriggers`, both `HasMisfiredTriggersInState` overloads,
`SelectMisfiredTriggersInGroupInState`, `IsExistingTriggerGroup`, `SelectJobExecutionCount`,
`SelectTriggerForFireTime`, `SelectNumJobs`, `SelectNumTriggers`, `SelectNumCalendars`, `SelectCalendars`,
`SelectPausedTriggerGroups`, `SelectJobGroups(conn, ct)` and `DeleteAllPausedTriggerGroups`. The
`GetSelectNextMisfiredTriggersInStateToAcquireSql` hook went with them, so a dialect delegate that overrode
it should delete that override.

Renamed to say what they return — a custom delegate author reads these names as the spec, and three of
them said `Names` or whole entities where they return keys, while `SelectTriggerGroups` was one name
overloaded across two unrelated result shapes:

| 3.x | 4.x |
|---|---|
| `SelectTriggerNamesForJob` → `List<TriggerKey>` | `SelectTriggerKeysForJob` |
| `SelectJobsInGroup` → `List<JobKey>` | `SelectJobKeysInGroup` |
| `SelectTriggersInGroup` → `List<TriggerKey>` | `SelectTriggerKeysInGroup` |
| `SelectTriggerGroups(conn, GroupMatcher, ct)` → `List<string>` | `SelectTriggerGroupNames` — the paged `SelectTriggerGroups(conn, TriggerGroupQuery, ct)` now owns the name alone |

Consolidated into records rather than overload families:

| 3.x | 4.x |
|---|---|
| `SelectFiredTriggerRecords`, `SelectFiredTriggerRecordsByJob`, `SelectInstancesFiredTriggerRecords` | `SelectFiredTriggerRecords(conn, FiredTriggerQuery, ct)` |
| four `DeleteFiredTriggers` overloads | `DeleteFiredTriggers(conn, FiredTriggerQuery, ct)` |
| two `SelectTriggerToAcquire` overloads | `SelectTriggersToAcquire(conn, TriggerAcquisitionCriteria, ct)` |
| two `SelectJobForTrigger` overloads | one, with a required `bool loadJobType` |
| `DeletePausedTriggerGroup(conn, string, ct)` | the `GroupMatcher<TriggerKey>` overload |

`FiredTriggerQuery` carries an optional `Trigger`, `Job` and `InstanceId` combined with AND — all null
selects or deletes every fired trigger. `TriggerAcquisitionCriteria` carries `NoLaterThan`, `NoEarlierThan`,
`MaxCount`, `ExecutionLimits` and `LiveNodeCutoff`, and is the extension point for future acquisition
filtering: another way of narrowing what a node picks up is another optional property, not another overload.
`LiveNodeCutoff` is a `required DateTimeOffset` like its two time siblings — it was briefly a raw
`UtcTicks` `long` that defaulted to zero, which silently meant "every node counts as dead"; the tick
conversion lives in the parameter binder now.

Subclassing `StdAdoDelegate` gets you all of it. A database whose row-limiting syntax is not the ANSI
`OFFSET … FETCH NEXT` should override the paging seam — **`ApplyPaging(sql, takeLimited)`** appends the
clause and **`AddPagingParameters(cmd, skip, take, takeLimited)`** binds it (`skip` and `take` are `int`,
matching the query objects they serve). Override both together when your clause names the two parameters
in the other order, because providers that bind positionally take parameters in the order the statement
mentions them. `MySQLDelegate` and `SQLiteDelegate` do exactly this for `LIMIT … OFFSET`. The two
parameter names the pair has to agree on are `AdoConstants.ParameterPageSkip` and
`AdoConstants.ParameterPageTake` — new in 4.0, and public because a delegate outside Quartz has to
spell them.

The value-conversion pairs on `IDbAccessor` are no longer all overridable on `StdAdoDelegate`: the
boolean pair (`GetDbBooleanValue` / `GetBooleanFromDbValue`) stays virtual, because Oracle genuinely
stores booleans differently, but the date/time and time-span conversions are frozen. UTC ticks and
whole milliseconds are part of the schema contract — the preferred-node liveness SQL does raw tick
arithmetic against `LAST_CHECKIN_TIME` and `CHECKIN_INTERVAL`, so a delegate that changed the storage
format would silently break cluster failover for pinned triggers (3.x only logged a warning when it
detected such an override; 4.0 removes the half-open door). A delegate for a database that stores
`DATETIME` natively must implement `IDriverDelegate` directly and own its SQL.

Finally, `ITriggerPersistenceDelegate` gained a batch `LoadExtendedTriggerProperties` taking several trigger
keys. It is a **default interface method** that loops the single-key overload, so a third-party trigger
persistence delegate needs no change; override it only to turn a batch into one round trip.

## Asking a one-word question does not need a query object

The paged query objects are the right primitive for a listing, and they are staying. What they cost is
the trivial case, where the replacement for a parameterless call was
`QueryFireInstances(new FireInstanceQuery())` — a record whose only job was to be the shape the member
takes. Five shorthands close that, and none of them changes what the member behind it does.

| Instead of | Write |
|---|---|
| `QueryJobs(new JobQuery())` | `QueryJobs()` |
| `QueryTriggers(new TriggerQuery())` | `QueryTriggers()` |
| `QueryFireInstances(new FireInstanceQuery())` | `QueryFireInstances()` |
| `QueryFireInstances(new FireInstanceQuery { Job = jobKey })` | `QueryFireInstancesOfJob(jobKey)` |
| `QueryTriggers(new TriggerQuery { State = TriggerState.Error })` | `QueryTriggersInError()` |

They are extension methods in `SchedulerQueryExtensions`, beside the 3.x-compatible listings — but they
belong to the *other* family in that class, and page the way the member they call pages:
`PagedQuery.DefaultTake` items, with `HasMore` reporting the rest. Name the query record when you want a
different page, a total count, or a filter.

### Resetting the triggers that failed is one call each way

`ResetTriggerFromErrorState(TriggerKey)` and `ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey>)`
were already there, and `TriggerQuery.State` could already select the failed ones. What was missing was a
way to say "the ones in this group":

```csharp
// Before
PagedResult<TriggerHeader> failed = await scheduler.QueryTriggers(new TriggerQuery
{
    Group = GroupMatcher<TriggerKey>.GroupEquals("imports"),
    State = TriggerState.Error,
    Take = int.MaxValue
});
await scheduler.ResetTriggersFromErrorState(failed.Items.Select(x => x.Key).ToList());

// After
await scheduler.ResetTriggersFromErrorState(GroupMatcher<TriggerKey>.GroupEquals("imports"));
```

It is that pair of calls, not a new store operation: two round trips, not one transaction, and a trigger
that enters the error state between them is left for the next call. What resetting a trigger *does* is
unchanged. Passing `null` throws rather than quietly meaning every group.

### `Exists` has a calendar overload

`IScheduler.Exists` and `IJobStore.Exists` took a `JobKey` or a `TriggerKey`. Asking the same question
about a calendar meant loading it:

```diff
- bool exists = await scheduler.GetCalendar("holidays") is not null;
+ bool exists = await scheduler.Exists("holidays");
```

The difference is not spelling. `GetCalendar` reads the stored blob and deserializes it — an
`AnnualCalendar` holding a decade of excluded days, materialized to be thrown away. The overload asks the
store for the name: `RAMJobStore` looks it up without cloning, and the ADO.NET store runs the existence
statement that selects a constant rather than the calendar column. **No new `IDriverDelegate` member** —
`CalendarExists` has been on that interface all along, used by `AddCalendar` and by the trigger update
that validates a calendar reference; it simply had nothing above it.

Over HTTP it is `GET {ApiPath}/schedulers/{name}/calendars/{calendarName}/exists`, answering
`{ "exists": … }` like the job and trigger routes it mirrors.

### A scheduler can be required rather than looked up

`ISchedulerFactory.LookupScheduler` answers `null` for a name this container does not know, so every
caller that treats absence as a bug wrote the same throw:

```diff
- IScheduler scheduler = await factory.LookupScheduler("reporting")
-     ?? throw new InvalidOperationException("No scheduler named 'reporting'");
+ IScheduler scheduler = await factory.GetRequiredScheduler("reporting");
```

`GetRequiredScheduler` throws `SchedulerNotFoundException`, which is a `SchedulerException` carrying the
`SchedulerName` that was asked for. It is an extension method rather than a member of `ISchedulerFactory`,
for two reasons: it is composition over `LookupScheduler` that no factory could implement better, and an
extension also resolves on a concrete receiver such as `StandaloneSchedulerFactory`, where a default
interface method would need a cast. `LookupScheduler` is unchanged and stays the right call when a
missing scheduler is an answer rather than a failure.

## Trigger states are typed on the driver delegate

Eighteen members of `IDriverDelegate` took a trigger state as a `string` whose only legal values were the
`AdoConstants.State*` constants. A typo, a stale spelling, or a transposed `newState`/`oldState` pair
compiled and then quietly matched no row. `StoredTriggerState` is now that type — and it lives in
**`Quartz.Extensibility`**, not in the ADO namespace, because it is every store's vocabulary: the
in-memory store keeps its triggers in the same enum (its private `InternalTriggerState` twin is gone),
and a custom `IJobStore` uses it too. The precedence that turns a stored state plus "is it executing"
into the `TriggerState` callers see is public beside it:

```csharp
TriggerState reported = TriggerStateResolver.Resolve(stored, isExecuting);
```

`Resolve` applies `None > Error > Paused > Executing > Blocked > Complete > Normal`. Every built-in
store resolves through it, so a custom store that does the same cannot report a different state than
the ADO store would for the same situation — which used to require re-deriving the precedence from an
internal class's XML comment.

**Nothing changes in the database.** The columns still hold the same strings; the conversion happens at the
delegate boundary, and `AdoConstants.State*` stays public because the strings are the schema contract
(the string mapping, `StoredTriggerStates`, stays in `Quartz.Impl.AdoJobStore` with them). A 4.0
scheduler reads and writes rows a 3.x one wrote, so the two can share a cluster for the length of a
rolling upgrade — under conditions that are about calendars and execution limits rather than trigger
states, and that
[Operating a Cluster](operations.md#a-mixed-3-x-and-4-0-window) sets out.

| `AdoConstants` constant | Stored value | `StoredTriggerState` member |
|---|---|---|
| `StateWaiting` | `WAITING` | `StoredTriggerState.Waiting` |
| `StateAcquired` | `ACQUIRED` | `StoredTriggerState.Acquired` |
| `StateExecuting` | `EXECUTING` | `StoredTriggerState.Executing` |
| `StateComplete` | `COMPLETE` | `StoredTriggerState.Complete` |
| `StateBlocked` | `BLOCKED` | `StoredTriggerState.Blocked` |
| `StateError` | `ERROR` | `StoredTriggerState.Error` |
| `StatePaused` | `PAUSED` | `StoredTriggerState.Paused` |
| `StatePausedBlocked` | `PAUSED_BLOCKED` | `StoredTriggerState.PausedBlocked` |
| `StateDeleted` | `DELETED` | `StoredTriggerState.Deleted` |

Both directions are public, because a custom delegate binds the string into its own statements, and both
are plain static methods — one mapping, one call shape:

```csharp
string stored = StoredTriggerStates.ToStoredValue(StoredTriggerState.PausedBlocked);   // "PAUSED_BLOCKED"
StoredTriggerState state = StoredTriggerStates.FromStoredValue(stored);
```

`FromStoredValue` is deliberately lenient in exactly the way the store always was: a value this version does
not recognise — left by a third-party delegate, a migration, or a hand-repaired row — reads as `Waiting`
(schedulable, reported as a normal trigger), and a `null` column value reads as `Deleted`, the sentinel a
missing trigger already reported. `SelectTriggerState` returns `StoredTriggerState` for the same reason its
result flows straight into `AddTrigger` and `UpdateTrigger`.

One behavior follows from that, and only for a row whose stored state no Quartz version writes: the
mutation side now agrees with the listing side about it. A listing has always reported such a trigger as
`Normal`, while `PauseTrigger` matched neither `WAITING` nor `ACQUIRED` and silently did nothing; now it
pauses it. Storing such a trigger back writes `WAITING` rather than preserving the unrecognised value.

`MisfiredTriggerUpdate.NewState` and `TriggerExecutionState.State` (and its constructor) carry the enum too.
On `AdoJobStoreBase`, the protected members that pass a state through follow: `AddTrigger`,
`UpdateMisfiredTrigger` and `CheckBlockedState`, the last of which now returns
`ValueTask<StoredTriggerState>`.

### The `…FromOtherStates` members take a collection

Three members hard-coded two or three old states. A caller that wanted one repeated a value; one that wanted
four could not say so. The predicate is now generated for the length of the set, with duplicates folded away.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `UpdateTriggerStatesFromOtherStates(conn, newState, oldState1, oldState2, ct)` | `(conn, StoredTriggerState newState, IReadOnlyCollection<StoredTriggerState> oldStates, ct)` |
| `UpdateTriggerStateFromOtherStates(conn, key, newState, oldState1, oldState2, oldState3, ct)` | `(conn, key, StoredTriggerState newState, IReadOnlyCollection<StoredTriggerState> oldStates, ct)` |
| `UpdateTriggerGroupStateFromOtherStates(conn, matcher, newState, oldState1, oldState2, oldState3, ct)` | `(conn, matcher, StoredTriggerState newState, IReadOnlyCollection<StoredTriggerState> oldStates, ct)` |

```diff
- await Delegate.UpdateTriggerStatesFromOtherStates(conn, AdoConstants.StateWaiting,
-     AdoConstants.StateAcquired, AdoConstants.StateBlocked, cancellationToken);
+ await Delegate.UpdateTriggerStatesFromOtherStates(conn, StoredTriggerState.Waiting,
+     [StoredTriggerState.Acquired, StoredTriggerState.Blocked], cancellationToken);
```

An empty set throws `ArgumentException` rather than building a statement that matches nothing. The parameter
is a plain collection rather than a `params` one because every async member of this codebase ends with its
cancellation token, and a `params` parameter has to come last.

### One term for a scheduler instance

The value these members carry has always been the scheduler *instance id*
(`quartz.scheduler.instanceId`), not the scheduler name — `SchedulerStateRecord.SchedulerInstanceId` already
said so, and the interface said `instanceId` while the implementation said `instanceName`.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `FiredTriggerQuery.InstanceName` | `FiredTriggerQuery.InstanceId` |
| `InsertSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `UpdateSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `DeleteSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `SelectSchedulerStateRecords(conn, string? instanceName, …)` | `instanceId` |

**Column names are unchanged**: `SCHED_NAME` still holds the scheduler name and `INSTANCE_NAME` the
instance id. `TriggerPersistenceDelegateContext.SchedulerName` and `DriverDelegateContext.SchedulerName`
keep that spelling — those two really are the scheduler name, and the second one was
`DelegateInitializationArgs.InstanceName` — see
[The initialization seams are context records](#the-initialization-seams-are-context-records).

### Three parameter shapes were fixed

* **`IsJobCurrentlyExecuting(conn, JobKey jobKey, ct)`** — it took `(string jobName, string jobGroup)`, the
  last member of the interface splitting a key into two transposable strings.
* **`SelectJobForTrigger(conn, key, loadHelper, bool loadJobType, ct)`** — `loadJobType` defaulted to `true`
  while sitting in front of the cancellation token, so a call passing a token positionally bound it to the
  wrong parameter. Both values are genuinely used, so the parameter stays; pass `loadJobType: true` for the
  previous default.
* **`UpdateTriggerPreferredNodeConditional(conn, key, PreferredNodeTransition transition, ct)`** — the
  compare-and-swap took `(node, auto, expectedNode, expectedAuto)`, four loose values in two pairs that are
  trivial to transpose and impossible for the compiler to check. The record names both sides and carries
  [`PreferredNode`](#the-preferred-node-is-a-value) values rather than the raw column pair:

  ```csharp
  await Delegate.UpdateTriggerPreferredNodeConditional(conn, trigger.Key, new PreferredNodeTransition
  {
      Expected = PreferredNode.Auto,
      New = PreferredNode.For(InstanceId)
  }, cancellationToken);
  ```

### The matcher-based selects stay, deliberately

`SelectTriggerGroups(matcher)`, `SelectJobsInGroup`, `SelectTriggersInGroup` and `SelectTriggerNamesForJob`
look like leftovers next to the paged `SelectJobHeaders` / `SelectTriggerHeaders` / `SelectTriggerGroups(query)`
members, and they are not. They are not listings: they serve the pause/resume and removal mutation paths,
which have to move every matching row under one lock and therefore must not be paged. Their doc comments now
say so, and they are not going away.

## The ADO.NET job stores are named for whose transaction they use

`JobStoreTX` and `JobStoreCMT` were named after a Java EE distinction — "TX" versus
"container-managed transactions" — that says nothing in .NET, and neither name says which one commits.
They now say it:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `Quartz.Impl.AdoJobStore.JobStoreTX` | `Quartz.Impl.AdoJobStore.LocalTransactionJobStore` |
| `Quartz.Impl.AdoJobStore.JobStoreCMT` | `Quartz.Impl.AdoJobStore.ExternalTransactionJobStore` |

`LocalTransactionJobStore` begins the transaction each operation runs in and commits or rolls it back
itself; it stays the default and the one nearly everybody wants. `ExternalTransactionJobStore` runs
inside a transaction somebody else owns and neither commits nor rolls back.

Their abstract base follows the same cleanup: `JobStoreSupport` is **`AdoJobStoreBase`**, so
`LocalTransactionJobStore : AdoJobStoreBase` says what it is instead of carrying the Java/Spring
`*Support` idiom the listener bases already shed. For the same reason
`SimplePropertiesTriggerPersistenceDelegateSupport` — the base class for a custom trigger type's
persistence delegate — is **`SimplePropertiesTriggerPersistenceDelegateBase`**. Both are abstract
types: they are never spelled in configuration strings and never stored as `$type` values, so there
is no fallback to need — a subclass updates the name in its base list and recompiles.

**Configuration naming either as a string keeps working.** `quartz.jobStore.type` is the one type name
almost every persistent configuration spells out, so both old names resolve through the same fallback as
the [renamed namespaces](#quartz-spi-and-quartz-simpl-were-renamed), with a warning telling you what to
write instead:

```text
# both of these resolve, the first with a warning
quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz
quartz.jobStore.type = Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz
```

Code that names the type — `UsePersistentStore<JobStoreTX>()`, a subclass, a `typeof` — has to be updated.
`UsePersistentStore()` with no type argument already picks the right store and needs no change.

### The vocabulary follows

The "non-managed TX" phrasing went with the old names. The members it appeared in are protected, so this
only reaches a `AdoJobStoreBase` subclass:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `GetNonManagedTXConnection` | `GetLocalTransactionConnection` |
| `ExecuteInNonManagedTXLock` | `ExecuteInLocalTransactionLock` |
| `RetryExecuteInNonManagedTXLock` | `RetryExecuteInLocalTransactionLock` |

`ExternalTransactionJobStore.OpenConnection` is `AdoJobStoreOptions.OpenConnection` now. The store
property was the one piece of store configuration outside the options system — settable only by
resolving `IJobStore` and downcasting, after the container had already built the store, with no
guarantee the write landed before `Initialize` ran:

```diff
- ((ExternalTransactionJobStore) store).OpenConnection = true;
+ services.AddQuartz(q => q.UsePersistentStore<ExternalTransactionJobStore>(store =>
+     store.ConfigureStore(options => options.OpenConnection = true)));
```

## Nine `Execute…Lock` overloads became four members

`AdoJobStoreBase` had nine overlapping ways to run a callback under a lock, three of which existed only to
adapt a `void` callback and did so by returning `object` — a value that was always `null` and was never
read. Optional parameters replace the ladder:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `ExecuteWithoutLock<T>(txCallback, ct)` | unchanged |
| `abstract ExecuteInLock<T>(lockName, txCallback, ct)` | `ExecuteInLock<T>(SchedulerLock? lockKind, txCallback, ct)` |
| `ExecuteInLock(lockName, txCallback, ct)` → `ValueTask<object>` | `ExecuteInLock(SchedulerLock? lockKind, txCallback, ct)` → `ValueTask` |
| `ExecuteInNonManagedTXLock` ×4 | `ExecuteInLocalTransactionLock<T>(SchedulerLock? lockKind, txCallback, txValidator = null, requestorId = null, ct)` plus one `ValueTask`-returning convenience |
| `RetryExecuteInNonManagedTXLock` ×2 | `RetryExecuteInLocalTransactionLock<T>(SchedulerLock? lockKind, txCallback, requestorId = null, ct)` plus one `ValueTask`-returning convenience |

A call that passed a cancellation token positionally after the validator now has to name it
(`cancellationToken: cancellationToken`), because the parameters between them became optional.
`RecoverJobs(CancellationToken)` returns `ValueTask` rather than `ValueTask<bool>` — the `bool` was the
constant `true` that the old void adapter produced.

## Locks are a `SchedulerLock`, not a string

`ILockHandler` took the lock as a `string` whose only two legal values were `"TRIGGER_ACCESS"` and
`"STATE_ACCESS"`; anything else threw at run time. `Quartz.Impl.AdoJobStore.SchedulerLock` is now that
type, with members `TriggerAccess` and `StateAccess`.

```diff
- ValueTask<bool> ObtainLock(Guid requestorId, ConnectionAndTransactionHolder? conn, string lockName, CancellationToken ct = default);
- ValueTask ReleaseLock(Guid requestorId, string lockName, CancellationToken ct = default);
+ ValueTask<bool> AcquireLock(Guid requestorId, ConnectionAndTransactionHolder? conn, SchedulerLock lockKind, CancellationToken ct = default);
+ ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken ct = default);
```

`AdoJobStoreBase.LockTriggerAccess` and `LockStateAccess` are gone with the strings they held. **Nothing
changes in the database**: the `LOCK_NAME` column still holds `TRIGGER_ACCESS` and `STATE_ACCESS`, the
conversion happens where the row is written, and a 4.0 node contends for the same rows as a 3.x one. The
same applies to `Quartz.Extensions.Redis`, whose keys keep their `…:TRIGGER_ACCESS` spelling.

`DbLockHandler.ExecuteSql` still receives the stored name as a `string` — that parameter really is the value
bound into the statement.

## The job store configuration is read-only, and no longer a public currency

Twenty-odd `AdoJobStoreBase` properties duplicated `AdoJobStoreOptions` and `QuartzSchedulerOptions` with
a public setter. Writing one after the store had started did nothing useful in most cases and quietly
diverged from the options everything else reads — and reading store configuration through a downcast
`IJobStore` was a second currency for options that are already injectable. They are get-only and no
longer public: the mirrors a derived store legitimately reads while doing its work are `protected`
(`AcquireTriggersWithinLock`, `CanUseProperties`, `ClusterCheckinMisfireThreshold`,
`DataSource`, `DoubleCheckLockMisfireHandler`, `LockOnInsert`, `MaxMisfiresToHandleAtATime`,
`MaxTransientRetries`, `ObjectSerializer`, `SchemaProvisioning`, `SelectWithLockSql`, `TablePrefix`,
`TransientRetryInterval`, `TransactionIsolationLevel`, `UseDbLocks`), and the ones only the store's
own cluster and misfire machinery reads are internal (`AcceptEnlistedTransactions`,
`ClusterCheckinInterval`, `DbRetryInterval`, `InstanceId`, `InstanceName`, `UseBackgroundThreads`,
`MisfireHandlerFrequency`, `RetryableActionErrorLogThreshold`). `Clustered`, `SupportsPersistence` and
`EstimatedTimeToReleaseAndAcquireTrigger` stay public — they are `IJobStore` members.

`EstimatedTimeToReleaseAndAcquireTrigger` did change type, though, on `IJobStore` itself and so on every
store: it was a `long` holding milliseconds and is a `TimeSpan`. The unit used to live in the member's
documentation, which is where a wrong answer hides — the scheduler thread subtracts this from the time it
is prepared to sleep, so a store that meant seconds simply idled wrong. A store returning `70` returns
`TimeSpan.FromMilliseconds(70)`; the compiler catches the port, so the only way to get it wrong now is to
reach for `TimeSpan.FromTicks`.

Code that read configuration off the store resolves `IOptions<AdoJobStoreOptions>` instead, and
configuring goes where it always did in 4.0:

```diff
- var store = new JobStoreTX(...) { Clustered = true, MaxTransientRetries = 5 };
+ services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
+ {
+     options.Clustered = true;
+     options.MaxTransientRetries = 5;
+ })));
```

`MisfireThreshold` keeps a **public getter** on both `AdoJobStoreBase` and `RAMJobStore` — it is read on
every misfire pass rather than only at startup, so a store that wraps one needs to see it — but its setter
is `internal` like the rest. Set it on the options type: `UsePersistentStore(store => store.ConfigureStore(o =>
o.MisfireThreshold = …))`, or `UseInMemoryStore(o => o.MisfireThreshold = …)`.

Two properties that nothing read are gone rather than made read-only: `DriverDelegateType` (the delegate is
injected, not loaded from a type name here) and `DontSetAutoCommitFalse` (never consulted).
`AdoJobStoreOptions.DontSetAutoCommitFalse` went with the store property: no code path ever read it and no
configuration key ever set it, so an application that set it was configuring nothing.
`LastCheckin` is internal — cluster check-in bookkeeping a subclass has no business in — and
`LogWarnIfNonZero` is gone: its callers raise source-generated events instead, at the level its name always
claimed, as [Every message carries an event id](#every-message-carries-an-event-id) describes. The
`[TimeSpanParseRule]` attributes on these properties are gone too; they are read only when a component's
settings arrive as strings, which for this store they no longer do.

### `AdoJobStoreBase`'s overridable surface is a decision now

::: warning
This section describes an intermediate state. The curation below happened first; then the whole
hierarchy went internal, so none of it is a seam any more — see
[The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) for
what replaced each of these. It is kept because it explains what the store now does *not* offer, and
why that was never much of a loss.
:::

The base store had 56 `protected virtual` members — every internal step of every operation was an
override point, which 4.0 would have frozen as a behavior contract by default. The seam was curated
instead, down to what derived stores demonstrably used:

* **Lifecycle** — `Initialize` and `Shutdown` (the two shipped stores override both).
* **Connections and transactions** — `GetConnection`, `GetLocalTransactionConnection` and
  `ExecuteInLock<T>` (both abstract), and `IsTransient` for provider-specific transient-error
  classification. This is the seam an ambient-transaction store builds on.
* **Acquisition** — `AcquireNextTriggers`, `GetFiredTriggerRecordId`, and
  `CreateAcquisitionCriteria`, the seam for acquisition filtering (issue #2238): it builds the
  `TriggerAcquisitionCriteria` the delegate is asked with, and a derived store narrows what its node
  picks up by returning `base.CreateAcquisitionCriteria(request) with { … }`.

Everything else — the per-entity add/get/delete internals, the pause/resume walkers, the fire path, the
cluster check-in and recovery passes, the connection cleanup helpers — is non-virtual. Those members
were virtual because the Java port made everything virtual, not because subclassing them was supported;
overriding one changed the store's internal call order in ways no test covered. The seven conn-taking
`PauseTrigger`/`PauseTriggerGroup`/`PauseAll`/`ResumeTrigger`/`ResumeTriggers`/`ResumeAll`/
`RecoverMisfiredJobs` overloads are `protected` for the same reason: nothing outside the store can
obtain the `ConnectionAndTransactionHolder` they demand. The dialect seam — statement text, paging,
parameter binding — was never here; it belongs to `StdAdoDelegate`.

## The semaphores are lock handlers

A semaphore is a counted permit. This thing is a mutual-exclusion lock over a named row — one holder,
re-entrant per connection, released on the same connection — so "semaphore" named one implementation
strategy rather than the thing itself, and the name came from the Java port rather than from anything it
does. Every type in the family says `LockHandler` now, which is what the builder method
(`UseLockHandler`), the configuration key (`quartz.jobStore.lockHandler.type`) and the how-to have said
all along:

| 3.x | 4.0 alpha | 4.0 |
|-----|-----------|-----|
| `ISemaphore` | `ISemaphore` | `ILockHandler` |
| `DBSemaphore` | `DbSemaphore` | `DbLockHandler` |
| `StdRowLockSemaphore` | `SelectForUpdateSemaphore` | `SelectForUpdateLockHandler` |
| `PostgreSQLRowLockSemaphore` | `PostgreSqlSelectForUpdateSemaphore` | `PostgreSqlSelectForUpdateLockHandler` |
| `UpdateLockRowSemaphore` | `UpdateRowSemaphore` | `UpdateRowLockHandler` |
| `UpdateLockRowSemaphoreMOT` | `SqlServerMemoryOptimizedUpdateRowSemaphore` | `SqlServerMemoryOptimizedUpdateRowLockHandler` |
| `SimpleSemaphore` | `SimpleSemaphore` (internal) | `InProcessLockHandler` (internal) |
| — | `SQLiteSemaphore` (internal) | `SqliteLockHandler` (internal) |
| `RedisSemaphore` | `RedisSemaphore` | `RedisLockHandler` |
| — | `SemaphoreContext` | `LockHandlerContext` |
| `ISemaphore.ObtainLock` | `ISemaphore.ObtainLock` | `ILockHandler.AcquireLock` |

`ReleaseLock` and `RequiresConnection` keep their names — the verb pairs with `AcquireLock`, and the
property was never about permits.

A `quartz.jobStore.lockHandler.type` naming any of the old types — either generation of them — still
resolves, with a warning.

The four database lock handlers had also named the same concept three different ways in 3.x:
`StdRowLockSemaphore` and `UpdateLockRowSemaphore` transposed the same two words for two strategies that
diverge only under load, and `MOT` was an acronym expanded nowhere. They are named for the SQL they issue
now, which is the only thing that distinguishes them.

* The public static SQL fields settled on one convention and became `protected const`, because nothing
  outside the class hierarchy that owns them ever read them and every interpolation hole in them is
  itself a constant: `SelectForUpdateLockHandler.SelectForLock` / `.InsertLock` keep their member names,
  and `UpdateLockRowSemaphore.SqlUpdateForLock` / `.SqlInsertLock` are `UpdateRowLockHandler.UpdateForLock`
  / `.InsertLock`.
* `DbLockHandler.Sql` is `LockSql`, saying which of the two statements it holds and pairing with
  `InsertSql`. Both are get-only and arrive through the constructor. They were `protected` settable,
  which let a subclass swap a statement after the table prefix had already been folded into it — the
  select and the insert backing the same lock could end up naming different tables. A subclass that
  needs its own insert statement passes it up:

  ```diff
    public MyRowLockSemaphore(string tablePrefix, string schedulerName, string? selectWithLockSql, IDbProvider dbProvider)
  -     : base(tablePrefix, schedulerName, selectWithLockSql, dbProvider)
  - {
  -     InsertSql = MyInsertLock;
  - }
  +     : base(tablePrefix, schedulerName, selectWithLockSql, MyInsertLock, dbProvider)
  + {
  + }
  ```

## A lock handler is told which scheduler it locks for

`ITablePrefixAware` is gone. A lock handler used to learn its scheduler's identity through that get/set
property pair, which the store property-injected after construction — and a handler that never touches a
SQL table (the Redis one, say) still had to carry a dead `TablePrefix` property in order to be told its
own scheduler's name. `ILockHandler` has a single initialization seam now:

```csharp
public interface ILockHandler
{
    void Initialize(LockHandlerContext context)
    {
    }

    // AcquireLock (was ObtainLock) / ReleaseLock / RequiresConnection otherwise unchanged
}
```

`LockHandlerContext` carries the identity a handler locks under — `SchedulerName`, `InstanceId` and
`TablePrefix` — and the environment it locks in: `TimeProvider`, the clock a handler backs off on
between attempts, and `CommandTimeout`, how long its statements may run. The job store calls
`Initialize` once, before the handler is used, whether the store built the handler itself or the
container or configuration supplied it. The default implementation does nothing, so a handler that does
not key its locks by scheduler identity implements nothing. `DbLockHandler` overrides it to re-expand its
statements with the table prefix and rebuild its accessor with the timeout; its `TablePrefix` and
`SchedulerName` properties are read-only now, and it exposes the clock as a `protected TimeProvider`.

Both shipped row-lock handlers wait on that clock rather than on wall time, so their retry behaviour is
finally testable — the backoff was a `Task.Delay(TimeSpan, CancellationToken)`, which meant the only way
to watch a retry was to sit out the real second, and neither retry loop had a test. `UpdateRowLockHandler`
picked up a `RetryPeriod` in the process: its backoff was a literal `TimeSpan.FromSeconds(1)`, so it
ignored `quartz.jobStore.lockHandler.retryPeriod` while `SelectForUpdateLockHandler` beside it honoured
the same key.

`SelectForUpdateLockHandler.MaxRetry` and `.RetryPeriod` are `init`-only. How many times a contended lock
is retried is fixed for the life of the handler, and a setter invited changing it mid-retry. The flat
`quartz.jobStore.lockHandler.maxRetry` / `.retryPeriod` keys still reach them: the property bridge writes
the handler by reflection, and an init accessor is an ordinary setter to reflection. Code that assigned
them after construction moves the values into the object initializer:

```diff
- var lockHandler = new StdRowLockSemaphore(dbProvider);
- lockHandler.MaxRetry = 5;
+ var lockHandler = new SelectForUpdateLockHandler(dbProvider) { MaxRetry = 5 };
```

A custom handler that implemented `ITablePrefixAware` replaces the property pair with `Initialize` and
reads the same values from the context:

```diff
- public sealed class ConsulSemaphore : ISemaphore, ITablePrefixAware
+ public sealed class ConsulLockHandler : ILockHandler
  {
-     public string TablePrefix { get; set; } = "";
-     public string? SchedName { get; set; }
+     public string? SchedulerName { get; private set; }
+
+     public void Initialize(LockHandlerContext context)
+     {
+         SchedulerName = context.SchedulerName;
+     }
```

With the setters gone, the configuration keys that reached them by property injection went too:
`quartz.jobStore.lockHandler.tablePrefix` and `quartz.jobStore.lockHandler.schedName` are rejected with
advice naming this seam. `schedName` is the spelling a file carried over from 3.x contains, because the
key was derived from the `ITablePrefixAware.SchedName` property it wrote;
`quartz.jobStore.lockHandler.schedulerName`, which is what the 4.x property name suggests, is rejected
with the same advice rather than reported as a typo. The store hands the handler its own
`quartz.jobStore.tablePrefix` value, so the lock rows follow the store's table prefix without separate
configuration.

## A cancelled acquire throws, and `false` means only re-entry

`ILockHandler.AcquireLock` returns a release obligation rather than a report of success: `true` says
this call took the lock and its caller has to give it back, `false` says the calling context already
held it and must not. That was the rule in 3.x too, and the store still reads the answer that way — what
was never written down is that **re-entry is the only thing `false` may mean**, so an acquire that did
not take the lock throws: `LockException` when it was refused, `OperationCanceledException` when the
token fired.

`SimpleSemaphore.ObtainLock` and `RedisSemaphore.ObtainLock` swallowed the cancellation and answered
`false`, which the store reads as *already held, do not release* — it would then run the guarded
operation with no lock and release nothing on the way out. Their 4.0 counterparts throw. Nothing ran
unlocked in a shipped configuration, because both answer `false` to `RequiresConnection` and the
connection open immediately after observes the same token, but that is statement ordering rather than a
guarantee, and a handler of your own inherits the hole silently.

A custom handler that caught `OperationCanceledException` around its wait rethrows instead, and gives
back anything it had taken before the exception escapes:

```diff
  try
  {
      await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
  }
  catch (OperationCanceledException)
  {
-     return false;
+     throw;
  }
```

## A job store of your own can join your transaction

`AdoJobStoreBase` was public and abstract at the time, but everything needed to honour an enlisted
transaction was
`private protected`, so a store outside this assembly could not take part in one — it could only open a
connection of its own while the caller believed the scheduling was inside their transaction.

`GetEnlistedConnection` is now `protected`. A `GetLocalTransactionConnection` override starts with it, and
gets `null` back when enlisted transactions are not accepted or nothing is enlisted:

```csharp
protected override async ValueTask<ConnectionAndTransactionHolder> GetLocalTransactionConnection(
    CancellationToken cancellationToken = default)
{
    ConnectionAndTransactionHolder? enlisted = await GetEnlistedConnection(cancellationToken);
    if (enlisted is not null)
    {
        return enlisted;
    }

    return await GetConnection(cancellationToken);
}
```

Everything that makes an enlistment safe to use happens inside it: the transaction is checked to be alive
and still current, the provider is checked to match, the connection is opened if it is not, and it is
booked out for the operation so two concurrent scheduler calls cannot share it. Cleaning the holder up
through `CleanupConnection` hands the booking back.

`ConnectionAndTransactionHolder` gained a matching public constructor,
`(DbConnection connection, DbTransaction? transaction, bool ownsResources)`, and a public `OwnsResources`,
for a store that borrows a connection from somewhere else entirely. Owning nothing means committing
nothing — `Commit`, `Rollback`, `Close` and `Dispose` all return without touching a borrowed connection.

`Commit(bool)` and `Rollback(bool)` are internal: when the unit of work commits is the job store's
decision, and the ADO.NET store makes it. `Close` stays public, for the store that borrowed the
connection and has to give it back.

## The driver delegate speaks in records

The six types `IDriverDelegate` hands back or takes in were mutable classes with settable properties, loose
`string` pairs where a key belongs, and — in one case — properties that were non-nullable but unassigned. They
are records now, and say what they hold.

| Type | What changed |
|---|---|
| `FiredTriggerRecord` | `sealed record`, `[Serializable]` dropped, `FireInstanceState` is a `StoredTriggerState` |
| `RecoverMisfiredJobsResult` | `sealed record`; the property is `EarliestNewTimeUtc`, matching its constructor argument |
| `DelegateInitializationArgs` | Renamed `DriverDelegateContext`; `sealed record` with `required` / `init` members, and `InstanceName` is `SchedulerName` — see [The initialization seams are context records](#the-initialization-seams-are-context-records) |
| `TriggerAcquireResult` | carries a `TriggerKey` instead of `TriggerName` + `TriggerGroup` |
| `TriggerStatus` | replaced by `StoredTriggerHeader`, returned by `SelectTriggerHeader` |
| `SchedulerStateRecord` | `sealed record` with a positional constructor `(string SchedulerInstanceId, DateTimeOffset CheckinTimestamp, TimeSpan CheckinInterval)` and `init`-only members; `[Serializable]` dropped, and the three properties are no longer `virtual`. Construction moves from `new SchedulerStateRecord { … }` to the constructor — all three values are read from one row, so none of them is optional |

`FiredTriggerRecord.FireInstanceState` was the last place raw `AdoConstants.State*` string comparisons
survived after the delegate's states were typed, in stale-acquired recovery and in cluster recovery. Its
always-populated members are `required` and non-nullable now — `FireInstanceId`, `FireInstanceState`,
`TriggerKey` and `SchedulerInstanceId` — and `JobKey` stays nullable because an ACQUIRED row is written
before the job has been loaded. `[Serializable]` went with the class: nothing has serialized this record
since binary serialization was dropped.

`TriggerStatus` was a mutable class with a settable key, a `string` state and a name that said nothing.
`StoredTriggerHeader` is the storage-side counterpart of `TriggerHeader`:

```diff
- TriggerStatus? status = await Delegate.SelectTriggerStatus(conn, triggerKey, cancellationToken);
- bool blocked = AdoConstants.StatePausedBlocked == status.Status;
+ StoredTriggerHeader? status = await Delegate.SelectTriggerHeader(conn, triggerKey, cancellationToken);
+ bool blocked = status.State == StoredTriggerState.PausedBlocked;
```

It speaks `StoredTriggerState` rather than the reported `TriggerState`, because resuming a trigger has to
tell `PausedBlocked` from `Paused` and the reported state does not.

`FiredTriggerQuery` stays unpaged, deliberately, and now says so in its own doc comment: FIRED_TRIGGERS holds
one row per firing in flight, and every caller is a maintenance pass — recovery, cluster failover, blocked
state checks — that has to see the whole set. Handing one of those a page would leave the rest unrecovered.

## The initialization seams are context records

The ADO.NET store has three things it initializes after construction, and each of them used to say so
differently: a lock handler took a `LockHandlerContext`, a driver delegate took a bag called
`DelegateInitializationArgs`, and a trigger persistence delegate took three loose positional arguments.
All three take a context record now, and the records agree on what the scheduler's name is called:

| Seam | 4.0 |
|---|---|
| `ILockHandler.Initialize` | `LockHandlerContext` — unchanged |
| `IDriverDelegate.Initialize` | `DriverDelegateContext` (was `DelegateInitializationArgs`), whose `InstanceName` is `SchedulerName` |
| `ITriggerPersistenceDelegate.Initialize` | `TriggerPersistenceDelegateContext` (was `(string tablePrefix, string schedulerName, IDbAccessor dbAccessor)`) |

```diff
- public void Initialize(string tablePrefix, string schedulerName, IDbAccessor dbAccessor)
+ public void Initialize(TriggerPersistenceDelegateContext context)
  {
-     TablePrefix = tablePrefix;
-     SchedulerName = schedulerName;
-     DbAccessor = dbAccessor;
+     TablePrefix = context.TablePrefix;
+     SchedulerName = context.SchedulerName;
+     DbAccessor = context.DbAccessor;
  }
```

`DelegateInitializationArgs.InstanceName` held the scheduler name, which is the confusion
[One term for a scheduler instance](#one-term-for-a-scheduler-instance) settled everywhere else, so it is
`SchedulerName` — matching `LockHandlerContext.SchedulerName` beside it. `InstanceId` keeps its name and its
meaning: the node's identity within the cluster.

Neither of the two delegate seams has a default implementation, unlike `ILockHandler.Initialize`. A lock
handler that ignores its context is a legitimate handler — one that does not key its locks by scheduler
identity has nothing to read. A delegate that ignores its context has no table prefix, no provider and no
accessor, so a do-nothing default would only move the failure from startup to the first statement.

Both stay two-phase rather than moving to constructor injection, because `InstanceId` may be *generated*
rather than configured: the store is built before the id generator has run, so the value does not exist
when the container constructs the delegate.

## `ValidateSchema` is part of `IDriverDelegate`

Startup schema validation was a `StdAdoDelegate` method the job store reached by type test, so a delegate
that was not a `StdAdoDelegate` silently skipped the check that `quartz.jobStore.performSchemaValidation`
had asked for. It is an interface member now, and every delegate participates:

```csharp
ValueTask<int> ValidateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);
```

`CreateSchema` sits beside it, for the `SchemaProvisioning.CreateIfMissing` the flag it replaced could not
express — see [`PerformSchemaValidation` became `SchemaProvisioning`](#performschemavalidation-became-schemaprovisioning-which-has-a-third-position).

A delegate of your own that derives from `StdAdoDelegate` inherits the implementation and can extend it to
cover tables of its own. One written against the interface directly has to implement it; returning `0`
without checking anything restores the old skip.

Three more `StdAdoDelegate` methods moved onto the interface for the same reason — the store called them
through a type test, so a delegate that was not a `StdAdoDelegate` quietly did not participate:

| Member | What the store does with it |
|---|---|
| `RepinTriggersFromDeadNode(conn, oldPreferredNode, newPreferredNode, ct)` | Steals a dead node's pinned triggers during cluster recovery |
| `UpdateMisfireOriginalFireTime(conn, triggerKey, fireTime, ct)` | Records the fire time a misfire displaced |
| `ClearMisfireOriginalFireTime(conn, triggerKey, ct)` | Clears it once the trigger fires normally again |

All three are **abstract** on `IDriverDelegate`, not default interface members, because a store that
silently does nothing for them loses node affinity on failover or reports the wrong original fire time —
failures that surface far from their cause. A delegate deriving from `StdAdoDelegate` inherits all three.

## The optional columns are required, so the probes are gone

3.x asked the database at startup whether `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP` and
`PREFERRED_NODE` were present, and switched the matching feature off when they were not. **4.x
requires all of them**, so there is no question left to ask, and the members that asked it are gone
from `StdAdoDelegate`:

| Removed | What it did |
|---|---|
| `HasMisfireOriginalFireTimeColumn`, `HasExecutionGroupColumn`, `HasPreferredNodeColumn` | Reported the result of the matching probe |
| `SupportsMisfireOriginalFireTimeColumn`, `SupportsExecutionGroupColumn`, `SupportsPreferredNodeColumn` | Ran the probe, swallowing the provider error that meant "no such column" |
| `VerifyTriggersTableReachable` | Told a genuinely missing column apart from a database that was momentarily unreachable, so a transient failure did not disable a feature for the life of the process |

**The upgrade path is the schema migration, and it is mandatory.** Run
`schema_30_to_40_upgrade_<dialect>.sql` from
[database/migrations/4.0/](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0)
before pointing 4.x at a 3.x database — [Database Schema Migration](#database-schema-migration) lists
the columns and what each script does. Skipping it no longer degrades gracefully, and it does not
fail tidily either: [`ValidateSchema`](#validateschema-is-part-of-idriverdelegate) only checks that
each table can be queried, not which columns it has, so what you reach instead is a provider-level
missing-column error from the first statement that names one. That happens almost immediately —
loading a trigger projects all four columns, and storing one names three of them.

A delegate of your own has nothing to override here any more. If it derived from `StdAdoDelegate` and
overrode a probe — to hard-code `true` against a schema you knew was current, say — delete the
override; the base class no longer declares it.

### The three extra acquisition SQL hooks went with them

Because the columns were optional, 3.x carried four versions of the trigger acquisition statement and
chose between them at run time from the probe results: plain, with `EXECUTION_GROUP`, with
`PREFERRED_NODE`, and with both. Each had its own `protected virtual` hook, and all six dialect
delegates overrode all four to hang their own row-limiting clause off the end. Three of the four are
gone from `StdAdoDelegate` and from `FirebirdDelegate`, `MySQLDelegate`, `OracleDelegate`,
`PostgreSQLDelegate`, `SQLiteDelegate` and `SqlServerDelegate`:

* `GetSelectNextTriggerToAcquireWithExecutionGroupSql(int maxCount)`
* `GetSelectNextTriggerToAcquireWithPreferredNodeSql(int maxCount)`
* `GetSelectNextTriggerToAcquireWithPreferredNodeOnlySql(int maxCount)`

With the columns required there is one statement — `StdAdoConstants.SqlSelectNextTriggerToAcquire`,
which always projects `EXECUTION_GROUP` and always carries the preferred-node filter — and one hook,
which is the one that was already there:

```csharp
protected virtual string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
```

Its parameter is new: `TriggerAcquisitionSqlShape` carries everything about an acquisition attempt
that changes the statement's text — `MaxCount`, and `ExcludedJobTypeBucket` for the job-type
exclusion clause — so the next acquisition dimension is a property on the record rather than another
parameter here. **A dialect delegate of your own should delete the three removed overrides**; if the
one it keeps exists only to hang a row limit off the statement, delete that too and say the limit
once — see [Row limiting is a slot, not a splice](#row-limiting-is-a-slot-not-a-splice).

The node-affinity parameters the statement now always carries are bound for you by the protected
`AddPreferredNodeParameters(cmd, liveNodeCutoff)`, so an override that rewrites the statement text
still does not have to know their names or the order they are bound in. The cutoff parameter is a
`DateTimeOffset`; the binder converts it to the stored tick value itself.

### Row limiting is a slot, not a splice

Two statements come in a row-limited form — trigger acquisition and the misfire scan — and each of the
six dialects used to produce its own by editing the finished SQL: `SqlServerDelegate` cut the first
six characters off and pasted `SELECT TOP n` back on, `OracleDelegate` wrapped the whole thing in a
`rownum` filter, and the rest appended `LIMIT n` or `ROWS n`. Correct, and correct only while the
statement began with exactly `SELECT` — which nothing enforced — and repeated once per statement per
dialect, each with its own `count == -1` test.

A dialect now says where its clause goes, once, and the statement is built with it already there:

```csharp
protected virtual SqlRowLimit GetRowLimit(int count)
```

`SqlRowLimit` is new and names the three placements: `InProjection("TOP", count)` for SQL Server,
`AtStatementEnd("LIMIT", count)` for PostgreSQL, MySQL and SQLite, `AtStatementEnd("ROWS", count)`
for Firebird, `InEnclosingSelect("rownum", count)` for Oracle, and `Unlimited` for a database that
cannot limit rows, which is what `StdAdoDelegate` returns. `count` is never the `-1` sentinel: that is
turned into `Unlimited` before the member is called.

`GetSelectNextTriggerToAcquireSql(shape)` and `GetSelectMisfiredTriggersToRecoverSql(count)` are still
`protected virtual`, and still the seam for a statement that needs something a row limit cannot say —
`MySQLDelegate` overrides both for its `FORCE INDEX` hint. The five other dialect delegates no longer
override either.

**If your dialect delegate overrides one of the two only to add a row limit, replace both overrides
with a single `GetRowLimit`.** Nothing breaks if you do not: the two hooks behave as they always did,
and an override that appends its own clause still works. It just spells the same thing twice, against
a statement it does not own.

One shipped statement changed as a result. `MySQLDelegate`'s misfire scan carries its
`FORCE INDEX (IDX_…_T_NFT_ST_MISFIRE)` hint for an unlimited sweep as well now; it used to lose the
hint in exactly that case, because the hint and the row limit shared one early return. Which index
the sweep should read does not depend on how many rows it returns, and the unlimited sweep is the one
that reads the most.

## The connection manager is gone

`DBConnectionManager` was a name-keyed registry of `IDbProvider`s, reached through a process-wide
`Instance`. The container is that registry now: a scheduler's provider is registered under the
scheduler's own name as the service key, and the job store is handed the one it was built with. The
manager was left holding a copy of what the container already knew, which nothing read back — and two
schedulers whose data sources happened to share a name silently overwrote each other in it.

`IDbConnectionManager`, `DbConnectionManager` and `DBConnectionManager.Instance` are removed, with no
replacement type. The two things they were used for have separate answers.

**Registering a connection provider of your own** is a store configuration call. `UseConnectionProvider`
is where the `quartz.dataSource.<name>.connectionProvider.type` key lands too, so the configuration and
the code spelling say the same thing:

```diff
- DBConnectionManager.Instance.AddConnectionProvider("default", new MyDbProvider());
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider<MyDbProvider>()));
```

```diff
- DBConnectionManager.Instance.AddConnectionProvider("default", myProvider);
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider(_ => myProvider)));
```

The provider replaces whichever one the database choice registered, in either order — `UseSqlServer`
before or after `UseConnectionProvider` gives the same result — and it belongs to this scheduler alone.
It also names the data source for you, so a store configured this way needs no `UseDataSource` call.

**Reading a provider back out** is container resolution. The default scheduler's provider is unkeyed and
a named scheduler's is keyed by its name:

```diff
- var provider = DBConnectionManager.Instance.GetConnectionProvider("default");
+ var provider = serviceProvider.GetRequiredService<IDbProvider>();
+ var reporting = serviceProvider.GetRequiredKeyedService<IDbProvider>("reporting");
```

The manager's other three members have no equivalent, because each was one call on the provider:
`GetConnection(name)` is `provider.CreateConnection()`, `GetDbMetadata(name)` is `provider.Metadata`, and
`Shutdown(name)` is `provider.Shutdown()`.

`IDbProvider` itself is constructor-shaped: `Initialize()` is gone (every implementation resolved its
driver description during construction, so the member was an empty ritual), and `ConnectionString` is
get-only — it arrives through the implementation's constructor, and a provider is fully usable once
constructed. A custom `IDbProvider` deletes its empty `Initialize` and its `ConnectionString` setter.

## The isolation level is an isolation level

`TxIsolationLevelSerializable` was a `bool`, so the only two things a configuration could say were
"serializable" and "whatever Quartz picks". `Snapshot` — which is what a SQL Server deployment usually
wants, and which reads without blocking writers — was not expressible at all, and neither were
`RepeatableRead` or a deliberate `ReadUncommitted`.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `AdoJobStoreOptions.TxIsolationLevelSerializable = true` | `AdoJobStoreOptions.TransactionIsolationLevel = IsolationLevel.Serializable` |
| `TxIsolationLevelSerializable = false`, or unset | leave `TransactionIsolationLevel` unset |
| — | `TransactionIsolationLevel = IsolationLevel.Snapshot`, and every other `System.Data.IsolationLevel` |

```diff
- store.Configure(options => options.TxIsolationLevelSerializable = true);
+ store.ConfigureStore(options => options.TransactionIsolationLevel = IsolationLevel.Serializable);
```

`quartz.jobStore.txIsolationLevelSerializable` is still read: `true` becomes `Serializable`, and `false`
leaves the level unset rather than becoming an explicit `ReadCommitted` — the flag's `false` was the
absence of a choice, and things downstream read that absence. The typed configuration binds the enum by
name, so `"Quartz:JobStore:TransactionIsolationLevel": "Snapshot"` works with no further plumbing.

Unset means `ReadCommitted`, which is Quartz's default rather than the provider's. Provider defaults
differ — MySQL's is repeatable read — so inheriting them would have changed how the store behaves
depending on which database it happened to be talking to.

Two behaviours are unchanged. SQLite is forced to `Serializable` whatever this says, because concurrent
SQLite transactions at a lower level fail with "database is locked". And the setting applies only to
connections the job store opens itself: an operation running on a connection the application enlisted
uses the level that transaction was begun at, which the store still warns about at startup.

## `RAMJobStore` is sealed

Every public method of the in-memory store was `virtual`, which invited overriding operations that hold its
lock, mutate its indexes in a fixed order and raise listener notifications after releasing the lock — none of
which is a documented contract, and all of which the overriding code has to preserve. A job store is written
against `IJobStore`; the implementations Quartz ships are not base classes.

`RAMJobStore` is `sealed`, its `virtual`s are gone, and `GetFiredTriggerRecordId` is private. A store that
wants the in-memory behaviour plus something of its own wraps it, deriving from the new
`Quartz.Impl.DelegatingJobStore`:

```diff
- public class SlowJobStore : RAMJobStore
+ public sealed class SlowJobStore : DelegatingJobStore
  {
      public SlowJobStore(ILoggerFactory loggerFactory, ISchedulerSignaler signaler, TimeProvider timeProvider)
-         : base(loggerFactory, signaler, timeProvider)
+         : base(new RAMJobStore(loggerFactory, signaler, timeProvider))
      {
      }

      public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
          TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
      {
          List<IOperableTrigger> triggers = await base.AcquireNextTriggers(request, cancellationToken);
          await Task.Delay(10, cancellationToken);
          return triggers;
      }
  }
```

`UsePersistentStore<TStore>()` and `quartz.jobStore.type` take the wrapping store exactly as they took the
derived one; the `Quartz.Examples.AspNetCore` sample's `CustomJobStore` shows the whole shape.

`MisfireThreshold` is readable here as it is on `AdoJobStoreBase` — it is read on every misfire pass rather
than only at startup — but the setter is `internal`. Configure it with
`UseInMemoryStore(o => o.MisfireThreshold = …)`.

### `DelegatingJobStore` decorates a store

`IJobStore` has around fifty members, so hand-writing a forwarder for the sake of one of them is a lot of
code that only has to be revisited every time the interface changes. `Quartz.Impl.DelegatingJobStore` is the
store-level counterpart of `DelegatingScheduler`: a `public class` that takes the store to wrap as its
constructor argument, forwards every `IJobStore` member to it, and declares each one `virtual` so a derived
store overrides only what it changes. The wrapped store is available to derived types as `InnerJobStore`.

It is the supported way to add logging, metrics, tenant routing or fault injection to a store — including a
sealed one such as `RAMJobStore`. A store that keeps scheduling data somewhere new implements `IJobStore`
directly instead; nothing forces it through this base.

`DelegatingScheduler` gained the same shape: every one of its members is `virtual` now, so a scheduler
decorator overrides only what it changes instead of shadowing members with `new` — which compiled, and
then silently failed to intercept calls made through `IScheduler`.

## Jobs take a CancellationToken

`IJob.Execute` now takes the cancellation token as a parameter alongside the context:

```diff
- public async ValueTask Execute(IJobExecutionContext context)
+ public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
```

It is the *same* token as `IJobExecutionContext.CancellationToken`, which still works — so a job body that already
reads the token off the context needs no further change. The parameter exists because a token on a context property
is easy to never notice, and a job that never notices it cannot be interrupted by `IScheduler.Interrupt` and will
hold up shutdown until it finishes on its own.

The practical benefit is that the compiler now helps. With the token as a parameter, the built-in `CA2016` analyzer
flags every `await` inside a job that fails to pass it on:

```diff
  public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
  {
-     await httpClient.GetAsync(url);              // CA2016: forward the cancellationToken parameter
+     await httpClient.GetAsync(url, cancellationToken);
  }
```

When adding this to Quartz's own jobs it found nine places that were silently ignoring interruption, including the
sample that exists to demonstrate interruption.

## The job factory hands out a scope

`IJobFactory` is built around a `JobScope` rather than a bare `IJob`, and `NewJob` is now `CreateJob`:

```diff
- ValueTask<IJob> NewJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default);
- ValueTask ReturnJob(IJob job);
+ ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default);
+ ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default);
```

`JobScope` is a readonly struct holding the job plus an opaque `State` object. If your factory allocates something
in order to build a job — a DI scope, a connection, a tenant context — put it in `State` and you get it back in
`ReturnJob` instead of having to hide it inside the job instance:

```diff
- protected override IJob InstantiateJob(TriggerFiredBundle bundle, IScheduler scheduler)
- {
-     var scope = serviceProvider.CreateScope();
-     return new MyWrapperJob(scope, scope.ServiceProvider.GetRequiredService<MyJob>());
- }
+ protected override ValueTask<JobScope> CreateJobInstance(
+     TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default)
+ {
+     var scope = serviceProvider.CreateScope();
+     var job = ActivatorUtilities.CreateInstance<MyJob>(scope.ServiceProvider);
+     return new ValueTask<JobScope>(new JobScope(job, scope));
+ }
```

::: warning
Note that this example *activates* the job rather than resolving it from the scope. `SimpleJobFactory.ReturnJob`
disposes the job and then the state, so if you resolve the job from the scope — `GetRequiredService<MyJob>()` — the
scope disposes it too and your job's `Dispose` is called twice. Either activate it as above, or override `ReturnJob`
to skip the job when the container owns it, which is what `MicrosoftDependencyInjectionJobFactory` does.
:::

Keep `CreateJobInstance` non-`async` when its body is synchronous. An async state machine restores the caller's
execution context when its synchronous part returns, which would discard any `AsyncLocal` you set while building the
job — including anything `ConfigureScope` establishes for the job to read.

Because of that, **`IJobWrapper` has been removed** and `MicrosoftDependencyInjectionJobFactory` no longer wraps
your job. `IJobExecutionContext.JobInstance` and every listener now see the type you actually wrote.

`PropertySettingJobFactory.InstantiateJob` — the hook derived factories override — was replaced by the asynchronous
`CreateJobInstance` shown above. The old hook was synchronous even after `NewJob` became asynchronous, so a factory
that needed to do real work had to override `NewJob` outright and reimplement the property setting.

There is one job factory interface again. 3.x carried a second, internal `IJobWithAsyncReturnFactory` beside
`IJobFactory`, so that a factory returning its job asynchronously could be recognised without changing the
released interface; the asynchronous shape is `IJobFactory`'s own now, and the internal one is gone.

`SimpleJobFactory`'s `protected static Dispose(object?)` helper is `DisposeIfDisposable(object?, CancellationToken)`,
which is what it has always done: it disposes the argument only when the argument is disposable.

### Scheduler context entries are no longer injected into job properties

On 3.x, `PropertySettingJobFactory.BuildJobDataMap` merged the whole `SchedulerContext` underneath
the job's and trigger's data on every fire, so a context entry whose key matched a job property was
silently injected into the job. That stops: the map applied to the job is the trigger's data merged
over the job's, and nothing else. **This is a silent behavioral change** — a job that declared a
property fed from `scheduler.Context["ConnectionString"]` (or a `quartz.context.key.*` property)
keeps its default value and nothing throws. `MicrosoftDependencyInjectionJobFactory` derives from
`PropertySettingJobFactory`, so this covers the default DI path too.

The replacements:

```csharp
// read the context where it lives…
public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    var connectionString = context.Scheduler.Context.GetString("ConnectionString");
    // …
}

// …or opt back into merging by overriding the hook, which is handed the scheduler for this reason
public class ContextMergingJobFactory : MicrosoftDependencyInjectionJobFactory
{
    protected override JobDataMap BuildJobDataMap(TriggerFiredBundle bundle, IScheduler scheduler)
    {
        var map = new JobDataMap((IDictionary<string, object?>) scheduler.Context);
        foreach (var pair in base.BuildJobDataMap(bundle, scheduler))
        {
            map[pair.Key] = pair.Value;
        }
        return map;
    }
}
```

The merge was also defective in ways the removal fixes: 3.x's DI integration seeded a service-provider
entry into every scheduler context, which no job has a property for, so every container-hosted fire
logged a property miss — and threw, with `PropertyMismatchBehavior.Throw`; and the factory
enumerated the context while plugins could still be writing to it. 4.0 seeds no such entry either —
see [The container is not in the scheduler context](#the-container-is-not-in-the-scheduler-context).

### One setting says what a property miss does

`PropertySettingJobFactory` had two independent booleans for one three-way decision, and their fourth
combination was unreachable: `ThrowIfPropertyNotFound` threw before `WarnIfPropertyNotFound` could log,
so setting both meant "throw". They are one `PropertyMismatchBehavior` property now.

| 3.x | 4.x |
|---|---|
| both `false` (the default) | `PropertyMismatchBehavior.Ignore` (the default) |
| `WarnIfPropertyNotFound = true` | `PropertyMismatchBehavior.Warn` |
| `ThrowIfPropertyNotFound = true` | `PropertyMismatchBehavior.Throw` |
| both `true` | `PropertyMismatchBehavior.Throw` — the warning was never reached |

```diff
- var factory = new PropertySettingJobFactory { ThrowIfPropertyNotFound = true };
+ var factory = new PropertySettingJobFactory { PropertyMismatchBehavior = PropertyMismatchBehavior.Throw };
```

`PropertyMismatchBehavior` is in `Quartz.Impl`, beside the factory. Neither boolean ever had a
`quartz.*` property key, so nothing in the legacy configuration bridge changes.

### The factory is set where the scheduler is built

`IScheduler.JobFactory` was a setter-only property, and it is gone from `IScheduler`, `StdScheduler`,
`DelegatingScheduler` and `HttpScheduler`. A job factory is part of how a scheduler is built, not something to swap
underneath a running one — and on `HttpScheduler` the setter only ever threw, since the jobs run in another process.

```diff
- scheduler.JobFactory = new MyJobFactory();
+ services.AddQuartz(q => q.UseJobFactory(new MyJobFactory()));
```

`UseJobFactory(IJobFactory)` is new on `IQuartzBuilder` — the generic `UseJobFactory<T>()` overload has
always been there, but an already-constructed factory had nowhere to go:

```csharp
// standalone
var builder = QuartzSchedulerBuilder.Create();
builder.UseJobFactory(new MyJobFactory());
var scheduler = await builder.BuildScheduler();
```

There is no by-hand path left: `QuartzScheduler` is internal, so the job factory is always configured through
the builder or the container.

#### When the factory's dependency does not exist yet

Setting `IScheduler.JobFactory` late was how 3.x code handled a factory that needed something the
application only has after startup — a connected bus, a tenant catalogue, an elected leader. Configuring
the factory at build time does not mean *resolving* its dependency at build time. Two shapes cover it:

**Resolve per firing.** `UseJobFactory<T>()` constructs the factory from the scheduler's own service
provider, so a factory that takes `IServiceProvider` can resolve whatever it needs inside `CreateJob`,
which runs once per firing rather than once per scheduler:

<!-- snippet: sample_migration_late_bound_job_factory_use -->
```csharp
services.AddSingleton<LateBound<IMessageBus>>();
services.AddQuartz(q => q.UseJobFactory<BusAwareJobFactory>());
```
<!-- endSnippet -->

**Hand it a holder.** When the dependency is not in the container at all — because whatever produces it
also runs at startup — register a holder the factory can read through, and fill it when the value exists.
That is the late-bound-holder pattern, and it is what the standalone `QuartzSchedulerBuilder` needs too,
since it builds its own container and there is nothing to add to afterwards:

<!-- snippet: sample_migration_late_bound_job_factory -->
```csharp
/// <summary>
/// Holds something the container cannot supply yet, so that a component built at startup can be
/// given the handle now and read the value later.
/// </summary>
public sealed class LateBound<T> where T : class
{
    private T? value;

    public T Value => value ?? throw new InvalidOperationException($"{typeof(T).Name} is not available yet.");

    public void Set(T instance) => value = instance;
}

public sealed class BusAwareJobFactory(IServiceProvider provider, LateBound<IMessageBus> bus) : IJobFactory
{
    public ValueTask<JobScope> CreateJob(
        TriggerFiredBundle bundle,
        IScheduler scheduler,
        CancellationToken cancellationToken = default)
    {
        IServiceScope scope = provider.GetRequiredService<IServiceScopeFactory>().CreateScope();

        // Read per firing rather than captured per scheduler, which is what makes a dependency that
        // only exists once the bus has connected reachable from a factory built long before it.
        IJob job = (IJob) ActivatorUtilities.CreateInstance(
            scope.ServiceProvider,
            bundle.JobDetail.JobType.Type,
            bus.Value);

        return new ValueTask<JobScope>(new JobScope(job, scope));
    }

    public ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default)
    {
        (scope.State as IServiceScope)?.Dispose();
        return default;
    }
}
```
<!-- endSnippet -->

Keep `CreateJob` synchronous. An `async` method restores the caller's `ExecutionContext` when it
resumes, which discards any `AsyncLocal<T>` the factory set while building the job — the job then runs
without the ambient state the factory established.

## Trigger fire times are properties

```diff
- DateTimeOffset? next = trigger.GetNextFireTimeUtc();
+ DateTimeOffset? next = trigger.NextFireTimeUtc;

- operableTrigger.SetNextFireTimeUtc(value);
+ operableTrigger.NextFireTimeUtc = value;

- if (trigger.GetMayFireAgain()) { … }
+ if (trigger.MayFireAgain) { … }
```

The three `Get` methods are gone — they spent a while as `[Obsolete]` forwarders on both `ITrigger` and
`TriggerBase` and have now been removed — so fix the call by deleting `Get` and `()`. The `Set` methods
likewise have no stand-in, because a method and a property setter cannot share a name.

A **custom trigger deriving from `TriggerBase`** overrides the `MayFireAgain` property now, because that
is the abstract member:

```diff
- public override bool GetMayFireAgain() => NextFireTimeUtc is not null;
+ public override bool MayFireAgain => NextFireTimeUtc is not null;
```

`CronTriggerImpl.CronExpression` also gained a getter — it was setter-only — and is typed `CronExpression?`,
which is what it always was underneath.

## The thread pool is asynchronous

Only relevant if you implement `IThreadPool` yourself:

```diff
- bool RunInThread(Func<Task> runnable);
- int BlockForAvailableThreads();
- void Initialize();
- void Shutdown(bool waitForJobsToComplete = true);
- string InstanceId { set; }
- string InstanceName { set; }
+ ValueTask<bool> TryRun(Func<ValueTask> action, CancellationToken cancellationToken = default);
+ ValueTask<int> WaitForAvailableThreads(CancellationToken cancellationToken = default);
+ ValueTask Initialize(CancellationToken cancellationToken = default);
+ ValueTask Shutdown(bool waitForJobsToComplete = true, CancellationToken cancellationToken = default);
+ ValueTask<bool> Drain(CancellationToken cancellationToken = default);
```

The two renamed methods used to block the calling thread on a semaphore, and the caller is the scheduler's own
asynchronous loop, so waiting for pool capacity tied up a thread. Use `WaitAsync` in your implementation.

### `Drain` is the shutdown that can be given a deadline

`Drain` stops the pool accepting work, waits for the work already running, and **reports** whether it
finished waiting or gave up. It is what `Shutdown(waitForJobsToComplete: true)` could never be: that
member's wait is unbounded by contract, and a caller that abandoned it by throwing would skip the job
store shutdown, the plugin shutdown and the listener notification that follow it. So `IScheduler.Shutdown`
now passes its own token down to `Drain` rather than `CancellationToken.None`, and a host stop that runs
out of its budget stops *waiting* rather than being stuck.

The token cancels the wait and nothing else. Running jobs are not cancelled — whether a shutting-down
scheduler interrupts its jobs is `ShutdownJobInterruption`'s decision, and it still defaults to never.

**Implementing it is optional.** A pool that does not override it gets a default implementation that calls
`Shutdown(waitForJobsToComplete: true, CancellationToken.None)` and returns `true`, which is exactly what a
3.x-shaped pool did. Overriding it is worth it when your pool can honour a deadline, and the one rule to
get right is what the barrier covers:

> `TryRun` is handed the *whole* of a job's execution, and the last act of that execution is the job store
> update that completes the trigger. A pool that waits for its work items therefore waits for those writes
> too. A count of executing jobs does not: the job listeners are told the job was executed before the store
> update is issued, so `NumberOfJobsExecutingHere` reads zero while a persistent store is still being
> written to. Do not build the barrier on it.

`TaskSchedulingThreadPool` implements both members over one asynchronous barrier, so `Shutdown` no longer
blocks a thread either — its wait is awaited rather than `CountdownEvent.Wait`-ed. A caller that never
awaited the returned `ValueTask` used to get the wait anyway; it has to await it now.

`TryRun`'s work item is a `Func<ValueTask>`: it was the one place an extensibility SPI still made you
write `async Task` in a surface that is `ValueTask` everywhere else, and the `Task`-shaped delegate
cost the dispatch path a `Task<Task>`/`Unwrap` round trip per fire. A custom pool changes the
parameter type; a lambda that returned `Task.CompletedTask` returns `ValueTask.CompletedTask`.

`InstanceId` and `InstanceName` are gone rather than moved: Quartz set them and nothing ever read them. If your
pool wants the scheduler's identity, take `IOptions<QuartzSchedulerOptions>` from the container.

`TaskSchedulingThreadPool.ThreadCount` was removed as well; it read and wrote `MaxConcurrency`, so use that
directly. **The `quartz.threadPool.threadCount` configuration key is unaffected** and still sets `MaxConcurrency`.

## Quartz.Spi and Quartz.Simpl were renamed

`Quartz.Spi` is now `Quartz.Extensibility`, and `Quartz.Simpl` merged into the existing `Quartz.Impl`. Both old
names were transliterations of `org.quartz.spi` and `org.quartz.simpl`. For source code this is a find-and-replace
over `using` directives that the compiler will walk you through. Quartz's own directory layout followed, so if you
read the source or port a patch between the branches, `src/Quartz/SPI/` is `src/Quartz/Extensibility/` and
`src/Quartz/Simpl/` is `src/Quartz/Impl/`.

Configuration is the part that would not have failed loudly, because it names types by string:

```diff
- quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
+ quartz.jobStore.type = Quartz.Impl.RAMJobStore, Quartz
```

**Existing configuration keeps working.** A type name naming a pre-4.0 namespace that no longer resolves is retried
under the new one, and a warning is logged naming both spellings. Treat that as a grace period rather than a promise.

The same fallback covers the assemblies that were merged into the core package, and composes with the namespace
rename, so a string naming both still resolves:

```diff
- quartz.serializer.type = Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson
+ quartz.serializer.type = Quartz.Impl.SystemTextJsonObjectSerializer, Quartz
```

Configuration is not the only place a type is named by string. `JOB_CLASS_NAME` holds whatever spelling the
version that wrote the row used, so a database carried over from 2.x or 3.x names jobs by namespaces and
assemblies that have since moved. **Those stored names now resolve through the same fallback**, with the same
warning naming both spellings, rather than through the runtime's lookup alone — a `Quartz.Job.NoOpJob, Quartz`
written years ago finds the type in `Quartz.Jobs` today. The column itself is left alone: reading a job never
rewrites what is persisted for it, so the fallback stays visible until you migrate the data. Previously such a
job started up and listed perfectly well, because a job's type is resolved lazily, and then failed with a
`TypeLoadException` the first time it fired.

### Other namespaces that moved

`Quartz.Spi` and `Quartz.Simpl` were not the only ones. 4.0 also settles the singular/plural split between a
namespace, its assembly and its package — where the package is `Quartz.Jobs`, the namespace is `Quartz.Jobs`
too — and empties the namespaces that held a single type or a handful of them. For source code each row is a
`using` directive; where a row says the old spelling still resolves, the same fallback and the same warning as
above cover configuration strings.

| 3.x namespace | 4.x namespace | Notes |
|---|---|---|
| `Quartz.Job` | `Quartz.Jobs` | Namespace, assembly and package now agree. A configuration string or a stored `JOB_CLASS_NAME` naming the old spelling still resolves, with a warning |
| `Quartz.Extensibility.IDirectoryProvider` | `Quartz.Jobs.IDirectoryProvider` | It exists for `DirectoryScanJob` alone, so it lives with it. It is resolved from `SchedulerContext` by key, never by type name. Its one member also changed shape: `GetDirectoriesToScan(JobDataMap)` returns `List<string>` rather than `IReadOnlyList<string>`, following the concrete-out convention, so an implementation returning an array or a `ToList()` result needs one edit |
| `Quartz.Logging`, `Quartz.Logging.LogProviders` | `Quartz.Diagnostics` | `LogProvider`, `DiagnosticHeaders` (now `ActivityTags`) and `OperationName` moved; `ILogProvider`, `LogContext`, `LogLevel`, the `Logger` delegate, `IJobDiagnosticData` and `LogProviders.LibLogException` went with LibLog. A `using Quartz.Logging;` no longer resolves at all, and nothing names these namespaces in configuration, so there is no fallback — see [Logging](#logging) |
| `Quartz.Plugin.History`, `Quartz.Plugin.Json`, `Quartz.Plugin.Management`, `Quartz.Plugin.Xml`, `Quartz.Plugin.TimeZoneConverter` | `Quartz.Plugins.*` | Same rule as the jobs: the packages are `Quartz.Plugins` and `Quartz.Plugins.TimeZoneConverter`. A `quartz.plugin.<name>.type` naming the old spelling still resolves, with a warning. The **configuration key** prefix is still `quartz.plugin.`, singular — it is not a namespace. `Quartz.Plugin.Interrupt` has no 4.x counterpart: its one type is gone — see [`JobInterruptMonitorPlugin` is retired; a job timeout is middleware](#jobinterruptmonitorplugin-is-retired-a-job-timeout-is-middleware) |
| `Quartz.Listener` | `Quartz.Listeners` | A `quartz.jobListener.<name>.type` or `quartz.triggerListener.<name>.type` naming the old spelling still resolves, with a warning — but see [The three `*Support` base classes are gone](#the-three-support-base-classes-are-gone): three of the seven types are not there under either name |
| `Quartz.Impl.Matchers` | `Quartz` | See [Matchers moved to `Quartz`](#matchers-moved-to-quartz). No shim is needed: a matcher is passed as an object and is never named by a configuration string |
| `Quartz.AspNetCore`, `Quartz.AspNetCore.HealthChecks`, `Quartz.AspNetCore.HttpApi` | `Quartz` | Only the namespaces are gone; `AddQuartzHealthChecks`, `AddQuartzHttpApi` and `MapQuartzHttpApi` are extension methods and resolve through the `Quartz` you already have, so a `using Quartz.AspNetCore;` can simply be deleted. The **packages** differ: the HTTP API is still `Quartz.AspNetCore`, hosted by `QuartzAspNetCoreConfigurationExtensions` (renamed from `QuartzServiceCollectionExtensions` because the core package now has a class of that name in the same namespace), while the health check is in `Quartz` from `4.0.0-alpha.4`, hosted by `QuartzHealthCheckExtensions` — see [The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore) |
| `Quartz.HttpClient` | `Quartz` | `HttpScheduler` and `HttpClientException`; the package is still `Quartz.HttpClient`. The namespace had to go because it shadowed `System.Net.Http.HttpClient` for every file under `Quartz.*`, including Quartz's own. `HttpScheduler` is also `sealed` now |
| `Quartz.Serialization.Json`, `Quartz.Serialization.Json.Calendars`, `Quartz.Serialization.Json.Triggers` | `Quartz.Serialization.SystemTextJson[.Calendars\|.Triggers]` | These are the System.Text.Json types, which merged into the core package; the namespace was named after the *retired 3.x Newtonsoft package*. `Quartz.JsonConfigurationExtensions` is `Quartz.SystemTextJsonConfigurationExtensions` to match — the extension methods on it are unaffected. **Read the warning below before changing a `using` on a ported serializer** |
| `Quartz.Impl.Redis` | `Quartz.Extensions.Redis` | One type, `RedisSemaphore`, filed under `Impl` as if it were part of the core; it is `RedisLockHandler` now. Namespace, assembly and package are the same string now; the **package id is unchanged**. A `quartz.jobStore.lockHandler.type` naming the old namespace or the old type name still resolves, with a warning |

::: warning Porting a 3.x Newtonsoft serializer
In 3.x, `Quartz.Serialization.Json.Triggers` and `Quartz.Serialization.Json.Calendars` were the
**Newtonsoft** package's namespaces. In 4.x the same spellings, minus `.Json`, plus `.SystemTextJson`,
belong to System.Text.Json — and the Newtonsoft package's are `Quartz.Serialization.Newtonsoft.Triggers`
and `Quartz.Serialization.Newtonsoft.Calendars`.

So a 3.x custom Newtonsoft serializer that is ported by changing its `using` to
`Quartz.Serialization.SystemTextJson.Triggers` compiles against the *System.Text.Json* base class, and then
fails on the overrides: the abstract members take a `Utf8JsonWriter` and a `JsonElement`, not a
`JsonWriter` and a `JObject`. The signature mismatch is the symptom; the wrong base class is the bug.
A Newtonsoft serializer stays on the Newtonsoft base — see
[Newtonsoft types moved out of the core namespaces](#newtonsoft-types-moved-out-of-the-core-namespaces).
:::

## The scheduler and the job store speak the same verbs

`IJobStore` had its own vocabulary — Store/Remove/Retrieve — for the operations `IScheduler` calls
Schedule/Add/Delete/Get, so the two halves of one operation had different names, and a stack trace had to be
translated on the way down. The store now uses the scheduler's words. If you implement a job store, rename;
if you only call `IScheduler`, nothing here affects you.

| `IJobStore` in 3.x | `IJobStore` in 4.x |
|---|---|
| `StoreJobAndTrigger(job, trigger)` | `ScheduleJob(job, trigger)` |
| `StoreJobsAndTriggers(triggersAndJobs, replace)` | `ScheduleJobs(triggersAndJobs, ScheduleJobOptions?)` |
| `StoreJob(job, replaceExisting)` | `AddJob(job, AddJobOptions?)` |
| `StoreTrigger(trigger, replaceExisting)` | `AddTrigger(trigger, AddTriggerOptions?)` |
| `RemoveJob(key)`, `RemoveJobs(keys)` | `DeleteJob(key)`, `DeleteJobs(keys)` |
| `RemoveTrigger(key)`, `RemoveTriggers(keys)` | `DeleteTrigger(key)`, `DeleteTriggers(keys)` |
| `RetrieveJob(key)` | `GetJob(key)` |
| `RetrieveTrigger(key)` | `GetTrigger(key)` |
| `StoreCalendar(name, cal, replaceExisting, updateTriggers)` | `AddCalendar(name, cal, AddCalendarOptions?)` |
| `RemoveCalendar(name)` | `DeleteCalendar(name)` |
| `RetrieveCalendar(name)` | `GetCalendar(name)` |
| `ClearAllSchedulingData()` | `Clear()` |
| `AcquireNextTriggers(noLaterThan, maxCount, timeWindow, executionLimits)` | `AcquireNextTriggers(TriggerAcquisitionRequest)` |

3.x spelled the flag that decides whether an existing item is over-written `replaceExisting` on some members
and `replace` on others. 4.x has no flag on any of them: each takes the same options record `IScheduler`
does — see [The store takes the same options](#the-store-takes-the-same-options). The `protected`
`AdoJobStoreBase` members that mirror these — the `ConnectionAndTransactionHolder` overloads, and
`AcquireNextTrigger` — were renamed with them and keep the `bool`, being the store's own machinery rather
than its contract.

The activity names in `Quartz.Diagnostics.OperationName.JobStore` follow the methods they name, so a trace
filter matching `"Quartz.JobStore.StoreJob"` now needs `"Quartz.JobStore.AddJob"`, and so on for every row
above.

### Acquisition takes a request record

```diff
- await store.AcquireNextTriggers(noLaterThan, maxCount, timeWindow, executionLimits, ct);
+ await store.AcquireNextTriggers(new TriggerAcquisitionRequest
+ {
+     NoLaterThan = noLaterThan,
+     MaxCount = maxCount,
+     TimeWindow = timeWindow,
+     ExecutionLimits = executionLimits,
+ }, ct);
```

`TriggerAcquisitionRequest` lives in `Quartz.Extensibility`. Acquisition keeps growing dimensions — the
batching window, per-execution-group limits, node affinity — and each one used to be another parameter on the
hot path of every store. As a record, the next one is an added optional property a store can ignore. It is the
store-level counterpart of the delegate-level `TriggerAcquisitionCriteria`. `TimeWindow`
rejects a negative value at construction, where `AdoJobStoreBase` used to throw from inside acquisition.

Both records also expose optional `ExcludedJobTypeNames` collections, and **every shipped store honours the
request-level one**. `AdoJobStoreBase` copies it into its delegate criteria and the standard ADO.NET delegates
keep the rows out in the acquisition SQL; `RAMJobStore` skips a candidate whose job type name is in the set.
Names use the stored `TriggerAcquireResult.JobTypeName` spelling, which is `JobType.FullName`. `RAMJobStore`
compares ordinally; SQL comparison follows the database job-class column's collation, including its
case-sensitivity rules.

## Options records replace boolean parameters

`AddJob` and `AddCalendar` took bare booleans that say nothing at the call site about which is which, and
`AddJob` had a second overload only because its second boolean was added later. Both are one member now,
taking an optional record whose defaults are the conservative choice (`Replace = false`,
`StoreNonDurableWhileAwaitingScheduling = false`, `UpdateTriggers = false`).

| 3.x | 4.x |
|---|---|
| `AddJob(job, replace: false)` | `AddJob(job)` |
| `AddJob(job, replace: true)` | `AddJob(job, new AddJobOptions { Replace = true })` |
| `AddJob(job, true, true)` | `AddJob(job, new AddJobOptions { Replace = true, StoreNonDurableWhileAwaitingScheduling = true })` |
| `AddCalendar(name, cal, false, false)` | `AddCalendar(name, cal)` |
| `AddCalendar(name, cal, true, true)` | `AddCalendar(name, cal, new AddCalendarOptions { Replace = true, UpdateTriggers = true })` |

`replace: true` on its own is nearly all of what these options are ever asked for, so each record names it:
`AddJobOptions.Replacing`, `ScheduleJobOptions.Replacing`, `AddCalendarOptions.Replacing` and
`AddCalendarOptions.ReplacingAndUpdatingTriggers` are the initializers above under a name — see
[Five shorthands for the common case](#five-shorthands-for-the-common-case). The initializer stays the way to
say anything else.

`AddJobOptions` and `AddCalendarOptions` are both in the `Quartz` namespace, and `IJobStore` takes them as
well — see [The store takes the same options](#the-store-takes-the-same-options).
`AddJobOptions.StoreNonDurableWhileAwaitingScheduling` is a scheduler-level rule that `IScheduler` has
already applied by the time the store is called, so a store reads only `Replace`.

Both are `readonly record struct`s, and the parameter is `AddJobOptions options = default` rather than
`AddJobOptions? options = null`. The defaults already were the conservative choice, so `default` *is* what
passing nothing has always meant — and the signature stops claiming three states (not given, empty,
configured) where there are two. Four implementations independently wrote `options ??= new()` to discover
that for themselves; a store of your own no longer has to.

```diff
- await scheduler.AddJob(job, null);
+ await scheduler.AddJob(job);

- AddJobOptions? options = null;
+ AddJobOptions options = default;
```

`new AddJobOptions { Replace = true }` and `new AddCalendarOptions { … }` are unchanged, and so is every
call that omits the argument. What stops compiling is passing an explicit `null`, or declaring a nullable
local of the type and handing it in — and a `CalendarConfiguration.Options` read that expected `null` to
mean "the scheduler's own defaults", which it never did.

The DI-time builders — `q.AddJob<T>(…)` and `q.AddCalendar<T>(…)` on `IQuartzBuilder` — are unchanged.

### `ScheduleJob` and `ScheduleJobs` take the same treatment

The two `IScheduler` members that schedule a job together with a collection of triggers took a bare
`bool replace` in the middle of their argument list, where it read as anonymous as `AddJob`'s did.

| 3.x | 4.x |
|---|---|
| `ScheduleJob(job, triggers, replace: false)` | `ScheduleJob(job, triggers)` |
| `ScheduleJob(job, triggers, replace: true)` | `ScheduleJob(job, triggers, new ScheduleJobOptions { Replace = true })` |
| `ScheduleJobs(triggersAndJobs, false)` | `ScheduleJobs(triggersAndJobs)` |
| `ScheduleJobs(triggersAndJobs, true)` | `ScheduleJobs(triggersAndJobs, new ScheduleJobOptions { Replace = true })` |

`ScheduleJobOptions` is a separate type from `AddJobOptions` rather than a reuse of it. `AddJobOptions`
also carries `StoreNonDurableWhileAwaitingScheduling`, which has no meaning here — a trigger is always
supplied, so the job is never awaiting scheduling — and its `Replace` is about the job alone, where this
one covers the job *and* its triggers.

The HTTP API's `ScheduleJobsRequest` keeps its `Replace` property; the endpoint adapts.

### The store takes the same options

`IJobStore` had the same inconsistency the scheduler did, and for longer: `AddCalendar` took an options
record while `AddJob`, `AddTrigger` and `ScheduleJobs` took a bare `bool`, so one interface spelled one
decision two ways. All four take a record now, and it is the *same* record the scheduler takes — a
decorator forwarding a call passes the value through rather than unpacking and rebuilding it.

| 3.x | 4.x |
|---|---|
| `store.StoreJob(job, replaceExisting: false)` | `store.AddJob(job)` |
| `store.StoreJob(job, replaceExisting: true)` | `store.AddJob(job, AddJobOptions.Replacing)` |
| `store.StoreTrigger(trigger, replaceExisting: true)` | `store.AddTrigger(trigger, AddTriggerOptions.Replacing)` |
| `store.StoreJobsAndTriggers(batch, replace: true)` | `store.ScheduleJobs(batch, ScheduleJobOptions.Replacing)` |

`AddTriggerOptions` is new, and carries `Replace` alone. It has one call site, because `IScheduler` has no
`AddTrigger`: a trigger reaches a scheduler through `ScheduleJob`, and `IJobStore.AddTrigger` is the
storage half of that.

If you implement `IJobStore`, the three signatures change and the argument you already had is
`options.Replace`. If you only call `IScheduler`, nothing here affects you.

## A component of your own can have options of its own

`IQuartzBuilder.ConfigureOptions<TOptions>(Action<TOptions>?)` is new. It registers the callback under
this scheduler's options name and declares the type as belonging to the scheduler, so a component the
container builds — which asks for `IOptions<TOptions>` and would otherwise be handed the *unnamed*
instance — sees what was configured for its own scheduler.

That mechanism existed; it was reachable only through `AddPlugin<T, TOptions>()`, which is now sugar
over it. Everything else built by the container — a thread pool, a job store, a lock handler, a
listener, a job factory — had no way to reach it, and under `AddQuartz("name", …)` quietly saw defaults.

```csharp
services.AddQuartz("reporting", q =>
{
    q.ConfigureOptions<MyThreadPoolOptions>(options => options.Slots = 20);
    q.UseThreadPool<MyThreadPool>();
});
```

It is a default interface implementation, so an `IQuartzBuilder` implemented outside Quartz keeps
compiling — and the default body is the whole mechanism rather than a stub, because the scheduler's
options name is its `SchedulerName` (`Options.DefaultName` is the empty string, which is what
`SchedulerName` is for the unnamed scheduler).

With a replacement to point at, `UseThreadPool<T>()` drops its `Action<ThreadPoolOptions>` parameter:

| Before | After |
|---|---|
| `UseThreadPool<T>(options => options.MaxConcurrency = 20)` | `UseDefaultThreadPool(maxConcurrency: 20)`, or `ConfigureOptions<TOwnOptions>(…)` for a pool of your own |

The parameter was honoured for exactly one implementation — `TaskSchedulingThreadPool` and its
descendants, which is what `MaxConcurrency` means. `UseThreadPool<AnythingElse>(o => o.MaxConcurrency = 5)`
compiled, registered the callback and did nothing with it. `UseDefaultThreadPool` keeps the parameter,
because that is where the options are read.

## Overloads that differed only by a default

| 3.x | 4.x |
|---|---|
| `Shutdown()`, `Shutdown(bool waitForJobsToComplete)` | `Shutdown(bool waitForJobsToComplete = false, …)` |
| `TriggerJob(jobKey)`, `TriggerJob(jobKey, data)` | `TriggerJob(JobKey jobKey, JobDataMap? data = null, …)` |
| `Interrupt(string fireInstanceId)` | `InterruptFireInstance(string fireInstanceId)` |
| `GetMetaData()` | `GetMetadata()`, returning `SchedulerMetadata` |
| `GetTriggersOfJob(jobKey)` | extension method over `QueryTriggers(new TriggerQuery { Job = jobKey })` |

Calls to `Shutdown` and `TriggerJob` compile unchanged unless they passed a `CancellationToken` positionally
into the short overload, which now needs to be named:

```diff
- await scheduler.Shutdown(cancellationToken);
+ await scheduler.Shutdown(cancellationToken: cancellationToken);
```

`Interrupt` overloaded on `JobKey` versus `string` hid two different operations behind one name — cancel every
execution of a job, versus cancel one specific fire — so picking the wrong one was a silent, type-driven
mistake. `Interrupt(JobKey)` keeps its name.

`GetTriggersOfJob` is an extension method on `SchedulerQueryExtensions` now, so existing call sites still
compile. It runs `QueryTriggers` and then `GetTriggers` on the keys it found; when the state and fire times a
listing needs are enough, call `QueryTriggers` with `TriggerQuery.Job` yourself and save the second round trip.

### `SchedulerMetadata` replaces `SchedulerMetaData`

```diff
- SchedulerMetaData metaData = await scheduler.GetMetaData();
- Console.WriteLine($"Executed {metaData.NumberOfJobsExecuted} jobs.");
- Console.WriteLine(metaData.GetSummary());
+ SchedulerMetadata metadata = await scheduler.GetMetadata();
+ Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
+ Console.WriteLine(metadata);
```

The old type was built through a fifteen-parameter constructor of which six were adjacent booleans. It is a
`sealed record` with `init` properties now, so a snapshot is built and read by name. `GetSummary()` is gone —
the record's `ToString()` prints every value, which is what the hand-written summary was for. Over HTTP,
`SchedulerStatisticsDto.NumberOfJobsExecuted` is `JobsExecuted` to match.

The renamed and reshaped properties:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `SchedulerType` (`Type`) | `SchedulerTypeName` (`string`) |
| `JobStoreType` (`Type`) | `JobStoreTypeName` (`string`) |
| `ThreadPoolType` (`Type`) | `ThreadPoolTypeName` (`string`) |
| `SchedulerRemote` / `IsRemote` | `IsProxy` |
| `NumberOfJobsExecuted` | `JobsExecuted` |
| `JobStoreSupportsPersistence` | `JobStorePersistent`, reading like its sibling `JobStoreClustered` |
| `Started`, `InStandbyMode`, `Shutdown` | one `required SchedulerStatus Status` — see [A scheduler's lifecycle is one value](#a-scheduler-s-lifecycle-is-one-value) |

The `Type` members became assembly-qualified names (without version) because a proxy cannot promise to
materialize them: an `HttpScheduler` reads the remote scheduler's metadata over the wire, and the remote's
job store or thread pool type need not exist — and previously had to be `Type.GetType`-resolved — in the
client process. `IsProxy` says what the flag always meant: this metadata describes a proxy to a scheduler
running elsewhere, with the values read over the wire, rather than the in-process instance.

## Single-key mutations answer whether they applied

`IScheduler` already answered for some mutations — `DeleteJob`, `UnscheduleJob` and
`UpdateTriggerDetails` return a `bool` — while the pause family returned nothing, so a caller pausing
a mistyped key learned nothing. The rule is now uniform: a mutation aimed at one key returns
`ValueTask<bool>` meaning "the entity existed and the operation applied", and the group-matcher forms
return the affected group names.

| Member (on `IScheduler` and `IJobStore`) | 3.x returned | 4.x returns |
|---|---|---|
| `PauseTrigger(key)`, `ResumeTrigger(key)` | `ValueTask` | `ValueTask<bool>` |
| `PauseJob(key)`, `ResumeJob(key)` | `ValueTask` | `ValueTask<bool>` |
| `ResetTriggerFromErrorState(key)` | `ValueTask` | `ValueTask<bool>` |
| `PauseTriggers(matcher)`, `ResumeTriggers(matcher)` | `ValueTask` (scheduler) | `ValueTask<List<string>>` of the group names affected |
| `PauseJobs(matcher)`, `ResumeJobs(matcher)` | `ValueTask` (scheduler) | `ValueTask<List<string>>` of the group names affected |

What the `bool` means, precisely:

* `PauseTrigger` — the trigger exists and ended up paused because of this call. Already paused,
  complete, or missing → `false`.
* `ResumeTrigger` — the trigger existed in a paused state and was resumed. Not paused or missing →
  `false`.
* `PauseJob` / `ResumeJob` — the job exists. A job that currently has zero triggers returns `true`:
  the job was found and the operation applied to all (zero) of its triggers.
* `ResetTriggerFromErrorState` — the trigger existed in the `Error` state and was reset. Not in
  `Error`, or missing → `false`.

When the result is `false`, no scheduler-listener events are raised — a no-op no longer looks like a
state change to listeners. Awaiting call sites compile unchanged; only `ISchedulerListener`
implementations that counted on being told about no-op pauses will notice.

Over the HTTP API these endpoints used to answer `200 OK` with an empty body. They now answer
`200 OK` with a JSON body: `{"applied": bool}` for the single-key forms and `{"groups": [...]}` for
the group-matcher forms. This is additive for clients that ignored the body — but a **4.0-final
`HttpScheduler` against a 4.0-preview server throws** on these calls, because the client now reads a
response body the old server never sends. Upgrade the server before, or together with, its remote
clients.

## A set of keys pauses, resumes or resets in one call

Pausing forty triggers took forty calls, and on a database store forty transactions and forty
scheduling signals. `IScheduler` and `IJobStore` now take a key set as well as one key or a group
matcher:

| New member (on `IScheduler` and `IJobStore`) | Returns |
|---|---|
| `PauseTriggers(IReadOnlyCollection<TriggerKey>)`, `ResumeTriggers(…)` | `ValueTask<List<TriggerKey>>` of the keys it applied to |
| `PauseJobs(IReadOnlyCollection<JobKey>)`, `ResumeJobs(…)` | `ValueTask<List<JobKey>>` of the keys it applied to |
| `ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey>)` | `ValueTask<List<TriggerKey>>` of the keys it reset |

```csharp
List<TriggerKey> paused = await scheduler.PauseTriggers(
    [new TriggerKey("nightly", "reports"), new TriggerKey("hourly", "reports")]);
```

The answer is the plural of the single-key `bool`: a key the operation did not apply to — one that
names nothing, one that was already paused, one that was not in the error state — is **absent from
the returned list**, never an exception. The list keeps the order the keys were given in. These are
overloads, so the existing single-key and group-matcher forms are untouched; only a `null` literal
argument, which never compiled against `PauseJobs(null)` anyway, now needs a cast to pick an overload.

What the outside world sees:

* **Listener events stay per key.** One `TriggerPaused`, `JobPaused`, `TriggerResumed` or `JobResumed`
  for every key the operation applied to, and nothing for the rest. There is no key-set listener
  event and none was added: `TriggersPaused(null)` means *every group*, so a listener watching for
  outages would read a bulk pause of two triggers as the whole scheduler going down, and a new
  event with a default implementation would silently stop telling existing listeners anything.
* **One scheduling signal per call**, not one per key — the scheduler thread reads the signal as a
  level, so collapsing them loses nothing. Resetting from the error state signals nothing at all,
  here as in the single-key form.
* **One pass in the store.** `RAMJobStore` takes its lock once; the ADO store runs the whole set
  inside one `SchedulerLock.TriggerAccess` scope and one transaction, so a bulk pause is atomic
  where forty single pauses were not.

`IJobStore`'s five members are default implementations that walk the set one key at a time, so a job
store of your own keeps compiling and stays correct. Override them to do the walk in one pass, as both
shipped stores do.

Over HTTP the key-set forms are new endpoints, described in
[the HTTP API page](packages/http-api.md#a-whole-set-of-keys-in-one-call):
`POST …/jobs/keys/pause`, `…/jobs/keys/resume`, `…/triggers/keys/pause`, `…/triggers/keys/resume` and
`…/triggers/keys/reset-from-error-state`. They live under `keys/` because the collection-level `pause`
and `resume` already belong to the group-matcher forms.

## The key-set delete and unschedule answer with the keys they removed

`DeleteJobs` and `UnscheduleJobs` predate that convention and used to answer one `bool` meaning "every
key given was found". That answer was lossy in the case a caller most needs to know about: delete
three of five existing jobs and **three jobs are deleted** while the call answers `false`, which is
indistinguishable from nothing having happened. A caller who retried on `false` never learned that
most of the work had already succeeded. They now answer the way the rest of the key-set family does.

| Member | 3.x / 4.0 preview returned | 4.0 returns |
|---|---|---|
| `IScheduler.DeleteJobs(IReadOnlyCollection<JobKey>)` | `ValueTask<bool>` — every key was found | `ValueTask<List<JobKey>>` of the keys it deleted |
| `IScheduler.UnscheduleJobs(IReadOnlyCollection<TriggerKey>)` | `ValueTask<bool>` | `ValueTask<List<TriggerKey>>` of the keys it removed |
| `IJobStore.DeleteJobs(IReadOnlyCollection<JobKey>)` | `ValueTask<bool>` | `ValueTask<List<JobKey>>` |
| `IJobStore.DeleteTriggers(IReadOnlyCollection<TriggerKey>)` | `ValueTask<bool>` | `ValueTask<List<TriggerKey>>` |

**To get the old answer back**, compare the counts:

```csharp
List<JobKey> deleted = await scheduler.DeleteJobs(jobKeys);
bool allFound = deleted.Count == jobKeys.Count;   // what the bool used to say
```

That one line is why the change is a replacement rather than a second overload: the old answer is
still available to anyone who wants it, while the question it could not answer — *which* keys — now
has one.

Three consequences worth knowing:

* **A key that named nothing no longer raises a listener event.** The old implementation raised one
  `ISchedulerListener.JobDeleted` / `JobUnscheduled` per key **given**, so a mistyped key told every
  listener that a job by that name had been deleted. The events now follow the keys the call applied
  to, which is what the single-key `DeleteJob` and the whole key-set pause family already did. The
  scheduling change is still signalled once for the call, and not at all when nothing was removed.
* **An empty key set never reaches the store**, and a `null` one is an `ArgumentNullException` rather
  than a `NullReferenceException` — again matching the key-set pause family.
* **Repeating a key in the set no longer poisons the answer.** The second pass finds nothing left to
  delete; before, that dragged the whole call's `bool` to `false`.

If you implement `IJobStore`, both members are now **default implementations** that walk the set one
key at a time, so a store that only implements the single-key `DeleteJob` / `DeleteTrigger` is correct
without writing them. That has one trap: a store that still declares
`ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey>, CancellationToken)` **still compiles** — the
method simply stops implementing the interface member, and the per-key default silently takes over.
Change the return type. Both shipped stores override the pair to walk inside one lock and one
transaction, and `JobStoreContractTest` fails a store that has left the default in place.

The ADO store's walk stays per key on purpose. Deleting a job there is a cascade rather than a
statement — its triggers and their sub-table rows, the fired-trigger rows that would otherwise
resurrect it, then the job detail row — and a set-based `DELETE … WHERE … IN (…)` reports a row count
rather than which keys it hit. Naming the deleted keys therefore costs no extra round trip: each
iteration's result was already there and was being folded into a boolean and thrown away.

Over HTTP, `POST …/jobs/delete` and `POST …/triggers/unschedule` answer `{"jobs": [ … ]}` and
`{"triggers": [ … ]}` instead of `{"allFound": …}`, which makes them ordinary members of
[the key-set family](packages/http-api.md#a-whole-set-of-keys-in-one-call) and removes the one
exception to the flag-naming rule.

## Jobs and triggers can be removed by group

Pause has always had a group form and delete has not, so calling off everything that belonged to one
saga, one tenant or one import meant listing the keys and then deleting them — two calls, and a window
between them in which another node can schedule one more thing into the group that the delete will
then miss. Both members now take a `GroupMatcher` as well as a key set:

| New member (on `IScheduler`) | Returns |
|---|---|
| `DeleteJobs(GroupMatcher<JobKey>)` | `ValueTask<List<JobKey>>` of the jobs it deleted |
| `UnscheduleJobs(GroupMatcher<TriggerKey>)` | `ValueTask<List<TriggerKey>>` of the triggers it removed |

```csharp
// Everything scheduled for one saga, called off in one call.
List<TriggerKey> calledOff = await scheduler.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals(sagaId));
```

The answer is the **keys**, not the group names that `PauseJobs(matcher)` returns. A pause has
something to remember per group — a group stays paused, and a job added to it later is born paused —
and a delete has nothing: the group is simply emptier than it was. What a caller can still act on is
what went, so that is what it gets.

* **`null` is an `ArgumentNullException`, not "the default group".** `PauseJobs(null)` and
  `ResumeJobs(null)` read a missing matcher as `GroupEquals(JobKey.DefaultGroup)`. These do not, and
  will not: a pause taken by mistake can be resumed. A `null` literal now also needs a cast to pick an
  overload, exactly as it did when the key-set forms arrived —
  `DeleteJobs((IReadOnlyCollection<JobKey>) null!)`.
* **One scheduling signal per call and one listener event per key**, the same shape the key-set forms
  have: an `ISchedulerListener.JobDeleted` or `JobUnscheduled` for each key removed, nothing for an
  empty group, and no group-level event — `JobsPaused(null)` means *every group* and a monitoring
  listener would read it as a total outage.
* **A non-durable job orphaned by `UnscheduleJobs(matcher)` is deleted, and is not named.** That is
  what the single-key `UnscheduleJob` and the key-set `DeleteTriggers` already do; the answer is about
  triggers.

`IJobStore` gains `DeleteJobs(GroupMatcher<JobKey>)` and `DeleteTriggers(GroupMatcher<TriggerKey>)` as
**default implementations** that list the matching keys and then delete them, so a store of your own
keeps compiling and stays correct. What the default cannot be is atomic — the listing and the deletion
are two operations — so both shipped stores override the pair to resolve the group inside the lock that
empties it: `RAMJobStore` under its single lock, the ADO store inside one `SchedulerLock.TriggerAccess`
scope and one transaction, using the `SelectJobKeysInGroup` / `SelectTriggerKeysInGroup` its driver
delegate already had. `JobStoreContractTest` fails a shipped store that has left the default in place.

The ADO store's walk stays per key once the group is resolved, for the reason
[the key-set forms give](#the-key-set-delete-and-unschedule-answer-with-the-keys-they-removed): the
answer has to name the keys it hit, and a set-based `DELETE … WHERE … IN (…)` reports a row count.

Over HTTP the group forms are new endpoints, `POST …/jobs/delete-by-group` and
`POST …/triggers/unschedule-by-group`, taking the same four `group*` query parameters as
`…/jobs/pause` and answering with the same `{"jobs": […]}` / `{"triggers": […]}` bodies the key-set
forms do — [described here](packages/http-api.md#a-whole-group-in-one-call). They carry `-by-group` in
the path because the plain `delete` and `unschedule` paths were taken by the key-set forms before
there was a group form to give them to.

## One wire contract, and its enums have names

The DTOs that define the HTTP API's JSON used to live in `Quartz.HttpClient`, and `Quartz.AspNetCore`
reached them by depending on the client package. They now live in `Quartz`, internal, visible to both —
so the server package no longer ships a dependency on the client package. Nothing public moved: the
contract was internal before and is internal still, and every JSON property name survived the move
unchanged.

What did change is how the contract's enums are spelled. A trigger body has always said
`"repeatIntervalUnit": "Hour"`, because Quartz's own converters write enums by name, while the DTOs
beside it said `"status": 1`. One value now has one spelling everywhere:

| Body | 4.0 preview | 4.0 |
|---|---|---|
| `GET …/schedulers` item, `GET …/schedulers/{name}` | `"status": 1` | `"status": "Running"` |
| `GET …/schedulers/{name}/triggers` item | `"state": 1` | `"state": "Paused"` |
| `GET …/schedulers/{name}/triggers/{group}/{name}/state` | `{"state": 1}` | `{"state": "Paused"}` |

Reading still accepts both, so a request body or `?state=` filter written against a preview keeps
working; only responses changed. `HttpScheduler` reads the new spelling, so a client and server upgraded
together need no code change — a hand-written client that read `status`/`state` as numbers does.

One field was spelled twice for the same value, and now is not:

| Body | 4.0 preview | 4.0 |
|---|---|---|
| `GET …/schedulers/{name}/jobs` item | `"jobTypeName": "Some.Job, Some.Assembly"` | `"jobType": "Some.Job, Some.Assembly"` |

The value is unchanged — an assembly-qualified name in both places — and `jobType` is what
`GET …/jobs/{group}/{name}` has always called it. Core's listing record keeps `JobHeader.JobTypeName`:
the store's noun and the wire's need not agree, but the wire may not disagree with itself.

Only these two enums are affected, and the converters are registered per enum type: the HTTP API adds
its converters to the application's shared `JsonOptions`, and a host's own endpoints must keep rendering
their own enums the way they always did.

## One flag per mutation, named for what it reports

Which body a `200` carries is now a rule rather than a per-endpoint fact: **a `200` carries a body
exactly when the operation has something to say that the caller could not have worked out for
itself.** A mutation that always acts — `AddJob`, `TriggerJob`, `PauseAll`, `ScheduleJobs`,
`AddCalendar`, the scheduler and execution-limit writes — answers with an empty body; a mutation whose
effect may be a no-op answers with one boolean flag; a mutation aimed at a group matcher or a key set
answers with what it applied to; and a mutation that computed something answers with that value.

The empty-body and flag-carrying halves were already split that way. What was not consistent was the
*name*: seven spellings of the same question, so a caller had to look up the body per endpoint instead
of predicting it.

| Endpoint | 4.0 preview | 4.0 |
|---|---|---|
| `DELETE …/jobs/{group}/{name}` | `{"jobFound": …}` | `{"applied": …}` |
| `POST …/jobs/{group}/{name}/interrupt` | `{"interrupted": …}` | `{"applied": …}` |
| `POST …/jobs/interrupt/{fireInstanceId}` | `{"interrupted": …}` | `{"applied": …}` |
| `POST …/triggers/{group}/{name}/unschedule` | `{"triggerFound": …}` | `{"applied": …}` |
| `DELETE …/calendars/{name}` | `{"calendarFound": …}` | `{"applied": …}` |
| `POST …/jobs/delete` | `{"allJobsFound": …}` | `{"jobs": [ … ]}` |
| `POST …/triggers/unschedule` | `{"allTriggersFound": …}` | `{"triggers": [ … ]}` |

`applied` means the entity existed and the operation changed it, and there is no second spelling: an
operation that cannot answer that question about a single entity is a key-set form, and answers with
the keys instead. The last two rows are the ones that changed *shape* rather than name — they are key
sets, and a single boolean could only say that not every key was found, which a caller could not tell
from nothing having happened. See
[the key-set delete and unschedule](#the-key-set-delete-and-unschedule-answer-with-the-keys-they-removed)
for the scheduler API behind them.

`HttpScheduler` reads the new spellings, so a client and server upgraded together need no code change;
a hand-written client reading the old names does.

Errors gained a rule of their own: **a client-actionable error names the exception type it came from;
a server fault does not.** Every `400` and every `404` now carries the same members whichever layer
raised it — `type`, `title`, `status`, `detail` and `Quartz-ExceptionType`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "The scheduler has been shut down",
  "Quartz-ExceptionType": "SchedulerException"
}
```

`Quartz-ExceptionType` used to ride only on the `SchedulerException` path, so a `400` from request
validation and a `400` from the scheduler were two different shapes, and a client could not tell a
body the member was missing from a body that never carries it. It now names the exception type on
every `400` and `404`, including the framework's own — `BadHttpRequestException` for a request the
endpoint rejected, `NotFoundException` for a `404`. Map the Quartz names back to typed exceptions and
treat the rest as opaque, which is what `HttpScheduler` does.

A `500` deliberately does **not** carry it. It is a fault the caller cannot act on, so naming the type
behind it would say something about the server's internals and nothing a client could use.

The one `400` that still has no body is not the API's: a query parameter the framework could not bind
never reaches an endpoint, so nothing of ours writes the response.

## Durations are `TimeSpan`s, wherever they are

A trigger body has always said `"repeatIntervalTimeSpan": "120.02:30:59.9990000"`. Three duration
members beside it counted whole milliseconds instead, which both disagreed with that and threw away
everything below a millisecond:

| 4.0 preview | 4.0 |
|---|---|
| `POST …/schedulers/{name}/start?delayMilliseconds=30000` | `?delay=00:00:30` |
| `JobExecutionResultDto.RunTimeMs` (`long`) | `RunTime` (`TimeSpan`) |
| `DashboardHistoryEntry.DurationMs` (`long`) | `Duration` (`TimeSpan`) |

`HttpScheduler.StartDelayed` sends the new spelling, so a client and server upgraded together need no
code change. A negative `delay` is now a `400` rather than a delay that runs backwards.

The two dashboard records are the ones the dashboard's SignalR hub and its history store put on the
wire, so a browser or history store reading `runTimeMs` / `durationMs` as a number reads
`runTime` / `duration` as an ISO-ish `TimeSpan` string instead.

## The dashboard's client speaks one currency

`IQuartzApiClient` is the dashboard's own projection of the HTTP API — public so that an application can
replace it with its own data source. It used to answer in three vocabularies at once: an enum from
`GetTriggers` and a `string` from `GetTriggerState`, a paging model of its own beside core's, and
sixteen methods taking a loose `(schedulerName, group, name)` triplet next to a hub interface that
already had `JobKeyDto` and `TriggerKeyDto`. Now it says what Quartz says:

| 3.x | 4.x |
|---|---|
| `GetTriggerState(…)` → `string` | → `TriggerState` |
| `TriggerHeaderDto.State` is `string?` | `TriggerState?` |
| `SchedulerHeaderDto.Status`, `SchedulerDetailDto.Status` are `string` | `SchedulerStatus`, a new public enum in `Quartz` |
| `GetJobs(name, string? groupFilter, int page, int pageSize)` → `JobPageDto` | `QueryJobs(name, DashboardJobQuery)` → `PagedResult<JobKeyDto>` |
| `GetTriggers(name, string? groupFilter, TriggerState?, int page, int pageSize)` → `TriggerPageDto` | `QueryTriggers(name, DashboardTriggerQuery)` → `PagedResult<TriggerHeaderDto>` |
| `GetHistory(JobHistoryQueryDto)` → `JobHistoryPageDto` (a `JsonElement`) | `QueryExecutions(DashboardHistoryQuery)` → `PagedResult<DashboardHistoryEntry>` |
| `GetCurrentlyExecutingJobs(name)` → `List<CurrentlyExecutingJobDto>` | `QueryFireInstances(name, DashboardFireInstanceQuery)` → `PagedResult<FireInstanceDto>`, following `IScheduler` — see [What is running is a listing, not a list of contexts](#what-is-running-is-a-listing-not-a-list-of-contexts) |
| `CurrentlyExecutingJobDto` | `FireInstanceDto`: `FireInstanceId` is non-null and leads, `JobKey` is nullable (an acquired firing has no job loaded yet), and `SchedulerInstanceId`, `FireInstanceState State` and `ScheduledFireTimeUtc` are new |
| — | `QueryClusterNodes(name)` → `List<ClusterNodeDto>` (new), behind the **Cluster** page — see [The nodes of a cluster are a listing](#the-nodes-of-a-cluster-are-a-listing) |
| `IsJobGroupPaused(name, group)` → `bool` | `QueryJobGroups(name, DashboardGroupQuery)` → `PagedResult<JobGroupDto>`, each carrying `Name` and `Paused`; one call answers for every group instead of one, and `Take = 0` with `Paused = true` counts the paused ones without listing them |
| — | `QueryTriggerGroups(name, DashboardGroupQuery)` → `PagedResult<TriggerGroupDto>` (new), the trigger-group twin of the above |
| — | `CountMisfires(name, since)` → `int` (new), what the overview's misfire tile reads — see [History and live events say which node they came from](#history-and-live-events-say-which-node-they-came-from) |
| `ExecutionLimitsDto(IReadOnlyDictionary<string, int?> Limits)` | `ExecutionLimitsDto(Dictionary<string, DashboardExecutionLimit> Limits, bool UsesTriggerGroupWhenUnset = false, bool CanReport = true)` — concrete out, as everywhere else; each entry carries the limit's [scope](#an-execution-limit-can-be-cluster-wide) as well as its number, and the keys are the spellings configuration and the HTTP API use (`_` for the ungrouped bucket, `*` for the catch-all) so that a firing can be joined to the limit governing it |
| `GetExecutionLimits(name)` → `ExecutionLimitsDto?`, null both for "nothing is limited" and for "this scheduler cannot say" | → `ExecutionLimitsDto`, never null: nothing limited is an empty `Limits`, and a scheduler that cannot answer is `ExecutionLimitsDto.CannotReport`, whose `CanReport` is `false` |
| `IDashboardHistoryStore.GetPage(name, page, pageSize, jobFilter, triggerFilter)` → `DashboardHistoryPage` | `QueryExecutions(DashboardHistoryQuery)` → `PagedResult<DashboardHistoryEntry>` |
| `…Job(name, string group, string jobName)` — eight members | `…Job(name, JobKeyDto)` |
| `…Trigger(name, string group, string triggerName)` — seven members | `…Trigger(name, TriggerKeyDto)` |

`DashboardJobQuery`, `DashboardTriggerQuery` and `DashboardHistoryQuery` derive from `PagedQuery`, so they
carry `Skip`, `Take` and `IncludeTotalCount` with the meanings the job stores give them, and every listing
returns `PagedResult<T>` with `HasMore` and a nullable `TotalCount`. A 1-based page becomes
`Skip = (page - 1) * pageSize, Take = pageSize`, computed once where the pager lives instead of at every
call site. `JobPageDto`, `TriggerPageDto`, `DashboardHistoryPage`, `JobHistoryPageDto` and
`JobHistoryQueryDto` are gone.

`SchedulerStatus` is the enum the HTTP API has always put on the wire; it is public now because the
dashboard's contract needed a name for it, and it is what
[`IScheduler.Status`](#a-scheduler-s-lifecycle-is-one-value) reports. Note that a running scheduler is
`Running`: the in-process client used to call it `"Started"` while the HTTP-backed client called the same
state `"Running"`, and code that matched on either string now matches on the enum.

The dashboard hub's `SchedulerStateDto` follows: it is
`(string SchedulerName, string SchedulerInstanceId, SchedulerStatus Status)` rather than
`(string SchedulerName, string State)`, and it is pushed once per state the scheduler arrives in.
`SchedulerStarting` pushes nothing, being an event rather than a state. The instance id is new in
alpha.3 — see [History and live events say which node they came from](#history-and-live-events-say-which-node-they-came-from).

### The client speaks the scheduler's verbs

The names went with the types. An operation `IQuartzApiClient` forwards to a scheduler is spelled the way
`IScheduler` spells it, so a reader who knows one knows the other; only what has no counterpart on
`IScheduler` — the scheduler listing, the execution history — names itself. The DTO suffixes and the
`PagedResult<T>` returns are unchanged.

| 4.0 preview | 4.0 |
|---|---|
| `StartScheduler`, `StandbyScheduler`, `ShutdownScheduler` | `Start`, `Standby`, `Shutdown` |
| `InterruptJob(name, JobKeyDto)` | `Interrupt(name, JobKeyDto)`; `InterruptFireInstance` is unchanged |
| `TriggerJob(name, JobKeyDto)` **and** `TriggerJobWithData(name, JobKeyDto, JobDataMap)` | one `TriggerJob(name, JobKeyDto, JobDataMap? jobDataMap = null)`, as `IScheduler.TriggerJob` is |
| `GetJobs`, `GetJobGroups`, `GetTriggers`, `GetTriggerGroups`, `GetFireInstances`, `GetClusterNodes` | `QueryJobs`, `QueryJobGroups`, `QueryTriggers`, `QueryTriggerGroups`, `QueryFireInstances`, `QueryClusterNodes` |
| `GetJob(name, JobKeyDto)` → `JobDetailDto` | `GetJobDetail(name, JobKeyDto)`, the noun `IScheduler` uses |
| `GetJobTriggers(name, JobKeyDto)` | `GetTriggersOfJob(name, JobKeyDto)` |
| `GetHistory(DashboardHistoryQuery)` → `PagedResult<DashboardHistoryEntry>?` | `QueryExecutions(…)` → `PagedResult<DashboardHistoryEntry>`, never null |
| `GetMisfires(DashboardMisfireQuery)` → `PagedResult<DashboardMisfireEntry>?` | `QueryMisfires(…)` → `PagedResult<DashboardMisfireEntry>`, never null |
| `CountMisfires(name, since)` → `int?` | → `int` |
| `IDashboardHistoryStore.Add`, `GetPage`, `GetMisfires` | `AddExecution`, `QueryExecutions`, `QueryMisfires`; `AddMisfire` and `CountMisfires` are unchanged |

`GetSchedulers`, `GetScheduler` and `GetCalendarNames` keep their names: the first two list and read the
container's registrations, which is not an operation a scheduler has, and `GetCalendarNames` reads *every*
page of `IScheduler.QueryCalendarNames` rather than one, so naming it `Query*` would promise a paged query
it does not take.

The three nullable returns were nullable because the deleted HTTP-backed client turned a 404 into "this
data source keeps no history". Nothing in this process has that answer: `IDashboardHistoryStore` always
answers, and a store holding nothing returns an empty page and a count of zero. The History page's
"execution history is unavailable" state went with the null, and the overview's misfire tile shows a dash
only while no scheduler has answered yet.

### A trigger is an `ITrigger`, a calendar is an `ICalendar`

Six members of that contract still spoke `System.Text.Json.JsonElement`. They were placeholders from
the dashboard's build-out, and they made every consumer of `IQuartzApiClient` parse JSON to read
something Quartz already has a type for:

| 4.0 preview | 4.0 |
|---|---|
| `GetTrigger(…)` → `TriggerDetailDto(JsonElement Value)` | → `ITrigger` |
| `GetCalendar(…)` → `CalendarDetailDto(JsonElement Value)` | → `ICalendar` |
| `ScheduleJobRequest(JsonElement Trigger, JobDetailDto? Job)` | `ScheduleJobRequest(ITrigger Trigger, JobDetailDto? Job)` |
| `RescheduleRequest(JsonElement NewTrigger)` | `RescheduleRequest(ITrigger NewTrigger)` |
| `AddCalendarRequest(string, JsonElement Calendar, bool, bool)` | `AddCalendarRequest(string, ICalendar Calendar, bool, bool)` |
| `JobDetailDto.JobDataMap` is a `JsonElement` | a `JobDataMap` |
| `TriggerJobWithData(…, JsonElement jobDataMap, …)` | `TriggerJob(…, JobDataMap? jobDataMap = null, …)` |

`TriggerDetailDto` and `CalendarDetailDto` are gone with them; nothing remains for them to wrap.

The polymorphism a trigger needs is Quartz's own: the serializer registry maps each kind to its own
serializer, custom kinds an application registered included, and the HTTP API's wire format *is* that
discriminated shape. A per-kind DTO family in the dashboard would have to grow a member for every
trigger kind and would still have nothing to say about a kind it had never heard of, so the contract
speaks `ITrigger` and lets the registry do what it is for. `JobDataMap` is the same argument at the
other end of the scale: the map holds arbitrary user values, which is exactly what `JobDataMap` is,
and typing it as one keeps an `int` an `int` where a JSON round trip made it whatever the reader
guessed.

One consequence worth knowing: **the client no longer serializes anything.** It used to write the
trigger to JSON and read it back for no reason but the contract's type, and it fell back to reflecting
over the trigger for kinds the registry did not know — which produced a payload that could not be posted
back. A custom trigger type now reaches the detail page as itself.

`TriggerHeaderDto` was half a positional record and half property-initialised, which read as an
accident because it was one. It is positional throughout:

```diff
- new TriggerHeaderDto(group, name, executionGroup) { TriggerType = …, ScheduleSummary = …, State = … }
+ new TriggerHeaderDto(group, name, triggerType, scheduleSummary, state, executionGroup)
```

### The dashboard reads the schedulers in its own process

`Quartz.Dashboard.Services.QuartzApiClient` — the `IQuartzApiClient` implementation that called a Quartz
HTTP API over the network — is gone, and `QuartzDashboardOptions.BaseUrl` and
`QuartzDashboardOptions.ApiPath` are gone with it. Nothing ever resolved it: `AddQuartzDashboard`
registers the in-process client, which reads `ISchedulerRepository` directly, and it did so in every
4.0 preview. Two implementations of one interface where only one ever ran had already disagreed twice
about the same fact — `"Started"` against `"Running"` for a running scheduler, `CronTrigger` against
`Cron` for a trigger's kind — because nothing exercised the one that never ran.

`AddQuartzDashboard` still registers `IQuartzApiClient` with `TryAdd`, so an application that registers
its own implementation first is the one the pages read. A dashboard rendering a scheduler in another
process is a product with its own questions — authentication forwarding, execution limits, a history
endpoint no Quartz HTTP API serves — and it is designed in
[#3387](https://github.com/quartznet/quartznet/issues/3387) rather than left half-built here.

`QuartzHttpApiOptions.ApiPath`, which is where the HTTP API itself is served, is unaffected: it is a
different option on a different type, and `AddQuartzHttpApi` still reads it.

## History and live events say which node they came from

A cluster is one scheduler running in several processes. Each node keeps its own history of its own
executions and pushes its own live events, and neither said which node it was — so the History page
could not attribute a row to a machine and the Live Logs view could not tell a local event from a
peer's. Both feeds carry the node now, and the history is bounded by age as well as by count.

| 4.0 preview | 4.0 |
|---|---|
| `DashboardHistoryEntry(SchedulerName, JobGroup, …)` | `DashboardHistoryEntry(SchedulerName, SchedulerInstanceId, JobGroup, …)` |
| — | `DashboardMisfireEntry(SchedulerName, SchedulerInstanceId, TriggerGroup, TriggerName, JobKeyDto? JobKey, MisfiredAtUtc, DateTimeOffset? ScheduledFireTimeUtc)` (new) |
| — | `DashboardMisfireQuery : PagedQuery`, with `SchedulerName`, `SchedulerInstanceId` and `TriggerFilter` (new) |
| `DashboardHistoryQuery` | gained `string? SchedulerInstanceId` — null lists every node's |
| `IDashboardHistoryStore` | gained `AddMisfire`, `QueryMisfires(DashboardMisfireQuery)` and `CountMisfires(name, since)` |
| `IQuartzApiClient` | gained `QueryMisfires(DashboardMisfireQuery)` → `PagedResult<DashboardMisfireEntry>` and `CountMisfires(name, since)` → `int`, which is what the overview's misfire tile reads |
| `QuartzDashboardOptions` | gained `TimeSpan HistoryRetention` (24 hours) and `int HistoryMaxEntriesPerScheduler` (2000) |
| `DashboardHistoryPlugin(IServiceProvider)` | `DashboardHistoryPlugin(IServiceProvider, TimeProvider)`, and it implements `ITriggerListener` as well as `IJobListener` |
| `SchedulerStateDto(SchedulerName, Status)` | `SchedulerStateDto(SchedulerName, SchedulerInstanceId, Status)` |
| `SchedulerErrorDto(SchedulerName, Message, …)` | `SchedulerErrorDto(SchedulerName, SchedulerInstanceId, Message, …)` |
| `JobEventDto(JobKey, …)`, `JobExecutionResultDto(JobKey, …)`, `TriggerEventDto(TriggerKey, …)` | each leads with `string SchedulerInstanceId` |
| `IQuartzDashboardHubClient.TriggerPaused(TriggerKeyDto)` / `TriggerResumed` | take `TriggerLifecycleDto(SchedulerInstanceId, TriggerKey)` |
| `IQuartzDashboardHubClient.JobPaused(JobKeyDto)` / `JobResumed` | take `JobLifecycleDto(SchedulerInstanceId, JobKey)` |

**`IDashboardHistoryStore` is public and is the documented persistence seam, so the three new members
are a breaking change for anyone who implemented it.** A store that records no misfires answers with an
empty page and a count of zero; the History page renders the section saying so.

A pause and a resume get a payload of their own rather than growing `JobKeyDto` / `TriggerKeyDto`:
those are keys, `IQuartzApiClient` uses them everywhere, and a key does not belong to a node.

The two new options are what bounds the shipped in-memory store. It was bounded by count alone, which
says nothing about a scheduler that has gone quiet — it keeps whatever it last recorded, so its page
shows executions from an arbitrary distance in the past. The window is measured on the scheduler's
`TimeProvider` and applied when history is read as well as when it is written.

## A scheduler can be authorized on its own name

Both management surfaces served every scheduler in the process behind one uniformly applied policy, so a
tenant who could reach one scheduler could reach all of them. Each now takes a policy of its own that is
evaluated per request against the scheduler the request is for.

| 4.0 preview | 4.0 |
|---|---|
| — | `Quartz.SchedulerResource(string SchedulerName)`, in `Quartz.AspNetCore` (new) |
| — | `QuartzHttpApiOptions.SchedulerAuthorizationPolicy` (new, `null`) |
| — | `QuartzDashboardOptions.SchedulerAuthorizationPolicy` (new, `null`) |

Nothing is removed and nothing changes for an application that leaves them null: the API is still whatever
`RequireAuthorization(…)` the application put on the mapped group, and the dashboard is still whatever
`QuartzDashboardOptions.AuthorizationPolicy` says.

Set one and Quartz asks
`IAuthorizationService.AuthorizeAsync(user, new SchedulerResource(name), policy)` — ASP.NET Core's own
resource-based authorization, so the application writes one
`AuthorizationHandler<TRequirement, SchedulerResource>` and there is no Quartz callback type or claim-name
convention to learn. The API answers `403` with problem details, decided before the scheduler is looked up
so that a `404` only ever answers a name the caller was allowed to ask about, and filters
`GET {ApiPath}/schedulers` to what the caller may act on. The dashboard filters its scheduler picker and
its Schedulers page, renders a *not authorized* frame instead of a page for a scheduler the visitor fails
for, and refuses the live-events hub group for it. Setting either option in a container with no
authorization services is refused at startup.

See [Authorizing a tenant on its own scheduler](multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler)
for the handler and the registration.

## `CheckExists` is `Exists`

Both overloads, on `IScheduler` and `IJobStore`:

```diff
- if (await scheduler.CheckExists(jobKey)) { … }
+ if (await scheduler.Exists(jobKey)) { … }
```

"Check" said only that the member does what calling it does; the return value already answers the
question. The HTTP API routes (`…/jobs/{group}/{name}/exists`) are unchanged.

## Names that were normalized

Renames only — the behavior behind each is unchanged, and a rename that also changes a configuration key is
called out.

| 3.x | 4.x |
|---|---|
| `QuartzScheduler.NumJobsExecuted` | `NumberOfJobsExecuted` (the type is internal now — read `IScheduler.GetMetadata()`) |
| `QuartzScheduler.JobStoreClass`, `.ThreadPoolClass` | `JobStoreType`, `ThreadPoolType` (they return a `Type`; the type is internal now) |
| `JobStoreSupport.UseDBLocks`, `.SelectWithLockSQL` | `UseDbLocks`, `SelectWithLockSql` |
| `DBSemaphore.SQL`, `.InsertSQL`, `.ExecuteSQL` | `LockSql`, `InsertSql` (both readable now), `ExecuteSql` — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| `DbMetadata.Init()` | Gone entirely: `DbMetadata` is an init-only record now, and the reflection its description implies happens internally instead of in a second phase you had to remember. `UseGenericDatabase`'s describing overloads take a `Func<DbMetadata>` returning `new DbMetadata { … }`; the dead `ParameterIsNullableProperty` went too |
| `DbMetadata.DbBinaryType` and `.ParameterDbTypeProperty` are internal | Everything else on the record is something you *say* about a driver; these two were the lookups Quartz then performed — an `Enum.Parse` of `DbBinaryTypeName` against `ParameterDbType`, and a `GetProperty(ParameterDbTypePropertyName)` on `ParameterType`. Describe the driver with the four naming properties, which are unchanged and public; the resolved results are Quartz's business |
| `AdoConstants.ColumnMifireInstruction` | `ColumnMisfireInstruction` (a typo; the column name is unchanged) |
| `SchedulerConstants.FailedJobOriginalTriggerFiretime`, `…ScheduledFiretime` | `…TriggerFireTime`, `…ScheduledFireTime` (the string values are unchanged) |
| `SchedulingOptions.OverWriteExistingData` | `OverwriteExistingData`. The configuration key is spelled `Quartz:Scheduling:OverwriteExistingData` now; keys are matched case-insensitively, so an existing file keeps binding, but code assigning the property has to change |
| `RedisSemaphore.LockTtlMilliseconds`, `.LockRetryIntervalMilliseconds` | `RedisLockHandler.LockTimeToLive`, `.LockRetryInterval`, both `TimeSpan` — **also the config keys `lockTtlMilliseconds` → `lockTimeToLive` and `lockRetryIntervalMilliseconds` → `lockRetryInterval`** |
| `IObjectSerializer.DeSerialize` | `Deserialize`. `IObjectSerializer.Initialize()` went at the same time: a serializer builds whatever it needs on first use, and nothing was left for a separate initialization call to do |
| `TriggerFiredBundle.PrevFireTimeUtc` | `PreviousFireTimeUtc`, matching the spelling used everywhere else. The type is a required-init record now: the eight-positional constructor ended in three interchangeable `DateTimeOffset?` values, so transposing `scheduledFireTimeUtc` and `previousFireTimeUtc` compiled cleanly and reported wrong fire times to every listener. A custom job store's `TriggerFired` writes `new TriggerFiredBundle { JobDetail = …, Trigger = …, Recovering = …, FireTimeUtc = …, ScheduledFireTimeUtc = …, PreviousFireTimeUtc = …, NextFireTimeUtc = … }`; only `Calendar` is optional |
| `Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin` | `Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin` — the namespace moved and the casing follows .NET rules. A `quartz.plugin.<name>.type` naming either old spelling still resolves, with a warning. Its nested `JobFile` class and its `JobFiles` property are internal now: they are how the plugin tracks what it has read, not something to call. So are `FileNames`, `ScanInterval`, `FailOnFileNotFound`, `FailOnSchedulingError`, `Name` and `Scheduler`, and `ProcessFile` is private — see [The two scheduling-data plugins have one surface](#the-two-scheduling-data-plugins-have-one-surface) |
| `XMLSchedulingDataProcessorPlugin.ProcessFile(fileName)` | `FileUpdated(fileName)`, the `IFileScanListener` member the file-scan job calls. The plugin-typed one is private |
| `Quartz.Xml.ValidationException` | `Quartz.SchedulingDataValidationException`. The old name collided with `System.ComponentModel.DataAnnotations.ValidationException` in any file that used both, and it was never XML-specific — the JSON processor throws it too. Its `ValidationExceptions` is an `IReadOnlyList<Exception>`; it was a `List<Exception>` a caller could add to |

### One spelling per constant

Three values were public twice under two names. The values are unchanged; one spelling of each survives,
the one that sits next to the thing it describes.

| Removed | Write instead |
|---|---|
| `SchedulerConstants.DefaultGroup` | `JobKey.DefaultGroup` / `TriggerKey.DefaultGroup` (both `Key<T>.DefaultGroup`, still `"DEFAULT"`) |
| `AdoJobStoreOptions.DefaultTablePrefix` | `AdoConstants.DefaultTablePrefix`, alongside the table and column names it prefixes (still `"QRTZ_"`) |
| `TaskSchedulingThreadPool.DefaultMaxConcurrency` (`protected`) | `ThreadPoolOptions.DefaultMaxConcurrency`, where the option it defaults is (still `10`) |

### Abbreviated parameter names were spelled out

Parameter names inherited from the Java port were spelled out across the public surface. Only named
arguments and overriding signatures are affected — a positional call site compiles unchanged.

`cal` → `calendar`, `sched` → `scheduler`, `schedName` → `schedulerName`, `calName` → `calendarName`,
`schedInstId` → `schedulerInstanceId`, `triggerInstCode` / `instCode` → `triggerInstructionCode` /
`instructionCode`, `jec` → `context`, `prevFireTimeUtc` → `previousFireTimeUtc`, `tz` / `timezone` →
`timeZone` on the `InTimeZone` schedule-builder methods, and `je` → `jobExecutionException`.

Every abbreviated constructor parameter of `SchedulerMetaData` was spelled out as well, on what is now
[`SchedulerMetadata`](#schedulermetadata-replaces-schedulermetadata): `schedInst` → `schedulerInstanceId`,
`schedType` → `schedulerType`, `numberOfJobsExec` → `numberOfJobsExecuted`, `jsType` → `jobStoreType`,
`jsPersistent` → `jobStoreSupportsPersistence`, `jsClustered` → `jobStoreClustered`, `tpType` →
`threadPoolType`, `tpSize` → `threadPoolSize`.

## Matchers moved to `Quartz`

A matcher is something you hand to `IScheduler`, not an implementation detail of one, and every signature that
takes one already lives in `Quartz`, so the types moved there rather than into a namespace of their own:

```diff
- using Quartz.Impl.Matchers;
```

`GroupMatcher<T>`, `NameMatcher<T>`, `KeyMatcher<T>`, `EverythingMatcher<T>`, `AndMatcher<T>`, `OrMatcher<T>`,
`NotMatcher<T>`, `StringMatcher<T>` and `StringOperator` all moved. `GroupMatcher<T>` and `NameMatcher<T>`
keep their factory methods (`GroupEquals`, `NameStartsWith`, `AnyGroup`, …), and
`NameMatcher<TKey>.AnyName()` is new, the counterpart of `GroupMatcher<TKey>.AnyGroup()`.

`IMatcher<T>` no longer redeclares `Equals(object)` and `GetHashCode()`. They are `object`'s own members, so
declaring them on the interface added no requirement and told an implementer nothing — but a matcher is still
expected to behave as a value, so that comparing two of them, or holding them in a set, answers to the shape
of the matcher rather than to its identity.

### `Matchers` is the entry point; combinators are extensions

Building a matcher used to start from whichever concrete type held the factory you needed, and some of
those statics ignored the class's own type parameter — `EverythingMatcher<JobKey>.AllTriggers()` compiled,
and answered a matcher for a different key type than the one you named. The roots now live on one
non-generic entry class, and the combinators are extension methods on any `IMatcher<TKey>`, so an
expression reads left to right:

```csharp
IMatcher<JobKey> matcher = Matchers.Group<JobKey>(StringOperator.StartsWith, "reporting")
    .And(Matchers.Name<JobKey>(StringOperator.Contains, "cleanup").Not());

scheduler.ListenerManager.AddJobListener(listener, Matchers.AllJobs());
```

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `EverythingMatcher<JobKey>.AllJobs()` | `Matchers.AllJobs()` |
| `EverythingMatcher<TriggerKey>.AllTriggers()` | `Matchers.AllTriggers()` |
| — | `EverythingMatcher<TKey>.All()` — the generic form, matching the class's own key type |
| `KeyMatcher<JobKey>.KeyEquals(key)` | `Matchers.Key(key)` (overloaded for `JobKey` and `TriggerKey`) |
| `AndMatcher<JobKey>.And(left, right)` | `left.And(right)` |
| `OrMatcher<JobKey>.Or(left, right)` | `left.Or(right)` |
| `NotMatcher<JobKey>.Not(matcher)` | `matcher.Not()` |
| — | `Matchers.Group<TKey>(StringOperator, string)`, `Matchers.Name<TKey>(StringOperator, string)` |

The concrete matcher types stay public — they are what the expressions above return, and what a custom
`IMatcher<TKey>` composes with — but they no longer construct themselves: `Matchers` and the extensions
are the one way to build them.

### `StringOperator` exposes properties and a name

The five built-in operators (`Equality`, `StartsWith`, `EndsWith`, `Contains`, `Anything`) are static
get-only properties now; they were `public static readonly` fields. Call sites compile unchanged.
`StringOperator` also gained an abstract `Name` property that discriminates the operator — it is what
identifies an operator when a matcher crosses a process boundary, and what `ToString()` returns. A custom
`StringOperator` subclass must now implement `Name` alongside `Evaluate`.

## `Key<T>` moved to `Quartz` and is immutable

`JobKey` and `TriggerKey` are part of the public model, and their base type sat in a utility namespace:

```diff
- using Quartz.Util;   // for Key<T>
+ // Key<T> is in Quartz, alongside JobKey and TriggerKey
```

The `Name` and `Group` setters are gone. A key is a value, and it is a dictionary key throughout the job
stores — mutating one in place could move an entry out of reach of its own hash bucket. Build a new key
instead:

```diff
- jobKey.Group = "reports";
+ jobKey = new JobKey(jobKey.Name, "reports");
```

`JobKey.Create` is gone too; use the constructors, which is what `TriggerKey` always offered:

```diff
- JobKey key = JobKey.Create("myJob", "reports");
+ JobKey key = new JobKey("myJob", "reports");
```

Payloads written by earlier versions still read: both serializers build a key through its
`(name, group)` constructor now, and the JSON itself is unchanged.

`JobType`, the type of `IJobDetail.JobType`, moved from `Quartz.Impl` to `Quartz` for the same reason.

Keys can also be sorted now. `JobKey` and `TriggerKey` implement `IComparable<JobKey>` /
`IComparable<TriggerKey>` and `Key<T>` implements the non-generic `IComparable`, so
`Comparer<JobKey>.Default` finds a real comparison. On 3.x only `IComparable<Key<T>>` was there, which
the default comparer does not recognise, and `keys.Sort()`, `keys.OrderBy(k => k)`,
`SortedSet<JobKey>` and `SortedDictionary<JobKey, _>` all compiled and then threw at runtime. The
rule that puts the default group first is unchanged; the comparison under it is not. 3.x compared group
and name with `CultureInfo.CurrentCulture.CompareInfo`, and 4.x compares them with
`StringComparer.Ordinal` — so a sorted listing reorders, most visibly by putting every upper-case letter
before every lower-case one. The order no longer depends on the machine's culture, which is the point,
but it is a different order from the one 3.x produced.

## Listing queries can filter by name

| Query | New property |
|---|---|
| `JobQuery` | `NameMatcher<JobKey>? Name` |
| `TriggerQuery` | `NameMatcher<TriggerKey>? Name` |
| `JobGroupQuery` | `string? Name` — one group, matched exactly |
| `TriggerGroupQuery` | `string? Name` — one group, matched exactly |
| `CalendarQuery` | `CalendarNameMatcher? Name` |

```csharp
PagedResult<JobHeader> nightly = await scheduler.QueryJobs(new JobQuery
{
    Group = GroupMatcher<JobKey>.GroupEquals("reports"),
    Name = NameMatcher<JobKey>.NameStartsWith("nightly")
});
```

The filters combine with AND. `RAMJobStore` and `StdAdoDelegate` both honor them; the ADO store escapes the
matcher's own wildcards in the LIKE it generates, so a job literally named `50%` is matched literally and is
not a pattern. Over HTTP the job, trigger and calendar listings take `nameEquals`, `nameStartsWith`,
`nameEndsWith` or `nameContains` (at most one), and the group listings take `name`.

Calendars filter by name too, and they are the one listing whose filter is not a `NameMatcher<TKey>`: a
calendar is identified by a bare string rather than a `Key<T>`, so `CalendarQuery.Name` is a
`CalendarNameMatcher`, with the same four factories:

```csharp
PagedResult<string> holidays = await scheduler.QueryCalendarNames(new CalendarQuery
{
    Name = CalendarNameMatcher.NameStartsWith("holiday-")
});
```

There is no `AnyName()` on it: the property is nullable, and null already means every calendar.

If you wrote a dialect delegate that overrides `StdAdoDelegate.ToSqlLikeClause<T>(StringMatcher<T>)`,
move the override to `ToSqlLikeClause(StringOperator, string)`. That overload is the virtual one now,
so that a calendar's matcher and a key's matcher translate through the same code; the generic form
forwards to it and is no longer virtual.

`IsJobGroupPaused` and `IsTriggerGroupPaused` are built on the group-name filter now, so they ask the store
about the one group instead of listing every paused group and searching it.

`PagedResult<T>.Items` is an `IReadOnlyList<T>` rather than a `List<T>` — a page is a result to read, not a
list to mutate. The two `List<T>` members a caller is most likely to have used:

```diff
- IReadOnlyList<JobKey> keys = result.Items.ConvertAll(x => x.Key);
+ List<JobKey> keys = result.Items.Select(x => x.Key).ToList();

- bool found = result.Items.Exists(x => x.Name == group);
+ bool found = result.Items.Any(x => x.Name == group);
```

## `ISchedulerRepository` overloads collapsed

| 3.x | 4.x |
|---|---|
| `Bind(scheduler)`, `Bind(scheduler, instanceId)` | `Bind(IScheduler scheduler, string? instanceId = null)` |
| `Lookup(name)`, `Lookup(name, instanceId)` | `Lookup(string schedulerName, string? instanceId = null)` |
| `Remove(name)` → `void`, `Remove(name, instanceId)` → `bool` | `Remove(string schedulerName, string? instanceId = null)` → `bool` |

Existing calls compile unchanged. A null instance ID means the same as the old one-argument overload: bind
under the scheduler's own `SchedulerInstanceId`, and look up or remove the first scheduler registered under the
name. `Remove` returns `bool` in both shapes now, where the one-argument form used to return nothing.
`LookupAll` and `LookupByName` are unchanged — cluster scenarios disambiguate through them.

## Trigger fire state is read-only on the interfaces

```diff
- int Priority { get; set; }   // ITrigger
+ int Priority { get; }

- int TimesTriggered { get; set; }   // ISimpleTrigger, ICalendarIntervalTrigger,
+ int TimesTriggered { get; }        // IDailyTimeIntervalTrigger, IRecurrenceTrigger
```

`IMutableTrigger.Priority` still has its setter, which is what `TriggerBuilder.WithPriority` and the job stores
go through. `TimesTriggered` is fire-count state the scheduler maintains; the concrete `SimpleTriggerImpl`,
`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl` keep their settable
property. A trigger handed back by `IScheduler.GetTrigger` is a snapshot, so writing to either never reached
the store to begin with — to change a stored trigger, build a new one and reschedule.

The built-in JSON trigger serializers are typed on the concrete triggers now, because they restore
`TimesTriggered` when deserializing. A custom serializer deriving from one of them has to follow:

```diff
- public class MySimpleTriggerSerializer : TriggerSerializer<ISimpleTrigger>
+ public class MySimpleTriggerSerializer : TriggerSerializer<SimpleTriggerImpl>
```

This applies to `SimpleTriggerSerializer`, `CalendarIntervalTriggerSerializer`,
`DailyTimeIntervalTriggerSerializer` and `RecurrenceTriggerSerializer`, in both
`Quartz.Serialization.SystemTextJson.Triggers` and `Quartz.Serialization.Newtonsoft.Triggers`. `CronTriggerSerializer`
is unchanged — it has no fire-count state to restore.

## The trigger family interfaces are read models

The same reasoning removes the rest of the schedule setters from the five family interfaces. Setting one on a
trigger you got back from the scheduler compiled, ran, and changed nothing: `RAMJobStore.GetTrigger` hands out
`Trigger.Clone()` and the ADO.NET store materializes a fresh instance per read, so every trigger the scheduler
gives you is a detached copy.

| 3.x | 4.x |
|---|---|
| `int ISimpleTrigger.RepeatCount { get; set; }` | `{ get; }` |
| `TimeSpan ISimpleTrigger.RepeatInterval { get; set; }` | `{ get; }` |
| `string? ICronTrigger.CronExpressionString { get; set; }` | `{ get; }` |
| `TimeZoneInfo ICronTrigger.TimeZone { get; set; }` | `{ get; }` |
| `IntervalUnit ICalendarIntervalTrigger.RepeatIntervalUnit { get; set; }` | `{ get; }` |
| `int ICalendarIntervalTrigger.RepeatInterval { get; set; }` | `{ get; }` |
| `IReadOnlyCollection<DayOfWeek> IDailyTimeIntervalTrigger.DaysOfWeek { get; set; }` | `{ get; }` |
| `string IRecurrenceTrigger.RecurrenceRule { get; set; }` | `{ get; }` |
| `TimeZoneInfo IRecurrenceTrigger.TimeZone { get; set; }` | `{ get; }` |

There are two sanctioned ways to change a stored trigger, and both were already public:

```diff
  ICronTrigger t = (ICronTrigger) await scheduler.GetTrigger(key);

- t.CronExpressionString = "0 0 12 * * ?";                 // compiled, did nothing
+ ITrigger updated = t.GetTriggerBuilder()
+     .WithCronSchedule("0 0 12 * * ?")
+     .Build();
+ await scheduler.RescheduleJob(key, updated);             // reshape the schedule

+ await scheduler.UpdateTriggerDetails(key,                // edit metadata in place
+     new TriggerDetailsUpdate().WithDescription("noon"));
```

Code that owns a concrete `CronTriggerImpl` / `SimpleTriggerImpl` / … is unaffected: `TriggerBase` and the
concrete triggers keep their public setters, and `IMutableTrigger` — the contract job stores and custom
trigger authors write through — is unchanged.

`ICalendar` deliberately keeps its two setters (`Description`, `CalendarBase`). It is an implementable SPI:
the built-in calendar serializers assign through the interface while rebuilding a calendar, so they are part
of its contract in a way the trigger setters never were.

**`ITrigger` no longer implements `IComparable<ITrigger>`**, and neither do the five family interfaces that
re-declared it. It compared keys, which is what `ITrigger.Key.CompareTo` says out loud; ordering triggers by
identity is rarely what a caller wants, and where it is, `Key` is a `Key<T>` with the full comparison
operator set. Two call shapes change:

```diff
- if (a.CompareTo(b) < 0) { … }
+ if (a.Key.CompareTo(b.Key) < 0) { … }

- triggers.Sort();                       // List<ITrigger>, now throws InvalidOperationException
+ triggers.Sort((x, y) => x.Key.CompareTo(y.Key));
```

The second one is the one to watch: `List<ITrigger>.Sort()`, `SortedSet<ITrigger>`, `OrderBy(t => t)` and
anything else that reaches for `Comparer<ITrigger>.Default` still **compile**, and throw at run time.
Comparing by fire time — which is usually the intent — was never what this gave you; sort on
`NextFireTimeUtc` explicitly. `TriggerBase.CompareTo(ITrigger)` went with the interface.

If you author an `ITriggerPersistenceDelegate` of your own, `TriggerPropertyBundle` no longer carries the
parallel `StatePropertyNames` / `StatePropertyValues` arrays that were applied to the trigger by
reflection. The bundle takes the schedule builder plus an optional applier delegate, checked by the
compiler instead of resolved from strings at trigger-load time:

```diff
- return new TriggerPropertyBundle(sb, ["timesTriggered"], [timesTriggered]);
+ return new TriggerPropertyBundle(sb, t => ((SimpleTriggerImpl) t).TimesTriggered = timesTriggered);
```

The single-argument constructor is unchanged for a delegate that carries no state beyond the schedule —
the Cron delegate passes none, and a null applier is simply skipped. The lambda casts to the concrete
trigger type because the family interfaces (`ISimpleTrigger` and its siblings) expose `TimesTriggered`
get-only; the four `Quartz.Impl.Triggers` trigger classes stay public with public `TimesTriggered`
setters precisely so this write path exists.

Registering the delegate is a typed builder call now, discoverable by dot-typing from the store builder
like the driver delegate and lock handler beside it:

```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseTriggerPersistenceDelegate<MyTriggerPersistenceDelegate>();
    // or, when it needs configuring first:
    store.UseTriggerPersistenceDelegate(provider => new MyTriggerPersistenceDelegate(...));
}));
```

The delimited `quartz.jobStore.driverDelegateInitString` format this replaces — split on `|` or `\`,
one supported setting under two spellings, type names instantiated by reflection — is gone from the
API: `DelegateInitializationArgs.InitString` was replaced by a typed `DriverDelegateContext.TriggerPersistenceDelegates`
collection, and `AdoJobStoreOptions.DriverDelegateInitString` went with it (as did the
`AdoJobStoreBase` property mirroring it). **The legacy key itself keeps working**: the property bridge
translates `quartz.jobStore.driverDelegateInitString = triggerPersistenceDelegateTypes=...` (and the
older `triggerPersistenceDelegateClasses` spelling, with both of its list separators) into the same
registrations `UseTriggerPersistenceDelegate<T>()` produces. A misspelled setting name inside the
string is rejected at `AddQuartz` time instead of at store startup.

## `CronExpression` is immutable

`CronExpression` always read like a value — value equality, a get-only expression string — but it was an
open class with a settable `TimeZone` and a `protected` parser underneath. It is a **`sealed` immutable
value** now: the time zone arrives through a constructor or through `WithTimeZone`, which returns a retimed
copy.

| 3.x | 4.x |
|---|---|
| `expr.TimeZone = tz;` | `expr = expr.WithTimeZone(tz);` |
| `new CronExpression(s) { TimeZone = tz }` | `new CronExpression(s, tz)` |
| `expr.GetTimeAfter(d)` | `expr.GetNextValidTimeAfter(d)` — the two were verbatim aliases; one name remains |
| `expr.GetFinalFireTime()` | Removed — it was never implemented and always returned `null` |
| `expr.Clone()` | Removed — the type is sealed and immutable, so reuse the instance |
| `CronExpression.MaxYear` | Removed — it was `DateTime.Now.Year + 100` evaluated once per process, so its value depended on when the process started. The cap is internal; a schedule that has to stop at a year should say which year |

`null` still means the system's local time zone, and an expression that was never given a zone still
serializes with the local zone's id, so persisted payloads keep their meaning.

The setter's removal also fixes a real defect: `CronScheduleBuilder` handed the *same* `CronExpression`
instance to every trigger it built, so calling `InTimeZone` after the first `Build()` silently retimed
triggers that were already built — and writing `TimeZone` on an expression you had passed to a builder
retimed the builder behind your back. Both are now impossible: the builder reshapes its own copy, and
already-built triggers keep the zone they were built with.

`CronCalendar` had a defect of its own in the same plumbing:
`new CronCalendar(baseCalendar, expression, timeZone)` handed the zone to `BaseCalendar` but built the
expression without it, and `CronCalendar.TimeZone` reads off that expression — so the argument was
dropped and the calendar excluded the *local* machine's hours while reporting the local zone back
([#3321](https://github.com/quartznet/quartznet/issues/3321)). The constructor now builds the
expression with the zone. Assigning `TimeZone` after construction always worked and still does, and
the stored form is unchanged — the zone has always been persisted with the nested expression, so
**no migration is needed**. What changes is which times such a calendar excludes: code that passed a
zone and quietly got local-time exclusions now gets what it asked for.

`CronTriggerImpl.FinalFireTimeUtc` now returns `null` directly for a trigger with no end time, which is the
value it always produced through `GetFinalFireTime`.

`Clone()` went with the setter. A copy of an immutable sealed value is an allocation and nothing else, so
the two places in Quartz that cloned one — `CronCalendar.Clone` and `CronTriggerImpl.Clone` — share the
instance instead. If you called it, drop the call.

### The parser is not a subclassing seam

3.x's `CronExpression` was an open `public class` that handed a derived type the whole parser: eleven
`protected const` field indices (`Second`, `Minute`, `Hour`, `DayOfMonth`, `Month`, `DayOfWeek`, `Year`,
`AllSpec`, `AllSpecInt`, `NoSpec`, `NoSpecInt`), thirteen `protected` fields holding the parsed sets
(`seconds`, `minutes`, `hours`, `daysOfMonth`, `months`, `daysOfWeek`, `years`, `lastdayOfWeek`,
`nthdayOfWeek`, `everyNthWeek`, `calendardayOfWeek`, `calendardayOfMonth`, `expressionParsed`) and twelve
`protected virtual` parse and arithmetic hooks (`AddToSet`, `BuildExpression`, `CheckNext`,
`GetExpressionSetSummary`, `GetLastDayOfMonth`, `GetSet`, `GetTime`, `IsLeapYear`, `SkipWhiteSpace`,
`StoreExpressionVals`, `CreateDateTimeWithoutMillis`, `SetCalendarHour`). Its public members were `virtual`
for the same reason.

None of it survives. The type is `sealed`, so those fields are private and the `virtual` keywords are gone.
The lowercase field names give the seam away as an artifact of the Java port rather than a design: it
exposed the mid-parse state of a value type, and any override that touched it could leave an expression
that parsed to something its own string does not say. To vary cron syntax, produce a Quartz expression
string — `CronExpressionBuilder` builds one field by field — or write a trigger type of your own; do not
reach into the parser.

`CronExpression` also stops implementing `IDeserializationCallback`, so `OnDeserialization(object?)` is
gone from its surface. It keeps `[Serializable]`, `ISerializable` and `GetObjectData`, because a
`CronCalendar` inside a 3.x `CALENDARS` blob is made of one — see
[`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it).
The re-parse that `OnDeserialization` used to perform happens in the deserialization constructor now,
which is where it belongs on a type whose fields are set once. Blobs written by 3.x still read back: the
payload is the expression string and the time zone id, unchanged.

### `CronExpression` parses without throwing, and says it is equatable

`IsValidExpression` was a `try`/`catch` around the constructor and `ValidateExpression` was a
construction it discarded, which is the shape `TryParse` and `Parse` exist to replace. Those are the
members now, alongside `IParsable<CronExpression>` — the same pair `JobKey` and `TriggerKey` already had.

```csharp
if (CronExpression.TryParse(userInput, out CronExpression? expression))
{
    // expression is non-null here
}

CronExpression parsed = CronExpression.Parse(userInput);   // ArgumentNullException / FormatException
```

**All four validation members are gone**, because each restated one of the two:

| 3.x | 4.0 |
|---|---|
| `CronExpression.IsValidExpression(expr)` | `CronExpression.TryParse(expr, out _)` |
| `CronExpression.ValidateExpression(expr)` | `CronExpression.Parse(expr)` |
| `CronExpression.IsValidExpression(expr, hashKey)` | `CronExpression.TryParseWithHash(expr, hashKey, out _)` |
| `CronExpression.ValidateExpression(expr, hashKey)` | `CronExpression.ParseWithHash(expr, hashKey)` |

The replacements hand back the expression they parsed, so validating one and then using it is a single
parse rather than two. Where the old call was genuinely only a check, discard the result — `out _`, or
the return value of `Parse`.

There are no `ReadOnlySpan<char>` overloads, deliberately: the type keeps its source string, so a span
argument would only be copied back into one.

### The hash key is a `Parse` argument, not a constructor overload

3.x resolved `H` (hash) tokens through two constructors, `CronExpression(string, string hashKey)` and
`CronExpression(string, int hashSeed)`. Between them and `CronExpression(string, TimeZoneInfo?)` they
made the null literal ambiguous, so `new CronExpression(expr, null)` — the documented way to say "the
system's local zone" — did not compile at all. The type keeps two constructors, and the hash resolution
moves to statics beside `Parse`:

```csharp
CronExpression expression = CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup");
CronExpression seeded = CronExpression.ParseWithHash("0 H H(0-7) * * ?", 42);

if (CronExpression.TryParseWithHash(userInput, "nightly-cleanup", out CronExpression? parsed))
{
    // parsed is non-null here
}

CronExpression local = new CronExpression("0 H H(0-7) * * ?", null);   // compiles now
```

Resolving `H` **is** a parse — it reads the expression, and it throws `FormatException` when the
expression is malformed — so `ParseWithHash` is where it belongs. `CronExpression.ResolveHash`, which
returns the resolved string rather than an expression, is unchanged.

Equality was already implemented; it is now declared. `CronExpression` states `IEquatable<CronExpression>`,
and `Equals` takes a nullable argument, as the contract requires. `GetHashCode` hashes the `TimeZone`
property that `Equals` compares rather than the nullable backing field, so an expression that was never
given a zone and one given `TimeZoneInfo.Local` explicitly no longer hash apart while comparing equal.

### The derived calendars declare the equality they implement

`AnnualCalendar`, `CronCalendar`, `DailyCalendar`, `HolidayCalendar`, `MonthlyCalendar` and
`WeeklyCalendar` each had a `public bool Equals(X obj)` that no interface asked for and that could not be
handed a `null`. Each declares `IEquatable<X>` now, with the nullable parameter that entails — matching
`BaseCalendar`, which always did it correctly. Existing calls compile unchanged.

All six are also `sealed`, so there is nothing left to override on them; a calendar of your own derives
from `BaseCalendar`, which stays open and needs the `?` on its `Equals` parameter — see
[The shipped implementations are sealed](#the-shipped-implementations-are-sealed).

### `CronExpressionBuilder`'s list fields take a span

The seven members that take a list of field values — `WithSeconds`, `WithMinutes`, `WithHours`,
`WithDaysOfMonth`, `WithMonths`, `WithDaysOfWeek`, `WithYears` — gained `params ReadOnlySpan<…>`
overloads, so the common call no longer allocates an array per field. The `params` array overloads stay,
so an argument that is already a collection still binds. Nothing at an existing call site changes: C# 13
prefers the span overload in expanded form, and both do the same thing.

### `ITrigger` is Quartz-implemented

The read-model split makes explicit what was always true operationally: Quartz owns the implementations
of `ITrigger`. Build triggers with `TriggerBuilder`; a custom trigger type derives from `TriggerBase`,
which carries the mutable and operational contracts the scheduler and the stores need. An object that
implements only `ITrigger` cannot be scheduled — `ScheduleJob` and `RescheduleJob` used to fail on it with
an `InvalidCastException` from inside the scheduler; they now reject it with a `SchedulerException` that
names the type and says what to do instead. `ITrigger`'s own documentation states the rule.

`IJobStore.ScheduleJobs` follows the rest of the store contract and takes
`IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>>` — the scheduler validates and
downcasts the caller's triggers before the store sees them, so a custom store no longer casts each
`ITrigger` itself. `IScheduler.ScheduleJobs` is unchanged.

## The trigger implementations construct, then initialize

The five `*TriggerImpl` types carried thirty-four constructors between them, spelling out every combination
of name, group, job name, job group, start time, end time and schedule. Every value they set is a settable
property, so each combination was a second way to say the same thing — and the longest of them took nine
positional arguments, most of which read as anonymous at the call site.

Each type now has one no-settings constructor taking an optional `TimeProvider`, and at most one
convenience constructor. Everything else is an object initializer.

| Type | Constructors before | Constructors now |
|---|---|---|
| `SimpleTriggerImpl` | 11 | `(TimeProvider? = null)`, and `(name, group, jobName, jobGroup, startTimeUtc, endTimeUtc, repeatCount, repeatInterval, TimeProvider? = null)` |
| `CronTriggerImpl` | 9 | `(TimeProvider? = null)`, and `(name, group, cronExpression, TimeProvider? = null)` |
| `CalendarIntervalTriggerImpl` | 6 | `(TimeProvider? = null)` |
| `DailyTimeIntervalTriggerImpl` | 6 | `(TimeProvider? = null)` |
| `RecurrenceTriggerImpl` | 2 | `(TimeProvider? = null)`, and `(name, group, recurrenceRule, TimeProvider? = null)` |
| `TriggerBase` (protected) | 5 | `(TimeProvider? = null)` |

Every surviving convenience constructor takes a **non-nullable** `group`. `RecurrenceTriggerImpl` is the
one that had a `string?` there, so `new RecurrenceTriggerImpl(name, null, rule)` becomes a nullability
warning — an error under `TreatWarningsAsErrors`. Name the group you meant, or `TriggerKey.DefaultGroup`
if you had none, or use the object initializer and set `Key`.

The recipe is mechanical: the name and group become `Key`, the job name and group become `JobKey`, and the
rest keep their property names.

```diff
- var trigger = new SimpleTriggerImpl("nightly", "reports", startAt, endAt, 5, TimeSpan.FromHours(1));
+ var trigger = new SimpleTriggerImpl
+ {
+     Key = new TriggerKey("nightly", "reports"),
+     StartTimeUtc = startAt,
+     EndTimeUtc = endAt,
+     RepeatCount = 5,
+     RepeatInterval = TimeSpan.FromHours(1)
+ };
```

Set `StartTimeUtc` before `EndTimeUtc`: each setter validates against the other, exactly as the
constructors did in that order.

One behavioural detail to carry over. The overloads that took no start time — `SimpleTriggerImpl(name)`,
`(name, group)`, `(name, repeatCount, repeatInterval)`, `(name, group, repeatCount, repeatInterval)`, and
the `CalendarIntervalTriggerImpl` / `DailyTimeIntervalTriggerImpl` equivalents — set `StartTimeUtc` to *now*
rather than leaving it at its default. Write that out if you relied on it:

```diff
- var trigger = new SimpleTriggerImpl("nightly", "reports");
+ var trigger = new SimpleTriggerImpl
+ {
+     Key = new TriggerKey("nightly", "reports"),
+     StartTimeUtc = TimeProvider.System.GetUtcNow()
+ };
```

`CronTriggerImpl`'s surviving constructors already set `StartTimeUtc` to the time provider's now and
`TimeZone` to `TimeZoneInfo.Local`, as they always did.

`SimpleTriggerImpl` and `CalendarIntervalTriggerImpl` had both a parameterless constructor and a
`TimeProvider` one; those merged into a single `(TimeProvider? timeProvider = null)`, matching the shape
`CronTriggerImpl` and `DailyTimeIntervalTriggerImpl` already had. `new SimpleTriggerImpl()` still compiles.
What no longer works is a `where T : new()` constraint or `Activator.CreateInstance(type)` over these types
— an all-optional constructor is not a parameterless one as far as the runtime is concerned. Derive a type
of your own and it gets an implicit parameterless constructor, which satisfies both again.

The `[Serializable]` blob contract pins fields, not constructors, so persisted binary payloads are
unaffected.

## Nine `UsingJobData` overloads became one

`JobDataMap`'s indexer takes an `object?`, and every one of the nine primitive `UsingJobData` overloads had
the same one-line body writing through it. The overload set decided nothing except which of nine identical
methods the compiler picked, and it cost forty-four declarations across `IJobConfigurator<TJob>`,
`JobBuilder<TJob>`, `ITriggerConfigurator<TJob>` and `TriggerBuilder<TJob>`. There are twelve now.

| 3.x | 4.x |
|---|---|
| `UsingJobData(string key, string? value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, int value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, long value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, float value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, double value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, decimal value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, bool value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, Guid value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(string key, char value)` | `UsingJobData(string key, object? value)` |
| `UsingJobData(JobDataMap newJobDataMap)` | unchanged — merges into what the builder already holds |
| `UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value)` | unchanged |
| `SetJobData(JobDataMap newJobDataMap)` | removed |

**Existing calls compile and store exactly what they stored before.** An `int` argument still lands in the map
boxed as an `int`, a `Guid` as a `Guid`, a `null` as a `null`. Nothing is converted on the way in, and what a
persistent store can hold is still whatever its serializer round-trips — AdoJobStore's string-only mode,
strings only.

To store a value in a job property's own type — an `int` literal narrowed to the `byte` the property
declares, an enum written as its name — name the property instead of its key:

```csharp
JobBuilder.Create<MyJob>().UsingJobData(job => job.RetryCount, 3)
```

### `SetJobData` is gone

`SetJobData` *replaced* the builder's map where `UsingJobData(JobDataMap)` merges into it: one character of
difference in the call, the opposite meaning. Replacing is only what a job store rebuilding a stored job
wants, so it is internal now.

```diff
- JobBuilder.Create<MyJob>().UsingJobData("a", 1).SetJobData(map)   // "a" silently discarded
+ JobBuilder.Create<MyJob>().UsingJobData(map)                      // merge, or start from a fresh builder
```

## One family of `WithXSchedule` extensions

Attaching a schedule to a trigger was spread over six static extension classes and twenty-nine methods, half
of them existing only because `TriggerBuilder<TJob>` and `ITriggerConfigurator<TJob>` each needed their own
copy of the same body. `Quartz.TriggerConfiguratorExtensions` replaces all six with twelve methods that are
generic in the receiver and return it unchanged, so one method serves both and the chain keeps its type.

Deleted: `SimpleScheduleTriggerBuilderExtensions`, `CronScheduleTriggerBuilderExtensions`,
`CalendarIntervalTriggerBuilderExtensions`, `DailyTimeIntervalTriggerBuilderExtensions`,
`RecurrenceTriggerBuilderExtensions`, `TriggerExtensions`.

| 3.x | 4.x |
|---|---|
| `WithSimpleSchedule()` | `WithSimpleSchedule()` |
| `WithSimpleSchedule(Action<SimpleScheduleBuilder>)` | `WithSimpleSchedule(Action<SimpleScheduleBuilder>? configure = null)` |
| `WithSimpleSchedule(SimpleScheduleBuilder)` | `WithSimpleSchedule(SimpleScheduleBuilder schedule)` |
| `WithCronSchedule(string)` | `WithCronSchedule(string cronExpression, Action<CronScheduleBuilder>? configure = null)` |
| `WithCronSchedule(string, Action<CronScheduleBuilder>)` | same member |
| `WithCronSchedule(string expr, string hashKey)` | `WithCronSchedule(CronExpression.ParseWithHash(expr, hashKey))` |
| `WithCronSchedule(string expr, string hashKey, Action<CronScheduleBuilder>)` | `WithCronSchedule(CronExpression.ParseWithHash(expr, hashKey), configure)` |
| `WithCronSchedule(CronScheduleBuilder)` | `WithCronSchedule(CronScheduleBuilder schedule)` |
| — | `WithCronSchedule(CronExpression cronExpression, Action<CronScheduleBuilder>? configure = null)` — new; also the home of the hash-key shape |
| — | `WithCronSchedule(CronExpressionBuilder cronExpression, Action<CronScheduleBuilder>? configure = null)` — new; closes the [`CronExpressionBuilder`](#cronschedulebuilder-s-convenience-factories-are-gone) chain without naming `CronScheduleBuilder` |
| `WithCalendarIntervalSchedule()` | `WithCalendarIntervalSchedule(Action<CalendarIntervalScheduleBuilder>? configure = null)` |
| `WithCalendarIntervalSchedule(Action<CalendarIntervalScheduleBuilder>)` | same member |
| `WithCalendarIntervalSchedule(CalendarIntervalScheduleBuilder)` | `WithCalendarIntervalSchedule(CalendarIntervalScheduleBuilder schedule)` |
| `WithDailyTimeIntervalSchedule()` | `WithDailyTimeIntervalSchedule(Action<DailyTimeIntervalScheduleBuilder>? configure = null)` |
| `WithDailyTimeIntervalSchedule(Action<DailyTimeIntervalScheduleBuilder>)` | same member |
| `WithDailyTimeIntervalSchedule(int interval, IntervalUnit unit, Action<…>? action = null)` | `WithDailyTimeIntervalSchedule(x => x.WithInterval(interval, unit))` |
| `WithDailyTimeIntervalSchedule(DailyTimeIntervalScheduleBuilder)` | `WithDailyTimeIntervalSchedule(DailyTimeIntervalScheduleBuilder schedule)` |
| `WithRecurrenceSchedule(string)` | `WithRecurrenceSchedule(string recurrenceRule, Action<RecurrenceScheduleBuilder>? configure = null)` |
| `WithRecurrenceSchedule(string, Action<RecurrenceScheduleBuilder>)` | same member |
| `WithRecurrenceSchedule(RecurrenceScheduleBuilder)` | `WithRecurrenceSchedule(RecurrenceScheduleBuilder schedule)` |

Only two call shapes need editing:

```diff
- .WithDailyTimeIntervalSchedule(interval: 10, intervalUnit: IntervalUnit.Second)
+ .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))

- .WithCronSchedule("0 H H(0-7) * * ?", "nightly-cleanup")
+ .WithCronSchedule(CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup"))
```

The hash-key overloads went because a hash key belongs to the expression, not to the way the expression is
attached to a trigger — [`CronExpression.ParseWithHash`](#the-hash-key-is-a-parse-argument-not-a-constructor-overload)
takes it, and the `CronExpression`-taking overload carries the result. Without a key, `H` tokens still hash on the trigger's identity.

### `ITriggerConfigurator` gained a non-generic base

The extensions are written against a new non-generic `ITriggerConfigurator`, which holds the one member they
need:

```csharp
public interface ITriggerConfigurator
{
    ITriggerConfigurator WithSchedule(IScheduleBuilder scheduleBuilder);
}

public interface ITriggerConfigurator<TJob> : ITriggerConfigurator where TJob : IJob { … }
```

The generic interface redeclares `WithSchedule` with its own return type, so a chain there keeps `TJob` and
the job-property overload of `UsingJobData` that comes with it. Code implementing `ITriggerConfigurator<TJob>`
is unaffected; `TriggerBuilder<TJob>` implements both.

## `ModifiedByCalendar` is `WithCalendarName`

```diff
  ITrigger trigger = TriggerBuilder.Create()
      .WithIdentity("trigger1")
-     .ModifiedByCalendar("myHolidays")
+     .WithCalendarName("myHolidays")
      .Build();
```

It sets `ITrigger.CalendarName` and every other setter on the builder is named for the property it sets. The
rename applies to `TriggerBuilder<TJob>` and `ITriggerConfigurator<TJob>` alike. The old name also read as
though it modified the calendar.

## The preferred node is a value

Node affinity was two properties that only made sense read together: a `string?` in which `"*"` meant
something other than a node name, and a `bool` that meant nothing unless the string was one. Copying a pin
from one trigger to another through the setter silently dropped the flag, turning an auto-claim into a named
pin that would never fail over. `Quartz.PreferredNode` — a `readonly record struct` — carries both.

| 3.x | 4.x |
|---|---|
| `string? ITrigger.PreferredNode` | `PreferredNode ITrigger.PreferredNode` |
| `bool ITrigger.IsPreferredNodeAuto` | `trigger.PreferredNode.IsAutomatic` |
| `trigger.PreferredNode` (the node name) | `trigger.PreferredNode.Node` — null for no pin *and* for an unclaimed auto-pin |
| `trigger.PreferredNode is null` | `trigger.PreferredNode.IsNone` |
| `trigger.PreferredNode == "*"` | `trigger.PreferredNode == PreferredNode.Auto` |
| `WithPreferredNode(null)` | `WithPreferredNode(PreferredNode.None)` |
| `WithPreferredNode("*")` | `WithPreferredNode(PreferredNode.Auto)` |
| `WithPreferredNode("node-1")` | `WithPreferredNode(PreferredNode.For("node-1"))` |
| `new TriggerDetailsUpdate().WithPreferredNode(string?)` | `.WithPreferredNode(PreferredNode)` |
| `IMutableTrigger.PreferredNode { get; set; }` → `string?` | → `PreferredNode` |

```diff
- .WithPreferredNode("production-node-1")
+ .WithPreferredNode(PreferredNode.For("production-node-1"))

- string? node = t.PreferredNode;
- bool auto = t.IsPreferredNodeAuto;
+ string? node = t.PreferredNode.Node;
+ bool auto = t.PreferredNode.IsAutomatic;
```

`PreferredNode.For` trims its argument and rejects a blank one and the pinning protocol's own markers (`*`,
`_`, `null`) — names that could never identify a node, and which used to be accepted and then quietly mean
something else. Any other name is legal; the node name is stored verbatim, so `auto:thing` or `*-west` is
fine. Assigning a pin records it as the pin it was, auto-claim included, so copying one between triggers is
lossless.

**Storage is unchanged.** `QRTZ_TRIGGERS.PREFERRED_NODE` and `PREFERRED_NODE_AUTO` still hold the string and
the flag; the mapping happens at the store boundary and the sentinel is an internal constant rather than
something a caller spells. Databases written by 3.19's node-affinity migration or by an earlier 4.0 preview
read back identically, and the JSON trigger payloads never carried the pin.

## Misfire instructions are enums

Each schedule builder had one no-argument method per misfire policy — eighteen of them across five builders,
with a slightly different vocabulary each. A method name is a poor place to keep a value: it cannot be read
from configuration, cannot be switched on, and cannot be defaulted. Every builder now has one
`WithMisfireInstruction` taking its family's enum.

```diff
  .WithSimpleSchedule(x => x
      .WithInterval(TimeSpan.FromMinutes(5))
      .RepeatForever()
-     .WithMisfireHandlingInstructionNextWithExistingCount())
+     .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount))
```

Earlier 4.0 previews spelled that method `WithMisfireHandlingInstruction`. It is `WithMisfireInstruction` now,
on all five builders, matching `TriggerDetailsUpdate.WithMisfireInstruction` and the XML element name.

### SimpleScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireNow()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow)` |
| `WithMisfireHandlingInstructionNowWithExistingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithExistingCount)` |
| `WithMisfireHandlingInstructionNowWithRemainingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NowWithRemainingCount)` |
| `WithMisfireHandlingInstructionNextWithRemainingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount)` |
| `WithMisfireHandlingInstructionNextWithExistingCount()` | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount)` |
| (call nothing) | `WithMisfireInstruction(SimpleTriggerMisfireInstruction.SmartPolicy)`, still the default |

### CronScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(CronTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |

### CalendarIntervalScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(CalendarIntervalTriggerMisfireInstruction.DoNothing)` |

### DailyTimeIntervalScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(DailyTimeIntervalTriggerMisfireInstruction.DoNothing)` |

### RecurrenceScheduleBuilder

| 3.x | 4.x |
|---|---|
| `WithMisfireHandlingInstructionIgnoreMisfires()` | `WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.IgnoreMisfires)` |
| `WithMisfireHandlingInstructionFireAndProceed()` | `WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.FireAndProceed)` |
| `WithMisfireHandlingInstructionDoNothing()` | `WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.DoNothing)` |

### `TriggerDetailsUpdate` takes the same enums

The update object had a single `WithMisfireInstruction(int)`, which let a simple trigger's code be applied to
a cron trigger: the number is in range for both families and means a different policy in each. It now has one
overload per family, plus a code form for callers that genuinely have a number and no family — a value read
off the wire, from configuration, or from a trigger.

| 4.0 preview | 4.0 |
|---|---|
| `.WithMisfireInstruction(2)` | `.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |
| `.WithMisfireInstruction(MisfireInstruction.CronTrigger.DoNothing)` | `.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |
| `.WithMisfireInstruction(someInt)` | `.WithMisfireInstructionCode(someInt)` |

The typed overloads are the taught path: the store rejects an update whose family is not the stored trigger's,
so a cron policy sent to a simple trigger is now an error rather than a silently different policy.
`WithMisfireInstructionCode` keeps only the range check the trigger itself applies.

### The enums are the vocabulary

A trigger's misfire policy is read from its family interface, typed. The family-agnostic number a trigger
stores is still on `ITrigger`, renamed so it no longer competes with the typed property for the good name,
and `IMutableTrigger` still carries the settable one.

| 3.x | 4.x |
|---|---|
| `int ITrigger.MisfireInstruction { get; }` | `int ITrigger.MisfireInstructionCode { get; }` |
| `int IMutableTrigger.MisfireInstruction { get; set; }` | `int IMutableTrigger.MisfireInstructionCode { get; set; }` |
| `int AbstractTrigger.MisfireInstruction { get; set; }` | `int TriggerBase.MisfireInstructionCode { get; set; }` |
| `(CronTriggerMisfireInstruction) trigger.MisfireInstruction` | `((ICronTrigger) trigger).MisfireInstruction` |
| (new) | `SimpleTriggerMisfireInstruction ISimpleTrigger.MisfireInstruction { get; }`, and one per family |

```diff
- var policy = (CronTriggerMisfireInstruction) trigger.MisfireInstruction;
+ CronTriggerMisfireInstruction policy = ((ICronTrigger) trigger).MisfireInstruction;

  // still there, for code generic over every family - serializers, the wire, logging
- int stored = trigger.MisfireInstruction;
+ int stored = trigger.MisfireInstructionCode;
```

An enum member's underlying value *is* the code, so the two convert freely, and the numbers in
`QRTZ_TRIGGERS.MISFIRE_INSTR` and in JSON trigger payloads are unchanged.

**The `MisfireInstruction` constant class is internal.** Its members were a third spelling of a vocabulary
that already had two, and the enums cover all five families where it covered them unevenly. Replace
`MisfireInstruction.CronTrigger.DoNothing` with `CronTriggerMisfireInstruction.DoNothing`,
`MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount` with
`SimpleTriggerMisfireInstruction.NowWithExistingCount`, `MisfireInstruction.IgnoreMisfirePolicy` with the
family's `IgnoreMisfires`, and `MisfireInstruction.SmartPolicy` with the family's `SmartPolicy`.

### The XML and JSON names are resolved per family

Both scheduling-data readers used to resolve a misfire instruction name by reflecting over the constant class
and *all* of its nested types at once, so any family's name resolved for any family's trigger. In JSON, which
has no schema to catch it, a cron trigger configured with `"MisfireInstruction": "RescheduleNowWithExistingRepeatCount"`
became `DoNothing` — both are 2 — and nothing said so. Explicit per-family maps replace the reflection.

Every name that parses today still parses, and each family additionally accepts its own enum member names, so
`FireAndProceed` and `NowWithExistingCount` can be written in configuration as well as in code. A name
belonging to another family is still resolved when the code is legal for this one, but it is logged as a
warning naming the policy the value actually selects; a name whose code is out of the family's range is
rejected with a message listing the names that work, where it used to fail later with
"The misfire instruction code is invalid for this type of trigger".

The XML processor's `ReadMisfireInstructionFromString` was `protected virtual` and is now private — as is
the processor itself: it could not tell the families apart, because it was not told which one it was
reading. XML itself is unaffected
either way — `job_scheduling_data_2_0.xsd` restricts `misfire-instruction` per trigger type, so the schema
rejects a foreign name before the resolver sees it.

## Every builder starts with `Create`

3.x taught a different way to start each builder: `Create()` on some, `CronSchedule(...)` on
`CronScheduleBuilder`, `NewDate()` on `DateBuilder`, `new` on others. In 4.x every builder in the DSL
is reached the same way — a static `Create(...)` taking only what cannot be defaulted.

| 3.x | 4.x |
|---|---|
| `CronScheduleBuilder.CronSchedule(string)` | `CronScheduleBuilder.Create(string)` |
| `CronScheduleBuilder.CronSchedule(CronExpression)` | `CronScheduleBuilder.Create(CronExpression)` |
| `DateBuilder.NewDate()` | `DateBuilder.Create()` |
| `DateBuilder.NewDateInTimeZone(tz)` | `DateBuilder.CreateInTimeZone(tz)` |
| `JobBuilder.Create(Type)` | `JobBuilder.Create().OfType(type)` |

`ExecutionLimitsBuilder`, new in 4.0, follows the same convention: its constructor is no longer
public, so `new ExecutionLimitsBuilder()` becomes `ExecutionLimitsBuilder.Create()`.

The `JobBuilder` row is the only one that is more than a rename. `Create(Type)` duplicated
`OfType(Type)` on the same builder; when the job type only arrives at runtime — read from
configuration, a database row, or a message — build the detail as:

```csharp
IJobDetail job = JobBuilder.Create().OfType(jobType).WithIdentity(name).Build();
```

`JobBuilder.Create()` / `Create<TJob>()` and `TriggerBuilder.Create()` / `Create<TJob>()` are
unchanged — that generic/non-generic split carries real type information (see
[The builders carry the job type](#the-builders-carry-the-job-type)).

## Intervals are said once per builder

Every schedule builder had a `WithIntervalIn<Unit>` method per unit alongside a general `WithInterval`. The
per-unit methods are gone; the general one stays.

### SimpleScheduleBuilder — `WithInterval(TimeSpan)`

| 3.x | 4.x |
|---|---|
| `WithIntervalInSeconds(n)` | `WithInterval(TimeSpan.FromSeconds(n))` |
| `WithIntervalInMinutes(n)` | `WithInterval(TimeSpan.FromMinutes(n))` |
| `WithIntervalInHours(n)` | `WithInterval(TimeSpan.FromHours(n))` |

### CalendarIntervalScheduleBuilder — `WithInterval(int, IntervalUnit)`

| 3.x | 4.x |
|---|---|
| `WithIntervalInSeconds(n)` | `WithInterval(n, IntervalUnit.Second)` |
| `WithIntervalInMinutes(n)` | `WithInterval(n, IntervalUnit.Minute)` |
| `WithIntervalInHours(n)` | `WithInterval(n, IntervalUnit.Hour)` |
| `WithIntervalInDays(n)` | `WithInterval(n, IntervalUnit.Day)` |
| `WithIntervalInWeeks(n)` | `WithInterval(n, IntervalUnit.Week)` |
| `WithIntervalInMonths(n)` | `WithInterval(n, IntervalUnit.Month)` |
| `WithIntervalInYears(n)` | `WithInterval(n, IntervalUnit.Year)` |

### DailyTimeIntervalScheduleBuilder — `WithInterval(int, IntervalUnit)`

| 3.x | 4.x |
|---|---|
| `WithIntervalInSeconds(n)` | `WithInterval(n, IntervalUnit.Second)` |
| `WithIntervalInMinutes(n)` | `WithInterval(n, IntervalUnit.Minute)` |
| `WithIntervalInHours(n)` | `WithInterval(n, IntervalUnit.Hour)` |

A calendar interval and a simple interval are genuinely different things — a `TimeSpan` is a fixed amount of
time, while `1, IntervalUnit.Month` is however long the next month happens to be — so the two shapes differ on
purpose.

## `SimpleScheduleBuilder`'s twelve `Repeat*` factories are gone

Three units × forever-or-a-count × with-or-without an explicit count, twelve static factories in all, each of
which built a `TimeSpan` and set a repeat count.

| 3.x | 4.x |
|---|---|
| `SimpleScheduleBuilder.RepeatSecondlyForever()` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(1)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatSecondlyForever(n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(n)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatMinutelyForever()` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(1)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatMinutelyForever(n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(n)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatHourlyForever()` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(1)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatHourlyForever(n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(n)).RepeatForever()` |
| `SimpleScheduleBuilder.RepeatSecondlyForTotalCount(c)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(1)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatSecondlyForTotalCount(c, n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromSeconds(n)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatMinutelyForTotalCount(c)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(1)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatMinutelyForTotalCount(c, n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromMinutes(n)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatHourlyForTotalCount(c)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(1)).WithRepeatCount(c - 1)` |
| `SimpleScheduleBuilder.RepeatHourlyForTotalCount(c, n)` | `SimpleScheduleBuilder.Create().WithInterval(TimeSpan.FromHours(n)).WithRepeatCount(c - 1)` |

::: warning
Mind the `- 1` in the `ForTotalCount` rows. The repeat count is one fewer than the number of firings, because
the trigger also fires at its start time — `RepeatMinutelyForTotalCount(3)` fires three times, and the
equivalent repeat count is 2. This subtraction is the only thing the old factories said that `WithInterval`
does not, and it is now said on `WithRepeatCount`, where the trigger says it.
:::

Inside a `WithSimpleSchedule` delegate you never needed the factories at all:

```diff
- .WithSchedule(SimpleScheduleBuilder.RepeatMinutelyForever(5))
+ .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())
```

And an interval overload shortens even that, which is as close as 4.x comes to bringing the factories back —
see [Five shorthands for the common case](#five-shorthands-for-the-common-case):

```csharp
.WithSimpleSchedule(TimeSpan.FromMinutes(5))                   // the same schedule
.WithSimpleSchedule(TimeSpan.FromMinutes(5), repeatCount: 2)   // three firings rather than forever
```

## `CronScheduleBuilder`'s convenience factories are gone

`CronSchedule(string)` and `CronSchedule(CronExpression)` stay, spelled `Create(...)` now (see
[Every builder starts with `Create`](#every-builder-starts-with-create)). The six factories that assembled an expression
from numbers are replaced by `CronExpressionBuilder`, which names each field instead of relying on argument
order — the old set used three different orders for the same three numbers.

`CronExpressionBuilder` spells "restrict this field to these values" as `With*` on every field, day-of-week
included (`WithDaysOfWeek`, `WithDayOfWeekRange`, `WithDayOfWeekIncrements`); the `On*` prefix is only for the
positional and special forms (`OnWeekdays`, `OnLastDayOfMonth`, `OnLastDayOfWeek`, `OnLastDayOfWeekOfMonth`,
`OnNthDayOfWeekOfMonth`, `OnNearestWeekdayOfMonth`). A trigger takes the builder — or a built
`CronExpression` — directly through `WithCronSchedule`, so the chain closes without naming
`CronScheduleBuilder`.

The four that took a time of day take it as one `TimeOnly` now, through `AtTime` — which sets the second,
minute and hour fields together, so the "at 09:30" half of each old factory is one call and the "on these
days" half is `WithDaysOfWeek` or `WithDayOfMonth` beside it. Cron resolves to a whole second, so any
sub-second part of the `TimeOnly` is ignored.

| 3.x | 4.x |
|---|---|
| `CronScheduleBuilder.DailyAtHourAndMinute(h, m)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m))` |
| `CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(h, m, days)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m)).WithDaysOfWeek(days)` |
| `CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(day, h, m)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m)).WithDaysOfWeek(day)` |
| `CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(dom, h, m)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m)).WithDayOfMonth(dom)` |
| `CronScheduleBuilder.CronScheduleWithHash(expr, hashKey)` | `CronScheduleBuilder.Create(CronExpression.ParseWithHash(expr, hashKey))` |
| `CronScheduleBuilder.CronScheduleWithHash(expr, hashSeed)` | `CronScheduleBuilder.Create(CronExpression.ParseWithHash(expr, hashSeed))` |

`WithCronSchedule` takes the builder, so nothing in the first four rows names `CronScheduleBuilder` at all:

```diff
- .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(9, 30))
+ .WithCronSchedule(CronExpressionBuilder.Create().AtTime(new TimeOnly(9, 30)))
```

A literal expression is still the shortest thing to write when you have one:

```diff
- .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(9, 30))
+ .WithCronSchedule("0 30 9 ? * *")
```

## Five shorthands for the common case

Replacing a family of static factories with a builder makes the unusual call sayable and the usual one
longer. Five additions give the usual one its short spelling back: an interval overload of
`WithSimpleSchedule`, `CronExpressionBuilder.AtTime`, `IScheduler.ScheduleJob<TJob>`, a named static on each
of the four option records, and `TriggerBuilder<TJob>.Key`. All are additive — nothing they shorten stopped
working, and the longer form is still what to reach for the moment the call needs more than the shorthand
says.

| Instead of | Write |
|---|---|
| `WithSimpleSchedule(x => x.WithInterval(i).RepeatForever())` | `WithSimpleSchedule(i)` |
| `WithSimpleSchedule(x => x.WithInterval(i).WithRepeatCount(n))` | `WithSimpleSchedule(i, n)` |
| `CronExpressionBuilder.Create().WithSecond(0).WithMinute(m).WithHour(h)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m))` |
| `scheduler.ScheduleJob(JobBuilder.Create<TJob>().WithIdentity(trigger.Key.Name, trigger.Key.Group).Build(), trigger)` | `scheduler.ScheduleJob<TJob>(trigger)` |
| `new AddJobOptions { Replace = true }` | `AddJobOptions.Replacing` |
| `new ScheduleJobOptions { Replace = true }` | `ScheduleJobOptions.Replacing` |
| `new AddTriggerOptions { Replace = true }` | `AddTriggerOptions.Replacing` |
| `new AddCalendarOptions { Replace = true }` | `AddCalendarOptions.Replacing` |
| `new AddCalendarOptions { Replace = true, UpdateTriggers = true }` | `AddCalendarOptions.ReplacingAndUpdatingTriggers` |

The repeat count in `WithSimpleSchedule(i, n)` is the trigger's own `RepeatCount`, so it fires `n + 1` times —
the same number `WithRepeatCount` takes, with none of the `- 1` arithmetic the 3.x `ForTotalCount` factories
did. Omitting it repeats forever.

`IScheduler.ScheduleJob<TJob>(trigger, configure)` is the imperative twin of the container's
`q.ScheduleJob<TJob>(...)`, and names the job the same way: whatever `configure` gave it, else the job the
trigger already points at through `ForJob`, else the trigger's own key. So neither of these names a
`JobBuilder`, and the two read alike:

```csharp
// at start-up, in AddQuartz
q.ScheduleJob<ReportJob>(trigger => trigger.WithIdentity("nightly").WithCronSchedule("0 30 9 ? * *"));

// at run time, against a scheduler
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly")
    .WithCronSchedule("0 30 9 ? * *")
    .Build();

await scheduler.ScheduleJob<ReportJob>(trigger);
```

`TriggerBuilder<TJob>.Key` rounds out the same pass: it reports the identity the trigger was given, the way
`JobBuilder<TJob>.Key` already did, so code that has to agree with a trigger can read its key rather than
build the trigger to find it out. It differs in one way — a trigger builder keeps the key `Build()` generated
when none was named, so building twice produces the same trigger, and reading `Key` after `Build()` reports
that generated key rather than `null`.

## Day selection on `DailyTimeIntervalScheduleBuilder`

The two `OnDaysOfTheWeek` overloads are one C# 13 params collection, so both old call shapes still compile:

```csharp
x.OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Wednesday);   // still fine
x.OnDaysOfTheWeek(daysFromConfiguration);                   // still fine
```

The three `public static readonly` day sets are gone. They existed only to be handed straight back to
`OnDaysOfTheWeek`, which the named methods already do:

| 3.x | 4.x |
|---|---|
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek)` | `OnEveryDay()` — also the default when you say nothing |
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.MondayThroughFriday)` | `OnMondayThroughFriday()` |
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.SaturdayAndSunday)` | `OnSaturdayAndSunday()` |

If you used a set for something other than the builder, `Enum.GetValues<DayOfWeek>()` is the whole week.

## `EndingDailyAfterCount` is computed at `Build()`

In 3.x, `EndingDailyAfterCount(count)` resolved the daily window immediately, against the wall clock
and against whatever start time, interval and time zone happened to be configured *before* the call —
which is why its documentation had to insist on call order. The computation now runs when the trigger
is built:

* It sees the schedule as finally configured, so `EndingDailyAfterCount` no longer has to be the last
  method in the chain.
* It runs against the clock of the `TriggerBuilder` that builds the trigger, so a scheduler configured
  with a custom `TimeProvider` (a `FakeTimeProvider` in tests) is honored. Previously the builder read
  the wall clock and a configured clock was silently ignored.
* Validation moves with it: "count too large" and "start time not set" are now reported by `Build()`,
  not by the `EndingDailyAfterCount` call. A count that is not positive is still rejected immediately.

`DailyTimeIntervalScheduleBuilder.Create()` takes no `TimeProvider` — none of the schedule builders
do; the clock belongs to `TriggerBuilder.Create(TimeProvider?)`, and it reaches the trigger itself as
well as the schedule — see [A trigger holds the clock that built it](#a-trigger-holds-the-clock-that-built-it).

## A trigger holds the clock that built it

3.x had one clock. `SystemTime.UtcNow` was a process-wide hook, so a trigger, the store that swept it
up and the scheduler that fired it could not disagree about what time it was. 4.x replaced it with
`TimeProvider`, which is an object rather than a hook — which means each trigger *has* a clock, and it
matters where it got one.

It gets one from whoever produced it:

* `TriggerBuilder.Create(clock)...Build()` gives the built trigger that clock. Previously it used the
  clock only for the default start time and for the schedule builder's own arithmetic, and the trigger
  itself was left on `TimeProvider.System` — so the past-due clamp in `ComputeFirstFireTimeUtc` and the
  whole of `UpdateAfterMisfire` read the machine's wall clock. `TriggerBuilder.Create()` with no
  argument still means `TimeProvider.System`.
* `trigger.GetTriggerBuilder()` carries the trigger's clock into the rebuilt trigger.
* A job store hands its scheduler's clock to every trigger it materializes. `RAMJobStore` hands back
  the object it was given, clock and all; the ADO.NET store builds triggers from rows and stamps each
  one — the blob path included, since the clock does not serialize.

In production every one of these is `TimeProvider.System` and nothing looks different. It shows up in a
scheduler configured with a `FakeTimeProvider`: a misfire that the store *selected* on the test's clock
used to be *recovered* onto the machine's, which is the ADO.NET half of what
[#3456](https://github.com/quartznet/quartznet/issues/3456) reported.

The clock is not part of a trigger's public surface. It is a construction-or-store decision, because
the decision that a trigger has misfired and the arithmetic that recovers it have to be made against
one reading of "now".

## `InTimeZone` is nullable everywhere

`CronScheduleBuilder` and `DailyTimeIntervalScheduleBuilder` declared `InTimeZone(TimeZoneInfo)` while
`CalendarIntervalScheduleBuilder` and `RecurrenceScheduleBuilder` declared `InTimeZone(TimeZoneInfo?)`, so
passing a zone that may be absent needed a `!` on two of the four. All four — and `DateBuilder.InTimeZone` —
now take `TimeZoneInfo?`. `null` means what it always meant: the system's local time zone.

```diff
- .InTimeZone(configuredZone!)
+ .InTimeZone(configuredZone)
```

## `ScheduleBuilder<T>` is gone

The five schedule builders implement `IScheduleBuilder` directly. The abstract base declared one member that
`IScheduleBuilder` already declares, and nothing ever used its type parameter. A schedule builder of your own
implements the interface and drops the `override`:

```diff
- private sealed class MyScheduleBuilder : ScheduleBuilder<MyTrigger>
+ private sealed class MyScheduleBuilder : IScheduleBuilder
  {
-     public override IMutableTrigger Build() => new MyTrigger();
+     public IMutableTrigger Build() => new MyTrigger();
  }
```

## `DateBuilder`'s static factories are gone

The fluent API keeps its shape: `DateBuilder.Create()` (named `NewDate()` in 3.x), `CreateInTimeZone()`
(3.x `NewDateInTimeZone()`), the `At*`/`On*`/`In*` setters and `Build()`. The seventeen statics were
doing two unrelated jobs under one name — naming a specific date, which
the fluent API does, and arithmetic on a `DateTimeOffset`, which `DateTimeOffset` does.

### Naming a date

| 3.x | 4.x |
|---|---|
| `DateBuilder.DateOf(h, m, s)` | `DateBuilder.Create().AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.TodayAt(h, m, s)` | `DateBuilder.Create().AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.DateOf(h, m, s, day, month)` | `DateBuilder.Create().InMonthOnDay(month, day).AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.DateOf(h, m, s, day, month, year)` | `DateBuilder.Create().InYear(year).InMonthOnDay(month, day).AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.TomorrowAt(h, m, s)` | `DateBuilder.Create().AtHourMinuteAndSecond(h, m, s).Build().AddDays(1)` |

Note that `InMonthOnDay` takes the month first, where `DateOf` took the day first.

### Now plus something

| 3.x | 4.x |
|---|---|
| `DateBuilder.FutureDate(n, IntervalUnit.Second)` | `DateTimeOffset.UtcNow.AddSeconds(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Minute)` | `DateTimeOffset.UtcNow.AddMinutes(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Hour)` | `DateTimeOffset.UtcNow.AddHours(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Day)` | `DateTimeOffset.UtcNow.AddDays(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Week)` | `DateTimeOffset.UtcNow.AddDays(n * 7)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Month)` | `DateTimeOffset.UtcNow.AddMonths(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Year)` | `DateTimeOffset.UtcNow.AddYears(n)` |
| `DateBuilder.FutureDate(n, IntervalUnit.Millisecond)` | `DateTimeOffset.UtcNow.AddMilliseconds(n)` |

### Rounding

Each of these was one line of `DateTimeOffset` construction:

| 3.x | 4.x |
|---|---|
| `DateBuilder.EvenSecondDateBefore(d)` | `new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Offset)` |
| `DateBuilder.EvenSecondDate(d)` | the same, on `d.AddSeconds(1)` |
| `DateBuilder.EvenSecondDateAfterNow()` | the same, on `DateTimeOffset.Now.AddSeconds(1)` |
| `DateBuilder.EvenMinuteDateBefore(d)` | `new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, 0, d.Offset)` |
| `DateBuilder.EvenMinuteDate(d)` | the same, on `d.AddMinutes(1)` |
| `DateBuilder.EvenMinuteDateAfterNow()` | the same, on `DateTimeOffset.Now.AddMinutes(1)` |
| `DateBuilder.EvenHourDateBefore(d)` | `new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, 0, 0, d.Offset)` |
| `DateBuilder.EvenHourDate(d)` | the same, on `d.AddHours(1)` |
| `DateBuilder.EvenHourDateAfterNow()` | the same, on `DateTimeOffset.Now.AddHours(1)` |
| `DateBuilder.NextGivenMinuteDate(d, minuteBase)` | round `d` up to the next multiple of `minuteBase` minutes |
| `DateBuilder.NextGivenSecondDate(d, secondBase)` | round `d` up to the next multiple of `secondBase` seconds |

Most start times do not need the rounding at all. A trigger that starts at an arbitrary instant and repeats
every minute keeps the same schedule as one whose start was rounded up first — it just begins a fraction of a
second earlier. Reach for rounding when you want the *displayed* times to be tidy, and write the line where
you want it.

## `TimeOfDay` became `TimeOnly`

The hand-written `TimeOfDay` class predates `System.TimeOnly`. It is gone, and `IDailyTimeIntervalTrigger`
and `DailyTimeIntervalScheduleBuilder` speak `TimeOnly`.

| 3.x | 4.x |
|---|---|
| `TimeOfDay.HourAndMinuteOfDay(8, 0)` | `new TimeOnly(8, 0)` |
| `TimeOfDay.HourMinuteAndSecondOfDay(8, 0, 30)` | `new TimeOnly(8, 0, 30)` |
| `new TimeOfDay(8, 0)` / `new TimeOfDay(8, 0, 30)` | `new TimeOnly(8, 0)` / `new TimeOnly(8, 0, 30)` |
| `IDailyTimeIntervalTrigger.StartTimeOfDay` returning `TimeOfDay` | returning `TimeOnly` |
| `IDailyTimeIntervalTrigger.EndTimeOfDay` returning `TimeOfDay` | returning `TimeOnly` |
| `StartingDailyAt(TimeOfDay)` / `EndingDailyAt(TimeOfDay)` | `StartingDailyAt(TimeOnly)` / `EndingDailyAt(TimeOnly)` |
| `a.Before(b)` | `a < b` |
| `timeOfDay.GetTimeOfDayForDate(date)` | `new DateTimeOffset(date.Date, date.Offset).Add(timeOfDay.ToTimeSpan())` |

```csharp
// 3.x
.WithDailyTimeIntervalSchedule(x => x
    .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(8, 0))
    .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(17, 0)))

// 4.x
.WithDailyTimeIntervalSchedule(x => x
    .StartingDailyAt(new TimeOnly(8, 0))
    .EndingDailyAt(new TimeOnly(17, 0)))
```

Two things to know:

* The two properties are non-nullable now. A `TimeOnly` is a struct, and the defaults are the ones the
  builder always applied anyway — `00:00:00` and `23:59:59`.
* A value with sub-second precision is rejected with an `ArgumentException`. The job store keeps the window
  in hour, minute and second columns, so `TimeOnly.FromDateTime(DateTime.Now)` would lose its fractional part
  the moment the trigger were persisted. Round it yourself if that is what you meant.

Nothing about storage changed: the `SIMPROP_INT_PROP` columns are the same, and the JSON
`StartTimeOfDay`/`EndTimeOfDay` objects keep their `{ Hour, Minute, Second }` shape, so existing triggers
load unchanged.

## `DailyCalendar` takes two `TimeOnly` values

Eight constructors and four `SetTimeRange` overloads described one pair of times four different ways. One
constructor and one property replace them.

| 3.x | 4.x |
|---|---|
| `new DailyCalendar("08:00", "17:00")` | `new DailyCalendar(new TimeOnly(8, 0), new TimeOnly(17, 0))` |
| `new DailyCalendar("08:00:00:500", "17:00:00:000")` | `new DailyCalendar(new TimeOnly(8, 0, 0, 500), new TimeOnly(17, 0))` |
| `new DailyCalendar(baseCal, "08:00", "17:00")` | `new DailyCalendar(new TimeOnly(8, 0), new TimeOnly(17, 0), baseCal)` |
| `new DailyCalendar(8, 0, 0, 0, 17, 0, 0, 0)` | `new DailyCalendar(new TimeOnly(8, 0), new TimeOnly(17, 0))` |
| `new DailyCalendar(startDateTime, endDateTime)` | `new DailyCalendar(TimeOnly.FromDateTime(startDateTime), TimeOnly.FromDateTime(endDateTime))` |
| `new DailyCalendar(startTicks, endTicks)` | `new DailyCalendar(new TimeOnly(startTicks), new TimeOnly(endTicks))` |
| `calendar.SetTimeRange(...)` (four overloads) | `calendar.TimeRange = (start, end)` |
| `calendar.RangeStartingTime` (a string) | `calendar.TimeRange.Start` |
| `calendar.RangeEndingTime` (a string) | `calendar.TimeRange.End` |

`TimeRange` is a `readonly record struct` of `Start` and `End` in the `Quartz` namespace. A `(start, end)`
tuple converts to it implicitly, so the assignment in the table above reads as it always did; a variable
holding the range is a `TimeRange` rather than a `ValueTuple`, and an equality comparison against a bare
tuple needs the type spelled out — `calendar.TimeRange == new TimeRange(start, end)`. What the calendar
stores did not change: its serialized form is the same eight numbers it has always been.

The `"HH:MM:SS:mmm"` string form — note the colon before the milliseconds — was a format nothing else in
.NET parses. `InvertTimeRange`, `GetTimeRangeStartingTimeUtc` and `GetTimeRangeEndingTimeUtc` are unchanged.
The constructor no longer takes a `TimeProvider`; it only ever used one to check that the range starts
before it ends, which two `TimeOnly` values answer directly. Precision finer than a millisecond is rejected,
matching what the calendar's serialized form can carry.

Persisted `DailyCalendar` blobs load unchanged: the serializers write `RangeStart`/`RangeEnd` now but still
read the old `RangeStartingTime`/`RangeEndingTime` strings.

## Excluded days are a read-only set

The four day-excluding calendars had four idioms for one idea. They now share one: a read-only set of the
thing being excluded, plus `AddExcludedDay` and `RemoveExcludedDay`, which return whether the set changed.

| 3.x | 4.x |
|---|---|
| `AnnualCalendar.DaysExcluded` as a settable `IReadOnlyCollection<DateTime>` | `IReadOnlySet<MonthDay>`, get-only |
| `annual.SetDayExcluded(day, true)` | `annual.AddExcludedDay(MonthDay)` |
| `annual.SetDayExcluded(day, false)` | `annual.RemoveExcludedDay(MonthDay)` |
| `annual.IsDayExcluded(DateTimeOffset)` | `annual.IsDayExcluded(MonthDay)` |
| `HolidayCalendar.ExcludedDates` as a `List<DateTime>` copy | `HolidayCalendar.DaysExcluded` as `IReadOnlySet<DateOnly>` |
| `holiday.AddExcludedDate(DateTime)` | `holiday.AddExcludedDay(DateOnly)` |
| `holiday.RemoveExcludedDate(DateTime)` | `holiday.RemoveExcludedDay(DateOnly)` |
| (nothing) | `holiday.IsDayExcluded(DateOnly)` |
| `MonthlyCalendar.DaysExcluded` as a settable `bool[31]` | `IReadOnlySet<int>`, get-only, days 1 through 31 |
| `monthly.SetDayExcluded(15, true)` / `(15, false)` | `monthly.AddExcludedDay(15)` / `monthly.RemoveExcludedDay(15)` |
| `WeeklyCalendar.DaysExcluded` as a settable `bool[7]` | `IReadOnlySet<DayOfWeek>`, get-only |
| `weekly.SetDayExcluded(DayOfWeek.Friday, true)` / `(…, false)` | `weekly.AddExcludedDay(DayOfWeek.Friday)` / `weekly.RemoveExcludedDay(DayOfWeek.Friday)` |
| `CronCalendar.SetCronExpressionString(expr)` | `cron.CronExpression = new CronExpression(expr)` |

```csharp
// 3.x
var holidays = new HolidayCalendar();
holidays.AddExcludedDate(new DateTime(2025, 12, 25));

var christmas = new AnnualCalendar();
christmas.SetDayExcluded(new DateTime(2025, 12, 25), true);

var weekends = new WeeklyCalendar();
weekends.SetDayExcluded(DayOfWeek.Friday, true);

// 4.x
var holidays = new HolidayCalendar();
holidays.AddExcludedDay(new DateOnly(2025, 12, 25));   // a specific date, once

var christmas = new AnnualCalendar();
christmas.AddExcludedDay(new MonthDay(12, 25));        // the same date, every year

var weekends = new WeeklyCalendar();
weekends.AddExcludedDay(DayOfWeek.Friday);
```

Two behaviors worth knowing:

* `AnnualCalendar` speaks `MonthDay` — a `readonly record struct` of month and day in the `Quartz`
  namespace, with `MonthDay.From(DateOnly)` for when you hold a date. A `DateOnly` always carries a year,
  so a set of them had to lie: what you put in was not what you read back, and `DaysExcluded.Contains`
  disagreed with `AddExcludedDay`. `MonthDay` says exactly what is stored — the same date every year —
  and February 29th is a valid value. Its text form is the ISO 8601 spelling of a recurring month-day,
  `--MM-DD`, and it implements the full BCL set around that one form: `IParsable<MonthDay>`,
  `ISpanParsable<MonthDay>`, `ISpanFormattable` and `IUtf8SpanFormattable`, so it reads out of
  configuration, a route value or a JSON string like any other primitive. There is nothing to vary, so
  the format string and the format provider those interfaces pass are ignored. What
  `AnnualCalendar` payloads carry is unchanged — a serialized day is still the date-shaped form pinned
  to the year 2000.
* `AnnualCalendar.IsDayExcluded` now answers only about the calendar's own set. The base calendar is
  consulted by `IsTimeIncluded`, which is the member that asks a question about an instant.

`MonthlyCalendar.AreAllDaysExcluded` and `WeeklyCalendar.AreAllDaysExcluded` are unchanged, and a fresh
`WeeklyCalendar` still starts out excluding Saturday and Sunday.

Existing calendar blobs load unchanged. Both serializers write the new shapes and read the old ones: an
`ExcludedDays`/`ExcludedDates` array may hold timestamps or dates, and per-day booleans or day numbers or
day names.

## `[Serializable]` survives only where a database blob needs it

`BinaryFormatter` is obsolete on .NET 8 (SYSLIB0051) and throws on .NET 9 and later, and Quartz 4 ships no
binary serializer. The attributes that only `BinaryFormatter` ever read were still on 49 types — every
exception, every matcher, the job execution context, the job detail. Nineteen of them keep the attributes;
the other 30 lost them.

The line is drawn at the database. A type keeps `[Serializable]`, `ISerializable` and `GetObjectData` when a
job store blob can be made of it, so that a 3.x database whose blobs were written by `BinaryFormatter` stays
readable while you migrate it to JSON — see
[Migrating from binary serialization](packages/json-serialization.md#migrating-from-binary-serialization).

| Blob column | Types that keep the attributes |
|---|---|
| `JOB_DETAILS.JOB_DATA`, `TRIGGERS.JOB_DATA` | `JobDataMap`, `Key<T>`, `JobKey`, `TriggerKey` |
| `CALENDARS.CALENDAR` | `BaseCalendar`, `AnnualCalendar`, `CronCalendar`, `DailyCalendar`, `HolidayCalendar`, `MonthlyCalendar`, `WeeklyCalendar`, `CronExpression` |
| `BLOB_TRIGGERS.BLOB_DATA` | `TriggerBase`, `SimpleTriggerImpl`, `CronTriggerImpl`, `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` |

A trigger reaches that third row when no trigger persistence delegate handles it — a type of your own, or one
deriving from any of the five shipped implementations with `HasAdditionalProperties` returning `true`. The
store writes the whole object into `BLOB_TRIGGERS`, so the trigger class hierarchy is part of the blob graph.

`RecurrenceTriggerImpl` is the one trigger implementation *not* on that list: it does not carry
`[Serializable]`, where `TriggerBase` and the other four do. In practice a recurrence trigger is written to
`SIMPROP_TRIGGERS` by `RecurrenceTriggerPersistenceDelegate` and never takes the blob path. It did carry the
attribute on 3.x, though, so a
3.x database that somehow holds a binary recurrence-trigger blob (which takes a delegate list with that
delegate removed) should have that row migrated to JSON on 3.x before the upgrade.

3.x listed `StringKeyDirtyFlagMap` and `DirtyFlagMap<TKey, TValue>` in the first row; both are
internal now. That costs nothing for existing blobs: `BinaryFormatter` records an `ISerializable`
graph under the runtime type name — `Quartz.JobDataMap` — plus its `version`/`dirty`/`map` entries,
never the base chain, and `JobDataMap` carries that exact constructor and entry set itself.

Everything else lost `[Serializable]`:

| Where | Types |
|---|---|
| Exceptions | `SchedulerException`, `JobExecutionException`, `JobPersistenceException`, `ObjectAlreadyExistsException`, `SchedulerConfigException`, `JsonSerializationException`, `LockException`, `NoSuchDelegateException`, `SchedulingDataValidationException` |
| Matchers | `AndMatcher<TKey>`, `GroupMatcher<TKey>`, `KeyMatcher<TKey>`, `NameMatcher<TKey>`, `NotMatcher<TKey>`, `OrMatcher<TKey>`, `StringMatcher<TKey>`, `StringOperator` |
| Everything else | `JobType`, `SchedulerContext`, `JobExecutionContextImpl` |

The `protected` / `public` `(SerializationInfo, StreamingContext)` constructors went with them, on
`SchedulerException`, `JobPersistenceException`, `SchedulerConfigException`
and `HttpClientException`. If you derive from one of those and forward a `SerializationInfo` to the base,
delete your constructor — the base class library's `Exception(SerializationInfo, StreamingContext)` is
obsolete too, and nothing calls yours.

`Key<T>` and its two subclasses are on the keep side because a key can be a *value* inside a job data map. Quartz
never puts one there itself — the recovery entries it writes are strings, and both `TriggerBase` and
`JobDetailImpl` deliberately mark their key fields `[NonSerialized]` and serialize the name and group as separate
strings — but a job data map holds arbitrary `object` values and serializes them all, so an application that did
`jobDataMap.Put("parent", jobKey)` on 3.x has a `JobKey` sitting in its `JOB_DATA`. `BinaryFormatter` refuses to
deserialize an instance whose type is not marked serializable, so removing the attribute would make that blob
unreadable even through the compatibility package. Blob-reachable means what the graph *can* contain, not only
what Quartz itself puts there.

## `JobExecutionException` has four constructors and init-only flags

The exception had seven constructors, three of which existed only to smuggle the `refireImmediately`
flag in positionally — `new JobExecutionException(ex, true)` said nothing at the call site about what
the `true` meant. The four that remain mirror every other exception's shape — `()`, `(message)`,
`(cause)`, `(message, cause)` — and the three scheduler directives (`RefireImmediately`,
`UnscheduleFiringTrigger`, `UnscheduleAllTriggers`) are init-only properties:

```diff
- throw new JobExecutionException(msg, ex, true);
+ throw new JobExecutionException(msg, ex) { RefireImmediately = true };
```

An exception's instructions are fixed at the throw site, which is what init-only says; nothing could
meaningfully flip them on a caught instance in flight. `JobDetail` — which the scheduler fills in on the
way to the listeners — is read-only outside Quartz for the same reason.

### It is sealed, and `Exception.Data` is what a subclass was for

`JobExecutionException` is `sealed` in 4.0. What it says is three instructions to the scheduler, and the
scheduler reads exactly those three: a subclass adding a fourth would be a directive nothing acts on.
Frameworks that subclassed it were not adding a directive — they were carrying their own state past the
scheduler to their own listener, and `Exception.Data` does that:

```diff
- public sealed class ImportJobException : JobExecutionException
- {
-     public string TenantId { get; init; }
- }
- throw new ImportJobException(ex) { TenantId = tenantId, RefireImmediately = true };
+ throw new JobExecutionException(ex) { RefireImmediately = true, Data = { ["tenant"] = tenantId } };
```

An instance a job throws is handed to `IJobListener.JobWasExecuted` **unchanged** — the run shell only
fills in `JobDetail` — so the listener reads `jobException.Data["tenant"]` straight off it.

Wrapping an exception of your own as the cause carries typed state just as well, and is the closer
replacement for a subclass:

```csharp
throw new JobExecutionException(new ImportFailed(tenantId)) { RefireImmediately = true };

// in the listener
if (jobException?.InnerException is ImportFailed failure) { … }
```

One shape to know when you write that listener: an exception that is *not* a `JobExecutionException`
arrives wrapped twice — `JobExecutionException` → `JobExecutionProcessException` → your exception — so a
listener that wants to handle both routes walks `InnerException` until it finds what it knows.

## The two exceptions moved out of `Quartz.Core`

`Quartz.Core` held exactly two public types, and both were exceptions: `JobExecutionProcessException` and
`JobInstantiationException`. Every one of their siblings — `SchedulerException`, which they both derive from,
`JobExecutionException`, `SchedulerConfigException` — lives in `Quartz`. The two
moved there too, and `Quartz.Core` is now internal from top to bottom: nothing in it is a type you can name.

```diff
- using Quartz.Core;
-
- public ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken ct = default)
+ public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext error, CancellationToken ct = default)
  {
-     if (cause is JobInstantiationException failure) { … }
+     if (error.Exception is JobInstantiationException failure) { … }
      return default;
  }
```

Catching or type-testing either exception needs no change beyond deleting the `using Quartz.Core;`, which the
compiler flags as unused. `JobInstantiationException` is new in 4.0 and has never shipped, so nobody is catching it
under the old namespace yet.

## Execution limits are built once, then frozen

`ExecutionLimits` used to be two things at the same time. It was a mutable fluent builder — `ForGroup`,
`ForDefaultGroup`, `ForOtherGroups` and `Unlimited` all mutated `this` and returned `this` — and it was an
`IReadOnlyDictionary<string, int?>`, a collection whose name had to suppress CA1710 to survive analysis. The
scheduler defended itself against the mutable half by snapshotting whatever it was handed, which is a hint that the
type was doing one job too many. It is now two types: `ExecutionLimitsBuilder` mutates, `ExecutionLimits` is the
immutable snapshot that `Build()` returns and that the scheduler thread reads.

```diff
- await scheduler.SetExecutionLimits(new ExecutionLimits()
+ await scheduler.SetExecutionLimits(ExecutionLimitsBuilder.Create()
      .ForGroup("batch-jobs", 2)
      .ForDefaultGroup(10)
-     .ForOtherGroups(5));
+     .ForOtherGroups(5)
+     .Build());
```

`IQuartzBuilder.UseExecutionLimits` takes an `Action<ExecutionLimitsBuilder>`, so a configuration lambda is
unchanged and still reads the same way:

```csharp
q.UseExecutionLimits(limits => limits.ForGroup("high-cpu", 3));
```

Reading limits back is what changed shape, because the snapshot is not a dictionary:

| Before | 4.0 |
|---|---|
| `limits["heavy"]` | `limits.TryGetLimit(ExecutionGroupScope.Named("heavy"), out int? maxConcurrent)` |
| `limits[ExecutionLimits.DefaultGroupKey]` | `limits.TryGetLimit(ExecutionGroupScope.Default, out int? maxConcurrent)` |
| `limits["*"]` | `limits.TryGetLimit(ExecutionGroupScope.OtherGroups, out int? maxConcurrent)` |
| `limits.ContainsKey("heavy")` | `limits.TryGetLimit(ExecutionGroupScope.Named("heavy"), out _)` |
| `limits.Count` | `limits.Groups.Count`, or `limits.IsEmpty` for the question actually being asked |
| `foreach (KeyValuePair<string, int?> pair in limits)` | `foreach (ExecutionGroupLimit limit in limits.Groups)` |

`TryGetLimit` returning `false` is not the same as unlimited: it means the scope has no entry of its own, and a
named group without one still falls back to `ExecutionGroupScope.OtherGroups`. That distinction was invisible when
the type was a dictionary, and it is the reason the lookup is a `TryGet` rather than an indexer.

The read side is typed instead of spelled. `ExecutionGroupScope` is a readonly record struct with exactly the
three cases the builder can write — `Default` (triggers with no execution group), `OtherGroups` (the catch-all)
and `Named(name)` — mirroring how `PreferredNode` models none/auto/named rather than using a nullable string
with a sentinel. `ExecutionGroupLimit.Group` is that value, so enumerating `Groups` no longer requires knowing
that `null` meant the default bucket and `"*"` meant the catch-all:

```csharp
foreach (ExecutionGroupLimit limit in limits.Groups)
{
    string label = limit.Group.IsDefault ? "(default)"
        : limit.Group.IsOtherGroups ? "(other groups)"
        : limit.Group.Name!;
}
```

`ExecutionGroupLimit.Scope` is a different thing — `ExecutionLimitScope.Node` or `.Cluster`, added by
[cluster-wide ceilings](#an-execution-limit-can-be-cluster-wide). The two words sit side by side on
purpose: `Group` says *which* bucket the limit is for, `Scope` says *what it is counted against*.

The configuration spellings are unchanged: `quartz.executionLimit.*` keys and the HTTP API still say `*` for the
catch-all and `_` or `null` for the default bucket (neither a property key nor a JSON object key can be empty).
All three remain reserved: a trigger cannot have `"*"`, `"_"` or `"null"` as its execution group, and
`ExecutionGroupScope.Named` rejects them the same way `ExecutionLimitsBuilder.ForGroup` does.

### A job store is handed the limits, and a way to spend them

`TriggerAcquisitionRequest.ExecutionLimits` and `TriggerAcquisitionCriteria.ExecutionLimits` carry
`ExecutionLimits` rather than `IReadOnlyDictionary<string, int?>`. What they carry is still the *available* slots —
the configured limits less what is already running on this node — not the configuration.

A store acquiring triggers has to count those slots down as it takes them, and the rule for doing so is not
obvious: a group's own entry wins, a named group without one falls back to `OtherGroups`, a trigger with no
execution group never does, and an unlisted group that borrows from `OtherGroups` gets its own allowance rather
than sharing one. That rule used to live inside Quartz where only the two built-in stores could reach it, so a
third-party store either reimplemented it or ignored execution groups. It is now `ExecutionSlots`:

```csharp
public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
    TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
{
    ExecutionSlots? slots = request.ExecutionLimits?.CreateSlots();

    foreach (IOperableTrigger candidate in Candidates(request))
    {
        if (slots is not null && !slots.TryTake(candidate.ExecutionGroup, candidate.Key.Group))
        {
            continue; // this group is forbidden here, or has run out for this pass
        }
        …
    }
}
```

Create the ledger per acquisition attempt, not per store: it is mutable and not thread-safe by design, and a
retried acquisition has to start from the limits again rather than from what the failed attempt had counted down.
`CreateSlots()` leaves the snapshot untouched, so the same `ExecutionLimits` can produce as many as you need.

A clustered store of your own has one more thing to do — see
[cluster-wide ceilings](#an-execution-limit-can-be-cluster-wide).

## Interruption has two names, not three

Stopping a running job had three names in the public API. `IScheduler.Interrupt(JobKey)` and
`IScheduler.InterruptFireInstance(fireInstanceId)` requested it, `IJobExecutionContext.CancellationToken` — the same
token a job receives as the `cancellationToken` parameter of `IJob.Execute` — observed it, and
`ICancellableJobExecutionContext.Cancel()` sat between them looking like a third, supported way to do it. It was
not: calling it bypassed the scheduler, so no `ISchedulerListener.JobInterrupted` was raised, and it worked only for
a context you happened to be holding in the same process.

`ICancellableJobExecutionContext` is gone from the public API, and `JobExecutionContextImpl.Cancel()` with it. The
plumbing still exists inside Quartz under the name the public API already uses for the concept. Ask the scheduler
instead:

```diff
- ((ICancellableJobExecutionContext) context).Cancel();
+ await scheduler.InterruptFireInstance(context.FireInstanceId);
```

The fire instance id the call needs comes from `IScheduler.QueryFireInstances`, which replaced
`GetCurrentlyExecutingJobs` — see
[what is running is a listing](#what-is-running-is-a-listing-not-a-list-of-contexts). Its element type
never was the cancellable interface either, only the documentation said so.

### `UnableToInterruptJobException` is gone

The exception dates from the Java lineage, where interrupting a job that did not implement
`InterruptableJob` had to fail. In 4.0 interruption is cancellation: every job receives the
cancellation token, so there is no such thing as a job that cannot be asked to stop, and the
exception had no throw site left. `Interrupt(JobKey)` and `InterruptFireInstance(fireInstanceId)`
keep their semantics — they set the token on the matching executions and return whether they found
any; whether the job honors the token remains the job's business. A `catch
(UnableToInterruptJobException)` block can simply be deleted; `HttpScheduler`'s error mapping no
longer resurrects the type either, so a remote scheduler fault on an interrupt call surfaces as the
`SchedulerException`-derived type the server actually reported.

## `TimeZoneUtil` became `Quartz.TimeZones`

`FindTimeZoneById` is how a trigger built with `InTimeZone(...)` comes back out of a job store, and
the wall-clock `GetUtcOffset(DateTime, TimeZoneInfo)` overload *is* the scheduler-wide daylight
saving policy — an ambiguous local time resolves to the daylight offset, the first of the two
occurrences. That is scheduling API, not a utility, so 4.0 stops calling it one: the type is
`TimeZones` — a static class named by its domain, the way `Matchers` is — and it lives in the root
namespace next to the builders whose behavior it defines. `FindTimeZoneById` sheds the words the
type name now carries and reads `TimeZones.FindById(id)`. Every file under a `Quartz.*` namespace
sees it without any `using`; elsewhere, `using Quartz.Util;` becomes `using Quartz;`:

```diff
- using Quartz.Util;
+ using Quartz;

- var zone = TimeZoneUtil.FindTimeZoneById("Europe/Helsinki");
+ var zone = TimeZones.FindById("Europe/Helsinki");
```

No configuration shim is needed: `TimeZones` is a static class that is called, never a type a
configuration string names and the scheduler instantiates.

Two members went internal on the way: `ConvertTime(DateTimeOffset, TimeZoneInfo)` and
`GetUtcOffset(DateTimeOffset, TimeZoneInfo)` were Mono-era shims over the `TimeZoneInfo` calls they
forward to — call `TimeZoneInfo` directly. The wall-clock `GetUtcOffset(DateTime, TimeZoneInfo)`
overload is the daylight saving policy, not a shim, and stays public.

The id alias table also stays: on Windows, "Coordinated Universal Time" and "CET" fail
`TimeZoneInfo.FindSystemTimeZoneById` and both `TryConvert*` conversions even on ICU, and resolve
through the table alone. Its one dead entry — "US Central Standard Time" ↔ "US/Indiana-Stark",
where each side aliased the other and neither is a system id on Windows — was pruned; both ids now
fail with the exception that points at `Quartz.Plugins.TimeZoneConverter`. When the direct lookup
and the aliases have both failed, `FindById` now also asks
`TimeZoneInfo.TryConvertIanaIdToWindowsId` before consulting the registered resolvers. The
conversion runs *after* the direct lookup on purpose: run first, it would turn "US/Eastern" into a
`TimeZoneInfo` whose `Id` is "Eastern Standard Time", and that rewritten Id is what a job store
writes back to `TIME_ZONE_ID`.

### `CustomResolver` became `AddResolver`

`CustomResolver` was one settable delegate, which made every installer overwrite the previous one:
two schedulers running `Quartz.Plugins.TimeZoneConverter` in the same process fought over the slot,
and shutting either scheduler down left the winner's resolver installed for the rest of the
process's life. `AddResolver` composes instead — each caller gets an `IDisposable` registration
back, and disposing it removes exactly that resolver:

```diff
- TimeZoneUtil.CustomResolver = id => Resolve(id);
+ IDisposable registration = TimeZones.AddResolver(id => Resolve(id));
  ...
- TimeZoneUtil.CustomResolver = null;
+ registration.Dispose();
```

Resolvers are consulted **most recently added first**, so a later registration shadows an earlier
one for the ids it resolves — which is exactly the last-write-wins behavior assigning
`CustomResolver` had, minus the data loss. A resolver declines an id by returning `null`, or by
throwing `TimeZoneNotFoundException` — the search catches it and continues with the next resolver,
and `FindById` itself throws only after every fallback has failed.

`TimeZoneConverterPlugin` now registers its own resolver on `Initialize` — targeting
`TZConvert.TryGetTimeZoneInfo`, so an unknown id declines quietly instead of by exception — and
disposes that registration on `Shutdown`. Shutting one scheduler down no longer changes time zone
resolution for the other schedulers in the process, and no longer leaves a resolver installed after
the last scheduler is gone.

## `TriggerUtils` became `TriggerFireTimes`

It computes fire times by advancing a copy of a trigger through its schedule, applying the calendar
at each step, which is exactly what `IOperableTrigger` adds over `ITrigger` — so it is named for the
fire times it computes and lives in `Quartz.Extensibility` with that contract, rather than in the
root namespace next to `IScheduler`. The type name carries the "fire times" half, so the methods
stop repeating it:

| 3.x | 4.x |
|---|---|
| `TriggerUtils.ComputeFireTimes(...)` | `TriggerFireTimes.Compute(...)` |
| `TriggerUtils.ComputeFireTimesBetween(...)` | `TriggerFireTimes.ComputeBetween(...)` |
| `TriggerUtils.ComputeEndTimeToAllowParticularNumberOfFirings(...)` | `TriggerFireTimes.ComputeEndTimeForCount(...)` |

Parameters and behavior are unchanged:

```diff
+ using Quartz.Extensibility;

- var times = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, calendar, 10);
+ var times = TriggerFireTimes.Compute((IOperableTrigger) trigger, calendar, 10);
```

## Other Breaking Changes

| Change | Details |
|--------|---------|
| `SimpleTriggerImpl.GetFireTimeBefore(DateTimeOffset? endUtc)` takes a non-nullable `DateTimeOffset` | The nullable parameter was a lie: after the one guard, 3.x dereferenced `endUtc!.Value`, so passing null threw rather than meaning "no bound". A caller holding a nullable end checks it first. `EndTimeUtc` itself is still `DateTimeOffset?` |
| `QuartzScheduler` and `QuartzSchedulerResources` are internal | Resolve `IScheduler` / `ISchedulerFactory`; scheduler-wide settings are `QuartzSchedulerOptions` |
| `JobType` introduced | Stores job type info without requiring an actual `Type` instance. A `Type` converts implicitly (validated: it must implement `IJob`); a string converts only explicitly or via the constructor, because resolving the name is deferred and can fail — `Type` throws for a name that does not resolve, `TryResolve` is the non-throwing probe. Equality (`Equals`, `==`/`!=`) is by `FullName`. There is deliberately **no** implicit conversion back to `Type`: `jobDetail.JobType.Type` spells out that assembly probing may happen, and can throw, at that read |
| `JobBuilder.OfType(JobType)` added | Carries a stored type name and its resolver through a rebuild without forcing the name to resolve; `OfType(string)` constructs an unvalidated `JobType` for the same reason |
| `IJobDetail.GetJobBuilder()` removed from the interface | The same call still compiles: it is an extension method on `IJobDetail` in the `Quartz` namespace, built from the detail's public state. Only an implementation of the interface has to change — see [An `IJobDetail` of your own](#an-ijobdetail-of-your-own) |
| `IJobDetail.WithJobData(JobDataMap)` added | The one member a job store needs of a detail it cannot construct: a copy of it carrying the given data. `RAMJobStore` calls it where it used to rebuild the detail through `JobBuilder`, so a custom `IJobDetail` survives its first `[PersistJobDataAfterExecution]` completion — see [An `IJobDetail` of your own](#an-ijobdetail-of-your-own) |
| The `Try*` members carry the nullability attributes their shape implies | Not breaking, and nothing behaves differently: `CronExpression.TryParse` marks its input `[NotNullWhen(true)]`, as `JobKey.TryParse`, `TriggerKey.TryParse` and `MonthDay.TryParse` already did, and `JobDataMap.TryGetValue` / `SchedulerContext.TryGetValue` mark `value` `[MaybeNullWhen(false)]`, as `Dictionary<,>.TryGetValue` does. They show up in the public API baselines, which is why they are listed here |
| `ObjectAlreadyExistsException.JobKey` / `.TriggerKey` added | The identity that clashed, set by the constructor that already took the job detail or the trigger, and `null` on the message-only constructor. The message is unchanged, so a handler that was parsing the key back out of it still works |
| `StdAdoDelegate.ConvertFromProperty` returns `Dictionary<string, object?>` | It returned the non-generic `System.Collections.IDictionary`, the last one on the public surface, and its only caller copied the entries into a `Dictionary<string, object?>` to build a `JobDataMap` out of them. An override changes its return type and drops nothing else |
| `DailyTimeIntervalTriggerImpl` rounds `StartTimeUtc` and `EndTimeUtc` down to the whole second | `CronTriggerImpl.StartTimeUtc` always did; this trigger did not, and its fire times are a whole number of intervals counted in whole seconds from the start of the day. A start of `22:50:00.68` with an 02:15 start-of-day and a five-minute interval therefore computed a first fire time of `22:50:00.000` — before the trigger's own start ([#3386](https://github.com/quartznet/quartznet/issues/3386)). A boundary time carrying milliseconds now loses them; the fire times themselves are unchanged |
| `RescheduleJob` recomputes a fire time that has fallen behind the start time | Handing `IScheduler.RescheduleJob` a trigger that already carries fire times still keeps a next fire time you set — that is what makes `GetTrigger` → mutate → `RescheduleJob` work, and it is how a replacement carries the cadence of the trigger it replaces. What it no longer keeps is one *behind the trigger's own start time*: rescheduling advances the start of a never-fired repeating simple trigger whose start is in the past, and a trigger stored with a next fire time before its start fires there and then immediately again at the start, because that is what its own `GetFireTimeAfter` answers ([#3554](https://github.com/quartznet/quartznet/issues/3554)). The first fire time is computed from the adjusted start instead. No job store recomputed anything here — both of them store the trigger as it was handed over |
| `SchedulingDataValidationException` derives from `SchedulerException` | It derived straight from `System.Exception`, the one break in the hierarchy, so a `catch (SchedulerException)` around loading an XML or JSON scheduling data file missed exactly the failure the file was most likely to have. `catch (Exception)` and `catch (SchedulingDataValidationException)` behave as before |
| `HttpScheduler.Context` and `.ListenerManager` throw `NotSupportedException` | A scheduler reached over HTTP has neither in this process. `Context` used to make a **synchronous** HTTP call from a property getter and hand back a detached copy that could not be written back; `ListenerManager` threw a `SchedulerException`. `UpdateTriggerDetails` throws the same exception with the same message shape, because the API has no endpoint for it — see [what is not supported remotely](packages/http-client.md#what-is-not-supported-remotely) |
| `RecoveringTriggerKey` behavior | `IJobExecutionContext.RecoveringTriggerKey` now returns `null` when not recovering instead of throwing |
| `IScheduler.Shutdown`'s token bounds the wait for running jobs | It was ignored on that path: 3.x and early 4.0 handed the thread pool `CancellationToken.None`, so `Shutdown(waitForJobsToComplete: true, ct)` waited however long the slowest job took. It now stops waiting when the token fires — the rest of the shutdown still runs, and the jobs are not cancelled — see [`Drain` is the shutdown that can be given a deadline](#drain-is-the-shutdown-that-can-be-given-a-deadline) |
| `QuartzHostedService` shuts its schedulers down concurrently | With more than one scheduler registered, the host's stop time was the *sum* of their waits for running jobs, which is what overran `HostOptions.ShutdownTimeout`. Each scheduler owns its own pool, store and thread, so there was nothing for them to serialize behind |
| `DictionaryExtensions` removed | `Quartz.Util.DictionaryExtensions` type was removed |
| `AdoJobStoreBase` connection methods | `GetLocalTransactionConnection` (was `GetNonManagedTXConnection`) and `GetConnection` now return `ValueTask<ConnectionAndTransactionHolder>` |
| `JobStoreSupport.UseProperties` `string` setter removed | The `bool` `AdoJobStoreOptions.StoreJobDataAsStrings` option and the read-only `CanUseProperties` remain; the property bridge parses the key |
| Protected `AdoJobStoreBase` / `StdAdoDelegate` members take a `CancellationToken` | Overrides have to add the parameter; callers do not |
| `ConnectionAndTransactionHolder.Close` takes a `CancellationToken` | `.Commit` and `.Rollback` took one too, and are now internal — see [A job store of your own can join your transaction](#a-job-store-of-your-own-can-join-your-transaction) |
| `IJobConfigurator<TJob>` members return `IJobConfigurator<TJob>` | `JobBuilder<TJob>` implements them explicitly and keeps its own `JobBuilder<TJob>`-returning members, so `JobBuilder.Create()…` chains are unaffected — see [Job data can name the property](#job-data-can-name-the-property) for the type parameter |
| `UsingJobData` takes an `object?` | The nine primitive overloads collapsed into one — see [Nine `UsingJobData` overloads became one](#nine-usingjobdata-overloads-became-one) |
| `IDirectoryScanListener` is asynchronous | `FilesUpdatedOrAdded` and `FilesDeleted` return `ValueTask` and take a `CancellationToken` |
| `SendMailJob.Send` is asynchronous | `protected virtual ValueTask Send(MailInfo mailInfo, CancellationToken cancellationToken = default)`. It uses `SmtpClient.SendMailAsync`, so a job fired on the scheduler's thread pool no longer blocks it for the length of an SMTP round trip, and `Execute` forwards its token. An override returns `default` where it used to return nothing |
| `SendMailJob.MailInfo` is `Quartz.Jobs.MailInfo` | The nested class became a top-level `sealed` type in the same namespace, with `required`/`init` members: `MailMessage` and `SmtpHost` are required, `SmtpPort` and `Credentials` are optional. An override of `Send` keeps compiling — `MailInfo` resolves through the `Quartz.Jobs` using — and code that constructed one with an object initializer keeps compiling too. Assigning to a property after construction does not: build the whole value in the initializer. The two credential strings became one `Credentials`, see [The SMTP password does not belong in job data](#the-smtp-password-does-not-belong-in-job-data) |
| The jobs in `Quartz.Jobs` have options types | `DirectoryScanOptions`, `FileScanOptions`, `NativeJobOptions` and `SendMailOptions`, written by `Using*Options(…)` on the job's configurator. The job data keys are unchanged and configuring key by key still works — see [The shipped jobs are configured by name](#the-shipped-jobs-are-configured-by-name) |
| `JobDataMap.GetEnumerator` returns the interface | `IEnumerator<KeyValuePair<string, object?>>` rather than `Dictionary<string, object?>.Enumerator`, matching `SchedulerContext`. `foreach` is unaffected; a variable declared as the concrete struct type needs retyping |
| `CronTriggerImpl.WillFireOn` is one method | `WillFireOn(DateTimeOffset timeUtc, bool dayOnly = false)`. The two overloads differed only by that default. Both call shapes compile unchanged |
| `JobExecutionContextImpl.IncrementRefireCount()` and the `JobRunTime` setter are internal | Both record what the scheduler observed while running the job; `JobRunShell` is the only caller, and writing either from a job or a listener reported a fire that never happened |
| `UseShutdownHook` takes an options delegate | `UseShutdownHook(o => o.CleanShutdown = false)` replaces `UseShutdownHook(cleanShutdown: false)`, matching the seven other `Use*` plugin methods. `UseShutdownHook()` is unchanged, and `CleanShutdown` still defaults to `true` |
| `LoggingJobHistoryPlugin.Name`, `LoggingTriggerHistoryPlugin.Name` are get-only | The name is handed to a plugin by `Initialize`; writing it afterwards did nothing |
| `TimeSpanParseRuleAttribute` is public | It says how a bare number in configuration is read as a `TimeSpan`, which a component configured by the same keys needs to be able to say |
| `TimeZoneUtil.CustomResolver` became `TimeZones.AddResolver(...)` | Returns an `IDisposable` whose disposal removes exactly that resolver; resolvers are consulted most recently added first — see [`CustomResolver` became `AddResolver`](#customresolver-became-addresolver) |
| Setter-only members gained getters | `DbMetadata.DbBinaryTypeName` (now nullable) and `.ParameterDbTypePropertyName` |
| `TriggerState.Executing` added | Reported where `Normal`, `Complete` or `Blocked` used to be, and `Blocked` narrowed to mean a sibling trigger is running (see [Executing is a trigger state](#executing-is-a-trigger-state)) |
| `IDriverDelegate.IsTriggerCurrentlyExecuting` removed | Replaced by `SelectTriggerStateWithExecuting`, which reads the state and the execution in one statement and returns `TriggerExecutionState` |
| `StdAdoConstants.SqlSelectCountExecutingFiredTriggersOfTrigger` removed | Removed with the method that used it; the per-job `SqlSelectCountExecutingFiredTriggersOfJob` remains — both on what is now an internal type |
| `StdAdoConstants` and `IAdoUtil` are internal | Statement text is not a contract; the schema names stay public on `AdoConstants`, which is a static class rather than a base class |
| Trigger persistence delegates are all public and `sealed` | `CronTriggerPersistenceDelegate`, `SimpleTriggerPersistenceDelegate` and `DailyTimeIntervalTriggerPersistenceDelegate` were internal; derive from `SimplePropertiesTriggerPersistenceDelegateBase` for a delegate of your own |
| `SimplePropertiesTriggerProperties` is an init-only `record` | The payload of that seam: one side of it builds a row, the other reads one, and neither edits what the other made. Build it with an object initializer — `new SimplePropertiesTriggerProperties { Int1 = …, String1 = … }` — where a property was assigned after construction, and use a `with` expression to derive one from another |
| `SchedulerConstants` is a `static class` | It was a `struct` holding only `const`s; constant references are unchanged |
| `MisfireInstruction` is internal | The five per-family enums are the vocabulary; every constant has an enum member with the same value |
| Every public `*Options` type is `sealed`, including `QuartzHttpApiOptions` and `HttpClientOptions` | None had a virtual member, and Quartz constructs each of them itself, so a subclass was never resolved. `QuartzHostedService` itself stays open for `AddQuartzHostedService<T>` |
| `QuartzOptions.Scheduling` is get-only | Set its properties rather than assigning a new instance; assignment discarded what other callbacks and `Quartz:Scheduling` had already set |
| `InternalTriggerState.Executing` removed | It was never assigned or read; RAMJobStore counts executions separately from the state that drives scheduling |
| `ScheduleBuilder<T>` removed | The five schedule builders implement `IScheduleBuilder` directly — see [`ScheduleBuilder<T>` is gone](#schedulebuilder-t-is-gone) |
| `DailyTimeIntervalScheduleBuilder`'s day-set fields are internal | `AllDaysOfTheWeek`, `MondayThroughFriday` and `SaturdayAndSunday` are reached through `OnEveryDay()`, `OnMondayThroughFriday()` and `OnSaturdayAndSunday()` |
| `PreserveHourOfDayAcrossDaylightSavings` and `SkipDayIfHourDoesNotExist` default to `true` | Turning the flag on reads as a call with no argument; passing the value still works |
| `TimeOfDay` removed | `TimeOnly` replaces it — see [`TimeOfDay` became `TimeOnly`](#timeofday-became-timeonly) |
| `DailyCalendar` has one constructor | Two `TimeOnly` values and an optional base calendar — see [`DailyCalendar` takes two `TimeOnly` values](#dailycalendar-takes-two-timeonly-values) |
| Calendar `SetDayExcluded` / `AddExcludedDate` removed | `AddExcludedDay` / `RemoveExcludedDay` over a read-only set — see [Excluded days are a read-only set](#excluded-days-are-a-read-only-set) |
| `CronCalendar.SetCronExpressionString` removed | Assign `CronExpression` instead; the property already accepted a parsed expression |
| `JobDataMap(IDictionary)` removed | `JobDataMap(IDictionary<string, object?>)` remains and absorbed the dirty-marker handling |
| `GetDecimal` / `TryGetDecimal` added to the accessor set | A `decimal` could be written but not read back |
| `DirtyFlagMap<TKey, TValue>` and `StringKeyDirtyFlagMap` are internal | `JobDataMap` and `SchedulerContext` are sealed, self-contained dictionaries; the typed accessors are extension members — see [JobDataMap and SchedulerContext stand alone](#jobdatamap-and-schedulercontext-stand-alone) |
| `JobDataMap.Dirty` / `ClearDirtyFlag()` are internal | Clearing the flag from a job silently skipped the `[PersistJobDataAfterExecution]` rewrite; `SchedulerConstants.ForceJobDataMapDirty` remains the supported way to force one |
| `JobDataMap.Equals` compares values, not just keys | Two maps with the same keys but different values no longer compare equal, and equal maps hash equally; `SchedulerContext` compares by reference |
| `SchedulerContext` is backed by `ConcurrentDictionary` | Reading and writing it concurrently is safe; enumeration no longer races plugin writes |
| `PropertySettingJobFactory` no longer merges the scheduler context into job properties | **Silent behavioral change** — read `context.Scheduler.Context` in `Execute`, or override `BuildJobDataMap` — see [Scheduler context entries are no longer injected into job properties](#scheduler-context-entries-are-no-longer-injected-into-job-properties) |
| `JobKey` and `TriggerKey` implement `IEquatable<T>` and `IParsable<T>` | Additive. `TryParse`/`Parse` invert `ToString`'s `<group>.<name>` form, splitting at the first '.' — a *group* containing '.' is the ambiguous case. Every job-store dictionary probe also stops paying for the object-comparer path, and the hash is computed once |
| `PutAsString` writes round-trip ("O") formats for `DateTime`/`DateTimeOffset` | Sub-second precision and Kind/offset survive; the dedicated `DateTime` overload also **rebinds** calls that used to hit the `IConvertible` one — see [`PutAsString` writes round-trip formats now](#putasstring-writes-round-trip-formats-now) |
| `TryGetDateTime` parses with `DateTimeStyles.RoundtripKind` | **Behavioral**: a stored string ending in `Z` now returns the UTC clock reading with `Kind=Utc` instead of a local-shifted `Kind=Local` value — see [`PutAsString` writes round-trip formats now](#putasstring-writes-round-trip-formats-now) |
| `PutAsString(string, Guid?)` removed | `null` wrote a present-but-null entry no reader could read back — see [`PutAsString(string, Guid?)` is gone](#putasstring-string-guid-is-gone) |
| `DateOnly`/`TimeOnly`/enum accessors, `PutAsString(DateOnly/TimeOnly)` and `TryGet<T>` added | Additive; job data catches up with the types 4.0 made primary |
| `SystemTextJsonSerializerOptions` and `NewtonsoftJsonSerializerOptions` removed | The `Use*JsonSerializer` callback hands you the registry itself; lambda bodies compile unchanged, `RegisterTriggerConverters` is a parameter — see [Custom trigger and calendar serializers are no longer static](#custom-trigger-and-calendar-serializers-are-no-longer-static) |
| Newtonsoft `ICalendarSerializer.CalendarTypeName` added (default interface member) | Existing implementations compile unchanged; the registry indexes a named serializer under both the assembly-qualified name (which 3.x payloads carry, and always stays) and the discriminator, case-insensitively — see [Newtonsoft types moved out of the core namespaces](#newtonsoft-types-moved-out-of-the-core-namespaces) |
| Newtonsoft calendar contracts moved to `Quartz.Serialization.Newtonsoft.Calendars` | Namespace symmetry with `Quartz.Serialization.SystemTextJson.Calendars`; source-only |
| `RecurrenceTriggerSerializer` unsealed in both packages | It was the one sealed built-in trigger serializer; deriving from a built-in serializer for a subclassed trigger is a supported scenario on all five |
| `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl` unsealed | The other half of that scenario: three of the five concrete triggers were sealed, so there was no subclassed trigger for a derived serializer to serialize. All five are subclassable now, as `CronTriggerImpl` and `SimpleTriggerImpl` always were. Purely a loosening — nothing was widened to `protected`, and a sealed type becoming unsealed breaks no caller |
| `ISchedulerFactory.GetAllSchedulers` returns `ValueTask<List<IScheduler>>` | Quartz returns concrete collection types from its query members for allocation and enumeration cost; this was the one that did not |
| `IInstanceIdGenerator.GenerateInstanceId` returns `ValueTask<string>` | It never returned null, and a null instance id is not a usable one |
| An `IJobStore` that implements `IJobListener` no longer receives events automatically | Register it as a job listener through the scheduler's `IListenerManager` |
| `[Serializable]` removed from `TriggerFiredBundle` and `TriggerFiredResult` | It has meant nothing since binary serialization was dropped |
| `TriggerFiredResult` is made by three factories | `TriggerFiredResult.Fired(bundle)`, `TriggerFiredResult.NotFired` and `TriggerFiredResult.Failed(exception)` replace the two constructors, which between them could build a result carrying both a bundle and an exception, or neither by accident — `new TriggerFiredResult((TriggerFiredBundle?) null)` was how a store said "this trigger turned out not to be firable". The three outcomes are now named, and the cast that used to be needed to pick a constructor overload is gone |
| `StdSchedulerFactory` removed | `QuartzSchedulerBuilder.Create().UseProperties(properties)` — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone) for every removed constant |
| `GetScheduler()` after `Shutdown()` throws | 3.x built a fresh scheduler, because the factory constructed every part itself. The container owns those lifetimes now, so the same call would re-initialize the thread pool and job store the shutdown just tore down and hand back the same closed instance. It throws `SchedulerException` instead. Use `Standby()`/`Start()` to pause and resume a scheduler, or build a new host or container for a fresh one |
| `QuartzSchedulerBuilder` implements `IQuartzBuilder` | Its five duplicated members and `Configure(Action<IQuartzBuilder>)` are gone, and configuration members return `IQuartzBuilder`, so `Build()` is called on a builder held in a variable — see [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder) |
| `IQuartzBuilder` gained `UseThreadPool(IThreadPool)` and `UseJobStore(IJobStore)` | A pre-built part can be handed to a scheduler registered with `AddQuartz`, not only to a standalone one |
| `IQuartzBuilder.ConfigureJobScope(...)` | The `ConfigureScope` hook as a delegate, so preparing a job's scope no longer needs a job factory of your own — see [The job scope is prepared without writing a job factory](#the-job-scope-is-prepared-without-writing-a-job-factory) |
| `IQuartzBuilder` gained `UseJobStore<T>` / `<T, TOptions>` / factory | The seam for a job store of your own, built by the container with its scheduler's collaborators — see [A component of your own is chosen the same way a shipped one is](#a-component-of-your-own-is-chosen-the-same-way-a-shipped-one-is) |
| `IQuartzBuilder` gained `UseInstanceIdGenerator<T>` / `<T, TOptions>` / instance | Replaces `quartz.scheduler.instanceIdGenerator.type`, and sets `GenerateInstanceId` because choosing a generator means the id is generated |
| The builder's listener overloads take `params IReadOnlyCollection<IMatcher<T>>` | Aligned with `IListenerManager`; existing call sites are unaffected |
| `AddQuartzHttpApi` is on `IServiceCollection`, and only there | The API is container-wide, not one scheduler's — `services.AddQuartzHttpApi()`. The `IQuartzBuilder` form an earlier 4.0 preview kept is gone |
| Clustering settings moved to `ClusteringOptions` | `AdoJobStoreOptions.Clustered` and the two `ClusterCheckin*` settings are gone; `IJobStore.Clustered` reports the state rather than setting it — see [Clustering is configured in one place](#clustering-is-configured-in-one-place) |
| `AdoJobStoreBase`'s constructor takes `IOptions<ClusteringOptions>` | Between `storeOptions` and `objectSerializer`; a job store deriving from it has to pass one on |
| `UseSQLite` is `UseSystemDataSqlite`, `UseMicrosoftSQLite` is `UseSqlite` | **The short name changed meaning** — see [The SQLite extension methods swapped names](#the-sqlite-extension-methods-swapped-names) |
| `UseDataSourceConnectionProvider()` removed | `DataSourceOptions.UseRegisteredDataSource`, which is what it set |
| `AddDataSourceProvider()` removed | Its other half. It registered `DataSourceDbProvider` in the container for `UseDataSourceConnectionProvider()` to name; `UseRegisteredDataSource` builds the provider itself from the registered `DbDataSource` — see [`AddDataSourceProvider()` went with it](#adddatasourceprovider-went-with-it) |
| Every `Use<Db>` gained a `(DbProviderFactory factory, string connectionString)` overload | `UseSqlServer(SqlClientFactory.Instance, connectionString)` and its siblings reach the driver through the factory it ships, so no type is resolved from a string and none is constructed by reflection. This is the registration a `PublishTrimmed` or `PublishAot` application uses; it takes the connection string directly, so `ConnectionStringName` does not apply to it — see [Naming a driver, or handing over its factory](configuration/reference.md#naming-a-driver-or-handing-over-its-factory) |
| `UseOracle(factory, connectionString, configureCommand, configureBinaryParameter)` | Oracle is the one shipped driver that needs more than a factory: ODP.NET binds by position unless `OracleCommand.BindByName` is set, and reads `DbType.Binary` as `OracleDbType.Raw`, which holds two kilobytes of a job data map. Naming the driver instead says both for you |
| `UseGenericDatabase(factory, connectionString, DbMetadata)` | A driver Quartz ships no description for, reached through its own factory and described in code. No provider name, because the description arrived rather than being looked up |
| `DbMetadata` gained `ConfigureCommand` and `ConfigureBinaryParameter` | The two things the name path reaches by reflecting over `CommandType` and `ParameterType`, said as lambdas by the application that references the driver. Every `Type` on `DbMetadata` is optional now: a description behind a factory or a `DbDataSource` names none, and a binary parameter with neither a seam nor a described parameter type binds as `DbType.Binary` |
| The `Use<Db>` overloads that take a name carry `[RequiresUnreferencedCode]` | Including `UseGenericDatabase(provider, …)`. The warning surfaces inside your `UsePersistentStore` callback and names the two ways out; it does not reach `AddQuartz`, so an application on the in-memory store is told nothing |
| `ProviderFactoryDbProvider` added | The `IDbProvider` those overloads register, public so that `UseConnectionProvider(_ => new ProviderFactoryDbProvider(metadata, factory, connectionString))` can build one over a factory no overload knows about |
| The Oracle driver description says `DbBinaryTypeName = "Blob"` | It named no binary type, so ODP.NET inferred `Raw` from the `byte[]` and capped a job data map at 2000 bytes in SQL. Both ways of reaching Oracle now write a `BLOB` |
| `QuartzOptions.SchedulerName`, `.SchedulerId`, `.MisfireThreshold` removed | Each duplicated a typed option — see [`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings) |
| Job execution metrics are published by every scheduler | The meters were configured only by `StdSchedulerFactory`, so a scheduler registered with `AddQuartz` published none |
| **Every instrument and attribute was renamed, and two instruments were dropped** | `scheduling.quartz.*` became `quartz.job.execution.*`, unprefixed attributes became `quartz.*`, and the two counters gave way to the histogram's own count. Every dashboard, alert and recording rule keyed on the old names breaks — the [old → new table](#old-and-new-telemetry-names) is the complete mapping |
| `quartz.job.execution.duration` records **seconds**, not milliseconds | A histogram's default bucket boundaries assume seconds, so every execution over ten seconds landed in the top bucket. A chart with a hard-coded millisecond axis reads 1000× low until it is changed — see [Job execution metrics](#job-execution-metrics) |
| `quartz.job.execution.active` is an `UpDownCounter<long>`, unit `{job}` | It was a `Counter<long>` receiving the `-1` that ends an execution, which an exporter aggregating a monotonic sum may drop; `ea` is not a UCUM unit — see [Job execution metrics](#job-execution-metrics) |
| Every job execution measurement is tagged with `quartz.scheduler.name` | A process can run several schedulers, whose measurements used to arrive as one series. One series per scheduler where there was one in total: a query that aggregated across everything needs to say so — see [Job execution metrics](#job-execution-metrics) |
| Every measurement is tagged with `quartz.scheduler.id` too | A cluster is several schedulers sharing one name, so the name alone never answered *which node*. The id was a span attribute only. One series per node where there was one per scheduler — see [Job execution metrics](#job-execution-metrics) |
| `quartz.execution.group` on the execution span and the execution measurements | The bucket a thread limit is applied per, so a saturated group can be seen in the signals rather than inferred. A trigger that names no group carries no such attribute at all, rather than an empty one |
| `ActivityTags.ExecutionGroup`, `.JobStoreOperation`, `.RecoveredInstanceId` added | The three new attribute names, as constants beside the ones that were already there |
| Five new instruments cover misfires, acquisition and the cluster | `quartz.trigger.misfire`, `quartz.trigger.acquisition.duration`, `quartz.trigger.acquired`, `quartz.cluster.checkin.duration`, `quartz.cluster.recovery.trigger` — none of those events produced any measurement before — see [the observability page](packages/opentelemetry-integration.md#metrics) |
| `quartz.jobstore.operation.duration` measures every store round trip | Named by `quartz.jobstore.operation`, whose value is the same string the operation's span is called, and tagged with `error.type` when the call failed |
| **Every store emits `Quartz.JobStore.*` spans, not only the ADO.NET one** | Store tracing was thirty-three call sites inside `AdoJobStoreBase`, so the in-memory store, a community package's store and a store of your own produced none. It is a decorator over `IJobStore` now, applied once by the registration that builds a scheduler's resources. The span names, kind and attributes are unchanged. A store the container builds is therefore not the object the scheduler holds — anything comparing `IJobStore` instances or type-testing one has to unwrap `Quartz.Impl.DelegatingJobStore` first, and `IScheduler.GetMetadata().JobStoreTypeName` still reports the inner store |
| The meter is built from the container's `IMeterFactory` | It was a process-wide static, so two containers in one process shared one set of instruments and `MetricCollector` could not see them. The meter's name and what an exporter subscribes to are unchanged, and an application that never calls `AddMetrics()` still gets a meter — see [Job execution metrics](#job-execution-metrics) |
| `scheduling.quartz.exception_type` is `error.type`, naming the exception the job threw | The tag was added to a copy of the tag list and discarded, so the counter said only that something failed — and the type it named was the `JobExecutionException` the run shell wraps everything in. It is OpenTelemetry's conventional name now — the one attribute Quartz does not namespace — it is on the duration histogram and the execution's span, and a query matching the old name or expecting the old value has to be rewritten — see [Job execution metrics](#job-execution-metrics) |
| `Quartz.Diagnostics.QuartzInstrumentation` publishes the `ActivitySource` and `Meter` names | `AddSource("Quartz")` / `AddMeter("Quartz")` were the only strings on the instrumentation surface with no constant behind them. Both values are still `"Quartz"`, so existing wiring keeps working; `InstrumentationOptions` is gone with the change — see [Job execution metrics](#job-execution-metrics) |
| The vetoed-fire span is `Quartz.Job.Veto` | `OperationName.Job.Veto` read `"Quartz.Job.Vetoed"` — the constant's name and its value disagreed. `Quartz.Job.Execute` and the `Quartz.JobStore.*` span names are unchanged |
| `XmlSchedulingOptions` and `JsonSchedulingOptions` merged | They were byte-for-byte identical and are now one type |
| Constructing a scheduler no longer starts a thread | `QuartzScheduler` starts its scheduler thread from `Start()` rather than its constructor, so resolving the service graph, running a `ValidateOnBuild` pass or asserting on registrations no longer spins one up. The thread always started paused, so this changes when the thread exists, not when jobs run |
| `IPersistentStoreBuilder.AcceptEnlistedTransactions()` | Never shipped; the setting is `ConfigureStore(o => o.AcceptEnlistedTransactions = true)`, like the other nineteen `AdoJobStoreOptions` settings — see [Joining an existing transaction](tutorial/job-stores.md#joining-an-existing-transaction) |
| Group matchers translate to SQL correctly | `SelectTriggerGroups`, `DeletePausedTriggerGroup` and both `UpdateTriggerGroupStateFromOtherState(s)` members always built a `LIKE`, even for an equality matcher; they take the `=` path now, which is exact and index-friendly. `LIKE` patterns escape `%`, `_` and the escape character in the matcher's own text with an explicit `ESCAPE` clause, so a group literally named `50%` matches itself. The escape character is `!` rather than a backslash, because MySQL applies C-style escaping inside string literals and `ESCAPE '\'` is a syntax error there |
| `StdAdoConstants` group and fired-trigger statements were split | `SqlDeletePausedTriggerGroup`, `SqlSelectTriggerGroupsFiltered`, `SqlUpdateTriggerGroupStateFromState` and `SqlUpdateTriggerGroupStateFromStates` are `…Equals` / `…Like` pairs, and the FIRED_TRIGGERS statements are one `SqlSelectFiredTriggers` / `SqlDeleteFiredTriggers` base plus `SqlFiredTrigger*Predicate` fragments. The type is internal |
| `IDashboardAuthorizationFilter` and `QuartzDashboardOptions.AuthorizationFilter` removed | Nothing ever invoked the filter, so setting it bought a false sense of security. Use `AuthorizationPolicy`, which is enforced |
| `IDashboardHistoryStore` is asynchronous | `ValueTask AddExecution`, `ValueTask<PagedResult<DashboardHistoryEntry>> QueryExecutions(DashboardHistoryQuery)`, so a store can talk to a database. `SearchFilter.DebounceMilliseconds` is a `TimeSpan Debounce`, and `InProcessQuartzApiClient` is internal — resolve `IQuartzApiClient` |
| `IDashboardHistoryStore` carries the misfire feed | `AddMisfire`, `QueryMisfires(DashboardMisfireQuery)` and `CountMisfires(name, since)` are new members on the interface, so **an application with its own store has to implement three more** — see [History and live events say which node they came from](#history-and-live-events-say-which-node-they-came-from) |
| `IQuartzApiClient` speaks Quartz's vocabulary, with `IScheduler`'s verbs | The renames are a table of their own — see [The client speaks the scheduler's verbs](#the-client-speaks-the-scheduler-s-verbs) |
| The dashboard's HTTP-backed API client is gone | `QuartzApiClient` was never registered; the dashboard renders the schedulers in its own process, and `QuartzDashboardOptions.BaseUrl` and `.ApiPath` went with it — see [The dashboard reads the schedulers in its own process](#the-dashboard-reads-the-schedulers-in-its-own-process) |
| Serializers outside a scheduler read a container-wide registry | Because the serializer maps are per-serializer, the HTTP API and `Quartz.HttpClient` read a `SystemTextJsonSerializerRegistry` registered in the container. Register it as a singleton to make a custom serializer visible to them. The dashboard no longer registers one of its own: it passes triggers and calendars through as themselves |
| `SystemTextJsonSerializerRegistry` gained `AddTypeInfoResolver(IJsonTypeInfoResolver)` | Where reflection-based serialization is off — a `PublishTrimmed` or `PublishAot` application — this is how job-data values of the application's own types are answered for. Hand it a generated `JsonSerializerContext`'s `Default`. Everything Quartz writes, and every custom trigger or calendar registered with the registry, is already covered; with reflection on it changes nothing — see [Trimming annotations](#trimming-annotations) |
| The `Quartz` package declares `IsAotCompatible`, and binds configuration with the source-generated binder | No API moved. Quartz reports no `IL3050` at all now: the `Quartz` section binds through code the compiler wrote rather than through `ConfigurationBinder`'s reflection, so an application configured from `appsettings.json` publishes native AOT as safely as one configured in code — with the reflection binder that preceded it, `MaxBatchSize`, `ShutdownJobInterruption` and the whole scheduler context arrived as their defaults out of a native publish, silently. The `IL2xxx` for the string-named paths are unchanged — see [Trimming annotations](#trimming-annotations) |
| Every other shipped package says whether it is trimmable | No API moved, and no public member gained `[RequiresUnreferencedCode]` or `[DynamicallyAccessedMembers]`. Six packages are marked `IsTrimmable` and report their own reflective call sites where 3.x reported one `IL2104` per assembly; `Quartz.Serialization.Newtonsoft` and `Quartz.Dashboard` say `IsTrimmable=false` on purpose, and their readmes say why — see [Trimming annotations](#trimming-annotations) |
| `IDriverDelegate` trigger states are `StoredTriggerState` | Eighteen members took the state as a `string`; the database still stores the same values — see [Trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate) |
| The `…FromOtherStates` members take a state collection | Two or three fixed old-state parameters became one `IReadOnlyCollection<StoredTriggerState>` |
| `FiredTriggerQuery.InstanceName` is `InstanceId` | With the `instanceName` parameters of the scheduler-state members; the `INSTANCE_NAME` column is unchanged |
| `IDriverDelegate.IsJobCurrentlyExecuting` takes a `JobKey` | It took `(string jobName, string jobGroup)` |
| `IDriverDelegate.SelectJobForTrigger`'s `loadJobType` is required | It defaulted in front of the cancellation token; pass `loadJobType: true` for the old default |
| `IDriverDelegate.UpdateTriggerPreferredNodeConditional` takes a `PreferredNodeTransition` | Four loose compare-and-swap parameters became one record naming `Expected` and `New` |
| `TriggerAcquisitionCriteria.LiveNodeCutoff` is a `required DateTimeOffset` | It was an optional `long` of `UtcTicks` beside two required `DateTimeOffset` siblings, and omitting it silently meant "every node is dead". The tick conversion happens in `AddPreferredNodeParameters`, whose parameter is a `DateTimeOffset` too |
| `TriggerAcquisitionRequest.ExcludedJobTypeNames` and `TriggerAcquisitionCriteria.ExcludedJobTypeNames` added | Optional exact job-type-name exclusions for acquisition, honoured by every shipped store. The ADO.NET store applies them in SQL, where comparison follows the `JOB_CLASS_NAME` column's collation; `RAMJobStore` compares `JobType.FullName` ordinally |
| `StdAdoDelegate.AddPagingParameters(cmd, int skip, int take, bool)` | `skip`/`take` were `long` while `PagedQuery.Skip`/`.Take` are `int`; the dialect override contract now matches the query object it serves |
| `StdAdoDelegate.ReadBytesFromBlob` takes a `DbDataReader` and is asynchronous | It took a `System.Data.IDataReader` — the last legacy `System.Data.I*` interface anywhere in the public surface — which forced a synchronous, thread-blocking read that ignored the cancellation token it was handed. It is `async ValueTask<byte[]?>` over `IsDBNullAsync` / `GetFieldValueAsync<byte[]>`. **The behaviour contract is unchanged**: a `NULL` column still yields `null`, and an empty blob still yields an empty array, which the only caller (`GetObjectFromBlob`) has always treated the same as `null` by testing `Length > 0`. An override changes its parameter type; every reader the store passes was already a `DbDataReader` |
| `StdAdoDelegate`'s date/time and time-span conversions are non-virtual | UTC ticks and whole milliseconds are the schema contract the liveness SQL assumes; only the boolean pair remains a dialect seam. A delegate that changes the storage format implements `IDriverDelegate` itself — see [If you implement `IDriverDelegate`: the listing members](#if-you-implement-idriverdelegate-the-listing-members) |
| `JobStoreTX` is `LocalTransactionJobStore`, `JobStoreCMT` is `ExternalTransactionJobStore` | The names now say whose transaction the store uses. `quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz` and the `JobStoreCMT` spelling still resolve, with a warning — see [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use) |
| `GetNonManagedTXConnection`, `ExecuteInNonManagedTXLock`, `RetryExecuteInNonManagedTXLock` renamed | `GetLocalTransactionConnection`, `ExecuteInLocalTransactionLock`, `RetryExecuteInLocalTransactionLock`; protected, so only a `AdoJobStoreBase` subclass sees them |
| The three ADO store constructors take one `AdoJobStoreDependencies` | `AdoJobStoreBase`, `LocalTransactionJobStore` and `ExternalTransactionJobStore` each took the same twelve parameters, and a store deriving from one of them restated all twelve to forward them. They take one record instead, whose properties are exactly those parameters: `new LocalTransactionJobStore(signaler, typeLoader, …)` becomes `new LocalTransactionJobStore(new AdoJobStoreDependencies(signaler, typeLoader, …))`, and a derived store chains `: base(dependencies)`. The container registers the record per scheduler and builds it the way it built the store, so `UseJobStore<MyStore>()` and `UsePersistentStore<MyStore>(…)` were unchanged. Moot now: all four types are internal and nothing derives from them — see [The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) |
| The ADO store reports a failure once, not once per nesting level | Every operation wraps a provider exception in a `JobPersistenceException` naming what it could not do, as before. What it no longer does is wrap a `JobPersistenceException` again: a failure the store raised on purpose — `The job (…) referenced by the trigger does not exist.`, `Calendar cannot be removed if it is referenced by a trigger!`, an `ObjectAlreadyExistsException` — now reaches the caller as itself, where 3.x prefixed it with the operation in progress (`Couldn't store trigger '…' for '…' job: The job (…) …`). The type is unchanged, so `catch (JobPersistenceException)` and the HTTP API's exception mapping are unaffected; code matching on the *text* of a nested message reads the inner message now |
| Cancelling a store operation raises `OperationCanceledException` | It was reported as a `JobPersistenceException` wrapping one. Cancellation is not a persistence failure, and callers match on the type. This holds through the lock and transaction plumbing as well as through the guard around each statement: an operation cancelled while it holds a lock and an open transaction rolls back, releases the lock, and reports the cancellation as itself rather than as `Unexpected runtime exception` or `Failed to obtain DB connection`. An `OperationCanceledException` raised while the caller's own token is *not* cancelled keeps the classification it had — the caller did not ask to stop, so from where they stand the store failed |
| `AdoJobStoreBase`'s nine `Execute…Lock` overloads became four members | Optional parameters replace the ladder, and no member returns `object` as a stand-in for `void` any more — see [Nine `Execute…Lock` overloads became four members](#nine-execute-lock-overloads-became-four-members) |
| `ExternalTransactionJobStore.OpenConnection` moved to `AdoJobStoreOptions.OpenConnection` | The last store setting outside the options system; the store reads it once at construction — see [Nine `Execute…Lock` overloads became four members](#nine-execute-lock-overloads-became-four-members) |
| `ILockHandler` takes a `SchedulerLock` | The `string lockName` had two legal values. The `LOCK_NAME` column and the Redis keys are unchanged — see [Locks are a `SchedulerLock`, not a string](#locks-are-a-schedulerlock-not-a-string) |
| `AdoJobStoreBase.LockTriggerAccess` / `.LockStateAccess` removed | `SchedulerLock.TriggerAccess` / `.StateAccess` replace the two protected constants |
| ~25 `AdoJobStoreBase` configuration properties are read-only and `protected`/internal | They duplicated `AdoJobStoreOptions` / `QuartzSchedulerOptions`; resolve `IOptions<AdoJobStoreOptions>` to read, configure the options to write. `MisfireThreshold` deliberately stays publicly *readable* (its setter is internal), and the `IJobStore` members stay public — see [The job store configuration is read-only](#the-job-store-configuration-is-read-only-and-no-longer-a-public-currency) |
| The seven conn-taking `Pause…`/`Resume…`/`RecoverMisfiredJobs` overloads are `protected` | They took a `ConnectionAndTransactionHolder` no caller outside the store can obtain; call the public keyed overloads, which take the lock and the connection themselves |
| `AdoJobStoreBase`'s virtual surface is a curated seam | Only `Initialize`, `Shutdown`, `GetConnection`, `GetLocalTransactionConnection`, `ExecuteInLock<T>`, `IsTransient`, `AcquireNextTriggers`, `CreateAcquisitionCriteria` and `GetFiredTriggerRecordId` remain overridable; the other ~75 members were virtual by default, not by design, and freezing the store's internal call order as a behavior contract would have made it unrefactorable |
| A transaction the database rolled back is retried whatever the driver calls it | `AdoJobStoreBase.IsTransient` also reads the exception's SQLSTATE and retries class `40`, the standard's "transaction rollback", with `40002` excepted. 3.x read only the driver's own `IsTransient` property, SQL Server's error numbers and SQLite's busy codes, so a Firebird write conflict (`40001`) and a MySQL `1213` deadlock on MySql.Data were fatal — neither driver reports the condition as transient — where SQL Server's 1205 and SQLite's `BUSY` were retried. They are now retried too, bounded by the same `MaxTransientRetries` and `TransientRetryInterval`. An `IsTransient` override still replaces the whole verdict |
| `AdoJobStoreBase.DriverDelegateType` and `.DontSetAutoCommitFalse` removed | Nothing read either one; the driver delegate is injected |
| `AdoJobStoreOptions.DontSetAutoCommitFalse` removed | The option the deleted store property mirrored. No code path read it and no `quartz.*` key set it, so setting it configured nothing |
| `AdoJobStoreBase.LastCheckin` is internal, `LogWarnIfNonZero` is removed | Cluster check-in bookkeeping and a logging helper, neither of them an extension point. The helper's callers raise source-generated events, at the level its name always claimed — see [Every message carries an event id](#every-message-carries-an-event-id) |
| `AdoJobStoreBase.DoCheckin` and `.DoRecoverMisfires` lost the `Do` prefix | `CheckIn` and `RecoverMisfires`. The prefix distinguished each from nothing — neither had a same-named member to be told apart from — and in the box only `ClusterManager` and `MisfireHandler` call them |
| Four `AdoJobStoreBase` parameters are spelled out | `CloseConnection`, `RollbackConnection` and `CommitConnection` take `connection` where they took `cth`; `CalcFailedIfAfter` takes `record` where it took `rec`. Only a call that named its argument is affected |
| `AdoJobStoreBase.RecoverJobs(CancellationToken)` returns `ValueTask` | The `bool` it returned was the constant `true` |
| `DbLockHandler.LockSql` (was `Sql`) and `InsertSql` are get-only, fed by the constructor | Assigning one after construction left it un-prefixed relative to its pair — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| The row-lock handlers are named for the SQL they issue | `StdRowLockSemaphore` is `SelectForUpdateLockHandler`, `UpdateLockRowSemaphore` is `UpdateRowLockHandler`, `PostgreSQLRowLockSemaphore` is `PostgreSqlSelectForUpdateLockHandler`, `UpdateLockRowSemaphoreMOT` is `SqlServerMemoryOptimizedUpdateRowLockHandler`. `quartz.jobStore.lockHandler.type` naming an old one still resolves, with a warning — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| Row-lock handler SQL fields are `protected const` and consistently named | `UpdateLockRowSemaphore.SqlUpdateForLock` / `.SqlInsertLock` are `UpdateRowLockHandler.UpdateForLock` / `.InsertLock`; `SelectForUpdateLockHandler.SelectForLock` / `.InsertLock` keep their member names |
| `ISemaphore` is `ILockHandler`, and every implementation says `LockHandler` | A semaphore is a counted permit; this is a mutual-exclusion lock with one holder. `DBSemaphore` is `DbLockHandler`, `SimpleSemaphore` is the internal `InProcessLockHandler`, `RedisSemaphore` is `RedisLockHandler` — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| `ISemaphore.ObtainLock` is `ILockHandler.AcquireLock` | `ReleaseLock` and `RequiresConnection` are unchanged; the verb pairs with the one that gives the lock back |
| `SemaphoreContext` is `LockHandlerContext` | The record `Initialize` takes. Its members are unchanged |
| `ILockHandler.AcquireLock` throws on cancellation instead of answering `false` | `false` means a re-entrant acquire and nothing else, so a cancelled acquire that answered it told the store the lock was already held. `SimpleSemaphore` and `RedisSemaphore` did; their 4.0 counterparts throw — see [A cancelled acquire throws, and `false` means only re-entry](#a-cancelled-acquire-throws-and-false-means-only-re-entry) |
| `ILockHandler.Initialize(LockHandlerContext)` replaces `ITablePrefixAware` | Identity arrives through one initialization call instead of a property pair; the default implementation does nothing — see [A lock handler is told which scheduler it locks for](#a-lock-handler-is-told-which-scheduler-it-locks-for) |
| `LockHandlerContext` also carries `TimeProvider` and `CommandTimeout` | The environment a handler locks in, beside the identity it locks under. `DbLockHandler` exposes the clock as a `protected TimeProvider` and both shipped row-lock handlers back off on it, so a retry is observable without waiting out the real second |
| `LockHandlerContext.LoggerFactory` and `DriverDelegateContext.LoggerFactory` added | Where the handler and the delegate create their loggers, defaulting to `NullLoggerFactory.Instance`. The job store passes the factory its container gave it, so lock contention and statement failures reach an application that never called `LogProvider.SetLogProvider` — see [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient) |
| The ADO stores take an optional `ILoggerFactory?`, through `AdoJobStoreDependencies.LoggerFactory` | The container fills it in, and the store, its cluster manager, its misfire handler and its units of work log through it. A store constructed by hand leaves it unset and keeps reading the ambient factory — see [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient) |
| The components `Use*<T>()` builds take an optional logger | `TaskSchedulingThreadPool` (both constructors) and `DefaultThreadPool`, `ZeroSizeThreadPool` and `HostNameBasedIdGenerator` take an `ILogger<T>?`; `SimpleJobFactory`, `PropertySettingJobFactory` and `MicrosoftDependencyInjectionJobFactory` take an `ILoggerFactory?`, because the factory chain logs under two categories. All are optional and `null` means the ambient factory, so `new DefaultThreadPool()` and a derived type's `: base()` compile unchanged; a subclass that wants the container's logger passes the parameter through — see [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient) |
| `DbLockHandler.PrepareCommand` / `.AddCommandParameter` are `protected` | The two things a subclass needs to implement `ExecuteSql`, which `private protected AdoUtil` had left it unable to do at all — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `SelectForUpdateLockHandler.MaxRetry` / `.RetryPeriod` are `init`-only | Assign them in an object initializer. `quartz.jobStore.lockHandler.maxRetry` / `.retryPeriod` still reach them — the property bridge writes by reflection, which an init accessor does not stop |
| `UpdateRowLockHandler.RetryPeriod` added | Its backoff was a literal one second, so it ignored `quartz.jobStore.lockHandler.retryPeriod` while its sibling honoured it. The default is unchanged at one second |
| `AdoJobStoreBase.GetEnlistedConnection` is `protected` | So a job store outside the core assembly can honour an enlisted transaction rather than silently opening its own connection |
| `ConnectionAndTransactionHolder` gained an ownership-aware constructor and `OwnsResources` | `(connection, transaction, ownsResources)` for a store running on a connection it did not open |
| `ConnectionAndTransactionHolder` is `IAsyncDisposable` | `await using` is the form to prefer in an async method — the provider closes its connection without blocking a thread. Purely additive; `using` keeps working, and both disposal paths now log failures at debug instead of swallowing them |
| `FiredTriggerRecord`, `RecoverMisfiredJobsResult`, `DriverDelegateContext` are `sealed record`s | Immutable, with `required` / `init` members instead of settable ones — see [The driver delegate speaks in records](#the-driver-delegate-speaks-in-records) |
| `DelegateInitializationArgs` is `DriverDelegateContext`, and its `InstanceName` is `SchedulerName` | It holds the scheduler name, not the instance id, and the three ADO.NET initialization seams now agree on the term — see [The initialization seams are context records](#the-initialization-seams-are-context-records) |
| `ITriggerPersistenceDelegate.Initialize` takes a `TriggerPersistenceDelegateContext` | It took `(string tablePrefix, string schedulerName, IDbAccessor dbAccessor)` — two transposable strings and an accessor. Neither delegate seam has a default implementation, because a delegate that skips initialization has nothing to issue a statement with — see [The initialization seams are context records](#the-initialization-seams-are-context-records) |
| `DriverDelegateContext.InitString` replaced by `TriggerPersistenceDelegates` | The delimited string became a typed collection; register delegates with `UseTriggerPersistenceDelegate<T>()`. The legacy `quartz.jobStore.driverDelegateInitString` key still translates |
| `FiredTriggerRecord.FireInstanceState` is a `StoredTriggerState` | The last raw `AdoConstants.State*` comparisons in the store; `[Serializable]` is gone with it, and the always-populated members are non-nullable |
| `StoredTriggerState` moved to `Quartz.Extensibility`; `TriggerStateResolver.Resolve` is public | The stored-state vocabulary and its reporting precedence belong to every job store, not just the ADO one; members and stored strings are unchanged, so a delegate updates a `using` directive — see [Trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate) |
| `RecoverMisfiredJobsResult.EarliestNewTime` is `EarliestNewTimeUtc` | The property and its constructor argument disagreed about the `Utc` suffix |
| `RecoverMisfiredJobsResult.NoOp` is a static property | It was a `public static readonly` field beside sentinels that are properties; source keeps compiling, a recompile is required |
| `TriggerAcquireResult` carries a `TriggerKey` | It carried `TriggerName` and `TriggerGroup`, which every caller immediately paired back up |
| `TriggerStatus` removed, `IDriverDelegate.SelectTriggerStatus` is `SelectTriggerHeader` | It returns `StoredTriggerHeader`, an immutable record whose state is a `StoredTriggerState` — see [The driver delegate speaks in records](#the-driver-delegate-speaks-in-records) |
| `IDriverDelegate.ValidateSchema` added | Schema validation was a `StdAdoDelegate` method reached by type test, so a delegate of your own silently skipped it — see [`ValidateSchema` is part of `IDriverDelegate`](#validateschema-is-part-of-idriverdelegate) |
| `IDriverDelegate.ApplyTriggerFired` added | One trigger fire is one round trip's worth of writes rather than five to eight; `TriggerFiredUpdate` describes it — see [Batched trigger fire](#batched-trigger-fire) |
| `IDriverDelegate` gains a transition-list `UpdateTriggerStatesForJobFromOtherState` and a state-filtered `SelectTriggerKeysForJob` | Overloads beside the existing ones, so the blocking and unblocking of a job's triggers is one round trip and completion stops reading a state per trigger — see [Batched trigger fire](#batched-trigger-fire) |
| `IDriverDelegate` gains set-taking `UpdateTriggerStatesForJobsFromOtherState` and `DeleteFiredTriggers` | Overloads beside the single-key ones, so recovering a failed node issues one round trip per kind of change rather than one per fired-trigger row. Its fourth step, releasing what the node had reserved, uses the key-set `UpdateTriggerStatesFromOtherStates` listed with the acquisition members — see [Batched cluster recovery](#batched-cluster-recovery) |
| `IDriverDelegate.SelectTriggerKeysInState` added | Which of a set of triggers are in a state, in one read; recovery asked `SelectTriggerState` once per trigger a dead node had left behind — see [Batched cluster recovery](#batched-cluster-recovery) |
| `StoredTriggerHeader` carries `TriggerType` | A fifth positional parameter; it comes off the row the state came from, which is what removes the separate type lookup — see [Batched trigger fire](#batched-trigger-fire) |
| `ITriggerPersistenceDelegate.TryDescribeUpdateExtendedTriggerProperties` added | A default interface member returning `false`, so an existing persistence delegate is unaffected; implementing it puts a trigger's schedule in the same round trip as its row — see [Batched trigger fire](#batched-trigger-fire) |
| `IDriverDelegate.UpdateFiredTrigger` removed | `ApplyTriggerFired` writes the fired-trigger row as one command of its batch, and nothing else called it; an override of it would have stopped taking effect silently — see [Batched trigger fire](#batched-trigger-fire) |
| `IDriverDelegate` gains six set-shaped members | `InsertFiredTriggers`, `SelectStoredTriggerHeaders`, a key-set `UpdateTriggerStatesFromOtherStates`, `SelectTriggerKeysForJobs`, `SelectPausedJobGroups` and `InsertPausedJobGroups`. All are default interface members whose default is the per-key loop the store used to make, so an existing delegate is unaffected — see [Batched trigger acquisition, and the sets pause and resume work on](#batched-trigger-acquisition-and-the-sets-pause-and-resume-work-on) |
| `IDriverDelegate.FiltersAcquisitionJobTypeExclusions` added | A default interface member answering `false`; `StdAdoDelegate` answers `true`. A delegate that says it filters the excluded job types itself is taken at its word, and the store's backstop is skipped — see [Batched trigger acquisition, and the sets pause and resume work on](#batched-trigger-acquisition-and-the-sets-pause-and-resume-work-on) |
| `StdAdoDelegate`'s column probes removed | The three `Has*Column` properties, the three `Supports*Column` probes and `VerifyTriggersTableReachable`. The columns they probed for are required on 4.x, so the schema migration replaces them — see [The optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone) |
| `GetSelectNextTriggerToAcquireWith*Sql` removed | The `…WithExecutionGroupSql`, `…WithPreferredNodeSql` and `…WithPreferredNodeOnlySql` hooks, on `StdAdoDelegate` and all six dialect delegates. One statement covers every case now, so a dialect delegate keeps only its `GetSelectNextTriggerToAcquireSql` override — see [The three extra acquisition SQL hooks went with them](#the-three-extra-acquisition-sql-hooks-went-with-them) |
| `StdAdoDelegate.GetSelectNextTriggerToAcquireSql(int maxCount)` | `GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)`; a custom dialect override passes the whole shape to `base`, so the next acquisition dimension is a property on the record rather than another parameter |
| `StdAdoDelegate.GetRowLimit(int count)` added | Where a dialect's row-limiting clause goes, said once for both statements that carry one, as a `SqlRowLimit` rather than an edit to finished SQL. Five of the six dialect delegates stopped overriding `GetSelectNextTriggerToAcquireSql` and `GetSelectMisfiredTriggersToRecoverSql` entirely; both hooks stay `protected virtual` and an override that splices its own clause still works — see [Row limiting is a slot, not a splice](#row-limiting-is-a-slot-not-a-splice) |
| `Quartz.Impl.AdoJobStore.SqlRowLimit` added | A `readonly record struct` naming the three placements a row limit can take — `InProjection`, `AtStatementEnd`, `InEnclosingSelect` — and `Unlimited` for a database that cannot limit rows |
| `IDbConnectionManager` / `DbConnectionManager` removed | The container is the provider registry, keyed by scheduler name; register a provider with `UseConnectionProvider` — see [The connection manager is gone](#the-connection-manager-is-gone) |
| `AdoJobStoreOptions.TxIsolationLevelSerializable` is `TransactionIsolationLevel` | An `IsolationLevel?` rather than a `bool`, so `Snapshot` and the rest are expressible. The legacy key still translates — see [The isolation level is an isolation level](#the-isolation-level-is-an-isolation-level) |
| `AdoJobStoreOptions.CommandTimeout` added | Bounds every statement the store issues, the lock handler's included; it reaches them through `DriverDelegateContext.CommandTimeout` and `LockHandlerContext.CommandTimeout`. Unset keeps each provider's own default, so nothing changes for a store that does not set it. 3.x had no way to say this at all — there was no `quartz.*` key for it, so nothing needs translating |
| `DbMetadataFactory` is internal | Every implementation was already internal and no public member accepted one; describe a driver through `UseGenericDatabase`'s metadata factory |
| `DbProvider.PropertyDbProvider` and `.DbProviderResourceName` removed | Two `protected const`s nothing read, left over from the process-wide provider registry |
| `SimplePropertiesTriggerPersistenceDelegateBase`'s four SQL statements are private | `SelectSimplePropsTrigger`, `DeleteSimplePropsTrigger`, `InsertSimplePropsTrigger` and `UpdateSimplePropsTrigger` name every column the base class binds, so replacing one could not work. The column name constants stay `protected` — they are the schema contract — and `TableSimplePropertiesTriggers` is `protected` still, now aliasing the `public` `AdoConstants.TableSimplePropertiesTriggers` that schema validation walks |
| `RAMJobStore` is `sealed` and has no `virtual` members | Wrap it in a store deriving from the new `DelegatingJobStore` instead of deriving from it — see [`RAMJobStore` is sealed](#ramjobstore-is-sealed) |
| `Quartz.Impl.DelegatingJobStore` added | Forwards every `IJobStore` member to a wrapped store, each one `virtual`, so a decorating store overrides only what it changes — see [`DelegatingJobStore` decorates a store](#delegatingjobstore-decorates-a-store) |
| `HostnameInstanceIdGenerator` is `HostNameInstanceIdGenerator` | Casing matched to `HostNameBasedIdGenerator`. The type is internal; a `quartz.scheduler.instanceIdGenerator.type` still naming the old spelling resolves, with a warning |
| `AddJob<T>` and `ScheduleJob<T>` register the job type | Scoped, with `TryAdd`, so an unresolvable job fails `ValidateOnBuild` instead of at fire time — see [`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container) |
| A registered job's constructor may not take a scheduler's parts | `IScheduler`, `ISchedulerFactory`, `IJobStore`, `IThreadPool` and `IOptions<QuartzSchedulerOptions>` belong to one scheduler, and the container resolves them unkeyed, so a registered job taking one is refused when the host starts rather than handed the default scheduler's. Read the scheduler from `IJobExecutionContext.Scheduler`, or register the job with `AddJobType<T>(factory)` and resolve the part by key there — see [`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container) |
| The `JobKey`-taking `AddJob` overloads removed | Identity is set inside the configurator with `WithIdentity` — see [One shape per registration method](#one-shape-per-registration-method) |
| `IServiceCollectionQuartzConfigurator` is `IQuartzBuilder` | And the `AddQuartz` overloads taking an `(configurator, IServiceProvider)` callback are gone; use the `(IServiceProvider, configurator)` shape of `AddJob` / `AddTrigger` / `ScheduleJob` |
| DI `AddCalendar` takes `AddCalendarOptions` | The two adjacent bools are gone, and `calendarName` is `name` |
| `AddPlugin` shapes aligned to the listener trio | The name is an optional trailing argument on all three — see [Plugins are registered like listeners](#plugins-are-registered-like-listeners) |
| `AddQuartzSchedulers(IConfiguration, …)` added | `AddQuartz(configuration)` no longer fans out over a `Schedulers` section; it throws and points here |
| `QuartzHostedService` takes an `IServiceProvider` and an `IOptionsMonitor` | It resolves every scheduler in the container when the host starts — see [The hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler) |
| `AddQuartzHostedService(string schedulerName, …)` added | `QuartzHostedServiceOptions` are named options; the unnamed call still configures every scheduler |
| `QuartzHostedServiceOptions.AutoStart` added | Defaults to `true`, so nothing changes for an existing application. `false` has the scheduler resolved, initialized and bound but left in `Created` for the application to start, and the health check reports it degraded rather than unhealthy — see [A hosted scheduler can be started by the application](#a-hosted-scheduler-can-be-started-by-the-application) |
| `IQuartzBuilder.AddHttpApi` / `MapQuartzApi` renamed | `services.AddQuartzHttpApi()` / `MapQuartzHttpApi`, the first of them on the service collection rather than a scheduler's builder; `AddQuartzHealthChecks` gained an `IQuartzBuilder` overload, which it keeps because a health check really is one scheduler's |
| The health check is added on `IHealthChecksBuilder` | `AddHealthChecks().AddQuartz()` / `.AddQuartz("reporting")`, so it composes with an application's other checks. `AddQuartzHealthChecks()` is shorthand for the first — see [The ASP.NET Core methods say Quartz once](#the-asp-net-core-methods-say-quartz-once) |
| The health check ships in `Quartz` | It was in `Quartz.AspNetCore` through `4.0.0-alpha.3`, whose `FrameworkReference` a worker on a `dotnet/runtime` image cannot satisfy. No call site changes; drop the package reference if the check was the only reason for it — see [The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore) |
| `QuartzHealthCheckOptions` goes through the options pipeline | It was constructed and read inside the registration call, so `services.Configure<QuartzHealthCheckOptions>(...)` did nothing. `Name` is nullable and defaults to the scheduler's check name |
| `QuartzHealthCheckOptions.Tags` is a get-only `List<string>` | It was a settable `IReadOnlyCollection<string>`. Add to it — `options.Tags.AddRange(["ready", "live"])` — rather than assigning; one `configure` callback can no longer discard the tags another added — see [A shipped component is configured through its options type, and only there](#a-shipped-component-is-configured-through-its-options-type-and-only-there) |
| `QuartzSchedulerBuilder.Build()` returns `StandaloneSchedulerFactory` | It is an `ISchedulerFactory` that is also `IAsyncDisposable` and `IDisposable`, so disposing the container needs no cast |
| `JobBuilder<TJob>.Key` is public | Reports the identity the builder was given, or `null` when none was set, so a trigger registered alongside a job can agree with it |
| `ISchedulerProxyFactory` and `HttpSchedulerProxyFactory` removed | Nothing read them — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `quartz.scheduler.proxy*` and `quartz.scheduler.exporter*` are rejected | They were whitelisted but read by nobody; the exception names the replacement |
| `[Serializable]` removed from 30 types | It stays only on the types a job store blob can be made of — see [`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it) |
| The `(SerializationInfo, StreamingContext)` constructors removed | On `SchedulerException`, `JobPersistenceException`, `SchedulerConfigException` and `HttpClientException`. `BinaryFormatter` was their only caller, and the base class library's equivalent is obsolete |
| `[Serializable]` removed from `JobExecutionContextImpl` and `SchedulerContext` | Neither is persisted; `SchedulerContext` also lost its private deserialization constructor |
| `JobExecutionProcessException` and `JobInstantiationException` moved to `Quartz` | `Quartz.Core` is internal now — see [The two exceptions moved out of `Quartz.Core`](#the-two-exceptions-moved-out-of-quartz-core) |
| `ExecutionLimits` split into a builder and a snapshot | `ExecutionLimitsBuilder` mutates, `ExecutionLimits` is immutable and is no longer an `IReadOnlyDictionary<string, int?>` — see [Execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen) |
| `IQuartzBuilder.UseExecutionLimits` takes an `Action<ExecutionLimitsBuilder>` | The lambda body is unchanged |
| `TriggerAcquisitionRequest.ExecutionLimits` and `TriggerAcquisitionCriteria.ExecutionLimits` are `ExecutionLimits?` | They were `IReadOnlyDictionary<string, int?>?`; spend the slots through `CreateSlots()` — see [A job store is handed the limits, and a way to spend them](#a-job-store-is-handed-the-limits-and-a-way-to-spend-them) |
| `Quartz.ExecutionSlots`, `Quartz.ExecutionGroupLimit` and `Quartz.ExecutionGroupScope` added | The slot-counting rule, one scope's entry in a snapshot, and the typed default/other/named scope those entries carry — see [Execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen) |
| `Quartz.MonthDay` added | The month-and-day value `AnnualCalendar` excludes — see [Excluded days are a read-only set](#excluded-days-are-a-read-only-set) |
| `ICancellableJobExecutionContext` removed | Interruption is `IScheduler.Interrupt` / `InterruptFireInstance` to request and `IJobExecutionContext.CancellationToken` to observe — see [Interruption has two names, not three](#interruption-has-two-names-not-three) |
| `Quartz.Diagnostics.IJobDiagnosticData` removed | It was the payload contract of the `DiagnosticSource` events `Quartz.OpenTracing` consumed. Both the package and the events are gone; job execution is on `Activity` through `QuartzActivitySource`, and `IJobExecutionContext` is what a listener reads |
| `CronExpression.Clone()` removed | The type is sealed and immutable, so a copy was an allocation and nothing else — reuse the instance. See [`CronExpression` is immutable](#cronexpression-is-immutable) |
| `IJobExecutionContext.Put` / `.Get` removed | The volatile per-execution side-map had no reader in Quartz and no way for its two ends to find each other but a shared string. A job talking to its listeners sets `Result`, whose type the two agree on between themselves; state that must survive the execution belongs in `JobDataMap`. `JobExecutionContextImpl` is public, so this is a class-member removal too |
| `JobDataMap`'s sixty typed accessors removed | One set of extension members does the same job — see [`JobDataMap`'s typed accessors are extension members](#jobdatamap-s-typed-accessors-are-extension-members) |
| `Quartz.AspNetCore.AddQuartzServer` removed | `AddQuartzHostedService` starts the scheduler and `AddQuartzHealthChecks` registers the check — see [`AddQuartzServer` is `AddQuartzHostedService`](#addquartzserver-is-addquartzhostedservice) |
| `ISchedulerFactory.GetScheduler(name)` is `LookupScheduler(name)` | Two members named `GetScheduler` differed only in nullability. `GetScheduler()` builds this factory's scheduler and cannot return null; `LookupScheduler(name)` looks one up in the container's repository and can, which is what the verb now says. `Lookup` matches `ISchedulerRepository.Lookup` |
| `TriggerUtils` became `Quartz.Extensibility.TriggerFireTimes` | A fire-time calculator over `IOperableTrigger`, named for what it computes; the three `Compute*` methods shortened with it — see [`TriggerUtils` became `TriggerFireTimes`](#triggerutils-became-triggerfiretimes) |
| `TimeZoneUtil` became `Quartz.TimeZones` | `FindTimeZoneById` — now `FindById` — and the wall-clock `GetUtcOffset` are scheduling API, not utilities — see [`TimeZoneUtil` became `Quartz.TimeZones`](#timezoneutil-became-quartz-timezones) |
| `Quartz.Util.ObjectExtensions` is internal | `AssemblyQualifiedNameWithoutVersion()` is how Quartz spells a type name into a blob or onto the wire, not a general-purpose helper |
| `Quartz.Diagnostics.ActivityOptions` is `ActivityTags` | It holds `Activity` tag names, not options, and `*Options` names an options type everywhere else. It replaced 3.x's `DiagnosticHeaders`; the constant names are unchanged, but their **values** are all `quartz.*` now — see [Job execution metrics](#job-execution-metrics) |
| `DBSemaphore` is `DbLockHandler` | The last `DB` spelling left in the ADO.NET surface, and the noun the whole family settled on — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers). The type is abstract and is never named in configuration |
| `StartingDailyAt` / `EndingDailyAt` take a `timeOfDay` | The parameter was `timeOfDayUtc`, and the value is wall-clock in the trigger's time zone rather than UTC — the property it sets, `StartTimeOfDay`, never claimed otherwise. `DailyTimeIntervalTriggerImpl`'s five constructors say `startTimeOfDay` / `endTimeOfDay` for the same reason |
| `ITriggerSerializer.TriggerTypeForJson` is `TriggerTypeName` | `ICalendarSerializer.CalendarTypeName` names the same concept; both JSON serializers changed |
| `IDriverDelegate.SelectNumTriggersForJob` is `CountTriggersForJob` | Matching `CountMisfiredTriggersInState`, and spelling out the last `Num` |
| `SimpleTriggerImpl.ComputeNumTimesFiredBetween` is `ComputeNumberOfTimesFiredBetween` | As above |
| `TriggerAcquireResult.JobType` is `JobTypeName` | It holds `JOB_CLASS_NAME` — a type name, the same thing `JobHeader.JobTypeName` carries — and was documented as a discriminator, which it is not. `TriggerHeader.TriggerType` really is a discriminator and keeps its name |
| Parameter names spelled out on the ADO.NET surface | `IDriverDelegate.SelectJobDetail`'s `classLoadHelper` is `loadHelper` (the implementation already called it that, so named arguments disagreed with the interface); `ts` is `misfireTime`; `AdoJobStoreBase.ReleaseLock`'s `doIt` is `shouldRelease`; `StdAdoDelegate.AddTriggerPersistenceDelegate`'s `del` is `persistenceDelegate`; `TriggerPropertyBundle`'s `sb` is `scheduleBuilder`; `CronTriggerImpl.WillFireOn`'s `test` is `timeUtc`; `TriggerFireTimes.Compute`'s `numTimes` is `numberOfTimes` |

## Appendix: what happened to a name

The guide above is organised by topic, which is the right shape for reading it and the wrong shape
for the question a broken build actually asks — *what happened to this one type?* This appendix is
the index for that question, and it links back to the section that explains each entry rather than
explaining it a second time.

It is derived mechanically, by diffing the public API baselines both branches keep under
`src/Quartz.Tests.Unit/Verify/` and `src/Quartz.Tests.AspNetCore/Verify/`, so it names **every**
public type 3.x had and 4.0 does not — all 98, across every package — rather than the ones that came
to mind. If you want the raw delta for one package rather than this summary, `git diff` its baseline
across the two branches; package boundaries moved, so match the files up with this first:

| 3.x baseline | 4.x baseline |
|---|---|
| `PublicApiTest_Quartz` | `PublicApiTest_Quartz` — which also absorbs DI, Hosting and SystemTextJson |
| `PublicApiTest_Quartz.Extensions.DependencyInjection` | folded into `Quartz` (`Quartz.Configuration`) |
| `PublicApiTest_Quartz.Extensions.Hosting` | folded into `Quartz` (`src/Quartz/Hosting/`) |
| `PublicApiTest_Quartz.Serialization.SystemTextJson` | folded into `Quartz` (`SystemTextJsonObjectSerializer`) |
| `PublicApiTest_Quartz.Serialization.Json` | `PublicApiTest_Quartz.Serialization.Newtonsoft` |
| `Quartz.Jobs`, `Quartz.Plugins`, `Quartz.Plugins.TimeZoneConverter`, `Quartz.Extensions.Redis`, `Quartz.AspNetCore`, `Quartz.Dashboard` | same name on both sides |
| `PublicApiTest_Quartz.OpenTracing` | dropped; there is no 4.x package |
| (no 3.x baseline — its ancient `OpenTelemetry` dependency fails restore there) | dropped; subscribe to the `Quartz` activity source directly |
| — | `PublicApiTest_Quartz.HttpClient` — new in 4.x |

3.x snapshots `net10.0` only, so its `net472` and `REMOTING` surface never appears in that diff.

Two whole-surface changes are deliberately left out, because repeating them per type would bury
everything else: `Task` became [`ValueTask`](#tasks-changed-to-valuetask) on nearly every member, and
the namespaces moved — [`Quartz.Spi` is `Quartz.Extensibility` and `Quartz.Simpl` is
`Quartz.Impl`](#quartz-spi-and-quartz-simpl-were-renamed), and three packages folded into `Quartz`
(see [Package Changes](#package-changes)). **A type that only changed namespace keeps its name and is
not listed here**, so if you cannot find one below, that is the first thing to check.

*Internal* below means the type is still there and still doing its job — it is simply no longer part
of the contract. If you were deriving from one of them, please
[open an issue](https://github.com/quartznet/quartznet/issues) rather than working around it.

Both tables are ordered by the name you would have typed — the type's own name, ignoring its
namespace, and `Type.Member` for the second one.

### Types that were removed, internalized or renamed

| 3.x type | What happened | What to use instead |
|---|---|---|
| `Quartz.Impl.AdoJobStore.AdoJobStoreBase` (3.x `JobStoreSupport`) | Internal | `IJobStore` for a store of your own, `DelegatingJobStore` to wrap one, `IDriverDelegate` for a database with no shipped dialect — see [The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) |
| `Quartz.Impl.AdoJobStore.AdoJobStoreDependencies` | Internal | Nothing; it existed so a derived store could chain one constructor argument, and there is no derived store — as above |
| `Quartz.Impl.AdoJobStore.AdoJobStoreUtil` | Internal | Nothing; it built statement text, which is not a contract — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.AdoProviderExtensions` | Renamed `PersistentStoreBuilderExtensions` | Same `Use*` methods, extending `IPersistentStoreBuilder` instead of `SchedulerBuilder.PersistentStoreOptions`. Two of them swapped meaning — see [The SQLite extension methods swapped names](#the-sqlite-extension-methods-swapped-names) |
| `Quartz.SchedulerBuilder.AdoProviderOptions` | Removed | `DataSourceOptions` — see [A data source is defined, referred to, or handed over](#a-data-source-is-defined-referred-to-or-handed-over) |
| `Quartz.AdoProviderOptionsExtensions` | Removed | It held only `UseDataSourceConnectionProvider()`; set `DataSourceOptions.UseRegisteredDataSource` — see [`AddDataSourceProvider()` went with it](#adddatasourceprovider-went-with-it) |
| `Quartz.Impl.AdoJobStore.AdoUtil` | Internal, with `IAdoUtil` | Nothing; parameter binding is not an extension point — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Simpl.BinaryObjectSerializer` | Removed | `SystemTextJsonObjectSerializer` or `NewtonsoftJsonObjectSerializer`; there is no binary serializer, because `BinaryFormatter` throws on .NET 9 — see [`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it) |
| `Quartz.Listener.BroadcastJobListener` | Removed | Register the listeners individually; the listener manager already notifies every one of them — see [The broadcast listeners are gone](#the-broadcast-listeners-are-gone) |
| `Quartz.Listener.BroadcastSchedulerListener` | Removed | As above |
| `Quartz.Listener.BroadcastTriggerListener` | Removed | As above |
| `Quartz.CalendarIntervalTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.SchedulerBuilder.ClusterOptions` | Removed | `ClusteringOptions` — see [Clustering is configured in one place](#clustering-is-configured-in-one-place) |
| `Quartz.Impl.AdoJobStore.ExternalTransactionJobStore` (3.x `JobStoreCMT`) | Internal | Unchanged as a configuration string; `quartz.jobStore.type` still names it — see [The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) |
| `Quartz.Impl.AdoJobStore.LocalTransactionJobStore` (3.x `JobStoreTX`) | Internal | As above |
| `Quartz.Impl.AdoJobStore.RecoverMisfiredJobsResult` | Internal | Nothing; it was a `protected` method's return type |
| `Quartz.Impl.AdoJobStore.Common.ConfigurationBasedDbMetadataFactory` | Internal | The metadata factory on `UseGenericDatabase` |
| `Quartz.CronScheduleTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.DailyTimeIntervalTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Util.DataReaderExtensions` | Internal | No replacement; they were `IDataReader` conveniences Quartz used on its own reads |
| `Quartz.Util.DBConnectionManager` | Removed | Register a provider with `UseConnectionProvider`, and resolve `IDbProvider` from the container; `.Instance` is gone — see [The connection manager is gone](#the-connection-manager-is-gone) |
| `Quartz.Impl.AdoJobStore.Common.DbMetadataFactory` | Internal | The metadata factory on `UseGenericDatabase` |
| `Quartz.Impl.AdoJobStore.DBSemaphore` | Renamed `DbLockHandler` | Same abstract base, still public — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| `Quartz.Simpl.DedicatedThreadPool` | Internal | `IQuartzBuilder.UseThreadPool(IThreadPool)` for a pool of your own — see [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `Quartz.Logging.DiagnosticHeaders` | Renamed `Quartz.Diagnostics.ActivityTags` | The constant names are unchanged; every value is `quartz.*` now — see [Job execution metrics](#job-execution-metrics) |
| `Quartz.Util.DictionaryExtensions` | Removed | No replacement — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Impl.DirectSchedulerFactory` | Removed | `QuartzSchedulerBuilder`, with `UseThreadPool(IThreadPool)` / `UseJobStore(IJobStore)` for pre-built parts — see [Removed](#removed) |
| `Quartz.Impl.AdoJobStore.Common.EmbeddedAssemblyResourceDbMetadataFactory` | Internal | The metadata factory on `UseGenericDatabase` |
| `Quartz.Util.FileUtil` | Internal | No replacement; it resolved a path relative to the base directory |
| `Quartz.Simpl.HostnameInstanceIdGenerator` | Renamed `HostNameInstanceIdGenerator`, and internal | A `quartz.scheduler.instanceIdGenerator.type` naming the old spelling still resolves, with a warning; in code, register your own `IInstanceIdGenerator` — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.ICancellableJobExecutionContext` | Removed | `IScheduler.Interrupt` to request, `IJobExecutionContext.CancellationToken` to observe — see [Interruption has two names, not three](#interruption-has-two-names-not-three) |
| `Quartz.IDashboardAuthorizationFilter` | Removed | `QuartzDashboardOptions.AuthorizationPolicy`; nothing ever invoked the filter — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Logging.IJobDiagnosticData` | Removed | `IJobExecutionContext`, read from a listener; the `DiagnosticSource` events it was the payload of are gone — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Core.IJobRunShellFactory` | Internal | No replacement; how a fire is wrapped is not a contract |
| `Quartz.IJobWrapper` | Removed | Per-fire state rides in `JobScope.State` — see [The job factory hands out a scope](#the-job-factory-hands-out-a-scope) |
| `Quartz.Logging.ILogProvider` | Removed with LibLog | `ILoggerFactory` — see [Logging](#logging) |
| `Quartz.SchedulerBuilder.InMemoryStoreOptions` | Removed | `InMemoryJobStoreOptions`, through `UseInMemoryStore(configure)` |
| `Quartz.Dashboard.Services.InProcessQuartzApiClient` | Internal | Resolve `IQuartzApiClient` — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Simpl.InternalTriggerState` | Removed | `Quartz.Extensibility.StoredTriggerState`, the stored-state vocabulary every store now shares — see [Trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate) |
| `Quartz.IPropertyConfigurationRoot` | Removed | Typed options — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.Impl.AdoJobStore.ISemaphore` | Renamed `ILockHandler` | `ObtainLock` is `AcquireLock`; `ReleaseLock` and `RequiresConnection` are unchanged — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| `Quartz.Impl.AdoJobStore.ITablePrefixAware` | Removed | `ILockHandler.Initialize(LockHandlerContext)` — see [A lock handler is told which scheduler it locks for](#a-lock-handler-is-told-which-scheduler-it-locks-for) |
| `Quartz.IPropertyConfigurer` | Removed | Typed options — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.IPropertySetter` | Removed | Typed options — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.Dashboard.Services.IQuartzApiClientExecutionLimits` | Removed | `IQuartzApiClient`, which carries `GetExecutionLimits` itself |
| `Quartz.Simpl.IRemotableQuartzScheduler` | Removed | Nothing; .NET Remoting is not supported — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Spi.IRemotableSchedulerProxyFactory` | Removed | Nothing; `Quartz.HttpClient` talks to a remote scheduler over HTTP — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Spi.ISchedulerExporter` | Removed | Nothing; `AddQuartzHttpApi` / `MapQuartzHttpApi` serve a scheduler over HTTP — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.IServiceCollectionQuartzConfigurator` | Renamed `IQuartzBuilder` | The same members, on one interface shared with the standalone builder — see [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder) |
| `Quartz.Spi.ITypeLoadHelper` | Renamed `Quartz.Extensibility.ITypeLoader` | The last `*Helper` left in the public surface; the builder method `UseTypeLoader<T>()` already had the new spelling. `Initialize()` is gone; `LoadType` is the whole interface. `AdoJobStoreBase.TypeLoadHelper` and `DriverDelegateContext.TypeLoadHelper` are `TypeLoader` to match |
| `Quartz.Impl.JobDetailImpl` | Internal | `JobBuilder.Create<TJob>()`; read an `IJobDetail` |
| `Quartz.JobFactoryOptions` | Kept, but emptied and refilled | Its two 3.x properties — `AllowDefaultConstructor` and `CreateScope` — were already `[Obsolete]` no-ops and are gone. The type is `sealed` and carries `ConfigureScope`, the per-scheduler hook for a scope Quartz opens — see [The job factory hands out a scope](#the-job-factory-hands-out-a-scope) |
| `Quartz.Plugin.Interrupt.JobInterruptMonitorPlugin` | Removed | `q.AddJobTimeout(TimeSpan)` and `[JobTimeout("hh:mm:ss")]`, both in the core package. Its `JobDataMapKeyAutoInterruptable` and `JobDataMapKeyMaxRunTime` constants go with it — see [`JobInterruptMonitorPlugin` is retired; a job timeout is middleware](#jobinterruptmonitorplugin-is-retired-a-job-timeout-is-middleware) |
| `Quartz.Core.JobRunShell` | Internal | No replacement; use `IJobListener` to observe a fire |
| `Quartz.Impl.AdoJobStore.JobStoreCMT` | Renamed `ExternalTransactionJobStore` | The old spelling still resolves in configuration, with a warning — see [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use) |
| `Quartz.Impl.AdoJobStore.JobStoreTX` | Renamed `LocalTransactionJobStore` | As above — see [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use) |
| `Quartz.Impl.AdoJobStore.JobStoreSupport` | Renamed `AdoJobStoreBase` | Abstract, never a configuration string, so no fallback is needed; a derived store updates its base list — see [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use) |
| `Quartz.Impl.Triggers.AbstractTrigger` | Renamed `TriggerBase` | Abstract, so never a `$type` value in stored JSON; the five concrete `*TriggerImpl` names are unchanged — see [TriggerBase Property Removals](#triggerbase-property-removals) for the rename and its one binary-blob caveat |
| `Quartz.Impl.AdoJobStore.SimplePropertiesTriggerPersistenceDelegateSupport` | Renamed `SimplePropertiesTriggerPersistenceDelegateBase` | As above |
| `Quartz.Simpl.JsonObjectSerializer` | Renamed `Quartz.Impl.NewtonsoftJsonObjectSerializer`, still in the `Quartz.Serialization.Newtonsoft` package | `UseNewtonsoftJsonSerializer()` registers it. Spelled into `quartz.serializer.type` it is `Quartz.Impl.NewtonsoftJsonObjectSerializer, Quartz.Serialization.Newtonsoft` — see [JSON Serialization](#json-serialization) |
| `Quartz.JsonSchedulingOptions` | Merged into `FileSchedulingOptions` | It was byte-for-byte identical to `XmlSchedulingOptions` — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.JsonSerializerOptions` | Removed | `UseNewtonsoftJsonSerializer`'s callback hands you the `NewtonsoftJsonSerializerRegistry` itself, with `registerTriggerConverters` as a parameter of the method — see [Custom trigger and calendar serializers are no longer static](#custom-trigger-and-calendar-serializers-are-no-longer-static) |
| `Quartz.Logging.LogProviders.LibLogException` | Removed with LibLog | No replacement — see [Logging](#logging) |
| `Quartz.Core.ListenerManagerImpl` | Internal | `IScheduler.ListenerManager`, typed `IListenerManager` — see [Listener API Changes](#listener-api-changes) |
| `Quartz.Logging.LogContext` | Removed with LibLog | `LogProvider.SetLogProvider(ILoggerFactory)` — see [Logging](#logging) |
| `Quartz.Logging.Logger` (delegate) | Removed with LibLog | `ILogger` — see [Logging](#logging) |
| `Quartz.Logging.LogLevel` | Removed with LibLog | `Microsoft.Extensions.Logging.LogLevel` — see [Logging](#logging) |
| `Quartz.Util.ObjectExtensions` | Internal | No replacement — see [Other Breaking Changes](#other-breaking-changes) |
| `Quartz.Util.ObjectUtils` | Removed | No replacement; it set properties reflectively from strings, which typed options removed the need for — see [Code-first configuration is typed](#code-first-configuration-is-typed) |
| `Quartz.SchedulerBuilder.PersistentStoreOptions` | Removed | `IPersistentStoreBuilder`, through `UsePersistentStore(configure)` |
| `Quartz.PropertiesHolder` | Removed | Typed options — see [Removed](#removed) |
| `Quartz.Util.PropertiesParser` | Internal | No replacement; `QuartzPropertyBridge` is the only reader of flat `quartz.*` keys now — see [Flat keys still work](#flat-keys-still-work) |
| `Quartz.PropertiesSetter` | Removed | Typed options — see [Removed](#removed) |
| `Quartz.QuartzConfiguratorExecutionLimitsExtensions` | Removed | `IQuartzBuilder.UseExecutionLimits(Action<ExecutionLimitsBuilder>)` — see [Execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen) |
| `Quartz.OpenTracing.QuartzDiagnosticOptions` | Removed with its package | `AddSource(QuartzInstrumentation.ActivitySourceName)`; job execution is on `Activity` through `QuartzActivitySource` — see [Observability](packages/opentelemetry-integration.md) |
| `Quartz.Util.QuartzEnvironment` | Internal | `System.Environment`, or `IConfiguration` for settings |
| `Quartz.Core.QuartzRandom` | Internal | `System.Random` |
| `Quartz.Core.QuartzScheduler` | Internal | Resolve `IScheduler` or `ISchedulerFactory` — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Core.QuartzSchedulerResources` | Internal | `QuartzSchedulerOptions` — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Core.QuartzSchedulerThread` | Internal | No replacement; the scheduling loop is not an extension point |
| `Quartz.RecurrenceTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.RemoteScheduler` | Removed | `Quartz.HttpClient` — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Simpl.RemotingSchedulerProxyFactory` | Removed | As above — see [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.ScheduleBuilder<T>` | Removed | Implement `IScheduleBuilder` directly — see [`ScheduleBuilder<T>` is gone](#schedulebuilder-t-is-gone) |
| `Quartz.SchedulerBuilder` | Renamed `QuartzSchedulerBuilder` | And it implements `IQuartzBuilder`, so its configuration members return that — see [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder) |
| `Quartz.SchedulerExtensions` | Removed | Its three members are on `IScheduler` itself: `GetExecutionLimits`, `SetExecutionLimits`, `UpdateTriggerDetails` |
| `Quartz.SchedulerMetaData` | Renamed `SchedulerMetadata` | A `sealed record`, returned by `IScheduler.GetMetadata()` — see [`SchedulerMetadata` replaces `SchedulerMetaData`](#schedulermetadata-replaces-schedulermetadata) |
| `Quartz.SchedulerPluginConfigurationExtensions` | Removed | `IQuartzBuilder.AddPlugin<T>()` — see [Plugins are registered like listeners](#plugins-are-registered-like-listeners) |
| `Quartz.Core.SchedulerSignalerImpl` | Internal | Take `ISchedulerSignaler` through your constructor — see [SPI changes](#spi-changes) |
| `Quartz.Configuration.QuartzConfigurationHelper` | Internal | Hand the section to `AddQuartz(configuration)`, or to `QuartzSchedulerBuilder.UseConfiguration(configuration)` without a host — both flatten it themselves. `QuartzOptions.ToProperties()` stays for a caller that holds a `QuartzOptions`. This empties the public `Quartz.Configuration` namespace |
| `Quartz.ServiceCollectionExtensions` | Split into `Quartz.QuartzServiceCollectionExtensions` and `Quartz.QuartzBuilderExtensions` | Extension-form call sites are unaffected; only a static-form call (`ServiceCollectionExtensions.AddQuartz(services, …)`) has to change. `ServiceCollectionExtensions` is the most common helper-class name in .NET, and claiming it in `Quartz` gave CS0104 in any file that had one of its own and a `using Quartz;` |
| `Quartz.Simpl.SimpleInstanceIdGenerator` | Internal | It is still the default; register your own `IInstanceIdGenerator` to replace it |
| `Quartz.SimpleScheduleTriggerBuilderExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.AdoJobStore.SimpleSemaphore` | Internal, renamed `InProcessLockHandler` | It is the in-process lock the ADO.NET store falls back to when database locking is off; implement `ILockHandler` for a lock of your own — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| `Quartz.Xml.XMLSchedulingDataProcessor` | Internal, respelled `XmlSchedulingDataProcessor` | `UseXmlSchedulingConfiguration()` — the plugin *is* the supported entry point. The type's only constructor needed an `ITypeLoader`, whose every implementation is internal; `OverwriteExistingData = false` was reverted by any file carrying `<processing-directives>`; and `ProcessFile(fileName, systemId)` wanted an identifier from a specification Quartz no longer uses. With it goes the last public type in `Quartz.Xml` |
| `Quartz.Simpl.SimpleTypeLoadHelper` | Internal, renamed `SimpleTypeLoader` | Register your own `ITypeLoader`; a configuration string naming the old type still resolves, with a warning |
| `Quartz.Impl.AdoJobStore.StdAdoConstants` | Internal | `AdoConstants` for table, column and state names; statement text is not a contract — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Impl.AdoJobStore.StdRowLockSemaphore` | Renamed `SelectForUpdateLockHandler` | The old spelling still resolves in configuration, with a warning — see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| `Quartz.Impl.AdoJobStore.PostgreSQLRowLockSemaphore` | Renamed `PostgreSqlSelectForUpdateLockHandler` | As above |
| `Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore` | Renamed `UpdateRowLockHandler` | As above |
| `Quartz.Impl.AdoJobStore.UpdateLockRowSemaphoreMOT` | Renamed `SqlServerMemoryOptimizedUpdateRowLockHandler` | As above |
| `Quartz.Impl.StdJobRunShellFactory` | Internal | No replacement; see `IJobRunShellFactory` above |
| `Quartz.Impl.StdScheduler` | Internal | Resolve `IScheduler` — see [Sealed and Internalized Types](#sealed-and-internalized-types) |
| `Quartz.Impl.StdSchedulerFactory` | Removed, with all 47 constants | `QuartzSchedulerBuilder.Create().UseProperties(properties)` — see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone) for every constant and member |
| `Quartz.SchedulerBuilder.StoreOptions` | Removed | Nothing; it was the base of the two store option classes, which are now `InMemoryJobStoreOptions` and `IPersistentStoreBuilder` |
| `Quartz.Util.StringExtensions` | Internal | No replacement |
| `Quartz.Simpl.SystemPropertyInstanceIdGenerator` | Internal | `quartz.scheduler.instanceId = SYS_PROP` still selects it; in code, register your own `IInstanceIdGenerator` |
| `Quartz.SystemTime` | Removed | `TimeProvider` — see [SystemTime Replaced with TimeProvider](#systemtime-replaced-with-timeprovider) |
| `Quartz.TimeOfDay` | Removed | `TimeOnly` — see [`TimeOfDay` became `TimeOnly`](#timeofday-became-timeonly) |
| `Quartz.Util.TimeZoneUtil` | Renamed `Quartz.TimeZones` | `FindTimeZoneById` is `FindById`; `CustomResolver` is `AddResolver(...)`, whose `IDisposable` undoes the registration; the Mono-era `ConvertTime` / `GetUtcOffset(DateTimeOffset, TimeZoneInfo)` shims went internal, while the wall-clock `GetUtcOffset(DateTime, TimeZoneInfo)` stays public — see [`TimeZoneUtil` became `Quartz.TimeZones`](#timezoneutil-became-quartz-timezones) |
| `Quartz.TriggerExtensions` | Removed | `TriggerConfiguratorExtensions` — see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.AdoJobStore.TriggerStatus` | Removed | `StoredTriggerHeader`, returned by `IDriverDelegate.SelectTriggerHeader` — see [The driver delegate speaks in records](#the-driver-delegate-speaks-in-records) |
| `Quartz.TriggerTimeComparator` | Internal | No replacement; it ordered by next fire time, then priority descending, then key — write that inline if you need it |
| `Quartz.TriggerUtils` | Renamed `Quartz.Extensibility.TriggerFireTimes` | `ComputeFireTimes` is `Compute`, `ComputeFireTimesBetween` is `ComputeBetween` and `ComputeEndTimeToAllowParticularNumberOfFirings` is `ComputeEndTimeForCount`, with parameters and behavior unchanged — see [`TriggerUtils` became `TriggerFireTimes`](#triggerutils-became-triggerfiretimes) |
| `Quartz.Simpl.TriggerWrapper` | Internal | No replacement; it is `RAMJobStore`'s per-trigger state — see [`RAMJobStore` is sealed](#ramjobstore-is-sealed) |
| `Quartz.UnableToInterruptJobException` | Removed | Nothing throws it: interruption is cancellation, and every job receives the token — see [`UnableToInterruptJobException` is gone](#unabletointerruptjobexception-is-gone) |
| `Quartz.XmlSchedulingOptions` | Merged into `FileSchedulingOptions` | See [Other Breaking Changes](#other-breaking-changes) |

### Members that were removed

A member whose own type is listed above is not repeated here, and neither is one that went with a
sealing — a type that became `sealed` took its `protected` surface with it. What is left is the
removals on types that are still public and still open, which no section above names.

| 3.x member | What happened | What to use instead |
|---|---|---|
| `AbstractTrigger.CompareTo(ITrigger)` | Removed; neither `TriggerBase` nor `ITrigger` itself implements `IComparable<ITrigger>` any more | It compared keys — `trigger.Key.CompareTo(other.Key)`, or `triggers.OrderBy(t => t.Key)`, both of which sort properly now that the key types implement `IComparable<JobKey>` / `IComparable<TriggerKey>`. `List<ITrigger>.Sort()` and friends still compile and now throw — see [The trigger family interfaces are read models](#the-trigger-family-interfaces-are-read-models) |
| `AbstractTrigger.FullJobName` | Removed | `JobKey.ToString()`, alongside the rest in [TriggerBase Property Removals](#triggerbase-property-removals) |
| `AdoConstants.AliasColumnNextFireTime` | Removed | No replacement, and none is needed: it named a column alias (`ALIAS_NXT_FR_TM`) that no 4.x statement uses and the schema has never had a column for. It was a carryover from the Java acquisition sub-select, and a delegate that copied 3.x SQL is the only thing that could have named it |
| `new CronExpression(expr, hashKey)`, `new CronExpression(expr, hashSeed)` | Removed; they made `new CronExpression(expr, null)` ambiguous with the time-zone overload, so the null literal did not compile | `CronExpression.ParseWithHash(expr, hashKey)` / `ParseWithHash(expr, hashSeed)` — see [The hash key is a `Parse` argument, not a constructor overload](#the-hash-key-is-a-parse-argument-not-a-constructor-overload) |
| `CronExpression`'s `protected` constants, fields and parse hooks, and `OnDeserialization` | Gone with the type, which is `sealed` now and no longer implements `IDeserializationCallback` | No replacement; the parsed sets were never a contract — see [The parser is not a subclassing seam](#the-parser-is-not-a-subclassing-seam) |
| `CronExpression.GetExpressionSummary()`, `ICronTrigger.GetExpressionSummary()`, `CronTriggerImpl.GetExpressionSummary()` | Removed | No replacement. It dumped the parsed field sets in an undocumented format; `CronExpressionString` is the expression, and the parsed sets were never a supported view — see [The parser is not a subclassing seam](#the-parser-is-not-a-subclassing-seam) |
| `CronExpression.GetTimeBefore(t)` | Renamed | `GetPreviousValidTimeBefore(t)`, which reads as the pair of `GetNextValidTimeAfter` that it is |
| `CronExpression.IsValidExpression(expr)` | Removed | `CronExpression.TryParse(expr, out _)` — see [`CronExpression` parses without throwing](#cronexpression-parses-without-throwing-and-says-it-is-equatable) |
| `CronExpression.IsValidExpression(expr, hashKey)` | Removed | `CronExpression.TryParseWithHash(expr, hashKey, out _)` |
| `CronExpression.MaxYear` | Removed (a `public static readonly int`) | No replacement. It was `DateTime.Now.Year + 100`, computed once per process — see [`CronExpression` is immutable](#cronexpression-is-immutable) |
| `CronExpression.ValidateExpression(expr)` | Removed; its whole body was a construction it discarded | `CronExpression.Parse(expr)`, which hands back the expression it parsed |
| `CronExpression.ValidateExpression(expr, hashKey)` | Removed | `CronExpression.ParseWithHash(expr, hashKey)` |
| `CronTriggerImpl.GetTimeAfter(DateTimeOffset)` | Removed (it was `protected`) | `GetFireTimeAfter(DateTimeOffset?)`, or `CronExpression.GetNextValidTimeAfter` for the expression on its own |
| `CronTriggerImpl.GetTimeBefore(DateTimeOffset)` | Renamed (it is `protected`) | `GetPreviousValidTimeBefore(DateTimeOffset)`, renamed with the `CronExpression` member it forwards to |
| `CronTriggerImpl.YearToGiveupSchedulingAt` | Removed (a `protected const`) | No replacement; where the search stops is the expression's business |
| `DateBuilder.ValidateDayOfMonth`, `.ValidateHour`, `.ValidateMinute`, `.ValidateMonth`, `.ValidateSecond`, `.ValidateYear` | Removed | No replacement; the builder validates its own arguments, and it is `sealed` — see [`DateBuilder`'s static factories are gone](#datebuilder-s-static-factories-are-gone) |
| `DbProvider.CreateParameter()` | Removed | `CreateCommand().CreateParameter()` |
| `DbProvider.DbProviderSectionName`, `.GenerateValidProviderNamesInfo()` | Removed (`protected`) | No replacement; leftovers of the process-wide provider registry, like the two named in [Other Breaking Changes](#other-breaking-changes) |
| `DirtyFlagMap<TKey, TValue>`, `StringKeyDirtyFlagMap` | Internal | `JobDataMap` / `SchedulerContext` are self-contained; the typed accessors are extension members — see [JobDataMap and SchedulerContext stand alone](#jobdatamap-and-schedulercontext-stand-alone) |
| `DirtyFlagMap.Clone()` | Removed | Construct a new map from the old one |
| `DirtyFlagMap.Dirty`, `.ClearDirtyFlag()` | Internal | `SchedulerConstants.ForceJobDataMapDirty` forces a rewrite; clearing the flag from a job silently lost data |
| `DirtyFlagMap.EntrySet()` | Removed | `GetEnumerator()`, or `foreach` over the map |
| `DirtyFlagMap.KeySet()` | Removed | `Keys` |
| `DirtyFlagMap.Put()`, `.PutAll()` | Removed; both were `[Obsolete]` in 3.x | `map[key] = value`, in a loop for `PutAll` — see [JobDataMap and SchedulerContext stand alone](#jobdatamap-and-schedulercontext-stand-alone) |
| `DirtyFlagMap.WrappedMap` | Removed | No replacement; the map *is* the dictionary, and handing out the inner one let a caller write past the dirty flag |
| `IDriverDelegate.UpdateTriggerPreferredNode`, `StdAdoDelegate.UpdateTriggerPreferredNode` | Removed | `UpdateTriggerPreferredNodeConditional`, which is a compare-and-swap, or `IScheduler.UpdateTriggerDetails` from outside the store — see [The preferred node is a value](#the-preferred-node-is-a-value) |
| `InvalidConfigurationException()` | Removed; the type is `sealed` and keeps only `(string message)` | Say what was invalid — an exception with no message is one nobody can act on |
| `IListenerManager.AddJobListenerMatcher`, `.RemoveJobListenerMatcher`, `.SetJobListenerMatchers`, `.GetJobListenerMatchers`, and their `TriggerListener` twins | Removed | `AddJobListener(listener, params matchers)` / `AddTriggerListener(listener, params matchers)`, which is registration; registering the same name again replaces the listener and its matchers together — see [Matchers are given at registration](#matchers-are-given-at-registration) |
| `IObjectSerializer.Initialize()` | Removed | No replacement; a serializer builds what it needs on first use — see [Names that were normalized](#names-that-were-normalized) |
| `IScheduler.InStandbyMode` | Removed | `Status is SchedulerStatus.Created or SchedulerStatus.Standby or SchedulerStatus.ShuttingDown` — see [A scheduler's lifecycle is one value](#a-scheduler-s-lifecycle-is-one-value) |
| `IScheduler.IsShutdown` | Removed | `Status is SchedulerStatus.Shutdown` — see [A scheduler's lifecycle is one value](#a-scheduler-s-lifecycle-is-one-value) |
| `IScheduler.IsStarted` | Removed | `Status is not SchedulerStatus.Created` faithfully; `Status is SchedulerStatus.Running` if what you meant was "is running now", which it did not say — see [A scheduler's lifecycle is one value](#a-scheduler-s-lifecycle-is-one-value) |
| `IScheduler.JobFactory` (setter-only) | Removed, from `IScheduler`, `StdScheduler`, `DelegatingScheduler` and `HttpScheduler` | `IQuartzBuilder.UseJobFactory(IJobFactory)` or `UseJobFactory<T>()` where the scheduler is built; `ConfigureJobScope(...)` when all it did was seed the DI scope. A factory whose dependency does not exist yet resolves it in `CreateJob` — see [The factory is set where the scheduler is built](#the-factory-is-set-where-the-scheduler-is-built) |
| `JobBuilder.CreateForAsync<T>()` | Removed | `JobBuilder.Create<T>()`; every job has been asynchronous since 3.0 |
| `JobStoreSupport.calendarCache`, `.delegateType`, `.firstCheckIn` | Removed (`protected` fields) | No replacement; they are the base class's own bookkeeping |
| `JobStoreSupport.GetTriggerNames(conn, matcher, ct)` | Removed (`protected`) | The listing members became queries — see [Job store listings became queries](#job-store-listings-became-queries) |
| `LogProvider.IsDisabled` | Removed | No replacement; filter through the `ILoggerFactory` — see [Logging](#logging) |
| `LogProvider.SetCurrentLogProvider(ILogProvider)` | Removed with LibLog | `LogProvider.SetLogProvider(ILoggerFactory)` — see [Logging](#logging) |
| `QuartzDashboardOptions.ApiPath` | Removed | No replacement; it addressed the HTTP API the dashboard's remote client called, and that client is gone — see [The dashboard reads the schedulers in its own process](#the-dashboard-reads-the-schedulers-in-its-own-process). `QuartzHttpApiOptions.ApiPath`, which is where the API is served, is a different option and unchanged |
| `SchedulerMetadata.Started`, `.InStandbyMode`, `.Shutdown` | Removed | `Status`, a `required SchedulerStatus` — see [A scheduler's lifecycle is one value](#a-scheduler-s-lifecycle-is-one-value) |
| `SimplePropertiesTriggerPersistenceDelegateSupport.SchedNameLiteral`, and the same member on `DbLockHandler` | Removed; both were `[Obsolete]` in 3.x | No replacement; the scheduler name is a SQL parameter, not literal text |
| `StdAdoDelegate.GetStorableJobTypeName(Type)` | Removed (`protected`) | `new JobType(type).FullName`, which is the spelling the `JOB_CLASS_NAME` column holds |
| `StdAdoDelegate.SchedulerNameLiteral` | Removed; it was `[Obsolete]` in 3.x | No replacement; as above |
| `StringKeyDirtyFlagMap.GetKeys()` | Removed | `Keys` |
| `StringKeyDirtyFlagMap.GetNullableGuid()`, `.TryGetNullableGuid()` | Removed | `TryGetGuid(key, out var value)`, whose `false` says the same thing as a `null` did — see [`JobDataMap`'s typed accessors are extension members](#jobdatamap-s-typed-accessors-are-extension-members) |
| `StringKeyDirtyFlagMap.Put()` (eight overloads), `.PutAll()` | Removed; all were `[Obsolete]` in 3.x | `map[key] = value` |
| `TaskSchedulingThreadPool.ThreadPriority` | Removed | No replacement; work runs on a `TaskScheduler`, which has no thread to prioritise — see [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `ZeroSizeThreadPool.AvailableThreadCount` | Removed | `PoolSize`, which is `0` — the pool never had a thread to report |

### Types 4.0 added to the `Quartz` namespace

The two tables above answer "where did my type go". This one answers the opposite question, which a
build only asks once and then asks loudly: **CS0104, "ambiguous reference"**, because a name Quartz now
declares collides with one of yours under `using Quartz;`. Three packages folded into `Quartz` and the
namespace absorbed the matchers, `Key<T>` and the options types, so the namespace is much larger than it
was — and the errors it produces name the right members on the wrong type, which reads like your own code
having gone wrong.

Derived the same mechanical way as the tables above: types in the 4.x `Quartz` namespace that are in the
3.x `Quartz` namespace of none of `Quartz`, `Quartz.Extensions.DependencyInjection` or
`Quartz.Extensions.Hosting`. **Ninety-four names.** A project that also referenced
`Quartz.Serialization.SystemTextJson` on 3.x already had `JsonSerializationException`.

```text
AddCalendarOptions                          AddJobOptions                       AddTriggerOptions
AdoJobStoreOptions                          AndMatcher<T>                       CalendarIntervalTriggerMisfireInstruction
CalendarNameMatcher                         CalendarQuery                       ClusterNode
ClusterNodeState                            ClusteringOptions                   CronTriggerMisfireInstruction
DailyTimeIntervalTriggerMisfireInstruction  DataMapExtensions                   DataSourceOptions
EverythingMatcher<T>                        ExecutionGroupInFlight              ExecutionGroupLimit
ExecutionGroupScope                         ExecutionLimitScope                 ExecutionLimitsBuilder
ExecutionSlots                              FireInstance                        FireInstanceQuery
FireInstanceState                           GroupMatcher<T>                     IJob<T>
IJobConfigurator<T>                         IJobExecutionContextAccessor        IJobExecutionMiddleware
IPersistentStoreBuilder                     IQuartzBuilder                      ISchedulerRegistry
ITriggerConfigurator<T>                     InMemoryJobStoreOptions             JobBuilder<T>
JobDetailExtensions                         JobExecutionContextInputExtensions  JobExecutionDelegate
JobExecutionProcessException                JobGroup                            JobGroupQuery
JobHeader                                   JobInputBuilderExtensions           JobInstantiationException
JobQuery                                    JobType                             JsonSerializationException
Key<T>                                      KeyMatcher<T>                       Matchers
MonthDay                                    NameMatcher<T>                      NotMatcher<T>
OneOffJobOptions                            OrMatcher<T>                        PagedQuery
PagedResult<T>                              PersistentStoreBuilderExtensions    PreferredNode
QuartzBuilderExtensions                     QuartzHealthCheckExtensions         QuartzHealthCheckOptions
QuartzHostApplicationBuilderExtensions      QuartzSchedulerBuilder              QuartzSchedulerOptions
RecurrenceTriggerMisfireInstruction         RetryPolicy                         ScheduleJobOptions
SchedulerErrorContext                       SchedulerJobExtensions              SchedulerMetadata
SchedulerOrigin                             SchedulerQueryExtensions            SchedulerRegistration
SchedulerStatus                             SchedulingDataValidationException   SchemaProvisioning
ShutdownJobInterruption                     SimpleTriggerMisfireInstruction     StandaloneSchedulerFactory
StringMatcher<T>                            StringOperator                      SystemTextJsonConfigurationExtensions
ThreadPoolOptions                           TimeRange                           TimeZones
TriggerBuilder<T>                           TriggerConfiguratorExtensions       TriggerGroup
TriggerGroupQuery                           TriggerHeader                       TriggerQuery
TypeLoaderOptions
```

Most of those are unmistakably Quartz's. These are the ones a host application or an integration library
plausibly declares itself, which makes them the ones to check first:

| Name | Who else has one |
|---|---|
| `QuartzSchedulerOptions` | the known case — it shadowed MassTransit's own `QuartzSchedulerOptions`, producing six compile errors that named the right members on the wrong type. 3.x's equivalent was `Quartz.Core.QuartzSchedulerResources`, safely out of the way |
| `JsonSerializationException` | **Newtonsoft.Json declares this exact name**, so a file with both `using Quartz;` and `using Newtonsoft.Json;` is ambiguous |
| `RetryPolicy` | Polly, MassTransit, the Azure SDKs, and most in-house resilience code |
| `TimeRange`, `MonthDay` | ordinary domain vocabulary; NodaTime has `MonthDay` |
| `PagedResult<T>`, `PagedQuery` | near-universal in an API or repository layer |
| `Key<T>` | was `Quartz.Util.Key<T>`; now a one-word name at the top level |
| `Matchers`, `TimeZones` | one-word static helpers, previously `Quartz.Impl.Matchers` and `Quartz.Util.TimeZoneUtil` |
| `ThreadPoolOptions`, `DataSourceOptions` | common options-class names |
| `JobType`, `SchedulerStatus`, `ClusterNode`, `ClusterNodeState` | anything with a job or scheduler model of its own |
| `StringOperator`, `ExecutionSlots`, `ExecutionGroupScope`, `ExecutionLimitScope` | generic concurrency and matching vocabulary |

The fix is a using-alias in the affected file, which beats a namespace import and needs no change to
either library:

```csharp
using QuartzSchedulerOptions = Acme.Bus.QuartzSchedulerOptions;
```

One collision went away: 3.x's `Quartz.ServiceCollectionExtensions` — the most common helper-class name in
.NET — is not in the `Quartz` namespace on 4.x. Its members live on `QuartzServiceCollectionExtensions`
and `QuartzBuilderExtensions`, and since both are extension methods, only a static-form call has to change.
