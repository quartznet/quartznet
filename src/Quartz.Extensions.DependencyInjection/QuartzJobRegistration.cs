using System.Threading.Tasks;

namespace Quartz
{
    internal class QuartzJobRegistration
    {
        private readonly ServiceCollectionJobConfigurator configurator;

        public QuartzJobRegistration(ServiceCollectionJobConfigurator configurator)
        {
            this.configurator = configurator;
        }

        public async Task Attach(IScheduler scheduler)
        {
            var job = configurator.jobBuilder.Build();
            await scheduler.AddJob(job, true);
        }
    }
}