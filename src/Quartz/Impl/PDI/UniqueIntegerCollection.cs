//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : UniqueIntegerCollection.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/23/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a type-safe collection class that is used to contain a set of unique integer values with
// an optional range restriction and zero exclusion.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/12/2004  EFW  Created the code
// 03/05/2007  EFW  Converted to use a generic base class
//===============================================================================================================

// Ignore Spelling: ic

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
	/// A type-safe collection of unique integer values with an optional range restriction and zero exclusion
	/// </summary>
    [Serializable]
    public class UniqueIntegerCollection : Collection<int>
    {
        #region Private data members
        //=====================================================================

        // These are used to parse a string of values for adding into the collection
        private static Regex reStripNonDigits = new Regex(@"[^0-9\-,]");
        private static Regex reIsRange = new Regex("(?<=[^-])-");

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This read-only property is used to get the minimum value allowed to be added to the collection
        /// </summary>
        public int MinimumValue { get; }

        /// <summary>
        /// This read-only property is used to get the highest value allowed to be added to the collection
        /// </summary>
        public int MaximumValue { get; }

        /// <summary>
        /// This read-only property is used to get whether or not zero is allowed as a valid value
        /// </summary>
        /// <value>This is useful if you need to set a valid range that includes negative and positive values but
        /// excludes zero (i.e. -53 to -1 and +1 to +53 but not zero).</value>
        public bool AllowZero { get; }

        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <remarks>No validation range is defined and zero is allowed</remarks>
        /// <overloads>There are five overloads for the constructor</overloads>
        public UniqueIntegerCollection()
        {
            this.MinimumValue = Int32.MinValue;
            this.MaximumValue = Int32.MaxValue;
            this.AllowZero = true;
        }

        /// <summary>
        /// Construct a collection with a range and zero exclusion option
        /// </summary>
        /// <param name="min">The minimum value allowed</param>
        /// <param name="max">The maximum value allowed</param>
        /// <param name="zeroAllowed">Allow zero or not</param>
        public UniqueIntegerCollection(int min, int max, bool zeroAllowed)
        {
            this.MinimumValue = min;
            this.MaximumValue = max;
            this.AllowZero = zeroAllowed;
        }

        /// <summary>
        /// Construct a collection from an enumerable list of unique integers without range checking and with
        /// zero allowed.
        /// </summary>
        /// <param name="values">The enumerable list of integers</param>
        public UniqueIntegerCollection(IEnumerable<int> values) : this()
        {
            if(values != null)
                this.AddRange(values);
        }

        /// <summary>
        /// Construct a collection from an enumerable list of unique integers with a range and zeros exclusion
        /// setting.
        /// </summary>
        /// <param name="values">The enumerable list of integers</param>
        /// <param name="min">The minimum value allowed</param>
        /// <param name="max">The maximum value allowed</param>
        /// <param name="zeroAllowed">Allow zero or not</param>
        public UniqueIntegerCollection(IEnumerable<int> values, int min, int max, bool zeroAllowed) :
          this(min, max, zeroAllowed)
        {
            if(values != null)
                this.AddRange(values);
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="values">The <c>UniqueIntegerCollection</c> to copy.  The range and zero exclusion
        /// setting are inherited from the copied collection.</param>
        public UniqueIntegerCollection(UniqueIntegerCollection values)
        {
            if(values != null)
            {
                this.MinimumValue = values.MinimumValue;
                this.MaximumValue = values.MaximumValue;
                this.AllowZero = values.AllowZero;

                this.AddRange(values);
            }
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// Add a range of integers from an enumerable list
        /// </summary>
        /// <param name="values">The enumerable list of integers</param>
        public void AddRange(IEnumerable<int> values)
        {
            if(values != null)
                foreach(int v in values)
                    base.Add(v);
        }

        /// <summary>
        /// Insert an integer into the collection
        /// </summary>
        /// <param name="index">The index at which to insert the integer</param>
        /// <param name="item">The integer to insert</param>
        /// <remarks>If the integer already exists in the collection, it will be moved to the new position</remarks>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the value is less than
        /// <see cref="MinimumValue"/> or greater than <see cref="MaximumValue"/>.</exception>
        /// <exception cref="ArgumentException">This is thrown if the value is zero and zeros are not allowed in
        /// the collection.</exception>
        protected override void InsertItem(int index, int item)
        {
            if(item < this.MinimumValue || item > this.MaximumValue)
                throw new ArgumentOutOfRangeException(nameof(item), item, LR.GetString("ExUICValueOutOfRange"));

            if(item == 0 && !this.AllowZero)
                throw new ArgumentException(LR.GetString("ExUICZerosNotAllowed"), nameof(item));

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
        /// <param name="item">The integer value to store</param>
        /// <remarks>If the integer already exists in the collection, it will be moved to the new position</remarks>
        protected override void SetItem(int index, int item)
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
            ((List<int>)base.Items).RemoveRange(index, count);
        }

        /// <summary>
        /// This is used to sort the collection in ascending or descending order
        /// </summary>
        /// <param name="ascending">Pass true for ascending order, false for descending order</param>
        public void Sort(bool ascending)
        {
            ((List<int>)base.Items).Sort((x, y) =>
            {
                if(ascending)
                    return x.CompareTo(y);

                return y.CompareTo(x);
            });
        }

        /// <summary>
        /// This is used to get a string containing the values in the collection
        /// </summary>
        /// <returns>A string containing the values.  Ranges of consecutive values are compressed into an "X-Y"
        /// format.  For example: 1,10,15-20,30-35,100</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(256);
            int idx, value, startValue, count = base.Count;

            // Copy the items to an array and sort them in ascending order.  We do this so as not to disturb the
            // order of the collection.
            int[] array = new int[base.Count];
            base.CopyTo(array, 0);

            Array.Sort(array);

            for(idx = 0; idx < count; idx++)
            {
                if(idx != 0)
                    sb.Append(',');

                value = array[idx];
                sb.Append(value);

                if(idx < count - 1 && array[idx + 1] == value + 1)
                {
                    startValue = value;

                    while(idx < count - 1 && array[idx + 1] == value + 1)
                    {
                        idx++;
                        value = array[idx];
                    }

                    // If it's only one greater, use a comma
                    if(value == startValue + 1)
                        sb.AppendFormat(",{0}", value);
                    else
                        sb.AppendFormat("-{0}", value);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// This is used to parse a set of individual numbers or ranges of numbers from a string and store them
        /// in the collection.
        /// </summary>
        /// <param name="values">The string containing the values to parse and store</param>
        /// <remarks>Any values that are not valid are discarded</remarks>
        /// <example>
        /// <code language="cs">
        /// UniqueIntegerCollection ic = new UniqueIntegerCollection();
        /// ic.ParseValues("1, 10, 15-20, 30-35, 100");
        /// </code>
        /// <code language="vbnet">
        /// Dim ic As New UniqueIntegerCollection()
        /// ic.ParseValues("1, 10, 15-20, 30-35, 100")
        /// </code>
        /// </example>
        public void ParseValues(string values)
        {
            string[] parts, range;
            int value;

            // Remove all characters that are not a digit, dash, or comma
            string parse = reStripNonDigits.Replace(values, String.Empty);

            parts = parse.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

            foreach(string s in parts)
                if(!reIsRange.IsMatch(s))
                {
                    // Single value.  Discard invalid and out of range entries.
                    if(Int32.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value) &&
                      value >= this.MinimumValue && value <= this.MaximumValue && (value != 0 || this.AllowZero))
                        base.Add(value);
                }
                else
                {
                    // Range of values
                    range = reIsRange.Split(s);

                    // Discard invalid entries
                    if(Int32.TryParse(range[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out int low) &&
                      Int32.TryParse(range[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int high))
                    {
                        // Flip the range if necessary
                        if(low > high)
                        {
                            value = low;
                            low = high;
                            high = value;
                        }

                        // Out of range values are discarded
                        while(low <= high)
                        {
                            if(low >= this.MinimumValue && low <= this.MaximumValue && (low != 0 || this.AllowZero))
                                base.Add(low);

                            low++;
                        }
                    }
                }
        }
        #endregion
    }
}
