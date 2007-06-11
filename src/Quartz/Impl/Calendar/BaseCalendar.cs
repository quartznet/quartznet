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
	/// This implementation of the Calendar may be used (you don't have to) as a
	/// base class for more sophisticated one's. It merely implements the base
	/// functionality required by each Calendar.
	/// <p>
	/// Regarded as base functionality is the treatment of base calendars. Base
	/// calendar allow you to chain (stack) as much calendars as you may need. For
	/// example to exclude weekends you may use WeeklyCalendar. In order to exclude
	/// holidays as well you may define a WeeklyCalendar instance to be the base
	/// calendar for HolidayCalendar instance.
	/// </p>
	/// </summary>
	/// <seealso cref="ICalendar" /> 
	/// <author>Juergen Donnerstag</author>
	/// <author>James House</author>
	[Serializable]
	public class BaseCalendar : ICalendar
	{
		/// <summary> 
		/// Gets or sets the description given to the <see cref="ICalendar" /> instance by
		/// its creator (if any).
		/// </summary>
		public virtual string Description
		{
			get { return description; }
			set { description = value; }
		}

		// A optional base calendar.
		private ICalendar baseCalendar;
		private string description;

		/// <summary> <p>
		/// Default Constructor
		/// </p>
		/// </summary>
		public BaseCalendar()
		{
		}

		/// <summary> <p>
		/// Constructor
		/// </p>
		/// </summary>
		public BaseCalendar(ICalendar baseCalendar)
		{
			CalendarBase = baseCalendar;
		}

		/// <summary>
		/// Set a new base calendar or remove the existing one
		/// </summary>
		/// <value></value>
		public ICalendar CalendarBase
		{
			set { baseCalendar = value; }
			get { return baseCalendar; }
		}

		/// <summary>
		/// Get the base calendar. Will be null, if not set.
		/// </summary>
		public ICalendar GetBaseCalendar()
		{
			return baseCalendar;
		}

		/// <summary>
		/// Check if date/time represented by timeStamp is included. If included
		/// return true. The implementation of BaseCalendar simply calls the base
		/// calendars IsTimeIncluded() method if base calendar is set.
		/// </summary>
		/// <seealso cref="ICalendar.IsTimeIncluded" />
		public virtual bool IsTimeIncluded(DateTime timeStamp)
		{
			if (timeStamp == DateTime.MinValue)
			{
				throw new ArgumentException("timeStamp must be greater 0");
			}

			if (baseCalendar != null)
			{
				if (baseCalendar.IsTimeIncluded(timeStamp) == false)
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Determine the next time (in milliseconds) that is 'included' by the
		/// Calendar after the given time. Return the original value if timeStamp is
		/// included. Return 0 if all days are excluded.
		/// </summary>
		/// <seealso cref="ICalendar.GetNextIncludedTime" />
		public virtual DateTime GetNextIncludedTime(DateTime time)
		{
			if (time == DateTime.MinValue)
			{
				throw new ArgumentException("timeStamp must be greater 0");
			}

			if (baseCalendar != null)
			{
				return baseCalendar.GetNextIncludedTime(time);
			}

			return time;
		}

		/// <summary>
		/// Utility method. Return the date of excludeDate. The time fraction will
		/// be reset to 00.00:00.
		/// </summary>
		public static DateTime BuildHoliday(DateTime date)
		{
			return date.Date;
		}

	}
}