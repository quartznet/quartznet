---

title: Retrying Failed Jobs
---

# Retrying Failed Jobs

A **retry policy** says how many times, and how far apart, the scheduler re-fires a trigger when its job
fails. With a policy set, a job that throws is retried and one that succeeds is not. The policy is the
trigger's own, its job type's or the scheduler's default: see
[Declare it on the job, or set a default](#declare-it-on-the-job-or-set-a-default).

## Give the trigger a policy

There are exactly three kinds: fixed, exponential and explicit. A fixed policy waits the same time
between retries:

<!-- snippet: sample_retry_fixed -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
    q.AddTrigger<ImportJob>(t => t
        .ForJob("import", "nightly")
        .WithCronSchedule("0 0 2 * * ?")
        // Three retries, five minutes apart, after a failure.
        .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5))));
});
```
<!-- endSnippet -->

An exponential policy backs off, optionally up to a ceiling:

<!-- snippet: sample_retry_exponential -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
    q.AddTrigger<ImportJob>(t => t
        .ForJob("import", "nightly")
        .WithCronSchedule("0 0 2 * * ?")
        // 30s, 1m, 2m, 4m, 8m — but never longer than ten minutes.
        .WithRetryPolicy(RetryPolicy.Exponential(
            maxAttempts: 5,
            initialDelay: TimeSpan.FromSeconds(30),
            factor: 2,
            maxDelay: TimeSpan.FromMinutes(10))));
});
```
<!-- endSnippet -->

An exponential policy can be **jittered**, so triggers that failed together do not return together. Each
wait is multiplied by a value drawn uniformly from `[1 - jitter, 1 + jitter]`, still bounded by the ceiling:

<!-- snippet: sample_retry_jitter -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
    q.AddTrigger<ImportJob>(t => t
        .ForJob("import", "nightly")
        .WithCronSchedule("0 0 2 * * ?")
        // The same backoff as above, spread by a fifth either way: the first retry lands
        // between 24 and 36 seconds after the failure, the second between 48 and 72, and so
        // on. A hundred triggers that failed on the same outage come back at a hundred
        // different instants instead of all at once.
        .WithRetryPolicy(RetryPolicy.Exponential(
            maxAttempts: 5,
            initialDelay: TimeSpan.FromSeconds(30),
            factor: 2,
            maxDelay: TimeSpan.FromMinutes(10),
            jitter: 0.2)));
});
```
<!-- endSnippet -->

A jitter of `0` (the default) equals the four-argument overload, down to the bytes in the `RETRY_POLICY`
column.

::: warning A jittered policy is not readable by a node older than 4.2
A non-zero jitter is stored as a `;j<value>` token appended to the policy's stored form. A 4.1 node cannot
parse it and reports the row as unreadable. In a mixed cluster, roll every node to 4.2 before using jitter.
:::

An explicit policy lists the waits; the list's length is the number of attempts, and its last entry
repeats:

<!-- snippet: sample_retry_explicit -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
    q.AddTrigger<ImportJob>(t => t
        .ForJob("import", "nightly")
        .WithCronSchedule("0 0 2 * * ?")
        // Try again quickly twice, then give the upstream system an hour.
        .WithRetryPolicy(RetryPolicy.Explicit(
            TimeSpan.FromSeconds(10),
            TimeSpan.FromMinutes(1),
            TimeSpan.FromHours(1))));
});
```
<!-- endSnippet -->

`MaxAttempts` counts retries *after* the first failure: `Fixed(3, …)` runs a job that keeps failing four
times.

## Declare it on the job, or set a default

The first policy found applies:

| Order | Set with | Applies to |
|---|---|---|
| 1. The trigger's own | `.WithRetryPolicy(…)` | that trigger |
| 2. The job type's | `[RetryPolicy(…)]` on the class, a base class or an interface | the job's triggers with no policy of their own |
| 3. The scheduler's default | `q.UseDefaultRetryPolicy(…)` | triggers whose trigger and job type name none |
| None found | — | a failed job is reported, not retried |

`RetryPolicy.None` found at any level means no retry.

Declare the policy on the job when whether it is safe to retry is a property of its code:

<!-- snippet: sample_retry_policy_attribute -->
```csharp
// Five retries, 30s, 1m, 2m, 4m and 8m apart, but never more than ten minutes, for
// every trigger of this job that names no policy of its own.
[RetryPolicy(5, "00:00:30", 2, MaxDelay = "00:10:00")]
public sealed class FeedImportJob : IJob
{
    private readonly IImportService importer;

    public FeedImportJob(IImportService importer)
    {
        this.importer = importer;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        await importer.Run(cancellationToken);
    }
}
```
<!-- endSnippet -->

| Attribute | Policy |
|---|---|
| `[RetryPolicy(3, "00:05:00")]` | `RetryPolicy.Fixed(3, …)` |
| `[RetryPolicy(5, "00:00:30", 2, MaxDelay = "00:10:00", Jitter = 0.2)]` | `RetryPolicy.Exponential(…)` |
| `[RetryPolicy("00:00:10", "00:01:00", "01:00:00")]` | `RetryPolicy.Explicit(…)` |
| `[RetryPolicy(0)]` | `RetryPolicy.None` |

Durations are invariant `TimeSpan` strings. An attribute whose arguments are not a policy fails
`AddJob` and `ScheduleJob` with a `SchedulerException` naming the job type.

Set a default for everything else:

<!-- snippet: sample_retry_default_policy -->
```csharp
builder.Services.AddQuartz(q =>
{
    // Any trigger whose own policy and job type name none: three retries, one minute apart.
    q.UseDefaultRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(1)));
});
```
<!-- endSnippet -->

Opt a job type or a trigger out of what it would inherit:

<!-- snippet: sample_retry_never_retried_job -->
```csharp
// Charging a card twice is worse than not charging it: never retried, whatever the
// scheduler's default says.
[RetryPolicy(0)]
public sealed class ChargeCardJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
```
<!-- endSnippet -->

<!-- snippet: sample_retry_none_on_trigger -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
    q.AddTrigger<ImportJob>(t => t
        .ForJob("import", "nightly")
        .WithCronSchedule("0 0 2 * * ?")
        // Never retried, whatever ImportJob declares or the scheduler defaults to.
        .WithRetryPolicy(RetryPolicy.None));
});
```
<!-- endSnippet -->

**The policy is looked up when the job fails.** Nothing is written to the trigger, so the job type's policy
and the default cover triggers stored before them, and the `RETRY_POLICY` column stays the trigger's own.
`IJobExecutionContext.RetryPolicy` is the policy that applies to a firing.

**Every node decides for itself.** A node reads the attribute from its own build and the default from its
own configuration, so deploy the same on every node. A node older than 4.4 ignores both.

**Manual runs and recovery inherit too.** `TriggerJob`, the dashboard's *Run now* and *Run again*, and the
trigger that [recovers](../tutorial/more-about-jobs.md) a job a dead node was running all fire on a trigger
with no policy of its own, so the job type's policy or the default retries them. `[RetryPolicy(0)]` on the job
stops that.

**Mark a job that must not run twice with `[RetryPolicy(0)]`.** It lives in code, so no node can drop it. A
trigger's `RetryPolicy.None` is a stored value, which a 4.3 node empties when it fires the trigger.
`SendMailJob` and `NativeJob` carry it.

::: warning RetryPolicy.None on a trigger needs every node on 4.4
`None` is stored as `none`. A 4.3 node reads it as no policy, which means the same there, but writes the
column back empty when it fires the trigger. The trigger then inherits on a 4.4 node.
:::

## What counts as a failure

A job fails when `Execute` throws anything; there is nothing to implement or annotate. Not failures:

* A `JobExecutionException` asking for `RefireImmediately`, `UnscheduleFiringTrigger` or
  `UnscheduleAllTriggers`: the job's own decision wins over the policy.
* A cancellation on the scheduler's own token (shutdown, interrupt). For a node vanishing mid-execution, use
  [`RequestsRecovery`](../tutorial/more-about-jobs.md); for a shutdown, see
  [A job a shutdown stops](#a-job-a-shutdown-stops).
* Anything, when no policy applies (the default).

A job can catch a failure that retrying cannot fix and return, saving its attempts:

<!-- snippet: sample_retry_not_worth_retrying -->
```csharp
public sealed class SelectiveImportJob : IJob
{
    private readonly IImportService importer;
    private readonly ILogger<SelectiveImportJob> logger;

    public SelectiveImportJob(IImportService importer, ILogger<SelectiveImportJob> logger)
    {
        this.importer = importer;
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await importer.Run(cancellationToken);
        }
        catch (TransientImportException)
        {
            // Let it out. Throwing is what asks for a retry, so the trigger's policy takes over.
            throw;
        }
        catch (InvalidOperationException e)
        {
            // A failure no amount of retrying can fix - bad input, not a flaky dependency. Report
            // it and return: the occurrence is over, and the trigger goes back to its ordinary
            // schedule instead of spending its attempts on a certainty.
            logger.LogError(e, "Import cannot succeed for this occurrence and will not be retried");
        }
    }
}
```
<!-- endSnippet -->

## A job a shutdown stops

A shutdown's cancellation is not a failure, so no policy retries it. From 4.4 a persistent store can hand
the firing back for recovery instead: the job runs again on a peer's next acquisition, or on this node when
it starts. Off by default.

<!-- snippet: sample_retry_hand_back_on_shutdown -->
```csharp
builder.Services.AddQuartz(q =>
{
    // The shutdown asks running jobs to stop, and waits for them.
    q.ConfigureScheduler(options => options.ShutdownJobInterruption = ShutdownJobInterruption.WhenWaitingForJobs);

    q.UsePersistentStore(store =>
    {
        store.UsePostgres(connectionString);
        store.ConfigureStore(options => options.RecoverFiringsCancelledByShutdown = true);
    });

    q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly").RequestRecovery());
});

builder.Services.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

| What | Must be |
|---|---|
| The store | Persistent, with `RecoverFiringsCancelledByShutdown` (flat key `quartz.jobStore.recoverFiringsCancelledByShutdown`). The in-memory store has nothing to hand back to; the setting does not exist there |
| The job | `RequestRecovery()`, and it stops by throwing `OperationCanceledException`. A job that returns has finished |
| The cancellation | The shutdown's, through [`ShutdownJobInterruption`](../configuration/reference.md#scheduler). `Interrupt`, `InterruptFireInstance` and `[JobTimeout]` never hand back |

What a hand-back does:

* Stores a recovery trigger in `RECOVERING_JOBS`, in the completion's transaction, as cluster recovery does
  after a crash. The job sees `Recovering` and `RecoveringTriggerKey`.
* Counts no retry: `RetryAttempt` does not move, and the policy is not consulted.
* Moves what awaits the trigger onto the recovery trigger, so the replay's outcome settles it: an
  `OnSuccess` continuation runs once the replay succeeds, an `OnFailure` one once it fails with no retry
  left. A spent one-shot trigger is deleted as usual.
* Does not write the job's data, even with `[PersistJobDataAfterExecution]`: the replay starts from the data
  the cancelled run started with.
* Releases a `[DisallowConcurrentExecution]` job's other triggers, and a `BufferOne` or `CancelPrevious`
  trigger's hold, as any completion does.
* Records a `Cancelled` history row with the summary *Handed back for recovery: the scheduler shut down
  while it ran.* Log event `3054` names the recovery trigger.

The replay is an ordinary recovery trigger, as after a crash. It has no policy of its own, nor the original
trigger's, so a failed replay is retried by the job type's `[RetryPolicy]` or the scheduler's default, from
attempt `0`; see
[Declare it on the job, or set a default](#declare-it-on-the-job-or-set-a-default). Handed back again, it
keeps the markers of the original firing.

A completion that arrives after the store has closed is refused, as before. Its fired-trigger row stays, and
recovery replays the job when a peer, or this node at its next start, recovers it. A 4.3 node ignores the
setting, and fires a recovery trigger a 4.4 node stored.

## What the job sees

`IJobExecutionContext.RetryAttempt` is `0` on a regular fire and *n* on the *n*-th retry:

<!-- snippet: sample_retry_reading_the_attempt -->
```csharp
public sealed class RetryAwareImportJob : IJob
{
    private readonly IImportService importer;
    private readonly ILogger<RetryAwareImportJob> logger;

    public RetryAwareImportJob(IImportService importer, ILogger<RetryAwareImportJob> logger)
    {
        this.importer = importer;
        this.logger = logger;
    }

    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        if (context.RetryAttempt > 0)
        {
            logger.LogWarning(
                "Import retry {Attempt} for the occurrence scheduled at {Scheduled}",
                context.RetryAttempt,
                context.ScheduledFireTimeUtc);
        }

        // Throwing anything is what asks for a retry. There is nothing to opt into.
        await importer.Run(cancellationToken);
    }
}
```
<!-- endSnippet -->

`ScheduledFireTimeUtc` is the same on every retry: the occurrence the schedule called for, not when the retry
ran.

::: warning RetryAttempt is not RefireCount

| | `RefireCount` | `RetryAttempt` |
|---|---|---|
| Counts | iterations of the in-process refire loop | retries of an occurrence |
| Runs in | the same context and thread; the execution slot is never released | a fresh firing |
| Delay | none | until a later instant |
| Ceiling | none | the policy |
| Persisted | no | in the job store; survives a restart, visible to every node in a cluster |

**`RefireImmediately` is not a zero-delay retry**; the two counters are independent.
:::

## When the policy gives up

**1. The execution context.** `IJobExecutionContext.Outcome` is how the firing ended;
`IJobExecutionContext.RetryScheduled` is whether another attempt follows. Both are set before completion
notifications, so `JobWasExecuted` and `TriggerComplete` can read them:

<!-- snippet: sample_retry_reading_the_outcome -->
```csharp
/// <summary>
/// A job listener that tells an attempt from a verdict, which before 4.2 only a trigger listener
/// could do — and only by comparing an instruction against <c>RetryTrigger</c>.
/// </summary>
public sealed class OutcomeReadingListener : IJobListener
{
    private readonly ILogger<OutcomeReadingListener> logger;

    public OutcomeReadingListener(ILogger<OutcomeReadingListener> logger)
    {
        this.logger = logger;
    }

    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        if (context.Outcome == ExecutionOutcome.Failed && !context.RetryScheduled)
        {
            logger.LogError("{Job} failed for the last time", context.JobDetail.Key);
        }

        return default;
    }
}
```
<!-- endSnippet -->

A job that threw is `ExecutionOutcome.Failed` whether or not it will be retried; `RetryScheduled` says
whether the occurrence is finished.

**2. `ITriggerListener.TriggerRetriesExhausted`**, raised once per occurrence, between `JobWasExecuted` and
`TriggerComplete`, when a policy applies, the job threw, and no attempt follows because:

* the policy's attempts are **spent**;
* a retry was **declined for lack of room**: it would land at or within a second of the next occurrence,
  after the trigger's end time, or past the end of representable time ([the rules](#the-rules-worth-knowing)),
  so attempts are left. Only under the trigger's own policy: see the warning below;
* the job's `JobExecutionException` asked for **`UnscheduleFiringTrigger` or `UnscheduleAllTriggers`**, so no
  retry was attempted.

Tell them apart with `context.RetryAttempt` against `context.RetryPolicy.MaxAttempts`, and
`TriggerComplete`'s instruction.

::: warning An inherited policy with no room is not exhausted
A job type's `[RetryPolicy]` and the scheduler's default are chosen without knowing a trigger's schedule. When
one of them leaves no room for a retry, the occurrence settles quietly: no `TriggerRetriesExhausted`, no
`quartz.trigger.retries_exhausted` count, no [pause](pausing-with-a-reason.md#pausing-when-retries-run-out),
only debug event `1062`. A minutely trigger under a five-minute default is never retried and never reported.
Give such a trigger a policy of its own that fits its schedule, or `RetryPolicy.None`.
:::

Only a trigger whose completion `TriggerBase.ExecutionComplete` decides applies an inherited policy. A trigger
of your own that does not derive from `TriggerBase`, or overrides `ExecutionComplete` without calling the base,
is reported by its own policy alone.

<!-- snippet: sample_retry_listener_gave_up -->
```csharp
/// <summary>
/// Raises an alert when an occurrence has run out of retries, and says nothing while it is still
/// trying.
/// </summary>
public sealed class GaveUpListener : ITriggerListener
{
    private readonly ILogger<GaveUpListener> logger;

    public GaveUpListener(ILogger<GaveUpListener> logger)
    {
        this.logger = logger;
    }

    public ValueTask TriggerRetriesExhausted(
        ITrigger trigger,
        IJobExecutionContext context,
        JobExecutionException exception,
        CancellationToken cancellationToken = default)
    {
        // context.RetryAttempt is how many retries this occurrence spent before giving up, and
        // context.RetryScheduled is false: there is no further attempt coming.
        logger.LogError(
            exception,
            "{Job} gave up after {Attempts} retries; the occurrence scheduled for {Scheduled} never succeeded",
            context.JobDetail.Key,
            context.RetryAttempt,
            context.ScheduledFireTimeUtc);

        return default;
    }
}
```
<!-- endSnippet -->

<!-- snippet: sample_retry_listener_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddTriggerListener<GaveUpListener>(Matchers.AllTriggers());
});
```
<!-- endSnippet -->

Never raised when **no** policy applies, nor for a failure being retried, a cancelled, vetoed or successful
firing, or a `RefireImmediately` run other than the one that ends the firing. It is a default interface
member, so listeners written for 4.0 or 4.1 are unchanged.

**3. The execution history.** Each row carries `RetryAttempt` and `RetryScheduled`. A failed row with
`RetryScheduled` false is a *final* failure, selected by `ExecutionHistoryQuery.FailedFinally` (HTTP API:
`failedFinally` on `GET …/history/executions`). `Succeeded` alone cannot do this: a policy of three writes
four failed rows for one bad night.

The dashboard's **History** page shows *Failed (retrying)* or *Failed*, has a *Failed after retries* option
in its **Outcome** filter, and gives a final failure a **Run again** button (recorded in the action log;
absent when read-only).

**4. Pause the trigger instead.** From 4.3, `q.PauseTriggerWhenRetriesExhausted()` pauses a trigger that
gives up, with the exception's message as the reason, until someone resumes it. See
[Pausing when retries run out](pausing-with-a-reason.md#pausing-when-retries-run-out).

## The rules worth knowing

**A retry never displaces the trigger's next occurrence.** One that would land at, or within a second of,
the next fire time is dropped: an hourly trigger with a 90-minute wait is never retried. Nor is a retry
scheduled past `EndTimeUtc` or the end of the calendar (an exponential wait can outgrow `DateTimeOffset`).
The occurrence ends and the schedule continues.

**A retry burns nothing**: no `SimpleTrigger` repeat count, recurrence `COUNT` slot or `TimesTriggered`. The
schedule afterwards is the one there would have been without the failure.

**Running out of attempts is not an error.** The trigger returns to its schedule with the attempt reset, not
to `TriggerState.Error`, unless [`PauseTriggerWhenRetriesExhausted()`](pausing-with-a-reason.md#pausing-when-retries-run-out)
pauses it.

**A missed retry is an ordinary misfire.** The trigger's misfire instruction decides, and the attempt is
cleared with its occurrence. There is no separate retry-misfire policy.

## Changing a policy on a stored trigger

`UpdateTriggerDetails` changes the policy without rescheduling:

<!-- snippet: sample_retry_update_stored_trigger -->
```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("nightly", "imports"),
    new TriggerDetailsUpdate().WithRetryPolicy(RetryPolicy.Fixed(5, TimeSpan.FromMinutes(2))),
    cancellationToken);
```
<!-- endSnippet -->

`RetryPolicy.None` stops retries:

<!-- snippet: sample_retry_stop_stored_trigger -->
```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("nightly", "imports"),
    new TriggerDetailsUpdate().WithRetryPolicy(RetryPolicy.None),
    cancellationToken);
```
<!-- endSnippet -->

`null` removes the trigger's own policy, so it inherits its job type's or the scheduler's default. With
neither, that also stops retries:

<!-- snippet: sample_retry_clear_stored_trigger -->
```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("nightly", "imports"),
    new TriggerDetailsUpdate().WithRetryPolicy(null),
    cancellationToken);
```
<!-- endSnippet -->

The change applies from the next failure; a pending retry keeps its time. The *attempt* cannot be set: it
belongs to the occurrence in flight.

## Watching it happen

* Meter `quartz.trigger.retry`: each scheduled retry, tagged with scheduler, trigger group and execution
  group (the same tags as `quartz.trigger.misfire`).
* Meter `quartz.trigger.retries_exhausted`: each occurrence that gave up, same tags. Compare the two to see
  where a policy gains nothing.
* Log event `1056` (`Information`): trigger, attempt and retry instant. `1057`: the occurrence that gave up,
  attempts spent, and the last exception.
* `ITriggerListener.TriggerComplete` with `SchedulerInstruction.RetryTrigger`, and
  `ITriggerListener.TriggerRetriesExhausted` once per occurrence that gives up.
* `QRTZ_TRIGGERS` columns: `RETRY_POLICY` (the trigger's own policy in its stored string form, `none`, or
  empty when it inherits) and `RETRY_ATTEMPT` (progress of the current occurrence, whichever policy applies).
  Both queryable; shown on the dashboard's trigger page.
* `QRTZ_EXECUTION_HISTORY`, where history is in the database: `RETRY_ATTEMPT` and `RETRY_SCHEDULED` per row.

## See also

* [More About Triggers](../tutorial/more-about-triggers.md) — misfire instructions, priorities and calendars
* [Rescheduling Jobs](rescheduling-jobs.md) — changing a live schedule, and recovering a trigger in error
* [Pausing with a Reason](pausing-with-a-reason.md) — pausing a trigger whose retries ran out
