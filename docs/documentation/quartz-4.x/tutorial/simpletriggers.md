---

title: 'Simple Triggers'
---

# Simple Triggers

Use a SimpleTrigger to run a job once at a specific moment, or at a moment and then repeatedly at an
interval: for example, at exactly 11:23:54 AM on January 13, 2005, and then five more times, every ten
seconds.

| Property | Values |
|---|---|
| start time | a `DateTimeOffset` |
| end time (`EndTimeUtc`) | a `DateTimeOffset`; overrides the repeat count |
| repeat count | zero, a positive integer, or `SimpleTriggerImpl.RepeatIndefinitely` (`-1`), which `RepeatForever()` sets |
| repeat interval | `TimeSpan.Zero` or a positive `TimeSpan` |

* A repeat interval of zero makes the repeat-count firings happen concurrently, or as close to it as the
  scheduler can manage.
* Start and end times carry an offset, so they are unambiguous. Compute one from `DateTimeOffset.UtcNow`;
  in code that has a `TimeProvider` (a job, a test), read the clock from it, as
  `DateBuilder.Create(timeProvider)` does.
* To fire every 10 seconds until a given moment, set the end time and a repeat count of
  `RepeatIndefinitely` instead of computing the number of repeats. A repeat count larger than the number
  of firings before the end time also works.

Build a SimpleTrigger with `TriggerBuilder` (the main properties) and its `WithSimpleSchedule`
extension method (the SimpleTrigger-specific properties).

__A specific moment in time, with no repeats:__

<!-- snippet: sample_simpletriggers_one_shot -->
```csharp
// trigger builder creates simple trigger by default
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .StartAt(myStartTime) // some Date
    .ForJob("job1", "group1") // identify job with name, group strings
    .Build();
```
<!-- endSnippet -->

The trigger family interfaces (`ISimpleTrigger` and friends) are read models: cast to one to *inspect*
a trigger's schedule, never to change it. To change a schedule, rebuild the trigger with
`trigger.GetTriggerBuilder()` and pass it to `IScheduler.RescheduleJob`.

__A specific moment in time, then every ten seconds ten times:__

<!-- snippet: sample_simpletriggers_repeat_ten_times -->
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
<!-- endSnippet -->

__Once, five minutes in the future:__

<!-- snippet: sample_simpletriggers_five_minutes_from_now -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger5", "group1")
    .StartAt(DateTimeOffset.UtcNow.AddMinutes(5))
    .ForJob(myJobKey) // identify job with its JobKey
    .Build();
```
<!-- endSnippet -->

__Now, then every five minutes, until 22:00:__

<!-- snippet: sample_simpletriggers_repeat_until_end_time -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger7", "group1")
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromMinutes(5))
        .RepeatForever())
    .EndAt(DateBuilder.Create().AtHourMinuteAndSecond(22, 0, 0).Build())
    .Build();
```
<!-- endSnippet -->

__At the top of the next hour, then every 2 hours, forever:__

<!-- snippet: sample_simpletriggers_every_two_hours -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger8") // because group is not specified, "trigger8" will be in the default group
    .StartAt(DateBuilder.Create().AtMinute(0).AtSecond(0).Build().AddHours(1)) // the next even hour
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromHours(2))
        .RepeatForever())
    // note that in this example, 'ForJob(..)' is not called
    //  - which is valid if the trigger is passed to the scheduler along with the job
    .Build();

await scheduler.ScheduleJob(job, trigger);
```
<!-- endSnippet -->

`TriggerBuilder` and `WithSimpleSchedule` have more options than these examples show.

## SimpleTrigger Misfire Instructions

Misfires are explained in [More About Triggers](more-about-triggers.md#misfire-instructions). SimpleTrigger's
instructions are on the `SimpleTriggerMisfireInstruction` enum, whose API documentation describes each:

* `SimpleTriggerMisfireInstruction.IgnoreMisfires`
* `SimpleTriggerMisfireInstruction.FireNow`
* `SimpleTriggerMisfireInstruction.NowWithExistingCount`
* `SimpleTriggerMisfireInstruction.NowWithRemainingCount`
* `SimpleTriggerMisfireInstruction.NextWithRemainingCount`
* `SimpleTriggerMisfireInstruction.NextWithExistingCount`

`SmartPolicy` is available on every trigger and is the default. On a SimpleTrigger it resolves by repeat
count:

| Repeat count | Resolves to |
|---|---|
| `0` — fires once | `FireNow` |
| `RepeatIndefinitely` — repeats forever | `NextWithRemainingCount` |
| a finite count | `NowWithExistingCount` |

`FireNow` on a repeating trigger is treated as `NowWithRemainingCount`, so the rest of the schedule is
kept. The behaviour is in `SimpleTriggerImpl.UpdateAfterMisfire`.

Set the misfire instruction on the simple schedule (`SimpleScheduleBuilder`):

<!-- snippet: sample_simpletriggers_misfire_instruction -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger7", "group1")
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromMinutes(5))
        .RepeatForever()
        .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithExistingCount))
    .Build();
```
<!-- endSnippet -->
