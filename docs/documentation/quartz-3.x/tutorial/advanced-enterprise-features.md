---

title: 'Advanced (Enterprise) Features'
---

## Clustering

Clustering works only with the AdoJobStore (`JobStoreTX` or `JobStoreCMT`). It provides load balancing and job fail-over (when the JobDetail's "request recovery" flag is true).

To enable clustering:

1. Set `quartz.jobStore.clustered` to "true".
2. Use the same Quartz properties on every instance. The allowed differences are the thread pool size and `quartz.scheduler.instanceId`.
3. Give each node a unique instanceId. `AUTO` does this without separate properties files.

See [Clustering](../configuration/reference.md#clustering) in the configuration reference for how load balancing and fail-over work.

::: danger
Never cluster separate machines unless their clocks are synchronized by a time-sync service that runs very regularly (clocks within a second of each other).
See [https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its](https://www.nist.gov/pml/time-and-frequency-division/services/internet-time-service-its) if you are unfamiliar with how to do this.
:::

::: danger
Never start (`scheduler.Start()`) a non-clustered instance against the same set of database tables that any other started (`Start()`ed) instance uses.
You may get serious data corruption, and will see erratic behavior.
:::

::: danger
Make sure your nodes have enough CPU to complete jobs.
A node at 100% CPU may be unable to update the job store, and other nodes can then consider its jobs lost and recover them by re-running.
:::
