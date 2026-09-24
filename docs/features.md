---

title: Quartz.NET Features
---

Quartz.NET 4.x targets .NET 10. The [quick start](/documentation/quartz-4.x/quick-start.html) gets a
scheduler running in a few lines.

## Runtime Environments

* Runs embedded in your application (a console program, a worker service, an ASP.NET Core
  application), and the application's container builds the scheduler.
* Several schedulers can run side by side in one process, each with its own store, thread pool and
  listeners. This is how a host serves [several tenants](/documentation/quartz-4.x/multi-tenancy.html).
* Any number of processes sharing one database form a
  [cluster](/documentation/quartz-4.x/tutorial/advanced-enterprise-features.html) that balances work
  across its nodes and takes over the work of a node that dies.
* A scheduler can also be built [without an application container](/documentation/quartz-4.x/tutorial/standalone-scheduler.html),
  or [embedded in a library](/documentation/quartz-4.x/how-tos/embedding-quartz-in-a-library.html)
  that should not own the host's.

## Job Scheduling

Jobs run when a trigger fires. Triggers can combine nearly any of these:

* at a certain time of day (to the millisecond)
* on certain days of the week
* on certain days of the month
* on certain days of the year
* not on certain days listed in a registered calendar (such as business holidays)
* repeated a specific number of times
* repeated until a specific time/date
* repeated indefinitely
* repeated with a delay interval
* by an [RFC 5545 recurrence rule](/documentation/quartz-4.x/tutorial/recurrencetrigger.html), the
  rule an iCalendar event repeats on

[Cron syntax](/documentation/quartz-4.x/cron-expressions.html) is the usual way to state the first
four. It reads Unix five-field expressions as well as Quartz's own.

* Jobs and triggers have names and can be organized into named groups.
* A job is added to the scheduler once and can be registered with several triggers.
* A trigger carries a priority and a misfire instruction, so a scheduler that falls behind resumes in
  the order you chose, not the order it reads rows.

## Job Execution

* A job is any .NET class implementing `IJob`: one `Execute` method taking the execution context and
  a `CancellationToken`.
* The container constructs the job, so a job takes its dependencies as constructor parameters, and
  each firing gets its own scope.
* A firing carries a [`JobDataMap`](/documentation/quartz-4.x/tutorial/job-data-map.html), whose
  entries can be [bound to the job's properties](/documentation/quartz-4.x/tutorial/more-about-jobs.html)
  by name.
* When a trigger fires, the scheduler notifies any
  [`IJobListener` and `ITriggerListener`](/documentation/quartz-4.x/tutorial/trigger-and-job-listeners.html)
  objects, again after the job has run. A trigger listener can veto a firing before it starts.
* [Middleware](/documentation/quartz-4.x/tutorial/job-execution-middleware.html) wraps execution as
  ASP.NET Core middleware wraps a request. The shipped middleware
  [retries a failed job](/documentation/quartz-4.x/how-tos/retrying-failed-jobs.html) and cancels one
  that overran its `[JobTimeout]`.
* `[DisallowConcurrentExecution]` stops a job overlapping itself; `[PersistJobDataAfterExecution]`
  keeps its map across firings.
* Running jobs can be listed and interrupted across the whole cluster, and a trigger reports
  `TriggerState.Executing` while its job runs.

## Job Persistence

* Job storage is pluggable through the `IJobStore` interface.
* The included ADO.NET job store keeps jobs and triggers in a relational database. SQL Server,
  PostgreSQL, MySQL, Oracle, SQLite and Firebird each have a driver delegate, and
  [`database/`](https://github.com/quartznet/quartznet/tree/main/database) has the schema and
  migrations for each.
* The included `RAMJobStore` keeps jobs and triggers in memory. Nothing persists between runs, but no
  external database is needed.
* A store writes JSON, through System.Text.Json or
  [Newtonsoft.Json](/documentation/quartz-4.x/packages/json-serialization.html).
* The store can [join a transaction the application owns](/documentation/quartz-4.x/tutorial/job-stores.html),
  so saving your data and scheduling the job that acts on it commit together.

## Clustering

* Fail-over: another node picks up a dead node's in-flight recoverable work.
* Load balancing: any node in the cluster may fire any trigger.
* [Node affinity](/documentation/quartz-4.x/tutorial/node-affinity.html) and
  [execution groups](/documentation/quartz-4.x/tutorial/execution-groups.html) control *which* node
  runs a job and how many run at once.
* An [external leader](/documentation/quartz-4.x/how-tos/external-leader.html) can decide which node
  is active, when something outside Quartz already elects one.

## Listeners & Plug-Ins

* Listener interfaces let applications catch scheduling events to monitor or control job and trigger
  behavior.
* [Plug-ins](/documentation/quartz-4.x/packages/quartz-plugins.html) add functionality, such as
  keeping a history of job executions or loading job and trigger definitions from a file.
* Quartz ships plug-ins and listeners, and
  [ready-made jobs](/documentation/quartz-4.x/packages/quartz-jobs.html) for scanning a directory,
  sending mail and running a process.

## Operating It

* [OpenTelemetry](/documentation/quartz-4.x/packages/opentelemetry-integration.html): one activity
  source and one meter, so firings show up as spans and metrics in whatever you already collect.
* A [health check](/documentation/quartz-4.x/packages/hosted-services-integration.html#health-checks)
  for the scheduler and, in a cluster, for the node's own check-in.
* [Every log message has an event id](/documentation/quartz-4.x/log-events.html), catalogued with
  its level and template.
* A [dashboard](/documentation/quartz-4.x/packages/dashboard.html) and an
  [HTTP API](/documentation/quartz-4.x/packages/http-api.html) to inspect and drive a running
  scheduler, and a [client](/documentation/quartz-4.x/packages/http-client.html) that uses that API
  as if it were a local `IScheduler`. Both surfaces refuse to start unless something authorizes them.
* A [production checklist](/documentation/quartz-4.x/production-checklist.html) and an
  [operations guide](/documentation/quartz-4.x/operations.html) covering rolling upgrades, failover,
  sizing and backup.
