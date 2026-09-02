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
            // One job and the one trigger that fires it. The job class you wrote in the
            // Jobs folder is the type argument; the job takes its identity from the trigger.
            q.ScheduleJob<SendEmailJob>(trigger => trigger
                .WithIdentity("SendEmailJob-trigger")
                // This Cron interval can be described as "run every minute" (when second is zero)
                .WithCronSchedule("0 * * ? * *"));
        });

        builder.AddQuartzHostedService(options => options.WaitForJobsToComplete = true);

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
