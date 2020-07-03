using Microsoft.Extensions.DependencyInjection;

namespace Quartz
{
    internal class ServiceCollectionTriggerConfigurator : TriggerBuilder, IServiceCollectionTriggerConfigurator
    {
        private readonly IServiceCollection services;

        public ServiceCollectionTriggerConfigurator(IServiceCollection services)
        {
            this.services = services;
        }
    }
}