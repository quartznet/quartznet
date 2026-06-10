---
title: 'Node Affinity (Preferred Node)'
---

::: tip
Available from Quartz.NET 3.18 onwards. See also [Execution Groups](execution-groups.md) for per-node thread limits.
:::

Node affinity allows you to control which cluster node runs a specific trigger. This is useful when a job
maintains in-memory state (such as a cache) between runs and needs to consistently execute on the same node.

## Concepts

A **preferred node** is an optional property on a **trigger** that specifies which cluster node should acquire it.
Since the setting is per-trigger (not per-job), a job with multiple triggers could have different preferred
nodes — set the same value on all triggers for a job if you need full job-level affinity.

- When set to a specific scheduler instance id (e.g. `"node-1"`), only that node will acquire the trigger.
- When set to `"*"` (auto-pin), any node can acquire the trigger on its first fire, and it is then
  automatically pinned to whichever node fires it first.
- When `null` (the default), any node can acquire the trigger — the standard Quartz behavior.

Preferred node is a **strong preference with automatic failover**, not a hard constraint. The trigger
acquisition SQL filters out triggers pinned to live nodes, but if the preferred node is not currently
registered (hasn't started yet, or its check-in has expired), other nodes may temporarily acquire the
trigger. See [Failover behavior](#failover-behavior) for the full semantics.

## Setting preferred node on triggers

Use `TriggerBuilder.WithPreferredNode()`:

```csharp
// Pin to a specific node
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode("production-node-1")
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```

```csharp
// Auto-pin: first node to fire claims it
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode("*")
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```

## Auto-pin mode

When a trigger has `PreferredNode = "*"`:

1. The trigger enters the `WAITING` state as usual. Any node can acquire it.
2. The first node to fire the trigger writes `"auto:" + instanceId` to the `PREFERRED_NODE` column
   (e.g., `"auto:production-node-1"`). The `"auto:"` prefix distinguishes auto-pinned triggers from
   explicit pins.
3. From that point on, only that node will acquire the trigger (unless it goes down).

Reading back an auto-pinned trigger's `PreferredNode` property returns the plain node name
(e.g., `"production-node-1"`) — the internal `"auto:"` prefix is stripped by the public getter.
This means `other.PreferredNode = trigger.PreferredNode` works naturally. The value is safe for
`GetTriggerBuilder()`, `TriggerDetailsUpdate`, and any copy/assign flow. To restore auto-pin
behavior on a rebuilt trigger, set `"*"` explicitly.

This is ideal when you don't know node names at configuration time, or when you want Quartz to
automatically distribute pinned triggers across available nodes.

## Failover behavior

When a preferred node dies:

1. **Acquisition**: The trigger acquisition SQL includes a `NOT IN` subquery against
   `QRTZ_SCHEDULER_STATE` that treats nodes with expired check-ins (or absent state rows)
   as dead. Other nodes can acquire the trigger during this window.

2. **Auto-pinned trigger recovery**: The cluster recovery process (`ClusterRecover`)
   detects the dead node using the standard `CalcFailedIfAfter` failure detector. Before
   deleting the dead node's scheduler state row, it resets auto-pinned triggers
   (`PREFERRED_NODE = 'auto:' + deadNodeId`) back to the `"*"` sentinel. The first eligible
   node to fire the trigger claims it, correctly respecting execution group limits.

3. **Explicit pins are preserved**: Triggers with explicit pins (no `"auto:"` prefix) are
   NOT re-pinned during failover. The acquisition SQL's failover clause allows any surviving
   node to acquire the trigger while the preferred node is down. When the original node
   returns and registers in `QRTZ_SCHEDULER_STATE`, it naturally reclaims its triggers.

4. **Startup-order race**: If a trigger is pinned to a node that hasn't started yet (no
   `QRTZ_SCHEDULER_STATE` row), another node may temporarily acquire it. However, since
   cluster recovery only processes nodes that **were** registered and have stale check-ins,
   the pin is not overwritten. The intended node receives the trigger once it starts.

Important caveats:

- Between the preferred node dying and cluster recovery running, a fast-firing trigger
  may execute on multiple surviving nodes before the batch re-pin runs. In a 3+ node cluster,
  this means a few fires may be distributed before ownership settles.
- Preferred node is a strong preference, not a hard constraint. If the target node has never
  registered, the trigger is eligible on any node. Always verify that `quartz.scheduler.instanceId`
  is set to a stable, correct value.
- The `"auto:"` substring is reserved. `WithPreferredNode()`, `TriggerDetailsUpdate.WithPreferredNode()`,
  and the `AbstractTrigger.PreferredNode` setter reject values containing `"auto:"` anywhere.
  Scheduler instance IDs containing `"auto:"` are also flagged at startup.

## Updating preferred node at runtime

You can change a trigger's preferred node without rescheduling:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode("new-node"));
```

Pass `null` to clear the preference:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode(null));
```

## Important: Instance ID stability

Node affinity relies on matching instance names. If your scheduler uses auto-generated instance IDs
(`quartz.scheduler.instanceId = AUTO`), the ID changes on every restart, which breaks node affinity.

**You must configure stable instance IDs** when using preferred node. The instance ID must not
contain the substring `"auto:"` (reserved for internal auto-pin tracking).

```
quartz.scheduler.instanceId = production-node-1
```

Or via dependency injection:

```csharp
services.AddQuartz(q =>
{
    q.SchedulerId = "production-node-1";
});
```

## Interaction with execution groups

Node affinity and [execution groups](execution-groups.md) are orthogonal features that compose naturally:

- **Execution groups** control how many concurrent threads a category of job can use on a given node.
- **Preferred node** controls which node should run the trigger.

A trigger can have both:

```csharp
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode("batch-node-1")
    .WithExecutionGroup("batch-jobs")
    .WithCronSchedule("0 0 2 * * ?")
    .Build();
```

Both constraints are enforced during trigger acquisition.

## RAMJobStore

Node affinity is a clustering feature. RAMJobStore is inherently single-node, so the `PreferredNode`
property has no effect on trigger acquisition when using RAMJobStore. The property is preserved on the
trigger object in memory, but no filtering is applied.

## Database migration

For ADO.NET job stores, preferred node is stored in a `PREFERRED_NODE` column on the `QRTZ_TRIGGERS` table.
Without this column, node affinity functionality is not available.

To add the column:

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE NVARCHAR(250) NULL;

-- PostgreSQL / MySQL / SQLite
ALTER TABLE QRTZ_TRIGGERS ADD COLUMN PREFERRED_NODE VARCHAR(250) NULL;

-- Oracle
ALTER TABLE QRTZ_TRIGGERS ADD (PREFERRED_NODE VARCHAR2(250) NULL);
```

The column is wider than `INSTANCE_NAME` (200) to accommodate the internal `"auto:"` prefix (5 chars).
The scheduler probes for column existence at startup and logs a warning if the column is missing.

## Common scenarios

### In-memory cache between job runs

```csharp
// Use auto-pin so the first node to run builds the cache,
// and all subsequent runs stay on that node
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("cacheJob")
    .ForJob(job)
    .WithPreferredNode("*")
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```

### Dedicated hardware for specific jobs

```csharp
// Pin to the node with GPU access
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("mlTraining")
    .ForJob(job)
    .WithPreferredNode("gpu-node-1")
    .WithCronSchedule("0 0 2 * * ?")
    .Build();
```

### Manual load distribution

```csharp
// Explicitly distribute triggers across known nodes
await scheduler.ScheduleJob(
    TriggerBuilder.Create()
        .WithIdentity("reportA").ForJob(job)
        .WithPreferredNode("node-1")
        .WithCronSchedule("0 0 3 * * ?").Build());

await scheduler.ScheduleJob(
    TriggerBuilder.Create()
        .WithIdentity("reportB").ForJob(job)
        .WithPreferredNode("node-2")
        .WithCronSchedule("0 0 3 * * ?").Build());
```
