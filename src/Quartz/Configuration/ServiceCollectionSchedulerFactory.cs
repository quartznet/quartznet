using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl;
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
    private readonly SemaphoreSlim semaphore = new(1, 1);
    private IScheduler? initializedScheduler;

    public ServiceCollectionSchedulerFactory(
        ILogger<ServiceCollectionSchedulerFactory> logger,
        IServiceProvider serviceProvider,
        IOptions<QuartzOptions> options,
        ContainerConfigurationProcessor processor)
    {
        this.logger = logger;
        this.serviceProvider = serviceProvider;
        this.options = options;
        this.processor = processor;
    }

    public override async ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
    {
        return await EnsureSchedulerCreated(cancellationToken).ConfigureAwait(false);
    }

    public override async ValueTask<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
    {
        await EnsureSchedulerCreated(cancellationToken).ConfigureAwait(false);
        return await base.GetScheduler(schedName, cancellationToken).ConfigureAwait(false);
    }

    private async ValueTask<IScheduler> EnsureSchedulerCreated(CancellationToken cancellationToken)
    {
        await semaphore.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (initializedScheduler is null)
            {
                Initialize(options.Value.ToNameValueCollection());
            }

            var scheduler = await base.GetScheduler(cancellationToken).ConfigureAwait(false);

            // The base method may produce a new scheduler in the event that the original scheduler was stopped
            if (!ReferenceEquals(scheduler, initializedScheduler))
            {
                await InitializeScheduler(scheduler, cancellationToken).ConfigureAwait(false);
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
        // Default scheduler uses flat ISchedulerListener services (backward compatible)
        foreach (var listener in serviceProvider.GetServices<ISchedulerListener>())
        {
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
            scheduler.ListenerManager.AddJobListener(listener, configuration?.Matchers ?? []);
        }

        var triggerListeners = serviceProvider.GetServices<ITriggerListener>();
        var triggerListenerConfigurations = serviceProvider.GetServices<TriggerListenerConfiguration>()
            .Where(x => x.OptionsName.Length == 0)
            .ToArray();
        foreach (var listener in triggerListeners)
        {
            var configuration = triggerListenerConfigurations.SingleOrDefault(x => x.ListenerType == listener.GetType());
            scheduler.ListenerManager.AddTriggerListener(listener, configuration?.Matchers ?? []);
        }

        var calendars = serviceProvider.GetServices<CalendarConfiguration>()
            .Where(x => x.OptionsName.Length == 0);
        foreach (var configuration in calendars)
        {
            await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken).ConfigureAwait(false);
        }

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
        var configuration = serviceProvider.GetService<IConfiguration>();
        var connectionString = configuration?.GetConnectionString(connectionStringName);
        if (!string.IsNullOrWhiteSpace(connectionString))
        {
            return connectionString;
        }

        return base.GetNamedConnectionString(connectionStringName);
    }

    protected override T InstantiateType<T>(Type? implementationType)
    {
        var service = serviceProvider.GetService<T>();
        if (service is null)
        {
            service = ObjectUtils.InstantiateType<T>(implementationType);
        }

        return service;
    }
}
