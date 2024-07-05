using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz;

public static class QuartzServiceCollectionExtensions
{
    public static IServiceCollection AddQuartzHostedService(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }
        return services.AddSingleton<IHostedService, QuartzHostedService>();
    }
}