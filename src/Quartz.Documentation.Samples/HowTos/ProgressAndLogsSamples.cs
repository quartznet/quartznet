namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/progress-and-execution-logs.md.
/// </summary>
public sealed class ProgressAndLogsSamples
{
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
