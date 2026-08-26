---

title: Troubleshooting
---

# Troubleshooting

This guide covers common issues users encounter with Quartz.NET and how to diagnose and resolve them.

## Scheduler Stops Executing Jobs

**Symptoms:** Jobs stop firing after running for hours or days. No error messages in logs. The scheduler appears to be running but no triggers fire.

**Common Causes:**

1. **Thread pool exhaustion** — All worker threads are occupied by long-running jobs. Other jobs queue up and eventually misfire.
   * Check the thread pool size (default: 10) — `ThreadPool:MaxConcurrency` in 4.x,
     `quartz.threadPool.threadCount` as a flat key on both versions. Increase it if you have many
     concurrent jobs.
   * Ensure jobs don't block threads indefinitely. Use cancellation tokens and timeouts.
   * Consider using `[DisallowConcurrentExecution]` to prevent a single slow job from consuming all threads.

2. **Database connectivity issues** — Transient database errors during trigger acquisition can leave the scheduler unable to pick up new triggers.
   * Check your database connection string and connection pool configuration.
   * Ensure your connection pool size is at least thread count + 3 (see [Best Practices](best-practices.md#the-connection-pool-is-the-thread-pool-plus-three)).
   * Review database server logs for connection timeouts or deadlocks.

3. **Unhandled exceptions in listeners** — An exception thrown from a `IJobListener`, `ITriggerListener`, or `ISchedulerListener` can disrupt the scheduling cycle.
   * Always wrap listener code in try-catch blocks (see [Best Practices](best-practices.md#listeners-run-in-the-middle-of-everything)).

**Diagnosis Steps:**

1. Enable debug logging for `Quartz` namespace to see trigger acquisition activity.
2. Check `QRTZ_FIRED_TRIGGERS` table for jobs that never completed.
3. Check `QRTZ_TRIGGERS` table for triggers stuck in unexpected states (see next section).
4. Verify the scheduler is still firing: `scheduler.Status` should be `SchedulerStatus.Running` in 4.x,
   `scheduler.IsStarted` should be `true` and `scheduler.InStandbyMode` `false` on 3.x.

## Triggers Stuck in ACQUIRED State

**Symptoms:** Triggers show `TRIGGER_STATE = 'ACQUIRED'` in the database but never fire. New triggers are not being picked up.

**Causes:**

* The scheduler instance that acquired the trigger crashed or lost connectivity before it could fire.
* Transient database errors during the fire-and-complete cycle — the reservation was written, and the
  statement that would have fired it or released it did not run.

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

**Resolution: the store already does this.** `RecoverStaleAcquiredTriggers` runs on the persistent
store's misfire loop — every `MisfireHandlerFrequency`, which defaults to the misfire threshold, one
minute — on both versions, and whether or not the scheduler is clustered. For each of **this node's
own** fired-trigger rows still in `ACQUIRED` state past the stale threshold, it puts the trigger back to
`WAITING` (from `ACQUIRED` or `BLOCKED`, since a `[DisallowConcurrentExecution]` job's trigger may have
moved on) and deletes the row. It is worth knowing about because it is easy to mistake for something
having gone wrong: rows disappear on their own, a minute or two after they stopped moving.

The stale threshold is derived rather than configured: it is **twice the misfire threshold, with a floor
of two minutes**. The floor is what keeps it clear of normal acquisition, which takes at most one
`IdleWaitTime` — 30 seconds by default — plus the time to fire. Widening `MisfireThreshold` widens this
with it; there is no separate setting.

Two things it deliberately does not do:

* It only touches rows carrying **its own** instance id. A row left by a node that is gone is cleaned up
  by cluster recovery once that node is declared failed, not by this sweep — see
  [Operating a Cluster (4.x)](quartz-4.x/operations.md#when-a-peer-takes-over).
* It does not touch rows in `EXECUTING` state. Those describe a job the node believes is running, and
  the node is the authority on that.

So the ordinary answer is to wait one sweep, and if nothing changes, to find out which instance id owns
the rows. In 4.x, `IScheduler.QueryFireInstances(new FireInstanceQuery { State = null })` lists them
without SQL, and `QueryClusterNodes()` says which of those instance ids still exist.

**Resolution, as a fallback:**

1. **Restart the scheduler.** A non-clustered scheduler frees every `ACQUIRED` and `BLOCKED` trigger and
   deletes every fired-trigger row at startup. A clustered one does the same for its own rows on its
   first check-in.
2. **Manual recovery** — if a restart is not possible, put the stuck triggers back to `WAITING`:

```sql
UPDATE QRTZ_TRIGGERS
SET TRIGGER_STATE = 'WAITING'
WHERE TRIGGER_STATE = 'ACQUIRED'
  AND NEXT_FIRE_TIME < :currentTimeInMillis;
```

::: warning
Only perform manual database updates as a last resort, and never against a running cluster: the row you
edit may be one a node is about to fire, and the fired-trigger row that pairs with it is left behind.
Prefer letting the sweep or a restart handle it.
:::

**Prevention:**

* Ensure adequate database connection pool sizing.
* Use clustered mode if running multiple scheduler instances — it includes automatic recovery for failed nodes.
* Keep jobs short-running to minimize the window for failures.

## The Misfire Sweep Times Out

**Symptoms:** `JobPersistenceException` with an inner timeout from the misfire handler, repeating every
minute; `Handling the first N triggers of M misfired triggers` in the log and never catching up; the
scheduler otherwise alive but firing late.

**Cause:** the sweep is doing too much work per pass for the time it is allowed, or the query that finds
misfired triggers is scanning. Three settings and one index decide it.

The sweep runs on every node, and each pass starts with a `COUNT` that takes no cluster-wide lock — the
double-check that avoids paying for the lock when there is nothing to do. That count is
`WHERE SCHED_NAME = ? AND MISFIRE_INSTR <> -1 AND NEXT_FIRE_TIME <= ? AND TRIGGER_STATE = ?`, which the
4.x index `IDX_QRTZ_T_NFT_ST_MISFIRE` on `(SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)`
serves. A schema that predates the [3.20 index migration](database/schema-changes.md#version-3-20) has a
different index shape, and on a large `QRTZ_TRIGGERS` this query is where a slow database first shows.

<!-- snippet: sample_troubleshooting_misfire_sweep -->
```csharp
q.UsePersistentStore(s =>
{
    s.UseSystemTextJsonSerializer();
    s.UseSqlServer(connectionString);

    s.Configure(options =>
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

* **Apply the current index set.** [`migrations/3.20`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.20)
  on 3.x, or section 5 of [`migrations/4.0`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0)
  on 4.x. This is the fix that helps most and costs least.
* **Lower `MaxMisfiresToHandleAtATime`** (default 20). It bounds one pass; the loop comes straight back
  for the rest after a 50 ms pause, so a smaller number means more, shorter transactions rather than less
  progress.
* **Raise `CommandTimeout`** — `JobStore:CommandTimeout` in 4.x — if the statements are genuinely slow
  rather than blocked. It applies to every statement the store issues, so raise it knowing that a node
  waiting on the cluster-wide lock waits this long before it can fail and retry.
* **Raise `MisfireThreshold`** if the schedule can tolerate more lateness. Fewer triggers cross the line,
  so there is less to sweep.

::: warning
**A non-clustered scheduler's startup sweep is unbounded on purpose**, on both versions: it handles
*every* misfired trigger in one pass, ignoring `MaxMisfiresToHandleAtATime`, so that a scheduler
starting after a long outage is caught up before it begins firing. That is the pass most likely to time
out on a large schedule, and lowering the batch size does not affect it — only the index and the timeout
do. A clustered scheduler does no such pass: its startup work is the first cluster check-in, which
recovers fired triggers rather than misfires, and the ordinary bounded sweep catches up afterwards.
:::

## Clock Skew Between Nodes

**Symptoms:** jobs run twice; a node logs
`This scheduler instance (…) is still active but was recovered by another instance in the cluster`;
nodes flip between `Alive` and `Failed` in the cluster listing with no corresponding outage.

**Cause:** clustered failure detection compares a timestamp one node wrote against another node's clock.
A node whose clock runs ahead of a peer's by more than the slack in that comparison writes off a healthy
peer, releases its acquired triggers and re-runs its recovery-requesting jobs — while it is still
executing them.

**Resolution:** run a time-synchronisation service on every node; that is the fix, and ordinary NTP is
orders of magnitude inside the requirement. Where you cannot guarantee it — or cannot guarantee that the
process gets CPU promptly, which produces the same symptom with a perfect clock — widen the window with
`quartz.jobStore.clusterCheckinMisfireThreshold`.

[Clocks in a cluster](best-practices.md#clocks-in-a-cluster) has the arithmetic, the size of the default
window, and why a pause matters more than an inaccuracy. In 4.x,
[Operating a Cluster (4.x)](quartz-4.x/operations.md#when-a-peer-takes-over) states the exact predicate, and
`IScheduler.QueryClusterNodes()` shows what each node currently believes about the others.

## Misfire Handling

A **misfire** occurs when a trigger's scheduled fire time passes without the job being executed. This can happen because the scheduler was shut down, there were no available worker threads, or the system was under heavy load.

### How It Works

1. On startup (and periodically during operation), Quartz scans for triggers whose `NEXT_FIRE_TIME` is at or older than `now - misfireThreshold`.
2. For each misfired trigger, Quartz applies the trigger's configured misfire instruction.
3. The default misfire threshold is 60 seconds for a persistent store — `JobStore:MisfireThreshold` in 4.x,
   `quartz.jobStore.misfireThreshold` as a flat key on both versions.

A trigger is misfired when its fire time is at or before `now - misfireThreshold` — the threshold
instant itself counts as late. In 4.x that is one rule wherever the question is asked: the in-memory
store, the persistent store's periodic sweep, and the single-trigger path a resumed or unblocked
trigger goes through all draw the line in the same place. On 3.x the persistent store's sweep is
strictly *before* the threshold instant, so a trigger due at exactly `now - misfireThreshold` is
misfired in memory and, for one tick, not in the database.

### Misfire Instructions by Trigger Type

Each family of triggers has its own instructions. Quartz 4.x names them on an enum per family
(`SimpleTriggerMisfireInstruction`, `CronTriggerMisfireInstruction` and so on); Quartz 3.x names the same
values as constants under `MisfireInstruction`, sometimes with a longer spelling.

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

Every family also has `IgnoreMisfires`, which fires every missed firing as fast as it can, and
`SmartPolicy`, which is the default. What smart policy resolves to varies by trigger type: for
`CronTrigger` and `RecurrenceTrigger` it fires once now and resumes; for `SimpleTrigger` it depends on the
repeat count.

### Tuning

If triggers misfire frequently under normal operation, consider:

* Raising the thread pool size to handle more concurrent jobs — `ThreadPool:MaxConcurrency` in 4.x,
  `quartz.threadPool.threadCount` as a flat key on both versions.
* Raising the misfire threshold if slight delays are acceptable — `JobStore:MisfireThreshold` in 4.x,
  `quartz.jobStore.misfireThreshold` as a flat key on both.
* Splitting high-frequency triggers across multiple scheduler instances using clustering.

## Job Deserialization Failures After Refactoring

**Symptoms:** After renaming a job class, changing its namespace, or moving it to a different assembly, the scheduler throws `TypeLoadException` or `JobPersistenceException` on startup.

**Cause:** The `QRTZ_JOB_DETAILS` table stores the full type name (including namespace and assembly) in the `JOB_CLASS_NAME` column. When the type moves, the stored reference no longer resolves.

The trigger for such a job goes to `ERROR` rather than firing, because the failure is in *building* the
job rather than in running it — see
[What the trigger states mean](best-practices.md#what-the-trigger-states-mean).

**Resolution — rewrite the stored name:**

```sql
UPDATE QRTZ_JOB_DETAILS
SET JOB_CLASS_NAME = 'NewNamespace.NewClassName, NewAssembly'
WHERE JOB_CLASS_NAME = 'OldNamespace.OldClassName, OldAssembly';
```

Run it during the deployment that renames the type, and clear the affected triggers with
`IScheduler.ResetTriggerFromErrorState` afterwards if any reached `ERROR` first.

**Resolution — teach the scheduler the old name (4.x):**

Every stored type name is resolved through `ITypeLoader`, which is a single method and a replaceable
seam. An implementation that consults a rename table before falling back to `Type.GetType` makes the old
name keep working without touching a row, which is what a rolling deployment needs: the nodes still
running the old build write the old name while the new ones read it.

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

An implementation must **throw** rather than return `null` for a name it cannot resolve — Quartz only
asks when it already knows a type is required, so a `null` surfaces later with nothing left to point at.
`null` is reserved for a null or empty name.

The loader Quartz ships already does this for **Quartz's own** 3.x → 4.0 renames: it retries
`Quartz.Spi.*` as `Quartz.Extensibility.*`, `Quartz.Simpl.*` as `Quartz.Impl.*`, `Quartz.Job.*` as
`Quartz.Jobs.*`, `Quartz.Plugin.*` as `Quartz.Plugins.*`, `Quartz.Listener.*` as `Quartz.Listeners.*`,
the job stores' old names (`JobStoreTX`, `JobStoreCMT`) and the assemblies that were merged into the
core package, logging a warning each time so the configuration can be corrected. It knows nothing about
your types, which is what the sample above is for.

**Prevention:**

* Keep job class names and namespaces stable across releases.
* If you must rename, apply the database update as part of your deployment process.
* Name the type in one place — a `public static readonly JobKey` on the job class, and registration
  through `AddJob<T>()` rather than a type-name string.

## Database Connection Issues

**Symptoms:** `JobPersistenceException` with inner `SqlException`/`NpgsqlException`, intermittent "Couldn't obtain triggers" errors, or "Object cannot be cast from DBNull" errors.

**Common Causes:**

1. **Insufficient connection pool size** — The connection pool is exhausted under load.
   * Recommended minimum: thread pool size + 3.
   * For clustered setups, account for the additional cluster management connections.

2. **Connection timeouts** — The database is slow to respond or the network is unreliable.
   * Increase `CommandTimeout` in your connection string.
   * Verify network latency between the scheduler and database server.

3. **Lock contention** — Multiple scheduler instances competing for the same rows.
   * Two schedulers share a name (`Scheduler:InstanceName`, or `quartz.scheduler.instanceName`) only when
     they are meant to be one cluster, and then clustering has to be enabled on both.
   * Never point multiple non-clustered schedulers at the same database tables (see [Best Practices](best-practices.md#one-name-per-cluster-one-id-per-node)).

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

By default, IIS recycles and stops application pools due to inactivity. This will stop your Quartz scheduler.

**Solutions:**

**IIS 8+:** Configure your site as "Always Running" with preload enabled. See [Microsoft docs on Application Initialization](https://learn.microsoft.com/en-us/iis/get-started/whats-new-in-iis-8/iis-80-application-initialization).

**Use the Hosted Service integration** (recommended) — Register Quartz as a hosted service so it ties into the ASP.NET Core application lifecycle:

<!-- snippet: sample_troubleshooting_wait_for_jobs -->
```csharp
services.AddQuartz(q =>
{
    // configure jobs and triggers
});
services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```
<!-- endSnippet -->

**Run as a separate process** — For critical scheduling, consider running the scheduler in a Windows Service or Linux systemd service rather than inside a web application.

### Graceful Shutdown

When the application shuts down, give jobs time to complete:

<!-- snippet: sample_troubleshooting_wait_for_jobs_block -->
```csharp
services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});
```
<!-- endSnippet -->

Jobs should check `IJobExecutionContext.CancellationToken` to respond to shutdown requests promptly.

## Common Error Messages

| Error | Likely Cause | Resolution |
|-------|-------------|------------|
| `ObjectAlreadyExistsException` | Attempting to schedule a job or trigger with a key that already exists | Use `scheduler.RescheduleJob()` to replace an existing trigger, or check existence first with `scheduler.Exists()` (Quartz 4.x; on Quartz 3.x the method is `scheduler.CheckExists()`) |
| `JobPersistenceException` | Database error during job store operation | Check database connectivity, connection pool size, and query timeouts |
| `SchedulerException: Scheduler has been shutdown` | Calling scheduler methods after `Shutdown()` | Ensure your application lifecycle correctly manages the scheduler |
| `TypeLoadException` on job execution | Job class not found — possibly renamed or moved | Update `JOB_CLASS_NAME` in `QRTZ_JOB_DETAILS` (see [Job Deserialization Failures](#job-deserialization-failures-after-refactoring)) |
| `JobExecutionException` | Unhandled exception inside `IJob.Execute()` | Add try-catch in your job's Execute method (see [Best Practices](best-practices.md#what-happens-when-a-job-throws)) |
