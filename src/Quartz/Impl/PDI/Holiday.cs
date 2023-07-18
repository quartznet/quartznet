//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Holidays.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains an abstract base classes used to automatically calculate holiday dates
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 07/10/2003  EFW  Created the code
//===============================================================================================================

using System;
using System.Xml.Serialization;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This abstract base class defines the core features of a holiday object
    /// </summary>
    [Serializable]
    public abstract class Holiday : ICloneable
    {
        #region Private data members
        //=====================================================================

        private int holidayMonth;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This sets or gets the month used for the holiday
        /// </summary>
        /// <exception cref="System.ArgumentOutOfRangeException">An exception will be thrown if the month is not
        /// between 1 and 12.
        /// </exception>
        [XmlAttribute]
        public virtual int Month
        {
            get => holidayMonth;
            set
            {
                if(value < 1 || value > 12)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExHolBadMonthValue"));

                holidayMonth = value;
            }
        }

        /// <summary>
        /// This sets or gets a description for the holiday
        /// </summary>
        [XmlText]
        public string Description { get; set; }

        #endregion

        #region Constructor
        //=====================================================================

        /// <summary>
        /// Constructor
        /// </summary>
        protected Holiday()
        {
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// This must be overridden to clone a holiday object
        /// </summary>
        /// <returns>A clone of the object</returns>
        public abstract object Clone();

        /// <summary>
        /// Convert the instance to a <see cref="DateTime" /> object based on its settings and the passed year
        /// value.
        /// </summary>
        /// <param name="year">The year in which the holiday occurs</param>
        /// <returns>Returns a <see cref="DateTime" /> object that represents the holiday date</returns>
        public abstract DateTime ToDateTime(int year);

        /// <summary>
        /// Convert the holiday instance to its string description
        /// </summary>
        /// <returns>Returns the description of the holiday</returns>
        public override string ToString()
        {
            return this.Description;
        }
        #endregion
    }
}
