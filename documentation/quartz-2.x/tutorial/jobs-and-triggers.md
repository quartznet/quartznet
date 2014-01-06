---
title: 'Lesson 2: Jobs And Triggers'
layout: default
---

## The Quartz API

The key interfaces of the Quartz API are:

* Scheduler - the main API for interacting with the scheduler.
* Job - an interface to be implemented by components that you wish to have executed by the scheduler.
* JobDetail - used to define instances of Jobs.
* Trigger - a component that defines the schedule upon which a given Job will be executed.
* JobBuilder - used to define/build JobDetail instances, which define instances of Jobs.
* TriggerBuilder - used to define/build Trigger instances.

A Scheduler's life-cycle is bounded by it's creation, via a SchedulerFactory and a call to its Shutdown() method. 
Once created the IScheduler interface can be used add, remove, and list Jobs and Triggers, and perform other scheduling-related operations (such as pausing a trigger). 
However, the Scheduler will not actually act on any triggers (execute jobs) until it has been started with the Start() method, as shown in [Lesson 1](using-quartz.html).

Quartz provides "builder" classes that define a Domain Specific Language (or DSL, also sometimes referred to as a "fluent interface"). In the previous lesson you saw an example of it, which we present a portion of here again:

```c#
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

As mentioned previously, you can make .NET component executable by the scheduler simply by making it
implement the IJob interface. Here is the interface:

__IJob Interface__

```c#
    namespace Quartz
    {
        public interface IJob
        {
            void Execute(JobExecutionContext context);
        }
    }
```	

In case you couldn't guess, when the job's trigger fires (more on that in a moment), the Execute(..) method
is invoked by the scheduler. The JobExecutionContext object that is passed to this method provides 
the job instance with information about its "run-time" environment - a handle to the IScheduler that executed it,
a handle to the Trigger that triggered the execution, the job's JobDetail object, and a few other items.

The JobDetail object is created by the Quartz.NET client (your program) at the time the Job is added
to the scheduler. It contains various property settings for the Job, as well as a JobDataMap, which can be used
to store state information for a given instance of your job class.

Trigger objects are used to trigger the execution (or 'firing') of jobs. When you wish to schedule a job, 
you instantiate a trigger and 'tune' its properties to provide the scheduling you wish to have. 
Triggers may also have a JobDataMap associated with them - this is useful to passing parameters to a Job 
that are specific to the firings of the trigger. Quartz.NET ships with a handful of different trigger types, 
but the most commonly used types are SimpleTrigger and CronTrigger.

SimpleTrigger is handy if you need 'one-shot' execution (just single execution of a job at a given moment in time),
or if you need to fire a job at a given time, and have it repeat N times, with a delay of T between executions. 
CronTrigger is useful if you wish to have triggering based on calendar-like schedules - such as "every Friday,
at noon" or "at 10:15 on the 10th day of every month."

Why Jobs AND Triggers? Many job schedulers do not have separate notions of jobs and triggers. Some define a 'job' as simply an 
execution time (or schedule) along with some small job identifier. Others are much like the union 
of Quartz.NET's job and trigger objects. While developing Quartz for Java, Quartz team decided that it made sense to create 
a separation between the schedule and the work to be performed on that schedule. This has (in our opinion) 
many benefits.

For example, jobs can be created and stored in the job scheduler independent of a trigger, and many triggers 
can be associated with the same job. Another benefit of this loose-coupling is the ability to configure jobs 
that remain in the scheduler after their associated triggers have expired, so that that it can be rescheduled 
later, without having to re-define it. It also allows you to modify or replace a trigger without having to 
re-define its associated job.

## Identities

Jobs and Triggers are given identifying names as they are registered with the Quartz.NET scheduler. 
Jobs and triggers can also be placed into 'groups' which can be useful for organizing your jobs and triggers 
into categories for later maintenance. The name of a job or trigger must be unique within its group - or in other
words, the true identifier of a job or trigger is its name + group. If you leave the group of the 
Job or Trigger 'null', it is equivalent to having specified SchedulerConstants.DefaultGroup.

You now have a general idea about what Jobs and Triggers are, you can learn more about them in 
[Lesson 3: More About Jobs & JobDetails](more-about-jobs.html) and [Lesson 4: More About Triggers](more-about-triggers.html)

[&laquo; Lesson 1](using-quartz.html) | [Lesson 3 &raquo;](more-about-jobs.html)
