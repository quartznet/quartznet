using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Configuration;

/// <summary>
/// A job factory registration Quartz made, which says the type of the factory it builds.
/// </summary>
/// <remarks>
/// <para>
/// Quartz registers a scheduler's parts as delegates, so each one is constructed from its own
/// scheduler's view of the container, and a delegate does not say what it constructs. Whether a
/// scheduler's job types are registered with the container depends on its job factory's type — see
/// <see cref="BuildsJobsFromContainer" /> — so the type is carried on the registration itself, and what
/// is read is the registration the container will resolve, whatever else was registered around it.
/// </para>
/// <para>
/// <c>UseJobFactory(instance)</c> does not make one, deliberately. An instance was constructed before this
/// container existed, so whatever it builds jobs from, it is not this container.
/// </para>
/// </remarks>
internal sealed class JobFactoryDescriptor : ServiceDescriptor
{
    private JobFactoryDescriptor(Type factoryType, Func<IServiceProvider, object> factory)
        : base(typeof(IJobFactory), factory, ServiceLifetime.Singleton)
    {
        FactoryType = factoryType;
    }

    private JobFactoryDescriptor(Type factoryType, object serviceKey, Func<IServiceProvider, object?, object> factory)
        : base(typeof(IJobFactory), serviceKey, factory, ServiceLifetime.Singleton)
    {
        FactoryType = factoryType;
    }

    /// <summary>
    /// The type of job factory the registration builds.
    /// </summary>
    public Type FactoryType { get; }

    /// <summary>
    /// Registers a scheduler's job factory, keyed for a named scheduler and unkeyed for the default one,
    /// unless the scheduler already has one.
    /// </summary>
    /// <param name="services">The service collection to register into.</param>
    /// <param name="schedulerKey">The scheduler's service key, or <see langword="null" /> for the default scheduler.</param>
    /// <param name="factoryType">The type <paramref name="create" /> builds.</param>
    /// <param name="create">Builds the factory, given the container and the scheduler's service key.</param>
    public static void TryAdd(
        IServiceCollection services,
        object? schedulerKey,
        Type factoryType,
        Func<IServiceProvider, object?, IJobFactory> create)
    {
        services.TryAdd(schedulerKey is null
            ? new JobFactoryDescriptor(factoryType, provider => create(provider, null))
            : new JobFactoryDescriptor(factoryType, schedulerKey, (provider, key) => create(provider, key)));
    }

    /// <summary>
    /// Whether the job factory a registration builds takes its jobs from the container: that is
    /// <see cref="MicrosoftDependencyInjectionJobFactory" />, the default, or a factory derived from it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Any other factory is the application's own, and builds its jobs its own way — from another
    /// container, very often — so a registration of the job type would be one nothing resolves, and
    /// container validation would fail it on dependencies only that factory supplies (#3930).
    /// </para>
    /// <para>
    /// An application's registration by type says its type. One made with an instance or a delegate of the
    /// application's own does not, and is taken to be the application's own factory: an instance cannot
    /// have been built over this container, and a delegate is how a factory of one's own is usually
    /// handed its dependencies.
    /// </para>
    /// </remarks>
    public static bool BuildsJobsFromContainer(ServiceDescriptor registration)
    {
        Type? factoryType = registration is JobFactoryDescriptor quartz
            ? quartz.FactoryType
            : RegisteredJobTypes.ImplementationType(registration);

        return factoryType is not null && typeof(MicrosoftDependencyInjectionJobFactory).IsAssignableFrom(factoryType);
    }
}
