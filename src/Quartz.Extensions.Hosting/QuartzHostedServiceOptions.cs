namespace Quartz
{
    public class QuartzHostedServiceOptions
    {
        /// <summary>
        /// if <see langword="true" /> the scheduler will not allow shutdown process
        /// to return until all currently executing jobs have completed.
        /// </summary>
        public bool WaitForJobsToComplete { get; set; }
    }
}