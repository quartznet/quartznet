using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

using Quartz.AspNetCore.HealthChecks;

namespace Quartz
{
    internal class QuartzHostedService : IHostedService
    {
        private readonly ISchedulerFactory schedulerFactory;
        private readonly SchedulerHealthCheck healthCheck;
        private readonly QuartzHostedServiceOptions options;
        private IScheduler scheduler = null!;

        public QuartzHostedService(
            ISchedulerFactory schedulerFactory,
            SchedulerHealthCheck healthCheck,
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
