---
title: 'Lesson 2: Jobs And Triggers'
---

## The Quartz API

The key interfaces and classes of the Quartz API are:

* IScheduler - the main API for interacting with the scheduler.
* IJob - an interface to be implemented by components that you wish to have executed by the scheduler.
* IJobDetail - used to define instances of Jobs.
* ITrigger - a component that defines the schedule upon which a given Job will be executed.
* JobBuilder - used to define/build JobDetail instances, which define instances of Jobs.
* TriggerBuilder - used to define/build Trigger instances.

In this tutorial for readability's sake following terms are used interchangeably: IScheduler and Scheduler, IJob and Job, IJobDetail and JobDetail, ITrigger and Trigger.

A **Scheduler**'s life-cycle is bounded by it's creation, via a **SchedulerFactory** and a call to its Shutdown() method. 
Once created the IScheduler interface can be used add, remove, and list Jobs and Triggers, and perform other scheduling-related operations (such as pausing a trigger). 
However, the Scheduler will not actually act on any triggers (execute jobs) until it has been started with the Start() method, as shown in [Lesson 1](using-quartz.md).

Quartz provides "builder" classes that define a Domain Specific Language (or DSL, also sometimes referred to as a "fluent interface"). In the previous lesson you saw an example of it, which we present a portion of here again:

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
			.WithIntervalInSeconds(40)
			.RepeatForever())            
		.Build();
		
	// Tell quartz to schedule the job using our trigger
	sched.scheduleJob(job, trigger);
```
  
The block of code that builds the job definition is using JobBuilder using fluent interface to create the product, IJobDetail.
Likewise, the block of code that builds the trigger is using TriggerBuilder's fluent interface and extension methods that are specific to given trigger type.
Possible schedule extension methods are:

* WithCalendarIntervalSchedule
* WithCronSchedule
* WithDailyTimeIntervalSchedule
* WithSimpleSchedule

The DateBuilder class contains various methods for easily constructing DateTimeOffset instances for particular points in time (such as a date that represents the next even hour - or in other words 10:00:00 if it is currently 9:43:27).

## Jobs and Triggers

A Job is a class that implements the IJob interface, which has only one simple method:

__IJob Interface__

```csharp
    namespace Quartz
    {
        public interface IJob
        {
            void Execute(JobExecutionContext context);
        }
    }
```	

When the Job's trigger fires (more on that in a moment), the Execute(..) method is invoked by one of the scheduler's worker threads.
The JobExecutionContext object that is passed to this method provides the job instance with information about its "run-time" environment -
a handle to the Scheduler that executed it, a handle to the Trigger that triggered the execution, the job's JobDetail object, and a few other items.

The JobDetail object is created by the Quartz.NET client (your program) at the time the Job is added to the scheduler.
It contains various property settings for the Job, as well as a JobDataMap, which can be used to store state information for a given instance of your job class.
It is essentially the definition of the job instance, and is discussed in further detail in the next lesson.

Trigger objects are used to trigger the execution (or 'firing') of jobs. When you wish to schedule a job, you instantiate a trigger and 'tune' its properties
to provide the scheduling you wish to have. Triggers may also have a JobDataMap associated with them - this is useful to passing parameters to a 
Job that are specific to the firings of the trigger. Quartz ships with a handful of different trigger types, but the most commonly used types 
are SimpleTrigger (interface ISimpleTrigger) and CronTrigger (interface ICronTrigger).

SimpleTrigger is handy if you need 'one-shot' execution (just single execution of a job at a given moment in time), or if you need to fire a job at a given time,
and have it repeat N times, with a delay of T between executions. CronTrigger is useful if you wish to have triggering based on calendar-like schedules - 
such as "every Friday, at noon" or "at 10:15 on the 10th day of every month."

Why Jobs AND Triggers? Many job schedulers do not have separate notions of jobs and triggers. Some define a 'job' as simply an execution time (or schedule) 
along with some small job identifier. Others are much like the union of Quartz's job and trigger objects. While developing Quartz, we decided that it made sense
 to create a separation between the schedule and the work to be performed on that schedule. This has (in our opinion) many benefits.

For example, Jobs can be created and stored in the job scheduler independent of a trigger, and many triggers can be associated with the same job.
Another benefit of this loose-coupling is the ability to configure jobs that remain in the scheduler after their associated triggers have expired, 
so that that it can be rescheduled later, without having to re-define it. It also allows you to modify or replace a trigger without having to re-define 
its associated job.

## Identities

Jobs and Triggers are given identifying keys as they are registered with the Quartz scheduler. 
The keys of Jobs and Triggers (JobKey and TriggerKey) allow them to be placed into 'groups' which can be useful for organizing your jobs and
 triggers into categories such as "reporting jobs" and "maintenance jobs". The name portion of the key of a job or trigger must be unique within the group
- or in other words, the complete key (or identifier) of a job or trigger is the compound of the name and group.

You now have a general idea about what Jobs and Triggers are, you can learn more about them in 
[Lesson 3: More About Jobs & JobDetails](more-about-jobs.md) and [Lesson 4: More About Triggers](more-about-triggers.md)
