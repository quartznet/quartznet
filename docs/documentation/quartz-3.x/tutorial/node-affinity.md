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

- A specific scheduler instance id (e.g. `"node-1"`, matching `quartz.scheduler.instanceId`) pins the
  trigger to that node.
- The sentinel `"*"` requests **auto-pin**: the first node to fire the trigger claims it.
- `null` (the default) means no preference — standard Quartz behavior.

Preferred node is a **strong preference with automatic failover**, not a hard constraint. Acquisition
filters out triggers pinned to *live* nodes, but if the pinned node is not currently checking in, other
nodes take over. See [Failover behavior](#failover-behavior).

Two columns back this on `QRTZ_TRIGGERS`:

| `PREFERRED_NODE` | `PREFERRED_NODE_AUTO` | Meaning |
|---|---|---|
| `NULL` | false | No affinity (default) |
| `'*'` | false | Auto-pin requested, not yet claimed |
| `'node-1'` | true | Auto-claimed by `node-1` |
| `'node-1'` | false | Explicit pin to `node-1` |

The node name is stored verbatim and the auto-claim is recorded separately, so **no instance id is
reserved** — a node may legitimately be called `auto:thing` or `*-west` without confusing Quartz.

## Setting the preferred node

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
// Auto-pin: the first node to fire it claims it
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode("*")
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```

Read it back by casting to `AbstractTrigger` (in 3.x the properties live there rather than on
`ITrigger`, which cannot gain members without a breaking change):

```csharp
ITrigger t = await scheduler.GetTrigger(new TriggerKey("myTrigger"));
var at = (AbstractTrigger) t;
string? node = at.PreferredNode;      // "production-node-1"
bool auto = at.IsPreferredNodeAuto;   // false for an explicit pin
```

::: warning
The value must match the instance id **exactly**. Pin comparisons happen in SQL using the database's
string collation, so a value differing only in case is a different node — and on a case-sensitive
database, one that never matches.
:::

## Auto-pin mode

When a trigger's preferred node is `"*"`:

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

Assigning `PreferredNode` directly always records an **explicit** pin, since it expresses your intent
rather than a claim Quartz made:

```csharp
trigger.PreferredNode = "node-2";   // explicit pin; IsPreferredNodeAuto becomes false
```

## Failover behavior

When the preferred node stops checking in:

1. **Acquisition.** The acquisition query treats a node whose last check-in is older than the
   cluster check-in threshold as dead, so surviving nodes may acquire its pinned triggers immediately —
   without waiting for cluster recovery.
2. **Steal on fire.** A node that fires a trigger still auto-claimed by another (stale) node takes the
   pin over via compare-and-swap. Affinity converges on a live node instead of bouncing.
3. **Cluster recovery.** When recovery confirms a node dead, auto-claimed pins belonging to it are reset
   to `"*"` before its state row is deleted, so any *eligible* node can claim them — which correctly
   respects execution group limits.
4. **Explicit pins are preserved.** They are never re-pinned. While the node is down other nodes run the
   trigger; when it returns and checks in again, it naturally reclaims it.

## Updating the preferred node at runtime

You can re-pin without rescheduling:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode("node-2"));
```

Pass `null` to clear the preference entirely:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode(null));
```

## Requirements and limitations

- **Clustering and a stable instance id.** Affinity only means anything with
  `quartz.jobStore.clustered = true` and a *stable* `quartz.scheduler.instanceId`. With `AUTO`, the id
  changes on every restart and a stored pin refers to a node that no longer exists. Quartz warns at
  startup when it detects an auto-generated id.
- **RAMJobStore ignores it.** A pin is stored and returned as metadata but never filters acquisition —
  a single-node in-memory scheduler always runs the trigger.
- **Custom driver delegates.** If a delegate customizes trigger acquisition (by overriding
  `SelectTriggerToAcquire` or the `GetSelectNextTriggerToAcquire*Sql` builders) but does not extend the
  preferred-node variants, Quartz keeps acquisition on that delegate's own path and logs a warning:
  the custom SQL keeps working, but SQL-level affinity filtering is not applied for it.
- **Pinned to a node that never registers.** If the target instance id has never checked in, the trigger
  is eligible everywhere. Affinity is a preference, not a guarantee, so verify the id is spelled right.
- **A live but saturated node still holds its pin.** If the pinned node is up but its
  [execution group](execution-groups.md) is at its limit, the trigger waits for that node rather than
  moving. Failover reacts to node death, not to node busyness.
- **Brief spread during failover.** Between a node dying and ownership settling, a fast-firing trigger
  may run on more than one surviving node before converging.

## Schema

`PREFERRED_NODE` and `PREFERRED_NODE_AUTO` are **optional** columns on `QRTZ_TRIGGERS` in 3.x. Add them
with `database/schema_30_add_preferred_node.sql` (both columns must be added together).

Quartz probes for them at startup. Without them the feature is simply unavailable: the scheduler logs a
warning, pins are ignored, and everything else works exactly as before — so upgrading the assembly
without touching the database is safe. The probe tolerates a database that is temporarily unreachable
at startup and retries on the first successful operation.

Both columns are mandatory in 4.x, which stores pins in exactly the same way — so upgrading needs no
data migration.
