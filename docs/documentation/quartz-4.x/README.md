---
title: Quartz.NET 4.x
prev: false
next: false
---

## Why Quartz.NET

All of this ships in Quartz's own packages; none of it needs a third-party package.

* **Dashboard and HTTP API.** Thirteen pages and sixty-five routes —
  [dashboard](packages/dashboard.md), [HTTP API](packages/http-api.md). Both are
  [fail-closed](packages/dashboard.md#production-hardening): a mapping that authorizes nothing refuses
  to start.
* **Telemetry without an instrumentation package.** Two job spans, thirty-three store spans and eleven
  instruments on the `Quartz` activity source and meter. They cover job execution, trigger acquisition,
  cluster check-in and every store round trip — [OpenTelemetry](packages/opentelemetry-integration.md).
  The scheduler [health check](packages/hosted-services-integration.md#health-checks) is in the core
  package. [Quartz.Aspire](packages/aspire.md) turns an Aspire connection name into a persistent store
  with its telemetry and health check.
* **Trimming and native AOT are tested.** `Quartz` produces no `IL3050`. Every pull request publishes a
  canary application as a native executable and runs it on Windows, Linux and macOS —
  [Publishing Trimmed and Native AOT](how-tos/trimming-and-native-aot.md).
* **A cluster is the same scheduler pointed at one database.** `UseClustering()` turns coordination on.
  A trigger is acquired under a row lock, so a second node adds throughput, not a second firing —
  [Clustering](tutorial/advanced-enterprise-features.md). A firing is at-most-once by default;
  [asking for recovery](tutorial/advanced-enterprise-features.md#asking-for-recovery) re-runs it when
  its node dies.
* **Two concurrency limits.** `[DisallowConcurrentExecution]` stops a job overlapping itself. An
  [execution group](tutorial/execution-groups.md) caps a category of work, per node or across every
  node sharing the store.
* **Retry policies on the trigger.** `RetryPolicy.Fixed`, `Exponential` and `Explicit` are persisted,
  survive a restart and are visible to every node. A policy that runs out is reported to a listener, a
  counter and the history — [Retrying Failed Jobs](how-tos/retrying-failed-jobs.md).
* **Jobs declared on the class, checked by the compiler.** `[QuartzJob]` and `[CronTrigger]` declare a
  job and its schedules, and a source generator writes the registration. An analyzer in the package
  fails the build on a cron expression or `[JobTimeout]` that does not parse —
  [Declaring Jobs with Attributes](tutorial/declaring-jobs-with-attributes.md),
  [Compile-Time Checks](tutorial/compile-time-checks.md).
* **Continuations.** A continuation is a trigger the store holds until its parent's firing ends. It is
  released or discarded inside the parent's own transaction, so a crash cannot lose the link —
  [Job Continuations](how-tos/job-continuations.md).
* **Execution history in the database.** `UseExecutionHistory()` keeps one history for a cluster
  instead of one per node. A dashboard [pointed at the database](packages/dashboard.md#store-attached-targets)
  shows every scheduler in it without running any of them.

[Comparison](comparison.md) sets Quartz against Hangfire, TickerQ, Wolverine and Coravel, with sources,
including where Quartz loses.

## Reading order

* [Quick Start](quick-start.md) — install the package and run a first job
* [Tutorial](tutorial/) — from a first scheduler to clustering
* [How To's](how-tos/) — one recipe per task
* [Configuration Reference](configuration/reference.md) — every option, typed and legacy
* [JSON Configuration](configuration/json.md) — the schedule file format
* [Cron Expression Reference](cron-expressions.md) — the cron syntax
* [Multi-Tenancy](multi-tenancy.md) — three ways to separate tenants, and what each isolates

Going to production:

* [Before you go live](production-checklist.md) — the checklist, each line linking its page
* [Operations](operations.md) — rolling upgrades, failover, sizing, backup, health checks
* [Database Schema](db/) — what the tables hold and which indexes matter
* [Log Events](log-events.md) — every event id, with its level and template

Coming from 3.x:

* [Migration Guide](migration-guide.md) — what changed from 3.x, and what to do about it
* [Upgrading a running deployment](migration-guide.md#upgrading-a-running-deployment) — the ordered runbook
