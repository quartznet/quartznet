using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// Wrapper to initialize registered jobs.
    /// </summary>
    internal sealed class ServiceCollectionSchedulerFactory : StdSchedulerFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IOptions<QuartzOptions> options;
        private readonly ContainerConfigurationProcessor processor;
        private bool initialized;
        private readonly SemaphoreSlim initializationLock = new SemaphoreSlim(1, 1);

        public ServiceCollectionSchedulerFactory(
            IServiceProvider serviceProvider,
            IOptions<QuartzOptions> options,
            ContainerConfigurationProcessor processor)
        {
            this.serviceProvider = serviceProvider;
            this.options = options;
            this.processor = processor;
        }

        public override async Task<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
        {
            base.Initialize(options.Value.ToNameValueCollection());
            var scheduler = await base.GetScheduler(cancellationToken);
            if (initialized)
            {
                return scheduler;
            }

            await initializationLock.WaitAsync(cancellationToken);
            try
            {
                if (!initialized)
                {
                    await InitializeScheduler(scheduler, cancellationToken);
                    initialized = true;
                }
            }
            finally
            {
                initializationLock.Release();
            }
            return scheduler;
        }

        private async Task InitializeScheduler(IScheduler scheduler, CancellationToken cancellationToken)
        {
            foreach (var listener in serviceProvider.GetServices<ISchedulerListener>())
            {
                scheduler.ListenerManager.AddSchedulerListener(listener);
            }

            var jobListeners = serviceProvider.GetServices<IJobListener>();
            var jobListenerConfigurations = serviceProvider.GetServices<JobListenerConfiguration>().ToArray();
            foreach (var listener in jobListeners)
            {
                var configuration = jobListenerConfigurations.SingleOrDefault(x => x.ListenerType == listener.GetType());
                scheduler.ListenerManager.AddJobListener(listener, configuration?.Matchers ?? Array.Empty<IMatcher<JobKey>>());
            }

            var triggerListeners = serviceProvider.GetServices<ITriggerListener>();
            var triggerListenerConfigurations = serviceProvider.GetServices<TriggerListenerConfiguration>().ToArray();
            foreach (var listener in triggerListeners)
            {
                var configuration = triggerListenerConfigurations.SingleOrDefault(x => x.ListenerType == listener.GetType());
                scheduler.ListenerManager.AddTriggerListener(listener, configuration?.Matchers ?? Array.Empty<IMatcher<TriggerKey>>());
            }

            var calendars = serviceProvider.GetServices<CalendarConfiguration>();
            foreach (var configuration in calendars)
            {
                await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken);
            }

            await processor.ScheduleJobs(scheduler, cancellationToken);
        }

        private protected override string GetNamedConnectionString(string connectionStringName)
        {
            var configuration = serviceProvider.GetService<IConfiguration>();
            var connectionString = configuration.GetConnectionString(connectionStringName);
            if (!string.IsNullOrWhiteSpace(connectionString))
            {
                return connectionString;
            }

            return base.GetNamedConnectionString(connectionString);
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
}