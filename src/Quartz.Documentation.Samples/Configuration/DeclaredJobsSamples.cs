using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Documentation.Samples.Configuration;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/tutorial/declaring-jobs-with-attributes.md.
/// </summary>
/// <remarks>
/// This project names <c>Quartz.Analyzers</c> as an analyzer, so the generator runs over these
/// declarations exactly as it runs over an application's: the <c>AddDeclaredJobs</c> the second
/// sample calls is generated from the first, and a cron expression that stopped parsing would fail
/// the build.
/// </remarks>
public static class DeclaredJobsSamples
{
    public static void AddDeclaredJobs(IServiceCollection services)
    {
        #region sample_add_declared_jobs

        services.AddQuartz(q =>
        {
            // Every job in this assembly that carries [QuartzJob], with the schedules it declares.
            q.AddDeclaredJobs();

            // Anything an attribute cannot say is still written here, beside it.
            q.AddTrigger<CleanupJob>(trigger => trigger
                .WithIdentity("cleanup-on-start")
                .ForJob("cleanup", "maintenance")
                .StartNow());
        });

        services.AddQuartzHostedService();

        #endregion
    }
}

#region sample_declared_job

[QuartzJob(Name = "cleanup", Group = "maintenance", Description = "removes rows nobody reads")]
[CronTrigger("0 0 0/6 * * ?")]
[CronTrigger("0 0 12 ? * MON-FRI", Name = "cleanup-weekday-noon", TimeZone = "Europe/Helsinki")]
public sealed class CleanupJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}

#endregion

#region sample_declared_job_configuration_key

// Jobs:Report:Cron in appsettings.json, or Jobs__Report__Cron in the environment, replaces the
// expression. Without it, the report runs at 06:00.
[QuartzJob(Name = "report")]
[CronTrigger("0 0 6 * * ?", ConfigurationKey = "Jobs:Report:Cron")]
public sealed class DailyReportJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}

#endregion

#region sample_declared_interval_job

// Fires as the scheduler starts, then every ten minutes. Jobs:Inbox:Interval, when it is set, replaces
// the ten minutes; the warm-up polls four times, five seconds apart, and stops.
[QuartzJob(Name = "poll-inbox")]
[SimpleTrigger("00:10:00", ConfigurationKey = "Jobs:Inbox:Interval")]
[SimpleTrigger("00:00:05", Name = "poll-inbox-warm-up", RepeatCount = 3)]
public sealed class PollInboxJob : IJob
{
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
    {
        return default;
    }
}

#endregion
