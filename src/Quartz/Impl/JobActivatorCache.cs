using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Impl;

internal sealed class JobActivatorCache
{
    private readonly ConcurrentDictionary<Type, ObjectFactory> activatorCache = new();

    public IJob CreateInstance(
        IServiceProvider serviceProvider,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] Type jobType)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        ArgumentNullException.ThrowIfNull(jobType);

        // Before a factory is built for it, let alone invoked: ActivatorUtilities constructs whatever it
        // is handed, and the cast below would only find out afterwards.
        JobType.EnsureIsJob(jobType);

        // Looked up before it is built rather than through a GetOrAdd factory, because a lambda's
        // parameter carries no annotation and the job type would reach ActivatorUtilities with nothing
        // said about its constructors. The caching is the same: a race builds two factories, stores one.
        if (activatorCache.TryGetValue(jobType, out var factory))
        {
            return (IJob) factory(serviceProvider, null);
        }

        factory = activatorCache.GetOrAdd(jobType, ActivatorUtilities.CreateFactory(jobType, Type.EmptyTypes));
        return (IJob) factory(serviceProvider, null);
    }
}