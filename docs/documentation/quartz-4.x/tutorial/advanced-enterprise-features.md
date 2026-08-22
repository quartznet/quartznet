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
        store.UseSystemTextJsonSerializer();
        store.UseClustering();
    });
});
```

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

```csharp
store.UseClustering(cluster =>
{
    cluster.CheckinInterval = TimeSpan.FromSeconds(10);
    cluster.CheckinMisfireThreshold = TimeSpan.FromSeconds(20);
});
```

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

```csharp
q.ConfigureScheduler(options =>
{
    options.MaxBatchSize = 10;
    options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(1);
});
```

That is worth doing when many triggers fire at once — a few hundred at the top of the hour — because
one acquisition and one `TRIGGERS_FIRED` round trip replace one of each per trigger. It is not worth
doing for a schedule of triggers a minute apart.

The price of the window is that triggers fire early, by up to the window. A one-second window on a
schedule with second-level precision is a behaviour change, not a tuning knob.

`MaxBatchSize` may not exceed the thread pool's `MaxConcurrency`, and is rejected at startup if it does:
triggers acquired beyond the number of threads available to run them are held by this node, unfireable
by any other, until the pool drains.
