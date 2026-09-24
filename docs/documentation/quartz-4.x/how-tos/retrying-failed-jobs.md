---

title: Retrying Failed Jobs
---

# Retrying Failed Jobs

A trigger's **retry policy** says how many times, and how far apart, the scheduler re-fires it when its job
fails. With a policy set, a job that throws is retried and one that succeeds is not.

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

## What counts as a failure

A job fails when `Execute` throws anything; there is nothing to implement or annotate. Not failures:

* A `JobExecutionException` asking for `RefireImmediately`, `UnscheduleFiringTrigger` or
  `UnscheduleAllTriggers`: the job's own decision wins over the policy.
* A cancellation on the scheduler's own token (shutdown, interrupt). For a node vanishing mid-execution, use
  [`RequestsRecovery`](../tutorial/more-about-jobs.md).
* Anything, on a trigger with no policy (the default, unchanged behaviour).

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
`TriggerComplete`, when the trigger has a policy, the job threw, and no attempt follows because:

* the policy's attempts are **spent**;
* a retry was **declined for lack of room**: it would land at or within a second of the next occurrence,
  after the trigger's end time, or past the end of representable time ([the rules](#the-rules-worth-knowing)),
  so attempts are left;
* the job's `JobExecutionException` asked for **`UnscheduleFiringTrigger` or `UnscheduleAllTriggers`**, so no
  retry was attempted.

Tell them apart with `context.RetryAttempt` against the policy's `MaxAttempts`, and `TriggerComplete`'s
instruction.

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

Never raised for a trigger with **no** policy, a failure being retried, a cancelled, vetoed or successful
firing, or a `RefireImmediately` run other than the one that ends the firing. It is a default interface
member, so listeners written for 4.0 or 4.1 are unchanged.

**3. The execution history.** Each row carries `RetryAttempt` and `RetryScheduled`. A failed row with
`RetryScheduled` false is a *final* failure, selected by `ExecutionHistoryQuery.FailedFinally` (HTTP API:
`failedFinally` on `GET …/history/executions`). `Succeeded` alone cannot do this: a policy of three writes
four failed rows for one bad night.

The dashboard's **History** page shows *Failed (retrying)* or *Failed*, has a *Failed after retries* option
in its **Outcome** filter, and gives a final failure a **Run again** button (recorded in the action log;
absent when read-only).

## The rules worth knowing

**A retry never displaces the trigger's next occurrence.** One that would land at, or within a second of,
the next fire time is dropped: an hourly trigger with a 90-minute wait is never retried. Nor is a retry
scheduled past `EndTimeUtc` or the end of the calendar (an exponential wait can outgrow `DateTimeOffset`).
The occurrence ends and the schedule continues.

**A retry burns nothing**: no `SimpleTrigger` repeat count, recurrence `COUNT` slot or `TimesTriggered`. The
schedule afterwards is the one there would have been without the failure.

**Running out of attempts is not an error.** The trigger returns to its schedule with the attempt reset, not
to `TriggerState.Error`.

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

`null` stops retries:

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
* `QRTZ_TRIGGERS` columns: `RETRY_POLICY` (the policy's stored string form) and `RETRY_ATTEMPT` (progress of
  the current occurrence). Both queryable; shown on the dashboard's trigger page.
* `QRTZ_EXECUTION_HISTORY`, where history is in the database: `RETRY_ATTEMPT` and `RETRY_SCHEDULED` per row.

## See also

* [More About Triggers](../tutorial/more-about-triggers.md) — misfire instructions, priorities and calendars
* [Rescheduling Jobs](rescheduling-jobs.md) — changing a live schedule, and recovering a trigger in error
