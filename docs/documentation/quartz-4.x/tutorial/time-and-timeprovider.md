---
title: 'Time and TimeProvider'
---

In 4.x every part of a scheduler reads the current time from one `TimeProvider`, injected like any other
service: the scheduling loop, every trigger's fire-time computation, the misfire handler and the cluster
check-in.

## SystemTime is gone

3.x had `SystemTime.UtcNow`, a mutable static `Func<DateTimeOffset>` you assigned to. It was global, so
two tests that each wanted a fake clock could not run at the same time.

4.x uses the .NET `TimeProvider` as a *per-scheduler* service. The clock is injected, not assigned, and
one scheduler's fake clock does not affect another.

::: tip
`DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.Today` and the implicit
`DateTime` → `DateTimeOffset` conversion are banned in the Quartz codebase by an analyzer. `UtcNow` is
allowed where there is no scheduler to ask. Avoid them in your own jobs too: a job that reads
`DateTime.Now` cannot be tested on a fake clock.
:::

## Setting the clock

One call, on either builder:

<!-- snippet: sample_time_provider_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UseTimeProvider(myTimeProvider);
});
```
<!-- endSnippet -->

<!-- snippet: sample_time_provider_standalone -->
```csharp
IScheduler scheduler = await QuartzSchedulerBuilder
    .Create(q => q.UseTimeProvider(myTimeProvider))
    .BuildScheduler();
```
<!-- endSnippet -->

### Precedence

Most specific first:

1. **`UseTimeProvider(...)` on this scheduler.** Wins over everything.
2. **A `TimeProvider` registered in the container.** A named scheduler with no clock of its own
   inherits the application's.
3. **The legacy `quartz.timeProvider.type` property key.**
4. **`TimeProvider.System`.**

* The container-wide default is registered with `TryAddSingleton`. An application that already registers
  a `TimeProvider` (`services.AddSingleton(TimeProvider.System)`, or a test clock) keeps it, and every
  scheduler uses it.
* `UseTimeProvider` on a **named** scheduler registers the clock keyed by that scheduler's name. On the
  **default** scheduler it replaces the container's unkeyed `TimeProvider`. So a fake clock given to one
  named scheduler does not change the others.

## How far the clock reaches

Everything the container builds for a scheduler gets that scheduler's clock:

| Component | What it uses the clock for |
|---|---|
| `QuartzScheduler` / the scheduling loop | deciding whether a trigger is due, `StartDelayed` |
| `RAMJobStore` | fire times, misfire detection |
| The ADO.NET store | the same, plus retry backoff |
| `IDriverDelegate` (via `DriverDelegateContext.TimeProvider`) | timestamps written to the database |
| `ILockHandler` (via `LockHandlerContext.TimeProvider`) | lock-acquisition backoff |
| `MisfireHandler` | its scan interval |
| `ClusterManager` | check-in interval and failed-node detection |

A custom job store, driver delegate or lock handler gets the same clock by taking a `TimeProvider`
constructor parameter or reading the one its context carries. It is resolved for the scheduler the
component belongs to.

## Builders and the clock

Only the trigger builder takes a clock:

<!-- A listing of signatures rather than code, so it is written out here rather than compiled. -->

```csharp
TriggerBuilder.Create(TimeProvider? timeProvider = null);
TriggerBuilder.Create<TJob>(TimeProvider? timeProvider = null);
```

The builder uses the clock:

* as the default `StartTimeUtc` when you do not call `StartAt`;
* in `StartNow()`;
* passed to the schedule builder at `Build()`, so a schedule computed there
  (`DailyTimeIntervalScheduleBuilder.EndingDailyAfterCount(n)`) uses the same clock;
* passed to the trigger, which keeps it. See [Which clock a trigger holds](#which-clock-a-trigger-holds).

The five schedule builders take **no** clock. They describe a shape (every day at 09:00, every 15
minutes, the third Tuesday); when one needs "now", it gets it from the trigger builder.

<!-- snippet: sample_time_provider_schedule_builders -->
```csharp
CronScheduleBuilder.Create(cronExpression);
SimpleScheduleBuilder.Create();
CalendarIntervalScheduleBuilder.Create();
DailyTimeIntervalScheduleBuilder.Create();
RecurrenceScheduleBuilder.Create(recurrenceRule);
```
<!-- endSnippet -->

`DateBuilder` has two statics, both taking an optional clock:

<!-- snippet: sample_time_provider_date_builder -->
```csharp
DateTimeOffset when = DateBuilder.Create(timeProvider).InYear(2027).InMonthOnDay(3, 15).AtHourOfDay(9).Build();
DateTimeOffset local = DateBuilder.CreateInTimeZone(tz, timeProvider).AtHourMinuteAndSecond(9, 30, 0).Build();
```
<!-- endSnippet -->

::: warning Changed in 4.x
`DateBuilder.NewDate` / `NewDateInTimeZone` are now `Create` / `CreateInTimeZone`, and
`CronScheduleBuilder.CronSchedule(...)` is `CronScheduleBuilder.Create(...)`: the whole family uses a
`Create` factory. `DailyTimeIntervalScheduleBuilder.Create()` also **lost its `TimeProvider`
parameter**; it takes the trigger builder's clock, so `EndingDailyAfterCount` respects a fake clock.
:::

## The trap: triggers built outside the container

`AddTrigger<TJob>` and `ScheduleJob<T>` in DI configuration create their builder as
`TriggerBuilder.Create<TJob>(serviceProvider.GetService<TimeProvider>())`, so a trigger configured there
starts on the scheduler's clock:

<!-- snippet: sample_time_provider_configured_trigger -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UseTimeProvider(fakeClock);

    // this trigger's implicit start time is the fake clock's now
    q.AddTrigger<ReportJob>(t => t
        .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever()));
});
```
<!-- endSnippet -->

A trigger you build yourself does not:

<!-- snippet: sample_time_provider_wall_clock_trigger -->
```csharp
// StartTimeUtc is the WALL CLOCK, whatever the scheduler's TimeProvider says
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("hourly")
    .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
    .Build();
```
<!-- endSnippet -->

This is the most common surprise in a fake-clock test: the scheduler is on 2024-01-01, but the trigger
starts at the real current time. Pass the clock:

<!-- snippet: sample_time_provider_trigger_builder_clock -->
```csharp
ITrigger trigger = TriggerBuilder.Create(fakeClock)
    .WithIdentity("hourly")
    .StartAt(fakeClock.GetUtcNow())
    .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
    .Build();
```
<!-- endSnippet -->

In tests, set `StartAt` explicitly; then the builder's clock does not matter.

## Which clock a trigger holds

A trigger is given a clock when it is created and keeps it. Everything it later reads as "now" (the
past-due clamp in `ComputeFirstFireTimeUtc`, and all of `UpdateAfterMisfire`) comes from that clock.

| A trigger produced by | holds |
|---|---|
| `TriggerBuilder.Create(clock)` | `clock` |
| `TriggerBuilder.Create()` | `TimeProvider.System` |
| `AddTrigger<TJob>` / `ScheduleJob<T>` in DI configuration | the scheduler's `TimeProvider` |
| `trigger.GetTriggerBuilder().Build()` | whatever `trigger` held |
| `new CronTriggerImpl(clock)` and its siblings | `clock`, or `TimeProvider.System` when omitted |
| a job store reading it back | the clock that store's scheduler runs on |

For the last row:

* `RAMJobStore` returns the object it was given, so a stored trigger keeps the clock that built it.
* The ADO.NET store builds a new trigger on every read and gives it the store's clock. That includes a
  trigger deserialized from `BLOB_TRIGGERS`, because the clock field is not serialized.

The store detects a misfire by its own clock, and the trigger computes the recovery by its own clock.
They must be the same clock; otherwise a scheduler on a `FakeTimeProvider` recovers a 2024 misfire onto
today.

::: warning
The clock is not part of a trigger's public surface: there is no `ITrigger.TimeProvider` to read or
assign. It is set when the trigger is constructed, or by the store that materialized it, and nothing
else changes it.
:::

## Time zones are a separate axis

`TimeProvider` says what instant it is. `TimeZoneInfo` says what that instant looks like where the
schedule lives. They are independent: a fake clock does not fake a time zone.

<!-- snippet: sample_time_provider_time_zone -->
```csharp
TriggerBuilder.Create()
    .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(TimeZones.FindById("Europe/Helsinki")))
    .Build();
```
<!-- endSnippet -->

| `TimeZones` member | Does |
|---|---|
| `FindById(string id)` | looks up a zone; use it instead of `TimeZoneInfo.FindSystemTimeZoneById` |
| `GetUtcOffset(DateTime, TimeZoneInfo)` | returns the offset; an ambiguous (repeated) local time resolves to the *daylight* instance, the first of the two |
| `AddResolver(Func<string, TimeZoneInfo?>)` | registers a fallback lookup; returns an `IDisposable` that removes it |

* `FindById` tries, in order: the platform, a built-in alias table (`UTC`, `CET`, `US/Eastern` and
  friends), IANA-to-Windows conversion, then registered resolvers. The platform goes first because
  converting first would rewrite `US/Eastern` to `Eastern Standard Time`, and a job store would write
  the rewritten id back into `TIME_ZONE_ID`.
* Resolvers are consulted most-recently-added first, and are process-wide, because `FindById` is called
  where no scheduler is in scope (parsing a cron expression, deserializing a trigger from a blob).
  `Quartz.Plugins.TimeZoneConverter` installs one and disposes it at scheduler shutdown.

::: warning Changed in 4.x
`TimeZoneUtil` is now `TimeZones`, and `CustomResolver` is `AddResolver`, which returns a registration
you dispose rather than a property you assign.
:::

Daylight saving behaviour differs by trigger family:

* [CronTriggers](crontriggers.md): a cron time that does not exist on a spring-forward day, and one that
  happens twice on a fall-back day
* [More About Triggers](more-about-triggers.md): calendar-interval triggers,
  `PreserveHourOfDayAcrossDaylightSavings` and `SkipDayIfHourDoesNotExist`
* [Testing](testing.md#crossing-a-daylight-saving-transition): how to check *your* schedule, in
  microseconds and with no scheduler

## Testing with a fake clock

`Microsoft.Extensions.TimeProvider.Testing` provides `FakeTimeProvider`:

<!-- Not a compiled sample: `FakeTimeProvider` comes from `Microsoft.Extensions.TimeProvider.Testing`,
     which this repository does not reference outside its test projects. -->

```csharp
FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

builder.Services.AddQuartz(q => q.UseTimeProvider(clock));
```

::: warning
Advancing a fake clock changes what the scheduler *computes*, but it does not *wake* the scheduler.
`clock.Advance(TimeSpan.FromHours(1))` does not make a trigger fire.
:::

* The scheduling loop's idle wait and pre-fire wait are `SemaphoreSlim` waits on the real clock;
  `SemaphoreSlim.WaitAsync` has no `TimeProvider` overload.
* The misfire handler's and cluster manager's scan intervals run on the `TimeProvider`, but only wake
  when their own real delay elapses.
* The fake clock does drive every fire-time computation, misfire detection, `StartDelayed`, and the retry
  and backoff delays in the ADO store.

[Testing](testing.md) turns this into a rule, **advance, then signal**, and covers the four levels of
Quartz test, starting with computing fire times with no scheduler, where a fake clock works fully.
`IdleWaitTime`, misfire thresholds and the other timings are in the
[Configuration Reference](../configuration/reference.md).

## Legacy: the property key

`quartz.timeProvider.type` still works. It names a type with a parameterless constructor:

```text
quartz.timeProvider.type = MyApp.TestClock, MyApp
```

* It is registered with `TryAdd` semantics. The configuration callback runs first, so `UseTimeProvider`
  wins.
* It does replace Quartz's own `TimeProvider.System` fallback, so the key is never read and then
  ignored.
