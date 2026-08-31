---
title: Database Schema
---

When using an ADO.NET-based job store (the usual being `LocalTransactionJobStore`), Quartz requires a set of tables. Creating them can be automatic: `ProvisionSchema()` has the store run the DDL for its own database as it starts and create whatever is missing — see [Creating the schema](../tutorial/job-stores.md#creating-the-schema). It is opt-in rather than the default, because creating tables needs a permission a production database is often right not to grant. **Migrating** an existing schema is still a manual step, and nothing in Quartz does it for you.

| Table | Brief Description |
| -- | -- |
| qrtz_calendars | Stores non-standard calendars |
| qrtz_job_details | Stores `IJobDetail` data |
| qrtz_locks | locks used by quartz |
| qrtz_scheduler_state | stores `IScheduler` data |
| qrtz_triggers | Stores `ITrigger` data |
| qrtz_cron_triggers | Stores CRON trigger cron expression |
| qrtz_fired_triggers | triggers that are currently running |
| qrtz_blob_triggers | trigger table with a binary blob data storage |
| qrtz_simple_triggers | data for very simple repeat triggers |
| qrtz_simprop_triggers | Reusable table for custom triggers. `ICalendarIntervalTrigger`, `IDailyTimeIntervalTrigger`, and `IRecurrenceTrigger` use this |
| qrtz_paused_trigger_grps | `IScheduler.PauseTriggers` data |
| qrtz_paused_job_grps | `IScheduler.PauseJobs` data — one row per paused job group, so a group paused while it holds nothing is still reported as paused |

The scripts to create these tables for various providers can be found [here](https://github.com/quartznet/quartznet/tree/main/database/tables).

Upgrading an existing database instead? See [Database Schema Changes](../../database/schema-changes.md) — upgrading from 3.x to 4.x is **mandatory**, because 4.x no longer probes for the optional 3.x columns.

## Creating it, and why migrating it is different

A store can create a missing schema; nothing in Quartz upgrades one that already exists. The split is
not arbitrary, and two neighbouring libraries show what decides it.
[Hangfire](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html) does both, on by
default — `SqlServerStorageOptions.PrepareSchemaIfNecessary` and its `Hangfire.PostgreSql` namesake both
default to `true` — and it can, because its schema records its own version: a `Schema` table with a
single `Version` column that an incremental install script steps forward one release at a time. Even
there the two expensive migrations of the 1.8 release are
[held back](https://docs.hangfire.io/en/latest/upgrade-guides/upgrading-to-hangfire-1.8.html) behind
`EnableHeavyMigrations`, which is off by default, "to prevent uncontrolled upgrades that may lead to
extended downtime or deadlocks". [TickerQ](https://tickerq.net/docs/entity-framework/migrations) takes
the opposite route: its schema is Entity Framework Core's, so the application generates the migrations
with `dotnet ef migrations add` and applies them itself, and the library ships neither DDL nor an
apply-at-startup switch. Quartz can only do the first of the two because its tables carry no version
marker of any kind — a guarded `CREATE TABLE` skips a table that is already there without looking
inside it, and there is nothing to read that would say which columns that table ought to have by now.
Adding such a marker would itself be a migration, so provisioning stops at creation and
[`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations), whose
folder names are the version numbers, is what a deployment pipeline runs.

## Columns 4.x requires

These four columns are optional in Quartz.NET 3.x — the scheduler probes for them at startup and disables the corresponding feature when they are missing. **4.x removed those probes and assumes all four exist.**

| Column | Table(s) | Added as optional in |
| -- | -- | -- |
| `MISFIRE_ORIG_FIRE_TIME` | `QRTZ_TRIGGERS` | 3.17 |
| `EXECUTION_GROUP` | `QRTZ_TRIGGERS`, `QRTZ_FIRED_TRIGGERS` | 3.18 |
| `PREFERRED_NODE` | `QRTZ_TRIGGERS` | 3.19 |
| `PREFERRED_NODE_AUTO` | `QRTZ_TRIGGERS` | 3.19 |

4.x also needs two **columns** 3.x never had, `RETRY_POLICY` and `RETRY_ATTEMPT` on `QRTZ_TRIGGERS`,
and a **table** 3.x never had, `QRTZ_PAUSED_JOB_GRPS` — which is what lets a job group be paused
while it holds nothing and what makes a job group listing report `paused` truthfully.

Apply [`database/migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) to add whichever are missing — it covers the table as well as the columns. Every statement is guarded, so it is safe to run on a database that already has some of them. [Database Schema Changes](../../database/schema-changes.md#version-4-0) lists the whole 3.x → 4.x set.

## The QRTZ_TRIGGERS table

This table stores the `ITrigger` data that is shared across all trigger types. What is specific to a type
lives in one of the sibling tables — `QRTZ_CRON_TRIGGERS`, `QRTZ_SIMPLE_TRIGGERS`, `QRTZ_SIMPROP_TRIGGERS` or
`QRTZ_BLOB_TRIGGERS` — and `TRIGGER_TYPE` says which.

| Column | Holds |
| -- | -- |
| `SCHED_NAME` | The `Scheduler:InstanceName` this row belongs to. Every table has it: one database can hold several schedulers. |
| `TRIGGER_NAME`, `TRIGGER_GROUP` | The `TriggerKey`. Together with `SCHED_NAME` they are the primary key. |
| `JOB_NAME`, `JOB_GROUP` | The `JobKey` of the job this trigger fires. |
| `DESCRIPTION` | `ITrigger.Description`. |
| `NEXT_FIRE_TIME`, `PREV_FIRE_TIME` | Fire times, as UTC ticks. Null when there is none. |
| `PRIORITY` | `ITrigger.Priority`, which breaks ties between triggers due at the same instant. |
| `TRIGGER_STATE` | The stored state — see the table below. |
| `TRIGGER_TYPE` | `CRON`, `SIMPLE`, `CAL_INT`, `DAILY_I`, `RECUR` or `BLOB`; which sibling table holds the rest. |
| `START_TIME`, `END_TIME` | The window the schedule is in force, as UTC ticks. |
| `CALENDAR_NAME` | The `QRTZ_CALENDARS` entry excluding times from this trigger's schedule, if any. |
| `MISFIRE_INSTR` | The misfire instruction, as its numeric value. |
| `MISFIRE_ORIG_FIRE_TIME` | The fire time a misfire handler moved the trigger away from, so a job can see what it missed. |
| `EXECUTION_GROUP` | The [execution group](../tutorial/execution-groups.md) this trigger's work belongs to. |
| `PREFERRED_NODE`, `PREFERRED_NODE_AUTO` | [Node affinity](../tutorial/node-affinity.md): which node should acquire this trigger, and whether it claimed the pin itself. |
| `RETRY_POLICY`, `RETRY_ATTEMPT` | The trigger's [retry policy](../how-tos/retrying-failed-jobs.md) in its stored string form, and how many retries of the occurrence being executed have already been made. `RETRY_POLICY` is `NULL` on a trigger that does not retry, which is the default; `RETRY_ATTEMPT` is `0` on a row this release wrote and `NULL` on one an upgrade brought across, and the store reads both as "no retries behind it". |
| `JOB_DATA` | The trigger's own `JobDataMap`, serialized. |

### Trigger states

The string in `TRIGGER_STATE` is not the enum an application sees. The stored vocabulary is
[`StoredTriggerState`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/Impl/AdoJobStore/StoredTriggerState.cs),
in `Quartz.Extensibility`:

| `TRIGGER_STATE` | Meaning |
| -- | -- |
| `WAITING` | Ready to be picked up when its time comes. This is the ordinary resting state. |
| `ACQUIRED` | A node has taken this trigger and is about to fire it. |
| `EXECUTING` | Its job is running. |
| `COMPLETE` | It will not fire again. |
| `BLOCKED` | Its job is `[DisallowConcurrentExecution]` and another firing of it is in progress. |
| `PAUSED` | Paused, and will not fire until resumed. |
| `PAUSED_BLOCKED` | Both at once: paused, and blocked by a running firing of the same job. |
| `ERROR` | The trigger could not be fired — usually because its job type could not be built. `IScheduler.ResetTriggerFromErrorState` clears it. |
| `DELETED` | A transient marker used while a trigger is being removed. |

What `IScheduler.GetTriggerState` returns is
[`TriggerState`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/TriggerState.cs), which is the
API's shorter vocabulary — `Normal`, `Paused`, `Complete`, `Error`, `Blocked`, `Executing` and `None` for a
trigger that does not exist. The stored states map onto it: `WAITING` and `ACQUIRED` both read as `Normal`,
`PAUSED_BLOCKED` reads as `Paused`, and `DELETED` reads as `None`. A trigger whose job is running reads as
`Executing`, unless the row says it is deleted, in error or paused — those are reported as they stand.
`TriggerStateResolver.Resolve` is that mapping, should you need it in code of your own.

### Indexes, and the acquisition index in particular

Four indexes ship on `QRTZ_TRIGGERS`, five on SQL Server, MySQL, Oracle and Firebird. Three are lookups
by key — `(SCHED_NAME, JOB_NAME, JOB_GROUP)`, `(SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME)` and
`(SCHED_NAME, CALENDAR_NAME)`. The fifth, `IDX_QRTZ_T_NFT_ST_MISFIRE`, serves the misfire sweep;
PostgreSQL and SQLite omit it because the fourth already covers that predicate.

That fourth one is `IDX_QRTZ_T_NFT_ST`, the index acquisition runs on, and it is the only index in the
schema whose shape differs by dialect:

```sql
-- SQL Server, PostgreSQL, MySQL, Oracle, SQLite
(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)

-- Firebird
(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)
```

Its last two columns are there for a plan rather than for a predicate
([#3510](https://github.com/quartznet/quartznet/issues/3510)). `SelectNextTriggerToAcquire` orders by
`NEXT_FIRE_TIME ASC, PRIORITY DESC` and every dialect splices its row limit into that statement, so the
`ORDER BY` decides which rows come back at all — the highest-priority trigger of a tied set, which is
what `RAMJobStore` does too. An index whose two directions match lets the engine take the first entry
instead of reading every candidate and sorting it:

| 100,000 triggers, 5,000 due, one acquisition | SQL Server 2022 | MySQL 8.0 | PostgreSQL 15 | Firebird 4 |
|---|---|---|---|---|
| p50, three columns | 21.6 ms | 11.8 ms | 0.89 ms | 14.8 ms |
| p50, shipped shape | **0.59 ms** | **0.69 ms** | 0.74 ms | 14.8 ms |
| reads, three columns | 20,395 | 15,517 | 1,526 | 10,025 |
| reads, shipped shape | **8** | **96** | **24** | 10,025 |
| index size | +6.2 % | +13 % | +0.4 % | unchanged |

`MISFIRE_INSTR` is the fifth column because the ordered seek starts at the oldest waiting trigger, and
the statement's lower bound on `NEXT_FIRE_TIME` sits inside an `OR` with `MISFIRE_INSTR`, so it cannot
narrow that seek. A backlog of misfired triggers below the acquisition window therefore sits in front of
every acquisition, and each backlogged row the seek walks costs a lookup into the table to find out it
is not wanted. That column makes the whole disjunction index-resident, so the rows are skipped inside
the index: against a 5,000-row backlog on SQL Server, 20,401 logical reads become **84**, and on MySQL
15,071 buffer reads become **117**. The statement is unchanged; the predicate never has to become
sargable.

**The one cell where this is slower than the three-column index** is a backlog with almost nothing due:
several thousand misfired triggers below the window and a handful of candidates above it costs about
3 ms against 1.5 ms. With several thousand *due* it is 3 ms against 30 ms, and backlogs drain. The trade
is deliberate.

Three things are worth knowing before you diff an index definition:

- **Firebird keeps the three-column index.** Its indexes are ascending or descending as a whole, with no
  per-column direction, so `CREATE INDEX` rejects the `ASC` token outright — and the usual workaround, a
  computed column holding the negated priority, cannot be indexed there either (*attempt to index
  COMPUTED BY column*). Acquisition on Firebird still materialises its candidates and sorts them —
  the 10,025 index record reads in the table above, where every other engine reads under a hundred —
  and nothing in the shipped schema fixes that. The one lever available is ordering by
  `NEXT_FIRE_TIME` alone, which measures 3.2× faster and 345× cheaper there but changes which trigger
  of a tied set fires first, so if this is costing you, open an issue with numbers rather than
  patching your schema.
- **MySQL before 8.0.1 and MariaDB before 10.8** parse `DESC` in an index definition and ignore it. That
  is harmless and it silently buys nothing.
- **Oracle** makes a descending key column into a function-based index, so `USER_IND_COLUMNS` shows a
  hidden `SYS_NC000nn$` at that position with `DESC` as its direction. Expected, not a defect.

`PREFERRED_NODE`, which node affinity filters and re-pins on, is in no index at all. That was measured
on PostgreSQL 15, SQL Server 2022 and MySQL 8.0 against 100,000 triggers
([#3426](https://github.com/quartznet/quartznet/issues/3426)); the harness is
[`AcquisitionIndexBenchmark`](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Benchmark/AcquisitionIndexBenchmark.cs),
which prints the plans again on demand.

**An index on `PREFERRED_NODE` does nothing for acquisition.** The node-affinity filter is a
disjunction — unpinned, *or* pinned here, *or* pinned to a node that has stopped checking in — and no
B-tree serves an `OR`. The plan is unchanged on all three engines with the index in place. Nor does
adding it to the acquisition index help: it buys nothing over `MISFIRE_INSTR` alone on SQL Server and
MySQL, and it makes PostgreSQL's backlog case measurably worse — 660 shared buffers against 408 — since
a wider entry is more index pages to walk. What it does
change is the failover re-pin, the one `UPDATE` `ClusterRecover` issues per dead node: from a full scan
(PostgreSQL 8.4 ms, SQL Server 4,319 logical reads, MySQL around 88 ms) to a seek of two or three pages.
That statement runs once per node failure, so a permanent write cost on the busiest table in the schema
is not a trade the shipped schema makes.

**If you add one anyway**, `(SCHED_NAME, PREFERRED_NODE, PREFERRED_NODE_AUTO)` is the shape the re-pin
wants, and a cluster that fails over often enough for a multi-second recovery to hurt is the case for
it. Measure your own schema first: none of this is visible below a few thousand triggers, where every
plan reads a handful of pages whatever the indexes are.
