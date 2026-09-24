---

title: 'More About Triggers'
---

# More About Triggers

There are several trigger types for different scheduling needs. They share the options below.

## Common Trigger Attributes

Every trigger type has a `TriggerKey` for its identity, plus these properties, set with the TriggerBuilder when you build the trigger:

* `JobKey`: the identity of the job to execute when the trigger fires.
* `StartTimeUtc`: when the trigger's schedule first takes effect, as a DateTimeOffset. Some trigger types fire at the start time; for others it only marks when the schedule starts being followed. A trigger stored in January with the schedule "every 5th day of the month" and `StartTimeUtc` set to April 1st first fires months later.
* `EndTimeUtc`: when the trigger's schedule stops. A trigger with the schedule "every 5th day of the month" and an end time of July 1st fires for the last time on June 5th.

The properties that need more explanation follow.

## Priority

When many triggers fire at the same time (or the thread pool has few worker threads), Quartz.NET may not have the threads to fire them all at once. Set a trigger's priority to decide which ones get the free threads first. If N triggers fire at the same time and only Z worker threads are free, the Z triggers with the highest priority run first.

* The default priority is 5.
* Any integer is allowed, positive or negative. A larger number is a higher priority: 7 beats 5.

::: tip
Priorities are only compared when triggers have the same fire time. A trigger scheduled to fire at 10:59 will always fire before one scheduled to fire at 11:00.
:::

::: tip
When a trigger's job needs recovery, the recovery is scheduled with the same priority as the original trigger.
:::

## Misfire Instructions

A misfire happens when a persistent trigger misses its firing time because the scheduler was shut down or the thread pool had no free thread. Each trigger type has its own misfire instructions. The default is a 'smart policy' instruction, whose behavior depends on the trigger type and configuration.

When the scheduler starts, it finds persistent triggers that have misfired and updates each according to its misfire instruction. Learn the misfire instructions of the trigger types you use; their API documentation explains them, and the lesson for each trigger type covers them.

## Execution Groups

A trigger can have an **execution group**: a tag describing the resource needs of its job (e.g. `"batch-jobs"`, `"high-cpu"`). Each scheduler node can limit how many threads a group may use concurrently, so resource-intensive jobs cannot starve other work.

Set it with `TriggerBuilder`:

```csharp
TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .WithExecutionGroup("batch-jobs")
    // ...
    .Build();
```

See the [Execution Groups tutorial](execution-groups.md) for configuration and usage. To control *which cluster node* runs a trigger, see the [Node Affinity tutorial](node-affinity.md).

## Calendars

A calendar (an `ICalendar` implementation) associated with a trigger excludes blocks of time from the trigger's schedule. Associate it when the trigger is stored in the scheduler. For instance, a trigger fires a job every weekday at 9:30 am, and a calendar excludes the business's holidays.

A calendar can be any serializable object implementing `ICalendar`:

```csharp
namespace Quartz
{
 public interface ICalendar
 {
  string Description { get; set; }

  ICalendar CalendarBase { set; get; }

  bool IsTimeIncluded(DateTimeOffset timeUtc);

  DateTime GetNextIncludedTimeUtc(DateTimeOffset timeUtc);

  ICalendar Clone();
 }
} 
```

Calendars can block out time as narrow as a millisecond, but you will usually block out whole days. `HolidayCalendar` does that.

Instantiate calendars and register them with the scheduler's `AddCalendar(..)` method. Fill a `HolidayCalendar` with the days to exclude using its `AddExcludedDate(DateTime date)` method. Several triggers can use the same calendar instance:

__Calendar Example__

```csharp
    HolidayCalendar cal = new HolidayCalendar();
    cal.AddExcludedDate(someDate);
    
    await sched.AddCalendar("myHolidays", cal, false);
    
 ITrigger t = TriggerBuilder.Create()
  .WithIdentity("myTrigger")
  .ForJob("myJob")
  .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(9, 30)) // execute job daily at 9:30
  .ModifiedByCalendar("myHolidays") // but not on holidays
  .Build();

 // .. schedule job with trigger

 ITrigger t2 = TriggerBuilder.Create()
  .WithIdentity("myTrigger2")
  .ForJob("myJob2")
  .WithSchedule(CronScheduleBuilder.DailyAtHourAndMinute(11, 30)) // execute job daily at 11:30
  .ModifiedByCalendar("myHolidays") // but not on holidays
  .Build();

	// Use H (hash) to spread triggers across time instead of a fixed schedule.
	// The trigger identity is used as the hash seed, so each trigger fires at a unique time.
	ITrigger t3 = TriggerBuilder.Create()
		.WithIdentity("myTrigger3")
		.ForJob("myJob3")
		.WithCronSchedule("0 H H(9-17) * * ?") // execute at a hash-derived time during business hours
		.ModifiedByCalendar("myHolidays")
		.Build();

    // .. schedule jobs with triggers
```

The triggers above fire daily, and skip any firing that falls in a period the calendar excludes. The next lessons cover building triggers in detail.

The Quartz.Impl.Calendar namespace has more `ICalendar` implementations.
