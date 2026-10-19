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

## Time Zones

Cron, calendar-interval, daily time-interval and recurrence triggers read their schedule in a time zone. A
trigger given none reads as `TimeZoneInfo.Local`: the developer's machine in development, and often UTC in
a container. Name it with `InTimeZone(…)` on the trigger, or once for the scheduler.

### The scheduler's default time zone

A trigger whose zone nobody stated takes the scheduler's `DefaultTimeZone` when it is scheduled (4.7):

<!-- snippet: sample_more_about_triggers_default_time_zone -->
```csharp
// A cron, calendar-interval, daily or recurrence trigger that names no zone reads its schedule in UTC,
// whatever zone the machine is in.
services.AddQuartz(q => q.UseDefaultTimeZone(TimeZoneInfo.Utc));
```
<!-- endSnippet -->

* Default `null`: `TimeZoneInfo.Local`, as before 4.7.
* **Stated wins, `TimeZoneInfo.Local` included.** `InTimeZone(…)`, a `CronExpression` built with a zone,
  an attribute's or the JSON schedule's `TimeZone`, the XML file's `<time-zone>`, and a trigger read from
  a store or deserialized all state it. `InTimeZone(null)` states none.
* Resolved by `ScheduleJob`, `ScheduleJobs`, `ScheduleTrigger` and `RescheduleJob`, before the first fire
  time is computed, so the store holds the zone. Triggers already stored keep theirs. A trigger scheduled
  over the HTTP API arrives deserialized, so it keeps the zone the client gave it.
* Simple triggers and `TriggerJob`'s one-shot triggers have no zone. A `CronExpression` used on its own
  reads its own.
* A daily time-interval schedule's `EndingDailyAfterCount` works out the window's end when the trigger is
  built, in its `InTimeZone` zone or else the machine's: the default does not reach it. The two give the
  same end except on a day either zone changes its clocks. Name the zone with `InTimeZone` if that day
  matters.
* Configuration: `Scheduler:DefaultTimeZone` or the flat key `quartz.scheduler.defaultTimeZone`, an IANA
  or Windows id; an id the system cannot resolve stops the scheduler starting. Each node applies its own,
  so set the same on every node.

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

* `SmartPolicy`, the default unless the scheduler's
  [default instruction](#the-scheduler-s-default-instruction) says otherwise. The trigger resolves it to
  one of its own instructions when the misfire is handled; only `SimpleTrigger` looks at anything to decide.
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
forward and is not replayed. Only `IgnoreMisfires` replays them. To run a past range you choose, whatever
the instruction did, see [Backfill](../how-tos/backfill.md).

### A trigger's own misfire threshold

A firing later than the misfire threshold is a misfire. The threshold is the store's `MisfireThreshold`
(one minute persistent, five seconds in memory) unless the trigger has one of its own (4.6):

<!-- snippet: sample_more_about_triggers_misfire_threshold -->
```csharp
// Behind a quota, this report may start up to fifteen minutes late and still be worth running.
// Every other trigger keeps the store's threshold.
ITrigger report = TriggerBuilder.Create()
    .WithIdentity("nightly-report")
    .ForJob("report")
    .WithCronSchedule("0 0 2 * * ?")
    .WithMisfireThreshold(TimeSpan.FromMinutes(15))
    .Build();
```
<!-- endSnippet -->

* Shorter or longer than the store's. Positive, in whole milliseconds.
* Both stores apply it everywhere they decide a misfire: acquisition, the misfire scan, a trigger let go
  from `BLOCKED`, a resumed trigger, a replaced calendar.
* Set it for work that waits behind a [quota](execution-groups.md#configuring-execution-limits) or
  [`BufferOne`](../how-tos/overlap-policy.md), instead of raising the store's threshold for every trigger.
* Also on [`[CronTrigger]` and `[SimpleTrigger]`](declaring-jobs-with-attributes.md), the
  [JSON schedule](../configuration/json.md), the XML file's `<misfire-threshold>`,
  `TriggerDetailsUpdate.WithMisfireThreshold` and the dashboard's *Edit details*. `null` in an update puts
  the trigger back on the store's.
* **Cluster:** stored in `QRTZ_TRIGGERS.MISFIRE_THRESHOLD`. A 4.5 node ignores it and judges every trigger
  by the store's threshold, so roll every node to 4.6 before you rely on it. See
  [Version 4.6](../../database/schema-changes.md#version-4-6).

### The scheduler's default instruction

A trigger whose instruction nobody stated takes the scheduler's `DefaultMisfireHandling` when it is
scheduled (4.6):

<!-- snippet: sample_more_about_triggers_default_misfire_handling -->
```csharp
// A trigger nobody gave an instruction skips what it missed and waits for its next time.
services.AddQuartz(q => q.ConfigureScheduler(options =>
    options.DefaultMisfireHandling = Quartz.DefaultMisfireHandling.DoNothing));
```
<!-- endSnippet -->

| `DefaultMisfireHandling` | Simple trigger | Cron, calendar interval, daily time interval, recurrence |
|---|---|---|
| `SmartPolicy` (default) | `SmartPolicy` | `SmartPolicy` |
| `IgnoreMisfires` | `IgnoreMisfires` | `IgnoreMisfires` |
| `FireAndProceed` | `FireNow` | `FireAndProceed` |
| `DoNothing` | `NextWithRemainingCount` | `DoNothing` |

* A simple trigger has no exact match for the last two. `FireNow` fires once now and re-anchors a
  repeating trigger's remaining repeats there. `NextWithRemainingCount` counts the missed firings as
  fired, so the series ends where it would have; a one-shot trigger that misfires never fires.
* A trigger type of your own keeps its own smart policy.
* **Stated wins, `SmartPolicy` included.** A `WithMisfireInstruction(…)` call, an assignment of
  `MisfireInstructionCode`, an attribute's or the JSON schedule's `MisfireInstruction`, and a trigger read
  from a store or deserialized all state it. Only an instruction left unset takes the default.
* Resolved by `ScheduleJob`, `ScheduleJobs`, `ScheduleTrigger` and `RescheduleJob`, so the store holds the
  mapped instruction. Triggers already stored keep theirs. A trigger scheduled over the HTTP API arrives
  deserialized, so it keeps the instruction it was sent with.
* Flat key: `quartz.scheduler.defaultMisfireHandling`. Each node applies its own, so set the same on every
  node.

## Retry Policies

A retry policy re-fires a trigger after its job throws, on a fixed, exponential or explicit table of
waits. (A misfire is a firing that never happened; a retry follows a firing that failed.)

```csharp
.WithRetryPolicy(RetryPolicy.Exponential(maxAttempts: 3, initialDelay: TimeSpan.FromSeconds(30)))
```

* A trigger with no policy of its own inherits its job type's `[RetryPolicy]` or the scheduler's
  `UseDefaultRetryPolicy`. `RetryPolicy.None` refuses both.
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

## Overlap Policies

From 4.3, a trigger says what happens when one of its firings comes due while its last one still runs:

```csharp
.WithOverlapPolicy(OverlapPolicy.Skip) // or BufferOne, CancelPrevious, AllowAll
```

* `Default` starts it beside the running one, as every trigger did before 4.3.
* `[DisallowConcurrentExecution]` on the job still wins, whatever the policy.

See [Overlap Policy](../how-tos/overlap-policy.md).

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

## Upcoming fire times

From 4.7, `GetUpcomingFireTimes` answers what a scheduled trigger fires next:

<!-- snippet: sample_more_about_triggers_upcoming_fire_times -->
```csharp
// The next ten fire times, in UTC; null when there is no such trigger.
List<DateTimeOffset>? fireTimes = await scheduler.GetUpcomingFireTimes(new TriggerKey("myTrigger"), count: 10);
```
<!-- endSnippet -->

* It works for every trigger type. The list starts at the trigger's next fire time and follows its schedule
  in the trigger's own time zone, skipping what its calendar excludes and stopping at its end time.
* `count` is 1 to 100. The list is shorter when the schedule ends sooner, and empty for a trigger that does
  not fire again.
* A paused trigger answers the times it would fire once resumed, before misfire handling.
* On an `HttpScheduler` it is one request; the host computes the times. See
  [Upcoming fire times](../packages/http-api.md#upcoming-fire-times) in the HTTP API.
* For a trigger you have not scheduled, use [`TriggerFireTimes`](testing.md#level-0-schedules-with-no-scheduler).
