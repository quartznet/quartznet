//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : MonthlyFrequency.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class used to implements the Monthly frequency rules
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
    /// This implements the Monthly frequency rules
    /// </summary>
    internal sealed class MonthlyFrequency : IFrequencyRules
    {
        /// <summary>
        /// This is used to find the starting point for the monthly frequency
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

            // Adjust the date if the starting date is before the limiting range start date
            if(RecurDateTime.Compare(rdt, from, RecurDateTime.DateTimePart.Month) < 0)
            {
                adjust = ((from.Year - rdt.Year) * 12) + from.Month - rdt.Month + r.Interval - 1;
                rdt.AddMonths(adjust - (adjust % r.Interval));
            }

            if(RecurDateTime.Compare(rdt, end, RecurDateTime.DateTimePart.Month) > 0 ||
              RecurDateTime.Compare(rdt, to, RecurDateTime.DateTimePart.Month) > 0)
                return null;

            return rdt;
        }

        /// <summary>
        /// This is used to find the next instance of the monthly frequency
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="end">The recurrence end date</param>
        /// <param name="to">The end date of the range limiting the instances generated</param>
        /// <param name="last">This is used to pass in the last instance date calculated and return the next
        /// instance date</param>
        /// <returns>True if the recurrence has another instance or false if there are no more instances</returns>
        public bool FindNext(Recurrence r, RecurDateTime end, RecurDateTime to, RecurDateTime last)
        {
            last.AddMonths(r.Interval);

            if(RecurDateTime.Compare(last, end, RecurDateTime.DateTimePart.Month) > 0 ||
              RecurDateTime.Compare(last, to, RecurDateTime.DateTimePart.Month) > 0)
                return false;

            return true;
        }

        /// <summary>
        /// This is used to filter the monthly frequency by month
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
        /// ByWeekNo is only applicable in the Yearly frequency and is ignored for the Monthly frequency
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
        /// ByYearDay doesn't appear to be useful for the Monthly frequency but the spec doesn't exclude it.  If
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
        /// This is used to expand the monthly frequency by month day
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
        /// This is used to expand the monthly frequency by day of the week
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        /// <remarks>If an expanded date is invalid, it will be discarded</remarks>
        public int ByDay(Recurrence r, RecurDateTimeCollection dates)
        {
            return Expand.ByDayInMonths(r, dates);
        }

        /// <summary>
        /// This is used to expand the monthly frequency by hour
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
        /// This is used to expand the monthly frequency by minute
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
        /// This is used to expand the monthly frequency by second
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
