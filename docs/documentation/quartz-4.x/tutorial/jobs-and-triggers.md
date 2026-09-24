---

title: 'Jobs And Triggers'
---

## The Quartz API

| Type | Role |
|---|---|
| `IScheduler` | the main API for interacting with the scheduler |
| `IJob` | implemented by the work the scheduler runs |
| `IJobDetail` | defines an instance of a job |
| `ITrigger` | the schedule a job runs on; a job can have several triggers |
| `JobBuilder` | builds `IJobDetail` instances |
| `TriggerBuilder` | builds `ITrigger` instances |
| `QuartzSchedulerBuilder` | builds a scheduler when there is no host to register Quartz with |

The tutorial uses `IScheduler` and `Scheduler`, `IJob` and `Job`, `IJobDetail` and `JobDetail`, and
`ITrigger` and `Trigger` interchangeably.

A scheduler exists from the moment the container builds it until `Shutdown()`. Meanwhile `IScheduler`
adds, removes and lists jobs and triggers and performs other operations such as pausing a trigger.
Triggers do not fire until `Start()` is called; the hosted service calls it, as shown in
[Lesson 1](using-quartz.md).

Jobs and triggers are defined with fluent builders. The example from the previous lesson:

<!-- snippet: sample_jobs_and_triggers_builders -->
```csharp
// define the job and tie it to our HelloJob class
IJobDetail job = JobBuilder.Create<HelloJob>()
    .WithIdentity("myJob", "group1") // name "myJob", group "group1"
    .Build();

// Trigger the job to run now, and then every 40 seconds
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger", "group1")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithInterval(TimeSpan.FromSeconds(40))
        .RepeatForever())
    .Build();

// Tell Quartz to schedule the job using our trigger
await scheduler.ScheduleJob(job, trigger);
```
<!-- endSnippet -->
  
`JobBuilder` builds the `IJobDetail`. `TriggerBuilder` builds the trigger, with one schedule method per
trigger type:

| Method | Schedule |
|---|---|
| `WithSimpleSchedule` | a fixed interval, repeated a number of times or forever ([SimpleTriggers](simpletriggers.md)) |
| `WithCronSchedule` | a cron expression ([CronTriggers](crontriggers.md)); `H` tokens [spread fire times across triggers](../cron-expressions.md#h-hash-for-load-distribution) |
| `WithRecurrenceSchedule` | an [RFC 5545 RRULE](recurrencetrigger.md), for patterns cron cannot say, such as "the 2nd Monday of the month" |
| `WithCalendarIntervalSchedule` | an interval in calendar units: "every month" keeps the day of the month, "every day" keeps the wall-clock hour across a daylight saving change |
| `WithDailyTimeIntervalSchedule` | an interval inside a daily time window on chosen days of the week |

A daily time interval schedule looks like `StartingDailyAt(new TimeOnly(9, 0))`,
`EndingDailyAt(new TimeOnly(17, 0))`, `OnMondayThroughFriday()`. Times of day are `TimeOnly` in 4.0;
3.x had its own `TimeOfDay` type.

`DateBuilder` builds a `DateTimeOffset` that is awkward to write out:
`DateBuilder.Create().AtHourMinuteAndSecond(22, 0, 0).Build()` is today at 22:00:00, and
`DateBuilder.CreateInTimeZone(timeZone)` does the same in a given zone. When you pass the scheduler's
`TimeProvider`, both read the clock through it, so a test that fakes time sees the faked time.

## Jobs and Triggers

A job is a class that implements the `IJob` interface, which has only one simple method:

__IJob Interface__

<!-- Quartz's own declaration of the interface, so it is written out here rather than compiled from the
     samples project: a second `Quartz.IJob` in that project would shadow the real one. -->

```csharp
namespace Quartz
{
    public interface IJob
    {
        ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default);
    }
}
```

When the job's trigger fires, one of the scheduler's worker threads calls `Execute`. The
`IJobExecutionContext` carries the run-time environment: the scheduler that ran the job, the trigger that
fired it, the job's `JobDetail`, and a few other items.

`cancellationToken` is the same token as `context.CancellationToken`. It is a separate parameter so that
analysis (CA2016) catches a job that does not forward it.

Your program creates the `JobDetail` when it adds the job to the scheduler. It holds the job's settings
and a `JobDataMap` for state; [the next lesson](more-about-jobs.md) covers it.

A trigger fires a job. It can also carry a `JobDataMap`, for parameters specific to its firings. The
common trigger types:

| Trigger | Interface | Use for |
|---|---|---|
| SimpleTrigger | `ISimpleTrigger` | one-shot execution at a moment, or a start time repeated N times with a delay of T between executions |
| CronTrigger | `ICronTrigger` | calendar-like schedules: "every Friday, at noon", "at 10:15 on the 10th day of every month" |
| [RecurrenceTrigger](recurrencetrigger.md) | `IRecurrenceTrigger` | RFC 5545 RRULE patterns cron cannot express: "every 2nd Monday of the month", "every other week on specific days", "the last weekday of March each year" |

Jobs and triggers are separate so that:

* a job can be stored without a trigger, and many triggers can fire the same job;
* a job can stay in the scheduler after its triggers expire and be rescheduled later without being
  re-defined;
* a trigger can be changed or replaced without re-defining its job.

## Identities

Jobs and triggers are identified by keys (`JobKey` and `TriggerKey`), each a name plus a group. Groups
organize jobs and triggers into categories such as "reporting jobs" and "maintenance jobs". A name must
be unique within its group. A key given only a name is in the group `Key<T>.DefaultGroup`, `"DEFAULT"`.

Next: [Lesson 3: More About Jobs & JobDetails](more-about-jobs.md) and
[Lesson 5: More About Triggers](more-about-triggers.md).
