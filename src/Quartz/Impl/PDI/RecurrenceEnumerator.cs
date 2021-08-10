//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : RecurrenceEnumerator.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/23/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a type-safe enumerator for Recurrence objects
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/12/2004  EFW  Created the code
//===============================================================================================================

using System;
using System.Collections;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// A type-safe enumerator for the <see cref="Recurrence"/> class
    /// </summary>
    public class RecurrenceEnumerator : IEnumerator
    {
        #region Private data members
        //=====================================================================

        // These are used to generate the recurrence dates and track progress through the sequence
        private int idx;
        private DateTimeCollection dates;
        private Recurrence recurrence;
        private DateTime start, end;

        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="recurrence">The <see cref="Recurrence" /> object to enumerate</param>
        public RecurrenceEnumerator(Recurrence recurrence)
        {
            this.recurrence = recurrence;
            this.Reset();
        }
        #endregion

        #region IEnumerable implementation
        //=====================================================================

        /// <summary>
        /// Type-safe enumerator <c>Current</c> method
        /// </summary>
        public DateTime Current => dates[idx];

        /// <summary>
        /// Type-unsafe <c>IEnumerator.Current</c>
        /// </summary>
        object System.Collections.IEnumerator.Current => dates[idx];

        /// <summary>
        /// Move to the next element
        /// </summary>
        /// <returns>Returns true if not at the end, false if it is</returns>
        public bool MoveNext()
        {
            bool hasMore = true;

            // First time through?
            if(dates == null)
            {
                // If it's got a count, generate them all as we can't do it by yearly ranges.  There shouldn't be
                // too many although it's possible.
                if(recurrence.MaximumOccurrences != 0)
                    dates = recurrence.InstancesBetween(start, DateTime.MaxValue);
                else
                {
                    // If it's got an end date, we'll generate instances in one year increments.  This should be
                    // more efficient for long running or never ending sequences.
                    end = start.AddYears(1).AddSeconds(-1);
                    dates = recurrence.InstancesBetween(start, end);
                }
            }
            else
                idx++;

            if(idx >= dates.Count)
            {
                // If it uses a count, we got them all on the first call so stop now
                if(recurrence.MaximumOccurrences != 0)
                    hasMore = false;
                else
                {
                    idx = 0;

                    // Advance one year at a time until we get something or the end date is reached
                    do
                    {
                        if(start.Year < DateTime.MaxValue.Year)
                        {
                            start = end.AddSeconds(1);

                            if(start > recurrence.RecurUntil)
                            {
                                hasMore = false;
                                break;
                            }
                        }
                        else
                        {
                            hasMore = false;
                            break;
                        }

                        if(start.Year < DateTime.MaxValue.Year)
                            end = start.AddYears(1).AddSeconds(-1);
                        else
                            end = new DateTime(DateTime.MaxValue.Year, 12, 31, 23, 59, 59);

                        dates = recurrence.InstancesBetween(start, end);

                    } while(dates.Count == 0);
                }
            }

            return hasMore;
        }

        /// <summary>
        /// Reset the enumerator to the start
        /// </summary>
        public void Reset()
        {
            idx = 0;
            start = recurrence.StartDateTime;
            dates = null;
        }
        #endregion
    }
}
