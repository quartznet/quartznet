using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz;

/// <summary>
/// Options for the Quartz scheduler health check registered by
/// <see cref="QuartzHealthCheckExtensions.AddQuartz(Microsoft.Extensions.DependencyInjection.IHealthChecksBuilder, Action{QuartzHealthCheckOptions})" />
/// and by <see cref="QuartzHealthCheckExtensions.AddQuartzHealthChecks(IQuartzBuilder, Action{QuartzHealthCheckOptions})" />.
/// </summary>
public sealed class QuartzHealthCheckOptions
{
    /// <summary>
    /// The name used to register the health check.
    /// </summary>
    /// <remarks>
    /// Left unset, the check is called <c>quartz-scheduler</c>, or
    /// <c>quartz-scheduler-&lt;scheduler name&gt;</c> for a named scheduler — which is why it is
    /// nullable rather than carrying one of those as its value: the name depends on which scheduler the
    /// check is for, and that is not known to an options object.
    /// </remarks>
    public string? Name { get; set; }

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

    /// <summary>
    /// The <see cref="HealthStatus" /> reported while the scheduler is in
    /// <see cref="SchedulerStatus.Standby" />. When <see langword="null" /> the default
    /// (<see cref="HealthStatus.Degraded" />) is used.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Degraded is the right default and the wrong answer for some deployments. Standby is deliberate
    /// and reversible, so calling it healthy would hide an application that never started its
    /// scheduler and calling it unhealthy would take a node out of rotation for doing what it was
    /// told. But a report is only read by whoever it reaches: an ASP.NET Core application can remap
    /// degraded on the endpoint with <c>MapHealthChecks(…, new HealthCheckOptions { ResultStatusCodes
    /// = … })</c>, while a worker has no endpoint at all — its only reader is the probe that asks the
    /// <c>HealthCheckService</c> directly, and a standby node it must not route to has to say
    /// <see cref="HealthStatus.Unhealthy" /> here.
    /// </para>
    /// <para>
    /// This is the standby verdict alone. A scheduler still in <see cref="SchedulerStatus.Created" />
    /// because <see cref="QuartzHostedServiceOptions.AutoStart" /> is <see langword="false" /> also
    /// reports degraded, and keeps doing so: that is a window between the host starting and the
    /// application pressing start, not a state a node sits in.
    /// </para>
    /// </remarks>
    public HealthStatus? StandbyStatus { get; set; }
}
