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

using Nullables;

namespace Quartz
{
	/// <summary> 
	/// A trigger which fires on the N<sup>th</sup> day of every interval type 
	/// ({@link #INTERVAL_TYPE_WEEKLY}, {@link #INTERVAL_TYPE_MONTHLY} or 
	/// {@link #INTERVAL_TYPE_YEARLY}) that is <i>not</i> excluded by the associated
	/// calendar. When determining what the N<sup>th</sup> day of the month or year
	/// is, <code>NthIncludedDayTrigger</code> will skip excluded days on the 
	/// associated calendar. This would commonly be used in an N<sup>th</sup> 
	/// business day situation, in which the user wishes to fire a particular job on
	/// the N<sup>th</sup> business day (i.e. the 5<sup>th</sup> business day of
	/// every month). Each <code>NthIncludedDayTrigger</code> also has an associated
	/// <code>fireAtTime</code> which indicates at what time of day the trigger is
	/// to fire.
	/// <p>
	/// All <code>NthIncludedDayTrigger</code>s default to a monthly interval type
	/// (fires on the N<SUP>th</SUP> day of every month) with N = 1 (first 
	/// non-excluded day) and <code>fireAtTime</code> set to 12:00 PM (noon). These
	/// values can be changed using the {@link #setN}, {@link #setIntervalType}, and
	/// {@link #setFireAtTime} methods. Users may also want to note the 
	/// {@link #setNextFireCutoffInterval} and {@link #getNextFireCutoffInterval}
	/// methods.
	/// </p>
	/// <p>
	/// Take, for example, the following calendar:
	/// </p>
	/// <pre>
	/// July                  August                September
	/// Su Mo Tu We Th Fr Sa   Su Mo Tu We Th Fr Sa   Su Mo Tu We Th Fr Sa
	/// 1  W       1  2  3  4  5  W                1  2  W
	/// W  H  5  6  7  8  W    W  8  9 10 11 12  W    W  H  6  7  8  9  W
	/// W 11 12 13 14 15  W    W 15 16 17 18 19  W    W 12 13 14 15 16  W
	/// W 18 19 20 21 22  W    W 22 23 24 25 26  W    W 19 20 21 22 23  W
	/// W 25 26 27 28 29  W    W 29 30 31             W 26 27 28 29 30
	/// W
	/// </pre>
	/// Where W's represent weekend days, and H's represent holidays, all of which
	/// are excluded on a calendar associated with an 
	/// <code>NthIncludedDayTrigger</code> with <code>n=5</code> and 
	/// <code>intervalType=INTERVAL_TYPE_MONTHLY</code>. In this case, the trigger 
	/// would fire on the 8<sup>th</sup> of July (because of the July 4 holiday), 
	/// the 5<sup>th</sup> of August, and the 8<sup>th</sup> of September (because 
	/// of Labor Day).
	/// 
	/// </summary>
	/// <author>Aaron Craven</author>
	[Serializable]
	public class NthIncludedDayTrigger : Trigger
	{
		/// <summary> 
		/// Returns the day of the interval on which the 
		/// <code>NthIncludedDayTrigger</code> should fire.
		/// 
		/// Sets the day of the interval on which the 
		/// <code>NthIncludedDayTrigger</code> should fire. If the N<SUP>th</SUP>
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
		/// Returns the interval type for the <code>NthIncludedDayTrigger</code>.
		/// 
		/// Sets the interval type for the <code>NthIncludedDayTrigger</code>. If
		/// {@link #INTERVAL_TYPE_MONTHLY}, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every month. If 
		/// {@link #INTERVAL_TYPE_YEARLY}, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every year. If 
		/// {@link #INTERVAL_TYPE_WEEKLY}, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every month. 
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
		/// Returns the fire time for the <code>NthIncludedDayTrigger</code> as a
		/// string with the format &quot;HH:MM&quot;, with HH representing the 
		/// 24-hour clock hour of the fire time.
		///
		/// Sets the fire time for the <code>NthIncludedDayTrigger</code>, which
		/// should be represented as a string with the format &quot;HH:MM&quot;, 
		/// with HH representing the 24-hour clock hour of the fire time. Hours can
		/// be represented as either a one-digit or two-digit number.
		/// </summary>
		public virtual string FireAtTime
		{
			get { return fireAtHour + ":" + fireAtMinute; }

			set
			{
				int fireHour = 12;
				int fireMinute = 0;
				//string[] components;

				try
				{
					int i = value.IndexOf(":");
					fireHour = Int32.Parse(value.Substring(0, (i) - (0)));
					fireMinute = Int32.Parse(value.Substring(i + 1));
				}
				catch (Exception e)
				{
					fireHour = 12;
					fireMinute = 0;
					throw new ArgumentException("Could not parse time expression: " + e.Message);
				}
				finally
				{
					fireAtHour = fireHour;
					fireAtMinute = fireMinute;
				}
			}
		}

		/// <summary> 
		/// Returns the <code>nextFireCutoffInterval</code> for the 
		/// <code>NthIncludedDayTrigger</code>.
		/// <P>
		/// Because of the conceptual design of <code>NthIncludedDayTrigger</code>,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// {@link #setNextFireCutoffInterval(int)} and 
		/// {@link #getNextFireCutoffInterval()} methods. The default cutoff is 12
		/// of the intervals specified by <code>{@link #getIntervalType()
		/// intervalType}</code>.
		/// </P>
		/// <p>
		/// Because of the conceptual design of <code>NthIncludedDayTrigger</code>,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// {@link #setNextFireCutoffInterval(int)} and 
		/// {@link #getNextFireCutoffInterval()} methods. The default cutoff is 12
		/// of the intervals specified by <code>{@link #getIntervalType()
		/// intervalType}</code>.
		/// </p>
		/// <P>
		/// In most cases, the default value of this setting (12) is sufficient (it
		/// is highly unlikely, for example, that you will need to look at more than
		/// 12 months of dates to ensure that your trigger will never fire again).  
		/// However, this setting is included to allow for the rare exceptions where
		/// this might not be true.
		/// </P>
		/// <P>
		/// For example, if your trigger is associated with a calendar that excludes
		/// a great many dates in the next 12 months, and hardly any following that,
		/// it is possible (if <code>n</code> is large enough) that you could run 
		/// into this situation.  
		/// </P>
		/// </summary>
		public virtual int NextFireCutoffInterval
		{
			get { return nextFireCutoffInterval; }
			set { nextFireCutoffInterval = value; }
		}


		/// <summary>
		/// Returns the last time the <code>NthIncludedDayTrigger</code> will fire.
		/// If the trigger will not fire at any point between <code>startTime</code>
		/// and <code>endTime</code>, <code>null</code> will be returned.
		/// </summary>
		/// <returns> the last time the trigger will fire.
		/// </returns>
		public override NullableDateTime FinalFireTime
		{
			get
			{
				NullableDateTime finalTime = NullableDateTime.Default;
				NullableDateTime currCal = new NullableDateTime(EndTime.Value);

				while (!finalTime.HasValue && StartTime < currCal.Value)
				{
					currCal = currCal.Value.AddDays(-1);
					finalTime = GetFireTimeAfter(currCal);
				}

				return finalTime;
			}
		}

		public override bool HasMillisecondPrecision
		{
			get { throw new NotImplementedException(); }
		}

		/// <summary> 
		/// Instructs the <code>Scheduler</code> that upon a mis-fire situation, the
		/// <code>NthIncludedDayTrigger</code> wants to be fired now by the 
		/// <code>Scheduler</code>
		/// </summary>
		public const int MISFIRE_INSTRUCTION_FIRE_ONCE_NOW = 1;

		/// <summary> 
		/// Instructs the <code>Scheduler</code> that upon a mis-fire situation, the
		/// <code>NthIncludedDayTrigger</code> wants to have 
		/// <code>nextFireTime</code> updated to the next time in the schedule after
		/// the current time, but it does not want to be fired now.
		/// </summary>
		public const int MISFIRE_INSTRUCTION_DO_NOTHING = 2;

		/// <summary> 
		/// Indicates a monthly trigger type (fires on the N<SUP>th</SUP> included
		/// day of every month).
		/// </summary>
		public const int INTERVAL_TYPE_MONTHLY = 1;

		/// <summary> indicates a yearly trigger type (fires on the N<SUP>th</SUP> included 
		/// day of every year).
		/// </summary>
		public const int INTERVAL_TYPE_YEARLY = 2;

		/// <summary> indicates a weekly trigger type (fires on the N<SUP>th</SUP> included
		/// day of every week). When using this interval type, care must be taken
		/// not to think of the value of <code>n</code> as an analog to 
		/// <code>java.util.Calendar.DAY_OF_WEEK</code>. Such a comparison can only
		/// be drawn when there are no calendars associated with the trigger. To 
		/// illustrate, consider an <code>NthIncludedDayTrigger</code> with 
		/// <code>n = 3</code> which is associated with a Calendar excluding
		/// non-weekdays. The trigger would fire on the 3<SUP>rd</SUP> 
		/// <I>included</I> day of the week, which would be 4<SUP>th</SUP> 
		/// <I>actual</I> day of the week.
		/// </summary>
		public const int INTERVAL_TYPE_WEEKLY = 3;

		private NullableDateTime previousFireTime;
		private NullableDateTime nextFireTime;
		private ICalendar calendar;

		private int n = 1;
		private int intervalType = INTERVAL_TYPE_MONTHLY;
		private int fireAtHour = 12;
		private int fireAtMinute = 0;
		private int nextFireCutoffInterval = 12;

		/// <summary> 
		/// Create an <code>NthIncludedDayTrigger</code> with no specified name,
		/// group, or <code>JobDetail</code>. This will result initially in a
		/// default monthly trigger that fires on the first day of every month at
		/// 12:00 PM (<code>n</code>=1, 
		/// <code>intervalType={@link #INTERVAL_TYPE_MONTHLY}</code>, 
		/// <code>fireAtTime="12:00"</code>).
		/// <p>
		/// Note that <code>setName()</code>, <code>setGroup()</code>, 
		/// <code>setJobName()</code>, and <code>setJobGroup()</code>, must be 
		/// called before the <code>NthIncludedDayTrigger</code> can be placed into
		/// a <code>Scheduler</code>.
		/// </p>
		/// </summary>
		public NthIncludedDayTrigger() : base()
		{
		}

		/// <summary> 
		/// Create an <code>NthIncludedDayTrigger</code> with the given name and
		/// group but no specified <code>JobDetail</code>. This will result 
		/// initially in a default monthly trigger that fires on the first day of 
		/// every month at 12:00 PM (<code>n</code>=1, 
		/// <code>intervalType={@link #INTERVAL_TYPE_MONTHLY}</code>, 
		/// <code>fireAtTime="12:00"</code>).
		/// <p>
		/// Note that <code>setJobName()</code> and <code>setJobGroup()</code> must
		/// be called before the <code>NthIncludedDayTrigger</code> can be placed 
		/// into a <code>Scheduler</code>.
		/// </p>
		/// </summary>
		/// <param name="name"> the name for the <code>NthIncludedDayTrigger</code>
		/// </param>
		/// <param name="group">the group for the <code>NthIncludedDayTrigger</code>
		/// </param>
		public NthIncludedDayTrigger(string name, string group) : base(name, group)
		{
		}

		/// <summary> Create an <code>NthIncludedDayTrigger</code> with the given name and
		/// group and the specified <code>JobDetail</code>. This will result 
		/// initially in a default monthly trigger that fires on the first day of
		/// every month at 12:00 PM (<code>n</code>=1, 
		/// <code>intervalType={@link #INTERVAL_TYPE_MONTHLY}</code>, 
		/// <code>fireAtTime="12:00"</code>).
		/// 
		/// </summary>
		/// <param name="name">    the name for the <code>NthIncludedDayTrigger</code>
		/// </param>
		/// <param name="group">   the group for the <code>NthIncludedDayTrigger</code>
		/// </param>
		/// <param name="jobName"> the name of the job to associate with the 
		/// <code>NthIncludedDayTrigger</code>
		/// </param>
		/// <param name="jobGroup">the group containing the job to associate with the 
		/// <code>NthIncludedDayTrigger</code>
		/// </param>
		public NthIncludedDayTrigger(string name, string group, string jobName, string jobGroup)
			: base(name, group, jobName, jobGroup)
		{
		}

		/// <summary> 
		/// Returns the next time at which the <code>NthIncludedDayTrigger</code>
		/// will fire. If the trigger will not fire again, <code>null</code> will be
		/// returned. 
		/// <p>
		/// Because of the conceptual design of <code>NthIncludedDayTrigger</code>,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// {@link #setNextFireCutoffInterval(int)} and 
		/// {@link #getNextFireCutoffInterval()} methods. The default cutoff is 12
		/// of the intervals specified by <code>{@link #getIntervalType()
		/// intervalType}</code>.
		/// </p>
		/// <p>
		/// The returned value is not guaranteed to be valid until after
		/// the trigger has been added to the scheduler.
		/// </p>
		/// </summary>
		/// <returns> the next fire time for the trigger
		/// </returns>
		/// <seealso cref="NextFireCutoffInterval" /> 
		/// <seealso cref="GetFireTimeAfter(NullableDateTime)" />
		public override NullableDateTime GetNextFireTime()
		{
			return nextFireTime;
		}

		/// <summary> Returns the previous time at which the 
		/// <code>NthIncludedDayTrigger</code> fired. If the trigger has not yet 
		/// fired, <code>null</code> will be returned.
		/// 
		/// </summary>
		/// <returns> the previous fire time for the trigger
		/// </returns>
		public override NullableDateTime GetPreviousFireTime()
		{
			return previousFireTime;
		}

		/// <summary>
		/// Returns the first time the <code>NthIncludedDayTrigger</code> will fire
		/// after the specified date. 
		/// <P> 
		/// Because of the conceptual design of <code>NthIncludedDayTrigger</code>,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// {@link #setNextFireCutoffInterval(int)} and 
		/// {@link #getNextFireCutoffInterval()} methods. The default cutoff is 12
		/// of the intervals specified by <code>{@link #getIntervalType()
		/// intervalType}</code>.
		/// </P>
		/// <P>
		/// Therefore, for triggers with <code>intervalType = 
		/// {@link NthIncludedDayTrigger#INTERVAL_TYPE_WEEKLY 
		/// INTERVAL_TYPE_WEEKLY}</code>, if the trigger will not fire within 12
		/// weeks after the given date/time, <code>null</code> will be returned. For
		/// triggers with <code>intervalType = 
		/// {@link NthIncludedDayTrigger#INTERVAL_TYPE_MONTHLY
		/// INTERVAL_TYPE_MONTHLY}</code>, if the trigger will not fire within 12 
		/// months after the given date/time, <code>null</code> will be returned. 
		/// For triggers with <code>intervalType = 
		/// {@link NthIncludedDayTrigger#INTERVAL_TYPE_YEARLY 
		/// INTERVAL_TYPE_YEARLY}</code>, if the trigger will not fire within 12
		/// years after the given date/time, <code>null</code> will be returned.  In 
		/// all cases, if the trigger will not fire before <code>endTime</code>, 
		/// <code>null</code> will be returned.
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
		public override NullableDateTime GetFireTimeAfter(NullableDateTime afterTime)
		{
			if (afterTime == null || !afterTime.HasValue)
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
				return NullableDateTime.Default;
			}
		}

		/// <summary>
		/// Called when the <code>Scheduler</code> has decided to 'fire' the trigger
		/// (Execute the associated <code>Job</code>), in order to give the 
		/// <code>Trigger</code> a chance to update itself for its next triggering 
		/// (if any).
		/// </summary>
		public override void Triggered(ICalendar cal)
		{
			calendar = cal;
			previousFireTime = nextFireTime;
			nextFireTime = GetFireTimeAfter(nextFireTime);
		}

		/// <summary>
		/// Called by the scheduler at the time a <code>Trigger</code> is first
		/// added to the scheduler, in order to have the <code>Trigger</code>
		/// compute its first fire time, based on any associated calendar.
		/// <p>
		/// After this method has been called, <code>getNextFireTime()</code>
		/// should return a valid answer.
		/// </p>
		/// 
		/// </summary>
		/// <returns> the first time at which the <code>Trigger</code> will be fired
		/// by the scheduler, which is also the same value 
		/// {@link #getNextFireTime()} will return (until after the first 
		/// firing of the <code>Trigger</code>).
		/// </returns>
		public override NullableDateTime ComputeFirstFireTime(ICalendar cal)
		{
			calendar = cal;
			DateTime tempAux = StartTime.AddMilliseconds(-1*1000);
			nextFireTime = GetFireTimeAfter(tempAux);

			return nextFireTime;
		}

		/// <summary> 
		/// Called after the <code>Scheduler</code> has executed the 
		/// <code>JobDetail</code> associated with the <code>Trigger</code> in order
		/// to get the final instruction code from the trigger.
		/// </summary>
		/// <param name="jobCtx">
		/// The <code>JobExecutionContext</code> that was used by the
		/// <code>Job</code>'s <code>Execute()</code> method.
		/// </param>
		/// <param name="result">
		/// The <code>JobExecutionException</code> thrown by the
		/// <code>Job</code>, if any (may be <code>null</code>)
		/// </param>
		/// <returns> one of the Trigger.INSTRUCTION_XXX constants.
		/// </returns>
		public override int ExecutionComplete(JobExecutionContext jobCtx, JobExecutionException result)
		{
			if (result != null && result.RefireImmediately())
			{
				return INSTRUCTION_RE_EXECUTE_JOB;
			}

			if (result != null && result.unscheduleFiringTrigger())
			{
				return INSTRUCTION_SET_TRIGGER_COMPLETE;
			}

			if (result != null && result.unscheduleAllTriggers())
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
		/// Used by the <code>Scheduler</code> to determine whether or not it is
		/// possible for this <code>Trigger</code> to fire again.
		/// <P>
		/// If the returned value is <code>false</code> then the 
		/// <code>Scheduler</code> may remove the <code>Trigger</code> from the
		/// <code>JobStore</code>
		/// </P>
		/// </summary>
		/// <returns> a boolean indicator of whether the trigger could potentially fire
		/// again
		/// </returns>
		public override bool MayFireAgain()
		{
			NullableDateTime d = GetNextFireTime();
			return (d == null || !d.HasValue);
		}

		/// <summary> Indicates whether <code>misfireInstruction</code> is a valid misfire
		/// instruction for this <code>Trigger</code>.
		/// 
		/// </summary>
		/// <returns> whether <code>misfireInstruction</code> is valid.
		/// </returns>
		protected override bool ValidateMisfireInstruction(int misfireInstruction)
		{
			if ((misfireInstruction == MISFIRE_INSTRUCTION_SMART_POLICY) ||
			    (misfireInstruction == MISFIRE_INSTRUCTION_DO_NOTHING) ||
			    (misfireInstruction == MISFIRE_INSTRUCTION_FIRE_ONCE_NOW))
			{
				return true;
			}
			else
			{
				return false;
			}
		}

		/// <summary> Updates the <code>NthIncludedDayTrigger</code>'s state based on the
		/// MISFIRE_INSTRUCTION_XXX that was selected when the 
		/// <code>NthIncludedDayTrigger</code> was created
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

			if (instruction == MISFIRE_INSTRUCTION_SMART_POLICY)
			{
				instruction = MISFIRE_INSTRUCTION_FIRE_ONCE_NOW;
			}

			if (instruction == MISFIRE_INSTRUCTION_DO_NOTHING)
			{
				DateTime tempAux = DateTime.Now;
				nextFireTime = GetFireTimeAfter(tempAux);
			}
			else if (instruction == MISFIRE_INSTRUCTION_FIRE_ONCE_NOW)
			{
				nextFireTime = DateTime.Now;
			}
		}

		/// <summary> Updates the <code>NthIncludedDayTrigger</code>'s state based on the 
		/// given new version of the associated <code>Calendar</code>. 
		/// 
		/// </summary>
		/// <param name="cal">        a new or updated calendar to use for the trigger
		/// </param>
		/// <param name="misfireThreshold">the amount of time (in milliseconds) that must
		/// be between &quot;now&quot; and the time the next
		/// firing of the trigger is supposed to occur.
		/// </param>
		public override void UpdateWithNewCalendar(ICalendar cal, long misfireThreshold)
		{
			long diff;

			calendar = cal;
			nextFireTime = GetFireTimeAfter(previousFireTime);

			DateTime now = DateTime.Now;
			if ((nextFireTime != null && nextFireTime.HasValue) && ((nextFireTime.Value < now)))
			{
				diff = (long) (now - nextFireTime.Value).TotalMilliseconds;
				if (diff >= misfireThreshold)
				{
					nextFireTime = GetFireTimeAfter(nextFireTime);
				}
			}
		}

		/// <summary> Calculates the first time an <code>NthIncludedDayTrigger</code> with 
		/// <code>intervalType = {@link #INTERVAL_TYPE_WEEKLY}</code> will fire 
		/// after the specified date. See {@link #getNextFireTime} for more 
		/// information.
		/// 
		/// </summary>
		/// <param name="afterDate">The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified
		/// date
		/// </returns>
		private NullableDateTime GetWeeklyFireTimeAfter(NullableDateTime afterDate)
		{
			int currN = 0;
			DateTime afterCal = afterDate.Value;
			DateTime currCal = new DateTime(afterCal.Year, afterCal.Month, afterCal.Day);
			int currWeek;
			int weekCount = 0;
			bool gotOne = false;

			//move to the first day of the week (SUNDAY)
			currCal = currCal.AddDays(((int) afterCal.DayOfWeek - 1)*- 1);

			currCal = new DateTime(currCal.Year, currCal.Month, currCal.Day, fireAtHour, fireAtMinute, 0, 0);

			currWeek = GetWeekOfYear(currCal);

			while ((!gotOne) && (weekCount < nextFireCutoffInterval))
			{
				while ((currN != n) && (weekCount < 12))
				{
					//if we move into a new month, reset the current "n" counter
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
						return NullableDateTime.Default;
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
				return NullableDateTime.Default;
			}
		}

		/// <summary> 
		/// Calculates the first time an <code>NthIncludedDayTrigger</code> with 
		/// <code>intervalType = {@link #INTERVAL_TYPE_MONTHLY}</code> will fire 
		/// after the specified date. See {@link #getNextFireTime} for more 
		/// information.
		/// </summary>
		/// <param name="afterDate">
		/// The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified date </returns>
		private NullableDateTime GetMonthlyFireTimeAfter(DateTime afterDate)
		{
			int currN = 0;
			DateTime afterCal = afterDate;
			DateTime currCal = new DateTime(afterCal.Year, afterCal.Month, afterCal.Day, fireAtHour, fireAtMinute, 0, 0);
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
						return NullableDateTime.Default;
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
				return NullableDateTime.Default;
			}
		}

		/// <summary> Calculates the first time an <code>NthIncludedDayTrigger</code> with 
		/// <code>intervalType = {@link #INTERVAL_TYPE_YEARLY}</code> will fire 
		/// after the specified date. See {@link #getNextFireTime} for more 
		/// information.
		/// 
		/// </summary>
		/// <param name="afterDate">The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified
		/// date
		/// </returns>
		private NullableDateTime GetYearlyFireTimeAfter(NullableDateTime afterDate)
		{
			int currN = 0;
			DateTime afterCal = afterDate.Value;
			DateTime currCal = new DateTime(afterCal.Year, 1, 1, fireAtHour, fireAtMinute, 0, 0);
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
						return NullableDateTime.Default;
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
				return NullableDateTime.Default;
			}
		}


		private int GetWeekOfYear(DateTime date)
		{
			GregorianCalendar gCal = new GregorianCalendar();
			// TODO, it isn't always monday..
			return gCal.GetWeekOfYear(date, CalendarWeekRule.FirstFourDayWeek, DayOfWeek.Monday);
		}
	}
}