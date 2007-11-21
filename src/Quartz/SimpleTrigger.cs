/* 
* Copyright 2004-2005 OpenSymphony 
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

/*
* Previously Copyright (c) 2001-2004 James House
*/

using System;

using Quartz.Util;

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

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

        private NullableDateTime nextFireTimeUtc = null;
		private NullableDateTime previousFireTimeUtc = null;

        private int repeatCount = 0;
        private long repeatInterval = 0;
        private int timesTriggered = 0;
        private bool complete = false;

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
		public SimpleTrigger(string name, string group) : this(name, group, DateTime.UtcNow, null, 0, 0)
		{
		}

		/// <summary>
		/// Create a <see cref="SimpleTrigger" /> that will occur immediately, and
		/// repeat at the the given interval the given number of times.
		/// </summary>
		public SimpleTrigger(string name, string group, int repeatCount, long repeatInterval)
			: this(name, group, DateTime.UtcNow, null, repeatCount, repeatInterval)
		{
		}

		/// <summary>
		/// Create a <see cref="SimpleTrigger" /> that will occur at the given time,
		/// and not repeat.
		/// </summary>
		public SimpleTrigger(string name, string group, DateTime startTimeUtc) : this(name, group, startTimeUtc, null, 0, 0)
		{
		}

        /// <summary>
        /// Create a <see cref="SimpleTrigger" /> that will occur at the given time,
        /// and repeat at the the given interval the given number of times, or until
        /// the given end time.
        /// </summary>
        /// <param name="name">The name.</param>
        /// <param name="group">The group.</param>
        /// <param name="startTimeUtc">A UTC <see cref="DateTime" /> set to the time for the <see cref="Trigger" /> to fire.</param>
        /// <param name="endTimeUtc">A UTC <see cref="DateTime" /> set to the time for the <see cref="Trigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="Trigger" /> to repeat
        /// firing, use <see cref="RepeatIndefinitely "/> for unlimited times.</param>
        /// <param name="repeatInterval">The number of milliseconds to pause between the repeat firing.</param>
		public SimpleTrigger(string name, string group, DateTime startTimeUtc,
            NullableDateTime endTimeUtc, 
            int repeatCount, long repeatInterval) : base(name, group)
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
        /// <param name="startTimeUtc">A <see cref="DateTime" /> set to the time for the <see cref="Trigger" />
        /// to fire.</param>
        /// <param name="endTimeUtc">A <see cref="DateTime" /> set to the time for the <see cref="Trigger" />
        /// to quit repeat firing.</param>
        /// <param name="repeatCount">The number of times for the <see cref="Trigger" /> to repeat
        /// firing, use RepeatIndefinitely for unlimited times.</param>
        /// <param name="repeatInterval">The number of milliseconds to pause between the repeat firing.</param>
		public SimpleTrigger(string name, string group, string jobName, string jobGroup, DateTime startTimeUtc,
                 NullableDateTime endTimeUtc,
                 int repeatCount, long repeatInterval)
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
        /// Get or set the the time interval (in milliseconds) at which the <see cref="SimpleTrigger" />
        /// should repeat.
        /// </summary>
        public long RepeatInterval
        {
            get { return repeatInterval; }

            set
            {
                if (value < 0)
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
        public override NullableDateTime FinalFireTimeUtc
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

                DateTime lastTrigger = StartTimeUtc.AddMilliseconds(repeatCount * repeatInterval);

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
            return (misfireInstruction == MisfirePolicy.SimpleTrigger.FireNow) 
                || (misfireInstruction == MisfirePolicy.SimpleTrigger.RescheduleNextWithExistingCount) 
                || (misfireInstruction == MisfirePolicy.SimpleTrigger.RescheduleNextWithRemainingCount)
                || (misfireInstruction == MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount)
                || (misfireInstruction == MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount)
                || (misfireInstruction == MisfirePolicy.SmartPolicy);
		           
		}

		/// <summary>
		/// Updates the <see cref="SimpleTrigger" />'s state based on the
        /// MisfirePolicy value that was selected when the <see cref="SimpleTrigger" />
		/// was created.
		/// </summary>
		/// <remarks>
		/// If MisfireSmartPolicyEnabled is set to true,
		/// then the following scheme will be used: <br />
		/// <ul>
		/// <li>If the Repeat Count is 0, then the instruction will
        /// be interpreted as <see cref="MisfirePolicy.SimpleTrigger.FireNow" />.</li>
		/// <li>If the Repeat Count is <see cref="RepeatIndefinitely" />, then
        /// the instruction will be interpreted as <see cref="MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount" />.
        /// <b>WARNING:</b> using MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount 
		/// with a trigger that has a non-null end-time may cause the trigger to 
		/// never fire again if the end-time arrived during the misfire time span. 
		/// </li>
		/// <li>If the Repeat Count is > 0, then the instruction
        /// will be interpreted as <see cref="MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount" />.
		/// </li>
		/// </ul>
		/// </remarks>
		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instr = MisfireInstruction;
			if (instr == MisfirePolicy.SmartPolicy)
			{
				if (RepeatCount == 0)
				{
                    instr = MisfirePolicy.SimpleTrigger.FireNow;
				}
				else if (RepeatCount == RepeatIndefinitely)
				{
                    instr = MisfirePolicy.SimpleTrigger.RescheduleNextWithRemainingCount;
					    
				}
				else
				{
                    instr = MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount;
				}
			}
            else if (instr == MisfirePolicy.SimpleTrigger.FireNow && RepeatCount != 0)
			{
                instr = MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount;
			}

            if (instr == MisfirePolicy.SimpleTrigger.FireNow)
			{
				SetNextFireTime(DateTime.UtcNow);
			}
			else if (instr == MisfirePolicy.SimpleTrigger.RescheduleNextWithExistingCount)
			{
                NullableDateTime newFireTime = GetFireTimeAfter(DateTime.UtcNow);

                while (newFireTime.HasValue && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
				{
					newFireTime = GetFireTimeAfter(newFireTime);
				}
				SetNextFireTime(newFireTime);
			}
			else if (instr == MisfirePolicy.SimpleTrigger.RescheduleNextWithRemainingCount)
			{
                NullableDateTime newFireTime = GetFireTimeAfter(DateTime.UtcNow);

				while (newFireTime.HasValue && cal != null && !cal.IsTimeIncluded(newFireTime.Value))
				{
					newFireTime = GetFireTimeAfter(newFireTime);
				}

				if (newFireTime.HasValue)
				{
					int timesMissed = ComputeNumTimesFiredBetween(nextFireTimeUtc, newFireTime);
					TimesTriggered = TimesTriggered + timesMissed;
				}

				SetNextFireTime(newFireTime);
			}
			else if (instr == MisfirePolicy.SimpleTrigger.RescheduleNowWithExistingRepeatCount)
			{
				DateTime newFireTime = DateTime.UtcNow;
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
			else if (instr == MisfirePolicy.SimpleTrigger.RescheduleNowWithRemainingRepeatCount)
			{
				DateTime newFireTime = DateTime.UtcNow;
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
			}
		}


        /// <summary>
        /// Updates the instance with new calendar.
        /// </summary>
        /// <param name="calendar">The calendar.</param>
        /// <param name="misfireThreshold">The misfire threshold.</param>
		public override void UpdateWithNewCalendar(ICalendar calendar, long misfireThreshold)
		{
			nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

            if (nextFireTimeUtc == null || calendar == null)
            {
                return;
            }

            DateTime now = DateTime.UtcNow;
            while (nextFireTimeUtc.HasValue && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
            {
                nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

                if (nextFireTimeUtc != null && nextFireTimeUtc.Value < now)
                {
                    long diff = (long) (now - nextFireTimeUtc.Value).TotalMilliseconds;
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
        public override NullableDateTime ComputeFirstFireTimeUtc(ICalendar cal)
		{
			nextFireTimeUtc = StartTimeUtc;

			while (nextFireTimeUtc.HasValue && cal != null && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
			{
				nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
			}

			return nextFireTimeUtc.Value;
		}

		
		/// <summary>
		/// Returns the next time at which the <see cref="SimpleTrigger" /> will
		/// fire. If the trigger will not fire again, <see langword="null" /> will be
		/// returned. The value returned is not guaranteed to be valid until after
		/// the <see cref="Trigger" /> has been added to the scheduler.
		/// </summary>
        public override NullableDateTime GetNextFireTimeUtc()
		{
			return nextFireTimeUtc;
		}

		/// <summary>
		/// Returns the previous time at which the <see cref="SimpleTrigger" /> fired.
		/// If the trigger has not yet fired, <see langword="null" /> will be
		/// returned.
		/// </summary>
        public override NullableDateTime GetPreviousFireTimeUtc()
		{
			return previousFireTimeUtc;
		}

		/// <summary>
		/// Set the next UTC time at which the <see cref="SimpleTrigger" /> should fire.
		/// <strong>This method should not be invoked by client code.</strong>
		/// </summary>
        public void SetNextFireTime(NullableDateTime fireTimeUtc)
		{
			nextFireTimeUtc = DateTimeUtil.AssumeUniversalTime(fireTimeUtc);
		}

		/// <summary>
		/// Set the previous UTC time at which the <see cref="SimpleTrigger" /> fired.
		/// <strong>This method should not be invoked by client code.</strong>
		/// </summary>
        public virtual void SetPreviousFireTime(NullableDateTime fireTimeUtc)
		{
			previousFireTimeUtc = DateTimeUtil.AssumeUniversalTime(fireTimeUtc);
		}

		/// <summary> 
		/// Returns the next UTC time at which the <see cref="SimpleTrigger" /> will
		/// fire, after the given UTC time. If the trigger will not fire after the given
		/// time, <see langword="null" /> will be returned.
		/// </summary>
        public override NullableDateTime GetFireTimeAfter(NullableDateTime afterTimeUtc)
		{
            afterTimeUtc = DateTimeUtil.AssumeUniversalTime(afterTimeUtc);

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
				afterTimeUtc = DateTime.UtcNow;
			}

			if (repeatCount == 0 && afterTimeUtc.Value.CompareTo(StartTimeUtc) >= 0)
			{
				return null;
			}

			DateTime startMillis = StartTimeUtc;
			DateTime afterMillis = afterTimeUtc.Value;
			DateTime endMillis = !EndTimeUtc.HasValue ? DateTime.MaxValue : EndTimeUtc.Value;


			if (endMillis <= afterMillis) 
			{
				return null;
			}

			if (afterMillis < startMillis) 
			{
				return startMillis;
			}

			long numberOfTimesExecuted = ((long) (afterMillis - startMillis).TotalMilliseconds / repeatInterval) + 1;

			if ((numberOfTimesExecuted > repeatCount) && 
				(repeatCount != RepeatIndefinitely)) 
			{
				return null;
			}

			DateTime time = startMillis.AddMilliseconds(numberOfTimesExecuted * repeatInterval);

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
        public virtual NullableDateTime GetFireTimeBefore(NullableDateTime endUtc)
		{
            endUtc = DateTimeUtil.AssumeUniversalTime(endUtc);

			if (endUtc.Value < StartTimeUtc)
			{
				return null;
			}

			int numFires = ComputeNumTimesFiredBetween(StartTimeUtc, endUtc);
			return StartTimeUtc.AddMilliseconds(numFires*repeatInterval);
		}

        /// <summary>
        /// Computes the number of times fired between the two UTC date times.
        /// </summary>
        /// <param name="startTimeUtc">The UTC start date and time.</param>
        /// <param name="endTimeUtc">The UTC end date and time.</param>
        /// <returns></returns>
        public virtual int ComputeNumTimesFiredBetween(NullableDateTime startTimeUtc, NullableDateTime endTimeUtc)
		{
            startTimeUtc = DateTimeUtil.AssumeUniversalTime(startTimeUtc);
            endTimeUtc = DateTimeUtil.AssumeUniversalTime(endTimeUtc);

			long time = (long) (endTimeUtc.Value - startTimeUtc.Value).TotalMilliseconds;
			return (int) (time/repeatInterval);
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

			if (repeatCount != 0 && repeatInterval < 1)
			{
				throw new SchedulerException("Repeat Interval cannot be zero.", SchedulerException.ErrorClientError);
			}
		}
	}
}
