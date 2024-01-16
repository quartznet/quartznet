---

title: Library Overview
---

# The Quartz API

The key interfaces and classes of the Quartz API are:

| Type | |
|-|--|
| `IScheduler` | the main API for interacting with the scheduler |
| `IJob` | an interface to be implemented by components that you wish to have executed by the scheduler |
| `IJobDetail` | used to define instances of Jobs |
| `ITrigger` | a component that defines the schedule upon which a given Job will be executed, job can have multiple associated triggers |
| `JobBuilder` | used to define/build JobDetail instances, which define instances of Jobs |
| `TriggerBuilder` | used to define/build Trigger instances |
| `SchedulerBuilder` | used to define/build scheduler instances, requires Quartz 3.1 or later |

In this tutorial for readability's sake following terms are used interchangeably: `IScheduler` and `Scheduler`, `IJob` and `Job`, `IJobDetail` and `JobDetail`, `ITrigger` and `Trigger`.

A `Scheduler`'s life-cycle is bounded by its creation via a `SchedulerFactory`, and a call to its `Shutdown()` method.
Once created, the `IScheduler` interface can be used to add, remove, list Jobs and Triggers, and perform other scheduling-related operations (such as pausing a trigger).
However, the Scheduler will not actually act on any triggers (execute jobs) until it has been started with the `Start()` method, as shown in [Lesson 1](using-quartz.md).

Quartz provides "builder" classes that define a Domain Specific Language (or DSL, also sometimes referred to as a "fluent interface"). In the previous lesson you saw an example of it, which we present a portion of here again:

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
  
The block of code that builds the job definition is using `JobBuilder` to create the `IJobDetail`.
Likewise, the block of code that builds the trigger is using `TriggerBuilder`'s fluent interface to
the trigger.

Possible schedule extension methods are:

* `WithCalendarIntervalSchedule`
* `WithCronSchedule`
* `WithDailyTimeIntervalSchedule`
* `WithSimpleSchedule`

The `DateBuilder` type contains various methods for easily constructing `DateTimeOffset`
instances for particular points in time (such as a date that represents the next even
hour — for example, 10:00:00 if it is currently 9:43:27).
