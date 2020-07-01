using Microsoft.Extensions.DependencyInjection;

namespace Quartz
{
    public class ServiceCollectionTriggerConfigurator : TriggerBuilder
    {
        private readonly IServiceCollection services;
        internal readonly TriggerBuilder builder;

        public ServiceCollectionTriggerConfigurator(IServiceCollection services, TriggerBuilder builder)
        {
            this.services = services;
            this.builder = builder;
        }
    }
}