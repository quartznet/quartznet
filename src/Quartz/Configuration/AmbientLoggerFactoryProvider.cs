using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Quartz.Diagnostics;

namespace Quartz.Configuration;

/// <summary>
/// The provider Quartz constructs a component from when the container it was given holds no
/// <see cref="ILoggerFactory" />: everything resolves as it would have, and a request for a factory is
/// answered with the ambient one rather than refused.
/// </summary>
/// <remarks>
/// <para>
/// Quartz registers no <see cref="ILoggerFactory" /> — a <c>TryAdd</c> of one would beat the
/// <c>services.AddLogging(…)</c> an application writes after <c>AddQuartz</c> and drop its providers in
/// silence (#3730). But a component Quartz builds may well ask for one: a job store of your own
/// registered with <c>UseJobStore&lt;T&gt;()</c>, a serializer, a connection provider, a listener. Those
/// constructors are the application's, they have always been satisfiable, and "you configured no
/// logging, so your job store cannot be built" is not an answer.
/// </para>
/// <para>
/// So the fallback lives here, on the construction path, rather than in the container: it is reached
/// only while Quartz is activating something, and it cannot shadow a registration because it is not
/// one. <see cref="ActivatorUtilities" /> asks <see cref="IServiceProviderIsService" /> which
/// constructor it can satisfy before resolving anything, so that is answered too — without it a
/// component with a second, shorter constructor would quietly get that one instead.
/// </para>
/// <para>
/// Applied by <see cref="SchedulerScopedServiceProvider.For" />, and only when the container really has
/// nothing: an application on a host, one that called <c>AddLogging</c> either side of
/// <c>AddQuartz</c>, and a standalone <see cref="QuartzSchedulerBuilder" /> all have a factory by the
/// time anything is built, and are handed their own provider unwrapped.
/// </para>
/// </remarks>
internal sealed class AmbientLoggerFactoryProvider
    : IKeyedServiceProvider, IServiceProviderIsKeyedService
{
    private readonly IServiceProvider inner;

    public AmbientLoggerFactoryProvider(IServiceProvider inner)
    {
        this.inner = inner;
    }

    public object? GetService(Type serviceType)
    {
        if (serviceType == typeof(ILoggerFactory))
        {
            return LogProviderLoggerFactory.Instance;
        }

        // A component handed "the container" keeps the fallback with it, because what it resolves later
        // is built the same way - the content initializer is the case that shows it, since the listeners
        // and middleware an application registered by type are activated from the provider it holds.
        if (serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService))
        {
            return this;
        }

        return inner.GetService(serviceType);
    }

    public object? GetKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetKeyedService(serviceType, serviceKey);
    }

    public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetRequiredKeyedService(serviceType, serviceKey);
    }

    public bool IsService(Type serviceType)
    {
        return serviceType == typeof(ILoggerFactory)
            || serviceType == typeof(IServiceProvider)
            || serviceType == typeof(IServiceProviderIsService)
            || serviceType == typeof(IServiceProviderIsKeyedService)
            || (inner.GetService<IServiceProviderIsService>()?.IsService(serviceType) ?? false);
    }

    public bool IsKeyedService(Type serviceType, object? serviceKey)
    {
        return inner.GetService<IServiceProviderIsKeyedService>()?.IsKeyedService(serviceType, serviceKey) ?? false;
    }
}
