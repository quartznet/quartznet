---
title: Database Schema
---

An ADO.NET job store (usually `LocalTransactionJobStore`) needs a set of tables.

* **Creating** them can be automatic: `ProvisionSchema()` makes the store run the DDL for its database at
  startup and create whatever is missing — see [Creating the schema](../tutorial/job-stores.md#creating-the-schema).
  It is opt-in, because creating tables needs a permission production databases often do not grant.
* **Migrating** an existing schema is always a manual step; nothing in Quartz does it.

| Table | Holds |
| -- | -- |
| qrtz_calendars | non-standard calendars |
| qrtz_job_details | `IJobDetail` data |
| qrtz_locks | the locks Quartz takes |
| qrtz_scheduler_state | `IScheduler` data |
| qrtz_triggers | `ITrigger` data |
| qrtz_cron_triggers | the cron expression of a cron trigger |
| qrtz_fired_triggers | triggers that are currently running |
| qrtz_blob_triggers | triggers stored as binary blob data |
| qrtz_simple_triggers | simple repeat triggers |
| qrtz_simprop_triggers | custom triggers; `ICalendarIntervalTrigger`, `IDailyTimeIntervalTrigger` and `IRecurrenceTrigger` use it |
| qrtz_paused_trigger_grps | `IScheduler.PauseTriggerGroups` data |
| qrtz_paused_job_grps | `IScheduler.PauseJobGroups` data — one row per paused job group, so an empty group is still reported as paused |

The scripts for each database are in
[`database/tables`](https://github.com/quartznet/quartznet/tree/main/database/tables).

The ADO.NET driver is a package reference of the application's: `UsePostgres` needs `Npgsql`,
`UseSqlServer` needs `Microsoft.Data.SqlClient`. A project missing it compiles, then fails as the store
initializes with `Could not load file or assembly`. The
[method-to-package table](../tutorial/job-stores.md#configuring-a-persistent-store) lists all eight.

Upgrading an existing database? See [Database Schema Changes](../../database/schema-changes.md).
Upgrading from 3.x to 4.x is **mandatory**, because 4.x no longer probes for the optional 3.x columns.

## Creating it, and why migrating it is different

A store can create a missing schema but never upgrades an existing one. Quartz's tables carry no version
marker, so a guarded `CREATE TABLE` skips a table that exists without checking which columns it should
have by now, and adding a marker would itself be a migration. A deployment pipeline runs
[`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations), whose
folder names are the version numbers.

For comparison: [Hangfire](https://docs.hangfire.io/en/latest/configuration/using-sql-server.html)
records its schema version and migrates at startup by default, holding its heaviest 1.8 migrations
behind an [opt-in switch](https://docs.hangfire.io/en/latest/upgrade-guides/upgrading-to-hangfire-1.8.html);
[TickerQ](https://tickerq.net/docs/entity-framework/migrations) leaves both steps to the application's
EF Core migrations.

## Columns 4.x requires

These four columns are optional on 3.x, which probes for them at startup and disables the matching
feature when one is missing. **4.x has no probes and requires all four.**

| Column | Table(s) | Added as optional in |
| -- | -- | -- |
| `MISFIRE_ORIG_FIRE_TIME` | `QRTZ_TRIGGERS` | 3.17 |
| `EXECUTION_GROUP` | `QRTZ_TRIGGERS`, `QRTZ_FIRED_TRIGGERS` | 3.18 |
| `PREFERRED_NODE` | `QRTZ_TRIGGERS` | 3.19 |
| `PREFERRED_NODE_AUTO` | `QRTZ_TRIGGERS` | 3.19 |

4.x also needs what 3.x never had: the **columns** `RETRY_POLICY` and `RETRY_ATTEMPT` on
`QRTZ_TRIGGERS`, and the **table** `QRTZ_PAUSED_JOB_GRPS`, which lets an empty job group be paused and
a job group listing report `paused` correctly.

Apply [`database/migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0)
to add whichever are missing, table included. Every statement is guarded, so it is safe on a database
that already has some of them. [Database Schema Changes](../../database/schema-changes.md#version-4-0)
lists the whole 3.x → 4.x set.

## The QRTZ_TRIGGERS table

Holds the data shared by all trigger types. Type-specific data is in `QRTZ_CRON_TRIGGERS`,
`QRTZ_SIMPLE_TRIGGERS`, `QRTZ_SIMPROP_TRIGGERS` or `QRTZ_BLOB_TRIGGERS`, as `TRIGGER_TYPE` says.

| Column | Holds |
| -- | -- |
| `SCHED_NAME` | The `Scheduler:InstanceName` the row belongs to. On every table, so one database can hold several schedulers. |
| `TRIGGER_NAME`, `TRIGGER_GROUP` | The `TriggerKey`. With `SCHED_NAME`, the primary key. |
| `JOB_NAME`, `JOB_GROUP` | The `JobKey` of the job this trigger fires. |
| `DESCRIPTION` | `ITrigger.Description`. |
| `NEXT_FIRE_TIME`, `PREV_FIRE_TIME` | Fire times, as UTC ticks; null when there is none. |
| `PRIORITY` | `ITrigger.Priority`, which breaks ties between triggers due at the same instant. |
| `TRIGGER_STATE` | The stored state — see below. |
| `TRIGGER_TYPE` | `CRON`, `SIMPLE`, `CAL_INT`, `DAILY_I`, `RECUR` or `BLOB`: which sibling table holds the rest. |
| `START_TIME`, `END_TIME` | When the schedule is in force, as UTC ticks. |
| `CALENDAR_NAME` | The `QRTZ_CALENDARS` entry that excludes times from the schedule, if any. |
| `MISFIRE_INSTR` | The misfire instruction's numeric value. |
| `MISFIRE_ORIG_FIRE_TIME` | The fire time a misfire handler moved the trigger away from, so a job can see what it missed. |
| `EXECUTION_GROUP` | The trigger's [execution group](../tutorial/execution-groups.md). |
| `PREFERRED_NODE`, `PREFERRED_NODE_AUTO` | [Node affinity](../tutorial/node-affinity.md): which node should acquire the trigger, and whether it claimed the pin itself. |
| `RETRY_POLICY`, `RETRY_ATTEMPT` | The [retry policy](../how-tos/retrying-failed-jobs.md) in stored string form, and how many retries of the current occurrence have run. See below. |
| `JOB_DATA` | The trigger's own `JobDataMap`, serialized. |

`RETRY_POLICY` is `NULL` on a trigger that does not retry, the default. `RETRY_ATTEMPT` is `0` on a row
4.x wrote and `NULL` on one an upgrade brought across; the store reads both as "no retries so far".

### Trigger states

`TRIGGER_STATE` holds the stored vocabulary,
[`StoredTriggerState`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/Extensibility/StoredTriggerState.cs)
in `Quartz.Extensibility` — not the enum an application sees.

| `TRIGGER_STATE` | Meaning |
| -- | -- |
| `WAITING` | Ready to be picked up when due. The ordinary resting state. |
| `ACQUIRED` | A node has taken the trigger and is about to fire it. |
| `EXECUTING` | Its job is running. |
| `COMPLETE` | It will not fire again. |
| `BLOCKED` | Its job is `[DisallowConcurrentExecution]` and another firing of it is running. |
| `PAUSED` | Paused until resumed. |
| `PAUSED_BLOCKED` | Paused, and blocked by a running firing of the same job. |
| `ERROR` | The trigger could not fire, usually because its job type could not be built. `IScheduler.ResetTriggerFromErrorState` clears it. |
| `DELETED` | A transient marker while a trigger is being removed. |

`IScheduler.GetTriggerState` returns
[`TriggerState`](https://github.com/quartznet/quartznet/blob/main/src/Quartz/TriggerState.cs): `Normal`,
`Paused`, `Complete`, `Error`, `Blocked`, `Executing`, and `None` for a trigger that does not exist.

* `WAITING` and `ACQUIRED` read as `Normal`; `PAUSED_BLOCKED` as `Paused`; `DELETED` as `None`.
* A trigger whose job is running reads as `Executing`, unless the row says deleted, error or paused —
  those are reported as they are.
* `TriggerStateResolver.Resolve` is that mapping, for code of your own.

### Indexes, and the acquisition index in particular

Four indexes ship on `QRTZ_TRIGGERS`, the same four on every dialect:

* three key lookups — `(SCHED_NAME, JOB_NAME, JOB_GROUP)`, `(SCHED_NAME, TRIGGER_GROUP, TRIGGER_NAME)`
  and `(SCHED_NAME, CALENDAR_NAME)`;
* `IDX_QRTZ_T_NFT_ST`, which **both** trigger sweeps read: acquisition, and misfire recovery with the
  counting peek before it. It is the only index whose shape differs by dialect:

```sql
-- SQL Server, PostgreSQL, MySQL, Oracle, SQLite
(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)

-- Firebird
(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)
```

**4.0 drops `IDX_QRTZ_T_NFT_ST_MISFIRE`** ([#3656](https://github.com/quartznet/quartznet/issues/3656)),
a fifth index over `(SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)` on SQL Server, MySQL,
Oracle and Firebird (PostgreSQL and SQLite never had it). It led with `MISFIRE_INSTR`, which both misfire
statements compare with `<> -1` and no B-tree can seek past. Measured on all four engines
([#3608](https://github.com/quartznet/quartznet/issues/3608),
[#3656](https://github.com/quartznet/quartznet/issues/3656)), no optimizer picks it for either misfire
statement; MySQL only appeared to because `MySQLDelegate` forced it by name, and that hint now names the
acquisition index. An upgrade from 3.x drops it in `schema_30_to_40_indexes_<dialect>.sql`, a separate
file because 3.x *does* sweep from it, so it waits for the last 3.x node to shut down.

The last two columns of the acquisition index serve the plan, not a predicate
([#3510](https://github.com/quartznet/quartznet/issues/3510)). `SelectNextTriggerToAcquire` orders by
`NEXT_FIRE_TIME ASC, PRIORITY DESC` and every dialect puts its row limit into that statement, so the
`ORDER BY` picks the highest-priority trigger of a tied set, as `RAMJobStore` does. With matching index
directions the engine takes the first entry instead of reading and sorting every candidate:

| 100,000 triggers, 5,000 due, one acquisition | SQL Server 2022 | MySQL 8.0 | PostgreSQL 15 | Firebird 4 |
|---|---|---|---|---|
| p50, three columns | 21.6 ms | 11.8 ms | 0.89 ms | 14.8 ms |
| p50, shipped shape | **0.59 ms** | **0.69 ms** | 0.74 ms | 14.8 ms |
| reads, three columns | 20,395 | 15,517 | 1,526 | 10,025 |
| reads, shipped shape | **8** | **96** | **24** | 10,025 |
| index size | +6.2 % | +13 % | +0.4 % | unchanged |

`MISFIRE_INSTR` is the fifth column for misfire backlogs. The ordered seek starts at the oldest waiting
trigger, and the statement's lower bound on `NEXT_FIRE_TIME` is inside an `OR` with `MISFIRE_INSTR`, so
it cannot narrow the seek. Without the column, every backlogged row the seek walks costs a table lookup.
With it, those rows are skipped inside the index: against a 5,000-row backlog, 20,401 logical reads
become **84** on SQL Server, and 15,071 buffer reads become **117** on MySQL.

The one case where it is slower than the three-column index: several thousand misfired triggers below
the window and only a handful due costs about 3 ms against 1.5 ms. With several thousand *due* it is
3 ms against 30 ms, and backlogs drain.

Before you diff an index definition:

* **Firebird keeps the three-column index.** Its indexes are ascending or descending as a whole, so
  `CREATE INDEX` rejects `ASC`, and a computed column with the negated priority cannot be indexed either
  (*attempt to index COMPUTED BY column*). Acquisition there still sorts its candidates — the 10,025
  reads above. Ordering by `NEXT_FIRE_TIME` alone measures 3.2× faster and 345× cheaper there, but
  changes which trigger of a tied set fires first; if this costs you, open an issue with numbers rather
  than patching your schema.
* **MySQL before 8.0.1 and MariaDB before 10.8** parse `DESC` in an index and ignore it. Harmless; it
  buys nothing.
* **Oracle** turns a descending key column into a function-based index, so `USER_IND_COLUMNS` shows a
  hidden `SYS_NC000nn$` column with `DESC` at that position. Expected.

**`PREFERRED_NODE` is in no index**, measured on PostgreSQL 15, SQL Server 2022 and MySQL 8.0 against
100,000 triggers ([#3426](https://github.com/quartznet/quartznet/issues/3426));
[`AcquisitionIndexBenchmark`](https://github.com/quartznet/quartznet/blob/main/src/Quartz.Benchmark/AcquisitionIndexBenchmark.cs)
prints the plans on demand.

* **It does nothing for acquisition.** The node-affinity filter is a disjunction (unpinned, pinned here,
  or pinned to a node that stopped checking in), and no B-tree serves an `OR`; the plan is unchanged on
  all three engines. Adding it to the acquisition index buys nothing over `MISFIRE_INSTR` on SQL Server
  and MySQL, and makes PostgreSQL's backlog case worse — 660 shared buffers against 408.
* **It helps only the failover re-pin**, the one `UPDATE` `ClusterRecover` issues per dead node: from a
  full scan (PostgreSQL 8.4 ms, SQL Server 4,319 logical reads, MySQL around 88 ms) to a seek of two or
  three pages. That runs once per node failure, which does not justify a permanent write cost on the
  busiest table.
* **If you add one anyway**, use `(SCHED_NAME, PREFERRED_NODE, PREFERRED_NODE_AUTO)`, for a cluster that
  fails over often enough for a multi-second recovery to hurt. Measure first: below a few thousand
  triggers every plan reads a handful of pages whatever the indexes are.
