---
title: Best Practices
---

# Best Practices

These practices apply to **Quartz 3.x and 4.x**; where the two differ, the text says so. The C#
samples are 4.x, and the text gives the 3.x spelling where it differs.

## Designing a job

### Assume the job will run more than once

By default a firing runs **at most once**. If a node dies mid-execution, its fired-trigger row is
cleaned up and that occurrence is lost; the trigger continues from its next scheduled time.
[Requesting recovery](#what-requestsrecovery-re-runs-and-when) changes this to **at least once**.

Work can also run twice in these cases:

- **A node wrongly declared dead.** Its peers release its acquired triggers and re-schedule its
  recovery-requesting executions while it is still running them. See
  [Clocks in a cluster](#clocks-in-a-cluster).
- **Misfire catch-up.** A trigger set to ignore misfires fires every missed occurrence, as fast as
  the pool allows. See [Choosing a misfire instruction](#choosing-a-misfire-instruction-by-its-consequence).
- **A refire.** `JobExecutionException.RefireImmediately` re-runs the same firing on the same worker.
- **Two schedulers that are not one cluster.** The most common cause in practice. See
  [One name per cluster, one id per node](#one-name-per-cluster-one-id-per-node).

Sidekiq, Hangfire and Kubernetes `CronJob` make no exactly-once promise either, and all three ask
for idempotent jobs. Design jobs so that a second run does no harm:

- A job that computes an absolute state and upserts it (a rollup, a cache refresh, a regenerated
  report) needs nothing more.
- A job with an external side effect needs an idempotency key.

<!-- snippet: sample_best_practices_idempotent_job -->
```csharp
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    // Key the occurrence, not the firing: a recovered execution has a new trigger and a new
    // fire instance id.
    string period = context.MergedJobDataMap.GetString("period")!;
    string idempotencyKey = $"{context.JobDetail.Key}:{period}";

    // Record the key and do the work in one transaction, with a unique index on the key.
    await ledger.ChargeOnce(idempotencyKey, period, cancellationToken);
}
```
<!-- endSnippet -->

Rules for the key:

- **Derive it from the occurrence, not from `FireInstanceId` or the trigger.** A recovered execution
  runs on a new trigger in the `RECOVERING_JOBS` group with a new fire instance id, so a key built
  from either would not match the run it repeats. Use `FireInstanceId` to interrupt one execution
  (`IScheduler.InterruptFireInstance`), not to deduplicate.
- **Write the key and the effect in one transaction, with a unique index on the key.** A read
  followed by a write leaves a race between two executions.
- **Keep the keys for as long as work can be replayed.** A recovery run can arrive minutes after the
  original; a manual re-trigger can arrive months after.

On Quartz 3.x the signature is `Task Execute(IJobExecutionContext context)`; the rest is the same.

### What RequestsRecovery re-runs, and when

`RequestRecovery` is off by default. It needs a persistent store: `RAMJobStore` loses its state with
the process.

<!-- snippet: sample_best_practices_request_recovery -->
```csharp
q.AddJob<ChargeInvoicesJob>(j => j
    .WithIdentity("charge-invoices")
    .RequestRecovery());
```
<!-- endSnippet -->

Recovery runs when a scheduler with a persistent store starts, and when a clustered scheduler's
cluster manager decides a peer has stopped checking in. For each fired-trigger row the failed
instance left behind:

- If the job **requests recovery**, the row becomes a new trigger in the `RECOVERING_JOBS` group. It
  fires as soon as the scheduler can run it and carries the original trigger's job data.
- Otherwise the row is deleted and that occurrence is lost.

Recovery re-runs only executions that were interrupted by a process dying or a machine going away.
It is not a retry: a job that threw has completed its firing.

A trigger that was acquired but never fired is not recovered. The fired-trigger row is inserted at
acquisition with no job name and `REQUESTS_RECOVERY` false, and is updated to `EXECUTING` with the
job's real flags only when the trigger fires. Cluster recovery also releases such a reservation back
to `WAITING`, so its own trigger fires it again in the ordinary way.

A recovered execution can tell that it is one:

<!-- snippet: sample_best_practices_recovering -->
```csharp
if (context.Recovering)
{
    TriggerKey original = context.RecoveringTriggerKey!;
    string firstFiredAt = context.MergedJobDataMap.GetString(
        SchedulerConstants.FailedJobOriginalTriggerFireTime)!;

    logger.LogWarning(
        "Recovering work that {Trigger} started at {FirstFiredAt} on a node that did not finish it.",
        original, firstFiredAt);
}
```
<!-- endSnippet -->

`SchedulerConstants.FailedJobOriginalTriggerName` and `…OriginalTriggerGroup` are in the same map.

On both versions, recovery of a `[DisallowConcurrentExecution]` job is deferred on first detection,
for roughly two check-in intervals plus the check-in misfire threshold, because a node that missed
one check-in may still be running the job.

Turning recovery on means preferring a job that ran twice to work that never ran. Reports and
reconciliations usually want it. A job that refreshes something every five minutes usually does
not, because the next firing is soon.

### What happens when a job throws

An exception that escapes `Execute` is caught, logged, wrapped in a `JobExecutionException` and
handed to the trigger, which completes the firing normally. **The job is not re-executed and the
schedule is unchanged**, on both versions, whatever Java Quartz's best-practices page says.

`RefireImmediately` re-runs the same firing on the same worker, at once, and increments
`IJobExecutionContext.RefireCount`. It has no backoff, so an unbounded refire is a tight failure
loop. Bound it:

<!-- snippet: sample_best_practices_bounded_refire -->
```csharp
try
{
    await gateway.Publish(context.JobDetail.Key, cancellationToken);
}
catch (HttpRequestException ex) when (context.RefireCount < 3)
{
    // A refire runs this firing again immediately, on the same worker, with no delay of its own.
    throw new JobExecutionException(ex) { RefireImmediately = true };
}
```
<!-- endSnippet -->

On Quartz 3.x the flag is a constructor argument:
`throw new JobExecutionException(ex, refireImmediately: true)`.

The same exception has two more flags:

- `UnscheduleFiringTrigger` removes the trigger that fired.
- `UnscheduleAllTriggers` removes every trigger for the job.

`RefireImmediately` wins over both.

A job that ends because its cancellation token fired is **not** a failure: the scheduler logs it at
information level and completes the firing.

### Give the trigger a retry policy

**Quartz 4.x puts a retry policy on the trigger; 3.x has none.**

On 4.x a trigger carries a `RetryPolicy`, made only by `Fixed`, `Exponential` or `Explicit`. A job
that throws is re-fired at the stated waits, with nothing else to opt into:

<!-- snippet: sample_best_practices_retry_policy -->
```csharp
services.AddQuartz(q =>
{
    q.AddJob<PublishReportJob>(j => j.WithIdentity("report", "nightly"));
    q.AddTrigger<PublishReportJob>(t => t
        .ForJob("report", "nightly")
        .WithCronSchedule("0 0 2 * * ?")
        // 30s, 1m, 2m, 4m, 8m — never longer than ten minutes, and never past the trigger's
        // own next occurrence.
        .WithRetryPolicy(RetryPolicy.Exponential(
            maxAttempts: 5,
            initialDelay: TimeSpan.FromSeconds(30),
            factor: 2,
            maxDelay: TimeSpan.FromMinutes(10))));
});
```
<!-- endSnippet -->

A retry is a new firing of the same occurrence at a later time:

- It is written to the job store, survives a restart, and every node in the cluster sees it.
- `IJobExecutionContext.RetryAttempt` counts it.
- `ScheduledFireTimeUtc` still reports the scheduled occurrence, not the time the retry ran.

A retry is not `RefireImmediately` with a delay. A refire runs in process, persists nothing and keeps
the execution slot, and the two counters move independently.

Three rules decide whether a policy does anything:

- A retry that would land at, or within a second of, the trigger's next occurrence is dropped. A
  policy whose waits are longer than the gap between occurrences does nothing.
- Running out of attempts is not an error. The trigger returns to its schedule, not to
  `TriggerState.Error`.
- A retry uses up nothing: no `SimpleTrigger` repeat count, no recurrence `COUNT`, no
  `TimesTriggered`.

See [Retrying Failed Jobs](/documentation/quartz-4.x/how-tos/retrying-failed-jobs).

**On 3.x there is only immediate refire and the next scheduled occurrence.** To retry later, do not
`Task.Delay` inside the job, which holds a worker for the whole wait. Either store a one-off trigger
a few minutes ahead and return, or let the next occurrence pick the work up, if the work is defined
by its inputs. [Rescheduling Jobs](/documentation/quartz-4.x/how-tos/rescheduling-jobs#retrying-inside-the-job)
shows both ([3.x](/documentation/quartz-3.x/how-tos/rescheduling-jobs)).

### Keep job data small, string-safe and free of secrets

Put an identifier in the job data and load what it names when the job runs. Job data lives as long
as the trigger, so a stored object goes stale; large values bloat the store; and serialized types
break as code changes. Java Quartz, Hangfire and Sidekiq give the same rule.

- **No credentials, tokens or connection strings.** Job data appears in every backup, in the
  dashboard and in the HTTP API. Get secrets from the container.
- **String-only storage removes the versioning problem.** With it on (`StoreJobDataAsStrings` in
  4.x, the flat key `quartz.jobStore.useProperties` on both), every value must be a string. Turn it
  on at the start of a project, not in the middle.
- **Read `IJobExecutionContext.MergedJobDataMap`**: the job's map with the trigger's laid over it.
  This lets several triggers drive one job with different inputs. In 4.x the scheduler context is
  not part of the merge; read scheduler-wide values from `context.Scheduler.Context`.
- **Spell a job's key once.** Give the job class a `public static readonly JobKey` and use it
  everywhere. The [job template how-to](/documentation/quartz-4.x/how-tos/job-template) does this
  ([3.x](/documentation/quartz-3.x/how-tos/job-template)).

See [Job Data Map (4.x)](/documentation/quartz-4.x/tutorial/job-data-map) and
[More About Jobs (3.x)](/documentation/quartz-3.x/tutorial/more-about-jobs).

## Deciding what may run at the same time

| To limit | Use |
|---|---|
| Overlapping executions of one job key | [`[DisallowConcurrentExecution]`](#disallowconcurrentexecution-bounds-one-job-key) |
| How many of a category of work run at once | [Execution groups](#execution-groups-bound-a-category-of-work) |

Both apply at once; a trigger must satisfy both to be acquired. Neither is a queue: see
[Held-back work misfires](#held-back-work-misfires-it-does-not-queue).

### DisallowConcurrentExecution bounds one job key

The attribute stops two executions of **the same job detail** overlapping. It is not per class: two
jobs of the same class with different keys run concurrently; one job with three triggers does not.

<!-- snippet: sample_best_practices_disallow_concurrent -->
```csharp
[DisallowConcurrentExecution]
public sealed class RebuildSearchIndexJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // One execution of this job key at a time — across the whole cluster, with a persistent store.
        return default;
    }
}
```
<!-- endSnippet -->

With a persistent store this holds **across the cluster**. Trigger acquisition skips a job that
already has a live row in `QRTZ_FIRED_TRIGGERS`, and the fire path checks again under the
cluster-wide `TRIGGER_ACCESS` lock before committing the firing. The job's other triggers are
`BLOCKED` meanwhile. The check and the firing commit together under one database lock, not a
client-side lease.

The guarantee is only as good as the fired-triggers table. A node wrongly declared dead has its rows
deleted while its job is still running. Recovery defers that case on first detection, but a window
remains; see [Clocks in a cluster](#clocks-in-a-cluster). Use the attribute to keep the normal case
orderly, and idempotence to keep the abnormal case correct.

A job with `[PersistJobDataAfterExecution]` should also carry `[DisallowConcurrentExecution]`.
Otherwise two concurrent executions race over which job data is stored.

### Execution groups bound a category of work

An execution group is a tag on a trigger. An execution limit caps how many triggers of that group
run at once. Use it for rules like "reindexing may use at most two workers": the attribute cannot
express that because reindexing is many job keys, and a smaller thread pool would throttle
everything else too.

<!-- snippet: sample_best_practices_execution_limits -->
```csharp
q.AddTrigger<ReindexTenantJob>(t => t
    .WithIdentity("reindex-acme")
    .WithExecutionGroup("reindex")
    .WithCronSchedule("0 0 3 * * ?"));

q.UseExecutionLimits(limits =>
{
    limits.ForGroup("reindex", maxConcurrent: 2);
    limits.ForOtherGroups(maxConcurrent: 8);
});
```
<!-- endSnippet -->

A limit is **per node** by default: three nodes each configured with `2` can run six. That fits
hardware capacity, not a quota. For a quota, Quartz 4.x adds `ExecutionLimitScope.Cluster`, counted
from the fired-triggers table; 3.x has only the per-node form.

The cluster-scoped limit's guarantees and cost are on
[Execution Groups (4.x)](/documentation/quartz-4.x/tutorial/execution-groups) and
[Execution Groups (3.x)](/documentation/quartz-3.x/tutorial/execution-groups).

### Held-back work misfires; it does not queue

A trigger that acquisition skips, because its job is running or its group is at its limit, keeps its
original next fire time. If it is held back past the misfire threshold, it misfires, and the
**trigger's misfire instruction** decides whether that occurrence is skipped, run late, or run
alongside the ones behind it.

For a `[DisallowConcurrentExecution]` job this happens at once: when the running execution
completes, the store applies each unblocked trigger's misfire policy in the same transaction that
unblocks it.

So choose the concurrency limit and the misfire instruction together:

- Occurrences must not be dropped: use an instruction that catches up.
- Occurrences only matter when fresh: use an instruction that skips.

## Choosing a misfire instruction by its consequence

A misfire is a firing that did not run within the misfire threshold of its scheduled time. Causes:
the scheduler was down, the thread pool was full, or the trigger was held back. The mechanics are in
[Troubleshooting](troubleshooting.md#misfire-handling).

| Consequence | Cron, recurrence, calendar-interval, daily-time-interval | Simple |
|---|---|---|
| Run every missed occurrence | `IgnoreMisfires` | `IgnoreMisfires` |
| Run one now, then resume the schedule | `FireAndProceed` | `FireNow` or a `Now…` variant |
| Skip what was missed; resume at the next scheduled time | `DoNothing` | `NextWithExistingCount` or `NextWithRemainingCount` |

`IgnoreMisfires` catches up as fast as the thread pool allows: a trigger firing every fifteen
seconds that was down for five minutes fires twenty times in a row.

The default on every trigger is `SmartPolicy`. It resolves to:

| Trigger | `SmartPolicy` does |
|---|---|
| Cron, recurrence, calendar-interval, daily-time-interval | Fire once now, then resume |
| Simple, one-shot | Fire now |
| Simple, repeats forever | Skip to the next occurrence, keeping the remaining count |
| Simple, finite repeat count | Fire now, keeping the count it has |

To choose, ask whether an occurrence is about a moment or about a backlog:

- A nightly settlement that missed 02:00 should still run, late and once: the default is right.
- An hourly report that missed six hours: `DoNothing` if the report describes "now";
  `IgnoreMisfires` if each report describes its own hour.
- A cache refresh that missed anything should just do the next one.

<!-- snippet: sample_best_practices_misfire_do_nothing -->
```csharp
q.AddTrigger<NightlyRollupJob>(t => t
    .WithIdentity("nightly-rollup")
    .WithCronSchedule("0 0 2 * * ?", x => x
        .InTimeZone(TimeZones.FindById("Europe/Helsinki"))
        .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)));
```
<!-- endSnippet -->

On Quartz 3.x that is `.WithMisfireHandlingInstructionDoNothing()`. 4.x replaced the named methods
with `WithMisfireInstruction`, taking a per-family enum.

Two traps:

- **`IgnoreMisfires` catches up; it does not skip.** It ignores the misfire *policy*: the trigger is
  excluded from misfire handling, stays acquirable however late it is, and fires every missed
  occurrence in turn.
- **The threshold differs by store.** A persistent store treats a firing as misfired at sixty
  seconds late; `RAMJobStore` at five seconds. These are the defaults on both versions. Misfire
  behaviour tested only against the in-memory store has not met the production threshold.

### Do not start a trigger in the past

A trigger whose start time is already past the misfire threshold when it is stored has misfired
before its first firing. Its misfire instruction decides what happens; by default it fires
immediately. This is the most common cause of a schedule firing when nobody expected it.

Two habits cause it:

- Rebuilding a trigger from `GetTriggerBuilder()` keeps the *original* start time, which may be
  months old.
- Re-registering triggers on every deployment with a start time of "now plus a few seconds" ties the
  schedule to each process start instead of the calendar.

With a persistent store, give a repeating trigger a fixed start time and add it only if it is
missing:

<!-- snippet: sample_best_practices_fixed_start_time -->
```csharp
q.AddTrigger<HourlySyncJob>(t => t
    .WithIdentity("hourly-sync")
    .StartAt(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
    .WithSimpleSchedule(s => s
        .WithInterval(TimeSpan.FromHours(1))
        .RepeatForever()));
```
<!-- endSnippet -->

## Daylight saving, clock changes and cluster skew

### Say the schedule in the trigger type that means it

Most daylight saving surprises come from the choice of trigger type.

| You mean | Trigger | Across a daylight saving transition |
|---|---|---|
| Every N seconds, minutes or hours of real time | `SimpleTrigger` | Spacing unchanged; the wall-clock time moves (03:00 becomes 04:00) |
| At this time of day, in this zone | `CronTrigger`, `RecurrenceTrigger` | [The two rules below](#the-two-daylight-saving-rules) |
| Every N calendar days, months or years | `CalendarIntervalTrigger` | Shifts by the transition delta, unless `PreserveHourOfDayAcrossDaylightSavings` is set (off by default) |
| Repeatedly inside a daily window | `DailyTimeIntervalTrigger` | Wall-clock window, so the day's run gets longer or shorter |

Quartz cron states more than most cron dialects: `0 0 0 ? * MON#2` is the second Monday of the month
and `0 0 0 LW 3 ?` the last weekday of March. It cannot state:

- a position counted from the end of a month, other than the last (`#` counts forwards, up to 5);
- a fortnight;
- a cadence its fields cannot divide. `0 0 0 1/3 * ?` restarts on the 1st of each month, so
  31 January is followed by 1 February.

`RecurrenceTrigger` and its RFC 5545 rule state all three, on
[4.x](/documentation/quartz-4.x/tutorial/recurrencetrigger) and
[3.x](/documentation/quartz-3.x/tutorial/recurrencetrigger). If you need several cron triggers, or a
workaround inside an expression, use one.

| Schedule | RFC 5545 rule |
|---|---|
| Second-to-last Friday of the month | `FREQ=MONTHLY;BYDAY=-2FR` |
| Every other Monday | `FREQ=WEEKLY;INTERVAL=2;BYDAY=MO` |
| Every three days, not reset each month | `FREQ=DAILY;INTERVAL=3` |

**`0 0 0 ? * MON/2` means three different things.** It is a textual day-of-week with a step:

| Version | Meaning |
|---|---|
| 3.x | Every other Monday |
| 4.0 | `FormatException` |
| 4.1 and later | Same as `0 0 0 ? * 2/2`: Monday, Wednesday and Friday. 156 fires a year instead of 26, with nothing logged |

Audit for it before upgrading from 3.x to any 4.x. See
[`MON/2` is a step through the week](/documentation/quartz-4.x/cron-expressions#mon-2-is-a-step-through-the-week),
and [Forms the parser refuses](/documentation/quartz-4.x/cron-expressions#forms-the-parser-refuses)
for the six shapes 4.x rejects that 3.x accepted and reinterpreted.

4.x dropped the fortnight because its phase depended on whatever last evaluated the expression: a
misfire, a restart or a failover recomputed it from a different day. `FREQ=WEEKLY;INTERVAL=2;BYDAY=MO`
anchors on the trigger's start time instead.

### The two daylight saving rules

A cron expression is never skipped by a transition, and never fires twice for one scheduled
occurrence. Where it fires depends on the expression and the version:

- A **fixed-time** expression has plain values or comma lists in the second, minute and hour fields:
  `0 30 2 * * ?`, `0 0,30 2 * * ?`.
- An **interval** expression has a wildcard, step or range in one of them: `0 * * * * ?`,
  `0 0/30 * * * ?`.

| Case | 4.x | 3.x |
|---|---|---|
| Fixed-time, the time does not exist (spring forward) | Fires once, at the **end of the gap**: a daily 02:30 over a 02:00–03:00 gap fires at 03:00 | Fires once, shifted forward by the delta, at 03:30 |
| Fixed-time, the time occurs twice (fall back) | Fires once, at the first occurrence | Same as 4.x |
| Interval, the repeated hour (fall back) | Fires through **both** passes | Fires the hour once; "every minute" loses an hour of real time each autumn |
| Interval, the gap (spring forward) | `0 30 * * * ?` fires at 03:00, for the occurrence the gap swallowed, and at 03:30 | Fires once, at the shifted 03:30 |

- Where the delta is not a whole hour (Australia/Lord_Howe, where 02:00 becomes 02:30), a daily
  `0 15 2 * * ?` fires at 02:30 on 4.x and 02:45 on 3.x.
- Only 4.x's gap fire time matches the expression: `IsSatisfiedBy` says yes on 4.x and no on 3.x.

**Name the time zone.** A cron trigger with no zone uses `TimeZoneInfo.Local`: the developer's
machine in development, and often UTC in a container. Look zones up with `TimeZones.FindById` (on
3.x, `TimeZoneUtil`) rather than `TimeZoneInfo.FindSystemTimeZoneById`; it resolves Windows and IANA
identifiers on either platform. See the
[FAQ's daylight saving section](faq.md#daylight-saving-time-and-triggers), and 4.x's
[Time and TimeProvider](/documentation/quartz-4.x/tutorial/time-and-timeprovider) on the clock and
the zone as separate settings.

**Quartz cron puts seconds first.** This causes more "my job ran fifty times" reports than daylight
saving does. `* 0/5 * * * ?` is every second of every fifth minute; every five minutes is
`0 0/5 * * * ?`. A five-field crontab line is not a Quartz expression: prepending `0` fixes the
layout but not the day-of-week numbering, which is 0-6 from Sunday in crontab and 1-7 from Sunday
here.

- **4.x:** `CronExpression.Parse(line, CronFormat.Unix)` and
  `CronScheduleBuilder.Create(line, CronFormat.Unix)` read the five-field form as written,
  renumbering included, and store the canonical Quartz spelling.
- **3.x:** translate by hand, and check the day-of-week digit.

See [Cron Triggers (4.x)](/documentation/quartz-4.x/tutorial/crontriggers) and
[Cron Triggers (3.x)](/documentation/quartz-3.x/tutorial/crontriggers).

### When the clock moves for other reasons

Quartz schedules against the wall clock, which NTP corrections, manual changes and suspended virtual
machines all move. A computed fire time is a point on the calendar, so after a backwards move it
waits for the clock to catch up. Quartz resumes on its own after any change, within one
`IdleWaitTime` (30 seconds by default) for the firing loop, one misfire handler period for misfire
handling, and one check-in interval for cluster check-in; see the
[FAQ](faq.md#system-clock-changes-ntp-corrections-manual-adjustments). To test clock movement, fake
the clock instead of moving the machine's.

### Clocks in a cluster

Cluster nodes compare a timestamp written by one node with the clock of another, so the clocks must
agree.

- Each node writes its check-in time to the scheduler-state table.
- A peer declares that node failed once *the peer's* clock passes that timestamp plus the node's
  check-in interval plus the check-in misfire threshold.
- Both default to 7.5 seconds. A node is written off about fifteen seconds after its last check-in,
  and since it checks in every 7.5 seconds, only the other 7.5 seconds are slack.

**A node whose clock runs more than about seven seconds ahead of a peer's can write off a healthy
peer.** The declaring node then runs cluster recovery against a node that is still working:

- it releases that node's acquired triggers so another node can take them;
- it schedules recovery triggers for its recovery-requesting jobs;
- it deletes its fired-trigger rows, so `[DisallowConcurrentExecution]` stops holding.

The victim logs this line; alert on it:

```text
This scheduler instance (…) is still active but was recovered by another instance in the cluster.
```

**A clock is not the only way to miss a check-in.** With a perfect clock, these miss check-ins too:

- a node pinned at 100% CPU, or a long garbage collection pause;
- a paused virtual machine. Azure can pause virtual machines for up to 30 seconds during
  memory-preserving maintenance, twice the default detection window;
- before 3.22 and 4.1, a refused database connection. One failed check-in backed off the full
  `DbRetryInterval` (15 seconds) and wrote its next row after the peers had stopped trusting it.
  Since then the check-in loop retries inside the window, so a blip shorter than the threshold no
  longer costs the node its row.

Size the margin for the environment's worst *pause*, not its worst clock error.

What to do, in order:

1. **Run time synchronisation on every node**, and give the scheduler enough CPU headroom to check
   in on time. NTP is within a few milliseconds on a LAN; the failures in practice are
   unsynchronised or starved machines.
2. **If you cannot guarantee both, widen the window.** Raise
   `quartz.jobStore.clusterCheckinMisfireThreshold`, or the check-in interval
   (`quartz.jobStore.clusterCheckinInterval`), past your environment's worst pause. The maintainer
   suggests a minute to people who hit this. A dead node's work then waits that much longer to be
   taken over.
3. **Keep the jobs idempotent anyway.** The margin lowers the odds; it does not remove them.

Clustering configuration is on
[Advanced Enterprise Features (4.x)](/documentation/quartz-4.x/tutorial/advanced-enterprise-features)
and [(3.x)](/documentation/quartz-3.x/tutorial/advanced-enterprise-features).

## Sizing the thread pool and the connection pool

### Max concurrency is a permit count, not a thread count

The default thread pool is a semaphore of `MaxConcurrency` permits over the .NET thread pool, not a
set of dedicated threads. Both versions default it to **10**.

| Where | Spelling |
|---|---|
| Flat key, both versions | `quartz.threadPool.threadCount` |
| Code | `UseDefaultThreadPool(maxConcurrency)` |
| 4.x configuration | `ThreadPool:MaxConcurrency` |

- **A job that `await`s I/O** holds its permit but releases the thread. Twenty jobs waiting on HTTP
  calls do not hold twenty threads.
- **A job that blocks** (`.Result`, `.Wait()`, `Thread.Sleep`) holds both. The .NET thread pool
  replaces a blocked thread at roughly one or two per second, so a burst of blocking work slows the
  whole application for minutes at almost no CPU. Do not block.
- **A job that waits** holds a permit, even with `await Task.Delay`. A job waiting an hour for a
  record to appear occupies a slot for an hour. Exit, and let a later firing do the work.

Java Quartz's thread-count advice is about dedicated threads and does not apply to this pool. With a
persistent store, size by database connections instead:
[see below](#the-connection-pool-is-the-thread-pool-plus-three).

### The connection pool is the thread pool plus three

Size the connection pool to at least `MaxConcurrency + 3`. The rule comes from Java Quartz and holds
for Quartz.NET 4.x:

| Consumer | Connections |
|---|---|
| Executing jobs | `MaxConcurrency` |
| Scheduler thread, acquiring and firing triggers | 1 |
| Misfire handler, clustered or not | 1 |
| Cluster manager, when clustering is on | 1 |

A job holds no connection while it runs: the store is used on the scheduler thread before the job
starts, and again when it finishes. But every worker can be in its completion write at the same
instant, so `MaxConcurrency` is the burst ceiling. That makes `+ 3` clustered and `+ 2` otherwise.
Taking the cluster-wide lock does not cost another connection; the row-lock handler uses the
caller's.

<!-- snippet: sample_best_practices_pool_sizing -->
```csharp
services.AddQuartz(q =>
{
    q.UseDefaultThreadPool(maxConcurrency: 20);

    q.UsePersistentStore(s =>
    {
        s.UseSystemTextJsonSerializer();
        s.UseClustering();

        // 20 workers, the scheduler thread, the misfire handler and the cluster manager
        s.UseSqlServer($"{connectionString};Max Pool Size=25");
    });
});
```
<!-- endSnippet -->

**Everything else in the process uses the same pool, and no Quartz setting bounds it:**

- the HTTP API and the dashboard take a connection per in-flight request;
- a job that calls `IScheduler` takes a second one, alongside its own;
- every scoped `DbContext` a job opens takes another.

Size the pool for what the process does, not only for the scheduler.

**Do not answer a pool timeout by raising the pool.** `Microsoft.Data.SqlClient` and Npgsql both
default `Max Pool Size` to 100, and both fail after a fifteen-second wait with a message suggesting
you raise it. A database works best with few active connections: HikariCP's
[pool-sizing guide](https://github.com/brettwooldridge/HikariCP/wiki/About-Pool-Sizing) recommends
about `(core_count × 2) + effective_spindle_count`. That budget is shared by every node: ten
schedulers with a pool of 25 each present 250 connections to one server.

**Use `MaxConcurrency` for admission control.** Set it to the database's connection budget divided
by the number of nodes, and the pool just above it. Queueing at the scheduler is visible, tunable
and governed by your misfire instructions. Queueing inside ADO.NET is invisible and surfaces as a
pool timeout that names the pool, not the cause.

### Batching changes the round trips, not the connections

`MaxBatchSize` (flat key `quartz.scheduler.batchTriggerAcquisitionMaxCount`) defaults to 1. Raising
it acquires several triggers in one round trip. It adds no connections, but:

- A round that asks for more than one trigger takes the cluster-wide trigger-access lock; a
  single-trigger round does not.
- Load across cluster nodes can become uneven: a node that acquires ten triggers holds them until it
  can run them.
- Quartz 4.x refuses a `MaxBatchSize` larger than `MaxConcurrency` at startup, because triggers
  acquired beyond the available workers are held by that node until its pool drains.
- `BatchTriggerAcquisitionFireAheadTimeWindow` must be non-zero for a larger batch to have much
  effect; otherwise a batch holds only triggers that are already due.

### Scheduling many jobs at once

`ScheduleJob` in a loop costs one lock acquisition and one transaction per job. `ScheduleJobs` takes
a dictionary of jobs and their triggers and uses one of each:

<!-- snippet: sample_best_practices_schedule_jobs -->
```csharp
Dictionary<IJobDetail, IReadOnlyCollection<ITrigger>> jobsDictionary = new();
foreach (var data in allData)
{
    var triggerSet = new HashSet<ITrigger>();
    IJobDetail job = JobBuilder.Create<JobName>()
        .UsingJobData("jobData", data.ToString())
        .Build();
    ITrigger trigger = TriggerBuilder.Create()
        .ForJob(job)
        .Build();
    triggerSet.Add(trigger);
    jobsDictionary.Add(job, triggerSet);
}
await scheduler.ScheduleJobs(jobsDictionary, new ScheduleJobOptions { Replace = true });
```
<!-- endSnippet -->

For reads, 4.x's paged, projected queries and the bulk `GetJobDetails(keys)` / `GetTriggers(keys)`
fetch a page of keys in one round trip instead of one per key.

Before scaling this, ask whether you need a trigger per entity. A trigger per row scales to
thousands, but a single trigger that scans for due rows is usually simpler to operate and needs no
migration when the set of entities changes.

## Operating a scheduler

### Shutdown has a deadline

`WaitForJobsToComplete` is off by default, so a shutdown returns while jobs are still running. From
Quartz 4.1, such a shutdown still stops firing before it closes its thread pool, and gives the
executions in flight a couple of seconds to report completion, so a job that was about to finish
leaves nothing for a peer to recover.

With `WaitForJobsToComplete` on, the scheduler waits for the jobs, for as long as the host allows:

<!-- snippet: sample_best_practices_shutdown -->
```csharp
// The scheduler's wait for running jobs is bounded by the host's shutdown budget,
// which is 30 seconds unless you say otherwise.
services.Configure<HostOptions>(options => options.ShutdownTimeout = TimeSpan.FromMinutes(2));

services.AddQuartz(q => q.ConfigureScheduler(options =>
    options.ShutdownJobInterruption = ShutdownJobInterruption.Always));

services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

The hosted service passes the host's shutdown token into the wait. `HostOptions.ShutdownTimeout`
defaults to **30 seconds**.

- **4.x:** the drain observes the token. When the budget runs out, the scheduler logs that it gave up
  waiting, then finishes tearing down the pool, the plugins and the job store.
- **3.x:** the pool's shutdown ignores the token and blocks until the jobs finish, so a long job
  makes the host's stop take as long as the job.

Interrupting is a separate setting from waiting. Both versions default to **not** interrupting
running jobs.

- **4.x:** one setting, `ShutdownJobInterruption`.
- **3.x:** the flat keys `quartz.scheduler.interruptJobsOnShutdown` and
  `…interruptJobsOnShutdownWithWait`.

Interrupting only signals the cancellation token. A job that does not check the token, or pass it to
what it awaits, runs to completion regardless.

Plan for these:

- **The platform's budget must be larger than the application's.** Kubernetes'
  `terminationGracePeriodSeconds` also defaults to 30 seconds, so the two defaults collide. Whatever
  supervises the process (an orchestrator, a Windows service manager, an app-pool recycle) has its
  own budget; fit the application's inside it.
- **Shutdown is terminal.** A shut-down scheduler cannot restart; `Standby()` and `Start()` pause
  and resume. Do not call `Shutdown` yourself when a hosted service owns the scheduler; the host
  calls it.
- **A longer timeout is rarely the fix.** It slows every deployment and does not help when a node is
  evicted or a machine dies. What survives all of those is a job that can be interrupted and
  [run again](#assume-the-job-will-run-more-than-once).

### One name per cluster, one id per node

The most common cause of "my job ran twice" is two schedulers that were never one cluster. Check
all five:

- **Every node of a cluster uses the same scheduler name.** The name makes rows in the database
  belong to one logical scheduler. A name per node makes each node an independent scheduler that
  fires every trigger.
- **Every node has a unique instance id.** `AUTO` generates one. Never share an id between nodes.
- **Clustering is on for every node.** One node with it off breaks the cluster for all of them.
- **The store is persistent.** Two processes with in-memory stores are two schedulers with two
  copies of the schedule, and both fire it.
- **No second, non-clustered scheduler uses the same tables.** Results range from triggers that
  vanish without executing to deadlocks and corrupted state.

Deployment topology counts as configuration. A staging slot, a canary or a second replica set that
runs the same configuration against the same database is a cluster member; if its scheduler name
matches, it takes work.

**Never write to Quartz's tables directly.** The state machine spans several tables, and a
hand-edited row causes the symptoms above. If a manual repair is the last resort,
[Troubleshooting](troubleshooting.md#triggers-stuck-in-acquired-state) has the statements and the
warning that goes with them.

### What the trigger states mean

As read from `QRTZ_TRIGGERS`:

| State | Meaning |
|---|---|
| `WAITING` | Normal. Acquirable when its next fire time arrives. |
| `ACQUIRED` | A node has reserved it to fire. Stuck here: that node stopped between reserving and firing. |
| `BLOCKED` | Another execution of the same `[DisallowConcurrentExecution]` job is running. |
| `PAUSED` / `PAUSED_BLOCKED` | Paused through the API or a group matcher. |
| `ERROR` | The job could not be **built**. |
| `COMPLETE` | Nothing left to fire. |

Two states are often misdiagnosed:

- **A trigger stuck in `BLOCKED`** almost always means a job that never returned: a synchronous call
  that hangs, a deadlock, an unawaited task.
- **`ERROR`** means the job's constructor threw, the container could not resolve it, or the store
  could not read the job detail. A job body that throws does *not* land here; that is an ordinary
  completed firing. Because it is a composition-root failure, an integration test that resolves the
  job usually reproduces it.

Reset a trigger with `IScheduler.ResetTriggerFromErrorState(triggerKey)` on both versions; 4.x adds
an overload taking a set of keys. See
[Recovering triggers that failed](/documentation/quartz-4.x/how-tos/rescheduling-jobs#recovering-triggers-that-failed),
which pages through them rather than assuming one query sees them all.

### Listeners run in the middle of everything

A listener runs on the job's worker or on the scheduler thread, and its cost is added to every
firing it matches. The history plugins are listeners too. Keep listeners short, and prefer
listeners matched to specific jobs over global ones.

Catch exceptions inside a listener:

- A trigger or job listener that throws *before* the job runs stops the job running. The scheduler
  logs "Job will NOT be executed!", tells the scheduler listeners, and completes the firing without
  executing anything.
- One that throws afterwards stops the remaining listeners being notified.

The firing is completed either way, so a throwing listener does not wedge a trigger, but it can
silently cost you the execution.

### Do not let users choose the job type

If an application lets users schedule jobs, the job type must not be one of their choices.
`Quartz.Jobs` ships `NativeJob`, which runs an arbitrary operating-system command, and
`SendMailJob`. A user who can name a job type and its data can run either, which amounts to command
injection. Offer a fixed set of job types and validate their parameters.

The management surfaces need the same care:

- **3.x:** the dashboard has a single authorization policy and a single read-only flag, with no
  per-scheduler policy. If different people should reach different schedulers, enforce that
  outside Quartz.NET.
- **4.x:** `QuartzDashboardOptions` and `QuartzHttpApiOptions` both take a
  `SchedulerAuthorizationPolicy`, evaluated per request against a `SchedulerResource` naming the
  scheduler. One `AuthorizationHandler<TRequirement, SchedulerResource>` holds each caller to its
  own scheduler. What a caller may *do* to that scheduler is still process-wide, through the
  dashboard's read-only flag.

See
[Authorizing a tenant on its own scheduler](quartz-4.x/multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler)
and [Tenancy Patterns](tenancy-patterns.md#what-quartz-net-does-not-give-you).

## What to watch

The failures on this page (a starved pool, a node wrongly declared dead, a group stuck at its
limit) show up as work that did *not* happen. A dashboard that only shows what ran cannot tell that
from a quiet night.

**Traces**, on both versions, come from an `ActivitySource` named `Quartz`:

| Activity | Emitted for |
|---|---|
| `Quartz.Job.Execute` | Each job execution; records a thrown exception |
| `Quartz.Job.Veto` | A vetoed firing |
| `Quartz.JobStore.AcquireNextTriggers`, `.TriggersFired` and the rest | Each job store operation |

The job and trigger name and group, the job type and the fire instance id are attributes. Watch the
store-operation spans: acquisition latency and its exceptions are where a struggling database first
shows.

**Metrics are 4.x only**; Quartz 3.x publishes none. There are eight instruments, on a meter named
`Quartz`. **Every measurement carries `quartz.scheduler.name` and `quartz.scheduler.id`**, so you
can split by node and by scheduler with no instrumentation of your own.

| Instrument | Shows |
|---|---|
| `quartz.job.execution.duration` | Histogram in seconds, tagged `error.type` on failure; its count gives execution and failure counts |
| `quartz.job.execution.active` | Executions in flight; stuck at a ceiling means a starved pool or a saturated group |
| `quartz.trigger.misfire` | Firings that did not happen on time; alert on it for a slipping schedule or a group stuck at its limit |
| `quartz.trigger.acquisition.duration` | How long the scheduling loop waited on the store; a struggling database shows here first |
| `quartz.trigger.acquired` | Triggers acquired |
| `quartz.cluster.checkin.duration` | Cluster check-in time |
| `quartz.cluster.recovery.trigger` | A node's work being taken over |
| `quartz.jobstore.operation.duration` | Store operations, tagged with the operation's name |

The [OpenTelemetry page](quartz-4.x/packages/opentelemetry-integration.md#metrics) lists each
instrument's attributes.

**There is no trigger-state gauge.** Query the store instead: count `QRTZ_TRIGGERS` by
`TRIGGER_STATE`, and alert on `ERROR` and on `BLOCKED` rows older than your longest job. Count
`QRTZ_FIRED_TRIGGERS` to see what the cluster believes is running; in 4.x,
`IScheduler.QueryFireInstances` answers that without SQL.

**The health check** asserts only that the scheduler is in a state that can fire and that the job
store answers a query. It does **not** assert that any trigger is firing, so pair it with an alert
on a job you expect to see regularly.

- **4.x:** in the core package, with no web stack required. Register it explicitly:
  `services.AddHealthChecks().AddQuartz()`, or `q.AddQuartzHealthChecks()` inside a named
  scheduler's `AddQuartz(name, …)` callback. A standby scheduler reports *degraded*.
- **3.x:** in `Quartz.AspNetCore`, registered by `AddQuartzServer()`. It reports healthy or
  unhealthy from `IsStarted` alone, so a scheduler in standby passes.

**Logging is the first diagnostic step.** Misfire handling and every cluster-recovery decision log
at information level when they act; trigger acquisition logs at debug. That makes "no triggers were
acquired" and "someone recovered this node" readable after the fact. Most investigations in this
project's issue tracker end once logging is on. Configure it before you need it.

Setup is on [Observability (4.x)](/documentation/quartz-4.x/packages/opentelemetry-integration) and
[OpenTelemetry Integration (3.x)](/documentation/quartz-3.x/packages/opentelemetry-integration).

## See also

- [Troubleshooting](troubleshooting.md): symptoms, and what to do about each
- [FAQ](faq.md): daylight saving and clock changes in more detail
- [Tenancy Patterns](tenancy-patterns.md): partitioning a scheduler between tenants
- [Configuration Reference (4.x)](/documentation/quartz-4.x/configuration/reference) and
  [(3.x)](/documentation/quartz-3.x/configuration/reference): every setting named here, with its
  default
