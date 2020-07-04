using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Impl;
using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// Wrapper to initialize registered jobs.
    /// </summary>
    internal class ServiceCollectionSchedulerFactory : StdSchedulerFactory
    {
        private readonly IServiceProvider serviceProvider;
        private readonly ServiceCollectionQuartzConfigurator configurator;
        private bool initialized;

        public ServiceCollectionSchedulerFactory(
            IServiceProvider serviceProvider, 
            ServiceCollectionQuartzConfigurator configurator) : base(configurator.schedulerBuilder.Properties)
        {
            this.serviceProvider = serviceProvider;
            this.configurator = configurator;

            // check if logging provider configured and let if configure
            serviceProvider.GetService<LoggingProvider>();
        }

        public override async Task<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
        {
            var scheduler = await base.GetScheduler(cancellationToken);
            if (initialized)
            {
                return scheduler;
            }

            ContainerConfigurationProcessor configurationProcessor = new ContainerConfigurationProcessor(serviceProvider);
            await configurationProcessor.ScheduleJobs(scheduler, cancellationToken);
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