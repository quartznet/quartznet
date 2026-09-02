---
title: 'Execution Groups'
---

Execution groups allow you to limit how many threads a category of job can use concurrently.
This prevents resource-intensive jobs from starving lightweight jobs of available threads.

## Concepts

An **execution group** is an optional tag on a trigger that characterizes the resource requirements of its associated job.
Examples might be `"batch-jobs"`, `"high-cpu"`, `"large-ram"`, or `"reports"`.

**Execution limits** declare how many threads each group may consume:

- A positive integer (e.g. `5`) limits the group to that many concurrent executions.
- `0` forbids the group from running entirely.
- No limit configured means unlimited (no restriction).

Every limit also says **what it is counted against**, its `ExecutionLimitScope`:

| Scope | The number is | Use it for |
| -- | -- | -- |
| `Node` (the default) | what *this* node may run at once. Each node enforces its own copy, so an N-node cluster can be running N times the number. | heterogeneous hardware — a batch node and an API node want different numbers |
| `Cluster` | what *every node sharing the job store* may run between them | quotas — "this tenant gets eight threads", however many nodes are up |

The two coexist in one set of limits, and one deployment often wants both. A limit that says nothing is
node-scoped, which is what execution limits have always meant.

## Setting execution groups on triggers

Use `TriggerBuilder.WithExecutionGroup()`:

<!-- snippet: sample_execution_groups_trigger -->
```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithExecutionGroup("batch-jobs")
    .WithCronSchedule("0 0 2 * * ?")
    .Build();
```
<!-- endSnippet -->

Triggers without an execution group (`null`) use the default behavior. It is expected that all triggers
for a given job share the same execution group.

A stored trigger can be moved between groups without rescheduling it:

<!-- snippet: sample_execution_groups_update_trigger -->
```csharp
await scheduler.UpdateTriggerDetails(
    trigger.Key,
    new TriggerDetailsUpdate().WithExecutionGroup("batch-jobs"));

// pass null to take the trigger out of every group
await scheduler.UpdateTriggerDetails(
    trigger.Key,
    new TriggerDetailsUpdate().WithExecutionGroup(null));
```
<!-- endSnippet -->

The new group applies from the next acquisition cycle; a job already running keeps counting against the
group it was acquired under.

The following names are reserved and cannot be used as execution group names:
- `*` — used for the "other groups" catch-all limit
- `_` — used as a property-config alias for the default (ungrouped) triggers
- `null` (case-insensitive) — same alias as `_`

Empty or whitespace-only strings are normalized to `null` (no group).

## Configuring execution limits

### Via properties

```
quartz.executionLimit.batch-jobs = 2
quartz.executionLimit.high-cpu = 3
quartz.executionLimit._ = 10
quartz.executionLimit.* = 5

quartz.clusterExecutionLimit.tenant-acme = 8
```

| Key | Meaning |
|-----|---------|
| `quartz.executionLimit.batch-jobs` | At most 2 concurrent "batch-jobs" triggers **on this node** |
| `quartz.executionLimit.high-cpu` | At most 3 concurrent "high-cpu" triggers on this node |
| `quartz.executionLimit._` (underscore) | At most 10 concurrent triggers with no execution group, on this node |
| `quartz.executionLimit.*` (asterisk) | Default limit of 5 for any group not explicitly listed |
| `quartz.clusterExecutionLimit.tenant-acme` | At most 8 concurrent "tenant-acme" triggers **across the whole cluster** |

`quartz.clusterExecutionLimit.*` takes the same group keys — including `_` and `*` — and the same
values as `quartz.executionLimit.*`; the only difference is the scope. It is a prefix of its own rather
than a magic value under the existing one, because every key under `quartz.executionLimit` is a group
name and every value is a count, so neither half had a spelling to spare.

Special values for the limit:
- `unlimited`, `none`, or `null` — no restriction (same as not listing the group); it takes no scope,
  since unlimited on a node and unlimited across the cluster are the same permission
- `0` — completely forbidden

### Via dependency injection

<!-- snippet: sample_execution_groups_dependency_injection -->
```csharp
services.AddQuartz(q =>
{
    q.UseExecutionLimits(limits =>
    {
        limits.ForGroup("batch-jobs", maxConcurrent: 2);                        // per node
        limits.ForGroup("high-cpu", maxConcurrent: 3);                          // per node
        limits.ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster);         // per cluster
        limits.ForDefaultGroup(maxConcurrent: 10);
        limits.ForOtherGroups(maxConcurrent: 5);
    });
});
```
<!-- endSnippet -->

`ForGroup`, `ForDefaultGroup` and `ForOtherGroups` all take an optional trailing
`ExecutionLimitScope`, defaulting to `Node`.

### Via scheduler API at runtime

<!-- snippet: sample_execution_groups_set_at_runtime -->
```csharp
await scheduler.SetExecutionLimits(
    ExecutionLimitsBuilder.Create()
        .ForGroup("batch-jobs", 2)
        .ForDefaultGroup(10)
        .ForOtherGroups(5)
        .Build());
```
<!-- endSnippet -->

`ExecutionLimitsBuilder` is mutable and `ExecutionLimits` — what `Build()` returns and what the scheduler
reads — is not, so limits cannot change underneath the scheduler thread that is acquiring triggers with them.

### Letting the trigger group stand in

Some schedules already partition their work by trigger group: a group per tenant, a group per subsystem.
Tagging every one of those triggers with an execution group of the same name would be a second copy of a
fact the key already carries. `UseTriggerGroupWhenUnset()` says so once instead:

<!-- snippet: sample_execution_groups_trigger_group_when_unset -->
```csharp
await scheduler.SetExecutionLimits(
    ExecutionLimitsBuilder.Create()
        .UseTriggerGroupWhenUnset()
        .ForGroup("tenant-a", 4)   // names a trigger group here, because none of its triggers name one
        .ForOtherGroups(2)         // every other tenant gets two
        .Build());
```
<!-- endSnippet -->

With the option on, a trigger that carries no execution group is limited as though its group were its own
`TriggerKey.Group`. Three things are worth knowing:

* **An explicit execution group always wins.** A trigger that names one is limited by that one, whatever
  group it is in. The derivation only fills a gap.
* **Nothing is persisted differently.** `ITrigger.ExecutionGroup` still reads `null`, and the store still
  writes `null` to `EXECUTION_GROUP`. The rule is applied where a limit is evaluated — the scheduler
  thread's in-flight counting and both job stores' acquisition filters — and nowhere else. Turning it off
  again changes nothing but how the limits are read.
* **`ForDefaultGroup` stops applying.** With the derivation on, no trigger is ungrouped, so ungrouped
  triggers fall under `ForGroup`/`ForOtherGroups` like any other. The one exception is a trigger whose
  group is spelled like a name the limits reserve (`*`, `_`, `null`): it stays ungrouped rather than being
  folded into the bucket that spelling means.

The option is a code-level one. There is no `quartz.executionLimit.*` property for it, because every key
under that prefix is a group name and a magic one would collide with a group that happened to share the
spelling.
Read one back with `TryGetLimit(group, out int? maxConcurrent)`, or enumerate `Groups`. Each entry's
`Group` is an `ExecutionGroupScope`, one of exactly three cases — `Default` (triggers with no execution
group), `OtherGroups` (the catch-all) and `Named(name)` — so reading limits never involves sentinel
strings, and its `Scope` says which scope the number is counted in:

<!-- snippet: sample_execution_groups_read_limits -->
```csharp
ExecutionLimits? limits = await scheduler.GetExecutionLimits();
foreach (ExecutionGroupLimit limit in limits?.Groups ?? [])
{
    string group = limit.Group.IsDefault ? "(no group)"
        : limit.Group.IsOtherGroups ? "(other groups)"
        : limit.Group.Name!;
    Console.WriteLine($"{group}: {limit.MaxConcurrent?.ToString() ?? "unlimited"} per {limit.Scope}");
}

limits?.TryGetLimit(ExecutionGroupScope.Named("batch-jobs"), out int? batchLimit);
```
<!-- endSnippet -->

Limits take effect on the next trigger acquisition cycle. Pass `null` to clear all limits:

<!-- snippet: sample_execution_groups_clear_limits -->
```csharp
await scheduler.SetExecutionLimits(null);
```
<!-- endSnippet -->

### The `*` in configuration keys, and the other `*`

Configuration spells the scopes with reserved key spellings: `_` (or `null`) for the default group and `*`
for the catch-all. Quartz has one other `*` sentinel, in trigger preferred-node pinning, and the two mean
different things — the table below puts them side by side so neither is read as the other:

| Where it appears | What `*` means there | Typed reading |
|---|---|---|
| `quartz.executionLimit.*` or `quartz.clusterExecutionLimit.*` key / execution-limits HTTP body | The catch-all limit applied to any *named* group without a limit of its own (never to ungrouped triggers) | `ExecutionGroupScope.OtherGroups` |
| A trigger row's preferred-node column | An automatic pin no node has claimed yet — the trigger runs anywhere until one node fires it first and keeps it | `PreferredNode.Auto` |

In both places `*` is reserved vocabulary, not a name: a trigger cannot have `*` as its execution group, and
a node cannot have `*` as its scheduler instance id.

## How it works

On each trigger acquisition cycle, the scheduler thread:

1. Computes the available slots per execution group by subtracting currently running counts from the
   configured **node-scoped** limits. Cluster-scoped limits are deliberately left as configured here —
   this node's firings are already reservations the store is holding, and subtracting them twice would
   halve the quota on the busiest node.
2. Passes these available limits to the job store during trigger acquisition.
3. The job store lowers each **cluster-scoped** limit by what the cluster holds in flight, then skips
   triggers whose execution group has no available slots.
4. When a job starts, the running count for its group is incremented; when it completes, the count is decremented.

This means:
- The overall thread pool limit (`quartz.threadPool.threadCount`) still applies as a global cap.
- Execution group limits provide additional per-group caps within that global pool.
- In the worst case, a group might be slightly under-utilized for one cycle if a slot opens between computation and acquisition.

## Clustering considerations

### Node-scoped limits

A node-scoped limit is configuration each node declares and enforces for itself. This is intentional —
different nodes in a cluster may have different hardware capabilities.

Example: in a cluster with dedicated batch nodes and API nodes:
```
# batch-node.properties
quartz.executionLimit.batch-jobs = 8
quartz.executionLimit.* = 2

# api-node.properties
quartz.executionLimit.batch-jobs = 0
quartz.executionLimit.* = 10
```

Because each node enforces its own copy, three nodes each configured `batch-jobs = 8` can be running 24
batch jobs. That is the right answer for hardware capacity and the wrong one for a quota.

### Cluster-scoped limits

A cluster-scoped limit is one number for the whole cluster, and every node enforces the same one:

<!-- snippet: sample_execution_groups_cluster_scope -->
```csharp
q.UseExecutionLimits(limits => limits
    .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster));
```
<!-- endSnippet -->

**Where the count comes from.** `QRTZ_FIRED_TRIGGERS` already is the cluster's reservation ledger — a
row appears when any node acquires a trigger, becomes the running execution, and is deleted when the job
completes or when cluster recovery cleans up after the node that owned it. Acquisition aggregates that
table by execution group, so the ceiling needs no new table, no new column and no migration; the
`EXECUTION_GROUP` column on `QRTZ_FIRED_TRIGGERS` has been part of the 4.x schema since it was written.

Four things about the guarantee are worth knowing before you rely on it.

**1. It is approximate by default, with a bounded overshoot.** By default the ADO.NET store acquires
triggers *without* taking the cluster's `TRIGGER_ACCESS` lock (`AcquireTriggersWithinLock` is `false` and
`MaxBatchSize` is `1`), so two nodes can read "2 of 3 in flight" in the same instant and each take one.

> The ceiling holds within one acquisition round. Transient overshoot is bounded by the number of nodes
> acquiring concurrently — at most `limit + (nodes − 1)`, for as long as it takes the losers to notice.
> Setting `AcquireTriggersWithinLock = true` makes it exact, at the cost of serializing acquisition
> cluster-wide for *every* group rather than only the limited ones.

One trigger per node is the whole of the overshoot, because the lock-free path only exists at an
*effective* batch of one: the store takes `TRIGGER_ACCESS` whenever it is asked for more than one
trigger, and a round asks for `min(available threads, MaxBatchSize)`. Raising `MaxBatchSize` therefore
does not widen the overshoot — it removes it for every round with more than one thread free, by taking
that lock, which is the same trade `AcquireTriggersWithinLock` makes deliberately. A node down to its
last free thread still acquires lock-free whatever `MaxBatchSize` says, which is why the bound is
stated per node rather than per batch.

That is a real improvement on `limit × nodes`, and it is what a tenant quota actually needs: "8,
occasionally 9 for a moment" is fine, "8 became 24" is not. If you need exactness more than you need
acquisition throughput, turn the lock on deliberately.

::: tip SQLite is exact, and nothing else is by default
`AcquireTriggersWithinLock` is forced to `true` for SQLite at startup (it has to be, for locking
reasons), so the ceiling is exact there and approximate on every other database until you say otherwise.
Do not read a SQLite test as evidence about SQL Server or PostgreSQL.
:::

**2. It fails closed, structurally.** The ledger and the work queue are the same database, so there is
nothing to fail open *to*. If the store is unreachable or a node is partitioned away from it, that node's
`AcquireNextTriggers` throws, the scheduler thread raises `SchedulerError` and backs off, and the node
fires **nothing at all** — the quota is not bypassed, the node is out of service. A database outage
therefore stops firing rather than removing the ceiling. That is the safe direction for a quota and the
one to plan for.

**3. A dead node's slots are held until recovery.** `ClusterRecover` deletes a failed node's fired-trigger
rows on the ordinary check-in cadence (`CheckinInterval` + `CheckinMisfireThreshold`). Until it does, the
dead node's reservations still count, so the quota is briefly **under**-served, never over-served. The
count is deliberately *not* narrowed to nodes that are currently checking in: a node that has missed one
check-in but is still running jobs would stop counting, and that would let the cluster exceed the ceiling.

**4. Held-back work can misfire.** A trigger a ceiling holds back stays `WAITING`, and acquisition
excludes anything older than `MisfireThreshold` — one minute by default. A group parked at its ceiling
for longer than that feeds its backlog into `RecoverMisfiredJobs`, with whatever each trigger's misfire
instruction says. This is more likely with a cluster-scoped limit than a node-scoped one, because a
saturated node simply loses the trigger to a peer while a saturated *cluster* has no peer to lose it to.
Pair a tightly limited group with `MisfireInstruction.IgnoreMisfirePolicy`, or raise
`MisfireThreshold`, if the backlog matters.

**Cost.** One extra aggregate per acquisition attempt — not per trigger — emitted only when at least one
limit is cluster-scoped. A configuration with none pays nothing.

What that costs was measured against PostgreSQL 15 and SQL Server 2022 with `QRTZ_FIRED_TRIGGERS` seeded
from ten to ten thousand rows (`ExecutionCeilingBenchmark` in `src/Quartz.Benchmark`, which says how to
run it). Figures below are means with BenchmarkDotNet's error, from containers on a developer machine —
read the shape rather than the absolute microseconds, since a tuned deployment's round trip is faster
than a container's. Two things came out of it:

- **Below about a thousand rows in flight the aggregate is one round trip and almost no work.** With a
  thousand rows and eight groups it took 672 µs (± 10) on PostgreSQL against 627 µs (± 7) for the
  acquisition attempt's own candidate select, and 1,311 µs (± 34) on SQL Server against 2,992 µs (± 68).
  The two databases differ because their candidate selects do; the aggregate costs much the same on
  both, and much the same at a thousand rows as at ten — which is to say the round trip is the whole of
  it. So the ceiling's price at `MaxBatchSize = 1` — the default — is *one extra round trip*, not one
  extra scan. Raising `MaxBatchSize` amortises that round trip over the whole batch, and takes the
  cluster lock while doing it, so what it buys in throughput it pays for in lock traffic — and gets
  an exact ceiling on those rounds as change.
- **Above that the scan starts to show.** At ten thousand rows and sixty-four groups the aggregate took
  2,723 µs (± 116) on PostgreSQL and 7,821 µs (± 167) on SQL Server. `QRTZ_FIRED_TRIGGERS` holds one row
  per reservation or running execution, so ten thousand is past what a realistic cluster's thread pools
  can hold at once; it is where a cluster that has been losing nodes faster than `ClusterRecover` cleans
  up after them ends up.

::: tip An index for very large clusters, deliberately not in the standard schema
A covering index on `(SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP)` cuts the aggregate roughly in half at
a thousand rows in flight on SQL Server (1,311 µs → 714 µs) and by half to three quarters at ten
thousand on both (PostgreSQL 2,723 µs → 1,350 µs; SQL Server 7,821 µs → 1,870 µs). At a hundred rows and
below the difference is inside the measurement error.

It is **not** part of the standard schema, because `QRTZ_FIRED_TRIGGERS` is inserted into and deleted
from on every single firing: the index is a write cost every deployment would pay to speed up a
statement only the deployments that opt into a cluster-scoped ceiling ever issue, and only at
concurrency levels most clusters never reach. Add it yourself if you run a cluster-scoped ceiling *and*
routinely hold four figures of work in flight:

```sql
CREATE INDEX IDX_QRTZ_FT_EG_TG ON QRTZ_FIRED_TRIGGERS (SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP);
```

If the extra round trip rather than the scan is what bothers you — which is what the numbers say for
everything short of ten thousand rows — the answer is to fold the count into the candidate select rather
than to index the table.
:::

**RAMJobStore.** `RAMJobStore` is never clustered, so its cluster is the one process: it counts its own
reservations and running executions, and a cluster-scoped limit comes out as the same number a
node-scoped one would. Both stores are held to the same assertions in `JobStoreContractTest`.

## Interaction with DisallowConcurrentExecution

`[DisallowConcurrentExecution]` is always respected regardless of execution group configuration. Both
constraints are applied — a trigger must satisfy both to be acquired. In the ADO job store, neither is a
SQL predicate: the candidate select projects each trigger's execution group and the delegate counts slots
down as it reads the rows, and `[DisallowConcurrentExecution]` is checked afterwards in the acquisition
loop. One statement therefore serves every limits configuration, which is also why letting the trigger
group stand in for an unset execution group needs no dialect SQL of its own.

## Database schema

In Quartz.NET 4.x, the `EXECUTION_GROUP` column is part of the standard schema and is
**required** for ADO.NET job stores. The column is included in all table creation scripts.

If you are upgrading from a 3.x database, add the column:

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;

-- PostgreSQL / MySQL / SQLite
ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;

-- Oracle
ALTER TABLE QRTZ_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);
```

The standard 4.x schema also includes an `EXECUTION_GROUP` column on `QRTZ_FIRED_TRIGGERS`. It records
the execution group a firing belongs to, which two things read: `IScheduler.QueryFireInstances` reports
it from any node in the cluster, and a **cluster-scoped** limit is counted by aggregating over it. Which
triggers are candidates is still decided from `QRTZ_TRIGGERS`. If upgrading from 3.x, add it alongside
the `QRTZ_TRIGGERS` column:

```sql
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;  -- SQL Server
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;  -- PostgreSQL/MySQL/SQLite
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);  -- Oracle
```

RAMJobStore requires no schema changes.

## Dashboard

The Quartz Dashboard shows execution group information:
- The overview page carries an execution-group panel: one row per group with its limit, the scope that
  limit is counted in, what it has in flight and the headroom left — cluster-wide when the job store is
  persistent — which is where to look to see whether a ceiling set here is the thing holding work back
  (see [Dashboard](../packages/dashboard.md#execution-groups))
- Trigger list page displays an "Execution Group" column
- Trigger detail page shows the execution group
- Currently executing page shows which execution group each running job belongs to

## Common scenarios

### Preventing batch jobs from starving interactive work

<!-- snippet: sample_execution_groups_batch_versus_interactive -->
```csharp
q.UseExecutionLimits(limits =>
{
    limits.ForGroup("batch", maxConcurrent: 3);    // max 3 batch jobs
    limits.ForOtherGroups(maxConcurrent: 10);      // everything else gets up to 10
});
```
<!-- endSnippet -->

### Dedicating a node to specific workloads

```
# Only run "reports" group on this node
quartz.executionLimit.reports = 10
quartz.executionLimit.* = 0
```

### Multi-tenant isolation

A tenant quota is a property of the tenant, not of the machine, so it is cluster-scoped:

<!-- snippet: sample_execution_groups_tenant_quotas -->
```csharp
limits.ForGroup("tenant-a", 5, ExecutionLimitScope.Cluster);
limits.ForGroup("tenant-b", 5, ExecutionLimitScope.Cluster);
limits.ForGroup("tenant-c", 5, ExecutionLimitScope.Cluster);
```
<!-- endSnippet -->

Node-scoped instead (`limits.ForGroup("tenant-a", 5)`) would give each tenant five threads *per node*,
which on a three-node cluster is fifteen. See [Multi-tenancy](../multi-tenancy.md) for the rest of the
per-tenant story.
