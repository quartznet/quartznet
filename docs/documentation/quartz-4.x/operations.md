---
title: 'Operating a Cluster'
---

# Operating a Cluster

The rest of the documentation says how to build a scheduler. This page is about running one that is
already built: upgrading it without stopping it, giving each node a name that survives a restart,
reading what the tables are telling you, and knowing what a restore of the database means for work
that was in flight when the backup was taken.

It is 4.x, and it assumes a clustered ADO.NET store. [Clustering](tutorial/advanced-enterprise-features.md)
covers what a cluster is and how to configure one; [Best Practices](../best-practices.md) covers the
decisions that shape a schedule. Everything here has been checked against the code in this repository,
and where that code disagrees with received wisdom the sentence says so.

## Rolling a new version through a cluster

### Schema first, then nodes

Quartz.NET never creates or migrates its own schema. A deployment therefore has two steps in a fixed
order, and the order is not negotiable in either direction:

1. **Apply the migration**, from [`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations),
   every folder between the version the database is at and the version you are going to, in ascending
   order.
2. **Replace the nodes**, one at a time.

The migrations are written to make that safe. Every statement checks before it acts, so a script is a
no-op the second time and a partially-applied script is safe to re-run — the one exception being
SQLite's `ADD COLUMN`, which has no conditional DDL. What they do is additive: columns and tables are
added, never dropped or narrowed, so a node still running the old version keeps working against the
migrated schema. Indexes are the one thing a migration does remove, and the next section says what that
costs. That is the [expand phase of parallel change](https://martinfowler.com/bliki/ParallelChange.html)
applied to a scheduler, and it is why the schema goes first: an old node tolerates a new schema, while
a new node does not tolerate an old one.

A node that meets a schema it cannot use refuses to start rather than misbehaving, which is what you
want: the store issues a `SELECT 1` against every table it needs and fails with
`SchedulerException: Database schema validation failed` if one is missing. Know its limit, though — it
checks *tables*, not columns, so a database that is missing only a column gets past startup and fails on
the first statement that names it. `JobStore:SchemaProvisioning` set to `None` turns the check off; there
is no good reason to.

::: warning
The fresh-install scripts in [`database/tables/`](https://github.com/quartznet/quartznet/tree/main/database/tables)
are not migrations. **Each one drops the existing Quartz schema before recreating it**, and the switch
that governs the drops — `@DropDb` on SQL Server and MySQL, `DropDb` elsewhere, declared at the top of
the file — defaults to **1**, meaning drop. Set it to `0` to get creation only, and on SQLite, which
has no variables, delete the block between the `BEGIN DROP TABLES` and `END DROP TABLES` markers.
:::

### Replacing the nodes

Once the schema is ahead of every node, replace the nodes one at a time. Three things happen as each
one goes down and comes back that are worth knowing about in advance.

**A clean shutdown gives its reservations back.** When the scheduling loop halts, every trigger it had
acquired but not yet fired is released to `WAITING`, so another node picks it up on its next pass
rather than waiting for the failure detector. A process that is killed rather than stopped does not do
this, and its reservations wait for the check-in machinery below.

**A clean shutdown does not delete the node's check-in row.** Nothing removes a `QRTZ_SCHEDULER_STATE`
row on the way down; the row stays, with the timestamp the node last wrote, until a peer notices it has
gone quiet and recovers it. So a node that stops is declared *failed* about fifteen seconds later on
the default settings, exactly as if it had crashed — and if it was running jobs that request recovery,
those jobs are scheduled again on another node. That is correct behaviour for a crash and
indistinguishable from one here, which is the argument for
[`WaitForJobsToComplete`](../best-practices.md#shutdown-has-a-deadline): a node that finishes its work
before it exits has nothing left to recover.

**A node that generates its instance id comes back as a different node.** `GenerateInstanceId` derives
the id from the host name and a timestamp, so a restart produces a new one. The old id's check-in row
and any fired-trigger rows it left behind are cleaned up by whichever node next notices them, and the
new id starts clean. This is fine and is the default for a reason — but it means a node's identity in
the dashboard, in the `INSTANCE_NAME` of every fired-trigger row, and in a `PREFERRED_NODE` pin does not
survive the deployment. The next section is about when that matters.

### A mixed 3.x and 4.0 window

Upgrading a cluster from 3.x to 4.0 means the
[mandatory 4.0 migration](../database/schema-changes.md#version-4-0) and then replacing nodes — so for
however long the rollout takes, a 3.x node and a 4.0 node are running against one set of tables. Here is
what has been checked against both branches' code, and what has not.

**A 3.x node keeps working against the migrated schema.** The migration adds columns and one table and
takes nothing away except optional indexes. 3.x's `INSERT` statements name their columns explicitly, so
the new `PREFERRED_NODE_AUTO NOT NULL DEFAULT 0` takes its default rather than failing. And 3.x probes
for `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` at startup:
finding them present, it turns those features on, which is the state a fully-migrated 3.x database is in
anyway.

**The vocabularies the two versions read and write are identical.** The stored trigger states
(`WAITING`, `ACQUIRED`, `EXECUTING`, `COMPLETE`, `BLOCKED`, `PAUSED`, `PAUSED_BLOCKED`, `ERROR`,
`DELETED`), the pause-all marker, the trigger-type discriminators (`SIMPLE`, `CRON`, `CAL_INT`,
`DAILY_I`, `RECUR`, `BLOB` — `RECUR` since 3.18), the lock names and the check-in row's columns are the
same constants on both branches, and job data serializes to the same JSON but for one value shape, below.
The failure predicate is the
same code, so the two versions judge and recover each other by it; the acquisition compare-and-swap is
the same statement, so neither takes a trigger the other has; the stale-acquired sweep is scoped to the
sweeping node's own rows on both, so neither disturbs the other's reservations; and node-affinity pins
are stored identically.

So the core of scheduling holds across the window: neither version fires a trigger the other has taken,
neither loses one, and neither refuses to start because of the other. What does not hold is a short list,
and it is specific enough to plan around.

**Do not let a 4.0 node write a calendar during the window.** This is the one break that will cost you
firings rather than accuracy. 4.0 changed how three calendars are serialized: `WeeklyCalendar` and
`MonthlyCalendar` write day names and day numbers where 3.x wrote a positional array of booleans, and
`DailyCalendar` writes `RangeStart`/`RangeEnd` where 3.x wrote `RangeStartingTime`/`RangeEndingTime`.
The compatibility is deliberately **one-way**: 4.0's readers accept either shape, and 3.x's readers accept
only their own, so a calendar a 4.0 node stores is one a 3.x node throws on every time it reads it. And
it does read it every time — a clustered store bypasses its calendar cache by design, so the failure is
per firing rather than once. Existing rows are untouched and safe; it is `AddCalendar` from a 4.0 node
that does the damage. Route calendar changes through the 3.x nodes until the last one is retired.

**Both versions have to be on JSON, and on the same serializer.** 4.0 refuses `quartz.serializer.type
= binary` at startup, so a cluster whose 3.x nodes wrote binary job data has no window at all — that is
a migration to do before the rollout, not during it.

**A `Dictionary<string, string>` job data value is the one shape whose JSON differs**, and only on the
Newtonsoft serializer. 4.0 writes it as the plain object System.Text.Json has always written, where 3.x
wrote the type name Json.NET puts beside a value an `object`-typed slot cannot name — see
[A string dictionary is written the same way by both serializers](migration-guide.md#a-string-dictionary-is-written-the-same-way-by-both-serializers).
The compatibility runs the same way round as the calendars': 4.0 reads both forms, and 3.x's Newtonsoft
reader reads only its own, handing back a Json.NET `JObject` where the job put a dictionary. A job that
stores a string map therefore has to keep its writes on the 3.x nodes until the last one is retired, or
store the map as a string it serializes itself. Nothing else in job data is affected, and a cluster on
System.Text.Json is not affected at all.

**Defer section 6 of the migration until the last 3.x node is gone.** Sections 1 to 5 are the required
ones; section 6 realigns the index set, and it *drops seven indexes that the 3.20 migration created for
3.x* — `IDX_QRTZ_T_G_J`, `IDX_QRTZ_T_N_STATE`, `IDX_QRTZ_T_N_G_STATE`, `IDX_QRTZ_T_NEXT_FIRE_TIME`,
`IDX_QRTZ_T_NFT_ST_MISFIRE_GRP`, `IDX_QRTZ_FT_G_J` and `IDX_QRTZ_FT_G_T`. Its replacements have the
leading columns 4.x's queries want, not 3.x's; one of them, `IDX_QRTZ_T_NFT_ST_MISFIRE_GRP`, serves a
3.x statement that has no 4.x counterpart at all. Nothing breaks, but a 3.x node scans where it used to
seek, which on a large schedule is the difference between a misfire sweep that finishes and one that
times out. The script is guarded and re-runnable, so running sections 1 to 5 now and the whole file
again afterwards costs nothing.

**A retry policy is invisible to a 3.x node, and a 3.x reschedule destroys one.** `RETRY_POLICY` and
`RETRY_ATTEMPT` are new in 4.x, so a job that fails on a 3.x node is not retried and its attempt count
is not advanced — that half is only a feature being absent. The half that loses data is rescheduling:
3.x implements `IScheduler.RescheduleJob` as a delete followed by an insert, and its insert names no
`RETRY_POLICY` or `RETRY_ATTEMPT` column, so the trigger comes back with both null and the policy is
gone with no error anywhere. An in-place update is safe — 3.x's trigger `UPDATE` sets a fixed column
list that omits the two, so it leaves whatever 4.0 wrote alone. So: **do not reschedule a trigger from a
3.x node during the window if it carries a retry policy.** Pausing, resuming, deleting and firing it are
all fine.

**Treat cluster-scoped execution limits as unavailable during the window, not merely approximate.**
`ExecutionLimitScope.Cluster` is 4.x only, and a 4.0 node enforces it by counting `QRTZ_FIRED_TRIGGERS`
grouped by `EXECUTION_GROUP`. 3.x never writes that column on a fired trigger — its own insert has a
fixed column list that omits it — so every firing a 3.x node owns is counted as *ungrouped*. The effect
is worse than under-counting: the limited group's ceiling misses the 3.x work entirely, and the
ungrouped bucket is charged for it, so a group you did not limit can be throttled by work that belongs
to one you did. Per-node limits are unaffected, because they never crossed nodes.
[Job-type exclusions](how-tos/custom-job-store.md#excluding-job-types-from-acquisition) are 4.x-only in
the same way, and are per-node by construction: a 3.x node will happily run a job type the 4.0 nodes
were told to refuse.

**Paused job groups are recorded by 4.0 nodes only.** `QRTZ_PAUSED_JOB_GRPS` is new in 4.x and 3.x has
no code that touches it. What actually fires is decided by trigger state, which both versions agree
about, so pausing a job group works in the window whichever node does it — but the *record* drifts, in
both directions. A group paused by a 3.x node is not recorded, so `JobGroup.Paused` on a 4.0 node says
`false` for a group whose every trigger is paused. A group paused by a 4.0 node and then resumed — or
cleared — by a 3.x node leaves its row behind, so the 4.0 node goes on calling a group paused whose
triggers are running, and goes on doing so indefinitely. Pause and resume from the same version, and
reconcile the table when the rollout is done.

**What has not been established.** Nothing in this repository tests two versions against one schema, and
no release is validated for it; the findings above come from reading both branches, not from running
them together. Java Quartz's documentation says nothing about version mixing either, so there is no
upstream position to appeal to.

The honest shape of the advice, then: the window is **workable under those conditions** and it is not a
supported steady state. Keep it as short as the rollout needs. Hangfire, which does support this
explicitly, is worth the comparison — it states that "1.6.X/1.7.X and 1.8.0 servers can co-exist in the
same environment just fine, thanks to forward compatibility", and gates its risky migrations behind an
`EnableHeavyMigrations` switch so the operator picks the moment. Quartz.NET makes no such promise, and
the list above is what it offers instead.

Rolling back is available for the same reason the window works: the migration is additive, so a 3.x node
starts against the 4.0 schema without anything being undone. Only section 6's index drops would need
putting back, by re-running [`migrations/3.20`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.20) —
and any calendar a 4.0 node wrote would need rewriting from a 3.x one.

## Naming a node in a container

### What the default gives you, and what it does not

`InstanceId` defaults to the literal string `NON_CLUSTERED`, and the instance id is how a node
recognises its own check-in row and its own firings. Two nodes that share one are not two members of a
cluster; they are one member as far as every query is concerned, each one treating the other's rows as
its own. Nothing in Quartz.NET detects this — there is no validation that a clustered scheduler's id is
unique, because no node can see what the others were configured with.

So a clustered scheduler has to be told to derive one:

```csharp
q.ConfigureScheduler(options =>
{
    options.InstanceName = "orders";
    options.GenerateInstanceId = true;
});
```

`GenerateInstanceId` runs the registered `IInstanceIdGenerator`, which by default is the host name
followed by a high-resolution timestamp. The flat key that means the same thing is
`quartz.scheduler.instanceId = AUTO`. Only a clustered store calls the generator at all: a store with
clustering switched off has nothing to distinguish itself from, so the id stays `NON_CLUSTERED`
whatever the setting says.

That default is **unique but not stable**. The timestamp makes a collision essentially impossible even
between two containers reporting the same host name — but every restart is a new identity. Three
things want a stable one:

- **[Node affinity](tutorial/node-affinity.md)**, which pins a trigger to an instance id. A pin to an
  id that no longer exists is a pin to nobody.
- **Correlating a node across a deployment** — in the dashboard's Cluster page, in
  `FireInstance.SchedulerInstanceId`, in the `quartz.scheduler.id` attribute on every span and log
  scope.
- **Reading the check-in table by hand** and expecting yesterday's rows to name the same machines.

### Taking the id from the pod

The pattern that gives a stable identity in Kubernetes is a **StatefulSet** plus the **Downward API**.
A StatefulSet names its pods `$(statefulset name)-$(ordinal)`, and the docs are explicit that this
"identity sticks to the Pod, regardless of which node it's (re)scheduled on". Inject that name and use
it as the instance id:

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

The fallback matters as much as the assignment: a manifest that has not been updated, or a developer
running the same image locally, should not silently give every replica the id `NON_CLUSTERED`.

`metadata.uid` is the wrong field to use instead. Kubernetes documents UIDs as existing "to
distinguish between historical occurrences of similar entities" — a pod recreated under the same name
gets a new one, which is precisely the churn the pod name avoids.

There is also a generator that reads the id from the environment directly, selected with the flat value
`quartz.scheduler.instanceId = SYS_PROP`; it reads the environment variable named
`quartz.scheduler.instanceId`, or another one if
`quartz.scheduler.instanceIdGenerator.systemPropertyName` names it. It exists for configuration files
carried over from 3.x. In 4.x, reading the variable in code and assigning `InstanceId` says the same
thing in one fewer indirection, and lets you write the fallback.

### When two pods report the same host name

The genuinely dangerous configuration is one where the host name is not unique *and* the id is derived
from the host name alone. On a Deployment, pod names — and therefore host names — carry a random
suffix, so they are unique per pod but change on every restart. The cases where two pods really do
report the same name are `hostNetwork: true`, where every pod on a node reports the node's own host
name, and a manifest that sets `spec.hostname` to a literal rather than templating it.

Quartz.NET's default generator survives both, because of the timestamp. What does not survive is a
configuration that names the host-name-only generator through
`quartz.scheduler.instanceIdGenerator.type` — that one returns the host name unchanged, by design, for
the case where "your scheduler instance will be the only one running on a particular machine". Under
`hostNetwork` it is not.

The failure that follows is the one Kubernetes describes for its own StatefulSets, and the wording
transfers: "Having multiple members with the same identity can be disastrous". Concretely, in a Quartz
cluster: each node treats the other's fired-trigger rows as its own, so a node's first check-in after a
restart recovers firings that another node is still executing, and
`[DisallowConcurrentExecution]` stops holding for exactly the jobs that were running.

The rule the neighbours state the same way is worth repeating for its unanimity. Hangfire derives a
server id from the machine name and process id and says that "since the defaults values provide
uniqueness only on a process level, you should handle it manually" beyond that. Kafka: "Every node in a
KRaft cluster must have a unique `node.id`". Orleans' Kubernetes hosting "sets `SiloOptions.SiloName` to
the pod name" and requires that "silo names must match pod names". Elasticsearch's Kubernetes operator
does the same thing implicitly — "Elasticsearch nodes have the same name as the Pod they are running
on". Every one of them ends up at the pod name.

## Check-in, node states and failover

### What a check-in is

Each node writes a row to `QRTZ_SCHEDULER_STATE` and updates its timestamp every `CheckinInterval` —
7.5 seconds by default. That write is the whole of what a node claims about itself. There is no
heartbeat between nodes, no leader, and no election: every judgement one node makes about another is
that node reading a timestamp somebody else wrote and comparing it against its own clock.

The first check-in happens during `Start()`, before the scheduler begins firing, and it is the one that
does the most work: a node's first check-in also treats *its own* previous row as a failed instance, so
whatever it left behind on its last run is recovered then. Subsequent check-ins take the cheap path —
update the timestamp, look for failed peers, and take the cluster-wide locks only if there are any.

Two details of that row are worth knowing because they are not what most people assume:

- **The stored check-in interval is written once.** `CHECKIN_INTERVAL` is set when the row is inserted
  and never updated afterwards — only `LAST_CHECKIN_TIME` is. A node with a stable instance id that
  changes its `CheckinInterval` keeps advertising the old value to its peers until its row is deleted
  and recreated, which happens after a recovery rather than at a restart. If you widen the interval
  across a cluster, expect the change to take effect for the peers' arithmetic only after each node has
  been declared failed once, or delete the rows while the cluster is stopped.
- **Check-in failures are logged sparsely.** The cluster manager logs one line for every
  `RetryableActionErrorLogThreshold` consecutive failures, which defaults to **4**. A database that is
  down produces a quarter of the log lines you would expect.

### When a peer takes over

A node decides a peer has failed when this is true, all times read from the deciding node's own clock:

> the peer's last check-in timestamp, plus the longer of *the peer's own stored check-in interval* and
> *the time since this node last checked in*, plus this node's check-in misfire threshold, is in the
> past.

On the defaults — both intervals 7.5 seconds — that is about fifteen seconds after the last timestamp
the peer wrote, of which only half is slack, since the peer writes one every 7.5 seconds.

The middle term is the part that is usually left out of the summary, and it is a deliberate piece of
self-protection: an observer that has itself been away from its check-in loop for a minute grants every
peer a minute of slack. So a database outage that stops the whole cluster checking in does not end with
the first node back declaring all the others dead.

Everything else about it is the standard caution about failure detectors, and
[Clocks in a cluster](../best-practices.md#clocks-in-a-cluster) has it: fifteen seconds is shorter than
a long garbage-collection pause, shorter than the thirty seconds Azure documents for
memory-preserving maintenance, and comfortably shorter than the clock skew of a machine with no
time-synchronisation service. Raise `CheckinMisfireThreshold` past your environment's worst *pause*,
not its worst clock error.

What a takeover does is release the failed node's acquired triggers, schedule recovery triggers for the
jobs of its interrupted executions that asked for recovery, delete the rest of its fired-trigger rows,
release any node-affinity pins it claimed automatically, and delete its check-in row.

One case is deliberately slower: recovering a `[DisallowConcurrentExecution]` job is held back on first
detection, because a node that has missed a check-in may still be running it. While anything is held
back, the failed node's check-in row is left in place with its stale timestamp — so it goes on being
reported `Failed` rather than disappearing, which is the store keeping the node visible until it is
finished with it.

### When the node that was taken over is still running

A takeover is one node's opinion, and it can be wrong: a stalled process, a paused container or a clock
that drifted is enough for a peer to write off a node that is still working. The node that was written
off finds out on its next check-in, when its own `QRTZ_SCHEDULER_STATE` row is not there any more. It:

- **writes the row back**, which is what re-registers it — until then it does not exist as far as its
  peers are concerned, and it is not listed by `QueryClusterNodes()` on any other node;
- **logs a warning** — `This scheduler instance (…) is still active but was recovered by another
  instance in the cluster` (event id `3501`), followed by one naming the peer that did it (`3515`) or
  saying that it cannot be named (`3516`). The peer can only be named when it is the only other node
  with a state row, because nothing in the schema records who recovered whom;
- **counts the event** on `quartz.cluster.recovery.trigger` with `quartz.cluster.recovered.instance.id`
  set to its own instance id. That equality — recovered node and reporting node the same — is what an
  alert on "this node is being failed out" matches. It counts 1, because how many firings the peer took
  over cannot be known from this side; the peer's own measurement carries that number;
- **does not recover its own fired triggers.** The peer released, rescheduled and deleted them under the
  trigger-access lock, and running recovery over the same rows again would schedule a second recovery
  trigger for a firing that is already being replayed.

None of that makes the takeover harmless — the peer has started work this node may still be doing, and
`[DisallowConcurrentExecution]` is not honoured across a firing the cluster believes has been recovered.
It is a symptom to fix at its cause, and the cause is nearly always the clock or a pause; see
[Clock Skew Between Nodes](../troubleshooting.md#clock-skew-between-nodes).

### Reading the cluster

`IScheduler.QueryClusterNodes()` lists the nodes with a verdict on each, decided by the same predicate
the recovery sweep applies — so the listing and the sweep cannot disagree:

| State | Means |
|---|---|
| `Alive` | Checked in within its own check-in interval. |
| `Overdue` | Has missed a check-in. Normal under load; nothing is recovered from an overdue node. |
| `Failed` | Past the boundary above. The next check-in pass by any node takes its work over and deletes its row, after which it stops being listed. |

A `Failed` node is therefore reported for a short while and then vanishes, which is what a healthy
failover looks like from the outside. A node that stays `Failed` across several minutes of polling is
one nobody is sweeping — check that at least one other node is running and that its cluster manager is
not stuck on the database.

The same listing is `GET {ApiPath}/schedulers/{name}/nodes` in the
[HTTP API](packages/http-api.md#cluster-nodes) and the Cluster page of the
[dashboard](packages/dashboard.md), which puts the `Acquired` and `Executing` counts for each node
beside its state. `GET {ApiPath}/schedulers` is the other half of the picture: it lists every scheduler
the process knows about, including registrations nothing has built yet, so a scheduler that never
started is distinguishable from one that does not exist.

## What the tables are telling you

### Fired triggers: backlog or leak

`QRTZ_FIRED_TRIGGERS` is the cluster's account of what is happening right now. A row is written when a
trigger is **acquired**, updated when the trigger actually **fires**, and deleted when the firing
completes. So the healthy steady state is a table whose row count tracks concurrency and whose oldest
row is no older than your longest-running job.

Growth is one of two things, and the difference is the age distribution rather than the count:

- **A backlog** is many rows, all young, spread across the nodes that are alive. The cluster is running
  as much as it can and more work is arriving than it finishes. The fix is capacity or a smaller
  schedule, not a database operation.
- **A leak** is rows that do not age out. Look at what they say about themselves: an old row in
  `EXECUTING` state means a job that never returned — a synchronous call that hangs, an unawaited task
  — and the node is genuinely still holding it. An old row in `ACQUIRED` state means a node reserved a
  trigger and never fired it, which is [the stale-acquired case](../troubleshooting.md#triggers-stuck-in-acquired-state)
  and is swept automatically. A row belonging to an instance id the cluster no longer lists is the real
  orphan, and orphans are only swept when a node performs its *first* check-in — so a cluster that has
  been up for months has never looked for them.

`IScheduler.QueryFireInstances` answers all of that without SQL, and joins to the node listing on the
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

To clear a stale `EXECUTING` row that belongs to a node that is still alive, restart that node: its
first check-in recovers its own leftovers. Nothing else sweeps a live node's rows, by design — the node
is the authority on what it is running.

### Nothing is firing

When the table is empty and jobs are not running, the question is a different one. Work the store is
deliberately holding back does not appear in `QRTZ_FIRED_TRIGGERS` at all, because it was never
acquired. The three usual reasons, in the order they are worth checking:

- **The scheduler is in standby.** `IScheduler.Status` says so, and the health check reports it as
  *degraded* rather than unhealthy — which, as [the Aspire how-to](how-tos/aspire.md) explains, does not
  survive an HTTP probe, because ASP.NET Core maps degraded to 200.
- **A group is paused.** Pausing is durable and survives restarts. `QRTZ_PAUSED_TRIGGER_GRPS` holds
  paused *trigger* groups, and it is the one that changes what happens next: a trigger stored into a
  paused trigger group is stored `PAUSED`. `QRTZ_PAUSED_JOB_GRPS` is new in 4.x and holds paused *job*
  groups, which is what makes `JobGroup.Paused` and `GET …/jobs/groups?paused=true` answer truthfully
  and survive a restart — 3.x pauses a job group by pausing the triggers of the jobs in it at that
  moment and recording nothing. Note that neither table pauses a *later* arrival into a paused job
  group: pausing a job group pauses the triggers of the jobs that were in it, and a job added
  afterwards fires. A group paused during an incident and never resumed is a common and entirely silent
  cause of "nothing runs"; in the dashboard both listings carry the flag.
- **Every trigger is blocked or in error.** `BLOCKED` means another firing of the same
  `[DisallowConcurrentExecution]` job is running — see the leak above. `ERROR` means the job could not
  be *built*, which is a composition-root failure rather than an execution failure and is fixed in the
  application, then cleared with `ResetTriggerFromErrorState`.
  [What the trigger states mean](../best-practices.md#what-the-trigger-states-mean) has the full table.

There is a fourth reason, and it is the nastiest because everything reports healthy: **the node is
reading the wrong tables.** A mistyped `JobStore:TablePrefix` connects to the right database, finds its
own empty table set, passes schema validation because those tables exist, starts, answers healthy and
fires nothing ever again. 4.x notices one shape of this — two schedulers in one container that share a
database and disagree about the prefix — and logs a warning naming both, which it does rather than
failing because separate table sets are a legitimate arrangement. It cannot notice the single-scheduler
case at all. If a scheduler is silent and every other explanation has been ruled out, count the rows in
the tables it is actually pointed at.

## Backup and restore

Back up the Quartz tables with the rest of the application's database, on the same schedule, and expect
the same recovery point. Nothing about Quartz needs special backup treatment. What needs thought is the
*restore*, because the Quartz tables are not only data — they are a distributed system's account of
what is running.

**Stop every node before restoring, and start them afterwards.** This is the rule every system with a
shared coordination store states. Kubernetes puts it most plainly for etcd: "If any API servers are
running in your cluster, you should not attempt to restore instances of etcd. Instead… stop *all* API
server instances, restore state in all etcd instances, restart all API server instances." The hazard
etcd's own documentation names is exactly the one here — a live process whose view of the store is
suddenly older than its own memory of it. Airflow's guidance is milder but the same shape: back up the
metadata database before any operation that modifies it, and "consider disabling the Airflow cluster
while you perform such maintenance".

**A point-in-time restore does not restore the work, only the record of it.** Both major engines
document the semantics without hedging: SQL Server recovers to "the latest transaction commit that
occurred at or before" the stop time, and PostgreSQL's own worked example is restoring to a minute
before a mistake and losing everything after it. For a scheduler that means:

- Triggers fire again from where the backup thought they were. A nightly job whose `PREV_FIRE_TIME` has
  been rolled back will run that night again; jobs written to be
  [idempotent](../best-practices.md#assume-the-job-will-run-more-than-once) do not care, and jobs that
  are not, do.
- Fired-trigger rows come back for firings that have already finished. Until they are swept, the
  cluster believes those jobs are running, and `[DisallowConcurrentExecution]` holds their job keys.
  Starting the nodes after the restore is what clears them: each node's first check-in recovers its own
  rows, and rows belonging to instance ids that are gone are swept as orphans on the same pass.
- Anything scheduled after the recovery point is gone, including one-off triggers an application
  created in response to something. If those matter, they have to be re-derivable from whatever created
  them.

**Prefer redeploying the schedule to restoring it.** The definitions — jobs, triggers, calendars — are
the part of the store that a deployment can put back. `AddJob` and `AddTrigger` in `AddQuartz`, or a
scheduling data file, mean the schedule is described in source control and re-applied on every start;
`SchedulingOptions.OverwriteExistingData` is on by default, so a start after a restore reconciles the
definitions back to what the code says. That leaves the backup responsible only for runtime state,
which is the part nobody can reconstruct anyway. This is the split Airflow makes structurally — DAGs
are Python files under version control and the metadata database holds only runs — and the reason its
restore guidance never mentions restoring workflow definitions.

Two things a restore does not need: the `QRTZ_LOCKS` rows are written by the lock handler when they are
missing, so a restore that loses them costs nothing, and Quartz keeps no state outside the database —
there is no node-local file to restore alongside it.

## Timeouts and transient failures

### CommandTimeout

`JobStore:CommandTimeout` bounds every statement the store issues, including the ones the lock handler
takes its row lock with. Left unset, each statement gets whatever the ADO.NET provider gives a new
command — usually 30 seconds. There is deliberately no per-statement override: every statement runs
inside a lock the rest of the cluster is waiting on, so none of the store's work is more expendable
than the rest.

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

        // How long the check-in and misfire loops back off after a failure that was not
        // transient — a database that is down rather than busy.
        options.DbRetryInterval = TimeSpan.FromSeconds(15);
    });
});
```
<!-- endSnippet -->

The case that decides the value is a node blocked on `QRTZ_LOCKS` behind a peer that stopped without
releasing the lock. Until the command times out, that node's scheduling loop is doing nothing at all.
A shorter timeout turns a long stall into a fast failure and a retry; too short a timeout turns a
merely busy database into a retry storm. ADO.NET counts whole seconds, and Quartz rounds a configured
value **up** — `00:00:01.500` is applied as 2 seconds — because rounding down would turn a sub-second
value into `0`, which every provider reads as "wait forever".

### What counts as transient

A failure the store considers transient is retried `MaxTransientRetries` times (default 3),
`TransientRetryInterval` apart (default 1 second). What qualifies, on 4.x:

- a `TimeoutException`, from anywhere in the exception chain;
- the driver's own verdict, `DbException.IsTransient`;
- **a SQLSTATE in class `40`** — the standard's "transaction rollback" class, which covers `40001`
  serialization failure and PostgreSQL's `40P01` deadlock detected, with `40002` excluded because a
  deferred constraint violation fails identically on every attempt. This one is **4.x only**, and it is
  what catches the drivers that do not implement `IsTransient` honestly: Firebird reports
  `IsTransient: false` for a serialization failure, so before this the store treated the one condition
  retrying exists for as fatal;
- SQL Server's transient error numbers, read off `SqlException.Errors`, which is where 1205 (deadlock
  victim) arrives because both SqlClients leave `SqlState` null;
- SQLite's busy and locked codes.

`AdoJobStoreOptions.IsTransient` is where you say so when a driver of your own reports something the
list above misses. It is a `Func<Exception, bool>` set in code, consulted before the list, and it can
only add — answering `false` is the same as not having one, so it cannot switch off a retry Quartz
already performs. The exception it is handed is the store's own, so reach the driver's with
`GetBaseException()`.

`DbRetryInterval` (default 15 seconds) is the different knob: it is how long the check-in and misfire
loops back off after a failure that was *not* transient — a database that is down rather than busy —
so that a cluster does not hammer a dead server every 7.5 seconds.

Retrying is not free of consequence in a cluster. A check-in that fails for longer than the failure
boundary means the peers write this node off while it is still working, so a database outage long
enough to exhaust the retries is also long enough to produce spurious failovers. That is the reason the
first section of [Best Practices](../best-practices.md#assume-the-job-will-run-more-than-once) starts
where it does.

## Sizing a cluster

The arithmetic is on Best Practices and is not repeated here:
[max concurrency is a permit count](../best-practices.md#max-concurrency-is-a-permit-count-not-a-thread-count),
and [the connection pool is the thread pool plus three](../best-practices.md#the-connection-pool-is-the-thread-pool-plus-three).
Four things change when the process is one of several.

**The database's connection budget is shared and does not scale with the node count.** Ten nodes with a
modest pool of 25 each present 250 connections to one server. The number to divide is the database's,
so `MaxConcurrency` is derived from the budget divided by the number of nodes rather than chosen per
node — which makes adding a node a decision about the database as much as about the application.

**Every node runs its own misfire handler.** The misfire loop is not a cluster singleton: each node
scans every `MisfireHandlerFrequency` (defaulting to `MisfireThreshold`, one minute), and with
`DoubleCheckLockMisfireHandler` on — the default — the scan starts with a `COUNT` that takes no lock
and only escalates to the cluster-wide lock when it finds something. So the baseline cost of a node is
one count query per minute; the contended cost is one lock per minute per node with work to do.

**Every node runs its own cluster manager.** One `SELECT` of the state table plus one `UPDATE` per node
per `CheckinInterval`. At the default of 7.5 seconds, a ten-node cluster is 160 statements a minute
before any job runs. This is the traffic that a shorter interval buys faster failure detection with.

**Batching trades round trips for balance.** `MaxBatchSize` above 1 makes every acquisition cycle take
the `TRIGGER_ACCESS` lock, including cycles that acquire nothing, and needs
`BatchTriggerAcquisitionFireAheadTimeWindow` above zero to batch anything at all. Java Quartz's warning
holds here too: the larger number comes at the cost of possible imbalanced load between nodes, because
a node that acquires ten triggers has made them its own until it can run them.
[Batching trigger acquisition](tutorial/advanced-enterprise-features.md#batching-trigger-acquisition)
has the pair in full.

Adding nodes does not make a single trigger fire faster, and it does not shorten a job. It adds
capacity for concurrent firings and it adds a node to fail over to. If the schedule is dominated by one
long job, a second node changes nothing about it.

## Health checks and probes

The check that ships with `Quartz` asserts two things: that the scheduler is in a state that can fire,
and that its job store answers a query. It reports *healthy* for a running scheduler whose store
responds, *degraded* for one in standby, and *unhealthy* for one that was created but never started, is
shutting down, has shut down, or whose store threw. It registers on the standard `IHealthChecksBuilder`
and needs nothing from ASP.NET Core, so a worker on a `dotnet/runtime` image carries it too.

<!-- snippet: sample_operations_readiness_probe -->
```csharp
// Tagged, so a readiness endpoint can select it while the liveness endpoint does not: a
// scheduler in standby, or one whose database is unreachable, should leave the rotation
// without the process being killed.
services.AddHealthChecks().AddQuartz(options => options.Tags.Add("ready"));
```
<!-- endSnippet -->

Three limits are worth being deliberate about.

**It does not assert that anything is firing.** A scheduler with an empty schedule, a paused group or a
starved thread pool is healthy by this definition. Pair it with an alert on a job you expect to see
regularly — the store is the source for that, since the shipped instruments do not cover it.

**It says nothing about the cluster.** A node that has stopped checking in — because its cluster
manager is wedged on the database while the rest of the process is fine — still answers healthy. The
node listing is where that shows, not the health endpoint.

**Degraded does not survive an HTTP probe by default.** ASP.NET Core maps `Degraded` to 200, exactly as
it maps `Healthy`, so a standby scheduler looks healthy to anything that reads the status code. Map
`Degraded` to 503 in `HealthCheckOptions.ResultStatusCodes` if a standby node should leave the
rotation. Under Aspire this interacts with `WithHttpHealthCheck`, and
[the Aspire how-to](how-tos/aspire.md) has the whole table, including the fact that a worker project
has no health endpoint to poll at all. That is what
`AddQuartzHealthChecks(options => options.StandbyStatus = HealthStatus.Unhealthy)` is for: it changes
the verdict itself rather than its status code, so a probe reading the `HealthCheckService` directly
sees it. It covers standby and nothing else — a scheduler still in `Created` because
`AutoStart = false` keeps reporting degraded.

For what to watch besides the probe — the `Quartz` activity source, the `quartz` meter and what the
instruments do and do not cover — see [Observability](packages/opentelemetry-integration.md), which
lists the instruments, and [What to watch](../best-practices.md#what-to-watch).

## See also

- [Clustering](tutorial/advanced-enterprise-features.md) — configuring a cluster in the first place
- [Troubleshooting](../troubleshooting.md) — symptoms and what to do about each
- [Best Practices](../best-practices.md) — the decisions this page assumes have been made
- [Database Schema](db/) and [Schema Changes](../database/schema-changes.md) — what the tables hold and
  what each version added
- [Configuration Reference](configuration/reference.md#persistent-job-store) — every setting named here,
  with its default

## Sources

Prior art surveyed in August 2026. Quartz.NET's own behaviour is stated from the source in this
repository rather than from any of these.

- Kubernetes, [StatefulSets](https://kubernetes.io/docs/concepts/workloads/controllers/statefulset/),
  [Force delete StatefulSet Pods](https://kubernetes.io/docs/tasks/run-application/force-delete-stateful-set-pod/),
  [Pod hostname](https://kubernetes.io/docs/concepts/workloads/pods/pod-hostname/),
  [Downward API](https://kubernetes.io/docs/concepts/workloads/pods/downward-api/) and
  [Operating etcd clusters](https://kubernetes.io/docs/tasks/administer-cluster/configure-upgrade-etcd/)
- etcd, [Disaster recovery](https://etcd.io/docs/v3.6/op-guide/recovery/)
- Hangfire, [Upgrading to Hangfire 1.8](https://docs.hangfire.io/en/latest/upgrade-guides/upgrading-to-hangfire-1.8.html),
  [Using SQL Server](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html) and
  [Running multiple server instances](https://docs.hangfire.io/en/latest/background-processing/running-multiple-server-instances.html)
- Temporal, [Upgrade Server](https://docs.temporal.io/self-hosted-guide/upgrade-server)
- Apache Airflow, [Upgrading](https://airflow.apache.org/docs/apache-airflow/stable/installation/upgrading.html)
  and [Best Practices](https://airflow.apache.org/docs/apache-airflow/stable/best-practices.html)
- Apache Kafka, [KRaft](https://kafka.apache.org/33/operations/kraft/); Strimzi,
  [Node ID management](https://strimzi.io/blog/2023/08/23/kafka-node-pools-node-id-management/)
- Microsoft, [Orleans on Kubernetes](https://learn.microsoft.com/dotnet/orleans/deployment/kubernetes),
  [Restore a SQL Server database to a point in time](https://learn.microsoft.com/sql/relational-databases/backup-restore/restore-a-sql-server-database-to-a-point-in-time-full-recovery-model)
  and [Azure SQL recovery using backups](https://learn.microsoft.com/azure/azure-sql/database/recovery-using-backups)
- PostgreSQL, [Continuous archiving and point-in-time recovery](https://www.postgresql.org/docs/current/continuous-archiving.html)
- Elastic, [Elastic Cloud on Kubernetes orchestration](https://www.elastic.co/guide/en/cloud-on-k8s/master/k8s-orchestration.html)
- Camunda, [Restore a backup](https://docs.camunda.io/docs/self-managed/operational-guides/backup-restore/restore/)
- Quartz (Java), [JDBC-JobStore clustering](https://www.quartz-scheduler.org/documentation/quartz-2.3.0/configuration/ConfigJDBCJobStoreClustering.html)
- Martin Fowler, [Parallel Change](https://martinfowler.com/bliki/ParallelChange.html)
