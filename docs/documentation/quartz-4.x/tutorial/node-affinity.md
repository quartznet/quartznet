---
title: 'Node Affinity (Preferred Node)'
---

Node affinity controls **which cluster node runs a specific trigger**. Use it when a job keeps in-memory
state between runs, such as a cache or a warmed-up connection, and should keep running on the same node.
Affinity decides *where* a trigger runs; [Execution Groups](execution-groups.md) decide *how many*
threads of a node a category of job may use. The two compose.

## Concepts

A **preferred node** is an optional property of a **trigger** naming the scheduler instance that should
acquire it. It is per trigger, so for job-level affinity set the same value on all of a job's triggers.

`ITrigger.PreferredNode` is a `PreferredNode` value:

| Value | Means |
|---|---|
| `PreferredNode.For("node-1")` | pinned to that scheduler instance id (the node's `Scheduler:InstanceId`) |
| `PreferredNode.Auto` | **auto-pin**: the first node to fire the trigger claims it |
| `PreferredNode.None` (the default) | no preference; standard Quartz behavior |

A preferred node is a **strong preference with automatic failover**, not a hard constraint. Acquisition
skips triggers pinned to *live* nodes; if the pinned node is not checking in, other nodes take over. See
[Failover behavior](#failover-behavior).

Two columns on `QRTZ_TRIGGERS` store it:

| `PREFERRED_NODE` | `PREFERRED_NODE_AUTO` | Meaning |
|---|---|---|
| `NULL` | false | No affinity (`PreferredNode.None`, the default) |
| `'*'` | false | Auto-pin requested, not yet claimed (`PreferredNode.Auto`) |
| `'node-1'` | true | Auto-claimed by `node-1` |
| `'node-1'` | false | Named pin to `node-1` (`PreferredNode.For("node-1")`) |

The node name is stored verbatim and the auto-claim separately, so **no instance id is reserved**: a
node may be called `auto:thing` or `*-west`. `PreferredNode.For` refuses only the protocol's markers
(`*`, `_`, `null`).

## Setting the preferred node

Use `TriggerBuilder.WithPreferredNode()`:

<!-- snippet: sample_node_affinity_pin -->
```csharp
// Pin to a specific node
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode(PreferredNode.For("production-node-1"))
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```
<!-- endSnippet -->

<!-- snippet: sample_node_affinity_auto_pin -->
```csharp
// Auto-pin: the first node to fire it claims it
ITrigger trigger = TriggerBuilder.Create()
    .WithIdentity("myTrigger")
    .ForJob(job)
    .WithPreferredNode(PreferredNode.Auto)
    .WithCronSchedule("0 0/5 * * * ?")
    .Build();
```
<!-- endSnippet -->

Read it back from any `ITrigger`:

<!-- snippet: sample_node_affinity_reading_the_pin -->
```csharp
ITrigger? t = await scheduler.GetTrigger(new TriggerKey("myTrigger"));
PreferredNode pin = t!.PreferredNode;   // GetTrigger returns null when there is no such trigger
string? node = pin.Node;         // "production-node-1"; null when unpinned or an unclaimed auto-pin
bool auto = pin.IsAutomatic;     // false for a pin you named
bool unpinned = pin.IsNone;
```
<!-- endSnippet -->

::: warning
The value must match the instance id **exactly**. Pins are compared in SQL with the database's string
collation, so a value differing only in case is a different node, and on a case-sensitive database it
never matches.
:::

### In a scheduling file

A deployment-specific scheduling file can set a pin. In JSON (the `Quartz:Schedule` section of
`appsettings.json`, or a standalone `quartz_jobs.json`) it is a
[common trigger field](../configuration/json.md#common-trigger-fields):

```json
{
  "Name": "nightlyReport",
  "JobName": "reportJob",
  "PreferredNode": "production-node-1",
  "Cron": { "Expression": "0 0 2 * * ?" }
}
```

In `quartz_jobs.xml` it is an optional `<preferred-node>` element, after `<retry-policy>` and before
`<job-data-map>`:

```xml
<trigger>
  <cron>
    <name>nightlyReport</name>
    <job-name>reportJob</job-name>
    <preferred-node>production-node-1</preferred-node>
    <cron-expression>0 0 2 * * ?</cron-expression>
  </cron>
</trigger>
```

* Both formats spell `*` for [an automatic pin](#auto-pin-mode).
* Both leave the trigger unpinned when the value is absent, so re-reading a file clears a pin set some
  other way, as it clears an execution group.
* A reserved node name is refused when the file is read, naming the trigger.

::: warning
A file naming a node that this cluster does not have pins the trigger to a node that is never live, so
any node fires it and the pin does nothing. Name a node only in a file that belongs to one deployment;
use `*` in a file read by more than one.
:::

## Auto-pin mode

With `PreferredNode.Auto`:

1. Any node can acquire the trigger, as usual.
2. The first node to fire it writes its own instance id to `PREFERRED_NODE` and sets
   `PREFERRED_NODE_AUTO`. The write is a compare-and-swap against the value seen at acquisition, so a
   concurrent re-pin or clear wins over the claim.
3. From then on only that node acquires the trigger, until it stops checking in.

Use it when you do not know node names at configuration time but want a trigger to stay put.

Rebuilding an auto-pinned trigger keeps the auto-claim:

<!-- snippet: sample_node_affinity_rebuild -->
```csharp
// The rebuilt trigger is still auto-pinned, so it will still fail over if that node dies
ITrigger rebuilt = trigger.GetTriggerBuilder().WithDescription("updated").Build();
```
<!-- endSnippet -->

The pin carries its own auto-claim flag, so a pin moved to another trigger arrives unchanged:

<!-- snippet: sample_node_affinity_rebuild_with_new_pin -->
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
<!-- endSnippet -->

`ITrigger.PreferredNode` is read-only, like the rest of a trigger. To change it, rebuild the trigger and
pass it to `IScheduler.RescheduleJob`, or
[update the trigger in place](#updating-the-preferred-node-at-runtime).

## Failover behavior

When the preferred node stops checking in:

1. **Acquisition.** A node whose last check-in is older than the cluster check-in threshold is treated
   as dead, so surviving nodes can acquire its pinned triggers immediately, without waiting for cluster
   recovery.
2. **Steal on fire.** A node that fires a trigger still auto-claimed by a stale node takes the pin over
   by compare-and-swap, so affinity moves to a live node instead of bouncing.
3. **Cluster recovery.** When recovery confirms a node dead, its auto-claimed pins are reset to unclaimed
   auto-pins before its state row is deleted. Any *eligible* node can then claim them, respecting
   execution group limits.
4. **Named pins are kept.** They are never re-pinned. Other nodes run the trigger while the node is down;
   when it checks in again, it takes the trigger back.

## Updating the preferred node at runtime

Re-pin without rescheduling:

<!-- snippet: sample_node_affinity_move_pin -->
```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.For("node-2")));
```
<!-- endSnippet -->

Pass `PreferredNode.None` to clear the preference:

<!-- snippet: sample_node_affinity_clear_pin -->
```csharp
await scheduler.UpdateTriggerDetails(
    new TriggerKey("myTrigger"),
    new TriggerDetailsUpdate().WithPreferredNode(PreferredNode.None));
```
<!-- endSnippet -->

## Requirements and limitations

* **Clustering and a stable instance id.** Affinity only applies in a cluster (`store.UseClustering()`,
  see [Clustering](advanced-enterprise-features.md)) with a *stable* `Scheduler:InstanceId`. With
  `GenerateInstanceId = true` the id changes on every restart, so a stored pin names a node that no longer
  exists. Quartz warns at startup when it detects an auto-generated id.
* **RAMJobStore ignores it.** The pin is stored and returned but never filters acquisition; a single-node
  in-memory scheduler always runs the trigger.
* **Pinned to a node that never registers.** If the target instance id has never checked in, the trigger
  is eligible everywhere. Affinity is a preference, not a guarantee; check the id's spelling.
* **A live but saturated node keeps its pin.** If the pinned node is up but its
  [execution group](execution-groups.md) is at its limit, the trigger waits for that node. Failover
  reacts to node death, not to busyness.
* **Brief spread during failover.** Between a node dying and ownership moving, a fast-firing trigger may
  run on more than one surviving node.

## Schema

`PREFERRED_NODE` and `PREFERRED_NODE_AUTO` are part of the 4.x `QRTZ_TRIGGERS` schema. When upgrading
from 3.x, apply the script for your database in
[`database/migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0).
If you already ran 3.19's optional node-affinity migration, the columns exist and no data migration is
needed; both versions store pins identically. See
[Database Schema Changes](../../database/schema-changes.md#version-4-0).
