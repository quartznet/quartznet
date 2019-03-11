namespace Quartz
{
    /// <summary>
    /// Represents a job execution context which can be cancelled.
    /// </summary>
    public interface ICancellableJobExecutionContext : IJobExecutionContext
    {
        /// <summary>
        /// Cancels the execution of the job. It is the responsibility of the job instance to observe the cancellation token if it can be cancelled.
        /// </summary>
        void Cancel();
    }
}