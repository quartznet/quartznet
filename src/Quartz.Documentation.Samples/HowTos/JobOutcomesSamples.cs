using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using Quartz.Extensibility;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/job-outcomes.md.
/// </summary>
public sealed class JobOutcomesSamples
{
    #region sample_job_outcome_report

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

    #endregion

    public static void KeepingHistoryByResult(IHostApplicationBuilder builder)
    {
        #region sample_job_outcome_retention

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

        #endregion
    }

    public static async Task ReadingAStatus(IExecutionHistoryStore history, string schedulerName)
    {
        #region sample_job_outcome_status

        JobRunStatus? status = await history.GetJobRunStatus(schedulerName, new JobKey("release-stale", "billing"));
        if (status is { ConsecutiveFailures: > 0 })
        {
            Console.WriteLine($"failing {status.ConsecutiveFailures}x since {status.LastSucceededAtUtc}: {status.LastFailureMessage}");
        }

        PagedResult<JobRunStatus> failing = await history.QueryJobRunStatuses(
            new JobRunStatusQuery { SchedulerName = schedulerName, Failing = true });

        #endregion

        _ = failing;
    }

    public static void AlertingWhenAJobStopsSucceeding(IHostApplicationBuilder builder)
    {
        #region sample_job_outcome_health_check

        builder.Services.AddQuartzExecutionHistory();
        builder.Services.AddHealthChecks().AddQuartz(options =>
        {
            // Degraded once the nightly report has not succeeded for 26 hours.
            options.RequireSuccessWithin(new JobKey("nightly-report", "reports"), TimeSpan.FromHours(26));

            // Unhealthy, so the node leaves the rotation, once the ledger has not closed for 90 minutes.
            options.RequireSuccessWithin(new JobKey("ledger-close", "billing"), TimeSpan.FromMinutes(90), HealthStatus.Unhealthy);
        });

        #endregion
    }

    public static async Task FilteringTheHistory(IExecutionHistoryStore history, string schedulerName, DateTimeOffset since)
    {
        #region sample_job_outcome_query

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

        #endregion
    }
}
