using Microsoft.Extensions.DependencyInjection;

namespace Quartz
{
    internal class ServiceCollectionJobConfigurator : JobBuilder, IServiceCollectionJobConfigurator
    {
        private readonly IServiceCollection collection;

        internal ServiceCollectionJobConfigurator(IServiceCollection collection)
        {
            this.collection = collection;
        }
    }
}