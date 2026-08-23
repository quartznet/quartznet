using Microsoft.Extensions.DependencyInjection;

namespace Quartz;

/// <summary>
/// A scheduler factory that owns the container its scheduler was built from.
/// </summary>
/// <remarks>
/// <para>
/// This is what <see cref="QuartzSchedulerBuilder.Build"/> returns. A scheduler built without an
/// application-supplied container has one of its own, and something has to own it — so the factory
/// does. Disposing the factory shuts down every scheduler that container built and then disposes the
/// container, which is what the hosted service does for an application that has one.
/// </para>
/// <para>
/// The shutdown does not wait for running jobs, which is the default
/// <see cref="QuartzHostedServiceOptions.WaitForJobsToComplete"/> and
/// <see cref="IScheduler.Shutdown"/> both carry — two Quartz-owned shutdown paths that disagreed about
/// whether a job gets to finish would be a trap. A caller that wants to wait says so and then disposes:
/// <c>await scheduler.Shutdown(waitForJobsToComplete: true)</c> leaves the factory with nothing left to
/// shut down, so the two compose.
/// </para>
/// <para>
/// Disposal is asynchronous where the caller can await it: shutting a scheduler down is asynchronous
/// work, and <see cref="IDisposable.Dispose"/> can only block on it. Prefer
/// <c>await using</c> over <c>using</c>.
/// </para>
/// <para>
/// Disposing twice does nothing the second time, and disposing a factory whose scheduler was never
/// asked for does nothing at all — a scheduler is never built merely to be torn down.
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
    private int disposed;

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
    /// Shuts down the schedulers this factory's container built, then disposes the container.
    /// </summary>
    /// <remarks>
    /// Blocking, because a synchronous door onto asynchronous work can do nothing else, and the
    /// alternative is what this used to do: return promptly with the scheduler still running. It also
    /// cannot be a bare container disposal, synchronous or not — a container that handed its
    /// <see cref="IScheduler"/> to anything holds a singleton implementing only
    /// <see cref="IAsyncDisposable"/>, and disposing such a container synchronously throws.
    /// </remarks>
    public void Dispose()
    {
        DisposeAsync().AsTask().GetAwaiter().GetResult();
    }

    /// <inheritdoc cref="Dispose" />
    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) == 1)
        {
            return;
        }

        try
        {
            await ShutdownSchedulers().ConfigureAwait(false);
        }
        finally
        {
            // Even when a shutdown failed: the container is this factory's to release, and leaking it
            // would leak the thread pool and the job store the failure left behind.
            await provider.DisposeAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Shuts down whatever this container actually built, reporting all the failures rather than the
    /// first one.
    /// </summary>
    /// <remarks>
    /// The repository asked here is this container's own, and a scheduler enters it when it is created
    /// and leaves it when it is shut down. So this is nothing at all for a factory whose scheduler was
    /// never asked for, and nothing again for one the caller has already shut down — neither case
    /// builds a scheduler in order to tear it down.
    /// </remarks>
    private async ValueTask ShutdownSchedulers()
    {
        List<Exception>? exceptions = null;
        foreach (IScheduler scheduler in await inner.GetAllSchedulers().ConfigureAwait(false))
        {
            try
            {
                await scheduler.Shutdown(waitForJobsToComplete: false).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                exceptions ??= [];
                exceptions.Add(e);
            }
        }

        if (exceptions is not null)
        {
            throw new AggregateException("One or more scheduler shutdowns failed.", exceptions);
        }
    }
}
