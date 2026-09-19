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

    public static void AJitteredRetryPolicy(IHostApplicationBuilder builder)
    {
        #region sample_retry_jitter

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<ImportJob>(j => j.WithIdentity("import", "nightly"));
            q.AddTrigger<ImportJob>(t => t
                .ForJob("import", "nightly")
                .WithCronSchedule("0 0 2 * * ?")
                // The same backoff as above, spread by a fifth either way: the first retry lands
                // between 24 and 36 seconds after the failure, the second between 48 and 72, and so
                // on. A hundred triggers that failed on the same outage come back at a hundred
                // different instants instead of all at once.
                .WithRetryPolicy(RetryPolicy.Exponential(
                    maxAttempts: 5,
                    initialDelay: TimeSpan.FromSeconds(30),
                    factor: 2,
                    maxDelay: TimeSpan.FromMinutes(10),
                    jitter: 0.2)));
        });

        #endregion
    }

    #region sample_retry_listener_gave_up

    /// <summary>
    /// Raises an alert when an occurrence has run out of retries, and says nothing while it is still
    /// trying.
    /// </summary>
    public sealed class GaveUpListener : ITriggerListener
    {
        private readonly ILogger<GaveUpListener> logger;

        public GaveUpListener(ILogger<GaveUpListener> logger)
        {
            this.logger = logger;
        }

        public ValueTask TriggerRetriesExhausted(
            ITrigger trigger,
            IJobExecutionContext context,
            JobExecutionException exception,
            CancellationToken cancellationToken = default)
        {
            // context.RetryAttempt is how many retries this occurrence spent before giving up, and
            // context.RetryScheduled is false: there is no further attempt coming.
            logger.LogError(
                exception,
                "{Job} gave up after {Attempts} retries; the occurrence scheduled for {Scheduled} never succeeded",
                context.JobDetail.Key,
                context.RetryAttempt,
                context.ScheduledFireTimeUtc);

            return default;
        }
    }

    #endregion

    public static void RegisteringTheListener(IHostApplicationBuilder builder)
    {
        #region sample_retry_listener_registration

        builder.Services.AddQuartz(q =>
        {
            q.AddTriggerListener<GaveUpListener>(Matchers.AllTriggers());
        });

        #endregion
    }

    #region sample_retry_reading_the_outcome

    /// <summary>
    /// A job listener that tells an attempt from a verdict, which before 4.2 only a trigger listener
    /// could do — and only by comparing an instruction against <c>RetryTrigger</c>.
    /// </summary>
    public sealed class OutcomeReadingListener : IJobListener
    {
        private readonly ILogger<OutcomeReadingListener> logger;

        public OutcomeReadingListener(ILogger<OutcomeReadingListener> logger)
        {
            this.logger = logger;
        }

        public ValueTask JobWasExecuted(
            IJobExecutionContext context,
            JobExecutionException? jobException,
            CancellationToken cancellationToken = default)
        {
            if (context.Outcome == ExecutionOutcome.Failed && !context.RetryScheduled)
            {
                logger.LogError("{Job} failed for the last time", context.JobDetail.Key);
            }

            return default;
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
