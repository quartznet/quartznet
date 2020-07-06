---
title: 'Lesson 8: SchedulerListeners'
---

SchedulerListeners are much like ITriggerListeners and IJobListeners, except they receive notification of 
events within the scheduler itself - not necessarily events related to a specific trigger or job.

Scheduler-related events include: the addition of a job/trigger, the removal of a job/trigger, a serious error 
within the scheduler, notification of the scheduler being shutdown, and others.


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
	
SchedulerListeners are registered with the scheduler's ListenerManager.
SchedulerListeners can be virtually any object that implements the ISchedulerListener interface.

**Adding a SchedulerListener:**

```csharp
scheduler.ListenerManager.AddSchedulerListener(mySchedListener);
```

**Removing a SchedulerListener:**

```csharp
scheduler.ListenerManager.RemoveSchedulerListener(mySchedListener);
```
