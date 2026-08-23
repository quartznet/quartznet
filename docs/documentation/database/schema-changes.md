---
title: Database Schema Changes
---

Every Quartz.NET release that changed the database schema, in order, with the migration to run
and what happens if you skip it.

Quartz.NET never creates or migrates your schema automatically. Read from your current version
down to your target version and apply what each section lists.

::: warning
Always run migration scripts in a test environment against a copy of your production database
first.
:::

## How to use this page

Scripts live in [`database/migrations/`](https://github.com/quartznet/quartznet/tree/main/database/migrations),
one folder per version. Inside each folder, run the file whose suffix matches your database —
`_sqlServer`, `_postgres`, `_mysql_innodb`, `_oracle`, `_sqlite`, `_firebird`. Each file is
directly runnable with no editing.

**Apply every version between where you are and where you are going, in ascending order, and do
not skip one.** Some migrations are optional — you can defer them — but they are *cumulative*,
not alternatives. Skipping 3.17 and later running 3.19 leaves you without the 3.17 column;
nothing goes back and adds it.

Everything except the SQL Server 1.0→2.0 script checks before it acts, so re-running a migration
is a no-op and a half-applied migration is safe to re-run. SQLite `ADD COLUMN` is the one
exception: SQLite has no conditional DDL, so those statements fail on a second run.

A **fresh install** needs none of this. The scripts in
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
| any 3.x | 4.x | [4.0](#version-4-0) — **mandatory**, and it folds in everything from 3.17 onward |

## Upgrading to 4.x is mandatory

This is the one migration you cannot defer.

Quartz.NET 3.x probes for `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE` and
`PREFERRED_NODE_AUTO` when the scheduler starts. If a column is missing it logs a warning and
turns the corresponding feature off — which is why those migrations are optional on 3.x.

**4.x removed those probes** and assumes all four columns exist. It also adds a table 3.x never
had, `QRTZ_PAUSED_JOB_GRPS`, and validates its whole schema at startup. So even a 3.x database
that took every optional migration going will not work against 4.x until [4.0](#version-4-0) has
been applied.

---

## Version 2.0

**Required** when upgrading from 1.x. Nothing later applies until this has run.

The 1.x → 2.x schema overhaul: the listener tables are dropped, `varchar(1)` flag columns become
real `bit` columns, `IS_STATEFUL` is replaced by `IS_NONCONCURRENT` and `IS_UPDATE_DATA`, and
`SCHED_NAME` is introduced across every table with the primary keys and indexes rebuilt around
it.

- Script: [`migrations/2.0/schema_10_to_20_upgrade_sqlServer.sql`](https://github.com/quartznet/quartznet/blob/main/database/migrations/2.0/schema_10_to_20_upgrade_sqlServer.sql)
- SQL Server only — it is a sample, and needs adapting for other databases.
- **Not idempotent.** It drops and recreates objects unconditionally, so it fails on a
  partially-migrated database. Run it once, on a restorable copy.

::: warning
The script defaults `SCHED_NAME` to `TestScheduler`. If you have existing data, change it to
match your `quartz.scheduler.instanceName`.
:::

## Version 2.2

**Required** when upgrading from 2.0 or 2.1.

Adds `SCHED_TIME` to `QRTZ_FIRED_TRIGGERS` so recovery jobs can see both the scheduled and the
actual fire time ([#113](https://github.com/quartznet/quartznet/issues/113)).

- Scripts: [`migrations/2.2/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/2.2) — all databases

The column is `NOT NULL` with no default, so the `ALTER` fails on a table that already holds
rows. `QRTZ_FIRED_TRIGGERS` only ever holds in-flight entries, so stop the scheduler and clear
it first:

```sql
DELETE FROM QRTZ_FIRED_TRIGGERS;
```

## Version 2.6

**Required** when upgrading from 2.5 or earlier.

Adds `TIME_ZONE_ID` to `QRTZ_SIMPROP_TRIGGERS` and `QRTZ_CRON_TRIGGERS`, so a trigger's time zone
survives a restart ([#136](https://github.com/quartznet/quartznet/issues/136)).

- Scripts: [`migrations/2.6/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/2.6) — all databases

::: tip
Older copies of this migration only altered `QRTZ_SIMPROP_TRIGGERS`. `QRTZ_CRON_TRIGGERS` needs
the column too ([#1985](https://github.com/quartznet/quartznet/issues/1985)) — if you upgraded
2.5→2.6 some time ago, check that both tables have it.
:::

## Version 3.0

**Required** when upgrading a SQL Server database from 2.6.

Converts the deprecated `IMAGE` columns to `VARBINARY(MAX)`
([#291](https://github.com/quartznet/quartznet/issues/291)):
`QRTZ_CALENDARS.CALENDAR`, `QRTZ_JOB_DETAILS.JOB_DATA`, `QRTZ_BLOB_TRIGGERS.BLOB_DATA` and
`QRTZ_TRIGGERS.JOB_DATA`.

- Script: [`migrations/3.0/schema_26_to_30_upgrade_sqlServer.sql`](https://github.com/quartznet/quartznet/blob/main/database/migrations/3.0/schema_26_to_30_upgrade_sqlServer.sql)
- SQL Server only — no other dialect ever used `IMAGE`.

## Version 3.17

**Optional on 3.x. Required on 4.x.**

Adds `MISFIRE_ORIG_FIRE_TIME` to `QRTZ_TRIGGERS`
([#2899](https://github.com/quartznet/quartznet/issues/2899)). It stores the original scheduled
fire time before misfire handling overwrites it.

- Scripts: [`migrations/3.17/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.17) — all databases

**If you skip it:** AdoJobStore keeps working, but `ScheduledFireTimeUtc` equals `FireTimeUtc`
for misfired triggers under the "fire now" misfire policies, instead of reporting the time the
trigger was *supposed* to fire. `RAMJobStore` is unaffected.

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD MISFIRE_ORIG_FIRE_TIME bigint NULL;
```

## Version 3.18

**Optional on 3.x. Required on 4.x.**

Adds `EXECUTION_GROUP` to `QRTZ_TRIGGERS` *and* `QRTZ_FIRED_TRIGGERS`
([#3004](https://github.com/quartznet/quartznet/pull/3004)), carrying the execution group tag
that per-node thread limits are enforced against. Both tables need it.

- Scripts: [`migrations/3.18/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.18) — all databases

**If you skip it:** [execution groups](../quartz-3.x/tutorial/execution-groups.md) still work,
but the limit is applied by in-memory filtering after acquisition rather than in the acquire
query — so a node acquires triggers it then has to put back.

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

**Both columns must be added together.** Quartz probes for both and only enables node affinity
when both are present, so adding just one leaves the feature off.

**If you skip it:** node affinity is unavailable. The scheduler logs a warning at startup and
otherwise behaves exactly as it did in 3.18.

```sql
-- SQL Server
ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE nvarchar(200) NULL;
ALTER TABLE QRTZ_TRIGGERS ADD PREFERRED_NODE_AUTO bit NOT NULL DEFAULT 0;
```

## Version 3.20

**Optional, performance only.**

Realigns the index set with the statements AdoJobStore actually issues
([#3203](https://github.com/quartznet/quartznet/pull/3203)).

- Scripts: [`migrations/3.20/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/3.20) — all databases

Every Quartz statement filters `SCHED_NAME` first, yet several shipped indexes did not lead with
it and so could not serve a single-scheduler lookup at all — most visibly on PostgreSQL, where 9
of 11 indexes were affected and `IDX_QRTZ_T_NFT_ST` had its columns in the wrong order. Indexes
that are a leftmost prefix of a wider one, or that no statement can drive a scan from, are
dropped.

SQLite previously shipped no secondary indexes whatsoever, so every acquire poll was a full table
scan; this adds them.

**If you skip it:** everything works, just with more index maintenance on writes and worse plans
on reads. A database created from the current `tables/` script already matches and needs nothing
here.

::: tip
On a busy PostgreSQL database use `CREATE INDEX CONCURRENTLY` / `DROP INDEX CONCURRENTLY`.
Neither can run inside a transaction block, so run those statements one at a time.
:::

## Version 4.0

**Mandatory.** See [above](#upgrading-to-4-x-is-mandatory) for why.

- Scripts: [`migrations/4.0/`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) — all databases

Applies everything from [3.17](#version-3-17), [3.18](#version-3-18), [3.19](#version-3-19) and
[3.20](#version-3-20), plus the `QRTZ_PAUSED_JOB_GRPS` table and the 4.x index shape, in one
pass. Run it whether or not you applied the optional migrations — every statement is guarded, so
it is safe on a partially-migrated database.

Sections, in order:

| # | Change | Status |
|---|---|---|
| 1 | `MISFIRE_ORIG_FIRE_TIME` column | required |
| 2 | `EXECUTION_GROUP` columns | required |
| 3 | `PREFERRED_NODE` / `PREFERRED_NODE_AUTO` columns | required |
| 4 | `QRTZ_PAUSED_JOB_GRPS` table | required |
| 5 | Index set aligned with the 4.x schema | optional |

Run them in order — the drops in section 5 assume the creates above them have already succeeded.

Sections 1–3 have 3.x counterparts and only fold them in. **Section 4 does not**: it is new in
4.x, and it is why this migration is required even for a database that is fully migrated on 3.x.
`QRTZ_PAUSED_JOB_GRPS` holds one row per paused job group, mirroring `QRTZ_PAUSED_TRIGGER_GRPS`
([#3336](https://github.com/quartznet/quartznet/issues/3336)). 3.x pauses a job group without
recording it anywhere, so `IsJobGroupPaused` answered `false` for every group and the pause was
lost on restart; 4.x records the group names, which is what makes `JobGroup.Paused` truthful and
`QueryJobGroups(new JobGroupQuery { Paused = true })` a real listing. A group can be paused while
holding no jobs, so this is a table rather than a column on `QRTZ_JOB_DETAILS` — a group with no
rows has nothing to hang a flag on.

If you already built a schema from an **earlier 4.0 preview**, run the script again: every statement
is guarded, so the only thing it does the second time is create this table. On SQLite run section 4
alone — the `ADD COLUMN` statements above it have no guard and fail on a database that already has
those columns.

The 4.x listing queries page with `ORDER BY JOB_GROUP, JOB_NAME` and
`ORDER BY TRIGGER_GROUP, TRIGGER_NAME`, and the primary keys are name-before-group, so section 5
adds `IDX_QRTZ_J_G_N` and `IDX_QRTZ_T_G_N` to serve those ordered scans. Without them each page
is a scan plus a sort.

Node affinity needs no data migration: 3.x and 4.x store pins identically.

::: tip
Because section 4 has no 3.x counterpart, the `4.0` scripts are maintained on `main` and the
`3.x` branch's copy of them may lag. Run the `main` copy linked above.
:::

## See also

- [`database/README.md`](https://github.com/quartznet/quartznet/blob/main/database/README.md) — the same table, in the repository
- [Database Schema](../quartz-3.x/db/index.md) — what each table holds
- [Migration Guide](../quartz-4.x/migration-guide.md) — the rest of the 3.x → 4.x upgrade
