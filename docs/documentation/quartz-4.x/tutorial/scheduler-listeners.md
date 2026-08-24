---

title: 'Scheduler Listeners'
---

# Scheduler Listeners

SchedulerListeners are much like `ITriggerListener`s and `IJobListener`s, except they receive notification of
events within the scheduler itself - not necessarily events related to a specific trigger or job.

Scheduler-related events include: the addition of a job/trigger, the removal of a job/trigger, a serious error
within the scheduler, notification of the scheduler being shutdown, and others.

::: danger
Make sure your scheduler listeners never throw an exception (use a try-catch) and that they can handle internal problems.
Quartz can get in unpredictable state when it is unable to determine whether required logic in listener was completed successfully when listener notification failed.
:::

__The ISchedulerListener Interface__

<!-- Quartz's own declaration of the interface, so it is written out here rather than compiled from the
     samples project: a second `Quartz.ISchedulerListener` in that project would shadow the real one. -->

```csharp
public interface ISchedulerListener
{
    string Name => GetType().Name;

    ValueTask JobScheduled(ITrigger trigger, CancellationToken cancellationToken = default);

    ValueTask JobUnscheduled(TriggerKey triggerKey, CancellationToken cancellationToken = default);

    ValueTask TriggerFinalized(ITrigger trigger, CancellationToken cancellationToken = default);

    ValueTask TriggersPaused(string? triggerGroup, CancellationToken cancellationToken = default);

    ValueTask TriggersResumed(string? triggerGroup, CancellationToken cancellationToken = default);

    ValueTask JobsPaused(string? jobGroup, CancellationToken cancellationToken = default);

    ValueTask JobsResumed(string? jobGroup, CancellationToken cancellationToken = default);

    ValueTask SchedulerError(string message, SchedulerException exception, CancellationToken cancellationToken = default);

    ValueTask SchedulerShutdown(CancellationToken cancellationToken = default);

    // ...and the rest; every member has a do-nothing default implementation, so implement only what you care about
}
```

A null group in `JobsPaused`, `JobsResumed`, `TriggersPaused` or `TriggersResumed` means every group.

SchedulerListeners are registered with the scheduler's `ListenerManager`.
SchedulerListeners can be virtually any object that implements the `ISchedulerListener` interface.

A scheduler listener is identified by its `Name`, which defaults to the type's name. Registering a second
listener under a name that is already taken replaces the first, so override `Name` if you register several
instances of one type with the same scheduler.

__Adding a SchedulerListener:__

<!-- snippet: sample_scheduler_listeners_add -->
```csharp
scheduler.ListenerManager.AddSchedulerListener(mySchedListener);
```
<!-- endSnippet -->

__Removing a SchedulerListener:__

<!-- snippet: sample_scheduler_listeners_remove -->
```csharp
scheduler.ListenerManager.RemoveSchedulerListener(mySchedListener.Name);
```
<!-- endSnippet -->

A listener that belongs to the application, rather than to a moment in its run, is better registered where the
scheduler is configured — it is then constructed from the container, and it is in place before the scheduler
starts, so it hears the starting and started notifications too:

<!-- snippet: sample_scheduler_listeners_under_di -->
```csharp
builder.AddQuartz(q =>
{
    q.AddSchedulerListener<AuditSchedulerListener>();
});
```
<!-- endSnippet -->

There are overloads for an instance you built yourself and for a factory over the service provider, matching
[the ones for job and trigger listeners](trigger-and-job-listeners.md#registering-listeners-with-the-container).
