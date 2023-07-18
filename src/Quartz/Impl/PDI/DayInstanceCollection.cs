//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : DayInstanceCollection.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a day instance collection class.  The collection is serializable.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/12/2004  EFW  Created the code
// 03/17/2007  EFW  Converted to use a generic base class
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// A type-safe collection of <see cref="DayInstance"/> objects.  All instances in the collection are unique.
    /// </summary>
    [Serializable]
    public class DayInstanceCollection : Collection<DayInstance>
    {
        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <overloads>There are two overloads for the constructor</overloads>
        public DayInstanceCollection()
        {
        }

        /// <summary>
        /// Construct a collection from an enumerable list of <see cref="DayInstance"/> objects
        /// </summary>
        /// <param name="days">The enumerable list of day instances to add</param>
        public DayInstanceCollection(IEnumerable<DayInstance> days)
        {
            if(days != null)
                this.AddRange(days);
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// Add a range of <see cref="DayInstance"/> objects from an enumerable list
        /// </summary>
        /// <param name="days">The enumerable list of day instances</param>
        /// <overloads>There are two overloads for this method.</overloads>
        public void AddRange(IEnumerable<DayInstance> days)
        {
            if(days != null)
                foreach(DayInstance d in days)
                    base.Add(d);
        }

        /// <summary>
        /// Add a range of <see cref="DayOfWeek"/> values to the collection.  The instance for each entry will be
        /// set to zero to represent all days rather than a specific instance.
        /// </summary>
        /// <param name="days">The array of days of the week to add.</param>
        public void AddRange(IEnumerable<DayOfWeek> days)
        {
            if(days != null)
                foreach(DayOfWeek d in days)
                    base.Add(new DayInstance(d));
        }

        /// <summary>
        /// Add a <see cref="DayInstance"/> to the collection and set it to all instances of the specified day of
        /// the week.
        /// </summary>
        /// <param name="day">The day of the week to add</param>
        public void Add(DayOfWeek day)
        {
            base.Add(new DayInstance(day));
        }

        /// <summary>
        /// Add a <see cref="DayInstance"/> to the collection and set it to the specified instance of the
        /// specified day of the week.
        /// </summary>
        /// <param name="instance">The instance value</param>
        /// <param name="day">The day of the week to add</param>
        public void Add(int instance, DayOfWeek day)
        {
            base.Add(new DayInstance(instance, day));
        }

        /// <summary>
        /// Insert a day instance into the collection
        /// </summary>
        /// <param name="index">The index at which to insert the integer</param>
        /// <param name="item">The day instance to insert</param>
        /// <remarks>If the day instance already exists in the collection, it will be moved to the new
        /// position.</remarks>
        protected override void InsertItem(int index, DayInstance item)
        {
            int curIdx = base.IndexOf(item);

            if(curIdx == -1)
                base.InsertItem(index, item);
            else
                if(index != curIdx)
                {
                    base.RemoveAt(curIdx);

                    if(index > base.Count)
                        base.InsertItem(base.Count, item);
                    else
                        base.InsertItem(index, item);
                }
        }

        /// <summary>
        /// Set an item in the collection
        /// </summary>
        /// <param name="index">The index of the item to set</param>
        /// <param name="item">The day instance value to store</param>
        /// <remarks>If the day instance already exists in the collection, it will be moved to the new
        /// position.</remarks>
        protected override void SetItem(int index, DayInstance item)
        {
            int curIdx = base.IndexOf(item);

            if(curIdx == -1)
                base.SetItem(index, item);
            else
                if(index != curIdx)
                    this.InsertItem(index, item);
        }

        /// <summary>
        /// Remove a range of items from the collection
        /// </summary>
        /// <param name="index">The zero-based index at which to start removing items</param>
        /// <param name="count">The number of items to remove</param>
        public void RemoveRange(int index, int count)
        {
            ((List<DayInstance>)base.Items).RemoveRange(index, count);
        }
        #endregion
    }
}
