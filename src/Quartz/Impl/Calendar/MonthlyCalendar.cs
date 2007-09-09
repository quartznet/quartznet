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
* and Juergen Donnerstag (c) 2002, EDS 2002
*/
using System;

namespace Quartz.Impl.Calendar
{
	/// <summary>
	/// This implementation of the Calendar excludes a set of days of the month. You
	/// may use it to exclude every 1. of each month for example. But you may define
	/// any day of a month.
	/// </summary>
	/// <seealso cref="ICalendar" />
	/// <seealso cref="BaseCalendar" />
	/// <author>Juergen Donnerstag</author>
	[Serializable]
	public class MonthlyCalendar : BaseCalendar, ICalendar
	{
        private const int MAX_DAYS_IN_MONTH = 31;

		/// <summary>
		/// Get or set the array which defines the exclude-value of each day of month
		/// Setting will redefine the array of days excluded. The array must of size greater or
		/// equal 31.
		/// </summary>
		public virtual bool[] DaysExcluded
		{
			get { return excludeDays; }

			set
			{
				if (value == null)
				{
					return;
				}

				excludeDays = value;
				excludeAll = AreAllDaysExcluded();
			}
		}

		// An array to store a months days which are to be excluded.
		// Day as index.
        private bool[] excludeDays = new bool[MAX_DAYS_IN_MONTH];

		// Will be set to true, if all week days are excluded
		private bool excludeAll = false;

		/// <summary> <p>
		/// Constructor
		/// </p>
		/// </summary>
		public MonthlyCalendar() : base()
		{
			Init();
		}

		/// <summary>
		/// Constructor
		/// </summary>
		public MonthlyCalendar(ICalendar baseCalendar) : base(baseCalendar)
		{
			Init();
		}

		/// <summary> 
		/// Initialize internal variables
		/// </summary>
		private void Init()
		{
			// all days are included by default
			excludeAll = AreAllDaysExcluded();
		}

		/// <summary>
		/// Return true, if mday is defined to be exluded.
		/// </summary>
		public virtual bool IsDayExcluded(int day)
		{
			if ((day < 1) || (day > MAX_DAYS_IN_MONTH)) 
			{
				throw new ArgumentException(
					string.Format("The day parameter must be in the range of 1 to {0}", MAX_DAYS_IN_MONTH));
			}
			return excludeDays[day - 1];
		}

		/// <summary>
		/// Redefine a certain day of the month to be excluded (true) or included
		/// (false).
		/// </summary>
		public virtual void SetDayExcluded(int day, bool exclude)
		{
			excludeDays[day - 1] = exclude;
			excludeAll = AreAllDaysExcluded();
		}

		/// <summary>
		/// Check if all days are excluded. That is no day is included.
		/// </summary>
		/// <returns> boolean
		/// </returns>
		public virtual bool AreAllDaysExcluded()
		{
			for (int i = 1; i <= 31; i++)
			{
				if (IsDayExcluded(i) == false)
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Determine whether the given time (in milliseconds) is 'included' by the
		/// Calendar.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override bool IsTimeIncluded(DateTime timeStampUtc)
		{
			if (excludeAll)
			{
				return false;
			}

			// Test the base calendar first. Only if the base calendar not already
			// excludes the time/date, continue evaluating this calendar instance.
			if (!base.IsTimeIncluded(timeStampUtc))
			{
				return false;
			}

			int day = timeStampUtc.Day;

			return !(IsDayExcluded(day));
		}

		/// <summary>
		/// Determine the next time (in milliseconds) that is 'included' by the
		/// Calendar after the given time. Return the original value if timeStamp is
		/// included. Return DateTime.MinValue if all days are excluded.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override DateTime GetNextIncludedTimeUtc(DateTime timeUtc)
		{
			if (excludeAll)
			{
				return DateTime.MinValue;
			}

			// Call base calendar implementation first
			DateTime baseTime = base.GetNextIncludedTimeUtc(timeUtc);
			if ((baseTime != DateTime.MinValue) && (baseTime > timeUtc))
			{
				timeUtc = baseTime;
			}

			// Get timestamp for 00:00:00
			DateTime newTimeStamp = TimeZone.ToLocalTime(timeUtc.Date);
			int day = newTimeStamp.Day;

			if (!IsDayExcluded(day))
			{
				return timeUtc;
			} // return the original value

			while (IsDayExcluded(day))
			{
				newTimeStamp = newTimeStamp.AddDays(1);
				day = (int) newTimeStamp.DayOfWeek + 1;
			}

			return newTimeStamp.ToUniversalTime();
	
		}

        public override int GetHashCode()
        {
            int baseHash = 0;
            if (GetBaseCalendar() != null)
                baseHash = GetBaseCalendar().GetHashCode();

            return DaysExcluded.GetHashCode() + 5 * baseHash;
        }

        public bool Equals(MonthlyCalendar obj)
        {
            //a little trick here : Monthly calendar knows nothing
            //about the precise month it is dealing with, so
            //FebruaryCalendars will be only equal if their
            // 31st days are equally included ))
            //but that's not going to be a problem since 
            //there's no need to redefine default value of false
            //for such days
            if (obj == null)
                return false;
            bool baseEqual = GetBaseCalendar() != null ?
                             GetBaseCalendar().Equals(obj.GetBaseCalendar()) : true;

            return baseEqual && (ArraysEqualElementsOnEqualPlaces(DaysExcluded,obj.DaysExcluded)
            );

        }


        public override bool Equals(object obj)
        {
            if ((obj == null) || !(obj is MonthlyCalendar))
                return false;
            else
                return Equals((MonthlyCalendar)obj);


        }
 

	}
}
