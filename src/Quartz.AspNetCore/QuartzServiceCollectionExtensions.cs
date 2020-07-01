using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using Quartz.AspNetCore.HealthChecks;

namespace Quartz
{
    public static class QuartzServiceCollectionExtensions
    {
        public static IServiceCollection AddQuartzServer(
            this IServiceCollection services)
        {
            return services.AddSingleton<IHostedService>(serviceProvider =>
            {
                var check = new SchedulerHealthCheck();
                services.AddSingleton(check);

                services
                    .AddHealthChecks()
                    .AddQuartzHealthCheck("scheduler", check);

                var scheduler = serviceProvider.GetRequiredService<ISchedulerFactory>();
                return new QuartzHostedService(scheduler, check);
            });
        }

        private static IHealthChecksBuilder AddQuartzHealthCheck(
            this IHealthChecksBuilder builder,
            string suffix,
            IHealthCheck healthCheck)
        {
            return builder.AddCheck($"quartz-{suffix}", healthCheck, HealthStatus.Unhealthy, new[] {"ready"});
        }
    }
}