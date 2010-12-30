namespace Quartz.Tests.Integration.ExceptionPolicy
{
    public class ExceptionJob : IJob
    {
        public static int LaunchCount = 0;
        public static bool Refire = false;
        public static bool UnscheduleFiringTrigger = false;
        public static bool UnscheduleAllTriggers = false;
        public static bool ThrowsException = true;

        /// <summary>
        /// Called by the <see cref="IScheduler"/> when a <see cref="ITrigger"/>
        /// fires that is associated with the <see cref="IJob"/>.
        /// </summary>
        /// <param name="context">The execution context.</param>
        /// <remarks>
        /// The implementation may wish to set a  result object on the
        /// JobExecutionContext before this method exits.  The result itself
        /// is meaningless to Quartz, but may be informative to
        /// <see cref="IJobListener"/>s or
        /// <see cref="ITriggerListener"/>s that are watching the job's
        /// execution.
        /// </remarks>
        public void Execute(IJobExecutionContext context)
        {
            LaunchCount++;
            if (ThrowsException)
            {
                JobExecutionException toThrow = new JobExecutionException("test exception");
                toThrow.RefireImmediately = Refire;
                toThrow.UnscheduleFiringTrigger = UnscheduleFiringTrigger;
                toThrow.UnscheduleAllTriggers = UnscheduleAllTriggers;
               
                throw toThrow;
            }
        }
    }
}
