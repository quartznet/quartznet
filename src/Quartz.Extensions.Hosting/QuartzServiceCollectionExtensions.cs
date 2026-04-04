using System;
using System.Linq;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Quartz;

public static class QuartzServiceCollectionExtensions
{
    public static IServiceCollection AddQuartzHostedService(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        if (configure != null)
        {
            services.Configure(configure);
        }

        // Only register the default QuartzHostedService if a default ISchedulerFactory was registered
        // (i.e., user called the unnamed AddQuartz() overload)
        if (services.Any(d => d.ServiceType == typeof(ISchedulerFactory)))
        {
            services.AddSingleton<IHostedService, QuartzHostedService>();
        }

        // Always register NamedSchedulerHostedService -- it no-ops if no named schedulers exist
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NamedSchedulerHostedService>());

        return services;
    }
}