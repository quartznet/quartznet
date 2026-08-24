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

    await scheduler.ScheduleJob(job, trigger, cancellationToken);
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
