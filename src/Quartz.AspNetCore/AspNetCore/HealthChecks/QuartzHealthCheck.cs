#if SUPPORTS_HEALTH_CHECKS
using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Quartz.Listener;
using Quartz.Spi;

namespace Quartz.AspNetCore.HealthChecks
{
    internal class QuartzHealthCheck : IHealthCheck
    {
        private readonly ISchedulerFactory schedulerFactory;

        public QuartzHealthCheck(ISchedulerFactory schedulerFactory)
        {
            this.schedulerFactory = schedulerFactory;
        }

        async Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            var scheduler = await this.schedulerFactory.GetScheduler(cancellationToken);
            if (!scheduler?.IsStarted ?? false)
            {
                return HealthCheckResult.Unhealthy("Quartz scheduler is not running");
            }

            try
            {
                // Ask for a job we know doesn't exist
                if (scheduler is not null)
                {
                    await scheduler.CheckExists(new JobKey(Guid.NewGuid().ToString()), cancellationToken);
                }
            }
            catch (SchedulerException)
            {
                return HealthCheckResult.Unhealthy("Quartz scheduler cannot connect to the store");
            }

            return HealthCheckResult.Healthy("Quartz scheduler is ready");
        }
    }
}
#endif