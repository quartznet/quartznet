//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : StringCollection.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/23/2018
// Note    : Copyright 2014-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a type-safe collection class that is used to contain string objects
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 03/22/2007  EFW  Created the code
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// A type-safe collection of <see cref="String"/> objects
    /// </summary>
    [Serializable]
    public class StringCollection : Collection<string>
    {
        #region Private data members
        //=====================================================================

        private bool suppressListChanged;

        #endregion

        #region Events
        //=====================================================================

        /// <summary>
        /// This event is raised when an item is added or removed from the list, when the list is cleared, and
        /// when an item is replaced in the list.
        /// </summary>
        public event ListChangedEventHandler ListChanged;

        /// <summary>
        /// This raises the <see cref="ListChanged"/> event
        /// </summary>
        /// <param name="e">The event arguments</param>
        protected virtual void OnListChanged(ListChangedEventArgs e)
        {
            ListChanged?.Invoke(this, e);
        }
        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <overloads>There are two overloads for the constructor</overloads>
        public StringCollection()
        {
        }

        /// <summary>
        /// Construct a collection from an enumerable list of <see cref="String"/> objects
        /// </summary>
        /// <param name="values">The enumerable list of strings</param>
        public StringCollection(IEnumerable<string> values)
        {
            if(values != null)
                this.AddRange(values);
        }
        #endregion

        #region Add and Remove methods
        //=====================================================================

        /// <summary>
        /// Add a range of <see cref="String"/> objects from an enumerable list
        /// </summary>
        /// <param name="values">The enumerable list of values</param>
        public void AddRange(IEnumerable<string> values)
        {
            if(values != null)
                try
                {
                    suppressListChanged = true;

                    foreach(string s in values)
                        base.Add(s);
                }
                finally
                {
                    suppressListChanged = false;

                    OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
                }
        }

        /// <summary>
        /// Remove a range of items from the collection
        /// </summary>
        /// <param name="index">The zero-based index at which to start removing items</param>
        /// <param name="count">The number of items to remove</param>
        public void RemoveRange(int index, int count)
        {
            ((List<string>)base.Items).RemoveRange(index, count);

            OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }
        #endregion

        #region Sorting support
        //=====================================================================

        /// <summary>
        /// This is used to sort the collection in ascending or descending order
        /// </summary>
        /// <param name="ascending">Pass true for ascending order, false for descending order</param>
        /// <param name="ignoreCase">Pass true for a case-insensitive sort or false for a case-sensitive sort</param>
        public void Sort(bool ascending, bool ignoreCase)
        {
            ((List<string>)base.Items).Sort((x, y) =>
            {
                if(ascending)
                    return String.Compare(x, y, ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture);

                return String.Compare(y, x, ignoreCase ? StringComparison.CurrentCultureIgnoreCase : StringComparison.CurrentCulture);
            });

            OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }
        #endregion

        #region Method overrides
        //=====================================================================

        /// <summary>
        /// This is overridden to raise the <see cref="ListChanged"/> event
        /// </summary>
        protected override void ClearItems()
        {
            base.ClearItems();

            if(!suppressListChanged)
                OnListChanged(new ListChangedEventArgs(ListChangedType.Reset, -1));
        }

        /// <summary>
        /// This is overridden to raise the <see cref="ListChanged"/> event
        /// </summary>
        /// <param name="index">The index at which to insert the item</param>
        /// <param name="item">The item to insert</param>
        protected override void InsertItem(int index, string item)
        {
            base.InsertItem(index, item);

            if(!suppressListChanged)
                OnListChanged(new ListChangedEventArgs(ListChangedType.ItemAdded, index));
        }

        /// <summary>
        /// This is overridden to raise the <see cref="ListChanged"/> event
        /// </summary>
        /// <param name="index">The index of the item to remove</param>
        protected override void RemoveItem(int index)
        {
            base.RemoveItem(index);

            if(!suppressListChanged)
                OnListChanged(new ListChangedEventArgs(ListChangedType.ItemDeleted, index));
        }

        /// <summary>
        /// This is overridden to raise the <see cref="ListChanged"/> event
        /// </summary>
        /// <param name="index">The index of the item to set</param>
        /// <param name="item">The item to store at the specified position</param>
        protected override void SetItem(int index, string item)
        {
            base.SetItem(index, item);

            if(!suppressListChanged)
                OnListChanged(new ListChangedEventArgs(ListChangedType.ItemChanged, index));
        }
        #endregion
    }
}
