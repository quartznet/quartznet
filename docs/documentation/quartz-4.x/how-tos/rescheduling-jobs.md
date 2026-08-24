---
title: 'Rescheduling Jobs'
---

# Rescheduling Jobs

Three quite different things get called *rescheduling*, and they use three different APIs. Picking the
wrong one is how a trigger loses its fire history, or how a "just change the priority" edit silently
resets the next fire time.

| You want to | Use | Fire times |
|---|---|---|
| Change **when** the job runs | `RescheduleJob` | recomputed from the new trigger |
| Change the trigger's **metadata** | `UpdateTriggerDetails` | preserved |
| Retry **this firing** | `JobExecutionException { RefireImmediately = true }` | untouched |

## Changing the schedule: RescheduleJob

`RescheduleJob` is delete-and-store in one call. The old trigger goes, the new one is stored, and the
new one must name the same job:

<!-- snippet: sample_rescheduling_replace_trigger -->
```csharp
ITrigger replacement = TriggerBuilder.Create()
    .WithIdentity("nightly", "reports")
    .ForJob(new JobKey("build-report", "reports"))
    .WithCronSchedule("0 30 2 * * ?")
    .Build();

DateTimeOffset? firstFire = await scheduler.RescheduleJob(
    new TriggerKey("nightly", "reports"),
    replacement,
    cancellationToken);
```
<!-- endSnippet -->

The new trigger does **not** have to keep the old name — passing a different `WithIdentity` renames it
— but it does have to carry a job key, because the old trigger is gone before the new one is stored
and there is nothing left to inherit it from.

The return value is the new trigger's first fire time, or **`null` if the old trigger was not found**.
A null return means nothing was stored: the call is not "create if missing". If you are recovering
from a state where the trigger may or may not exist, check the result:

<!-- snippet: sample_rescheduling_missing_trigger -->
```csharp
DateTimeOffset? next = await scheduler.RescheduleJob(key, replacement, cancellationToken);
if (next is null)
{
    // the old trigger was gone; store the new one on its own terms
    await scheduler.ScheduleJob(replacement, cancellationToken);
}
```
<!-- endSnippet -->

Because the trigger is replaced, everything derived from the old one is recomputed. `PreviousFireTimeUtc`
starts empty, a `SimpleTrigger`'s repeat count starts over, and a paused trigger comes back in whatever
state the new trigger's group implies. Use it when the *schedule* changed.

## Changing metadata in place: UpdateTriggerDetails

`UpdateTriggerDetails` patches a stored trigger without rescheduling it. Fire times and trigger state
are preserved — a paused trigger stays paused, a trigger due in ten minutes is still due in ten
minutes.

<!-- snippet: sample_rescheduling_update_details -->
```csharp
bool applied = await scheduler.UpdateTriggerDetails(
    new TriggerKey("nightly", "reports"),
    new TriggerDetailsUpdate()
        .WithPriority(10)
        .WithDescription("moved up ahead of the invoice run"),
    cancellationToken);
```
<!-- endSnippet -->

`TriggerDetailsUpdate` is a **patch**, not a snapshot: each `With…` call marks one property as
"change this", and everything you do not call is left alone. That is what makes `null` meaningful —
`WithCalendarName(null)` disassociates the calendar, where *not calling* `WithCalendarName` leaves the
existing association in place.

| Method | Changes |
|---|---|
| `WithDescription(string?)` | the description |
| `WithPriority(int)` | acquisition priority |
| `WithJobDataMap(JobDataMap)` | the trigger's job data map, wholesale |
| `WithCalendarName(string?)` | the associated calendar; `null` or blank disassociates |
| `WithMisfireInstruction(…)` | the misfire policy — five family-typed overloads |
| `WithMisfireInstructionCode(int)` | the same, as a raw code |
| `WithExecutionGroup(string?)` | the execution group; `null` removes it from every group |
| `WithPreferredNode(PreferredNode)` | the cluster node pin |

The return value is `true` when the trigger was found and updated, `false` when the key names nothing.

Two of these do affect firing, just not the fire *times*: the misfire instruction changes what happens
the next time the trigger is late, and the execution group changes which limit the job counts against —
from the next acquisition cycle, so a job already running keeps counting against the group it was
acquired under.

### Misfire instructions are validated against the trigger's family

The same numeric code means a different policy in each trigger family: `1` is `FireNow` on a simple
trigger and `FireOnceNow` on a cron trigger. The typed overloads carry the family with the value, and
the store rejects an update whose family is not the stored trigger's:

<!-- snippet: sample_rescheduling_typed_misfire_instruction -->
```csharp
// fine — the key resolves to a cron trigger
await scheduler.UpdateTriggerDetails(cronKey, new TriggerDetailsUpdate()
    .WithMisfireInstruction(CronTriggerMisfireInstruction.DoNothing));

// rejected — the key resolves to a cron trigger, not a simple one
await scheduler.UpdateTriggerDetails(cronKey, new TriggerDetailsUpdate()
    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow));
```
<!-- endSnippet -->

`WithMisfireInstructionCode(int)` exists for callers holding a bare number — a value read off the
wire, out of configuration, or from `ITrigger.MisfireInstructionCode`. It skips the family check,
which is exactly why the typed overloads are the ones to reach for.

::: warning Changed in 4.x
The builders spell this `WithMisfireInstruction` now, on all five schedule builders —
`WithMisfireHandlingInstruction…` and the `MisfireInstruction.*` constant class are gone. The typed
enums (`SimpleTriggerMisfireInstruction`, `CronTriggerMisfireInstruction`, and the three others) are
the public vocabulary.
:::

## Choosing between them

- The **schedule** changed — a different cron expression, a different interval, a new end date:
  `RescheduleJob`. There is no way to edit a schedule in place, because a schedule is the trigger.
- Anything on the table above changed: `UpdateTriggerDetails`. It is one statement rather than a
  delete and an insert, it does not disturb the fire times, and it does not need you to rebuild the
  trigger to change its description.

The tempting middle path — read the trigger, `GetJobBuilder`-style rebuild it, store it back — is a
`RescheduleJob` with extra steps, and it quietly resets the same state.

## Retrying inside the job

A job that failed for a transient reason can ask to be run again immediately:

<!-- snippet: sample_rescheduling_refire -->
```csharp
public sealed class ImportJob(IImportService importer) : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        try
        {
            await importer.Run(cancellationToken);
        }
        catch (TransientImportException ex) when (context.RefireCount < 3)
        {
            throw new JobExecutionException(ex) { RefireImmediately = true };
        }
    }
}
```
<!-- endSnippet -->

`RefireImmediately` re-executes the same firing straight away, on the same thread-pool slot, and
`context.RefireCount` counts how many times that has happened — guard on it, or a permanently failing
job becomes a hot loop.

The same exception carries two unschedule flags for the failures that are not worth retrying:

- `UnscheduleFiringTrigger = true` removes the trigger that fired
- `UnscheduleAllTriggers = true` removes every trigger of the job

<!-- snippet: sample_rescheduling_unschedule_all_triggers -->
```csharp
throw new JobExecutionException($"account {id} no longer exists")
{
    UnscheduleAllTriggers = true,
};
```
<!-- endSnippet -->

::: warning Changed in 4.x
`JobExecutionException` has four constructors — `()`, `(Exception)`, `(string)` and
`(string, Exception)` — and the three flags are **init-only properties** rather than constructor
parameters. The 3.x `new JobExecutionException(msg, cause, refireImmediately)` shapes are gone;
write `new JobExecutionException(ex) { RefireImmediately = true }`.
:::

### Backoff without holding a thread

`RefireImmediately` means *immediately*, and an in-job retry loop — `Task.Delay`, Polly, a
`while` with a sleep — holds a thread-pool slot for the whole backoff. On a scheduler with a pool of
ten, three jobs backing off for a minute each have taken a third of the scheduler for a minute.

When the retry can wait, store a one-off trigger and return normally:

<!-- snippet: sample_rescheduling_retry_trigger -->
```csharp
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    try
    {
        await importer.Run(cancellationToken);
    }
    catch (TransientImportException) when (context.RefireCount == 0)
    {
        ITrigger retry = TriggerBuilder.Create()
            .WithIdentity($"{context.Trigger.Key.Name}-retry-{context.FireInstanceId}", "retries")
            .ForJob(context.JobDetail.Key)
            .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
            .Build();

        await context.Scheduler.ScheduleJob(retry, cancellationToken);
    }
}
```
<!-- endSnippet -->

The trigger name matters. Reuse one fixed retry name and the second retry collides with the first —
`ObjectAlreadyExistsException`, from inside a job, which is a confusing place to debug it. The fire
instance id is unique per firing and makes a good suffix.

A one-off trigger that has fired and has no next fire time is removed by the store, and the job with
it if the job is not durable, so retries do not accumulate.

## Recovering triggers that failed

A trigger whose job threw in a way the scheduler could not recover from lands in
`TriggerState.Error` and stops firing. Finding them is a query — and it pages, so a recovery script
must loop rather than assume one call sees everything:

<!-- snippet: sample_rescheduling_reset_error_state -->
```csharp
TriggerQuery broken = new() { State = TriggerState.Error, Take = 250 };

while (true)
{
    PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(broken, cancellationToken);
    if (page.Items.Count == 0)
    {
        break;
    }

    List<TriggerKey> keys = page.Items.Select(h => h.Key).ToList();
    List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(keys, cancellationToken);
    logger.LogInformation("Reset {Count} triggers", reset.Count);

    if (!page.HasMore)
    {
        break;
    }
}
```
<!-- endSnippet -->

`ResetTriggerFromErrorState(key)` returns `true` when the trigger existed *and* was in the error state,
`false` for a key that names nothing or a trigger that was not in error — the same missing-key rule
`PauseTrigger`, `ResumeTrigger` and `UnscheduleJob` follow.

`ResetTriggersFromErrorState(keys)` does the whole set in one pass, under one lock and one transaction
on the ADO store, and returns the keys it actually reset, in the order they were given. Keys it did not
apply to are absent, never an error. Resetting raises no scheduler-listener event and signals no
scheduling change; the reset triggers are picked up by the next acquisition cycle.

The reset puts the trigger back to `Normal`, or to `Paused` if its group is paused.

::: tip
Reset is not a fix. A trigger goes into the error state because something about it could not be
processed — most often a job type that no longer resolves. Resetting it without addressing that just
puts it back into error on the next fire.
:::

## See also

- [Job Template](job-template.md) — the job skeleton these snippets fit into
- [Querying Jobs and Triggers](../tutorial/querying-jobs-and-triggers.md) — paging, filters and the counting idiom
- [More About Triggers](../tutorial/more-about-triggers.md) — misfire instructions in full
