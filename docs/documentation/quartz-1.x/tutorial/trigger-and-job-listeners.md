---

title: 'Lesson 7: TriggerListeners and JobListeners'
---

Listeners are objects that you create to perform actions based on events occurring within the scheduler.
As you can probably guess, TriggerListeners receive events related to triggers, and JobListeners receive events related to jobs.

Trigger-related events include: trigger firings, trigger mis-firings (discussed in the "Triggers" section of this document),
and trigger completions (the jobs fired off by the trigger is finished).

__The ITriggerListener Interface__

```csharp
    public interface ITriggerListener
    {
         string Name { get; }
         
         void TriggerFired(Trigger trigger, JobExecutionContext context);
         
         bool VetoJobExecution(Trigger trigger, JobExecutionContext context);
         
         void TriggerMisfired(Trigger trigger);
         
         void TriggerComplete(Trigger trigger, JobExecutionContext context, int triggerInstructionCode);
    }
```

Job-related events include: a notification that the job is about to be executed, and a notification when the job has completed execution.

__The IJobListener Interface__

```csharp
    public interface IJobListener
    {
        string Name { get; }
    
        void JobToBeExecuted(JobExecutionContext context);
    
        void JobExecutionVetoed(JobExecutionContext context);
    
        void JobWasExecuted(JobExecutionContext context, JobExecutionException jobException);
    } 
```

## Using Your Own Listeners

To create a listener, simply create an object the implements either the ITriggerListener and/or IJobListener interface.
Listeners are then registered with the scheduler during run time, and must be given a name (or rather, they must advertise their own
name via their Name property. Listeners can be registered as either "global" or "non-global".
Global listeners receive events for ALL triggers/jobs, and non-global listeners receive events only for the specific triggers/jobs that
explicitly name the listener in their GetTriggerListenerNames() or GetJobListenerNames() properties.

As described above, listeners are registered with the scheduler during run time, and are NOT stored in the JobStore along with the jobs and triggers.
The jobs and triggers only have the names of the related listeners stored with them. Hence, each time your application runs, the listeners
need to be re-registered with the scheduler.

__Adding a JobListener to the Scheduler__

```csharp
    scheduler.AddGlobalJobListener(myJobListener);
```

or

```csharp
    scheduler.AddJobListener(myJobListener);
```

Listeners are not used by most users of Quartz.NET, but are handy when application requirements create the need
for the notification of events, without the Job itself explicitly notifying the application.
