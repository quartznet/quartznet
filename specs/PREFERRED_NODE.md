# Preferred Node (Node Affinity) Specification

Implementation of [quartznet/quartznet#1615](https://github.com/quartznet/quartznet/issues/1615) for the 3.x branch.

## 1. Problem Statement

In a Quartz.NET cluster, any node can acquire any trigger. There is no mechanism to control which
node runs a given job. The issue requests the ability to pin a job to a specific node so that:

- Periodic jobs that maintain in-memory caches between runs always execute on the same node.
- If the pinned node fails, another node takes over automatically.
- Optionally, the node can be chosen automatically on first fire rather than specified upfront.

## 2. Design Decisions

### 2.1 Trigger-Level, Not Job-Level

The feature is implemented as a property on triggers (not jobs), consistent with how execution
groups work. A job with multiple triggers can have different preferred nodes per trigger. For
full job-level affinity, set the same preferred node on all triggers for the job.

**Rationale:** Trigger-level is more flexible, consistent with the existing execution groups
pattern, and avoids adding a column to `QRTZ_JOB_DETAILS`.

### 2.2 Naming: `PreferredNode`

The property, database column, and API all use the name `PreferredNode`. This signals that the
feature is a strong preference with automatic failover, not a hard constraint.

### 2.3 Strong Preference, Not Hard Constraint

`PreferredNode` routes triggers to a specific node when that node is live, but allows failover
when it is not. This is intentional: a hard constraint would mean triggers pinned to a typo'd,
decommissioned, or not-yet-started node would be stuck forever.

### 2.4 Failover Behavior

When a node is confirmed dead (by `ClusterRecover` using `CalcFailedIfAfter`):
- **Auto-pinned triggers** (`"auto:"` prefix) are reset to the `"*"` sentinel, allowing any
  eligible node to claim them on the next fire. This respects execution group limits.
- **Explicit pins** (no prefix) are left untouched. The acquisition SQL's `NOT IN` clause
  handles failover. When the original node returns, it naturally reclaims its triggers.

### 2.5 INextVersion* Pattern (3.x Backward Compatibility)

All new interfaces and properties use the internal `INextVersion*` pattern to avoid breaking
public SPI contracts (`IDriverDelegate`, `IJobStore`, `ITrigger`). These will be promoted to
the public interfaces in 4.x.

### 2.6 Optional Database Column

The `PREFERRED_NODE` column on `QRTZ_TRIGGERS` is optional. The delegate probes for its
existence during `Initialize()`. When absent, preferred node functionality is silently disabled.

## 3. Implementation Architecture

### 3.1 Data Flow

```
TriggerBuilder.WithPreferredNode("nodeA")
  --> INextVersionTrigger.PreferredNode (on AbstractTrigger)
    --> StdAdoDelegate.InsertTrigger() writes PREFERRED_NODE column
      --> SQL acquisition query filters by PREFERRED_NODE using REPLACE + NOT IN subquery
        --> TriggerFired: auto-pin "*" --> writes "auto:" + InstanceId
        --> ClusterRecover: dead node --> batch UPDATE re-pins only "auto:" prefixed triggers
```

### 3.2 Acquisition SQL Filter

The trigger acquisition query includes a WHERE clause that filters triggers at the SQL level:

```sql
AND (t.PREFERRED_NODE IS NULL
     OR t.PREFERRED_NODE = @instanceId
     OR t.PREFERRED_NODE = @autoInstanceId
     OR t.PREFERRED_NODE = @autoPinSentinel
     OR REPLACE(t.PREFERRED_NODE, 'auto:', '') NOT IN (
         SELECT ss.INSTANCE_NAME FROM QRTZ_SCHEDULER_STATE ss
         WHERE ss.SCHED_NAME = @schedulerName
         AND ss.LAST_CHECKIN_TIME + ss.CHECKIN_INTERVAL * 10000 >= @liveNodeCutoff))
```

This returns triggers where:
1. No preferred node (null) -- any node can acquire
2. Preferred node matches this instance (explicit pin) -- pinned to us
3. Auto-pin matches this instance (`"auto:" + instanceId`) -- auto-pinned to us
4. Auto-pin sentinel (`"*"`) -- any node can claim on first fire
5. Preferred node (with `"auto:"` prefix stripped) is NOT in the set of live nodes -- failover

Two query variants exist: one including both `EXECUTION_GROUP` and `PREFERRED_NODE` columns,
and one with `PREFERRED_NODE` only (for deployments that applied only the preferred node migration).

### 3.3 Auto-Pin Mechanism

When `PreferredNode = "*"`, the trigger is in auto-pin mode. During `TriggerFired()`, after the
trigger has been confirmed as ACQUIRED, the firing node writes `"auto:" + InstanceId` to the
`PREFERRED_NODE` column (e.g., `"auto:nodeA"`). The `"auto:"` prefix distinguishes auto-pinned
triggers from explicit pins. Subsequent fires are routed to that node.

The `"auto:"` prefix is reserved — `TriggerBuilder.WithPreferredNode()` and the
`AbstractTrigger.PreferredNode` setter reject values containing `"auto:"` (case-insensitive).
Internal writes (auto-pin in `TriggerFired`, DB reads) use `SetPreferredNodeRaw()` to bypass
validation. Scheduler instance IDs containing `"auto:"` are rejected at startup for clustered
schedulers.

`GetTriggerBuilder()` on an auto-pinned trigger strips the `"auto:"` prefix and emits the
plain node name as an explicit pin (e.g., `"auto:nodeA"` becomes `"nodeA"`). This preserves
node affinity through clone/reschedule flows but converts auto-pin semantics to explicit pin
(the trigger will not be auto-re-pinned on failover). Users who want auto-pin behavior on
the rebuilt trigger should explicitly set `"*"`.

### 3.4 Sticky Failover via ClusterRecover

When `ClusterRecover()` processes a dead node:
1. `FindFailedInstances()` confirms the node is dead via `CalcFailedIfAfter()`
2. Before deleting the dead node's `SCHEDULER_STATE` row, a batch `UPDATE` resets
   **auto-pinned** triggers (`PREFERRED_NODE = 'auto:' + deadNodeId`) back to the `"*"` sentinel.
   This allows any eligible node to claim the trigger on the next fire, which correctly
   handles execution group limits (avoids re-pinning to a node that can't run the trigger).
3. **Explicit pins** (`PREFERRED_NODE = deadNodeId` without prefix) are left untouched —
   the acquisition SQL's `REPLACE + NOT IN` clause handles failover. When the original node
   returns, it naturally reclaims its explicitly pinned triggers.
4. The state row is then deleted

This approach:
- Uses the authoritative failure detector (`CalcFailedIfAfter`)
- Runs before the state row is deleted (no race)
- Resets auto-pinned triggers in a single SQL UPDATE (O(1) regardless of trigger count)
- Respects execution group limits — the first eligible node claims the trigger
- Preserves explicit pins through failover for automatic recovery when the original node returns
- Does not add work to the `TriggerFired` hot path

### 3.5 Column Probing

Optional columns (`MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE`) are probed
during `Initialize()` using a `SELECT column FROM table WHERE 1 = 0` query. If the column
doesn't exist, the probe fails silently and the feature is disabled.

### 3.6 JSON Serialization

Both `Quartz.Serialization.SystemTextJson` and `Quartz.Serialization.Json` TriggerConverters
serialize/deserialize `ExecutionGroup` and `PreferredNode`. Backward-compatible: missing
properties during deserialization default to null.

### 3.7 RAMJobStore

RAMJobStore does not implement preferred node filtering. It is inherently single-node, so the
concept is meaningless. The `PreferredNode` property is preserved on the trigger object in
memory but has no effect on acquisition. `TriggerDetailsUpdate.WithPreferredNode()` works
for metadata storage.

## 4. Files Modified

### New Files
| File | Purpose |
|------|---------|
| `database/schema_30_add_preferred_node.sql` | Migration script for all databases |
| `docs/documentation/quartz-3.x/tutorial/node-affinity.md` | User documentation |
| `src/Quartz.Tests.Unit/NodeAffinityTest.cs` | 13 unit tests |
| `src/Quartz.Tests.Integration/.../PreferredNodeClusterTest.cs` | 5 SQLite integration tests |
| `src/Quartz.Tests.Integration/.../PreferredNodeClusteredPostgresTest.cs` | 3 PostgreSQL clustered tests |

### Modified Files (Core)
| File | Changes |
|------|---------|
| `AdoConstants.cs` | `ColumnPreferredNode` constant |
| `IOperableTrigger.cs` | `INextVersionTrigger.PreferredNode` property |
| `AbstractTrigger.cs` | Backing field, property, `GetTriggerBuilder()` |
| `TriggerBuilder.cs` | `WithPreferredNode()` fluent method, `Build()` |
| `TriggerDetailsUpdate.cs` | `WithPreferredNode()` for runtime updates |
| `IDriverDelegate.cs` | `INextVersionDelegate` extensions, `TriggerAcquireResult.PreferredNode` |
| `StdAdoConstants.cs` | SQL probe, read, update, acquisition queries, re-pin batch UPDATE |
| `StdAdoDelegate.Triggers.cs` | Probing, read/write, insert/update, acquisition overloads |
| `JobStoreSupport.cs` | Column probing in `Initialize()`, acquisition with SQL filter, auto-pin in `TriggerFired`, sticky failover in `ClusterRecover` |
| `RAMJobStore.cs` | `TriggerDetailsUpdate` pass-through only |

### Modified Files (DB Delegates)
`SqlServerDelegate.cs`, `PostgreSQLDelegate.cs`, `MySQLDelegate.cs`, `OracleDelegate.cs`,
`SQLiteDelegate.cs`, `FirebirdDelegate.cs` -- each adds two `GetSelect...Sql()` overrides.

### Modified Files (Schema)
All 8 `database/tables/tables_*.sql` files -- add `PREFERRED_NODE` column to `QRTZ_TRIGGERS`.

### Modified Files (Serialization)
`Quartz.Serialization.SystemTextJson/Converters/TriggerConverter.cs`,
`Quartz.Serialization.Json/Converters/TriggerConverter.cs` -- read/write `ExecutionGroup`
and `PreferredNode`.

## 5. Known Limitations and Trade-Offs

### 5.1 SQL Liveness Check vs CalcFailedIfAfter (Low — Resolved)

**Finding:** The acquisition SQL uses `LAST_CHECKIN_TIME + CHECKIN_INTERVAL * 10000 >= @liveNodeCutoff`
to determine node liveness, while `CalcFailedIfAfter()` uses
`max(checkinInterval, timeSinceOurLastCheckin) + threshold`.

**Analysis:** The SQL simplifies to: node is live if `now - lastCheckin <= interval + misfireThreshold`.
For a **healthy acquiring node** (where `timeSinceOurLastCheckin < checkinInterval`),
`CalcFailedIfAfter` produces the identical formula: `failed if now - checkinTimestamp > interval + threshold`.
The formulas only diverge when the **acquiring node itself** is unhealthy (its own checkins
are late), in which case `CalcFailedIfAfter` becomes MORE lenient while the SQL stays fixed.

**Impact:** Being more aggressive (the SQL direction) when the acquiring node is itself
struggling is the **safer** direction — it prevents triggers pinned to a dead node from being
stuck when surviving nodes are under load. Temporary acquisition before `ClusterRecover`
confirms death is bounded by the cluster check-in interval.

**Why not use CalcFailedIfAfter in SQL:** `CalcFailedIfAfter` uses `max(interval, timeSinceOurLastCheckin)`
which depends on the acquiring node's own checkin state — not expressible in portable SQL.
The equivalent-for-healthy-nodes simplification is intentional.

### 5.2 Manual Pinning is a Strong Preference, Not a Hard Constraint (High)

**Finding:** The SQL `NOT IN` subquery treats nodes absent from `SCHEDULER_STATE` as dead,
allowing acquisition. This means a trigger pinned to a node that hasn't started yet, or whose
name is typoed, can run on another node.

**Impact:** Falls short of a strict "run only here" guarantee for manual pinning.

**Mitigation:** `TriggerFired` does not re-pin for explicit pins when the preferred node
has no `SCHEDULER_STATE` row (startup-order race). The pin is preserved, so the intended
node receives subsequent fires once it starts. For typoed names, the trigger runs on
whichever node acquires it -- same as not having a preferred node at all.

**Why not make it a hard constraint:** A hard constraint would mean triggers pinned to a
decommissioned, typoed, or not-yet-started node would be stuck forever and never fire.
The "strong preference with failover" design prioritizes liveness over strict placement.

### 5.3 Failover and Explicit Pins (Resolved)

**Design:** `ClusterRecover` only re-pins **auto-pinned** triggers (those stored with the
`"auto:"` prefix). Explicit pins are left untouched. The acquisition SQL's `REPLACE + NOT IN`
clause handles failover for explicit pins — any surviving node can acquire the trigger while
the preferred node is down. When the original node returns and registers in `SCHEDULER_STATE`,
it naturally reclaims its explicitly pinned triggers.

**Upgrade note:** Pre-upgrade auto-pinned triggers have `PREFERRED_NODE = "nodeA"` (no prefix).
After upgrading, these behave like explicit pins — they are not re-pinned during failover but
work correctly via `NOT IN` fallback. To restore auto-pin re-pin behavior, update to `"*"`.

### 5.4 Multi-Survivor Bounce Window (Medium)

**Finding:** Between a node dying and `ClusterRecover` running, a fast-firing trigger
can execute on multiple surviving nodes before the batch re-pin runs.

**Impact:** In a 3+ node cluster, a few fires of a repeating trigger may be distributed
before ownership settles. Bounded by the cluster check-in interval.

**Mitigation:** After `ClusterRecover` runs, the trigger is pinned to a single node.
The bounce window is at most one check-in interval (default 7.5s).

### 5.5 Read API Requires Downcast (Low)

**Finding:** `PreferredNode` is writable via `TriggerBuilder.WithPreferredNode()` and
`TriggerDetailsUpdate.WithPreferredNode()`, but readable only through the internal
`INextVersionTrigger` or concrete `AbstractTrigger`. Callers retrieving an `ITrigger`
must downcast. Third-party trigger implementations cannot opt in unless they inherit
`AbstractTrigger`.

**Rationale:** This is inherent to the `INextVersion*` pattern used for 3.x backward
compatibility. In 4.x, `PreferredNode` will be promoted to `ITrigger`, eliminating the
downcast requirement and enabling third-party implementations.

### 5.6 Extra SELECTs in Acquisition Path (Low)

**Finding:** `SelectTrigger()` (called by `RetrieveTrigger` during acquisition) performs
separate `SELECT` queries to read `ExecutionGroup` and `PreferredNode`, even though the
acquisition SQL already fetched `PREFERRED_NODE`.

**Impact:** Two extra scalar SELECTs per trigger per acquisition cycle.

**Rationale:** Pre-existing pattern shared with `ExecutionGroup`. `SelectTrigger` is also
called from non-acquisition paths (e.g., `GetTrigger()` API) where the acquisition result
isn't available. Optimizing by including optional columns in the main `SelectTrigger` SQL
requires changing `IDriverDelegate`, which is planned for 4.x.

## 6. Original Plan

### 6.1 Context and Motivation

GitHub issue #1615 requests control over which cluster node runs a job. The use case: in-memory
caching between job runs requires the job to consistently execute on the same node. In a
standard Quartz cluster, any node can acquire any trigger -- there is no placement control.

### 6.2 Design Choices Made During Planning

| Decision | Choice | Rationale |
|----------|--------|-----------|
| Scope | Trigger-level | Consistent with execution groups; more flexible than job-level |
| Failover mode | Sticky failover | Re-pin to failover node permanently (approved by maintainer) |
| Naming | `PreferredNode` | Signals preference with failover, not hard constraint |
| 3.x compatibility | `INextVersion*` pattern | No breaking changes to public SPI |
| RAMJobStore | No changes | Single-node store; filtering is meaningless |
| DB column | Optional, probed at startup | Same pattern as `EXECUTION_GROUP` |

### 6.3 Evolution During Review

The implementation went through several iterations based on review feedback:

1. **Initial:** Application-level filtering after `RetrieveTrigger`.
   Problem: batch starvation when many triggers pinned to other live nodes.

2. **SQL-level filter added:** `NOT IN` subquery against `SCHEDULER_STATE`.
   Problem: simple presence check didn't detect stale nodes.

3. **Checkin-time-aware subquery:** Added `LAST_CHECKIN_TIME + CHECKIN_INTERVAL * 10000 >= @liveNodeCutoff`.
   Problem: differs from `CalcFailedIfAfter` (accepted trade-off).

4. **Sticky failover in TriggerFired:** Re-pin during trigger firing using `CalcFailedIfAfter`.
   Problem: `ClusterRecover` deletes the state row before `TriggerFired` runs.

5. **Final: Sticky failover in ClusterRecover:** Batch `UPDATE` during cluster recovery,
   before the state row is deleted. Auto-pin stays in `TriggerFired`.

6. **Column probing moved to Initialize():** Ensures triggers scheduled before `Start()` persist.

7. **JSON serializers updated:** Both SystemTextJson and Newtonsoft.Json converters now
   round-trip `ExecutionGroup` and `PreferredNode`.

### 6.4 What Changes for 4.x Port

When porting to `main` (4.x branch) where breaking changes are allowed:

- `PreferredNode` moves from `INextVersionTrigger` to `ITrigger` (public interface)
- Delegate methods move from `INextVersionDelegate` to `IDriverDelegate`
- `PREFERRED_NODE` column becomes mandatory in schema (not optional)
- Column probing for preferred node removed
- Optional columns included in main `SelectTrigger` SQL (eliminates extra SELECTs)
- `docs/documentation/quartz-4.x/tutorial/node-affinity.md` created

## 7. Test Coverage

### 7.1 Unit Tests (`NodeAffinityTest.cs`)

13 tests covering:
- `TriggerBuilder.WithPreferredNode()` set, clear (null/empty/whitespace), trim, round-trip
- Auto-pin sentinel `"*"` accepted
- Combined with `ExecutionGroup`
- `TriggerDetailsUpdate.WithPreferredNode()` has-flag behavior
- `AbstractTrigger.PreferredNode` set/get and clone

### 7.2 Integration Tests -- SQLite (`PreferredNodeClusterTest.cs`)

5 tests covering ADO.NET persistence (non-clustered, SQLite):
- Manual pin round-trip
- Auto-pin on first fire
- `UpdateTriggerDetails` set and clear
- Combined preferred node + execution group
- Pre-start scheduling persistence (`ScheduleJob` before `Start()`)

### 7.3 Integration Tests -- PostgreSQL (`PreferredNodeClusteredPostgresTest.cs`)

3 tests covering real 2-node clustered behavior (requires Docker/Testcontainers):
- `AutoPin_PinsToFirstFiringNode`: fire trigger with `"*"`, verify re-pinned to firing node,
  `RecordingJob` proves which instance executed
- `LiveNodePin_ExecutesOnPinnedNode`: two live nodes, repeating trigger pinned to nodeA,
  assert ALL recorded executions are on nodeA (proves nodeB never stole it)
- `Failover_DeadNodeTriggerRepinnedToSurvivor`: kill nodeA, wait for stale checkin, start
  nodeB, verify `PREFERRED_NODE` re-pinned to nodeB AND `RecordingJob` recorded nodeB execution

### 7.4 Smoke Test (`SmokeTestPerformer.TestPreferredNode`)

Round-trip persistence test that runs against all database backends in CI (PostgreSQL,
SQL Server, MySQL, Oracle, Firebird, SQLite).

## 8. Database Migration

### 8.1 Migration Script

`database/schema_30_add_preferred_node.sql` provides commented-out `ALTER TABLE` statements
for all supported databases. Only the `QRTZ_TRIGGERS` table needs the column.

### 8.2 Column Specification

```
Column: PREFERRED_NODE
Type:   VARCHAR(250) / NVARCHAR(250) NULL (wider than INSTANCE_NAME to accommodate "auto:" prefix)
Table:  QRTZ_TRIGGERS only (not QRTZ_FIRED_TRIGGERS)
```

### 8.3 No Column on QRTZ_FIRED_TRIGGERS

The existing `INSTANCE_NAME` column on `QRTZ_FIRED_TRIGGERS` already tracks which node
fired a trigger. Adding `PREFERRED_NODE` there would be redundant.
