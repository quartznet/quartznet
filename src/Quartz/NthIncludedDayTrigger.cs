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
using System.Globalization;

using Quartz.Impl.Triggers;
using Quartz.Spi;

namespace Quartz
{
	/// <summary> 
	/// A trigger which fires on the N<sup>th</sup> day of every interval type 
    /// <see cref="IntervalTypeWeekly" />, <see cref="IntervalTypeMonthly" /> or 
    /// <see cref="IntervalTypeYearly" /> that is <i>not</i> excluded by the associated
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
	/// <para>
	/// All <see cref="NthIncludedDayTrigger" />s default to a monthly interval type
	/// (fires on the N<SUP>th</SUP> day of every month) with N = 1 (first 
	/// non-excluded day) and <see cref="FireAtTime" /> set to 12:00 PM (noon). These
    /// values can be changed using the <see cref="N" />, <see cref="IntervalType" />, and
    /// <see cref="FireAtTime" /> methods. Users may also want to note the 
    /// <see cref="NextFireCutoffInterval" /> and <see cref="NextFireCutoffInterval" />
	/// methods.
	/// </para>
	/// <para>
	/// Take, for example, the following calendar:
	/// </para>
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
	///  intervalType=IntervalTypeMonthly. In this case, the trigger 
	/// would fire on the 8<sup>th</sup> of July (because of the July 4 holiday), 
	/// the 5<sup>th</sup> of August, and the 8<sup>th</sup> of September (because 
	/// of Labor Day).
    /// </remarks>
	/// <author>Aaron Craven</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
	public class NthIncludedDayTrigger : AbstractTrigger
	{

		/// <summary> 
		/// Indicates a monthly trigger type (fires on the N<SUP>th</SUP> included
		/// day of every month).
		/// </summary>
		public const int IntervalTypeMonthly = 1;

		/// <summary> indicates a yearly trigger type (fires on the N<SUP>th</SUP> included 
		/// day of every year).
		/// </summary>
		public const int IntervalTypeYearly = 2;

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
		public const int IntervalTypeWeekly = 3;

        private DateTimeOffset? previousFireTimeUtc;
        private DateTimeOffset? nextFireTimeUtc;
        private ICalendar calendar;

		private int n = 1;
		private int intervalType = IntervalTypeMonthly;
		private int fireAtHour = 12;
		private int fireAtMinute = 0;
		private int nextFireCutoffInterval = 12;
		private int fireAtSecond = 0;
		private DayOfWeek triggerCalendarFirstDayOfWeek = DayOfWeek.Sunday;
		private CalendarWeekRule triggerCalendarWeekRule = CalendarWeekRule.FirstFourDayWeek;
        private TimeZoneInfo timeZone;


        /// <summary> 
        /// Create an <see cref="NthIncludedDayTrigger" /> with no specified name,
        /// group, or <see cref="IJobDetail" />. This will result initially in a
        /// default monthly trigger that fires on the first day of every month at
        /// 12:00 PM (n = 1, 
        /// intervalType=<see cref="IntervalTypeMonthly" />, 
        /// fireAtTime="12:00").
        /// </summary>
        /// <remarks>
        /// Note that <see cref="ITrigger.Key" /> and <see cref="ITrigger.JobKey" />, must be 
        /// called before the <see cref="NthIncludedDayTrigger" /> can be placed into
        /// a <see cref="IScheduler" />.
        /// </remarks>
        public NthIncludedDayTrigger()
        {
            StartTimeUtc = SystemTime.UtcNow();
        }

        /// <summary> 
        /// Create an <see cref="NthIncludedDayTrigger" /> with the given name and
        /// default group but no specified <see cref="IJobDetail" />. This will result 
        /// initially in a default monthly trigger that fires on the first day of 
        /// every month at 12:00 PM (<see cref="n" />=1, 
        /// intervalType=<see cref="IntervalTypeMonthly" />, 
        /// fireAtTime=12:00").
        /// <para>
        /// Note that <see cref="ITrigger.JobKey" /> must
        /// be called before the <see cref="NthIncludedDayTrigger" /> can be placed 
        /// into a <see cref="IScheduler" />.
        /// </para>
        /// </summary>
        /// <param name="name"> the name for the <see cref="NthIncludedDayTrigger" />
        /// </param>
        public NthIncludedDayTrigger(string name) : this(name, null)
        {
        }

        /// <summary> 
        /// Create an <see cref="NthIncludedDayTrigger" /> with the given name and
        /// group but no specified <see cref="IJobDetail" />. This will result 
        /// initially in a default monthly trigger that fires on the first day of 
        /// every month at 12:00 PM (<see cref="n" />=1, 
        /// intervalType=<see cref="IntervalTypeMonthly" />, 
        /// fireAtTime=12:00").
        /// <para>
        /// Note that <see cref="ITrigger.JobKey" /> must
        /// be called before the <see cref="NthIncludedDayTrigger" /> can be placed 
        /// into a <see cref="IScheduler" />.
        /// </para>
        /// </summary>
        /// <param name="name"> the name for the <see cref="NthIncludedDayTrigger" />
        /// </param>
        /// <param name="group">the group for the <see cref="NthIncludedDayTrigger" />
        /// </param>
        public NthIncludedDayTrigger(string name, string group)
            : base(name, group)
        {
            StartTimeUtc = SystemTime.UtcNow();
        }

        /// <summary> 
        /// Create an <see cref="NthIncludedDayTrigger" /> with the given name and
        /// group and the specified <see cref="IJobDetail" />. This will result 
        /// initially in a default monthly trigger that fires on the first day of
        /// every month at 12:00 PM (<see cref="n" />=1, 
        /// intervalType=<see cref="IntervalTypeMonthly" />, 
        /// fireAtTime="12:00").
        /// </summary>
        /// <param name="name">The name for the <see cref="NthIncludedDayTrigger" />.</param>
        /// <param name="group">The group for the <see cref="NthIncludedDayTrigger" />.</param>
        /// <param name="jobName">The name of the job to associate with the <see cref="NthIncludedDayTrigger" />.</param>
        /// <param name="jobGroup">The group containing the job to associate with the <see cref="NthIncludedDayTrigger" />.</param>
        public NthIncludedDayTrigger(string name, string group, string jobName, string jobGroup)
            : base(name, group, jobName, jobGroup)
        {
            StartTimeUtc = SystemTime.UtcNow();
        }


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
		/// </summary>
		/// <remarks>
		/// Sets the interval type for the <see cref="NthIncludedDayTrigger" />. If
        /// <see cref="IntervalTypeMonthly" />, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every month. If 
        /// <see cref="IntervalTypeYearly" />, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every year. If 
        /// <see cref="IntervalTypeWeekly" />, the trigger will fire on the 
		/// N<SUP>th</SUP> included day of every week. 
        /// </remarks>
		/// <seealso cref="IntervalTypeWeekly" />
		/// <seealso cref="IntervalTypeMonthly" />
		/// <seealso cref="IntervalTypeYearly" />
		public virtual int IntervalType
		{
			get { return intervalType; }

			set
			{
				switch (value)
				{
					case IntervalTypeWeekly:
						intervalType = value;
						break;

					case IntervalTypeMonthly:
						intervalType = value;
						break;

					case IntervalTypeYearly:
						intervalType = value;
						break;

					default:
						throw new ArgumentException("Invalid Interval Type: " + value);
				}
			}
		}

		/// <summary>
		/// Returns the fire time for the <see cref="NthIncludedDayTrigger" /> as a
        /// string with the format &quot;HH:MM[:SS]&quot;, with HH representing the 
        /// 24-hour clock hour of the fire time. Seconds are optional and their 
        /// inclusion depends on whether or not they were provided to 
        /// <see cref="FireAtTime" />. 
		/// </summary>
		public virtual string FireAtTime
		{
			get { return string.Format(CultureInfo.InvariantCulture, "{0:00}:{1:00}:{2:00}", fireAtHour, fireAtMinute, fireAtSecond); }

			set
			{
				try
				{
					string[] components = value.Split(':');

                    int newFireHour = Int32.Parse(components[0], CultureInfo.InvariantCulture);
					if (components[1].Length != 2)
					{
						// minutes must be in two digit format
						throw new Exception();
					}
                    int newFireMinute = Int32.Parse(components[1], CultureInfo.InvariantCulture);
					int newFireSecond = 0;
					if (components.Length == 3)
					{
						if (components[2].Length != 2)
						{
							// seconds must be in two digit format
							throw new Exception();
						}
                        newFireSecond = Convert.ToInt32(components[2], CultureInfo.InvariantCulture);
					}

					
					// Check ranges
					if ((newFireHour < 0) || (newFireHour > 23)) 
					{
						throw new ArgumentException(
							string.Format(CultureInfo.InvariantCulture, "Could not parse time expression '{0}':fireAtHour must be between 0 and 23", value));
					} 
					else if ((newFireMinute < 0) || (newFireMinute > 59)) 
					{
						throw new ArgumentException(
							string.Format(CultureInfo.InvariantCulture, "Could not parse time expression '{0}':fireAtMinute must be between 0 and 59", value));
					} 
					else if ((newFireSecond < 0) || (newFireSecond > 59)) 
					{
						throw new ArgumentException(
							string.Format(CultureInfo.InvariantCulture, "Could not parse time expression '{0}':fireAtMinute must be between 0 and 59", value));
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
		/// <para>
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
        /// <see cref="NextFireCutoffInterval" /> property. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType"/> intervalType" />.
		/// </para>
		/// <para>
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <I>never</I> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// <see cref="NextFireCutoffInterval" /> method. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType" /> intervalType".
		/// </para>
		/// <para>
		/// In most cases, the default value of this setting (12) is sufficient (it
		/// is highly unlikely, for example, that you will need to look at more than
		/// 12 months of dates to ensure that your trigger will never fire again).  
		/// However, this setting is included to allow for the rare exceptions where
		/// this might not be true.
		/// </para>
		/// <para>
		/// For example, if your trigger is associated with a calendar that excludes
		/// a great many dates in the next 12 months, and hardly any following that,
		/// it is possible (if <see cref="N" /> is large enough) that you could run 
		/// into this situation.  
		/// </para>
		/// </remarks>
		public virtual int NextFireCutoffInterval
		{
			get { return nextFireCutoffInterval; }
			set { nextFireCutoffInterval = value; }
		}

	    /// <summary>
		/// Returns the last UTC time the <see cref="NthIncludedDayTrigger" /> will fire.
		/// If the trigger will not fire at any point between <see name="startTime" />
		/// and <see name="endTime" />, <see langword="null" /> will be returned.
		/// </summary>
		/// <returns> the last time the trigger will fire.</returns>
        public override DateTimeOffset? FinalFireTimeUtc
		{
			get
			{
                if (!EndTimeUtc.HasValue)
                {
                    // short-circuit
                    return null;
                }

                DateTimeOffset? finalTime = null;
                DateTimeOffset? currCal = EndTimeUtc.Value;

                while (!finalTime.HasValue && StartTimeUtc < currCal.Value)
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
		/// Returns the next UTC time at which the <see cref="NthIncludedDayTrigger" />
		/// will fire. If the trigger will not fire again, <see langword="null" /> will be
		/// returned. 
		/// </summary>
		/// <remarks>
		/// <para>
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <i>never</i> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// <see cref="NextFireCutoffInterval" /> property. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType" /> intervalType.
		/// </para>
		/// <para>
		/// The returned value is not guaranteed to be valid until after
		/// the trigger has been added to the scheduler.
		/// </para>
        /// </remarks>
		/// <returns> the next fire time for the trigger</returns>
		/// <seealso cref="NextFireCutoffInterval" /> 
        public override DateTimeOffset? GetNextFireTimeUtc()
		{
			return nextFireTimeUtc;
		}

	    /// <summary> 
	    /// Returns the previous UTC time at which the 
	    /// <see cref="NthIncludedDayTrigger" /> fired. If the trigger has not yet 
	    /// fired, <see langword="null" /> will be returned.
	    /// </summary>
	    /// <returns> the previous fire time for the trigger</returns>
	    public override DateTimeOffset? GetPreviousFireTimeUtc()
	    {
	        return previousFireTimeUtc;
	    }


	    /// <summary>
	    /// Sets or gets the time zone in which the <see cref="FireAtTime" /> will be resolved.
	    /// If no time zone is provided, then the default time zone will be used. 
	    /// </summary>
	    /// <see cref="System.TimeZone.CurrentTimeZone" />
	    /// <see cref="FireAtTime" />
	    public virtual TimeZoneInfo TimeZone
	    {
	        get
	        {
	            if (timeZone == null)
	            {
	                timeZone = TimeZoneInfo.Local;
	            }
	            return timeZone;
	        }
	        set { timeZone = value; }
	    }


	    /// <summary>
		/// Returns the first time the <see cref="NthIncludedDayTrigger" /> will fire
		/// after the specified date.
		/// </summary>
		/// <remarks>
		/// <para> 
		/// Because of the conceptual design of <see cref="NthIncludedDayTrigger" />,
		/// it is not always possible to decide with certainty that the trigger
		/// will <i>never</i> fire again. Therefore, it will search for the next 
		/// fire time up to a given cutoff. These cutoffs can be changed by using the
		/// <see cref="NextFireCutoffInterval" /> property. The default cutoff is 12
		/// of the intervals specified by <see cref="IntervalType" /> intervalType.
		/// </para>
		/// <para>
		/// Therefore, for triggers with intervalType = 
        /// <see cref="IntervalTypeWeekly" />, if the trigger 
        /// will not fire within 12
		/// weeks after the given date/time, <see langword="null" /> will be returned. For
		/// triggers with intervalType = 
        /// <see cref="IntervalTypeMonthly" />
		/// , if the trigger will not fire within 12 
		/// months after the given date/time, <see langword="null" /> will be returned. 
		/// For triggers with intervalType = 
        /// <see cref="IntervalTypeYearly" />
		/// , if the trigger will not fire within 12
		/// years after the given date/time, <see langword="null" /> will be returned.  In 
		/// all cases, if the trigger will not fire before <see field="endTime" />, 
		/// <see langword="null" /> will be returned.
		/// </para>
        /// </remarks> 
		/// <param name="afterTimeUtc">The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> 
		/// the first time the trigger will fire following the specified date
		/// </returns>
        public override DateTimeOffset? GetFireTimeAfter(DateTimeOffset? afterTimeUtc)
		{
			if (!afterTimeUtc.HasValue)
			{
				afterTimeUtc = SystemTime.UtcNow();
			}

			if ((afterTimeUtc.Value < StartTimeUtc))
			{
				afterTimeUtc = StartTimeUtc.AddMilliseconds(-1*1000);
			}

			if (intervalType == IntervalTypeWeekly)
			{
				return GetWeeklyFireTimeAfter(afterTimeUtc.Value);
			}
			else if (intervalType == IntervalTypeMonthly)
			{
				return GetMonthlyFireTimeAfter(afterTimeUtc.Value);
			}
			else if (intervalType == IntervalTypeYearly)
			{
				return GetYearlyFireTimeAfter(afterTimeUtc.Value);
			}
			else
			{
				return null;
			}
		}

		/// <summary>
		/// Called when the <see cref="IScheduler" /> has decided to 'fire' the trigger
		/// (Execute the associated <see cref="IJob" />), in order to give the 
		/// <see cref="ITrigger" /> a chance to update itself for its next triggering 
		/// (if any).
		/// </summary>
		public override void Triggered(ICalendar cal)
		{
			calendar = cal;
			previousFireTimeUtc = nextFireTimeUtc;
			nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
		}

		/// <summary>
		/// Called by the scheduler at the time a <see cref="ITrigger" /> is first
		/// added to the scheduler, in order to have the <see cref="ITrigger" />
		/// compute its first fire time, based on any associated calendar.
		/// <para>
		/// After this method has been called, <see cref="GetNextFireTimeUtc" />
		/// should return a valid answer.
		/// </para>
		/// 
		/// </summary>
		/// <returns> the first time at which the <see cref="ITrigger" /> will be fired
		/// by the scheduler, which is also the same value 
        /// <see cref="GetNextFireTimeUtc" /> will return (until after the first 
		/// firing of the <see cref="ITrigger" />).
		/// </returns>
        public override DateTimeOffset? ComputeFirstFireTimeUtc(ICalendar cal)
		{
			calendar = cal;
            DateTimeOffset dt = StartTimeUtc.AddMilliseconds(-1 * 1000);
			nextFireTimeUtc = GetFireTimeAfter(dt);

			return nextFireTimeUtc;
		}

		/// <summary> 
		/// Called after the <see cref="IScheduler" /> has executed the 
		/// <see cref="IJobDetail" /> associated with the <see cref="ITrigger" /> in order
		/// to get the final instruction code from the trigger.
		/// </summary>
		/// <param name="jobCtx">
		/// The <see cref="IJobExecutionContext" /> that was used by the
		/// <see cref="IJob" />'s <see cref="IJob.Execute" /> method.
		/// </param>
		/// <param name="result">
		/// The <see cref="JobExecutionException" /> thrown by the
		/// <see cref="IJob" />, if any (may be <see langword="null" />)
		/// </param>
		/// <returns> one of the Trigger.INSTRUCTION_XXX constants.
		/// </returns>
        public override SchedulerInstruction ExecutionComplete(IJobExecutionContext jobCtx, JobExecutionException result)
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
		/// possible for this <see cref="ITrigger" /> to fire again.
		/// </summary>'
		/// <remarks>
		/// <para>
		/// If the returned value is <see langword="false" /> then the 
		/// <see cref="IScheduler" /> may remove the <see cref="ITrigger" /> from the
		/// <see cref="IJobStore" />
		/// </para>
		/// </remarks>
		/// <returns>
		/// A boolean indicator of whether the trigger could potentially fire
		/// again.
		/// </returns>
		public override bool GetMayFireAgain()
		{
			return GetNextFireTimeUtc().HasValue;
		}

		/// <summary> 
		/// Indicates whether <param name="misfireInstruction" /> is a valid misfire
		/// instruction for this <see cref="ITrigger" />.
		/// </summary>
        /// <returns>Whether <see param="misfireInstruction" /> is valid.</returns>
		protected override bool ValidateMisfireInstruction(int misfireInstruction)
		{
            return (misfireInstruction == Quartz.MisfireInstruction.NthIncludedDayTrigger.DoNothing) 
                || (misfireInstruction == Quartz.MisfireInstruction.NthIncludedDayTrigger.FireOnceNow)
                || (misfireInstruction == Quartz.MisfireInstruction.SmartPolicy);
		
		}

		/// <summary> Updates the <see cref="NthIncludedDayTrigger" />'s state based on the
        /// MisfireInstruction that was selected when the 
		/// <see cref="NthIncludedDayTrigger" /> was created
		/// <P>
		/// If the misfire instruction is set to MISFIRE_INSTRUCTION_SMART_POLICY,
		/// then the instruction will be interpreted as 
        /// <see cref="MisfireInstruction.NthIncludedDayTrigger.FireOnceNow" />.
		/// </P>
		/// </summary>
		/// <param name="cal">a new or updated calendar to use for the trigger
		/// </param>
		public override void UpdateAfterMisfire(ICalendar cal)
		{
			int instruction = MisfireInstruction;

			calendar = cal;

			if (instruction == Quartz.MisfireInstruction.SmartPolicy)
			{
				instruction = Quartz.MisfireInstruction.NthIncludedDayTrigger.FireOnceNow;
			}

			if (instruction == Quartz.MisfireInstruction.NthIncludedDayTrigger.DoNothing)
			{
                nextFireTimeUtc = GetFireTimeAfter(SystemTime.UtcNow());
			}
			else if (instruction == Quartz.MisfireInstruction.NthIncludedDayTrigger.FireOnceNow)
			{
				nextFireTimeUtc = SystemTime.UtcNow();
			}
		}

		/// <summary> 
		/// Updates the <see cref="NthIncludedDayTrigger" />'s state based on the 
		/// given new version of the associated <see cref="ICalendar" />. 
		/// </summary>
		/// <param name="cal">A new or updated calendar to use for the trigger</param>
		/// <param name="misfireThreshold">the amount of time that must
		/// be between &quot;now&quot; and the time the next
		/// firing of the trigger is supposed to occur.
		/// </param>
		public override void UpdateWithNewCalendar(ICalendar cal, TimeSpan misfireThreshold)
		{
		    calendar = cal;
			nextFireTimeUtc = GetFireTimeAfter(previousFireTimeUtc);

			DateTimeOffset now = SystemTime.UtcNow();
			if ((nextFireTimeUtc.HasValue) && ((nextFireTimeUtc.Value < now)))
			{
			    TimeSpan diff = now - nextFireTimeUtc.Value;
			    if (diff >= misfireThreshold)
				{
					nextFireTimeUtc = GetFireTimeAfter(nextFireTimeUtc);
				}
			}
		}

		/// <summary> 
		/// Calculates the first time an <see cref="NthIncludedDayTrigger" /> with 
		/// <c>intervalType = IntervalTypeWeekly</c> will fire 
		/// after the specified date. See <see cref="GetNextFireTimeUtc" /> for more 
		/// information.
		/// </summary>
		/// <param name="afterDateUtc">The time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified
		/// date
		/// </returns>
        private DateTimeOffset? GetWeeklyFireTimeAfter(DateTimeOffset afterDateUtc)
        {
			int currN = 0;
			int currWeek;
			int weekCount = 0;
			bool gotOne = false;

            afterDateUtc = TimeZoneInfo.ConvertTime(afterDateUtc, TimeZone);
            DateTime currCal = new DateTime(afterDateUtc.Year, afterDateUtc.Month, afterDateUtc.Day);

            // move to the first day of the week
            // TODO, we are still bound to fixed local time zone as with TimeZone property
            while (currCal.DayOfWeek != DateTimeFormatInfo.CurrentInfo.FirstDayOfWeek)
            {
                currCal = currCal.AddDays(-1);
            }

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
                    if (EndTimeUtc.HasValue && TimeZoneInfo.ConvertTimeToUtc(currCal, TimeZone) > EndTimeUtc.Value)
					{
						return null;
					}
				}

				// We found an "n" or we've checked the requisite number of weeks.
				// If we've found an "n", is it the right one? -- that is, we could
				// be looking at an nth day PRIOR to afterDateUtc
				if (currN == n)
				{
                    if (afterDateUtc < TimeZoneInfo.ConvertTimeToUtc(currCal, TimeZone))
					{
						gotOne = true;
					}
					else
					{
						// resume checking on the first day of the next week
                        // move back to the beginning of the week and add 7 days
                        // TODO, need to correlate with time zone in .NET 3.5
                        while (currCal.DayOfWeek != DateTimeFormatInfo.CurrentInfo.FirstDayOfWeek)
                        {
                            currCal = currCal.AddDays(-1);
                        }
                        currCal = currCal.AddDays(7);

                        currN = 0;
					}
				}
			}

			if (weekCount < nextFireCutoffInterval)
			{
                return TimeZoneInfo.ConvertTimeToUtc(currCal, TimeZone); 
			}
			else
			{
				return null;
			}
		}

		/// <summary> 
		/// Calculates the first UTC time an <see cref="NthIncludedDayTrigger" /> with 
		/// intervalType = <see cref="IntervalTypeMonthly" /> will fire 
		/// after the specified date. See <see cref="GetNextFireTimeUtc" /> for more 
		/// information.
		/// </summary>
		/// <param name="afterDateUtc">
		/// The UTC time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified date </returns>
        private DateTimeOffset? GetMonthlyFireTimeAfter(DateTimeOffset afterDateUtc)
		{
			int currN = 0;
            DateTimeOffset currCal = TimeZoneInfo.ConvertTime(afterDateUtc, TimeZone);
            currCal = new DateTime(currCal.Year, currCal.Month, 1, fireAtHour, fireAtMinute, fireAtSecond, 0);
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
					if (EndTimeUtc.HasValue && currCal > EndTimeUtc.Value)
					{
						return null;
					}
				}

				//We found an "n" or we've checked the requisite number of months.
				// If we've found an "n", is it the right one? -- that is, we could
				// be looking at an nth day PRIOR to afterDateUtc
				if (currN == n)
				{
					if (afterDateUtc < currCal)
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
                return TimeZoneInfo.ConvertTime(currCal, TimeZone);
			}
			else
			{
				return null;
			}
		}

		/// <summary> 
		/// Calculates the first time an <see cref="NthIncludedDayTrigger" /> with 
		/// intervalType = <see cref="IntervalTypeYearly" /> will fire 
		/// after the specified date. See <see cref="GetNextFireTimeUtc" /> for more 
		/// information.
		/// </summary>
		/// <param name="afterDateUtc">
		/// The UTC time after which to find the nearest fire time.
		/// This argument is treated as exclusive &#x8212; that is,
		/// if afterTime is a valid fire time for the trigger, it
		/// will not be returned as the next fire time.
		/// </param>
		/// <returns> the first time the trigger will fire following the specified
		/// date
		/// </returns>
        private DateTimeOffset? GetYearlyFireTimeAfter(DateTimeOffset afterDateUtc)
		{
			int currN = 0;
            DateTimeOffset currCal = TimeZoneInfo.ConvertTime(afterDateUtc, TimeZone);
            currCal = new DateTime(currCal.Year, 1, 1, fireAtHour, fireAtMinute, fireAtSecond, 0);
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
                    if (EndTimeUtc.HasValue && TimeZoneInfo.ConvertTime(currCal, TimeZone) > EndTimeUtc.Value)
                    {
						return null;
					}
				}

				//We found an "n" or we've checked the requisite number of years.
				// If we've found an "n", is it the right one? -- that is, we 
				// could be looking at an nth day PRIOR to afterDateUtc
				if (currN == n)
				{
                    if (afterDateUtc < TimeZoneInfo.ConvertTime(currCal, TimeZone))
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
                return TimeZoneInfo.ConvertTime(currCal, TimeZone);
			}
			else
			{
				return null;
			}
		}


		private int GetWeekOfYear(DateTime date)
		{
			GregorianCalendar gCal = new GregorianCalendar();
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


        /// <summary>
        /// Get a <see cref="IScheduleBuilder" /> that is configured to produce a
        /// schedule identical to this trigger's schedule.
        /// </summary>
        /// <remarks>
        /// </remarks>
	    public override IScheduleBuilder GetScheduleBuilder()
	    {
	        throw new NotImplementedException(); // TODO
	    }

        public override void SetNextFireTimeUtc(DateTimeOffset? nextFireTime)
        {
            throw new NotImplementedException();
        }

        public override void SetPreviousFireTimeUtc(DateTimeOffset? previousFireTime)
        {
            throw new NotImplementedException();
        }
	}
}
