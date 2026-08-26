# Quartz.NET console examples

A guided tour of Quartz.NET, one runnable example at a time. Each one schedules something, starts a
scheduler, and then waits while it fires — so the thing being taught happens in front of you, in the
console, rather than being described.

This is not the canonical sample set. The code the [documentation](https://www.quartz-scheduler.net/)
renders lives in `src/Quartz.Documentation.Samples`, and is compiled as part of the solution so that it
cannot rot. The examples here exist for what a page cannot do: run.

For Quartz under a host, see the sibling projects instead — `Quartz.Examples.Worker` (a worker
service), `Quartz.Examples.AspNetCore` (health checks, the HTTP API and the dashboard) and
`Quartz.Examples.HttpClient` (driving a remote scheduler over HTTP, which is what replaced the
remoting example this tour used to carry).

## Running it

```shell
# pick from a menu
dotnet run --project src/Quartz.Examples

# or name the example, and the logger
dotnet run --project src/Quartz.Examples -- 1
dotnet run --project src/Quartz.Examples -- 5 --logger serilog

# what is on offer
dotnet run --project src/Quartz.Examples -- --list
```

Most examples run for under a minute and stop by themselves. **Ctrl+C** ends one early: it cancels the
example, which shuts its scheduler down properly on the way out.

Quartz logs through `Microsoft.Extensions.Logging`, and the tour offers three back-ends behind it —
`microsoft`, `serilog` and `nlog` — because wiring one up is the first thing a console application has
to do. `Logging.cs` is all three, in twenty lines.

## The examples

| # | Directory | What it shows |
|---|---|---|
| 1 | `01_SimpleJobScheduler` | Schedule one job for a given time, start the scheduler, watch it fire. |
| 2 | `02_SchedulingCapabilitiesUsingSimpleTriggers` | `ISimpleTrigger`: once, *n* times, forever; two triggers on one job; a durable job fired by hand; rescheduling under a live scheduler. |
| 3 | `03_SchedulingCapabilitiesUsingCronTriggers` | `ICronTrigger`, seven expressions and the first fire time each one resolves to. |
| 4 | `04_JobParametersAndJobsStateMaintenance` | Passing data in through `JobDataMap`, and `[PersistJobDataAfterExecution]` keeping it across firings — while a field on the job does not. |
| 5 | `05_SchedulingJobsSettingMisfireInstructions` | Two identical triggers whose jobs overrun, and the two different things their misfire instructions do about it. |
| 6 | `06_JobExecutionExceptions` | `JobExecutionException` with `RefireImmediately`, and with `UnscheduleAllTriggers`. |
| 7 | `07_InterruptingJobsInProgress` | `IScheduler.Interrupt`, the cancellation token it delivers, and a job moving across pool threads between awaits. |
| 8 | `08_ExcludeTimePeriodsUsingCalendars` | `AnnualCalendar` blocking whole days and `CronCalendar` blocking part of every minute, and the firings each suppresses. |
| 9 | `09_TriggeringAJobUsingJobListeners` | An `IJobListener` that schedules a second job when the first finishes, and the matcher that decides what it hears about. |
| 10 | `10_RunningLargeNumberOfJobs` | Five hundred jobs at once, and a fifty-thread pool working through them. |
| 11 | `11_RunJobsByPriorityWithTriggersPriority` | Three triggers due at the same instant, one worker thread, and priority deciding the order. |
| 12 | `12_ConfigureJobSchedulingByUsingXmlConfigurations` | Jobs and triggers read from `quartz_jobs.xml` by a plugin, rescanned while the scheduler runs — edit the file and watch the schedule follow. |
| 13 | `13_ClusteringJobsExecution` | Several instances sharing a database: load balancing, check-ins, and recovering the jobs of a node that died. Needs SQL Server. |

The numbers are positions in the tour and nothing more; they are the directory prefixes so that a
menu entry leads straight to the code behind it.

### Example 13 needs a database

It is the only one that does, and it has no default connection string — a connection string carries a
credential, and this repository keeps credentials out of its source. Set `QUARTZ_EXAMPLES_SQLSERVER`,
and with it unset the example says so and stops.

Any SQL Server with the Quartz schema in it will do. One way to get one:

```shell
docker run -d -p 1433:1433 -e ACCEPT_EULA=Y -e MSSQL_SA_PASSWORD='<a strong password>' \
  mcr.microsoft.com/mssql/server:2022-latest
```

then create a `quartznet` database and run `database/tables/tables_sqlserver.sql` against it — the
script opens with `USE [enter_db_name_here]`, so put the database's name there first. Then point the
example at it:

```shell
export QUARTZ_EXAMPLES_SQLSERVER='Server=localhost;Database=quartznet;User Id=sa;Password=<the password you chose>;TrustServerCertificate=true;'
```

Run the example in two terminals to see a cluster of two. Each node prints what it is running and what
it believes about the others; kill one and the survivor reports it `Failed` and re-runs the jobs it was
holding, marked `RECOVERING`.

## Adding an example

Add a directory numbered after the last one, with a class implementing `IExample`, and a line in
`ExampleCatalog.cs`. The catalog is written out by hand rather than discovered, so that the number on
the menu and the number on the directory cannot drift apart.

An example earns its place by being worth *watching*. Something better read than run belongs in
`Quartz.Documentation.Samples`, where the documentation will render it and the compiler will keep it
honest.
