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
        void JobScheduled(Trigger trigger);
    
        void JobUnscheduled(string triggerName, string triggerGroup);
    
        void TriggerFinalized(Trigger trigger);
    
        void TriggersPaused(string triggerName, string triggerGroup);
    
        void TriggersResumed(string triggerName, string triggerGroup);
    
        void JobsPaused(string jobName, string jobGroup);
    
        void JobsResumed(string jobName, string jobGroup);
    
        void SchedulerError(string msg, SchedulerException cause);
    
        void SchedulerShutdown();
    } 
```
	
ISchedulerListener instances are created and registered in much the same way as the other listener types, 
except there is no distinction between global and non-global listeners. Scheduler listeners can be 
virtually any object that implements the ISchedulerListener interface.
