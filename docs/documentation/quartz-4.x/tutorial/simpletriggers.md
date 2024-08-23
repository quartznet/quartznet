---

title: 'Simple Triggers'
---

# Simple Triggers

SimpleTrigger should meet your scheduling needs if you need to have a job execute exactly once at a specific moment in time,
or at a specific moment in time followed by repeats at a specific interval. Or plainer English, if you want the trigger to
fire at exactly 11:23:54 AM on January 13, 2005, and then fire five more times, every ten seconds.

With this description, you may not find it surprising to find that the properties of a SimpleTrigger include: a start-time,
and end-time, a repeat count, and a repeat interval. All of these properties are exactly what you'd expect them to be, with
only a couple special notes related to the end-time property.

The repeat count can be zero, a positive integer, or the constant value `SimpleTrigger.RepeatIndefinitely`.
The repeat interval property must be `TimeSpan.Zero`, or a positive TimeSpan value.
Note that a repeat interval of zero will cause 'repeat count' firings of the trigger to happen concurrently
(or as close to concurrently as the scheduler can manage).

If you're not already familiar with the `DateTime` class, you may find it helpful for computing your trigger fire-times,
depending on the startTimeUtc (or endTimeUtc) that you're trying to create.

The `EndTimeUtc` property (if it is specified) over-rides the repeat count property. This can be useful if you wish to create a trigger
such as one that fires every 10 seconds until a given moment in time - rather than having to compute the number of times it would
repeat between the start-time and the end-time, you can simply specify the end-time and then use a repeat count of RepeatIndefinitely
(you could even specify a repeat count of some huge number that is sure to be more than the number of times the trigger will actually
fire before the end-time arrives).

SimpleTrigger instances are built using `TriggerBuilder` (for the trigger's main properties) and `WithSimpleSchedule` extension method
(for the SimpleTrigger-specific properties).

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

Spend some time looking at all of the available methods in the language defined by `TriggerBuilder` and its extension method `WithSimpleSchedule`
so that you can be familiar with options available to you that may not have been demonstrated in the examples above.

## SimpleTrigger Misfire Instructions

SimpleTrigger has several instructions that can be used to inform Quartz.NET what it should do when a misfire occurs.
(Misfire situations were introduced in the [More About Triggers](/documentation/quartz-4.x/tutorial/more-about-triggers.html) section of this tutorial).
These instructions are defined as constants on `MisfirePolicy.SimpleTrigger` (including API documentation describing their behavior).
The instructions include:

__Misfire Instruction Constants for SimpleTrigger__

* `MisfireInstruction.IgnoreMisfirePolicy`
* `MisfirePolicy.SimpleTrigger.FireNow`
* `MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount`
* `MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount`
* `MisfirePolicy.SimpleTrigger.RescheduleNextWithRemainingCount`
* `MisfirePolicy.SimpleTrigger.RescheduleNextWithExistingCount`

You should recall from the earlier lessons that all triggers have the `MisfirePolicy.SmartPolicy` instruction available for use,
and this instruction is also the default for all trigger types.

If the 'smart policy' instruction is used, SimpleTrigger dynamically chooses between its various MISFIRE instructions, based on the configuration
and state of the given SimpleTrigger instance. The documentation for the `SimpleTrigger.UpdateAfterMisfire()` method explains the exact details of
this dynamic behavior.

When building SimpleTriggers, you specify the misfire instruction as part of the simple schedule (via SimpleSchedulerBuilder):

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger7", "group1")
    .WithSimpleSchedule(x => x
        .WithIntervalInMinutes(5)
        .RepeatForever()
        .WithMisfireHandlingInstructionNextWithExistingCount())
    .Build();
```
