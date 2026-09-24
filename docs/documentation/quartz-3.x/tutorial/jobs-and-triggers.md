---

title: 'Jobs And Triggers'
---

# Jobs and Triggers

**Jobs** and **Triggers** are the core of the Quartz library.

## Jobs

A job is a class that implements the `IJob` interface, which has one method:

```csharp
namespace Quartz
{
    public interface IJob
    {
        Task Execute(JobExecutionContext context);
    }
}
```

When the job's trigger fires, one of the scheduler's worker threads calls `Execute(..)`. The `JobExecutionContext` passed to it describes the job's run-time environment: the `IScheduler` that executed it, the Trigger that fired it, the job's `IJobDetail` object, and a few other items.

Your program creates the `IJobDetail` when it adds the job to the scheduler. It is the definition of the job instance: property settings for the job, and a `JobDataMap` that can store state for a given instance of your job class. The next lesson covers it in detail.

## Triggers

A trigger fires the execution of a job. To schedule a job, create a trigger and set its properties to the schedule you want. A trigger can have its own `JobDataMap`, to pass the job parameters specific to that trigger's firings. Quartz has several trigger types; the most used are the simple trigger (interface `ISimpleTrigger`) and the cron trigger (interface `ICronTrigger`).

:::warning
[`cron`](https://en.wikipedia.org/wiki/Cron) is an early Linux command-line job scheduler with its own schedule format. `CronTrigger` uses a different format, with seconds as the first field. [More...](/documentation/quartz-3.x/tutorial/crontrigger)
:::

**SimpleTrigger** is for 'one-shot' execution (a single execution at a given moment), or for firing at a given time and repeating `N` times with a delay of `T` between executions, much like the .NET Timer class.

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

**CronTrigger** is for calendar-like schedules, such as "every Friday, at noon" or "at 10:15 on the 10th day of every month". `WithCronSchedule` supports `H` (hash) tokens to [spread fire times across triggers](crontrigger#h-hash-for-load-distribution). See [CronTrigger](crontrigger) for the syntax, and `CronExpressionBuilder` to compose an expression without writing the string.

```csharp
var example = TriggerBuilder.Create()
    .WithIdentity("trigger-name", "trigger-group")
    .ForJob("job-name", "job-group")
    .WithCronSchedule("45 23 * * 6")
    .Build();
```

**[RecurrenceTrigger](recurrencetrigger.md)** (Quartz 3.18+) is for calendar patterns cron cannot express, such as "every 2nd Monday of the month", "every other week on specific days" or "the last weekday of March each year". It uses RFC 5545 RRULE strings.

```csharp
var example = TriggerBuilder.Create()
    .WithIdentity("trigger-name", "trigger-group")
    .ForJob("job-name", "job-group")
    .WithRecurrenceSchedule("FREQ=MONTHLY;BYDAY=2MO")
    .Build();
```

## Why Jobs and Triggers?

Quartz separates the schedule (trigger) from the work (job). As a result:

* Jobs can be stored in the scheduler without a trigger, and many triggers can use the same job.
* A job can stay in the scheduler after its triggers have expired, and be rescheduled later without being re-defined.
* A trigger can be modified or replaced without re-defining its job.

## Identities

Jobs and triggers get identifying keys (`JobKey` and `TriggerKey`) when they are registered with the scheduler. A key has a name and a group; groups organize jobs and triggers into categories such as "reporting jobs" and "maintenance jobs". The name must be unique within the group, and name plus group is the complete key.

More in [Lesson 4: More About Jobs & JobDetails](more-about-jobs.md) and [Lesson 5: More About Triggers](more-about-triggers.md).
