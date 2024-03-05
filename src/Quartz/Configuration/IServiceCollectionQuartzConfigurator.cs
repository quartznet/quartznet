using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Spi;

namespace Quartz;

public interface IServiceCollectionQuartzConfigurator : IPropertyConfigurer, IPropertyConfigurationRoot
{
    internal IServiceCollection Services { get; }

    void SetLoggerFactory(ILoggerFactory loggerFactory);

    void UseTypeLoader<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() where T : ITypeLoadHelper;
    void UseSimpleTypeLoader();

    /// <inheritdoc cref="SchedulerBuilder.SchedulerId"/>
    string SchedulerId { set; }

    /// <inheritdoc cref="SchedulerBuilder.SchedulerName"/>
    string SchedulerName { set; }

    /// <inheritdoc cref="SchedulerBuilder.MisfireThreshold"/>
    TimeSpan MisfireThreshold { set; }

    /// <inheritdoc cref="SchedulerBuilder.InterruptJobsOnShutdown"/>
    bool InterruptJobsOnShutdown { set; }

    /// <inheritdoc cref="SchedulerBuilder.InterruptJobsOnShutdownWithWait"/>
    bool InterruptJobsOnShutdownWithWait { set; }

    /// <inheritdoc cref="SchedulerBuilder.MaxBatchSize"/>
    int MaxBatchSize { set; }

    /// <inheritdoc cref="SchedulerBuilder.BatchTriggerAcquisitionFireAheadTimeWindow"/>
    TimeSpan BatchTriggerAcquisitionFireAheadTimeWindow { set; }

    /// <inheritdoc cref="SchedulerBuilder.CheckConfiguration"/>
    bool CheckConfiguration { set; }

    /// <summary>
    /// Configure custom job factory.
    /// </summary>
    void UseJobFactory<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(Action<JobFactoryOptions>? configure = null) where T : class, IJobFactory;

    void UseInMemoryStore(Action<SchedulerBuilder.InMemoryStoreOptions>? configure = null);
    void UsePersistentStore(Action<SchedulerBuilder.PersistentStoreOptions> configure);
    void UsePersistentStore<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(Action<SchedulerBuilder.PersistentStoreOptions> configure) where T : class, IJobStore;
    void UseThreadPool<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null) where T : class, IThreadPool;
    void UseDefaultThreadPool(int maxConcurrency, Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
    void UseDefaultThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
    void UseZeroSizeThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
    void UseDedicatedThreadPool(int maxConcurrency, Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
    void UseDedicatedThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);

    void AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>() where T : class, ISchedulerListener;
    void AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(T implementationInstance) where T : class, ISchedulerListener;
    void AddSchedulerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(Func<IServiceProvider, T> implementationFactory) where T : class, ISchedulerListener;

    void AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(params IMatcher<JobKey>[] matchers) where T : class, IJobListener;
    void AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(T implementationInstance, params IMatcher<JobKey>[] matchers) where T : class, IJobListener;
    void AddJobListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(Func<IServiceProvider, T> implementationFactory, params IMatcher<JobKey>[] matchers) where T : class, IJobListener;

    void AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener;
    void AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(T implementationInstance, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener;
    void AddTriggerListener<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicMethods)] T>(Func<IServiceProvider, T> implementationFactory, params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener;
}