# Quartz.NET database scripts

Quartz.NET does not create or migrate its schema automatically. Creating the tables and applying
schema changes is a manual, deliberate step.

A migration **both branches can run** is kept byte-identical on `3.x` and `main`, so its path
resolves whichever branch you land on. The `4.0` folder is the exception: it is the 3.x → 4.x
upgrade, so it can carry changes that exist only on 4.x, and it is maintained on `main`. The
**Branch** column below says which is which — for anything marked `main`, use the `main` copy.
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
| [`4.0`](migrations/4.0) | Everything above, plus the `QRTZ_PAUSED_JOB_GRPS` table (#3336) and the 4.x index shape | **Mandatory for 4.x** | all | `main` |

### Upgrading 3.x → 4.x is mandatory

Quartz.NET 3.x probes for `MISFIRE_ORIG_FIRE_TIME`, `EXECUTION_GROUP`, `PREFERRED_NODE` and
`PREFERRED_NODE_AUTO` at startup, logs a warning when they are missing, and turns the
corresponding feature off. **4.x removed those probes** and assumes all four columns exist.

4.x also adds a table 3.x never had, `QRTZ_PAUSED_JOB_GRPS`, and validates its whole schema at
startup — so even a 3.x database that took every optional migration going still needs this one.

So a 3.x database will not work against 4.x until [`migrations/4.0`](migrations/4.0) has been
applied. That script folds in everything from 3.17, 3.18, 3.19 and 3.20, and every statement is
guarded — run it whether or not you applied the optional ones.

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
2. Describe the change once in `build/Build.DatabaseMigrations.Scripts.cs`, and fold it into the
   `4.0` script there too if it is a 3.x change.
3. Run `dotnet fallout GenerateMigrations` and commit the result. CI runs `VerifyMigrations`, so
   a definition change without a regenerated script fails the build.
4. If **both branches can run the change**, mirror `migrations/`, this README and
   `build/Build.DatabaseMigrations*.cs` to the other branch in a companion PR — they must stay
   byte-identical, or a documented path 404s on whichever branch lacks it (#3218). A change that
   exists **only on 4.x** has no companion: it goes into `4.0` alone, on `main`, and the Branch
   column above says so. `tables/` is version-specific and stays per-branch either way.
5. Add a section to the schema-changes page in the documentation (docs live on `main` only).

The `2.0` and `3.0` migrations are hand-written: they are SQL Server-only historical scripts
that predate this layout and have no per-dialect variants.
