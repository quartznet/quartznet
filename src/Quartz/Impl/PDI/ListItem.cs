//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : ListItem.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/22/2014
// Note    : Copyright 2004-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a simple list item object that can be used to obtain a list of values suitable for binding
// to a combo box, list box, etc.  The Windows Forms user controls use this to bind the values and descriptions
// for the various recurrence options to the combo boxes.
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

using System;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This is a simple list item object that can be used as part of a collection suitable for binding to a
    /// combo box, radio button list, list box, etc.
    /// </summary>
    /// <remarks>The Windows Forms user controls use this to bind the values and descriptions for the various
    /// recurrence options to the combo boxes.  The <see cref="RecurOptsDataSource"/> class methods return
    /// collections of these items for the data sources.</remarks>
    public class ListItem
    {
        #region Properties
        //=====================================================================

        /// <summary>
        /// This returns the value of the item
        /// </summary>
        /// <remarks>Specify this property name for the <c>ValueMember</c> or <c>DataValueField</c> property of
        /// the control using the item as part of its data source.</remarks>
        public object Value { get; set; }

        /// <summary>
        /// This returns the display text for the item
        /// </summary>
        /// <remarks>Specify this property name for the <c>DisplayMember</c> or <c>DataTextField</c> property of
        /// the control using the item as part of its data source.</remarks>
        public string Display { get; set; }

        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <remarks>The value is set to null and the display text is set to an empty string</remarks>
        /// <overloads>There are three overloads for the constructor</overloads>
        public ListItem() : this(null, null)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="value">The value for the item</param>
        /// <remarks>The display text will be set to the string representation of the value</remarks>
        public ListItem(object value) : this(value, null)
        {
        }

        /// <summary>
        /// Constructor
        /// </summary>
        /// <param name="value">The value for the item</param>
        /// <param name="text">The display text for the item</param>
        /// <remarks>Pass it the value and display text to use for the item.  If the display text is null, it
        /// will be set to the string representation of the value.</remarks>
        /// <example>
        /// <code language="cs">
        /// // Set the sort order values using ListItem objects
        /// cboSortOrder.DisplayMember = "Display";
        /// cboSortOrder.ValueMember = "Value";
        /// cboSortOrder.Items.Add(new ListItem(1, "Item Description"));
        /// cboSortOrder.Items.Add(new ListItem(2, "Maker"));
        /// cboSortOrder.Items.Add(new ListItem(3, "Model"));
        /// cboSortOrder.Items.Add(new ListItem(4, "Part Number"));
        /// cboSortOrder.SelectedIndex = 0;
        /// </code>
        /// <code language="vbnet">
        /// ' Set the sort order values using ListItem objects
        /// cboSortOrder.DisplayMember = "Display"
        /// cboSortOrder.ValueMember = "Value"
        /// cboSortOrder.Items.Add(New ListItem(1, "Item Description"))
        /// cboSortOrder.Items.Add(New ListItem(2, "Maker"))
        /// cboSortOrder.Items.Add(New ListItem(3, "Model"))
        /// cboSortOrder.Items.Add(New ListItem(4, "Part Number"))
        /// cboSortOrder.SelectedIndex = 0
        /// </code>
        /// </example>
        public ListItem(object value, string text)
        {
            this.Value = value;

            if(String.IsNullOrEmpty(text))
                this.Display = (value != null) ? value.ToString() : String.Empty;
            else
                this.Display = text;
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// This is overridden to return the display value when the item is converted to a string
        /// </summary>
        /// <returns>The <see cref="Display"/> member's value</returns>
        public override string ToString()
        {
            return this.Display;
        }

        /// <summary>
        /// This returns the hash code for the object
        /// </summary>
        /// <returns>The hash code of the <see cref="Display"/> member's value</returns>
        public override int GetHashCode()
        {
            return this.Display.GetHashCode();
        }
        #endregion
    }
}
