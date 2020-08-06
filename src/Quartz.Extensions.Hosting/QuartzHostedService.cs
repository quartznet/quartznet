using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace Quartz
{
    internal class QuartzHostedService : IHostedService
    {
        private readonly ISchedulerFactory schedulerFactory;
        private readonly QuartzHostedServiceOptions options;
        private IScheduler scheduler = null!;

        public QuartzHostedService(
            ISchedulerFactory schedulerFactory,
            QuartzHostedServiceOptions options)
        {
            this.schedulerFactory = schedulerFactory;
            this.options = options;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            await scheduler.Start(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return scheduler.Shutdown(options.WaitForJobsToComplete, cancellationToken);
        }
    }
}
