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
	/// This implementation of the Calendar excludes a set of days of the week. You
	/// may use it to exclude weekends for example. But you may define any day of
	/// the week.
	/// </summary>
	/// <seealso cref="ICalendar" />
	/// <seealso cref="BaseCalendar" />
	/// <author>Juergen Donnerstag</author>
	[Serializable]
	public class WeeklyCalendar : BaseCalendar, ICalendar
	{
		/// <summary> 
		/// Get the array with the week days.
		/// Setting will redefine the array of days excluded. The array must of size greater or
		/// equal 8. java.util.Calendar's constants like MONDAY should be used as
		/// index. A value of true is regarded as: exclude it.
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

		// An array to store the week days which are to be excluded.
		// java.util.Calendar.MONDAY etc. are used as index.
		private bool[] excludeDays = new bool[8];

		// Will be set to true, if all week days are excluded
		private bool excludeAll = false;

		/// <summary> <p>
		/// Constructor
		/// </p>
		/// </summary>
		public WeeklyCalendar() : base()
		{
			Init();
		}

		/// <summary> <p>
		/// Constructor
		/// </p>
		/// </summary>
		public WeeklyCalendar(ICalendar baseCalendar) : base(baseCalendar)
		{
			Init();
		}

		/// <summary>
		/// Initialize internal variables
		/// </summary>
		private void Init()
		{
			excludeDays[(int) DayOfWeek.Sunday] = true;
			excludeDays[(int) DayOfWeek.Saturday] = true;
			excludeAll = AreAllDaysExcluded();
		}

		/// <summary> 
		/// Return true, if wday (see Calendar.get()) is defined to be exluded. E. g.
		/// saturday and sunday.
		/// </summary>
		public virtual bool IsDayExcluded(int wday)
		{
			return excludeDays[wday];
		}

		/// <summary>
		/// Redefine a certain day of the week to be excluded (true) or included
		/// (false). Use java.util.Calendar's constants like MONDAY to determine the
		/// wday.
		/// </summary>
		public virtual void SetDayExcluded(int wday, bool exclude)
		{
			excludeDays[wday] = exclude;
			excludeAll = AreAllDaysExcluded();
		}

		/// <summary>
		/// Check if all week ays are excluded. That is no day is included.
		/// </summary>
		public virtual bool AreAllDaysExcluded()
		{
			if (IsDayExcluded((int) DayOfWeek.Sunday) == false)
			{
				return false;
			}

			if (IsDayExcluded((int) DayOfWeek.Monday) == false)
			{
				return false;
			}

			if (IsDayExcluded((int) DayOfWeek.Tuesday) == false)
			{
				return false;
			}

			if (IsDayExcluded((int) DayOfWeek.Wednesday) == false)
			{
				return false;
			}

			if (IsDayExcluded((int) DayOfWeek.Thursday) == false)
			{
				return false;
			}

			if (IsDayExcluded((int) DayOfWeek.Friday) == false)
			{
				return false;
			}

			if (IsDayExcluded((int) DayOfWeek.Saturday) == false)
			{
				return false;
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
		public override bool IsTimeIncluded(DateTime time)
		{
			if (excludeAll)
			{
				return false;
			}

			// Test the base calendar first. Only if the base calendar not already
			// excludes the time/date, continue evaluating this calendar instance.
			if (!base.IsTimeIncluded(time))
			{
				return false;
			}

			int wday = (int) time.DayOfWeek;

			return !(IsDayExcluded(wday));
		}

		/// <summary>
		/// Determine the next time (in milliseconds) that is 'included' by the
		/// Calendar after the given time. Return the original value if timeStamp is
		/// included. Return DateTime.MinValue if all days are excluded.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override DateTime GetNextIncludedTime(DateTime time)
		{
			if (excludeAll == true)
			{
				return DateTime.MinValue;
			}

			// Call base calendar implementation first
			DateTime baseTime = base.GetNextIncludedTime(time);
			if ((baseTime != DateTime.MinValue) && (baseTime > time))
			{
				time = baseTime;
			}

			// Get timestamp for 00:00:00
			DateTime d = BuildHoliday(time);

			if (!IsDayExcluded((int) d.DayOfWeek))
			{
				return time;
			} // return the original value

			while (IsDayExcluded((int) d.DayOfWeek))
			{
				d = d.AddDays(1);
			}

			return d;
		}
	}
}