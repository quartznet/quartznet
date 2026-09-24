---
title: Before You Go Live
---

# Before You Go Live

Each line is a decision another page explains; the link goes to that page. Skip what does not apply: a
single-node in-memory scheduler can skip the schema lines, and a worker with no web stack the security
ones.

## Configuration

1. **`InstanceName` is set, and the same on every node of the cluster.** It is the `SCHED_NAME` column
   of every row: two nodes with different names sharing a database are two schedulers, not one cluster —
   [Clustering](tutorial/advanced-enterprise-features.md#enabling-it).
2. **`InstanceId` is unique per node**, and stable across restarts if you use
   [node affinity](tutorial/node-affinity.md). For containers, see
   [Naming a node in a container](operations.md#naming-a-node-in-a-container).
3. **`StoreJobDataAsStrings` is on**, unless your job data holds something that is not a string. It keeps
   a stored map readable and free of class-versioning problems —
   [Storing job data as strings](tutorial/job-stores.md#storing-job-data-as-strings).
4. **`MaxConcurrency` is a number you chose**, derived from what the database can serve across every node,
   not left at the default of **10** — [Sizing a cluster](operations.md#sizing-a-cluster). It bounds
   `MaxBatchSize`, and startup warns when the two disagree:

   ```text
   MaxBatchSize is 25, which is more than the thread pool's MaxConcurrency of 10. Triggers acquired
   beyond the number of threads available to run them are held by this node until the pool drains.
   ```

5. **The connection pool is at least `MaxConcurrency` plus three.** The scheduling loop, the misfire handler
   and the cluster check-in each need a connection of their own —
   [The connection pool is the thread pool plus three](../best-practices.md#the-connection-pool-is-the-thread-pool-plus-three).
6. **`CommandTimeout` is set.** The provider default is usually thirty seconds and applies to a statement
   that has already started — [CommandTimeout](operations.md#commandtimeout).
7. **`WaitForJobsToComplete` and `HostOptions.ShutdownTimeout` fit your longest job.** A shutdown budget
   shorter than the job kills it mid-flight on every deploy —
   [Shutdown has a budget](packages/hosted-services-integration.md#shutdown-has-a-budget).
8. **Every job and trigger has a name you chose.** A generated name is a new row on every start, so a
   persistent schedule keeps growing —
   [Persistent job stores](packages/microsoft-di-integration.md#persistent-job-stores).

## Schema

1. **`SchemaProvisioning` is left at `Validate`**, and Quartz's schema is applied by whatever applies the
   rest of yours. Creating tables needs a permission production databases usually do not grant —
   [Creating the schema](tutorial/job-stores.md#creating-the-schema).
2. **The fresh-install script's drop switch is `0`** when you run one against a database that already
   has data. It defaults to *drop* — [Schema first, then nodes](operations.md#schema-first-then-nodes).
3. **Upgrading from 3.x: the cron audit was run, then the 4.0 migration was applied.** The migration is
   mandatory even for a database that took every optional 3.x one —
   [Database Schema Migration](migration-guide.md#database-schema-migration). A stored expression 4.x
   rejects fails the *read* of the trigger, not only its firing —
   [Before you upgrade](migration-guide.md#before-you-upgrade). The full sequence is
   [Upgrading a running deployment](migration-guide.md#upgrading-a-running-deployment).
4. **The listing and acquisition indexes are present** once the schema is large enough for a scan to
   show — [Indexes, and the acquisition index in particular](db/#indexes-and-the-acquisition-index-in-particular).

## Monitor

1. **`quartz.job.execution.duration` is exported.** Its *count* is the number of executions, and the part
   tagged `error.type` is the number of failures — [Metrics](packages/opentelemetry-integration.md#metrics).
2. **There is an alert on a job you expect to see regularly.** The health check does not assert that
   anything is firing; a scheduler with an empty schedule is healthy —
   [Health checks and probes](operations.md#health-checks-and-probes).
3. **`quartz.trigger.misfire` and `quartz.cluster.recovery.trigger` are alerted on.** Both counters
   normally stay flat — [Metrics](packages/opentelemetry-integration.md#metrics).
4. **Somebody watches the node listing** — `QueryClusterNodes()`, `GET /schedulers/{name}/nodes`, or the
   dashboard's Cluster page — [Reading the cluster](operations.md#reading-the-cluster).
5. **The name attributes are dropped in a view** before they reach the backend, unless job and trigger
   names are a bounded set. A per-tenant trigger name is unbounded cardinality —
   [Metrics](packages/opentelemetry-integration.md#metrics).
6. **The event ids you alert on are written down.** Ids are stable across releases; message wording is
   not — [Log Events](log-events.md).
7. **Warning 3716 and `quartz.jobstore.lock.wait.duration` are alerted on.** A node blocked on the job
   store lock produces no other signal — [Log Events](log-events.md) and
   [Metrics](packages/opentelemetry-integration.md#metrics).

## Secure

1. **`MapQuartzHttpApi()` states its authorization**, `IncludeStackTraceInProblemDetails` is off, and
   `MaxPageSize` is left set. A job scheduled through the API names its type in a string from the
   request — [Production hardening](packages/http-api.md#production-hardening).
2. **The dashboard is behind a policy, read-only, or both.** Its pages start, stand by, shut down, pause,
   resume, delete and trigger — [Production hardening](packages/dashboard.md#production-hardening).
3. **No secrets are in a `JobDataMap`.** It is persisted, readable in the database, and on every listing
   the API and the dashboard serve —
   [Keep job data small, string-safe and free of secrets](../best-practices.md#keep-job-data-small-string-safe-and-free-of-secrets).

## Rehearse

1. **The schedules are asserted** in the time zone they will run in, and across a daylight-saving
   transition if they cross one. This needs no scheduler and takes microseconds —
   [Level 0: schedules, with no scheduler](tutorial/testing.md#level-0-schedules-with-no-scheduler) and
   [Crossing a daylight-saving transition](tutorial/testing.md#crossing-a-daylight-saving-transition).
2. **One failover has been rehearsed.** Kill a node mid-job, watch the recovery, and confirm the work did
   not run twice (a property of your job, not of the scheduler) —
   [When a peer takes over](operations.md#when-a-peer-takes-over) and
   [Assume the job will run more than once](../best-practices.md#assume-the-job-will-run-more-than-once).
3. **One restore has been rehearsed on a copy**, to see what a restore does to work that was in flight
   at backup time — [Backup and restore](operations.md#backup-and-restore).
4. **A node that vanishes while holding the lock has been survived.** Cutting a node's network is a
   different test from killing it: only the first leaves the lock held —
   [A Lock Held by a Connection That Is Gone](../troubleshooting.md#a-lock-held-by-a-connection-that-is-gone).

## See also

- [Operating a Cluster](operations.md) — the day-two half of this list
- [Best Practices](../best-practices.md) — the decisions behind most of the lines above
- [Troubleshooting](../troubleshooting.md) — for when one of them was missed
