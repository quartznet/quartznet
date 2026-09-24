---
title: 'Operating a Cluster'
---

# Operating a Cluster

For a 4.x scheduler with a clustered ADO.NET store. To configure a cluster, see
[Clustering](tutorial/advanced-enterprise-features.md); for the decisions that shape a schedule, see
[Best Practices](../best-practices.md).

## Rolling a new version through a cluster

### Schema first, then nodes

Quartz.NET never *migrates* its schema. `ProvisionSchema()`
([`JobStore:SchemaProvisioning = CreateIfMissing`](tutorial/job-stores.md#creating-the-schema)) runs the
fresh-install DDL for a database with no Quartz tables, does nothing to one that has them, and never
alters a table. Deploy in this order:

1. **Apply the migrations** from
   [`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations): every
   folder between the database's version and the target, in ascending order.
2. **Replace the nodes**, one at a time.

An old node tolerates a new schema; a new node does not tolerate an old one. The migrations are written
for this:

- Every statement checks before it acts, so a script is a no-op the second time and a partly applied one
  can be re-run. The exception is SQLite's `ADD COLUMN`, which has no conditional DDL.
- They are additive: columns and tables are added, never dropped or narrowed. Indexes are the one thing a
  migration removes; see [A mixed 3.x and 4.0 window](#a-mixed-3-x-and-4-0-window).

**A node refuses to start against a schema it cannot use.** The store runs `SELECT 1` against every table
it needs, and `SELECT <column> … WHERE 1 = 0` for each column 4.x added to a table 3.x already had. A
missing one fails with `SchedulerException: Database schema validation failed`, naming it and the
migration script to run.

- It checks that a column resolves, not its type or width: a wrongly declared hand-built column still
  fails on the first statement that binds it.
- `JobStore:SchemaProvisioning = None` turns the check off. There is no good reason to.
- `CreateIfMissing` does not fill gaps. A database whose Quartz tables 4.x did not create — a needed table
  present but missing a needed column, such as a 3.x schema — is refused, not half-completed. Creating
  only the missing *tables* would give a scheduler that starts, logs itself validated and fires nothing.

::: warning
The fresh-install scripts in [`database/tables/`](https://github.com/quartznet/quartznet/tree/main/database/tables)
are not migrations. **Each drops the existing Quartz schema before recreating it.** The drop switch —
`@DropDb` on SQL Server and MySQL, `DropDb` elsewhere, declared at the top of the file — defaults to
**1**, meaning drop. Set it to `0` for creation only. SQLite has no variables: delete the block between
the `BEGIN DROP TABLES` and `END DROP TABLES` markers.
:::

### Replacing the nodes

Once the schema is ahead of every node, replace them one at a time. As each goes down and comes back:

- **A clean shutdown gives its reservations back.** The scheduling loop is stopped and awaited first.
  Triggers it acquired but had not fired are released to `WAITING` for another node's next pass, and a
  firing it had already committed is dispatched, not dropped. A killed process does neither; what it
  leaves waits for check-in recovery.
- **A shutdown that does not wait still finishes what it can.** Since 4.1 it gives executions in flight a
  couple of seconds to report completion before the job store closes, because a later completion is
  refused and leaves the firing `EXECUTING` with its trigger `BLOCKED`. A job still running when the window
  closes is abandoned, for a peer to recover.
- **A clean shutdown leaves the node's check-in row.** The `QRTZ_SCHEDULER_STATE` row keeps its last
  timestamp until a peer recovers it, so a stopped node is declared *failed* about fifteen seconds later on
  the default settings, exactly like a crash. Its jobs that request recovery run again on another node.
  Use [`WaitForJobsToComplete`](../best-practices.md#shutdown-has-a-deadline) so a node has nothing left to
  recover when it exits.
- **A node that generates its instance id comes back as a different node.** `GenerateInstanceId` uses the
  host name and a timestamp, so each restart gets a new id. The old id's check-in row and fired-trigger
  rows are cleaned up by whichever node notices them. The node's identity in the dashboard, in
  `INSTANCE_NAME` on fired-trigger rows and in a `PREFERRED_NODE` pin does not survive the deployment —
  see [Naming a node in a container](#naming-a-node-in-a-container).

### A mixed 3.x and 4.0 window

Upgrading from 3.x means the [mandatory 4.0 migration](../database/schema-changes.md#version-4-0), then
replacing nodes, so 3.x and 4.0 nodes share one set of tables during the rollout. Scheduling itself holds:
neither version fires a trigger the other took, loses one, or refuses to start because of the other.

| Area | Rule during the window |
|---|---|
| Calendars | write them from 3.x nodes only |
| Serializer | JSON on both, and the same serializer |
| `Dictionary<string, string>` job data on Newtonsoft | write it from 3.x nodes only, or store it as a string |
| `schema_30_to_40_indexes_<db>.sql` | run it after the last 3.x node is gone |
| Triggers with a retry policy | do not reschedule them from a 3.x node |
| Cluster-scoped execution limits, job-type exclusions | treat them as unavailable |
| Paused job groups | pause, resume and schedule into them from one version |

**Why the core holds:**

- **A 3.x node works against the migrated schema.** The migration adds columns and one table and removes
  only optional indexes. 3.x's `INSERT` statements name their columns, so the new
  `PREFERRED_NODE_AUTO NOT NULL DEFAULT 0` takes its default. 3.x probes for `MISFIRE_ORIG_FIRE_TIME`,
  `EXECUTION_GROUP`, `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` at startup and turns those features on.
- **Both use the same stored vocabulary**: trigger states (`WAITING`, `ACQUIRED`, `EXECUTING`, `COMPLETE`,
  `BLOCKED`, `PAUSED`, `PAUSED_BLOCKED`, `ERROR`, `DELETED`), the pause-all marker, trigger-type
  discriminators (`SIMPLE`, `CRON`, `CAL_INT`, `DAILY_I`, `RECUR`, `BLOB`; `RECUR` since 3.18), lock
  names and check-in columns. Job data is the same JSON except one value shape, below.
- **Both use the same coordination code**: the failure predicate (so each judges and recovers the other),
  the acquisition compare-and-swap, the stale-acquired sweep scoped to the sweeping node's own rows, and
  node-affinity pin storage.

**Calendars.** This break costs firings. 4.0 writes `WeeklyCalendar` and `MonthlyCalendar` as day names
and numbers (3.x: a positional boolean array) and `DailyCalendar` with `RangeStart`/`RangeEnd` (3.x:
`RangeStartingTime`/`RangeEndingTime`). 4.0 reads both shapes; 3.x only its own. A clustered store skips
its calendar cache, so a 3.x node fails on every *acquisition pass*.

- Measured on a two-node 3.20 cluster: one `AddCalendar` from a 4.0 node put both 3.x nodes into
  `Couldn't retrieve calendar: Could not deserialize JSON` at about **sixty error lines a second each** —
  roughly 1,630 lines in under a minute — before a backoff cut it to one every twenty seconds. The trigger
  using that calendar fired 18 seconds late. Expect an incident.
- Existing rows are safe; only `AddCalendar` from a 4.0 node does damage.
- Repair: rewrite the calendar from a 3.x process. The errors stop at the write, and misfire handling
  catches up the displaced firings.

**Serializer.** 4.0 refuses `quartz.serializer.type = binary` at startup, so a cluster whose 3.x nodes
wrote binary job data must move to JSON before the rollout.

**String dictionaries on Newtonsoft.** 4.0 writes a `Dictionary<string, string>` value as a plain object,
as System.Text.Json always has; 3.x wrote the Json.NET type name beside it — see
[A string dictionary is written the same way by both serializers](migration-guide.md#a-string-dictionary-is-written-the-same-way-by-both-serializers).
3.x's Newtonsoft reader cannot read 4.0's form and returns a Json.NET `JObject`. Nothing else in job data
differs, and a System.Text.Json cluster is unaffected.

**Indexes.** Run `schema_30_to_40_upgrade_<db>.sql` now: it is additive and safe during the window. Run
`schema_30_to_40_indexes_<db>.sql`, which realigns indexes, once the last 3.x node is gone. It drops:

| Database | What the index file drops that 3.x had |
|---|---|
| SQL Server | Eight: `IDX_QRTZ_T_G_J`, `IDX_QRTZ_T_N_STATE`, `IDX_QRTZ_T_N_G_STATE`, `IDX_QRTZ_T_NEXT_FIRE_TIME`, `IDX_QRTZ_T_NFT_ST_MISFIRE`, `IDX_QRTZ_T_NFT_ST_MISFIRE_GRP`, `IDX_QRTZ_FT_G_J`, `IDX_QRTZ_FT_G_T` |
| MySQL, Oracle, Firebird | Those two misfire indexes and eight more, including `IDX_QRTZ_J_GRP`, `IDX_QRTZ_J_REQ_RECOVERY`, `IDX_QRTZ_T_JG`, `IDX_QRTZ_FT_JG` and `IDX_QRTZ_FT_TG` |
| PostgreSQL, SQLite | Two: `IDX_QRTZ_J_REQ_RECOVERY` and `IDX_QRTZ_T_NEXT_FIRE_TIME`. Neither ever created a misfire index |

- The replacements lead with the columns 4.x's queries use. Where they exist, two dropped indexes are read
  only by 3.x: `IDX_QRTZ_T_NFT_ST_MISFIRE_GRP` serves a 3.x statement with no 4.x counterpart, and 3.x runs
  its misfire sweep from `IDX_QRTZ_T_NFT_ST_MISFIRE`, which 4.0 no longer creates
  ([#3656](https://github.com/quartznet/quartznet/issues/3656)).
- **PostgreSQL and SQLite**: the file drops and recreates `IDX_QRTZ_T_NFT_ST`, which *both* versions
  acquire on; a 3.x node acquiring between the two statements scans the whole trigger table.
- **Firebird** keeps the 3.x acquisition index, so it waits only for the misfire index.
- Running it early breaks nothing: 3.x scans where it used to seek, which on a large schedule can make its
  misfire sweep time out. Both files are guarded and re-runnable.

**Retry policies.** `RETRY_POLICY` and `RETRY_ATTEMPT` are 4.x columns, so a job failing on a 3.x node is
not retried and its attempt count does not advance. 3.x implements `IScheduler.RescheduleJob` as a delete
and an insert that names neither column, so a reschedule from 3.x silently removes the policy. 3.x's
trigger `UPDATE` leaves both columns alone, so pausing, resuming, deleting and firing are safe.

**Execution limits.** A 4.0 node enforces `ExecutionLimitScope.Cluster` (4.x only) by counting
`QRTZ_FIRED_TRIGGERS` rows by `EXECUTION_GROUP`, a column 3.x never writes on a fired trigger. Every 3.x
firing counts as *ungrouped*: a limited group's ceiling misses it and the ungrouped bucket is charged, so a
group you did not limit can be throttled. Per-node limits are unaffected.
[Job-type exclusions](how-tos/custom-job-store.md#excluding-job-types-from-acquisition) are 4.x-only and
per node: a 3.x node runs a job type the 4.0 nodes refuse.

**Paused job groups.** `QRTZ_PAUSED_JOB_GRPS` is 4.x-only. Both versions agree on trigger state, which
decides what fires, so pausing works from either, but the record drifts:

- A group paused by a 3.x node is not recorded: `JobGroup.Paused` on a 4.0 node says `false`.
- A group paused by 4.0 and resumed or cleared by 3.x keeps its row: 4.0 reports it paused indefinitely
  while its triggers run.
- A trigger a **4.0** node stores for a job in a recorded-paused group is born `PAUSED`; one a **3.x** node
  stores is born `WAITING`.

Reconcile the table after the rollout.

#### What has not been established

These findings come from reading both branches. Nothing in this repository tests two versions against one
schema, no release is validated for it, and Java Quartz's documentation does not cover it. The window is
workable under the rules above but not a supported steady state: keep it short.

**Rolling back** works because the migration is additive: a 3.x node starts against the 4.0 schema as is.
Put back the index file's drops by re-running
[`migrations/3.20`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.20), and rewrite
from a 3.x node any calendar a 4.0 node wrote.

## Naming a node in a container

### What the default gives you, and what it does not

`InstanceId` defaults to the literal `NON_CLUSTERED`. A node recognises its own check-in row and firings by
its instance id, so two nodes sharing one act as one member, each treating the other's rows as its own.
Nothing detects this: no node can see what the others were configured with.

A clustered scheduler must derive an id:

```csharp
q.ConfigureScheduler(options =>
{
    options.InstanceName = "orders";
    options.GenerateInstanceId = true;
});
```

- `GenerateInstanceId` runs the registered `IInstanceIdGenerator`; the default returns the host name plus a
  high-resolution timestamp. The equivalent flat key is `quartz.scheduler.instanceId = AUTO`.
- Only a clustered store calls the generator. With clustering off, the id stays `NON_CLUSTERED` whatever
  the setting says.
- The default is **unique but not stable**: the timestamp prevents collisions even between containers
  with the same host name, but every restart is a new identity.

A stable id matters for:

- **[Node affinity](tutorial/node-affinity.md)**: a trigger pinned to an id that no longer exists is pinned
  to nobody.
- **Correlating a node across deployments**: the dashboard's Cluster page,
  `FireInstance.SchedulerInstanceId`, and the `quartz.scheduler.id` attribute on every span and log scope.
- **Reading the check-in table by hand** and expecting yesterday's rows to name the same machines.

### Taking the id from the pod

In Kubernetes, use a **StatefulSet** and the
**[Downward API](https://kubernetes.io/docs/concepts/workloads/pods/downward-api/)**. A
[StatefulSet](https://kubernetes.io/docs/concepts/workloads/controllers/statefulset/) names its pods
`$(statefulset name)-$(ordinal)`, and a pod keeps that name when rescheduled on another node. Inject the
name and use it as the instance id:

```yaml
env:
  - name: POD_NAME
    valueFrom:
      fieldRef:
        fieldPath: metadata.name
```

<!-- snippet: sample_operations_instance_id_from_pod_name -->
```csharp
// POD_NAME comes from the Downward API: fieldRef fieldPath: metadata.name. On a StatefulSet
// that is "<set>-<ordinal>", which the same replica gets back after a restart.
string? podName = Environment.GetEnvironmentVariable("POD_NAME");

services.AddQuartz(q =>
{
    q.ConfigureScheduler(options =>
    {
        options.InstanceName = "orders";

        if (podName is { Length: > 0 })
        {
            options.InstanceId = podName;
        }
        else
        {
            // Nothing injected the pod name — a developer's machine, or a manifest that has
            // not been updated. Fall back to a generated id, which is unique but not stable.
            options.GenerateInstanceId = true;
        }
    });

    q.UsePersistentStore(store =>
    {
        store.UseSqlServer(connectionString);
        store.UseSystemTextJsonSerializer();
        store.UseClustering();
    });
});
```
<!-- endSnippet -->

- Keep the fallback: an outdated manifest, or the image run locally, must not give every replica the id
  `NON_CLUSTERED`.
- Do not use `metadata.uid`. A pod recreated under the same name gets a new UID, which is the churn the pod
  name avoids.
- `quartz.scheduler.instanceId = SYS_PROP` selects a generator that reads the environment variable
  `quartz.scheduler.instanceId`, or the one `quartz.scheduler.instanceIdGenerator.systemPropertyName`
  names. It exists for 3.x configuration files; in 4.x, read the variable in code and assign
  `InstanceId`, which also lets you write the fallback.

### When two pods report the same host name

The dangerous case is a host name that is not unique *and* an id derived from the host name alone.

- Deployment pod names, and so host names, carry a random suffix: unique per pod, new on every restart.
- Two pods report the same name under `hostNetwork: true` (every pod on a node reports the node's host
  name), or when a manifest sets `spec.hostname` to a literal instead of templating it.
- Quartz.NET's default generator survives both, because of the timestamp. The host-name-only generator,
  named through `quartz.scheduler.instanceIdGenerator.type`, returns the host name unchanged; it is meant
  for one scheduler per machine, which `hostNetwork` breaks.

With two members sharing an identity, each node treats the other's fired-trigger rows as its own. A
node's first check-in after a restart recovers firings another node is still executing, and
`[DisallowConcurrentExecution]` stops holding for the jobs that were running.

## Check-in, node states and failover

### What a check-in is

Each node writes a row to `QRTZ_SCHEDULER_STATE` and updates its timestamp every `CheckinInterval`
(7.5 seconds by default). There is no heartbeat between nodes, no leader and no election: one node judges
another by reading the timestamp it wrote and comparing it with its own clock.

- **The first check-in** happens during `Start()`, before firing begins. It treats the node's *own*
  previous row as a failed instance, so whatever the last run left behind is recovered then.
- **Later check-ins** update the timestamp and look for failed peers, taking the cluster-wide locks only
  when there are any.
- **The stored check-in interval is written once.** `CHECKIN_INTERVAL` is set when the row is inserted;
  only `LAST_CHECKIN_TIME` is updated. A node with a stable instance id that changes its `CheckinInterval`
  keeps advertising the old value until its row is deleted and recreated, which happens after a recovery,
  not a restart. When widening the interval across a cluster, expect peers to use the new value only
  after each node has been declared failed once, or delete the rows while the cluster is stopped.
- **Check-in failures are logged sparsely**: one line per `RetryableActionErrorLogThreshold` consecutive
  failures, **4** by default. A database that is down produces a quarter of the lines you might expect.

### When a peer takes over

A node declares a peer failed when this is in the past, on the deciding node's own clock:

```text
  peer's last check-in timestamp
+ max(peer's stored check-in interval, time since this node last checked in)
+ this node's CheckinMisfireThreshold
```

- On the defaults (both intervals 7.5 seconds) that is about fifteen seconds after the peer's last
  timestamp.
- The middle term protects against false verdicts: a node that has been away from its own check-in loop
  for a minute grants every peer a minute of slack, so a database outage does not end with the first node
  back declaring all the others dead.

**The judged node retries inside the window.** A failed check-in is retried in half the remaining time
each attempt, never later than `DbRetryInterval` — on the defaults roughly 11.25, 13.1, 14.1 and 14.5
seconds after its last row. Only after the window closes does it back off `DbRetryInterval` between
attempts. A database blip shorter than the threshold costs a few error lines, not the node's row.

- Before 4.1 one failed check-in slept the full `DbRetryInterval` and wrote the next row 22.5 seconds after
  the last, 7.5 seconds after peers stopped trusting it (#3777).
- Retrying only helps when the failure *reports* inside the window. A connection attempt that hangs for a
  15-second connect timeout has spent the window by the time it fails; that is what the threshold is for.

**Set `CheckinMisfireThreshold` above your environment's worst pause, not its worst clock error.** Fifteen
seconds is shorter than a long garbage-collection pause, than the thirty seconds Azure documents for
memory-preserving maintenance, and than the clock skew of a machine without time synchronisation — see
[Clocks in a cluster](../best-practices.md#clocks-in-a-cluster).

A takeover:

1. releases the failed node's acquired triggers;
2. schedules recovery triggers for its interrupted executions whose jobs requested recovery;
3. deletes the rest of its fired-trigger rows;
4. releases node-affinity pins it claimed automatically;
5. deletes its check-in row.

Recovering a `[DisallowConcurrentExecution]` job is held back on first detection, because the node that
missed a check-in may still be running it. While anything is held back, the failed node's row stays with
its stale timestamp, so it keeps being reported `Failed` instead of disappearing.

### When the node that was taken over is still running

A takeover can be wrong: a stalled process, a paused container or a drifting clock can get a working node
written off. That node finds out at its next check-in, when its `QRTZ_SCHEDULER_STATE` row is gone. It:

- **writes the row back**, which re-registers it. Until then its peers do not know it, and
  `QueryClusterNodes()` on another node does not list it.
- **logs a warning**: `This scheduler instance (…) is still active but was recovered by another instance
  in the cluster` (event id `3501`), then one naming the peer (`3515`) or saying it cannot (`3516`). The
  peer can be named only when it is the only other node with a state row; the schema does not record who
  recovered whom.
- **counts the event** on `quartz.cluster.recovery.trigger` with `quartz.cluster.recovered.instance.id`
  set to its own instance id. Alert on recovered node = reporting node to catch "this node is being
  failed out". It counts 1; the peer's own measurement carries how many firings it took over.
- **does not recover its own fired triggers.** The peer already released, rescheduled and deleted them
  under the trigger-access lock; recovering again would schedule a second recovery trigger for a firing
  already being replayed.

The takeover is still harmful: the peer has started work this node may still be doing, and
`[DisallowConcurrentExecution]` is not honoured across a firing the cluster believes recovered. Fix the
cause, nearly always the clock or a pause — see
[Clock Skew Between Nodes](../troubleshooting.md#clock-skew-between-nodes).

### Reading the cluster

`IScheduler.QueryClusterNodes()` lists the nodes with a verdict on each, from the same predicate the
recovery sweep uses, so the two cannot disagree:

| State | Means |
|---|---|
| `Alive` | Checked in within its own check-in interval. |
| `Overdue` | Has missed a check-in. Normal under load; nothing is recovered from an overdue node. |
| `Failed` | Past the boundary above. The next check-in pass by any node takes its work over and deletes its row, after which it is no longer listed. |

- A healthy failover shows a node as `Failed` briefly, then gone.
- A node that stays `Failed` across several minutes of polling is not being swept: check that another
  node is running and that its cluster manager is not stuck on the database.
- The same listing is `GET {ApiPath}/schedulers/{name}/nodes` in the
  [HTTP API](packages/http-api.md#cluster-nodes), and the Cluster page of the
  [dashboard](packages/dashboard.md), which adds each node's `Acquired` and `Executing` counts.
- `GET {ApiPath}/schedulers` lists every scheduler the process knows, including registrations nothing has
  built, so a scheduler that never started can be told from one that does not exist.

## What the tables are telling you

### Fired triggers: backlog or leak

A `QRTZ_FIRED_TRIGGERS` row is written when a trigger is **acquired**, updated when it **fires**, and
deleted when the firing completes. Healthy: the row count tracks concurrency, and the oldest row is no
older than your longest-running job.

Growth is one of two things; the age of the rows tells them apart:

- **Backlog**: many rows, all young, spread across live nodes. More work arrives than the cluster
  finishes. Add capacity or shrink the schedule; this is not a database problem.
- **Leak**: rows that do not age out.
  - An old `EXECUTING` row is a job that never returned (a synchronous call that hangs, an unawaited task),
    and the node is still holding it.
  - An old `ACQUIRED` row is a trigger reserved and never fired —
    [the stale-acquired case](../troubleshooting.md#triggers-stuck-in-acquired-state), swept
    automatically.
  - A row whose instance id the cluster no longer lists is an orphan. Orphans are swept only on a node's
    *first* check-in, so a cluster up for months has never looked for them.

`IScheduler.QueryFireInstances` answers all of this without SQL, joined to the node listing on the
instance id:

<!-- snippet: sample_operations_stale_firings -->
```csharp
List<ClusterNode> nodes = await scheduler.QueryClusterNodes();
HashSet<string> known = nodes.Select(node => node.InstanceId).ToHashSet(StringComparer.Ordinal);

// State = null lists reservations as well as executions; the default lists executions only.
PagedResult<FireInstance> firings = await scheduler.QueryFireInstances(new FireInstanceQuery
{
    State = null,
    Take = 500
});

DateTimeOffset cutoff = timeProvider.GetUtcNow().AddHours(-1);

foreach (FireInstance firing in firings.Items)
{
    // A row whose node is no longer listed is a leak: no peer will recognise it as its own,
    // and only a node's first check-in sweeps firings with no scheduler-state row behind them.
    if (!known.Contains(firing.SchedulerInstanceId))
    {
        logger.LogWarning(
            "Firing {FireInstanceId} of {Trigger} belongs to {Node}, which the cluster no longer lists.",
            firing.FireInstanceId, firing.TriggerKey, firing.SchedulerInstanceId);
    }
    else if (firing.FireTimeUtc < cutoff)
    {
        logger.LogWarning(
            "Firing {FireInstanceId} of {Trigger} has been {State} on {Node} since {FireTime}.",
            firing.FireInstanceId, firing.TriggerKey, firing.State, firing.SchedulerInstanceId,
            firing.FireTimeUtc);
    }
}
```
<!-- endSnippet -->

To clear a stale `EXECUTING` row of a node that is still alive, restart that node: its first check-in
recovers its own leftovers. Nothing else sweeps a live node's rows; the node is the authority on what it
is running.

### Nothing is firing

Work the store is holding back was never acquired, so it is not in `QRTZ_FIRED_TRIGGERS`. Check, in this
order:

1. **Standby.** `IScheduler.Status` says so. The health check reports *degraded*, not unhealthy, which an
   HTTP probe reads as healthy, because ASP.NET Core maps degraded to 200 — see
   [the Aspire how-to](how-tos/aspire.md).
2. **A paused group.** Pausing is durable across restarts.
   - `QRTZ_PAUSED_TRIGGER_GRPS` holds paused *trigger* groups, and a trigger stored into one is stored
     `PAUSED`.
   - `QRTZ_PAUSED_JOB_GRPS` (new in 4.x) holds paused *job* groups, so `JobGroup.Paused` and
     `GET …/jobs/groups?paused=true` answer correctly and survive a restart. 3.x pauses a job group by
     pausing its jobs' triggers at that moment and records nothing.
   - On 4.x, a trigger stored later for a job in a paused job group is stored `PAUSED`, as it is for a
     paused trigger group. On 3.x, pausing a job group pauses only the triggers it has then, and a job
     added afterwards fires.
   - A group paused during an incident and never resumed is a common, silent cause. Both dashboard
     listings show the flag.
3. **Every trigger blocked or in error.** `BLOCKED`: another firing of the same
   `[DisallowConcurrentExecution]` job is running — see the leak above. `ERROR`: the job could not be
   *built*, a composition-root failure; fix the application, then clear it with
   `ResetTriggerFromErrorState`. See
   [What the trigger states mean](../best-practices.md#what-the-trigger-states-mean).
4. **The wrong tables.** A mistyped `JobStore:TablePrefix` connects to the right database, finds an empty
   table set of its own, passes schema validation because those tables exist, starts, reports healthy and
   fires nothing. 4.x logs a warning for one case — two schedulers in one container sharing a database
   with different prefixes — but not an error, since separate table sets are legitimate. It cannot detect
   the single-scheduler case. Count the rows in the tables the scheduler actually points at.

## Backup and restore

Back up the Quartz tables with the rest of the application's database, on the same schedule and to the
same recovery point; they need no special treatment. The restore needs care, because the tables record
what a distributed system is running.

- **Stop every node before restoring, and start them afterwards.** A live node would otherwise see a store
  older than its own memory of it.
- **A point-in-time restore restores the record of the work, not the work.**
  - Triggers fire again from where the backup left them. A nightly job whose `PREV_FIRE_TIME` rolled back
    runs again that night; [idempotent](../best-practices.md#assume-the-job-will-run-more-than-once) jobs
    do not care.
  - Fired-trigger rows come back for firings that already finished. Until swept, the cluster believes
    those jobs are running and `[DisallowConcurrentExecution]` holds their job keys. Starting the nodes
    clears them: each node's first check-in recovers its own rows, and rows of instance ids that are gone
    are swept as orphans in the same pass.
  - Anything scheduled after the recovery point is gone, including one-off triggers an application created
    in response to something. If those matter, make them re-derivable from their source.
- **Prefer redeploying the schedule to restoring it.** With `AddJob` and `AddTrigger` in `AddQuartz`, or
  a scheduling data file, the schedule is in source control and re-applied on every start.
  `SchedulingOptions.OverwriteExistingData` is on by default, so a start after a restore brings the
  definitions back to what the code says, and the backup only has to cover runtime state.
- **Not needed**: `QRTZ_LOCKS` rows, which the lock handler writes when they are missing. Quartz keeps no
  state outside the database.

## Timeouts and transient failures

### CommandTimeout

`JobStore:CommandTimeout` bounds every statement the store issues, including the lock handler's row lock.
Unset, each statement gets the ADO.NET provider's default, usually 30 seconds. There is no per-statement
override: every statement runs inside a lock the rest of the cluster waits on.

<!-- snippet: sample_operations_store_timeouts -->
```csharp
q.UsePersistentStore(store =>
{
    store.UseSqlServer(connectionString);
    store.UseSystemTextJsonSerializer();
    store.UseClustering();

    store.ConfigureStore(options =>
    {
        // Every statement the store issues, the lock handler's included. Left unset it is
        // whatever the provider gives a new command, usually 30 seconds.
        options.CommandTimeout = TimeSpan.FromSeconds(15);

        // A deadlock or a dropped connection is retried this many times, this far apart.
        options.MaxTransientRetries = 3;
        options.TransientRetryInterval = TimeSpan.FromSeconds(1);

        // How long the misfire loop backs off after a failure that was not transient — a
        // database that is down rather than busy — and the check-in loop once a failed
        // check-in has spent the window its peers give it.
        options.DbRetryInterval = TimeSpan.FromSeconds(15);
    });
});
```
<!-- endSnippet -->

- **What decides the value**: a node blocked on `QRTZ_LOCKS` behind a peer that stopped without releasing
  the lock schedules nothing until the command times out. Shorter turns a long stall into a fast failure
  and retry; too short turns a busy database into a retry storm.
- ADO.NET counts whole seconds, and Quartz rounds **up** — `00:00:01.500` is applied as 2 seconds —
  because rounding a sub-second value down gives `0`, which every provider reads as "wait forever".
- **A peer need not have stopped.** When a node's network is cut while it holds the lock, the server keeps
  its session, open transaction and row lock, because no reset reached it. Quartz cannot free another
  session's lock, so the timeout is all a client can do; a server-side setting ends the dead session —
  [A Lock Held by a Connection That Is Gone](../troubleshooting.md#a-lock-held-by-a-connection-that-is-gone)
  has both halves per database.
- **On Oracle**, which has no session-level DML lock wait timeout, put the wait timeout in the lock
  statement: `SelectWithLockSql` ending `FOR UPDATE WAIT 20`, which fails with `ORA-30006` and ends the
  wait in the server instead of cancelling from outside.

**Alert on warning 3716.** While a lock wait goes on, `JobStore:LockWaitWarningThreshold` (30 seconds by
default, `null` to turn off) logs **warning 3716** once per acquisition, naming the lock, the wait so far
and the requestor. It is the only signal of a node stalled on a lock, because a blocked statement raises
nothing. Every acquisition is also measured on `quartz.jobstore.lock.wait.duration`, tagged
`quartz.jobstore.lock` (`TRIGGER_ACCESS` or `STATE_ACCESS`).

### What counts as transient

A transient failure is retried `MaxTransientRetries` times (default 3), `TransientRetryInterval` apart
(default 1 second). Transient on 4.x means:

- a `TimeoutException` anywhere in the exception chain;
- the driver's own `DbException.IsTransient`;
- **a SQLSTATE in class `40`** ("transaction rollback"): `40001` serialization failure, PostgreSQL's
  `40P01` deadlock detected, and the rest of the class except `40002`, a deferred constraint violation that
  fails identically every time. **4.x only.** It catches drivers whose `IsTransient` is wrong: Firebird
  reports `IsTransient: false` for a serialization failure, the condition retrying exists for;
- SQL Server's transient error numbers, read from `SqlException.Errors`, which is where 1205 (deadlock
  victim) arrives because both SqlClients leave `SqlState` null;
- SQLite's busy and locked codes.

`AdoJobStoreOptions.IsTransient` (a `Func<Exception, bool>` set in code) adds conditions for a driver of
your own. It is consulted first and can only add: `false` means the same as having none, so it cannot turn
off a retry Quartz already makes. It receives the store's own exception; reach the driver's with
`GetBaseException()`.

`DbRetryInterval` (default 15 seconds) is separate. It is how long the misfire loop backs off after a
failure that was *not* transient (a database that is down, not busy), and how long the check-in loop backs
off once a failed check-in has spent its window, so a cluster does not hit a dead server every 7.5 seconds.
Inside the window the check-in loop retries sooner, and `DbRetryInterval` only caps the wait — see
[When a peer takes over](#when-a-peer-takes-over).

A check-in that fails for longer than the failure boundary gets the node written off while it is still
working, so an outage long enough to exhaust the retries also causes spurious failovers. Hence
[assume the job will run more than once](../best-practices.md#assume-the-job-will-run-more-than-once).

## Sizing a cluster

The per-node arithmetic is in Best Practices:
[max concurrency is a permit count](../best-practices.md#max-concurrency-is-a-permit-count-not-a-thread-count),
and [the connection pool is the thread pool plus three](../best-practices.md#the-connection-pool-is-the-thread-pool-plus-three).
With several nodes:

- **The database's connection budget is shared.** Ten nodes with a pool of 25 each present 250 connections
  to one server. Derive `MaxConcurrency` from the database's budget divided by the node count; adding a
  node is a database decision as well.
- **Every node runs its own misfire handler.** Each scans every `MisfireHandlerFrequency` (default
  `MisfireThreshold`, one minute). With `DoubleCheckLockMisfireHandler` on (the default) the scan starts
  with a `COUNT` that takes no lock, and takes the cluster-wide lock only when it finds something. Baseline
  cost: one count query per minute per node; contended: one lock per minute per node with work.
- **Every node runs its own cluster manager**: one `SELECT` of the state table and one `UPDATE` per node
  per `CheckinInterval`. At 7.5 seconds, ten nodes issue 160 statements a minute before any job runs. A
  shorter interval buys faster failure detection with more of this traffic.
- **Batching trades round trips for balance.** `MaxBatchSize` above 1 makes every acquisition cycle take the
  `TRIGGER_ACCESS` lock, even cycles that acquire nothing, and batches nothing unless
  `BatchTriggerAcquisitionFireAheadTimeWindow` is above zero. Load can become uneven: a node that acquires
  ten triggers holds them until it can run them. See
  [Batching trigger acquisition](tutorial/advanced-enterprise-features.md#batching-trigger-acquisition).

More nodes add capacity for concurrent firings and a node to fail over to. They do not make one trigger
fire faster or one job finish sooner; a schedule dominated by one long job gains nothing.

## Performance

These are the only end-to-end firing figures published for 4.0, summarised from
[`src/Quartz.Benchmark/README.md`](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Benchmark/README.md),
which has the method, machine, settings and caveats.

**Read them as ratios, not a capacity plan.** One machine, one PostgreSQL container over loopback, one
node, no clustering. Your storage, network and jobs decide the absolute numbers; what carries over is how
4.0 compares with 3.20 on the same machine on the same day.

### A firing, end to end

One firing: acquire, fire, run a job that does nothing, complete. `MaxBatchSize` equals `MaxConcurrency` in
these runs, since the scheduler refuses a batch larger than the pool.
`BatchTriggerAcquisitionFireAheadTimeWindow` is one second instead of the shipped zero; without a window a
batch is one trigger however large `MaxBatchSize` is, which is what a deployment at the defaults gets.

| Store         | MaxConcurrency | 3.20               | 4.0                | Per firing  |
|-------------- |--------------- |------------------- |------------------- |------------ |
| PostgreSQL    | 10             | 9.87 ms / 136 KB   | 6.18 ms / 56 KB    | 1.6x faster, 2.4x less garbage |
| PostgreSQL    | 50             | 9.21 ms / 132 KB   | 6.13 ms / 53 KB    | 1.5x faster, 2.5x less garbage |
| `RAMJobStore` | 10             | 2.58 µs / 3.25 KB  | 2.16 µs / 2.57 KB  | 1.2x faster, 21 % less garbage |
| `RAMJobStore` | 50             | 2.71 µs / 3.29 KB  | 1.81 µs / 2.57 KB  | 1.5x faster, 21 % less garbage |

- About 162 firings a second per node on PostgreSQL, and about 460,000 to 550,000 on `RAMJobStore`, on this
  machine.
- The PostgreSQL gain is the batched fire path: a firing costs **1.27 database commits**, where it used to
  cost one round trip per statement.
- The in-memory rows are medians of five alternating pairs under load, not tight single figures like the
  PostgreSQL rows: read the ratio, not the absolute. The benchmark README has the ranges.
- 4.0 once measured 1.4x *slower* on `RAMJobStore`
  ([#3674](https://github.com/quartznet/quartznet/issues/3674)). The cause was in the in-memory store: a
  dictionary allocated and discarded per firing, and a lock taken asynchronously where 3.x took a monitor,
  so most store calls on the fire path suspended and paid a thread-pool hop. Both are fixed. The DI scope,
  middleware pipeline, execution-group ledger and retry-policy check each measured cheaper than the 3.x
  code they replaced, or free.

**A bigger pool does not start firings faster.** Five times the threads bought about 17 % on
`RAMJobStore` and nothing measurable on PostgreSQL, because a node's store operations serialise on one
lock: `TRIGGER_ACCESS` on a persistent store, the store's own monitor in memory. `MaxConcurrency` buys more
*jobs* running at once — see [Sizing a cluster](#sizing-a-cluster).

### Scheduling and cron

Measured against 3.14, also in the benchmark README:

| Operation                                       | 3.14                | 4.0                 |
|------------------------------------------------ |-------------------- |-------------------- |
| Parse a `CronExpression`                        | 3.0–4.6 µs / 8.7–12.9 KB | 236–338 ns / 576–688 B |
| `GetNextValidTimeAfter`                         | ~1.29 µs / ~3.2 KB  | 315–373 ns / **0 B** |
| `ScheduleJob`, cron trigger, into `RAMJobStore` | 31 µs / 38.7 KB     | 10.5 µs / 5 KB      |

Also measured: [the acquisition index](db/index.md#indexes-and-the-acquisition-index-in-particular), over a
hundred thousand triggers on four engines, and
[the cluster-wide execution ceiling](tutorial/execution-groups.md#cluster-scoped-limits), which costs one
aggregate per acquisition attempt.

### Against other .NET schedulers

[`src/Quartz.Benchmark.Competitors`](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Benchmark.Competitors/README.md)
runs Quartz, **TickerQ 10.4.0** and **Hangfire 1.8.25** on one machine in one sitting, over the same
workloads with the same worker limit, counting inside the executing job on every side:

| | Quartz.NET | TickerQ 10.4.0 | Hangfire 1.8.25 |
|--- |--- |--- |--- |
| One execution, in memory | 3.9-5.5 µs / 3.5 KB | 8.7-10.3 µs / 4.9-5.4 KB | 14.9-17.1 µs / 24.4 KB |
| One execution, PostgreSQL | 11.3-11.8 ms / 90 KB | 2.9-3.2 ms / 49 KB | 15.8 ms / 102 KB |
| Statements per execution, PostgreSQL | 26.0 | 1.96 | 61.6 |
| Schedule to execute on an idle node, p50 / p99 | 58-70 µs / 261-441 µs | 14.7 ms / 15.6-16.0 ms | 94-235 µs / 1.0-22.5 ms |
| Writing one schedule | 7.7-7.9 µs / 3.5 KB | 1.1-1.3 µs / 562 B | 6.5-7.1 µs / 7.2 KB |

- The Quartz column uses **shipped defaults**, not the batched settings above; batching moves the two
  PostgreSQL rows to 10.2-10.5 ms and 19.6 statements.
- The latency row is each library's fastest "run this now": `StartNow` for Quartz, a null `ExecutionTime`
  for TickerQ, `Enqueue` for Hangfire. Through Hangfire's scheduler instead it is 30 ms (in the benchmark
  README).
- `MaxConcurrency` is ten on all three. That is Quartz's default; TickerQ's is `Environment.ProcessorCount`
  and Hangfire's `ProcessorCount × 5`, which on this machine would have given Hangfire sixteen times the
  workers. The other two also get a faster poll than they ship with; everything else is each library's
  default.

**Results.** Quartz starts an execution faster in memory, allocates less, and gets a "run now" job to a
worker an order of magnitude sooner. On a one-second recurring schedule at its defaults it was the only
one of the three to fire all six thousand firings within fifty milliseconds of the due second. **It loses
the database rows to TickerQ clearly** — 3.5-4× on time and more than tenfold on statements — and the
schedule-writing row by six to eight times. Part of that is the workload: these rows schedule one-off
jobs, which Quartz deletes on completion and TickerQ leaves in its table.

### What has been run against a cluster

**The soak test**: two clustered nodes sharing one scheduler name, 30 minutes per run. The workload covers
every trigger family including recurrence, a `[DisallowConcurrentExecution]` job behind an overlap
detector, a retry policy over a job that always fails, and a job that overruns its `AddJobTimeout` budget.
Induced failures: both nodes in standby past the misfire threshold; one node killed with an `EXECUTING` row
left and its check-in aged; that node replaced once the survivor had recovered it.

Every run passed and ended the same way: no `ACQUIRED` or `BLOCKED` triggers, `QRTZ_FIRED_TRIGGERS`
empty, every trigger family on schedule, **no overlapping firing of the serial job across nodes** (peak
observed concurrency 1), exactly one interrupted firing recovered by the survivor, no unexpected scheduler
error or unobserved task exception, and heap and handle counts no larger at the end than at the start. No
tolerance was widened; the fixture's assertions are the release's.

| Run | Serial job firings | Retry-policy re-fires | Overruns interrupted | Heap / handles |
|---|---|---|---|---|
| beta.1, PostgreSQL | 2,662 | 356 | 178 | 5 MB / ~635 |
| beta.1, SQL Server | 2,367 | 346 | 175 | 5 MB / ~730 |
| release candidate `2e207e37b`, PostgreSQL, 2026-09-03 | 2,661 | 356 of 534 attempts | 178 | 4–5 MB / 619–640 |
| rc.1 `97505ecce`, SQL Server, 2026-09-03 | 2,578 | 355 of 532 attempts | 178 | 4–5 MB / 706–756 |
| 4.1 runtime tenant `b67f0faa9a`, 2026-09-09 (cluster) | 2,645 | 356 of 534 attempts | 178 | — |

Firings per family (simple / daily-time-interval / calendar-interval / cron / recurrence), and when the
survivor replayed the killed node's firing:

- **Release candidate, PostgreSQL** — 890 / 591 / 444 / 355 / 296; a second after the kill. Repeated
  because sixteen commits had touched `Quartz` since beta.1.
- **rc.1, SQL Server** — 890 / 594 / 446 / 357 / 297, within about 1% of the schedule; `soak-node-a`
  replayed it in the same second, and the killed node's `EXECUTING` row was gone at the end. Run because
  rc.1 drops `IDX_QRTZ_T_NFT_ST_MISFIRE`, so it provisioned a new `tables_sqlServer.sql`, and to cover
  rc.1's store changes: the paused-job-group probe every trigger store makes, and column-level schema
  validation, which all three nodes passed at startup.
- **4.1 cluster** — 890 / 591 / 444 / 354 / 296; a second after the kill.

4.0.0 is two `Quartz` commits after `97505ecce`, and neither is reached by the soak. `SelectJobForTrigger`
now also selects `IS_NONCONCURRENT` and `IS_UPDATE_DATA`, so a process without the job's class still reads
its flags (#3705); only rescheduling, editing and deleting a trigger reach it. `QuartzHostedService`'s stop
path changed; the fixture builds its nodes in containers of its own and never enters it. The figures stand
for the release.

**A runtime-added tenant (4.1).** 4.1 is the first release in which a scheduler can arrive after the
container is built. In the 4.1 run above, a third scheduler was added to node A's container through
[`ISchedulerRuntime.Add`](multi-tenancy.md), under its own `SCHED_NAME` in the same tables, with a
`[DisallowConcurrentExecution]` job every two seconds and an ordinary one every second. It was restarted
through `ISchedulerRuntime.Restart` with a thirty-second drain every three minutes, and removed and re-added
from the same recipe every seven: twelve rebuilds, thirteen generations.

- Its ordinary trigger had **1,796 scheduled fire times and fired each exactly once.** A rebuild gap is
  under a second, far inside the misfire threshold, so each generation caught up what it missed.
- Its serial job's peak observed concurrency was **1** over 898 firings across the thirteen generations;
  the trigger rows carry the block over a rebuild.
- No drain was abandoned, and every rebuild was firing again within 0.9 s against a budget of ten.
- It ended with nothing `ACQUIRED` or `BLOCKED` under either scheduler name, `QRTZ_FIRED_TRIGGERS` empty for
  both, and a 5 MB live heap behind 629–656 handles across all thirty samples.
- **A non-clustered scheduler's instance id is `NON_CLUSTERED` in every generation**, because the id
  generator is not called for an unshared store. To tell generations apart, use something else: the
  fixture stamps an ordinal into the scheduler's `SchedulerContext` as the recipe runs.

The harness is `ClusteredSoakTestBase` in `Quartz.Tests.Integration`: opt-in (`[Category("LongRunning")]`,
`QUARTZ_SOAK_MINUTES`), run before a tag, not in CI.

## Health checks and probes

The check in `Quartz` asserts that the scheduler can fire, that its job store answers a query, and, when
clustered, that this node is still checking in. With `StaleFiringTolerance` set, it also asserts that
nothing schedulable is badly overdue.

| Scheduler | Reports |
|---|---|
| Running, store responds | *healthy* |
| In standby | *degraded* |
| Shutting down or shut down, or the store threw | *unhealthy* |
| In `Created`, and the hosted service should have started it | *unhealthy* |
| In `Created` with `AutoStart = false`, started by the application | *degraded* |

The last row is the normal state for [an external leader election](how-tos/external-leader.md) and
[embedding Quartz in a library](how-tos/embedding-quartz-in-a-library.md). The check registers on the
standard `IHealthChecksBuilder` and needs nothing from ASP.NET Core, so it works on a `dotnet/runtime`
image.

<!-- snippet: sample_operations_readiness_probe -->
```csharp
// Tagged, so a readiness endpoint can select it while the liveness endpoint does not: a
// scheduler in standby, or one whose database is unreachable, should leave the rotation
// without the process being killed.
services.AddHealthChecks().AddQuartz(options => options.Tags.Add("ready"));
```
<!-- endSnippet -->

**It does not assert that anything fires, unless you ask.** An empty schedule, a paused group or a starved
thread pool is healthy.

- `QuartzHealthCheckOptions.StaleFiringTolerance` (`null`, so off, by default) changes that: a schedulable
  trigger overdue by more than that many of the store's misfire thresholds is *degraded*, twice as far is
  *unhealthy*, and the report names the trigger and when it was due. See
  [Saying that a scheduler has stopped firing](packages/hosted-services-integration.md#saying-that-a-scheduler-has-stopped-firing).
- That is about the queue, not a particular job. Also alert on a job you expect to see regularly, from the
  *count* of `quartz.job.execution.duration` — a histogram with no observations is the signal:

```promql
# no execution of the nightly close in the last 25 hours, on any node of the cluster
sum(increase(quartz_job_execution_duration_count{quartz_job_name="nightly-close"}[25h])) == 0
```

- Leave room in the window for jitter and a retry, and alert per job that matters: in aggregate, a busy
  fleet hides one job that stopped. Which attributes are high-cardinality and should be dropped in a view:
  [Observability](packages/opentelemetry-integration.md#metrics).
- When a job's *absence* is the incident, query the store too for a trigger whose `NextFireTimeUtc` is far
  in the past, or one in `Error`. `new TriggerQuery { State = TriggerState.Normal, NextFireTimeBefore = cutoff }`
  is the query the tolerance issues.

**Of the cluster, it reports only a node that has stopped checking in.** A node whose cluster manager is
wedged on the database can still fire and say `Running` while its peers recover its triggers. So a
**clustered** scheduler reports *degraded*, naming the node and how late it is, when its last check-in is
older than `QuartzHealthCheckOptions.ClusterCheckinTolerance` (`3` by default) times its own check-in
interval. `null` or `0` skips the query. For the state of peers, use the
[node listing](tutorial/advanced-enterprise-features.md).

**Degraded passes an HTTP probe by default.** ASP.NET Core maps `Degraded` to 200, like `Healthy`, so a
standby scheduler looks healthy to anything reading the status code.

- To take a standby node out of rotation, map `Degraded` to 503 in `HealthCheckOptions.ResultStatusCodes`.
- Under Aspire this interacts with `WithHttpHealthCheck`; [the Aspire how-to](how-tos/aspire.md) has the
  table, including that a worker project has no health endpoint to poll.
- `AddQuartzHealthChecks(options => options.StandbyStatus = HealthStatus.Unhealthy)` changes the verdict
  itself rather than its status code, so a probe reading the `HealthCheckService` directly sees it too. It
  covers standby only; a scheduler in `Created` because `AutoStart = false` still reports degraded.

What else to watch — the `Quartz` activity source, the `quartz` meter, and what the instruments cover —
is in [Observability](packages/opentelemetry-integration.md) and
[What to watch](../best-practices.md#what-to-watch).

## See also

- [Before you go live](production-checklist.md) — the decisions this page assumes, as a pre-deploy list
- [Upgrading a running deployment](migration-guide.md#upgrading-a-running-deployment) — the ordered
  sequence; the rolling upgrade here is its step four
- [Log Events](log-events.md) — every event id, its level and its message template
- [Clustering](tutorial/advanced-enterprise-features.md) — configuring a cluster
- [Troubleshooting](../troubleshooting.md) — symptoms and what to do about each
- [Best Practices](../best-practices.md) — the decisions this page assumes
- [Database Schema](db/) and [Schema Changes](../database/schema-changes.md) — what the tables hold and
  what each version added
- [`Quartz.Benchmark`](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Benchmark/README.md) — the numbers above, with their method and caveats
- [Configuration Reference](configuration/reference.md#persistent-job-store) — every setting named here,
  with its default
