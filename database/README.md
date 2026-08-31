# Quartz.NET database scripts

These are the scripts a person runs against a database with a database client. Since 4.0 a scheduler
can also create a missing schema for itself, if it is asked to — see
[What the scheduler runs](#what-the-scheduler-runs) below — but **migrating** an existing schema is
still a manual, deliberate step, and nothing in Quartz does it for you.

A migration **both branches can run** is kept byte-identical on `3.x` and `main`, so its path
resolves whichever branch you land on. The `4.0` folder is the exception and **lives on `main`
only**: it is the 3.x → 4.0 upgrade path, what it has to do is decided by 4.x's schema, so one
maintained copy is the point — the `3.x` branch links here instead of carrying a mirror that
would go stale the moment 4.x moved. The **Branch** column below says which versions are on both.
`tables/` is the *current* schema and so differs by design: on `3.x` it creates the 3.x schema,
on `main` the 4.x one.

```
database/
  tables/       fresh-install scripts -- one per database, creates the current schema
  migrations/   schema changes, grouped by the Quartz.NET version that introduced them
```

## Fresh install

Run the one script matching your database from [`tables/`](tables). It creates the current
schema in full, including every column the migrations below add — a new database needs nothing
from `migrations/`.

**Every one of these scripts drops an existing Quartz schema before it recreates it**, so running
one against a live database destroys what is in it. Each says at the top how to decline that: set
the `DropDb` variable declared above the drops to `0` — `@DropDb` on SQL Server and MySQL — or, on
SQLite, which has no variables, delete the block between the `BEGIN DROP TABLES` and
`END DROP TABLES` markers. What is left then only creates tables, so run it on a database that has
none: the `CREATE TABLE` statements are not guarded and fail against a schema that already exists.

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

A store configured with `SchemaProvisioning.CreateIfMissing` — `store.ProvisionSchema()` in code —
creates a missing schema itself at startup. It does **not** run the scripts above. It runs a second
set, embedded in `Quartz.dll` and living in the source tree at
[`src/Quartz/Impl/AdoJobStore/Schema/create_<dialect>.sql`](../src/Quartz/Impl/AdoJobStore/Schema):

| Database | Script |
|---|---|
| SQL Server | [`create_sqlServer.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_sqlServer.sql) |
| PostgreSQL | [`create_postgres.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_postgres.sql) |
| MySQL / MariaDB | [`create_mysql_innodb.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_mysql_innodb.sql) |
| Oracle | [`create_oracle.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_oracle.sql) |
| SQLite | [`create_sqlite.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_sqlite.sql) |
| Firebird | [`create_firebird.sql`](../src/Quartz/Impl/AdoJobStore/Schema/create_firebird.sql) |

Six, against `tables/`'s eight: the memory-optimized and pre-2016 SQL Server variants have no
counterpart here. Both are deliberate departures from the standard schema, chosen by a person for a
particular deployment, and neither is a decision a scheduler should make for itself. Neither has a
driver delegate of its own either, so a store pointed at one of them and asked to provision creates the
**standard** schema instead. Run those two by hand and leave the setting at `Validate`.

**These are not the scripts to run by hand.** They are written for an ADO.NET provider rather than for
a command-line client: the table prefix is a `{0}` placeholder rather than a literal `QRTZ_`, statements
are separated by a line reading `--;;` rather than by `GO`, `/` or a `SET TERM` pair, and there is no
`DECLARE` or variable of any kind because a provider is sent one statement at a time. Paste one into a
query window and it will not run. Run [`tables/`](tables) instead — that is what those files are for.

They are also **generated**, from the schema model in `build/Build.DatabaseSchema.cs`; `dotnet fallout
GenerateSchema` emits them and CI's `VerifySchema` fails a build where they are out of step. Editing
one by hand is pointless. What keeps the two sets honest about each other is `SchemaScriptTest`, which
parses a `tables/` script and its `create_` counterpart with one parser and compares the tables, columns
and indexes they name, and `SchemaProvisioningTest`, which provisions a real database of each dialect and
compares its catalog with one built from `tables/`.

The provisioning script only ever creates. Nothing in it drops or alters, so it is safe against a schema
that already exists — and it is equally **not** an upgrade: it cannot add a column to a table that is
already there. Moving an existing schema forward is the `migrations/` folders below, and only those.

## Upgrading an existing database

Each folder under `migrations/` is named for the Quartz.NET version that introduced the change.
Inside, run the file whose suffix matches your database — `_sqlServer`, `_postgres`,
`_mysql_innodb`, `_oracle`, `_sqlite`, `_firebird`.

**Apply every folder between your current version and your target version, in ascending order,
and do not skip one.** Migrations assume the ones before them have run. The optional ones may be
deferred, but they are cumulative: skipping 3.17 and later running 3.19 still leaves you without
the 3.17 column.

Everything except the SQL Server 1.0→2.0 script checks before it acts, so re-running a migration
is a no-op and a partially-applied migration is safe to re-run. SQLite is the exception for
`ADD COLUMN`: it has no conditional DDL, so those statements fail on a second run — see the note
in each SQLite file.

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
| [`4.0`](migrations/4.0) | Everything above, plus `RETRY_POLICY` and `RETRY_ATTEMPT` on `QRTZ_TRIGGERS` (#3520), the `QRTZ_PAUSED_JOB_GRPS` table (#3336) and the 4.x index shape, in which `IDX_QRTZ_T_NFT_ST` is dropped and recreated as `(SCHED_NAME, TRIGGER_STATE, NEXT_FIRE_TIME ASC, PRIORITY DESC, MISFIRE_INSTR)` — Firebird excepted (#3510) | **Mandatory for 4.x** | all | `main` only |

### Upgrading 3.x → 4.x is mandatory

Quartz.NET 3.x probes for `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE` and
`PREFERRED_NODE_AUTO` at startup, logs a warning when they are missing, and turns the
corresponding feature off. **4.x removed those probes** and assumes all four columns exist.

4.x also adds columns and a table 3.x never had — `RETRY_POLICY` and `RETRY_ATTEMPT` on
`QRTZ_TRIGGERS`, both nullable with no default, and the `QRTZ_PAUSED_JOB_GRPS` table — and
validates its schema at startup, so even a 3.x database that took every optional migration going
still needs this one.

So a 3.x database will not work against 4.x until [`migrations/4.0`](migrations/4.0) has been
applied. That script folds in everything from 3.17, 3.18, 3.19 and 3.20, and every statement is
guarded — run it whether or not you applied the optional ones.

**This is the only copy.** The `3.x` branch used to carry one and no longer does: what the
3.x → 4.0 script has to do is decided by 4.x's schema, which moves here, so a second copy there
could only be right by accident. Read the version of this folder that matches the 4.x you are
upgrading to.

## Where these files moved

The scripts used to sit flat in `database/`, with the dialects other than SQL Server commented
out inside each file. Old links keep working against release tags, for example
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
| `database/schema_30_to_40_upgrade.sql` | `migrations/4.0/schema_30_to_40_upgrade_<db>.sql` |

## Adding a migration

Everything under `migrations/` except the `2.0` and `3.0` folders is **generated** — do not edit
those files by hand, they will be overwritten.

1. Add the change to every `tables/tables_*.sql`, so fresh installs get it.
2. Add it to the schema model in `build/Build.DatabaseSchema.cs` as well, so a scheduler that
   provisions its own schema gets it too, and run `dotnet fallout GenerateSchema`. CI runs
   `VerifySchema`, and `SchemaScriptTest` compares the two sets object by object — a change made in
   only one of them fails both.
3. Describe the change once in `build/Build.DatabaseMigrations.Scripts.cs`, and fold it into the
   `4.0` script there too if it is a 3.x change.
4. Run `dotnet fallout GenerateMigrations` and commit the result. CI runs `VerifyMigrations`, so
   a definition change without a regenerated script fails the build.
5. If **both branches can run the change**, mirror the new `migrations/` folder and the definition
   behind it to `3.x` in a companion pull request — a migration both branches can run must stay
   byte-identical, or a documented path 404s on whichever branch lacks it (#3218). A change that
   exists **only on 4.x** has no companion at all. Either way the `4.0` fold happens here: `3.x`
   does not carry that folder. `tables/` and this README describe their own branch, so neither is
   mirrored verbatim.
6. Add a section to the schema-changes page in the documentation (docs live on `main` only).

The `2.0` and `3.0` migrations are hand-written: they are SQL Server-only historical scripts
that predate this layout and have no per-dialect variants.
