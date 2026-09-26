using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// An application service a delegate job takes as a parameter.
/// </summary>
public interface ISessionStore
{
    ValueTask<int> PurgeExpired(CancellationToken cancellationToken = default);
}

/// <summary>
/// The category a delegate job logs under. A lambda has no type of its own to name.
/// </summary>
public sealed class SessionCleanup;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/delegate-jobs.md.
/// </summary>
public static class DelegateJobsSamples
{
    public static void ScheduleJob(IServiceCollection services)
    {
        #region sample_delegate_job

        services.AddQuartz(q =>
        {
            q.ScheduleJob(
                "session-cleanup",
                static async (ISessionStore sessions, ILogger<SessionCleanup> log, CancellationToken cancellationToken) =>
                {
                    int purged = await sessions.PurgeExpired(cancellationToken);
                    log.LogInformation("Purged {Count} expired sessions", purged);
                },
                trigger => trigger.WithCronSchedule("0 0 * * * ?"));
        });

        services.AddQuartzHostedService();

        #endregion
    }

    public static void AddJob(IServiceCollection services)
    {
        #region sample_delegate_job_add_job

        services.AddQuartz(q =>
        {
            // Durable, and fired only by a trigger of its own or by TriggerJob.
            q.AddJob("send-digest", static (IEmailSender email, CancellationToken cancellationToken) =>
                email.SendDigest(cancellationToken));

            q.AddTrigger(trigger => trigger
                .ForJob("send-digest")
                .WithCronSchedule("0 0 7 ? * MON-FRI"));
        });

        #endregion
    }

    public static async Task OneOff(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_delegate_job_one_off

        // A one-off firing of the named job, carrying data of its own.
        await scheduler.TriggerJob(
            new JobKey("send-digest"),
            new JobDataMap { ["recipient"] = "ada@example.com" },
            cancellationToken);

        #endregion
    }

    public static void ReadTheFiring(IServiceCollection services)
    {
        #region sample_delegate_job_context

        services.AddQuartz(q =>
        {
            q.AddJob("send-digest", static (IJobExecutionContext context, ILogger<SessionCleanup> log) =>
            {
                string? recipient = context.MergedJobDataMap.GetString("recipient");
                log.LogInformation("Digest for {Recipient}, fired by {Trigger}", recipient, context.Trigger.Key);
            });
        });

        #endregion
    }

    public static void Composition(IServiceCollection services)
    {
        #region sample_delegate_job_composition

        services.AddQuartz(q =>
        {
            // DelegateJob carries no [JobTimeout], so the scheduler-wide default bounds it.
            q.AddJobTimeout(TimeSpan.FromMinutes(5));

            q.AddJob(
                "reindex",
                static async (ISessionStore sessions, CancellationToken cancellationToken) =>
                {
                    await sessions.PurgeExpired(cancellationToken);
                },
                job => job
                    .WithIdentity("reindex", "maintenance")
                    .WithDescription("Rebuilds the session index")
                    // The attribute's builder form: every delegate job shares one type.
                    .DisallowConcurrentExecution());

            q.AddTrigger(trigger => trigger
                .ForJob("reindex", "maintenance")
                .WithCronSchedule("0 0/15 * * * ?")
                .WithRetryPolicy(RetryPolicy.Exponential(3, TimeSpan.FromSeconds(30))));
        });

        #endregion
    }
}
