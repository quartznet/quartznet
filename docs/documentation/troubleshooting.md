---

title: Troubleshooting
---

# Troubleshooting

This guide covers common issues users encounter with Quartz.NET and how to diagnose and resolve them.

## Scheduler Stops Executing Jobs

**Symptoms:** Jobs stop firing after running for hours or days. No error messages in logs. The scheduler appears to be running but no triggers fire.

**Common Causes:**

1. **Thread pool exhaustion** — All worker threads are occupied by long-running jobs. Other jobs queue up and eventually misfire.
   * Check `quartz.threadPool.threadCount` (default: 10). Increase if you have many concurrent jobs.
   * Ensure jobs don't block threads indefinitely. Use cancellation tokens and timeouts.
   * Consider using `[DisallowConcurrentExecution]` to prevent a single slow job from consuming all threads.

2. **Database connectivity issues** — Transient database errors during trigger acquisition can leave the scheduler unable to pick up new triggers.
   * Check your database connection string and connection pool configuration.
   * Ensure your connection pool size is at least thread count + 3 (see [Best Practices](best-practices.md)).
   * Review database server logs for connection timeouts or deadlocks.

3. **Unhandled exceptions in listeners** — An exception thrown from a `IJobListener`, `ITriggerListener`, or `ISchedulerListener` can disrupt the scheduling cycle.
   * Always wrap listener code in try-catch blocks (see [Best Practices](best-practices.md#listeners-triggerlistener-joblistener-schedulerlistener)).

**Diagnosis Steps:**

1. Enable debug logging for `Quartz` namespace to see trigger acquisition activity.
2. Check `QRTZ_FIRED_TRIGGERS` table for jobs that never completed.
3. Check `QRTZ_TRIGGERS` table for triggers stuck in unexpected states (see next section).
4. Verify the scheduler is still started: `scheduler.IsStarted` should be `true`.

## Triggers Stuck in ACQUIRED State

**Symptoms:** Triggers show `TRIGGER_STATE = 'ACQUIRED'` in the database but never fire. New triggers are not being picked up.

**Causes:**

* The scheduler instance that acquired the trigger crashed or lost connectivity before it could fire.
* Transient database errors during the fire-and-complete cycle.

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

**Resolution:**

1. **Restart the scheduler** — On startup, Quartz performs misfire recovery and will re-evaluate stuck triggers based on their misfire instruction.
2. **Manual recovery** — If a restart is not possible, you can update stuck triggers back to `WAITING` state:

```sql
UPDATE QRTZ_TRIGGERS
SET TRIGGER_STATE = 'WAITING'
WHERE TRIGGER_STATE = 'ACQUIRED'
  AND NEXT_FIRE_TIME < :currentTimeInMillis;
```

::: warning
Only perform manual database updates as a last resort. Prefer restarting the scheduler to let Quartz handle recovery properly.
:::

**Prevention:**

* Ensure adequate database connection pool sizing.
* Use clustered mode if running multiple scheduler instances — it includes automatic recovery for failed nodes.
* Keep jobs short-running to minimize the window for failures.

## Misfire Handling

A **misfire** occurs when a trigger's scheduled fire time passes without the job being executed. This can happen because the scheduler was shut down, there were no available worker threads, or the system was under heavy load.

### How It Works

1. On startup (and periodically during operation), Quartz scans for triggers whose `NEXT_FIRE_TIME` is older than `now - misfireThreshold`.
2. For each misfired trigger, Quartz applies the trigger's configured misfire instruction.
3. The default `misfireThreshold` is 60 seconds (configurable via `quartz.jobStore.misfireThreshold`).

### Misfire Instructions by Trigger Type

| Trigger Type | Instruction | Behavior |
|-------------|-------------|----------|
| **SimpleTrigger** | `FireNow` | Fire immediately, remaining repeat count unchanged |
| | `RescheduleNowWithExistingRepeatCount` | Fire now, keep original repeat count |
| | `RescheduleNowWithRemainingRepeatCount` | Fire now, only remaining repeats |
| | `RescheduleNextWithExistingCount` | Skip to next scheduled time, keep original count |
| | `RescheduleNextWithRemainingCount` | Skip to next scheduled time, remaining count |
| **CronTrigger** | `FireOnceNow` | Fire immediately once, then resume schedule |
| | `DoNothing` | Skip missed firings, wait for next scheduled time |
| **RecurrenceTrigger** | `FireOnceNow` (default) | Fire immediately once, then resume schedule |
| | `DoNothing` | Skip missed firings, wait for next scheduled time |

The default "smart policy" varies by trigger type. For `CronTrigger` and `RecurrenceTrigger`, it defaults to `FireOnceNow`. For `SimpleTrigger`, it depends on the repeat count configuration.

### Tuning

If triggers misfire frequently under normal operation, consider:

* Increasing `quartz.threadPool.threadCount` to handle more concurrent jobs.
* Increasing `quartz.jobStore.misfireThreshold` if slight delays are acceptable.
* Splitting high-frequency triggers across multiple scheduler instances using clustering.

## Job Deserialization Failures After Refactoring

**Symptoms:** After renaming a job class, changing its namespace, or moving it to a different assembly, the scheduler throws `TypeLoadException` or `JobPersistenceException` on startup.

**Cause:** The `QRTZ_JOB_DETAILS` table stores the full type name (including namespace and assembly) in the `JOB_CLASS_NAME` column. When the type moves, the stored reference no longer resolves.

**Resolution:**

Update the stored type name in the database:

```sql
UPDATE QRTZ_JOB_DETAILS
SET JOB_CLASS_NAME = 'NewNamespace.NewClassName, NewAssembly'
WHERE JOB_CLASS_NAME = 'OldNamespace.OldClassName, OldAssembly';
```

**Prevention:**

* Keep job class names and namespaces stable across releases.
* If you must rename, apply the database update as part of your deployment process.
* Consider using the `JobType` abstraction introduced in Quartz 4.x for more flexible type resolution.

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
   * Ensure all scheduler instances use the same `quartz.scheduler.instanceName` only when clustering is enabled.
   * Never point multiple non-clustered schedulers at the same database tables (see [Best Practices](best-practices.md#adonet-jobstore)).

### Datasource Configuration Example

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

## Scheduler in Web Environments

### IIS App Pool Recycling

By default, IIS recycles and stops application pools due to inactivity. This will stop your Quartz scheduler.

**Solutions:**

**IIS 8+:** Configure your site as "Always Running" with preload enabled. See [Microsoft docs on Application Initialization](https://learn.microsoft.com/en-us/iis/get-started/whats-new-in-iis-8/iis-80-application-initialization).

**Use the Hosted Service integration** (recommended) — Register Quartz as a hosted service so it ties into the ASP.NET Core application lifecycle:

```csharp
services.AddQuartz(q =>
{
    // configure jobs and triggers
});
services.AddQuartzHostedService(q => q.WaitForJobsToComplete = true);
```

**Run as a separate process** — For critical scheduling, consider running the scheduler in a Windows Service or Linux systemd service rather than inside a web application.

### Graceful Shutdown

When the application shuts down, give jobs time to complete:

```csharp
services.AddQuartzHostedService(options =>
{
    options.WaitForJobsToComplete = true;
});
```

Jobs should check `IJobExecutionContext.CancellationToken` to respond to shutdown requests promptly.

## Common Error Messages

| Error | Likely Cause | Resolution |
|-------|-------------|------------|
| `ObjectAlreadyExistsException` | Attempting to schedule a job or trigger with a key that already exists | Use `scheduler.RescheduleJob()` to replace an existing trigger, or check existence first with `scheduler.CheckExists()` |
| `JobPersistenceException` | Database error during job store operation | Check database connectivity, connection pool size, and query timeouts |
| `SchedulerException: Scheduler has been shutdown` | Calling scheduler methods after `Shutdown()` | Ensure your application lifecycle correctly manages the scheduler |
| `TypeLoadException` on job execution | Job class not found — possibly renamed or moved | Update `JOB_CLASS_NAME` in `QRTZ_JOB_DETAILS` (see [Job Deserialization Failures](#job-deserialization-failures-after-refactoring)) |
| `JobExecutionException` | Unhandled exception inside `IJob.Execute()` | Add try-catch in your job's Execute method (see [Best Practices](best-practices.md#throwing-exceptions)) |
