using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Configuration;

/// <summary>
/// Shared helper for creating listener instances from configuration objects.
/// Used by both <see cref="ServiceCollectionSchedulerFactory"/> and <see cref="NamedSchedulerFactory"/>.
/// </summary>
internal static class ListenerCreationHelper
{
    public static ISchedulerListener CreateSchedulerListener(SchedulerListenerConfiguration config, IServiceProvider serviceProvider)
    {
        if (config.ListenerInstance is not null)
        {
            return config.ListenerInstance;
        }

        if (config.ListenerFactory is not null)
        {
            return config.ListenerFactory(serviceProvider);
        }

        return (ISchedulerListener) ActivatorUtilities.CreateInstance(serviceProvider, config.ListenerType);
    }

    public static IJobListener CreateJobListener(JobListenerConfiguration config, IServiceProvider serviceProvider)
    {
        if (config.ListenerInstance is not null)
        {
            return config.ListenerInstance;
        }

        if (config.ListenerFactory is not null)
        {
            return config.ListenerFactory(serviceProvider);
        }

        return (IJobListener) ActivatorUtilities.CreateInstance(serviceProvider, config.ListenerType);
    }

    public static ITriggerListener CreateTriggerListener(TriggerListenerConfiguration config, IServiceProvider serviceProvider)
    {
        if (config.ListenerInstance is not null)
        {
            return config.ListenerInstance;
        }

        if (config.ListenerFactory is not null)
        {
            return config.ListenerFactory(serviceProvider);
        }

        return (ITriggerListener) ActivatorUtilities.CreateInstance(serviceProvider, config.ListenerType);
    }
}
