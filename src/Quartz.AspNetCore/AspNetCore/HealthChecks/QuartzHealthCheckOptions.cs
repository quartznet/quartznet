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
    /// <remarks>
    /// Get-only with an in-place initializer, like every other collection on a Quartz options type: a
    /// configuration binder binds into a non-null collection without needing a setter, and one
    /// <c>configure</c> callback cannot discard the tags another added. Add to it —
    /// <c>options.Tags.Add("ready")</c> — rather than assigning a new collection.
    /// </remarks>
    public List<string> Tags { get; } = [];

    /// <summary>
    /// The <see cref="HealthStatus" /> reported when the check fails. When <see langword="null" />
    /// the default (<see cref="HealthStatus.Unhealthy" />) is used.
    /// </summary>
    public HealthStatus? FailureStatus { get; set; }
}
