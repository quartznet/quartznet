# Quartz.Tests.Integration.Seeder

Fills a 3.20 schema with rows the **released `Quartz` 3.20.0 package** wrote, so that
`UpgradeRehearsalTest` can run the 4.0 migration over data instead of over an empty schema, and so
that `LegacyJsonPayloadTest` can read fixtures captured from a 3.20 process rather than literals
transcribed by hand.

It is a separate process for one reason: `Quartz` 3.20.0 and this repository's `Quartz` have the same
assembly identity, so nothing can reference both. The version is pinned literally — a
`VersionOverride` on the `PackageReference`, never a float — because the whole point of the fixture is
a *named* released version.

## What it seeds

Under one scheduler name and one table prefix:

- one trigger of each of the five families 3.20 persists, in group `seed`, plus the same families
  again in group `blob`, which `BlobStorageOverride` declines a persistence delegate for so that
  `QRTZ_BLOB_TRIGGERS` holds a real payload of each shape;
- a trigger carrying `EXECUTION_GROUP` and `PREFERRED_NODE`;
- calendars of all six kinds and a chained pair, each with probe instants and the answers 3.20 gave;
- a job data map holding every value type 4.0's JSON write gate admits, plus the
  `Dictionary<string, string>` of #3582, and — on a job of its own — a `JobKey`, which the gate would
  refuse;
- a paused trigger group and a paused job group, each with one member stored *before* the pause and
  one *after* it;
- a `QRTZ_FIRED_TRIGGERS` row abandoned mid-execution, by killing the process with a firing still in
  flight.

It finishes by writing `seed.json`, the manifest the rehearsal's assertions name their expectations
from, and — when `--fixture-output` says where — the blob columns, byte for byte.

## Running it

```
--dialect            sqlite | sqlServer | postgres | mysql_innodb | oracle | firebird
--connection-string  the connection string for that database
--serializer         json (Newtonsoft) | stj (System.Text.Json)
--output             directory to write seed.json to
--table-prefix       table prefix to seed under            (default QRTZU_)
--scheduler-name     scheduler instance name               (default Quartz320Upgrade)
--instance-id        scheduler instance id                 (default seed-node)
--schema             fresh-install script to run first     (SQLite only)
--fixture-output     directory to dump the blob columns to (optional)
```

`--schema` is SQLite-only on purpose. Every other dialect's fresh-install script is written for that
database's own command-line client, and the rehearsal runs it through exactly that client inside the
container — which is the only way to be sure the script a user is told to run is the script that ran.

## Regenerating the committed fixtures

`src/Quartz.Tests.Unit/TestData/Legacy/3.20/` is produced by two SQLite runs, and its own README says
so. From the repository root, after `dotnet build src/Quartz.Tests.Integration.Seeder`:

```shell
dotnet artifacts/bin/Quartz.Tests.Integration.Seeder/debug/Quartz.Tests.Integration.Seeder.dll \
  --dialect sqlite --connection-string "Data Source=/tmp/seed-stj.db;" --serializer stj \
  --schema src/Quartz.Tests.Integration/SchemaBaselines/3.20/tables_sqlite.sql \
  --table-prefix QRTZ_ --output /tmp/seed-stj \
  --fixture-output src/Quartz.Tests.Unit/TestData/Legacy/3.20

dotnet artifacts/bin/Quartz.Tests.Integration.Seeder/debug/Quartz.Tests.Integration.Seeder.dll \
  --dialect sqlite --connection-string "Data Source=/tmp/seed-json.db;" --serializer json \
  --schema src/Quartz.Tests.Integration/SchemaBaselines/3.20/tables_sqlite.sql \
  --table-prefix QRTZ_ --output /tmp/seed-json \
  --fixture-output src/Quartz.Tests.Unit/TestData/Legacy/3.20
```

Delete the two database files first; the schema script is a fresh install and will not run twice.
