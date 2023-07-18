//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Filter.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a static class containing recurrence filter methods
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/20/2004  EFW  Created the code
//===============================================================================================================

using System;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This class is used to contain the filter methods that remove date/times from the recurrence set and are
    /// common to several frequencies.
    /// </summary>
    internal static class Filter
    {
        /// <summary>
        /// This is used to filter by month
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public static int ByMonth(Recurrence r, RecurDateTimeCollection dates)
        {
            int count = dates.Count;

            // Don't bother if either collection is empty
            if(count != 0 && r.ByMonth.Count != 0)
                for(int idx = 0, collIdx = 0; idx < count; idx++)
                {
                    // Remove the date/time if the month isn't wanted
                    if(!r.isMonthUsed[dates[collIdx].Month])
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                    }
                    else
                        collIdx++;
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to filter by year day
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If a date in the collection is invalid, it will be discarded</remarks>
        public static int ByYearDay(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt;
            int days, count = dates.Count;

            // Don't bother if either collection is empty
            if(count != 0 && r.ByYearDay.Count != 0)
                for(int idx = 0, collIdx = 0; idx < count; idx++)
                {
                    rdt = dates[collIdx];

                    // If not valid, discard it
                    if(!rdt.IsValidDate())
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                        continue;
                    }

                    days = (DateTime.IsLeapYear(rdt.Year)) ? 367 : 366;

                    // Remove the date/time if the year day isn't wanted.  Check both from the start of the year
                    // and from the end of the year.
                    if(!r.isYearDayUsed[rdt.DayOfYear] && !r.isNegYearDayUsed[days - rdt.DayOfYear])
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                    }
                    else
                        collIdx++;
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to filter by month day
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If a date in the collection is invalid, it will be discarded</remarks>
        public static int ByMonthDay(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt;
            int count = dates.Count;

            // Don't bother if either collection is empty
            if(count != 0 && r.ByMonthDay.Count != 0)
                for(int idx = 0, collIdx = 0; idx < count; idx++)
                {
                    rdt = dates[collIdx];

                    // If not valid, discard it
                    if(!rdt.IsValidDate())
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                        continue;
                    }

                    // Remove the date/time if the month day isn't wanted.  Check both from the start of the
                    // month and from the end of the month.
                    if(!r.isMonthDayUsed[rdt.Day] && !r.isNegMonthDayUsed[DateTime.DaysInMonth(rdt.Year,
                      rdt.Month + 1) - rdt.Day + 1])
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                    }
                    else
                        collIdx++;
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to filter by day of the week
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If a date in the collection is invalid, it will be discarded</remarks>
        public static int ByDay(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt;
            int count = dates.Count;

            // Don't bother if either collection is empty
            if(count != 0 && r.ByDay.Count != 0)
                for(int idx = 0, collIdx = 0; idx < count; idx++)
                {
                    rdt = dates[collIdx];

                    // If not valid, discard it
                    if(!rdt.IsValidDate())
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                        continue;
                    }

                    // Remove the date/time if the weekday isn't wanted
                    if(!r.isDayUsed[(int)rdt.DayOfWeek])
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                    }
                    else
                        collIdx++;
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to filter by hour
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public static int ByHour(Recurrence r, RecurDateTimeCollection dates)
        {
            int count = dates.Count;

            // Don't bother if either collection is empty
            if(count != 0 && r.ByHour.Count != 0)
                for(int idx = 0, collIdx = 0; idx < count; idx++)
                {
                    // Remove the date/time if the hour isn't wanted
                    if(!r.isHourUsed[dates[collIdx].Hour])
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                    }
                    else
                        collIdx++;
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to filter by minute
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public static int ByMinute(Recurrence r, RecurDateTimeCollection dates)
        {
            int count = dates.Count;

            // Don't bother if either collection is empty
            if(count != 0 && r.ByMinute.Count != 0)
                for(int idx = 0, collIdx = 0; idx < count; idx++)
                {
                    // Remove the date/time if the minute isn't wanted
                    if(!r.isMinuteUsed[dates[collIdx].Minute])
                    {
                        dates.RemoveAt(collIdx);
                        count--;
                        idx--;
                    }
                    else
                        collIdx++;
                }

            return dates.Count;
        }
    }
}
