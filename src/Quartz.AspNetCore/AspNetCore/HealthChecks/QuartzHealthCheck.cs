#if SUPPORTS_HEALTH_CHECKS
using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz.AspNetCore.HealthChecks
{
    internal sealed class QuartzHealthCheck : IHealthCheck
    {
        private readonly ISchedulerFactory schedulerFactory;

        public QuartzHealthCheck(ISchedulerFactory schedulerFactory)
        {
            this.schedulerFactory = schedulerFactory ?? throw new ArgumentNullException(nameof(schedulerFactory));
        }

        async Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            var scheduler = await schedulerFactory.GetScheduler(cancellationToken);
            if (!scheduler.IsStarted)
            {
                return HealthCheckResult.Unhealthy("Quartz scheduler is not running");
            }

            try
            {
                // Ask for a job we know doesn't exist
                await scheduler.CheckExists(new JobKey(Guid.NewGuid().ToString()), cancellationToken);
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