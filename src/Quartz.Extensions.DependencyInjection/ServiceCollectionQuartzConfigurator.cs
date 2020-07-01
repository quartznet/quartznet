using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz
{
    public class ServiceCollectionQuartzConfigurator : SchedulerBuilder
    {
        internal ServiceCollectionQuartzConfigurator(IServiceCollection collection) : base(null)
        {
            Services = collection;
        }

        internal IServiceCollection Services { get; }

        public override void UseJobFactory<T>()
        {
            base.UseJobFactory<T>();
            Services.TryAddSingleton(typeof(IJobFactory), typeof(T));
        }

        public void UseMicrosoftDependencyInjectionJobFactory()
        {
            UseJobFactory<MicrosoftDependencyInjectionJobFactory>();
        }

        public override void UseTypeLoader<T>()
        {
            base.UseTypeLoader<T>();
            Services.TryAddSingleton(typeof(ITypeLoadHelper), typeof(T));
        }

        public void UseSimpleTypeLoader()
        {
            UseTypeLoader<SimpleTypeLoadHelper>();
        }
    }
}