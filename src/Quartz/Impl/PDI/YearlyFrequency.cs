//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : YearlyFrequency.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/25/2020
// Note    : Copyright 2003-2020, Eric Woodruff, All rights reserved
//
// This file contains a class used to implements the Yearly frequency rules
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
    /// This implements the Yearly frequency rules
    /// </summary>
    internal sealed class YearlyFrequency : IFrequencyRules
    {
        /// <summary>
        /// This is used to find the starting point for the yearly frequency
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="start">The recurrence start date</param>
        /// <param name="end">The recurrence end date</param>
        /// <param name="from">The start date of the range limiting the instances generated</param>
        /// <param name="to">The end date of the range limiting the instances generated</param>
        /// <returns>The first instance date or null if there are no more instances</returns>
        public RecurDateTime FindStart(Recurrence r, RecurDateTime start, RecurDateTime end, RecurDateTime from,
          RecurDateTime to)
        {
            int adjust;

            RecurDateTime rdt = new RecurDateTime(start);

            // Adjust the year if the starting date is before the limiting range start date
            if(rdt.Year < from.Year)
            {
                adjust = from.Year - rdt.Year + r.Interval - 1;
                rdt.Year += (adjust - (adjust % r.Interval));
            }

            if(rdt.Year > end.Year || rdt.Year > to.Year)
                return null;

            return rdt;
        }

        /// <summary>
        /// This is used to find the next instance of the yearly frequency
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="end">The recurrence end date</param>
        /// <param name="to">The end date of the range limiting the instances generated</param>
        /// <param name="last">This is used to pass in the last instance date calculated and return the next
        /// instance date</param>
        /// <returns>True if the recurrence has another instance or false if there are no more instances</returns>
        public bool FindNext(Recurrence r, RecurDateTime end, RecurDateTime to, RecurDateTime last)
        {
            last.Year += r.Interval;

            if(last.Year > end.Year || last.Year > to.Year)
                return false;

            return true;
        }

        /// <summary>
        /// This is used to expand the yearly frequency by month
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>This may generate invalid dates (i.e. June 31st).  These will be removed later.</remarks>
        public int ByMonth(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;

            int expIdx, count = dates.Count;

            UniqueIntegerCollection byMonth = r.ByMonth;

            // Don't bother if either collection is empty
            if(count != 0 && byMonth.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // Expand the date/time by adding a new entry for each month specified
                    for(expIdx = 0; expIdx < byMonth.Count; expIdx++)
                    {
                        rdtNew = new RecurDateTime(rdt) { Month = byMonth[expIdx] - 1 };
                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand the yearly frequency by week number
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public int ByWeekNo(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;

            int expIdx, week, yearWeeks, count = dates.Count;

            UniqueIntegerCollection byWeekNo = r.ByWeekNo;

            // Don't bother if either collection is empty
            if(count != 0 && byWeekNo.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    yearWeeks = DateUtils.WeeksInYear(rdt.Year, r.WeekStart);
                    dates.RemoveAt(0);

                    // Expand the date/time by adding a new entry for each week number specified
                    for(expIdx = 0; expIdx < byWeekNo.Count; expIdx++)
                    {
                        week = byWeekNo[expIdx];

                        // If not in the year, discard it
                        if((week == 53 || week == -53) && yearWeeks == 52)
                            continue;

                        if(week > 0)
                        {
                            rdtNew = new RecurDateTime(DateUtils.DateFromWeek(rdt.Year, week, r.WeekStart,
                                r.weekdayOffset));
                        }
                        else
                            rdtNew = new RecurDateTime(DateUtils.DateFromWeek(rdt.Year, yearWeeks + week + 1,
                                r.WeekStart, r.weekdayOffset));

                        rdtNew.Hour = rdt.Hour;
                        rdtNew.Minute = rdt.Minute;
                        rdtNew.Second = rdt.Second;

                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand the yearly frequency by year day
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public int ByYearDay(Recurrence r, RecurDateTimeCollection dates)
        {
            RecurDateTime rdt, rdtNew;

            int expIdx, yearDay, count = dates.Count;

            UniqueIntegerCollection byYearDay = r.ByYearDay;

            // Don't bother if either collection is empty
            if(count != 0 && byYearDay.Count != 0)
                for(int idx = 0; idx < count; idx++)
                {
                    rdt = dates[0];
                    dates.RemoveAt(0);

                    // Expand the date/time by adding a new entry for each year day specified
                    for(expIdx = 0; expIdx < byYearDay.Count; expIdx++)
                    {
                        yearDay = byYearDay[expIdx];
                        rdtNew = new RecurDateTime(rdt) { Month = 0, Day = 1 };

                        // From start of year or end of year?
                        if(yearDay > 0)
                            rdtNew.AddDays(yearDay - 1);
                        else
                        {
                            rdtNew.Year++;
                            rdtNew.AddDays(yearDay);
                        }

                        // If not in the year, discard it
                        if(rdtNew.Year != rdt.Year)
                            continue;

                        dates.Add(rdtNew);
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand the yearly frequency by month day
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int ByMonthDay(Recurrence r, RecurDateTimeCollection dates)
        {
            return Expand.ByMonthDay(r, dates);
        }

        /// <summary>
        /// This is used to expand the yearly frequency by day of the week
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public int ByDay(Recurrence r, RecurDateTimeCollection dates)
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
                            // Expand to every specified day of the week in the year
                            rdtNew = new RecurDateTime(rdt) { Month = 0, Day = 1 };
                            rdtNew.AddDays(((int)dow + 7 - (int)rdtNew.DayOfWeek) % 7);

                            while(rdtNew.IsValidDate() && rdtNew.Year == rdt.Year)
                            {
                                dates.Add(new RecurDateTime(rdtNew));
                                rdtNew.AddDays(7);
                            }

                            continue;
                        }

                        if(instance > 0)
                        {
                            // Add the nth instance of the day of the week
                            rdtNew = new RecurDateTime(rdt) { Month = 0, Day = 1 };
                            rdtNew.AddDays((((int)dow + 7 - (int)rdtNew.DayOfWeek) % 7) + ((instance - 1) * 7));
                        }
                        else
                        {
                            // Add the nth instance of the day of the week from the end of the year
                            rdtNew = new RecurDateTime(rdt) { Month = 11, Day = 31 };
                            rdtNew.AddDays(0 - (((int)rdtNew.DayOfWeek + 7 - (int)dow) % 7) + ((instance + 1) * 7));
                        }

                        // If not in the year, discard it
                        if(rdtNew.Year != rdt.Year)
                            continue;

                        dates.Add(new RecurDateTime(rdtNew));
                    }
                }

            return dates.Count;
        }

        /// <summary>
        /// This is used to expand the yearly frequency by hour
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int ByHour(Recurrence r, RecurDateTimeCollection dates)
        {
            return Expand.ByHour(r, dates);
        }

        /// <summary>
        /// This is used to expand the yearly frequency by minute
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int ByMinute(Recurrence r, RecurDateTimeCollection dates)
        {
            return Expand.ByMinute(r, dates);
        }

        /// <summary>
        /// This is used to expand the yearly frequency by second
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int BySecond(Recurrence r, RecurDateTimeCollection dates)
        {
            return Expand.BySecond(r, dates);
        }
    }
}