# Schema baselines

Frozen copies of `database/tables/tables_<dialect>.sql` as they stood at a released version, so that
`MigrationScriptTest` can build that version's schema and migrate it forward.

## `3.16/`

Vendored verbatim from tag **`v3.16.1`**, path `database/tables/tables_<dialect>.sql` (the same six
files are byte-identical at `v3.16.0`; `git rev-parse v3.16.1:database/tables/tables_sqlite.sql` and
friends reproduce the hashes). Each file is byte-identical to the copy the 3.x branch keeps under
`src/Quartz.Tests.Integration/SchemaBaselines/3.16/`, so the two branches migrate from the same
starting point.

3.16 is the last version before the schema started moving: `MISFIRE_ORIG_FIRE_TIME` arrived in 3.17,
`EXECUTION_GROUP` in 3.18, `PREFERRED_NODE` / `PREFERRED_NODE_AUTO` in 3.19 and the realigned index
set in 3.20. A database created by any earlier 3.x release therefore has this shape, which makes it
the oldest schema the 4.0 upgrade has to accept.

## `3.20/`

Vendored verbatim from tag **`v3.20.0`**, path `database/tables/tables_<dialect>.sql`. 3.20 is the
newest 3.x release, so this is the *other* end of the range the 4.0 upgrade has to accept: a database
that took every optional 3.x migration going, where 3.16 is one that took none.

`UpgradeRehearsalTest` builds this schema, has `Quartz.Tests.Integration.Seeder` — a separate process
on the released `Quartz` 3.20.0 package — fill it with rows, and then runs the 4.0 upgrade over data
rather than over an empty schema. The three SQL Server variants 3.20 shipped are not vendored: the
rehearsal runs against `tables_sqlServer.sql`, which is the one the test environment creates.

These are copies rather than something read out of git at run time on purpose: CI checks out shallow,
so a `git show origin/3.x:...` in a test would fail on the very machines that run it. They are also
deliberately never regenerated — the point of a baseline is that it does not track `main`. Changing a
file here changes what "an old database" means, so do it only to correct a mis-vendored copy, and say
which revision the correction came from.
