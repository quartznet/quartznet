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
        return services.AddQuartzHostedService<QuartzHostedService>(configure);
    }

    /// <summary>
    /// Add an <see cref="QuartzHostedService"/> to the <see cref="IServiceCollection"/>.
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

        return services.AddHostedService<T>();
    }
}