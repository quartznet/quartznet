using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Spi;

namespace Quartz.Configuration;

/// <summary>
/// Applies the listeners, calendars, jobs and triggers registered for a scheduler once that scheduler
/// exists.
/// </summary>
/// <remarks>
/// <para>
/// This is deliberately separate from constructing the scheduler. Construction is what the container
/// does; this is the content the application asked to be present, none of which can be applied until
/// there is a scheduler to apply it to.
/// </para>
/// <para>
/// Listener registrations are still matched by options name rather than by service key. Moving them to
/// keyed registrations is a change to the public registration surface, so it is left to the slice that
/// reworks listeners rather than smuggled in here.
/// </para>
/// </remarks>
internal sealed class SchedulerContentInitializer
{
    /// <summary>
    /// The scheduler context entry through which plugins reach the container.
    /// </summary>
    internal const string ServiceProviderContextKey = "Quartz.ServiceProvider";

    private readonly IServiceProvider serviceProvider;
    private readonly QuartzOptions options;
    private readonly ContainerConfigurationProcessor processor;

    /// <remarks>
    /// Takes the resolved options so a named scheduler gets its own, rather than every scheduler
    /// sharing the unnamed instance.
    /// </remarks>
    public SchedulerContentInitializer(
        IServiceProvider serviceProvider,
        QuartzOptions options,
        ContainerConfigurationProcessor processor)
    {
        this.serviceProvider = serviceProvider;
        this.options = options;
        this.processor = processor;
    }

    public async ValueTask Initialize(IScheduler scheduler, string optionsName, CancellationToken cancellationToken)
    {
        // Plugins reach the container through the scheduler context.
        scheduler.Context[ServiceProviderContextKey] = serviceProvider;

        // Deferred configuration may have registered singletons that are not in the built container.
        var provider = options._deferredSingletons.WrapServiceProvider(serviceProvider);

        AddSchedulerListeners(scheduler, optionsName, provider);
        AddJobListeners(scheduler, optionsName, provider);
        AddTriggerListeners(scheduler, optionsName, provider);

        await AddCalendars(scheduler, optionsName, cancellationToken).ConfigureAwait(false);
        await processor.ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    private void AddSchedulerListeners(IScheduler scheduler, string optionsName, IServiceProvider provider)
    {
        if (optionsName.Length == 0)
        {
            foreach (var listener in serviceProvider.GetServices<ISchedulerListener>())
            {
                scheduler.ListenerManager.AddSchedulerListener(listener);
            }
        }

        foreach (var configuration in Configurations<SchedulerListenerConfiguration>(optionsName, x => x.OptionsName))
        {
            scheduler.ListenerManager.AddSchedulerListener(
                ListenerCreationHelper.CreateSchedulerListener(configuration, provider));
        }

        foreach (var configuration in options._deferredSchedulerListeners.Where(x => x.OptionsName == optionsName))
        {
            scheduler.ListenerManager.AddSchedulerListener(
                ListenerCreationHelper.CreateSchedulerListener(configuration, provider));
        }
    }

    private void AddJobListeners(IScheduler scheduler, string optionsName, IServiceProvider provider)
    {
        var configurations = Configurations<JobListenerConfiguration>(optionsName, x => x.OptionsName);

        if (optionsName.Length == 0)
        {
            foreach (var listener in serviceProvider.GetServices<IJobListener>())
            {
                var configuration = configurations.SingleOrDefault(x => x.ListenerType == listener.GetType());
                scheduler.ListenerManager.AddJobListener(listener, configuration?.Matchers ?? []);
            }
        }
        else
        {
            foreach (var configuration in configurations)
            {
                scheduler.ListenerManager.AddJobListener(
                    ListenerCreationHelper.CreateJobListener(configuration, provider), configuration.Matchers);
            }
        }

        foreach (var configuration in options._deferredJobListeners.Where(x => x.OptionsName == optionsName))
        {
            scheduler.ListenerManager.AddJobListener(
                ListenerCreationHelper.CreateJobListener(configuration, provider), configuration.Matchers ?? []);
        }
    }

    private void AddTriggerListeners(IScheduler scheduler, string optionsName, IServiceProvider provider)
    {
        var configurations = Configurations<TriggerListenerConfiguration>(optionsName, x => x.OptionsName);

        if (optionsName.Length == 0)
        {
            foreach (var listener in serviceProvider.GetServices<ITriggerListener>())
            {
                var configuration = configurations.SingleOrDefault(x => x.ListenerType == listener.GetType());
                scheduler.ListenerManager.AddTriggerListener(listener, configuration?.Matchers ?? []);
            }
        }
        else
        {
            foreach (var configuration in configurations)
            {
                scheduler.ListenerManager.AddTriggerListener(
                    ListenerCreationHelper.CreateTriggerListener(configuration, provider), configuration.Matchers);
            }
        }

        foreach (var configuration in options._deferredTriggerListeners.Where(x => x.OptionsName == optionsName))
        {
            scheduler.ListenerManager.AddTriggerListener(
                ListenerCreationHelper.CreateTriggerListener(configuration, provider), configuration.Matchers ?? []);
        }
    }

    private async ValueTask AddCalendars(IScheduler scheduler, string optionsName, CancellationToken cancellationToken)
    {
        foreach (var configuration in Configurations<CalendarConfiguration>(optionsName, x => x.OptionsName))
        {
            await scheduler.AddCalendar(
                configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken)
                .ConfigureAwait(false);
        }

        foreach (var configuration in options._deferredCalendars.Where(x => x.OptionsName == optionsName))
        {
            await scheduler.AddCalendar(
                configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    private T[] Configurations<T>(string optionsName, Func<T, string> nameOf)
    {
        return serviceProvider.GetServices<T>().Where(x => nameOf(x!) == optionsName).ToArray()!;
    }
}
