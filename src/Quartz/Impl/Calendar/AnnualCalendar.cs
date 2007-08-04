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
using System.Collections;

using Nullables;

namespace Quartz.Impl.Calendar
{
	/// <summary>
	/// This implementation of the Calendar excludes a set of days of the year. You
	/// may use it to exclude bank holidays which are on the same date every year.
	/// </summary>
	/// <seealso cref="ICalendar" />
	/// <seealso cref="BaseCalendar" />
	/// <author>Juergen Donnerstag</author>
	[Serializable]
	public class AnnualCalendar : BaseCalendar, ICalendar
	{
        private ArrayList excludeDays = new ArrayList();

		// true, if excludeDays is sorted
		private bool dataSorted = false;

        /// <summary>
        /// Constructor
        /// </summary>
		public AnnualCalendar()
		{
		}

		/// <summary>
		/// Constructor
		/// </summary>
		/// <param name="baseCalendar">The base calendar.</param>
		public AnnualCalendar(ICalendar baseCalendar) : base(baseCalendar)
		{
		}

        /// <summary> 
        /// Get or the array which defines the exclude-value of each day of month.
        /// Setting will redefine the array of days excluded. The array must of size greater or
        /// equal 31.
        /// </summary>
        public virtual IList DaysExcluded
        {
            get { return excludeDays; }

            set
            {
                if (value == null)
                {
                    excludeDays = new ArrayList();
                }
                else
                {
                    excludeDays = new ArrayList(value);
                    dataSorted = false;
                }
            }
        }

		/// <summary>
		/// Return true, if day is defined to be exluded.
		/// </summary>
		public virtual bool IsDayExcluded(DateTime day)
		{
		    NullableDateTime d = FindExcludedDateByMonthAndDay(day);

			return d.HasValue;
		}

        protected virtual NullableDateTime FindExcludedDateByMonthAndDay(DateTime day)
        {
            int dmonth = day.Month;
            int dday = day.Day;

            if (!dataSorted)
            {
                excludeDays.Sort();
                dataSorted = true;
            } 
            
            foreach (DateTime cl in excludeDays)
            {
                // remember, the list is sorted
                if (dmonth < cl.Month)
                {
                    return null;
                }

                if (dday != cl.Day)
                {
                    continue;
                }

                if (dmonth != cl.Month)
                {
                    continue;
                }

                return cl;
            }

            // not found
            return null;
        }

		/// <summary>
		/// Redefine a certain day to be excluded (true) or included (false).
		/// </summary>
		public virtual void SetDayExcluded(DateTime day, bool exclude)
		{
            if (exclude)
			{
				if (!IsDayExcluded(day))
				{
                    excludeDays.Add(day.Date);
				}
			}
            else
            {
                // include
                // find first, year may vary
                NullableDateTime d = FindExcludedDateByMonthAndDay(day);
                if (d.HasValue)
                {
                     excludeDays.Remove(d.Value);
                }
            }
			dataSorted = false;
		}

		/// <summary>
		/// Determine whether the given time (in milliseconds) is 'included' by the
		/// Calendar.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override bool IsTimeIncluded(DateTime date)
		{
			// Test the base calendar first. Only if the base calendar not already
			// excludes the time/date, continue evaluating this calendar instance.
			if (!base.IsTimeIncluded(date))
			{
				return false;
			}

			return !(IsDayExcluded(date));
		}

		/// <summary>
		/// Determine the next time (in milliseconds) that is 'included' by the
		/// Calendar after the given time. Return the original value if timeStamp is
		/// included. Return 0 if all days are excluded.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override DateTime GetNextIncludedTime(DateTime timeStamp)
		{
			// Call base calendar implementation first
			DateTime baseTime = base.GetNextIncludedTime(timeStamp);
			if ((baseTime != DateTime.MinValue) && (baseTime > timeStamp))
			{
				timeStamp = baseTime;
			}

			// Get timestamp for 00:00:00
			DateTime day = BuildHoliday(timeStamp);

			if (IsDayExcluded(day) == false)
			{
				// return the original value
				return timeStamp;
			}

			while (IsDayExcluded(day))
			{
				day = day.AddDays(1);
			}

			return day;
		}
	}
}