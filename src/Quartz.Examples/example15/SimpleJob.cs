using System;

using Common.Logging;

namespace Quartz.Examples.Example15
{
    /// <summary>
    /// This is just a simple job that gets fired off many times by example 15.
    /// </summary>
    /// <author>Bill Kratzer</author>
    /// <author>Marko Lahma (.NET)</author>
    public class SimpleJob : IJob
    {
        private static readonly ILog log = LogManager.GetLogger(typeof (SimpleJob));

        /// <summary>
        /// Called by the <see cref="IScheduler" /> when a
        /// <see cref="ITrigger" /> fires that is associated with the <see cref="IJob" />.
        /// </summary>
        public virtual void Execute(IJobExecutionContext context)
        {
            // This job simply prints out its job name and the
            // date and time that it is running
            JobKey jobKey = context.JobDetail.Key;
            log.InfoFormat("SimpleJob says: {0} executing at {1}", jobKey, DateTime.Now.ToString("r"));
        }
    }
}