#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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

using System;

using Quartz.Spi;

namespace Quartz
{
    /// <summary>
    /// The base interface with properties common to all <see cref="ITrigger" />s - 
    /// use <see cref="TriggerBuilder" /> to instantiate an actual Trigger.
    /// </summary>
    /// <remarks>
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
    public interface ITrigger : ICloneable, IComparable<ITrigger>
    {
        TriggerKey Key { get; }

        JobKey JobKey { get; }

        /// <summary>
        /// Get a <see cref="TriggerBuilder" /> that is configured to produce a 
        /// trigger identical to this one.
        /// </summary>
        /// <seealso cref="GetScheduleBuilder"/>
        /// <returns></returns>
        TriggerBuilder GetTriggerBuilder();

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
        string Description { get; }

        /// <summary>
        /// Get or set  the <see cref="ICalendar" /> with the given name with
        /// this Trigger. Use <see langword="null" /> when setting to dis-associate a Calendar.
        /// </summary>
        string CalendarName { get; }

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
        /// Get or set the instruction the <see cref="IScheduler" /> should be given for
        /// handling misfire situations for this <see cref="ITrigger" />- the
        /// concrete <see cref="ITrigger" /> type that you are using will have
        /// defined a set of additional MISFIRE_INSTRUCTION_XXX
        /// constants that may be set to this property.
        /// <para>
        /// If not explicitly set, the default value is <see cref="Quartz.MisfireInstruction.InstructionNotSet" />.
        /// </para>
        /// </summary>
        /// <seealso cref="Quartz.MisfireInstruction.InstructionNotSet" />
        /// <seealso cref="ISimpleTrigger" />
        /// <seealso cref="ICronTrigger" />
        int MisfireInstruction { get; }

        /// <summary>
        /// Gets and sets the date/time on which the trigger must stop firing. This 
        /// defines the final boundary for trigger firings &#x8212; the trigger will
        /// not fire after to this date and time. If this value is null, no end time
        /// boundary is assumed, and the trigger can continue indefinitely.
        /// </summary>
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
        int Priority { get; set; }

        /// <summary> 
        /// Used by the <see cref="IScheduler" /> to determine whether or not
        /// it is possible for this <see cref="ITrigger" /> to fire again.
        /// <para>
        /// If the returned value is <see langword="false" /> then the <see cref="IScheduler" />
        /// may remove the <see cref="ITrigger" /> from the <see cref="IJobStore" />.
        /// </para>
        /// </summary>
        bool GetMayFireAgain();

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
        DateTimeOffset? GetNextFireTimeUtc();

        /// <summary>
        /// Returns the previous time at which the <see cref="ITrigger" /> fired.
        /// If the trigger has not yet fired, <see langword="null" /> will be returned.
        /// </summary>
        DateTimeOffset? GetPreviousFireTimeUtc();

        /// <summary>
        /// Returns the next time at which the <see cref="ITrigger" /> will fire,
        /// after the given time. If the trigger will not fire after the given time,
        /// <see langword="null" /> will be returned.
        /// </summary>
        DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTime);

        bool HasMillisecondPrecision { get; }

    }
}