---

title: 'Scheduler Listeners'
---

SchedulerListeners are much like `ITriggerListener`s and `IJobListener`s, except they receive notification of
events within the scheduler itself - not necessarily events related to a specific trigger or job.

Scheduler-related events include: the addition of a job/trigger, the removal of a job/trigger, a serious error
within the scheduler, notification of the scheduler being shutdown, and others.

::: danger
Make sure your scheduler listeners never throw an exception (use a try-catch) and that they can handle internal problems.
Quartz can get in unpredictable state when it is unable to determine whether required logic in listener was completed successfully when listener notification failed.
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

SchedulerListeners are registered with the scheduler's `ListenerManager`.
SchedulerListeners can be virtually any object that implements the `ISchedulerListener` interface.

__Adding a SchedulerListener:__

```csharp
scheduler.ListenerManager.AddSchedulerListener(mySchedListener);
```

__Removing a SchedulerListener:__

```csharp
scheduler.ListenerManager.RemoveSchedulerListener(mySchedListener);
```
