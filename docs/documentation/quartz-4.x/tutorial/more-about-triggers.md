---

title: 'More About Triggers'
---

# More About Triggers

Like jobs, triggers are relatively easy to work with, but do contain a variety of customizable options that you need to
be aware of and understand before you can make full use of Quartz.NET. Also, as noted earlier, there are different types of triggers,
that you can select to meet different scheduling needs.

## Common Trigger Attributes

Aside from the fact that all trigger types have `TriggerKey` properties for tracking their identities,
there are a number of other properties that are common to all trigger types. These common properties are set using the TriggerBuilder
when you are building the trigger definition (examples of that will follow).

Here is a listing of properties common to all trigger types:

* The `JobKey` property indicates the identity of the job that should be executed when the trigger fires.
* The `StartTimeUtc` property indicates when the trigger's schedule first comes into affect.
The value is a DateTimeOffset object that defines a moment in time on a given calendar date.
For some trigger types, the trigger will actually fire at the start time, for others it simply marks the time that the schedule should start being followed.
This means you can store a trigger with a schedule such as "every 5th day of the month" during January, and if the StartTimeUtc property is set to April 1st,
 it will be a few months before the first firing.
* The `EndTimeUtc` property indicates when the trigger's schedule should no longer be in effect.
In other words, a trigger with a schedule of "every 5th day of the month" and with an end time of July 1st will fire for it's last time on June 5th.

Other properties, which take a bit more explanation are discussed in the following sub-sections.

## Priority

Sometimes, when you have many Triggers (or few worker threads in your Quartz.NET thread pool), Quartz.NET may not have enough resources to immediately fire all
of the Triggers that are scheduled to fire at the same time. In this case, you may want to control which of your Triggers get first crack at the available Quartz.NET worker threads.
For this purpose, you can set the priority property on a Trigger. If N Triggers are to fire at the same time, but there are only Z worker threads currently available,
then the first Z Triggers with the highest priority will be executed first. If you do not set a priority on a Trigger, then it will use the default priority of 5.
Any integer value is allowed for priority, positive or negative.  A larger number indicates a higher priority.  i.e. A trigger with a Priority of 7 will have priority over trigger with a value of 5.

::: tip
Priorities are only compared when triggers have the same fire time. A trigger scheduled to fire at 10:59 will always fire before one scheduled to fire at 11:00.
:::

::: tip
When a trigger's job is detected to require recovery, its recovery is scheduled with the same priority as the original trigger.
:::

## Misfire Instructions

Another important property of a Trigger is its "misfire instruction". A misfire occurs if a persistent trigger "misses" its firing time because of the scheduler being shutdown,
or because there are no available threads in Quartz.NET's thread pool for executing the job.
When the scheduler starts, it searches for any persistent triggers that have misfired, and it then updates each of them based on their individually
configured misfire instructions.

Each trigger family has its own set of instructions, and each set is an enum of its own —
`SimpleTriggerMisfireInstruction`, `CronTriggerMisfireInstruction`, `CalendarIntervalTriggerMisfireInstruction`,
`DailyTimeIntervalTriggerMisfireInstruction` and `RecurrenceTriggerMisfireInstruction`. You set one on the
schedule builder for that family, so the only values in scope are the ones that family understands:

```csharp
.WithSimpleSchedule(x => x
    .WithInterval(TimeSpan.FromMinutes(5))
    .RepeatForever()
    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount))
```

Every family has `SmartPolicy`, which is the default, and `IgnoreMisfires`, which fires every missed firing as
fast as it can once the scheduler is back. `SmartPolicy` has dynamic behaviour chosen by the trigger type and its
configuration; what it resolves to is described in the lesson for each trigger type.

## Execution Groups

Triggers can optionally be assigned an **execution group** -- a tag that characterizes the resource
requirements of the associated job (e.g. `"batch-jobs"`, `"high-cpu"`). Execution groups allow each
scheduler node to limit how many threads a particular category of job may consume concurrently,
preventing resource-intensive jobs from starving other work.

Set an execution group via `TriggerBuilder`:

```csharp
TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .WithExecutionGroup("batch-jobs")
    // ...
    .Build();
```

See the [Execution Groups tutorial](execution-groups.md) for full details on configuration and usage.

To control *which cluster node* runs a trigger, rather than how many threads it may use, see the
[Node Affinity tutorial](node-affinity.md).

## Calendars

Quartz.NET Calendar objects implementing `ICalendar` interface can be associated with triggers at the time the trigger is stored in the scheduler.
Calendars are useful for excluding blocks of time from the trigger's firing schedule. For instance, you could
create a trigger that fires a job every weekday at 9:30 am, but then add a Calendar that excludes all of the business's holidays.

A calendar is any object implementing the `ICalendar` interface, which looks like this:

```csharp
namespace Quartz
{
    public interface ICalendar
    {
        string? Description { get; set; }

        ICalendar? CalendarBase { get; set; }

        bool IsTimeIncluded(DateTimeOffset timeUtc);

        DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc);

        ICalendar Clone();
    }
}
```

`CalendarBase` chains calendars: a calendar excludes a time if it excludes it itself *or* if its base does, so
"not on holidays, and not outside business hours" is two calendars, one based on the other.

A calendar of your own only has to survive whatever your job store does with it. `RAMJobStore` holds the instance
and hands back clones. A persistent store writes it as a serialized blob, so a calendar going into one has to be
something the configured serializer can read back: the calendars in `Quartz.Impl.Calendar` ship with serializers
for both JSON serializers, and a calendar of your own needs a `CalendarSerializer<T>` registered alongside it —
see [System.Text.Json serialization](../packages/system-text-json.md).

Even though calendars can 'block out' sections of time as narrow as a millisecond, most likely, you'll be interested in
'blocking-out' entire days. As a convenience, Quartz.NET includes the class HolidayCalendar, which does just that.

Calendars are registered with the scheduler under a name, and triggers refer to them by that name. If you use
`HolidayCalendar`, use its `AddExcludedDay(DateOnly day)` method to populate it with the days you wish to have
excluded from scheduling. The same calendar can be used by any number of triggers:

**Calendar Example**

```csharp
HolidayCalendar holidays = new();
holidays.AddExcludedDay(new DateOnly(2026, 12, 24));

await scheduler.AddCalendar("myHolidays", holidays);

ITrigger t = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob("myJob")
    .WithCronSchedule("0 30 9 ? * *")  // execute job daily at 9:30
    .WithCalendarName("myHolidays")    // but not on holidays
    .Build();

ITrigger t2 = TriggerBuilder.Create()
    .WithIdentity("myTrigger2")
    .ForJob("myJob2")
    .WithCronSchedule("0 30 11 ? * *") // execute job daily at 11:30
    .WithCalendarName("myHolidays")    // but not on holidays
    .Build();

// Use H (hash) to spread triggers across time instead of a fixed schedule.
// The trigger identity is used as the hash seed, so each trigger fires at a unique time.
ITrigger t3 = TriggerBuilder.Create()
    .WithIdentity("myTrigger3")
    .ForJob("myJob3")
    .WithCronSchedule("0 H H(9-17) * * ?") // a hash-derived time during business hours
    .WithCalendarName("myHolidays")
    .Build();

// .. schedule jobs with triggers
```

Any firing that would have occurred during a period the calendar excludes is skipped.

Re-registering a calendar under a name that is already taken is refused unless you say so, and saying so has
two parts, which is what `AddCalendarOptions` is for:

```csharp
await scheduler.AddCalendar("myHolidays", holidays, new AddCalendarOptions
{
    Replace = true,        // there is already a calendar under this name
    UpdateTriggers = true, // recompute the next fire time of every trigger using it
});
```

Without `UpdateTriggers`, triggers already scheduled against the old calendar keep the fire times they had
computed; the new exclusions only take effect the next time each trigger recomputes on its own.

Registering a calendar at configuration time rather than at run time is `q.AddCalendar<T>`:

```csharp
q.AddCalendar<HolidayCalendar>("myHolidays", new AddCalendarOptions { Replace = true }, calendar =>
{
    calendar.AddExcludedDay(new DateOnly(2026, 12, 24));
});
```

See the `Quartz.Impl.Calendar` namespace for a number of `ICalendar` implementations that may suit your needs:
`AnnualCalendar` (the same days every year), `CronCalendar`, `DailyCalendar` (a time range each day),
`HolidayCalendar`, `MonthlyCalendar` and `WeeklyCalendar`.
