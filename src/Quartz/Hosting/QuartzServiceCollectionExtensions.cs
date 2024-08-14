using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.DependencyInjection;

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

        return services.AddHostedService<QuartzHostedService>();
    }

    /// <summary>
    /// Add an <see cref="QuartzHostedService"/> to the <see cref="IServiceCollection"/>.
    /// </summary>
    /// <typeparam name="TExtendedHostedService">Type extending the <see cref="QuartzHostedService"/> class.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TExtendedHostedService>(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
        where TExtendedHostedService : QuartzHostedService
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return services.AddHostedService<TExtendedHostedService>();
    }
}