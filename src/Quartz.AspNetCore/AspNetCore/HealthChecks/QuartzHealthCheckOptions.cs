using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz;

/// <summary>
/// Options for the Quartz scheduler health check registered by
/// <see cref="QuartzAspNetCoreConfigurationExtensions.AddQuartzHealthChecks(Microsoft.Extensions.DependencyInjection.IServiceCollection, Action{QuartzHealthCheckOptions})" />.
/// </summary>
public sealed class QuartzHealthCheckOptions
{
    /// <summary>
    /// The name used to register the health check. Defaults to <c>quartz-scheduler</c>, or
    /// <c>quartz-scheduler-&lt;scheduler name&gt;</c> for a named scheduler.
    /// </summary>
    public string Name { get; set; } = "quartz-scheduler";

    /// <summary>
    /// Tags associated with the health check, allowing it to be filtered (for example into
    /// separate liveness and readiness probes).
    /// </summary>
    public IReadOnlyCollection<string> Tags { get; set; } = [];

    /// <summary>
    /// The <see cref="HealthStatus" /> reported when the check fails. When <see langword="null" />
    /// the default (<see cref="HealthStatus.Unhealthy" />) is used.
    /// </summary>
    public HealthStatus? FailureStatus { get; set; }
}
