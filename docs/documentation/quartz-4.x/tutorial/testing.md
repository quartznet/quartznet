---
title: 'Testing'
---

<!-- The blocks on this page without a `snippet:` marker are hand-written on purpose. Compiling a test
     means a test framework, an assertion library, `Microsoft.Extensions.TimeProvider.Testing` and
     `Microsoft.AspNetCore.Mvc.Testing` as dependencies of the documentation-samples project, and a
     NuGet dependency taken purely for a documentation sample is not worth it. Everything here that
     compiles against Quartz alone is a snippet. -->

Scheduling code is unusually easy to test badly. A test that starts a scheduler, sleeps two seconds and
asserts that a counter moved is a test that passes on your laptop and fails in CI, and the fix people
reach for — a longer sleep — makes the suite slower without making it correct.

There are four levels of Quartz test, in increasing cost. Most of what you want to know can be answered
at the first two, which involve no scheduler at all or no clock at all.

| Level | What it exercises | Cost |
|---|---|---|
| **0** | schedule arithmetic — when *would* this fire? | microseconds, fully deterministic |
| **1** | one job's `Execute`, against a context you build | microseconds, fully deterministic |
| **2** | a real in-memory scheduler | milliseconds, needs a completion signal |
| **3** | the whole host, or a real database | seconds |

## Level 0: schedules, with no scheduler

A trigger is a pure function from a start time to a sequence of fire times, and you can call it
directly. This is where a fake clock is *completely* effective, and it is where most schedule bugs
actually live.

```csharp
[Test]
public void CronScheduleSkipsWeekends()
{
    FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 6, 0, 0, 0, TimeSpan.Zero)); // a Friday

    ITrigger trigger = TriggerBuilder.Create(clock)
        .WithIdentity("weekdays")
        .StartAt(clock.GetUtcNow())
        .WithCronSchedule("0 0 9 ? * MON-FRI", x => x.InTimeZone(TimeZoneInfo.Utc))
        .Build();

    List<DateTimeOffset> fires = TriggerFireTimes.Compute(trigger, calendar: null, numberOfTimes: 3);

    fires[0].Should().Be(new DateTimeOffset(2026, 3, 6, 9, 0, 0, TimeSpan.Zero));
    fires[1].Should().Be(new DateTimeOffset(2026, 3, 9, 9, 0, 0, TimeSpan.Zero));  // Monday
    fires[2].Should().Be(new DateTimeOffset(2026, 3, 10, 9, 0, 0, TimeSpan.Zero));
}
```

`TriggerFireTimes` lives in `Quartz.Extensibility` and has three members:

| Member | Answers |
|---|---|
| `Compute(trigger, calendar, numberOfTimes)` | the next *n* fire times |
| `ComputeBetween(trigger, calendar, from, to)` | every fire time in a window |
| `ComputeEndTimeForCount(trigger, calendar, numberOfTimes)` | the `EndAt` that would allow exactly *n* firings |

Each has an `ITrigger` overload and an `IOperableTrigger` one. Pass what you are holding: the
`ITrigger` form does the cast for you, and answers with an `ArgumentException` naming the type if the
trigger is one of your own that cannot be advanced. They clone the trigger before computing and prime
it themselves, so you do not have to call `ComputeFirstFireTimeUtc` first, and the trigger you passed
in is untouched.

For a single step, `ITrigger.GetFireTimeAfter(DateTimeOffset?)` answers "and then?" directly — it
computes from the schedule rather than from stored state, so it works on a trigger that has never been
scheduled.

::: warning
`TriggerBuilder.Create()` with no argument defaults its start time to the **wall clock**, even inside a
test holding a `FakeTimeProvider`. Pass the clock — `TriggerBuilder.Create(clock)` — and set `StartAt`
explicitly. See [Time and TimeProvider](time-and-timeprovider.md#the-trap-triggers-built-outside-the-container).

Passing the clock is enough: the built trigger keeps it, so every "now" it reads afterwards — a cron
trigger's past-due clamp in `ComputeFirstFireTimeUtc`, and the whole of `UpdateAfterMisfire` — is the
clock you passed rather than the machine's.
:::

Calendars are testable the same way: `ICalendar.IsTimeIncluded(when)` needs nothing but the calendar.

### Crossing a daylight-saving transition

The question a DST test asks is "what does my schedule do on the two days a year the local clock is not
monotonic", and level 0 answers it exactly: put the fake clock a day before a transition, put the trigger
in a real time zone, and ask for the window. No scheduler, no waiting, and the assertion is the answer
rather than a proxy for it.

```csharp
[Test]
public void DailyCronKeepsItsLocalTimeAcrossSpringForward()
{
    // Europe/Helsinki springs forward at 03:00 local on 2026-03-29
    TimeZoneInfo helsinki = TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki");
    FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero));

    ITrigger trigger = TriggerBuilder.Create(clock)
        .WithIdentity("nightly")
        .StartAt(clock.GetUtcNow())
        .WithCronSchedule("0 30 2 * * ?", x => x.InTimeZone(helsinki))
        .Build();

    List<DateTimeOffset> fires = TriggerFireTimes.ComputeBetween(
        trigger,
        calendar: null,
        from: new DateTimeOffset(2026, 3, 27, 0, 0, 0, TimeSpan.Zero),
        to: new DateTimeOffset(2026, 3, 31, 0, 0, 0, TimeSpan.Zero));

    // 02:30 local every day: +02:00 before the transition, +03:00 after it
    fires.Select(fire => TimeZoneInfo.ConvertTime(fire, helsinki).TimeOfDay)
        .Should().AllBeEquivalentTo(new TimeSpan(2, 30, 0),
            "a cron trigger keeps its local wall-clock time, so the UTC instant moves instead");
}
```

Three more cases are worth a test of their own, and all three are the same shape:

- **A time that does not exist**, in the hour spring-forward skips — `0 30 3 * * ?` in the zone above.
  Assert on the instant the trigger actually picks rather than assuming one: on 2026-03-29 that schedule
  fires at 04:00 local, the moment the clock jumps to, and not at 03:30 of either offset.
- **A time that happens twice**, in the hour fall-back repeats. Assert on the *count* in the window: one
  firing or two is the whole question.
- **An interval schedule across the same boundary.** Interval triggers count *elapsed time* by default,
  so a 24-hour `SimpleTrigger` that fired at 02:30 fires at 03:30 local afterwards — and so does a
  one-day `CalendarIntervalTrigger`, unless it is asked otherwise.
  `PreserveHourOfDayAcrossDaylightSavings()` is the ask, and it is the one that keeps 02:30 across the
  transition. Which of the two your schedule wants is a decision, and this is the test that shows you
  made it.

`TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki")` resolves IANA ids on Windows too since .NET 6;
[Quartz.Plugins.TimeZoneConverter](../packages/timezoneconverter-integration.md) is for the cases it
still cannot.

## Level 1: one job, one context

A job's `Execute` takes an `IJobExecutionContext`. Build one and call it:

```csharp
[Test]
public async Task ImportJobWritesTheWatermark()
{
    IJobDetail detail = JobBuilder.Create<ImportJob>()
        .WithIdentity("import", "sync")
        .UsingJobData("source", "orders")
        .Build();

    IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
        .WithIdentity("import-trigger", "sync")
        .ForJob(detail)
        .Build();

    TriggerFiredBundle bundle = new()
    {
        JobDetail = detail,
        Trigger = trigger,
        Recovering = false,
        FireTimeUtc = new DateTimeOffset(2026, 3, 6, 9, 0, 0, TimeSpan.Zero),
        ScheduledFireTimeUtc = new DateTimeOffset(2026, 3, 6, 9, 0, 0, TimeSpan.Zero),
        PreviousFireTimeUtc = null,
        NextFireTimeUtc = null,
    };

    ImportJob job = new(importer);
    using JobExecutionContextImpl context = new(scheduler: null!, bundle, job);

    await job.Execute(context, CancellationToken.None);

    importer.LastSource.Should().Be("orders");
}
```

`TriggerFiredBundle` is a required-init record, which is what makes this level teachable — the compiler
lists what a firing consists of. Seven members are required: `JobDetail`, `Trigger`, `Recovering`,
`FireTimeUtc`, `ScheduledFireTimeUtc`, `PreviousFireTimeUtc` and `NextFireTimeUtc`. Three of those are
nullable but still required, so you have to say `null` rather than forget them. `Calendar` is optional.

`JobExecutionContextImpl` does no null-checking in its constructor — it copies fields. Passing `null`
for the scheduler and the job is fine as long as the code under test does not reach for them; reach for
`context.Scheduler` with a null scheduler and you get the `NullReferenceException` you asked for. Fake
the scheduler when the job uses it.

::: warning
`context.JobRunTime` while a job is still running is computed from `DateTimeOffset.UtcNow`, not from the
scheduler's `TimeProvider`. Under a fake clock set to a different instant the mid-execution value is
meaningless and can be negative. The value the scheduler records *after* the job completes is measured
from a monotonic timestamp and is always sane.
:::

### Keeping jobs testable

Three habits make level 1 the level you spend most of your time at:

- **Inject dependencies through the constructor.** The container builds jobs; a job that news up its
  own `HttpClient` cannot be tested without a network.
- **Read inputs from `MergedJobDataMap`**, or let the job factory set properties for you. Either way
  the inputs are data you can supply.
- **Forward the cancellation token.** `Execute(context, cancellationToken)` receives it as a parameter
  precisely so that `CA2016` flags a job that drops it — and a job that drops it cannot be tested for
  cancellation.

### Jobs the container builds

When a job takes constructor dependencies, resolve it the way the scheduler will:

```csharp
ServiceCollection services = new();
services.AddSingleton<IImporter, FakeImporter>();
services.AddTransient<ImportJob>();
ServiceProvider provider = services.BuildServiceProvider();

MicrosoftDependencyInjectionJobFactory factory = new(provider);
JobScope scope = await factory.CreateJob(bundle, scheduler);
try
{
    await scope.Job.Execute(context, CancellationToken.None);
}
finally
{
    await factory.ReturnJob(scope);
}
```

That exercises the whole instantiation path — the scope, the property injection, and any
`ConfigureJobScope` hook. It is also the level at which to test a per-firing `AsyncLocal`: the hook is
deliberately synchronous so that values it sets flow into `Execute`, and this is the test that proves
they do.

::: warning Changed in 4.x
The scheduler context is **no longer** merged into the properties the job factory sets, and no longer
merged into `context.MergedJobDataMap`. A job that read a scheduler-wide value from either now reads it
from `context.Scheduler.Context`.
:::

## Level 2: a real in-memory scheduler

When the thing under test is the *wiring* — that a trigger reaches a job, that a listener vetoes, that
`[DisallowConcurrentExecution]` does what it says — run a real scheduler in memory:

<!-- snippet: sample_testing_in_memory_scheduler -->
```csharp
await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q => q
        .UseInMemoryStore()
        .ConfigureScheduler(o => o.InstanceName = $"test-{Guid.NewGuid():N}"))
    .Build();

IScheduler scheduler = await factory.GetScheduler();
await scheduler.Start();
```
<!-- endSnippet -->

`BuildScheduler()` is the shortcut when you do not need the factory, but hold the factory in a test:
disposing it is what shuts the scheduler down and releases its container.

### Signal completion; never sleep

The one rule that makes level 2 reliable: **the job tells the test when it is done.** A
`TaskCompletionSource` on a listener is the tidiest form, and because `IJobListener` has a default
implementation for every member you only write the one you need:

<!-- snippet: sample_testing_completion_listener -->
```csharp
internal sealed class CompletionListener : IJobListener
{
    private readonly TaskCompletionSource<JobExecutionException?> completed =
        new(TaskCreationOptions.RunContinuationsAsynchronously);

    public Task<JobExecutionException?> Completed => completed.Task;

    public ValueTask JobWasExecuted(
        IJobExecutionContext context,
        JobExecutionException? jobException,
        CancellationToken cancellationToken = default)
    {
        completed.TrySetResult(jobException);
        return default;
    }
}
```
<!-- endSnippet -->

```csharp
CompletionListener listener = new();
scheduler.ListenerManager.AddJobListener(listener);

await scheduler.ScheduleJob(detail, trigger);

JobExecutionException? failure = await listener.Completed.WaitAsync(TimeSpan.FromSeconds(30));
failure.Should().BeNull();
```

Two details worth copying:

- **`RunContinuationsAsynchronously`.** Without it the continuation runs on the scheduler's own thread,
  inside the notification, and a test that then blocks deadlocks the scheduler.
- **A generous deadline, never a timing assertion.** Thirty seconds is not "the job takes thirty
  seconds"; it is "if we are still waiting after thirty seconds, something is broken". The deadline
  decides when it is safe to give up, not when it is correct to look.

Registering a listener with no matchers means every job. `IJobListener.Name` defaults to the
implementing type's name, which is fine until you register two instances of the same listener type with
one scheduler — the second replaces the first. Override `Name` when you do that.

The same shape works for `ITriggerListener` (whose `VetoJobExecution` defaults to vetoing nothing) and
`ISchedulerListener`.

::: warning Changed in 4.x
`JobListenerSupport`, `TriggerListenerSupport` and `SchedulerListenerSupport` are gone. The interfaces
carry default implementations now, so implement the interface directly — and note the namespace for the
shipped listeners is `Quartz.Listeners`, plural.
:::

### Asserting on the outcome

```csharp
// what state did the trigger end in?
PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggersInError();
triggers.Items.Should().BeEmpty();

// what is running right now?
PagedResult<FireInstance> running = await scheduler.QueryFireInstances();

// what did the job produce?
context.Result.Should().Be(42);
```

Remember that a query pages: `Take` defaults to 250, so an assertion on a large result set needs
`Take = PagedQuery.All` or a loop. See
[Querying Jobs and Triggers](querying-jobs-and-triggers.md#paging).

::: warning Changed in 4.x
`GetCurrentlyExecutingJobs()` is gone; `QueryFireInstances()` is the replacement, and it lists firings
across the cluster rather than only on the node that answered.
:::

### Controlling time

Give the scheduler a `FakeTimeProvider` and every *computation* moves when you advance it:

```csharp
FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 6, 8, 0, 0, TimeSpan.Zero));

await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q => q
        .UseInMemoryStore()
        .UseTimeProvider(clock))
    .Build();
```

**Advancing the clock does not wake the scheduler.** The scheduling loop reads the `TimeProvider` for
every decision, but it *waits* on a `SemaphoreSlim`, which only knows about real elapsed time. So:

> **Advance, then signal.** Move the fake clock, then do something that signals a scheduling change —
> scheduling, rescheduling, pausing or resuming anything releases the loop's semaphore and it
> re-evaluates immediately against the new "now".

```csharp
clock.Advance(TimeSpan.FromHours(2));
await scheduler.ScheduleJob(detail, trigger);   // this both schedules and wakes the loop
```

Where there is nothing natural to signal, shorten the wait instead:

<!-- snippet: sample_testing_idle_wait_time -->
```csharp
.ConfigureScheduler(o => o.IdleWaitTime = TimeSpan.FromSeconds(1))
```
<!-- endSnippet -->

One second is the minimum the option validator accepts; the default is thirty. The misfire handler and
the cluster manager have the same property — they compute on the `TimeProvider` but wake on their own
real delay — so misfire recovery and cluster check-in are equally undrivable by `Advance` alone.

**Never write `Advance(1h)` and assert "therefore it fired".** That test passes or fails on wall-clock
timing, which is the thing the fake clock was supposed to remove.

### Testing misfire behaviour

Misfire is a comparison between a trigger's scheduled time and now, so a fake clock plus a small
threshold makes it reachable:

<!-- snippet: sample_testing_misfire_threshold -->
```csharp
QuartzSchedulerBuilder.Create(q => q
    .UseInMemoryStore(o => o.MisfireThreshold = TimeSpan.FromMilliseconds(50))
    .UseTimeProvider(clock))
```
<!-- endSnippet -->

The in-memory store's threshold defaults to five seconds, the ADO store's to one minute, and both must
be at least one millisecond. Put the scheduler in standby, move the clock past the fire time, then start
it — the trigger is late by construction, with no sleeping involved.

### Fault injection

`DelegatingJobStore` is public, non-sealed and virtual throughout, for exactly this:

<!-- snippet: sample_testing_flaky_job_store -->
```csharp
internal sealed class FlakyJobStore(IJobStore inner) : DelegatingJobStore(inner)
{
    public int AcquireCalls { get; private set; }

    public override ValueTask<List<IOperableTrigger>> AcquireNextTriggers(
        TriggerAcquisitionRequest request,
        CancellationToken cancellationToken = default)
    {
        AcquireCalls++;
        if (AcquireCalls == 1)
        {
            throw new JobPersistenceException("simulated outage");
        }

        return base.AcquireNextTriggers(request, cancellationToken);
    }
}
```
<!-- endSnippet -->

<!-- snippet: sample_testing_fault_injection_registration -->
```csharp
QuartzSchedulerBuilder.Create(q => q
    .UseJobStore(sp => new FlakyJobStore(ActivatorUtilities.CreateInstance<RAMJobStore>(sp))))
```
<!-- endSnippet -->

Counting, stalling and failing store calls is how you test retry and backoff behaviour without a
database. `DelegatingScheduler` is the same idea one layer up.

## Level 3: the host, and a real database

### Under a host

`AddQuartz` plus `AddQuartzHostedService` inside `WebApplicationFactory<TProgram>` exercises the real
startup path — configuration binding, hosted-service ordering, the lot:

```csharp
await using WebApplicationFactory<Program> app = new();
IScheduler scheduler = app.Services.GetRequiredService<IScheduler>();
```

Set `WaitForJobsToComplete = true` on `QuartzHostedServiceOptions` in tests so that teardown does not
race a running job. `AwaitApplicationStarted` and `StartDelay` are the other two knobs, and both change
*when* jobs first become eligible — worth setting explicitly rather than inheriting.

### Against a persistent store, without Docker

Most of what you want from a persistent store in a test is *persistence*: that a job survives a restart,
that job data round-trips through the serializer, that your trigger's persistence delegate writes what it
reads. None of that needs a server. A **file** SQLite database plus `ProvisionSchema()` gives you a real
ADO job store in milliseconds, with no container to start:

```csharp
string databasePath = Path.Combine(Path.GetTempPath(), $"quartz-{Guid.NewGuid():N}.db");

await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q =>
    {
        q.ConfigureScheduler(options => options.InstanceName = $"test-{Guid.NewGuid():N}");
        q.UsePersistentStore(store =>
        {
            store.UseSqlite($"Data Source={databasePath}");

            // creates the twelve tables in the empty file; see Creating the schema
            store.ProvisionSchema();
        });
    })
    .Build();
```

A file, not `:memory:` — an in-memory SQLite database lives as long as its connection, and the store
opens and closes one per operation, so the tables vanish between them. Delete the file in teardown.

What this level cannot tell you is anything dialect-specific: the SQL a `SqlServerDelegate` emits, how
Postgres locks a row, whether an index is used. It also
[cannot be clustered](job-stores.md#configuring-a-persistent-store) — SQLite locks in process rather than in the
database, and `UseClustering()` with it is refused at startup. For those, go one level further.

### Against a real database

A test whose subject is the SQL — a driver delegate, a lock handler, a migration, an index — needs the
engine it is written for. Provision one per fixture with Testcontainers, and **create the schema from the
shipped DDL** — `database/tables/tables_<dialect>.sql` — rather than from a hand-maintained copy.
Applying it with the engine's own client is what handles the dialect's batch separator (`GO`, `/`,
`SET TERM`); a plain `ExecuteNonQuery` over the whole file will not.

A container per fixture, not per test: starting SQL Server takes longer than every test in the class.

## Isolation rules

Four things keep tests from contaminating each other:

- **A unique instance name per test.** The scheduler repository indexes by name, and binding a second
  scheduler with the same name *and* the same instance id throws. `$"test-{Guid.NewGuid():N}"` is
  enough.
- **One container per test.** `QuartzSchedulerBuilder.Build()` creates its own service provider and
  therefore its own scheduler repository — two builders never see each other's schedulers. That is what
  makes parallel tests safe, and it is also why a test cannot look up another test's scheduler.
- **`await using` the factory.** Disposing `StandaloneSchedulerFactory` shuts the scheduler down and
  disposes its container. A leaked scheduler keeps a scheduling loop running for the rest of the run.
- **`Shutdown(waitForJobsToComplete: true)`** when a job may still be in flight and you need it
  finished before assertions or cleanup.

A persistent store adds two more, because the state now outlives the process that wrote it:

- **`SCHED_NAME` is the partition.** Every Quartz table carries it, and every statement the store issues
  filters on it, so a unique `InstanceName` per test isolates tests *inside one database* — which is what
  makes a container per fixture affordable. It is not a substitute for the unique name above; it is the
  same setting doing a second job.
- **Give each test its own database file, or clean up after it.** A shared file plus unique names works
  and leaves the file growing; a file per test is simpler and costs nothing at SQLite's price. Where a
  fixture is shared, `IScheduler.Clear()` is the cheap reset: for that scheduler name it deletes the
  jobs, the triggers of every type, the calendars, the paused job and trigger groups, and the
  fired-trigger rows. What it leaves behind is the node's own `QRTZ_SCHEDULER_STATE` check-in row, which
  is why a test asserting on `QueryClusterNodes()` wants a name nothing else has used.

## Anti-patterns

- **Sleeping for a fire.** `await Task.Delay(2000)` is a coin flip on a loaded CI agent. Signal.
- **Sharing one scheduler across tests.** State from one test — a paused group, a stored job, a
  listener — leaks into the next, and the failure surfaces in whichever test happens to run second.
- **Asserting on wall-clock times.** `firedAt.Should().BeCloseTo(expected, 100.Milliseconds())` is a
  flake waiting for a slow day. Assert on the fire times the trigger *computes* (level 0), and on
  ordering and counts everywhere else.
- **Testing Quartz.** That a `SimpleTrigger` repeats, or that pausing a group stops it firing, is
  tested here. Test your schedule and your job.

## See also

- [Time and TimeProvider](time-and-timeprovider.md) — the clock seam, and where a fake clock reaches
- [Building a Scheduler Without a Host](standalone-scheduler.md) — the builder these tests use
- [Querying Jobs and Triggers](querying-jobs-and-triggers.md) — the assertions available after a run
