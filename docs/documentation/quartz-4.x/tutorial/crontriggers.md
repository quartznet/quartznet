---

title: 'Cron Triggers'
---

# Cron Triggers

CronTriggers are often more useful than SimpleTrigger, if you need a job-firing schedule that recurs based on calendar-like notions,
rather than on the exactly specified intervals of SimpleTrigger.

With CronTrigger, you can specify firing-schedules such as "every Friday at noon", or "every weekday and 9:30 am",
or even "every 5 minutes between 9:00 am and 10:00 am on every Monday, Wednesday and Friday".

Even so, like SimpleTrigger, CronTrigger has a startTime which specifies when the schedule is in force, and an (optional)
endTime that specifies when the schedule should be discontinued.

## Cron Expressions

A cron expression is a string of 6 or 7 whitespace-separated fields - seconds, minutes, hours,
day-of-month, month, day-of-week and an optional year - where each field takes a value, a list, a
range, an increment, or one of the special characters allowed for that field.

`0 0 12 ? * WED` is a complete expression, and it means "every Wednesday at 12:00 pm".

The full field table, every special character (`*`, `?`, `-`, `,`, `/`, `L`, `W`, `#` and the `H`
hash token used to spread load across triggers), and a table of worked examples are in the
[Cron Expression Reference](../cron-expressions.md).

## Example Cron Expressions

Here are a few more examples of expressions and their meanings - you can find even more in the API documentation for CronTrigger

**CronTrigger Example 1 - an expression to create a trigger that simply fires every 5 minutes**

```text
    "0 0/5 * * * ?"
```

**CronTrigger Example 2 - an expression to create a trigger that fires every 5 minutes, at 10 seconds after the minute (i.e. 10:00:10 am, 10:05:10 am, etc.).**

```text
    "10 0/5 * * * ?"
```

**CronTrigger Example 3 - an expression to create a trigger that fires at 10:30, 11:30, 12:30, and 13:30, on every Wednesday and Friday.**

```text
    "0 30 10-13 ? * WED,FRI"
```

**CronTrigger Example 4 - an expression to create a trigger that fires every half hour between the hours of 8 am and 10 am on the 5th and 20th of every month.
Note that the trigger will NOT fire at 10:00 am, just at 8:00, 8:30, 9:00 and 9:30**

```text
    "0 0/30 8-9 5,20 * ?"
```

Note that some scheduling requirements are too complicated to express with a single trigger - such as "every 5 minutes between 9:00 am and 10:00 am,
and every 20 minutes between 1:00 pm and 10:00 pm". The solution in this scenario is to simply create two triggers, and register both of them to run the same job.

## Building CronTriggers

CronTrigger instances are built using `TriggerBuilder` (for the trigger's main properties) and `WithCronSchedule`
extension method (for the CronTrigger-specific properties).

`CronScheduleBuilder.Create(cronExpression)` builds the schedule on its own when you want to hold it in a
variable or share it between triggers; `WithCronSchedule` is the same thing inline.

To compose the cron expression string itself programmatically, see
[Building cron expressions programmatically](../cron-expressions.md#building-cron-expressions-programmatically).

**Build a trigger that will fire every other minute, between 8am and 5pm, every day:**

<!-- snippet: sample_crontriggers_every_other_minute -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 0/2 8-17 * * ?")
    .ForJob("myJob", "group1")
    .Build();
```
<!-- endSnippet -->

**Build a trigger that will fire daily at 10:42 am:**

<!-- snippet: sample_crontriggers_daily_question_mark_in_day_of_week -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 42 10 ? * *")
    .ForJob(myJobKey)
    .Build();
```
<!-- endSnippet -->

or -

<!-- snippet: sample_crontriggers_daily_question_mark_in_day_of_month -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 42 10 * * ?")
    .ForJob("myJob", "group1")
    .Build();
```
<!-- endSnippet -->

**Build a trigger that will fire on Wednesdays at 10:42 am, in a TimeZone other than the system's default:**

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

or, with the schedule built first so that several triggers can share it -

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

`TimeZones.FindById` is `TimeZoneInfo.FindSystemTimeZoneById` plus whatever resolvers are registered — which is
what makes a Windows id resolve on Linux once the
[TimeZoneConverter plugin](../packages/timezoneconverter-integration.md) is added.

**Build a trigger that fires once per day at a hash-derived time between midnight and 7:59 AM, spreading load across triggers:**

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

The following instructions can be used to inform Quartz what it should do when a misfire occurs for CronTrigger.
(Misfire situations were introduced in the More About Triggers section of this tutorial). These instructions are defined in as
the `CronTriggerMisfireInstruction` enum (and API documentation has description for their behavior). The instructions include:

- `CronTriggerMisfireInstruction.IgnoreMisfires`
- `CronTriggerMisfireInstruction.DoNothing`
- `CronTriggerMisfireInstruction.FireAndProceed`

All triggers have the `SmartPolicy` instruction available for use, and this instruction is also the default for all trigger types.
The 'smart policy' instruction is interpreted by CronTrigger as `FireAndProceed`: one firing happens as soon as the
scheduler is back, and the schedule then continues from the next time it comes round. `DoNothing` skips the missed
firings entirely and waits for the next scheduled time; `IgnoreMisfires` fires every missed firing, as fast as the
scheduler can, until the schedule has caught up. `CronTriggerImpl.UpdateAfterMisfire` is where this happens.

When building CronTriggers, you specify the misfire instruction as part of the cron schedule (via `WithCronSchedule` extension method):

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
