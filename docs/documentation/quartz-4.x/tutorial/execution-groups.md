---
title: 'Execution Groups'
---

Execution groups limit how many threads a category of job can use at once, so resource-heavy jobs do not
starve lightweight ones of threads.

## Concepts

An **execution group** is an optional tag on a trigger that names its job's resource needs, such as
`"batch-jobs"`, `"high-cpu"`, `"large-ram"` or `"reports"`.

An **execution limit** says how many threads a group may use:

| Limit | Means |
|---|---|
| a positive integer (e.g. `5`) | at most that many concurrent executions |
| `0` | the group never runs |
| none configured | unlimited |

Each limit is counted against an `ExecutionLimitScope`:

| Scope | The number is | Use it for |
| -- | -- | -- |
| `Node` (the default) | what *this* node may run at once; each node enforces its own copy, so N nodes can run N times the number | different hardware: a batch node and an API node want different numbers |
| `Cluster` | what *all nodes sharing the job store* may run between them | quotas: "this tenant gets eight threads", however many nodes are up |

One set of limits can mix both scopes. A limit with no scope is node-scoped, as execution limits always
were.

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

Triggers without an execution group (`null`) use the default behavior. All triggers of a job should share
the same execution group.

Move a stored trigger between groups without rescheduling it:

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

The new group applies from the next acquisition cycle; a running job keeps counting against the group it
was acquired under.

Reserved names, which cannot be execution groups:

| Name | Used for |
|---|---|
| `*` | the "other groups" catch-all limit |
| `_` | the property-config alias for the default (ungrouped) triggers |
| `null` (case-insensitive) | same alias as `_` |

Empty or whitespace-only strings become `null` (no group).

## Configuring execution limits

### Via properties

```properties
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

`quartz.clusterExecutionLimit.*` takes the same group keys, including `_` and `*`, and the same values as
`quartz.executionLimit.*`; only the scope differs. It is a separate prefix because every key under
`quartz.executionLimit` is a group name and every value a count, leaving no spelling for a scope.

| Value | Means |
|---|---|
| `unlimited`, `none` or `null` | no restriction, the same as not listing the group; takes no scope |
| `0` | completely forbidden |

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

`ForGroup`, `ForDefaultGroup` and `ForOtherGroups` take an optional trailing `ExecutionLimitScope`,
defaulting to `Node`.

For limits that come from a service, such as per-tenant quotas the application configures, use the
overload that receives the container. The callback runs when the scheduler is built, after options are
bound, post-configured and validated:

<!-- snippet: sample_execution_groups_from_options -->
```csharp
services.AddQuartz(q => q.UseExecutionLimits((serviceProvider, limits) =>
{
    TenantQuotaOptions quotas = serviceProvider.GetRequiredService<IOptions<TenantQuotaOptions>>().Value;

    foreach ((string tenant, int maxConcurrent) in quotas.PerTenant)
    {
        limits.ForGroup(tenant, maxConcurrent, ExecutionLimitScope.Cluster);
    }
}));
```
<!-- endSnippet -->

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

`ExecutionLimitsBuilder` is mutable; `ExecutionLimits`, which `Build()` returns and the scheduler reads,
is not, so limits cannot change under the scheduler thread while it acquires triggers.

### Letting the trigger group stand in

When a schedule already partitions work by trigger group (a group per tenant, per subsystem), use
`UseTriggerGroupWhenUnset()` instead of tagging each trigger with an execution group of the same name:

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

A trigger with no execution group is then limited as though its group were its `TriggerKey.Group`.

* **An explicit execution group always wins.** The derivation only fills a gap.
* **Nothing is persisted differently.** `ITrigger.ExecutionGroup` still reads `null`, and the store still
  writes `null` to `EXECUTION_GROUP`. The rule applies only where a limit is evaluated: the scheduler
  thread's in-flight counting and both job stores' acquisition filters. Turning it off changes only how
  limits are read.
* **`ForDefaultGroup` stops applying.** No trigger is ungrouped, so they fall under
  `ForGroup`/`ForOtherGroups`. Exception: a trigger whose group is spelled like a reserved name (`*`, `_`,
  `null`) stays ungrouped.
* It is code-only. There is no `quartz.executionLimit.*` property for it, because every key under that
  prefix is a group name.

Read limits back with `TryGetLimit(group, out int? maxConcurrent)`, or enumerate `Groups`. Each entry's
`Group` is an `ExecutionGroupScope`, one of three cases: `Default` (triggers with no execution group),
`OtherGroups` (the catch-all) and `Named(name)`, so no sentinel strings are involved. Its `Scope` says
which scope the number is counted in:

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

Configuration uses `_` (or `null`) for the default group and `*` for the catch-all. Quartz's other `*`,
in preferred-node pinning, means something else:

| Where it appears | What `*` means there | Typed reading |
|---|---|---|
| `quartz.executionLimit.*` or `quartz.clusterExecutionLimit.*` key / execution-limits HTTP body | The catch-all limit applied to any *named* group without a limit of its own (never to ungrouped triggers) | `ExecutionGroupScope.OtherGroups` |
| A trigger row's preferred-node column | An automatic pin no node has claimed yet — the trigger runs anywhere until one node fires it first and keeps it | `PreferredNode.Auto` |

In both places `*` is reserved: a trigger cannot have `*` as its execution group, and a node cannot have
`*` as its scheduler instance id.

## How it works

On each trigger acquisition cycle, the scheduler thread:

1. Computes the free slots per group by subtracting running counts from the **node-scoped** limits.
   Cluster-scoped limits are passed as configured: this node's firings are already reservations the store
   holds, and subtracting them twice would halve the quota on the busiest node.
2. Passes these limits to the job store during trigger acquisition.
3. The job store lowers each **cluster-scoped** limit by what the cluster has in flight, then skips
   triggers whose group has no free slot.
4. Increments a group's running count when a job starts, and decrements it when the job completes.

So:

* The thread pool limit (`quartz.threadPool.threadCount`) is still the global cap.
* Execution group limits add per-group caps inside it.
* At worst, a group is slightly under-used for one cycle if a slot opens between computation and
  acquisition.

## Clustering considerations

### Node-scoped limits

Each node declares and enforces its own node-scoped limits, so nodes with different hardware can differ.
For example, dedicated batch nodes and API nodes:

```properties
# batch-node.properties
quartz.executionLimit.batch-jobs = 8
quartz.executionLimit.* = 2

# api-node.properties
quartz.executionLimit.batch-jobs = 0
quartz.executionLimit.* = 10
```

Three nodes each configured `batch-jobs = 8` can run 24 batch jobs: right for hardware capacity, wrong
for a quota.

### Cluster-scoped limits

A cluster-scoped limit is one number for the whole cluster, enforced by every node:

<!-- snippet: sample_execution_groups_cluster_scope -->
```csharp
q.UseExecutionLimits(limits => limits
    .ForGroup("tenant-acme", 8, ExecutionLimitScope.Cluster));
```
<!-- endSnippet -->

The count comes from `QRTZ_FIRED_TRIGGERS`, which already records every reservation: a row appears when
any node acquires a trigger, becomes the running execution, and is deleted when the job completes or
cluster recovery cleans up after its node. Acquisition aggregates it by execution group, using the
`EXECUTION_GROUP` column the 4.x schema already has, so no table, column or migration is added.

**1. It is approximate by default, with a bounded overshoot.** By default the ADO.NET store acquires
triggers *without* the cluster's `TRIGGER_ACCESS` lock (`AcquireTriggersWithinLock` is `false`,
`MaxBatchSize` is `1`), so two nodes can both read "2 of 3 in flight" and each take one.

* The ceiling holds within one acquisition round. Overshoot is at most `limit + (nodes − 1)`, until the
  losers notice.
* `AcquireTriggersWithinLock = true` makes it exact, but serializes acquisition cluster-wide for *every*
  group, not only the limited ones.
* Overshoot is one trigger per node because the lock-free path only runs at an effective batch of one. A
  round asks for `min(available threads, MaxBatchSize)`, and the store takes `TRIGGER_ACCESS` whenever
  asked for more than one. Raising `MaxBatchSize` therefore removes the overshoot for rounds with more
  than one free thread, by taking the lock. A node down to its last free thread still acquires lock-free,
  so the bound is per node.

That is far below `limit × nodes`. For a tenant quota, "8, occasionally 9 for a moment" is fine; "8
became 24" is not. If you need exactness more than acquisition throughput, turn the lock on.

::: tip SQLite is exact, and nothing else is by default
`AcquireTriggersWithinLock` is forced to `true` for SQLite at startup, for locking reasons, so the
ceiling is exact there and approximate on every other database unless you turn the lock on. A SQLite test
proves nothing about SQL Server or PostgreSQL.
:::

**2. It fails closed.** The count and the work queue are in the same database. If the store is
unreachable or a node is partitioned from it, that node's `AcquireNextTriggers` throws, the scheduler
thread raises `SchedulerError` and backs off, and the node fires **nothing**. A database outage stops
firing; it does not remove the ceiling.

**3. A dead node's slots are held until recovery.** `ClusterRecover` deletes a failed node's
fired-trigger rows on the check-in cadence (`CheckinInterval` + `CheckinMisfireThreshold`). Until then
its reservations still count, so the quota is briefly **under**-served, never over-served. The count
deliberately includes nodes that have missed a check-in: one still running jobs would otherwise stop
counting and let the cluster exceed the ceiling.

**4. Held-back work can misfire.** A trigger held back by a ceiling stays `WAITING`, and acquisition
excludes anything older than `MisfireThreshold` (one minute by default). A group at its ceiling for
longer sends its backlog to `RecoverMisfiredJobs`, which applies each trigger's misfire instruction. This
is likelier with a cluster-scoped limit: a saturated node loses the trigger to a peer, but a saturated
cluster has no peer. If the backlog matters, pair a tightly limited group with
`MisfireInstruction.IgnoreMisfirePolicy`, or raise `MisfireThreshold`.

**Cost.** One extra aggregate per acquisition attempt, not per trigger, and only when at least one limit
is cluster-scoped. A configuration without one pays nothing.

Measured against PostgreSQL 15 and SQL Server 2022 with `QRTZ_FIRED_TRIGGERS` seeded from ten to ten
thousand rows (`ExecutionCeilingBenchmark` in `src/Quartz.Benchmark`, which says how to run it). Means
with BenchmarkDotNet's error, from containers on a developer machine; read the shape, not the absolute
microseconds:

| Rows in flight, groups | Aggregate, PostgreSQL | Aggregate, SQL Server | Candidate select, PostgreSQL | Candidate select, SQL Server |
|---|---|---|---|---|
| 1,000, eight | 672 µs (± 10) | 1,311 µs (± 34) | 627 µs (± 7) | 2,992 µs (± 68) |
| 10,000, sixty-four | 2,723 µs (± 116) | 7,821 µs (± 167) | | |

* **Below about a thousand rows the aggregate is one round trip and almost no work.** It costs about the
  same on both databases (their candidate selects differ), and about the same at a thousand rows as at
  ten. At the default `MaxBatchSize = 1` the ceiling costs *one extra round trip*, not one extra scan.
  Raising `MaxBatchSize` spreads that round trip over the batch, but takes the cluster lock, trading
  lock traffic for throughput and making the ceiling exact on those rounds.
* **Above that the scan shows.** `QRTZ_FIRED_TRIGGERS` has one row per reservation or running execution,
  so ten thousand is more than a realistic cluster's thread pools hold. A cluster reaches it by losing
  nodes faster than `ClusterRecover` cleans up after them.

::: tip An index for very large clusters, deliberately not in the standard schema
A covering index on `(SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP)` speeds up the aggregate:

| Rows in flight | PostgreSQL | SQL Server |
|---|---|---|
| 1,000 | | 1,311 µs → 714 µs |
| 10,000 | 2,723 µs → 1,350 µs | 7,821 µs → 1,870 µs |

At a hundred rows and below the difference is inside the measurement error.

It is **not** in the standard schema: `QRTZ_FIRED_TRIGGERS` gets an insert and a delete on every firing,
so every deployment would pay the write cost for a statement only cluster-scoped ceilings issue, at
concurrency most clusters never reach. Add it if you run a cluster-scoped ceiling *and* routinely hold
four figures of work in flight:

```sql
CREATE INDEX IDX_QRTZ_FT_EG_TG ON QRTZ_FIRED_TRIGGERS (SCHED_NAME, EXECUTION_GROUP, TRIGGER_GROUP);
```

If the extra round trip, rather than the scan, is the concern (as it is below ten thousand rows), the fix
is to fold the count into the candidate select, not to index the table.
:::

**RAMJobStore** is never clustered, so its cluster is the one process. It counts its own reservations
and running executions, and a cluster-scoped limit gives the same number as a node-scoped one. Both
stores are held to the same assertions in `JobStoreContractTest`.

## Interaction with DisallowConcurrentExecution

`[DisallowConcurrentExecution]` is always respected: a trigger must satisfy it and its execution group's
limit to be acquired. In the ADO job store neither is a SQL predicate. The candidate select returns each
trigger's execution group, the delegate counts slots down as it reads the rows, and
`[DisallowConcurrentExecution]` is checked afterwards in the acquisition loop. One statement serves every
limits configuration, so [letting the trigger group stand in](#letting-the-trigger-group-stand-in) needs
no dialect SQL of its own.

## Database schema

In 4.x the `EXECUTION_GROUP` column on `QRTZ_TRIGGERS` is part of the standard schema, **required** for
ADO.NET job stores, and in every table creation script. When upgrading a 3.x database, add it:

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;

-- PostgreSQL / MySQL / SQLite
ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;

-- Oracle
ALTER TABLE QRTZ_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);
```

`QRTZ_FIRED_TRIGGERS` also has an `EXECUTION_GROUP` column, recording the group a firing belongs to.
`IScheduler.QueryFireInstances` reports it from any node, and a **cluster-scoped** limit is counted from
it. Candidate triggers are still chosen from `QRTZ_TRIGGERS`. When upgrading from 3.x, add it too:

```sql
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;  -- SQL Server
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;  -- PostgreSQL/MySQL/SQLite
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);  -- Oracle
```

RAMJobStore requires no schema changes.

## Dashboard

The Quartz Dashboard shows execution groups on:

* the overview page: one row per group with its limit, its scope, what it has in flight and the headroom
  left (cluster-wide with a persistent store). Look here to see whether a ceiling is holding work back.
  See [Dashboard](../packages/dashboard.md#execution-groups).
* the trigger list page, as an "Execution Group" column;
* the trigger detail page;
* the currently executing page, for each running job.

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

```properties
# Only run "reports" group on this node
quartz.executionLimit.reports = 10
quartz.executionLimit.* = 0
```

### Multi-tenant isolation

A tenant quota belongs to the tenant, not the machine, so make it cluster-scoped:

<!-- snippet: sample_execution_groups_tenant_quotas -->
```csharp
limits.ForGroup("tenant-a", 5, ExecutionLimitScope.Cluster);
limits.ForGroup("tenant-b", 5, ExecutionLimitScope.Cluster);
limits.ForGroup("tenant-c", 5, ExecutionLimitScope.Cluster);
```
<!-- endSnippet -->

Node-scoped (`limits.ForGroup("tenant-a", 5)`) would give each tenant five threads *per node*: fifteen on
a three-node cluster. See [Multi-tenancy](../multi-tenancy.md) for the rest of per-tenant setup.
