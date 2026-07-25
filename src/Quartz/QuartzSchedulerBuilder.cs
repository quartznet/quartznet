using Microsoft.Extensions.DependencyInjection;

using Quartz.Configuration;
using Quartz.Spi;

namespace Quartz;

/// <summary>
/// Builds a scheduler without an application-supplied dependency injection container.
/// </summary>
/// <remarks>
/// <para>
/// Console applications, tests and anything else without a host use this instead of registering Quartz
/// into their own container. It is not a second construction path: it creates a container, applies the
/// same registrations <c>AddQuartz</c> applies, and builds the scheduler from it. Whatever works here
/// works identically under a host.
/// </para>
/// <para>
/// The builder owns the <see cref="IServiceProvider"/> it creates and disposes it when the returned
/// factory is disposed, so callers that never dispose behave exactly as they did with the old
/// process-lifetime scheduler.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var scheduler = await QuartzSchedulerBuilder.Create()
///     .ConfigureScheduler(options => options.InstanceName = "reporting")
///     .UseDefaultThreadPool(maxConcurrency: 20)
///     .UseInMemoryStore()
///     .BuildScheduler();
/// </code>
/// </example>
public sealed class QuartzSchedulerBuilder
{
    private readonly ServiceCollection services = [];

    private QuartzSchedulerBuilder()
    {
        services.AddQuartzScheduler();
    }

    /// <summary>
    /// Creates a new builder.
    /// </summary>
    public static QuartzSchedulerBuilder Create()
    {
        return new QuartzSchedulerBuilder();
    }

    /// <summary>
    /// The services the scheduler will be built from. Register your own implementations here to
    /// override the defaults — anything registered wins over the built-in registration.
    /// </summary>
    public IServiceCollection Services => services;

    /// <summary>
    /// Configures the scheduler itself.
    /// </summary>
    public QuartzSchedulerBuilder ConfigureScheduler(Action<QuartzSchedulerOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        services.Configure(configure);
        return this;
    }

    /// <summary>
    /// Uses the default thread pool with the given maximum concurrency.
    /// </summary>
    public QuartzSchedulerBuilder UseDefaultThreadPool(int maxConcurrency)
    {
        return UseDefaultThreadPool(options => options.MaxConcurrency = maxConcurrency);
    }

    /// <summary>
    /// Uses the default thread pool.
    /// </summary>
    public QuartzSchedulerBuilder UseDefaultThreadPool(Action<ThreadPoolOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return this;
    }

    /// <summary>
    /// Uses a thread pool the caller has already built.
    /// </summary>
    public QuartzSchedulerBuilder UseThreadPool(IThreadPool threadPool)
    {
        ArgumentNullException.ThrowIfNull(threadPool);
        services.AddSingleton(threadPool);
        return this;
    }

    /// <summary>
    /// Uses a job store the caller has already built.
    /// </summary>
    public QuartzSchedulerBuilder UseJobStore(IJobStore jobStore)
    {
        ArgumentNullException.ThrowIfNull(jobStore);
        services.AddSingleton(jobStore);
        return this;
    }

    /// <summary>
    /// Uses the in-memory job store, which does not survive process restarts.
    /// </summary>
    public QuartzSchedulerBuilder UseInMemoryStore(Action<InMemoryJobStoreOptions>? configure = null)
    {
        if (configure is not null)
        {
            services.Configure(configure);
        }

        return this;
    }

    /// <summary>
    /// Builds the scheduler factory, along with the container backing it.
    /// </summary>
    public ISchedulerFactory Build()
    {
        var provider = services.BuildServiceProvider(new ServiceProviderOptions
        {
            ValidateOnBuild = true,
            ValidateScopes = true,
        });

        return new OwnedSchedulerFactory(provider);
    }

    /// <summary>
    /// Builds the scheduler.
    /// </summary>
    public ValueTask<IScheduler> BuildScheduler(CancellationToken cancellationToken = default)
    {
        return Build().GetScheduler(cancellationToken);
    }

    /// <summary>
    /// A scheduler factory that owns the container it resolves from.
    /// </summary>
    private sealed class OwnedSchedulerFactory : ISchedulerFactory, IDisposable, IAsyncDisposable
    {
        private readonly ServiceProvider provider;
        private readonly ISchedulerFactory inner;

        public OwnedSchedulerFactory(ServiceProvider provider)
        {
            this.provider = provider;
            inner = provider.GetRequiredService<ISchedulerFactory>();
        }

        public ValueTask<IReadOnlyList<IScheduler>> GetAllSchedulers(CancellationToken cancellationToken = default)
        {
            return inner.GetAllSchedulers(cancellationToken);
        }

        public ValueTask<IScheduler> GetScheduler(CancellationToken cancellationToken = default)
        {
            return inner.GetScheduler(cancellationToken);
        }

        public ValueTask<IScheduler?> GetScheduler(string schedName, CancellationToken cancellationToken = default)
        {
            return inner.GetScheduler(schedName, cancellationToken);
        }

        public void Dispose()
        {
            provider.Dispose();
        }

        public ValueTask DisposeAsync()
        {
            return provider.DisposeAsync();
        }
    }
}
