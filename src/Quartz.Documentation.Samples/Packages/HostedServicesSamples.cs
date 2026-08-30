using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/hosted-services-integration.md.
/// </summary>
public static class HostedServicesSamples
{
    public static async ValueTask TheWholeProgram(string[] args)
    {
        #region sample_hosted_program

        HostApplicationBuilder builder = Host.CreateApplicationBuilder(args);

        // see Quartz documentation about how to configure different configuration aspects
        builder.AddQuartz(q =>
        {
            // your configuration here
        });

        // Quartz hosting
        builder.AddQuartzHostedService(options =>
        {
            // when shutting down we want jobs to complete gracefully
            options.WaitForJobsToComplete = true;
        });

        await builder.Build().RunAsync();

        #endregion
    }

    public static void DerivedHostedService(IHostApplicationBuilder builder)
    {
        #region sample_hosted_derived_service

        builder.AddQuartzHostedService<WarmUpBeforeSchedulingService>(options => options.WaitForJobsToComplete = true);

        #endregion
    }

    public static void SchedulingFromConfiguration(HostApplicationBuilder builder)
    {
        #region sample_hosted_configuration_section

        builder.Services.AddQuartz(builder.Configuration.GetSection("Scheduling"), q => { });

        #endregion
    }

    public static void DeferredStart(IHostApplicationBuilder builder)
    {
        #region sample_hosted_deferred_start

        builder.AddQuartz("reporting", q => { });

        // Built, initialized and bound with the host, but left in Created for the application to start
        builder.AddQuartzHostedService("reporting", options => options.AutoStart = false);

        #endregion
    }

    public static void HealthCheck(IHostApplicationBuilder builder)
    {
        #region sample_hosted_health_check

        builder.Services.AddHealthChecks().AddQuartz();

        #endregion
    }

    public static void HealthCheckOptions(IHostApplicationBuilder builder)
    {
        #region sample_hosted_health_check_options

        builder.Services.AddHealthChecks().AddQuartz(options =>
        {
            options.Name = "quartz-scheduler";   // the default, or quartz-scheduler-<name> for a named scheduler
            options.Tags.AddRange(["ready", "live"]);
            options.FailureStatus = HealthStatus.Unhealthy;
        });

        #endregion
    }

    public static void NamedSchedulerHealthCheck(IHostApplicationBuilder builder)
    {
        #region sample_hosted_named_health_check

        builder.Services.AddHealthChecks().AddQuartz("reporting", options => options.Tags.Add("ready"));

        // or, where the scheduler is configured
        builder.Services.AddQuartz("reporting", q => q.AddQuartzHealthChecks());

        #endregion
    }

    public static void NamedHealthCheckOptions(IHostApplicationBuilder builder)
    {
        #region sample_hosted_named_health_check_options

        builder.Services.Configure<QuartzHealthCheckOptions>("reporting", options => options.Tags.Add("ready"));

        #endregion
    }
}
