---

title: 'CronTrigger Tutorial'
---

## Introduction

`CronTrigger` is based on the scheduling of the UNIX cron tool. It uses cron expressions to build schedules such as "at 8:00am every Monday through Friday" or "at 1:30am every last Friday of the month".

## Format

A cron expression is a string of 6 or 7 fields separated by white space. Each field takes its allowed values and special characters:

| **Field Name** | **Mandatory** | **Allowed Values** | **Allowed Special Characters** |
|----------------|---------------|--------------------|--------------------------------|
| Seconds        | YES           | 0-59               | , - * / H                      |
| Minutes        | YES           | 0-59               | , - * / H                      |
| Hours          | YES           | 0-23               | , - * / H                      |
| Day of month   | YES           | 1-31               | , - * ? / L W H                |
| Month          | YES           | 1-12 or JAN-DEC    | , - * / H                      |
| Day of week    | YES           | 1-7 or SUN-SAT     | , - * ? / L # H                |
| Year           | NO            | empty, 1970-2099   | , - * /                        |

A simple expression: `* * * * ? *`

A complex one: `0/5 14,18,3-39,52 * ? JAN,MAR,SEP MON-FRI 2002-2010`

## Special characters

* `*` ("all values"): every value in the field. `*` in the minute field means "every minute".
* `?` ("no specific value"): allowed in day-of-month and day-of-week, to set one and leave the other open. To fire on the 10th of the month whatever the weekday, put `10` in day-of-month and `?` in day-of-week.
* `-`: a range. `10-12` in the hour field means "the hours 10, 11 and 12".
* `,`: additional values. `MON,WED,FRI` in the day-of-week field means "Monday, Wednesday and Friday".
* `/`: increments. `0/15` in the seconds field means "the seconds 0, 15, 30 and 45"; `5/15` means "the seconds 5, 20, 35 and 50". `*` before the `/` is the same as `0`. `1/3` in the day-of-month field means "every 3 days starting on the first day of the month".
* `L` ("last"): means something different in each of its two fields.
  * In day-of-month, `L` is the last day of the month: day 31 for January, day 28 for February in non-leap years. `L-3` is three days before the last day: the 28th of a 31-day month.
  * In day-of-week on its own, it means "7" or "SAT". After another value it means "the last xxx day of the month": `6L` is the last Friday of the month.
  * Do not combine `L` with lists or ranges; the results are confusing.
* `W` ("weekday"): the weekday (Monday-Friday) nearest the given day. `15W` in day-of-month means "the nearest weekday to the 15th": Friday the 14th if the 15th is a Saturday, Monday the 16th if it is a Sunday, Tuesday the 15th if it is a Tuesday. It never crosses into another month: `1W` on a month whose 1st is a Saturday fires on Monday the 3rd. `W` works only with a single day, not a range or list.
::: tip
`L` and `W` combine in the day-of-month field as `LW`: *"last weekday of the month"*.
:::
* `#`: "the nth" XXX day of the month. `6#3` in day-of-week is the third Friday of the month (day 6 = Friday, `#3` = the 3rd one). `2#1` is the first Monday and `4#5` the fifth Wednesday. If the month has no 5th such day, `#5` does not fire that month.
::: tip
Special characters and the names of months and days of the week are not case sensitive. MON is the same as mon.
:::

## H (hash) for load distribution

`H` ("hash") in place of a value spreads triggers across time. Many triggers with the same expression, such as `0 0 0 * * ?` (midnight daily), otherwise all fire at once and cause resource spikes.

`H` resolves to a **deterministic** value derived from the trigger's identity (name and group). It stays stable while the identity does not change, and differs between triggers.

### Syntax

| **Expression** | **Meaning** |
|:---------------|:------------|
| `H`            | Hash value within the full range of the field |
| `H(0-7)`       | Hash value constrained to the range 0 through 7 |
| `H/15`         | Hash-derived offset, then repeat every 15 (e.g., 7, 22, 37, 52) |
| `H(0-29)/10`   | Hash-derived offset in 0-29, then repeat every 10 (e.g., 3, 13, 23) |

* `H` can appear in **comma-separated lists** with fixed values (e.g., `H,30,45`).
* `H` is **not** supported in the Year field, and cannot be combined with `L`, `W` or `#`.

### Examples

| **Expression** | **Description** |
|:---------------|:----------------|
| `0 H H * * ?`       | Once per day at a hash-derived hour and minute |
| `0 H H(0-7) * * ?`  | Once per day between midnight and 7:59 AM |
| `0 H/15 * * * ?`    | Every 15 minutes, starting from a hash-derived offset |
| `H H H * * ?`       | Once per day at a unique second, minute, and hour |

### Usage with TriggerBuilder

The builder uses the trigger identity as the hash seed. You **must** call `WithIdentity()`, or the hash is derived from a random GUID:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly-cleanup")
    .WithCronSchedule("0 H H(0-7) * * ?")
    .Build();
```

An explicit hash key needs no trigger identity:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithCronSchedule("0 H H(0-7) * * ?", "nightly-cleanup")
    .Build();
```

Or construct a `CronExpression` directly:

```csharp
var expr = new CronExpression("0 H H(0-7) * * ?", "nightly-cleanup");
```

::: tip
`CronExpressionString` returns the **resolved** expression (e.g., `"0 23 3 * * ?"`), with H tokens replaced by their computed values. The resolved form is what the database stores, so it stays stable across scheduler restarts.
:::

## Building cron expressions programmatically

To build an expression from user input, such as a scheduling UI with dropdowns, use the fluent `CronExpressionBuilder` instead of concatenating strings:

```csharp
CronExpression expression = CronExpressionBuilder.Create()
    .WithSecond(0)
    .WithMinuteIncrements(0, 15) // every 15 minutes
    .WithHourRange(8, 17)        // between 8:00 and 17:59
    .OnWeekdays()                // Monday through Friday
    .Build(); // "0 0/15 8-17 ? * MON-FRI"

ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .WithSchedule(CronScheduleBuilder.CronSchedule(expression))
    .Build();
```

Each field has a single value, list, range and increment form (e.g. `WithHour`, `WithHours`, `WithHourRange`, `WithHourIncrements`). The special characters have their own methods:

```csharp
CronExpressionBuilder.Create().OnLastDayOfMonth();                         // "* * * L * ?"
CronExpressionBuilder.Create().OnNearestWeekdayOfMonth(15);                // "* * * 15W * ?"
CronExpressionBuilder.Create().OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 3); // "* * * ? * FRI#3"
CronExpressionBuilder.Create().OnLastDayOfWeekOfMonth(DayOfWeek.Friday);   // "* * * ? * FRIL"
```

Rules:

- Unconfigured fields default to `*` (every value).
- Each field can be configured once; configuring it again throws `InvalidOperationException`.
- Day-of-month and day-of-week are mutually exclusive. The builder renders the unused one as `?` and throws if you configure both.
- Values are validated eagerly against each field's allowed range. `Build()` returns a fully validated `CronExpression`; `ToString()` returns only the expression string.
- Days of the week are emitted as names (`MON`, `FRI`, ...), so the expressions stay unambiguous across cron dialects that number weekdays differently.

::: tip
`CronExpressionBuilder` is available from Quartz.NET 3.19 onwards.
:::

## Examples

| **Expression**             | **Meaning**                                                                                                                         |
|:---------------------------|:------------------------------------------------------------------------------------------------------------------------------------|
| `0 0 12 * * ?`             | At 12pm (noon) every day                                                                                                            |
| `0 15 10 ? * *`            | At 10:15am every day                                                                                                                |
| `0 15 10 * * ?`            | At 10:15am every day                                                                                                                |
| `0 15 10 * * ? *`          | At 10:15am every day                                                                                                                |
| `0 15 10 * * ? 2005`       | At 10:15am every day during the year 2005                                                                                           |
| `0 * 14 * * ?`             | Every minute starting at 2pm and ending at 2:59pm, every day                                                                        |
| `0 0/5 14 * * ?`           | Every 5 minutes starting at 2pm and ending at 2:55pm, every day                                                                     |
| `0 0/5 14,18 * * ?`        | Every 5 minutes starting at 2pm and ending at 2:55pm, AND every 5 minutes starting at 6pm and ending at 6:55pm, every day           |
| `0 0-5 14 * * ?`           | Every minute starting at 2pm and ending at 2:05pm, every day                                                                        |
| `0 10,44 14 ? 3 WED`       | At 2:10pm and at 2:44pm every Wednesday in the month of March.                                                                      |
| `0 15 10 ? * MON-FRI`      | At 10:15am every Monday, Tuesday, Wednesday, Thursday and Friday                                                                    |
| `0 15 10 15 * ?`           | At 10:15am on the 15th day of every month                                                                                           |
| `0 15 10 L * ?`            | At 10:15am on the last day of every month                                                                                           |
| `0 15 10 L-2 * ?`          | At 10:15am two days before the last day of every month                                                                               |
| `0 15 10 ? * 6L`           | At 10:15am on the last Friday of every month                                                                                        |
| `0 15 10 ? * 6L 2002-2005` | At 10:15am on every last Friday of every month during the years 2002, 2003, 2004 and 2005                                           |
| `0 15 10 ? * 6#3`          | At 10:15am on the third Friday of every month                                                                                       |
| `0 0 12 1/5 * ?`           | At 12pm (noon) every 5 days every month, starting on the first day of the month.                                                    |
| `0 11 11 11 11 ?`          | Every November 11th at 11:11am.                                                                                                     |
| `H H H * * ?`              | Once per day at a hash-derived second, minute, and hour (spread across triggers)                                                    |
| `0 H H(0-7) * * ?`         | Once per day between midnight and 7:59 AM, at a hash-derived time                                                                  |
| `0 H/15 * * * ?`           | Every 15 minutes, starting from a hash-derived offset                                                                               |

::: tip
Mind the effect of `?` and `*` in the day-of-week and day-of-month fields.
:::

## Notes

::: warning
Setting both a day-of-week and a day-of-month value is not fully supported: use `?` in one of these fields.
:::
::: warning
Take care with fire times in the early-morning hours when daylight saving time changes in your locale (in US locales, typically the hour before and after 2:00 AM). The shift can skip or repeat a firing, depending on whether the clock moves back or forward. See [Daylight saving time around the world](https://secure.wikimedia.org/wikipedia/en/wiki/Daylight_saving_time_around_the_world) for your locale's specifics.
:::
