---
title: 'Node Affinity (Preferred Node)'
---

Node affinity lets you control **which cluster node runs a specific trigger**. This is useful when a job
maintains in-memory state (such as a cache or a warmed-up connection) between runs and should keep
executing on the same node.

See also [Execution Groups](execution-groups.md), which limits *how many* threads a category of job may
use on a node. The two features compose: affinity decides *where* a trigger runs, execution groups decide
*how much* of a node it may consume.

## Concepts

A **preferred node** is an optional property of a **trigger** naming the scheduler instance that should
acquire it. Because the setting lives on the trigger rather than the job, a job with several triggers
could in principle have different preferred nodes — set the same value on all of a job's triggers if you
want job-level affinity.

`ITrigger.PreferredNode` is a `PreferredNode` value with three ways to make one:

- `PreferredNode.For("node-1")` pins the trigger to that scheduler instance id (the `Scheduler:InstanceId`
  of the node).
- `PreferredNode.Auto` requests **auto-pin**: the first node to fire the trigger claims it.
- `PreferredNode.None` (the default) means no preference — standard Quartz behavior.

Preferred node is a **strong preference with automatic failover**, not a hard constraint. Acquisition
filters out triggers pinned to *live* nodes, but if the pinned node is not currently checking in, other
nodes take over. See [Failover behavior](#failover-behavior).

Two columns back this on `QRTZ_TRIGGERS`:

| `PREFERRED_NODE` | `PREFERRED_NODE_AUTO` | Meaning |
|---|---|---|
| `NULL` | false | No affinity (`PreferredNode.None`, the default) |
| `'*'` | false | Auto-pin requested, not yet claimed (`PreferredNode.Auto`) |
| `'node-1'` | true | Auto-claimed by `node-1` |
| `'node-1'` | false | Named pin to `node-1` (`PreferredNode.For("node-1")`) |

The node name is stored verbatim and the auto-claim is recorded separately, so **no instance id is
reserved** — a node may legitimately be called `auto:thing` or `*-west` without confusing Quartz. The
protocol's own markers (`*`, `_`, `null`) are the only names `PreferredNode.For` refuses.

## Setting the preferred node

Use `TriggerBuilder.WithPreferredNode()`:

```csharp
// Pin to a specific node
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode(PreferredNode.For("production-node-1"))
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```

```csharp
// Auto-pin: the first node to fire it claims it
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode(PreferredNode.Auto)
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```

Read it back from any `ITrigger`:

```csharp
ITrigger t = await scheduler.GetTrigger(new TriggerKey("myTrigger"));
PreferredNode pin = t.PreferredNode;
string? node = pin.Node;         // "production-node-1"; null when unpinned or an unclaimed auto-pin
bool auto = pin.IsAutomatic;     // false for a pin you named
bool unpinned = pin.IsNone;
```

::: warning
The value must match the instance id **exactly**. Pin comparisons happen in SQL using the database's
string collation, so a value differing only in case is a different node — and on a case-sensitive
database, one that never matches.
:::

## Auto-pin mode

When a trigger's preferred node is `PreferredNode.Auto`:

1. The trigger is acquirable by any node, as usual.
2. The first node to fire it writes its own instance id to `PREFERRED_NODE` and sets
   `PREFERRED_NODE_AUTO`. The write is a compare-and-swap against the value seen at acquisition, so a
   concurrent re-pin or clear wins over the claim rather than being clobbered by it.
3. From then on only that node acquires the trigger — until it stops checking in.

This is ideal when you don't know node names at configuration time but still want a trigger to stay put.

Rebuilding an auto-pinned trigger preserves the auto-claim:

```csharp
// The rebuilt trigger is still auto-pinned, so it will still fail over if that node dies
ITrigger rebuilt = trigger.GetTriggerBuilder().WithDescription("updated").Build();
```

The pin carries its own auto-claim flag, so a pin moved from one trigger to another arrives as the pin it
was. What you write is what you get back:

```csharp
// a pin you named; IsAutomatic is false
ITrigger named = trigger.GetTriggerBuilder()
    .WithPreferredNode(PreferredNode.For("node-2"))
    .Build();

// no preference at all
ITrigger unpinned = trigger.GetTriggerBuilder()
    .WithPreferredNode(PreferredNode.None)
    .Build();
```

`ITrigger.PreferredNode` is read-only, like the rest of a trigger: rebuild the trigger to change it, and hand
the result to `IScheduler.RescheduleJob` — or, for this one property on its own,
[update the trigger in place](#updating-the-preferred-node-at-runtime).

## Failover behavior

When the preferred node stops checking in:

1. **Acquisition.** The acquisition query treats a node whose last check-in is older than the
   cluster check-in threshold as dead, so surviving nodes may acquire its pinned triggers immediately —
   without waiting for cluster recovery.
2. **Steal on fire.** A node that fires a trigger still auto-claimed by another (stale) node takes the
   pin over via compare-and-swap. Affinity converges on a live node instead of bouncing.
3. **Cluster recovery.** When recovery confirms a node dead, auto-claimed pins belonging to it are reset
   to an unclaimed auto-pin before its state row is deleted, so any *eligible* node can claim them —
   which correctly respects execution group limits.
4. **Named pins are preserved.** They are never re-pinned. While the node is down other nodes run the
   trigger; when it returns and checks in again, it naturally reclaims it.

## Updating the preferred node at runtime

You can re-pin without rescheduling:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.For("node-2")));
```

Pass `PreferredNode.None` to clear the preference entirely:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.None));
```

## Requirements and limitations

- **Clustering and a stable instance id.** Affinity only means anything in a cluster —
  `store.UseClustering()`, see [Clustering](advanced-enterprise-features.md) — and only with a *stable*
  `Scheduler:InstanceId`. With `GenerateInstanceId = true` the id changes on every restart, so a stored pin
  names a node that no longer exists; Quartz warns at startup when it detects an auto-generated id.
- **RAMJobStore ignores it.** A pin is stored and returned as metadata but never filters acquisition —
  a single-node in-memory scheduler always runs the trigger.
- **Pinned to a node that never registers.** If the target instance id has never checked in, the trigger
  is eligible everywhere. Affinity is a preference, not a guarantee, so verify the id is spelled right.
- **A live but saturated node still holds its pin.** If the pinned node is up but its
  [execution group](execution-groups.md) is at its limit, the trigger waits for that node rather than
  moving. Failover reacts to node death, not to node busyness.
- **Brief spread during failover.** Between a node dying and ownership settling, a fast-firing trigger
  may run on more than one surviving node before converging.

## Schema

`PREFERRED_NODE` and `PREFERRED_NODE_AUTO` are part of the 4.x `QRTZ_TRIGGERS` schema. Upgrading from
3.x, apply the script for your database in [`database/migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0);
if you already ran 3.19's optional node-affinity migration the columns exist and no data migration is
needed — the two versions store pins identically. See [Database Schema Changes](../../database/schema-changes.md#version-4-0).
