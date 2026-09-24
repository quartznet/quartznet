---
title: 'Execution Groups'
---

Execution groups limit how many threads a category of job can use concurrently on a scheduler node, so
resource-intensive jobs cannot starve lightweight ones of threads.

## Concepts

An **execution group** is an optional tag on a trigger describing the resource needs of its job, such as
`"batch-jobs"`, `"high-cpu"`, `"large-ram"` or `"reports"`.

**Execution limits** are configured per node and say how many threads each group may use:

- A positive integer (e.g. `5`) limits the group to that many concurrent executions.
- `0` forbids the group on this node.
- No limit means unlimited.

Each node declares its own limits, which suits heterogeneous clusters: some nodes tuned for heavy batch
work, others for lightweight, latency-sensitive jobs.

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

Triggers without an execution group (`null`) use the default behavior. All triggers of a job are expected
to share the same execution group.

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

Special limit values:
- `unlimited`, `none`, or `null`: no restriction (same as not listing the group)
- `0`: forbidden on this node

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
scheduler.SetExecutionLimits(
    new ExecutionLimits()
        .ForGroup("batch-jobs", 2)
        .ForDefaultGroup(10)
        .ForOtherGroups(5));
```

Limits take effect on the next trigger acquisition cycle. Pass `null` to clear all limits:

```csharp
scheduler.SetExecutionLimits(null);
```

## How it works

On each trigger acquisition cycle:

1. The scheduler thread computes the free slots per execution group: configured limit minus running count.
2. It passes these to the job store during trigger acquisition.
3. The job store skips triggers whose execution group has no free slot.
4. A group's running count goes up when a job starts and down when it completes.

So:
- The overall thread pool limit (`quartz.threadPool.threadCount`) is still the global cap.
- Execution group limits add per-group caps within that pool.
- At worst, a group is slightly under-used for one cycle if a slot frees up between computation and acquisition.

## Clustering considerations

Execution limits are **per node**: each node declares and enforces its own, because nodes may have
different hardware.

Example: a cluster with dedicated batch nodes and API nodes:
```
# batch-node.properties
quartz.executionLimit.batch-jobs = 8
quartz.executionLimit.* = 2

# api-node.properties
quartz.executionLimit.batch-jobs = 0
quartz.executionLimit.* = 10
```

## Interaction with DisallowConcurrentExecution

`[DisallowConcurrentExecution]` is always respected. A trigger must satisfy both constraints to be
acquired.

The order of the two checks differs by store. It matters only because a slot can be spent on a trigger
that is then dropped.

- `RAMJobStore` checks `[DisallowConcurrentExecution]` first, the execution group limit second.
- In the ADO job store neither is a SQL predicate. The candidate select returns each trigger's execution
  group and the delegate counts slots down as it reads the rows; `[DisallowConcurrentExecution]` is
  checked afterwards in the acquisition loop.

Either way, a dropped trigger stays where it is and is reconsidered on the next cycle.

## Database migration

ADO.NET job stores keep the execution group in an `EXECUTION_GROUP` column on `QRTZ_TRIGGERS`. Without
the column, values set with `WithExecutionGroup()` are not persisted and every trigger looks ungrouped
after a restart. With RAMJobStore, execution group limits work without schema changes.

An ADO job store that uses execution groups needs the column. Add it to `QRTZ_TRIGGERS`:

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;

-- PostgreSQL / MySQL / SQLite
ALTER TABLE QRTZ_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;

-- Oracle
ALTER TABLE QRTZ_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);
```

Optionally, add it to `QRTZ_FIRED_TRIGGERS` for forward compatibility. It is not read or written yet;
it is reserved for future cluster-wide execution group counting:

```sql
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP NVARCHAR(200) NULL;  -- SQL Server
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD COLUMN EXECUTION_GROUP VARCHAR(200) NULL;  -- PostgreSQL/MySQL/SQLite
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD (EXECUTION_GROUP VARCHAR2(200) NULL);  -- Oracle
```

The scheduler probes for the column at startup and logs at Debug level if it is missing.

## Dashboard

The Quartz Dashboard shows the execution group on:
- the trigger list page (an "Execution Group" column)
- the trigger detail page
- the currently executing page, for each running job

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

- Every trigger of a tenant must carry `.WithExecutionGroup("tenant-a")`. The execution group is a
  separate tag, not derived from the trigger's key group.
- Limits are per node: a three-node cluster with the configuration above allows fifteen concurrent jobs
  per tenant, not five.

See [Multi-Tenancy](../multi-tenancy.md) and [Tenancy Patterns](../../tenancy-patterns.md) for the other
ways to separate tenants and how to choose.
