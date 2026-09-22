---

title: Job Continuations
---

# Job Continuations

A **continuation** is an ordinary trigger that waits, in the store, for another trigger's firing to end —
and is released or discarded by *how* it ended. "Reconcile the ledger once tonight's import has worked",
"page operations if it did not", "clean up whatever happened".

The wait is held by the job store, not by the process that arranged it. Nothing is running while a
continuation waits, no misfire accrues, and whichever node runs the parent is the node that settles the
continuation — inside the parent's own lock and transaction, so it is never half-settled, and the death of
the node that scheduled it changes nothing.

The death of the node **running the parent** is the case to know. With a persistent store, a one-shot
parent lost with its node is deleted by cluster recovery, so its continuations are settled by
[the deleted-parent rule](#when-the-parent-is-deleted) — parked in `Error`, or released if they wait on
`OnAnyOutcome` — and the recovery firing, which runs under a key of its own, settles nothing. A parent with
firings left keeps its continuations waiting for the next one.

## The model

A trigger carrying a `Continuation` is stored in `TriggerState.Awaiting` and is never acquired while it is
there. When the parent's firing completes:

* an outcome the continuation's `ContinuationCondition` names **releases** it, with its next fire time set to
  the later of *now* and its own start time — moved on to the next instant its calendar includes, if it
  names one that excludes that — into the state any trigger stored at that moment would get: `Normal`;
  `Paused` if its group is; `Blocked` if its job disallows concurrent execution and is running under
  another trigger, until that execution completes. A continuation whose end time is behind that instant
  has no firing left, and is discarded as below instead;
* any other outcome **discards** it: the trigger is deleted and its listeners told it is finalized, because
  the firing it was waiting for has been and gone.

Listeners hear of a settlement — `TriggerFinalized` for a discard, `TriggerInError` for a parked
continuation — once it has committed and the store's lock is released, never from inside it, so a listener
that reads the trigger back sees what the notification says. (Where the store works inside a transaction
the application owns, "committed" is the store's part of it being done: the commit is the application's.)

A discarded continuation never runs, so nothing waiting on it can ever be satisfied either: the
continuations waiting on it are **discarded with it**, and theirs with them, each finalized. In a chain
`import → reconcile (OnSuccess) → cleanup (OnAnyOutcome)`, an import that fails discards both — the cleanup
is not released for a reconciliation that never took place. That is the difference from
[a parent that is deleted](#when-the-parent-is-deleted), which is a question for an operator.

Settlement is **one-shot**. Every statement that settles a continuation names `Awaiting`, and a settled
trigger no longer holds it, so a continuation is released or discarded exactly once. A continuation is not
a subscription to a schedule — for that, see [a recurring chain](#a-recurring-chain-is-a-listener) below.

A released continuation is an **ordinary trigger** from then on. The release clears what it waited for, so
its `Continuation` is `Continuation.None`, a listing no longer names the parent, `GetTriggerBuilder()` does
not re-arm the wait for a firing that has been, and a later error and reset keeps whatever schedule the
trigger is on.

The parent is a `TriggerKey` rather than a `JobKey`: a continuation waits for one *firing*, and a job may
be fired by several triggers.

## Declaring one

`StartAfter` sits beside `StartAt` and `StartNow` on the trigger builder, and composes with a schedule
rather than replacing one:

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

The condition is flags, so "whenever it did not get there" needs no member of its own:

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

A condition that names no outcome at all is refused: it would discard the trigger whatever the parent did,
which is a schedule nobody means to write.

`StartTimeUtc` stays a **floor** rather than a schedule, so "an hour after the import, and never before
nine" is expressible:

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

A continuation is refused when it is stored if its parent is not in the store: `ScheduleJob`,
`AddTrigger`, `RescheduleJob` and the batch `ScheduleJobs` throw `ObjectDoesNotExistException`, naming both
keys in its message and as `TriggerKey` and `MissingTriggerKey`, and store nothing — not the job scheduled
beside the trigger, and not a change to the trigger a reschedule would have replaced. A continuation of a
trigger that does not exist would wait for a firing that can never happen, and a trigger that waits for
ever is one nobody notices; the refusal turns a misspelled key into an error at the call that made it.

The case that is easy to walk into is a **one-shot parent that has already fired**: a trigger with nothing
left to fire is deleted once its firing completes, so a continuation scheduled after that finds nothing to
wait for. **Schedule the continuation before the parent can finish** — together with it, or before it is
due — **or check the parent's key.** A batch passed to `ScheduleJobs` may carry the parent and the
continuation together, in either order.

## The outcome table

The outcome says what the firing *did*:

| Outcome of the parent's firing | Releases | What it is |
|---|---|---|
| `ExecutionOutcome.Succeeded` | `OnSuccess`, `OnAnyOutcome` | the job ran and returned |
| `ExecutionOutcome.Failed` | `OnFailure`, `OnAnyOutcome` | the job ran and threw |
| `ExecutionOutcome.Cancelled` | `OnCancellation`, `OnAnyOutcome` | the firing's token was signalled and the job stopped |
| `ExecutionOutcome.Vetoed` | `OnVeto`, `OnAnyOutcome` | a trigger listener refused the firing |
| `ExecutionOutcome.NotExecuted` | nothing | the occurrence did not happen — a listener abandoned it, the job could not be built, the scheduler could not dispatch it. The continuation keeps waiting |

**A retry settles nothing.** A failure the trigger's [retry policy](retrying-failed-jobs.md) answers with
another attempt is still reported as `Failed` — the job did run and it did throw — but the occurrence is
not over, and what says so is the *instruction* rather than the outcome: every store skips settling on
`SchedulerInstruction.RetryTrigger`. So a continuation waiting on `OnSuccess` survives the parent's first
hiccup, and one waiting on `OnFailure` is released when the attempts are spent rather than at the first of
them.

## When the parent is deleted

A parent removed while continuations await it — by `UnscheduleJob`, `DeleteJob` or anything else that
deletes a trigger — is the one settlement with no outcome to match. A continuation that did not care how
the firing ended — `OnAnyOutcome` — is released anyway; anything narrower is parked in
`TriggerState.Error`, with `ISchedulerListener.TriggerInError`, for an operator to see rather than silently
deleted or left waiting forever. A continuation *discarded* by its parent's outcome is not a deletion in
this sense: what waits on it is discarded with it, as above.

`ResetTriggerFromErrorState` on such a trigger gives it the fire time a release would have given it, so
**resetting one means running it** — and, like a release, the reset clears the parent it named, so from
then on it is an ordinary trigger. `PauseTrigger` on a trigger that is still `Awaiting` answers `false`:
there is nothing to hold back that is not already held back. Nor does a sibling move it: when another
trigger of the same job ends in `SetAllJobTriggersError` or `SetAllJobTriggersComplete` — the job could
not be built, or it asked for all of its triggers to be unscheduled — an `Awaiting` trigger is left where it
is, for its parent to settle.

## Seeing what is waiting

A trigger listing carries the continuation, so "why is this not running" is answerable without
materializing a trigger per row:

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

The [dashboard](../packages/dashboard.md#continuations) has an **Awaiting only** filter on its trigger
listing, and the trigger's detail page shows what it continues after and which outcomes release it. Over
the [HTTP API](../packages/http-api.md#continuations) the state is `"Awaiting"` and the header carries
`continuesAfterTriggerName`, `continuesAfterTriggerGroup` and `continuationCondition`.

## One call, for a firing whose time is another firing's completion

When the job takes [a typed input](../tutorial/job-data-map.md#a-typed-input-the-third-read-side), the
[one-off overloads](one-off-job.md) have a form with no time argument — because the time is the parent's
completion:

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

A continuation is a *setting* rather than a kind of trigger, so both scheduling-file formats take it on
any trigger they can already declare. In XML:

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

and in JSON — a standalone `quartz_jobs.json` or the `Quartz:Schedule` section of `appsettings.json`:

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

An omitted `Group` is the default group and an omitted condition is `OnSuccess`. The parent is **named,
not resolved as the file is read**, so it may be declared later in the same file — the file's triggers are
stored parent first, whatever their order — or not be in the file at all because it is already in the
store. A parent that is in neither is refused when the file is scheduled, as
[any continuation of a missing trigger](#the-parent-has-to-exist) is. An outcome that is not one — or a
condition with nothing to wait for beside it — is refused as the file is read, naming the trigger.

## A recurring chain is a listener

A continuation settles once. "Run the cleanup **whenever** the nightly job fails" is not that, and it is
not a continuation: it is
[`JobChainingJobListener`](../tutorial/trigger-and-job-listeners.md), which learned the same vocabulary:

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

The differences are worth stating plainly. A link is a process-local arrangement on the node that ran the
first job, re-registered on every start, and the follow-up is *fired* rather than scheduled — there is no
trigger to see, pause or query. A continuation is a row in the store that survives a restart and is
settled by whichever node ran the parent.

## Upgrading a running cluster

The columns a continuation lives in arrived in 4.2, and a 4.1 node cannot settle one: a parent completing
there leaves the triggers waiting on that firing exactly where they are. Nor does a 4.1 node know the
`AWAITING` state — it reads it as waiting — so a single-trigger `PauseTrigger` from one writes `PAUSED` over
a continuation, which a resume then sets off without its parent, and a 4.1 reschedule rewrites it as an
ordinary trigger.

So the order is: run
[the migration](../../database/schema-changes.md#version-4-2), roll **every** node to 4.2, and only then start
scheduling continuations; while any 4.1 node is still running, do not pause, resume or reschedule a
continuation from it — which a cluster that schedules its first continuation after the last node has rolled
never has to think about. Rolling the migration itself while 4.1 nodes are still running is safe — the
columns are nullable with no default — and a 4.2 node refuses to start against a database that has not
taken it, naming the column and the script. The
[migration guide](../migration-guide.md#the-4-2-schema-migration) has the whole of it.

## See also

* [One-Off Job](one-off-job.md) — the typed one-call overloads, of which the continuation form is one
* [Retrying Failed Jobs](retrying-failed-jobs.md) — what a retry is, and why it settles nothing
* [More About Triggers](../tutorial/more-about-triggers.md) — misfire instructions, priorities and calendars
* [Trigger and Job Listeners](../tutorial/trigger-and-job-listeners.md) — `JobChainingJobListener`, and what a veto is
