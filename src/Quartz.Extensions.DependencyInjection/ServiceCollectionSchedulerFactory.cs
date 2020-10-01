using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// Wrapper to initialize registered jobs.
    /// </summary>
    internal class ServiceCollectionSchedulerFactory : StdSchedulerFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly IOptions<QuartzOptions> options;
        private readonly ContainerConfigurationProcessor processor;
        private bool initialized;

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
            // check if logging provider configured and let if configure
            serviceProvider.GetService<MicrosoftLoggingProvider>();

            base.Initialize(options.Value);
            var scheduler = await base.GetScheduler(cancellationToken);
            if (initialized)
            {
                return scheduler;
            }

            foreach (var listener in serviceProvider.GetServices<ISchedulerListener>())
            {
                scheduler.ListenerManager.AddSchedulerListener(listener);
            }

            var jobListeners = serviceProvider.GetServices<IJobListener>();
            foreach (var configuration in serviceProvider.GetServices<JobListenerConfiguration>())
            {
                var listener = jobListeners.First(x => x.GetType() == configuration.ListenerType);
                scheduler.ListenerManager.AddJobListener(listener, configuration.Matchers);
            }

            var triggerListeners = serviceProvider.GetServices<ITriggerListener>();
            foreach (var configuration in serviceProvider.GetServices<TriggerListenerConfiguration>())
            {
                var listener = triggerListeners.First(x => x.GetType() == configuration.ListenerType);
                scheduler.ListenerManager.AddTriggerListener(listener, configuration.Matchers);
            }

            var calendars = serviceProvider.GetServices<CalendarConfiguration>();
            foreach (var configuration in calendars)
            {
                await scheduler.AddCalendar(configuration.Name, configuration.Calendar, configuration.Replace, configuration.UpdateTriggers, cancellationToken);
            }

            await processor.ScheduleJobs(scheduler, cancellationToken);
            initialized = true;
            return scheduler;
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