using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Hosting;

using Quartz.AspNetCore.HealthChecks;

namespace Quartz
{
    internal class QuartzHostedService : IHostedService
    {
        private readonly IScheduler scheduler;
        private readonly SchedulerHealthCheck healthCheck;

        public QuartzHostedService(
            IScheduler scheduler,
            SchedulerHealthCheck healthCheck)
        {
            this.scheduler = scheduler;
            this.healthCheck = healthCheck;
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            scheduler.ListenerManager.AddSchedulerListener(healthCheck);
            return scheduler.Start(cancellationToken);
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            return scheduler.Shutdown(cancellationToken);
        }
    }
}
