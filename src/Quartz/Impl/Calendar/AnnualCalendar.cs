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
		/// <summary> 
		/// Get or the array which defines the exclude-value of each day of month.
		/// Setting will redefine the array of days excluded. The array must of size greater or
		/// equal 31.
		/// </summary>
		public virtual ArrayList DaysExcluded
		{
			get { return excludeDays; }

			set
			{
				if (value == null)
				{
					excludeDays = new ArrayList();
				}

				excludeDays = value;
				dataSorted = false;
			}
		}

		private ArrayList excludeDays = new ArrayList();

		// true, if excludeDays is sorted
		private bool dataSorted = false;

		/// <summary> <p>
		/// Constructor
		/// </p>
		/// </summary>
		public AnnualCalendar() : base()
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
		/// Return true, if day is defined to be exluded.
		/// </summary>
		public virtual bool IsDayExcluded(NullableDateTime day)
		{
			if (day == null || !day.HasValue)
			{
				throw new ArgumentException("Parameter day must not be null");
			}

			int dmonth = day.Value.Month;
			int dday = day.Value.Day;

			if (!dataSorted)
			{
				excludeDays.Sort();
				dataSorted = true;
			}

			foreach (NullableDateTime cl in excludeDays)
			{
				// remember, the list is sorted
				if (dmonth < cl.Value.Month)
				{
					return false;
				}

				if (dday != cl.Value.Day)
				{
					continue;
				}

				if (dmonth != cl.Value.Month)
				{
					continue;
				}

				return true;
			}

			return false;
		}

		/// <summary>
		/// Redefine a certain day to be excluded (true) or included (false).
		/// </summary>
		public virtual void SetDayExcluded(NullableDateTime day, bool exclude)
		{
			if (IsDayExcluded(day))
			{
				return;
			}

			excludeDays.Add(day);
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