---

title: Progress and Execution Logs
---

# Progress and Execution Logs

A running job can say how far it has got, and what it logs while it runs can be kept with its history
row. Progress shows on the dashboard's Currently Executing page, on every node of a cluster; the log
shows on the execution's own page, reached from Execution History.

## Report progress

Call `IJobExecutionContext.ReportProgress(percent, message)` as often as is convenient:

<!-- snippet: sample_progress_reporting_job -->
```csharp
public sealed class ExportJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        const int pages = 40;

        for (int page = 1; page <= pages; page++)
        {
            await ExportPage(page, cancellationToken);

            // Returns at once. The scheduler writes at most once a second, and only a change.
            context.ReportProgress(page * 100 / pages, $"page {page} of {pages}");
        }
    }

    private static ValueTask ExportPage(int page, CancellationToken cancellationToken) => default;
}
```
<!-- endSnippet -->

| Rule | Value |
|---|---|
| `percent` | `0` to `100`; anything else throws `ArgumentOutOfRangeException` |
| `message` | Optional; cut to 250 characters (`FireInstanceProgress.MaxMessageLength`) |
| Store writes | At most one per second per firing, only when the value changed |
| The last report | Always written, however quickly the reports came |
| The job's thread | Never waits: the write is queued off the job's flow and enlists in nothing |
| A failed write | Logged as event [`1058`](../log-events.md), and the job carries on |
| Lifetime | The firing's: gone when the job completes; a retry starts with none |

## Read it

`FireInstance.Progress` and `FireInstance.ProgressMessage`, from `QueryFireInstances`:

<!-- snippet: sample_progress_reading -->
```csharp
PagedResult<FireInstance> running = await scheduler.QueryFireInstances(new FireInstanceQuery());

foreach (FireInstance firing in running.Items)
{
    // Null until the job reports; cluster-wide with a persistent store.
    Console.WriteLine($"{firing.JobKey}: {firing.Progress}% {firing.ProgressMessage}");
}
```
<!-- endSnippet -->

* Both are `null` until the job reports, and on a firing that is only `Acquired`.
* The persistent store keeps them in `QRTZ_FIRED_TRIGGERS.PROGRESS` and `PROGRESS_MESSAGE`, so every
  node, and a [store-attached dashboard](../packages/dashboard.md#store-attached-targets), reads them.
  Run [`4.3/add_fire_progress_<db>.sql`](../../database/schema-changes.md#version-4-3) first.
* Over HTTP, `GET …/jobs/fire-instances` carries `progress` and `progressMessage`.
* The dashboard draws a bar on [Currently Executing](../packages/dashboard.md#currently-executing).

## Keep a job's log lines

`UseExecutionLogCapture()` keeps what each of the scheduler's jobs logs while it runs, and records it on
the execution's history row:

<!-- snippet: sample_execution_log_capture -->
```csharp
builder.Services.AddQuartz(q =>
{
    // First, so what later middleware logs is kept too. The bounds are the defaults.
    q.UseExecutionLogCapture(options =>
    {
        options.MaxLines = 200;
        options.MaxBytes = 16 * 1024;
    });

    q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
});
```
<!-- endSnippet -->

The job logs as it always did:

<!-- snippet: sample_execution_log_job -->
```csharp
public sealed class ImportJob : IJob
{
    private readonly ILogger<ImportJob> logger;

    public ImportJob(ILogger<ImportJob> logger)
    {
        this.logger = logger;
    }

    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        // An ordinary ILogger: the line goes wherever the host sends logs, and onto this firing's
        // history row as well.
        logger.LogInformation("Importing {File}", context.MergedJobDataMap.GetString("file"));
        return default;
    }
}
```
<!-- endSnippet -->

| Rule | Value |
|---|---|
| What is kept | Lines logged through the container's `ILoggerFactory` on the firing's flow |
| When | From the job's start to the firing's end, its unhandled exception included |
| What is not kept | Lines logged outside a firing, or in another firing |
| Bounds | `MaxLines` (200) and `MaxBytes` (16 KB, UTF-8) per execution; the oldest line goes first |
| When a line was dropped | The log opens with `[n earlier log entries dropped: …]` |
| Line format | `2026-09-26T12:00:00.123Z info Category: message`, UTC, then the exception if any |
| Filtering | As any logging provider; its alias is `QuartzExecutionLog` |
| Other schedulers | Unaffected: capture is the calling scheduler's choice |
| Cost without it | None: no provider is registered, so no log call reaches it |
| Invalid bounds | `MaxLines` < 1 or `MaxBytes` < 256: `SchedulerConfigException` at build |

## Read a log

`ExecutionHistoryEntry.Log`, from `IExecutionHistoryStore.GetExecution(schedulerName, entryId)`:

<!-- snippet: sample_execution_log_reading -->
```csharp
PagedResult<ExecutionHistoryEntry> page = await history.QueryExecutions(
    new ExecutionHistoryQuery { SchedulerName = schedulerName, Take = 10 });

foreach (ExecutionHistoryEntry row in page.Items)
{
    // The listing may leave the log out; the single read always carries it.
    ExecutionHistoryEntry? execution = await history.GetExecution(schedulerName, row.EntryId!);
    Console.WriteLine(execution?.Log ?? "(nothing captured)");
}
```
<!-- endSnippet -->

| Where | Carries the log |
|---|---|
| `GetExecution` | Always |
| `QueryExecutions`, in-memory history | Yes |
| `QueryExecutions`, [database history](../tutorial/job-stores.md#execution-history-in-the-database) | No: `EXECUTION_LOG` is not in the listing's `SELECT` |
| `GET …/history/executions/{entryId}` | Yes, as `log` |
| `GET …/history/executions` | No; each row carries `entryId` |
| Dashboard | [The execution's page](../packages/dashboard.md#execution-history), from a History row's fire time |

* The database history needs [`4.3/add_execution_log_<db>.sql`](../../database/schema-changes.md#version-4-3);
  a store that keeps its history there refuses to start without it.
* The in-memory history keeps each log in memory, so its size is bounded by
  `MaxEntriesPerScheduler` × `MaxBytes`.

## A job store of your own

`IJobStore.UpdateFireInstanceProgress(fireInstanceId, progress)` is a default interface member that
records nothing, so a store written for 4.2 compiles and its firings report no progress. Implement it
to keep the value beside the firing, and return it on `FireInstance.Progress` and `ProgressMessage`.
A fire instance that has completed is not an error: update nothing.

## A history store of your own

`IExecutionHistoryStore.GetExecution` is a default interface member. It reads the scheduler's whole
history through `QueryExecutions` and picks the row out by `EntryId`, so a store written for 4.2
answers it. Override it with a read by key. Keep `EntryId` and `Log` on the rows you store; the
recorder sets both.
