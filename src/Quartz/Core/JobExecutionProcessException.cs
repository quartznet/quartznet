namespace Quartz.Core;

/// <summary>
/// This exception may be thrown when an error occurs during execution:
/// - Job execution
/// - JobListener methods
/// - TriggerListener methods
/// The exception ensures that the job execution context is transferred to the implementation
/// of the error handling method of the scheduler listener, to try to fix the problem.
/// </summary>
public sealed class JobExecutionProcessException : SchedulerException
{
    internal JobExecutionProcessException(IJobExecutionContext jobExecutionContext, Exception cause)
        : base("Job threw an unhandled exception", cause)
    {
        JobExecutionContext = jobExecutionContext;
    }

    internal JobExecutionProcessException(IJobListener listener, IJobExecutionContext jobExecutionContext, Exception cause)
        : base($"JobListener '{listener.Name}' threw exception: {cause.Message}.", cause)
    {
        JobExecutionContext = jobExecutionContext;
    }

    internal JobExecutionProcessException(ITriggerListener listener, IJobExecutionContext jobExecutionContext, Exception cause)
        : base($"TriggerListener '{listener.Name}' threw exception: {cause.Message}.", cause)
    {
        JobExecutionContext = jobExecutionContext;
    }

    public IJobExecutionContext JobExecutionContext { get; }
}