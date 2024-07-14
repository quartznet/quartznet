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

using System.Runtime.Serialization;

using Quartz.Spi;

namespace Quartz.Impl.Triggers;

/// <summary>
/// The base abstract class to be extended by all triggers.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ITrigger" />s have a name and group associated with them, which
/// should uniquely identify them within a single <see cref="IScheduler" />.
/// </para>
///
/// <para>
/// <see cref="ITrigger" />s are the 'mechanism' by which <see cref="IJob" /> s
/// are scheduled. Many <see cref="ITrigger" /> s can point to the same <see cref="IJob" />,
/// but a single <see cref="ITrigger" /> can only point to one <see cref="IJob" />.
/// </para>
///
/// <para>
/// Triggers can 'send' parameters/data to <see cref="IJob" />s by placing contents
/// into the <see cref="JobDataMap" /> on the <see cref="ITrigger" />.
/// </para>
/// </remarks>
/// <seealso cref="ISimpleTrigger" />
/// <seealso cref="ICronTrigger" />
/// <seealso cref="IDailyTimeIntervalTrigger" />
/// <seealso cref="JobDataMap" />
/// <seealso cref="IJobExecutionContext" />
/// <author>James House</author>
/// <author>Sharada Jambula</author>
/// <author>Marko Lahma (.NET)</author>
[Serializable]
public abstract class AbstractTrigger : IOperableTrigger, IEquatable<AbstractTrigger>
{
#pragma warning disable IDE0052
    // We use these field to (de)serialize the Key and JobKey for backward compatibility
    private string name = null!;
    private string group = SchedulerConstants.DefaultGroup;
    private string jobName = null!;
    private string jobGroup = SchedulerConstants.DefaultGroup;
#pragma warning restore IDE0052

    [NonSerialized] // we serialize this via the 'name' and 'group' fields
    private TriggerKey? key;
    [NonSerialized] // we serialize this via the 'jobName' and 'jobGroup' fields
    private JobKey? jobKey;
    private JobDataMap jobDataMap = null!;

    private int misfireInstruction = Quartz.MisfireInstruction.InstructionNotSet;

    private DateTimeOffset? endTimeUtc;
    private DateTimeOffset startTimeUtc;

    [NonSerialized]
    private TimeProvider timeProvider;

    internal TimeProvider TimeProvider => timeProvider ?? TimeProvider.System;

    /// <summary>
    /// Gets or sets the key of the trigger.
    /// </summary>
    /// <value>The key of the trigger.</value>
    public TriggerKey Key
    {
        get { return key!; }
        set
        {
            // Update fields to ensure we remain backward compatible for serialization
            if (value is null)
            {
                name = null!;
                group = null!;
            }
            else
            {
                name = value.Name;
                group = value.Group;
            }

            key = value;
        }
    }

    /// <summary>
    /// Gets or sets the key of the job.
    /// </summary>
    /// <value>The key of the job.</value>
    public JobKey JobKey
    {
        get { return jobKey!; }
        set
        {
            // Update fields to ensure we remain backward compatibile for serialization
            if (value is null)
            {
                jobName = null!;
                jobGroup = null!;
            }
            else
            {
                jobName = value.Name;
                jobGroup = value.Group;
            }

            jobKey = value;
        }
    }

    public TriggerBuilder GetTriggerBuilder()
    {
        return TriggerBuilder.Create()
            .ForJob(JobKey)
            .ModifiedByCalendar(CalendarName)
            .UsingJobData(JobDataMap)
            .WithDescription(Description)
            .EndAt(EndTimeUtc)
            .WithIdentity(Key)
            .WithPriority(Priority)
            .StartAt(StartTimeUtc)
            .WithSchedule(GetScheduleBuilder());
    }

    public abstract IScheduleBuilder GetScheduleBuilder();

    /// <summary>
    /// Get or set the description given to the <see cref="ITrigger" /> instance by
    /// its creator (if any).
    /// </summary>
    public virtual string? Description { get; set; }

    /// <summary>
    /// Get or set  the <see cref="ICalendar" /> with the given name with
    /// this Trigger. Use <see langword="null" /> when setting to dis-associate a Calendar.
    /// </summary>
    public virtual string? CalendarName { get; set; }

    /// <summary>
    /// Get or set the <see cref="JobDataMap" /> that is associated with the
    /// <see cref="ITrigger" />.
    /// <para>
    /// Changes made to this map during job execution are not re-persisted, and
    /// in fact typically result in an illegal state.
    /// </para>
    /// </summary>
    public virtual JobDataMap JobDataMap
    {
        get
        {
            if (jobDataMap is null)
            {
                jobDataMap = new JobDataMap();
            }
            return jobDataMap;
        }

        set => jobDataMap = value;
    }

    /// <summary>
    /// Returns the last UTC time at which the <see cref="ITrigger" /> will fire, if
    /// the Trigger will repeat indefinitely, null will be returned.
    /// <para>
    /// Note that the return time *may* be in the past.
    /// </para>
    /// </summary>
    public abstract DateTimeOffset? FinalFireTimeUtc { get; }

    /// <summary>
    /// Get or set the instruction the <see cref="IScheduler" /> should be given for
    /// handling misfire situations for this <see cref="ITrigger" />- the
    /// concrete <see cref="ITrigger" /> type that you are using will have
    /// defined a set of additional MISFIRE_INSTRUCTION_XXX
    /// constants that may be passed to this method.
    /// <para>
    /// If not explicitly set, the default value is <see cref="Quartz.MisfireInstruction.InstructionNotSet" />.
    /// </para>
    /// </summary>
    /// <seealso cref="Quartz.MisfireInstruction.InstructionNotSet" />
    /// <seealso cref="UpdateAfterMisfire" />
    /// <seealso cref="ISimpleTrigger" />
    /// <seealso cref="ICronTrigger" />
    public virtual int MisfireInstruction
    {
        get => misfireInstruction;

        set
        {
            if (!ValidateMisfireInstruction(value))
            {
                ThrowHelper.ThrowArgumentException("The misfire instruction code is invalid for this type of trigger.");
            }
            misfireInstruction = value;
        }
    }

    /// <summary>
    /// This method should not be used by the Quartz client.
    /// </summary>
    /// <remarks>
    /// Usable by <see cref="IJobStore" />
    /// implementations, in order to facilitate 'recognizing' instances of fired
    /// <see cref="ITrigger" /> s as their jobs complete execution.
    /// </remarks>
    public virtual string FireInstanceId { get; set; } = null!;

    public abstract void SetNextFireTimeUtc(DateTimeOffset? nextFireTime);

    public abstract void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime);

    /// <summary>
    /// Returns the previous time at which the <see cref="ITrigger" /> fired.
    /// If the trigger has not yet fired, <see langword="null" /> will be returned.
    /// </summary>
    public abstract DateTimeOffset? GetPreviousFireTimeUtc();

    /// <summary>
    /// Gets and sets the date/time on which the trigger must stop firing. This
    /// defines the final boundary for trigger firings &#x8212; the trigger will
    /// not fire after to this date and time. If this value is null, no end time
    /// boundary is assumed, and the trigger can continue indefinitely.
    /// </summary>
    public virtual DateTimeOffset? EndTimeUtc
    {
        get => endTimeUtc;

        set
        {
            DateTimeOffset sTime = StartTimeUtc;

            if (value.HasValue && sTime > value.Value)
            {
                ThrowHelper.ThrowArgumentException("End time cannot be before start time");
            }

            endTimeUtc = value;
        }
    }

    /// <summary>
    /// The time at which the trigger's scheduling should start.  May or may not
    /// be the first actual fire time of the trigger, depending upon the type of
    /// trigger and the settings of the other properties of the trigger.  However
    /// the first actual first time will not be before this date.
    /// </summary>
    /// <remarks>
    /// Setting a value in the past may cause a new trigger to compute a first
    /// fire time that is in the past, which may cause an immediate misfire
    /// of the trigger.
    /// </remarks>
    public virtual DateTimeOffset StartTimeUtc
    {
        get => startTimeUtc;

        set
        {
            if (EndTimeUtc.HasValue && EndTimeUtc.Value < value)
            {
                ThrowHelper.ThrowArgumentException("End time cannot be before start time");
            }

            if (!HasMillisecondPrecision)
            {
                // round off millisecond...
                startTimeUtc = value.AddMilliseconds(-value.Millisecond);
            }
            else
            {
                startTimeUtc = value;
            }
        }
    }

    /// <summary>
    /// Tells whether this Trigger instance can handle events
    /// in millisecond precision.
    /// </summary>
    public abstract bool HasMillisecondPrecision
    {
        get;
    }

    protected AbstractTrigger()
    {
        this.timeProvider = TimeProvider.System;
    }

    /// <summary>
    /// Create a <see cref="ITrigger" /> with no specified name, group, or <see cref="IJobDetail" />.
    /// </summary>
    /// <remarks>
    /// Note that <see cref="Key" /> and <see cref="JobKey" /> must be set before
    /// the <see cref="ITrigger" /> can be placed into a <see cref="IScheduler" />.
    /// </remarks>
    /// <param name="timeProvider">Time provider instance to use</param>
    protected AbstractTrigger(TimeProvider timeProvider)
    {
        this.timeProvider = timeProvider;
    }

    /// <summary>
    /// Create a <see cref="ITrigger" /> with the given name, and default group.
    /// </summary>
    /// <remarks>
    /// Note that <see cref="JobKey" /> must be set before the <see cref="ITrigger" />
    /// can be placed into a <see cref="IScheduler" />.
    /// </remarks>
    /// <param name="name">The name.</param>
    /// <param name="timeProvider">Time provider instance to use</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is <see langword="null"/>.</exception>
    protected AbstractTrigger(string name, TimeProvider timeProvider) : this(name, SchedulerConstants.DefaultGroup, timeProvider)
    {
    }

    /// <summary>
    /// Create a <see cref="ITrigger" /> with the given name, and group.
    /// </summary>
    /// <remarks>
    /// Note that <see cref="JobKey" /> must be set before the <see cref="ITrigger" />
    /// can be placed into a <see cref="IScheduler" />.
    /// </remarks>
    /// <param name="name">The name.</param>
    /// <param name="group">The group.</param>
    /// <param name="timeProvider">Time provider instance to use</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> or <paramref name="group"/> are <see langword="null"/>.</exception>
    protected AbstractTrigger(string name, string group, TimeProvider timeProvider) : this(timeProvider)
    {
        Key = new TriggerKey(name, group);
    }

    /// <summary>
    /// Create a <see cref="ITrigger" /> with the given name, and group.
    /// </summary>
    /// <param name="name">The name.</param>
    /// <param name="group">if <see langword="null" />, Scheduler.DefaultGroup will be used.</param>
    /// <param name="jobName">Name of the job.</param>
    /// <param name="jobGroup">The job group.</param>
    /// <param name="timeProvider">Time provider instance to use</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/>, <paramref name="group"/>, <paramref name="jobName"/> or <paramref name="jobGroup"/> are <see langword="null"/>.</exception>
    protected AbstractTrigger(string name, string group, string jobName, string jobGroup, TimeProvider timeProvider) : this(timeProvider)
    {
        Key = new TriggerKey(name, group);
        JobKey = new JobKey(jobName, jobGroup);
    }

    /// <summary>
    /// The priority of a <see cref="ITrigger" /> acts as a tie breaker such that if
    /// two <see cref="ITrigger" />s have the same scheduled fire time, then Quartz
    /// will do its best to give the one with the higher priority first access
    /// to a worker thread.
    /// </summary>
    /// <remarks>
    /// If not explicitly set, the default value is <i>5</i>.
    /// </remarks>
    /// <returns></returns>
    /// <see cref="TriggerConstants.DefaultPriority" />
    public virtual int Priority { get; set; } = TriggerConstants.DefaultPriority;

    /// <summary>
    /// This method should not be used by the Quartz client.
    /// </summary>
    /// <remarks>
    /// Called when the <see cref="IScheduler" /> has decided to 'fire'
    /// the trigger (Execute the associated <see cref="IJob" />), in order to
    /// give the <see cref="ITrigger" /> a chance to update itself for its next
    /// triggering (if any).
    /// </remarks>
    /// <seealso cref="JobExecutionException" />
    public abstract void Triggered(ICalendar? cal);


    /// <summary>
    /// This method should not be used by the Quartz client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by the scheduler at the time a <see cref="ITrigger" /> is first
    /// added to the scheduler, in order to have the <see cref="ITrigger" />
    /// compute its first fire time, based on any associated calendar.
    /// </para>
    ///
    /// <para>
    /// After this method has been called, <see cref="GetNextFireTimeUtc" />
    /// should return a valid answer.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The first time at which the <see cref="ITrigger" /> will be fired
    /// by the scheduler, which is also the same value <see cref="GetNextFireTimeUtc" />
    /// will return (until after the first firing of the <see cref="ITrigger" />).
    /// </returns>
    public abstract DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? cal);

    /// <summary>
    /// This method should not be used by the Quartz client.
    /// </summary>
    /// <remarks>
    /// Called after the <see cref="IScheduler" /> has executed the
    /// <see cref="IJobDetail" /> associated with the <see cref="ITrigger" />
    /// in order to get the final instruction code from the trigger.
    /// </remarks>
    /// <param name="context">
    /// is the <see cref="IJobExecutionContext" /> that was used by the
    /// <see cref="IJob" />'s<see cref="IJob.Execute" /> method.</param>
    /// <param name="result">is the <see cref="JobExecutionException" /> thrown by the
    /// <see cref="IJob" />, if any (may be null).
    /// </param>
    /// <returns>
    /// One of the <see cref="SchedulerInstruction"/> members.
    /// </returns>
    /// <seealso cref="SchedulerInstruction" />
    /// <seealso cref="Triggered" />
    public virtual SchedulerInstruction ExecutionComplete(IJobExecutionContext context, JobExecutionException? result)
    {
        if (result is not null && result.RefireImmediately)
        {
            return SchedulerInstruction.ReExecuteJob;
        }

        if (result is not null && result.UnscheduleFiringTrigger)
        {
            return SchedulerInstruction.SetTriggerComplete;
        }

        if (result is not null && result.UnscheduleAllTriggers)
        {
            return SchedulerInstruction.SetAllJobTriggersComplete;
        }

        if (!GetMayFireAgain())
        {
            return SchedulerInstruction.DeleteTrigger;
        }

        return SchedulerInstruction.NoInstruction;
    }

    /// <summary>
    /// Used by the <see cref="IScheduler" /> to determine whether or not
    /// it is possible for this <see cref="ITrigger" /> to fire again.
    /// <para>
    /// If the returned value is <see langword="false" /> then the <see cref="IScheduler" />
    /// may remove the <see cref="ITrigger" /> from the <see cref="IJobStore" />.
    /// </para>
    /// </summary>
    public abstract bool GetMayFireAgain();

    /// <summary>
    /// Returns the next time at which the <see cref="ITrigger" /> is scheduled to fire. If
    /// the trigger will not fire again, <see langword="null" /> will be returned.  Note that
    /// the time returned can possibly be in the past, if the time that was computed
    /// for the trigger to next fire has already arrived, but the scheduler has not yet
    /// been able to fire the trigger (which would likely be due to lack of resources
    /// e.g. threads).
    /// </summary>
    ///<remarks>
    /// The value returned is not guaranteed to be valid until after the <see cref="ITrigger" />
    /// has been added to the scheduler.
    /// </remarks>
    /// <returns></returns>
    public abstract DateTimeOffset? GetNextFireTimeUtc();

    /// <summary>
    /// Returns the next time at which the <see cref="ITrigger" /> will fire,
    /// after the given time. If the trigger will not fire after the given time,
    /// <see langword="null" /> will be returned.
    /// </summary>
    public abstract DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime);

    /// <summary>
    /// Validates the misfire instruction.
    /// </summary>
    /// <param name="misfireInstruction">The misfire instruction.</param>
    /// <returns></returns>
    protected abstract bool ValidateMisfireInstruction(int misfireInstruction);

    /// <summary>
    /// This method should not be used by the Quartz client.
    /// <para>
    /// To be implemented by the concrete classes that extend this class.
    /// </para>
    /// <para>
    /// The implementation should update the <see cref="ITrigger" />'s state
    /// based on the MISFIRE_INSTRUCTION_XXX that was selected when the <see cref="ITrigger" />
    /// was created.
    /// </para>
    /// </summary>
    public abstract void UpdateAfterMisfire(ICalendar? cal);

    /// <summary>
    /// This method should not be used by the Quartz client.
    /// <para>
    /// The implementation should update the <see cref="ITrigger" />'s state
    /// based on the given new version of the associated <see cref="ICalendar" />
    /// (the state should be updated so that it's next fire time is appropriate
    /// given the Calendar's new settings).
    /// </para>
    /// </summary>
    /// <param name="cal"> </param>
    /// <param name="misfireThreshold"></param>
    public abstract void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold);

    /// <summary>
    /// Validates whether the properties of the <see cref="IJobDetail" /> are
    /// valid for submission into a <see cref="IScheduler" />.
    /// </summary>
    public virtual void Validate()
    {
        if (key is null)
        {
            ThrowHelper.ThrowSchedulerException("Trigger's key cannot be null");
        }

        if (jobKey is null)
        {
            ThrowHelper.ThrowSchedulerException("Trigger's job key cannot be null");
        }
    }

    /// <summary>
    /// Gets a value indicating whether this instance has additional properties
    /// that should be considered when for example saving to database.
    /// </summary>
    /// <remarks>
    /// If trigger implementation has additional properties that need to be saved
    /// with base properties you need to make your class override this property with value true.
    /// Returning true will effectively mean that ADOJobStore needs to serialize
    /// this trigger instance to make sure additional properties are also saved.
    /// </remarks>
    /// <value>
    /// 	<c>true</c> if this instance has additional properties; otherwise, <c>false</c>.
    /// </value>
    public virtual bool HasAdditionalProperties => false;

    /// <summary>
    /// Return a simple string representation of this object.
    /// </summary>
    public override string ToString()
        => $"Trigger '{key}':  triggerClass: '{GetType().FullName} calendar: '{CalendarName}' misfireInstruction: {MisfireInstruction} nextFireTime: {GetNextFireTimeUtc()}";

    /// <summary>
    /// Determines whether the specified <see cref="System.Object"></see> is equal to the current <see cref="System.Object"></see>.
    /// </summary>
    /// <param name="obj">The <see cref="System.Object"></see> to compare with the current <see cref="System.Object"></see>.</param>
    /// <returns>
    /// true if the specified <see cref="System.Object"></see> is equal to the current <see cref="System.Object"></see>; otherwise, false.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as AbstractTrigger);
    }

    /// <summary>
    /// Trigger equality is based upon the equality of the TriggerKey.
    /// </summary>
    /// <param name="trigger"></param>
    /// <returns>true if the key of this Trigger equals that of the given Trigger</returns>
    public virtual bool Equals(AbstractTrigger? trigger)
    {
        if (trigger?.Key is null || Key is null)
        {
            return false;
        }

        return Key.Equals(trigger.Key);
    }

    /// <summary>
    /// Serves as a hash function for a particular type. <see cref="System.Object.GetHashCode"></see> is suitable for use in hashing algorithms and data structures like a hash table.
    /// </summary>
    /// <returns>
    /// A hash code for the current <see cref="System.Object"></see>.
    /// </returns>
    public override int GetHashCode()
    {
        if (Key is null)
        {
            return base.GetHashCode();
        }

        return Key.GetHashCode();
    }

    /// <summary>
    /// Creates a new object that is a copy of the current instance.
    /// </summary>
    /// <returns>
    /// A new object that is a copy of this instance.
    /// </returns>
    public virtual ITrigger Clone()
    {
        AbstractTrigger copy = (AbstractTrigger) MemberwiseClone();

        // Shallow copy the jobDataMap.  Note that this means that if a user
        // modifies a value object in this map from the cloned Trigger
        // they will also be modifying this Trigger.
        if (jobDataMap is not null)
        {
            copy.jobDataMap = (JobDataMap) jobDataMap.Clone();
        }

        return copy;
    }

    /// <summary>
    /// Called immediately after deserialization.
    /// </summary>
    /// <param name="context">The source of the deserialization.</param>
    /// <remarks>
    /// We use this to reconstruct the <see cref="Key"/> and <see cref="JobKey"/>.
    /// </remarks>
    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        if (name is not null && group is not null)
        {
            key = new TriggerKey(name, group);
        }

        if (jobName is not null && jobGroup is not null)
        {
            jobKey = new JobKey(jobName, jobGroup);
        }
    }
}