using System.Collections.Specialized;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

using Quartz.Configuration;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore.Common;
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
    /// Configures Quartz services to underlying service collection. This API maybe change!
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        NameValueCollection properties,
        Action<IServiceCollectionQuartzConfigurator>? configure = null)
    {
        services.AddOptions();

        var schedulerBuilder = SchedulerBuilder.Create(properties);
        if (configure is not null)
        {
            var target = new ServiceCollectionQuartzConfigurator(services, schedulerBuilder);
            configure(target);
        }

        services.TryAddSingleton<IDbConnectionManager, DBConnectionManager>();
        services.TryAddSingleton<ISchedulerRepository, SchedulerRepository>();

        // try to add services if not present with defaults, without overriding other configuration
        if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerTypeLoadHelperType]))
        {
            services.TryAddSingleton<ITypeLoadHelper, SimpleTypeLoadHelper>();
        }

        services.TryAddSingleton(TimeProvider.System);
        if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerJobFactoryType]))
        {
            // there's no explicit job factory defined, use MS version
            properties[StdSchedulerFactory.PropertySchedulerJobFactoryType] = typeof(MicrosoftDependencyInjectionJobFactory).AssemblyQualifiedNameWithoutVersion();
            services.TryAddSingleton<IJobFactory, MicrosoftDependencyInjectionJobFactory>();
        }

        services.Configure<QuartzOptions>(options =>
        {
            foreach (var key in schedulerBuilder.Properties.AllKeys)
            {
                if (key is null)
                {
                    continue;
                }
                options[key] = schedulerBuilder.Properties[key];
            }
        });

        services.TryAddSingleton<ContainerConfigurationProcessor>();
        services.TryAddSingleton<ISchedulerFactory, ServiceCollectionSchedulerFactory>();

        // Note: TryAddEnumerable() is used here to ensure the initializers are registered only once.
        services.TryAddEnumerable([
            ServiceDescriptor.Singleton<IPostConfigureOptions<QuartzOptions>, QuartzConfiguration>()
        ]);

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
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(properties);

        services.AddOptions();

        // Force the scheduler instance name to the provided name
        properties[StdSchedulerFactory.PropertySchedulerInstanceName] = name;

        var schedulerBuilder = SchedulerBuilder.Create(properties);
        if (configure is not null)
        {
            var target = new ServiceCollectionQuartzConfigurator(services, schedulerBuilder, name);
            configure(target);
        }

        // Re-force the scheduler instance name after configuration so it cannot drift
        // from the named options key / registry entry via SetProperty() or Properties[]
        schedulerBuilder.Properties[StdSchedulerFactory.PropertySchedulerInstanceName] = name;

        // Shared singletons -- safe to register multiple times via TryAdd
        services.TryAddSingleton<IDbConnectionManager, DBConnectionManager>();
        services.TryAddSingleton<ISchedulerRepository, SchedulerRepository>();

        if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerTypeLoadHelperType]))
        {
            services.TryAddSingleton<ITypeLoadHelper, SimpleTypeLoadHelper>();
        }

        services.TryAddSingleton(TimeProvider.System);
        if (string.IsNullOrWhiteSpace(properties[StdSchedulerFactory.PropertySchedulerJobFactoryType]))
        {
            properties[StdSchedulerFactory.PropertySchedulerJobFactoryType] = typeof(MicrosoftDependencyInjectionJobFactory).AssemblyQualifiedNameWithoutVersion();
            services.TryAddSingleton<IJobFactory, MicrosoftDependencyInjectionJobFactory>();
        }

        // Configure named options for this scheduler
        services.Configure<QuartzOptions>(name, options =>
        {
            foreach (var key in schedulerBuilder.Properties.AllKeys)
            {
                if (key is null)
                {
                    continue;
                }
                options[key] = schedulerBuilder.Properties[key];
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
            if (existing is not null)
            {
                services.Remove(existing);
            }
            SchedulerNameRegistry newRegistry = new();
            newRegistry.Add(name);
            services.AddSingleton(newRegistry);
        }

        // Note: ISchedulerFactory and ContainerConfigurationProcessor are NOT registered here.
        // Named schedulers are created by NamedSchedulerHostedService using NamedSchedulerFactory.

        services.TryAddEnumerable([
            ServiceDescriptor.Singleton<IPostConfigureOptions<QuartzOptions>, QuartzConfiguration>()
        ]);

        return services;
    }

    /// <summary>
    /// Add job to underlying service collection. This API maybe change!
    /// </summary>
    public static IServiceCollectionQuartzConfigurator AddJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
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
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
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
           [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
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
           [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type jobType,
        JobKey? jobKey = null,
        Action<IServiceProvider, IJobConfigurator>? configure = null)
    {
        if (!typeof(IJob).IsAssignableFrom(jobType))
        {
            Throw.ArgumentException("jobType must implement the IJob interface", nameof(jobType));
        }
        var c = JobBuilder.Create();
        if (jobKey is not null)
        {
            c.WithIdentity(jobKey);
        }

        var optionsName = options.OptionsName;
        options.Services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            var jobDetail = ConfigureAndBuildJobDetail(serviceProvider, jobType, c, configure, hasCustomKey: out _);

            return new ConfigureNamedOptions<QuartzOptions>(optionsName, x =>
            {
                x._jobDetails.Add(jobDetail);
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
                x._triggers.Add(trigger);
            });
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
        return options.ScheduleJob<T>((_, triggerConfigurator) => trigger(triggerConfigurator), (_, jobConfigurator) => job?.Invoke(jobConfigurator));
    }

    /// <summary>
    /// Schedule job with trigger to underlying service collection. This API maybe change!
    /// </summary>
    public static IServiceCollectionQuartzConfigurator ScheduleJob<T>(
        this IServiceCollectionQuartzConfigurator options,
        Action<IServiceProvider, ITriggerConfigurator> trigger,
        Action<IServiceProvider, IJobConfigurator>? job = null) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var optionsName = options.OptionsName;
        options.Services.AddSingleton<IConfigureOptions<QuartzOptions>>(serviceProvider =>
        {
            return new ConfigureNamedOptions<QuartzOptions>(optionsName, quartzOptions =>
            {
                var jobConfigurator = JobBuilder.Create();
                var jobDetail = ConfigureAndBuildJobDetail(serviceProvider, typeof(T), jobConfigurator, job, out var jobHasCustomKey);

                quartzOptions._jobDetails.Add(jobDetail);

                var triggerConfigurator = new TriggerConfigurator();
                triggerConfigurator.ForJob(jobDetail);

                trigger.Invoke(serviceProvider, triggerConfigurator);
                var t = triggerConfigurator.Build();

                // The job configurator is optional and omitted in most examples
                // If no job key was specified, have the job key match the trigger key
                if (!jobHasCustomKey)
                {
                    ((JobDetailImpl) jobDetail).Key = new JobKey(t.Key.Name, t.Key.Group);

                    // Keep ITrigger.JobKey in sync with IJobDetail.Key
                    ((IMutableTrigger) t).JobKey = jobDetail.Key;
                }

                if (t.JobKey is null || !t.JobKey.Equals(jobDetail.Key))
                {
                    Throw.InvalidOperationException("Trigger doesn't refer to job being scheduled");
                }

                quartzOptions._triggers.Add(t);
            });
        });

        return options;
    }

    private static IJobDetail ConfigureAndBuildJobDetail(
        IServiceProvider serviceProvider,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type type,
        JobBuilder builder,
        Action<IServiceProvider, IJobConfigurator>? configure,
        out bool hasCustomKey)
    {
        builder.OfType(type);
        configure?.Invoke(serviceProvider, builder);
        hasCustomKey = builder.Key is not null;
        var jobDetail = builder.Build();
        return jobDetail;
    }

    public static IServiceCollectionQuartzConfigurator AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        this IServiceCollectionQuartzConfigurator configurator,
        string name,
        bool replace,
        bool updateTriggers,
        Action<T> configure) where T : ICalendar, new()
    {
        return configurator.AddCalendar<T>(name, replace, updateTriggers, (_, calendar) => configure(calendar));
    }

    public static IServiceCollectionQuartzConfigurator AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
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
}