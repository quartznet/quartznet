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
using System.Collections;

using System;

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

        // year to use as fixed year
	    private const int FixedYear = 2000;

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
                }
                dataSorted = false;
            }
        }

		/// <summary>
		/// Return true, if day is defined to be exluded.
		/// </summary>
		public virtual bool IsDayExcluded(DateTime day)
		{
            return IsDateTimeExcluded(day);
		}

        protected virtual bool IsDateTimeExcluded(DateTime day)
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
                    return false;
                }

                if (dday != cl.Day)
                {
                    continue;
                }

                if (dmonth != cl.Month)
                {
                    continue;
                }

                return true;
            }

            // not found
            return false;
        }

		/// <summary>
		/// Redefine a certain day to be excluded (true) or included (false).
		/// </summary>
		public virtual void SetDayExcluded(DateTime day, bool exclude)
		{
            DateTime d = new DateTime(FixedYear, day.Month, day.Day);

            if (exclude)
			{
				if (!IsDayExcluded(day))
				{
                    excludeDays.Add(d);
				}
			}
            else
            {
                // include
                if (IsDayExcluded(day))
                {
                     excludeDays.Remove(d);
                }
            }
			dataSorted = false;
		}

		/// <summary>
		/// Determine whether the given UTC time (in milliseconds) is 'included' by the
		/// Calendar.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override bool IsTimeIncluded(DateTime dateUtc)
		{
			// Test the base calendar first. Only if the base calendar not already
			// excludes the time/date, continue evaluating this calendar instance.
			if (!base.IsTimeIncluded(dateUtc))
			{
				return false;
			}

#if !NET_35
			return !(IsDayExcluded(TimeZone.ToLocalTime(dateUtc)));
#else
            return !(IsDayExcluded(TimeZoneInfo.ConvertTimeFromUtc(dateUtc, TimeZoneInfo.Local)));
#endif
		}

		/// <summary>
		/// Determine the next UTC time (in milliseconds) that is 'included' by the
		/// Calendar after the given time. Return the original value if timeStampUtc is
		/// included. Return 0 if all days are excluded.
		/// <p>
		/// Note that this Calendar is only has full-day precision.
		/// </p>
		/// </summary>
		public override DateTime GetNextIncludedTimeUtc(DateTime timeStampUtc)
		{
			// Call base calendar implementation first
			DateTime baseTime = base.GetNextIncludedTimeUtc(timeStampUtc);
			if ((baseTime != DateTime.MinValue) && (baseTime > timeStampUtc))
			{
				timeStampUtc = baseTime;
			}

			// Get timestamp for 00:00:00
#if !NET_35
            DateTime day = TimeZone.ToLocalTime(new DateTime(timeStampUtc.Year, timeStampUtc.Month, timeStampUtc.Day));
#else
            DateTime day = TimeZoneInfo.ConvertTimeFromUtc(new DateTime(timeStampUtc.Year, timeStampUtc.Month, timeStampUtc.Day), TimeZoneInfo.Local);
#endif

			if (!IsDayExcluded(day))
			{
				// return the original value
				return timeStampUtc;
			}

			while (IsDayExcluded(day))
			{
				day = day.AddDays(1);
			}

#if !NET_35
			return day.ToUniversalTime();
#else
            return TimeZoneInfo.ConvertTimeToUtc(day);
#endif
		}
	    
	    
	    public override int GetHashCode()
	    {
            int baseHash = 0;
            if (GetBaseCalendar() != null)
                baseHash = GetBaseCalendar().GetHashCode();
	        
	        return excludeDays.GetHashCode() + 5*baseHash;
	    }
	    
	    public bool Equals(AnnualCalendar obj)
	    {
	        if (obj == null)
	            return false;
             bool toReturn = GetBaseCalendar() != null ? 
                             GetBaseCalendar().Equals(obj.GetBaseCalendar()) : true;

             toReturn = toReturn && (DaysExcluded.Count == obj.DaysExcluded.Count);
             if (toReturn)
            {
                foreach (DateTime date in DaysExcluded)
                    toReturn = toReturn && obj.DaysExcluded.Contains(date);
            }
            return toReturn;
	    }
	    
	    
	    public override bool Equals(object obj)
	    {
	        if ((obj == null) || !(obj is AnnualCalendar))
	            return false;
            else    
	             return Equals((AnnualCalendar) obj);
	        
	    }
	}
}
