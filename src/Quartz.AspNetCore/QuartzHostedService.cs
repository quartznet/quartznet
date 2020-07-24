using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

using Quartz.AspNetCore.HealthChecks;

namespace Quartz
{
    internal class QuartzHostedService : IHostedService
    {
        private readonly ISchedulerFactory schedulerFactory;
        private readonly QuartzHealthCheck healthCheck;
        private readonly QuartzHostedServiceOptions options;
        private IScheduler scheduler = null!;

        public QuartzHostedService(
            ISchedulerFactory schedulerFactory,
            QuartzHealthCheck healthCheck,
            QuartzHostedServiceOptions options)
        {
            this.schedulerFactory = schedulerFactory;
            this.healthCheck = healthCheck;
            this.options = options;
        }

        public async Task StartAsync(CancellationToken cancellationToken)
        {
            scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            scheduler.ListenerManager.AddSchedulerListener(healthCheck);
            await scheduler.Start(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return scheduler.Shutdown(options.WaitForJobsToComplete, cancellationToken);
        }
    }
}
