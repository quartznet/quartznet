---
title: 'Compile-Time Checks'
---

<ApplicableVersion version="4.2" />

<!-- The C# blocks on this page are hand-written rather than `snippet:` markers, and have to be. Most
     of them are the code the analyzer refuses: a sample project carrying them would fail `Compile`
     with QZ0001 and QZ0002, which is the analyzer working rather than a sample rotting. -->

A cron expression that cannot parse is an exception, and the question is only how long you wait for it.
`AddQuartz` raises one while the host is starting; `ScheduleJob` raises one when the code that schedules
finally runs; `[JobTimeout("5 minutes")]` raises one the first time the timeout middleware reflects over
the job, which is after deployment.

`Quartz` ships an analyzer that asks the same questions at build time. It is inside the package — under
`analyzers/dotnet/cs` — so referencing `Quartz` is all there is to do:

```xml
<PackageReference Include="Quartz" Version="4.2.0" />
```

The analyzer reads cron with the very parser Quartz reads it with at run time. It is not a smaller
reimplementation: `H`, wrapping ranges, `L`, `W`, `#`, the `@` macros and the five-field Unix dialect all
mean here exactly what they mean there, and the sentence the compiler prints is the sentence the
exception would have carried.

## What it checks

| Id | Severity | What it says |
|---|---|---|
| [`QZ0001`](#qz0001-invalidcronexpression) | Error | A cron literal or constant that the parser refuses |
| [`QZ0002`](#qz0002-invalidjobtimeout) | Error | A `[JobTimeout]` argument that is not a `TimeSpan`, or is negative |
| [`QZ0003`](#qz0003-persistjobdatawithoutdisallowconcurrent) | Warning | A job that persists its data map and allows concurrent firings |
| [`QZ0004`](#qz0004-cancellationtokennotobserved) | Info | A job body that awaits or loops without reading its cancellation token |

Only values the compiler already knows are read: a string literal, a `const`, an interpolated string with
nothing interpolated into it. An expression built at run time, read from configuration or passed in a
variable is left alone — silently, because an analyzer that guessed would be worse than one that did not
look.

### QZ0001 InvalidCronExpression

Reported at every call that takes a cron expression string: `CronScheduleBuilder.Create`,
`WithCronSchedule`, the `CronExpression` constructors, `CronExpression.Parse`, `TryParse`,
`ParseWithHash`, `TryParseWithHash` and `ResolveHash`, the `CronCalendar` constructors and
`CronTriggerImpl`'s — and at the one entry point that is not a call, the
[`[CronTrigger]`](declaring-jobs-with-attributes.md) attribute a job declares its schedule with.

```csharp
// error QZ0001: '0 0 12 * *' is not a valid cron expression: ... has 5 fields, but 6 or 7 are
// required: seconds, minutes, hours, day-of-month, month, day-of-week, and optionally year.
_ = TriggerBuilder.Create().WithCronSchedule("0 0 12 * *").Build();
```

Two things decide how a literal is read, and both come from the call rather than from the string:

- **The dialect.** A sibling `CronFormat` argument is honoured, so `CronExpression.Parse("30 4 * * 1",
  CronFormat.Unix)` is a valid crontab line and the same five fields without it are not. A `CronFormat`
  the compiler cannot evaluate skips the check entirely: the same digits mean different days in each
  dialect, so there is nothing to check rather than something to guess.
- **Whether `H` is resolved.** `WithCronSchedule("0 H 3 * * ?")` is valid, because the builder resolves
  the hash against the trigger key later; `CronExpression.Parse("0 H 3 * * ?")` is not, because it has no
  key to resolve it against. The analyzer reports exactly what each entry point would have thrown.

`TryParse` is checked too. Its contract is that an expression *might* not parse; a literal that can
never parse is still a bug, and a `TryParse` that can only return `false` is not what anybody wrote.

A constant that is `null`, empty or only whitespace is reported as a missing expression rather than as a
parse error — `error QZ0001: The cron expression is missing: the argument is null`. A `null` is left
alone only where the parameter is `string?`: `TryParse` and `TryParseWithHash` answer `false` for it by
contract, where every other entry point throws.

### QZ0002 InvalidJobTimeout

[`[JobTimeout]`](job-execution-middleware.md) takes its budget as an invariant `TimeSpan` string, because
an attribute argument cannot be a `TimeSpan`. The constructor parses it, and nothing calls that
constructor until the timeout middleware reflects over the job type.

```csharp
[JobTimeout("5 minutes")] // error QZ0002: '5 minutes' is not a TimeSpan. Spell the job's timeout the
public class ReportJob : IJob // way TimeSpan does, invariantly: "00:05:00" for five minutes.
```

A negative budget is reported too. `"00:00:00"` is not: zero is how a job says it has no timeout,
whatever the scheduler's default is.

### QZ0003 PersistJobDataWithoutDisallowConcurrent

`[PersistJobDataAfterExecution]` re-stores the job data map when a firing completes. With concurrent
firings allowed, two of them read the same map, change it independently and store it one after the
other — so whatever the first wrote is gone, and nothing anywhere says so.

```csharp
[PersistJobDataAfterExecution]
[DisallowConcurrentExecution] // without this line: warning QZ0003
public class CounterJob : IJob
```

Both attributes are read the way Quartz reads them at run time: the type's own declaration, a base class
it inherits from, or any interface it implements. So a job that takes `[DisallowConcurrentExecution]`
from a contract it fulfils is not reported, and a job that inherits `[PersistJobDataAfterExecution]` from
one is.

### QZ0004 CancellationTokenNotObserved

The token `IJob.Execute` is handed is the one the scheduler cancels on shutdown and on
`IScheduler.Interrupt`. A body that awaits or loops and never reads it — neither the parameter nor
`IJobExecutionContext.CancellationToken`, which is the same token — runs to completion whatever the
scheduler asks of it.

```csharp
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    await Task.Delay(TimeSpan.FromMinutes(5)); // info QZ0004
}
```

The body read is the one the scheduler runs when it calls `IJob.Execute`: the implementation, explicit or
not, an `override` of it in a job deriving from a virtual or abstract base, or the half of a `partial`
method that carries the body. A method called `Execute` that hides the base job's with `new` is not that
body, and is not read.

This is information rather than a warning, and deliberately. Whether a piece of work is interruptible is
a judgement no analyzer can make, and a job that returns in a millisecond is right to ignore the token.
The `await`-or-loop condition is what keeps it off the jobs that could not honour a cancellation anyway.
`CA2016`, which the .NET SDK ships, covers the neighbouring case: a token that exists and is not
forwarded to a call that would take one.

## Changing a severity, or turning it off

Each diagnostic is an ordinary compiler diagnostic, so `.editorconfig` moves it:

```ini
[*.cs]
# A cron literal that cannot parse is only a warning here
dotnet_diagnostic.QZ0001.severity = warning

# This codebase does not want the cancellation-token hint
dotnet_diagnostic.QZ0004.severity = none
```

To take the analyzer out of the build entirely, exclude the analyzer assets from the package reference:

```xml
<PackageReference Include="Quartz" Version="4.2.0" ExcludeAssets="analyzers" />
```

::: tip
`ExcludeAssets="analyzers"` is a whole-package switch and affects nothing else Quartz does. Prefer the
`.editorconfig` route when what you want is one rule quieter rather than all four gone.
:::

## What it does not check

Some things look checkable and are not, and each is left out for a reason rather than for want of time.

- **RRULE literals** (`RecurrenceScheduleBuilder.Create`). The recurrence parser is not BCL-clean, so it
  cannot be linked into an assembly the compiler loads.
- **Time-zone ids** (`TimeZones.FindById`). The build machine's zone database is not the run-time
  machine's, and an analyzer that refused a zone your server has would be worse than no analyzer.
- **Retry policies, preferred nodes, key names and type-name strings.** Each is either rare in code,
  refused by name at startup, or already `[RequiresUnreferencedCode]`.

## Related

The analyzer half of the 4.2 compile-time work is this page. The other half — attributes that declare a
job's schedule where the job is written, and a source generator that registers them — is
[Declaring Jobs with Attributes](declaring-jobs-with-attributes.md), and ships in the same assembly.
Its four diagnostics are `QZ1001`, `QZ1002`, `QZ1003` and `QZ1004`; the cron expression an attribute
carries is read by `QZ0001` above.
