using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

using Quartz.Core;
using Quartz.Impl;
using Quartz.Impl.AdoJobStore;
using Quartz.Impl.AdoJobStore.Common;
using Quartz.Serialization.Json;
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
internal sealed class SchedulerScopedServiceProvider
    : IKeyedServiceProvider, IServiceProviderIsKeyedService, IServiceScopeFactory
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
        typeof(IObjectSerializer),
        typeof(SystemTextJsonSerializerRegistry),
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

    /// <summary>
    /// The options a scheduler's components ask for as <see cref="IOptions{TOptions}"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A scheduler's options are <em>named</em> options, but <see cref="IOptions{TOptions}"/> always
    /// resolves the unnamed instance. A named scheduler's job store would therefore be configured from
    /// the default scheduler's settings, which is the same cross-wiring this class exists to prevent —
    /// so the request is answered from the monitor, under this scheduler's name.
    /// </para>
    /// <para>
    /// The map is explicit rather than generic because closing <c>OptionsWrapper&lt;&gt;</c> over a
    /// runtime type is not something a trimmer can follow.
    /// </para>
    /// </remarks>
    private static readonly Dictionary<Type, Func<IServiceProvider, string, object>> namedOptions = new()
    {
        [typeof(IOptions<QuartzSchedulerOptions>)] = static (p, name) => Named<QuartzSchedulerOptions>(p, name),
        [typeof(IOptions<ThreadPoolOptions>)] = static (p, name) => Named<ThreadPoolOptions>(p, name),
        [typeof(IOptions<InMemoryJobStoreOptions>)] = static (p, name) => Named<InMemoryJobStoreOptions>(p, name),
        [typeof(IOptions<AdoJobStoreOptions>)] = static (p, name) => Named<AdoJobStoreOptions>(p, name),
        [typeof(IOptions<QuartzOptions>)] = static (p, name) => Named<QuartzOptions>(p, name),
    };

    private static OptionsWrapper<T> Named<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicParameterlessConstructor)] T>(
        IServiceProvider provider,
        string name) where T : class
    {
        return new OptionsWrapper<T>(provider.GetRequiredService<IOptionsMonitor<T>>().Get(name));
    }

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
        if (schedulerScoped.Contains(serviceType))
        {
            return inner.GetKeyedService(serviceType, key);
        }

        if (namedOptions.TryGetValue(serviceType, out var options))
        {
            return options(inner, key as string ?? Options.DefaultName);
        }

        // A component handed "the container" so it can resolve things later must keep resolving this
        // scheduler's parts, not the default scheduler's — plugins built from a type name are the case
        // that makes the difference visible. The same goes for the "is this registered?" services, which
        // ActivatorUtilities consults while choosing a constructor, and for the scope factory: a job
        // scope that resolved from the raw container would build jobs with the wrong scheduler's parts.
        if (serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService)
            || serviceType == typeof(IServiceScopeFactory))
        {
            return this;
        }

        return inner.GetService(serviceType);
    }

    /// <summary>
    /// Creates a scope whose provider still resolves this scheduler's parts.
    /// </summary>
    /// <remarks>
    /// Jobs are built inside a scope. Without this the scope comes straight from the container, so a job
    /// that takes an <see cref="ISchedulerFactory"/> is handed the default scheduler's — or, when only
    /// named schedulers exist, cannot be constructed at all.
    /// </remarks>
    public IServiceScope CreateScope()
    {
        return new Scope(inner.GetRequiredService<IServiceScopeFactory>().CreateScope(), key);
    }

    private sealed class Scope : IServiceScope, IAsyncDisposable
    {
        private readonly IServiceScope scope;

        public Scope(IServiceScope scope, object? key)
        {
            this.scope = scope;
            ServiceProvider = For(scope.ServiceProvider, key);
        }

        public IServiceProvider ServiceProvider { get; }

        public void Dispose() => scope.Dispose();

        /// <summary>
        /// Disposes the wrapped scope asynchronously where it supports it.
        /// </summary>
        /// <remarks>
        /// Jobs are torn down through <see cref="IAsyncDisposable"/>. Without it here, a scoped service
        /// that is only async-disposable throws when the container disposes the scope synchronously.
        /// </remarks>
        public ValueTask DisposeAsync()
        {
            if (scope is IAsyncDisposable asyncDisposable)
            {
                return asyncDisposable.DisposeAsync();
            }

            scope.Dispose();
            return default;
        }
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetRequiredKeyedService(serviceType, serviceKey);
    }

    /// <summary>
    /// Answers what this scheduler can be given, not what the container holds unkeyed.
    /// </summary>
    /// <remarks>
    /// <see cref="ActivatorUtilities"/> asks this before choosing a constructor, and treats a service it
    /// is told does not exist as a parameter it cannot supply. Answering from the container directly
    /// would report every one of a named scheduler's own parts as missing, so a component with more than
    /// one constructor gets the wrong one chosen — or is rejected outright.
    /// </remarks>
    public bool IsService(Type serviceType)
    {
        if (schedulerScoped.Contains(serviceType))
        {
            return IsKeyedService(serviceType, key);
        }

        if (namedOptions.ContainsKey(serviceType)
            || serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService)
            || serviceType == typeof(IServiceScopeFactory))
        {
            return true;
        }

        return inner.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false;
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetService<IServiceProviderIsKeyedService>()?.IsKeyedService(serviceType, serviceKey) ?? false;
    }
}
