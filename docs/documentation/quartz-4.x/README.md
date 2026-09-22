---
title: Quartz.NET 4.x
prev: false
next: false
---

## Why Quartz.NET

Most of what is written about Quartz.NET is about the trigger model, so the rest of what 4.x ships
tends to be a surprise. It is all in the box, and none of it needs a third-party package:

* **The dashboard and the HTTP API are Quartz's own**, and both are
  [fail-closed](packages/dashboard.md#production-hardening): a mapping that authorizes nothing refuses
  to start, rather than serving a mutating surface to anyone who finds the path. Thirteen pages and
  sixty-five routes — [dashboard](packages/dashboard.md), [HTTP API](packages/http-api.md).
* **Telemetry without an instrumentation package.** Two job spans, thirty-three store spans and eleven
  instruments on the `Quartz` activity source and meter, covering job execution, trigger acquisition,
  cluster check-in and every store round trip —
  [OpenTelemetry](packages/opentelemetry-integration.md). The scheduler
  [health check](packages/hosted-services-integration.md#health-checks) is in the core package, and
  [Quartz.Aspire](packages/aspire.md) turns an Aspire connection name into a persistent store with its
  telemetry and health check attached.
* **Trimming and native AOT are tested rather than asserted.** `Quartz` produces no `IL3050` at all, and
  a canary application is published as a native executable and *run* on Windows, Linux and macOS in
  every pull request — [Publishing Trimmed and Native AOT](how-tos/trimming-and-native-aot.md).
* **A cluster is the same scheduler, pointed at one database.** Every node runs it, `UseClustering()`
  turns the coordination on, and a trigger is acquired under a row lock — so a second node adds
  throughput and not a second firing.
  [Clustering](tutorial/advanced-enterprise-features.md) is the page. A firing is at-most-once by default; asking
  for [recovery](tutorial/advanced-enterprise-features.md#asking-for-recovery) makes a node's death
  re-run what it was doing.
* **Concurrency has two bounds, and one of them is the cluster's.** `[DisallowConcurrentExecution]`
  keeps one job from overlapping itself; an [execution group](tutorial/execution-groups.md) caps a whole
  category of work, counted per node or across every node sharing the store.
* **A retry policy lives on the trigger.** `RetryPolicy.Fixed`, `Exponential` and `Explicit` are
  persisted, survive a restart and are visible to every node, and a policy that runs out says so to a
  listener, a counter and the history —
  [Retrying Failed Jobs](how-tos/retrying-failed-jobs.md).
* **A job can be declared on its class, and the compiler reads it.** `[QuartzJob]` and `[CronTrigger]`
  put a job and its schedules on the class and a source generator writes the registration; an analyzer
  shipped inside the package fails the build on a cron expression or a `[JobTimeout]` that would not
  parse — [Declaring Jobs with Attributes](tutorial/declaring-jobs-with-attributes.md),
  [Compile-Time Checks](tutorial/compile-time-checks.md).
* **One firing can wait for another.** A continuation is a trigger the store holds until its parent's
  firing ends, released or discarded by how it ended inside the parent's own transaction, so a crash
  cannot lose the link — [Job Continuations](how-tos/job-continuations.md).
* **The history can live in the database, and so can the dashboard's view of it.**
  `UseExecutionHistory()` keeps one history for a cluster rather than one per node, and a dashboard
  [pointed at the database](packages/dashboard.md#store-attached-targets) shows every scheduler in it
  without running any of them.

How all of that lines up against Hangfire, TickerQ, Wolverine and Coravel is
[Comparison](comparison.md), which is sourced and says where Quartz loses.

## Reading order

* [Quick Start](quick-start.md) — install the package and run a first job
* [Tutorial](tutorial/) — the guided tour, from a first scheduler to clustering
* [How To's](how-tos/) — short recipes for one task each
* [Configuration Reference](configuration/reference.md) — every option, typed and legacy
* [JSON Configuration](configuration/json.md) — the schedule file format
* [Cron Expression Reference](cron-expressions.md) — the cron syntax
* [Multi-Tenancy](multi-tenancy.md) — the three ways to separate tenants, and what each one isolates

Going to production:

* [Before you go live](production-checklist.md) — the checklist, every line linking the page behind it
* [Operations](operations.md) — rolling upgrades, failover, sizing, backup, health checks
* [Database Schema](db/) — what the tables hold and which indexes matter
* [Log Events](log-events.md) — every event id the scheduler writes, with its level and template

Coming from 3.x:

* [Migration Guide](migration-guide.md) — what changed from 3.x, and what to do about it
* [Upgrading a running deployment](migration-guide.md#upgrading-a-running-deployment) — the ordered runbook
