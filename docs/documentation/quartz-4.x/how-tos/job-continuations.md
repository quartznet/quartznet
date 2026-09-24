---

title: Job Continuations
---

# Job Continuations

A **continuation** is an ordinary trigger that waits in the store for another trigger's firing to end, and is
released or discarded by *how* it ended: "reconcile once tonight's import worked", "page operations if it
did not", "clean up whatever happened".

The store, not a process, holds the wait. Nothing runs and no misfire accrues while it waits. The node that
runs the parent settles the continuation inside the parent's own lock and transaction, so it is never
half-settled, and the node that scheduled it may die without effect.

If the node **running the parent** dies, cluster recovery (persistent store) deletes a one-shot parent, so its
continuations follow [the deleted-parent rule](#when-the-parent-is-deleted): parked in `Error`, or released
if they wait on `OnAnyOutcome`. The recovery firing has its own key and settles nothing. A parent with
firings left keeps its continuations waiting for the next one.

## The model

A trigger with a `Continuation` is stored in `TriggerState.Awaiting` and never acquired there. When the
parent's firing completes:

* An outcome its `ContinuationCondition` names **releases** it:
  * next fire time = the later of *now* and its own start time, moved to the next instant its calendar
    includes if the calendar excludes that time;
  * state = what any trigger stored then would get: `Normal`; `Paused` if its group is paused; `Blocked` if
    its job disallows concurrent execution and is running under another trigger, until that completes;
  * if its end time is before that instant, it has no firing left and is discarded instead.
* Any other outcome **discards** it: the trigger is deleted and its listeners told it is finalized.

Listeners hear of a settlement (`TriggerFinalized` for a discard, `TriggerInError` for a parked
continuation) after it commits and the store's lock is released, so reading the trigger back matches the
notification. When the store runs inside an application-owned transaction, "committed" means the store's
part is done; the commit is the application's.

**Discards cascade.** Continuations waiting on a discarded continuation are **discarded with it**, and
theirs with them, each finalized. In `import → reconcile (OnSuccess) → cleanup (OnAnyOutcome)`, a failed
import discards both. A [deleted parent](#when-the-parent-is-deleted) is different: it is left for an
operator.

**Settlement is one-shot.** Every settling statement names `Awaiting`, so a continuation is released or
discarded exactly once. To follow every firing of a schedule, use
[a recurring chain](#a-recurring-chain-is-a-listener).

**A released continuation is an ordinary trigger**: its `Continuation` is `Continuation.None`, listings no
longer name the parent, `GetTriggerBuilder()` does not re-arm the wait, and a later error and reset keeps its
schedule.

The parent is a `TriggerKey`, not a `JobKey`: a continuation waits for one *firing*, and a job may have
several triggers.

## Declaring one

`StartAfter` sits beside `StartAt` and `StartNow` and composes with a schedule:

<!-- snippet: sample_continuations_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<DataImportJob>(j => j.WithIdentity("import", "nightly"));
    q.AddTrigger<DataImportJob>(t => t
        .WithIdentity("import", "nightly")
        .ForJob("import", "nightly")
        .WithCronSchedule("0 0 2 * * ?"));

    // Reconciliation has no schedule of its own: it runs when the import has run, and only
    // if the import worked. Until then the trigger sits in the store in Awaiting.
    q.AddJob<ReconcileJob>(j => j.WithIdentity("reconcile", "nightly"));
    q.AddTrigger<ReconcileJob>(t => t
        .WithIdentity("reconcile", "nightly")
        .ForJob("reconcile", "nightly")
        .StartAfter(new TriggerKey("import", "nightly")));
});
```
<!-- endSnippet -->

The condition is flags, so combinations need no member of their own:

<!-- snippet: sample_continuations_conditions -->
```csharp
// "Tell operations whenever the import does not get there": a failure the retry policy has
// given up on, or a firing somebody interrupted. A success discards this trigger.
ITrigger alert = TriggerBuilder.Create<AlertOpsJob>(scheduler.TimeProvider)
    .WithIdentity("alert", "nightly")
    .ForJob("alert", "nightly")
    .StartAfter(
        new TriggerKey("import", "nightly"),
        ContinuationCondition.OnFailure | ContinuationCondition.OnCancellation)
    .Build();

await scheduler.ScheduleJob(alert, cancellationToken: cancellationToken);
```
<!-- endSnippet -->

| `ContinuationCondition` | Released by |
|---|---|
| `OnSuccess` (the default) | the parent's job ran and returned |
| `OnFailure` | it ran and threw, with no retry left to take |
| `OnCancellation` | the firing's token was signalled and the job stopped rather than finished |
| `OnVeto` | a trigger listener refused the firing, so the job never ran |
| `OnAnyOutcome` | all four |

A condition naming no outcome is refused.

`StartTimeUtc` stays a **floor**, so "after the import, and never before nine" works:

<!-- snippet: sample_continuations_floor -->
```csharp
// StartAfter composes with the rest of the builder rather than replacing it. The start time
// stays a floor, so a continuation released at 03:00 still waits until 09:00; and the
// schedule is the schedule the released trigger then keeps.
ITrigger report = TriggerBuilder.Create<ReconcileJob>(scheduler.TimeProvider)
    .WithIdentity("report", "nightly")
    .ForJob("reconcile", "nightly")
    .StartAfter(new TriggerKey("import", "nightly"))
    .StartAt(DateTimeOffset.UtcNow.Date.AddDays(1).AddHours(9))
    .Build();

await scheduler.ScheduleJob(report, cancellationToken: cancellationToken);
```
<!-- endSnippet -->

## The parent has to exist

Storing a continuation whose parent is not in the store fails: `ScheduleJob`, `AddTrigger`, `RescheduleJob`
and the batch `ScheduleJobs` throw `ObjectDoesNotExistException` (both keys in the message and as
`TriggerKey` and `MissingTriggerKey`) and store nothing, neither the job scheduled with it nor a
reschedule's change. A misspelled key fails at the call instead of waiting for ever.

The trap is a **one-shot parent that has already fired**: it is deleted when its firing completes, so a
continuation scheduled afterwards has nothing to wait for. **Schedule the continuation before the parent
can finish** (with it, or before it is due), **or check the parent's key.** A `ScheduleJobs` batch may
carry both, in either order.

## The outcome table

| Outcome of the parent's firing | Releases | What it is |
|---|---|---|
| `ExecutionOutcome.Succeeded` | `OnSuccess`, `OnAnyOutcome` | the job ran and returned |
| `ExecutionOutcome.Failed` | `OnFailure`, `OnAnyOutcome` | the job ran and threw |
| `ExecutionOutcome.Cancelled` | `OnCancellation`, `OnAnyOutcome` | the firing's token was signalled and the job stopped |
| `ExecutionOutcome.Vetoed` | `OnVeto`, `OnAnyOutcome` | a trigger listener refused the firing |
| `ExecutionOutcome.NotExecuted` | nothing; the continuation keeps waiting | the occurrence did not happen |

`NotExecuted`: a listener abandoned the occurrence, the job could not be built, or the scheduler could not
dispatch it.

**A retry settles nothing.** A failure the [retry policy](retrying-failed-jobs.md) answers with another
attempt is still `Failed`, but every store skips settling on `SchedulerInstruction.RetryTrigger`. So
`OnSuccess` survives a first failure, and `OnFailure` is released only when the attempts are spent.

## When the parent is deleted

When a parent is removed (`UnscheduleJob`, `DeleteJob` or any other trigger deletion) while continuations
wait, there is no outcome:

* `OnAnyOutcome` continuations are released anyway.
* Narrower ones are parked in `TriggerState.Error`, with `ISchedulerListener.TriggerInError`, for an
  operator, rather than deleted or left waiting.

This does not apply to a continuation *discarded* by its parent's outcome; that [cascades](#the-model).

For a parked continuation:

* `ResetTriggerFromErrorState` gives it the fire time a release would, so **resetting runs it**, and clears
  the parent, making it an ordinary trigger.
* `PauseTrigger` on an `Awaiting` trigger returns `false`: it is already held.
* Siblings do not move it: when another trigger of the job ends in `SetAllJobTriggersError` or
  `SetAllJobTriggersComplete` (the job could not be built, or asked to unschedule all its triggers), an
  `Awaiting` trigger is left for its parent.

## Seeing what is waiting

Trigger listings carry the continuation, without loading each trigger:

<!-- snippet: sample_continuations_listing -->
```csharp
PagedResult<TriggerHeader> waiting = await scheduler.QueryTriggers(
    new TriggerQuery { State = TriggerState.Awaiting },
    cancellationToken);

foreach (TriggerHeader trigger in waiting.Items)
{
    // A listing says what each one is waiting for and what releases it, without loading a
    // trigger per row.
    logger.LogInformation(
        "{Trigger} is waiting for {Parent} ({Condition})",
        trigger.Key,
        trigger.ContinuesAfter,
        trigger.ContinuationCondition);
}
```
<!-- endSnippet -->

* [Dashboard](../packages/dashboard.md#continuations): an **Awaiting only** filter on the trigger listing;
  the detail page shows the parent and releasing outcomes.
* [HTTP API](../packages/http-api.md#continuations): state `"Awaiting"`; the header carries
  `continuesAfterTriggerName`, `continuesAfterTriggerGroup` and `continuationCondition`.

## One call, for a firing whose time is another firing's completion

For a job with [a typed input](../tutorial/job-data-map.md#a-typed-input-the-third-read-side), the
[one-off overloads](one-off-job.md) take a `Continuation` instead of a time:

<!-- snippet: sample_continuations_one_off -->
```csharp
// One firing of the import, six hours from now. The key it answers with is the handle.
ScheduledOneOffJob import = await scheduler.ScheduleJob<DataImportJob, ImportRequest>(
    new ImportRequest("eu-west"),
    TimeSpan.FromHours(6),
    cancellationToken: cancellationToken);

// And one firing of the reconciliation after it. There is no time argument, because the
// time is the import's completion.
ScheduledOneOffJob reconcile = await scheduler.ScheduleJob<ReconcileJob, ImportRequest>(
    new ImportRequest("eu-west"),
    Continuation.After(import.TriggerKey),
    cancellationToken: cancellationToken);
```
<!-- endSnippet -->

## Declaring one in a scheduling file

Both scheduling-file formats accept a continuation on any trigger they can declare. XML:

```xml
<trigger>
  <cron>
    <name>reconcile</name>
    <group>nightly</group>
    <job-name>reconcileJob</job-name>
    <continues-after>
      <name>import</name>
      <group>nightly</group>
    </continues-after>
    <continuation-condition>OnFailure|OnCancellation</continuation-condition>
    <cron-expression>0 0 2 * * ?</cron-expression>
  </cron>
</trigger>
```

JSON (`quartz_jobs.json` or the `Quartz:Schedule` section of `appsettings.json`):

```json
{
  "Name": "reconcile",
  "Group": "nightly",
  "JobName": "reconcileJob",
  "ContinuesAfter": { "Name": "import", "Group": "nightly" },
  "ContinuationCondition": "OnFailure|OnCancellation",
  "Cron": { "Expression": "0 0 2 * * ?" }
}
```

* Omitted `Group`: the default group. Omitted condition: `OnSuccess`.
* The parent is **named, not resolved at read time**: it may come later in the file (parents are stored
  first) or already be in the store. One in neither is refused when the file is scheduled, as
  [above](#the-parent-has-to-exist).
* An unknown outcome, or a condition with nothing to wait for, is refused when the file is read, naming the
  trigger.

## A recurring chain is a listener

For "run the cleanup **whenever** the nightly job fails", use
[`JobChainingJobListener`](../tutorial/trigger-and-job-listeners.md), which takes the same conditions:

<!-- snippet: sample_continuations_chaining_listener -->
```csharp
JobChainingJobListener chain = new("nightly-chain");

// Every time the import fails, run the alert. A listener link fires on every completion,
// where a continuation settles once - which is the difference between the two.
chain.AddJobChainLink(
    new JobKey("import", "nightly"),
    new JobKey("alert", "nightly"),
    ContinuationCondition.OnFailure);

builder.Services.AddQuartz(q => q.AddJobListener(chain));
```
<!-- endSnippet -->

| | Listener link | Continuation |
|---|---|---|
| Lives | in the process, on the node that ran the first job | as a row in the store |
| After a restart | re-registered on every start | survives |
| Follow-up | *fired*, with no trigger to see, pause or query | a trigger, settled by whichever node ran the parent |

## Upgrading a running cluster

Continuation columns arrived in 4.2. A 4.1 node in the cluster:

* cannot settle one: a parent completing there leaves its continuations waiting;
* reads `AWAITING` as waiting, so its single-trigger `PauseTrigger` writes `PAUSED` over a continuation,
  which a resume then starts without its parent;
* rewrites a continuation as an ordinary trigger on reschedule.

Order:

1. Run [the migration](../../database/schema-changes.md#version-4-2). Safe with 4.1 nodes running: the
   columns are nullable with no default.
2. Roll **every** node to 4.2. A 4.2 node refuses to start on an unmigrated database, naming the column and
   script.
3. Only then schedule continuations. While any 4.1 node runs, do not pause, resume or reschedule a
   continuation from it.

See the [migration guide](../migration-guide.md#the-4-2-schema-migration).

## See also

* [One-Off Job](one-off-job.md) — the typed one-call overloads, of which the continuation form is one
* [Retrying Failed Jobs](retrying-failed-jobs.md) — what a retry is, and why it settles nothing
* [More About Triggers](../tutorial/more-about-triggers.md) — misfire instructions, priorities and calendars
* [Trigger and Job Listeners](../tutorial/trigger-and-job-listeners.md) — `JobChainingJobListener`, and what a veto is
