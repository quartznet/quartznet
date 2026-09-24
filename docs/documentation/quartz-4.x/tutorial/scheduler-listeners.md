---

title: 'Scheduler Listeners'
---

# Scheduler Listeners

SchedulerListeners are like `ITriggerListener`s and `IJobListener`s, but receive events from the scheduler
itself, not necessarily about a specific trigger or job: a job or trigger added or removed, a serious error
in the scheduler, the scheduler shutting down, and others.

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

No scheduler-listener callback runs inside a firing, so every member takes the scheduler as its first
argument. One listener instance can then serve several schedulers in one host and know which one it is
hearing from:

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

The argument is the scheduler itself, so the listener can act on it: pause the trigger, read `Status`,
ask for the job. For identity only, read `SchedulerName` and `SchedulerInstanceId`.

## Reporting an error

`SchedulerError` is raised when something goes seriously wrong: a job that could not be built, a job store
that keeps failing, a job that threw. Its `SchedulerErrorContext` says what went wrong and, where the
scheduler knows, for which trigger, job and firing:

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

The three keys are null when there is nothing to name, such as a scan that never reached a trigger or a
store retrying a connection. Every failure inside a firing fills in all three:

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

## Registering

Any object implementing `ISchedulerListener` can be registered with the scheduler's `ListenerManager`.

A scheduler listener is identified by its `Name`, which defaults to the type's name. Registering a second
listener under a taken name replaces the first, so override `Name` if you register several instances of
one type with the same scheduler.

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

Register a listener that belongs to the application where the scheduler is configured. The container
constructs it, and it is in place before the scheduler starts, so it also hears the starting and started
notifications:

<!-- snippet: sample_scheduler_listeners_under_di -->
```csharp
builder.AddQuartz(q =>
{
    q.AddSchedulerListener<AuditSchedulerListener>();
});
```
<!-- endSnippet -->

Overloads take an instance you built or a factory over the service provider, like
[the ones for job and trigger listeners](trigger-and-job-listeners.md#registering-listeners-with-the-container).
