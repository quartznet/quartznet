using Microsoft.Extensions.DependencyInjection;

namespace Quartz;

/// <summary>
/// A scheduler factory that owns the container its scheduler was built from.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="QuartzSchedulerBuilder.Build"/> returns. A scheduler built without an
/// application-supplied container has one of its own, and something has to own it — so the factory
/// does, and disposing the factory disposes the container and everything in it, the scheduler
/// included.
/// </para>
/// <para>
/// Disposal is asynchronous where the caller can await it: shutting a scheduler down is asynchronous
/// work, and <see cref="IDisposable.Dispose"/> can only block on it. Prefer
/// <c>await using</c> over <c>using</c>.
/// </para>
/// <para>
/// A caller that never disposes behaves exactly as one did with the process-lifetime scheduler of
/// earlier versions: the scheduler runs until the process ends.
/// </para>
/// </remarks>
public sealed class StandaloneSchedulerFactory : ISchedulerFactory, IDisposable, IAsyncDisposable
{
    private readonly ServiceProvider provider;
    private readonly ISchedulerFactory inner;

    internal StandaloneSchedulerFactory(ServiceProvider provider)
    {
        this.provider = provider;
        inner = provider.GetRequiredService<ISchedulerFactory>();
    }

    /// <inheritdoc />
    public ValueTask<List<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
    {
        return inner.GetAllSchedulers(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
    {
        return inner.GetScheduler(cancellationToken);
    }

    /// <inheritdoc />
    public ValueTask<IScheduler?> LookupScheduler(string schedulerName, CancellationToken cancellationToken = default)
    {
        return inner.LookupScheduler(schedulerName, cancellationToken);
    }

    /// <summary>
    /// Disposes the container this factory owns, shutting its scheduler down.
    /// </summary>
    public void Dispose()
    {
        provider.Dispose();
    }

    /// <inheritdoc cref="Dispose" />
    public ValueTask DisposeAsync()
    {
        return provider.DisposeAsync();
    }
}
