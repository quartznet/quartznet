using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

using Quartz.Diagnostics;

namespace Quartz.Documentation.Samples.Packages;

/// <summary>
/// Samples for docs/documentation/quartz-4.x/packages/opentelemetry-integration.md.
/// </summary>
public static class OpenTelemetrySamples
{
    public static void SubscribeToQuartzSignals(IHostApplicationBuilder builder)
    {
        #region sample_opentelemetry_subscribe

        builder.Services.AddOpenTelemetry()
            .WithTracing(tracing => tracing
                .AddSource(QuartzInstrumentation.ActivitySourceName)
                .AddOtlpExporter())
            .WithMetrics(metrics => metrics
                .AddMeter(QuartzInstrumentation.MeterName)
                .AddOtlpExporter());

        #endregion
    }
}
