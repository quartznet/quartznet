---

title: Quartz.NET Features
---

Quartz.NET 4.0 targets .NET 10. The [quick start](/documentation/quartz-4.x/quick-start.html) has a
running scheduler in a few lines; this page is what is in the box.

## Runtime Environments

* Quartz.NET runs embedded in whatever your application already is — a console program, a worker
  service, an ASP.NET Core application — and the container it already has builds the scheduler.
* Several schedulers can run side by side in one process, each with its own store, thread pool and
  listeners, which is how a host serves [several tenants](/documentation/quartz-4.x/multi-tenancy.html).
* Any number of processes sharing one database form a [cluster](/documentation/quartz-4.x/tutorial/advanced-enterprise-features.html)
  that balances work across its nodes and takes over the work of one that dies.
* A scheduler can also be built [without an application container](/documentation/quartz-4.x/tutorial/standalone-scheduler.html),
  and [embedded in a library](/documentation/quartz-4.x/how-tos/embedding-quartz-in-a-library.html)
  that does not want to own the host's.

## Job Scheduling

Jobs are scheduled to run when a given Trigger occurs. Triggers can be created with nearly any
combination of the following directives:

* at a certain time of day (to the millisecond)
* on certain days of the week
* on certain days of the month
* on certain days of the year
* not on certain days listed within a registered Calendar (such as business holidays)
* repeated a specific number of times
* repeated until a specific time/date
* repeated indefinitely
* repeated with a delay interval
* by an [RFC 5545 recurrence rule](/documentation/quartz-4.x/tutorial/recurrencetrigger.html), the
  rule an iCalendar event repeats on

The [cron syntax](/documentation/quartz-4.x/cron-expressions.html) is the usual way of saying the
first four, and it reads Unix five-field expressions as well as Quartz's own.

Jobs are given names by their creator and can also be organized into named groups.
Triggers may also be given names and placed into groups, in order to easily organize them within the scheduler.
Jobs can be added to the scheduler once, but registered with multiple Triggers.

A trigger carries a priority and a misfire instruction, so a scheduler that falls behind resumes in
the order you chose rather than in the order it happens to read rows.

## Job Execution

* A job is any .NET class implementing `IJob`, which is one `Execute` method taking the execution
  context and a `CancellationToken`.
* The container constructs the job, so a job takes its dependencies as constructor parameters like
  anything else, and each firing gets its own scope.
* A firing carries a [`JobDataMap`](/documentation/quartz-4.x/tutorial/job-data-map.html), and its
  entries can be [bound to the job's properties](/documentation/quartz-4.x/tutorial/more-about-jobs.html)
  by name.
* When a Trigger fires, the scheduler notifies zero or more objects implementing
  [`IJobListener` and `ITriggerListener`](/documentation/quartz-4.x/tutorial/trigger-and-job-listeners.html);
  they are notified again after the job has run, and a trigger listener can veto a firing before it
  starts.
* [Middleware](/documentation/quartz-4.x/tutorial/job-execution-middleware.html) wraps execution the
  way ASP.NET Core middleware wraps a request — the shipped ones
  [retry a failed job](/documentation/quartz-4.x/how-tos/retrying-failed-jobs.html) and cancel one
  that overran its `[JobTimeout]`.
* A job that must not overlap itself says so with `[DisallowConcurrentExecution]`, and a job whose
  map should survive a firing with `[PersistJobDataAfterExecution]`.
* Running jobs can be listed and interrupted across the whole cluster, and a trigger reports
  `TriggerState.Executing` while its job runs.

## Job Persistence

* The design of Quartz.NET includes an `IJobStore` interface that can be implemented to provide various mechanisms for the storage of jobs.
* With the use of the included ADO.NET job store, all Jobs and Triggers are stored in a relational
  database — SQL Server, PostgreSQL, MySQL, Oracle, SQLite and Firebird each have a driver delegate,
  and [`database/`](https://github.com/quartznet/quartznet/tree/main/database) has the schema and the
  migrations for every one of them.
* With the use of the included `RAMJobStore`, all Jobs and Triggers are stored in memory and therefore do not persist between program executions - but this has the advantage of not requiring an external database.
* What a store writes is JSON, through System.Text.Json or
  [Newtonsoft.Json](/documentation/quartz-4.x/packages/json-serialization.html).
* The store can [join a transaction the application owns](/documentation/quartz-4.x/tutorial/job-stores.html),
  so saving your data and scheduling the job that acts on it commit together.

## Clustering

* Fail-over: a node that dies has its in-flight recoverable work picked up by another.
* Load balancing: any node in the cluster may fire any trigger.
* [Node affinity](/documentation/quartz-4.x/tutorial/node-affinity.html) and
  [execution groups](/documentation/quartz-4.x/tutorial/execution-groups.html) when *which* node runs
  a job, or how many run at once, is part of the answer.
* An [external leader](/documentation/quartz-4.x/how-tos/external-leader.html) can decide which node
  is active, when something outside Quartz already elects one.

## Listeners & Plug-Ins

* Applications can catch scheduling events to monitor or control job/trigger behavior by implementing one or more listener interfaces.
* The [plug-in mechanism](/documentation/quartz-4.x/packages/quartz-plugins.html) can be used to add
  functionality to Quartz, such as keeping a history of job executions, or loading job and trigger
  definitions from a file.
* Quartz ships with a number of "factory-built" plug-ins and listeners, and with
  [ready-made jobs](/documentation/quartz-4.x/packages/quartz-jobs.html) for scanning a directory,
  sending mail and running a process.

## Operating It

* [OpenTelemetry](/documentation/quartz-4.x/packages/opentelemetry-integration.html): one activity
  source and one meter, so firings show up as spans and as metrics in whatever you already collect.
* A [health check](/documentation/quartz-4.x/packages/hosted-services-integration.html#health-checks)
  that answers for the scheduler and, in a cluster, for the node's own check-in.
* [Every log message carries an event id](/documentation/quartz-4.x/log-events.html), catalogued with
  its level and template.
* A [dashboard](/documentation/quartz-4.x/packages/dashboard.html) and an
  [HTTP API](/documentation/quartz-4.x/packages/http-api.html) for looking at a running scheduler and
  driving it, and a [client](/documentation/quartz-4.x/packages/http-client.html) that speaks to that
  API as if it were a local `IScheduler`. Both surfaces refuse to start unless something authorizes
  them.
* A [production checklist](/documentation/quartz-4.x/production-checklist.html) and an
  [operations guide](/documentation/quartz-4.x/operations.html) covering rolling upgrades, failover,
  sizing and backup.
