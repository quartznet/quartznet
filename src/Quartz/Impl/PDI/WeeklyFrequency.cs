//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : WeeklyFrequency.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class used to implements the Weekly frequency rules
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
    /// This implements the Weekly frequency rules
    /// </summary>
    internal sealed class WeeklyFrequency : IFrequencyRules
    {
        /// <summary>
        /// This is used to find the starting point for the weekly frequency
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
            RecurDateTime rdtWeek, rdt = new RecurDateTime(start);
            int adjust;

            // Get the difference between the recurrence start and the limiting range start
            DateTime dtStart = start.ToDateTime().Date.AddDays(0 - r.weekdayOffset);

            DateTime dtFrom = from.ToDateTime().Date;
            dtFrom = dtFrom.AddDays(0 - ((int)dtFrom.DayOfWeek + 7 - (int)r.WeekStart) % 7);

            // Adjust the date so that it's in range
            if(dtStart < dtFrom)
            {
                TimeSpan ts = dtFrom - dtStart;

                adjust = (ts.Days / 7) + r.Interval - 1;
                rdt.AddDays((adjust - (adjust % r.Interval)) * 7);
            }

            // If the start of the week is after the ranges, stop now
            rdtWeek = new RecurDateTime(rdt);
            rdtWeek.AddDays(0 - r.weekdayOffset);

            if(RecurDateTime.Compare(rdtWeek, end, RecurDateTime.DateTimePart.Day) > 0 ||
              RecurDateTime.Compare(rdtWeek, to, RecurDateTime.DateTimePart.Day) > 0)
                return null;

            return rdt;
        }

        /// <summary>
        /// This is used to find the next instance of the weekly frequency
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="end">The recurrence end date</param>
        /// <param name="to">The end date of the range limiting the instances generated</param>
        /// <param name="last">This is used to pass in the last instance date calculated and return the next
        /// instance date</param>
        /// <returns>True if the recurrence has another instance or false if there are no more instances</returns>
        public bool FindNext(Recurrence r, RecurDateTime end, RecurDateTime to, RecurDateTime last)
        {
            RecurDateTime rdtWeek;

            last.AddDays(r.Interval * 7);

            // For this one, compare the week start date
            rdtWeek = new RecurDateTime(last);
            rdtWeek.AddDays(0 - r.weekdayOffset);

            if(RecurDateTime.Compare(rdtWeek, end, RecurDateTime.DateTimePart.Day) > 0 ||
              RecurDateTime.Compare(rdtWeek, to, RecurDateTime.DateTimePart.Day) > 0)
                return false;

            return true;
        }

        /// <summary>
        /// This is used to filter the weekly frequency by month
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int ByMonth(Recurrence r, RecurDateTimeCollection dates)
        {
            return Filter.ByMonth(r, dates);
        }

        /// <summary>
        /// ByWeekNo is only applicable in the Yearly frequency and is ignored for the Weekly frequency
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int ByWeekNo(Recurrence r, RecurDateTimeCollection dates)
        {
            return dates.Count;
        }

        /// <summary>
        /// ByYearDay doesn't appear to be useful for the Weekly frequency but the spec doesn't exclude it.  If
        /// specified, it acts as a filter.
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int ByYearDay(Recurrence r, RecurDateTimeCollection dates)
        {
            return Filter.ByYearDay(r, dates);
        }

        /// <summary>
        /// ByMonthDay doesn't appear to be useful for the Weekly frequency but the spec doesn't exclude it.  If
        /// specified, it acts as a filter.
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        public int ByMonthDay(Recurrence r, RecurDateTimeCollection dates)
        {
            return Filter.ByMonthDay(r, dates);
        }

        /// <summary>
        /// This is used to expand the weekly frequency by day of the week
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public int ByDay(Recurrence r, RecurDateTimeCollection dates)
        {
            return Expand.ByDayInWeeks(r, dates);
        }

        /// <summary>
        /// This is used to expand the weekly frequency by hour
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
        /// This is used to expand the weekly frequency by minute
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
        /// This is used to expand the weekly frequency by second
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
