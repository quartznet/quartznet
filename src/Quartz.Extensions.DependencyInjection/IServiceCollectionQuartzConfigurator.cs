using System;

using Microsoft.Extensions.DependencyInjection;

using Quartz.Spi;

namespace Quartz
{
    public interface IServiceCollectionQuartzConfigurator
    {
        internal IServiceCollection Services { get; }
        void UseJobFactory<T>() where T : IJobFactory;
        void UseMicrosoftDependencyInjectionJobFactory();
        void UseTypeLoader<T>() where T : ITypeLoadHelper;
        void UseSimpleTypeLoader();
        void SetProperty(string name, string value);
        SchedulerBuilder SetSchedulerId(string id);
        SchedulerBuilder SetSchedulerName(string name);
        void UseInMemoryStore(Action<SchedulerBuilder.InMemoryStoreOptions>? options = null);
        void UsePersistentStore(Action<SchedulerBuilder.PersistentStoreOptions> options);
        void UseThreadPool<T>(Action<SchedulerBuilder.ThreadPoolOptions>? configurer = null) where T : IThreadPool;
        void UseDefaultThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configurer = null);
        void UseDedicatedThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configurer = null);
        SchedulerBuilder SetMisfireThreshold(TimeSpan threshold);
    }
}