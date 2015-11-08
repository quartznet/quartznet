using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Core
{
    /// <summary>
    /// Custom RAMJobStore for producing context switches.
    /// </summary>
    public class SlowRAMJobStore : RAMJobStore
    {
        public override async Task<IReadOnlyList<IOperableTrigger>> AcquireNextTriggersAsync(DateTimeOffset noLaterThan, int maxCount, TimeSpan timeWindow)
        {
            var nextTriggers = await base.AcquireNextTriggersAsync(noLaterThan, maxCount, timeWindow);

            // Wait just a bit for hopefully having a context switch leading to the race condition
            await Task.Delay(10);

            return nextTriggers;
        }
    }
}