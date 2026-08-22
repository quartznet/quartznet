using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;

namespace Quartz.Examples.AspNetCore;

/// <summary>
/// Shows a job store of your own, registered through <c>UsePersistentStore&lt;T&gt;</c> and taking
/// dependencies of its own from the container.
/// </summary>
/// <remarks>
/// A store that only adds behaviour around the edges - logging, metrics, tenant routing - derives from
/// <see cref="DelegatingJobStore" />, wraps the store it wants that behaviour on top of, and overrides
/// only the operations it actually changes. A store that keeps scheduling data somewhere new implements
/// <see cref="IJobStore" /> directly instead.
/// </remarks>
public sealed class CustomJobStore : DelegatingJobStore
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<CustomJobStore> logger;

    public CustomJobStore(
        ILoggerFactory loggerFactory,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider,
        IServiceProvider serviceProvider,
        ILogger<CustomJobStore> logger)
        : base(new RAMJobStore(loggerFactory, signaler, timeProvider))
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    public override async ValueTask Initialize(SchedulerIdentity identity, CancellationToken cancellationToken = default)
    {
        await base.Initialize(identity, cancellationToken);
        logger.LogInformation("CustomJobStore has been initialized, service provider is {ServiceProviderType}", serviceProvider.GetType());
    }
}
