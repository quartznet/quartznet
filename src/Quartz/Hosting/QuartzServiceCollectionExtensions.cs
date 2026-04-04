using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Quartz;

public static class QuartzServiceCollectionExtensions
{
    /// <summary>
    /// Add an <see cref="QuartzHostedService"/> to the <see cref="IServiceCollection"/>.
    /// Starts all registered schedulers (both default and named).
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        return services.AddQuartzHostedService<QuartzHostedService>(configure);
    }

    /// <summary>
    /// Add an <see cref="QuartzHostedService"/> to the <see cref="IServiceCollection"/>.
    /// Starts all registered schedulers (both default and named).
    /// </summary>
    /// <typeparam name="T">Type extending the <see cref="QuartzHostedService"/> class.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
        where T : QuartzHostedService
    {
        if (configure is not null)
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
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, T>());
        }

        // Always register NamedSchedulerHostedService -- it no-ops if no named schedulers exist
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, NamedSchedulerHostedService>());

        return services;
    }
}