using Microsoft.Extensions.Logging;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Tests;

namespace Quartz.Tests.Unit.Core;

/// <summary>
/// A job store that wraps <see cref="RAMJobStore"/> and slows acquisition down, for producing context
/// switches.
/// </summary>
public class SlowRAMJobStore : DelegatingJobStore
{
    public SlowRAMJobStore(
        ILoggerFactory loggerFactory,
        ISchedulerSignaler signaler,
        TimeProvider timeProvider)
        : base(new RAMJobStore(loggerFactory, signaler, timeProvider))
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
