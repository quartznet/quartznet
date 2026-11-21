# Quartz.Tests.Integration.MixedVersionNode

One node of the cluster `MixedVersionClusterPostgresTest` runs: a clustered scheduler on a shared
PostgreSQL, with the shipped defaults, driven one command at a time over standard input.

It builds twice, with one assembly name, so a job type either build stores reads back the same in the
other:

| Build | Quartz | Output |
|---|---|---|
| default | the working tree | `artifacts/bin/Quartz.Tests.Integration.MixedVersionNode/<config>/` |
| `-p:ReleasedQuartzVersion=4.7.3` | that version from nuget.org | `artifacts/bin/Quartz.Tests.Integration.MixedVersionNode.4.7.3/<config>/` |

Building the default builds the released one too, so a solution build, `dotnet fallout Compile` and
`dotnet build src/Quartz.Tests.Integration` all leave both where the test looks.

The released version is named once, as `MixedVersionReleasedQuartz` in
[`MixedVersionReleasedQuartz.props`](MixedVersionReleasedQuartz.props): this project builds against it,
and `Quartz.Tests.Integration` imports it and stamps it into its assembly metadata, where the test reads it.
At each minor release it moves to the newest patch of the previous minor
([branches-and-releases.md](../../maintainers/branches-and-releases.md#the-mixed-version-gate-at-a-minor-release)).
The node's source compiles against both, so it uses only API that version has.

## Running it

```text
--instance-id        this node's instance id
--scheduler-name     the cluster's scheduler name
--connection-string  the PostgreSQL both nodes share
--table-prefix       the Quartz table prefix
--runs-table         the table each job execution is written to; the test creates it
--log-level          the least level written to standard error (default Warning)
--history            keep the execution history in the database (UseExecutionHistory())
--lock-free          true or false: AdoJobStoreOptions.LockFreeFiringRounds, on the working tree only (default true)
--checkin-interval   ClusteringOptions.CheckinInterval, in milliseconds (default: what the version ships)
--checkin-threshold  ClusteringOptions.CheckinMisfireThreshold, in milliseconds (default: what the version ships)
```

`Protocol.cs` describes the command format and `Node.cs` lists the commands. Only the working tree reports
a run's outcome (`JobRunReport`) and a firing's progress. `round-locks` answers how many firing rounds the
node ran with no lock, under `TRIGGER_ACCESS`, and without saying (an older node).
