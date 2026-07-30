using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;
using Quartz.Impl;
using Quartz.Extensibility;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// Custom RAMJobStore for producing context switches.
/// </summary>
public class SlowRAMJobStore : RAMJobStore
{
    public SlowRAMJobStore(
        ILoggerFactory loggerFactory,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider)
        : base(loggerFactory, signaler, timeProvider)
    {
    }

    public override async ValueTask<List<IOperableTrigger>> AcquireNextTriggers(TriggerAcquisitionRequest request, CancellationToken cancellationToken = default)
    {
        var nextTriggers = await base.AcquireNextTriggers(request, cancellationToken);

        // Wait just a bit for hopefully having a context switch leading to the race condition
        await Task.Delay(10, cancellationToken);

        return nextTriggers;
    }
}