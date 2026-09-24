---
title: 'Testing'
---

<!-- The blocks on this page without a `snippet:` marker are hand-written on purpose. Compiling a test
     means a test framework, an assertion library, `Microsoft.Extensions.TimeProvider.Testing` and
     `Microsoft.AspNetCore.Mvc.Testing` as dependencies of the documentation-samples project, and a
     NuGet dependency taken purely for a documentation sample is not worth it. Everything here that
     compiles against Quartz alone is a snippet. -->

Test Quartz code at the cheapest level that answers the question. A test that starts a scheduler, sleeps
two seconds and checks a counter passes locally and fails in CI; a longer sleep only makes it slower.
Most questions are answered at levels 0 and 1, which need no scheduler or no clock.

| Level | What it exercises | Cost |
|---|---|---|
| **0** | schedule arithmetic — when *would* this fire? | microseconds, fully deterministic |
| **1** | one job's `Execute`, against a context you build | microseconds, fully deterministic |
| **2** | a real in-memory scheduler | milliseconds, needs a completion signal |
| **3** | the whole host, or a real database | seconds |

## Level 0: schedules, with no scheduler

A trigger is a pure function from a start time to a sequence of fire times; call it directly. A fake
clock works fully here, and most schedule bugs are found here.

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

* Each has an `ITrigger` overload and an `IOperableTrigger` one. The `ITrigger` form casts for you, and
  throws an `ArgumentException` naming the type for a trigger of your own that cannot be advanced.
* They clone the trigger and prime it themselves: you need not call `ComputeFirstFireTimeUtc` first, and
  the trigger you passed is untouched.
* For a single step, `ITrigger.GetFireTimeAfter(DateTimeOffset?)` computes from the schedule, not from
  stored state, so it works on a trigger that was never scheduled.

::: warning
`TriggerBuilder.Create()` with no argument defaults its start time to the **wall clock**, even inside a
test holding a `FakeTimeProvider`. Pass the clock — `TriggerBuilder.Create(clock)` — and set `StartAt`
explicitly. See [Time and TimeProvider](time-and-timeprovider.md#the-trap-triggers-built-outside-the-container).

The built trigger keeps the clock, so every later "now" it reads (a cron trigger's past-due clamp in
`ComputeFirstFireTimeUtc`, and all of `UpdateAfterMisfire`) comes from it, not from the machine.
:::

Calendars are testable the same way: `ICalendar.IsTimeIncluded(when)` needs nothing but the calendar.

### Crossing a daylight-saving transition

To test what a schedule does on the two days a year the local clock is not monotonic: set the fake clock
a day before a transition, put the trigger in a real time zone, and compute the window. No scheduler and
no waiting.

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

Test these three cases the same way:

* **A time that does not exist**, in the hour spring-forward skips: `0 30 3 * * ?` in the zone above.
  Assert on the instant the trigger picks. On 2026-03-29 that schedule fires at 04:00 local, when the
  clock jumps, not at 03:30 of either offset.
* **A time that happens twice**, in the hour fall-back repeats. Assert on the *count* of firings in the
  window: one or two.
* **An interval schedule across the boundary.** Interval triggers count *elapsed time* by default, so a
  24-hour `SimpleTrigger` that fired at 02:30 fires at 03:30 local afterwards. So does a one-day
  `CalendarIntervalTrigger`, unless you call `PreserveHourOfDayAcrossDaylightSavings()`, which keeps
  02:30. The test shows which of the two your schedule does.

`TimeZoneInfo.FindSystemTimeZoneById("Europe/Helsinki")` resolves IANA ids on Windows too since .NET 6.
Use [Quartz.Plugins.TimeZoneConverter](../packages/timezoneconverter-integration.md) where it still
cannot.

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

* `TriggerFiredBundle` is a required-init record, so the compiler lists what a firing needs. Required:
  `JobDetail`, `Trigger`, `Recovering`, `FireTimeUtc`, `ScheduledFireTimeUtc`, `PreviousFireTimeUtc` and
  `NextFireTimeUtc`. Three are nullable but still required, so write `null` explicitly. `Calendar` is
  optional.
* `JobExecutionContextImpl` does no null-checking in its constructor. `null` for the scheduler and the
  job is fine if the code under test does not use them; `context.Scheduler` with a null scheduler throws
  `NullReferenceException`. Fake the scheduler when the job uses it.

::: warning
`context.JobRunTime` while a job is running is computed from `DateTimeOffset.UtcNow`, not from the
scheduler's `TimeProvider`. Under a fake clock set to another instant the mid-execution value is
meaningless and can be negative. The value recorded *after* the job completes uses a monotonic timestamp
and is always correct.
:::

### Keeping jobs testable

* **Inject dependencies through the constructor.** A job that creates its own `HttpClient` cannot be
  tested without a network.
* **Read inputs from `MergedJobDataMap`**, or let the job factory set properties. Either way the test
  supplies them as data.
* **Forward the cancellation token.** It is a parameter of `Execute(context, cancellationToken)` so that
  `CA2016` flags a job that drops it; such a job cannot be tested for cancellation.

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

This exercises the whole instantiation path: the scope, property injection, and any
`ConfigureJobScope` hook. Test a per-firing `AsyncLocal` here: the hook is synchronous so that values it
sets flow into `Execute`, and this test proves they do.

::: warning Changed in 4.x
The scheduler context is **no longer** merged into the properties the job factory sets, and no longer
merged into `context.MergedJobDataMap`. A job that read a scheduler-wide value from either now reads it
from `context.Scheduler.Context`.
:::

## Level 2: a real in-memory scheduler

To test the *wiring* (a trigger reaches a job, a listener vetoes, `[DisallowConcurrentExecution]`
works), run a real scheduler in memory:

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

In a test, hold the factory rather than using `BuildScheduler()`: disposing it shuts the scheduler down
and releases its container.

### Signal completion; never sleep

**The job tells the test when it is done.** Use a `TaskCompletionSource` on a listener. `IJobListener`
has a default implementation for every member, so write only the one you need:

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

* **`RunContinuationsAsynchronously`.** Without it the continuation runs on the scheduler's thread,
  inside the notification, and a test that then blocks deadlocks the scheduler.
* **A generous deadline, never a timing assertion.** Thirty seconds means "if still waiting, something
  is broken", not "the job takes thirty seconds".
* A listener with no matchers hears every job.
* `IJobListener.Name` defaults to the type's name. A second instance of the same type registered with
  one scheduler replaces the first; override `Name` to register both.
* The same shape works for `ITriggerListener` (whose `VetoJobExecution` defaults to vetoing nothing) and
  `ISchedulerListener`.

::: warning Changed in 4.x
`JobListenerSupport`, `TriggerListenerSupport` and `SchedulerListenerSupport` are gone. The interfaces
have default implementations, so implement the interface directly. The shipped listeners' namespace is
`Quartz.Listeners`, plural.
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

Queries page: `Take` defaults to 250, so an assertion on a large result set needs
`Take = PagedQuery.All` or a loop. See
[Querying Jobs and Triggers](querying-jobs-and-triggers.md#paging).

::: warning Changed in 4.x
`GetCurrentlyExecutingJobs()` is gone. `QueryFireInstances()` replaces it and lists firings across the
cluster, not only on the node that answered.
:::

### Controlling time

With a `FakeTimeProvider`, every scheduler *computation* moves when you advance it:

```csharp
FakeTimeProvider clock = new(new DateTimeOffset(2026, 3, 6, 8, 0, 0, TimeSpan.Zero));

await using StandaloneSchedulerFactory factory = QuartzSchedulerBuilder
    .Create(q => q
        .UseInMemoryStore()
        .UseTimeProvider(clock))
    .Build();
```

**Advancing the clock does not wake the scheduler.** The scheduling loop reads the `TimeProvider` for
every decision, but *waits* on a `SemaphoreSlim`, which only knows real elapsed time.

**Advance, then signal.** Move the fake clock, then signal a scheduling change. Scheduling,
rescheduling, pausing or resuming anything releases the loop's semaphore, and it re-evaluates against
the new "now":

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

* `IdleWaitTime` is at least one second (the option validator's minimum); the default is thirty.
* The misfire handler and the cluster manager also compute on the `TimeProvider` but wake on their own
  real delay, so `Advance` alone cannot drive misfire recovery or cluster check-in either.
* **Never write `Advance(1h)` and assert "therefore it fired".** That test depends on wall-clock timing,
  which the fake clock was meant to remove.

### Testing misfire behaviour

A misfire compares a trigger's scheduled time with now, so a fake clock and a small threshold produce
one:

<!-- snippet: sample_testing_misfire_threshold -->
```csharp
QuartzSchedulerBuilder.Create(q => q
    .UseInMemoryStore(o => o.MisfireThreshold = TimeSpan.FromMilliseconds(50))
    .UseTimeProvider(clock))
```
<!-- endSnippet -->

| Store | `MisfireThreshold` default | Minimum |
|---|---|---|
| in-memory | five seconds | one millisecond |
| ADO | one minute | one millisecond |

Put the scheduler in standby, move the clock past the fire time, then start it: the trigger is late,
with no sleeping.

### Fault injection

`DelegatingJobStore` is public, non-sealed and virtual throughout, for this purpose:

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

Count, stall and fail store calls to test retry and backoff without a database. `DelegatingScheduler`
does the same one layer up.

## Level 3: the host, and a real database

### Under a host

`AddQuartz` plus `AddQuartzHostedService` inside `WebApplicationFactory<TProgram>` exercises the real
startup path, including configuration binding and hosted-service ordering:

```csharp
await using WebApplicationFactory<Program> app = new();
IScheduler scheduler = app.Services.GetRequiredService<IScheduler>();
```

* Set `WaitForJobsToComplete = true` on `QuartzHostedServiceOptions`, so teardown does not race a
  running job.
* `AwaitApplicationStarted` and `StartDelay` change *when* jobs first become eligible; set them
  explicitly in tests.

### Against a persistent store, without Docker

Testing *persistence* (a job survives a restart, job data round-trips through the serializer, your
trigger's persistence delegate writes what it reads) needs no server. A **file** SQLite database plus
`ProvisionSchema()` gives a real ADO job store in milliseconds, with no container:

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

* Use a file, not `:memory:`. An in-memory SQLite database lives as long as its connection, and the
  store opens one per operation, so the tables vanish between operations. Delete the file in teardown.
* It tells you nothing dialect-specific: the SQL a `SqlServerDelegate` emits, how Postgres locks a row,
  whether an index is used.
* It [cannot be clustered](job-stores.md#configuring-a-persistent-store): SQLite locks in process, and
  `UseClustering()` with it is refused at startup.

For those, use a real database.

### Against a real database

A test of SQL (a driver delegate, a lock handler, a migration, an index) needs the engine it is written
for.

* Provision one per fixture with Testcontainers, not per test: starting SQL Server takes longer than
  every test in the class.
* **Create the schema from the shipped DDL**, `database/tables/tables_<dialect>.sql`, not from a
  hand-maintained copy.
* Apply it with the engine's own client, which handles the dialect's batch separator (`GO`, `/`,
  `SET TERM`); a plain `ExecuteNonQuery` over the whole file does not.

## Isolation rules

* **A unique instance name per test.** The scheduler repository indexes by name, and binding a second
  scheduler with the same name *and* instance id throws. `$"test-{Guid.NewGuid():N}"` is enough.
* **One container per test.** `QuartzSchedulerBuilder.Build()` creates its own service provider and
  scheduler repository, so two builders never see each other's schedulers. Parallel tests are safe, and
  a test cannot look up another test's scheduler.
* **`await using` the factory.** Disposing `StandaloneSchedulerFactory` shuts the scheduler down and
  disposes its container. A leaked scheduler keeps a scheduling loop running for the rest of the run.
* **`Shutdown(waitForJobsToComplete: true)`** when a job may still be running and must finish before
  assertions or cleanup.

With a persistent store, state outlives the process, so also:

* **`SCHED_NAME` is the partition.** Every Quartz table has it and every store statement filters on it,
  so the unique `InstanceName` per test also isolates tests *inside one database*. That makes a
  container per fixture affordable.
* **Give each test its own database file, or clean up.** A shared file with unique names works but
  grows; a file per test is simpler and cheap with SQLite. For a shared fixture, `IScheduler.Clear()`
  resets that scheduler name: it deletes the jobs, the triggers of every type, the calendars, the paused
  job and trigger groups, and the fired-trigger rows. It leaves the node's own `QRTZ_SCHEDULER_STATE`
  check-in row, so a test asserting on `QueryClusterNodes()` needs a name nothing else has used.

## Anti-patterns

* **Sleeping for a fire.** `await Task.Delay(2000)` fails at random on a loaded CI agent. Signal.
* **Sharing one scheduler across tests.** State (a paused group, a stored job, a listener) leaks into
  the next test, and the failure appears in whichever test runs second.
* **Asserting on wall-clock times.** `firedAt.Should().BeCloseTo(expected, 100.Milliseconds())` fails on
  a slow day. Assert on the fire times the trigger *computes* (level 0), and on order and counts
  elsewhere.
* **Testing Quartz.** Quartz's own tests cover that a `SimpleTrigger` repeats or that pausing a group
  stops it firing. Test your schedule and your job.

The clock is covered in [Time and TimeProvider](time-and-timeprovider.md), and the builder these tests
use in [Building a Scheduler Without a Host](standalone-scheduler.md).
