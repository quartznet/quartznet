# Quartz.Tests.Integration.MixedVersionNode

One node of the cluster `MixedVersionClusterPostgresTest` runs: a clustered scheduler on a shared
PostgreSQL, with the shipped defaults, driven one command at a time over standard input.

It builds twice, with one assembly name, so a job type either build stores reads back the same in the
other:

| Build | Quartz | Output |
|---|---|---|
| default | the working tree | `artifacts/bin/Quartz.Tests.Integration.MixedVersionNode/<config>/` |
| `-p:ReleasedQuartzVersion=4.3.0` | that version from nuget.org | `artifacts/bin/Quartz.Tests.Integration.MixedVersionNode.4.3.0/<config>/` |

Building the default builds the released one too, so a solution build, `dotnet fallout Compile` and
`dotnet build src/Quartz.Tests.Integration` all leave both where the test looks.

## Running it

```
--instance-id        this node's instance id
--scheduler-name     the cluster's scheduler name
--connection-string  the PostgreSQL both nodes share
--table-prefix       the Quartz table prefix
--runs-table         the table each job execution is written to; the test creates it
--log-level          the least level written to standard error (default Warning)
--history            keep the execution history in the database (UseExecutionHistory())
```

`Protocol.cs` describes the command format and `Node.cs` lists the commands. Only the working tree reports
a run's outcome (`JobRunReport`) and a firing's progress.
