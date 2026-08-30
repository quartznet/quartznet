using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/external-leader.md.
/// </summary>
public static class ExternalLeaderSamples
{
    public static void Registration(IHostApplicationBuilder builder, string connectionString)
    {
        #region sample_external_leader_registration

        builder.Services.AddQuartz(q =>
        {
            q.UsePersistentStore(store => store.UseSqlServer(connectionString));

            // No UseClustering(). Exactly one process is meant to be scheduling, and the election
            // outside Quartz is what says which one.
        });

        builder.Services.AddQuartzHostedService(options =>
        {
            // Built, initialized and bound with the host - and then left alone. The leader starts it.
            options.AutoStart = false;

            // A leader that is stepping down because the host is stopping should finish what it began.
            options.WaitForJobsToComplete = true;
        });

        builder.Services.AddHealthChecks().AddQuartz();

        #endregion
    }

    #region sample_external_leader_callbacks

    /// <summary>
    /// The two callbacks every leader election has, whatever it calls them.
    /// </summary>
    public sealed class SchedulerLeadership(IScheduler scheduler)
    {
        public ValueTask OnStartedLeading(CancellationToken cancellationToken)
        {
            // The first acquisition starts the scheduler and every later one resumes it from standby.
            // Start does both, and does nothing when the scheduler is already running.
            return scheduler.Start(cancellationToken);
        }

        public ValueTask OnStoppedLeading(CancellationToken cancellationToken)
        {
            // Standby, not Shutdown: a shut-down scheduler cannot be started again, and this process
            // may well be elected once more in a minute. Losing the lease while the host is already
            // stopping is ordinary, and Standby throws once the scheduler has shut down.
            return scheduler.Status == SchedulerStatus.Running
                ? scheduler.Standby(cancellationToken)
                : default;
        }
    }

    #endregion

    public static void Tuning(IHostApplicationBuilder builder)
    {
        #region sample_external_leader_tuning

        builder.Services.AddQuartz(q =>
        {
            q.ConfigureScheduler(options =>
            {
                // How long a trigger written by another process may sit before this one looks again.
                options.IdleWaitTime = TimeSpan.FromSeconds(5);

                // Both halves or neither: a batch stops at the first trigger that is not due within
                // the window of the one that opened it.
                options.MaxBatchSize = 10;
                options.BatchTriggerAcquisitionFireAheadTimeWindow = TimeSpan.FromSeconds(2);
            });

            // MaxBatchSize may not exceed this: triggers acquired beyond the number of threads there
            // are to run them on are held by this node, unfireable by any other, until the pool drains.
            q.UseDefaultThreadPool(maxConcurrency: 10);
        });

        #endregion
    }

    public static void RequestingRecovery(IHostApplicationBuilder builder)
    {
        #region sample_external_leader_request_recovery

        builder.Services.AddQuartz(q =>
        {
            q.AddJob<ReportingJob>(j => j
                .WithIdentity("nightly-close", "reporting")
                .RequestRecovery()
                .StoreDurably());
        });

        #endregion
    }
}
