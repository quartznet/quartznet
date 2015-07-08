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

namespace Quartz.Impl.Triggers
{
	/// <summary> 
	/// A concrete <see cref="ITrigger" /> that is used to fire a <see cref="IJobDetail" />
	/// at a given moment in time, and optionally repeated at a specified interval.
	/// </summary>
    /// <seealso cref="ITrigger" />
	/// <seealso cref="ICronTrigger" />
	/// <author>James House</author>
	/// <author>Contributions by Lieven Govaerts of Ebitec Nv, Belgium.</author>
	/// <author>Marko Lahma (.NET)</author>
	[Serializable]
	public class SimpleTriggerImpl : AbstractTrigger, ISimpleTrigger
	{
        /// <summary>
        /// Used to indicate the 'repeat count' of the trigger is indefinite. Or in
        /// other words, the trigger should repeat continually until the trigger's
        /// ending timestamp.
        /// </summary>
        public const int RepeatIndefinitely = -1;
        private const int YearToGiveupSchedulingAt = 2299;

        private DateTimeOffset? nextFireTimeUtc;
		private DateTimeOffset? previousFireTimeUtc;

        private int repeatCount;
        private TimeSpan repeatInterval = TimeSpan.Zero;
        private int timesTriggered;

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> with no settings.
        /// </summary>
        public SimpleTriggerImpl()
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// not repeat.
        /// </summary>
        public SimpleTriggerImpl(string name) : this(name, null)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// not repeat.
        /// </summary>
        public SimpleTriggerImpl(string name, string group)
            : this(name, group, SystemTime.UtcNow(), null, 0, TimeSpan.Zero)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// repeat at the given interval the given number of times.
        /// </summary>
        public SimpleTriggerImpl(string name, int repeatCount, TimeSpan repeatInterval)
            : this(name, null, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur immediately, and
        /// repeat at the given interval the given number of times.
        /// </summary>
        public SimpleTriggerImpl(string name, string group, int repeatCount, TimeSpan repeatInterval)
            : this(name, group, SystemTime.UtcNow(), null, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and not repeat.
        /// </summary>
        public SimpleTriggerImpl(string name, DateTimeOffset startTimeUtc)
            : this(name, null, startTimeUtc)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and not repeat.
        /// </summary>
        public SimpleTriggerImpl(string name, string group, DateTimeOffset startTimeUtc)
            : this(name, group, startTimeUtc, null, 0, TimeSpan.Zero)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and repeat at the given interval the given number of times, or until
        /// the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to fire.</param>
        /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
        /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        public SimpleTriggerImpl(string name, DateTimeOffset startTimeUtc,
            DateTimeOffset? endTimeUtc, int repeatCount, TimeSpan repeatInterval)
            : this(name, null, startTimeUtc, endTimeUtc, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// and repeat at the given interval the given number of times, or until
        /// the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to fire.</param>
        /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
        /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        public SimpleTriggerImpl(string name, string group, DateTimeOffset startTimeUtc,
            DateTimeOffset? endTimeUtc, int repeatCount, TimeSpan repeatInterval)
            : base(name, group)
        {
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            RepeatCount = repeatCount;
            RepeatInterval = repeatInterval;
        }

        /// <summary>
        /// Create a <see cref="SimpleTriggerImpl" /> that will occur at the given time,
        /// fire the identified <see cref="IJob" /> and repeat at the given
        /// interval the given number of times, or until the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="jobGroup">The job group.</param>
        /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to fire.</param>
        /// <param name="endTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="ITrigger" /> to repeat
        /// firing, use RepeatIndefinitely for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        public SimpleTriggerImpl(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc,
                 DateTimeOffset? endTimeUtc,
                 int repeatCount, TimeSpan repeatInterval)
            : base(name, group, jobName, jobGroup)
        {
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            RepeatCount = repeatCount;
            RepeatInterval = repeatInterval;
        }

        /// <summary>
        /// Get or set the number of times the <see cref="SimpleTriggerImpl" /> should
        /// repeat, after which it will be automatically deleted.
        /// </summary>
        /// <seealso cref="RepeatIndefinitely" />
        public int RepeatCount
        {
            get { return repeatCount; }

            set
            {
                if (value < 0 && value != RepeatIndefinitely)
                {
                    throw new ArgumentException("Repeat count must be >= 0, use the constant RepeatIndefinitely for infinite.");
                }

                repeatCount = value;
            }
        }

        /// <summary>
        /// Get or set the time interval at which the <see cref="ISimpleTrigger" /> should repeat.
        /// </summary>
        public TimeSpan RepeatInterval
        {
            get { return repeatInterval; }

            set
            {
                if (value < TimeSpan.Zero)
                {
                    throw new ArgumentException("Repeat interval must be >= 0");
                }

                repeatInterval = value;
            }
        }

        /// <summary>
        /// Get or set the number of times the <see cref="ISimpleTrigger" /> has already
        /// fired.
        /// </summary>
        public virtual int TimesTriggered
        {
            get { return timesTriggered; }
            set { timesTriggered = value; }
        }

	    public override IScheduleBuilder GetScheduleBuilder()
	    {
            SimpleScheduleBuilder sb = SimpleScheduleBuilder.Create()
            .WithInterval(RepeatInterval)
            .WithRepeatCount(RepeatCount);

            switch (MisfireInstruction)
            {
                case Quartz.MisfireInstruction.SimpleTrigger.FireNow: sb.WithMisfireHandlingInstructionFireNow();
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount: sb.WithMisfireHandlingInstructionNextWithExistingCount();
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount : sb.WithMisfireHandlingInstructionNextWithRemainingCount();
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount : sb.WithMisfireHandlingInstructionNowWithExistingCount();
                    break;
                case Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount: sb.WithMisfireHandlingInstructionNowWithRemainingCount();
                    break;
            }

            return sb;
	    }

	    /// <summary> 
        /// Returns the final UTC time at which the <see cref="ISimpleTrigger" /> will
        /// fire, if repeatCount is RepeatIndefinitely, null will be returned.
        /// <para>
        /// Note that the return time may be in the past.
        /// </para>
        /// </summary>
        public override DateTimeOffset? FinalFireTimeUtc
        {
            get
            {
                if (repeatCount == 0)
                {
                    return StartTimeUtc;
                }

                if (repeatCount == RepeatIndefinitely && !EndTimeUtc.HasValue)
                {
                    return null;
                }

                if (repeatCount == RepeatIndefinitely && !EndTimeUtc.HasValue)
                {
                    return null;
                }
                else if (repeatCount == RepeatIndefinitely)
                {
                    return GetFireTimeBefore(EndTimeUtc);
                }

                DateTimeOffset lastTrigger = StartTimeUtc.AddMilliseconds(repeatCount * repeatInterval.TotalMilliseconds);

                if (!EndTimeUtc.HasValue || lastTrigger < EndTimeUtc.Value)
                {
                    return lastTrigger;
                }
                else
                {
                    return GetFireTimeBefore(EndTimeUtc);
                }
            }
        }

        /// <summary>
        /// Tells whether this Trigger instance can handle events
        /// in millisecond precision.
        /// </summary>
        /// <value></value>
        public override bool HasMillisecondPrecision
        {
            get { return true; }
        }


		/// <summary>
		/// Validates the misfire instruction.
		/// </summary>
		/// <param name="misfireInstruction">The misfire instruction.</param>
		/// <returns></returns>
		protected override bool ValidateMisfireInstruction(int misfireInstruction)
		{
            if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
            {
                return false;
            }

            if (misfireInstruction > Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount)
            {
                return false;
            }

            return true;
		           
		}

		/// <summary>
		/// Updates the <see cref="ISimpleTrigger" />'s state based on the
        /// MisfireInstruction value that was selected when the <see cref="ISimpleTrigger" />
		/// was created.
		/// </summary>
		/// <remarks>
		/// If MisfireSmartPolicyEnabled is set to true,
		/// then the following scheme will be used: <br />
		/// <ul>
		/// <li>If the Repeat Count is 0, then the instruction will
        /// be interpreted as <see cref="MisfireInstruction.SimpleTrigger.FireNow" />.</li>
		/// <li>If the Repeat Count is <see cref="RepeatIndefinitely" />, then
        /// the instruction will be interpreted as <see cref="MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount" />.
        /// <b>WARNING:</b> using MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount 
		/// with a trigger that has a non-null end-time may cause the trigger to 
		/// never fire again if the end-time arrived during the misfire time span. 
		/// </li>
		/// <li>If the Repeat Count is > 0, then the instruction
        /// will be interpreted as <see cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount" />.
		/// </li>
		/// </ul>
		/// </remarks>
		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instr = MisfireInstruction;
			if (instr == Quartz.MisfireInstruction.SmartPolicy)
			{
				if (RepeatCount == 0)
				{
                    instr = Quartz.MisfireInstruction.SimpleTrigger.FireNow;
				}
				else if (RepeatCount == RepeatIndefinitely)
				{
                    instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount;
					    
				}
				else
				{
                    instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
				}
			}
            else if (instr == Quartz.MisfireInstruction.SimpleTrigger.FireNow && RepeatCount != 0)
			{
                instr = Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
			}

            if (instr == Quartz.MisfireInstruction.SimpleTrigger.FireNow)
			{
				nextFireTimeUtc = SystemTime.UtcNow();
			}
			else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount)
			{
                DateTimeOffset? newFireTime = GetFireTimeAfter(SystemTime.UtcNow());

                while (newFireTime.HasValue && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
				{
					newFireTime = GetFireTimeAfter(newFireTime);

                    if (!newFireTime.HasValue)
                    {
                        break;
                    }
                    
                    //avoid infinite loop
                    if (newFireTime.Value.Year > YearToGiveupSchedulingAt)
                    {
                        newFireTime = null;
                    }
				}
				nextFireTimeUtc = newFireTime;
			}
			else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount)
			{
                DateTimeOffset? newFireTime = GetFireTimeAfter(SystemTime.UtcNow());

				while (newFireTime.HasValue && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
				{
					newFireTime = GetFireTimeAfter(newFireTime);

                    if (!newFireTime.HasValue)
                    {
                        break;
                    }

                    //avoid infinite loop
                    if (newFireTime.Value.Year > YearToGiveupSchedulingAt)
                    {
                        newFireTime = null;
                    }
				}

				if (newFireTime.HasValue)
				{
					int timesMissed = ComputeNumTimesFiredBetween(nextFireTimeUtc, newFireTime);
					TimesTriggered = TimesTriggered + timesMissed;
				}

                nextFireTimeUtc = newFireTime;
			}
			else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount)
			{
				DateTimeOffset newFireTime = SystemTime.UtcNow();
				if (repeatCount != 0 && repeatCount != RepeatIndefinitely)
				{
					RepeatCount = RepeatCount - TimesTriggered;
					TimesTriggered = 0;
				}

				if (EndTimeUtc.HasValue && EndTimeUtc.Value < newFireTime) 
				{
					nextFireTimeUtc = null; // We are past the end time
				} 
				else 
				{
					StartTimeUtc = newFireTime;
					nextFireTimeUtc = newFireTime;
				}
			}
			else if (instr == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount)
			{
				DateTimeOffset newFireTime = SystemTime.UtcNow();
				int timesMissed = ComputeNumTimesFiredBetween(nextFireTimeUtc, newFireTime);

				if (repeatCount != 0 && repeatCount != RepeatIndefinitely)
				{
					int remainingCount = RepeatCount - (TimesTriggered + timesMissed);
					if (remainingCount <= 0)
					{
						remainingCount = 0;
					}
					RepeatCount = remainingCount;
					TimesTriggered = 0;
				}


				if (EndTimeUtc.HasValue && EndTimeUtc.Value < newFireTime) 
				{
					nextFireTimeUtc = null; // We are past the end time
				} 
				else 
				{
					StartTimeUtc = newFireTime;
					nextFireTimeUtc = newFireTime;
				} 
			}
		}

		/// <summary>
		/// Called when the <see cref="IScheduler" /> has decided to 'fire'
		/// the trigger (Execute the associated <see cref="IJob" />), in order to
		/// give the <see cref="ITrigger" /> a chance to update itself for its next
		/// triggering (if any).
		/// </summary>
		/// <seealso cref="JobExecutionException" />
		public override void Triggered(ICalendar cal)
		{
			timesTriggered++;
			previousFireTimeUtc = nextFireTimeUtc;
			nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

			while (nextFireTimeUtc.HasValue && cal != null && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
			{
				nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (!nextFireTimeUtc.HasValue)
                {
                     break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
                {
                    nextFireTimeUtc = null;
                }
			}
		}


        /// <summary>
        /// Updates the instance with new calendar.
        /// </summary>
        /// <param name="calendar">The calendar.</param>
        /// <param name="misfireThreshold">The misfire threshold.</param>
		public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
		{
			nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

            if (nextFireTimeUtc == null || calendar == null)
            {
                return;
            }

            DateTimeOffset now = SystemTime.UtcNow();
            while (nextFireTimeUtc.HasValue && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (!nextFireTimeUtc.HasValue)
                {
                    break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
                {
                    nextFireTimeUtc = null;
                }

                if (nextFireTimeUtc != null && nextFireTimeUtc.Value < now)
                {
                    TimeSpan diff = now - nextFireTimeUtc.Value;
                    if (diff >= misfireThreshold)
                    {
                        nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
                    }
                }
            }
        }

		/// <summary>
		/// Called by the scheduler at the time a <see cref="ITrigger" /> is first
		/// added to the scheduler, in order to have the <see cref="ITrigger" />
		/// compute its first fire time, based on any associated calendar.
		/// <para>
		/// After this method has been called, <see cref="GetNextFireTimeUtc" />
		/// should return a valid answer.
		/// </para>
		/// </summary>
		/// <returns> 
		/// The first time at which the <see cref="ITrigger" /> will be fired
		/// by the scheduler, which is also the same value <see cref="GetNextFireTimeUtc" />
		/// will return (until after the first firing of the <see cref="ITrigger" />).
		/// </returns>
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar cal)
    	{
			nextFireTimeUtc = StartTimeUtc;

			while (cal != null && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
			{
				nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (!nextFireTimeUtc.HasValue)
                {
                    break;
                }

                //avoid infinite loop
                if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
                {
                    return null;
                }
			}

			return nextFireTimeUtc;
		}

		
		/// <summary>
		/// Returns the next time at which the <see cref="ISimpleTrigger" /> will
		/// fire. If the trigger will not fire again, <see langword="null" /> will be
		/// returned. The value returned is not guaranteed to be valid until after
		/// the <see cref="ITrigger" /> has been added to the scheduler.
		/// </summary>
        public override DateTimeOffset? GetNextFireTimeUtc()
		{
			return nextFireTimeUtc;
		}

        public override void SetNextFireTimeUtc(DateTimeOffset? nextFireTime)
	    {
            nextFireTimeUtc = nextFireTime;
	    }

        public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime)
	    {
            previousFireTimeUtc = previousFireTime;
	    }

	    /// <summary>
	    /// Returns the previous time at which the <see cref="ISimpleTrigger" /> fired.
	    /// If the trigger has not yet fired, <see langword="null" /> will be
	    /// returned.
	    /// </summary>
	    public override DateTimeOffset? GetPreviousFireTimeUtc()
	    {
	        return previousFireTimeUtc;
	    }

		/// <summary> 
		/// Returns the next UTC time at which the <see cref="ISimpleTrigger" /> will
		/// fire, after the given UTC time. If the trigger will not fire after the given
		/// time, <see langword="null" /> will be returned.
		/// </summary>
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTimeUtc)
		{
			if ((timesTriggered > repeatCount) && (repeatCount != RepeatIndefinitely))
			{
				return null;
			}

			if (!afterTimeUtc.HasValue)
			{
				afterTimeUtc = SystemTime.UtcNow();
			}

			if (repeatCount == 0 && afterTimeUtc.Value.CompareTo(StartTimeUtc) >= 0)
			{
				return null;
			}

			DateTimeOffset startMillis = StartTimeUtc;
			DateTimeOffset afterMillis = afterTimeUtc.Value;
			DateTimeOffset endMillis = !EndTimeUtc.HasValue ? DateTimeOffset.MaxValue : EndTimeUtc.Value;


			if (endMillis <= afterMillis) 
			{
				return null;
			}

			if (afterMillis < startMillis) 
			{
				return startMillis;
			}

			long numberOfTimesExecuted = (long) (((long) (afterMillis - startMillis).TotalMilliseconds / repeatInterval.TotalMilliseconds) + 1);

			if ((numberOfTimesExecuted > repeatCount) && 
				(repeatCount != RepeatIndefinitely)) 
			{
				return null;
			}

			DateTimeOffset time = startMillis.AddMilliseconds(numberOfTimesExecuted * repeatInterval.TotalMilliseconds);

			if (endMillis <= time) 
			{
				return null;
			}


			return time;
		}

		/// <summary>
		/// Returns the last UTC time at which the <see cref="ISimpleTrigger" /> will
		/// fire, before the given time. If the trigger will not fire before the
		/// given time, <see langword="null" /> will be returned.
		/// </summary>
        public virtual DateTimeOffset? GetFireTimeBefore(DateTimeOffset? endUtc)
		{
			if (endUtc.Value < StartTimeUtc)
			{
				return null;
			}

			int numFires = ComputeNumTimesFiredBetween(StartTimeUtc, endUtc);
			return StartTimeUtc.AddMilliseconds(numFires*repeatInterval.TotalMilliseconds);
		}

        /// <summary>
        /// Computes the number of times fired between the two UTC date times.
        /// </summary>
        /// <param name="startTimeUtc">The UTC start date and time.</param>
        /// <param name="endTimeUtc">The UTC end date and time.</param>
        /// <returns></returns>
        public virtual int ComputeNumTimesFiredBetween(DateTimeOffset? startTimeUtc, DateTimeOffset? endTimeUtc)
		{
			long time = (long) (endTimeUtc.Value - startTimeUtc.Value).TotalMilliseconds;
			return (int) (time/repeatInterval.TotalMilliseconds);
		}

		/// <summary> 
		/// Determines whether or not the <see cref="ISimpleTrigger" /> will occur
		/// again.
		/// </summary>
		public override bool GetMayFireAgain()
		{
			return GetNextFireTimeUtc().HasValue;
		}

		/// <summary>
		/// Validates whether the properties of the <see cref="IJobDetail" /> are
		/// valid for submission into a <see cref="IScheduler" />.
		/// </summary>
		public override void Validate()
		{
			base.Validate();

			if (repeatCount != 0 && repeatInterval.TotalMilliseconds < 1)
			{
				throw new SchedulerException("Repeat Interval cannot be zero.");
			}
		}
	}
}
