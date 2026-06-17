using System;
using System.Collections.Generic;

using Microsoft.Extensions.DependencyInjection;

#if SUPPORTS_HEALTH_CHECKS
using Quartz.AspNetCore.HealthChecks;
#endif

namespace Quartz.AspNetCore;

public static class QuartzServiceCollectionExtensions
{
    /// <summary>
    /// Adds the Quartz hosted service that starts and stops the scheduler with the application lifetime.
    /// On target frameworks with health check support a <c>quartz-scheduler</c> health check is also registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for the hosted service.</param>
    public static IServiceCollection AddQuartzServer(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        return services.AddQuartzServer(configure, healthCheckTags: null);
    }

    /// <summary>
    /// Adds the Quartz hosted service that starts and stops the scheduler with the application lifetime.
    /// On target frameworks with health check support a <c>quartz-scheduler</c> health check is also registered.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional configuration for the hosted service.</param>
    /// <param name="healthCheckTags">
    /// Tags to associate with the <c>quartz-scheduler</c> health check, allowing it to be filtered (for example
    /// into liveness/readiness probes). Has no effect on target frameworks without health check support.
    /// </param>
    public static IServiceCollection AddQuartzServer(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure,
        IEnumerable<string>? healthCheckTags)
    {
#if SUPPORTS_HEALTH_CHECKS
        services
            .AddHealthChecks()
            .AddTypeActivatedCheck<QuartzHealthCheck>(
                "quartz-scheduler",
                failureStatus: null,
                tags: healthCheckTags ?? Array.Empty<string>());
#endif

        return services.AddQuartzHostedService(configure);
    }
}