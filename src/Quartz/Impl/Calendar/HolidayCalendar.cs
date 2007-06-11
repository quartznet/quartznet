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

using Quartz.Collection;

namespace Quartz.Impl.Calendar
{
	/// <summary>
	/// This implementation of the Calendar stores a list of holidays (full days
	/// that are excluded from scheduling).
	/// <p>
	/// The implementation DOES take the year into consideration, so if you want to
	/// exclude July 4th for the next 10 years, you need to add 10 entries to the
	/// exclude list.
	/// </p>
	/// </summary>
	/// <author>Sharada Jambula</author>
	/// <author>Juergen Donnerstag</author>
	[Serializable]
	public class HolidayCalendar : BaseCalendar, ICalendar
	{
		/// <summary>
		/// Returns a <see cref="ISortedSet" /> of Dates representing the excluded
		/// days. Only the month, day and year of the returned dates are
		/// significant.
		/// </summary>
		public virtual ISortedSet ExcludedDates
		{
			get { return TreeSet.UnmodifiableTreeSet(dates); }
		}

		// A sorted set to store the holidays
		private TreeSet dates = new TreeSet();

		/// <summary> Constructor</summary>
		public HolidayCalendar()
		{
		}

		/// <summary> Constructor</summary>
		public HolidayCalendar(ICalendar baseCalendar)
		{
			CalendarBase = baseCalendar;
		}

		/// <summary>
		/// Determine whether the given time (in milliseconds) is 'included' by the
		/// Calendar.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override bool IsTimeIncluded(DateTime timeStamp)
		{
			if (!base.IsTimeIncluded(timeStamp))
			{
				return false;
			}

			DateTime lookFor = BuildHoliday(timeStamp);

			return !(dates.Contains(lookFor));
		}

		/// <summary>
		/// Determine the next time (in milliseconds) that is 'included' by the
		/// Calendar after the given time.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override DateTime GetNextIncludedTime(DateTime time)
		{
			// Call base calendar implementation first
			DateTime baseTime = base.GetNextIncludedTime(time);
			if ((time != DateTime.MinValue) && (baseTime > time))
			{
				time = baseTime;
			}

			// Get timestamp for 00:00:00
			DateTime day = BuildHoliday(time);

			while (!IsTimeIncluded(day))
			{
				day = day.AddDays(1);
			}

			return day;
		}

		/// <summary>
		/// Add the given Date to the list of excluded days. Only the month, day and
		/// year of the returned dates are significant.
		/// </summary>
		public virtual void AddExcludedDate(DateTime excludedDate)
		{
			DateTime date = BuildHoliday(excludedDate);
			dates.Add(date);
		}

		/// <summary>
		/// Removes the excluded date.
		/// </summary>
		/// <param name="dateToRemove">The date to remove.</param>
		public virtual void RemoveExcludedDate(DateTime dateToRemove)
		{
			DateTime date = BuildHoliday(dateToRemove);
			dates.Remove(date);
		}
	}
}