using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace Quartz;

public static partial class QuartzServiceCollectionExtensions
{
    /// <summary>
    /// Adds a <see cref="QuartzHostedService"/> to the <see cref="IServiceCollection"/>, which starts
    /// and stops every scheduler in the container with the application.
    /// </summary>
    /// <remarks>
    /// The options configured here apply to every scheduler, which is what they have always meant. One
    /// scheduler that has to differ says so with
    /// <see cref="AddQuartzHostedService(IServiceCollection, string, Action{QuartzHostedServiceOptions})"/>.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        return services.AddQuartzHostedService<QuartzHostedService>(configure);
    }

    /// <summary>
    /// Adds a hosted service of your own deriving from <see cref="QuartzHostedService"/>.
    /// </summary>
    /// <inheritdoc cref="AddQuartzHostedService(IServiceCollection, Action{QuartzHostedServiceOptions})" path="/remarks" />
    /// <typeparam name="T">Type extending the <see cref="QuartzHostedService"/> class.</typeparam>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] T>(
        this IServiceCollection services,
        Action<QuartzHostedServiceOptions>? configure = null)
        where T : QuartzHostedService
    {
        ArgumentNullException.ThrowIfNull(services);

        if (configure is not null)
        {
            // Every scheduler's options, not only the default scheduler's: one hosted service used to
            // read one options object for all of them, and a setting like WaitForJobsToComplete has to
            // go on meaning what it did.
            services.ConfigureAll(configure);
        }

        // Registered whatever else the container holds. The service resolves its schedulers when the host
        // starts, so this no longer has to be called after AddQuartz to have any effect — and a container
        // with no scheduler in it is reported at startup rather than silently doing nothing.
        AddHostedService(services, typeof(T));

        return services;
    }

    /// <summary>
    /// Configures how a named scheduler is started and stopped by the hosted service.
    /// </summary>
    /// <remarks>
    /// The hosted service starts every scheduler in the container; this says how one of them is treated.
    /// A scheduler that should wait for application startup while another does not, or that needs longer
    /// to drain on shutdown, is configured here. It refines the settings shared by every scheduler,
    /// whichever order the two calls are made in.
    /// </remarks>
    /// <param name="services">The <see cref="IServiceCollection"/>.</param>
    /// <param name="schedulerName">The name the scheduler was registered under with <c>AddQuartz(name, …)</c>.</param>
    /// <param name="configure">A delegate that is used to configure an <see cref="QuartzHostedServiceOptions"/>.</param>
    public static IServiceCollection AddQuartzHostedService(
        this IServiceCollection services,
        string schedulerName,
        Action<QuartzHostedServiceOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(schedulerName);

        if (configure is not null)
        {
            // PostConfigure rather than Configure, so this scheduler's own settings are applied after the
            // shared ones no matter which call came first.
            services.PostConfigure(schedulerName, configure);
        }

        // So that configuring one scheduler is enough to have the schedulers started, without a second
        // hosted service appearing when the application also called AddQuartzHostedService().
        AddHostedService(services, typeof(QuartzHostedService));

        return services;
    }

    /// <summary>
    /// Registers the hosted service, keeping there being exactly one of it.
    /// </summary>
    /// <remarks>
    /// Two of them would each start every scheduler in the container. <c>TryAddEnumerable</c> is not
    /// enough on its own, because it tells registrations apart by implementation type: a container told
    /// both <c>AddQuartzHostedService&lt;MyService&gt;()</c> and <c>AddQuartzHostedService("reporting", …)</c>
    /// would get one of each. The more derived service wins, since it is the one the application wrote.
    /// </remarks>
    private static void AddHostedService(
        IServiceCollection services,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    {
        var existing = services.FirstOrDefault(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType is not null
            && typeof(QuartzHostedService).IsAssignableFrom(descriptor.ImplementationType));

        if (existing is null)
        {
            services.Add(ServiceDescriptor.Singleton(typeof(IHostedService), implementationType));
            return;
        }

        if (existing.ImplementationType != implementationType
            && existing.ImplementationType == typeof(QuartzHostedService))
        {
            services.Remove(existing);
            services.Add(ServiceDescriptor.Singleton(typeof(IHostedService), implementationType));
        }
    }
}
