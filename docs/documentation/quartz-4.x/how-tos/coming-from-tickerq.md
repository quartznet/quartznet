---

title: 'Coming from TickerQ'
---

# Coming from TickerQ

This page maps **TickerQ 10.4.0** (source at
[commit `c6ed1e7d`](https://github.com/Arcenox-co/TickerQ/tree/c6ed1e7daa90ab3f4c65b40319a153126a910093),
which that release's packages name; there is no `v10.4.0` tag) to Quartz.NET 4.1. To weigh the two, see
[Comparison](../comparison.md).

The main difference: **a TickerQ job is an attributed method, found by a source generator and scheduled by
its string name; a Quartz job is a type, registered and scheduled by that type**, so there is no name to
keep in step with a method.

<!-- snippet: sample_coming_from_tickerq_job -->
```csharp
// [TickerFunction("cleanup", "0 */6 * * *")] on a method
services.AddQuartz(q =>
{
    q.AddJob<CleanupJob>(j => j.WithIdentity("cleanup"));
    q.AddTrigger<CleanupJob>(t => t
        .ForJob("cleanup")
        // NCrontab takes '*' in both day fields; Quartz wants '?' in one of them, and it
        // reads six fields with seconds first.
        .WithCronSchedule("0 0 0/6 * * ?"));
});
```
<!-- endSnippet -->

* **Build-time cron checks carry over.** TickerQ reports an unparseable expression as `TQ003`; Quartz 4.2
  does the same with its run-time parser. See [Compile-Time Checks](../tutorial/compile-time-checks.md).
* **So do attributes.** [`[QuartzJob]` and `[CronTrigger]`](../tutorial/declaring-jobs-with-attributes.md)
  declare a job and schedule on the class; a source generator writes the registration.
* An expression built at run time is checked at run time. Build one with
  [`CronExpressionBuilder`](../cron-expressions.md#building-cron-expressions-programmatically), or check
  one by [asking the trigger when it fires](../cron-expressions.md#checking-an-expression).

## The API, side by side

| TickerQ | Quartz.NET | Difference |
|---|---|---|
| `[TickerFunction("name")]` on a method | a class implementing `IJob`, registered with `q.AddJob<T>(…)` or with [`[QuartzJob]`](../tutorial/declaring-jobs-with-attributes.md) on the class | the schedule names the class |
| `[TickerFunction("name", "*/5 * * * *")]` | `q.AddTrigger<T>(t => t.WithCronSchedule(…))`, or [`[CronTrigger("0 0/5 * * * ?")]`](../tutorial/declaring-jobs-with-attributes.md) on the class | the schedule is its own trigger, so one job can have several |
| `new TimeTickerEntity { Function = "name", ExecutionTime = … }` | `scheduler.ScheduleJob<TJob, TInput>(input, at)` | |
| `timeTicker.AddAsync<WelcomeJob>(executionTime)` | the same call | both typed; Quartz's also carries the payload type |
| `new CronTickerEntity { Expression = … }` | a cron trigger through `TriggerBuilder` | |
| `RunCondition` on a child ticker | `.StartAfter(parentTriggerKey, condition)` on the follow-up's trigger | see [Chaining](#chaining-and-its-condition) |
| `Retries` + `RetryIntervals = [30, 120, 600]` | `RetryPolicy.Explicit(…)` on the trigger | the wait is held in the store, not the process |
| `maxConcurrency` on `[TickerFunction]` | an [execution group](../tutorial/execution-groups.md) and a limit | the limit can be cluster-wide |
| `TickerTaskPriority` | `Priority` on the trigger | TickerQ's orders dispatch per function; Quartz's breaks ties between triggers due together |
| `TickerFunctionContext` | `IJobExecutionContext` | |
| `TerminateExecutionException` | return normally, or throw `JobExecutionException` with an unschedule instruction | |

A one-shot ticker is the overload taking a payload and a time. `SendWelcomeEmailJob` is an
`IJob<string>`, so the payload type is checked at the call site:

<!-- snippet: sample_coming_from_tickerq_one_off -->
```csharp
// await timeTicker.AddAsync(new TimeTickerEntity { Function = "send-welcome", ... })
await scheduler.ScheduleJob<SendWelcomeEmailJob, string>(
    "ada@example.com",
    TimeSpan.FromSeconds(30),
    cancellationToken: cancellationToken);
```
<!-- endSnippet -->

### Configuration

| TickerQ | Quartz.NET |
|---|---|
| `builder.Services.AddTickerQ(opt => …)` | `builder.AddQuartz(q => …)` |
| `app.UseTickerQ()` | `builder.AddQuartzHostedService()` |
| `app.UseTickerQ(TickerQStartMode.Manual)` | `builder.AddQuartzHostedService(o => o.AutoStart = false)`, then `scheduler.Start()` |
| `s.MaxConcurrency = 16` | `q.UseDefaultThreadPool(maxConcurrency: 16)` |
| `s.SchedulerTimeZone = …` | `InTimeZone(…)` on each trigger — there is no scheduler-wide zone |
| `s.NodeIdentifier = …` | `QuartzSchedulerOptions.InstanceId`, and [`PreferredNode`](../tutorial/node-affinity.md) to pin a trigger to a node |
| `s.MinPollingInterval` | `IdleWaitTime` — a ceiling, not a floor: see [below](#there-is-no-polling-floor) |
| `s.FallbackIntervalChecker` | the misfire handler, whose cadence follows `MisfireThreshold` |
| `SkipStaleCronOccurrencesOnStartup()` | the `DoNothing` [misfire instruction](../tutorial/more-about-triggers.md#misfire-instructions), per trigger |
| `opt.AddOperationalStore(ef => …)` | `q.UsePersistentStore(s => s.UsePostgres(cs))` — [ADO.NET, not EF Core](../tutorial/job-stores.md) |
| `opt.AddDashboard()` | `services.AddQuartzDashboard()` + `app.MapQuartzDashboard()`, plus an authorization decision |
| `opt.AddOpenTelemetryInstrumentation()` | `AddSource(QuartzInstrumentation.ActivitySourceName)` and `AddMeter(QuartzInstrumentation.MeterName)` — [no package needed](../packages/opentelemetry-integration.md) |
| `opt.WithJsonContext(AppJsonContext.Default)` | `store.UseSystemTextJsonSerializer(r => r.AddTypeInfoResolver(…))` — [Publishing Trimmed and Native AOT](trimming-and-native-aot.md) |

## The differences that bite

### The cron dialect is not the same one

| | TickerQ ([NCrontab](https://github.com/atifaziz/NCrontab)) | Quartz ([reference](../cron-expressions.md)) |
|---|---|---|
| Fields | six, seconds first; or five, expanded with a `0` seconds field | six or seven |
| Special characters | `*`, `,`, `-`, `/`, digits and names | those, plus `L`, `W`, `#` and `H` |
| Day fields | `*` in both | **one of the two must be `?`** (the two are a union) |

* `*/5 * * * * *` becomes `*/5 * * * * ?`; five-field `0 */6 * * *` becomes `0 0 0/6 * * ?`.
* A five-field expression in the default format is an error naming the rewrite, not a silently different
  schedule. `CronFormat.Unix` reads it as written.

### There is no polling floor

TickerQ's `MinPollingInterval` (one second by default) is the minimum pause between database polls, so it
bounds finer schedules. Quartz's `IdleWaitTime` (thirty seconds by default) is a ceiling: the thread waits
until the earliest trigger due within it, and a schedule change wakes it early. A seconds-level cron fires
at its stated cadence, including one TickerQ was rounding up. See
[What the loop costs while it waits](external-leader.md#what-the-loop-costs-while-it-waits).

### Time zones are per trigger, and the default is the machine's

Both default to the machine's zone (TickerQ's `SchedulerTimeZone`, Quartz's `TimeZoneInfo.Local` per cron
trigger), but Quartz has no scheduler-wide setting. Set `InTimeZone(…)` explicitly on every trigger.

### A missed occurrence is a decision, not a late run

TickerQ's fallback sweep runs an overdue row late, unless `SkipStaleCronOccurrencesOnStartup()` drops
occurrences older than its threshold. Quartz decides per trigger:
[`FireAndProceed`, `IgnoreMisfires` or `DoNothing`](../tutorial/more-about-triggers.md#misfire-instructions).
The default `SmartPolicy` is `FireAndProceed` for cron (one catch-up, then on schedule). `DoNothing` is the
nearest to `SkipStaleCronOccurrencesOnStartup`, set per trigger instead of by a start-up sweep.

### Retries wait in the store, not in the worker

TickerQ retries inside the execution with a
[`Task.Delay` between attempts](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/Src/TickerExecutionTaskHandler.cs),
holding the worker slot and the row's lease, and ending with the process. A Quartz retry is a new fire time
in the store:

<!-- snippet: sample_coming_from_tickerq_retry -->
```csharp
// Retries = 3, RetryIntervals = [30, 120, 600]
services.AddQuartz(q =>
{
    q.AddJob<CleanupJob>(j => j.WithIdentity("cleanup"));
    q.AddTrigger<CleanupJob>(t => t
        .ForJob("cleanup")
        .WithCronSchedule("0 0 0/6 * * ?")
        .WithRetryPolicy(RetryPolicy.Explicit(
            TimeSpan.FromSeconds(30),
            TimeSpan.FromMinutes(2),
            TimeSpan.FromMinutes(10))));
});
```
<!-- endSnippet -->

* The slot is released between attempts, a retry survives a node dying, and any node can run it.
* Intervals are `TimeSpan`s, not integers.
* A retry that would land at or after the next occurrence is dropped: a ten-minute wait on a five-minute
  schedule does nothing. See [Retrying Failed Jobs](retrying-failed-jobs.md).
* When attempts run out, TickerQ marks the ticker failed; Quartz returns the trigger to its schedule and
  reports it through
  [`ITriggerListener.TriggerRetriesExhausted`, a log event, a counter and a final history row](retrying-failed-jobs.md#when-the-policy-gives-up).

### Concurrency has two settings, and one of them can be the cluster's

| Limit | TickerQ | Quartz |
|---|---|---|
| This process | `MaxConcurrency` in `ConfigureScheduler` | the thread pool size |
| One function or category | `maxConcurrency` on `[TickerFunction]`, a `SemaphoreSlim` in the process | an execution group limit, optionally counted across every node sharing the store |

<!-- snippet: sample_coming_from_tickerq_concurrency -->
```csharp
services.AddQuartz(q =>
{
    // s.MaxConcurrency = 16 — how much this process runs at once
    q.UseDefaultThreadPool(maxConcurrency: 16);

    // maxConcurrency on [TickerFunction] — how much of one category runs at once, except
    // that the scope is yours to choose
    q.UseExecutionLimits(limits =>
    {
        limits.ForGroup("cleanup", maxConcurrent: 1, ExecutionLimitScope.Cluster);
    });
});
```
<!-- endSnippet -->

`[DisallowConcurrentExecution]` is narrower: one firing per job key at a time, cluster-wide with a persistent
store.

### The dashboard will not start until you say who may reach it

TickerQ's dashboard defaults to
[`AuthMode.None`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/authentication.mdx).
Quartz's fails before the web host binds a listener if the mapping has neither `RequireAuthorization` nor
`AllowAnonymous` and the host has no fallback policy. Add one call:
`app.MapQuartzDashboard().RequireAuthorization(…)` after `AddQuartzDashboard()`. See
[Production hardening](../packages/dashboard.md#production-hardening) for read-only mode and the job type
allow-list.

### Chaining and its condition

TickerQ's `RunCondition` becomes [a continuation](job-continuations.md):
`StartAfter(parentTriggerKey, condition)` on the follow-up's trigger, held by the **store** in
`TriggerState.Awaiting` and released inside the parent's transaction. The parent is a *trigger's firing*,
not a row.

| TickerQ `RunCondition` | Quartz `ContinuationCondition` |
|---|---|
| `OnSuccess` | `OnSuccess` |
| `OnFailure` | `OnFailure` |
| `OnCancelled` | `OnCancellation` |
| `OnFailureOrCancelled` | `OnFailure` and `OnCancellation` combined; the enum is flags |
| `OnAnyCompletedStatus` | `OnAnyOutcome` |
| `InProgress` | none: a continuation is released by an outcome, and a running firing has none |
| — | `OnVeto`, for a firing an `ITriggerListener` refused |

TickerQ's chaining is [`TimeTicker`-only](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/job-chaining.mdx);
`JobChainingJobListener` works on job keys, so a cron trigger's job can have a follow-up.

### The store is ADO.NET, not EF Core

TickerQ's `AddOperationalStore(ef => …)` adds tables to your `DbContext`. Quartz uses
[its own tables](../db/) (`QRTZ_`-prefixed, prefix configurable) with no `DbContext` or EF migration; create
them with [`ProvisionSchema()`](../quick-start.md#creating-and-initializing-the-database) or the script for
your database. A schedule change is not part of `SaveChanges`; for one transaction with your data, use an
[ambient transaction](../tutorial/job-stores.md).

## Running both while you move

They are separate hosted services on separate schemas, so they can share a host. A recurring job registered
in both fires twice, unreported: remove it from one before adding it to the other.

## See also

* [Comparison](../comparison.md) — the two weighed against each other, sourced
* [One-Off Job](one-off-job.md) — the `ScheduleJob<TJob, TInput>` overloads in full
* [Retrying Failed Jobs](retrying-failed-jobs.md) — the retry policy and its rules
* [Cron Expression Reference](../cron-expressions.md) — the dialect, and how to check an expression
* [Publishing Trimmed and Native AOT](trimming-and-native-aot.md) — the equivalent of `WithJsonContext`
