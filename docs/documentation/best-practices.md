---
title: Best Practices
---

# Best Practices

A scheduler is easy to start and hard to run. The API is small enough that the first job is working
in an afternoon; what takes longer is deciding how often a job may run, which of two concurrency
mechanisms bounds it, what a missed firing should do, what the clock does to a schedule, how large
the two pools should be, and what to look at when it stops.

This page is those decisions. It is organised by the choice a reader is making rather than by the
type they are calling, and every claim about Quartz.NET's behaviour has been checked against the
code that ships. Where practice in the wider field diverges, the divergence is named rather than
averaged.

It applies to **both Quartz 3.x and Quartz 4.x**. Where the two differ — a name, a default, or a
capability only one of them has — the sentence says so. The C# is 4.x, because that is what the
samples project compiles; where 3.x needs a different spelling, the prose beside the sample gives
it.

## Designing a job

### Assume the job will run more than once

Start from what Quartz.NET actually promises, because it is narrower than the folklore and wider
than the Java page's version of it.

By default a firing is **at most once**. If a node dies mid-execution, the fired-trigger row it left
behind is cleaned up and the occurrence is simply gone; nothing re-runs it, and the trigger carries
on from its next scheduled time. Ask for recovery — the next section — and you have deliberately
bought **at least once** instead.

That is the guarantee. It is not the whole story, because four ordinary situations produce a second
run of work you thought ran once:

- **A node wrongly declared dead.** Its peers release its acquired triggers and, for jobs that
  request recovery, schedule the interrupted executions again — while it is still executing them.
  See [clocks in a cluster](#clocks-in-a-cluster).
- **Misfire catch-up.** A trigger set to ignore misfires fires every occurrence it missed, as fast
  as the pool allows. See [choosing a misfire instruction](#choosing-a-misfire-instruction-by-its-consequence).
- **A refire.** `JobExecutionException.RefireImmediately` re-runs the same firing on the same worker.
- **Two schedulers that are not the cluster you think they are.** By far the most common cause in
  practice, and the subject of [one name per cluster, one id per node](#one-name-per-cluster-one-id-per-node).

The rest of the field does not hedge on this at all. Sidekiq: "Sidekiq will execute your job at
least once, not exactly once. Even a job which has completed can be re-run… Sidekiq makes no
exactly-once guarantee at all." Hangfire: "your background jobs can still be executed several times,
due to re-queue on shutdown and other compensation logic that guarantees the *at least once*
processing." Kubernetes says the same of `CronJob`: "the Jobs that you define should be
*idempotent*." Java Quartz is the outlier, scoping idempotence to jobs marked recoverable, and a
production deployment sees more than that.

So write the job so a second run is uneventful. The order to try, which Microsoft's *Idempotent
Consumer* guidance puts well, is to "design for natural idempotency first, and add deduplication
techniques only for operations that can't be made naturally idempotent". A job that computes an
absolute state and upserts it — a rollup, a cache refresh, a regenerated report — needs nothing
else. A job with an external side effect needs a key.

The key names the **occurrence**, not the firing:

<!-- snippet: sample_best_practices_idempotent_job -->
```csharp
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    // The key names the occurrence, not the firing. A recovered execution arrives on a new trigger
    // with a new fire instance id, so a key derived from either of those would never match the
    // execution it is repeating.
    string period = context.MergedJobDataMap.GetString("period")!;
    string idempotencyKey = $"{context.JobDetail.Key}:{period}";

    // Recording the key and doing the work commit together, and a unique index on the key is what
    // settles a race between two executions rather than a read followed by a write.
    await ledger.ChargeOnce(idempotencyKey, period, cancellationToken);
}
```
<!-- endSnippet -->

Two things about that key are worth being deliberate about.

**Do not derive it from `FireInstanceId` or from the trigger.** A recovered execution arrives on a
newly created trigger in the `RECOVERING_JOBS` group with a fresh fire instance id, so a key built
from either is different on the run that is repeating the work — which is exactly the run the key
exists to catch. `FireInstanceId` is the right identifier for *interrupting* a particular execution
(`IScheduler.InterruptFireInstance`); it is the wrong one for deduplication.

**Write the key and the effect in one transaction, and let a unique index settle the race.** A read
followed by a write is not a check, it is a wider window. Microsoft's guidance is blunt about it:
"Enforce correctness at the data store instead of in application logic… Use a unique constraint on
the deduplication key… This approach makes the database the single arbiter of the race." Keep the
records at least as long as work can be replayed — a recovery run can arrive minutes after the
original, and an operator re-triggering a job by hand can arrive months after it.

On Quartz 3.x the sample's signature is `Task Execute(IJobExecutionContext context)`; everything
else about it is the same.

### What RequestsRecovery re-runs, and when

`RequestRecovery` is off by default, and it only means anything with a persistent store —
`RAMJobStore` loses its state with the process, so there is nothing left to recover from.

<!-- snippet: sample_best_practices_request_recovery -->
```csharp
q.AddJob<ChargeInvoicesJob>(j => j
    .WithIdentity("charge-invoices")
    .RequestRecovery());
```
<!-- endSnippet -->

What it buys is precise. Recovery runs when a scheduler with a persistent store starts, and again
whenever a clustered scheduler's cluster manager decides a peer has stopped checking in. Either way
the store walks the fired-trigger rows the failed instance left behind, and for each one:

- A row whose job **requests recovery** becomes a new trigger in the `RECOVERING_JOBS` group,
  scheduled to fire as soon as the scheduler can run it, carrying the original trigger's job data.
- A row whose job does not is deleted, and that occurrence is lost.

A reservation that never became an execution is not recovered on either path, and the mechanism is
worth knowing because it is what makes that safe. The fired-trigger row is inserted when a trigger is
*acquired*, but the job has not been loaded at that point, so the row goes in with no job name and
`REQUESTS_RECOVERY` false; only when the trigger actually fires is it updated to `EXECUTING` with the
job's real flags. Recovery therefore selects executions and never reservations. Cluster recovery
additionally releases the reservation back to `WAITING`, so its own trigger fires it again in the
ordinary way.

The re-run is not a retry of a *failure*. A job that threw is a completed firing as far as the store
is concerned; recovery is only ever about executions that were interrupted, by a process dying or a
machine going away.

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
Note that recovery of a `[DisallowConcurrentExecution]` job is deliberately deferred on first
detection — for roughly two check-in intervals plus the check-in misfire threshold — because a node
that has missed one check-in may still be running the job. Both versions do this.

Turning recovery on is a decision about which failure you prefer: a job that ran twice, or work that
never ran at all. Reports and reconciliations usually want recovery; a job that merely refreshes
something on a five-minute schedule usually does not, because the next firing is along shortly.

### What happens when a job throws

An exception that escapes `Execute` is caught, logged, wrapped in a `JobExecutionException` and
handed to the trigger, which completes the firing normally. **The job is not re-executed and the
schedule is not disturbed.** Java Quartz's best-practices page says the opposite — "Quartz will
typically immediately re-execute it" — and that is not what this code does, on either version.

You can ask for a re-execution, and it is worth knowing exactly what it is: a loop on the same
worker, running the same firing again with no delay of any kind, incrementing
`IJobExecutionContext.RefireCount`. There is no backoff, so an unbounded refire is a tight failure
loop against whatever just failed. Bound it:

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

On Quartz 3.x the flag is a constructor argument rather than an init-only property:
`throw new JobExecutionException(ex, refireImmediately: true)`.

This is where Quartz differs most sharply from its neighbours, and the difference is worth stating
rather than papering over. Hangfire applies an `AutomaticRetryAttribute` to every job by default —
ten attempts with an increasing, jittered delay spanning about three hours, then a durable `Failed`
state with a dashboard to inspect and re-queue it. Sidekiq does much the same. **Quartz.NET has no
retry policy at all**: there is immediate refire, and there is the next scheduled occurrence, and
nothing in between. So a job that wants delayed retry has to build it, and the way to build it is
not a `Task.Delay` inside the job — that holds a worker for the whole backoff. Store a one-off
trigger for a few minutes' time and return, or let the next scheduled occurrence pick the work back
up because the work is described by its inputs rather than by having been attempted.
[Rescheduling Jobs](/documentation/quartz-4.x/how-tos/rescheduling-jobs#retrying-inside-the-job) has
both shapes written out ([3.x](/documentation/quartz-3.x/how-tos/rescheduling-jobs)).

Two more instructions are available on the same exception, and both are drastic:
`UnscheduleFiringTrigger` removes the trigger that fired, and `UnscheduleAllTriggers` removes every
trigger for the job. `RefireImmediately` wins over both if set.

A job that unwinds because its cancellation token fired is **not** treated as a failure: the
scheduler logs it at information level and completes the firing. That is what makes cooperative
cancellation safe to use.

### Keep job data small, string-safe and free of secrets

The three systems in this survey agree on the rule and give three different reasons, all of which
apply here. Java Quartz says primitives only "to avoid data serialization issues short and
long-term" — a versioning argument. Hangfire adds that large arguments "can blow up your job
storage", and that "background jobs may be processed days or weeks after they were enqueued… it may
become stale". Sidekiq's version is the sharpest: "what happens if your queue backs up and that
quote object changes in the meantime? Don't save state to Sidekiq, save simple identifiers."

For a scheduler the staleness argument is the strongest of the three, because a trigger's job data
is not held for days but for as long as the trigger exists. Put an identifier in the map and read
the thing it names when the job runs.

Two more rules follow from job data being durable. It appears in every backup, in the dashboard and
in the HTTP API, so **no credentials, no tokens, no connection strings** — those come from the
container. And if you turn on string-mode storage (`StoreJobDataAsStrings` in 4.x, the flat key
`quartz.jobStore.useProperties` on both), every value must be a string, which removes the versioning
problem entirely; turn it on at the start of a project, not in the middle.

Read job data from `IJobExecutionContext.MergedJobDataMap`, which is the job's map with the
trigger's laid over it, rather than from either one directly — that is what lets several triggers
drive one job with different inputs. In 4.x the scheduler context is no longer folded into that
merge; read scheduler-wide values from `context.Scheduler.Context`.

The full treatment is on
[Job Data Map (4.x)](/documentation/quartz-4.x/tutorial/job-data-map) and
[More About Jobs (3.x)](/documentation/quartz-3.x/tutorial/more-about-jobs).

Finally, a small convention that pays for itself: give a job class a `public static readonly JobKey`
and use it everywhere the key is needed, so the key is spelled once. The
[job template how-to](/documentation/quartz-4.x/how-tos/job-template) does this throughout
([3.x](/documentation/quartz-3.x/how-tos/job-template)).

## Deciding what may run at the same time

Quartz.NET has two mechanisms, and they answer different questions. Reaching for the wrong one is a
common source of both surprise concurrency and surprise serialisation.

### DisallowConcurrentExecution bounds one job key

The attribute stops two executions of **the same job detail** overlapping. Java's tutorial chose its
words carefully and they are worth repeating: "The constraint is based upon an instance definition
(JobDetail), not on instances of the job class." Two jobs of the same class with different keys run
concurrently; one job with three triggers does not.

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

With a persistent store this holds **across the cluster**, not merely within a process. Trigger
acquisition skips a job that already has a live row in `QRTZ_FIRED_TRIGGERS`, and the fire path
checks again under the cluster-wide `TRIGGER_ACCESS` lock before committing the firing; the job's
other triggers are moved to `BLOCKED` for the duration.

It is worth being honest about where that ends. The ledger is the fired-triggers table, so the
constraint is exactly as good as that table's account of what is running — and a node that is
wrongly declared dead has its rows deleted while its job is still executing. That case is deferred
on first detection precisely because it is the dangerous one, but the residual window is real, which
is why [clocks in a cluster](#clocks-in-a-cluster) is not an optional section.

The neighbours are more pessimistic about features of this shape. Hangfire says of its own
`DisableConcurrentExecution` that "there's no reliable way to prevent multiple executions of the
same background job other than by using transactions in background job method itself", and that the
filter "may help a bit by narrowing the safety violation surface". Sidekiq declines to ship one at
all: "Sidekiq will not provide features which hack around a lack of concurrency in your jobs."
Quartz's version is stronger than a lease, because the check and the firing commit together under
one database lock rather than depending on a client-side timer — but it is a failure-detection
system underneath, and a failure detector can be wrong. Treat it as the thing that keeps the normal
case orderly, and idempotence as the thing that keeps the abnormal case correct.

If a job also carries `[PersistJobDataAfterExecution]`, it should carry this attribute too. Java's
tutorial: "you should strongly consider also using the `@DisallowConcurrentExecution` annotation, in
order to avoid possible confusion (race conditions) of what data was left stored when two instances
of the same job (JobDetail) executed concurrently."

### Execution groups bound a category of work

An execution group is a tag on a trigger, and an execution limit caps how many triggers of that
group may be running at once. This is the mechanism for "reindexing must never take more than two
of my workers", which the attribute cannot express because reindexing is many job keys, and which a
smaller thread pool cannot express because it would throttle everything else too.

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

A limit is **per node** by default, so three nodes each configured `2` can be running six. That is
the right answer for hardware capacity and the wrong one for a quota. Quartz 4.x adds
`ExecutionLimitScope.Cluster`, counted from the fired-triggers table, for the quota case; 3.x has
only the per-node form. This is the same trap Celery documents for its own rate limit — "this is a
*per worker instance* rate limit, and not a global rate limit" — and the one Hangfire only escapes
in a paid add-on.

Both mechanisms apply at once; a trigger has to satisfy both to be acquired. The full treatment,
including the cluster-scoped ceiling's guarantees and its cost, is on
[Execution Groups (4.x)](/documentation/quartz-4.x/tutorial/execution-groups) and
[Execution Groups (3.x)](/documentation/quartz-3.x/tutorial/execution-groups).

### Held-back work misfires; it does not queue

Neither mechanism is a queue. A trigger that acquisition skips — because its job is already running,
or because its group is at its ceiling — stays where it is, keeping its original next fire time.
If it is held back for longer than the misfire threshold, the ordinary misfire machinery claims it,
and the **trigger's misfire instruction**, not the concurrency setting, decides whether that
occurrence is skipped, run late, or run alongside the ones behind it.

For a `[DisallowConcurrentExecution]` job this is immediate rather than eventual: when the running
execution completes, the store applies each unblocked trigger's misfire policy on the spot, in the
same transaction that unblocks it.

The practical consequence is that limiting concurrency and choosing a misfire instruction are one
decision. A tightly capped group whose occurrences must not be dropped wants an instruction that
catches up; one whose occurrences are only meaningful when fresh wants an instruction that skips.

## Choosing a misfire instruction by its consequence

A misfire is a firing whose scheduled time has passed by more than the misfire threshold without the
job having run — because the scheduler was down, the pool was full, or the trigger was held back.
[Troubleshooting](troubleshooting.md#misfire-handling) has the mechanics and the table of names in
both versions' spellings; this section is only about which one to pick.

There are three possible consequences, whatever the family calls them:

1. **Run every occurrence that was missed.** `IgnoreMisfires`. The trigger catches up as fast as the
   thread pool allows: a trigger firing every fifteen seconds that was down for five minutes fires
   twenty times in a row.
2. **Run one occurrence now, then resume the schedule.** `FireAndProceed` for cron, recurrence,
   calendar-interval and daily-time-interval triggers; `FireNow` or one of the `Now…` variants for
   simple triggers.
3. **Skip what was missed and resume at the next scheduled time.** `DoNothing` for cron, recurrence,
   calendar-interval and daily-time-interval triggers; `NextWithExistingCount` or
   `NextWithRemainingCount` for simple triggers.

The default on every trigger is `SmartPolicy`, and what that resolves to is worth knowing rather
than assuming. For cron, recurrence, calendar-interval and daily-time-interval triggers it is always
**consequence 2** — fire once now, then resume. For a simple trigger it depends on the repeat count:
a one-shot trigger fires now; a trigger that repeats forever skips to its next occurrence keeping
its remaining count; a trigger with a finite repeat count fires now and keeps the count it has.

So the decision is one question about the work: **is an occurrence about a moment, or about a
backlog?** A nightly settlement run that missed 02:00 should still happen — late, once — so the
default is right. An hourly report that missed six hours should produce one report, not six, so
`DoNothing` is right if the report describes "now" and `IgnoreMisfires` is right if each report
describes its own hour. A cache refresh that missed anything at all should just do the next one.

<!-- snippet: sample_best_practices_misfire_do_nothing -->
```csharp
q.AddTrigger<NightlyRollupJob>(t => t
    .WithIdentity("nightly-rollup")
    .WithCronSchedule("0 0 2 * * ?", x => x
        .InTimeZone(TimeZones.FindById("Europe/Helsinki"))
        .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing)));
```
<!-- endSnippet -->

On Quartz 3.x that reads `.WithMisfireHandlingInstructionDoNothing()`; 4.x replaced the family of
named methods with `WithMisfireInstruction` taking a per-family enum.

Two traps are worth naming explicitly.

**`IgnoreMisfires` does not mean "ignore the missed firings".** It means ignore the misfire
*policy*: the trigger is excluded from misfire handling entirely and stays acquirable however stale
it has become, so every missed occurrence is fired in turn, as quickly as the pool can take them.
It is the catch-up instruction, not the skip instruction. This misreading is common enough in this
project's issue history to be worth a sentence of its own.

**The threshold is not the same in both stores.** A persistent store treats a firing as misfired
sixty seconds late; `RAMJobStore` does so after five seconds. Both versions, both defaults. A
schedule whose misfire behaviour was only ever exercised against the in-memory store in tests has
not been tested against the thresholds it will meet in production.

### Do not start a trigger in the past

A trigger whose start time is already behind the misfire threshold when it is stored has misfired
before it has ever fired, and the instruction you chose above decides what happens — which, on the
default, is "fire immediately". This is the single most common way a schedule fires when nobody
expected it to.

Two habits cause it. Rebuilding a trigger from `GetTriggerBuilder()` carries the *original* start
time forward, which may be months old. And re-registering triggers on every deployment with a start
time of "now plus a few seconds" makes the schedule relative to each process start rather than to
the calendar.

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

Most daylight-saving surprises are really a trigger-type choice made without noticing, because the
families answer "what time is it" in genuinely different ways.

| You mean | Trigger | What a daylight saving transition does to it |
|---|---|---|
| Every N seconds, minutes or hours of real time | `SimpleTrigger` | Nothing. The interval is absolute, so the *name* of the fire time moves — 03:00 becomes 04:00 — while the spacing does not. |
| At this time of day, in this zone | `CronTrigger`, `RecurrenceTrigger` | The two rules below. |
| Every N calendar days, months or years | `CalendarIntervalTrigger` | The instant shifts by the transition delta unless `PreserveHourOfDayAcrossDaylightSavings` is set, which is off by default. |
| Repeatedly inside a daily window | `DailyTimeIntervalTrigger` | The window is wall-clock, so a transition lengthens or shortens the day's run. |

Quartz cron says more than most cron dialects, so check it before assuming a pattern needs something
else: `0 0 0 ? * MON#2` is the second Monday of the month, `0 0 0 LW 3 ?` the last weekday of March,
and `0 0 0 ? * MON/2` every other Monday, holding its fortnightly cadence across month and year
boundaries. What it genuinely cannot state is a position counted from the *end* of a month other
than the last one — `#` counts forwards, and only as far as 5 — and any cadence its fields cannot
divide: `0 0 0 1/3 * ?` reads like "every third day" but restarts at the 1st of each month, so 31
January is followed by 1 February. `RecurrenceTrigger` and its RFC 5545 rule state both,
`FREQ=MONTHLY;BYDAY=-2FR` for the second-to-last Friday and `FREQ=DAILY;INTERVAL=3` for a three-day
cadence that does not reset, on [4.x](/documentation/quartz-4.x/tutorial/recurrencetrigger) and
[3.x](/documentation/quartz-3.x/tutorial/recurrencetrigger). Reaching for several cron triggers, or
for a cron expression with a workaround in it, is usually the sign.

### The two daylight saving rules

Java Quartz's page warns that a cron trigger may fire twice or not at all across a transition. That
is not what this implementation does, and the difference matters enough to state precisely. For a
**fixed-time** expression such as `0 30 2 * * ?`, on both versions:

- A wall-clock time that **does not exist** on a spring-forward day fires exactly **once**, shifted
  forward by the transition delta. A daily 02:30 over a 02:00–03:00 gap fires at 03:30. It is not
  skipped.
- A wall-clock time that **occurs twice** on a fall-back day fires **once**, at the first of the two
  occurrences.

**Quartz 4.x only:** an *interval* expression — one with a wildcard, step or range in the second,
minute or hour field, such as `0 * * * * ?` or `0 0/30 * * * ?` — fires through **both** passes of
the repeated hour. On 3.x the repeated hour is fired once, which means an "every minute" schedule
silently loses an hour of real time each autumn. Fixed-time expressions, including comma lists like
`0 0,30 2 * * ?`, are unchanged between the versions.

Whatever the family, **name the time zone**. A cron trigger with no zone uses `TimeZoneInfo.Local`,
which is the developer's machine in development and very often UTC in a container, so the schedule
means two different things in the two places. `TimeZones.FindById` is the lookup to use rather than
`TimeZoneInfo.FindSystemTimeZoneById`, because it resolves Windows and IANA identifiers on either
platform (on 3.x the same type is called `TimeZoneUtil`). The
[FAQ's daylight saving section](faq.md#daylight-saving-time-and-triggers) has the longer treatment,
and 4.x's [Time and TimeProvider](/documentation/quartz-4.x/tutorial/time-and-timeprovider) covers
how the clock and the zone are two separate axes.

One more cron trap, since it accounts for more "my job ran fifty times" reports in this project than
daylight saving does: Quartz cron puts **seconds first**, so `* 0/5 * * * ?` means *every second of
every fifth minute*, not every five minutes. That is `0 0/5 * * * ?`. See
[Cron Triggers (4.x)](/documentation/quartz-4.x/tutorial/crontriggers) and
[Cron Triggers (3.x)](/documentation/quartz-3.x/tutorial/crontriggers).

### When the clock moves for other reasons

An NTP correction, a manual change or a suspended virtual machine moves the wall clock in either
direction, and Quartz schedules against the wall clock. Moving it backwards means a trigger whose
next fire time was already computed waits for the clock to catch up; that is correct, because a fire
time is a point on the calendar rather than an offset from now.

What matters operationally is that recovery is bounded rather than instantaneous: the firing loop
re-evaluates within one `IdleWaitTime` (30 seconds by default), misfire handling within one misfire
handler period, and cluster check-in within one check-in interval. The
[FAQ's section on clock changes](faq.md#system-clock-changes-ntp-corrections-manual-adjustments)
has the detail. To test clock movement, fake the clock rather than moving the machine's.

### Clocks in a cluster

Clustered scheduling compares a timestamp written by one node against the clock of another, so the
clocks have to agree. Concretely: each node writes its check-in time to the scheduler-state table,
and a peer decides that node has failed once *its own* clock passes that timestamp plus the failed
node's check-in interval plus the check-in misfire threshold. Both of those default to 7.5 seconds,
so a node is written off about fifteen seconds after the timestamp it last wrote — and since it
writes one every 7.5 seconds, only the other 7.5 is slack. **A node whose clock runs more than about
seven seconds ahead of a peer's can write off a healthy peer.** Java Quartz states the requirement as
a precondition for running clustered at all: "the clocks must be within a second of each other."

What follows from a false declaration is not subtle. The live node runs cluster recovery against a
node that is still working: it releases that node's acquired triggers so another node can take them,
schedules recovery triggers for its recovery-requesting jobs, and deletes its fired-trigger rows —
which is also what makes `[DisallowConcurrentExecution]` stop holding. The victim eventually logs a
line worth alerting on:

```text
This scheduler instance (…) is still active but was recovered by another instance in the cluster.
```

The important part is that **a clock is not the only way to miss a check-in**. A node pinned at 100%
CPU, a long garbage-collection pause, or a paused virtual machine misses check-ins with a perfect
clock — and Azure documents its virtual machines being paused "for up to 30 seconds" during
memory-preserving maintenance, which is twice the default detection window. This is the standard
distributed-systems caution rather than a Quartz quirk; the literature on leases is unanimous that a
missed heartbeat is a decision to act as if a node were dead, never evidence that it is, and that
the safety margin has to cover the environment's worst *pause* rather than its worst clock error.

Three things to do about it, in order:

1. **Run a time-synchronisation service on every node**, and give the scheduler enough CPU headroom
   that it can always take its check-in turn. Ordinary NTP is within a few milliseconds on a LAN,
   which is far inside the requirement; the failures in practice are unsynchronised machines and
   starved ones, not inaccurate ones.
2. **If you cannot guarantee either, widen the window.** Raising
   `quartz.jobStore.clusterCheckinMisfireThreshold`, or the check-in interval itself
   (`quartz.jobStore.clusterCheckinInterval`), past your environment's worst pause — a minute is the
   number this project's maintainer has suggested to people who hit this — stops the false failovers,
   at the cost of a genuinely dead node's work waiting that much longer to be taken over.
3. **Keep the jobs idempotent anyway**, because the margin is a probability rather than a proof.

Clustering configuration in full is on
[Advanced Enterprise Features (4.x)](/documentation/quartz-4.x/tutorial/advanced-enterprise-features)
and [(3.x)](/documentation/quartz-3.x/tutorial/advanced-enterprise-features).

## Sizing the thread pool and the connection pool

### Max concurrency is a permit count, not a thread count

The default thread pool is a semaphore of `MaxConcurrency` permits over the .NET thread pool, not a
set of dedicated threads. Both versions default it to **10**, and both spell it
`quartz.threadPool.threadCount` as a flat key (`UseDefaultThreadPool(maxConcurrency)` in code,
`ThreadPool:MaxConcurrency` in 4.x configuration).

That distinction changes the advice inherited from Java. A job that `await`s I/O holds its permit
but releases the thread, so a scheduler running twenty jobs that are all waiting on HTTP calls is
not holding twenty threads. A job that *blocks* — `.Result`, `.Wait()`, `Thread.Sleep` — holds both,
and the .NET thread pool replaces a blocked thread at roughly one or two per second, so a burst of
blocking work degrades the whole application for minutes at almost no CPU. Java Quartz's warning
that "performance starts to tank as you get into the several hundreds of threads" was written about
dedicated threads and does not describe this pool; the .NET rule is simply not to block.

What Java's advice does still get right is the shape of a job that waits. "If you feel the need to
call `Thread.sleep()` on the worker thread executing the Job, it is typically a sign that the job is
not ready to do the rest of its work because it needs to wait for some condition… A better solution
is to release the worker thread (exit the job) and allow other jobs to execute on that thread." That
holds for `await Task.Delay` too, because the permit is what is scarce: a job waiting an hour for a
record to appear occupies a slot for an hour. Exit and let a later firing do the work.

Nobody in the field agrees on a number, and the disagreement is informative. Java Quartz refuses to
default it at all and says "if you only have a few jobs that fire a few times a day, then 1 thread is
plenty", rising to "more like 50 or 100" for tens of thousands of jobs. Hangfire derives it,
`Environment.ProcessorCount * 5`, capped at 20. Neither formula transfers, because for a scheduler
with a persistent store the number that binds is not threads at all — it is connections.

### The connection pool is the thread pool plus three

The old advice — "at least the number of worker threads in the thread pool plus three" — is
inherited from Java Quartz and still comes out right for Quartz.NET 4.x. The arithmetic, from the
code:

- **One per executing job.** A job holds no connection while it runs; the store is touched before
  the job starts, on the scheduler thread, and again when it finishes. But every worker can be
  inside its completion write at the same instant, so `MaxConcurrency` is the burst ceiling.
- **One for the scheduler thread**, acquiring triggers and firing them.
- **One for the misfire handler**, which runs on its own loop whether or not the scheduler is
  clustered.
- **One for the cluster manager**, when clustering is on.

So `MaxConcurrency + 3` clustered and `+ 2` otherwise; the round number covers both. Taking the
cluster-wide lock does not cost a second connection — the row-lock semaphore is handed the caller's
connection rather than opening one of its own.

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

The sentence beside that arithmetic on the old page does more work than the arithmetic does, and
deserves promoting: **everything else in the process draws on the same pool, and none of it is
bounded by a Quartz setting.** The HTTP API and the dashboard each take a connection per in-flight
request. A job that calls `IScheduler` takes a second one, concurrently with its own. Every scoped
`DbContext` a job opens is another. Size for what the process does, not for what the scheduler does.

It is also worth knowing which direction the defaults push. `Microsoft.Data.SqlClient` and Npgsql
both default `Max Pool Size` to 100 and both fail, after a fifteen-second wait, with a message
suggesting you raise it. That is usually the wrong response, because the database's own optimum is
small — the widely cited HikariCP treatment of pool sizing recommends roughly
`(core_count × 2) + effective_spindle_count` *active* connections and argues for "a small pool,
saturated with threads waiting for connections", citing an Oracle demonstration where shrinking the
pool alone took response times "from ~100ms to ~2ms". That budget belongs to the database and is
shared by every node: ten schedulers with a modest pool of 25 each present 250 connections to one
server.

Which makes `MaxConcurrency` the admission-control knob. Derive it from the database's connection
budget divided by the number of nodes, then set the pool just above it. Queueing at the scheduler is
visible, tunable, and subject to misfire instructions you chose; queueing inside ADO.NET is
invisible and surfaces as a pool timeout that names the pool rather than the cause.

### Batching changes the round trips, not the connections

`MaxBatchSize` — `quartz.scheduler.batchTriggerAcquisitionMaxCount` as a flat key — defaults to 1,
and raising it acquires several triggers in a single round trip rather than one at a time. It does
not add connections. What it does change is locking: a round that asks for more than one trigger takes the
cluster-wide trigger-access lock, which a single-trigger round does not. Java Quartz's warning
applies to Quartz.NET too — the larger number comes "at the cost of possible imbalanced load between
cluster nodes", because a node that acquires ten triggers has made them its own until it can run
them.

Quartz 4.x refuses a `MaxBatchSize` larger than `MaxConcurrency` at startup, with a message that
explains why: triggers acquired beyond the number of workers available to run them are held by that
node until the pool drains. Widening the batch also needs
`BatchTriggerAcquisitionFireAheadTimeWindow` to be non-zero to have much effect, since a batch can
otherwise only contain triggers that are already due.

### Scheduling many jobs at once

Calling `ScheduleJob` in a loop costs a lock acquisition and a transaction per job. `ScheduleJobs`
takes a dictionary of jobs and their triggers and does all of it inside one of each:

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

The same instinct applies on the read side: 4.x's paged, projected queries and bulk
`GetJobDetails(keys)` / `GetTriggers(keys)` turn a page of keys into one round trip, where fetching
each key in turn is one round trip each. Java Quartz added the same advice to its own page for 2.5.

Before scaling any of this, though, ask whether the schedule needs one trigger per entity at all. A
trigger per row scales to thousands without difficulty, but a single trigger that scans for the rows
that are due is usually simpler to operate, and it does not need a migration when the set of
entities changes.

## Operating a scheduler

### Shutdown has a deadline

`WaitForJobsToComplete` is off by default, which means a shutdown returns while jobs are still
running. Turning it on makes the scheduler wait — but not indefinitely, and the bound is not a
Quartz setting:

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

The hosted service passes the host's shutdown token down into the wait, and
`HostOptions.ShutdownTimeout` defaults to **30 seconds**. In Quartz 4.x the drain observes that
token: when the budget runs out the scheduler logs that it gave up waiting, and then finishes
tearing down the pool, the plugins and the job store rather than abandoning them half-done. On
Quartz 3.x the pool's shutdown ignores the token and blocks until the jobs finish, so a long job
makes the whole host's stop take as long as the job does.

Interrupting is a separate decision from waiting. Both versions default to **not** interrupting
running jobs; 4.x says which case you want in one setting, `ShutdownJobInterruption`, while 3.x has
the two flat keys `quartz.scheduler.interruptJobsOnShutdown` and
`…interruptJobsOnShutdownWithWait`. Either way it only signals a cancellation token: a job that does
not check the token, or forward it to what it awaits, runs to completion regardless.

Three things worth planning around:

- **The platform's budget has to be larger than the application's.** Kubernetes'
  `terminationGracePeriodSeconds` also defaults to 30 seconds, so two defaults that both look
  generous collide exactly. Whatever supervises the process — an orchestrator, a Windows service
  manager, an app-pool recycle — has a budget of its own, and the application's should fit inside it.
- **Shutdown is terminal.** A shut-down scheduler cannot be restarted; `Standby()` and `Start()` are
  the pause-and-resume pair. Do not call `Shutdown` yourself when a hosted service owns the
  scheduler — the host calls it.
- **Extending the timeout is rarely the real fix.** Waiting longer makes every deployment slower and
  still does not help when a node is evicted or a machine dies. The guarantee that survives all of
  those is the one from the first section: a job that can be interrupted and run again.

### One name per cluster, one id per node

The largest single category of "my job ran twice" in this project's history is not a scheduling bug.
It is two schedulers that were never one cluster. Five rules cover almost all of it:

- **Every node of a cluster uses the same scheduler name.** The name is what makes rows in the
  database belong to the same logical scheduler. Giving each node its own name — a natural-looking
  thing to do — makes each node an independent scheduler that fires every trigger.
- **Every node has a unique instance id.** `AUTO` generates one; a shared id is as bad as a shared
  name is good.
- **Clustering is on for every node.** One node with it off is enough to break the cluster for all
  of them.
- **The store is persistent.** Two processes with in-memory stores are two schedulers with two
  copies of the schedule, and both will fire it.
- **Never point a second, non-clustered scheduler at the same tables.** This is Java Quartz's own
  warning and it still holds: the outcomes range from triggers that vanish without executing to
  deadlocks and corrupted state.

Deployment topology counts as configuration here. A staging slot, a canary, or a second replica set
running the same configuration against the same database is a cluster member you did not intend to
have — and if its scheduler name matches, it will take work.

The corollary rule from Java's page is also still true: **never write to Quartz's tables directly.**
The state machine spans several tables, and a hand-edited row produces exactly the symptoms above.
When a manual repair really is the last resort,
[Troubleshooting](troubleshooting.md#triggers-stuck-in-acquired-state) has the statements and the
warning that goes with them.

### What the trigger states mean

Operators read these out of `QRTZ_TRIGGERS` more often than any API, and two of them are routinely
misdiagnosed:

| State | What it means |
|---|---|
| `WAITING` | Normal. Eligible to be acquired when its next fire time arrives. |
| `ACQUIRED` | A node has reserved it and is about to fire it. Rows that stay here belong to a node that stopped between reserving and firing. |
| `BLOCKED` | Another execution of the same `[DisallowConcurrentExecution]` job is running. **A trigger stuck here almost always means a job that never returned** — a synchronous call that hangs, a deadlock, an unawaited task. |
| `PAUSED` / `PAUSED_BLOCKED` | Paused explicitly, through the API or a group matcher. |
| `ERROR` | The job could not be **built**. Its constructor threw, the container could not resolve it, or the store could not read the job detail. A job body that throws does *not* land here — that is an ordinary completed firing. |
| `COMPLETE` | Nothing left to fire. |

`ERROR` catches people out because it looks like an execution failure and is not: it is a
composition-root failure, which is why it is usually reproducible from an integration test that
resolves the job. Recovery is `IScheduler.ResetTriggerFromErrorState(triggerKey)` on both versions,
and 4.x adds an overload taking a whole set of keys — see
[Recovering triggers that failed](/documentation/quartz-4.x/how-tos/rescheduling-jobs#recovering-triggers-that-failed),
which pages through them rather than assuming one query sees the lot.

### Listeners run in the middle of everything

A listener is not an observer running to one side. It runs on the same worker as the job, or on the
scheduler thread, and its cost is added to every firing it matches — which is also true of the
history plugins, since those are listeners. Java's advice stands: keep them short, and prefer
listeners matched to specific jobs over global ones.

Handling exceptions inside them is not optional, and the reason is stronger than "it might be
ignored". A trigger listener or a job listener that throws *before* the job runs means the job does
not run at all — the scheduler logs "Job will NOT be executed!", tells the scheduler listeners, and
completes the firing without executing anything. One that throws afterwards stops the remaining
listeners being notified. The firing itself is completed properly either way, so a throwing listener
does not wedge a trigger, but it can silently cost you the execution.

### Do not let users choose the job type

If an application exposes scheduling to its users, the type of job must not be one of the things
they choose. `Quartz.Jobs` still ships `NativeJob`, which runs an arbitrary operating-system
command, and `SendMailJob`; a user who can name a job type and its data can run either. Java's page
puts it correctly: allowing users to define whatever job they want "effectively opens your system to
all sorts of vulnerabilities comparable/equivalent to Command Injection Attacks as defined by OWASP
and MITRE". Offer a fixed set of job types and validate their parameters.

The same caution applies to the management surfaces. On 3.x the dashboard has a single authorization
policy and a single read-only flag, and there is no per-scheduler policy: if different people should
reach different schedulers, enforce that outside Quartz.NET. On 4.x both surfaces take a
`SchedulerAuthorizationPolicy` — `QuartzDashboardOptions` and `QuartzHttpApiOptions` — evaluated per
request against a `SchedulerResource` naming the scheduler, so one
`AuthorizationHandler<TRequirement, SchedulerResource>` holds each caller to its own scheduler. What
a caller may *do* to the scheduler it reaches is still process-wide, through the dashboard's read-only
flag. See
[Authorizing a tenant on its own scheduler](quartz-4.x/multi-tenancy.md#authorizing-a-tenant-on-its-own-scheduler)
and [Tenancy Patterns](tenancy-patterns.md#what-quartz-net-does-not-give-you).

## What to watch

Be sceptical of a dashboard that only shows what ran. The failures in this page — a starved pool, a
cluster mis-declaring a node, a group parked at its ceiling — all look like *absence*, and absence
is what a naive metric cannot distinguish from a quiet night.

**Traces**, on both versions, come from an `ActivitySource` named `Quartz`: one activity per job
execution (`Quartz.Job.Execute`, which records the exception when one is thrown), one for a vetoed
firing (`Quartz.Job.Veto`), and one per job store operation (`Quartz.JobStore.AcquireNextTriggers`,
`.TriggersFired` and the rest). The job and trigger name and group, the job type and the fire
instance id are attributes. Store-operation spans are the ones to watch for the failures above:
acquisition latency and its exceptions are where a struggling database first shows.

**Metrics are 4.x only** — Quartz 3.x publishes none at all — and there are eight instruments, all on
a meter named `Quartz`. **Every measurement carries `quartz.scheduler.name` and
`quartz.scheduler.id`**, so a cluster is separable by node and a process running several schedulers by
scheduler, with no instrumentation of your own. Four of them answer the failures in this page directly:

- `quartz.job.execution.duration` — a histogram in seconds, tagged `error.type` when the execution
  failed. Its *count* is the number of executions, so execution and failure counts come out of it and
  do not need counters of their own.
- `quartz.job.execution.active` — an up-down counter of executions in flight. Parked at a ceiling is
  what a starved pool and a saturated execution group both look like.
- `quartz.trigger.misfire` — firings that were owed and did not happen on time. This is the alert to
  build for "the schedule is slipping", and it is the one that catches a group parked at its limit.
- `quartz.trigger.acquisition.duration` — how long the scheduling loop waited on its store for the
  next batch. A struggling database shows here before it shows anywhere else.

The other four are `quartz.trigger.acquired`, `quartz.cluster.checkin.duration`,
`quartz.cluster.recovery.trigger` — a node's work being taken over, which is a cluster mis-declaring a
node made visible — and `quartz.jobstore.operation.duration`, tagged with the operation's name. The
[OpenTelemetry page](quartz-4.x/packages/opentelemetry-integration.md#metrics) has the full table with
each instrument's attributes.

**There is still no trigger-state gauge**, so do not build an alert on one. For that the store is the
source: count `QRTZ_TRIGGERS` grouped by `TRIGGER_STATE` and alert on `ERROR` and on `BLOCKED` rows
older than your longest job, and count `QRTZ_FIRED_TRIGGERS` to see what the cluster believes is
running. In 4.x, `IScheduler.QueryFireInstances` answers the latter for the whole cluster without SQL.

**A health check** ships with the ASP.NET Core integration, and it asserts less than its name
suggests: that the scheduler is in a state that can fire, and that the job store answers a query. It
does **not** assert that any trigger is actually firing, so pair it with an alert on a job you expect
to see regularly. In Quartz 4.x you register it explicitly —
`services.AddHealthChecks().AddQuartz()`, or `AddQuartzHealthChecks()` per named scheduler — and it
distinguishes the states, reporting a standby scheduler as *degraded* rather than healthy or dead.
On 3.x it is registered for you by `AddQuartzServer()` and only reports healthy or unhealthy, from
`IsStarted` alone, which means a scheduler sitting in standby passes.

**Logging is the first diagnostic step**, not the last. Misfire handling and every cluster-recovery
decision are logged at information level when they do anything at all, and trigger acquisition at
debug — which is what makes "no triggers were acquired" and "someone recovered this node" readable
after the fact. The great majority of investigations in this project's issue tracker are resolved by
the first person to turn logging on. Configure it before you need it, not after.

Setup for all of the above is on
[Observability (4.x)](/documentation/quartz-4.x/packages/opentelemetry-integration) and
[OpenTelemetry Integration (3.x)](/documentation/quartz-3.x/packages/opentelemetry-integration).

## See also

- [Troubleshooting](troubleshooting.md) — symptoms, and what to do about each
- [FAQ](faq.md) — including the longer treatments of daylight saving and clock changes
- [Tenancy Patterns](tenancy-patterns.md) — partitioning a scheduler between tenants
- [Configuration Reference (4.x)](/documentation/quartz-4.x/configuration/reference) and
  [(3.x)](/documentation/quartz-3.x/configuration/reference) — every setting named here, with its default

## Sources

Prior art surveyed in August 2026. Quartz.NET's own behaviour is stated from the source in this
repository rather than from any of these.

- Quartz (Java), [Best Practices](https://www.quartz-scheduler.org/documentation/quartz-2.5.0/best-practices.html),
  [Configuration Reference](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/configuration/ConfigMain.html),
  [JDBC-JobStore clustering](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/configuration/ConfigJDBCJobStoreClustering.html)
  and [tutorial lessons 3–6](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/tutorials/tutorial-lesson-03.html)
- Hangfire, [Best Practices](https://docs.hangfire.io/en/latest/best-practices.html),
  [Dealing with exceptions](https://docs.hangfire.io/en/latest/background-processing/dealing-with-exceptions.html),
  [Throttling](https://docs.hangfire.io/en/latest/background-processing/throttling.html) and
  [Using cancellation tokens](https://docs.hangfire.io/en/latest/background-methods/using-cancellation-tokens.html)
- Sidekiq, [Best Practices](https://github.com/sidekiq/sidekiq/wiki/Best-Practices)
- Kubernetes, [CronJob](https://kubernetes.io/docs/concepts/workloads/controllers/cron-jobs/) and
  [Pod lifecycle](https://kubernetes.io/docs/concepts/workloads/pods/pod-lifecycle/)
- Microsoft, [Idempotent Consumer pattern](https://learn.microsoft.com/azure/architecture/patterns/idempotent-consumer),
  [Transient fault handling](https://learn.microsoft.com/azure/architecture/best-practices/transient-faults),
  [Diagnosing thread pool starvation](https://learn.microsoft.com/dotnet/core/diagnostics/debug-threadpool-starvation),
  [Generic host shutdown](https://learn.microsoft.com/dotnet/core/extensions/generic-host) and
  [Time sync for Azure virtual machines](https://learn.microsoft.com/azure/virtual-machines/linux/time-sync)
- Particular Software, [Outbox](https://docs.particular.net/nservicebus/outbox/) and
  [What does idempotent mean?](https://particular.net/blog/what-does-idempotent-mean)
- Celery, [Tasks: `Task.rate_limit`](https://docs.celeryq.dev/en/stable/userguide/tasks.html)
- HikariCP, [About Pool Sizing](https://github.com/brettwooldridge/HikariCP/wiki/About-Pool-Sizing), and
  PostgreSQL, [Number of database connections](https://wiki.postgresql.org/wiki/Number_Of_Database_Connections)
- Microsoft, [SQL Server connection pooling](https://learn.microsoft.com/sql/connect/ado-net/sql-server-connection-pooling),
  and Npgsql, [Connection string parameters](https://www.npgsql.org/doc/connection-string-parameters.html)
- Martin Kleppmann, [How to do distributed locking](https://martin.kleppmann.com/2016/02/08/how-to-do-distributed-locking.html),
  and Marc Brooker, [It's About Time](https://brooker.co.za/blog/2023/11/27/about-time.html)
- David Fowler, [Async Guidance](https://github.com/davidfowl/AspNetCoreDiagnosticScenarios/blob/master/AsyncGuidance.md)
