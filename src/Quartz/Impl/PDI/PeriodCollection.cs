//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Period.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2004-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a collection class for Period objects
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 07/10/2003  EFW  Created the code
// 03/05/2007  EFW  Converted to use a generic base class
//===============================================================================================================

using System.Collections.Generic;
using System.Collections.ObjectModel;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
	/// A type-safe collection of <see cref="Period"/> objects
	/// </summary>
	public class PeriodCollection : Collection<Period>
	{
        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <overloads>There are two overloads for the constructor</overloads>
        public PeriodCollection()
        {
        }

        /// <summary>
        /// Construct a collection from an enumerable list of <see cref="Period"/> objects
        /// </summary>
        /// <param name="periods">The enumerable list of periods</param>
        public PeriodCollection(IEnumerable<Period> periods)
        {
            if(periods != null)
                this.AddRange(periods);
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// Add a range of <see cref="Period"/> objects from an enumerable list
        /// </summary>
        /// <param name="periods">The enumerable list of periods</param>
        public void AddRange(IEnumerable<Period> periods)
        {
            if(periods != null)
                foreach(Period p in periods)
                    base.Add(p);
        }

        /// <summary>
        /// Remove a range of items from the collection
        /// </summary>
        /// <param name="index">The zero-based index at which to start removing items</param>
        /// <param name="count">The number of items to remove</param>
        public void RemoveRange(int index, int count)
        {
            ((List<Period>)base.Items).RemoveRange(index, count);
        }

        /// <summary>
        /// This is used to sort the collection in ascending or descending order
        /// </summary>
        /// <param name="ascending">Pass true for ascending order, false for descending order</param>
        public void Sort(bool ascending)
        {
            ((List<Period>)base.Items).Sort((x, y) =>
            {
                if(ascending)
                    return Period.Compare(x, y);

                return Period.Compare(y, x);
            });
        }
        #endregion
    }
}
