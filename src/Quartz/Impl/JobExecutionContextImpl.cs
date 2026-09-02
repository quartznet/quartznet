#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using Quartz.Core;
using Quartz.Extensibility;

namespace Quartz.Impl;

/// <summary>
/// A context bundle containing handles to various environment information, that
/// is given to a <see cref="JobDetail" /> instance as it is
/// executed, and to a <see cref="ITrigger" /> instance after the
/// execution completes.
/// </summary>
/// <remarks>
/// <para>
/// The <see cref="JobDataMap" /> found on this object (via the
/// <see cref="MergedJobDataMap" /> method) serves as a convenience -
/// it is a merge of the <see cref="JobDataMap" /> found on the
/// <see cref="JobDetail" /> and the one found on the <see cref="ITrigger" />, with
/// the value in the latter overriding any same-named values in the former.
/// <i>It is thus considered a 'best practice' that the Execute code of a Job
/// retrieve data from the JobDataMap found on this object</i>
/// </para>
/// <para>
/// That map is this firing's own copy: writing to it is safe and reaches the job, the middleware and
/// the listeners, and none of it is written back to what the job or the trigger has stored.
/// </para>
///
/// <para>
/// A context exists only inside the process running the job, and only for as long as it runs. The
/// scheduler does not hand it out: <see cref="IScheduler.QueryFireInstances" /> lists firings across the
/// cluster as <see cref="FireInstance" /> projections, which carry keys, times and the owning node but
/// none of the live state below. Code that needs the live state — the job instance, the merged job data,
/// the result, the cancellation handle — gets it from an <see cref="IJobListener" /> of its own, which is
/// handed the context and can keep it for the duration of the execution.
/// </para>
/// </remarks>
/// <seealso cref="JobDetail" />
/// <seealso cref="IScheduler" />
/// <seealso cref="IJob" />
/// <seealso cref="ITrigger" />
/// <seealso cref="JobDataMap" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
#pragma warning disable CA1708
public sealed class JobExecutionContextImpl : IInterruptableJobExecutionContext, IJobInputSource, IDisposable
#pragma warning restore CA1708
{
    private readonly IJobInputSerializer? inputSerializer;

    private readonly ITrigger trigger;
    private readonly IJobDetail jobDetail;

    /// <summary>
    /// The merged map, published only once it is fully populated. Volatile because the fast path in
    /// <see cref="MergedJobDataMap" /> reads it without taking <see cref="lazyInitLock" />.
    /// </summary>
    private volatile JobDataMap? jobDataMap;

    private readonly IScheduler scheduler;

    private int numRefires;
    private TimeSpan? jobRunTime;

    /// <summary>
    /// Volatile for the same reason as <see cref="jobDataMap" />: the fast path reads it outside the lock.
    /// </summary>
    private volatile CancellationTokenSource? cancellationTokenSource;

    internal readonly IJob jobInstance;

    private readonly Lock lazyInitLock = new();

    /// <summary>
    /// Create a JobExecutionContext with the given context data.
    /// </summary>
    /// <param name="scheduler">The scheduler this firing belongs to.</param>
    /// <param name="firedBundle">What the job store handed back when the trigger fired.</param>
    /// <param name="job">The job instance the factory built for this firing.</param>
    /// <param name="inputSerializer">
    /// What <see cref="JobExecutionContextInputExtensions.GetInput{TInput}" /> and an
    /// <see cref="IJob{TInput}" /> read a stored input with. Quartz hands the scheduler's own; a context
    /// built by hand without one reports a <see cref="SchedulerException" /> if a job asks it for a
    /// stored input, rather than reflecting its way to an answer.
    /// </param>
    public JobExecutionContextImpl(
        IScheduler scheduler,
        TriggerFiredBundle firedBundle,
        IJob job,
        IJobInputSerializer? inputSerializer = null)
    {
        this.scheduler = scheduler;
        this.inputSerializer = inputSerializer;
        trigger = firedBundle.Trigger;
        Calendar = firedBundle.Calendar;
        jobDetail = firedBundle.JobDetail;
        jobInstance = job;
        Recovering = firedBundle.Recovering;
        FireTimeUtc = firedBundle.FireTimeUtc;
        ScheduledFireTimeUtc = firedBundle.ScheduledFireTimeUtc;
        PreviousFireTimeUtc = firedBundle.PreviousFireTimeUtc;
        NextFireTimeUtc = firedBundle.NextFireTimeUtc;
    }

    /// <summary>
    /// Get a handle to the <see cref="IScheduler" /> instance that fired the
    /// <see cref="IJob" />.
    /// </summary>
    public IScheduler Scheduler => scheduler;

    /// <inheritdoc />
    IJobInputSerializer? IJobInputSource.JobInputSerializer => inputSerializer;

    /// <summary>
    /// Get a handle to the <see cref="ITrigger" /> instance that fired the
    /// <see cref="IJob" />.
    /// </summary>
    public ITrigger Trigger => trigger;

    /// <summary>
    /// Get a handle to the <see cref="ICalendar" /> referenced by the <see cref="ITrigger" />
    /// instance that fired the <see cref="IJob" />.
    /// </summary>
    public ICalendar? Calendar { get; }

    /// <summary>
    /// If the <see cref="IJob" /> is being re-executed because of a 'recovery'
    /// situation, this method will return <see langword="true" />.
    /// </summary>
    public bool Recovering { get; }

    /// <inheritdoc />
    public TriggerKey? RecoveringTriggerKey
    {
        get
        {
            if (Recovering)
            {
                var map = MergedJobDataMap;
                var triggerName = map.GetString(SchedulerConstants.FailedJobOriginalTriggerName)!;
                var triggerGroup = map.GetString(SchedulerConstants.FailedJobOriginalTriggerGroup)!;
                return new TriggerKey(triggerName, triggerGroup);
            }

            return null;
        }
    }

    /// <summary>
    /// Gets the refire count.
    /// </summary>
    /// <value>The refire count.</value>
    public int RefireCount => numRefires;

    /// <summary>
    /// How many times this occurrence has already been retried under the trigger's retry policy.
    /// </summary>
    /// <remarks>
    /// Read from the trigger this firing was handed, which is the copy the job store fired: the store
    /// wrote the attempt when it scheduled the retry and read it back when it acquired the trigger, so
    /// this is the count as the store has it and not something the run shell keeps.
    /// </remarks>
    public int RetryAttempt => Trigger.RetryAttempt;

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
    /// Data that has to outlive the firing goes into <see cref="IJobDetail.JobDataMap" /> on a job
    /// marked <see cref="PersistJobDataAfterExecutionAttribute" />, which is what a job store writes
    /// back.
    /// </para>
    /// </remarks>
    public JobDataMap MergedJobDataMap
    {
        get
        {
            JobDataMap? current = jobDataMap;
            if (current is not null)
            {
                return current;
            }

            lock (lazyInitLock)
            {
                current = jobDataMap;
                if (current is not null)
                {
                    return current;
                }

                // Merge into a local and publish the reference only once it is fully populated: the
                // fast path above reads the field without the lock, so a reference stored first and
                // filled afterwards would let a racing reader see a half-built map.
                JobDataMap merged = new JobDataMap(jobDetail.JobDataMap.Count + trigger.JobDataMap.Count);
                foreach (var pair in jobDetail.JobDataMap)
                {
                    merged[pair.Key] = pair.Value;
                }
                foreach (var pair in trigger.JobDataMap)
                {
                    merged[pair.Key] = pair.Value;
                }

                jobDataMap = merged;
                return merged;
            }
        }
    }

    /// <summary>
    /// Get the <see cref="JobDetail" /> associated with the <see cref="IJob" />.
    /// </summary>
    public IJobDetail JobDetail => jobDetail;

    /// <summary>
    /// Get the instance of the <see cref="IJob" /> that was created for this
    /// execution.
    /// <para>
    /// Note: The Job instance is not available through remote scheduler
    /// interfaces.
    /// </para>
    /// </summary>
    public IJob JobInstance => jobInstance;

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
    public object? Result { get; set; }

    /// <inheritdoc />
    public TimeSpan JobRunTime
    {
        get
        {
            if (jobRunTime is null)
            {
                // we are still in progress, calculate dynamically
                return DateTimeOffset.UtcNow - FireTimeUtc;
            }

            return jobRunTime.Value;
        }
        internal set => jobRunTime = value;
    }

    /// <summary>
    /// Increments the refire count.
    /// </summary>
    /// <remarks>
    /// Both this and the <see cref="JobRunTime" /> setter record what the scheduler observed while
    /// running the job. <see cref="Core.JobRunShell" /> is the only caller, and a job or listener
    /// writing either would be reporting a fire that never happened.
    /// </remarks>
    internal void IncrementRefireCount()
    {
        Interlocked.Increment(ref numRefires);
    }

    /// <summary>
    /// Returns a <see cref="System.String"/> that represents the current <see cref="System.Object"/>.
    /// </summary>
    /// <returns>
    /// A <see cref="System.String"/> that represents the current <see cref="System.Object"/>.
    /// </returns>
    public override string ToString()
    {
        return
            $"JobExecutionContext: trigger: '{Trigger.Key}' job: '{JobDetail.Key}' fireTimeUtc: '{FireTimeUtc:r}' scheduledFireTimeUtc: '{ScheduledFireTimeUtc:r}' previousFireTimeUtc: '{PreviousFireTimeUtc:r}' nextFireTimeUtc: '{NextFireTimeUtc:r}' recovering: {Recovering} refireCount: {RefireCount}";
    }

    void IInterruptableJobExecutionContext.Interrupt()
    {
        CancellationTokenSource.Cancel();
    }

    /// <inheritdoc />
    /// <remarks>
    /// Never null here, although <see cref="IOperableTrigger.FireInstanceId" /> is: a context exists
    /// only for a firing, and a store writes the id as it hands the trigger over.
    /// </remarks>
    public string FireInstanceId => ((IOperableTrigger) trigger).FireInstanceId!;

    /// <inheritdoc />
    public CancellationToken CancellationToken => CancellationTokenSource.Token;

    /// <summary>
    /// Lazily initializes the <see cref="CancellationTokenSource"/>.
    /// </summary>
    private CancellationTokenSource CancellationTokenSource
    {
        get
        {
            CancellationTokenSource? current = cancellationTokenSource;
            if (current is not null)
            {
                return current;
            }

            lock (lazyInitLock)
            {
                return cancellationTokenSource ??= new CancellationTokenSource();
            }
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        cancellationTokenSource?.Dispose();
    }
}