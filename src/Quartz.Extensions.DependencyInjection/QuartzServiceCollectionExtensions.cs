using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Quartz.Logging;

namespace Quartz
{
    public static class QuartzServiceCollectionExtensions
    {
        public static IServiceCollection AddQuartz(
            this IServiceCollection services,
            Action<ServiceCollectionSchedulerConfigurator>? configure = null)
        {
            services.TryAddSingleton(new QuartzConfiguration());
            services.AddSingleton<ISchedulerFactory>(serviceProvider =>
            {
                var builder = new ServiceCollectionSchedulerConfigurator(services);
                configure?.Invoke(builder);

                // check if logging provider configured and let if configure
                serviceProvider.GetService<LoggingProvider>();

                return new ServiceCollectionScheduler(serviceProvider, builder.Build());
            });
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
            services.TryAddSingleton(new QuartzConfiguration());

            var builder = JobBuilder.Create<T>();
            var configurator = new ServiceCollectionJobConfigurator(services, builder);
            configure?.Invoke(configurator);
            services.AddTransient(x => new QuartzJobRegistration(configurator));
            
            return services;
        }        

        /// <summary>
        /// Adds LibLog configuration to use Microsoft's logging abstraction instead of trying to find one.
        /// </summary>
        public static IServiceCollection AddQuartzTrigger<T>(this IServiceCollection services, Action<ServiceCollectionJobConfigurator>? configure = null) where T : IJob
        {
            services.TryAddSingleton(new QuartzConfiguration());

            var builder = JobBuilder.Create<T>();
            var configurator = new ServiceCollectionJobConfigurator(services, builder);
            configure?.Invoke(configurator);
            return services;
        }
    }
}