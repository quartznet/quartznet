using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz.Configuration;

/// <summary>
/// Scheduler factory for named (non-default) schedulers in the multi-scheduler DI scenario.
/// Each instance manages a single named scheduler with its own options, listeners, and jobs.
/// </summary>
internal sealed class NamedSchedulerFactory : StdSchedulerFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly string optionsName;
    private readonly QuartzOptions quartzOptions;

    public NamedSchedulerFactory(
        IServiceProvider serviceProvider,
        string optionsName,
        QuartzOptions quartzOptions)
    {
        this.serviceProvider = serviceProvider;
        this.optionsName = optionsName;
        this.quartzOptions = quartzOptions;
    }

    public async ValueTask<IScheduler> CreateAndInitializeScheduler(CancellationToken cancellationToken)
    {
        Initialize(quartzOptions.ToNameValueCollection());

        IScheduler scheduler = await base.GetScheduler(cancellationToken).ConfigureAwait(false);
        await InitializeScheduler(scheduler, cancellationToken).ConfigureAwait(false);

        return scheduler;
    }

    private async Task InitializeScheduler(IScheduler scheduler, CancellationToken cancellationToken)
    {
        // Scheduler listeners for this named scheduler
        IEnumerable<SchedulerListenerConfiguration> schedulerListenerConfigurations = serviceProvider.GetServices<SchedulerListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName);
        foreach (SchedulerListenerConfiguration configuration in schedulerListenerConfigurations)
        {
            ISchedulerListener listener = ListenerCreationHelper.CreateSchedulerListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Deferred scheduler listeners from factory-based AddQuartz overload
        foreach (SchedulerListenerConfiguration configuration in quartzOptions._deferredSchedulerListeners.Where(x => x.OptionsName == optionsName))
        {
            ISchedulerListener listener = ListenerCreationHelper.CreateSchedulerListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Job listeners for this named scheduler
        JobListenerConfiguration[] jobListenerConfigurations = serviceProvider.GetServices<JobListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName)
            .ToArray();
        foreach (JobListenerConfiguration configuration in jobListenerConfigurations)
        {
            IJobListener listener = ListenerCreationHelper.CreateJobListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddJobListener(listener, configuration.Matchers ?? []);
        }

        // Deferred job listeners from factory-based AddQuartz overload
        foreach (JobListenerConfiguration configuration in quartzOptions._deferredJobListeners.Where(x => x.OptionsName == optionsName))
        {
            IJobListener listener = ListenerCreationHelper.CreateJobListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddJobListener(listener, configuration.Matchers ?? []);
        }

        // Trigger listeners for this named scheduler
        TriggerListenerConfiguration[] triggerListenerConfigurations = serviceProvider.GetServices<TriggerListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName)
            .ToArray();
        foreach (TriggerListenerConfiguration configuration in triggerListenerConfigurations)
        {
            ITriggerListener listener = ListenerCreationHelper.CreateTriggerListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddTriggerListener(listener, configuration.Matchers ?? []);
        }

        // Deferred trigger listeners from factory-based AddQuartz overload
        foreach (TriggerListenerConfiguration configuration in quartzOptions._deferredTriggerListeners.Where(x => x.OptionsName == optionsName))
        {
            ITriggerListener listener = ListenerCreationHelper.CreateTriggerListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddTriggerListener(listener, configuration.Matchers ?? []);
        }

        // Calendars for this named scheduler
        IEnumerable<CalendarConfiguration> calendars = serviceProvider.GetServices<CalendarConfiguration>()
            .Where(x => x.OptionsName == optionsName);
        foreach (CalendarConfiguration configuration in calendars)
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

        // Deferred calendars from factory-based AddQuartz overload
        foreach (CalendarConfiguration configuration in quartzOptions._deferredCalendars.Where(x => x.OptionsName == optionsName))
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

        // Schedule jobs and triggers from options
        ITypeLoadHelper typeLoadHelper = serviceProvider.GetRequiredService<ITypeLoadHelper>();
        ILogger<Xml.XMLSchedulingDataProcessor> xmlLogger = serviceProvider.GetRequiredService<ILogger<Xml.XMLSchedulingDataProcessor>>();
        TimeProvider timeProvider = serviceProvider.GetRequiredService<TimeProvider>();
        ContainerConfigurationProcessor processor = new(xmlLogger, typeLoadHelper, timeProvider, Options.Create(quartzOptions));
        await processor.ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    protected override ISchedulerRepository GetSchedulerRepository()
    {
        return serviceProvider.GetRequiredService<ISchedulerRepository>();
    }

    protected override IDbConnectionManager GetDbConnectionManager()
    {
        return serviceProvider.GetRequiredService<IDbConnectionManager>();
    }

    protected override string? GetNamedConnectionString(string connectionStringName)
    {
        IConfiguration? configuration = serviceProvider.GetService<IConfiguration>();
        string? connectionString = configuration?.GetConnectionString(connectionStringName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return base.GetNamedConnectionString(connectionStringName);
    }

    protected override T InstantiateType<T>(Type? implementationType)
    {
        // For named schedulers, prefer creating from the concrete implementationType determined
        // by this scheduler's properties. Resolving by interface (GetService<T>) would pick up
        // singletons registered by the default scheduler or other named schedulers, breaking isolation.
        if (implementationType is not null)
        {
            object? concrete = serviceProvider.GetService(implementationType);
            if (concrete is T typed)
            {
                return typed;
            }

            return (T) ActivatorUtilities.CreateInstance(serviceProvider, implementationType);
        }

        T? service = serviceProvider.GetService<T>();
        if (service is not null)
        {
            return service;
        }

        throw new InvalidOperationException(
            $"No service registered for {typeof(T).Name} and no implementation type was provided. " +
            $"Ensure the required service is registered in DI or configured via scheduler properties.");
    }
}
