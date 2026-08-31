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

using Quartz.Extensibility;

namespace Quartz;

/// <summary>
/// The base interface with properties common to all <see cref="ITrigger" />s -
/// use <see cref="TriggerBuilder" /> to instantiate an actual Trigger.
/// </summary>
/// <remarks>
/// <para>
/// <b>Quartz owns the implementations of this interface.</b> Build triggers with
/// <see cref="TriggerBuilder" /> (or the DI configuration equivalents); a custom trigger type
/// derives from <c>TriggerBase</c>, which implements the mutable and operational contracts
/// the scheduler and the job stores rely on. An object that implements only
/// <see cref="ITrigger" /> is a read model — handing one to the scheduler is rejected with a
/// clear error rather than scheduled.
/// </para>
///
/// <para>
/// <see cref="ITrigger" />s have a <see cref="TriggerKey" /> associated with them, which
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
/// <seealso cref="TriggerBuilder" />
/// <seealso cref="ICalendarIntervalTrigger" />
/// <seealso cref="ISimpleTrigger" />
/// <seealso cref="ICronTrigger" />
/// <seealso cref="IDailyTimeIntervalTrigger" />
/// <seealso cref="JobDataMap" />
/// <seealso cref="IJobExecutionContext" />
/// <author>James House</author>
/// <author>Sharada Jambula</author>
/// <author>Marko Lahma (.NET)</author>
public interface ITrigger
{
    TriggerKey Key { get; }

    JobKey JobKey { get; }

    /// <summary>
    /// Get a <see cref="TriggerBuilder" /> that is configured to produce a
    /// trigger identical to this one.
    /// </summary>
    /// <remarks>
    /// An interface member where its twin
    /// <see cref="JobDetailExtensions.GetJobBuilder" /> is an extension, and the asymmetry is the
    /// difference between the two rebuilds rather than an unfinished move. A detail's builder can be
    /// filled in from the detail's public state alone, so an extension can write it once for every
    /// implementation. A trigger's cannot: the builder has to be created against the trigger's own
    /// <see cref="System.TimeProvider" />, so that the rebuilt trigger computes its fire times from
    /// the same reading of "now" — and a trigger's clock is not part of this interface, because
    /// nothing else has any business reading it.
    /// </remarks>
    /// <seealso cref="GetScheduleBuilder"/>
    /// <returns></returns>
    TriggerBuilder<IJob> GetTriggerBuilder();

    /// <summary>
    /// Get a <see cref="IScheduleBuilder" /> that is configured to produce a
    /// schedule identical to this trigger's schedule.
    /// </summary>
    /// <returns></returns>
    IScheduleBuilder GetScheduleBuilder();

    /// <summary>
    /// Get or set the description given to the <see cref="ITrigger" /> instance by
    /// its creator (if any).
    /// </summary>
    string? Description { get; }

    /// <summary>
    /// Gets the execution group for this trigger. Execution groups allow thread
    /// limits to be configured - per node or across the cluster - so that
    /// resource-intensive jobs do not saturate all available threads.
    /// </summary>
    /// <remarks>
    /// A <see langword="null"/> value means the trigger has no execution group
    /// (the default, backward-compatible behavior).
    /// </remarks>
    string? ExecutionGroup { get; }

    /// <summary>
    /// Which cluster node this trigger prefers to run on. Only that node acquires the trigger,
    /// with automatic failover while it is down.
    /// </summary>
    /// <remarks>
    /// <see cref="Quartz.PreferredNode.None" /> — the default — means the trigger has no node
    /// preference.
    /// </remarks>
    /// <seealso cref="Quartz.PreferredNode" />
    PreferredNode PreferredNode { get; }

    /// <summary>
    /// How the scheduler re-fires this trigger when its job fails, or <see langword="null" /> —
    /// the default — when a failed job is simply reported and the trigger waits for its next
    /// scheduled occurrence.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A retry never displaces the next scheduled occurrence: one that would land at or within a
    /// second of it is dropped and the ordinary schedule wins.
    /// </para>
    /// <para>
    /// This is not <see cref="JobExecutionException.RefireImmediately" />, which re-runs the job on
    /// the same thread in the same firing, with no delay, no ceiling and nothing persisted.
    /// </para>
    /// </remarks>
    /// <seealso cref="Quartz.RetryPolicy" />
    RetryPolicy? RetryPolicy { get; }

    /// <summary>
    /// How many times the occurrence currently being executed has already been retried. <c>0</c> on
    /// a regular fire, <c>n</c> on the <c>n</c>-th retry.
    /// </summary>
    /// <remarks>
    /// Reset to <c>0</c> as soon as the occurrence succeeds, exhausts its
    /// <see cref="RetryPolicy" />, or misfires. Distinct from
    /// <see cref="IJobExecutionContext.RefireCount" />, which counts iterations of the in-process
    /// refire loop within a single firing.
    /// </remarks>
    int RetryAttempt { get; }

    /// <summary>
    /// Get or set  the <see cref="ICalendar" /> with the given name with
    /// this Trigger. Use <see langword="null" /> when setting to dis-associate a Calendar.
    /// </summary>
    /// <remarks>
    /// A blank name means no calendar: the built-in trigger implementations store an empty or
    /// whitespace-only name as <see langword="null" />, because a name no calendar can be found
    /// under would otherwise stop the trigger from ever firing.
    /// </remarks>
    string? CalendarName { get; }

    /// <summary>
    /// Get or set the <see cref="JobDataMap" /> that is associated with the
    /// <see cref="ITrigger" />.
    /// <para>
    /// Changes made to this map during job execution are not re-persisted, and
    /// in fact typically result in an illegal state.
    /// </para>
    /// </summary>
    JobDataMap JobDataMap { get; }

    /// <summary>
    /// Returns the last UTC time at which the <see cref="ITrigger" /> will fire, if
    /// the Trigger will repeat indefinitely, null will be returned.
    /// <para>
    /// Note that the return time *may* be in the past.
    /// </para>
    /// </summary>
    DateTimeOffset? FinalFireTimeUtc { get; }

    /// <summary>
    /// The raw code of the instruction the <see cref="IScheduler" /> follows when this trigger
    /// misses a firing. This is the number the job store persists, and it is family-agnostic:
    /// the same number means a different policy in each trigger family.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Read the policy from the family interface's own <c>MisfireInstruction</c> property instead
    /// whenever the family is known — <see cref="ISimpleTrigger.MisfireInstruction" />,
    /// <see cref="ICronTrigger.MisfireInstruction" /> and so on. This member exists for code that
    /// is generic over every family: serializers, the wire contract, logging and diagnostics.
    /// </para>
    /// <para>
    /// The default is <c>0</c>, the smart policy: the trigger's family picks the policy for it.
    /// </para>
    /// </remarks>
    /// <seealso cref="ISimpleTrigger.MisfireInstruction" />
    /// <seealso cref="ICronTrigger.MisfireInstruction" />
    int MisfireInstructionCode { get; }

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
    DateTimeOffset? EndTimeUtc { get; }

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
    DateTimeOffset StartTimeUtc { get; }

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
    int Priority { get; }

    /// <summary>
    /// Used by the <see cref="IScheduler" /> to determine whether or not
    /// it is possible for this <see cref="ITrigger" /> to fire again.
    /// <para>
    /// If the returned value is <see langword="false" /> then the <see cref="IScheduler" />
    /// may remove the <see cref="ITrigger" /> from the <see cref="IJobStore" />.
    /// </para>
    /// </summary>
    bool MayFireAgain { get; }

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
    DateTimeOffset? NextFireTimeUtc { get; }

    /// <summary>
    /// The previous time at which the <see cref="ITrigger" /> fired.
    /// If the trigger has not yet fired, <see langword="null" /> will be returned.
    /// </summary>
    DateTimeOffset? PreviousFireTimeUtc { get; }

    /// <summary>
    /// Returns the next time at which the <see cref="ITrigger" /> will fire,
    /// after the given time. If the trigger will not fire after the given time,
    /// <see langword="null" /> will be returned.
    /// </summary>
    DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime);

    ITrigger Clone();
}