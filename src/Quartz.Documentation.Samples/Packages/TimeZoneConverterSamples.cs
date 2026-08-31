using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/timezoneconverter-integration.md.
/// </summary>
public static class TimeZoneConverterSamples
{
    public static void UnderAHost(IHostApplicationBuilder builder)
    {
        #region sample_timezoneconverter_host

        builder.Services.AddQuartz(q => q.UseTimeZoneConverter());

        #endregion
    }

    public static async ValueTask Standalone()
    {
        #region sample_timezoneconverter_standalone

        await using StandaloneSchedulerFactory schedulerFactory = QuartzSchedulerBuilder
            .Create(q => q.UseTimeZoneConverter())
            .Build();

        #endregion
    }
}
