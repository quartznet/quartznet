---

title: 'Trigger and Job Listeners'
---

# Trigger and Job Listeners

Listeners act on events in the scheduler. TriggerListeners receive trigger events; JobListeners receive job events. Use them when your application needs to be notified of events without the job notifying it; most users do not need them.

Trigger events: firings, misfires (see [More About Triggers](more-about-triggers.md)), and completions (the job fired by the trigger has finished).

::: danger
Your trigger and job listeners must never throw an exception (use a try-catch) and must handle internal problems themselves.
When a listener notification fails, Quartz cannot tell whether the listener's required logic completed, and jobs can get stuck.
:::

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

Job events: the job is about to be executed, and the job has completed execution.

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

A listener implements `ITriggerListener` and/or `IJobListener`, or extends `JobListenerSupport` or `TriggerListenerSupport` and overrides only the events it needs. It must return its name from its Name property.

Register listeners at run time with the scheduler's `ListenerManager`, together with a Matcher that selects the Jobs/Triggers the listener receives events for.

::: tip
Listeners are __NOT__ stored in the JobStore with the jobs and triggers, because they are usually an integration point with your application.
Register them again each time your application runs.
:::

__Adding a JobListener that is interested in a particular job:__

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, KeyMatcher<JobKey>.KeyEquals(new JobKey("myJobName", "myJobGroup")));
```

__Adding a JobListener that is interested in all jobs of a particular group:__

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, GroupMatcher<JobKey>.GroupEquals("myJobGroup"));
```

__Adding a JobListener that is interested in all jobs of two particular groups:__

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener,
 OrMatcher<JobKey>.Or(GroupMatcher<JobKey>.GroupEquals("myJobGroup"), GroupMatcher<JobKey>.GroupEquals("yourGroup")));
```

__Adding a JobListener that is interested in all jobs:__

```csharp
scheduler.ListenerManager.AddJobListener(myJobListener, GroupMatcher<JobKey>.AnyGroup());
```
