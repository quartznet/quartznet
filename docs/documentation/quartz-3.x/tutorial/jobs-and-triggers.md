---
title: 'Jobs And Triggers'
---

# Jobs and Triggers

**Jobs** and **Triggers** will be the core tools that you use as a developer
working with the Quartz library.

## Jobs

A job is a class that implements the `IJob` interface, which has only one simple method:

```csharp
namespace Quartz
{
    public interface IJob
    {
        Task Execute(JobExecutionContext context);
    }
}
```

When the job's trigger fires (more on that in a moment), the `Execute(..)` method is invoked by one of the scheduler's worker threads.
The `JobExecutionContext` object that is passed to this method provides the job instance with information about its "run-time" environment -
a handle to the `IScheduler` that executed it, a handle to the Trigger that triggered the execution, the job's `IJobDetail` object, and a few other items.

The `IJobDetail` object is created by the Quartz.NET client (your program) at the time the job is added to the scheduler.
It contains various property settings for the job, as well as a `JobDataMap`, which can be used to store state information for a given instance of your job class.
It is essentially the definition of the job instance, and is discussed in further detail in the next lesson.

## Triggers

Trigger objects are used to trigger the execution (or 'firing') of jobs. When you wish to schedule a job, you instantiate a trigger and use its properties to configure the scheduling you wish to have. Triggers may also have a `JobDataMap` associated with them. - this is useful to passing parameters to a 
Job that are specific to the firings of the trigger. Quartz ships with a handful of different trigger types, but the most commonly used types are simple trigger (interface `ISimpleTrigger`) and a cron trigger (interface `ICronTrigger`).

:::warning
[`cron`](https://en.wikipedia.org/wiki/Cron) is the name of an early Linux command-line utility used to schedule
jobs. It developed a specific way of describing how a job runs, however the `CronTrigger` uses a different format where Quartz expects seconds as the first parameter. [More...](/documentation/quartz-3.x/tutorial/crontrigger)
:::

**SimpleTrigger** is handy if you need 'one-shot' execution (just single execution of a job at a given moment in time), or if you need to fire a job at a given time, and have it repeat `N` times, with a delay of `T` between executions. This should feel similar to the .NET Timer class.

```csharp
var example = TriggerBuilder.Create()
    .WithIdentity("trigger-name", "trigger-group")
    .ForJob("job-name", "job-group")
    .WithSimpleSchedule(o =>
    {
        o.WithRepeatCount(5)
            .WithInterval(TimeSpan.FromMinutes(5));
    })
    .Build();
```

**CronTrigger** is useful if you wish to have triggering based on calendar-like schedules - 
such as "every Friday, at noon" or "at 10:15 on the 10th day of every month.". You can use [Cron Maker](http://www.cronmaker.com/) to explore the syntax.

```csharp
var example = TriggerBuilder.Create()
    .WithIdentity("trigger-name", "trigger-group")
    .ForJob("job-name", "job-group")
    .WithCronSchedule("45 23 * * 6")
    .Build();
```

## Why Jobs and Triggers?

Many job schedulers do not have separate notions of jobs and triggers. Some define a 'job' as simply an execution time (or schedule) 
along with some small job identifier. Others are much like the union of Quartz's job and trigger objects. While developing Quartz, we decided that it made sense
 to create a separation between the schedule and the work to be performed on that schedule. This has (in our opinion) many benefits.

For example, Jobs can be created and stored in the job scheduler independent of a trigger, and many triggers can be associated with the same job.
Another benefit of this loose-coupling is the ability to configure jobs that remain in the scheduler after their associated triggers have expired, 
so that that it can be rescheduled later, without having to re-define it. It also allows you to modify or replace a trigger without having to re-define 
its associated job.

## Identities

Jobs and Triggers are given identifying keys as they are registered with the Quartz scheduler. 
The keys of Jobs and Triggers (`JobKey` and `TriggerKey`) allow them to be placed into 'groups' which can be useful for organizing your jobs and
 triggers into categories such as "reporting jobs" and "maintenance jobs". The name portion of the key of a job or trigger must be unique within the group.
The complete key (or identifier) of a job or trigger is the compound of the name and group.

You now have a general idea about what Jobs and Triggers are, you can learn more about them in 
[Lesson 4: More About Jobs & JobDetails](more-about-jobs.md) and [Lesson 5: More About Triggers](more-about-triggers.md)
