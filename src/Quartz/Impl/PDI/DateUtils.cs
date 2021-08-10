//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : DateUtils.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/22/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a sealed class that contains various helpful date utility methods used by other classes in
// the PDI library.
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
using System.Globalization;
using System.Text.RegularExpressions;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This is a sealed class that contains various helpful date utility methods used by other classes in the
    /// PDI library.
    /// </summary>
    public static class DateUtils
    {
        #region Private data members
        //=====================================================================

        // These are used to parse date/time strings in ISO 8601 format
        private static Regex reISO8601 = new Regex(@"^\s*(?<Year>\d{4})-?" +
            @"(?<Month>\d{2})-?(?<Day>\d{2})(?<Time>T(?<Hour>\d{2}):?" +
            @"(?<Minutes>\d{2})?:?(?<Seconds>\d{2}(\.\d*)?)?)?((?<Zulu>Z)|" +
            @"((?<TZHours>(\+|-)\d{2})(:?(?<TZMinutes>\d{2})?)))?\s*$",
            RegexOptions.IgnoreCase);

        private static Regex reTimeZone = new Regex(
            @"^\s*(?<TZHours>(\+|-)\d{2})(:?(?<TZMinutes>\d{2})?)(:?(?<TZSeconds>\d{2})?)\s*$");

        #endregion

        #region Methods
        //=====================================================================

        /// <summary>
        /// This can be used to explicitly convert a <see cref="System.DayOfWeek"/> value to a <see cref="DaysOfWeek"/>
        /// value.
        /// </summary>
        /// <param name="dow">The <c>DayOfWeek</c> value to convert</param>
        /// <returns>Returns the <c>DayOfWeek</c> value as a <c>DaysOfWeek</c> value</returns>
        public static DaysOfWeek ToDaysOfWeek(System.DayOfWeek dow)
        {
            DaysOfWeek d;

            switch(dow)
            {
                case DayOfWeek.Sunday:
                    d = DaysOfWeek.Sunday;
                    break;

                case DayOfWeek.Monday:
                    d = DaysOfWeek.Monday;
                    break;

                case DayOfWeek.Tuesday:
                    d = DaysOfWeek.Tuesday;
                    break;

                case DayOfWeek.Wednesday:
                    d = DaysOfWeek.Wednesday;
                    break;

                case DayOfWeek.Thursday:
                    d = DaysOfWeek.Thursday;
                    break;

                case DayOfWeek.Friday:
                    d = DaysOfWeek.Friday;
                    break;

                case DayOfWeek.Saturday:
                    d = DaysOfWeek.Saturday;
                    break;

                default:
                    d = DaysOfWeek.None;    // Not a DayOfWeek value;
                    break;
            }

            return d;
        }

        /// <summary>
        /// This can be used to explicitly convert a <see cref="DaysOfWeek"/> value to a <see cref="System.DayOfWeek"/>
        /// value.
        /// </summary>
        /// <param name="days">The <c>DaysOfWeek</c> value to convert</param>
        /// <returns>Returns the <c>DaysOfWeek</c> value as a <c>DayOfWeek</c> value.  If the <c>DaysOfWeek</c>
        /// value is a combination of days, only the first day of the week found is returned.  If it is set to
        /// <c>None</c>, it returns Sunday.</returns>
        public static DayOfWeek ToDayOfWeek(DaysOfWeek days)
        {
            DayOfWeek d;

            if((days & DaysOfWeek.Sunday) != 0)
                d = DayOfWeek.Sunday;
            else if((days & DaysOfWeek.Monday) != 0)
                d = DayOfWeek.Monday;
            else if((days & DaysOfWeek.Tuesday) != 0)
                d = DayOfWeek.Tuesday;
            else if((days & DaysOfWeek.Wednesday) != 0)
                d = DayOfWeek.Wednesday;
            else if((days & DaysOfWeek.Thursday) != 0)
                d = DayOfWeek.Thursday;
            else if((days & DaysOfWeek.Friday) != 0)
                d = DayOfWeek.Friday;
            else if((days & DaysOfWeek.Saturday) != 0)
                d = DayOfWeek.Saturday;
            else
                d = DayOfWeek.Sunday;   // Was set to None

            return d;
        }

        /// <summary>
        /// This method is used to calculate the date on which a fixed day occurs (for example, July 4th).
        /// However, it adjusts the date to the preceding Friday if the date falls on a Saturday or the following
        /// Monday if the date falls on a Sunday.
        /// </summary>
        /// <remarks>The <see cref="FixedHoliday"/> class uses this method to calculate the date on which a
        /// holiday is observed rather than the actual date on which it falls when its
        /// <see cref="FixedHoliday.AdjustFixedDate"/> property is set to true (i.e. to calculate holiday dates
        /// for a business).</remarks>
        /// <param name="year">The year in which the day occurs</param>
        /// <param name="month">The month in which the day occurs</param>
        /// <param name="day">The day of the month on which the day occurs</param>
        /// <returns>Returns a <see cref="DateTime" /> object that represents the date calculated from the
        /// settings adjusted if necessary so that it does not occur on a weekend.</returns>
        /// <example>
        /// <code language="cs">
        /// // Returns 07/04/2003 (7/4 falls on a Friday).
        /// dtDate = DateUtils.CalculateFixedDate(2003, 7, 4);
        ///
        /// // Returns 07/05/2004 (7/4 falls on a Sunday so it adjusts it forward
        /// // to the Monday).
        /// dtDate = DateUtils.CalculateFixedDate(2004, 7, 4);
        /// </code>
        /// <code language="vbnet">
        /// ' Returns 07/04/2003 (7/4 falls on a Friday).
        /// dtDate = DateUtils.CalculateFixedDate(2003, 7, 4)
        ///
        /// ' Returns 07/05/2004 (7/4 falls on a Sunday so it adjusts it forward
        /// ' to the Monday).
        /// dtDate = DateUtils.CalculateFixedDate(2004, 7, 4)
        /// </code>
        /// </example>
        public static DateTime CalculateFixedDate(int year, int month, int day)
        {
            // Get the date and the day if falls on
            DateTime dtDate = new DateTime(year, month, day);
            DayOfWeek dowDay = dtDate.DayOfWeek;

            // Adjust for weekend if necessary
            if(dowDay == DayOfWeek.Sunday)
                dtDate = dtDate.AddDays(1);
            else
                if(dowDay == DayOfWeek.Saturday)
                    dtDate = dtDate.AddDays(-1);

            return dtDate;
        }

        /// <summary>
        /// This method is used to calculate the date on which a floating day occurs (for example, the 4th
        /// Thursday in November).
        /// </summary>
        /// <param name="year">The year in which the day occurs.</param>
        /// <param name="month">The month in which the day occurs.</param>
        /// <param name="occur">The occurrence of the day of the week on which the day falls.</param>
        /// <param name="dowDay">The day of the week on which the day occurs.</param>
        /// <param name="offset">The number of days before or after the calculated date on which the day actually
        /// falls.</param>
        /// <returns>Returns a <see cref="DateTime" /> object that represents the date calculated from the
        /// settings.</returns>
        /// <remarks><para>Use a positive value for the <c>nOffset</c> parameter for a number of days after the
        /// calculated date or a negative number for a number of days before the calculated date.</para>
        /// 
        /// <para>Normally, this value will be zero so that the calculated date is the actual date returned.
        /// However, in cases where a date is calculated in terms of the number of days before or after a given
        /// date, this can be set to the offset to adjust the calculated date.</para>
        /// 
        /// <para>For example, to calculate the day after Thanksgiving, the value of this parameter would be set
        /// to 1 (one day after Thanksgiving, which is the 4th Thursday in November). You cannot use the 4th
        /// Friday to calculate the date because if the month starts on a Friday, the calculated date would be a
        /// week too early. As such, the <c>nOffset</c> parameter is used instead.</para></remarks>
        /// <example>
        /// <code language="cs">
        /// // Returns 11/28/2002 (Thanksgiving)
        /// dtThanksgiving = DateUtils.CalculateFloatingDate(2002, 11,
        ///     DayOccurrence.Fourth, DayOfWeek.Thursday, 0);
        ///
        /// // Returns 11/29/2002 (Day after Thanksgiving)
        /// dtDayAfterTG = DateUtils.CalculateFloatingDate(2002, 11,
        ///     DayOccurrence.Fourth, DayOfWeek.Thursday, 1);
        ///
        /// // Returns 11/22/2002 (Fourth Friday isn't after the fourth
        /// // Thursday in 2002 hence the use of the nOffset parameter
        /// // in the call above).
        /// dtFourthFri = DateUtils.CalculateFloatingDate(2002, 11,
        ///     DayOccurrence.Fourth, DayOfWeek.Friday, 0);
        /// </code>
        /// <code language="vbnet">
        /// ' Returns 11/28/2002 (Thanksgiving)
        /// dtThanksgiving = DateUtils.CalculateFloatingDate(2002, 11,
        ///     DayOccurrence.Fourth, DayOfWeek.Thursday, 0)
        ///
        /// ' Returns 11/29/2002 (Day after Thanksgiving)
        /// dtDayAfterTG = DateUtils.CalculateFloatingDate(2002, 11,
        ///     DayOccurrence.Fourth, DayOfWeek.Thursday, 1)
        ///
        /// ' Returns 11/22/2002 (Fourth Friday isn't after the fourth
        /// ' Thursday in 2002 hence the use of the nOffset parameter
        /// ' in the call above).
        /// dtFourthFri = DateUtils.CalculateFloatingDate(2002, 11,
        ///     DayOccurrence.Fourth, DayOfWeek.Friday, 0)
        /// </code>
        /// </example>
        /// <exception cref="ArgumentException">This is thrown if <c>None</c> is passed for the <c>DayOccurrence</c>
        /// parameter.</exception>
        public static DateTime CalculateFloatingDate(int year, int month, DayOccurrence occur,
          System.DayOfWeek dowDay, int offset)
        {
            DateTime dtDate;

            if(occur == DayOccurrence.None)
                throw new ArgumentException(LR.GetString("ExDUOccurIsNone"), nameof(occur));

            // Calculating a specific occurrence or the last one?
            if(occur != DayOccurrence.Last)
            {
                // Specific occurrence
                dtDate = new DateTime(year, month, 1);
                dtDate = dtDate.AddDays((((int)dowDay + 7 - (int)dtDate.DayOfWeek) % 7) + (((int)occur - 1) * 7));
            }
            else
            {
                // Get the last occurrence of the month
                dtDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));
                dtDate = dtDate.AddDays(0 - (((int)dtDate.DayOfWeek + 7 - (int)dowDay) % 7));
            }

            // Return the date plus any additional offset
            return dtDate.AddDays(offset);
        }

        /// <summary>
        /// This method is used to calculate the date on which a specific occurrence of any of a set of days
        /// occurs (for example, the 4th weekday in November or the last weekend day in January).
        /// </summary>
        /// <param name="year">The year in which the day occurs</param>
        /// <param name="month">The month in which the day occurs</param>
        /// <param name="occur">The occurrence of the day of the week on which the day falls</param>
        /// <param name="days">The day(s) of the week on which the day can occurs</param>
        /// <param name="offset">The number of days before or after the calculated date on which the day falls</param>
        /// <returns>Returns a <see cref="DateTime" /> object that represents the date calculated from the
        /// settings.</returns>
        /// <remarks><para>This method is intended for use in finding an occurrence of any one of a set of days
        /// of the week and is normally used with the <see cref="DaysOfWeek.Weekdays"/> or
        /// <see cref="DaysOfWeek.Weekends"/> day of week value.  However, the days of week parameter can be any
        /// valid combination of days including an individual day of the week.</para>
        /// 
        /// <para>Use a positive value for the <c>nOffset</c> parameter for a number of days after the calculated
        /// date or a negative number for a number of days before the calculated date.</para>
        /// 
        /// <para>Normally, this value will be zero so that the calculated date is the actual date returned.
        /// However, in cases where a date is calculated in terms of the number of days before or after a given
        /// date, this can be set to the offset to adjust the calculated date.  Note that if used, the date
        /// returned may not be on one of the days of the week specified to calculate the original unadjusted
        /// date.</para></remarks>
        /// <example>
        /// <code language="cs">
        /// // Returns 01/06/2004 (fourth weekday in Jan 2004)
        /// dtFourthWeekday = DateUtils.CalculateOccurrenceDate(2004, 1,
        ///     DayOccurrence.Fourth, DaysOfWeek.Weekdays, 0);
        ///
        /// // Returns 01/08/2004 (fourth weekday plus 2 days)
        /// dtPlusTwo = DateUtils.CalculateOccurrenceDate(2004, 1,
        ///     DayOccurrence.Fourth, DaysOfWeek.Weekdays, 2);
        /// </code>
        /// <code language="vbnet">
        /// ' Returns 01/06/2004 (fourth weekday in Jan 2004)
        /// dtFourthWeekday = DateUtils.CalculateOccurrenceDate(2004, 1,
        ///     DayOccurrence.Fourth, DaysOfWeek.Weekdays, 0)
        ///
        /// ' Returns 01/08/2004 (fourth weekday plus 2 days)
        /// dtPlusTwo = DateUtils.CalculateOccurrenceDate(2004, 1,
        ///     DayOccurrence.Fourth, DaysOfWeek.Weekdays, 2)
        /// </code>
        /// </example>
        /// <exception cref="ArgumentException">This is thrown if <c>None</c> is passed for the <c>DayOccurrence</c>
        /// parameter.</exception>
        public static DateTime CalculateOccurrenceDate(int year, int month, DayOccurrence occur, DaysOfWeek days,
          int offset)
        {
            DateTime dtDate;
            int count = 0, occurrence = (int)occur;

            if(occur == DayOccurrence.None)
                throw new ArgumentException(LR.GetString("ExDUOccurIsNone"), nameof(occur));

            // Calculating a specific occurrence or the last one?
            if(occur != DayOccurrence.Last)
            {
                dtDate = new DateTime(year, month, 1);

                while(count != occurrence)
                {
                    switch(dtDate.DayOfWeek)
                    {
                        case DayOfWeek.Sunday:
                            if((days & DaysOfWeek.Sunday) != 0)
                                count++;
                            break;

                        case DayOfWeek.Monday:
                            if((days & DaysOfWeek.Monday) != 0)
                                count++;
                            break;

                        case DayOfWeek.Tuesday:
                            if((days & DaysOfWeek.Tuesday) != 0)
                                count++;
                            break;

                        case DayOfWeek.Wednesday:
                            if((days & DaysOfWeek.Wednesday) != 0)
                                count++;
                            break;

                        case DayOfWeek.Thursday:
                            if((days & DaysOfWeek.Thursday) != 0)
                                count++;
                            break;

                        case DayOfWeek.Friday:
                            if((days & DaysOfWeek.Friday) != 0)
                                count++;
                            break;

                        case DayOfWeek.Saturday:
                            if((days & DaysOfWeek.Saturday) != 0)
                                count++;
                            break;
                    }

                    if(count != occurrence)
                        dtDate = dtDate.AddDays(1);
                }
            }
            else
            {
                // Find last occurrence
                count++;
                dtDate = new DateTime(year, month, DateTime.DaysInMonth(year, month));

                while(count != 0)
                {
                    switch(dtDate.DayOfWeek)
                    {
                        case DayOfWeek.Sunday:
                            if((days & DaysOfWeek.Sunday) != 0)
                                count--;
                            break;

                        case DayOfWeek.Monday:
                            if((days & DaysOfWeek.Monday) != 0)
                                count--;
                            break;

                        case DayOfWeek.Tuesday:
                            if((days & DaysOfWeek.Tuesday) != 0)
                                count--;
                            break;

                        case DayOfWeek.Wednesday:
                            if((days & DaysOfWeek.Wednesday) != 0)
                                count--;
                            break;

                        case DayOfWeek.Thursday:
                            if((days & DaysOfWeek.Thursday) != 0)
                                count--;
                            break;

                        case DayOfWeek.Friday:
                            if((days & DaysOfWeek.Friday) != 0)
                                count--;
                            break;

                        case DayOfWeek.Saturday:
                            if((days & DaysOfWeek.Saturday) != 0)
                                count--;
                            break;
                    }

                    if(count != 0)
                        dtDate = dtDate.AddDays(-1);
                }
            }

            // Return the date plus any additional offset
            return dtDate.AddDays(offset);
        }

        /// <summary>
        /// This method returns a date calculated from a year, a week number, and the day on which weeks start
        /// </summary>
        /// <param name="year">The year containing the week</param>
        /// <param name="week">The week of the year</param>
        /// <param name="dow">The day on which a week starts</param>
        /// <param name="offset">An additional offset in days to add to the week start date</param>
        /// <returns>Returns the calculated date.</returns>
        /// <remarks><para>A week is defined as a seven day period starting on the specified day of the week.
        /// The first week of the year is defined as the one starting on the specified day of the week and
        /// containing at least four days of the year.</para>
        /// 
        /// <para>If the year starts on a day of the week later than the one specified, this may return a date a
        /// few days earlier than January 1st of the specified year for week 1.</para>
        /// 
        /// <para>Not all years will have a 53rd week.  For example, assuming a Monday week start, week 53 can
        /// only occur when Thursday is January 1st or if it is a leap year and Wednesday is January 1st.  It is
        /// up to the caller to determine this and discard the date.  <see cref="WeeksInYear"/> can be used to
        /// determine this condition.</para></remarks>
        /// <exception cref="ArgumentOutOfRangeException">An exception is thrown if the week value is not between
        /// 1 and 53.</exception>
        /// <example>
        /// <code language="cs">
        /// // Returns Monday 12/29/1997 because 01/01/1998 falls on a Thursday
        /// dtDate = DateUtils.DateFromWeek(1998, 1, DayOfWeek.Monday, 0);
        ///
        /// // Returns 08/27/2003
        /// dtDate = DateUtils.DateFromWeek(2003, 35, DayOfWeek.Wednesday, 0);
        /// </code>
        /// <code language="vbnet">
        /// ' Returns Monday 12/29/1997 because 01/01/1998 falls on a Thursday
        /// dtDate = DateUtils.DateFromWeek(1998, 1, DayOfWeek.Monday, 0)
        ///
        /// ' Returns 08/27/2003
        /// dtDate = DateUtils.DateFromWeek(2003, 35, DayOfWeek.Wednesday, 0)
        /// </code>
        /// </example>
        /// <seealso cref="WeeksInYear"/>
        /// <seealso cref="WeekFromDate"/>
        public static DateTime DateFromWeek(int year, int week, DayOfWeek dow, int offset)
        {
            DateTime dt;
            int weekOffset;

            // Validate week number
            if(week < 1 || week > 53)
                throw new ArgumentOutOfRangeException(nameof(week), week, LR.GetString("ExDUBadWeekNumber"));

            // Bit of a hack but if you're looking for dates this far out, I'd like to know what the hell you're
            // doing.
            if(year > 9998)
                year = 9999;

            if(year == 9999 && week == 53)
                week = 52;

            // Find out the weekday of Jan 1st in the year
            dt = new DateTime(year, 1, 1);

            // Calculate the offset for the first full week
            weekOffset = ((int)dow + 7 - (int)dt.DayOfWeek) % 7;

            // Adjust it back a week if the prior week contains at least four days
            if(weekOffset >= 4)
                weekOffset -= 7;

            // Get the start of the week requested
            dt = dt.AddDays(weekOffset);

            if(week != 1)
                dt = dt.AddDays(7 * (week - 1));

            // Add in the additional offset if requested
	        if(offset != 0)
                dt = dt.AddDays(offset);

            return dt;
        }

        /// <summary>
        /// This is used to determine the number of weeks in a year
        /// </summary>
        /// <param name="year">The year in which to get the week count</param>
        /// <param name="dow">The day on which a week starts</param>
        /// <returns>Returns the week count (52 or 53).</returns>
        /// <remarks><para>A week is defined as a seven day period starting on the specified day of the week.
        /// The first week of the year is defined as the one starting on the specified day of the week and
        /// containing at least four days of the year.</para>
        /// 
        /// <para>Not all years will have a 53rd week.  For example, assuming a Monday week start, week 53 can
        /// only occur when Thursday is January 1st or if it is a leap year and Wednesday is January 1st.</para>
        /// </remarks>
        /// <seealso cref="DateFromWeek"/>
        /// <seealso cref="WeekFromDate"/>
        public static int WeeksInYear(int year, DayOfWeek dow)
        {
            // Bit of a hack but if you're looking for dates this far out, I'd like to know what the hell you're
            // doing.
            if(year > 9998)
                return 52;

            DateTime week53 = DateUtils.DateFromWeek(year, 53, dow, 0);
            DateTime week1 = DateUtils.DateFromWeek(year + 1, 1, dow, 0);

            return (week1 == week53) ? 52 : 53;
        }

        /// <summary>
        /// This can be used to determine the week of the year in which a specified date falls
        /// </summary>
        /// <param name="weekDate">The date to use when determining the week</param>
        /// <param name="dow">The day on which a week starts</param>
        /// <returns>Returns a week number between 1 and 53</returns>
        /// <remarks><para>A week is defined as a seven day period starting on the specified day of the week.
        /// The first week of the year is defined as the one starting on the specified day of the week containing
        /// at least four days of the year.</para>
        /// 
        /// <para>Not all years will have a 53rd week.  For example, assuming a Monday week start, week 53 can
        /// only occur when Thursday is January 1st or if it is a leap year and Wednesday is January 1st.</para>
        /// </remarks>
        /// <seealso cref="DateFromWeek"/>
        /// <seealso cref="WeeksInYear"/>
        public static int WeekFromDate(DateTime weekDate, DayOfWeek dow)
        {
            DateTime week1 = DateUtils.DateFromWeek(weekDate.Year, 1, dow, 0);
            TimeSpan ts = weekDate - week1;
            int week = (ts.Days / 7) + 1;

            // If it gets 53 and there aren't 53 weeks in the year, set it to week 1 of the next year
            if(week == 53 && DateUtils.WeeksInYear(weekDate.Year, dow) != 53)
                week = 1;

            return week;
        }

        /// <summary>
        /// This method can be used to calculate the Easter Sunday date using one of three methods defined by
        /// <see cref="EasterMethod"/>.
        /// </summary>
        /// <remarks>See the <see cref="EasterMethod"/> enumerated type for the ways in which Easter Sunday can
        /// be calculated.  A number of days can be added to or subtracted from the returned date to find other
        /// Easter related dates (i.e. subtract two from the returned date to get the date on which Good Friday
        /// falls).</remarks>
        /// <param name="year">The year in which to calculate Easter</param>
        /// <param name="method">The method to use for calculating Easter</param>
        /// <returns>The date on which Easter falls in the specified year as calculated using the specified
        /// method.</returns>
        /// <exception cref="ArgumentOutOfRangeException">An exception is thrown if the year is not between 1583
        /// and 4099 for the Orthodox and Gregorian methods or if the year is less than 326 for the Julian
        /// method.</exception>
        /// <example>
        /// <code language="cs">
        /// // Returns 04/11/2004 for Easter
        /// dtEaster = DateUtils.EasterSunday(2004, EasterMethod.Gregorian);
        ///
        /// // Calculate Good Friday from that date
        /// dtGoodFriday = dtEaster.AddDays(-2);
        /// </code>
        /// <code language="vbnet">
        /// ' Returns 04/11/2004 for Easter
        /// dtEaster = DateUtils.EasterSunday(2004, EasterMethod.Gregorian)
        ///
        /// ' Calculate Good Friday from that date
        /// dtGoodFriday = dtEaster.AddDays(-2)
        /// </code>
        /// </example>
        public static DateTime EasterSunday(int year, EasterMethod method)
        {
            int century, yearRem, temp, pfm, tabc, day, month;

            // Validate year
            if(method == EasterMethod.Julian && year < 326)
                throw new ArgumentOutOfRangeException(nameof(year), year, LR.GetString("ExDUBadJulianYear"));

            if(method != EasterMethod.Julian && (year < 1583 || year > 4099))
                throw new ArgumentOutOfRangeException(nameof(year), year, LR.GetString("ExDUBadOrthGregYear"));

            century = year / 100;     // Get century
            yearRem = year % 19;      // Get remainder of year / 19

        	if(method != EasterMethod.Gregorian)
            {
              	// Calculate PFM date (Julian and Orthodox)
          		pfm = ((225 - 11 * yearRem) % 30) + 21;

          	    // Find the next Sunday
          		temp = year % 100;
          		day = pfm + ((20 - ((pfm - 19) % 7) - ((40 - century) % 7) - ((temp + (temp / 4)) % 7)) % 7) + 1;

                // Convert Julian to Gregorian date for Orthodox method
          		if(method == EasterMethod.Orthodox)
                {
          			// Ten days were skipped in the Gregorian calendar from October 5-14, 1582
          			day += 10;

          			// Only one in every four century years are leap years in the Gregorian calendar.  Every
                    // century is a leap year in the Julian calendar.
          			if(year > 1600)
                        day += (century - 16 - ((century - 16) / 4));
          		}
            }
        	else
            {
                // Calculate PFM date (Gregorian)
                temp = ((century - 15) / 2) + 202 - (11 * yearRem);

                switch(century)
                {
                    case 21:
                    case 24:
                    case 25:
                    case 27:
                    case 28:
                    case 29:
                    case 30:
                    case 31:
                    case 32:
                    case 34:
                    case 35:
                    case 38:
                        temp--;
                        break;

                    case 33:
                    case 36:
                    case 37:
                    case 39:
                    case 40:
                        temp -= 2;
                        break;

                    default:
                        break;
                }

                temp %= 30;

                pfm = temp + 21;

                if(temp == 29 || (temp == 28 && yearRem > 10))
                    pfm--;

                // Find the next Sunday
                tabc = (40 - century) % 4;

                if(tabc == 3)
                    tabc++;

                if(tabc > 1)
                    tabc++;

                temp = year % 100;

                day = pfm + ((20 - ((pfm - 19) % 7) - tabc - ((temp + (temp / 4)) % 7)) % 7) + 1;
            }

            // Return the date
            if(day > 61)
            {
                day -= 61;
                month = 5;     // Can occur in May for EasterMethod.Orthodox
            }
            else
                if(day > 31)
                {
                    day -= 31;
                    month = 4;
                }
                else
                    month = 3;

            return new DateTime(year, month, day);
        }

        /// <summary>
        /// This method is used to convert an ISO 8601 formatted date string to a DateTime value that it
        /// represents.
        /// </summary>
        /// <param name="dateTimeText">The ISO 8601 formatted date to parse.</param>
        /// <param name="toLocalTime">If true and the string is in a universal time format, the value is
        /// converted to local time before being returned.  If false, it is returned as a universal time
        /// value.</param>
        /// <returns>The specified string converted to a local date/time</returns>
        /// <exception cref="ArgumentException">This is thrown if the specified date/time string is not valid</exception>
        /// <remarks><para>The <see cref="System.DateTime.Parse(String)"/> method is capable of parsing a string
        /// in a very specific layout of the ISO 8601 format (SortableDateTimePattern).  However, if the string
        /// deviates even slightly from that pattern, it cannot be parsed.  This method takes an ISO 8601
        /// formatted date string in any of various formats and returns the DateTime value that it represents.</para>
        /// 
        /// <para>This method does not handle all possible forms of the ISO 8601 format, just those related to
        /// date and time values (<c>YYYY-MM-DDTHH:MM:SS.MMMM+HH:MM</c>).  Date and time separators (except the
        /// 'T') are optional as is the time zone specifier.  The time indicator ('T') and the time value can be
        /// omitted entirely if not needed.  A 'Z' (Zulu) can appear in place of the time zone specifier to
        /// indicate universal time.  Date/times in universal time format or with a time zone offset are
        /// converted to local time if the <c>bToLocalTime</c> parameter is set to true.  All other values are
        /// assumed to be local time already and will be returned unmodified as are date-only values.</para></remarks>
        public static DateTime FromISO8601String(string dateTimeText, bool toLocalTime)
        {
            DateTime convertedDate;
            int year, day, month, hour = 0, minutes = 0, wholeSeconds = 0, milliseconds = 0;
            double fracSeconds;

            Match m = reISO8601.Match(dateTimeText);

            if(!m.Success)
                throw new ArgumentException(LR.GetString("ExDUBadISOFormat"), nameof(dateTimeText));

            // Day parts must be there
            year = Convert.ToInt32(m.Groups["Year"].Value, CultureInfo.InvariantCulture);
            month = Convert.ToInt32(m.Groups["Month"].Value, CultureInfo.InvariantCulture);
            day = Convert.ToInt32(m.Groups["Day"].Value, CultureInfo.InvariantCulture);

            // Sometimes we get a bad date with parts outside their respective ranges.  Rather than throw an
            // exception, we'll restrict them to the minimum or maximum value.
            if(year < 1)
                year = 1;

            if(month < 1)
                month = 1;
            else
                if(month > 12)
                    month = 12;

            if(day < 1)
                day = 1;
            else
                if(day > DateTime.DaysInMonth(year, month))
                    day = DateTime.DaysInMonth(year, month);

            // Time parts are optional
            if(m.Groups["Hour"].Value.Length != 0)
            {
                hour = Convert.ToInt32(m.Groups["Hour"].Value, CultureInfo.InvariantCulture);

                if(m.Groups["Minutes"].Value.Length != 0)
                {
                    minutes = Convert.ToInt32(m.Groups["Minutes"].Value, CultureInfo.InvariantCulture);

                    if(m.Groups["Seconds"].Value.Length != 0)
                    {
                        fracSeconds = Convert.ToDouble(m.Groups["Seconds"].Value, CultureInfo.InvariantCulture);
                        wholeSeconds = (int)fracSeconds;
                        milliseconds = (int)((fracSeconds - wholeSeconds) * 1000);
                    }
                }
            }

            convertedDate = new DateTime(year, month, day);

            // Sometimes we get something like 240000 to represent the time for a whole day.  By adding the time
            // parts as a time span, we bypass any potential problems with the DateTime() constructor that
            // expects the time parts to be within bounds.
            convertedDate += new TimeSpan(0, hour, minutes, wholeSeconds, milliseconds);

            // Convert to local time if necessary
            if(m.Groups["Zulu"].Value.Length != 0)
            {
                if(toLocalTime)
                   convertedDate = convertedDate.ToLocalTime();
            }
            else
                if(m.Groups["TZHours"].Value.Length != 0)
                {
                    // If a time zone offset was specified, add it to the time to get UTC
                    hour = Convert.ToInt32(m.Groups["TZHours"].Value, CultureInfo.InvariantCulture);

                    if(m.Groups["TZMinutes"].Value.Length == 0)
                        minutes = 0;
                    else
                        minutes = Convert.ToInt32(m.Groups["TZMinutes"].Value, CultureInfo.InvariantCulture);

                    convertedDate = convertedDate.AddMinutes(0 - (hour * 60 + minutes));

                    if(toLocalTime)
                        convertedDate = convertedDate.ToLocalTime();
                }

            return convertedDate;
        }

        /// <summary>
        /// This method is used to see if an ISO 8601 formatted date string is in floating format (a universal
        /// time indicator or time zone offset is not specified).
        /// </summary>
        /// <param name="dateTimeText">The ISO 8601 formatted date to parse</param>
        /// <returns>Returns true if the date/time has no universal time indicator (Z) or time zone offset
        /// (+/-HHMM) or if it has no time (assumes midnight local time).  Returns false if there is a universal
        /// time indicator or a time zone offset.</returns>
        /// <exception cref="ArgumentException">This is thrown if the specified date/time string is not valid</exception>
        public static bool IsFloatingFormat(string dateTimeText)
        {
            Match m = reISO8601.Match(dateTimeText);

            if(!m.Success)
                throw new ArgumentException(LR.GetString("ExDUBadISOFormat"), nameof(dateTimeText));

            if(m.Groups["Zulu"].Value.Length != 0 || m.Groups["TZHours"].Value.Length != 0)
                return false;

            return true;
        }

        /// <summary>
        /// This method is used to convert an ISO 8601 time zone string into a <see cref="System.TimeSpan"/>
        /// object.
        /// </summary>
        /// <param name="timeZone">The ISO 8601 formatted time zone to parse</param>
        /// <returns>The specified string converted to a time span</returns>
        /// <remarks>The string should be in the form +HH:MM or -HH:MM.  The sign (+/-) and time separator (:)
        /// are optional as are the minutes.  If minutes are not present, they are defaulted to zero.</remarks>
        /// <exception cref="ArgumentException">This is thrown if the specified time zone string is not valid</exception>
        public static TimeSpan FromISO8601TimeZone(string timeZone)
        {
            TimeSpan ts;
            int hour, minutes = 0, seconds = 0;

            Match m = reTimeZone.Match(timeZone);

            if(!m.Success)
                throw new ArgumentException(LR.GetString("ExDUBadISOTZFormat"), nameof(timeZone));

            // Hour must be there, minutes are optional
            hour = Convert.ToInt32(m.Groups["TZHours"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["TZMinutes"].Value.Length != 0)
                minutes = Convert.ToInt32(m.Groups["TZMinutes"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["TZSeconds"].Value.Length != 0)
                seconds = Convert.ToInt32(m.Groups["TZSeconds"].Value, CultureInfo.InvariantCulture);

            if(m.Groups["TZHours"].Value[0] == '-')
                ts = new TimeSpan(hour, 0 - minutes, 0 - seconds);
            else
                ts = new TimeSpan(hour, minutes, seconds);

            return ts;
        }
        #endregion
    }
}
