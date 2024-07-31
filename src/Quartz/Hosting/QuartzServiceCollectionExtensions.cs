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
}