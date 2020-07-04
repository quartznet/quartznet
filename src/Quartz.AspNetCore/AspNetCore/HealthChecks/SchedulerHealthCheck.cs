using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

using Quartz.Listener;

namespace Quartz.AspNetCore.HealthChecks
{
    internal class SchedulerHealthCheck : SchedulerListenerSupport, IHealthCheck
    {
        private bool running;
        private int errorCount;

        Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            HealthCheckResult result;
            if (!running)
            {
                result = HealthCheckResult.Unhealthy($"Quartz scheduler is not running");
            }
            else if (errorCount > 0)
            {
                result = HealthCheckResult.Unhealthy($"Quartz scheduler has experienced {errorCount} errors");
            }
            else
            {
                result = HealthCheckResult.Healthy("Quartz scheduler is ready");
            }

            return Task.FromResult(result);
        }

        public override Task SchedulerError(string msg, SchedulerException cause, CancellationToken cancellationToken = default)
        {
            errorCount++;
            return Task.CompletedTask;
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
