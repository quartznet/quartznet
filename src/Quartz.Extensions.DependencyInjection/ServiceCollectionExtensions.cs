using System;
using System.Collections.Specialized;
using System.Data.Common;
using System.Linq;

using Microsoft.Extensions.Configuration;
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

namespace Quartz;

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
    /// Configures Quartz services from a hierarchical <see cref="IConfiguration"/> section (typically <c>Configuration.GetSection("Quartz")</c>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Nested JSON properties are automatically converted to flat Quartz property keys
    /// (e.g., <c>Scheduler:InstanceName</c> becomes <c>quartz.scheduler.instanceName</c>).
    /// A <c>Schedule</c> sub-section with <c>Jobs</c> and <c>Triggers</c> arrays is also supported.
    /// </para>
    /// <para>
    /// If a <c>Schedulers</c> sub-section is present, each child is registered as a named scheduler.
    /// If the section contains direct scheduler configuration (e.g., <c>Scheduler</c>, <c>ThreadPool</c>),
    /// a single default scheduler is registered. Defining both <c>Schedulers</c> and direct scheduler
    /// configuration is an error.
    /// </para>
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">The configuration section containing Quartz settings.</param>
    /// <param name="configure">Optional configuration action applied to each scheduler (or the default scheduler).</param>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IServiceCollectionQuartzConfigurator>? configure = null)
    {
        IConfigurationSection schedulersSection = configuration.GetSection("Schedulers");
        bool hasNamedSchedulers = schedulersSection.Exists();
        bool hasDirectConfig = HasDirectSchedulerConfiguration(configuration);

        if (hasNamedSchedulers && hasDirectConfig)
        {
            throw new SchedulerConfigException(
                "The Quartz configuration section contains both a 'Schedulers' sub-section and direct scheduler " +
                "configuration (for example, hierarchical sections like 'Scheduler' or 'ThreadPool', or legacy " +
                "flat 'quartz.*' keys such as 'quartz.scheduler.instanceName'). Use 'Schedulers' for named " +
                "schedulers or direct configuration for a single default scheduler, but not both.");
        }

        if (hasNamedSchedulers)
        {
            foreach (IConfigurationSection child in schedulersSection.GetChildren())
            {
                AddQuartz(services, child.Key, child, configure);
            }
            return services;
        }

        // Default single scheduler
        NameValueCollection properties = QuartzConfigurationHelper.ToNameValueCollection(configuration);
        AddQuartz(services, properties, configure);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, configuration);
        return services;
    }

    private static bool HasDirectSchedulerConfiguration(IConfiguration configuration)
    {
        // Any key that is not a reserved section name is treated as direct scheduler configuration.
        foreach (IConfigurationSection child in configuration.GetChildren())
        {
            string key = child.Key;

            // Skip reserved sections that are not direct scheduler configuration
            if (string.Equals(key, "Schedulers", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Schedule", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(key, "Scheduling", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Configures a named Quartz scheduler from a hierarchical <see cref="IConfiguration"/> section.
    /// Nested JSON properties are automatically converted to flat Quartz property keys.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Unique scheduler name, used as both the scheduler instance name and the options key.</param>
    /// <param name="configuration">The configuration section containing Quartz settings.</param>
    /// <param name="configure">Optional configuration action for jobs, triggers, listeners, etc.</param>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IServiceCollectionQuartzConfigurator>? configure = null)
    {
        NameValueCollection properties = QuartzConfigurationHelper.ToNameValueCollection(configuration);
        AddQuartz(services, name, properties, configure);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, configuration, name);
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

        services.TryAddSingleton<ISchedulerRepository, SchedulerRepository>();
        services.TryAddSingleton<IDbConnectionManager, DBConnectionManager>();

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
    /// Configures a named Quartz scheduler, allowing multiple independent schedulers in a single DI container.
    /// Each named scheduler has its own jobs, triggers, listeners, and configuration.
    /// Use <c>AddQuartzHostedService</c> to start all registered schedulers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Unique scheduler name, used as both the scheduler instance name and the options key.</param>
    /// <param name="configure">Optional configuration action for jobs, triggers, listeners, etc.</param>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        Action<IServiceCollectionQuartzConfigurator>? configure = null)
    {
        return AddQuartz(services, name, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Configures a named Quartz scheduler with explicit properties, allowing multiple independent schedulers in a single DI container.
    /// Each named scheduler has its own jobs, triggers, listeners, and configuration.
    /// Use <c>AddQuartzHostedService</c> to start all registered schedulers.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="name">Unique scheduler name, used as both the scheduler instance name and the options key.</param>
    /// <param name="properties">Quartz configuration properties for this scheduler.</param>
    /// <param name="configure">Optional configuration action for jobs, triggers, listeners, etc.</param>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        NameValueCollection properties,
        Action<IServiceCollectionQuartzConfigurator>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Scheduler name must not be null or empty.", nameof(name));
        }

        if (properties is null)
        {
            throw new ArgumentNullException(nameof(properties));
        }

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

        // Force the scheduler instance name to the provided name
        properties[StdSchedulerFactory.PropertySchedulerInstanceName] = name;

        SchedulerBuilder schedulerBuilder = SchedulerBuilder.Create(properties);
        if (configure != null)
        {
            ServiceCollectionQuartzConfigurator target = new ServiceCollectionQuartzConfigurator(services, schedulerBuilder, name);
            configure(target);
        }

        // Re-force the scheduler instance name after configuration so it cannot drift
        // from the named options key / registry entry via SetProperty() or Properties[]
        schedulerBuilder.Properties[StdSchedulerFactory.PropertySchedulerInstanceName] = name;

        // Shared singletons -- safe to register multiple times via TryAdd
        services.TryAddSingleton<ISchedulerRepository, SchedulerRepository>();
        services.TryAddSingleton<IDbConnectionManager, DBConnectionManager>();

        if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerTypeLoadHelperType]))
        {
            services.TryAddSingleton(typeof(ITypeLoadHelper), typeof(SimpleTypeLoadHelper));
        }

        if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerJobFactoryType]))
        {
            properties[StdSchedulerFactory.PropertySchedulerJobFactoryType] = typeof(MicrosoftDependencyInjectionJobFactory).AssemblyQualifiedNameWithoutVersion();
            services.TryAddSingleton(typeof(IJobFactory), typeof(MicrosoftDependencyInjectionJobFactory));
        }

        // Configure named options for this scheduler
        services.Configure<QuartzOptions>(name, options =>
        {
            foreach (var key in schedulerBuilder.Properties.AllKeys)
            {
                if (key is not null)
                {
                    options[key] = schedulerBuilder.Properties[key];
                }
            }
        });

        // Register this scheduler name in the registry
        ServiceDescriptor? existing = services.FirstOrDefault(d => d.ServiceType == typeof(SchedulerNameRegistry));
        if (existing?.ImplementationInstance is SchedulerNameRegistry existingRegistry)
        {
            existingRegistry.Add(name);
        }
        else
        {
            if (existing != null)
            {
                services.Remove(existing);
            }
            SchedulerNameRegistry newRegistry = new SchedulerNameRegistry();
            newRegistry.Add(name);
            services.AddSingleton(newRegistry);
        }

        // Note: ISchedulerFactory and ContainerConfigurationProcessor are NOT registered here.
        // Named schedulers are created by NamedSchedulerHostedService using NamedSchedulerFactory.

        services.TryAddEnumerable(new[]
        {
            ServiceDescriptor.Singleton<IPostConfigureOptions<QuartzOptions>, QuartzConfiguration>()
        });

        return services;
    }

    /// <summary>
    /// Configures Quartz services with access to <see cref="IServiceProvider"/>.
    /// The configuration delegate is deferred until the service provider is available,
    /// allowing resolution of registered services (e.g. to obtain connection strings).
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        Action<IServiceCollectionQuartzConfigurator, IServiceProvider> configure)
    {
        return AddQuartz(services, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Configures Quartz services with initial properties and access to <see cref="IServiceProvider"/>.
    /// The configuration delegate is deferred until the service provider is available,
    /// allowing resolution of registered services (e.g. to obtain connection strings).
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        NameValueCollection properties,
        Action<IServiceCollectionQuartzConfigurator, IServiceProvider> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        // Register core services via the base overload (idempotent due to TryAdd)
        services.AddQuartz(properties);

        // Register the deferred configuration — runs when IOptions<QuartzOptions>.Value is accessed
        services.AddSingleton<IConfigureOptions<QuartzOptions>>(sp =>
            new DeferredQuartzConfiguration(sp, services, configure, optionsName: ""));

        return services;
    }

    /// <summary>
    /// Configures a named Quartz scheduler with access to <see cref="IServiceProvider"/>.
    /// The configuration delegate is deferred until the service provider is available,
    /// allowing resolution of registered services (e.g. to obtain connection strings).
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        Action<IServiceCollectionQuartzConfigurator, IServiceProvider> configure)
    {
        return AddQuartz(services, name, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Configures a named Quartz scheduler with explicit properties and access to <see cref="IServiceProvider"/>.
    /// The configuration delegate is deferred until the service provider is available,
    /// allowing resolution of registered services (e.g. to obtain connection strings).
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        NameValueCollection properties,
        Action<IServiceCollectionQuartzConfigurator, IServiceProvider> configure)
    {
        if (configure is null)
        {
            throw new ArgumentNullException(nameof(configure));
        }

        // Register core services via the base named overload (idempotent due to TryAdd)
        services.AddQuartz(name, properties);

        // Register the deferred configuration — runs when IOptionsMonitor<QuartzOptions>.Get(name) is accessed
        services.AddSingleton<IConfigureOptions<QuartzOptions>>(sp =>
            new DeferredQuartzConfiguration(sp, services, configure, optionsName: name));

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
        Action<IJobConfigurator> configure) where T : IJob
    {
        return options.AddJob<T>((_, jobConfigurator) => configure.Invoke(jobConfigurator));
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
        Action<IServiceProvider, IJobConfigurator> configure) where T : IJob
    {
        return options.AddJob(typeof(T), null, configure);
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
        JobKey? jobKey,
        Action<IJobConfigurator> configure) where T : IJob
    {
        return options.AddJob<T>(jobKey, (_, jobConfigurator) => configure.Invoke(jobConfigurator));
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
        Action<IServiceProvider, IJobConfigurator>? configure = null) where T : IJob
    {
        return options.AddJob(typeof(T), jobKey, configure);
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
        JobKey? jobKey,
        Action<IJobConfigurator> configure)
    {
        return options.AddJob(jobType, jobKey, (_, jobConfigurator) => configure.Invoke(jobConfigurator));
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
        Action<IServiceProvider, IJobConfigurator>? configure = null)
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

        var optionsName = options.OptionsName;
        options.Services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            var jobDetail = ConfigureAndBuildJobDetail(serviceProvider, jobType, c, configure, hasCustomKey: out _);

            return new ConfigureNamedOptions<QuartzOptions>(optionsName, x =>
            {
                x.jobDetails.Add(jobDetail);
            });
        });

        return options;
    }


    /// <summary>
    /// Add trigger to underlying service collection. This API maybe change!
    /// </summary>
    public static IServiceCollectionQuartzConfigurator AddTrigger(
        this IServiceCollectionQuartzConfigurator options,
        Action<ITriggerConfigurator> configure)
    {
        return options.AddTrigger((_, triggerConfigurator) => configure.Invoke(triggerConfigurator));
    }

    /// <summary>
    /// Add trigger to underlying service collection. This API maybe change!
    /// </summary>
    public static IServiceCollectionQuartzConfigurator AddTrigger(
        this IServiceCollectionQuartzConfigurator options,
        Action<IServiceProvider, ITriggerConfigurator> configure)
    {
        var optionsName = options.OptionsName;
        options.Services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            var c = new TriggerConfigurator();
            configure.Invoke(serviceProvider, c);
            var trigger = c.Build();

            if (trigger.JobKey is null)
            {
                throw new InvalidOperationException("Trigger hasn't been associated with a job");
            }

            return new ConfigureNamedOptions<QuartzOptions>(optionsName, x =>
            {
                x.triggers.Add(trigger);
            });
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
        return options.ScheduleJob<T>((_, triggerConfigurator) => trigger(triggerConfigurator), (_, jobConfigurator) => job?.Invoke(jobConfigurator));
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
        Action<IServiceProvider, ITriggerConfigurator> trigger,
        Action<IServiceProvider, IJobConfigurator>? job = null) where T : IJob
    {
        if (trigger is null)
        {
            throw new ArgumentNullException(nameof(trigger));
        }

        var optionsName = options.OptionsName;
        options.Services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            return new ConfigureNamedOptions<QuartzOptions>(optionsName, quartzOptions =>
            {
                var jobConfigurator = new JobConfigurator();
                var jobDetail = ConfigureAndBuildJobDetail(serviceProvider, typeof(T), jobConfigurator, job, out var jobHasCustomKey);

                quartzOptions.jobDetails.Add(jobDetail);

                var triggerConfigurator = new TriggerConfigurator();
                triggerConfigurator.ForJob(jobDetail);

                trigger.Invoke(serviceProvider, triggerConfigurator);
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

                quartzOptions.triggers.Add(t);
            });
        });

        return options;
    }

    private static IJobDetail ConfigureAndBuildJobDetail(
        IServiceProvider serviceProvider,
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        Type type,
        JobConfigurator builder,
        Action<IServiceProvider, IJobConfigurator>? configure,
        out bool hasCustomKey)
    {
        builder.OfType(type);
        configure?.Invoke(serviceProvider, builder);
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
        return configurator.AddCalendar<T>(name, replace, updateTriggers, (_, calendar) => configure(calendar));
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
        Action<IServiceProvider, T> configure) where T : ICalendar, new()
    {
        var optionsName = configurator.OptionsName;
        configurator.Services.AddSingleton(serviceProvider =>
        {
            var calendar = new T();
            configure(serviceProvider, calendar);

            return new CalendarConfiguration(name, calendar, replace, updateTriggers, optionsName);
        });
        return configurator;
    }

    public static IServiceCollectionQuartzConfigurator AddCalendar(
        this IServiceCollectionQuartzConfigurator configurator,
        string name,
        ICalendar calendar,
        bool replace,
        bool updateTriggers)
    {
        configurator.Services.AddSingleton(new CalendarConfiguration(name, calendar, replace, updateTriggers, configurator.OptionsName));
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