---

title: 'Advanced (Enterprise) Features'
---

## Clustering

Clustering currently only works with the AdoJobstore (`LocalTransactionJobStore`).
Features include load-balancing and job fail-over (if the JobDetail's "request recovery" flag is set to true).

Enable clustering by setting the `quartz.jobStore.clustered` property to "true".
Each instance in the cluster should use the same copy of the Quartz properties.
Exceptions of this would be to use properties that are identical, with the following allowable exceptions:
Different thread pool size, and different value for the `quartz.scheduler.instanceId` property.
Each node in the cluster MUST have a unique instanceId, which is easily done (without needing different properties files) by placing `AUTO` as the value of this property.

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
