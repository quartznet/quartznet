//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Duration.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2004-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class that represents a duration.  It contains a TimeSpan object and adds support for
// handling ISO 8601 duration values.
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
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This class adds support to <see cref="System.TimeSpan"/> for handling ISO 8601 duration values.  Since it
    /// cannot derive from it, the class contains a <c>TimeSpan</c> instance and allows modifications to it
    /// through additional methods and properties.
    /// </summary>
    /// <remarks><para>Since durations can specify a number of weeks, months, and/or years, additional properties
    /// and methods are available to support those options.  Methods are also present that can convert an ISO
    /// 8601 formatted string to a duration object and back to a string.</para>
    /// 
    /// <para>Since the definition of a month and year varies, the class allows the specification of the length
    /// of time in days for a month and a year.  By default, it uses 30 days for a month, and 365 days for a
    /// year.  To guarantee a consistent definition of a duration, you can limit the maximum units to weeks when
    /// converting the duration to a string.</para></remarks>
    [Serializable]
    public struct Duration : IComparable
    {
        #region MaxUnit nested enumerated type
        //=====================================================================

        /// <summary>
        /// This enumerated type defines the maximum unit of time allowed when converting a duration to its ISO
        /// 8601 string format.
        /// </summary>
        [Serializable]
        public enum MaxUnit
        {
            /// <summary>Full syntax, all parts represented</summary>
            Years,
            /// <summary>Time expressed in units up to months</summary>
            Months,
            /// <summary>Time expressed in units up to weeks</summary>
            Weeks,
            /// <summary>Time expressed in units up to days</summary>
            Days,
            /// <summary>Time expressed in units up to hours</summary>
            Hours,
            /// <summary>Time expressed in units up to minutes</summary>
            Minutes,
            /// <summary>Time expressed in units up to seconds</summary>
            Seconds
        }
        #endregion

        #region Private data members
        //=====================================================================

        private TimeSpan ts;    // The underlying time span

        // The number of days in a month and year
        private static double daysInMonth = 30, daysInYear = 365;

        private static Regex reDuration = new Regex(@"^\s*(?<Negative>-)?" +
            @"P(((?<Years>\d*)Y)?((?<Months>\d*)M)?((?<Weeks>\d*)W)?" +
            @"((?<Days>\d*)D)?(T((?<Hours>\d*)H)?((?<Mins>\d*)M)?" +
            @"((?<Secs>\d*)S)?)?)\s*$", RegexOptions.IgnoreCase);
        #endregion

        #region Constant fields
        //=====================================================================

        /// <summary>
        /// Represents the number of ticks in 1 week. This field is read-only.
        /// </summary>
        public const long TicksPerWeek = TimeSpan.TicksPerDay * 7;

        /// <summary>
        /// This represents a zero length duration.  This field is read-only.
        /// </summary>
        public static readonly Duration Zero = new Duration(0L);
        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This property is used to set or get the number of days in one month
        /// </summary>
        /// <value>The number of days in a month can vary.  By default, it is defined as 30 days.  The value can
        /// contain fractional days.
        /// </value>
        [XmlIgnore]
        public static double DaysInOneMonth
        {
            get => daysInMonth;
            set => daysInMonth = value;
        }

        /// <summary>
        /// This property is used to set or get the number of days in one year
        /// </summary>
        /// <value>The number of days in a year can vary.  By default, it is defined as 365 days.  The value can
        /// contain fractional days.
        /// </value>
        [XmlIgnore]
        public static double DaysInOneYear
        {
            get => daysInYear;
            set => daysInYear = value;
        }

        /// <summary>
        /// This returns the number of timer ticks in one month based on the current setting of
        /// <see cref="DaysInOneMonth"/>.
        /// </summary>
        public static long TicksPerMonth => (long)(TimeSpan.TicksPerDay * Duration.DaysInOneMonth);

        /// <summary>
        /// This returns the number of timer ticks in one year based on the current setting of
        /// <see cref="DaysInOneYear"/>.
        /// </summary>
        public static long TicksPerYear => (long)(TimeSpan.TicksPerDay * Duration.DaysInOneYear);

        /// <summary>
        /// This allows access to the underlying <see cref="TimeSpan"/> object
        /// </summary>
        [XmlIgnore]
        public TimeSpan TimeSpan
        {
            get => ts;
            set => ts = value;
        }

        /// <summary>
        /// This allows the underlying time span (and thus the duration) to be serialized and deserialized
        /// </summary>
        public long Ticks
        {
            get => ts.Ticks;
            set => ts = new TimeSpan(value);
        }

        /// <summary>
        /// Gets the number of whole days represented by this instance
        /// </summary>
        /// <remarks>Note that this returns the number of whole days in the <strong>duration</strong> rather than
        /// the number of whole days in the underlying time span.</remarks>
        public int Days => (int)(ts.TotalDays % Duration.DaysInOneYear % Duration.DaysInOneMonth % 7);

        /// <summary>
        /// Gets the number of whole weeks represented by this instance
        /// </summary>
        public int Weeks => (int)(ts.TotalDays % Duration.DaysInOneYear % Duration.DaysInOneMonth / 7);

        /// <summary>
        /// Gets the number of whole months represented by this instance
        /// </summary>
        public int Months => (int)(ts.TotalDays % Duration.DaysInOneYear / Duration.DaysInOneMonth);

        /// <summary>
        /// Gets the number of whole years represented by this instance
        /// </summary>
        public int Years => (int)(ts.TotalDays / Duration.DaysInOneYear);

        /// <summary>
        /// Gets the value of this instance expressed in whole and fractional weeks
        /// </summary>
        public double TotalWeeks => ts.TotalDays / 7;

        /// <summary>
        /// Gets the value of this instance expressed in whole and fractional months
        /// </summary>
        public double TotalMonths => ts.TotalDays / Duration.DaysInOneMonth;

        /// <summary>
        /// Gets the value of this instance expressed in whole and fractional years
        /// </summary>
        public double TotalYears => ts.TotalDays / Duration.DaysInOneYear;

        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Construct a duration from a number of timer ticks
        /// </summary>
        /// <param name="ticks">The number of timer ticks used to initialize the instance</param>
        /// <overloads>There are six overloads for the constructor</overloads>
        public Duration(long ticks)
        {
            ts = new TimeSpan(ticks);
        }

        /// <summary>
        /// Construct a duration from a <see cref="TimeSpan"/>
        /// </summary>
        /// <param name="timeSpan">The time span used to initialize the instance</param>
        public Duration(TimeSpan timeSpan)
        {
            ts = timeSpan;
        }

        /// <summary>
        /// Construct a duration from a number of weeks and a <see cref="TimeSpan"/>
        /// </summary>
        /// <param name="weeks">The number of weeks</param>
        /// <param name="timeSpan">The time span used to initialize the instance</param>
        public Duration(int weeks, TimeSpan timeSpan)
        {
            ts = timeSpan.Add(new TimeSpan(weeks * Duration.TicksPerWeek));
        }

        /// <summary>
        /// Construct a duration from a number of months, weeks, and a <see cref="TimeSpan"/>
        /// </summary>
        /// <param name="months">The number of months</param>
        /// <param name="weeks">The number of weeks</param>
        /// <param name="timeSpan">The time span used to initialize the instance</param>
        public Duration(int months, int weeks, TimeSpan timeSpan)
        {
            ts = timeSpan.Add(new TimeSpan(months * Duration.TicksPerMonth)).Add(
                new TimeSpan(weeks * Duration.TicksPerWeek));
        }

        /// <summary>
        /// Construct a duration from a number of years, months, weeks, and a <see cref="TimeSpan"/>
        /// </summary>
        /// <param name="years">The number of years</param>
        /// <param name="months">The number of months</param>
        /// <param name="weeks">The number of weeks</param>
        /// <param name="timeSpan">The time span used to initialize the instance</param>
        public Duration(int years, int months, int weeks, TimeSpan timeSpan)
        {
            ts = timeSpan.Add(new TimeSpan(years * Duration.TicksPerYear)).Add(
                new TimeSpan(months * Duration.TicksPerMonth)).Add(
                    new TimeSpan(weeks * Duration.TicksPerWeek));
        }

        /// <summary>
        /// Construct a duration from a string in ISO 8601 duration format
        /// </summary>
        /// <param name="duration">The ISO 8601 formatted duration to parse</param>
        /// <remarks>This parses a string in the form P#Y#M#W#DT#H#M#S to create a duration.  '#' indicates a
        /// string of one or more digits and the letters represent the years, months, weeks, days, hours,
        /// minutes, and seconds.  Any of the parts except the leading 'P' and the 'T' time separator can be
        /// omitted entirely if not needed.</remarks>
        /// <exception cref="ArgumentException">This is thrown if the specified duration string is not valid</exception>
        public Duration(string duration)
        {
            int years = 0, months = 0, weeks = 0, days = 0, hours = 0, minutes = 0, seconds = 0;

            // If null or empty, default to zero
            if(String.IsNullOrWhiteSpace(duration))
            {
                ts = TimeSpan.Zero;
                return;
            }

            Match m = reDuration.Match(duration);

            if(!m.Success)
                throw new ArgumentException(LR.GetString("ExDurBadISOFormat"), nameof(duration));

            if(m.Groups["Years"].Value.Length != 0)
                years = Convert.ToInt32(m.Groups["Years"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["Months"].Value.Length != 0)
                months = Convert.ToInt32(m.Groups["Months"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["Weeks"].Value.Length != 0)
                weeks = Convert.ToInt32(m.Groups["Weeks"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["Days"].Value.Length != 0)
                days = Convert.ToInt32(m.Groups["Days"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["Hours"].Value.Length != 0)
                hours = Convert.ToInt32(m.Groups["Hours"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["Mins"].Value.Length != 0)
                minutes = Convert.ToInt32(m.Groups["Mins"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["Secs"].Value.Length != 0)
                seconds = Convert.ToInt32(m.Groups["Secs"].Value, CultureInfo.InvariantCulture);

            ts = new TimeSpan(days, hours, minutes, seconds, 0);

            if(years != 0)
                ts += new TimeSpan(years * Duration.TicksPerYear);

            if(months != 0)
                ts += new TimeSpan(months * Duration.TicksPerMonth);

            if(weeks != 0)
                ts += new TimeSpan(weeks * Duration.TicksPerWeek);

            if(m.Groups["Negative"].Value == "-")
                ts = ts.Negate();
        }
        #endregion

        #region Static duration creation methods
        //=====================================================================

        /// <summary>
        /// Returns a <see cref="Duration"/> that represents a specified number of weeks, where the specification
        /// is accurate to the nearest millisecond.
        /// </summary>
        /// <param name="value">A number of weeks, accurate to the nearest millisecond</param>
        /// <returns>A <see cref="Duration"/> that represents the value</returns>
        public static Duration FromWeeks(double value)
        {
            return new Duration(TimeSpan.FromDays(value * 7));
        }

        /// <summary>
        /// Returns a <see cref="Duration"/> that represents a specified number of months, where the
        /// specification is accurate to the nearest millisecond.
        /// </summary>
        /// <param name="value">A number of months, accurate to the nearest millisecond</param>
        /// <returns>A <see cref="Duration"/> that represents the value</returns>
        public static Duration FromMonths(double value)
        {
            return new Duration(TimeSpan.FromDays(value * Duration.DaysInOneMonth));
        }

        /// <summary>
        /// Returns a <see cref="Duration"/> that represents a specified number of years, where the specification
        /// is accurate to the nearest millisecond.
        /// </summary>
        /// <param name="value">A number of years, accurate to the nearest millisecond</param>
        /// <returns>A <see cref="Duration"/> that represents the value</returns>
        public static Duration FromYears(double value)
        {
            return new Duration(TimeSpan.FromDays(value * Duration.DaysInOneYear));
        }

        /// <summary>
        /// Construct a new <c>Duration</c> object from a duration specified in a string.  Parameters specify the
        /// duration and the variable where the new <c>Duration</c> object is returned.
        /// </summary>
        /// <param name="duration">The string that specifies the duration.</param>
        /// <param name="result">When this method returns, this contains an object that represents the duration
        /// specified by s, or <see cref="Zero"/> if the conversion failed. This parameter is passed in
        /// uninitialized.</param>
        /// <returns>True if successfully parsed or false if the value could not be parsed</returns>
        public static bool TryParse(string duration, out Duration result)
        {
            if(!reDuration.IsMatch(duration))
            {
                result = Duration.Zero;
                return false;
            }

            try
            {
                result = new Duration(duration);
            }
            catch(OverflowException )
            {
                result = Duration.Zero;
                return false;
            }

            return true;
        }
        #endregion

        #region Equality and hash code methods
        //=====================================================================

        /// <summary>
        /// Returns a value indicating whether two specified instances of <c>Duration</c> are equal
        /// </summary>
        /// <param name="d1">The first duration to compare</param>
        /// <param name="d2">The second duration to compare</param>
        /// <returns>Returns true if the durations are equal, false if they are not</returns>
        public static bool Equals(Duration d1, Duration d2)
        {
            return d1.Equals(d2);
        }

        /// <summary>
        /// This is overridden to allow proper comparison of <c>Duration</c> objects
        /// </summary>
        /// <param name="obj">The object to which this instance is compared</param>
        /// <returns>Returns true if the object equals this instance, false if it does not</returns>
        public override bool Equals(object obj)
        {
            return (obj != null && (obj is Duration) && ts.Ticks == ((Duration)obj).Ticks);
        }

        /// <summary>
        /// Get a hash code for the duration object
        /// </summary>
        /// <remarks>To compute the hash code, it uses the string form of the object</remarks>
        /// <returns>Returns the hash code for the duration object</returns>
        public override int GetHashCode()
        {
            return this.ToString().GetHashCode();
        }
        #endregion

        #region ToString and ToDescription methods
        //=====================================================================

        /// <summary>
        /// Convert the duration instance to its ISO 8601 string form
        /// </summary>
        /// <returns>Returns the duration in ISO 8601 format</returns>
        /// <remarks>The conversion to string breaks the duration down by largest to smallest unit (years down to
        /// seconds).  As such, the string returned may not match the string it was parsed from if a unit in the
        /// input string exceeds a unit of time used when converting it back to a string (i.e. PT24H will be
        /// returned as P1D, P7W will be returned as P1M2W5D if a month is defined as 30 days).  The overloaded
        /// version can be used to convert to a string with a maximum unit of time represented.</remarks>
        /// <overloads>There are two overloads for this method</overloads>
        public override string ToString()
        {
            return this.ToString(Duration.MaxUnit.Years);
        }

        /// <summary>
        /// Convert the duration instance to its ISO 8601 string form with the specified maximum unit of time
        /// </summary>
        /// <param name="maxUnit">The maximum unit of time that should be represented in the returned string</param>
        /// <returns>Returns the duration in ISO 8601 format</returns>
        /// <remarks>The conversion to string breaks the duration down by largest to smallest unit (years down to
        /// seconds).  This version allows you to specify the maximum unit of time that should be represented.
        /// Any time in excess of the maximum time unit will be returned in the maximum time unit (i.e. PT1D will
        /// be returned as P24H for a maximum time unit of hours, P1M2W5D will be returned as P7W for a maximum
        /// time unit of weeks if a month is defined as 30 days).</remarks>
        public string ToString(MaxUnit maxUnit)
        {
            StringBuilder sb = new StringBuilder("P", 50);
            Duration d = new Duration(this.Ticks);

            int years, months, weeks, days, hours, minutes, seconds;

            if(d.Ticks < 0)
                d.TimeSpan = d.TimeSpan.Negate();

            switch(maxUnit)
            {
                case MaxUnit.Months:
                    years = 0;
                    months = (int)(d.TimeSpan.Days / Duration.DaysInOneMonth);
                    weeks = d.TimeSpan.Days - (int)(months * Duration.DaysInOneMonth);
                    days = d.TimeSpan.Days - (int)(months * Duration.DaysInOneMonth) - (int)(weeks * 7);
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Weeks:
                    years = months = 0;
                    weeks = (int)(d.TimeSpan.Days / 7);
                    days = d.TimeSpan.Days - (weeks * 7);
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Days:
                    years = months = weeks = 0;
                    days = d.TimeSpan.Days;
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Hours:
                    years = months = weeks = days = 0;
                    hours = (int)d.TimeSpan.TotalHours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Minutes:
                    years = months = weeks = days = hours = 0;
                    minutes = (int)d.TimeSpan.TotalMinutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Seconds:
                    years = months = weeks = days = hours = minutes = 0;
                    seconds = (int)d.TimeSpan.TotalSeconds;
                    break;

                default:    // Max unit = Years, all parts specified
                    years = d.Years;
                    months = d.Months;
                    weeks = d.Weeks;
                    days = d.Days;
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;
            }

            if(years != 0)
                sb.AppendFormat("{0}Y", years);

            if(months != 0)
                sb.AppendFormat("{0}M", months);

            if(weeks != 0)
                sb.AppendFormat("{0}W", weeks);

            if(days != 0)
                sb.AppendFormat("{0}D", days);

            // Append time if needed
            if(sb.Length == 1 || hours != 0 || minutes != 0 || seconds != 0)
            {
                sb.Append('T');

                if(hours != 0)
                    sb.AppendFormat("{0}H", hours);

                if(minutes != 0)
                    sb.AppendFormat("{0}M", minutes);

                // Always append seconds if there's nothing else
                if(seconds != 0 || sb.Length == 2)
                    sb.AppendFormat("{0}S", seconds);
            }

            // Negative duration?
            if(this.Ticks < 0)
                sb.Insert(0, '-');

            return sb.ToString();
        }

        /// <summary>
        /// Convert the duration instance to a text description
        /// </summary>
        /// <returns>Returns the duration description.</returns>
        /// <remarks>The conversion to string breaks the duration down by largest to smallest unit (years down to
        /// seconds).  As such, the string returned may not match the string it was parsed from if a unit in the
        /// input string exceeds a unit of time used when converting it back to a string (i.e. PT24H will be
        /// returned as 1 day, P7W will be returned as 1 month, 2 weeks, 5 days if a month is defined as 30
        /// days).  The overloaded version can be used to convert to a string with a maximum unit of time
        /// represented. </remarks>
        /// <overloads>There are two overloads for this method</overloads>
        public string ToDescription()
        {
            return this.ToDescription(Duration.MaxUnit.Years);
        }

        /// <summary>
        /// Convert the duration instance to a text description with the specified maximum unit of time
        /// </summary>
        /// <param name="maxUnit">The maximum unit of time that should be represented in the returned string</param>
        /// <returns>Returns the duration description</returns>
        /// <remarks>The conversion to string breaks the duration down by largest to smallest unit (years down to
        /// seconds).  This version allows you to specify the maximum unit of time that should be represented.
        /// Any time in excess of the maximum time unit will be returned in the maximum time unit (i.e. PT1D will
        /// be returned as 24 hours for a maximum time unit of hours, P1M2W5D will be returned as 7 weeks for a
        /// maximum time unit of weeks if a month is defined as 30 days).</remarks>
        public string ToDescription(MaxUnit maxUnit)
        {
            StringBuilder sb = new StringBuilder(50);
            Duration d = new Duration(this.Ticks);

            int years, months, weeks, days, hours, minutes, seconds;

            if(d.Ticks < 0)
                d.TimeSpan = d.TimeSpan.Negate();

            switch(maxUnit)
            {
                case MaxUnit.Months:
                    years = 0;
                    months = (int)(d.TimeSpan.Days / Duration.DaysInOneMonth);
                    weeks = d.TimeSpan.Days - (int)(months * Duration.DaysInOneMonth);
                    days = d.TimeSpan.Days - (int)(months * Duration.DaysInOneMonth) - (int)(weeks * 7);
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Weeks:
                    years = months = 0;
                    weeks = (int)(d.TimeSpan.Days / 7);
                    days = d.TimeSpan.Days - (weeks * 7);
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Days:
                    years = months = weeks = 0;
                    days = d.TimeSpan.Days;
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Hours:
                    years = months = weeks = days = 0;
                    hours = (int)d.TimeSpan.TotalHours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Minutes:
                    years = months = weeks = days = hours = 0;
                    minutes = (int)d.TimeSpan.TotalMinutes;
                    seconds = d.TimeSpan.Seconds;
                    break;

                case MaxUnit.Seconds:
                    years = months = weeks = days = hours = minutes = 0;
                    seconds = (int)d.TimeSpan.TotalSeconds;
                    break;

                default:    // Max unit = Years, all parts specified
                    years = d.Years;
                    months = d.Months;
                    weeks = d.Weeks;
                    days = d.Days;
                    hours = d.TimeSpan.Hours;
                    minutes = d.TimeSpan.Minutes;
                    seconds = d.TimeSpan.Seconds;
                    break;
            }

            if(years != 0)
            {
                sb.Append(years);
                sb.Append(LR.GetString((years == 1) ? "DurYear" : "DurYears"));
            }

            if(months != 0)
            {
                sb.Append(", ");
                sb.Append(months);
                sb.Append(LR.GetString((months == 1) ? "DurMonth" : "DurMonths"));
            }

            if(weeks != 0)
            {
                sb.Append(", ");
                sb.Append(weeks);
                sb.Append(LR.GetString((weeks == 1) ? "DurWeek" : "DurWeeks"));
            }

            if(days != 0)
            {
                sb.Append(", ");
                sb.Append(days);
                sb.Append(LR.GetString((days == 1) ? "DurDay" : "DurDays"));
            }

            // Append time if needed
            if(sb.Length == 0 || hours != 0 || minutes != 0 || seconds != 0)
            {
                if(hours != 0)
                {
                    sb.Append(", ");
                    sb.Append(hours);
                    sb.Append(LR.GetString((hours == 1) ? "DurHour" : "DurHours"));
                }

                if(minutes != 0)
                {
                    sb.Append(", ");
                    sb.Append(minutes);
                    sb.Append(LR.GetString((minutes == 1) ? "DurMinute" : "DurMinutes"));
                }

                // Always append seconds if there's nothing else
                if(seconds != 0 || sb.Length == 0)
                {
                    sb.Append(", ");
                    sb.Append(seconds);
                    sb.Append(LR.GetString((seconds == 1) ? "DurSecond" : "DurSeconds"));
                }
            }

            // Remove the leading comma separator if it's there
            if(sb[0] == ',')
                sb.Remove(0, 2);

            // Negative duration?
            if(this.Ticks < 0)
            {
                sb.Insert(0, ' ');
                sb.Insert(0, LR.GetString("DurNegative"));
            }

            return sb.ToString();
        }
        #endregion

        #region Comparison and operator overloads
        //=====================================================================

        /// <summary>
        /// Compares this instance to a specified object and returns an indication of their relative values
        /// </summary>
        /// <param name="obj">A Duration object to compare</param>
        /// <returns>Returns -1 if this instance is less than the value, 0 if they are equal, or 1 if this
        /// instance is greater than the value or the value is null.</returns>
        /// <exception cref="ArgumentException">This is thrown if the object to be compared is not a Duration</exception>
        public int CompareTo(object obj)
        {
              if(!(obj is Duration))
                throw new ArgumentException(LR.GetString("ExDurBadCompareObject"));

            return Duration.Compare(this, (Duration)obj);
        }

        /// <summary>
        /// Compares two <c>Duration</c> values and returns an integer that indicates their relationship
        /// </summary>
        /// <param name="d1">The first duration</param>
        /// <param name="d2">The second duration</param>
        /// <returns>Returns -1 if the first instance is less than the second, 0 if they are equal, or 1 if the
        /// first instance is greater than the second.</returns>
        public static int Compare(Duration d1, Duration d2)
        {
            if(d1.TimeSpan.Ticks > d2.TimeSpan.Ticks)
                return 1;

            if(d1.TimeSpan.Ticks < d2.TimeSpan.Ticks)
                return -1;

            return 0;
        }

        /// <summary>
        /// Overload for equal operator
        /// </summary>
        /// <param name="d1">The first duration object</param>
        /// <param name="d2">The second duration object</param>
        /// <returns>True if equal, false if not</returns>
        public static bool operator == (Duration d1, Duration d2)
        {
            return (Duration.Compare(d1, d2) == 0);
        }

        /// <summary>
        /// Overload for not equal operator
        /// </summary>
        /// <param name="d1">The first duration object</param>
        /// <param name="d2">The second duration object</param>
        /// <returns>True if not equal, false if they are equal</returns>
        public static bool operator != (Duration d1, Duration d2)
        {
            return (Duration.Compare(d1, d2) != 0);
        }

        /// <summary>
        /// Overload for less than operator
        /// </summary>
        /// <param name="d1">The first duration object</param>
        /// <param name="d2">The second duration object</param>
        /// <returns>True if r1 is less than r2, false if not</returns>
        public static bool operator < (Duration d1, Duration d2)
        {
            return (Duration.Compare(d1, d2) < 0);
        }

        /// <summary>
        /// Overload for greater than operator
        /// </summary>
        /// <param name="d1">The first duration object</param>
        /// <param name="d2">The second duration object</param>
        /// <returns>True if r1 is greater than r2, false if not</returns>
        public static bool operator > (Duration d1, Duration d2)
        {
            return (Duration.Compare(d1, d2) > 0);
        }

        /// <summary>
        /// Overload for less than or equal operator
        /// </summary>
        /// <param name="d1">The first duration object</param>
        /// <param name="d2">The second duration object</param>
        /// <returns>True if r1 is less than or equal r2, false if not</returns>
        public static bool operator <= (Duration d1, Duration d2)
        {
            return (Duration.Compare(d1, d2) <= 0);
        }

        /// <summary>
        /// Overload for greater than or equal operator
        /// </summary>
        /// <param name="d1">The first duration object</param>
        /// <param name="d2">The second duration object</param>
        /// <returns>True if r1 is greater than or equal r2, false if not</returns>
        public static bool operator >= (Duration d1, Duration d2)
        {
            return (Duration.Compare(d1, d2) >= 0);
        }
        #endregion
    }
}
