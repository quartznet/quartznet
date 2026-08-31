---

title: Retrying Failed Jobs
---

# Retrying Failed Jobs

A trigger can carry a **retry policy**: how many times, and how far apart, the scheduler re-fires it when
its job fails. Give a trigger one and there is nothing else to do — a job that throws is retried, and a job
that succeeds is not.

## Give the trigger a policy

Three shapes, and the only three ways to make one:

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

Or spell the waits out. The table's length is the number of attempts, and its last entry repeats:

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

`MaxAttempts` counts retries *after* the first failure, not fires: `Fixed(3, …)` means a persistently
failing job runs four times in all.

## What counts as a failure

A job fails, for retry purposes, when `Execute` throws — anything at all. There is no interface to
implement and no attribute to add.

Three things are deliberately not failures:

* A `JobExecutionException` that asks for something itself. `RefireImmediately`, `UnscheduleFiringTrigger`
  and `UnscheduleAllTriggers` are decisions the job made, and they win over the trigger's policy.
* A cancellation on the scheduler's own token. Shutdown and interrupt are operator decisions; a node that
  vanishes mid-execution is what [`RequestsRecovery`](../tutorial/more-about-jobs.md) is for.
* Anything at all, on a trigger with no policy. That is the default, and it behaves exactly as it always
  did.

A job that knows a failure is not worth retrying can say so, and keep its attempts for the failures that
are:

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

`ScheduledFireTimeUtc` is the same on every one of those firings — a retry is another attempt at one
occurrence, so it reports the occurrence the schedule called for rather than the instant it actually ran.

::: warning RetryAttempt is not RefireCount
`RefireCount` counts iterations of the in-process refire loop: same context, same thread, no delay, no
ceiling, nothing persisted, and the execution slot never released. `RetryAttempt` counts retries of an
occurrence: a fresh firing at a later instant, recorded in the job store, surviving a restart and visible
to every node in a cluster. **`RefireImmediately` is not a zero-delay retry**, and the two counters move
independently.
:::

## The rules worth knowing

**A retry never displaces the trigger's next scheduled occurrence.** If the retry would land at, or within
a second of, the next fire time, it is dropped and the occurrence wins. So a policy whose waits are longer
than the gap between occurrences quietly does nothing — an hourly trigger with a 90-minute retry wait is
never retried, because the next hour comes first. A retry is not scheduled past the trigger's `EndTimeUtc`
either, nor past the end of the calendar: an exponential policy's waits grow until one of them is longer
than the room left in a `DateTimeOffset`, and a retry with nowhere to land is declined for the same reason
as one that would land too late. In every case the occurrence settles and the trigger keeps its ordinary
schedule.

**A retry burns nothing.** It does not consume a `SimpleTrigger` repeat count, a recurrence rule's `COUNT`
slot, or a `TimesTriggered`. The schedule after a retry is exactly the schedule there would have been if
nothing had failed.

**Running out of attempts is not an error.** The trigger goes back to its ordinary schedule with the
attempt reset — it is not moved to `TriggerState.Error`, because one bad hour must not kill a cron trigger.

**A missed retry is an ordinary misfire.** If the scheduler never got to the retry, the trigger's own
misfire instruction decides what happens, and the attempt is cleared: the occurrence it belonged to is
gone. There is no separate retry-misfire policy.

## Changing a policy on a stored trigger

`UpdateTriggerDetails` changes the policy without rescheduling the trigger:

<!-- snippet: sample_retry_update_stored_trigger -->
```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("nightly", "imports"),
    new TriggerDetailsUpdate().WithRetryPolicy(RetryPolicy.Fixed(5, TimeSpan.FromMinutes(2))),
    cancellationToken);
```
<!-- endSnippet -->

Passing `null` stops it retrying:

<!-- snippet: sample_retry_clear_stored_trigger -->
```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("nightly", "imports"),
    new TriggerDetailsUpdate().WithRetryPolicy(null),
    cancellationToken);
```
<!-- endSnippet -->

The new policy applies from the next failure. An occurrence already waiting on a retry keeps the schedule
it was given, and there is deliberately no way to set the *attempt* through an update: it belongs to the
occurrence in flight, and setting it would either grant a running job extra attempts or take away ones it
has already spent.

## Watching it happen

* Meter `quartz.trigger.retry` counts each retry the scheduler schedules, tagged with the scheduler, the
  trigger group and the execution group — the same tags `quartz.trigger.misfire` carries.
* Log event `1056` reports the trigger, the attempt and the retry instant at `Information`.
* `ITriggerListener.TriggerComplete` is called with `SchedulerInstruction.RetryTrigger`.
* On a persistent store the two columns are on `QRTZ_TRIGGERS`: `RETRY_POLICY` holds the policy's stored
  string form and `RETRY_ATTEMPT` how far through it the current occurrence is. Both are queryable, and
  the dashboard's trigger page shows them.

## See also

* [More About Triggers](../tutorial/more-about-triggers.md) — misfire instructions, priorities and calendars
* [Rescheduling Jobs](rescheduling-jobs.md) — changing a live schedule, and recovering a trigger in error
