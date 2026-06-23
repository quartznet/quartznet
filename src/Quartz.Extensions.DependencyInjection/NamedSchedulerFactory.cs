using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

namespace Quartz;

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

    public async Task<IScheduler> CreateAndInitializeScheduler(CancellationToken cancellationToken)
    {
        serviceProvider.GetService<MicrosoftLoggingProvider>();

        Initialize(quartzOptions.ToNameValueCollection());

        IScheduler scheduler = await base.GetScheduler(cancellationToken).ConfigureAwait(false);
        await InitializeScheduler(scheduler, cancellationToken).ConfigureAwait(false);

        return scheduler;
    }

    private async Task InitializeScheduler(IScheduler scheduler, CancellationToken cancellationToken)
    {
        // Deferred listeners may depend on singletons registered during deferred configuration
        IServiceProvider deferredAwareServiceProvider = quartzOptions.deferredSingletons.WrapServiceProvider(serviceProvider);

        // Scheduler listeners for this named scheduler
        IEnumerable<SchedulerListenerConfiguration> schedulerListenerConfigurations = serviceProvider.GetServices<SchedulerListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName);
        foreach (SchedulerListenerConfiguration configuration in schedulerListenerConfigurations)
        {
            ISchedulerListener listener = ListenerCreationHelper.CreateSchedulerListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Deferred scheduler listeners from factory-based AddQuartz overload
        foreach (SchedulerListenerConfiguration configuration in quartzOptions.deferredSchedulerListeners.Where(x => x.OptionsName == optionsName))
        {
            ISchedulerListener listener = ListenerCreationHelper.CreateSchedulerListener(configuration, deferredAwareServiceProvider);
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Job listeners for this named scheduler
        JobListenerConfiguration[] jobListenerConfigurations = serviceProvider.GetServices<JobListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName)
            .ToArray();
        foreach (JobListenerConfiguration configuration in jobListenerConfigurations)
        {
            IJobListener listener = ListenerCreationHelper.CreateJobListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddJobListener(listener, configuration.Matchers ?? Array.Empty<IMatcher<JobKey>>());
        }

        // Deferred job listeners from factory-based AddQuartz overload
        foreach (JobListenerConfiguration configuration in quartzOptions.deferredJobListeners.Where(x => x.OptionsName == optionsName))
        {
            IJobListener listener = ListenerCreationHelper.CreateJobListener(configuration, deferredAwareServiceProvider);
            scheduler.ListenerManager.AddJobListener(listener, configuration.Matchers ?? Array.Empty<IMatcher<JobKey>>());
        }

        // Trigger listeners for this named scheduler
        TriggerListenerConfiguration[] triggerListenerConfigurations = serviceProvider.GetServices<TriggerListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName)
            .ToArray();
        foreach (TriggerListenerConfiguration configuration in triggerListenerConfigurations)
        {
            ITriggerListener listener = ListenerCreationHelper.CreateTriggerListener(configuration, serviceProvider);
            scheduler.ListenerManager.AddTriggerListener(listener, configuration.Matchers ?? Array.Empty<IMatcher<TriggerKey>>());
        }

        // Deferred trigger listeners from factory-based AddQuartz overload
        foreach (TriggerListenerConfiguration configuration in quartzOptions.deferredTriggerListeners.Where(x => x.OptionsName == optionsName))
        {
            ITriggerListener listener = ListenerCreationHelper.CreateTriggerListener(configuration, deferredAwareServiceProvider);
            scheduler.ListenerManager.AddTriggerListener(listener, configuration.Matchers ?? Array.Empty<IMatcher<TriggerKey>>());
        }

        // Calendars for this named scheduler
        IEnumerable<CalendarConfiguration> calendars = serviceProvider.GetServices<CalendarConfiguration>()
            .Where(x => x.OptionsName == optionsName);
        foreach (CalendarConfiguration configuration in calendars)
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

        // Deferred calendars from factory-based AddQuartz overload
        foreach (CalendarConfiguration configuration in quartzOptions.deferredCalendars.Where(x => x.OptionsName == optionsName))
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

        // Schedule jobs and triggers from options
        ITypeLoadHelper typeLoadHelper = serviceProvider.GetRequiredService<ITypeLoadHelper>();
        ContainerConfigurationProcessor processor = new ContainerConfigurationProcessor(typeLoadHelper, Options.Create(quartzOptions));
        await processor.ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    protected override ISchedulerRepository GetSchedulerRepository()
    {
        return serviceProvider.GetRequiredService<ISchedulerRepository>();
    }

    protected override IDbConnectionManager GetDBConnectionManager()
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

            // Singleton registrations captured during deferred configuration cannot be part of the
            // already-built container; consult the registry first so that those instances (and their
            // companion services) are used, then fall back to plain ActivatorUtilities construction
            // with the deferred registrations available as constructor dependencies. Consult both the
            // service type and the configured concrete type so registrations are found regardless of
            // which one they were keyed with (this matches ServiceCollectionSchedulerFactory).
            if (quartzOptions.deferredSingletons.Resolve(typeof(T), serviceProvider) is T deferred)
            {
                return deferred;
            }
            if (implementationType != typeof(T)
                && quartzOptions.deferredSingletons.Resolve(implementationType, serviceProvider) is T deferredConcrete)
            {
                return deferredConcrete;
            }

            return (T) ActivatorUtilities.CreateInstance(quartzOptions.deferredSingletons.WrapServiceProvider(serviceProvider), implementationType);
        }

        T? service = serviceProvider.GetService<T>();
        if (service is not null)
        {
            return service;
        }

        return ObjectUtils.InstantiateType<T>(implementationType);
    }
}
