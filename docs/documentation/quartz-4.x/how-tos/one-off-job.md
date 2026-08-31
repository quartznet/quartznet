---

title: One-Off Job
---

# One-Off Job

Running a job exactly once — now, or at a moment you choose — takes either a stored job you trigger on demand,
or a job and trigger built on the spot.

## A job registered ahead of time, triggered on demand

When the set of jobs is known at startup, register them where the scheduler is configured and trigger them
later by key:

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

`StoreDurably()` is what makes the job stay in the store with no trigger attached. Without it a job is deleted
as soon as it has no triggers left, and a job registered with no trigger at all would not survive to be
triggered.

Then, from anywhere that has the scheduler:

<!-- snippet: sample_one_off_job_trigger_now -->
```csharp
public async ValueTask RunNow(IScheduler scheduler, CancellationToken cancellationToken)
{
    await scheduler.TriggerJob(new JobKey("name", "group"), cancellationToken: cancellationToken);
}
```
<!-- endSnippet -->

`TriggerJob` fires the job once, immediately. It creates no trigger and leaves nothing behind.

To give that one firing some data of its own, pass a `JobDataMap`. It is merged over the job's own data for
this firing only, exactly as a trigger's data would be:

<!-- snippet: sample_one_off_job_trigger_now_with_data -->
```csharp
public async ValueTask RunNow(IScheduler scheduler, string customer, CancellationToken cancellationToken)
{
    JobDataMap data = new() { { "CustomerId", customer } };
    await scheduler.TriggerJob(new JobKey("name", "group"), data, cancellationToken);
}
```
<!-- endSnippet -->

The same thing at run time, for a job that was not registered at startup, is `AddJob`:

<!-- snippet: sample_one_off_job_add_job -->
```csharp
IJobDetail job = JobBuilder.Create<AnExampleJob>()
    .WithIdentity("name", "group")
    .StoreDurably()
    .Build();

await scheduler.AddJob(job, new AddJobOptions { Replace = true }, cancellationToken);
```
<!-- endSnippet -->

`Replace` says that re-registering a job under a name that is already taken is intended;
without it the second call throws `ObjectAlreadyExistsException`. `StoreNonDurableWhileAwaitingScheduling` is
the other way to store a job with no trigger: it accepts a job that is *not* durable, on the understanding that
a trigger is coming. Once one arrives the job is ordinary again — deleted as soon as it has no triggers left.

## A job and a trigger built on the spot

When both the job and its schedule are decided at run time, build the pair and schedule them together:

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

No `StoreDurably()` here, and none wanted: the job arrives with its trigger, and both are gone once the trigger
has fired and has nothing left to do.

A trigger with no schedule builder fires exactly once, at its start time. `StartNow()` makes that "as soon as
the scheduler gets to it". Adding `.WithSimpleSchedule()` with no configuration of its own changes nothing —
a simple schedule with no interval and no repeat count *is* a single firing — so write it only when you are
about to configure something on it:

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
A one-shot trigger left on the default `SmartPolicy` resolves to `FireNow`, so a firing missed because the
scheduler was down happens as soon as it is back rather than being dropped. The other instructions, and when
they are worth naming, are in
[SimpleTriggers](../tutorial/simpletriggers.md#simpletrigger-misfire-instructions).
:::

## A payload and a time, in one call

When the job takes [a typed input](../tutorial/job-data-map.md#a-typed-input-the-third-read-side), there is
nothing left to build — say what to run, what to run it with, and when:

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

There are two overloads, one taking a `DateTimeOffset` and one a `TimeSpan` from now, and both answer with a
`ScheduledOneOffJob`: the `TriggerKey` of the firing they stored — the handle to cancel it with, or to replace
it by scheduling the same name again — and `FirstFireTimeUtc`, the time the store says it will fire. Two
members and no more; everything else about the firing is a property of the trigger the key names, and
`GetTrigger` is how to ask for it.

What is stored is **one durable job per job type**, under
`SchedulerConstants.ScheduledJobKey<TJob>()` — `(typeof(TJob).Name, SchedulerConstants.ScheduledJobGroup)`
— plus one trigger per call. That is the shape a message bus's Quartz integration converges on: a scheduled
message is a trigger, so there is no job churn to pay for however many firings are in flight. The job is stored
idempotently the first time a call is made on a scheduler and remembered afterwards, so it is safe for several
nodes to do at once and costs one round trip rather than two from the second call on. It is left behind when
the last firing is cancelled — one row per job type, whatever the traffic.

`OneOffJobOptions` carries what would otherwise be `TriggerBuilder` calls: `Name` and `Group` (defaulting
to a generated identifier in a group named after the job type), `Description`, `Priority`, `ExecutionGroup`,
`MisfireInstruction`, and `Replace`. **The group is the correlation axis** — everything scheduled for one saga,
one tenant or one conversation shares a group and can be listed, paused or unscheduled together.

::: warning A group default that a cancellation contract has to know about
`Group` defaults to the job type's name, not to `TriggerKey.DefaultGroup`. Code that cancels with
`new TriggerKey(id)` is naming the *default* group, so a firing scheduled through these overloads without
`Group = TriggerKey.DefaultGroup` is somewhere that cancellation silently stops matching — the unschedule
finds nothing and reports it did nothing. An integration adopting the one-liner over an existing trigger-key
contract sets the group its callers already expect.
:::

`RequestRecovery` is the one member that describes the durable job rather than the trigger. Set it and the
ensured job is marked `RequestsRecovery`, so a firing interrupted by a hard shutdown is re-executed when the
scheduler comes back:

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

The job is ensured once per scheduler instance, so **the first call's value wins for the lifetime of the
process**: a later call asking for something else finds the job already stored and does not store it again.
That is how everything else about the job works too — its description, its durability, the type it names — and
it is why this is a boolean rather than a configuration delegate that would look as though it varied per call.

## A schedule of your own on the same job

The durable job is addressable, which is what a second scheduling path needs: an integration that also builds a
recurring trigger for the same job points it at `ScheduledJobKey<TJob>()` rather than adding a job of its own to
the reserved group.

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

`SchedulerConstants.ScheduledJobGroup` is reserved for the jobs the one-liner maintains — do not put jobs of
your own in it — but the job that *is* in it is meant to be pointed at, and `ScheduledJobKey<TJob>()` spells the
whole key so nothing has to re-derive it.

The key is derived from the type, so it answers before anything has been scheduled; the job itself appears the
first time one of the one-call overloads is used on that scheduler. A store refuses a trigger whose job is
missing, so a second path that can run *first* stores the durable job itself — `AddJob` under the same key,
with `AddJobOptions.Replacing`, which is what the one-liner does and is idempotent between them.

## Reading input written by an older schema

`IJob<TInput>` fails a firing whose input is missing, by name, rather than running it with a default payload.
That is right for a fresh 4.x application and wrong halfway through an upgrade: a 3.x application converting a
job to `IJob<TInput>` finds its store already holding triggers whose payload is spread over flat `JobDataMap`
keys, with nothing under `QRTZ_JOB_INPUT`, and every one of those firings would throw.

A job that has to serve both shapes for a while stays an `IJob` and asks:

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

`TryGetInput` answers `false` only when the key is absent. A value that is present but cannot be read — neither
a `TInput` nor a payload the serializer understands — still throws, because corruption is not compatibility.
Once the pre-upgrade triggers have drained, the job becomes an `IJob<TInput>` and the fallback goes.

## Calling off a whole correlation

The group that made those firings findable is what cancels them:

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

`UnscheduleJobs(GroupMatcher<TriggerKey>)` removes every trigger in the matching groups in one call, and
answers with the keys it removed — so a cancellation that found nothing is an empty list rather than
something to infer. There is no listing step to lose a race against: the store resolves the group inside
the same lock that empties it, so a firing scheduled by another node a moment earlier goes with the rest.

`DeleteJobs(GroupMatcher<JobKey>)` is the same operation one level up, for jobs and every trigger that
references them. The one-liner's durable job is shared by every firing of its type and is *not* what you
want to delete to cancel a correlation — unschedule the trigger group instead.

::: warning A matcher is required
Both members throw `ArgumentNullException` on a `null` matcher rather than reading it as "the default
group", which is what the pause and resume group forms do. A pause taken by mistake can be resumed; a
delete cannot.
:::

## Scheduling over a firing that is already there

Replacing what is already scheduled is one call rather than three. The two `ScheduleJob` overloads that take a
`ScheduleJobOptions` do the whole thing inside the store's own lock:

```csharp
// Trigger only - the job it names is already stored.
await scheduler.ScheduleJob(trigger, new ScheduleJobOptions { Replace = true }, cancellationToken);

// Job and trigger together, in one store operation.
await scheduler.ScheduleJob(job, trigger, new ScheduleJobOptions { Replace = true }, cancellationToken);
```

Without them the only way to reschedule under a key you may or may not already hold was
`CheckExists` → `UnscheduleJob` → `ScheduleJob`, which is three round trips and a window in which another node
can do the same thing. `options` has no default on these two overloads, deliberately: giving it one would make
`scheduler.ScheduleJob(trigger)` ambiguous.

A replaced trigger **keeps the previous fire time it had**, so a job reading
`context.PreviousFireTimeUtc` is not told the schedule has never fired merely because its trigger was
rewritten. Supply a `PreviousFireTimeUtc` on the incoming trigger to say otherwise.
