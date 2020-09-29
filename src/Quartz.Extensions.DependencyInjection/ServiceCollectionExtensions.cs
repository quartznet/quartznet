using System;
using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

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
            return AddQuartz(services, new NameValueCollection(), configure);
        }

        /// <summary>
        /// Configures Quartz services to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollection AddQuartz(
            this IServiceCollection services,
            NameValueCollection properties,
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
            
            // Note that we can't call UseSimpleTypeLoader(), as that would overwrite any other configured type loaders
            services.TryAddSingleton(typeof(ITypeLoadHelper), typeof(SimpleTypeLoadHelper));

            var schedulerBuilder = SchedulerBuilder.Create(properties);
            if (configure != null)
            {
                var target = new ServiceCollectionQuartzConfigurator(services, schedulerBuilder);
                configure(target);
            }
            
            services.Configure<QuartzOptions>(options =>
            {
                foreach (var key in schedulerBuilder.Properties.AllKeys)
                {
                    options[key] = schedulerBuilder.Properties[key];
                }
            });
            
            services.AddSingleton<ContainerConfigurationProcessor>();
            services.AddSingleton<IPostConfigureOptions<QuartzOptions>, QuartzConfiguration>();
            services.AddSingleton<ISchedulerFactory, ServiceCollectionSchedulerFactory>();

            return services;
        }

        /// <summary>
        /// Add job to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddJob<T>(
            this IServiceCollectionQuartzConfigurator options,
            Action<IJobConfigurator>? configure = null) where T : IJob
        {
            return AddJob<T>(options, null, configure);
        }

        /// <summary>
        /// Add job to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddJob<T>(
            this IServiceCollectionQuartzConfigurator options,
            JobKey? jobKey = null,
            Action<IJobConfigurator>? configure = null) where T : IJob
        {
            var c = new JobConfigurator();
            if (jobKey != null)
            {
                c.WithIdentity(jobKey);
            }

            var jobDetail = ConfigureAndBuildJobDetail<T>(c, configure);
            
            options.Services.Configure<QuartzOptions>(x =>
            {
                x.JobDetails.Add(jobDetail);
            });
            options.Services.TryAddTransient(jobDetail.JobType);

            return options;
        }

        /// <summary>
        /// Add trigger to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddTrigger(
            this IServiceCollectionQuartzConfigurator options,
            Action<ITriggerConfigurator>? configure = null)
        {
            var c = new TriggerConfigurator();
            configure?.Invoke(c);
            var trigger = c.Build();

            if (trigger.JobKey is null)
            {
                throw new InvalidOperationException("Trigger hasn't been associated with a job");
            }

            options.Services.Configure<QuartzOptions>(x =>
            {
                x.Triggers.Add(trigger);
            });

            return options;
        }

        /// <summary>
        /// Schedule job with trigger to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator ScheduleJob<T>(
            this IServiceCollectionQuartzConfigurator options,
            Action<ITriggerConfigurator> trigger,
            Action<IJobConfigurator>? job = null) where T : IJob
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            var jobConfigurator = new JobConfigurator();
            var jobDetail = ConfigureAndBuildJobDetail<T>(jobConfigurator, job);

            options.Services.Configure<QuartzOptions>(quartzOptions =>
            {
                quartzOptions.JobDetails.Add(jobDetail);
            });
            
            options.Services.TryAddTransient(jobDetail.JobType);

            var triggerConfigurator = new TriggerConfigurator();
            triggerConfigurator.ForJob(jobDetail);

            trigger.Invoke(triggerConfigurator);
            var t = triggerConfigurator.Build();

            if (t.JobKey is null || !t.JobKey.Equals(jobDetail.Key))
            {
                throw new InvalidOperationException("Trigger doesn't refer to job being scheduled");
            }

            options.Services.Configure<QuartzOptions>(quartzOptions =>
            {
                quartzOptions.Triggers.Add(t);
            });
            
            return options;
        }

        private static IJobDetail ConfigureAndBuildJobDetail<T>(
            JobConfigurator builder,
            Action<IJobConfigurator>? configure) where T : IJob
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