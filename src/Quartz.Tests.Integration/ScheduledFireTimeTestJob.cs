using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Quartz.Tests.Integration
{
    public class ScheduledFireTimeTestJob:IJob
    {

        public void Execute(IJobExecutionContext context)
        {
            // Cannot Assert anything here because we are on a background thread, so any exceptions here will not cause the test to fail
            ScheduledFireTimeUtc = context.ScheduledFireTimeUtc;
            // Could set as context.FireTimeUtc, but using the time now just in case there is something wrong with context.FireTimeUtc as well
            FireTimeUtc = DateTimeOffset.UtcNow;
        }

        public DateTimeOffset FireTimeUtc { get; set; }

        public DateTimeOffset? ScheduledFireTimeUtc { get; set; }
    }
}
