using System;
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
            return AddJob<T>(configurator, null, configure);
        }

        /// <summary>
        /// Add job to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddJob<T>(
            this IServiceCollectionQuartzConfigurator configurator,
            JobKey? jobKey = null,
            Action<IServiceCollectionJobConfigurator>? configure = null) where T : IJob
        {
            var c = new ServiceCollectionJobConfigurator(configurator.Services);
            if (jobKey != null)
            {
                c.WithIdentity(jobKey);
            }

            var jobDetail = ConfigureAndBuildJobDetail<T>(c, configure);

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

            if (trigger.JobKey is null)
            {
                throw new InvalidOperationException("Trigger hasn't been associated with a job");
            }

            configurator.Services.AddTransient(x => trigger);

            return configurator;
        }

        /// <summary>
        /// Schedule job with trigger to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator ScheduleJob<T>(
            this IServiceCollectionQuartzConfigurator configurator,
            Action<IServiceCollectionTriggerConfigurator> trigger,
            Action<IServiceCollectionJobConfigurator>? job = null) where T : IJob
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            var jobConfigurator = new ServiceCollectionJobConfigurator(configurator.Services);
            var jobDetail = ConfigureAndBuildJobDetail<T>(jobConfigurator, job);

            configurator.Services.AddTransient(x => jobDetail);
            configurator.Services.AddTransient(jobDetail.JobType);

            var triggerConfigurator = new ServiceCollectionTriggerConfigurator(configurator.Services);
            triggerConfigurator.ForJob(jobDetail);

            trigger.Invoke(triggerConfigurator);
            var t = triggerConfigurator.Build();

            if (t.JobKey is null || !t.JobKey.Equals(jobDetail.Key))
            {
                throw new InvalidOperationException("Trigger doesn't refer to job being scheduled");
            }

            configurator.Services.AddTransient(x => t);

            return configurator;
        }

        private static IJobDetail ConfigureAndBuildJobDetail<T>(
            ServiceCollectionJobConfigurator builder,
            Action<IServiceCollectionJobConfigurator>? configure) where T : IJob
        {
            builder.OfType<T>();
            configure?.Invoke(builder);
            var jobDetail = builder.Build();
            return jobDetail;
        }

        public static IServiceCollectionQuartzConfigurator AddCalendar<T>(
            this IServiceCollectionQuartzConfigurator configurator,
            string name,
            bool replace,
            bool updateTriggers,
            Action<T> configure) where T : ICalendar, new()
        {
            var calendar = new T();
            configure(calendar);
            configurator.Services.AddSingleton(new CalendarConfiguration(name, calendar, replace, updateTriggers));
            return configurator;
        }

        public static IServiceCollectionQuartzConfigurator AddCalendar(
            this IServiceCollectionQuartzConfigurator configurator,
            string name,
            ICalendar calendar,
            bool replace,
            bool updateTriggers)
        {
            configurator.Services.AddSingleton(new CalendarConfiguration(name, calendar, replace, updateTriggers));
            return configurator;
        }
    }
}