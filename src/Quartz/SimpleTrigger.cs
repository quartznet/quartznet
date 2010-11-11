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

using Quartz.Util;

namespace Quartz
{
	/// <summary> 
	/// A concrete <see cref="Trigger" /> that is used to fire a <see cref="JobDetail" />
	/// at a given moment in time, and optionally repeated at a specified interval.
	/// </summary>
	/// <seealso cref="Trigger" />
	/// <seealso cref="CronTrigger" />
	/// <seealso cref="TriggerUtils" />
	/// 
	/// <author>James House</author>
	/// <author>Contributions by Lieven Govaerts of Ebitec Nv, Belgium.</author>
	/// <author>Marko Lahma (.NET)</author>
	[Serializable]
	public class SimpleTrigger : Trigger
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
        private bool complete;

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> with no settings.
        /// </summary>
        public SimpleTrigger()
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur immediately, and
        /// not repeat.
        /// </summary>
        public SimpleTrigger(string name) : this(name, null)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur immediately, and
        /// not repeat.
        /// </summary>
        public SimpleTrigger(string name, string group)
            : this(name, group, SystemTime.UtcNow(), null, 0, TimeSpan.Zero)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur immediately, and
        /// repeat at the the given interval the given number of times.
        /// </summary>
        public SimpleTrigger(string name, int repeatCount, TimeSpan repeatInterval)
            : this(name, null, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur immediately, and
        /// repeat at the the given interval the given number of times.
        /// </summary>
        public SimpleTrigger(string name, string group, int repeatCount, TimeSpan repeatInterval)
            : this(name, group, SystemTime.UtcNow(), null, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur at the given time,
        /// and not repeat.
        /// </summary>
        public SimpleTrigger(string name, DateTimeOffset startTimeUtc)
            : this(name, null, startTimeUtc)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur at the given time,
        /// and not repeat.
        /// </summary>
        public SimpleTrigger(string name, string group, DateTimeOffset startTimeUtc)
            : this(name, group, startTimeUtc, null, 0, TimeSpan.Zero)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur at the given time,
        /// and repeat at the the given interval the given number of times, or until
        /// the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="Trigger" /> to fire.</param>
        /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="Trigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="Trigger" /> to repeat
        /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        public SimpleTrigger(string name, DateTimeOffset startTimeUtc,
            DateTimeOffset? endTimeUtc, int repeatCount, TimeSpan repeatInterval)
            : this(name, null, startTimeUtc, endTimeUtc, repeatCount, repeatInterval)
        {
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur at the given time,
        /// and repeat at the the given interval the given number of times, or until
        /// the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        /// <param name="startTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="Trigger" /> to fire.</param>
        /// <param name="endTimeUtc">A UTC <see cref="DateTimeOffset" /> set to the time for the <see cref="Trigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="Trigger" /> to repeat
        /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        public SimpleTrigger(string name, string group, DateTimeOffset startTimeUtc,
            DateTimeOffset? endTimeUtc, int repeatCount, TimeSpan repeatInterval)
            : base(name, group)
        {
            StartTimeUtc = startTimeUtc;
            EndTimeUtc = endTimeUtc;
            RepeatCount = repeatCount;
            RepeatInterval = repeatInterval;
        }

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur at the given time,
        /// fire the identified <see cref="IJob" /> and repeat at the the given
        /// interval the given number of times, or until the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        /// <param name="jobName">Name of the job.</param>
        /// <param name="jobGroup">The job group.</param>
        /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="Trigger" />
        /// to fire.</param>
        /// <param name="endTimeUtc">A <see cref="DateTimeOffset" /> set to the time for the <see cref="Trigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="Trigger" /> to repeat
        /// firing, use RepeatIndefinitely for unlimited times.</param>
        /// <param name="repeatInterval">The time span to pause between the repeat firing.</param>
        public SimpleTrigger(string name, string group, string jobName, string jobGroup, DateTimeOffset startTimeUtc,
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
        /// Get or set thhe number of times the <see cref="SimpleTrigger" /> should
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
        /// Get or set the the time interval at which the <see cref="SimpleTrigger" /> should repeat.
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
        /// Get or set the number of times the <see cref="SimpleTrigger" /> has already
        /// fired.
        /// </summary>
        public virtual int TimesTriggered
        {
            get { return timesTriggered; }
            set { timesTriggered = value; }
        }

        /// <summary> 
        /// Returns the final UTC time at which the <see cref="SimpleTrigger" /> will
        /// fire, if repeatCount is RepeatIndefinitely, null will be returned.
        /// <p>
        /// Note that the return time may be in the past.
        /// </p>
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
            return (misfireInstruction == Quartz.MisfireInstruction.SimpleTrigger.FireNow) 
                || (misfireInstruction == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithExistingCount) 
                || (misfireInstruction == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNextWithRemainingCount)
                || (misfireInstruction == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithExistingRepeatCount)
                || (misfireInstruction == Quartz.MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount)
                || (misfireInstruction == Quartz.MisfireInstruction.SmartPolicy);
		           
		}

		/// <summary>
		/// Updates the <see cref="SimpleTrigger" />'s state based on the
        /// MisfireInstruction value that was selected when the <see cref="SimpleTrigger" />
		/// was created.
		/// </summary>
		/// <remarks>
		/// If MisfireSmartPolicyEnabled is set to true,
		/// then the following scheme will be used: <br />
		/// <ul>
		/// <li>If the Repeat Count is 0, then the instruction will
        /// be interpreted as <see cref="MisfireInstruction.SimpleTrigger.FireNow" />.</li>
		/// <li>If the Repeat Count is <see cref="RepeatIndefinitely" />, then
        /// the instruction will be interpreted as <see cref="MisfireInstruction.SimpleTrigger.RescheduleNowWithRemainingRepeatCount" />.
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
				SetNextFireTime(SystemTime.UtcNow());
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
				SetNextFireTime(newFireTime);
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

				SetNextFireTime(newFireTime);
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
					SetNextFireTime(null); // We are past the end time
				} 
				else 
				{
					StartTimeUtc = newFireTime;
					SetNextFireTime(newFireTime);
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
					SetNextFireTime(null); // We are past the end time
				} 
				else 
				{
					StartTimeUtc = newFireTime;
					SetNextFireTime(newFireTime);
				} 
			}
		}

		/// <summary>
		/// Called when the <see cref="IScheduler" /> has decided to 'fire'
		/// the trigger (Execute the associated <see cref="IJob" />), in order to
		/// give the <see cref="Trigger" /> a chance to update itself for its next
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
		/// Called by the scheduler at the time a <see cref="Trigger" /> is first
		/// added to the scheduler, in order to have the <see cref="Trigger" />
		/// compute its first fire time, based on any associated calendar.
		/// <p>
		/// After this method has been called, <see cref="GetNextFireTimeUtc" />
		/// should return a valid answer.
		/// </p>
		/// </summary>
		/// <returns> 
		/// The first time at which the <see cref="Trigger" /> will be fired
		/// by the scheduler, which is also the same value <see cref="GetNextFireTimeUtc" />
		/// will return (until after the first firing of the <see cref="Trigger" />).
		/// </returns>
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar cal)
    	{
			nextFireTimeUtc = StartTimeUtc;

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
                    return null;
                }
			}

			return nextFireTimeUtc;
		}

		
		/// <summary>
		/// Returns the next time at which the <see cref="SimpleTrigger" /> will
		/// fire. If the trigger will not fire again, <see langword="null" /> will be
		/// returned. The value returned is not guaranteed to be valid until after
		/// the <see cref="Trigger" /> has been added to the scheduler.
		/// </summary>
        public override DateTimeOffset? GetNextFireTimeUtc()
		{
			return nextFireTimeUtc;
		}

		/// <summary>
		/// Returns the previous time at which the <see cref="SimpleTrigger" /> fired.
		/// If the trigger has not yet fired, <see langword="null" /> will be
		/// returned.
		/// </summary>
        public override DateTimeOffset? GetPreviousFireTimeUtc()
		{
			return previousFireTimeUtc;
		}

		/// <summary>
		/// Set the next UTC time at which the <see cref="SimpleTrigger" /> should fire.
		/// <strong>This method should not be invoked by client code.</strong>
		/// </summary>
        public void SetNextFireTime(DateTimeOffset? fireTimeUtc)
		{
			nextFireTimeUtc = fireTimeUtc;
		}

		/// <summary>
		/// Set the previous UTC time at which the <see cref="SimpleTrigger" /> fired.
		/// <strong>This method should not be invoked by client code.</strong>
		/// </summary>
        public virtual void SetPreviousFireTime(DateTimeOffset? fireTimeUtc)
		{
			previousFireTimeUtc = fireTimeUtc;
		}

		/// <summary> 
		/// Returns the next UTC time at which the <see cref="SimpleTrigger" /> will
		/// fire, after the given UTC time. If the trigger will not fire after the given
		/// time, <see langword="null" /> will be returned.
		/// </summary>
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTimeUtc)
		{
			if (complete)
			{
				return null;
			}

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
		/// Returns the last UTC time at which the <see cref="SimpleTrigger" /> will
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
		/// Determines whether or not the <see cref="SimpleTrigger" /> will occur
		/// again.
		/// </summary>
		public override bool GetMayFireAgain()
		{
			return GetNextFireTimeUtc().HasValue;
		}

		/// <summary>
		/// Validates whether the properties of the <see cref="JobDetail" /> are
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
