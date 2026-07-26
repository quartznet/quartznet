using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Impl.Matchers;
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

        AddSchedulerListeners(scheduler, optionsName, serviceProvider);
        AddJobListeners(scheduler, optionsName, serviceProvider);
        AddTriggerListeners(scheduler, optionsName, serviceProvider);

        await AddCalendars(scheduler, optionsName, cancellationToken).ConfigureAwait(false);
        await processor.ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    private void AddSchedulerListeners(IScheduler scheduler, string optionsName, IServiceProvider provider)
    {
        var configurations = Configurations<SchedulerListenerConfiguration>(optionsName, x => x.OptionsName);

        foreach (var configuration in configurations)
        {
            scheduler.ListenerManager.AddSchedulerListener(
                ListenerCreationHelper.CreateSchedulerListener(configuration, provider));
        }

        // Listeners the application registered directly, which carry no configuration of their own.
        if (optionsName.Length == 0)
        {
            var configured = configurations.Select(x => x.ListenerType).ToHashSet();
            foreach (var listener in serviceProvider.GetServices<ISchedulerListener>().Where(x => !configured.Contains(x.GetType())))
            {
                scheduler.ListenerManager.AddSchedulerListener(listener);
            }
        }

    }

    private void AddJobListeners(IScheduler scheduler, string optionsName, IServiceProvider provider)
    {
        var configurations = Configurations<JobListenerConfiguration>(optionsName, x => x.OptionsName);

        foreach (var configuration in configurations)
        {
            scheduler.ListenerManager.AddJobListener(
                ListenerCreationHelper.CreateJobListener(configuration, provider), configuration.Matchers);
        }

        // Listeners the application registered directly, which carry no configuration of their own.
        if (optionsName.Length == 0)
        {
            var configured = configurations.Select(x => x.ListenerType).ToHashSet();
            foreach (var listener in serviceProvider.GetServices<IJobListener>().Where(x => !configured.Contains(x.GetType())))
            {
                scheduler.ListenerManager.AddJobListener(listener, []);
            }
        }

        // Listeners named by quartz.jobListener.* properties, which carry no matchers and so listen to
        // everything, as that format has always meant.
        foreach (var listener in PropertyListenerFactory.Create<IJobListener>(
                     serviceProvider, options.ToNameValueCollection(), StdSchedulerFactory.PropertyJobListenerPrefix))
        {
            scheduler.ListenerManager.AddJobListener(listener, EverythingMatcher<JobKey>.AllJobs());
        }
    }

    private void AddTriggerListeners(IScheduler scheduler, string optionsName, IServiceProvider provider)
    {
        var configurations = Configurations<TriggerListenerConfiguration>(optionsName, x => x.OptionsName);

        foreach (var configuration in configurations)
        {
            scheduler.ListenerManager.AddTriggerListener(
                ListenerCreationHelper.CreateTriggerListener(configuration, provider), configuration.Matchers);
        }

        if (optionsName.Length == 0)
        {
            var configured = configurations.Select(x => x.ListenerType).ToHashSet();
            foreach (var listener in serviceProvider.GetServices<ITriggerListener>().Where(x => !configured.Contains(x.GetType())))
            {
                scheduler.ListenerManager.AddTriggerListener(listener, []);
            }
        }

        foreach (var listener in PropertyListenerFactory.Create<ITriggerListener>(
                     serviceProvider, options.ToNameValueCollection(), StdSchedulerFactory.PropertyTriggerListenerPrefix))
        {
            scheduler.ListenerManager.AddTriggerListener(listener, EverythingMatcher<TriggerKey>.AllTriggers());
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

    }

    private T[] Configurations<T>(string optionsName, Func<T, string> nameOf)
    {
        return serviceProvider.GetServices<T>().Where(x => nameOf(x!) == optionsName).ToArray()!;
    }
}
