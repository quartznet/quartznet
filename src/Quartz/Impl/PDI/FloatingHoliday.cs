//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : FloatingHolidays.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class used to automatically calculate floating holidays.  The class is serializable.
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
    /// This class is used to define a floating holiday, one that falls on a particular day of the week instance
    /// (i.e. Thanksgiving is the 4th Thursday in November).
    /// </summary>
    /// <remarks><para>Floating holidays are calculated based on an occurrence of a day of the week in a month
    /// (1st through 4th or last).  The <see cref="Offset"/> property can be used to calculate a day that falls
    /// some time before or after a holiday (i.e. the day after Thanksgiving).</para>
    /// 
    /// <para>Normally, this class is not used by itself.  Instead, it is used in conjunction with the
    /// <see cref="HolidayCollection"/> class.  The class supports serialization.</para></remarks>
    [Serializable]
    public class FloatingHoliday : Holiday, ISerializable
    {
        #region Private data members
        //=====================================================================

        private int holidayOffset;
        private DayOfWeek dayOfWeek;
        private DayOccurrence holidayOccurrence;

        // The last year used is saved so that it we don't have to calculate the date unless it or one of the
        // properties changes.
        private int convertYear;
        private DateTime convertedDate;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This sets or gets the occurrence of the day of the week on which a floating holiday falls
        /// </summary>
        /// <exception cref="ArgumentException">This is thrown if <c>None</c> is specified as the value</exception>
        [XmlAttribute]
        public DayOccurrence Occurrence
        {
            get => holidayOccurrence;
            set
            {
                if(value == DayOccurrence.None)
                    throw new ArgumentException(LR.GetString("ExDUOccurIsNone"), nameof(value));

                holidayOccurrence = value;
                convertYear = 0;
            }
        }

        /// <summary>
        /// This sets or gets the week day used for the holiday
        /// </summary>
        /// <value>This returns the day of the week on which it occurs</value>
        [XmlAttribute]
        public DayOfWeek Weekday
        {
            get => dayOfWeek;
            set
            {
                dayOfWeek = value;
                convertYear = 0;
            }
        }

        /// <summary>
        /// This sets or gets the number of days before or after the calculated floating date on which the
        /// holiday actually occurs.
        /// </summary>
        /// <value><para>Use a positive value for a number of days after the calculated date or a negative number
        /// for a number of days before the calculated date.</para>
        /// 
        /// <para>Normally, this value will be zero so that the calculated date is the holiday date.  However, in
        /// cases where a holiday is calculated in terms of the number of days after a given holiday, this can be
        /// set to the offset to adjust the calculated date.</para>
        /// 
        /// <para>For example, a "Day after Thanksgiving" holiday would set this property to 1 (one day after
        /// Thanksgiving, which is the 4th Thursday in November).  You cannot use the 4th Friday to calculate the
        /// date because if the month starts on a Friday, the calculated date would be a week too early.  As
        /// such, the <c>Offset</c> property is used instead.</para></value>
        [XmlAttribute]
        public int Offset
        {
            get => holidayOffset;
            set
            {
                holidayOffset = value;
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
        /// use in deserialization.  The defaults are set to the first Monday in September, Labor Day.</remarks>
        /// <overloads>There are three constructors for this class</overloads>
        public FloatingHoliday() : this(DayOccurrence.First, DayOfWeek.Monday, 9, 0, LR.GetString("HCLaborDay"))
        {
        }

        /// <summary>
        /// Deserialization constructor for use with <see cref="System.Runtime.Serialization.ISerializable"/>
        /// </summary>
        /// <param name="info">The serialization info object</param>
        /// <param name="context">The streaming context object</param>
        protected FloatingHoliday(SerializationInfo info, StreamingContext context)
        {
            if(info != null)
            {
                this.Occurrence = (DayOccurrence)info.GetValue("Occurrence", typeof(DayOccurrence));
                this.Weekday = (DayOfWeek)info.GetValue("Weekday", typeof(DayOfWeek));
                this.Month = (int)info.GetValue("Month", typeof(int));
                this.Offset = (int)info.GetValue("Offset", typeof(int));
                this.Description = (string)info.GetValue("Description", typeof(string));
            }
        }

        /// <summary>
        /// Construct a new holiday object that occurs on a floating date
        /// </summary>
        /// <param name="month">The month of the holiday.</param>
        /// <param name="dow">The day of the week on which it occurs.</param>
        /// <param name="occur">The occurrence of the day of the week on which the floating holiday falls.</param>
        /// <param name="offset">The number of days before or after the calculated floating date on which the
        /// holiday actually occurs.  See the <see cref="Offset"/> property for more information about this
        /// parameter.</param>
        /// <param name="description">A description of the holiday.</param>
        /// <exception cref="System.ArgumentOutOfRangeException">An exception will be thrown if the month is not
        /// between 1 and 12.</exception>
        /// <include file='DateExamples.xml' path='Examples/Holiday/HelpEx[@name="Ex1"]/*' />
        public FloatingHoliday(DayOccurrence occur, DayOfWeek dow, int month, int offset, string description)
        {
            this.Occurrence = occur;
            this.Weekday = dow;
            this.Month = month;
            this.Offset = offset;
            this.Description = description;
        }
        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// This is overridden to clone a floating holiday object
        /// </summary>
        /// <returns>A clone of the object.</returns>
        public override object Clone()
        {
            return new FloatingHoliday(this.Occurrence, this.Weekday, this.Month, this.Offset, this.Description);
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
                convertedDate = DateUtils.CalculateFloatingDate(year, this.Month, holidayOccurrence, dayOfWeek,
                    holidayOffset);
            }

            return convertedDate;
        }

        /// <summary>
        /// This is overridden to allow proper comparison of FloatingHoliday objects
        /// </summary>
        /// <param name="obj">The object to which this instance is compared</param>
        /// <returns>Returns true if the object equals this instance, false if it does not</returns>
        public override bool Equals(object obj)
        {
            if(!(obj is FloatingHoliday h))
                return false;

            return (this == h || (this.Month == h.Month && dayOfWeek == h.Weekday &&
                holidayOccurrence == h.Occurrence && holidayOffset == h.Offset && this.Description == h.Description));
        }

        /// <summary>
        /// Get a hash code for the holiday object
        /// </summary>
        /// <remarks>To compute the hash code, it XORs the various property values together</remarks>
        /// <returns>Returns the hash code for the holiday object</returns>
        public override int GetHashCode()
        {
            int hash = this.Description.GetHashCode();

            hash = hash ^ this.Month ^ (int)dayOfWeek ^ (int)holidayOccurrence ^ holidayOffset;

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
                info.AddValue("Occurrence", this.Occurrence);
                info.AddValue("Weekday", this.Weekday);
                info.AddValue("Month", this.Month);
                info.AddValue("Offset", this.Offset);
                info.AddValue("Description", this.Description);
            }
        }
        #endregion
    }
}
