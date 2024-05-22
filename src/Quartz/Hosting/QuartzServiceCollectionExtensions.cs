using Microsoft.Extensions.DependencyInjection;

#if NET8_0_OR_GREATER
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection.Extensions;
#else
using Microsoft.Extensions.Hosting;
#endif

namespace Quartz;

public static class QuartzServiceCollectionExtensions
{
    /// <summary>
    /// Add an <see cref="QuartzHostedService"/> to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

#if NET8_0_OR_GREATER
        return services.AddHostedService<QuartzHostedService>();
#else
        return services.AddSingleton<IHostedService, QuartzHostedService>();
#endif
    }

#if NET8_0_OR_GREATER
    /// <summary>
    /// Add an <see cref="QuartzHostedService"/> and implementation <see cref="IQuartzHostedLifecycleService"/>> to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="THostedLifecycleService">Type implementing the <see cref="IQuartzHostedLifecycleService"/> interface.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] THostedLifecycleService>(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
        where THostedLifecycleService : class, IQuartzHostedLifecycleService
    {
        services.TryAddSingleton<IQuartzHostedLifecycleService, THostedLifecycleService>();

        return services.AddQuartzHostedService(configure);
    }
#endif
}