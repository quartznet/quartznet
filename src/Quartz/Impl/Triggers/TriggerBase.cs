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

using Quartz.Extensibility;

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
public abstract class TriggerBase : IOperableTrigger, IEquatable<TriggerBase>
{
#pragma warning disable IDE0052
    // We use these field to (de)serialize the Key and JobKey for backward compatibility
    private string name = null!;
    private string group = TriggerKey.DefaultGroup;
    private string jobName = null!;
    private string jobGroup = JobKey.DefaultGroup;
#pragma warning restore IDE0052

    [NonSerialized] // we serialize this via the 'name' and 'group' fields
    private TriggerKey? key;
    [NonSerialized] // we serialize this via the 'jobName' and 'jobGroup' fields
    private JobKey? jobKey;
    private JobDataMap jobDataMap = null!;

    private int misfireInstruction = Quartz.MisfireInstruction.InstructionNotSet;

    private DateTimeOffset? endTimeUtc;
    private DateTimeOffset startTimeUtc;
    private string? executionGroup;
    private string? preferredNode;

    // True when preferredNode holds a pin this trigger claimed automatically (auto-pin) rather
    // than one the user set explicitly. Kept out-of-band from preferredNode so the node name is
    // stored verbatim; see PREFERRED_NODE_AUTO in the triggers table.
    private bool preferredNodeAuto;

    // Tracks whether the pin was changed on this instance (vs. loaded from the database); the
    // ADO.NET job store only writes the preferred node columns on update when set, because
    // writing back an unchanged value loaded at acquire time would clobber concurrent updates
    // (ClusterRecover's failover reset, an UpdateTriggerDetails re-pin).
    private bool preferredNodeDirty;

    // The retry policy is held as the string the RETRY_POLICY column carries rather than as the
    // value, so that a [Serializable] trigger's blob holds a primitive. A blob written before
    // triggers could retry simply has no such field and deserializes to null, which is "no policy".
    private string? retryPolicy;
    private int retryAttempt;

    // Whether ExecutionComplete has just cleared a non-zero attempt, so the stores know they have a
    // write to make on a completion that otherwise writes nothing. Not serialized: it says something
    // about this completion, not about the trigger.
    [NonSerialized]
    private bool retryAttemptCleared;

    // Parsing the stored form once per trigger rather than once per read. Not serialized: it is
    // derived from the field above, which is.
    [NonSerialized]
    private RetryPolicy? retryPolicyValue;

    [NonSerialized]
    private TimeProvider? timeProvider;

    /// <summary>
    /// The clock every "now" this trigger reads comes from: the past-due clamp in
    /// <see cref="ComputeFirstFireTimeUtc" />, and the whole of <see cref="UpdateAfterMisfire" />.
    /// </summary>
    /// <remarks>
    /// <para>
    /// It arrives from whoever produced the trigger, and only from there:
    /// <see cref="TriggerBuilder{TJob}.Build" /> hands over the clock its <c>Create</c> was given, and a
    /// job store hands over the scheduler's clock to every trigger it materializes from its rows. A
    /// trigger nobody handed a clock reads <see cref="System.TimeProvider.System" />, which is also what
    /// a deserialized one starts out with — the field is <see cref="NonSerializedAttribute" />, so a
    /// trigger out of a blob has no clock until the store that read it says otherwise.
    /// </para>
    /// <para>
    /// Settable, but internal: a trigger's clock is a construction-or-store decision. Nothing outside
    /// Quartz swaps the clock under a scheduled trigger, because the misfire decision and the misfire
    /// arithmetic have to be made against the same reading.
    /// </para>
    /// </remarks>
    internal TimeProvider TimeProvider
    {
        get => timeProvider ?? TimeProvider.System;
        set => timeProvider = value;
    }

    /// <summary>
    /// Stores the original NextFireTimeUtc before misfire handling changes it.
    /// Used to provide the correct ScheduledFireTimeUtc in JobExecutionContext.
    /// Copied by MemberwiseClone in <see cref="Clone"/> (works for RAMJobStore).
    /// </summary>
    [NonSerialized]
    internal DateTimeOffset? MisfiredFromFireTimeUtc;

    /// <summary>
    /// Maximum elapsed time (in ms) between the time captured before UpdateAfterMisfire
    /// and the new fire time set by a "fire now" misfire policy. Used to distinguish
    /// "fire now" policies (FireOnceNow, FireNow, RescheduleNowWith*) from "reschedule
    /// next" policies (DoNothing, RescheduleNextWith*) where the existing code is already
    /// correct.
    /// </summary>
    internal const double FireNowMisfireDetectionThresholdMs = 500;

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

    public TriggerBuilder<IJob> GetTriggerBuilder()
    {
        // This trigger's own clock, so that rebuilding a trigger keeps the reading of "now" it was
        // computing against - the past-due clamp in ComputeFirstFireTimeUtc runs on whatever the
        // rebuilt trigger ends up holding.
        return TriggerBuilder.Create(TimeProvider)
            .ForJob(JobKey)
            .WithCalendarName(CalendarName)
            .UsingJobData(JobDataMap)
            .WithDescription(Description)
            .WithExecutionGroup(ExecutionGroup)
            // The pin round-trips losslessly, auto-claim flag included: rebuilding an auto-pinned
            // trigger keeps it auto-pinned (so it is still released if that node dies) instead of
            // silently hardening into a pin the user named.
            .WithPreferredNode(PreferredNode)
            // The policy is part of the definition and round-trips; the attempt is not - it counts
            // retries of the occurrence being executed, and a rebuilt trigger has no occurrence in
            // flight, exactly as it has no NextFireTimeUtc.
            .WithRetryPolicy(RetryPolicy)
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
    /// <remarks>
    /// An empty or whitespace-only name is stored as <see langword="null" />. Every job store reads
    /// a non-null calendar name as "this trigger observes a calendar" and silently drops the fire
    /// when no such calendar exists, so a blank name has to mean no calendar rather than a calendar
    /// nothing can find. The value is not trimmed: a calendar is looked up by its exact stored name.
    /// </remarks>
    public virtual string? CalendarName
    {
        get;
        set => field = string.IsNullOrWhiteSpace(value) ? null : value;
    }

    /// <summary>
    /// Gets or sets the execution group for this trigger. Execution groups allow
    /// thread limits to be configured - per node or across the cluster - so that
    /// resource-intensive jobs do not saturate all available threads.
    /// </summary>
    /// <remarks>
    /// <para>A <see langword="null"/> value means the trigger has no execution group
    /// (the default, backward-compatible behavior).</para>
    /// </remarks>
    public string? ExecutionGroup
    {
        get => executionGroup;
        set
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                executionGroup = null;
            }
            else
            {
                string trimmed = value!.Trim();
                if (ExecutionLimits.IsReservedGroupName(trimmed))
                {
                    throw new ArgumentException(
                        $"Execution group name '{trimmed}' is reserved for limits configuration.",
                        nameof(value));
                }
                executionGroup = trimmed;
            }
        }
    }

    /// <summary>
    /// Gets or sets which cluster node this trigger prefers to run on. Only that node acquires
    /// the trigger, with automatic failover while it is down.
    /// </summary>
    /// <remarks>
    /// <para><see cref="Quartz.PreferredNode.None"/> means the trigger has no node preference
    /// (the default, backward-compatible behavior).</para>
    /// <para>The value is recorded as given, automatic-pin flag included, so a pin survives being
    /// copied from one trigger to another as the pin it was.</para>
    /// </remarks>
    public PreferredNode PreferredNode
    {
        get => Quartz.PreferredNode.FromStored(preferredNode, preferredNodeAuto);
        set => SetPreferredNode(value, markDirty: true);
    }

    /// <summary>
    /// Whether the preferred node was changed on this trigger instance (by user code, a builder,
    /// deserialization, or an auto-pin claim) as opposed to merely being loaded from the database.
    /// </summary>
    internal bool PreferredNodeDirty => preferredNodeDirty;

    /// <summary>
    /// Sets the preferred node, optionally without marking it changed.
    /// </summary>
    /// <param name="value">The pin to record.</param>
    /// <param name="markDirty">
    /// Whether the write marks the value as changed. Pass <see langword="false"/> only when
    /// populating the trigger from its own database row, where the in-memory value mirrors
    /// persistent state — this also clears any earlier dirtiness.
    /// </param>
    internal void SetPreferredNode(PreferredNode value, bool markDirty)
    {
        preferredNode = value.StoredNode;
        preferredNodeAuto = value.StoredAutomatic;
        preferredNodeDirty = markDirty;
    }

    /// <summary>
    /// Gets or sets how the scheduler re-fires this trigger when its job fails.
    /// <see langword="null" /> — the default — means a failed job is reported and the trigger waits
    /// for its next scheduled occurrence.
    /// </summary>
    /// <remarks>
    /// Held as the policy's stored string form, which is what the trigger's row and a serialized
    /// trigger both carry; the value is parsed on first read and kept for as long as the string
    /// does not change.
    /// </remarks>
    /// <seealso cref="Quartz.RetryPolicy" />
    public RetryPolicy? RetryPolicy
    {
        get
        {
            if (retryPolicyValue is null && retryPolicy is not null)
            {
                retryPolicyValue = Quartz.RetryPolicy.Parse(retryPolicy);
            }

            return retryPolicyValue;
        }
        set
        {
            retryPolicyValue = value;
            retryPolicy = value?.ToStoredString();
        }
    }

    /// <summary>
    /// How many times the occurrence currently being executed has already been retried. <c>0</c> on
    /// a regular fire.
    /// </summary>
    /// <remarks>
    /// <b>The setter should not be used by client code.</b> The scheduler advances it as retries are
    /// scheduled and clears it when the occurrence is done with; a job store assigns it when
    /// restoring a trigger from its row.
    /// </remarks>
    public int RetryAttempt
    {
        get => retryAttempt;
        set
        {
            if (value < 0)
            {
                Throw.ArgumentOutOfRangeException(nameof(value), $"A retry attempt count is never negative; {value} is not one.");
            }

            retryAttempt = value;
        }
    }

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
    /// Get or set the raw code of the instruction the <see cref="IScheduler" /> follows when this
    /// trigger misses a firing. The concrete trigger type validates the code against its own
    /// family's range.
    /// <para>
    /// If not explicitly set, the code is zero — "instruction not set" — and the scheduler applies
    /// the family's smart policy. The named values are on the per-family enums, such as
    /// <see cref="CronTriggerMisfireInstruction" /> and <see cref="SimpleTriggerMisfireInstruction" />.
    /// </para>
    /// </summary>
    /// <seealso cref="UpdateAfterMisfire(ICalendar?)" />
    /// <seealso cref="ISimpleTrigger" />
    /// <seealso cref="ICronTrigger" />
    public virtual int MisfireInstructionCode
    {
        get => misfireInstruction;

        set
        {
            if (!ValidateMisfireInstruction(value))
            {
                Throw.ArgumentException("The misfire instruction code is invalid for this type of trigger.");
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

    /// <summary>
    /// The previous time at which the <see cref="ITrigger" /> fired.
    /// If the trigger has not yet fired, <see langword="null" /> will be returned.
    /// </summary>
    /// <remarks>
    /// <b>The setter should not be used by client code.</b> The scheduler records this as it fires
    /// the trigger; assigning it yourself corrupts the schedule.
    /// </remarks>
    public abstract DateTimeOffset? PreviousFireTimeUtc { get; set; }

    /// <summary>
    /// Gets and sets the date/time on which the trigger must stop firing. This
    /// defines the final boundary for trigger firings &#x8212; the trigger will
    /// not fire after this date and time. If this value is null, no end time
    /// boundary is assumed, and the trigger can continue indefinitely.
    /// </summary>
    /// <remarks>
    /// The end time is inclusive, for every trigger type: it is the last instant at which the
    /// trigger may fire, so a fire time exactly equal to it is one the trigger fires, and the first
    /// instant after it is where the schedule stops.
    /// </remarks>
    public virtual DateTimeOffset? EndTimeUtc
    {
        get => endTimeUtc;

        set
        {
            DateTimeOffset sTime = StartTimeUtc;

            if (value.HasValue && sTime > value.Value)
            {
                Throw.ArgumentException("End time cannot be before start time");
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
                Throw.ArgumentException("End time cannot be before start time");
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
    /// Whether this trigger's fire times are meaningful to the millisecond. A trigger that says no
    /// has its start time rounded down to the second.
    /// </summary>
    /// <remarks>
    /// This is how a trigger describes its own schedule to <see cref="TriggerBase" />, not
    /// something a caller reads: nothing outside the trigger acted on it.
    /// </remarks>
    protected abstract bool HasMillisecondPrecision { get; }

    /// <summary>
    /// Create a <see cref="ITrigger" /> with no specified name, group, or <see cref="IJobDetail" />.
    /// </summary>
    /// <remarks>
    /// Note that <see cref="Key" /> and <see cref="JobKey" /> must be set before
    /// the <see cref="ITrigger" /> can be placed into a <see cref="IScheduler" />.
    /// </remarks>
    /// <param name="timeProvider">Time provider instance to use, defaults to <see cref="TimeProvider.System"/></param>
    // S5766 (validate data in deserialization constructors): the [Serializable] attribute exists only
    // for the documented BinaryFormatter blob-migration contract, whose deserialization is field-based
    // and runs no constructor at all — this one initializes a clock and receives no external data.
#pragma warning disable S5766
    protected TriggerBase(TimeProvider? timeProvider = null)
#pragma warning restore S5766
    {
        // Left null when none was given, so the getter answers TimeProvider.System: a trigger that
        // came back through deserialization is in exactly that state, and the two read alike.
        this.timeProvider = timeProvider;
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
    public abstract void Triggered(ICalendar? calendar);


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
    /// After this method has been called, <see cref="NextFireTimeUtc" />
    /// should return a valid answer.
    /// </para>
    /// </remarks>
    /// <returns>
    /// The first time at which the <see cref="ITrigger" /> will be fired
    /// by the scheduler, which is also the same value <see cref="NextFireTimeUtc" />
    /// will return (until after the first firing of the <see cref="ITrigger" />).
    /// </returns>
    public abstract DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar? calendar);

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
            // An explicit directive wins over the trigger's retry policy, and the two are different
            // things: this re-runs the job on the same thread inside the same firing, with no delay,
            // no ceiling and nothing persisted. It does not touch the retry attempt.
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

        // The retry decision sits after the explicit directives, which win, and before the
        // nothing-left-to-fire check below, which a scheduled retry has to be able to postpone: a
        // one-shot trigger waiting to retry may still fire again, and announcing it as finalized
        // here would be announcing it twice.
        if (result is not null && RetryPolicy is { } policy && RetryAttempt < policy.MaxAttempts && TryScheduleRetry(policy))
        {
            return SchedulerInstruction.RetryTrigger;
        }

        // Everything else settles the occurrence: it succeeded, the trigger has no policy, its
        // attempts are spent, or the retry would have landed on top of the next scheduled
        // occurrence. All of them go back to the ordinary schedule, and none of them is an error —
        // one bad hour must not kill a cron trigger.
        ClearRetryAttempt();

        if (!MayFireAgain)
        {
            return SchedulerInstruction.DeleteTrigger;
        }

        return SchedulerInstruction.NoInstruction;
    }

    /// <summary>
    /// How close to the next scheduled occurrence a retry may be scheduled before the occurrence
    /// supersedes it.
    /// </summary>
    /// <remarks>
    /// One second, because <c>CalendarIntervalTriggerImpl.GetFireTimeAfter</c> and
    /// <c>DailyTimeIntervalTriggerImpl.GetFireTimeAfter</c> both add a second before searching: a
    /// retry closer than that to the next occurrence could not be told apart from it, and
    /// <see cref="RetryFired" /> would answer with the occurrence after it instead — losing a fire.
    /// </remarks>
    internal static readonly TimeSpan RetrySupersedeMargin = TimeSpan.FromSeconds(1);

    /// <summary>
    /// Moves the trigger's next fire time to the retry instant, if there is room for one before the
    /// next scheduled occurrence.
    /// </summary>
    /// <returns>
    /// <see langword="true" /> when the retry was scheduled, and the trigger now carries its instant
    /// and the incremented attempt.
    /// </returns>
    private bool TryScheduleRetry(RetryPolicy policy)
    {
        // The trigger's own clock, so a retry instant and the fire times it is compared with are two
        // readings of the same one.
        DateTimeOffset now = TimeProvider.GetUtcNow();
        TimeSpan delay = policy.DelayFor(RetryAttempt + 1);

        // A retry that lands past the end of representable time is a retry nobody ever comes back for,
        // which is the same answer as the two cases below: the occurrence settles and the trigger keeps
        // its ordinary schedule. It has to be decided before the arithmetic rather than after, because
        // adding to a DateTimeOffset throws where DelayFor saturates — and an exponential policy of one
        // second times ten runs out of calendar on its twelfth retry, which is a policy somebody might
        // actually write. The supersede margin comes off here too, so the comparison it is used in
        // cannot overflow either.
        if (delay > DateTimeOffset.MaxValue - RetrySupersedeMargin - now)
        {
            return false;
        }

        DateTimeOffset retryAt = now + delay;

        // What the schedule says comes next, which the fire that just completed advanced this to.
        DateTimeOffset? regularNext = NextFireTimeUtc;

        if (regularNext is not null && retryAt + RetrySupersedeMargin >= regularNext.Value)
        {
            // The occurrence wins. Retrying at or beside it would fire the job twice for what is
            // really one late attempt, and a policy whose waits are longer than the gap between
            // occurrences would do that on every failure.
            return false;
        }

        if (EndTimeUtc is not null && retryAt > EndTimeUtc.Value)
        {
            // A trigger does not fire after its end time, and a retry is a fire.
            return false;
        }

        NextFireTimeUtc = retryAt;
        RetryAttempt++;
        retryAttemptCleared = false;
        return true;
    }

    /// <summary>
    /// Puts the occurrence's retry count back to zero, recording whether that changed anything so a
    /// job store knows whether it has a write to make.
    /// </summary>
    private void ClearRetryAttempt()
    {
        if (retryAttempt != 0)
        {
            retryAttempt = 0;
            retryAttemptCleared = true;
        }
    }

    /// <summary>
    /// Whether <see cref="ExecutionComplete" /> has just put a non-zero <see cref="RetryAttempt" />
    /// back to zero, and so left the trigger's stored attempt behind.
    /// </summary>
    /// <remarks>
    /// The job stores write the attempt only when it changed. A completion that settles an
    /// occurrence which was never retried is by far the common case, and it costs nothing.
    /// </remarks>
    internal bool RetryAttemptCleared => retryAttemptCleared;

    /// <summary>
    /// This method should not be used by the Quartz client.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Called by a job store when it is firing a retry rather than a scheduled occurrence — that is,
    /// when the trigger's next fire time is a retry instant <see cref="ExecutionComplete" /> put
    /// there. It advances <see cref="NextFireTimeUtc" /> past the retry to the occurrence the
    /// schedule actually calls for, applying the trigger's calendar exactly as
    /// <see cref="Triggered" /> does.
    /// </para>
    /// <para>
    /// Unlike <see cref="Triggered" /> it touches no counter and does not move
    /// <see cref="PreviousFireTimeUtc" />: a retry is another attempt at an occurrence that has
    /// already been counted, so it must not burn a repeat count or a recurrence rule's <c>COUNT</c>
    /// slot, and it reports the original occurrence as its scheduled fire time.
    /// </para>
    /// </remarks>
    /// <param name="calendar">The calendar the trigger observes, if any.</param>
    /// <seealso cref="Triggered" />
    public virtual void RetryFired(ICalendar? calendar)
    {
        NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

        while (NextFireTimeUtc is not null && calendar is not null && !calendar.IsTimeIncluded(NextFireTimeUtc.Value))
        {
            NextFireTimeUtc = GetFireTimeAfter(NextFireTimeUtc);

            if (NextFireTimeUtc is null)
            {
                break;
            }

            // avoid infinite loop
            if (NextFireTimeUtc.Value.Year > TriggerConstants.YearToGiveUpSchedulingAt)
            {
                NextFireTimeUtc = null;
                break;
            }
        }
    }

    /// <summary>
    /// Used by the <see cref="IScheduler" /> to determine whether or not
    /// it is possible for this <see cref="ITrigger" /> to fire again.
    /// <para>
    /// If the returned value is <see langword="false" /> then the <see cref="IScheduler" />
    /// may remove the <see cref="ITrigger" /> from the <see cref="IJobStore" />.
    /// </para>
    /// </summary>
    public abstract bool MayFireAgain { get; }

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
    /// <para>
    /// <b>The setter should not be used by client code.</b> The scheduler advances this as it fires
    /// the trigger; assigning it yourself corrupts the schedule.
    /// </para>
    /// </remarks>
    public abstract DateTimeOffset? NextFireTimeUtc { get; set; }

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
    /// The implementation should update the <see cref="ITrigger" />'s state according to the misfire
    /// instruction the <see cref="ITrigger" /> was built with, read as
    /// <see cref="ITrigger.MisfireInstructionCode" />.
    /// </para>
    /// </summary>
    public abstract void UpdateAfterMisfire(ICalendar? calendar);

    /// <summary>
    /// This method should not be used by the Quartz client.
    /// <para>
    /// The implementation should update the <see cref="ITrigger" />'s state
    /// based on the given new version of the associated <see cref="ICalendar" />
    /// (the state should be updated so that it's next fire time is appropriate
    /// given the Calendar's new settings).
    /// </para>
    /// </summary>
    /// <param name="calendar"> </param>
    /// <param name="misfireThreshold"></param>
    public abstract void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold);

    /// <summary>
    /// Validates whether the properties of the <see cref="IJobDetail" /> are
    /// valid for submission into a <see cref="IScheduler" />.
    /// </summary>
    public virtual void Validate()
    {
        if (key is null)
        {
            Throw.SchedulerException("Trigger's key cannot be null");
        }

        if (jobKey is null)
        {
            Throw.SchedulerException("Trigger's job key cannot be null");
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
        => $"Trigger '{key}':  triggerClass: '{GetType().FullName} calendar: '{CalendarName}' misfireInstruction: {MisfireInstructionCode} nextFireTime: {NextFireTimeUtc}";

    /// <summary>
    /// Determines whether the specified <see cref="System.Object"></see> is equal to the current <see cref="System.Object"></see>.
    /// </summary>
    /// <param name="obj">The <see cref="System.Object"></see> to compare with the current <see cref="System.Object"></see>.</param>
    /// <returns>
    /// true if the specified <see cref="System.Object"></see> is equal to the current <see cref="System.Object"></see>; otherwise, false.
    /// </returns>
    public override bool Equals(object? obj)
    {
        return Equals(obj as TriggerBase);
    }

    /// <summary>
    /// Trigger equality is based upon the equality of the TriggerKey.
    /// </summary>
    /// <param name="trigger"></param>
    /// <returns>true if the key of this Trigger equals that of the given Trigger</returns>
    public virtual bool Equals(TriggerBase? trigger)
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
        TriggerBase copy = (TriggerBase) MemberwiseClone();

        // Shallow copy the jobDataMap.  Note that this means that if a user
        // modifies a value object in this map from the cloned Trigger
        // they will also be modifying this Trigger.
        if (jobDataMap is not null)
        {
            copy.jobDataMap = jobDataMap.Clone();
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