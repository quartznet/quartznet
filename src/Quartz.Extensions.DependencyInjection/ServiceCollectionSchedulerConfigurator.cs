using Microsoft.Extensions.DependencyInjection;

namespace Quartz
{
    public class ServiceCollectionSchedulerConfigurator : SchedulerBuilder
    {
        private readonly IServiceCollection collection;

        internal ServiceCollectionSchedulerConfigurator(IServiceCollection collection) : base(null)
        {
            this.collection = collection;
        }
    }
}