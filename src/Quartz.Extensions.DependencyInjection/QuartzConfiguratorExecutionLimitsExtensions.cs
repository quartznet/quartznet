using System;

using Quartz.Impl;

namespace Quartz;

/// <summary>
/// Extension methods for configuring execution group limits via dependency injection.
/// </summary>
public static class QuartzConfiguratorExecutionLimitsExtensions
{
    /// <summary>
    /// Configures execution group limits for this scheduler node. Execution groups
    /// allow per-node thread limits so that resource-intensive jobs do not saturate
    /// all available threads.
    /// </summary>
    /// <param name="configurator">The Quartz DI configurator.</param>
    /// <param name="configure">Action to configure the execution limits.</param>
    public static void UseExecutionLimits(
        this IServiceCollectionQuartzConfigurator configurator,
        Action<ExecutionLimits> configure)
    {
        if (configurator is null)
        {
            throw new ArgumentNullException(nameof(configurator));
        }

        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        ExecutionLimits limits = new();
        configure(limits);

        foreach (var kvp in limits)
        {
            string propKey;
            if (kvp.Key == ExecutionLimits.DefaultGroupKey)
            {
                propKey = "_";
            }
            else
            {
                propKey = kvp.Key;
            }

            string propValue = kvp.Value?.ToString() ?? "unlimited";
            configurator.SetProperty($"{StdSchedulerFactory.PropertyExecutionLimitPrefix}.{propKey}", propValue);
        }
    }
}
