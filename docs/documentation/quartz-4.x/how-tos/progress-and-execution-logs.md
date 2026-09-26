---

title: Progress and Execution Logs
---

# Progress and Execution Logs

A running job can say how far it has got. The value shows in the fire-instance listing and on the
dashboard's Currently Executing page, on every node of a cluster.

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

## A job store of your own

`IJobStore.UpdateFireInstanceProgress(fireInstanceId, progress)` is a default interface member that
records nothing, so a store written for 4.2 compiles and its firings report no progress. Implement it
to keep the value beside the firing, and return it on `FireInstance.Progress` and `ProgressMessage`.
A fire instance that has completed is not an error: update nothing.
