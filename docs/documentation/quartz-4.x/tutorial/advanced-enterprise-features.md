---

title: 'Advanced (Enterprise) Features'
---

## Clustering

Clustering currently only works with the AdoJobstore (`JobStoreTX`).
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
