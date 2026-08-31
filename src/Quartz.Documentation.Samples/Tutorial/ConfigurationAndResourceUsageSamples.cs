using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Documentation.Samples.Tutorial;

/// <summary>
/// Samples for
/// docs/documentation/quartz-4.x/tutorial/configuration-resource-usage-and-scheduler-factory.md.
/// </summary>
public static class ConfigurationAndResourceUsageSamples
{
    public static async ValueTask BuildingAScheduler()
    {
        #region sample_configuration_building_a_scheduler

        IScheduler scheduler = await QuartzSchedulerBuilder
            .Create(q => q
                .ConfigureScheduler(options => options.InstanceName = "reporting")
                .UseDefaultThreadPool(maxConcurrency: 10)
                .UseInMemoryStore())
            .BuildScheduler();

        #endregion
    }

    public static async ValueTask FromProperties(NameValueCollection properties)
    {
        #region sample_configuration_from_properties

        await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder.Create()
            .UseProperties(properties)
            .Build();

        #endregion
    }

    public static void Logging(IServiceProvider serviceProvider)
    {
        #region sample_configuration_log_provider

        // obtain your logger factory, for example from IServiceProvider
        ILoggerFactory loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

        LogProvider.SetLogProvider(loggerFactory);

        #endregion
    }
}
