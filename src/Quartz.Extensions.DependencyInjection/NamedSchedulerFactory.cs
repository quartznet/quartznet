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
        // Scheduler listeners for this named scheduler
        IEnumerable<SchedulerListenerConfiguration> schedulerListenerConfigurations = serviceProvider.GetServices<SchedulerListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName);
        foreach (SchedulerListenerConfiguration configuration in schedulerListenerConfigurations)
        {
            ISchedulerListener listener = CreateSchedulerListener(configuration);
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Job listeners for this named scheduler
        JobListenerConfiguration[] jobListenerConfigurations = serviceProvider.GetServices<JobListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName)
            .ToArray();
        foreach (JobListenerConfiguration configuration in jobListenerConfigurations)
        {
            IJobListener listener = CreateJobListener(configuration);
            scheduler.ListenerManager.AddJobListener(listener, configuration.Matchers ?? Array.Empty<IMatcher<JobKey>>());
        }

        // Trigger listeners for this named scheduler
        TriggerListenerConfiguration[] triggerListenerConfigurations = serviceProvider.GetServices<TriggerListenerConfiguration>()
            .Where(x => x.OptionsName == optionsName)
            .ToArray();
        foreach (TriggerListenerConfiguration configuration in triggerListenerConfigurations)
        {
            ITriggerListener listener = CreateTriggerListener(configuration);
            scheduler.ListenerManager.AddTriggerListener(listener, configuration.Matchers ?? Array.Empty<IMatcher<TriggerKey>>());
        }

        // Calendars for this named scheduler
        IEnumerable<CalendarConfiguration> calendars = serviceProvider.GetServices<CalendarConfiguration>()
            .Where(x => x.OptionsName == optionsName);
        foreach (CalendarConfiguration configuration in calendars)
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

        // Schedule jobs and triggers from options
        ITypeLoadHelper typeLoadHelper = serviceProvider.GetRequiredService<ITypeLoadHelper>();
        ContainerConfigurationProcessor processor = new ContainerConfigurationProcessor(typeLoadHelper, Options.Create(quartzOptions));
        await processor.ScheduleJobs(scheduler, cancellationToken).ConfigureAwait(false);
    }

    private ISchedulerListener CreateSchedulerListener(SchedulerListenerConfiguration configuration)
    {
        if (configuration.ListenerInstance != null)
        {
            return configuration.ListenerInstance;
        }

        if (configuration.ListenerFactory != null)
        {
            return configuration.ListenerFactory(serviceProvider);
        }

        return (ISchedulerListener) ActivatorUtilities.CreateInstance(serviceProvider, configuration.ListenerType);
    }

    private IJobListener CreateJobListener(JobListenerConfiguration configuration)
    {
        if (configuration.ListenerInstance != null)
        {
            return configuration.ListenerInstance;
        }

        if (configuration.ListenerFactory != null)
        {
            return configuration.ListenerFactory(serviceProvider);
        }

        return (IJobListener) ActivatorUtilities.CreateInstance(serviceProvider, configuration.ListenerType);
    }

    private ITriggerListener CreateTriggerListener(TriggerListenerConfiguration configuration)
    {
        if (configuration.ListenerInstance != null)
        {
            return configuration.ListenerInstance;
        }

        if (configuration.ListenerFactory != null)
        {
            return configuration.ListenerFactory(serviceProvider);
        }

        return (ITriggerListener) ActivatorUtilities.CreateInstance(serviceProvider, configuration.ListenerType);
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

            return (T) ActivatorUtilities.CreateInstance(serviceProvider, implementationType);
        }

        T? service = serviceProvider.GetService<T>();
        if (service is not null)
        {
            return service;
        }

        return ObjectUtils.InstantiateType<T>(implementationType);
    }
}
