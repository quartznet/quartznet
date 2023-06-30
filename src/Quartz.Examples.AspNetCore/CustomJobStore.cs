using System;
using System.Threading;
using System.Threading.Tasks;

using Microsoft.Extensions.Logging;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Examples.AspNetCore;

public class CustomJobStore : RAMJobStore
{
    private readonly IServiceProvider serviceProvider;
    private readonly ILogger<CustomJobStore> logger;

    public CustomJobStore(
        IServiceProvider serviceProvider,
        ILogger<CustomJobStore> logger)
    {
        this.serviceProvider = serviceProvider;
        this.logger = logger;
    }

    public override async ValueTask Initialize(ITypeLoadHelper loadHelper, ISchedulerSignaler signaler, CancellationToken cancellationToken = default)
    {
        await base.Initialize(loadHelper, signaler, cancellationToken);
        logger.LogInformation("CustomJobStore has been initialized, service provider is {ServiceProviderType}", serviceProvider.GetType());
    }
}