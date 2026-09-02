using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// Thrown when the <see cref="IJobFactory" /> cannot produce a job instance for a trigger that has
/// already fired.
/// </summary>
/// <remarks>
/// The failure happens before there is an <see cref="IJobExecutionContext" />, so no
/// <see cref="ITriggerListener" /> or <see cref="IJobListener" /> callback can be raised for it and
/// <see cref="ISchedulerListener.SchedulerError" /> is the only notification the scheduler makes.
/// This exception carries the identity of what failed, so that a listener can act on it without
/// parsing the message text.
/// </remarks>
public sealed class JobInstantiationException : SchedulerException
{
    internal JobInstantiationException(string message, TriggerFiredBundle bundle, Exception cause)
        : base(message, cause)
    {
        Trigger = bundle.Trigger;
        JobDetail = bundle.JobDetail;
        // A bundle is a firing, so the store has written the id by now.
        FireInstanceId = bundle.Trigger.FireInstanceId!;
    }

    /// <summary>
    /// The trigger whose firing could not be served.
    /// </summary>
    /// <remarks>
    /// Typed as <see cref="ITrigger" /> rather than <see cref="IOperableTrigger" />: this is here to
    /// be read, and mutating the scheduler's trigger from an error handler is not supported.
    /// </remarks>
    public ITrigger Trigger { get; }

    /// <summary>
    /// The job that could not be instantiated.
    /// </summary>
    public IJobDetail JobDetail { get; }

    /// <summary>
    /// Identifies this particular firing, matching <see cref="IJobExecutionContext.FireInstanceId" />
    /// of the execution that never started.
    /// </summary>
    public string FireInstanceId { get; }
}
