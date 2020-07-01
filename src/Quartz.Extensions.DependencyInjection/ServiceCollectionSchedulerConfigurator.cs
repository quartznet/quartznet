using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Spi;

namespace Quartz
{
    public class ServiceCollectionSchedulerConfigurator : SchedulerBuilder
    {
        private readonly IServiceCollection collection;

        internal ServiceCollectionSchedulerConfigurator(IServiceCollection collection) : base(null)
        {
            this.collection = collection;
        }

        public override SchedulerBuilder WithJobFactory<T>()
        {
            base.WithJobFactory<T>();
            collection.TryAddSingleton(typeof(IJobFactory), typeof(T));
            return this;
        }

        public ServiceCollectionSchedulerConfigurator WithMicrosoftDependencyInjectionJobFactory()
        {
            WithJobFactory<MicrosoftDependencyInjectionJobFactory>();
            return this;
        }

        public override SchedulerBuilder WithTypeLoadHelper<T>()
        {
            base.WithTypeLoadHelper<T>();
            collection.TryAddSingleton(typeof(ITypeLoadHelper), typeof(T));
            return this;
        }
    }
}