---

title: 'Comparison'
---

# Comparison

Five .NET libraries schedule background work. Every cell below was read from the named project's own
documentation or source at a pinned version, and every cell that is not Quartz's links its source.
Rows where Quartz.NET is the more expensive answer say so;
[Where Quartz.NET costs more than it is worth](#where-quartz-net-costs-more-than-it-is-worth) collects
them.

Read as of **19 September 2026**. If a cell no longer matches its link, the link is right —
please [open an issue](https://github.com/quartznet/quartznet/issues).

- **"None"**: the project's documentation has no such concept, and its source at the pinned version has
  none. You can usually still write the code yourself.
- **"—"**: the row does not apply to that library.

| Library | Version read | Pinned at |
|---|---|---|
| Quartz.NET | 4.2 | this repository |
| Hangfire | 1.8.25 | [tag `v1.8.25`](https://github.com/HangfireIO/Hangfire/tree/v1.8.25) |
| TickerQ | 10.4.0 | [commit `c6ed1e7d`](https://github.com/Arcenox-co/TickerQ/tree/c6ed1e7daa90ab3f4c65b40319a153126a910093), named by the 10.4.0 packages' SourceLink; there is no `v10.4.0` tag |
| Wolverine | 6.35.0 | [tag `V6.35.0`](https://github.com/JasperFx/wolverine/tree/V6.35.0) |
| Coravel | 6.0.2 | [commit `88ea3e89`](https://github.com/jamesmh/coravel/tree/88ea3e892cfa3ce3d50054c7b430f16457b4d919), named by the 6.0.2 nuspec; the last tag is `4.0.3` |

## What each one is

- **Quartz.NET** is a scheduler. A trigger is a stored object with a schedule, time zone, misfire
  instruction, calendar and priority, and the scheduler fires it once, on time, on one node of however
  many are running.
- **Hangfire** is a background job processor first: a queue with a durable state machine (enqueued,
  processing, succeeded, failed) and a dashboard built around it. A recurring job is a cron string that
  enqueues into it.
- **TickerQ** is a source-generated dispatcher: an attribute on a method, a Roslyn generator for the
  registration, no reflection on the dispatch path. It has a one-shot ticker and a cron ticker.
- **Wolverine** is a message bus with cron since 6.34. `opts.Schedules.ScheduleRecurring` publishes a
  message on a schedule, using the bus's delivery, durability and replay. It is not a scheduler;
  [Quartz.NET with Wolverine](how-tos/wolverine.md) covers which of the two should own a schedule.
- **Coravel** is an in-process convenience layer for ASP.NET Core: a scheduler, a queue, a cache and an
  event dispatcher, all in memory, in one small package.

## Declaring a job

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| The unit of work | a class implementing `IJob` or `IJob<TInput>`; from 4.3, also [a lambda](tutorial/delegate-jobs.md) | [an expression tree naming a method](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html) | [a method with `[TickerFunction("name")]`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/Base/TickerFunctionAttribute.cs) | [a message type and its handler](https://wolverinefx.net/guide/messaging/recurring.html) | [a class implementing `IInvocable`](https://docs.coravel.net/Invocables/) |
| Scheduling it | `AddJob<T>` + `AddTrigger<T>`, or `ScheduleJob<TJob, TInput>(input, delay)`; from 4.3, `ScheduleJob(name, lambda, trigger)` | [`BackgroundJob.Enqueue`, `.Schedule`, `RecurringJob.AddOrUpdate(id, …, cron)`](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html) | [by function name, `new TimeTickerEntity { Function = "send-welcome" }`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/getting-started/quick-start.mdx), or by type after `MapTicker<T>()` | [`opts.Schedules.ScheduleRecurring<T>("0 2 * * *")`](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/Recurring/RecurringMessageCollection.cs) | [`scheduler.Schedule<T>().EveryTenMinutes()`](https://docs.coravel.net/Scheduler/) |
| Registration | `builder.AddQuartz()` + `builder.AddQuartzHostedService()` | [`services.AddHangfire(…)` + `services.AddHangfireServer()`](https://docs.hangfire.io/en/latest/getting-started/aspnet-core-applications.html) | [`builder.Services.AddTickerQ();` + `app.UseTickerQ();`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/getting-started/quick-start.mdx) | [inside `UseWolverine`](https://wolverinefx.net/guide/configuration.html) | [`services.AddScheduler()` + `app.Services.UseScheduler(…)`](https://docs.coravel.net/Scheduler/) |
| Compiler checks | job type; payload type for `IJob<TInput>`; a cron literal or `const` ([`QZ0001`](tutorial/compile-time-checks.md)); a `[JobTimeout]` argument | [the method call](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html); the recurring id is a string | [function name, signature and cron literal (`TQ003`)](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.SourceGenerator/Validation/DiagnosticDescriptors.cs) | the message type; a bad cron throws [at the registration line](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | the invocable type; [cron parsed at run time](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Cron/CronExpression.cs) |
| Where the schedule lives | the job store; changed at run time by any node | [the storage](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html); changed at run time | the persistence provider; changed at run time or from the dashboard | code, at `UseWolverine` | [code, at `UseScheduler`](https://docs.coravel.net/Scheduler/); not persisted |

Before 4.2 Quartz parsed cron only at run time, and TickerQ's generator won this row. Now an analyzer
inside `Quartz.nupkg` parses a cron literal or `const` with the run-time parser and fails the build on
an error ([Compile-Time Checks](tutorial/compile-time-checks.md)), and
[`[QuartzJob]` and `[CronTrigger]`](tutorial/declaring-jobs-with-attributes.md) declare a job and its
schedule on the class for a source generator to register. An expression built at run time is still
checked at run time, with [`CronExpressionBuilder` and the "when does this fire"
helper](cron-expressions.md#checking-an-expression).

## Trigger kinds and cron grammar

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Schedule kinds | five: [cron](tutorial/crontriggers.md), [simple](tutorial/simpletriggers.md), calendar-interval, daily-time-interval, [RFC 5545 recurrence](tutorial/recurrencetrigger.md) | [fire-and-forget, delayed, recurring, continuation](https://docs.hangfire.io/en/latest/background-methods/index.html); batches on the paid tier | [`TimeTicker` (one-shot) and `CronTicker` (recurring)](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/what-is-tickerq.mdx) | [scheduled, and recurring on cron](https://wolverinefx.net/guide/messaging/recurring.html) | [fluent intervals, or cron](https://docs.coravel.net/Scheduler/) |
| Cron parser | Quartz's own | [Cronos 0.11.1, internalized into `Hangfire.Core`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Hangfire.Core.csproj) | [NCrontab 3.3.0](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/TickerQ.Utilities.csproj) | [Cronos](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [hand-written](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Cron/CronExpression.cs) |
| Fields | [six, or seven with a year](cron-expressions.md); five with [`CronFormat.Unix`](cron-expressions.md#the-unix-five-field-form) | [five, or six with leading seconds](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/RecurringJobEntity.cs) | [six; five is expanded with `0` seconds](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/scheduling/cron-ticker.mdx) | [five, or six with leading seconds](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [five](https://docs.coravel.net/Scheduler/) |
| Beyond `* , - /` | `L`, `W`, `#`, [`H`](cron-expressions.md#h-hash-for-load-distribution), [wrapping ranges](cron-expressions.md#special-characters) | [`L`, `W`, `#` (Cronos)](https://github.com/HangfireIO/Cronos) | [none](https://github.com/atifaziz/NCrontab) | [the Cronos grammar](https://github.com/HangfireIO/Cronos) | [none](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Cron/CronExpression.cs) |
| Finest cadence | no floor; seconds mean seconds | [`SchedulePollingInterval`, 15 s by default](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJobServerOptions.cs); recurring jobs are [documented as minute-based](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html) | [`MinPollingInterval`, 1 s by default](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | [5 s; quicker is refused](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [1 s for a fluent interval; cron only on the minute](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Scheduler.cs) |
| Tie between two due at once | `Priority` on the trigger | [none (requested since 2022)](https://github.com/HangfireIO/Hangfire/issues/2109); SQL Server drains queues in [alphanumeric order](https://docs.hangfire.io/en/latest/background-processing/configuring-queues.html) | [`TickerTaskPriority` per function: dispatch order](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/api-reference/attributes.mdx) | [the receiving endpoint decides](https://wolverinefx.net/guide/messaging/listeners.html) | [none](https://docs.coravel.net/Scheduler/) |

## A firing the process was down for

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| The policy | [a misfire instruction per trigger](tutorial/more-about-triggers.md#misfire-instructions): skip, fire one catch-up, or fire every missed one | [`MisfireHandlingMode`: `Relaxed` (default, one job), `Strict` (one per missed occurrence), `Ignorable` (none)](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/MisfireHandlingMode.cs) | none: the fallback sweep runs an overdue row late; [`SkipStaleCronOccurrencesOnStartup()` drops stale occurrences, off by default](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/TickerOptionsBuilder.cs) | [no back-fill; the one pre-scheduled occurrence fires, the rest is lost](https://wolverinefx.net/guide/messaging/recurring.html) | nothing; [the tick catch-up is seeded at process start](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/HostedService/SchedulerHost.cs), so it covers a stalled timer, not a restart |
| Chosen per | trigger | recurring job | — | — | — |

## Calendars and time zones

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Dates it must not fire on | six calendars: `HolidayCalendar`, `CronCalendar`, `DailyCalendar`, `WeeklyCalendar`, `AnnualCalendar`, `MonthlyCalendar` | none | none | none | none |
| Time zone | per trigger, `InTimeZone` | [`RecurringJobOptions.TimeZone`, UTC by default](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/RecurringJobOptions.cs) | [one `SchedulerTimeZone`, machine-local by default](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | [per schedule, UTC by default](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [`.Zoned(TimeZoneInfo)` per schedule, UTC by default](https://docs.coravel.net/Scheduler/) |

Elsewhere, "not on public holidays" is a check in the job body.

## Running once when several nodes are up

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| How one node wins | [a row lock at trigger acquisition](tutorial/advanced-enterprise-features.md), before the job runs | [a distributed lock around the recurring enqueue](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html); the first server to dequeue runs it | [a conditional `UPDATE`'s affected-row count](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.EntityFrameworkCore/Infrastructure/BasePersistenceProvider.cs), with no row lock, `SKIP LOCKED` or lock table; Lua scripts on Redis | [one `SingularAgent` per cluster, plus a deterministic deduplication id](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/Recurring/RecurringMessageAgent.cs) | nothing: a process-local `Timer`, so every instance runs every schedule |
| A node dies mid-execution | check-in detects it; a job that [requests recovery](tutorial/advanced-enterprise-features.md#asking-for-recovery) re-runs | [removed after `ServerTimeout` (5 minutes); its jobs are requeued](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Server/ServerWatchdog.cs) | the fallback sweep takes rows with a stale lease; Redis has a dead-node script | the agent moves to another node | — |
| Pinning work to one node | `PreferredNode` on the trigger | [queue names per server](https://docs.hangfire.io/en/latest/background-processing/configuring-queues.html) | [`NodeIdentifier`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) names the lease holder; it does not route | the agent's assignment | — |

Quartz fires each trigger once, because acquisition takes the lock before the job runs. A firing is
**at most once** unless recovery is requested, which makes it at least once. No library here promises
exactly once; Hangfire's [best practices](https://docs.hangfire.io/en/latest/best-practices.html) ask for
re-entrant methods because it retries, and its
[throttling page](https://docs.hangfire.io/en/latest/background-processing/throttling.html) says a mutex
does not prevent simultaneous execution of one job. Write jobs so a second run is harmless —
[Best Practices](/documentation/best-practices#assume-the-job-will-run-more-than-once).

## Concurrency control

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Overall parallelism | `MaxConcurrency`, ten by default | [`WorkerCount`, `min(ProcessorCount × 5, 20)`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJobServerOptions.cs) | [`MaxConcurrency`, `Environment.ProcessorCount`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | [the receiving endpoint's](https://wolverinefx.net/guide/messaging/listeners.html) | [none documented](https://docs.coravel.net/Scheduler/) |
| One job not overlapping itself | `[DisallowConcurrentExecution]`: cluster-wide with a persistent store, enforced by the store, no waiting | [`DisableConcurrentExecution(timeoutSeconds)`: a distributed lock that **waits**, then throws `DistributedLockTimeoutException`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/DisableConcurrentExecutionAttribute.cs) | [`maxConcurrency` on `[TickerFunction]`, a per-process `SemaphoreSlim`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/Src/TickerFunctionConcurrencyGate.cs) | [as for any message](https://wolverinefx.net/guide/messaging/listeners.html) | [`PreventOverlapping`, an in-memory mutex](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Mutex/InMemoryMutex.cs) |
| Capping a category of work | [execution groups](tutorial/execution-groups.md), per node or **across every node sharing the store** | [`Hangfire.Throttling`, Business tier](https://www.hangfire.io/ace/); [best-effort, not for hundreds of jobs on one semaphore](https://docs.hangfire.io/en/latest/background-processing/throttling.html) | per function and per process only | — | — |

Only Quartz counts a limit across nodes for free: "this tenant gets eight threads, however many nodes are
up" is counted by the store, not per process.

## Retries and failure

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Retry on failure | [`RetryPolicy` on the trigger](how-tos/retrying-failed-jobs.md): `Fixed`, `Exponential`, `Explicit`; opt-in | [`AutomaticRetryAttribute` on every job by default: ten attempts, `(attempt − 1)⁴ + 15 + rand(30) × attempt` seconds apart](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/AutomaticRetryAttribute.cs) | [`Retries` and `RetryIntervals` (seconds) per ticker](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/error-handling.mdx) | the message's retry policies and dead-letter queue | [none: `OnError` and a `ScheduledEventFailed` broadcast](https://docs.coravel.net/Scheduler/) |
| What holds the wait | the job store: a new fire time on the trigger, which survives a restart and runs on any node | the storage: the job waits in `Scheduled` | [a `Task.Delay` in the execution, holding the worker slot and the lease; lost with the process](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/Src/TickerExecutionTaskHandler.cs) | the message store | — |
| Jitter | [opt-in on `Exponential`: each wait × `[1 − jitter, 1 + jitter]`](how-tos/retrying-failed-jobs.md#give-the-trigger-a-policy) | [always, `rand(30) × attempt` seconds](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/AutomaticRetryAttribute.cs) | none | the bus's policy | — |
| Attempts run out | back to the ordinary schedule, not an error state; reported by [`ITriggerListener.TriggerRetriesExhausted`, a log event, a counter and a final history row](how-tos/retrying-failed-jobs.md#when-the-policy-gives-up) | [`Failed`, never expires](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/States/FailedState.cs); requeue from the dashboard | recorded as failed | dead-letter queue | — |
| Per-job timeout | [`[JobTimeout]`, enforced once `AddJobTimeout` registers the middleware](tutorial/job-execution-middleware.md#timing-a-job-out) | none | [none](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | the message's own | none |

Hangfire retries by default; Quartz retries only triggers with a policy, and jitters only when the policy
says so. Quartz holds the wait in the store, so a node that dies during a backoff does not lose the retry.

## Continuations and chaining

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Run B after A | [`StartAfter(parentTriggerKey, condition)`](how-tos/job-continuations.md), or `ScheduleJob<TJob, TInput>(input, Continuation.After(…))` for a one-off | [`BackgroundJob.ContinueJobWith(parentId, …)`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJob.cs) | [parent/child `TimeTicker`s](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/job-chaining.mdx) | a handler publishes the next message | none |
| Conditional on the outcome | [`ContinuationCondition`](how-tos/job-continuations.md) flags: `OnSuccess` (default), `OnFailure`, `OnCancellation`, `OnVeto`, `OnAnyOutcome` | [`JobContinuationOptions`: `OnAnyFinishedState`, `OnlyOnSucceededState` (default), `OnlyOnDeletedState`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/JobContinuationOptions.cs) | [`RunCondition`: `OnSuccess`, `OnFailure`, `OnCancelled`, `OnFailureOrCancelled`, `OnAnyCompletedStatus`, `InProgress`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/Enums/RunCondition.cs) | the handler decides | — |
| Where the link is kept | the job store: a trigger in `Awaiting`, released or discarded in the parent's completion transaction on any node; shown in the dashboard | the storage; `Awaiting` in the dashboard | the persistence provider, on the child row | code | — |
| On a recurring schedule | yes, with any schedule; `JobChainingJobListener` links recurring jobs with the same conditions | [yes: recurring jobs enqueue ordinary jobs, which can be continued](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html) | [no, `TimeTicker` only](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/job-chaining.mdx) | yes | — |

Before 4.2 Quartz had only `JobChainingJobListener`: links were not persisted, the follow-up ran on the
parent's node, and it ran even when the parent threw. Now the link is in the store, so a crash between
the parent finishing and the release cannot lose it, and a continuation whose condition was not met is
discarded. See [Job Continuations](how-tos/job-continuations.md).

## Persistence

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Default | in-memory `RAMJobStore` | [none; a storage is required](https://docs.hangfire.io/en/latest/configuration/index.html) | [in-memory](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/getting-started/installation.mdx) | none for the schedule | [in-memory only](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Scheduler.cs) |
| Databases shipped by the project | [SQL Server, PostgreSQL, MySQL, Oracle, SQLite, Firebird](db/), through ADO.NET | [SQL Server and `Hangfire.InMemory`; Redis (`Hangfire.Pro.Redis`) is paid](https://www.hangfire.io/pricing/) | EF Core — [SQL Server, PostgreSQL, SQLite in the docs](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/what-is-tickerq.mdx), [MySQL in the readme](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/README.md) — plus Redis | [PostgreSQL, SQL Server, MySQL, SQLite](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Persistence/Wolverine.RDBMS/DatabaseConstants.cs), for the message store | — |
| Anything else | your own store, or your own [dialect](how-tos/dialect-delegate.md) | [community packages (PostgreSQL, MongoDB, MySQL, SQLite), officially unsupported](https://www.hangfire.io/extensions.html) | your own provider | — | — |
| Creating the schema | [`ProvisionSchema()`, or the DDL yourself](quick-start.md#creating-and-initializing-the-database) | [automatic unless `PrepareSchemaIfNecessary` is off](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html) | [your own EF Core migration](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/entity-framework/migrations.mdx) | [Wolverine provisions its tables](https://wolverinefx.net/guide/durability/) | — |

## The dashboard, and what it allows before you configure anything

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| In the box | [`Quartz.Dashboard`, free, thirteen pages](packages/dashboard.md) | [in `Hangfire.Core`, free](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) | [`TickerQ.Dashboard`, free](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/index.mdx) | none; [CritterWatch is paid](https://jasperfx.net/our-products/) | none; [Coravel Pro is separate](https://www.pro.coravel.net/) |
| Who may reach it by default | **nobody**: a mapping with neither `RequireAuthorization` nor `AllowAnonymous`, under a host with no fallback policy, [fails at start-up](packages/dashboard.md#production-hardening) | [local requests only](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html); otherwise implement `IDashboardAuthorizationFilter` | [everyone: `AuthMode.None` is the default](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/authentication.mdx) | — | — |
| Read-only mode | yes, plus a [job-type allow-list](packages/dashboard.md#narrowing-which-job-types-may-be-named) for scheduling through it | [`IsReadOnlyFunc`, off by default](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) | no | — | — |
| Best at | the cluster: node check-ins, execution groups, misfires, an action log; [pointed at a database](packages/dashboard.md#store-attached-targets), every scheduler in it without running them | [the state machine: a page per state, one-click requeue or delete of failed jobs, singly or in bulk](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) | [live SignalR monitoring, editing both ticker kinds, starting and stopping the host](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/index.mdx) | — | — |

Hangfire's per-state lists are better for browsing failures by kind. Quartz's History page has a
**Failed after retries** filter and a **Run again** button on each occurrence that gave up — see
[When the policy gives up](how-tos/retrying-failed-jobs.md#when-the-policy-gives-up).

## Observability

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Traces | [two job spans, thirty-three store spans, `Quartz` activity source](packages/opentelemetry-integration.md) | [none in the box](https://github.com/HangfireIO/Hangfire/tree/v1.8.25/src/Hangfire.Core); the [OpenTelemetry community package](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Hangfire) is pre-release | [`TickerQ.Instrumentation.OpenTelemetry`: `tickerq.job.execute.*` spans](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/opentelemetry/index.mdx) | the bus's message spans, tagged [`wolverine.schedule.name`](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/WolverineTracing.cs) | none |
| Metrics | [eleven instruments on the `Quartz` meter](packages/opentelemetry-integration.md#metrics) | none | [none; traces and `ILogger` events only](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/opentelemetry/index.mdx) | [yes, Wolverine's meter](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/WolverineRuntime.cs) | none |
| Health check | [in the core package](packages/hosted-services-integration.md#health-checks) | none | none | [`WolverineFx.HealthChecks`, separate package](https://www.nuget.org/packages/WolverineFx.HealthChecks) | none |
| .NET Aspire | [`Quartz.Aspire`](packages/aspire.md): a connection name becomes a store with telemetry and health check | none | none | — | none |

## Trimming and native AOT

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Declared | [`IsAotCompatible`, no `IL3050` anywhere](how-tos/trimming-and-native-aot.md) | [no](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Hangfire.Core.csproj) | [`IsAotCompatible` on four of six libraries; not the EF Core provider or OpenTelemetry package](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/TickerQ.csproj) | [`IsAotCompatible`](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Wolverine.csproj) | [no](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Coravel.csproj) |
| Checked | a native canary is published and **run** on Windows, Linux and macOS on every pull request | [not supported yet](https://github.com/HangfireIO/Hangfire/issues/2478) | [an AOT sample publishes natively](https://github.com/Arcenox-co/TickerQ/tree/c6ed1e7daa90ab3f4c65b40319a153126a910093/samples) | [trim and AOT analyzers, both target frameworks](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Wolverine.csproj) | — |
| What you must still do | name or root job types so the trimmer sees them; [the page lists the paths that still warn](how-tos/trimming-and-native-aot.md) | — | [pass a `JsonSerializerContext` via `WithJsonContext` for payloads](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | — | — |

## Target frameworks

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Targets | `net10.0`; the maintained [3.x line](/documentation/quartz-3.x/quick-start) covers .NET Standard 2.0 and .NET Framework | [`net451`, `net46`, `netstandard1.3`, `netstandard2.0`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Hangfire.Core.csproj) | [`net10.0`; 8.x and 9.x lines target `net8.0` and `net9.0`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/Directory.Build.props) | [`net9.0` and `net10.0`](https://www.nuget.org/packages/WolverineFx/6.35.0) | [`net6.0`](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Coravel.csproj) |
| Serializing what is stored | System.Text.Json; Newtonsoft.Json as a [second serializer](packages/json-serialization.md) | [Newtonsoft.Json only](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Common/SerializationHelper.cs) | [System.Text.Json; a `JsonSerializerContext` when trimmed](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | — | — |

## Licence and price

| | Quartz.NET 4.2 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Licence | Apache-2.0 | [LGPL v3, or commercial](https://www.hangfire.io/pricing/) | [`MIT OR Apache-2.0`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/LICENSE), with [a contributor CLA](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/CLA.md) | [MIT](https://github.com/JasperFx/wolverine/blob/V6.35.0/LICENSE) | [MIT](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/LICENSE) |
| Free tier | everything | [Open: SQL Server and in-memory storage, community support](https://www.hangfire.io/pricing/) | everything | [everything in Wolverine](https://github.com/JasperFx/wolverine/blob/V6.35.0/LICENSE) | [the library](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/LICENSE) |
| Paid tiers | none | [Startup $500, Business $1,500, Enterprise $4,500 per organization per year; batches and Redis are Pro, throttling is Ace](https://www.hangfire.io/pricing/) | none; [a commercial model proposed on 3 August 2026](https://github.com/Arcenox-co/TickerQ/discussions/886) keeps existing MIT and Apache releases free | [none for Wolverine; CritterWatch and AI Skills are paid](https://jasperfx.net/our-products/) | [Coravel Pro: free for personal use, $299 a year commercial](https://www.pro.coravel.net/) |

## Where Quartz.NET costs more than it is worth

- **The first five minutes are longer.** TickerQ is two registration lines and an attribute; Coravel is
  two lines and a fluent chain. Quartz's [shortest form](quick-start.md#the-shortest-thing-that-works)
  is comparable, and from 4.3 a [delegate job](tutorial/delegate-jobs.md) is one call with the job as a
  lambda. A recurring schedule on a database still adds a builder chain, a connection string, a driver
  package and a schema.
- **A schedule costs a row.** With a persistent store, adding a schedule is an `INSERT` and a round trip,
  and each firing costs a few more. TickerQ's in-memory default and Coravel are a dictionary insert. For
  thousands of short-lived one-off firings, use
  [one durable job per job type with a trigger per firing](how-tos/one-off-job.md).
- **Retry and jitter are opt-in.** Hangfire retries every job by default and spreads the attempts; Quartz
  retries only triggers with a policy, at exactly the policy's waits unless it names a `jitter`.
- **There are no queues.** Hangfire's `[Queue]`, with servers subscribing to different sets, routes work.
  [Execution groups](tutorial/execution-groups.md) bound how much of a category runs at once but do not
  choose the node, and `PreferredNode` pins rather than balances.
- **A message may belong to a bus.** An application already running Wolverine with an outbox, wanting
  "publish X every weekday at 03:00", should use Wolverine's schedule rather than a second runtime and
  set of tables — [Quartz.NET with Wolverine](how-tos/wolverine.md) covers both directions.
- **.NET Framework is 3.x territory.** Quartz 4.x is `net10.0` only; Hangfire still ships `net451`, and is
  the only one of the five whose current line a .NET Framework application can take.

Choose:

- **Quartz.NET** for stored schedules with calendars, time zones, misfire policies and priorities; a
  cluster that fires each trigger on one node; limits counted across nodes; retries held in the store.
- **Hangfire** for a queue with a state-machine dashboard, retry on by default, queue-based routing, or
  .NET Framework on a current line.
- **TickerQ** or **Coravel** for the shortest setup, or many short-lived in-memory firings.
- **Wolverine's own schedule** when the occurrence is a message in an application already on Wolverine.

## Coming from one of these

- [Coming from Hangfire](how-tos/coming-from-hangfire.md) — the API mapping, and the semantics that differ
- [Coming from TickerQ](how-tos/coming-from-tickerq.md) — the same, shorter
- [Quartz.NET with Wolverine](how-tos/wolverine.md) — running both in one host, and deciding which owns a schedule
