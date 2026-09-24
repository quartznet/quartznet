---

title: Library Overview
---

# The Quartz API

The key interfaces and classes of the Quartz API:

| Type | Purpose |
|-|--|
| `IScheduler` | the main API for interacting with the scheduler |
| `IJob` | implemented by components the scheduler executes |
| `IJobDetail` | defines instances of Jobs |
| `ITrigger` | defines the schedule on which a Job executes; a job can have several triggers |
| `JobBuilder` | builds JobDetail instances, which define instances of Jobs |
| `TriggerBuilder` | builds Trigger instances |
| `SchedulerBuilder` | builds scheduler instances; requires Quartz 3.1 or later |

This tutorial uses `IScheduler` and `Scheduler`, `IJob` and `Job`, `IJobDetail` and `JobDetail`, `ITrigger` and `Trigger` interchangeably.

A `Scheduler` lives from its creation by a `SchedulerFactory` until its `Shutdown()` method is called. Once created, the `IScheduler` interface can add, remove and list Jobs and Triggers, and perform other scheduling operations (such as pausing a trigger). The Scheduler does not act on any triggers (execute jobs) until it is started with `Start()`, as shown in [Lesson 1](using-quartz.md).

Quartz's "builder" classes form a fluent interface (a Domain Specific Language, or DSL). From the previous lesson:

```csharp
// define the job and tie it to our HelloJob class
IJobDetail job = JobBuilder.Create<HelloJob>()
    .WithIdentity(name: "myJob", group: "group1")
    .Build();
    
// Trigger the job to run now, and then every 40 seconds
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity(name: "myTrigger", group: "group1")
    .StartNow()
    .WithSimpleSchedule(x => x
        .WithIntervalInSeconds(40)
        .RepeatForever())            
    .Build();

var sched = scheduleFactory.GetScheduler();

// Tell Quartz to schedule the job using our trigger
await sched.ScheduleJob(job, trigger);
```
  
`JobBuilder` creates the `IJobDetail`, and `TriggerBuilder`'s fluent interface creates the trigger.

The schedule extension methods are:

* `WithCalendarIntervalSchedule`
* `WithCronSchedule`
* `WithDailyTimeIntervalSchedule`
* `WithRecurrenceSchedule`: uses [RFC 5545 RRULE](recurrencetrigger) for complex patterns like "2nd Monday of the month" (Quartz 3.18+)
* `WithSimpleSchedule`

`DateBuilder` has methods for building `DateTimeOffset` instances for particular points in time, such as the next even hour (10:00:00 if it is now 9:43:27).
