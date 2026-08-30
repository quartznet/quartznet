# Quartz.NET with Wolverine

A runnable console application putting Quartz.NET beside the [Wolverine](https://wolverinefx.net)
message bus. Wolverine can deliver a message later but has no recurring or cron concept at all, and its
maintainer has said it never will
([JasperFx/wolverine#1403](https://github.com/JasperFx/wolverine/issues/1403)); this project is the
recipe for filling that gap, and the prose that goes with it is
[Quartz.NET with Wolverine](https://www.quartz-scheduler.net/documentation/quartz-4.x/how-tos/wolverine.html).

Every C# block on that page is copied from this project and checked against it by
`WolverineHowToTest`, so the page cannot drift from code that compiles.

## Running it

```shell
dotnet run --project src/Quartz.Examples.Wolverine
```

Nothing external is needed: Wolverine's in-memory local transport and Quartz's in-memory store. Add
`--smoke` to run every part on a compressed clock, assert each one happened, and exit:

```shell
dotnet run --project src/Quartz.Examples.Wolverine -- --smoke
```

Set `QUARTZ_WOLVERINE_POSTGRES` to a connection string and two more parts come to life — Wolverine
agents, which do not run at all without a message store, and enlisting Quartz in the application's own
transaction, which needs a persistent job store:

```shell
QUARTZ_WOLVERINE_POSTGRES="Host=localhost;Database=quartz;Username=quartz;Password=quartz" \
  dotnet run --project src/Quartz.Examples.Wolverine -- --smoke
```

The schema is created on startup in that mode, including a small `refunds` table the last part writes
to.

## The six parts

| File | What it shows |
|---|---|
| `Part1RecurringPublishing.cs` | An `IJob<TInput>` publishing a Wolverine message on a cron schedule |
| `Part2OneOffFromHandler.cs` | Scheduling one firing from a handler, and cancelling every firing for one order by trigger group |
| `Part3RawMessageData.cs` | Storing a serialized envelope and sending it with `SendRawMessageAsync` at fire time |
| `Part4TunedLatency.cs` | `IdleWaitTime`, `MaxBatchSize` and `BatchTriggerAcquisitionFireAheadTimeWindow`, and what they really bound |
| `Part5StartedByWolverine.cs` | `AutoStart = false`, with the scheduler started by Wolverine's runtime |
| `Part6EnlistedTransaction.cs` | One transaction holding the application's row, Wolverine's outgoing envelope and Quartz's trigger |

Each file opens with a comment saying what Wolverine lacks and what Quartz supplies.
