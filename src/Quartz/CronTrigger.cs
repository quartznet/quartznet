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

#if NET_20
using NullableDateTime = System.Nullable<System.DateTime>;
#else
using Nullables;
#endif

using Quartz.Spi;
using Quartz.Util;

namespace Quartz
{
	/// <summary>
	/// A concrete <see cref="Trigger" /> that is used to fire a <see cref="JobDetail" />
	/// at given moments in time, defined with Unix 'cron-like' definitions.
	/// </summary>
	/// <remarks>
	/// <p>
	/// For those unfamiliar with "cron", this means being able to create a firing
	/// schedule such as: "At 8:00am every Monday through Friday" or "At 1:30am
	/// every last Friday of the month".
	/// </p>
	/// 
	/// <p>
	/// The format of a "Cron-Expression" string is documented on the 
	/// <see cref="CronExpression" /> class.
	/// </p>
	/// 
	/// <p>
	/// Here are some full examples: <br />
	/// <table cellspacing="8">
	/// <tr>
	/// <th align="left">Expression</th>
	/// <th align="left"> </th>
	/// <th align="left">Meaning</th>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 0 12 * * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 12pm (noon) every day" /></td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 ? * *"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am every day" /></td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 * * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am every day" /></td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 * * ? *"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am every day" /></td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 * * ? 2005"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am every day during the year 2005" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 * 14 * * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire every minute starting at 2pm and ending at 2:59pm, every day" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 0/5 14 * * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire every 5 minutes starting at 2pm and ending at 2:55pm, every day" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 0/5 14,18 * * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire every 5 minutes starting at 2pm and ending at 2:55pm, AND fire every 5 minutes starting at 6pm and ending at 6:55pm, every day" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 0-5 14 * * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire every minute starting at 2pm and ending at 2:05pm, every day" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 10,44 14 ? 3 WED"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 2:10pm and at 2:44pm every Wednesday in the month of March." />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 ? * MON-FRI"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am every Monday, Tuesday, Wednesday, Thursday and Friday" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 15 * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am on the 15th day of every month" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 L * ?"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am on the last day of every month" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 ? * 6L"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am on the last Friday of every month" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 ? * 6L"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am on the last Friday of every month" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 ? * 6L 2002-2005"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am on every last friday of every month during the years 2002, 2003, 2004 and 2005" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 ? * 6#3"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am on the third Friday of every month" />
	/// </td>
	/// </tr>
	/// </table>
	/// </p>
	/// 
	/// <p>
	/// Pay attention to the effects of '?' and '*' in the day-of-week and
	/// day-of-month fields!
	/// </p>
	/// 
	/// <p>
	/// <b>NOTES:</b>
	/// <ul>
	/// <li>Support for specifying both a day-of-week and a day-of-month value is
	/// not complete (you'll need to use the '?' character in on of these fields).
	/// </li>
	/// <li>Be careful when setting fire times between mid-night and 1:00 AM -
	/// "daylight savings" can cause a skip or a repeat depending on whether the
	/// time moves back or jumps forward.</li>
	/// </ul>
	/// </p>
	/// </remarks>
	/// <seealso cref="Trigger"/>
	/// <seealso cref="SimpleTrigger"/>
	/// <seealso cref="TriggerUtils"/>
	/// <author>Sharada Jambula</author>
	/// <author>James House</author>
	/// <author>Contributions from Mads Henderson</author>
    [Serializable]
	public class CronTrigger : Trigger
	{
		private CronExpression cronEx = null;
		private DateTime startTimeUtc = DateTime.MinValue;
        private NullableDateTime endTimeUtc = null;
		private NullableDateTime nextFireTimeUtc = null;
		private NullableDateTime previousFireTimeUtc = null;
		[NonSerialized] private TimeZone timeZone = null;

		/// <summary>
		/// Create a <see cref="CronTrigger" /> with no settings.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
        /// </remarks>
		public CronTrigger()
		{
			StartTimeUtc = DateTime.UtcNow;
			TimeZone = TimeZone.CurrentTimeZone;
		}

		/// <summary>
		/// Create a <see cref="CronTrigger" /> with the given name and group.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
        /// </remarks>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		public CronTrigger(string name, string group) : base(name, group)
		{
			StartTimeUtc = DateTime.UtcNow;
			TimeZone = TimeZone.CurrentTimeZone;
		}


		/// <summary>
		/// Create a <see cref="CronTrigger" /> with the given name, group and
		/// expression.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
        /// </remarks>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="cronExpression">The cron expression.</param>
		public CronTrigger(string name, string group, string cronExpression) : base(name, group)
		{
			CronExpressionString = cronExpression;
			StartTimeUtc = DateTime.UtcNow;
			TimeZone = TimeZone.CurrentTimeZone;
		}


		/// <summary>
		/// Create a <see cref="CronTrigger" /> with the given name and group, and
		/// associated with the identified <see cref="JobDetail" />.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
		/// </remarks>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		public CronTrigger(String name, string group, string jobName,
			string jobGroup) : base(name, group, jobName, jobGroup)
		{
			StartTimeUtc = DateTime.UtcNow;
			TimeZone = TimeZone.CurrentTimeZone;
		}

		/// <summary>
		/// Create a <see cref="CronTrigger" /> with the given name and group,
		/// associated with the identified <see cref="JobDetail" />,
		/// and with the given "cron" expression.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
        /// </remarks>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="cronExpression">The cron expression.</param>
		public CronTrigger(string name, string group, string jobName,
			string jobGroup, string cronExpression)
			: this(name, group, jobName, jobGroup, DateTime.UtcNow, null, cronExpression, TimeZone.CurrentTimeZone)
		{
		}

		/// <summary>
		/// Create a <see cref="CronTrigger" /> with the given name and group,
		/// associated with the identified <see cref="JobDetail" />,
		/// and with the given "cron" expression resolved with respect to the <see cref="TimeZone" />.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="cronExpression">The cron expression.</param>
		/// <param name="timeZone">The time zone.</param>
		public CronTrigger(string name, string group, string jobName,
			string jobGroup, string cronExpression, TimeZone timeZone)
			: this(name, group, jobName, jobGroup, DateTime.UtcNow, null, cronExpression,
			timeZone)
		{
		}


		/// <summary>
		/// Create a <see cref="CronTrigger" /> that will occur at the given time,
		/// until the given end time.
		/// <p>
		/// If null, the start-time will also be set to the current time, the time
		/// zone will be set the the system's default.
		/// </p>
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="startTime">The start time.</param>
		/// <param name="endTime">The end time.</param>
		/// <param name="cronExpression">The cron expression.</param>
		public CronTrigger(string name, string group, string jobName,
			string jobGroup, DateTime startTime, 
            NullableDateTime endTime, 
            string cronExpression)
			: base(name, group, jobName, jobGroup)
		{
			CronExpressionString = cronExpression;

			if (startTime == DateTime.MinValue)
			{
				startTime = DateTime.UtcNow;
			}
			StartTimeUtc = DateTimeUtil.AssumeUniversalTime(startTime);
			if (endTime.HasValue)
			{
				EndTimeUtc = endTime;
			}
			TimeZone = TimeZone.CurrentTimeZone;
		}


		/// <summary>
		/// Create a <see cref="CronTrigger" /> with fire time dictated by the
		/// <param name="cronExpression" /> resolved with respect to the specified
		/// <param name="timeZone" /> occuring from the <see cref="startTimeUtc" /> until
		/// the given <paran name="endTimeUtc" />.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="startTime">The start time.</param>
		/// <param name="endTime">The end time.</param>
		public CronTrigger(string name, string group, string jobName,
			string jobGroup, DateTime startTime, 
            NullableDateTime endTime,
			string cronExpression, TimeZone timeZone) : base(name, group, jobName, jobGroup)
		{
			CronExpressionString = cronExpression;

			if (startTime == DateTime.MinValue)
			{
				startTime = DateTime.UtcNow;
			}
			StartTimeUtc = DateTimeUtil.AssumeUniversalTime(startTime);
			if (endTime.HasValue)
			{
				EndTimeUtc = endTime;
			}
			if (timeZone == null)
			{
				TimeZone = TimeZone.CurrentTimeZone;
			}
			else
			{
				TimeZone = timeZone;
			}
		}

		/// <summary>
		/// Clones this instance.
		/// </summary>
		/// <returns></returns>
		public override object Clone()
		{
			CronTrigger copy = (CronTrigger) MemberwiseClone();
			if (cronEx != null)
			{
			    copy.CronExpression = (CronExpression) cronEx.Clone();
			}
			return copy;
		}

		/// <summary>
		/// Gets or sets the cron expression string.
		/// </summary>
		/// <value>The cron expression string.</value>
		public string CronExpressionString
		{
			set
			{
				cronEx = new CronExpression(value);
				cronEx.TimeZone = TimeZone;
			}
			get { return cronEx == null ? null : cronEx.CronExpressionString; }
		}

		/// <summary>
		/// Sets the cron expression.
		/// </summary>
		/// <value>The cron expression.</value>
		public CronExpression CronExpression
		{
			set
			{
				cronEx = value;
				timeZone = value.TimeZone;
			}
		}

		/// <summary>
		/// Returns the date/time on which the trigger may begin firing. This
		/// defines the initial boundary for trigger firings the trigger
		/// will not fire prior to this date and time.
		/// </summary>
		/// <value></value>
		public override DateTime StartTimeUtc
		{
			get
			{
				return startTimeUtc;
			}
			set
			{
				NullableDateTime eTime = EndTimeUtc;
				if (eTime.HasValue && eTime.Value < value)
				{
					throw new ArgumentException("End time cannot be before start time");
				}
        
				// round off millisecond...
				DateTime dt = new DateTime(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second);
			    DateTimeUtil.AssumeUniversalTime(dt);
                startTimeUtc = dt;
			}
		}


		/// <summary>
		/// Get or sets the time at which the <c>CronTrigger</c> should quit
		/// repeating - even if repeastCount isn't yet satisfied. 
		/// </summary>
		public override NullableDateTime EndTimeUtc
		{
			get
			{
				return endTimeUtc;
			}
			set
			{
				DateTime sTime = StartTimeUtc;
				if (value.HasValue && sTime > value.Value)
				{
					throw new ArgumentException("End time cannot be before start time");
				}

				endTimeUtc = DateTimeUtil.AssumeUniversalTime(value);
			}
		}


		/// <summary>
		/// Returns the next time at which the <see cref="Trigger" /> will fire. If
		/// the trigger will not fire again, <see langword="null" /> will be returned.
		/// The value returned is not guaranteed to be valid until after the <see cref="Trigger" />
		/// has been added to the scheduler.
		/// </summary>
        /// <returns></returns>
        public override NullableDateTime GetNextFireTimeUtc()
        {
			return nextFireTimeUtc;
		}

		/// <summary>
		/// Returns the previous time at which the <see cref="Trigger" /> fired.
		/// If the trigger has not yet fired, <see langword="null" /> will be returned.
		/// </summary>
        /// <returns></returns>
        public override NullableDateTime GetPreviousFireTimeUtc()
		{
			return previousFireTimeUtc;
		}


		/// <summary>
		/// Sets the next fire time.
		/// <p>
		/// <b>This method should not be invoked by client code.</b>
		/// </p>
		/// </summary>
		/// <param name="fireTime">The fire time.</param>
        public void SetNextFireTime(NullableDateTime fireTime)
		{
			nextFireTimeUtc = DateTimeUtil.AssumeUniversalTime(fireTime);
		}


		/// <summary>
		/// Sets the previous fire time.
		/// <p>
		/// <b>This method should not be invoked by client code.</b>
		/// </p>
		/// </summary>
		/// <param name="fireTime">The fire time.</param>
        public void SetPreviousFireTime(NullableDateTime fireTime)
		{
			previousFireTimeUtc = DateTimeUtil.AssumeUniversalTime(fireTime);
		}


		/// <summary>
		/// Gets or sets the time zone.
		/// </summary>
		/// <value>The time zone.</value>
		public TimeZone TimeZone
		{
			get
			{
				if (cronEx != null)
				{
					return cronEx.TimeZone;
				}

				if (timeZone == null)
				{
					timeZone = TimeZone.CurrentTimeZone;
				}
				return timeZone;
			}
			set
			{
				if (cronEx != null)
				{
					cronEx.TimeZone = value;
				}
				timeZone = value;
			}
		}


		/// <summary>
		/// Returns the next time at which the <see cref="Trigger" /> will fire,
		/// after the given time. If the trigger will not fire after the given time,
		/// <see langword="null" /> will be returned.
		/// </summary>
		/// <param name="afterTimeUtc"></param>
        /// <returns></returns>
        public override NullableDateTime GetFireTimeAfter(NullableDateTime afterTimeUtc)
		{
			if (!afterTimeUtc.HasValue)
			{
				afterTimeUtc = DateTime.UtcNow;
			}

			if (StartTimeUtc > afterTimeUtc.Value)
			{
				afterTimeUtc = DateTimeUtil.AssumeUniversalTime(startTimeUtc).AddSeconds(-1);
			}

            if (EndTimeUtc.HasValue && (afterTimeUtc.Value.CompareTo(EndTimeUtc.Value) >= 0))
            {
                return null;
            }

            NullableDateTime pot = GetTimeAfter(afterTimeUtc.Value);
            if (EndTimeUtc.HasValue && pot.Value > EndTimeUtc.Value)
			{
				return null;
			}

			return pot;
		}

		/// <summary>
		/// Returns the last UTC time at which the <see cref="Trigger" /> will fire, if
		/// the Trigger will repeat indefinitely, null will be returned.
		/// <p>
		/// Note that the return time *may* be in the past.
		/// </p>
        /// </summary>
        public override NullableDateTime FinalFireTimeUtc
		{
			get
            {
                NullableDateTime resultTime;
                if (EndTimeUtc.HasValue)
                {
                    resultTime = GetTimeBefore(EndTimeUtc.Value.AddSeconds(1));
                }
                else
                {
                    resultTime = (cronEx == null) ? null : cronEx.GetFinalFireTime();
                }

                if (resultTime.HasValue && resultTime.Value < StartTimeUtc)
                {
                    return null;
                }

                return resultTime;
			}
		}

		/// <summary>
		/// Tells whether this Trigger instance can handle events
		/// in millisecond precision.
		/// </summary>
		/// <value></value>
		public override bool HasMillisecondPrecision
		{
			get { return false; }
		}

		/// <summary>
		/// Used by the <see cref="IScheduler" /> to determine whether or not
		/// it is possible for this <see cref="Trigger" /> to fire again.
		/// <p>
		/// If the returned value is <see langword="false" /> then the <see cref="IScheduler" />
		/// may remove the <see cref="Trigger" /> from the <see cref="IJobStore" />.
		/// </p>
		/// </summary>
		/// <returns></returns>
		public override bool GetMayFireAgain()
		{
			return GetNextFireTimeUtc().HasValue;
		}

		/// <summary>
		/// Validates the misfire instruction.
		/// </summary>
		/// <param name="misfireInstruction">The misfire instruction.</param>
		/// <returns></returns>
		protected override bool ValidateMisfireInstruction(int misfireInstruction)
		{
            return (misfireInstruction == MisfirePolicy.CronTrigger.DoNothing) 
                || (misfireInstruction == MisfirePolicy.CronTrigger.FireOnceNow)
                || (misfireInstruction == MisfirePolicy.SmartPolicy);
			
		}


		/// <summary>
		/// This method should not be used by the Quartz client.
		/// <p>
		/// To be implemented by the concrete classes that extend this class.
		/// </p>
		/// <p>
		/// The implementation should update the <see cref="Trigger" />'s state
		/// based on the MISFIRE_INSTRUCTION_XXX that was selected when the <see cref="Trigger" />
		/// was created.
		/// </p>
		/// </summary>
		/// <param name="cal"></param>
		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instr = MisfireInstruction;

			if (instr == MisfirePolicy.SmartPolicy)
			{
				instr = MisfirePolicy.CronTrigger.FireOnceNow;
			}

			if (instr == MisfirePolicy.CronTrigger.DoNothing)
			{
                NullableDateTime newFireTime = GetFireTimeAfter(DateTime.Now);

                while (newFireTime.HasValue && cal != null
				       && !cal.IsTimeIncluded(newFireTime.Value))
				{
					newFireTime = GetFireTimeAfter(newFireTime);
				}
				SetNextFireTime(newFireTime);
			}
			else if (instr == MisfirePolicy.CronTrigger.FireOnceNow)
			{
				SetNextFireTime(DateTime.UtcNow);
			}
		}


		/// <summary>
		/// <p>
		/// Determines whether the date and (optionally) time of the given Calendar 
		/// instance falls on a scheduled fire-time of this trigger.
		/// </p>
		/// 
		/// <p>
		/// Equivalent to calling <see cref="WillFireOn(DateTime, bool)" />.
		/// </p>
		/// </summary>
		/// <param name="test">The date to compare.</param>
		/// <returns></returns>
		public bool WillFireOn(DateTime test)
		{
			return WillFireOn(test, false);
		}


		/// <summary>
		/// Determines whether the date and (optionally) time of the given Calendar 
		/// instance falls on a scheduled fire-time of this trigger.
		/// <p>
		/// Note that the value returned is NOT validated against the related
		/// ICalendar (if any).
		/// </p>
		/// </summary>
		/// <param name="test">The date to compare</param>
		/// <param name="dayOnly">If set to true, the method will only determine if the
		/// trigger will fire during the day represented by the given Calendar
		/// (hours, minutes and seconds will be ignored).</param>
		/// <returns></returns>
		public bool WillFireOn(DateTime test, bool dayOnly)
		{
			test = new DateTime(test.Year, test.Month, test.Day, test.Hour, test.Minute, test.Second);
			if (dayOnly)
			{
				test = new DateTime(test.Year, test.Month, test.Day, 0, 0, 0);
			}
            DateTimeUtil.AssumeUniversalTime(test);

            NullableDateTime fta = GetFireTimeAfter(test.AddMilliseconds(-1 * 1000));
            DateTime p = TimeZone.ToLocalTime(fta.Value);

			if (dayOnly)
			{
				return (p.Year == test.Year
				        && p.Month == test.Month
				        && p.Day == test.Day);
			}

			while (fta.Value < test)
			{
				fta = GetFireTimeAfter(fta);
			}

			if (fta.Equals(test))
			{
				return true;
			}   

			return false;
		}

		/// <summary>
		/// Called when the <see cref="IScheduler" /> has decided to 'fire'
		/// the trigger (Execute the associated <see cref="IJob" />), in order to
		/// give the <see cref="Trigger" /> a chance to update itself for its next
		/// triggering (if any).
		/// </summary>
		/// <param name="cal"></param>
		/// <seealso cref="JobExecutionException" />
		public override void Triggered(ICalendar cal)
		{
			previousFireTimeUtc = nextFireTimeUtc;
			nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);

			while (nextFireTimeUtc.HasValue && cal != null
			       && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
			{
				nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
			}
		}

		/// <summary>
		/// Updates the trigger with new calendar.
		/// </summary>
		/// <param name="calendar">The calendar to update with.</param>
		/// <param name="misfireThreshold">The misfire threshold.</param>
		public override void UpdateWithNewCalendar(ICalendar calendar, long misfireThreshold)
		{
			nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

			DateTime now = DateTime.UtcNow;
			do
			{
				while (nextFireTimeUtc.HasValue && calendar != null
				       && !calendar.IsTimeIncluded(nextFireTimeUtc.Value))
				{
					nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
				}

				if (nextFireTimeUtc.HasValue && nextFireTimeUtc.Value < (now))
				{
					long diff = (long) (now - nextFireTimeUtc.Value).TotalMilliseconds;
					if (diff >= misfireThreshold)
					{
						nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
						continue;
					}
				}
			} while (false);
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
		/// <param name="cal"></param>
		/// <returns>
		/// the first time at which the <see cref="Trigger" /> will be fired
		/// by the scheduler, which is also the same value <see cref="GetNextFireTimeUtc" />
		/// will return (until after the first firing of the <see cref="Trigger" />).
		/// </returns>
        public override NullableDateTime ComputeFirstFireTimeUtc(ICalendar cal)
        {
			nextFireTimeUtc = GetFireTimeAfter(startTimeUtc.AddSeconds(-1));

			while (nextFireTimeUtc.HasValue && cal != null && !cal.IsTimeIncluded(nextFireTimeUtc.Value))
			{
				nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
			}

			return nextFireTimeUtc;
		}

		/// <summary>
		/// Gets the expression summary.
		/// </summary>
		/// <returns></returns>
		public string GetExpressionSummary()
		{
			return cronEx == null ? null : cronEx.GetExpressionSummary();
		}

		////////////////////////////////////////////////////////////////////////////
		//
		// Computation Functions
		//
		////////////////////////////////////////////////////////////////////////////

		/// <summary>
		/// Gets the next time to fire after the given time.
		/// </summary>
		/// <param name="afterTime">The time to compute from.</param>
		/// <returns></returns>
        protected NullableDateTime GetTimeAfter(DateTime afterTime)
        {
            if (cronEx != null)
            {
                return cronEx.GetTimeAfter(afterTime);
            }
            else
            {
                return null;
            }
		}

		/// <summary>
		/// NOT YET IMPLEMENTED: Returns the time before the given time
        /// that this <code>CronTrigger</code> will fire.
		/// </summary>
		/// <param name="date">The date.</param>
		/// <returns></returns> 
        protected NullableDateTime GetTimeBefore(NullableDateTime date)
		{
            return (cronEx == null) ? null : cronEx.GetTimeBefore(endTimeUtc);
		}
	}
}
