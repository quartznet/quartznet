//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Holidays.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 10/17/2014
// Note    : Copyright 2003-2014, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a holiday collection class.  The collection is serializable.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 07/10/2003  EFW  Created the code
// 03/05/2007  EFW  Converted to use a generic base class
// 10/17/2014  EFW  Updated for use with .NET 4.0
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Xml.Serialization;

#nullable disable
namespace EWSoftware.PDI
{
	/// <summary>
	/// A type-safe collection of <see cref="Holiday"/> objects
	/// </summary>
    /// <remarks>Besides the standard collection methods, this class also contains some utility methods used to
    /// see if a specific date is a holiday (<see cref="IsHoliday"/>), get a description for a date if it is a
    /// holiday (<see cref="HolidayDescription"/>), and get a list of dates based on the holiday entries
    /// (<see cref="HolidaysBetween"/>).  The class also has a type-safe enumerator and is serializable.</remarks>
    /// <include file='DateExamples.xml' path='Examples/Holiday/HelpEx[@name="Ex1"]/*' />
    [Serializable]
    [XmlInclude(typeof(FixedHoliday)), XmlInclude(typeof(FloatingHoliday))]
    public class HolidayCollection : Collection<Holiday>
    {
        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <overloads>There are two overloads for the constructor</overloads>
        public HolidayCollection()
        {
        }

        /// <summary>
        /// Construct the collection from an enumerable list of <see cref="Holiday"/> objects
        /// </summary>
        /// <param name="holidays">The enumerable list of holidays to add</param>
        public HolidayCollection(IEnumerable<Holiday> holidays)
        {
            this.AddRange(holidays);
        }
        #endregion

        #region Helper methods
        //=====================================================================

        /// <summary>
        /// Add a range of <see cref="Holiday"/> instances from an enumerable list
        /// </summary>
        /// <param name="holidays">The enumerable list of holiday instances to add</param>
        public void AddRange(IEnumerable<Holiday> holidays)
        {
            if(holidays != null)
                foreach(Holiday h in holidays)
                    base.Add(h);
        }

        /// <summary>
        /// Add a new holiday object to the collection that occurs on a fixed date
        /// </summary>
        /// <param name="month">The month of the holiday.</param>
        /// <param name="day">The day of the month on which it occurs.</param>
        /// <param name="adjust">Set to true to adjust date to the Friday or Monday if it falls on a weekend or
        /// false to always keep it on the specified day of the month.</param>
        /// <param name="description">A description of the holiday.</param>
        public void AddFixed(int month, int day, bool adjust, string description)
        {
            base.Add(new FixedHoliday(month, day, adjust, description));
        }

        /// <summary>
        /// Add a new holiday object to the collection that occurs on a floating date
        /// </summary>
        /// <param name="month">The month of the holiday.</param>
        /// <param name="dow">The day of the week on which it occurs.</param>
        /// <param name="occur">The occurrence of the day of the week on which the floating holiday falls.</param>
        /// <param name="offset">The number of days before or after the calculated floating date on which the
        /// holiday actually occurs.  See the <see cref="FloatingHoliday.Offset"/> property for more information
        /// about this parameter.</param>
        /// <param name="description">A description of the holiday.</param>
        public void AddFloating(DayOccurrence occur, DayOfWeek dow, int month, int offset, string description)
        {
            base.Add(new FloatingHoliday(occur, dow, month, offset, description));
        }

        /// <summary>
        /// This method returns true if the specified date falls on a holiday or false if it does not
        /// </summary>
        /// <param name="date">The date to check to see if it is a holiday defined in this collection</param>
        /// <returns>Returns true if the date is a holiday in this collection, false if it is not</returns>
        /// <include file='DateExamples.xml' path='Examples/Holiday/HelpEx[@name="Ex1"]/*' />
        public bool IsHoliday(DateTime date)
        {
            DateTime holiday;
            bool isHoliday = false;
            int  year = date.Year;

            // See if any of them match the given date
            foreach(Holiday hol in this)
            {
                holiday = hol.ToDateTime(year);

                // Compare date part only, times may differ
                if(holiday.Date == date.Date)
                {
                    isHoliday = true;
                    break;
                }

                // If the month is January or December, check the previous and next year to see if the holiday
                // gets pushed backward or forward and ends up matching the date.
                if((hol.Month == 1 || hol.Month == 12) && (date.Month == 1 || date.Month == 12))
                {
                    if(year > 1)
                    {
                        holiday = hol.ToDateTime(year - 1);

                        if(holiday.Date == date.Date)
                        {
                            isHoliday = true;
                            break;
                        }
                    }

                    if(year < 9999)
                    {
                        holiday = hol.ToDateTime(year + 1);

                        if(holiday.Date == date.Date)
                        {
                            isHoliday = true;
                            break;
                        }
                    }
                }
            }

            return isHoliday;
        }

        /// <summary>
        /// This method returns a description if the specified date falls on a holiday or an empty string if it
        /// does not.
        /// </summary>
        /// <param name="date">The date to check to see if it is a holiday defined in this collection</param>
        /// <returns>Returns the holiday description if the date is a holiday in this collection or an empty
        /// string if it is not.</returns>
        public string HolidayDescription(DateTime date)
        {
            DateTime holiday;
            string desc = String.Empty;
            int year = date.Year;

            // See if any of them match the given date
            foreach(Holiday hol in this)
            {
                holiday = hol.ToDateTime(year);

                // Compare date part only, times may differ
                if(holiday.Date == date.Date)
                {
                    desc = hol.Description;
                    break;
                }

                // If the month is January or December, check the previous and next year to see if the holiday
                // gets pushed backward or forward and ends up matching the date.
                if((hol.Month == 1 || hol.Month == 12) && (date.Month == 1 || date.Month == 12))
                {
                    if(year > 1)
                    {
                        holiday = hol.ToDateTime(year - 1);

                        if(holiday.Date == date.Date)
                        {
                            desc = hol.Description;
                            break;
                        }
                    }

                    if(year < 9999)
                    {
                        holiday = hol.ToDateTime(year + 1);

                        if(holiday.Date == date.Date)
                        {
                            desc = hol.Description;
                            break;
                        }
                    }
                }
            }

            return desc;
        }

        /// <summary>
        /// This method returns an enumerable list of holidays between the given years
        /// </summary>
        /// <remarks>This method can be used to get a list of holidays for faster searching when a large number
        /// of dates need to be checked.  When generating dates, it scans years on either side of the given
        /// range too so as to catch holidays that may fall on a weekend and get pushed backwards or forwards
        /// into the prior or next year (i.e. New Year's Day may occur on 12/31 if 01/01 is a Saturday).</remarks>
        /// <param name="startYear">The year in which to start generating holiday dates</param>
        /// <param name="endYear">The year in which to stop generating holiday dates</param>
        /// <returns>Returns an enumerable list of <see cref="DateTime"/> instances representing the holiday
        /// dates.</returns>
        /// <include file='DateExamples.xml' path='Examples/Holiday/HelpEx[@name="Ex1"]/*' />
        public IEnumerable<DateTime> HolidaysBetween(int startYear, int endYear)
        {
            DateTime holiday;
            int year, start = startYear, end = endYear;

            // Keep the years within the valid range
            if(start < 1)
                start = 1;
            else
                if(start > 9999)
                    start = 9999;
                else
                    if(start > 1)
                        start--;

            if(end < 1)
                end = 1;
            else
                if(end > 9999)
                    end = 9999;
                else
                    if(end < 9999)
                        end++;

            for(year = start; year <= end; year++)
                foreach(Holiday hol in this)
                {
                    holiday = hol.ToDateTime(year);

                    if(holiday.Year >= startYear && holiday.Year <= endYear)
                        yield return holiday;
                }
        }

        /// <summary>
        /// This adds a standard set of holidays to the collection
        /// </summary>
        /// <remarks><para>This adds a standard set of holidays to the collection that the author gets to take.
        /// If you do not get the same set, you can modify the collection after the call or derive a class and
        /// override this method to provide the set that you need.</para>
        /// 
        /// <para>As an alternative, you could serialize a set of holidays to an XML file and load them from it
        /// instead.</para>
        /// 
        /// <para>Fixed dates are set to be adjusted to not fall on a weekend.  The holidays added are as
        /// follows:</para>
        /// 
        /// <list type="table">
        ///     <listheader>
        ///         <term>Holiday</term>
        ///         <description>Description</description>
        ///     </listheader>
        ///     <item>
        ///         <term>New Year's Day</term>
        ///         <description>January 1st</description>
        ///     </item>
        ///     <item>
        ///         <term>Martin Luther King Day</term>
        ///         <description>3rd Monday in January</description>
        ///     </item>
        ///     <item>
        ///         <term>President's Day</term>
        ///         <description>Third Monday in February</description>
        ///     </item>
        ///     <item>
        ///         <term>Memorial Day</term>
        ///         <description>Last Monday in May</description>
        ///     </item>
        ///     <item>
        ///         <term>Independence Day</term>
        ///         <description>July 4th</description>
        ///     </item>
        ///     <item>
        ///         <term>Labor Day</term>
        ///         <description>1st Monday in September</description>
        ///     </item>
        ///     <item>
        ///         <term>Veteran's Day</term>
        ///         <description>November 11th</description>
        ///     </item>
        ///     <item>
        ///         <term>Thanksgiving</term>
        ///         <description>4th Thursday in November</description>
        ///     </item>
        ///     <item>
        ///         <term>Day After Thanksgiving</term>
        ///         <description>1 day after 4th Thursday in November</description>
        ///     </item>
        ///     <item>
        ///         <term>Christmas Day</term>
        ///         <description>December 25th</description>
        ///     </item>
        /// </list>
        /// </remarks>
        /// <returns>A reference to the holiday collection</returns>
        public virtual HolidayCollection AddStandardHolidays()
        {
            this.AddRange(new Holiday[] {
                new FixedHoliday(1, 1, true, LR.GetString("HCNewYearsDay")),
                new FloatingHoliday(DayOccurrence.Third, DayOfWeek.Monday, 1, 0, LR.GetString("HCMLKDay")),
                new FloatingHoliday(DayOccurrence.Third, DayOfWeek.Monday, 2, 0, LR.GetString("HCPresidentsDay")),
                new FloatingHoliday(DayOccurrence.Last, DayOfWeek.Monday, 5, 0, LR.GetString("HCMemorialDay")),
                new FixedHoliday(7, 4, true, LR.GetString("HCIndependenceDay")),
                new FloatingHoliday(DayOccurrence.First, DayOfWeek.Monday, 9, 0, LR.GetString("HCLaborDay")),
                new FixedHoliday(11, 11, true, LR.GetString("HCVeteransDay")),
                new FloatingHoliday(DayOccurrence.Fourth, DayOfWeek.Thursday, 11, 0, LR.GetString("HCThanksgiving")),
                new FloatingHoliday(DayOccurrence.Fourth, DayOfWeek.Thursday, 11, 1, LR.GetString("HCDayAfterTG")),
                new FixedHoliday(12, 25, true, LR.GetString("HCChristmas"))
            });

            return this;
        }
        #endregion
    }
}
