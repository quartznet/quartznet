using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
}
