---

title: Troubleshooting
---

# Troubleshooting

Common Quartz.NET problems, by symptom, with how to diagnose and fix each.

## Scheduler Stops Executing Jobs

**Symptoms:** jobs stop firing after hours or days. No errors in the logs. The scheduler appears to
be running, but no triggers fire.

**Common causes:**

1. **Thread pool exhaustion.** Long-running jobs occupy every worker; other jobs wait and eventually
   misfire.
   * Check the thread pool size (default 10): `ThreadPool:MaxConcurrency` in 4.x,
     `quartz.threadPool.threadCount` as a flat key on both versions. Raise it if you run many jobs
     at once.
   * Make sure jobs do not block threads indefinitely. Use cancellation tokens and timeouts.
   * Consider `[DisallowConcurrentExecution]` so one slow job cannot take every thread.
2. **Database connectivity.** Transient database errors during trigger acquisition can leave the
   scheduler unable to pick up new triggers.
   * Check the connection string and the connection pool configuration.
   * Make the connection pool at least the thread count + 3 (see
     [Best Practices](best-practices.md#the-connection-pool-is-the-thread-pool-plus-three)).
   * Check the database server's logs for connection timeouts or deadlocks.
3. **Unhandled exceptions in listeners.** An exception from an `IJobListener`, `ITriggerListener` or
   `ISchedulerListener` can disrupt the scheduling cycle.
   * Wrap listener code in try-catch (see
     [Best Practices](best-practices.md#listeners-run-in-the-middle-of-everything)).

**Diagnosis:**

1. Enable debug logging for the `Quartz` namespace to see trigger acquisition.
2. Check `QRTZ_FIRED_TRIGGERS` for jobs that never completed.
3. Check `QRTZ_TRIGGERS` for triggers stuck in unexpected states (see the next section).
4. Check that the scheduler is still firing: `scheduler.Status` is `SchedulerStatus.Running` in 4.x;
   on 3.x, `scheduler.IsStarted` is `true` and `scheduler.InStandbyMode` is `false`.

## Triggers Stuck in ACQUIRED State

**Symptoms:** triggers show `TRIGGER_STATE = 'ACQUIRED'` in the database but never fire. New triggers
are not picked up.

**Causes:**

* The scheduler instance that acquired the trigger crashed or lost connectivity before firing it.
* A transient database error during the fire-and-complete cycle: the reservation was written, but
  the statement that would have fired or released it did not run.

**Diagnosis:**

```sql
-- Find stuck triggers
SELECT TRIGGER_NAME, TRIGGER_GROUP, TRIGGER_STATE, NEXT_FIRE_TIME
FROM QRTZ_TRIGGERS
WHERE TRIGGER_STATE = 'ACQUIRED';

-- Find fired triggers that never completed
SELECT * FROM QRTZ_FIRED_TRIGGERS
WHERE STATE = 'ACQUIRED';
```

**Resolution: the store already does this.** On both versions, clustered or not,
`RecoverStaleAcquiredTriggers` runs on the persistent store's misfire loop, every
`MisfireHandlerFrequency` (by default the misfire threshold, one minute).

* For each of **this node's own** fired-trigger rows still `ACQUIRED` past the stale threshold, it
  sets the trigger back to `WAITING` (from `ACQUIRED` or `BLOCKED`, since a
  `[DisallowConcurrentExecution]` job's trigger may have moved on) and deletes the row.
* The stale threshold is **twice the misfire threshold, with a floor of two minutes**. The floor keeps
  it clear of normal acquisition, which takes at most one `IdleWaitTime` (30 seconds by default) plus
  the time to fire. There is no separate setting; widening `MisfireThreshold` widens it.

So stuck rows disappear on their own, a minute or two after they stopped moving. The sweep does not
touch:

* **Rows with another instance id.** Rows left by a node that is gone are cleaned up by cluster
  recovery once that node is declared failed; see
  [Operating a Cluster (4.x)](quartz-4.x/operations.md#when-a-peer-takes-over). For this node's rows
  swept by a peer that decided *it* was gone, see [Clock Skew Between Nodes](#clock-skew-between-nodes).
* **Rows in `EXECUTING` state.** They describe a job the node believes is running, and the node is
  the authority on that.

Wait one sweep. If nothing changes, find which instance id owns the rows. In 4.x,
`IScheduler.QueryFireInstances(new FireInstanceQuery { State = null })` lists them without SQL, and
`QueryClusterNodes()` says which of those instance ids still exist.

**Resolution, as a fallback:**

1. **Restart the scheduler.** A non-clustered scheduler frees every `ACQUIRED` and `BLOCKED` trigger
   and deletes every fired-trigger row at startup. A clustered one does the same for its own rows on
   its first check-in.
2. **Manual recovery.** If a restart is not possible, put the stuck triggers back to `WAITING`:

```sql
UPDATE QRTZ_TRIGGERS
SET TRIGGER_STATE = 'WAITING'
WHERE TRIGGER_STATE = 'ACQUIRED'
  AND NEXT_FIRE_TIME < :currentTimeInMillis;
```

::: warning
Update the database by hand only as a last resort, and never against a running cluster: the row you
edit may be one a node is about to fire, and its paired fired-trigger row is left behind. Prefer the
sweep or a restart.
:::

**Prevention:**

* Size the database connection pool adequately.
* Run clustered if you run several scheduler instances; clustering recovers failed nodes
  automatically.
* Keep jobs short to narrow the window for failures.

## A Lock Held by a Connection That Is Gone

**Symptoms:** every node of a cluster stops firing at the same moment, and the log is silent: no
exception, no misfire, no `SchedulerError`, not even a retry. The processes are healthy and the
scheduler reports itself running. In the database, a session is waiting on `QRTZ_LOCKS`, and the
session blocking it belongs to a client that no longer exists.

**Diagnosis:** ask the database who is blocking whom, then whether the blocker's client still exists.

```sql
-- Oracle
SELECT sid, serial#, status, last_call_et, blocking_session, event
FROM v$session
WHERE blocking_session IS NOT NULL
   OR sid IN (SELECT blocking_session FROM v$session WHERE blocking_session IS NOT NULL);

-- PostgreSQL: the blocker is the one sitting in 'idle in transaction'
SELECT pid, state, wait_event_type, wait_event, state_change, pg_blocking_pids(pid) AS blocked_by
FROM pg_stat_activity
WHERE backend_type = 'client backend';

-- SQL Server
SELECT session_id, blocking_session_id, wait_type, wait_time, command
FROM sys.dm_exec_requests
WHERE blocking_session_id <> 0;

-- MySQL
SELECT * FROM performance_schema.data_lock_waits;
```

It is this problem if the blocking session has been idle as long as the outage has lasted and belongs
to a node whose process is gone.

**Cause:** a node held the `TRIGGER_ACCESS` row lock, and its connection died without telling the
server.

* Killing the process sends a TCP reset, and the server ends the session at once. Killing a node
  therefore does *not* reproduce this.
* Cutting the network under a live process sends nothing: the socket is aborted on the client side
  and no FIN or RST reaches the server. The server keeps the session, its open transaction and the
  row lock until *it* notices the client is gone.
* When the node comes back, it connects on a fresh session and queues behind its own ghost.

**Quartz cannot release that lock, and neither can any other client.** A row lock belongs to the
session that took it; only the server can end a session that is no longer there. A blocked lock
statement returns nothing and throws nothing, so the handler's retry loop, the store's
transient-failure handling and the scheduler's error listener all wait with it in silence. Since
[#3764](https://github.com/quartznet/quartznet/issues/3764), Quartz can make the wait finite and
visible.

There are two fixes, for different halves of the problem. Apply both.

### Make the wait finite

This turns a stall into a failure the scheduler reports and recovers from. It does not free the lock.

* **`CommandTimeout`**: `JobStore:CommandTimeout` in 4.x, `quartz.jobStore.commandTimeout` from 3.22.
  It applies to every statement the store issues, the lock statement included, on every database.
  On Oracle, ODP.NET's
  [`CommandTimeout`](https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/CommandCommandTimeout.html)
  cancels the statement rather than ending the wait on the server. The cancel usually surfaces as
  `ORA-01013`, and on some managed-driver versions as `ORA-03111`. Either way Quartz treats it as a
  failed statement.
* **A wait timeout in the lock statement itself.** Oracle has no session-level DML lock wait timeout,
  so on Oracle this is the cleaner option: `FOR UPDATE WAIT 20` fails the statement with `ORA-30006`
  after twenty seconds.

<!-- snippet: sample_troubleshooting_oracle_lock_wait -->
```csharp
q.UsePersistentStore(s =>
{
    s.UseSystemTextJsonSerializer();
    s.UseOracle(connectionString);

    s.ConfigureStore(options =>
    {
        // Bounds every statement the store issues, the lock statement included: what would
        // have been a wait with no end becomes a failure the store retries and reports.
        options.CommandTimeout = TimeSpan.FromSeconds(30);

        // And Oracle's own wait timeout, written into the lock statement itself, which
        // fails it with ORA-30006 after twenty seconds. {0} is the table prefix, and the
        // @ parameter prefix is rewritten for the driver.
        options.SelectWithLockSql =
            "SELECT * FROM {0}LOCKS WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName FOR UPDATE WAIT 20";
    });
});
```
<!-- endSnippet -->

On 3.x the same statement is a flat key:

```text
quartz.jobStore.selectWithLockSQL = SELECT * FROM {0}LOCKS WHERE SCHED_NAME = @schedulerName AND LOCK_NAME = @lockName FOR UPDATE WAIT 20
```

* **PostgreSQL:** [`lock_timeout`](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-LOCK-TIMEOUT)
  aborts any statement that waits longer than it for a lock. Npgsql sets it per connection with
  `Options=-c lock_timeout=20000` in the connection string.
* **MySQL:** [`innodb_lock_wait_timeout`](https://dev.mysql.com/doc/refman/8.4/en/innodb-parameters.html#sysvar_innodb_lock_wait_timeout)
  already bounds the waiter at 50 seconds by default.

**What Quartz then does:**

* The row-lock handler tries the statement `MaxRetry` times (three by default, a second apart), then
  throws `LockException`.
* The scheduler thread reports the first failure through `ISchedulerListener.SchedulerError` and backs
  off `DbRetryInterval` before trying again.
* The check-in and misfire loops log every `RetryableActionErrorLogThreshold`-th consecutive failure
  as an error.

None of it is fatal. The scheduler picks up by itself the moment the lock is released; the cluster is
now loud instead of silent.

On 4.x it is loud before the timeout expires, too. An acquisition waiting longer than
`JobStore:LockWaitWarningThreshold` (30 seconds by default) logs **warning 3716** once, naming the
lock and how long it has waited. Every acquisition is measured on the
`quartz.jobstore.lock.wait.duration` histogram, tagged with `quartz.jobstore.lock`. Alert on that
warning: while a lock wait is in progress, nothing else in Quartz produces a signal.

### Make the server drop the dead session

This half frees the lock. It is configured on the database server, not in Quartz.

| Database | Server setting | Default |
|---|---|---|
| Oracle | [`SQLNET.EXPIRE_TIME=n`](https://docs.oracle.com/en/database/oracle/oracle-database/21/netrf/parameters-for-the-sqlnet.ora.html) in the **server's** `sqlnet.ora` | `0` (off) |
| Oracle | [`MAX_IDLE_BLOCKER_TIME`](https://docs.oracle.com/en/database/oracle/oracle-database/23/refrn/MAX_IDLE_BLOCKER_TIME.html) (19c and later, minutes, `ALTER SYSTEM`, per-PDB) | — |
| PostgreSQL | [`tcp_keepalives_idle` / `_interval` / `_count`](https://www.postgresql.org/docs/current/runtime-config-connection.html#GUC-TCP-KEEPALIVES-IDLE) | `0`: the operating system's values |
| PostgreSQL | [`idle_in_transaction_session_timeout`](https://www.postgresql.org/docs/current/runtime-config-client.html#GUC-IDLE-IN-TRANSACTION-SESSION-TIMEOUT) | — |
| SQL Server | [Keep Alive](https://learn.microsoft.com/en-us/sql/tools/configuration-manager/tcp-ip-properties-protocols-tab) on the TCP/IP protocol, in SQL Server Configuration Manager | [30 seconds, one-second retransmission interval](https://learn.microsoft.com/en-us/archive/blogs/sql_protocols/understand-special-tcpip-property-keep-alive-in-sql-server-2005) |
| MySQL | OS keepalive (`net.ipv4.tcp_keepalive_time`), and `wait_timeout` | Two hours on Linux; eight hours |

* **Oracle `EXPIRE_TIME`** is dead connection detection. It probes every *n* minutes and closes the
  connection when the client is gone, which ends the session and rolls its transaction back. Left at
  `0`, you depend on the operating system's TCP keepalive, typically two hours. A single-digit number
  of minutes is usual.
* **Oracle `MAX_IDLE_BLOCKER_TIME`** ends a session that has been **idle while blocking another
  session** for that long. A session running a long statement is never idle, so a long query or a
  slow job is not a candidate. The only Quartz session it can reach is one idle between the
  statements of a lock-holding transaction, which lasts milliseconds unless the process is paused; if
  one is caught there, the commit fails and the store retries. A few minutes is a good backstop next
  to `EXPIRE_TIME`.
* **PostgreSQL:** setting the keepalives makes the server notice a gone client on its own schedule.
  `idle_in_transaction_session_timeout` ends a session idle inside an open transaction, which is
  exactly the shape of a lock-holding ghost.
* **SQL Server** enables keep-alive on every connection, unlike the operating system default, so an
  orphaned connection is usually dropped within a minute and this case rarely lasts long.
* **MySQL:** the dead session lives until the OS keepalive gives up or `wait_timeout` closes the idle
  connection. Tune the keepalive: `innodb_lock_wait_timeout` covers the waiter, and nothing else
  covers the holder.

::: warning
**Client-side keepalive is not a substitute.** ODP.NET's `Keep Alive=true`, `(ENABLE=BROKEN)` in a
connect descriptor, and the client-side `EXPIRE_TIME` of newer clients help the *client* notice a
dead *server*. None of them frees a lock held by a session on the server, which is the direction
this failure runs in.
:::

### Rehearsing it

Reproduce it before you need to:

1. In a clustered node, put a breakpoint after the lock statement, or suspend the process.
2. Disable that machine's network adapter. Do not kill the process: that sends the reset that makes
   the server clean up.
3. Every other node stops firing within one lock attempt. On 4.x, warning 3716 appears after
   `LockWaitWarningThreshold` and the lock-wait histogram climbs.
4. With the server-side setting in place, the server drops the session, the lock is released and the
   cluster resumes on its own.

## The Misfire Sweep Times Out

**Symptoms:** `JobPersistenceException` with an inner timeout from the misfire handler, repeating every
minute; `Handling the first N triggers of M misfired triggers` in the log, never catching up; the
scheduler otherwise alive but firing late.

**Cause:** the sweep does too much work per pass for the time it is allowed, or the query that finds
misfired triggers is scanning. Three settings and one index decide it.

The sweep runs on every node. Each pass starts with a `COUNT` that takes no cluster-wide lock, to avoid
paying for the lock when there is nothing to do:
`WHERE SCHED_NAME = ? AND MISFIRE_INSTR <> -1 AND NEXT_FIRE_TIME <= ? AND TRIGGER_STATE = ?`.

* **4.x** serves it from the acquisition index `IDX_QRTZ_T_NFT_ST` on
  `(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)`: two equalities, a
  range, and `MISFIRE_INSTR` in the index so the `<> -1` never leaves it. 4.0 drops the second index,
  `IDX_QRTZ_T_NFT_ST_MISFIRE`, that four dialects had
  ([#3656](https://github.com/quartznet/quartznet/issues/3656)): it led with `MISFIRE_INSTR`, compared
  with `<>`, so it could not seek, and no measured optimizer picked it
  ([#3608](https://github.com/quartznet/quartznet/issues/3608)).
* **3.x** still ships `IDX_QRTZ_T_NFT_ST_MISFIRE` and sweeps from it.
* A schema older than the [3.20 index migration](database/schema-changes.md#version-3-20) has a
  different index shape. On a large `QRTZ_TRIGGERS`, this query is where a slow database first shows.

<!-- snippet: sample_troubleshooting_misfire_sweep -->
```csharp
q.UsePersistentStore(s =>
{
    s.UseSystemTextJsonSerializer();
    s.UseSqlServer(connectionString);

    s.ConfigureStore(options =>
    {
        // A pass handles at most this many triggers, then commits. Lower it when the
        // sweep is timing out; the loop comes straight back for the rest.
        options.MaxMisfiresToHandleAtATime = 20;

        // How often the sweep runs. Defaults to MisfireThreshold.
        options.MisfireHandlerFrequency = TimeSpan.FromMinutes(1);

        // Applied to every statement the store issues, this one included.
        options.CommandTimeout = TimeSpan.FromSeconds(30);
    });
});
```
<!-- endSnippet -->

**Resolution:**

* **Apply the current index set:** [`migrations/3.20`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.20)
  on 3.x, or section 5 of [`migrations/4.0`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0)
  on 4.x. This helps most and costs least.
* **Lower `MaxMisfiresToHandleAtATime`** (default 20). It bounds one pass; the loop comes back for the
  rest after a 50 ms pause, so a smaller number means more, shorter transactions, not less progress.
* **Raise `CommandTimeout`** (`JobStore:CommandTimeout` in 4.x) if the statements are slow rather
  than blocked. It applies to every statement the store issues, so a node waiting on the cluster-wide
  lock also waits this long before it can fail and retry.
* **Raise `MisfireThreshold`** if the schedule can tolerate more lateness. Fewer triggers cross the
  line, so there is less to sweep.

::: warning
**A non-clustered scheduler's startup sweep is unbounded on purpose**, on both versions. It handles
*every* misfired trigger in one pass, ignoring `MaxMisfiresToHandleAtATime`, so that a scheduler
starting after a long outage catches up before it fires. It is the pass most likely to time out on a
large schedule, and the batch size does not affect it; only the index and the timeout do. A clustered
scheduler has no such pass: its startup work is the first cluster check-in, which recovers fired
triggers rather than misfires, and the ordinary bounded sweep catches up afterwards.
:::

## Clock Skew Between Nodes

**Symptoms:** jobs run twice; a node logs
`This scheduler instance (…) is still active but was recovered by another instance in the cluster`;
nodes flip between `Alive` and `Failed` in the cluster listing with no matching outage.

**Cause:** clustered failure detection compares a timestamp one node wrote with another node's clock.
A node whose clock runs ahead of a peer's by more than the slack writes off a healthy peer, releases
its acquired triggers and re-runs its recovery-requesting jobs while the peer is still executing them.

**The database's clock plays no part.** `LAST_CHECKIN_TIME` holds the writing node's own clock
reading, and no SQL in the store asks the server for the time (no `GETDATE()`, `now()` or `SYSDATE`).
Setting the database server's clock does not fix this. Only the nodes' clocks agreeing with *each
other* matters, within the failed node's stored check-in interval plus the deciding node's check-in
misfire threshold.

**Resolution:** run a time-synchronisation service on every node; ordinary NTP is orders of magnitude
inside the requirement. If you cannot guarantee that, or that the process gets CPU promptly (a
starved process shows the same symptom with a perfect clock), widen the window with
`quartz.jobStore.clusterCheckinMisfireThreshold`.

**What the written-off node does, on 4.x.** A node that finds its own `QRTZ_SCHEDULER_STATE` row gone
on a check-in other than its first has been failed out by a peer. It:

* writes the row back; until then, the rest of the cluster does not see it;
* logs the warning above (event id `3501`), plus one naming the peer that recovered it (`3515`), or
  saying the peer cannot be named because more than one node has a state row and no row records who
  recovered whom (`3516`);
* counts the event on `quartz.cluster.recovery.trigger` with `quartz.cluster.recovered.instance.id`
  set to its **own** instance id. Alert on that series: it is a node reporting that it was written
  off while running. The count is 1, not a number of triggers; the peer's own measurement carries
  that number under the same attribute.

It does not recover its own fired triggers. The peer already released, rescheduled and deleted them,
so a second pass would schedule a second recovery trigger for a firing already being replayed.

On 3.x the node logs the warning and otherwise carries on. Neither version makes this *safe*: the
peer took over work from a process that is still running it, and only the clock fixes that. Rows
left in `ACQUIRED` by the same event clean themselves up; see
[Triggers Stuck in ACQUIRED State](#triggers-stuck-in-acquired-state).

[Clocks in a cluster](best-practices.md#clocks-in-a-cluster) has the arithmetic, the default window,
and why a pause matters more than an inaccurate clock. On 4.x,
[Operating a Cluster (4.x)](quartz-4.x/operations.md#when-a-peer-takes-over) states the exact
predicate, and `IScheduler.QueryClusterNodes()` shows what each node believes about the others.

## Misfire Handling

A **misfire** is a trigger whose scheduled fire time passed without the job running. Causes: the
scheduler was shut down, no worker thread was free, or the system was under heavy load.

### How It Works

1. On startup, and periodically while running, Quartz finds triggers whose `NEXT_FIRE_TIME` is at or
   before `now - misfireThreshold`.
2. It applies each misfired trigger's misfire instruction.

The default misfire threshold is 60 seconds for a persistent store: `JobStore:MisfireThreshold` in
4.x, `quartz.jobStore.misfireThreshold` as a flat key on both versions.

The threshold instant itself counts as late. In 4.x the rule is the same everywhere: the in-memory
store, the persistent store's periodic sweep, and the single-trigger path a resumed or unblocked
trigger takes. On 3.x the persistent store's sweep uses strictly *before*, so a trigger due at exactly
`now - misfireThreshold` is misfired in memory and, for one tick, not in the database.

### Misfire Instructions by Trigger Type

Quartz 4.x names the instructions on one enum per family (`SimpleTriggerMisfireInstruction`,
`CronTriggerMisfireInstruction` and so on). Quartz 3.x names the same values as constants under
`MisfireInstruction`, sometimes spelled longer.

| Trigger Type | 4.x | 3.x | Behavior |
|-------------|-----|-----|----------|
| **SimpleTrigger** | `FireNow` | `FireNow` | Fire immediately, remaining repeat count unchanged |
| | `NowWithExistingCount` | `RescheduleNowWithExistingRepeatCount` | Fire now, keep original repeat count |
| | `NowWithRemainingCount` | `RescheduleNowWithRemainingRepeatCount` | Fire now, only remaining repeats |
| | `NextWithExistingCount` | `RescheduleNextWithExistingCount` | Skip to next scheduled time, keep original count |
| | `NextWithRemainingCount` | `RescheduleNextWithRemainingCount` | Skip to next scheduled time, remaining count |
| **CronTrigger** | `FireAndProceed` | `FireOnceNow` | Fire immediately once, then resume schedule |
| | `DoNothing` | `DoNothing` | Skip missed firings, wait for next scheduled time |
| **RecurrenceTrigger** | `FireAndProceed` (default) | — | Fire immediately once, then resume schedule |
| | `DoNothing` | — | Skip missed firings, wait for next scheduled time |

Every family also has:

* `IgnoreMisfires`: fires every missed firing, as fast as it can.
* `SmartPolicy`: the default. For `CronTrigger` and `RecurrenceTrigger` it fires once now and
  resumes; for `SimpleTrigger` it depends on the repeat count.

[Choosing a misfire instruction](best-practices.md#choosing-a-misfire-instruction-by-its-consequence)
covers which to pick.

### Tuning

If triggers misfire often under normal load:

* Raise the thread pool size: `ThreadPool:MaxConcurrency` in 4.x, `quartz.threadPool.threadCount` as
  a flat key on both versions.
* Raise the misfire threshold if small delays are acceptable: `JobStore:MisfireThreshold` in 4.x,
  `quartz.jobStore.misfireThreshold` as a flat key on both.
* Spread high-frequency triggers across several scheduler instances with clustering.

## Job Deserialization Failures After Refactoring

**Symptoms:** after renaming a job class, changing its namespace or moving it to another assembly,
the scheduler throws `TypeLoadException` or `JobPersistenceException` on startup.

**Cause:** `QRTZ_JOB_DETAILS.JOB_CLASS_NAME` stores the full type name, including namespace and
assembly. When the type moves, the stored name no longer resolves. The job's trigger goes to `ERROR`
rather than firing, because the failure is in *building* the job, not running it; see
[What the trigger states mean](best-practices.md#what-the-trigger-states-mean).

**Resolution: rewrite the stored name.**

```sql
UPDATE QRTZ_JOB_DETAILS
SET JOB_CLASS_NAME = 'NewNamespace.NewClassName, NewAssembly'
WHERE JOB_CLASS_NAME = 'OldNamespace.OldClassName, OldAssembly';
```

Run it during the deployment that renames the type. Afterwards, clear any triggers that reached
`ERROR` with `IScheduler.ResetTriggerFromErrorState`.

**Resolution: declare the rename (4.x).** Map the old name to the new type, and every stored row
carrying the old name keeps resolving. This suits a rolling deployment: nodes still on the old build
write the old name while the new ones read it, so nothing needs rewriting while both run.

<!-- snippet: sample_troubleshooting_type_loader_map -->
```csharp
services.AddQuartz(q => q.UseTypeLoader(loader =>
{
    // Old assembly-qualified name as stored, and the type it means now. Keep the entry until
    // every row that could carry the old name has been rewritten or has aged out.
    loader.Map("Acme.Jobs.NightlyReport, Acme.Jobs", typeof(NightlyRollupJob));
}));
```
<!-- endSnippet -->

The same map binds from configuration, so a rename can ship in `appsettings.json` with the
deployment, without a rebuild:

```json
{
  "Quartz": {
    "TypeLoader": {
      "Aliases": {
        "Acme.Jobs.NightlyReport, Acme.Jobs": "Acme.Jobs.NightlyRollupJob, Acme.Jobs"
      }
    }
  }
}
```

How the map behaves:

* **Where it applies:** wherever Quartz turns a **string** into a type at run time: a stored
  `JOB_CLASS_NAME`, a job named in XML or JSON scheduling data, a `quartz.plugin.<name>.type` key.
* **Where it does not:** the flat keys naming a scheduler's own components (job store, thread pool,
  serializer, lock handler, job factory, instance id generator, time provider, connection provider)
  are resolved while the service collection is still being built, before any options exist, and are
  **not** aliased. Each names a type in a file you can edit.
* **Matching:** a key matches the whole stored name, or the part before the comma that starts the
  assembly. `Acme.Jobs.NightlyReport` covers any assembly spelling after it;
  `Acme.Jobs.NightlyReport, Acme.Jobs` covers only that one.
* **Validation:** an alias whose target names no loadable type **fails at startup**, naming both
  halves of the entry, rather than surfacing later as a `TypeLoadException` about the dead name.
* **Scope:** type loading is container-wide. A rename declared through any scheduler's builder applies
  to every scheduler in the container.
* **Nothing is written back.** A job read under an aliased name keeps the old spelling in
  `JOB_CLASS_NAME`. That makes the alias safe during a rollout, and makes the `UPDATE` above the way to
  retire it eventually. Enable `Debug` logging for `Quartz.Impl.SimpleTypeLoader` to see whether
  anything still hits an alias before you remove it.

**Resolution: a type loader of your own (4.x).** When the answer is not a table (a name resolved out of
a plugin's `AssemblyLoadContext`, or a naming scheme rather than a list), implement `ITypeLoader`, a
single method, and register it with `UseTypeLoader<T>()`:

<!-- snippet: sample_troubleshooting_type_loader_implementation -->
```csharp
/// <summary>
/// Resolves the type names stored in JOB_CLASS_NAME, translating the ones that have since moved.
/// </summary>
public sealed class RenameAwareTypeLoader : ITypeLoader
{
    // Old assembly-qualified name as stored, new type. Keep an entry until every row that could
    // carry the old name has been rewritten or has aged out.
    private static readonly Dictionary<string, Type> renamed = new(StringComparer.Ordinal)
    {
        ["Acme.Jobs.NightlyReport, Acme.Jobs"] = typeof(NightlyRollupJob)
    };

    public Type? LoadType(string name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return null;
        }

        if (renamed.TryGetValue(name, out Type? moved))
        {
            return moved;
        }

        // A name that cannot be resolved must throw rather than return null: Quartz only asks when
        // it already knows a type is required.
        return Type.GetType(name, throwOnError: true);
    }
}
```
<!-- endSnippet -->

<!-- snippet: sample_troubleshooting_type_loader -->
```csharp
services.AddQuartz(q => q.UseTypeLoader<RenameAwareTypeLoader>());
```
<!-- endSnippet -->

* It replaces the loader for the whole container, and the declared map with it: the map is read by
  the loader Quartz ships.
* It must **throw** for a name it cannot resolve. Quartz asks only when it knows a type is required,
  so a `null` would fail later with nothing to point at. Return `null` only for a null or empty name.

The loader Quartz ships also maps **Quartz's own** 3.x → 4.0 renames, logging a warning each time so
the configuration can be corrected:

* `Quartz.Spi.*` as `Quartz.Extensibility.*`
* `Quartz.Simpl.*` as `Quartz.Impl.*`
* `Quartz.Job.*` as `Quartz.Jobs.*`
* `Quartz.Plugin.*` as `Quartz.Plugins.*`
* `Quartz.Listener.*` as `Quartz.Listeners.*`
* the job stores' old names (`JobStoreTX`, `JobStoreCMT`) and the assemblies merged into the core
  package

**Prevention:**

* Keep job class names and namespaces stable across releases.
* To rename, declare the alias in the deployment that renames the type (4.x), and update the database
  in a later one, once nothing hits the alias.
* Name the type in one place: a `public static readonly JobKey` on the job class, and registration
  through `AddJob<T>()` rather than a type-name string.

## Database Connection Issues

**Symptoms:** `JobPersistenceException` with an inner `SqlException`/`NpgsqlException`, intermittent
"Couldn't obtain triggers" errors, or "Object cannot be cast from DBNull" errors.

**Common causes:**

1. **Connection pool too small.** The pool runs out under load.
   * Minimum: thread pool size + 3.
   * Clustered setups need extra connections for cluster management.
2. **Connection timeouts.** The database is slow or the network unreliable.
   * Set the store's `CommandTimeout` (`JobStore:CommandTimeout` in 4.x,
     `quartz.jobStore.commandTimeout` from 3.22), not a connection-string keyword. It bounds every
     statement the store issues and is the only setting that reaches the lock statement; not every
     driver has a connection-string equivalent, and ODP.NET has none.
   * Check network latency between the scheduler and the database server.
   * If the statements are stuck rather than slow, and the whole cluster with them, see
     [A Lock Held by a Connection That Is Gone](#a-lock-held-by-a-connection-that-is-gone).
3. **Lock contention.** Several scheduler instances compete for the same rows.
   * Two schedulers share a name (`Scheduler:InstanceName`, or `quartz.scheduler.instanceName`) only
     when they are meant to be one cluster, and then both must have clustering enabled.
   * Never point several non-clustered schedulers at the same tables (see
     [Best Practices](best-practices.md#one-name-per-cluster-one-id-per-node)).

### Datasource Configuration Example

<!-- snippet: sample_troubleshooting_pool_size -->
```csharp
services.AddQuartz(q =>
{
    q.UsePersistentStore(s =>
    {
        s.UseSystemTextJsonSerializer();
        s.UseSqlServer(connectionString);
        // Ensure your connection string has an adequate pool size
        // e.g., "...;Max Pool Size=25;"
    });
});
```
<!-- endSnippet -->

## Scheduler in Web Environments

### IIS App Pool Recycling

By default IIS recycles and stops idle application pools, which stops the scheduler. Fixes:

* **IIS 8+:** configure the site as "Always Running" with preload enabled. See
  [Application Initialization](https://learn.microsoft.com/en-us/iis/get-started/whats-new-in-iis-8/iis-80-application-initialization).
* **Use the hosted service integration** (recommended), so Quartz follows the ASP.NET Core
  application lifecycle:

<!-- snippet: sample_troubleshooting_wait_for_jobs -->
```csharp
services.AddQuartz(q =>
{
    // configure jobs and triggers
});
services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

* **Run a separate process.** For critical scheduling, run the scheduler as a Windows Service or a
  Linux systemd service instead of inside a web application.

### Graceful Shutdown

To give jobs time to complete when the application shuts down:

<!-- snippet: sample_troubleshooting_wait_for_jobs_block -->
```csharp
services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});
```
<!-- endSnippet -->

Jobs should check `IJobExecutionContext.CancellationToken` so they respond to shutdown promptly. See
[Shutdown has a deadline](best-practices.md#shutdown-has-a-deadline) for the time limits.

## Common Error Messages

| Error | Likely cause | Resolution |
|-------|-------------|------------|
| `ObjectAlreadyExistsException` | Scheduling a job or trigger whose key already exists | `scheduler.RescheduleJob()` to replace a trigger, or check first with `scheduler.Exists()` (3.x: `scheduler.CheckExists()`) |
| `JobPersistenceException` | Database error in a job store operation | Check connectivity, pool size and query timeouts |
| `SchedulerException: Scheduler has been shutdown` | Calling the scheduler after `Shutdown()` | Fix the application's scheduler lifecycle |
| `TypeLoadException` on job execution | Job class renamed or moved | Declare the rename (4.x) or update `JOB_CLASS_NAME` in `QRTZ_JOB_DETAILS`; see [Job Deserialization Failures](#job-deserialization-failures-after-refactoring) |
| `JobExecutionException` | Unhandled exception inside `IJob.Execute()` | Catch it in the job; see [Best Practices](best-practices.md#what-happens-when-a-job-throws) |
