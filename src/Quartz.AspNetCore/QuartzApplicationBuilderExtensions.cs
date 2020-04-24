using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;

using Quartz.AspNetCore.HealthChecks;

namespace Quartz
{
    /*
    public static class QuartzApplicationBuilderExtensions
    {
        /// <summary>
        /// Adds the Quartz <see cref="IHostedService"/>, which includes a scheduler and a health check.
        /// </summary>
        public static IServiceCollection UseQuartzServer(this IApplicationBuilder app)
        {
            var check = new SchedulerHealthCheck();
            var services = app.ApplicationServices;
#if NETCOREAPP3_0
            var lifetime = services.GetRequiredService<IHostApplicationLifetime>();
#else
            var lifetime = services.GetRequiredService<IApplicationLifetime>();
#endif


            lifetime.ApplicationStopping.Register(() => server.SendStop());
            lifetime.ApplicationStopped.Register(() => server.Dispose());

            return collection.AddSingleton<IHostedService>(p =>
            {
                var scheduler = p.GetRequiredService<IScheduler>();
                return new QuartzHostedService(scheduler, new SchedulerHealthCheck());
            });
        }
    }
*/
}
