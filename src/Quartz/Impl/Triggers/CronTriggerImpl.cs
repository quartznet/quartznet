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
using Quartz.Util;

namespace Quartz.Impl.Triggers
{
	/// <summary>
	/// A concrete <see cref="ITrigger" /> that is used to fire a <see cref="IJobDetail" />
	/// at given moments in time, defined with Unix 'cron-like' definitions.
	/// </summary>
	/// <remarks>
	/// <para>
	/// For those unfamiliar with "cron", this means being able to create a firing
	/// schedule such as: "At 8:00am every Monday through Friday" or "At 1:30am
	/// every last Friday of the month".
	/// </para>
	/// 
	/// <para>
	/// The format of a "Cron-Expression" string is documented on the 
	/// <see cref="CronExpression" /> class.
	/// </para>
	/// 
	/// <para>
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
	/// <td align="left">Fire at 10:15am on every last Friday of every month during the years 2002, 2003, 2004 and 2005" />
	/// </td>
	/// </tr>
	/// <tr>
	/// <td align="left">"0 15 10 ? * 6#3"" /></td>
	/// <td align="left"> </td>
	/// <td align="left">Fire at 10:15am on the third Friday of every month" />
	/// </td>
	/// </tr>
	/// </table>
	/// </para>
	/// 
	/// <para>
	/// Pay attention to the effects of '?' and '*' in the day-of-week and
	/// day-of-month fields!
	/// </para>
	/// 
	/// <para>
	/// <b>NOTES:</b>
	/// <ul>
	/// <li>Support for specifying both a day-of-week and a day-of-month value is
	/// not complete (you'll need to use the '?' character in on of these fields).
	/// </li>
	/// <li>Be careful when setting fire times between mid-night and 1:00 AM -
	/// "daylight savings" can cause a skip or a repeat depending on whether the
	/// time moves back or jumps forward.</li>
	/// </ul>
	/// </para>
	/// </remarks>
	/// <seealso cref="ITrigger"/>
	/// <seealso cref="ISimpleTrigger"/>
	/// <author>Sharada Jambula</author>
	/// <author>James House</author>
	/// <author>Contributions from Mads Henderson</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class CronTriggerImpl : AbstractTrigger, ICronTrigger
	{
        protected const int YearToGiveupSchedulingAt = 2299;
		private CronExpression cronEx;
        private DateTimeOffset startTimeUtc = DateTimeOffset.MinValue;
        private DateTimeOffset? endTimeUtc;
        private DateTimeOffset? nextFireTimeUtc;
        private DateTimeOffset? previousFireTimeUtc;
        [NonSerialized] private TimeZoneInfo timeZone;

		/// <summary>
        /// Create a <see cref="CronTriggerImpl" /> with no settings.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set to the system's default time zone.
        /// </remarks>
		public CronTriggerImpl()
		{
			StartTimeUtc = SystemTime.UtcNow();
            TimeZone = TimeZoneInfo.Local;
		}

        /// <summary>
        /// Create a <see cref="CronTriggerImpl" /> with the given name and default group.
        /// </summary>
        /// <remarks>
        /// The start-time will also be set to the current time, and the time zone
        /// will be set to the system's default time zone.
        /// </remarks>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        public CronTriggerImpl(string name) : this(name, null)
        {
        }

		/// <summary>
		/// Create a <see cref="CronTriggerImpl" /> with the given name and group.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set to the system's default time zone.
        /// </remarks>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
		public CronTriggerImpl(string name, string group) : base(name, group)
		{
			StartTimeUtc = SystemTime.UtcNow();
            TimeZone = TimeZoneInfo.Local;
        }


		/// <summary>
		/// Create a <see cref="CronTriggerImpl" /> with the given name, group and
		/// expression.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set to the system's default time zone.
        /// </remarks>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="cronExpression"> A cron expression dictating the firing sequence of the <see cref="ITrigger" /></param>
		public CronTriggerImpl(string name, string group, string cronExpression) : base(name, group)
		{
			CronExpressionString = cronExpression;
			StartTimeUtc = SystemTime.UtcNow();
            TimeZone = TimeZoneInfo.Local;
        }


		/// <summary>
		/// Create a <see cref="CronTriggerImpl" /> with the given name and group, and
		/// associated with the identified <see cref="IJobDetail" />.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set to the system's default time zone.
		/// </remarks>
        /// <param name="name">The name of the <see cref="ITrigger" />.</param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
		public CronTriggerImpl(string name, string group, string jobName,
			string jobGroup) : base(name, group, jobName, jobGroup)
		{
			StartTimeUtc = SystemTime.UtcNow();
            TimeZone = TimeZoneInfo.Local;
        }

		/// <summary>
		/// Create a <see cref="ICronTrigger" /> with the given name and group,
		/// associated with the identified <see cref="IJobDetail" />,
		/// and with the given "cron" expression.
		/// </summary>
		/// <remarks>
		/// The start-time will also be set to the current time, and the time zone
		/// will be set to the system's default time zone.
        /// </remarks>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="cronExpression"> A cron expression dictating the firing sequence of the <see cref="ITrigger" /></param>
		public CronTriggerImpl(string name, string group, string jobName,
			string jobGroup, string cronExpression)
			: this(name, group, jobName, jobGroup, SystemTime.UtcNow(), null, cronExpression, TimeZoneInfo.Local)
		{
		}

		/// <summary>
		/// Create a <see cref="ICronTrigger" /> with the given name and group,
		/// associated with the identified <see cref="IJobDetail" />,
		/// and with the given "cron" expression resolved with respect to the <see cref="TimeZone" />.
		/// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="cronExpression"> A cron expression dictating the firing sequence of the <see cref="ITrigger" /></param>
        /// <param name="timeZone">
        /// Specifies for which time zone the cronExpression should be interpreted, 
        /// i.e. the expression 0 0 10 * * ?, is resolved to 10:00 am in this time zone.
        /// </param>
		public CronTriggerImpl(string name, string group, string jobName,
			string jobGroup, string cronExpression, TimeZoneInfo timeZone)
			: this(name, group, jobName, jobGroup, SystemTime.UtcNow(), null, cronExpression,
			timeZone)
		{
		}


		/// <summary>
		/// Create a <see cref="ICronTrigger" /> that will occur at the given time,
		/// until the given end time.
		/// <para>
		/// If null, the start-time will also be set to the current time, the time
		/// zone will be set to the system's default.
		/// </para>
		/// </summary>
        /// <param name="name">The name of the <see cref="ITrigger" /></param>
        /// <param name="group">The group of the <see cref="ITrigger" /></param>
        /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
        /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the earliest time for the  <see cref="ITrigger" /> to start firing.</param>
        /// <param name="endTime">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to quit repeat firing.</param>
        /// <param name="cronExpression"> A cron expression dictating the firing sequence of the <see cref="ITrigger" /></param>
		public CronTriggerImpl(string name, string group, string jobName,
			string jobGroup, DateTimeOffset startTimeUtc, 
            DateTimeOffset? endTime, 
            string cronExpression)
			: base(name, group, jobName, jobGroup)
		{
			CronExpressionString = cronExpression;

            if (startTimeUtc == DateTimeOffset.MinValue)
			{
                startTimeUtc = SystemTime.UtcNow();
			}
			StartTimeUtc = startTimeUtc;
			if (endTime.HasValue)
			{
				EndTimeUtc = endTime;
			}
            TimeZone = TimeZoneInfo.Local;
        }

	    /// <summary>
	    /// Create a <see cref="CronTriggerImpl" /> with fire time dictated by the
	    /// <paramref name="cronExpression" /> resolved with respect to the specified
	    /// <paramref name="timeZone" /> occurring from the <see cref="startTimeUtc" /> until
	    /// the given <paramref name="endTime" />.
	    /// </summary>
	    /// <param name="name">The name of the <see cref="ITrigger" /></param>
	    /// <param name="group">The group of the <see cref="ITrigger" /></param>
	    /// <param name="jobName">name of the <see cref="IJobDetail" /> executed on firetime</param>
	    /// <param name="jobGroup">Group of the <see cref="IJobDetail" /> executed on firetime</param>
	    /// <param name="startTimeUtc">A <see cref="DateTimeOffset" /> set to the earliest time for the <see cref="ITrigger" /> to start firing.</param>
        /// <param name="endTime">A <see cref="DateTimeOffset" /> set to the time for the <see cref="ITrigger" /> to quit repeat firing.</param>
        /// <param name="cronExpression"> A cron expression dictating the firing sequence of the <see cref="ITrigger" /></param>
        /// <param name="timeZone">
        /// Specifies for which time zone the cronExpression should be interpreted, 
        /// i.e. the expression 0 0 10 * * ?, is resolved to 10:00 am in this time zone.
        /// </param>
		public CronTriggerImpl(string name, string group, string jobName,
            string jobGroup, DateTimeOffset startTimeUtc, 
            DateTimeOffset? endTime,
			string cronExpression, 
            TimeZoneInfo timeZone) : base(name, group, jobName, jobGroup)
		{
			CronExpressionString = cronExpression;

            if (startTimeUtc == DateTimeOffset.MinValue)
			{
                startTimeUtc = SystemTime.UtcNow();
			}
            StartTimeUtc = startTimeUtc;

			if (endTime.HasValue)
			{
				EndTimeUtc = endTime;
			}
			if (timeZone == null)
			{
                TimeZone = TimeZoneInfo.Local;
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
            CronTriggerImpl copy = (CronTriggerImpl) MemberwiseClone();
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
			    TimeZoneInfo originalTimeZone = TimeZone;
				cronEx = new CronExpression(value);
				cronEx.TimeZone = originalTimeZone;
			}
			get { return cronEx == null ? null : cronEx.CronExpressionString; }
		}

		/// <summary>
		/// Set the CronExpression to the given one.  The TimeZone on the passed-in
        /// CronExpression over-rides any that was already set on the Trigger.
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
        public override DateTimeOffset StartTimeUtc
		{
			get {  return startTimeUtc; }
			set
			{
                DateTimeOffset? eTime = EndTimeUtc;
				if (eTime.HasValue && eTime.Value < value)
				{
					throw new ArgumentException("End time cannot be before start time");
				}
        
				// round off millisecond...
                DateTimeOffset dt = new DateTimeOffset(value.Year, value.Month, value.Day, value.Hour, value.Minute, value.Second, value.Offset);
                startTimeUtc = dt;
			}
		}


		/// <summary>
		/// Get or sets the time at which the <c>CronTrigger</c> should quit
		/// repeating - even if repeatCount isn't yet satisfied. 
		/// </summary>
        public override DateTimeOffset? EndTimeUtc
		{
			get { return endTimeUtc; }
			set
			{
                DateTimeOffset sTime = StartTimeUtc;
				if (value.HasValue && sTime > value.Value)
				{
					throw new ArgumentException("End time cannot be before start time");
				}

				endTimeUtc = value;
			}
		}

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
        public override DateTimeOffset? GetNextFireTimeUtc()
        {
			return nextFireTimeUtc;
		}

	    /// <summary>
	    /// Returns the previous time at which the <see cref="ITrigger" /> fired.
	    /// If the trigger has not yet fired, <see langword="null" /> will be returned.
	    /// </summary>
	    /// <returns></returns>
	    public override DateTimeOffset? GetPreviousFireTimeUtc()
	    {
	        return previousFireTimeUtc;
	    }


	    /// <summary>
		/// Sets the next fire time.
		/// <para>
		/// <b>This method should not be invoked by client code.</b>
		/// </para>
		/// </summary>
		/// <param name="fireTime">The fire time.</param>
        public override void SetNextFireTimeUtc(DateTimeOffset? fireTime)
		{
			nextFireTimeUtc = fireTime;
		}


		/// <summary>
		/// Sets the previous fire time.
		/// <para>
		/// <b>This method should not be invoked by client code.</b>
		/// </para>
		/// </summary>
		/// <param name="fireTime">The fire time.</param>
        public override void SetPreviousFireTimeUtc(DateTimeOffset? fireTime)
		{
			previousFireTimeUtc = fireTime;
		}


		/// <summary>
        /// Sets the time zone for which the <see cref="ICronTrigger.CronExpressionString" /> of this
        /// <see cref="ICronTrigger" /> will be resolved.
        /// </summary>
        /// <remarks>
        /// If <see cref="ICronTrigger.CronExpressionString" /> is set after this
        /// property, the TimeZone setting on the CronExpression will "win".  However
        /// if <see cref="CronExpressionString" /> is set after this property, the
        /// time zone applied by this method will remain in effect, since the 
        /// string cron expression does not carry a time zone!
        /// </remarks>
		/// <value>The time zone.</value>
        public TimeZoneInfo TimeZone
		{
			get
			{
				if (cronEx != null)
				{
					return cronEx.TimeZone;
				}

				if (timeZone == null)
				{
                    timeZone = TimeZoneInfo.Local;
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
		/// Returns the next time at which the <see cref="ITrigger" /> will fire,
		/// after the given time. If the trigger will not fire after the given time,
		/// <see langword="null" /> will be returned.
		/// </summary>
		/// <param name="afterTimeUtc"></param>
        /// <returns></returns>
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTimeUtc)
		{
			if (!afterTimeUtc.HasValue)
			{
				afterTimeUtc = SystemTime.UtcNow();
			}

			if (StartTimeUtc > afterTimeUtc.Value)
			{
				afterTimeUtc = startTimeUtc.AddSeconds(-1);
			}

            if (EndTimeUtc.HasValue && (afterTimeUtc.Value.CompareTo(EndTimeUtc.Value) >= 0))
            {
                return null;
            }

            DateTimeOffset? pot = GetTimeAfter(afterTimeUtc.Value);
            if (EndTimeUtc.HasValue && pot.HasValue && pot.Value > EndTimeUtc.Value)
			{
				return null;
			}

			return pot;
		}

	    public override IScheduleBuilder GetScheduleBuilder()
	    {
            CronScheduleBuilder cb = CronScheduleBuilder.CronSchedule(CronExpressionString).InTimeZone(TimeZone);

            switch (MisfireInstruction)
            {
                case Quartz.MisfireInstruction.CronTrigger.DoNothing: cb.WithMisfireHandlingInstructionDoNothing();
                    break;
                case Quartz.MisfireInstruction.CronTrigger.FireOnceNow: cb.WithMisfireHandlingInstructionFireAndProceed();
                    break;
            }

            return cb;
	    }

	    /// <summary>
		/// Returns the last UTC time at which the <see cref="ITrigger" /> will fire, if
		/// the Trigger will repeat indefinitely, null will be returned.
		/// <para>
		/// Note that the return time *may* be in the past.
		/// </para>
        /// </summary>
        public override DateTimeOffset? FinalFireTimeUtc
		{
			get
            {
                DateTimeOffset? resultTime;
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
		/// it is possible for this <see cref="ITrigger" /> to fire again.
		/// <para>
		/// If the returned value is <see langword="false" /> then the <see cref="IScheduler" />
		/// may remove the <see cref="ITrigger" /> from the <see cref="IJobStore" />.
		/// </para>
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
            if (misfireInstruction < Quartz.MisfireInstruction.IgnoreMisfirePolicy)
            {
                return false;
            }

            if (misfireInstruction > Quartz.MisfireInstruction.CronTrigger.DoNothing)
            {
                return false;
            }

            return true;
		}


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
		/// <param name="cal"></param>
		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instr = MisfireInstruction;

			if (instr == Quartz.MisfireInstruction.SmartPolicy)
			{
				instr = Quartz.MisfireInstruction.CronTrigger.FireOnceNow;
			}

			if (instr == Quartz.MisfireInstruction.CronTrigger.DoNothing)
			{
                DateTimeOffset? newFireTime = GetFireTimeAfter(SystemTime.UtcNow());

                while (newFireTime.HasValue && cal != null
				       && !cal.IsTimeIncluded(newFireTime.Value))
				{
					newFireTime = GetFireTimeAfter(newFireTime);
				}
				SetNextFireTimeUtc(newFireTime);
			}
			else if (instr == Quartz.MisfireInstruction.CronTrigger.FireOnceNow)
			{
				SetNextFireTimeUtc(SystemTime.UtcNow());
			}
		}


		/// <summary>
		/// <para>
		/// Determines whether the date and (optionally) time of the given Calendar 
		/// instance falls on a scheduled fire-time of this trigger.
		/// </para>
		/// 
		/// <para>
		/// Equivalent to calling <see cref="WillFireOn(DateTimeOffset, bool)" />.
		/// </para>
		/// </summary>
		/// <param name="test">The date to compare.</param>
		/// <returns></returns>
		public bool WillFireOn(DateTimeOffset test)
		{
			return WillFireOn(test, false);
		}


		/// <summary>
		/// Determines whether the date and (optionally) time of the given Calendar 
		/// instance falls on a scheduled fire-time of this trigger.
		/// <para>
		/// Note that the value returned is NOT validated against the related
		/// ICalendar (if any).
		/// </para>
		/// </summary>
		/// <param name="test">The date to compare</param>
		/// <param name="dayOnly">If set to true, the method will only determine if the
		/// trigger will fire during the day represented by the given Calendar
		/// (hours, minutes and seconds will be ignored).</param>
		/// <returns></returns>
        public bool WillFireOn(DateTimeOffset test, bool dayOnly)
		{
			test = new DateTime(test.Year, test.Month, test.Day, test.Hour, test.Minute, test.Second);
			if (dayOnly)
			{
				test = new DateTime(test.Year, test.Month, test.Day, 0, 0, 0);
			}

            DateTimeOffset? fta = GetFireTimeAfter(test.AddMilliseconds(-1 * 1000));


            if (fta == null)
            {
                return false;
            }

            DateTimeOffset p = TimeZoneUtil.ConvertTime(fta.Value, TimeZone);

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
		/// give the <see cref="ITrigger" /> a chance to update itself for its next
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
		public override void UpdateWithNewCalendar(ICalendar calendar, TimeSpan misfireThreshold)
		{
			nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

            if (!nextFireTimeUtc.HasValue || calendar == null)
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

                // avoid infinite loop
                if (nextFireTimeUtc.Value.Year > YearToGiveupSchedulingAt)
                {
                    nextFireTimeUtc = null;
                }

			    if (nextFireTimeUtc.HasValue && nextFireTimeUtc.Value < (now))
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
		/// <param name="cal"></param>
		/// <returns>
		/// the first time at which the <see cref="ITrigger" /> will be fired
		/// by the scheduler, which is also the same value <see cref="GetNextFireTimeUtc" />
		/// will return (until after the first firing of the <see cref="ITrigger" />).
		/// </returns>
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar cal)
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
        protected DateTimeOffset? GetTimeAfter(DateTimeOffset afterTime)
		{
		    if (cronEx != null)
            {
                return cronEx.GetTimeAfter(afterTime);
            }
		    
            return null;
		}

	    /// <summary>
		/// NOT YET IMPLEMENTED: Returns the time before the given time
        /// that this <see cref="ICronTrigger" /> will fire.
		/// </summary>
		/// <param name="date">The date.</param>
		/// <returns></returns> 
        protected DateTimeOffset? GetTimeBefore(DateTimeOffset? date)
		{
            return (cronEx == null) ? null : cronEx.GetTimeBefore(endTimeUtc);
		}
	}
}
