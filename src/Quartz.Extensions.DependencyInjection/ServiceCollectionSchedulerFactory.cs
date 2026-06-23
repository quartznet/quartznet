using System;
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
/// Wrapper to initialize registered jobs.
/// </summary>
internal sealed class ServiceCollectionSchedulerFactory : StdSchedulerFactory
{
    private readonly IServiceProvider serviceProvider;
    private readonly IOptions<QuartzOptions> options;
    private readonly ContainerConfigurationProcessor processor;
    private readonly SemaphoreSlim semaphore = new SemaphoreSlim(1, 1);
    private IScheduler? initializedScheduler;

    public ServiceCollectionSchedulerFactory(
        IServiceProvider serviceProvider,
        IOptions<QuartzOptions> options,
        ContainerConfigurationProcessor processor)
    {
        this.serviceProvider = serviceProvider;
        this.options = options;
        this.processor = processor;
    }

    public override Task<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
    {
        return EnsureSchedulerCreated(cancellationToken);
    }

    /// <summary>
    /// Gets a scheduler by name, ensuring it is created and initialized first.
    /// </summary>
    /// <param name="schedName">Name of the scheduler to retrieve</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>The scheduler with the given name, or null if not found</returns>    
    public override async Task<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
    {
        // Ensure the default scheduler is created and initialized
        await EnsureSchedulerCreated(cancellationToken);

        // Now perform the lookup by name
        return await base.GetScheduler(schedName, cancellationToken);
    }

    private async Task<IScheduler> EnsureSchedulerCreated(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken);
        try
        {
            if (initializedScheduler == null)
            {
                // check if logging provider configured and let if configure
                serviceProvider.GetService<MicrosoftLoggingProvider>();

                Initialize(options.Value.ToNameValueCollection());
            }

            var scheduler = await base.GetScheduler(cancellationToken);

            // The base method may produce a new scheduler in the event that the original scheduler was stopped
            if (!ReferenceEquals(scheduler, initializedScheduler))
            {
                await InitializeScheduler(scheduler, cancellationToken);
                initializedScheduler = scheduler;
            }

            return scheduler;
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task InitializeScheduler(IScheduler scheduler, CancellationToken cancellationToken)
    {
        // Make the DI service provider available to plugins via SchedulerContext
        scheduler.Context["Quartz.ServiceProvider"] = serviceProvider;

        // Deferred listeners may depend on singletons registered during deferred configuration
        var deferredAwareServiceProvider = options.Value.deferredSingletons.WrapServiceProvider(serviceProvider);

        // Default scheduler uses flat ISchedulerListener services (backward compatible)
        foreach (var listener in serviceProvider.GetServices<ISchedulerListener>())
        {
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Process deferred scheduler listeners from factory-based AddQuartz overload
        foreach (var config in options.Value.deferredSchedulerListeners.Where(x => x.OptionsName.Length == 0))
        {
            var listener = ListenerCreationHelper.CreateSchedulerListener(config, deferredAwareServiceProvider);
            scheduler.ListenerManager.AddSchedulerListener(listener);
        }

        // Filter configurations to only those belonging to the default (unnamed) scheduler
        var jobListeners = serviceProvider.GetServices<IJobListener>();
        var jobListenerConfigurations = serviceProvider.GetServices<JobListenerConfiguration>()
            .Where(x => x.OptionsName.Length == 0)
            .ToArray();
        foreach (var listener in jobListeners)
        {
            var configuration = jobListenerConfigurations.SingleOrDefault(x => x.ListenerType == listener.GetType());
            scheduler.ListenerManager.AddJobListener(listener, configuration?.Matchers ?? Array.Empty<IMatcher<JobKey>>());
        }

        // Process deferred job listeners from factory-based AddQuartz overload
        foreach (var config in options.Value.deferredJobListeners.Where(x => x.OptionsName.Length == 0))
        {
            var listener = ListenerCreationHelper.CreateJobListener(config, deferredAwareServiceProvider);
            scheduler.ListenerManager.AddJobListener(listener, config.Matchers ?? Array.Empty<IMatcher<JobKey>>());
        }

        var triggerListeners = serviceProvider.GetServices<ITriggerListener>();
        var triggerListenerConfigurations = serviceProvider.GetServices<TriggerListenerConfiguration>()
            .Where(x => x.OptionsName.Length == 0)
            .ToArray();
        foreach (var listener in triggerListeners)
        {
            var configuration = triggerListenerConfigurations.SingleOrDefault(x => x.ListenerType == listener.GetType());
            scheduler.ListenerManager.AddTriggerListener(listener, configuration?.Matchers ?? Array.Empty<IMatcher<TriggerKey>>());
        }

        // Process deferred trigger listeners from factory-based AddQuartz overload
        foreach (var config in options.Value.deferredTriggerListeners.Where(x => x.OptionsName.Length == 0))
        {
            var listener = ListenerCreationHelper.CreateTriggerListener(config, deferredAwareServiceProvider);
            scheduler.ListenerManager.AddTriggerListener(listener, config.Matchers ?? Array.Empty<IMatcher<TriggerKey>>());
        }

        var calendars = serviceProvider.GetServices<CalendarConfiguration>()
            .Where(x => x.OptionsName.Length == 0);
        foreach (var configuration in calendars)
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

        // Process deferred calendars from factory-based AddQuartz overload
        foreach (var configuration in options.Value.deferredCalendars.Where(x => x.OptionsName.Length == 0))
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

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
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var connectionString = configuration.GetConnectionString(connectionStringName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return base.GetNamedConnectionString(connectionStringName);
    }

    protected override T InstantiateType<T>(Type? implementationType)
    {
        var service = serviceProvider.GetService<T>();
        if (service is not null)
        {
            return service;
        }

        // Singleton registrations captured during deferred configuration cannot be part of the
        // already-built container; construct them here with constructor injection support.
        // Consult both the service type and the configured concrete type so registrations are
        // found regardless of which one they were keyed with.
        if (options.Value.deferredSingletons.Resolve(typeof(T), serviceProvider) is T deferred)
        {
            return deferred;
        }
        if (implementationType is not null && implementationType != typeof(T)
            && options.Value.deferredSingletons.Resolve(implementationType, serviceProvider) is T deferredConcrete)
        {
            return deferredConcrete;
        }

        return ObjectUtils.InstantiateType<T>(implementationType);
    }
}