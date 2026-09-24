---

title: Migration Guide
---

*This document outlines changes needed when upgrading from Quartz.NET 3.x to 4.x. You should also check [the release notes](https://github.com/quartznet/quartznet/releases) for each version.*

::: tip
If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/)
:::

## Start here

| You are upgrading | Read first |
|---|---|
| A running 3.x deployment | [Upgrading a running deployment](#upgrading-a-running-deployment). Two of its seven steps happen while you are still on 3.x |
| An application's code from 3.x | [Package Changes](#package-changes): the first error a mixed 3.x/4.x project shows is a package problem. Then [The road from 3.x, phase by phase](#the-road-from-3-x-phase-by-phase) |
| An F# application | [Upgrading an F# project](#upgrading-an-f-project) first. F# reports the same upgrade as more errors than it has causes |
| From a 4.0 alpha or beta | [Appendix: if you ran a 4.0 pre-release](#appendix-if-you-ran-a-4-0-pre-release) |
| From 4.1 | [Upgrading from 4.1 to 4.2](#upgrading-from-4-1-to-4-2). It has the first database migration since 4.0 |
| From 4.0 | [Upgrading from 4.0 to 4.1](#upgrading-from-4-0-to-4-1), then 4.1 to 4.2 |
| Nothing: you are starting a new project | The [quick start](quick-start.md), then [the tutorial](tutorial/) |

The compiler finds most of the 3.x → 4.0 work.

## Upgrading from 4.1 to 4.2

An application on 4.1 compiles on 4.2 unchanged. **The database schema changed**, for the first time
since 4.0: run [the 4.2 schema migration](#the-4-2-schema-migration).

| Added | What it is |
|---|---|
| `QuartzHealthCheckOptions.StaleFiringTolerance` | `double?`, default `null` (off). Degraded when a schedulable trigger is overdue by this many misfire thresholds, unhealthy at twice that. Start at `3`. See [Health checks](packages/hosted-services-integration.md#health-checks) |
| `TriggerQuery.NextFireTimeBefore` | `DateTimeOffset?`, default `null` (every trigger). Selects triggers due before that instant; a trigger with no next fire time never matches. HTTP: `nextFireTimeBefore` on `GET …/triggers`, sent by `HttpScheduler` |
| An analyzer in `Quartz.nupkg` | Under `analyzers/dotnet/cs`; no new dependency. Diagnostics `QZ0001`, `QZ0002`, `QZ0003`, `QZ0004`. Can turn a run-time failure into a build error. `<DisableQuartzAnalyzers>true</DisableQuartzAnalyzers>` turns it off. See [Compile-Time Checks](tutorial/compile-time-checks.md) |
| `QuartzJobAttribute` | `[QuartzJob]` declares a job; a source generator writes its `AddJob<T>` call, run by `builder.AddDeclaredJobs()`. Trimming- and AOT-safe. Errors: `QZ1001`, `QZ1002`. See [Declaring Jobs with Attributes](tutorial/declaring-jobs-with-attributes.md) |
| `CronTriggerAttribute` | `[CronTrigger("0 0 0/6 * * ?")]` beside `[QuartzJob]`, once per schedule. Checked by `QZ0001`; `H` accepted. Without `[QuartzJob]`: `QZ1003` |
| `ContinuationCondition` | `[Flags]`: `OnSuccess = 1`, `OnFailure = 2`, `OnCancellation = 4`, `OnVeto = 8`, `OnAnyOutcome = 15`. Persisted as the integer |
| `Continuation` | `readonly record struct Continuation(TriggerKey? Parent, ContinuationCondition When)`, with `Continuation.None`, `Continuation.After(parent, condition)` and `IsNone` |
| `ExecutionOutcome` | How a firing ended: `Succeeded`, `Failed`, `Cancelled`, `Vetoed`, `NotExecuted` |
| `ITrigger.Continuation` | Default: `Continuation.None`. `TriggerBase` carries the settable property; `IMutableTrigger` is unchanged |
| `TriggerBuilder<TJob>.StartAfter(parent, condition)` | Composes with a schedule: `StartAfter` plus `WithCronSchedule` starts the cron after the parent finishes. `StartTimeUtc` stays a floor |
| `ITriggerConfigurator<TJob>.StartAfter(parent, condition)` | Default throws `NotSupportedException` |
| `TriggerState.Awaiting = 7` | Appended. The state a continuation waits in |
| `StoredTriggerState.Awaiting`, `AdoConstants.StateAwaiting` | The storage names; `"AWAITING"` in `TRIGGER_STATE` |
| `AdoConstants.ColumnContinuesTriggerName`, `ColumnContinuesTriggerGroup`, `ColumnContinuationCondition` | The three nullable `QRTZ_TRIGGERS` columns of `4.2/add_continuations_<db>.sql`: parent key and condition (as its integer). `null` on other triggers |
| `TriggerHeader.ContinuesAfter`, `TriggerHeader.ContinuationCondition` | What a listing shows for a waiting trigger |
| `TriggeredJobCompleteContext` | `Quartz.Extensibility`. `required init` `Trigger`, `JobDetail`, `Instruction` (what `IJobStore.TriggeredJobComplete` took), plus `Outcome` and `Exception` |
| `IJobStore.FiringComplete` | The completion the scheduler calls. Default drops the outcome and calls `TriggeredJobComplete` |
| `IDriverDelegate.SelectAwaitingContinuations`, `ReleaseContinuation`, `ResetContinuationFireTime` | Default: nothing awaiting, nothing to fix. `ReleaseContinuation` takes the `fireTime` the store computed (not now) and clears the three continuation columns |
| `AwaitingContinuation` | `SelectAwaitingContinuations`' result: a `TriggerKey` and its condition |
| `ObjectDoesNotExistException` | A `JobPersistenceException`. Thrown when a continuation names a trigger the store does not hold; carries `TriggerKey` and `MissingTriggerKey`; nothing is stored. `HttpScheduler` rebuilds it |
| `ScheduleJob<TJob, TInput>(input, Continuation after, options)` | Schedules a firing for when the parent completes; no time argument |
| `JobChainingJobListener.AddJobChainLink(first, second, condition)` | A conditional chain link. The two-argument overload is unchanged (`OnAnyOutcome`). `JobExecutionVetoed` is now declared, so a veto link fires |
| `IPersistentStoreBuilder.UseExecutionHistory()` | Keeps execution history in the job store's database, one per cluster. Default registers the store. Needs [the execution history tables](#the-execution-history-tables) |
| `AdoJobStoreOptions.ExecutionHistory` | `bool`, `false`. Set by `UseExecutionHistory()`; the history tables are probed at startup only when `true`. Flat key: `quartz.jobStore.executionHistory` |
| `AdoConstants.TableExecutionHistory`, `TableMisfireHistory`, `ColumnRunTime`, `ColumnSucceeded`, `ColumnErrorMessage`, `ColumnMisfireTime` | The two history tables and their four columns not shared with `QRTZ_FIRED_TRIGGERS` |
| `IJobExecutionContext.Outcome`, `IJobExecutionContext.RetryScheduled` | How the firing ended and whether a retry was scheduled. Defaults: `ExecutionOutcome.Succeeded`, `false`. Written before `JobWasExecuted` and `TriggerComplete`. `JobExecutionContextImpl` declares both |
| `ITriggerListener.TriggerRetriesExhausted` | Raised once when a failed occurrence under a retry policy will not be retried. Default does nothing. See [Retrying Failed Jobs](how-tos/retrying-failed-jobs.md#when-the-policy-gives-up) |
| `RetryPolicy.Exponential(maxAttempts, initialDelay, factor, maxDelay, jitter)`, `RetryPolicy.Jitter` | Jitter on exponential backoff; a new five-argument overload. See [Retrying Failed Jobs](how-tos/retrying-failed-jobs.md#give-the-trigger-a-policy) |
| `ExecutionHistoryEntry.RetryAttempt`, `ExecutionHistoryEntry.RetryScheduled` | Which attempt a row was and whether another was scheduled. Also on `DashboardHistoryEntry` and `ExecutionHistoryEntryDto` |
| `ExecutionHistoryQuery.FailedFinally`, `DashboardHistoryQuery.FailedFinally` | `bool?`, `null` (everything). `true` lists failures that were not retried, `false` the rest. HTTP: `failedFinally` on `GET …/history/executions`, sent by `HttpScheduler` |
| `QuartzInstrumentation.Instruments.TriggerRetriesExhausted` | `quartz.trigger.retries_exhausted`, a `Counter<long>` of `{trigger}` with `quartz.trigger.retry`'s attributes. The eleventh instrument |
| `AdoConstants.ColumnRetryScheduled` | `RETRY_SCHEDULED` on `QRTZ_EXECUTION_HISTORY`, beside `RETRY_ATTEMPT`. Part of the optional history script |
| `IThreadPool.TryRunWithState` | `TryRunWithState(Func<object?, ValueTask> action, object? state, CancellationToken)`. Default wraps the pair and calls `TryRun`. `TaskSchedulingThreadPool` overrides it, so a firing allocates no closure |
| `QuartzDashboardOptions.AttachStore(target, store, configure)` | Shows every scheduler in a database as a **window**. `store` is the cluster's own `IPersistentStoreBuilder` callback. See [Store-attached targets](packages/dashboard.md#store-attached-targets) |
| `AttachStoreOptions` | `RediscoveryInterval`: `TimeSpan?`, one minute; `null` asks once |
| `SchedulerOrigin.Window = 3` | Appended. A scheduler reached through the database it shares with its process |
| `SchedulerRegistration.Target` | `string?`, `null` for a scheduler of this process. The attached store a window came from; with `Name`, the window's identity (`prod/reporting`). HTTP: `target`, now written for every entry |
| `SchedulerHeaderDto.Target`, `IsWindow`, `DisplayName` | `Target` as above; `IsWindow` is `Origin == SchedulerOrigin.Window`; `DisplayName` is the label (`prod/reporting` or the bare name). Clients and authorization still use `SchedulerName` |
| `IDriverDelegate.SelectSchedulerNames` | Every `SCHED_NAME` in the database; the one member not scoped to one scheduler. Default throws `NotSupportedException`; `StdAdoDelegate` implements it in dialect-neutral SQL, so every shipped delegate has it |

**Interface members are default interface members.** Every member added above to an interface
(`ITrigger`, `ITriggerConfigurator<TJob>`, `IJobStore`, `IDriverDelegate`, `IPersistentStoreBuilder`,
`IJobExecutionContext`, `ITriggerListener`, `IThreadPool`) has a default body, so an implementation
written for 4.0 or 4.1 compiles and behaves as it did. The default is in the table where it does
something. Properties added to records (`TriggerHeader`, `ExecutionHistoryEntry`,
`SchedulerRegistration`, `TriggerQuery`) are non-positional `init` properties, so constructors are
unchanged.

**Mixed 4.1 and 4.2 versions:**

* `TriggerState` travels over the HTTP API as its name. A 4.1 client reading a listing from a 4.2 host
  throws on `"Awaiting"` once a continuation exists.
* `SchedulerOrigin` is numeric on the wire. A 4.1 client sees an unnamed value for a window.
* A trigger with retry jitter stores a `;j<value>` token, which a node older than 4.2 cannot read. A
  policy without jitter stores what it always did.

### Conditional continuations

A *conditional continuation* is a trigger that waits in the store, in `TriggerState.Awaiting`, for
another trigger's firing to end. The firing's `ExecutionOutcome` then releases it (if its
`ContinuationCondition` names that outcome) or discards it. It settles once, inside the parent's lock and
transaction. [Job Continuations](how-tos/job-continuations.md) has the full rules: release and discard,
deleted parents, cluster recovery, and what a listing shows.

* The parent is a `TriggerKey`, not a `JobKey`: a continuation waits for one firing, and a job may have
  several triggers.
* The parent must already be stored, or the continuation is refused with
  `ObjectDoesNotExistException`.
* **A retried failure settles nothing.** It is reported as `Failed`, but its
  `TriggeredJobCompleteContext.Instruction` is `SchedulerInstruction.RetryTrigger`, and every store
  skips settling on that instruction. A store that checked only the outcome would discard an
  `OnSuccess` continuation at the parent's first failure.
* A **recurring** conditional chain ("run the cleanup whenever the nightly job fails") is
  `JobChainingJobListener.AddJobChainLink(first, second, condition)`, not a continuation.

### Continuations in files, in the dashboard and on the wire

* **Scheduling files**, on any trigger. XML: `<continues-after>` (a `<name>` and an optional `<group>`)
  and `<continuation-condition>` (outcomes joined with `|`), optional, between `<preferred-node>` and
  `<job-data-map>`; the schema keeps version `2.0` and its namespace, and the
  [XML trigger kinds stay frozen](packages/quartz-plugins.md#the-xml-trigger-kinds-are-frozen). JSON:
  `ContinuesAfter` as a `Name`/`Group` object and `ContinuationCondition`. A file's triggers are stored
  parent first. See
  [Declaring one in a scheduling file](how-tos/job-continuations.md#declaring-one-in-a-scheduling-file).
* **HTTP API.** `TriggerHeaderDto` carries `continuesAfterTriggerName`, `continuesAfterTriggerGroup`
  and `continuationCondition` (as names;
  [enums travel as names](packages/http-api.md#enums-travel-as-names)). `?state=Awaiting` lists what is
  waiting. `TriggerDetailsUpdate` has no continuation fields: changing the parent is a reschedule.
* **A 4.1 client throws on `"Awaiting"`** from a 4.2 host, as a 4.0 client did on
  `SchedulerOrigin.Remote` from 4.1. It happens only once a continuation is scheduled, which the
  upgrade order below puts last.
* **Dashboard.** An **Awaiting only** filter, and the parent and condition on waiting triggers; see
  [Continuations](packages/dashboard.md#continuations). `Quartz.Dashboard`'s `TriggerHeaderDto` gains
  `ContinuesAfter` and `ContinuationCondition` as non-positional `init` properties.

### The 4.2 schema migration

`database/migrations/4.2/add_continuations_<db>.sql` adds three nullable columns to `QRTZ_TRIGGERS`:
`CONTINUES_TRIGGER_NAME`, `CONTINUES_TRIGGER_GROUP` and `CONTINUATION_CONDITION`. A 4.2 node **refuses to
start** without them, and the error names the column and the script.

1. Run the migration while 4.1 nodes are still running. The columns are nullable with no default, and
   nothing a 4.1 node does on its own touches an `AWAITING` row: its `INSERT` names its own columns,
   its acquisition and misfire sweeps select `WAITING`, and its cluster recovery touches `ACQUIRED` and
   `BLOCKED`.
2. Roll **every** node to 4.2.
3. Only then schedule continuations.

The order matters because a 4.1 node mishandles continuations:

* It cannot **settle** one: a parent completing on a 4.1 node leaves its continuations waiting.
* It reads an unknown state as waiting, so it reports an `AWAITING` row as `Normal`. Its
  single-trigger `PauseTrigger` then writes `PAUSED` over the row, and a resume makes it `WAITING`: the
  continuation fires without its parent. A 4.1 reschedule rewrites it as an ordinary trigger.
  `PauseJob` and the group and batch pauses leave `AWAITING` rows alone.

While a 4.1 node runs, do not pause, resume or reschedule a continuation from it.

`ProvisionSchema()` does not add columns to an existing table. A fresh install from
`database/tables/` already has them. See
[Database Schema Changes](../database/schema-changes.md#version-4-2).

### A retry policy that gives up says so

A trigger that ran out of retry attempts used to go back to its schedule silently. It is now reported
in four places; what the scheduler does is unchanged.

* **Context:** `IJobExecutionContext.Outcome` and `IJobExecutionContext.RetryScheduled` are written
  before the completion notifications, so a job listener can tell an attempt from the final result.
* **Trigger listener:** `ITriggerListener.TriggerRetriesExhausted`, once, between `JobWasExecuted` and
  `TriggerComplete`.
* **History:** every row carries `RetryAttempt` and `RetryScheduled`, and
  `ExecutionHistoryQuery.FailedFinally` selects occurrences that gave up. The dashboard's History page
  has the filter and a **Run again** button on those rows.
* **Metric:** `quartz.trigger.retries_exhausted`, with the attributes of `quartz.trigger.retry`.

`RetryPolicy.Exponential` also takes a `jitter`, so triggers that failed together do not retry
together. See [Retrying Failed Jobs](how-tos/retrying-failed-jobs.md#when-the-policy-gives-up).

**Behaviour change:** `IJobExecutionContext.RetryAttempt` is now the attempt the job **just made**. It
used to read the trigger's field live, which `ExecutionComplete` advances or zeroes, so a listener saw
the next attempt or nothing. Inside `Execute` the value is unchanged.

### The execution history tables

`database/migrations/4.2/add_execution_history_<db>.sql` is **optional**. It creates
`QRTZ_EXECUTION_HISTORY` and `QRTZ_MISFIRE_HISTORY`, used only by
[`UsePersistentStore(store => store.UseExecutionHistory())`](tutorial/job-stores.md#execution-history-in-the-database).
A scheduler that keeps no history never probes for them, so skip the file if you do not want a
database-backed history.

Why use it: the default history is in memory and per process, so a node's History page shows only its
own executions, and a store-attached dashboard shows none. With the tables, every node writes one
history and every row carries its instance id.

* `ProvisionSchema()` creates these tables, and so does a fresh install from `database/tables/`
  (whether or not the history is on). Only a database created by 4.0 or 4.1 needs the script.
* A store asked for a history it cannot keep names the script.
* Safe in a mixed cluster: a 4.1 node cannot see the tables, and a 4.2 node without
  `UseExecutionHistory()` neither reads nor writes them.

## Upgrading from 4.0 to 4.1

An application on 4.0 compiles on 4.1 unchanged, and the database schema did not change. The
exceptions, both at the end of this section: a logging package that `Quartz` no longer brings
transitively, and two cron expressions that 4.1 reads differently.

| Added | What it is |
|---|---|
| `ISchedulerRuntime : ISchedulerRegistry` | Adds and removes schedulers after the container is built. `Add(schedulerName, configure, options, cancellationToken)` builds one in its own container and binds it into this container's repository; `Remove(schedulerName, waitForJobsToComplete, cancellationToken)` shuts it down and releases it. Registered by `AddQuartz`; `ISchedulerRegistry` resolves to the same object |
| `SchedulerAddOptions` | `readonly record struct`: `Properties`, `Configuration`, `CreateWithoutStarting`. `default` builds from the recipe and starts |
| `ISchedulerRuntime.Restart` | `Restart(schedulerName, options, cancellationToken)` shuts a scheduler down and builds a new one from its recipe. Works for runtime-added schedulers and for `AddQuartz(name, …)` ones. Only the name carries over |
| `SchedulerRestartOptions` | `readonly record struct`: `DrainTimeout` (30 seconds) and `Start` (default: as the old one was) |
| `SchedulerRestartException` | The outgoing scheduler's jobs outlived the drain. Carries `SchedulerName`, `JobsStillExecuting`, `DrainTimeout`. The old scheduler is down and no new one was built; retry once the work finishes |
| `IScheduler.GetStatus`, `IScheduler.GetSchedulerInstanceId` | Async forms of `Status` and `SchedulerInstanceId`. Default returns the property; `HttpScheduler` overrides both. Use these on a request path. See [Blocking members](packages/http-client.md#blocking-members) |
| `PreferredNode` in a scheduling file | `PreferredNode` in `Quartz:Schedule` and `quartz_jobs.json`, `<preferred-node>` in `quartz_jobs.xml`. An instance id pins to that node, `"*"` to the first node that fires it. See [preferred node](tutorial/node-affinity.md#in-a-scheduling-file) |
| `QuartzHttpApiOptions.IsJobTypeAllowed` | `Func<string, bool>?`, `null` (all allowed). Gets the type *name* as sent; a refusal is `403`, and a refused job fails the whole `schedule-multiple` batch. See [Narrowing which job types may be named](packages/http-api.md#narrowing-which-job-types-may-be-named) and [`SECURITY.md`](https://github.com/quartznet/quartznet/blob/main/.github/SECURITY.md#what-is-not-a-vulnerability) |
| `QuartzDashboardOptions.IsJobTypeAllowed` | The same for the dashboard's `IQuartzApiClient`; a refusal raises `UnauthorizedAccessException`. Configured separately from the API. See [the dashboard's section](packages/dashboard.md#narrowing-which-job-types-may-be-named) |
| `<execution-group>` and `<retry-policy>` in `job_scheduling_data_2_0.xsd` | Optional, between `<calendar-name>` and `<job-data-map>`. Schema version `2.0` and its namespace are kept, so older files still validate. The trigger kinds stay [frozen](packages/quartz-plugins.md#the-xml-trigger-kinds-are-frozen) |
| `SchedulerOrigin.Remote` | A scheduler reached through a proxy (`HttpScheduler` from `AddQuartzHttpClient`); was reported as `Runtime` |
| `SchedulerRegistration.SchedulerInstanceId` | The scheduler's node, or `null` if none is built or it could not be asked. An `init` property beside the positional three |
| `IExecutionHistoryStore` | `Quartz.Extensibility`: `AddExecution`, `QueryExecutions`, `AddMisfire`, `QueryMisfires`, `CountMisfires`. Shipped implementation is in memory and bounded; register your own before `AddQuartzExecutionHistory()`. `IDashboardHistoryStore` still works and is adapted both ways |
| `ExecutionHistoryEntry`, `MisfireHistoryEntry` | Same fields as `DashboardHistoryEntry` and `DashboardMisfireEntry`, but a misfire's job is a `JobKey`, not a `JobKeyDto` |
| `ExecutionHistoryQuery`, `MisfireHistoryQuery` | `PagedQuery` records: required `SchedulerName`, optional `SchedulerInstanceId`, and `JobContains` / `TriggerContains` (match group, name or `group.name`). The dashboard's queries keep `JobFilter` / `TriggerFilter` |
| `ExecutionHistoryOptions` | `Retention` (24 hours), `MaxEntriesPerScheduler` (2000). `0` records nothing |
| `services.AddQuartzExecutionHistory(configure)` | Records every scheduler's executions and misfires. Idempotent (`AddQuartzHttpApi()` and `AddQuartzDashboard()` both call it); order against `AddQuartz` does not matter |
| Three history routes | `GET …/schedulers/{schedulerName}/history/executions` (`skip`, `take`, `includeTotalCount`, `schedulerInstanceId`, `jobContains`, `triggerContains`), `GET …/history/misfires` (same, without `jobContains`), `GET …/history/misfires/count?since=…`. Per-scheduler authorization. A 4.0 host answers `404`, which `HttpExecutionHistoryStore` reports as "no history" |
| An event stream on the wire | `GET …/schedulers/{schedulerName}/events`, [server-sent events](packages/http-api.md#the-event-stream). Fourteen kinds, including new `JobInterrupted`, `TriggerInError` and `Heartbeat`. Per-scheduler authorization, checked before the stream opens. No replay, no `Last-Event-ID`. A 4.0 host answers `404`, reported as "no event stream" |
| `QuartzHttpApiOptions.EventStreamHeartbeatInterval` | Idle time before a `Heartbeat` frame; 15 seconds, validated at startup. Keep it under your proxy's read timeout (nginx: 60 seconds, Azure front doors: 90) |
| `QuartzHttpApiOptions.ReadOnly` | `false`. When `true`, every mutating route answers `403` before its handler runs. `POST …/jobs/fetch` and `POST …/triggers/fetch` are reads. The dashboard has its own `QuartzDashboardOptions.ReadOnly`. See [Serving reads only](packages/http-api.md#serving-reads-only) |

**Mixed 4.0 and 4.1 versions:** `SchedulerOrigin` travels as its **name** in `GET …/schedulers`, so a
4.0 client throws on `"Remote"` from a 4.1 host that fronts a proxy. Upgrade the client first, or do
not front proxies from a host that older clients read.

Fourteen behaviours changed without a signature changing:

* **`Shutdown(waitForJobsToComplete: false)` no longer drops a firing.** It stops and waits for the
  firing loop before closing the thread pool, so an occurrence already committed to the store is
  dispatched. It then gives running executions up to two seconds to report completion before closing
  the job store. A completion after that is refused, leaving the firing `EXECUTING` and its trigger
  `BLOCKED` until a peer recovers it. A job still running after two seconds is abandoned as before. So
  the default shutdown (`QuartzHostedServiceOptions.WaitForJobsToComplete` is off) can take up to two
  seconds longer. See [When a node leaves](tutorial/advanced-enterprise-features.md#when-a-node-leaves).
* **`HttpScheduler.UpdateTriggerDetails` works** instead of throwing `NotSupportedException`. The route
  is `POST …/triggers/{triggerGroup}/{triggerName}/update-details`, answering `{ "applied": … }`. The
  body is a patch: an omitted member is left alone, `null` clears it. A misfire instruction for the
  wrong schedule family is refused, as in process. A 4.0 server answers `404`, so upgrade the host
  before the client. See [Editing a trigger in place](packages/http-api.md#editing-a-trigger-in-place).
* **`ISchedulerFactory.LookupScheduler` builds a registered scheduler that is not built yet**, instead of
  answering `null`. A scheduler added at runtime is found while alive and not rebuilt after shutdown.
* **`QuartzHostedService` shuts down schedulers added at runtime** when the host stops, each under the
  `QuartzHostedServiceOptions` for its name. So
  `AddQuartzHostedService("acme", o => o.WaitForJobsToComplete = true)` configures a tenant added later.
  Before, they were left to container disposal, after the shutdown window.
* **A health check falls back to the repository** when the container has no registration under its
  name, so `AddHealthChecks().AddQuartz("acme")` reports on a tenant added later. The not-found message
  names both places it looked.
* **A misspelled `quartz.jobStore.*` key fails startup**, as on 3.x; 4.0 ignored it (for example
  `quartz.jobStore.dbRetryIntreval`). An ADO.NET store refuses an unread key under its prefix when its
  options are resolved. It also catches keys from an `appsettings.json` `JobStore` section, such as
  `Quartz:JobStore:TabelPrefix`.
  * `lockHandler.*` and `driverDelegateInitString` keep their own handling.
  * A store with no options type keeps failing in the binder that writes its keys.
  * `quartz.checkConfiguration = false` allows keys of your own, as before.
* **`AddQuartzHttpApi()` records execution history** by calling `AddQuartzExecutionHistory()`: in
  memory, 24 hours, 2000 rows per scheduler per feed. To opt out, set
  `ExecutionHistoryOptions.MaxEntriesPerScheduler` to `0`. To keep it elsewhere, register an
  `IExecutionHistoryStore` before the call. See [Execution history](packages/http-api.md#execution-history).
* **`AddQuartzDashboard()` no longer registers `DashboardHistoryPlugin`.** The recorder does its work.
  Do not register the plugin beside it, or every execution is recorded twice. `IDashboardHistoryStore`
  is unchanged and is adapted onto `IExecutionHistoryStore` both ways.
* **`AddQuartzDashboard()` no longer registers `DashboardLiveEventsPlugin`, and the pages no longer
  connect to the dashboard's own hub.** It calls `AddQuartzSchedulerEvents()`, and Live Logs subscribes
  to that stream: in process, or over the event route for a scheduler registered with
  `AddQuartzHttpClient`. The page used to open a SignalR client connection to the dashboard's public
  hub URL, which failed behind a reverse proxy
  ([#3713](https://github.com/quartznet/quartznet/discussions/3713)). **A proxy now needs to forward
  `{DashboardPath}/hub` only for your own clients.** The hub is still served, with the same
  `IQuartzDashboardHubClient` payloads. Registering the plugin beside the publisher pushes every event
  twice.
* **Dashboard events `9100` and `9101` gained two trailing placeholders**:
  `"… : {Outcome} (origin {Origin}, node {Node})"` and `"… and it failed: {Reason} (origin {Origin}, node
  {Node})"`. They name the `SchedulerOrigin` and node, or `(unknown)`. Matching on the id or a prefix
  still works; matching the whole line does not. The Action Log page shows both and now records
  interrupts from **Currently Executing**. See [Action Log](packages/dashboard.md#action-log).
* **The HTTP API logs successful mutations.** Event `9007`, `Information`,
  `"Api user {User} performed {Operation} on scheduler {SchedulerName}: {Route}"`, category
  `Quartz.HttpApi`. `{User}` is `HttpContext.User.Identity.Name` or `(anonymous)` (every line under
  `AllowAnonymous()`). Written after the handler, for `2xx` answers only; refusals and failures are
  still `9005` and `9003`/`9004`. Check your log volume budget. See
  [Production hardening](packages/http-api.md#production-hardening).
* **`AddQuartzHttpApi()` streams events** by calling `AddQuartzSchedulerEvents()`. With no subscriber,
  no event is built.
* **Nothing in Quartz reads `Status` or `SchedulerInstanceId` off a remote scheduler.**
  `ISchedulerRepository` used to read `Status` under its lock on every lookup, so one unreachable
  `HttpScheduler` stalled every lookup for the client's timeout. The listing now calls the async forms
  with a two-second deadline and reports a silent target as `SchedulerStatus.Unknown` with a null
  instance id. See [Blocking members](packages/http-client.md#blocking-members).
* **`AddQuartzHttpClient` refuses a scheduler name it has already registered.** A second call used to
  replace the first silently. Use a different scheduler name per target.

Recipes: [Multi-Tenancy](multi-tenancy.md#adding-a-tenant-while-the-process-is-running), and for the
dashboard,
[Fronting a scheduler in another process over HTTP](packages/dashboard.md#fronting-a-scheduler-in-another-process-over-http).

**`Quartz` depends on `Microsoft.Extensions.Logging.Abstractions`, not
`Microsoft.Extensions.Logging`.** `AddQuartz` no longer calls `AddLogging()`. Quartz logs through
whatever `ILoggerFactory` the container holds, so a hosted application, or one that calls
`services.AddLogging(…)`, sees no difference.

A project that used `LoggerFactory`, `AddLogging` or another `Microsoft.Extensions.Logging` type
through `Quartz` without referencing the package must now reference it:

```shell
dotnet add package Microsoft.Extensions.Logging
```

Two more consequences, neither of which affects a hosted application:

* A container with no logging configured holds no `ILoggerFactory`. It still builds and runs a
  scheduler, and Quartz's lines go to `LogProvider`. (Quartz registering a factory would override a
  later `services.AddLogging(logging => logging.AddConsole())` and drop its providers.)
* `QuartzSchedulerBuilder` refuses at `Build()` a container with `ILoggerProvider` registrations and no
  `ILoggerFactory`. Register the provider with `Services.AddLogging(logging =>
  logging.AddProvider(…))`, which adds a factory.

Two cron expressions change meaning. Both parse on 4.1, so nothing reports it:

| Change | 4.0 | 4.1 |
|---|---|---|
| **Behaviour change.** `MON/2`: a textual day-of-week with a step | `FormatException` | Means `2/2`: Monday, Wednesday and Friday. On 3.x it meant *every second Monday*, so an expression carried from 3.x fires 156 times a year instead of 26, silently. [What to do about it](#mon-2-is-a-fortnight-on-3-x-and-a-step-from-4-1) |
| **Behaviour change.** `MON-FRI/2`: a step after a textual *range* | Parsed, step dropped: `MON-FRI`, five days | `2-6/2`: Monday, Wednesday and Friday. A trigger written this way was firing on two extra days |

## The road from 3.x, phase by phase

The 4.0 API came from six passes over the public surface. This guide is organised by topic; work
through the passes in this order.

1. **The extensibility contracts** (the interfaces you implement). Start here: everything else assumes
   these signatures.
   [`Quartz.Spi` is `Quartz.Extensibility` and `Quartz.Simpl` is `Quartz.Impl`](#quartz-spi-and-quartz-simpl-were-renamed);
   every async member returns [`ValueTask`](#tasks-changed-to-valuetask) and ends with a
   `CancellationToken`; [jobs take that token as a parameter](#jobs-take-a-cancellationtoken); the
   [job factory hands out a scope](#the-job-factory-hands-out-a-scope); the
   [thread pool is asynchronous](#the-thread-pool-is-asynchronous);
   [trigger fire times are properties](#trigger-fire-times-are-properties); and
   [a job detail of your own](#an-ijobdetail-of-your-own) is implementable, because the member no
   implementation could write is gone from `IJobDetail`.
2. **The vocabulary and the surface.** One word per concept, and nothing public that was never a
   contract. [Names that were normalized](#names-that-were-normalized),
   [the scheduler and the job store speak the same verbs](#the-scheduler-and-the-job-store-speak-the-same-verbs),
   [matchers moved to `Quartz`](#matchers-moved-to-quartz),
   [`Key<T>` moved to `Quartz` and is immutable](#key-t-moved-to-quartz-and-is-immutable),
   [`SchedulerMetadata` replaces `SchedulerMetaData`](#schedulermetadata-replaces-schedulermetadata),
   [the listener API](#listener-api-changes), and
   [sealed and internalized types](#sealed-and-internalized-types).
3. **Listings, schedules and dates.** [Job store listings became queries](#job-store-listings-became-queries)
   returning `PagedResult<T>` of headers; [misfire instructions are enums](#misfire-instructions-are-enums);
   [intervals are said once per builder](#intervals-are-said-once-per-builder);
   [`TimeOfDay` became `TimeOnly`](#timeofday-became-timeonly);
   [`DateBuilder`'s static factories are gone](#datebuilder-s-static-factories-are-gone);
   [`Executing` is a trigger state](#executing-is-a-trigger-state); and
   [the preferred node is a value](#the-preferred-node-is-a-value).
4. **The ADO.NET job store.** Read
   [the store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) first: the
   hierarchy is internal, so several later sections describe a seam that is now closed. Then
   [trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate);
   [the stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use);
   [nine `Execute…Lock` overloads became four](#nine-execute-lock-overloads-became-four-members);
   [locks are a `SchedulerLock`](#locks-are-a-schedulerlock-not-a-string);
   [the job store configuration is read-only](#the-job-store-configuration-is-read-only-and-no-longer-a-public-currency);
   [the driver delegate speaks in records](#the-driver-delegate-speaks-in-records);
   [the optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone),
   which makes the [schema migration](#database-schema-migration) mandatory;
   [`RAMJobStore` is sealed](#ramjobstore-is-sealed);
   [a job store of your own can join your transaction](#a-job-store-of-your-own-can-join-your-transaction);
   and the two stores, held to one contract test,
   [answer the same way](#the-two-job-stores-answer-the-same-way).
5. **Configuration and hosting.** The container builds the scheduler:
   [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone),
   [the standalone builder is the same builder](#the-standalone-builder-is-the-same-builder),
   [there is no process-global scheduler state](#no-process-global-scheduler-or-connection-state),
   [`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container),
   [one shape per registration method](#one-shape-per-registration-method),
   [clustering is configured in one place](#clustering-is-configured-in-one-place),
   [the hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler), and every
   scheduler publishes [job execution metrics](#job-execution-metrics).
6. **Serialization policy and the last edges.**
   [`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it);
   [the two exceptions moved out of `Quartz.Core`](#the-two-exceptions-moved-out-of-quartz-core);
   [execution limits are built once, then frozen](#execution-limits-are-built-once-then-frozen);
   [interruption has two names, not three](#interruption-has-two-names-not-three); and
   [Other Breaking Changes](#other-breaking-changes).

Two changes run through all six and are not repeated case by case: `Task` became `ValueTask` on nearly
every member, and the namespaces moved as in pass 1. If a type you used is not in this guide, check
[Package Changes](#package-changes): it may have moved packages.

## Target Framework

Quartz.NET 4.x targets `net10.0` only. There is no `netstandard2.0` build and no support for Full
Framework style `.config` files. Upgrade your application to .NET 10 before upgrading to Quartz 4.x.

### If you are a library rather than an application

A package that still serves `net8.0` and `net9.0` consumers references Quartz 3.x for those targets and
Quartz 4 for `net10.0`: a conditional `PackageReference`, plus `#if NET10_0_OR_GREATER` where the call
sites differ. This works only while the difference stays inside the library. If your own public surface
would differ per target framework, ship a separate `net10.0`-only package or drop the old targets.

[Embedding Quartz in a Library](how-tos/embedding-quartz-in-a-library.md#shipping-a-package-that-multi-targets)
has the csproj fragment, the limits, and the renames a real port hit.

## Package Changes

### The first error you will see

`dotnet add package Quartz` in a 3.x application installs 4.x. The merged packages stay in the project
file and reference `Quartz` with no upper bound, so the build compiles against `Quartz` 4.x **and** a
3.x `Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting` or
`Quartz.Serialization.SystemTextJson`. Eight type names exist in both:

```text
error CS0433: The type 'QuartzOptions' exists in both
'Quartz.Extensions.DependencyInjection, Version=3.20.0.0, Culture=neutral, PublicKeyToken=f6b8c98a402cc8a4'
and 'Quartz, Version=4.0.0.0, Culture=neutral, PublicKeyToken=f6b8c98a402cc8a4'
```

The other seven are `ITriggerConfigurator`, `JobFactoryOptions` and `SchedulingOptions` (from
`Quartz.Extensions.DependencyInjection`), `QuartzHostedService`, `QuartzHostedServiceOptions` and
`QuartzServiceCollectionExtensions` (from `Quartz.Extensions.Hosting`), and `JsonSerializationException`
(from `Quartz.Serialization.SystemTextJson`). Calls into the duplicated extension classes report
`CS0121: The call is ambiguous`, naming both assemblies.

**Fix: remove the three package references.** Both errors go with them.

`Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting`, and `Quartz.Serialization.SystemTextJson` have been merged into the main `Quartz` package:

```diff
- <PackageReference Include="Quartz.Extensions.DependencyInjection" Version="3.*" />
- <PackageReference Include="Quartz.Extensions.Hosting" Version="3.*" />
- <PackageReference Include="Quartz.Serialization.SystemTextJson" Version="3.*" />
+ <PackageReference Include="Quartz" Version="4.*" />
```

If you use Newtonsoft.Json serialization, reference `Quartz.Serialization.Newtonsoft` instead of the old `Quartz.Serialization.Json`.

Configuration that names a type from a merged assembly as a string keeps working: a name that fails to
resolve is retried against `Quartz`, with a warning naming both spellings.

### Empty packages under the four old ids

From 4.0.1, `Quartz.Extensions.DependencyInjection`, `Quartz.Extensions.Hosting`,
`Quartz.Serialization.Json` and `Quartz.Serialization.SystemTextJson` are published at every 4.x version
as **empty packages**: a dependency on the replacement package, a readme, and no assembly.

**Still remove them.** They exist so that a dependency bot can reach 4.x. A bot updating Quartz with
these as one group resolves to the newest version every member has; until 4.0.1 that was 3.20.1, so
bots closed the 4.0.0 pull request and landed 3.20.1. Now the group resolves to 4.x, and you delete the
reference in that pull request.

An empty package cannot cause the `CS0433` above. A pinned 3.x one still can.

`Quartz.OpenTracing` and `Quartz.OpenTelemetry.Instrumentation` are **not** published this way: they
have no 4.x replacement package to depend on. Quartz's own activity source and meter replace both; see
[OpenTelemetry Integration](packages/opentelemetry-integration.md).

`Quartz.OpenTracing` is **dropped**, with no 4.x release. It consumed the `DiagnosticSource` events that
4.x replaced with `System.Diagnostics.Activity`, and OpenTracing is archived. Remove the package
reference and the `AddQuartzOpenTracing` call, and subscribe to Quartz's activity source and meter; see
[OpenTelemetry Integration](packages/opentelemetry-integration.md#coming-from-quartz-opentracing).

```diff
- <PackageReference Include="Quartz.OpenTracing" Version="3.*" />
```

**`OpenTelemetry.Instrumentation.Quartz` is not the replacement.** It reads the same `DiagnosticSource`
events, so on 4.x it produces no spans, silently. Remove it and add
`AddSource(QuartzInstrumentation.ActivitySourceName)`; see
[OpenTelemetry.Instrumentation.Quartz](packages/opentelemetry-integration.md#opentelemetry-instrumentation-quartz).

```diff
- <PackageReference Include="OpenTelemetry.Instrumentation.Quartz" Version="1.*" />
```

### `Quartz.Aspire` is new

A new package with no 3.x counterpart; nothing to migrate.

`Quartz.Aspire` is the client integration for [Aspire](https://aspire.dev/). It turns an Aspire
connection name into a persistent job store: the driver delegate for that database, connections from a
`DbDataSource` in the container, the scheduler's health check, and Quartz's two telemetry signals
registered with your ServiceDefaults OpenTelemetry pipeline.

```csharp
builder.AddQuartzPersistentStore("quartz");
builder.AddQuartz();
```

It has **no `Aspire.*` package dependency** (`IHostApplicationBuilder` is its whole contract), so it
works in any generic-host application. There is no hosting integration: the AppHost declares its
database resources itself. [Aspire Integration](packages/aspire.md) lists every setting it reads from
`Aspire:Quartz`; [Running Quartz under Aspire](how-tos/aspire.md) shows what the call expands to.

Read this setting before the first run:

| Setting | Default | What it decides |
|---|---|---|
| `QuartzAspireSettings.SchemaProvisioning` | unset: `SchemaProvisioning.CreateIfMissing` under `Development`, nothing in other environments | What the store does about its schema at startup, with the values of `AdoJobStoreOptions.SchemaProvisioning`. A named value applies in every environment; a value set on the store itself wins. See [`PerformSchemaValidation` became `SchemaProvisioning`](#performschemavalidation-became-schemaprovisioning-which-has-a-third-position) |

### The health check is in `Quartz`, not `Quartz.AspNetCore`

The health check moved to the core package. `Quartz.AspNetCore` has a
`<FrameworkReference Include="Microsoft.AspNetCore.App" />`, so a worker on a `dotnet/runtime` image
that referenced it only for the check failed to start
([#3532](https://github.com/quartznet/quartznet/issues/3532)). The check still registers on the
standard `IHealthChecksBuilder`.

In 3.x, `AddQuartzServer` from `Quartz.AspNetCore` registered the hosted service and the health check
together, with only a tag list to choose. In 4.0 each has its own call, both in `Quartz`:

| | 3.x | 4.0 |
|---|---|---|
| The hosted service | `services.AddQuartzServer(…)` | `builder.AddQuartzHostedService(…)`, in `Quartz` |
| The health check | the same call, implicitly | `services.AddHealthChecks().AddQuartz(…)`, or `q.AddQuartzHealthChecks(…)` on the scheduler's own builder, both in `Quartz` |
| Its settings | `IEnumerable<string>? healthCheckTags` | `QuartzHealthCheckOptions`, in `Quartz` |

See [`AddQuartzServer` is `AddQuartzHostedService`](#addquartzserver-is-addquartzhostedservice). If the
check was your only reason to reference `Quartz.AspNetCore`, remove it:

```diff
- <PackageReference Include="Quartz.AspNetCore" Version="3.*" />
+ <PackageReference Include="Quartz" Version="4.*" />
```

Keep `Quartz.AspNetCore` for the [HTTP API](packages/http-api.md) or the
[dashboard](packages/dashboard.md). `MapHealthChecks` and `HealthCheckOptions.ResultStatusCodes`, which
decides what a *degraded* scheduler answers over HTTP, belong to ASP.NET Core; see
[ASP.NET Core Integration](packages/aspnet-core-integration.md#health-checks).

## Documentation pages that moved

The old URLs redirect. Update links you keep in a wiki or runbook:

| Old URL | Where it is now |
|---|---|
| `documentation/quartz-4.x/tutorial/crontrigger.html` | [`cron-expressions.html`](cron-expressions.md) |
| `documentation/quartz-4.x/how-tos/crontrigger.html` | [`cron-expressions.html`](cron-expressions.md) (the old page was a stale copy) |
| `documentation/quartz-4.x/packages/json-configuration.html` | [`configuration/json.html`](configuration/json.md) |
| `documentation/quartz-4.x/packages/opentracing-integration.html` | [`packages/opentelemetry-integration.html`](packages/opentelemetry-integration.md) (the package is gone) |
| `documentation/quartz-4.x/tutorial/miscellaneous-features.html` | [`packages/quartz-plugins.html`](packages/quartz-plugins.md) (the page was split; plug-ins were most of it) |

The externally linked anchors `#h-hash-for-load-distribution` and
`#building-cron-expressions-programmatically` moved with the cron material and resolve on
[Cron Expressions](cron-expressions.md).

## Upgrading a running deployment

The order for upgrading a deployment: a database others depend on, and nodes that are running. Each
step links the page that owns it. Steps 1 and 2 happen **while you are still on 3.x**.

1. **Get off binary serialization, on 3.x.** .NET removed `BinaryFormatter`, and 4.x cannot read a
   blob it wrote. The conversion needs `BinaryObjectSerializer`, which exists only on 3.x. Either let
   the scheduler rewrite blobs as it runs, or run a program that loads and writes back every serialized
   asset.
   [Migrating from binary serialization](packages/json-serialization.md#migrating-from-binary-serialization)
   says which blobs rewrite themselves, which never do, and how to find what is left.
   `BLOB_TRIGGERS.BLOB_DATA` cannot be converted from 4.x at all.
2. **Audit the stored cron expressions, on 3.x.** 4.x rejects several expressions 3.x stored, and
   rejects them when it reads the row, so the trigger cannot even be loaded. The SQL to find
   candidates (some clauses match too widely) is in [Before you upgrade](#before-you-upgrade).
3. **Decide on the window.** Either stop the cluster, or run 3.x and 4.0 nodes against the same tables
   during the rollout. [A mixed 3.x and 4.0 window](operations.md#a-mixed-3-x-and-4-0-window) says what
   has been checked about that state; in particular, do not let a 4.0 node write a calendar during it.
4. **Run the mandatory migration script** `schema_30_to_40_upgrade_<database>.sql`: one file per
   database, guarded, safe to run twice, and safe with 3.x nodes still firing. See
   [Database Schema Migration](#database-schema-migration). It is mandatory even for a database that
   took every optional 3.x migration, because 4.x validates the schema and `QRTZ_PAUSED_JOB_GRPS` is
   new. Always migrate the schema before the nodes: an old node tolerates a new schema, a new node does
   not tolerate an old one ([Schema first, then nodes](operations.md#schema-first-then-nodes)).
5. **Translate the configuration.** Flat `quartz.*` keys still work and mean the same thing, so this
   can be gradual; see [Configuration](#configuration) and
   [Legacy property keys](configuration/reference.md#legacy-property-keys). Before the first start,
   read [Defaults that changed](#defaults-that-changed) (an application that never named its scheduler
   stops seeing its own rows) and [Silent behaviour changes](#silent-behaviour-changes).
6. **Start 4.0.** It checks that every table it reads exists, and names a missing one. It reads the job
   type names 3.x stored: `SimpleTypeLoader` maps renamed Quartz types, and you declare an alias for a
   type of your own that moved ([Type loader](configuration/reference.md#type-loader)). Replace the
   nodes one at a time ([Replacing the nodes](operations.md#replacing-the-nodes)).
7. **Run `schema_30_to_40_indexes_<database>.sql` once the last 3.x node is gone.** It is optional and
   performance only; see [Listing indexes](#listing-indexes-optional),
   [the reshaped acquisition index](#the-acquisition-index-is-reshaped-optional) and
   [the dropped misfire index](#the-misfire-index-is-dropped-optional). Run it top to bottom. It waits
   because it drops indexes 3.x statements may still use (3.x sweeps misfires with the misfire index),
   and drops and recreates the index both versions acquire on. On an offline upgrade, run it straight
   after step 4.

[Before you go live](production-checklist.md) is the checklist for the finished state.

## Database Schema Migration

Quartz 4.x requires four columns on `QRTZ_TRIGGERS` (and one on `QRTZ_FIRED_TRIGGERS`) that were
**optional** in 3.x:

| Column | Table(s) | Optional since |
|---|---|---|
| `MISFIRE_ORIG_FIRE_TIME` | `QRTZ_TRIGGERS` | 3.17 |
| `EXECUTION_GROUP` | `QRTZ_TRIGGERS`, `QRTZ_FIRED_TRIGGERS` | 3.18 |
| `PREFERRED_NODE` | `QRTZ_TRIGGERS` | 3.19 |
| `PREFERRED_NODE_AUTO` | `QRTZ_TRIGGERS` | 3.19 |

3.x probed for these at startup and disabled a feature when its column was missing. **4.x has no
probes** and assumes all of them exist, so the migration is mandatory even if you never used misfire
reporting, execution groups or node affinity.

4.x also adds two columns 3.x never had:

| Column | Table | Holds |
|---|---|---|
| `RETRY_POLICY` | `QRTZ_TRIGGERS` | A trigger's retry policy, as its stored string form |
| `RETRY_ATTEMPT` | `QRTZ_TRIGGERS` | How many retries of the occurrence being executed have already been made |

Both are nullable with no default, so existing rows read as "no retry policy" and need no backfill.
They are in the 4.0 script because 4.x has no column probes, so a column added later would force a
mandatory migration mid-version. `AdoConstants` names them `ColumnRetryPolicy` and
`ColumnRetryAttempt`.

4.x also adds a table 3.x never had:

| Table | Holds |
|---|---|
| `QRTZ_PAUSED_JOB_GRPS` | One row per paused job group: `SCHED_NAME`, `JOB_GROUP` |

It mirrors `QRTZ_PAUSED_TRIGGER_GRPS` and makes
[`JobGroup.Paused` truthful on the ADO store](#job-store-listings-became-queries). A group can be
paused while it holds no jobs, so the flag cannot live on `QRTZ_JOB_DETAILS`. 4.x checks at startup
that every table it reads exists, including this one, so **the migration is mandatory even for a 3.x
database that took every optional migration**.

The startup check runs `SELECT 1` per table and `SELECT <column> … WHERE 1 = 0` for each column this
migration adds to an existing table. A database that skipped the migration is refused before the
scheduler starts, and the message names the column and this script. The check cannot see a column's
*type or width*: a hand-built column of the wrong shape starts cleanly, then fails on the first
statement that binds it with a provider error. Run the script in full. If startup succeeded but a
firing failed with a raw provider error, check the column definitions first.

`ProvisionSchema()` does not replace the migration. It refuses a database where a table it needs exists
but lacks a column it needs, so a 3.x database is refused rather than half-completed.

**A paused job group does not survive the upgrade.** 3.x's ADO store records nothing when it pauses a
job group (`IsJobGroupPaused` returns a hard-coded `false`, and `PauseJobs` only pauses the individual
triggers), so the migration has nothing to copy. The paused triggers stay paused. What is lost is the
group's pause: a trigger added to that job group after the upgrade starts running. Paused **trigger**
groups are unaffected; they were already `QRTZ_PAUSED_TRIGGER_GRPS` rows on 3.x.

If you rely on a paused job group, **pause it again once you are on 4.x**:

```csharp
await scheduler.PauseJobGroups(GroupMatcher<JobKey>.GroupEquals("nightly"));
```

This writes the `QRTZ_PAUSED_JOB_GRPS` row, so `JobGroup.Paused` and `IsJobGroupPaused` answer
correctly, and a trigger stored later for a job in that group is created `PAUSED`. An equality matcher
records a group with no jobs, so this also pauses a group before anything is deployed into it. Both
stores behave this way on 4.x; see
[The two job stores answer the same way](#the-two-job-stores-answer-the-same-way).

::: warning
Always run migration scripts in a test environment against a copy of your production database first.
:::

::: warning There is a step before this one
4.x rejects several cron expressions 3.x stored, and rejects them when it reads the row, so the
trigger cannot be loaded at all. Run the audit in [Before you upgrade](#before-you-upgrade) against the
live 3.x database first; the schema migration does not check the expressions.
[Upgrading a running deployment](#upgrading-a-running-deployment) has the whole sequence.
:::

[database/migrations/4.0/](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0)
holds **two** files per database, run at different times:

| File | Status | When |
|---|---|---|
| `schema_30_to_40_upgrade_<database>.sql` | **Mandatory** | Now. Everything in it is additive and safe to run while 3.x nodes are still firing. |
| `schema_30_to_40_indexes_<database>.sql` | Optional, performance only | Once the last 3.x node has shut down, or straight away if nothing is running. |

`<database>` is `sqlServer`, `postgres`, `mysql_innodb`, `oracle`, `sqlite` or `firebird`. Every
statement in both files is guarded, so each is safe whether or not you applied the optional 3.x
migrations, and safe to run twice. The three sections below describe the *index* file.

For SQL Server the column additions look like this:

```sql
IF COL_LENGTH('QRTZ_TRIGGERS','MISFIRE_ORIG_FIRE_TIME') IS NULL
BEGIN
  ALTER TABLE [dbo].[QRTZ_TRIGGERS] ADD [MISFIRE_ORIG_FIRE_TIME] bigint NULL;
END
```

Replace `QRTZ_` with your configured table prefix if different.

[Database Schema Changes](../database/schema-changes.md) has the version-by-version history, including
what each optional 3.x migration does and what skipping it costs.

### Listing indexes (optional)

The index file aligns the indexes with the statements 4.x issues. Two additions matter for the
[job and trigger listings](#job-store-listings-became-queries):

| Index | Table and columns |
|---|---|
| `IDX_QRTZ_J_G_N` | `QRTZ_JOB_DETAILS(SCHED_NAME, JOB_GROUP, JOB_NAME)` |
| `IDX_QRTZ_T_G_N` | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME)` |

Listings page with `ORDER BY JOB_GROUP, JOB_NAME` and `ORDER BY TRIGGER_GROUP, TRIGGER_NAME`, and the
primary keys are name-before-group, so without these indexes each page is a scan plus a sort. **They
are optional**; add them if you list jobs or triggers from a large schema. Fresh-install scripts for
every dialect already have them.

The file also drops indexes that are a leftmost prefix of a wider one, or that no 4.x statement uses.
PostgreSQL changes most: several of its indexes omitted `SCHED_NAME`, the leading column of every
Quartz predicate, so they could not serve a single-scheduler lookup.

### The acquisition index is reshaped (optional)

The same file drops and recreates `IDX_QRTZ_T_NFT_ST`, the index acquisition uses
([#3510](https://github.com/quartznet/quartznet/issues/3510)):

| | Table and columns |
|---|---|
| 3.x | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)` |
| 4.x | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)` |
| 4.x, Firebird | `QRTZ_TRIGGERS(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)`, unchanged |

Acquisition orders by `NEXT_FIRE_TIME ASC, PRIORITY DESC`, so an index with both directions lets the
engine take the first entry instead of sorting every due trigger. With 100,000 triggers and 5,000 due,
one acquisition on SQL Server drops from 21.6 ms and 20,395 logical reads to 0.6 ms and 8.
`MISFIRE_INSTR` avoids a table lookup per skipped misfired trigger. Only the DDL changed; the statement
did not. Firebird keeps the 3.x shape because its indexes take one direction for the whole index. See
[Database Schema](db/index.md#indexes-and-the-acquisition-index-in-particular) for the numbers and the
MySQL and Oracle notes.

**If you built a 4.0 preview schema before this change**, re-run
`schema_30_to_40_indexes_<database>.sql`. The index name did not change, so the script drops and
recreates it; a guarded `CREATE INDEX` alone would find the name taken and keep the old three columns.

### The misfire index is dropped (optional)

The same file drops `IDX_QRTZ_T_NFT_ST_MISFIRE`
([#3656](https://github.com/quartznet/quartznet/issues/3656)):

| | Table and columns |
|---|---|
| 3.x, on SQL Server, MySQL, Oracle and Firebird | `QRTZ_TRIGGERS(SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)` |
| 3.x, on PostgreSQL and SQLite | never created |
| 4.x | dropped on every dialect |

No optimizer uses it on any of the four engines that had it
([#3608](https://github.com/quartznet/quartznet/issues/3608),
[#3656](https://github.com/quartznet/quartznet/issues/3656)). It leads with `MISFIRE_INSTR`, which the
misfire sweep and its counting query compare with `<> -1`, so it can only be entered on `SCHED_NAME`;
the reshaped acquisition index serves both statements. `MySQLDelegate`'s `FORCE INDEX` hint named it,
and now names the acquisition index. The dropped index was maintained on every trigger write and read
by nothing.

* **Run the file top to bottom.** The acquisition reshape comes first, so a schema is never left with
  neither index. Every statement is guarded; re-run it as often as you like.
* **3.x reads this index**, which is the other reason the index file waits for the last 3.x node to
  shut down.

Full table creation scripts for fresh installations are available in [database/tables/](https://github.com/quartznet/quartznet/tree/main/database/tables).

## Defaults that changed

Option defaults are the same on 4.x as on 3.x: thread pool size, the ADO store's misfire threshold and
misfire batch, retry intervals, table prefix, cluster check-in interval, and the hosted service's two
switches. If you set a value yourself, this section does not apply.

Three effective defaults changed for applications that never configured Quartz, that is, built their
scheduler with `new StdSchedulerFactory()` or `GetDefaultScheduler()`. On 3.x those read an embedded
`quartz.config` that 4.x does not read.

| Setting | 3.x, unconfigured | 4.x |
|---|---|---|
| `quartz.scheduler.instanceName` | `DefaultQuartzScheduler` | `QuartzScheduler` (`QuartzSchedulerOptions.InstanceName`). **Persistent stores key every row on this**, so an application that never named its scheduler stops seeing its own jobs and triggers until it sets the old name |
| `quartz.jobStore.misfireThreshold` | 60 seconds | 5 seconds (`InMemoryJobStoreOptions.MisfireThreshold`), the in-memory store's code default on both versions; the embedded file raised it |
| `quartz.threadPool.threadCount` | 10 | 10 (`ThreadPoolOptions.MaxConcurrency`), unchanged |

`new StdSchedulerFactory(properties)` and `AddQuartz` never read the file, so they already used the
typed defaults. See [The `quartz.config` file is no longer read](#the-quartz-config-file-is-no-longer-read).

One default is new: `QuartzSchedulerOptions.PropagateTraceContext` is on, so a trigger scheduled inside
an `Activity` carries two extra data map entries. See
[Silent behaviour changes](#silent-behaviour-changes).

## Silent behaviour changes

These calls still compile but behave differently. Read this before the first run of the upgraded
application.

### Reading job data

4.x kept 3.x's shorter accessor names (`GetInt`, `GetBoolean`) with the semantics of the longer set
(`GetIntValue`, `GetBooleanValue`). If you called the longer set, nothing here changes for you.

* **The indexer throws on a missing key.** `map["absent"]` returned `null` on 3.x; on 4.x it throws
  `KeyNotFoundException`, on both `JobDataMap` and `SchedulerContext`. Use `TryGetValue` or a
  `TryGet…` accessor for an optional entry.
* **`GetString` no longer throws.** 3.x threw `KeyNotFoundException` for an absent key and
  `InvalidCastException` for a non-string. 4.x returns `null` for both, so a mistyped key or a wrongly
  typed value looks like "no value".
* **The typed accessors throw `InvalidCastException` for an absent key**, not `KeyNotFoundException`
  (`GetInt`, `GetDouble`, `GetBoolean` and the rest). A `catch (KeyNotFoundException)` around one stops
  catching.
* **`GetBoolean` reads anything but `"true"` as `false`.** 3.x used `Convert.ToBoolean`, so `"1"` or
  `"yes"` threw `InvalidCastException`. 4.x compares with `"true"` case-insensitively, so a flag stored
  as `"1"` now reads as off without an error. `TryGetBoolean` reports success for it too.
* **Strings are parsed with the invariant culture.** `GetInt`, `GetLong`, `GetDouble`, `GetFloat` and
  `Get<decimal>` used the current culture for a string value. On a comma-decimal culture, `"3.14"` read
  as `314` and now reads as `3.14`. Check stored data for the reverse case: `"3,14"` read as `3.14` and
  now throws `InvalidCastException` from every numeric accessor, because none allows a group separator.
* **`Get<DateTime>` parses with `DateTimeStyles.RoundtripKind`**, so a string ending in `Z` comes back
  as `Kind=Utc` rather than shifted to local time. See
  [`PutAsString` writes round-trip formats now](#putasstring-writes-round-trip-formats-now).
* **The scheduler context is no longer merged into job properties.** See
  [Scheduler context entries are no longer injected into job properties](#scheduler-context-entries-are-no-longer-injected-into-job-properties).

### Serialization and the store

* **`quartz.serializer.type = json` means System.Text.Json now.** On 3.x `json` meant Newtonsoft, with a
  warning. On 4.x `json` and `stj` both resolve to `SystemTextJsonObjectSerializer`, with no warning. A
  configuration file carried over verbatim changes the blob format and the registered trigger and
  calendar serializers. Write `newtonsoft` if you meant Newtonsoft.
* **Both serializers refuse a job data value they cannot read back**, at write time, and both refuse
  the same set. See
  [Both serializers refuse a job data value they cannot read back](#both-serializers-refuse-a-job-data-value-they-cannot-read-back).
* **A `Dictionary<string, string>` job data value is written the same way by both serializers**, which
  changes what Newtonsoft writes. See
  [A string dictionary is written the same way by both serializers](#a-string-dictionary-is-written-the-same-way-by-both-serializers).
* **Five calendar types serialize to a new shape.** 4.x reads both forms and 3.x only its own: harmless
  on a clean cut-over, fatal in a mixed window. See
  [A mixed 3.x and 4.0 window](operations.md#a-mixed-3-x-and-4-0-window).
* **The formerly optional columns are required.** Schema validation refuses an unmigrated schema at
  startup, naming the missing column and the migration script. With `SchemaProvisioning.None` it fails
  later instead, at the first statement that names the column. See
  [The optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone).
* **The two stores now answer the same way** where they used to disagree, which changes the ADO store
  in six places. See [The two job stores answer the same way](#the-two-job-stores-answer-the-same-way).

### Lifecycle and telemetry

* **`Standby()` after `Shutdown()` throws** instead of doing nothing. See
  [The transitions are honest about themselves](#the-transitions-are-honest-about-themselves).
* **A trigger scheduled inside an `Activity` carries two extra job-data entries.**
  `QuartzSchedulerOptions.PropagateTraceContext` is on by default, and the two reserved keys show
  wherever trigger data does: `MergedJobDataMap`, the dashboard, `GET /triggers`. Turn it off with
  `q.ConfigureScheduler(options => options.PropagateTraceContext = false)`.

### The end time is the last instant at which a trigger may fire

On 4.x, `EndTimeUtc` is the last instant a trigger may fire, for every trigger type: a fire time equal
to it fires. This matches Java Quartz's `AbstractTrigger`. On 3.x the types disagreed:

| Trigger | 3.x | 4.x |
| --- | --- | --- |
| `SimpleTriggerImpl` | A fire time equal to `EndTimeUtc` is dropped | It fires |
| `CalendarIntervalTriggerImpl` | A fire time equal to `EndTimeUtc` is dropped | It fires |
| `DailyTimeIntervalTriggerImpl` | Fires *past* `EndTimeUtc` until the daily window closes, when the end time falls between two fire times; `FinalFireTimeUtc` reports the close of the daily window even when that is past `EndTimeUtc` | Firing stops at the end time, and `FinalFireTimeUtc` is never past it |
| `CronTriggerImpl` | A fire time equal to `EndTimeUtc` fires | Unchanged |
| `RecurrenceTriggerImpl` | New in 4.x | A fire time equal to `EndTimeUtc` fires |

So a simple or calendar-interval trigger whose `EndAt` lands exactly on a fire time fires once more
than on 3.x, and a daily-time-interval trigger whose `EndAt` falls between two fire times fires fewer.
To keep the old count, move the end time one second off the boundary.

`TriggerFireTimes.ComputeBetween` follows the same rule: its `to` is inclusive for every trigger type.

### Ordering

* **`JobKey` and `TriggerKey` compare ordinally**; 3.x used the current culture. Sorted key listings
  come out in a different order. See
  [`Key<T>` moved to `Quartz` and is immutable](#key-t-moved-to-quartz-and-is-immutable).

### Null arguments raise `ArgumentNullException`

**Every** `IScheduler` member throws `ArgumentNullException` for a null argument, reads included:
`GetJobDetail`, `GetTrigger`, `GetTriggerState`, `GetTriggersOfJob`, `GetCalendar`, the three `Exists`
overloads and the six `Query*` members. (beta.1 covered only the mutation members; a read handed a null
failed wherever it was first dereferenced, often inside the store.)

* The mutation members name the parameter. 3.x raised `SchedulerException("JobDetail cannot be null")`
  from `ScheduleJob(null, trigger)` and similar, so `catch (SchedulerException)` around a scheduling
  call also swallowed the caller's own bug. `SchedulerQueryExtensions` already behaved the new way.
* `RescheduleJob` and `CronExpression.ResolveHash`'s two overloads raised `ArgumentException`.
  `ArgumentNullException` derives from it, so an existing `catch` still catches.
* The `<exception>` tags on `IScheduler` list which members refuse what: 97 of them, against 2 over 65
  members on 3.x.

### Between the alphas and beta.1

Corrections between 4.0 pre-releases are written into the sections above and below as 3.x against
4.0. The per-build list, with what an application on a pre-release must change, is in
[Appendix: if you ran a 4.0 pre-release](#appendix-if-you-ran-a-4-0-pre-release).

### What is on nobody's list because it did not change

Checked against both versions and identical:

* every misfire instruction's numeric value, so a carried-over `MISFIRE_INSTR` column means the same;
* `TriggerState`'s existing values (`Executing` is appended);
* the misfire computation of every trigger family;
* the merge order and content of `MergedJobDataMap`;
* `[PersistJobDataAfterExecution]` writes back only a modified map;
* the lock handler a store picks for itself;
* `StringOperator`'s matching;
* the effective default of `Shutdown(waitForJobsToComplete)`.

## Configuration

This is the largest change in 4.x. Configuration is strongly typed options and service registrations
instead of a bag of `quartz.*` strings, and the dependency injection container builds the scheduler.

### Flat keys still work

`quartz.*` keys from `appsettings.json` or a `NameValueCollection` keep working. They are translated
into the typed options, and both spellings of a setting give the same result. You do not have to
migrate configuration files to move to 4.x.

### A flat property bag is checked for keys nobody reads

`AddQuartz(services, properties)` and `AddQuartz(services, name, properties)` now check the bag as
`QuartzSchedulerBuilder.UseProperties` always did:

* A `quartz.*` key Quartz does not read throws `SchedulerConfigException` at registration. For example,
  `quartz.jobstore.type` (for `quartz.jobStore.type`) used to silently give you an in-memory store.
* A key 4.0 stopped reading is reported by name, with its replacement.
* Set `quartz.checkConfiguration` to `false` to allow keys of your own.
* The `IConfiguration` overloads are not checked, because flattening a section invents `quartz.*` keys
  whether Quartz reads them or not.

### A property key without the `quartz.` prefix is refused

`services.Configure<QuartzOptions>(…)` was not checked at all. `QuartzOptions.Properties` holds flat
`quartz.*` keys and `QuartzPropertyBridge` is its only reader, so a key without the prefix is read by
nobody and has no symptom. An `IValidateOptions<QuartzOptions>` now refuses one at host startup:

```diff
  services.Configure<QuartzOptions>(options =>
  {
-     options.Properties["scheduler.instanceName"] = "svc";
+     options.Properties["quartz.scheduler.instanceName"] = "svc";
  });
```

The usual cause is `services.Configure<QuartzOptions>(section)` on a section written for the 3.x
`QuartzOptions`, which was a dictionary: `Quartz:Properties:scheduler.instanceName` arrives as
`scheduler.instanceName`. The check cannot catch a section whose keys match no `QuartzOptions`
property, because that binds nothing. Bind the section with `AddQuartz(configuration)` instead.

This check does **not** compare keys against the ones Quartz reads, because a flattened section can
hold settings for something else. `quartz.checkConfiguration = false` turns it off too.

### A property bag is any dictionary

The property bag parameter is now `IEnumerable<KeyValuePair<string, string?>>`, the shape of
`Dictionary<string, string?>`, `IReadOnlyDictionary<string, string?>`, `QuartzOptions.Properties` and
`AddInMemoryCollection`'s argument.

```csharp
services.AddQuartz(new Dictionary<string, string?>
{
    ["quartz.scheduler.instanceName"] = "core",
    ["quartz.threadPool.maxConcurrency"] = "10"
});
```

A `NameValueCollection` (what a 3.x application passed to `StdSchedulerFactory`) still goes in
unchanged: `UseProperties` and `AddQuartz` keep an overload for it.

| Member | 3.x / earlier 4.0 preview | 4.0 |
|---|---|---|
| `QuartzSchedulerBuilder.UseProperties` | `UseProperties(NameValueCollection)` | `UseProperties(IEnumerable<KeyValuePair<string, string?>>)`, plus the `NameValueCollection` overload |
| `AddQuartz(services, properties, …)` | `NameValueCollection` | `IEnumerable<KeyValuePair<string, string?>>`, plus the `NameValueCollection` overload |
| `AddQuartz(services, name, properties, …)` | `NameValueCollection` | `IEnumerable<KeyValuePair<string, string?>>`, plus the `NameValueCollection` overload |
| `QuartzOptions.ToNameValueCollection()` | returned a `NameValueCollection` | `QuartzOptions.ToProperties()`, returning a `Dictionary<string, string?>` that goes straight back into either of the above |

`KeyValuePair<TKey, TValue>` is a struct, so `IEnumerable<KeyValuePair<string, string>>` does not
convert to `IEnumerable<KeyValuePair<string, string?>>`. A `Dictionary<string, string>` needs one
conversion:

```diff
- services.AddQuartz(settings);
+ services.AddQuartz(settings.ToDictionary(x => x.Key, x => (string?) x.Value));
```

`AddQuartz(services, properties)` now copies the bag, as `UseProperties` always did. Before, a caller
that reused its collection could change the scheduler's configuration after `AddQuartz` returned.

### Interrupting jobs on shutdown is one setting with four answers

`QuartzSchedulerOptions.InterruptJobsOnShutdown` and `InterruptJobsOnShutdownWithWait` are replaced by
the enum `ShutdownJobInterruption`. (`InterruptJobsOnShutdown = true` meant "only when not waiting",
which was easy to misread.)

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

Configuration files need no change. Both flat keys still map onto the enum by the table above, and
`Scheduler:InterruptJobsOnShutdown: true` in `appsettings.json` still works through its flattened
`quartz.*` key. The new spelling is `Scheduler:ShutdownJobInterruption: "Always"`.

### A setting stops wearing a verb

`IPersistentStoreBuilder.AcceptEnlistedTransactions()` is gone. It only set one option, so set it
through `ConfigureStore`, like the other nineteen `AdoJobStoreOptions` settings:

```diff
  q.UsePersistentStore(store =>
  {
      store.UsePostgres(connectionString);
-     store.AcceptEnlistedTransactions();
+     store.ConfigureStore(options => options.AcceptEnlistedTransactions = true);
  });
```

The option, the key `quartz.jobStore.acceptEnlistedTransactions` and the section entry
`JobStore:AcceptEnlistedTransactions` are unchanged. `UseClustering()` keeps its verb because it sets
two things: `Enabled` on `ClusteringOptions` and `UseDbLocks` on `AdoJobStoreOptions`.

### Two names that said the wrong thing

| Was | Is | Why |
|---|---|---|
| `AdoJobStoreOptions.UseProperties` | `AdoJobStoreOptions.StoreJobDataAsStrings` | Collided with `QuartzSchedulerBuilder.UseProperties` and `AddQuartz(properties)`, which are about flat configuration keys |
| `Matchers.Group<TKey>(@operator, …)` / `Matchers.Name<TKey>(@operator, …)` | the parameter is `matchOperator` | `operator` is a C# keyword |

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

The flat key `quartz.jobStore.useProperties` still sets `StoreJobDataAsStrings`. In `appsettings.json`
the entry is now `JobStore:StoreJobDataAsStrings`; `JobStore:UseProperties` still works through its
flattened `quartz.*` key.

### The persistent store builder says which thing it configures

| 4.0 preview | 4.0 |
|---|---|
| `store.Configure(options => …)` | `store.ConfigureStore(options => …)` |
| `store.UseDataSourceName(name)` | `store.UseDataSource(name)` |
| — | `store.UseDriverDelegate(factory)`, new |

`UseDriverDelegate` now has the `Func<IServiceProvider, T>` overload that `UseConnectionProvider`,
`UseSerializer`, `UseLockHandler` and `UseTriggerPersistenceDelegate` already had, so a dialect can take
services from the container.

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

No configuration key moved: `quartz.jobStore.*`, `Quartz:JobStore:*` and `quartz.jobStore.dataSource`
are unchanged.

### The hosted service's extension point is its four hooks

`QuartzHostedService.StartAsync` and `StopAsync` are no longer `virtual`. An override that skipped the
base call left schedulers that nothing could shut down.

Override the four `virtual` hooks instead (requested in #2386 and #2522):

* work in `StartAsync` moves to `StartingAsync` or `StartedAsync`;
* work in `StopAsync` moves to `StoppingAsync` or `StoppedAsync`;
* `protected IReadOnlyList<IScheduler> Schedulers` is a snapshot of the schedulers the service runs.

### A shipped component is configured through its options type, and only there

The public setters on shipped components are now `internal`. Configure them through their options type:

| Component | Configure through |
|---|---|
| `RAMJobStore.MisfireThreshold` | `UseInMemoryStore(o => o.MisfireThreshold = …)` |
| The ADO.NET store's `MisfireThreshold` | `UsePersistentStore(store => store.ConfigureStore(o => o.MisfireThreshold = …))` |
| `TaskSchedulingThreadPool.MaxConcurrency`, `.Scheduler` | `UseDefaultThreadPool(maxConcurrency: …)`; both setters are `protected internal`, so a pool of your own can still set them |
| `RedisLockHandler.RedisConfiguration`, `.KeyPrefix`, `.LockTimeToLive`, `.LockRetryInterval` | `UseRedisLockHandler(o => …)` |
| The history plugins' message templates | `UseJobHistoryLogging(o => …)`, `UseTriggerHistoryLogging(o => …)`, and now `UseStructuredJobLogging(o => …)` / `UseStructuredTriggerLogging(o => …)` |
| The scheduling-data plugins' `FileNames`, `ScanInterval`, `FailOn*` (internal outright; see [one surface](#the-two-scheduling-data-plugins-have-one-surface)) | `UseXmlSchedulingConfiguration(o => …)` / `UseJsonSchedulingConfiguration(o => …)` |
| `ShutdownHookPlugin.CleanShutdown` | `AddQuartzHostedService(o => o.WaitForJobsToComplete = …)`; the plugin is gone, see [`ShutdownHookPlugin` is retired; the host already shuts the scheduler down](#shutdownhookplugin-is-retired-the-host-already-shuts-the-scheduler-down) |

* The row-lock handlers' retry settings are `init`-only: `SelectForUpdateLockHandler` has `MaxRetry` and
  `RetryPeriod`, and `UpdateRowLockHandler` gained `RetryPeriod` (it was hard-coded to a second). Set
  them at construction or through `quartz.jobStore.lockHandler.maxRetry` / `.retryPeriod`.
* **Flat-key configuration is unaffected.** `quartz.plugin.<name>.<property>` and
  `quartz.jobStore.lockHandler.<property>` set the component through reflection, which now binds
  non-public setters.
* `UseStructuredJobLogging` and `UseStructuredTriggerLogging` gain a `configure` delegate.

Two collections are no longer assignable, so one `configure` callback cannot discard another's
additions. The configuration binder still binds into them.

```diff
- x.Files = ["~/quartz_jobs.xml"];
+ x.Files.Add("~/quartz_jobs.xml");

- options.Tags = ["ready", "live"];
+ options.Tags.AddRange(["ready", "live"]);
```

`FileSchedulingOptions.Files` is a `List<string>` rather than a `string[]`, and
`QuartzHealthCheckOptions.Tags` a `List<string>` rather than an `IReadOnlyCollection<string>`.

### The provider names have constants

The eight driver names Quartz ships for `DataSourceOptions.Provider` are constants:
`DataSourceOptions.Providers.SqlServer`, `.Npgsql`, `.MySql`, `.MySqlConnector`, `.Oracle`, `.Sqlite`,
`.SystemDataSqlite`, `.Firebird`. The `UseSqlServer` / `UsePostgres` / … extensions use them.

The property stays a `string`, because `UseGenericDatabase` can describe other drivers. The values are
unchanged, so configuration files are unaffected.

### The standalone builder reads a configuration section

`QuartzSchedulerBuilder.UseConfiguration(IConfiguration)` is new. Like `AddQuartz(configuration)`, it
binds the typed options, reads a `Schedule` section and translates flat `quartz.*` keys.

```diff
- var properties = QuartzConfigurationHelper.ToNameValueCollection(configuration.GetSection("Quartz"));
- var factory = QuartzSchedulerBuilder.Create().UseProperties(properties).Build();
+ var factory = QuartzSchedulerBuilder.Create().UseConfiguration(configuration.GetSection("Quartz")).Build();
```

`QuartzConfigurationHelper` is internal. `UseProperties(NameValueCollection)` is unchanged, for a bag you
built yourself from a properties file or environment variables.

### Two scheduler thread settings were dead and are gone

The scheduling loop is a long-running `Task`, not a `Thread`, so these never did anything in 4.0:

| Removed | Use instead |
|---|---|
| `QuartzSchedulerOptions.ThreadName` / `quartz.scheduler.threadName` | nothing |
| `QuartzSchedulerOptions.MakeSchedulerThreadDaemon` / `quartz.scheduler.makeSchedulerThreadDaemon` | nothing for the scheduler; `AdoJobStoreOptions.UseBackgroundThreads` for the store's threads |

Both keys are in the removed-key table, so a properties bag that still has one gets an explanation, not
"unknown key".

The job store's setting is renamed:

| Before | After |
|---|---|
| `AdoJobStoreOptions.MakeThreadsDaemons` | `AdoJobStoreOptions.UseBackgroundThreads` |

The flat key `quartz.jobStore.makeThreadsDaemons` still sets it. It covers the misfire handler and the
cluster manager, the only real threads Quartz creates, so it alone decides whether Quartz's threads
keep a console application open.

### `PerformSchemaValidation` became `SchemaProvisioning`, which has a third position

A persistent store can now create its own schema
([#3531](https://github.com/quartznet/quartznet/issues/3531)), so validation and creation are one
three-valued setting:

| Before | After |
|---|---|
| `AdoJobStoreOptions.PerformSchemaValidation = true` | `AdoJobStoreOptions.SchemaProvisioning = SchemaProvisioning.Validate` (the default) |
| `AdoJobStoreOptions.PerformSchemaValidation = false` | `AdoJobStoreOptions.SchemaProvisioning = SchemaProvisioning.None` |
| — | `AdoJobStoreOptions.SchemaProvisioning = SchemaProvisioning.CreateIfMissing`, or `store.ProvisionSchema()` |

* The flat key `quartz.jobStore.performSchemaValidation` still works: `true` maps to `Validate`,
  `false` to `None`.
* The new key `quartz.jobStore.schemaProvisioning` takes `None`, `Validate` or `CreateIfMissing`,
  case-insensitively. If both keys are present, the new one wins.
* `CreateIfMissing` only creates; it never alters or drops. It is safe on an existing schema, but a
  mis-typed `TablePrefix` gets a second, empty schema.
* It is **not** an upgrade: a missing column is left missing. `database/migrations/` moves a schema
  forward, and the 3.x → 4.0 upgrade is still mandatory.
* It is not the default, because it needs DDL permission. Without that permission, the error names the
  fresh-install script for your database and tells you to run it and return to `Validate`.

`IDriverDelegate` gained the member behind it:

```csharp
ValueTask CreateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);
```

A delegate deriving from `StdAdoDelegate` inherits an implementation that runs an embedded script,
named by overriding `protected virtual string? SchemaResourceName`. The lookup walks the base chain
nearest first: a delegate deriving from a shipped one inherits that dialect's script, and one that
overrides the name wins. A delegate with no script in its chain throws when asked to provision.

### Code-first configuration is typed

Settings that were write-only properties on the configurator are now options:

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

The option names match the configuration keys: `Quartz:Scheduler:MaxBatchSize` and
`options.MaxBatchSize` are the same setting.

### Data sources no longer need a name

A scheduler has one job store and so one database; the data source takes no name:

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

Schedulers that need different databases are registered under different names, each with its own
services.

### The quartz.config file is no longer read

Nothing is loaded from disk. A `quartz.config` file next to the application, one named by the
`quartz.config` environment variable, and the embedded copy Quartz used to ship are all ignored.
Configure a scheduler with properties passed to `QuartzSchedulerBuilder.UseProperties`, an
`IConfiguration` section passed to `AddQuartz`, or code.

The three values the embedded file supplied (`quartz.scheduler.instanceName`,
`quartz.threadPool.threadCount`, `quartz.jobStore.misfireThreshold`) are no longer defaults; see
[Defaults that changed](#defaults-that-changed) for the old and new values. Only
`StdSchedulerFactory.Initialize()` ever read the file; see
[`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone). Set the values explicitly if you want
the old ones.

To describe an ADO.NET driver Quartz ships no metadata for, use the code-first form below. The
`quartz.dbprovider.*` keys still work through `IConfiguration` or a `NameValueCollection`.

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

See [the configuration reference](configuration/reference.md#describing-a-driver-quartz-does-not-know).
`DbProvider.RegisterDbMetadata` is gone with the process-wide lookup it wrote into; use the callback
above, once per driver.

### `QuartzOptions` is no longer a dictionary

`QuartzOptions` used to derive from `Dictionary<string, string?>` and hold jobs and triggers as well as
flat keys. The keys moved to a `Properties` dictionary, and jobs and triggers are per-scheduler
registrations.

```diff
  services.Configure<QuartzOptions>(options =>
  {
-     options["quartz.plugin.jobHistory.type"] = "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins";
+     options.Properties["quartz.plugin.jobHistory.type"] = "Quartz.Plugins.History.LoggingJobHistoryPlugin, Quartz.Plugins";
  });
```

A plugin whose type you know is better added directly, which also gives it constructor injection:

```csharp
services.AddQuartz(q => q.AddPlugin<LoggingJobHistoryPlugin>());
```

A section of flat keys no longer binds onto `QuartzOptions` (it would bind `Quartz:Properties:*`).
Pass the section to `AddQuartz`, which reads the keys and binds the typed options from it:

```diff
- services.Configure<QuartzOptions>(configuration.GetSection("Quartz"));
  services.AddQuartz(configuration.GetSection("Quartz"), q => { /* ... */ });
```

Add jobs and triggers through the builder. The overloads taking an `IServiceProvider` replace the
options callback:

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

`QuartzOptions.SchedulerName` read and wrote `schedName`, an ADO.NET column key that nothing reads, so
a name set through it was silently ignored. It is gone with `SchedulerId` and `MisfireThreshold`; see
[`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings).

### Removed

| Removed | Use instead |
|---|---|
| `QuartzOptions : Dictionary<string, string?>` | `QuartzOptions.Properties` |
| `QuartzOptions.JobDetails`, `.Triggers`, `.AddJob`, `.AddTrigger` | `AddQuartz(q => q.AddJob(…))` / `q.AddTrigger(…)` |
| `StdSchedulerFactory` and its 47 constants | `QuartzSchedulerBuilder.UseProperties(properties)`; see [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone) |
| `StdSchedulerFactory.PropertySchedulerName` | nothing; it named an ADO.NET column, not a setting |
| `SchedulerBuilder` | `QuartzSchedulerBuilder` for standalone use, `AddQuartz` under a host |
| `DirectSchedulerFactory` | `QuartzSchedulerBuilder`, with `UseThreadPool(IThreadPool)` / `UseJobStore(IJobStore)` for pre-built parts |
| `IPropertyConfigurer`, `IPropertySetter`, `IPropertyConfigurationRoot`, `PropertiesHolder`, `PropertiesSetter` | typed options |
| `SchedulerBuilder.UseZeroSizeThreadPool()` | `UseThreadPool<ZeroSizeThreadPool>()`; the pool type is still public |
| `SchedulerBuilder.UseDedicatedThreadPool()` | `UseDefaultThreadPool(…)`; `DedicatedThreadPool` is internal. See [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `SchedulerPluginConfigurationExtensions.UsePlugin<T>(name)` | `AddPlugin<T>(name)` on the builder; see [Plugins are registered like listeners](#plugins-are-registered-like-listeners) |
| `SchedulerPluginConfigurationExtensions.TryRegisterSingleton<TService, TImplementation>()` | `builder.Services.TryAddSingleton<TService, TImplementation>()` |
| `AddQuartz(Action<configurator, IServiceProvider>)` | see [Deferred configuration](#deferred-configuration) |
| `quartz.config` file discovery, `StdSchedulerFactory.PropertiesFile` | `IConfiguration`, or properties passed to `QuartzSchedulerBuilder.UseProperties` |
| `DbProvider.RegisterDbMetadata` | the metadata factory on `UseGenericDatabase` |
| `quartz.scheduler.proxy*`, `quartz.scheduler.exporter*` | nothing; remoting is not supported on modern .NET |
| `quartz.jobListener.<name>.*`, `quartz.triggerListener.<name>.*` | `AddJobListener<T>(matchers)` / `AddTriggerListener<T>(matchers)`; the keys are rejected, not ignored. See [The listener property keys are retired](#the-listener-property-keys-are-retired) |
| `QuartzOptions.SchedulerName`, `.SchedulerId`, `.MisfireThreshold` | the typed options; see [`QuartzOptions` lost its three typed settings](#quartzoptions-lost-its-three-typed-settings) |
| `IPersistentStoreBuilder.UseDataSourceConnectionProvider()` | `DataSourceOptions.UseRegisteredDataSource` |
| `AdoJobStoreOptions.Clustered`, `.ClusterCheckinInterval`, `.ClusterCheckinMisfireThreshold` | `ClusteringOptions`; see [Clustering is configured in one place](#clustering-is-configured-in-one-place) |
| `SchedulerRepository.Instance` | `ISchedulerRepository` resolved from the container |
| `DBConnectionManager.Instance` | nothing; register a provider with `UseConnectionProvider` and resolve `IDbProvider` from the container. See [The connection manager is gone](#the-connection-manager-is-gone) |
| `StdSchedulerFactory.GetDbConnectionManager()`, `.GetSchedulerRepository()` | `IDbProvider`, keyed by scheduler name / `ISchedulerRepository`, both resolved from the container |

### Deferred configuration

The `AddQuartz` overloads taking an `IServiceProvider` are gone. They ran the callback against a
throwaway container. Instead, ask for the service where the value is used, when the real container
exists.

**A value that depends on a service:** configure the option from the service, through the options
pattern.

```diff
- services.AddQuartz((q, provider) => q.UsePersistentStore(store =>
- {
-     store.UseSqlServer(connectionString);
-     store.TablePrefix = provider.GetRequiredService<IMyService>().TablePrefix;
- }));
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseSqlServer(connectionString)));
+ services.AddOptions<AdoJobStoreOptions>()
+     .Configure<IMyService>((options, service) => options.TablePrefix = service.TablePrefix);
```

Use this route by default. Every option a scheduler reads is resolved after the container is built,
so `Configure`, `PostConfigure` and `IValidateOptions` have all run. Two silent mistakes to avoid:

* **A scheduler's options are its own named instance.** `AddQuartz(q => …)` configures the unnamed one,
  which reaches only the default scheduler. `AddQuartz("reporting", q => …)` is configured with
  `services.AddOptions<AdoJobStoreOptions>("reporting")`.
* **Reading `IConfiguration` yourself skips the options pipeline.** `builder.Configuration.GetSection("…")
  .Get<MyOptions>()` runs no `Configure<MyOptions>`, `PostConfigure<MyOptions>` or
  `IValidateOptions<MyOptions>`. Use it for a literal from configuration, not for a value the
  application computes:

  ```diff
  - var settings = builder.Configuration.GetSection("Jobs").Get<JobOptions>()!;
  - services.AddQuartz(q => q.UseExecutionLimits(l => l.ForGroup("reports", settings.MaxConcurrent)));
  + services.AddQuartz(q => q.UseExecutionLimits((provider, l) => l.ForGroup(
  +     "reports",
  +     provider.GetRequiredService<IOptions<JobOptions>>().Value.MaxConcurrent)));
  ```

**A registration that depends on a service:** every builder member that builds something has an
overload that is handed the container, so the service is resolved when the thing is built.

| Member | The shape that is given a container |
|---|---|
| `AddJob`, `AddTrigger`, `ScheduleJob` | `q.AddTrigger((provider, t) => …)` |
| `AddCalendar` | `q.AddCalendar("name", provider => …)` |
| `UseJobStore` | `q.UseJobStore(provider => …)` |
| `AddPlugin`, `AddSchedulerListener`, `AddJobListener`, `AddTriggerListener`, `AddJobMiddleware` | `q.AddPlugin(provider => …)` |
| `UseExecutionLimits` | `q.UseExecutionLimits((provider, limits) => …)` |
| `AddJobTimeout` | `q.AddJobTimeout(provider => …)` |

Listeners, plugins and middleware rarely need this: they are registered services and get their
dependencies through their constructors.

### SPI changes

A custom `IJobStore` or `ISchedulerPlugin` takes its collaborators through its constructor.

`IJobStore` loses `InstanceId`, `InstanceName`, `ThreadPoolSize` and `TimeProvider`, and `Initialize`
takes the scheduler's identity instead of collaborators:

```diff
- ValueTask Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default);
+ ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default);
```

Take `ISchedulerSignaler`, `ITypeLoader`, `TimeProvider` or `IOptions<QuartzSchedulerOptions>` through
the constructor. The identity cannot come through a constructor; see
[If you implement `IJobStore`](#if-you-implement-ijobstore). `Initialize` keeps only work that must
happen before the scheduler runs and cannot be done in the constructor, such as verifying a database
schema.

Options arrive as the scheduler's own through `IOptions<>`, `IOptionsMonitor<QuartzSchedulerOptions>`
and `IOptionsSnapshot<QuartzSchedulerOptions>`: `CurrentValue` and `Value` are your scheduler's options,
`Get(name)` answers for the name you pass, and `OnChange` reports only your scheduler's changes.

Plugin configuration extension methods extend `IQuartzBuilder` and register the plugin as a service,
instead of deriving from `PropertiesSetter` to write string keys.

### No process-global scheduler or connection state

`SchedulerRepository.Instance` and `DBConnectionManager.Instance` are gone. The repository is a
container registration, so **a scheduler is only visible in the repository of the container that built
it**:

```diff
- var scheduler = SchedulerRepository.Instance.Lookup("reporting");
+ var scheduler = serviceProvider.GetRequiredService<ISchedulerRepository>().Lookup("reporting");
```

The connection manager is removed outright. Register a connection provider through the store
configuration, and resolve it from the container:

```diff
- DBConnectionManager.Instance.AddConnectionProvider("default", myProvider);
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider(_ => myProvider)));
```

See [The connection manager is gone](#the-connection-manager-is-gone).

As a result, a scheduler registered with `AddQuartz` and one built by a `QuartzSchedulerBuilder` in the
same process do not see each other:

* `ISchedulerFactory.GetAllSchedulers()` on either lists only its own schedulers.
* `ISchedulerFactory.LookupScheduler(name)` returns `null` for the other's name.
* `ISchedulerRepository.Lookup(name)` sees only its own container's schedulers.

To reach a scheduler from code that has no reference to its factory, register it with `AddQuartz` and
inject `IScheduler`, `ISchedulerFactory` or `ISchedulerRepository`. To share one repository across
several entry points, register your own before calling `AddQuartz`; every Quartz registration is
`TryAdd`, so yours wins:

```csharp
var repository = new SchedulerRepository();
services.AddSingleton<ISchedulerRepository>(repository);
services.AddQuartz(/* ... */);
```

A `QuartzSchedulerBuilder` owns its container, so its repository holds only the schedulers it built.
Instead of `StdSchedulerFactory.GetSchedulerRepository()` and `GetDbConnectionManager()`, resolve
`ISchedulerRepository` from the container, and a scheduler's `IDbProvider` keyed by the scheduler's
name.

## `StdSchedulerFactory` is gone

The properties-based factory has been removed, along with its 47 public constants. Since 4.0 it only
forwarded to the container path that `AddQuartz` uses.

Flat `quartz.*` keys are **not** going away; hand them to `QuartzSchedulerBuilder` instead:

```diff
- ISchedulerFactory factory = new StdSchedulerFactory(properties);
- IScheduler scheduler = await factory.GetScheduler();
+ IScheduler scheduler = await QuartzSchedulerBuilder.Create()
+     .UseProperties(properties)
+     .BuildScheduler();
```

`UseProperties` uses the same translator, so every key means what it did, including the
`quartz.checkConfiguration` check that rejects a misspelled key. Configuration written in code wins
over the properties in either order: property-derived options are applied first, and implementations
the properties name are registered after (options are last-wins, registrations first-wins, as with
`AddQuartz`).

Two behaviours lived in `Initialize()` and are gone:

* **The `quartz.*` environment-variable overlay.** `new StdSchedulerFactory()` read every `quartz.*`
  environment variable. Nothing does now. Use `IConfiguration` with `AddEnvironmentVariables()` and
  pass the section to `AddQuartz`, or flatten it into the `NameValueCollection` you hand to
  `UseProperties`.
* **The embedded `quartz.config` defaults** (`instanceName = DefaultQuartzScheduler`,
  `threadCount = 10`, `misfireThreshold = 60000`). A builder with no properties uses the typed
  defaults; see [Defaults that changed](#defaults-that-changed). Set them explicitly to keep the old
  values. `new StdSchedulerFactory(properties)` never read the file.

`IsSupportedConfigurationKey` is gone, so you can no longer allow keys of your own by subclassing the
factory. Set `quartz.checkConfiguration` to `false` instead.

`GetDefaultScheduler()` is gone: there is no process-wide scheduler any more (see
[No process-global scheduler or connection state](#no-process-global-scheduler-or-connection-state)).
Build one scheduler where the application starts and hold it, or register it with `AddQuartz` and
inject `IScheduler`.

### Every removed constant

The key strings are unchanged. Search this table for the constant you used:

| Removed constant | Key | Typed equivalent |
|---|---|---|
| `PropertySchedulerInstanceName` | `quartz.scheduler.instanceName` | `QuartzSchedulerOptions.InstanceName` |
| `PropertySchedulerInstanceId` | `quartz.scheduler.instanceId` | `QuartzSchedulerOptions.InstanceId` |
| `PropertySchedulerInstanceIdGeneratorPrefix` | `quartz.scheduler.instanceIdGenerator` | constructor injection into your `IInstanceIdGenerator` |
| `PropertySchedulerInstanceIdGeneratorType` | `quartz.scheduler.instanceIdGenerator.type` | register `IInstanceIdGenerator` in the container |
| `PropertySchedulerThreadName` | `quartz.scheduler.threadName` | nothing; the key is rejected, not ignored. The scheduling loop is a `Task`, not a `Thread` |
| `PropertySchedulerBatchTimeWindow` | `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow` | `QuartzSchedulerOptions.BatchTriggerAcquisitionFireAheadTimeWindow` |
| `PropertySchedulerMaxBatchSize` | `quartz.scheduler.batchTriggerAcquisitionMaxCount` | `QuartzSchedulerOptions.MaxBatchSize` |
| `PropertySchedulerExporterPrefix` | `quartz.scheduler.exporter` | nothing; remoting is not supported on modern .NET |
| `PropertySchedulerExporterType` | `quartz.scheduler.exporter.type` | nothing; see above |
| `PropertySchedulerProxy` | `quartz.scheduler.proxy` | `Quartz.HttpClient` for talking to a remote scheduler |
| `PropertySchedulerProxyType` | `quartz.scheduler.proxy.type` | `Quartz.HttpClient`; the key is rejected, not ignored |
| `PropertySchedulerIdleWaitTime` | `quartz.scheduler.idleWaitTime` | `QuartzSchedulerOptions.IdleWaitTime` |
| `PropertySchedulerMakeSchedulerThreadDaemon` | `quartz.scheduler.makeSchedulerThreadDaemon` | nothing; the key is rejected, not ignored. For the store's misfire and cluster threads use `quartz.jobStore.makeThreadsDaemons` / `AdoJobStoreOptions.UseBackgroundThreads` |
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
| `PropertyJobStoreType` | `quartz.jobStore.type` | `UseInMemoryStore()`; `UsePersistentStore()`, with `UseAmbientTransactions()` inside it for 3.x's `JobStoreCMT`; `UsePersistentStore<T>()` for your own persistent store; or `UseJobStore(instance)` |
| `PropertyJobStoreDbRetryInterval` | `quartz.jobStore.dbRetryInterval` | `AdoJobStoreOptions.DbRetryInterval` |
| `PropertyJobStoreLockHandlerPrefix` | `quartz.jobStore.lockHandler` | constructor injection into your `ILockHandler` |
| `PropertyJobStoreLockHandlerType` | `quartz.jobStore.lockHandler.type` | `UseLockHandler<T>()` |
| `PropertyTablePrefix` | `tablePrefix` (under `quartz.jobStore`) | `AdoJobStoreOptions.TablePrefix` |
| `PropertyDataSourcePrefix` | `quartz.dataSource` | `DataSourceOptions`, bound from `Quartz:DataSource:<name>` |
| `PropertyDataSourceProvider` | `provider` (under a data source) | `DataSourceOptions.Provider` |
| `PropertyDataSourceConnectionString` | `connectionString` (under a data source) | `DataSourceOptions.ConnectionString` |
| `PropertyDataSourceConnectionStringName` | `connectionStringName` (under a data source) | `DataSourceOptions.ConnectionStringName` |
| `PropertyDbProvider` | `quartz.dbprovider` | the metadata factory on `UseGenericDatabase` |
| `PropertyDbProviderType` | `connectionProvider.type` (under a data source) | `UseConnectionProvider<T>()`, or `DataSourceOptions.UseRegisteredDataSource`; the key is still read |
| `PropertyExecutionLimitPrefix` | `quartz.executionLimit` | `UseExecutionLimits(limits => …)` |
| `PropertyPluginPrefix` | `quartz.plugin` | `AddPlugin<T>()` |
| `PropertyPluginType` | `type` (under a plugin) | `AddPlugin<T>()` |
| `PropertyJobListenerPrefix` | `quartz.jobListener` | `AddJobListener<T>(matchers)`; the key is rejected, not ignored. See [The listener property keys are retired](#the-listener-property-keys-are-retired) |
| `PropertyTriggerListenerPrefix` | `quartz.triggerListener` | `AddTriggerListener<T>(matchers)`; the key is rejected, not ignored |
| `PropertyListenerType` | `type` (under a listener) | the two `Add*Listener<T>` methods above |
| `PropertyCheckConfiguration` | `quartz.checkConfiguration` | still read, by `QuartzSchedulerBuilder.UseProperties` |
| `PropertyObjectSerializer` | `quartz.serializer` | `UseSerializer<T>()`, `UseSystemTextJsonSerializer()` |
| `PropertyThreadExecutor` | `quartz.threadExecutor` | nothing; the thread pool is asynchronous and has no thread executor |
| `PropertyThreadExecutorType` | `quartz.threadExecutor.type` | nothing; see above |
| `DefaultInstanceId` | `NON_CLUSTERED` | `QuartzSchedulerOptions.DefaultInstanceId`, the same string |
| `AutoGenerateInstanceId` | `AUTO` | `QuartzSchedulerOptions.GenerateInstanceId` |
| `SystemPropertyAsInstanceId` | `SYS_PROP` | keep setting `quartz.scheduler.instanceId` to `SYS_PROP`: it still reads the id from the `quartz.scheduler.instanceId` environment variable. That generator is internal; to change the behaviour, register your own `IInstanceIdGenerator` |

Three constants were already gone before this release but are likely in a 3.x configuration:

| Removed constant | Value | Replacement |
|---|---|---|
| `ConfigurationSectionName` | `quartz` | the `<quartz>` Full Framework configuration section is not read; use `IConfiguration` |
| `PropertiesFile` | `quartz.config` | nothing is read from disk; see [The quartz.config file is no longer read](#the-quartz-config-file-is-no-longer-read) |
| `PropertySchedulerName` | `schedName` | nothing; it named an ADO.NET column rather than a setting |

### Every removed member

A 3.x application that subclassed the factory usually wanted one of these override points:

| Removed member | Replacement |
|---|---|
| `StdSchedulerFactory()` | `QuartzSchedulerBuilder.Create()` |
| `StdSchedulerFactory(NameValueCollection)` | `QuartzSchedulerBuilder.Create().UseProperties(properties)` |
| `Initialize(NameValueCollection)` | `UseProperties(properties)` |
| `Initialize()` | nothing; there is no file or environment overlay to read |
| `GetScheduler()`, `LookupScheduler(name)`, `GetAllSchedulers()` | unchanged apart from the by-name rename; they are `ISchedulerFactory`, which `Build()` returns |
| `static GetDefaultScheduler()` | build a scheduler where the application starts and hold it, or inject `IScheduler` |
| `Dispose()`, `Dispose(bool)` | dispose the `StandaloneSchedulerFactory` that `Build()` returns; it owns its container and implements `IDisposable` and `IAsyncDisposable` |
| `GetSchedulerRepository()` | `ISchedulerRepository`, resolved from the container |
| `GetDBConnectionManager()` (3.x) | nothing; the container is the provider registry. See [The connection manager is gone](#the-connection-manager-is-gone) |
| `GetNamedConnectionString(string)` (3.x) | `DataSourceOptions.ConnectionStringName`, resolved from `IConfiguration`'s connection strings |
| `Instantiate(QuartzSchedulerResources, QuartzScheduler)` (3.x) | nothing; both types are internal and the container builds the graph |
| `InstantiateType<T>(Type?)` (3.x) | register the implementation in the container |
| `IsSupportedConfigurationKey(string)` | set `quartz.checkConfiguration` to `false` to allow keys of your own |
| `LoadType(string?)` | `ITypeLoader`, selected with `UseTypeLoader<T>()` |
| `ValidateConfiguration()` (3.x) | `quartz.checkConfiguration` for the keys, and `IValidateOptions<T>` for the typed options |

## The builder surface says each thing once

Three members duplicated a neighbour and are removed:

| Removed | Say it with |
|---|---|
| `IScheduler.ScheduleJob(IJobDetail, ITrigger, CancellationToken)` and `ScheduleJob(ITrigger, CancellationToken)` | the overloads taking `ScheduleJobOptions options`, which now defaults, so `ScheduleJob(job, trigger)` still compiles. A **token passed positionally** is now a compile error: write `ScheduleJob(job, trigger, cancellationToken: token)` |
| `IServiceCollection.AddQuartzHealthChecks(Action<QuartzHealthCheckOptions>?)` | `services.AddHealthChecks().AddQuartz(configure)`, which it forwarded to. `IQuartzBuilder.AddQuartzHealthChecks()` stays, because the scheduler's builder already knows the scheduler |
| `JobBuilder<TJob>.OfType(string typeName)` | `OfType((JobType) typeName)`. The conversion is **explicit** on purpose: the name is not validated and fails at the first use of `JobType.Type` |

`ScheduleJobOptions.Replace` now picks the store path, not the overload. Not replacing calls
`IJobStore.ScheduleJob` as before, with the same span and metric names. Replacing calls
`IJobStore.ScheduleJobs`, because overwriting a job and its trigger must be one operation under one
lock. So an explicit `new ScheduleJobOptions()` now takes the plain path: same store outcome, better
span name.

`QuartzAspireSettings.ProvisionSchema` became `SchemaProvisioning`, of type `SchemaProvisioning?`; see
the [`Quartz.Aspire` is new](#quartz-aspire-is-new) settings table.

## The standalone builder is the same builder

`QuartzSchedulerBuilder.Create` takes the same callback as `AddQuartz` and hands it the same
`IQuartzBuilder`. Use `AddQuartz` when the application has a container and `QuartzSchedulerBuilder` when
it does not; the callback reads the same.

```diff
- var scheduler = await QuartzSchedulerBuilder.Create()
-     .Configure(q =>
-     {
-         q.UsePersistentStore(store => store.UseSqlServer(connectionString));
-         q.AddJob<ReportJob>(j => j.WithIdentity("report"));
-     })
-     .UseDefaultThreadPool(maxConcurrency: 20)
-     .BuildScheduler();
+ IScheduler scheduler = await QuartzSchedulerBuilder
+     .Create(q => q
+         .UseDefaultThreadPool(maxConcurrency: 20)
+         .UsePersistentStore(store => store.UseSqlServer(connectionString))
+         .AddJob<ReportJob>(j => j.WithIdentity("report").StoreDurably()))
+     .BuildScheduler();
```

The callback has everything on `IQuartzBuilder` (jobs, triggers, calendars, listeners, plugins,
execution limits) and every extension a package adds. The builder itself declares only
`Create(configure)`, `Build()`, `BuildScheduler()`, `UseConfiguration(IConfiguration)` and the two
`UseProperties` overloads.

The callback is optional, as with `AddQuartz()`: a scheduler configured entirely from configuration or
flat keys is `Create().UseConfiguration(section)` or `Create().UseProperties(properties)`.

`Services` and `SchedulerName` belong to `IQuartzBuilder`: read them as `q.Services` and
`q.SchedulerName`. If a 4.0 pre-release let you chain calls directly on `QuartzSchedulerBuilder`, see
[Appendix: if you ran a 4.0 pre-release](#appendix-if-you-ran-a-4-0-pre-release).

### `Build()` returns something you can dispose

`Build()` returns `StandaloneSchedulerFactory`, an `ISchedulerFactory` that is also `IAsyncDisposable`
and `IDisposable`. It used to return `ISchedulerFactory`, so disposing it needed a cast:

```diff
- ISchedulerFactory factory = builder.Build();
- using IDisposable container = (IDisposable) factory;
+ await using StandaloneSchedulerFactory factory = builder.Build();
```

Prefer `await using`: disposal shuts the scheduler down, which `Dispose()` can only block on. If you
never dispose it, the scheduler runs until the process ends, as before.

Two members moved from the standalone builder onto `IQuartzBuilder`, so a scheduler registered with
`AddQuartz` can also be given a pre-built part:

| Member | Meaning |
|---|---|
| `UseThreadPool(IThreadPool)` | uses a pool the caller constructed |
| `UseJobStore(IJobStore)` | uses a store the caller constructed |

| Removed from `QuartzSchedulerBuilder` | Use instead |
|---|---|
| `Configure(Action<IQuartzBuilder>)` | `Create(Action<IQuartzBuilder>)`, the same callback |
| `ConfigureScheduler`, `UseDefaultThreadPool` ×2, `UseJobFactory(IJobFactory)`, `UseInMemoryStore` | the identical `IQuartzBuilder` members, inside the `Create` callback |

## `IScheduler` is `IAsyncDisposable`

`IScheduler` implements `IAsyncDisposable`, so `await using` can replace a `try`/`finally` that shuts it
down:

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

What disposing does depends on the instance:

| Instance | Disposing it |
|---|---|
| A local scheduler | `Shutdown(waitForJobsToComplete: false)`. It owns the execution it drives. |
| The `IScheduler` a container injects | disposes the scheduler it built, and does nothing if it never built one. |
| A `DelegatingScheduler` | forwards to the scheduler it wraps. |
| `HttpScheduler` | releases its own resources and **never** shuts the remote scheduler down. Call `Shutdown` for that. |

* Disposal is idempotent: disposing a scheduler that is already shut down does nothing.
* It does not wait for jobs. To let running jobs finish, call `Shutdown(waitForJobsToComplete: true)`
  first; disposing afterwards does nothing.
* `IScheduler` only *inherits* `IAsyncDisposable`. An earlier 4.0 preview redeclared `DisposeAsync` with
  `new ValueTask DisposeAsync()`. An `IScheduler` of your own implements
  `IAsyncDisposable.DisposeAsync`; the method you already have satisfies it.

### A container holding a scheduler is disposed asynchronously

This change can show up as a runtime error. `ServiceProvider.Dispose()` throws when a singleton it
created implements only `IAsyncDisposable`, so dispose a container that resolved an `IScheduler` with
`await using`:

```diff
- using var provider = services.BuildServiceProvider();
+ await using var provider = services.BuildServiceProvider();
```

Applications hosted by `IHost` need no change: the host disposes its container asynchronously, and
`AddQuartzHostedService` shuts the schedulers down first anyway. This affects containers built by hand,
mostly in tests.

## A scheduler's lifecycle is one value

`IScheduler.IsStarted`, `IScheduler.InStandbyMode` and `IScheduler.IsShutdown` are replaced by one
`SchedulerStatus Status`:

```csharp
public enum SchedulerStatus
{
    Unknown,        // a scheduler that could not be asked - a remote one that did not answer
    Created,        // built, never started
    Running,        // firing triggers
    Standby,        // started once, stood down, can be started again
    ShuttingDown,   // Shutdown() is running
    Shutdown        // down for good; a new scheduler replaces it, this one never runs again
}
```

Each call site has a mechanical replacement:

| 3.x | 4.x |
|---|---|
| `scheduler.IsStarted` | `scheduler.Status is not SchedulerStatus.Created` |
| `scheduler.InStandbyMode` | `scheduler.Status is SchedulerStatus.Created or SchedulerStatus.Standby or SchedulerStatus.ShuttingDown` |
| `scheduler.IsShutdown` | `scheduler.Status is SchedulerStatus.Shutdown` |

These are exact translations. Check two of them:

* `IsStarted` stayed `true` after a shutdown ("`Start` was called at some point"). Code that used it to
  mean "is running" should use `Status is SchedulerStatus.Running`.
* `InStandbyMode` was `true` for a never-started scheduler. That is now `Created`, which has no
  `RunningSince`.

`SchedulerMetadata` follows:

```diff
- if (metadata.Shutdown) { … }
- else if (metadata.InStandbyMode) { … }
- else if (metadata.Started) { … }
+ switch (metadata.Status) { … }
```

| 3.x | 4.x |
|---|---|
| `SchedulerMetadata.Started`, `.InStandbyMode`, `.Shutdown` | `required SchedulerStatus Status` |

`StdScheduler.GetMetadata` and `HttpScheduler.GetMetadata` now derive it the same way. Before, `Started`
meant "ever started" on one and "running now" on the other. `RunningSince` is unchanged.

### The transitions are honest about themselves

A transition that does not happen is no longer announced:

| Call | 3.x | 4.x |
|---|---|---|
| `Start()` on a scheduler that is already running | re-emits `SchedulerStarting` and `SchedulerStarted`, and tells the job store the scheduler resumed | does nothing |
| `Standby()` on a scheduler that is not running | emits `SchedulerInStandbyMode` and tells the job store it paused | does nothing; a never-started scheduler stays `Created` |
| `Standby()` on a scheduler that has shut down | silently pauses a scheduler that is already down | throws `SchedulerException` |
| `Shutdown()` | calls `Standby()` on the way down, so listeners hear `SchedulerInStandbyMode` before `SchedulerShuttingDown` | goes `Running`/`Standby`/`Created` → `ShuttingDown` → `Shutdown`, with no standby notification |

**Check your listeners for the last row** (the 3.x behaviour came from Java). A listener that used
`SchedulerInStandbyMode` to detect "stopped firing" should also handle `SchedulerShuttingDown`, which
every shutdown raises. A start, a standby and a shutdown now raise exactly:

`SchedulerStarting` → `SchedulerStarted` → `SchedulerInStandbyMode` → `SchedulerShuttingDown` →
`SchedulerShutdown`

* `Status` becomes `Shutdown` at the *end* of the shutdown, after the plugins and the job store are
  down. A scheduler draining its jobs reads `ShuttingDown` until then.
* It reads `Shutdown` even if a plugin or the job store threw on the way down, because a shutdown runs
  only once.
* A scheduler that is shutting down refuses work like a shut-down one, and
  `ISchedulerFactory.GetScheduler()` does not return it. `ISchedulerRepository` and the dashboard still
  list it while it drains.

### The health check reports the state it found

The health check used to read only `IsStarted`: a scheduler in standby was **healthy**, and a shut-down
one failed the store probe as if it were a connectivity problem. It now reports the state:

| `Status` | Health |
|---|---|
| `Running` | the store probe decides: `Healthy`, or `Unhealthy` when the store cannot be reached |
| `Standby` | `Degraded`: deliberate and reversible, so not a reason to take a node out of rotation |
| `Created`, with `AutoStart` left on | `Unhealthy`: the host should have started it |
| `Created`, with `AutoStart` set to `false` | `Degraded`: the application starts it itself |
| `ShuttingDown`, `Shutdown`, `Unknown` | `Unhealthy`, with a message naming the scheduler and the state |

* Registered for a default scheduler in a container with only named ones, the check no longer throws.
  It reports `Unhealthy` and says to call `AddQuartzHealthChecks()` on the scheduler's own builder.
* **A clustered scheduler is also checked for its last check-in.** A node whose cluster manager is
  stuck still fires and reports `Running` while its peers recover its triggers. A last check-in older
  than `QuartzHealthCheckOptions.ClusterCheckinTolerance` times the node's check-in interval reports
  `Degraded`, naming the node and how late it is. The tolerance is a `double`, default `3`; `null` or
  `0` turns it off. **Unclustered schedulers are not affected.**

## Clustering is configured in one place

`AdoJobStoreOptions` no longer has `Clustered`, `ClusterCheckinInterval` and
`ClusterCheckinMisfireThreshold`; they duplicated `UseClustering(…)` and `ClusteringOptions`, and the
two could disagree. `ClusteringOptions` is the one place, and the store only *reports* whether it is
clustered.

| Removed | Use instead |
|---|---|
| `AdoJobStoreOptions.Clustered` | `ClusteringOptions.Enabled` |
| `AdoJobStoreOptions.ClusterCheckinInterval` | `ClusteringOptions.CheckinInterval` |
| `AdoJobStoreOptions.ClusterCheckinMisfireThreshold` | `ClusteringOptions.CheckinMisfireThreshold` |

`ClusteringOptions`' two intervals are no longer nullable. `UseClustering()` with no arguments turns
clustering on and leaves them at their values.

Code-first configuration is unchanged:

```csharp
store.UseClustering(cluster =>
{
    cluster.CheckinInterval = TimeSpan.FromSeconds(10);
    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
});
```

The flat keys `quartz.jobStore.clustered`, `quartz.jobStore.clusterCheckinInterval` and
`quartz.jobStore.clusterCheckinMisfireThreshold` still work, and so does `JobStore:Clustered` in
`appsettings.json` (every section is also read as flat keys). New is the spelling that matches the
options type:

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

`AdoJobStoreOptions` validation no longer requires `UseDbLocks` when clustered. Every path that enables
clustering also enables database locking, and a store with its own lock handler (Redis, say) may leave
`UseDbLocks` off.

## The SQLite extension methods swapped names

::: warning
`UseSqlite` did not exist in 3.x and now means **Microsoft.Data.Sqlite**. The method that used to be
called `UseSQLite` (the legacy **System.Data.SQLite** driver) is now `UseSystemDataSqlite`. Changing
`UseSQLite` to `UseSqlite` compiles, runs, and silently swaps your ADO.NET provider. Read the table
before a case-insensitive find and replace.
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

The short name goes to the recommended driver, as with `UseMySql` and `UseMySqlConnector`, and as in
Entity Framework Core. Provider names (the value of a `quartz.dataSource.<name>.provider` key) are
unchanged, so configuration files need no change.

## A data source is defined, referred to, or handed over

`UseDataSourceConnectionProvider()` is gone. It only set `DataSourceOptions.UseRegisteredDataSource`,
and only worked when called in the right order.

```diff
  q.UsePersistentStore(store =>
  {
-     store.UsePostgres(db => db.Provider = "Npgsql");
-     store.UseDataSourceConnectionProvider();
+     store.UsePostgres(db => db.UseRegisteredDataSource = true);
  });
```

The remaining members:

| Member | Role |
|---|---|
| `UseDataSource(configure)` | **defines** a data source: the driver and how to reach the database. `UseSqlServer` and the other database methods are shorthands for it |
| `UseDataSource(name)` | **refers to** a data source by name, with settings registered elsewhere, such as a `Quartz:DataSource:<name>` section |
| `DataSourceOptions.UseRegisteredDataSource` | takes connections from the container's unkeyed `DbDataSource` instead of a connection string |
| `DataSourceOptions.DataSourceServiceKey` / `.DataSourceFactory` | the same, for a keyed `DbDataSource` or one the caller builds. Code only |
| `UseConnectionProvider<T>()` / `UseConnectionProvider(factory)` | **replaces** the connection provider, for connections Quartz cannot describe. The code form of `quartz.dataSource.<name>.connectionProvider.type` |

* `UseRegisteredDataSource`, `DataSourceServiceKey` and `DataSourceFactory` are `DataSourceOptions`
  settings, beside `ConnectionString` and `ConnectionStringName`. `UseRegisteredDataSource` wins over
  both.
* `UseConnectionProvider` is not a data source setting. It wins over whatever `UseSqlServer` and its
  siblings registered, in either call order, and names the store's data source itself, so it needs no
  `UseDataSource` call.

### `AddDataSourceProvider()` went with it

`AddDataSourceProvider()` (on the configurator) registered `DataSourceDbProvider` as the `IDbProvider`,
and `UseDataSourceConnectionProvider()` (on the store) named it as the connection provider. Both are
gone. `UseRegisteredDataSource` does the whole job: the data source builds its own
`DataSourceDbProvider` around the `DbDataSource` it resolves from the container.

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

You still register the `DbDataSource` yourself, for example with `services.AddNpgsqlDataSource(…)`.

`UseRegisteredDataSource` takes the container's one unkeyed `DbDataSource`, as 3.x's
`AddDataSourceProvider()` did. With several (a scheduler per tenant), name one with
`DataSourceServiceKey`; for a data source you build, use `DataSourceFactory`:

```csharp
services.AddNpgsqlDataSource(tenantA, serviceKey: "tenant-a");
services.AddQuartz("tenant-a", q => q.UsePersistentStore(store =>
    store.UsePostgres(db => db.DataSourceServiceKey = "tenant-a")));
```

Both can only be set from code, because a configuration binder cannot produce a service key or a
delegate. Both imply `UseRegisteredDataSource`, so neither needs a connection string.

On this path, commands are now created by the connection instead of from the driver description, so
they get what the `DbDataSource` configured (for `NpgsqlDataSource`: type mappers, logging and composite
type registrations). The connection-string path is unchanged.

## `QuartzOptions` lost its three typed settings

Three typed members of `QuartzOptions` duplicated settings that have typed options:

| Removed | Use instead |
|---|---|
| `QuartzOptions.SchedulerName` | `QuartzSchedulerOptions.InstanceName`, or `Properties["quartz.scheduler.instanceName"]` |
| `QuartzOptions.SchedulerId` | `QuartzSchedulerOptions.InstanceId`, or `Properties["quartz.scheduler.instanceId"]` |
| `QuartzOptions.MisfireThreshold` | `InMemoryJobStoreOptions.MisfireThreshold` / `AdoJobStoreOptions.MisfireThreshold` |

```diff
- services.Configure<QuartzOptions>(options => options.SchedulerName = "core");
+ services.AddQuartz(q => q.ConfigureScheduler(options => options.InstanceName = "core"));
```

`MisfireThreshold` also stored a `TimeSpan` as whole milliseconds, so sub-millisecond precision was
lost.

`Properties`, `ToProperties()` (formerly `ToNameValueCollection()`; see
[A property bag is any dictionary](#a-property-bag-is-any-dictionary)) and `Scheduling` stay.
`Scheduling`'s three directives say how a configured schedule is applied, and have no options type of
their own.

`Scheduling` is get-only, like `Properties`, so a later callback cannot discard what
`Quartz:Scheduling` or an earlier callback set. Set its properties instead:

```diff
- services.Configure<QuartzOptions>(options => options.Scheduling = new SchedulingOptions { IgnoreDuplicates = true });
+ services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);
```

## `AddJob` registers the job with the container

`AddJob<T>()`, `AddJob(type, …)` and `ScheduleJob<T>()` now register the job type as a **scoped**
service, with `TryAdd` semantics.

Before, a job whose constructor the container could not satisfy failed only at fire time, with a
`JobInstantiationException`, after every trigger of the job had moved to `TriggerState.Error`
(discussion [#3211](https://github.com/quartznet/quartznet/discussions/3211)). Now `ValidateOnBuild`
(on by default in the Development environment) catches it when the container is built:

```csharp
services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));

// now throws when the container is built:
// Unable to resolve service for type 'IReportStore' while attempting to activate 'SendReportsJob'
```

**A job you register yourself keeps your registration.** Quartz uses `TryAdd`, so your lifetime,
factory or implementation type wins, and calling `AddJob` twice is harmless. If you registered jobs
only to get startup validation, that line is now redundant but not wrong:

```diff
- services.AddScoped<SendReportsJob>();   // no longer needed for validation
  services.AddQuartz(q => q.AddJob<SendReportsJob>(j => j.WithIdentity("send-reports")));
```

**Scoped is the lifetime the job factory uses.** It opens a scope per fire, resolves the job from it,
and disposes the scope when the job returns, so a job may take scoped dependencies such as a database
context. If a job must be a singleton, register it yourself; it must then be thread-safe and must not
capture scoped dependencies.

Not registered, and so still failing at fire time on a missing dependency:

* a job type that is an interface or an abstract class;
* a job named only in an XML or JSON schedule.

**A registered job may not take a scheduler's own parts by constructor:** `IScheduler`,
`ISchedulerFactory`, `IJobStore`, `IThreadPool` or `IOptions<QuartzSchedulerOptions>`. Resolved from the
container, it would get the unkeyed ones: the default scheduler's, or nothing if the container holds
only named schedulers. The host refuses such a constructor at startup, naming the job and the
parameter:

```text
Job type ArchiveJob is registered on scheduler 'acme', and its constructor takes ISchedulerFactory
schedulerFactory — a part that belongs to one scheduler. …
```

Instead:

* take the scheduler from `IJobExecutionContext.Scheduler`, the one running the job;
* take `IJobExecutionContextAccessor` in code that is not handed the context;
* or register the job with `AddJobType<T>(factory)` (below) and resolve the part by key inside the
  factory, which is not checked.

A job type the container does not hold is unaffected: the job factory activates it with its own
scheduler's parts.

### `AddJobType` gives one scheduler its own build of a job type

New in 4.0, for containers with more than one scheduler. `AddJob<T>`'s registration is unkeyed and
`TryAdd`, so every scheduler gets the first one. `AddJobType` registers the job type for *this*
scheduler only:

```csharp
services.AddQuartz("acme", q =>
{
    q.AddJobType<ReportJob, AcmeReportJob>();                        // a different implementation
    q.AddJobType<AuditJob>(ServiceLifetime.Singleton);               // a different lifetime
    q.AddJobType<ExportJob>(sp => new ExportJob(sp.GetRequiredKeyedService<IExportSink>("acme")));
    q.AddJob<ReportJob>(j => j.WithIdentity("report"));
});
```

The registration is keyed by the scheduler's service key (unkeyed for the default scheduler). The job
factory looks there first, then falls back to the unkeyed registration, so a single-scheduler
application is unchanged.

The lifetime defaults to `ServiceLifetime.Scoped` and is chosen with an overload, not an optional
parameter. (An optional parameter defaulting to a shared-framework enum makes coverlet drop the whole
assembly from coverage.)

## Naming a job type by string says so under trimming

`Quartz` is trimmable in 4.0, and the job-type APIs carry trimming annotations. This is not a source or
binary break, but a `PublishTrimmed` build now reports warnings 3.x did not.

**The typed APIs require `[DynamicallyAccessedMembers(PublicConstructors | PublicProperties | Interfaces)]`
on the job type**: what a job factory constructs, what a `JobDataMap` binds onto, and where
`[DisallowConcurrentExecution]` may be inherited from. This covers `JobBuilder.Create<T>()`,
`JobBuilder<TJob>.OfType<T>()` and `OfType(Type)`, `AddJob<T>()`, `AddJob(Type, …)`, `AddJobType<T>()`,
`AddJobType<TJob, TImpl>()`, `AddTrigger<TJob>()`, `ScheduleJob<T>()`, `TriggerBuilder.Create<TJob>()`,
`new JobType(Type)` and the implicit `Type` → `JobType` conversion. The generic `JobBuilder<TJob>`,
`TriggerBuilder<TJob>`, `IJobConfigurator<TJob>` and `ITriggerConfigurator<TJob>` declare the same on
their type parameter. `PublicMethods`, declared in earlier 4.0 previews, is gone.

If you pass a `Type` variable rather than `typeof` or a generic argument, annotate it too:

```diff
- static IJobDetail Build(Type jobType) => JobBuilder.Create().OfType(jobType).Build();
+ static IJobDetail Build(
+     [DynamicallyAccessedMembers(
+         DynamicallyAccessedMemberTypes.PublicConstructors
+         | DynamicallyAccessedMemberTypes.PublicProperties
+         | DynamicallyAccessedMemberTypes.Interfaces)] Type jobType)
+     => JobBuilder.Create().OfType(jobType).Build();
```

**The string APIs are `[RequiresUnreferencedCode]`.** The `JobType(string)` constructor, the explicit
`string` → `JobType` cast and the `job_scheduling_data` XML loader report `IL2026` in a trimmed build.
Prefer the typed APIs, or root the type with a
[trimmer root descriptor](https://learn.microsoft.com/dotnet/core/deploying/trimming/trimming-options#root-descriptors).
See [Publishing Trimmed and Native AOT](how-tos/trimming-and-native-aot.md).

`DbMetadata.ConnectionType`, `.CommandType` and `.ParameterType` are annotated as well. A
`UseGenericDatabase` callback that sets them with `typeof(...)` already satisfies them.

**Behaviour change:** ADO.NET trigger acquisition now finds `[DisallowConcurrentExecution]` inherited
from an interface, as `IJobDetail.ConcurrentExecutionDisallowed` always did. Before, such a job was
serialized at firing but not at acquisition, so a batch could acquire two of its triggers and then
release the second.

### A named type is checked against `IJob` before it is constructed

A job type given as a *name* is now checked for `IJob` before anything is constructed: in the job
factory, the activator cache and `TypeActivator`. Before, the container and `ActivatorUtilities` ran the
type's static constructor, module initializer and instance constructor (with the scheduler scope's
services) before a cast failed. A job type given as a `Type` (`AddJob(Type)`, `new JobType(Type)`) was
always checked.

A firing of a type that is not an `IJob` now fails with a `JobInstantiationException` naming the type,
not an `InvalidCastException`, and no code of that type runs. If you caught `InvalidCastException` for
this, catch `SchedulerException`.

## A job type rename is declared as a map

A persistent job store keeps the job's type as text in `QRTZ_JOB_DETAILS.JOB_CLASS_NAME`, so renaming,
re-namespacing or moving the class breaks every row that names it. On 3.x the fix was a custom
`ITypeLoadHelper`. In 4.0, `TypeLoaderOptions.Aliases` maps the stored name to the current one, and
`UseTypeLoader(configure)` adds an alias in code:

<!-- snippet: sample_reference_type_loader_aliases -->
```csharp
services.AddQuartz(q => q.UseTypeLoader(loader =>
    loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob))));
```
<!-- endSnippet -->

Or in `appsettings.json`, shipped with the deployment that renames the type:

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

* The map applies wherever the shipped loader resolves a name at run time: `JOB_CLASS_NAME`, jobs
  named in XML or JSON scheduling data, and the `quartz.plugin.<name>.type` key.
* Flat keys naming a scheduler's own components are read before options exist, and are not aliased.
* Keys are compared ordinally, against the whole name or the part before the assembly's comma, as the
  loader's built-in 3.x → 4.0 rename table is.
* An alias whose target does not load **fails options validation at startup**, naming both halves.
* Nothing is written back: the row keeps the old name. That keeps an alias safe during a rolling
  deployment, while old nodes still write the old name.
* To retire an alias, run the SQL `UPDATE` in
  [Job deserialization failures after
  refactoring](../troubleshooting.md#job-deserialization-failures-after-refactoring) once nothing uses
  the old name.

`UseTypeLoader<T>()` still replaces the loader outright, for example to resolve names from a plugin's
`AssemblyLoadContext`. Replacing the loader drops the map, which belongs to the shipped loader.

## The job scope is prepared without writing a job factory

`ConfigureScope` prepares the dependency injection scope a job is built in, for example to set an
`AsyncLocal` its dependencies read. It used to require a subclass of
`MicrosoftDependencyInjectionJobFactory`; now it is a callback:

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

* It runs before the job is resolved, and it is synchronous so an `AsyncLocal` it sets survives into
  `Execute`.
* Callbacks combine rather than replace. The same delegate is `JobFactoryOptions.ConfigureScope`,
  which is per scheduler.
* Overriding the protected method still works, and replaces the delegate if the override does not call
  base.

### The firing can be read without being handed it

New in 4.0: `IJobExecutionContextAccessor`, a singleton registered by `AddQuartz`, gives the current
firing to code that is not handed an `IJobExecutionContext`, such as a scoped service, a logging
enricher or a repository. On 3.x you wrote your own `AsyncLocal`:

```diff
- public static class Tenant { public static readonly AsyncLocal<string?> Current = new(); }
- services.AddQuartz(q => q.ConfigureJobScope(
-     (scope, bundle, scheduler) => Tenant.Current.Value = bundle.Trigger.Key.Group));
+ public sealed class TenantConnectionFactory(IJobExecutionContextAccessor accessor)
+ {
+     public string ConnectionString => connectionStrings[accessor.Current!.Trigger.Key.Group];
+ }
```

* `Current` is set from when the execution context is created (before the trigger and job listeners are
  notified) until the job is returned to the job factory. It is `null` at every other time, including
  on the scheduling thread.
* It flows with the `ExecutionContext`, so it survives `await`, is captured by `Task.Run`, and is never
  another firing's.
* Work a job leaves running after its execution reads `null`, because the job's scope is disposed.
* There is no setter.

It does **not** replace `ConfigureJobScope`: the execution context does not exist while the job is
constructed, so anything the constructor needs still comes from the hook.

## A job can take a typed input

New in 4.0 and additive: `IJob<TInput>` declares the payload type a job is scheduled with, instead of
reading it out of `JobDataMap` strings.

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

The dispatch is `IJob<TInput>`'s default implementation of `IJob.Execute`, so the job factory,
`JobRunShell` and listeners still see an `IJob`, and nothing is generic at run time. That keeps it
working in a trimmed or native-AOT publish.

**API added:**

* In `Quartz`: `IJob<TInput>`; `SchedulerConstants.JobInput` (`"QRTZ_JOB_INPUT"`);
  `JobExecutionContextInputExtensions.GetInput<TInput>(this IJobExecutionContext)` and
  `TryGetInput<TInput>(this IJobExecutionContext, out TInput?)`; and
  `JobInputBuilderExtensions.UsingInput<TJob, TInput>` on `JobBuilder<TJob>`, `TriggerBuilder<TJob>`,
  `IJobConfigurator<TJob>` and `ITriggerConfigurator<TJob>`.
* In `Quartz.Extensibility`: `IJobInputSerializer`.
* In `Quartz.Impl`: `SystemTextJsonJobInputSerializer`, and an optional fourth parameter
  `IJobInputSerializer? inputSerializer = null` on the `JobExecutionContextImpl` constructor (the one
  existing signature that changed; source-compatible).

**Where the payload lives.** In the ordinary `JobDataMap` under `SchedulerConstants.JobInput`,
serialized to a **string** when the job or trigger is stored. It therefore works with
`StoreJobDataAsStrings`, the System.Text.Json write gate, the Newtonsoft serializer, the binary blob
column and the HTTP wire. A trigger's input overrides a job's, as in `MergedJobDataMap`.

`UsingInput` is an extension method (a method cannot constrain its own type's type parameter, CS0699),
offered only for a job that declares an input. `GetInput<TInput>()` is an extension so that hand-written
contexts keep compiling.

* The input type is inferred from the argument's static type. If you hold the payload as a base type,
  pass the type arguments: `UsingInput<SendEmailJob, SendEmail>(payload)`.
* A `[PersistJobDataAfterExecution]` job whose *detail* carries the input writes it back after every
  firing. That is harmless, but put an input that differs per firing on the trigger.

**Converting a job over a store that predates the input.** An `IJob<TInput>` fails a firing that
carries no input. Triggers written by the 3.x application keep their payload in flat `JobDataMap` keys
with nothing under `QRTZ_JOB_INPUT`. While those drain, keep the job an `IJob` and read both shapes with
`TryGetInput`:

```csharp
public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    SendEmail input = context.TryGetInput(out SendEmail? typed) && typed is not null
        ? typed
        : new SendEmail(context.MergedJobDataMap.GetString("to")!, context.MergedJobDataMap.GetString("subject")!);

    // ...
}
```

`TryGetInput` returns `false` only when the key is absent; a present but unreadable value still throws.
`GetInput<TInput>()` cannot tell a payload that read back as `null` from no payload.

**Serialization.** `IJobInputSerializer` is registered per scheduler, keyed by name. It defaults to
`SystemTextJsonJobInputSerializer`, built from the same `SystemTextJsonSerializerRegistry` as the
store's `IObjectSerializer`. For a trimmed or native publish, declare payload types with
`SystemTextJsonSerializerRegistry.AddTypeInfoResolver`, as for job data values. See
[Job Data](tutorial/job-data-map.md#a-typed-input-the-third-read-side).

## Scheduling a trigger can replace one atomically

New in 4.0 and additive: two `IScheduler.ScheduleJob` overloads take a `ScheduleJobOptions` and can
replace what is stored.

```csharp
ValueTask<DateTimeOffset> ScheduleJob(ITrigger trigger, ScheduleJobOptions options, CancellationToken cancellationToken = default);
ValueTask<DateTimeOffset> ScheduleJob(IJobDetail jobDetail, ITrigger trigger, ScheduleJobOptions options, CancellationToken cancellationToken = default);
```

On 3.x, rescheduling under a key you might already hold took three round trips with a race window:

```diff
- if (await scheduler.CheckExists(trigger.Key, cancellationToken))
- {
-     await scheduler.UnscheduleJob(trigger.Key, cancellationToken);
- }
- await scheduler.ScheduleJob(trigger, cancellationToken);
+ await scheduler.ScheduleJob(trigger, new ScheduleJobOptions { Replace = true }, cancellationToken);
```

* The new form is one store operation under the store's lock (`SchedulerLock.TriggerAccess` on an ADO
  store, the single lock in memory), so two nodes cannot leave the trigger deleted and not replaced.
* The job-and-trigger overload goes through the store's `ScheduleJobs`, so both are written together.
* `options` has **no default** on either overload, so that `scheduler.ScheduleJob(trigger)` and
  `scheduler.ScheduleJob(job, trigger)` stay unambiguous.
* **A replaced trigger keeps its previous fire time** on both stores. The ADO store did since #1834; the
  in-memory store lost it. `JobStoreContractTest` pins this, and that a trigger replaced into a paused
  group is created paused. To reset the history, set `PreviousFireTimeUtc` on the incoming trigger.
* **Over HTTP**, `POST /triggers/schedule` takes a `replace` flag, and `HttpScheduler` implements both
  overloads. Omitting the flag behaves as before.

## A scheduler says which clock it keeps

New in 4.0: `IScheduler.TimeProvider`, a **default interface member** answering `TimeProvider.System`,
so an `IScheduler` of your own keeps compiling. The shipped schedulers answer:

| Scheduler | Answers |
|---|---|
| the local scheduler | the `TimeProvider` it was configured with |
| `DelegatingScheduler` | whatever it decorates |
| the deferred scheduler `AddQuartz` registers | the built scheduler's, or the system clock while there is no scheduler yet |
| `HttpScheduler` | the system clock: the remote scheduler's clock decides firing, and it cannot be fetched over a wire |

On 3.x, code building a trigger for a scheduler used `DateTimeOffset.UtcNow`, which differs from the
scheduler's clock when it runs on a custom one (common in tests):

```diff
- ITrigger trigger = TriggerBuilder.Create()
-     .StartAt(DateTimeOffset.UtcNow.AddMinutes(10))
-     .Build();
+ ITrigger trigger = TriggerBuilder.Create(scheduler.TimeProvider)
+     .StartAt(scheduler.TimeProvider.GetUtcNow().AddMinutes(10))
+     .Build();
```

A job rescheduling itself reads it as `context.Scheduler.TimeProvider`. The one-call
`ScheduleJob<TJob, TInput>` overloads below use it for their `TimeSpan` form and for the trigger they
build.

## A typed job can be scheduled in one call

New in 4.0, building on [typed inputs](#a-job-can-take-a-typed-input): two `IScheduler` extension
methods schedule one firing of a job with a payload and a time.

```csharp
ValueTask<ScheduledOneOffJob> ScheduleJob<TJob, TInput>(this IScheduler scheduler, TInput input, DateTimeOffset at,
    OneOffJobOptions options = default, CancellationToken cancellationToken = default) where TJob : IJob<TInput>;

ValueTask<ScheduledOneOffJob> ScheduleJob<TJob, TInput>(this IScheduler scheduler, TInput input, TimeSpan delay,
    OneOffJobOptions options = default, CancellationToken cancellationToken = default) where TJob : IJob<TInput>;
```

They are the run-time form of `IQuartzBuilder.ScheduleJob<TJob>`, which only covers firings declared at
startup.

```csharp
ScheduledOneOffJob firing = await scheduler.ScheduleJob<SendInvoiceJob, SendInvoice>(
    invoice, TimeSpan.FromDays(7),
    OneOffJobOptions.Replacing($"invoice-{invoice.CustomerId}") with { Group = invoice.CustomerId });

logger.LogInformation("Reminder {Trigger} scheduled for {At}", firing.TriggerKey, firing.FirstFireTimeUtc);

await scheduler.UnscheduleJob(firing.TriggerKey);
```

**Result:** `ScheduledOneOffJob`, a `readonly record struct` with two members: the firing's `TriggerKey`
(to cancel it, or to replace it by scheduling the same name again) and the `FirstFireTimeUtc` the store
computed.

**One durable job per job type, many triggers.** The job is stored once under
`SchedulerConstants.ScheduledJobKey<TJob>()`, which is
`(typeof(TJob).Name, SchedulerConstants.ScheduledJobGroup)`; the new reserved group is
`"QRTZ_SCHEDULED"`. Each call adds a trigger, so there is no job churn.

* The job is ensured with `AddJob` and `Replace` (idempotent, cluster-safe) and remembered per
  scheduler instance.
* If the store reports the job missing (a cluster restore, an operator's delete), it is put back and
  the firing retried once.
* The key is public so an integration can point its own recurring trigger at the same job.
* In the 4.0 previews it was `SchedulerJobExtensions.ScheduledJobKey<TJob>()`; it moved beside
  `SchedulerConstants.ScheduledJobGroup`.

**`OneOffJobOptions`** configures the one firing (it is not `ScheduleJobOptions`, which describes a
store operation). Every member is optional; `default` is a one-shot trigger with a generated name in
the job type's own group.

| Member | Meaning |
|---|---|
| `Name`, `Group`, `Description`, `Priority`, `ExecutionGroup`, `MisfireInstruction` | the trigger settings you would otherwise set on `TriggerBuilder` |
| `Replace` | passed to the store |
| `RequestRecovery` | marks the ensured *job* `RequestsRecovery`, so a firing interrupted by a hard shutdown re-runs when the scheduler returns. The job is ensured once per scheduler instance, so the first call's value wins for the life of the process |

* `OneOffJobOptions.Replacing(name)` is the preset. It takes the name because `Replace` without a name
  does nothing: a generated name has nothing to replace.
* `AddJobOptions` gained `AddJobOptions.ReplacingAndStoringNonDurable`, the twin of
  `AddCalendarOptions.ReplacingAndUpdatingTriggers`.
* `Name` and `Group` are separate strings, not a `TriggerKey`, because their defaults differ (a
  generated name; a group named after the job type) and you may set either alone.
* A recurring overload, if added, will get its own options type.

**Group, cancelling and correlation.** Firings for one saga, tenant or conversation can share a group
and be listed, paused or unscheduled together. Cancel with `UnscheduleJob(key)`; the durable job stays,
one row per job type. **`Group` defaults to the job type's name**, so an integration whose callers
cancel with `new TriggerKey(id)` (the *default* group) must set `Group = TriggerKey.DefaultGroup`, or
cancellation silently stops matching.

See [One-Off Job](how-tos/one-off-job.md#a-payload-and-a-time-in-one-call).

## A scheduled job links back to the trace that scheduled it

New in 4.0 and on by default: a scheduling call made inside an `Activity` records that activity's W3C
trace context on the trigger, and the firing's `Quartz.Job.Execute` span carries an `ActivityLink` back
to it. No configuration or code change is needed.

On 3.x, integrators wrote their own key into the trigger's `JobDataMap`:

```diff
- trigger.UsingJobData("traceparent", Activity.Current?.Id);   // and a job that read it back by hand
+ // nothing — the scheduler writes it, and the execute span links to it
```

**A link, not a parent.** A job scheduled for next week may run on another node; a child span would
keep the trace open for a week. The firing is its own trace root, as OpenTelemetry models an
asynchronous producer and consumer. The span name is unchanged.

**API added**, in `Quartz`: `SchedulerConstants.TraceParent` (`"QRTZ_TRACEPARENT"`),
`SchedulerConstants.TraceState` (`"QRTZ_TRACESTATE"`), and `QuartzSchedulerOptions.PropagateTraceContext`,
default `true`. There is no flat `quartz.*` key; `LegacyPropertyKeys` rejects an invented one.

```csharp
q.ConfigureScheduler(options => options.PropagateTraceContext = false);
```

**Where it is written:** on the **trigger's** data map, at every entry point that stores a trigger:
both `ScheduleJob` overload pairs, `ScheduleJobs`, both `TriggerJob` forms and `RescheduleJob`. On a
reschedule, the new context overwrites the old one in the carried-over data map.

`UpdateTriggerDetails` writes **no** trace context, because the trigger keeps its fire times and the
original scheduler is still the right link. (It does normalize a typed input in the map it is given.)

* **Two more entries per trigger row.** They are ordinary string job data, so they work with
  `StoreJobDataAsStrings`, the System.Text.Json write gate, the Newtonsoft serializer, the binary blob
  column and the HTTP wire. They show in `MergedJobDataMap`, the dashboard and `GET /triggers`, like
  every `QRTZ_*` reserved key. Turn the option off if you do not want them.
* **The trigger's map, never the job's.** `[PersistJobDataAfterExecution]` writes back only the job's
  map, so a persisted job cannot carry a `traceparent` into its next firing.
* **A stale context is removed.** The scheduler stores the trigger object it was handed, so a trigger
  scheduled inside a request and again outside one loses the key on the second call.
* **Over HTTP it needs nothing.** `POST /jobs/{group}/{name}/trigger` and the scheduling endpoints run
  inside ASP.NET Core's server span, so the request's trace reaches the trigger.

See [Observability](packages/opentelemetry-integration.md#linking-a-firing-to-what-scheduled-it).

## A component of your own is chosen the same way a shipped one is

New code-first forms for a custom job store and instance id generator:

```csharp
q.UseJobStore<MyJobStore>();                                   // built by the container
q.UseJobStore<MyJobStore, MyJobStoreOptions>(o => o.X = 1);     // with options of its own
q.UseJobStore(provider => new MyJobStore(provider.GetRequiredService<ISchedulerSignaler>()));

q.UseInstanceIdGenerator<HostNameInstanceIdGenerator>();
```

* `UseJobStore<T>` is for a store of your own. Select the shipped stores with `UseInMemoryStore` and
  `UsePersistentStore`, which also configure them;
  [those stores are internal](#the-ado-net-store-is-a-store-not-a-base-class).
  `UsePersistentStore<T>(configure)` takes a persistent store of your own, and
  `UseJobStore(IJobStore)` (a store you built) is unchanged.
* `UseInstanceIdGenerator<T>()` replaces the keys `quartz.scheduler.instanceId = AUTO` and
  `quartz.scheduler.instanceIdGenerator.type`, and sets `GenerateInstanceId` too. As before, the
  generator is called only for a clustered scheduler.
* The `<T, TOptions>` forms are shorthand for `ConfigureOptions<TOptions>`, declared as that
  scheduler's options: under `AddQuartz("reporting", …)`, `IOptions<TOptions>` gets what was
  configured for `reporting`.

## Listener matchers are a collection

The builder's nine listener overloads take `params IReadOnlyCollection<IMatcher<T>>` instead of
`params IMatcher<T>[]`, as `IListenerManager.AddJobListener` and `AddTriggerListener` already did.
Existing calls still compile (loose arguments bind, and an array is an `IReadOnlyCollection<T>`), and a
`List<IMatcher<JobKey>>` no longer needs `ToArray()`.

## `AddQuartzHttpApi` is registered on the service collection

The HTTP API serves every scheduler in the container, so it is registered on the service collection:

```diff
- services.AddQuartz(q => q.AddQuartzHttpApi());
+ services.AddQuartzHttpApi();
```

The `IQuartzBuilder` form is gone. Inside `AddQuartz(name, …)` it looked like that scheduler's API but
configured everyone's, and two calls with different `ApiPath`s silently overrode each other.
`QuartzHttpApiOptions` stays singular: `ApiPath` belongs to the process.

## The route is named where the endpoints are mapped

`MapQuartzHttpApi` and `MapQuartzDashboard` take a route pattern, like `MapHealthChecks("/health")`:

```diff
- services.AddQuartzHttpApi(options => options.ApiPath = "/ops/api");
- services.AddQuartzDashboard(options => options.DashboardPath = "/ops/quartz");
  …
- endpoints.MapQuartzHttpApi();
- endpoints.MapQuartzDashboard();
+ endpoints.MapQuartzHttpApi("/ops/api");
+ endpoints.MapQuartzDashboard("/ops/quartz");
```

* Nothing is removed. `QuartzHttpApiOptions.ApiPath` and `QuartzDashboardOptions.DashboardPath` still
  work (use them when the path comes from configuration), and the parameterless overloads read them.
* When both are given, the pattern at the map site wins.
* A bad pattern throws `ArgumentException` naming the parameter, not an options-validation failure
  (validators have already run by then).
* There is no `MapQuartzDashboard(existingComponents, pattern)`: with an application's own Blazor root,
  the dashboard pages are fixed at `/quartz`, and a custom `DashboardPath` is rejected in that mode.

## The HTTP API and the dashboard will not serve anonymously by accident

Both surfaces can schedule a job whose type is a string from the request, resolved later with
`Type.GetType`. With `Quartz.Jobs` on the probing path, an open endpoint is remote code execution. 4.0
refuses to start rather than serve one unprotected by omission.

Before the server binds, a hosted lifecycle service checks every endpoint Quartz mapped and throws
unless each has authorization metadata or an explicit `AllowAnonymous()`. These also satisfy it:

* a non-null `AuthorizationOptions.FallbackPolicy`;
* authorization on a `MapGroup` above the mapping.

Registering the services without mapping anything is not checked. The error names all three fixes:

```csharp
app.MapQuartzHttpApi().RequireAuthorization();                 // the whole surface
// or QuartzHttpApiOptions.SchedulerAuthorizationPolicy        // each scheduler on its own
// or QuartzDashboardOptions.AuthorizationPolicy
app.MapQuartzDashboard().AllowAnonymous();                     // deliberately open
```

`AllowAnonymous()` is supported; saying nothing is not. Quartz authorizes but does not authenticate, so
the application still registers the authentication scheme; see
[Production hardening](packages/http-api.md#production-hardening).

### One builder covers the dashboard's pages and its hub

`MapQuartzDashboard()` returns an `IEndpointConventionBuilder` covering the dashboard's pages and its
SignalR hub, and nothing else. On 3.x it returned the pages' `RazorComponentsEndpointConventionBuilder`,
so `MapQuartzDashboard().RequireAuthorization()` left the hub open, and in the overload taking an
existing components builder it also reached the host application's own pages. Now one
`RequireAuthorization()` or `AllowAnonymous()` covers the dashboard.

If you called a `RazorComponentsEndpointConventionBuilder` member on the result (a render-mode call,
say), call it on your own `MapRazorComponents` and pass that builder to the `existingComponents`
overload.

### A paged request is bounded by `MaxPageSize`

`QuartzHttpApiOptions.MaxPageSize` is `1000`.

* A `take` **number** above it is a `400` naming the cap, `?take=2147483647` included.
* `?take=all` is capped, not refused, and reports `hasMore`. This keeps the 3.x-compatible listings
  working: `GetJobKeys` and similar ask for everything, and `HttpScheduler` turns a truncated answer to
  one of those into an exception rather than a short list.
* `0` means no limit.

### A `500` says nothing about the server

A `500`'s `detail` is now one fixed sentence: *"The scheduler failed to handle the request. The failure
is recorded in the server's log."* The exception's message, which could name the server, database,
login or constraint, is withheld (its type never was returned); both are still logged.
`QuartzHttpApiOptions.IncludeStackTraceInProblemDetails` puts the message back. A client matching on a
`500`'s `detail` now matches a constant.

### A job's two attribute flags are nullable on the wire

`concurrentExecutionDisallowed` and `persistJobDataAfterExecution` are `bool?` in every job body the API
reads or writes, and in `Quartz.Dashboard.Services.JobDetailDto`.

* **Present** means the sender stated the flag. **Absent** means "use `[DisallowConcurrentExecution]`
  and `[PersistJobDataAfterExecution]` on the type", as `JobBuilder` does.
* Before, a missing `bool` read as `false`, so a `POST …/jobs` that did not mention concurrency stored a
  job declared unsafe for concurrent runs as safe. A client that sent both fields is unaffected.
* A job whose type the answering process cannot resolve reports both flags as `null`. Before,
  `GET …/jobs/{group}/{name}` answered `500` for it, permanently, which is the normal case in a
  heterogeneous cluster.
* In code, `IJobDetail.ConcurrentExecutionDisallowed` is still `bool`, and answers `false` for an
  unresolvable type instead of throwing `InvalidOperationException`.

### A job type name is never resolved by the contract types

`JobDetailDto.AsIJobDetail()` and `JobDetailDto.Create()` no longer resolve a job type name in either
direction. `JobBuilder.Build()` no longer reads the two attribute flags; `JobDetailImpl` reads them
lazily when asked and the type is available.

* `Quartz.HttpClient` can schedule and add jobs whose type only the server has, as
  `packages/http-client.md` promised (beta.1 threw
  `InvalidOperationException: Job type … cannot be resolved`).
* A hostile server can no longer make a client's runtime load an assembly by simple name.
* If you relied on `Build()` capturing the attributes (build, unload the assembly, read the flags),
  read them before the assembly goes.

The persistent store does not resolve a job class outside firing either. A process that cannot load the
job classes (a `UseThreadPool<ZeroSizeThreadPool>()` administration node, or a web application that
edits schedules without referencing the worker) can read, pause, resume, reschedule, update, trigger,
add triggers to and delete jobs by stored name. Both attribute flags come from
`QRTZ_JOB_DETAILS.IS_NONCONCURRENT` and `IS_UPDATE_DATA`.

* `RescheduleJob` used to fail there with
  `JobPersistenceException: Couldn't replace trigger: Could not load type
  'Acme.Jobs.MessageCleanupJob, Acme.Jobs'`, because it loaded the class to check concurrency.
* Such a process needs no `ITypeLoader` of its own, and no placeholder job type as on 3.x. (A
  placeholder was a hazard: its `[DisallowConcurrentExecution]` decided whether a replacement trigger
  was stored `WAITING` or `BLOCKED`.)
* Only the firing path loads a job's class, where the job runs.

`IScheduler.Interrupt` does not work from such a node, by design: it is not cluster aware and cancels
only firings in the scheduler it is called on, and an administration node runs none.

## One shape per registration method

`AddJob`, `AddTrigger` and `AddCalendar` now each have two shapes: one taking a configurator, one taking
a configurator and the `IServiceProvider`. The removed overloads duplicated these, and optional
parameters made the no-argument calls ambiguous.

| Removed | Use instead |
|---|---|
| `AddJob<T>(JobKey?, …)`, `AddJob(Type, JobKey?, …)` | `WithIdentity(jobKey)` inside the configurator |
| `AddJob<T>()`, `AddJob<T>(JobKey)` with no configurator | `AddJob<T>(j => j.WithIdentity(…))` |

```diff
  var jobKey = new JobKey("awesome job", "awesome group");
- q.AddJob<ExampleJob>(jobKey, j => j.WithDescription("my awesome job"));
+ q.AddJob<ExampleJob>(j => j.WithIdentity(jobKey).WithDescription("my awesome job"));
```

`AddTrigger<TJob>`'s job type lets the trigger's job data name the job's properties. A trigger that
only uses `ForJob` needs none: `AddTrigger` without a type argument is the unchanged 3.x call.

These methods extend `IQuartzBuilder`, which 3.x called `IServiceCollectionQuartzConfigurator`. The
`AddQuartz` overloads that passed an `IServiceProvider` to the callback are gone, because that could
only be a throwaway container. Take the service provider where it is used, in the overload that is
handed one:

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

Every registration method has such an overload, and anything else is configured through the options
pattern. [Deferred configuration](#deferred-configuration) lists both routes, and why reading
`IConfiguration` at registration time replaces neither.

`AddCalendar` takes the `AddCalendarOptions` record that `IScheduler.AddCalendar` takes, instead of two
adjacent bools, and its first parameter is `name` rather than `calendarName`:

```diff
- q.AddCalendar<HolidayCalendar>("holidays", replace: true, updateTriggers: true,
-     cal => cal.AddExcludedDay(new DateOnly(2025, 12, 25)));
+ q.AddCalendar<HolidayCalendar>("holidays", new AddCalendarOptions { Replace = true, UpdateTriggers = true },
+     cal => cal.AddExcludedDay(new DateOnly(2025, 12, 25)));
```

The options and the configurator are both optional in the type-based overload, so
`q.AddCalendar<HolidayCalendar>("holidays")` registers an empty calendar.

### A calendar can be built from the container

The generic `AddCalendar<T>` overloads require `where T : ICalendar, new()`, so a calendar with
dependencies (a holiday list from a database, a clock, options) needs the factory overload:

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

* The factory gets the scheduler-scoped `IServiceProvider`, so a named scheduler's calendar gets that
  scheduler's parts.
* It runs once, when the scheduler's content is resolved.
* `AddCalendarOptions` is the optional third parameter, as on the other overloads.
* It is an `IQuartzBuilder` extension, so it also works inside `QuartzSchedulerBuilder.Create(q => …)`.

## Plugins are registered like listeners

`AddPlugin` has the same three shapes as the listener registrations, each with an optional trailing
name. (Before, one of four shapes took the name first and the others took none.)

| Shape | Meaning |
|---|---|
| `AddPlugin<T>(string? name = null)` | the container constructs the plugin |
| `AddPlugin<T>(Func<IServiceProvider, T> factory, string? name = null)` | you construct it |
| `AddPlugin<T, TOptions>(Action<TOptions>? configure = null, string? name = null)` | it is given options of its own |

```diff
- q.AddPlugin("xml", provider => new XmlSchedulingDataProcessorPlugin());
+ q.AddPlugin(provider => new XmlSchedulingDataProcessorPlugin(), "xml");
```

The name is how the scheduler refers to the plugin and the `<name>` in `quartz.plugin.<name>.*` keys.
Some plugins derive persisted job and trigger keys from it, so treat it as part of the deployment. If
unset, the plugin's type name is used, as before.

### A plugin implements only what it has to say

`ISchedulerPlugin.Start` and `ISchedulerPlugin.Shutdown` have default implementations that do nothing,
so a plugin that works only in `Initialize` can drop them:

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

* The scheduler still calls both, through the interface, at the same moments. Existing plugins need no
  change.
* The shipped plugins dropped their empty `Start`/`Shutdown` members, so those are no longer on the
  concrete types. If you called one directly, call it through `ISchedulerPlugin`.

### The two scheduling-data plugins have one surface

`XmlSchedulingDataProcessorPlugin` and `JsonSchedulingDataProcessorPlugin` now expose the same surface:
the two constructors plus what `ISchedulerPlugin` and `IFileScanListener` require.

| Member | Was | Now |
|---|---|---|
| `XmlSchedulingDataProcessorPlugin.ProcessFile(string, CancellationToken)` | public on the XML plugin, private on the JSON one | private on both. To read a file on demand, call `IFileScanListener.FileUpdated(fileName)`, as `FileScanJob` does |
| `JsonSchedulingDataProcessorPlugin.Shutdown(CancellationToken)` | public on the JSON plugin, absent on the XML one | absent on both; `ISchedulerPlugin.Shutdown` does nothing by default |
| `FileNames`, `ScanInterval`, `FailOnFileNotFound`, `FailOnSchedulingError` | public get-only on both, with internal setters | internal. Set through `FileSchedulingOptions`; `quartz.plugin.<name>.fileNames` and the related keys still write them |
| `Name`, `Scheduler` | public get-only on both | internal; they are what `Initialize` was handed |

Nothing that *configured* a plugin stops compiling. Code that *read* these properties should read the
options instead: `IOptionsFactory<FileSchedulingOptions>.Create(schedulerName)`.

`FileSchedulingOptions.Files` is a `List<string>`, while the plugin keeps a delimited `FileNames`
string to match the `quartz.plugin.<name>.fileNames` key. `UseXmlSchedulingConfiguration` /
`UseJsonSchedulingConfiguration` join them, the same way for both plugins.

Both also have a `params string[]` overload for the common one-file case:
`q.UseJsonSchedulingConfiguration("~/quartz_jobs.json")`. It adds to `Files`, so it combines with the
callback form and with itself. Rescanning and the two `FailOn*` settings still need the callback.

### The container is not in the scheduler context

3.x's `ServiceCollectionSchedulerFactory` put the `IServiceProvider` into
`scheduler.Context["Quartz.ServiceProvider"]`. 4.0 does not. Plugins, listeners and jobs are
constructed by the container and take dependencies through their constructors:

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

That entry also made the HTTP API's `GET …/schedulers/{name}/context` answer `500` for every
container-built scheduler ([#3408](https://github.com/quartznet/quartznet/issues/3408)). The endpoint now
renders every value as text, so a context entry of any type reads back.

## Cross-cutting concerns run as middleware

A log scope, tenant context, metric or exception translation has to *surround* the job call. On 3.x,
`IJobListener` only notifies before and after, so the only way was a job wrapping another job (as ABP,
Elsa and Brighter ship; requested since 2021 in
[#988](https://github.com/quartznet/quartznet/issues/988)). 4.0 adds job middleware:

```csharp
public delegate ValueTask JobExecutionDelegate(IJobExecutionContext context, CancellationToken cancellationToken);

public interface IJobExecutionMiddleware
{
    ValueTask Invoke(IJobExecutionContext context, JobExecutionDelegate next, CancellationToken cancellationToken = default);
}
```

It is registered on the scheduler's builder in the three shapes listeners have:

```csharp
q.AddJobMiddleware<LogScopeMiddleware>();
q.AddJobMiddleware(provider => new MeteredMiddleware(provider.GetRequiredService<IMeterFactory>()));
q.AddJobMiddleware(new TenantScopeMiddleware());
```

* Middleware is keyed per scheduler, so a named scheduler has its own.
* It runs in registration order, outermost first.
* The chain is built once with the scheduler, so one instance serves every firing. Keep per-firing
  state in an `AsyncLocal<T>` or the job's scope, not in a field.
* A scheduler with no middleware has no pipeline, so nothing changes if you add none.
* It runs inside the execution span and the duration measurement, and outside the run shell's
  exception handling. A `JobExecutionException` thrown by middleware is handled like one from the job,
  `RefireImmediately` included.

**Listeners are unchanged**: notification-only, `VetoJobExecution` still refuses a fire, and matchers
still pick jobs. [Job Execution Middleware](tutorial/job-execution-middleware.md) covers middleware and
which concerns belong where.

### `JobInterruptMonitorPlugin` is retired; a job timeout is middleware

`JobInterruptMonitorPlugin`, `UseJobAutoInterrupt`, `JobAutoInterruptOptions` and the two `JobDataMap`
keys (`"AutoInterruptable"` and `"MaxRunTime"`, in milliseconds) are gone. The replacement is typed and
in the core `Quartz` package:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `q.UseJobAutoInterrupt(o => o.DefaultMaxRunTime = TimeSpan.FromMinutes(5))` | `q.AddJobTimeout(TimeSpan.FromMinutes(5))` |
| `q.UseJobAutoInterrupt()` and no per-job data | `q.AddJobTimeout()`, which bounds only the jobs that declare a budget |
| `.UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable, true)` | nothing: every job the scheduler runs is bounded by the default |
| `.UsingJobData(JobInterruptMonitorPlugin.JobDataMapKeyMaxRunTime, "5000")` | `[JobTimeout("00:00:05")]` on the job class |
| no way to exempt one job from the default | `[JobTimeout("00:00:00")]` on the job class |
| `quartz.plugin.jobAutoInterrupt.type = Quartz.Plugin.Interrupt.JobInterruptMonitorPlugin, Quartz.Plugins` | remove the keys; the type no longer exists, so a configuration still naming it fails to load |
| `JobInterruptMonitorPlugin.JobDataMapKeyAutoInterruptable` / `.JobDataMapKeyMaxRunTime` | removed |

`[JobTimeout]` is read like `[DisallowConcurrentExecution]`: inherited from a base class or an interface,
with no schema column, wire format or data migration. Delete the two old keys from any job or trigger
data map; they do nothing now.

**Behaviour change, on purpose:** the plugin's interrupt counted as a *completed* execution, so no
`JobExecutionException` reached a job listener and no retry policy ran. The middleware interrupts
through `IScheduler.InterruptFireInstance` and then raises a `JobExecutionException` naming the budget.
A timeout is now an ordinary failure: visible to listeners, counted as an error, and retried by the
trigger's `RetryPolicy` if it has one. A job that swallows its cancellation and returns normally is also
reported as timed out.

A job that ignores its `CancellationToken` still cannot be stopped; `CA2016` flags that.

### `ShutdownHookPlugin` is retired; the host already shuts the scheduler down

`ShutdownHookPlugin`, `UseShutdownHook` and `ShutdownHookOptions` are gone. The plugin used an
`async void` `AppDomain.CurrentDomain.ProcessExit` handler, so nothing awaited its shutdown and the
process could exit part-way through.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `q.UseShutdownHook()` under a host | `services.AddQuartzHostedService()`, already the recommended registration and what stops the scheduler |
| `q.UseShutdownHook(o => o.CleanShutdown = true)` | `services.AddQuartzHostedService(o => o.WaitForJobsToComplete = true)` |
| `q.UseShutdownHook(o => o.CleanShutdown = false)` | `services.AddQuartzHostedService()`; `WaitForJobsToComplete` defaults to `false` |
| `q.UseShutdownHook()` with no host | `await scheduler.Shutdown(waitForJobsToComplete: true)` on the application's own exit path |
| `quartz.plugin.shutdownHook.type = Quartz.Plugin.Management.ShutdownHookPlugin, Quartz.Plugins` | remove the keys; the type no longer exists, so a configuration still naming it fails to load |
| `ShutdownHookPlugin.CleanShutdown` | `QuartzHostedServiceOptions.WaitForJobsToComplete` |

`QuartzHostedService` is an `IHostedLifecycleService`: the host stops every registered scheduler during
its own shutdown and awaits it, within `HostOptions.ShutdownTimeout`, shutting several schedulers down
concurrently.

Without a host, await the shutdown on your exit path (end of `Main`, a `Ctrl+C` handler, disposing a
scope):

```csharp
await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create().Build();
IScheduler scheduler = await schedulerFactory.GetScheduler();
await scheduler.Start();

// ... the application runs ...

await scheduler.Shutdown(waitForJobsToComplete: true);
```

3.x's `Quartz.Server` host already shut down this way and never used the plugin.

## Registered schedulers can be listed without being started

`ISchedulerFactory.GetAllSchedulers()` lists only schedulers already created, because it reads
`ISchedulerRepository`. With a scheduler per tenant, listing the tenants meant building them all.

`ISchedulerRegistry`, registered by `AddQuartz`, lists the registrations instead:

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

* **`Status` is `null` exactly when nothing has been built under that name.** Asking does not build it.
* **`Origin.Container`**: registered by `AddQuartz()` or `AddQuartz(name, …)`. **`Origin.Runtime`**: in
  the repository with no registration, such as a `QuartzSchedulerBuilder` scheduler bound by hand or a
  remote scheduler from `AddQuartzHttpClient`. The container does not own a runtime scheduler's
  lifetime.
* The default scheduler is listed under its configured `InstanceName`; it has no service key.

`GetAllSchedulers()` is unchanged; use it when you want the live scheduler instances.

### `GET /schedulers` answers from the registry

The HTTP API's scheduler listing reads `ISchedulerRegistry` too, so it includes registrations that are
not built, and each entry says which:

```json
{ "name": "acme", "schedulerInstanceId": null, "status": null, "origin": "Container" }
```

| | 3.x and the earlier 4.0 previews | 4.0 |
|---|---|---|
| Source | `ISchedulerRepository.LookupAll()` | `ISchedulerRegistry.QuerySchedulers()` |
| `status` | always a name | `null` when nothing has built the scheduler |
| `schedulerInstanceId` | always present | `null` when nothing has built the scheduler |
| `origin` | — | `Container` or `Runtime` |

* Handle `null` in `status` and `schedulerInstanceId`. To list only running schedulers, filter on
  `status`.
* A scheduler's own routes still resolve through the repository, so `GET /schedulers/{name}` answers
  `404` for a registration nothing has built.
* `Quartz.Dashboard`: `SchedulerHeaderDto.Status` and `SchedulerInstanceId` are nullable, and it gained
  `Origin` and `IsCreated`. `SchedulerDetailDto` gained the `SchedulerMetadata` fields `Clustered`,
  `Persistent`, `JobStoreTypeName`, `ThreadPoolTypeName`, `ThreadPoolSize`, `RunningSince`,
  `JobsExecuted` and `Version`. Both types are public because `IQuartzApiClient` is replaceable; an
  implementation of your own must fill the new members.

`ISchedulerRegistry` is the narrow half of the runtime tenant lifecycle API sketched in
[#3338](https://github.com/quartznet/quartznet/issues/3338). 4.1's `ISchedulerRuntime` extends it, and
resolving either returns the same object; see [Upgrading from 4.0 to 4.1](#upgrading-from-4-0-to-4-1).

## A shared database says so when two schedulers disagree about the table prefix

Schedulers sharing a database are told apart by `SCHED_NAME` and share one table prefix. A scheduler
pointed at the wrong prefix used to start, pass schema validation, report healthy, and never see its
data. `SchemaProvisioning.CreateIfMissing` makes that easier, because it creates the mis-typed tables.

Creating a scheduler now records its database and table prefix. If it shares a database with an
existing scheduler but uses a different prefix, a `Warning` names both schedulers and both prefixes. It
is not an error, because separate table sets in one database are legal.

* The check only compares schedulers in one container. Separate processes or containers are not
  compared.
* A provider that reports no connection string and no `DbDataSource` is skipped.
* The check is not configurable and never fails anything.

## Several schedulers are registered explicitly

`AddQuartz(IConfiguration)` used to register one named scheduler per child of a `Schedulers`
sub-section, if there was one. That is now its own method:

```diff
- services.AddQuartz(builder.Configuration.GetSection("Quartz"));   // with a Schedulers section
+ services.AddQuartzSchedulers(builder.Configuration.GetSection("Quartz"));
```

* `AddQuartz(configuration)` throws a `SchedulerConfigException` naming `AddQuartzSchedulers` when the
  section has a `Schedulers` sub-section.
* `AddQuartzSchedulers` throws when it has none.
* `AddQuartz(name, configuration)`, which registers one of the schedulers in a `Schedulers` section, is
  unchanged.

The six phases that decide which of a scheduler's descriptions wins (configuration last-wins,
registration first-wins) are documented on `AddQuartz`.

## Every scheduler in the container can be configured at once

New: `ConfigureAllQuartzSchedulers(Action<IQuartzBuilder>)`, the scheduler equivalent of the options
pattern's `ConfigureAll`. The delegate applies to every scheduler registered by `AddQuartz()`,
`AddQuartz(name, …)` or `AddQuartzSchedulers(…)`.

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

* **Call order does not matter.** Schedulers already registered are configured at the call; later ones
  by their own `AddQuartz`. So a library can add to every scheduler without knowing the application's
  order.
* The delegate gets a builder *per scheduler*, so its registrations go under that scheduler's service
  key, as if written in its own `AddQuartz(name, q => …)`. A plugin or listener added this way is **one
  instance per scheduler**, initialized with that scheduler's name.
* It runs after each scheduler's own callback. Registrations are first-wins, so a job store or thread
  pool a scheduler chose is kept. Options are last-wins, so a value set here overrides one set on a
  single scheduler, as `ConfigureAll<TOptions>` overrides a named `Configure`.
* Remote schedulers from `AddQuartzHttpClient` are skipped. Calling it with no schedulers registered is
  not an error.

`AddQuartzDashboard()` uses it to install its live-events and history plugins, so a scheduler
registered with `AddQuartz("acme", …)` now has a populated live view and execution history.

## `IScheduler` is a service, keyed by the scheduler's name

`AddQuartz()` registers `IScheduler` as well as `ISchedulerFactory`: the default scheduler unkeyed, a
named scheduler under its name. Inject it like any service:

```csharp
public sealed class ReportRunner(
    IScheduler scheduler,                                       // the default scheduler
    [FromKeyedServices("reporting")] IScheduler reporting)      // AddQuartz("reporting", …)
```

```csharp
var scheduler = provider.GetRequiredService<IScheduler>();
var reporting = provider.GetRequiredKeyedService<IScheduler>("reporting");
```

Resolving `ISchedulerFactory` and awaiting `GetScheduler()` still works.

The registered `IScheduler` is a handle, because building a scheduler is asynchronous and a container
constructs synchronously.

* Every asynchronous member waits for the scheduler to be built, so it is always safe.
* The synchronous members `SchedulerInstanceId`, `Status`, `Context` and `ListenerManager` throw
  `InvalidOperationException` if answering would require building the scheduler.
* Under `AddQuartzHostedService()` that cannot happen: every scheduler is built while the host starts.
  (Starting is a separate step, which by default waits until the application has started.)
* `SchedulerName` comes from the registration and never builds anything.

## A remote scheduler is registered by name, not by a marker interface

`AddQuartzHttpClient<TScheduler>(…)` and the runtime type generation behind it are removed. Use the
service key instead of a marker interface:

```diff
- services.AddQuartzHttpClient<IMyScheduler>("MyScheduler", "QuartzHttpClient");
- services.AddQuartzHttpClient<IMySecondScheduler>("MySecondScheduler", "QuartzHttpClient");
+ services.AddQuartzHttpClient("MyScheduler", "QuartzHttpClient");
+ services.AddQuartzHttpClient("MySecondScheduler", "QuartzHttpClient");

- var mine = provider.GetRequiredService<IMyScheduler>();
+ var mine = provider.GetRequiredKeyedService<IScheduler>("MyScheduler");
```

Delete the marker interfaces. In a container with only one remote scheduler, the first one registered
is still the unkeyed `IScheduler`.

With a local scheduler in the same container, **call `AddQuartz()` first**:

| Order | What happens |
|---|---|
| `AddQuartz()` then `AddQuartzHttpClient(…)` | The local default scheduler is `GetRequiredService<IScheduler>()`; the remote one is reached by name. Write this. |
| `AddQuartzHttpClient(…)` then `AddQuartz()` | `AddQuartz()` throws `InvalidOperationException` at registration, naming `AddQuartzHttpClient`. Before, registration was first-wins, so "the scheduler" silently became the remote one. |
| `AddQuartzHttpClient(…)` then `AddQuartz("Local", …)` | Fine in either order: a named scheduler is keyed and never uses the unkeyed slot. |

### A client is named or built, never handed over

`HttpClientOptions.HttpClient` and the `AddQuartzHttpClient(schedulerName, HttpClient, …)` overload are
removed. An `HttpClient` inside a cached, shared options object has no owner, cannot come from
`appsettings.json`, and bypasses `IHttpClientFactory` (which prevents stale DNS in a long-lived client).

| Shape | How |
|---|---|
| A named `IHttpClientFactory` client (preferred) | `AddQuartzHttpClient(name, "QuartzHttpClient")`, or `options.HttpClientName` |
| A factory of your own | `AddQuartzHttpClient(name, provider => …)`, or `options.CreateHttpClient` |

```diff
- var client = new HttpClient { BaseAddress = new Uri("http://localhost:5000/quartz-api/") };
- services.AddQuartzHttpClient("MyScheduler", client);
+ services.AddHttpClient("QuartzHttpClient", c => c.BaseAddress = new Uri("http://localhost:5000/quartz-api/"));
+ services.AddQuartzHttpClient("MyScheduler", "QuartzHttpClient");
```

To keep building the client yourself, wrap it in a factory. It runs once, when the scheduler is first
resolved, and gets the container:

```diff
- services.AddQuartzHttpClient("MyScheduler", client);
+ services.AddQuartzHttpClient("MyScheduler", _ => client);
```

* The client belongs to whoever made it; the scheduler never disposes it.
* `Quartz.HttpClient` no longer uses `System.Reflection.Emit` to generate a type at run time, which
  helps ahead-of-time compilation.
* Remote schedulers are bound into `ISchedulerRepository` under their own name when the host starts,
  not when first injected, so a dashboard or HTTP API listing shows them immediately. Without a host,
  nothing changes: the scheduler is built when first used.

## Quartz can be added to the host application builder

`AddQuartz` and `AddQuartzHostedService` have `IHostApplicationBuilder` overloads, for applications built
with `Host.CreateApplicationBuilder` or `WebApplication.CreateBuilder`. They find the configuration
section themselves:

```diff
- services.AddQuartz(builder.Configuration.GetSection("Quartz"), q => { });
- services.AddQuartzHostedService();
+ builder.AddQuartz(q => { });
+ builder.AddQuartzHostedService();
```

* The section read is `Quartz`. If your configuration is elsewhere, pass the section to the unchanged
  `IServiceCollection` overloads.
* A string is a scheduler name: `builder.AddQuartz("reporting")` registers a scheduler called
  `reporting` with settings from `Quartz:Schedulers:reporting`.
* `builder.AddQuartzSchedulers()` registers one scheduler per child of that sub-section.

## The hosted service starts every scheduler

`AddQuartzHostedService()` used to register nothing for the default scheduler if called before
`AddQuartz()`, silently: the application started and no job ran. Now the hosted service is always
registered and resolves its schedulers when the host starts, so call order does not matter. It starts
every scheduler in the container, default and named.

```diff
- services.AddQuartz(q => …);            // had to come first
  services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
+ services.AddQuartz(q => …);            // either order now
```

A container with no scheduler throws `SchedulerConfigException` at startup.

`QuartzHostedServiceOptions` are named options, keyed by scheduler name. `AddQuartzHostedService(configure)`
applies to every scheduler; per-scheduler settings are applied after the shared ones, in either call
order:

```csharp
services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
services.AddQuartzHostedService("Reporting", options => options.StartDelay = TimeSpan.FromMinutes(2));
```

`QuartzHostedService`'s constructor takes an `IServiceProvider` and an
`IOptionsMonitor<QuartzHostedServiceOptions>` instead of one factory and one options object. Update the
constructors of subclasses registered with `AddQuartzHostedService<T>()`; the
`Starting`/`Started`/`Stopping`/`Stopped` overrides are unchanged. The internal
`NamedSchedulerHostedService` is gone.

### A hosted scheduler can be started by the application

New in 4.x; nothing to migrate. `QuartzHostedServiceOptions.AutoStart` defaults to `true`. Set to
`false`, the scheduler is resolved, initialized and bound with the host but left in
`SchedulerStatus.Created` for the application to start:

```csharp
services.AddQuartzHostedService("Reporting", options => options.AutoStart = false);
```

* On 3.x, not starting a scheduler meant not registering the hosted service, which also lost shutdown
  handling.
* The scheduler is bound, so `ISchedulerRegistry`, the dashboard and `GET /schedulers` report it, and
  it is still shut down when the host stops.
* `AutoStart` wins over `AwaitApplicationStarted` and `StartDelay`.
* The health check reports a `Created` scheduler with `AutoStart` `false` as **degraded**, not
  **unhealthy**. A `Created` scheduler that should have started is still unhealthy.

It is meant for a library embedding Quartz in someone else's application; the how-to page on embedding
Quartz in a library covers the pattern.

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

The health check is in the core `Quartz` package now (see
[The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore)),
and composes with the application's other checks:

```csharp
services.AddHealthChecks()
    .AddSqlServer(connectionString)
    .AddQuartz()
    .AddQuartz("Reporting");
```

`services.AddQuartzHealthChecks()` is gone; use `services.AddHealthChecks().AddQuartz()`. A named
scheduler can also add its own check inside `AddQuartz`, which reports on that scheduler. The check is
named `quartz-scheduler-<scheduler name>` unless you name it:

```csharp
services.AddQuartz("Reporting", q => q.AddQuartzHealthChecks(options => options.Tags.Add("ready")));
```

`QuartzHealthCheckOptions` now goes through the options pipeline, so
`services.Configure<QuartzHealthCheckOptions>(...)` and a bound configuration section apply, in either
order (before, both were silently ignored). A named scheduler's options are configured under its name:

```csharp
services.Configure<QuartzHealthCheckOptions>("Reporting", options => options.Tags.Add("ready"));
```

`QuartzHealthCheckOptions.Name` is nullable; unset, the check is named after its scheduler. Setting it
still overrides that.

## `AddQuartzServer` is `AddQuartzHostedService`

`Quartz.AspNetCore.AddQuartzServer` registered both the hosted service and (where available) a health
check. It is gone; call each by name:

```diff
  services.AddQuartz(q => { /* ... */ });

- services.AddQuartzServer(options => options.WaitForJobsToComplete = true);
+ services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
+ services.AddHealthChecks().AddQuartz();
```

Both are in the core `Quartz` package, so replacing `AddQuartzServer` needs no `Quartz.AspNetCore`
reference. See
[The hosted service starts every scheduler](#the-hosted-service-starts-every-scheduler),
[The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore)
and [The ASP.NET Core methods say Quartz once](#the-asp-net-core-methods-say-quartz-once).

The overload taking `IEnumerable<string> healthCheckTags` is gone too. Add to
`QuartzHealthCheckOptions.Tags` instead:

```diff
- services.AddQuartzServer(configure, healthCheckTags: ["ready", "live"]);
+ services.AddHealthChecks().AddQuartz(options => options.Tags.AddRange(["ready", "live"]));
```

`QuartzHealthCheckOptions.StandbyStatus` changes the **verdict** for a scheduler in standby, which is
`Degraded` by default. ASP.NET Core maps `Degraded` to HTTP 200, and a worker has no endpoint to remap
it, so set this if standby nodes must leave the rotation. It covers standby only: a `Created` scheduler
with `AutoStart` `false` stays degraded. `FailureStatus` is unrelated and unchanged: it is what the
*registration* reports when the check fails.

## The OpenAPI calendar schema names the properties the payload actually uses

The OpenAPI document described `ICalendar` with a stand-in type, and two of its property names were
wrong, so a generated client could not round-trip a calendar:

| Schema said | Server sends |
|---|---|
| `calendarType` | `type` |
| `calendarBase` | `baseCalendar` |

The wire format did not change. A client regenerated against 4.x gets the two properties renamed, which
is a compile error at each use. Hand-written clients already had to send `type` and `baseCalendar`.

The other properties were already right: `description`, `timeZoneId`, `excludedDays`, `excludedDates`,
`cronExpressionString`, `rangeStart`, `rangeEnd` and `invertTimeRange`. A test in
`Quartz.Tests.AspNetCore` now checks the stand-in against those names both ways.

## Remoting a scheduler is not a Quartz concern

`ISchedulerProxyFactory` and `HttpSchedulerProxyFactory` are removed, and the `quartz.scheduler.proxy*`
and `quartz.scheduler.exporter*` keys are now rejected instead of silently ignored. Nothing in Quartz
used them after .NET Remoting went away. A configuration that still has one gets a
`SchedulerConfigException` saying what to use instead:

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

Most interfaces that returned or took a `Task` or `Task<T>` now use `ValueTask` or `ValueTask<T>`. Usually
only the signature changes:

```csharp
// 3.x
public async Task Execute(IJobExecutionContext context)

// 4.x
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
```

`Execute` also gained a `CancellationToken`; see
[Jobs take a CancellationToken](#jobs-take-a-cancellationtoken).

A missed `Task` → `ValueTask` on a listener still compiles. Every member of `IJobListener`,
`ITriggerListener` and `ISchedulerListener` has a default implementation, so a 3.x signature implements
nothing and is never called. Quartz refuses such a listener when it is registered, naming the member; see
[The compiler will not point at the callbacks you have to change, but the registration will](#the-compiler-will-not-point-at-the-callbacks-you-have-to-change-but-the-registration-will).

::: warning
Never do any of these with a `ValueTask<TResult>`:

* await it more than once;
* call `AsTask` more than once;
* use `.Result` or `.GetAwaiter().GetResult()` before it has completed, or more than once;
* combine more than one of these techniques.

For `Task` semantics, such as awaiting several times, call `.AsTask()` once and use the resulting `Task`.
:::

See [`ValueTask` on Microsoft Learn](https://learn.microsoft.com/en-us/dotnet/api/system.threading.tasks.valuetask-1).

## Upgrading an F# project

Every 4.0 signature is reachable from F#, and F# honours C# optional parameters at a call site, so
`scheduler.Start()` and `scheduler.ScheduleJob(job, trigger)` are written as in C#. Four errors are
F#-specific. F# infers parameter types, so one signature it cannot match leaves everything after it
untyped, and each of those is another error:
[ForNeVeR/nightwatch#68](https://github.com/ForNeVeR/nightwatch/pull/68) reported thirteen errors for
these four causes. Fix them from the top of the file down, and rebuild after each.

::: tip A working copy of all of this
[`src/Quartz.Examples.FSharp`](https://github.com/quartznet/quartznet/tree/main/src/Quartz.Examples.FSharp)
is this section as one console application: a job, a scheduler built by `QuartzSchedulerBuilder`, the same
scheduler built by a host, and one firing of each on the in-memory store. It is in the solution, every
`fsharp` block below is compared with it line for line, and the `ExamplesSmoke` target runs it on every
pull request.
:::

### `FS0856` — a job is implemented with both parameters

```text
error FS0856: This override takes a different number of arguments to the corresponding abstract member. The following abstract members were found:
   IJob.Execute(context: IJobExecutionContext, ?cancellationToken: System.Threading.CancellationToken) : ValueTask
```

`?cancellationToken` is how F# shows a C# optional parameter. An implementation must write the whole
signature. Do not mark the parameter optional with F#'s leading `?`: that declares an `option` and gives
`FS0001: This expression was expected to have type 'CancellationToken' but here has type 'CancellationToken option'`.

Write both parameters with type annotations. The `FS0072` and `FS0008` errors further down the file come
from this one and go away with it.

<!-- Not a compiled sample: `Quartz.Documentation.Samples` is a C# project.
     Copied from src/Quartz.Examples.FSharp/GreetJob.fs:27 — FSharpHowToTest fails when the two stop
     matching. -->

```fsharp
member _.Execute(context: IJobExecutionContext, cancellationToken: CancellationToken) : ValueTask =
    ValueTask(
        task {
            do! Task.Delay(TimeSpan.FromMilliseconds 1.0, cancellationToken)
            printfn "GreetJob fired at %O" context.FireTimeUtc
            Firings.record ()
        }
    )
```

FSharp.Core has no `valueTask { }` builder, so pass a `task { }` to `ValueTask`'s constructor. A 3.x
`upcast task { … }` does not work: `ValueTask` is a struct, not a supertype of `Task`, and the result is
`FS0013: The static coercion from type Task<unit> to 'a ... involves an indeterminate type`.

### `FS0041` — `ScheduleJob` has to know what it is being handed

```text
error FS0041: A unique overload for method 'ScheduleJob' could not be determined based on type information prior to this program point. A type annotation may be needed.

Known types of arguments: IJobDetail * 'a

Candidates:
 - (extension) SchedulerJobExtensions.ScheduleJob<'TJob,'TInput when 'TJob :> IJob<'TInput>>(input: 'TInput, at: System.DateTimeOffset, ?options: OneOffJobOptions, ?cancellationToken: System.Threading.CancellationToken) : ValueTask<ScheduledOneOffJob>
 - (extension) SchedulerJobExtensions.ScheduleJob<'TJob,'TInput when 'TJob :> IJob<'TInput>>(input: 'TInput, delay: System.TimeSpan, ?options: OneOffJobOptions, ?cancellationToken: System.Threading.CancellationToken) : ValueTask<ScheduledOneOffJob>
 - IScheduler.ScheduleJob(jobDetail: IJobDetail, trigger: ITrigger, ?options: ScheduleJobOptions, ?cancellationToken: System.Threading.CancellationToken) : ValueTask<System.DateTimeOffset>
 - IScheduler.ScheduleJob(jobDetail: IJobDetail, triggersForJob: System.Collections.Generic.IReadOnlyCollection<ITrigger>, ?options: ScheduleJobOptions, ?cancellationToken: System.Threading.CancellationToken) : ValueTask
```

The `'a` in *Known types of arguments* is the cause: the trigger is an inferred function parameter, so F#
cannot choose an overload. C# does not hit this, because a C# parameter has a declared type.

4.0 added candidates. `options` gained a default, so every candidate matches two arguments
([the surviving overloads](#the-builder-surface-says-each-thing-once)), and `SchedulerJobExtensions` adds
two typed-input overloads ([a typed job can be scheduled in one call](#a-typed-job-can-be-scheduled-in-one-call)).

Annotate the value, as a parameter, a `let` binding or at the call:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` is a C# project.
     Copied from src/Quartz.Examples.FSharp/Standalone.fs:39 — FSharpHowToTest fails when the two stop
     matching. -->

```fsharp
let scheduleOne (scheduler: IScheduler) (job: IJobDetail) (trigger: ITrigger) : Task<DateTimeOffset> =
    task {
        return! scheduler.ScheduleJob(job, trigger)
    }
```

The same applies to every overloaded asynchronous member of `IScheduler`, since each ends with at least one
optional parameter.

### `FS0041` — `Async.AwaitTask` has no `ValueTask` overload

```text
error FS0041: No overloads match for method 'AwaitTask'.

Known type of argument: ValueTask

Available overloads:
 - static member Async.AwaitTask: task: Task -> Async<unit> // Argument 'task' doesn't match
 - static member Async.AwaitTask: task: Task<'T> -> Async<'T> // Argument 'task' doesn't match
```

Every `Async.AwaitTask(scheduler.…)` in a 3.x F# project reports this. The fix depends on the computation
expression.

Inside `task { }`, bind the `ValueTask` directly:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` is a C# project.
     Copied from src/Quartz.Examples.FSharp/Standalone.fs:45 — FSharpHowToTest fails when the two stop
     matching. -->

```fsharp
let start (scheduler: IScheduler) : Task<unit> =
    task {
        do! scheduler.Start()
    }
```

Inside `async { }`, call `AsTask()` once (see the warning above) and await the `Task`:

<!-- Not a compiled sample: `Quartz.Documentation.Samples` is a C# project.
     Copied from src/Quartz.Examples.FSharp/Standalone.fs:52 — FSharpHowToTest fails when the two stop
     matching. -->

```fsharp
let stop (scheduler: IScheduler) : Async<unit> =
    async {
        do! scheduler.Shutdown(waitForJobsToComplete = true).AsTask() |> Async.AwaitTask
    }
```

Prefer `task { }` in new code: `AsTask()` allocates on every call. From a `task { }`, run an existing
`Async<unit>` such as `stop` with `do! Async.StartAsTask(stop scheduler)`.

### `FS0039` — `StdSchedulerFactory` is gone, and its replacement is not constructed

```text
error FS0039: The value or constructor 'StdSchedulerFactory' is not defined. Maybe you want one of the following:
   StandaloneSchedulerFactory
   ISchedulerFactory
```

`StandaloneSchedulerFactory` is the type you end up holding, but its constructor is internal, so calling
it gives `FS0801: This type has no accessible object constructors`. Build it with `QuartzSchedulerBuilder`
(the C# form is [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone)):

<!-- Not a compiled sample: `Quartz.Documentation.Samples` is a C# project.
     Copied from src/Quartz.Examples.FSharp/Standalone.fs:10 — FSharpHowToTest fails when the two stop
     matching. -->

```fsharp
let buildFactory () : StandaloneSchedulerFactory =
    QuartzSchedulerBuilder
        .Create(fun q ->
            q
                .ConfigureScheduler(fun options -> options.InstanceName <- "fsharp-example")
                .UseInMemoryStore()
            |> ignore)
        .Build()
```

An F# lambda converts to the `Action<IQuartzBuilder>` directly. Pipe each fluent call into `ignore`. F#
has no `await using`: dispose the factory, which owns the service provider, with
`do! factory.DisposeAsync()` inside a `task { }`.

With a container, use the same callback through `AddQuartz`
([the standalone builder is the same builder](#the-standalone-builder-is-the-same-builder)):

<!-- Not a compiled sample: `Quartz.Documentation.Samples` is a C# project.
     Copied from src/Quartz.Examples.FSharp/Hosting.fs:21 — FSharpHowToTest fails when the two stop
     matching. -->

```fsharp
builder.Services
    .AddQuartz(fun q ->
        q
            .UseInMemoryStore()
            .ScheduleJob<GreetJob>(fun trigger ->
                trigger.WithIdentity("greet-hosted", "fsharp").StartNow() |> ignore)
        |> ignore)
    .AddQuartzHostedService(fun options -> options.WaitForJobsToComplete <- true)
|> ignore
```

## SystemTime Replaced with TimeProvider

`SystemTime` is removed. To supply a custom time source (e.g. for testing), give the scheduler a
`TimeProvider`:

```csharp
// 3.x
SystemTime.UtcNow = () => new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);

// 4.x — use TimeProvider
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q.UseTimeProvider(new FakeTimeProvider()))
    .BuildScheduler();
```

Under a host, call it on the `AddQuartz` builder:

```csharp
services.AddQuartz(q => q.UseTimeProvider(new FakeTimeProvider()));
```

### The clock is a construction parameter, which is an API-shape change

`SystemTime.UtcNow` was a process-wide mutable field. A `TimeProvider` is given to a scheduler when it is
built, and cannot be swapped in later. So:

* code of yours that wraps scheduler construction needs a way to be told which clock to use;
* a trigger built by `TriggerBuilder.Create()` with no argument reads `TimeProvider.System`, so a component
  that builds triggers outside the container needs the clock passed in. See
  [The trap: triggers built outside the container](tutorial/time-and-timeprovider.md#the-trap-triggers-built-outside-the-container).

### Replacing a `SystemTime` *offset*, not a freeze

`FakeTimeProvider` (from `Microsoft.Extensions.TimeProvider.Testing`) starts stopped, moves only when
advanced, and cannot move backwards. Use it when a test owns the whole clock, not when something else in
the process still reads the real one.

3.x code that assigned an *offset*, such as `SystemTime.UtcNow = () => DateTimeOffset.UtcNow + skew`,
needs a clock that keeps running:

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

`CreateTimer` delegates to the real clock on purpose: a timer that fires only when the offset moves would
stall every wait inside the scheduler, as `FakeTimeProvider` does. With a frozen clock, advance it and then
wait for the scheduler to react instead of sleeping; see
[Controlling time](tutorial/testing.md#controlling-time).

### A clock belongs to one scheduler

`UseTimeProvider` used to replace the container's `TimeProvider` registration, re-timing every scheduler in
the container. It now applies to its own scheduler only:

```csharp
services.AddQuartz("reporting", q => q.UseTimeProvider(new FakeTimeProvider()));
services.AddQuartz("billing", q => …);   // still on the system clock
```

A scheduler without its own clock asks the container, so an application-wide `TimeProvider` registration
still reaches every scheduler. Precedence, most specific first:

| Where the clock comes from | Beats |
|---|---|
| `UseTimeProvider(...)` on that scheduler's builder | everything below |
| a `TimeProvider` registered in the container | the key and the default |
| `quartz.timeProvider.type` | the default |
| `TimeProvider.System` | — |

`quartz.timeProvider.type` used to override a clock chosen in code, because it replaced the registration
after the configuration callback ran. Like every flat key naming an implementation, it is now only a
fallback.

Triggers built by `q.AddTrigger(...)` and `q.ScheduleJob(...)` use their scheduler's clock, so a trigger with
no start time starts at that scheduler's current time.

### The scanning jobs take a clock and speak `DateTimeOffset`

`DirectoryScanJob` and `FileScanJob` compare a file's last write time with "now" to decide whether it has
settled. Both take a `TimeProvider` for it:

```csharp
public DirectoryScanJob(TimeProvider? timeProvider = null);
public DirectoryScanJob(IServiceProvider serviceProvider, TimeProvider? timeProvider = null);
public FileScanJob(TimeProvider? timeProvider = null);
```

The job factory passes the container's `TimeProvider`, a scheduler's own included. `null` means
`TimeProvider.System`, so `new DirectoryScanJob()` still compiles.

They work in `DateTimeOffset`, not local `DateTime`:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `protected virtual DateTime FileScanJob.GetLastModifiedDate(string)`; `DateTime.MinValue` for a missing file | `protected virtual DateTimeOffset? GetLastModifiedTime(string fileName)`; `null` for a missing file |
| `protected void DirectoryScanJob.GetUpdatedOrNewFiles(string, DateTime, DateTime, IReadOnlyCollection<FileInfo>, out List<FileInfo>, out List<FileInfo>, out List<FileInfo>, string, bool)` | private, with the `DirectoryScanResult` it returns |

Neither `GetUpdatedOrNewFiles` nor `DirectoryScanJob.Execute` was `virtual`, so no subclass could use them.
Use `IDirectoryScanListener`, which receives the files.

`LAST_MODIFIED_TIME` in these jobs' job data is written as a `DateTimeOffset`. A `DateTime` written by an
earlier version is still read, so files already seen are not reported again.

### The shipped jobs are configured by name

Each job in `Quartz.Jobs` has an options record mapped onto its job data keys, and an extension that
writes it:

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

* Stored data does not change: the extensions write the same keys and `FromJobData` reads them, so a job
  scheduled by 3.x reads identically, including string values from `StoreJobDataAsStrings`.
* Key-by-key configuration still works. `SEARCH_PATTERN` and `INCLUDE_SUB_DIRECTORIES`, previously
  undocumented `internal const`s, are `public const`s.
* `MinimumUpdateAge` is a `TimeSpan` in the options and a millisecond count in the map, as before.
* The extensions are generic in the configurator, so a `JobBuilder<TJob>` chain still ends in `Build()` and
  `AddJob`'s `IJobConfigurator<TJob>` still chains its own members.

### `DirectoryScanJob` finds its listener among what you registered

`DIRECTORY_SCAN_LISTENER_NAME` used to be resolved by calling `GetTypes()` on **every loaded assembly** for
a type with that simple name, caching every answer for the process lifetime. The job now looks in three
places, in order:

| How you register the listener | What to put in `ScanListenerName` |
|---|---|
| `AddKeyedSingleton<IDirectoryScanListener>("inbox", …)` | `"inbox"` |
| `AddSingleton<IDirectoryScanListener, InboxListener>()` | `nameof(InboxListener)` |
| `scheduler.Context["inbox"] = listener` | `"inbox"` |

**`AddSingleton<InboxListener>()` alone no longer resolves**: register it under the interface, or keyed.
The failure names the listener and the three places searched.

### `DirectoryScanJob` stores its file list as something a job store can write

`CURRENT_FILE_LIST`, kept by this `[PersistJobDataAfterExecution]` job, was a `List<FileInfo>`, which
neither shipped serializer can read back and both refuse. The first firing against a persistent store
failed to persist, and `GET …/jobs/{group}/{name}` refused the map. It is now a
`Dictionary<string, string>` of full path to last-write ticks. A `List<FileInfo>` left in a running
scheduler's in-memory map is still read.

### The SMTP password does not belong in job data

`SendMailJob` read its credential from the `smtp_username` and `smtp_password` job data entries, which a
persistent store writes to `QRTZ_JOB_DETAILS` and the dashboard shows. Register it in the container:

```csharp
CredentialCache credentials = new();
credentials.Add("smtp.example.com", 587, "Basic", new NetworkCredential("mailer", smtpPassword));
services.AddSingleton<ICredentialsByHost>(credentials);
```

**Register a `CredentialCache` bound to the server, not a bare `NetworkCredential`.** Whoever schedules the
job chooses `smtp_host`, and a `NetworkCredential` answers `ICredentialsByHost.GetCredential` with itself
for *every* host. The job asks the credential for the host it connects to:

| What you register | A job pointed at a host it covers | A job pointed at any other host |
|---|---|---|
| `CredentialCache` with an entry for the host | authenticates with that entry | sends unauthenticated |
| bare `NetworkCredential` | — | **refuses to send**, naming the host and `CredentialCache` |

* `SendMailOptions` has no user name or password.
* With nothing registered, the two job data keys are still read, so jobs scheduled by an earlier version
  keep sending, and the job logs a warning saying where the credential now belongs.
* A credential from the container wins over one in job data.
* `SendMailOptions.EnableSsl` (`smtp_enable_ssl`) is new, `false` by default as in `SmtpClient`. Turn it on
  for anything that authenticates: SMTP `AUTH LOGIN` is base64, not encryption.

| 3.x | 4.x |
|---|---|
| `SendMailJob()` | `SendMailJob(ICredentialsByHost? credentials = null)`, filled from the container by the job factory |
| `MailInfo.SmtpUserName` / `MailInfo.SmtpPassword` | `MailInfo.Credentials`, a `NetworkCredential?` resolved for `MailInfo.SmtpHost` |
| `protected virtual MailMessage BuildMessageFromParameters(JobDataMap data)` | `protected virtual MailMessage BuildMessage(SendMailOptions options)` |
| `protected virtual string GetRequiredParameter(JobDataMap, string)`, `GetOptionalParameter(JobDataMap, string)` | removed; `SendMailOptions.FromJobData` reads the data and reports the same missing key |

An override of `Send` that routes mail through another transport gets the credential that applied, in
`MailInfo.Credentials`.

### `NativeJob` no longer redirects a stream nobody reads

`NativeJob` redirected both of the child's streams even with `ConsumeStreams` off, and read them only with
it on. With the defaults (`ConsumeStreams` off, `WaitForProcess` on), a process that filled a pipe buffer
blocked for ever, and so did the Quartz worker thread in `WaitForExit`. Now a stream is redirected only when
something reads it, and the wait is `WaitForExitAsync(cancellationToken)`, so a shutdown reaches the job.
`NativeJob.Execute` is `async`; the override points are unchanged.

## Logging

LibLog is replaced by `Microsoft.Extensions.Logging.Abstractions`. A standalone scheduler is given an
`ILoggerFactory`, here with a simple console logger:

```csharp
var loggerFactory = LoggerFactory.Create(builder =>
    {
        builder
            .SetMinimumLevel(LogLevel.Debug)
            .AddSimpleConsole();
    });
LogProvider.SetLogProvider(loggerFactory);
```

**Under a host there is nothing to do**: `AddQuartz` registers the scheduler's parts in your container, and
they get the host's `ILoggerFactory`. `LogProvider.SetLogProvider` is needed only for the few types nothing
can inject; see [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient).

The console tour's [`Logging.cs`](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Examples/Logging.cs)
sets up [Serilog](https://serilog.net/), NLog and Microsoft.Logging behind Quartz. For Microsoft.Logging
itself, see [Microsoft docs](https://docs.microsoft.com/en-us/dotnet/core/extensions/logging).

### Every message carries an event id

Every shipped package logs through source-generated `[LoggerMessage]` methods: nothing is formatted or
boxed when the level is off, and every message has a stable event id to filter or alert on. The ranges are
stable from 4.0; [Log Events](log-events.md) lists every id.

| Range | Area |
|---|---|
| 1000–1999 | Scheduler core: the scheduler, its firing loop, the job run shell, the signaler, the error listener |
| 2000–2999 | `RAMJobStore` |
| 3000–3499 | ADO.NET store, its connections and its driver delegate |
| 3500–3599 | Clustering: check-in, failed-instance detection and recovery |
| 3600–3699 | Misfire handling |
| 3700–3799 | Lock handlers |
| 4000–4999 | Configuration, dependency injection and hosting, including thread pools and job factories |
| 5000–5999 | Serialization, type loading, triggers, calendars, XML scheduling data and the utilities |
| 6000–6199 | The history plugins, `LoggingJobHistoryPlugin` and `LoggingTriggerHistoryPlugin` |
| 6200–6299 | The XML scheduling data plugin |
| 6300–6399 | The JSON scheduling data plugin and its processor |
| 7000–7399 | `Quartz.Jobs`: directory scan job (7000–7099), file scan job (7100–7199), native job (7200–7299), send mail job (7300–7399) |
| 8000–8099 | `Quartz.Extensions.Redis`, the Redis lock handler |
| 9000–9099 | `Quartz.AspNetCore`, the HTTP API |

6400–6599 belonged to the interrupt monitor and shutdown hook plugins, which are retired. A job's timeout
is now core middleware logging `1090`–`1092`; see
[`JobInterruptMonitorPlugin` is retired; a job timeout is middleware](#jobinterruptmonitorplugin-is-retired-a-job-timeout-is-middleware).

Levels and message templates are otherwise **unchanged from 3.x**. The exceptions:

* **Cluster recovery counts.** The six messages of `JobStoreSupport.LogWarnIfNonZero` logged at
  Information when non-zero and Debug when zero. They are now Warning events, raised only for a non-zero
  count.
* **Four typos fixed.** `Removed  {Count} 'complete' triggers.` lost a double space;
  `complete triggers(s)` became `complete trigger(s)`; `Found {TriggerGroupDeleteCount}delete trigger group
  commands.` gained its space; and the "trigger already exists" message, spelled with double spaces in one
  place and single in the other, is one event with single spaces. Update queries that match on this text.
* **`NativeJob` output.** Each line went through one template, `{Type}>{Line}`, with `Type` holding
  `stdout` or `stderr`. There is now one event per stream: `stdout>{Line}` at Information (7201) and
  `stderr>{Line}` at Warning (7202). The rendered text and the levels are unchanged; a structured sink that
  indexed `Type` should filter on the id.
* **History plugins.** `LoggingJobHistoryPlugin` and `LoggingTriggerHistoryPlugin` format your configured
  `{0}`-style templates (`JobSuccessMessage` and its siblings) as before, and log the result through an
  event whose template is `{Message}`. The rendered text is unchanged, and each occurrence has an id:
  6000–6003 for the job plugin, 6010–6012 for the trigger plugin. `StructuredLoggingJobHistoryPlugin` and
  `StructuredLoggingTriggerHistoryPlugin` keep plain `ILogger` calls and have no event ids, since their
  templates carry *named* placeholders for a structured sink to capture.

### Each scheduling path logs under its own category

Jobs and triggers declared in a scheduling file, a JSON scheduling file, or with `AddJob`/`AddTrigger` in
`AddQuartz` all used to log under `Quartz.Xml.XmlSchedulingDataProcessor`, because
`ContainerConfigurationProcessor` and `JsonSchedulingDataProcessor` passed on its logger. Each path now
logs under its own category:

| What declared them | Category |
|---|---|
| a scheduling file | `Quartz.Xml.XmlSchedulingDataProcessor` |
| `AddQuartz(q => q.AddJob…)` | `Quartz.Configuration.ContainerConfigurationProcessor` |
| a JSON scheduling file | `Quartz.Plugins.Json.JsonSchedulingDataProcessor` |

The **event ids are unchanged**, so a filter on an id still matches all three. A filter that silenced
`Quartz.Xml` to quiet the container path's messages, such as `Adding 2 jobs, 2 triggers`, no longer does.

## The ambient logger factory stays ambient

`LogProvider.SetLogProvider(ILoggerFactory)` is the one piece of process-wide mutable state left in
Quartz, kept on purpose.

Everything a scheduler is made of is built by the container and injected an `ILogger`: `QuartzScheduler`
and its scheduling loop, the signaler and error listener, the ADO.NET job store with its cluster manager,
misfire handler, units of work, driver delegate and lock handler, and whatever `Use*<T>()` chooses (thread
pool, job factory, type loader, instance id generator). **Under `AddQuartz`, none of that needs this
setting.** Only these types read it:

| Type | Why it cannot be injected |
|---|---|
| `JobChainingJobListener` | constructed by the caller |
| `CronTriggerImpl` | a trigger, which may be deserialized from a job store |
| `TimeZones`, `MisfireInstructionNames`, `FileUtil`, `QuartzEnvironment` | static helpers, reached from parsing and deserialization with no scheduler in scope |
| The jobs in `Quartz.Jobs`, and anything else a caller constructs directly | constructed by the caller |

With a standalone `QuartzSchedulerBuilder`, setting it configures everything: the builder's container has
no logging providers unless you register some on `builder.Services`, so its `ILoggerFactory` forwards to
this setting. A registered provider takes precedence.

The setting is never filled from a container. It outlives any one container, so a process that builds a
host, disposes it and builds another (every integration test suite, every configuration reload) would keep
a disposed `ILoggerFactory`, and the next logger created would throw `ObjectDisposedException`. Whoever
sets it owns its lifetime: `LogProvider.SetLogProvider(host.Services.GetRequiredService<ILoggerFactory>())`
is correct if the host outlives the schedulers, and under a host it is needed only for the types above.

`TimeZones.AddResolver` is process-wide for the same reason: `FindById` runs while parsing a
`CronExpression` and deserializing a trigger, with no scheduler in scope. Installing
`Quartz.Plugins.TimeZoneConverter` in one scheduler changes id resolution for the whole process, and a
registration is undone by disposing it; see
[`TimeZoneUtil` became `Quartz.TimeZones`](#timezoneutil-became-quartz-timezones).

## Job execution metrics

::: danger EVERY NAME CHANGED
Every instrument and every attribute Quartz publishes was renamed in 4.0, and two of the four instruments
were removed. **Dashboards, alerts and recording rules built on the 3.x names all break.** The
[old → new table](#old-and-new-telemetry-names) below is the complete mapping. Nothing is emitted under
both names.
:::

Instruments moved from `scheduling.quartz.*` to OpenTelemetry-style `quartz.*` names; attributes such as
`job.name` and `trigger.group` gained the `quartz.` prefix so they cannot collide with another library's;
and the duration is in seconds. Every scheduler now publishes them: meter configuration was tied to
`StdSchedulerFactory`, so a scheduler registered with `AddQuartz` emitted nothing.

| Instrument | Type | Unit | Tags |
|---|---|---|---|
| `quartz.job.execution.duration` | `Histogram<double>` | `s` | `quartz.scheduler.name`, `quartz.scheduler.id`, `quartz.trigger.group`, `quartz.trigger.name`, `quartz.job.group`, `quartz.job.name`, `quartz.execution.group` when the trigger names one, **+ `error.type`** when the execution failed |
| `quartz.job.execution.active` | `UpDownCounter<long>` | `{job}` | the same identity attributes |

More instruments cover misfires, retries, acquisition, cluster check-in and recovery, and the job
store; the [observability page](packages/opentelemetry-integration.md#metrics) has the whole set.

### Old and new telemetry names

| 3.x | 4.x | Notes |
|---|---|---|
| *(none)* | `quartz.trigger.misfire` | New. `Counter<long>` of firings that did not happen on time |
| *(none)* | `quartz.trigger.acquisition.duration` | New. `Histogram<double>` of the scheduling loop's waits on its store |
| *(none)* | `quartz.trigger.acquired` | New. `Counter<long>` of what those rounds returned |
| *(none)* | `quartz.cluster.checkin.duration` | New. `Histogram<double>` of each cluster check-in attempt |
| *(none)* | `quartz.cluster.recovery.trigger` | New. `Counter<long>` of fired-trigger rows recovered from a failed node |
| *(none)* | `quartz.jobstore.operation.duration` | New. `Histogram<double>` of every store round trip, tagged with the operation |
| `scheduling.quartz.execute` | *removed* | Use the duration histogram's **count**: `sum(rate(quartz_job_execution_duration_count[5m]))` in Prometheus |
| `scheduling.quartz.execute.errors` | *removed* | Use the **`error.type`-tagged subset** of that count, which also says what failed |
| `scheduling.quartz.execute.active` | `quartz.job.execution.active` | Now an `UpDownCounter<long>` (was `Counter<long>`), unit `{job}` (was `ea`) |
| `scheduling.quartz.execute.duration` | `quartz.job.execution.duration` | **Unit `s`, not `ms`**: a chart with a hard-coded millisecond axis reads 1000× low |
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
| *(none)* | `quartz.execution.group` | New. On the execution span and both execution instruments, only when the trigger names a group |
| *(none)* | `quartz.jobstore.operation` | New. Which store operation a `quartz.jobstore.operation.duration` measurement is about |
| *(none)* | `quartz.cluster.recovered.instance.id` | New. The failed node a `quartz.cluster.recovery.trigger` measurement is about; the reporting node is `quartz.scheduler.id` |
| `scheduling.quartz.exception_type` | `error.type` | Not namespaced, and its value changed too; see below |
| `Quartz.Job.Vetoed` | `Quartz.Job.Veto` | Span name for a vetoed fire |

* `Quartz.Job.Execute` is unchanged, and so is every `Quartz.JobStore.*` span name 3.x used. Every store
  emits those now, not only the ADO.NET one, and there are 33 of them since the group forms of pause and
  resume [got names of their own](#pausing-by-matcher-is-a-group-operation-and-is-named-for-one).
* A backend cannot rename a series for you. A Prometheus recording rule from the old name to the new one,
  or a dashboard variable, keeps history readable across the upgrade.
* **The constants did not move.** `Quartz.Diagnostics.ActivityTags.JobName` is still
  `ActivityTags.JobName`; its *value* changed. Code that reads tag names through the constants is
  unaffected; queries written against the strings need rewriting.

### The two strings you subscribe with are constants now

`Quartz.Diagnostics.QuartzInstrumentation` publishes the `ActivitySource` and `Meter` names, which used to
be internal:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
    .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
```

Both are still `"Quartz"`, so an existing `AddSource("Quartz")` keeps working.

The nine instrument names are constants on the nested `QuartzInstrumentation.Instruments`:
`JobExecutionActive`, `JobExecutionDuration`, `TriggerMisfire`, `TriggerRetry`,
`TriggerAcquisitionDuration`, `TriggerAcquired`, `ClusterCheckinDuration`, `ClusterRecoveryTrigger` and
`JobStoreOperationDuration`. Their values are unchanged, and a test keeps the constants and the meter's
instruments equal in both directions.

### Two instruments, not four

* **`scheduling.quartz.execute` and `scheduling.quartz.execute.errors` are gone.** The duration
  histogram's count is the number of executions, and its `error.type`-tagged subset is the number of
  failures.
* **The duration is in seconds**, as OpenTelemetry records durations. In milliseconds, every execution
  longer than ten seconds landed in the top default bucket. `quartz.job.execution.active` is `{job}`
  rather than `ea`, which is not a UCUM unit.
* **Every measurement is tagged with `quartz.scheduler.name`**, so several schedulers in one process are
  separate series: one per scheduler where there was one in total. A query that aggregated across
  everything must now say so, such as `sum without (quartz_scheduler_name)` in Prometheus. The values are
  the configured scheduler names, a small fixed set.
* **And with `quartz.scheduler.id`**, since a cluster's nodes share one scheduler name. A slow node, one
  that stopped firing, or one with slow check-ins shows without a trace. That is one series per node; the
  values are the cluster's instance ids.
* **Instruments are created through the container's `IMeterFactory`** when it has one, as every
  generic-host application does. The meter was a process-wide static shared by every container; each
  container now publishes to its own, which lets `MetricCollector` (from
  `Microsoft.Extensions.Diagnostics.Testing`) see them. The meter's name is unchanged, and without
  `AddMetrics()` Quartz creates the meter directly, as before. Quartz registers no `IMeterFactory` of its
  own, so it never displaces the application's, whatever the order of `AddMetrics()` and `AddQuartz`.
* **`quartz.job.execution.active` is an up-down counter.** A `Counter` is monotonic, so an exporter could
  drop its decrements and a "jobs running" chart only climbed. Rebuild dashboards and alerts on a
  non-monotonic series: a `Sum` with `IsMonotonic = false` in the OpenTelemetry SDK, a gauge in Prometheus.
* **`scheduling.quartz.exception_type` is now
  [`error.type`](https://opentelemetry.io/docs/specs/semconv/registry/attributes/error/), and names the
  exception the job threw.** In 3.x the tag never reached the errors counter, and its value was the
  `JobExecutionException` the job run shell wraps every failure in. Now a job that throws
  `InvalidOperationException` reports `System.InvalidOperationException`, not the
  `JobExecutionException` -> `JobExecutionProcessException` -> cause chain; a job that throws a
  `JobExecutionException` itself reports `Quartz.JobExecutionException`. The value is a fully-qualified
  type name only, to keep cardinality bounded. **Rewrite a query or alert on
  `scheduling.quartz.exception_type`**; one that expected `JobExecutionException` now sees the thrown type.
* `error.type` is on `quartz.job.execution.duration`, not on `quartz.job.execution.active`, whose increment
  and decrement must carry identical attributes for the series to return to zero.
* A vetoed fire is not an execution and appears in neither instrument. Vetoes show in traces as a
  `Quartz.Job.Veto` span.
* The execution span carries the same `error.type` value, and still records the exception as an event
  with the whole wrapper chain and its stack traces.

## JSON Serialization

Replace `UseJsonSerializer` with `UseSystemTextJsonSerializer` or `UseNewtonsoftJsonSerializer`, and remove
the old `Quartz.Serialization.Json` package reference:

```csharp
// 3.x
q.UseJsonSerializer();

// 4.x — System.Text.Json (included in main package)
q.UseSystemTextJsonSerializer();

// 4.x — Newtonsoft.Json (requires Quartz.Serialization.Newtonsoft package)
q.UseNewtonsoftJsonSerializer();
```

### Deserialization failures surface as `Quartz.JsonSerializationException`

Both serializers report every deserialization failure as `Quartz.JsonSerializationException`, a
`SchedulerException`, with the payload in the message and the parse failure as `InnerException`. On 3.x,
`Newtonsoft.Json.JsonSerializationException` and `Newtonsoft.Json.JsonReaderException` could escape
`IObjectSerializer.DeSerialize`, because Quartz's same-named type shadowed Newtonsoft's inside the package
and the wrapping `catch` never matched. Catch the Quartz type around `IObjectSerializer.Deserialize` or any
job store read that uses it:

```csharp
// 3.x
catch (Newtonsoft.Json.JsonSerializationException e)

// 4.x — the same failure, whichever serializer is configured
catch (Quartz.JsonSerializationException e)
```

### Both serializers refuse a job data value they cannot read back

The System.Text.Json serializer reads a stored job data value back only as a string, bool, int, long,
double, null or `Dictionary<string, string>`; there is no polymorphic `object` deserialization, which keeps
trimmed publishes safe. A `JobDataMap` holding a `List<string>` or a `Dictionary<string, object>` used to
be written anyway and then fail with `JsonSerializationException` when the job next ran. Writing now
refuses it, naming the entry and the type:

```text
Job data entry 'recipients' holds a System.Collections.Generic.List`1[[System.String, …]], which Quartz's
JSON format cannot read back. A job data value has to be one of the types JobDataMap declares an accessor
for (string, bool, char, int, long, float, double, decimal, DateTime, DateTimeOffset, TimeSpan, Guid,
DateOnly, TimeOnly or an enum), a Dictionary<string, string>, or a type the application declares through
SystemTextJsonSerializerRegistry.AddTypeInfoResolver. Anything with structure of its own has to be
serialized by the job and stored as a string.
```

* Allowed: the types `DataMapExtensions` has an accessor for, `Dictionary<string, string>`, and enums.
* Declare a type of your own with `AddTypeInfoResolver`, which a trimmed or native-AOT publish needs
  anyway. It is then written like any other, and read back as the string map the store format gives for
  any object.
* The HTTP API shares the job data map converter, so the check applies there too, whatever the store,
  `RAMJobStore` included: a `GET` of such a job fails with the message above, where the server used to send
  a payload `Quartz.HttpClient` could not read. `AddTypeInfoResolver` fixes both ends.

**Newtonsoft refuses the same set**, checked against the same declaration, so both serializers accept the
same values. That includes `TimeZoneInfo`, which Json.NET wrote as
`{"$type":"System.TimeZoneInfo, …","Id":"Tokyo Standard Time", …}` and could not read back. Only the remedy
in the message differs:

```text
Job data entry 'zone' holds a System.TimeZoneInfo, which Quartz's JSON format cannot read back. … or a
type the application declares through NewtonsoftJsonSerializerRegistry.AddJobDataValueType. …
```

`AddJobDataValueType<T>()` is this package's counterpart to `AddTypeInfoResolver`; it only records that the
type reads back:

```csharp
store.UseNewtonsoftJsonSerializer(json => json.AddJobDataValueType<ReportOptions>());
```

Three kinds of value used to round-trip through Newtonsoft and now need that declaration: a class of your
own, a `JobKey` or `TriggerKey` held as a job data value, and a collection. A `TimeZoneInfo` and a nested
`JobDataMap` cannot be declared, since Json.NET cannot read either back: store a zone's `Id`, and serialize a
nested structure to a string in the job. `StoreJobDataAsStrings` is unaffected.

### A string dictionary is written the same way by both serializers

The Newtonsoft serializer used to write a `Dictionary<string, string>` job data value with its type:

```json
{"headers":{"$type":"System.Collections.Generic.Dictionary`2[[System.String, System.Private.CoreLib],[System.String, System.Private.CoreLib]], System.Private.CoreLib","tenant":"acme"}}
```

Read by System.Text.Json, that map gained a `$type` entry holding an assembly-qualified type name. 4.0's
Newtonsoft serializer writes the plain object, byte for byte what System.Text.Json writes:

```json
{"headers":{"tenant":"acme"}}
```

* **Both readers read both forms**, so existing blobs keep loading. No migration is needed.
* 3.x's Newtonsoft reader returns a Json.NET `JObject` instead of the dictionary for the new form. This
  matters only while a 3.x node reads what a 4.0 node wrote; see
  [A mixed 3.x and 4.0 window](operations.md#a-mixed-3-x-and-4-0-window).
* **`$type` is a reserved name inside a stored string map.** Both serializers refuse such a map when it is
  written:

```text
Job data entry 'headers' holds a string map with an entry named '$type'. '$type' is the name Json.NET
writes a value's type under, so Quartz's JSON format reads it as metadata rather than as an entry and no
reader can hand it back. Store that entry under a name of its own.
```

Such a map never round-tripped, so no working data is lost.

### Custom trigger and calendar serializers are no longer static

The static registration methods, which wrote to process-global dictionaries, are removed. Register through
the store builder; the registration belongs to that scheduler alone:

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

If you construct a serializer yourself, pass it a registry:

```csharp
var registry = new SystemTextJsonSerializerRegistry()
    .AddCalendarSerializer<CustomCalendar>(new CustomCalendarSerializer());

var serializer = new SystemTextJsonObjectSerializer(registry);
```

* The registries start with every built-in trigger and calendar type. The parameterless serializer
  constructors still exist and use the built-ins only.
* The callback parameter is the registry itself, `SystemTextJsonSerializerRegistry` or
  `NewtonsoftJsonSerializerRegistry`, as the constructors take. The `SystemTextJsonSerializerOptions` and
  `NewtonsoftJsonSerializerOptions` wrappers are removed. A lambda body like the one above compiles
  unchanged; retype only an explicitly typed lambda parameter or variable.
* Newtonsoft's `RegisterTriggerConverters` is now a parameter of the extension method:

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

The HTTP API, the dashboard and `Quartz.HttpClient` serialize triggers but belong to no single scheduler,
so they no longer see a scheduler's custom serializers. Register a container-wide registry as a singleton:

```csharp
services.AddSingleton(new SystemTextJsonSerializerRegistry()
    .AddTriggerSerializer<CustomTrigger>(new CustomTriggerSerializer()));
```

See [Serialization (System.Text.Json)](packages/system-text-json) for the full picture.

### A serialized trigger carries its preferred node

Both JSON trigger serializers write a trigger's [node affinity](tutorial/node-affinity.md) pin, as the pair
the `QRTZ_TRIGGERS` row stores:

```json
"PreferredNode": "node-1",
"PreferredNodeAuto": false
```

* `PreferredNode` is the node name: `null` when unpinned, `*` for an auto pin no node has claimed yet.
* `PreferredNodeAuto` says whether the node holding the pin claimed it automatically. Only an automatic pin
  is released when its node stops checking in.
* A payload without these fields, 3.x's included, reads back as `PreferredNode.None` (unpinned).

Before, the HTTP API, `Quartz.HttpClient`, the dashboard and a custom `IJobStore` persisting serialized
triggers dropped the pin, so scheduling or rescheduling a pinned trigger over HTTP unpinned it. The ADO.NET
job store keeps the pin in `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` and reapplies it to every trigger it
reads, blob triggers included, so it was not affected.

### A Newtonsoft-serialized trigger keeps its time zone

With `registerTriggerConverters` off, the `UseNewtonsoftJsonSerializer` default, a trigger's `TimeZoneInfo`
was written as its read-only public members and read back as nothing, so the trigger silently fell back to
`TimeZoneInfo.Local`: one stored under `Tokyo Standard Time` fired in the reading machine's zone. A
converter on every property *typed* `TimeZoneInfo` now writes the zone id, the spelling the trigger and
calendar serializers use:

```diff
- "TimeZone": { "Id": "Tokyo Standard Time", "DisplayName": "(UTC+09:00) Osaka, Sapporo, Tokyo", … }
+ "TimeZone": "Tokyo Standard Time"
```

* Both forms are read; an object-form blob takes its zone from `Id`.
* Affected: `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl`. Not
  `CronTriggerImpl`, whose zone rides on the `CronExpression` and its own converter.
* Only typed members change. A `TimeZoneInfo` stored as a *value* in a `JobDataMap` is refused when it is
  written (see [Both serializers refuse a job data value they cannot read back](#both-serializers-refuse-a-job-data-value-they-cannot-read-back)).
  Store the zone's id instead.
* The ADO.NET job store persists every shipped trigger type through a persistence delegate, not a blob, so
  this affects only a custom trigger type it serializes, or code passing a trigger to `IObjectSerializer`.
* The private `timeZoneInfoId` helpers on those triggers never worked and are removed.

### Newtonsoft types moved out of the core namespaces

Types in `Quartz.Serialization.Newtonsoft` moved out of namespaces that looked like the core package's.
Both packages had a `Quartz.JsonConfigurationExtensions`, which made `UseNewtonsoftJsonSerializer`
ambiguous when both were referenced.

| 3.x | 4.x |
|---|---|
| `Quartz.JsonConfigurationExtensions` (Newtonsoft) | `Quartz.NewtonsoftJsonConfigurationExtensions` |
| `Quartz.Serialization.Json.Triggers.ITriggerSerializer`, `TriggerSerializer<T>`, the built-in trigger serializers | `Quartz.Serialization.Newtonsoft.Triggers.*` |
| `Quartz.ICalendarSerializer`, `Quartz.CalendarSerializer<T>` | `Quartz.Serialization.Newtonsoft.Calendars.*`, the shape of `Quartz.Serialization.SystemTextJson.Calendars` |
| `Quartz.Converters.NameValueCollectionConverter` | internal; the serializer registers it, as the System.Text.Json package always has |

In 4.x the *core* package owns `Quartz.Serialization.SystemTextJson.Triggers`, the namespace nearest the old
one. `UseNewtonsoftJsonSerializer` itself is unchanged; only a `using` naming these types changes.

`AddCalendarSerializer<TCalendar>` is constrained to `ICalendar`, like the trigger side, and in both
packages takes a `CalendarSerializer<TCalendar>` instead of an `ICalendarSerializer`, so the calendar type
and its serializer must agree:

```diff
- // compiled fine, then threw InvalidCastException on the first calendar that round-tripped
- json.AddCalendarSerializer<HolidayCalendar>(new AnnualCalendarSerializer());
+ json.AddCalendarSerializer(new HolidayCalendarSerializer());   // TCalendar is inferred
```

Correctly paired calls compile unchanged, and the type argument can now be inferred.

`AddTriggerSerializer<TTrigger>` keeps `ITriggerSerializer`, so a serializer derived from a built-in one
still fits: `class ReportTriggerSerializer : CronTriggerSerializer`, for a
`ReportTrigger : CronTriggerImpl`, is a `TriggerSerializer<CronTriggerImpl>`, and `TriggerSerializer<T>` is
invariant.

`ICalendarSerializer` gained `CalendarTypeName`, a default interface member returning empty, so an existing
implementation compiles and is matched by the calendar's assembly-qualified type name, as in 3.x. When a
serializer supplies a name, the registry indexes both; the assembly-qualified key stays because 3.x
`CALENDARS.CALENDAR` payloads carry it. Calendar lookups are now case-insensitive, as on the trigger side
and in the System.Text.Json package.

### The OpenAPI trigger schema describes the whole payload

The HTTP API's OpenAPI document describes triggers through a stand-in type, since OpenAPI cannot describe
`ITrigger`. It lacked five properties the server has always sent:

| Property | Present on |
|---|---|
| `nextFireTimeUtc` | every trigger |
| `previousFireTimeUtc` | every trigger |
| `executionGroup` | every trigger |
| `timesTriggered` | every trigger type except `CronTrigger` |
| `recurrenceRule` | `RecurrenceTrigger` |

`RecurrenceTrigger` was also missing from the `triggerType` discriminator's values.

* The wire format is unchanged. A client regenerated against 4.x gains the five properties.
* The scheduler computes the two fire times and overwrites any value sent. `executionGroup`,
  `timesTriggered` and `recurrenceRule` are read from what you send.
* A test in `Quartz.Tests.AspNetCore` compares the stand-in with what each built-in trigger type
  serializes to, in both directions.

## Sealed and Internalized Types

Many types are now sealed or internal. If you extended one, file an issue asking for it to be reopened. The
changes most likely to reach existing code:

**`QuartzScheduler` and `QuartzSchedulerResources` are internal**, as is `StdScheduler`'s constructor.
Resolve `IScheduler` or `ISchedulerFactory`; the settings from `QuartzSchedulerResources` are on
`QuartzSchedulerOptions`.

**`StdAdoConstants` and `IAdoUtil` are internal, and constants are no longer inherited.** `AdoConstants`
stays public, for delegate authors, but is a `static class`, and `AdoJobStoreBase`, `StdAdoDelegate` and
`DbLockHandler` no longer derive from it or from `StdAdoConstants`:

```diff
  public class MyDelegate : StdAdoDelegate
  {
-     private string CountRows() => $"SELECT COUNT(*) FROM {TablePrefixSubst}{TableTriggers}";
+     private string CountRows() => $"SELECT COUNT(*) FROM {{0}}{AdoConstants.TableTriggers}";
  }
```

* The `Sql*` statement templates on `StdAdoConstants` are not visible any more. Build the statement your
  dialect needs, or override the unchanged `GetSelect*Sql` hooks.
* `AdoConstants` names **every** table, now including `TableSimplePropertiesTriggers`
  (`"SIMPROP_TRIGGERS"`), which 3.x declared only as `protected` on
  `SimplePropertiesTriggerPersistenceDelegateSupport`. Startup schema validation walks `AdoConstants`, so a
  database missing only `QRTZ_SIMPROP_TRIGGERS` used to start and fail on the first calendar-interval,
  daily-time-interval or recurrence trigger; it is now validated with the other eleven. The `protected`
  spelling stays on `SimplePropertiesTriggerPersistenceDelegateBase`, so a subclass compiles unchanged.
* `DbLockHandler.AdoUtil` is `private protected`. A lock handler of your own implements `ExecuteSql` with
  these `protected` members of `DbLockHandler`:

```csharp
protected DbCommand PrepareCommand(ConnectionAndTransactionHolder conn, string commandText);
protected void AddCommandParameter(DbCommand command, string paramName, object? paramValue);
```

The shipped `SelectForUpdateLockHandler` and `UpdateRowLockHandler` use them, so they serve as examples.
There is no overload with a provider-specific type or a size: a lock statement binds two strings. A
handler that does not lock in a database implements `ILockHandler` directly.

**Three trigger persistence delegates are public**: `CronTriggerPersistenceDelegate`,
`SimpleTriggerPersistenceDelegate` and `DailyTimeIntervalTriggerPersistenceDelegate` join
`CalendarIntervalTriggerPersistenceDelegate` and `RecurrenceTriggerPersistenceDelegate`, so a custom
delegate list can name all five. All five are `sealed`; write your own against
`SimplePropertiesTriggerPersistenceDelegateBase` or `ITriggerPersistenceDelegate`.

**`SchedulerConstants` is a static class**, not a struct. **Every public options type is `sealed`**,
including `QuartzOptions`, `SchedulingOptions`, `QuartzHostedServiceOptions`, `QuartzHttpApiOptions` and
`HttpClientOptions`; Quartz constructs each itself, so a derived type was never used. **`MisfireInstruction`
is internal**; see [the enums are the vocabulary](#the-enums-are-the-vocabulary).

**`HttpScheduler` is `sealed`.** To intercept calls, wrap it in an `IScheduler` of your own.

**Quartz.Dashboard's Blazor components are not API.** They are `public` because Razor requires it, and are
excluded from the dashboard's public-API baseline. Build against `QuartzDashboardOptions`,
`AddQuartzDashboard` and the model types.

**The `Quartz.Xml.JobSchedulingData20` namespace is gone.** Its fourteen classes, generated by `xsd.exe`
from `job_scheduling_data_2_0.xsd`, were public only for `XmlSerializer`, and no Quartz API used them:
`QuartzXmlConfiguration20`, `abstractTriggerType`, `calendarIntervalTriggerType`, `cronTriggerType`,
`entryType`, `jobdatamapType`, `jobdetailType`, `jobschedulingdataSchedule`, `preprocessingcommandsType`,
`preprocessingcommandsTypeDeletejob`, `preprocessingcommandsTypeDeletetrigger`,
`processingdirectivesType`, `simpleTriggerType` and `triggerType`. The processor now reads the document
itself, and both the model and the processor are internal.

**The XML format has not changed.** The schema, elements and attributes are as before, and
`job_scheduling_data_2_0.xsd` still validates the document before it is read. Two failures report
differently:

| Input | 3.x / earlier 4.0 preview | 4.0 |
|---|---|---|
| A file that is not well-formed XML | `InvalidOperationException`, "There is an error in XML document (3, 13)", wrapping an `XmlException` | the `XmlException` itself, naming the line, the position and the unclosed elements |
| A file whose elements are not in the `http://quartznet.sourceforge.net/JobSchedulingData` namespace | `InvalidOperationException`, "&lt;job-scheduling-data xmlns=''&gt; was not expected" | `SchedulerConfigException` naming the expected namespace |

A schema violation still throws `SchedulingDataValidationException` with every error, and
`XmlSchedulingDataProcessorPlugin` still wraps failures in a `SchedulerException`, so a plugin-based setup
sees no change.

**The XML trigger kinds are frozen** at `simple`, `cron` and `calendar-interval`. Daily time interval
triggers and recurrence rules are available only in the JSON format. **XML scheduling is not deprecated and
stays in 4.x.** Write a schedule that needs another trigger kind as JSON: `UseJsonSchedulingConfiguration`
takes the same `FileSchedulingOptions` as its XML twin.

4.1 added three optional trigger *settings* to the schema, `<execution-group>`, `<retry-policy>` and
`<preferred-node>`; see [Upgrading from 4.0 to 4.1](#upgrading-from-4-0-to-4-1). A 4.0 file is unaffected.

### The shipped plugins are sealed

`LoggingJobHistoryPlugin`, `LoggingTriggerHistoryPlugin`, `StructuredLoggingJobHistoryPlugin`,
`StructuredLoggingTriggerHistoryPlugin`, `XmlSchedulingDataProcessorPlugin`,
`JsonSchedulingDataProcessorPlugin` and `NoOpJob` are `sealed`. Implement `ISchedulerPlugin` (four
members) directly; every shipped plugin is example code for one.

* `XmlSchedulingDataProcessorPlugin.TypeLoader` is private (was `protected`). Pass an `ITypeLoader` to the
  constructor.
* `LoggingJobHistoryPlugin` and `LoggingTriggerHistoryPlugin` lost the protected `IsInfoEnabled` /
  `IsWarnEnabled` / `WriteInfo` / `WriteWarning` hooks. Pass an `ILogger` to the
  `(ILogger<T>, TimeProvider)` constructor instead.

### The shipped implementations are sealed

These were `public class`es with `virtual` members in 3.x:

| Sealed in 4.x | Derive from this instead |
|---|---|
| `AnnualCalendar`, `CronCalendar`, `DailyCalendar`, `HolidayCalendar`, `MonthlyCalendar`, `WeeklyCalendar` | `BaseCalendar`, which stays open, or `ICalendar` |
| `JobExecutionContextImpl`, and its nine `virtual` members | `IJobExecutionContext`: a test double implements it, a decorator wraps one |
| `DefaultThreadPool`, `ZeroSizeThreadPool` | `IThreadPool`, or `TaskSchedulingThreadPool`, which stays open; see [The thread pool is asynchronous](#the-thread-pool-is-asynchronous) |
| `JobChainingJobListener` | The listener interfaces, whose members all have defaults; see [The three `*Support` base classes are gone](#the-three-support-base-classes-are-gone) |

**The five `*TriggerImpl` types stay open.** A custom trigger derives from `TriggerBase` or any of them
(`HasAdditionalProperties` is `virtual` on `TriggerBase`), paired with a serializer derived from that
trigger's built-in JSON serializer. Those serializers are public and unsealed in both packages, so a
subclass calls `base.SerializeFields` / `base.DeserializeFields` to keep the built-in fields' stored shape.
`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl` were sealed
during 4.x's development and reopened for this; see [Other Breaking Changes](#other-breaking-changes).

`JobChainingJobListener` now implements `IJobListener` directly, since `Quartz.Listener.JobListenerSupport`
is gone, so its `Name` and `JobWasExecuted` are no longer `override`s. Replace a subclass with an
`IJobListener` that holds one and forwards.

`RAMJobStore` is sealed too, with a replacement seam; see [`RAMJobStore` is sealed](#ramjobstore-is-sealed).

## TriggerBase Property Removals

`AbstractTrigger` is renamed **`TriggerBase`**. It is abstract, so it never appears in configuration or as
a JSON `$type` value; update the base list and recompile. A `BinaryFormatter` payload names a private
base-class field `AbstractTrigger+field`, so a 3.x `BLOB_TRIGGERS` payload cannot be read on 4.x with the
compatibility package: do that part of a binary migration on 3.x, the recommended path anyway. See
[Migrating from binary serialization](packages/json-serialization.md#migrating-from-binary-serialization).

These properties are removed from `TriggerBase`, being redundant with `Key` and `JobKey`:

| Removed property | Replacement |
|---|---|
| `Name` | `Key.Name` |
| `Group` | `Key.Group` |
| `JobName` | `JobKey.Name` |
| `JobGroup` | `JobKey.Group` |
| `FullName` | `Key.ToString()` |
| `FullJobName` | `JobKey.ToString()` |

`HasMillisecondPrecision` moved from `ITrigger` to `protected abstract` on `TriggerBase`; when it is false,
the base class rounds the start time down to the second. In a custom trigger, change `public override` to
`protected override`. In a test, assert on `StartTimeUtc.Millisecond` instead of the flag.

`TriggerBase.FireInstanceId` and `IOperableTrigger.FireInstanceId` are `string?`. A store sets the id as it
hands a trigger over, so a trigger straight from `TriggerBuilder`, or read back without ever firing, has
always returned `null` there. Your own store or trigger may need a `!` or a null check.
`IJobExecutionContext.FireInstanceId` stays non-nullable.

## A blank calendar name is no calendar name

`TriggerBase.CalendarName` stores an empty or whitespace-only name as `null`, and so do
`TriggerBuilder.WithCalendarName` and `TriggerDetailsUpdate.WithCalendarName`. Names are not trimmed; only
blanks collapse.

Before, every job store treated `""` as a calendar to look up, did not find it, and dropped the fire, so
the trigger never fired again. The only sign was a `Couldn't find calendar with name ''` line from the ADO
delegate, and nothing under `RAMJobStore`. Oracle, where `''` is `NULL`, was not affected.

`CALENDAR_NAME = ''` rows already in a database (the dashboard's reschedule wrote them before
[#3294](https://github.com/quartznet/quartznet/issues/3294) was fixed) need **no migration script**. They
load as "no calendar", fire on the next acquisition, and are written back as `NULL` the next time the
trigger is persisted.

The normalization, and the missing-calendar warning both stores now log, also ship on 3.x. Only on 4.x:

* `IScheduler.RescheduleJob` throws `SchedulerException` when the new trigger names a calendar that does
  not exist, as `ScheduleJob` always has. 3.x stores a trigger that never fires. Add the calendar before
  rescheduling onto it.

## The two job stores answer the same way

`JobStoreContractTest` runs one set of assertions against `RAMJobStore` and the ADO.NET job store. Making
them agree changed these 3.x behaviours:

* **A prefix `PauseTriggerGroups` pauses every group it matches.** `RAMJobStore` recorded the matcher's
  text, so `PauseTriggerGroups(GroupMatcher<TriggerKey>.GroupStartsWith("report"))` paused and returned
  only the first matching group, and left a paused group literally named `report`. It now records and
  pauses every matched group, as the ADO store did. `ResumeTriggerGroups` with a non-equality matcher now
  clears those groups; it used to clear only an exact-name pause.

  On both stores, a pause remembers the **groups matched**, not the pattern. A trigger added later to a
  matched group is born paused; one added to a group that did not exist at pause time is not. To pause a
  group before it exists, pause it by exact name: `GroupEquals` records an empty group.

* **A paused job group applies to jobs added later, on the ADO store too.** The ADO store checked only the
  trigger's group when storing a trigger, so a job added to a paused group ran while `IsJobGroupPaused`
  said paused. It now checks the job's group too, and such a trigger is born `PAUSED`, or
  `PAUSED_BLOCKED`. Not ported: `RAMJobStore` lets a job resumed individually inside a paused group keep
  running, while the ADO store re-applies the group's pause the next time that job's triggers are stored.

* **Pausing no longer discards an error.** `RAMJobStore` paused a trigger from any state but `Complete`,
  overwriting `TriggerState.Error` so that `ResetTriggerFromErrorState` found nothing. Now only `Normal`
  (waiting), acquired and blocked triggers pause, as on the ADO store. A trigger in error keeps its state,
  and `PauseTrigger` returns false.

  Resetting such a trigger while its group is paused lands it in `Paused`, not `Normal`, on both stores.
  To suppress a failing trigger, pause it and then reset it.

* **`ResumeAll` on the ADO store resumes groups that hold no triggers.** It cleared only groups found in
  `QRTZ_TRIGGERS`, so a group paused while empty, such as
  `PauseTriggerGroups(GroupEquals("nightly"))` before anything is scheduled into `nightly`, kept its
  `QRTZ_PAUSED_TRIGGER_GRPS` row and paused everything scheduled into it later. `ResumeAll` now clears the
  scheduler's whole table, as `RAMJobStore` did. Remove a 3.x leftover by hand with
  `DELETE FROM QRTZ_PAUSED_TRIGGER_GRPS WHERE SCHED_NAME = '…'`.

* **The all-groups-paused marker is not listed as a group.** The ADO store records `PauseAll` as a row
  named `_$_ALL_GROUPS_PAUSED_$_`, which `GetPausedTriggerGroups()` and
  `QueryTriggerGroups(new TriggerGroupQuery { Paused = true })` returned. The listing and its count now
  leave it out; the marker and the schema are unchanged. `RAMJobStore` never had such a row.

* **A duplicate raises `ObjectAlreadyExistsException` on both stores.** For `AddCalendar` over an existing
  name without `Replace = true`, and `AddJob` over an existing key without `replace: true`, the ADO store
  wrapped it in a plain `JobPersistenceException`, with the real one as `InnerException`. Code catching
  `JobPersistenceException`, its base type, is unaffected.

* **The threshold instant itself is a misfire on the ADO store too.** A trigger is late once its fire time
  is *at or before* `now - MisfireThreshold`. The ADO store's periodic sweep used
  `NEXT_FIRE_TIME < @nextFireTime`, while `RAMJobStore` and the ADO store's single-trigger path used `<=`.
  Acquisition moved from `NEXT_FIRE_TIME >= @noEarlierThan` to `>`, so a trigger belongs to either
  acquisition or misfire handling. SQL only, no schema migration. A trigger due at exactly that instant now
  misfires instead of firing late without its policy. 3.x keeps the old behaviour.

* **An unblocked trigger's misfire policy is applied as it is unblocked, in memory too.** When a
  `[DisallowConcurrentExecution]` job finishes, its `Blocked` triggers return to `Waiting`, and one that
  passed its fire time meanwhile has its misfire policy applied before `TriggeredJobComplete` returns, as
  the ADO store always did. `RAMJobStore` deferred it to the next acquisition, so a `GetTrigger` in between
  showed the missed fire time. An unblocked trigger left with nothing to fire is removed rather than kept
  in `Complete`, so `GetTrigger` returns `null` for it. 3.x keeps the old behaviour.

* **A persistent store refuses a repeat interval it cannot store exactly.** Duration columns hold whole
  milliseconds, and `StdAdoDelegate.GetDbTimeSpanValue` cast `TotalMilliseconds` to `long`, so a
  `SimpleTrigger` repeat interval under a millisecond was stored as `0`. That trigger then threw
  `DivideByZeroException` from `GetFireTimeAfter`, which the store swallowed, leaving the row `ACQUIRED` for
  ever. The ADO store now raises `ArgumentException` naming the trigger, the column and the rule; round the
  interval to whole milliseconds. `RAMJobStore` still accepts it, the one intended difference. See
  [#3673](https://github.com/quartznet/quartznet/issues/3673).

## JobKey and TriggerKey Null Validation

`JobKey` and `TriggerKey` throw `ArgumentNullException` for a `null` `name` or `group`, so a trigger can no
longer have a null group name. Use an explicit group name.

## JobDataMap and SchedulerContext stand alone

`JobDataMap` and `SchedulerContext` no longer derive from
`Quartz.Util.StringKeyDirtyFlagMap : DirtyFlagMap<string, object>`, which is internal now. They are sealed
dictionaries implementing `IDictionary<string, object?>` and `IReadOnlyDictionary<string, object?>`.

* The typed read accessors (`GetInt`, `GetString`, `Get<T>` and the rest) are extension members in the
  `Quartz` namespace, declared in `DataMapExtensions`, for `JobDataMap` and `SchedulerContext` only, not
  every `IReadOnlyDictionary<string, object?>`. `map.GetInt("retries")` compiles unchanged. A variable typed
  as the removed base class does not: type it `JobDataMap`, or `IDictionary<string, object?>`.
* `Dirty` and `ClearDirtyFlag()` are internal. `ClearDirtyFlag()` in a `[PersistJobDataAfterExecution]` job
  silently skipped re-storing the data. To force a rewrite, put a `SchedulerConstants.ForceJobDataMapDirty`
  entry in the source dictionary when constructing the map (the binary-to-JSON migration recipe does this).
* 3.x's `Get(TKey key)` is gone. Use the indexer or `TryGetValue`.
* `JobDataMap.Equals`/`GetHashCode` compare content, and equal maps hash equally. 3.x compared key sets
  only, so a nested map with new values did not mark the outer map dirty and the job store rewrite was
  skipped. `SchedulerContext` compares by reference.
* `SchedulerContext` is backed by a `ConcurrentDictionary<string, object?>`, so it is safe to read, write
  and enumerate concurrently.

The binary-serialized shape of `JobDataMap` is unchanged (`[Serializable]`, its serialization constructor,
the `version`/`dirty`/`map` entries), so 3.x `JOB_DATA` and `BLOB_TRIGGERS` blobs still load; see
[`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it).

`IsReadOnly` is an explicit interface implementation. `IsFixedSize`, `SyncRoot` and `IsSynchronized` are
gone; see
[`JobDataMap` dropped the non-generic collection interfaces](#jobdatamap-dropped-the-non-generic-collection-interfaces).

## `JobDataMap` dropped the non-generic collection interfaces

`JobDataMap` and `SchedulerContext` (on 3.x, their `DirtyFlagMap<TKey, TValue>` base) no longer implement
`System.Collections.IDictionary` or `System.Collections.ICollection`, whose untyped members threw
`InvalidCastException` instead of `ArgumentException` for a key of the wrong type (#1417).

| 3.x | 4.x |
|---|---|
| `((IDictionary) map).Add(key, value)` | `map.Add(key, value)` |
| `((IDictionary) map)[key]` | `map[key]` |
| `((IDictionary) map).Contains(key)` | `map.ContainsKey(key)` |
| `((IDictionary) map).Remove(key)` | `map.Remove(key)` |
| `map.CopyTo(array, index)` (`Array`) | `map.CopyTo(KeyValuePair<string, object?>[], index)` |
| `new JobDataMap(someIDictionary)` | `new JobDataMap(someIDictionaryOfStringToObject)` |

`ISerializable` is untouched, so persisted maps still load. The generic
`JobDataMap(IDictionary<string, object?>)` constructor treats a `QRTZ_FORCE_JOB_DATAMAP_DIRTY` entry as
the removed one did: the entry is not copied, and the new map is flagged dirty.

A `decimal` can now be read back, with `Get<decimal>` / `TryGet<decimal>`; see
[One accessor per type](#one-accessor-per-type-for-the-types-job-data-is-made-of).

## `JobDataMap`'s typed accessors are extension members

`JobDataMap`'s sixty accessors of its own are gone: `GetIntValue`, `TryGetIntValue`,
`GetIntValueFromString`, `TryGetIntValueFromString`, and the same four for `bool`, `char`, `double`,
`float`, `long`, `Guid`, `TimeSpan`, `DateTime` and `DateTimeOffset`. The shorter set that its base,
`StringKeyDirtyFlagMap`, declared remains, as extension members in the `Quartz` namespace (in
`DataMapExtensions`, for `JobDataMap` and `SchedulerContext`):

```diff
- int retries = context.JobDetail.JobDataMap.GetIntValue("retries");
+ int retries = context.JobDetail.JobDataMap.GetInt("retries");

- if (map.TryGetTimeSpanValue("timeout", out TimeSpan timeout)) { }
+ if (map.TryGet("timeout", out TimeSpan timeout)) { }
```

| 3.x `JobDataMap` | 4.x extension members |
|---|---|
| `GetBooleanValue`, `GetBooleanValueFromString` | `GetBoolean` |
| `GetCharFromString` | `Get<char>` |
| `GetDateTimeValue`, `GetDateTimeValueFromString` | `Get<DateTime>` |
| `GetDateTimeOffsetValue`, `GetDateTimeOffsetValueFromString` | `GetDateTimeOffset` |
| `GetDoubleValue`, `GetDoubleValueFromString` | `GetDouble` |
| `GetFloatValue`, `GetFloatValueFromString` | `GetFloat` |
| `GetGuidValue`, `GetGuidValueFromString` | `Get<Guid>` |
| `GetIntValue`, `GetIntValueFromString` | `GetInt` |
| `GetLongValue`, `GetLongValueFromString` | `GetLong` |
| `GetTimeSpanValue`, `GetTimeSpanValueFromString` | `Get<TimeSpan>` |
| every `TryGet…Value` / `TryGet…ValueFromString` | the matching `TryGet…`, or `TryGet<T>` |
| `GetNullableGuidValue` | `TryGet<Guid>`, or read the entry and test it yourself |
| (none) | `GetString`, `TryGetString`, `Get<decimal>`, `TryGet<decimal>` |
| your own `TryGetValue<T>` extension | **keep it** |

* `TryGetValue<T>` was always your own extension on `IDictionary<string, object>` (3.x's `JobDataMap` had
  none), and it still binds. Do not replace it with `TryGet<T>`, which behaves differently; see below.
* The `…FromString` variants are not needed: the accessors parse a value stored as a string, as
  `StoreJobDataAsStrings` and `PutAsString` store them.
* `GetNullableGuidValue` returned `null` both when absent and when not a `Guid`; `TryGet<Guid>` tells them
  apart.

`PutAsString`'s eleven overloads are one generic `PutAsString<T>(string key, T value) where T :
IFormattable`, plus explicit overloads for `bool`, `char`, `DateTime`, `DateTimeOffset`, `DateOnly`,
`TimeOnly`, `Guid` and `TimeSpan`. Call sites are unchanged, with two exceptions described below.

### One accessor per type, for the types job data is made of

**`Get<T>`, `TryGet<T>` and `GetValueOrDefault<T>` coerce**, by the named accessors' rule: the stored type
first, then the invariant string form of every type `PutAsString` writes, an enum by name
(case-insensitively), and `Convert` for any other stored type. A type with no Quartz string form, such as
your own options class, is a plain type test. In 4.0's previews they were only a type test, so
`Get<Guid>` failed on a `PutAsString` value that `GetGuid` parsed.

The previews' rarer named accessors (33 per receiver in all) are replaced by the generic forms:

| Named accessor | 4.0 |
|---|---|
| `GetGuid` / `TryGetGuid` | `Get<Guid>` / `TryGet<Guid>` |
| `GetTimeSpan` / `TryGetTimeSpan` | `Get<TimeSpan>` / `TryGet<TimeSpan>` |
| `GetDecimal` / `TryGetDecimal` | `Get<decimal>` / `TryGet<decimal>` |
| `GetChar` / `TryGetChar` | `Get<char>` / `TryGet<char>` |
| `GetDateTime` / `TryGetDateTime` | `Get<DateTime>` / `TryGet<DateTime>` |
| `GetDateOnly` / `TryGetDateOnly` | `Get<DateOnly>` / `TryGet<DateOnly>` |
| `GetTimeOnly` / `TryGetTimeOnly` | `Get<TimeOnly>` / `TryGet<TimeOnly>` |
| `GetEnum<TEnum>` / `TryGetEnum<TEnum>` | `Get<TEnum>` / `TryGet<TEnum>` |

Named accessors kept, on both `JobDataMap` and `SchedulerContext`: `GetInt`, `GetLong`, `GetFloat`,
`GetDouble`, `GetBoolean`, `GetString` and `GetDateTimeOffset`, each with a `TryGet…` twin, plus the three
generic readers. That is 17 members per receiver instead of 33, and everything readable before is still
readable, including under `StoreJobDataAsStrings = true`.

`Get<T>` throws `KeyNotFoundException` naming the key when there is no entry, and `InvalidCastException`
naming the key, the stored type and the requested type when the entry cannot be read as a `T`.
`TryGet<T>` returns `false` for either; `GetValueOrDefault<T>(key, defaultValue)` returns the fallback.

::: warning `GetValueOrDefault<T>` and `TryGet<T>` changed answer for a string
They coerce now. `map.GetValueOrDefault("retries", -1)` on an entry holding `"42"` returned `-1` in the
previews and returns `42` in 4.0, and `TryGet<int>` on it returns `true` rather than `false`. Code that
used the old behaviour to detect "stored as a string" needs `map.TryGet("key", out string _)`.
:::

### `PutAsString` writes round-trip formats now

`PutAsString(key, dateTimeOffset)` wrote the invariant general form, without fractional seconds, and a
`DateTime` argument bound to the `IConvertible` overload, losing sub-second precision *and*
`DateTimeKind`. Both now write the round-trip ("O") format, exact to the tick, with Kind and offset. The
accessors read both forms, but:

* **Overload rebinding.** `map.PutAsString(key, someDateTime)` stored `"01/02/2026 15:04:05"` through
  `IConvertible`; it now binds to the `DateTime` overload and stores `"2026-01-02T15:04:05.0000000"`. Code
  outside Quartz that reads `JOB_DATA` strings sees the new shape once the value is next written.
* **`Get<DateTime>` parses with `DateTimeStyles.RoundtripKind`**, whatever wrote the value. A stored string
  ending in `Z` came back converted to local time with `Kind=Local`; it now keeps the UTC reading with
  `Kind=Utc`. A job computing `DateTime.Now - map.Get<DateTime>(key)` on such a value shifts by the local
  UTC offset. The System.Text.Json serializer writes a boxed UTC `DateTime` as `"…Z"` and returns it as a
  string, so such values exist.

### `PutAsString<T>` is constrained to `IFormattable`

The generic overload requires `IFormattable` instead of `IConvertible`; it only ever formatted the value.
More types qualify: `Int128`, `Half`, `BigInteger`, `Complex` and your own formattable types.

* `bool` and `char` are not `IFormattable`, so they have dedicated overloads, writing what they did before:
  `"True"` / `"False"`, and the single character.
* The six round-trip overloads are unchanged.
* `map.PutAsString(key, someString)` no longer compiles. Write `map[key] = someString`, which is what it
  did.

### `PutAsString(string, Guid?)` is gone

Passing `null` stored an entry nothing could read: `TryGet<Guid>` returned `false`, `Get<Guid>` threw, and
`StoreJobDataAsStrings = true` turned it into an empty string with the same result. Call
`PutAsString(key, value.Value)` when there is a value; otherwise decide explicitly, usually
`map.Remove(key)`.

## Listener API Changes

All three kinds of listener are registered under a name, replaced by registering that name again, and
removed by name.

| 3.x | 4.x |
|---|---|
| `AddJobListener(l, params IMatcher<JobKey>[])` and `AddJobListener(l, IReadOnlyCollection<…>)` | one `AddJobListener(l, params IReadOnlyCollection<IMatcher<JobKey>>)` |
| `AddTriggerListener(l, params IMatcher<TriggerKey>[])` and the collection overload | one `AddTriggerListener(l, params IReadOnlyCollection<IMatcher<TriggerKey>>)` |
| `GetJobListeners()`, `GetTriggerListeners()`, `GetSchedulerListeners()` → arrays | → `IReadOnlyList<T>` |
| `AddJobListenerMatcher`, `RemoveJobListenerMatcher`, `SetJobListenerMatchers`, `GetJobListenerMatchers` | gone; see [Matchers are given at registration](#matchers-are-given-at-registration) |
| `AddTriggerListenerMatcher`, `RemoveTriggerListenerMatcher`, `SetTriggerListenerMatchers`, `GetTriggerListenerMatchers` | gone; same |
| `GetJobListener(name)`, `GetTriggerListener(name)` throw `KeyNotFoundException` | return null |
| `RemoveSchedulerListener(ISchedulerListener)` | `RemoveSchedulerListener(string name)` |
| — | `GetSchedulerListener(string name)` → `ISchedulerListener?` |

C# 13 params collections let the single `Add*Listener` member take both call shapes, so existing calls
compile unchanged:

```csharp
// both still work
scheduler.ListenerManager.AddJobListener(myJobListener, matcherA, matcherB);
scheduler.ListenerManager.AddJobListener(myJobListener, listOfMatchers);
```

A listener that is not registered comes back as null instead of throwing:

```diff
- try { var listener = listenerManager.GetJobListener(name); } catch (KeyNotFoundException) { }
+ IJobListener? listener = listenerManager.GetJobListener(name);
```

### Matchers are given at registration

`AddJobListenerMatcher`, `RemoveJobListenerMatcher`, `SetJobListenerMatchers`, `GetJobListenerMatchers`
and their trigger twins are gone. Pass matchers to `Add*Listener(listener, params matchers)`, or through the
container to `AddJobListener<T>(matchers)`. To change a listener's matching, register it again under the
same name, which replaces the listener and its matchers together:

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

`IListenerManager` has no replacement for `GetJobListenerMatchers`; keep the matchers you registered with.

### The listener property keys are retired

`quartz.jobListener.<name>.type` and `quartz.triggerListener.<name>.type` are **rejected**, with a message
naming the registration that replaces them:

```diff
- ["quartz.jobListener.audit.type"] = "Acme.AuditListener, Acme",
- ["quartz.triggerListener.audit.type"] = "Acme.AuditTriggerListener, Acme",
```

```csharp
services.AddQuartz(q =>
{
    q.AddJobListener<AuditListener>(GroupMatcher<JobKey>.GroupEquals("reports"));
    q.AddTriggerListener<AuditTriggerListener>();
});
```

Unlike the keys, a registration can take matchers (a configured listener heard everything), and it does
not resolve a type from a string, which trimmed and native-AOT publishes cannot follow. When porting:

* **The listener supplies its name.** The key's `<name>` was written onto the listener's `Name`. Declare
  `Name => "audit"`, or keep the `GetType().Name` default. Two listeners of one type with different names
  are two instances registered with `AddJobListener(listener, matchers)`.
* **The other `quartz.jobListener.<name>.<property>` keys are gone too.** A container-built listener takes
  what it needs through its constructor, and its options through `IOptions<T>`.

`PropertyListenerFactory`, which was internal, is gone with them.

### The broadcast listeners are gone

`BroadcastJobListener`, `BroadcastTriggerListener` and `BroadcastSchedulerListener` are removed; the
listener manager already notifies every registered listener. Register the listeners individually:

```diff
- scheduler.ListenerManager.AddJobListener(new BroadcastJobListener("audit", [first, second]));
+ scheduler.ListenerManager.AddJobListener(first);
+ scheduler.ListenerManager.AddJobListener(second);
```

Remove each by its own name, where one `RemoveJobListener` used to detach a whole broadcast. A broadcast
swallowed what a listener behind it threw; the manager does not.

### Listeners are told which scheduler is calling

Every listener callback can now reach the scheduler that calls it, so one listener registered with
several schedulers in one host, such as `AddQuartz("acme", …)` and `AddQuartz("initech", …)`, can tell them
apart (#3063). **A callback inside a firing reaches it through `IJobExecutionContext.Scheduler`; any other
callback receives it as an argument:**

* first, in all twenty-three `ISchedulerListener` members;
* second, after the trigger, in `ITriggerListener.TriggerMisfired`, the one trigger callback with no
  execution context; see [`TriggerMisfired` takes the trigger first](#triggermisfired-takes-the-trigger-first).

```diff
- public ValueTask SchedulerStarted(CancellationToken cancellationToken = default)
+ public ValueTask SchedulerStarted(IScheduler scheduler, CancellationToken cancellationToken = default)
  {
-     logger.LogInformation("Scheduler started");
+     logger.LogInformation("Scheduler {SchedulerName} started", scheduler.SchedulerName);
      return default;
  }
```

The argument is the `IScheduler` itself, so a listener can act on it (pause the trigger, read `Status`);
its identity is `SchedulerName` and `SchedulerInstanceId`. It is the instance
`ISchedulerFactory.GetScheduler()` returns and `IJobExecutionContext.Scheduler` carries, so those compare
equal by reference. An `IScheduler` injected from the container is a lazily resolving proxy and does not;
compare `SchedulerName` instead.

#### `SchedulerError` says what the error was about

`SchedulerError`'s message and exception now travel in a `SchedulerErrorContext`, with the trigger, job
and firing the error concerns:

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

`Message` and `Exception` are the old parameters, so a listener that only logged changes little:

```diff
- public ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default)
+ public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
  {
-     logger.LogError(exception, "{Message}", message);
+     logger.LogError(errorContext.Exception, "{Message}", errorContext.Message);
      return default;
  }
```

* The three keys are null when the scheduler could not say, as for a trigger scan that reached no trigger,
  a job store retrying a connection, or a schedule file naming many jobs. Null means "unknown", not "no
  trigger".
* Every failure inside a firing fills all three, as does a misfire notification a listener broke
  (discussion #3211).
* `ISchedulerSignaler.NotifySchedulerListenersError` takes the record too, so a job store of your own can
  report the trigger it failed for:

```diff
- ValueTask NotifySchedulerListenersError(string message, SchedulerException exception, CancellationToken cancellationToken = default);
+ ValueTask NotifySchedulerListenersError(SchedulerErrorContext errorContext, CancellationToken cancellationToken = default);
```

#### The compiler will not point at the callbacks you have to change, but the registration will

Every listener member has a default implementation, so a listener with an old signature still compiles:
its method implements nothing, and the default runs instead. Quartz checks each listener as it is
registered and refuses one with a public method named like a notification but with a different signature,
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

Where it throws:

* `q.AddJobListener<MyListener>()` and the instance overload: from the `AddQuartz` call itself.
* A factory overload declared as the interface, a listener registered as a plain service, and
  `scheduler.ListenerManager.AddJobListener(…)`: when the listener is attached to a scheduler, which for a
  hosted application is host start.

A method that reuses a notification's name for something unrelated is refused too; rename it. An explicit
interface implementation is not examined, as it is not a public method of the class. The compiler also
hints: a callback that overrides nothing and uses no instance state trips CA1822 ("can be marked as
static"), and an `<inheritdoc />` on it trips MA0196.

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

`IJobListener`'s members and `ITriggerListener`'s other three are unchanged: their
`IJobExecutionContext` already carries the scheduler.

#### `TriggerMisfired` takes the trigger first

`ITriggerListener`'s members lead with the trigger, so `TriggerMisfired`'s new scheduler parameter comes
second, where `TriggerFired`, `VetoJobExecution` and `TriggerComplete` take the `IJobExecutionContext`.

```diff
- public ValueTask TriggerMisfired(ITrigger trigger, CancellationToken cancellationToken = default)
+ public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
  {
      logger.LogWarning("{SchedulerName} missed {TriggerKey}", scheduler.SchedulerName, trigger.Key);
      return default;
  }
```

With the two swapped, the listener compiles but silently stops implementing the interface. Quartz refuses
it at registration and says the parameters are in the wrong order. 4.0 pre-releases briefly put the
scheduler first; see [Appendix: if you ran a 4.0 pre-release](#appendix-if-you-ran-a-4-0-pre-release).

### The three `*Support` base classes are gone

`JobListenerSupport`, `TriggerListenerSupport` and `SchedulerListenerSupport` are removed. Every member of
`IJobListener`, `ITriggerListener` and `ISchedulerListener` is a default interface member: notifications do
nothing, and `Name` returns `GetType().Name`. Implement the interface and drop `override`:

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

* Drop `Name` unless several instances of one type are registered with the same scheduler, where a later
  registration would replace an earlier one of the same name.
* A listener derived from a base class fails to compile. One that implemented the interface directly
  compiles, but its `Task`-returning members implement nothing; Quartz refuses it at registration, naming
  the member. See
  [The compiler will not point at the callbacks you have to change, but the registration will](#the-compiler-will-not-point-at-the-callbacks-you-have-to-change-but-the-registration-will).
* **A default interface member is not a class member.** Reading `Name` from the concrete type no longer
  compiles unless the listener declares it. Read it through the interface:

```diff
- var listener = new MyListener();
- scheduler.ListenerManager.RemoveSchedulerListener(listener.Name);
+ ISchedulerListener listener = new MyListener();
+ scheduler.ListenerManager.RemoveSchedulerListener(listener.Name);
```

### Scheduler listeners are identified by name

`ISchedulerListener.Name` defaults to `GetType().Name`. Registering a scheduler listener with a `Name`
already registered replaces the first, as for job and trigger listeners. Override `Name` to register
several instances of one type with the same scheduler:

```diff
- scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener);
+ scheduler.ListenerManager.RemoveSchedulerListener(mySchedulerListener.Name);
```

A test double does not run default interface members, so give a faked `ISchedulerListener` a `Name`
before `AddSchedulerListener`:

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

Null means every group, as it already did for `TriggersPaused` and `TriggersResumed`. The scheduler's own
pause-all path still raises the job events once per group. Add the `?` to your implementation, or get a
nullability warning (an error under `TreatWarningsAsErrors`).

`JobScheduled(ITrigger)` and `JobUnscheduled(TriggerKey)` stay asymmetric: an unscheduled trigger no
longer exists to pass.

### Two members were renamed

```diff
- ValueTask SchedulerShuttingdown(CancellationToken cancellationToken = default);
+ ValueTask SchedulerShuttingDown(CancellationToken cancellationToken = default);

- ValueTask SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default);
+ ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default);
```

`SchedulerError`'s two parameters also became one record; see
[Listeners are told which scheduler is calling](#listeners-are-told-which-scheduler-is-calling).

### `JobInterrupted` says which firing was interrupted

`ISchedulerListener` gained an overload with the fire instance id, as a default interface member. A job
without `[DisallowConcurrentExecution]` can have several firings in flight under one key; the id says
which was cancelled, and matches that firing's `JobToBeExecuted`/`JobWasExecuted`.

```csharp
ValueTask JobInterrupted(IScheduler scheduler, JobKey jobKey, string fireInstanceId, CancellationToken cancellationToken = default);
```

* `InterruptFireInstance` raises it once.
* **`Interrupt(jobKey)` raises it once per cancelled firing**, where it used to raise one notification in
  total.
* The default body calls the key-only overload, so a listener implementing that one still works and now
  hears once per firing. Implement one overload, not both.

### Instantiation failures name the trigger

When `IJobFactory` cannot create a job, usually because the container cannot resolve a constructor
dependency, no `IJobExecutionContext` exists yet, so `SchedulerError` is the only notification. It carried
the job key only in its message, and not the trigger. It now receives a `JobInstantiationException`, and
the `SchedulerErrorContext` names the firing:

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

* This is additive, and mirrors `JobExecutionProcessException` for execution failures.
* Both factory paths raise it: the container's, where `ActivatorUtilities` throws, and
  `SimpleJobFactory`'s, with the original failure as `InnerException`.
* In the two messages reporting this, the closing quote moved to after the type name:
  `Problem instantiating type 'MyNamespace.MyJob': message`. Read the exception instead of the text.

To prevent the failure, see
[failing fast when job dependencies cannot be resolved](packages/microsoft-di-integration.md#failing-fast-when-job-dependencies-cannot-be-resolved).

### Triggers entering the error state are reported

A trigger moved to `TriggerState.Error` was only logged, and two ADO store transitions (a job type that
will not load during acquisition, a job that cannot be read back in `TriggersFired`) did not reach the
scheduler at all. Two notifications report it now:

```csharp
ValueTask TriggerInError(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default) => default;
ValueTask TriggersInError(IScheduler scheduler, JobKey jobKey, CancellationToken cancellationToken = default) => default;
```

* Both default to doing nothing, as do `ISchedulerSignaler.NotifySchedulerListenersTriggerInError` and
  `NotifySchedulerListenersTriggersInError`, so existing listeners and signalers are unaffected.
* The plural is keyed by `JobKey`, because `SetAllJobTriggersError` is one bulk statement. Call
  `GetTriggersOfJob` for the trigger keys.
* Neither carries a cause: `SchedulerError` says *why*, these say *what changed*, and where both apply they
  arrive together.
* Recover a trigger with `IScheduler.ResetTriggerFromErrorState`.

`JobChainingJobListener` is `sealed`; see
[The shipped implementations are sealed](#the-shipped-implementations-are-sealed).

An `IJobStore` that implements `IJobListener` no longer receives all events automatically. Register it with
`ListenerManager`:

```csharp
scheduler.ListenerManager.AddJobListener(myJobStoreListener);
```

## Scheduler Configuration Validation

* An `IdleWaitTime` less than or equal to zero now throws, instead of silently becoming a 30-second
  default.
* A negative `IdleWaitTime` or `BatchTimeWindow` is rejected.
* A `MaxBatchSize` less than or equal to zero is rejected.
* `MaxBatchSize` may not exceed `ThreadPoolOptions.MaxConcurrency`. Triggers acquired beyond the threads
  available are held by this node, unfireable by any other, until the pool drains. See
  [Batching trigger acquisition](tutorial/advanced-enterprise-features.md#batching-trigger-acquisition).

### A wait no timer will take is refused where it was configured

`Task.Delay` accepts at most `uint.MaxValue - 1` milliseconds, a little under 50 days. A longer configured
wait used to start fine and fail later with an `ArgumentOutOfRangeException` naming a parameter called
`delay`; `MisfireHandlerFrequency`'s came out of the ADO store's `Shutdown`
([#3577](https://github.com/quartznet/quartznet/issues/3577)). These six settings are now refused at
startup, with a message naming the option, the ceiling and the value:

| Setting | Which wait it becomes |
|---|---|
| `AdoJobStoreOptions.MisfireHandlerFrequency` | the pause between misfire passes |
| `AdoJobStoreOptions.MisfireThreshold` | the same, when no frequency is set |
| `AdoJobStoreOptions.DbRetryInterval` | the pause before retrying a failed connection |
| `AdoJobStoreOptions.TransientRetryInterval` | the pause before retrying a transient failure |
| `ClusteringOptions.CheckinInterval` | the pause between cluster check-ins |
| `QuartzHostedServiceOptions.StartDelay` | what the hosted service waits before starting the scheduler |

Two more are checked where they are set, not through `ValidateOnStart`:

* a lock handler's `RetryPeriod` has no options type, so its setter refuses it;
* `IScheduler.StartDelayed` checks its argument. An over-long delay used to fault an unobserved task and
  leave a scheduler that never started.

`IdleWaitTime` has no upper bound: it waits on a `SemaphoreSlim`, which takes a timeout of any length.

A value refused here used to start, so this is a startup failure for a configuration that ran before.

### Validation happens at startup, through one mechanism

Every Quartz options type is registered with `ValidateOnStart`, so a bad value fails `Host.Build()` with
every failure listed. Before, validation ran only when something first resolved the options, inside a
scheduler factory.

Only what the scheduler reads is validated:

| Options | Validated when |
|---|---|
| `QuartzSchedulerOptions`, `ThreadPoolOptions` | always |
| `InMemoryJobStoreOptions` | `UseInMemoryStore` was called |
| `AdoJobStoreOptions`, `ClusteringOptions` | `UsePersistentStore` was called |
| `DataSourceOptions` | a data source was configured |

The two satellite packages now use `IValidateOptions<T>` too, so `OptionsValidationException` reports
every configuration mistake:

| Before | After |
|---|---|
| `AddOptions<QuartzHttpApiOptions>().Validate(lambda, message)` | `QuartzHttpApiOptionsValidator`, with `ValidateOnStart()` |
| `HttpClientOptions.AssertValid()` throwing `InvalidOperationException` | `HttpClientOptionsValidator`, throwing `OptionsValidationException` from `AddQuartzHttpClient` |

`ValidateOnStart` does nothing in the container `QuartzSchedulerBuilder` builds, since nothing there
resolves `IStartupValidator`. `Build()` passes `ValidateOnBuild`, which checks the object graph, not
values, so there a bad value surfaces when the component reading it is built.

### `UseClustering(c => c.Enabled = false)` is now an error

It used to leave the store with database locking on, no cluster manager and no check-in row, silently. It
now fails validation. A scheduler that should not cluster does not call `UseClustering`.

### `IgnoreDuplicates` on its own turns overwriting off

`Scheduling.IgnoreDuplicates` and `Scheduling.OverwriteExistingData` both decide what happens to a
declared job or trigger whose key is already stored, and `OverwriteExistingData` wins. It defaults to
`true`, so on 3.x setting only `IgnoreDuplicates` did nothing: duplicates were replaced.

**Setting `IgnoreDuplicates` now turns overwriting off.** The option records whether its setter ran;
configuration binding and `Configure<QuartzOptions>` both go through it:

```csharp
services.Configure<QuartzOptions>(options => options.Scheduling.IgnoreDuplicates = true);
```

Setting *both* is refused at startup, naming both settings.

| What you write | What happens to a duplicate key |
|---|---|
| neither | replaced |
| `IgnoreDuplicates = true` | passed over |
| `OverwriteExistingData = false` | reported as an error |

In a scheduling file, processing directives with `ignore-duplicates` and no `overwrite-existing-data` turn
overwriting off too, in both the XML and the JSON plugin.

::: warning beta.1 refused the one-liner
On beta.1, `options.Scheduling.IgnoreDuplicates = true` alone failed startup with
`OptionsValidationException`. A workaround of adding `OverwriteExistingData = false` keeps working with the
same meaning. If you added `OverwriteExistingData = true`, remove it: that pair is now refused.
:::

## Cron Parser Enhancements

New syntax:

* `L` and `LW` combinations in day-of-month (e.g. `LW` for the last weekday of the month).
* `LW-<OFFSET>`, an offset from the last weekday (e.g. `LW-2`, two days before it). A result that crosses a
  month boundary resets to the 1st.
* Day-of-month and day-of-week together. A field written `*` or `?` **restricts nothing**, so the other
  field decides: `0 15 10 1 * *` is the 1st of the month, `0 15 10 * * MON` every Monday. When **both**
  fields name days, the expression fires on their union: `0 15 10 1,2,3 * MON,FRI` is the 1st, 2nd and 3rd
  **and** every Monday and Friday. This is the Unix `crontab(5)` rule; Cronos ANDs the fields instead. `?`
  and `*` are full synonyms in a day field, so `? * ?` means every day.
* `H` (hash) tokens for [load distribution](cron-expressions.md#h-hash-for-load-distribution) across
  triggers.

Parse errors name the fix:

* A five-field Unix/crontab expression is still an error when read as Quartz, which puts seconds first.
  The message shows the corrected six-field expression and names `CronFormat.Unix` for reading it as
  written.
* The day-of-week range error explains that Quartz numbers days 1-7 from Sunday where Unix uses 0-6, and
  recommends names (`SUN`, `MON`, …).
* For a bare-number day-of-week, the message also renumbers it. In `30 4 * * 1` the `1` is Monday in
  crontab but Sunday in Quartz, so the message names `"0 30 4 ? * MON"`.
* A day name in the **month** field gets the same advice: in `0 12 * * MON`, Quartz's fifth field is the
  month, so `MON` fails as a month name before the field count is checked.

### A cron expression can be written in the Unix five-field form

`CronFormat.Quartz` is the default. `CronFormat.Unix` reads the five-field crontab form, through three
entry points:

```csharp
CronExpression.Parse("30 4 * * 1", CronFormat.Unix);
CronExpression.TryParse(candidate, CronFormat.Unix, out CronExpression? expression);
CronScheduleBuilder.Create("30 4 * * 1", CronFormat.Unix);
```

`WithCronSchedule` has no format overload; use `WithCronSchedule(CronExpression.Parse(s, CronFormat.Unix))`.
Add a time zone with `CronExpression.Parse(s, CronFormat.Unix).WithTimeZone(tz)`.

The format changes two things:

* **Field layout**: five fields, minutes first, no seconds and no year.
* **Day-of-week numbering**: 0-7, with 0 and 7 both Sunday, so `1-5` is Monday to Friday.

The grammar is otherwise the same. `L`, `W` and `#` work, `L` alone in day-of-week is still Saturday, and
two restricted day fields fire on their union.

**`H` works with the format.** `CronScheduleBuilder.Create(s, CronFormat.Unix)` rewrites the expression to
the Quartz form, then hashes on the trigger's identity: `Create("H 4 * * 1", CronFormat.Unix)` on a trigger
identified as `nightly` becomes `0 13 4 ? * MON`.

* Without a trigger, `CronExpression.ParseWithHash(s, CronFormat, hashKey)`,
  `ParseWithHash(s, CronFormat, hashSeed)` and `TryParseWithHash(s, CronFormat, hashKey, out …)` do the
  same.
* An `H` in a five-field day-of-week is hashed over Quartz's 1-7, since the rewrite runs first.
* The format-aware `Parse` and `TryParse` take no hash key, so `H` cannot be used with them. `ResolveHash`
  takes a hash key but no format; it returns a Quartz-form string.

**The expression is stored in the canonical Quartz form; the original is not kept.**
`CronExpression.Parse("30 4 * * 1", CronFormat.Unix).CronExpressionString` is `"0 30 4 ? * MON"`, and that
is what `QRTZ_CRON_TRIGGERS.CRON_EXPRESSION`, the dashboard and the HTTP API show. There is no
`CRON_FORMAT` column, and there will not be one.

| crontab | stored, and shown, as |
|---|---|
| `30 4 * * 1` | `0 30 4 ? * MON` |
| `0 12 1 * *` | `0 0 12 1 * ?` |
| `* * * * *` | `0 * * * * ?` |
| `0 0 13 * 5` | `0 0 0 13 * FRI`: the 13th **and** every Friday, by the union rule |
| `0 0 * * 0-6` | `0 0 0 * * ?` |
| `0 0 * * 5-1` | `0 0 0 ? * FRI-MON` |
| `0 0 * * 1/2` | `0 0 0 ? * 2/2`: numeric, although [`MON/2` parses from 4.1](#mon-2-is-a-fortnight-on-3-x-and-a-step-from-4-1) with the same meaning |

**The XML schema, the HTTP API and the dashboard take no format**: a five-field expression is written in
C#, not stored. The `@` macros below work everywhere.

### `@daily` and the rest of the macros work everywhere

The `CronExpression` constructor expands the `@` macros, so they work wherever an expression string is
read, including `<cron-expression>@daily</cron-expression>` in an XML scheduling file, the dashboard's
expression box and the HTTP API.

| macro | expands to |
|---|---|
| `@yearly`, `@annually` | `0 0 0 1 1 ?` |
| `@monthly` | `0 0 0 1 * ?` |
| `@weekly` | `0 0 0 ? * SUN` |
| `@daily`, `@midnight` | `0 0 0 * * ?` |
| `@hourly` | `0 0 * * * ?` |

* Only Vixie's set: no `@every_minute`, `@every_second` or jittered variant, since `0 * * * * ?` is already
  short and `H` spreads load deterministically.
* `@reboot` is rejected by name. Any other `@name` is rejected with the supported list.
* The expansion is what is stored, so a trigger written with `@daily` shows `0 0 0 * * ?`.

### A bad argument, rather than a bad expression

* `CronExpression.TryParse(s, format, out)` returns `false` for an unknown `CronFormat` instead of throwing
  `ArgumentOutOfRangeException`. `Parse(s, format)` still throws it.
* `new CronExpression(null)` and `CronScheduleBuilder.Create(null)` throw `ArgumentNullException`, as
  `CronExpression.Parse` always has; 3.x threw `ArgumentException("cronExpression cannot be null")`. It is
  an `ArgumentException`, so a `catch` still catches it; the message is the framework's.

### The parser refuses what it used to ignore

These parsed on 3.x but meant something other than what they said. Each is now a `FormatException` whose
message names the expression that says what was meant.

| Expression | 3.x | 4.0 |
|---|---|---|
| `1-5W` | the `W` was dropped; meant `1-5` | `FormatException`: write `1W,2W,3W,4W,5W` or drop the `W` |
| `? * L-3`, `? * LW` | the suffix was dropped; meant Saturday | `FormatException`: those forms belong to day-of-month. A bare `L` in day-of-week is still Saturday |
| `MON,FRI#3` | fired on the third **Monday**; the Friday was ignored | `FormatException`: `#` cannot appear beside other days |
| `5C`, `1C` | meant `5` / `1`; `C` was never implemented | `FormatException`: use `ModifiedByCalendar` |
| `*/0`, `5/0`, `0-10/0` | a step of 0 became no step | `FormatException` |
| `0-10/120` | the step was never range-checked | `FormatException`: `Increment > 59 : 120`, as `0/120` already did |
| `MON/2` | every second week, with **no stable phase** | `FormatException` on 4.0; **parses on 4.1, as the step `2/2`**; see below |

### `MON/2` is a fortnight on 3.x and a step from 4.1

On 3.x, a textual day-of-week followed by `/` meant "every N weeks", counted from wherever the search
started, so a misfire, restart, failover or dashboard query could move the phase. The numeric `2/2` was an
ordinary step.

4.0 rejects `MON/2`. **4.1 reads it as the step `2/2`**: Monday, Wednesday and Friday. A fortnightly job
carried from 3.x to 4.1 fires three times a week (156 fires a year instead of 26), and nothing is logged,
because the expression is valid. The upgrade cannot report this;
[the audit below](#before-you-upgrade) finds it.

For every second Monday, use `RecurrenceScheduleBuilder.Create("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")`, which
anchors on the trigger's start time.

### Before you upgrade

3.x stored every expression in the rejection table above. The exception comes from
`CronTriggerImpl.CronExpressionString`'s setter, which the ADO.NET job store calls while loading a row, so
such an expression fails the *read*. Audit stored expressions first:

```sql
-- run against every scheduler's tables before upgrading; substitute your table prefix
SELECT SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, CRON_EXPRESSION
FROM   QRTZ_CRON_TRIGGERS
WHERE  CRON_EXPRESSION LIKE '%C%'    -- 'C' (calendar), never implemented
   OR  CRON_EXPRESSION LIKE '%/0%'   -- step of zero
   OR  CRON_EXPRESSION LIKE '%#%,%'  -- '#' beside other days
   OR  CRON_EXPRESSION LIKE '%,%#%'
   OR  CRON_EXPRESSION LIKE '%/%'    -- then eyeball for a textual day-of-week step: 'MON/2'
   OR  CRON_EXPRESSION LIKE '%W%';   -- then eyeball for 'W' after a range
```

* Read the matches by eye: most `/` is an ordinary step, most `W` a valid `15W` or `LW`, and `%C%` also
  matches `DEC` and `OCT` (expressions are stored upper-cased).
* `CronExpression.TryParse` against the 4.0 assembly settles the candidates, **except a textual
  day-of-week step** such as `MON/2` or `MON-FRI/2`: 4.1 parses it with a different meaning, so check it by
  eye.
* The query misses `CronCalendar` expressions, inside the serialized blob in `QRTZ_CALENDARS` and
  surfacing only at deserialization, and expressions in code or configuration files.

### If your expressions came from another cron library

3.x required `?` in exactly one day field. 4.0 reads `*` and `?` there as synonyms that restrict nothing,
and fires on the union when both day fields name days, as crontab does and `cron-expressions.md` has
always described. An expression written for a six-field crontab-derived .NET dialect, commonly Cronos,
failed to store on 3.x; on 4.0 it stores and fires, and two shapes fire on a different day:

| Expression | Quartz 4.0 | Cronos | |
|---|---|---|---|
| `0 0 2 * * MON` | 2026-08-24 02:00 | 2026-08-24 02:00 | same |
| `0 0 7 * * *` | 2026-08-21 07:00 | 2026-08-21 07:00 | same |
| `0 30 6 * * MON,TUE,WED,THU,FRI` | 2026-08-21 06:30 | 2026-08-21 06:30 | same |
| `0 0 2 * * 1` | 2026-08-23 (**Sunday**) | 2026-08-24 (**Monday**) | **differ** |
| `0 0 2 5 * MON` | 2026-08-24 (the union) | 2026-10-05 (the intersection) | **differ** |

Searching from `2026-08-21T00:00:00Z`:

* **Day numbers.** Quartz numbers days of the week 1-7 from Sunday; those dialects use 0-6. Every numeric
  form differs, `1`, `1-5`, `*/2`, `#n` and `L` alike: `6#3` is the third Saturday in Quartz and the third
  Friday there. Write days as names, such as `MON`.
* **Both day fields.** Quartz fires on the union; Cronos on the intersection. Put `?` in one day field.

Five-field expressions are not affected, since `CronFormat.Unix` renumbers them; see
[The Unix five-field form](cron-expressions.md#the-unix-five-field-form) and
[the same trap for a Spring expression](cron-expressions.md#an-expression-copied-from-spring).

Nothing can warn on a valid expression, so audit with a second query, in C# since splitting a field is
spelled differently in each database:

<!-- snippet: sample_cron_dialect_audit -->
```csharp
// Both shapes below are valid Quartz expressions, so nothing rejects them and nothing logs.
// What this lists is the expressions worth reading again if they were carried over from a
// crontab-derived library such as Cronos or NCrontab.
await using DbCommand command = connection.CreateCommand();

// A schema shared by several schedulers holds all of their rows in these tables, told apart
// by SCHED_NAME — the scheduler's InstanceName — alone. The audit therefore filters on it and
// lists it, because a line naming only the trigger's group and name would not say whose
// trigger it is. Run it for every scheduler before upgrading, and substitute your own table
// prefix for QRTZ_.
command.CommandText =
    """
    SELECT SCHED_NAME, TRIGGER_NAME, TRIGGER_GROUP, CRON_EXPRESSION
    FROM   QRTZ_CRON_TRIGGERS
    WHERE  SCHED_NAME = @schedulerName
    """;

// The name is bound, never written into the text. '@' is the marker this sample spells; a
// provider that spells it otherwise — ':' on Oracle — needs that one character changed, which
// is what DbMetadata.ParameterNamePrefix carries for Quartz's own statements.
DbParameter schedulerNameParameter = command.CreateParameter();
schedulerNameParameter.ParameterName = "@schedulerName";
schedulerNameParameter.Value = schedulerName;
command.Parameters.Add(schedulerNameParameter);

await using DbDataReader reader = await command.ExecuteReaderAsync();

while (await reader.ReadAsync())
{
    string scheduler = reader.GetString(0);
    string name = reader.GetString(1);
    string group = reader.GetString(2);
    string expression = reader.GetString(3);

    // A stored expression is always the canonical six-field Quartz form: seconds, minutes,
    // hours, day-of-month, month, day-of-week, and optionally a year.
    string[] fields = expression.Split((char[]?) null, StringSplitOptions.RemoveEmptyEntries);
    if (fields.Length < 6)
    {
        continue;
    }

    string dayOfMonth = fields[3];
    string dayOfWeek = fields[5];

    // Quartz numbers the days 1-7 from Sunday; the other dialects number them 0-6 (Unix: 0-7),
    // also from Sunday. So a day written as a number names a different day in each, in every
    // form that carries one: '1', '1-5', '*/2', and the '6#3' and '6L' forms with it.
    if (IsNumericDay(dayOfWeek))
    {
        Console.WriteLine($"{scheduler}: {group}.{name}: numeric day-of-week '{dayOfWeek}' — write it as a name");
    }

    // Quartz fires on the union of the two day fields, as crontab does; Cronos intersects them.
    if (!IsUnrestricted(dayOfMonth) && !IsUnrestricted(dayOfWeek))
    {
        Console.WriteLine($"{scheduler}: {group}.{name}: both day fields restricted — this fires on their union");
    }
}

// A day named by number rather than by name, whatever decorates it. A letter anywhere says the
// days were written as names and there is nothing to renumber — except 'L', which is a
// position rather than a name and shifts with the digit in front of it.
static bool IsNumericDay(string field)
    => field.Any(char.IsDigit) && !field.Any(c => char.IsLetter(c) && c != 'L');

// '*' and '?' both mean "this field restricts nothing"; either one leaves the other in charge.
static bool IsUnrestricted(string field) => field is "*" or "?";
```
<!-- endSnippet -->

## Daylight saving time

Three schedules fire at different times than on 3.x. None needs a code change; review any schedule that
crosses a transition.

**Interval cron expressions fire through both halves of a fall-back hour.** An expression with a wildcard,
step or range in its second, minute or hour field, such as `0 * * * * ?` or `0 0/30 * * * ?`, now fires
through **both** occurrences of the repeated hour. 3.x fired it once, skipping an hour of real time.
Fixed-time expressions such as `0 30 2 * * ?`, including lists like `0 0,30 2 * * ?`, still fire once per
day, at the first occurrence.

**A cron time that does not exist fires when the gap ends, not shifted by the transition delta.** A daily
`0 30 2 * * ?` over a 02:00–03:00 spring-forward gap fires at **03:00**; 3.x fired at 03:30. Where the delta
is not a whole hour (Australia/Lord_Howe), it fires at 02:30 rather than 02:45.

* All skipped times map to that one instant, so `0 0,30 2 * * ?` still fires once.
* An hourly `0 30 * * * ?` now fires at 03:00, for the skipped occurrence, and at 03:30 for the next hour.
  3.x shifted the first onto 03:30, merging the two into one fire.
* `IsSatisfiedBy` now agrees with the fire time, so a trigger no longer fires at an instant its own
  expression rejects. A `CronCalendar` written over the skipped hour now excludes that instant; it used to
  exclude nothing on the transition day.
* Fall-back behaviour is unchanged.

**`CalendarIntervalTrigger` with `PreserveHourOfDayAcrossDaylightSavings` steps in local wall-clock
time.** Fire times are exactly on schedule and strictly increasing; the old implementation could return
times the schedule never specified. Where the delta is not a whole hour (Australia/Lord_Howe), the local
time no longer drifts by the sub-hour part, so the full time of day is kept, as documented.

## New Features

What 4.x adds that a 3.x application is probably not using yet. A few were backported to a late 3.x
release.

* **[RecurrenceTrigger (RRULE)](tutorial/recurrencetrigger.md)**: RFC 5545 recurrence rules, for patterns
  like "every 2nd Monday of the month" or "last weekday of March each year".
* **H (hash) token in cron expressions**: deterministic load distribution, seeded by the trigger identity.
* **HTTP API**: an optional REST API to manage a scheduler remotely; see [HTTP API](packages/http-api.md).
* **`Quartz.HttpClient`**: `HttpScheduler`, an `IScheduler` that talks to a remote scheduler over that API,
  replacing .NET Remoting; see
  [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern).
* **Paged, projected job store queries**: list and count jobs, triggers, groups and calendars a page at a
  time; see [Job store listings became queries](#job-store-listings-became-queries).
* **Bulk fetch by key**: `GetJobDetails(keys)` and `GetTriggers(keys)` fetch a page of keys in one round
  trip, over ADO.NET and HTTP; same section.
* **Fire instances are a listing**: `QueryFireInstances` lists what is running, cluster-wide with a
  persistent store; see
  [What is running is a listing, not a list of contexts](#what-is-running-is-a-listing-not-a-list-of-contexts).
* **Cluster nodes can be listed**: `QueryClusterNodes`, with the `Alive`/`Overdue`/`Failed` verdict
  failover uses; see [The nodes of a cluster are a listing](#the-nodes-of-a-cluster-are-a-listing).
* **Job data by property name**: bind job data to a job property instead of spelling its key; see
  [Job data can name the property](#job-data-can-name-the-property).
* **Retry policy on a trigger**: `WithRetryPolicy(RetryPolicy.Exponential(3, TimeSpan.FromSeconds(30)))`
  re-fires a trigger whose job threw, on a fixed, exponential or explicit schedule, persisted across a
  restart or failover; see [A trigger can carry a retry policy](#a-trigger-can-carry-a-retry-policy) and
  [Retrying Failed Jobs](how-tos/retrying-failed-jobs.md).
* **`TriggerState.Executing`**: whether a trigger's job is running, across the cluster; see
  [Executing is a trigger state](#executing-is-a-trigger-state).
* **`JobInstantiationException`**: a job that could not be built names the trigger, the job and the fire
  instance; see [Instantiation failures name the trigger](#instantiation-failures-name-the-trigger).
* **`ISchedulerListener.TriggerInError` / `TriggersInError`**: notified when a trigger moves to
  `TriggerState.Error`; see
  [Triggers entering the error state are reported](#triggers-entering-the-error-state-are-reported).
* **Every listener callback names its scheduler**, and `SchedulerError` carries the trigger, job and
  firing. A listener with a 3.x or 4.0 pre-release signature is refused at registration; see
  [Listeners are told which scheduler is calling](#listeners-are-told-which-scheduler-is-calling).
* **Joining a transaction the application owns**: the ADO job store can join a transaction you started, so
  your data and the job that acts on it commit together. Turn it on with
  `store.ConfigureStore(o => o.AcceptEnlistedTransactions = true)`, `JobStore:AcceptEnlistedTransactions`
  or `quartz.jobStore.acceptEnlistedTransactions`, then hand the store a connection for a scope with
  `IScheduler.EnlistTransaction` / `EnlistConnection`. That is the only way in: a connection the store
  opens itself stays out of any ambient `TransactionScope`, which would otherwise need promoting to a
  distributed transaction. See [Joining an existing transaction](tutorial/job-stores.md#joining-an-existing-transaction).
* **Job execution middleware**: `IJobExecutionMiddleware` wraps every firing (a log scope, a tenant
  context, a metric, exception translation), which a listener cannot. Register it with
  `AddJobMiddleware<T>()` or its factory and instance overloads; see
  [Cross-cutting concerns run as middleware](#cross-cutting-concerns-run-as-middleware).
* **Builder methods for the classic history plugins**: `UseJobHistoryLogging()` and
  `UseTriggerHistoryLogging()`; before, only `quartz.plugin.*` property keys reached them.
* **Smaller plugins**: `ISchedulerPlugin.Start` and `Shutdown` have default implementations; see
  [A plugin implements only what it has to say](#a-plugin-implements-only-what-it-has-to-say).
* **A calendar can have dependencies**: `AddCalendar(name, serviceProvider => …)` builds it from the
  container, which the `where T : ICalendar, new()` overloads cannot; see
  [A calendar can be built from the container](#a-calendar-can-be-built-from-the-container).
* **A renamed job type is declared as a map**:
  `q.UseTypeLoader(loader => loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob)))`,
  or `Quartz:TypeLoader:Aliases` in configuration, keeps rows with the old `JOB_CLASS_NAME` firing. 3.x
  needed an `ITypeLoadHelper` of your own. See
  [A job type rename is declared as a map](#a-job-type-rename-is-declared-as-a-map).
* **A `quartz.*` key without its prefix is refused**: `QuartzOptions.Properties` reads only through the
  `quartz.` prefix, so such a key was silently ignored; it now fails options validation at startup. See
  [A property key without the `quartz.` prefix is refused](#a-property-key-without-the-quartz-prefix-is-refused).
* **An `IJobDetail` of your own**, which `RAMJobStore` hands back instead of swapping in Quartz's; see
  [An `IJobDetail` of your own](#an-ijobdetail-of-your-own).
* **Shortcuts**: `QueryTriggersInError()`; `ResetTriggersFromErrorState(GroupMatcher<TriggerKey>)` for a
  group's failed triggers; `Exists(string calendarName)` without deserializing the calendar;
  `ISchedulerFactory.GetRequiredScheduler(name)`, which throws where `LookupScheduler` returns null. See
  [Reading has two altitudes on purpose](#reading-has-two-altitudes-on-purpose).
* **Key sets in one call**: `PauseTriggers`, `ResumeTriggers`, `PauseJobs`, `ResumeJobs` and
  `ResetTriggersFromErrorState` take a key collection, apply it in one lock and one transaction, and return
  the keys they applied to; see
  [A set of keys pauses, resumes or resets in one call](#a-set-of-keys-pauses-resumes-or-resets-in-one-call).
* **`TriggerDetailsUpdate.WithExecutionGroup`**: move a stored trigger into an execution group, or out of
  every group, without rescheduling. The ADO store writes `QRTZ_TRIGGERS.EXECUTION_GROUP` through the
  generic trigger update, and `RAMJobStore` applies it in place, as it does a preferred node.
* **A chain can fan out**: `JobChainingJobListener` takes several follow-ups for one job, by calling
  `AddJobChainLink` again with the same first job or with the new
  `AddJobChainLinks(firstJob, followUpJobs)`. Each follow-up is its own firing, so they run concurrently,
  and one that cannot be triggered is logged without affecting the rest. On 3.x a second link threw. See
  [How do I chain Job execution?](../faq.md#how-do-i-chain-job-execution-or-how-do-i-create-a-workflow).

## Job data can name the property

`UsingJobData` has an overload that takes the job property instead of its key:

```diff
  q.AddJob<ExampleJob>(jobKey, j => j
-     .UsingJobData(nameof(ExampleJob.InjectedString), "Hello")
-     .UsingJobData(nameof(ExampleJob.InjectedBool), true)
+     .UsingJobData(x => x.InjectedString, "Hello")
+     .UsingJobData(x => x.InjectedBool, true)
  );
```

The key is the property's name, and the value must have the property's type, so a mistyped value (an
`int` for a `string` property) or another job's property does not compile.

What the compiler cannot rule out is rejected when the job data is written, by checking that the key
leads back to the same property through the job factory's lookup. Rejected:

* a property with no public setter, or a nested path;
* a property reached by casting the lambda parameter to another job;
* a property the factory cannot find: a name starting with a lowercase letter (keys are looked up
  upper-cased), or an explicit interface implementation;
* a name that resolves to a *different* property of another type, as a `new` member hiding a base property
  does;
* a value that will not convert to the property's type, or would lose information: a `double` rounded into
  an `int`, or saturated into a `float`;
* `null` for a property that cannot hold one. `int? retries = …; UsingJobData(j => j.RetryCount, retries)`
  compiles, since inference widens `TValue` to the nullable form, and is rejected here instead of becoming
  `0` when the job runs.

Enums are stored by name. The expression is read, never run. String-keyed `UsingJobData` calls are
unaffected.

### The builders carry the job type

The builders and configurator interfaces are generic in the job type. `JobBuilder` and `TriggerBuilder`
are static classes holding the `Create` methods, and the builders are `JobBuilder<TJob>` /
`TriggerBuilder<TJob>`:

| 4.0 preview | 4.0 |
|---|---|
| `JobBuilder` | `JobBuilder<TJob>`, from `JobBuilder.Create<TJob>()`; `JobBuilder.Create()` gives `JobBuilder<IJob>` |
| `TriggerBuilder` | `TriggerBuilder<TJob>`, from `TriggerBuilder.Create<TJob>()`; `TriggerBuilder.Create()` gives `TriggerBuilder<IJob>` |
| `IJobConfigurator` | `IJobConfigurator<TJob>` |
| `ITriggerConfigurator` | `ITriggerConfigurator<TJob>`; the 4.x `ITriggerConfigurator` is a smaller base with only `WithSchedule` (see [One family of `WithXSchedule` extensions](#one-family-of-withxschedule-extensions)) |
| `IJobDetail.GetJobBuilder()` | an extension method returning `JobBuilder<IJob>`; see [An `IJobDetail` of your own](#an-ijobdetail-of-your-own) |
| `ITrigger.GetTriggerBuilder()` | returns `TriggerBuilder<IJob>` |

Chained code and the `AddJob<T>` / `ScheduleJob<T>` lambdas read the same. Naming the builder as a type
breaks:

```diff
- TriggerBuilder builder = trigger.GetTriggerBuilder();
+ var builder = trigger.GetTriggerBuilder();
```

The configurator interfaces are **invariant** in `TJob`, which is both an input
(`Expression<Func<TJob, TValue>>`) and part of the returned interface, so a configuration delegate shared
across job types no longer type-checks. Make the helper generic:

```diff
- Action<ITriggerConfigurator> common = t => t.StartNow().WithSimpleSchedule();
- q.ScheduleJob<JobA>(common);
- q.ScheduleJob<JobB>(common);
+ static void Common<TJob>(ITriggerConfigurator<TJob> t) where TJob : IJob => t.StartNow().WithSimpleSchedule();
+ q.ScheduleJob<JobA>(Common);
+ q.ScheduleJob<JobB>(Common);
```

* `AddTrigger<TJob>` is new, since a trigger added on its own has no job type to infer.
* The internal `TriggerConfigurator` is gone; `TriggerBuilder<TJob>` implements `ITriggerConfigurator<TJob>`,
  which brings `WithExecutionGroup` and `WithPreferredNode` to the DI configurator.
* DI-registered triggers get a different clock. `TriggerConfigurator` always used `TimeProvider.System`;
  `AddTrigger` and `ScheduleJob` now use the container's `TimeProvider`. With a non-system one, such as a
  `FakeTimeProvider` in a test, a trigger without an explicit `StartAt` starts at that provider's time,
  which changes when it first fires.

Three runtime checks, only on a builder whose `TJob` is not `IJob`:

* `JobBuilder.Create<TJob>().OfType(type)` and `OfType<T>()` throw `ArgumentException` **at the `OfType`
  call** when the type is not a `TJob`. For a job type resolved at runtime, from configuration or a
  decorator type, use `JobBuilder.Create().OfType(type)`.
* `JobBuilder.Create<TJob>().OfType((JobType) typeName)` throws `InvalidOperationException` on `Build()`
  instead, since a type named by string is known only when it resolves.
* `TriggerBuilder.Create<TJob>().ForJob(jobDetail)` throws `ArgumentException` when the detail is not for a
  `TJob`. `ForJob(JobKey)`, and a detail whose type name does not resolve in this process, cannot be
  checked and are accepted.

## An `IJobDetail` of your own

`IJobDetail.GetJobBuilder()` could not be implemented outside Quartz, since `JobBuilder<TJob>` is sealed
with an internal constructor ([#1143](https://github.com/quartznet/quartznet/issues/1143)). `RAMJobStore`
called it to re-store a `[PersistJobDataAfterExecution]` job's data, so the first completion silently
replaced your detail with Quartz's. It is replaced:

| 4.0 preview | 4.0 |
|---|---|
| `IJobDetail.GetJobBuilder()` | `JobDetailExtensions.GetJobBuilder(this IJobDetail)`, in the `Quartz` namespace |
| — | `IJobDetail.WithJobData(JobDataMap)`: a copy of the detail carrying the given data |

**Calling code does not change.** `detail.GetJobBuilder()` compiles without a new `using` and returns
`JobBuilder<IJob>`, filled from the detail's public state. It keeps the detail's `JobType` as it is, so a
detail whose stored type name does not resolve in this process rebuilds, keeping that spelling, instead of
throwing.

**An implementation writes `WithJobData` instead.** It returns a copy carrying the given map (taken as
given, not copied) and leaves the original alone:

```diff
- public JobBuilder<IJob> GetJobBuilder() => /* nothing you can write */;
+ public IJobDetail WithJobData(JobDataMap jobDataMap) => new MyJobDetail(Key, JobType, …, jobDataMap);
```

* `RAMJobStore` keeps your instances and returns `Clone()`s, so your type round-trips, including the
  re-store of a `[PersistJobDataAfterExecution]` job.
* The ADO.NET job store writes `QRTZ_JOB_DETAILS` columns and rebuilds each detail through `JobBuilder`,
  and `HttpScheduler` rebuilds one from its wire payload, so both return Quartz's type. Put anything that
  must survive a persistent store in the `JobDataMap`.

## A trigger can carry a retry policy

3.x had no retry policy: a failed job asked for `RefireImmediately`, an in-process loop with no delay and
no ceiling, or scheduled a new trigger from `Execute`. `Quartz.RetryPolicy` says how many times and how far
apart a failed job's trigger re-fires.

| Member | What it is |
|---|---|
| `RetryPolicy.Fixed(maxAttempts, delay)` | The same wait before every retry |
| `RetryPolicy.Exponential(maxAttempts, initialDelay, factor = 2, maxDelay = null)` | A wait multiplied by `factor` each time, optionally clamped |
| `RetryPolicy.Explicit(params delays)` | A table of waits; `MaxAttempts` is its length and the last entry repeats |
| `RetryPolicy.DelayFor(attempt)` | The wait before the given retry, counting from one |
| `RetryPolicy.ToStoredString()` / `Parse` / `TryParse` | The single-string form used by the `RETRY_POLICY` column, the JSON payload and a serialized trigger |

* There is no public constructor, so a policy that cannot be honoured (no attempts, a negative wait, a
  shrinking backoff) cannot be built.
* A policy keeps which factory made it: `Exponential(3, delay, factor: 1)` waits like `Fixed(3, delay)` but
  is a different value, stored as `exp;3;…;1`.
* Equality compares the delay table by content, and the backoff factor bit for bit, as preserved by the
  `"R"` format.
* `MaxAttempts` counts retries *after* the first failure: `Fixed(2, …)` runs a persistently failing job
  three times.

### Where a policy is set and read

| Member | What it is |
|---|---|
| `ITrigger.RetryPolicy` | The trigger's policy, or `null` (the default) when it does not retry |
| `ITrigger.RetryAttempt` | How many times the executing occurrence has been retried; `0` on a regular fire |
| `IMutableTrigger.RetryPolicy` / `RetryAttempt` | The setters, for a job store restoring a trigger from its row |
| `TriggerBuilder<TJob>.WithRetryPolicy(RetryPolicy?)` | Give a trigger a policy while building it |
| `ITriggerConfigurator<TJob>.WithRetryPolicy(RetryPolicy?)` | The same, in the DI configuration API |
| `TriggerDetailsUpdate.WithRetryPolicy(RetryPolicy?)` | Change a stored trigger's policy without rescheduling it |
| `TriggerHeader.RetryPolicy` (string) / `RetryAttempt` | What a trigger listing reports |

* A builder-built trigger fully defines its policy, so replacing a stored trigger with a definition that
  sets none clears it, as with `WithExecutionGroup` and `WithPreferredNode`.
* `TriggerDetailsUpdate` cannot set the *attempt*, which belongs to the occurrence in flight.
* `TriggerHeader` carries the stored string, so a row with a policy this node cannot parse (written by a
  newer node) still lists. Its constructor gained two parameters, which breaks code that constructs one.

### Where a policy is stored

As its stored string: the `RETRY_POLICY` column, the `retryPolicy` property of a serialized trigger, and
the `retryPolicy` field of a JSON scheduling file or configuration section. A payload from before 4.0 reads
back as no policy and no attempt.

```json
{
  "Name": "nightly-import",
  "JobName": "import",
  "RetryPolicy": "exp;3;00:00:30;2;00:10:00",
  "Cron": { "Expression": "0 0 2 * * ?" }
}
```

A string that is not a policy is refused when the file is read.

### What happens when a job fails

A job fails, for retry purposes, when `Execute` throws anything but a `JobExecutionException` that asks for
something itself. With attempts left, the scheduler sets the next fire time to
`now + policy.DelayFor(attempt + 1)`, increments the attempt, and gives the job store the new instruction
`SchedulerInstruction.RetryTrigger`. Nothing else about the trigger changes.

| Member | What it is |
|---|---|
| `SchedulerInstruction.RetryTrigger` | New enum member, appended; no existing value moved. `ITriggerListener.TriggerComplete` sees it; there is no new listener member |
| `IJobExecutionContext.RetryAttempt` | `0` on a regular fire, *n* on the *n*-th retry |
| `TriggerBase.RetryFired(ICalendar?)` | `public virtual`, only on `TriggerBase`; `IOperableTrigger` is unchanged |
| `IDriverDelegate.UpdateTriggerForRetry` / `ClearTriggerRetryAttempt` | **Breaking for anything implementing `IDriverDelegate`**; `StdAdoDelegate` implements both `virtual` |

* **A retry never displaces the next scheduled occurrence.** If `retryAt + 1s >= NextFireTimeUtc`, the
  retry is dropped and the occurrence replaces it, with the attempt reset. (The margin is there because
  `CalendarIntervalTriggerImpl.GetFireTimeAfter` and `DailyTimeIntervalTriggerImpl.GetFireTimeAfter` add a
  second before searching.) A retry is never scheduled past the trigger's `EndTimeUtc` or the end of the
  calendar; when an exponential wait would overflow a `DateTimeOffset`, the retry is declined.
* **A retry fire is not a `Triggered()` call.** `RetryFired` advances past the retry instant with the same
  calendar-skip loop, but touches no counter and leaves `PreviousFireTimeUtc` alone, so a retry uses no
  repeat count or RRULE `COUNT` slot, and reports the *original* occurrence as its `ScheduledFireTimeUtc`.
* **Exhausted attempts return to the ordinary schedule, not to `TriggerState.Error`.** A misfired retry gets
  the trigger's own misfire instruction and the attempt is cleared; there is no retry-specific misfire
  policy.
* **`RefireImmediately` is not a zero-delay retry.** It re-runs the job on the same thread in the same
  firing, with no delay, no ceiling, nothing persisted and no slot released, and leaves `RetryAttempt`
  alone. An explicit `RefireImmediately` or `Unschedule*` wins over the policy. An
  `OperationCanceledException` from the scheduler's own token never retries: shutdown and interrupt are
  operator decisions, and a node vanishing mid-execution is what `RequestsRecovery` is for.

The meter `quartz.trigger.retry` counts each retry scheduled, tagged like `quartz.trigger.misfire`. Log
event `1056` reports the trigger, the attempt and the retry instant at `Information`.

### If you implement `IDriverDelegate`: the retry members

Two new members without default implementations, like `UpdateTriggerPreferredNodeConditional` and the
listing members. `StdAdoDelegate` implements both as `virtual`; a delegate written from scratch must
supply them.

| Member | Purpose |
|--------|---------|
| `UpdateTriggerForRetry(conn, trigger, newState, ct)` | Write the retry a completing trigger scheduled: `NEXT_FIRE_TIME`, `RETRY_ATTEMPT` and `TRIGGER_STATE` in one narrow statement |
| `ClearTriggerRetryAttempt(conn, triggerKey, ct)` | Reset `RETRY_ATTEMPT` to zero and touch nothing else |

They are separate from `UpdateTrigger`, so a retry does not rewrite the preferred node and job data map.
The clear runs only when the completing trigger had a non-zero attempt.

`UpdateMisfiredTrigger` and `UpdateMisfiredTriggers` keep their shape, but now also write `RETRY_ATTEMPT`:
misfire handling recomputes the trigger from its schedule, dropping the pending retry. A delegate that
spells those statements itself needs the column.

## Trimming annotations

`ScheduleJob<T>`, `AddTrigger<TJob>` and `TriggerBuilder.Create<TJob>()` have `[DynamicallyAccessedMembers]`
on their type parameter, like `AddJob<T>` and `JobBuilder.Create<T>()`. A generic method of yours that
forwards to them, built with the trim analyzer on, needs the same annotation:

```diff
- public static void Register<TJob>(IQuartzBuilder q) where TJob : IJob
+ public static void Register<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] TJob>(IQuartzBuilder q) where TJob : IJob
      => q.ScheduleJob<TJob>(t => t.StartNow());
```

**`Quartz` is marked `IsTrimmable`.** Under `TrimMode=full` it is member-trimmed either way; the mark
changes the reporting. With `TrimmerSingleWarn` on (the default), an assembly's warnings collapse into one
`IL2104: Assembly 'Quartz' produced trim warnings`, and `IsTrimmable` exempts its `IL2026`s from that. So
each `[RequiresUnreferencedCode]` call site is reported, and `<TrimmerSingleWarn>false</TrimmerSingleWarn>`
shows the rest: about fifty, at the call sites listed in `src/Quartz/TrimAnalysisBaseline.cs`.

* This is not a regression: the reflection was always there. No warning is suppressed in the shipped
  assembly.
* Reflection is needed for configuration by flat `quartz.*` keys, jobs named as strings
  (`job_scheduling_data` XML, a persisted `JOB_CLASS_NAME`), and `JobDataMap` values bound onto job
  properties. Configuring in code, referencing job types statically and keeping job data to primitives
  uses much less of it.
* A **persistent job store** publishes trimmed and native AOT when it reaches its driver without naming
  one: `UseSqlServer(SqlClientFactory.Instance, connectionString)`, or a registered `DbDataSource`. The
  default System.Text.Json serializer has a source-generated contract for every blob a store writes, and
  custom trigger and calendar types are handled by their registry. The one gap is a job-data value of your
  own type: register its metadata with `SystemTextJsonSerializerRegistry.AddTypeInfoResolver`; see
  [Publishing Trimmed and Native AOT](how-tos/trimming-and-native-aot.md).

**`Quartz` declares `IsAotCompatible`**, which 3.x does not: a native AOT publish reports no `IL3050`
against it. Binding the `Quartz` configuration section is source-generated, so configuring from
`appsettings.json` is AOT-safe with nothing asked of your application. The `IL2xxx` warnings above are still
reported. `Quartz.Trimming.Canary` is published by ILCompiler and **run** on Windows, Linux and macOS on
every pull request, scheduling and firing over a real SQLite store and binding a whole scheduler from an
`IConfiguration`. The remaining work is [#3341](https://github.com/quartznet/quartznet/issues/3341).

**Every other shipped package declares whether it can be trimmed**; on 3.x each unmarked one produced a
single `IL2104`.

* `Quartz.Jobs`, `Quartz.Plugins`, `Quartz.HttpClient`, `Quartz.AspNetCore`, `Quartz.Extensions.Redis` and
  `Quartz.Plugins.TimeZoneConverter` are marked trimmable and report their call sites individually: none
  to two each, all an `IL2026` for a job type spelled as a string. No public member of them gained
  `[RequiresUnreferencedCode]` or `[DynamicallyAccessedMembers]`.
* `Quartz.Serialization.Newtonsoft` and `Quartz.Dashboard` declare that they cannot be trimmed, in their
  csproj and nuget.org readme: Json.NET's contract is reflection, and Blazor Server binds components and
  parameters by name.

See
[Which packages say whether they can be trimmed](how-tos/trimming-and-native-aot.md#which-packages-say-whether-they-can-be-trimmed).

## Executing is a trigger state

`TriggerState` has an `Executing` member, reported by `IScheduler.GetTriggerState` and trigger listings.
With a persistent job store every node sees it, because it comes from the fired-triggers table. On 3.x only
`IScheduler.GetCurrentlyExecutingJobs` could tell, and only for its own node; that member is gone in 4.0
(see [what is running is a listing](#what-is-running-is-a-listing-not-a-list-of-contexts)).

### What changed in what you get back

A trigger with an execution in flight reported `Normal`, `Complete` or `Blocked`, depending on its
schedule; it now reports `Executing`. States are resolved in this order:

```text
None > Error > Paused > Executing > Blocked > Complete > Normal
```

So a paused or errored trigger reports that even while its job runs.

* `Blocked` now means a **different** trigger of the same `[DisallowConcurrentExecution]` job is running.
  The running trigger reports `Executing`; before, both reported `Blocked`.
* A trigger with no fire times left whose final execution is running reports `Executing`, not `Complete`
  (3.x: `Blocked`).
* A trigger whose job allows concurrent execution can be executing and still due to fire. It reports
  `Executing` until the last run finishes.

### What to check in your own code

* **A health check or guard like `if (state == TriggerState.Normal)`** now sees `Executing` for a busy
  trigger. Treat `Executing` as healthy.
* **A watchdog like `if (state != TriggerState.Normal) await ResumeTrigger(key)`** can now alter a healthy
  trigger's schedule. `ResumeTrigger` applies the misfire policy to any trigger whose next fire time has
  passed, whatever its state, and for a long job on a short interval that holds for the whole execution.
  This is not new, but `Executing` sends more triggers there. Gate the watchdog on the states you mean to
  repair (`Error`, `Paused`).
* **Alerting that treats `Blocked` as "a job is running"** should use `Executing`.

### Filtering a listing by state

`TriggerQuery.State` accepts `Executing`. The filter and the reported state are derived together, so a
listing filtered by `Normal` returns no trigger it would report as `Executing`.

### A note on stale executions

If a node dies mid-execution, its fired-trigger rows stay until another node's cluster recovery clears
them. Until then the trigger reports `Executing` although nothing runs, and is missing from a
`Normal`-filtered listing. `Blocked` already had the same window.

* An ordinary job's rows are cleared once another node detects the failure, after roughly
  `clusterCheckinInterval` + `clusterCheckinMisfireThreshold`.
* A `[DisallowConcurrentExecution]` job's executing rows are *kept* on first detection, since the node may
  still be running the job, and removed on a later pass once the elapsed time exceeds
  `2 × clusterCheckinInterval + clusterCheckinMisfireThreshold`. With a 15-second interval and a 60-second
  threshold, expect up to about 90 seconds plus one more check-in cycle.

### If you implement `IDriverDelegate`: the executing state

`IsTriggerCurrentlyExecuting` is replaced by `SelectTriggerStateWithExecuting`, which returns the stored
state and whether an execution is in flight from one statement. Subclasses of `StdAdoDelegate` get it for
free. There is no schema change.

`GetTriggerState` now calls it instead of `SelectTriggerState`. If you override `SelectTriggerState` for a
vendor quirk or a legacy state value, override `SelectTriggerStateWithExecuting` too; the compiler cannot
tell you, since the old method is still on the interface and used elsewhere.

## What is running is a listing, not a list of contexts

`IScheduler.GetCurrentlyExecutingJobs()` is **removed**. It returned the live contexts of the process it
was called in, so it could answer for only one node.

```diff
- List<IJobExecutionContext> running = await scheduler.GetCurrentlyExecutingJobs();
+ PagedResult<FireInstance> running = await scheduler.QueryFireInstances();
```

With a persistent job store the result covers the whole cluster. A `FireInstance` is what a store knows
about one firing:

| Member | Meaning |
|---|---|
| `FireInstanceId` | identifies this firing; what `InterruptFireInstance` takes |
| `TriggerKey` | the trigger that fired |
| `JobKey` | the job; `null` while the firing is only `Acquired` |
| `SchedulerInstanceId` | the node that reserved or is running it |
| `State` | `FireInstanceState.Acquired` or `FireInstanceState.Executing` |
| `FireTimeUtc` | when the owning node recorded the firing |
| `ScheduledFireTimeUtc` | the fire time the schedule called for |
| `ExecutionGroup` | the execution group the trigger carried when it fired |

`FireInstanceQuery` derives from `PagedQuery` and filters by trigger group and name matchers, job key,
scheduler instance id and state. **`State` defaults to `Executing`**, which lists what is running; set it to
`null` to include reservations. Results are ordered by trigger group, trigger name, then fire instance id,
since one trigger can have several firings at once.

### What a fire instance deliberately does not carry

The job instance, the merged job data map, `Result`, `JobRunTime` and the cancellation handle exist only in
the process running the job. If you need them, for a progress endpoint or an in-process cancel button,
keep the contexts yourself:

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

`FireInstanceId` links a context you hold to a `QueryFireInstances` row: list across the cluster, then use
the live context when the firing is yours.

### Three things to know about the numbers

* **A vetoed firing does not stay listed.** A veto completes the firing, which removes it; it can be listed
  only between the store recording it and the veto. `GetCurrentlyExecutingJobs` never showed one.
* **Elapsed time is `yourClock.UtcNow - FireTimeUtc`**, where `FireTimeUtc` comes from the firing node's
  clock. With skewed clocks it can be negative; clamp it at zero, as the dashboard does.
* **`ScheduledFireTimeUtc` is the schedule after a misfire.** For a misfired trigger it is the rescheduled
  time, not the missed one, so it can differ from `IJobExecutionContext.ScheduledFireTimeUtc`, and
  `FireTimeUtc - ScheduledFireTimeUtc` is not misfire lateness.

### `SchedulerMetadata` gained a node-local count

`SchedulerMetadata.LocalExecutingJobs` is the number of executions in *this* process. On a cluster it
differs from a `QueryFireInstances` count.

### The HTTP endpoint changed shape (breaking)

`GET …/jobs/currently-executing` is replaced by `GET …/jobs/fire-instances`, paged like the other
listings: the bare array of contexts becomes the usual `{ items, hasMore, totalCount }` envelope.

| Old field | New field |
|---|---|
| `jobDetail.name` / `jobDetail.group` | `jobName` / `jobGroup`, `null` for a reserved firing |
| `jobDetail.*` (type, durability, recovery, concurrency flags, job data map) | gone; fetch the job detail by key |
| `trigger` (the whole serialized trigger) | `triggerName` / `triggerGroup` |
| `trigger.executionGroup` | `executionGroup` |
| `calendar` | gone |
| `recovering` | gone |
| `fireTime` | `fireTimeUtc` |
| `scheduledFireTime` | `scheduledFireTimeUtc` |
| `previousFireTime`, `nextFireTime` | gone; they describe the trigger, not the firing |
| — | `fireInstanceId` (new; the old DTO never carried it, although clients read for it) |
| — | `schedulerInstanceId` (new) |
| — | `state` (new; `"Acquired"` or `"Executing"`, a name like every other enum on the wire) |

Query parameters: `skip`, `take`, `includeTotalCount`, the `group*`/`name*` matcher forms of the other
listings, `jobName`+`jobGroup`, `schedulerInstanceId` and `state`. Without `state` the default is
`Executing`; `state=Any` asks for every state.

The scheduler body's `statistics` object gained `localExecutingJobs`.

### If you implement `IJobStore`

Four members:

* `QueryFireInstances(FireInstanceQuery, CancellationToken)`: the listing, abstract like the rest of the
  query family.
* `QueryClusterNodes(CancellationToken)`: see
  [The nodes of a cluster are a listing](#the-nodes-of-a-cluster-are-a-listing).
* `Exists(string calendarName, CancellationToken)`: replaces `CalendarExists`; answer it without
  materializing the calendar.
* `Initialize` takes only a `SchedulerIdentity`; see [SPI changes](#spi-changes) for its 3.x parameters:

```csharp
ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default);
```

The identity (scheduler name and this node's instance id) cannot be a constructor argument: with
`GenerateInstanceId`, an `IInstanceIdGenerator` produces the id after the container has built the object
graph, as with `LockHandlerContext` on `ILockHandler.Initialize`. A store records it against the firings it
owns. The ADO.NET store's special case in the scheduler factory is gone; every store is told the same way.

### If you implement `IDriverDelegate`: fire instances

`SelectFireInstances(conn, FireInstanceQuery, CancellationToken)` is new; subclasses of `StdAdoDelegate`
get it for free. It is separate from `SelectFiredTriggerRecords`, whose callers are recovery passes that
must see every row.

`QRTZ_FIRED_TRIGGERS.EXECUTION_GROUP` is now written by the fired-trigger insert and update and read into
`FireInstance.ExecutionGroup`. **No schema change**: the column has shipped on every dialect since 3.18.
Rows written by an earlier 4.0 preview hold `NULL`.

## The nodes of a cluster are a listing

`QueryClusterNodes` lists a cluster's nodes. On 3.x nothing read `QRTZ_SCHEDULER_STATE`, and
`SchedulerMetadata.JobStoreClustered`, a `bool` saying clustering is *on*, was the only cluster fact the API
exposed.

```csharp
List<ClusterNode> nodes = await scheduler.QueryClusterNodes();
```

`ClusterNode` is `InstanceId`, `LastCheckInUtc`, `CheckInInterval`, a `ClusterNodeState` of `Alive`,
`Overdue` or `Failed`, and `IsCurrentNode`.

* The answering node comes first and is always present, even before its first check-in. It is the only one
  with `IsCurrentNode` `true`; the rest follow by instance id.
* The state uses the recovery sweep's own predicate, so the listing and failover agree.
* The two times are `null`, not zero, when the store keeps no check-in history. **`null` is not the
  epoch**: falling back to `DateTimeOffset.MinValue` shows a node dead since year one.
* A scheduler that is not clustered returns itself, `Alive` and with no times, not an empty list. See
  [Seeing the cluster](tutorial/advanced-enterprise-features.md#seeing-the-cluster).

`SchedulerStateRecord` is unchanged and remains the ADO.NET store's row shape (as `FiredTriggerRecord` sits
beside `FireInstance`); `ClusterNode` is the store-neutral projection.

| Where | What is new |
|---|---|
| `IScheduler` | `ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)`; implement it in an `IScheduler` of your own (`DelegatingScheduler` forwards it) |
| `IJobStore` | `ValueTask<List<ClusterNode>> QueryClusterNodes(CancellationToken cancellationToken = default)`, a plain member your store must implement. A store with no cluster state returns itself, `Alive`, both times `null` |
| HTTP API | `GET {ApiPath}/schedulers/{name}/nodes`, unpaged; `HttpScheduler.QueryClusterNodes` reads it (see [Cluster nodes](packages/http-api.md#cluster-nodes)) |
| Dashboard | `IQuartzApiClient.QueryClusterNodes(name, CancellationToken)`, and a **Cluster** page at `/quartz/cluster` |

No schema change, and no writes: it reads the rows the check-in loop already keeps.

## An unset execution group can be the trigger's group

`ExecutionLimitsBuilder.UseTriggerGroupWhenUnset()` limits a trigger with no execution group as if it
belonged to a group named after its `TriggerKey.Group`. It is opt-in; see
[Execution Groups](/documentation/quartz-4.x/tutorial/execution-groups.html#letting-the-trigger-group-stand-in).

`ExecutionSlots.TryTake` takes the trigger group as a second argument, so a job store of your own cannot
skip the derivation. `ExecutionLimits.UsesTriggerGroupWhenUnset`, set by the builder method, says whether it
is used.

```diff
- if (!slots.TryTake(candidate.ExecutionGroup))
+ if (!slots.TryTake(candidate.ExecutionGroup, candidate.Key.Group))
```

## An execution limit can be cluster-wide

An execution limit has a scope. `ExecutionLimitScope.Node`, the default and the only behaviour of 3.x and
earlier 4.0 previews, limits what *this* node runs, so an N-node cluster runs up to N times the number.
`ExecutionLimitScope.Cluster` limits all nodes sharing the job store together, as a per-tenant quota
needs:

```csharp
q.UseExecutionLimits(limits => limits
    .ForGroup("high-cpu", 2)                                  // per node, as before
    .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster)  // per cluster
    .ForOtherGroups(1));
```

Both scopes can appear in one set of limits. The property spelling is `quartz.clusterExecutionLimit.<group>`,
with the same group keys and values as `quartz.executionLimit.<group>`.

**No schema change or migration** on either branch. The count aggregates
`QRTZ_FIRED_TRIGGERS.EXECUTION_GROUP` (on every dialect since 3.18), whose rows live as long as a
reservation: inserted on acquisition, updated when the trigger fires, deleted on completion or by cluster
recovery.

Read [Execution Groups](/documentation/quartz-4.x/tutorial/execution-groups.html#clustering-considerations)
before relying on it:

* the ceiling is **approximate, with a bounded overshoot**, unless `AcquireTriggersWithinLock` is on;
* it **fails closed**: a node that cannot reach the store fires nothing;
* work held at a ceiling for longer than `MisfireThreshold` goes to misfire handling.

### What moved

| Before | 4.0 |
|---|---|
| `ExecutionGroupLimit(ExecutionGroupScope Scope, int? MaxConcurrent)` | `ExecutionGroupLimit(ExecutionGroupScope Group, int? MaxConcurrent, ExecutionLimitScope Scope = Node)` |
| `ExecutionLimits.TryGetLimit(ExecutionGroupScope scope, out int?)` | `TryGetLimit(ExecutionGroupScope group, out int?)`: parameter renamed; still returns only the number |
| `ExecutionLimits.CreateSlots()` | `CreateSlots(IReadOnlyCollection<ExecutionGroupInFlight>? clusterInFlight = null)` |
| — | `ExecutionLimits.HasClusterScopedLimits` |
| — | `ExecutionGroupInFlight(string? ExecutionGroup, string TriggerGroup, int Count)` |
| — | `TriggerAcquisitionCriteria.ClusterInFlight` |
| — | `IDriverDelegate.SelectExecutionGroupsInFlight(conn, cancellationToken)` |
| `ExecutionLimitsResponse` / `SetExecutionLimitsRequest` carrying `Dictionary<string, int?>` | carrying `Dictionary<string, ExecutionLimitDto>`, where the DTO is `(int? MaxConcurrent, ExecutionLimitScope Scope)` |
| `ExecutionLimitsDto(Dictionary<string, int?> Limits)` (dashboard) | `ExecutionLimitsDto(Dictionary<string, DashboardExecutionLimit> Limits, bool UsesTriggerGroupWhenUnset = false, bool CanReport = true)`; see [The dashboard's client speaks one currency](#the-dashboard-s-client-speaks-one-currency) |

* **Implementing `IDriverDelegate` from scratch:** the new member is a deliberate compile break, since a
  stub returning "nothing in flight" would fail the ceiling open. `StdAdoDelegate` implements it for every
  dialect.
* **Implementing `IJobStore`:** nothing is required. A store with `Clustered == false` has one node, so both
  scopes mean the same number. A clustered store passes its in-flight counts to `CreateSlots`, and must
  **not** expect the scheduler thread to have subtracted this node's running work from a cluster-scoped
  limit; those firings are already in its counts, and subtracting them twice halves the quota.

## Batched Misfire Recovery

Misfire recovery handles a batch of misfired triggers in a few statements: one set-based read, and writes
batched where the provider supports it (`DbConnection.CanCreateBatch`), individual statements elsewhere.
Nothing needs configuring.

`IDriverDelegate` has two new members; `StdAdoDelegate` subclasses get both:

| Member | Purpose |
|--------|---------|
| `SelectMisfiredTriggersToRecover` | Reads a whole misfire batch as populated triggers in one round trip |
| `UpdateMisfiredTriggers` | Applies a batch of misfire updates, batching the statements where supported |

A delegate for a database with its own row-limiting syntax should also override
`GetSelectMisfiredTriggersToRecoverSql`.

`ITriggerListener.TriggerMisfired` is now raised for every trigger in a batch before any of the batch's
updates are written, not interleaved per trigger. Both still happen in one transaction under the same
lock, so other nodes see no difference.

## Batched trigger fire

Firing a trigger took six to nine round trips inside the `TRIGGER_ACCESS` lock, plus a read per trigger of
the job on completion. A fire now reads the trigger's row once and writes everything in one batch, and
completion asks one question. Nothing needs configuring: without `DbConnection.CanCreateBatch`, the same
statements go out one at a time, in the same order as before.

`IDriverDelegate` has three new members:

| Member | Purpose |
|--------|---------|
| `ApplyTriggerFired` | Every write of one fire, described by `TriggerFiredUpdate`, as one batch: the fired-trigger row, the misfire original fire time, the sibling states of a serial job, the trigger's row and its schedule |
| `UpdateTriggerStatesForJobFromOtherState(conn, jobKey, IReadOnlyList<TriggerStateTransition>, ct)` | Several conditional state changes for one job in one round trip, beside the single-transition overload |
| `SelectTriggerKeysForJob(conn, jobKey, StoredTriggerState, ct)` | The keys of a job's triggers in one state, beside the unfiltered overload |

`ITriggerPersistenceDelegate` gains `TryDescribeUpdateExtendedTriggerProperties`, which adds the statement
`UpdateExtendedTriggerProperties` would issue to a `List<SqlStatement>`, so the schedule travels with the
trigger's row. It is a default interface member returning `false`, so an older persistence delegate keeps
its own round trip. `SqlStatement` and `SqlStatementParameter` are public for this.

Two contracts moved:

| Was | Is |
|-----|-----|
| `StoredTriggerHeader(Key, JobKey, State, NextFireTimeUtc)` | `StoredTriggerHeader(Key, JobKey, State, NextFireTimeUtc, TriggerType)`: the type is read from the same row, removing a separate `SELECT TRIGGER_TYPE` |
| `SqlSelectTriggerHeader` projects four columns | It projects `TRIGGER_TYPE` as well |

Behavioural notes:

* The fired-trigger row is written from the scheduled fire time the store passes to `ApplyTriggerFired`,
  not from the trigger's next fire time, so it no longer has to be written before `Triggered()` advances
  the trigger.
* A failed batch is replayed statement by statement so the exception names the failing statement, unless
  the failure was transient: then it surfaces as itself, so the store's retry recognises it. This applies
  to the batched misfire writes too.
* A failed fire raises `Couldn't record the fire of trigger '…' for '…' job`, instead of one of
  `Couldn't update fired trigger`, `Couldn't update states of blocked triggers` or
  `Couldn't store trigger '…'`.
* **`IDriverDelegate.UpdateFiredTrigger` is gone**, so an override of it fails to compile rather than
  silently stop running. `ApplyTriggerFired` writes that row now; move an override that changed what a fire
  records there. The statement is unchanged; the internal `StdAdoConstants.SqlUpdateFiredTrigger` still
  spells it.
* The fire path no longer calls `TriggerExists` or `UpdateTrigger`, which other paths still use. **Move an
  override of either that changed what a fire stores to `ApplyTriggerFired`**; it still compiles and works
  elsewhere, but not on this path.
* Completion asks for a job's trigger keys in the state it needs, loads those in one read, and applies
  their misfire policies as one batched write, as misfire recovery does. A trigger that runs out of fire
  times while blocked is still stored `COMPLETE`, finalized to the scheduler listeners, and deleted.

## Batched cluster recovery

Recovering a failed node issued up to ten statements per fired-trigger row it left, each its own round
trip inside the `TRIGGER_ACCESS` lock. The same statements now go a set at a time, where `DbBatch` is
supported; other providers issue them one at a time, in the same order. Nothing needs configuring.

`IDriverDelegate` has three new members; subclassing `StdAdoDelegate` gets all three:

| Member | Purpose |
|--------|---------|
| `UpdateTriggerStatesForJobsFromOtherState(conn, IReadOnlyCollection<JobKey>, newState, oldState, ct)` | The single-job statement for each job key, in one batch; unblocks the siblings of every interrupted execution |
| `DeleteFiredTriggers(conn, IReadOnlyCollection<string> entryIds, ct)` | The single-entry delete for each id, in one batch; clears a dead node's rows while holding some back |
| `SelectTriggerKeysInState(conn, IReadOnlyCollection<TriggerKey>, state, ct)` | Which of the given triggers are in a state, in one read instead of `SelectTriggerState` per key; finds which of a dead node's triggers ran to COMPLETE |

The first two return `ValueTask`, not a row count, since a batch does not report one per command portably.

Releasing a dead node's reservations uses
`UpdateTriggerStatesFromOtherStates(conn, IReadOnlyCollection<TriggerKey>, newState, oldStates, ct)`; see
[Batched trigger acquisition, and the sets pause and resume work on](#batched-trigger-acquisition-and-the-sets-pause-and-resume-work-on).
It is one `UPDATE` with a key-set predicate, so it *does* return a row count; for one old state, pass a set
of one.

Behavioural notes:

* `ClusterRecover` runs named steps: release, unblock, reschedule, delete the fired rows, delete the
  triggers those rows were the last of, and give up the failed node's registration. They run in one
  transaction under one lock on disjoint rows, so other nodes see no difference.
* Scheduling a replacement firing stays row by row, since each checks whether its job still exists and
  reads its trigger's data map.
* **A node that finds its own `QRTZ_SCHEDULER_STATE` row gone now handles it.** 3.x logged
  `This scheduler instance (…) is still active but was recovered by another instance in the cluster` and
  carried on. It now writes its row back, names the peer that recovered it where the remaining rows show
  that, counts the event on `quartz.cluster.recovery.trigger` under its own instance id, and does not
  recover its own fired triggers, which the peer already did. See
  [When the node that was taken over is still running](operations.md#when-the-node-that-was-taken-over-is-still-running).

## Batched trigger acquisition, and the sets pause and resume work on

Acquisition cost three statements per candidate after the read that found it: read it back,
compare-and-swap it to acquired, and insert its fired-trigger row. Pausing or resuming a set of triggers,
or a matcher's jobs, cost two or three statements per key. All of it ran inside the `TRIGGER_ACCESS` lock.

Now an acquisition round reads its candidates in one statement and writes their fired-trigger rows in one
batch; the compare-and-swap stays per candidate, since it decides whether that candidate was acquired.
Pause and resume read a set's stored states once, then issue one statement per transition needed. Nothing
needs configuring, and the rows written and results returned are unchanged.

In `IDriverDelegate`, every member below is a **default interface member** whose default is the old per-key
loop, so an older delegate compiles and behaves as before, still paying the round trips. `StdAdoDelegate`
overrides all of them.

| Member | Purpose |
|--------|---------|
| `InsertFiredTriggers(conn, IReadOnlyList<IOperableTrigger>, state, jobDetail, ct)` | An acquisition round's fired-trigger rows as one batch |
| `SelectStoredTriggerHeaders(conn, IReadOnlyCollection<TriggerKey>, ct)` | The plural of `SelectTriggerHeader`, for pause and resume. Not the listing `SelectTriggerHeaders`, which pages over `TriggerHeader` |
| `UpdateTriggerStatesFromOtherStates(conn, IReadOnlyCollection<TriggerKey>, newState, oldStates, ct)` | One conditional transition for a key set, as one `UPDATE` with a key-set predicate, beside the store-wide overload. Cluster recovery uses it with one old state |
| `SelectTriggerKeysForJobs(conn, IReadOnlyCollection<JobKey>, ct)` | Every trigger key of a set of jobs in one read, for pausing and resuming a job matcher |
| `SelectPausedJobGroups(conn, IReadOnlyCollection<string>, ct)` | Which of a set of job groups already have a paused row: the set form of `IsJobGroupPaused` |
| `InsertPausedJobGroups(conn, IReadOnlyCollection<string>, ct)` | The missing paused-job-group rows, together |

One new property:

| Member | Purpose |
|--------|---------|
| `FiltersAcquisitionJobTypeExclusions` | Whether `SelectTriggersToAcquire` already leaves out `TriggerAcquisitionCriteria.ExcludedJobTypeNames`. Defaults to `false`; `StdAdoDelegate` returns `true` |

Ignoring `ExcludedJobTypeNames` would make the node *run* excluded job types, so `AdoJobStoreBase` keeps a
backstop that drops an excluded candidate by the job type name the acquisition read returned. A delegate
that returns `true` is trusted and the backstop is skipped. **If you subclass `StdAdoDelegate` and override
`GetSelectNextTriggerToAcquireSql` with a statement that does not splice in the exclusion terms, override
`FiltersAcquisitionJobTypeExclusions` back to `false`**, or the exclusions are enforced nowhere.

Behavioural notes:

* `StdAdoDelegate.SelectTriggersForJob` reads the job's trigger keys, then one `SelectTriggers` for the set,
  instead of reading each trigger separately. The triggers and their order are the same.
* `StdAdoDelegate.SelectTriggers` and `InsertFiredTriggers` answer a one-element set through
  `SelectTrigger` and `InsertFiredTrigger`, since the default acquisition batch size is one. An override of
  either singular member therefore also applies to a set of one.
* Pausing or resuming a job no longer loads that job's triggers to reach their keys.
* `PauseAll` and `ResumeAll` pass the any-group matcher once, instead of once per trigger group.
* A resume still applies each overdue trigger's misfire policy with that trigger's own write, since
  recomputing a schedule is not a set operation.

## Job store listings became queries

Listing members are replaced by query members that take a query record and return one page of projected
results, so a listing pages, counts, and no longer costs a round trip per key. **Existing code keeps
compiling**: every removed `IScheduler` member is back as an extension method in `SchedulerQueryExtensions`,
with the same name and signature.

### What replaced what

`IScheduler` — the left column still works, as an extension method:

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

`IJobStore` loses the same members plus the counting and existence ones, with no extension methods; a job store
implements the query members:

| Removed from `IJobStore` | Use instead |
|---|---|
| `GetJobKeys`, `GetTriggerKeys` | `QueryJobs`, `QueryTriggers` |
| `GetJobGroupNames`, `GetTriggerGroupNames`, `GetPausedTriggerGroups` | `QueryJobGroups`, `QueryTriggerGroups` |
| `GetCalendarNames` | `QueryCalendarNames` |
| `IsJobGroupPaused`, `IsTriggerGroupPaused` | the matching `Query*Groups` with `Name` and `Paused = true` |
| `GetNumberOfJobs`, `GetNumberOfTriggers`, `GetNumberOfCalendars` | the matching query with `Take = 0, IncludeTotalCount = true` |
| `CalendarExists(name)` | `Exists(string calendarName)` — see [Reading has two altitudes on purpose](#reading-has-two-altitudes-on-purpose) |

New on both interfaces: **`GetJobDetails(jobKeys)`** (on `IJobStore`, **`GetJobs(jobKeys)`**) and
**`GetTriggers(triggerKeys)`** fetch many by key in one round trip. Missing keys are absent, duplicates fold
away, and results keep the order of the keys. The names differ on purpose: `IScheduler` hands users
`IJobDetail`, so it says `GetJobDetail`/`GetJobDetails`; the store pairs `GetJob`/`GetJobs` with
`GetTrigger`/`GetTriggers`.

### Paging and projection

Every query derives from `PagedQuery` (`Skip`, `Take`, `IncludeTotalCount`) and returns a `PagedResult<T>`
(`Items`, `HasMore`, nullable `TotalCount`). `HasMore` is exact and free: stores read one item past `Take`. See
[Querying Jobs and Triggers](tutorial/querying-jobs-and-triggers.md#paging).

* `Take` defaults to **250** (`PagedQuery.DefaultTake`); earlier 4.0 previews defaulted to `int.MaxValue`. The
  HTTP endpoints use the same default when a request names no `take`.
* `HttpScheduler` always sends `take`; earlier previews omitted it for `int.MaxValue`, which would now leave the
  choice to the server.
* The compat extension methods pin `Take = PagedQuery.All`, so they still return everything, as on 3.x.
* **`PagedQuery.All`** is `int.MaxValue`; over HTTP it is `?take=all`. `Take = int.MaxValue` and
  `?take=2147483647` still work.
* `Take = 0, IncludeTotalCount = true` runs only the count and skips the page select.

```csharp
PagedResult<JobHeader> everything = await scheduler.QueryJobs(new JobQuery { Take = PagedQuery.All });
```

The query types are records, so page by `with`-ing the next `Skip`:

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

`JobHeader` omits the `JobDataMap`, so listing never loads or deserializes job data. `TriggerHeader` carries
state, fire times, priority, calendar name and execution group. For whole objects, bulk-fetch the page:

```csharp
List<IJobDetail> details = await scheduler.GetJobDetails(page.Items.Select(x => x.Key).ToList());
```

A count reads no rows, and counts what a filter selects rather than a whole table:

```csharp
// Before
int total = await jobStore.GetNumberOfTriggers();

// After
PagedResult<TriggerHeader> count = await scheduler.QueryTriggers(
    new TriggerQuery { Take = 0, IncludeTotalCount = true });
int total = count.TotalCount!.Value;
```

```csharp
PagedResult<TriggerHeader> failed = await scheduler.QueryTriggers(
    new TriggerQuery { State = TriggerState.Error, Take = 0, IncludeTotalCount = true });
Console.WriteLine($"{failed.TotalCount} triggers need attention");
```

`TriggerQuery` also filters on `Job`, `CalendarName` and `Group`. Filters combine with AND; a null `Group`
matches every group.

### Behavior worth knowing

* **Order is group, then name**, on every page. `RAMJobStore` compares ordinal; the ADO job store sorts in the
  database, by the **server's collation** (for most collations, different from ordinal only in case and accent
  handling). Sort the page yourself for a specific culture's order.
* **A null matcher throws**: `scheduler.GetJobKeys(null)` and `GetTriggerKeys(null)` raise
  `ArgumentNullException` instead of silently listing only the `DEFAULT` group.
* **The extension methods enumerate everything**, at the old cost. Use the query member with `Skip`/`Take`
  where the result can be large.
* **`JobGroup.Paused` works on both stores.** The 3.x ADO store could not record a paused job group:
  `IsJobGroupPaused` answered `false` for every group, and the pause was lost on restart. 4.x stores it in
  `QRTZ_PAUSED_JOB_GRPS`, so it survives a restart, reaches every cluster node, and is listed by
  `QueryJobGroups(new JobGroupQuery { Paused = true })`. **That new table makes the 4.0 migration mandatory
  even for a database that took every optional 3.x migration** — see
  [Database Schema Migration](#database-schema-migration).
* **An empty group can be paused.** `Paused = true` lists it; the unfiltered listing, which lists the groups
  jobs are in, does not. Trigger groups always behaved this way. So on the ADO store
  `PauseJobGroups(GroupMatcher<JobKey>.GroupEquals(g))` answers `[g]` for a group with no jobs, where 3.x
  answered `[]`.
* **A job-group pause applies to jobs added later**, on both stores: pausing records the group as well as
  pausing its jobs' triggers, and a trigger stored later for a job in it is born `PAUSED`. Only `RAMJobStore`
  used to do this. One refinement is still in-memory only — see
  [The two job stores answer the same way](#the-two-job-stores-answer-the-same-way).
* **Two indexes were added** for the ordered scans — see [Database Schema Migration](#database-schema-migration).

### If you implement `IDriverDelegate`: the listing members

Beyond the two batched-misfire members above, new members to implement:

| Member | Purpose |
|--------|---------|
| `SelectJobHeaders`, `SelectTriggerHeaders` | One page of projected job/trigger listing rows |
| `SelectJobGroups(conn, JobGroupQuery, ct)`, `SelectTriggerGroups(conn, TriggerGroupQuery, ct)` | One page of groups, with pause state |
| `SelectCalendarNames` | One page of calendar names |
| `SelectJobDetails`, `SelectTriggers` | Bulk fetch by key set |
| `InsertPausedJobGroup`, `DeletePausedJobGroup`, `IsJobGroupPaused` | Read and write `QRTZ_PAUSED_JOB_GRPS`, like the three `…PausedTriggerGroup` members |

Deleted, having had no caller: `SelectMisfiredTriggers`, both `HasMisfiredTriggersInState` overloads,
`SelectMisfiredTriggersInGroupInState`, `IsExistingTriggerGroup`, `SelectJobExecutionCount`,
`SelectTriggerForFireTime`, `SelectNumJobs`, `SelectNumTriggers`, `SelectNumCalendars`, `SelectCalendars`,
`SelectPausedTriggerGroups`, `SelectJobGroups(conn, ct)` and `DeleteAllPausedTriggerGroups`. The
`GetSelectNextMisfiredTriggersInStateToAcquireSql` hook went with them; delete any override of it.

Renamed to say what they return:

| 3.x | 4.x |
|---|---|
| `SelectTriggerNamesForJob` → `List<TriggerKey>` | `SelectTriggerKeysForJob` |
| `SelectJobsInGroup` → `List<JobKey>` | `SelectJobKeysInGroup` |
| `SelectTriggersInGroup` → `List<TriggerKey>` | `SelectTriggerKeysInGroup` |
| `SelectTriggerGroups(conn, GroupMatcher, ct)` → `List<string>` | `SelectTriggerGroupNames`; `SelectTriggerGroups` now means only the paged `(conn, TriggerGroupQuery, ct)` form |

Consolidated into records rather than overload families:

| 3.x | 4.x |
|---|---|
| `SelectFiredTriggerRecords`, `SelectFiredTriggerRecordsByJob`, `SelectInstancesFiredTriggerRecords` | `SelectFiredTriggerRecords(conn, FiredTriggerQuery, ct)` |
| four `DeleteFiredTriggers` overloads | `DeleteFiredTriggers(conn, FiredTriggerQuery, ct)` |
| two `SelectTriggerToAcquire` overloads | `SelectTriggersToAcquire(conn, TriggerAcquisitionCriteria, ct)` |
| two `SelectJobForTrigger` overloads | one, with a required `bool loadJobType` |
| `DeletePausedTriggerGroup(conn, string, ct)` | the `GroupMatcher<TriggerKey>` overload |

* `FiredTriggerQuery` has optional `Trigger`, `Job` and `InstanceId`, combined with AND; all null selects or
  deletes every fired trigger.
* `TriggerAcquisitionCriteria` has `NoLaterThan`, `NoEarlierThan`, `MaxCount`, `ExecutionLimits` and
  `LiveNodeCutoff`. A new acquisition filter will be another optional property, not another overload.
* `LiveNodeCutoff` is a `required DateTimeOffset`, like its two time siblings. It was briefly a `UtcTicks`
  `long` defaulting to zero, which meant "every node counts as dead"; the parameter binder does the tick
  conversion now.

Other delegate changes, all inherited by a `StdAdoDelegate` subclass:

* **Paging.** For row limiting other than ANSI `OFFSET … FETCH NEXT`, override
  **`ApplyPaging(sql, takeLimited)`** (appends the clause) and
  **`AddPagingParameters(cmd, skip, take, takeLimited)`** (binds it; `skip` and `take` are `int`). Override both
  when the clause names the parameters in the other order, since positional providers bind in statement order,
  as `MySQLDelegate` and `SQLiteDelegate` do for `LIMIT … OFFSET`. The names are the new public
  `AdoConstants.ParameterPageSkip` and `AdoConstants.ParameterPageTake`. See
  [Paging](how-tos/dialect-delegate.md#paging).
* **`AdditionalLikeWildcards`** lists a dialect's `LIKE` wildcards beyond `%` and `_`: `""` on `StdAdoDelegate`,
  `"["` on `SqlServerDelegate`. T-SQL reads `[` as a character class, so `?nameContains=[a-z]` matched by class
  on SQL Server and Sybase and literally elsewhere. `[` cannot be escaped portably; PostgreSQL rejects an escape
  before anything but a wildcard or itself.
  `EscapeSqlLikeWildcards` has an overload taking the extra characters; the one-argument form escapes the
  standard three.
* **Of the `IDbAccessor` value conversions, only the boolean pair stays overridable** (`GetDbBooleanValue` /
  `GetBooleanFromDbValue`, for Oracle). The date/time and time-span conversions are frozen, because the
  preferred-node liveness SQL does tick arithmetic on `LAST_CHECKIN_TIME` and `CHECKIN_INTERVAL`; 3.x only
  warned about such an override. A database storing `DATETIME` natively implements `IDriverDelegate` directly.
* **`UpdateTriggerStateFromOtherStateWithNextFireTime` is `virtual`**, like every other statement-issuing
  member. It is the lock-free acquisition path's claim on a trigger. Additive.
* **`StdAdoDelegate.SchemaResourceName`** (`protected virtual string?`, now documented) returns an embedded
  script's manifest resource name, which is all `SchemaProvisioning.CreateIfMissing` needs; all six dialects
  override it. See [A Driver Delegate for a New Database](how-tos/dialect-delegate.md).
* **`protected string SchedulerName { get; }`** is new beside `protected IDbProvider DbProvider { get; }`, so a
  delegate writing SCHED_NAME-scoped statements no longer copies it from `DriverDelegateContext` in
  `Initialize`. Additive.
* **`ITriggerPersistenceDelegate`** has a batch `LoadExtendedTriggerProperties` taking several trigger keys, a
  **default interface method** looping the single-key one; override it only to make a batch one round trip.

## Reading has two altitudes on purpose

Both stay. `IScheduler`'s `Query*` members take a record (filter, page, optional total count) and return
headers for rendering a listing. `SchedulerQueryExtensions`' `Get*` conveniences return bare keys or names in
one line and cannot page.

Shorthands that only saved the `new` are gone:

| 4.0 preview | 4.0 |
|---|---|
| `QueryJobs()` | `QueryJobs(new JobQuery())` |
| `QueryTriggers()` | `QueryTriggers(new TriggerQuery())` |
| `QueryFireInstances()` | `QueryFireInstances(new FireInstanceQuery())` |
| `QueryFireInstancesOfJob(jobKey)` | `QueryFireInstances(new FireInstanceQuery { Job = jobKey })` |
| `QueryTriggersInError()` | unchanged — it is a **preset**, not a synonym |

`QueryTriggersInError()` stays because it knows the filter. It pages like the member: `PagedQuery.DefaultTake`
items, with `HasMore` reporting the rest.

### Resetting the triggers that failed is one call each way

`ResetTriggerFromErrorState(TriggerKey)` and `ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey>)`
already existed. The group form is new:

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

It is still that pair of calls: two round trips, not one transaction, so a trigger that fails between them
waits for the next call. What a reset does is unchanged. `null` throws instead of meaning every group. It is an
`IScheduler` member, not an extension — see
[Resetting a group from the error state is an `IScheduler` member](#resetting-a-group-from-the-error-state-is-an-ischeduler-member)
for the one call site that has to change.

### `Exists` has a calendar overload

`IScheduler.Exists` and `IJobStore.Exists` take a calendar name as well as a `JobKey` or `TriggerKey`:

```diff
- bool exists = await scheduler.GetCalendar("holidays") is not null;
+ bool exists = await scheduler.Exists("holidays");
```

Unlike `GetCalendar`, it does not deserialize the stored calendar: `RAMJobStore` looks up the name without
cloning, and the ADO.NET store selects a constant instead of the calendar column. **No new `IDriverDelegate`
member**: it uses the existing `CalendarExists`. Over HTTP it is
`GET {ApiPath}/schedulers/{name}/calendars/{calendarName}/exists`, answering `{ "exists": … }` like the job and
trigger routes.

### A scheduler can be required rather than looked up

`ISchedulerFactory.LookupScheduler` answers `null` for an unknown name. When absence is a bug, call
`GetRequiredScheduler`:

```diff
- IScheduler scheduler = await factory.LookupScheduler("reporting")
-     ?? throw new InvalidOperationException("No scheduler named 'reporting'");
+ IScheduler scheduler = await factory.GetRequiredScheduler("reporting");
```

It throws `SchedulerNotFoundException`, a `SchedulerException` carrying the requested `SchedulerName`. It is an
extension method, so it also resolves on a concrete receiver such as `StandaloneSchedulerFactory`.
`LookupScheduler` is unchanged; use it when a missing scheduler is an answer, not a failure.

## Shapes that were examined and kept

These look inconsistent and are deliberate; nothing changed:

* **`ITrigger.GetTriggerBuilder()` is an interface member; `IJobDetail.GetJobBuilder()` is an extension.** A
  trigger's builder must use the trigger's own `TimeProvider`, which is not on `ITrigger`; a detail's builder
  needs only public state.
* **`TriggerDetailsUpdate` has five typed `WithMisfireInstruction` overloads and
  `WithMisfireInstructionCode(int)`.** The same number means a different policy in each schedule family, so the
  typed overloads let the store reject an update aimed at a trigger of another family. The untyped one is the
  only way to set a code on a custom `ITrigger`, which is in none of the five families.
* **`SchedulerContext` gets the read accessors, not `PutAsString`.** A `JobDataMap` write is change-tracked
  (which persists it after a `[PersistJobDataAfterExecution]` job) and affects equality; an extension cannot
  do that, and a context is process state no store writes back.
* **`FireInstanceQuery` names its trigger filters `TriggerGroup` and `TriggerName`.** `Group` and `Name` filter
  a result's own identity; a filter on something the result refers to carries that thing's name (`Job`,
  `CalendarName`, `SchedulerInstanceId`), and a firing's trigger is such a reference.
* **`Matchers` and the per-type factories both build matchers** — see
  [`Matchers` is the entry point](#matchers-is-the-entry-point-combinators-are-extensions). A factory on the
  type names its comparison; a root on `Matchers` takes it as a value.

## Trigger states are typed on the driver delegate

Eighteen `IDriverDelegate` members took a trigger state as a `string` (one of the `AdoConstants.State*`
constants), so a typo or a swapped `newState`/`oldState` compiled and matched no row. They take
`StoredTriggerState` now. It lives in **`Quartz.Extensibility`** because every store uses it: `RAMJobStore`
keeps its triggers in it (its private `InternalTriggerState` is gone), and a custom `IJobStore` can too.

The precedence that turns a stored state plus "is it executing" into the reported `TriggerState` is public:

```csharp
TriggerState reported = TriggerStateResolver.Resolve(stored, isExecuting);
```

`Resolve` applies `None > Error > Paused > Executing > Blocked > Complete > Normal`. Every built-in store
uses it, so a custom store that does too reports what the ADO store would.

**Nothing changes in the database.** The columns hold the same strings, converted at the delegate boundary.
`AdoConstants.State*` stays public, and so does the string mapping, `StoredTriggerStates`, in
`Quartz.Impl.AdoJobStore`. A 4.0 scheduler reads and writes rows a 3.x one wrote, so the two can share a
cluster during a rolling upgrade, under the calendar and execution-limit conditions in
[Operating a Cluster](operations.md#a-mixed-3-x-and-4-0-window).

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

Both directions are public static methods, for a custom delegate that binds the string itself:

```csharp
string stored = StoredTriggerStates.ToStoredValue(StoredTriggerState.PausedBlocked);   // "PAUSED_BLOCKED"
StoredTriggerState state = StoredTriggerStates.FromStoredValue(stored);
```

* `FromStoredValue` is lenient, as the store always was: an unrecognised value (from a third-party delegate, a
  migration or a hand-repaired row) reads as `Waiting`, reported as normal, and `null` reads as `Deleted`.
* `SelectTriggerState` returns `StoredTriggerState`, because its result goes straight into `AddTrigger` and
  `UpdateTrigger`.
* For a row whose state no Quartz version writes, `PauseTrigger` now pauses it, where it matched neither
  `WAITING` nor `ACQUIRED` and silently did nothing (listings always reported it `Normal`). Storing such a
  trigger back writes `WAITING`.
* The enum also replaces the string in `MisfiredTriggerUpdate.NewState`, `TriggerExecutionState.State` (and its
  constructor), and the protected `AdoJobStoreBase` members `AddTrigger`, `UpdateMisfiredTrigger` and
  `CheckBlockedState`, which returns `ValueTask<StoredTriggerState>`.

### The `…FromOtherStates` members take a collection

Three members hard-coded two or three old states. They take a set now; the predicate is built for its length,
with duplicates folded away.

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

An empty set throws `ArgumentException`. The parameter is not `params`, because the cancellation token comes
last.

### One term for a scheduler instance

These members carry the scheduler *instance id* (`quartz.scheduler.instanceId`), not the scheduler name, and
are named for it now, as `SchedulerStateRecord.SchedulerInstanceId` already was:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `FiredTriggerQuery.InstanceName` | `FiredTriggerQuery.InstanceId` |
| `InsertSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `UpdateSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `DeleteSchedulerState(conn, string instanceName, …)` | `instanceId` |
| `SelectSchedulerStateRecords(conn, string? instanceName, …)` | `instanceId` |

**Column names are unchanged**: `SCHED_NAME` holds the scheduler name, `INSTANCE_NAME` the instance id.
`TriggerPersistenceDelegateContext.SchedulerName` and `DriverDelegateContext.SchedulerName` (formerly
`DelegateInitializationArgs.InstanceName`) are the scheduler name — see
[The initialization seams are context records](#the-initialization-seams-are-context-records).

### Three parameter shapes were fixed

* **`IsJobCurrentlyExecuting(conn, JobKey jobKey, ct)`** — was `(string jobName, string jobGroup)`.
* **`SelectJobForTrigger(conn, key, loadHelper, bool loadJobType, ct)`** — `loadJobType` is required now. Its
  `true` default sat before the cancellation token, so a token passed positionally bound to it. Pass
  `loadJobType: true` for the previous default.
* **`UpdateTriggerPreferredNodeConditional(conn, key, PreferredNodeTransition transition, ct)`** — the
  compare-and-swap took `(node, auto, expectedNode, expectedAuto)`. The record names both sides and carries
  [`PreferredNode`](#the-preferred-node-is-a-value) values instead of the raw column pair:

  ```csharp
  await Delegate.UpdateTriggerPreferredNodeConditional(conn, trigger.Key, new PreferredNodeTransition
  {
      Expected = PreferredNode.Auto,
      New = PreferredNode.For(InstanceId)
  }, cancellationToken);
  ```

### The matcher-based selects stay, deliberately

`SelectTriggerGroupNames(matcher)`, `SelectJobKeysInGroup`, `SelectTriggerKeysInGroup` and
`SelectTriggerKeysForJob` stay beside the paged `SelectJobHeaders` / `SelectTriggerHeaders` / `SelectTriggerGroups(query)`. They serve
pause/resume and removal, which must move every matching row under one lock, so they are not paged. Their doc
comments say so.

## The ADO.NET store is a store, not a base class

`AdoJobStoreBase`, `LocalTransactionJobStore`, `ExternalTransactionJobStore` and `AdoJobStoreDependencies` are
internal. The store works as before: `quartz.jobStore.type = Quartz.Impl.AdoJobStore.LocalTransactionJobStore,
Quartz` builds the same store, as do the `JobStoreTX` and `JobStoreCMT` spellings the bridge translates. Only
deriving is gone, which removes 162 members and about a hundred `protected` hooks from the contract. Those hooks
were never a real seam: each `protected` member below the two abstract ones is the connection-taking twin of a
public member (`AddJob(conn, …)` beside `AddJob(job, …)`) that does the work after the public one takes the
lock, so overriding it changed half an operation.

**For a relational database without a shipped dialect, write a driver delegate.** `IDriverDelegate`,
`StdAdoDelegate`, the six shipped dialects, `ITriggerPersistenceDelegate`, `ILockHandler`, `IDbProvider`,
`AdoConstants`, `ConnectionAndTransactionHolder` and the records they name (`TriggerAcquisitionCriteria`,
`TriggerAcquireResult`, `MisfiredTriggerBatch`, `SchedulerStateRecord` and the rest) stay public and unchanged.
See [A Driver Delegate for a New Database](how-tos/dialect-delegate.md).

**If you derived from `JobStoreSupport`, `JobStoreTX` or `JobStoreCMT` on 3.x:**

| What the override did | What to do now |
|---|---|
| Narrowed acquisition — `CreateAcquisitionCriteria`, `MaxCount`, `ExcludedJobTypeNames` | Derive from `DelegatingJobStore` and rewrite the request (below) |
| Added logging, metrics, tenant routing, fault injection | Derive from `DelegatingJobStore`, whose members are all `virtual`; see [A Job Store of Your Own](how-tos/custom-job-store.md) |
| Classified one more of a driver's failures as retryable | `AdoJobStoreOptions.IsTransient`, a predicate consulted before the built-in list; see [What counts as transient](operations.md#what-counts-as-transient) |
| Changed *how a transaction is managed* | Implement `IJobStore`, which stays public; if the shipped stores nearly fit, [open an issue](https://github.com/quartznet/quartznet/issues) |

To narrow acquisition, call `base.AcquireNextTriggers(request with { MaxCount = …, ExcludedJobTypeNames = … })`.
The store copies both fields into its delegate criteria, so the exclusion is still applied in SQL.

`RecoverMisfiredJobsResult` went too; it was only the return type of a `protected` method.

## The ADO.NET job stores are named for whose transaction they use

`JobStoreTX` and `JobStoreCMT` were named after a Java EE distinction, and neither name said which one commits.
They are renamed:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `Quartz.Impl.AdoJobStore.JobStoreTX` | `Quartz.Impl.AdoJobStore.LocalTransactionJobStore` |
| `Quartz.Impl.AdoJobStore.JobStoreCMT` | `Quartz.Impl.AdoJobStore.ExternalTransactionJobStore` |

* `LocalTransactionJobStore`, the default, begins each operation's transaction and commits or rolls it back.
* `ExternalTransactionJobStore` runs inside a transaction somebody else owns and never commits or rolls back.

The abstract bases drop the Java/Spring `*Support` suffix: `JobStoreSupport` is **`AdoJobStoreBase`**, and
`SimplePropertiesTriggerPersistenceDelegateSupport`, the base for a custom trigger type's persistence delegate,
is **`SimplePropertiesTriggerPersistenceDelegateBase`**. Abstract types never appear in configuration or as
stored `$type` values, so there is no fallback: change the base list and recompile.

**Configuration naming either old store keeps working**, through the same fallback as the
[renamed namespaces](#quartz-spi-and-quartz-simpl-were-renamed), with a warning naming the new spelling:

```text
# both of these resolve, the first with a warning
quartz.jobStore.type = Quartz.Impl.AdoJobStore.JobStoreTX, Quartz
quartz.jobStore.type = Quartz.Impl.AdoJobStore.LocalTransactionJobStore, Quartz
```

**In code, a call selects the store**, because [both stores are internal](#the-ado-net-store-is-a-store-not-a-base-class).
`UsePersistentStore()` gives the local-transaction store; `UseAmbientTransactions()` on the store builder
selects the other:

```diff
- q.UsePersistentStore<JobStoreCMT>(store => store.UseSqlServer(connectionString));
+ q.UsePersistentStore(store => store
+     .UseSqlServer(connectionString)
+     .UseAmbientTransactions());
```

`UsePersistentStore<T>(configure)` is unchanged, for a store of your **own** that the container can construct.
A `typeof` or subclass of a shipped store has no replacement; the table above says what an override becomes.

### The vocabulary follows

The "non-managed TX" wording went with the old names. These are protected members of an internal type, so this
matters only when porting a 3.x subclass — see
[The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class):

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `GetNonManagedTXConnection` | `GetLocalTransactionConnection` |
| `ExecuteInNonManagedTXLock` | `ExecuteInLocalTransactionLock` |
| `RetryExecuteInNonManagedTXLock` | `RetryExecuteInLocalTransactionLock` |

`ExternalTransactionJobStore.OpenConnection` is `AdoJobStoreOptions.OpenConnection`. The store property could
only be set by downcasting the built store, with no guarantee it landed before `Initialize` ran:

```diff
- ((ExternalTransactionJobStore) store).OpenConnection = true;
+ services.AddQuartz(q => q.UsePersistentStore(store => store
+     .UseAmbientTransactions()
+     .ConfigureStore(options => options.OpenConnection = true)));
```

`Quartz:JobStore:OpenConnection` binds like every `AdoJobStoreOptions` member, and the flat
`quartz.jobStore.openConnection` of a 3.x file is read too.

## Nine `Execute…Lock` overloads became four members

::: warning
`AdoJobStoreBase` is internal, so none of this is a seam — see
[The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class). It is listed
for reading a 3.x `JobStoreSupport` subclass during a port.
:::

Nine overlapping ways to run a callback under a lock, three of which adapted a `void` callback by returning an
always-`null` `object`, are replaced by optional parameters:

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `ExecuteWithoutLock<T>(txCallback, ct)` | unchanged |
| `abstract ExecuteInLock<T>(lockName, txCallback, ct)` | `ExecuteInLock<T>(SchedulerLock? lockKind, txCallback, ct)` |
| `ExecuteInLock(lockName, txCallback, ct)` → `ValueTask<object>` | `ExecuteInLock(SchedulerLock? lockKind, txCallback, ct)` → `ValueTask` |
| `ExecuteInNonManagedTXLock` ×4 | `ExecuteInLocalTransactionLock<T>(SchedulerLock? lockKind, txCallback, txValidator = null, requestorId = null, ct)` plus one `ValueTask`-returning convenience |
| `RetryExecuteInNonManagedTXLock` ×2 | `RetryExecuteInLocalTransactionLock<T>(SchedulerLock? lockKind, txCallback, requestorId = null, ct)` plus one `ValueTask`-returning convenience |

A cancellation token passed positionally after the validator must now be named
(`cancellationToken: cancellationToken`). `RecoverJobs(CancellationToken)` returns `ValueTask`; its old `bool`
was always `true`.

## Locks are a `SchedulerLock`, not a string

`ILockHandler` takes the lock as `Quartz.Impl.AdoJobStore.SchedulerLock` (`TriggerAccess`, `StateAccess`)
instead of a `string` that had to be `"TRIGGER_ACCESS"` or `"STATE_ACCESS"`:

```diff
- ValueTask<bool> ObtainLock(Guid requestorId, ConnectionAndTransactionHolder? conn, string lockName, CancellationToken ct = default);
- ValueTask ReleaseLock(Guid requestorId, string lockName, CancellationToken ct = default);
+ ValueTask<bool> AcquireLock(Guid requestorId, ConnectionAndTransactionHolder? conn, SchedulerLock lockKind, CancellationToken ct = default);
+ ValueTask ReleaseLock(Guid requestorId, SchedulerLock lockKind, CancellationToken ct = default);
```

`AdoJobStoreBase.LockTriggerAccess` and `LockStateAccess` are gone. **Nothing changes in the database**:
`LOCK_NAME` still holds `TRIGGER_ACCESS` and `STATE_ACCESS`, so 4.0 and 3.x nodes contend for the same rows, and
`Quartz.Extensions.Redis` keys keep their `…:TRIGGER_ACCESS` spelling. `DbLockHandler.ExecuteSql` still receives
the stored name as a `string`, because it is bound into the statement.

## The job store configuration is read-only, and no longer a public currency

About twenty `AdoJobStoreBase` properties duplicated `AdoJobStoreOptions` and `QuartzSchedulerOptions` with a
public setter that, once the store had started, mostly did nothing. They are get-only and no longer public:

* **`protected`** (read by a derived store while it works): `AcquireTriggersWithinLock`, `CanUseProperties`,
  `ClusterCheckinMisfireThreshold`, `DataSource`, `DoubleCheckLockMisfireHandler`, `LockOnInsert`,
  `MaxMisfiresToHandleAtATime`, `MaxTransientRetries`, `ObjectSerializer`, `SchemaProvisioning`,
  `SelectWithLockSql`, `TablePrefix`, `TransientRetryInterval`, `TransactionIsolationLevel`, `UseDbLocks`.
* **internal** (read only by the store's cluster and misfire machinery): `AcceptEnlistedTransactions`,
  `ClusterCheckinInterval`, `DbRetryInterval`, `InstanceId`, `InstanceName`, `UseBackgroundThreads`,
  `MisfireHandlerFrequency`, `RetryableActionErrorLogThreshold`.
* **public**: `Clustered`, `SupportsPersistence` and `EstimatedTimeToReleaseAndAcquireTrigger`, because they
  are `IJobStore` members.

Since [the store itself is internal](#the-ado-net-store-is-a-store-not-a-base-class), the `protected` list is a
porting note, not a seam.

**`EstimatedTimeToReleaseAndAcquireTrigger` is a `TimeSpan`**, not a `long` of milliseconds, on `IJobStore` and
so on every store. The scheduler thread subtracts it from its sleep. A store that returned `70` returns
`TimeSpan.FromMilliseconds(70)`, not `TimeSpan.FromTicks`.

Read settings from `IOptions<AdoJobStoreOptions>` instead of the store, and set them on the store builder:

```diff
- var store = new JobStoreTX(...) { Clustered = true, MaxTransientRetries = 5 };
+ services.AddQuartz(q => q.UsePersistentStore(store => store.ConfigureStore(options =>
+ {
+     options.Clustered = true;
+     options.MaxTransientRetries = 5;
+ })));
```

`MisfireThreshold` keeps a **public getter** on `RAMJobStore` (and on the ADO.NET store while that type was
public), because a wrapping store reads it on every misfire pass. Its setter is `internal`; set it with
`UsePersistentStore(store => store.ConfigureStore(o => o.MisfireThreshold = …))` or
`UseInMemoryStore(o => o.MisfireThreshold = …)`.

Also gone or hidden:

* `DriverDelegateType` — removed; the delegate is injected, not loaded by type name.
* `DontSetAutoCommitFalse` and `AdoJobStoreOptions.DontSetAutoCommitFalse` — removed; no code read them and no
  configuration key set them.
* `LastCheckin` — internal; it is cluster check-in bookkeeping.
* `LogWarnIfNonZero` — removed; its callers raise source-generated warning events, as
  [Every message carries an event id](#every-message-carries-an-event-id) describes.
* The `[TimeSpanParseRule]` attributes on these properties — removed; this store no longer takes settings as
  strings.

### `AdoJobStoreBase`'s overridable surface is a decision now

::: warning
This describes an intermediate state: the curation below happened, then the whole hierarchy went internal, so
none of it is a seam any more. See
[The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) for what
replaced each. It records what the store does *not* offer.
:::

The base store had 56 `protected virtual` members, which 4.0 would have frozen as contract. They were cut to
what derived stores used:

* **Lifecycle**: `Initialize` and `Shutdown` (the two shipped stores override both).
* **Connections and transactions**: `GetConnection`, `GetLocalTransactionConnection` and `ExecuteInLock<T>`
  (both abstract), and `IsTransient` for provider-specific transient errors. An ambient-transaction store
  builds on these.
* **Acquisition**: `AcquireNextTriggers`, `GetFiredTriggerRecordId`, and `CreateAcquisitionCriteria` (issue
  #2238), which builds the `TriggerAcquisitionCriteria` for the delegate; a derived store narrowed acquisition
  by returning `base.CreateAcquisitionCriteria(request) with { … }`.

Everything else became non-virtual: per-entity add/get/delete, the pause/resume walkers, the fire path, cluster
check-in and recovery, and connection cleanup. They were virtual only because the Java port made everything
virtual. The seven conn-taking `PauseTrigger`/`PauseTriggerGroup`/`PauseAll`/`ResumeTrigger`/
`ResumeTriggerGroups`/`ResumeAll`/`RecoverMisfiredJobs` overloads are `protected`, since only the store can
obtain their `ConnectionAndTransactionHolder`. The dialect seam (statement text, paging, parameter binding)
belongs to `StdAdoDelegate`.

## The semaphores are lock handlers

Every type in the semaphore family is named `LockHandler` now, like the builder method (`UseLockHandler`), the
configuration key (`quartz.jobStore.lockHandler.type`) and the how-to. It is a mutual-exclusion lock over a
named row, not a counted permit.

| 3.x | 4.0 |
|-----|-----|
| `ISemaphore` | `ILockHandler` |
| `DBSemaphore` | `DbLockHandler` |
| `StdRowLockSemaphore` | `SelectForUpdateLockHandler` |
| `PostgreSQLRowLockSemaphore` | `PostgreSqlSelectForUpdateLockHandler` |
| `UpdateLockRowSemaphore` | `UpdateRowLockHandler` |
| `UpdateLockRowSemaphoreMOT` | `SqlServerMemoryOptimizedUpdateRowLockHandler` |
| `SimpleSemaphore` | `InProcessLockHandler` (internal) |
| — | `SqliteLockHandler` (internal) |
| `RedisSemaphore` | `RedisLockHandler` |
| — | `LockHandlerContext` |
| `ISemaphore.ObtainLock` | `ILockHandler.AcquireLock` |

`ReleaseLock` and `RequiresConnection` keep their names. A `quartz.jobStore.lockHandler.type` naming an old
type still resolves, with a warning, including the 4.0 pre-release spellings in
[Appendix: if you ran a 4.0 pre-release](#appendix-if-you-ran-a-4-0-pre-release).

The database handlers are named for the SQL they issue; 3.x's `StdRowLockSemaphore` and
`UpdateLockRowSemaphore` transposed the same two words, and `MOT` was never expanded.

* The public static SQL fields are `protected const`. `SelectForUpdateLockHandler.SelectForLock` / `.InsertLock`
  keep their names; `UpdateLockRowSemaphore.SqlUpdateForLock` / `.SqlInsertLock` are
  `UpdateRowLockHandler.UpdateForLock` / `.InsertLock`.
* `DbLockHandler.Sql` is `LockSql`, pairing with `InsertSql`. Both are get-only and set through the
  constructor: as `protected` setters they let a subclass swap a statement after the table prefix was folded
  in, so one lock's select and insert could name different tables. Pass your own insert statement up:

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

`ITablePrefixAware` is gone. Its get/set pair, which the store set after construction, made even a handler with
no SQL table (the Redis one) carry a dead `TablePrefix`. `ILockHandler` has one initialization seam instead:

```csharp
public interface ILockHandler
{
    void Initialize(LockHandlerContext context)
    {
    }

    // AcquireLock (was ObtainLock) / ReleaseLock / RequiresConnection otherwise unchanged
}
```

* `LockHandlerContext` carries `SchedulerName`, `InstanceId`, `TablePrefix`, `TimeProvider` (the clock to back
  off on between attempts) and `CommandTimeout` (how long its statements may run). See
  [LockHandlerContext](how-tos/lock-handler.md#lockhandlercontext).
* The job store calls `Initialize` once, before first use, whether it built the handler or the container or
  configuration supplied it. The default does nothing.
* `DbLockHandler` overrides it to re-expand its statements with the table prefix and rebuild its accessor with
  the timeout. Its `TablePrefix` and `SchedulerName` are read-only, and it exposes the clock as a
  `protected TimeProvider`.
* Both shipped row-lock handlers back off on that clock, not wall time (`Task.Delay(TimeSpan, CancellationToken)`),
  so their retries are testable.
* `UpdateRowLockHandler` gained a `RetryPeriod`. Its backoff was a fixed `TimeSpan.FromSeconds(1)`, ignoring
  `quartz.jobStore.lockHandler.retryPeriod`, which `SelectForUpdateLockHandler` honoured.
* `SelectForUpdateLockHandler.MaxRetry` and `.RetryPeriod` are `init`-only, fixed for the handler's life. The
  `quartz.jobStore.lockHandler.maxRetry` / `.retryPeriod` keys still set them, because the property bridge
  writes by reflection.

```diff
- var lockHandler = new StdRowLockSemaphore(dbProvider);
- lockHandler.MaxRetry = 5;
+ var lockHandler = new SelectForUpdateLockHandler(dbProvider) { MaxRetry = 5 };
```

A custom handler that implemented `ITablePrefixAware` implements `Initialize` instead:

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

The keys that reached the removed setters are rejected, with advice naming this seam:
`quartz.jobStore.lockHandler.tablePrefix`, `quartz.jobStore.lockHandler.schedName` (the 3.x spelling, from
`ITablePrefixAware.SchedName`), and `quartz.jobStore.lockHandler.schedulerName` (not reported as a typo). The
handler gets the store's own `quartz.jobStore.tablePrefix`, so lock rows follow the store's prefix.

## A cancelled acquire throws, and `false` means only re-entry

`ILockHandler.AcquireLock` answers `true` when this call took the lock and must release it, and `false` when the
caller already held it. That was the 3.x rule too. Now explicit: **`false` means only re-entry**, so an acquire
that did not take the lock throws — `LockException` when refused, `OperationCanceledException` when the token
fired.

`SimpleSemaphore.ObtainLock` and `RedisSemaphore.ObtainLock` answered `false` on cancellation, which the store
reads as *already held, do not release*: it would run the operation unlocked and release nothing. Their 4.0
counterparts throw. Shipped configurations were protected only by statement order (both answer `false` to
`RequiresConnection`, and the connection open that follows sees the same token); a custom handler is not.
Rethrow, after giving back anything taken:

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

## A lock handler is told when to close what it opened

`ILockHandler` has a second default interface member:

```csharp
ValueTask Shutdown(CancellationToken cancellationToken = default) => default;
```

The job store calls it at the end of its own `Shutdown`, after the misfire handler, the cluster manager and the
database provider have stopped, so no acquire is in flight. **An existing handler need not implement it**; the
default does nothing, which suits every row-lock handler. An exception from it is logged, and shutdown
completes.

`RedisLockHandler` uses it to close the `ConnectionMultiplexer` it opens on first lock, which used to stay open
with its heartbeat, once per scheduler, for the life of the process (#3639). The two in-process handlers close
their `SemaphoreSlim` gates through it.

```csharp
public sealed class ConsulLockHandler : ILockHandler
{
    private readonly HttpClient consul = new();

    public ValueTask Shutdown(CancellationToken cancellationToken = default)
    {
        consul.Dispose();
        return default;
    }

    // AcquireLock / ReleaseLock / RequiresConnection as before
}
```

## A job store of your own can join your transaction

::: warning
The `GetEnlistedConnection` half of this section is an intermediate state, like
[`AdoJobStoreBase`'s overridable surface](#adojobstorebase-s-overridable-surface-is-a-decision-now): it was
opened while the store hierarchy was public, which it no longer is. What remains is the public
`ConnectionAndTransactionHolder` shape below, for a store written against `IJobStore`. Enlisting from the
application is unaffected: `AcceptEnlistedTransactions`, `IScheduler.EnlistTransaction` and `EnlistConnection`
are public — see [Joining an existing transaction](tutorial/job-stores.md#joining-an-existing-transaction).
:::

`AdoJobStoreBase`, public at the time, kept enlisted-transaction support `private protected`, so a store in
another assembly opened its own connection while the caller believed the work was in their transaction.
`GetEnlistedConnection` became `protected`. A `GetLocalTransactionConnection` override tried it first, and got
`null` when enlisted transactions are not accepted or nothing is enlisted:

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

It checks that the transaction is alive and current and the provider matches, opens the connection if needed,
and books it for the operation so two concurrent scheduler calls cannot share it. `CleanupConnection` returns
the booking.

`ConnectionAndTransactionHolder` has a public constructor,
`(DbConnection connection, DbTransaction? transaction, bool ownsResources)`, and a public `OwnsResources`, for a
store that borrows a connection. A holder that owns nothing leaves it alone: `Commit`, `Rollback`, `Close` and
`Dispose` do nothing to it. `Commit(bool)` and `Rollback(bool)` are internal, because the job store decides when
to commit; `Close` stays public so a borrower can give the connection back.

## The driver delegate speaks in records

The six types `IDriverDelegate` takes or returns were mutable classes, some with `string` pairs where a key
belongs and one with unassigned non-nullable properties. They are records now:

| Type | What changed |
|---|---|
| `FiredTriggerRecord` | `sealed record`, `[Serializable]` dropped, `FireInstanceState` is a `StoredTriggerState` |
| `RecoverMisfiredJobsResult` | internal now; see [The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) |
| `DelegateInitializationArgs` | Renamed `DriverDelegateContext`; `sealed record` with `required` / `init` members, and `InstanceName` is `SchedulerName` — see [The initialization seams are context records](#the-initialization-seams-are-context-records) |
| `TriggerAcquireResult` | carries a `TriggerKey` instead of `TriggerName` + `TriggerGroup` |
| `TriggerStatus` | replaced by `StoredTriggerHeader`, returned by `SelectTriggerHeader` |
| `SchedulerStateRecord` | `sealed record` with a positional constructor and `init`-only members; `[Serializable]` dropped; properties no longer `virtual` |

* `SchedulerStateRecord`'s constructor is
  `(string SchedulerInstanceId, DateTimeOffset CheckinTimestamp, TimeSpan CheckinInterval)`. Replace
  `new SchedulerStateRecord { … }` with it; all three values come from one row, so none is optional.
* `FiredTriggerRecord`: `FireInstanceId`, `FireInstanceState`, `TriggerKey` and `SchedulerInstanceId` are
  `required` and non-nullable. `JobKey` stays nullable, because an ACQUIRED row is written before the job is
  loaded. Stale-acquired and cluster recovery no longer compare raw `AdoConstants.State*` strings.

`StoredTriggerHeader` is the storage-side counterpart of `TriggerHeader`:

```diff
- TriggerStatus? status = await Delegate.SelectTriggerStatus(conn, triggerKey, cancellationToken);
- bool blocked = AdoConstants.StatePausedBlocked == status.Status;
+ StoredTriggerHeader? status = await Delegate.SelectTriggerHeader(conn, triggerKey, cancellationToken);
+ bool blocked = status.State == StoredTriggerState.PausedBlocked;
```

It uses `StoredTriggerState`, not the reported `TriggerState`, because resuming must tell `PausedBlocked` from
`Paused`.

`FiredTriggerQuery` stays unpaged, as its doc comment now says: FIRED_TRIGGERS holds one row per firing in
flight, and every caller is a maintenance pass (recovery, cluster failover, blocked-state checks) that must see
all of them.

## The initialization seams are context records

The ADO.NET store initializes three things after construction. All three take a context record now, and all
call the scheduler's name `SchedulerName`:

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

* `DelegateInitializationArgs.InstanceName` held the scheduler name, hence `SchedulerName`, matching
  `LockHandlerContext.SchedulerName` (see [One term for a scheduler instance](#one-term-for-a-scheduler-instance)).
  `InstanceId` keeps its name and meaning: the node's identity in the cluster.
* Neither delegate seam has a default implementation, unlike `ILockHandler.Initialize`: a delegate without its
  context has no table prefix, provider or accessor, so a default would only move the failure from startup to
  the first statement.
* Both stay two-phase rather than constructor-injected, because `InstanceId` may be *generated*, and the store
  is built before the id generator runs.

## `ValidateSchema` is part of `IDriverDelegate`

Startup schema validation was a `StdAdoDelegate` method the store reached by type test, so any other delegate
silently skipped the check `quartz.jobStore.performSchemaValidation` asked for. It is an interface member now:

```csharp
ValueTask<int> ValidateSchema(ConnectionAndTransactionHolder conn, CancellationToken cancellationToken = default);
```

`CreateSchema` sits beside it, for `SchemaProvisioning.CreateIfMissing` — see
[`PerformSchemaValidation` became `SchemaProvisioning`](#performschemavalidation-became-schemaprovisioning-which-has-a-third-position).
A `StdAdoDelegate` subclass inherits the implementation and can extend it to its own tables. A delegate written
against the interface must implement it; returning `0` without checking restores the old skip.

Three more members moved from `StdAdoDelegate` onto the interface for the same reason:

| Member | What the store does with it |
|---|---|
| `RepinTriggersFromDeadNode(conn, oldPreferredNode, newPreferredNode, ct)` | Steals a dead node's pinned triggers during cluster recovery |
| `UpdateMisfireOriginalFireTime(conn, triggerKey, fireTime, ct)` | Records the fire time a misfire displaced |
| `ClearMisfireOriginalFireTime(conn, triggerKey, ct)` | Clears it once the trigger fires normally again |

All three are **abstract** on `IDriverDelegate`, not default interface members: a silent no-op would lose node
affinity on failover or report the wrong original fire time. A `StdAdoDelegate` subclass inherits them.

## The optional columns are required, so the probes are gone

3.x checked at startup whether `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP` and `PREFERRED_NODE` existed and
switched the matching feature off if not. **4.x requires all of them**, and the probe members are gone from
`StdAdoDelegate`:

| Removed | What it did |
|---|---|
| `HasMisfireOriginalFireTimeColumn`, `HasExecutionGroupColumn`, `HasPreferredNodeColumn` | Reported the probe's result |
| `SupportsMisfireOriginalFireTimeColumn`, `SupportsExecutionGroupColumn`, `SupportsPreferredNodeColumn` | Ran the probe, swallowing the "no such column" error |
| `VerifyTriggersTableReachable` | Told a missing column from a momentarily unreachable database |

**The schema migration is mandatory.** Run `schema_30_to_40_upgrade_<dialect>.sql` from
[database/migrations/4.0/](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) before
pointing 4.x at a 3.x database; [Database Schema Migration](#database-schema-migration) lists the columns and
what each script does. Skipping it does not degrade gracefully:
[`ValidateSchema`](#validateschema-is-part-of-idriverdelegate) probes every column 4.x added, so startup
refuses an unmigrated schema and names the missing column and the script. With `SchemaProvisioning.None`,
the first statement naming a missing column fails with a provider error instead.

A delegate that overrode a probe (to hard-code `true`, say) deletes the override; the base no longer declares
it.

### The three extra acquisition SQL hooks went with them

3.x had four acquisition statements (plain, with `EXECUTION_GROUP`, with `PREFERRED_NODE`, with both), chosen
at run time from the probes. Each had a `protected virtual` hook that all six dialect delegates overrode to add
their row limit. Three hooks are gone from `StdAdoDelegate` and from `FirebirdDelegate`, `MySQLDelegate`,
`OracleDelegate`, `PostgreSQLDelegate`, `SQLiteDelegate` and `SqlServerDelegate`:

* `GetSelectNextTriggerToAcquireWithExecutionGroupSql(int maxCount)`
* `GetSelectNextTriggerToAcquireWithPreferredNodeSql(int maxCount)`
* `GetSelectNextTriggerToAcquireWithPreferredNodeOnlySql(int maxCount)`

The one statement left, `StdAdoConstants.SqlSelectNextTriggerToAcquire`, always projects `EXECUTION_GROUP` and
always has the preferred-node filter. The remaining hook takes a new parameter:

```csharp
protected virtual string GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)
```

* `TriggerAcquisitionSqlShape` carries what changes the statement text: `MaxCount`, and `ExcludedJobTypeBucket`
  for the job-type exclusion clause. A new acquisition dimension becomes a property, not a parameter.
* **Delete the three removed overrides** from a dialect delegate of your own. If the remaining override only
  adds a row limit, delete it too — see [Row limiting is a slot, not a splice](#row-limiting-is-a-slot-not-a-splice).
* The protected `AddPreferredNodeParameters(cmd, liveNodeCutoff)` binds the node-affinity parameters, so a
  rewritten statement need not know their names or order. The cutoff is a `DateTimeOffset`; the binder
  converts it to the stored ticks.

### Row limiting is a slot, not a splice

Trigger acquisition and the misfire scan have row-limited forms. Each dialect used to edit the finished SQL:
`SqlServerDelegate` cut the first six characters and pasted `SELECT TOP n` back on, `OracleDelegate` wrapped
it in a `rownum` filter, and the rest appended `LIMIT n` or `ROWS n`. That relied on the statement starting
with exactly `SELECT`, and repeated a `count == -1` test per statement per dialect. A dialect now says once
where its clause goes:

```csharp
protected virtual SqlRowLimit GetRowLimit(int count)
```

| `SqlRowLimit` (new) | Dialect |
|---|---|
| `InProjection("TOP", count)` | SQL Server |
| `AtStatementEnd("LIMIT", count)` | PostgreSQL, MySQL, SQLite |
| `AtStatementEnd("ROWS", count)` | Firebird |
| `InEnclosingSelect("rownum", count)` | Oracle |
| `Unlimited` | a database that cannot limit rows; `StdAdoDelegate`'s default |

`count` is never the `-1` sentinel; that becomes `Unlimited` before the call.

`GetSelectNextTriggerToAcquireSql(shape)` and `GetSelectMisfiredTriggersToRecoverSql(count)` stay
`protected virtual` for what a row limit cannot say. `MySQLDelegate` overrides both for its `FORCE INDEX`
hint; the other five dialect delegates override neither. **If your dialect overrides either one only to add a
row limit, replace both overrides with one `GetRowLimit`.** An existing override that appends its own clause
still works.

Two shipped statements changed:

* `MySQLDelegate`'s misfire scan keeps its `FORCE INDEX` hint for an unlimited sweep too. It lost it there
  before, because the hint and the row limit shared one early return.
* **Both of `MySQLDelegate`'s misfire hints name `IDX_…_T_NFT_ST`, the acquisition index, not
  `IDX_…_T_NFT_ST_MISFIRE`.** The sweep and its counting peek filter `SCHED_NAME` and `TRIGGER_STATE` by
  equality, range on `NEXT_FIRE_TIME`, order by `NEXT_FIRE_TIME ASC, PRIORITY DESC` and check `MISFIRE_INSTR`
  as a residual, which is the acquisition index's shape. The misfire index,
  `(SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)`, compares its second column with `<>`, which
  stops the seek. Other dialects' optimizers already chose the acquisition index; MySQL's hint prevented it. On
  a 100,000-trigger table with a 5,000-row backlog, the sweep went from 15,561 buffer pool reads to 129, and the
  counting peek, which runs on every misfire-handler pass, from 4,594 to 8
  ([#3608](https://github.com/quartznet/quartznet/issues/3608)). With no reader left on any dialect,
  `IDX_…_T_NFT_ST_MISFIRE` is no longer created —
  [The misfire index is dropped](#the-misfire-index-is-dropped-optional).

## The connection manager is gone

`IDbConnectionManager`, `DbConnectionManager` and `DBConnectionManager.Instance` are removed, with no
replacement type. The container is the registry of `IDbProvider`s now: a scheduler's provider is registered
under the scheduler's name as service key, and the job store gets the one it was built with. (In the
process-wide manager, two schedulers whose data sources shared a name overwrote each other.)

**To register a provider of your own**, configure the store. The
`quartz.dataSource.<name>.connectionProvider.type` key lands in `UseConnectionProvider` too:

```diff
- DBConnectionManager.Instance.AddConnectionProvider("default", new MyDbProvider());
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider<MyDbProvider>()));
```

```diff
- DBConnectionManager.Instance.AddConnectionProvider("default", myProvider);
+ services.AddQuartz(q => q.UsePersistentStore(store => store.UseConnectionProvider(_ => myProvider)));
```

It replaces the provider the database choice registered, in either order (`UseSqlServer` before or after
`UseConnectionProvider`), belongs to this scheduler alone, and names the data source, so no `UseDataSource`
call is needed.

**To read a provider back**, resolve it: unkeyed for the default scheduler, keyed by name for a named one:

```diff
- var provider = DBConnectionManager.Instance.GetConnectionProvider("default");
+ var provider = serviceProvider.GetRequiredService<IDbProvider>();
+ var reporting = serviceProvider.GetRequiredKeyedService<IDbProvider>("reporting");
```

| `DBConnectionManager` | Replacement |
|---|---|
| `GetConnection(name)` | `provider.CreateConnection()` |
| `GetDbMetadata(name)` | `provider.Metadata` |
| `Shutdown(name)` | `provider.Shutdown()` |

`IDbProvider` is constructor-shaped: `Initialize()` is gone (every implementation resolved its driver in its
constructor) and `ConnectionString` is get-only. A custom `IDbProvider` deletes its empty `Initialize` and its
`ConnectionString` setter.

## The isolation level is an isolation level

`TxIsolationLevelSerializable` was a `bool`, so `Snapshot` (usual for SQL Server; it reads without blocking
writers), `RepeatableRead` and a deliberate `ReadUncommitted` could not be configured.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `AdoJobStoreOptions.TxIsolationLevelSerializable = true` | `AdoJobStoreOptions.TransactionIsolationLevel = IsolationLevel.Serializable` |
| `TxIsolationLevelSerializable = false`, or unset | leave `TransactionIsolationLevel` unset |
| — | `TransactionIsolationLevel = IsolationLevel.Snapshot`, and every other `System.Data.IsolationLevel` |

```diff
- store.Configure(options => options.TxIsolationLevelSerializable = true);
+ store.ConfigureStore(options => options.TransactionIsolationLevel = IsolationLevel.Serializable);
```

* `quartz.jobStore.txIsolationLevelSerializable` is still read: `true` becomes `Serializable`, and `false` leaves
  the level unset rather than setting `ReadCommitted`.
* The typed configuration binds the enum by name: `"Quartz:JobStore:TransactionIsolationLevel": "Snapshot"`.
* Unset means `ReadCommitted`, Quartz's default, not the provider's (MySQL's is repeatable read), so the store
  behaves the same on every database.
* Unchanged: SQLite is always `Serializable`, because concurrent SQLite transactions at a lower level fail with
  "database is locked".
* Unchanged: the level applies only to connections the job store opens. An operation on a connection the
  application enlisted uses its transaction's level, and the store warns about that at startup.

## `RAMJobStore` is sealed

`RAMJobStore` is `sealed`, its `virtual`s are gone, and `GetFiredTriggerRecordId` is private. Its methods hold
its lock, update its indexes in a fixed order and notify listeners after releasing the lock, none of which an
override could be asked to keep. To add behaviour, wrap it in the new `Quartz.Impl.DelegatingJobStore`:

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

`UsePersistentStore<TStore>()` and `quartz.jobStore.type` take the wrapper as they took the subclass; the
`Quartz.Examples.AspNetCore` sample's `CustomJobStore` shows the whole shape.

`MisfireThreshold` is readable, as on `AdoJobStoreBase`, because it is read on every misfire pass; its setter
is `internal`. Set it with `UseInMemoryStore(o => o.MisfireThreshold = …)`.

### `DelegatingJobStore` decorates a store

`Quartz.Impl.DelegatingJobStore` is the store counterpart of `DelegatingScheduler`: a `public class` that takes
the store to wrap in its constructor, forwards all sixty-odd `IJobStore` members to it, and makes each `virtual`,
so a derived store overrides only what it changes. Derived types reach the wrapped store as `InnerJobStore`.
Use it for logging, metrics, tenant routing or fault injection, including around a sealed store such as
`RAMJobStore`. A store that keeps data somewhere new implements `IJobStore` directly.

Every `DelegatingScheduler` member is `virtual` now too. A decorator that shadowed members with `new` compiled,
but did not intercept calls made through `IScheduler`.

Both types declare every **default interface member** of the interface they forward. An undeclared one runs the
interface's default body *on the forwarder*: `ResetTriggersFromErrorState(GroupMatcher<TriggerKey>, CancellationToken)`
did during 4.0's development, so the inner scheduler got a `QueryTriggers` and a second reset instead of one
call. `DelegatingForwardingTest` sweeps both types with `GetInterfaceMap` and no exemption list. When you write
a decorator: a default interface member is not part of the implementing class, so it is callable only through
an interface-typed reference unless the class declares it.

## Jobs take a CancellationToken

`IJob.Execute` takes the cancellation token as a parameter:

```diff
- public async ValueTask Execute(IJobExecutionContext context)
+ public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
```

It is the *same* token as `IJobExecutionContext.CancellationToken`, which still works. Because it is a
parameter, the built-in `CA2016` analyzer flags every `await` that does not forward it. A job that ignores the
token cannot be interrupted by `IScheduler.Interrupt` and holds up shutdown until it finishes.

```diff
  public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
  {
-     await httpClient.GetAsync(url);              // CA2016: forward the cancellationToken parameter
+     await httpClient.GetAsync(url, cancellationToken);
  }
```

In Quartz's own jobs it found nine places that ignored interruption, including the interruption sample.

## The job factory hands out a scope

`IJobFactory` works with a `JobScope` instead of a bare `IJob`, and `NewJob` is `CreateJob`:

```diff
- ValueTask<IJob> NewJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default);
- ValueTask ReturnJob(IJob job);
+ ValueTask<JobScope> CreateJob(TriggerFiredBundle bundle, IScheduler scheduler, CancellationToken cancellationToken = default);
+ ValueTask ReturnJob(JobScope scope, CancellationToken cancellationToken = default);
```

`JobScope` is a readonly struct holding the job and an opaque `State`. Put what you allocated to build the job
(a DI scope, a connection, a tenant context) in `State`, and `ReturnJob` gets it back:

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
The example *activates* the job instead of resolving it from the scope. `SimpleJobFactory.ReturnJob` disposes
the job and then the state, so a job resolved from the scope (`GetRequiredService<MyJob>()`) is disposed twice.
Activate it, or override `ReturnJob` to skip a container-owned job, as `MicrosoftDependencyInjectionJobFactory`
does.
:::

Keep `CreateJobInstance` non-`async` when its body is synchronous: an async state machine restores the
caller's execution context on return, discarding any `AsyncLocal` set while building the job, including what
`ConfigureScope` sets for the job to read.

* **`IJobWrapper` is removed.** `MicrosoftDependencyInjectionJobFactory` no longer wraps your job, so
  `IJobExecutionContext.JobInstance` and every listener see your type.
* `PropertySettingJobFactory.InstantiateJob` is replaced by the asynchronous `CreateJobInstance` above. The old
  hook stayed synchronous after `NewJob` went async, so real work meant overriding `NewJob` and reimplementing
  property setting.
* The internal `IJobWithAsyncReturnFactory` that 3.x carried beside `IJobFactory` is gone; its asynchronous
  shape is `IJobFactory`'s own now.
* `SimpleJobFactory`'s `protected static Dispose(object?)` is `DisposeIfDisposable(object?, CancellationToken)`.
  It still disposes the argument only when it is disposable.

### Scheduler context entries are no longer injected into job properties

On 3.x, `PropertySettingJobFactory.BuildJobDataMap` merged the whole `SchedulerContext` under the job's and
trigger's data on every fire, so a context entry matching a job property was injected. 4.x applies only the
trigger's data merged over the job's. **This is a silent behavioral change**: a job property fed from
`scheduler.Context["ConnectionString"]` (or a `quartz.context.key.*` property) keeps its default, and nothing
throws. `MicrosoftDependencyInjectionJobFactory` derives from `PropertySettingJobFactory`, so the default DI
path is affected.

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

The merge also had defects: 3.x's DI integration put a service-provider entry in every scheduler context, so
every container-hosted fire logged a property miss (and threw under `PropertyMismatchBehavior.Throw`), and the
factory enumerated the context while plugins could still write to it. 4.0 seeds no such entry — see
[The container is not in the scheduler context](#the-container-is-not-in-the-scheduler-context).

### One setting says what a property miss does

`PropertySettingJobFactory`'s two booleans are one `PropertyMismatchBehavior` property:

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

`PropertyMismatchBehavior` is in `Quartz.Impl`. Neither boolean had a `quartz.*` key, so the legacy
configuration bridge is unchanged.

### The factory is set where the scheduler is built

The setter-only `IScheduler.JobFactory` is gone from `IScheduler`, `StdScheduler`, `DelegatingScheduler` and
`HttpScheduler` (where it only threw). Set the factory when building the scheduler:

```diff
- scheduler.JobFactory = new MyJobFactory();
+ services.AddQuartz(q => q.UseJobFactory(new MyJobFactory()));
```

`UseJobFactory(IJobFactory)` is new on `IQuartzBuilder`, beside the existing generic `UseJobFactory<T>()`:

```csharp
// standalone
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q.UseJobFactory(new MyJobFactory()))
    .BuildScheduler();
```

`QuartzScheduler` is internal, so the job factory is always configured through the builder or the container.

#### When the factory's dependency does not exist yet

3.x code set `IScheduler.JobFactory` late when the factory needed something available only after startup: a
connected bus, a tenant catalogue, an elected leader. Configuring the factory at build time does not mean
*resolving* its dependency then.

**Resolve per firing.** `UseJobFactory<T>()` builds the factory from the scheduler's service provider, so a
factory that takes `IServiceProvider` resolves what it needs in `CreateJob`, which runs once per firing:

<!-- snippet: sample_migration_late_bound_job_factory_use -->
```csharp
services.AddSingleton<LateBound<IMessageBus>>();
services.AddQuartz(q => q.UseJobFactory<BusAwareJobFactory>());
```
<!-- endSnippet -->

**Hand it a holder.** When the dependency is not in the container, because whatever produces it also runs at
startup, register a holder the factory reads through and fill it once the value exists. The standalone
`QuartzSchedulerBuilder` needs this too, since it builds its own container:

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

Keep `CreateJob` synchronous, for the `AsyncLocal<T>` reason [above](#the-job-factory-hands-out-a-scope): an
`async` method restores the caller's `ExecutionContext` when it resumes, and the job loses the ambient state
the factory set.

## Trigger fire times are properties

```diff
- DateTimeOffset? next = trigger.GetNextFireTimeUtc();
+ DateTimeOffset? next = trigger.NextFireTimeUtc;

- operableTrigger.SetNextFireTimeUtc(value);
+ operableTrigger.NextFireTimeUtc = value;

- if (trigger.GetMayFireAgain()) { … }
+ if (trigger.MayFireAgain) { … }
```

The `Get…()` methods, once `[Obsolete]` forwarders on `ITrigger` and `TriggerBase`, are removed: delete `Get` and
`()`. The `Set…` methods have no stand-in, since a method and a property setter cannot share a name. All are on
`ITrigger`, `IOperableTrigger` or `TriggerBase`, and on all five `*TriggerImpl` types by inheritance:

| 3.x | 4.x |
|---|---|
| `GetNextFireTimeUtc()` | `NextFireTimeUtc` |
| `SetNextFireTimeUtc(value)` | `NextFireTimeUtc = value` |
| `GetPreviousFireTimeUtc()` | `PreviousFireTimeUtc` |
| `SetPreviousFireTimeUtc(value)` | `PreviousFireTimeUtc = value` |
| `GetMayFireAgain()` | `MayFireAgain` |

A **custom trigger deriving from `TriggerBase`** overrides the `MayFireAgain` property, the abstract member now:

```diff
- public override bool GetMayFireAgain() => NextFireTimeUtc is not null;
+ public override bool MayFireAgain => NextFireTimeUtc is not null;
```

`CronTriggerImpl.CronExpression` gained a getter (it was setter-only) and is typed `CronExpression?`.

## The thread pool is asynchronous

Only relevant if you implement `IThreadPool`:

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

* The two renamed methods blocked a thread on a semaphore inside the scheduler's asynchronous loop; use
  `WaitAsync` in your implementation.
* `TryRun` takes a `Func<ValueTask>`; the `Task`-shaped delegate cost a `Task<Task>`/`Unwrap` per fire. A
  lambda that returned `Task.CompletedTask` returns `ValueTask.CompletedTask`.
* `InstanceId` and `InstanceName` are removed: Quartz set them and nothing read them. For the scheduler's
  identity, take `IOptions<QuartzSchedulerOptions>` from the container.
* `TaskSchedulingThreadPool.ThreadCount` is removed; use `MaxConcurrency`, which it read and wrote. **The
  `quartz.threadPool.threadCount` configuration key is unaffected** and still sets `MaxConcurrency`.

### `Drain` is the shutdown that can be given a deadline

`Drain` stops the pool accepting work, waits for running work, and **reports** whether it finished or gave up.
`Shutdown(waitForJobsToComplete: true)` waits without bound, and abandoning it by throwing would skip the job
store shutdown, plugin shutdown and listener notification after it. `IScheduler.Shutdown` now passes its own
token to `Drain` instead of `CancellationToken.None`, so a host stop that runs out of time stops *waiting*.

The token cancels only the wait, never running jobs; interrupting jobs on shutdown is
`ShutdownJobInterruption`'s decision, which still defaults to never.

**Implementing it is optional.** The default calls `Shutdown(waitForJobsToComplete: true, CancellationToken.None)`
and returns `true`, as a 3.x-shaped pool did. If your pool can honour a deadline, override it, and build the
barrier on work items:

* `TryRun` is handed the *whole* of a job's execution, ending with the job store update that completes the
  trigger, so waiting for work items also waits for those writes.
* `NumberOfJobsExecutingHere` is not a valid barrier: job listeners hear the job ran before the store update is
  issued, so it reads zero while a persistent store is still being written.

`TaskSchedulingThreadPool` implements both over one asynchronous barrier, so its `Shutdown` awaits instead of
calling `CountdownEvent.Wait`. A caller that never awaited the returned `ValueTask` used to get the wait anyway;
now it must await.

## Quartz.Spi and Quartz.Simpl were renamed

`Quartz.Spi` is `Quartz.Extensibility`, and `Quartz.Simpl` merged into `Quartz.Impl` (the old names copied
`org.quartz.spi` and `org.quartz.simpl`). In source this is a `using` find-and-replace the compiler guides you
through. The source tree followed: `src/Quartz/SPI/` is `src/Quartz/Extensibility/` and `src/Quartz/Simpl/` is
`src/Quartz/Impl/`.

Configuration names types by string, so it would not fail loudly:

```diff
- quartz.jobStore.type = Quartz.Simpl.RAMJobStore, Quartz
+ quartz.jobStore.type = Quartz.Impl.RAMJobStore, Quartz
```

**Existing configuration keeps working.** A type name in a pre-4.0 namespace that no longer resolves is
retried under the new one, and a warning names both spellings. Treat that as a grace period, not a promise. It
also covers assemblies merged into the core package, together with the namespace rename:

```diff
- quartz.serializer.type = Quartz.Simpl.SystemTextJsonObjectSerializer, Quartz.Serialization.SystemTextJson
+ quartz.serializer.type = Quartz.Impl.SystemTextJsonObjectSerializer, Quartz
```

**Stored job type names resolve through the same fallback**, with the same warning. `JOB_CLASS_NAME` holds the
spelling of the version that wrote the row, so in a database from 2.x or 3.x, `Quartz.Job.NoOpJob, Quartz` now
finds `Quartz.Jobs`. Reading a job never rewrites the column, so the warning stays until you migrate the data.
Before, such a job started and listed fine (job types resolve lazily) and failed with a `TypeLoadException`
the first time it fired.

### Other namespaces that moved

4.0 also aligns each namespace with its assembly and package, and empties namespaces that held one type or a
handful. In source each row is a `using` change.

| 3.x namespace | 4.x namespace | Old spelling in a string |
|---|---|---|
| `Quartz.Job` | `Quartz.Jobs` | resolves, with a warning, in configuration and in stored `JOB_CLASS_NAME` |
| `Quartz.Extensibility.IDirectoryProvider` | `Quartz.Jobs.IDirectoryProvider` | never named by type; resolved from `SchedulerContext` by key |
| `Quartz.Logging`, `Quartz.Logging.LogProviders` | `Quartz.Diagnostics` | never named in configuration; no fallback |
| `Quartz.Plugin.History`, `Quartz.Plugin.Json`, `Quartz.Plugin.Xml` | `Quartz.Plugins.*` | `quartz.plugin.<name>.type` resolves, with a warning |
| `Quartz.Plugin.Interrupt`, `Quartz.Plugin.Management`, `Quartz.Plugin.TimeZoneConverter` | — | the type each held is gone |
| `Quartz.Listener` | `Quartz.Listeners` | listeners are no longer named by string |
| `Quartz.Impl.Matchers` | `Quartz` | never named in configuration; no shim needed |
| `Quartz.AspNetCore`, `Quartz.AspNetCore.HealthChecks`, `Quartz.AspNetCore.HttpApi` | `Quartz` | — |
| `Quartz.HttpClient` | `Quartz` | — |
| `Quartz.Serialization.Json`, `Quartz.Serialization.Json.Calendars`, `Quartz.Serialization.Json.Triggers` | `Quartz.Serialization.SystemTextJson[.Calendars\|.Triggers]` | see the warning below |
| `Quartz.Impl.Redis` | `Quartz.Extensions.Redis` | `quartz.jobStore.lockHandler.type` naming the old namespace or type resolves, with a warning |

* **`Quartz.Jobs.IDirectoryProvider`** lives with `DirectoryScanJob`, its only user.
  `GetDirectoriesToScan(JobDataMap)` returns `List<string>` instead of `IReadOnlyList<string>`, so an
  implementation returning an array or a `ToList()` result needs one edit.
* **`Quartz.Diagnostics`** has `LogProvider`, `DiagnosticHeaders` (now `ActivityTags`) and `OperationName`.
  `ILogProvider`, `LogContext`, `LogLevel`, the `Logger` delegate, `IJobDiagnosticData` and
  `LogProviders.LibLogException` went with LibLog; `using Quartz.Logging;` no longer resolves — see
  [Logging](#logging).
* **`Quartz.Plugins.*`**: the package is `Quartz.Plugins`. The **configuration key** prefix stays
  `quartz.plugin.`, singular.
* **The retired plugin namespaces**: see
  [`JobInterruptMonitorPlugin` is retired; a job timeout is middleware](#jobinterruptmonitorplugin-is-retired-a-job-timeout-is-middleware),
  [`ShutdownHookPlugin` is retired; the host already shuts the scheduler down](#shutdownhookplugin-is-retired-the-host-already-shuts-the-scheduler-down)
  and [`TimeZoneConverterPlugin` is a resolver registration](#timezoneconverterplugin-is-a-resolver-registration).
  The `Quartz.Plugins.TimeZoneConverter` **package** still ships `UseTimeZoneConverter`.
* **`Quartz.Listeners`**: the `quartz.jobListener.<name>.type` and `quartz.triggerListener.<name>.type` keys are
  gone ([The listener property keys are retired](#the-listener-property-keys-are-retired)). Three of the seven
  types are gone under both names — see
  [The three `*Support` base classes are gone](#the-three-support-base-classes-are-gone).
* **`Quartz.Impl.Matchers`**: see [Matchers moved to `Quartz`](#matchers-moved-to-quartz).
* **`Quartz.AspNetCore*`**: `AddQuartzHealthChecks`, `AddQuartzHttpApi` and `MapQuartzHttpApi` are extension
  methods that resolve through `Quartz`, so delete `using Quartz.AspNetCore;`. The HTTP API package is still
  `Quartz.AspNetCore`, hosted by `QuartzAspNetCoreConfigurationExtensions` (renamed from
  `QuartzServiceCollectionExtensions`, a name core now uses in the same namespace). The health check is in
  `Quartz`, hosted by `QuartzHealthCheckExtensions` — see
  [The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore).
* **`Quartz.HttpClient`**: `HttpScheduler` and `HttpClientException` moved; the package is still
  `Quartz.HttpClient`. The namespace shadowed `System.Net.Http.HttpClient` in every file under `Quartz.*`.
  `HttpScheduler` is also `sealed` now.
* **`Quartz.Serialization.SystemTextJson`**: the System.Text.Json types merged into core; the old namespace was
  named after the *retired 3.x Newtonsoft package*. `Quartz.JsonConfigurationExtensions` is
  `Quartz.SystemTextJsonConfigurationExtensions`; its extension methods are unchanged. **Read the warning below
  before changing a `using` on a ported serializer.**
* **`Quartz.Extensions.Redis`**: its one type, `RedisSemaphore`, is `RedisLockHandler`. Namespace, assembly and
  package now match; the **package id is unchanged**.

::: warning Porting a 3.x Newtonsoft serializer
In 3.x, `Quartz.Serialization.Json.Triggers` and `Quartz.Serialization.Json.Calendars` were the **Newtonsoft**
package's namespaces. In 4.x, `Quartz.Serialization.SystemTextJson.*` is System.Text.Json's, and Newtonsoft's
are `Quartz.Serialization.Newtonsoft.Triggers` and `Quartz.Serialization.Newtonsoft.Calendars`.

A 3.x Newtonsoft serializer whose `using` is changed to `Quartz.Serialization.SystemTextJson.Triggers` compiles
against the *System.Text.Json* base class, then fails on the overrides, which take a `Utf8JsonWriter` and a
`JsonElement` instead of a `JsonWriter` and a `JObject`. Keep it on the Newtonsoft base — see
[Newtonsoft types moved out of the core namespaces](#newtonsoft-types-moved-out-of-the-core-namespaces).
:::

## The scheduler and the job store speak the same verbs

`IJobStore` now uses `IScheduler`'s verbs (Schedule/Add/Delete/Get) instead of Store/Remove/Retrieve. If you
implement a job store, rename; callers of `IScheduler` are unaffected.

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

* The overwrite flag, spelled `replaceExisting` or `replace` in 3.x, is gone: each member takes the options
  record `IScheduler` takes — see [The store takes the same options](#the-store-takes-the-same-options).
* The `protected` `AdoJobStoreBase` mirrors (the `ConnectionAndTransactionHolder` overloads, and
  `AcquireNextTrigger`) were renamed too, and keep their `bool`.
* The activity names in `Quartz.Diagnostics.OperationName.JobStore` follow the methods: a trace filter on
  `"Quartz.JobStore.StoreJob"` needs `"Quartz.JobStore.AddJob"`, and so on for every row.

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

`TriggerAcquisitionRequest` is in `Quartz.Extensibility`, the store-level counterpart of the delegate's
`TriggerAcquisitionCriteria`. A new acquisition dimension (like the batching window, execution-group limits and
node affinity before it) becomes an optional property a store can ignore, not a parameter. `TimeWindow`
rejects a negative value at construction; `AdoJobStoreBase` used to throw inside acquisition.

Both records have an optional `ExcludedJobTypeNames`, and **every shipped store honours the request-level
one**: `AdoJobStoreBase` copies it into its delegate criteria and the standard delegates exclude the rows in
SQL, while `RAMJobStore` skips a candidate whose job type name is in the set. Names are the stored
`TriggerAcquireResult.JobTypeName` spelling, `JobType.FullName`. `RAMJobStore` compares ordinally; SQL follows
the job-class column's collation, including case sensitivity.

## Options records replace boolean parameters

`AddJob` and `AddCalendar` each take one optional record instead of anonymous booleans (and `AddJob`'s second
overload is gone). The defaults are the conservative choice: `Replace = false`,
`StoreNonDurableWhileAwaitingScheduling = false`, `UpdateTriggers = false`.

| 3.x | 4.x |
|---|---|
| `AddJob(job, replace: false)` | `AddJob(job)` |
| `AddJob(job, replace: true)` | `AddJob(job, new AddJobOptions { Replace = true })` |
| `AddJob(job, true, true)` | `AddJob(job, new AddJobOptions { Replace = true, StoreNonDurableWhileAwaitingScheduling = true })` |
| `AddCalendar(name, cal, false, false)` | `AddCalendar(name, cal)` |
| `AddCalendar(name, cal, true, true)` | `AddCalendar(name, cal, new AddCalendarOptions { Replace = true, UpdateTriggers = true })` |

* The common `replace: true` case has a name on each record: `AddJobOptions.Replacing`,
  `ScheduleJobOptions.Replacing`, `AddCalendarOptions.Replacing` and
  `AddCalendarOptions.ReplacingAndUpdatingTriggers` — see
  [Five shorthands for the common case](#five-shorthands-for-the-common-case).
* `AddJobOptions` and `AddCalendarOptions` are in the `Quartz` namespace, and `IJobStore` takes them too — see
  [The store takes the same options](#the-store-takes-the-same-options). `IScheduler` applies
  `AddJobOptions.StoreNonDurableWhileAwaitingScheduling` before calling the store, so a store reads only
  `Replace`.
* Both are `readonly record struct`s, and the parameter is `AddJobOptions options = default`, not
  `AddJobOptions? options = null`: `default` is what passing nothing always meant. A store no longer needs
  `options ??= new()`.

```diff
- await scheduler.AddJob(job, null);
+ await scheduler.AddJob(job);

- AddJobOptions? options = null;
+ AddJobOptions options = default;
```

`new AddJobOptions { Replace = true }`, `new AddCalendarOptions { … }` and calls that omit the argument are
unchanged. What stops compiling: an explicit `null`, a nullable local of the type passed in, and a
`CalendarConfiguration.Options` read that expected `null` to mean "the scheduler's own defaults", which it never
did. The DI-time builders `q.AddJob<T>(…)` and `q.AddCalendar<T>(…)` on `IQuartzBuilder` are unchanged.

### `ScheduleJob` and `ScheduleJobs` take the same treatment

The two `IScheduler` members that schedule a job with a collection of triggers took a bare `bool replace`:

| 3.x | 4.x |
|---|---|
| `ScheduleJob(job, triggers, replace: false)` | `ScheduleJob(job, triggers)` |
| `ScheduleJob(job, triggers, replace: true)` | `ScheduleJob(job, triggers, new ScheduleJobOptions { Replace = true })` |
| `ScheduleJobs(triggersAndJobs, false)` | `ScheduleJobs(triggersAndJobs)` |
| `ScheduleJobs(triggersAndJobs, true)` | `ScheduleJobs(triggersAndJobs, new ScheduleJobOptions { Replace = true })` |

`ScheduleJobOptions` is not `AddJobOptions`: its `Replace` covers the job *and* its triggers, and
`StoreNonDurableWhileAwaitingScheduling` means nothing when a trigger is always supplied. The HTTP API's
`ScheduleJobsRequest` keeps its `Replace` property.

### The store takes the same options

`IJobStore.AddJob`, `AddTrigger` and `ScheduleJobs` took a `bool` while `AddCalendar` took a record. All four
take the *same* record the scheduler takes, so a decorator passes it through:

| 3.x | 4.x |
|---|---|
| `store.StoreJob(job, replaceExisting: false)` | `store.AddJob(job)` |
| `store.StoreJob(job, replaceExisting: true)` | `store.AddJob(job, AddJobOptions.Replacing)` |
| `store.StoreTrigger(trigger, replaceExisting: true)` | `store.AddTrigger(trigger, AddTriggerOptions.Replacing)` |
| `store.StoreJobsAndTriggers(batch, replace: true)` | `store.ScheduleJobs(batch, ScheduleJobOptions.Replacing)` |

`AddTriggerOptions` is new and carries only `Replace`. `IScheduler` has no `AddTrigger` (a trigger arrives
through `ScheduleJob`), so `IJobStore.AddTrigger` is its one call site. An `IJobStore` implementation changes
the three signatures and reads `options.Replace`.

## A component of your own can have options of its own

`IQuartzBuilder.ConfigureOptions<TOptions>(Action<TOptions>?)` is new. It registers the callback under this
scheduler's options name, so a container-built component asking for `IOptions<TOptions>` sees its own
scheduler's configuration instead of the *unnamed* instance. Before, only `AddPlugin<T, TOptions>()` could do
this (it is now sugar over this member); a thread pool, job store, lock handler, listener or job factory under
`AddQuartz("name", …)` saw defaults.

```csharp
services.AddQuartz("reporting", q =>
{
    q.ConfigureOptions<MyThreadPoolOptions>(options => options.Slots = 20);
    q.UseThreadPool<MyThreadPool>();
});
```

It is a default interface implementation, so an external `IQuartzBuilder` keeps compiling, and the default body
is the whole mechanism: the options name is the scheduler's `SchedulerName` (`Options.DefaultName`, the empty
string, for the unnamed scheduler).

`UseThreadPool<T>()` drops its `Action<ThreadPoolOptions>` parameter:

| Before | After |
|---|---|
| `UseThreadPool<T>(options => options.MaxConcurrency = 20)` | `UseDefaultThreadPool(maxConcurrency: 20)`, or `ConfigureOptions<TOwnOptions>(…)` for a pool of your own |

It worked only for `TaskSchedulingThreadPool` and descendants: `UseThreadPool<AnythingElse>(o => o.MaxConcurrency = 5)`
compiled and did nothing. `UseDefaultThreadPool` keeps the parameter.

## Overloads that differed only by a default

| 3.x | 4.x |
|---|---|
| `Shutdown()`, `Shutdown(bool waitForJobsToComplete)` | `Shutdown(bool waitForJobsToComplete = false, …)` |
| `TriggerJob(jobKey)`, `TriggerJob(jobKey, data)` | `TriggerJob(JobKey jobKey, JobDataMap? data = null, …)` |
| `Interrupt(string fireInstanceId)` | `InterruptFireInstance(string fireInstanceId)` |
| `GetMetaData()` | `GetMetadata()`, returning `SchedulerMetadata` |
| `GetTriggersOfJob(jobKey)` | extension method over `QueryTriggers(new TriggerQuery { Job = jobKey })` |

`Shutdown` and `TriggerJob` calls compile unchanged, unless they passed a `CancellationToken` positionally to
the short overload; name it:

```diff
- await scheduler.Shutdown(cancellationToken);
+ await scheduler.Shutdown(cancellationToken: cancellationToken);
```

* `Interrupt(string)` is renamed because `Interrupt(JobKey)` (every execution of a job) and
  `Interrupt(string)` (one fire) were two operations picked silently by argument type. `Interrupt(JobKey)` keeps
  its name.
* `GetTriggersOfJob` is an extension method in `SchedulerQueryExtensions`, so call sites compile. It runs
  `QueryTriggers` and then `GetTriggers`; if the header's state and fire times are enough, call `QueryTriggers`
  with `TriggerQuery.Job` and save the second round trip.

### `SchedulerMetadata` replaces `SchedulerMetaData`

```diff
- SchedulerMetaData metaData = await scheduler.GetMetaData();
- Console.WriteLine($"Executed {metaData.NumberOfJobsExecuted} jobs.");
- Console.WriteLine(metaData.GetSummary());
+ SchedulerMetadata metadata = await scheduler.GetMetadata();
+ Console.WriteLine($"Executed {metadata.JobsExecuted} jobs.");
+ Console.WriteLine(metadata);
```

The fifteen-parameter constructor (six adjacent booleans) is gone: it is a `sealed record` with `init`
properties. `GetSummary()` is gone; `ToString()` prints every value. Over HTTP,
`SchedulerStatisticsDto.NumberOfJobsExecuted` is `JobsExecuted` to match.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `SchedulerType` (`Type`) | `SchedulerTypeName` (`string`) |
| `JobStoreType` (`Type`) | `JobStoreTypeName` (`string`) |
| `ThreadPoolType` (`Type`) | `ThreadPoolTypeName` (`string`) |
| `SchedulerRemote` / `IsRemote` | `IsProxy` |
| `NumberOfJobsExecuted` | `JobsExecuted` |
| `JobStoreSupportsPersistence` | `JobStorePersistent`, like its sibling `JobStoreClustered` |
| `Started`, `InStandbyMode`, `Shutdown` | one `required SchedulerStatus Status` — see [A scheduler's lifecycle is one value](#a-scheduler-s-lifecycle-is-one-value) |

* The `*TypeName` members are assembly-qualified names without version: an `HttpScheduler` reads a remote
  scheduler's metadata over the wire, and the remote's job store or thread pool type need not exist in the
  client process (it used to be `Type.GetType`-resolved there).
* `IsProxy` means the metadata describes a proxy to a scheduler running elsewhere, read over the wire.

## Single-key mutations answer whether they applied

A mutation aimed at one key returns `ValueTask<bool>`, "the entity existed and the operation applied"; the
group-matcher forms return the affected group names. Before, `DeleteJob`, `UnscheduleJob` and
`UpdateTriggerDetails` returned a `bool`, but the pause family returned nothing.

| Member (on `IScheduler` and `IJobStore`) | 3.x returned | 4.x returns |
|---|---|---|
| `PauseTrigger(key)`, `ResumeTrigger(key)` | `ValueTask` | `ValueTask<bool>` |
| `PauseJob(key)`, `ResumeJob(key)` | `ValueTask` | `ValueTask<bool>` |
| `ResetTriggerFromErrorState(key)` | `ValueTask` | `ValueTask<bool>` |
| `PauseTriggerGroups(matcher)`, `ResumeTriggerGroups(matcher)` | `ValueTask` (scheduler) | `ValueTask<List<string>>` of the group names affected |
| `PauseJobGroups(matcher)`, `ResumeJobGroups(matcher)` | `ValueTask` (scheduler) | `ValueTask<List<string>>` of the group names affected |

| Member | `true` when | `false` when |
|---|---|---|
| `PauseTrigger` | the trigger exists and this call paused it | already paused, complete, or missing |
| `ResumeTrigger` | the trigger was paused and is resumed | not paused, or missing |
| `PauseJob` / `ResumeJob` | the job exists, even with zero triggers | missing |
| `ResetTriggerFromErrorState` | the trigger was in `Error` and is reset | not in `Error`, or missing |

* On `false`, no scheduler-listener events are raised, so a no-op no longer looks like a state change.
  Awaiting call sites compile unchanged; only an `ISchedulerListener` that relied on hearing about no-op pauses
  notices.
* Over HTTP these endpoints answered `200 OK` with an empty body; they now answer `{"applied": bool}` for the
  single-key forms and `{"groups": [...]}` for the group-matcher forms. Clients that ignored the body are
  unaffected, but **a 4.0-final `HttpScheduler` against a 4.0-preview server throws** on these calls, because
  it reads a body the old server never sends. Upgrade the server before, or with, its remote clients.

## A set of keys pauses, resumes or resets in one call

`IScheduler` and `IJobStore` take a key set as well as one key or a group matcher, so forty triggers are one
call, not forty (and on a database store, not forty transactions and forty scheduling signals):

| New member (on `IScheduler` and `IJobStore`) | Returns |
|---|---|
| `PauseTriggers(IReadOnlyCollection<TriggerKey>)`, `ResumeTriggers(…)` | `ValueTask<List<TriggerKey>>` of the keys it applied to |
| `PauseJobs(IReadOnlyCollection<JobKey>)`, `ResumeJobs(…)` | `ValueTask<List<JobKey>>` of the keys it applied to |
| `ResetTriggersFromErrorState(IReadOnlyCollection<TriggerKey>)` | `ValueTask<List<TriggerKey>>` of the keys it reset |

```csharp
List<TriggerKey> paused = await scheduler.PauseTriggers(
    [new TriggerKey("nightly", "reports"), new TriggerKey("hourly", "reports")]);
```

* A key the operation did not apply to (names nothing, already paused, not in error) is **absent from the
  result**, never an exception. The result keeps the input order.
* These are overloads, so the single-key and matcher forms are untouched; only a `null` literal argument, which
  never compiled against `PauseJobs(null)` anyway, needs a cast. The matcher forms are named
  [`PauseJobGroups` and its three siblings](#pausing-by-matcher-is-a-group-operation-and-is-named-for-one).
* **Listener events stay per key**: one `TriggerPaused`, `JobPaused`, `TriggerResumed` or `JobResumed` per key
  applied, nothing for the rest. There is no key-set event: `TriggersPaused(null)` means *every group*, so a
  listener would read a two-trigger bulk pause as the whole scheduler going down.
* **One scheduling signal per call**, not per key; the scheduler thread reads the signal as a level. Resetting
  from the error state signals nothing, as in the single-key form.
* **One pass in the store**: `RAMJobStore` locks once; the ADO store runs the set in one
  `SchedulerLock.TriggerAccess` scope and one transaction, so a bulk pause is atomic.
* `IJobStore`'s five members are default implementations that walk the set per key, so a custom store keeps
  compiling and stays correct. Override them for one pass, as both shipped stores do.

Over HTTP they are new endpoints — `POST …/jobs/keys/pause`, `…/jobs/keys/resume`, `…/triggers/keys/pause`,
`…/triggers/keys/resume` and `…/triggers/keys/reset-from-error-state` — see
[the HTTP API page](packages/http-api.md#a-whole-set-of-keys-in-one-call). They are under `keys/` because the
collection-level `pause` and `resume` belong to the group-matcher forms.

## The key-set delete and unschedule answer with the keys they removed

`DeleteJobs` and `UnscheduleJobs` answered one `bool`, "every key given was found": deleting three of five
existing jobs deleted **three jobs** and answered `false`, the same answer as doing nothing. They now return the
keys, like the rest of the key-set family:

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

* **A key that named nothing raises no listener event.** 3.x raised one `ISchedulerListener.JobDeleted` /
  `JobUnscheduled` per key **given**, so a mistyped key announced a deletion. Events now follow the keys
  applied, as with `DeleteJob` and the key-set pause family. The scheduling change is signalled once per call,
  and not at all when nothing was removed.
* **An empty key set never reaches the store**, and `null` throws `ArgumentNullException`, not
  `NullReferenceException`.
* **A repeated key no longer turns the answer `false`**; the second pass just finds nothing.
* For `IJobStore`, both members are **default implementations** walking the set per key, so a store that
  implements only the single-key `DeleteJob` / `DeleteTrigger` is correct. **Trap**: a store that still declares
  `ValueTask<bool> DeleteJobs(IReadOnlyCollection<JobKey>, CancellationToken)` **still compiles**, but that
  method no longer implements the interface member, and the per-key default silently takes over. Change the
  return type. Both shipped stores override the pair to walk inside one lock and one transaction;
  `JobStoreContractTest` fails a store that has left the default in place.
* The ADO store deletes per key on purpose. Deleting a job cascades (its triggers and their sub-table rows, the
  fired-trigger rows that would otherwise resurrect it, then the job detail row), and a set-based
  `DELETE … WHERE … IN (…)` reports a row count, not keys. The per-key results were already there, so naming
  the keys costs no extra round trip.

Over HTTP, `POST …/jobs/delete` and `POST …/triggers/unschedule` answer `{"jobs": [ … ]}` and
`{"triggers": [ … ]}` instead of `{"allFound": …}`, like the rest of
[the key-set family](packages/http-api.md#a-whole-set-of-keys-in-one-call).

## Jobs and triggers can be removed by group

`DeleteJobs` and `UnscheduleJobs` take a `GroupMatcher` as well as a key set. Removing one saga's, tenant's or
import's work used to mean listing the keys and then deleting them, leaving a window in which another node could
add to the group.

| New member (on `IScheduler`) | Returns |
|---|---|
| `DeleteJobs(GroupMatcher<JobKey>)` | `ValueTask<List<JobKey>>` of the jobs it deleted |
| `UnscheduleJobs(GroupMatcher<TriggerKey>)` | `ValueTask<List<TriggerKey>>` of the triggers it removed |

```csharp
// Everything scheduled for one saga, called off in one call.
List<TriggerKey> calledOff = await scheduler.UnscheduleJobs(GroupMatcher<TriggerKey>.GroupEquals(sagaId));
```

* They answer with **keys**, not the group names
  [`PauseJobGroups`](#pausing-by-matcher-is-a-group-operation-and-is-named-for-one) returns: a paused group is
  remembered (a job added later is born paused), a deleted one is not.
* **`null` is an `ArgumentNullException`, not "the default group".** 3.x's `PauseJobs(null)` and
  `ResumeJobs(null)` read a missing matcher as `GroupEquals(JobKey.DefaultGroup)`; these do not, because only a
  pause taken by mistake can be undone. A `null` literal needs a cast to pick an overload:
  `DeleteJobs((IReadOnlyCollection<JobKey>) null!)`.
* **One scheduling signal per call and one listener event per key removed** (`ISchedulerListener.JobDeleted`
  or `JobUnscheduled`), nothing for an empty group, and no group-level event: `JobsPaused(null)` means *every
  group*, which a monitoring listener would read as a total outage.
* **A non-durable job orphaned by `UnscheduleJobs(matcher)` is deleted but not named** in the answer, as with
  the single-key `UnscheduleJob` and the key-set `DeleteTriggers`.
* `IJobStore` gains `DeleteJobs(GroupMatcher<JobKey>)` and `DeleteTriggers(GroupMatcher<TriggerKey>)` as
  **default implementations** that list the keys and then delete them: correct for a custom store, but not
  atomic. Both shipped stores override them to resolve the group inside the lock that empties it: `RAMJobStore`
  under its single lock, the ADO store in one `SchedulerLock.TriggerAccess` scope and one transaction, using the
  `SelectJobKeysInGroup` / `SelectTriggerKeysInGroup` its driver delegate already had. `JobStoreContractTest`
  fails a shipped store that has left the default in place. The ADO store then deletes per key, for the reason
  [the key-set forms give](#the-key-set-delete-and-unschedule-answer-with-the-keys-they-removed).

Over HTTP they are `POST …/jobs/delete-by-group` and `POST …/triggers/unschedule-by-group`, taking the same four
`group*` query parameters as `…/jobs/pause` and answering with the key-set forms' `{"jobs": […]}` /
`{"triggers": […]}` bodies — [described here](packages/http-api.md#a-whole-group-in-one-call). The plain
`delete` and `unschedule` paths already belonged to the key-set forms.

## Pausing by matcher is a group operation, and is named for one

`PauseJobs` answered with keys when given keys, and with **group names** when given a `GroupMatcher`. The
matcher forms are renamed for what they do:

| 3.x / 4.0 preview | 4.0 |
|---|---|
| `PauseJobs(GroupMatcher<JobKey>)` | `PauseJobGroups(GroupMatcher<JobKey>)` |
| `ResumeJobs(GroupMatcher<JobKey>)` | `ResumeJobGroups(GroupMatcher<JobKey>)` |
| `PauseTriggers(GroupMatcher<TriggerKey>)` | `PauseTriggerGroups(GroupMatcher<TriggerKey>)` |
| `ResumeTriggers(GroupMatcher<TriggerKey>)` | `ResumeTriggerGroups(GroupMatcher<TriggerKey>)` |

The key-set overloads keep their names and `List<JobKey>` / `List<TriggerKey>` answers, so each name has one
return shape. The rename covers `IScheduler`, `IJobStore` and every shipped implementation; it is a compile
error, not a behaviour change.

The group form answers with group names because:

* **It writes group state, which outlives the keys.** A paused group is a row in `QRTZ_PAUSED_TRIGGER_GRPS` or
  `QRTZ_PAUSED_JOB_GRPS`: it survives a restart, reaches every cluster node, is what `IsTriggerGroupPaused`
  reports, and for trigger groups applies to triggers stored into the group *afterwards*. The names are the
  rows the call wrote.
* **An equality matcher can pause an empty group.** `PauseTriggerGroups(GroupEquals("nightly"))` records the
  pause so what is added next is born paused; a keys answer would say nothing happened.
* **Keys would cost an unbounded query.** The ADO store pauses a group with one set-based `UPDATE`.

`TracingJobStore` opens `Quartz.JobStore.PauseTriggerGroups` and its three siblings; the group and key-set forms
used to share one span name.

## Resetting a group from the error state is an `IScheduler` member

`ResetTriggersFromErrorState(GroupMatcher<TriggerKey>)` moved from `SchedulerQueryExtensions`, a class for
queries, to `IScheduler`, beside its key-set twin and the other matcher-shaped mutations (`DeleteJobs`,
`UnscheduleJobs`, the four `*Groups` members):

```diff
- List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(matcher);   // extension
+ List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(matcher);   // IScheduler member
```

It is a **default interface member** over `QueryTriggers` and the key-set `ResetTriggersFromErrorState`, so a
scheduler written elsewhere needs no change, and one whose store can do it in one statement can override it.

* Code holding an `IScheduler` is unchanged. A variable typed as a *concrete* scheduler (`HttpScheduler`,
  `StdScheduler`) needs `((IScheduler) scheduler).ResetTriggersFromErrorState(matcher)`, because C# does not
  surface a default interface member on the implementing type.
* It is still two round trips, as the extension was: a listing filtered to `TriggerState.Error` and the group,
  then the key-set reset. A trigger that fails between them waits for the next call.

## One wire contract, and its enums have names

The HTTP API's DTOs moved from `Quartz.HttpClient` to `Quartz`, internal and visible to both packages, so
`Quartz.AspNetCore` no longer depends on the client package. Nothing public moved, and every JSON property name
is unchanged.

Enums in the contract are now written by name everywhere, as trigger bodies always were
(`"repeatIntervalUnit": "Hour"`):

| Body | 4.0 preview | 4.0 |
|---|---|---|
| `GET …/schedulers` item, `GET …/schedulers/{name}` | `"status": 1` | `"status": "Running"` |
| `GET …/schedulers/{name}/triggers` item | `"state": 1` | `"state": "Paused"` |
| `GET …/schedulers/{name}/triggers/{group}/{name}/state` | `{"state": 1}` | `{"state": "Paused"}` |

Reading accepts both, so request bodies and `?state=` filters written against a preview keep working; only
responses changed. `HttpScheduler` reads the new spelling; a hand-written client that read `status`/`state` as
numbers must change.

One field had two names for the same value:

| Body | 4.0 preview | 4.0 |
|---|---|---|
| `GET …/schedulers/{name}/jobs` item | `"jobTypeName": "Some.Job, Some.Assembly"` | `"jobType": "Some.Job, Some.Assembly"` |

The value, an assembly-qualified name, is unchanged, and `jobType` matches `GET …/jobs/{group}/{name}`. Core's
`JobHeader.JobTypeName` keeps its name.

Only these two enums are affected. The converters are registered per enum type, because the HTTP API adds them
to the application's shared `JsonOptions`, and a host's own endpoints must keep rendering their enums as before.

## One flag per mutation, named for what it reports

**A `200` carries a body exactly when the operation has something to say that the caller could not work out:**

* a mutation that always acts (`AddJob`, `TriggerJob`, `PauseAll`, `ScheduleJobs`, `AddCalendar`, the scheduler
  and execution-limit writes) answers with an empty body;
* a mutation that may be a no-op answers with one boolean flag;
* a mutation on a group matcher or a key set answers with what it applied to;
* a mutation that computed something answers with that value.

The flag had seven spellings; it has one:

| Endpoint | 4.0 preview | 4.0 |
|---|---|---|
| `DELETE …/jobs/{group}/{name}` | `{"jobFound": …}` | `{"applied": …}` |
| `POST …/jobs/{group}/{name}/interrupt` | `{"interrupted": …}` | `{"applied": …}` |
| `POST …/jobs/interrupt/{fireInstanceId}` | `{"interrupted": …}` | `{"applied": …}` |
| `POST …/triggers/{group}/{name}/unschedule` | `{"triggerFound": …}` | `{"applied": …}` |
| `DELETE …/calendars/{name}` | `{"calendarFound": …}` | `{"applied": …}` |
| `POST …/jobs/delete` | `{"allJobsFound": …}` | `{"jobs": [ … ]}` |
| `POST …/triggers/unschedule` | `{"allTriggersFound": …}` | `{"triggers": [ … ]}` |

`applied` means the entity existed and the operation changed it. The last two rows are key sets and changed
*shape* — see [the key-set delete and unschedule](#the-key-set-delete-and-unschedule-answer-with-the-keys-they-removed).
`HttpScheduler` reads the new spellings; a hand-written client reading the old names must change.

**A client-actionable error names its exception type; a server fault does not.** Every `400` and `404` carries
`type`, `title`, `status`, `detail` and `Quartz-ExceptionType`, whichever layer raised it:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "The scheduler has been shut down",
  "Quartz-ExceptionType": "SchedulerException"
}
```

* `Quartz-ExceptionType` used to appear only on the `SchedulerException` path. It is now on every `400` and
  `404`, including the framework's own: `BadHttpRequestException` for a request the endpoint rejected,
  `NotFoundException` for a `404`. Map the Quartz names back to typed exceptions and treat the rest as opaque,
  as `HttpScheduler` does.
* A `500` does **not** carry it; the caller cannot act on it, and it would expose server internals.
* The one `400` with no body is not the API's: a query parameter the framework could not bind never reaches an
  endpoint.

## Durations are `TimeSpan`s, wherever they are

Three duration members counted whole milliseconds, unlike a trigger body's
`"repeatIntervalTimeSpan": "120.02:30:59.9990000"`, and dropped anything below a millisecond:

| 4.0 preview | 4.0 |
|---|---|
| `POST …/schedulers/{name}/start?delayMilliseconds=30000` | `?delay=00:00:30` |
| `JobExecutionResultDto.RunTimeMs` (`long`) | `RunTime` (`TimeSpan`) |
| `DashboardHistoryEntry.DurationMs` (`long`) | `Duration` (`TimeSpan`) |

`HttpScheduler.StartDelayed` sends the new spelling. A negative `delay` is now a `400`. The two dashboard
records are what the dashboard's SignalR hub and history store put on the wire, so a browser or history store
reading `runTimeMs` / `durationMs` as a number reads `runTime` / `duration` as a `TimeSpan` string instead.

## The dashboard's client speaks one currency

`IQuartzApiClient` is the dashboard's projection of the HTTP API, public so an application can replace it with
its own data source. It now uses Quartz's types throughout, instead of a `string` state beside an enum one, its
own paging model, and sixteen methods taking a `(schedulerName, group, name)` triplet:

| 3.x | 4.x |
|---|---|
| `GetTriggerState(…)` → `string` | → `TriggerState` |
| `TriggerHeaderDto.State` is `string?` | `TriggerState?` |
| `SchedulerHeaderDto.Status`, `SchedulerDetailDto.Status` are `string` | `SchedulerStatus`, a new public enum in `Quartz` |
| `GetJobs(name, string? groupFilter, int page, int pageSize)` → `JobPageDto` | `QueryJobs(name, DashboardJobQuery)` → `PagedResult<JobKeyDto>` |
| `GetTriggers(name, string? groupFilter, TriggerState?, int page, int pageSize)` → `TriggerPageDto` | `QueryTriggers(name, DashboardTriggerQuery)` → `PagedResult<TriggerHeaderDto>` |
| `GetHistory(JobHistoryQueryDto)` → `JobHistoryPageDto` (a `JsonElement`) | `QueryExecutions(DashboardHistoryQuery)` → `PagedResult<DashboardHistoryEntry>` |
| `GetCurrentlyExecutingJobs(name)` → `List<CurrentlyExecutingJobDto>` | `QueryFireInstances(name, DashboardFireInstanceQuery)` → `PagedResult<FireInstanceDto>` — see [What is running is a listing, not a list of contexts](#what-is-running-is-a-listing-not-a-list-of-contexts) |
| `CurrentlyExecutingJobDto` | `FireInstanceDto` (below) |
| — | `QueryClusterNodes(name)` → `List<ClusterNodeDto>` (new), behind the **Cluster** page — see [The nodes of a cluster are a listing](#the-nodes-of-a-cluster-are-a-listing) |
| `IsJobGroupPaused(name, group)` → `bool` | `QueryJobGroups(name, DashboardGroupQuery)` → `PagedResult<JobGroupDto>` (below) |
| — | `QueryTriggerGroups(name, DashboardGroupQuery)` → `PagedResult<TriggerGroupDto>` (new), the trigger-group twin |
| — | `CountMisfires(name, since)` → `int` (new), what the overview's misfire tile reads — see [History and live events say which node they came from](#history-and-live-events-say-which-node-they-came-from) |
| `ExecutionLimitsDto(IReadOnlyDictionary<string, int?> Limits)` | `ExecutionLimitsDto(Dictionary<string, DashboardExecutionLimit> Limits, bool UsesTriggerGroupWhenUnset = false, bool CanReport = true)` (below) |
| `GetExecutionLimits(name)` → `ExecutionLimitsDto?` | → `ExecutionLimitsDto`, never null (below) |
| `IDashboardHistoryStore.GetPage(name, page, pageSize, jobFilter, triggerFilter)` → `DashboardHistoryPage` | `QueryExecutions(DashboardHistoryQuery)` → `PagedResult<DashboardHistoryEntry>` |
| `…Job(name, string group, string jobName)` — eight members | `…Job(name, JobKeyDto)` |
| `…Trigger(name, string group, string triggerName)` — seven members | `…Trigger(name, TriggerKeyDto)` |

* **`FireInstanceDto`**: `FireInstanceId` is non-null and first; `JobKey` is nullable (an acquired firing has
  no job loaded yet); `SchedulerInstanceId`, `FireInstanceState State` and `ScheduledFireTimeUtc` are new.
* **`JobGroupDto`** carries `Name` and `Paused`. One call answers for every group, and `Take = 0` with
  `Paused = true` counts the paused ones without listing them.
* **`ExecutionLimitsDto.Limits`** is a concrete dictionary. Each entry carries the limit's
  [scope](#an-execution-limit-can-be-cluster-wide) and number, keyed by the configuration and HTTP API spellings
  (`_` for the ungrouped bucket, `*` for the catch-all), so a firing can be joined to its limit.
* **`GetExecutionLimits`** returned `null` both for "nothing is limited" and "cannot say". Now nothing limited
  is an empty `Limits`, and a scheduler that cannot answer returns `ExecutionLimitsDto.CannotReport`
  (`CanReport` is `false`).
* `DashboardJobQuery`, `DashboardTriggerQuery` and `DashboardHistoryQuery` derive from `PagedQuery` (`Skip`,
  `Take`, `IncludeTotalCount`, as in the job stores), and every listing returns `PagedResult<T>` with `HasMore`
  and a nullable `TotalCount`. A 1-based page is `Skip = (page - 1) * pageSize, Take = pageSize`. `JobPageDto`,
  `TriggerPageDto`, `DashboardHistoryPage`, `JobHistoryPageDto` and `JobHistoryQueryDto` are gone.
* `SchedulerStatus` is the enum the HTTP API always put on the wire, public now;
  [`IScheduler.Status`](#a-scheduler-s-lifecycle-is-one-value) reports it. A running scheduler is `Running`
  (the in-process client said `"Started"`, the HTTP-backed one `"Running"`); match the enum, not a string.
* The hub's `SchedulerStateDto` is `(string SchedulerName, string SchedulerInstanceId, SchedulerStatus Status)`
  instead of `(string SchedulerName, string State)`, pushed once per state the scheduler arrives in.
  `SchedulerStarting` pushes nothing, being an event, not a state. The instance id is new — see
  [History and live events say which node they came from](#history-and-live-events-say-which-node-they-came-from).

### The client speaks the scheduler's verbs

An operation `IQuartzApiClient` forwards to a scheduler is spelled as `IScheduler` spells it; only what has no
`IScheduler` counterpart (the scheduler listing, execution history) names itself. DTO suffixes and
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
| `Interrupt`, `InterruptFireInstance`, `DeleteJob`, `UnscheduleJob`, `DeleteCalendar` → `ValueTask` | → `ValueTask<bool>`, the flag `IScheduler` returns |

* `GetSchedulers`, `GetScheduler` and `GetCalendarNames` keep their names. The first two read the container's
  registrations, which no scheduler operation does; `GetCalendarNames` reads *every* page of
  `IScheduler.QueryCalendarNames`, so it takes no paged query.
* The five mutations that returned a bare `ValueTask` now return the applied flag (the HTTP API always sent it
  as `OperationAppliedResponse`), so all ten report it. An implementation returns what the scheduler told it,
  and a page can tell "deleted" from "was already gone".
* The three returns that were nullable, because the deleted HTTP-backed client turned a 404 into "no history",
  are not: `IDashboardHistoryStore` always answers, and an empty store returns an empty page and a count of
  zero. The History page's "execution history is unavailable" state is gone, and the overview's misfire tile
  shows a dash only until a scheduler has answered.

### A trigger is an `ITrigger`, a calendar is an `ICalendar`

Six members still used `System.Text.Json.JsonElement`, left over from the dashboard's build-out, so every
consumer had to parse JSON:

| 4.0 preview | 4.0 |
|---|---|
| `GetTrigger(…)` → `TriggerDetailDto(JsonElement Value)` | → `ITrigger` |
| `GetCalendar(…)` → `CalendarDetailDto(JsonElement Value)` | → `ICalendar` |
| `ScheduleJobRequest(JsonElement Trigger, JobDetailDto? Job)` | `ScheduleJobRequest(ITrigger Trigger, JobDetailDto? Job)` |
| `RescheduleRequest(JsonElement NewTrigger)` | `RescheduleRequest(ITrigger NewTrigger)` |
| `AddCalendarRequest(string, JsonElement Calendar, bool, bool)` | `AddCalendarRequest(string, ICalendar Calendar, bool, bool)` |
| `JobDetailDto.JobDataMap` is a `JsonElement` | a `JobDataMap` |
| `TriggerJobWithData(…, JsonElement jobDataMap, …)` | `TriggerJob(…, JobDataMap? jobDataMap = null, …)` |

* `TriggerDetailDto` and `CalendarDetailDto` are gone.
* Trigger kinds, custom ones an application registered included, are handled by Quartz's serializer registry,
  whose shape the HTTP wire format already is; a per-kind DTO family could not describe an unknown kind.
* A `JobDataMap` keeps an `int` an `int`, where a JSON round trip left it to the reader's guess.
* **The client no longer serializes anything.** It wrote the trigger to JSON and read it back, and for kinds
  the registry did not know it reflected over the trigger, producing a payload that could not be posted back. A
  custom trigger type now reaches the detail page as itself.
* `TriggerHeaderDto`, half positional and half property-initialised, is positional throughout:

```diff
- new TriggerHeaderDto(group, name, executionGroup) { TriggerType = …, ScheduleSummary = …, State = … }
+ new TriggerHeaderDto(group, name, triggerType, scheduleSummary, state, executionGroup)
```

### The dashboard reads the schedulers in its own process

`Quartz.Dashboard.Services.QuartzApiClient`, the `IQuartzApiClient` that called a Quartz HTTP API over the
network, is gone, with `QuartzDashboardOptions.BaseUrl` and `QuartzDashboardOptions.ApiPath`. Nothing resolved
it: in every 4.0 preview `AddQuartzDashboard` registered the in-process client, which reads
`ISchedulerRepository` directly. The unused one had already disagreed with it twice (`"Started"` against
`"Running"` for a running scheduler, `CronTrigger` against `Cron` for a trigger's kind).

* `AddQuartzDashboard` still registers `IQuartzApiClient` with `TryAdd`, so an implementation the application
  registers first is the one the pages read.
* A dashboard for a scheduler in another process (authentication forwarding, execution limits, a history
  endpoint no Quartz HTTP API serves) is designed in [#3387](https://github.com/quartznet/quartznet/issues/3387).
* `QuartzHttpApiOptions.ApiPath`, where the HTTP API itself is served, is a different option and unaffected;
  `AddQuartzHttpApi` still reads it.

## History and live events say which node they came from

Each cluster node keeps its own execution history and pushes its own live events, and neither said which node
it was. Both carry the node now, so the History page can attribute a row to a machine and Live Logs can tell a
local event from a peer's. History is also bounded by age as well as count.

| 4.0 preview | 4.0 |
|---|---|
| `DashboardHistoryEntry(SchedulerName, JobGroup, …)` | `DashboardHistoryEntry(SchedulerName, SchedulerInstanceId, JobGroup, …)` |
| — | `DashboardMisfireEntry(SchedulerName, SchedulerInstanceId, TriggerGroup, TriggerName, JobKeyDto? JobKey, MisfiredAtUtc, DateTimeOffset? ScheduledFireTimeUtc)` (new) |
| — | `DashboardMisfireQuery : PagedQuery`, with `SchedulerName`, `SchedulerInstanceId` and `TriggerFilter` (new) |
| `DashboardHistoryQuery` | gained `string? SchedulerInstanceId` — null lists every node's |
| `IDashboardHistoryStore` | gained `AddMisfire`, `QueryMisfires(DashboardMisfireQuery)` and `CountMisfires(name, since)` |
| `IQuartzApiClient` | gained `QueryMisfires(DashboardMisfireQuery)` → `PagedResult<DashboardMisfireEntry>` and `CountMisfires(name, since)` → `int`, which the overview's misfire tile reads |
| `QuartzDashboardOptions` | gained `TimeSpan HistoryRetention` (24 hours) and `int HistoryMaxEntriesPerScheduler` (2000) |
| `DashboardHistoryPlugin(IServiceProvider)` | `DashboardHistoryPlugin(IServiceProvider, TimeProvider)`, and it implements `ITriggerListener` as well as `IJobListener` |
| `SchedulerStateDto(SchedulerName, Status)` | `SchedulerStateDto(SchedulerName, SchedulerInstanceId, Status)` |
| `SchedulerErrorDto(SchedulerName, Message, …)` | `SchedulerErrorDto(SchedulerName, SchedulerInstanceId, Message, …)` |
| `JobEventDto(JobKey, …)`, `JobExecutionResultDto(JobKey, …)`, `TriggerEventDto(TriggerKey, …)` | each leads with `string SchedulerInstanceId` |
| `IQuartzDashboardHubClient.TriggerPaused(TriggerKeyDto)` / `TriggerResumed` | take `TriggerLifecycleDto(SchedulerInstanceId, TriggerKey)` |
| `IQuartzDashboardHubClient.JobPaused(JobKeyDto)` / `JobResumed` | take `JobLifecycleDto(SchedulerInstanceId, JobKey)` |

* **`IDashboardHistoryStore` is public and the documented persistence seam, so its three new members break any
  implementation.** A store that records no misfires answers with an empty page and a count of zero, and the
  History page says so.
* Pause and resume get payloads of their own; `JobKeyDto` / `TriggerKeyDto` are keys, used throughout
  `IQuartzApiClient`, and a key does not belong to a node.
* The two new options bound the shipped in-memory store, which was bounded by count alone, so a quiet
  scheduler's page showed executions from arbitrarily long ago. The window is measured on the scheduler's
  `TimeProvider` and applied on read as well as write.

## A scheduler can be authorized on its own name

Both management surfaces served every scheduler in the process behind one policy, so a tenant who could reach
one scheduler could reach all. Each now takes a policy evaluated per request against that request's scheduler.

| 4.0 preview | 4.0 |
|---|---|
| — | `Quartz.SchedulerResource(string SchedulerName)`, in `Quartz.AspNetCore` (new) |
| — | `QuartzHttpApiOptions.SchedulerAuthorizationPolicy` (new, `null`) |
| — | `QuartzDashboardOptions.SchedulerAuthorizationPolicy` (new, `null`) |

Left `null`, nothing changes: the API is guarded by whatever `RequireAuthorization(…)` the application put on
the mapped group, and the dashboard by `QuartzDashboardOptions.AuthorizationPolicy`.

Set one, and Quartz calls `IAuthorizationService.AuthorizeAsync(user, new SchedulerResource(name), policy)`,
ASP.NET Core's resource-based authorization. The application writes one
`AuthorizationHandler<TRequirement, SchedulerResource>`; there is no Quartz callback type or claim-name
convention.

* The API answers `403` with problem details, decided before the scheduler lookup, so a `404` only answers a
  name the caller may ask about. It filters `GET {ApiPath}/schedulers` to what the caller may act on.
* The dashboard filters its scheduler picker and Schedulers page, renders a *not authorized* frame for a
  scheduler the visitor fails for, and refuses that scheduler's live-events hub group.
* Setting either option in a container with no authorization services is refused at startup.

See [Authorizing a tenant on its own scheduler](multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler) for
the handler and the registration.

## `CheckExists` is `Exists`

Both overloads, on `IScheduler` and `IJobStore`. The HTTP API routes (`…/jobs/{group}/{name}/exists`) are
unchanged.

```diff
- if (await scheduler.CheckExists(jobKey)) { … }
+ if (await scheduler.Exists(jobKey)) { … }
```

## Names that were normalized

Renames only; behavior is unchanged.

| 3.x | 4.x | Note |
|---|---|---|
| `QuartzScheduler.NumJobsExecuted` | `NumberOfJobsExecuted` | The type is internal; read `IScheduler.GetMetadata()` |
| `QuartzScheduler.JobStoreClass`, `.ThreadPoolClass` | `JobStoreType`, `ThreadPoolType` | Return a `Type`; the type is internal |
| `JobStoreSupport.UseDBLocks`, `.SelectWithLockSQL` | `UseDbLocks`, `SelectWithLockSql` | |
| `DBSemaphore.SQL`, `.InsertSQL`, `.ExecuteSQL` | `LockSql`, `InsertSql`, `ExecuteSql` | The first two are readable now; see [The semaphores are lock handlers](#the-semaphores-are-lock-handlers) |
| `DbMetadata.Init()` | Removed | `DbMetadata` is an init-only record (below). `ParameterIsNullableProperty` is removed too |
| `DbMetadata.DbBinaryType`, `.ParameterDbTypeProperty` | Internal | Quartz derives them (below) |
| `AdoConstants.ColumnMifireInstruction` | `ColumnMisfireInstruction` | A typo; the column name is unchanged |
| `SchedulerConstants.FailedJobOriginalTriggerFiretime` | `SchedulerConstants.FailedJobOriginalTriggerFireTime` | String value unchanged |
| `SchedulerConstants.FailedJobOriginalTriggerScheduledFiretime` | `SchedulerConstants.FailedJobOriginalTriggerScheduledFireTime` | String value unchanged |
| `SchedulingOptions.OverWriteExistingData` | `OverwriteExistingData` | Key is `Quartz:Scheduling:OverwriteExistingData`; keys match case-insensitively, so files still bind, but code assigning the property must change |
| `RedisSemaphore.LockTtlMilliseconds`, `.LockRetryIntervalMilliseconds` | `RedisLockHandler.LockTimeToLive`, `.LockRetryInterval`, both `TimeSpan` | **Config keys too:** `lockTtlMilliseconds` → `lockTimeToLive`, `lockRetryIntervalMilliseconds` → `lockRetryInterval` |
| `IObjectSerializer.DeSerialize` | `Deserialize` | `IObjectSerializer.Initialize()` is removed; a serializer builds what it needs on first use |
| `TriggerFiredBundle.PrevFireTimeUtc` | `PreviousFireTimeUtc` | The type is a required-init record (below) |
| `Quartz.Plugin.Xml.XMLSchedulingDataProcessorPlugin` | `Quartz.Plugins.Xml.XmlSchedulingDataProcessorPlugin` | A `quartz.plugin.<name>.type` naming either old spelling resolves, with a warning |
| `XMLSchedulingDataProcessorPlugin.ProcessFile(fileName)` | `FileUpdated(fileName)` | The `IFileScanListener` member the file-scan job calls; `ProcessFile` is private |
| `Quartz.Xml.ValidationException` | `Quartz.SchedulingDataValidationException` | Clashed with `System.ComponentModel.DataAnnotations.ValidationException`; the JSON processor throws it too |

* **`DbMetadata`**: `UseGenericDatabase`'s describing overloads take a `Func<DbMetadata>` returning
  `new DbMetadata { … }`. Quartz derives `DbBinaryType` (`Enum.Parse` of `DbBinaryTypeName` against
  `ParameterDbType`) and `ParameterDbTypeProperty` (`GetProperty(ParameterDbTypePropertyName)` on
  `ParameterType`); those four properties stay public and unchanged.
* **`TriggerFiredBundle`** lost its eight-positional constructor, where swapping `scheduledFireTimeUtc`
  and `previousFireTimeUtc` compiled. A custom job store's `TriggerFired` writes
  `new TriggerFiredBundle { JobDetail = …, Trigger = …, Recovering = …, FireTimeUtc = …, ScheduledFireTimeUtc = …, PreviousFireTimeUtc = …, NextFireTimeUtc = … }`;
  only `Calendar` is optional.
* **`XmlSchedulingDataProcessorPlugin`**: its nested `JobFile` class and `JobFiles`, `FileNames`,
  `ScanInterval`, `FailOnFileNotFound`, `FailOnSchedulingError`, `Name` and `Scheduler` are internal — see
  [The two scheduling-data plugins have one surface](#the-two-scheduling-data-plugins-have-one-surface).
* **`SchedulingDataValidationException.ValidationExceptions`** is `IReadOnlyList<Exception>`, not
  `List<Exception>`.

### One spelling per constant

Three values had two public names. One survives; the values are unchanged.

| Removed | Use instead | Value |
|---|---|---|
| `SchedulerConstants.DefaultGroup` | `JobKey.DefaultGroup` / `TriggerKey.DefaultGroup` (both `Key<T>.DefaultGroup`) | `"DEFAULT"` |
| `AdoJobStoreOptions.DefaultTablePrefix` | `AdoConstants.DefaultTablePrefix` | `"QRTZ_"` |
| `TaskSchedulingThreadPool.DefaultMaxConcurrency` (`protected`) | `ThreadPoolOptions.DefaultMaxConcurrency` | `10` |

### Abbreviated parameter names were spelled out

Only named arguments and overriding signatures break; positional calls compile unchanged.

* Across the public surface: `cal` → `calendar`, `sched` → `scheduler`, `schedName` → `schedulerName`,
  `calName` → `calendarName`, `schedInstId` → `schedulerInstanceId`, `triggerInstCode` / `instCode` →
  `triggerInstructionCode` / `instructionCode`, `jec` → `context`, `prevFireTimeUtc` →
  `previousFireTimeUtc`, `je` → `jobExecutionException`, and `tz` / `timezone` → `timeZone` on the
  `InTimeZone` schedule-builder methods.
* The constructor of [`SchedulerMetadata`](#schedulermetadata-replaces-schedulermetadata) (was
  `SchedulerMetaData`): `schedInst` → `schedulerInstanceId`, `schedType` → `schedulerType`,
  `numberOfJobsExec` → `numberOfJobsExecuted`, `jsType` → `jobStoreType`, `jsPersistent` →
  `jobStoreSupportsPersistence`, `jsClustered` → `jobStoreClustered`, `tpType` → `threadPoolType`,
  `tpSize` → `threadPoolSize`.

## Matchers moved to `Quartz`

`GroupMatcher<T>`, `NameMatcher<T>`, `KeyMatcher<T>`, `EverythingMatcher<T>`, `AndMatcher<T>`,
`OrMatcher<T>`, `NotMatcher<T>`, `StringMatcher<T>` and `StringOperator` moved to `Quartz`, where every
signature that takes them already is. `GroupMatcher<T>` and `NameMatcher<T>` keep their factory methods
(`GroupEquals`, `NameStartsWith`, `AnyGroup`, …).

```diff
- using Quartz.Impl.Matchers;
```

`IMatcher<T>` no longer redeclares `Equals(object)` and `GetHashCode()`. A matcher should still compare
and hash by its shape, not its identity.

### `Matchers` is the entry point; combinators are extensions

The roots are on the non-generic `Matchers` class; `And`, `Or` and `Not` are extension methods on any
`IMatcher<TKey>`. (Some 3.x statics ignored their class's type parameter:
`EverythingMatcher<JobKey>.AllTriggers()` compiled and matched trigger keys.)

```csharp
IMatcher<JobKey> matcher = Matchers.Group<JobKey>(StringOperator.StartsWith, "reporting")
    .And(Matchers.Name<JobKey>(StringOperator.Contains, "cleanup").Not());

scheduler.ListenerManager.AddJobListener(listener, Matchers.AllJobs());
```

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `EverythingMatcher<JobKey>.AllJobs()` | `Matchers.AllJobs()` |
| `EverythingMatcher<TriggerKey>.AllTriggers()` | `Matchers.AllTriggers()` |
| `EverythingMatcher<TKey>.All()` (4.0 preview) | `Matchers.AllJobs()` / `Matchers.AllTriggers()`; the factory is internal |
| `NameMatcher<TKey>.AnyName()` (4.0 preview) | Leave the filter null; every property that takes one is nullable |
| `KeyMatcher<JobKey>.KeyEquals(key)` | `Matchers.Key(key)` (overloaded for `JobKey` and `TriggerKey`) |
| `AndMatcher<JobKey>.And(left, right)` | `left.And(right)` |
| `OrMatcher<JobKey>.Or(left, right)` | `left.Or(right)` |
| `NotMatcher<JobKey>.Not(matcher)` | `matcher.Not()` |
| — | `Matchers.Group<TKey>(StringOperator, string)`, `Matchers.Name<TKey>(StringOperator, string)` |

The concrete matcher types stay public; the expressions return them. Both ways to build a
`GroupMatcher` or `NameMatcher` stay:

* the factory on the type (`GroupEquals`, `NameStartsWith`, …) **names its comparison**, for a call site
  that knows it;
* the root on `Matchers` takes a `StringOperator` **value**, for a comparison read from configuration or
  the wire (the HTTP API builds every matcher this way).

Roots that name no comparison (`Matchers.AllJobs()`, `Matchers.AllTriggers()`, `Matchers.Key(key)`) exist
only on `Matchers`, so `EverythingMatcher<TKey>.All()` and `KeyMatcher<TKey>.KeyEquals()` are gone.

### `StringOperator` exposes properties and a name

* The five built-in operators (`Equality`, `StartsWith`, `EndsWith`, `Contains`, `Anything`) are static
  get-only properties; they were `public static readonly` fields. Call sites compile unchanged.
* The new abstract `Name` identifies the operator across a process boundary and is what `ToString()`
  returns. A custom `StringOperator` must implement `Name` as well as `Evaluate`.

## `Key<T>` moved to `Quartz` and is immutable

`Key<T>`, the base of `JobKey` and `TriggerKey`, moved out of the utility namespace:

```diff
- using Quartz.Util;   // for Key<T>
+ // Key<T> is in Quartz, alongside JobKey and TriggerKey
```

The `Name` and `Group` setters are gone: a key mutated in place could strand its job-store dictionary
entry. Build a new key:

```diff
- jobKey.Group = "reports";
+ jobKey = new JobKey(jobKey.Name, "reports");
```

`JobKey.Create` is gone; use the constructor, as `TriggerKey` always did:

```diff
- JobKey key = JobKey.Create("myJob", "reports");
+ JobKey key = new JobKey("myJob", "reports");
```

* Earlier payloads still read: both serializers build a key through its `(name, group)` constructor, and
  the JSON is unchanged.
* `JobType`, the type of `IJobDetail.JobType`, moved from `Quartz.Impl` to `Quartz` too.
* **Keys sort now.** `JobKey` / `TriggerKey` implement `IComparable<JobKey>` / `IComparable<TriggerKey>`
  and `Key<T>` implements `IComparable`. 3.x had only `IComparable<Key<T>>`, which
  `Comparer<JobKey>.Default` ignores, so `keys.Sort()`, `keys.OrderBy(k => k)`, `SortedSet<JobKey>` and
  `SortedDictionary<JobKey, _>` compiled and threw at run time.
* **The sort order changed.** The default group still sorts first, but group and name compare with
  `StringComparer.Ordinal`, not 3.x's `CultureInfo.CurrentCulture.CompareInfo`. Upper-case letters now
  sort before all lower-case ones, whatever the machine's culture.

## Listing queries can filter by name

| Query | New property |
|---|---|
| `JobQuery` | `NameMatcher<JobKey>? Name` |
| `TriggerQuery` | `NameMatcher<TriggerKey>? Name` |
| `JobGroupQuery` | `NameMatcher? Name` |
| `TriggerGroupQuery` | `NameMatcher? Name` |
| `CalendarQuery` | `NameMatcher? Name` |

```csharp
PagedResult<JobHeader> nightly = await scheduler.QueryJobs(new JobQuery
{
    Group = GroupMatcher<JobKey>.GroupEquals("reports"),
    Name = NameMatcher<JobKey>.NameStartsWith("nightly")
});
```

* The filters combine with AND.
* `RAMJobStore` and `StdAdoDelegate` both honor them. The ADO store escapes the matcher's own wildcards in
  its `LIKE`, so a job named `50%` matches literally.
* Over HTTP, every listing (jobs, triggers, calendars **and both group listings**) takes at most one of
  `nameEquals`, `nameStartsWith`, `nameEndsWith` or `nameContains`.

### One name filter, in two arities

A calendar or group has a bare name, not a `Key<T>`, so its filter is `NameMatcher`: the four factories,
wire spellings and escaping of `NameMatcher<TKey>`, without the type parameter.

```csharp
PagedResult<string> holidays = await scheduler.QueryCalendarNames(new CalendarQuery
{
    Name = NameMatcher.NameStartsWith("holiday-")
});

PagedResult<JobGroup> tenant = await scheduler.QueryJobGroups(new JobGroupQuery
{
    Name = NameMatcher.NameStartsWith("tenant-42-")
});
```

Two changes from the 4.0 previews:

* **`CalendarNameMatcher` is `NameMatcher`**, now that group listings use it too.
* **`JobGroupQuery.Name` and `TriggerGroupQuery.Name` were `string`, matched exactly.** As matchers they
  filter by prefix, suffix or substring. `NameMatcher.NameEquals(group)` is the old behavior and still
  generates `=`, not `LIKE`.

Also:

* Neither arity has `AnyName()`; null means every name. `GroupMatcher<TKey>.AnyGroup()` stays, because
  `PauseTriggerGroups`, `DeleteJobs` and similar members do not take null.
* A dialect delegate overriding `StdAdoDelegate.ToSqlLikeClause<T>(StringMatcher<T>)` must override
  `ToSqlLikeClause(StringOperator, string)` instead, now the virtual one; the generic form forwards to it.
* `IsJobGroupPaused` and `IsTriggerGroupPaused` query the one group (`NameMatcher.NameEquals`,
  `Take = 1`) instead of listing every paused group.
* `PagedResult<T>.Items` is `IReadOnlyList<T>`, not `List<T>`. The two `List<T>` members most likely in
  use:

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

Existing calls compile. A null instance ID means what the one-argument overload did: bind under the
scheduler's own `SchedulerInstanceId`, or look up or remove the first scheduler registered under the name.
`LookupAll` and `LookupByName`, which clusters use to disambiguate, are unchanged.

## Trigger fire state is read-only on the interfaces

```diff
- int Priority { get; set; }   // ITrigger
+ int Priority { get; }

- int TimesTriggered { get; set; }   // ISimpleTrigger, ICalendarIntervalTrigger,
+ int TimesTriggered { get; }        // IDailyTimeIntervalTrigger, IRecurrenceTrigger
```

* `IMutableTrigger.Priority` keeps its setter; `TriggerBuilder.WithPriority` and the job stores use it.
* `SimpleTriggerImpl`, `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and
  `RecurrenceTriggerImpl` keep a settable `TimesTriggered`.
* A trigger from `IScheduler.GetTrigger` is a snapshot, so writing either never reached the store. To
  change a stored trigger, build a new one and reschedule.

The built-in JSON trigger serializers are typed on the concrete triggers, because they restore
`TimesTriggered`. A serializer deriving from one follows:

```diff
- public class MySimpleTriggerSerializer : TriggerSerializer<ISimpleTrigger>
+ public class MySimpleTriggerSerializer : TriggerSerializer<SimpleTriggerImpl>
```

This applies to `SimpleTriggerSerializer`, `CalendarIntervalTriggerSerializer`,
`DailyTimeIntervalTriggerSerializer` and `RecurrenceTriggerSerializer`, in both
`Quartz.Serialization.SystemTextJson.Triggers` and `Quartz.Serialization.Newtonsoft.Triggers`.
`CronTriggerSerializer` is unchanged; it has no fire count to restore.

## The trigger family interfaces are read models

The remaining schedule setters are gone from the five family interfaces. Setting one on a trigger from the
scheduler compiled and changed nothing: `RAMJobStore.GetTrigger` returns `Trigger.Clone()`, and the ADO.NET
store builds a fresh instance per read.

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

Change a stored trigger with one of the two existing APIs:

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

* `TriggerBase`, the concrete triggers and `IMutableTrigger` (what job stores and custom triggers write
  through) keep their setters.
* `ICalendar` keeps its `Description` and `CalendarBase` setters. It is an implementable SPI, and the
  built-in calendar serializers assign through the interface.

**`ITrigger` no longer implements `IComparable<ITrigger>`**, nor do the five family interfaces that
re-declared it. It compared keys; compare `Key` instead, which has the full comparison operator set:

```diff
- if (a.CompareTo(b) < 0) { … }
+ if (a.Key.CompareTo(b.Key) < 0) { … }

- triggers.Sort();                       // List<ITrigger>, now throws InvalidOperationException
+ triggers.Sort((x, y) => x.Key.CompareTo(y.Key));
```

The second shape still **compiles** and throws at run time: `List<ITrigger>.Sort()`, `SortedSet<ITrigger>`,
`OrderBy(t => t)` and anything else that uses `Comparer<ITrigger>.Default`. To order by fire time, sort on
`NextFireTimeUtc`. `TriggerBase.CompareTo(ITrigger)` is gone too.

**A custom `ITriggerPersistenceDelegate`:** `TriggerPropertyBundle` no longer carries the
`StatePropertyNames` / `StatePropertyValues` arrays applied by reflection. It takes the schedule builder
and an optional applier delegate, checked by the compiler:

```diff
- return new TriggerPropertyBundle(sb, ["timesTriggered"], [timesTriggered]);
+ return new TriggerPropertyBundle(sb, t => ((SimpleTriggerImpl) t).TimesTriggered = timesTriggered);
```

* The single-argument constructor is unchanged; a null applier is skipped (the Cron delegate passes none).
* The lambda casts to the concrete trigger because the family interfaces expose `TimesTriggered`
  get-only. The four `Quartz.Impl.Triggers` classes stay public with public `TimesTriggered` setters for
  this.

Register the delegate on the store builder:

```csharp
services.AddQuartz(q => q.UsePersistentStore(store =>
{
    store.UseTriggerPersistenceDelegate<MyTriggerPersistenceDelegate>();
    // or, when it needs configuring first:
    store.UseTriggerPersistenceDelegate(provider => new MyTriggerPersistenceDelegate(...));
}));
```

The delimited `quartz.jobStore.driverDelegateInitString` format (split on `|` or `\`, type names
instantiated by reflection) is gone from the API:

* `DelegateInitializationArgs.InitString` became the typed `DriverDelegateContext.TriggerPersistenceDelegates`
  collection. `AdoJobStoreOptions.DriverDelegateInitString` and its `AdoJobStoreBase` mirror are removed.
* **The legacy key still works.** The property bridge translates
  `quartz.jobStore.driverDelegateInitString = triggerPersistenceDelegateTypes=...` (and the older
  `triggerPersistenceDelegateClasses` spelling, with both of its list separators) into the registrations
  `UseTriggerPersistenceDelegate<T>()` makes. A misspelled setting inside the string fails at `AddQuartz`,
  not at store startup.

## `CronExpression` is immutable

`CronExpression` is a **`sealed` immutable value**. It was an open class with a settable `TimeZone` and a
`protected` parser. The time zone comes from a constructor or from `WithTimeZone`, which returns a copy.

| 3.x | 4.x |
|---|---|
| `expr.TimeZone = tz;` | `expr = expr.WithTimeZone(tz);` |
| `new CronExpression(s) { TimeZone = tz }` | `new CronExpression(s, tz)` |
| `expr.GetTimeAfter(d)` | `expr.GetNextValidTimeAfter(d)`; the two were aliases |
| `expr.GetFinalFireTime()` | Removed; never implemented, always returned `null` |
| `expr.Clone()` | Removed; reuse the instance |
| `CronExpression.MaxYear` | Removed. It was `DateTime.Now.Year + 100`, fixed when the process started. The cap is internal; a schedule that must stop at a year should say so |

* `null` still means the local time zone, and an expression with no zone still serializes the local
  zone's id, so persisted payloads keep their meaning.
* **Fixed:** `CronScheduleBuilder` gave the *same* `CronExpression` to every trigger it built, so
  `InTimeZone` after the first `Build()` retimed triggers already built, and setting `TimeZone` on an
  expression passed to a builder retimed the builder. Now the builder changes its own copy, and built
  triggers keep their zone.
* **Fixed ([#3321](https://github.com/quartznet/quartznet/issues/3321)):**
  `new CronCalendar(baseCalendar, expression, timeZone)` built the expression without the zone, so the
  calendar excluded the *local* machine's hours and reported the local zone. It now uses the zone.
  Assigning `TimeZone` after construction always worked. The stored form is unchanged (the zone was always
  persisted with the nested expression), so **no migration is needed**; only which times are excluded
  changes, to what was asked for.
* `CronTriggerImpl.FinalFireTimeUtc` returns `null` directly for a trigger with no end time, the value it
  always produced through `GetFinalFireTime`.
* `CronCalendar.Clone` and `CronTriggerImpl.Clone` share the expression instead of cloning it. If you
  called `Clone()`, drop the call.

### The parser is not a subclassing seam

3.x's open `CronExpression` handed a derived type its whole parser. The type is `sealed` now, so all of
this is private and its public members are no longer `virtual`:

* eleven `protected const` field indices: `Second`, `Minute`, `Hour`, `DayOfMonth`, `Month`, `DayOfWeek`,
  `Year`, `AllSpec`, `AllSpecInt`, `NoSpec`, `NoSpecInt`;
* thirteen `protected` fields holding the parsed sets: `seconds`, `minutes`, `hours`, `daysOfMonth`,
  `months`, `daysOfWeek`, `years`, `lastdayOfWeek`, `nthdayOfWeek`, `everyNthWeek`, `calendardayOfWeek`,
  `calendardayOfMonth`, `expressionParsed`;
* twelve `protected virtual` parse and arithmetic hooks: `AddToSet`, `BuildExpression`, `CheckNext`,
  `GetExpressionSetSummary`, `GetLastDayOfMonth`, `GetSet`, `GetTime`, `IsLeapYear`, `SkipWhiteSpace`,
  `StoreExpressionVals`, `CreateDateTimeWithoutMillis`, `SetCalendarHour`.

An override could leave an expression that parsed to something its own string does not say. To vary cron
syntax, produce a Quartz expression string (`CronExpressionBuilder` builds one field by field) or write a
trigger type of your own.

`CronExpression` no longer implements `IDeserializationCallback`, so `OnDeserialization(object?)` is gone.
It keeps `[Serializable]`, `ISerializable` and `GetObjectData`, because a `CronCalendar` in a 3.x
`CALENDARS` blob contains one — see
[`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it).
The deserialization constructor now does the re-parse. 3.x blobs still read: the payload is the expression
string and the time zone id, unchanged.

### `CronExpression` parses without throwing, and says it is equatable

`CronExpression` has `TryParse` and `Parse` and implements `IParsable<CronExpression>`, as `JobKey` and
`TriggerKey` do.

```csharp
if (CronExpression.TryParse(userInput, out CronExpression? expression))
{
    // expression is non-null here
}

CronExpression parsed = CronExpression.Parse(userInput);   // ArgumentNullException / FormatException
```

**All four validation members are gone:**

| 3.x | 4.0 |
|---|---|
| `CronExpression.IsValidExpression(expr)` | `CronExpression.TryParse(expr, out _)` |
| `CronExpression.ValidateExpression(expr)` | `CronExpression.Parse(expr)` |
| `CronExpression.IsValidExpression(expr, hashKey)` | `CronExpression.TryParseWithHash(expr, hashKey, out _)` |
| `CronExpression.ValidateExpression(expr, hashKey)` | `CronExpression.ParseWithHash(expr, hashKey)` |

* The replacements return the parsed expression, so validating and then using it is one parse. For a
  check only, discard the result (`out _`, or `Parse`'s return value).
* There are deliberately no `ReadOnlySpan<char>` overloads: the type keeps its source string, so a span
  would be copied into one anyway.
* `CronExpression` declares `IEquatable<CronExpression>`, and `Equals` takes a nullable argument.
  `GetHashCode` hashes the `TimeZone` property that `Equals` compares, so an expression with no zone and
  one given `TimeZoneInfo.Local` no longer hash differently while comparing equal.

### The hash key is a `Parse` argument, not a constructor overload

3.x resolved `H` (hash) tokens through `CronExpression(string, string hashKey)` and
`CronExpression(string, int hashSeed)`. Beside `CronExpression(string, TimeZoneInfo?)` they made `null`
ambiguous, so `new CronExpression(expr, null)`, the documented way to ask for the local zone, did not
compile. Hash resolution moved to statics beside `Parse`:

```csharp
CronExpression expression = CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup");
CronExpression seeded = CronExpression.ParseWithHash("0 H H(0-7) * * ?", 42);

if (CronExpression.TryParseWithHash(userInput, "nightly-cleanup", out CronExpression? parsed))
{
    // parsed is non-null here
}

CronExpression local = new CronExpression("0 H H(0-7) * * ?", null);   // compiles now
```

`ParseWithHash` reads the expression and throws `FormatException` for a malformed one, as `Parse` does.
`CronExpression.ResolveHash`, which returns the resolved string, is unchanged.

### The derived calendars declare the equality they implement

`AnnualCalendar`, `CronCalendar`, `DailyCalendar`, `HolidayCalendar`, `MonthlyCalendar` and
`WeeklyCalendar` each declare `IEquatable<X>`, and their `public bool Equals(X obj)` takes a nullable
argument, as `BaseCalendar`'s always did. Existing calls compile.

All six are `sealed`. A calendar of your own derives from `BaseCalendar`, which stays open; add the `?` to
its `Equals` parameter — see [The shipped implementations are sealed](#the-shipped-implementations-are-sealed).

### `CronExpressionBuilder`'s list fields take a span

`WithSeconds`, `WithMinutes`, `WithHours`, `WithDaysOfMonth`, `WithMonths`, `WithDaysOfWeek` and
`WithYears` gained `params ReadOnlySpan<…>` overloads, so a call no longer allocates an array. The
`params` array overloads stay, for an argument that is already a collection. Call sites do not change:
C# 13 prefers the span overload in expanded form, and both behave the same.

### `ITrigger` is Quartz-implemented

Build triggers with `TriggerBuilder`. A custom trigger type derives from `TriggerBase`, which carries the
mutable and operational contracts the scheduler and stores need. An object that implements only
`ITrigger` cannot be scheduled: `ScheduleJob` and `RescheduleJob` reject it with a `SchedulerException`
that names the type, where they used to fail with an `InvalidCastException` inside the scheduler.

`IJobStore.ScheduleJobs` takes `IReadOnlyDictionary<IJobDetail, IReadOnlyCollection<IOperableTrigger>>`.
The scheduler validates and downcasts the triggers first, so a custom store no longer casts.
`IScheduler.ScheduleJobs` is unchanged.

## The trigger implementations construct, then initialize

The five `*TriggerImpl` types had thirty-four constructors, up to nine positional arguments long, for
values that are all settable properties. Each now has one constructor taking an optional `TimeProvider`,
at most one convenience constructor, and object initializers for the rest.

| Type | Constructors before | Constructors now |
|---|---|---|
| `SimpleTriggerImpl` | 11 | `(TimeProvider? = null)`, and `(name, group, jobName, jobGroup, startTimeUtc, endTimeUtc, repeatCount, repeatInterval, TimeProvider? = null)` |
| `CronTriggerImpl` | 9 | `(TimeProvider? = null)`, and `(name, group, cronExpression, TimeProvider? = null)` |
| `CalendarIntervalTriggerImpl` | 6 | `(TimeProvider? = null)` |
| `DailyTimeIntervalTriggerImpl` | 6 | `(TimeProvider? = null)` |
| `RecurrenceTriggerImpl` | 2 | `(TimeProvider? = null)`, and `(name, group, recurrenceRule, TimeProvider? = null)` |
| `TriggerBase` (protected) | 5 | `(TimeProvider? = null)` |

* Every convenience constructor takes a **non-nullable** `group`. `new RecurrenceTriggerImpl(name, null, rule)`
  is now a nullability warning (an error under `TreatWarningsAsErrors`): name the group,
  use `TriggerKey.DefaultGroup`, or use the initializer and set `Key`.
* To convert, the name and group become `Key`, the job name and group become `JobKey`, and the rest keep
  their property names:

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

* Set `StartTimeUtc` before `EndTimeUtc`; each setter validates against the other, as the constructors
  did.
* The overloads without a start time (`SimpleTriggerImpl(name)`, `(name, group)`,
  `(name, repeatCount, repeatInterval)`, `(name, group, repeatCount, repeatInterval)`, and the
  `CalendarIntervalTriggerImpl` / `DailyTimeIntervalTriggerImpl` equivalents) set `StartTimeUtc` to *now*.
  Write that out if you relied on it:

```diff
- var trigger = new SimpleTriggerImpl("nightly", "reports");
+ var trigger = new SimpleTriggerImpl
+ {
+     Key = new TriggerKey("nightly", "reports"),
+     StartTimeUtc = TimeProvider.System.GetUtcNow()
+ };
```

* `CronTriggerImpl`'s constructors still set `StartTimeUtc` to the time provider's now and `TimeZone` to
  `TimeZoneInfo.Local`.
* `SimpleTriggerImpl` and `CalendarIntervalTriggerImpl` merged their parameterless and `TimeProvider`
  constructors into `(TimeProvider? timeProvider = null)`, as `CronTriggerImpl` and
  `DailyTimeIntervalTriggerImpl` already had. `new SimpleTriggerImpl()` compiles, but a `where T : new()`
  constraint or `Activator.CreateInstance(type)` no longer works: the runtime does not treat an
  all-optional constructor as parameterless. A derived type gets an implicit parameterless constructor,
  which satisfies both.
* Persisted binary payloads are unaffected; the `[Serializable]` blob contract pins fields, not
  constructors.

## Nine `UsingJobData` overloads became one

The nine primitive `UsingJobData` overloads all wrote through `JobDataMap`'s `object?` indexer. On
`IJobConfigurator<TJob>`, `JobBuilder<TJob>`, `ITriggerConfigurator<TJob>` and `TriggerBuilder<TJob>` they
are now one `UsingJobData(string key, object? value)`.

| 3.x | 4.x |
|---|---|
| `UsingJobData(string key, string? value)` and the `int`, `long`, `float`, `double`, `decimal`, `bool`, `Guid` and `char` overloads | `UsingJobData(string key, object? value)` |
| `UsingJobData(JobDataMap newJobDataMap)` | Unchanged; merges into what the builder already holds |
| `UsingJobData<TValue>(Expression<Func<TJob, TValue>> jobProperty, TValue value)` | Unchanged |
| `SetJobData(JobDataMap newJobDataMap)` | Removed |

**Existing calls compile and store what they stored before.** An `int` is boxed as an `int`, a `Guid` as a
`Guid`, a `null` as a `null`; nothing is converted. A persistent store still holds whatever its serializer
round-trips (strings only, in AdoJobStore's string-only mode).

To store a value in the job property's own type (an `int` literal narrowed to a `byte` property, an enum
written as its name), name the property instead of its key:

```csharp
JobBuilder.Create<MyJob>().UsingJobData(job => job.RetryCount, 3)
```

### `SetJobData` is gone

`SetJobData` *replaced* the builder's map, where `UsingJobData(JobDataMap)` merges into it. Only a job store
rebuilding a stored job needs to replace, so `SetJobData` is internal.

```diff
- JobBuilder.Create<MyJob>().UsingJobData("a", 1).SetJobData(map)   // "a" silently discarded
+ JobBuilder.Create<MyJob>().UsingJobData(map)                      // merge, or start from a fresh builder
```

## One family of `WithXSchedule` extensions

`Quartz.TriggerConfiguratorExtensions` replaces six extension classes and twenty-nine methods with twelve.
Each is generic in the receiver and returns it, so one method serves both `TriggerBuilder<TJob>` and
`ITriggerConfigurator<TJob>`, and the chain keeps its type.

Deleted: `SimpleScheduleTriggerBuilderExtensions`, `CronScheduleTriggerBuilderExtensions`,
`CalendarIntervalTriggerBuilderExtensions`, `DailyTimeIntervalTriggerBuilderExtensions`,
`RecurrenceTriggerBuilderExtensions`, `TriggerExtensions`.

| Schedule | The 4.x methods |
|---|---|
| Simple | `WithSimpleSchedule(Action<SimpleScheduleBuilder>? configure = null)`, `WithSimpleSchedule(SimpleScheduleBuilder schedule)` |
| Cron | `WithCronSchedule(string cronExpression, Action<CronScheduleBuilder>? configure = null)`, `WithCronSchedule(CronScheduleBuilder schedule)` |
| Cron, new | `WithCronSchedule(CronExpression cronExpression, Action<CronScheduleBuilder>? configure = null)`, which takes a hash-keyed expression |
| Cron, new | `WithCronSchedule(CronExpressionBuilder cronExpression, Action<CronScheduleBuilder>? configure = null)`, which ends a [`CronExpressionBuilder`](#cronschedulebuilder-s-convenience-factories-are-gone) chain |
| Calendar interval | `WithCalendarIntervalSchedule(Action<CalendarIntervalScheduleBuilder>? configure = null)`, `WithCalendarIntervalSchedule(CalendarIntervalScheduleBuilder schedule)` |
| Daily time interval | `WithDailyTimeIntervalSchedule(Action<DailyTimeIntervalScheduleBuilder>? configure = null)`, `WithDailyTimeIntervalSchedule(DailyTimeIntervalScheduleBuilder schedule)` |
| Recurrence | `WithRecurrenceSchedule(string recurrenceRule, Action<RecurrenceScheduleBuilder>? configure = null)`, `WithRecurrenceSchedule(RecurrenceScheduleBuilder schedule)` |

Every 3.x call shape binds to one of these except three:

| 3.x | 4.x |
|---|---|
| `WithCronSchedule(string expr, string hashKey)` | `WithCronSchedule(CronExpression.ParseWithHash(expr, hashKey))` |
| `WithCronSchedule(string expr, string hashKey, Action<CronScheduleBuilder>)` | `WithCronSchedule(CronExpression.ParseWithHash(expr, hashKey), configure)` |
| `WithDailyTimeIntervalSchedule(int interval, IntervalUnit unit, Action<…>? action = null)` | `WithDailyTimeIntervalSchedule(x => x.WithInterval(interval, unit))` |

```diff
- .WithDailyTimeIntervalSchedule(interval: 10, intervalUnit: IntervalUnit.Second)
+ .WithDailyTimeIntervalSchedule(x => x.WithInterval(10, IntervalUnit.Second))

- .WithCronSchedule("0 H H(0-7) * * ?", "nightly-cleanup")
+ .WithCronSchedule(CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup"))
```

A hash key belongs to the expression: pass it to
[`CronExpression.ParseWithHash`](#the-hash-key-is-a-parse-argument-not-a-constructor-overload) and hand the
result to the `CronExpression` overload. Without a key, `H` tokens still hash on the trigger's identity.

### `ITriggerConfigurator` gained a non-generic base

The extensions target a new non-generic `ITriggerConfigurator`, which holds the one member they need:

```csharp
public interface ITriggerConfigurator
{
    ITriggerConfigurator WithSchedule(IScheduleBuilder scheduleBuilder);
}

public interface ITriggerConfigurator<TJob> : ITriggerConfigurator where TJob : IJob { … }
```

`ITriggerConfigurator<TJob>` redeclares `WithSchedule` with its own return type, so a chain keeps `TJob` and
the job-property `UsingJobData` overload. Implementations of `ITriggerConfigurator<TJob>` are unaffected;
`TriggerBuilder<TJob>` implements both.

## `ModifiedByCalendar` is `WithCalendarName`

```diff
  ITrigger trigger = TriggerBuilder.Create()
      .WithIdentity("trigger1")
-     .ModifiedByCalendar("myHolidays")
+     .WithCalendarName("myHolidays")
      .Build();
```

It sets `ITrigger.CalendarName`, and every builder setter is named for the property it sets; the old name
also read as if it modified the calendar. Applies to `TriggerBuilder<TJob>` and
`ITriggerConfigurator<TJob>`.

## The preferred node is a value

`Quartz.PreferredNode`, a `readonly record struct`, replaces a `string?` (where `"*"` meant auto) and a
`bool` flag that meant nothing without it. Copying a pin through the old setter dropped the flag and
turned an auto-claim into a named pin that never failed over.

| 3.x | 4.x |
|---|---|
| `string? ITrigger.PreferredNode` | `PreferredNode ITrigger.PreferredNode` |
| `bool ITrigger.IsPreferredNodeAuto` | `trigger.PreferredNode.IsAutomatic` |
| `trigger.PreferredNode` (the node name) | `trigger.PreferredNode.Node`; null for no pin *and* for an unclaimed auto-pin |
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

* `PreferredNode.For` trims its argument and rejects a blank name and the protocol's markers `*`, `_` and
  `null`, which used to be accepted and then mean something else. Any other name is stored verbatim, so
  `auto:thing` or `*-west` is fine.
* Assigning a pin keeps it as it was, auto-claim included, so copying one between triggers is lossless.
* **Storage is unchanged.** `QRTZ_TRIGGERS.PREFERRED_NODE` and `PREFERRED_NODE_AUTO` still hold the string
  and the flag; the sentinel is an internal constant. Databases written by 3.19's node-affinity migration
  or a 4.0 preview read back the same, and JSON trigger payloads never carried the pin.

## Misfire instructions are enums

The eighteen per-policy methods across five builders are now one `WithMisfireInstruction` per builder,
taking the family's enum. Unlike a method name, an enum value can be read from configuration, switched on
and defaulted.

```diff
  .WithSimpleSchedule(x => x
      .WithInterval(TimeSpan.FromMinutes(5))
      .RepeatForever()
-     .WithMisfireHandlingInstructionNextWithExistingCount())
+     .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount))
```

Earlier 4.0 previews spelled it `WithMisfireHandlingInstruction`. It is `WithMisfireInstruction` on all
five builders, matching `TriggerDetailsUpdate.WithMisfireInstruction` and the XML element name.

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

The single `WithMisfireInstruction(int)` let a simple trigger's code be applied to a cron trigger, where the
same number means a different policy. There is now one overload per family, plus
`WithMisfireInstructionCode` for a number with no family (read off the wire, from configuration or from a
trigger).

| 4.0 preview | 4.0 |
|---|---|
| `.WithMisfireInstruction(2)` | `.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |
| `.WithMisfireInstruction(MisfireInstruction.CronTrigger.DoNothing)` | `.WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)` |
| `.WithMisfireInstruction(someInt)` | `.WithMisfireInstructionCode(someInt)` |

Prefer the typed overloads: the store rejects an update whose family is not the stored trigger's, so a
cron policy sent to a simple trigger is an error rather than a different policy.
`WithMisfireInstructionCode` keeps only the trigger's own range check.

### The enums are the vocabulary

Read a trigger's policy, typed, from its family interface. The family-agnostic number stays on `ITrigger`
under a new name, and `IMutableTrigger` keeps the settable one.

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

An enum member's value *is* the code, so the two convert freely. The numbers in
`QRTZ_TRIGGERS.MISFIRE_INSTR` and in JSON trigger payloads are unchanged.

**The `MisfireInstruction` constant class is internal.** The enums cover all five families:

| 3.x | 4.x |
|---|---|
| `MisfireInstruction.CronTrigger.DoNothing` | `CronTriggerMisfireInstruction.DoNothing` |
| `MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount` | `SimpleTriggerMisfireInstruction.NowWithExistingCount` |
| `MisfireInstruction.IgnoreMisfirePolicy` | The family's `IgnoreMisfires` |
| `MisfireInstruction.SmartPolicy` | The family's `SmartPolicy` |

### The XML and JSON names are resolved per family

Both scheduling-data readers used to resolve a misfire instruction name against every family's constants
at once. In JSON, which has no schema, a cron trigger with
`"MisfireInstruction": "RescheduleNowWithExistingRepeatCount"` silently became `DoNothing` (both are 2).
Explicit per-family maps replace the reflection:

* Every name that parsed still parses, and each family also accepts its own enum member names, so
  `FireAndProceed` and `NowWithExistingCount` work in configuration too.
* Another family's name still resolves when its code is legal for this family, with a warning naming the
  policy it selects.
* A name whose code is out of the family's range is rejected with a message listing the valid names. It
  used to fail later with "The misfire instruction code is invalid for this type of trigger".

The XML processor's `ReadMisfireInstructionFromString` was `protected virtual` and is now private, and the
processor itself is not public. XML itself is unaffected: `job_scheduling_data_2_0.xsd` restricts
`misfire-instruction` per trigger type, so the schema rejects a foreign name first.

## Every builder starts with `Create`

Every builder starts with a static `Create(...)` that takes only what cannot be defaulted.

| 3.x | 4.x |
|---|---|
| `CronScheduleBuilder.CronSchedule(string)` | `CronScheduleBuilder.Create(string)` |
| `CronScheduleBuilder.CronSchedule(CronExpression)` | `CronScheduleBuilder.Create(CronExpression)` |
| `DateBuilder.NewDate()` | `DateBuilder.Create()` |
| `DateBuilder.NewDateInTimeZone(tz)` | `DateBuilder.CreateInTimeZone(tz)` |
| `JobBuilder.Create(Type)` | `JobBuilder.Create().OfType(type)` |

* `ExecutionLimitsBuilder`, new in 4.0, follows suit: `new ExecutionLimitsBuilder()` becomes
  `ExecutionLimitsBuilder.Create()`.
* `JobBuilder.Create(Type)` duplicated `OfType(Type)`. For a job type known only at run time (from
  configuration, a database row or a message):

```csharp
IJobDetail job = JobBuilder.Create().OfType(jobType).WithIdentity(name).Build();
```

* `JobBuilder.Create()` / `Create<TJob>()` and `TriggerBuilder.Create()` / `Create<TJob>()` are unchanged;
  the generic split carries the job type — see [The builders carry the job type](#the-builders-carry-the-job-type).

## Intervals are said once per builder

The per-unit `WithIntervalIn<Unit>` methods are gone; use `WithInterval`. The two signatures differ on
purpose: a `TimeSpan` is a fixed length, while `1, IntervalUnit.Month` is however long the next month is.

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

## `SimpleScheduleBuilder`'s twelve `Repeat*` factories are gone

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
Mind the `- 1` in the `ForTotalCount` rows. The trigger also fires at its start time, so the repeat count
is one less than the number of firings: `RepeatMinutelyForTotalCount(3)` fires three times, which is a
repeat count of 2.
:::

Inside a `WithSimpleSchedule` delegate you never needed the factories:

```diff
- .WithSchedule(SimpleScheduleBuilder.RepeatMinutelyForever(5))
+ .WithSimpleSchedule(x => x.WithInterval(TimeSpan.FromMinutes(5)).RepeatForever())
```

The interval overload is shorter still — see [Five shorthands for the common case](#five-shorthands-for-the-common-case):

```csharp
.WithSimpleSchedule(TimeSpan.FromMinutes(5))                   // the same schedule
.WithSimpleSchedule(TimeSpan.FromMinutes(5), repeatCount: 2)   // three firings rather than forever
```

## `CronScheduleBuilder`'s convenience factories are gone

`CronSchedule(string)` and `CronSchedule(CronExpression)` stay, spelled `Create(...)` (see
[Every builder starts with `Create`](#every-builder-starts-with-create)). The six factories that built an
expression from numbers, in three different argument orders, are replaced by `CronExpressionBuilder`, which
names each field.

`CronExpressionBuilder`'s naming:

* `With*` restricts a field to values, day-of-week included: `WithDaysOfWeek`, `WithDayOfWeekRange`,
  `WithDayOfWeekIncrements`.
* `On*` is only for the positional and special forms: `OnWeekdays`, `OnLastDayOfMonth`,
  `OnLastDayOfWeek`, `OnLastDayOfWeekOfMonth`, `OnNthDayOfWeekOfMonth`, `OnNearestWeekdayOfMonth`.
* `WithCronSchedule` takes the builder, or a built `CronExpression`, so the chain never names
  `CronScheduleBuilder`.

Three day-of-week members were renamed by that rule; arguments and meaning are unchanged:

| 3.x | 4.x |
|---|---|
| `CronExpressionBuilder.OnDaysOfWeek(params DayOfWeek[])` | `CronExpressionBuilder.WithDaysOfWeek(params DayOfWeek[])` |
| `CronExpressionBuilder.OnDayOfWeekRange(DayOfWeek, DayOfWeek)` | `CronExpressionBuilder.WithDayOfWeekRange(DayOfWeek, DayOfWeek)` |
| `CronExpressionBuilder.OnDayOfWeekIncrements(DayOfWeek, int)` | `CronExpressionBuilder.WithDayOfWeekIncrements(DayOfWeek, int)` |

The time of day is one `TimeOnly`, passed to `AtTime`, which sets the second, minute and hour fields
together; add `WithDaysOfWeek` or `WithDayOfMonth` for the days. Cron resolves to a whole second, so a
sub-second part is ignored.

| 3.x | 4.x |
|---|---|
| `CronScheduleBuilder.DailyAtHourAndMinute(h, m)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m))` |
| `CronScheduleBuilder.AtHourAndMinuteOnGivenDaysOfWeek(h, m, days)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m)).WithDaysOfWeek(days)` |
| `CronScheduleBuilder.WeeklyOnDayAndHourAndMinute(day, h, m)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m)).WithDaysOfWeek(day)` |
| `CronScheduleBuilder.MonthlyOnDayAndHourAndMinute(dom, h, m)` | `CronExpressionBuilder.Create().AtTime(new TimeOnly(h, m)).WithDayOfMonth(dom)` |
| `CronScheduleBuilder.CronScheduleWithHash(expr, hashKey)` | `CronScheduleBuilder.Create(CronExpression.ParseWithHash(expr, hashKey))` |
| `CronScheduleBuilder.CronScheduleWithHash(expr, hashSeed)` | `CronScheduleBuilder.Create(CronExpression.ParseWithHash(expr, hashSeed))` |

```diff
- .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(9, 30))
+ .WithCronSchedule(CronExpressionBuilder.Create().AtTime(new TimeOnly(9, 30)))
```

A literal expression is still the shortest:

```diff
- .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(9, 30))
+ .WithCronSchedule("0 30 9 ? * *")
```

## Five shorthands for the common case

Five additive shorthands shorten the usual calls. The longer forms still work; use them when a call needs
more than the shorthand says.

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

* `n` in `WithSimpleSchedule(i, n)` is the trigger's own `RepeatCount`, as `WithRepeatCount` takes it, so
  it fires `n + 1` times. Omit it to repeat forever.
* `IScheduler.ScheduleJob<TJob>(trigger, configure)` is the run-time twin of the container's
  `q.ScheduleJob<TJob>(...)` and names the job the same way: from `configure`, else the job the trigger
  points at through `ForJob`, else the trigger's own key. Neither names a `JobBuilder`:

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

* `TriggerBuilder<TJob>.Key` reports the trigger's identity, as `JobBuilder<TJob>.Key` does, so code that
  must agree with a trigger can read its key. Unlike the job builder, it keeps the key `Build()`
  generated when none was named: building twice gives the same trigger, and `Key` after `Build()`
  reports that key rather than `null`.

## Day selection on `DailyTimeIntervalScheduleBuilder`

The two `OnDaysOfTheWeek` overloads are one C# 13 params collection, so both call shapes compile:

```csharp
x.OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Wednesday);   // still fine
x.OnDaysOfTheWeek(daysFromConfiguration);                   // still fine
```

The three `public static readonly` day sets are gone; use the named methods:

| 3.x | 4.x |
|---|---|
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.AllDaysOfTheWeek)` | `OnEveryDay()`, also the default |
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.MondayThroughFriday)` | `OnMondayThroughFriday()` |
| `OnDaysOfTheWeek(DailyTimeIntervalScheduleBuilder.SaturdayAndSunday)` | `OnSaturdayAndSunday()` |

Outside the builder, `Enum.GetValues<DayOfWeek>()` is the whole week.

## `EndingDailyAfterCount` is computed at `Build()`

In 3.x, `EndingDailyAfterCount(count)` computed the daily window at the call, against the wall clock and
whatever start time, interval and time zone were already set. It now runs when the trigger is built:

* It sees the final schedule, so it no longer has to be last in the chain.
* It uses the clock of the `TriggerBuilder` that builds the trigger, so a custom `TimeProvider` (a
  `FakeTimeProvider` in tests) is honored; 3.x silently read the wall clock.
* "count too large" and "start time not set" are reported by `Build()`. A count that is not positive is
  still rejected at the call.

No schedule builder takes a `TimeProvider`, `DailyTimeIntervalScheduleBuilder.Create()` included. The clock
belongs to `TriggerBuilder.Create(TimeProvider?)`, and reaches the trigger as well as the schedule — see
[A trigger holds the clock that built it](#a-trigger-holds-the-clock-that-built-it).

## A trigger holds the clock that built it

3.x's `SystemTime.UtcNow` was one process-wide hook. 4.x's `TimeProvider` is an object, so each trigger has
a clock, taken from whoever produced it:

* `TriggerBuilder.Create(clock)...Build()` gives the built trigger that clock. Before, the clock set only
  the default start time and the schedule builder's arithmetic; the trigger stayed on
  `TimeProvider.System`, so the past-due clamp in `ComputeFirstFireTimeUtc` and all of
  `UpdateAfterMisfire` read the wall clock. `TriggerBuilder.Create()` still means `TimeProvider.System`.
* `trigger.GetTriggerBuilder()` carries the trigger's clock into the rebuilt trigger.
* A job store gives its scheduler's clock to every trigger it materializes. `RAMJobStore` returns the
  object it was given, clock and all; the ADO.NET store stamps each trigger it builds from a row, blob path
  included, since the clock does not serialize.

In production all of these are `TimeProvider.System`. With a `FakeTimeProvider`, a misfire *selected* on
the test's clock used to be *recovered* on the machine's: the ADO.NET half of
[#3456](https://github.com/quartznet/quartznet/issues/3456).

The clock is not on a trigger's public surface. It is set at construction or by the store, so that
deciding a misfire and recovering from it use one reading of "now".

## `InTimeZone` is nullable everywhere

All four schedule builders and `DateBuilder.InTimeZone` take `InTimeZone(TimeZoneInfo?)`.
`CronScheduleBuilder` and `DailyTimeIntervalScheduleBuilder` declared `InTimeZone(TimeZoneInfo)`, so a zone
that may be absent needed a `!`. `null` still means the local time zone.

```diff
- .InTimeZone(configuredZone!)
+ .InTimeZone(configuredZone)
```

## `ScheduleBuilder<T>` is gone

The five schedule builders implement `IScheduleBuilder` directly. The base class only redeclared the
interface's one member, and nothing used its type parameter. A schedule builder of your own implements the
interface and drops the `override`:

```diff
- private sealed class MyScheduleBuilder : ScheduleBuilder<MyTrigger>
+ private sealed class MyScheduleBuilder : IScheduleBuilder
  {
-     public override IMutableTrigger Build() => new MyTrigger();
+     public IMutableTrigger Build() => new MyTrigger();
  }
```

## `DateBuilder`'s static factories are gone

The fluent API stays: `DateBuilder.Create()` (3.x `NewDate()`), `CreateInTimeZone()` (3.x
`NewDateInTimeZone()`), the `At*`/`On*`/`In*` setters and `Build()`. The seventeen statics are gone: name a
date with the fluent API, and do arithmetic on `DateTimeOffset`.

### Naming a date

| 3.x | 4.x |
|---|---|
| `DateBuilder.DateOf(h, m, s)` | `DateBuilder.Create().AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.TodayAt(h, m, s)` | `DateBuilder.Create().AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.DateOf(h, m, s, day, month)` | `DateBuilder.Create().InMonthOnDay(month, day).AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.DateOf(h, m, s, day, month, year)` | `DateBuilder.Create().InYear(year).InMonthOnDay(month, day).AtHourMinuteAndSecond(h, m, s).Build()` |
| `DateBuilder.TomorrowAt(h, m, s)` | `DateBuilder.Create().AtHourMinuteAndSecond(h, m, s).Build().AddDays(1)` |

`InMonthOnDay` takes the month first; `DateOf` took the day first.

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

Most start times need no rounding. A trigger that starts at an arbitrary instant and repeats every minute
keeps the same schedule as a rounded one; it just begins a fraction of a second earlier. Round only when
you want tidy displayed times.

## `TimeOfDay` became `TimeOnly`

The hand-written `TimeOfDay` class is gone. `IDailyTimeIntervalTrigger` and
`DailyTimeIntervalScheduleBuilder` use `System.TimeOnly`.

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

* The two properties are non-nullable. Their defaults are the ones the builder always applied: `00:00:00`
  and `23:59:59`.
* A value with sub-second precision throws `ArgumentException`. The job store keeps hour, minute and
  second columns, so `TimeOnly.FromDateTime(DateTime.Now)` would lose its fraction when persisted. Round it
  yourself.
* Storage is unchanged: the same `SIMPROP_INT_PROP` columns, and the JSON `StartTimeOfDay`/`EndTimeOfDay`
  objects keep their `{ Hour, Minute, Second }` shape, so existing triggers load.

## `DailyCalendar` takes two `TimeOnly` values

One constructor and one property replace eight constructors and four `SetTimeRange` overloads.

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

* `TimeRange` is a `readonly record struct` of `Start` and `End` in the `Quartz` namespace. A
  `(start, end)` tuple converts to it implicitly, so the assignment above works. A variable holding the
  range is a `TimeRange`, not a `ValueTuple`, so compare with a typed value:
  `calendar.TimeRange == new TimeRange(start, end)`.
* The `"HH:MM:SS:mmm"` string form (note the colon before the milliseconds) is gone; nothing else in .NET
  parsed it.
* `InvertTimeRange`, `GetTimeRangeStartingTimeUtc` and `GetTimeRangeEndingTimeUtc` are unchanged.
* The constructor no longer takes a `TimeProvider`; it only used one to check that the range starts before
  it ends.
* Precision finer than a millisecond is rejected, matching what the serialized form can carry.
* Stored calendars load unchanged. The serialized form is the same eight numbers; the serializers write
  `RangeStart`/`RangeEnd` and still read the old `RangeStartingTime`/`RangeEndingTime` strings.

## Excluded days are a read-only set

The four day-excluding calendars share one shape: a read-only set of what is excluded, plus
`AddExcludedDay` and `RemoveExcludedDay`, which return whether the set changed.

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

* `AnnualCalendar` uses `MonthDay`, a `readonly record struct` of month and day in the `Quartz` namespace;
  `MonthDay.From(DateOnly)` converts a date. A set of `DateOnly` always carries a year, so what went in was
  not what came out, and `DaysExcluded.Contains` disagreed with `AddExcludedDay`. February 29th is a valid
  `MonthDay`.
* `MonthDay`'s text form is ISO 8601's recurring month-day, `--MM-DD`. It implements
  `IParsable<MonthDay>`, `ISpanParsable<MonthDay>`, `ISpanFormattable` and `IUtf8SpanFormattable`, so it
  binds from configuration, a route value or a JSON string. Format strings and format providers are
  ignored.
* `AnnualCalendar` payloads are unchanged: a day is still serialized date-shaped, pinned to the year 2000.
* `AnnualCalendar.IsDayExcluded` answers only about the calendar's own set. `IsTimeIncluded`, which asks
  about an instant, consults the base calendar.
* `MonthlyCalendar.AreAllDaysExcluded` and `WeeklyCalendar.AreAllDaysExcluded` are unchanged, and a new
  `WeeklyCalendar` still excludes Saturday and Sunday.
* Stored calendars load unchanged. Both serializers write the new shapes and read the old: an
  `ExcludedDays`/`ExcludedDates` array may hold timestamps or dates, and per-day booleans, day numbers or
  day names.

## `[Serializable]` survives only where a database blob needs it

`BinaryFormatter` is obsolete on .NET 8 (SYSLIB0051) and throws on .NET 9 and later, and Quartz 4 ships no
binary serializer. **Seventeen types** keep `[Serializable]`, `ISerializable` and `GetObjectData`: those a
job store blob can be made of, so that a 3.x database with `BinaryFormatter` blobs stays readable while you
migrate it to JSON — see
[Migrating from binary serialization](packages/json-serialization.md#migrating-from-binary-serialization).
Every other type lost them.

| Blob column | Types that keep the attributes |
|---|---|
| `JOB_DETAILS.JOB_DATA`, `TRIGGERS.JOB_DATA` | `JobDataMap`, `Key<T>`, `JobKey`, `TriggerKey` |
| `CALENDARS.CALENDAR` | `BaseCalendar`, `AnnualCalendar`, `CronCalendar`, `DailyCalendar`, `HolidayCalendar`, `MonthlyCalendar`, `WeeklyCalendar`, `CronExpression` |
| `BLOB_TRIGGERS.BLOB_DATA` | `TriggerBase`, `SimpleTriggerImpl`, `CronTriggerImpl`, `CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` |

* **Triggers:** a trigger goes to `BLOB_TRIGGERS` when no trigger persistence delegate handles it (a type
  of your own, or one deriving from a shipped implementation with `HasAdditionalProperties` returning
  `true`). The whole object is written, so the trigger class hierarchy is part of the blob graph.
* **`RecurrenceTriggerImpl`** is not on the list. `RecurrenceTriggerPersistenceDelegate` writes it to
  `SIMPROP_TRIGGERS`, so it never takes the blob path. It did carry the attribute on 3.x, so a 3.x database
  holding a binary recurrence-trigger blob (possible only with that delegate removed from the delegate
  list) must migrate that row to JSON on 3.x before upgrading.
* **Job data:** `StringKeyDirtyFlagMap` and `DirtyFlagMap<TKey, TValue>`, listed there on 3.x, are
  internal. Existing blobs are unaffected: `BinaryFormatter` records an `ISerializable` graph under the
  runtime type name, `Quartz.JobDataMap`, with its `version`/`dirty`/`map` entries and never the base
  chain, and `JobDataMap` has that exact constructor and entry set.
* **Keys** stay because a key can be a *value* in a job data map. Quartz never stores one there (its
  recovery entries are strings, and `TriggerBase` and `JobDetailImpl` mark their key fields
  `[NonSerialized]`), but an application that did `jobDataMap.Put("parent", jobKey)` on 3.x has a `JobKey`
  in its `JOB_DATA`. `BinaryFormatter` refuses a type not marked serializable, even through the
  compatibility package.

These still-public types lost `[Serializable]`. The rest went with a type that is now internal, removed or
renamed; see [the appendix](#appendix-what-happened-to-a-name).

| Where | Types |
|---|---|
| Exceptions | `SchedulerException`, `JobExecutionException`, `JobPersistenceException`, `ObjectAlreadyExistsException`, `SchedulerConfigException`, `JsonSerializationException`, `LockException`, `NoSuchDelegateException`, `SchedulingDataValidationException` |
| Matchers | `AndMatcher<TKey>`, `GroupMatcher<TKey>`, `KeyMatcher<TKey>`, `NameMatcher<TKey>`, `NotMatcher<TKey>`, `OrMatcher<TKey>`, `StringMatcher<TKey>`, `StringOperator` |
| Everything else | `JobType`, `SchedulerContext`, `JobExecutionContextImpl` |

The `protected` / `public` `(SerializationInfo, StreamingContext)` constructors are gone from
`SchedulerException`, `JobPersistenceException`, `SchedulerConfigException` and `HttpClientException`. If
you derive from one and forward a `SerializationInfo` to the base, delete your constructor: the base class
library's `Exception(SerializationInfo, StreamingContext)` is obsolete too, and nothing calls yours.

## `JobExecutionException` has four constructors and init-only flags

Seven constructors became the four every exception has: `()`, `(message)`, `(cause)`,
`(message, cause)`. The three removed ones took the `refireImmediately` flag positionally. The three
scheduler directives (`RefireImmediately`, `UnscheduleFiringTrigger`, `UnscheduleAllTriggers`) are
init-only properties:

```diff
- throw new JobExecutionException(msg, ex, true);
+ throw new JobExecutionException(msg, ex) { RefireImmediately = true };
```

The directives are fixed at the throw site. `JobDetail`, which the scheduler fills in on the way to the
listeners, is read-only outside Quartz.

### It is sealed, and `Exception.Data` is what a subclass was for

`JobExecutionException` is `sealed`. The scheduler reads only its three directives, so a subclass could
not add a fourth. To carry your own state to your own listener, use `Exception.Data`:

```diff
- public sealed class ImportJobException : JobExecutionException
- {
-     public string TenantId { get; init; }
- }
- throw new ImportJobException(ex) { TenantId = tenantId, RefireImmediately = true };
+ throw new JobExecutionException(ex) { RefireImmediately = true, Data = { ["tenant"] = tenantId } };
```

A thrown instance reaches `IJobListener.JobWasExecuted` **unchanged** (the run shell only fills in
`JobDetail`), so the listener reads `jobException.Data["tenant"]`.

For typed state, the closer replacement for a subclass, wrap an exception of your own as the cause:

```csharp
throw new JobExecutionException(new ImportFailed(tenantId)) { RefireImmediately = true };

// in the listener
if (jobException?.InnerException is ImportFailed failure) { … }
```

An exception that is *not* a `JobExecutionException` arrives wrapped twice: `JobExecutionException` →
`JobExecutionProcessException` → your exception. A listener that handles both routes walks
`InnerException` until it finds what it knows.

## The two exceptions moved out of `Quartz.Core`

`JobExecutionProcessException` and `JobInstantiationException`, the only public types in `Quartz.Core`,
moved to `Quartz` beside `SchedulerException` (their base), `JobExecutionException` and
`SchedulerConfigException`. Nothing in `Quartz.Core` is public now.

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

Catching or type-testing either exception needs no change except deleting `using Quartz.Core;`, which the
compiler flags as unused. `JobInstantiationException` is new in 4.0, so no released code catches it under
the old namespace.

## Execution limits are built once, then frozen

`ExecutionLimits` was both a mutable fluent builder (`ForGroup`, `ForDefaultGroup`, `ForOtherGroups` and
`Unlimited` mutated `this`) and an `IReadOnlyDictionary<string, int?>` that had to suppress CA1710. It is
two types now: `ExecutionLimitsBuilder` mutates, and `ExecutionLimits` is the immutable snapshot `Build()`
returns and the scheduler thread reads.

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
unchanged:

```csharp
q.UseExecutionLimits(limits => limits.ForGroup("high-cpu", 3));
```

Reading limits back changed, because the snapshot is not a dictionary:

| Before | 4.0 |
|---|---|
| `limits["heavy"]` | `limits.TryGetLimit(ExecutionGroupScope.Named("heavy"), out int? maxConcurrent)` |
| `limits[ExecutionLimits.DefaultGroupKey]` | `limits.TryGetLimit(ExecutionGroupScope.Default, out int? maxConcurrent)` |
| `limits["*"]` | `limits.TryGetLimit(ExecutionGroupScope.OtherGroups, out int? maxConcurrent)` |
| `limits.ContainsKey("heavy")` | `limits.TryGetLimit(ExecutionGroupScope.Named("heavy"), out _)` |
| `limits.Count` | `limits.Groups.Count`, or `limits.IsEmpty` |
| `foreach (KeyValuePair<string, int?> pair in limits)` | `foreach (ExecutionGroupLimit limit in limits.Groups)` |

* `TryGetLimit` returning `false` does not mean unlimited. The scope has no entry of its own, and a named
  group without one still falls back to `ExecutionGroupScope.OtherGroups`.
* `ExecutionGroupScope` is a readonly record struct with the three cases the builder can write: `Default`
  (triggers with no execution group), `OtherGroups` (the catch-all) and `Named(name)`, modelled like
  `PreferredNode`. `ExecutionGroupLimit.Group` is that value, so enumerating `Groups` no longer relies on
  `null` meaning the default bucket and `"*"` the catch-all:

```csharp
foreach (ExecutionGroupLimit limit in limits.Groups)
{
    string label = limit.Group.IsDefault ? "(default)"
        : limit.Group.IsOtherGroups ? "(other groups)"
        : limit.Group.Name!;
}
```

* `ExecutionGroupLimit.Scope` is something else: `ExecutionLimitScope.Node` or `.Cluster`, from
  [cluster-wide ceilings](#an-execution-limit-can-be-cluster-wide). `Group` says *which* bucket; `Scope`
  says *what it is counted against*.
* Configuration is unchanged: `quartz.executionLimit.*` keys and the HTTP API use `*` for the catch-all and
  `_` or `null` for the default bucket (property keys and JSON object keys cannot be empty). All three are
  reserved: a trigger cannot use `"*"`, `"_"` or `"null"` as its execution group, and
  `ExecutionGroupScope.Named` rejects them as `ExecutionLimitsBuilder.ForGroup` does.

### A job store is handed the limits, and a way to spend them

`TriggerAcquisitionRequest.ExecutionLimits` and `TriggerAcquisitionCriteria.ExecutionLimits` carry
`ExecutionLimits` instead of `IReadOnlyDictionary<string, int?>`. They still hold the *available* slots
(configured limits less what already runs on this node), not the configuration.

A store counts slots down as it acquires triggers. The rule, which only the two built-in stores could
reach before, is now public as `ExecutionSlots`:

* a group's own entry wins;
* a named group without one falls back to `OtherGroups`; a trigger with no execution group never does;
* each unlisted group that borrows from `OtherGroups` gets its own allowance rather than sharing one.

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

Create the slots per acquisition attempt, not per store: they are mutable and not thread-safe by design,
and a retried acquisition must start from the limits again. `CreateSlots()` leaves the snapshot untouched,
so one `ExecutionLimits` can produce any number.

A clustered store of your own has one more step — see
[cluster-wide ceilings](#an-execution-limit-can-be-cluster-wide).

## Interruption has two names, not three

`ICancellableJobExecutionContext`, and `JobExecutionContextImpl.Cancel()` with it, are gone from the public
API. `Cancel()` bypassed the scheduler, so no `ISchedulerListener.JobInterrupted` was raised, and it worked
only on a context held in the same process. Two names remain:

* to request: `IScheduler.Interrupt(JobKey)` or `IScheduler.InterruptFireInstance(fireInstanceId)`;
* to observe: `IJobExecutionContext.CancellationToken`, the same token `IJob.Execute` receives as its
  `cancellationToken` parameter.

```diff
- ((ICancellableJobExecutionContext) context).Cancel();
+ await scheduler.InterruptFireInstance(context.FireInstanceId);
```

The fire instance id comes from `IScheduler.QueryFireInstances`, which replaced `GetCurrentlyExecutingJobs`
— see [what is running is a listing](#what-is-running-is-a-listing-not-a-list-of-contexts). Its elements
were never the cancellable interface, whatever the documentation said.

### `UnableToInterruptJobException` is gone

Every job receives the cancellation token, so no job "cannot be interrupted" and nothing threw this
Java-era exception. `Interrupt(JobKey)` and `InterruptFireInstance(fireInstanceId)` keep their semantics:
they cancel the token of matching executions and return whether they found any. Honoring the token is up
to the job.

* Delete any `catch (UnableToInterruptJobException)` block.
* `HttpScheduler` no longer maps a remote fault to this type. A remote interrupt fault surfaces as the
  `SchedulerException`-derived type the server reported.

## `TimeZoneUtil` became `Quartz.TimeZones`

`TimeZoneUtil` is `TimeZones`, a static class in the root namespace, and `FindTimeZoneById` is `FindById`.
It is scheduling API: `FindTimeZoneById` restores the zone of an `InTimeZone(...)` trigger from a job
store, and the wall-clock `GetUtcOffset(DateTime, TimeZoneInfo)` overload is the scheduler-wide daylight
saving policy (an ambiguous local time resolves to the daylight offset, the first of its two
occurrences). Code under a `Quartz.*` namespace needs no `using`; elsewhere, `using Quartz.Util;` becomes
`using Quartz;`:

```diff
- using Quartz.Util;
+ using Quartz;

- var zone = TimeZoneUtil.FindTimeZoneById("Europe/Helsinki");
+ var zone = TimeZones.FindById("Europe/Helsinki");
```

* There is no configuration shim; no configuration string names this type.
* `ConvertTime(DateTimeOffset, TimeZoneInfo)` and `GetUtcOffset(DateTimeOffset, TimeZoneInfo)` are
  internal. They were Mono-era shims; call `TimeZoneInfo` directly. The wall-clock
  `GetUtcOffset(DateTime, TimeZoneInfo)` stays public.
* The id alias table stays. On Windows, "Coordinated Universal Time" and "CET" fail
  `TimeZoneInfo.FindSystemTimeZoneById` and both `TryConvert*` conversions even on ICU, and resolve only
  through the table. Its one dead entry, "US Central Standard Time" ↔ "US/Indiana-Stark" (neither is a
  Windows system id), was removed; both ids now fail with the exception that points at
  `Quartz.Plugins.TimeZoneConverter`.
* When the direct lookup and the aliases fail, `FindById` tries `TimeZoneInfo.TryConvertIanaIdToWindowsId`
  before the registered resolvers. Run first, the conversion would turn "US/Eastern" into a zone whose
  `Id` is "Eastern Standard Time", and a job store writes that Id back to `TIME_ZONE_ID`.

### `CustomResolver` became `AddResolver`

`CustomResolver` was one settable delegate. Two schedulers running `Quartz.Plugins.TimeZoneConverter` in
one process overwrote each other, and shutting either down left the winner's resolver installed for the
life of the process. `AddResolver` composes: each caller gets an `IDisposable` that removes exactly its
resolver.

```diff
- TimeZoneUtil.CustomResolver = id => Resolve(id);
+ IDisposable registration = TimeZones.AddResolver(id => Resolve(id));
  ...
- TimeZoneUtil.CustomResolver = null;
+ registration.Dispose();
```

* Resolvers are consulted **most recently added first**, so a later registration shadows an earlier one
  for the ids it resolves, as the last `CustomResolver` assignment did.
* A resolver declines an id by returning `null` or by throwing `TimeZoneNotFoundException`; the search
  continues with the next one. `FindById` throws only after every fallback fails.
* `UseTimeZoneConverter` registers the resolver, using `TZConvert.TryGetTimeZoneInfo` so that an unknown
  id declines without an exception — see
  [`TimeZoneConverterPlugin` is a resolver registration](#timezoneconverterplugin-is-a-resolver-registration).

### `TimeZoneConverterPlugin` is a resolver registration

`Quartz.Plugins.TimeZoneConverter` and `UseTimeZoneConverter` stay; `TimeZoneConverterPlugin` is gone. It
was one `TimeZones.AddResolver` call with a plugin's lifecycle, so `UseTimeZoneConverter` makes the call
itself.

| 3.x / earlier 4.0 preview | 4.0 |
|---|---|
| `q.UseTimeZoneConverter()` | unchanged |
| `q.AddPlugin<TimeZoneConverterPlugin>()` | `q.UseTimeZoneConverter()` |
| `quartz.plugin.timeZoneConverter.type = Quartz.Plugin.TimeZoneConverter.TimeZoneConverterPlugin, Quartz.Plugins.TimeZoneConverter` | Remove the key and call `UseTimeZoneConverter()`; a configuration still naming the type fails to load |
| `new TimeZoneConverterPlugin()` | Nothing; `TimeZones.AddResolver(id => …)` registers a resolver of your own |

* **The resolver is registered at configuration time, not scheduler start.** Zone lookups also happen with
  no scheduler in scope (building a trigger, parsing a `CronExpression`, deserializing a trigger from a
  job store), so a trigger built before the host starts now resolves its zone.
* **Nothing removes the resolver.** The plugin removed it on `Shutdown`, taking care not to disturb other
  schedulers in the process. One registration now outlives every scheduler, and a second
  `UseTimeZoneConverter` is a no-op rather than a duplicate resolver.
* **Plan for this in tests:** a test that asserts an id does *not* resolve must account for the
  process-wide resolver, which is permanent for the life of the process that installed it.

## `TriggerUtils` became `TriggerFireTimes`

`TriggerUtils` is `TriggerFireTimes`, in `Quartz.Extensibility` beside `IOperableTrigger`: it computes fire
times by advancing a copy of a trigger through its schedule, applying the calendar at each step. The
methods dropped the words the type name now says:

| 3.x | 4.x |
|---|---|
| `TriggerUtils.ComputeFireTimes(...)` | `TriggerFireTimes.Compute(...)` |
| `TriggerUtils.ComputeFireTimesBetween(...)` | `TriggerFireTimes.ComputeBetween(...)` |
| `TriggerUtils.ComputeEndTimeToAllowParticularNumberOfFirings(...)` | `TriggerFireTimes.ComputeEndTimeForCount(...)` |

Parameters and behavior are unchanged:

```diff
+ using Quartz.Extensibility;

- var times = TriggerUtils.ComputeFireTimes((IOperableTrigger) trigger, calendar, 10);
+ var times = TriggerFireTimes.Compute(trigger, calendar, 10);
```

The cast is no longer needed. Each method has an `ITrigger` overload beside its `IOperableTrigger` one,
which casts and throws an `ArgumentException` naming the type if the trigger cannot be advanced. This is
additive: a call that casts still binds to the `IOperableTrigger` overload.

## Other Breaking Changes

**Triggers, schedules and calendars**

* **`SimpleTriggerImpl.GetFireTimeBefore(DateTimeOffset? endUtc)` takes a non-nullable `DateTimeOffset`.**
  Null threw on 3.x anyway (`endUtc!.Value`); check a nullable end first. `EndTimeUtc` is still
  `DateTimeOffset?`.
* **`DailyTimeIntervalTriggerImpl` rounds `StartTimeUtc` and `EndTimeUtc` down to the whole second**, as
  `CronTriggerImpl.StartTimeUtc` always did. A start of `22:50:00.68` with an 02:15 start-of-day and a
  five-minute interval computed a first fire time of `22:50:00.000`, before the trigger's own start
  ([#3386](https://github.com/quartznet/quartznet/issues/3386)). Boundary times lose their milliseconds;
  fire times are unchanged.
* **`TriggerState.Executing` added**, and `Blocked` now means a sibling trigger is running — see
  [Executing is a trigger state](#executing-is-a-trigger-state).
* **`CronTriggerImpl.WillFireOn` is one method**, `WillFireOn(DateTimeOffset timeUtc, bool dayOnly = false)`.
  Both call shapes compile.
* **`SimpleTriggerImpl.ComputeNumTimesFiredBetween` is `ComputeNumberOfTimesFiredBetween`.**
* **`StartingDailyAt` / `EndingDailyAt` take a `timeOfDay`**, not `timeOfDayUtc`: the value is wall-clock
  time in the trigger's time zone.
* **`CalendarIntervalTriggerImpl`, `DailyTimeIntervalTriggerImpl` and `RecurrenceTriggerImpl` are
  unsealed**, so all five concrete triggers can be subclassed for a derived serializer. Nothing became
  `protected`.
* **`PreserveHourOfDayAcrossDaylightSavings` and `SkipDayIfHourDoesNotExist` default to `true`**: a call
  with no argument turns the flag on. Passing a value still works.
* **`MisfireInstruction` is internal**; use the per-family enums — see
  [The enums are the vocabulary](#the-enums-are-the-vocabulary).
* [**`ScheduleBuilder<T>` removed**](#schedulebuilder-t-is-gone).
* **`DailyTimeIntervalScheduleBuilder`'s day-set fields are internal.** Use `OnEveryDay()`,
  `OnMondayThroughFriday()` and `OnSaturdayAndSunday()` for `AllDaysOfTheWeek`, `MondayThroughFriday` and
  `SaturdayAndSunday`.
* [**`TimeOfDay` removed**](#timeofday-became-timeonly).
* [**`DailyCalendar` has one constructor**](#dailycalendar-takes-two-timeonly-values).
* [**Calendar `SetDayExcluded` / `AddExcludedDate` removed**](#excluded-days-are-a-read-only-set).
* **`CronCalendar.SetCronExpressionString` removed.** Assign `CronExpression`, which already took a parsed
  expression.
* [**`CronExpression.Clone()` removed**](#cronexpression-is-immutable).
* [**`Quartz.MonthDay` added**](#excluded-days-are-a-read-only-set).
* **`TriggerUtils` became `Quartz.Extensibility.TriggerFireTimes`**, with shorter `Compute*` names — see
  [`TriggerUtils` became `TriggerFireTimes`](#triggerutils-became-triggerfiretimes).
* [**`TimeZoneUtil` became `Quartz.TimeZones`**](#timezoneutil-became-quartz-timezones).
* [**`TimeZoneUtil.CustomResolver` became `TimeZones.AddResolver(...)`**](#customresolver-became-addresolver).

**Jobs and job data**

* **`JobType` introduced.** It holds a job type without needing a `Type` instance.
  * A `Type` converts implicitly and must implement `IJob`. A string converts only explicitly or through
    the constructor, because resolving it is deferred and can fail: `Type` throws for a name that does not
    resolve; `TryResolve` does not throw.
  * Equality (`Equals`, `==`/`!=`) is by `FullName`.
  * There is deliberately **no** implicit conversion back to `Type`; reading `jobDetail.JobType.Type` may
    probe assemblies, and can throw.
* **`JobBuilder.OfType(JobType)` added, and is the only name-taking spelling.** It carries a stored type
  name and its resolver through a rebuild without resolving it. The preview's `OfType(string)` is gone;
  write `OfType((JobType) typeName)` — see
  [The builder surface says each thing once](#the-builder-surface-says-each-thing-once).
* **`IJobDetail.GetJobBuilder()` removed from the interface.** It is still callable as an extension method,
  so only implementations change — see [An `IJobDetail` of your own](#an-ijobdetail-of-your-own).
* **`IJobDetail.WithJobData(JobDataMap)` added**: a copy carrying the given data, which `RAMJobStore` uses
  — see [An `IJobDetail` of your own](#an-ijobdetail-of-your-own).
* **`JobBuilder<TJob>.Key` is public.** It reports the identity given, or `null`, so a trigger registered
  alongside can agree with it.
* **`IJobConfigurator<TJob>` members return `IJobConfigurator<TJob>`.** `JobBuilder<TJob>` implements them
  explicitly and keeps its own members, so `JobBuilder.Create()…` chains are unaffected — see
  [Job data can name the property](#job-data-can-name-the-property).
* [**`UsingJobData` takes an `object?`**](#nine-usingjobdata-overloads-became-one).
* **`IJobExecutionContext.RecoveringTriggerKey`** returns `null` when not recovering, instead of throwing.
* **`IJobExecutionContext.Put` / `.Get` removed**, from the public `JobExecutionContextImpl` too. To talk to
  a listener, set `Result`; state that must outlive the execution belongs in `JobDataMap`.
* **`JobExecutionContextImpl.IncrementRefireCount()` and the `JobRunTime` setter are internal.** Only
  `JobRunShell` records them; writing either reported a fire that never happened.
* [**`ICancellableJobExecutionContext` removed**](#interruption-has-two-names-not-three).
* [**Silent behavioral change: `PropertySettingJobFactory` no longer merges the scheduler context into job properties**](#scheduler-context-entries-are-no-longer-injected-into-job-properties).
* **`JobKey` and `TriggerKey` implement `IEquatable<T>` and `IParsable<T>`.** Additive. `TryParse`/`Parse`
  invert `ToString`'s `<group>.<name>` form, splitting at the first `.`, so a *group* containing `.` is
  ambiguous. Job-store dictionary lookups also skip the object comparer, and the hash is computed once.
* **The `Try*` members carry nullability attributes.** Not breaking; listed because the API baselines show
  it. `CronExpression.TryParse` marks its input `[NotNullWhen(true)]`, as `JobKey.TryParse`,
  `TriggerKey.TryParse` and `MonthDay.TryParse` did; `JobDataMap.TryGetValue` /
  `SchedulerContext.TryGetValue` mark `value` `[MaybeNullWhen(false)]`, like `Dictionary<,>.TryGetValue`.
* [**`JobDataMap`'s sixty typed accessors removed**](#jobdatamap-s-typed-accessors-are-extension-members).
* **A `decimal` reader added**: `Get<decimal>` / `TryGet<decimal>` — see
  [One accessor per type](#one-accessor-per-type-for-the-types-job-data-is-made-of).
* **`DateOnly`/`TimeOnly`/enum accessors, `PutAsString(DateOnly/TimeOnly)` and `TryGet<T>` added.**
  Additive.
* **`PutAsString` writes round-trip ("O") formats for `DateTime`/`DateTimeOffset`**, and the new `DateTime`
  overload **rebinds** calls that used the `IConvertible` one — see
  [`PutAsString` writes round-trip formats now](#putasstring-writes-round-trip-formats-now).
* **`Get<DateTime>` parses with `DateTimeStyles.RoundtripKind`**: a string ending in `Z` returns
  `Kind=Utc`, not a local-shifted `Kind=Local` value — see
  [`PutAsString` writes round-trip formats now](#putasstring-writes-round-trip-formats-now).
* [**`PutAsString(string, Guid?)` removed**](#putasstring-string-guid-is-gone).
* **`JobDataMap(IDictionary)` removed.** `JobDataMap(IDictionary<string, object?>)` remains and handles the
  dirty marker.
* **`JobDataMap.GetEnumerator` returns `IEnumerator<KeyValuePair<string, object?>>`**, not
  `Dictionary<string, object?>.Enumerator`, as `SchedulerContext` does. `foreach` is unaffected; retype a
  variable declared as the struct.
* **`JobDataMap.Dirty` / `ClearDirtyFlag()` are internal.** Clearing the flag from a job silently skipped
  the `[PersistJobDataAfterExecution]` rewrite; `SchedulerConstants.ForceJobDataMapDirty` still forces one.
* **`JobDataMap.Equals` compares values, not only keys**, and equal maps hash equally. `SchedulerContext`
  compares by reference.
* [**`DirtyFlagMap<TKey, TValue>` and `StringKeyDirtyFlagMap` are internal**](#jobdatamap-and-schedulercontext-stand-alone).
* **`SchedulerContext` is backed by `ConcurrentDictionary`**, so concurrent reads and writes are safe and
  enumeration no longer races plugin writes.

**The shipped jobs (`Quartz.Jobs`)**

* **`IDirectoryScanListener` is asynchronous.** `FilesUpdatedOrAdded` and `FilesDeleted` return `ValueTask`
  and take a `CancellationToken`.
* **`SendMailJob.Send` is asynchronous:**
  `protected virtual ValueTask Send(MailInfo mailInfo, CancellationToken cancellationToken = default)`,
  using `SmtpClient.SendMailAsync`, so it no longer blocks a pool thread. `Execute` forwards its token; an
  override returns `default`.
* **`SendMailJob.MailInfo` is `Quartz.Jobs.MailInfo`**, a top-level `sealed` type with `required`/`init`
  members: `MailMessage` and `SmtpHost` required, `SmtpPort` and `Credentials` optional. Object-initializer
  construction compiles; assigning a property afterwards does not. The two credential strings are one
  `Credentials` — see [The SMTP password does not belong in job data](#the-smtp-password-does-not-belong-in-job-data).
* **The shipped jobs have options types**, `DirectoryScanOptions`, `FileScanOptions`, `NativeJobOptions`
  and `SendMailOptions`, set with `Using*Options(…)`. Job data keys still work — see
  [The shipped jobs are configured by name](#the-shipped-jobs-are-configured-by-name).

**Scheduler, factory and exceptions**

* **`QuartzScheduler` and `QuartzSchedulerResources` are internal.** Resolve `IScheduler` /
  `ISchedulerFactory`; scheduler-wide settings are `QuartzSchedulerOptions`.
* **`StdSchedulerFactory` removed.** Use `QuartzSchedulerBuilder.Create().UseProperties(properties)` — see
  [`StdSchedulerFactory` is gone](#stdschedulerfactory-is-gone).
* **`GetScheduler()` after `Shutdown()` throws `SchedulerException`** instead of building a fresh
  scheduler: the container owns the parts, so it would re-initialize the torn-down pool and store and hand
  back the same closed instance. Use
  `Standby()`/`Start()` to pause and resume, or build a new host or container.
* **`ISchedulerFactory.GetScheduler(name)` is `LookupScheduler(name)`.** `GetScheduler()` builds this
  factory's scheduler and never returns null; `LookupScheduler(name)` looks one up in the container's
  repository and can, like `ISchedulerRepository.Lookup`.
* **`ISchedulerFactory.GetAllSchedulers` returns `ValueTask<List<IScheduler>>`.**
* **`IInstanceIdGenerator.GenerateInstanceId` returns `ValueTask<string>`**, never null.
* **`HostnameInstanceIdGenerator` is `HostNameInstanceIdGenerator`** and internal. A
  `quartz.scheduler.instanceIdGenerator.type` naming the old spelling resolves, with a warning.
* **Constructing a scheduler no longer starts a thread.** `QuartzScheduler` starts it in `Start()`, so
  resolving the graph, `ValidateOnBuild` or asserting on registrations starts none. Jobs run when they did.
* **`IScheduler.Shutdown`'s token bounds the wait for running jobs.** 3.x and early 4.0 ignored it, so
  `Shutdown(waitForJobsToComplete: true, ct)` waited for the slowest job. Now it stops waiting when the
  token fires; the rest of shutdown runs and jobs are not cancelled — see
  [`Drain` is the shutdown that can be given a deadline](#drain-is-the-shutdown-that-can-be-given-a-deadline).
* **`RescheduleJob` recomputes a fire time that has fallen behind the start time.** A next fire time you
  set is still kept (so `GetTrigger` → mutate → `RescheduleJob` works and a replacement keeps its cadence),
  but not one *before the trigger's own start*. Rescheduling advances the start of a never-fired repeating
  simple trigger whose start is past, and such a trigger fired at the stored time and again at the start
  ([#3554](https://github.com/quartznet/quartznet/issues/3554)). The first fire time is now computed from
  the adjusted start. Neither job store recomputed anything here.
* **`ObjectAlreadyExistsException.JobKey` / `.TriggerKey` added**: the clashing identity, set by the
  constructors taking a job detail or trigger and `null` from the message-only one. The message is
  unchanged.
* **`SchedulingDataValidationException` derives from `SchedulerException`**, not `System.Exception`, so
  `catch (SchedulerException)` around loading an XML or JSON scheduling file now catches it.
  `catch (Exception)` and `catch (SchedulingDataValidationException)` are unaffected.
* [**`JobExecutionProcessException` and `JobInstantiationException` moved to `Quartz`**](#the-two-exceptions-moved-out-of-quartz-core).
* **`SchedulerConstants` is a `static class`**, not a `struct`; constant references are unchanged.
* **`TimeSpanParseRuleAttribute` is public.** It says how a bare number in configuration is read as a
  `TimeSpan`.
* **`Quartz.Util.DictionaryExtensions` removed.**
* **`Quartz.Util.ObjectExtensions` is internal.** `AssemblyQualifiedNameWithoutVersion()` is Quartz's
  blob and wire spelling of a type name, not a general helper.
* **`ISchedulerProxyFactory` and `HttpSchedulerProxyFactory` removed** — see
  [Remoting a scheduler is not a Quartz concern](#remoting-a-scheduler-is-not-a-quartz-concern).

**Configuration, dependency injection and hosting**

* **`QuartzSchedulerBuilder.Create` takes the `AddQuartz` callback**: configure in `Create(q => …)` — see
  [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder).
* **`QuartzSchedulerBuilder.Build()` returns `StandaloneSchedulerFactory`**, an `ISchedulerFactory` that is
  also `IAsyncDisposable` and `IDisposable`.
* **`IServiceCollectionQuartzConfigurator` is `IQuartzBuilder`.** The `AddQuartz` overloads with a
  `(configurator, IServiceProvider)` callback are gone; use `AddJob`, `AddTrigger`, `ScheduleJob`,
  `UseExecutionLimits`, `AddJobTimeout` and the rest, or the options pattern — see
  [Deferred configuration](#deferred-configuration).
* **`IQuartzBuilder` gained `UseThreadPool(IThreadPool)` and `UseJobStore(IJobStore)`**, for pre-built parts
  under `AddQuartz`.
* **`IQuartzBuilder` gained `UseJobStore<T>` / `<T, TOptions>` / factory** for a job store of your own — see
  [A component of your own is chosen the same way a shipped one is](#a-component-of-your-own-is-chosen-the-same-way-a-shipped-one-is).
* **`IQuartzBuilder` gained `UseInstanceIdGenerator<T>` / `<T, TOptions>` / instance**, replacing
  `quartz.scheduler.instanceIdGenerator.type`; it sets `GenerateInstanceId`.
* **`IQuartzBuilder.ConfigureJobScope(...)` added**, the `ConfigureScope` hook as a delegate — see
  [The job scope is prepared without writing a job factory](#the-job-scope-is-prepared-without-writing-a-job-factory).
* **The builder's listener overloads take `params IReadOnlyCollection<IMatcher<T>>`**, like
  `IListenerManager`. Call sites are unaffected.
* [**`AddPlugin` shapes aligned to the listener trio**](#plugins-are-registered-like-listeners).
* **`AddJob<T>` and `ScheduleJob<T>` register the job type**, so an unresolvable job fails
  `ValidateOnBuild` — see [`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container).
* **A registered job's constructor may not take a scheduler's parts**, such as `IScheduler` or `IJobStore`;
  the host refuses it at start — see
  [`AddJob` registers the job with the container](#addjob-registers-the-job-with-the-container).
* [**The `JobKey`-taking `AddJob` overloads removed**](#one-shape-per-registration-method).
* **DI `AddCalendar` takes `AddCalendarOptions`** instead of two adjacent bools; `calendarName` is `name`.
* **`AddQuartzSchedulers(IConfiguration, …)` added.** `AddQuartz(configuration)` no longer fans out over a
  `Schedulers` section; it throws and points here.
* [**`QuartzOptions.SchedulerName`, `.SchedulerId`, `.MisfireThreshold` removed**](#quartzoptions-lost-its-three-typed-settings).
* **`QuartzOptions.Scheduling` is get-only.** Set its properties; assigning a new instance discarded what
  other callbacks and `Quartz:Scheduling` had set.
* **Every public `*Options` type is `sealed`**, `QuartzHttpApiOptions` and `HttpClientOptions` included.
  `QuartzHostedService` stays open for `AddQuartzHostedService<T>`.
* **`XmlSchedulingOptions` and `JsonSchedulingOptions` merged** into one type; they were identical.
* **`LoggingJobHistoryPlugin.Name` and `LoggingTriggerHistoryPlugin.Name` are get-only.** `Initialize`
  hands over the name; writing it later did nothing.
* **`quartz.scheduler.proxy*` and `quartz.scheduler.exporter*` are rejected.** Nothing read them; the
  exception names the replacement.
* [**`ExecutionLimits` split into a builder and a snapshot**](#execution-limits-are-built-once-then-frozen).
* **`IQuartzBuilder.UseExecutionLimits` takes an `Action<ExecutionLimitsBuilder>`**; the lambda body is
  unchanged.
* [**`Quartz.ExecutionSlots`, `Quartz.ExecutionGroupLimit` and `Quartz.ExecutionGroupScope` added**](#execution-limits-are-built-once-then-frozen).
* [**`QuartzHostedService` takes an `IServiceProvider` and an `IOptionsMonitor`**](#the-hosted-service-starts-every-scheduler).
* **`QuartzHostedService` shuts its schedulers down concurrently.** With several schedulers, the stop time
  was the *sum* of their waits and overran `HostOptions.ShutdownTimeout`.
* **`AddQuartzHostedService(string schedulerName, …)` added.** `QuartzHostedServiceOptions` are named
  options; the unnamed call still configures every scheduler.
* **`QuartzHostedServiceOptions.AutoStart` added**, default `true` — see
  [A hosted scheduler can be started by the application](#a-hosted-scheduler-can-be-started-by-the-application).

**Dashboard, HTTP API and health check**

* **`AddQuartzHttpApi` is on `IServiceCollection` only**: `services.AddQuartzHttpApi()`. The preview's
  `IQuartzBuilder` form is gone.
* **`IQuartzBuilder.AddHttpApi` / `MapQuartzApi` renamed** `services.AddQuartzHttpApi()` /
  `MapQuartzHttpApi`. `AddQuartzHealthChecks` gained an `IQuartzBuilder` overload.
* **`HttpScheduler.Context` and `.ListenerManager` throw `NotSupportedException`**: a remote scheduler has
  neither in this process. `Context` made a **synchronous** HTTP call and returned a detached copy;
  `ListenerManager` threw a `SchedulerException`. `UpdateTriggerDetails` also threw through 4.0, and
  [4.1 gives it an endpoint](#upgrading-from-4-0-to-4-1) — see
  [what is not supported remotely](packages/http-client.md#what-is-not-supported-remotely).
* **The health check is added on `IHealthChecksBuilder`**: `AddHealthChecks().AddQuartz()` /
  `.AddQuartz("reporting")`. `IServiceCollection.AddQuartzHealthChecks()` is gone;
  `IQuartzBuilder.AddQuartzHealthChecks()` stays — see
  [The ASP.NET Core methods say Quartz once](#the-asp-net-core-methods-say-quartz-once).
* **The health check ships in `Quartz`**, not `Quartz.AspNetCore` — see
  [The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore).
* **`QuartzHealthCheckOptions` goes through the options pipeline**, so
  `services.Configure<QuartzHealthCheckOptions>(...)` works now. `Name` is nullable and defaults to the
  scheduler's check name.
* **`QuartzHealthCheckOptions.Tags` is a get-only `List<string>`**; add to it — see
  [A shipped component is configured through its options type, and only there](#a-shipped-component-is-configured-through-its-options-type-and-only-there).
* [**`Quartz.AspNetCore.AddQuartzServer` removed**](#addquartzserver-is-addquartzhostedservice).
* **`IDashboardAuthorizationFilter` and `QuartzDashboardOptions.AuthorizationFilter` removed.** Nothing
  invoked the filter; use `AuthorizationPolicy`, which is enforced.
* **`IDashboardHistoryStore` is asynchronous:** `ValueTask AddExecution` and
  `ValueTask<PagedResult<DashboardHistoryEntry>> QueryExecutions(DashboardHistoryQuery)`.
  `SearchFilter.DebounceMilliseconds` is `TimeSpan Debounce`, and `InProcessQuartzApiClient` is internal
  (resolve `IQuartzApiClient`).
* **`IDashboardHistoryStore` gained `AddMisfire`, `QueryMisfires(DashboardMisfireQuery)` and
  `CountMisfires(name, since)`**, which **a store of your own must implement** — see
  [History and live events say which node they came from](#history-and-live-events-say-which-node-they-came-from).
* [**`IQuartzApiClient` uses `IScheduler`'s verbs**](#the-client-speaks-the-scheduler-s-verbs).
* **The dashboard's HTTP-backed API client is gone** — see
  [The dashboard reads the schedulers in its own process](#the-dashboard-reads-the-schedulers-in-its-own-process).

**Serialization, trimming and AOT**

* **`SystemTextJsonSerializerOptions` and `NewtonsoftJsonSerializerOptions` removed.** The
  `Use*JsonSerializer` callback receives the registry; lambda bodies compile unchanged — see
  [Custom trigger and calendar serializers are no longer static](#custom-trigger-and-calendar-serializers-are-no-longer-static).
* **Newtonsoft `ICalendarSerializer.CalendarTypeName` added** as a default interface member; existing
  implementations compile — see
  [Newtonsoft types moved out of the core namespaces](#newtonsoft-types-moved-out-of-the-core-namespaces).
* **Newtonsoft calendar contracts moved to `Quartz.Serialization.Newtonsoft.Calendars`**, matching
  `Quartz.Serialization.SystemTextJson.Calendars`. Source-only.
* **`ITriggerSerializer.TriggerTypeForJson` is `TriggerTypeName`**, matching
  `ICalendarSerializer.CalendarTypeName`, in both JSON serializers.
* **`RecurrenceTriggerSerializer` is unsealed in both packages**, so all five built-in trigger serializers
  can be derived from.
* **Serializers outside a scheduler read a container-wide registry.** The HTTP API and `Quartz.HttpClient`
  read a `SystemTextJsonSerializerRegistry` from the container; register it as a singleton to make a custom
  serializer visible to them. The dashboard passes triggers and calendars through and registers none.
* **`SystemTextJsonSerializerRegistry` gained `AddTypeInfoResolver(IJsonTypeInfoResolver)`.** Under
  `PublishTrimmed` or `PublishAot`, pass it a generated `JsonSerializerContext`'s `Default` for job-data
  values of your own types. Quartz's types and registered custom triggers and calendars are covered already
  — see [Trimming annotations](#trimming-annotations).
* **The `Quartz` package declares `IsAotCompatible` and binds configuration with the source-generated
  binder.** No API moved, and Quartz reports no `IL3050`, so configuration from `appsettings.json` is as
  AOT-safe as configuration in code. With the reflection binder, a native publish
  silently got defaults for `MaxBatchSize`, `ShutdownJobInterruption` and the whole scheduler context. The
  `IL2xxx` for the string-named paths are unchanged — see [Trimming annotations](#trimming-annotations).
* **Every other shipped package says whether it is trimmable.** No API moved. Six are `IsTrimmable`;
  `Quartz.Serialization.Newtonsoft` and `Quartz.Dashboard` say `IsTrimmable=false`, and their readmes say
  why — see [Trimming annotations](#trimming-annotations).
* **`[Serializable]` removed from 30 types** — see
  [`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it).
* **The `(SerializationInfo, StreamingContext)` constructors removed** from `SchedulerException`,
  `JobPersistenceException`, `SchedulerConfigException` and `HttpClientException` — see
  [`[Serializable]` survives only where a database blob needs it](#serializable-survives-only-where-a-database-blob-needs-it).
* **`[Serializable]` removed from `JobExecutionContextImpl` and `SchedulerContext`**, which are never
  persisted; `SchedulerContext` also lost its private deserialization constructor.
* **`[Serializable]` removed from `TriggerFiredBundle` and `TriggerFiredResult`.**

**Telemetry and logging**

* **Job execution metrics are published by every scheduler.** Only `StdSchedulerFactory` configured the
  meters, so a scheduler registered with `AddQuartz` published none.
* **Every instrument and attribute was renamed, and two instruments were dropped.** `scheduling.quartz.*`
  became `quartz.job.execution.*`, unprefixed attributes became `quartz.*`, and the two counters gave way to
  the histogram's own count. Dashboards, alerts and recording rules on the old names break; see the
  [old → new table](#old-and-new-telemetry-names).
* **`quartz.job.execution.duration` records seconds, not milliseconds**; a millisecond axis reads 1000× low
  — see [Job execution metrics](#job-execution-metrics).
* **`quartz.job.execution.active` is an `UpDownCounter<long>` with unit `{job}`**, not a `Counter<long>` fed
  `-1` with unit `ea` — see [Job execution metrics](#job-execution-metrics).
* **Every job execution measurement is tagged with `quartz.scheduler.name`**: one series per scheduler —
  see [Job execution metrics](#job-execution-metrics).
* **Every measurement is tagged with `quartz.scheduler.id` too**: one series per node — see
  [Job execution metrics](#job-execution-metrics).
* **`quartz.execution.group` is on the execution span and measurements**: the bucket a thread limit applies
  to. A trigger with no group has no such attribute, rather than an empty one.
* **`ActivityTags.ExecutionGroup`, `.JobStoreOperation`, `.JobStoreLock`, `.RecoveredInstanceId` added**
  for the four new attribute names.
* **Five new instruments:** `quartz.trigger.misfire`, `quartz.trigger.acquisition.duration`,
  `quartz.trigger.acquired`, `quartz.cluster.checkin.duration`, `quartz.cluster.recovery.trigger` — see
  [the observability page](packages/opentelemetry-integration.md#metrics).
* **`quartz.jobstore.operation.duration` measures every store round trip**, named by
  `quartz.jobstore.operation` (the operation's span name) and tagged `error.type` on failure.
* **`quartz.jobstore.lock.wait.duration` measures every attempt to take a job store lock**, tagged
  `quartz.jobstore.lock` (`TRIGGER_ACCESS` or `STATE_ACCESS`) and `error.type` on failure. It is the one
  measurement a stalled scheduler produces, since a blocked lock statement records no operation and no
  failure. Re-entrant acquisitions are not recorded.
* **Every store emits `Quartz.JobStore.*` spans, not only the ADO.NET one.** Tracing is a decorator over
  `IJobStore`, applied by the registration that builds a scheduler's resources; span names, kind and
  attributes are unchanged. A container-built store is therefore not the object the scheduler holds:
  unwrap `Quartz.Impl.DelegatingJobStore` before comparing or type-testing `IJobStore` instances.
  `IScheduler.GetMetadata().JobStoreTypeName` still reports the inner store.
* **The meter is built from the container's `IMeterFactory`**, not a process-wide static — see
  [Job execution metrics](#job-execution-metrics).
* **`scheduling.quartz.exception_type` is `error.type`**, naming the exception the job threw; rewrite
  queries on the old name or value — see [Job execution metrics](#job-execution-metrics).
* **`Quartz.Diagnostics.QuartzInstrumentation` publishes the `ActivitySource` and `Meter` names**, which
  `AddSource("Quartz")` / `AddMeter("Quartz")` spelled as bare strings. Both are still `"Quartz"`;
  `InstrumentationOptions` is gone — see
  [Job execution metrics](#job-execution-metrics).
* **`Quartz.Diagnostics.ActivityOptions` is `ActivityTags`.** It replaced 3.x's `DiagnosticHeaders`; the
  constant names are unchanged, but every **value** is `quartz.*` now — see
  [Job execution metrics](#job-execution-metrics).
* **The vetoed-fire span is `Quartz.Job.Veto`**; `OperationName.Job.Veto` read `"Quartz.Job.Vetoed"`.
  `Quartz.Job.Execute` and the `Quartz.JobStore.*` names are unchanged.
* **`Quartz.Diagnostics.IJobDiagnosticData` removed**, with the `DiagnosticSource` events
  `Quartz.OpenTracing` consumed. Job execution is on `Activity` through `QuartzActivitySource`; a listener
  reads `IJobExecutionContext`.
* **`LockHandlerContext.LoggerFactory` and `DriverDelegateContext.LoggerFactory` added**, defaulting to
  `NullLoggerFactory.Instance`. The job store passes its container's factory, so lock contention and
  statement failures are logged without `LogProvider.SetLogProvider` — see
  [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient).
* **The ADO stores take an optional `ILoggerFactory?` through `AdoJobStoreDependencies.LoggerFactory`**,
  filled in by the container; the store, its cluster manager, misfire handler and units of work log through
  it. A hand-built store keeps the ambient factory — see
  [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient).
* **The components `Use*<T>()` builds take an optional logger.** `TaskSchedulingThreadPool` (both
  constructors), `DefaultThreadPool`, `ZeroSizeThreadPool` and `HostNameBasedIdGenerator` take an
  `ILogger<T>?`; `SimpleJobFactory`, `PropertySettingJobFactory` and `MicrosoftDependencyInjectionJobFactory`
  take an `ILoggerFactory?`. `null` means the ambient factory, so `new DefaultThreadPool()` and `: base()`
  compile unchanged; a subclass passes the parameter through to get the container's logger — see [The ambient logger factory stays ambient](#the-ambient-logger-factory-stays-ambient).

**Job stores**

* **`RAMJobStore` is `sealed`, with no `virtual` members.** Wrap it with `DelegatingJobStore` — see
  [`RAMJobStore` is sealed](#ramjobstore-is-sealed).
* [**`Quartz.Impl.DelegatingJobStore` added**](#delegatingjobstore-decorates-a-store).
* **An `IJobStore` that implements `IJobListener` no longer receives events automatically.** Register it
  through the scheduler's `IListenerManager`.
* **`TriggerFiredResult` is made by three factories**, `TriggerFiredResult.Fired(bundle)`,
  `TriggerFiredResult.NotFired` and `TriggerFiredResult.Failed(exception)`, replacing two constructors that
  could build a result with both a bundle and an exception, or neither.
  `new TriggerFiredResult((TriggerFiredBundle?) null)` meant "not firable".
* **`InternalTriggerState.Executing` removed.** Nothing assigned or read it.
* [**`TriggerAcquisitionRequest.ExecutionLimits` and `TriggerAcquisitionCriteria.ExecutionLimits` are `ExecutionLimits?`**](#a-job-store-is-handed-the-limits-and-a-way-to-spend-them).
* **`TriggerAcquisitionRequest.ExcludedJobTypeNames` and `TriggerAcquisitionCriteria.ExcludedJobTypeNames`
  added**: optional exact job-type-name exclusions, honored by every shipped store. The ADO.NET store
  compares in SQL, by the `JOB_CLASS_NAME` column's collation; `RAMJobStore` compares `JobType.FullName`
  ordinally.
* **`StoredTriggerState` moved to `Quartz.Extensibility`, and `TriggerStateResolver.Resolve` is public.**
  Members and stored strings are unchanged; a delegate updates a `using` — see
  [Trigger states are typed on the driver delegate](#trigger-states-are-typed-on-the-driver-delegate).

**ADO.NET job store**

* [**`JobStoreTX` is `LocalTransactionJobStore`, `JobStoreCMT` is `ExternalTransactionJobStore`**](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use).
* **The three ADO store constructors take one `AdoJobStoreDependencies`** instead of the same twelve
  parameters each. Moot now: all four types are internal — see
  [The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class).
* **`AdoJobStoreBase`'s constructor takes `IOptions<ClusteringOptions>`**, between `storeOptions` and
  `objectSerializer`.
* **Clustering settings moved to `ClusteringOptions`.** `AdoJobStoreOptions.Clustered` and the two
  `ClusterCheckin*` settings are gone, and `IJobStore.Clustered` reports the state rather than setting it —
  see [Clustering is configured in one place](#clustering-is-configured-in-one-place).
* **`AdoJobStoreBase.GetLocalTransactionConnection` (was `GetNonManagedTXConnection`) and `GetConnection`
  return `ValueTask<ConnectionAndTransactionHolder>`.**
* **`GetNonManagedTXConnection`, `ExecuteInNonManagedTXLock`, `RetryExecuteInNonManagedTXLock` renamed**
  `GetLocalTransactionConnection`, `ExecuteInLocalTransactionLock`, `RetryExecuteInLocalTransactionLock`
  (protected).
* [**`AdoJobStoreBase`'s nine `Execute…Lock` overloads became four members**](#nine-execute-lock-overloads-became-four-members).
* **`ExternalTransactionJobStore.OpenConnection` moved to `AdoJobStoreOptions.OpenConnection`**, read once
  at construction — see
  [Nine `Execute…Lock` overloads became four members](#nine-execute-lock-overloads-became-four-members).
* **Protected `AdoJobStoreBase` / `StdAdoDelegate` members take a `CancellationToken`.** Overrides add the
  parameter; callers do not.
* **`ConnectionAndTransactionHolder.Close` takes a `CancellationToken`**; `.Commit` and `.Rollback` are
  internal — see
  [A job store of your own can join your transaction](#a-job-store-of-your-own-can-join-your-transaction).
* **`ConnectionAndTransactionHolder` gained `(connection, transaction, ownsResources)` and
  `OwnsResources`**, for a store on a connection it did not open.
* **`ConnectionAndTransactionHolder` is `IAsyncDisposable`.** Prefer `await using` in async code; `using`
  still works. Both paths log failures at debug instead of swallowing them.
* **`AdoJobStoreBase.GetEnlistedConnection` is internal**, with its type; enlisting from the application
  side is unaffected — see
  [A job store of your own can join your transaction](#a-job-store-of-your-own-can-join-your-transaction).
* **`IPersistentStoreBuilder.AcceptEnlistedTransactions()`** never shipped; use
  `ConfigureStore(o => o.AcceptEnlistedTransactions = true)` — see
  [Joining an existing transaction](tutorial/job-stores.md#joining-an-existing-transaction).
* **~25 `AdoJobStoreBase` configuration properties are read-only and `protected`/internal**; read
  `IOptions<AdoJobStoreOptions>` — see
  [The job store configuration is read-only](#the-job-store-configuration-is-read-only-and-no-longer-a-public-currency).
* **`JobStoreSupport.UseProperties`'s `string` setter removed.** The `bool`
  `AdoJobStoreOptions.StoreJobDataAsStrings` and read-only `CanUseProperties` remain; the property bridge
  parses the key.
* **`AdoJobStoreBase.DriverDelegateType` and `.DontSetAutoCommitFalse` removed**, and
  **`AdoJobStoreOptions.DontSetAutoCommitFalse`** with them. Nothing read them, and no `quartz.*` key set
  the option.
* **The seven conn-taking `Pause…`/`Resume…`/`RecoverMisfiredJobs` overloads are `protected`.** Call the
  public keyed overloads.
* **`AdoJobStoreBase`'s virtual surface is curated.** Only `Initialize`, `Shutdown`, `GetConnection`,
  `GetLocalTransactionConnection`, `ExecuteInLock<T>`, `IsTransient`, `AcquireNextTriggers`,
  `CreateAcquisitionCriteria` and `GetFiredTriggerRecordId` are overridable; the other ~75 members are not.
* **`AdoJobStoreBase.LastCheckin` is internal, and `LogWarnIfNonZero` is removed.** Its callers raise
  source-generated events at warning level — see
  [Every message carries an event id](#every-message-carries-an-event-id).
* **`AdoJobStoreBase.DoCheckin` and `.DoRecoverMisfires` are `CheckIn` and `RecoverMisfires`.**
* **Four `AdoJobStoreBase` parameters are spelled out**: `CloseConnection`, `RollbackConnection` and
  `CommitConnection` take `connection` (was `cth`); `CalcFailedIfAfter` takes `record` (was `rec`). Only
  named arguments are affected.
* **`AdoJobStoreBase.RecoverJobs(CancellationToken)` returns `ValueTask`**; its `bool` was always `true`.
* **`AdoJobStoreBase.LockTriggerAccess` / `.LockStateAccess` removed.** Use `SchedulerLock.TriggerAccess` /
  `.StateAccess`.
* **The ADO store reports a failure once, not once per nesting level.** A provider exception is still
  wrapped in a `JobPersistenceException` naming the operation, but that is not wrapped again, so a
  deliberate failure (`The job (…) referenced by the trigger does not exist.`,
  `Calendar cannot be removed if it is referenced by a trigger!`, an `ObjectAlreadyExistsException`) reaches
  the caller as itself. 3.x prefixed it with the operation
  (`Couldn't store trigger '…' for '…' job: The job (…) …`). Code matching a nested message's *text* reads
  the inner message now; `catch (JobPersistenceException)` and the HTTP API's mapping are unaffected.
* **Cancelling a store operation raises `OperationCanceledException`**, not a `JobPersistenceException`
  wrapping one. A cancelled operation holding a lock and a transaction rolls back and releases the lock,
  rather than reporting `Unexpected runtime exception` or `Failed to obtain DB connection`. An
  `OperationCanceledException` raised while the caller's own token is *not* cancelled is still a store
  failure.
* **A transaction the database rolled back is retried whatever the driver calls it.**
  `AdoJobStoreBase.IsTransient` also retries SQLSTATE class `40` except `40002`, so a Firebird write
  conflict (`40001`) and a MySQL `1213` deadlock on MySql.Data are retried like SQL Server's 1205 and
  SQLite's `BUSY`. Retries are bounded by `MaxTransientRetries` and `TransientRetryInterval`; an
  `IsTransient` override still replaces the whole verdict.
* **`AdoJobStoreOptions.TxIsolationLevelSerializable` is `TransactionIsolationLevel`**, an
  `IsolationLevel?` — see [The isolation level is an isolation level](#the-isolation-level-is-an-isolation-level).
* **`AdoJobStoreOptions.CommandTimeout` added.** It bounds every statement the store issues, lock handler
  included, through `DriverDelegateContext.CommandTimeout` and `LockHandlerContext.CommandTimeout`. Unset
  keeps each provider's default. 3.22's `quartz.jobStore.commandTimeout` (milliseconds) translates; its `0`
  ("provider default") maps to unset, because 4.x refuses a non-positive timeout.
* **`AdoJobStoreOptions.LockWaitWarningThreshold` added**: how long one attempt to take a job store lock
  may run before warning **3716** is logged, once per acquisition. Default 30 seconds; `null` turns it off,
  zero is refused. It only reports; `CommandTimeout` or a wait timeout in `SelectWithLockSql` ends the wait.
  No 3.x key — see
  [A Lock Held by a Connection That Is Gone](../troubleshooting.md#a-lock-held-by-a-connection-that-is-gone).

**Database providers**

* **`UseSQLite` is `UseSystemDataSqlite`, and `UseMicrosoftSQLite` is `UseSqlite`**: **the short name
  changed meaning** — see [The SQLite extension methods swapped names](#the-sqlite-extension-methods-swapped-names).
* **`UseDataSourceConnectionProvider()` removed.** Set `DataSourceOptions.UseRegisteredDataSource`, which is
  what it did.
* [**`AddDataSourceProvider()` removed**](#adddatasourceprovider-went-with-it).
* **Every `Use<Db>` gained a `(DbProviderFactory factory, string connectionString)` overload**, such as
  `UseSqlServer(SqlClientFactory.Instance, connectionString)`. Nothing is resolved from a string or built by
  reflection, so use it for `PublishTrimmed` and `PublishAot`. `ConnectionStringName` does not apply — see
  [Naming a driver, or handing over its factory](configuration/reference.md#naming-a-driver-or-handing-over-its-factory).
* **`UseOracle(factory, connectionString, configureCommand, configureBinaryParameter)`.** ODP.NET binds by
  position unless `OracleCommand.BindByName` is set, and reads `DbType.Binary` as `OracleDbType.Raw`
  (two kilobytes of job data). Naming the driver instead sets both for you.
* **`UseGenericDatabase(factory, connectionString, DbMetadata)`**: a driver with no shipped description,
  reached through its factory and described in code.
* **`DbMetadata` gained `ConfigureCommand` and `ConfigureBinaryParameter`**, lambdas for what the name path
  does by reflecting over `CommandType` and `ParameterType`. Every `Type` on `DbMetadata` is optional, and
  a binary parameter with neither a seam nor a described parameter type binds as `DbType.Binary`.
* **`DbMetadata.DbBinaryTypeName` and `.ParameterDbTypePropertyName` gained getters** and are `string?`. A
  `DbBinaryTypeName` without the property to write it to now fails with a clear message, not as
  `Type.GetProperty(null)`.
* **The name-taking `Use<Db>` overloads carry `[RequiresUnreferencedCode]`**, `UseGenericDatabase(provider, …)`
  included. The warning appears in your `UsePersistentStore` callback, not at `AddQuartz`, and names the
  two ways out.
* **`ProviderFactoryDbProvider` added**, for a factory no overload knows:
  `UseConnectionProvider(_ => new ProviderFactoryDbProvider(metadata, factory, connectionString))`.
* **The Oracle driver description says `DbBinaryTypeName = "Blob"`.** ODP.NET used to infer `Raw` and cap a
  job data map at 2000 bytes; both ways of reaching Oracle now write a `BLOB`.
* [**`IDbConnectionManager` / `DbConnectionManager` removed**](#the-connection-manager-is-gone).
* **`DbMetadataFactory` is internal.** Describe a driver through `UseGenericDatabase`'s metadata factory.
* **`DbProvider.PropertyDbProvider` and `.DbProviderResourceName` removed**; nothing read these
  `protected const`s.

**Lock handlers**

* **`ISemaphore` is `ILockHandler`**: `DBSemaphore` is `DbLockHandler`, `SimpleSemaphore` the internal
  `InProcessLockHandler`, `RedisSemaphore` `RedisLockHandler` — see
  [The semaphores are lock handlers](#the-semaphores-are-lock-handlers).
* **`ISemaphore.ObtainLock` is `ILockHandler.AcquireLock`.** `ReleaseLock` and `RequiresConnection` are
  unchanged.
* **`SemaphoreContext` is `LockHandlerContext`**, with the same members.
* **The row-lock handlers are named for the SQL they issue**: `StdRowLockSemaphore` is
  `SelectForUpdateLockHandler`, `UpdateLockRowSemaphore` is `UpdateRowLockHandler`,
  `PostgreSQLRowLockSemaphore` is `PostgreSqlSelectForUpdateLockHandler`, `UpdateLockRowSemaphoreMOT` is
  `SqlServerMemoryOptimizedUpdateRowLockHandler` — see
  [The semaphores are lock handlers](#the-semaphores-are-lock-handlers).
* **Row-lock handler SQL fields are `protected const`.** `UpdateLockRowSemaphore.SqlUpdateForLock` /
  `.SqlInsertLock` are `UpdateRowLockHandler.UpdateForLock` / `.InsertLock`;
  `SelectForUpdateLockHandler.SelectForLock` / `.InsertLock` keep their names.
* **`ILockHandler` takes a `SchedulerLock`**, not a `string lockName` — see
  [Locks are a `SchedulerLock`, not a string](#locks-are-a-schedulerlock-not-a-string).
* **`ILockHandler.AcquireLock` throws on cancellation**; `false` now means only a re-entrant acquire — see
  [A cancelled acquire throws, and `false` means only re-entry](#a-cancelled-acquire-throws-and-false-means-only-re-entry).
* [**`ILockHandler.Initialize(LockHandlerContext)` replaces `ITablePrefixAware`**](#a-lock-handler-is-told-which-scheduler-it-locks-for).
* **`ILockHandler.Shutdown(CancellationToken)` added** as a default interface member that does nothing, so
  **an existing handler need not implement it** — see
  [A lock handler is told when to close what it opened](#a-lock-handler-is-told-when-to-close-what-it-opened).
* **`LockHandlerContext` also carries `TimeProvider`, `CommandTimeout` and `LockWaitWarningThreshold`.**
  `DbLockHandler` exposes the clock as a `protected TimeProvider`, and both row-lock handlers back off on
  it, so a retry is testable without a real wait. The threshold is how long one acquisition may run before
  `DbLockHandler` logs warning 3716.
* [**`DbLockHandler.LockSql` (was `Sql`) and `InsertSql` are get-only, set by the constructor**](#the-semaphores-are-lock-handlers).
* **`DbLockHandler.PrepareCommand` / `.AddCommandParameter` are `protected`**, so a subclass can implement
  `ExecuteSql`, which `private protected AdoUtil` made impossible — see [Sealed and Internalized Types](#sealed-and-internalized-types).
* **`SelectForUpdateLockHandler.MaxRetry` / `.RetryPeriod` are `init`-only.** Set them in an object
  initializer; `quartz.jobStore.lockHandler.maxRetry` / `.retryPeriod` still reach them.
* **`UpdateRowLockHandler.RetryPeriod` added.** Its backoff was a fixed one second that ignored
  `quartz.jobStore.lockHandler.retryPeriod`; the default is still one second.

**Driver delegate and SQL**

* [**`IDriverDelegate` trigger states are `StoredTriggerState`**](#trigger-states-are-typed-on-the-driver-delegate).
* **The `…FromOtherStates` members take one `IReadOnlyCollection<StoredTriggerState>`** instead of two or
  three fixed old-state parameters.
* [**`FiredTriggerRecord`, `RecoverMisfiredJobsResult`, `DriverDelegateContext` are `sealed record`s**](#the-driver-delegate-speaks-in-records).
* [**`DelegateInitializationArgs` is `DriverDelegateContext`, and its `InstanceName` is `SchedulerName`**](#the-initialization-seams-are-context-records).
* [**`ITriggerPersistenceDelegate.Initialize` takes a `TriggerPersistenceDelegateContext`**](#the-initialization-seams-are-context-records).
* [**`DriverDelegateContext.InitString` replaced by `TriggerPersistenceDelegates`**](#the-trigger-family-interfaces-are-read-models).
* **Trigger persistence delegates are all public and `sealed`.** `CronTriggerPersistenceDelegate`,
  `SimpleTriggerPersistenceDelegate` and `DailyTimeIntervalTriggerPersistenceDelegate` were internal;
  derive from `SimplePropertiesTriggerPersistenceDelegateBase` for your own.
* **`SimplePropertiesTriggerProperties` is an init-only `record`.** Build it with an object initializer
  (`new SimplePropertiesTriggerProperties { Int1 = …, String1 = … }`) and derive with `with`.
* **`SimplePropertiesTriggerPersistenceDelegateBase`'s four SQL statements are private**:
  `SelectSimplePropsTrigger`, `DeleteSimplePropsTrigger`, `InsertSimplePropsTrigger` and
  `UpdateSimplePropsTrigger`. The column name constants stay `protected`, and so does
  `TableSimplePropertiesTriggers`, now aliasing the public `AdoConstants.TableSimplePropertiesTriggers`.
* **`FiredTriggerRecord.FireInstanceState` is a `StoredTriggerState`**; `[Serializable]` is gone and the
  always-populated members are non-nullable.
* **`RecoverMisfiredJobsResult.EarliestNewTime` is `EarliestNewTimeUtc`.**
* **`RecoverMisfiredJobsResult.NoOp` is a static property**, not a field; recompile.
* **`TriggerAcquireResult` carries a `TriggerKey`**, not `TriggerName` and `TriggerGroup`.
* **`TriggerAcquireResult.JobType` is `JobTypeName`.** It holds `JOB_CLASS_NAME`, a type name like
  `JobHeader.JobTypeName`. `TriggerHeader.TriggerType` is a discriminator and keeps its name.
* [**`TriggerStatus` removed, and `IDriverDelegate.SelectTriggerStatus` is `SelectTriggerHeader`**](#the-driver-delegate-speaks-in-records).
* [**`StoredTriggerHeader` carries `TriggerType`**](#batched-trigger-fire).
* **`IDriverDelegate.IsTriggerCurrentlyExecuting` removed.** `SelectTriggerStateWithExecuting` reads state
  and execution in one statement and returns `TriggerExecutionState`.
* **`IDriverDelegate.IsJobCurrentlyExecuting` takes a `JobKey`**, not `(string jobName, string jobGroup)`.
* **`IDriverDelegate.SelectJobForTrigger`'s `loadJobType` is required**; pass `loadJobType: true` for the
  old default.
* **`IDriverDelegate.UpdateTriggerPreferredNodeConditional` takes a `PreferredNodeTransition`**, one record
  naming `Expected` and `New`.
* **`IDriverDelegate.SelectNumTriggersForJob` is `CountTriggersForJob`.**
* **`IDriverDelegate.ValidateSchema` added**; a delegate of your own used to skip schema validation — see
  [`ValidateSchema` is part of `IDriverDelegate`](#validateschema-is-part-of-idriverdelegate).
* **`IDriverDelegate.ApplyTriggerFired` added**, one round trip per trigger fire — see
  [Batched trigger fire](#batched-trigger-fire).
* [**`IDriverDelegate.UpdateFiredTrigger` removed**](#batched-trigger-fire).
* [**`IDriverDelegate` gains a transition-list `UpdateTriggerStatesForJobFromOtherState` and a state-filtered `SelectTriggerKeysForJob`**](#batched-trigger-fire).
* **`ITriggerPersistenceDelegate.TryDescribeUpdateExtendedTriggerProperties` added**, a default interface
  member returning `false` — see [Batched trigger fire](#batched-trigger-fire).
* [**`IDriverDelegate` gains set-taking `UpdateTriggerStatesForJobsFromOtherState` and `DeleteFiredTriggers`**](#batched-cluster-recovery).
* [**`IDriverDelegate.SelectTriggerKeysInState` added**](#batched-cluster-recovery).
* **`IDriverDelegate` gains six set-shaped default interface members**, so existing delegates are
  unaffected: `InsertFiredTriggers`, `SelectStoredTriggerHeaders`, a key-set
  `UpdateTriggerStatesFromOtherStates`, `SelectTriggerKeysForJobs`, `SelectPausedJobGroups`,
  `InsertPausedJobGroups` — see
  [Batched trigger acquisition, and the sets pause and resume work on](#batched-trigger-acquisition-and-the-sets-pause-and-resume-work-on).
* **`IDriverDelegate.FiltersAcquisitionJobTypeExclusions` added**, a default interface member answering
  `false` (`StdAdoDelegate` answers `true`). A delegate answering `true` must filter excluded job types
  itself; the store skips its backstop — see
  [Batched trigger acquisition, and the sets pause and resume work on](#batched-trigger-acquisition-and-the-sets-pause-and-resume-work-on).
* **`TriggerAcquisitionCriteria.LiveNodeCutoff` is a `required DateTimeOffset`.** It was an optional `long`
  of `UtcTicks`, and omitting it meant "every node is dead". `AddPreferredNodeParameters` takes a
  `DateTimeOffset` too.
* **`FiredTriggerQuery.InstanceName` is `InstanceId`**, as are the scheduler-state members' `instanceName`
  parameters. The `INSTANCE_NAME` column is unchanged.
* **`StdAdoDelegate.ConvertFromProperty` returns `Dictionary<string, object?>`**, not the non-generic
  `System.Collections.IDictionary`.
* **`StdAdoDelegate.AddPagingParameters(cmd, int skip, int take, bool)`**: `skip`/`take` were `long`; they
  now match `PagedQuery.Skip`/`.Take`.
* **`StdAdoDelegate.ReadBytesFromBlob` takes a `DbDataReader` and is asynchronous**,
  `async ValueTask<byte[]?>` over `IsDBNullAsync` / `GetFieldValueAsync<byte[]>`; it took a blocking
  `System.Data.IDataReader`. **Behavior is unchanged**: a `NULL` column yields `null`, an empty blob an
  empty array, and `GetObjectFromBlob` treats both alike (`Length > 0`). An override changes its parameter
  type.
* **`StdAdoDelegate`'s date/time and time-span conversions are non-virtual**; only the boolean pair is a
  dialect seam — see
  [If you implement `IDriverDelegate`: the listing members](#if-you-implement-idriverdelegate-the-listing-members).
* **`StdAdoDelegate`'s column probes removed**: the `Has*Column` properties, the `Supports*Column` probes
  and `VerifyTriggersTableReachable` — see
  [The optional columns are required, so the probes are gone](#the-optional-columns-are-required-so-the-probes-are-gone).
* **`GetSelectNextTriggerToAcquireWith*Sql` removed** (`…WithExecutionGroupSql`, `…WithPreferredNodeSql`,
  `…WithPreferredNodeOnlySql`) — see
  [The three extra acquisition SQL hooks went with them](#the-three-extra-acquisition-sql-hooks-went-with-them).
* **`StdAdoDelegate.GetSelectNextTriggerToAcquireSql(int maxCount)` is
  `GetSelectNextTriggerToAcquireSql(TriggerAcquisitionSqlShape shape)`.** An override passes the whole shape
  to `base`.
* **`StdAdoDelegate.GetRowLimit(int count)` added**, returning the dialect's row-limiting clause as a
  `SqlRowLimit` — see [Row limiting is a slot, not a splice](#row-limiting-is-a-slot-not-a-splice).
* **`Quartz.Impl.AdoJobStore.SqlRowLimit` added**: a `readonly record struct` with `InProjection`,
  `AtStatementEnd`, `InEnclosingSelect`, and `Unlimited` for a database that cannot limit rows.
* **Group matchers translate to SQL correctly.** `SelectTriggerGroups`, `DeletePausedTriggerGroup` and both
  `UpdateTriggerGroupStateFromOtherState(s)` members use `=` for an equality matcher instead of `LIKE`.
  `LIKE` patterns escape `%`, `_` and the escape character with an explicit `ESCAPE` clause, so a group
  named `50%` matches itself. The escape character is `!`, because `ESCAPE '\'` is a syntax error in MySQL.
* **`StdAdoConstants` and `IAdoUtil` are internal.** Schema names stay public on `AdoConstants`, now a
  static class rather than a base class.
* **`StdAdoConstants` group and fired-trigger statements were split.** `SqlDeletePausedTriggerGroup`,
  `SqlSelectTriggerGroupsFiltered`, `SqlUpdateTriggerGroupStateFromState` and
  `SqlUpdateTriggerGroupStateFromStates` are `…Equals` / `…Like` pairs; the FIRED_TRIGGERS statements are
  `SqlSelectFiredTriggers` / `SqlDeleteFiredTriggers` plus `SqlFiredTrigger*Predicate` fragments.
* **`StdAdoConstants.SqlSelectCountExecutingFiredTriggersOfTrigger` removed**;
  `SqlSelectCountExecutingFiredTriggersOfJob` remains.
* **Parameter names spelled out on the ADO.NET surface:** `IDriverDelegate.SelectJobDetail`'s
  `classLoadHelper` is `loadHelper`; `ts` is `misfireTime`; `AdoJobStoreBase.ReleaseLock`'s `doIt` is
  `shouldRelease`; `StdAdoDelegate.AddTriggerPersistenceDelegate`'s `del` is `persistenceDelegate`;
  `TriggerPropertyBundle`'s `sb` is `scheduleBuilder`; `CronTriggerImpl.WillFireOn`'s `test` is `timeUtc`;
  `TriggerFireTimes.Compute`'s `numTimes` is `numberOfTimes`.

## Appendix: what happened to a name

An index by old name, for a build error about one type. Each row links to the section that explains it.

* It is derived from the public API baselines both branches keep, under `src/Quartz.Tests.Unit/Verify/`
  and `src/Quartz.Tests.AspNetCore/Verify/`, so **every** public type 3.x had and 4.0 does not is here,
  across every package.
* A family that went at once has one entry: the fourteen `xsd.exe`-generated classes of
  `Quartz.Xml.JobSchedulingData20`, the nested structs of `MisfireInstruction`, the three `*Support`
  listener bases and the replaced dashboard DTOs are each explained in their section.
* Two whole-surface changes are not listed per type: `Task` became [`ValueTask`](#tasks-changed-to-valuetask)
  on nearly every member, and namespaces moved —
  [`Quartz.Spi` is `Quartz.Extensibility` and `Quartz.Simpl` is `Quartz.Impl`](#quartz-spi-and-quartz-simpl-were-renamed),
  and three packages folded into `Quartz` ([Package Changes](#package-changes)). **A type that only changed
  namespace keeps its name and is not listed**; check that first.
* *Internal* means the type is still there and still working, but no longer part of the contract. If you
  derived from one, [open an issue](https://github.com/quartznet/quartznet/issues) rather than working
  around it.
* Both tables are sorted by the name you would have typed: the type name without its namespace, and
  `Type.Member` in the second. A few rows name a type only a 4.0 pre-release had, such as
  `AdoJobStoreDependencies` — see [Appendix: if you ran a 4.0 pre-release](#appendix-if-you-ran-a-4-0-pre-release).

For the raw delta of one package, `git diff` its baseline across the two branches. Package boundaries
moved, so match the files up first:

| 3.x baseline | 4.x baseline |
|---|---|
| `PublicApiTest_Quartz` | `PublicApiTest_Quartz`, which also absorbs DI, Hosting and SystemTextJson |
| `PublicApiTest_Quartz.Extensions.DependencyInjection` | folded into `Quartz` (`Quartz.Configuration`) |
| `PublicApiTest_Quartz.Extensions.Hosting` | folded into `Quartz` (`src/Quartz/Hosting/`) |
| `PublicApiTest_Quartz.Serialization.SystemTextJson` | folded into `Quartz` (`SystemTextJsonObjectSerializer`) |
| `PublicApiTest_Quartz.Serialization.Json` | `PublicApiTest_Quartz.Serialization.Newtonsoft` |
| `Quartz.Jobs`, `Quartz.Plugins`, `Quartz.Plugins.TimeZoneConverter`, `Quartz.Extensions.Redis`, `Quartz.AspNetCore`, `Quartz.Dashboard` | same name on both sides |
| `PublicApiTest_Quartz.OpenTracing` | dropped; there is no 4.x package |
| (no 3.x baseline; its old `OpenTelemetry` dependency fails restore there) | dropped; subscribe to the `Quartz` activity source directly |
| — | `PublicApiTest_Quartz.HttpClient`, new in 4.x |

3.x snapshots `net10.0` only, so its `net472` and `REMOTING` surface is not in that diff.

The 4.x baselines render three things the 3.x ones do not. A cross-branch diff shows them on every line
they touch; none is an API change:

| In a 4.x baseline | Meaning |
|---|---|
| `public sealed record Foo` / `public readonly record struct Foo` | The type is a record. 3.x renders it as a class or struct, so a record↔class change was invisible there |
| A trailing `// default implementation` on an interface member | An implementor may omit the member; making it abstract would break every implementor |
| `// explicit interface implementation: …` inside a type | An explicit implementation, private in metadata, which 3.x rendered nowhere |

`PublicApiRendering` in the two test projects generates these, and fails rather than annotating silently
when its reading of a line and the rendering disagree.

### Types that were removed, internalized or renamed

| Type | What happened | Use instead |
|---|---|---|
| `Quartz.Impl.AdoJobStore.AdoJobStoreBase` (3.x `JobStoreSupport`) | Internal | [`IJobStore`, `DelegatingJobStore` or `IDriverDelegate`](#the-ado-net-store-is-a-store-not-a-base-class) |
| `Quartz.Impl.AdoJobStore.AdoJobStoreDependencies` | Internal | Nothing; no derived store exists (as above) |
| `Quartz.Impl.AdoJobStore.AdoJobStoreUtil` | Internal | Nothing; [statement text is not a contract](#sealed-and-internalized-types) |
| `Quartz.AdoProviderExtensions` | Renamed `PersistentStoreBuilderExtensions` | The same `Use*` methods, on `IPersistentStoreBuilder` instead of `SchedulerBuilder.PersistentStoreOptions`; [two swapped meaning](#the-sqlite-extension-methods-swapped-names) |
| `Quartz.SchedulerBuilder.AdoProviderOptions` | Removed | [`DataSourceOptions`](#a-data-source-is-defined-referred-to-or-handed-over) |
| `Quartz.AdoProviderOptionsExtensions` | Removed | [`DataSourceOptions.UseRegisteredDataSource`](#adddatasourceprovider-went-with-it), for its one member `UseDataSourceConnectionProvider()` |
| `Quartz.Impl.AdoJobStore.AdoUtil` | Internal, with `IAdoUtil` | Nothing; [parameter binding is not an extension point](#sealed-and-internalized-types) |
| `Quartz.Simpl.BinaryObjectSerializer` | Removed | [`SystemTextJsonObjectSerializer` or `NewtonsoftJsonObjectSerializer`](#serializable-survives-only-where-a-database-blob-needs-it); `BinaryFormatter` throws on .NET 9 |
| `Quartz.Listener.BroadcastJobListener` | Removed | [Register each listener](#the-broadcast-listeners-are-gone); every one is notified |
| `Quartz.Listener.BroadcastSchedulerListener` | Removed | As above |
| `Quartz.Listener.BroadcastTriggerListener` | Removed | As above |
| `Quartz.CalendarIntervalTriggerBuilderExtensions` | Removed | [`TriggerConfiguratorExtensions`](#one-family-of-withxschedule-extensions) |
| `Quartz.SchedulerBuilder.ClusterOptions` | Removed | [`ClusteringOptions`](#clustering-is-configured-in-one-place) |
| `Quartz.Impl.AdoJobStore.ExternalTransactionJobStore` (3.x `JobStoreCMT`) | Internal | [`UsePersistentStore(store => store.UseAmbientTransactions())`](#the-ado-net-store-is-a-store-not-a-base-class); `quartz.jobStore.type` still names it |
| `Quartz.Impl.AdoJobStore.LocalTransactionJobStore` (3.x `JobStoreTX`) | Internal | As above |
| `Quartz.Impl.AdoJobStore.RecoverMisfiredJobsResult` | Internal | Nothing; it was a `protected` method's return type |
| `Quartz.Impl.AdoJobStore.Common.ConfigurationBasedDbMetadataFactory` | Internal | The metadata factory on `UseGenericDatabase` |
| `Quartz.CronScheduleTriggerBuilderExtensions` | Removed | [`TriggerConfiguratorExtensions`](#one-family-of-withxschedule-extensions) |
| `Quartz.DailyTimeIntervalTriggerBuilderExtensions` | Removed | [`TriggerConfiguratorExtensions`](#one-family-of-withxschedule-extensions) |
| `Quartz.Util.DataReaderExtensions` | Internal | None; `IDataReader` helpers for Quartz's own reads |
| `Quartz.Util.DBConnectionManager` | Removed | [`UseConnectionProvider`](#the-connection-manager-is-gone), then resolve `IDbProvider`; `.Instance` is gone |
| `Quartz.Impl.AdoJobStore.Common.DbMetadataFactory` | Internal | The metadata factory on `UseGenericDatabase` |
| `Quartz.Impl.AdoJobStore.DBSemaphore` | Renamed `DbLockHandler` | [The same abstract base](#the-semaphores-are-lock-handlers), still public |
| `Quartz.Simpl.DedicatedThreadPool` | Internal | [`IQuartzBuilder.UseThreadPool(IThreadPool)`](#the-thread-pool-is-asynchronous) for your own pool |
| `Quartz.Logging.DiagnosticHeaders` | Renamed `Quartz.Diagnostics.ActivityTags` | [Same constant names; every value is `quartz.*`](#job-execution-metrics) |
| `Quartz.Util.DictionaryExtensions` | Removed | [None](#other-breaking-changes) |
| `Quartz.Impl.DirectSchedulerFactory` | Removed | [`QuartzSchedulerBuilder`](#removed), with `UseThreadPool(IThreadPool)` / `UseJobStore(IJobStore)` for pre-built parts |
| `Quartz.Impl.AdoJobStore.Common.EmbeddedAssemblyResourceDbMetadataFactory` | Internal | The metadata factory on `UseGenericDatabase` |
| `Quartz.Util.FileUtil` | Internal | None; it resolved a path relative to the base directory |
| `Quartz.Simpl.HostnameInstanceIdGenerator` | Renamed `HostNameInstanceIdGenerator`, and internal | [Your own `IInstanceIdGenerator`](#other-breaking-changes); the old spelling in `quartz.scheduler.instanceIdGenerator.type` resolves, with a warning |
| `Quartz.ICancellableJobExecutionContext` | Removed | [`IScheduler.Interrupt` to request, `IJobExecutionContext.CancellationToken` to observe](#interruption-has-two-names-not-three) |
| `Quartz.IDashboardAuthorizationFilter` | Removed | [`QuartzDashboardOptions.AuthorizationPolicy`](#other-breaking-changes); nothing invoked the filter |
| `Quartz.Logging.IJobDiagnosticData` | Removed | [`IJobExecutionContext`](#other-breaking-changes), read from a listener |
| `Quartz.Core.IJobRunShellFactory` | Internal | None; how a fire is wrapped is not a contract |
| `Quartz.IJobWrapper` | Removed | [`JobScope.State`](#the-job-factory-hands-out-a-scope) for per-fire state |
| `Quartz.Logging.ILogProvider` | Removed with LibLog | [`ILoggerFactory`](#logging) |
| `Quartz.SchedulerBuilder.InMemoryStoreOptions` | Removed | `InMemoryJobStoreOptions`, through `UseInMemoryStore(configure)` |
| `Quartz.Dashboard.Services.InProcessQuartzApiClient` | Internal | [Resolve `IQuartzApiClient`](#other-breaking-changes) |
| `Quartz.Simpl.InternalTriggerState` | Removed | [`Quartz.Extensibility.StoredTriggerState`](#trigger-states-are-typed-on-the-driver-delegate), shared by every store |
| `Quartz.IPropertyConfigurationRoot` | Removed | [Typed options](#code-first-configuration-is-typed) |
| `Quartz.Impl.AdoJobStore.ISemaphore` | Renamed `ILockHandler` | [`ObtainLock` is `AcquireLock`](#the-semaphores-are-lock-handlers); `ReleaseLock` and `RequiresConnection` are unchanged |
| `Quartz.Impl.AdoJobStore.ITablePrefixAware` | Removed | [`ILockHandler.Initialize(LockHandlerContext)`](#a-lock-handler-is-told-which-scheduler-it-locks-for) |
| `Quartz.IPropertyConfigurer` | Removed | [Typed options](#code-first-configuration-is-typed) |
| `Quartz.IPropertySetter` | Removed | [Typed options](#code-first-configuration-is-typed) |
| `Quartz.Dashboard.Services.IQuartzApiClientExecutionLimits` | Removed | `IQuartzApiClient`, which has `GetExecutionLimits` |
| `Quartz.Simpl.IRemotableQuartzScheduler` | Removed | Nothing; [.NET Remoting is not supported](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Spi.IRemotableSchedulerProxyFactory` | Removed | Nothing; [`Quartz.HttpClient`](#remoting-a-scheduler-is-not-a-quartz-concern) reaches a remote scheduler over HTTP |
| `Quartz.Spi.ISchedulerExporter` | Removed | Nothing; [`AddQuartzHttpApi` / `MapQuartzHttpApi`](#remoting-a-scheduler-is-not-a-quartz-concern) serve a scheduler over HTTP |
| `Quartz.IServiceCollectionQuartzConfigurator` | Renamed `IQuartzBuilder` | [The same members](#the-standalone-builder-is-the-same-builder), shared with the standalone builder |
| `Quartz.Spi.ITypeLoadHelper` | Renamed `Quartz.Extensibility.ITypeLoader` | `Initialize()` is gone; `LoadType` is the whole interface. `AdoJobStoreBase.TypeLoadHelper` and `DriverDelegateContext.TypeLoadHelper` are `TypeLoader`; `UseTypeLoader<T>()` already used the new name |
| `Quartz.Impl.JobDetailImpl` | Internal | `JobBuilder.Create<TJob>()`; read an `IJobDetail` |
| `Quartz.JobFactoryOptions` | Kept, emptied and refilled | `AllowDefaultConstructor` and `CreateScope` (`[Obsolete]` no-ops on 3.x) are gone; the `sealed` type carries [`ConfigureScope`](#the-job-factory-hands-out-a-scope) |
| `Quartz.Plugin.Interrupt.JobInterruptMonitorPlugin` | Removed | [`q.AddJobTimeout(TimeSpan)` or `[JobTimeout("hh:mm:ss")]`](#jobinterruptmonitorplugin-is-retired-a-job-timeout-is-middleware), in the core package; `JobDataMapKeyAutoInterruptable` and `JobDataMapKeyMaxRunTime` are gone |
| `Quartz.Core.JobRunShell` | Internal | None; use `IJobListener` to observe a fire |
| `Quartz.Impl.AdoJobStore.JobStoreCMT` | Renamed `ExternalTransactionJobStore`, and internal | [The old name still resolves in `quartz.jobStore.type`](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use), with a warning; [naming it in code](#the-ado-net-store-is-a-store-not-a-base-class) breaks |
| `Quartz.Impl.AdoJobStore.JobStoreTX` | Renamed `LocalTransactionJobStore`, and internal | As above |
| `Quartz.Impl.AdoJobStore.JobStoreSupport` | Renamed `AdoJobStoreBase`, and internal | Abstract, so never a configuration string. [Implement `IJobStore` or wrap one with `DelegatingJobStore`](#the-ado-net-store-is-a-store-not-a-base-class) |
| `Quartz.Impl.Triggers.AbstractTrigger` | Renamed `TriggerBase` | Never a stored JSON `$type` (abstract); the five `*TriggerImpl` names are unchanged. [One binary-blob caveat](#triggerbase-property-removals) |
| `Quartz.Impl.AdoJobStore.SimplePropertiesTriggerPersistenceDelegateSupport` | Renamed `SimplePropertiesTriggerPersistenceDelegateBase` | As above |
| `Quartz.Simpl.JsonObjectSerializer` | Renamed `Quartz.Impl.NewtonsoftJsonObjectSerializer`, still in `Quartz.Serialization.Newtonsoft` | [`UseNewtonsoftJsonSerializer()`](#json-serialization); as a `quartz.serializer.type` string, `Quartz.Impl.NewtonsoftJsonObjectSerializer, Quartz.Serialization.Newtonsoft` |
| `Quartz.JsonSchedulingOptions` | Merged into `FileSchedulingOptions` | [It was identical to `XmlSchedulingOptions`](#other-breaking-changes) |
| `Quartz.JsonSerializerOptions` | Removed | [`UseNewtonsoftJsonSerializer`'s callback receives the `NewtonsoftJsonSerializerRegistry`](#custom-trigger-and-calendar-serializers-are-no-longer-static); `registerTriggerConverters` is a method parameter |
| `Quartz.Logging.LogProviders.LibLogException` | Removed with LibLog | [None](#logging) |
| `Quartz.Core.ListenerManagerImpl` | Internal | [`IScheduler.ListenerManager`, typed `IListenerManager`](#listener-api-changes) |
| `Quartz.Logging.LogContext` | Removed with LibLog | [`LogProvider.SetLogProvider(ILoggerFactory)`](#logging) |
| `Quartz.Logging.Logger` (delegate) | Removed with LibLog | [`ILogger`](#logging) |
| `Quartz.Logging.LogLevel` | Removed with LibLog | [`Microsoft.Extensions.Logging.LogLevel`](#logging) |
| `Quartz.Util.ObjectExtensions` | Internal | [None](#other-breaking-changes) |
| `Quartz.Util.ObjectUtils` | Removed | None; [typed options](#code-first-configuration-is-typed) replaced setting properties from strings |
| `Quartz.SchedulerBuilder.PersistentStoreOptions` | Removed | `IPersistentStoreBuilder`, through `UsePersistentStore(configure)` |
| `Quartz.PropertiesHolder` | Removed | [Typed options](#removed) |
| `Quartz.Util.PropertiesParser` | Internal | None; [`QuartzPropertyBridge`](#flat-keys-still-work) is the only reader of flat `quartz.*` keys |
| `Quartz.PropertiesSetter` | Removed | [Typed options](#removed) |
| `Quartz.QuartzConfiguratorExecutionLimitsExtensions` | Removed | [`IQuartzBuilder.UseExecutionLimits(Action<ExecutionLimitsBuilder>)`](#execution-limits-are-built-once-then-frozen) |
| `Quartz.OpenTracing.QuartzDiagnosticOptions` | Removed with its package | [`AddSource(QuartzInstrumentation.ActivitySourceName)`](packages/opentelemetry-integration.md); job execution is on `Activity` through `QuartzActivitySource` |
| `Quartz.Util.QuartzEnvironment` | Internal | `System.Environment`, or `IConfiguration` for settings |
| `Quartz.Core.QuartzRandom` | Internal | `System.Random` |
| `Quartz.Core.QuartzScheduler` | Internal | [Resolve `IScheduler` or `ISchedulerFactory`](#sealed-and-internalized-types) |
| `Quartz.Core.QuartzSchedulerResources` | Internal | [`QuartzSchedulerOptions`](#sealed-and-internalized-types) |
| `Quartz.Core.QuartzSchedulerThread` | Internal | None; the scheduling loop is not an extension point |
| `Quartz.RecurrenceTriggerBuilderExtensions` | Removed | [`TriggerConfiguratorExtensions`](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.RemoteScheduler` | Removed | [`Quartz.HttpClient`](#remoting-a-scheduler-is-not-a-quartz-concern) |
| `Quartz.Simpl.RemotingSchedulerProxyFactory` | Removed | As above |
| `Quartz.ScheduleBuilder<T>` | Removed | [Implement `IScheduleBuilder` directly](#schedulebuilder-t-is-gone) |
| `Quartz.SchedulerBuilder` | Renamed `QuartzSchedulerBuilder` | [`Create` takes the `AddQuartz` callback](#the-standalone-builder-is-the-same-builder); configure against `IQuartzBuilder` |
| `Quartz.SchedulerExtensions` | Removed | Its three members are on `IScheduler`: `GetExecutionLimits`, `SetExecutionLimits`, `UpdateTriggerDetails` |
| `Quartz.SchedulerMetaData` | Renamed `SchedulerMetadata` | [A `sealed record` returned by `IScheduler.GetMetadata()`](#schedulermetadata-replaces-schedulermetadata) |
| `Quartz.SchedulerPluginConfigurationExtensions` | Removed | [`IQuartzBuilder.AddPlugin<T>()`](#plugins-are-registered-like-listeners) |
| `Quartz.Core.SchedulerSignalerImpl` | Internal | [Take `ISchedulerSignaler` through your constructor](#spi-changes) |
| `Quartz.Configuration.QuartzConfigurationHelper` | Internal | Pass the section to `AddQuartz(configuration)`, or to `QuartzSchedulerBuilder.UseConfiguration(configuration)` without a host; `QuartzOptions.ToProperties()` stays. The public `Quartz.Configuration` namespace is now empty |
| `Quartz.ServiceCollectionExtensions` | Split into `Quartz.QuartzServiceCollectionExtensions` and `Quartz.QuartzBuilderExtensions` | Only a static-form call (`ServiceCollectionExtensions.AddQuartz(services, …)`) changes; the old name caused CS0104 beside an application's own `ServiceCollectionExtensions` |
| `Quartz.Simpl.SimpleInstanceIdGenerator` | Internal | Still the default; register your own `IInstanceIdGenerator` to replace it |
| `Quartz.SimpleScheduleTriggerBuilderExtensions` | Removed | [`TriggerConfiguratorExtensions`](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.AdoJobStore.SimpleSemaphore` | Internal, renamed `InProcessLockHandler` | The in-process lock used when database locking is off; [implement `ILockHandler`](#the-semaphores-are-lock-handlers) for your own |
| `Quartz.Xml.XMLSchedulingDataProcessor` | Internal, respelled `XmlSchedulingDataProcessor` | `UseXmlSchedulingConfiguration()`, the supported entry point. The processor's constructor needed an `ITypeLoader`, which has no public implementation. It was the last public type in `Quartz.Xml` |
| `Quartz.Simpl.SimpleTypeLoadHelper` | Internal, renamed `SimpleTypeLoader` | Your own `ITypeLoader`; the old name in configuration resolves, with a warning |
| `Quartz.Plugin.Management.ShutdownHookPlugin` | Removed | [`AddQuartzHostedService(o => o.WaitForJobsToComplete = …)`](#shutdownhookplugin-is-retired-the-host-already-shuts-the-scheduler-down) under a host, or `scheduler.Shutdown(waitForJobsToComplete: true)` on your exit path; `UseShutdownHook` and `ShutdownHookOptions` are gone |
| `Quartz.Impl.AdoJobStore.StdAdoConstants` | Internal | [`AdoConstants`](#sealed-and-internalized-types) for table, column and state names |
| `Quartz.Impl.AdoJobStore.StdRowLockSemaphore` | Renamed `SelectForUpdateLockHandler` | [The old name in configuration resolves](#the-semaphores-are-lock-handlers), with a warning |
| `Quartz.Impl.AdoJobStore.PostgreSQLRowLockSemaphore` | Renamed `PostgreSqlSelectForUpdateLockHandler` | As above |
| `Quartz.Impl.AdoJobStore.UpdateLockRowSemaphore` | Renamed `UpdateRowLockHandler` | As above |
| `Quartz.Impl.AdoJobStore.UpdateLockRowSemaphoreMOT` | Renamed `SqlServerMemoryOptimizedUpdateRowLockHandler` | As above |
| `Quartz.Impl.StdJobRunShellFactory` | Internal | None; see `IJobRunShellFactory` above |
| `Quartz.Impl.StdScheduler` | Internal | [Resolve `IScheduler`](#sealed-and-internalized-types) |
| `Quartz.Impl.StdSchedulerFactory` | Removed, with all 47 constants | `QuartzSchedulerBuilder.Create().UseProperties(properties)`; [every constant and member](#stdschedulerfactory-is-gone) |
| `Quartz.SchedulerBuilder.StoreOptions` | Removed | Nothing; it was the base of the two store option classes, now `InMemoryJobStoreOptions` and `IPersistentStoreBuilder` |
| `Quartz.Util.StringExtensions` | Internal | None |
| `Quartz.Simpl.SystemPropertyInstanceIdGenerator` | Internal | `quartz.scheduler.instanceId = SYS_PROP` still selects it; in code, register your own `IInstanceIdGenerator` |
| `Quartz.SystemTime` | Removed | [`TimeProvider`](#systemtime-replaced-with-timeprovider) |
| `Quartz.TimeOfDay` | Removed | [`TimeOnly`](#timeofday-became-timeonly) |
| `Quartz.Plugin.TimeZoneConverter.TimeZoneConverterPlugin` | Removed | [`UseTimeZoneConverter()`](#timezoneconverterplugin-is-a-resolver-registration); the `Quartz.Plugins.TimeZoneConverter` package stays |
| `Quartz.Util.TimeZoneUtil` | Renamed `Quartz.TimeZones` | [`FindTimeZoneById` is `FindById`, `CustomResolver` is `AddResolver(...)`](#timezoneutil-became-quartz-timezones) |
| `Quartz.TriggerExtensions` | Removed | [`TriggerConfiguratorExtensions`](#one-family-of-withxschedule-extensions) |
| `Quartz.Impl.AdoJobStore.TriggerStatus` | Removed | [`StoredTriggerHeader`](#the-driver-delegate-speaks-in-records), from `IDriverDelegate.SelectTriggerHeader` |
| `Quartz.TriggerTimeComparator` | Internal | None; order by next fire time, then priority descending, then key |
| `Quartz.TriggerUtils` | Renamed `Quartz.Extensibility.TriggerFireTimes` | [`ComputeFireTimes` is `Compute`, `ComputeFireTimesBetween` is `ComputeBetween`, `ComputeEndTimeToAllowParticularNumberOfFirings` is `ComputeEndTimeForCount`](#triggerutils-became-triggerfiretimes) |
| `Quartz.Simpl.TriggerWrapper` | Internal | None; [`RAMJobStore`'s per-trigger state](#ramjobstore-is-sealed) |
| `Quartz.UnableToInterruptJobException` | Removed | [Nothing throws it](#unabletointerruptjobexception-is-gone) |
| `Quartz.XmlSchedulingOptions` | Merged into `FileSchedulingOptions` | [See Other Breaking Changes](#other-breaking-changes) |

### Members that were removed

Removals on types that are still public and open. Members of a type listed above, and `protected`
members that went when their type was sealed, are not repeated.

| 3.x member | What happened | Use instead |
|---|---|---|
| `AbstractTrigger.CompareTo(ITrigger)` | Removed; neither `TriggerBase` nor `ITrigger` implements `IComparable<ITrigger>` | [`trigger.Key.CompareTo(other.Key)` or `triggers.OrderBy(t => t.Key)`](#the-trigger-family-interfaces-are-read-models). `List<ITrigger>.Sort()` still compiles and now throws |
| `AbstractTrigger.FullJobName` | Removed | [`JobKey.ToString()`](#triggerbase-property-removals) |
| `AdoConstants.AliasColumnNextFireTime` | Removed | None needed; the alias (`ALIAS_NXT_FR_TM`) is used by no 4.x statement and was never a column |
| `new CronExpression(expr, hashKey)`, `new CronExpression(expr, hashSeed)` | Removed; they made `new CronExpression(expr, null)` ambiguous | [`CronExpression.ParseWithHash(expr, hashKey)` / `ParseWithHash(expr, hashSeed)`](#the-hash-key-is-a-parse-argument-not-a-constructor-overload) |
| `CronExpression`'s `protected` constants, fields and parse hooks, and `OnDeserialization` | Gone; the type is `sealed` and no longer implements `IDeserializationCallback` | [None](#the-parser-is-not-a-subclassing-seam) |
| `CronExpression.GetExpressionSummary()`, `ICronTrigger.GetExpressionSummary()`, `CronTriggerImpl.GetExpressionSummary()` | Removed | [None](#the-parser-is-not-a-subclassing-seam); it dumped the parsed sets in an undocumented format. `CronExpressionString` is the expression |
| `CronExpression.GetTimeBefore(t)` | Renamed | `GetPreviousValidTimeBefore(t)`, the pair of `GetNextValidTimeAfter` |
| `CronExpression.IsValidExpression(expr)` | Removed | [`CronExpression.TryParse(expr, out _)`](#cronexpression-parses-without-throwing-and-says-it-is-equatable) |
| `CronExpression.IsValidExpression(expr, hashKey)` | Removed | `CronExpression.TryParseWithHash(expr, hashKey, out _)` |
| `CronExpression.MaxYear` | Removed (a `public static readonly int`) | [None](#cronexpression-is-immutable); it was `DateTime.Now.Year + 100`, computed once per process |
| `CronExpression.ValidateExpression(expr)` | Removed | `CronExpression.Parse(expr)`, which returns the parsed expression |
| `CronExpression.ValidateExpression(expr, hashKey)` | Removed | `CronExpression.ParseWithHash(expr, hashKey)` |
| `CronTriggerImpl.GetTimeAfter(DateTimeOffset)` | Removed (was `protected`) | `GetFireTimeAfter(DateTimeOffset?)`, or `CronExpression.GetNextValidTimeAfter` |
| `CronTriggerImpl.GetTimeBefore(DateTimeOffset)` | Renamed (`protected`) | `GetPreviousValidTimeBefore(DateTimeOffset)`, like the `CronExpression` member it calls |
| `CronTriggerImpl.YearToGiveupSchedulingAt` | Removed (a `protected const`) | None |
| `DateBuilder.ValidateDayOfMonth`, `.ValidateHour`, `.ValidateMinute`, `.ValidateMonth`, `.ValidateSecond`, `.ValidateYear` | Removed | [None](#datebuilder-s-static-factories-are-gone); the `sealed` builder validates its own arguments |
| `DbProvider.CreateParameter()` | Removed | `CreateCommand().CreateParameter()` |
| `DbProvider.DbProviderSectionName`, `.GenerateValidProviderNamesInfo()` | Removed (`protected`) | [None](#other-breaking-changes); leftovers of the process-wide provider registry |
| `DirtyFlagMap<TKey, TValue>`, `StringKeyDirtyFlagMap` | Internal | [`JobDataMap` / `SchedulerContext`](#jobdatamap-and-schedulercontext-stand-alone); the typed accessors are extension members |
| `DirtyFlagMap.Clone()` | Removed | Construct a new map from the old one |
| `DirtyFlagMap.Dirty`, `.ClearDirtyFlag()` | Internal | `SchedulerConstants.ForceJobDataMapDirty` forces a rewrite |
| `DirtyFlagMap.EntrySet()` | Removed | `GetEnumerator()`, or `foreach` over the map |
| `DirtyFlagMap.KeySet()` | Removed | `Keys` |
| `DirtyFlagMap.Put()`, `.PutAll()` | Removed; `[Obsolete]` in 3.x | [`map[key] = value`](#jobdatamap-and-schedulercontext-stand-alone), in a loop for `PutAll` |
| `DirtyFlagMap.WrappedMap` | Removed | None; the map is the dictionary |
| `IDriverDelegate.UpdateTriggerPreferredNode`, `StdAdoDelegate.UpdateTriggerPreferredNode` | Removed | [`UpdateTriggerPreferredNodeConditional`](#the-preferred-node-is-a-value) (a compare-and-swap), or `IScheduler.UpdateTriggerDetails` from outside the store |
| `InvalidConfigurationException()` | Removed; the `sealed` type keeps only `(string message)` | Say what was invalid |
| `IListenerManager.AddJobListenerMatcher`, `.RemoveJobListenerMatcher`, `.SetJobListenerMatchers`, `.GetJobListenerMatchers`, and their `TriggerListener` twins | Removed | [`AddJobListener(listener, params matchers)` / `AddTriggerListener(listener, params matchers)`](#matchers-are-given-at-registration); registering a name again replaces listener and matchers together |
| `IObjectSerializer.Initialize()` | Removed | [None](#names-that-were-normalized) |
| `IScheduler.InStandbyMode` | Removed | [`Status is SchedulerStatus.Created or SchedulerStatus.Standby or SchedulerStatus.ShuttingDown`](#a-scheduler-s-lifecycle-is-one-value) |
| `IScheduler.IsShutdown` | Removed | [`Status is SchedulerStatus.Shutdown`](#a-scheduler-s-lifecycle-is-one-value) |
| `IScheduler.IsStarted` | Removed | [`Status is not SchedulerStatus.Created`](#a-scheduler-s-lifecycle-is-one-value); `Status is SchedulerStatus.Running` for "is running now" |
| `IScheduler.JobFactory` (setter-only) | Removed, from `IScheduler`, `StdScheduler`, `DelegatingScheduler` and `HttpScheduler` | [`IQuartzBuilder.UseJobFactory(IJobFactory)` or `UseJobFactory<T>()`](#the-factory-is-set-where-the-scheduler-is-built); `ConfigureJobScope(...)` if it only seeded the DI scope. A factory whose dependency does not exist yet resolves it in `CreateJob` |
| `IScheduler.PauseJobs(GroupMatcher<JobKey>)`, `.ResumeJobs(GroupMatcher<JobKey>)`, `.PauseTriggers(GroupMatcher<TriggerKey>)`, `.ResumeTriggers(GroupMatcher<TriggerKey>)`, and the same four on `IJobStore` | Renamed | [`PauseJobGroups`, `ResumeJobGroups`, `PauseTriggerGroups`, `ResumeTriggerGroups`](#pausing-by-matcher-is-a-group-operation-and-is-named-for-one); the key-set overloads keep the old names |
| `JobBuilder.CreateForAsync<T>()` | Removed | `JobBuilder.Create<T>()`; every job is asynchronous since 3.0 |
| `JobDataMap.GetCharFromString`, `.GetDateTimeValue`, `.GetGuidValue`, `.GetTimeSpanValue` and their `…FromString` twins | Removed with the other `…Value` accessors | [`Get<char>`, `Get<DateTime>`, `Get<Guid>`, `Get<TimeSpan>`](#one-accessor-per-type-for-the-types-job-data-is-made-of), which coerce the same way |
| `JobStoreSupport.calendarCache`, `.delegateType`, `.firstCheckIn` | Removed (`protected` fields) | None |
| `JobStoreSupport.GetTriggerNames(conn, matcher, ct)` | Removed (`protected`) | [The listing members became queries](#job-store-listings-became-queries) |
| `LogProvider.IsDisabled` | Removed | [Filter through the `ILoggerFactory`](#logging) |
| `LogProvider.SetCurrentLogProvider(ILogProvider)` | Removed with LibLog | [`LogProvider.SetLogProvider(ILoggerFactory)`](#logging) |
| `QuartzDashboardOptions.ApiPath` | Removed | None; [the dashboard's remote client is gone](#the-dashboard-reads-the-schedulers-in-its-own-process). `QuartzHttpApiOptions.ApiPath`, where the API is served, is unchanged |
| `SchedulerMetadata.Started`, `.InStandbyMode`, `.Shutdown` | Removed | [`Status`, a `required SchedulerStatus`](#a-scheduler-s-lifecycle-is-one-value) |
| `SimplePropertiesTriggerPersistenceDelegateSupport.SchedNameLiteral`, and the same member on `DbLockHandler` | Removed; `[Obsolete]` in 3.x | None; the scheduler name is a SQL parameter |
| `StdAdoDelegate.GetStorableJobTypeName(Type)` | Removed (`protected`) | `new JobType(type).FullName`, the spelling `JOB_CLASS_NAME` holds |
| `StdAdoDelegate.SchedulerNameLiteral` | Removed; `[Obsolete]` in 3.x | None; as above |
| `StringKeyDirtyFlagMap.GetKeys()` | Removed | `Keys` |
| `StringKeyDirtyFlagMap.GetNullableGuid()`, `.TryGetNullableGuid()` | Removed | [`TryGetGuid(key, out var value)`](#jobdatamap-s-typed-accessors-are-extension-members); `false` means what `null` did |
| `StringKeyDirtyFlagMap.Put()` (eight overloads), `.PutAll()` | Removed; `[Obsolete]` in 3.x | `map[key] = value` |
| `TaskSchedulingThreadPool.ThreadPriority` | Removed | None; [work runs on a `TaskScheduler`](#the-thread-pool-is-asynchronous) |
| `ZeroSizeThreadPool.AvailableThreadCount` | Removed | `PoolSize`, which is `0` |

### Types 4.0 added to the `Quartz` namespace

These cause the reverse error: **CS0104, "ambiguous reference"**, when a name Quartz now declares collides
with one of yours under `using Quartz;`. The errors name the right members on the wrong type, so they read
like a bug in your own code.

The list is every type in the 4.x `Quartz` namespace that was in the 3.x `Quartz` namespace of none of
`Quartz`, `Quartz.Extensions.DependencyInjection` or `Quartz.Extensions.Hosting`: **ninety-nine names**. A
project that referenced `Quartz.Serialization.SystemTextJson` on 3.x already had `JsonSerializationException`.

```text
AddCalendarOptions                      AddJobOptions                      AddTriggerOptions
AdoJobStoreOptions                      AndMatcher<T>                      CalendarIntervalTriggerMisfireInstruction
CalendarQuery                           ClusterNode                        ClusterNodeState
ClusteringOptions                       CronFormat                         CronTriggerMisfireInstruction
DailyTimeIntervalTriggerMisfireInstruction                                 DataMapExtensions
DataSourceOptions                       EverythingMatcher<T>               ExecutionGroupInFlight
ExecutionGroupLimit                     ExecutionGroupScope                ExecutionLimitScope
ExecutionLimitsBuilder                  ExecutionSlots                     FireInstance
FireInstanceQuery                       FireInstanceState                  GroupMatcher<T>
IJob<T>                                 IJobConfigurator<T>                IJobExecutionContextAccessor
IJobExecutionMiddleware                 IPersistentStoreBuilder            IQuartzBuilder
ISchedulerRegistry                      ITriggerConfigurator<T>            InMemoryJobStoreOptions
JobBuilder<T>                           JobDetailExtensions                JobExecutionContextInputExtensions
JobExecutionDelegate                    JobExecutionProcessException       JobGroup
JobGroupQuery                           JobHeader                          JobInputBuilderExtensions
JobInstantiationException               JobQuery                           JobTimeoutAttribute
JobType                                 JsonSerializationException         Key<T>
KeyMatcher<T>                           Matchers                           MonthDay
NameMatcher                             NameMatcher<T>                     NotMatcher<T>
OneOffJobOptions                        OrMatcher<T>                       PagedQuery
PagedResult<T>                          PersistentStoreBuilderExtensions   PreferredNode
QuartzBuilderExtensions                 QuartzHealthCheckExtensions        QuartzHealthCheckOptions
QuartzHostApplicationBuilderExtensions  QuartzSchedulerBuilder             QuartzSchedulerOptions
RecurrenceTriggerMisfireInstruction     RetryPolicy                        ScheduleJobOptions
ScheduledOneOffJob                      SchedulerErrorContext              SchedulerFactoryExtensions
SchedulerJobExtensions                  SchedulerMetadata                  SchedulerNotFoundException
SchedulerOrigin                         SchedulerQueryExtensions           SchedulerRegistration
SchedulerStatus                         SchedulingDataValidationException  SchemaProvisioning
ShutdownJobInterruption                 SimpleTriggerMisfireInstruction    StandaloneSchedulerFactory
StringMatcher<T>                        StringOperator                     SystemTextJsonConfigurationExtensions
ThreadPoolOptions                       TimeRange                          TimeZones
TriggerBuilder<T>                       TriggerConfiguratorExtensions      TriggerGroup
TriggerGroupQuery                       TriggerHeader                      TriggerQuery
TypeLoaderOptions
```

Check these first; an application or integration library plausibly declares them too:

| Name | Who else has one |
|---|---|
| `QuartzSchedulerOptions` | MassTransit's `QuartzSchedulerOptions` (six compile errors naming the right members on the wrong type). 3.x's equivalent was `Quartz.Core.QuartzSchedulerResources` |
| `JsonSerializationException` | **Newtonsoft.Json**, so a file with both `using Quartz;` and `using Newtonsoft.Json;` is ambiguous |
| `RetryPolicy` | Polly, MassTransit, the Azure SDKs, most in-house resilience code |
| `TimeRange`, `MonthDay` | ordinary domain vocabulary; NodaTime has `MonthDay` |
| `PagedResult<T>`, `PagedQuery` | most API and repository layers |
| `Key<T>` | was `Quartz.Util.Key<T>` |
| `Matchers`, `TimeZones` | were `Quartz.Impl.Matchers` and `Quartz.Util.TimeZoneUtil` |
| `ThreadPoolOptions`, `DataSourceOptions` | common options-class names |
| `JobType`, `SchedulerStatus`, `ClusterNode`, `ClusterNodeState` | anything with its own job or scheduler model |
| `StringOperator`, `ExecutionSlots`, `ExecutionGroupScope`, `ExecutionLimitScope` | concurrency and matching vocabulary |

Fix it with a using-alias in the affected file; neither library needs to change:

```csharp
using QuartzSchedulerOptions = Acme.Bus.QuartzSchedulerOptions;
```

One collision is gone: 3.x's `Quartz.ServiceCollectionExtensions`, the most common helper-class name in
.NET, is now `QuartzServiceCollectionExtensions` and `QuartzBuilderExtensions`. They hold extension
methods, so only a static-form call changes.

## Appendix: if you ran a 4.0 pre-release

For an application already running a 4.0 pre-release. Coming from 3.x, skip this: every correction is
described above as 4.0's behavior.

The public surface froze at `4.0.0-alpha.5`. Each later row is a deliberate exception (the API stated
something untrue) or a new refusal (no shape changed; only what a mis-stated configuration does).

::: details The build-by-build list

| Build | What changed | Where the 4.0 shape is described |
|---|---|---|
| alpha.2 | Every listener callback gained its `IScheduler` parameter. A listener written against alpha.1 is refused at registration | [Listeners are told which scheduler is calling](#listeners-are-told-which-scheduler-is-calling) |
| alpha.3 | Dashboard history and live events gained the instance id of the node they came from | [History and live events say which node they came from](#history-and-live-events-say-which-node-they-came-from) |
| alpha.4 | The health check moved from `Quartz.AspNetCore` to `Quartz`, with `QuartzHealthCheckOptions`. The four extension methods moved from `QuartzAspNetCoreConfigurationExtensions` to `QuartzHealthCheckExtensions`; `QuartzHealthCheck` / `SchedulerHealthCheckTarget` became internal to `Quartz`. **No call site changes**, only the package reference | [The health check is in `Quartz`, not `Quartz.AspNetCore`](#the-health-check-is-in-quartz-not-quartz-aspnetcore) |
| alpha.4 | An expression with a wildcard in one day field and values in the other fired every day of the month; it now fires only on the days the restricted field names | [Cron Parser Enhancements](#cron-parser-enhancements) |
| alpha.5 | `QuartzSchedulerBuilder` stopped re-declaring every `IQuartzBuilder` member (about sixty signatures). Move whatever was chained between `Create()` and the terminal call into `Create(q => q…)`; read `Services` and `SchedulerName` off the callback's argument | [The standalone builder is the same builder](#the-standalone-builder-is-the-same-builder) |
| alpha.5 | `TriggerMisfired` took its scheduler **first**; it now comes after the trigger. The old order compiles and is refused at registration | [`TriggerMisfired` takes the trigger first](#triggermisfired-takes-the-trigger-first) |
| beta.1 | `IScheduler`'s mutation members raise `ArgumentNullException`, not `SchedulerException("JobDetail cannot be null")` | [Null arguments raise `ArgumentNullException`](#null-arguments-raise-argumentnullexception) |
| beta.1 | `CronExpression.TryParse(s, format, out)` answers `false` for an unknown `CronFormat` instead of throwing; `new CronExpression(null)` and `CronScheduleBuilder.Create(null)` raise `ArgumentNullException`, not `ArgumentException` | [A bad argument, rather than a bad expression](#a-bad-argument-rather-than-a-bad-expression) |
| beta.1 | `IOperableTrigger.FireInstanceId`, `TriggerBase.FireInstanceId` and `DbMetadata.ParameterDbTypePropertyName` are `string?` (they were `string` initialised to `null!`). A custom store, trigger or driver description may need a `!` or a null check | [TriggerBase Property Removals](#triggerbase-property-removals), [Other Breaking Changes](#other-breaking-changes) |
| beta.1 | A `500` from the HTTP API no longer returns the exception's message | [A `500` says nothing about the server](#a-500-says-nothing-about-the-server) |
| beta.1 | A paged HTTP API request is bounded by `QuartzHttpApiOptions.MaxPageSize`, default `1000`; `take` used to reject only negatives. Set it to `0` for the old behavior | [A paged request is bounded by `MaxPageSize`](#a-paged-request-is-bounded-by-maxpagesize) |
| beta.1 | `MapQuartzHttpApi()` and `MapQuartzDashboard()` refuse to start when nothing authorizes them. **A bare API or dashboard needs one more call**; `AllowAnonymous()` is a supported answer | [The HTTP API and the dashboard will not serve anonymously by accident](#the-http-api-and-the-dashboard-will-not-serve-anonymously-by-accident) |
| beta.1 | `MapQuartzDashboard()` returns `IEndpointConventionBuilder`, not `RazorComponentsEndpointConventionBuilder`, so one `RequireAuthorization()` covers the pages *and* the hub | [One builder covers the dashboard's pages and its hub](#one-builder-covers-the-dashboard-s-pages-and-its-hub) |
| beta.1 | An ADO store refuses a `SimpleTrigger` repeat interval it cannot hold to the millisecond, instead of storing `0` and leaving the row stuck in `ACQUIRED` | [The two job stores answer the same way](#the-two-job-stores-answer-the-same-way) |
| beta.1 | `ILockHandler.Shutdown` added as a default interface member; existing handlers need not implement it | [A lock handler is told when to close what it opened](#a-lock-handler-is-told-when-to-close-what-it-opened) |
| beta.1 | `DelegatingScheduler` declares `ResetTriggersFromErrorState(GroupMatcher<TriggerKey>, …)` instead of running the interface default on the forwarder | [`DelegatingJobStore` decorates a store](#delegatingjobstore-decorates-a-store) |
| beta.1 | `TriggerFireTimes`'s three members gained `ITrigger` overloads, so the `IOperableTrigger` cast is not needed | [`TriggerUtils` became `TriggerFireTimes`](#triggerutils-became-triggerfiretimes) |
| beta.1 | Additive: `UseXmlSchedulingConfiguration` / `UseJsonSchedulingConfiguration` gained a `params string[]` overload; `QuartzHealthCheckOptions.StandbyStatus` and the nine `QuartzInstrumentation.Instruments` constants were added | [Plugins are registered like listeners](#plugins-are-registered-like-listeners), [Old and new telemetry names](#old-and-new-telemetry-names) |
| rc.1 | `Quartz.Dashboard.Services.JobDetailDto`'s `ConcurrentExecutionDisallowed` and `PersistJobDataAfterExecution`, and the same fields in every HTTP API job body, are `bool?`: absent means "whatever the type says". A request omitting them used to store a `[DisallowConcurrentExecution]` job as safe to run concurrently | [A job's two attribute flags are nullable on the wire](#a-job-s-two-attribute-flags-are-nullable-on-the-wire) |
| rc.1 | A job type name from the wire is never resolved during DTO conversion, on either side. A client can schedule a job whose type only the server has, and an unresolvable type reads as `200` with both flags absent, not a permanent `500` | [A job type name is never resolved by the contract types](#a-job-type-name-is-never-resolved-by-the-contract-types) |
| rc.1 | A job type named by a string is checked against `IJob` before construction. A type that is not one fails with a `JobInstantiationException` naming it, not an `InvalidCastException` after the constructor ran | [A named type is checked against `IJob` before it is constructed](#a-named-type-is-checked-against-ijob-before-it-is-constructed) |
| rc.1 | `Scheduling.IgnoreDuplicates` on its own turns `OverwriteExistingData` off, rather than being ignored (3.x) or refused (beta.1). Setting both is still refused | [`IgnoreDuplicates` on its own turns overwriting off](#ignoreduplicates-on-its-own-turns-overwriting-off) |
| rc.1 | `IScheduler`'s **read** members raise `ArgumentNullException` too; `CronExpression.ResolveHash` raises it instead of `ArgumentException` | [Null arguments raise `ArgumentNullException`](#null-arguments-raise-argumentnullexception) |
| rc.1 | `IScheduler.Interrupt(jobKey)` raises `JobInterrupted` once per firing it cancelled, not once in total. The new fire-instance overload is a default interface member, so existing listeners keep working | [`JobInterrupted` says which firing was interrupted](#jobinterrupted-says-which-firing-was-interrupted) |
| rc.1 | `SendMailJob` refuses to send when the registered credential answers for every host (a bare `NetworkCredential`) and the host comes from job data. Register a `CredentialCache` bound to the server. `SendMailOptions.EnableSsl` is new, off by default | [The SMTP password does not belong in job data](#the-smtp-password-does-not-belong-in-job-data) |
| rc.1 | `DirectoryScanJob` finds its listener among what the application registered, not by sweeping loaded assemblies. `AddSingleton<InboxListener>()` alone no longer resolves: register it as `IDirectoryScanListener`, key it, or put it in the `SchedulerContext` | [`DirectoryScanJob` finds its listener among what you registered](#directoryscanjob-finds-its-listener-among-what-you-registered) |
| rc.1 | `DirectoryScanJob` stores its previous scan as a `Dictionary<string, string>`, not a `List<FileInfo>` no serializer could write. Its first firing against a persistent store used to fail to persist | [`DirectoryScanJob` stores its file list as something a job store can write](#directoryscanjob-stores-its-file-list-as-something-a-job-store-can-write) |
| rc.1 | `NativeJob` redirects the child's streams only when `ConsumeStreams` is on, and waits with `WaitForExitAsync`. With the defaults, a process writing more than a pipe buffer used to block forever | [`NativeJob` no longer redirects a stream nobody reads](#nativejob-no-longer-redirects-a-stream-nobody-reads) |
| rc.1 | The health check reports `Degraded` for a **clustered** node whose last check-in is older than `QuartzHealthCheckOptions.ClusterCheckinTolerance` (`3`) times its own interval. `null` or `0` skips the query; an unclustered scheduler is not asked | [The health check reports the state it found](#the-health-check-reports-the-state-it-found) |
| rc.1 | A scheduling file, `AddQuartz` and the JSON plugin each log under their own category, not all as `Quartz.Xml.XmlSchedulingDataProcessor`. Event ids are unchanged | [Each scheduling path logs under its own category](#each-scheduling-path-logs-under-its-own-category) |
| rc.1 | The log catalogue moved: `7010` and `7011` are gone with the assembly sweep; `1020` (a shutdown abandoning running work), `1021` (something subscribed where 3.x published telemetry), `3156` (a schema that was already complete) and `9100`–`9103` (the dashboard's events) are new | [Every message carries an event id](#every-message-carries-an-event-id) |
| rc.1 | Schema validation probes the columns the 4.0 migration adds, not only the tables, and `CreateIfMissing` refuses a schema 4.x did not create. **A 4.x node against an unmigrated 3.x schema is refused at startup**, where `ProvisionSchema()` used to start it and fire nothing; both messages name `database/migrations/4.0/` | [Database Schema Migration](#database-schema-migration) |
| rc.1 | A recorded-paused **job** group binds what is added to it on the ADO store, as in memory: a trigger stored for a job in it is born `PAUSED`. A pre-release that paused a job group and then deployed a job into it ran that job | [The two job stores answer the same way](#the-two-job-stores-answer-the-same-way) |
| rc.1 | The 3.x-to-4.0 migration is **two files**: `schema_30_to_40_upgrade_<database>.sql` is mandatory and safe in a mixed window; the indexes moved to `schema_30_to_40_indexes_<database>.sql`, which waits for the last 3.x node. Section 6 of the old single file *is* the new file | [Database Schema Migration](#database-schema-migration) |
| rc.1 | The schema no longer creates `IDX_QRTZ_T_NFT_ST_MISFIRE`, and the migration drops it. **Run `schema_30_to_40_indexes_<database>.sql`** (section 6 of the old file) on a schema from an earlier pre-release; without it nothing fails, the index is just maintained for nothing | [The misfire index is dropped](#the-misfire-index-is-dropped-optional) |
| rc.2 | The persistent store no longer resolves a job's class on `RescheduleJob` or `UpdateTriggerDetails`, and reads both attribute flags from `IS_NONCONCURRENT` / `IS_UPDATE_DATA`. A process that cannot load its job classes needs no `ITypeLoader` or placeholder type | [A job type name is never resolved by the contract types](#a-job-type-name-is-never-resolved-by-the-contract-types) |
| rc.2 | Documentation only; the parser did not change. An expression from a crontab-derived .NET library parses on 4.0 where 3.x refused it, and two shapes (a numeric day-of-week, and both day fields restricted) fire on a different day | [If your expressions came from another cron library](#if-your-expressions-came-from-another-cron-library) |

:::

### Names a pre-release had and 4.0 does not

The lock-handler family was renamed twice: from the 3.x spellings, then again before 4.0.0. A
`quartz.jobStore.lockHandler.type` naming any generation resolves, with a warning.

| A 4.0 pre-release called it | 4.0 |
|---|---|
| `ISemaphore` | `ILockHandler` |
| `DbSemaphore` | `DbLockHandler` |
| `SelectForUpdateSemaphore` | `SelectForUpdateLockHandler` |
| `PostgreSqlSelectForUpdateSemaphore` | `PostgreSqlSelectForUpdateLockHandler` |
| `UpdateRowSemaphore` | `UpdateRowLockHandler` |
| `SqlServerMemoryOptimizedUpdateRowSemaphore` | `SqlServerMemoryOptimizedUpdateRowLockHandler` |
| `SimpleSemaphore` (internal) | `InProcessLockHandler` (internal) |
| `SQLiteSemaphore` (internal) | `SqliteLockHandler` (internal) |
| `RedisSemaphore` | `RedisLockHandler` |
| `SemaphoreContext` | `LockHandlerContext` |
| `ISemaphore.ObtainLock` | `ILockHandler.AcquireLock` |

* `AdoJobStoreBase` was a public base class in the alphas and is internal in 4.0. If you derived from it,
  [The ADO.NET store is a store, not a base class](#the-ado-net-store-is-a-store-not-a-base-class) has the
  four answers, the same as for 3.x's `JobStoreSupport`. `AdoJobStoreDependencies` is in
  [Types that were removed, internalized or renamed](#types-that-were-removed-internalized-or-renamed) for
  the same reason.
* `quartz.jobStore.openConnection` had no reader through `4.0.0-alpha.5`: it passed the unknown-key check
  under a supported prefix, and nothing read it — see
  [The ADO.NET job stores are named for whose transaction they use](#the-ado-net-job-stores-are-named-for-whose-transaction-they-use).
