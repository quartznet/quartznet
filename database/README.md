# Quartz.NET database scripts

Quartz.NET does not create or migrate its schema automatically. Creating the tables and applying
schema changes is a manual, deliberate step.

A migration **both branches can run** is kept byte-identical on `3.x` and `main`, so its path
resolves whichever branch you land on. The **3.x → 4.0 upgrade scripts are the exception: they
live on the `main` branch only**, and the links to them below point there. They change every time
4.x's schema changes, so one maintained copy is the point — a mirror on this branch would go
stale the moment 4.x moved, and an upgrade script that looks right but is missing something is
worse than one that is plainly somewhere else. `tables/` is the *current* schema and so differs
by design: on `3.x` it creates the 3.x schema, on `main` the 4.x one.

```
database/
  tables/       fresh-install scripts -- one per database, creates the current schema
  migrations/   schema changes, grouped by the Quartz.NET version that introduced them
```

## Fresh install

Run the one script matching your database from [`tables/`](tables). It creates the current
schema in full, including every column the migrations below add — a new database needs nothing
from `migrations/`.

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
| [`4.0`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) | Everything above, plus the tables and index shape 4.x adds | **Mandatory for 4.x** | all | `main` only |

### Upgrading 3.x → 4.x is mandatory, and the scripts are on `main`

Quartz.NET 3.x probes for `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE` and
`PREFERRED_NODE_AUTO` at startup, logs a warning when they are missing, and turns the
corresponding feature off. **4.x removed those probes** and assumes all four columns exist. 4.x
also validates its whole schema at startup, and it has tables 3.x never had, so even a database
that took every optional migration on this list still needs the upgrade script.

Run it from the `main` branch:
**<https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0>**

That is the only copy. This branch used to carry one and no longer does: what the 3.x → 4.0
script has to do is decided by 4.x's schema, which moves on `main`, so a second copy here could
only be right by accident. Read the version of it that matches the 4.x you are upgrading to.

The script folds in everything from 3.17, 3.18, 3.19 and 3.20, and every statement is guarded
(SQLite excepted, as always, for `ADD COLUMN`) — run it whether or not you applied the optional
ones.

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
| `database/schema_30_to_40_upgrade.sql` | [`migrations/4.0/schema_30_to_40_upgrade_<db>.sql`](https://github.com/quartznet/quartznet/tree/main/database/migrations/4.0) on `main` |

## Adding a migration

Everything under `migrations/` except the `2.0` and `3.0` folders is **generated** — do not edit
those files by hand, they will be overwritten.

1. Add the change to every `tables/tables_*.sql`, so fresh installs get it.
2. Describe the change once in `build/Build.DatabaseMigrations.Scripts.cs`.
3. Run `dotnet fallout GenerateMigrations` and commit the result. CI runs `VerifyMigrations`, so
   a definition change without a regenerated script fails the build.
4. Mirror the new `migrations/` folder and the definition behind it to `main` in a companion pull
   request — a migration both branches can run must stay byte-identical, or a documented path
   404s on whichever branch lacks it (#3218). The companion is also where the change gets folded
   into the `4.0` script, since that script is generated on `main` alone. `tables/` and this
   README describe what their own branch carries, so neither is mirrored verbatim.
5. Add a section to the schema-changes page in the documentation (docs live on `main` only).

The `2.0` and `3.0` migrations are hand-written: they are SQL Server-only historical scripts
that predate this layout and have no per-dialect variants.
