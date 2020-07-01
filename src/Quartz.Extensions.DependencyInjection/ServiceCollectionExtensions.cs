using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Quartz.Logging;

namespace Quartz
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddQuartz(
            this IServiceCollection services,
            Action<ServiceCollectionSchedulerConfigurator>? configure = null)
        {
            var builder = new ServiceCollectionSchedulerConfigurator(services);
            configure?.Invoke(builder);
            
            services.AddSingleton<ISchedulerFactory>(serviceProvider => new ServiceCollectionSchedulerFactory(serviceProvider, builder));
            return services;
        }        
        
        /// <summary>
        /// Adds LibLog configuration to use Microsoft's logging abstraction instead of trying to find one.
        /// </summary>
        public static IServiceCollection AddQuartzMicrosoftLoggingBridge(this IServiceCollection services)
        {
            services.TryAddSingleton(serviceProvider =>
            {
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                var loggingProvider = new LoggingProvider(loggerFactory);
                LogProvider.SetCurrentLogProvider(loggingProvider);
                return loggingProvider;
            });            
            return services;
        }
        
        /// <summary>
        /// Adds LibLog configuration to use Microsoft's logging abstraction instead of trying to find one.
        /// </summary>
        public static IServiceCollection AddQuartzJob<T>(
            this IServiceCollection services,
            Action<ServiceCollectionJobConfigurator>? configure = null) where T : IJob
        {
            var builder = JobBuilder.Create<T>();
            var configurator = new ServiceCollectionJobConfigurator(services, builder);
            configure?.Invoke(configurator);
            var jobDetail = configurator.jobBuilder.Build();

            services.AddTransient(x => jobDetail);
            services.AddTransient(jobDetail.JobType);
            
            return services;
        }        

        /// <summary>
        /// Adds LibLog configuration to use Microsoft's logging abstraction instead of trying to find one.
        /// </summary>
        public static IServiceCollection AddQuartzTrigger(this IServiceCollection services, Action<ServiceCollectionTriggerConfigurator>? configure = null)
        {
            var builder = TriggerBuilder.Create();
            var configurator = new ServiceCollectionTriggerConfigurator(services, builder);
            configure?.Invoke(configurator);
            var trigger = configurator.Build();
            
            services.AddTransient(x => trigger);
            
            return services;
        }
    }
}