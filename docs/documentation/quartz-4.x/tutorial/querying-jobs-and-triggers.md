---
title: 'Querying Jobs and Triggers'
---

To list what is scheduled, describe what you want in a query, ask for a page of it, and render the
headers that come back.

## Why listings became queries

In 3.x each listing member returned everything it found: no paging, keys only, and a separate call per
row for anything else. 4.x replaces them with six query members that work the same way:

| Query | Returns | Selects |
|---|---|---|
| `QueryJobs(JobQuery)` | `PagedResult<JobHeader>` | jobs |
| `QueryTriggers(TriggerQuery)` | `PagedResult<TriggerHeader>` | triggers |
| `QueryJobGroups(JobGroupQuery)` | `PagedResult<JobGroup>` | job groups |
| `QueryTriggerGroups(TriggerGroupQuery)` | `PagedResult<TriggerGroup>` | trigger groups |
| `QueryCalendarNames(CalendarQuery)` | `PagedResult<string>` | calendar names |
| `QueryFireInstances(FireInstanceQuery)` | `PagedResult<FireInstance>` | firings in flight |

The same six are on `IJobStore`, with the same shapes.

## Headers, not entities

A query returns *headers*: enough to render a row, and nothing that needs a second read.

| Header | Carries |
|---|---|
| `JobHeader` | `Key`, `Description`, `JobTypeName`, `Durable`, `ConcurrentExecutionDisallowed`, `PersistJobDataAfterExecution`, `RequestsRecovery` |
| `TriggerHeader` | `Key`, `JobKey`, `Description`, `TriggerType`, `State`, `StartTimeUtc`, `EndTimeUtc`, `NextFireTimeUtc`, `PreviousFireTimeUtc`, `CalendarName`, `Priority`, `ExecutionGroup` |

The trigger fields are explained in [More About Triggers](more-about-triggers.md).

* Neither header carries a `JobDataMap`: job data is a blob in a persistent store, and deserializing it
  per row would make every listing pay for it. Fetch the whole object when you need it; see
  [from a page to full detail](#from-a-page-to-full-detail).
* `TriggerHeader.State` is computed by the store in the same query. In 3.x you called `GetTriggerState`
  per key.

## Filtering

Every query is a record with init-only filter properties. A null filter matches everything; filters that
are set combine with **AND**:

<!-- snippet: sample_querying_trigger_query -->
```csharp
PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
{
    Group = GroupMatcher<TriggerKey>.GroupStartsWith("reporting-"),
    State = TriggerState.Error,
    Take = 50,
});
```
<!-- endSnippet -->

| Query | Filters |
|---|---|
| `JobQuery` | `Group` (`GroupMatcher<JobKey>`), `Name` (`NameMatcher<JobKey>`) |
| `TriggerQuery` | `Group`, `Name`, `Job` (`JobKey`), `CalendarName` (`string`), `State` (`TriggerState?`) |
| `JobGroupQuery` / `TriggerGroupQuery` | `Name` (`NameMatcher`), `Paused` (`bool?`) |
| `CalendarQuery` | `Name` (`NameMatcher`) |
| `FireInstanceQuery` | `TriggerGroup`, `TriggerName`, `Job`, `SchedulerInstanceId`, `State` |

`Group` and `Name` filter on the result's own identity. A filter on something the result refers to
carries that thing's name: `Job`, `CalendarName`, `SchedulerInstanceId`. A firing is identified by a fire
instance id, not a key, so `FireInstanceQuery` says `TriggerGroup` and `TriggerName`.

Name filters are matchers:

| Matcher | Methods | Matches |
|---|---|---|
| `GroupMatcher<TKey>` | `GroupEquals`, `GroupStartsWith`, `GroupEndsWith`, `GroupContains` | a key's group |
| `NameMatcher<TKey>` | `NameEquals`, `NameStartsWith`, `NameEndsWith`, `NameContains` | a key's name |
| `NameMatcher` | the same four | a name that belongs to no key: a calendar's, a group's |

* `GroupMatcher<TKey>.AnyGroup()` is for members that take a matcher and not a null, such as
  `PauseTriggerGroups`. In a query, leave the filter null.
* Matcher text is a literal, not a pattern. `GroupStartsWith("50%")` selects a group named `50%`; the
  store escapes the wildcard in SQL.

### Matchers as a vocabulary

`Matchers` builds matchers without spelling the generic argument, and holds the combinators:

<!-- snippet: sample_querying_combining_matchers -->
```csharp
IMatcher<JobKey> notArchived = Matchers.Group<JobKey>(StringOperator.StartsWith, "archive-").Not();
IMatcher<TriggerKey> either = Matchers.Key(triggerKey).Or(Matchers.AllTriggers());
```
<!-- endSnippet -->

* `Matchers.AllJobs()` and `Matchers.AllTriggers()` return `EverythingMatcher<TKey>`.
* `Matchers.Key(key)` matches one key exactly.
* `And`, `Or` and `Not` are extension methods on `IMatcher<TKey>`.

The combinators are for the *listener* matchers on `IListenerManager` and `IQuartzBuilder`, which run in
memory. Query filters only take `GroupMatcher<TKey>` and `NameMatcher<TKey>`, the two shapes a job store
can translate to SQL.

## Paging

Results are ordered by group, then name: ordinal in `RAMJobStore`, the database's collation in the ADO.NET
store. `Skip` and `Take` are offsets into that ordering, so page 3 is the same whichever node answers.

<!-- snippet: sample_querying_paging -->
```csharp
PagedResult<JobHeader> page = await scheduler.QueryJobs(new JobQuery
{
    Skip = (pageNumber - 1) * pageSize,
    Take = pageSize,
});
```
<!-- endSnippet -->

| Member | Value |
|---|---|
| `Take` default | `PagedQuery.DefaultTake`, **250** |
| `PagedQuery.All` | `int.MaxValue`; over HTTP, [`?take=all`](../packages/http-api.md#listing-endpoints-are-paged) |
| `PagedResult<T>.HasMore` | whether anything was left out; exact and effectively free (the stores read one row past `Take`) |
| `TotalCount` | `null` unless `IncludeTotalCount` is set; costs a second query on a persistent store |

Ask for everything explicitly:

<!-- snippet: sample_querying_everything -->
```csharp
JobQuery everything = new() { Take = PagedQuery.All };
```
<!-- endSnippet -->

Ask for a total:

<!-- snippet: sample_querying_total_count -->
```csharp
PagedResult<TriggerHeader> page = await scheduler.QueryTriggers(new TriggerQuery
{
    Take = pageSize,
    IncludeTotalCount = true,
});

int total = page.TotalCount!.Value;   // non-null because IncludeTotalCount was set
```
<!-- endSnippet -->

### Counting without rows

`GetNumberOfJobs`, `GetNumberOfTriggers` and `GetNumberOfCalendars` are gone. Count with a query that
asks for no rows:

<!-- snippet: sample_querying_count_only -->
```csharp
PagedResult<JobHeader> count = await scheduler.QueryJobs(new JobQuery
{
    Take = 0,
    IncludeTotalCount = true,
});

int jobCount = count.TotalCount!.Value;
```
<!-- endSnippet -->

`Take = 0` is valid and returns an empty `Items`; with `IncludeTotalCount` the stores run only the count
query. This counts anything a query can select: triggers in the error state, calendars whose name starts
with a prefix, firings on one node.

## From a page to full detail

To get the full objects for headers, fetch them by key in one round trip, not in a loop:

<!-- snippet: sample_querying_headers_to_details -->
```csharp
List<JobKey> keys = page.Items.Select(h => h.Key).ToList();
List<IJobDetail> details = await scheduler.GetJobDetails(keys);

List<ITrigger> triggers = await scheduler.GetTriggers(triggerKeys);
```
<!-- endSnippet -->

Keys that do not exist are absent from the result. A bulk fetch does not throw for a key deleted since
the listing ran, so it is not an existence check. Use `Exists(JobKey)` and `Exists(TriggerKey)`.

::: warning Changed in 4.x
`CheckExists` is now `Exists`, on both overloads.
:::

## Fire instances: what is running right now

`GetCurrentlyExecutingJobs()` is gone; it only described the answering node, returned whole
`IJobExecutionContext` objects, and had no filter or paging. `QueryFireInstances` replaces it. It is
store-backed, so on a persistent store it covers the whole cluster:

<!-- snippet: sample_querying_fire_instances -->
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
<!-- endSnippet -->

A `FireInstance` carries `FireInstanceId`, `TriggerKey`, `JobKey`, `SchedulerInstanceId`, `State`,
`FireTimeUtc`, `ScheduledFireTimeUtc` and `ExecutionGroup`.

* `State` is a `FireInstanceState`: `Acquired` or `Executing`. It is the only filter in the family with a
  **non-null default**: a query that says nothing about state lists executing firings. Set
  `State = null` to include firings a node has reserved but not started.
* `JobKey` is nullable because an `Acquired` firing has not resolved its job yet. A query filtered by
  `Job` never matches a reservation, so `Job` with `State = null` still lists executing firings only.
* Firings are ordered by trigger group, trigger name, then fire instance id, because one trigger can
  have several firings in flight.

<!-- snippet: sample_querying_fire_instance_state -->
```csharp
FireInstanceQuery reservedAndRunning = new() { State = null };
FireInstanceQuery reservedOnly = new() { State = FireInstanceState.Acquired };
```
<!-- endSnippet -->

### Three caveats for any UI built over this

* **A vetoed firing does not stay listed.** An `ITriggerListener` veto completes the firing. It is
  listed only between the store recording it and the veto, so a "running jobs" screen cannot count
  vetoes.
* **Elapsed time can be negative.** It is your clock minus `FireTimeUtc`, which the firing node's clock
  wrote. With skewed clocks on a cluster, clamp it.
* **`ScheduledFireTimeUtc` is not the missed time.** After a misfire it is the *rescheduled* time, as the
  owning node recorded it. Its gap to `FireTimeUtc` is not misfire lateness.

To stop a single firing, use `InterruptFireInstance(fireInstanceId)`. `Interrupt(jobKey)` stops every
execution of the job.

## Group pause state

The stores persist pause state for trigger groups and job groups, so `TriggerGroup.Paused` and
`JobGroup.Paused` are accurate on both stores.

| Question | Query |
|---|---|
| paused trigger groups (replaces `GetPausedTriggerGroups()`) | `QueryTriggerGroups(new TriggerGroupQuery { Paused = true })` |
| is one trigger group paused? | `new TriggerGroupQuery { Name = NameMatcher.NameEquals("reporting"), Take = 1 }` |
| paused job groups | `QueryJobGroups(new JobGroupQuery { Paused = true })` |
| is one job group paused? | `new JobGroupQuery { Name = NameMatcher.NameEquals("reporting"), Take = 1 }` |

The other comparisons list a tenant's or subsystem's groups: `NameMatcher.NameStartsWith("tenant-42-")`.

4.x records paused job groups in `QRTZ_PAUSED_JOB_GRPS` on both stores. On 3.x the ADO store could not
report them (`IsJobGroupPaused` answered `false` for every group), which is why the
[4.0 schema migration](../../database/schema-changes.md#version-4-0) is mandatory even for a database
that took every optional 3.x migration.

An empty group can be paused. `Paused = true` lists it; the unfiltered listing does not, because it
enumerates the groups jobs and triggers are in. The paused listing is the only place to find such a group
to resume it.

::: tip
A paused group binds what is added later, on both stores: a trigger stored into a paused trigger group,
or for a job in a paused job group, is stored paused.
:::

Pausing and resuming by matcher records the *group* as paused, which is what catches triggers added
later. So it is named for groups and returns their names. The key-set `PauseTriggers(keys)` returns the
keys it moved.

<!-- snippet: sample_querying_pause_triggers -->
```csharp
List<string> pausedGroups = await scheduler.PauseTriggerGroups(
    GroupMatcher<TriggerKey>.GroupStartsWith("nightly-"));
```
<!-- endSnippet -->

## A worked example: an admin list screen

Page size, a state filter, a total for the pager, and full detail only for the row that was opened:

<!-- snippet: sample_querying_trigger_list_model -->
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
<!-- endSnippet -->

Nothing here loops over keys, and nothing loads a `JobDataMap` the list does not show.

## The preset, and the mutation beside it

The `Query*` members take a record: a filter, a page, an optional count. The `Get*` conveniences answer
questions that need none of that. There is no overload that only saves the `new` in
`QueryJobs(new JobQuery())`.

`QueryTriggersInError()` is the one preset: it applies the filter for you, and pages like the member (the
first `PagedQuery.DefaultTake` items, with `HasMore` reporting the rest):

<!-- snippet: sample_querying_shorthands -->
```csharp
PagedResult<JobHeader> jobs = await scheduler.QueryJobs(new JobQuery());
PagedResult<TriggerHeader> triggers = await scheduler.QueryTriggers(new TriggerQuery());
PagedResult<FireInstance> running = await scheduler.QueryFireInstances(new FireInstanceQuery());
PagedResult<FireInstance> runningOneJob = await scheduler.QueryFireInstances(new FireInstanceQuery { Job = jobKey });

// the one shorthand that is a preset rather than a synonym: it knows the filter
PagedResult<TriggerHeader> failed = await scheduler.QueryTriggersInError();
```
<!-- endSnippet -->

`ResetTriggersFromErrorState(matcher)` on `IScheduler` resets the failed triggers of a group in one call:

<!-- snippet: sample_querying_reset_group_from_error -->
```csharp
List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(
    GroupMatcher<TriggerKey>.GroupEquals("imports"));
```
<!-- endSnippet -->

It is two calls underneath: a listing filtered by `State = TriggerState.Error` and the group, then
`ResetTriggersFromErrorState(keys)`. It is not atomic; a trigger that fails between them is left for the
next call. What resetting does is unchanged; see [Rescheduling Jobs](../how-tos/rescheduling-jobs.md).

`Exists(name)` asks the store whether a calendar is registered. `GetCalendar` would deserialize the
stored blob.

<!-- snippet: sample_querying_calendar_exists -->
```csharp
bool haveHolidays = await scheduler.Exists("holidays");
```
<!-- endSnippet -->

## The compatibility layer

`SchedulerQueryExtensions` restores eight 3.x call shapes as extension methods on `IScheduler`:

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

* Each **enumerates the entire result**: they pass `Take = PagedQuery.All`. Fine for group names, bad for
  a trigger listing on a busy scheduler. Where the result can be large, or the row needs state or fire
  times, use the query member.
* A null matcher throws `ArgumentNullException`. In 3.x `GetJobKeys(null)` silently listed only the
  `DEFAULT` group; pass `GroupMatcher<JobKey>.AnyGroup()` for every group.

## Notes for job store authors

The six query members are abstract on `IJobStore`, with no default. To match the shipped stores:

* Order by group then name, and add fire instance id as a third key for firings. Callers rely on a
  stable order for paging.
* Read one row past `Take` to set `HasMore`. Run the count query only when `IncludeTotalCount` is set.
  `Take = 0` with `IncludeTotalCount` must skip the row query.
* The bulk fetches are `GetJobs(keys)` and `GetTriggers(keys)` on the store; the scheduler's
  `GetJobDetails` is the same operation.

The [job store how-to](../how-tos/custom-job-store.md) covers the rest of the contract. The
[HTTP API](../packages/http-api.md) exposes the same queries over the wire.
