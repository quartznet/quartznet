using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;

using Quartz.AspNetCore.HealthChecks;

namespace Quartz
{
    public static class QuartzServiceCollectionExtensions
    {
        public static IServiceCollection AddQuartzServer(
            this IServiceCollection services,
            Action<QuartzHostedServiceOptions>? configure = null)
        {
            var check = new QuartzHealthCheck();
            services
                .AddHealthChecks()
                .AddQuartzHealthCheck<QuartzHealthCheck>("scheduler", check);

            services.AddSingleton<ISchedulerListener>(check);

            return services.AddQuartzHostedService(configure);
        }

        private static IHealthChecksBuilder AddQuartzHealthCheck<T>(
            this IHealthChecksBuilder builder,
            string suffix,
            IHealthCheck check)
        {
            return builder.AddCheck($"quartz-{suffix}", check);
        }
    }
}