---
title: 'RecurrenceTrigger'
---

RecurrenceTrigger uses iCalendar RFC 5545 recurrence rules (RRULE) to define schedules. This trigger type enables complex scheduling
patterns that cannot be expressed with CronTrigger or SimpleTrigger, such as "every 2nd Monday of the month", "every other week on
Monday, Wednesday and Friday", or "the last weekday of March each year".

RecurrenceTrigger accepts a standard RRULE string and computes fire times lazily without materializing all occurrences.

## RRULE Basics

An RRULE string defines a recurrence pattern using semicolon-separated key-value pairs. The `FREQ` property is required and specifies
the base frequency. Other properties refine the pattern:

| Property | Description | Example |
|----------|-------------|---------|
| `FREQ` | Base frequency (required) | `YEARLY`, `MONTHLY`, `WEEKLY`, `DAILY`, `HOURLY`, `MINUTELY`, `SECONDLY` |
| `INTERVAL` | How often the recurrence repeats (default: 1) | `INTERVAL=2` (every other) |
| `COUNT` | Maximum number of times the trigger will fire | `COUNT=10` |
| `UNTIL` | End date/time for the recurrence | `UNTIL=20251231T235959Z` |
| `BYDAY` | Days of the week, optionally with ordinal | `BYDAY=MO,WE,FR` or `BYDAY=2MO` (2nd Monday) |
| `BYMONTHDAY` | Days of the month (1-31 or -1 to -31) | `BYMONTHDAY=15` or `BYMONTHDAY=-1` (last day) |
| `BYMONTH` | Months of the year (1-12) | `BYMONTH=1,6,12` |
| `BYSETPOS` | Position within the expanded set | `BYSETPOS=-1` (last occurrence) |
| `BYHOUR` | Hours (0-23) | `BYHOUR=9,17` |
| `BYMINUTE` | Minutes (0-59) | `BYMINUTE=0,30` |
| `BYSECOND` | Seconds (0-59) | `BYSECOND=0` |
| `BYWEEKNO` | Week numbers (1-53 or -53 to -1) | `BYWEEKNO=1,26` |
| `BYYEARDAY` | Day of year (1-366 or -366 to -1) | `BYYEARDAY=1,100,200` |
| `WKST` | Week start day (default: `MO`) | `WKST=SU` |

::: tip
`COUNT` and `UNTIL` are mutually exclusive - you cannot use both in the same RRULE.
:::

::: warning
`COUNT` tracks the number of times the trigger has actually fired (via `TimesTriggered`),
not the number of theoretical recurrence occurrences. Misfired occurrences that are skipped
(e.g., via `DoNothing` misfire policy) do **not** count toward the limit. However, if the
misfire policy causes an immediate fire (e.g., `FireAndProceed`), that fire **does** count.
This is consistent with Quartz.NET trigger semantics but differs from strict RFC 5545
occurrence counting.
:::

## Examples

**Every 2nd Monday of the month at 9:00 AM:**

<!-- snippet: sample_recurrencetrigger_second_monday -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("monthlyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
    .StartAt(DateBuilder.Create().InYear(2025).InMonthOnDay(1, 1).AtHourMinuteAndSecond(9, 0, 0).Build())
    .Build();
```
<!-- endSnippet -->

**Every other week on Monday, Wednesday, and Friday:**

<!-- snippet: sample_recurrencetrigger_every_other_week -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("weeklyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR")
    .StartNow()
    .Build();
```
<!-- endSnippet -->

**Last weekday of March each year:**

<!-- snippet: sample_recurrencetrigger_last_weekday_of_march -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("yearlyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=YEARLY;BYMONTH=3;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1")
    .StartNow()
    .Build();
```
<!-- endSnippet -->

**Every day, but only on weekdays (skip weekends):**

<!-- snippet: sample_recurrencetrigger_every_weekday -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("weekdayTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR")
    .StartNow()
    .Build();
```
<!-- endSnippet -->

**Last day of every month:**

<!-- snippet: sample_recurrencetrigger_last_day_of_month -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("lastDayTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;BYMONTHDAY=-1")
    .StartNow()
    .Build();
```
<!-- endSnippet -->

**Every 3 months on the 1st and 15th, limited to 10 occurrences:**

<!-- snippet: sample_recurrencetrigger_quarterly -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("quarterlyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=1,15;COUNT=10")
    .StartNow()
    .Build();
```
<!-- endSnippet -->

## Time Zone Support

By default, recurrence calculations use the system's local time zone. You can specify a different time zone
using the builder's `InTimeZone` method:

<!-- snippet: sample_recurrencetrigger_in_time_zone -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO", b => b
        .InTimeZone(TimeZones.FindById("Eastern Standard Time")))
    .StartNow()
    .Build();
```
<!-- endSnippet -->

## DI / Hosted Service Configuration

When using `AddQuartz()` for dependency injection, configure a recurrence trigger with `WithRecurrenceSchedule`:

<!-- snippet: sample_recurrencetrigger_under_di -->
```csharp
services.AddQuartz(q =>
{
    q.AddJob<MyJob>(j => j.WithIdentity("myJob"));
    q.AddTrigger<IJob>(t => t
        .ForJob("myJob")
        .WithIdentity("myTrigger")
        .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
        .StartNow());
});
```
<!-- endSnippet -->

## RecurrenceTrigger Misfire Instructions

RecurrenceTrigger has two trigger-specific misfire instructions (identical semantics to CronTrigger),
plus the generic one every family has. They live on the `RecurrenceTriggerMisfireInstruction` enum:

* `RecurrenceTriggerMisfireInstruction.FireAndProceed`
* `RecurrenceTriggerMisfireInstruction.DoNothing`
* `RecurrenceTriggerMisfireInstruction.IgnoreMisfires`

If the `SmartPolicy` instruction is used (the default), RecurrenceTrigger will use `FireAndProceed`.

<!-- snippet: sample_recurrencetrigger_misfire_instruction -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .WithRecurrenceSchedule("FREQ=WEEKLY;BYDAY=MO", b => b
        .WithMisfireInstruction(RecurrenceTriggerMisfireInstruction.DoNothing))
    .Build();
```
<!-- endSnippet -->

## When to Use RecurrenceTrigger vs Other Triggers

| Scenario | Recommended Trigger |
|----------|-------------------|
| Fixed interval (every 10 seconds) | SimpleTrigger |
| Cron-expressible pattern (every weekday at 9am) | CronTrigger |
| Nth day-of-week in month (2nd Monday) | **RecurrenceTrigger** |
| Last weekday of a month | **RecurrenceTrigger** |
| Every other week on specific days | **RecurrenceTrigger** |
| Complex yearly patterns with BYSETPOS | **RecurrenceTrigger** |
| Calendar interval (every 5 months) | CalendarIntervalTrigger |

## Persistence

RecurrenceTrigger uses the existing `QRTZ_SIMPROP_TRIGGERS` table for persistence - no database schema changes are required.
The RRULE string is stored in the `STR_PROP_1` column (max 512 characters). The trigger type discriminator is `RECUR`.
