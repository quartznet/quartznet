---

title : Quartz.NET 4.0 Released
tags : [releases]
---

Quartz.NET 4.0 is released. It targets `net10.0`, the container builds the scheduler, and a public
surface that had accumulated for a decade has been read, argued over and trimmed.

**It is a major version with extensive breaking changes and a mandatory schema migration.** Nothing
here is a surprise if you have read the [migration guide](/documentation/quartz-4.x/migration-guide.html),
which lists every one of them with before and after, and carries an ordered runbook for upgrading a
running deployment.

[[toc]]

## What 4.0 is

* **`net10.0` only.** There is no `netstandard2.0` build and no Full Framework `.config` support. If
  you need either, [3.x](/documentation/quartz-3.x/quick-start.html) is maintained.
* **The container builds the scheduler.** Dependency injection and hosting are in the core `Quartz`
  package, `quartz.config` is no longer read, and the process-global singletons are gone. Flat
  `quartz.*` keys still work — they are translated into typed options, which have the same names in
  code and in `appsettings.json`.
* **Asynchronous throughout.** Every public `Task` became a `ValueTask`, every async member ends with
  a cancellation token, and `IJob.Execute` takes one — so a job that ignores a shutdown is a compiler
  warning rather than a mystery.
* **Listings became queries.** `QueryJobs` and `QueryTriggers` answer with a page of headers that
  already carry what a listing needs, so a UI over a large schema stops paying for the whole schema.
  The old call shapes remain as extension methods.
* **New ways to say when.** [Recurrence triggers](/documentation/quartz-4.x/tutorial/recurrencetrigger.html)
  take an RFC 5545 rule, the cron parser reads Unix five-field expressions beside Quartz's own, and
  `TriggerState.Executing` tells you a trigger's job is running anywhere in the cluster.
* **Operable out of the box.** An [HTTP API](/documentation/quartz-4.x/packages/http-api.html), a
  [dashboard](/documentation/quartz-4.x/packages/dashboard.html), a
  [health check](/documentation/quartz-4.x/packages/hosted-services-integration.html#health-checks),
  [OpenTelemetry](/documentation/quartz-4.x/packages/opentelemetry-integration.html) spans and
  metrics, and [an event id on every log message](/documentation/quartz-4.x/log-events.html). Both
  web surfaces refuse to start unless something authorizes them: either can schedule any job type by
  name.
* **Faster per firing than 3.20** — 1.5× faster and 2.4× less allocation on PostgreSQL, faster with
  21 % less allocation in memory. The [operations guide](/documentation/quartz-4.x/operations.html)
  has the numbers and what was run to get them.

## Before you upgrade

* **Run the schema migration.** `database/migrations/4.0/schema_30_to_40_upgrade_<database>.sql` is
  the mandatory half and is safe to apply while 3.x nodes are still running;
  `schema_30_to_40_indexes_<database>.sql` is the half that waits until the last one is gone. A 4.0
  node against an unmigrated schema refuses to start, and says which column is missing.
* **Daylight saving fire times changed.** Interval cron expressions fire through *both* halves of a
  repeated fall-back hour instead of skipping one, and `CalendarIntervalTrigger` with
  `PreserveHourOfDayAcrossDaylightSavings` no longer drifts in zones whose offset is not a whole
  hour. Review any schedule that crosses a transition.
* **The 3.20 → 4.0 upgrade is rehearsed** on every dialect in CI, against rows a released 3.20 wrote,
  and by hand on a running two-node cluster on PostgreSQL and SQL Server. The
  [production checklist](/documentation/quartz-4.x/production-checklist.html) is the short form of
  what that found.

## Where to start

The [quick start](/documentation/quartz-4.x/quick-start.html) is one package and a few lines, and the
[tutorial](/documentation/quartz-4.x/tutorial/) is the guided tour from there to a cluster.

Thank you to everyone who filed an issue, sent a pull request or ran a pre-release and told us what
broke. If something you relied on is gone, open an issue — decisions taken during 4.0's development
can be reopened.

<Download />
