#if SUPPORTS_HEALTH_CHECKS
using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Quartz.Listener;
using Quartz.Spi;

namespace Quartz.AspNetCore.HealthChecks
{
    internal class QuartzHealthCheck : SchedulerListenerSupport, IHealthCheck
    {
        private readonly IJobStore? store;
        private bool running;

        public QuartzHealthCheck(IJobStore? store = null)
        {
            this.store = store;
        }

        async Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            if (!running)
            {
                return HealthCheckResult.Unhealthy("Quartz scheduler is not running");
            }
            else if (store is not null)
            {
                try
                {
                    // Ask for a job we know doesn't exist
                    await store.CheckExists(new JobKey(Guid.NewGuid().ToString()), cancellationToken);
                }
                catch (SchedulerException)
                {
                    return HealthCheckResult.Unhealthy("Quartz scheduler cannot connect to the store");
                }
            }

            return HealthCheckResult.Healthy("Quartz scheduler is ready");
        }


        public override Task SchedulerStarted(CancellationToken cancellationToken = default)
        {
            running = true;
            return Task.CompletedTask;
        }

        public override Task SchedulerShutdown(CancellationToken cancellationToken = default)
        {
            running = false;
            return Task.CompletedTask;
        }
    }
}
#endif