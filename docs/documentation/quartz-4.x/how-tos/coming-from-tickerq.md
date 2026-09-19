---

title: 'Coming from TickerQ'
---

# Coming from TickerQ

TickerQ's model is small, which makes the mapping to Quartz.NET short. It is written against
**TickerQ 10.4.0** — source at
[commit `c6ed1e7d`](https://github.com/Arcenox-co/TickerQ/tree/c6ed1e7daa90ab3f4c65b40319a153126a910093),
which is the commit that release's packages name, since there is no `v10.4.0` tag — and Quartz.NET 4.1.
[Comparison](../comparison.md) is where the two are weighed against each other, including what TickerQ
does better. This page is for when the decision is already made.

One difference decides most of the others: **a TickerQ job is a method with an attribute, found by a
source generator and scheduled by its string name; a Quartz job is a type, registered by that type and
scheduled through it.**

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

The compile-time check comes with you. TickerQ's generator makes a cron expression that will not parse
a build error (`TQ003`); Quartz 4.2 does the same, with the parser that reads the expression at run
time — see [Compile-Time Checks](../tutorial/compile-time-checks.md). The attribute shape comes too:
[`[QuartzJob]` and `[CronTrigger]`](../tutorial/declaring-jobs-with-attributes.md) declare a job and its
schedule on the class, and a source generator writes the registration above for you. An expression
assembled at run time is still read at run time, and for that
[`CronExpressionBuilder`](../cron-expressions.md#building-cron-expressions-programmatically)
builds an expression without writing the string, and
[asking the trigger when it fires](../cron-expressions.md#checking-an-expression) checks one you have.

What you gain is that the job type is the registration: no string to keep in step with a method name,
and no second place the name can be wrong.

## The API, side by side

| TickerQ | Quartz.NET | Where it differs |
|---|---|---|
| `[TickerFunction("name")]` on a method | a class implementing `IJob`, registered with `q.AddJob<T>(…)` or with [`[QuartzJob]`](../tutorial/declaring-jobs-with-attributes.md) on the class | the class is what the schedule names |
| `[TickerFunction("name", "*/5 * * * *")]` | `q.AddTrigger<T>(t => t.WithCronSchedule(…))`, or [`[CronTrigger("0 0/5 * * * ?")]`](../tutorial/declaring-jobs-with-attributes.md) on the class | the schedule is a trigger of its own, so one job can have several |
| `new TimeTickerEntity { Function = "name", ExecutionTime = … }` | `scheduler.ScheduleJob<TJob, TInput>(input, at)` | |
| `timeTicker.AddAsync<WelcomeJob>(executionTime)` | the same call | both are typed; Quartz's carries the payload type too |
| `new CronTickerEntity { Expression = … }` | a cron trigger through `TriggerBuilder` | |
| `RunCondition` on a child ticker | `JobChainingJobListener` | **no condition** — the follow-up runs whatever the parent did. See [Chaining](#chaining-has-no-condition-yet) |
| `Retries` + `RetryIntervals = [30, 120, 600]` | `RetryPolicy.Explicit(…)` on the trigger | the wait is held in the store rather than in the process |
| `maxConcurrency` on `[TickerFunction]` | an [execution group](../tutorial/execution-groups.md) and a limit | the limit can be counted across the cluster, not only per process |
| `TickerTaskPriority` | `Priority` on the trigger | TickerQ's orders dispatch per function; Quartz's breaks ties between triggers due at once |
| `TickerFunctionContext` | `IJobExecutionContext` | |
| `TerminateExecutionException` | return normally, or throw `JobExecutionException` with an unschedule instruction | |

A one-shot ticker is the overload that takes a payload and a time. `SendWelcomeEmailJob` here is an
`IJob<string>`, so the payload type is checked at the call site rather than being a column:

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
| `s.NodeIdentifier = …` | `QuartzSchedulerOptions.InstanceId`, and [`PreferredNode`](../tutorial/node-affinity.md) if a trigger must run somewhere particular |
| `s.MinPollingInterval` | `IdleWaitTime` — but it is a *ceiling* on an idle wait, not a floor on a cadence: see [below](#there-is-no-polling-floor) |
| `s.FallbackIntervalChecker` | the misfire handler, whose cadence follows `MisfireThreshold` |
| `SkipStaleCronOccurrencesOnStartup()` | the `DoNothing` [misfire instruction](../tutorial/more-about-triggers.md#misfire-instructions), per trigger |
| `opt.AddOperationalStore(ef => …)` | `q.UsePersistentStore(s => s.UsePostgres(cs))` — [ADO.NET, not EF Core](../tutorial/job-stores.md) |
| `opt.AddDashboard()` | `services.AddQuartzDashboard()` + `app.MapQuartzDashboard()` — and an authorization decision |
| `opt.AddOpenTelemetryInstrumentation()` | `AddSource(QuartzInstrumentation.ActivitySourceName)` and `AddMeter(QuartzInstrumentation.MeterName)` — [no package needed](../packages/opentelemetry-integration.md) |
| `opt.WithJsonContext(AppJsonContext.Default)` | `store.UseSystemTextJsonSerializer(r => r.AddTypeInfoResolver(…))` — [Publishing Trimmed and Native AOT](trimming-and-native-aot.md) |

## The differences that bite

### The cron dialect is not the same one

TickerQ parses with [NCrontab](https://github.com/atifaziz/NCrontab): six fields with seconds first, or
five that are expanded with a `0` seconds field, and a grammar of `*`, `,`, `-`, `/`, digits and names.
Quartz's is [six or seven](../cron-expressions.md) and supports `L`, `W`, `#` and `H` besides — but the
part that breaks a copied expression is smaller than any of that: **one of the two day fields must be
`?`** rather than `*`, because Quartz reads the two as a union and `?` is how one of them stands aside.

So TickerQ's `*/5 * * * * *` is Quartz's `*/5 * * * * ?`, and TickerQ's five-field `0 */6 * * *` is
`0 0 0/6 * * ?`. A five-field expression handed to Quartz in the default format is an error that names
the rewritten form rather than a silently different schedule; `CronFormat.Unix` reads one as written if
that is what you want.

### There is no polling floor

`MinPollingInterval` is a second by default and is a floor: it is the minimum pause between database
polls, so a TickerQ schedule finer than that is bounded by it.

`IdleWaitTime` is the nearest Quartz setting and it is the opposite shape. The scheduling thread asks
the store for the triggers due inside the next `IdleWaitTime` — thirty seconds by default — waits until
the earliest of those, or until `IdleWaitTime` elapses if there were none, and is woken early when
something changes the schedule. It bounds how long the thread sits idle, not how soon a trigger may
fire, so a seconds-level cron fires at the cadence it states. An expression that was quietly rounded
up starts firing as written.

### Time zones are per trigger, and the default is the machine's

`SchedulerTimeZone` is one setting for the whole scheduler and defaults to the machine's local zone. A
Quartz cron trigger also defaults to `TimeZoneInfo.Local`, so the *default* survives the move — but
there is no scheduler-wide setting to carry across. Whatever `SchedulerTimeZone` was set to has to be
repeated as `InTimeZone(…)` on each trigger, and the safe move is to say it explicitly everywhere
rather than to rely on two defaults agreeing.

### A missed occurrence is a decision, not a late run

TickerQ has no misfire policy: an overdue row is picked up by the fallback sweep and runs late, unless
`SkipStaleCronOccurrencesOnStartup()` was called, which drops occurrences older than its threshold.
Quartz asks the question per trigger, and the answer is one of three —
[`FireAndProceed`, `IgnoreMisfires` or `DoNothing`](../tutorial/more-about-triggers.md#misfire-instructions).
`SmartPolicy`, the default, means `FireAndProceed` for a cron trigger: one catch-up, then back on
schedule. `DoNothing` is the nearest thing to what `SkipStaleCronOccurrencesOnStartup` does, and unlike
it, it is a property of the trigger rather than a start-up sweep.

### Retries wait in the store, not in the worker

`Retries` and `RetryIntervals` retry inside the execution: a
[`Task.Delay` between attempts](https://github.com/Arcenox-co/TickerQ/blob/c6ed1e7daa90ab3f4c65b40319a153126a910093/src/TickerQ/Src/TickerExecutionTaskHandler.cs)
that holds the worker slot and the row's lease for the whole sequence, and that dies with the process.
Quartz's retry is a new fire time written to the store:

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

The slot is released between attempts, a node that dies during a ten-minute wait does not take the
retry with it, and any node can run the attempt. What you give up is that the array is now three
`TimeSpan`s rather than three integers, and that a retry is dropped when it would land at or after the
trigger's next scheduled occurrence — a ten-minute wait on a five-minute schedule quietly does nothing.
That rule and the rest are [Retrying Failed Jobs](retrying-failed-jobs.md).

### Concurrency has two settings, and one of them can be the cluster's

`MaxConcurrency` in `ConfigureScheduler` is how much this process runs at once; `maxConcurrency` on
`[TickerFunction]` is how much of one function runs at once, enforced by a `SemaphoreSlim` in that
process. Quartz splits the same two ideas, and the second one can be counted across every node sharing
the store:

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

`[DisallowConcurrentExecution]` is the third, narrower tool: one firing of a given job key at a time,
cluster-wide with a persistent store, with no number to choose.

### The dashboard will not start until you say who may reach it

TickerQ's dashboard defaults to
[`AuthMode.None`](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/dashboard/authentication.mdx)
and its documentation says so plainly: "By default the dashboard has no authentication — it's publicly
accessible." Quartz's takes the opposite default and enforces it at start-up: a mapping that carries
neither `RequireAuthorization` nor `AllowAnonymous`, under a host with no fallback policy, fails before
the web host binds a listener.

That means a straight port of a TickerQ setup will refuse to start, and the fix is one call at the map
site. `AddQuartzDashboard()` registers the services; `app.MapQuartzDashboard().RequireAuthorization(…)`
maps them. [Production hardening](../packages/dashboard.md#production-hardening) is the whole model,
including read-only mode and an allow-list of the job types that may be named through it.

### Chaining has no condition yet

`RunCondition` settles a child ticker on the parent's outcome — `OnSuccess`, `OnFailure`,
`OnCancelled`, `OnFailureOrCancelled`, `OnAnyCompletedStatus` or `InProgress`. Quartz's
`JobChainingJobListener` has no equivalent: it triggers the follow-up when the parent completes, and a
parent that threw has completed. The links live in memory with the listener rather than in the store,
so they are re-registered on every start and the follow-up runs on whichever node ran the parent.

If the chain must not run after a failure, check inside the follow-up job, or schedule it from the end
of the parent. Store-owned continuations that settle on the outcome are
[#3805](https://github.com/quartznet/quartznet/issues/3805), scheduled for 4.2.

One thing does get better in the move: TickerQ's chaining is
[`TimeTicker`-only](https://github.com/Arcenox-co/TickerQ-UI/blob/main/content/docs/guides/job-chaining.mdx),
so a recurring job cannot have a follow-up. `JobChainingJobListener` is about job keys, so a cron
trigger's job can.

### The store is ADO.NET, not EF Core

`AddOperationalStore(ef => …)` puts TickerQ's tables in your `DbContext`. Quartz's persistent store
talks to the database directly, with [its own tables](../db/) and its own DDL, so there is no
`DbContext` to add it to and no EF Core migration to generate. The schema is created either by
[`ProvisionSchema()`](../quick-start.md#creating-and-initializing-the-database) or by running the
script for your database; the tables are `QRTZ_`-prefixed and the prefix is configurable.

The practical consequence is that a Quartz schedule change is not part of your `SaveChanges`. If a
schedule must be written in the same transaction as your own data, that is the
[ambient transaction](../tutorial/job-stores.md) shape, not the EF Core one.

## Running both while you move

The two are ordinary hosted services against separate schemas and know nothing of each other, so they
can share a host for as long as the move takes. The one thing worth deciding early is which of them
owns a given schedule: a recurring job registered in both fires twice, and nothing will tell you.
Remove it from the one before adding it to the other.

## See also

* [Comparison](../comparison.md) — the two weighed against each other, sourced
* [One-Off Job](one-off-job.md) — the `ScheduleJob<TJob, TInput>` overloads in full
* [Retrying Failed Jobs](retrying-failed-jobs.md) — the retry policy and its rules
* [Cron Expression Reference](../cron-expressions.md) — the dialect, and how to check an expression
* [Publishing Trimmed and Native AOT](trimming-and-native-aot.md) — the equivalent of `WithJsonContext`
