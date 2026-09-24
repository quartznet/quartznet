---
title: 'Compile-Time Checks'
---

<ApplicableVersion version="4.2" />

<!-- The C# blocks on this page are hand-written rather than `snippet:` markers, and have to be. Most
     of them are the code the analyzer refuses: a sample project carrying them would fail `Compile`
     with QZ0001 and QZ0002, which is the analyzer working rather than a sample rotting. -->

`Quartz` ships an analyzer that reports, at build time, mistakes that otherwise throw at run time:

| Mistake | Otherwise throws |
|---|---|
| a cron expression that cannot parse, in `AddQuartz` | while the host starts |
| the same, in `ScheduleJob` | when the scheduling code runs |
| `[JobTimeout("5 minutes")]` | the first time the timeout middleware reflects over the job, after deployment |

The analyzer is inside the package, under `analyzers/dotnet/cs`, so referencing `Quartz` is enough:

```xml
<PackageReference Include="Quartz" Version="4.2.0" />
```

It reads cron with the same parser Quartz uses at run time. `H`, wrapping ranges, `L`, `W`, `#`, the `@`
macros and the five-field Unix dialect mean the same thing, and the compiler prints the message the
exception would have carried.

## What it checks

| Id | Severity | What it says |
|---|---|---|
| [`QZ0001`](#qz0001-invalidcronexpression) | Error | A cron literal or constant that the parser refuses |
| [`QZ0002`](#qz0002-invalidjobtimeout) | Error | A `[JobTimeout]` argument that is not a `TimeSpan`, or is negative |
| [`QZ0003`](#qz0003-persistjobdatawithoutdisallowconcurrent) | Warning | A job that persists its data map and allows concurrent firings |
| [`QZ0004`](#qz0004-cancellationtokennotobserved) | Info | A job body that awaits or loops without reading its cancellation token |

Only values the compiler already knows are checked: a string literal, a `const`, an interpolated string
with nothing interpolated. A value built at run time, read from configuration or passed in a variable is
skipped silently, rather than guessed at.

To change a severity or turn a check off, see
[Changing a severity, or turning it off](#changing-a-severity-or-turning-it-off).

### QZ0001 InvalidCronExpression

**Reports** a cron string that cannot parse, at every call that takes one: `CronScheduleBuilder.Create`,
`WithCronSchedule`, the `CronExpression` constructors, `CronExpression.Parse`, `TryParse`,
`ParseWithHash`, `TryParseWithHash` and `ResolveHash`, the `CronCalendar` constructors and
`CronTriggerImpl`'s. Also on the [`[CronTrigger]`](declaring-jobs-with-attributes.md) attribute.

```csharp
// error QZ0001: '0 0 12 * *' is not a valid cron expression: ... has 5 fields, but 6 or 7 are
// required: seconds, minutes, hours, day-of-month, month, day-of-week, and optionally year.
_ = TriggerBuilder.Create().WithCronSchedule("0 0 12 * *").Build();
```

The call decides how the literal is read:

* **The dialect.** A sibling `CronFormat` argument is honoured: `CronExpression.Parse("30 4 * * 1",
  CronFormat.Unix)` is a valid crontab line, and the same five fields without it are not. A `CronFormat`
  the compiler cannot evaluate skips the check, because the same digits mean different days in each
  dialect.
* **Whether `H` is resolved.** `WithCronSchedule("0 H 3 * * ?")` is valid: the builder resolves the hash
  against the trigger key later. `CronExpression.Parse("0 H 3 * * ?")` is not: it has no key. The
  analyzer reports what each entry point would throw.
* **`TryParse` is checked too.** A literal that can never parse makes a `TryParse` that can only return
  `false`, which is a bug.
* **Missing expressions.** A constant that is `null`, empty or whitespace is reported as missing:
  `error QZ0001: The cron expression is missing: the argument is null`. A `null` is allowed only where
  the parameter is `string?`: `TryParse` and `TryParseWithHash` return `false` for it; every other entry
  point throws.

**Fix** the expression, pass the `CronFormat` it is written in, or use an entry point that resolves `H`.
**Suppress** with `dotnet_diagnostic.QZ0001.severity` in `.editorconfig`.

### QZ0002 InvalidJobTimeout

**Reports** a [`[JobTimeout]`](job-execution-middleware.md) argument that is not an invariant `TimeSpan`
string, or is negative. The argument is a string because an attribute argument cannot be a `TimeSpan`,
and nothing parses it until the timeout middleware reflects over the job type.

```csharp
[JobTimeout("5 minutes")] // error QZ0002: '5 minutes' is not a TimeSpan. Spell the job's timeout the
public class ReportJob : IJob // way TimeSpan does, invariantly: "00:05:00" for five minutes.
```

**Fix** by spelling the budget as `TimeSpan` does: `"00:05:00"` for five minutes. `"00:00:00"` is not
reported: zero means the job has no timeout, whatever the scheduler's default.
**Suppress** with `dotnet_diagnostic.QZ0002.severity` in `.editorconfig`.

### QZ0003 PersistJobDataWithoutDisallowConcurrent

**Reports** a job with `[PersistJobDataAfterExecution]` and without `[DisallowConcurrentExecution]`. The
data map is re-stored when a firing completes; with concurrent firings, two read the same map, change it
independently and store it in turn, so the first write is silently lost.

```csharp
[PersistJobDataAfterExecution]
[DisallowConcurrentExecution] // without this line: warning QZ0003
public class CounterJob : IJob
```

Both attributes are read as Quartz reads them at run time: on the type, a base class, or any interface
it implements. A job that gets `[DisallowConcurrentExecution]` from an interface is not reported; one
that inherits `[PersistJobDataAfterExecution]` from one is.

**Fix** by adding `[DisallowConcurrentExecution]`.
**Suppress** with `dotnet_diagnostic.QZ0003.severity` in `.editorconfig`.

### QZ0004 CancellationTokenNotObserved

**Reports** a job body that awaits or loops and never reads its cancellation token: neither the
parameter nor `IJobExecutionContext.CancellationToken`, which is the same token. The scheduler cancels
it on shutdown and on `IScheduler.Interrupt`; a body that ignores it runs to completion regardless.

```csharp
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    await Task.Delay(TimeSpan.FromMinutes(5)); // info QZ0004
}
```

* The body read is the one the scheduler runs for `IJob.Execute`: the implementation (explicit or not),
  an `override` in a job deriving from a virtual or abstract base, or the half of a `partial` method
  with the body. An `Execute` that hides the base job's with `new` is not read.
* It is information, not a warning: whether work is interruptible is a judgement no analyzer can make,
  and a job that returns in a millisecond is right to ignore the token. The await-or-loop condition
  keeps it off jobs that could not honour a cancellation anyway.
* `CA2016`, shipped with the .NET SDK, covers the related case: a token that is not forwarded to a call
  that takes one.

**Fix** by reading the token: pass it to what the job awaits, or check it in the loop.
**Suppress** with `dotnet_diagnostic.QZ0004.severity` in `.editorconfig`, as in the example below.

## Changing a severity, or turning it off

Each diagnostic is an ordinary compiler diagnostic, so `.editorconfig` sets its severity:

```ini
[*.cs]
# A cron literal that cannot parse is only a warning here
dotnet_diagnostic.QZ0001.severity = warning

# This codebase does not want the cancellation-token hint
dotnet_diagnostic.QZ0004.severity = none
```

To remove the analyzer from the build entirely, set one property in the project file:

```xml
<PropertyGroup>
  <DisableQuartzAnalyzers>true</DisableQuartzAnalyzers>
</PropertyGroup>
```

::: tip
The property removes the whole assembly, so the [source generator](declaring-jobs-with-attributes.md)
and its `AddDeclaredJobs()` go with the four diagnostics; nothing else Quartz does is affected. To quiet
one rule, use `.editorconfig` instead. `ExcludeAssets="analyzers"` on the package reference does not
work: the .NET 10 SDK still passes the assembly to the compiler.
:::

## What it does not check

* **RRULE literals** (`RecurrenceScheduleBuilder.Create`). The recurrence parser is not BCL-clean, so it
  cannot be linked into an assembly the compiler loads.
* **Time-zone ids** (`TimeZones.FindById`). The build machine's zone database is not the run-time
  machine's; refusing a zone your server has would be worse than no check.
* **Retry policies, preferred nodes, key names and type-name strings.** Each is rare in code, refused by
  name at startup, or already `[RequiresUnreferencedCode]`.

## Related

[Declaring Jobs with Attributes](declaring-jobs-with-attributes.md) covers the rest of the 4.2
compile-time work: attributes that declare a job's schedule on the job class, and a source generator,
in the same assembly, that registers them. Its diagnostics are `QZ1001`, `QZ1002`, `QZ1003` and
`QZ1004`; the cron expression on an attribute is checked by `QZ0001` above.
