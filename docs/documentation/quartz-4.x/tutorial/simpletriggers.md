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

The repeat count can be zero, a positive integer, or the constant value `SimpleTriggerImpl.RepeatIndefinitely`
(`-1`) — which is what `RepeatForever()` on the schedule builder sets for you.
The repeat interval property must be `TimeSpan.Zero`, or a positive TimeSpan value.
Note that a repeat interval of zero will cause 'repeat count' firings of the trigger to happen concurrently
(or as close to concurrently as the scheduler can manage).

Start and end times are `DateTimeOffset` values, so they carry an offset and are unambiguous.
`DateTimeOffset.UtcNow` is the straightforward way to compute one; in code that has a `TimeProvider` — a job, a
test — read the clock from that instead, and `DateBuilder.Create(timeProvider)` will do the same.

The `EndTimeUtc` property (if it is specified) over-rides the repeat count property. This can be useful if you wish to create a trigger
such as one that fires every 10 seconds until a given moment in time - rather than having to compute the number of times it would
repeat between the start-time and the end-time, you can simply specify the end-time and then use a repeat count of RepeatIndefinitely
(you could even specify a repeat count of some huge number that is sure to be more than the number of times the trigger will actually
fire before the end-time arrives).

SimpleTrigger instances are built using `TriggerBuilder` (for the trigger's main properties) and `WithSimpleSchedule` extension method
(for the SimpleTrigger-specific properties).

__Build a trigger for a specific moment in time, with no repeats:__

```csharp
// trigger builder creates simple trigger by default
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .StartAt(myStartTime) // some Date 
    .ForJob("job1", "group1") // identify job with name, group strings
    .Build();
```

The trigger family interfaces (`ISimpleTrigger` and friends) are read models: cast to one to *inspect*
a trigger's schedule, never to change it. To change a schedule, rebuild the trigger with
`trigger.GetTriggerBuilder()` and hand it to `IScheduler.RescheduleJob`.

__Build a trigger for a specific moment in time, then repeating every ten seconds ten times:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .StartAt(myTimeToStartFiring) // if a start time is not given (if this line were omitted), "now" is implied
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromSeconds(10))
        .WithRepeatCount(10)) // note that 10 repeats will give a total of 11 firings
    .ForJob(myJob) // identify job with handle to its JobDetail itself                   
    .Build();

```

__Build a trigger that will fire once, five minutes in the future:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger5", "group1")
    .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
    .ForJob(myJobKey) // identify job with its JobKey
    .Build();
```

__Build a trigger that will fire now, then repeat every five minutes, until the hour 22:00:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger7", "group1")
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromMinutes(5))
        .RepeatForever())
    .EndAt(DateBuilder.Create().AtHourMinuteAndSecond(22, 0, 0).Build())
    .Build();
```

__Build a trigger that will fire at the top of the next hour, then repeat every 2 hours, forever:__

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger8") // because group is not specified, "trigger8" will be in the default group
    .StartAt(DateBuilder.Create().AtMinute(0).AtSecond(0).Build().AddHours(1)) // the next even hour
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromHours(2))
        .RepeatForever())
    // note that in this example, 'forJob(..)' is not called 
    //  - which is valid if the trigger is passed to the scheduler along with the job  
    .Build();

await scheduler.ScheduleJob(job, trigger);
```

Spend some time looking at all of the available methods in the language defined by `TriggerBuilder` and its extension method `WithSimpleSchedule`
so that you can be familiar with options available to you that may not have been demonstrated in the examples above.

## SimpleTrigger Misfire Instructions

SimpleTrigger has several instructions that can be used to inform Quartz.NET what it should do when a misfire occurs.
(Misfire situations were introduced in the [More About Triggers](more-about-triggers.md#misfire-instructions) section of this tutorial).
The instructions live on the `SimpleTriggerMisfireInstruction` enum (whose API documentation describes each one's behavior):

__Misfire instructions for SimpleTrigger__

* `SimpleTriggerMisfireInstruction.IgnoreMisfires`
* `SimpleTriggerMisfireInstruction.FireNow`
* `SimpleTriggerMisfireInstruction.NowWithExistingCount`
* `SimpleTriggerMisfireInstruction.NowWithRemainingCount`
* `SimpleTriggerMisfireInstruction.NextWithRemainingCount`
* `SimpleTriggerMisfireInstruction.NextWithExistingCount`

You should recall from the earlier lessons that all triggers have the `SmartPolicy` instruction available for use,
and this instruction is also the default for all trigger types.

If the 'smart policy' instruction is used, SimpleTrigger chooses between its instructions based on the repeat
count of the trigger:

| Repeat count | Resolves to |
|---|---|
| `0` — fires once | `FireNow` |
| `RepeatIndefinitely` — repeats forever | `NextWithRemainingCount` |
| a finite count | `NowWithExistingCount` |

`FireNow` on a trigger that does repeat is treated as `NowWithRemainingCount`, since firing "now" and forgetting
the rest of the schedule is not what anyone means by it. The behaviour lives in
`SimpleTriggerImpl.UpdateAfterMisfire`.

When building SimpleTriggers, you specify the misfire instruction as part of the simple schedule (via `SimpleScheduleBuilder`):

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger7", "group1")
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromMinutes(5))
        .RepeatForever()
        .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount))
    .Build();
```
