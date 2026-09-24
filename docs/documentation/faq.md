---

title: Frequently Asked Questions
sidebarDepth: 0
---

# FAQ

::: tip
Having problems? Check the [Troubleshooting Guide](troubleshooting.md) for common issues and solutions.
:::

# General Questions

## What is Quartz

Quartz is a job scheduler: a system that runs (or notifies) other software components when a
scheduled time arrives. It can be used alongside almost any other software.

* It is lightweight, and needs little setup for basic needs.
* It supports several usage styles, alone or combined, so your code can take the shape that suits
  your project.
* It is fault-tolerant, and can persist scheduled jobs across restarts.
* Beyond running system processes on a schedule, it can drive your application's business processes.

## What is Quartz - From a Software Component View?

Quartz is a small library (`.dll`) containing all the core functionality.

* The main API is the scheduler interface: schedule and unschedule jobs; start, stop and pause the
  scheduler.
* Components you want run must implement the job interface and its execute method.
* Components you want notified when a fire time arrives implement the trigger listener or job
  listener interface.

The scheduler can run inside your own application, or as a stand-alone application with a remote
interface.

# Why not just use System.Timers.Timer?

`System.Timers.Timer` is .NET's built-in timer. Compared with Quartz:

* Timers have no persistence.
* Timers schedule inflexibly: a start time and a repeat interval, nothing based on dates or time of
  day.
* Timers have no concurrency limit: each `Elapsed` callback runs on the .NET thread pool, and a run
  that outlasts the interval overlaps the next one.
* Timers have no management: remembering, organizing and finding tasks by name is up to you.

For a simple application these may not matter, and then Quartz.NET may not be the right choice.

# Miscellaneous Questions

## How many jobs is Quartz capable of running?

It depends on these factors.

**How many can be stored** is limited by the job store's space: RAM for `RAMJobStore`, disk for the
ADO.NET store.

* The RAM-based store is much faster, about 1000x, than the ADO.NET store, but you have less RAM
  than disk.
* The ADO.NET store's speed depends almost entirely on the database connection, the database system
  and the database hardware. Quartz itself does very little processing; nearly all the time is spent
  in the database. See [How do I improve the performance of AdoJobStore?](#how-do-i-improve-the-performance-of-adojobstore)

**How many can run at once** is limited by the thread pool's maximum concurrency (default 10): with
a maximum of five, at most five jobs run at a time.

* The default pool is a permit count over the .NET thread pool, not a set of dedicated threads.
  Blocking jobs still starve it; see
  [Max concurrency is a permit count](best-practices.md#max-concurrency-is-a-permit-count-not-a-thread-count).
* Listeners slow the scheduler. Time spent in `TriggerListener`s, `JobListener`s and
  `SchedulerListener`s is added to each execution. Prefer specific listeners to global ones, avoid
  expensive work in them, and remember that many plugins (such as the history plugin) are listeners.
* Long-running or CPU-intensive jobs limit how many run at once and in a given time.

If one instance is not enough, load-balance several Quartz instances on separate machines. Each runs
jobs out of the shared database first-come first-served, as quickly as the triggers need firing.

For scale: some Quartz (Java) installations manage hundreds of thousands of jobs and triggers and
run dozens of jobs at any moment, without load-balancing.

# Questions About Jobs

## How can I control the instantiation of Jobs?

Implement `IJobFactory` (`Quartz.Spi.IJobFactory` in Quartz 3.x, `Quartz.Extensibility.IJobFactory`
in Quartz 4.x) and tell the scheduler to use it:

| Version | How |
|---|---|
| 3.x | Assign it to the `IScheduler.JobFactory` property, or name the type in the `quartz.scheduler.jobFactory.type` key |
| 4.x | `UseJobFactory<MyJobFactory>()` or `UseJobFactory(new MyJobFactory())` on the builder; the `quartz.scheduler.jobFactory.type` key still works. The scheduler has no `JobFactory` property. |

On both versions the Microsoft dependency injection integration installs its own job factory. For
constructor injection into jobs, you do not need to write one.

## How do I keep a Job from being removed after it completes?

Build it with `JobBuilder.Create<MyJob>().StoreDurably()`. Quartz then does not delete the job when it
becomes an "orphan", with no trigger referencing it. `IJobDetail.Durable` reports the flag; it is set
when the detail is built.

## How do I keep a Job from firing concurrently?

* **Quartz.NET 2.x, 3.x and 4.x:** implement `IJob` and decorate the job class with
  `[DisallowConcurrentExecution]`. See the API documentation for `DisallowConcurrentExecutionAttribute`.
* **Quartz.NET 1.x:** implement `IStatefulJob` instead of `IJob`. See the API documentation for
  `IStatefulJob`.

## How do I stop a Job that is currently executing?

**Quartz 1.x and 2.x:** see the `Quartz.IInterruptableJob` interface and the
`IScheduler.Interrupt(string, string)` method.

**Quartz 3.x and 4.x:** ask the scheduler to interrupt it.

* `IScheduler.Interrupt(jobKey)` interrupts every execution of the job.
* One firing, by its fire instance id: `InterruptFireInstance(id)` in 4.x, the `Interrupt(string)`
  overload in 3.x.
* In 4.x, list the executions to choose from with
  `IScheduler.QueryFireInstances(new FireInstanceQuery())`. With a persistent job store this sees the
  whole cluster, though the node running the execution handles the interrupt.

Both cancel the token the execution was given, so the job must co-operate: check
`IJobExecutionContext.CancellationToken.IsCancellationRequested`, or forward the token to what you
await, and return early when cancellation is requested. In 4.x the token is also a parameter of
`Execute`, so forwarding it is the default.

# Questions About Triggers

## How do I chain Job execution? Or, how do I create a workflow?

For "when this job finishes, run that one", Quartz ships `JobChainingJobListener`. Register it with
the pairs to chain, and it triggers the second job when the first completes:

<!-- snippet: sample_faq_job_chaining -->
```csharp
JobChainingJobListener chain = new("chain");
chain.AddJobChainLink(new JobKey("extract"), new JobKey("transform"));
chain.AddJobChainLink(new JobKey("transform"), new JobKey("load"));

scheduler.ListenerManager.AddJobListener(chain);
```
<!-- endSnippet -->

* The links live in memory with the listener, so register them on every start.
* The second job is fired, not scheduled: there is no trigger to see in the store.

In 4.x one job can chain to several follow-ups (a fan-out): call `AddJobChainLink` again with the
same first job, or name them all with `AddJobChainLinks`. Each follow-up is its own firing, so they
run concurrently, as many at a time as the thread pool has threads:

<!-- snippet: sample_faq_job_chaining_fan_out -->
```csharp
JobChainingJobListener chain = new("chain");
chain.AddJobChainLinks(new JobKey("transform"), [new JobKey("load-warehouse"), new JobKey("load-cache")]);
chain.AddJobChainLink(new JobKey("transform"), new JobKey("notify"));

scheduler.ListenerManager.AddJobListener(chain);
```
<!-- endSnippet -->

* A follow-up that must wait for a sibling is a link from *that* sibling, not a second link from the
  same job.
* Chaining the same follow-up to one job twice is rejected, since it would fire twice for one
  completion.
* On 3.x, a second link from the same first job throws.

For anything more:

* **A continuation (4.2).** A trigger that waits for another trigger's firing to end and fires on
  the outcomes you choose. See [Job Continuations](quartz-4.x/how-tos/job-continuations.md).
* **A listener** (`TriggerListener`, `JobListener` or `SchedulerListener`) notices a job completing
  and schedules a new trigger at once. You have to tell the listener which job follows which, and
  may need to persist that.
* **A job that schedules the next one.** Put the next job's name in the `JobDataMap`, and have the
  job schedule it as the last step of `Execute()`. A common shape is an abstract base job that reads
  the next job's name and group from the map under known keys and schedules it; each real job
  derives from it and adds its own work.

## Why isn't my trigger firing?

1. Most often, `Scheduler.Start()` was not called. Nothing fires until it is.
2. Next most often, the trigger or its trigger group is paused.

## Daylight Saving Time and Triggers

`SimpleTrigger` and `CronTrigger` each handle daylight saving time in the way natural to the trigger
type. Transition rules differ by country ([overview](http://webexhibits.org/daylightsaving/g.html)):
both the date and the time of the shift vary, and many places shift at 2:00 am but others at
1:00 am, 3:00 am or midnight.

**`SimpleTrigger` fires every N milliseconds**, with no relation to the time of day, so a transition
changes nothing. A trigger firing every 12 hours at 3:00 am and 3:00 pm before a transition fires at
4:00 am and 4:00 pm after it. That is not a bug: the interval is unchanged, only the wall-clock name
of the instant moved.

**`CronTrigger` fires at times of day.** A trigger for 10:00 am every day keeps firing at 10:00 am,
so the interval across a spring or autumn transition is 23 or 25 hours.

A `CronTrigger` is **never skipped** by a transition, and never fires twice for one scheduled
occurrence. The transition decides *which instant* a missing or repeated wall-clock time maps to,
and that depends on the kind of expression:

* **Fixed-time:** the second, minute and hour fields are plain values or comma lists of plain values,
  such as `0 15 2 * * ?` or `0 0,30 2 * * ?`.
* **Interval:** a wildcard, step or range in the second, minute or hour field, such as a trigger
  every 15 minutes of every hour.

For a fixed-time trigger at 2:15 am daily in the United States, where transitions happen at 2:00 am:

| Day | 4.x | 3.x |
|---|---|---|
| Daylight saving **begins**: 2:15 am does not exist | Fires once, at 3:00 am, the end of the gap | Fires once, shifted forward by the delta, at 3:15 am |
| Daylight saving **ends**: 2:15 am occurs twice | Fires once, at the first occurrence | Same as 4.x |

* On 4.x, the gap-end instant is the one in-gap instant the expression matches, so `IsSatisfiedBy`
  agrees with the fire time.
* A zone whose delta is not a whole hour, such as Australia/Lord_Howe, splits the same two ways on
  its own numbers.
* An expression matching several of the swallowed times, such as `0 0,15,30,45 2 * * ?`, still fires
  once: they all name the same instant.

Interval expressions are where the versions differ most:

* **4.x** keeps firing through the repeated hour, so both passes run.
* **3.x** fires the repeated hour once. On the day daylight saving ends, an hour of real time passes
  with no firing: at 2:00 am the clock returns to 1:00 am, the one o'clock firings have already
  happened, and the next fire time is already 2:00 am.
* Over a spring-forward gap, 4.x's gap-end rule adds a fire: an hourly `0 30 * * * ?` fires at 3:00
  for the occurrence the gap swallowed and again at 3:30 for the next hour. 3.x resumes from the
  shifted 3:30 and fires once.

Whatever the schedule, **name the time zone**. A trigger with no zone uses `TimeZoneInfo.Local`, so
the same expression means different things on a developer's machine and in a container. 4.x's
[Cron Expression Reference](quartz-4.x/cron-expressions.md#daylight-saving-time) states the rules on
their own, and
[Daylight saving, clock changes and cluster skew](best-practices.md#daylight-saving-clock-changes-and-cluster-skew)
covers choosing the trigger family that means what you meant.

## System clock changes (NTP corrections, manual adjustments)

An NTP correction, a manual change or a suspended and resumed virtual machine can move the system
clock by any amount, in either direction.

Quartz schedules against the wall clock. If the clock moves *backward* an hour, a trigger whose next
fire time was already computed waits until the clock catches up, living through the hour twice. That
is expected: a next fire time is a point on the calendar, not an offset from "now".

Quartz never waits on one unbounded sleep derived from the wall clock. It re-reads the time at
bounded intervals, so it resumes on its own after any clock change, within:

| Part | Recovers within |
|---|---|
| Firing loop | One `quartz.scheduler.idleWaitTime` (30 seconds by default) |
| Misfire handling (AdoJobStore) | One `quartz.jobStore.misfireHandlerFrequency` |
| Cluster check-in (AdoJobStore) | One `quartz.jobStore.clusterCheckinInterval` |

Allow up to one of those intervals after the clock is corrected. To test clock changes, fake the
clock (see `TimeProvider`) rather than changing the machine clock, so only the wall clock moves and
monotonic timers are unaffected.

# Questions About AdoJobStore

## How do I improve the performance of AdoJobStore?

* Use the driver delegate specific to your database, such as `SqlServerDelegate`. This is the
  practical one.
* A faster network between the Quartz machine and the database machine.
* A more powerful database machine.
* A better RDBMS.
* Use the latest version of the library.

# Quartz in web environment

## Scheduler keeps stopping when application pool gets recycled

By default IIS recycles and stops app pools from time to time. Even if `Application_Start` starts
Quartz on the first request, the scheduler can be disposed later because the site is idle.

On IIS 8, configure the site to be preloaded and kept running; see
[this blog post](https://blogs.msdn.microsoft.com/vijaysk/2012/10/11/iis-8-whats-new-website-settings/).
For web environments, IIS and hosted services in more detail, see the
[Troubleshooting Guide](troubleshooting.md#scheduler-in-web-environments).

# Quartz.NET 4.x Questions

## What .NET version does Quartz 4.x require?

.NET 10.0 or later. Quartz.NET 4.x targets .NET 10.0.

## Can I use Task instead of ValueTask in Quartz 4.x?

No. Quartz 4.x changed all `Task` return types to `ValueTask`, so `IJob.Execute` must return
`ValueTask`:

<!-- snippet: sample_faq_value_task_execute -->
```csharp
public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
{
    // your job logic
}
```
<!-- endSnippet -->

If you need `Task` semantics elsewhere (for example, to await a result several times), call
`.AsTask()` on the `ValueTask` once and use the resulting `Task`. See the
[Migration Guide](/documentation/quartz-4.x/migration-guide.md#tasks-changed-to-valuetask).

## What happened to Quartz.Extensions.DependencyInjection and Quartz.Extensions.Hosting?

They were merged into the main `Quartz` package in 4.x. Remove the separate package references;
`AddQuartz()` and `AddQuartzHostedService()` come from the `Quartz` package.

## How do I replace SystemTime in Quartz 4.x?

`SystemTime` was removed in 4.x. Use .NET's `TimeProvider` abstraction instead, for example to
control time in unit tests:

<!-- Not a compiled sample: `FakeTimeProvider` comes from `Microsoft.Extensions.TimeProvider.Testing`,
     which this repository does not reference outside its test projects. -->

```csharp
QuartzSchedulerBuilder builder = QuartzSchedulerBuilder.Create(q => q.UseTimeProvider(new FakeTimeProvider()));
```
