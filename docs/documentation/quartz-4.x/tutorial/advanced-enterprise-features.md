---

title: 'Clustering'
---

# Clustering

A cluster is several scheduler instances sharing one database.

* **Load balancing:** whichever node acquires a trigger runs it.
* **Failover:** when a node dies, the others recover the jobs it was running, if the job asked for it with
  `RequestRecovery()`.

Clustering needs a persistent store; `RAMJobStore` has nothing to share. Use `LocalTransactionJobStore`,
which `UsePersistentStore` registers.

## Enabling it

<!-- snippet: sample_advanced_clustering -->
```csharp
builder.Services.AddQuartz(q =>
{
    q.ConfigureScheduler(options =>
    {
        // every node in the cluster shares this name: it is what makes them one cluster
        options.InstanceName = "orders";

        // ...and each needs its own id. Generating one is the easy way to be sure.
        options.GenerateInstanceId = true;
    });

    q.UsePersistentStore(store =>
    {
        store.UseSqlServer(connectionString);
        store.UseClustering();
    });
});
```
<!-- endSnippet -->

* **`InstanceName` must be the same on every node.** It is the `SCHED_NAME` column of every row. Nodes
  with different names sharing a database are two schedulers that cannot see each other's work, not a
  cluster.
* **`InstanceId` must be different on every node.** A node recognises its own check-in row and fired
  triggers by it. `GenerateInstanceId = true` derives one at startup from the registered
  `IInstanceIdGenerator` (by default host name and timestamp); the flat value `AUTO` used to mean this. Set
  an explicit id when something outside Quartz names the node, such as [node affinity](node-affinity.md),
  which pins a trigger to an id and needs one that survives a restart.
* **The store configuration must match.** Nodes may differ in thread pool size and other process-local
  settings, but must use the same tables, table prefix and serializer.

`UseClustering()` also turns on database locking; clustering does not work without it.

## Tuning the check-in

<!-- snippet: sample_advanced_checkin_interval -->
```csharp
store.UseClustering(cluster =>
{
    cluster.CheckinInterval = TimeSpan.FromSeconds(10);
    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
});
```
<!-- endSnippet -->

| Option | Default | What it does |
|---|---|---|
| `CheckinInterval` | `00:00:07.5` | How often this node writes "still alive" to `QRTZ_SCHEDULER_STATE`. |
| `CheckinMisfireThreshold` | `00:00:07.5` | How long past a missed check-in another node waits before treating this one as dead and recovering its triggers. |

* A shorter interval notices a dead node sooner, at the cost of more database traffic from every node.
* Raise the threshold if a node is declared dead while merely busy or on a slow database. A false positive
  recovers its running jobs, so they run twice.

::: danger
Never run clustering on separate machines, unless their clocks are synchronized using some form of time-sync service (daemon) that runs very regularly (the clocks must be within a second of each other).
See [https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its](https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its) if you are unfamiliar with how to do this.
:::

::: danger
Never start (`scheduler.Start()`) a non-clustered instance against the same set of database tables that any other instance is running (`Start()`ed) against.
You may get serious data corruption, and will definitely experience erratic behavior.
:::

::: danger
Monitor and ensure that your nodes have enough CPU resources to complete jobs.
When some nodes are in 100% CPU, they may be unable to update the job store and other nodes can consider these jobs lost and recover them by re-running.
:::

### Batching trigger acquisition

Each node acquires the triggers it is about to fire in batches, one trigger by default. That default is
deliberate: at `MaxBatchSize = 1`, with `AcquireTriggersWithinLock` off (also the default), acquisition
takes no cluster-wide lock. Above 1, **every** acquisition cycle takes the `TRIGGER_ACCESS` row lock,
including cycles that acquire nothing; on a lightly loaded cluster that is more lock traffic for no
batching.

Two settings decide a batch's size, and neither does anything alone:

| Option | Flat key | Default |
|---|---|---|
| `Scheduler:MaxBatchSize` | `quartz.scheduler.batchTriggerAcquisitionMaxCount` | `1` |
| `Scheduler:BatchTriggerAcquisitionFireAheadTimeWindow` | `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow` | `00:00:00` |

* `MaxBatchSize` is the upper bound on how many triggers one acquisition takes.
* The window decides how many it actually takes: after the first trigger, only triggers due within the
  window of it join the batch. At zero, the default, a batch holds only triggers due at the same instant,
  so raising `MaxBatchSize` alone changes nothing for spread-out fire times.

Change both together, or neither:

<!-- snippet: sample_advanced_batch_acquisition -->
```csharp
q.ConfigureScheduler(options =>
{
    options.MaxBatchSize = 10;
    options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(1);
});
```
<!-- endSnippet -->

* Batch when many triggers fire at once (a few hundred at the top of the hour): one acquisition and one
  `TRIGGERS_FIRED` round trip replace one of each per trigger. Do not batch triggers a minute apart.
* Triggers fire early, by up to the window. A one-second window on a schedule with second-level precision
  changes behaviour.
* `MaxBatchSize` may not exceed the thread pool's `MaxConcurrency`; startup rejects it. Triggers acquired
  beyond the available threads are held by this node, unfireable by any other, until the pool drains.

## Seeing the cluster

Check-ins land in `QRTZ_SCHEDULER_STATE`. `IScheduler.QueryClusterNodes()` reads them without SQL:

<!-- snippet: sample_advanced_cluster_nodes -->
```csharp
List<ClusterNode> nodes = await scheduler.QueryClusterNodes();

foreach (ClusterNode node in nodes)
{
    string marker = node.IsCurrentNode ? " (this node)" : "";
    Console.WriteLine($"{node.InstanceId}{marker}: {node.State}, last check-in {node.LastCheckInUtc:u}");
}

// The verdicts come from the same predicate the failover sweep applies, so a node reported
// Failed is one whose in-flight work the cluster is about to take over.
List<ClusterNode> failed = nodes.FindAll(node => node.State == ClusterNodeState.Failed);
```
<!-- endSnippet -->

Each `ClusterNode` carries `InstanceId`, `LastCheckInUtc`, the `CheckInInterval` that node was configured
with, `IsCurrentNode`, and a `State`:

| `State` | Means |
|---|---|
| `Alive` | checking in |
| `Overdue` | missed a check-in; nothing is recovered from it |
| `Failed` | the next check-in pass will take over its work and delete its row |

* The answering node is listed first, then the rest by instance id. The answering node is always
  listed, even before its first check-in.
* The state comes from the same predicate the failover sweep uses, so the listing and the recovery cannot
  disagree. A failed node is listed for a while, then disappears.
* The states are what *this* node believes, by its own clock; another reason the clocks must agree.
* A scheduler that is not clustered returns itself, `Alive`, with both times `null`, so a caller need not
  check whether clustering is on.

To see what each node is doing, join the listing to `QueryFireInstances` on `SchedulerInstanceId`:

<!-- snippet: sample_advanced_cluster_node_firings -->
```csharp
List<ClusterNode> nodes = await scheduler.QueryClusterNodes();
PagedResult<FireInstance> firings = await scheduler.QueryFireInstances(new FireInstanceQuery
{
    // both states: what a node is holding is as interesting as what it is running, and a
    // reservation left behind by a dead node is what recovery is about to clear
    State = null
});

foreach (ClusterNode node in nodes)
{
    int running = firings.Items.Count(firing =>
        firing.SchedulerInstanceId == node.InstanceId && firing.State == FireInstanceState.Executing);

    Console.WriteLine($"{node.InstanceId} ({node.State}) is running {running} job(s)");
}
```
<!-- endSnippet -->

The same listing is behind `GET /schedulers/{name}/nodes` in the
[HTTP API](../packages/http-api.md#cluster-nodes) and the Cluster page of the
[dashboard](../packages/dashboard.md).

## When a node leaves

A node that is shut down, rather than killed, hands back what it can.

**It stops firing before it stops running jobs.** The scheduling loop is halted and waited for first:

* every trigger it had reserved but not fired is released to `WAITING` for another node's next pass;
* a firing it had already committed (fired-trigger row written, trigger moved to its next instant) is
  dispatched, not dropped.

This holds whether or not the shutdown waits for jobs. Before 4.1 an unwaited shutdown could close the
thread pool under its own loop and lose that occurrence, recoverable only through `RequestRecovery`.

**It completes the firings it can.**

| Shutdown | Running jobs |
|---|---|
| `Shutdown(waitForJobsToComplete: true)` | waited for; nothing is left over |
| `Shutdown(waitForJobsToComplete: false)` (the `AddQuartzHostedService` default) | not waited for, but executions in flight get a couple of seconds to report completion before the job store closes |

A completion issued after the store closes is refused: the firing stays `EXECUTING` and, for a
`[DisallowConcurrentExecution]` job, its trigger stays `BLOCKED`. A job still working when the window
closes is abandoned and leaves what a crashed node leaves. A peer resolves it once the check-in lapses,
after `CheckinInterval + CheckinMisfireThreshold` plus a grace period for an execution that may still be
alive.

**It does not delete its check-in row.** The `QRTZ_SCHEDULER_STATE` row keeps its last timestamp until a
peer declares the node failed and recovers it. So a node that leaves is declared failed on the same
schedule as one that crashed. Wait for jobs, and allow the window above, so the node leaves nothing to
recover.

## Asking for recovery

Failover recovers a dead node's *executions*, only for jobs that call `RequestRecovery()`:

<!-- snippet: sample_best_practices_request_recovery -->
```csharp
q.AddJob<ChargeInvoicesJob>(j => j
    .WithIdentity("charge-invoices")
    .RequestRecovery());
```
<!-- endSnippet -->

* It is off by default and does nothing without a persistent store.
* When a clustered scheduler decides a peer stopped checking in, it examines every fired-trigger row the
  peer left. If the job requested recovery, the row becomes a new trigger in the `RECOVERING_JOBS` group
  with the original trigger's job data. Otherwise the row is deleted and that occurrence is lost.
* Read it with `IJobDetail.RequestsRecovery`. A recovery firing sets `IJobExecutionContext.Recovering`.

Which jobs should ask, what recovery re-runs and what it does not, and how a recovered firing identifies
itself: [What RequestsRecovery re-runs, and when](../../best-practices.md#what-requestsrecovery-re-runs-and-when),
which also covers making a job safe to re-run. Recovery is for an *interrupted* execution. A job that
threw completed its firing; to re-run it, use a [retry policy](../how-tos/retrying-failed-jobs.md) on its
trigger.

Related:

* [Operating a Cluster](../operations.md): rolling upgrades, instance ids in containers, sizing, backup
* [Running under an External Leader Election](../how-tos/external-leader.md): when the election already
  exists
* [`Quartz.Examples.Worker`](https://github.com/quartznet/quartznet/tree/main/src/Quartz.Examples.Worker):
  a worker service with a persistent store, configured as this lesson describes
