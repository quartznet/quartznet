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
            Action<ServiceCollectionQuartzConfigurator>? configure = null)
        {
            var builder = new ServiceCollectionQuartzConfigurator(services);
            configure?.Invoke(builder);
            
            services.AddSingleton<ISchedulerFactory>(serviceProvider => new ServiceCollectionSchedulerFactory(serviceProvider, builder));
            return services;
        }        
        
        /// <summary>
        /// Adds LibLog configuration to use Microsoft's logging abstraction instead of trying to find one.
        /// </summary>
        public static void UseQuartzMicrosoftLoggingBridge(this ServiceCollectionQuartzConfigurator configurator)
        {
            configurator.Services.TryAddSingleton(serviceProvider =>
            {
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                var loggingProvider = new LoggingProvider(loggerFactory);
                LogProvider.SetCurrentLogProvider(loggingProvider);
                return loggingProvider;
            });            
        }
        
        /// <summary>
        /// Adds LibLog configuration to use Microsoft's logging abstraction instead of trying to find one.
        /// </summary>
        public static ServiceCollectionQuartzConfigurator AddJob<T>(
            this ServiceCollectionQuartzConfigurator configurator,
            Action<IServiceCollectionJobConfigurator>? configure = null) where T : IJob
        {
            var c = new ServiceCollectionJobConfigurator(configurator.Services);
            c.OfType<T>();

            configure?.Invoke(c);
            var jobDetail = c.Build();

            configurator.Services.AddTransient(x => jobDetail);
            configurator.Services.AddTransient(jobDetail.JobType);
            
            return configurator;
        }        

        /// <summary>
        /// Adds LibLog configuration to use Microsoft's logging abstraction instead of trying to find one.
        /// </summary>
        public static ServiceCollectionQuartzConfigurator AddTrigger(
            this ServiceCollectionQuartzConfigurator configurator, 
            Action<IServiceCollectionTriggerConfigurator>? configure = null)
        {
            var c = new ServiceCollectionTriggerConfigurator(configurator.Services);
            configure?.Invoke(c);
            var trigger = c.Build();
            
            configurator.Services.AddTransient(x => trigger);
            
            return configurator;
        }
    }
}