using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Custom RAMJobStore for producing context switches.
/// </summary>
public class SlowRAMJobStore : RAMJobStore
{
    public SlowRAMJobStore(
        ILogger<RAMJobStore> logger,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider)
        : base(logger, signaler, timeProvider)
    {
    }

    public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow, CancellationToken cancellationToken = default)
    {
        var nextTriggers = await base.AcquireNextTriggers(noLaterThan, maxCount, timeWindow, cancellationToken);

        // Wait just a bit for hopefully having a context switch leading to the race condition
        await Task.Delay(10, cancellationToken);

        return nextTriggers;
    }
}