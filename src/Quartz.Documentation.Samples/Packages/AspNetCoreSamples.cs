using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/aspnet-core-integration.md.
/// </summary>
public static class AspNetCoreSamples
{
    public static void Registration(string[] args)
    {
        #region sample_aspnetcore_registration

        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        builder.AddQuartz(q =>
        {
            // base Quartz scheduler, job and trigger configuration
        });

        // ASP.NET Core hosting
        builder.AddQuartzHostedService(options =>
        {
            // when shutting down we want jobs to complete gracefully
            options.WaitForJobsToComplete = true;
        });

        WebApplication app = builder.Build();

        #endregion
    }

    #region sample_aspnetcore_job

    public sealed class SendEmailJob : IJob
    {
        private readonly IEmailSender sender;

        public SendEmailJob(IEmailSender sender)
        {
            this.sender = sender;
        }

        public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken = default)
        {
            // Code that sends a periodic email to the user (for example)
            return sender.SendDigest(cancellationToken);
        }
    }

    #endregion

    public static void ScheduleTheJob(WebApplicationBuilder builder)
    {
        #region sample_aspnetcore_schedule_job

        builder.AddQuartz(q =>
        {
            // Just use the name of your job that you created in the Jobs folder.
            JobKey jobKey = new("SendEmailJob");
            q.AddJob<SendEmailJob>(opts => opts.WithIdentity(jobKey));

            q.AddTrigger<SendEmailJob>(opts => opts
                .ForJob(jobKey)
                .WithIdentity("SendEmailJob-trigger")
                // This Cron interval can be described as "run every minute" (when second is zero)
                .WithCronSchedule("0 * * ? * *"));
        });

        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void HealthCheckOptionsSample(WebApplicationBuilder builder)
    {
        #region sample_aspnetcore_health_check_options

        builder.Services.AddHealthChecks().AddQuartz(options =>
        {
            options.Name = "quartz-scheduler";   // the default, or quartz-scheduler-<name> for a named scheduler
            options.Tags.AddRange(["ready", "live"]);
            options.FailureStatus = HealthStatus.Unhealthy;
        });

        #endregion
    }

    public static void NamedSchedulerHealthCheck(WebApplicationBuilder builder)
    {
        #region sample_aspnetcore_named_health_check

        builder.Services.AddHealthChecks().AddQuartz("reporting", options => options.Tags.Add("ready"));

        // or, where the scheduler is configured
        builder.Services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());

        #endregion
    }

    public static void NamedHealthCheckOptions(WebApplicationBuilder builder)
    {
        #region sample_aspnetcore_named_health_check_options

        builder.Services.Configure<QuartzHealthCheckOptions>("reporting", options => options.Tags.Add("ready"));

        #endregion
    }

    public static void MapHealthChecks(WebApplication app)
    {
        #region sample_aspnetcore_map_health_checks

        app.MapHealthChecks("/healthz/ready", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready")
        });

        #endregion
    }
}
