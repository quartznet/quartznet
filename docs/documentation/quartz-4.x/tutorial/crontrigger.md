---

title: 'CronTrigger Tutorial'
---

## Introduction

cron is a UNIX tool that has been around for a long time, so its scheduling capabilities are powerful and proven.
The CronTrigger class is based on the scheduling capabilities of cron.

CronTrigger uses "cron expressions", which are able to create firing schedules such as: "At 8:00am every Monday through Friday" or "At 1:30am every last Friday of the month".

Cron expressions are powerful, but can be pretty confusing. This tutorial aims to take some of the mystery out of creating a cron expression,
giving users a resource which they can visit before having to ask in a forum or mailing list.

## Format

A cron expression is a string comprised of 6 or 7 fields separated by white space.
Fields can contain any of the allowed values, along with various combinations of the allowed special characters for that field. The fields are as follows:

| **Field Name** | **Mandatory** | **Allowed Values** | **Allowed Special Characters** |
|----------------|---------------|--------------------|--------------------------------|
| Seconds        | YES           | 0-59               | , - * / H                      |
| Minutes        | YES           | 0-59               | , - * / H                      |
| Hours          | YES           | 0-23               | , - * / H                      |
| Day of month   | YES           | 1-31               | , - * ? / L W H                |
| Month          | YES           | 1-12 or JAN-DEC    | , - * / H                      |
| Day of week    | YES           | 1-7 or SUN-SAT     | , - * ? / L # H                |
| Year           | NO            | empty, 1970-2099   | , - * /                        |

So cron expressions can be as simple as this: `* * * * ? *`

or more complex, like this: `0/5 14,18,3-39,52 * ? JAN,MAR,SEP MON-FRI 2002-2010`

## Special characters

* `*` ("all values") - used to select all values within a field. For example, `*` in the minute field means "every minute".
* `?` ("no specific value") - useful when you need to specify something in one of the two fields in which the character is allowed, but not the other.
For example, if I want my trigger to fire on a particular day of the month (say, the 10th), but don't care what day of the week that happens to be,
I would put `10` in the day-of-month field, and `?` in the day-of-week field. See the examples below for clarification.
* `-` - used to specify ranges. For example, `10-12` in the hour field means "the hours 10, 11 and 12".
* `,` - used to specify additional values. For example, `MON,WED,FRI` in the day-of-week field means "the days Monday, Wednesday, and Friday".
* `/` - used to specify increments. For example, `0/15` in the seconds field means "the seconds 0, 15, 30, and 45".
And `5/15` in the seconds field means "the seconds 5, 20, 35, and 50".
You can also specify `/` after the "character - in this case" is equivalent to having '0' before the '/'.
 `1/3` in the day-of-month field means "fire every 3 days starting on the first day of the month".
* `L` ("last") - has different meaning in each of the two fields in which it is allowed.
For example, the value `L` in the day-of-month field means "the last day of the month" - day 31 for January, day 28 for February on non-leap years.
If used in the day-of-week field by itself, it simply means "7" or "SAT". But if used in the day-of-week field after another value, it means "the last xxx day of the month" -
for example `6L` means "the last Friday of the month". You can also specify an offset from the last day of the month, such as `L-3` which
would mean the third-to-last day of the calendar month.
The `L` option can be used in a list, but there can only be one occurrence of the `L`.
For example `1,15,L` would mean trigger on the 1st, 15th and Last Day of the month.

* `W` ("weekday") - used to specify the weekday (Monday-Friday) nearest the given day.
As an example, if you were to specify `15W` as the value for the day-of-month field, the meaning is: "the nearest weekday to the 15th of the month".
So if the 15th is a Saturday, the trigger will fire on Friday the 14th. If the 15th is a Sunday, the trigger will fire on Monday the 16th. If the 15th is a Tuesday,
then it will fire on Tuesday the 15th. However if you specify `1W` as the value for day-of-month, and the 1st is a Saturday, the trigger will fire on Monday the 3rd,
as it will not 'jump' over the boundary of a month's days. The `W` character can only be specified when the day-of-month is a single day, not a range or list of days.

::: tip
 The `L` and `W` characters can also be combined in the day-of-month field to yield `LW`, which translates to *"last weekday of the month"*.  This field can also be used in a list, for example `1,15,LW` meaning 1st, 15th and Last Weekday of the month.  `LW` supports an offset value, which will be calculated by first identifying last weekday, then subtracting the offset. for example `LW-2`
:::

* `#` - used to specify "the nth" XXX day of the month. For example, the value of `6#3` in the day-of-week field means
"the third Friday of the month" (day 6 = Friday and "#3" = the 3rd one in the month).
Other examples: `2#1` = the first Monday of the month and `4#5` = the fifth Wednesday of the month.
Note that if you specify `#5` and there is not 5 of the given day-of-week in the month, then no firing will occur that month.
::: tip
The legal characters and the names of months and days of the week are not case sensitive. MON is the same as mon.
:::

## H (hash) for load distribution

The `H` symbol (for "hash") can be used in place of a specific value to spread scheduled tasks
evenly across time. When many triggers share an identical cron expression such as `0 0 0 * * ?` (midnight daily),
they all fire simultaneously, causing resource spikes.

`H` resolves to a **deterministic** value derived from the trigger's identity (name and group). The value stays
stable as long as the trigger identity doesn't change, but different triggers get different
values, spreading load across the allowed range.

### Syntax

| **Expression** | **Meaning** |
|:---------------|:------------|
| `H`            | Hash value within the full range of the field |
| `H(0-7)`       | Hash value constrained to the range 0 through 7 |
| `H/15`         | Hash-derived offset, then repeat every 15 (e.g., 7, 22, 37, 52) |
| `H(0-29)/10`   | Hash-derived offset in 0-29, then repeat every 10 (e.g., 3, 13, 23) |

`H` can appear in **comma-separated lists** alongside fixed values (e.g., `H,30,45`).

`H` is **not** supported in the Year field, and cannot be combined with `L`, `W`, or `#`.

### Examples

| **Expression** | **Description** |
|:---------------|:----------------|
| `0 H H * * ?`       | Once per day at a hash-derived hour and minute |
| `0 H H(0-7) * * ?`  | Once per day between midnight and 7:59 AM |
| `0 H/15 * * * ?`    | Every 15 minutes, starting from a hash-derived offset |
| `H H H * * ?`       | Once per day at a unique second, minute, and hour |

### Usage with TriggerBuilder

When using `H` through the builder API, the trigger identity is used as the hash seed.
You **must** call `WithIdentity()` so the hash is derived from a stable, meaningful name
rather than a random GUID:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly-cleanup")
    .WithCronSchedule("0 H H(0-7) * * ?")
    .Build();
```

You can also provide an explicit hash key, which does not require a trigger identity:

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
`CronExpressionString` returns the **resolved** expression (e.g., `"0 23 3 * * ?"`) after H
tokens are replaced with their computed values. This resolved form is what gets persisted to
the database, ensuring stability across scheduler restarts.
:::

## Examples

Here are some full examples:

| **Expression**             | **Meaning**                                                                                                                         |
|--:-------------------------|--:----------------------------------------------------------------------------------------------------------------------------------|
| `0 0 12 * * ?`             | Fire at 12pm (noon) every day                                                                                                       |
| `0 15 10 ? * *`            | Fire at 10:15am every day                                                                                                           |
| `0 15 10 * * ?`            | Fire at 10:15am every day                                                                                                           |
| `0 15 10 * * ? *`          | Fire at 10:15am every day                                                                                                           |
| `0 15 10 * * ? 2005`       | Fire at 10:15am every day during the year 2005                                                                                      |
| `0 * 14 * * ?`             | Fire every minute starting at 2pm and ending at 2:59pm, every day                                                                   |
| `0 0/5 14 * * ?`           | Fire every 5 minutes starting at 2pm and ending at 2:55pm, every day                                                                |
| `0 0/5 14,18 * * ?`        | Fire every 5 minutes starting at 2pm and ending at 2:55pm, AND fire every 5 minutes starting at 6pm and ending at 6:55pm, every day |
| `0 0-5 14 * * ?`           | Fire every minute starting at 2pm and ending at 2:05pm, every day                                                                   |
| `0 10,44 14 ? 3 WED`       | Fire at 2:10pm and at 2:44pm every Wednesday in the month of March.                                                                 |
| `0 15 10 ? * MON-FRI`      | Fire at 10:15am every Monday, Tuesday, Wednesday, Thursday and Friday                                                               |
| `0 15 10 15 * ?`           | Fire at 10:15am on the 15th day of every month                                                                                      |
| `0 15 10 L * ?`            | Fire at 10:15am on the last day of every month                                                                                      |
| `0 15 10 L-2 * ?`          | Fire at 10:15am on the 2nd-to-last last day of every month                                                                          |
| `0 15 10 ? * 6L`           | Fire at 10:15am on the last Friday of every month                                                                                   |
| `0 15 10 ? * 6L`           | Fire at 10:15am on the last Friday of every month                                                                                   |
| `0 15 10 ? * 6L 2002-2005` | Fire at 10:15am on every last Friday of every month during the years 2002, 2003, 2004 and 2005                                      |
| `0 15 10 ? * 6#3`          | Fire at 10:15am on the third Friday of every month                                                                                  |
| `0 0 12 1/5 * ?`           | Fire at 12pm (noon) every 5 days every month, starting on the first day of the month.                                               |
| `0 11 11 11 11 ?`          | Fire every November 11th at 11:11am.                                                                                                |
| `0 15 10 1,2,3 * MON,FRI`  | Fire at 10:15am on the 1st, 2nd, 3rd of the month, and every Monday and Friday                                                      |
| `H H H * * ?`              | Fire once per day at a hash-derived second, minute, and hour (spread across triggers)                                               |
| `0 H H(0-7) * * ?`         | Fire once per day between midnight and 7:59 AM, at a hash-derived time                                                             |
| `0 H/15 * * * ?`           | Fire every 15 minutes, starting from a hash-derived offset                                                                          |

::: tip
Pay attention to the effects of '?' and '*' in the day-of-week and day-of-month fields!
:::

## Notes

::: warning
Be careful when setting fire times between the hours of the morning when "daylight savings" changes occur in your locale (for US locales, this would typically be the hour before and after 2:00 AM - because the time shift can cause a skip or a repeat depending on whether the time moves back or jumps forward. You may find this Wikipedia entry helpful in determining the specifics to your locale:
[https://secure.wikimedia.org/wikipedia/en/wiki/Daylight_saving_time_around_the_world](https://secure.wikimedia.org/wikipedia/en/wiki/Daylight_saving_time_around_the_world)
:::
