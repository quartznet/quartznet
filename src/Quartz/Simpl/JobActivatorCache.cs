using System.Collections.Concurrent;

using Microsoft.Extensions.DependencyInjection;

namespace Quartz.Simpl;

internal sealed class JobActivatorCache
{
    private readonly ConcurrentDictionary<Type, ObjectFactory> activatorCache = new();
    private readonly Func<Type, ObjectFactory> createFactory = type => ActivatorUtilities.CreateFactory(type, Type.EmptyTypes);

    public IJob CreateInstance(IServiceProvider serviceProvider, Type jobType)
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        ArgumentNullException.ThrowIfNull(jobType);

        var factory = activatorCache.GetOrAdd(jobType, createFactory);
        return (IJob) factory(serviceProvider, null);
    }
}