using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/retrying-failed-jobs.md.
/// </summary>
public sealed class RetryingFailedJobsSamples
{
    public static void AFixedRetryPolicy(IHostApplicationBuilder builder)
    {
        #region sample_retry_fixed

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
            q.AddTrigger<ImportJob>(t => t
                .ForJob("import", "nightly")
                .WithCronSchedule("0 0 2 * * ?")
                // Three retries, five minutes apart, after a failure.
                .WithRetryPolicy(RetryPolicy.Fixed(3, TimeSpan.FromMinutes(5))));
        });

        #endregion
    }

    public static void AnExponentialRetryPolicy(IHostApplicationBuilder builder)
    {
        #region sample_retry_exponential

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
            q.AddTrigger<ImportJob>(t => t
                .ForJob("import", "nightly")
                .WithCronSchedule("0 0 2 * * ?")
                // 30s, 1m, 2m, 4m, 8m — but never longer than ten minutes.
                .WithRetryPolicy(RetryPolicy.Exponential(
                    maxAttempts: 5,
                    initialDelay: TimeSpan.FromSeconds(30),
                    factor: 2,
                    maxDelay: TimeSpan.FromMinutes(10))));
        });

        #endregion
    }

    public static void AnExplicitTableOfWaits(IHostApplicationBuilder builder)
    {
        #region sample_retry_explicit

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
            q.AddTrigger<ImportJob>(t => t
                .ForJob("import", "nightly")
                .WithCronSchedule("0 0 2 * * ?")
                // Try again quickly twice, then give the upstream system an hour.
                .WithRetryPolicy(RetryPolicy.Explicit(
                    TimeSpan.FromSeconds(10),
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromHours(1))));
        });

        #endregion
    }

    #region sample_retry_reading_the_attempt

    public sealed class RetryAwareImportJob : IJob
    {
        private readonly IImportService importer;
        private readonly ILogger<RetryAwareImportJob> logger;

        public RetryAwareImportJob(IImportService importer, ILogger<RetryAwareImportJob> logger)
        {
            this.importer = importer;
            this.logger = logger;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            if (context.RetryAttempt > 0)
            {
                logger.LogWarning(
                    "Import retry {Attempt} for the occurrence scheduled at {Scheduled}",
                    context.RetryAttempt,
                    context.ScheduledFireTimeUtc);
            }

            // Throwing anything is what asks for a retry. There is nothing to opt into.
            await importer.Run(cancellationToken);
        }
    }

    #endregion

    #region sample_retry_not_worth_retrying

    public sealed class SelectiveImportJob : IJob
    {
        private readonly IImportService importer;
        private readonly ILogger<SelectiveImportJob> logger;

        public SelectiveImportJob(IImportService importer, ILogger<SelectiveImportJob> logger)
        {
            this.importer = importer;
            this.logger = logger;
        }

        public async ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            try
            {
                await importer.Run(cancellationToken);
            }
            catch (TransientImportException)
            {
                // Let it out. Throwing is what asks for a retry, so the trigger's policy takes over.
                throw;
            }
            catch (InvalidOperationException e)
            {
                // A failure no amount of retrying can fix - bad input, not a flaky dependency. Report
                // it and return: the occurrence is over, and the trigger goes back to its ordinary
                // schedule instead of spending its attempts on a certainty.
                logger.LogError(e, "Import cannot succeed for this occurrence and will not be retried");
            }
        }
    }

    #endregion

    public static async ValueTask ChangingThePolicyOfAStoredTrigger(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_retry_update_stored_trigger

        await scheduler.UpdateTriggerDetails(
            new TriggerKey("nightly", "imports"),
            new TriggerDetailsUpdate().WithRetryPolicy(RetryPolicy.Fixed(5, TimeSpan.FromMinutes(2))),
            cancellationToken);

        #endregion
    }

    public static async ValueTask StoppingATriggerFromRetrying(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_retry_clear_stored_trigger

        await scheduler.UpdateTriggerDetails(
            new TriggerKey("nightly", "imports"),
            new TriggerDetailsUpdate().WithRetryPolicy(null),
            cancellationToken);

        #endregion
    }

    public sealed class ImportJob : IJob
    {
        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
    }
}
