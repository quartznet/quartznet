//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Expand.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/25/2020
// Note    : Copyright 2003-2020, Eric Woodruff, All rights reserved
//
// This file contains a static class containing recurrence expansion methods
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
    /// This class is used to contain the expansion methods that add date/times to the recurrence set and are
    /// common to several frequencies.
    /// </summary>
    internal static class Expand
    {
        /// <summary>
        /// This is used to expand by month day
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public static int ByMonthDay(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;
            int expIdx, monthDay, count = dates.Count;

            UniqueIntegerCollection byMonthDay = r.ByMonthDay;

            // Don't bother if either collection is empty
            if(count != 0 && byMonthDay.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // Expand the date/time by adding a new entry for each month day specified
                    for(expIdx = 0; expIdx < byMonthDay.Count; expIdx++)
                    {
                        monthDay = byMonthDay[expIdx];
                        rdtNew = new RecurDateTime(rdt) { Day = 1 };

                        // From start of month or end of month?
                        if(monthDay > 0)
                            rdtNew.AddDays(monthDay - 1);
                        else
                        {
                            rdtNew.AddMonths(1);
                            rdtNew.AddDays(monthDay);
                        }

                        // If not in the month, discard it
                        if(rdtNew.Month != rdt.Month)
                            continue;

                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand one or more months by day of the week.  It is used by the monthly and yearly
        /// frequencies.
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public static int ByDayInMonths(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;
            DayOfWeek dow;
            int expIdx, instance, count = dates.Count;

            DayInstanceCollection byDay = r.ByDay;

            // Don't bother if either collection is empty
            if(count != 0 && byDay.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // Expand the date/time by adding a new entry for each week day instance specified
                    for(expIdx = 0; expIdx < byDay.Count; expIdx++)
                    {
                        instance = byDay[expIdx].Instance;
                        dow = byDay[expIdx].DayOfWeek;

                        if(instance == 0)
                        {
                            // Expand to every specified day of the week
                            // in the month.
                            rdtNew = new RecurDateTime(rdt) { Day = 1 };

                            rdtNew.AddDays(((int)dow + 7 - (int)rdtNew.DayOfWeek) % 7);

                            while(rdtNew.IsValidDate() && rdtNew.Year == rdt.Year && rdtNew.Month == rdt.Month)
                            {
                                dates.Add(new RecurDateTime(rdtNew));
                                rdtNew.AddDays(7);
                            }

                            continue;
                        }

                        if(instance > 0)
                        {
                            // Add the nth instance of the day of the week
                            rdtNew = new RecurDateTime(rdt) { Day = 1 };

                            rdtNew.AddDays((((int)dow + 7 - (int)rdtNew.DayOfWeek) % 7) + ((instance - 1) * 7));
                        }
                        else
                        {
                            // Add the nth instance of the day of the week
                            // from the end of the month.
                            rdtNew = new RecurDateTime(rdt) { Day = DateTime.DaysInMonth(rdt.Year, rdt.Month + 1) };

                            rdtNew.AddDays(0 - (((int)rdtNew.DayOfWeek + 7 - (int)dow) % 7) + ((instance + 1) * 7));
                        }

                        // If not in the year, discard it
                        if(rdtNew.Year != rdt.Year || rdtNew.Month != rdt.Month)
                            continue;

                        dates.Add(new RecurDateTime(rdtNew));
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand one or more weeks by day of the week
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public static int ByDayInWeeks(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;
            int expIdx, count = dates.Count;

            DayInstanceCollection byDay = r.ByDay;

            // Don't bother if either collection is empty
            if(count != 0 && byDay.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // If not valid, discard it
                    if(!rdt.IsValidDate())
                        continue;

                    // Expand the date/time by adding a new entry for each day of the week.  As with filtering,
                    // the instance number is ignored as it isn't useful here.  For this, the "week" is the seven
                    // day period starting on the occurrence date.
                    for(expIdx = 0; expIdx < byDay.Count; expIdx++)
                    {
                        rdtNew = new RecurDateTime(rdt);
                        rdtNew.AddDays((((int)byDay[expIdx].DayOfWeek + 7 - (int)r.WeekStart) % 7) -
                            (((int)rdt.DayOfWeek + 7 - (int)r.WeekStart) % 7));
                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand by hour
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If a date in the collection is invalid, it will be discarded</remarks>
        public static int ByHour(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;
            int expIdx, count = dates.Count;

            UniqueIntegerCollection byHour = r.ByHour;

            // Don't bother if either collection is empty
            if(count != 0 && byHour.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // If not valid, discard it
                    if(!rdt.IsValidDate())
                        continue;

                    // Expand the date/time by adding a new entry for each hour specified
                    for(expIdx = 0; expIdx < byHour.Count; expIdx++)
                    {
                        rdtNew = new RecurDateTime(rdt) { Hour = byHour[expIdx] };

                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand by minute
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If a date in the collection is invalid, it will be discarded</remarks>
        public static int ByMinute(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;
            int expIdx, count = dates.Count;

            UniqueIntegerCollection byMinute = r.ByMinute;

            // Don't bother if either collection is empty
            if(count != 0 && byMinute.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // If not valid, discard it
                    if(!rdt.IsValidDate())
                        continue;

                    // Expand the date/time by adding a new entry for each minute specified
                    for(expIdx = 0; expIdx < byMinute.Count; expIdx++)
                    {
                        rdtNew = new RecurDateTime(rdt) { Minute = byMinute[expIdx] };

                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand by second
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If a date in the collection is invalid, it will be discarded</remarks>
        public static int BySecond(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;
            int expIdx, count = dates.Count;

            UniqueIntegerCollection bySecond = r.BySecond;

            // Don't bother if either collection is empty
            if(count != 0 && bySecond.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // If not valid, discard it
                    if(!rdt.IsValidDate())
                        continue;

                    // Expand the date/time by adding a new entry for each second specified
                    for(expIdx = 0; expIdx < bySecond.Count; expIdx++)
                    {
                        rdtNew = new RecurDateTime(rdt) { Second = bySecond[expIdx] };

                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }
    }
}
