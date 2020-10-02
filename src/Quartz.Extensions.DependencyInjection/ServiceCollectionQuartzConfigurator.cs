using System;
using System.Collections.Specialized;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    internal class ServiceCollectionQuartzConfigurator : IServiceCollectionQuartzConfigurator
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
            services.Replace(new ServiceDescriptor(typeof(IJobFactory), typeof(T), ServiceLifetime.Singleton));
            if (configure != null)
            {
                services.Configure<QuartzOptions>(options =>
                {
                    configure(options.JobFactory);
                });
            };
        }

        public void UseTypeLoader<T>() where T : ITypeLoadHelper
        {
            schedulerBuilder.UseTypeLoader<T>();
            services.Replace(new ServiceDescriptor(typeof(ITypeLoadHelper), typeof(T), ServiceLifetime.Singleton));
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
            set => schedulerBuilder.MisfireThreshold = value;
        }

        public void AddSchedulerListener<T>() where T : class, ISchedulerListener
        {
            services.AddSingleton<ISchedulerListener, T>();
        }

        public void AddJobListener<T>(params IMatcher<JobKey>[] matchers) where T : class, IJobListener
        {
            services.AddSingleton(new JobListenerConfiguration(typeof(T), matchers));
            services.AddSingleton<IJobListener, T>();
        }

        public void AddTriggerListener<T>(params IMatcher<TriggerKey>[] matchers) where T : class, ITriggerListener
        {
            services.AddSingleton(new TriggerListenerConfiguration(typeof(T), matchers));
            services.AddSingleton<ITriggerListener, T>();
        }
    }
}