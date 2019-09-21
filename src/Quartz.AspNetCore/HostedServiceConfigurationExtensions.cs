using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.AspNetCore.HealthChecks;

namespace Quartz.AspNetCore
{
    public static class HostedServiceConfigurationExtensions
    {
        public static void UseHealthCheck(this IScheduler scheduler, IServiceProvider provider)
        {
            var healthCheck = provider.GetRequiredService<SchedulerHealthCheck>();
            scheduler.ListenerManager.AddSchedulerListener(healthCheck);
        }

        /// <summary>
        /// Adds the MassTransit <see cref="IHostedService"/>, which includes a bus and endpoint health check
        /// </summary>
        /// <param name="collection"></param>
        public static void AddQuartzHostedService(this IServiceCollection collection)
        {
            var check = new SchedulerHealthCheck();

            collection.AddSingleton(check);

            collection.AddHealthChecks()
                .AddQuartzHealthCheck("scheduler", check);

            collection.AddSingleton<IHostedService>(p =>
            {
                var scheduler = p.GetRequiredService<IScheduler>();
                var loggerFactory = p.GetService<ILoggerFactory>();

                return new QuartzHostedService(scheduler, loggerFactory, new SchedulerHealthCheck());
            });
        }

        static IHealthChecksBuilder AddQuartzHealthCheck(
            this IHealthChecksBuilder builder,
            string suffix,
            IHealthCheck healthCheck)
        {
            return builder.AddCheck($"quartz-{suffix}", healthCheck, HealthStatus.Unhealthy, new[] {"ready"});
        }
    }
}
