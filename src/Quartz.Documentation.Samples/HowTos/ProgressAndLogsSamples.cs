using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.Extensibility;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/progress-and-execution-logs.md.
/// </summary>
public sealed class ProgressAndLogsSamples
{
    public static void CapturingLogLines(IHostApplicationBuilder builder)
    {
        #region sample_execution_log_capture

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

        #endregion
    }

    #region sample_execution_log_job

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

    #endregion

    public static async Task ReadingALog(IExecutionHistoryStore history, string schedulerName)
    {
        #region sample_execution_log_reading

        PagedResult<ExecutionHistoryEntry> page = await history.QueryExecutions(
            new ExecutionHistoryQuery { SchedulerName = schedulerName, Take = 10 });

        foreach (ExecutionHistoryEntry row in page.Items)
        {
            // The listing may leave the log out; the single read always carries it.
            ExecutionHistoryEntry? execution = await history.GetExecution(schedulerName, row.EntryId!);
            Console.WriteLine(execution?.Log ?? "(nothing captured)");
        }

        #endregion
    }

    #region sample_progress_reporting_job

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

    #endregion

    #region sample_progress_listener

    public sealed class ProgressFeed : IJobListener
    {
        private readonly ILogger<ProgressFeed> logger;

        public ProgressFeed(ILogger<ProgressFeed> logger)
        {
            this.logger = logger;
        }

        // Off the job's thread, at most once a second per firing, and only on a change.
        public ValueTask JobProgressChanged(IJobExecutionContext context, FireInstanceProgress progress, CancellationToken cancellationToken = default)
        {
            logger.LogInformation("{JobKey} ({FireInstanceId}): {Percent}% {Message}",
                context.JobDetail.Key, context.FireInstanceId, progress.Percent, progress.Message);
            return default;
        }
    }

    #endregion

    public static void RegisteringAProgressListener(IHostApplicationBuilder builder)
    {
        #region sample_progress_listener_registration

        builder.Services.AddQuartz(q =>
        {
            // Hears the export jobs only, as its matcher says.
            q.AddJobListener<ProgressFeed>(GroupMatcher<JobKey>.GroupEquals("exports"));
        });

        #endregion
    }

    public static async Task ReadingProgress(IScheduler scheduler)
    {
        #region sample_progress_reading

        PagedResult<FireInstance> running = await scheduler.QueryFireInstances(new FireInstanceQuery());

        foreach (FireInstance firing in running.Items)
        {
            // Null until the job reports; cluster-wide with a persistent store.
            Console.WriteLine($"{firing.JobKey}: {firing.Progress}% {firing.ProgressMessage}");
        }

        #endregion
    }
}
