---

title: Backfill
---

# Backfill

`Backfill` runs a trigger's schedule over a past range you choose: one firing of its job for each fire time
the trigger had in the range. From 4.3.

<!-- snippet: sample_backfill_three_nights -->
```csharp
// The nightly export did not run on the 1st, 2nd and 3rd: run each of those nights now,
// five minutes apart.
BackfillResult result = await scheduler.Backfill(
    new TriggerKey("nightly-export", "reports"),
    from: new DateTimeOffset(2026, 9, 1, 0, 0, 0, TimeSpan.Zero),
    to: new DateTimeOffset(2026, 9, 4, 0, 0, 0, TimeSpan.Zero),
    new BackfillOptions { Spacing = TimeSpan.FromMinutes(5) },
    cancellationToken);

Console.WriteLine($"{result.SlotsFound} slots: {result.Scheduled} scheduled, {result.AlreadyScheduled} already scheduled");
```
<!-- endSnippet -->

## Backfill or misfire

| | Misfire instruction | Backfill |
|---|---|---|
| For | fire times the scheduler missed while down or busy | a range you name, afterwards |
| Decided | on the trigger, ahead of time | by the call |
| Fires | what the instruction says: none, one, or every missed time | once per slot in the range |

See [Misfire Instructions](../tutorial/more-about-triggers.md#misfire-instructions).

## Which slots

A slot is a fire time of the trigger in `[from, to)`:

* `from` is included; `to` is excluded.
* The trigger's start and end time bound the range. There are no slots before its start time.
* A time the trigger's calendar excludes is not a slot.

A trigger builder sets the start time to the moment it builds unless told otherwise. To backfill from before a
cron trigger was scheduled, give it a start time in the past; its first fire time is then past too, and its
misfire instruction handles it. A simple trigger stored with a start in the past, or rescheduled by a misfire,
starts from then instead.

## What is scheduled

One one-shot trigger per slot, for the same job:

| | |
|---|---|
| Group | `backfill:` and the trigger's group; `SchedulerConstants.BackfillGroupPrefix` |
| Name | the trigger's name, `@`, the slot in UTC: `nightly-export@2026-09-01T02:00:00Z` |
| Job data | the trigger's, plus the slot under `SchedulerConstants.BackfillOriginalFireTime` |
| Priority, retry policy, preferred node | the trigger's |
| Execution group | `BackfillOptions.ExecutionGroup`, else the trigger's |
| Calendar | none; the slot has been checked against it |
| Misfire instruction | `FireNow` |
| Start time | now, plus the slot's index times `BackfillOptions.Spacing` |

A `[DisallowConcurrentExecution]` job runs its backfilled firings one at a time.

## The slot in the job

A backfilled firing runs now, so `ScheduledFireTimeUtc` is not the slot. `GetBackfillSlot()` is, and is `null`
on a firing that is not a backfill:

<!-- snippet: sample_backfill_slot_reader -->
```csharp
public sealed class NightlyExportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // A backfilled firing runs now: the night it is for is its slot.
        DateTimeOffset night = context.GetBackfillSlot() ?? context.ScheduledFireTimeUtc ?? context.FireTimeUtc;
        return Export(night, cancellationToken);
    }

    private static ValueTask Export(DateTimeOffset night, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
```
<!-- endSnippet -->

## Which occurrence a firing serves

`context.GetOccurrence()` says which occurrence a firing serves and why it runs, so a consumer that
deduplicates per occurrence can derive a stable key. From 4.8.

| `Origin` | When | `OccurrenceUtc` |
|---|---|---|
| `Backfill` | `GetBackfillSlot()` is not `null` | the slot |
| `Manual` | `TriggerJob` asked for the run | when the run was asked for |
| `Schedule` | otherwise | the fire time the schedule named |

* `OccurrenceUtc` and `Origin` are the same on every retry and on a recovery. `RetryAttempt` and `Recovering`
  say which attempt it is.
* A misfire catch-up is `Schedule`, with the missed time. A late `FireTimeUtc` shows how late it ran.
* A backfill of a trigger `TriggerJob` stored carries both marks, and is a `Backfill`.
* Two backfills of one slot share `OccurrenceUtc` and `context.Trigger.Key`: a backfill names its trigger
  after the slot. `ScheduledFireTimeUtc`, when each backfill started its firing, tells them apart, and is
  the same on that backfill's retries.
* A recovery of a retry or a catch-up serves the time that attempt was due, with `RetryAttempt` `0`
  ([#4272](https://github.com/quartznet/quartznet/issues/4272)).
* Quartz formats no key. The format is yours:

<!-- snippet: sample_firing_occurrence_key -->
```csharp
public sealed class PublishSettlementJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        FiringOccurrence occurrence = context.GetOccurrence();

        // The schedule: this trigger, or on a recovery the trigger it stands in for.
        TriggerKey schedule = context.RecoveringTriggerKey ?? context.Trigger.Key;

        // One key per occurrence: the same on its retries and on its recovery.
        string key = occurrence.Origin switch
        {
            FiringOrigin.Schedule => $"{schedule}:{occurrence.OccurrenceUtc:O}",
            FiringOrigin.Manual => $"{schedule}:manual:{occurrence.OccurrenceUtc:O}",

            // Two backfills of one slot share the slot and the trigger key: when each started tells them apart.
            FiringOrigin.Backfill => $"{schedule}:backfill:{occurrence.OccurrenceUtc:O}:{context.ScheduledFireTimeUtc:O}",
            _ => throw new UnreachableException()
        };

        return Publish(key, cancellationToken);
    }

    private static ValueTask Publish(string deduplicationKey, CancellationToken cancellationToken) => ValueTask.CompletedTask;
}
```
<!-- endSnippet -->

## Options

| `BackfillOptions` | Default | |
|---|---|---|
| `MaxSlots` | `1000` | A range holding more is refused whole |
| `Spacing` | zero | Time between the firings' starts. At zero they start together, in the order the store acquires them |
| `ExecutionGroup` | the trigger's | The group the firings count against. A name, stored as written |

## Refusals

Each is raised before anything is scheduled.

| Case | Exception |
|---|---|
| `to` is not after `from` | `ArgumentException` |
| `to` is after now on the scheduler's clock | `ArgumentException`; the trigger fires the future itself |
| More slots than `MaxSlots` | `ArgumentException`, naming the count |
| An option out of range | `ArgumentException` |
| No such trigger | `ObjectDoesNotExistException` |

## Running a range again

* A slot whose trigger is still stored, waiting, paused or firing, is skipped and counted in
  `AlreadyScheduled`.
* A slot whose trigger has fired is no longer stored, and is scheduled again.
* The slots are scheduled one call each. A failure or cancellation part way keeps those already scheduled;
  run the range again for the rest.

## Over HTTP and in the dashboard

| Where | How |
|---|---|
| HTTP API | `POST …/triggers/{triggerGroup}/{triggerName}/backfill`; see [Backfilling a trigger](../packages/http-api.md#backfilling-a-trigger) |
| `HttpScheduler` | One request to that route, whatever the slot count: the host's clock decides "now", and the host audits one `BackfillTrigger` |
| Dashboard | *Backfill…* on a trigger's page; see [Backfilling a trigger](../packages/dashboard.md#backfilling-a-trigger) |

## A mixed 4.2 and 4.3 cluster

The slots are plain one-shot simple triggers, so a 4.2 node fires them. Only the slot key is new: a job on a
4.2 node reads it from `context.Trigger.JobDataMap` as a string. A 4.2 HTTP host has no `backfill` route and
answers it `404`; an `HttpScheduler` then backfills through the routes 4.2 has, a request per slot.
