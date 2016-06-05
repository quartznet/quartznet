#region License
/* 
 * All content copyright Terracotta, Inc., unless otherwise indicated. All rights reserved. 
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
#endregion

using System;
using System.Runtime.Serialization;
using System.Security;

using Quartz.Collection;
using Quartz.Util;

namespace Quartz.Impl.Calendar
{
	/// <summary>
	/// This implementation of the Calendar stores a list of holidays (full days
	/// that are excluded from scheduling).
    /// </summary>
	/// <remarks>
	/// The implementation DOES take the year into consideration, so if you want to
	/// exclude July 4th for the next 10 years, you need to add 10 entries to the
	/// exclude list.
	/// </remarks>
	/// <author>Sharada Jambula</author>
	/// <author>Juergen Donnerstag</author>
	/// <author>Marko Lahma (.NET)</author>
	[Serializable]
	public class HolidayCalendar : BaseCalendar
	{
		/// <summary>
		/// Returns a <see cref="ISortedSet&lt;DateTime&gt;" /> of Dates representing the excluded
		/// days. Only the month, day and year of the returned dates are
		/// significant.
		/// </summary>
		public virtual ISortedSet<DateTime> ExcludedDates
		{
			get { return new TreeSet<DateTime>(dates); }
		}

		// A sorted set to store the holidays
		private TreeSet<DateTime> dates = new TreeSet<DateTime>();

        /// <summary>
        /// Initializes a new instance of the <see cref="HolidayCalendar"/> class.
        /// </summary>
		public HolidayCalendar()
		{
		}

        /// <summary>
        /// Initializes a new instance of the <see cref="HolidayCalendar"/> class.
        /// </summary>
        /// <param name="baseCalendar">The base calendar.</param>
		public HolidayCalendar(ICalendar baseCalendar)
		{
			CalendarBase = baseCalendar;
		}
        
        /// <summary>
        /// Serialization constructor.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected HolidayCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
        {
            int version;
            try
            {
                version = info.GetInt32("version");
            }
            catch
            {
                version = 0;
            }

            switch (version)
            {
                case 0:
                    object o = info.GetValue("dates", typeof(object));
                    TreeSet oldTreeset = o as TreeSet;
                    if (oldTreeset != null)
                    {
                        foreach (DateTime dateTime in oldTreeset)
                        {
                            dates.Add(dateTime);
                        }
                    }
                    else
                    {
                        // must be generic treeset 
                        dates = (TreeSet<DateTime>) o;
                    }
                    break;
                case 1:
                    dates = (TreeSet<DateTime>) info.GetValue("dates", typeof(TreeSet<DateTime>));
                    break;
                case 2:
                    dates = new TreeSet<DateTime>((DateTime[]) info.GetValue("dates", typeof(DateTime[])));
                    break;
                default:
                    throw new NotSupportedException("Unknown serialization version");
            }

        }

        [SecurityCritical]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("version", 2);
            info.AddValue("dates", dates.ToArray());
        }

		/// <summary>
		/// Determine whether the given time (in milliseconds) is 'included' by the
		/// Calendar.
		/// <para>
		/// Note that this Calendar is only has full-day precision.
		/// </para>
		/// </summary>
        public override bool IsTimeIncluded(DateTimeOffset timeStampUtc)
		{
			if (!base.IsTimeIncluded(timeStampUtc))
			{
				return false;
			}

            //apply the timezone
            timeStampUtc = TimeZoneUtil.ConvertTime(timeStampUtc, this.TimeZone);
            DateTime lookFor = timeStampUtc.Date;

			return !(dates.Contains(lookFor));
		}

		/// <summary>
		/// Determine the next time (in milliseconds) that is 'included' by the
		/// Calendar after the given time.
		/// <para>
		/// Note that this Calendar is only has full-day precision.
		/// </para>
		/// </summary>
        public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
		{
			// Call base calendar implementation first
            DateTimeOffset baseTime = base.GetNextIncludedTimeUtc(timeUtc);
            if ((timeUtc != DateTimeOffset.MinValue) && (baseTime > timeUtc))
			{
				timeUtc = baseTime;
			}

            //apply the timezone
            timeUtc = TimeZoneUtil.ConvertTime(timeUtc, this.TimeZone);

            // Get timestamp for 00:00:00, with the correct timezone offset
            DateTimeOffset day = new DateTimeOffset(timeUtc.Date, timeUtc.Offset);

			while (!IsTimeIncluded(day))
			{
				day = day.AddDays(1);
			}

            return day;
		}

	    /// <summary>
	    /// Creates a new object that is a copy of the current instance.
	    /// </summary>
	    /// <returns>A new object that is a copy of this instance.</returns>
	    public override object Clone()
	    {
            HolidayCalendar clone = (HolidayCalendar) base.Clone();
            clone.dates = new TreeSet<DateTime>(dates);
            return clone;
	    }

	    /// <summary>
		/// Add the given Date to the list of excluded days. Only the month, day and
		/// year of the returned dates are significant.
		/// </summary>
		public virtual void AddExcludedDate(DateTime excludedDateUtc)
		{
			DateTime date = excludedDateUtc.Date;
			dates.Add(date);
		}

		/// <summary>
		/// Removes the excluded date.
		/// </summary>
		/// <param name="dateToRemoveUtc">The date to remove.</param>
		public virtual void RemoveExcludedDate(DateTime dateToRemoveUtc)
		{
			DateTime date = dateToRemoveUtc.Date;
			dates.Remove(date);
		}

        public override int GetHashCode()
        {
            int baseHash = 0;
            if (GetBaseCalendar() != null)
            {
                baseHash = GetBaseCalendar().GetHashCode();
            }

            return ExcludedDates.GetHashCode() + 5 * baseHash;
        }

        public bool Equals(HolidayCalendar obj)
        {
            if (obj == null)
            {
                return false;
            }

            bool baseEqual = GetBaseCalendar() == null || GetBaseCalendar().Equals(obj.GetBaseCalendar());

            return baseEqual && (ExcludedDates.Equals(obj.ExcludedDates));

        }


        public override bool Equals(object obj)
        {
            if (!(obj is HolidayCalendar))
            {
                return false;
            }
            
            return Equals((HolidayCalendar)obj);
        }
	}
}
