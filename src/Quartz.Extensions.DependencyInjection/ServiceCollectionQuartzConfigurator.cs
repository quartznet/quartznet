using System;
using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz;

internal sealed class ServiceCollectionQuartzConfigurator : IServiceCollectionQuartzConfigurator, IContainerConfigurationSupport
{
    private readonly IServiceCollection services;
    private readonly SchedulerBuilder schedulerBuilder;
    private readonly string optionsName;

    internal ServiceCollectionQuartzConfigurator(
        IServiceCollection collection,
        SchedulerBuilder schedulerBuilder,
        string? optionsName = null)
    {
        services = collection;
        this.schedulerBuilder = schedulerBuilder;
        this.optionsName = optionsName ?? "";
    }

    IServiceCollection IServiceCollectionQuartzConfigurator.Services => services;

    string IServiceCollectionQuartzConfigurator.OptionsName => optionsName;

    private bool IsNamedScheduler => optionsName.Length > 0;

    public void UseSimpleTypeLoader()
    {
        UseTypeLoader<SimpleTypeLoadHelper>();
    }

    public void SetProperty(string name, string value)
    {
        schedulerBuilder.SetProperty(name, value);
    }

    public NameValueCollection Properties => schedulerBuilder.Properties;

    public string SchedulerId
    {
        set => schedulerBuilder.SchedulerId = value;
    }

    public string SchedulerName
    {
        set
        {
            if (IsNamedScheduler)
            {
                throw new InvalidOperationException(
                    $"SchedulerName cannot be changed for a named scheduler. The name '{optionsName}' was set when calling AddQuartz(\"{optionsName}\", ...).");
            }
            schedulerBuilder.SchedulerName = value;
        }
    }

    public bool InterruptJobsOnShutdown
    {
        set => schedulerBuilder.InterruptJobsOnShutdown = value;
    }

    public bool InterruptJobsOnShutdownWithWait
    {
        set => schedulerBuilder.InterruptJobsOnShutdownWithWait = value;
    }

    public int MaxBatchSize
    {
        set => schedulerBuilder.MaxBatchSize = value;
    }

    public TimeSpan BatchTriggerAcquisitionFireAheadTimeWindow
    {
        set => schedulerBuilder.BatchTriggerAcquisitionFireAheadTimeWindow = value;
    }

    public bool CheckConfiguration
    {
        set => schedulerBuilder.CheckConfiguration = value;
    }

    public void UseInMemoryStore(Action<SchedulerBuilder.InMemoryStoreOptions>? configure = null)
    {
        schedulerBuilder.UseInMemoryStore(configure);
    }

    public void UsePersistentStore(Action<SchedulerBuilder.PersistentStoreOptions> configure)
    {
        schedulerBuilder.UsePersistentStore(configure);
    }

    public void UsePersistentStore<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(Action<SchedulerBuilder.PersistentStoreOptions> configure) where T : class, IJobStore
    {
        // Named schedulers get their own IJobStore from StdSchedulerFactory via properties;
        // only register the global DI singleton for the default scheduler
        if (!IsNamedScheduler)
        {
            services.AddSingleton<IJobStore, T>();
        }
        schedulerBuilder.UsePersistentStore<T>(configure);
    }

    [Obsolete("MicrosoftDependencyInjectionJobFactory is the default for DI configuration, this method will be removed later on")]
    public void UseMicrosoftDependencyInjectionJobFactory(Action<JobFactoryOptions>? configure = null)
    {
        UseJobFactory<MicrosoftDependencyInjectionJobFactory>(configure);
    }

    public void UseJobFactory<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(Action<JobFactoryOptions>? configure = null) where T : class, IJobFactory
    {
        schedulerBuilder.UseJobFactory<T>();
        // Named schedulers configure job factories via properties; only replace the global
        // DI singleton for the default scheduler to avoid cross-scheduler side effects
        if (!IsNamedScheduler)
        {
            services.Replace(ServiceDescriptor.Singleton(typeof(IJobFactory), typeof(T)));
        }
        if (configure != null)
        {
            services.Configure<QuartzOptions>(optionsName, options =>
            {
                configure(options.JobFactory);
            });
        }
    }

    public void UseTypeLoader<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>() where T : ITypeLoadHelper
    {
        schedulerBuilder.UseTypeLoader<T>();
        // Named schedulers configure type loaders via properties; only replace the global
        // DI singleton for the default scheduler to avoid cross-scheduler side effects
        if (!IsNamedScheduler)
        {
            services.Replace(ServiceDescriptor.Singleton(typeof(ITypeLoadHelper), typeof(T)));
        }
    }

    public void UseThreadPool<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null) where T : class, IThreadPool
    {
        schedulerBuilder.UseThreadPool<T>(configure);
    }

    public void UseDefaultThreadPool(int maxConcurrency, Action<SchedulerBuilder.ThreadPoolOptions>? configure = null)
    {
        schedulerBuilder.UseDefaultThreadPool(options =>
        {
            options.MaxConcurrency = maxConcurrency;
            configure?.Invoke(options);
        });
    }

    public void UseDefaultThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null)
    {
        schedulerBuilder.UseDefaultThreadPool(configure);
    }

    public void UseZeroSizeThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null)
    {
        schedulerBuilder.UseZeroSizeThreadPool(configure);
    }

    public void UseDedicatedThreadPool(int maxConcurrency, Action<SchedulerBuilder.ThreadPoolOptions>? configure = null)
    {
        schedulerBuilder.UseDedicatedThreadPool(options =>
        {
            options.MaxConcurrency = maxConcurrency;
            configure?.Invoke(options);
        });
    }

    public void UseDedicatedThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null)
    {
        schedulerBuilder.UseDedicatedThreadPool(configure);
    }

    public TimeSpan MisfireThreshold
    {
        set => schedulerBuilder.MisfireThreshold = value;
    }

    public void AddSchedulerListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>() where T : class, ISchedulerListener
    {
        if (IsNamedScheduler)
        {
            services.AddSingleton(new SchedulerListenerConfiguration(typeof(T), optionsName));
        }
        else
        {
            services.AddSingleton<ISchedulerListener, T>();
        }
    }

    public void AddSchedulerListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(T implementationInstance) where T : class, ISchedulerListener
    {
        if (IsNamedScheduler)
        {
            services.AddSingleton(new SchedulerListenerConfiguration(typeof(T), optionsName, listenerInstance: implementationInstance));
        }
        else
        {
            services.AddSingleton<ISchedulerListener>(implementationInstance);
        }
    }

    public void AddSchedulerListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(Func<IServiceProvider, T> implementationFactory) where T : class, ISchedulerListener
    {
        if (IsNamedScheduler)
        {
            services.AddSingleton(new SchedulerListenerConfiguration(typeof(T), optionsName, listenerFactory: sp => implementationFactory(sp)));
        }
        else
        {
            services.AddSingleton<ISchedulerListener>(implementationFactory);
        }
    }

    public void AddJobListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers, optionsName));
        if (!IsNamedScheduler)
        {
            services.AddSingleton<IJobListener, T>();
        }
    }

    public void AddJobListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(T implementationInstance, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers, optionsName, listenerInstance: implementationInstance));
        if (!IsNamedScheduler)
        {
            services.AddSingleton<IJobListener>(implementationInstance);
        }
    }

    public void AddJobListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(Func<IServiceProvider, T> implementationFactory, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers, optionsName, listenerFactory: sp => implementationFactory(sp)));
        if (!IsNamedScheduler)
        {
            services.AddSingleton<IJobListener>(implementationFactory);
        }
    }

    public void AddTriggerListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers, optionsName));
        if (!IsNamedScheduler)
        {
            services.AddSingleton<ITriggerListener, T>();
        }
    }

    public void AddTriggerListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(T implementationInstance, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers, optionsName, listenerInstance: implementationInstance));
        if (!IsNamedScheduler)
        {
            services.AddSingleton<ITriggerListener>(implementationInstance);
        }
    }

    public void AddTriggerListener<
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        T>(Func<IServiceProvider, T> implementationFactory, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers, optionsName, listenerFactory: sp => implementationFactory(sp)));
        if (!IsNamedScheduler)
        {
            services.AddSingleton<ITriggerListener>(implementationFactory);
        }
    }

    public void RegisterSingleton<
        TService,
#if NET6_0_OR_GREATER
            [System.Diagnostics.CodeAnalysis.DynamicallyAccessedMembers(System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicConstructors | System.Diagnostics.CodeAnalysis.DynamicallyAccessedMemberTypes.PublicMethods)]
#endif
        TImplementation>()
        where TService : class
        where TImplementation : class, TService
    {

        services.AddSingleton<TService, TImplementation>();
    }
}