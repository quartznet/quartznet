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

using Quartz.Spi;

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
/// NOTE: Do not
/// expect value 'set' into this JobDataMap to somehow be set back onto a
/// job's own JobDataMap.
/// </para>
///
/// <para>
/// <see cref="IJobExecutionContext" /> s are also returned from the
/// <see cref="IScheduler.GetCurrentlyExecutingJobs" />
/// method. These are the same instances as those past into the jobs that are
/// currently executing within the scheduler. The exception to this is when your
/// application is using Quartz remotely (i.e. via remoting or WCF) - in which case you get
/// a clone of the <see cref="IJobExecutionContext" />s, and their references to
/// the <see cref="IScheduler" /> and <see cref="IJob" /> instances have been lost (a
/// clone of the <see cref="JobDetail" /> is still available - just not a handle
/// to the job instance that is running).
/// </para>
/// </remarks>
/// <seealso cref="JobDetail" />
/// <seealso cref="IScheduler" />
/// <seealso cref="IJob" />
/// <seealso cref="ITrigger" />
/// <seealso cref="JobDataMap" />
/// <author>James House</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
#pragma warning disable CA1708
public sealed class JobExecutionContextImpl : ICancellableJobExecutionContext, IDisposable
#pragma warning restore CA1708
{
    private readonly ITrigger trigger;
    private readonly IJobDetail jobDetail;
    private JobDataMap? jobDataMap;
    [NonSerialized]
    private readonly IScheduler scheduler;

    private int numRefires;
    private TimeSpan? jobRunTime;

    [NonSerialized]
    private Dictionary<object, object>? data;

    [NonSerialized]
    private CancellationTokenSource? cancellationTokenSource;

    [NonSerialized] internal readonly IJob jobInstance;

    [NonSerialized]
    private readonly object lazyInitLock = new object();

    /// <summary>
    /// Create a JobExecutionContext with the given context data.
    /// </summary>
    public JobExecutionContextImpl(IScheduler scheduler, TriggerFiredBundle firedBundle, IJob job)
    {
        this.scheduler = scheduler;
        trigger = firedBundle.Trigger;
        Calendar = firedBundle.Calendar;
        jobDetail = firedBundle.JobDetail;
        jobInstance = job;
        Recovering = firedBundle.Recovering;
        FireTimeUtc = firedBundle.FireTimeUtc;
        ScheduledFireTimeUtc = firedBundle.ScheduledFireTimeUtc;
        PreviousFireTimeUtc = firedBundle.PrevFireTimeUtc;
        NextFireTimeUtc = firedBundle.NextFireTimeUtc;
    }

    /// <summary>
    /// Get a handle to the <see cref="IScheduler" /> instance that fired the
    /// <see cref="IJob" />.
    /// </summary>
    public IScheduler Scheduler => scheduler;

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
    public JobDataMap MergedJobDataMap
    {
        get
        {
            if (jobDataMap is null)
            {
                lock (lazyInitLock)
                {
                    if (jobDataMap is null)
                    {
                        jobDataMap = new JobDataMap(jobDetail.JobDataMap.Count + trigger.JobDataMap.Count);
                        jobDataMap.PutAll(jobDetail.JobDataMap);
                        jobDataMap.PutAll(trigger.JobDataMap);
                    }
                }
            }

            return jobDataMap;
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
    public IJob JobInstance => (jobInstance as IJobWrapper)?.Target ?? jobInstance;

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

    /// <summary>
    /// The amount of time the job ran for.  The returned
    /// value will be <see cref="TimeSpan.MinValue" /> until the job has actually completed (or thrown an
    /// exception), and is therefore generally only useful to
    /// <see cref="IJobListener" />s and <see cref="ITriggerListener" />s.
    /// </summary>
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
        set => jobRunTime = value;
    }

    /// <summary>
    /// Increments the refire count.
    /// </summary>
    public void IncrementRefireCount()
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

    /// <summary>
    /// Put the specified value into the context's data map with the given key.
    /// Possibly useful for sharing data between listeners and jobs.
    /// <para>
    /// NOTE: this data is volatile - it is lost after the job execution
    /// completes, and all TriggerListeners and JobListeners have been
    /// notified.
    /// </para>
    /// </summary>
    /// <param name="key">
    /// </param>
    /// <param name="objectValue">
    /// </param>
    public void Put(object key, object objectValue)
    {
        Data[key] = objectValue;
    }

    /// <summary>
    /// Get the value with the given key from the context's data map.
    /// </summary>
    /// <param name="key">
    /// </param>
    public object? Get(object key)
    {
        if (Data.TryGetValue(key, out var retValue))
        {
            return retValue;
        }
        return null;
    }

    public void Cancel()
    {
        CancellationTokenSource.Cancel();
    }

    /// <summary>
    /// Returns the fire instance id.
    /// </summary>
    public string FireInstanceId => ((IOperableTrigger) trigger).FireInstanceId;

    public CancellationToken CancellationToken => CancellationTokenSource.Token;

    /// <summary>
    /// Lazily initializes the <see cref="CancellationTokenSource"/>.
    /// </summary>
    private CancellationTokenSource CancellationTokenSource
    {
        get
        {
            if (cancellationTokenSource is null)
            {
                lock (lazyInitLock)
                {
                    if (cancellationTokenSource is null)
                    {
                        cancellationTokenSource = new CancellationTokenSource();
                    }
                }
            }

            return cancellationTokenSource;
        }
    }

    /// <summary>
    /// Lazily initializes a <see cref="Dictionary{TKey,TValue}"/> for holding the context's data.
    /// </summary>
    private Dictionary<object, object> Data
    {
        get
        {
            if (data is null)
            {
                lock (lazyInitLock)
                {
                    if (data is null)
                    {
                        data = new Dictionary<object, object>();
                    }
                }
            }

            return data;
        }
    }

    public void Dispose()
    {
        cancellationTokenSource?.Dispose();
    }
}