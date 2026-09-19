---

title: 'Coming from Hangfire'
---

# Coming from Hangfire

Most of Hangfire's API has a Quartz.NET equivalent that does the same thing, and a handful have one
that looks the same and behaves differently. This page is both lists.

It is written against **Hangfire 1.8.25** ([tag `v1.8.25`](https://github.com/HangfireIO/Hangfire/tree/v1.8.25))
and Quartz.NET 4.1. It does not argue that you should move —
[Comparison](../comparison.md) is where the two are weighed against each other, including the places
Hangfire is the better answer. This page is for when the decision is already made.

The one difference to understand before the table makes sense: **Hangfire's unit of work is a method
call and Quartz's is a type.** `BackgroundJob.Enqueue(() => mailer.SendWelcome("ada@example.com"))`
serializes an expression tree — the type, the method and the arguments — and reconstructs the call
later. Quartz stores a job type and a data map, and the container builds the job for each firing:

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

That is the shape every row below follows from. An argument that was a method parameter becomes either
a constructor-injected service, a `JobDataMap` entry, or the typed input of `IJob<TInput>`.

## The API, side by side

### Scheduling

| Hangfire | Quartz.NET | Where it differs |
|---|---|---|
| `BackgroundJob.Enqueue(() => …)` | `scheduler.ScheduleJob<TJob, TInput>(input, TimeSpan.Zero)` | a trigger that fires now, rather than an entry on a queue |
| `BackgroundJob.Schedule(() => …, TimeSpan)` | the same call with the delay | |
| `BackgroundJob.Schedule(() => …, DateTimeOffset)` | the `DateTimeOffset` overload of the same call | |
| `BackgroundJob.Delete(jobId)` | `scheduler.UnscheduleJob(triggerKey)` | the `TriggerKey` on the returned `ScheduledOneOffJob` is the handle |
| `BackgroundJob.Requeue(jobId)` | `scheduler.TriggerJob(jobKey)`, optionally with a `JobDataMap` | fires the job again now; there is no failed record to put back on a queue |
| `BackgroundJob.Reschedule(jobId, …)` | `scheduler.RescheduleJob(triggerKey, newTrigger)` | |
| `BackgroundJob.ContinueJobWith(parentId, …)` | `JobChainingJobListener` | **the follow-up runs whatever the parent did**, including throwing. See [Continuations](#continuations-are-the-weakest-mapping) |
| `RecurringJob.AddOrUpdate(id, () => …, cron)` | `q.AddJob<T>(…)` + `q.AddTrigger<T>(t => t.WithCronSchedule(…))` | six cron fields, not five, and the default time zone differs |
| `RecurringJob.RemoveIfExists(id)` | `scheduler.DeleteJob(jobKey)`, or `UnscheduleJob` to keep the job | |
| `RecurringJob.TriggerJob(id)` | `scheduler.TriggerJob(jobKey)` | |
| `IBackgroundJobClient`, `IRecurringJobManager` | `IScheduler`, injected | one interface for both, and every member is awaitable |

Both `ScheduleJob` overloads answer with a `ScheduledOneOffJob` carrying the `TriggerKey` and the first
fire time; [One-Off Job](one-off-job.md) is the page on naming and grouping those so a whole
conversation can be cancelled at once.

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

A recurring job becomes a durable job plus a trigger. Two things change in the move, and both are in
the sample:

<!-- snippet: sample_coming_from_hangfire_recurring -->
```csharp
// RecurringJob.AddOrUpdate("nightly-import", () => importer.Run(), "0 2 * * *")
services.AddQuartz(q =>
{
    q.AddJob<NightlyImportJob>(j => j.WithIdentity("nightly-import"));
    q.AddTrigger<NightlyImportJob>(t => t
        .ForJob("nightly-import")
        // Six fields, and one of the two day fields must be '?'. A Hangfire expression is
        // five fields read as minutes upward, so prepend the seconds field.
        .WithCronSchedule("0 0 2 * * ?", x => x
            // Hangfire reads a cron in UTC unless RecurringJobOptions says otherwise; a
            // Quartz cron trigger reads it in the machine's local zone unless you say
            // otherwise. Say otherwise.
            .InTimeZone(TimeZoneInfo.Utc)));
});
```
<!-- endSnippet -->

### Attributes and filters

| Hangfire | Quartz.NET | Where it differs |
|---|---|---|
| `[AutomaticRetry(Attempts = n)]` | [`RetryPolicy`](retrying-failed-jobs.md) on the trigger | **opt-in, and per trigger rather than per job.** Hangfire applies `AutomaticRetryAttribute` to everything by default; Quartz retries nothing you did not ask it to |
| `[DisableConcurrentExecution(seconds)]` | `[DisallowConcurrentExecution]` | no timeout, because there is no wait: the store leaves the second trigger `Blocked` until the first firing completes |
| `[Queue("critical")]` | [an execution group](../tutorial/execution-groups.md) and a limit | **an execution group bounds concurrency; it does not route.** See [Queues](#queues-become-limits-not-routes) |
| `[JobDisplayName("…")]` | `.WithDescription("…")` on the job or trigger | |
| `[LatencyTimeout(seconds)]` — delete a job that waited too long to start | the `DoNothing` [misfire instruction](../tutorial/more-about-triggers.md#misfire-instructions) | the trigger says what a late firing should do, and it skips that occurrence rather than deleting anything |
| `GlobalJobFilters.Filters.Add(…)` | [job execution middleware](../tutorial/job-execution-middleware.md), or a [job listener](../tutorial/trigger-and-job-listeners.md) | middleware wraps the execution; a listener observes it |
| `PerformContext` | `IJobExecutionContext` | |
| a `CancellationToken` parameter | the `CancellationToken` parameter of `Execute` | the same token as `IJobExecutionContext.CancellationToken`; `Interrupt` and `InterruptFireInstance` are what cancel it |

### Hosting and configuration

| Hangfire | Quartz.NET |
|---|---|
| `services.AddHangfire(x => x.UseSqlServerStorage(cs))` | `builder.AddQuartz(q => q.UsePersistentStore(s => s.UseSqlServer(cs)))` |
| `services.AddHangfireServer()` | `builder.AddQuartzHostedService()` |
| `BackgroundJobServerOptions.WorkerCount` | `q.UseDefaultThreadPool(maxConcurrency: n)` |
| `BackgroundJobServerOptions.Queues` | — there are no queues; see [below](#queues-become-limits-not-routes) |
| `BackgroundJobServerOptions.ServerTimeout` | `CheckinInterval` and `CheckinMisfireThreshold` on the clustering options — [Tuning the check-in](../tutorial/advanced-enterprise-features.md#tuning-the-check-in) |
| `SchedulePollingInterval` | `IdleWaitTime` — but it is a *ceiling*, not a tick: see [Sub-minute schedules](#sub-minute-schedules-behave-differently) |
| `JobStorage.Current.JobExpirationTimeout` | `ExecutionHistoryOptions.Retention` — a different thing; see [Retention](#job-expiration-is-not-history-retention) |
| `app.UseHangfireDashboard(path, options)` | `app.MapQuartzDashboard()` — and it will not start without an authorization decision |
| `IDashboardAuthorizationFilter` | `RequireAuthorization(policy)` at the map site |
| `DashboardOptions.IsReadOnlyFunc` | `QuartzDashboardOptions.ReadOnly` |
| SQL Server storage creating its own schema | [`ProvisionSchema()`, off by default](../quick-start.md#creating-and-initializing-the-database) |

## The differences that bite

### A cron expression has six fields, and a different default time zone

Hangfire's cron is [Cronos](https://github.com/HangfireIO/Cronos): five fields, or six with a leading
seconds field. Quartz's is [six or seven](../cron-expressions.md), seconds first, and **one of the two
day fields must be `?`** rather than `*` — the two day fields are read as a union, and `?` is how one
of them stands aside. So Hangfire's `0 2 * * *` is Quartz's `0 0 2 * * ?`.

The trap that does not announce itself is the time zone. `RecurringJobOptions.TimeZone`
[defaults to UTC](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/RecurringJobOptions.cs);
a Quartz cron trigger defaults to `TimeZoneInfo.Local`. A schedule moved across without
`InTimeZone(TimeZoneInfo.Utc)` keeps firing — at a different hour, on a server whose zone is not UTC,
and twice or not at all on the two days a year the offset changes. Say the zone explicitly on every
trigger you move.

If the expression has to stay in five-field form, Quartz reads that too:
[`CronFormat.Unix`](../cron-expressions.md#the-unix-five-field-form) is stated rather than sniffed, so
a five-field string in the default format is an error naming the rewritten expression rather than a
silently different schedule.

### Sub-minute schedules behave differently

Hangfire's recurring scheduler enqueues on a poll, and that poll is
[`BackgroundJobServerOptions.SchedulePollingInterval`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/BackgroundJobServerOptions.cs) —
fifteen seconds by default, and the documentation
[describes recurring jobs as minute-based](https://docs.hangfire.io/en/latest/background-methods/performing-recurrent-tasks.html).

Quartz's scheduling thread works the other way round. It asks the store for the triggers due inside the
next `IdleWaitTime` — thirty seconds by default — and then waits until the earliest of *those*, or until
`IdleWaitTime` elapses if there were none, and it is woken early when something changes the schedule.
So `IdleWaitTime` bounds how long the thread sits idle; it does not bound how soon a trigger can fire,
and the seconds field means what it says.

The consequence for a move is worth checking before it surprises you: an expression that was quietly
rounded up to a minute over there starts firing at its stated cadence over here.

### Misfires are decided per trigger, not per job

`MisfireHandlingMode` has three values and lives on the recurring job. Quartz's
[misfire instructions](../tutorial/more-about-triggers.md#misfire-instructions) live on the trigger and
differ by trigger kind, which is more to learn and more to get right. The mapping is close enough to
start from:

| `MisfireHandlingMode` | The nearest `CronTriggerMisfireInstruction` |
|---|---|
| `Relaxed` — one job however many occurrences were missed | `FireAndProceed`, which fires one catch-up and then resumes the schedule |
| `Strict` — one job per missed occurrence | `IgnoreMisfires`, which fires every missed occurrence as fast as the pool allows |
| `Ignorable` — none | `DoNothing`, which skips to the next scheduled occurrence |

`Relaxed` is Hangfire's default, and `SmartPolicy` — Quartz's default — means `FireAndProceed` for a
cron trigger. So a schedule moved across without touching the instruction behaves as it did.

The other half of a misfire is how late counts as late. Hangfire's window is its polling interval;
Quartz's is `MisfireThreshold` on the job store, one minute for the ADO.NET store and five seconds for
the in-memory one.

### `DisableConcurrentExecution` waits; `[DisallowConcurrentExecution]` does not

Hangfire's attribute takes a distributed lock with a timeout, and a worker that cannot get the lock
within it
[throws `DistributedLockTimeoutException`](https://github.com/HangfireIO/Hangfire/blob/v1.8.25/src/Hangfire.Core/DisableConcurrentExecutionAttribute.cs) —
which, with `AutomaticRetry` on by default, becomes a retry. Quartz's attribute has no timeout because
nothing waits: while a firing of that job key is in flight, its other triggers sit in the `Blocked`
state and are released when it completes. Nothing is occupying a worker while it waits, and nothing
fails because the wait was too long.

The scope is the same in the case that matters — with a persistent store the exclusion is cluster-wide,
because it is the store that blocks.

### Queues become limits, not routes

This is the mapping most likely to disappoint, so it is worth being plain. Hangfire's `[Queue]` does two
things: it bounds how much of that work runs, and it decides *which servers* run it, because a server
subscribes to a list of queue names. Quartz's [execution groups](../tutorial/execution-groups.md) do the
first and not the second:

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

    q.UseExecutionLimits(limits =>
    {
        // Two at a time across every node sharing the store, rather than two per process.
        limits.ForGroup("reports", maxConcurrent: 2, ExecutionLimitScope.Cluster);
    });
});
```
<!-- endSnippet -->

What you gain is that the limit can be counted across the whole cluster rather than per process, which
is what `ExecutionLimitScope.Cluster` above says — Hangfire's equivalent is `Hangfire.Throttling` on the
Business tier. What you lose is routing. If a job must run on particular machines, the tools are
[`PreferredNode`](../tutorial/node-affinity.md), which pins a trigger to a named instance, or a second
scheduler with its own store; neither is a queue.

### Retry is opt-in, and it is on the trigger

`AutomaticRetryAttribute` is in `GlobalJobFilters` by default, so in Hangfire every job retries ten
times unless told not to. Quartz retries nothing until a trigger carries a policy:

<!-- snippet: sample_coming_from_hangfire_retry -->
```csharp
// [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 60, 300, 900 })]
services.AddQuartz(q =>
{
    q.AddJob<NightlyImportJob>(j => j.WithIdentity("nightly-import"));
    q.AddTrigger<NightlyImportJob>(t => t
        .ForJob("nightly-import")
        .WithCronSchedule("0 0 2 * * ?")
        // The policy is on the trigger, not on the job type, and nothing is retried
        // without one.
        .WithRetryPolicy(RetryPolicy.Explicit(
            TimeSpan.FromMinutes(1),
            TimeSpan.FromMinutes(5),
            TimeSpan.FromMinutes(15))));
});
```
<!-- endSnippet -->

Two consequences of "on the trigger" rather than "on the job". Two triggers for the same job can retry
differently, which is often what you want — the nightly run and the operator's manual one rarely
deserve the same patience. And a policy is a property of the stored trigger, so it survives a restart
and is visible to every node: a node that dies during a five-minute backoff does not take the retry
with it, which is not true of an in-process wait.

Quartz's waits are exactly what the policy says, with no jitter. Jitter, and a signal when a policy has
given up, are [#3807](https://github.com/quartznet/quartznet/issues/3807) and are scheduled for 4.2.

The rest of what a retry does and does not do — that it never displaces the trigger's own next
occurrence, that running out of attempts is not an error, that it burns no repeat count — is
[Retrying Failed Jobs](retrying-failed-jobs.md).

### Continuations are the weakest mapping

`ContinueJobWith` takes a `JobContinuationOptions` and settles on the parent's final state;
`OnlyOnSucceededState` is the default. Quartz's `JobChainingJobListener` has no equivalent of that: it
triggers the follow-up when the parent completes, **and a parent that threw has completed**. The links
also live in memory with the listener rather than in the store, so they are re-registered on every
start and the follow-up runs on whichever node ran the parent.

If the chain must not run after a failure, check inside the follow-up job, or schedule it from the end
of the parent rather than from a listener. Store-owned continuations that settle on the outcome are
[#3805](https://github.com/quartznet/quartznet/issues/3805), scheduled for 4.2.

### Job expiration is not history retention

`JobStorage.Current.JobExpirationTimeout` is how long Hangfire keeps a *job record* — one day for
succeeded and deleted jobs, while failed ones never expire and stay in the dashboard until somebody
acts on them. Quartz keeps no job record: a trigger's firing leaves a fired-trigger row that is deleted
when the firing settles, and what you see afterwards is the
[execution history](../packages/dashboard.md#execution-history-and-misfires), which is a separate,
in-memory-by-default store holding 2,000 entries per scheduler for 24 hours.

So the two settings are not each other. If you relied on "failed jobs are still in the dashboard next
week", nothing in Quartz does that today; the history store is a recent-activity view, and a durable
record is your logging pipeline's job. The retention and size are
`ExecutionHistoryOptions.Retention` and `MaxEntriesPerScheduler`.

### The dashboard is fail-closed

Hangfire's dashboard is
[readable from localhost by default](https://docs.hangfire.io/en/latest/configuration/using-dashboard.html)
and needs an `IDashboardAuthorizationFilter` for anything else. Quartz's refuses to let the application
start until the mapping says who may reach it — either `RequireAuthorization`, or `AllowAnonymous` if
you mean it:

<!-- snippet: sample_coming_from_hangfire_dashboard -->
```csharp
// app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [ ... ] })
app.MapQuartzDashboard().RequireAuthorization("QuartzOperators");
```
<!-- endSnippet -->

The full model, including read-only mode and the allow-list of job types that may be named through it,
is [Production hardening](../packages/dashboard.md#production-hardening).

### What "once" means

Hangfire's documentation asks for
[re-entrant methods](https://docs.hangfire.io/en/latest/best-practices.html) because an interruption
"can be caused by many different things (i.e. exceptions, server shut-down), and Hangfire will attempt
to retry processing many times". Quartz's default is the other way round: a firing interrupted by a
node dying is **lost**, not repeated, unless the job asks for
[recovery](../tutorial/advanced-enterprise-features.md#asking-for-recovery).

So a job that relied on Hangfire re-running it after a crash needs `RequestRecovery()` here, and a job
written to be safely re-run stays safe either way. Write it to be safely re-run:
[Best Practices](/documentation/best-practices#assume-the-job-will-run-more-than-once) has the shapes,
and the reasons the field agrees on this.

## Running both while you move

Nothing stops the two from sharing a host — they are separate hosted services against separate schemas,
and a `BackgroundJobServer` and a Quartz scheduler know nothing of each other. The one thing worth
deciding early is which of them owns a given schedule, because a recurring job running in both is a
duplicate nobody notices until it matters. Move a schedule by removing it from the one before adding it
to the other, in that order.

## See also

* [Comparison](../comparison.md) — the two weighed against each other, sourced
* [One-Off Job](one-off-job.md) — the `ScheduleJob<TJob, TInput>` overloads in full
* [Retrying Failed Jobs](retrying-failed-jobs.md) — the retry policy and its rules
* [Execution Groups](../tutorial/execution-groups.md) — what `[Queue]` becomes
* [Migration Guide](../migration-guide.md) — for a move from Quartz 3.x rather than from Hangfire
