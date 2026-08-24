---

title: 'Trigger and Job Listeners'
---

# Trigger and Job Listeners

Listeners are objects that you create to perform actions based on events occurring within the scheduler.
As you can probably guess, TriggerListeners receive events related to triggers, and JobListeners receive events related to jobs.

Trigger-related events include: trigger firings, trigger mis-firings (discussed in the "Triggers" section of this document),
and trigger completions (the jobs fired off by the trigger is finished).

::: danger
Make sure your trigger and job listeners never throw an exception (use a try-catch) and that they can handle internal problems.
Jobs can get stuck after Quartz is unable to determine whether required logic in listener was completed successfully when listener notification failed.
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

    ValueTask TriggerMisfired(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default);

    ValueTask TriggerComplete(ITrigger trigger, IJobExecutionContext context, SchedulerInstruction triggerInstructionCode, CancellationToken cancellationToken = default);
}
```

`triggerInstructionCode` is the `SchedulerInstruction` the trigger returned for this fire — what the scheduler
is about to do with the trigger, from `NoInstruction` through `SetTriggerComplete` to `DeleteTrigger`.

A listener reaches the scheduler it serves through its execution context, or as its first argument when there
is no execution. Three of these four callbacks happen inside a firing, so they read `context.Scheduler`;
`TriggerMisfired` is the exception, because a misfire is noticed rather than executed, and it takes the
scheduler directly:

```csharp
public ValueTask TriggerMisfired(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default)
{
    logger.LogWarning("{SchedulerName} missed {TriggerKey}", scheduler.SchedulerName, trigger.Key);
    return default;
}
```

Job-related events include: a notification that the job is about to be executed, and a notification when the job has completed execution.

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

`jobException` is null when the job completed without throwing, so a listener that only reacts to failures
starts with a null check rather than assuming there is an exception to log.

## Using Your Own Listeners

To create a listener, simply create an object the implements either the `ITriggerListener` and/or `IJobListener` interface.
Listeners are then registered with the scheduler during run time under a name, which their `Name` property advertises.

Every member of both interfaces has a default implementation — the notifications do nothing, and `Name` returns
the type's name — so implement only the events you're interested in, and only declare `Name` when you register
several instances of one type with the same scheduler.

::: warning
The price of those defaults is that a method whose *signature* does not match the interface's is not a
compile error. It simply stops implementing anything, and the default runs in its place — the method is
never called. Quartz refuses a listener in that shape when it is registered, naming the method and the
signature it should have, rather than attaching one that will be silent.
:::

Listeners are registered with the scheduler's `ListenerManager` along with a Matcher that describes which Jobs/Triggers the listener wants to receive events for.

::: tip
Listeners are registered with the scheduler during run time, and are __NOT__ stored in the JobStore along with the jobs and triggers.
This is because listeners are typically an integration point with your application.
Hence, each time your application runs, the listeners need to be re-registered with the scheduler.
:::

__Adding a JobListener that is interested in a particular job:__

<!-- snippet: sample_job_listeners_match_one_job -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, Matchers.Key(new JobKey("myJobName", "myJobGroup")));
```
<!-- endSnippet -->

__Adding a JobListener that is interested in all jobs of a particular group:__

<!-- snippet: sample_job_listeners_match_a_group -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, GroupMatcher<JobKey>.GroupEquals("myJobGroup"));
```
<!-- endSnippet -->

__Adding a JobListener that is interested in all jobs of two particular groups:__

<!-- snippet: sample_job_listeners_match_two_groups -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener,
    GroupMatcher<JobKey>.GroupEquals("myJobGroup").Or(GroupMatcher<JobKey>.GroupEquals("yourGroup")));
```
<!-- endSnippet -->

__Adding a JobListener that is interested in all jobs:__

<!-- snippet: sample_job_listeners_match_every_job -->
```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, Matchers.AllJobs());
```
<!-- endSnippet -->

Passing no matcher at all means the same thing — a listener with no matchers hears about every job — so
`AddJobListener(myJobListener)` is the shortest way to say it.

The `Matchers` class is the entry point: its static factories build the roots (`Matchers.AllJobs()`,
`Matchers.AllTriggers()`, `Matchers.Key(key)`, `Matchers.Group<JobKey>(StringOperator.StartsWith, "a")`,
`Matchers.Name<JobKey>(…)`), and any matcher composes with the `And`, `Or` and `Not` extension methods.

## Registering listeners with the container

A listener that belongs to the application rather than to a moment in its run is registered where the
scheduler is configured, and constructed from the container like anything else:

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

This is the same registration the `ListenerManager` calls perform, done before the scheduler starts, which is
what makes it survive a restart of the host without a startup hook of your own.

## Holding on to a running job's context

A job listener is also the way to keep hold of the executions running in this process. The scheduler does
not hand them out: `IScheduler.QueryFireInstances` lists firings across the cluster as `FireInstance`
projections, which carry keys, times and the owning node — but not the job instance, the merged job data
map, the result or the cancellation handle, because those exist only where the job is running.

A listener is handed the context and can keep it for the duration of the execution, keyed by
`IJobExecutionContext.FireInstanceId` so that a row the listing returned and a context you are holding can
be matched up. The migration guide has the whole thing in about thirty lines, under
[what is running is a listing](/documentation/quartz-4.x/migration-guide.html#what-is-running-is-a-listing-not-a-list-of-contexts).

Listeners are not used by most users of Quartz.NET, but are handy when application requirements create the need
for the notification of events, without the Job itself explicitly notifying the application.
