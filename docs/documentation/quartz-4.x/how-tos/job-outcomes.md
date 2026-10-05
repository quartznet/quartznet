---

title: Job Outcomes
---

# Job Outcomes

A job can say what its run achieved: a result, a one-line summary and named metrics. The execution
history records them, and keeps a status per job that outlives its rows.

## Report a result

Set `IJobExecutionContext.Result` to a `JobRunReport`:

<!-- snippet: sample_job_outcome_report -->
```csharp
public sealed class ReleaseStaleReservationsJob : IJob
{
    public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        int scanned = await CountReservations(cancellationToken);
        int released = await ReleaseStale(cancellationToken);

        // A run that found nothing to do is a success, recorded as Skipped.
        context.Result = released == 0
            ? JobRunReport.Skipped("no stale reservations").With("scanned", scanned)
            : JobRunReport.Succeeded($"released {released}").With("scanned", scanned).With("released", released);
    }

    private static ValueTask<int> CountReservations(CancellationToken cancellationToken) => new(1200);

    private static ValueTask<int> ReleaseStale(CancellationToken cancellationToken) => new(0);
}
```
<!-- endSnippet -->

| `JobRunResult` | Means | A success |
|---|---|---|
| `Succeeded` (0) | The job did its work | Yes |
| `Failed` (1) | The job threw, or reported a failure | No |
| `Cancelled` (2) | The firing was interrupted and the job stopped | No |
| `Skipped` (3) | The job found nothing to do | Yes |

The history stores the integer. New members are appended; none is renumbered.

## Which result is recorded

The first rule that holds wins:

1. The firing was cancelled: `Cancelled`.
2. The job threw: `Failed`.
3. `context.Result` is an `IJobRunReport`: its `Result`.
4. Otherwise: `Succeeded`.

* The summary and metrics are recorded whichever rule wins.
* Any other `context.Result` is ignored. `NativeJob` keeps an exit code there.
* `ExecutionHistoryEntry.Succeeded` is `true` for `Succeeded` and `Skipped`.
* The `quartz.job.execution.duration` histogram and the `Quartz.Job.Execute` span carry the same result,
  lower-cased, as `quartz.job.result`, with or without the history. See
  [Metrics](../packages/opentelemetry-integration.md#metrics) and
  [Failed spans](../packages/opentelemetry-integration.md#failed-spans).

## Throw to make the scheduler act

A reported `Failed` is history only. To have the scheduler act on a failure, throw.

| | Throw | `JobRunReport.Failed(…)` |
|---|---|---|
| Recorded as `Failed` | Yes | Yes |
| Retried under the trigger's [retry policy](retrying-failed-jobs.md) | Yes | No |
| Runs `OnFailure` [continuations](job-continuations.md) | Yes | No |
| Raises `TriggerRetriesExhausted` | Yes | No |
| `context.Outcome` | `Failed` | `Succeeded` |

## Limits

| What | Limit | Over it |
|---|---|---|
| `Summary` | 1,000 characters, `JobRunReport.MaxSummaryLength` | Cut, never inside a surrogate pair |
| `Metrics` as JSON | 4,000 characters, `JobRunReport.MaxMetricsLength` | Dropped whole, with log event [`1059`](../log-events.md) |
| A metric value | Its text must not throw | Metrics dropped whole, with log event [`1060`](../log-events.md), naming the metric. The run is still recorded |

Metrics are written by value type, without reflection:

| Value | JSON |
|---|---|
| `string`, `bool`, `null` | As is |
| Integers, `decimal`, finite `double` and `float` | Number |
| `NaN`, `±Infinity` | String: `"NaN"`, `"Infinity"`, `"-Infinity"` |
| `DateTimeOffset`, `DateTime` | Round-trip string, `"O"` |
| `TimeSpan` | Constant string, `"c"` |
| `Guid` | String |
| Enum | Its name |
| Anything else | Its invariant-culture text |

* Every character outside ASCII is escaped, so 4,000 characters is also 4,000 bytes.
* `With(name, value)` returns a copy. A name already present takes the new value.

## What else the history records

| Where | What |
|---|---|
| `ExecutionHistoryEntry.Result` | The result. `null` on a row written before 4.4 |
| `ExecutionHistoryEntry.EffectiveResult` | `Result`, or `Succeeded`/`Failed` from `Succeeded` on an older row. Filters, tiers and statuses use it |
| `Summary`, `MetricsJson` | What the job reported |
| `ExceptionMessage` | The message of what the job threw, not the scheduler's wrapper. `null` if it did not throw |
| `Manual` | `true` for a run `IScheduler.TriggerJob` asked for |
| `FireInstanceId` | The firing's id, as on its span and log scope. Not unique across restarts |
| The misfire feed | A vetoed firing, with `MisfireReason.Vetoed`. `CountMisfires` does not count it |

`TriggerJob` marks its trigger with `SchedulerConstants.ManualTrigger` (`QRTZ_MANUAL_TRIGGER = "true"`) in
the trigger's `JobDataMap`. The key persists in every store and shows in `MergedJobDataMap`.

## Keep history by result

<!-- snippet: sample_job_outcome_retention -->
```csharp
builder.Services.AddQuartzExecutionHistory(options =>
{
    options.Retention = TimeSpan.FromDays(1);
    options.RetentionByResult[JobRunResult.Failed] = TimeSpan.FromDays(30);
    options.RetentionByResult[JobRunResult.Skipped] = TimeSpan.FromHours(1);
    options.MisfireRetention = TimeSpan.FromDays(7);

    // A job that runs every second keeps its latest 100 runs, and every failure.
    options.MaxEntriesPerJob = 100;
    options.MaxEntriesPerScheduler = 20_000;
});
```
<!-- endSnippet -->

| `ExecutionHistoryOptions` | Default | What |
|---|---|---|
| `Retention` | 24 hours | Age of every result without a tier |
| `RetentionByResult` | Empty | Age per `JobRunResult`, matched on `EffectiveResult` |
| `MisfireRetention` | `null`: `Retention` | Age of the misfire feed |
| `MaxEntriesPerJob` | `0`: no cap | Rows kept per job, earliest-fired out first. `Failed` rows are exempt |
| `MaxEntriesPerScheduler` | 2000 | The backstop, per feed, oldest out first whatever the result. `0` records nothing |

* Every age must be positive and `MaxEntriesPerJob` must not be negative, or the host fails at startup.
* `TimeSpan.MaxValue` keeps a result for good, within `MaxEntriesPerScheduler`.
* Both histories apply all five. The [database history](../tutorial/job-stores.md#execution-history-in-the-database)
  applies them by a sweep; its reads apply only the longest age.

## Read a job's status

<!-- snippet: sample_job_outcome_status -->
```csharp
JobRunStatus? status = await history.GetJobRunStatus(schedulerName, new JobKey("release-stale", "billing"));
if (status is { ConsecutiveFailures: > 0 })
{
    Console.WriteLine($"failing {status.ConsecutiveFailures}x since {status.LastSucceededAtUtc}: {status.LastFailureMessage}");
}

PagedResult<JobRunStatus> failing = await history.QueryJobRunStatuses(
    new JobRunStatusQuery { SchedulerName = schedulerName, Failing = true });
```
<!-- endSnippet -->

| `JobRunStatus` | What |
|---|---|
| `LastFiredAtUtc`, `LastResult`, `LastDuration`, `LastSummary`, `LastEntryId`, `LastSchedulerInstanceId` | The run that fired latest |
| `LastSucceededAtUtc` | The latest `Succeeded` or `Skipped` run |
| `LastFailedAtUtc`, `LastFailureMessage` | The latest `Failed` run, retried or not: its `ExceptionMessage`, else its summary |
| `ConsecutiveFailures` | Occurrences in a row that failed for good. A success resets it; `Cancelled` and a retried failure leave it |
| `RunCount`, `FailureCount` | Every run; occurrences that failed for good |
| `FirstFiredAtUtc` | The earliest run recorded |

* A status is folded from each row as it is recorded, so it outlives trimming.
* A run that completes after a later-fired run is counted. It does not change the `Last*` run fields or
  `ConsecutiveFailures`.
* `JobRunStatusQuery` pages by job group, then name. `Failing = true` lists `ConsecutiveFailures > 0`;
  `Jobs` names the jobs.

| Store | Statuses |
|---|---|
| In-memory history | At most `MaxEntriesPerScheduler` per scheduler. The job that ran longest ago goes first |
| [Database history](../tutorial/job-stores.md#execution-history-in-the-database) | One per job, in `QRTZ_JOB_STATUS`, committed with each row. Deleted once its job is gone and its last run is older than the longest age |
| The one [`AddQuartzHttpClient`](../packages/http-client.md) registers | The host's, when it is 4.4 or later and its store keeps them; otherwise `NotSupportedException` |
| An `IDashboardHistoryStore` of your own | `NotSupportedException` |

::: warning One job run many times at once
The database history writes each run's row and its job's status in one transaction. Runs of one job
that complete at the same moment take turns on that job's status row, each holding it through its
commit, before its trigger completes. Measured on PostgreSQL with one job, 500 one-off firings and 10
workers: 113–248 firings a second with the row alone, 77–189 with the row and the status. With the
history off, or with runs spread over many jobs, nothing waits. See
[The execution history's status row](../operations.md#the-execution-history-s-status-row).
:::

## Filter the history

<!-- snippet: sample_job_outcome_query -->
```csharp
PagedResult<ExecutionHistoryEntry> page = await history.QueryExecutions(new ExecutionHistoryQuery
{
    SchedulerName = schedulerName,
    Job = new JobKey("release-stale", "billing"),
    FiredFrom = since,
    Results = [JobRunResult.Failed, JobRunResult.Cancelled]
});

foreach (ExecutionHistoryEntry row in page.Items)
{
    // EffectiveResult answers for rows written before 4.4, which carry no Result.
    Console.WriteLine($"{row.FiredAtUtc:O} {row.EffectiveResult} {row.Summary} {row.MetricsJson}");
}
```
<!-- endSnippet -->

| `ExecutionHistoryQuery` | Matches |
|---|---|
| `Job` | One job key, exactly |
| `FiredFrom` | Fired at or after, inclusive |
| `FiredBefore` | Fired before, exclusive |
| `Results` | `EffectiveResult` in the set. An empty set matches nothing |

* `MisfireHistoryQuery.Job` narrows the misfire feed the same way, and `MisfireHistoryQuery.Reasons` to some
  `MisfireReason`s. An empty set matches nothing. A row a 4.2 node wrote has no reason and matches `Missed`.
* A cancelled run matches `FailedFinally = true`.

## See it in the dashboard and over HTTP

| Where | What |
|---|---|
| History page | The result, the summary, a chip per metric and a *Manual* badge on each row. Filters for results and for one job. See [Execution history and misfires](../packages/dashboard.md#execution-history-and-misfires) |
| Jobs page | *Last run*, *Last success* and *failing ×N* per job. See [Job run status](../packages/dashboard.md#job-run-status) |
| Job Detail page | A *Runs* panel. *View execution history* opens that job's rows only |
| Execution page | The result, the summary, a table of metrics, *Manual* and the fire instance id |
| HTTP API | The five members on each row, the four filters, and the `…/history/job-status` routes. See [Execution history](../packages/http-api.md#execution-history) |

```http
GET /quartz-api/schedulers/QuartzScheduler/history/executions?jobGroup=billing&jobName=release-stale&results=Failed,Cancelled
GET /quartz-api/schedulers/QuartzScheduler/history/job-status?failing=true
```

* A vetoed firing is listed only when `reasons` names `Vetoed`, so a 4.3 client can read the default listing.
  The dashboard and `AddQuartzHttpClient` ask for it.
* A scheduler in another process whose host is older than 4.4 has no statuses and cannot filter. The dashboard
  leaves the columns out and says so on the History page.

## Alert when a job stops succeeding

The Quartz [health check](../packages/hosted-services-integration.md#health-checks) reports a job that has not
succeeded within a window:

<!-- snippet: sample_job_outcome_health_check -->
```csharp
builder.Services.AddQuartzExecutionHistory();
builder.Services.AddHealthChecks().AddQuartz(options =>
{
    // Degraded once the nightly report has not succeeded for 26 hours.
    options.RequireSuccessWithin(new JobKey("nightly-report", "reports"), TimeSpan.FromHours(26));

    // Unhealthy, so the node leaves the rotation, once the ledger has not closed for 90 minutes.
    options.RequireSuccessWithin(new JobKey("ledger-close", "billing"), TimeSpan.FromMinutes(90), HealthStatus.Unhealthy);
});
```
<!-- endSnippet -->

| `RequiredJobOptions` | Default | What |
|---|---|---|
| `Name`, `Group` | `Group`: `DEFAULT` | The job |
| `SucceededWithin` | None; must be positive | How long ago its last success may have fired |
| `Status` | `Degraded` | What the check reports while the job is late. `Unhealthy` for a job that must never be late. `Healthy` is refused |

* The window runs from `JobRunStatus.LastSucceededAtUtc`, on the scheduler's clock. A `Skipped` run is a success.
* A job with no recorded success is judged from the first time the check evaluated it. A process that has just
  started gives each job one window.
* The check reports the worst of the scheduler's own verdict and each late job's `Status`. It does not read
  the jobs of a scheduler that is already *unhealthy*. It does read them in standby.
* The message names the gravest late job and counts the others. The data has one entry per late job, keyed
  `<group>.<name>`: `lastSucceededAtUtc` (or `never`), `consecutiveFailures` and `succeededWithin`.
* Every check reads all the jobs' statuses in one call.
* A job given twice keeps the later entry. `RequireSuccessWithin` replaces the earlier one.

The statuses come from the execution history, which must keep them: see the table under
[Read a job's status](#read-a-job-s-status).

| The scheduler's history | Result |
|---|---|
| None | The host fails at startup, naming `AddQuartzExecutionHistory()` and `UsePersistentStore(store => store.UseExecutionHistory())` |
| Its status read throws `NotSupportedException` | The host fails at startup, with the store's reason |
| A remote host older than 4.4, through `AddQuartzHttpClient` | The check reports *unhealthy*, with the host's reason. Startup cannot tell |

`RequiredJobs` binds from configuration. A `TimeSpan` is `d.hh:mm:ss`, so 26 hours is `1.02:00:00`:

```json
{
  "HealthChecks": {
    "Quartz": {
      "RequiredJobs": [
        { "Name": "nightly-report", "Group": "reports", "SucceededWithin": "1.02:00:00" },
        { "Name": "ledger-close", "Group": "billing", "SucceededWithin": "01:30:00", "Status": "Unhealthy" }
      ]
    }
  }
}
```

Bind it with `services.Configure<QuartzHealthCheckOptions>(configuration.GetSection("HealthChecks:Quartz"))`,
or under the scheduler's name for a named scheduler.

## A history store of your own

* Keep `Result`, `Summary`, `MetricsJson`, `Manual` and `FireInstanceId` on the rows you store. The
  recorder sets them.
* Apply the four filters above, `MisfireHistoryQuery.Job` and `MisfireHistoryQuery.Reasons`.
* Count only `MisfireReason.Missed` rows in `CountMisfires`.
* `QueryJobRunStatuses` and `GetJobRunStatus` are default interface members. The first throws
  `NotSupportedException`; the second asks the first for one job. Implement `QueryJobRunStatuses` to keep
  a status beside the rows, updated as each row is recorded.
