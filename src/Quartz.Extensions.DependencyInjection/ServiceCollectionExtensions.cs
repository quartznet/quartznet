using System;
using System.Collections.Specialized;
using System.Data.Common;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

using Quartz.Impl;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;
using Quartz.Util;

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
            services.AddOptions();
            services.TryAddSingleton<MicrosoftLoggingProvider>(serviceProvider =>
            {
                var loggerFactory = serviceProvider.GetService<ILoggerFactory>();

                if (loggerFactory is null)
                {
                    throw new InvalidOperationException($"{nameof(ILoggerFactory)} service is required");
                }

                LogContext.SetCurrentLogProvider(loggerFactory);

                return (LogProvider.CurrentLogProvider as MicrosoftLoggingProvider)!;
            });

            var schedulerBuilder = SchedulerBuilder.Create(properties);
            if (configure != null)
            {
                var target = new ServiceCollectionQuartzConfigurator(services, schedulerBuilder);
                configure(target);
            }

            services.AddSingleton<ISchedulerRepository>(new SchedulerRepository());

            // try to add services if not present with defaults, without overriding other configuration
            if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerTypeLoadHelperType]))
            {
                services.TryAddSingleton(typeof(ITypeLoadHelper), typeof(SimpleTypeLoadHelper));
            }

            if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerJobFactoryType]))
            {
                // there's no explicit job factory defined, use MS version
                properties[StdSchedulerFactory.PropertySchedulerJobFactoryType] = typeof(MicrosoftDependencyInjectionJobFactory).AssemblyQualifiedNameWithoutVersion();
                services.TryAddSingleton(typeof(IJobFactory), typeof(MicrosoftDependencyInjectionJobFactory));
            }

            services.Configure<QuartzOptions>(options =>
            {
                foreach (var key in schedulerBuilder.Properties.AllKeys)
                {
                    if (key is not null)
                    {
                        options[key] = schedulerBuilder.Properties[key];
                    }
                }
            });

            services.TryAddSingleton<ContainerConfigurationProcessor>();
            services.TryAddSingleton<ISchedulerFactory, ServiceCollectionSchedulerFactory>();

            // Note: TryAddEnumerable() is used here to ensure the initializers are registered only once.
            services.TryAddEnumerable(new[]
            {
                ServiceDescriptor.Singleton<IPostConfigureOptions<QuartzOptions>, QuartzConfiguration>()
            });

            return services;
        }

        /// <summary>
        /// Add job to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddJob<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
            T>(
            this IServiceCollectionQuartzConfigurator options,
            Action<IJobConfigurator>? configure = null) where T : IJob
        {
            return AddJob(options,typeof(T), null, configure);
        }

        /// <summary>
        /// Add job to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddJob<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
            T>(
            this IServiceCollectionQuartzConfigurator options,
            JobKey? jobKey = null,
            Action<IJobConfigurator>? configure = null) where T : IJob
        {
            return AddJob(options, typeof(T), jobKey, configure);
        }
        /// <summary>
        /// Add job to underlying service collection.jobType shoud be implement `IJob`
        /// </summary>
        public static IServiceCollectionQuartzConfigurator AddJob(
           this IServiceCollectionQuartzConfigurator options,
#if NET6_0_OR_GREATER
           [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
           Type jobType,
           JobKey? jobKey = null,
           Action<IJobConfigurator>? configure = null)
        {
            if (!typeof(IJob).IsAssignableFrom(jobType))
            {
                ExceptionHelper.ThrowArgumentException("jobType must implement the IJob interface", nameof(jobType));
            }
            var c = new JobConfigurator();
            if (jobKey != null)
            {
                c.WithIdentity(jobKey);
            }

            var jobDetail = ConfigureAndBuildJobDetail(jobType, c, configure, hasCustomKey: out _);

            options.Services.Configure<QuartzOptions>(x =>
            {
                x.jobDetails.Add(jobDetail);
            });

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
                x.triggers.Add(trigger);
            });

            return options;
        }

        /// <summary>
        /// Schedule job with trigger to underlying service collection. This API maybe change!
        /// </summary>
        public static IServiceCollectionQuartzConfigurator ScheduleJob<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
            T>(
            this IServiceCollectionQuartzConfigurator options,
            Action<ITriggerConfigurator> trigger,
            Action<IJobConfigurator>? job = null) where T : IJob
        {
            if (trigger is null)
            {
                throw new ArgumentNullException(nameof(trigger));
            }

            var jobConfigurator = new JobConfigurator();
            var jobDetail = ConfigureAndBuildJobDetail(typeof(T), jobConfigurator, job, out var jobHasCustomKey);

            options.Services.Configure<QuartzOptions>(quartzOptions =>
            {
                quartzOptions.jobDetails.Add(jobDetail);
            });

            var triggerConfigurator = new TriggerConfigurator();
            triggerConfigurator.ForJob(jobDetail);

            trigger.Invoke(triggerConfigurator);
            var t = triggerConfigurator.Build();

            // The job configurator is optional and omitted in most examples
            // If no job key was specified, have the job key match the trigger key
            if (!jobHasCustomKey)
            {
                ((JobDetailImpl)jobDetail).Key = new JobKey(t.Key.Name, t.Key.Group);

                // Keep ITrigger.JobKey in sync with IJobDetail.Key
                ((IMutableTrigger)t).JobKey = jobDetail.Key;
            }

            if (t.JobKey is null || !t.JobKey.Equals(jobDetail.Key))
            {
                throw new InvalidOperationException("Trigger doesn't refer to job being scheduled");
            }

            options.Services.Configure<QuartzOptions>(quartzOptions =>
            {
                quartzOptions.triggers.Add(t);
            });

            return options;
        }

        private static IJobDetail ConfigureAndBuildJobDetail(
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
            Type type,
            JobConfigurator builder,
            Action<IJobConfigurator>? configure,
            out bool hasCustomKey)
        {
            builder.OfType(type);
            configure?.Invoke(builder);
            hasCustomKey = builder.Key is not null;
            var jobDetail = builder.Build();
            return jobDetail;
        }

        public static IServiceCollectionQuartzConfigurator AddCalendar<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
            T>(
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

#if NET8_0_OR_GREATER
        /// <summary>
        /// Registers an <see cref="IDbProvider"/> that fetches connections from a <see cref="DbDataSource"/> within the container.
        /// Should be used with `UseDataSourceConnectionProvider` within a ADO.NET persistence store. />
        /// </summary>
        /// <param name="configurator"></param>
        /// <returns></returns>
        public static IServiceCollectionQuartzConfigurator AddDataSourceProvider(this IServiceCollectionQuartzConfigurator configurator)
        {
            configurator.Services.AddSingleton<IDbProvider, DataSourceDbProvider>();
            return configurator;
        }
#endif
    }
}