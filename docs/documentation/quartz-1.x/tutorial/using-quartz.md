---
title: 'Lesson 1: Using Quartz'
---

Before you can use the scheduler, it needs to be instantiated (who'd have guessed?).
To do this, you use an implementor of ISchedulerFactory.

Once a scheduler is instantiated, it can be started, placed in stand-by mode, and shutdown.
Note that once a scheduler is shutdown, it cannot be restarted without being re-instantiated.
Triggers do not fire (jobs do not execute) until the scheduler has been started, nor while it is
in the paused state.

Here's a quick snippet of code, that instantiates and starts a scheduler, and schedules a job for execution:

__Using Quartz.NET__

```csharp
    // construct a scheduler factory
    ISchedulerFactory schedFact = new StdSchedulerFactory();
    
    // get a scheduler
    IScheduler sched = schedFact.GetScheduler();
    sched.Start();
    
    // construct job info
    JobDetail jobDetail = new JobDetail("myJob", null, typeof(HelloJob));
    // fire every hour
    Trigger trigger = TriggerUtils.MakeHourlyTrigger();
    // start on the next even hour
    trigger.StartTimeUtc = TriggerUtils.GetEvenHourDate(DateTime.UtcNow);
    trigger.Name = "myTrigger";
    sched.ScheduleJob(jobDetail, trigger);
```

As you can see, working with Quartz.NET is rather simple. In [Lesson 2](jobs-and-triggers.md) we'll give a quick overview of Jobs and Triggers, so that you can more fully understand this example.
