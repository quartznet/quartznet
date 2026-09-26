# Quartz.NET database scripts

Scripts to run against a database with a database client. Since 4.0 a scheduler can also create a missing
schema itself when asked — see [What the scheduler runs](#what-the-scheduler-runs) — but **migrating** an
existing schema is always a manual step; nothing in Quartz does it.

```
database/
  tables/       fresh-install scripts -- one per database, creates the current schema
  migrations/   schema changes, grouped by the Quartz.NET version that introduced them
```

Branches:

- A migration **both branches can run** is byte-identical on `3.x` and `main`, so its path resolves on
  either. The **Branch** column below says which versions are on both.
- The `4.0` folder **lives on `main` only**. It is the 3.x → 4.0 upgrade path and its content is decided
  by 4.x's schema, so `3.x` links here instead of carrying a copy that would go stale.
- `tables/` is the *current* schema and differs by design: the 3.x schema on `3.x`, the 4.x one on
  `main`.

## Fresh install

Run the one script for your database from [`tables/`](tables). It creates the current schema in full,
including every column the migrations add; a new database needs nothing from `migrations/`.

- **Every script drops an existing Quartz schema before recreating it**, so running one against a live
  database destroys its data. To prevent that, set the `DropDb` variable declared above the drops to `0`
  (`@DropDb` on SQL Server and MySQL). SQLite has no variables: delete the block between the
  `BEGIN DROP TABLES` and `END DROP TABLES` markers. The rest only creates tables, and its
  `CREATE TABLE` statements are not guarded, so run it on a database with no Quartz tables.
- **The SQL Server scripts begin `USE [enter_db_name_here];`** — put your database name there, or SQL
  Server answers `Msg 911, Database 'enter_db_name_here' does not exist`. No other dialect names a
  database. The memory-optimized variant also has `[enter_path_here]`, for the filegroup's directory.

| Database | Script |
|---|---|
| SQL Server 2016+ | [`tables/tables_sqlServer.sql`](tables/tables_sqlServer.sql) |
| SQL Server, memory-optimized | [`tables/tables_sqlServerMOT.sql`](tables/tables_sqlServerMOT.sql) |
| SQL Server 2012/2014 | [`tables/tables_sqlServer_Below2016.sql`](tables/tables_sqlServer_Below2016.sql) |
| PostgreSQL | [`tables/tables_postgres.sql`](tables/tables_postgres.sql) |
| MySQL / MariaDB | [`tables/tables_mysql_innodb.sql`](tables/tables_mysql_innodb.sql) |
| Oracle | [`tables/tables_oracle.sql`](tables/tables_oracle.sql) |
| SQLite | [`tables/tables_sqlite.sql`](tables/tables_sqlite.sql) |
| Firebird | [`tables/tables_firebird.sql`](tables/tables_firebird.sql) |

## What the scheduler runs

A store configured with `SchemaProvisioning.CreateIfMissing` (`store.ProvisionSchema()` in code) creates a
missing schema at startup. It does **not** run the scripts above but a second set, embedded in
`Quartz.dll`, at
[`src/Quartz/Impl/AdoJobStore/Schema/create_<dialect>.sql`](../src/Quartz/Impl/AdoJobStore/Schema):

| Database | Script |
|---|---|
| SQL Server | [`create_sqlServer.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_sqlServer.sql) |
| PostgreSQL | [`create_postgres.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_postgres.sql) |
| MySQL / MariaDB | [`create_mysql_innodb.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_mysql_innodb.sql) |
| Oracle | [`create_oracle.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_oracle.sql) |
| SQLite | [`create_sqlite.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_sqlite.sql) |
| Firebird | [`create_firebird.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_firebird.sql) |

- **Six, not eight.** The memory-optimized and pre-2016 SQL Server variants are deliberate departures a
  person chooses for a deployment, so they have no counterpart here. Neither has a driver delegate of its
  own, so a store pointed at one and asked to provision creates the **standard** schema. Run those two by
  hand and leave the setting at `Validate`.
- **Do not run these by hand.** They are written for an ADO.NET provider: the table prefix is a `{0}`
  placeholder, statements are separated by a `--;;` line (not `GO`, `/` or `SET TERM`), and there are no
  `DECLARE`s or variables, since a provider is sent one statement at a time. Use [`tables/`](tables).
- **They are generated** from the schema model in `build/Build.DatabaseSchema.cs` by `dotnet fallout
  GenerateSchema`; CI's `VerifySchema` fails a build where they are out of step, so do not edit them.
  `SchemaScriptTest` parses each `tables/` script and its `create_` counterpart with one parser and compares
  their tables, columns and indexes; `SchemaProvisioningTest` provisions a real database of each dialect and
  compares its catalog with one built from `tables/`.
- **They only create.** Nothing drops or alters, so they are safe against an existing schema, and they are
  **not** an upgrade: they cannot add a column to an existing table. Only `migrations/` moves an existing
  schema forward.

## Upgrading an existing database

Each folder under `migrations/` is named for the Quartz.NET version that introduced the change. Run the
file whose suffix matches your database: `_sqlServer`, `_postgres`, `_mysql_innodb`, `_oracle`, `_sqlite`,
`_firebird`.

- **Apply every folder between your current and target versions, in ascending order, without skipping.**
  Migrations assume the earlier ones ran. Optional ones may be deferred but are cumulative: skipping 3.17
  and later running 3.19 still leaves you without the 3.17 column.
- Everything except the SQL Server 1.0→2.0 script checks before it acts, so re-running is a no-op and a
  partly applied migration can be re-run. SQLite's `ADD COLUMN` is the exception: it has no conditional
  DDL and fails on a second run — see the note in each SQLite file.

| Version | What changed | Status | Databases | Branch |
|---|---|---|---|---|
| [`2.0`](migrations/2.0) | Listener tables dropped, flag columns become `bit`, `SCHED_NAME` introduced across every table | Required from 1.x | SQL Server only (sample; adapt for others) | both |
| [`2.2`](migrations/2.2) | `SCHED_TIME` on `QRTZ_FIRED_TRIGGERS` (#113) | Required from 2.0/2.1 | all | both |
| [`2.6`](migrations/2.6) | `TIME_ZONE_ID` on `QRTZ_SIMPROP_TRIGGERS` and `QRTZ_CRON_TRIGGERS` (#136, #1985) | Required from ≤2.5 | all | both |
| [`3.0`](migrations/3.0) | `IMAGE` columns become `VARBINARY(MAX)` (#291) | Required from 2.6 | SQL Server only (no other dialect used `IMAGE`) | both |
| [`3.17`](migrations/3.17) | `MISFIRE_ORIG_FIRE_TIME` on `QRTZ_TRIGGERS` (#2899) | Optional on 3.x, **required on 4.x** | all | both |
| [`3.18`](migrations/3.18) | `EXECUTION_GROUP` on `QRTZ_TRIGGERS` and `QRTZ_FIRED_TRIGGERS` (#3004) | Optional on 3.x, **required on 4.x** | all | both |
| [`3.19`](migrations/3.19) | `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` on `QRTZ_TRIGGERS` (#3013, #3144) | Optional on 3.x, **required on 4.x** | all | both |
| [`3.20`](migrations/3.20) | Index set realigned so every index leads with `SCHED_NAME`; prefix-redundant indexes dropped (#3203) | Optional, performance only | all | both |
| [`4.0`](migrations/4.0) | **Two files**: `schema_30_to_40_upgrade_<db>.sql` (columns and a table) and `schema_30_to_40_indexes_<db>.sql` (the 4.x index shape) — see [below](#upgrading-3x--4x-is-mandatory) | Upgrade **mandatory for 4.x** and safe during a mixed window; indexes optional, and wait for the last 3.x node | all | `main` only |
| [`4.2`](migrations/4.2) | `add_continuations_<db>.sql`: `CONTINUES_TRIGGER_NAME`, `CONTINUES_TRIGGER_GROUP` and `CONTINUATION_CONDITION` on `QRTZ_TRIGGERS`, for a trigger that waits in the store for another trigger's firing to end (#3805) | **Required on 4.2+**, safe during a mixed 4.1/4.2 window | all | `main` only |
| [`4.2`](migrations/4.2) | `add_execution_history_<db>.sql`: the `QRTZ_EXECUTION_HISTORY` and `QRTZ_MISFIRE_HISTORY` tables, a cluster-wide record of what ran and what was missed (#3771) | **Optional**: needed only with `UseExecutionHistory()`; safe under a mixed cluster, since a node without it neither writes nor reads these tables | all | `main` only |
| [`4.3`](migrations/4.3) | `add_fire_progress_<db>.sql`: `PROGRESS` and `PROGRESS_MESSAGE` on `QRTZ_FIRED_TRIGGERS`, what a running job last reported (#3874) | **Required on 4.3+**, safe during a mixed 4.2/4.3 window | all | `main` only |
| [`4.3`](migrations/4.3) | `add_execution_log_<db>.sql`: `EXECUTION_LOG` on `QRTZ_EXECUTION_HISTORY`, the log lines an execution wrote (#3874) | **Optional**: needed only with `UseExecutionHistory()`, and only after `4.2/add_execution_history_<db>.sql`; safe under a mixed cluster | all | `main` only |

### Upgrading 3.x → 4.x is mandatory

3.x probes for `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE` and `PREFERRED_NODE_AUTO` at
startup, warns when they are missing, and turns the feature off. **4.x has no probes** and requires all
four. It also adds what 3.x never had — `RETRY_POLICY` and `RETRY_ATTEMPT` on `QRTZ_TRIGGERS` (#3520),
both nullable with no default, and the `QRTZ_PAUSED_JOB_GRPS` table (#3336) — and validates its schema at
startup. So even a 3.x database with every optional migration needs [`migrations/4.0`](migrations/4.0),
which is **two files** run at different times:

| File | Status | When |
|---|---|---|
| `schema_30_to_40_upgrade_<db>.sql` | Mandatory | Now. It folds in 3.17, 3.18 and 3.19, every statement is guarded, and all of it is safe while 3.x nodes are still up. |
| `schema_30_to_40_indexes_<db>.sql` | Optional, performance only | Once the last 3.x node has shut down, or straight after the first file when nothing is running. It supersedes 3.20. |

- The index file lands the 4.x index shape. It drops and recreates `IDX_QRTZ_T_NFT_ST` as
  `(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)`, Firebird excepted
  (#3510), and drops `IDX_QRTZ_T_NFT_ST_MISFIRE`, which that reshape left with no reader on any dialect
  (#3656).
- It waits because 3.x drives its misfire sweep from `IDX_QRTZ_T_NFT_ST_MISFIRE`; 4.x does not read it.
- Run it top to bottom: the reshape of `IDX_QRTZ_T_NFT_ST` comes before that drop, so a schema is never
  left with neither index. It is guarded throughout, so re-running changes nothing.
- A 4.x node starts against a database that has the first file and not the second; it scans where it
  would otherwise seek, which matters on a large schema.

**This is the only copy.** The `3.x` branch no longer carries one: the 3.x → 4.0 script follows 4.x's
schema, which changes here. Read the version of this folder that matches the 4.x you are upgrading to.

### Upgrading 4.0/4.1 → 4.2 is mandatory too

[`migrations/4.2`](migrations/4.2) is the first schema change since 4.0. It adds three nullable columns to
`QRTZ_TRIGGERS`. 4.2 names all three when it stores a trigger, so a 4.2 node refuses to start against a
database without them.

**Run it while 4.1 nodes are still running.** The columns are nullable with no default, so every existing
row stays valid. A 4.1 node's `INSERT` names its own columns, its acquisition and misfire sweeps select
`WAITING`, and its cluster recovery touches `ACQUIRED` and `BLOCKED`, so nothing a 4.1 node does on its own
picks up an `AWAITING` row.

But a 4.1 node reads a state string it does not recognise as waiting, so it *reports* an `AWAITING` row as
`Normal`, and two operations act on that:

- 4.1's single-trigger `PauseTrigger` writes `PAUSED` over the row, and a resume then makes it `WAITING`:
  the continuation fires without its parent.
- A 4.1 reschedule rewrites the row as an ordinary trigger that has forgotten what it waited for.
- (`PauseJob` and the group and batch pauses name the states they move, so they leave an `AWAITING` row
  alone.)

A 4.1 node also cannot **release or discard** a continuation: a parent completing there leaves its waiting triggers
where they are. So:

1. Run the migration.
2. Roll **every** node to 4.2.
3. Only then start scheduling continuations.

While any 4.1 node runs, do not pause, resume or reschedule a continuation from it. A cluster that
schedules its first continuation after the last node has rolled has none for a 4.1 node to touch.

A fresh install from [`tables/`](tables) already has the columns.

### The execution history's tables are optional

`add_execution_history_<db>.sql`, in the same folder, creates `QRTZ_EXECUTION_HISTORY` and
`QRTZ_MISFIRE_HISTORY`. Only `UsePersistentStore(store => store.UseExecutionHistory())` reads or writes
them; nothing else in the schema references them, and a scheduler that keeps no history never probes for
them. Run it when you want a cluster-wide execution history, at any time, or never.

- A store configured with `UseExecutionHistory()` refuses to start without them, naming this script.
- A fresh install from [`tables/`](tables), and `ProvisionSchema()`, create them anyway, so only a
  database created by 4.0 or 4.1 needs the file.
- Safe under a mixed cluster both ways: a 4.0 or 4.1 node cannot see these tables, and a 4.2 node that
  keeps no history neither writes nor reads them. Nodes that do share one history; every row carries the
  instance id that wrote it.

### Upgrading 4.2 → 4.3

[`migrations/4.3`](migrations/4.3) has two files.

| File | Status | What |
|---|---|---|
| `add_fire_progress_<db>.sql` | **Required** | `PROGRESS` and `PROGRESS_MESSAGE` on `QRTZ_FIRED_TRIGGERS`. A 4.3 node reads them whenever it lists what is running, and refuses to start without them. |
| `add_execution_log_<db>.sql` | Optional | `EXECUTION_LOG` on `QRTZ_EXECUTION_HISTORY`. Needed only with `UseExecutionHistory()`; run it after `4.2/add_execution_history_<db>.sql`, because it alters that table and fails where the table is missing. |

- **Run both while 4.2 nodes are still running.** Every column is nullable with no default. A 4.2 node
  never names them: its firings show no progress and its history rows no log.
- A fresh install from [`tables/`](tables), and `ProvisionSchema()`, already have all three columns.

## Where these files moved

The scripts used to sit flat in `database/`, with the non-SQL Server dialects commented out inside each
file. Old links still work against release tags, for example
`https://github.com/quartznet/quartznet/blob/v3.19.1/database/schema_30_add_preferred_node.sql`.

| Old path | New path |
|---|---|
| `database/sqlserver_schema_10_to_20_upgrade.sql`<br>`database/schema_10_to_20_upgrade.sql` | `migrations/2.0/schema_10_to_20_upgrade_sqlServer.sql` |
| `database/schema_20_to_22_upgrade.sql` | `migrations/2.2/schema_20_to_22_upgrade_<db>.sql` |
| `database/schema_25_to_26_upgrade.sql` | `migrations/2.6/schema_25_to_26_upgrade_<db>.sql` |
| `database/schema_26_to_30.sql`<br>`database/schema_26_to_30_upgrade.sql` | `migrations/3.0/schema_26_to_30_upgrade_sqlServer.sql` |
| `database/schema_30_add_misfire_orig_fire_time.sql` | `migrations/3.17/add_misfire_orig_fire_time_<db>.sql` |
| `database/schema_30_add_execution_group.sql` | `migrations/3.18/add_execution_group_<db>.sql` |
| `database/schema_30_add_preferred_node.sql` | `migrations/3.19/add_preferred_node_<db>.sql` |
| `database/schema_30_drop_redundant_indexes.sql`<br>`database/schema_30_postgres_index_realignment.sql`<br>`database/schema_30_sqlite_indexes.sql` | `migrations/3.20/index_alignment_<db>.sql` |
| `database/schema_30_to_40_upgrade.sql` | `migrations/4.0/schema_30_to_40_upgrade_<db>.sql`, and its index half `migrations/4.0/schema_30_to_40_indexes_<db>.sql` |

## Adding a migration

Everything under `migrations/` except the `2.0` and `3.0` folders is **generated**; hand edits are
overwritten.

1. Add the change to every `tables/tables_*.sql`, so fresh installs get it.
2. Add it to the schema model in `build/Build.DatabaseSchema.cs`, so a scheduler that provisions its own
   schema gets it too, and run `dotnet fallout GenerateSchema`. CI runs `VerifySchema`, and
   `SchemaScriptTest` compares the two sets object by object; a change in only one fails both.
3. Describe the change once in `build/Build.DatabaseMigrations.Scripts.cs`, and fold it into the `4.0`
   script there too if it is a 3.x change.
4. Run `dotnet fallout GenerateMigrations` and commit the result. CI runs `VerifyMigrations`, so a
   definition change without a regenerated script fails the build.
5. If **both branches can run the change**, mirror the new `migrations/` folder and its definition to `3.x`
   in a companion pull request: a migration both branches can run must stay byte-identical, or a documented
   path 404s on the branch that lacks it (#3218). A **4.x-only** change has no companion. Either way the
   `4.0` fold happens here, since `3.x` does not carry that folder. `tables/` and this README describe
   their own branch and are not mirrored verbatim.
6. Add a section to the schema-changes page in the documentation (docs live on `main` only).

The `2.0` and `3.0` migrations are hand-written SQL Server-only historical scripts that predate this
layout and have no per-dialect variants.
