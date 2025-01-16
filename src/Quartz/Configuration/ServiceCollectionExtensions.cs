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
            services.TryAddSingleton<IJobFactory,MicrosoftDependencyInjectionJobFactory>();
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
        services.TryAddEnumerable(new[]
        {
            ServiceDescriptor.Singleton<IPostConfigureOptions<QuartzOptions>, QuartzConfiguration>()
        });

        return services;
    }

    /// <summary>
    /// Add job to underlying service collection. This API maybe change!
    /// </summary>
    public static IServiceCollectionQuartzConfigurator AddJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        this IServiceCollectionQuartzConfigurator options,
        Action<IJobConfigurator>? configure = null) where T : IJob
    {
        return AddJob(options, typeof(T), null, configure);
    }

    /// <summary>
    /// Add job to underlying service collection. This API maybe change!
    /// </summary>
    public static IServiceCollectionQuartzConfigurator AddJob<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        this IServiceCollectionQuartzConfigurator options,
        JobKey? jobKey = null,
        Action<IJobConfigurator>? configure = null) where T : IJob
    {
        return AddJob(options, typeof(T), jobKey, configure);
    }
    /// <summary>
    /// Add job to underlying service collection.jobType should be implement `IJob`
    /// </summary>
    public static IServiceCollectionQuartzConfigurator AddJob(
        this IServiceCollectionQuartzConfigurator options,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type jobType,
        JobKey? jobKey = null,
        Action<IJobConfigurator>? configure = null)
    {
        if (!typeof(IJob).IsAssignableFrom(jobType))
        {
            ThrowHelper.ThrowArgumentException("jobType must implement the IJob interface", nameof(jobType));
        }
        var c = JobBuilder.Create();
        if (jobKey is not null)
        {
            c.WithIdentity(jobKey);
        }

        var jobDetail = ConfigureAndBuildJobDetail(jobType, c, configure, hasCustomKey: out _);

        options.Services.Configure<QuartzOptions>(x =>
        {
            x._jobDetails.Add(jobDetail);
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
            ThrowHelper.ThrowInvalidOperationException("Trigger hasn't been associated with a job");
        }

        options.Services.Configure<QuartzOptions>(x =>
        {
            x._triggers.Add(trigger);
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
        ArgumentNullException.ThrowIfNull(trigger);

        var jobConfigurator = JobBuilder.Create();
        var jobDetail = ConfigureAndBuildJobDetail(typeof(T), jobConfigurator, job, out var jobHasCustomKey);

        options.Services.Configure<QuartzOptions>(quartzOptions =>
        {
            quartzOptions._jobDetails.Add(jobDetail);
        });

        var triggerConfigurator = new TriggerConfigurator();
        triggerConfigurator.ForJob(jobDetail);

        trigger.Invoke(triggerConfigurator);
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
            ThrowHelper.ThrowInvalidOperationException("Trigger doesn't refer to job being scheduled");
        }

        options.Services.Configure<QuartzOptions>(quartzOptions =>
        {
            quartzOptions._triggers.Add(t);
        });

        return options;
    }

    private static IJobDetail ConfigureAndBuildJobDetail(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
        Type type,
        JobBuilder builder,
        Action<IJobConfigurator>? configure,
        out bool hasCustomKey)
    {
        builder.OfType(type);
        configure?.Invoke(builder);
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