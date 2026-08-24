---
title: 'Time and TimeProvider'
---

A scheduler is a machine for asking "what time is it?" — thousands of times an hour, from the
scheduling loop, from every trigger's fire-time computation, from the misfire handler and the cluster
check-in. In 4.x there is exactly one place that question is answered: a `TimeProvider`, injected like
any other service.

## SystemTime is gone

3.x had `SystemTime.UtcNow`, a mutable static `Func<DateTimeOffset>` you assigned to. It worked, it was
global, and two tests that both wanted a fake clock could not run at the same time.

`TimeProvider` is the .NET-standard replacement, and Quartz treats it as a *per-scheduler* service.
Nothing is assigned; the clock is injected, and one scheduler's fake clock is not another's.

::: tip
`DateTime.Now`, `DateTime.Today`, `DateTimeOffset.Now`, `DateTimeOffset.Today` and the implicit
`DateTime` → `DateTimeOffset` conversion are banned in the Quartz codebase by an analyzer. `UtcNow` is
not banned — it is the ambient clock's *correct* answer where there is no scheduler to ask. In your own
jobs the same discipline pays off for the same reason: a job that reads `DateTime.Now` cannot be tested
on a fake clock.
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
IScheduler scheduler = await QuartzSchedulerBuilder.Create()
    .UseTimeProvider(myTimeProvider)
    .BuildScheduler();
```
<!-- endSnippet -->

### Precedence

Four sources, most specific first:

1. **`UseTimeProvider(...)` on this scheduler.** Wins over everything.
2. **A `TimeProvider` registered in the container.** A named scheduler with no clock of its own
   inherits the application's.
3. **The legacy `quartz.timeProvider.type` property key.** Code beats strings here as it does
   everywhere else.
4. **`TimeProvider.System`.**

The container-wide default is registered with `TryAddSingleton`, so an application that already does
`services.AddSingleton(TimeProvider.System)` — or registers a test clock — keeps its own registration
and every scheduler picks it up.

`UseTimeProvider` on a **named** scheduler registers the clock keyed by that scheduler's name; on the
**default** scheduler it replaces the container's unkeyed `TimeProvider`. That asymmetry is deliberate:
a container-wide replacement would re-time every other scheduler in the process, and a test that hands
one scheduler a fake clock does not mean the others should start lying too.

## How far the clock reaches

Everything the container builds for a scheduler gets that scheduler's clock:

| Component | What it uses the clock for |
|---|---|
| `QuartzScheduler` / the scheduling loop | deciding whether a trigger is due, `StartDelayed` |
| `RAMJobStore` | fire times, misfire detection |
| `AdoJobStoreBase` and its subclasses | the same, plus retry backoff |
| `IDriverDelegate` (via `DriverDelegateContext.TimeProvider`) | timestamps written to the database |
| `ISemaphore` (via `SemaphoreContext.TimeProvider`) | lock-acquisition backoff |
| `MisfireHandler` | its scan interval |
| `ClusterManager` | check-in interval and failed-node detection |

A custom job store, driver delegate or lock handler joins that list simply by taking a `TimeProvider`
constructor parameter, or by reading the one its context carries — it is resolved for the scheduler the
component belongs to.

## Builders and the clock

Exactly one builder takes a clock:

<!-- A listing of signatures rather than code, so it is written out here rather than compiled. -->

```csharp
TriggerBuilder.Create(TimeProvider? timeProvider = null);
TriggerBuilder.Create<TJob>(TimeProvider? timeProvider = null);
```

Inside the builder it does three things: it is the default `StartTimeUtc` when you do not call
`StartAt`, it is what `StartNow()` reads, and it is handed to the schedule builder at `Build()` time so
that a schedule computed there — `DailyTimeIntervalScheduleBuilder.EndingDailyAfterCount(n)` is the one
that does this — is computed against the same clock.

The five schedule builders take **no** clock:

<!-- snippet: sample_time_provider_schedule_builders -->
```csharp
CronScheduleBuilder.Create(cronExpression);
SimpleScheduleBuilder.Create();
CalendarIntervalScheduleBuilder.Create();
DailyTimeIntervalScheduleBuilder.Create();
RecurrenceScheduleBuilder.Create(recurrenceRule);
```
<!-- endSnippet -->

They describe a *shape* — every day at 09:00, every 15 minutes, the third Tuesday — and a shape needs
no clock. When one of them does need "now", it gets it from the trigger builder.

`DateBuilder` has two statics, both taking an optional clock:

<!-- snippet: sample_time_provider_date_builder -->
```csharp
DateTimeOffset when = DateBuilder.Create(timeProvider).InYear(2027).InMonthOnDay(3, 15).AtHourOfDay(9).Build();
DateTimeOffset local = DateBuilder.CreateInTimeZone(tz, timeProvider).AtHourMinuteAndSecond(9, 30, 0).Build();
```
<!-- endSnippet -->

::: warning Changed in 4.x
`DateBuilder.NewDate` / `NewDateInTimeZone` are now `Create` / `CreateInTimeZone`, and
`CronScheduleBuilder.CronSchedule(...)` is `CronScheduleBuilder.Create(...)` — the whole family follows
the `Create` factory convention now. `DailyTimeIntervalScheduleBuilder.Create()` also **lost its
`TimeProvider` parameter**; it takes the trigger builder's clock instead, which is what makes
`EndingDailyAfterCount` respect a fake clock.
:::

## The trap: triggers built outside the container

The DI configuration path threads the container's clock through for you. Both
`AddTrigger<TJob>` and `ScheduleJob<T>` create their builder as
`TriggerBuilder.Create<TJob>(serviceProvider.GetService<TimeProvider>())`, so a trigger configured
there starts on the scheduler's clock:

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

This is the single most common surprise in a fake-clock test: the scheduler is on 2024-01-01, the
trigger says it starts *now*, and now is whenever the test ran. Pass the clock:

<!-- snippet: sample_time_provider_trigger_builder_clock -->
```csharp
ITrigger trigger = TriggerBuilder.Create(fakeClock)
    .WithIdentity("hourly")
    .StartAt(fakeClock.GetUtcNow())
    .WithSimpleSchedule(s => s.WithInterval(TimeSpan.FromHours(1)).RepeatForever())
    .Build();
```
<!-- endSnippet -->

An explicit `StartAt` sidesteps the question entirely, which is why it is worth being explicit in tests
even when you would not bother in production code.

::: warning
Constructing a trigger implementation directly —
`new SimpleTriggerImpl(timeProvider)`, `new CronTriggerImpl(name, group, expression, timeProvider)` and
their siblings — takes a `TimeProvider?` that defaults to `TimeProvider.System`. The parameter is also
not serialized: a trigger read back out of a job store reads the system clock until the store hands it
one.
:::

## Time zones are a separate axis

`TimeProvider` answers *what instant is it*. `TimeZoneInfo` answers *what does that instant look like
where the schedule lives*. They are independent, and a fake clock does not fake a time zone.

<!-- snippet: sample_time_provider_time_zone -->
```csharp
TriggerBuilder.Create()
    .WithCronSchedule("0 0 9 * * ?", x => x.InTimeZone(TimeZones.FindById("Europe/Helsinki")))
    .Build();
```
<!-- endSnippet -->

`TimeZones` has three members:

- **`FindById(string id)`** — the lookup to use instead of `TimeZoneInfo.FindSystemTimeZoneById`. It
  tries the platform first, then a built-in alias table (`UTC`, `CET`, `US/Eastern` and friends), then
  IANA-to-Windows conversion, then any registered resolver. The platform lookup goes first on purpose:
  converting an id up front would rewrite `US/Eastern` into `Eastern Standard Time`, and it is the
  rewritten id a job store would write back into `TIME_ZONE_ID`.
- **`GetUtcOffset(DateTime, TimeZoneInfo)`** — the offset, resolving an ambiguous (repeated) local time
  to the *daylight* instance, because that is the first of the two.
- **`AddResolver(Func<string, TimeZoneInfo?>)`** — registers a fallback lookup and returns an
  `IDisposable` that removes it. Resolvers are consulted most-recently-added first, and this is
  process-wide: `FindById` is reached from places with no scheduler in scope, such as parsing a cron
  expression or deserializing a trigger out of a blob. `Quartz.Plugins.TimeZoneConverter` installs one
  and disposes it at scheduler shutdown.

::: warning Changed in 4.x
`TimeZoneUtil` is now `TimeZones`, and `CustomResolver` is `AddResolver`, which returns a registration
you dispose rather than a property you assign.
:::

Daylight saving is where the two axes meet, and each trigger family answers it differently:

- [CronTriggers](crontriggers.md) — a cron time that does not exist on a spring-forward day, and one
  that happens twice on a fall-back day
- [More About Triggers](more-about-triggers.md) — calendar-interval triggers,
  `PreserveHourOfDayAcrossDaylightSavings` and `SkipDayIfHourDoesNotExist`

## Testing with a fake clock

`Microsoft.Extensions.TimeProvider.Testing` gives you `FakeTimeProvider`:

<!-- Not a compiled sample: `FakeTimeProvider` comes from `Microsoft.Extensions.TimeProvider.Testing`,
     which this repository does not reference outside its test projects. -->

```csharp
FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero));

builder.Services.AddQuartz(q => q.UseTimeProvider(clock));
```

**Read this before you rely on it:** advancing a fake clock changes what the scheduler *computes*, but
it does not *wake* the scheduler. The scheduling loop's idle wait and its pre-fire wait are
`SemaphoreSlim` waits on the real clock — `SemaphoreSlim.WaitAsync` has no `TimeProvider` overload — so
`clock.Advance(TimeSpan.FromHours(1))` does not make a trigger fire. The same is true of the misfire
handler's and the cluster manager's scan intervals, which do run on the `TimeProvider` but only wake
when their own real delay elapses.

What the fake clock *does* drive: every fire-time computation, misfire detection, `StartDelayed`, and
the retry and backoff delays in the ADO store.

The [Testing](testing.md) page turns that into a rule — **advance, then signal** — and leads with the
level where a fake clock is completely effective: computing fire times with no scheduler at all.

## Legacy: the property key

`quartz.timeProvider.type` still works, and names a type with a parameterless constructor:

```text
quartz.timeProvider.type = MyApp.TestClock, MyApp
```

It is registered with `TryAdd` semantics, which is exactly what makes `UseTimeProvider` win: the
configuration callback runs first. It does forcibly displace Quartz's own `TimeProvider.System`
fallback, though — a key that was read and then quietly ignored is the one outcome no configuration key
is allowed to have.

## See also

- [Testing](testing.md) — the fake-clock rules, and the three levels of Quartz test
- [Configuration Reference](../configuration/reference.md) — `IdleWaitTime`, misfire thresholds and the rest
- [CronTriggers](crontriggers.md) — time zones and DST in cron schedules
