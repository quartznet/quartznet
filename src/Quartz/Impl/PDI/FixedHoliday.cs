//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : FixedHolidays.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class used to automatically calculate fixed holidays.  The class is serializable.
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
using System.Runtime.Serialization;
using System.Security;
using System.Xml.Serialization;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This class is used to define a fixed holiday, one that falls on a specific month and day (i.e. July 4th).
    /// </summary>
    /// <remarks><para>Fixed holidays can be automatically adjusted backwards to Friday if they fall on a
    /// Saturday or forward to Monday if they fall on a Sunday.</para>
    /// 
    /// <para>Normally, this class is not used by itself.  Instead, it is used in conjunction with the
    /// <see cref="HolidayCollection"/> class.  The class supports serialization.</para></remarks>
    [Serializable]
    public class FixedHoliday : Holiday, ISerializable
    {
        #region Private data members
        //=====================================================================

        private int dayOfMonth;
        private bool adjustFixed;

        // The last year used is saved so that it we don't have to calculate the date unless it or one of the
        // properties changes.
        private int convertYear;
        private DateTime convertedDate;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This sets or gets the month used for the holiday
        /// </summary>
        /// <remarks>If the currently selected day is not valid for the new month, it will be forced to the first
        /// day of the month.</remarks>
        [XmlAttribute]
        public override int Month
        {
            get => base.Month;
            set
            {
                base.Month = value;
                convertYear = 0;

                // Validate the day for the current month.  Yes, hard-coded non-leap year.  We don't allow a Feb
                // 29th as they don't occur every year.  If not valid, force it to the 1st.
                if(DateTime.DaysInMonth(2003, base.Month) < dayOfMonth)
                    dayOfMonth = 1;
            }
        }

        /// <summary>
        /// This sets or gets the day used for the holiday
        /// </summary>
        /// <value>This returns the literal day of the month on which the holiday falls</value>
        /// <exception cref="ArgumentOutOfRangeException">This will be thrown if the day is not valid for the
        /// currently set month.  February 29th (leap year) is not accepted either.</exception>
        [XmlAttribute]
        public int Day
        {
            get => dayOfMonth;
            set
            {
                // Validate the day for the current month.  Yes, hard-coded non-leap year.  We don't allow a Feb
                // 29th as they don't occur every year.
                if(DateTime.DaysInMonth(2003, this.Month) < value)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExHolBadDayValueForMonth"));

                dayOfMonth = value;
                convertYear = 0;
            }
        }

        /// <summary>
        /// This property is used to determine whether or not fixed holiday dates are adjusted so as not to fall
        /// on a weekend.
        /// </summary>
        /// <value>If set to true, the actual date is adjusted to fall on the preceding Friday if the actual date
        /// falls on a Saturday or on the following Monday if the actual date falls on a Sunday.  If set to
        /// false, fixed dates will fall on the month and day they indicate.</value>
        [XmlAttribute]
        public bool AdjustFixedDate
        {
            get => adjustFixed;
            set
            {
                adjustFixed = value;
                convertYear = 0;
            }
        }
        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <remarks>Normally, you will not use the default constructor.  Its main reason for existence is for
        /// use in deserialization.  The defaults are set to January 1st, New Year's Day.</remarks>
        /// <overloads>There are three constructors for this class.</overloads>
        public FixedHoliday() : this(1, 1, true, LR.GetString("HCNewYearsDay"))
        {
        }

        /// <summary>
        /// Deserialization constructor for use with <see cref="System.Runtime.Serialization.ISerializable"/>
        /// </summary>
        /// <param name="info">The serialization info object</param>
        /// <param name="context">The streaming context object</param>
        protected FixedHoliday(SerializationInfo info, StreamingContext context)
        {
            if(info != null)
            {
                this.Month = (int)info.GetValue("Month", typeof(int));
                this.Day = (int)info.GetValue("Day", typeof(int));
                this.AdjustFixedDate = (bool)info.GetValue("AdjustFixedDate", typeof(bool));
                this.Description = (string)info.GetValue("Description", typeof(string));
            }
        }

        /// <summary>
        /// Construct a new holiday object that occurs on a fixed date.  The holiday date can optionally be
        /// adjusted to a Friday or Monday if it falls on a weekend.
        /// </summary>
        /// <param name="month">The month of the holiday.</param>
        /// <param name="day">The day of the month on which it occurs.</param>
        /// <param name="adjust">Set to true to adjust date to the Friday or Monday if it falls on a weekend or
        /// false to always keep it on the specified day of the month.</param>
        /// <param name="description">A description of the holiday.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">An exception will be thrown if the month is not
        /// between 1 and 12.</exception>
        /// <exception cref="ArgumentException">An exception will be thrown if the day is not valid for the
        /// month.  February 29th (leap year) is not accepted either.</exception>
        /// <include file='DateExamples.xml' path='Examples/Holiday/HelpEx[@name="Ex1"]/*' />
        public FixedHoliday(int month, int day, bool adjust, string description)
        {
            this.Month = month;
            this.Day = day;
            this.AdjustFixedDate = adjust;
            this.Description = description;
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// This is overridden to clone a fixed holiday object
        /// </summary>
        /// <returns>A clone of the object.</returns>
        public override object Clone()
        {
            return new FixedHoliday(this.Month, this.Day, this.AdjustFixedDate, this.Description);
        }

        /// <summary>
        /// Convert the instance to a <see cref="DateTime" /> object based on its settings and the passed year
        /// value.
        /// </summary>
        /// <param name="year">The year in which the holiday occurs</param>
        /// <returns>Returns a <see cref="DateTime" /> object that represents the holiday date</returns>
        public override DateTime ToDateTime(int year)
        {
            // If the year is the same, return the last calculated date
            if(year != convertYear)
            {
                convertYear = year;

                if(adjustFixed)
                    convertedDate = DateUtils.CalculateFixedDate(year, this.Month, dayOfMonth);
                else
                    convertedDate = new DateTime(year, this.Month, dayOfMonth);
            }

            return convertedDate;
        }

        /// <summary>
        /// This is overridden to allow proper comparison of <c>FixedHoliday</c> objects
        /// </summary>
        /// <param name="obj">The object to which this instance is compared</param>
        /// <returns>Returns true if the object equals this instance, false if it does not</returns>
        public override bool Equals(object obj)
        {
            if(!(obj is FixedHoliday h))
                return false;

            return (this == h || (this.Month == h.Month && dayOfMonth == h.Day &&
                adjustFixed == h.AdjustFixedDate && this.Description == h.Description));
        }

        /// <summary>
        /// Get a hash code for the holiday object
        /// </summary>
        /// <remarks>To compute the hash code, it XORs the various property values together</remarks>
        /// <returns>Returns the hash code for the holiday object</returns>
        public override int GetHashCode()
        {
            int hash = this.Description.GetHashCode();

            hash = hash ^ this.Month ^ dayOfMonth;

            if(adjustFixed)
                hash ^= 1;

            return hash;
        }
        #endregion

        #region ISerializable implementation
        //=====================================================================

        /// <summary>
        /// This implements the <see cref="System.Runtime.Serialization.ISerializable"/> interface and adds the
        /// appropriate members to the serialization info.
        /// </summary>
        /// <param name="info">The serialization info object</param>
        /// <param name="context">The streaming context</param>
        [SecurityCritical]
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            if(info != null)
            {
                info.AddValue("Month", this.Month);
                info.AddValue("Day", this.Day);
                info.AddValue("AdjustFixedDate", this.AdjustFixedDate);
                info.AddValue("Description", this.Description);
            }
        }
        #endregion
    }
}
