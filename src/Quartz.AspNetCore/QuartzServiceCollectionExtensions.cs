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
            services
                .AddHealthChecks()
                .AddQuartzHealthCheck<QuartzHealthCheck>("scheduler");

            return services.AddQuartzHostedService(configure);
        }

        private static IHealthChecksBuilder AddQuartzHealthCheck<T>(
            this IHealthChecksBuilder builder,
            string suffix) where T : class, IHealthCheck
        {
            return builder.AddCheck<T>($"quartz-{suffix}");
        }
    }
}