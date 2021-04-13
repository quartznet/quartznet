using System;

namespace Quartz
{
    public class QuartzHostedServiceOptions
    {
        /// <summary>
        /// if <see langword="true" /> the scheduler will not allow shutdown process
        /// to return until all currently executing jobs have completed.
        /// </summary>
        public bool WaitForJobsToComplete { get; set; }

        /// <summary>
        /// if not <see langword="null" /> the scheduler will start after specified delay.
        /// </summary>
        public TimeSpan? StartDelay { get; set; }
    }
}
