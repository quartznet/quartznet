namespace Quartz.Diagnostics;

[Serializable]
internal sealed class JobDiagnosticData : IJobDiagnosticData
{
    public JobDiagnosticData(IJobExecutionContext context)
    {
        Trigger = context.Trigger;
        Recovering = context.Recovering;
        RecoveringTriggerKey = context.RecoveringTriggerKey;
        RefireCount = context.RefireCount;
        MergedJobDataMap = context.MergedJobDataMap;
        JobDetail = context.JobDetail;
        FireTimeUtc = context.FireTimeUtc;
        ScheduledFireTimeUtc = context.ScheduledFireTimeUtc;
        PreviousFireTimeUtc = context.PreviousFireTimeUtc;
        NextFireTimeUtc = context.NextFireTimeUtc;
        FireInstanceId = context.FireInstanceId;
        Result = context.Result;
        JobRunTime = context.JobRunTime;
    }

    /// <summary>
    /// Get a handle to the <see cref="ITrigger" /> instance that fired the
    /// <see cref="IJob" />.
    /// </summary>
    public ITrigger Trigger { get; }

    /// <summary>
    /// If the <see cref="IJob" /> is being re-executed because of a 'recovery'
    /// situation, this method will return <see langword="true" />.
    /// </summary>
    public bool Recovering { get; }

    /// <summary>
    /// Returns the <see cref="TriggerKey" /> of the originally scheduled and now recovering job.
    /// </summary>
    /// <remarks>
    /// When recovering a previously failed job execution this property returns the identity
    /// of the originally firing trigger. This recovering job will have been scheduled for
    /// the same firing time as the original job, and so is available via the
    /// <see cref="ScheduledFireTimeUtc" /> property. The original firing time of the job can be
    /// accessed via the <see cref="SchedulerConstants.FailedJobOriginalTriggerFiretime" />
    /// element of this job's <see cref="JobDataMap" />.
    /// </remarks>
    public TriggerKey? RecoveringTriggerKey { get; }

    /// <summary>
    /// Gets the refire count.
    /// </summary>
    /// <value>The refire count.</value>
    public int RefireCount { get; }

    /// <summary>
    /// Get the convenience <see cref="JobDataMap" /> of this execution context.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The <see cref="JobDataMap" /> found on this object serves as a convenience -
    /// it is a merge of the <see cref="JobDataMap" /> found on the
    /// <see cref="JobDetail" /> and the one found on the <see cref="ITrigger" />, with
    /// the value in the latter overriding any same-named values in the former.
    /// <i>It is thus considered a 'best practice' that the Execute code of a Job
    /// retrieve data from the JobDataMap found on this object.</i>
    /// </para>
    /// <para>
    /// NOTE: Do not expect value 'set' into this JobDataMap to somehow be
    /// set back onto a job's own JobDataMap.
    /// </para>
    /// <para>
    /// Attempts to change the contents of this map typically result in an
    /// illegal state.
    /// </para>
    /// </remarks>
    public JobDataMap MergedJobDataMap { get; }

    /// <summary>
    /// Get the <see cref="JobDetail" /> associated with the <see cref="IJob" />.
    /// </summary>
    public IJobDetail JobDetail { get; }

    /// <summary>
    /// The actual time the trigger fired. For instance the scheduled time may
    /// have been 10:00:00 but the actual fire time may have been 10:00:03 if
    /// the scheduler was too busy.
    /// </summary>
    /// <returns> Returns the fireTimeUtc.</returns>
    /// <seealso cref="ScheduledFireTimeUtc" />
    public DateTimeOffset FireTimeUtc { get; }

    /// <summary>
    /// The scheduled time the trigger fired for. For instance the scheduled
    /// time may have been 10:00:00 but the actual fire time may have been
    /// 10:00:03 if the scheduler was too busy.
    /// </summary>
    /// <returns> Returns the scheduledFireTimeUtc.</returns>
    /// <seealso cref="FireTimeUtc" />
    public DateTimeOffset? ScheduledFireTimeUtc { get; }

    /// <summary>
    /// Gets the previous fire time.
    /// </summary>
    /// <value>The previous fire time.</value>
    public DateTimeOffset? PreviousFireTimeUtc { get; }

    /// <summary>
    /// Gets the next fire time.
    /// </summary>
    /// <value>The next fire time.</value>
    public DateTimeOffset? NextFireTimeUtc { get; }

    /// <summary>
    /// Get the unique Id that identifies this particular firing instance of the
    /// trigger that triggered this job execution.  It is unique to this
    /// JobExecutionContext instance as well.
    /// </summary>
    ///  <returns>the unique fire instance id</returns>
    /// <seealso cref="IScheduler.Interrupt(System.String, System.Threading.CancellationToken)" />
    public string FireInstanceId { get; }

    /// <summary>
    /// Returns the result (if any) that the <see cref="IJob" /> set before its
    /// execution completed (the type of object set as the result is entirely up
    /// to the particular job).
    /// </summary>
    /// <remarks>
    /// <para>
    /// The result itself is meaningless to Quartz, but may be informative
    /// to <see cref="IJobListener" />s or
    /// <see cref="ITriggerListener" />s that are watching the job's
    /// execution.
    /// </para>
    ///
    /// Set the result (if any) of the <see cref="IJob" />'s execution (the type of
    /// object set as the result is entirely up to the particular job).
    ///
    /// <para>
    /// The result itself is meaningless to Quartz, but may be informative
    /// to <see cref="IJobListener" />s or
    /// <see cref="ITriggerListener" />s that are watching the job's
    /// execution.
    /// </para>
    /// </remarks>
    public object? Result { get; }

    /// <summary>
    /// The amount of time the job ran for.  The returned
    /// value will be <see cref="TimeSpan.MinValue" /> until the job has actually completed (or thrown an
    /// exception), and is therefore generally only useful to
    /// <see cref="IJobListener" />s and <see cref="ITriggerListener" />s.
    /// </summary>
    public TimeSpan JobRunTime { get; }
}