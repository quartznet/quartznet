---

title: 'Coming from Hangfire'
---

# Coming from Hangfire

This page maps **Hangfire 1.8.25** ([tag `v1.8.25`](https://github.com/HangfireIO/Hangfire/tree/v1.8.25))
to Quartz.NET 4.1. To weigh the two against each other, see [Comparison](../comparison.md).

The main difference: **Hangfire's unit of work is a method call; Quartz's is a type.**
`BackgroundJob.Enqueue(() => mailer.SendWelcome("ada@example.com"))` serializes an expression tree and
replays the call later. Quartz stores a job type and a data map, and the container builds the job for each
firing:

<!-- snippet: sample_coming_from_hangfire_job -->
```csharp
// A Hangfire job is a method named by an expression tree; a Quartz job is a type. Its payload is a
// constructor-injected service, a JobDataMap entry, or — as here — the typed input of IJob<TInput>.
public sealed class SendWelcomeEmailJob : IJob<string>
{
    private readonly IMailer mailer;

    public SendWelcomeEmailJob(IMailer mailer) => this.mailer = mailer;

    public ValueTask Execute(IJobExecutionContext context, string emailAddress, CancellationToken cancellationToken = default)
    {
        return mailer.SendWelcome(emailAddress, cancellationToken);
    }
}
```
<!-- endSnippet -->

A method argument becomes a constructor-injected service, a `JobDataMap` entry, or the typed input of
`IJob<TInput>`.

From 4.3 a recurring job can also be a lambda, with its services as parameters:
`q.ScheduleJob("send-digest", (IMailer mailer, CancellationToken ct) => …, t => t.WithCronSchedule(…))`.
It is stored by its key, not as a serialized call, so every node registers it. See
[Delegate Jobs](../tutorial/delegate-jobs.md).

## The API, side by side

### Scheduling

| Hangfire | Quartz.NET | Difference |
|---|---|---|
| `BackgroundJob.Enqueue(() => …)` | `scheduler.ScheduleJob<TJob, TInput>(input, TimeSpan.Zero)`, or from 4.7 [`scheduler.TriggerJob(jobKey, input)`](one-off-job.md#a-stored-typed-job-run-now) for a stored job | a trigger that fires now, not a queue entry; both answer with its `TriggerKey` |
| `BackgroundJob.Schedule(() => …, TimeSpan)` | the same call with the delay | |
| `BackgroundJob.Schedule(() => …, DateTimeOffset)` | the `DateTimeOffset` overload of the same call | |
| `BackgroundJob.Delete(jobId)` | `scheduler.UnscheduleJob(triggerKey)` | the `TriggerKey` on the returned `ScheduledOneOffJob` is the handle |
| `BackgroundJob.Requeue(jobId)` | `scheduler.TriggerJob(jobKey)`, optionally with a `JobDataMap` | fires the job again now; there is no failed record to requeue |
| `BackgroundJob.Reschedule(jobId, …)` | `scheduler.RescheduleJob(triggerKey, newTrigger)` | |
| `BackgroundJob.ContinueJobWith(parentId, …)` | `.StartAfter(parentTriggerKey, condition)` on the follow-up's trigger | see [Continuations](#continuations) |
| `RecurringJob.AddOrUpdate(id, () => …, cron)` | `q.ScheduleJob<T>(t => t.WithIdentity(id).WithCronSchedule(…))`, or from 4.3 [`q.ScheduleJob(id, lambda, t => t.WithCronSchedule(…))`](../tutorial/delegate-jobs.md) | six cron fields, not five, and a different default time zone |
| [`Cron.MinuteInterval(10)`, `Cron.HourInterval(6)`, `Cron.Daily(3, 0)`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/Cron.cs) | from 4.3, `t.WithCronSchedule(cron => cron.Every(TimeSpan.FromMinutes(10)))`, `cron.Every(TimeSpan.FromHours(6))`, `cron.AtTime(new TimeOnly(3, 0))` | an interval that does not divide the hour or day throws instead of firing unevenly |
| A job that ends with `BackgroundJob.Schedule(…, delay)` to run again after a pause | from 4.7, a simple trigger with [`MeasureIntervalFromCompletion()`](../tutorial/simpletriggers.md#measured-from-completion) | the store schedules the next run when the firing completes, however it ended, and never runs it twice at once |
| `RecurringJob.RemoveIfExists(id)` | `scheduler.DeleteJob(jobKey)`, or `UnscheduleJob` to keep the job | |
| `RecurringJob.TriggerJob(id)` | `scheduler.TriggerJob(jobKey)` | |
| `IBackgroundJobClient`, `IRecurringJobManager` | `IScheduler`, injected | one interface for both; every member is awaitable |

Both `ScheduleJob` overloads return a `ScheduledOneOffJob` with the `TriggerKey` and the first fire time;
see [One-Off Job](one-off-job.md) for grouping them so a whole conversation can be cancelled at once.

<!-- snippet: sample_coming_from_hangfire_enqueue -->
```csharp
// BackgroundJob.Enqueue(() => mailer.SendWelcome("ada@example.com"))
await scheduler.ScheduleJob<SendWelcomeEmailJob, string>(
    "ada@example.com",
    TimeSpan.Zero,
    cancellationToken: cancellationToken);

// BackgroundJob.Schedule(() => mailer.SendWelcome("ada@example.com"), TimeSpan.FromDays(1))
ScheduledOneOffJob tomorrow = await scheduler.ScheduleJob<SendWelcomeEmailJob, string>(
    "ada@example.com",
    TimeSpan.FromDays(1),
    cancellationToken: cancellationToken);

// BackgroundJob.Delete(jobId) — the TriggerKey is the handle, and it is the trigger that goes
await scheduler.UnscheduleJob(tomorrow.TriggerKey, cancellationToken);
```
<!-- endSnippet -->

A recurring job becomes a job and its trigger, added in one call, with a new cron format and an explicit
time zone:

<!-- snippet: sample_coming_from_hangfire_recurring -->
```csharp
// RecurringJob.AddOrUpdate("nightly-import", () => importer.Run(), "0 2 * * *")
services.AddQuartz(q =>
{
    // One call adds the job and its trigger, and the job takes the trigger's key.
    q.ScheduleJob<NightlyImportJob>(t => t
        .WithIdentity("nightly-import")
        // Six fields, seconds first. Prepend 0 to Hangfire's five, not '*', which fires
        // every second of the matching minute.
        .WithCronSchedule("0 0 2 * * *", x => x
            // Hangfire reads a cron in UTC unless RecurringJobOptions says otherwise; a
            // Quartz cron trigger reads it in the machine's local zone unless you say
            // otherwise. Say otherwise.
            .InTimeZone(TimeZoneInfo.Utc)));
});
```
<!-- endSnippet -->

A job that several triggers fire is added once with `AddJob<T>`, and each trigger with `AddTrigger<T>`;
see [Microsoft DI Integration](../packages/microsoft-di-integration.md#a-worked-configuration).

### Attributes and filters

| Hangfire | Quartz.NET | Difference |
|---|---|---|
| `[AutomaticRetry(Attempts = n)]` | [`[RetryPolicy(…)]`](retrying-failed-jobs.md#declare-it-on-the-job-or-set-a-default) on the job type, or `RetryPolicy` on a trigger | **opt-in**: nothing is retried without a policy; see [Retry](#retry-is-opt-in) |
| `[DisableConcurrentExecution(seconds)]` | `[DisallowConcurrentExecution]` | no timeout, because nothing waits; see [below](#disableconcurrentexecution-waits-disallowconcurrentexecution-does-not) |
| `[Queue("critical")]` | `.WithExecutionGroup("critical")` on the trigger, and `ServeExecutionGroups("critical")` on the nodes that run it | on the trigger, not the job type; a limit bounds it. See [Queues](#queues-become-execution-groups) |
| `[JobDisplayName("…")]` | `.WithDescription("…")` on the job or trigger | |
| `[LatencyTimeout(seconds)]` — delete a job that waited too long to start | the `DoNothing` [misfire instruction](../tutorial/more-about-triggers.md#misfire-instructions) | skips that occurrence rather than deleting anything |
| `GlobalJobFilters.Filters.Add(…)` | [job execution middleware](../tutorial/job-execution-middleware.md), or a [job listener](../tutorial/trigger-and-job-listeners.md) | middleware wraps the execution; a listener observes it |
| `PerformContext` | `IJobExecutionContext` | |
| `context.WriteProgressBar()` ([Hangfire.Console](https://github.com/pieceofsummer/Hangfire.Console)) | `context.ReportProgress(percent, message)` | shown on Currently Executing; see [Progress and Execution Logs](progress-and-execution-logs.md) |
| `context.WriteLine(…)` (Hangfire.Console) | an injected `ILogger`, with `q.UseExecutionLogCapture()` | the lines go to your logging as well, and onto the execution's history row |
| a `CancellationToken` parameter | the `CancellationToken` parameter of `Execute` | same token as `IJobExecutionContext.CancellationToken`; cancelled by `Interrupt` and `InterruptFireInstance` |

### Hosting and configuration

| Hangfire | Quartz.NET |
|---|---|
| `services.AddHangfire(x => x.UseSqlServerStorage(cs))` | `builder.AddQuartz(q => q.UsePersistentStore(s => s.UseSqlServer(cs)))` |
| `services.AddHangfireServer()` | `builder.AddQuartzHostedService()` |
| `BackgroundJobServerOptions.WorkerCount` | `q.UseDefaultThreadPool(maxConcurrency: n)` |
| `BackgroundJobServerOptions.Queues` | `q.ServeExecutionGroups("critical", "default")`, or `Scheduler:ServedExecutionGroups`; see [below](#queues-become-execution-groups) |
| `BackgroundJobServerOptions.ServerTimeout` | `CheckinInterval` and `CheckinMisfireThreshold` on the clustering options — [Tuning the check-in](../tutorial/advanced-enterprise-features.md#tuning-the-check-in) |
| `SchedulePollingInterval` | `IdleWaitTime` — a *ceiling*, not a tick: see [Sub-minute schedules](#sub-minute-schedules-behave-differently) |
| `JobStorage.Current.JobExpirationTimeout` | `ExecutionHistoryOptions.Retention` — a different thing; see [Retention](#job-expiration-is-not-history-retention) |
| `app.UseHangfireDashboard(path, options)` | `app.MapQuartzDashboard()` — will not start without an authorization decision |
| `IDashboardAuthorizationFilter` | `RequireAuthorization(policy)` at the map site |
| `DashboardOptions.IsReadOnlyFunc` | `QuartzDashboardOptions.ReadOnly` |
| SQL Server storage creating its own schema | [`ProvisionSchema()`, off by default](../quick-start.md#creating-and-initializing-the-database) |

## The differences that bite

### A cron expression has six fields, and a different default time zone

| | Hangfire ([Cronos](https://github.com/HangfireIO/Cronos)) | Quartz ([reference](../cron-expressions.md)) |
|---|---|---|
| Fields | five, or six with a leading seconds field | six or seven, seconds first |
| Day fields | both restricted: their intersection | `*` or `?` restricts nothing; both restricted: their **union** |
| Day-of-week numbers | Sunday is `0` | Sunday is `1` |
| Default time zone | UTC ([`RecurringJobOptions.TimeZone`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/RecurringJobOptions.cs)) | `TimeZoneInfo.Local`, or the scheduler's [`DefaultTimeZone`](../tutorial/more-about-triggers.md#the-scheduler-s-default-time-zone) from 4.7 |

* `q.UseDefaultTimeZone(TimeZoneInfo.Utc)` gives every trigger that names no zone Hangfire's default, so a
  migrated schedule fires at the same instants without an `InTimeZone` on each.
* Hangfire's `0 2 * * *` is Quartz's `0 0 2 * * *`. Prepend `0`, not `*`: a `*` seconds field fires every
  second of each matching minute, and
  [`QZ0007`](../tutorial/compile-time-checks.md#qz0007-wildcardsecondsfield) warns of it.
* Write days of the week as names. `1` is Monday in Hangfire and Sunday here; `MON` is Monday in both. See
  [If your expressions came from another .NET cron library](../cron-expressions.md#if-your-expressions-came-from-another-net-cron-library).
* Hangfire's `0 2 13 * FRI` fires only on a Friday the 13th. Quartz's `0 0 2 13 * FRI` fires on every 13th
  and every Friday.
* **Set `InTimeZone(TimeZoneInfo.Utc)` on every trigger you move.** Otherwise, on a server not in UTC, it
  fires at a different hour, and twice or not at all on the two days a year the offset changes.
* [`CronFormat.Unix`](../cron-expressions.md#the-unix-five-field-form) keeps the five-field form. The format
  is stated, not detected: a five-field string in the default format is an error naming the rewrite.

### Sub-minute schedules behave differently

Hangfire enqueues recurring jobs on a poll,
[`BackgroundJobServerOptions.SchedulePollingInterval`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJobServerOptions.cs)
(fifteen seconds by default), and
[documents recurring jobs as minute-based](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html).
Quartz acquires triggers due within the next `IdleWaitTime` (thirty seconds by default) and waits for the
earliest; a schedule change wakes it early. `IdleWaitTime` limits idle time, not how soon a trigger fires,
so the seconds field is honoured. An expression Hangfire rounded up to a minute fires at its stated cadence
in Quartz. See [What the loop costs while it waits](external-leader.md#what-the-loop-costs-while-it-waits).

### Misfires are decided per trigger, not per job

Hangfire's `MisfireHandlingMode` is per recurring job. Quartz's
[misfire instructions](../tutorial/more-about-triggers.md#misfire-instructions) are per trigger and differ
by trigger kind:

| `MisfireHandlingMode` | The nearest `CronTriggerMisfireInstruction` |
|---|---|
| `Relaxed` — one job however many occurrences were missed | `FireAndProceed` — one catch-up, then the schedule resumes |
| `Strict` — one job per missed occurrence | `IgnoreMisfires` — every missed occurrence, as fast as the pool allows |
| `Ignorable` — none | `DoNothing` — skips to the next scheduled occurrence |

* Both defaults match: Hangfire's is `Relaxed`, and Quartz's `SmartPolicy` means `FireAndProceed` for a
  cron trigger.
* "Late" is Hangfire's polling interval, and Quartz's `MisfireThreshold` on the job store: one minute for
  the ADO.NET store, five seconds in memory.

### `DisableConcurrentExecution` waits; `[DisallowConcurrentExecution]` does not

Hangfire's attribute takes a distributed lock with a timeout; a worker that times out
[throws `DistributedLockTimeoutException`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/DisableConcurrentExecutionAttribute.cs),
which `AutomaticRetry` then retries. Quartz's attribute has no timeout: while a firing of the job key runs,
its other triggers are `Blocked` and released on completion, with no worker occupied. With a persistent
store the exclusion is cluster-wide.

From 4.3 a trigger can decide instead of its job, with an [overlap policy](overlap-policy.md): `BufferOne`
holds one firing until the running one ends, `Skip` drops it, `CancelPrevious` interrupts the running one.
From 4.7 a simple trigger can [measure its interval from completion](../tutorial/simpletriggers.md#measured-from-completion),
so it never overlaps itself at all.

### Queues become execution groups

Hangfire's `[Queue]` names the work and a server subscribes to queue names. Quartz's
[execution group](../tutorial/execution-groups.md) is the name, on the trigger; a node
[serves or refuses groups](../tutorial/execution-groups.md#routing-which-node-serves-a-group), which is the
subscription; a limit on the group bounds how much of it runs:

<!-- snippet: sample_coming_from_hangfire_queues -->
```csharp
// [Queue("reports")] on the job, plus a server that subscribes to that queue
services.AddQuartz(q =>
{
    q.AddJob<ReportingJob>(j => j.WithIdentity("monthly-report"));
    q.AddTrigger<ReportingJob>(t => t
        .ForJob("monthly-report")
        .WithCronSchedule("0 0 3 1 * ?")
        .WithExecutionGroup("reports"));

    // The server side of BackgroundJobServerOptions.Queues: this node runs "reports" triggers
    // and nothing else. Leave it out on a node that runs everything.
    q.ServeExecutionGroups("reports");

    q.UseExecutionLimits(limits =>
    {
        // Two at a time across every node sharing the store, rather than two per process.
        limits.ForGroup("reports", maxConcurrent: 2, ExecutionLimitScope.Cluster);
    });
});
```
<!-- endSnippet -->

* `ExecutionLimitScope.Cluster` counts the limit across the cluster (Hangfire's equivalent is
  `Hangfire.Throttling`, on the Business tier).
* The group is on the trigger, not on the job type: one job can run under several groups.
* A group with no live node serving it waits; the health check and the dashboard say so. Hangfire's
  equivalent is a queue no server listens to, which grows silently.
* Ungrouped triggers are the `_` group: a node that serves `critical` alone does not run them.
* To run on one particular machine, pin a trigger to an instance with
  [`PreferredNode`](../tutorial/node-affinity.md).

### Retry is opt-in

Hangfire's `AutomaticRetryAttribute` is in `GlobalJobFilters` by default: every job retries ten times.
Quartz retries only when a policy applies: the trigger's own, the job type's `[RetryPolicy]`, or the
scheduler's default. The attribute is the counterpart of `[AutomaticRetry]`:

<!-- snippet: sample_coming_from_hangfire_retry -->
```csharp
// [AutomaticRetry(Attempts = 3, DelaysInSeconds = new[] { 60, 300, 900 })]
[RetryPolicy("00:01:00", "00:05:00", "00:15:00")]
public sealed class NightlyImportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
```
<!-- endSnippet -->

`UseDefaultRetryPolicy` is the counterpart of the global filter:

<!-- snippet: sample_coming_from_hangfire_retry_default -->
```csharp
// GlobalJobFilters.Filters.Add(new AutomaticRetryAttribute { Attempts = 10 })
services.AddQuartz(q =>
{
    // Every trigger whose own policy and job type name none.
    q.UseDefaultRetryPolicy(RetryPolicy.Exponential(
        maxAttempts: 10,
        initialDelay: TimeSpan.FromSeconds(15),
        factor: 2,
        maxDelay: TimeSpan.FromHours(1),
        jitter: 0.2));
});
```
<!-- endSnippet -->

* A trigger's own policy beats the job type's, so two triggers of one job can retry differently.
* A scheduled retry is stored with the trigger, so it survives a restart and any node runs it.
* Waits are exact unless `Exponential` is given jitter (its fifth argument), which spreads them as
  Hangfire's do.
* A retry never displaces the trigger's next occurrence, running out is not an error, and a retry uses no
  repeat count. When attempts run out, `ITriggerListener.TriggerRetriesExhausted`, a log event, a counter
  and a final history row report it; the row has the dashboard's *Run again* button, the equivalent of a
  requeue. From 4.4 it passes the run's input back when the history [records inputs](job-outcomes.md#record-a-run-s-input).
  See [When the policy gives up](retrying-failed-jobs.md#when-the-policy-gives-up).
* From 4.3, [`PauseTriggerWhenRetriesExhausted()`](pausing-with-a-reason.md#pausing-when-retries-run-out)
  pauses a trigger that gave up, with the error as the reason, so it stays stopped until resumed, as a
  Hangfire job stays `Failed` until requeued.

### Continuations

`ContinueJobWith` (a `JobContinuationOptions`, default `OnlyOnSucceededState`) becomes
[a continuation](job-continuations.md): a trigger with `StartAfter(parentTriggerKey, condition)`, held by
the **store** in `TriggerState.Awaiting` and released inside the parent's transaction. `OnSuccess` is the
default.

* **The parent is a trigger firing, not a job.** Use the `TriggerKey` the one-call overloads return; for a
  job with several triggers, name the one to wait for.
* **The conditions differ.** `OnSuccess`, `OnFailure`, `OnCancellation` and `OnVeto` are flags
  (`OnFailure | OnCancellation` needs no member). Nothing matches `OnlyOnDeletedState`: Quartz keeps no job
  record to delete.
* For a *recurring* conditional chain, use `JobChainingJobListener` with the same conditions. Its links fire
  on every completion but live in memory: re-registered on every start, and the follow-up runs on the node
  that ran the parent.

### Job expiration is not history retention

Hangfire's `JobStorage.Current.JobExpirationTimeout` keeps succeeded and deleted *job records* for one day;
failed ones never expire and stay in the dashboard. Quartz keeps no job record: a firing's fired-trigger row
is deleted when it completes. What remains is the
[execution history](../packages/dashboard.md#execution-history-and-misfires), in memory by default, 2,000
entries per scheduler for 24 hours (`ExecutionHistoryOptions.Retention`, `MaxEntriesPerScheduler`). It is a
recent-activity view; keep durable records in your logging pipeline.

### The dashboard is fail-closed

Hangfire's dashboard is
[readable from localhost by default](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html).
Quartz's stops the application from starting until the mapping says `RequireAuthorization`, or
`AllowAnonymous` if you mean it:

<!-- snippet: sample_coming_from_hangfire_dashboard -->
```csharp
// app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [ ... ] })
app.MapQuartzDashboard().RequireAuthorization("QuartzOperators");
```
<!-- endSnippet -->

See [Production hardening](../packages/dashboard.md#production-hardening) for read-only mode and the job
type allow-list.

### What "once" means

Hangfire retries a job interrupted by an exception or shut-down, so it
[asks for re-entrant methods](https://docs.hangfire.io/en/latest/best-practices.html). In Quartz a firing
lost with its node is **not** repeated unless the job asks for
[recovery](../tutorial/advanced-enterprise-features.md#asking-for-recovery) with `RequestRecovery()`. Write
jobs to be safely re-run either way; see
[Best Practices](/documentation/best-practices#assume-the-job-will-run-more-than-once).

## Running both while you move

They are separate hosted services on separate schemas, so they can share a host. A recurring job registered
in both runs twice, unreported: remove each schedule from Hangfire before adding it to Quartz.

## See also

* [Comparison](../comparison.md) — the two weighed against each other, sourced
* [One-Off Job](one-off-job.md) — the `ScheduleJob<TJob, TInput>` overloads in full
* [Retrying Failed Jobs](retrying-failed-jobs.md) — the retry policy and its rules
* [Execution Groups](../tutorial/execution-groups.md) — what `[Queue]` becomes
* [Migration Guide](../migration-guide.md) — for a move from Quartz 3.x rather than from Hangfire
