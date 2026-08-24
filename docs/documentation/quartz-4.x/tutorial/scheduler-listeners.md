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

    ValueTask JobScheduled(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default);

    ValueTask JobUnscheduled(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default);

    ValueTask TriggerFinalized(IScheduler scheduler, ITrigger trigger, CancellationToken cancellationToken = default);

    ValueTask TriggersPaused(IScheduler scheduler, string? triggerGroup, CancellationToken cancellationToken = default);

    ValueTask TriggersResumed(IScheduler scheduler, string? triggerGroup, CancellationToken cancellationToken = default);

    ValueTask JobsPaused(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default);

    ValueTask JobsResumed(IScheduler scheduler, string? jobGroup, CancellationToken cancellationToken = default);

    ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default);

    ValueTask SchedulerShutdown(IScheduler scheduler, CancellationToken cancellationToken = default);

    // ...and the rest; every member has a do-nothing default implementation, so implement only what you care about
}
```

A null group in `JobsPaused`, `JobsResumed`, `TriggersPaused` or `TriggersResumed` means every group.

## Every callback names its scheduler

A listener reaches the scheduler it serves through its execution context, or as its first argument when there
is no execution. Nothing here runs inside a firing, so every member takes the scheduler — which is what lets
one listener instance serve several schedulers in one host and still say which of them it is hearing from:

```csharp
public sealed class AuditSchedulerListener : ISchedulerListener
{
    public ValueTask TriggerPaused(IScheduler scheduler, TriggerKey triggerKey, CancellationToken cancellationToken = default)
    {
        logger.LogInformation("{SchedulerName} paused {TriggerKey}", scheduler.SchedulerName, triggerKey);
        return default;
    }
}
```

It is the scheduler itself rather than its name, so a listener that wants to act on what it heard can: pause
the trigger, read `Status`, ask for the job. `SchedulerName` and `SchedulerInstanceId` are on it when identity
is all you need.

## Reporting an error

`SchedulerError` is raised when something goes seriously wrong — a job that could not be built, a job store
that keeps failing, a job that threw. It receives a `SchedulerErrorContext`, which says what went wrong and,
where the scheduler knew it, what it went wrong for:

```csharp
public sealed record SchedulerErrorContext
{
    public required string Message { get; init; }
    public required SchedulerException Exception { get; init; }
    public TriggerKey? TriggerKey { get; init; }
    public JobKey? JobKey { get; init; }
    public string? FireInstanceId { get; init; }
}
```

The three keys are null when there is nothing to name — a scan that never reached a trigger, a store retrying
a connection. Every failure inside a firing fills in all three:

```csharp
public ValueTask SchedulerError(IScheduler scheduler, SchedulerErrorContext errorContext, CancellationToken cancellationToken = default)
{
    if (errorContext.TriggerKey is { } triggerKey)
    {
        return scheduler.PauseTrigger(triggerKey, cancellationToken);
    }

    logger.LogError(errorContext.Exception, "{Message}", errorContext.Message);
    return default;
}
```

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
