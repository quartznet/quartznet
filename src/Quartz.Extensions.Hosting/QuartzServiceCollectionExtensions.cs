using System;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Quartz
{
    public static class QuartzServiceCollectionExtensions
    {
        public static IServiceCollection AddQuartzHostedService(
            this IServiceCollection services,
            Action<QuartzHostedServiceOptions>? configure = null)
        {
            return services.AddSingleton<IHostedService>(serviceProvider =>
            {
                var scheduler = serviceProvider.GetRequiredService<ISchedulerFactory>();

                var options = new QuartzHostedServiceOptions();
                configure?.Invoke(options);
                
                return new QuartzHostedService(scheduler, options);
            });
        }
    }
}