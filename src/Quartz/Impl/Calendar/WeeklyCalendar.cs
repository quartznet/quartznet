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

using Quartz.Util;

using System;
using System.Runtime.Serialization;
using System.Security;

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
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class WeeklyCalendar : BaseCalendar
    {
        // An array to store the week days which are to be excluded.
        // DayOfWeek enumeration values are used as index.
        private bool[] excludeDays = new bool[7];

        // Will be set to true, if all week days are excluded
        private bool excludeAll;

        /// <summary>
        /// Initializes a new instance of the <see cref="WeeklyCalendar"/> class.
        /// </summary>
        public WeeklyCalendar()
        {
            Init();
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="WeeklyCalendar"/> class.
        /// </summary>
        /// <param name="baseCalendar">The base calendar.</param>
        public WeeklyCalendar(ICalendar baseCalendar) : base(baseCalendar)
        {
            Init();
        }

        /// <summary>
        /// Serialization constructor.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected WeeklyCalendar(SerializationInfo info, StreamingContext context) : base(info, context)
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
                case 1:
                    excludeDays = (bool[]) info.GetValue("excludeDays", typeof (bool[]));
                    excludeAll = (bool) info.GetValue("excludeAll", typeof (bool));
                    break;
                default:
                    throw new NotSupportedException("Unknown serialization version");
            }
        }

        [SecurityCritical]
        public override void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            base.GetObjectData(info, context);
            info.AddValue("version", 1);
            info.AddValue("excludeDays", excludeDays);
            info.AddValue("excludeAll", excludeAll);
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

        /// <summary> 
        /// Return true, if wday is defined to be excluded. E. g.
        /// saturday and sunday.
        /// </summary>
        public virtual bool IsDayExcluded(DayOfWeek wday)
        {
            return excludeDays[(int) wday];
        }

        /// <summary>
        /// Redefine a certain day of the week to be excluded (true) or included
        /// (false). Use <see cref="DayOfWeek"/> enum to determine the weekday.
        /// </summary>
        public virtual void SetDayExcluded(DayOfWeek wday, bool exclude)
        {
            excludeDays[(int) wday] = exclude;
            excludeAll = AreAllDaysExcluded();
        }

        /// <summary>
        /// Check if all week ays are excluded. That is no day is included.
        /// </summary>
        public virtual bool AreAllDaysExcluded()
        {
            if (IsDayExcluded(DayOfWeek.Sunday) == false)
            {
                return false;
            }

            if (IsDayExcluded(DayOfWeek.Monday) == false)
            {
                return false;
            }

            if (IsDayExcluded(DayOfWeek.Tuesday) == false)
            {
                return false;
            }

            if (IsDayExcluded(DayOfWeek.Wednesday) == false)
            {
                return false;
            }

            if (IsDayExcluded(DayOfWeek.Thursday) == false)
            {
                return false;
            }

            if (IsDayExcluded(DayOfWeek.Friday) == false)
            {
                return false;
            }

            if (IsDayExcluded(DayOfWeek.Saturday) == false)
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// Determine whether the given time (in milliseconds) is 'included' by the
        /// Calendar.
        /// <para>
        /// Note that this Calendar is only has full-day precision.
        /// </para>
        /// </summary>
        public override bool IsTimeIncluded(DateTimeOffset timeUtc)
        {
            if (excludeAll)
            {
                return false;
            }

            // Test the base calendar first. Only if the base calendar not already
            // excludes the time/date, continue evaluating this calendar instance.
            if (!base.IsTimeIncluded(timeUtc))
            {
                return false;
            }

            timeUtc = TimeZoneUtil.ConvertTime(timeUtc, this.TimeZone); //apply the timezone
            return !(IsDayExcluded(timeUtc.DayOfWeek));
        }

        /// <summary>
        /// Determine the next time (in milliseconds) that is 'included' by the
        /// Calendar after the given time. Return the original value if timeStamp is
        /// included. Return DateTime.MinValue if all days are excluded.
        /// <para>
        /// Note that this Calendar is only has full-day precision.
        /// </para>
        /// </summary>
        public override DateTimeOffset GetNextIncludedTimeUtc(DateTimeOffset timeUtc)
        {
            if (excludeAll)
            {
                return DateTimeOffset.MinValue;
            }

            // Call base calendar implementation first
            DateTimeOffset baseTime = base.GetNextIncludedTimeUtc(timeUtc);
            if ((baseTime != DateTimeOffset.MinValue) && (baseTime > timeUtc))
            {
                timeUtc = baseTime;
            }

            //apply the timezone
            timeUtc = TimeZoneUtil.ConvertTime(timeUtc, this.TimeZone);

            // Get timestamp for 00:00:00, in the correct timezone offset
            DateTimeOffset d = new DateTimeOffset(timeUtc.Date, timeUtc.Offset);

            if (!IsDayExcluded(d.DayOfWeek))
            {
                return d;
            } // return the original value with the correct offset time.

            while (IsDayExcluded(d.DayOfWeek))
            {
                d = d.AddDays(1);
            }

            return d;
        }

        public override object Clone()
        {
            WeeklyCalendar clone = (WeeklyCalendar) base.Clone();
            bool[] excludeCopy = new bool[excludeDays.Length];
            Array.Copy(excludeDays, excludeCopy, excludeDays.Length);
            clone.excludeDays = excludeCopy;
            return clone;
        }

        public override int GetHashCode()
        {
            int baseHash = 0;
            if (GetBaseCalendar() != null)
            {
                baseHash = GetBaseCalendar().GetHashCode();
            }

            return DaysExcluded.GetHashCode() + 5*baseHash;
        }

        public bool Equals(WeeklyCalendar obj)
        {
            if (obj == null)
            {
                return false;
            }
            bool baseEqual = GetBaseCalendar() == null || GetBaseCalendar().Equals(obj.GetBaseCalendar());

            return baseEqual && (ArraysEqualElementsOnEqualPlaces(obj.DaysExcluded, DaysExcluded));
        }

        public override bool Equals(object obj)
        {
            if (!(obj is WeeklyCalendar))
            {
                return false;
            }
            else
            {
                return Equals((WeeklyCalendar) obj);
            }
        }
    }
}