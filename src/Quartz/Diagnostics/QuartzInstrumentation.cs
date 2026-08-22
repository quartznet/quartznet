namespace Quartz.Diagnostics;

/// <summary>
/// The names an application subscribes to Quartz's telemetry with.
/// </summary>
/// <remarks>
/// Wiring Quartz into OpenTelemetry begins with these two strings, and until 4.0 they were the only part
/// of the instrumentation surface that was not published: the tag names nobody types by hand were public
/// constants while the two names everybody types were internal, so every sample in existence spells them
/// as literals.
/// <code>
/// services.AddOpenTelemetry()
///     .WithTracing(tracing => tracing.AddSource(QuartzInstrumentation.ActivitySourceName))
///     .WithMetrics(metrics => metrics.AddMeter(QuartzInstrumentation.MeterName));
/// </code>
/// </remarks>
public static class QuartzInstrumentation
{
    /// <summary>
    /// The name of the <see cref="System.Diagnostics.ActivitySource"/> a scheduler emits its spans on —
    /// what <c>AddSource</c> is given.
    /// </summary>
    public const string ActivitySourceName = "Quartz";

    /// <summary>
    /// The name of the <see cref="System.Diagnostics.Metrics.Meter"/> a scheduler publishes its
    /// instruments on — what <c>AddMeter</c> is given.
    /// </summary>
    /// <remarks>
    /// It is the same string as <see cref="ActivitySourceName"/>, and deliberately a separate constant:
    /// traces and metrics are subscribed to independently, and a future divergence should not silently
    /// re-point whichever call site borrowed the other one.
    /// </remarks>
    public const string MeterName = "Quartz";

    /// <summary>
    /// The Quartz assembly version, reported as the version of both the source and the meter.
    /// </summary>
    internal static readonly string? Version = typeof(QuartzInstrumentation).Assembly.GetName().Version?.ToString();
}
