using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using OpenTracing;
using OpenTracing.Util;
using Quartz.OpenTracing;

namespace Microsoft.Extensions.DependencyInjection
{
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        /// Adds OpenTracing instrumentation for Quartz.
        /// </summary>
        public static IServiceCollection AddQuartzOpenTracing(this IServiceCollection services, Action<QuartzDiagnosticOptions>? options = null)
        {
            if (services == null)
                throw new ArgumentNullException(nameof(services));

            if (options != null)
                services.Configure(options);

            services.TryAddSingleton<ITracer>(GlobalTracer.Instance);
            services.TryAddSingleton<QuartzDiagnostic>();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IHostedService, InstrumentationService>());

            return services;
        }
    }
}
