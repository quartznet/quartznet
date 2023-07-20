using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

using Quartz.Logging;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Configuration;

internal sealed class ServiceCollectionQuartzConfigurator : IServiceCollectionQuartzConfigurator
{
    private readonly IServiceCollection services;
    private readonly SchedulerBuilder schedulerBuilder;

    internal ServiceCollectionQuartzConfigurator(
        IServiceCollection collection,
        SchedulerBuilder schedulerBuilder)
    {
        services = collection;
        this.schedulerBuilder = schedulerBuilder;
    }

    IServiceCollection IServiceCollectionQuartzConfigurator.Services => services;

    public void UseSimpleTypeLoader()
    {
        UseTypeLoader<SimpleTypeLoadHelper>();
    }

    public void SetLoggerFactory(ILoggerFactory loggerFactory)
    {
        LogProvider.SetLogProvider(loggerFactory);
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
        set => schedulerBuilder.SchedulerName = value;
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

    public void UseInMemoryStore(Action<SchedulerBuilder.InMemoryStoreOptions>? configure = null)
    {
        schedulerBuilder.UseInMemoryStore(configure);
    }

    public void UsePersistentStore(Action<SchedulerBuilder.PersistentStoreOptions> configure)
    {
        schedulerBuilder.UsePersistentStore(configure);
    }

    public void UsePersistentStore<T>(Action<SchedulerBuilder.PersistentStoreOptions> configure) where T : class, IJobStore
    {
        services.AddSingleton<IJobStore, T>();
        schedulerBuilder.UsePersistentStore<T>(configure);
    }

    public void UseJobFactory<T>(Action<JobFactoryOptions>? configure = null) where T : class, IJobFactory
    {
        schedulerBuilder.UseJobFactory<T>();
        services.Replace(ServiceDescriptor.Singleton(typeof(IJobFactory), typeof(T)));
        if (configure != null)
        {
            services.Configure<QuartzOptions>(options =>
            {
                configure(options.JobFactory);
            });
        }
    }

    public void UseTypeLoader<T>() where T : ITypeLoadHelper
    {
        schedulerBuilder.UseTypeLoader<T>();
        services.Replace(ServiceDescriptor.Singleton(typeof(ITypeLoadHelper), typeof(T)));
    }

    public void UseThreadPool<T>(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null) where T : class, IThreadPool
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

    public void AddSchedulerListener<T>() where T : class, ISchedulerListener
    {
        services.AddSingleton<ISchedulerListener, T>();
    }

    public void AddSchedulerListener<T>(T implementationInstance) where T : class, ISchedulerListener
    {
        services.AddSingleton<ISchedulerListener>(implementationInstance);
    }

    public void AddSchedulerListener<T>(Func<IServiceProvider, T> implementationFactory) where T : class, ISchedulerListener
    {
        services.AddSingleton<ISchedulerListener>(implementationFactory);
    }

    public void AddJobListener<T>(params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers));
        services.AddSingleton<IJobListener, T>();
    }

    public void AddJobListener<T>(T implementationInstance, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers));
        services.AddSingleton<IJobListener>(implementationInstance);
    }

    public void AddJobListener<T>(Func<IServiceProvider, T> implementationFactory, params IMatcher<JobKey>[] matchers) where T : class, IJobListener
    {
        services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers));
        services.AddSingleton<IJobListener>(implementationFactory);
    }

    public void AddTriggerListener<T>(params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers));
        services.AddSingleton<ITriggerListener, T>();
    }

    public void AddTriggerListener<T>(T implementationInstance, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers));
        services.AddSingleton<ITriggerListener>(implementationInstance);
    }

    public void AddTriggerListener<T>(Func<IServiceProvider, T> implementationFactory, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
    {
        services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers));
        services.AddSingleton<ITriggerListener>(implementationFactory);
    }
}