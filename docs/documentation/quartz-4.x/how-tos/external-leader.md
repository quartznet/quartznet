---

title: 'Running under an External Leader Election'
---

# Running under an External Leader Election

When an application already elects a leader (a Kubernetes `Lease`, Wolverine's leader-pinned agents, a
Consul or etcd session, a database advisory lock, a single bus consumer), run **a persistent job store with
clustering off, one process scheduling at a time, started and stopped by that election**, instead of a
second election inside Quartz.

::: warning This is not the default answer
Use [clustering](../tutorial/advanced-enterprise-features.md) unless you have a reason not to: it recovers
a dead node's firings, which nothing here does, and lets every node work. Choose an external election when
it already exists and must gate several components, or when it is all your platform offers.
:::

## Not clustering, and staying that way

Leave clustering off by not calling `UseClustering()`, not by calling it and disabling it:

```csharp
// Refused at startup
q.UsePersistentStore(store => store.UseClustering(c => c.Enabled = false));
```

`UseClustering` sets `ClusteringOptions.Enabled` and also `AdoJobStoreOptions.UseDbLocks`. Turning `Enabled`
off again (in the callback or a later `Configure<ClusteringOptions>`) would leave database locking with no
cluster manager or check-in row, so `ClusteringStaysEnabledValidator` refuses it when the host starts
(`ValidateOnStart`). The check is per scheduler, so a sibling scheduler can run un-clustered.

A non-clustered persistent store:

* writes no `QRTZ_SCHEDULER_STATE` row and runs no check-in, so `QueryClusterNodes()` returns nothing;
* never runs the failover sweep, so no peer takes over a dead node's firings;
* takes its `TRIGGER_ACCESS` lock **in process** (`InProcessLockHandler`), excluding only one scheduler's
  threads.

So the election is required for correctness; see [Two leaders at once](#two-leaders-at-once).

## Building it

<!-- snippet: sample_external_leader_registration -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.UsePersistentStore(store => store.UseSqlServer(connectionString));

    // No UseClustering(). Exactly one process is meant to be scheduling, and the election
    // outside Quartz is what says which one.
});

builder.Services.AddQuartzHostedService(options =>
{
    // Built, initialized and bound with the host - and then left alone. The leader starts it.
    options.AutoStart = false;

    // A leader that is stepping down because the host is stopping should finish what it began.
    options.WaitForJobsToComplete = true;
});

builder.Services.AddHealthChecks().AddQuartz();
```
<!-- endSnippet -->

`AutoStart = false` builds, initializes and binds the scheduler with the host and leaves it in `Created`.
Shutdown still runs, whether or not it was started. See the
[hosted service page](../packages/hosted-services-integration.md#a-scheduler-the-application-starts-itself).

## Starting and standing down

<!-- snippet: sample_external_leader_callbacks -->
```csharp
/// <summary>
/// The two callbacks every leader election has, whatever it calls them.
/// </summary>
public sealed class SchedulerLeadership(IScheduler scheduler)
{
    public ValueTask OnStartedLeading(CancellationToken cancellationToken)
    {
        // The first acquisition starts the scheduler and every later one resumes it from standby.
        // Start does both, and does nothing when the scheduler is already running.
        return scheduler.Start(cancellationToken);
    }

    public ValueTask OnStoppedLeading(CancellationToken cancellationToken)
    {
        // Standby, not Shutdown: a shut-down scheduler cannot be started again, and this process
        // may well be elected once more in a minute. Losing the lease while the host is already
        // stopping is ordinary, and Standby throws once the scheduler has shut down.
        return scheduler.Status == SchedulerStatus.Running
            ? scheduler.Standby(cancellationToken)
            : default;
    }
}
```
<!-- endSnippet -->

Wire these to your election: `OnStartedLeading`/`OnStoppedLeading` on the Kubernetes C# client's
[`LeaderElector`](https://github.com/kubernetes-client/csharp), a Wolverine agent's start and stop,
`PostCreate`/`PreStop` on a MassTransit bus observer, or a distributed lock's acquire and release.

* **`Start` is idempotent and resumes from standby.** Only the first call runs start-up recovery and starts
  plugins, so re-election is cheap.
* **Use `Standby`, not `Shutdown`.** After shutdown, `Start` throws
  *"The Scheduler cannot be restarted after Shutdown() has been called."*;
  [`ISchedulerRuntime.Restart`](../multi-tenancy.md#restarting-a-scheduler) would build a whole new
  scheduler.
* **`Standby` after shutdown throws** `SchedulerException("The Scheduler has been Shutdown.")`. Losing the
  lease while the host stops is normal, so check `Status` first.

`SchedulerStatus` (`Created`, `Running`, `Standby`, `ShuttingDown`, `Shutdown`) replaces 3.x's
`IsStarted` / `InStandbyMode` / `IsShutdown`. A never-started scheduler stands down to `Created`.

### What standby does, and what it does not

Standby pauses the scheduling loop and tells the store. It does **not**:

* **stop running jobs.** A job that must stop watches `IJobExecutionContext.CancellationToken`.
* **release already acquired triggers.** A node that just stood down can still fire up to `MaxBatchSize`
  triggers, up to `IdleWaitTime` later. Only shutdown releases an acquired batch.
* **stop the misfire handler.** It starts on the first start, stops only on shutdown, and keeps scanning
  every `MisfireHandlerFrequency` (default `MisfireThreshold`, one minute for the ADO store), writing
  trigger state.

**Standby means "not acquiring", not "not touching the database".** A never-elected process is inert (no
misfire handler until the first `Start()`); one that led and stood down is not.

::: tip A process that only writes the schedule
A process that will *never* be elected (an admin API, a migration tool) should use
`q.UseThreadPool<ZeroSizeThreadPool>()`, which is public for this. It creates no threads, and the two
members a running scheduler calls throw `NotSupportedException`, so starting it fails loudly. It need not
reference your job assemblies: the store reads, edits, pauses, reschedules, triggers and deletes by stored
type name. The dashboard's [`AttachStore`](../packages/dashboard.md#store-attached-targets) creates one of
these per scheduler it discovers in a database.
:::

The health check reports *degraded*, not *unhealthy*, in `Created` with `AutoStart = false` and in
`Standby`, so a non-leader replica stays in rotation. See
[Health checks](../packages/hosted-services-integration.md#health-checks).

## What the loop costs while it waits

| Setting | Default | What it decides |
|---|---|---|
| `QuartzSchedulerOptions.IdleWaitTime` | 30 seconds | how long the loop waits before asking the store again when it found nothing; at least one second |
| `QuartzSchedulerOptions.MaxBatchSize` | 1 | the most triggers acquired per round; must not exceed `ThreadPoolOptions.MaxConcurrency` |
| `QuartzSchedulerOptions.BatchTriggerAcquisitionFireAheadTimeWindow` | `TimeSpan.Zero` | how early a trigger may fire to join a batch already forming |

* **`IdleWaitTime` is not latency for work scheduled in this process**: every scheduling call signals the
  loop. It bounds pickup of triggers written by **another process** (an API node, a migration, a
  hand-written row); nothing signals the loop from outside.
* The wait is randomized within `[0.8 × IdleWaitTime, IdleWaitTime)`, so nodes do not poll in step.
* A round acquires triggers due within the next `IdleWaitTime`, so a trigger due during the sleep is late,
  not lost.
* Change `MaxBatchSize` and the fire-ahead window together: a batch stops at the first trigger not due
  within the opening trigger's window.

<!-- snippet: sample_external_leader_tuning -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.ConfigureScheduler(options =>
    {
        // How long a trigger written by another process may sit before this one looks again.
        options.IdleWaitTime = TimeSpan.FromSeconds(5);

        // Both halves or neither: a batch stops at the first trigger that is not due within
        // the window of the one that opened it.
        options.MaxBatchSize = 10;
        options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(2);
    });

    // MaxBatchSize may not exceed this: triggers acquired beyond the number of threads there
    // are to run them on are held by this node, unfireable by any other, until the pool drains.
    q.UseDefaultThreadPool(maxConcurrency: 10);
});
```
<!-- endSnippet -->

Costs: a batch size above one takes the `TRIGGER_ACCESS` lock on every round, even empty ones (the default
of one takes none). A wide window fires triggers early and holds an acquired batch longer, and here nothing
recovers a batch held by a dead process until that scheduler starts again.

## When the leader moves

A new leader on a non-clustered ADO.NET store runs this recovery pass under the trigger-access lock:

1. **`ACQUIRED` and `BLOCKED` triggers of this scheduler go back to `WAITING`**, and `PAUSED_BLOCKED` to
   `PAUSED`. Scoped by scheduler name, so it frees what the previous leader left.
2. **Every misfire is resolved, with no batch limit.** `MaxMisfiresToHandleAtATime` (20 by default) bounds
   only the background handler.
3. **Jobs marked for recovery are re-scheduled** from fired-trigger rows with `REQUESTS_RECOVERY`: each
   becomes a `recover_<instanceId>_<n>` trigger in `SchedulerConstants.DefaultRecoveryGroup`, starting at
   the firing's scheduled time with `IgnoreMisfires`, carrying the original job data plus the four
   `QRTZ_FAILED_JOB_ORIG_*` entries. The job sees `IJobExecutionContext.Recovering`.
4. **Lingering `COMPLETE` triggers are deleted, then every fired-trigger row of this scheduler**, with no
   instance filter.

### The instance id decides whether recovery survives a leader move

Step 3 selects rows whose `INSTANCE_NAME` is *this store's* instance id; step 4 deletes all rows. A new
leader with a **different** id recovers nothing and deletes the rows: `RequestsRecovery` jobs are silently
not re-run (scheduling still resumes).

| Instance id setting | Non-clustered store uses | Recovery across a leader move |
|---|---|---|
| default | `"NON_CLUSTERED"`, the same in every process | works |
| generated: `GenerateInstanceId`, flat `quartz.scheduler.instanceId = AUTO`, or any `UseInstanceIdGenerator(...)` | ignored; still `"NON_CLUSTERED"` | works |
| a literal `InstanceId` per process (pod name, host name) | honoured | **lost** — do not do this here |

Clusters need a distinct id per node ([Naming a node in a container](../operations.md#naming-a-node-in-a-container));
here the id names *the leader*, of which there is one.

Jobs must ask for recovery:

<!-- snippet: sample_external_leader_request_recovery -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.AddJob<ReportingJob>(j => j
        .WithIdentity("nightly-close", "reporting")
        .RequestRecovery()
        .StoreDurably());
});
```
<!-- endSnippet -->

::: tip Recovery is a re-run, not a resume
A recovered firing starts the job from the beginning, so it must be safe to re-run.
:::

## Two leaders at once

Every election has windows without mutual exclusion: a lease expiring while its holder is paused by
garbage collection or a frozen container, clock drift, a healed partition. Election libraries document
this (Kubernetes'
[`client-go/tools/leaderelection`](https://pkg.go.dev/k8s.io/client-go/tools/leaderelection), Apache
Curator's [Tech Note 10](https://curator.apache.org/docs/tech-note-10)). A fencing token (etcd's revision,
Consul's `LockIndex`) does not help: it only fences a resource that validates it
([etcd](https://etcd.io/docs/v3.6/learning/why/)), and `QRTZ_*` has no epoch column. The in-process
`TRIGGER_ACCESS` lock does not exclude a second scheduler.

If a second process starts while the first still leads:

* its recovery resets the incumbent's `ACQUIRED` triggers to `WAITING`, so both can fire them;
* sharing `"NON_CLUSTERED"`, its step 3 schedules recovery for the incumbent's *in-flight* jobs;
* its step 4 deletes the incumbent's fired-trigger rows, hiding them from `QueryFireInstances`;
* `[DisallowConcurrentExecution]` holds within a scheduler, not between two.

Definitions are not corrupted, but jobs double-fire and state is mis-reported. So:

* **Write jobs to tolerate running twice.** This topology requires it.
* **Let the departing leader finish.** Standby keeps jobs running and the acquired batch, so set
  `WaitForJobsToComplete`, `HostOptions.ShutdownTimeout`, and a `terminationGracePeriodSeconds` longer than
  the longest job; see [Shutdown has a budget](../packages/hosted-services-integration.md#shutdown-has-a-budget).
  Consul's [`LockDelay`](https://developer.hashicorp.com/consul/docs/automate/session) (fifteen seconds by
  default) delays re-acquisition for the same reason, without guaranteeing it.
* **Prefer an election in the same database.** A `pg_advisory_lock` or `sp_getapplock` on the connection
  that writes `QRTZ_*` fails with the tables. But PostgreSQL grants advisory locks per server, including on
  a hot standby, so after a failover old and new primaries can both hold one
  ([Hot Standby](https://www.postgresql.org/docs/current/hot-standby.html)).
* **Adding safeguards means rebuilding clustering.** `UseDbLocks` without a cluster manager serializes store
  operations but both processes still fire. `UseClustering()` is the smaller, supported change.

## See also

* [Advanced Enterprise Features](../tutorial/advanced-enterprise-features.md) — clustering, which this page
  is the alternative to
* [Operating a Cluster](../operations.md) — check-in, failover and what the tables are telling you, for
  when you change your mind
* [Hosted Services Integration](../packages/hosted-services-integration.md) — `AutoStart`, the health
  check and the shutdown budget
* [Embedding Quartz in a Library](embedding-quartz-in-a-library.md) — the other half of this, for a package
  that must fit into an application it does not own
* [Dashboard — Store-attached targets](../packages/dashboard.md#store-attached-targets) — the never-started
  scheduler over a shared store, with discovery and a UI over it
* [Quartz.NET with Wolverine](wolverine.md#letting-wolverine-start-the-scheduler) — one concrete election: a bus's
  leader-pinned agent pressing start on the scheduler
* [Configuration Reference](../configuration/reference.md#persistent-job-store) — every setting named here,
  with its default
* [DistributedLock — Other topics](https://github.com/madelson/DistributedLock/blob/master/docs/Other%20topics.md)
  — the renewal-timeout risk, and why a lock on the protected database's connection holds
