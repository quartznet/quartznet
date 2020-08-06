---
layout: default
title: Version Migration Guide
---

*This document outlines changes needed per version upgrade basis. You need to check the steps for each version you are jumping over. You should also check [the complete change log](https://raw.github.com/quartznet/quartznet/master/changelog.txt).*

**If you are a new user starting with the latest version, you don't need to follow this guide. Just jump right to [the tutorial](tutorial/index.html)**

## Upgrading to 2.2 from 2.1

### Database schema changes

Database schema has changed to include the scheduled time for fired triggers table. You need to run the migration script:

	database\schema_20_to_22_upgrade.sql

**Make sure you check the scheduler name in the script - the default value of sched_name column is TestScheduler! If you have existing data the scheduler name should correspond to your existing scheduler name in Quartz configuration (quartz.scheduler.instanceName).**
	
### Other

* SchedulerStarting() method was added to ISchedulerListener interface
* dbFailureRetryInterval parameter was removed from DirectSchedulerFactory API
	
There are variations for different database server inside the script. Choose the one suiting you the best.

## Upgrading to 2.1 from 2.0

* NthIncludedDayTrigger was removed that was supposed to be removed in 2.0
* There are no Visual Studio 2008 solutions and projects anymore, you need VS2010 or later

## Upgrading to 2.0 from 1.0

### Database schema changes

Database has changed since 1.0 version. You need to run the database migration script:

	database\sqlserver_schema_10_to_20_upgrade.sql
	
The script is made for SQL Server, but should work for others. You can adapt the script when needed for your specific database. **Always test the migration on non-production server before upgrading production**

### API Changes
				
The most obvious differences with version 2.0 are the significant changes to the API. 
These changes have aimed to: modernize the API to use collections and generics, remove ambiguities and redundancies,
hide/remove methods that should not be public to client code, improve separation of concerns, and introduce
a Domain Specific Language (DSL) for working with the core entities (jobs and triggers).
				
				
While the API changes are significant, and the usage of the "new way of doing things" is highly encouraged, 
there are some formulaic (search-and-replace) techniques that can be used to get 1.x code quickly working with version 2.0.
See the migration guide for more information.
				
**Outline of most significant API changes:**

				
API methods that return (or take as parameters) arrays now return (or take) typed collections. 
For example, rather than GetJobGroupNames(): string[] we now have GetJobGroupNames(): IList&lt;string&gt;
Job and Trigger identification is now based on JobKey and TriggerKey. Keys include both a name and group. 
Methods which operate on particular jobs/triggers now take keys as the parameter. For example, GetTrigger(TriggerKey key): ITrigger, 
rather than GetTrigger(string name, string group): Trigger.
ITrigger is now an interface, rather than a class. Likewise for ISimpleTrigger, ICronTrigger, etc.
New DSL/builder-based API for construction Jobs and Triggers:

```csharp
IJobDetail job = JobBuilder.Create<SimpleJob>()
	.WithIdentity("job1", "group1")
	.Build();

ITrigger trigger = TriggerBuilder.Create()
	.WithIdentity("trigger1", "group1")
	.StartAt(DateBuilder.FutureDate(2, IntervalUnit.Hour))
	.WithSimpleSchedule(x => x.RepeatHourlyForever())
	.ModifiedByCalendar("holidays")
	.Build();
```

Methods from TriggerUtils related to easy construction of Dates have been moved to new DateBuilder class,
that can be used with static imports to nicely create Date instances for trigger start and end times, etc.

```csharp
// build a date for 9:00 am on Halloween
DateTimeOffset runDate = DateBuilder.DateOf(0, 0, 9, 31, 10);

// build a date 2 hours in the future
DateTimeOffset myDate = DateBuilder.FutureDate(2, IntervalUnit.Hour);
```

The IStatefulJob interface has been deprecated in favor of new class-level attributes for IJob implementations 
(using both attributes produces equivalent to that of the old IStatefulJob interface):
				
```csharp
// instructs the scheduler to re-store the Job's JobDataMap contents after execution completes
[PersistJobDataAfterExecution]
public class MyJob : IJob
{
}
```


```csharp
// instructs the scheduler to block other instances of the same job (by JobKey) from executing when one already is
[DisallowConcurrentExecution]
public class MyJob : IJob
{
}
```

**Significant changes to usage of JobListener and TriggerListener**
					
* Removal of distinction between "global" and "non-global" listeners
* JobDetails and Triggers are no longer configured with a list of names of listeners to notify, instead listeners identify which jobs/triggers they're interested in.
* Listeners are now assigned a set of Matcher instances - which provide matching rules for jobs/triggers they wish to receive events for.
* Listeners are now managed through a ListenerManager API, rather than directly with the Scheduler API.
					
**Other changes**
					
* The SchedulerException class and class hierarchy has been cleaned up.
* DateIntervalTrigger was renamed to CalendarIntervalTrigger (or more exactly the concrete class is now CalendarIntervalTriggerImpl).
* The notion (property) of "volatility" of jobs and triggers has been eliminated.
* New trigger misfire instruction MisfireInstruction.IgnoreMisfirePolicy lets a trigger be configured in such a way 
	that it is selectively ignored from all misfire handling. In other words, it will fire as soon as it can, with no special handling -
	a great option for improving performance particularly with setups that have lots of one-shot (non-repeating) triggers.
* Trigger's CompareTo() method now correctly relates to its Equals() method, in that it compares the trigger's key, rather than next fire time.
A new Comparator that sorts triggers according to fire time, priority and key was added as Trigger.TriggerTimeComparator.
					
## New Features
					
* Scheduler.Clear() method provides convenient (and dangerous!) way to remove all jobs, triggers and calendars from the scheduler.
* Scheduler.ScheduleJobs(IDictionary&lt;IJobDetail, IList&lt;ITrigger&gt;&gt; triggersAndJobs, boolean replace) method provides convenient bulk addition of jobs and triggers.
* Scheduler.UnscheduleJobs(IList&lt;TriggerKey&gt; triggerKeys) method provides convenient bulk unscheduling of jobs.
* Scheduler.DeleteJobs(IList&lt;JobKey&gt; jobKeys) method provides convenient bulk deletion of jobs (and related triggers).
* Scheduler.CheckExists(JobKey jobKey) and Scheduler.CheckExists(TriggerKey triggerKey) methods provides convenient way to determine uniqueness of job/trigger keys (as opposed to old have of having to retrieve the job/trigger by name and then check whether the result was null).
* AdoJobStore now allows one set of tables to be used by multiple distinct scheduler instances
* AdoJobStore is now capable of storing non-core Trigger implementations without using BLOB columns, through the use of the new TriggerPersistenceDelegate interface, which can (optionally) be implemented by implementers of custom Trigger types.
* Cron expressions now support the ability to specify an offset for "last day of month" and "last weekday of month" expressions. For examples: "L-3" (three days back from the last of the month) or "L-3W" (nearest weekday to the day three days back from the last day of the month).
* XML files containing scheduling data now have a way to specify trigger start times as offsets into the future from the time the file is processed (useful for triggers that need to begin firing some time after the application is launched/deployed).
	From schema: &lt;xs:element name="start-time-seconds-in-future" type="xs:nonNegativeInteger"/&gt;
* XML file schema now supports specifying the 'priority' property of triggers.
* Added DirectoryScanJob to core jobs that ship with Quartz, also added minimum age parameter to pre-existing FileScanJob.

## Miscellaneous
				
Various performance improvements, including (but not limited to):
					
* Ability to batch-acquire triggers that are ready to be fired, which can provide performance improvements for very busy schedulers
* Methods for batch addition/removal of jobs and triggers (see "New Features")
* Triggers have a new misfire instruction option, MisfireInstruction.IgnoreMisfirePolicy, which may be useful if you do not require misfire handling for your trigger(s), and want to take advantage of a performance gain
