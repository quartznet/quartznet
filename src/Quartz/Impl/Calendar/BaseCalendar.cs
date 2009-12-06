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
#if NET_35
using TimeZone = System.TimeZoneInfo;
#endif

namespace Quartz.Impl.Calendar
{
	/// <summary>
	/// This implementation of the Calendar may be used (you don't have to) as a
	/// base class for more sophisticated one's. It merely implements the base
	/// functionality required by each Calendar.
	/// </summary>
	/// <remarks>
	/// Regarded as base functionality is the treatment of base calendars. Base
	/// calendar allow you to chain (stack) as much calendars as you may need. For
	/// example to exclude weekends you may use WeeklyCalendar. In order to exclude
	/// holidays as well you may define a WeeklyCalendar instance to be the base
	/// calendar for HolidayCalendar instance.
	/// </remarks>
	/// <seealso cref="ICalendar" /> 
	/// <author>Juergen Donnerstag</author>
	/// <author>James House</author>
	[Serializable]
	public class BaseCalendar : ICalendar
	{
        // A optional base calendar.
        private ICalendar baseCalendar;
        private string description;
        private TimeZone timeZone;

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
        /// </summary>
        public BaseCalendar()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
        /// </summary>
        /// <param name="baseCalendar">The base calendar.</param>
        public BaseCalendar(ICalendar baseCalendar)
        {
            CalendarBase = baseCalendar;
        }


        /// <summary>
        /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
        /// </summary>
        /// <param name="timeZone">The time zone.</param>
        public BaseCalendar(TimeZone timeZone)
	    {
	        this.timeZone = timeZone;
	    }

        /// <summary>
        /// Initializes a new instance of the <see cref="BaseCalendar"/> class.
        /// </summary>
        /// <param name="baseCalendar">The base calendar.</param>
        /// <param name="timeZone">The time zone.</param>
        public BaseCalendar(ICalendar baseCalendar, TimeZone timeZone)
        {
	        this.baseCalendar = baseCalendar;
	        this.timeZone = timeZone;
	    }

        /// <summary>
        /// Gets or sets the time zone.
        /// </summary>
        /// <value>The time zone.</value>
	    public virtual TimeZone TimeZone
	    {
	        get
	        {
                if (timeZone == null)
                {
#if !NET_35
                    timeZone = System.TimeZone.CurrentTimeZone;
#else
                    timeZone = TimeZoneInfo.Local;
#endif
                }
                return timeZone;
            }
            set { timeZone = value; }
        }

	    /// <summary>
        /// checks whether two arrays have 
        /// the same length and 
        /// for any given place there are equal elements 
        /// in both arrays
        /// </summary>
        /// <returns></returns>
        protected static bool ArraysEqualElementsOnEqualPlaces(Array array1, Array array2)
        {
            if (array1.Length != array2.Length)
            {
                return false;
            }
            bool toReturn = true;
            for (int i = 0; i < array1.Length; i++)
            {
                toReturn = toReturn && (array1.GetValue(i).Equals(array2.GetValue(i)));
            }
            return toReturn;
        }


		/// <summary> 
		/// Gets or sets the description given to the <see cref="ICalendar" /> instance by
		/// its creator (if any).
		/// </summary>
		public virtual string Description
		{
			get { return description; }
			set { description = value; }
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
		public virtual bool IsTimeIncluded(DateTime timeStampUtc)
		{
			if (timeStampUtc == DateTime.MinValue)
			{
				throw new ArgumentException("timeStampUtc must be greater 0");
			}

			if (baseCalendar != null)
			{
				if (baseCalendar.IsTimeIncluded(timeStampUtc) == false)
				{
					return false;
				}
			}

			return true;
		}

		/// <summary>
		/// Determine the next UTC time (in milliseconds) that is 'included' by the
		/// Calendar after the given time. Return the original value if timeStamp is
		/// included. Return 0 if all days are excluded.
		/// </summary>
		/// <seealso cref="ICalendar.GetNextIncludedTimeUtc" />
		public virtual DateTime GetNextIncludedTimeUtc(DateTime timeUtc)
		{
			if (timeUtc == DateTime.MinValue)
			{
				throw new ArgumentException("timeStamp must be greater DateTime.MinValue");
			}

			if (baseCalendar != null)
			{
				return baseCalendar.GetNextIncludedTimeUtc(timeUtc);
			}

			return timeUtc;
		}


	    /// <summary>
	    /// Creates a new object that is a copy of the current instance.
	    /// </summary>
	    /// <returns>A new object that is a copy of this instance.</returns>
	    public virtual object Clone()
	    {
	        BaseCalendar clone = (BaseCalendar) MemberwiseClone();
            if (GetBaseCalendar() != null)
            {
                clone.baseCalendar = (ICalendar) GetBaseCalendar().Clone();
            }
            return clone;
	    }
	}
}
