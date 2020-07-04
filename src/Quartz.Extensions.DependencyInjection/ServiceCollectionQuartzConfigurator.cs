using System;
using System.Threading.Tasks;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Impl;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    internal class ServiceCollectionQuartzConfigurator : IServiceCollectionQuartzConfigurator 
    {
        private readonly IServiceCollection services;
        internal readonly SchedulerBuilder schedulerBuilder;

        internal ServiceCollectionQuartzConfigurator(IServiceCollection collection, SchedulerBuilder schedulerBuilder)
        {
            services = collection;
            this.schedulerBuilder = schedulerBuilder;
        }

        IServiceCollection IServiceCollectionQuartzConfigurator.Services => services;

        public void UseMicrosoftDependencyInjectionJobFactory()
        {
            UseJobFactory<MicrosoftDependencyInjectionJobFactory>();
        }

        public void UseMicrosoftDependencyInjectionScopedJobFactory()
        {
            UseJobFactory<MicrosoftDependencyInjectionScopedJobFactory>();
        }

        public void UseSimpleTypeLoader()
        {
            UseTypeLoader<SimpleTypeLoadHelper>();
        }

        public void SetProperty(string name, string value)
        {
            schedulerBuilder.SetProperty(name, value);
        }

        public SchedulerBuilder SetSchedulerId(string id)
        {
            return schedulerBuilder.SetSchedulerId(id);
        }

        public SchedulerBuilder SetSchedulerName(string name)
        {
            return schedulerBuilder.SetSchedulerName(name);
        }

        public void UseInMemoryStore(Action<SchedulerBuilder.InMemoryStoreOptions>? options = null)
        {
            schedulerBuilder.UseInMemoryStore(options);
        }

        public void UsePersistentStore(Action<SchedulerBuilder.PersistentStoreOptions> options)
        {
            schedulerBuilder.UsePersistentStore(options);
        }

        public void UseJobFactory<T>() where T : IJobFactory
        {
            schedulerBuilder.UseJobFactory<T>();
            services.TryAddSingleton(typeof(IJobFactory), typeof(T));
        }

        public void UseTypeLoader<T>() where T : ITypeLoadHelper
        {
            schedulerBuilder.UseTypeLoader<T>();
            services.TryAddSingleton(typeof(ITypeLoadHelper), typeof(T));
        }

        public StdSchedulerFactory Build()
        {
            return schedulerBuilder.Build();
        }

        public Task<IScheduler> BuildScheduler()
        {
            return schedulerBuilder.BuildScheduler();
        }

        public void UseThreadPool<T>(Action<SchedulerBuilder.ThreadPoolOptions>? configurer = null) where T : IThreadPool
        {
            schedulerBuilder.UseThreadPool<T>(configurer);
        }

        public void UseDefaultThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configurer = null)
        {
            schedulerBuilder.UseDefaultThreadPool(configurer);
        }

        public void UseDedicatedThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configurer = null)
        {
            schedulerBuilder.UseDedicatedThreadPool(configurer);
        }

        public SchedulerBuilder SetMisfireThreshold(TimeSpan threshold)
        {
            return schedulerBuilder.SetMisfireThreshold(threshold);
        }
    }
}