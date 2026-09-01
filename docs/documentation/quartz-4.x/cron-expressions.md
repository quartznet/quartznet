---

title: 'Cron Expression Reference'
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

::: tip
For easy generation of cron intervals using UI you can use some of these services:

- [Cron Expression Generator & Explainer](https://www.freeformatter.com/cron-expression-generator-quartz.html)
- [CronMaker](http://www.cronmaker.com/)

NOTE: There are many cron standards/implementations. The results from some generators may not always be correct for Quartz.NET.
A generator that emits the five-field Unix form can be read as written - see [The Unix five-field form](#the-unix-five-field-form).
:::

## Special characters

* `*` ("all values") - used to select all values within a field. For example, `*` in the minute field means "every minute".
* `?` ("no specific value") - allowed in the day-of-month and day-of-week fields, where it is a synonym for `*`: both say that the field names no days.
Use it when you need to specify something in one of the two fields but not the other. For example, if I want my trigger to fire on a particular day of the month (say, the 10th),
but don't care what day of the week that happens to be, I would put `10` in the day-of-month field, and `?` in the day-of-week field. See the examples below for clarification.
* `-` - used to specify ranges. For example, `10-12` in the hour field means "the hours 10, 11 and 12".
* `,` - used to specify additional values. For example, `MON,WED,FRI` in the day-of-week field means "the days Monday, Wednesday, and Friday".
* `/` - used to specify increments. For example, `0/15` in the seconds field means "the seconds 0, 15, 30, and 45".
And `5/15` in the seconds field means "the seconds 5, 20, 35, and 50".
You can also specify `/` after the `*` character - in this case `*` is equivalent to having `0` before the `/`.
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

* `@` - names a whole schedule instead of one field. `@daily` *is* the expression; there is nothing else in it.
See [Macros](#macros) below for the set.

::: tip
The legal characters and the names of months and days of the week are not case sensitive. MON is the same as mon.
:::

## Forms the parser refuses

Seven shapes parsed on 3.x and then meant something other than what they said — a special character was
dropped on the floor, or a step degenerated. Each is a `FormatException` in 4.x, and the message names
the expression that says what the author meant.

| Written | What it used to mean | Write instead |
|:--------|:---------------------|:--------------|
| `1-5W` | the `W` was dropped, leaving `1-5` | `1W,2W,3W,4W,5W`, or drop the `W`. `W` applies to a single day, not a range or a list |
| `? * L-3`, `? * LW` | the suffix was dropped, leaving Saturday | `L-3` and `LW` belong to day-of-month. A bare `L` in day-of-week is still Saturday |
| `MON,FRI#3` | the third **Monday**; the Friday was never fired | one trigger per day, or drop the `#`. `#` applies to the whole field |
| `5C`, `1C` | `5` / `1` — `C` ("calendar") was never implemented | `WithCalendarName`, which is what a calendar is for |
| `*/0`, `5/0`, `0-10/0` | no step at all | `*` for every value, or a step of 1 or more |
| `0-10/120` | an unchecked step; `0/120` was already rejected | a step inside the field's range |
| `MON/2` | every second week, with no stable phase | `MON,WED,FRI` for a step through the week, or `RecurrenceScheduleBuilder.Create("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")` for every second Monday |

`MON/2` is the one that changes a schedule rather than only a spelling. A textual day-of-week followed by
`/` meant "every N weeks", while the numeric `2/2` beside it meant an ordinary step — two readings of one
grammar. The fortnight also had no phase to keep: it counted whole weeks from wherever the search
happened to start, so a misfire, a restart, a failover or a dashboard query recomputed it from a
different day and moved it. It is rejected rather than quietly re-read as a step, because re-reading it
would turn a fortnightly job into a thrice-weekly one — 26 fires a year become 156 — with nothing logged.
[`RecurrenceTrigger`](tutorial/recurrencetrigger.md) anchors the interval on the trigger's start time, so
the fortnight belongs to the trigger.

If a database may hold one of these expressions, audit it before upgrading:
[Before you upgrade](migration-guide.md#before-you-upgrade) has the query.

## Macros

The `@` macros are the ones Unix cron has had since Vixie's, and they mean the same thing here:

| **Macro**             | **Expands to** | **Meaning**                           |
|:----------------------|:---------------|:--------------------------------------|
| `@yearly`, `@annually` | `0 0 0 1 1 ?`  | Midnight on 1 January                 |
| `@monthly`            | `0 0 0 1 * ?`  | Midnight on the 1st of every month    |
| `@weekly`             | `0 0 0 ? * SUN` | Midnight every Sunday                |
| `@daily`, `@midnight` | `0 0 0 * * ?`  | Midnight every day                    |
| `@hourly`             | `0 0 * * * ?`  | The top of every hour                 |

A macro needs no dialect and no opt-in, so it works wherever an expression string is read - in code, in an
XML scheduling file's `<cron-expression>@daily</cron-expression>`, in the dashboard's expression box and over
the HTTP API:

<!-- snippet: sample_cron_expressions_macro -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly")
    .WithCronSchedule("@daily") // stored, and shown, as "0 0 0 * * ?"
    .Build();
```
<!-- endSnippet -->

The expansion is what gets stored: a trigger written with `@daily` reports its expression as `0 0 0 * * ?`.

`@reboot` is rejected by name - a scheduler has no reboot to fire on, so schedule the work at startup
instead - and any other `@name` is rejected with the list above. There is deliberately no `@every_minute`
or `@every_second`: `0 * * * * ?` is already short, and where the point is to spread load,
[`H`](#h-hash-for-load-distribution) does it deterministically.

## The Unix five-field form

A cron expression copied from a crontab, a Kubernetes `CronJob` or almost any online generator has **five**
fields rather than six: it has no seconds field, and it numbers the days of the week 0-7 from Sunday rather
than 1-7. Quartz reads that form when you ask it to, with `CronFormat.Unix`:

<!-- snippet: sample_cron_expressions_unix_format -->
```csharp
// "at 04:30 on Mondays", written the way crontab writes it
CronExpression expression = CronExpression.Parse("30 4 * * 1", CronFormat.Unix);

// ...and held the way Quartz writes it: "0 30 4 ? * MON"
string canonical = expression.CronExpressionString;
```
<!-- endSnippet -->

The format is a way of reading the string, and that is all it is. There are three doors -
`CronExpression.Parse`, `CronExpression.TryParse` and `CronScheduleBuilder.Create` - and past them the
expression is an ordinary `CronExpression`:

<!-- snippet: sample_cron_expressions_unix_format_trigger -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("weekday-report")
    .WithSchedule(CronScheduleBuilder.Create("15 10 * * 1-5", CronFormat.Unix))
    .Build();

// WithCronSchedule has no format overload; compose one when you need its other options
ITrigger composed = TriggerBuilder.Create()
    .WithIdentity("weekday-report-2")
    .WithCronSchedule(CronExpression.Parse("15 10 * * 1-5", CronFormat.Unix))
    .Build();
```
<!-- endSnippet -->

A time zone composes the same way: `CronExpression.Parse(s, CronFormat.Unix).WithTimeZone(tz)`.

| **Crontab**     | **Read as**          | **Meaning**                                           |
|:----------------|:---------------------|:------------------------------------------------------|
| `30 4 * * 1`    | `0 30 4 ? * MON`     | 04:30 every Monday                                    |
| `0 12 1 * *`    | `0 0 12 1 * ?`       | Noon on the 1st of every month                        |
| `* * * * *`     | `0 * * * * ?`        | Every minute                                          |
| `15 10 * * 1-5` | `0 15 10 ? * MON-FRI` | 10:15 on weekdays                                    |
| `0 0 * * 0-6`   | `0 0 0 * * ?`        | Midnight every day - `0-6` is the whole week          |
| `0 0 * * 5-1`   | `0 0 0 ? * FRI-MON`  | Midnight Friday through Monday                        |
| `0 0 13 * 5`    | `0 0 0 13 * FRI`     | The 13th **and** every Friday - both fields name days |

Two things differ between the dialects and nothing else does. The **layout**: five fields, minutes first,
with no seconds and no year. The **day-of-week numbering**: 0-7 with both 0 and 7 meaning Sunday, so `1-5` is
Monday to Friday as it is in crontab, and `5` is Friday rather than the Thursday the same digit means in a
Quartz expression. Everything above is one grammar - `L`, `W` and `#` all work inside the five-field
layout, and `L` alone in day-of-week still means Saturday, because it is not a number and so has nothing to
renumber.

::: tip `H` composes with the Unix form
`CronScheduleBuilder.Create(expression, CronFormat.Unix)` reads an `H` in the five-field form: it rewrites
to the Quartz form first and then defers the hash to the trigger's identity, so
`Create("H 4 * * 1", CronFormat.Unix)` on a trigger identified as `nightly` comes out as `0 13 4 ? * MON`.
[`ParseWithHash`](#h-hash-for-load-distribution) and `TryParseWithHash` take a format beside the hash key
and run the same rewrite first, so `ParseWithHash("H 4 * * 1", CronFormat.Unix, "nightly")` is that same
expression without a trigger to hang it on. Because the rewrite runs first, an `H` in a five-field
day-of-week is hashed over Quartz's 1-7 rather than crontab's 0-7 and can never land on a day the
renumbering would have moved.

`CronExpression.Parse` and `TryParse` take a format but no hash key, so there is nothing for `H` to hash
against. `ResolveHash` still takes a hash key but no format: it answers with a string rather than an
expression, so it stays a Quartz-form operation, and a five-field expression is rejected there with the
six-field advice. Resolve through `ParseWithHash` instead.
:::

::: warning
The expression is **normalised** to the canonical Quartz form, and the original text is not recoverable.
`CronExpressionString`, the dashboard, the HTTP API and `QRTZ_CRON_TRIGGERS.CRON_EXPRESSION` all show
`0 30 4 ? * MON` for a trigger written as `30 4 * * 1`. That is deliberate: a stored string that parses only
under a flag the store does not persist would be a trap, so there is no format column and there will not be
one. It is the same trade as the uppercasing that has always turned `mon-fri` into `MON-FRI`.

The consequence is that the format is a parse-time argument only: the XML schema, the HTTP API and the
dashboard do not take one, so a five-field expression is something you write in C#, not something you store.
The macros above have no such limit.
:::

There is no auto-detection, and the field count alone does not choose the dialect. Letting it would make a
*dropped* field silent: `0 0 12 * * ?` without its month field is `0 0 12 * ?`, a well-formed crontab line
meaning midnight on the 12th rather than noon every day. The same digit also names a different day in each
dialect. So Quartz asks, and the error a five-field expression gets when nobody asked names the method that
does read it.

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

### Hash examples

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

<!-- snippet: sample_cron_expressions_hash_from_trigger_name -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly-cleanup")
    .WithCronSchedule("0 H H(0-7) * * ?")
    .Build();
```
<!-- endSnippet -->

You can also provide an explicit hash key, which does not require a trigger identity. The key rides on
the `CronExpression`, and `WithCronSchedule` takes one directly:

<!-- snippet: sample_cron_expressions_hash_key_on_expression -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithCronSchedule(CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup"))
    .Build();
```
<!-- endSnippet -->

Or resolve the expression on its own with `CronExpression.ParseWithHash`, which is the parse the
builder overload does for you. `CronExpression.TryParseWithHash` is the non-throwing form, for a key
and an expression that both came from somewhere you do not control. Both take a
[`CronFormat`](#the-unix-five-field-form) as their second argument when the expression is not written
the Quartz way:

<!-- snippet: sample_cron_expressions_hash_key -->
```csharp
CronExpression expr = CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup");
```
<!-- endSnippet -->

::: tip
`CronExpressionString` returns the **resolved** expression (e.g., `"0 23 3 * * ?"`) after H
tokens are replaced with their computed values. This resolved form is what gets persisted to
the database, ensuring stability across scheduler restarts.
:::

## Building cron expressions programmatically

When a schedule is assembled from user input - for example a scheduling UI that offers
dropdowns instead of a free-form cron field - you can compose the expression with the fluent
`CronExpressionBuilder` instead of concatenating strings:

<!-- snippet: sample_cron_expressions_builder -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .WithCronSchedule(CronExpressionBuilder.Create()
        .WithSecond(0)
        .WithMinuteIncrements(0, 15) // every 15 minutes
        .WithHourRange(8, 17)        // between 8:00 and 17:59
        .OnWeekdays())               // "0 0/15 8-17 ? * MON-FRI"
    .Build();
```
<!-- endSnippet -->

`WithCronSchedule` accepts the builder (or a built `CronExpression`) directly, so the chain closes
without naming `CronScheduleBuilder`; call `Build()` yourself when you want the `CronExpression` as a
value.

Each field offers a single value, list, range and increment form (e.g. `WithHour`,
`WithHours`, `WithHourRange`, `WithHourIncrements`). A schedule that fires once a day sets three of
those fields to say one thing, so `AtTime` sets them together from a `TimeOnly` — add the days it
applies to beside it:

<!-- snippet: sample_cron_expressions_at_time -->
```csharp
CronExpressionBuilder.Create().AtTime(new TimeOnly(9, 30));            // "0 30 9 ? * *"

CronExpressionBuilder.Create()
    .AtTime(new TimeOnly(9, 30))
    .WithDaysOfWeek(DayOfWeek.Monday, DayOfWeek.Thursday);            // "0 30 9 ? * MON,THU"

CronExpressionBuilder.Create()
    .AtTime(new TimeOnly(9, 30))
    .WithDayOfMonth(15);                                              // "0 30 9 15 * ?"
```
<!-- endSnippet -->

Cron resolves to a whole second, so any sub-second part of the `TimeOnly` is ignored.

The special characters are available through dedicated methods:

<!-- snippet: sample_cron_expressions_day_rules -->
```csharp
CronExpressionBuilder.Create().OnLastDayOfMonth();                         // "* * * L * ?"
CronExpressionBuilder.Create().OnNearestWeekdayOfMonth(15);                // "* * * 15W * ?"
CronExpressionBuilder.Create().OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 3); // "* * * ? * FRI#3"
CronExpressionBuilder.Create().OnLastDayOfWeekOfMonth(DayOfWeek.Friday);   // "* * * ? * FRIL"
```
<!-- endSnippet -->

A few rules to be aware of:

- Unconfigured fields default to `*` (every value).
- Each field can be configured only once; configuring it again throws `InvalidOperationException`.
- One expression carries one day field: the builder renders the unused one as `?` and throws if
  you configure both. That is the builder's own rule rather than cron's, because an expression
  naming both day fields fires on the union of the two (`0 15 10 1,2,3 * MON,FRI`), which
  `CronExpression.Parse` accepts - so write that one as text.
- Values are validated eagerly against each field's allowed range, and `Build()` returns a
  fully validated `CronExpression`; use `ToString()` if you only need the expression string.
- Days of the week are emitted using their textual names (`MON`, `FRI`, ...), so the produced
  expressions stay unambiguous across cron dialects that number weekdays differently.

## Examples

Here are some full examples:

| **Expression**             | **Meaning**                                                                                                                         |
|:---------------------------|:------------------------------------------------------------------------------------------------------------------------------------|
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
| `0 15 10 ? * 6L 2002-2005` | Fire at 10:15am on every last Friday of every month during the years 2002, 2003, 2004 and 2005                                      |
| `0 15 10 ? * 6#3`          | Fire at 10:15am on the third Friday of every month                                                                                  |
| `0 0 12 1/5 * ?`           | Fire at 12pm (noon) every 5 days every month, starting on the first day of the month.                                               |
| `0 11 11 11 11 ?`          | Fire every November 11th at 11:11am.                                                                                                |
| `0 15 10 1,2,3 * MON,FRI`  | Fire at 10:15am on the 1st, 2nd, 3rd of the month, and every Monday and Friday                                                      |
| `H H H * * ?`              | Fire once per day at a hash-derived second, minute, and hour (spread across triggers)                                               |
| `0 H H(0-7) * * ?`         | Fire once per day between midnight and 7:59 AM, at a hash-derived time                                                             |
| `0 H/15 * * * ?`           | Fire every 15 minutes, starting from a hash-derived offset                                                                          |

::: tip
Pay attention to the effects of `?` and `*` in the day-of-week and day-of-month fields. A day field
written exactly `*` or `?` names no days, so it restricts nothing and the other day field decides:
`0 15 10 1 * *` fires on the 1st of the month, and `0 15 10 * * MON` fires every Monday. Only when
**both** fields name days does the expression fire on the union of the two, as
`0 15 10 1,2,3 * MON,FRI` above does; when neither names days, every day matches. This is the Unix
`crontab(5)` rule - some other cron implementations intersect the two fields instead, so an expression
copied from one of those fires more often here than it did there.
:::

## Daylight saving time

A cron expression names a wall-clock time, and a daylight saving transition is exactly the event that
makes a wall clock ambiguous or missing. **Nothing is skipped and nothing is fired twice**, but it is
worth knowing which instant is chosen, and the answer depends on whether the expression names a fixed
time of day or an interval.

A **fixed-time** expression is one whose second, minute and hour fields are plain values or comma lists
of plain values - `0 30 2 * * ?`, `0 0,30 2 * * ?`:

- A wall-clock time the clocks **skip** fires once, at the **end of the gap** - the instant the clocks
  moved. A daily `0 30 2 * * ?` over a 02:00-03:00 spring-forward gap fires at 03:00. Every wall clock
  the gap swallowed names that one instant, so an expression matching several of them still fires once.
  This is the instant the expression itself matches: `IsSatisfiedBy` agrees with the fire time, which is
  what makes it the right answer.
- A wall-clock time that **occurs twice** on a fall-back day fires once, at the **first** of the two
  occurrences.

An **interval** expression - one with a wildcard, a step or a range in the second, minute or hour field,
such as `0 * * * * ?` or `0 0/30 * * * ?` - keeps firing through the repeated hour, so both passes of it
run. Over a spring-forward gap the gap-end rule shows as an extra fire rather than a moved one:
`0 30 * * * ?` fires at 03:00 for the occurrence the gap swallowed and again at 03:30 for the next
hour's.

A `CronCalendar` written over the skipped hour excludes the gap's end for the same reason.

::: warning Quartz 3.x behaves differently
On 3.x a skipped time is shifted forward by the transition's delta instead - the daily `0 30 2 * * ?`
above fires at 03:30, an instant its own expression does not match - and an interval expression fires the
repeated hour only once, so an "every minute" schedule silently loses an hour of real time each autumn.
:::

Whatever the schedule, **name the time zone**: an expression with none uses `TimeZoneInfo.Local`, which
is the developer's machine in development and very often UTC in a container.
[Daylight saving, clock changes and cluster skew](../best-practices.md#daylight-saving-clock-changes-and-cluster-skew)
covers the choice of trigger family, and the
[FAQ](../faq.md#daylight-saving-time-and-triggers) has the longer treatment.
