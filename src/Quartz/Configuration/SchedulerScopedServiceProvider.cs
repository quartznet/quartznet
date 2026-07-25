using Microsoft.Extensions.DependencyInjection;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Spi;

namespace Quartz.Configuration;

/// <summary>
/// Resolves a scheduler's own parts when constructing one of its components.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ActivatorUtilities"/> resolves constructor dependencies without a service key, so a
/// component built for a named scheduler would be handed the default scheduler's collaborators — or
/// fail outright, because for a named scheduler they are only registered keyed. That is silent
/// cross-wiring of exactly the kind keyed registration exists to prevent.
/// </para>
/// <para>
/// Wrapping the provider fixes it in one place: while constructing a component for scheduler
/// <c>reporting</c>, a request for <see cref="ISchedulerSignaler"/> resolves <c>reporting</c>'s
/// signaler. Services that are genuinely shared, such as loggers and the connection manager, are
/// resolved normally.
/// </para>
/// </remarks>
internal sealed class SchedulerScopedServiceProvider : IServiceProvider, IKeyedServiceProvider
{
    /// <summary>
    /// The services registered once per scheduler rather than once per container.
    /// </summary>
    private static readonly HashSet<Type> schedulerScoped =
    [
        typeof(ISchedulerSignaler),
        typeof(IJobStore),
        typeof(IDbProvider),
        typeof(IDriverDelegate),
        typeof(ISemaphore),
        typeof(IThreadPool),
        typeof(IJobFactory),
        typeof(IJobRunShellFactory),
        typeof(IInstanceIdGenerator),
        typeof(ISchedulerFactory),
        typeof(QuartzSchedulerResources),
        typeof(QuartzScheduler),
        typeof(ContainerConfigurationProcessor),
        typeof(SchedulerContentInitializer),
    ];

    private readonly IServiceProvider inner;
    private readonly object? key;

    private SchedulerScopedServiceProvider(IServiceProvider inner, object? key)
    {
        this.inner = inner;
        this.key = key;
    }

    /// <summary>
    /// Returns a provider that resolves the given scheduler's parts. The default scheduler's services
    /// are not keyed, so it needs no wrapper.
    /// </summary>
    public static IServiceProvider For(IServiceProvider provider, object? key)
    {
        return key is null ? provider : new SchedulerScopedServiceProvider(provider, key);
    }

    public object? GetService(Type serviceType)
    {
        return schedulerScoped.Contains(serviceType)
            ? inner.GetKeyedService(serviceType, key)
            : inner.GetService(serviceType);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetRequiredKeyedService(serviceType, serviceKey);
    }
}
