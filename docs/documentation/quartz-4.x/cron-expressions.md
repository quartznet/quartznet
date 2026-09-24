---

title: 'Cron Expression Reference'
---

A `CronTrigger` fires on a schedule written as a cron expression, such as "at 8:00 every Monday through
Friday" or "at 1:30 on the last Friday of the month".

## Format

A cron expression is 6 or 7 fields separated by white space:

| **Field Name** | **Mandatory** | **Allowed Values** | **Allowed Special Characters** |
|----------------|---------------|--------------------|--------------------------------|
| Seconds        | YES           | 0-59               | , - * / H                      |
| Minutes        | YES           | 0-59               | , - * / H                      |
| Hours          | YES           | 0-23               | , - * / H                      |
| Day of month   | YES           | 1-31               | , - * ? / L W H                |
| Month          | YES           | 1-12 or JAN-DEC    | , - * / H                      |
| Day of week    | YES           | 1-7 or SUN-SAT     | , - * ? / L # H                |
| Year           | NO            | empty, 1970-2099   | , - * /                        |

Simple: `* * * * ? *`. Complex: `0/5 14,18,3-39,52 * ? JAN,MAR,SEP MON-FRI 2002-2010`.

::: tip
Build an expression with [`CronExpressionBuilder`](#building-cron-expressions-programmatically), and
check one by [asking Quartz.NET when it fires](#checking-an-expression); only the library agrees with
the library. An online generator targets Java Quartz or plain Unix cron and does not know
[`H`](#h-hash-for-load-distribution) or [`MON/2`](#mon-2-is-a-step-through-the-week), so treat its
output as a draft and confirm it here. Five-field Unix output can be read as written — see
[The Unix five-field form](#the-unix-five-field-form).
:::

## Special characters

- `*` — all values. `*` in the minute field is every minute.
- `?` — no specific value. Day-of-month and day-of-week only, where it is a synonym for `*`: the field
  names no days. Use it to set one day field and not the other: `10` in day-of-month and `?` in
  day-of-week fires on the 10th, whatever the weekday.
- `-` — a range. `10-12` in the hour field is 10, 11 and 12. A range whose end is below its start wraps:
  `22-2` in the hour field is 22, 23, 0, 1 and 2, and `FRI-MON` is Friday to Monday.
- `,` — a list. `MON,WED,FRI` in the day-of-week field is Monday, Wednesday and Friday.
- `/` — an increment. `0/15` in the seconds field is 0, 15, 30 and 45; `5/15` is 5, 20, 35 and 50. `*/n`
  is `0/n`. `1/3` in day-of-month is every 3 days starting on the 1st. A day-of-week step may start from
  a name: `MON/2` is `2/2`, Monday, Wednesday and Friday — see
  [`MON/2` is a step through the week](#mon-2-is-a-step-through-the-week) if the expression came from 3.x.
- `L` — last. Its meaning depends on the field:
  - Day-of-month: the last day of the month — 31 for January, 28 for February in a non-leap year.
    `L-3` is three days before the last day: the 28th of a 31-day month. `L` can be used in a list:
    `1,15,L` is the 1st, the 15th and the last day, and `L,L-1` is the last two days.
  - Day-of-week, alone: `7`, Saturday. After a value: the last such day of the month — `6L` is the last
    Friday.
- `W` — the weekday (Monday to Friday) nearest the given day, in day-of-month only, and only after a
  single day, not a range or list. `15W`: a Saturday 15th fires on Friday the 14th, a Sunday 15th on
  Monday the 16th, a Tuesday 15th on the 15th. It never crosses into another month: `1W` on a Saturday
  1st fires on Monday the 3rd.
- `#` — the nth weekday of the month, in day-of-week. `6#3` is the third Friday, `2#1` the first Monday,
  `4#5` the fifth Wednesday. A month without a fifth one does not fire.
- `@` — a macro naming a whole schedule: `@daily` *is* the expression. See [Macros](#macros).

::: tip
`L` and `W` combine in day-of-month as `LW`, the last weekday of the month. It can be in a list
(`1,15,LW`) and take an offset: `LW-2` finds the last weekday, then subtracts 2.
:::

::: tip
Characters and the names of months and days are case-insensitive: `MON` is `mon`.
:::

## Forms the parser refuses

These six shapes parsed on 3.x but meant something other than what they said: a special character was
dropped, or a step degenerated. 4.x throws a `FormatException` whose message gives the expression the
author meant.

| Written | What it used to mean | Write instead |
|:--------|:---------------------|:--------------|
| `1-5W` | the `W` was dropped, leaving `1-5` | `1W,2W,3W,4W,5W`, or drop the `W`. `W` applies to a single day, not a range or a list |
| `? * L-3`, `? * LW` | the suffix was dropped, leaving Saturday | `L-3` and `LW` belong to day-of-month. A bare `L` in day-of-week is still Saturday |
| `MON,FRI#3` | the third **Monday**; the Friday was never fired | one trigger per day, or drop the `#`. `#` applies to the whole field |
| `5C`, `1C` | `5` / `1` — `C` ("calendar") was never implemented | `WithCalendarName`, which is what a calendar is for |
| `*/0`, `5/0`, `0-10/0` | no step at all | `*` for every value, or a step of 1 or more |
| `0-10/120` | an unchecked step; `0/120` was already rejected | a step inside the field's range |

If a database may hold one of these, audit it before upgrading:
[Before you upgrade](migration-guide.md#before-you-upgrade) has the query.

## `MON/2` is a step through the week

Since 4.1 a textual day-of-week may take a step, and it means what its numeric twin means: `MON/2` is
`2/2`, Monday, Wednesday and Friday. `MON-FRI/2` is `2-6/2`, the same three days. The step runs to
Saturday and does not wrap, as after a number.

::: danger An expression carried over from 3.x means something else
On 3.x, `MON/2` meant **every second Monday**, a fortnight. 4.0 rejected it. 4.1 parses it as Monday,
Wednesday and Friday: **26 fires a year become 156**, and nothing is logged, because the expression is
valid.

Audit for it before upgrading ([Before you upgrade](migration-guide.md#before-you-upgrade) has the
query) and rewrite what you find. Every second Monday is
`RecurrenceScheduleBuilder.Create("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO")`.
[`RecurrenceTrigger`](tutorial/recurrencetrigger.md) anchors the interval on the trigger's start time,
so the fortnight keeps its phase. 3.x counted whole weeks from wherever the search started, so a misfire,
a restart, a failover or a dashboard query could move it.
:::

The step must be a whole number from 1 to 7, as in the numeric spelling. `MON/X` and `SUN/9` are
rejected; the message says what a step must be and where the fortnight lives now.

## Macros

The `@` macros are Vixie cron's, with the same meanings:

| **Macro**             | **Expands to** | **Meaning**                           |
|:----------------------|:---------------|:--------------------------------------|
| `@yearly`, `@annually` | `0 0 0 1 1 ?`  | Midnight on 1 January                 |
| `@monthly`            | `0 0 0 1 * ?`  | Midnight on the 1st of every month    |
| `@weekly`             | `0 0 0 ? * SUN` | Midnight every Sunday                |
| `@daily`, `@midnight` | `0 0 0 * * ?`  | Midnight every day                    |
| `@hourly`             | `0 0 * * * ?`  | The top of every hour                 |

A macro needs no dialect and no opt-in. It works wherever an expression string is read: in code, in an
XML scheduling file's `<cron-expression>@daily</cron-expression>`, in the dashboard's expression box and
over the HTTP API.

<!-- snippet: sample_cron_expressions_macro -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly")
    .WithCronSchedule("@daily") // stored, and shown, as "0 0 0 * * ?"
    .Build();
```
<!-- endSnippet -->

The expansion is what is stored: a trigger written with `@daily` reports `0 0 0 * * ?`.

- `@reboot` is rejected by name: a scheduler has no reboot to fire on, so schedule the work at startup.
- Any other `@name` is rejected with the list above.
- There is no `@every_minute` or `@every_second`: `0 * * * * ?` is already short, and
  [`H`](#h-hash-for-load-distribution) spreads load deterministically.

## The Unix five-field form

An expression from a crontab, a Kubernetes `CronJob` or most online generators has **five** fields: no
seconds, and days of the week numbered 0-7 from Sunday instead of 1-7. Quartz reads that form when asked,
with `CronFormat.Unix`:

<!-- snippet: sample_cron_expressions_unix_format -->
```csharp
// "at 04:30 on Mondays", written the way crontab writes it
CronExpression expression = CronExpression.Parse("30 4 * * 1", CronFormat.Unix);

// ...and held the way Quartz writes it: "0 30 4 ? * MON"
string canonical = expression.CronExpressionString;
```
<!-- endSnippet -->

The format only changes how the string is read. Three methods take it — `CronExpression.Parse`,
`CronExpression.TryParse` and `CronScheduleBuilder.Create` — and the result is an ordinary
`CronExpression`:

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

[crontab.guru](https://crontab.guru/) explains a five-field expression field by field. Use it to read
the string, then pass the string to `CronFormat.Unix` rather than translating it by hand.

| **Crontab**     | **Read as**          | **Meaning**                                           |
|:----------------|:---------------------|:------------------------------------------------------|
| `30 4 * * 1`    | `0 30 4 ? * MON`     | 04:30 every Monday                                    |
| `0 12 1 * *`    | `0 0 12 1 * ?`       | Noon on the 1st of every month                        |
| `* * * * *`     | `0 * * * * ?`        | Every minute                                          |
| `15 10 * * 1-5` | `0 15 10 ? * MON-FRI` | 10:15 on weekdays                                    |
| `0 0 * * 0-6`   | `0 0 0 * * ?`        | Midnight every day - `0-6` is the whole week          |
| `0 0 * * 5-1`   | `0 0 0 ? * FRI-MON`  | Midnight Friday through Monday                        |
| `0 0 13 * 5`    | `0 0 0 13 * FRI`     | The 13th **and** every Friday - both fields name days |

Only two things differ between the dialects:

- **Layout**: five fields, minutes first, no seconds and no year.
- **Day-of-week numbering**: 0-7, with both 0 and 7 meaning Sunday. `1-5` is Monday to Friday, as in
  crontab, and `5` is Friday, not the Thursday it means in a Quartz expression.

The grammar is otherwise the same: `L`, `W` and `#` work in the five-field layout, and `L` alone in
day-of-week is still Saturday, since it is not a number.

::: tip `H` composes with the Unix form
`CronScheduleBuilder.Create(expression, CronFormat.Unix)` reads `H` in the five-field form. It rewrites
to the Quartz form first, then hashes on the trigger's identity: `Create("H 4 * * 1", CronFormat.Unix)`
on a trigger identified as `nightly` becomes `0 13 4 ? * MON`.
[`ParseWithHash`](#h-hash-for-load-distribution) and `TryParseWithHash` take a format beside the hash
key and rewrite first too, so `ParseWithHash("H 4 * * 1", CronFormat.Unix, "nightly")` is the same
expression without a trigger. Because the rewrite comes first, an `H` in a five-field day-of-week is
hashed over Quartz's 1-7, not crontab's 0-7.

`CronExpression.Parse` and `TryParse` take a format but no hash key, so `H` has nothing to hash.
`ResolveHash` takes a hash key but no format; it returns a string, so it stays Quartz-form only and
rejects a five-field expression with the six-field advice. Use `ParseWithHash` instead.
:::

::: warning
The expression is **normalised** to the Quartz form, and the original text is lost.
`CronExpressionString`, the dashboard, the HTTP API and `QRTZ_CRON_TRIGGERS.CRON_EXPRESSION` all show
`0 30 4 ? * MON` for a trigger written as `30 4 * * 1`. By design: a stored string that parses
only under a flag the store does not persist would be a trap, so there is no format column and there
will not be one. It is the same trade as the uppercasing that turns `mon-fri` into `MON-FRI`.

So the format is a parse-time argument only. The XML schema, the HTTP API and the dashboard do not take
one: a five-field expression is written in C#, not stored. Macros have no such limit.
:::

**The format is stated, never detected.** Detecting it from the field count would make a *dropped*
field silent: `0 0 12 * * ?` without its month field is `0 0 12 * ?`, a valid crontab line meaning
midnight on the 12th, not noon every day. The same digit also names a different day in each dialect. A
five-field expression given without a format fails with an error naming the method that reads it.

### An expression copied from Spring

::: danger A Spring expression with a numeric day-of-week schedules the wrong day
Spring's `@Scheduled(cron = …)` has **six** fields, seconds first — the shape of a Quartz expression — so
Quartz accepts it. But Spring numbers days the Unix way, 0-7 with 0 and 7 both Sunday, where Quartz uses
1-7 from Sunday. Every numeric day reads one day earlier than written, silently, on every Quartz version.

| `@Scheduled(cron = …)` | Spring fires | Quartz reads it as |
|:---|:---|:---|
| `0 0 9 * * 1` | 09:00 on Mondays | 09:00 on **Sundays** |
| `0 0 9 * * 1-5` | 09:00 Monday to Friday | 09:00 **Sunday to Thursday** |
| `0 0 9 * * 6` | 09:00 on Saturdays | 09:00 on **Fridays** |
| `0 0 9 * * MON-FRI` | 09:00 Monday to Friday | 09:00 Monday to Friday |

**Write the day as a name.** `SUN` through `SAT` mean the same day in Spring, crontab and Quartz, so a
named expression survives being pasted either way, as the last row shows.

There is no `CronFormat.Spring` and no detection of one. The two six-field dialects have the same shape,
so the string cannot tell them apart — which is why [the format is stated](#the-unix-five-field-form).
A `CronFormat.Spring` member could be added later without breaking anything; auto-detection never
could, because the existing six-field reading is the one schedules depend on.
:::

### If your expressions came from another .NET cron library

Cronos and NCrontab expressions parse here, and almost all fire at the same instants. Two differences
are silent, because the expression is valid Quartz cron that means something else:

- **Day-of-week numbering.** Quartz numbers `1-7` with Sunday `1`; those libraries number `0-6` with
  Sunday `0`, and crontab `0-7` with both ends Sunday. `0 0 2 * * 1` is 02:00 on **Sunday** here and on
  **Monday** there — the same issue as [a Spring expression](#an-expression-copied-from-spring), with the
  same fix: write the day as a name.
- **Both day fields restricted.** Quartz fires on the **union** of day-of-month and day-of-week, as
  crontab does; Cronos takes the intersection. `0 0 2 5 * MON` is "the 5th, and every Monday" here and
  "the 5th, if it is a Monday" there. Put `?` in one of the two fields to say which one applies.

Searching from `2026-08-21T00:00:00Z`, against Cronos 0.11.0:

| Expression | Quartz | Cronos | |
|:---|:---|:---|:---|
| `0 0 2 * * MON` | 2026-08-24 02:00 | 2026-08-24 02:00 | same |
| `0 0 7 * * *` | 2026-08-21 07:00 | 2026-08-21 07:00 | same |
| `0 0 */1 * * *` | 2026-08-21 01:00 | 2026-08-21 01:00 | same |
| `0 */15 * * * *` | 2026-08-21 00:15 | 2026-08-21 00:15 | same |
| `0 30 6 * * MON,TUE,WED,THU,FRI` | 2026-08-21 06:30 | 2026-08-21 06:30 | same |
| `0 0 2 * * 1` | 2026-08-23 (**Sunday**) | 2026-08-24 (**Monday**) | **differ** |
| `0 0 2 5 * MON` | 2026-08-24 (union) | 2026-10-05 (intersection) | **differ** |

A **five**-field expression from those libraries is read by `CronFormat.Unix`, which renumbers the days:
`CronExpression.Parse("0 2 * * 1", CronFormat.Unix)` is Monday, as its author meant. See
[The Unix five-field form](#the-unix-five-field-form).

::: tip Upgrading from 3.x with such an expression in the database
3.x required `?` in exactly one day field, so it rejected an expression with both day fields restricted;
4.0 stores and fires it. The migration guide has an audit for both shapes above:
[If your expressions came from another cron library](migration-guide.md#if-your-expressions-came-from-another-cron-library).
:::

## H (hash) for load distribution

`H` ("hash") spreads triggers that share an expression. Many triggers on `0 0 0 * * ?` (midnight daily)
all fire at once and cause a resource spike; `H` in place of a value gives each trigger a different one.

`H` resolves to a **deterministic** value derived from the trigger's identity (name and group). It stays
the same while the identity does, and differs between triggers.

### Syntax

| **Expression** | **Meaning** |
|:---------------|:------------|
| `H`            | Hash value within the full range of the field |
| `H(0-7)`       | Hash value constrained to the range 0 through 7 |
| `H/15`         | Hash-derived offset, then repeat every 15 (e.g., 7, 22, 37, 52) |
| `H(0-29)/10`   | Hash-derived offset in 0-29, then repeat every 10 (e.g., 3, 13, 23) |

- `H` can appear in a **comma-separated list** with fixed values, e.g. `H,30,45`.
- `H` is **not** allowed in the Year field, and cannot be combined with `L`, `W` or `#`.

### Hash examples

| **Expression** | **Description** |
|:---------------|:----------------|
| `0 H H * * ?`       | Once per day at a hash-derived hour and minute |
| `0 H H(0-7) * * ?`  | Once per day between midnight and 7:59 AM |
| `0 H/15 * * * ?`    | Every 15 minutes, starting from a hash-derived offset |
| `H H H * * ?`       | Once per day at a unique second, minute, and hour |

### Usage with TriggerBuilder

Through the builder, the trigger identity is the hash seed. You **must** call `WithIdentity()`, so the
hash comes from a stable name rather than a random GUID:

<!-- snippet: sample_cron_expressions_hash_from_trigger_name -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("nightly-cleanup")
    .WithCronSchedule("0 H H(0-7) * * ?")
    .Build();
```
<!-- endSnippet -->

Or give an explicit hash key, which needs no trigger identity. The key rides on the `CronExpression`,
and `WithCronSchedule` takes one directly:

<!-- snippet: sample_cron_expressions_hash_key_on_expression -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithCronSchedule(CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup"))
    .Build();
```
<!-- endSnippet -->

`CronExpression.ParseWithHash` resolves the expression on its own; `CronExpression.TryParseWithHash` is
the non-throwing form, for a key and expression from outside your control. Both take a
[`CronFormat`](#the-unix-five-field-form) as their second argument for an expression not written the
Quartz way:

<!-- snippet: sample_cron_expressions_hash_key -->
```csharp
CronExpression expr = CronExpression.ParseWithHash("0 H H(0-7) * * ?", "nightly-cleanup");
```
<!-- endSnippet -->

::: tip
`CronExpressionString` returns the **resolved** expression (e.g., `"0 23 3 * * ?"`), with `H` replaced
by its computed value. That resolved form is what is persisted, so it is stable across restarts.
:::

## Building cron expressions programmatically

To build a schedule from user input, such as a scheduling UI with dropdowns, use the fluent
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

`WithCronSchedule` accepts the builder (or a built `CronExpression`) directly, so the chain needs no
`CronScheduleBuilder`. Call `Build()` yourself when you want the `CronExpression` as a value.

Each field has a single-value, list, range and increment form (e.g. `WithHour`, `WithHours`,
`WithHourRange`, `WithHourIncrements`). `AtTime` sets the second, minute and hour together from a
`TimeOnly`; add the days beside it:

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

Cron resolves to a whole second, so the sub-second part of the `TimeOnly` is ignored.

The special characters have dedicated methods:

<!-- snippet: sample_cron_expressions_day_rules -->
```csharp
CronExpressionBuilder.Create().OnLastDayOfMonth();                         // "* * * L * ?"
CronExpressionBuilder.Create().OnNearestWeekdayOfMonth(15);                // "* * * 15W * ?"
CronExpressionBuilder.Create().OnNthDayOfWeekOfMonth(DayOfWeek.Friday, 3); // "* * * ? * FRI#3"
CronExpressionBuilder.Create().OnLastDayOfWeekOfMonth(DayOfWeek.Friday);   // "* * * ? * FRIL"
```
<!-- endSnippet -->

Rules:

- Unconfigured fields default to `*` (every value).
- Each field can be configured once; configuring it again throws `InvalidOperationException`.
- The builder uses one day field: it renders the other as `?` and throws if you configure both. That is
  the builder's rule, not cron's. An expression naming both day fields fires on their union
  (`0 15 10 1,2,3 * MON,FRI`) and `CronExpression.Parse` accepts it, so write that one as text.
- Values are validated against each field's range as they are set, and `Build()` returns a validated
  `CronExpression`. Use `ToString()` if you only need the string.
- Days of the week are emitted as names (`MON`, `FRI`, ...), so the expression means the same day in
  cron dialects that number weekdays differently.

## Checking an expression

Put an expression you were given — by a colleague, an online generator or an old configuration file —
through the parser before it reaches a scheduler. `CronExpression.TryParse` says whether Quartz.NET can
read it; `GetNextValidTimeAfter` says what it means:

<!-- snippet: sample_cron_expressions_preview -->
```csharp
// does Quartz.NET accept it?
if (!CronExpression.TryParse("0 0/15 8-17 ? * MON-FRI", out CronExpression? expression))
{
    throw new ArgumentException("Quartz.NET cannot read that expression");
}

// what does it mean? - the next five times it fires, in the expression's own time zone
DateTimeOffset after = DateTimeOffset.UtcNow;
for (int i = 0; i < 5; i++)
{
    DateTimeOffset? fireTime = expression.GetNextValidTimeAfter(after);
    if (fireTime is null)
    {
        break;
    }

    Console.WriteLine(TimeZoneInfo.ConvertTime(fireTime.Value, expression.TimeZone));
    after = fireTime.Value;
}
```
<!-- endSnippet -->

A schedule off by a day or an hour looks plausible as a string and obvious as a list of dates. This is
also the only check that covers the whole dialect: [`H`](#h-hash-for-load-distribution), a wrapping range
(`22-2`, `FRI-MON`), [`MON/2`](#mon-2-is-a-step-through-the-week) and how the two day fields combine
each differ from Java Quartz, Unix cron or both, and no external tool implements them.

- `GetNextValidTimeAfter` returns `null` when there is no further fire time (for example, an expression
  naming a past year), so the loop stops.
- The times are `DateTimeOffset`. `TimeZone` is the zone the expression is read in (the local zone
  unless you passed one); converting before printing makes [daylight saving time](#daylight-saving-time)
  visible.
- `CronExpression.Parse` throws instead of returning `false`. Both take a
  [`CronFormat`](#the-unix-five-field-form).

## Examples

| **Expression**             | **Meaning**                                                                          |
|:---------------------------|:-------------------------------------------------------------------------------------|
| `0 0 12 * * ?`             | 12:00 (noon) every day                                                               |
| `0 15 10 ? * *`            | 10:15 every day                                                                      |
| `0 15 10 * * ?`            | 10:15 every day                                                                      |
| `0 15 10 * * ? *`          | 10:15 every day                                                                      |
| `0 15 10 * * ? 2005`       | 10:15 every day during 2005                                                          |
| `0 * 14 * * ?`             | Every minute from 14:00 to 14:59, every day                                          |
| `0 0/5 14 * * ?`           | Every 5 minutes from 14:00 to 14:55, every day                                       |
| `0 0/5 14,18 * * ?`        | Every 5 minutes from 14:00 to 14:55 and from 18:00 to 18:55, every day               |
| `0 0-5 14 * * ?`           | Every minute from 14:00 to 14:05, every day                                          |
| `0 10,44 14 ? 3 WED`       | 14:10 and 14:44 every Wednesday in March                                             |
| `0 15 10 ? * MON-FRI`      | 10:15 Monday to Friday                                                               |
| `0 15 10 15 * ?`           | 10:15 on the 15th of every month                                                     |
| `0 15 10 L * ?`            | 10:15 on the last day of every month                                                 |
| `0 15 10 L-2 * ?`          | 10:15 two days before the last day of every month                                       |
| `0 15 10 ? * 6L`           | 10:15 on the last Friday of every month                                              |
| `0 15 10 ? * 6L 2002-2005` | 10:15 on the last Friday of every month in 2002, 2003, 2004 and 2005                 |
| `0 15 10 ? * 6#3`          | 10:15 on the third Friday of every month                                             |
| `0 0 12 1/5 * ?`           | 12:00 every 5 days, starting on the 1st of the month                                 |
| `0 11 11 11 11 ?`          | 11:11 every 11 November                                                              |
| `0 15 10 1,2,3 * MON,FRI`  | 10:15 on the 1st, 2nd and 3rd of the month, and every Monday and Friday              |
| `H H H * * ?`              | Once a day at a hash-derived second, minute and hour (spread across triggers)        |
| `0 H H(0-7) * * ?`         | Once a day between midnight and 7:59, at a hash-derived time                         |
| `0 H/15 * * * ?`           | Every 15 minutes, starting from a hash-derived offset                                |

::: tip
Mind `?` and `*` in the two day fields. A day field written exactly `*` or `?` names no days, so it
restricts nothing and the other field decides: `0 15 10 1 * *` fires on the 1st, and `0 15 10 * * MON`
every Monday. When **both** fields name days, the expression fires on their union, as
`0 15 10 1,2,3 * MON,FRI` above does; when neither does, every day matches. This is the Unix
`crontab(5)` rule. Some other cron implementations intersect the two fields, so an expression copied
from one of those fires more often here.
:::

## Daylight saving time

A cron expression names a wall-clock time, and a daylight saving transition makes a wall-clock time
missing or ambiguous. **Nothing is skipped and nothing fires twice.** Which instant fires depends on
whether the expression names a fixed time or an interval.

A **fixed-time** expression has plain values, or comma lists of plain values, in its second, minute and
hour fields — `0 30 2 * * ?`, `0 0,30 2 * * ?`:

- A time the clocks **skip** fires once, at the **end of the gap**, the instant the clocks moved. A daily
  `0 30 2 * * ?` over a 02:00-03:00 spring-forward gap fires at 03:00. Every wall-clock time in the gap
  names that instant, so an expression matching several of them still fires once. `IsSatisfiedBy` agrees
  with that fire time.
- A time that **occurs twice** on a fall-back day fires once, at the **first** occurrence.

An **interval** expression has a wildcard, a step or a range in the second, minute or hour field, such
as `0 * * * * ?` or `0 0/30 * * * ?`. It keeps firing through the repeated hour, so both passes run. Over
a spring-forward gap, the gap-end rule shows as an extra fire: `0 30 * * * ?` fires at 03:00 for the
occurrence the gap swallowed and at 03:30 for the next hour's.

A `CronCalendar` written over the skipped hour excludes the gap's end for the same reason.

::: warning Quartz 3.x behaves differently
On 3.x a skipped time is shifted forward by the transition's delta: the daily `0 30 2 * * ?` above fires
at 03:30, an instant its expression does not match. An interval expression fires the repeated hour only
once, so an "every minute" schedule loses an hour of real time each autumn.
:::

**Name the time zone.** An expression with none uses `TimeZoneInfo.Local`: the developer's machine in
development, and often UTC in a container.
[Daylight saving, clock changes and cluster skew](../best-practices.md#daylight-saving-clock-changes-and-cluster-skew)
covers the choice of trigger family, and the [FAQ](../faq.md#daylight-saving-time-and-triggers) has more.
