using System;

using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

using Quartz.AspNetCore.HealthChecks;
using Quartz.AspNetCore.Logging;
using Quartz.Impl;
using Quartz.Logging;

namespace Quartz.AspNetCore
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceProvider UseQuartz(this IServiceProvider provider)
        {
            LogProvider.SetCurrentLogProvider(provider.GetRequiredService<LoggingProvider>());
            var scheduler = provider.GetRequiredService<IScheduler>();
            scheduler.Start();
            return provider;
        }

        public static IApplicationBuilder UseQuartz(this IApplicationBuilder builder)
        {
            LogProvider.SetCurrentLogProvider(builder.ApplicationServices.GetRequiredService<LoggingProvider>());
            var scheduler = builder.ApplicationServices.GetRequiredService<IScheduler>();
            scheduler.Start();
            return builder;
        }

        public static IServiceCollection AddQuartz(
            this IServiceCollection services,
            Action<SchedulerBuilder> optionsAction = null)
        {
            var builder = SchedulerBuilder.Create();
            optionsAction?.Invoke(builder);

            var scheduler = builder.Build();
            services.AddSingleton(scheduler);
            services.AddTransient<ISchedulerFactory, StdSchedulerFactory>();
            services.AddTransient<LoggingProvider>();
            return services;
        }

        private static void AddSimplifiedHostedService(
            this IServiceCollection services,
            Action<HealthCheckOptions> configureHealthChecks)
        {
            var schedulerCheck = new SchedulerHealthCheck();

            var healthCheckOptions = HealthCheckOptions.Default;
            configureHealthChecks?.Invoke(healthCheckOptions);

            services.AddHealthChecks()
                .AddCheck(healthCheckOptions.SchedulerHealthCheckName, schedulerCheck, healthCheckOptions.FailureStatus, healthCheckOptions.Tags);

            services.AddSingleton<IHostedService>(p =>
            {
                var scheduler = p.GetRequiredService<IScheduler>();
                var loggerFactory = p.GetService<ILoggerFactory>();

                return new QuartzHostedService(scheduler, loggerFactory, schedulerCheck);
            });
        }
    }
}
