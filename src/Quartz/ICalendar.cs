/* 
* Copyright 2004-2009 James House 
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

using Quartz.Impl.Calendar;

namespace Quartz
{
	/// <summary> 
	///  An interface to be implemented by objects that define spaces of time during 
    /// which an associated <see cref="Trigger" /> may fire. 
    /// </summary>
    /// <remarks>
    /// Calendars do not  define actual fire times, but rather are used to limit a 
    /// <see cref="Trigger" />  from firing on its normal schedule if necessary. Most 
    /// Calendars include all  times by default and allow the user to specify times to
    /// exclude. As such, it  is often useful to think of Calendars as being used to
    /// <i>exclude</i> a block of time, as opposed to <i>include</i> 
    /// a block of time. (i.e. the  schedule &quot;fire every five minutes except on Sundays&quot; could be 
    /// implemented with a <see cref="SimpleTrigger" /> and a <see cref="WeeklyCalendar" /> which excludes Sundays)
    /// </remarks>
	/// <author>James House</author>
	/// <author>Juergen Donnerstag</author>
	public interface ICalendar
	{
		/// <summary> 
		/// Gets or sets a description for the <see cref="ICalendar" /> instance - may be
		/// useful for remembering/displaying the purpose of the calendar, though
		/// the description has no meaning to Quartz.
		/// </summary>
		string Description { get; set; }

		/// <summary>
		/// Set a new base calendar or remove the existing one.
		/// Get the base calendar.
		/// </summary>
		ICalendar CalendarBase { set; get; }

		/// <summary>
		/// Determine whether the given UTC time  is 'included' by the
		/// Calendar.
		/// </summary>
		bool IsTimeIncluded(DateTime timeUtc);

		/// <summary>
		/// Determine the next UTC time that is 'included' by the
		/// Calendar after the given UTC time.
		/// </summary>
		DateTime GetNextIncludedTimeUtc(DateTime timeUtc);
	}
}