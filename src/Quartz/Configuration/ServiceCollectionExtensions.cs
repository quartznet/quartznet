using System.Collections.Specialized;
using System.Data.Common;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.Configuration;
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
    /// Registers a Quartz scheduler.
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        Action<IQuartzBuilder>? configure = null)
    {
        return AddQuartzScheduler(services, schedulerName: null, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Registers a Quartz scheduler, seeded with flat <c>quartz.*</c> properties.
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(properties);
        return AddQuartzScheduler(services, schedulerName: null, properties, configure);
    }

    /// <summary>
    /// Registers a Quartz scheduler from a configuration section.
    /// </summary>
    /// <remarks>
    /// Hierarchical sections such as <c>Scheduler</c> and <c>ThreadPool</c> bind onto the typed options
    /// directly. Flat <c>quartz.*</c> keys are still accepted and mean the same thing. A
    /// <c>Schedulers</c> sub-section registers one named scheduler per child.
    /// </remarks>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var schedulers = configuration.GetSection("Schedulers");
        if (schedulers.Exists())
        {
            if (HasDirectSchedulerConfiguration(configuration))
            {
                Throw.SchedulerConfigException(
                    "The Quartz configuration section contains both a 'Schedulers' sub-section and direct " +
                    "scheduler configuration. Use one or the other.");
            }

            if (configuration.GetSection("Schedule").Exists() || configuration.GetSection("Scheduling").Exists())
            {
                Throw.SchedulerConfigException(
                    "The Quartz configuration section contains a 'Schedulers' sub-section and a top-level " +
                    "'Schedule' section. Jobs and triggers belong to a specific scheduler, so move them " +
                    "under the scheduler they belong to.");
            }

            foreach (var scheduler in schedulers.GetChildren())
            {
                AddQuartz(services, scheduler.Key, scheduler, configure);
            }

            return services;
        }

        // Bound before the callback runs, so configuration is the starting point and code overrides it.
        services.BindQuartzOptions(configuration);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, configuration, Options.DefaultName);
        AddQuartzScheduler(services, schedulerName: null, LegacyProperties(configuration), configure);
        return services;
    }

    /// <summary>
    /// Registers a named Quartz scheduler, so several independent schedulers can share a container.
    /// </summary>
    /// <remarks>
    /// The name is the scheduler's instance name, the key its components are registered under, and the
    /// name of its options, so its registrations and its configuration always agree.
    /// </remarks>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        return AddQuartzScheduler(services, name, new NameValueCollection(), configure);
    }

    /// <summary>
    /// Registers a named Quartz scheduler, seeded with flat <c>quartz.*</c> properties.
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(properties);
        return AddQuartzScheduler(services, name, properties, configure);
    }

    /// <summary>
    /// Registers a named Quartz scheduler from a configuration section.
    /// </summary>
    public static IServiceCollection AddQuartz(
        this IServiceCollection services,
        string name,
        IConfiguration configuration,
        Action<IQuartzBuilder>? configure = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(configuration);

        // Callers may pass either the scheduler's own section or the root section containing
        // "Schedulers:{name}", so resolve to whichever actually holds this scheduler's settings.
        var own = configuration.GetSection("Schedulers").GetSection(name);
        var effective = own.Exists() ? own : configuration;

        services.BindQuartzOptions(effective, name);
        JsonSchedulingHelper.ConfigureOptionsFromConfiguration(services, effective, name);
        AddQuartzScheduler(services, name, LegacyProperties(effective), configure);
        return services;
    }

    /// <summary>
    /// Uses the default type load helper, which resolves type names against loaded assemblies.
    /// </summary>
    public static IQuartzBuilder UseSimpleTypeLoader(this IQuartzBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        return builder.UseTypeLoader<SimpleTypeLoadHelper>();
    }

    /// <summary>
    /// The sections that bind onto typed options, and so must not also be flattened into legacy keys.
    /// </summary>
    private static readonly HashSet<string> typedSections = new(StringComparer.OrdinalIgnoreCase)
    {
        QuartzTypedOptions.SchedulerSection,
        QuartzTypedOptions.ThreadPoolSection,
        QuartzTypedOptions.JobStoreSection,
        QuartzTypedOptions.DataSourceSection,
    };

    /// <summary>
    /// Returns the properties a section contributes to the legacy string format.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Sections with typed options of their own are excluded, because they are bound directly and
    /// flattening them as well would send the same value through both readers — and the two disagree
    /// about form, a duration being <c>00:00:30</c> to the binder and a count of milliseconds to the
    /// legacy reader.
    /// </para>
    /// <para>
    /// Everything else is flattened. Plugins, serializers, listeners and execution limits have no typed
    /// options yet, so the legacy keys are the only thing that reads them; dropping those sections would
    /// mean a documented, valid <c>Quartz:Plugin:*</c> configuration was accepted and then ignored.
    /// </para>
    /// </remarks>
    /// <summary>
    /// The paths inside a typed section that select an implementation, or configure the thing selected,
    /// rather than setting a value on the options type.
    /// </summary>
    /// <remarks>
    /// These have no property to bind to, so excluding their section from flattening would leave nobody
    /// reading them at all. Turning a type name into a registration is the legacy adapter's job, and
    /// these are the keys that ask for it.
    /// </remarks>
    private static readonly (string Path, string LegacyPath)[] implementationPaths =
    [
        ("Scheduler:InstanceId", "scheduler.instanceId"),
        ("Scheduler:InstanceIdGenerator", "scheduler.instanceIdGenerator"),
        ("Scheduler:JobFactory:Type", "scheduler.jobFactory.type"),
        ("Scheduler:TypeLoadHelper:Type", "scheduler.typeLoadHelper.type"),
        ("ThreadPool:Type", "threadPool.type"),
        ("JobStore:Type", "jobStore.type"),
        ("JobStore:DriverDelegateType", "jobStore.driverDelegateType"),
        ("JobStore:LockHandler", "jobStore.lockHandler"),
    ];

    private static NameValueCollection LegacyProperties(IConfiguration configuration)
    {
        var properties = new NameValueCollection();
        QuartzConfigurationHelper.PopulateProperties(configuration, properties, typedSections);

        foreach (var (path, legacyPath) in implementationPaths)
        {
            var section = configuration.GetSection(path);
            if (section.Exists())
            {
                QuartzConfigurationHelper.FlattenInto(section, legacyPath, properties);
            }
        }

        return properties;
    }

    private static bool HasDirectSchedulerConfiguration(IConfiguration configuration)
    {
        foreach (var child in configuration.GetChildren())
        {
            if (string.Equals(child.Key, "Schedulers", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.Key, "Schedule", StringComparison.OrdinalIgnoreCase)
                || string.Equals(child.Key, "Scheduling", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Registers one scheduler: its services, its configuration, and whatever the caller adds to it.
    /// </summary>
    private static IServiceCollection AddQuartzScheduler(
        IServiceCollection services,
        string? schedulerName,
        NameValueCollection properties,
        Action<IQuartzBuilder>? configure)
    {
        services.AddOptions();

        var optionsName = schedulerName ?? Options.DefaultName;

        services.Configure<QuartzOptions>(optionsName, options =>
        {
            foreach (var key in properties.AllKeys)
            {
                if (key is not null)
                {
                    options[key] = properties[key];
                }
            }
        });

        // Legacy flat keys become typed options first, so a value set in the callback below is applied
        // last and therefore wins.
        QuartzPropertyBridge.ApplyOptions(services, properties, schedulerName);
        QuartzPropertyBridge.ApplyFromQuartzOptions(services, schedulerName);

        configure?.Invoke(new QuartzBuilder(services, schedulerName));

        // The implementations named by legacy keys go in after the callback, because registration is
        // first-wins: a job store chosen by UsePersistentStore has to beat a leftover
        // quartz.jobStore.type from an old configuration file. Code beats strings in both directions.
        QuartzPropertyBridge.ApplyRegistrations(services, properties, schedulerName);

        // Defaults go in last, so an explicitly configured job store, thread pool or serializer is not
        // beaten to the registration by the fallback it was meant to replace.
        services.AddQuartzScheduler(schedulerName);

        if (schedulerName is not null)
        {
            var registry = services
                .FirstOrDefault(d => d.ServiceType == typeof(SchedulerNameRegistry))?.ImplementationInstance
                as SchedulerNameRegistry;

            if (registry is null)
            {
                registry = new SchedulerNameRegistry();
                services.AddSingleton(registry);
            }

            registry.Add(schedulerName);
        }

        return services;
    }
    /// <summary>
    /// Add job to underlying service collection. This API maybe change!
    /// </summary>
    public static IQuartzBuilder AddJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
    T>(
        this IQuartzBuilder options,
        Action<IJobConfigurator> configure) where T : IJob
    {
        return options.AddJob<T>((_, jobConfigurator) => configure.Invoke(jobConfigurator));
    }

    /// <summary>
    /// Add job to underlying service collection. This API maybe change!
    /// </summary>
    public static IQuartzBuilder AddJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
    T>(
        this IQuartzBuilder options,
        Action<IServiceProvider, IJobConfigurator> configure) where T : IJob
    {
        return options.AddJob(typeof(T), null, configure);
    }

    /// <summary>
    /// Add job to underlying service collection. This API maybe change!
    /// </summary>
    public static IQuartzBuilder AddJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
    T>(
        this IQuartzBuilder options,
        JobKey? jobKey,
        Action<IJobConfigurator> configure) where T : IJob
    {
        return options.AddJob<T>(jobKey, (_, jobConfigurator) => configure.Invoke(jobConfigurator));
    }

    /// <summary>
    /// Add job to underlying service collection. This API maybe change!
    /// </summary>
    public static IQuartzBuilder AddJob<
            [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)]
    T>(
        this IQuartzBuilder options,
        JobKey? jobKey = null,
        Action<IServiceProvider, IJobConfigurator>? configure = null) where T : IJob
    {
        return options.AddJob(typeof(T), jobKey, configure);
    }

    /// <summary>
    /// Add job to underlying service collection.jobType shoud be implement `IJob`
    /// </summary>
    public static IQuartzBuilder AddJob(
        this IQuartzBuilder options,
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
    public static IQuartzBuilder AddJob(
        this IQuartzBuilder options,
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

        var optionsName = options.SchedulerName;
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
    public static IQuartzBuilder AddTrigger(
        this IQuartzBuilder options,
        Action<ITriggerConfigurator> configure)
    {
        return options.AddTrigger((_, triggerConfigurator) => configure.Invoke(triggerConfigurator));
    }

    /// <summary>
    /// Add trigger to underlying service collection. This API maybe change!
    /// </summary>
    public static IQuartzBuilder AddTrigger(
        this IQuartzBuilder options,
        Action<IServiceProvider, ITriggerConfigurator> configure)
    {
        var optionsName = options.SchedulerName;
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
    public static IQuartzBuilder ScheduleJob<T>(
        this IQuartzBuilder options,
        Action<ITriggerConfigurator> trigger,
        Action<IJobConfigurator>? job = null) where T : IJob
    {
        return options.ScheduleJob<T>((_, triggerConfigurator) => trigger(triggerConfigurator), (_, jobConfigurator) => job?.Invoke(jobConfigurator));
    }

    /// <summary>
    /// Schedule job with trigger to underlying service collection. This API maybe change!
    /// </summary>
    public static IQuartzBuilder ScheduleJob<T>(
        this IQuartzBuilder options,
        Action<IServiceProvider, ITriggerConfigurator> trigger,
        Action<IServiceProvider, IJobConfigurator>? job = null) where T : IJob
    {
        ArgumentNullException.ThrowIfNull(trigger);

        var optionsName = options.SchedulerName;
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

    public static IQuartzBuilder AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        this IQuartzBuilder configurator,
        string name,
        bool replace,
        bool updateTriggers,
        Action<T> configure) where T : ICalendar, new()
    {
        return configurator.AddCalendar<T>(name, replace, updateTriggers, (_, calendar) => configure(calendar));
    }

    public static IQuartzBuilder AddCalendar<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(
        this IQuartzBuilder configurator,
        string name,
        bool replace,
        bool updateTriggers,
        Action<IServiceProvider, T> configure) where T : ICalendar, new()
    {
        var optionsName = configurator.SchedulerName;
        configurator.Services.AddSingleton(serviceProvider =>
        {
            var calendar = new T();
            configure(serviceProvider, calendar);

            return new CalendarConfiguration(name, calendar, replace, updateTriggers, optionsName);
        });
        return configurator;
    }

    public static IQuartzBuilder AddCalendar(
        this IQuartzBuilder configurator,
        string name,
        ICalendar calendar,
        bool replace,
        bool updateTriggers)
    {
        configurator.Services.AddSingleton(new CalendarConfiguration(name, calendar, replace, updateTriggers, configurator.SchedulerName));
        return configurator;
    }

}