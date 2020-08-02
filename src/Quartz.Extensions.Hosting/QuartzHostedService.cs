using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

namespace Quartz
{
    internal class QuartzHostedService : IHostedService
    {
        private readonly ISchedulerFactory schedulerFactory;
        private readonly QuartzHostedServiceListener listener;
        private readonly QuartzHostedServiceOptions options;
        private IScheduler scheduler = null!;

        public QuartzHostedService(
            ISchedulerFactory schedulerFactory,
            QuartzHostedServiceListener listener,
            QuartzHostedServiceOptions options)
        {
            this.schedulerFactory = schedulerFactory;
            this.listener = listener;
            this.options = options;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            scheduler.ListenerManager.AddSchedulerListener(listener);
            await scheduler.Start(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return scheduler.Shutdown(options.WaitForJobsToComplete, cancellationToken);
        }
    }
}
