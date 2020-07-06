---
title: 'Lesson 7: TriggerListeners and JobListeners'
---

Listeners are objects that you create to perform actions based on events occuring within the scheduler.
As you can probably guess, TriggerListeners receive events related to triggers, and JobListeners receive events related to jobs.

Trigger-related events include: trigger firings, trigger mis-firings (discussed in the "Triggers" section of this document),
and trigger completions (the jobs fired off by the trigger is finished).

__The ITriggerListener Interface__

```csharp
public interface ITriggerListener
{
	 string Name { get; }
	 
	 Task TriggerFired(ITrigger trigger, IJobExecutionContext context);
	 
	 Task<bool> VetoJobExecution(ITrigger trigger, IJobExecutionContext context);
	 
	 Task TriggerMisfired(ITrigger trigger);
	 
	 Task TriggerComplete(ITrigger trigger, IJobExecutionContext context, int triggerInstructionCode);
}
```

Job-related events include: a notification that the job is about to be executed, and a notification when the job has completed execution.

__The IJobListener Interface__

```csharp
public interface IJobListener
{
	string Name { get; }

	Task JobToBeExecuted(IJobExecutionContext context);

	Task JobExecutionVetoed(IJobExecutionContext context);

	Task JobWasExecuted(IJobExecutionContext context, JobExecutionException jobException);
} 
```

## Using Your Own Listeners

To create a listener, simply create an object the implements either the ITriggerListener and/or IJobListener interface. 
Listeners are then registered with the scheduler during run time, and must be given a name (or rather, they must advertise their own 
name via their Name property. 

For your convenience, rather than implementing those interfaces, your class could also extend the class JobListenerSupport or TriggerListenerSupport
and simply override the events you're interested in.


Listeners are registered with the scheduler's ListenerManager along with a Matcher that describes which Jobs/Triggers the listener wants to receive events for.

*Listeners are registered with the scheduler during run time, and are NOT stored in the JobStore along with the jobs and triggers. 
This is because listeners are typically an integration point with your application. 
Hence, each time your application runs, the listeners need to be re-registered with the scheduler.*


**Adding a JobListener that is interested in a particular job:**

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, KeyMatcher<JobKey>.KeyEquals(new JobKey("myJobName", "myJobGroup")));
```

**Adding a JobListener that is interested in all jobs of a particular group:**

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, GroupMatcher<JobKey>.GroupEquals("myJobGroup"));
```

**Adding a JobListener that is interested in all jobs of two particular groups:**

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener,
	OrMatcher<JobKey>.Or(GroupMatcher<JobKey>.GroupEquals("myJobGroup"), GroupMatcher<JobKey>.GroupEquals("yourGroup")));
```


**Adding a JobListener that is interested in all jobs:**

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, GroupMatcher<JobKey>.AnyGroup());
```

Listeners are not used by most users of Quartz.NET, but are handy when application requirements create the need
for the notification of events, without the Job itself explicitly notifying the application.

