using Quartz.OpenTelemetry.Instrumentation;

namespace OpenTelemetry.Trace;

public static class TraceBuilderExtensions
{
    /// <summary>
    /// Adds Quartz instrumentation to the <see cref="TracerProviderBuilder"/>.
    /// </summary>
    /// <param name="builder">The <see cref="TracerProviderBuilder"/> to add instrumentation to.</param>
    /// <param name="configure">Optional configuration action for <see cref="QuartzInstrumentationOptions"/>.</param>
    /// <returns>The <see cref="TracerProviderBuilder"/> for chaining.</returns>
    [Obsolete("Quartz.OpenTelemetry.Instrumentation is obsolete and incompatible with .NET 10+. Use the official OpenTelemetry.Instrumentation.Quartz package from https://www.nuget.org/packages/OpenTelemetry.Instrumentation.Quartz instead.", false)]
    public static TracerProviderBuilder AddQuartzInstrumentation(
        this TracerProviderBuilder builder,
        Action<QuartzInstrumentationOptions>? configure = null)
    {
        var options = new QuartzInstrumentationOptions();
        configure?.Invoke(options);
        return builder.AddInstrumentation(t => new QuartzJobInstrumentation(t, options));
    }
}