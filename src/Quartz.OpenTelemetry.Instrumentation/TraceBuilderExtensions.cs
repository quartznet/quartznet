using Quartz.OpenTelemetry.Instrumentation;

namespace OpenTelemetry.Trace;

public static class TraceBuilderExtensions
{
    public static TracerProviderBuilder AddQuartzInstrumentation(
        this TracerProviderBuilder builder,
        Action<QuartzInstrumentationOptions>? configure = null)
    {
        var options = new QuartzInstrumentationOptions();
        configure?.Invoke(options);
        return builder.AddInstrumentation(t => new QuartzJobInstrumentation(t, options));
    }
}