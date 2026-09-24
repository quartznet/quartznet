---

title: 'Simple Triggers'
---

# Simple Triggers

Use SimpleTrigger to execute a job exactly once at a specific moment, or at a specific moment and then repeatedly at a fixed interval. For example: fire at exactly 11:23:54 AM on January 13, 2005, then five more times, every ten seconds.

A SimpleTrigger has a start time, an end time, a repeat count and a repeat interval:

* The repeat count can be zero, a positive integer, or the constant `SimpleTrigger.RepeatIndefinitely`.
* The repeat interval must be `TimeSpan.Zero` or a positive TimeSpan. With an interval of zero, the 'repeat count' firings happen concurrently (or as close to it as the scheduler manages).
* `EndTimeUtc`, if set, overrides the repeat count. For a trigger that fires every 10 seconds until a given moment, set the end time and a repeat count of RepeatIndefinitely instead of computing the number of repeats. (A huge repeat count sure to exceed the firings before the end time also works.)

The `DateTime` class helps compute fire times for the startTimeUtc (or endTimeUtc) you want.

Build SimpleTrigger instances with `TriggerBuilder` (the trigger's main properties) and the `WithSimpleSchedule` extension method (the SimpleTrigger-specific properties).

__Build a trigger for a specific moment in time, with no repeats:__

```csharp
// trigger builder creates simple trigger by default, actually an ITrigger is returned
ISimpleTrigger trigger = (ISimpleTrigger) TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .StartAt(myStartTime) // some Date 
    .ForJob("job1", "group1") // identify job with name, group strings
    .Build();
```

__Build a trigger for a specific moment in time, then repeating every ten seconds ten times:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .StartAt(myTimeToStartFiring) // if a start time is not given (if this line were omitted), "now" is implied
    .WithSimpleSchedule(x => x
        .WithIntervalInSeconds(10)
        .WithRepeatCount(10)) // note that 10 repeats will give a total of 11 firings
    .ForJob(myJob) // identify job with handle to its JobDetail itself                   
    .Build();

```

__Build a trigger that will fire once, five minutes in the future:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger5", "group1")
    .StartAt(DateBuilder.FutureDate(5, IntervalUnit.Minute)) // use DateBuilder to create a date in the future
    .ForJob(myJobKey) // identify job with its JobKey
    .Build();
```

__Build a trigger that will fire now, then repeat every five minutes, until the hour 22:00:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger7", "group1")
    .WithSimpleSchedule(x => x
        .WithIntervalInMinutes(5)
        .RepeatForever())
    .EndAt(DateBuilder.DateOf(22, 0, 0))
    .Build();
```

__Build a trigger that will fire at the top of the next hour, then repeat every 2 hours, forever:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger8") // because group is not specified, "trigger8" will be in the default group
    .StartAt(DateBuilder.EvenHourDate(null)) // get the next even-hour (minutes and seconds zero ("00:00"))
    .WithSimpleSchedule(x => x
        .WithIntervalInHours(2)
        .RepeatForever())
    // note that in this example, 'forJob(..)' is not called 
    //  - which is valid if the trigger is passed to the scheduler along with the job  
    .Build();

await scheduler.scheduleJob(trigger, job);
```

`TriggerBuilder` and its `WithSimpleSchedule` extension method have more options than these examples show.

## SimpleTrigger Misfire Instructions

These instructions tell Quartz.NET what to do when a SimpleTrigger misfires (see [More About Triggers](/documentation/quartz-3.x/tutorial/more-about-triggers.html) for misfires). They are constants on `MisfirePolicy.SimpleTrigger`, and their API documentation describes each one:

__Misfire Instruction Constants for SimpleTrigger__

* `MisfireInstruction.IgnoreMisfirePolicy`
* `MisfirePolicy.SimpleTrigger.FireNow`
* `MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount`
* `MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount`
* `MisfirePolicy.SimpleTrigger.RescheduleNextWithRemainingCount`
* `MisfirePolicy.SimpleTrigger.RescheduleNextWithExistingCount`

Every trigger type also has `MisfirePolicy.SmartPolicy`, the default. With it, SimpleTrigger chooses among its misfire instructions based on the trigger's configuration and state; the documentation of `SimpleTrigger.UpdateAfterMisfire()` has the details.

Set the misfire instruction as part of the simple schedule (via SimpleSchedulerBuilder):

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger7", "group1")
    .WithSimpleSchedule(x => x
        .WithIntervalInMinutes(5)
        .RepeatForever()
        .WithMisfireHandlingInstructionNextWithExistingCount())
    .Build();
```
