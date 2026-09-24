---
title: 'Rescheduling Jobs'
---

# Rescheduling Jobs

Three operations get called rescheduling. Pick the wrong one and a trigger loses its fire history, or a
priority change resets the next fire time.

| You want to | Use | Fire times |
|---|---|---|
| Change **when** the job runs | `RescheduleJob` | recomputed from the new trigger |
| Change the trigger's **metadata** | `UpdateTriggerDetails` | preserved |
| Retry **this firing** | `JobExecutionException { RefireImmediately = true }` | untouched |

## Changing the schedule: RescheduleJob

`RescheduleJob` deletes the old trigger and stores the new one, which must name the same job:

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

* A different `WithIdentity` renames the trigger.
* The new trigger must carry a job key; the old one is gone before it is stored.
* It returns the first fire time, or **`null` if the old trigger was not found**, in which case nothing is
  stored. To handle a missing trigger:

<!-- snippet: sample_rescheduling_missing_trigger -->
```csharp
DateTimeOffset? next = await scheduler.RescheduleJob(key, replacement, cancellationToken);
if (next is null)
{
    // the old trigger was gone; store the new one on its own terms
    await scheduler.ScheduleJob(replacement, cancellationToken: cancellationToken);
}
```
<!-- endSnippet -->

Everything derived from the old trigger resets: `PreviousFireTimeUtc` is empty, a `SimpleTrigger`'s repeat
count starts over, and a paused trigger takes whatever state its new group implies.

A firing already running is not touched: it completes as the old trigger, and in a persistent store its
fired-trigger record stays until then, so a job that requested recovery is still recovered if the node dies.
This matters because `AddJob` and `AddTrigger` registrations are re-applied as a reschedule on every process
start, before the scheduler starts and recovery runs. Unscheduling, by contrast, removes its executions'
records, and nothing is recovered.

## Changing metadata in place: UpdateTriggerDetails

`UpdateTriggerDetails` patches a stored trigger; fire times and state are kept (paused stays paused, due in
ten minutes stays due in ten minutes):

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

`TriggerDetailsUpdate` is a **patch**: only properties you set change. `WithCalendarName(null)` removes the
calendar; not calling `WithCalendarName` keeps it.

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

* Returns `true` if the trigger was found and updated, `false` if the key names nothing.
* A new misfire instruction applies the next time the trigger is late.
* A new execution group applies from the next acquisition cycle; a running job keeps counting against the
  group it was acquired under.

### Misfire instructions are validated against the trigger's family

The same code means different policies per family (`1` is `FireNow` on a simple trigger, `FireOnceNow` on a
cron trigger). The typed overloads carry the family, and the store rejects a mismatch:

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

`WithMisfireInstructionCode(int)`, for a bare number from the wire, configuration or
`ITrigger.MisfireInstructionCode`, skips the check. Prefer the typed overloads.

::: warning Changed in 4.x
All five schedule builders spell this `WithMisfireInstruction`. `WithMisfireHandlingInstruction…` and the
`MisfireInstruction.*` constant class are gone. The typed enums (`SimpleTriggerMisfireInstruction`,
`CronTriggerMisfireInstruction`, and the three others) are the public vocabulary.
:::

## Choosing between them

* The **schedule** changed (cron expression, interval, end date): `RescheduleJob`. A schedule cannot be
  edited in place; it is the trigger.
* Anything in the table above: `UpdateTriggerDetails`, one statement, no fire-time change, no rebuilt
  trigger.

Reading, rebuilding `GetJobBuilder`-style and storing back is a `RescheduleJob` and resets the same state.

## Retrying inside the job

A job can ask to run again immediately after a transient failure:

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

`RefireImmediately` re-runs the same firing at once, on the same thread-pool slot. Guard on
`context.RefireCount`, or a job that always fails becomes a hot loop.

For failures not worth retrying:

* `UnscheduleFiringTrigger = true` removes the trigger that fired
* `UnscheduleAllTriggers = true` removes every trigger of the job

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

An in-job retry loop (`Task.Delay`, Polly, a sleeping `while`) holds a pool slot through the backoff: three
jobs backing off for a minute on a pool of ten take a third of the scheduler. If the retry can wait, schedule
a one-off trigger and return:

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

        await context.Scheduler.ScheduleJob(retry, cancellationToken: cancellationToken);
    }
}
```
<!-- endSnippet -->

* Name each retry trigger uniquely (the fire instance id is a good suffix). A fixed name makes the second
  retry throw `ObjectAlreadyExistsException` inside the job.
* The store removes a fired one-off trigger with no next fire time, and a non-durable job with it, so
  retries do not pile up.

## Recovering triggers that failed

A trigger whose job failed in a way the scheduler could not handle goes to `TriggerState.Error` and stops.
Query for them, looping over pages:

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

* `ResetTriggerFromErrorState(key)` returns `true` only if the trigger exists *and* was in error; `false`
  otherwise, the same missing-key rule as `PauseTrigger`, `ResumeTrigger` and `UnscheduleJob`.
* `ResetTriggersFromErrorState(keys)` resets the set in one pass (one lock and transaction on the ADO store)
  and returns the keys it reset, in the given order; others are simply absent.
* A reset raises no scheduler-listener event and signals nothing; the next acquisition cycle picks it up.
* The trigger returns to `Normal`, or `Paused` if its group is paused.

::: tip
Reset is not a fix. Fix the cause first (most often a job type that no longer resolves), or the trigger
returns to error on its next fire.
:::

## See also

* [Job Template](job-template.md) — the job skeleton these snippets fit into
* [Querying Jobs and Triggers](../tutorial/querying-jobs-and-triggers.md) — paging, filters and the counting idiom
* [More About Triggers](../tutorial/more-about-triggers.md) — misfire instructions in full
