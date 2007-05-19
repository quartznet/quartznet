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
using Nullables;

namespace Quartz
{
	/// <summary>
	/// <p>
	/// A concrete <code>{@link Trigger}</code> that is used to fire a <code>{@link org.quartz.JobDetail}</code>
	/// at given moments in time, defined with Unix 'cron-like' definitions.
	/// </p>
	/// 
	/// <p>
	/// For those unfamiliar with "cron", this means being able to create a firing
	/// schedule such as: "At 8:00am every Monday through Friday" or "At 1:30am
	/// every last Friday of the month".
	/// </p>
	/// 
	/// <p>
	/// The format of a "Cron-Expression" string is documented on the 
	/// {@link org.quartz.CronExpression} class.
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
	/// <td align="left"><code>"0 0 12 * * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 12pm (noon) every day</code></td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 ? * *"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am every day</code></td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 * * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am every day</code></td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 * * ? *"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am every day</code></td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 * * ? 2005"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am every day during the year 2005</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 * 14 * * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire every minute starting at 2pm and ending at 2:59pm, every day</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 0/5 14 * * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire every 5 minutes starting at 2pm and ending at 2:55pm, every day</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 0/5 14,18 * * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire every 5 minutes starting at 2pm and ending at 2:55pm, AND fire every 5 minutes starting at 6pm and ending at 6:55pm, every day</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 0-5 14 * * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire every minute starting at 2pm and ending at 2:05pm, every day</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 10,44 14 ? 3 WED"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 2:10pm and at 2:44pm every Wednesday in the month of March.</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 ? * MON-FRI"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am every Monday, Tuesday, Wednesday, Thursday and Friday</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 15 * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am on the 15th day of every month</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 L * ?"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am on the last day of every month</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 ? * 6L"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am on the last Friday of every month</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 ? * 6L"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am on the last Friday of every month</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 ? * 6L 2002-2005"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am on every last friday of every month during the years 2002, 2003, 2004 and 2005</code>
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left"><code>"0 15 10 ? * 6#3"</code></td>
	/// <td align="left"> </td>
	/// <td align="left"><code>Fire at 10:15am on the third Friday of every month</code>
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
	/// 
	/// <seealso cref="Trigger"/>
	/// <seealso cref="SimpleTrigger"/>
	/// <seealso cref="TriggerUtils"/>
	/// <author>Sharada Jambula</author>
	/// <author>James House</author>
	/// <author>Contributions from Mads Henderson</author>
	/// </summary>
	public class CronTrigger : Trigger
	{
		private CronExpression cronEx = null;
		private DateTime startTime = DateTime.MinValue;
		private NullableDateTime nextFireTime = null;
		private NullableDateTime previousFireTime = null;
		[NonSerialized] private TimeZone timeZone = null;

		/// <summary>
		/// Instructs the <code>{@link Scheduler}</code> that upon a mis-fire
		/// situation, the <code>{@link CronTrigger}</code> wants to be fired now
		/// by <code>Scheduler</code>.
		/// </summary>
		const int MISFIRE_INSTRUCTION_FIRE_ONCE_NOW = 1;

		/// <summary>
		/// Instructs the <code>{@link Scheduler}</code> that upon a mis-fire
		/// situation, the <code>{@link CronTrigger}</code> wants to have it's
		/// next-fire-time updated to the next time in the schedule after the
		/// current time (taking into account any associated <code>{@link Calendar}</code>,
		/// but it does not want to be fired now.
		/// </summary>
		const int MISFIRE_INSTRUCTION_DO_NOTHING = 2;


		/// <summary>
		/// <p>
		/// Create a <code>CronTrigger</code> with no settings.
		/// </p>
		/// 
		/// <p>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
		/// </p>
		/// </summary>
		public CronTrigger() : base()
		{
			StartTime = DateTime.Now;
			TimeZone = TimeZone.CurrentTimeZone;
		}

		/// <summary>
		/// <p>
		/// Create a <code>CronTrigger</code> with the given name and group.
		/// </p>
		/// 
		/// <p>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
		/// </p>
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		public CronTrigger(string name, string group) : base(name, group)
		{
			StartTime = DateTime.Now;
			TimeZone = TimeZone.CurrentTimeZone;
		}


		/// <summary>
		/// <p>
		/// Create a <code>CronTrigger</code> with the given name, group and
		/// expression.
		/// </p>
		/// 
		/// <p>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
		/// </p>
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="cronExpression">The cron expression.</param>
		public CronTrigger(String name, string group, string cronExpression) : base(name, group)
		{
			CronExpressionString = cronExpression;
			StartTime = DateTime.Now;
			TimeZone = TimeZone.CurrentTimeZone;
		}


		/// <summary>
		/// <p>
		/// Create a <code>CronTrigger</code> with the given name and group, and
		/// associated with the identified <code>{@link org.quartz.JobDetail}</code>.
		/// </p>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		public CronTrigger(String name, string group, string jobName,
		                   string jobGroup) : base(name, group, jobName, jobGroup)
		{
			StartTime = DateTime.Now;
			TimeZone = TimeZone.CurrentTimeZone;
		}

		/// <summary>
		/// Create a <code>CronTrigger</code> with the given name and group,
		/// associated with the identified <code>{@link org.quartz.JobDetail}</code>,
		/// and with the given "cron" expression.
		/// <p>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set the the system's default time zone.
		/// </p>
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="cronExpression">The cron expression.</param>
		public CronTrigger(string name, string group, string jobName,
		                   string jobGroup, string cronExpression)
			: this(name, group, jobName, jobGroup, DateTime.Now, null, cronExpression, TimeZone.CurrentTimeZone)
		{
		}

		/// <summary>
		/// Create a <code>CronTrigger</code> with the given name and group,
		/// associated with the identified <code>{@link org.quartz.JobDetail}</code>,
		/// and with the given "cron" expression resolved with respect to the <code>TimeZone</code>.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="cronExpression">The cron expression.</param>
		/// <param name="timeZone">The time zone.</param>
		public CronTrigger(string name, string group, string jobName,
		                   string jobGroup, string cronExpression, TimeZone timeZone)
			: this(name, group, jobName, jobGroup, DateTime.Now, null, cronExpression,
			       timeZone)
		{
		}


		/// <summary>
		/// Create a <code>CronTrigger</code> that will occur at the given time,
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
		                   string jobGroup, DateTime startTime, NullableDateTime endTime, string cronExpression)
			: base(name, group, jobName, jobGroup)
		{
			CronExpressionString = cronExpression;

			if (startTime == DateTime.MinValue)
			{
				startTime = DateTime.Now;
			}
			StartTime = startTime;
			if (endTime != null && endTime.HasValue)
			{
				EndTime = endTime;
			}
			TimeZone = TimeZone.CurrentTimeZone;
		}


		/// <summary>
		/// Create a <code>CronTrigger</code> with fire time dictated by the
		/// <code>cronExpression</code> resolved with respect to the specified
		/// <code>timeZone</code> occuring from the <code>startTime</code> until
		/// the given <code>endTime</code>.
		/// </summary>
		/// <param name="name">The name.</param>
		/// <param name="group">The group.</param>
		/// <param name="jobName">Name of the job.</param>
		/// <param name="jobGroup">The job group.</param>
		/// <param name="startTime">The start time.</param>
		/// <param name="endTime">The end time.</param>
		/// <param name="cronExpression">The cron expression.</param>
		/// <param name="timeZone">The time zone.</param>
		public CronTrigger(string name, string group, string jobName,
		                   string jobGroup, DateTime startTime, NullableDateTime endTime,
		                   string cronExpression, TimeZone timeZone) : base(name, group, jobName, jobGroup)
		{
			CronExpressionString = cronExpression;

			if (startTime == DateTime.MinValue)
			{
				startTime = DateTime.Now;
			}
			StartTime = startTime;
			if (endTime != null && endTime.HasValue)
			{
				EndTime = endTime;
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
			copy.CronExpression = (CronExpression) cronEx.Clone();
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
				cronEx.SetTimeZone(TimeZone);
			}
			get { return cronEx == null ? null : cronEx.getCronExpression(); }
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
				timeZone = value.GetTimeZone();
			}
		}


		/// <summary>
		/// 	<p>
		/// Returns the next time at which the <code>Trigger</code> will fire. If
		/// the trigger will not fire again, <code>null</code> will be returned.
		/// The value returned is not guaranteed to be valid until after the <code>Trigger</code>
		/// has been added to the scheduler.
		/// </p>
		/// </summary>
		/// <returns></returns>
		public override NullableDateTime GetNextFireTime()
		{
			return nextFireTime;
		}

		/// <summary>
		/// Returns the previous time at which the <code>Trigger</code> will fire.
		/// If the trigger has not yet fired, <code>null</code> will be returned.
		/// </summary>
		/// <returns></returns>
		public override NullableDateTime GetPreviousFireTime()
		{
			return previousFireTime;
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
			nextFireTime = fireTime;
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
			previousFireTime = fireTime;
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
					return cronEx.GetTimeZone();
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
					cronEx.SetTimeZone(value);
				}
				timeZone = value;
			}
		}


		/// <summary>
		/// Returns the next time at which the <code>Trigger</code> will fire,
		/// after the given time. If the trigger will not fire after the given time,
		/// <code>null</code> will be returned.
		/// </summary>
		/// <param name="afterTime"></param>
		/// <returns></returns>
		public override NullableDateTime GetFireTimeAfter(NullableDateTime afterTime)
		{
			if (afterTime == null)
			{
				afterTime = DateTime.Now;
			}

			if (StartTime > afterTime.Value)
			{
				afterTime = startTime.AddMilliseconds(1000L);
			}

			NullableDateTime pot = GetTimeAfter(afterTime.Value);
			if (EndTime.HasValue && pot != null && pot.Value > EndTime.Value)
			{
				return NullableDateTime.Default;
			}

			return pot;
		}

		/// <summary>
		/// Returns the last time at which the <code>Trigger</code> will fire, if
		/// the Trigger will repeat indefinitely, null will be returned.
		/// <p>
		/// Note that the return time *may* be in the past.
		/// </p>
		/// </summary>
		public override NullableDateTime FinalFireTime
		{
			get
			{
				if (EndTime.HasValue)
				{
					return GetTimeBefore(EndTime);
				}
				else
				{
					return null;
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
			get { return false; }
		}

		/// <summary>
		/// Used by the <code>Scheduler</code> to determine whether or not
		/// it is possible for this <code>Trigger</code> to fire again.
		/// <p>
		/// If the returned value is <code>false</code> then the <code>Scheduler</code>
		/// may remove the <code>Trigger</code> from the <code>JobStore</code>.
		/// </p>
		/// </summary>
		/// <returns></returns>
		public override bool MayFireAgain()
		{
			return (GetNextFireTime() != null);
		}

		/// <summary>
		/// Validates the misfire instruction.
		/// </summary>
		/// <param name="misfireInstruction">The misfire instruction.</param>
		/// <returns></returns>
		protected override bool ValidateMisfireInstruction(int misfireInstruction)
		{
			if (misfireInstruction < MISFIRE_INSTRUCTION_SMART_POLICY)
			{
				return false;
			}

			if (misfireInstruction > MISFIRE_INSTRUCTION_DO_NOTHING)
			{
				return false;
			}

			return true;
		}


		/// <summary>
		/// This method should not be used by the Quartz client.
		/// <p>
		/// To be implemented by the concrete classes that extend this class.
		/// </p>
		/// <p>
		/// The implementation should update the <code>Trigger</code>'s state
		/// based on the MISFIRE_INSTRUCTION_XXX that was selected when the <code>Trigger</code>
		/// was created.
		/// </p>
		/// </summary>
		/// <param name="cal"></param>
		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instr = MisfireInstruction;

			if (instr == MISFIRE_INSTRUCTION_SMART_POLICY)
			{
				instr = MISFIRE_INSTRUCTION_FIRE_ONCE_NOW;
			}

			if (instr == MISFIRE_INSTRUCTION_DO_NOTHING)
			{
				NullableDateTime newFireTime = GetFireTimeAfter(DateTime.Now);
				while (newFireTime != null && cal != null
				       && !cal.IsTimeIncluded(newFireTime.Value))
				{
					newFireTime = GetFireTimeAfter(newFireTime);
				}
				SetNextFireTime(newFireTime);
			}
			else if (instr == MISFIRE_INSTRUCTION_FIRE_ONCE_NOW)
			{
				SetNextFireTime(DateTime.Now);
			}
		}


		/// <summary>
		/// <p>
		/// Determines whether the date and (optionally) time of the given Calendar 
		/// instance falls on a scheduled fire-time of this trigger.
		/// </p>
		/// 
		/// <p>
		/// Equivalent to calling <code>willFireOn(cal, false)</code>.
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

			NullableDateTime fta = GetFireTimeAfter(test.AddMilliseconds(-1*1000));
			DateTime p = fta.Value;

			if (dayOnly)
			{
				return (p.Year == test.Year
				        && p.Month == test.Month
				        && p.Day == test.Day);
			}

			while (fta.Value < (test))
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
		/// <p>
		/// Called after the <code>{@link Scheduler}</code> has executed the
		/// <code>{@link org.quartz.JobDetail}</code> associated with the <code>Trigger</code>
		/// in order to get the final instruction code from the trigger.
		/// </p>
		/// </summary>
		/// <param name="context">is the <code>JobExecutionContext</code> that was used by the
		/// <code>Job</code>'s<code>Execute(xx)</code> method.</param>
		/// <param name="result">is the <code>JobExecutionException</code> thrown by the
		/// <code>Job</code>, if any (may be null).</param>
		/// <returns>
		/// one of the Trigger.INSTRUCTION_XXX constants.
		/// </returns>
		/// <seealso cref="Trigger.INSTRUCTION_NOOP" />
		/// <seealso cref="Trigger.INSTRUCTION_RE_EXECUTE_JOB" />
		/// <seealso cref="Trigger.INSTRUCTION_DELETE_TRIGGER" />
		/// <seealso cref="Trigger.INSTRUCTION_SET_TRIGGER_COMPLETE" />
		/// <seealso cref="Triggered" />
		public override int ExecutionComplete(JobExecutionContext context,
		                                      JobExecutionException result)
		{
			if (result != null && result.RefireImmediately)
			{
				return INSTRUCTION_RE_EXECUTE_JOB;
			}

			if (result != null && result.UnscheduleFiringTrigger)
			{
				return INSTRUCTION_SET_TRIGGER_COMPLETE;
			}

			if (result != null && result.UnscheduleAllTriggers)
			{
				return INSTRUCTION_SET_ALL_JOB_TRIGGERS_COMPLETE;
			}

			if (!MayFireAgain())
			{
				return INSTRUCTION_DELETE_TRIGGER;
			}

			return INSTRUCTION_NOOP;
		}

		/// <summary>
		/// Called when the <code>{@link Scheduler}</code> has decided to 'fire'
		/// the trigger (Execute the associated <code>Job</code>), in order to
		/// give the <code>Trigger</code> a chance to update itself for its next
		/// triggering (if any).
		/// </summary>
		/// <param name="cal"></param>
		/// <seealso cref="JobExecutionException" />
		public override void Triggered(ICalendar cal)
		{
			previousFireTime = nextFireTime;
			nextFireTime = GetFireTimeAfter(nextFireTime);

			while (nextFireTime != null && cal != null
			       && !cal.IsTimeIncluded(nextFireTime.Value))
			{
				nextFireTime = GetFireTimeAfter(nextFireTime);
			}
		}

		/// <summary>
		/// Updates the trigger with new calendar.
		/// </summary>
		/// <param name="calendar">The calendar to update with.</param>
		/// <param name="misfireThreshold">The misfire threshold.</param>
		public override void UpdateWithNewCalendar(ICalendar calendar, long misfireThreshold)
		{
			nextFireTime = GetFireTimeAfter(previousFireTime);

			DateTime now = DateTime.Now;
			do
			{
				while (nextFireTime != null && calendar != null
				       && !calendar.IsTimeIncluded(nextFireTime.Value))
				{
					nextFireTime = GetFireTimeAfter(nextFireTime);
				}

				if (nextFireTime != null && nextFireTime.Value < (now))
				{
					long diff = (long) (now - nextFireTime.Value).TotalMilliseconds;
					if (diff >= misfireThreshold)
					{
						nextFireTime = GetFireTimeAfter(nextFireTime);
						continue;
					}
				}
			} while (false);
		}


		/// <summary>
		/// Called by the scheduler at the time a <code>Trigger</code> is first
		/// added to the scheduler, in order to have the <code>Trigger</code>
		/// compute its first fire time, based on any associated calendar.
		/// <p>
		/// After this method has been called, <code>getNextFireTime()</code>
		/// should return a valid answer.
		/// </p>
		/// </summary>
		/// <param name="cal"></param>
		/// <returns>
		/// the first time at which the <code>Trigger</code> will be fired
		/// by the scheduler, which is also the same value <code>getNextFireTime()</code>
		/// will return (until after the first firing of the <code>Trigger</code>).
		/// </returns>
		public override NullableDateTime ComputeFirstFireTime(ICalendar cal)
		{
			nextFireTime = GetFireTimeAfter(startTime.AddMilliseconds(1000));

			while (nextFireTime != null && nextFireTime.HasValue && cal != null
			       && !cal.IsTimeIncluded(nextFireTime.Value))
			{
				nextFireTime = GetFireTimeAfter(nextFireTime);
			}

			return nextFireTime;
		}

		/// <summary>
		/// Gets the expression summary.
		/// </summary>
		/// <returns></returns>
		public string GetExpressionSummary()
		{
			return cronEx == null ? null : cronEx.getExpressionSummary();
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
			return cronEx.GetTimeAfter(afterTime);
		}

		/// <summary>
		/// Gets the time before.
		/// </summary>
		/// <param name="date">The date.</param>
		/// <returns></returns>
		protected NullableDateTime GetTimeBefore(NullableDateTime date)
		{
			return null;
		}
	}
}