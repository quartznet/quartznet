﻿using System;
using System.Collections.Specialized;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Configures Quartz services to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollection AddQuartz(
            this IServiceCollection services,
            Action<IServiceCollectionQuartzConfigurator>? configure = null)
        {
            services.TryAddSingleton<MicrosoftLoggingProvider?>(serviceProvider =>
            {
                var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
                if (loggerFactory != null)
                {
                    LogContext.SetCurrentLogProvider(loggerFactory);
                }

                return LogProvider.CurrentLogProvider as MicrosoftLoggingProvider;
            });
            
            var builder = new ServiceCollectionQuartzConfigurator(services, SchedulerBuilder.Create());
            configure?.Invoke(builder);
            
            // Note that we can't call UseSimpleTypeLoader(), as that would overwrite any other configured type loaders
            services.TryAddSingleton(typeof(ITypeLoadHelper), typeof(SimpleTypeLoadHelper));
            
            services.AddSingleton<ISchedulerFactory>(serviceProvider =>
            {
                // try standard appsettings.json
                var config = serviceProvider.GetService<IConfiguration>();
                var section = config.GetSection("Quartz");
                var options = new NameValueCollection();

                foreach (var kvp in section.GetChildren())
                {
                    options.Set(kvp.Key, kvp.Value);
                }
                
                // now override with programmatic configuration
                foreach (string? key in builder.Properties.Keys)
                {
                    options.Set(key, builder.Properties[key]);
                }
                
                return new ServiceCollectionSchedulerFactory(serviceProvider, options);
            });
            return services;
        }        
        
        /// <summary>
        /// Configures Quartz services to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollection AddQuartz(
            this IServiceCollection services,
            NameValueCollection properties,
            Action<IServiceCollectionQuartzConfigurator>? configure = null)
        {
            var builder = new ServiceCollectionQuartzConfigurator(services, SchedulerBuilder.Create(properties));
            configure?.Invoke(builder);
            
            services.AddSingleton<ISchedulerFactory>(serviceProvider => new ServiceCollectionSchedulerFactory(serviceProvider, properties));
            return services;
        }        
        
        /// <summary>
        /// Add job to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddJob<T>(
            this IServiceCollectionQuartzConfigurator configurator,
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
        /// Add trigger to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddTrigger(
            this IServiceCollectionQuartzConfigurator configurator, 
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