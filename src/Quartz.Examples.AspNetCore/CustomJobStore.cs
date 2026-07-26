using Microsoft.Extensions.Logging;

using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Examples.AspNetCore;

/// <summary>
/// Shows a custom job store taking dependencies of its own alongside the ones its base class needs.
/// </summary>
public class CustomJobStore : RAMJobStore
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<CustomJobStore> logger;

    public CustomJobStore(
        ILoggerFactory loggerFactory,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider,
        IServiceProvider serviceProvider,
        ILogger<CustomJobStore> logger)
        : base(loggerFactory, signaler, timeProvider)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    public override async ValueTask Initialize(CancellationToken cancellationToken = default)
    {
        await base.Initialize(cancellationToken);
        logger.LogInformation("CustomJobStore has been initialized, service provider is {ServiceProviderType}", serviceProvider.GetType());
    }
}
