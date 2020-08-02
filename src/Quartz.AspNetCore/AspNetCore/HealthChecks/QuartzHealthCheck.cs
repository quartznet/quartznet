using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz.AspNetCore.HealthChecks
{
    internal class QuartzHealthCheck : IHealthCheck
    {
        private readonly IQuartzHostedServiceListener listener;

        public QuartzHealthCheck(IQuartzHostedServiceListener listener)
        {
            this.listener = listener;
        }

        Task<HealthCheckResult> IHealthCheck.CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken)
        {
            HealthCheckResult result;
            if (!listener.Running)
            {
                result = HealthCheckResult.Unhealthy($"Quartz scheduler is not running");
            }
            else if (listener.ErrorCount > 0)
            {
                result = HealthCheckResult.Unhealthy($"Quartz scheduler has experienced {listener.ErrorCount} errors");
            }
            else
            {
                result = HealthCheckResult.Healthy("Quartz scheduler is ready");
            }

            return Task.FromResult(result);
        }
    }
}
