namespace Quartz;

/// <summary>
/// A context bundle containing handles to various environment information, that
/// is given to a <see cref="JobDetail" /> instance as it is
/// executed, and to a <see cref="ITrigger" /> instance after the
/// execution completes.
/// </summary>
public interface IJobExecutionContext
{
    /// <summary>
    /// Get a handle to the <see cref="IScheduler" /> instance that fired the
    /// <see cref="IJob" />.
    /// </summary>
    IScheduler Scheduler { get; }

    /// <summary>
    /// Get a handle to the <see cref="ITrigger" /> instance that fired the
    /// <see cref="IJob" />.
    /// </summary>
    ITrigger Trigger { get; }

    /// <summary>
    /// Get a handle to the <see cref="ICalendar" /> referenced by the <see cref="ITrigger" />
    /// instance that fired the <see cref="IJob" />.
    /// </summary>
    ICalendar? Calendar { get; }

    /// <summary>
    /// If the <see cref="IJob" /> is being re-executed because of a 'recovery'
    /// situation, this method will return <see langword="true" />.
    /// </summary>
    bool Recovering { get; }

    /// <summary>
    /// Returns the <see cref="TriggerKey" /> of the originally scheduled and now recovering job.
    /// </summary>
    /// <remarks>
    /// When recovering a previously failed job execution this property returns the identity
    /// of the originally firing trigger. This recovering job will have been scheduled for
    /// the same firing time as the original job, and so is available via the
    /// <see cref="ScheduledFireTimeUtc" /> property. The original firing time of the job can be
    /// accessed via the <see cref="SchedulerConstants.FailedJobOriginalTriggerFireTime" />
    /// element of this job's <see cref="JobDataMap" />.
    /// </remarks>
    TriggerKey? RecoveringTriggerKey { get; }

    /// <summary>
    /// Gets the refire count.
    /// </summary>
    /// <value>The refire count.</value>
    int RefireCount { get; }

    /// <summary>
    /// How many times this occurrence has already been retried under the trigger's
    /// <see cref="ITrigger.RetryPolicy" />: <c>0</c> on a regular fire, <c>n</c> on the <c>n</c>-th
    /// retry.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Distinct from <see cref="RefireCount" />, which counts iterations of the in-process refire
    /// loop within a single firing — same context, same thread, nothing persisted. A retry is a
    /// fresh firing at a later instant, recorded in the job store, and it releases the execution
    /// slot while it waits.
    /// </para>
    /// <para>
    /// <c>0</c> for every trigger with no retry policy, which is the default.
    /// </para>
    /// </remarks>
    /// <seealso cref="Quartz.RetryPolicy" />
    int RetryAttempt { get; }

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
    /// <strong>Writing to it is safe, and it is this firing's own channel.</strong> The map is built
    /// once per firing by copying the two it merges, so a value put into it is visible to the job, to
    /// the rest of the middleware pipeline and to the listeners for as long as the firing lasts, and is
    /// seen by nothing else. It is the only per-firing bag there is, and passing something from a
    /// middleware to a listener is what it is for.
    /// </para>
    /// <para>
    /// <strong>Nothing written here is persisted.</strong> It is a copy: neither the job's nor the
    /// trigger's stored data map is touched, and the next firing starts from the stored values again.
    /// Data that has to outlive the firing goes into <c>JobDetail.JobDataMap</c> on a job marked
    /// <see cref="PersistJobDataAfterExecutionAttribute" />, which is what a job store writes back.
    /// </para>
    /// </remarks>
    JobDataMap MergedJobDataMap { get; }

    /// <summary>
    /// Get the <see cref="JobDetail" /> associated with the <see cref="IJob" />.
    /// </summary>
    IJobDetail JobDetail { get; }

    /// <summary>
    /// Get the instance of the <see cref="IJob" /> that was created for this
    /// execution.
    /// <para>
    /// Note: The Job instance is not available through remote scheduler
    /// interfaces.
    /// </para>
    /// </summary>
    IJob JobInstance { get; }

    /// <summary>
    /// The actual time the trigger fired. For instance the scheduled time may
    /// have been 10:00:00 but the actual fire time may have been 10:00:03 if
    /// the scheduler was too busy.
    /// </summary>
    /// <returns> Returns the fireTimeUtc.</returns>
    /// <seealso cref="ScheduledFireTimeUtc" />
    DateTimeOffset FireTimeUtc { get; }

    /// <summary>
    /// The scheduled time the trigger fired for. For instance the scheduled
    /// time may have been 10:00:00 but the actual fire time may have been
    /// 10:00:03 if the scheduler was too busy.
    /// </summary>
    /// <returns> Returns the scheduledFireTimeUtc.</returns>
    /// <seealso cref="FireTimeUtc" />
    DateTimeOffset? ScheduledFireTimeUtc { get; }

    /// <summary>
    /// Gets the previous fire time.
    /// </summary>
    /// <value>The previous fire time.</value>
    DateTimeOffset? PreviousFireTimeUtc { get; }

    /// <summary>
    /// Gets the next fire time.
    /// </summary>
    /// <value>The next fire time.</value>
    DateTimeOffset? NextFireTimeUtc { get; }

    /// <summary>
    /// Get the unique Id that identifies this particular firing instance of the
    /// trigger that triggered this job execution.  It is unique to this
    /// JobExecutionContext instance as well.
    /// </summary>
    ///  <returns>the unique fire instance id</returns>
    /// <seealso cref="IScheduler.InterruptFireInstance(System.String, System.Threading.CancellationToken)" />
    string FireInstanceId { get; }

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
    /// <para>
    /// It stays <see cref="object" />: a job writes it and a listener reads it, and the two agree on
    /// the type between themselves. Quartz cannot name that type without making
    /// <see cref="IJobExecutionContext" /> generic, which would type the whole scheduling API for the
    /// sake of a value it never looks at.
    /// </para>
    /// </remarks>
    object? Result { get; set; }

    /// <summary>
    /// The amount of time the job ran for.  The returned
    /// value will be <see cref="TimeSpan.MinValue" /> until the job has actually completed (or thrown an
    /// exception), and is therefore generally only useful to
    /// <see cref="IJobListener" />s and <see cref="ITriggerListener" />s.
    /// </summary>
    TimeSpan JobRunTime { get; }

    /// <summary>
    /// Returns the cancellation token which will be cancelled when the job cancellation has been requested via
    /// <see cref="IScheduler.Interrupt(Quartz.JobKey, System.Threading.CancellationToken)"/>
    /// or <see cref="IScheduler.InterruptFireInstance(System.String, System.Threading.CancellationToken)"/>.
    /// </summary>
    CancellationToken CancellationToken { get; }
}