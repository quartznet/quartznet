using System;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    public interface IServiceCollectionQuartzConfigurator : IPropertyConfigurer, IPropertyConfigurationRoot
    {
        internal IServiceCollection Services { get; }

        void UseTypeLoader<T>() where T : ITypeLoadHelper;
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

        /// <summary>
        /// Configure custom job factory.
        /// </summary>
        void UseJobFactory<T>(Action<JobFactoryOptions>? configure = null) where T : IJobFactory;

        /// <summary>
        /// Use <see cref="MicrosoftDependencyInjectionJobFactory"/> to produce Quartz jobs.
        /// </summary>
        void UseMicrosoftDependencyInjectionJobFactory(Action<JobFactoryOptions>? configure = null);

        /// <summary>
        /// Use <see cref="UseMicrosoftDependencyInjectionScopedJobFactory"/> to produce Quartz jobs.
        /// </summary>
        [Obsolete("Jobs are always created with scope, use UseMicrosoftDependencyInjectionJobFactory")]
        void UseMicrosoftDependencyInjectionScopedJobFactory(Action<JobFactoryOptions>? configure = null);

        void UseInMemoryStore(Action<SchedulerBuilder.InMemoryStoreOptions>? configure = null);
        void UsePersistentStore(Action<SchedulerBuilder.PersistentStoreOptions> configure);
        void UseThreadPool<T>(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null) where T : IThreadPool;
        void UseDefaultThreadPool(int maxConcurrency, Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
        void UseDefaultThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
        void UseZeroSizeThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
        void UseDedicatedThreadPool(int maxConcurrency, Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);
        void UseDedicatedThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null);

        void AddSchedulerListener<T>() where T : class, ISchedulerListener;
        void AddJobListener<T>(params IMatcher<JobKey>[] matchers) where T : class, IJobListener;
        void AddTriggerListener<T>(params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener;
    }
}