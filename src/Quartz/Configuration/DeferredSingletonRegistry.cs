using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Configuration;

/// <summary>
/// Holds singleton registrations captured during deferred Quartz configuration
/// (<c>AddQuartz(Action&lt;IServiceCollectionQuartzConfigurator, IServiceProvider&gt;)</c>).
/// The service provider is already built when the deferred configuration callback runs, so
/// these registrations cannot be added to the container itself; instead the scheduler
/// factories consult this registry when instantiating components, constructing the
/// implementation types with constructor injection against the root provider.
/// </summary>
internal sealed class DeferredSingletonRegistry
{
    private readonly Lock syncRoot = new();
    private readonly List<Registration> registrations = new();
    private readonly Dictionary<Type, object> instances = new();
    private readonly HashSet<Type> resolutionStack = new();

    public void Register(Type serviceType, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
    {
        lock (syncRoot)
        {
            registrations.Add(new Registration(serviceType, implementationType));
        }
    }

    /// <summary>
    /// Resolves an instance for the given service type if it has been registered, returns null otherwise.
    /// Instances are cached to preserve singleton semantics; the last registration for a service type
    /// wins, mirroring container behavior. Other deferred registrations are available as constructor
    /// dependencies during instantiation.
    /// </summary>
    public object? Resolve(Type serviceType, IServiceProvider serviceProvider)
    {
        lock (syncRoot)
        {
            if (instances.TryGetValue(serviceType, out var existing))
            {
                return existing;
            }

            Registration? registration = FindRegistration(serviceType);
            if (registration is null)
            {
                return null;
            }

            if (!resolutionStack.Add(serviceType))
            {
                throw new InvalidOperationException($"A circular dependency was detected resolving deferred singleton registration for service type {serviceType}.");
            }

            try
            {
                var instance = ActivatorUtilities.CreateInstance(WrapServiceProvider(serviceProvider), registration.ImplementationType);
                instances[serviceType] = instance;
                return instance;
            }
            finally
            {
                resolutionStack.Remove(serviceType);
            }
        }
    }

    /// <summary>
    /// Determines whether the registry has a registration for the given service type.
    /// </summary>
    public bool IsRegistered(Type serviceType)
    {
        lock (syncRoot)
        {
            return instances.ContainsKey(serviceType) || FindRegistration(serviceType) is not null;
        }
    }

    // Caller must hold syncRoot. Returns the most recent matching registration (last-wins).
    private Registration? FindRegistration(Type serviceType)
    {
        for (var i = registrations.Count - 1; i >= 0; i--)
        {
            if (registrations[i].ServiceType == serviceType)
            {
                return registrations[i];
            }
        }
        return null;
    }

    /// <summary>
    /// Wraps a service provider so that deferred registrations are available as constructor
    /// dependencies for types constructed via <see cref="ActivatorUtilities"/>. Services from
    /// the container take precedence; the registry is a fallback for registrations that could
    /// not be added to the already-built container. When the registry is empty the provider is
    /// returned unchanged so that the common non-deferred path keeps the container's full
    /// service provider capabilities (constructor selection, keyed services).
    /// </summary>
    public IServiceProvider WrapServiceProvider(IServiceProvider serviceProvider)
    {
        lock (syncRoot)
        {
            if (registrations.Count == 0)
            {
                return serviceProvider;
            }
        }
        return new RegistryAwareServiceProvider(this, serviceProvider);
    }

    private sealed class RegistryAwareServiceProvider : IServiceProvider, IServiceProviderIsService, IKeyedServiceProvider
    {
        private readonly DeferredSingletonRegistry registry;
        private readonly IServiceProvider inner;

        public RegistryAwareServiceProvider(DeferredSingletonRegistry registry, IServiceProvider inner)
        {
            this.registry = registry;
            this.inner = inner;
        }

        public object? GetService(Type serviceType)
        {
            return inner.GetService(serviceType) ?? registry.Resolve(serviceType, inner);
        }

        // ActivatorUtilities consults IServiceProviderIsService for best-constructor selection;
        // surface both the container's knowledge and the registry's registrations so that
        // wrapping does not degrade constructor selection.
        public bool IsService(Type serviceType)
        {
            if (registry.IsRegistered(serviceType))
            {
                return true;
            }
            return inner is IServiceProviderIsService isService && isService.IsService(serviceType);
        }

        public object? GetKeyedService(Type serviceType, object? serviceKey)
        {
            if (inner is IKeyedServiceProvider keyedServiceProvider)
            {
                return keyedServiceProvider.GetKeyedService(serviceType, serviceKey);
            }
            return null;
        }

        public object GetRequiredKeyedService(Type serviceType, object? serviceKey)
        {
            if (inner is IKeyedServiceProvider keyedServiceProvider)
            {
                return keyedServiceProvider.GetRequiredKeyedService(serviceType, serviceKey);
            }
            throw new InvalidOperationException("The underlying service provider does not support keyed services.");
        }
    }

    private sealed class Registration
    {
        public Registration(Type serviceType, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type implementationType)
        {
            ServiceType = serviceType;
            ImplementationType = implementationType;
        }

        public Type ServiceType { get; }

        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)]
        public Type ImplementationType { get; }
    }
}
