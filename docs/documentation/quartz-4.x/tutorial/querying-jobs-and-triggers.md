---
title: 'Querying Jobs and Triggers'
---

Sooner or later something has to answer "what is scheduled?" — an admin screen, a health endpoint, a
support script, a migration that has to find every trigger it broke. In 3.x that meant a handful of
listing members that each returned everything they could find. In 4.x it is one query family: you
describe what you want, ask for a page of it, and get back headers you can render.

## Why listings became queries

3.x answered every one of these questions with a listing member of its own, and every one of them
enumerated. `GetTriggerKeys` returned keys, so a UI that wanted a trigger's state fetched each trigger
separately — one round trip per row. `GetNumberOfJobs` existed because counting through `GetJobKeys`
meant loading every key. Nothing paged, so a scheduler with fifty thousand triggers had no safe way to
show the first fifty.

Six query members replace them, and they all work the same way:

| Query | Returns | Selects |
|---|---|---|
| `QueryJobs(JobQuery)` | `PagedResult<JobHeader>` | jobs |
| `QueryTriggers(TriggerQuery)` | `PagedResult<TriggerHeader>` | triggers |
| `QueryJobGroups(JobGroupQuery)` | `PagedResult<JobGroup>` | job groups |
| `QueryTriggerGroups(TriggerGroupQuery)` | `PagedResult<TriggerGroup>` | trigger groups |
| `QueryCalendarNames(CalendarQuery)` | `PagedResult<string>` | calendar names |
| `QueryFireInstances(FireInstanceQuery)` | `PagedResult<FireInstance>` | firings in flight |

The same six are on `IJobStore`, with the same shapes, so a custom store implements the listing story
once.

## Headers, not entities

A listing hands back a *header*: enough to render a row, and nothing that costs a second read.

`JobHeader` carries `Key`, `Description`, `JobTypeName`, `Durable`,
`ConcurrentExecutionDisallowed`, `PersistJobDataAfterExecution` and `RequestsRecovery`.

`TriggerHeader` carries `Key`, `JobKey`, `Description`, `TriggerType`, `State`, `StartTimeUtc`,
`EndTimeUtc`, `NextFireTimeUtc`, `PreviousFireTimeUtc`, `CalendarName`, `Priority` and
`ExecutionGroup`.

Neither carries a `JobDataMap`. Job data is a blob in the persistent store, and deserializing one per
row would make every listing pay for data no list screen shows. When you need the whole object, fetch
it — see [from a page to full detail](#from-a-page-to-full-detail) below.

`TriggerHeader.State` is the one to notice: in 3.x a trigger listing gave you keys and you called
`GetTriggerState` per key to colour the rows. The state is in the row now, computed by the store as
part of the same query.

## Filtering

Every query is a record with init-only filter properties. A null filter matches everything, and the
filters that are set combine with **AND**:

```csharp
PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
{
    Group = GroupMatcher<TriggerKey>.GroupStartsWith("reporting-"),
    State = TriggerState.Error,
    Take = 50,
});
```

| Query | Filters |
|---|---|
| `JobQuery` | `Group` (`GroupMatcher<JobKey>`), `Name` (`NameMatcher<JobKey>`) |
| `TriggerQuery` | `Group`, `Name`, `Job` (`JobKey`), `CalendarName` (`string`), `State` (`TriggerState?`) |
| `JobGroupQuery` / `TriggerGroupQuery` | `Name` (one group, matched exactly), `Paused` (`bool?`) |
| `CalendarQuery` | `Name` (`CalendarNameMatcher`) |
| `FireInstanceQuery` | `TriggerGroup`, `TriggerName`, `Job`, `SchedulerInstanceId`, `State` |

`GroupMatcher<TKey>` and `NameMatcher<TKey>` have the four shapes you would expect —
`GroupEquals`/`GroupStartsWith`/`GroupEndsWith`/`GroupContains` and the `Name*` counterparts — plus
`AnyGroup()` / `AnyName()`, which mean the same thing as leaving the filter null. `CalendarNameMatcher`
is the same idea for calendar names, which are not keys.

The matcher text is a literal, not a pattern: a group named `50%` is selected by
`GroupStartsWith("50%")`, and the store escapes the wildcard on its way into SQL.

### Matchers as a vocabulary

`Matchers` is the entry point when you would rather not spell the generic argument, and it is where
the combinators live:

```csharp
IMatcher<JobKey> notArchived = Matchers.Group<JobKey>(StringOperator.StartsWith, "archive-").Not();
IMatcher<TriggerKey> either = Matchers.Key(triggerKey).Or(Matchers.AllTriggers());
```

`Matchers.AllJobs()` and `Matchers.AllTriggers()` return `EverythingMatcher<TKey>`, `Matchers.Key(key)`
matches one key exactly, and `And`, `Or` and `Not` are extension methods on `IMatcher<TKey>`.

The combinators are for the *listener* matchers on `IListenerManager` and `IQuartzBuilder`, which
evaluate in memory. The query filters are typed to `GroupMatcher<TKey>` and `NameMatcher<TKey>`
specifically, because those are the two shapes a job store can translate to SQL.

## Paging

Results are ordered by group and then name, ordinal, on every store. That is what makes a page
deterministic: `Skip` and `Take` are offsets into one stable ordering, so page 3 is page 3 whichever
node answers.

```csharp
PagedResult<JobHeader> page = await scheduler.QueryJobs(new JobQuery
{
    Skip = (pageNumber - 1) * pageSize,
    Take = pageSize,
});
```

`Take` defaults to `PagedQuery.DefaultTake`, which is **250**. An unpaged call therefore cannot
accidentally materialize a hundred thousand rows; `PagedResult<T>.HasMore` tells you whether anything
was left out. Ask for everything explicitly:

```csharp
JobQuery everything = new() { Take = int.MaxValue };
```

::: warning Changed in 4.x
In the 4.0 previews `Take` defaulted to `int.MaxValue`. Code that built a query without setting `Take`
and expected the whole result now gets the first 250 items with `HasMore = true`. Set
`Take = int.MaxValue` where you meant everything.
:::

`HasMore` is exact and effectively free — the stores read one row past `Take` to answer it.
`TotalCount` is `null` unless you ask for it, because on a persistent store it costs a second query:

```csharp
PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
{
    Take = pageSize,
    IncludeTotalCount = true,
});

int total = page.TotalCount!.Value;   // non-null because IncludeTotalCount was set
```

### Counting without rows

`GetNumberOfJobs`, `GetNumberOfTriggers` and `GetNumberOfCalendars` are gone. A count is a query that
asks for no rows:

```csharp
PagedResult<JobHeader> count = await scheduler.QueryJobs(new JobQuery
{
    Take = 0,
    IncludeTotalCount = true,
});

int jobCount = count.TotalCount!.Value;
```

`Take = 0` is valid and returns an empty `Items`; the stores recognize the combination and run the
count query alone. The same idiom counts anything the family can select — triggers in the error state,
calendars whose name starts with a prefix, firings on one node.

## From a page to full detail

A listing gives you headers. When the user opens a row, or a script needs the actual objects, fetch
them by key in one round trip rather than in a loop:

```csharp
List<JobKey> keys = page.Items.Select(h => h.Key).ToList();
List<IJobDetail> details = await scheduler.GetJobDetails(keys);

List<ITrigger> triggers = await scheduler.GetTriggers(triggerKeys);
```

Keys that do not exist are simply absent from the result — a bulk fetch is not an existence check, and
it does not throw for a key that has been deleted since the listing ran. `Exists(JobKey)` and
`Exists(TriggerKey)` answer that question directly.

::: warning Changed in 4.x
`CheckExists` is now `Exists`, on both overloads.
:::

## Fire instances: what is running right now

`GetCurrentlyExecutingJobs()` is gone. It could only ever describe the node that answered, it returned
whole `IJobExecutionContext` objects, and it had no filter and no paging. `QueryFireInstances` replaces
it, and because it is store-backed it covers the whole cluster on a persistent store:

```csharp
PagedResult<FireInstance> running = await scheduler.QueryFireInstances(new FireInstanceQuery
{
    TriggerGroup = GroupMatcher<TriggerKey>.GroupEquals("reporting"),
});

foreach (FireInstance fire in running.Items)
{
    Console.WriteLine($"{fire.TriggerKey} on {fire.SchedulerInstanceId} since {fire.FireTimeUtc:O}");
}
```

A `FireInstance` carries `FireInstanceId`, `TriggerKey`, `JobKey`, `SchedulerInstanceId`, `State`,
`FireTimeUtc`, `ScheduledFireTimeUtc` and `ExecutionGroup`.

`State` is a `FireInstanceState` — `Acquired` or `Executing` — and it is the one filter in the whole
family with a **non-null default**. A `FireInstanceQuery` that says nothing about state lists what is
running, because that is the question the query is usually asked. Set `State = null` to include
firings that a node has reserved but not yet started:

```csharp
FireInstanceQuery reservedAndRunning = new() { State = null };
FireInstanceQuery reservedOnly = new() { State = FireInstanceState.Acquired };
```

`JobKey` is nullable for the same reason: an `Acquired` firing has not resolved its job yet. That also
means a query filtered by `Job` never matches a reservation, so combining `Job` with `State = null`
still lists executing firings only.

Ordering adds a third key here. Group and name do not order a page deterministically when one trigger
has several firings in flight, so firings are ordered by trigger group, then trigger name, then fire
instance id.

### Three caveats for any UI built over this

- **A vetoed firing does not linger.** Applying an `ITriggerListener` veto completes the firing. It can
  be listed for the instant between the store recording it and the veto being decided, and never after
  — so a "running jobs" screen cannot be used to count vetoes.
- **Elapsed time can come out negative.** It is your clock minus `FireTimeUtc`, and `FireTimeUtc` was
  written by the firing node's clock. On a cluster with skewed clocks the difference can be below zero;
  clamp it.
- **`ScheduledFireTimeUtc` is not the missed time.** It is the schedule as the owning node recorded it,
  which after a misfire is the *rescheduled* time. The gap between it and `FireTimeUtc` is not misfire
  lateness.

To stop one of them, `InterruptFireInstance(fireInstanceId)` targets a single firing where
`Interrupt(jobKey)` stops every execution of that job.

## Group pause state

`TriggerGroup.Paused` is real: the stores persist trigger group pause state, so
`QueryTriggerGroups(new TriggerGroupQuery { Paused = true })` is the replacement for
`GetPausedTriggerGroups()`, and `new TriggerGroupQuery { Name = "reporting", Take = 1 }` answers "is
this one group paused?" without listing the rest.

`JobGroup.Paused` works the same way, and on both stores: 4.x records paused job groups in
`QRTZ_PAUSED_JOB_GRPS`, so `QueryJobGroups(new JobGroupQuery { Paused = true })` is a real listing and
`new JobGroupQuery { Name = "reporting", Take = 1 }` answers for one group. On 3.x this was the one
thing the ADO store could not report — `IsJobGroupPaused` answered `false` for every group there —
which is why the [4.0 schema migration](../../database/schema-changes.md#version-4-0) is mandatory even
for a database that took every optional 3.x migration.

A group can be paused while it holds nothing. `Paused = true` reports such a group; the unfiltered
listing does not, because it enumerates the groups jobs and triggers are actually in. Pausing an empty
group is how you pause what is about to be added to it.

Pausing and resuming by matcher tells you which groups it touched:

```csharp
List<string> pausedGroups = await scheduler.PauseTriggers(
    GroupMatcher<TriggerKey>.GroupStartsWith("nightly-"));
```

## A worked example: an admin list screen

Page size, a state filter, a total for the pager, and full detail only for the row that was opened:

```csharp
public sealed class TriggerListModel(IScheduler scheduler)
{
    public async Task<(IReadOnlyList<TriggerHeader> Rows, int Total)> GetPage(
        int pageNumber,
        int pageSize,
        TriggerState? state,
        string? groupPrefix,
        CancellationToken cancellationToken)
    {
        TriggerQuery query = new()
        {
            Skip = (pageNumber - 1) * pageSize,
            Take = pageSize,
            IncludeTotalCount = true,
            State = state,
            Group = groupPrefix is null
                ? null
                : GroupMatcher<TriggerKey>.GroupStartsWith(groupPrefix),
        };

        PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(query, cancellationToken);
        return (page.Items, page.TotalCount ?? page.Items.Count);
    }

    public ValueTask<List<ITrigger>> Expand(
        IReadOnlyCollection<TriggerKey> keys,
        CancellationToken cancellationToken) =>
        scheduler.GetTriggers(keys, cancellationToken);
}
```

Nothing here loops over keys, and nothing loads a `JobDataMap` the list does not show.

## The compatibility layer

The old call shapes still compile. `SchedulerQueryExtensions` puts eight of them back as extension
methods on `IScheduler`:

| Extension | Built on |
|---|---|
| `GetJobKeys(matcher)` | `QueryJobs` |
| `GetTriggerKeys(matcher)` | `QueryTriggers` |
| `GetTriggersOfJob(jobKey)` | `QueryTriggers` + `GetTriggers` |
| `GetJobGroupNames()` | `QueryJobGroups` |
| `GetTriggerGroupNames()` | `QueryTriggerGroups` |
| `GetPausedTriggerGroups()` | `QueryTriggerGroups` with `Paused = true` |
| `GetCalendarNames()` | `QueryCalendarNames` |
| `IsJobGroupPaused(name)` / `IsTriggerGroupPaused(name)` | the group listings |

They exist so a 3.x port compiles, and each one **enumerates the entire result** — they pass
`Take = int.MaxValue` deliberately. That is fine for a group-name list and a bad idea for a trigger
listing on a busy scheduler. Treat them as a migration aid: anywhere the result can be large, or where
the row needs state or fire times, move to the query member.

One behavioral difference to know about: a null matcher now throws `ArgumentNullException`. In 3.x
`GetJobKeys(null)` quietly meant "everything"; pass `GroupMatcher<JobKey>.AnyGroup()` when that is what
you meant.

## Notes for job store authors

The six query members are abstract on `IJobStore`, so a custom store implements them rather than
inheriting a default. Three rules keep a store consistent with the shipped ones:

- Order by group then name, ordinal, and add fire instance id as a third key for firings. Paging is
  meaningless without a total order, and callers rely on this one.
- Read one row past `Take` to set `HasMore`, and only run the count query when `IncludeTotalCount` is
  set. `Take = 0` with `IncludeTotalCount` must skip the row query entirely.
- The bulk fetches are `GetJobs(keys)` and `GetTriggers(keys)` on the store — the scheduler's
  `GetJobDetails` is the same operation under the scheduler's vocabulary.

The [job store how-to](../how-tos/custom-job-store.md) covers the rest of the contract.

## See also

- [More About Triggers](more-about-triggers.md) — what the header fields mean
- [Rescheduling Jobs](../how-tos/rescheduling-jobs.md) — finding triggers in the error state and fixing them
- [HTTP API](../packages/http-api.md) — the same queries over the wire
