---

title: One-Off Job
---

# One-Off Job

To run a job once, now or at a chosen time, trigger a stored job on demand or schedule a job and trigger
built on the spot.

## A job registered ahead of time, triggered on demand

Register the job at startup:

<!-- snippet: sample_one_off_job_durable_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<AnExampleJob>(j => j
        .WithIdentity("name", "group")
        .StoreDurably());
});
```
<!-- endSnippet -->

`StoreDurably()` keeps a job with no triggers in the store; otherwise it is deleted when it has none.

Trigger it from anywhere with the scheduler. `TriggerJob` fires it once, immediately, and leaves nothing
behind:

<!-- snippet: sample_one_off_job_trigger_now -->
```csharp
public async ValueTask RunNow(IScheduler scheduler, CancellationToken cancellationToken)
{
    await scheduler.TriggerJob(new JobKey("name", "group"), cancellationToken: cancellationToken);
}
```
<!-- endSnippet -->

A `JobDataMap` passed to it is merged over the job's data for this firing only:

<!-- snippet: sample_one_off_job_trigger_now_with_data -->
```csharp
public async ValueTask RunNow(IScheduler scheduler, string customer, CancellationToken cancellationToken)
{
    JobDataMap data = new() { { "CustomerId", customer } };
    await scheduler.TriggerJob(new JobKey("name", "group"), data, cancellationToken);
}
```
<!-- endSnippet -->

For a job not registered at startup, use `AddJob`:

<!-- snippet: sample_one_off_job_add_job -->
```csharp
IJobDetail job = JobBuilder.Create<AnExampleJob>()
    .WithIdentity("name", "group")
    .StoreDurably()
    .Build();

await scheduler.AddJob(job, new AddJobOptions { Replace = true }, cancellationToken);
```
<!-- endSnippet -->

* `Replace` allows re-registering a taken name; without it the call throws `ObjectAlreadyExistsException`.
* `StoreNonDurableWhileAwaitingScheduling` stores a *non*-durable job with no trigger, expecting one. Once a
  trigger arrives the job is ordinary, deleted when it has no triggers left.

## A job and a trigger built on the spot

<!-- snippet: sample_one_off_job_schedule_once -->
```csharp
public async ValueTask ScheduleOnce(IScheduler scheduler, CancellationToken cancellationToken)
{
    IJobDetail job = JobBuilder.Create<AnExampleJob>()
        .WithIdentity("name", "group")
        .Build();

    ITrigger trigger = TriggerBuilder.Create()
        .WithIdentity("name", "group")
        .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
        .Build();

    await scheduler.ScheduleJob(job, trigger, cancellationToken: cancellationToken);
}
```
<!-- endSnippet -->

No `StoreDurably()`: both are removed once the trigger has fired and has nothing left to do.

A trigger with no schedule fires once, at its start time; `StartNow()` means as soon as the scheduler gets to
it. A bare `.WithSimpleSchedule()` changes nothing, so add one only to configure it:

<!-- snippet: sample_one_off_job_misfire_instruction -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("name", "group")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithMisfireInstruction(SimpleTriggerMisfireInstruction.FireNow))
    .Build();
```
<!-- endSnippet -->

::: tip Misfire behaviour
A one-shot trigger on the default `SmartPolicy` resolves to `FireNow`: a firing missed while the scheduler
was down runs when it returns. See [SimpleTriggers](../tutorial/simpletriggers.md#simpletrigger-misfire-instructions).
:::

## A payload and a time, in one call

For a job with [a typed input](../tutorial/job-data-map.md#a-typed-input-the-third-read-side), pass the job
type, the input and the time:

<!-- snippet: sample_one_off_job_typed_one_liner -->
```csharp
public sealed record SendInvoice(string CustomerId, decimal Amount);

public sealed class SendInvoiceJob : IJob<SendInvoice>
{
    public ValueTask Execute(IJobExecutionContext context, SendInvoice input, CancellationToken cancellationToken = default)
    {
        // input.CustomerId, input.Amount
        return default;
    }
}

public sealed class Invoicing
{
    public async ValueTask Remind(IScheduler scheduler, ILogger logger, SendInvoice invoice, CancellationToken cancellationToken)
    {
        ScheduledOneOffJob firing = await scheduler.ScheduleJob<SendInvoiceJob, SendInvoice>(
            invoice,
            TimeSpan.FromDays(7),
            // Named, so it can be replaced or cancelled; grouped by the thing it is about, so the
            // whole conversation can be cancelled at once. Replacing(name) is the preset for the
            // pair, because a firing with no name of its own has nothing to replace.
            OneOffJobOptions.Replacing($"invoice-{invoice.CustomerId}") with { Group = invoice.CustomerId },
            cancellationToken);

        // What was arranged: the trigger's key, and when the store says it will first fire.
        logger.LogInformation("Reminder {Trigger} scheduled for {At}", firing.TriggerKey, firing.FirstFireTimeUtc);

        // ... and to call it off:
        await scheduler.UnscheduleJob(firing.TriggerKey, cancellationToken);
    }
}
```
<!-- endSnippet -->

One overload takes a `DateTimeOffset`, the other a `TimeSpan` from now. Both return a `ScheduledOneOffJob`:

| Member | Meaning |
|---|---|
| `TriggerKey` | the stored firing; cancel it with this, or replace it by scheduling the same name again |
| `FirstFireTimeUtc` | when the store says it will fire |

Read anything else with `GetTrigger`.

What is stored:

* **One durable job per job type** at `SchedulerConstants.ScheduledJobKey<TJob>()`, which is
  `(typeof(TJob).Name, SchedulerConstants.ScheduledJobGroup)`, plus one trigger per call, so there is no job
  churn however many firings are pending.
* The job is stored idempotently on the first call on a scheduler, then remembered: safe from several nodes
  at once, and one round trip instead of two from the second call.
* It stays after the last firing is cancelled: one row per job type.

`OneOffJobOptions` holds the `TriggerBuilder` settings: `Name` and `Group` (by default a generated
identifier, in a group named after the job type), `Description`, `Priority`, `ExecutionGroup`,
`MisfireInstruction`, and `Replace`. **The group is the correlation axis**: give one saga, tenant or
conversation one group to list, pause or unschedule its firings together.

::: warning A group default that a cancellation contract has to know about
`Group` defaults to the job type's name, not `TriggerKey.DefaultGroup`. Code cancelling with
`new TriggerKey(id)` targets the *default* group, so it silently finds nothing for firings scheduled here
without `Group = TriggerKey.DefaultGroup`. An integration moving to the one-liner under an existing
trigger-key contract must set the group its callers expect.
:::

`RequestRecovery` describes the durable job, not the trigger: the job is marked `RequestsRecovery`, so a
firing cut short by a hard shutdown re-runs when the scheduler returns:

<!-- snippet: sample_one_off_job_request_recovery -->
```csharp
public async ValueTask Remind(IScheduler scheduler, SendInvoice invoice, CancellationToken cancellationToken)
{
    await scheduler.ScheduleJob<SendInvoiceJob, SendInvoice>(
        invoice,
        TimeSpan.FromDays(7),
        new OneOffJobOptions { RequestRecovery = true },
        cancellationToken);
}
```
<!-- endSnippet -->

The job is stored once per scheduler instance, so **the first call's value wins for the life of the
process**, like the job's description, durability and type.

## A schedule of your own on the same job

Point a recurring trigger of your own at `ScheduledJobKey<TJob>()`; do not add your own job to the reserved
group:

<!-- snippet: sample_one_off_job_scheduled_job_key -->
```csharp
public async ValueTask Nightly(IScheduler scheduler, CancellationToken cancellationToken)
{
    // A schedule of its own, pointed at the job the one-liner keeps rather than at a second job
    // built here: same job, same payload shape, one more trigger.
    ITrigger nightly = TriggerBuilder.Create<SendInvoiceJob>(scheduler.TimeProvider)
        .WithIdentity("nightly", "invoicing")
        .ForJob(SchedulerConstants.ScheduledJobKey<SendInvoiceJob>())
        .WithCronSchedule("0 0 2 * * ?")
        .UsingInput(new SendInvoice("all", 0m))
        .Build();

    await scheduler.ScheduleJob(nightly, cancellationToken: cancellationToken);
}
```
<!-- endSnippet -->

* `SchedulerConstants.ScheduledJobGroup` is reserved for the one-liner's jobs, but its job is meant to be
  pointed at.
* The key comes from the type, so it is known before anything is scheduled; the job appears on the first
  one-call overload.
* A store refuses a trigger whose job is missing. If your path can run first, store the job yourself with
  `AddJob` under the same key and `AddJobOptions.Replacing`, which is idempotent with the one-liner.

## A firing whose time is another firing's completion

The third overload takes the `TriggerKey` of an earlier firing instead of a time, and stores a
[continuation](job-continuations.md): held in `TriggerState.Awaiting`, never acquired, and released or
discarded by the first firing's outcome.

<!-- snippet: sample_one_off_job_continuation -->
```csharp
public sealed class InvoiceRun
{
    public async ValueTask Send(IScheduler scheduler, ImportRequest request, CancellationToken cancellationToken)
    {
        ScheduledOneOffJob import = await scheduler.ScheduleJob<DataImportJob, ImportRequest>(
            request,
            TimeSpan.FromMinutes(5),
            cancellationToken: cancellationToken);

        // The second firing's time is the first firing's completion, so the overload takes a
        // Continuation where the others take a DateTimeOffset or a TimeSpan.
        await scheduler.ScheduleJob<ReconcileJob, ImportRequest>(
            request,
            Continuation.After(import.TriggerKey, ContinuationCondition.OnSuccess),
            cancellationToken: cancellationToken);
    }
}
```
<!-- endSnippet -->

* `Continuation.After(parent)` waits for success; pass a
  [`ContinuationCondition`](job-continuations.md#declaring-one) for others, such as `OnAnyOutcome` for a
  cleanup.
* For a floor ("and never before nine"), build the trigger by hand with `StartAfter` and `StartAt`.

## Reading input written by an older schema

`IJob<TInput>` fails a firing whose input is missing. Midway through converting a 3.x job, the store still
holds triggers with the payload in flat `JobDataMap` keys and nothing under `QRTZ_JOB_INPUT`, so each would
throw. Keep the job an `IJob` while both shapes exist:

<!-- snippet: sample_one_off_job_try_get_input -->
```csharp
public sealed class SendInvoiceCompatJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // A firing scheduled by 4.x carries the whole payload under one key. One scheduled before the
        // upgrade carries the flat keys the 3.x job wrote, and there is nothing to read there — which
        // is an answer here, where an IJob<SendInvoice> would have failed the firing instead.
        if (!context.TryGetInput(out SendInvoice? invoice) || invoice is null)
        {
            invoice = new SendInvoice(
                context.MergedJobDataMap.GetString("CustomerId")!,
                context.MergedJobDataMap.Get<decimal>("Amount"));
        }

        return Send(invoice, cancellationToken);
    }

    private static ValueTask Send(SendInvoice invoice, CancellationToken cancellationToken) => default;
}
```
<!-- endSnippet -->

`TryGetInput` returns `false` only when the key is absent; a present but unreadable value still throws.
Once the old triggers have drained, switch to `IJob<TInput>`.

## Calling off a whole correlation

<!-- snippet: sample_one_off_job_cancel_by_group -->
```csharp
public async ValueTask<int> CustomerWentAway(IScheduler scheduler, string customerId, CancellationToken cancellationToken)
{
    // Every firing scheduled under this customer's group goes in one call: the group the one-liner
    // put them in is the handle for calling all of them off, and nothing has to list the keys first.
    List<TriggerKey> calledOff = await scheduler.UnscheduleJobs(
        GroupMatcher<TriggerKey>.GroupEquals(customerId),
        cancellationToken);

    // The answer names what went, so "there was nothing left to cancel" is a count, not a guess.
    return calledOff.Count;
}
```
<!-- endSnippet -->

* `UnscheduleJobs(GroupMatcher<TriggerKey>)` removes every trigger in the matching groups and returns the
  removed keys (empty if none).
* The store resolves the group under the lock that empties it, so a firing another node added a moment
  earlier is removed too.
* `DeleteJobs(GroupMatcher<JobKey>)` does the same for jobs and their triggers. Do not use it to cancel a
  correlation: the one-liner's durable job is shared by every firing of its type.

::: warning A matcher is required
Both throw `ArgumentNullException` on a `null` matcher, unlike the pause and resume group forms, which read
it as the default group. A mistaken pause can be resumed; a delete cannot.
:::

## Scheduling over a firing that is already there

The `ScheduleJob` overloads taking `ScheduleJobOptions` replace an existing trigger in one call, inside the
store's lock:

```csharp
// Trigger only - the job it names is already stored.
await scheduler.ScheduleJob(trigger, new ScheduleJobOptions { Replace = true }, cancellationToken);

// Job and trigger together, in one store operation.
await scheduler.ScheduleJob(job, trigger, new ScheduleJobOptions { Replace = true }, cancellationToken);
```

* They replace `CheckExists` → `UnscheduleJob` → `ScheduleJob`: three round trips and a race with other
  nodes.
* `options` has no default here, or `scheduler.ScheduleJob(trigger)` would be ambiguous.
* A replaced trigger **keeps its previous fire time**, so `context.PreviousFireTimeUtc` survives the
  rewrite. Set `PreviousFireTimeUtc` on the new trigger to override it.
