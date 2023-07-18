//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Period.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2004-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class that represents a period of time
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
using System.Globalization;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This class is used to represent a period of time
    /// </summary>
    /// <remarks>The class contains a property to specify a start date and properties for an end date and a
    /// duration representing the difference between the two.  Setting any property adjusts the others
    /// accordingly.</remarks>
    [Serializable]
    public class Period : IComparable
    {
        #region Period format nested enumerated type
        //=====================================================================

        /// <summary>
        /// This enumerated type defines the format of the period when converted to a string
        /// </summary>
        [Serializable]
        public enum PeriodFormat
        {
            /// <summary>Format as start date and duration</summary>
            StartDuration,
            /// <summary>Format as start date and end date</summary>
            StartEnd
        }
        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This is used to set or get the starting date/time of the period
        /// </summary>
        /// <value>The value is expressed in local time.  The <see cref="Duration"/> property is updated
        /// automatically based on the new value.</value>
        public DateTime StartDateTime { get; set; }

        /// <summary>
        /// This is used to set or get the ending date/time of the period
        /// </summary>
        /// <value>The value is expressed in local time.  The <see cref="Duration"/> property is updated
        /// automatically based on the new value.</value>
        public DateTime EndDateTime { get; set; }

        /// <summary>
        /// This is used to set or get the duration of the time period
        /// </summary>
        /// <value>The <see cref="EndDateTime"/> property is updated automatically based on the new value</value>
        public Duration Duration
        {
            get => new Duration(this.EndDateTime - this.StartDateTime);
            set => this.EndDateTime = this.StartDateTime + value.TimeSpan;
        }

        /// <summary>
        /// This is used to set the format used when the period is converted to its string format
        /// </summary>
        /// <value>The default is based on how the object is constructed</value>
        public PeriodFormat Format { get; set; }

        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <remarks>The period start date is set to the minimum date value with a duration of zero.  The period
        /// format is set to start date with duration.</remarks>
        /// <overloads>There are five overloads for the constructor.</overloads>
        public Period() : this(DateTime.MinValue, Duration.Zero)
        {
        }

        /// <summary>
        /// Construct a period from a start date and a duration
        /// </summary>
        /// <param name="startDateTime">The start date/time of the period</param>
        /// <param name="duration">The duration of the period</param>
        /// <remarks>The period format is set to start date with duration</remarks>
        public Period(DateTime startDateTime, Duration duration)
        {
            this.StartDateTime = startDateTime;
            this.Duration = duration;
            this.Format = PeriodFormat.StartDuration;
        }

        /// <summary>
        /// Construct a period from a start date and an end date
        /// </summary>
        /// <param name="startDateTime">The start date/time of the period</param>
        /// <param name="endDateTime">The end date/time of the period</param>
        /// <remarks>The period format is set to start date with end date</remarks>
        public Period(DateTime startDateTime, DateTime endDateTime)
        {
            this.StartDateTime = startDateTime;
            this.EndDateTime = endDateTime;
            this.Format = PeriodFormat.StartEnd;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="period">The period to copy</param>
        public Period(Period period)
        {
            if(period != null)
            {
                this.StartDateTime = period.StartDateTime;
                this.EndDateTime = period.EndDateTime;
                this.Format = period.Format;
            }
        }

        /// <summary>
        /// Construct a period from a string in ISO 8601 period format
        /// </summary>
        /// <param name="period">The ISO 8601 formatted period to parse</param>
        /// <remarks>This parses a string in the form <c>[dateTime]/[dateTime]</c> or <c>[dateTime]/[duration]</c>
        /// to create a period.  The <c>[dateTime]</c> part(s) should be ISO 8601 formatted date/time values (see
        /// <see cref="DateUtils.FromISO8601String"/>).  The <c>[duration]</c> part should be an ISO 8601
        /// formatted duration value (see <see cref="PDI.Duration"/>).  The period format is set based on the
        /// format of the string received.</remarks>
        /// <exception cref="ArgumentException">This is thrown if the specified period string or any of its parts
        /// are not valid.</exception>
        public Period(string period)
        {
            if(String.IsNullOrWhiteSpace(period))
            {
                this.StartDateTime = DateTime.MinValue;
                this.Duration = Duration.Zero;
                this.Format = PeriodFormat.StartDuration;
                return;
            }

            string[] parts = period.Split('/');

            if(parts.Length != 2)
                throw new ArgumentException(LR.GetString("ExPeriodInvalidISOFormat"), nameof(period));

            this.StartDateTime = DateUtils.FromISO8601String(parts[0], true);

            if(!Char.IsDigit(parts[1][0]))
            {
                this.Duration = new Duration(parts[1]);
                this.Format = PeriodFormat.StartDuration;
            }
            else
            {
                this.EndDateTime = DateUtils.FromISO8601String(parts[1], true);
                this.Format = PeriodFormat.StartEnd;
            }
        }
        #endregion

        #region TryParse
        //=====================================================================

        /// <summary>
        /// Construct a new <c>Period</c> object from a period specified in a string. Parameters specify the
        /// period and the variable where the new <c>Period</c> object is returned.
        /// </summary>
        /// <param name="period">The string that specifies the period.</param>
        /// <param name="result">When this method returns, this contains an object that represents the period
        /// specified by s, or an empty period object if the conversion failed. This parameter is passed in
        /// uninitialized.</param>
        /// <returns>True if successfully parsed or false if the value could not be parsed</returns>
        public static bool TryParse(string period, out Period result)
        {
            try
            {
                result = new Period(period);
            }
            catch(ArgumentException )
            {
                result = new Period();
                return false;
            }
            catch(OverflowException )
            {
                result = new Period();
                return false;
            }

            return true;
        }
        #endregion

        #region Equality, hash code, ToString
        //=====================================================================

        /// <summary>
        /// Returns a value indicating whether two specified instances of <c>Period</c> are equal
        /// </summary>
        /// <param name="p1">The first period to compare</param>
        /// <param name="p2">The second period to compare</param>
        /// <returns>Returns true if the periods are equal, false if they are not</returns>
        public static bool Equals(Period p1, Period p2)
        {
            if(p1 is null && p2 is null)
                return true;

            if(p1 is null)
                return false;

            return p1.Equals(p2);
        }

        /// <summary>
        /// This is overridden to allow proper comparison of <c>Period</c> objects
        /// </summary>
        /// <param name="obj">The object to which this instance is compared</param>
        /// <returns>Returns true if the object equals this instance, false if it does not</returns>
        public override bool Equals(object obj)
        {
            Period p = obj as Period;

            if(p == null)
                return false;

            return (this.StartDateTime == p.StartDateTime && this.EndDateTime == p.EndDateTime);
        }

        /// <summary>
        /// Get a hash code for the period object
        /// </summary>
        /// <remarks>To compute the hash code, it uses the string form of the object</remarks>
        /// <returns>Returns the hash code for the period object</returns>
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        /// <summary>
        /// Convert the period instance to its ISO 8601 string form
        /// </summary>
        /// <returns>Returns the period in ISO 8601 format</returns>
        /// <remarks>The string format is based on the <see cref="PeriodFormat"/> property.  It can be expressed
        /// as either a starting and ending date or as a starting date and a duration.  When formatted as a start
        /// date and duration, the duration portion is expressed using a maximum unit of weeks to prevent any
        /// ambiguity that would be present in larger units such as months or years.</remarks>
        public override string ToString()
        {
            if(this.Format == PeriodFormat.StartDuration)
                return String.Format(CultureInfo.InvariantCulture, "{0}/{1}",
                    this.StartDateTime.ToUniversalTime().ToString(ISO8601Format.BasicDateTimeUniversal, CultureInfo.InvariantCulture),
                    this.Duration.ToString(Duration.MaxUnit.Weeks));

            return String.Format(CultureInfo.InvariantCulture, "{0}/{1}",
                this.StartDateTime.ToUniversalTime().ToString(ISO8601Format.BasicDateTimeUniversal, CultureInfo.InvariantCulture),
                this.EndDateTime.ToUniversalTime().ToString(ISO8601Format.BasicDateTimeUniversal, CultureInfo.InvariantCulture));
        }
        #endregion

        #region Comparison and operator overloads
        //=====================================================================

        /// <summary>
        /// Compares this instance to a specified object and returns an indication of their relative values
        /// </summary>
        /// <param name="obj">An object to compare or null</param>
        /// <returns>Returns -1 if this instance is less than the value, 0 if they are equal, or 1 if this
        /// instance is greater than the value or the value is null.</returns>
        /// <exception cref="ArgumentException">This is thrown if the object to be compared is not a
        /// <c>Period</c>.</exception>
        public int CompareTo(object obj)
        {
            if(!(obj is Period p))
                throw new ArgumentException(LR.GetString("ExPeriodBadCompareObject"));

            return Period.Compare(this, p);
        }

        /// <summary>
        /// Compares two <c>Period</c> values and returns an integer that indicates their relationship
        /// </summary>
        /// <param name="p1">The first period</param>
        /// <param name="p2">The second period</param>
        /// <returns>Returns -1 if the first instance is less than the second, 0 if they are equal, or 1 if the
        /// first instance is greater than the second.</returns>
        public static int Compare(Period p1, Period p2)
        {
            if(p1 is null && p2 is null)
                return 0;

            if(!(p1 is null) && p2 is null)
                return 1;

            if(p1 is null && !(p2 is null))
                return -1;

            if(p1.StartDateTime > p2.StartDateTime)
                return 1;

            if(p1.StartDateTime < p2.StartDateTime)
                return -1;

            if(p1.EndDateTime > p2.EndDateTime)
                return 1;

            if(p1.EndDateTime < p2.EndDateTime)
                return -1;

            return 0;
        }

        /// <summary>
        /// Overload for equal operator
        /// </summary>
        /// <param name="p1">The first period object</param>
        /// <param name="p2">The second period object</param>
        /// <returns>True if equal, false if not</returns>
        public static bool operator == (Period p1, Period p2)
        {
            return (Period.Compare(p1, p2) == 0);
        }

        /// <summary>
        /// Overload for not equal operator
        /// </summary>
        /// <param name="p1">The first period object</param>
        /// <param name="p2">The second period object</param>
        /// <returns>True if not equal, false if they are equal</returns>
        public static bool operator != (Period p1, Period p2)
        {
            return (Period.Compare(p1, p2) != 0);
        }

        /// <summary>
        /// Overload for less than operator
        /// </summary>
        /// <param name="p1">The first period object</param>
        /// <param name="p2">The second period object</param>
        /// <returns>True if r1 is less than r2, false if not</returns>
        public static bool operator < (Period p1, Period p2)
        {
            return (Period.Compare(p1, p2) < 0);
        }

        /// <summary>
        /// Overload for greater than operator
        /// </summary>
        /// <param name="p1">The first period object</param>
        /// <param name="p2">The second period object</param>
        /// <returns>True if r1 is greater than r2, false if not</returns>
        public static bool operator > (Period p1, Period p2)
        {
            return (Period.Compare(p1, p2) > 0);
        }

        /// <summary>
        /// Overload for less than or equal operator
        /// </summary>
        /// <param name="p1">The first period object</param>
        /// <param name="p2">The second period object</param>
        /// <returns>True if r1 is less than or equal to r2, false if not</returns>
        public static bool operator <= (Period p1, Period p2)
        {
            return (Period.Compare(p1, p2) <= 0);
        }

        /// <summary>
        /// Overload for greater than or equal operator
        /// </summary>
        /// <param name="p1">The first period object</param>
        /// <param name="p2">The second period object</param>
        /// <returns>True if r1 is greater than or equal to r2, false if not</returns>
        public static bool operator >= (Period p1, Period p2)
        {
            return (Period.Compare(p1, p2) >= 0);
        }
        #endregion
    }
}
