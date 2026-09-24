---
title: 'Node Affinity (Preferred Node)'
---

Node affinity sets **which cluster node runs a trigger**. Use it for a job that keeps in-memory state
between runs, such as a cache or a warmed-up connection, and should stay on one node.

[Execution Groups](execution-groups.md) limit *how many* threads a category of job may use on a node.
The two combine: affinity decides *where* a trigger runs, execution groups *how much* of a node it uses.

## Concepts

A **preferred node** is an optional property of a **trigger** naming the scheduler instance that should
acquire it. It is set per trigger, not per job, so for job-level affinity set the same value on all of
a job's triggers.

| Value | Effect |
|---|---|
| a scheduler instance id (e.g. `"node-1"`, matching `quartz.scheduler.instanceId`) | pins the trigger to that node |
| `"*"` | **auto-pin**: the first node to fire the trigger claims it |
| `null` (default) | no preference; standard Quartz behavior |

A preferred node is a **strong preference with automatic failover**, not a hard constraint. Acquisition
skips triggers pinned to *live* nodes, but if the pinned node stops checking in, other nodes take over.
See [Failover behavior](#failover-behavior).

Two columns on `QRTZ_TRIGGERS` store it:

| `PREFERRED_NODE` | `PREFERRED_NODE_AUTO` | Meaning |
|---|---|---|
| `NULL` | false | No affinity (default) |
| `'*'` | false | Auto-pin requested, not yet claimed |
| `'node-1'` | true | Auto-claimed by `node-1` |
| `'node-1'` | false | Explicit pin to `node-1` |

The node name is stored verbatim and the auto-claim separately, so **no instance id is reserved**: a
node may be called `auto:thing` or `*-west`.

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

Read it back by casting to `AbstractTrigger`. In 3.x the properties are there, not on `ITrigger`,
which cannot gain members without a breaking change:

```csharp
ITrigger t = await scheduler.GetTrigger(new TriggerKey("myTrigger"));
var at = (AbstractTrigger) t;
string? node = at.PreferredNode;      // "production-node-1"
bool auto = at.IsPreferredNodeAuto;   // false for an explicit pin
```

::: warning
The value must match the instance id **exactly**. Pins are compared in SQL with the database's string
collation, so a value differing only in case is a different node, and on a case-sensitive database it
never matches.
:::

## Auto-pin mode

When a trigger's preferred node is `"*"`:

1. Any node can acquire the trigger, as usual.
2. The first node to fire it writes its own instance id to `PREFERRED_NODE` and sets
   `PREFERRED_NODE_AUTO`. The write is a compare-and-swap against the value seen at acquisition, so a
   concurrent re-pin or clear wins over the claim.
3. From then on only that node acquires the trigger, until it stops checking in.

Use it when you do not know node names at configuration time but want a trigger to stay put.

Rebuilding an auto-pinned trigger keeps the auto-claim:

```csharp
// The rebuilt trigger is still auto-pinned, so it will still fail over if that node dies
ITrigger rebuilt = trigger.GetTriggerBuilder().WithDescription("updated").Build();
```

Assigning `PreferredNode` directly always records an **explicit** pin:

```csharp
trigger.PreferredNode = "node-2";   // explicit pin; IsPreferredNodeAuto becomes false
```

## Failover behavior

When the preferred node stops checking in:

1. **Acquisition.** The acquisition query treats a node whose last check-in is older than the cluster
   check-in threshold as dead, so surviving nodes can acquire its pinned triggers at once, without
   waiting for cluster recovery.
2. **Steal on fire.** A node that fires a trigger still auto-claimed by a stale node takes over the pin
   by compare-and-swap. Affinity converges on a live node instead of bouncing.
3. **Cluster recovery.** When recovery confirms a node dead, its auto-claimed pins are reset to `"*"`
   before its state row is deleted. Any *eligible* node can then claim them, which respects execution
   group limits.
4. **Explicit pins are kept.** They are never re-pinned. While the node is down other nodes run the
   trigger; when it checks in again, it reclaims it.

## Updating the preferred node at runtime

Re-pin without rescheduling:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode("node-2"));
```

Pass `null` to clear the preference:

```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode(null));
```

## Requirements and limitations

- **Clustering and a stable instance id.** Affinity works only with `quartz.jobStore.clustered = true`
  and a *stable* `quartz.scheduler.instanceId`. With `AUTO`, the id changes on every restart and a
  stored pin names a node that no longer exists. Quartz warns at startup when it detects an
  auto-generated id.
- **RAMJobStore ignores it.** A pin is stored and returned as metadata but never filters acquisition;
  a single-node in-memory scheduler always runs the trigger.
- **Custom driver delegates.** If a delegate customizes trigger acquisition (overriding
  `SelectTriggerToAcquire` or the `GetSelectNextTriggerToAcquire*Sql` builders) without extending the
  preferred-node variants, Quartz keeps acquisition on the delegate's own path and logs a warning. The
  custom SQL keeps working, without SQL-level affinity filtering.
- **A node that never registers.** If the target instance id has never checked in, the trigger is
  eligible everywhere. Check the id's spelling.
- **A live but saturated node keeps its pin.** If the pinned node is up but its
  [execution group](execution-groups.md) is at its limit, the trigger waits for that node. Failover
  reacts to node death, not to load.
- **Brief spread during failover.** Between a node dying and ownership settling, a fast-firing trigger
  may run on more than one surviving node.

## Schema

`PREFERRED_NODE` and `PREFERRED_NODE_AUTO` are **optional** columns on `QRTZ_TRIGGERS` in 3.x. Add both
together with the script for your database in [`database/migrations/3.19/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.19).
See [Database Schema Changes](../../database/schema-changes.md#version-3-19).

Quartz probes for them at startup. Without them the feature is unavailable: the scheduler logs a
warning, pins are ignored, and everything else works as before, so upgrading the assembly without
touching the database is safe. If the database is unreachable at startup, the probe retries on the
first successful operation.

Both columns are mandatory in 4.x, which stores pins the same way, so upgrading needs no data
migration.
