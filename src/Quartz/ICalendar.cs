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

namespace Quartz
{
	/// <summary> 
	/// An interface to be implemented by objects that define spaces of time that
	/// should be included or excluded from a <code>Trigger</code>'s
	/// normal 'firing' schedule.
	/// </summary>
	/// <author>James House</author>
	/// <author>Juergen Donnerstag</author>
	public interface ICalendar
	{
		/// <summary> 
		/// Gets or sets a description for the <code>Calendar</code> instance - may be
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
		/// Determine whether the given time  is 'included' by the
		/// Calendar.
		/// </summary>
		bool IsTimeIncluded(DateTime time);

		/// <summary>
		/// Determine the next time that is 'included' by the
		/// Calendar after the given time.
		/// </summary>
		DateTime GetNextIncludedTime(DateTime time);
	}
}