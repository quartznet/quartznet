---
title: 'Execution Groups'
---

Execution groups allow you to limit how many threads a category of job can use concurrently on a given scheduler node.
This prevents resource-intensive jobs from starving lightweight jobs of available threads.

## Concepts

An **execution group** is an optional tag on a trigger that characterizes the resource requirements of its associated job.
Examples might be `"batch-jobs"`, `"high-cpu"`, `"large-ram"`, or `"reports"`.

**Execution limits** are configured per node, declaring how many threads each group may consume:

- A positive integer (e.g. `5`) limits the group to that many concurrent executions.
- `0` forbids the group from running on this node entirely.
- No limit configured means unlimited (no restriction).

Each scheduler node can declare its own independent limits, making this ideal for heterogeneous clusters
where some nodes are tuned for heavy batch work and others for lightweight, latency-sensitive jobs.

## Setting execution groups on triggers

Use `TriggerBuilder.WithExecutionGroup()`:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithExecutionGroup("batch-jobs")
    .WithCronSchedule("0 0 2 * * ?")
    .Build();
```

Triggers without an execution group (`null`) use the default behavior. It is expected that all triggers
for a given job share the same execution group.

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
```

| Key | Meaning |
|-----|---------|
| `batch-jobs` | At most 2 concurrent "batch-jobs" triggers |
| `high-cpu` | At most 3 concurrent "high-cpu" triggers |
| `_` (underscore) | At most 10 concurrent triggers with no execution group |
| `*` (asterisk) | Default limit of 5 for any group not explicitly listed |

Special values for the limit:
- `unlimited`, `none`, or `null` — no restriction (same as not listing the group)
- `0` — completely forbidden on this node

### Via dependency injection

```csharp
services.AddQuartz(q =>
{
    q.UseExecutionLimits(limits =>
    {
        limits.ForGroup("batch-jobs", maxConcurrent: 2);
        limits.ForGroup("high-cpu", maxConcurrent: 3);
        limits.ForDefaultGroup(maxConcurrent: 10);
        limits.ForOtherGroups(maxConcurrent: 5);
    });
});
```

### Via scheduler API at runtime

```csharp
await scheduler.SetExecutionLimits(
    new ExecutionLimits()
        .ForGroup("batch-jobs", 2)
        .ForDefaultGroup(10)
        .ForOtherGroups(5));
```

Limits take effect on the next trigger acquisition cycle. Pass `null` to clear all limits:

```csharp
await scheduler.SetExecutionLimits(null);
```

## How it works

On each trigger acquisition cycle, the scheduler thread:

1. Computes the available slots per execution group by subtracting currently running counts from configured limits.
2. Passes these available limits to the job store during trigger acquisition.
3. The job store skips triggers whose execution group has no available slots.
4. When a job starts, the running count for its group is incremented; when it completes, the count is decremented.

This means:
- The overall thread pool limit (`quartz.threadPool.threadCount`) still applies as a global cap.
- Execution group limits provide additional per-group caps within that global pool.
- In the worst case, a group might be slightly under-utilized for one cycle if a slot opens between computation and acquisition.

## Clustering considerations

Execution limits are **per-node** configuration. Each scheduler node independently declares and enforces its own limits.
This is intentional — different nodes in a cluster may have different hardware capabilities.

Example: in a cluster with dedicated batch nodes and API nodes:
```
# batch-node.properties
quartz.executionLimit.batch-jobs = 8
quartz.executionLimit.* = 2

# api-node.properties
quartz.executionLimit.batch-jobs = 0
quartz.executionLimit.* = 10
```

## Interaction with DisallowConcurrentExecution

`[DisallowConcurrentExecution]` is always respected regardless of execution group configuration.
In the ADO job store, execution group filtering happens at the SQL level during trigger candidate selection,
while `[DisallowConcurrentExecution]` is enforced afterward in the acquisition loop. Both constraints
are applied — a trigger must satisfy both to be acquired.

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

The standard 4.x schema also includes an `EXECUTION_GROUP` column on `QRTZ_FIRED_TRIGGERS`.
It is currently not read or written by execution group logic, but is reserved for forward
compatibility and possible future cluster-wide execution group counting. If upgrading from 3.x,
add it alongside the `QRTZ_TRIGGERS` column:

```sql
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;  -- SQL Server
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;  -- PostgreSQL/MySQL/SQLite
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);  -- Oracle
```

RAMJobStore requires no schema changes.

## Dashboard

The Quartz Dashboard shows execution group information:
- Trigger list page displays an "Execution Group" column
- Trigger detail page shows the execution group
- Currently executing page shows which execution group each running job belongs to

## Common scenarios

### Preventing batch jobs from starving interactive work

```csharp
q.UseExecutionLimits(limits =>
{
    limits.ForGroup("batch", maxConcurrent: 3);    // max 3 batch jobs
    limits.ForOtherGroups(maxConcurrent: 10);      // everything else gets up to 10
});
```

### Dedicating a node to specific workloads

```
# Only run "reports" group on this node
quartz.executionLimit.reports = 10
quartz.executionLimit.* = 0
```

### Multi-tenant isolation

```csharp
limits.ForGroup("tenant-a", maxConcurrent: 5);
limits.ForGroup("tenant-b", maxConcurrent: 5);
limits.ForGroup("tenant-c", maxConcurrent: 5);
```
