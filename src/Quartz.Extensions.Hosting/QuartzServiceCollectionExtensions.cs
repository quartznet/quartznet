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
        // (i.e., user called the unnamed AddQuartz() overload). We cannot register it unconditionally
        // because QuartzHostedService requires ISchedulerFactory as a constructor dependency --
        // the DI container would fail at host startup when only named schedulers are used.
        // This means AddQuartz() must be called before AddQuartzHostedService() for the default scheduler.
        if (services.Any(d => d.ServiceType == typeof(ISchedulerFactory)))
        {
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, QuartzHostedService>());
        }

        // Always register NamedSchedulerHostedService -- it no-ops if no named schedulers exist
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NamedSchedulerHostedService>());

        return services;
    }
}