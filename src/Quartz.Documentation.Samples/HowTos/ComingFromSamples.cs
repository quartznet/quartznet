using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.HowTos;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/how-tos/coming-from-hangfire.md and
/// docs/documentation/quartz-4.x/how-tos/coming-from-tickerq.md.
/// </summary>
/// <remarks>
/// Both pages show the other library's call in a plain fence beside the Quartz one, because this
/// project references neither. Only the Quartz half is compiled.
/// </remarks>
public static class ComingFromSamples
{
    public static async ValueTask EnqueueAndSchedule(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_coming_from_hangfire_enqueue

        // BackgroundJob.Enqueue(() => mailer.SendWelcome("ada@example.com"))
        await scheduler.ScheduleJob<SendWelcomeEmailJob, string>(
            "ada@example.com",
            TimeSpan.Zero,
            cancellationToken: cancellationToken);

        // BackgroundJob.Schedule(() => mailer.SendWelcome("ada@example.com"), TimeSpan.FromDays(1))
        ScheduledOneOffJob tomorrow = await scheduler.ScheduleJob<SendWelcomeEmailJob, string>(
            "ada@example.com",
            TimeSpan.FromDays(1),
            cancellationToken: cancellationToken);

        // BackgroundJob.Delete(jobId) — the TriggerKey is the handle, and it is the trigger that goes
        await scheduler.UnscheduleJob(tomorrow.TriggerKey, cancellationToken);

        #endregion
    }

    public static void Recurring(IServiceCollection services)
    {
        #region sample_coming_from_hangfire_recurring

        // RecurringJob.AddOrUpdate("nightly-import", () => importer.Run(), "0 2 * * *")
        services.AddQuartz(q =>
        {
            q.AddJob<NightlyImportJob>(j => j.WithIdentity("nightly-import"));
            q.AddTrigger<NightlyImportJob>(t => t
                .ForJob("nightly-import")
                // Six fields, and one of the two day fields must be '?'. A Hangfire expression is
                // five fields read as minutes upward, so prepend the seconds field.
                .WithCronSchedule("0 0 2 * * ?", x => x
                    // Hangfire reads a cron in UTC unless RecurringJobOptions says otherwise; a
                    // Quartz cron trigger reads it in the machine's local zone unless you say
                    // otherwise. Say otherwise.
                    .InTimeZone(TimeZoneInfo.Utc)));
        });

        #endregion
    }

    public static void Retry(IServiceCollection services)
    {
        #region sample_coming_from_hangfire_retry

        // [AutomaticRetry(Attempts = 5, DelaysInSeconds = new[] { 60, 300, 900 })]
        services.AddQuartz(q =>
        {
            q.AddJob<NightlyImportJob>(j => j.WithIdentity("nightly-import"));
            q.AddTrigger<NightlyImportJob>(t => t
                .ForJob("nightly-import")
                .WithCronSchedule("0 0 2 * * ?")
                // The policy is on the trigger, not on the job type, and nothing is retried
                // without one.
                .WithRetryPolicy(RetryPolicy.Explicit(
                    TimeSpan.FromMinutes(1),
                    TimeSpan.FromMinutes(5),
                    TimeSpan.FromMinutes(15))));
        });

        #endregion
    }

    public static void Queues(IServiceCollection services)
    {
        #region sample_coming_from_hangfire_queues

        // [Queue("reports")] on the job, plus a server that subscribes to that queue
        services.AddQuartz(q =>
        {
            q.AddJob<ReportingJob>(j => j.WithIdentity("monthly-report"));
            q.AddTrigger<ReportingJob>(t => t
                .ForJob("monthly-report")
                .WithCronSchedule("0 0 3 1 * ?")
                .WithExecutionGroup("reports"));

            q.UseExecutionLimits(limits =>
            {
                // Two at a time across every node sharing the store, rather than two per process.
                limits.ForGroup("reports", maxConcurrent: 2, ExecutionLimitScope.Cluster);
            });
        });

        #endregion
    }

    public static void Dashboard(WebApplication app)
    {
        #region sample_coming_from_hangfire_dashboard

        // app.UseHangfireDashboard("/hangfire", new DashboardOptions { Authorization = [ ... ] })
        app.MapQuartzDashboard().RequireAuthorization("QuartzOperators");

        #endregion
    }

    public static void TypedJobRegistration(IServiceCollection services)
    {
        #region sample_coming_from_tickerq_job

        // [TickerFunction("cleanup", "0 */6 * * *")] on a method
        services.AddQuartz(q =>
        {
            q.AddJob<CleanupJob>(j => j.WithIdentity("cleanup"));
            q.AddTrigger<CleanupJob>(t => t
                .ForJob("cleanup")
                // NCrontab takes '*' in both day fields; Quartz wants '?' in one of them, and it
                // reads six fields with seconds first.
                .WithCronSchedule("0 0 0/6 * * ?"));
        });

        #endregion
    }

    public static async ValueTask OneOff(IScheduler scheduler, CancellationToken cancellationToken)
    {
        #region sample_coming_from_tickerq_one_off

        // await timeTicker.AddAsync(new TimeTickerEntity { Function = "send-welcome", ... })
        await scheduler.ScheduleJob<SendWelcomeEmailJob, string>(
            "ada@example.com",
            TimeSpan.FromSeconds(30),
            cancellationToken: cancellationToken);

        #endregion
    }

    public static void RetryIntervals(IServiceCollection services)
    {
        #region sample_coming_from_tickerq_retry

        // Retries = 3, RetryIntervals = [30, 120, 600]
        services.AddQuartz(q =>
        {
            q.AddJob<CleanupJob>(j => j.WithIdentity("cleanup"));
            q.AddTrigger<CleanupJob>(t => t
                .ForJob("cleanup")
                .WithCronSchedule("0 0 0/6 * * ?")
                .WithRetryPolicy(RetryPolicy.Explicit(
                    TimeSpan.FromSeconds(30),
                    TimeSpan.FromMinutes(2),
                    TimeSpan.FromMinutes(10))));
        });

        #endregion
    }

    public static void Concurrency(IServiceCollection services)
    {
        #region sample_coming_from_tickerq_concurrency

        services.AddQuartz(q =>
        {
            // s.MaxConcurrency = 16 — how much this process runs at once
            q.UseDefaultThreadPool(maxConcurrency: 16);

            // maxConcurrency on [TickerFunction] — how much of one category runs at once, except
            // that the scope is yours to choose
            q.UseExecutionLimits(limits =>
            {
                limits.ForGroup("cleanup", maxConcurrent: 1, ExecutionLimitScope.Cluster);
            });
        });

        #endregion
    }
}

#region sample_coming_from_hangfire_job

// A Hangfire job is a method named by an expression tree; a Quartz job is a type. Its payload is a
// constructor-injected service, a JobDataMap entry, or — as here — the typed input of IJob<TInput>.
public sealed class SendWelcomeEmailJob : IJob<string>
{
    private readonly IMailer mailer;

    public SendWelcomeEmailJob(IMailer mailer) => this.mailer = mailer;

    public ValueTask Execute(IJobExecutionContext context, string emailAddress, CancellationToken cancellationToken = default)
    {
        return mailer.SendWelcome(emailAddress, cancellationToken);
    }
}

#endregion

/// <summary>Stands in for whatever sends the mail in the coming-from samples.</summary>
public interface IMailer
{
    ValueTask SendWelcome(string emailAddress, CancellationToken cancellationToken = default);
}

public sealed class NightlyImportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}

public sealed class CleanupJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default) => default;
}
