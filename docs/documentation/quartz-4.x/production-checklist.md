---
title: Before You Go Live
---

# Before You Go Live

For a scheduler that works in development and is about to carry production work. **Nothing here is new** —
every line is a decision one of the other pages already explains, and the link goes to that page. The
assembly is the point: these are the things that are cheap to get right on Friday and expensive to discover
on Monday.

Skip what does not apply. A single-node in-memory scheduler owes you nothing on the schema lines; a worker
with no web stack owes you nothing on the security ones.

## Configuration

1. **`InstanceName` is set, and is the same on every node of the cluster.** It is the `SCHED_NAME` column
   of every row, so two nodes with different names sharing a database are two schedulers rather than one
   cluster — [Clustering](tutorial/advanced-enterprise-features.md#enabling-it).
2. **`InstanceId` is unique per node**, and *stable* across restarts if you use
   [node affinity](tutorial/node-affinity.md). The container case has its own answer —
   [Naming a node in a container](operations.md#naming-a-node-in-a-container).
3. **`StoreJobDataAsStrings` is on** unless something in your job data genuinely is not a string. It is the
   setting that keeps a stored map readable and free of class-versioning problems —
   [Storing job data as strings](tutorial/job-stores.md#storing-job-data-as-strings).
4. **`MaxConcurrency` is a number you chose**, derived from what the database can serve across every node
   rather than from the default — [Sizing a cluster](operations.md#sizing-a-cluster).
5. **The connection pool is at least `MaxConcurrency` plus three.** The scheduling loop, the misfire handler
   and the cluster check-in each need one that is not a job's —
   [The connection pool is the thread pool plus three](../best-practices.md#the-connection-pool-is-the-thread-pool-plus-three).
6. **`CommandTimeout` is set.** The provider default is usually thirty seconds and applies to a statement
   that has already started — [CommandTimeout](operations.md#commandtimeout).
7. **`WaitForJobsToComplete` and `HostOptions.ShutdownTimeout` agree with your longest job.** A shutdown
   budget shorter than the job is a job killed mid-flight on every deploy —
   [Shutdown has a budget](packages/hosted-services-integration.md#shutdown-has-a-budget).
8. **Every job and trigger has a name you chose.** A generated name is a new row on every start, and with a
   persistent store that is a schedule that grows —
   [Persistent job stores](packages/microsoft-di-integration.md#persistent-job-stores).

## Schema

1. **`SchemaProvisioning` is left at `Validate`**, and whatever applies the rest of your schema applies
   Quartz's — creating tables needs a permission a production database is usually right not to grant —
   [Creating the schema](tutorial/job-stores.md#creating-the-schema).
2. **The fresh-install script's drop switch is `0`** if you run one against a database that already has
   data. It defaults to *drop* — [Schema first, then nodes](operations.md#schema-first-then-nodes).
3. **Upgrading from 3.x: the 4.0 migration is applied, and the cron audit was run first.** The migration is
   mandatory even for a database that took every optional 3.x one —
   [Database Schema Migration](migration-guide.md#database-schema-migration) — and a stored expression 4.x
   rejects fails the *read* of the trigger, not only its firing —
   [Before you upgrade](migration-guide.md#before-you-upgrade). The whole ordered sequence is
   [Upgrading a running deployment](migration-guide.md#upgrading-a-running-deployment).
4. **The listing and acquisition indexes are present** if the schema is large enough for a scan to show —
   [Indexes, and the acquisition index in particular](db/#indexes-and-the-acquisition-index-in-particular).

## Monitor

1. **`quartz.job.execution.duration` is exported.** Its *count* is the number of executions and the part of
   that count tagged `error.type` is the number of failures, so one instrument answers both —
   [Metrics](packages/opentelemetry-integration.md#metrics).
2. **There is an alert on a job you expect to see regularly.** The health check does not assert that
   anything is firing, and a scheduler with an empty schedule is healthy by its definition —
   [Health checks and probes](operations.md#health-checks-and-probes).
3. **`quartz.trigger.misfire` and `quartz.cluster.recovery.trigger` are alerted on.** Both are counters that
   should normally stay flat, which makes them cheap to watch —
   [Metrics](packages/opentelemetry-integration.md#metrics).
4. **Somebody watches the node listing** — `QueryClusterNodes()`, `GET /schedulers/{name}/nodes`, or the
   dashboard's Cluster page — [Reading the cluster](operations.md#reading-the-cluster).
5. **The name attributes are dropped in a view** before they reach the backend, unless your job and trigger
   names are a bounded set. A per-tenant trigger name is unbounded cardinality —
   [Metrics](packages/opentelemetry-integration.md#metrics).
6. **The event ids you alert on are written down.** An id is stable across releases where a message's
   wording is not — [Log Events](log-events.md).

## Secure

1. **`MapQuartzHttpApi()` says what it means about authorization**, `IncludeStackTraceInProblemDetails` is
   off, and `MaxPageSize` is left set. A job scheduled through the API names its type as a string the
   request supplies — [Production hardening](packages/http-api.md#production-hardening).
2. **The dashboard is behind a policy, or read-only, or both.** Its pages start, stand by, shut down, pause,
   resume, delete and trigger — [Production hardening](packages/dashboard.md#production-hardening).
3. **No secrets are in a `JobDataMap`.** It is persisted, it is readable in the database, and it is on every
   listing the API and the dashboard serve —
   [Keep job data small, string-safe and free of secrets](../best-practices.md#keep-job-data-small-string-safe-and-free-of-secrets).

## Rehearse

1. **The schedules are asserted**, in the time zone they will really run in, and across a daylight-saving
   transition if they cross one. This costs microseconds and needs no scheduler —
   [Level 0: schedules, with no scheduler](tutorial/testing.md#level-0-schedules-with-no-scheduler) and
   [Crossing a daylight-saving transition](tutorial/testing.md#crossing-a-daylight-saving-transition).
2. **One failover has been rehearsed.** Kill a node mid-job, watch the recovery, and confirm the work did
   not run twice — which is a property of your job rather than of the scheduler —
   [When a peer takes over](operations.md#when-a-peer-takes-over) and
   [Assume the job will run more than once](../best-practices.md#assume-the-job-will-run-more-than-once).
3. **One restore has been rehearsed on a copy**, because what a restore means for work that was in flight
   when the backup was taken is not obvious — [Backup and restore](operations.md#backup-and-restore).

## See also

- [Operating a Cluster](operations.md) — the day-two half of this list, in full
- [Best Practices](../best-practices.md) — the decisions behind most of the lines above
- [Troubleshooting](../troubleshooting.md) — for when one of them was missed
