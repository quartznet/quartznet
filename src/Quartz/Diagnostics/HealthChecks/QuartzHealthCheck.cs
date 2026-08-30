using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;

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
    private readonly IServiceProvider serviceProvider;
    private readonly SchedulerHealthCheckTarget target;
    private readonly IOptionsMonitor<QuartzHostedServiceOptions> hostedServiceOptions;

    public QuartzHealthCheck(
        IServiceProvider serviceProvider,
        SchedulerHealthCheckTarget target,
        IOptionsMonitor<QuartzHostedServiceOptions> hostedServiceOptions)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);
        ArgumentNullException.ThrowIfNull(target);
        ArgumentNullException.ThrowIfNull(hostedServiceOptions);

        this.serviceProvider = serviceProvider;
        this.target = target;
        this.hostedServiceOptions = hostedServiceOptions;
    }

    async Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
    {
        // Resolved here rather than in the constructor, and asked for rather than demanded: a container
        // whose schedulers are all named has no unkeyed ISchedulerFactory, and a missing scheduler should
        // be an unhealthy probe with something to read, not an InvalidOperationException out of the
        // health-check pipeline.
        ISchedulerFactory? schedulerFactory = target.SchedulerName is null
            ? serviceProvider.GetService<ISchedulerFactory>()
            : serviceProvider.GetKeyedService<ISchedulerFactory>(target.SchedulerName);

        if (schedulerFactory is null)
        {
            return HealthCheckResult.Unhealthy(target.SchedulerName is null
                ? "There is no default Quartz scheduler in this container, so this health check has nothing to "
                  + "report on. Every scheduler here is registered under a name; call AddQuartzHealthChecks() on "
                  + "the scheduler's own builder, or AddQuartz(schedulerName) on the health checks builder, so the "
                  + "check knows which one it is for."
                : $"There is no Quartz scheduler named '{target.SchedulerName}' in this container.");
        }

        IScheduler scheduler = await schedulerFactory.GetScheduler(cancellationToken).ConfigureAwait(false);
        string name = scheduler.SchedulerName;

        switch (scheduler.Status)
        {
            case SchedulerStatus.Running:
                break;

            // Alive, reachable and deliberately firing nothing. Reporting healthy would hide an
            // application that never started its scheduler; reporting unhealthy would take a node out of
            // rotation for doing exactly what it was told.
            case SchedulerStatus.Standby:
                return HealthCheckResult.Degraded($"Quartz scheduler '{name}' is in standby");

            // The same argument as standby, one step earlier: a scheduler whose AutoStart is false was
            // never meant to be started by the host, so its being Created is the configuration working
            // rather than failing. Unhealthy here would take a correctly configured node out of
            // rotation for the window between the host starting and the application pressing start.
            case SchedulerStatus.Created when StartedByTheApplication():
                return HealthCheckResult.Degraded($"Quartz scheduler '{name}' has been created and is started by the application");

            case SchedulerStatus.Created:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' has been created but never started");

            case SchedulerStatus.ShuttingDown:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' is shutting down");

            case SchedulerStatus.Shutdown:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' has been shut down");

            default:
                return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' did not report a state it is in");
        }

        try
        {
            // Ask for a job we know doesn't exist
            await scheduler.Exists(new JobKey(Guid.NewGuid().ToString()), cancellationToken).ConfigureAwait(false);
        }
        catch (SchedulerException)
        {
            return HealthCheckResult.Unhealthy($"Quartz scheduler '{name}' cannot connect to the store");
        }

        return HealthCheckResult.Healthy($"Quartz scheduler '{name}' is ready");
    }

    /// <summary>
    /// Whether this check's scheduler is one the application starts itself rather than one the hosted
    /// service starts.
    /// </summary>
    /// <remarks>
    /// The scheduler's own options, read under its own name, the way every other per-scheduler setting
    /// is. A container with no hosted service in it has nothing configuring these options at all, so
    /// <see cref="QuartzHostedServiceOptions.AutoStart" /> is its default and a created scheduler stays
    /// unhealthy — which is what it should be, since nothing is going to start it.
    /// </remarks>
    private bool StartedByTheApplication()
    {
        return !hostedServiceOptions.Get(target.SchedulerName ?? Options.DefaultName).AutoStart;
    }
}
