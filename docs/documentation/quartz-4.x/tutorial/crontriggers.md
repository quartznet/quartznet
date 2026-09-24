---

title: 'Cron Triggers'
---

# Cron Triggers

Use a CronTrigger for a schedule that recurs on calendar notions rather than at the exact intervals of a
SimpleTrigger: "every Friday at noon", "every weekday at 9:30 am", "every 5 minutes between 9:00 am and
10:00 am on every Monday, Wednesday and Friday". Like a SimpleTrigger, it has a start time, when the
schedule comes into force, and an optional end time, when it stops.

## Cron Expressions

A cron expression is 6 or 7 whitespace-separated fields: seconds, minutes, hours, day-of-month, month,
day-of-week and an optional year. Each field takes a value, a list, a range, an increment, or a special
character allowed for that field. `0 0 12 ? * WED` means "every Wednesday at 12:00 pm".

The [Cron Expression Reference](../cron-expressions.md) has the field table, every special character
(`*`, `?`, `-`, `,`, `/`, `L`, `W`, `#` and the `H` hash token that spreads load across triggers), and
worked examples.

## Example Cron Expressions

More are in the API documentation for CronTrigger.

| Expression | Fires |
|---|---|
| `0 0/5 * * * ?` | every 5 minutes |
| `10 0/5 * * * ?` | every 5 minutes, 10 seconds after the minute (10:00:10 am, 10:05:10 am, …) |
| `0 30 10-13 ? * WED,FRI` | at 10:30, 11:30, 12:30 and 13:30 every Wednesday and Friday |
| `0 0/30 8-9 5,20 * ?` | every half hour between 8 am and 10 am on the 5th and 20th of every month: 8:00, 8:30, 9:00 and 9:30, **not** 10:00 |

Some schedules need more than one trigger, such as "every 5 minutes between 9:00 am and 10:00 am, and
every 20 minutes between 1:00 pm and 10:00 pm". Create two triggers and register both for the same job.

## Building CronTriggers

Build a CronTrigger with `TriggerBuilder` (the main properties) and its `WithCronSchedule` extension
method (the CronTrigger-specific properties). `CronScheduleBuilder.Create(cronExpression)` builds the
same schedule on its own, to hold in a variable or share between triggers. To compose the expression
string in code, see
[Building cron expressions programmatically](../cron-expressions.md#building-cron-expressions-programmatically).

**Every other minute, between 8am and 5pm, every day:**

<!-- snippet: sample_crontriggers_every_other_minute -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 0/2 8-17 * * ?")
    .ForJob("myJob", "group1")
    .Build();
```
<!-- endSnippet -->

**Daily at 10:42 am:**

<!-- snippet: sample_crontriggers_daily_question_mark_in_day_of_week -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 42 10 ? * *")
    .ForJob(myJobKey)
    .Build();
```
<!-- endSnippet -->

or:

<!-- snippet: sample_crontriggers_daily_question_mark_in_day_of_month -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 42 10 * * ?")
    .ForJob("myJob", "group1")
    .Build();
```
<!-- endSnippet -->

**Wednesdays at 10:42 am, in a time zone other than the system's default:**

<!-- snippet: sample_crontriggers_in_time_zone -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 42 10 ? * WED", x => x
        .InTimeZone(TimeZones.FindById("Central America Standard Time")))
    .ForJob(myJobKey)
    .Build();
```
<!-- endSnippet -->

or, with the schedule built first so that several triggers can share it:

<!-- snippet: sample_crontriggers_schedule_built_separately -->
```csharp
CronScheduleBuilder schedule = CronScheduleBuilder
    .Create("0 42 10 ? * WED")
    .InTimeZone(TimeZones.FindById("Central America Standard Time"));

ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule(schedule)
    .ForJob(myJobKey)
    .Build();
```
<!-- endSnippet -->

`TimeZones.FindById` is `TimeZoneInfo.FindSystemTimeZoneById` plus any registered resolvers. With the
[TimeZoneConverter plugin](../packages/timezoneconverter-integration.md) added, a Windows id resolves on
Linux.

**Once per day at a hash-derived time between midnight and 7:59 AM, spreading load across triggers:**

<!-- snippet: sample_crontriggers_hashed_fire_time -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly-cleanup", "maintenance")
    .WithCronSchedule("0 H H(0-7) * * ?")
    .ForJob("cleanupJob", "maintenance")
    .Build();
```
<!-- endSnippet -->

## CronTrigger Misfire Instructions

Misfires are explained in [More About Triggers](more-about-triggers.md#misfire-instructions).
CronTrigger's instructions are on the `CronTriggerMisfireInstruction` enum, whose API documentation
describes each:

| Instruction | After a misfire |
|---|---|
| `SmartPolicy` | the default on every trigger; CronTrigger treats it as `FireAndProceed` |
| `CronTriggerMisfireInstruction.FireAndProceed` | fires once as soon as the scheduler is back, then continues from the next scheduled time |
| `CronTriggerMisfireInstruction.DoNothing` | skips the missed firings and waits for the next scheduled time |
| `CronTriggerMisfireInstruction.IgnoreMisfires` | fires every missed firing, as fast as the scheduler can, until the schedule has caught up |

The behaviour is in `CronTriggerImpl.UpdateAfterMisfire`. Set the instruction on the cron schedule
(`WithCronSchedule`):

<!-- snippet: sample_crontriggers_misfire_instruction -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 0/2 8-17 * * ?", x => x
        .WithMisfireInstruction(CronTriggerMisfireInstruction.FireAndProceed))
    .ForJob("myJob", "group1")
    .Build();
```
<!-- endSnippet -->
