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
using System;
using System.Globalization;

#if !NET_20
using Nullables;
#endif

using Quartz.Spi;

namespace Quartz
{
	/// <summary> 
	/// A trigger which fires on the N<sup>th</sup> day of every interval type 
    /// <see cref="INTERVAL_TYPE_WEEKLY" />, <see cref="INTERVAL_TYPE_MONTHLY" /> or 
    /// <see cref="INTERVAL_TYPE_YEARLY" /> that is <i>not</i> excluded by the associated
	/// calendar. 
	/// </summary>
	/// <remarks>
	/// When determining what the N<sup>th</sup> day of the month or year
	/// is, <see cref="NthIncludedDayTrigger" /> will skip excluded days on the 
	/// associated calendar. This would commonly be used in an N<sup>th</sup> 
	/// business day situation, in which the user wishes to fire a particular job on
	/// the N<sup>th</sup> business day (i.e. the 5<sup>th</sup> business day of
	/// every month). Each <see cref="NthIncludedDayTrigger" /> also has an associated
	/// <see cref="FireAtTime" /> which indicates at what time of day the trigger is
	/// to fire.
	/// <p>
	/// All <see cref="NthIncludedDayTrigger" />s default to a monthly interval type
	/// (fires on the N<SUP>th</SUP> day of every month) with N = 1 (first 
	/// non-excluded day) and <see cref="FireAtTime" /> set to 12:00 PM (noon). These
    /// values can be changed using the <see cref="N" />, <see cref="IntervalType" />, and
    /// <see cref="FireAtTime" /> methods. Users may also want to note the 
    /// <see cref="NextFireCutoffInterval" /> and <see cref="NextFireCutoffInterval" />
	/// methods.
	/// </p>
	/// <p>
	/// Take, for example, the following calendar:
	/// </p>
	/// <code>
	/// July                  August                September
	/// Su Mo Tu We Th Fr Sa   Su Mo Tu We Th Fr Sa   Su Mo Tu We Th Fr Sa
	/// 1  W       1  2  3  4  5  W                1  2  W
	/// W  H  5  6  7  8  W    W  8  9 10 11 12  W    W  H  6  7  8  9  W
	/// W 11 12 13 14 15  W    W 15 16 17 18 19  W    W 12 13 14 15 16  W
	/// W 18 19 20 21 22  W    W 22 23 24 25 26  W    W 19 20 21 22 23  W
	/// W 25 26 27 28 29  W    W 29 30 31             W 26 27 28 29 30
	/// W
	/// </code>
	/// Where W's represent weekend days, and H's represent holidays, all of which
	/// are excluded on a calendar associated with an 
	/// <see cref="NthIncludedDayTrigger" /> with n=5 and 
	///  intervalType=INTERVAL_TYPE_MONTHLY. In this case, the trigger 
	/// would fire on the 8<sup>th</sup> of July (because of the July 4 holiday), 
	/// the 5<sup>th</sup> of August, and the 8<sup>th</sup> of September (because 
	/// of Labor Day).
    /// </remarks>
	/// <author>Aaron Craven</author>
	[Serializable]
	public class NthIncludedDayTrigger : Trigger
	{

		/// <summary> 
		/// Indicates a monthly trigger type (fires on the N<SUP>th</SUP> included
		/// day of every month).
		/// </summary>
		public const int INTERVAL_TYPE_MONTHLY = 1;

		/// <summary> indicates a yearly trigger type (fires on the N<SUP>th</SUP> included 
		/// day of every year).
		/// </summary>
		public const int INTERVAL_TYPE_YEARLY = 2;

		/// <summary>
		/// Indicates a weekly trigger type (fires on the N<SUP>th</SUP> included
		/// day of every week). When using this interval type, care must be taken
		/// not to think of the value of <see cref="n" /> as an analog to 
		/// <see cref="DateTime.DayOfWeek" />. Such a comparison can only
		/// be drawn when there are no calendars associated with the trigger. To 
		/// illustrate, consider an <see cref="NthIncludedDayTrigger" /> with 
		/// n = 3 which is associated with a Calendar excluding
		/// non-weekdays. The trigger would fire on the 3<SUP>rd</SUP> 
		/// <I>included</I> day of the week, which would be 4<SUP>th</SUP> 
		/// <I>actual</I> day of the week.
		/// </summary>
		public const int INTERVAL_TYPE_WEEKLY = 3;

#if !NET_20
        private NullableDateTime previousFireTime;
		private NullableDateTime nextFireTime;
#else
        private DateTime? previousFireTime;
        private DateTime? nextFireTime;
#endif  
        private ICalendar calendar;

		private int n = 1;
		private int intervalType = INTERVAL_TYPE_MONTHLY;
		private int fireAtHour = 12;
		private int fireAtMinute = 0;
		private int nextFireCutoffInterval = 12;
		private int fireAtSecond = 0;
		private DayOfWeek triggerCalendarFirstDayOfWeek = DayOfWeek.Sunday;
		private CalendarWeekRule triggerCalendarWeekRule = CalendarWeekRule.FirstFourDayWeek;

		/// <summary> 
		/// Gets or sets the day of the interval on which the 
		/// <see cref="NthIncludedDayTrigger" /> should fire. If the N<SUP>th</SUP>
		/// day of the interval does not exist (i.e. the 32<SUP>nd</SUP> of a 
		/// month), the trigger simply will never fire. N may not be less than 1.
		/// </summary>
		public virtual int N
		{
			get { return n; }
			set
			{
				if (value > 0)
				{
					n = value;
				}
				else
				{
					throw new ArgumentException("N must be greater than 0.");
				}
			}
		}

		/// <summary> 
		/// Returns the interval type for the <see cref="NthIncludedDayTrigger" />.
		/// 
		/// Sets the interval type for the <see cref="NthIncludedDayTrigger" />. If
		/// {@link #INTERVAL_TYPE_MONTHLY}, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every month. If 
		/// {@link #INTERVAL_TYPE_YEARLY}, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every year. If 
		/// {@link #INTERVAL_TYPE_WEEKLY}, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every week. 
		/// </summary>
		/// <seealso cref="INTERVAL_TYPE_WEEKLY">
		/// </seealso>
		/// <seealso cref="INTERVAL_TYPE_MONTHLY">
		/// </seealso>
		/// <seealso cref="INTERVAL_TYPE_YEARLY">
		/// </seealso>
		public virtual int IntervalType
		{
			get { return intervalType; }

			set
			{
				switch (value)
				{
					case INTERVAL_TYPE_WEEKLY:
						intervalType = value;
						break;

					case INTERVAL_TYPE_MONTHLY:
						intervalType = value;
						break;

					case INTERVAL_TYPE_YEARLY:
						intervalType = value;
						break;

					default:
						throw new ArgumentException("Invalid Interval Type:" + value);
				}
			}
		}

		/// <summary>
		/// Sets or gets the fire time for the <CODE>NthIncludedDayTrigger</CODE>, which
		/// should be represented as a string with the format 
		/// &quot;HH:MM[:SS]&quot;, with HH representing the 24-hour clock hour
		/// of the fire time. Hours can be represented as either a one-digit or 
		/// two-digit number. Seconds are optional.
		/// </summary>
		public virtual string FireAtTime
		{
			get { return string.Format("{0:00}:{1:00}:{2:00}", fireAtHour, fireAtMinute, fireAtSecond); }

			set
			{
				try
				{
					string[] components = value.Split(':');

					int newFireHour = Int32.Parse(components[0]);
					if (components[1].Length != 2)
					{
						// minutes must be in two digit format
						throw new Exception();
					}
					int newFireMinute = Int32.Parse(components[1]);
					int newFireSecond = 0;
					if (components.Length == 3)
					{
						if (components[2].Length != 2)
						{
							// seconds must be in two digit format
							throw new Exception();
						}
						newFireSecond = Convert.ToInt32(components[2]);
					}

					
					// Check ranges
					if ((newFireHour < 0) || (newFireHour > 23)) 
					{
						throw new ArgumentException(
							string.Format("Could not parse time expression '{0}':fireAtHour must be between 0 and 23", value));
					} 
					else if ((newFireMinute < 0) || (newFireMinute > 59)) 
					{
						throw new ArgumentException(
							string.Format("Could not parse time expression '{0}':fireAtMinute must be between 0 and 59", value));
					} 
					else if ((newFireSecond < 0) || (newFireSecond > 59)) 
					{
						throw new ArgumentException(
							string.Format("Could not parse time expression '{0}':fireAtMinute must be between 0 and 59", value));
					}

					fireAtHour = newFireHour;
					fireAtMinute = newFireMinute;
					fireAtSecond = newFireSecond;
				}
				catch (Exception e)
				{
					throw new ArgumentException("Could not parse time expression: " + e.Message);
				}
			}
		}

		/// <summary> 
		/// Returns the <see cref="nextFireCutoffInterval" /> for the 
		/// <see cref="NthIncludedDayTrigger" />.
		/// </summary>
		/// <remarks>
		/// <p>
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// {@link #setNextFireCutoffInterval(int)} and 
		/// {@link #getNextFireCutoffInterval()} methods. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType"/> intervalType" />.
		/// </p>
		/// <p>
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// <see cref="NextFireCutoffInterval" /> method. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType" /> intervalType".
		/// </p>
		/// <p>
		/// In most cases, the default value of this setting (12) is sufficient (it
		/// is highly unlikely, for example, that you will need to look at more than
		/// 12 months of dates to ensure that your trigger will never fire again).  
		/// However, this setting is included to allow for the rare exceptions where
		/// this might not be true.
		/// </p>
		/// <p>
		/// For example, if your trigger is associated with a calendar that excludes
		/// a great many dates in the next 12 months, and hardly any following that,
		/// it is possible (if <see cref="N" /> is large enough) that you could run 
		/// into this situation.  
		/// </p>
		/// </remarks>
		public virtual int NextFireCutoffInterval
		{
			get { return nextFireCutoffInterval; }
			set { nextFireCutoffInterval = value; }
		}


		/// <summary>
		/// Returns the last time the <see cref="NthIncludedDayTrigger" /> will fire.
		/// If the trigger will not fire at any point between <see name="startTime" />
		/// and <see name="endTime" />, <see langword="null" /> will be returned.
		/// </summary>
		/// <returns> the last time the trigger will fire.
		/// </returns>
#if !NET_20
        public override NullableDateTime FinalFireTime
#else
        public override DateTime? FinalFireTime
#endif
		{
			get
			{
#if !NET_20
                NullableDateTime finalTime = null;
				NullableDateTime currCal = new NullableDateTime(EndTime.Value);
#else
                DateTime? finalTime = null;
                DateTime? currCal = EndTime.Value;
#endif

                while (!finalTime.HasValue && StartTime < currCal.Value)
				{
					currCal = currCal.Value.AddDays(-1);
					finalTime = GetFireTimeAfter(currCal);
				}

				return finalTime;
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
		/// Create an <see cref="NthIncludedDayTrigger" /> with no specified name,
		/// group, or <see cref="JobDetail" />. This will result initially in a
		/// default monthly trigger that fires on the first day of every month at
		/// 12:00 PM (n = 1, 
		/// intervalType={@link #INTERVAL_TYPE_MONTHLY" />, 
		/// fireAtTime="12:00").
		/// <p>
        /// Note that <see cref="Trigger.Name" />, <see cref="Trigger.Group" />, 
        /// <see cref="Trigger.JobName" />, and <see cref="Trigger.JobGroup" />, must be 
		/// called before the <see cref="NthIncludedDayTrigger" /> can be placed into
		/// a <see cref="IScheduler" />.
		/// </p>
		/// </summary>
		public NthIncludedDayTrigger()
		{
		}

		/// <summary> 
		/// Create an <see cref="NthIncludedDayTrigger" /> with the given name and
		/// group but no specified <see cref="JobDetail" />. This will result 
		/// initially in a default monthly trigger that fires on the first day of 
		/// every month at 12:00 PM (<see cref="n" />=1, 
		/// intervalType={@link #INTERVAL_TYPE_MONTHLY" />, 
		/// fireAtTime=12:00").
		/// <p>
        /// Note that <see cref="Trigger.JobName" /> and <see cref="Trigger.JobGroup" /> must
		/// be called before the <see cref="NthIncludedDayTrigger" /> can be placed 
		/// into a <see cref="IScheduler" />.
		/// </p>
		/// </summary>
		/// <param name="name"> the name for the <see cref="NthIncludedDayTrigger" />
		/// </param>
		/// <param name="group">the group for the <see cref="NthIncludedDayTrigger" />
		/// </param>
		public NthIncludedDayTrigger(string name, string group) : base(name, group)
		{
		}

		/// <summary> 
		/// Create an <see cref="NthIncludedDayTrigger" /> with the given name and
		/// group and the specified <see cref="JobDetail" />. This will result 
		/// initially in a default monthly trigger that fires on the first day of
		/// every month at 12:00 PM (<see cref="n" />=1, 
		/// intervalType={@link #INTERVAL_TYPE_MONTHLY" />, 
		/// fireAtTime="12:00").
		/// </summary>
		/// <param name="name">The name for the <see cref="NthIncludedDayTrigger" />.</param>
		/// <param name="group">The group for the <see cref="NthIncludedDayTrigger" />.</param>
		/// <param name="jobName">The name of the job to associate with the <see cref="NthIncludedDayTrigger" />.</param>
		/// <param name="jobGroup">The group containing the job to associate with the <see cref="NthIncludedDayTrigger" />.</param>
		public NthIncludedDayTrigger(string name, string group, string jobName, string jobGroup)
			: base(name, group, jobName, jobGroup)
		{
		}

		/// <summary> 
		/// Returns the next time at which the <see cref="NthIncludedDayTrigger" />
		/// will fire. If the trigger will not fire again, <see langword="null" /> will be
		/// returned. 
		/// <p>
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// {@link #setNextFireCutoffInterval(int)} and 
		/// {@link #getNextFireCutoffInterval()} methods. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType" /> intervalType.
		/// </p>
		/// <p>
		/// The returned value is not guaranteed to be valid until after
		/// the trigger has been added to the scheduler.
		/// </p>
		/// </summary>
		/// <returns> the next fire time for the trigger
		/// </returns>
		/// <seealso cref="NextFireCutoffInterval" /> 
#if !NET_20
        public override NullableDateTime GetNextFireTime()
#else
        public override DateTime? GetNextFireTime()
#endif
		{
			return nextFireTime;
		}

		/// <summary> Returns the previous time at which the 
		/// <see cref="NthIncludedDayTrigger" /> fired. If the trigger has not yet 
		/// fired, <see langword="null" /> will be returned.
		/// 
		/// </summary>
		/// <returns> the previous fire time for the trigger
		/// </returns>
#if !NET_20
        public override NullableDateTime GetPreviousFireTime()
#else
        public override DateTime? GetPreviousFireTime()
#endif
		{
			return previousFireTime;
		}

		/// <summary>
		/// Returns the first time the <see cref="NthIncludedDayTrigger" /> will fire
		/// after the specified date. 
		/// <P> 
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// {@link #setNextFireCutoffInterval(int)} and 
		/// {@link #getNextFireCutoffInterval()} methods. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType" /> intervalType.
		/// </P>
		/// <P>
		/// Therefore, for triggers with intervalType = 
		/// {@link NthIncludedDayTrigger#INTERVAL_TYPE_WEEKLY 
		/// INTERVAL_TYPE_WEEKLY" />, if the trigger will not fire within 12
		/// weeks after the given date/time, <see langword="null" /> will be returned. For
		/// triggers with intervalType = 
		/// {@link NthIncludedDayTrigger#INTERVAL_TYPE_MONTHLY
		/// INTERVAL_TYPE_MONTHLY" />, if the trigger will not fire within 12 
		/// months after the given date/time, <see langword="null" /> will be returned. 
		/// For triggers with intervalType = 
		/// {@link NthIncludedDayTrigger#INTERVAL_TYPE_YEARLY 
		/// INTERVAL_TYPE_YEARLY" />, if the trigger will not fire within 12
		/// years after the given date/time, <see langword="null" /> will be returned.  In 
		/// all cases, if the trigger will not fire before <see field="endTime" />, 
		/// <see langword="null" /> will be returned.
		/// </P>
		/// </summary>
		/// <param name="afterTime">The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified
		/// date
		/// </returns>
#if !NET_20
        public override NullableDateTime GetFireTimeAfter(NullableDateTime afterTime)
#else
        public override DateTime? GetFireTimeAfter(DateTime? afterTime)
#endif
		{
			if (!afterTime.HasValue)
			{
				afterTime = DateTime.Now;
			}

			if ((afterTime.Value < StartTime))
			{
				afterTime = StartTime.AddMilliseconds(-1*1000);
			}

			if (intervalType == INTERVAL_TYPE_WEEKLY)
			{
				return GetWeeklyFireTimeAfter(afterTime);
			}
			else if (intervalType == INTERVAL_TYPE_MONTHLY)
			{
				return GetMonthlyFireTimeAfter(afterTime.Value);
			}
			else if (intervalType == INTERVAL_TYPE_YEARLY)
			{
				return GetYearlyFireTimeAfter(afterTime.Value);
			}
			else
			{
				return null;
			}
		}

		/// <summary>
		/// Called when the <see cref="IScheduler" /> has decided to 'fire' the trigger
		/// (Execute the associated <see cref="IJob" />), in order to give the 
		/// <see cref="Trigger" /> a chance to update itself for its next triggering 
		/// (if any).
		/// </summary>
		public override void Triggered(ICalendar cal)
		{
			calendar = cal;
			previousFireTime = nextFireTime;
			nextFireTime = GetFireTimeAfter(nextFireTime);
		}

		/// <summary>
		/// Called by the scheduler at the time a <see cref="Trigger" /> is first
		/// added to the scheduler, in order to have the <see cref="Trigger" />
		/// compute its first fire time, based on any associated calendar.
		/// <p>
		/// After this method has been called, <see cref="GetNextFireTime()" />
		/// should return a valid answer.
		/// </p>
		/// 
		/// </summary>
		/// <returns> the first time at which the <see cref="Trigger" /> will be fired
		/// by the scheduler, which is also the same value 
		/// {@link #getNextFireTime()} will return (until after the first 
		/// firing of the <see cref="Trigger" />).
		/// </returns>
#if !NET_20
        public override NullableDateTime ComputeFirstFireTime(ICalendar cal)
#else
        public override DateTime? ComputeFirstFireTime(ICalendar cal)
#endif
		{
			calendar = cal;
			DateTime tempAux = StartTime.AddMilliseconds(-1*1000);
			nextFireTime = GetFireTimeAfter(tempAux);

			return nextFireTime;
		}

		/// <summary> 
		/// Called after the <see cref="IScheduler" /> has executed the 
		/// <see cref="JobDetail" /> associated with the <see cref="Trigger" /> in order
		/// to get the final instruction code from the trigger.
		/// </summary>
		/// <param name="jobCtx">
		/// The <see cref="JobExecutionContext" /> that was used by the
		/// <see cref="IJob" />'s <see cref="IJob.Execute" /> method.
		/// </param>
		/// <param name="result">
		/// The <see cref="JobExecutionException" /> thrown by the
		/// <see cref="IJob" />, if any (may be <see langword="null" />)
		/// </param>
		/// <returns> one of the Trigger.INSTRUCTION_XXX constants.
		/// </returns>
        public override SchedulerInstruction ExecutionComplete(JobExecutionContext jobCtx, JobExecutionException result)
		{
			if (result != null && result.RefireImmediately)
			{
                return SchedulerInstruction.ReExecuteJob;
			}

			if (result != null && result.UnscheduleFiringTrigger)
			{
                return SchedulerInstruction.SetTriggerComplete;
			}

			if (result != null && result.UnscheduleAllTriggers)
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
		/// Used by the <see cref="IScheduler" /> to determine whether or not it is
		/// possible for this <see cref="Trigger" /> to fire again.
		/// </summary>'
		/// <remarks>
		/// <p>
		/// If the returned value is <see langword="false" /> then the 
		/// <see cref="IScheduler" /> may remove the <see cref="Trigger" /> from the
		/// <see cref="IJobStore" />
		/// </p>
		/// </remarks>
		/// <returns>
		/// A boolean indicator of whether the trigger could potentially fire
		/// again.
		/// </returns>
		public override bool GetMayFireAgain()
		{
			return GetNextFireTime().HasValue;
		}

		/// <summary> 
		/// Indicates whether <param name="misfireInstruction" /> is a valid misfire
		/// instruction for this <see cref="Trigger" />.
		/// </summary>
        /// <returns>Whether <see param="misfireInstruction" /> is valid.</returns>
		protected override bool ValidateMisfireInstruction(int misfireInstruction)
		{
            return (misfireInstruction == MisfirePolicy.NthIncludedDayTrigger.DoNothing) 
                || (misfireInstruction == MisfirePolicy.NthIncludedDayTrigger.FireOnceNow)
                || (misfireInstruction == MisfirePolicy.SmartPolicy);
		
		}

		/// <summary> Updates the <see cref="NthIncludedDayTrigger" />'s state based on the
		/// MISFIRE_INSTRUCTION_XXX that was selected when the 
		/// <see cref="NthIncludedDayTrigger" /> was created
		/// <P>
		/// If the misfire instruction is set to MISFIRE_INSTRUCTION_SMART_POLICY,
		/// then the instruction will be interpreted as 
		/// {@link #MISFIRE_INSTRUCTION_FIRE_ONCE_NOW}.
		/// </P>
		/// </summary>
		/// <param name="cal">a new or updated calendar to use for the trigger
		/// </param>
		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instruction = MisfireInstruction;

			calendar = cal;

			if (instruction == MisfirePolicy.SmartPolicy)
			{
				instruction = MisfirePolicy.NthIncludedDayTrigger.FireOnceNow;
			}

			if (instruction == MisfirePolicy.NthIncludedDayTrigger.DoNothing)
			{
				DateTime tempAux = DateTime.Now;
				nextFireTime = GetFireTimeAfter(tempAux);
			}
			else if (instruction == MisfirePolicy.NthIncludedDayTrigger.FireOnceNow)
			{
				nextFireTime = DateTime.Now;
			}
		}

		/// <summary> 
		/// Updates the <see cref="NthIncludedDayTrigger" />'s state based on the 
		/// given new version of the associated <see cref="ICalendar" />. 
		/// </summary>
		/// <param name="cal">A new or updated calendar to use for the trigger</param>
		/// <param name="misfireThreshold">the amount of time (in milliseconds) that must
		/// be between &quot;now&quot; and the time the next
		/// firing of the trigger is supposed to occur.
		/// </param>
		public override void UpdateWithNewCalendar(ICalendar cal, long misfireThreshold)
		{
		    calendar = cal;
			nextFireTime = GetFireTimeAfter(previousFireTime);

			DateTime now = DateTime.Now;
			if ((nextFireTime.HasValue) && ((nextFireTime.Value < now)))
			{
			    long diff = (long) (now - nextFireTime.Value).TotalMilliseconds;
			    if (diff >= misfireThreshold)
				{
					nextFireTime = GetFireTimeAfter(nextFireTime);
				}
			}
		}

		/// <summary> 
		/// Calculates the first time an <see cref="NthIncludedDayTrigger" /> with 
		/// <c>intervalType = INTERVAL_TYPE_WEEKLY</c> will fire 
		/// after the specified date. See <see cref="GetNextFireTime" /> for more 
		/// information.
		/// </summary>
		/// <param name="afterDate">The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified
		/// date
		/// </returns>
#if !NET_20
        private NullableDateTime GetWeeklyFireTimeAfter(NullableDateTime afterDate)
#else
        private DateTime? GetWeeklyFireTimeAfter(DateTime? afterDate)
#endif
		{
			int currN = 0;
			DateTime afterCal = afterDate.Value;
			DateTime currCal = new DateTime(afterCal.Year, afterCal.Month, afterCal.Day);
			int currWeek;
			int weekCount = 0;
			bool gotOne = false;

			//move to the first day of the week (SUNDAY)
			currCal = currCal.AddDays(((int) afterCal.DayOfWeek)*- 1);

			currCal = new DateTime(currCal.Year, currCal.Month, currCal.Day, fireAtHour, fireAtMinute, fireAtSecond, 0);

			currWeek = GetWeekOfYear(currCal);

			while ((!gotOne) && (weekCount < nextFireCutoffInterval))
			{
				while ((currN != n) && (weekCount < 12))
				{
					//if we move into a new week, reset the current "n" counter
					if (GetWeekOfYear(currCal) != currWeek)
					{
						currN = 0;
						weekCount++;
						currWeek = GetWeekOfYear(currCal);
					}

					//treating a null calendar as an all-inclusive calendar,
					// increment currN if the current date being tested is included
					// on the calendar
					if ((calendar == null) || calendar.IsTimeIncluded(currCal))
					{
						currN++;
					}

					if (currN != n)
					{
						currCal = currCal.AddDays(1);
					}

					//if we pass endTime, drop out and return null.
					if (EndTime.HasValue && currCal > EndTime.Value)
					{
						return null;
					}
				}

				//We found an "n" or we've checked the requisite number of weeks.
				// If we've found an "n", is it the right one? -- that is, we could
				// be looking at an nth day PRIOR to afterDate
				if (currN == n)
				{
					if (afterDate.Value < currCal)
					{
						gotOne = true;
					}
					else
					{
						//resume checking on the first day of the next week
						currCal = currCal.AddDays((- 1)*(currN - 1));
						currCal = currCal.AddDays(7);
						currN = 0;
					}
				}
			}

			if (weekCount < nextFireCutoffInterval)
			{
				return currCal;
			}
			else
			{
				return null;
			}
		}

		/// <summary> 
		/// Calculates the first time an <see cref="NthIncludedDayTrigger" /> with 
		/// intervalType = <see cref="INTERVAL_TYPE_MONTHLY" /> will fire 
		/// after the specified date. See <see cref="GetNextFireTime" /> for more 
		/// information.
		/// </summary>
		/// <param name="afterDate">
		/// The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified date </returns>
#if !NET_20
        private NullableDateTime GetMonthlyFireTimeAfter(DateTime afterDate)
#else
        private DateTime? GetMonthlyFireTimeAfter(DateTime afterDate)
#endif
		{
			int currN = 0;
			DateTime afterCal = afterDate;
			DateTime currCal = new DateTime(afterCal.Year, afterCal.Month, afterCal.Day, fireAtHour, fireAtMinute, fireAtSecond, 0);
			int currMonth;
			int monthCount = 0;
			bool gotOne = false;

			currMonth = currCal.Month;

			while ((!gotOne) && (monthCount < nextFireCutoffInterval))
			{
				while ((currN != n) && (monthCount < 12))
				{
					//if we move into a new month, reset the current "n" counter
					if (currCal.Month != currMonth)
					{
						currN = 0;
						monthCount++;
						currMonth = currCal.Month;
					}

					//treating a null calendar as an all-inclusive calendar,
					// increment currN if the current date being tested is included
					// on the calendar
					if ((calendar == null) || calendar.IsTimeIncluded(currCal))
					{
						currN++;
					}

					if (currN != n)
					{
						currCal = currCal.AddDays(1);
					}

					//if we pass endTime, drop out and return null.
					if (EndTime.HasValue && currCal > EndTime.Value)
					{
						return null;
					}
				}

				//We found an "n" or we've checked the requisite number of months.
				// If we've found an "n", is it the right one? -- that is, we could
				// be looking at an nth day PRIOR to afterDate
				if (currN == n)
				{
					if (afterDate < currCal)
					{
						gotOne = true;
					}
					else
					{
						//resume checking on the first day of the next month
						currCal =
							new DateTime(currCal.Year, currCal.Month, 1, currCal.Hour, currCal.Minute, currCal.Second, currCal.Millisecond);
						currCal = currCal.AddMonths(1);
						currN = 0;
					}
				}
			}

			if (monthCount < nextFireCutoffInterval)
			{
				return currCal;
			}
			else
			{
				return null;
			}
		}

		/// <summary> 
		/// Calculates the first time an <see cref="NthIncludedDayTrigger" /> with 
		/// intervalType = <see cref="INTERVAL_TYPE_YEARLY" /> will fire 
		/// after the specified date. See <see cref="GetNextFireTime" /> for more 
		/// information.
		/// </summary>
		/// <param name="afterDate">
		/// The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified
		/// date
		/// </returns>
#if !NET_20
        private NullableDateTime GetYearlyFireTimeAfter(NullableDateTime afterDate)
#else
        private DateTime? GetYearlyFireTimeAfter(DateTime? afterDate)
#endif
		{
			int currN = 0;
			DateTime afterCal = afterDate.Value;
			DateTime currCal = new DateTime(afterCal.Year, 1, 1, fireAtHour, fireAtMinute, fireAtSecond, 0);
			int currYear;
			int yearCount = 0;
			bool gotOne = false;

			currYear = currCal.Year;

			while ((!gotOne) && (yearCount < nextFireCutoffInterval))
			{
				while ((currN != n) && (yearCount < 5))
				{
					//if we move into a new year, reset the current "n" counter
					if (currCal.Year != currYear)
					{
						currN = 0;
						yearCount++;
						currYear = currCal.Year;
					}

					//treating a null calendar as an all-inclusive calendar,
					// increment currN if the current date being tested is included
					// on the calendar
					if (calendar == null || calendar.IsTimeIncluded(currCal))
					{
						currN++;
					}

					if (currN != n)
					{
						currCal = currCal.AddDays(1);
					}

					//if we pass endTime, drop out and return null.
					if (EndTime.HasValue && currCal > EndTime.Value)
					{
						return null;
					}
				}

				//We found an "n" or we've checked the requisite number of years.
				// If we've found an "n", is it the right one? -- that is, we 
				// could be looking at an nth day PRIOR to afterDate
				if (currN == n)
				{
					if (afterDate.Value < currCal)
					{
						gotOne = true;
					}
					else
					{
						//resume checking on the first day of the next year
						currCal = new DateTime(currCal.Year + 1, 1, 1, currCal.Hour, currCal.Minute, currCal.Second);
						currN = 0;
					}
				}
			}

			if (yearCount < nextFireCutoffInterval)
			{
				return currCal;
			}
			else
			{
				return null;
			}
		}


		private int GetWeekOfYear(DateTime date)
		{
			GregorianCalendar gCal = new GregorianCalendar();
			// TODO, it isn't always monday..
			return gCal.GetWeekOfYear(date, TriggerCalendarWeekRule, TriggerCalendarFirstDayOfWeek);
		}

		/// <summary>
		/// Gets or sets the trigger's calendar week rule.
		/// </summary>
		/// <value>The trigger calendar week rule.</value>
		public CalendarWeekRule TriggerCalendarWeekRule
		{
			get { return triggerCalendarWeekRule ; }
			set { triggerCalendarWeekRule = value; }
		}

		/// <summary>
		/// Gets or sets the trigger's calendar first day of week rule.
		/// </summary>
		/// <value>The trigger calendar first day of week.</value>
		public DayOfWeek TriggerCalendarFirstDayOfWeek
		{
			get { return triggerCalendarFirstDayOfWeek; }
			set { triggerCalendarFirstDayOfWeek = value;}
		}
	}
}
