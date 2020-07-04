using System.Collections.Generic;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Quartz.AspNetCore.HealthChecks
{
    internal class HealthCheckOptions
    {
        public string? SchedulerHealthCheckName { get; set; }
        public HealthStatus FailureStatus { get; set; }
        public IEnumerable<string>? Tags { get; set; }

        public static HealthCheckOptions Default
            => new HealthCheckOptions
            {
                SchedulerHealthCheckName = "quartz-scheduler",
                FailureStatus = HealthStatus.Unhealthy,
                Tags = new[] { "ready" }
            };
    }
}
