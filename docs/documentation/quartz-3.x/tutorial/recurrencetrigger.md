---
title: 'RecurrenceTrigger'
---

::: tip
RecurrenceTrigger is available from Quartz.NET 3.18 onwards.
:::

RecurrenceTrigger schedules with iCalendar RFC 5545 recurrence rules (RRULE). It covers patterns CronTrigger and SimpleTrigger
cannot express, such as "every 2nd Monday of the month", "every other week on Monday, Wednesday and Friday", or "the last
weekday of March each year". It takes a standard RRULE string and computes fire times lazily, without materializing all
occurrences.

## RRULE Basics

An RRULE is semicolon-separated key-value pairs. `FREQ`, the base frequency, is required; the other properties refine the
pattern:

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
`COUNT` and `UNTIL` are mutually exclusive in one RRULE.
:::

::: warning
`COUNT` counts actual firings (`TimesTriggered`), not theoretical occurrences. Misfired
occurrences that are skipped (e.g., by the `DoNothing` misfire policy) do **not** count;
an immediate fire caused by the misfire policy (e.g., `FireOnceNow`) **does**. This follows
Quartz.NET trigger semantics and differs from strict RFC 5545 occurrence counting.
:::

## Examples

**Every 2nd Monday of the month at 9:00 AM:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("monthlyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
    .StartAt(DateBuilder.DateOf(9, 0, 0, 1, 1, 2025))
    .Build();
```

**Every other week on Monday, Wednesday, and Friday:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("weeklyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR")
    .StartNow()
    .Build();
```

**Last weekday of March each year:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("yearlyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=YEARLY;BYMONTH=3;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1")
    .StartNow()
    .Build();
```

**Every day, but only on weekdays (skip weekends):**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("weekdayTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR")
    .StartNow()
    .Build();
```

**Last day of every month:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("lastDayTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;BYMONTHDAY=-1")
    .StartNow()
    .Build();
```

**Every 3 months on the 1st and 15th, limited to 10 occurrences:**

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("quarterlyTrigger", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;INTERVAL=3;BYMONTHDAY=1,15;COUNT=10")
    .StartNow()
    .Build();
```

## Time Zone Support

Recurrence calculations use the system's local time zone unless you set another with the builder's `InTimeZone` method:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO", b => b
        .InTimeZone(Quartz.Util.TimeZoneUtil.FindTimeZoneById("Eastern Standard Time")))
    .StartNow()
    .Build();
```

## DI / Hosted Service Configuration

With `AddQuartz()`, use `WithRecurrenceSchedule` on the trigger:

```csharp
services.AddQuartz(q =>
{
    q.AddJob<MyJob>(j => j.WithIdentity("myJob"));
    q.AddTrigger(t => t
        .ForJob("myJob")
        .WithIdentity("myTrigger")
        .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
        .StartNow());
});
```

## RecurrenceTrigger Misfire Instructions

Two trigger-specific instructions, with the same semantics as CronTrigger's, plus the generic `IgnoreMisfirePolicy`:

* `MisfireInstruction.RecurrenceTrigger.FireOnceNow`
* `MisfireInstruction.RecurrenceTrigger.DoNothing`
* `MisfireInstruction.IgnoreMisfirePolicy`

The 'smart policy' instruction (the default) means `FireOnceNow`.

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("trigger1", "group1")
    .WithRecurrenceSchedule("FREQ=WEEKLY;BYDAY=MO", b => b
        .WithMisfireHandlingInstructionDoNothing())
    .Build();
```

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

RecurrenceTrigger is stored in the existing `QRTZ_SIMPROP_TRIGGERS` table, so no schema change is needed. The RRULE string is
in the `STR_PROP_1` column (max 512 characters), and the trigger type discriminator is `RECUR`.
