using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz;

/// <summary>
/// Which scheduler a health check reports on: a named one, or the default one.
/// </summary>
/// <remarks>
/// Handed to the check as a constructor argument rather than resolved from the container, because
/// several checks can be registered in one container and each has to know its own scheduler.
/// </remarks>
internal sealed record SchedulerHealthCheckTarget(string? SchedulerName);

internal sealed class QuartzHealthCheck : IHealthCheck
{
    private readonly ISchedulerFactory schedulerFactory;

    public QuartzHealthCheck(IServiceProvider serviceProvider, SchedulerHealthCheckTarget target)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(target);

        schedulerFactory = target.SchedulerName is null
            ? serviceProvider.GetRequiredService<ISchedulerFactory>()
            : serviceProvider.GetRequiredKeyedService<ISchedulerFactory>(target.SchedulerName);
    }

    async Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        var scheduler = await schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
        if (!scheduler.IsStarted)
        {
            return HealthCheckResult.Unhealthy("Quartz scheduler is not running");
        }

        try
        {
            // Ask for a job we know doesn't exist
            await scheduler.CheckExists(new JobKey(Guid.NewGuid().ToString()), cancellationToken).ConfigureAwait(false);
        }
        catch (SchedulerException)
        {
            return HealthCheckResult.Unhealthy("Quartz scheduler cannot connect to the store");
        }

        return HealthCheckResult.Healthy("Quartz scheduler is ready");
    }
}