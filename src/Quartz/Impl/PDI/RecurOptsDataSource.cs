//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : RecurOptsDataSource.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2004-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a helper class that can be used to obtain a list of values suitable for binding to a combo
// box, list box, etc.  The Windows Forms user controls use these to bind the values and descriptions for the
// various recurrence options to the combo boxes.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/18/2003  EFW  Created the code
//===============================================================================================================

using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This is a helper class use to create data sources for such controls as combo boxes, list boxes, etc. so
    /// that the various recurrence options can be shown and modified with them.  The descriptions are localized
    /// and will be set according to the current culture.
    /// </summary>
    public static class RecurOptsDataSource
    {
        /// <summary>
        /// This read-only property returns a data source containing <see cref="DayOccurrence"/> values
        /// </summary>
        /// <value>An <see cref="IList" /> interface reference suitable for data binding.  The underlying object
        /// is a <see cref="List{T}"/> containing a set of <see cref="ListItem"/> objects.</value>
        public static IList DayOccurrences => new List<ListItem>(new[] {
            new ListItem(DayOccurrence.First, LR.EnumDesc(DayOccurrence.First)),
            new ListItem(DayOccurrence.Second, LR.EnumDesc(DayOccurrence.Second)),
            new ListItem(DayOccurrence.Third, LR.EnumDesc(DayOccurrence.Third)),
            new ListItem(DayOccurrence.Fourth, LR.EnumDesc(DayOccurrence.Fourth)),
            new ListItem(DayOccurrence.Last, LR.EnumDesc(DayOccurrence.Last))
        });

        /// <summary>
        /// This read-only property returns a data source containing the days of the week based on
        /// <see cref="DaysOfWeek"/>.  This includes entries for Weekdays, Weekends, and Every Day.
        /// </summary>
        /// <value>An <see cref="IList" /> interface reference suitable for data binding.  The underlying object
        /// is a <see cref="List{T}"/> containing a set of <see cref="ListItem"/> objects.</value>
        public static IList DaysOfWeek
        {
            get
            {
                string[] dayNames = CultureInfo.CurrentCulture.DateTimeFormat.DayNames;

                return new List<ListItem>(new[] {
                    new ListItem(PDI.DaysOfWeek.Sunday, dayNames[0]),
                    new ListItem(PDI.DaysOfWeek.Monday, dayNames[1]),
                    new ListItem(PDI.DaysOfWeek.Tuesday, dayNames[2]),
                    new ListItem(PDI.DaysOfWeek.Wednesday, dayNames[3]),
                    new ListItem(PDI.DaysOfWeek.Thursday, dayNames[4]),
                    new ListItem(PDI.DaysOfWeek.Friday, dayNames[5]),
                    new ListItem(PDI.DaysOfWeek.Saturday, dayNames[6]),
                    new ListItem(PDI.DaysOfWeek.Weekdays, LR.EnumDesc(PDI.DaysOfWeek.Weekdays)),
                    new ListItem(PDI.DaysOfWeek.Weekends, LR.EnumDesc(PDI.DaysOfWeek.Weekends)),
                    new ListItem(PDI.DaysOfWeek.EveryDay, LR.EnumDesc(PDI.DaysOfWeek.EveryDay))
                });
            }
        }

        /// <summary>
        /// This read-only property returns a data source containing the days of the week based on
        /// <see cref="System.DayOfWeek"/>.
        /// </summary>
        /// <value>An <see cref="IList" /> interface reference suitable for data binding.  The underlying object
        /// is a <see cref="List{T}"/> containing a set of <see cref="ListItem"/> objects.</value>
        public static IList DayOfWeek
        {
            get
            {
                string[] dayNames = CultureInfo.CurrentCulture.DateTimeFormat.DayNames;

                return Enumerable.Range(0, 7).Select(d => new ListItem((System.DayOfWeek)d, dayNames[d])).ToList();
            }
        }

        /// <summary>
        /// This read-only property returns a data source containing the months of the year
        /// </summary>
        /// <value>An <see cref="IList" /> interface reference suitable for data binding.  The underlying object
        /// is a <see cref="List{T}"/> containing a set of <see cref="ListItem"/> objects.</value>
        public static IList MonthsOfYear
        {
            get
            {
                string[] monthNames = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;

                return Enumerable.Range(0, 12).Select(m => new ListItem(m + 1, monthNames[m])).ToList();
            }
        }
    }
}
