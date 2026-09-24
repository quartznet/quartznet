---

title: 'Cron Triggers'
---

# Cron Triggers

Use CronTrigger for schedules based on calendar notions rather than the fixed intervals of SimpleTrigger: "every Friday at noon", "every weekday at 9:30 am", or "every 5 minutes between 9:00 am and 10:00 am on every Monday, Wednesday and Friday".

Like SimpleTrigger, CronTrigger has a start time from which the schedule is in force, and an optional end time at which it stops.

## Cron Expressions

A cron expression is a string of 6 or 7 fields separated by white space. Each field takes its allowed values and special characters:

| Field Name   | Mandatory | Allowed Values   | Allowed Special Characters   |
|--------------|-----------|------------------|------------------------------|
| Seconds      | YES       | 0-59             | `, - * / H`                  |
| Minutes      | YES       | 0-59             | `, - * / H`                  |
| Hours        | YES       | 0-23             | `, - * / H`                  |
| Day of month | YES       | 1-31             | `, - * ? / L W H`            |
| Month        | YES       | 1-12 or JAN-DEC  | `, - * / H`                  |
| Day of week  | YES       | 1-7 or SUN-SAT   | `, - * ? / L # H`            |
| Year         | NO        | empty, 1970-2099 | `, - * /`                    |

::: tip
You do not need an online generator. `CronExpressionBuilder` composes an expression from typed calls
(see [Building cron expressions programmatically](crontrigger#building-cron-expressions-programmatically)),
and `CronExpression` reads one back: `CronExpression.IsValidExpression` says whether Quartz.NET
accepts the string, `GetExpressionSummary()` describes it field by field, and
`GetNextValidTimeAfter` gives the times it will fire.

There are many cron standards, so an online generator may not agree with Quartz.NET; it will not
know `H`, for one. Check anything it gives you against the library.
:::

`0 0 12 ? * WED` means "every Wednesday at 12:00 pm".

- **Ranges and lists.** The day-of-week field `WED` could be `MON-FRI`, `MON, WED, FRI` or `MON-WED,SAT`.
- **Wild-card.** `*` means every value of the field: every month in the Month field, every day in the Day-Of-Week field.
- **Values.** Seconds and minutes 0 to 59; hours 0 to 23; day-of-month 1-31 (mind how many days a month has); months 1 to 12 or JAN, FEB, MAR, APR, MAY, JUN, JUL, AUG, SEP, OCT, NOV and DEC; days-of-week 1 to 7 (1 = Sunday) or SUN, MON, TUE, WED, THU, FRI and SAT.
- **`/`** sets increments. `0/15` in Minutes means every 15 minutes starting at minute zero. `3/20` means every 20 minutes starting at minute three, the same as `3,23,43`.
- **`?`** ("no specific value") is allowed in day-of-month and day-of-week, to set one of the two and leave the other open.
- **`L`** ("last") is allowed in day-of-month and day-of-week, with a different meaning in each. In day-of-month it is the last day of the month: day 31 for January, day 28 for February in non-leap years. In day-of-week on its own it means "7" or "SAT"; after another value it means "the last xxx day of the month", so `6L` and `FRIL` both mean the last Friday of the month. Do not combine `L` with lists or ranges; the results are confusing.
- **`W`** is the weekday (Monday-Friday) nearest the given day. `15W` in day-of-month means the nearest weekday to the 15th.
- **`#`** is "the nth" XXX weekday of the month. `6#3` or `FRI#3` in day-of-week means the third Friday of the month.
- **`H`** (hash) spreads triggers evenly across time. It resolves to a deterministic value derived from the trigger's identity (name and group), so triggers with the same expression fire at different times. `0 H H(0-7) * * ?` fires once per day between midnight and 7:59 AM at a trigger-specific time; `0 H/15 * * * ?` fires every 15 minutes from a hash-derived offset. With the builder API, call `WithIdentity()` so the hash comes from a stable identity.

The [CronTrigger Tutorial](crontrigger) has the full syntax and more examples.

## Example Cron Expressions

More examples are in the API documentation for CronTrigger.

**Every 5 minutes:**

```text
    "0 0/5 * * * ?"
```

**Every 5 minutes, at 10 seconds after the minute (10:00:10 am, 10:05:10 am, etc.):**

```text
    "10 0/5 * * * ?"
```

**At 10:30, 11:30, 12:30 and 13:30, every Wednesday and Friday:**

```text
    "0 30 10-13 ? * WED,FRI"
```

**Every half hour between 8 am and 10 am on the 5th and 20th of every month.** It does NOT fire at 10:00 am, only at 8:00, 8:30, 9:00 and 9:30:

```text
    "0 0/30 8-9 5,20 * ?"
```

Some schedules are too complex for one trigger, such as "every 5 minutes between 9:00 am and 10:00 am, and every 20 minutes between 1:00 pm and 10:00 pm". Create two triggers and register both for the same job.

## Building CronTriggers

Build CronTrigger instances with `TriggerBuilder` (the trigger's main properties) and the `WithCronSchedule` extension method (the CronTrigger-specific properties). `CronScheduleBuilder`'s static methods also create schedules. To compose the expression string itself in code, see [Building cron expressions programmatically](crontrigger.md#building-cron-expressions-programmatically).

**Every other minute, between 8am and 5pm, every day:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 0/2 8-17 * * ?")
    .ForJob("myJob", "group1")
    .Build();
```

**Daily at 10:42 am:**

```csharp
// we use CronScheduleBuilder's static helper methods here
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(10, 42))
    .ForJob(myJobKey)
    .Build();
```

or:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 42 10 * * ?")
    .ForJob("myJob", "group1")
    .Build();
```

**Wednesdays at 10:42 am, in a time zone other than the system's default:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithSchedule(CronScheduleBuilder
        .WeeklyOnDayAndHourAndMinute(DayOfWeek.Wednesday, 10, 42)
        .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time")))
    .ForJob(myJobKey)
    .Build();
```

or:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 42 10 ? * WED", x => x
        .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Central America Standard Time")))
    .ForJob(myJobKey)
    .Build();
```

**Once per day at a hash-derived time between midnight and 7:59 AM, spreading load across triggers:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly-cleanup", "maintenance")
    .WithCronSchedule("0 H H(0-7) * * ?")
    .ForJob("cleanupJob", "maintenance")
    .Build();
```

## CronTrigger Misfire Instructions

These constants tell Quartz what to do when a CronTrigger misfires (see [More About Triggers](more-about-triggers.md) for misfires; the API documentation describes each one):

- `MisfireInstruction.IgnoreMisfirePolicy`
- `MisfireInstruction.CronTrigger.DoNothing`
- `MisfireInstruction.CronTrigger.FireOnceNow`

Every trigger type also has `MisfireInstruction.SmartPolicy`, the default. CronTrigger interprets it as `MisfireInstruction.CronTrigger.FireOnceNow`; the API documentation for `CronTrigger.UpdateAfterMisfire()` has the details.

Set the misfire instruction as part of the cron schedule, in `WithCronSchedule`:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger3", "group1")
    .WithCronSchedule("0 0/2 8-17 * * ?", x => x
        .WithMisfireHandlingInstructionFireAndProceed())
    .ForJob("myJob", "group1")
    .Build();
```
