---
title: Database Schema Changes
---

Every Quartz.NET release that changed the database schema, in order, with the migration to run and
what happens if you skip it.

Quartz.NET never migrates your schema. A 4.x store asked to
[provision its own](../quartz-4.x/tutorial/job-stores.md#creating-the-schema) creates a missing
schema, but cannot move an existing one forward: a guarded `CREATE TABLE` skips a table that exists
without looking inside, so a table missing a column stays missing it. Read from your current version
down to your target version and run what each section lists.

::: warning
Always run migration scripts in a test environment against a copy of your production database
first.
:::

## How to use this page

Scripts live in [`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations),
one folder per version. In each folder, run the file whose suffix matches your database: `_sqlServer`,
`_postgres`, `_mysql_innodb`, `_oracle`, `_sqlite`, `_firebird`. Each file runs as is, with no
editing.

- **Apply every version between where you are and where you are going, in ascending order, and skip
  none.** Some migrations are optional (you can defer them), but they are *cumulative*, not
  alternatives. Skipping 3.17 and later running 3.19 leaves you without the 3.17 column; nothing
  goes back and adds it.
- **Re-running is safe.** Every script except the SQL Server 1.0→2.0 one checks before it acts, so
  re-running a migration is a no-op and a half-applied migration can be re-run. The exception is
  SQLite `ADD COLUMN`: SQLite has no conditional DDL, so those statements fail on a second run.
- **A fresh install needs none of this.** The scripts in
  [`database/tables/`](https://github.com/quartznet/quartznet/tree/main/database/tables) already
  create everything below.

## What do I need?

| Coming from | Going to | What to run |
|---|---|---|
| 1.x | any 2.x/3.x | [2.0](#version-2-0), [2.2](#version-2-2), [2.6](#version-2-6), then 3.x as below |
| 2.0 / 2.1 | 3.x | [2.2](#version-2-2), [2.6](#version-2-6), [3.0](#version-3-0), then the optional 3.x ones |
| 2.2–2.5 | 3.x | [2.6](#version-2-6), [3.0](#version-3-0), then the optional 3.x ones |
| 2.6 | 3.x | [3.0](#version-3-0), then the optional 3.x ones |
| 3.0–3.16 | latest 3.x | [3.17](#version-3-17), [3.18](#version-3-18), [3.19](#version-3-19), [3.20](#version-3-20) — all optional |
| any 3.x | 4.0 / 4.1 | [4.0](#version-4-0) — **mandatory**, and it folds in everything from 3.17 onward |
| any 3.x | 4.2 | [4.0](#version-4-0), then [4.2](#version-4-2) — both **mandatory** |
| any 3.x | 4.3+ | [4.0](#version-4-0), [4.2](#version-4-2), then [4.3](#version-4-3) — all **mandatory** |
| 4.0 / 4.1 | 4.2 | [4.2](#version-4-2) — **mandatory** |
| 4.0 / 4.1 | 4.3+ | [4.2](#version-4-2), then [4.3](#version-4-3) — both **mandatory** |
| 4.2 | 4.3+ | [4.3](#version-4-3) — **mandatory** |

## Upgrading to 4.x is mandatory

This is the one migration you cannot defer.

- **3.x** probes for `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE` and
  `PREFERRED_NODE_AUTO` when the scheduler starts. If a column is missing, it logs a warning and
  turns the feature off. That is why those migrations are optional on 3.x.
- **4.x removed those probes** and assumes all four columns exist. It also needs two columns 3.x
  never had, `RETRY_POLICY` and `RETRY_ATTEMPT` on `QRTZ_TRIGGERS`, and a table 3.x never had,
  `QRTZ_PAUSED_JOB_GRPS`.

So even a 3.x database with every optional migration applied does not work with 4.x until
[4.0](#version-4-0) has been applied.

At startup, 4.x checks that every table it needs is queryable, and runs one
`SELECT <column> … WHERE 1 = 0` per column this migration adds to a table 3.x already had. A database
missing the table or any of those columns is refused, and the message names the column and the
script. The check cannot see a column's type or width, so run the whole script.

---

## Version 2.0

**Required** when upgrading from 1.x. Nothing later applies until this has run.

The 1.x → 2.x schema overhaul:

- the listener tables are dropped;
- `varchar(1)` flag columns become real `bit` columns;
- `IS_STATEFUL` is replaced by `IS_NONCONCURRENT` and `IS_UPDATE_DATA`;
- `SCHED_NAME` is added to every table, with the primary keys and indexes rebuilt around it.

- Script: [`migrations/2.0/schema_10_to_20_upgrade_sqlServer.sql`](https://github.com/quartznet/quartznet/blob/main/database/migrations/2.0/schema_10_to_20_upgrade_sqlServer.sql)
- SQL Server only. It is a sample, and needs adapting for other databases.
- **Not idempotent.** It drops and recreates objects unconditionally, so it fails on a
  partially-migrated database. Run it once, on a restorable copy.

::: warning
The script defaults `SCHED_NAME` to `TestScheduler`. If you have existing data, change it to
match your `quartz.scheduler.instanceName`.
:::

## Version 2.2

**Required** when upgrading from 2.0 or 2.1.

Adds `SCHED_TIME` to `QRTZ_FIRED_TRIGGERS`, so recovery jobs can see both the scheduled and the
actual fire time ([#113](https://github.com/quartznet/quartznet/issues/113)).

- Scripts: [`migrations/2.2/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/2.2) — all databases

The column is `NOT NULL` with no default, so the `ALTER` fails on a table that holds rows.
`QRTZ_FIRED_TRIGGERS` only holds in-flight entries, so stop the scheduler and clear it first:

```sql
DELETE FROM QRTZ_FIRED_TRIGGERS;
```

## Version 2.6

**Required** when upgrading from 2.5 or earlier.

Adds `TIME_ZONE_ID` to `QRTZ_SIMPROP_TRIGGERS` and `QRTZ_CRON_TRIGGERS`, so a trigger's time zone
survives a restart ([#136](https://github.com/quartznet/quartznet/issues/136)).

- Scripts: [`migrations/2.6/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/2.6) — all databases

::: tip
Older copies of this migration only altered `QRTZ_SIMPROP_TRIGGERS`. `QRTZ_CRON_TRIGGERS` needs the
column too ([#1985](https://github.com/quartznet/quartznet/issues/1985)). If you upgraded 2.5→2.6
some time ago, check that both tables have it.
:::

## Version 3.0

**Required** when upgrading a SQL Server database from 2.6.

Converts the deprecated `IMAGE` columns to `VARBINARY(MAX)`
([#291](https://github.com/quartznet/quartznet/issues/291)):
`QRTZ_CALENDARS.CALENDAR`, `QRTZ_JOB_DETAILS.JOB_DATA`, `QRTZ_BLOB_TRIGGERS.BLOB_DATA` and
`QRTZ_TRIGGERS.JOB_DATA`.

- Script: [`migrations/3.0/schema_26_to_30_upgrade_sqlServer.sql`](https://github.com/quartznet/quartznet/blob/main/database/migrations/3.0/schema_26_to_30_upgrade_sqlServer.sql)
- SQL Server only; no other dialect used `IMAGE`.

## Version 3.17

**Optional on 3.x. Required on 4.x.**

Adds `MISFIRE_ORIG_FIRE_TIME` to `QRTZ_TRIGGERS`
([#2899](https://github.com/quartznet/quartznet/issues/2899)). It stores the original scheduled fire
time before misfire handling overwrites it.

- Scripts: [`migrations/3.17/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.17) — all databases

**If you skip it:** AdoJobStore keeps working, but for misfired triggers under the "fire now" misfire
policies `ScheduledFireTimeUtc` equals `FireTimeUtc`, instead of the time the trigger was *supposed*
to fire. `RAMJobStore` is unaffected.

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD MISFIRE_ORIG_FIRE_TIME bigint NULL;
```

## Version 3.18

**Optional on 3.x. Required on 4.x.**

Adds `EXECUTION_GROUP` to `QRTZ_TRIGGERS` *and* `QRTZ_FIRED_TRIGGERS`
([#3004](https://github.com/quartznet/quartznet/pull/3004)). It carries the execution group tag that
per-node thread limits are enforced against. Both tables need it.

- Scripts: [`migrations/3.18/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.18) — all databases

**If you skip it:** [execution groups](../quartz-3.x/tutorial/execution-groups.md) still work, but
the limit is applied by in-memory filtering after acquisition rather than in the acquire query, so a
node acquires triggers it then has to put back.

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD EXECUTION_GROUP nvarchar(200) NULL;
ALTER TABLE QRTZ_FIRED_TRIGGERS ADD EXECUTION_GROUP nvarchar(200) NULL;
```

## Version 3.19

**Optional on 3.x. Required on 4.x.**

Adds `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` to `QRTZ_TRIGGERS`
([#3013](https://github.com/quartznet/quartznet/pull/3013),
[#3144](https://github.com/quartznet/quartznet/pull/3144)), which back
[node affinity](../quartz-3.x/tutorial/node-affinity.md).

- Scripts: [`migrations/3.19/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.19) — all databases

**Add both columns together.** Quartz enables node affinity only when both are present.

**If you skip it:** node affinity is unavailable. The scheduler logs a warning at startup and
otherwise behaves exactly as in 3.18.

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE nvarchar(200) NULL;
ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE_AUTO bit NOT NULL DEFAULT 0;
```

## Version 3.20

**Optional, performance only.**

Realigns the index set with the statements AdoJobStore issues
([#3203](https://github.com/quartznet/quartznet/pull/3203)).

- Scripts: [`migrations/3.20/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.20) — all databases

- Every Quartz statement filters `SCHED_NAME` first, but several shipped indexes did not lead with it
  and could not serve a single-scheduler lookup. PostgreSQL was worst: 9 of 11 indexes were affected,
  and `IDX_QRTZ_T_NFT_ST` had its columns in the wrong order.
- Indexes that are a leftmost prefix of a wider one, or that no statement can drive a scan from, are
  dropped.
- SQLite previously had no secondary indexes, so every acquire poll was a full table scan; this adds
  them.

**If you skip it:** everything works, with more index maintenance on writes and worse plans on
reads. A database created from the current `tables/` script already matches.

::: tip
On a busy PostgreSQL database use `CREATE INDEX CONCURRENTLY` / `DROP INDEX CONCURRENTLY`.
Neither can run inside a transaction block, so run those statements one at a time.
:::

## Version 4.0

**Mandatory.** See [above](#upgrading-to-4-x-is-mandatory).

- Scripts: [`migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) — all databases

There are **two files**, run at different moments:

| File | Status | When to run it |
|---|---|---|
| `schema_30_to_40_upgrade_<db>.sql` | **Mandatory** | Now. Everything in it is safe while 3.x nodes are still up. |
| `schema_30_to_40_indexes_<db>.sql` | Optional, performance only | Once the last 3.x node has shut down, or straight afterwards on an offline upgrade. |

### The upgrade file

It applies everything from [3.17](#version-3-17), [3.18](#version-3-18) and [3.19](#version-3-19),
plus the retry columns and the `QRTZ_PAUSED_JOB_GRPS` table. Run it whether or not you applied the
optional migrations; every statement is guarded, so it is safe on a partially-migrated database.

| # | Change | Status |
|---|---|---|
| 1 | `MISFIRE_ORIG_FIRE_TIME` column | required |
| 2 | `EXECUTION_GROUP` columns | required |
| 3 | `PREFERRED_NODE` / `PREFERRED_NODE_AUTO` columns | required |
| 4 | `RETRY_POLICY` / `RETRY_ATTEMPT` columns | required |
| 5 | `QRTZ_PAUSED_JOB_GRPS` table | required |

Sections 1–3 fold in their 3.x counterparts. **Sections 4 and 5 are new in 4.x**, and are why this
migration is required even for a database fully migrated on 3.x.

- **`RETRY_POLICY` and `RETRY_ATTEMPT`** on `QRTZ_TRIGGERS` back a trigger's retry policy
  ([#3520](https://github.com/quartznet/quartznet/issues/3520)): the policy in its stored string form,
  and how many retries of the executing occurrence have been made. Both are nullable with no default,
  so existing rows read as "no retry policy" and nothing needs backfilling.
- **`QRTZ_PAUSED_JOB_GRPS`** holds one row per paused job group, mirroring
  `QRTZ_PAUSED_TRIGGER_GRPS` ([#3336](https://github.com/quartznet/quartznet/issues/3336)). 3.x pauses
  a job group without recording it, so `IsJobGroupPaused` answered `false` for every group and the
  pause was lost on restart. 4.x records the group names, so `JobGroup.Paused` is accurate and
  `QueryJobGroups(new JobGroupQuery { Paused = true })` lists them. A group with no jobs can be paused.

**From an earlier 4.0 preview:** run both files again. Every statement is guarded, so the second run
only applies what the preview lacked: the retry columns, this table on a preview old enough to
predate it, and the index set as a pre-release moved it. On SQLite the upgrade file's sections 1–4 are
not guarded (it has no conditional `ADD COLUMN`), so check `PRAGMA table_info(QRTZ_TRIGGERS)` and run
only the sections whose columns are missing. The index file is guarded on SQLite too.

### The index file

It supersedes [3.20](#version-3-20) and lands the 4.x index shape. It is separate because it cannot
run while 3.x nodes are up: it drops `IDX_QRTZ_T_NFT_ST_MISFIRE`, which 3.x drives its misfire sweep
from and 4.x does not read ([#3656](https://github.com/quartznet/quartznet/issues/3656)).

- Run it top to bottom: the drops assume the creates above them succeeded. Re-run it as often as you
  like; every statement is guarded.
- A 4.x node starts fine against a database with the upgrade file but not this one; it scans where it
  would otherwise seek.

What it changes:

- **Adds `IDX_QRTZ_J_G_N` and `IDX_QRTZ_T_G_N`.** The 4.x listing queries page with
  `ORDER BY JOB_GROUP, JOB_NAME` and `ORDER BY TRIGGER_GROUP, TRIGGER_NAME`, and the primary keys are
  name-before-group. Without these indexes each page is a scan plus a sort.
- **Reshapes `IDX_QRTZ_T_NFT_ST`**, the acquisition index, from
  `(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME)` to
  `(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)`
  ([#3510](https://github.com/quartznet/quartznet/issues/3510)).
  - Acquisition orders by `NEXT_FIRE_TIME ASC, PRIORITY DESC`; an index with matching directions
    lets the engine take the first entry instead of reading and sorting every candidate. On SQL
    Server, a round trip against 5,000 due triggers goes from 21.6 ms and 20,395 logical reads to
    0.6 ms and 8.
  - `MISFIRE_INSTR` lets a backlog of misfired triggers below the acquisition window be skipped inside
    the index rather than one table lookup at a time.
  - The name is unchanged but the columns are not, so the script drops the index before recreating it;
    a guarded `CREATE INDEX` would find the name taken and keep the old shape.
  - **Firebird keeps the three-column index.** Its indexes take one direction for the whole index, so
    it cannot express this one.
- **Drops `IDX_QRTZ_T_NFT_ST_MISFIRE`**, which SQL Server, MySQL, Oracle and Firebird created over
  `(SCHED_NAME, MISFIRE_INSTR, NEXT_FIRE_TIME, TRIGGER_STATE)` and PostgreSQL and SQLite never did
  ([#3656](https://github.com/quartznet/quartznet/issues/3656)).
  - It leads with `MISFIRE_INSTR`, which both misfire statements compare with `<> -1`, and no B-tree
    can seek past that. Those statements filter `SCHED_NAME` and `TRIGGER_STATE` by equality and
    `NEXT_FIRE_TIME` by range: the reshaped acquisition index's leading columns.
  - Measured plan by plan on all four engines that had it, no optimizer picks it for either statement
    ([#3608](https://github.com/quartznet/quartznet/issues/3608),
    [#3656](https://github.com/quartznet/quartznet/issues/3656)). MySQL only appeared to, because
    `MySQLDelegate` named it in a `FORCE INDEX` hint; the hint now names the acquisition index.
  - **The drop comes after the reshape in the same file on purpose**, so a schema that never took the
    reshape takes it first and is never left with neither index.

Node affinity needs no data migration: 3.x and 4.x store pins identically.

### SQLite trigger names

`database/tables/tables_sqlite.sql` now names its four referential-integrity triggers
`QRTZ_DELETE_SIMPLE_TRIGGER`, `QRTZ_DELETE_SIMPROP_TRIGGER`, `QRTZ_DELETE_CRON_TRIGGER` and
`QRTZ_DELETE_BLOB_TRIGGER`. They were `DELETE_SIMPLE_TRIGGER` and so on, with no table prefix, so two
Quartz schemas could not share one SQLite database whatever their table prefixes: the second
`CREATE TRIGGER` collided with the first.

**No action is required on an existing database.** Nothing in Quartz names these triggers; SQLite runs
them itself when a row leaves `QRTZ_TRIGGERS`, so an older schema keeps working under the old names.
Rename them only to put a second Quartz schema in the same file:

```sql
DROP TRIGGER DELETE_SIMPLE_TRIGGER;
DROP TRIGGER DELETE_SIMPROP_TRIGGER;
DROP TRIGGER DELETE_CRON_TRIGGER;
DROP TRIGGER DELETE_BLOB_TRIGGER;
```

then re-create them from the current `tables_sqlite.sql`, replacing `QRTZ_` with your table prefix.
There is no migration script for this; it changes nothing Quartz stores or reads.

::: tip
The `4.0` scripts live on `main` only. The `3.x` branch links to them rather than carrying a copy,
because this upgrade is decided by 4.x's schema and a copy would go stale. The links above are the
right ones.
:::

---

## Version 4.2

Two scripts; only the first is mandatory.

- [`migrations/4.2/add_continuations_<db>.sql`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.2) — **mandatory** for 4.2 and later, all databases
- [`migrations/4.2/add_execution_history_<db>.sql`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.2) — **optional**, needed only by a store configured with `UseExecutionHistory()`

### The continuation columns

**Mandatory** for 4.2 and later. The first schema change since 4.0.

Three nullable columns on `QRTZ_TRIGGERS` carry a *conditional continuation*: a trigger that waits, in
the store, for another trigger's firing to end
([#3805](https://github.com/quartznet/quartznet/issues/3805)).

| Column | What it holds |
|---|---|
| `CONTINUES_TRIGGER_NAME` | The name of the trigger whose firing this one waits for |
| `CONTINUES_TRIGGER_GROUP` | That trigger's group |
| `CONTINUATION_CONDITION` | The outcomes that release the wait, as flags (below) |

`CONTINUATION_CONDITION` flags: `1` on success, `2` on failure, `4` on cancellation, `8` on veto,
`15` however it ends. A row that names a parent but has no condition reads as `15`.

- The waiting trigger is in a new `TRIGGER_STATE`, `AWAITING`, and is never acquired while there.
- The parent's completion resolves it inside the parent's own lock and transaction. An outcome the
  condition names releases the trigger into the ordinary schedule, with its next fire time the later
  of "now" and its own `START_TIME`. Any other outcome deletes it.
- The node that ran the parent resolves it, so a continuation survives the failure of the node that
  scheduled it.
- All three columns are nullable with no default, so existing rows are valid at once and nothing
  needs backfilling.
- No index is added. The lookup is
  `SCHED_NAME = ? AND TRIGGER_STATE = 'AWAITING' AND CONTINUES_TRIGGER_NAME = ? AND CONTINUES_TRIGGER_GROUP = ?`;
  its two leading equality columns are what `IDX_QRTZ_T_NFT_ST` already leads with, and `AWAITING` is
  a small partition even in a schedule that uses continuations heavily. Measure before adding one.

### Rolling 4.1 → 4.2

**Run the migration while 4.1 nodes are still up.** A 4.1 node's trigger `INSERT` names its own
columns, its acquisition and misfire sweeps select `WAITING`, and its cluster recovery touches
`ACQUIRED` and `BLOCKED`, so nothing it does on its own picks up an `AWAITING` row. But it reads an
unknown state string as waiting:

- it *reports* an `AWAITING` row as `Normal`;
- its single-trigger `PauseTrigger` writes `PAUSED` over one, which a resume then releases without its
  parent;
- its reschedule rewrites one as an ordinary trigger.

A 4.1 node cannot **resolve** a continuation at all: a parent completing there leaves the triggers
waiting on that firing where they are. So while any 4.1 node runs, do not pause, resume or reschedule
a continuation from it, and:

1. Run `add_continuations_<db>.sql`.
2. Roll every node to 4.2.
3. *Then* start scheduling continuations.

A 4.2 node refuses to start against a database without this migration, because it names all three
columns in the statement that stores every trigger. The startup check says which column and which
script.

`ProvisionSchema()` does not help here: it creates missing tables and never adds a column to an
existing table. Run the script.

### The execution history tables

**Optional.** `add_execution_history_<db>.sql` creates two tables that only
`UsePersistentStore(store => store.UseExecutionHistory())` reads or writes
([#3771](https://github.com/quartznet/quartznet/issues/3771)):

| Table | One row per |
|---|---|
| `QRTZ_EXECUTION_HISTORY` | Finished execution |
| `QRTZ_MISFIRE_HISTORY` | Missed firing |

- **`QRTZ_EXECUTION_HISTORY`** holds the node that ran it, the job and trigger, when it fired, how
  long it took in ticks, whether it threw, what it said, which attempt at the occurrence it was
  (`RETRY_ATTEMPT`) and whether the trigger answered it with another (`RETRY_SCHEDULED`).
- **`QRTZ_MISFIRE_HISTORY`** holds the trigger, the node that noticed, when it noticed, and the firing
  that was missed. Nothing ran, so there is no duration or outcome; that is why it is a separate table.
- Both are keyed by `(SCHED_NAME, ENTRY_ID)`. The store writes `ENTRY_ID` exactly as it writes
  `QRTZ_FIRED_TRIGGERS.ENTRY_ID`, because no two dialects spell an identity column the same way, and
  nothing reads the number.
- Neither table has a foreign key, since a history row outlives the trigger and job it names. No other
  statement in the schema mentions them.
- Each has two indexes: `(SCHED_NAME, <time>)` for the age query and the retention sweep, and
  `(SCHED_NAME, INSTANCE_NAME)` for the dashboard's node filter.
- A search by job or trigger name is a scan, deliberately: it lowercases the key to match the
  in-memory history, which no index can serve, and the feed it searches is bounded by the retention
  window. `RETRY_ATTEMPT` and `RETRY_SCHEDULED` are not indexed either: the "failed after retries"
  filter (`SUCCEEDED = 0 AND RETRY_SCHEDULED = 0`) runs over the same bounded feed.

When you need it:

- A scheduler that keeps no history never probes for these tables, so a database created by 4.0 or
  4.1 keeps working untouched.
- A store configured for history refuses to start without them, naming this script.
- A fresh install from `database/tables/` creates them either way, and so does `ProvisionSchema()`.
  This is the one 4.2 script provisioning can replace, because what is missing is whole tables, not
  columns of an existing one.

It is safe in a mixed cluster in both directions. A 4.0 or 4.1 node cannot see these tables, and a 4.2
node that does not ask for history neither writes nor reads them. The nodes that do ask share one
history, and every row carries the instance id that produced it.

The store trims both tables itself, to `ExecutionHistoryOptions.Retention` (24 hours by default) and
`MaxEntriesPerScheduler` (2,000); see
[the persistent store's execution history](../quartz-4.x/tutorial/job-stores.md#execution-history-in-the-database).

## Version 4.3

Two scripts; only the first is mandatory
([#3874](https://github.com/quartznet/quartznet/issues/3874)).

| Script | Status | Adds |
|---|---|---|
| [`migrations/4.3/add_fire_progress_<db>.sql`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.3) | **Mandatory** for 4.3 and later | `PROGRESS`, `PROGRESS_MESSAGE` on `QRTZ_FIRED_TRIGGERS` |
| [`migrations/4.3/add_execution_log_<db>.sql`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.3) | Optional: only with `UseExecutionHistory()` | `EXECUTION_LOG` on `QRTZ_EXECUTION_HISTORY` |

### The progress columns

| Column | What it holds |
|---|---|
| `PROGRESS` | The percentage, 0 to 100, a running job last passed to `ReportProgress` |
| `PROGRESS_MESSAGE` | The message it passed with it, truncated to 250 characters |

- Written at most once a second per firing, and only when the value changes, by `ENTRY_ID`. The row is
  the writing node's own, so no lock is taken.
- Oracle declares `PROGRESS_MESSAGE` as `VARCHAR2(1000)`: `VARCHAR2` counts bytes, and 250 characters of
  UTF-8 can take 1,000.
- Read by the fire-instance listing, which is what the dashboard's Currently Executing page shows
  cluster-wide.
- No index: the write is by primary key.
- A 4.3 node refuses to start without them; the startup check names the column and the script.

### The execution log column

`EXECUTION_LOG` holds the log lines an execution wrote, captured by `UseExecutionLogCapture()`. A large
object on every dialect: `nvarchar(max)`, `TEXT`, `LONGTEXT`, `CLOB`, `BLOB SUB_TYPE TEXT`.

- The history listing never selects it; only the read of one entry does.
- A store configured with `UseExecutionHistory()` refuses to start without it. No other store probes for
  it.
- Run it only on a database that has `QRTZ_EXECUTION_HISTORY`: it alters that table, and fails where the
  table is missing. A database without the table runs `4.2/add_execution_history_<db>.sql` first.

### Rolling 4.2 → 4.3

Run both scripts while 4.2 nodes are still up. Every column is nullable with no default, and a 4.2 node
never names them: its firings read as having reported no progress, and its history rows carry no log.

A fresh install from `database/tables/`, and `ProvisionSchema()`, create all three columns.

## See also

- [`database/README.md`](https://github.com/quartznet/quartznet/blob/main/database/README.md): the same table, in the repository
- [Database Schema](../quartz-3.x/db/index.md): what each table holds
- [Migration Guide](../quartz-4.x/migration-guide.md): the rest of the 3.x → 4.x upgrade
