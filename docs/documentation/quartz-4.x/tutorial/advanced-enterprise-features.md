---

title: 'Clustering'
---

# Clustering

A cluster is several scheduler instances sharing one database. Between them they load-balance the work —
whichever node acquires a trigger runs it — and they fail over: when a node dies, the jobs it was running are
recovered by the others, provided the job asked for it with `RequestRecovery()`.

Clustering needs a persistent store; `RAMJobStore` has nothing to share. `LocalTransactionJobStore` — what
`UsePersistentStore` registers — is the store to use.

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

Three rules follow from what those settings mean:

* **`InstanceName` must be the same on every node.** It is the `SCHED_NAME` column of every row, so two nodes
  with different names sharing a database are not a cluster — they are two schedulers that cannot see each
  other's work.
* **`InstanceId` must be different on every node.** It is how a node recognises its own check-in row and its
  own fired triggers. `GenerateInstanceId = true` derives one at startup from the registered
  `IInstanceIdGenerator` — by default host name and timestamp — which is what the flat value `AUTO` used to
  mean. Set an explicit id instead when something outside Quartz needs to name the node, such as
  [node affinity](node-affinity.md), which pins a trigger to an id and therefore needs one that survives a
  restart.
* **The rest of the configuration should match.** Nodes may differ in thread pool size, and in anything else
  local to the process, but they must agree about the store: same tables, same table prefix, same serializer.

`UseClustering()` turns on database locking as well, because clustering has never worked without it.

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

Shorter intervals notice a dead node sooner, at the cost of more database traffic from every node all the time.
The threshold is the one to raise if a node is being declared dead while it is merely busy or its database is
slow — a false positive means its running jobs are recovered and run twice.

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

Each node acquires the triggers it is about to fire in batches. By default a batch is one trigger, and
that default is deliberate rather than conservative: at `MaxBatchSize = 1` — with
`AcquireTriggersWithinLock` left off, which is also the default — acquisition takes no cluster-wide lock
at all. Raise it above 1 and **every** acquisition cycle takes the `TRIGGER_ACCESS` row lock, including
the cycles that acquire nothing. On a lightly loaded cluster that is strictly more lock traffic for no
batching.

Two settings decide the size of a batch, and neither does anything on its own:

| Option | Flat key | Default |
|---|---|---|
| `Scheduler:MaxBatchSize` | `quartz.scheduler.batchTriggerAcquisitionMaxCount` | `1` |
| `Scheduler:BatchTriggerAcquisitionFireAheadTimeWindow` | `quartz.scheduler.batchTriggerAcquisitionFireAheadTimeWindow` | `00:00:00` |

`MaxBatchSize` is the upper bound on how many triggers one acquisition may take. The window is what
decides how many it actually takes: after the first trigger, only triggers due within that window of it
join the batch. With the window at zero — the default — a batch holds the triggers due at the same
instant and nothing else, so raising `MaxBatchSize` alone changes nothing for a schedule whose fire
times are spread out.

So: move the pair together, or leave them alone.

<!-- snippet: sample_advanced_batch_acquisition -->
```csharp
q.ConfigureScheduler(options =>
{
    options.MaxBatchSize = 10;
    options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(1);
});
```
<!-- endSnippet -->

That is worth doing when many triggers fire at once — a few hundred at the top of the hour — because
one acquisition and one `TRIGGERS_FIRED` round trip replace one of each per trigger. It is not worth
doing for a schedule of triggers a minute apart.

The price of the window is that triggers fire early, by up to the window. A one-second window on a
schedule with second-level precision is a behaviour change, not a tuning knob.

`MaxBatchSize` may not exceed the thread pool's `MaxConcurrency`, and is rejected at startup if it does:
triggers acquired beyond the number of threads available to run them are held by this node, unfireable
by any other, until the pool drains.

## Seeing the cluster

`QRTZ_SCHEDULER_STATE` is where the check-ins land, and `IScheduler.QueryClusterNodes()` is how to read
it without writing SQL:

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

Each `ClusterNode` carries the node's `InstanceId`, its `LastCheckInUtc`, the `CheckInInterval` that
node was configured with, whether it `IsCurrentNode`, and a `State` of `Alive`, `Overdue` or `Failed`.
The list is the node answering first, then the rest by instance id, and the node answering is always in
it — even before its first check-in has written a row.

The verdict is decided by the same predicate the failover sweep uses, so the listing and the recovery
it predicts cannot disagree: `Failed` means the next check-in pass will take this node's work over and
delete its row, which is why a corpse is reported for a while and then disappears. `Overdue` is a missed
check-in and nothing more; nothing is recovered from an overdue node. The verdicts are what *this* node
believes, read off its own clock — which is another reason the clocks have to agree.

A scheduler that is not clustered answers with the one node it is, `Alive`, and both times `null`:
there is no check-in history because there is nobody to keep one for. That is the honest answer rather
than an empty list, so a caller need not branch on whether clustering is on.

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
