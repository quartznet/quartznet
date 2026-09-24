---

title: 'More About Triggers'
---

# More About Triggers

Options common to every trigger type. The types themselves have lessons of their own.

## Common Trigger Attributes

Every trigger has a `TriggerKey` identity and these properties, set with `TriggerBuilder`:

| Property | Meaning |
|---|---|
| `JobKey` | the job to execute when the trigger fires |
| `StartTimeUtc` | when the schedule comes into effect, as a `DateTimeOffset` |
| `EndTimeUtc` | when the schedule stops being in effect |

* For some trigger types the trigger fires at the start time; for others the start time only marks when
  the schedule begins. A trigger stored in January with the schedule "every 5th day of the month" and a
  `StartTimeUtc` of April 1st first fires months later.
* The end time is the last instant at which the trigger may fire. A fire time exactly equal to it fires;
  the schedule stops at the first instant after it. With "every 5th day of the month" and an end time of
  July 1st, the last firing is June 5th. The rule is the same for every trigger type.

## Priority

When more triggers are due at once than there are free worker threads, priority decides which fire
first. If N triggers are due and Z threads are free, the Z triggers with the highest priority run first.

* Default: 5.
* Any integer, positive or negative. Larger is higher: 7 beats 5.

::: tip
Priorities are only compared when triggers have the same fire time. A trigger scheduled to fire at 10:59 will always fire before one scheduled to fire at 11:00.
:::

::: tip
When a trigger's job is detected to require recovery, its recovery is scheduled with the same priority as the original trigger.
:::

## Misfire Instructions

A misfire occurs when a persistent trigger misses its firing time because the scheduler was shut down or
the thread pool had no free thread. When the scheduler starts, it finds misfired persistent triggers and
updates each by its own misfire instruction.

Each trigger family has its own enum of instructions: `SimpleTriggerMisfireInstruction`,
`CronTriggerMisfireInstruction`, `CalendarIntervalTriggerMisfireInstruction`,
`DailyTimeIntervalTriggerMisfireInstruction` and `RecurrenceTriggerMisfireInstruction`. Set it on that
family's schedule builder, so only its values are in scope:

<!-- snippet: sample_more_about_triggers_misfire_instruction -->
```csharp
.WithSimpleSchedule(x => x
    .WithInterval(TimeSpan.FromMinutes(5))
    .RepeatForever()
    .WithMisfireInstruction(SimpleTriggerMisfireInstruction.NextWithRemainingCount))
```
<!-- endSnippet -->

Every family has:

* `SmartPolicy`, the default. The trigger resolves it to one of its own instructions when the misfire is
  handled; only `SimpleTrigger` looks at anything to decide.
* `IgnoreMisfires`, which fires every missed firing as fast as it can once the scheduler is back.

| Trigger family | `SmartPolicy` resolves to | Which means |
|---|---|---|
| **Simple**, `RepeatCount = 0` | `SimpleTriggerMisfireInstruction.FireNow` | fire the missed occurrence immediately |
| **Simple**, repeating forever | `SimpleTriggerMisfireInstruction.NextWithRemainingCount` | skip what was missed and wait for the next scheduled time |
| **Simple**, a finite repeat count | `SimpleTriggerMisfireInstruction.NowWithExistingCount` | fire now and keep the remaining count, so the series runs to its full length |
| **Cron** | `CronTriggerMisfireInstruction.FireAndProceed` | fire once now, then continue the schedule |
| **Calendar interval** | `CalendarIntervalTriggerMisfireInstruction.FireAndProceed` | the same |
| **Daily time interval** | `DailyTimeIntervalTriggerMisfireInstruction.FireAndProceed` | the same |
| **Recurrence (RRULE)** | `RecurrenceTriggerMisfireInstruction.FireAndProceed` | the same |

However many firings were missed, a resolved instruction fires **once** at most: the trigger moves
forward and is not replayed. Only `IgnoreMisfires` replays them.

## Retry Policies

A retry policy re-fires a trigger after its job throws, on a fixed, exponential or explicit table of
waits. (A misfire is a firing that never happened; a retry follows a firing that failed.)

```csharp
.WithRetryPolicy(RetryPolicy.Exponential(maxAttempts: 3, initialDelay: TimeSpan.FromSeconds(30)))
```

* A retry never displaces the trigger's next scheduled occurrence and uses no repeat count.
* When attempts run out, the trigger returns to its ordinary schedule, not to an error state.

See [Retrying Failed Jobs](../how-tos/retrying-failed-jobs.md), including why `RefireImmediately` is not
a zero-delay retry.

## Continuations

`StartAfter` makes a trigger start after another trigger's firing instead of at a time. The trigger is
stored in `TriggerState.Awaiting` and nothing acquires it. Depending on how the parent's firing ended,
its completion releases the trigger into the ordinary schedule or discards it.

<!-- snippet: sample_continuations_tutorial -->
```csharp
public sealed class TutorialContinuation
{
    public async ValueTask Schedule(IScheduler scheduler, CancellationToken cancellationToken)
    {
        // The trigger is ordinary in every way except when it fires: it is stored in
        // TriggerState.Awaiting, nothing acquires it, and the import's completion settles it.
        ITrigger reconcile = TriggerBuilder.Create<ReconcileJob>(scheduler.TimeProvider)
            .WithIdentity("reconcile", "nightly")
            .ForJob("reconcile", "nightly")
            .StartAfter(new TriggerKey("import", "nightly"), ContinuationCondition.OnSuccess)
            .Build();

        await scheduler.ScheduleJob(reconcile, cancellationToken: cancellationToken);
    }
}
```
<!-- endSnippet -->

* The condition is flags: `OnSuccess`, `OnFailure`, `OnCancellation`, `OnVeto`, or `OnAnyOutcome` for all
  four.
* It composes with the trigger's schedule: `StartAfter` plus `WithCronSchedule` is "start this cron once
  the import has finished".
* The store holds the wait, so it survives a restart. The node that releases or discards it need not be
  the node that scheduled it.
* A continuation is released or discarded **once**. For a recurring conditional chain, such as "run the
  cleanup whenever the nightly job fails", use `JobChainingJobListener`.

See [Job Continuations](../how-tos/job-continuations.md), including what a deleted parent does and why a
retry releases nothing.

## Execution Groups

An **execution group** is an optional tag on a trigger that names the job's resource needs (e.g.
`"batch-jobs"`, `"high-cpu"`). Each scheduler node can limit how many threads a group uses at once, so
resource-heavy jobs do not starve other work.

<!-- snippet: sample_more_about_triggers_execution_group -->
```csharp
TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .WithExecutionGroup("batch-jobs")
    // ...
    .Build();
```
<!-- endSnippet -->

See [Execution Groups](execution-groups.md). To control *which cluster node* runs a trigger, see
[Node Affinity](node-affinity.md).

## Calendars

A calendar excludes blocks of time from a trigger's schedule: for example, a trigger that fires every
weekday at 9:30 am, with a calendar that excludes the business's holidays. Calendars are associated with
triggers when the trigger is stored. A calendar is any object implementing `ICalendar`:

<!-- Quartz's own declaration of the interface, so it is written out here rather than compiled from the
     samples project: a second `Quartz.ICalendar` in that project would shadow the real one. -->

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

* `CalendarBase` chains calendars: a time is excluded if the calendar or its base excludes it. "Not on
  holidays and not outside business hours" is two calendars, one based on the other.
* `RAMJobStore` holds your calendar instance and returns clones.
* A persistent store writes the calendar as a serialized blob, so the configured serializer must be able
  to read it back. The calendars in `Quartz.Impl.Calendar` ship with serializers for both JSON serializers;
  a calendar of your own needs a `CalendarSerializer<T>` registered with it. See
  [System.Text.Json serialization](../packages/system-text-json.md).

Calendars can exclude time as narrow as a millisecond, but usually exclude whole days, which
`HolidayCalendar` does. Fill it with `AddExcludedDay(DateOnly day)`.

Calendars are registered with the scheduler under a name, and triggers refer to them by that name. Any
number of triggers can use one calendar:

<!-- snippet: sample_more_about_triggers_calendar -->
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
<!-- endSnippet -->

A firing that falls in an excluded period is skipped.

Registering a calendar under a name that is already taken is refused unless you set both parts of
`AddCalendarOptions`:

<!-- snippet: sample_more_about_triggers_replace_calendar -->
```csharp
await scheduler.AddCalendar("myHolidays", holidays, new AddCalendarOptions
{
    Replace = true,        // there is already a calendar under this name
    UpdateTriggers = true, // recompute the next fire time of every trigger using it
});
```
<!-- endSnippet -->

Without `UpdateTriggers`, triggers already scheduled against the old calendar keep their computed fire
times; the new exclusions apply the next time each trigger recomputes on its own.

To register a calendar at configuration time, use `q.AddCalendar<T>`:

<!-- snippet: sample_more_about_triggers_add_calendar_at_configuration_time -->
```csharp
q.AddCalendar<HolidayCalendar>("myHolidays", new AddCalendarOptions { Replace = true }, calendar =>
{
    calendar.AddExcludedDay(new DateOnly(2026, 12, 24));
});
```
<!-- endSnippet -->

Other `ICalendar` implementations in the `Quartz.Impl.Calendar` namespace: `AnnualCalendar` (the same
days every year), `CronCalendar`, `DailyCalendar` (a time range each day), `HolidayCalendar`,
`MonthlyCalendar` and `WeeklyCalendar`.
