using System;

using Microsoft.Extensions.DependencyInjection;

#if SUPPORTS_HEALTH_CHECKS
using Quartz.AspNetCore.HealthChecks;
#endif

namespace Quartz
{
    public static class QuartzServiceCollectionExtensions
    {
        public static IServiceCollection AddQuartzServer(
            this IServiceCollection services,
            Action<QuartzHostedServiceOptions>? configure = null)
        {
#if SUPPORTS_HEALTH_CHECKS
            var check = new QuartzHealthCheck();
            services
                .AddHealthChecks()
                .AddCheck("quartz-scheduler", check);

            services.AddSingleton<ISchedulerListener>(check);
#endif

            return services.AddQuartzHostedService(configure);
        }
    }
}