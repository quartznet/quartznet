---

title: 'Scheduler Listeners'
---

# Scheduler Listeners

SchedulerListeners are like `ITriggerListener`s and `IJobListener`s, but receive events of the scheduler itself, not necessarily of a specific trigger or job: a job/trigger added or removed, a serious error in the scheduler, the scheduler shutting down, and others.

::: danger
Your scheduler listeners must never throw an exception (use a try-catch) and must handle internal problems themselves.
When a listener notification fails, Quartz cannot tell whether the listener's required logic completed, and can end up in an unpredictable state.
:::

__The ISchedulerListener Interface__

```csharp
public interface ISchedulerListener
{
 Task JobScheduled(Trigger trigger);

 Task JobUnscheduled(string triggerName, string triggerGroup);

 Task TriggerFinalized(Trigger trigger);

 Task TriggersPaused(string triggerName, string triggerGroup);

 Task TriggersResumed(string triggerName, string triggerGroup);

 Task JobsPaused(string jobName, string jobGroup);

 Task JobsResumed(string jobName, string jobGroup);

 Task SchedulerError(string msg, SchedulerException cause);

 Task SchedulerShutdown();
} 
```

Any object implementing `ISchedulerListener` can be registered with the scheduler's `ListenerManager`.

__Adding a SchedulerListener:__

```csharp
scheduler.ListenerManager.AddSchedulerListener(mySchedListener);
```

__Removing a SchedulerListener:__

```csharp
scheduler.ListenerManager.RemoveSchedulerListener(mySchedListener);
```
