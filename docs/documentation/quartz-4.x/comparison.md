---

title: 'Comparison'
---

# Comparison

This page is an inventory, not a verdict. Five .NET libraries schedule background work, they disagree
about almost everything below the word "cron", and the differences that matter are rarely the ones a
feature list puts first. So every cell here was read out of the named project's own documentation or
source at a pinned version, and every cell that is not Quartz's links what it was read from. Where
Quartz.NET is the more expensive answer, the row says so, and
[Where Quartz.NET costs more than it is worth](#where-quartz-net-costs-more-than-it-is-worth) collects
those admissions in one place.

Read as of **19 September 2026**. A comparison rots; if a cell below no longer matches what the link
says, the link is right and this page is wrong — please
[open an issue](https://github.com/quartznet/quartznet/issues).

Two conventions. **"None" means the project's own documentation has no such concept and a search of its
source at the pinned version finds none** — it is not a claim that the effect cannot be had by writing
the code yourself, which it usually can. **"—" means the row does not apply**, because the library has
nothing of that kind for the question to be asked of.

| Library | Version read | Pinned at |
|---|---|---|
| Quartz.NET | 4.1 | this repository |
| Hangfire | 1.8.25 | [tag `v1.8.25`](https://github.com/HangfireIO/Hangfire/tree/v1.8.25) |
| TickerQ | 10.4.0 | [commit `c6ed1e7d`](https://github.com/Arcenox-co/TickerQ/tree/c6ed1e7daa90ab3f4c65b40319a153126a910093) — there is no `v10.4.0` tag, and this is the commit the 10.4.0 packages' SourceLink metadata names |
| Wolverine | 6.35.0 | [tag `V6.35.0`](https://github.com/JasperFx/wolverine/tree/V6.35.0) |
| Coravel | 6.0.2 | [commit `88ea3e89`](https://github.com/jamesmh/coravel/tree/88ea3e892cfa3ce3d50054c7b430f16457b4d919) — the repository has no tag past `4.0.3`, and this is the commit the 6.0.2 nuspec names |

## What each one is

They are not five of the same thing, and half of the differences below follow from that.

**Quartz.NET** is a scheduler: a trigger is a stored object with a schedule, a time zone, a misfire
instruction, a calendar and a priority, and the scheduler's job is to fire it once, on time, on one node
of however many are running.

**Hangfire** is a background job processor first and a scheduler second. Its centre is a queue with a
durable state machine — enqueued, processing, succeeded, failed — and a dashboard built around that
state machine; recurring work is a cron string that enqueues into it.

**TickerQ** is a source-generated dispatcher. A method carries an attribute, a Roslyn generator writes
the registration, and the dispatch path uses no reflection. Its two scheduling kinds are a one-shot
ticker and a cron ticker.

**Wolverine** is a message bus that grew a cron in 6.34. `opts.Schedules.ScheduleRecurring` publishes a
message on a schedule, over the delivery, durability and replay the bus already had. It is deliberately
not a scheduler, and [Quartz.NET with Wolverine](how-tos/wolverine.md) is about which of the two should
own a given schedule.

**Coravel** is an in-process convenience layer for ASP.NET Core: a scheduler, a queue, a cache and an
event dispatcher, all in memory, all in one small package.

## Declaring a job

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| The unit of work | a class implementing `IJob` or `IJob<TInput>` | [an expression tree naming a method](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html) — no interface to implement | [a method carrying `[TickerFunction("name")]`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/Base/TickerFunctionAttribute.cs) | [a message type and its handler](https://wolverinefx.net/guide/messaging/recurring.html) | [a class implementing `IInvocable`](https://docs.coravel.net/Invocables/) |
| Scheduling it | `AddJob<T>` + `AddTrigger<T>`, or `ScheduleJob<TJob, TInput>(input, delay)` | [`BackgroundJob.Enqueue`, `.Schedule`, `RecurringJob.AddOrUpdate(id, …, cron)`](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html) | [by function name — `new TimeTickerEntity { Function = "send-welcome" }`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/getting-started/quick-start.mdx) — or by type after `MapTicker<T>()` | [`opts.Schedules.ScheduleRecurring<T>("0 2 * * *")`](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/Recurring/RecurringMessageCollection.cs) | [`scheduler.Schedule<T>().EveryTenMinutes()`](https://docs.coravel.net/Scheduler/) |
| Registration in full | `builder.AddQuartz()` + `builder.AddQuartzHostedService()` | [`services.AddHangfire(…)` + `services.AddHangfireServer()`](https://docs.hangfire.io/en/latest/getting-started/aspnet-core-applications.html) | [`builder.Services.AddTickerQ();` + `app.UseTickerQ();`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/getting-started/quick-start.mdx) | [inside `UseWolverine`](https://wolverinefx.net/guide/configuration.html) | [`services.AddScheduler()` + `app.Services.UseScheduler(…)`](https://docs.coravel.net/Scheduler/) |
| What the compiler checks | the job type and, for `IJob<TInput>`, its payload type | [the method call, because it is an expression tree](https://docs.hangfire.io/en/latest/background-methods/calling-methods-in-background.html); the recurring id is a string | [the function name, its signature and its cron literal — `TQ003` is a build error for a cron that will not parse](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.SourceGenerator/Validation/DiagnosticDescriptors.cs) | the message type; a bad cron throws [at the registration line](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) rather than at start-up | the invocable type; [a cron string is parsed at run time](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Cron/CronExpression.cs) |
| Where the schedule lives | in the job store — added, rescheduled and deleted while the host is up, by any node | [in the storage](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html), changed at run time | in the persistence provider, changed at run time or from the dashboard | in code, at `UseWolverine`; the set is whatever the process was compiled with | [in code, at `UseScheduler`](https://docs.coravel.net/Scheduler/); nothing is persisted |

Through 4.1 Quartz read its cron at run time alone, so an expression that could not parse was an
exception rather than a build error and TickerQ's generator won this outright. **4.2 closes it**: an
analyzer inside `Quartz.nupkg` reads a cron literal or `const` with the very parser that reads it at
run time and fails the build on one that does not parse
([Compile-Time Checks](tutorial/compile-time-checks.md)), and
[`[QuartzJob]` and `[CronTrigger]`](tutorial/declaring-jobs-with-attributes.md) declare a job and its
schedule on the class for a source generator to register.

## Trigger kinds and cron grammar

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Schedule kinds | five: [cron](tutorial/crontriggers.md), [simple](tutorial/simpletriggers.md), calendar-interval, daily-time-interval and [RFC 5545 recurrence](tutorial/recurrencetrigger.md) | [fire-and-forget, delayed, recurring, continuation](https://docs.hangfire.io/en/latest/background-methods/index.html), and batches on the paid tier | [two: `TimeTicker` (one-shot) and `CronTicker` (recurring)](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/what-is-tickerq.mdx) | [a scheduled message, and a recurring one on cron](https://wolverinefx.net/guide/messaging/recurring.html) | [fluent intervals, or a cron string](https://docs.coravel.net/Scheduler/) |
| Cron parser | Quartz's own | [Cronos 0.11.1, ILRepack-internalized into `Hangfire.Core`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Hangfire.Core.csproj) | [NCrontab 3.3.0](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/TickerQ.Utilities.csproj) | [Cronos](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [hand-written](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Cron/CronExpression.cs) |
| Fields | [six, or seven with a trailing year](cron-expressions.md), plus a five-field [`CronFormat.Unix`](cron-expressions.md#the-unix-five-field-form) mode | [five, or six with a leading seconds field](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/RecurringJobEntity.cs) | [six with seconds; a five-field expression is expanded with `0` seconds](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/scheduling/cron-ticker.mdx) | [five, or six with a leading seconds field](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [five](https://docs.coravel.net/Scheduler/) |
| Beyond `* , - /` | `L`, `W`, `#`, [`H` hashing](cron-expressions.md#h-hash-for-load-distribution), [wrapping ranges](cron-expressions.md#special-characters) | [`L`, `W`, `#`, and nothing beyond what Cronos parses](https://github.com/HangfireIO/Cronos) | [none — NCrontab's grammar is `* , - /`, digits and names](https://github.com/atifaziz/NCrontab) | [the same Cronos grammar](https://github.com/HangfireIO/Cronos) | [none](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Cron/CronExpression.cs) |
| The finest cadence it will honour | no floor; the seconds field means what it says | [the server's `SchedulePollingInterval`, 15 seconds by default](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJobServerOptions.cs) — a six-field expression parses, but [the documentation describes recurring jobs as minute-based](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html) | [`MinPollingInterval`, one second by default](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | [five seconds; quicker is refused where it is written](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [one second for a fluent interval; a cron schedule is only evaluated on the minute mark](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Scheduler.cs) |
| Breaking a tie between two due at once | `Priority` on the trigger | [none — an open request since 2022](https://github.com/HangfireIO/Hangfire/issues/2109); on SQL Server queues are drained in [alphanumeric order](https://docs.hangfire.io/en/latest/background-processing/configuring-queues.html) | [`TickerTaskPriority` per *function*, deciding dispatch order rather than which occurrence wins](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/api-reference/attributes.mdx) | an occurrence is a message, so [the receiving endpoint decides](https://wolverinefx.net/guide/messaging/listeners.html) | [none](https://docs.coravel.net/Scheduler/) |

## A firing the process was down for

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| The policy | [a misfire instruction per trigger](tutorial/more-about-triggers.md#misfire-instructions): skip it, fire one catch-up, or fire every one that was missed | [`MisfireHandlingMode` per recurring job — `Relaxed` (the default: one job however many were missed), `Strict` (one per missed occurrence), `Ignorable` (none)](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/MisfireHandlingMode.cs) | none. An overdue row is picked up by the fallback sweep and run late; [`SkipStaleCronOccurrencesOnStartup()` opts into dropping stale occurrences and is off unless called](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/TickerOptionsBuilder.cs) | [no back-fill; the one occurrence already pre-scheduled still fires, and the rest of the window is lost](https://wolverinefx.net/guide/messaging/recurring.html) | nothing. [The tick catch-up the documentation describes is seeded at process start](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/HostedService/SchedulerHost.cs), so it covers a stalled timer and not a restart |
| Where the choice is made | on the trigger, so two schedules in one application can differ | per recurring job | — | — | — |

## Calendars and time zones

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Dates it must not fire on | six calendars on the trigger — `HolidayCalendar`, `CronCalendar`, `DailyCalendar`, `WeeklyCalendar`, `AnnualCalendar`, `MonthlyCalendar` | none | none | none | none |
| Time zone | per trigger, with `InTimeZone` | [`RecurringJobOptions.TimeZone`, UTC unless set](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/RecurringJobOptions.cs) | [one `SchedulerTimeZone` for the whole scheduler, the machine's local zone unless set](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | [per schedule, UTC unless supplied](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/CronSchedule.cs) | [`.Zoned(TimeZoneInfo)` per schedule, UTC by default](https://docs.coravel.net/Scheduler/) |

For everyone except Quartz, "not on public holidays" is a condition the job body checks. A calendar is
the one axis on this page where nobody else competes.

## Running once when several nodes are up

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| How one node wins | [trigger acquisition takes a row lock](tutorial/advanced-enterprise-features.md) before the job runs | [a distributed lock around the recurring enqueue](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html); the enqueued job is then taken off the queue by whichever server gets there first | [a conditional `UPDATE` whose affected-row count decides — no row lock, no `SKIP LOCKED`, no lock table](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.EntityFrameworkCore/Infrastructure/BasePersistenceProvider.cs); the Redis provider uses Lua scripts instead | [one `SingularAgent` per cluster, reassigned on failover, plus a deterministic deduplication id](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/Recurring/RecurringMessageAgent.cs) | nothing — the scheduler is a process-local `Timer`, so every instance runs every schedule |
| A node that dies mid-execution | check-in detects it, and a job that [requests recovery](tutorial/advanced-enterprise-features.md#asking-for-recovery) is re-run | [a server that stops heartbeating is removed after `ServerTimeout` (5 minutes) and its jobs are requeued](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Server/ServerWatchdog.cs) | the fallback sweep picks up rows whose lease has gone stale; the Redis provider has a dead-node recovery script | the agent moves to another node | — |
| Pinning work to one node | `PreferredNode` on the trigger | [queue names, with each server subscribing to a different set](https://docs.hangfire.io/en/latest/background-processing/configuring-queues.html) | [`NodeIdentifier`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) identifies the lease holder; it does not route work | the agent's own assignment | — |

The guarantee is worth stating exactly rather than in marketing terms. Quartz fires each trigger once,
because acquisition is the lock and it happens before the job runs; a firing is then **at most once**
unless recovery is asked for, which buys at least once instead. Nobody here promises exactly once, and
Hangfire's own documentation says as much in two places — its
[best practices](https://docs.hangfire.io/en/latest/best-practices.html) page asks for re-entrant
methods because an interruption "can be caused by many different things (i.e. exceptions, server
shut-down), and Hangfire will attempt to retry processing many times", and its
[throttling page](https://docs.hangfire.io/en/latest/background-processing/throttling.html) warns that
a mutex "doesn't prevent simultaneous execution of the same background job".
Write the job so a second run is uneventful whichever library you pick —
[Best Practices](/documentation/best-practices#assume-the-job-will-run-more-than-once) has the shapes.

## Concurrency control

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Overall parallelism | `MaxConcurrency` on the thread pool, ten by default | [`WorkerCount`, `min(ProcessorCount × 5, 20)`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJobServerOptions.cs) | [`MaxConcurrency`, `Environment.ProcessorCount` by default](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | [the receiving endpoint's own concurrency](https://wolverinefx.net/guide/messaging/listeners.html) | [none documented](https://docs.coravel.net/Scheduler/) |
| One job not overlapping itself | `[DisallowConcurrentExecution]` — cluster-wide with a persistent store, and enforced by the store rather than by a wait | [`DisableConcurrentExecution(timeoutSeconds)`, a distributed lock that **waits** and then throws `DistributedLockTimeoutException`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/DisableConcurrentExecutionAttribute.cs) | [`maxConcurrency` on `[TickerFunction]`, a `SemaphoreSlim` in this process](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/Src/TickerFunctionConcurrencyGate.cs) | [an occurrence is a message like any other](https://wolverinefx.net/guide/messaging/listeners.html) | [`PreventOverlapping`, an in-memory mutex](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Mutex/InMemoryMutex.cs) |
| Capping a whole category of work | [execution groups](tutorial/execution-groups.md), with each limit counted per node or **across every node sharing the store** | [`Hangfire.Throttling` on the Business tier](https://www.hangfire.io/ace/) — mutexes, semaphores and rate limiters, documented as ["best-effort"](https://docs.hangfire.io/en/latest/background-processing/throttling.html) and as not suitable "for workloads where several hundreds of background jobs compete for the same semaphore" | per function and per process only; nothing counts across nodes | — | — |

Quartz's cluster-scoped execution limit is the one thing in this table that nothing else has for free:
"this tenant gets eight threads, however many nodes are up" is a limit the store counts, not a number
each process keeps its own copy of.

## Retries and failure

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Retry on failure | [`RetryPolicy` on the trigger](how-tos/retrying-failed-jobs.md) — `Fixed`, `Exponential` or `Explicit`, opt-in per trigger | [`AutomaticRetryAttribute`, applied to every job by default: ten attempts, delay `(attempt − 1)⁴ + 15 + rand(30) × attempt` seconds](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/AutomaticRetryAttribute.cs) | [`Retries` and `RetryIntervals` in seconds, per ticker](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/error-handling.mdx) | the message's own retry policies and dead-letter queue | [none — `OnError` and a `ScheduledEventFailed` broadcast, with no re-invocation](https://docs.coravel.net/Scheduler/) |
| What holds the wait | the job store: a retry is a new fire time on the trigger, so it survives a restart and any node can run it | the storage: the job sits in the `Scheduled` state until its next attempt | [an in-process `Task.Delay` inside the execution, so the worker slot and the row's lease are held for the whole sequence and it dies with the process](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/Src/TickerExecutionTaskHandler.cs) | the message store | — |
| Jitter | [opt-in on `Exponential`: each wait is drawn from `[1 − jitter, 1 + jitter]` times what the backoff says](how-tos/retrying-failed-jobs.md#give-the-trigger-a-policy) | [yes, `rand(30) × attempt` seconds](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/AutomaticRetryAttribute.cs) | none | the bus's own policy | — |
| When the attempts run out | the trigger returns to its ordinary schedule; it is not parked in an error state. [`ITriggerListener.TriggerRetriesExhausted`, a log event, a counter and a history row marked final](how-tos/retrying-failed-jobs.md#when-the-policy-gives-up) all say it happened | [the job lands in `Failed`, which never expires](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/States/FailedState.cs) and can be requeued from the dashboard | the ticker is recorded as failed | dead-letter queue | — |
| Per-job timeout | [`[JobTimeout]` on the job class, enforced once `AddJobTimeout` registers the middleware](tutorial/job-execution-middleware.md#timing-a-job-out) | none | [none](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | the message's own | none |

Hangfire's retry is on by default and Quartz's is opt-in, which is a real difference in what a careless
application gets. Hangfire jitters unconditionally where Quartz's jitter is a number on the policy, so a
schedule that wants its waits exact keeps them. What Quartz has that the others do not is that the wait
is held in the store rather than in the process: a node that dies during a five-minute backoff does not
take the retry with it.

## Continuations and chaining

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Run B after A | [`JobChainingJobListener`](/documentation/faq#how-do-i-chain-job-execution-or-how-do-i-create-a-workflow) | [`BackgroundJob.ContinueJobWith(parentId, …)`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJob.cs) | [parent/child `TimeTicker`s](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/job-chaining.mdx) | a handler that publishes the next message | none |
| Conditional on the outcome | **no** — the listener fires the follow-up on any completion, [including one that threw](https://github.com/quartznet/quartznet/blob/main/src/Quartz/Listeners/JobChainingJobListener.cs) | [`JobContinuationOptions`: `OnAnyFinishedState`, `OnlyOnSucceededState` (the default), `OnlyOnDeletedState`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/JobContinuationOptions.cs) | [`RunCondition`: `OnSuccess`, `OnFailure`, `OnCancelled`, `OnFailureOrCancelled`, `OnAnyCompletedStatus`, `InProgress`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ.Utilities/Enums/RunCondition.cs) | whatever the handler decides | — |
| Where the link is kept | in memory with the listener, re-registered on every start; the follow-up is *fired* rather than scheduled, so there is no trigger to see | in the storage, with an `Awaiting` state in the dashboard | in the persistence provider, on the child row | in code | — |
| On a recurring schedule | yes — the listener is about job keys | [recurring jobs enqueue ordinary jobs, which can be continued](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html) | [no — chaining is `TimeTicker` only](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/job-chaining.mdx) | yes | — |

The table is the 4.1 state, and this was Quartz's weakest row in it: `JobChainingJobListener` calls
itself "a poor man's workflow" in its own documentation, and it is — the links are not persisted, the
follow-up runs on whichever node ran the parent, and a parent that threw still triggers it. **4.2
answers it.** A trigger carrying `StartAfter(parentTriggerKey, condition)` waits in the job store, is
settled by the parent's completion inside the parent's own transaction, and is released or discarded by
the outcome it named; the listener remains as the *recurring* form and takes the same conditions. See
[Job Continuations](how-tos/job-continuations.md).

## Persistence

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Default | in-memory `RAMJobStore` | [none — a storage must be configured](https://docs.hangfire.io/en/latest/configuration/index.html) | [in-memory](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/getting-started/installation.mdx) | none for the schedule | [in-memory, and there is no other option](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Scheduling/Schedule/Scheduler.cs) |
| Databases the project itself ships | [SQL Server, PostgreSQL, MySQL, Oracle, SQLite and Firebird](db/), through ADO.NET | [SQL Server, plus `Hangfire.InMemory`; Redis is `Hangfire.Pro.Redis`, on the paid tier](https://www.hangfire.io/pricing/) | EF Core — [the documentation names SQL Server, PostgreSQL and SQLite](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/what-is-tickerq.mdx) while [the readme adds MySQL](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/README.md) — plus Redis | [PostgreSQL, SQL Server, MySQL and SQLite](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Persistence/Wolverine.RDBMS/DatabaseConstants.cs), for the message store | — |
| Anything else | a store of your own, or a [dialect of your own](how-tos/dialect-delegate.md) | [community packages for PostgreSQL, MongoDB, MySQL and SQLite, under an explicit "not responsible … does not provide official support" disclaimer](https://www.hangfire.io/extensions.html) | a provider of your own | — | — |
| Creating the schema | [`ProvisionSchema()`, or run the DDL yourself](quick-start.md#creating-and-initializing-the-database) | [installed automatically from the storage's constructor unless `PrepareSchemaIfNecessary` says otherwise](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html) | [an EF Core migration of your own](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/entity-framework/migrations.mdx) | [Wolverine provisions its own tables](https://wolverinefx.net/guide/durability/) | — |

## The dashboard, and what it allows before you configure anything

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| In the box | [`Quartz.Dashboard`, free, thirteen pages](packages/dashboard.md) | [in `Hangfire.Core`, free](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) | [`TickerQ.Dashboard`, free](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/index.mdx) | none; [CritterWatch is the paid console](https://jasperfx.net/our-products/) | none; [Coravel Pro is a separate product](https://www.pro.coravel.net/) |
| Who may reach it by default | **nobody, and the application will not start until you say** — a mapping that carries neither `RequireAuthorization` nor `AllowAnonymous`, under a host with no fallback policy, [fails at start-up](packages/dashboard.md#production-hardening) | [local requests only](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html); anything else means implementing `IDashboardAuthorizationFilter` | [everyone. `AuthMode.None` is the default, and the documentation says so: "By default the dashboard has no authentication — it's publicly accessible."](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/authentication.mdx) | — | — |
| Read-only mode | yes, and a [job-type allow-list](packages/dashboard.md#narrowing-which-job-types-may-be-named) for what may be scheduled through it | [`IsReadOnlyFunc`, off by default](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) | no | — | — |
| What it is best at | the whole cluster: node check-ins, execution groups and their headroom, misfires, an action log of what was done from it | [the state machine — one page per state, and one-click requeue or delete of a failed job, singly or in bulk](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html) | [live SignalR monitoring, editing both ticker kinds, starting and stopping the host](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/index.mdx) | — | — |

Hangfire's per-state lists are still better at browsing failures by kind. The
"something failed last night, show me and run it again" workflow itself is answered: the History page
has a **Failed after retries** filter and a **Run again** button on every occurrence that gave up — see
[When the policy gives up](how-tos/retrying-failed-jobs.md#when-the-policy-gives-up).

## Observability

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Traces | [two job spans and thirty-three store spans on the `Quartz` activity source](packages/opentelemetry-integration.md) | [none in the box](https://github.com/HangfireIO/Hangfire/tree/v1.8.25/src/Hangfire.Core); the [OpenTelemetry community's instrumentation package](https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Hangfire) is still pre-release | [`TickerQ.Instrumentation.OpenTelemetry` — `tickerq.job.execute.*` spans on a `TickerQ` source](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/opentelemetry/index.mdx) | the bus's own message spans, tagged [`wolverine.schedule.name`](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/WolverineTracing.cs) | none |
| Metrics | [eleven instruments on the `Quartz` meter](packages/opentelemetry-integration.md#metrics) | none | [none — the package emits traces and `ILogger` events only](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/opentelemetry/index.mdx) | [yes, on Wolverine's own meter](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Runtime/WolverineRuntime.cs) | none |
| Health check | [in the core package](packages/hosted-services-integration.md#health-checks) | none | none | [`WolverineFx.HealthChecks`, a separate package](https://www.nuget.org/packages/WolverineFx.HealthChecks) | none |
| .NET Aspire | [`Quartz.Aspire`](packages/aspire.md) — a connection name becomes a persistent store with its telemetry and health check | none | none | — | none |

## Trimming and native AOT

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Declared | [`IsAotCompatible`, with no `IL3050` anywhere in the package](how-tos/trimming-and-native-aot.md) | [no](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Hangfire.Core.csproj) | [`IsAotCompatible` on four of the six shipped libraries — not the EF Core provider and not the OpenTelemetry package](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/TickerQ.csproj) | [`IsAotCompatible`](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Wolverine.csproj) | [no](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Coravel.csproj) |
| Checked | a canary application is published as a native executable and **run**, on Windows, Linux and macOS, on every pull request | [the maintainer's answer is "not supported yet"](https://github.com/HangfireIO/Hangfire/issues/2478) | [an AOT sample publishes natively](https://github.com/Arcenox-co/TickerQ/tree/c6ed1e7daa90ab3f4c65b40319a153126a910093/samples) | [the trim and AOT analyzers, on both target frameworks](https://github.com/JasperFx/wolverine/blob/V6.35.0/src/Wolverine/Wolverine.csproj) | — |
| What you must still do | name job types in a way the trimmer can see, or root them — [the page says which paths still warn](how-tos/trimming-and-native-aot.md) | — | [supply a `JsonSerializerContext` through `WithJsonContext` for payloads](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | — | — |

## Target frameworks

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Targets | `net10.0`; the [3.x line](/documentation/quartz-3.x/quick-start) covers .NET Standard 2.0 and .NET Framework and is maintained | [`net451`, `net46`, `netstandard1.3`, `netstandard2.0`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Hangfire.Core.csproj) | [`net10.0`; parallel 8.x and 9.x lines target `net8.0` and `net9.0`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/Directory.Build.props) | [`net9.0` and `net10.0`](https://www.nuget.org/packages/WolverineFx/6.35.0) | [`net6.0`](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/Src/Coravel/Coravel.csproj) |
| Serializing what is stored | System.Text.Json, with Newtonsoft.Json available as a [second serializer](packages/json-serialization.md) | [Newtonsoft.Json only](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Common/SerializationHelper.cs) | [System.Text.Json, needing a `JsonSerializerContext` when trimmed](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/configuration.mdx) | — | — |

Hangfire's framework list is the widest here by a distance, and that is a real reason to choose it: it
is the only one of the five whose current line a .NET Framework application can take. Quartz's answer
there is the [3.x line](/documentation/quartz-3.x/quick-start), which is maintained but is not 4.x.

## Licence and price

| | Quartz.NET 4.1 | Hangfire 1.8.25 | TickerQ 10.4.0 | Wolverine 6.35 | Coravel 6.0.2 |
|---|---|---|---|---|---|
| Licence | Apache-2.0 | [LGPL v3, or a commercial licence](https://www.hangfire.io/pricing/) | [`MIT OR Apache-2.0`](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/LICENSE), with [a CLA for contributors](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/CLA.md) | [MIT](https://github.com/JasperFx/wolverine/blob/V6.35.0/LICENSE) | [MIT](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/LICENSE) |
| Free tier | everything | [Open: SQL Server and in-memory storage, community support](https://www.hangfire.io/pricing/) | everything | [everything in Wolverine](https://github.com/JasperFx/wolverine/blob/V6.35.0/LICENSE) | [the library](https://github.com/jamesmh/coravel/blob/88ea3e892cfa3ce3d50054c7b430f16457b4d919/LICENSE) |
| Paid tiers | none | [Startup $500, Business $1,500, Enterprise $4,500 per organization per year; batches and Redis storage are Pro, throttling is Ace](https://www.hangfire.io/pricing/) | none today. [A commercial model was put to the community on 3 August 2026](https://github.com/Arcenox-co/TickerQ/discussions/886), which says that nothing changes today and that existing MIT and Apache releases remain free permanently | [none for Wolverine; CritterWatch and AI Skills are the family's paid products](https://jasperfx.net/our-products/) | [Coravel Pro: free for personal use, $299 a year commercial](https://www.pro.coravel.net/) |

## Where Quartz.NET costs more than it is worth

Collected in one place, because a comparison that only flatters its author is not worth reading.

**The first five minutes are longer.** TickerQ is two registration lines and an attribute on a method;
Coravel is two lines and a fluent chain. Quartz's shortest form is
[comparable](quick-start.md#the-shortest-thing-that-works), but the moment a recurring schedule and a
database appear it is a builder chain, a connection string, a driver package of your own and a schema
to create. That is the price of a schedule that outlives the process, and it is a price — not everybody
needs to pay it.

**A schedule costs a row.** With a persistent store, adding a schedule is an `INSERT` and a round trip,
and every firing is a handful more. TickerQ's in-memory default and Coravel's whole model are a
dictionary insert. For thousands of short-lived one-off firings, that difference is real, and the
answer is [one durable job per job type with a trigger per firing](how-tos/one-off-job.md) rather than
a pretence that the row is free.

**A bad cron was a run-time exception rather than a build error, through 4.1.** TickerQ's source
generator won this outright, and 4.2 is where Quartz answers it: a cron literal or `const` is read at
build time ([Compile-Time Checks](tutorial/compile-time-checks.md)), and a job can declare its schedule
on its class ([Declaring Jobs with Attributes](tutorial/declaring-jobs-with-attributes.md)). An
expression assembled at run time is still read at run time, and for that
[`CronExpressionBuilder` and the "when does this fire" helper](cron-expressions.md#checking-an-expression)
are what Quartz offers.

**Continuations were a listener, not a contract, through 4.1.** 4.2 makes them a trigger the store
holds: see [Continuations and chaining](#continuations-and-chaining) above and
[Job Continuations](how-tos/job-continuations.md).

**Retry is opt-in, and so is its jitter.** Hangfire retries every job by default and spreads the
attempts. Quartz retries only the triggers you gave a policy to, and the waits are exactly what the
policy says — unless the policy names a `jitter`, which spreads them the same way.

**There are no queues.** Hangfire's `[Queue]` with several servers each subscribing to a different set
is a routing mechanism, and Quartz has nothing that routes.
[Execution groups](tutorial/execution-groups.md) bound how much of a category runs at once; they do not
decide which node takes it, and `PreferredNode` pins rather than balances.

**If the occurrence is a message, a bus may be the better home.** An application that already runs
Wolverine, already has an outbox and wants "publish X every weekday at 03:00" should use Wolverine's own
schedule rather than a second runtime and a set of tables. [Quartz.NET with Wolverine](how-tos/wolverine.md)
is that argument in full, including the cases where it goes the other way.

**.NET Framework is 3.x territory.** Quartz 4.x is `net10.0` only. Hangfire still ships `net451`.

## Coming from one of these

* [Coming from Hangfire](how-tos/coming-from-hangfire.md) — the API mapping, and the semantics that differ
* [Coming from TickerQ](how-tos/coming-from-tickerq.md) — the same, shorter
* [Quartz.NET with Wolverine](how-tos/wolverine.md) — running both in one host, and deciding which owns a schedule
