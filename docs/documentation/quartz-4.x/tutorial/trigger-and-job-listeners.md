---

title: 'Trigger and Job Listeners'
---

# Trigger and Job Listeners

Listeners are objects you create to act on events in the scheduler. Most applications do not need them;
use one when the application must hear about events without the job notifying it.

| Listener | Events |
|---|---|
| `ITriggerListener` | trigger firings, misfires, and completions (the job the trigger fired has finished) |
| `IJobListener` | a job is about to run, was vetoed, or has finished |

::: danger
Make sure your trigger and job listeners never throw an exception (use a try-catch) and that they can handle
internal problems. A throwing listener costs the *firing*:

* one that throws before the job runs abandons the firing, so the job does not run;
* one that throws after cannot undo it: the job has run and the trigger has decided what it wants done.

Either way the failure is reported to the scheduler listeners through `ISchedulerListener.SchedulerError`,
wrapped in a `JobExecutionProcessException` that names the listener and the firing. The trigger is
released, including the siblings a `[DisallowConcurrentExecution]` job was blocking. Nothing gets stuck;
the firing is lost.
:::

__The ITriggerListener Interface__

<!-- Quartz's own declaration of the interface, so it is written out here rather than compiled from the
     samples project: a second `Quartz.ITriggerListener` in that project would shadow the real one. -->

```csharp
public interface ITriggerListener
{
    string Name => GetType().Name;

    ValueTask TriggerFired(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default);

    ValueTask<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context, CancellationToken cancellationToken = default);

    ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default);

    ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default);
}
```

* `triggerInstructionCode` is the `SchedulerInstruction` the trigger returned for this fire: what the
  scheduler will do with the trigger, from `NoInstruction` through `SetTriggerComplete` to
  `DeleteTrigger`.
* Every callback leads with the trigger. Three of them run inside a firing and reach the scheduler
  through `context.Scheduler`. `TriggerMisfired` has no execution, so it takes the scheduler directly:

```csharp
public ValueTask TriggerMisfired(ITrigger trigger, IScheduler scheduler, CancellationToken cancellationToken = default)
{
    logger.LogWarning("{SchedulerName} missed {TriggerKey}", scheduler.SchedulerName, trigger.Key);
    return default;
}
```

__The IJobListener Interface__

<!-- Quartz's own declaration of the interface, so it is written out here rather than compiled from the
     samples project: a second `Quartz.IJobListener` in that project would shadow the real one. -->

```csharp
public interface IJobListener
{
    string Name => GetType().Name;

    ValueTask JobToBeExecuted(IJobExecutionContext context, CancellationToken cancellationToken = default);

    ValueTask JobExecutionVetoed(IJobExecutionContext context, CancellationToken cancellationToken = default);

    ValueTask JobWasExecuted(IJobExecutionContext context, JobExecutionException? jobException, CancellationToken cancellationToken = default);
}
```

`jobException` is null when the job completed without throwing, so check for null before logging it.

## Using Your Own Listeners

Implement `ITriggerListener`, `IJobListener`, or both. Listeners are registered with the scheduler at run
time under the name their `Name` property returns.

Every member of both interfaces has a default implementation: notifications do nothing, and `Name`
returns the type's name. Implement only the events you need. Declare `Name` only when you register
several instances of one type with the same scheduler.

::: warning
Because of those defaults, a method whose *signature* does not match the interface's is not a compile
error. It implements nothing, and the default runs instead. Quartz refuses such a listener when it is
registered, naming the method and the signature it should have.
:::

Listeners are registered with the scheduler's `ListenerManager`, with a matcher that selects the jobs or
triggers the listener hears about.

::: tip
Listeners are registered at run time and are __NOT__ stored in the JobStore with the jobs and triggers.
They are usually an integration point with your application, so re-register them every time the
application runs.
:::

__One job:__

<!-- snippet: sample_job_listeners_match_one_job -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, Matchers.Key(new JobKey("myJobName", "myJobGroup")));
```
<!-- endSnippet -->

__All jobs of a group:__

<!-- snippet: sample_job_listeners_match_a_group -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, GroupMatcher<JobKey>.GroupEquals("myJobGroup"));
```
<!-- endSnippet -->

__All jobs of two groups:__

<!-- snippet: sample_job_listeners_match_two_groups -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener,
    GroupMatcher<JobKey>.GroupEquals("myJobGroup").Or(GroupMatcher<JobKey>.GroupEquals("yourGroup")));
```
<!-- endSnippet -->

__All jobs:__

<!-- snippet: sample_job_listeners_match_every_job -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, Matchers.AllJobs());
```
<!-- endSnippet -->

* No matcher also means every job: `AddJobListener(myJobListener)`.
* Matchers are given only at registration. To change them, register again under the same name; the new
  registration replaces the listener and its matchers together.
* Listeners are notified in registration order, and that order is guaranteed, so one listener can
  prepare something the next reads. Re-registering under the same name keeps the listener's place. A
  listener registered after another was removed is notified last.
* `Matchers` builds the roots: `Matchers.AllJobs()`, `Matchers.AllTriggers()`, `Matchers.Key(key)`,
  `Matchers.Group<JobKey>(StringOperator.StartsWith, "a")`, `Matchers.Name<JobKey>(…)`. Any matcher
  composes with the `And`, `Or` and `Not` extension methods.

## Registering listeners with the container

Register a listener that belongs to the application where the scheduler is configured. The container
constructs it:

<!-- snippet: sample_job_listeners_under_di -->
```csharp
builder.AddQuartz(q =>
{
    // every job
    q.AddJobListener<AuditListener>();

    // only the reporting group, and only triggers whose name starts with "nightly"
    q.AddJobListener<ReportAuditListener>(GroupMatcher<JobKey>.GroupEquals("reports"));
    q.AddTriggerListener<NightlyListener>(NameMatcher<TriggerKey>.NameStartsWith("nightly"));

    // an instance you built yourself, or a factory over the provider
    q.AddTriggerListener(new VetoWeekends(), Matchers.AllTriggers());
    q.AddJobListener(provider => new MeteredListener(provider.GetRequiredService<IMeterFactory>()));
});
```
<!-- endSnippet -->

This is the same registration the `ListenerManager` calls perform, done before the scheduler starts, so
it survives a restart of the host without a startup hook of your own.

## Holding on to a running job's context

To keep hold of executions running in this process, use a job listener. `IScheduler.QueryFireInstances`
lists firings across the cluster as `FireInstance` projections with keys, times and the owning node. It
does not include the job instance, the merged job data map, the result or the cancellation handle,
because those exist only where the job runs.

A listener gets the context and can keep it for the execution, keyed by
`IJobExecutionContext.FireInstanceId` to match rows from the listing. The migration guide has the code,
about thirty lines, under
[what is running is a listing](/documentation/quartz-4.x/migration-guide.html#what-is-running-is-a-listing-not-a-list-of-contexts).

## A listener or a middleware?

Listeners only notify. A listener is told a job is about to run and told what it did; the execution
happens between the notifications. A listener cannot wrap the call in a scope or a stopwatch, skip it, or
catch or translate its exception: the exception is already classified and the trigger's fate decided.

| Need | Use |
|---|---|
| *surround* the execution: a log scope, a tenant context, a timing, translating a third-party exception | a [job execution middleware](job-execution-middleware.md) |
| *observe* it: audit a completion, chain the next job, count failures | a listener, with matchers to choose jobs and triggers (middleware has none) |
| veto it | `ITriggerListener.VetoJobExecution` |

A veto is the scheduler's own refusal and raises `JobExecutionVetoed`. A middleware that skips the job is
invisible outside the pipeline.
