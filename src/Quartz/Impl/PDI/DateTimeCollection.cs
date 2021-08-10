//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : DateTimeCollection.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/17/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a type-safe collection class that is used to contain DateTime objects
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/18/2003  EFW  Created the code
// 03/17/2007  EFW  Converted to use a generic base class
// 10/17/2014  EFW  Updated for use with .NET 4.0
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
	/// A type-safe collection of <see cref="DateTime"/> objects.  The other classes in this namespace use this
    /// collection when returning a list of dates.  The collection can be used as a data source for data binding.
	/// </summary>
    [Serializable]
	public class DateTimeCollection : Collection<DateTime>
	{
        #region Constructor
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <overloads>There are two overloads for the constructor.</overloads>
        public DateTimeCollection()
        {
        }

        /// <summary>
        /// Construct the collection from an enumerable list of <see cref="DateTime"/> objects
        /// </summary>
        /// <param name="dates">The enumerable list of dates to add</param>
        public DateTimeCollection(IEnumerable<DateTime> dates)
        {
            if(dates != null)
                this.AddRange(dates);
        }
        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// Add a range of <see cref="DateTime"/> objects from an enumerable list
        /// </summary>
        /// <param name="dates">The enumerable list of dates to add</param>
        public void AddRange(IEnumerable<DateTime> dates)
        {
            if(dates != null)
                foreach(DateTime d in dates)
                    base.Add(d);
        }

        /// <summary>
        /// Remove a range of items from the collection
        /// </summary>
        /// <param name="index">The zero-based index at which to start removing items</param>
        /// <param name="count">The number of items to remove</param>
        public void RemoveRange(int index, int count)
        {
            ((List<DateTime>)base.Items).RemoveRange(index, count);
        }

        /// <summary>
        /// This is used to sort the collection in ascending or descending order
        /// </summary>
        /// <param name="ascending">Pass true for ascending order, false for descending order</param>
        public void Sort(bool ascending)
        {
            ((List<DateTime>)base.Items).Sort((x, y) =>
            {
                if(ascending)
                    return x.CompareTo(y);

                return y.CompareTo(x);
            });
        }
        #endregion
    }
}
