using System;
using System.Collections.Specialized;
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
            set => schedulerBuilder.SetSchedulerId(value);
        }

        public string SchedulerName
        {
            set => schedulerBuilder.SetSchedulerName(value);
        }

        public void UseInMemoryStore(Action<SchedulerBuilder.InMemoryStoreOptions>? configure = null)
        {
            schedulerBuilder.UseInMemoryStore(configure);
        }

        public void UsePersistentStore(Action<SchedulerBuilder.PersistentStoreOptions> configure)
        {
            schedulerBuilder.UsePersistentStore(configure);
        }
        
        public void UseMicrosoftDependencyInjectionJobFactory(Action<JobFactoryOptions>? configure = null)
        {
            UseJobFactory<MicrosoftDependencyInjectionJobFactory>(configure);
        }

        public void UseMicrosoftDependencyInjectionScopedJobFactory(Action<JobFactoryOptions>? configure = null)
        {
            UseJobFactory<MicrosoftDependencyInjectionScopedJobFactory>(configure);
        }
        
        public void UseJobFactory<T>(Action<JobFactoryOptions>? configure = null) where T : IJobFactory
        {
            schedulerBuilder.UseJobFactory<T>();
            services.TryAddSingleton(typeof(IJobFactory), typeof(T));
            var options = new JobFactoryOptions();
            configure?.Invoke(options);
            services.TryAddSingleton(options);
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

        public void UseThreadPool<T>(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null) where T : IThreadPool
        {
            schedulerBuilder.UseThreadPool<T>(configure);
        }

        public void UseDefaultThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null)
        {
            schedulerBuilder.UseDefaultThreadPool(configure);
        }

        public void UseDedicatedThreadPool(Action<SchedulerBuilder.ThreadPoolOptions>? configure = null)
        {
            schedulerBuilder.UseDedicatedThreadPool(configure);
        }

        public TimeSpan MisfireThreshold
        {
            set => schedulerBuilder.SetMisfireThreshold(value);
        }
    }
}