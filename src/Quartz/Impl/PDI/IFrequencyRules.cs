//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : IFrequencyRules.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains an interface that defines the methods used to expand or filter recurrence instances based
// on the various rules in a recurrence
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

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This interface class defines the methods used to expand or filter recurrence instances based on the
    /// various rules in a <see cref="Recurrence"/> object.
    /// </summary>
    /// <remarks>So, why is it done this way?  Well, you could have a base recurrence class and derive a new
    /// class from it for each of the frequencies but in my opinion, that makes it harder to use as you have to
    /// create an instance of the type based on the frequency.  With this implementation, you create an instance
    /// of the <see cref="Recurrence"/> class, set the frequency, and it figures out what to do by itself.  The
    /// exposed object model is also simpler.  All of this stuff is internal and the end-user does not need to
    /// know about it in order to use the class.</remarks>
    internal interface IFrequencyRules
    {
        /// <summary>
        /// This is used to find the starting point for the frequency
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="start">The recurrence start date</param>
        /// <param name="end">The recurrence end date</param>
        /// <param name="from">The start date of the range limiting the instances generated</param>
        /// <param name="to">The end date of the range limiting the instances generated</param>
        /// <returns>The first instance date or null if there are no more instances in the given ranges</returns>
        RecurDateTime FindStart(Recurrence r, RecurDateTime start, RecurDateTime end, RecurDateTime from,
            RecurDateTime to);

        /// <summary>
        /// This is used to find the next instance of the frequency
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="end">The recurrence end date</param>
        /// <param name="to">The end date of the range limiting the instances generated</param>
        /// <param name="last">This is used to pass in the last instance date calculated and return the next
        /// instance date</param>
        /// <returns>True if the recurrence has another instance or false if there are no more instances</returns>
        bool FindNext(Recurrence r, RecurDateTime end, RecurDateTime to, RecurDateTime last);

        /// <summary>
        /// This is used to expand or filter by month
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int ByMonth(Recurrence r, RecurDateTimeCollection dates);

        /// <summary>
        /// This is used to expand or filter by week number
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int ByWeekNo(Recurrence r, RecurDateTimeCollection dates);

        /// <summary>
        /// This is used to expand or filter by year day
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int ByYearDay(Recurrence r, RecurDateTimeCollection dates);

        /// <summary>
        /// This is used to expand or filter by month day
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int ByMonthDay(Recurrence r, RecurDateTimeCollection dates);

        /// <summary>
        /// This is used to expand or filter by day of the week
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int ByDay(Recurrence r, RecurDateTimeCollection dates);

        /// <summary>
        /// This is used to expand or filter by hour
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int ByHour(Recurrence r, RecurDateTimeCollection dates);

        /// <summary>
        /// This is used to expand or filter by minute
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int ByMinute(Recurrence r, RecurDateTimeCollection dates);

        /// <summary>
        /// This is used to expand or filter by second
        /// </summary>
        /// <param name="r">A reference to the recurrence</param>
        /// <param name="dates">A reference to the collection of current instances that have been generated</param>
        /// <returns>The number of instances in the collection.  If zero, subsequent rules don't have to be
        /// checked as there's nothing else to do.</returns>
        int BySecond(Recurrence r, RecurDateTimeCollection dates);
    }
}
