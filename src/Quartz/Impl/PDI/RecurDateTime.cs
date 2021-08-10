//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : RecurDateTime.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2004-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class that contains a date/time object used in the calculation of recurring date/times.
// It is different from the standard System.DateTime object in that it can be set to invalid dates which is
// necessary for calculating recurring dates.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/17/2004  EFW  Created the code
//===============================================================================================================

using System;
using System.Globalization;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This class is used in the calculation of recurring date/times
    /// </summary>
    /// <remarks>It is different from the standard <see cref="System.DateTime"/> object in that it can be set to
    /// invalid dates which is necessary for calculating recurring dates.</remarks>
    internal sealed class RecurDateTime : IComparable
    {
        #region Date/time part nested enumerated type
        //=====================================================================

        /// <summary>
        /// This is used by the <see cref="Compare(RecurDateTime, RecurDateTime, DateTimePart)"/> method to
        /// compare date/time values up to a certain part.
        /// </summary>
        public enum DateTimePart
        {
            /// <summary>Compare only the year</summary>
            Year,
            /// <summary>Compare only the year and month</summary>
            Month,
            /// <summary>Compare date parts only.  Ignore time parts.</summary>
            Day,
            /// <summary>Compare date parts and hour</summary>
            Hour,
            /// <summary>Compare date parts, hour, and minute</summary>
            Minute,
            /// <summary>Compare all parts</summary>
            All
        }
        #endregion

        #region Private data members
        //=====================================================================

        private int year, month, day, hour, minute, second;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// Get or set the year
        /// </summary>
        /// <remarks>When set, the date may not be valid (i.e. June 31st)</remarks>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the year is less than the minimum
        /// year value supported by a normal <see cref="System.DateTime"/> object.</exception>
        public int Year
        {
            get => year;
            set
            {
                if(value < DateTime.MinValue.Year)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRDTYearOutOfRange"));

                year = value;
            }
        }

        /// <summary>
        /// Get or set the month
        /// </summary>
        /// <remarks>When set, the date may not be valid (i.e. June 31st).  Since this is an internal class,
        /// we'll use zero based month values to make some of the math easier.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the month is less than 0 or greater
        /// than 11.</exception>
        public int Month
        {
            get => month;
            set
            {
                if(value < 0 || value > 11)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRDTBadMonth"));

                month = value;
            }
        }

        /// <summary>
        /// Get or set the day
        /// </summary>
        /// <remarks>When set, the date may not be valid (i.e. June 31st)</remarks>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the day is less than 1 or greater
        /// than 31.</exception>
        public int Day
        {
            get => day;
            set
            {
                if(value < 1 || value > 31)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRDTBadDay"));

                day = value;
            }
        }

        /// <summary>
        /// Get or set the hour
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the hour is less than 0 or greater
        /// than 23.</exception>
        public int Hour
        {
            get => hour;
            set
            {
                if(value < 0 || value > 23)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRDTBadHour"));

                hour = value;
            }
        }

        /// <summary>
        /// Get or set the minute
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the minute is less than 0 or greater
        /// than 59.</exception>
        public int Minute
        {
            get => minute;
            set
            {
                if(value < 0 || value > 59)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRDTBadMinute"));

                minute = value;
            }
        }

        /// <summary>
        /// Get or set the second
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the second is less than 0 or greater
        /// than 59.</exception>
        public int Second
        {
            get => second;
            set
            {
                if(value < 0 || value > 59)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRDTBadSecond"));

                second = value;
            }
        }

        /// <summary>
        /// This read-only property is used to get the weekday of the date
        /// </summary>
        /// <remarks>You should check to be sure the date is valid before using this property.  If not valid,
        /// either adjust the date or discard it.</remarks>
        /// <exception cref="InvalidOperationException">This is thrown if the date is not valid</exception>
        public DayOfWeek DayOfWeek => this.ToDateTime().DayOfWeek;

        /// <summary>
        /// This read-only property is used to get the day of the year of the date
        /// </summary>
        /// <remarks>You should check to be sure the date is valid before using this property.  If not valid,
        /// either adjust the date or discard it.</remarks>
        /// <exception cref="InvalidOperationException">This is thrown if the date is not valid</exception>
        public int DayOfYear => this.ToDateTime().DayOfYear;

        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Construct a date/time from a <see cref="System.DateTime"/> object
        /// </summary>
        /// <param name="dateTime">The date time to use</param>
        /// <overloads>There are two overloads for the constructor.</overloads>
        public RecurDateTime(DateTime dateTime)
        {
            year = dateTime.Year;
            month = dateTime.Month - 1;
            day = dateTime.Day;
            hour = dateTime.Hour;
            minute = dateTime.Minute;
            second = dateTime.Second;
        }

        /// <summary>
        /// Copy constructor
        /// </summary>
        /// <param name="rdt">The recurrence date/time to copy</param>
        public RecurDateTime(RecurDateTime rdt)
        {
            year = rdt.Year;
            month = rdt.Month;
            day = rdt.Day;
            hour = rdt.Hour;
            minute = rdt.Minute;
            second = rdt.Second;
        }
        #endregion

        #region Equality, hash code, ToString, ToDateTime
        //=====================================================================

        /// <summary>
        /// Returns a value indicating whether two specified instances of RecurDateTime are equal
        /// </summary>
        /// <param name="r1">The first date/time to compare</param>
        /// <param name="r2">The second date/time to compare</param>
        /// <returns>Returns true if the date/times are equal, false if they are not</returns>
        public static bool Equals(RecurDateTime r1, RecurDateTime r2)
        {
            if(r1 is null && r2 is null)
                return true;

            if(r1 is null)
                return false;

            return r1.Equals(r2);
        }

        /// <summary>
        /// This is overridden to allow proper comparison of RecurDateTime objects
        /// </summary>
        /// <param name="obj">The object to which this instance is compared</param>
        /// <returns>Returns true if the object equals this instance, false if it does not</returns>
        public override bool Equals(object obj)
        {
            RecurDateTime r = obj as RecurDateTime;

            if(r == null)
                return false;

            return (year == r.Year && month == r.Month && day == r.Day && hour == r.Hour && minute == r.Minute &&
                second == r.Second);
        }

        /// <summary>
        /// Get a hash code for the date/time
        /// </summary>
        /// <remarks>To compute the hash code, it uses the string form of the object</remarks>
        /// <returns>Returns the hash code for the date/time object</returns>
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }

        /// <summary>
        /// Convert the instance to its string form
        /// </summary>
        /// <returns>Returns the date/time as a string.  It may or may not represent a valid date</returns>
        public override string ToString()
        {
            return String.Format(CultureInfo.InvariantCulture, "{0:0000}{1:00}{2:00} {3:00}{4:00}{5:00}",
                year, month + 1, day, hour, minute, second);
        }

        /// <summary>
        /// Convert the instance to a <see cref="System.DateTime"/> object
        /// </summary>
        /// <returns>Returns the instance as a date/time</returns>
        /// <exception cref="InvalidOperationException">This is thrown if the date is not valid</exception>
        public DateTime ToDateTime()
        {
            if(!this.IsValidDate())
                throw new InvalidOperationException(LR.GetString("ExRDTInvalidDateTime"));

            return new DateTime(year, month + 1, day, hour, minute, second);
        }
        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// This is used to add a positive or negative number of months to the date/time
        /// </summary>
        /// <param name="months">The number of months to add</param>
        /// <remarks>The year is updated accordingly so that we end up with a valid month.  The day may be
        /// invalid (i.e. June 31st).</remarks>
        public void AddMonths(int months)
        {
            int newMonth, years;

            newMonth = month + months;

            if(newMonth > 0)
            {
                month = newMonth % 12;
                year += newMonth / 12;
            }
            else
            {
                years = newMonth / 12;
                newMonth %= 12;

                if(newMonth != 0)
                {
                    month = newMonth + 12;
                    years -= 1;
                }
                else
                    month = 0;

                if(years != 0)
                    year += years;
            }
        }

        /// <summary>
        /// This is used to add a positive or negative number of days to the date/time
        /// </summary>
        /// <param name="days">The number of days to add</param>
        /// <remarks>The year and month are updated accordingly so that we end up with a valid day</remarks>
        public void AddDays(int days)
        {
            int newDay, daysInMonth;

            newDay = day + days;

            if(days >= 0)
            {
                while(true)
                {
                    daysInMonth = DateTime.DaysInMonth(year, month + 1);

                    if(newDay <= daysInMonth)
                        break;

                    if(month + 1 >= 12)
                    {
                        // If we exceed the maximum year, just stop
                        if(year == 9999)
                            break;

                        year++;
                        month = 0;
                    }
                    else
                        month++;

                    newDay -= daysInMonth;
                }
            }
            else
                while(newDay <= 0)
                {
                    if(month == 0)
                    {
                        year--;
                        month = 11;
                    }
                    else
                        month--;

                    newDay += DateTime.DaysInMonth(year, month + 1);
                }

            day = newDay;
        }

        /// <summary>
        /// This is used to add a positive or negative number of hours to the date/time
        /// </summary>
        /// <param name="hours">The number of hours to add</param>
        /// <remarks>The year, month, and day are updated accordingly so that we end up with a valid date/time</remarks>
        public void AddHours(int hours)
        {
            int newHour, days;

            newHour = hour + hours;

            if(newHour >= 0)
            {
                hour = newHour % 24;

                if(newHour >= 24)
                    this.AddDays(newHour / 24);
            }
            else
            {
                days = newHour / 24;
                newHour %= 24;

                if(newHour != 0)
                {
                    hour = newHour + 24;
                    days -= 1;
                }
                else
                    hour = 0;

                if(days != 0)
                    this.AddDays(days);
            }
        }

        /// <summary>
        /// This is used to add a positive or negative number of minutes to the date/time
        /// </summary>
        /// <param name="minutes">The number of minutes to add</param>
        /// <remarks>The year, month, day, and hour are updated accordingly so that we end up with a valid
        /// date/time.</remarks>
        public void AddMinutes(int minutes)
        {
            int newMinute, hours;

            newMinute = minute + minutes;

            if(newMinute >= 0)
            {
                minute = newMinute % 60;

                if(newMinute >= 60)
                    this.AddHours(newMinute / 60);
            }
            else
            {
                hours = newMinute / 60;
                newMinute %= 60;

                if(newMinute != 0)
                {
                    minute = newMinute + 60;
                    hours -= 1;
                }
                else
                    minute = 0;

                if(hours != 0)
                    this.AddHours(hours);
            }
        }

        /// <summary>
        /// This is used to add a positive or negative number of seconds to the date/time
        /// </summary>
        /// <param name="seconds">The number of seconds to add</param>
        /// <remarks>The year, month, day, hour, and minute are updated accordingly so that we end up with a
        /// valid date/time.</remarks>
        public void AddSeconds(int seconds)
        {
            int newSecond, minutes;

            newSecond = second + seconds;

            if(newSecond >= 0)
            {
                second = newSecond % 60;

                if (newSecond >= 60)
                    this.AddMinutes(newSecond / 60);
            }
            else
            {
                minutes = newSecond / 60;
                newSecond %= 60;

                if(newSecond != 0)
                {
                    second = newSecond + 60;
                    minutes -= 1;
                }
                else
                    second = 0;

                if(minutes != 0)
                    this.AddMinutes(minutes);
            }
        }

        /// <summary>
        /// This is used to see if the date is valid
        /// </summary>
        /// <returns>True if valid or false if not</returns>
        /// <remarks>The date is invalid if the year is outside the range supported by a normal
        /// <see cref="System.DateTime"/> object or if the day number is greater than the number of days in the
        /// month in the given year.</remarks>
        public bool IsValidDate()
        {
            return !(year < DateTime.MinValue.Year || year > DateTime.MaxValue.Year ||
              day > DateTime.DaysInMonth(year, month + 1));
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
        /// <c>RecurDateTime</c>.</exception>
        public int CompareTo(object obj)
        {
            RecurDateTime rd = obj as RecurDateTime;

            if(rd == null)
                throw new ArgumentException(LR.GetString("ExRDTBadCompareObject"));

            return RecurDateTime.Compare(this, rd);
        }

        /// <summary>
        /// Compares two <c>RecurDateTime</c> values and returns an integer that indicates their relationship
        /// </summary>
        /// <param name="r1">The first date/time</param>
        /// <param name="r2">The second date/time</param>
        /// <returns>Returns -1 if the first instance is less than the second, 0 if they are equal, or 1 if the
        /// first instance is greater than the second.</returns>
        /// <overloads>There are two versions of this method</overloads>
        public static int Compare(RecurDateTime r1, RecurDateTime r2)
        {
            if(r1 is null && r2 is null)
                return 0;

            if(!(r1 is null) && r2 is null)
                return 1;

            if(r1 is null && !(r2 is null))
                return -1;

            if(r1.Year < r2.Year)
                return -1;

            if(r1.Year > r2.Year)
                return 1;

            if(r1.Month < r2.Month)
                return -1;

            if(r1.Month > r2.Month)
                return 1;

            if(r1.Day < r2.Day)
                return -1;

            if(r1.Day > r2.Day)
                return 1;

            if(r1.Hour < r2.Hour)
                return -1;

            if(r1.Hour > r2.Hour)
                return 1;

            if(r1.Minute < r2.Minute)
                return -1;

            if(r1.Minute > r2.Minute)
                return 1;

            if(r1.Second < r2.Second)
                return -1;

            if(r1.Second > r2.Second)
                return 1;

            return 0;
        }

        /// <summary>
        /// Compares two <c>RecurDateTime</c> values and returns an integer that indicates their relationship.
        /// This version only compares up to the specified date/time part.
        /// </summary>
        /// <param name="r1">The first date/time</param>
        /// <param name="r2">The second date/time</param>
        /// <param name="part">The part up to which comparisons are made.  Parts smaller than this are ignored.</param>
        /// <returns>Returns -1 if the first instance is less than the second, 0 if they are equal, or 1 if the
        /// first instance is greater than the second.</returns>
        public static int Compare(RecurDateTime r1, RecurDateTime r2, DateTimePart part)
        {
            if(r1 is null && r2 is null)
                return 0;

            if(!(r1 is null) && r2 is null)
                return 1;

            if(r1 is null && !(r2 is null))
                return -1;

            if(r1.Year < r2.Year)
                return -1;

            if(r1.Year > r2.Year)
                return 1;

            if(part == DateTimePart.Year)
                return 0;

            if(r1.Month < r2.Month)
                return -1;

            if(r1.Month > r2.Month)
                return 1;

            if(part == DateTimePart.Month)
                return 0;

            if(r1.Day < r2.Day)
                return -1;

            if(r1.Day > r2.Day)
                return 1;

            if(part == DateTimePart.Day)
                return 0;

            if(r1.Hour < r2.Hour)
                return -1;

            if(r1.Hour > r2.Hour)
                return 1;

            if(part == DateTimePart.Hour)
                return 0;

            if(r1.Minute < r2.Minute)
                return -1;

            if(r1.Minute > r2.Minute)
                return 1;

            if(part == DateTimePart.Minute)
                return 0;

            if(r1.Second < r2.Second)
                return -1;

            if(r1.Second > r2.Second)
                return 1;

            return 0;
        }

        /// <summary>
        /// Overload for equal operator
        /// </summary>
        /// <param name="r1">The first date/time object</param>
        /// <param name="r2">The second date/time object</param>
        /// <returns>True if equal, false if not</returns>
        public static bool operator == (RecurDateTime r1, RecurDateTime r2)
        {
            return (RecurDateTime.Compare(r1, r2) == 0);
        }

        /// <summary>
        /// Overload for not equal operator
        /// </summary>
        /// <param name="r1">The first date/time object</param>
        /// <param name="r2">The second date/time object</param>
        /// <returns>True if not equal, false if they are equal</returns>
        public static bool operator != (RecurDateTime r1, RecurDateTime r2)
        {
            return (RecurDateTime.Compare(r1, r2) != 0);
        }

        /// <summary>
        /// Overload for less than operator
        /// </summary>
        /// <param name="r1">The first date/time object</param>
        /// <param name="r2">The second date/time object</param>
        /// <returns>True if r1 is less than r2, false if not</returns>
        public static bool operator < (RecurDateTime r1, RecurDateTime r2)
        {
            return (RecurDateTime.Compare(r1, r2) < 0);
        }

        /// <summary>
        /// Overload for greater than operator
        /// </summary>
        /// <param name="r1">The first date/time object</param>
        /// <param name="r2">The second date/time object</param>
        /// <returns>True if r1 is greater than r2, false if not</returns>
        public static bool operator > (RecurDateTime r1, RecurDateTime r2)
        {
            return (RecurDateTime.Compare(r1, r2) > 0);
        }

        /// <summary>
        /// Overload for less than or equal operator
        /// </summary>
        /// <param name="r1">The first date/time object</param>
        /// <param name="r2">The second date/time object</param>
        /// <returns>True if r1 is less than or equal to r2, false if not</returns>
        public static bool operator <= (RecurDateTime r1, RecurDateTime r2)
        {
            return (RecurDateTime.Compare(r1, r2) <= 0);
        }

        /// <summary>
        /// Overload for greater than or equal operator
        /// </summary>
        /// <param name="r1">The first date/time object</param>
        /// <param name="r2">The second date/time object</param>
        /// <returns>True if r1 is greater than or equal to r2, false if not</returns>
        public static bool operator >= (RecurDateTime r1, RecurDateTime r2)
        {
            return (RecurDateTime.Compare(r1, r2) >= 0);
        }
        #endregion
    }
}
