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

A filter is named for what it selects on: `Group` and `Name` are the result's own identity, and a
filter on something the result merely refers to carries that thing's name — `Job`, `CalendarName`,
`SchedulerInstanceId`. That is why `FireInstanceQuery` alone says `TriggerGroup` and `TriggerName`: a
firing is identified by a fire instance id rather than by a key, so the trigger it belongs to is a
reference like any other, and an unqualified `Name` would leave you guessing whether it meant the
trigger's or the job's.

Every name filter is a matcher of one family. `GroupMatcher<TKey>` and `NameMatcher<TKey>` have the
four shapes you would expect — `GroupEquals`/`GroupStartsWith`/`GroupEndsWith`/`GroupContains` and the
`Name*` counterparts — and `NameMatcher`, the arity-free twin, is the same four over a name that
belongs to no key: a calendar's, a group's. `GroupMatcher<TKey>.AnyGroup()` exists for the members
that take a matcher and not a null, such as `PauseTriggerGroups`; a query filter is nullable and null
already means every name, so there is no "any" spelling to learn here.

The matcher text is a literal, not a pattern: a group named `50%` is selected by
`GroupStartsWith("50%")`, and the store escapes the wildcard on its way into SQL.

### Matchers as a vocabulary

`Matchers` is the entry point when you would rather not spell the generic argument, and it is where
the combinators live:

<!-- snippet: sample_querying_combining_matchers -->
```csharp
IMatcher<JobKey> notArchived = Matchers.Group<JobKey>(StringOperator.StartsWith, "archive-").Not();
IMatcher<TriggerKey> either = Matchers.Key(triggerKey).Or(Matchers.AllTriggers());
```
<!-- endSnippet -->

`Matchers.AllJobs()` and `Matchers.AllTriggers()` return `EverythingMatcher<TKey>`, `Matchers.Key(key)`
matches one key exactly, and `And`, `Or` and `Not` are extension methods on `IMatcher<TKey>`.

The combinators are for the *listener* matchers on `IListenerManager` and `IQuartzBuilder`, which
evaluate in memory. The query filters are typed to `GroupMatcher<TKey>` and `NameMatcher<TKey>`
specifically, because those are the two shapes a job store can translate to SQL.

## Paging

Results are ordered by group and then name, ordinal, on every store. That is what makes a page
deterministic: `Skip` and `Take` are offsets into one stable ordering, so page 3 is page 3 whichever
node answers.

<!-- snippet: sample_querying_paging -->
```csharp
PagedResult<JobHeader> page = await scheduler.QueryJobs(new JobQuery
{
    Skip = (pageNumber - 1) * pageSize,
    Take = pageSize,
});
```
<!-- endSnippet -->

`Take` defaults to `PagedQuery.DefaultTake`, which is **250**. An unpaged call therefore cannot
accidentally materialize a hundred thousand rows; `PagedResult<T>.HasMore` tells you whether anything
was left out. Ask for everything explicitly:

<!-- snippet: sample_querying_everything -->
```csharp
JobQuery everything = new() { Take = PagedQuery.All };
```
<!-- endSnippet -->

`PagedQuery.All` is `int.MaxValue`. It has a name because a call site that says `int.MaxValue` reads
as an overflow guard rather than as a decision, and over HTTP the same thing is spelled
[`?take=all`](../packages/http-api.md#listing-endpoints-are-paged).

`HasMore` is exact and effectively free — the stores read one row past `Take` to answer it.
`TotalCount` is `null` unless you ask for it, because on a persistent store it costs a second query:

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

`GetNumberOfJobs`, `GetNumberOfTriggers` and `GetNumberOfCalendars` are gone. A count is a query that
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

`Take = 0` is valid and returns an empty `Items`; the stores recognize the combination and run the
count query alone. The same idiom counts anything the family can select — triggers in the error state,
calendars whose name starts with a prefix, firings on one node.

## From a page to full detail

A listing gives you headers. When the user opens a row, or a script needs the actual objects, fetch
them by key in one round trip rather than in a loop:

<!-- snippet: sample_querying_headers_to_details -->
```csharp
List<JobKey> keys = page.Items.Select(h => h.Key).ToList();
List<IJobDetail> details = await scheduler.GetJobDetails(keys);

List<ITrigger> triggers = await scheduler.GetTriggers(triggerKeys);
```
<!-- endSnippet -->

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

`State` is a `FireInstanceState` — `Acquired` or `Executing` — and it is the one filter in the whole
family with a **non-null default**. A `FireInstanceQuery` that says nothing about state lists what is
running, because that is the question the query is usually asked. Set `State = null` to include
firings that a node has reserved but not yet started:

<!-- snippet: sample_querying_fire_instance_state -->
```csharp
FireInstanceQuery reservedAndRunning = new() { State = null };
FireInstanceQuery reservedOnly = new() { State = FireInstanceState.Acquired };
```
<!-- endSnippet -->

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
`GetPausedTriggerGroups()`, and
`new TriggerGroupQuery { Name = NameMatcher.NameEquals("reporting"), Take = 1 }` answers "is this one
group paused?" without listing the rest. The other three comparisons list a tenant's or a subsystem's
groups the same way: `NameMatcher.NameStartsWith("tenant-42-")`.

`JobGroup.Paused` works the same way, and on both stores: 4.x records paused job groups in
`QRTZ_PAUSED_JOB_GRPS`, so `QueryJobGroups(new JobGroupQuery { Paused = true })` is a real listing and
`new JobGroupQuery { Name = NameMatcher.NameEquals("reporting"), Take = 1 }` answers for one group. On 3.x this was the one
thing the ADO store could not report — `IsJobGroupPaused` answered `false` for every group there —
which is why the [4.0 schema migration](../../database/schema-changes.md#version-4-0) is mandatory even
for a database that took every optional 3.x migration.

A group can be paused while it holds nothing, and `Paused = true` reports such a group. The unfiltered
listing does not, because it enumerates the groups jobs and triggers are actually in — so a group with
no members appears in the paused listing alone, which is the only place a caller can find it in order
to resume it.

::: tip
Pausing an empty *trigger* group also pauses the triggers added to it afterwards. A paused *job* group
does that only in the in-memory store; the ADO store pauses the triggers of the jobs in the group when
the pause runs, and records the group, but does not impose the pause on jobs added later.
:::

Pausing and resuming by matcher is a *group* operation — it records the group as paused, which is what
catches the triggers added to it afterwards — so it is named for groups and answers with their names,
where the key-set `PauseTriggers(keys)` answers with the keys it moved:

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

Reading has two altitudes on purpose. The `Query*` members take a record — a filter, a page, an
optional count — and the `Get*` conveniences above answer the questions that need none of that.
Neither is the other's leftovers, and a third thing is deliberately missing: a shorthand that saves
only the `new`. `QueryJobs(new JobQuery())` is not worth an overload, because the record it names is
the point of the query API.

What does earn a name is a *preset* — one that knows a filter you would otherwise have to look up.
There is one, and it pages exactly as the member does — the first `PagedQuery.DefaultTake` items, with
`HasMore` reporting the rest:

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

Two more sit beside it. Resetting the failed triggers of a group was a listing plus a key-set reset;
it is one call on `IScheduler`, beside the key-set form it is built from:

<!-- snippet: sample_querying_reset_group_from_error -->
```csharp
List<TriggerKey> reset = await scheduler.ResetTriggersFromErrorState(
    GroupMatcher<TriggerKey>.GroupEquals("imports"));
```
<!-- endSnippet -->

That is still the two calls underneath — a listing filtered by `State = TriggerState.Error` and the
group, then `ResetTriggersFromErrorState(keys)` — so it is not one atomic operation, and a trigger that
fails between them is left for the next call. What resetting a trigger does is unchanged.

And asking whether a calendar is registered no longer means loading it. `GetCalendar` deserializes the
stored blob to hand you an `ICalendar` you were going to throw away; `Exists` asks the store for the
name:

<!-- snippet: sample_querying_calendar_exists -->
```csharp
bool haveHolidays = await scheduler.Exists("holidays");
```
<!-- endSnippet -->

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
`Take = PagedQuery.All` deliberately. That is fine for a group-name list and a bad idea for a trigger
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
