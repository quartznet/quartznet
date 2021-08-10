//===============================================================================================================
// System  : Personal Data Interchange Classes
// File    : Recurrence.cs
// Author  : Eric Woodruff  (Eric@EWoodruff.us)
// Updated : 11/23/2018
// Note    : Copyright 2003-2018, Eric Woodruff, All rights reserved
// Compiler: Microsoft Visual C#
//
// This file contains a class that can be used to generate recurring date/time sequences based on a pattern
// defined by the RRULE property in the iCalender 2.0 specification.
//
// This code is published under the Microsoft Public License (Ms-PL).  A copy of the license should be
// distributed with the code and can be found at the project website: https://github.com/EWSoftware/PDI.
// This notice, the author's name, and all copyright notices must remain intact in all applications,
// documentation, and source files.
//
//    Date     Who  Comments
// ==============================================================================================================
// 08/12/2004  EFW  Created the code
// 07/05/2011  EFW  Added support for BYSETPOS in vCalendar RRULEs
//===============================================================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using System.Security;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Schema;
using System.Xml.Serialization;

#nullable disable
namespace EWSoftware.PDI
{
    /// <summary>
    /// This class can be used to generate recurring date/time sequences based on a pattern defined by the RRULE
    /// property in the iCalender 2.0 specification.
    /// </summary>
    /// <remarks><para>It is separate from the other PDI calendar classes so that you can use the recurrence
    /// engine without the extra overhead of the calendar classes if you do not need it.</para>
    /// 
    /// <para>Although it does not implement the <see cref="System.Collections.IEnumerable"/> interface, the
    /// class does provide a type-safe enumerator via the <see cref="GetEnumerator"/> method.</para></remarks>
    [Serializable]
    public class Recurrence : ISerializable, IXmlSerializable
    {
        #region Private class members
        //=====================================================================

        // Regular expressions used for parsing
        private static Regex reSplit = new Regex(@"\s");
        private static Regex reFreqPresent = new Regex("FREQ=", RegexOptions.IgnoreCase);
        private static Regex reParse = new Regex("(?:(?:R|EX)RULE:)?(?:(?<Prop>[^=;]+)=(?<Value>[^;]*))?");
        private static Regex reDays = new Regex(@"(?<Instance>[\-0-9]*)?(?<DOW>[A-Z]*)");

        // This is used to convert the days of the week to their string form.  This is convenient for generating
        // its iCalendar representation.
        private static readonly string[] abbrevDays = { "SU", "MO", "TU", "WE", "TH", "FR", "SA" };

        private DateTime startDate;

        // Until date (UNTIL) and maximum occurrences (COUNT) are mutually exclusive
        private DateTime untilDate;
        private int maxOccur;

        private bool canOccurOnHoliday;

        private RecurFrequency  frequency;

        private int interval;

        private DayOfWeek weekStart;

        // Collections of unique integer values representing the BY* rules
        private UniqueIntegerCollection byMonth, byWeekNo, byYearDay, byMonthDay, byHour, byMinute, bySecond,
            bySetPos;

        // A collection of BYDAY rules.  These can be week days or instances of weekdays (i.e. 1st Monday, second
        // to last Friday, etc).
        private DayInstanceCollection byDay;

        // Arrays for some of the above.  These are used during the recurrence calculation for speed.  They are
        // marked internal so that the filter functions can access them directly.
        internal bool[] isSecondUsed, isMinuteUsed, isHourUsed, isDayUsed, isMonthDayUsed, isNegMonthDayUsed,
            isYearDayUsed, isNegYearDayUsed, isMonthUsed;

        // This is used for the WEEKLY frequency and BYWEEKNO rule.  It specifies the offset from the week start
        // day to the recurrence's start date.
        internal int weekdayOffset;

        // This is used to reference the frequency rules used to expand the recurrence
        private IFrequencyRules freqRules;

        // These are used to contain holidays for the CanOccurOnHoliday option when it is set to false.  Note
        // that the holiday collection is static and will be shared by all Recurrence instances.
        private static HolidayCollection holidays;

        // These are used to contain holidays for the CanOccurOnHoliday option when it is set to false on
        // a per-recurrence instance basis.
        private HolidayCollection instanceHolidays;
        private HashSet<DateTime> holDates;

        // This is used to preserve custom properties not supported by this class
        private StringCollection customProps;

        #endregion

        #region Properties
        //=====================================================================

        /// <summary>
        /// This is used to set or get the starting date/time for the recurrence
        /// </summary>
        /// <remarks>If not set, it defaults to the minimum date value.  The value is expressed in local time.
        /// If a <c>BYxxx</c> rule has no values, the corresponding date or time part from the start date/time is
        /// used in all instances generated by the recurrence.  For example, if no <c>BYHOUR</c>, <c>BYMINUTE</c>,
        /// and <c>BYSECOND</c> rule values are specified, the hour, minute, and seconds value on all instances
        /// will be defaulted to the corresponding values found in the start date/time.</remarks>
        public DateTime StartDateTime
        {
            get => startDate;
            set
            {
                startDate = value;

                // Get the offset used for the WEEKLY frequency and the BYWEEKNO rule.  This is used to shift the
                // day to the start date's day of the week.
                weekdayOffset = ((int)startDate.DayOfWeek + 7 - (int)weekStart) % 7;
            }
        }

        /// <summary>
        /// When retrieved, this property can be used to determine the end date of a recurrence based on the
        /// current settings.  When set, it makes the recurrence end after the specified date.
        /// </summary>
        /// <value><para>If no frequency has been set or it is set to never end, <see cref="DateTime">DateTime.MaxValue</see>
        /// is returned.  If set to end after a specific date, the specified ending date is returned.  If set to
        /// end after a specific number of occurrences (see <see cref="MaximumOccurrences"/>), the end date is
        /// calculated and returned.</para>
        /// 
        /// <para>If set to a date, the recurrence will be set to end after the specified date and the
        /// <see cref="MaximumOccurrences"/> value will be set to zero.  The value is expressed in local
        /// time.</para></value>
        /// <remarks>This is useful for obtaining an end date for storing a pattern in a database.  The starting
        /// and ending dates can then be used in queries to retrieve recurrences that may generate dates within a
        /// given range.</remarks>
        public DateTime RecurUntil
        {
            get
            {
                if(frequency == RecurFrequency.Undefined || (untilDate == DateTime.MaxValue && maxOccur == 0))
                    return DateTime.MaxValue;

                if(maxOccur == 0)
                    return untilDate;

                // Ends after a specific number of occurrences so figure out the last date
                DateTimeCollection dcDates = this.AllInstances();

                // If there are none, return the starting date
                if(dcDates.Count == 0)
                    return startDate;

                // Return the last found date
                return dcDates[dcDates.Count - 1];
            }
            set
            {
                untilDate = value;
                maxOccur = 0;
            }
        }

        /// <summary>
        /// This is used to get or set the maximum number of occurrences that should be generated by the
        /// recurrence.
        /// </summary>
        /// <remarks>If set to never end or to end by a specific date using <see cref="RecurUntil"/>, this will
        /// return zero.  A non-zero value indicates the maximum number of occurrences that will be calculated.
        /// If set to a non-zero value, <see cref="RecurUntil"/> will be ignored.  If set to zero, <c>RecurUntil</c>
        /// is set to <c>DateTime.MaximumDate</c> and the recurrence never ends.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the value is negative</exception>
        public int MaximumOccurrences
        {
            get => maxOccur;
            set
            {
                if(value < 0)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRecurNegativeCount"));

                maxOccur = value;
                untilDate = DateTime.MaxValue;
            }
        }

        /// <summary>
        /// This is used to set or get whether or not the instances can occur on a holiday
        /// </summary>
        /// <remarks>If set to false, any generated instance that matches a holiday date found in the
        /// <see cref="Holidays" /> property will be discarded.  The default is true so that instances can occur
        /// on holiday dates.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex1"]/*' />
        /// <seealso cref="Holidays"/>
        public bool CanOccurOnHoliday
        {
            get => canOccurOnHoliday;
            set
            {
                canOccurOnHoliday = value;

                if(!value && holidays == null)
                    holidays = new HolidayCollection();
            }
        }

        /// <summary>
        /// This property is used to set or get the current recurrence frequency
        /// </summary>
        public RecurFrequency Frequency
        {
            get => frequency;
            set
            {
                frequency = value;

                // Get a reference to the rules used to expand/filter the recurrence options
                switch(value)
                {
                    case RecurFrequency.Yearly:
                        freqRules = new YearlyFrequency();
                        break;

                    case RecurFrequency.Monthly:
                        freqRules = new MonthlyFrequency();
                        break;

                    case RecurFrequency.Weekly:
                        freqRules = new WeeklyFrequency();
                        break;

                    case RecurFrequency.Daily:
                        freqRules = new DailyFrequency();
                        break;

                    case RecurFrequency.Hourly:
                        freqRules = new HourlyFrequency();
                        break;

                    case RecurFrequency.Minutely:
                        freqRules = new MinutelyFrequency();
                        break;

                    case RecurFrequency.Secondly:
                        freqRules = new SecondlyFrequency();
                        break;

                    default:    // Undefined
                        freqRules = null;
                        break;
                }
            }
        }

        /// <summary>
        /// This property is used to set or get the interval between instances (the number of seconds, minutes,
        /// hours, days, weeks, months, or years based on the recurrence frequency).
        /// </summary>
        /// <exception cref="ArgumentOutOfRangeException">This is thrown if the value is less than one</exception>
        public int Interval
        {
            get => interval;
            set
            {
                if(value < 1)
                    throw new ArgumentOutOfRangeException(nameof(value), value, LR.GetString("ExRecurBadInterval"));

                interval = value;
            }
        }

        /// <summary>
        /// This property is used to set or get the day of the week on which a week begins.  This only applies to
        /// weekly and yearly recurrences.
        /// </summary>
        /// <remarks>A week is defined as a seven day period, starting on the day of the week defined by this
        /// property.  The default value is Monday.</remarks>
        public DayOfWeek WeekStart
        {
            get => weekStart;
            set
            {
                weekStart = value;

                // Get the offset used for the WEEKLY frequency and the BYWEEKNO rule.  This is used to shift the
                // day to the start date's day of the week.
                weekdayOffset = ((int)startDate.DayOfWeek + 7 - (int)weekStart) % 7;
            }
        }

        /// <summary>
        /// This is used to modify the BYMONTH rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing month values from 1 to
        /// 12.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex9"]/*' />
        public UniqueIntegerCollection ByMonth => byMonth;

        /// <summary>
        /// This is used to modify the BYWEEKNO rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing week number values from
        /// -53 to +53 excluding zero.  Negative values specify a week number from the end of the year.  Positive
        /// values specify a week number from the start of the year.  This rule is only applicable to the
        /// <c>Yearly</c> frequency.  It is ignored for all other frequencies.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex10"]/*' />
        public UniqueIntegerCollection ByWeekNo => byWeekNo;

        /// <summary>
        /// This is used to modify the BYYEARDAY rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing day number values from
        /// -366 to +366 excluding zero.  Negative values specify a day number from the end of the year.
        /// Positive values specify a day number from the start of the year.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex11"]/*' />
        public UniqueIntegerCollection ByYearDay => byYearDay;

        /// <summary>
        /// This is used to modify the BYMONTHDAY rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing day number values from
        /// -31 to +31 excluding zero.  Negative values specify a day number from the end of the month.  Positive
        /// values specify a day number from the start of the month.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex12"]/*' />
        public UniqueIntegerCollection ByMonthDay => byMonthDay;

        /// <summary>
        /// This is used to modify the BYHOUR rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing hour values from 0 to
        /// 23.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex13"]/*' />
        public UniqueIntegerCollection ByHour => byHour;

        /// <summary>
        /// This is used to modify the BYMINUTE rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing minutes values from 0 to
        /// 59.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex13"]/*' />
        public UniqueIntegerCollection ByMinute => byMinute;

        /// <summary>
        /// This is used to modify the BYSECOND rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing seconds values from 0 to
        /// 59.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex13"]/*' />
        public UniqueIntegerCollection BySecond => bySecond;

        /// <summary>
        /// This is used to modify the BYSETPOS rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique integer values representing index positions that
        /// should be used to filter the set of recurrence dates calculated at each interval.  Index values can
        /// be from -366 to +366 excluding zero.  Negative values specify an index from the end of the set.
        /// Positive values specify an index from the start of the set.  Zero is not a valid index value.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex14"]/*' />
        public UniqueIntegerCollection BySetPos => bySetPos;

        /// <summary>
        /// This is used to modify the BYDAY rule of a recurrence
        /// </summary>
        /// <remarks>The collection contains a set of unique <see cref="DayInstance"/> values representing day of
        /// the week instances on which calculated recurrence dates can fall.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex14"]/*' />
        public DayInstanceCollection ByDay => byDay;

        /// <summary>
        /// This is used to add holidays to the recurrence holiday list.  These will be used in conjunction with
        /// the <see cref="CanOccurOnHoliday" /> option if it is set to false.
        /// </summary>
        /// <remarks>Note that the holiday list is static and will be shared amongst all instances of the
        /// <c>Recurrence</c> class to save having to assign it to each new instance.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex1"]/*' />
        /// <seealso cref="CanOccurOnHoliday"/>
        public static HolidayCollection Holidays
        {
            get
            {
                if(holidays == null)
                    holidays = new HolidayCollection();

                return holidays;
            }
        }

        /// <summary>
        /// This is used to add holidays to the recurrence instance.  These will be used in conjunction with
        /// the <see cref="CanOccurOnHoliday" /> option if it is set to false.
        /// </summary>
        /// <remarks>Note that the holiday list is not static and will not be shared amongst all instances of the
        /// <c>Recurrence</c> classes.</remarks>
        /// <seealso cref="CanOccurOnHoliday"/>
        public HolidayCollection InstanceHolidays
        {
            get => instanceHolidays;
            set => instanceHolidays = value;
        }

        /// <summary>
        /// This returns a set of custom properties (if any) found when the recurrence properties where parsed
        /// from a string.
        /// </summary>
        public StringCollection CustomProperties => customProps;

        #endregion

        #region Constructors
        //=====================================================================

        /// <summary>
        /// Default constructor
        /// </summary>
        /// <remarks>See <see cref="Reset"/> for a description of the default state of the recurrence object</remarks>
        /// <overloads>There are three constructors for this class</overloads>
        public Recurrence()
        {
            byMonth = new UniqueIntegerCollection(1, 12, false);
            byWeekNo = new UniqueIntegerCollection(-53, 53, false);
            byYearDay = new UniqueIntegerCollection(-366, 366, false);
            byMonthDay = new UniqueIntegerCollection(-31, 31, false);
            byHour = new UniqueIntegerCollection(0, 23, true);
            byMinute = new UniqueIntegerCollection(0, 59, true);
            bySecond = new UniqueIntegerCollection(0, 59, true);
            bySetPos = new UniqueIntegerCollection(-366, 366, false);
            byDay = new DayInstanceCollection();
            customProps = new StringCollection();

            isSecondUsed = new bool[60];
            isMinuteUsed = new bool[60];
            isHourUsed = new bool[24];
            isDayUsed = new bool[7];       // When filtered, instance isn't used
            isMonthDayUsed = new bool[32];
            isNegMonthDayUsed = new bool[32];  // Negative days
            isYearDayUsed = new bool[367];
            isNegYearDayUsed = new bool[367];  // Negative days
            isMonthUsed = new bool[12];

            this.Parse(null);
        }

        /// <summary>
        /// Construct a recurrence from a string in vCalendar 1.0 basic grammar format or iCalendar 2.0
        /// RRULE/EXRULE format.
        /// </summary>
        /// <param name="recur">The recurrence parameters in vCalendar 1.0 basic grammar format or iCalendar 2.0
        /// RRULE/EXRULE format.</param>
        /// <remarks>The RRULE or EXRULE property name is ignored if present</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex15"]/*' />
        public Recurrence(string recur) : this()
        {
            this.Parse(recur);
        }

        /// <summary>
        /// Deserialization constructor for use with <see cref="System.Runtime.Serialization.ISerializable"/>
        /// </summary>
        /// <param name="info">The serialization info object</param>
        /// <param name="context">The streaming context object</param>
        protected Recurrence(SerializationInfo info, StreamingContext context) : this()
        {
            if(info != null)
                this.Parse((string)info.GetValue("Recurrence", typeof(string)));
        }
        #endregion

        #region Private class methods
        //=====================================================================

        /// <summary>
        /// This is used to parse a recurrence string that is in vCalendar format.  Only the basic rule grammar
        /// is supported.  The extended rule grammar is much more complex and since I don't think its used much,
        /// I'm going to ignore it for now.
        /// </summary>
        /// <param name="recurrenceText">The recurrence string</param>
        private void ParseVCalendar(string recurrenceText)
        {
            int idx = 0, dayIdx, instance;
            bool isByDay;

            string[] parts = reSplit.Split(recurrenceText.ToUpperInvariant());

            while(idx < parts.Length && parts[idx].Length == 0)
                idx++;

            // If no count or end date is specified, the count is set to 2
            this.MaximumOccurrences = 2;

            // The first part is the frequency
            switch(parts[idx][0])
            {
                case 'D':
                    this.Frequency = RecurFrequency.Daily;
                    this.Interval = Convert.ToInt32(parts[0].Substring(1), CultureInfo.InvariantCulture);

                    do
                    {
                        idx++;
                    } while(idx < parts.Length && parts[idx].Length == 0);

                    if(idx < parts.Length)
                        if(parts[idx][0] == '#')
                            this.MaximumOccurrences = Convert.ToInt32(parts[idx].Substring(1), CultureInfo.InvariantCulture);
                        else
                            this.RecurUntil = DateUtils.FromISO8601String(parts[idx], true);
                    break;

                case 'W':
                    this.Frequency = RecurFrequency.Weekly;
                    this.Interval = Convert.ToInt32(parts[0].Substring(1), CultureInfo.InvariantCulture);

                    // Parse out the week days and end parameters
                    for(idx = 1; idx < parts.Length; idx++)
                    {
                        if(parts[idx].Length == 0)
                            continue;

                        if(parts[idx][0] == '#')
                        {
                            this.MaximumOccurrences = Convert.ToInt32(parts[idx].Substring(1), CultureInfo.InvariantCulture);
                            continue;
                        }

                        if(Char.IsDigit(parts[idx][0]))
                        {
                            this.RecurUntil = DateUtils.FromISO8601String(parts[idx], true);
                            continue;
                        }

                        for(dayIdx = 0; dayIdx < 7; dayIdx++)
                            if(abbrevDays[dayIdx] == parts[idx])
                                break;

                        if(dayIdx == 7)
                            throw new ArgumentException(LR.GetString("ExRecurBadDOW"), nameof(recurrenceText));

                        byDay.Add(new DayInstance((DayOfWeek)dayIdx));
                    }
                    break;

                case 'M':
                    this.Frequency = RecurFrequency.Monthly;
                    this.Interval = Convert.ToInt32(parts[0].Substring(2), CultureInfo.InvariantCulture);
                    instance = 0;
                    isByDay = (parts[0][1] != 'P');

                    // Parse out the days instances and end parameters
                    for(idx = 1; idx < parts.Length; idx++)
                    {
                        if(parts[idx].Length == 0)
                            continue;

                        if(parts[idx][0] == '#')
                        {
                            this.MaximumOccurrences = Convert.ToInt32(parts[idx].Substring(1), CultureInfo.InvariantCulture);
                            continue;
                        }

                        if(parts[idx] == "LD")
                        {
                            byMonthDay.Add(-1);
                            continue;
                        }

                        if(Char.IsDigit(parts[idx][0]) && parts[idx].Length > 7)
                        {
                            this.RecurUntil = DateUtils.FromISO8601String(parts[idx], true);
                            continue;
                        }

                        if(Char.IsDigit(parts[idx][0]))
                        {
                            if(parts[idx].EndsWith("-", StringComparison.Ordinal))
                            {
                                instance = Convert.ToInt32(parts[idx].Substring(0, parts[idx].Length - 1),
                                    CultureInfo.InvariantCulture);
                                instance *= -1;
                            }
                            else
                                if(parts[idx].EndsWith("+", StringComparison.Ordinal))
                                {
                                    instance = Convert.ToInt32(parts[idx].Substring(0, parts[idx].Length - 1),
                                        CultureInfo.InvariantCulture);
                                }
                                else
                                    instance = Convert.ToInt32(parts[idx], CultureInfo.InvariantCulture);

                            if(isByDay)
                                byMonthDay.Add(instance);

                            continue;
                        }

                        for(dayIdx = 0; dayIdx < 7; dayIdx++)
                            if(abbrevDays[dayIdx] == parts[idx])
                                break;

                        if(dayIdx == 7)
                            throw new ArgumentException(LR.GetString("ExRecurBadDOW"), nameof(recurrenceText));

                        byDay.Add(new DayInstance(instance, (DayOfWeek)dayIdx));
                    }
                    break;

                case 'Y':
                    this.Frequency = RecurFrequency.Yearly;
                    this.Interval = Convert.ToInt32(parts[0].Substring(2), CultureInfo.InvariantCulture);
                    isByDay = (parts[0][1] != 'M');

                    // Parse out the months or days and the end parameters
                    for(idx = 1; idx < parts.Length; idx++)
                    {
                        if(parts[idx].Length == 0)
                            continue;

                        if(parts[idx][0] == '#')
                        {
                            this.MaximumOccurrences = Convert.ToInt32(parts[idx].Substring(1), CultureInfo.InvariantCulture);
                            continue;
                        }

                        if(Char.IsDigit(parts[idx][0]) && parts[idx].Length > 7)
                        {
                            this.RecurUntil = DateUtils.FromISO8601String(parts[idx], true);
                            continue;
                        }

                        if(Char.IsDigit(parts[idx][0]))
                        {
                            if(parts[idx].EndsWith("-", StringComparison.Ordinal))
                            {
                                instance = Convert.ToInt32(parts[idx].Substring(0, parts[idx].Length - 1),
                                    CultureInfo.InvariantCulture);
                                instance *= -1;
                            }
                            else
                                if(parts[idx].EndsWith("+", StringComparison.Ordinal))
                                {
                                    instance = Convert.ToInt32(parts[idx].Substring(0, parts[idx].Length - 1),
                                        CultureInfo.InvariantCulture);
                                }
                                else
                                    instance = Convert.ToInt32(parts[idx], CultureInfo.InvariantCulture);

                            if(isByDay)
                                byYearDay.Add(instance);
                            else
                                byMonth.Add(instance);
                        }
                    }
                    break;

                default:
                    throw new ArgumentException(LR.GetString("ExRecurUnknownRuleFormat"), nameof(recurrenceText));
            }
        }

        /// <summary>
        /// This method is used to return all recurring instances between the two specified date/times based on
        /// the current settings.
        /// </summary>
        /// <param name="fromDate">The minimum date/time on or after which instances should occur.</param>
        /// <param name="toDate">The maximum date/time on or before which instances should occur.</param>
        /// <returns>Returns a <see cref="DateTimeCollection"/> of <see cref="DateTime" /> objects that represent
        /// the instances found between the two specified date/times.</returns>
        private DateTimeCollection GenerateInstances(DateTime fromDate, DateTime toDate)
        {
            RecurDateTimeCollection rdtc;
            RecurDateTime rdt;
            int idx, count, lastYear = -1;

            DateTimeCollection dcDates = new DateTimeCollection();

            // If undefined or if the requested range is outside that of the recurrence, don't bother.  Just
            // return an empty collection.  Note that for defined recurrences that use a count, we'll always
            // have to expand it.
            if(frequency == RecurFrequency.Undefined || startDate > toDate || untilDate < fromDate)
                return dcDates;

            RecurDateTime start = new RecurDateTime(startDate), end = new RecurDateTime(untilDate),
                from = new RecurDateTime(fromDate), to = new RecurDateTime(toDate);

            RecurDateTime current = freqRules.FindStart(this, start, end, from, to);

            // If there's nothing to generate, stop now
            if(current == null)
                return dcDates;

            rdtc = new RecurDateTimeCollection();

            // Initialize the filtering arrays.  These help speed up the filtering process by letting us do one
            // look up as opposed to comparing all elements in the collection.
            Array.Clear(isSecondUsed, 0, isSecondUsed.Length);
            Array.Clear(isMinuteUsed, 0, isMinuteUsed.Length);
            Array.Clear(isHourUsed, 0, isHourUsed.Length);
            Array.Clear(isDayUsed, 0, isDayUsed.Length);
            Array.Clear(isMonthDayUsed, 0, isMonthDayUsed.Length);
            Array.Clear(isNegMonthDayUsed, 0, isNegMonthDayUsed.Length);
            Array.Clear(isYearDayUsed, 0, isYearDayUsed.Length);
            Array.Clear(isNegYearDayUsed, 0, isNegYearDayUsed.Length);
            Array.Clear(isMonthUsed, 0, isMonthUsed.Length);

            if(bySecond.Count != 0)
                foreach(int second in bySecond)
                    isSecondUsed[second] = true;

            if(byMinute.Count != 0)
                foreach(int minute in byMinute)
                    isMinuteUsed[minute] = true;

            if(byHour.Count != 0)
                foreach(int hour in byHour)
                    isHourUsed[hour] = true;

            if(byMonth.Count != 0)
                foreach(int month in byMonth)
                    isMonthUsed[month - 1] = true;

            // When filtering, the instance is ignored
            if(byDay.Count != 0)
                foreach(DayInstance di in byDay)
                    isDayUsed[(int)di.DayOfWeek] = true;

            // Negative days are from the end of the month
            if(byMonthDay.Count != 0)
                foreach(int monthDay in byMonthDay)
                    if(monthDay > 0)
                        isMonthDayUsed[monthDay] = true;
                    else
                        isNegMonthDayUsed[0 - monthDay] = true;

            // Negative days are from the end of the year
            if(byYearDay.Count != 0)
                foreach(int yearDay in byYearDay)
                    if(yearDay > 0)
                        isYearDayUsed[yearDay] = true;
                    else
                        isNegYearDayUsed[0 - yearDay] = true;

            do
            {
                rdtc.Clear();
                rdtc.Add(current);

                // The spec is rather vague about how some of the rules are used together.  For example, it says
                // that rule parts for a period of time less than the frequency generally expand it.  However,
                // an example for the MONTHLY frequency shows that when BYMONTHDAY and BYDAY are used together,
                // BYDAY acts as a filter for BYMONTHDAY not an expansion of the frequency.  When used by
                // themselves, the rules in question do act as expansions.  There are no examples for the yearly
                // frequency that show how all of the various combinations interact so I'm making some
                // assumptions based on an evaluation of what makes the most sense.
                switch(frequency)
                {
                    case RecurFrequency.Yearly:
                        // This one gets rather messy so it's separate
                        ExpandYearly(rdtc);
                        break;

                    case RecurFrequency.Monthly:
                        if(freqRules.ByMonth(this, rdtc) != 0)
                            if(freqRules.ByYearDay(this, rdtc) != 0)
                            {
                                // If BYMONTHDAY and BYDAY are specified, expand by month day and filter by day.
                                // If one but not the other or neither is specified, handle them in order as
                                // usual.
                                if(byMonthDay.Count != 0 && byDay.Count != 0)
                                {
                                    if(Expand.ByMonthDay(this, rdtc) != 0)
                                        if(Filter.ByDay(this, rdtc) != 0)
                                        {
                                            // These always expand if used
                                            Expand.ByHour(this, rdtc);
                                            Expand.ByMinute(this, rdtc);
                                            Expand.BySecond(this, rdtc);
                                        }
                                }
                                else
                                    if(Expand.ByMonthDay(this, rdtc) != 0)
                                        if(freqRules.ByDay(this, rdtc) != 0)
                                        {
                                            // These always expand if used
                                            Expand.ByHour(this, rdtc);
                                            Expand.ByMinute(this, rdtc);
                                            Expand.BySecond(this, rdtc);
                                        }
                            }
                        break;

                    default:
                        // Everything else is fairly straightforward.  We just expand or filter based on the
                        // frequency type and what rules are specified.
                        if(freqRules.ByMonth(this, rdtc) != 0)
                            if(freqRules.ByYearDay(this, rdtc) != 0)
                                if(freqRules.ByMonthDay(this, rdtc) != 0)
                                    if(freqRules.ByDay(this, rdtc) != 0)
                                        if(freqRules.ByHour(this, rdtc) != 0)
                                            if(freqRules.ByMinute(this, rdtc) != 0)
                                                freqRules.BySecond(this, rdtc);
                        break;
                }

                // Sort the dates and remove invalid and duplicate dates
                rdtc.Sort();

                for(idx = 0, count = rdtc.Count; idx < count; idx++)
                {
                    rdt = rdtc[idx];

                    // If not valid, discard it.
                    if(!rdt.IsValidDate())
                    {
                        rdtc.RemoveAt(idx);
                        idx--;
                        count--;
                        continue;
                    }

                    // Discard it if it falls on a holiday
                    if(!canOccurOnHoliday)
                    {
                        // If this is the first call or the year changes, get the holidays in the date's year
                        // and the next year.
                        if(holDates == null || lastYear != rdt.Year)
                        {
                            var holidayCollection = instanceHolidays ?? holidays;
                            holDates = new HashSet<DateTime>(holidayCollection.HolidaysBetween(rdt.Year, rdt.Year + 1));
                            lastYear = rdt.Year;
                        }

                        // Note that we only compare the date part as the holiday's time probably will not match
                        // the recurrence's time.
                        if(holDates.Contains(rdt.ToDateTime().Date))
                        {
                            rdtc.RemoveAt(idx);
                            idx--;
                            count--;
                            continue;
                        }
                    }

                    // Discard it if it's a duplicate
                    if(idx != 0 && rdt == rdtc[idx - 1])
                    {
                        rdtc.RemoveAt(idx);
                        idx--;
                        count--;
                        continue;
                    }
                }

                if(rdtc.Count != 0)
                {
                    // Apply the BYSETPOS rule and remove entries prior to the start or past the end of the
                    // ranges.
                    if(bySetPos.Count != 0)
                    {
                        foreach(int nPos in bySetPos)
                        {
                            // Invert negative values.  They'll select elements indexed from the end of the
                            // array.
                            if(nPos < 0)
                                idx = nPos + rdtc.Count;
                            else
                                idx = nPos - 1;

                            if(idx >= 0 && idx < rdtc.Count)
                                if(rdtc[idx] >= start && rdtc[idx] <= end && rdtc[idx] >= from && rdtc[idx] <= to)
                                    dcDates.Add(rdtc[idx].ToDateTime());
                        }
                    }
                    else
                        for(idx = 0; idx < rdtc.Count; idx++)
                            if(rdtc[idx] >= start && rdtc[idx] <= end && rdtc[idx] >= from && rdtc[idx] <= to)
                                dcDates.Add(rdtc[idx].ToDateTime());

                    // Handle MaxOccurrences property.  Note that if it's used, it is assumed that the limiting
                    // range starts at the recurrence start.  Otherwise, we have no way of knowing how many
                    // occurred between the recurrence start and the limiting range's start date.
                    if(maxOccur != 0 && dcDates.Count > maxOccur)
                        dcDates.RemoveRange(maxOccur, dcDates.Count - maxOccur);
                }

                // Loop until the end of the recurrence or the range
            } while(freqRules.FindNext(this, end, to, current) && (maxOccur == 0 || dcDates.Count < maxOccur));

            // Sort the collection one last time.  There's no guaranteed order of selection if BYSETPOS was used.
            dcDates.Sort(true);

            return dcDates;
        }

        /// <summary>
        /// This is used to handle the expansion of the yearly frequency
        /// </summary>
        /// <param name="dates">The collection in which to put the dates</param>
        /// <remarks>The spec is rather vague about how all the rules should interact so I'm making some best
        /// guesses here based on the examples in the spec itself although not all combinations are shown.
        /// </remarks>
        private void ExpandYearly(RecurDateTimeCollection dates)
        {
            RecurDateTimeCollection rdtcMonth = null, rdtcMoDay = null, rdtcWeek = null, rdtcYrDay = null,
                rdtcDay = null;
            bool isExpanded = false;

            // We'll expand each rule individually and combine the results before applying the time expansions.
            // The application of the BYMONTHDAY and BYDAY rules varies based on whatever other rule parts are
            // present as well.
            if(byMonth.Count != 0)
            {
                // Expand by month
                isExpanded = true;
                rdtcMonth = new RecurDateTimeCollection(dates);
                freqRules.ByMonth(this, rdtcMonth);

                // If BYMONTHDAY and BYDAY are both specified, we need to expand by month day and then filter by
                // day.  If we expand by day alone, note that we do so only in the months specified in the
                // BYMONTH rule.
                if(byMonthDay.Count != 0 && byDay.Count != 0)
                {
                    Expand.ByMonthDay(this, rdtcMonth);
                    Filter.ByDay(this, rdtcMonth);
                }
                else
                    if(Expand.ByMonthDay(this, rdtcMonth) != 0)
                        Expand.ByDayInMonths(this, rdtcMonth);
            }
            else
            {
                if(byMonthDay.Count != 0)
                {
                    // Expand by month day if specified without any by month rule part
                    isExpanded = true;
                    rdtcMoDay = new RecurDateTimeCollection(dates);
                    freqRules.ByMonthDay(this, rdtcMoDay);
                }

                // As long as by week number isn't specified either, we'll expand the by day rule here too
                if(byWeekNo.Count == 0)
                {
                    isExpanded = true;
                    rdtcDay = new RecurDateTimeCollection(dates);
                    freqRules.ByDay(this, rdtcDay);
                }
            }

            if(byWeekNo.Count != 0)
            {
                // Expand by week number
                isExpanded = true;
                rdtcWeek = new RecurDateTimeCollection(dates);
                freqRules.ByWeekNo(this, rdtcWeek);

                // Expand by days of the week in those weeks
                Expand.ByDayInWeeks(this, rdtcWeek);
            }

            if(byYearDay.Count != 0)
            {
                // Expand by year day
                isExpanded = true;
                rdtcYrDay = new RecurDateTimeCollection(dates);
                freqRules.ByYearDay(this, rdtcYrDay);
            }

            // Combine the various expansions.  If nothing was done, leave the original date in the collection.
            if(isExpanded)
            {
                dates.Clear();

                if(rdtcMonth != null && rdtcMonth.Count != 0)
                    dates.AddRange(rdtcMonth);

                if(rdtcMoDay != null && rdtcMoDay.Count != 0)
                    dates.AddRange(rdtcMoDay);

                if(rdtcWeek != null && rdtcWeek.Count != 0)
                    dates.AddRange(rdtcWeek);

                if(rdtcYrDay != null && rdtcYrDay.Count != 0)
                    dates.AddRange(rdtcYrDay);

                if(rdtcDay != null && rdtcDay.Count != 0)
                    dates.AddRange(rdtcDay);
            }

            // In any case, the time parts are easy.  They always expand the instances if there's anything there.
            if(dates.Count != 0)
            {
                Expand.ByHour(this, dates);
                Expand.ByMinute(this, dates);
                Expand.BySecond(this, dates);
            }
        }
        #endregion

        #region Public methods and overrides
        //=====================================================================

        /// <summary>
        /// This can be used to reset a recurrence to its default state
        /// </summary>
        /// <remarks>The recurrence defaults to allowing instances to occur on holidays and never ends.  No
        /// specific start date is assumed and the frequency is undefined.  The interval is set to one (1) and
        /// the week start is set to Monday.  All rule parts are cleared.
        /// </remarks>
        public void Reset()
        {
            this.Parse(null);
        }

        /// <summary>
        /// This is used to parse recurrence properties from a string in vCalendar 1.0 basic grammar format or
        /// iCalendar 2.0 RRULE/EXRULE format.
        /// </summary>
        /// <param name="recur">The recurrence parameters in vCalendar 1.0 basic grammar format or iCalendar 2.0
        /// RRULE/EXRULE format.</param>
        /// <remarks>The RRULE or EXRULE property name is ignored if present.  All properties except the start
        /// date/time are cleared and reset to their default values prior to parsing the string.  Passing a null
        /// or empty string is equivalent to calling <see cref="Reset"/>.  If the string is in the older
        /// vCalendar format, only the basic recurrence rule grammar is supported.  Full support is provided for
        /// the iCalendar format.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex15"]/*' />
        public void Parse(string recur)
        {
            string property, value;
            string[] values;
            int dayIdx;

            canOccurOnHoliday = true;

            untilDate = DateTime.MaxValue;

            this.Frequency = RecurFrequency.Undefined;
            this.WeekStart = DayOfWeek.Monday;
            interval = 1;
            maxOccur = 0;

            byMonth.Clear();
            byWeekNo.Clear();
            byYearDay.Clear();
            byMonthDay.Clear();
            byHour.Clear();
            byMinute.Clear();
            bySecond.Clear();
            bySetPos.Clear();
            byDay.Clear();
            customProps.Clear();

            if(recur == null || recur.Length == 0)
                return;

            // If there is no frequency, try parsing it in vCalendar format
            if(!reFreqPresent.IsMatch(recur))
            {
                this.ParseVCalendar(recur);
                return;
            }

            // Split the string into its properties
            MatchCollection matches = reParse.Matches(recur.ToUpperInvariant());

            foreach(Match m in matches)
            {
                property = m.Groups["Prop"].Value.Trim();
                value = m.Groups["Value"].Value.Trim();

                if(value.Length != 0)
                    switch(property)
                    {
                        case "FREQ":
                            this.Frequency = (RecurFrequency)Enum.Parse(typeof(RecurFrequency), value, true);
                            break;

                        case "INTERVAL":
                            this.Interval = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                            break;

                        case "COUNT":
                            this.MaximumOccurrences = Convert.ToInt32(value, CultureInfo.InvariantCulture);
                            break;

                        case "UNTIL":
                            this.RecurUntil = DateUtils.FromISO8601String(value, true);
                            break;

                        case "WKST":
                            for(dayIdx = 0; dayIdx < 7; dayIdx++)
                                if(abbrevDays[dayIdx] == value)
                                    break;

                            if(dayIdx == 7)
                                throw new ArgumentOutOfRangeException("WKST", value, LR.GetString("ExRecurBadWeekStart"));

                            this.WeekStart = (DayOfWeek)dayIdx;
                            break;

                        case "BYMONTH":
                            values = value.Split(',');

                            foreach(string s in values)
                                byMonth.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "BYWEEKNO":
                            values = value.Split(',');

                            foreach(string s in values)
                                byWeekNo.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "BYYEARDAY":
                            values = value.Split(',');

                            foreach(string s in values)
                                byYearDay.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "BYMONTHDAY":
                            values = value.Split(',');

                            foreach(string s in values)
                                byMonthDay.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "BYDAY":
                            MatchCollection matchDays = reDays.Matches(value);

                            foreach(Match mDay in matchDays)
                                if(mDay.Value.Length != 0)
                                {
                                    for(dayIdx = 0; dayIdx < 7; dayIdx++)
                                        if(abbrevDays[dayIdx] == mDay.Groups["DOW"].Value)
                                            break;

                                    if(dayIdx == 7)
                                        throw new ArgumentOutOfRangeException("BYDAY", value, LR.GetString("ExRecurBadBYDAYRule"));

                                    if(mDay.Groups["Instance"].Value.Length == 0)
                                        byDay.Add(new DayInstance((DayOfWeek)dayIdx));
                                    else
                                        byDay.Add(new DayInstance(Convert.ToInt32(mDay.Groups["Instance"].Value,
                                            CultureInfo.InvariantCulture), (DayOfWeek)dayIdx));
                                }
                            break;

                        case "BYHOUR":
                            values = value.Split(',');

                            foreach(string s in values)
                                byHour.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "BYMINUTE":
                            values = value.Split(',');

                            foreach(string s in values)
                                byMinute.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "BYSECOND":
                            values = value.Split(',');

                            foreach(string s in values)
                                bySecond.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "BYSETPOS":
                            values = value.Split(',');

                            foreach(string s in values)
                                bySetPos.Add(Convert.ToInt32(s, CultureInfo.InvariantCulture));
                            break;

                        case "X-EWSOFTWARE-OCCURONHOL":
                            this.CanOccurOnHoliday = (value[0] != '0');
                            break;

                        case "X-EWSOFTWARE-DTSTART":
                            this.StartDateTime = DateUtils.FromISO8601String(value, true);
                            break;

                        default:    // Preserve unknown properties
                            customProps.Add(String.Format(CultureInfo.InvariantCulture, "{0}={1}", property, value));
                            break;
                    }
            }
        }

        /// <summary>
        /// This is overridden to allow proper comparison of recurrence objects
        /// </summary>
        /// <param name="obj">The object to which this instance is compared</param>
        /// <returns>Returns true if the object equals this instance, false if it does not</returns>
        public override bool Equals(object obj)
        {
            if(!(obj is Recurrence r))
                return false;

            return (this == r || this.ToString() == r.ToString());
        }

        /// <summary>
        /// Get a hash code for the recurrence object
        /// </summary>
        /// <returns>Returns the hash code for the recurrence object</returns>
        /// <remarks>To compute the hash code, it converts the recurrence to its string form</remarks>
        public override int GetHashCode()
        {
            return this.ToStringWithStartDateTime().GetHashCode();
        }

        /// <summary>
        /// Convert the recurrence to its string form
        /// </summary>
        /// <returns>This returns the recurrence in its string form suitable for saving in an iCalendar file.
        /// Since the start date is not part of the recurrence for iCalendar, it is omitted.</returns>
        public override string ToString()
        {
            StringBuilder sb = new StringBuilder(256);

            sb.AppendFormat("FREQ={0}", this.Frequency.ToString().ToUpperInvariant());

            if(this.Interval != 1)
                sb.AppendFormat(";INTERVAL={0}", this.Interval);

            if(this.MaximumOccurrences != 0)
                sb.AppendFormat(";COUNT={0}", this.MaximumOccurrences);
            else
                if(this.RecurUntil != DateTime.MaxValue)
                    sb.AppendFormat(";UNTIL={0}", this.RecurUntil.ToUniversalTime().ToString(
                        ISO8601Format.BasicDateTimeUniversal, CultureInfo.InvariantCulture));

            if(this.WeekStart != DayOfWeek.Monday)
                sb.AppendFormat(";WKST={0}", abbrevDays[(int)this.WeekStart]);

            if(byMonth.Count != 0)
            {
                sb.Append(";BYMONTH=");

                foreach(int month in byMonth)
                    sb.AppendFormat("{0},", month);

                sb.Remove(sb.Length - 1, 1);
            }

            if(byWeekNo.Count != 0)
            {
                sb.Append(";BYWEEKNO=");

                foreach(int week in byWeekNo)
                    sb.AppendFormat("{0},", week);

                sb.Remove(sb.Length - 1, 1);
            }

            if(byYearDay.Count != 0)
            {
                sb.Append(";BYYEARDAY=");

                foreach(int yearDay in byYearDay)
                    sb.AppendFormat("{0},", yearDay);

                sb.Remove(sb.Length - 1, 1);
            }

            if(byMonthDay.Count != 0)
            {
                sb.Append(";BYMONTHDAY=");

                foreach(int monthDay in byMonthDay)
                    sb.AppendFormat("{0},", monthDay);

                sb.Remove(sb.Length - 1, 1);
            }

            if(byDay.Count != 0)
            {
                sb.Append(";BYDAY=");

                foreach(DayInstance di in byDay)
                    sb.AppendFormat("{0},", di.ToString());

                sb.Remove(sb.Length - 1, 1);
            }

            if(byHour.Count != 0)
            {
                sb.Append(";BYHOUR=");

                foreach(int hour in byHour)
                    sb.AppendFormat("{0},", hour);

                sb.Remove(sb.Length - 1, 1);
            }

            if(byMinute.Count != 0)
            {
                sb.Append(";BYMINUTE=");

                foreach(int minute in byMinute)
                    sb.AppendFormat("{0},", minute);

                sb.Remove(sb.Length - 1, 1);
            }

            if(bySecond.Count != 0)
            {
                sb.Append(";BYSECOND=");

                foreach(int second in bySecond)
                    sb.AppendFormat("{0},", second);

                sb.Remove(sb.Length - 1, 1);
            }

            if(bySetPos.Count != 0)
            {
                sb.Append(";BYSETPOS=");

                foreach(int pos in bySetPos)
                    sb.AppendFormat("{0},", pos);

                sb.Remove(sb.Length - 1, 1);
            }

            if(!this.CanOccurOnHoliday)
                sb.Append(";X-EWSOFTWARE-OCCURONHOL=0");

            if(customProps.Count != 0)
                foreach(string s in customProps)
                {
                    sb.Append(';');
                    sb.Append(s);
                }

            return sb.ToString();
        }

        /// <summary>
        /// Convert the recurrence to its string form including the starting date/time
        /// </summary>
        /// <returns>This returns the recurrence in its string form (iCalendar format) but also includes the
        /// starting date/time as an extended property (X-EWSOFTWARE-DTSTART) which isn't normally part of the
        /// recurrence.  This is useful if you are using the recurrence outside of the iCalendar framework.</returns>
        public string ToStringWithStartDateTime()
        {
            return String.Format(CultureInfo.InvariantCulture, "{0};X-EWSOFTWARE-DTSTART={1}", this.ToString(),
                this.StartDateTime.ToUniversalTime().ToString(ISO8601Format.BasicDateTimeUniversal,
                    CultureInfo.InvariantCulture));
        }

        /// <summary>
        /// Convert the recurrence to its vCalendar 1.0 string form
        /// </summary>
        /// <returns>A string containing the recurrence information</returns>
        /// <remarks>The vCalendar 1.0 basic grammar format is much more limited than the iCalendar 2.0 format.
        /// Any options that are not part of the basic grammar are ignored.</remarks>
        public string ToVCalendarString()
        {
            StringBuilder sb = new StringBuilder(256);

            switch(this.Frequency)
            {
                case RecurFrequency.Daily:
                    sb.AppendFormat("D{0}", this.Interval);
                    break;

                case RecurFrequency.Weekly:
                    sb.AppendFormat("W{0}", this.Interval);

                    if(byDay.Count != 0)
                        foreach(DayInstance d in byDay)
                            sb.AppendFormat(" {0}", abbrevDays[(int)d.DayOfWeek]);
                    break;

                case RecurFrequency.Monthly:
                    if(byMonthDay.Count != 0)
                    {
                        sb.AppendFormat("MD{0}", this.Interval);

                        foreach(int day in byMonthDay)
                            if(day < 0)
                                sb.AppendFormat(" {0}-", day * -1);
                            else
                                sb.AppendFormat(" {0}", day);
                    }
                    else
                    {
                        sb.AppendFormat("MP{0}", this.Interval);

                        foreach(DayInstance d in byDay)
                        {
                            // If the day instance is zero, assume BYSETPOS contains the days to use
                            if(d.Instance == 0)
                            {
                                foreach(int p in bySetPos)
                                    if(p < 0)
                                        sb.AppendFormat(" {0}- {1}", p * -1, abbrevDays[(int)d.DayOfWeek]);
                                    else
                                        sb.AppendFormat(" {0}+ {1}", p, abbrevDays[(int)d.DayOfWeek]);
                            }
                            else
                                if(d.Instance < 0)
                                    sb.AppendFormat(" {0}- {1}", d.Instance * -1, abbrevDays[(int)d.DayOfWeek]);
                                else
                                    sb.AppendFormat(" {0}+ {1}", d.Instance, abbrevDays[(int)d.DayOfWeek]);
                        }
                    }
                    break;

                case RecurFrequency.Yearly:
                    if(byMonth.Count != 0)
                    {
                        sb.AppendFormat("YM{0}", this.Interval);

                        foreach(int month in byMonth)
                            sb.AppendFormat(" {0}", month);
                    }
                    else
                    {
                        sb.AppendFormat("YD{0}", this.Interval);

                        // There don't appear to be any examples that suggest a negative year day value is
                        // supported but we'll assume it is.
                        foreach(int day in byYearDay)
                            if(day < 0)
                                sb.AppendFormat(" {0}-", day * -1);
                            else
                                sb.AppendFormat(" {0}", day);
                    }
                    break;

                default:
                    // Anything else is unsupported
                    break;
            }

            if(sb.Length > 0)
                if(this.MaximumOccurrences != 0)
                    sb.AppendFormat(" #{0}", this.MaximumOccurrences);
                else
                    if(this.RecurUntil == DateTime.MaxValue)
                        sb.Append(" #0");   // Forever
                    else
                        sb.AppendFormat(" {0}",
                            this.RecurUntil.ToUniversalTime().ToString(ISO8601Format.BasicDateTimeUniversal,
                                CultureInfo.InvariantCulture));

            return sb.ToString();
        }

        /// <summary>
        /// Initialize a daily recurrence pattern
        /// </summary>
        /// <param name="recurInterval">The interval between occurrences in days</param>
        /// <remarks>This is a convenience method that mimics the daily recurrence pattern in Microsoft Outlook.
        /// When called, it sets up the recurrence for a daily pattern that recurs at the specified interval.
        /// All rule parts are cleared prior to setting the daily options but other parameters such as the start
        /// date are left alone.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex5"]/*' />
        public void RecurDaily(int recurInterval)
        {
            this.Frequency = RecurFrequency.Daily;
            this.Interval = recurInterval;

            byMonth.Clear();
            byWeekNo.Clear();
            byYearDay.Clear();
            byMonthDay.Clear();
            byHour.Clear();
            byMinute.Clear();
            bySecond.Clear();
            bySetPos.Clear();
            byDay.Clear();
            customProps.Clear();
        }

        /// <summary>
        /// This emulates the Microsoft Outlook daily pattern with the "Every Weekday" option selected
        /// </summary>
        /// <remarks>This actually equates to a weekly pattern that recurs every week on Monday through Friday.
        /// It is a convenience method that can be used in place of <see cref="RecurWeekly"/> for this particular
        /// recurrence pattern</remarks>
        /// <example>
        /// This call:<br/><br/>
        /// <code language="none">
        /// r.RecurEveryWeekday();
        /// </code>
        /// <br/>
        /// Is equivalent to this call:<br/><br/>
        /// <code language="none">
        /// r.RecurWeekly(1, DaysOfWeek.Weekdays);
        /// </code>
        /// </example>
        public void RecurEveryWeekday()
        {
            this.RecurWeekly(1, DaysOfWeek.Weekdays);
        }

        /// <summary>
        /// Initialize a weekly recurrence pattern
        /// </summary>
        /// <param name="recurInterval">The interval between occurrences in weeks</param>
        /// <param name="daysOfWeek">The days of the week on which the instances should occur</param>
        /// <remarks>This is a convenience method that mimics the weekly recurrence pattern in Microsoft Outlook.
        /// When called, it sets up the recurrence for a weekly pattern that recurs at the specified interval on
        /// the specified days of the week.  All rule parts are cleared prior to setting the weekly options but
        /// other parameters such as the start date are left alone.</remarks>
        /// <seealso cref="DaysOfWeek"/>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex7"]/*' />
        public void RecurWeekly(int recurInterval, DaysOfWeek daysOfWeek)
        {
            this.Frequency = RecurFrequency.Weekly;
            this.Interval = recurInterval;

            byMonth.Clear();
            byWeekNo.Clear();
            byYearDay.Clear();
            byMonthDay.Clear();
            byHour.Clear();
            byMinute.Clear();
            bySecond.Clear();
            bySetPos.Clear();
            byDay.Clear();
            customProps.Clear();

            // Set day(s)
            if((daysOfWeek & DaysOfWeek.Sunday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Sunday));

            if((daysOfWeek & DaysOfWeek.Monday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Monday));

            if((daysOfWeek & DaysOfWeek.Tuesday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Tuesday));

            if((daysOfWeek & DaysOfWeek.Wednesday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Wednesday));

            if((daysOfWeek & DaysOfWeek.Thursday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Thursday));

            if((daysOfWeek & DaysOfWeek.Friday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Friday));

            if((daysOfWeek & DaysOfWeek.Saturday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Saturday));
        }

        /// <summary>
        /// Initialize a monthly recurrence pattern that occurs on a specific day of the month at the specified
        /// monthly interval.
        /// </summary>
        /// <param name="day">The day of the month on which to occur</param>
        /// <param name="recurInterval">The interval between occurrences in months</param>
        /// <overloads>This method has two overloads.</overloads>
        /// <remarks>This is a convenience method that mimics the monthly recurrence pattern in Microsoft
        /// Outlook.  When called, it sets up the recurrence for a monthly pattern that recurs at the specified
        /// interval on the specified day of the month.  All rule parts are cleared prior to setting the monthly
        /// options but other parameters such as the start date are left alone.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">An exception will be thrown if the day value is not
        /// between 1 and 31.</exception>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex6"]/*' />
        public void RecurMonthly(int day, int recurInterval)
        {
            // Day of the month should be between 1 and 31
            if(day < 1 || day > 31)
                throw new ArgumentOutOfRangeException(nameof(day), day, LR.GetString("ExRecurBadDayOfMonth"));

            this.Frequency = RecurFrequency.Monthly;
            this.Interval = recurInterval;

            byMonth.Clear();
            byWeekNo.Clear();
            byYearDay.Clear();
            byMonthDay.Clear();
            byHour.Clear();
            byMinute.Clear();
            bySecond.Clear();
            bySetPos.Clear();
            byDay.Clear();
            customProps.Clear();

            byMonthDay.Add(day);
        }

        /// <summary>
        /// Initialize a monthly recurrence pattern that occurs on a specific occurrence of a day of the week at
        /// the specified monthly interval (i.e. the 4th Tuesday every two months).
        /// </summary>
        /// <param name="occur">The occurrence of the day of the week on which to occur.</param>
        /// <param name="daysOfWeek">The days of the week on which to occur.  This may be an individual week day
        /// or any combination of week days.</param>
        /// <param name="recurInterval">The interval between occurrences in months.</param>
        /// <remarks>This is a convenience method that mimics the monthly recurrence pattern in Microsoft
        /// Outlook.  When called, it sets up the recurrence for a monthly pattern that recurs at the specified
        /// interval on the specified occurrence of the days of the week.  All rule parts are cleared prior to
        /// setting the monthly options but other parameters such as the start date are left alone.</remarks>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex6"]/*' />
        public void RecurMonthly(DayOccurrence occur, DaysOfWeek daysOfWeek, int recurInterval)
        {
            this.Frequency = RecurFrequency.Monthly;
            this.Interval = recurInterval;

            byMonth.Clear();
            byWeekNo.Clear();
            byYearDay.Clear();
            byMonthDay.Clear();
            byHour.Clear();
            byMinute.Clear();
            bySecond.Clear();
            bySetPos.Clear();
            byDay.Clear();
            customProps.Clear();

            // Set day(s)
            if((daysOfWeek & DaysOfWeek.Sunday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Sunday));

            if((daysOfWeek & DaysOfWeek.Monday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Monday));

            if((daysOfWeek & DaysOfWeek.Tuesday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Tuesday));

            if((daysOfWeek & DaysOfWeek.Wednesday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Wednesday));

            if((daysOfWeek & DaysOfWeek.Thursday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Thursday));

            if((daysOfWeek & DaysOfWeek.Friday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Friday));

            if((daysOfWeek & DaysOfWeek.Saturday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Saturday));

            // If only one day was added, set its instance as it will be more efficient than using BYSETPOS
            if(byDay.Count == 1)
            {
                if(occur == DayOccurrence.Last)
                    byDay[0].Instance = -1;
                else
                    byDay[0].Instance = (int)occur;
            }
            else
                if(occur == DayOccurrence.Last)
                    bySetPos.Add(-1);
                else
                    bySetPos.Add((int)occur);
        }

        /// <summary>
        /// Initialize a yearly recurrence that occurs on a specific month and day at the specified yearly
        /// interval.
        /// </summary>
        /// <param name="month">The month in which to occur</param>
        /// <param name="day">The day on which to occur</param>
        /// <param name="recurInterval">The interval between occurrences in years</param>
        /// <remarks>This is a convenience method that mimics the yearly recurrence pattern in Microsoft Outlook.
        /// When called, it sets up the recurrence for a yearly pattern that recurs at the specified interval on
        /// the specified month and day.  All rule parts are cleared prior to setting the yearly options but
        /// other parameters such as the start date are left alone.</remarks>
        /// <overloads>This method has two overloads.</overloads>
        /// <exception cref="ArgumentOutOfRangeException">An exception is thrown if the month is not between 1
        /// and 12.</exception>
        /// <exception cref="ArgumentException">An exception is thrown if the day is not valid for the specified
        /// month.</exception>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex8"]/*' />
        public void RecurYearly(int month, int day, int recurInterval)
        {
            // Month should be valid
            if(month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), month, LR.GetString("ExRecurBadMonth"));

            // Day of the month should be valid.  NOTE: We use a leap year so that 29 is valid for February.
            // It'll get thrown out by the recurrence when it isn't valid.
            if(day < 1 || day > DateTime.DaysInMonth(2004, month))
                throw new ArgumentOutOfRangeException(nameof(day), day, LR.GetString("ExRecurInvalidDayForMonth"));

            this.Frequency = RecurFrequency.Yearly;
            this.Interval = recurInterval;

            byMonth.Clear();
            byWeekNo.Clear();
            byYearDay.Clear();
            byMonthDay.Clear();
            byHour.Clear();
            byMinute.Clear();
            bySecond.Clear();
            bySetPos.Clear();
            byDay.Clear();
            customProps.Clear();

            byMonth.Add(month);
            byMonthDay.Add(day);
        }

        /// <summary>
        /// Initialize a yearly recurrence pattern that occurs on a specific occurrence of a day of the week in
        /// the specified month at the specified yearly interval (i.e. the last Sunday in September every year).
        /// </summary>
        /// <param name="occur">The occurrence of the day of the week on which to occur</param>
        /// <param name="daysOfWeek">The day of the week on which to occur</param>
        /// <param name="month">The month in which to occur.</param>
        /// <param name="recurInterval">The interval between occurrences in years</param>
        /// <remarks>This is a convenience method that mimics the yearly recurrence pattern in Microsoft Outlook.
        /// When called, it sets up the recurrence for a yearly pattern that recurs at the specified interval on
        /// the specified occurrence of the days of the week.  All rule parts are cleared prior to setting the
        /// monthly options but other parameters such as the start date are left alone.</remarks>
        /// <exception cref="ArgumentOutOfRangeException">An exception is thrown if the month is not between 1
        /// and 12.</exception>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex8"]/*' />
        public void RecurYearly(DayOccurrence occur, DaysOfWeek daysOfWeek, int month, int recurInterval)
        {
            // Month should be valid
            if(month < 1 || month > 12)
                throw new ArgumentOutOfRangeException(nameof(month), month, LR.GetString("ExRecurBadMonth"));

            this.Frequency = RecurFrequency.Yearly;
            this.Interval = recurInterval;

            byMonth.Clear();
            byWeekNo.Clear();
            byYearDay.Clear();
            byMonthDay.Clear();
            byHour.Clear();
            byMinute.Clear();
            bySecond.Clear();
            bySetPos.Clear();
            byDay.Clear();
            customProps.Clear();

            byMonth.Add(month);

            // Set day(s)
            if((daysOfWeek & DaysOfWeek.Sunday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Sunday));

            if((daysOfWeek & DaysOfWeek.Monday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Monday));

            if((daysOfWeek & DaysOfWeek.Tuesday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Tuesday));

            if((daysOfWeek & DaysOfWeek.Wednesday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Wednesday));

            if((daysOfWeek & DaysOfWeek.Thursday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Thursday));

            if((daysOfWeek & DaysOfWeek.Friday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Friday));

            if((daysOfWeek & DaysOfWeek.Saturday) != 0)
                byDay.Add(new DayInstance(DayOfWeek.Saturday));

            // If only one day was added, set its instance as it will be more efficient than using BYSETPOS
            if(byDay.Count == 1)
            {
                if(occur == DayOccurrence.Last)
                    byDay[0].Instance = -1;
                else
                    byDay[0].Instance = (int)occur;
            }
            else
                if(occur == DayOccurrence.Last)
                    bySetPos.Add(-1);
                else
                    bySetPos.Add((int)occur);
        }

        /// <summary>
        /// This method is used to return all recurring instances based on the current settings alone
        /// </summary>
        /// <remarks>This is best used with a recurrence that ends after a specific number of occurrences or by a
        /// specific date.  If set to never end, this will return a really large collection of dates.
        /// </remarks>
        /// <returns>Returns a <see cref="DateTimeCollection"/> of <see cref="DateTime" /> objects that represent
        /// all instances found using the current settings.</returns>
        /// <seealso cref="InstancesBetween"/>
        /// <seealso cref="OccursOn"/>
        /// <seealso cref="NextInstance"/>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex2"]/*' />
        public DateTimeCollection AllInstances()
        {
            return GenerateInstances(this.StartDateTime, DateTime.MaxValue);
        }

        /// <summary>
        /// This method is used to determine whether or not an instance of the recurrence falls on the specified
        /// date/time.
        /// </summary>
        /// <param name="checkDate">The date/time to check for an occurrence.</param>
        /// <param name="includeTime">If true, time is included in the search.  If false, time is ignored and it
        /// returns true if an instance occurs at any time on the given date.</param>
        /// <returns>Returns true if an instance occurs on the specified date/time, false if not</returns>
        /// <seealso cref="InstancesBetween"/>
        /// <seealso cref="AllInstances"/>
        /// <seealso cref="NextInstance"/>
        public bool OccursOn(DateTime checkDate, bool includeTime)
        {
            if(includeTime)
                return (this.InstancesBetween(checkDate, checkDate).Count > 0);

            // Use the full time range for the given date
            return (this.InstancesBetween(checkDate.Date, new DateTime(checkDate.Year, checkDate.Month,
                checkDate.Day, 23, 59, 59)).Count > 0);
        }

        /// <summary>
        /// This method is used to return all recurring instances between the two specified date/times based on
        /// the current settings.
        /// </summary>
        /// <param name="fromDate">The minimum date/time on or after which instances should occur</param>
        /// <param name="toDate">The maximum date/time on or before which instances should occur</param>
        /// <returns>Returns a <see cref="DateTimeCollection"/> of <see cref="DateTime" /> objects that represent
        /// the instances found between the two specified date/times.</returns>
        /// <seealso cref="AllInstances"/>
        /// <seealso cref="OccursOn"/>
        /// <seealso cref="NextInstance"/>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex4"]/*' />
        public DateTimeCollection InstancesBetween(DateTime fromDate, DateTime toDate)
        {
            DateTimeCollection dtc;
            int idx;

            // If we are using a count, we don't know how many instances may have occurred prior to the requested
            // start date.  As such, we must get all instances and filter them.  It may be more work, but
            // shouldn't be too bad as usually the count specified isn't excessive.
            if(maxOccur != 0)
            {
                dtc = GenerateInstances(this.StartDateTime, DateTime.MaxValue);

                // Filter out dates outside the requested range
                for(idx = 0; idx < dtc.Count; idx++)
                    if(dtc[idx] < fromDate || dtc[idx] > toDate)
                    {
                        dtc.RemoveAt(idx);
                        idx--;
                    }
            }
            else
                dtc = GenerateInstances(fromDate, toDate);

            return dtc;
        }

        /// <summary>
        /// This method is used to return the next recurring instances on or after the specified date/time based
        /// on the current settings.
        /// </summary>
        /// <param name="fromDate">The minimum date/time on or after which the instance should occur.</param>
        /// <param name="onlyAfter">Specify true to only return a next instance if it occurs after the specified
        /// date/time.  If false, it will allow an exact match on the specified date/time to be returned as well.</param>
        /// <returns>Returns a <see cref="DateTime"/> that represent the next instance found or <c>DateTime.MinValue</c>
        /// if there are no more instances.</returns>
        /// <seealso cref="AllInstances"/>
        /// <seealso cref="OccursOn"/>
        /// <seealso cref="InstancesBetween"/>
        public DateTime NextInstance(DateTime fromDate, bool onlyAfter)
        {
            DateTimeCollection dtc;
            DateTime instance = DateTime.MinValue;
            TimeSpan ts;
            int idx;

            // If it won't allow an exact match, bump it forward one second
            if(onlyAfter)
                fromDate = fromDate.AddSeconds(1);

            // If we are using a count, we don't know how many instances may have occurred prior to the requested
            // start date.  As such, we must get all instances and filter them.  It may be more work, but
            // shouldn't be too bad as usually the count specified isn't excessive.
            if(maxOccur != 0)
            {
                dtc = GenerateInstances(this.StartDateTime, DateTime.MaxValue);

                // Filter out dates outside the requested range
                for(idx = 0; idx < dtc.Count; idx++)
                    if(dtc[idx] >= fromDate)
                    {
                        instance = dtc[idx];
                        break;
                    }
            }
            else
            {
                // Figure out the interval based on the frequency
                switch(frequency)
                {
                    case RecurFrequency.Yearly:
                        ts = new TimeSpan(Duration.TicksPerYear);
                        break;

                    case RecurFrequency.Monthly:
                        ts = new TimeSpan(Duration.TicksPerMonth);
                        break;

                    case RecurFrequency.Weekly:
                        ts = new TimeSpan(Duration.TicksPerWeek);
                        break;

                    case RecurFrequency.Daily:
                        ts = new TimeSpan(TimeSpan.TicksPerDay);
                        break;

                    default:    // Hourly or less
                        ts = new TimeSpan(TimeSpan.TicksPerHour);
                        break;
                }

                // Search for the next instance
                while(fromDate < untilDate)
                {
                    dtc = GenerateInstances(fromDate, fromDate.Add(ts));
                    if(dtc.Count > 0)
                    {
                        instance = dtc[0];
                        break;
                    }

                    fromDate = fromDate.Add(ts);
                }
            }

            return instance;
        }

        /// <summary>
        /// Get a type-safe <see cref="Recurrence"/> enumerator
        /// </summary>
        /// <remarks><para>Set up the recurrence pattern and the starting date before using the enumerator.  It
        /// is best to use a recurrence that ends after a specific number of occurrences or a specific date.  If
        /// enumerating one that does not end, it will go until it is about a year short of
        /// <see cref="DateTime">DateTime.MaxValue</see>.</para>
        /// 
        /// <para>Note that this is not exposed via the <see cref="System.Collections.IEnumerable"/> interface.
        /// Instead, it is only used by <c>foreach</c> loop code or if called explicitly.  As such, the object
        /// itself cannot be used as a data source for data binding.  However, the <see cref="DateTimeCollection"/>
        /// returned by the <see cref="InstancesBetween"/> and <see cref="AllInstances"/> methods can be used as
        /// a data source.</para></remarks>
        /// <returns>A type-safe <c>Recurrence</c> enumerator</returns>
        /// <include file='DateExamples.xml' path='Examples/Recurrence/HelpEx[@name="Ex5"]/*' />
        public RecurrenceEnumerator GetEnumerator()
        {
            return new RecurrenceEnumerator(this);
        }
        #endregion

        #region ISerializable implementation
        //=====================================================================

        /// <summary>
        /// This implements the <see cref="System.Runtime.Serialization.ISerializable"/> interface and adds the
        /// appropriate members to the serialization info based on the recurrence pattern.
        /// </summary>
        /// <param name="info">The serialization info object</param>
        /// <param name="context">The streaming context</param>
        [SecurityCritical]
        public virtual void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            // Since the string form is the most compact, use that
            if(info != null)
                info.AddValue("Recurrence", this.ToStringWithStartDateTime());
        }
        #endregion

        #region IXmlSerializable implementation
        //=====================================================================

        /// <summary>
        /// This returns the schema for the serialized recurrence
        /// </summary>
        /// <returns>The XML schema</returns>
        public XmlSchema GetSchema()
        {
            XmlSchema xs = null;

            using(StreamReader sr = new StreamReader(Assembly.GetExecutingAssembly().GetManifestResourceStream(
              "EWSoftware.PDI.Schemas.Recurrence.xsd")))
            {
                xs = XmlSchema.Read(sr, null);
            }

            return xs;
        }

        /// <summary>
        /// This is called to serialize the instance to XML
        /// </summary>
        /// <param name="writer">The XML writer to which the instance is serialized</param>
        /// <remarks>The recurrence object already has an extremely efficient serialized format, the iCalendar
        /// RRULE.  As such, when serializing we'll use that compact form rather than a more verbose form with
        /// the various properties all spelled out with separate entities.
        /// </remarks>
        public void WriteXml(XmlWriter writer)
        {
            if(writer != null)
                writer.WriteString(this.ToStringWithStartDateTime());
        }

        /// <summary>
        /// This is called to deserialize the instance from XML
        /// </summary>
        /// <param name="reader">The XML reader from which the instance is deserialized</param>
        public void ReadXml(XmlReader reader)
        {
            if(reader != null)
                this.Parse(reader.ReadString());
        }
        #endregion

        #region Description conversion properties and methods
        //=====================================================================

        /// <summary>
        /// This read-only property is used to examine the recurrence and determine if it is a simple pattern or
        /// an advanced pattern.
        /// </summary>
        /// <returns>True if it is an advanced pattern or false if it is a simple pattern</returns>
        /// <remarks>This is used by the Windows Forms recurrence pattern control and the ASP.NET recurrence
        /// pattern web server control to determine if they can use one of the simple frequency panels or if
        /// it is more advanced and requires the use of the Advanced panel.  It is also used by the
        /// <see cref="ToDescription"/> method to figure out how to convert the recurrence to a plain text
        /// description.</remarks>
        public bool IsAdvancedPattern
        {
            get
            {
                DaysOfWeek rd = DaysOfWeek.None;
                bool isAdvanced = false;

                int idx, count, byMonthCount = this.ByMonth.Count, byWeekNoCount = this.ByWeekNo.Count,
                    byYearDayCount = this.ByYearDay.Count, byMonthDayCount = this.ByMonthDay.Count,
                    byDayCount = this.ByDay.Count, byHourCount = this.ByHour.Count,
                    byMinuteCount = this.ByMinute.Count, bySecondCount = this.BySecond.Count,
                    bySetPosCount = this.BySetPos.Count;

                switch(this.Frequency)
                {
                    case RecurFrequency.Secondly:
                    case RecurFrequency.Minutely:
                    case RecurFrequency.Hourly:
                        // Anything but an interval on these is advanced
                        if(byMonthCount + byYearDayCount + byMonthDayCount + byDayCount + byHourCount +
                          byMinuteCount + bySecondCount + bySetPosCount != 0)
                            isAdvanced = true;
                        break;

                    case RecurFrequency.Daily:
                        // Anything but an interval on this is advanced unless it's ByDay which has an exception
                        if(byMonthCount + byYearDayCount + byMonthDayCount + byHourCount + byMinuteCount +
                          bySecondCount + bySetPosCount != 0)
                        {
                            isAdvanced = true;
                        }
                        else
                            if(byDayCount == 5)
                            {
                                // Any interval other than one makes it advanced
                                if(interval != 1)
                                    isAdvanced = true;
                                else
                                {
                                    // If it's just weekdays, it's still simple
                                    for(idx = 0; idx < 5; idx++)
                                        if(this.ByDay[idx].Instance != 0 ||
                                          this.ByDay[idx].DayOfWeek == DayOfWeek.Saturday ||
                                          this.ByDay[idx].DayOfWeek == DayOfWeek.Sunday)
                                        {
                                            isAdvanced = true;
                                        }
                                }
                            }
                            else
                                if(byDayCount != 0)
                                    isAdvanced = true;
                        break;

                    case RecurFrequency.Weekly:
                        // Anything but an interval and ByDay on this is advanced
                        if(byMonthCount + byYearDayCount + byMonthDayCount + byHourCount + byMinuteCount +
                          bySecondCount + bySetPosCount != 0)
                            isAdvanced = true;
                        break;

                    case RecurFrequency.Monthly:
                        // Anything but an interval on this is advanced unless it's ByMonthDay, ByDay, or
                        // BySetPos which have exceptions.
                        if(byMonthCount + byYearDayCount + byHourCount + byMinuteCount + bySecondCount != 0)
                            isAdvanced = true;
                        else
                            if(byDayCount < 2)
                            {
                                if((byDayCount == 0 && byMonthDayCount != 1) ||
                                  (byDayCount == 1 && byMonthDayCount != 0) ||
                                  (byDayCount == 1 && ((this.ByDay[0].Instance < 1 &&
                                  this.ByDay[0].Instance != -1) || this.ByDay[0].Instance > 5)) ||
                                  bySetPosCount != 0)
                                {
                                    isAdvanced = true;
                                }
                            }
                            else
                            {
                                // Figure out days used
                                count = 0;

                                foreach(DayInstance di in this.ByDay)
                                    switch(di.DayOfWeek)
                                    {
                                        case DayOfWeek.Sunday:
                                            rd |= DaysOfWeek.Sunday;
                                            count++;
                                            break;

                                        case DayOfWeek.Monday:
                                            rd |= DaysOfWeek.Monday;
                                            count++;
                                            break;

                                        case DayOfWeek.Tuesday:
                                            rd |= DaysOfWeek.Tuesday;
                                            count++;
                                            break;

                                        case DayOfWeek.Wednesday:
                                            rd |= DaysOfWeek.Wednesday;
                                            count++;
                                            break;

                                        case DayOfWeek.Thursday:
                                            rd |= DaysOfWeek.Thursday;
                                            count++;
                                            break;

                                        case DayOfWeek.Friday:
                                            rd |= DaysOfWeek.Friday;
                                            count++;
                                            break;

                                        case DayOfWeek.Saturday:
                                            rd |= DaysOfWeek.Saturday;
                                            count++;
                                            break;
                                    }

                                // If not EveryDay, Weekdays, Weekends, or a single day, it's advanced
                                if(bySetPosCount != 1 || byMonthDayCount != 0 || (count > 1 &&
                                  rd != DaysOfWeek.None && rd != DaysOfWeek.EveryDay &&
                                  rd != DaysOfWeek.Weekdays && rd != DaysOfWeek.Weekends))
                                {
                                    isAdvanced = true;
                                }
                            }
                        break;

                    case RecurFrequency.Yearly:
                        // Anything but an interval on this is advanced unless it's ByMonth, ByMonthDay, ByDay,
                        // or BySetPos which have exceptions.
                        if(byWeekNoCount + byYearDayCount + byHourCount + byMinuteCount + bySecondCount != 0 ||
                          byMonthCount != 1)
                        {
                            isAdvanced = true;
                        }
                        else
                            if(byDayCount < 2)
                            {
                                if((byDayCount == 0 && byMonthDayCount != 1) || (byDayCount == 1 &&
                                  byMonthDayCount != 0) || (byDayCount == 1 && ((this.ByDay[0].Instance < 1 &&
                                  this.ByDay[0].Instance != -1) || this.ByDay[0].Instance > 5)) ||
                                  bySetPosCount != 0)
                                {
                                    isAdvanced = true;
                                }
                            }
                            else
                            {
                                // Figure out days used
                                count = 0;

                                foreach(DayInstance di in this.ByDay)
                                    switch(di.DayOfWeek)
                                    {
                                        case DayOfWeek.Sunday:
                                            rd |= DaysOfWeek.Sunday;
                                            count++;
                                            break;

                                        case DayOfWeek.Monday:
                                            rd |= DaysOfWeek.Monday;
                                            count++;
                                            break;

                                        case DayOfWeek.Tuesday:
                                            rd |= DaysOfWeek.Tuesday;
                                            count++;
                                            break;

                                        case DayOfWeek.Wednesday:
                                            rd |= DaysOfWeek.Wednesday;
                                            count++;
                                            break;

                                        case DayOfWeek.Thursday:
                                            rd |= DaysOfWeek.Thursday;
                                            count++;
                                            break;

                                        case DayOfWeek.Friday:
                                            rd |= DaysOfWeek.Friday;
                                            count++;
                                            break;

                                        case DayOfWeek.Saturday:
                                            rd |= DaysOfWeek.Saturday;
                                            count++;
                                            break;
                                    }

                                // If not EveryDay, Weekdays, Weekends, or a single day, it's advanced
                                if(bySetPosCount != 1 || byMonthDayCount != 0 || (count > 1 &&
                                  rd != DaysOfWeek.None && rd != DaysOfWeek.EveryDay &&
                                  rd != DaysOfWeek.Weekdays && rd != DaysOfWeek.Weekends))
                                {
                                    isAdvanced = true;
                                }
                            }
                        break;
                }

                return isAdvanced;
            }
        }

        /// <summary>
        /// Convert the recurrence to a plain text description
        /// </summary>
        /// <returns>The recurrence described in plain text</returns>
        public string ToDescription()
        {
            string desc, range, weekStartDesc = String.Empty, holidayDesc = ".";

            if(frequency == RecurFrequency.Undefined)
                return LR.GetString("RDUndefined");

            if(this.IsAdvancedPattern)
                return this.ToAdvancedDescription();

            // Simple pattern.  Describe the frequency.
            switch(frequency)
            {
                case RecurFrequency.Yearly:
                    desc = this.ToYearlyDescription();
                    break;

                case RecurFrequency.Monthly:
                    desc = this.ToMonthlyDescription();
                    break;

                case RecurFrequency.Weekly:
                    desc = this.ToWeeklyDescription();

                    // Note the week start if it will be relevant
                    if(interval > 1 && byDay.Count != 0)
                        weekStartDesc = LR.GetString("RDWeekStart",
                            CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)weekStart]);
                    break;

                case RecurFrequency.Daily:
                    if(this.ByDay.Count == 0)
                        desc = LR.GetString("RDDaily", interval);
                    else
                        desc = LR.GetString("RDEveryWeekday");
                    break;

                case RecurFrequency.Hourly:
                    desc = LR.GetString("RDHourly", interval);
                    break;

                case RecurFrequency.Minutely:
                    desc = LR.GetString("RDMinutely", interval);
                    break;

                default:    // Secondly
                    desc = LR.GetString("RDSecondly", interval);
                    break;
            }

            // Describe the range
            if(untilDate == DateTime.MaxValue && maxOccur == 0)
                range = LR.GetString("RDForever");
            else
                if(maxOccur != 0)
                    range = LR.GetString("RDMaxOccur", maxOccur);
                else
                    range = LR.GetString("RDRecurUntil", untilDate);

            // Note if instances cannot occur on holidays
            if(!canOccurOnHoliday)
                holidayDesc = LR.GetString("RDNoHolidays");

            return desc + range + weekStartDesc + holidayDesc;
        }

        /// <summary>
        /// Describe a weekly pattern
        /// </summary>
        private string ToWeeklyDescription()
        {
            StringBuilder sb = new StringBuilder(LR.GetString("RDWeekly", interval), 256);

            if(byDay.Count != 0)
            {
                sb.Append(LR.GetString("RDOn"));

                string[] dayNames = CultureInfo.CurrentCulture.DateTimeFormat.DayNames;

                for(int idx = 0; idx < byDay.Count; idx++)
                {
                    sb.Append(dayNames[(int)byDay[idx].DayOfWeek]);

                    if(idx < byDay.Count - 2)
                        sb.Append(", ");
                    else
                        if(idx < byDay.Count - 1)
                        {
                            if(idx != 0)
                                sb.Append(",");

                            sb.Append(LR.GetString("RDAnd"));
                        }
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Describe a monthly pattern
        /// </summary>
        private string ToMonthlyDescription()
        {
            DaysOfWeek rd = DaysOfWeek.None;
            int occurrence;

            if(byDay.Count == 0)
                return LR.GetString("RDMonthlyDayX", byMonthDay[0], interval);

            // If it's a single day, use ByDay.  If it's a combination, use ByDay with BySetPos.
            if(byDay.Count == 1)
            {
                occurrence = byDay[0].Instance;

                if(occurrence < 1 || occurrence > 4)
                    occurrence = 5;

                return LR.GetString("RDMonthlyDOW", LR.EnumDesc((DayOccurrence)occurrence).ToLower(
                    CultureInfo.CurrentCulture), LR.EnumDesc(DateUtils.ToDaysOfWeek(byDay[0].DayOfWeek)), interval);
            }

            // Figure out days used
            foreach(DayInstance di in byDay)
                switch(di.DayOfWeek)
                {
                    case DayOfWeek.Sunday:
                        rd |= DaysOfWeek.Sunday;
                        break;

                    case DayOfWeek.Monday:
                        rd |= DaysOfWeek.Monday;
                        break;

                    case DayOfWeek.Tuesday:
                        rd |= DaysOfWeek.Tuesday;
                        break;

                    case DayOfWeek.Wednesday:
                        rd |= DaysOfWeek.Wednesday;
                        break;

                    case DayOfWeek.Thursday:
                        rd |= DaysOfWeek.Thursday;
                        break;

                    case DayOfWeek.Friday:
                        rd |= DaysOfWeek.Friday;
                        break;

                    case DayOfWeek.Saturday:
                        rd |= DaysOfWeek.Saturday;
                        break;
                }

            // If not EveryDay, Weekdays, or Weekends, force it to a single day of the week
            if(rd == DaysOfWeek.None || (rd != DaysOfWeek.EveryDay && rd != DaysOfWeek.Weekdays &&
              rd != DaysOfWeek.Weekends))
            {
                rd = DateUtils.ToDaysOfWeek(DateUtils.ToDayOfWeek(rd));
            }

            occurrence = bySetPos[0];

            if(occurrence < 1 || occurrence > 4)
                occurrence = 5;

            return LR.GetString("RDMonthlyDOW", LR.EnumDesc((DayOccurrence)occurrence).ToLower(
                CultureInfo.CurrentCulture), LR.EnumDesc(rd).ToLower(CultureInfo.CurrentCulture), interval);
        }

        /// <summary>
        /// Describe a yearly pattern
        /// </summary>
        private string ToYearlyDescription()
        {
            DaysOfWeek rd = DaysOfWeek.None;
            int occurrence;

            if(byDay.Count == 0)
                return LR.GetString("RDYearlyDayX", CultureInfo.CurrentCulture.DateTimeFormat.MonthNames[
                    byMonth[0] - 1], byMonthDay[0], DayInstance.NumericSuffix(byMonthDay[0]), interval);

            // If it's a single day, use ByDay.  If it's a combination, use ByDay with BySetPos.
            if(byDay.Count == 1)
            {
                occurrence = byDay[0].Instance;

                if(occurrence < 1 || occurrence > 4)
                    occurrence = 5;

                return LR.GetString("RDYearlyDOW", LR.EnumDesc((DayOccurrence)occurrence).ToLower(
                    CultureInfo.CurrentCulture), LR.EnumDesc(DateUtils.ToDaysOfWeek(byDay[0].DayOfWeek)),
                    CultureInfo.CurrentCulture.DateTimeFormat.MonthNames[byMonth[0] - 1], interval);
            }

            // Figure out days used
            foreach(DayInstance di in byDay)
                switch(di.DayOfWeek)
                {
                    case DayOfWeek.Sunday:
                        rd |= DaysOfWeek.Sunday;
                        break;

                    case DayOfWeek.Monday:
                        rd |= DaysOfWeek.Monday;
                        break;

                    case DayOfWeek.Tuesday:
                        rd |= DaysOfWeek.Tuesday;
                        break;

                    case DayOfWeek.Wednesday:
                        rd |= DaysOfWeek.Wednesday;
                        break;

                    case DayOfWeek.Thursday:
                        rd |= DaysOfWeek.Thursday;
                        break;

                    case DayOfWeek.Friday:
                        rd |= DaysOfWeek.Friday;
                        break;

                    case DayOfWeek.Saturday:
                        rd |= DaysOfWeek.Saturday;
                        break;
                }

            // If not EveryDay, Weekdays, or Weekends, force it to a single day of the week
            if(rd == DaysOfWeek.None || (rd != DaysOfWeek.EveryDay && rd != DaysOfWeek.Weekdays &&
              rd != DaysOfWeek.Weekends))
            {
                rd = DateUtils.ToDaysOfWeek(DateUtils.ToDayOfWeek(rd));
            }

            occurrence = bySetPos[0];

            if(occurrence < 1 || occurrence > 4)
                occurrence = 5;

            return LR.GetString("RDYearlyDOW", LR.EnumDesc((DayOccurrence)occurrence).ToLower(
                CultureInfo.CurrentCulture), LR.EnumDesc(rd).ToLower(CultureInfo.CurrentCulture),
                CultureInfo.CurrentCulture.DateTimeFormat.MonthNames[byMonth[0] - 1], interval);
        }

        /// <summary>
        /// Describe an advanced pattern
        /// </summary>
        private string ToAdvancedDescription()
        {
            StringBuilder sb = new StringBuilder(256);
            string weekStartDesc = null;
            bool addComma = false;
            int idx;

            // Describe the frequency
            switch(frequency)
            {
                case RecurFrequency.Yearly:
                    sb.Append(LR.GetString("RDYearly", interval));

                    // Note the week start if it will be relevant
                    if(byWeekNo.Count != 0)
                        weekStartDesc = LR.GetString("RDWeekStart",
                            CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)weekStart]);
                    break;

                case RecurFrequency.Monthly:
                    sb.Append(LR.GetString("RDMonthly", interval));
                    break;

                case RecurFrequency.Weekly:
                    sb.Append(LR.GetString("RDWeekly", interval));

                    // Note the week start if it will be relevant
                    if(interval > 1 && byDay.Count != 0)
                        weekStartDesc = LR.GetString("RDWeekStart",
                            CultureInfo.CurrentCulture.DateTimeFormat.DayNames[(int)weekStart]);
                    break;

                case RecurFrequency.Daily:
                    sb.Append(LR.GetString("RDDaily", interval));
                    break;

                case RecurFrequency.Hourly:
                    sb.Append(LR.GetString("RDHourly", interval));
                    break;

                case RecurFrequency.Minutely:
                    sb.Append(LR.GetString("RDMinutely", interval));
                    break;

                default:    // Secondly
                    sb.Append(LR.GetString("RDSecondly", interval));
                    break;
            }

            // Describe the range
            if(untilDate == DateTime.MaxValue && maxOccur == 0)
                sb.Append(LR.GetString("RDForever"));
            else
                if(maxOccur != 0)
                    sb.Append(LR.GetString("RDMaxOccur", maxOccur));
                else
                    sb.Append(LR.GetString("RDRecurUntil", untilDate));

            // Note if the week start is different
            if(weekStartDesc != null)
                sb.Append(weekStartDesc);

            // Note if instances cannot occur on holidays
            if(!canOccurOnHoliday)
                sb.Append(LR.GetString("RDNoHolidays"));
            else
                sb.Append('.');

            // Add filter information if any is used
            if(byMonth.Count != 0 || byWeekNo.Count != 0 || byYearDay.Count != 0 || byMonthDay.Count != 0 ||
              byDay.Count != 0 || byHour.Count != 0 || byMinute.Count != 0 || bySecond.Count != 0)
            {
                sb.Append(LR.GetString("RDLimitedTo"));

                string[] monthNames = CultureInfo.CurrentCulture.DateTimeFormat.MonthNames;

                for(idx = 0; idx < byMonth.Count; idx++)
                {
                    if(idx == 0)
                    {
                        sb.Append(LR.GetString("RDByMonth"));
                        addComma = true;
                    }

                    sb.Append(monthNames[byMonth[idx] - 1]);

                    if(idx < byMonth.Count - 2)
                        sb.Append(", ");
                    else
                        if(idx < byMonth.Count - 1)
                        {
                            if(idx != 0)
                                sb.Append(",");

                            sb.Append(LR.GetString("RDAnd"));
                        }
                }

                Recurrence.AddIntegerCollection(sb, byWeekNo, "RDByWeekNo", ref addComma);
                Recurrence.AddIntegerCollection(sb, byYearDay, "RDByYearDay", ref addComma);
                Recurrence.AddIntegerCollection(sb, byMonthDay, "RDByMonthDay", ref addComma);

                string[] dayNames = CultureInfo.CurrentCulture.DateTimeFormat.DayNames;

                for(idx = 0; idx < byDay.Count; idx++)
                {
                    if(idx == 0)
                    {
                        if(addComma)
                            sb.Append(",");
                        else
                            addComma = true;

                        sb.Append(LR.GetString("RDByDay"));
                    }

                    if(byDay[idx].Instance != 0)
                        sb.Append(byDay[idx].ToDescription());
                    else
                        sb.Append(dayNames[(int)byDay[idx].DayOfWeek]);

                    if(idx < byDay.Count - 2)
                        sb.Append(", ");
                    else
                        if(idx < byDay.Count - 1)
                        {
                            if(idx != 0)
                                sb.Append(",");

                            sb.Append(LR.GetString("RDAnd"));
                        }
                }

                Recurrence.AddIntegerCollection(sb, byHour, "RDByHour", ref addComma);
                Recurrence.AddIntegerCollection(sb, byMinute, "RDByMinute", ref addComma);
                Recurrence.AddIntegerCollection(sb, bySecond, "RDBySecond", ref addComma);
                Recurrence.AddIntegerCollection(sb, bySetPos, "RDBySetPos", ref addComma);

                sb.Append(".");
            }

            return sb.ToString();
        }

        /// <summary>
        /// Add integer collection values to the description
        /// </summary>
        private static void AddIntegerCollection(StringBuilder sb, UniqueIntegerCollection uic, string desc,
          ref bool addComma)
        {
            for(int idx = 0; idx < uic.Count; idx++)
            {
                if(idx == 0)
                {
                    if(addComma)
                        sb.Append(",");
                    else
                        addComma = true;

                    sb.Append(LR.GetString("RDThe"));
                }

                if(uic[idx] < 0)
                {
                    sb.Append(uic[idx] * -1);
                    sb.Append(DayInstance.NumericSuffix(uic[idx]));
                    sb.Append(' ');
                    sb.Append(LR.GetString("DIFromEnd"));
                }
                else
                {
                    sb.Append(uic[idx]);
                    sb.Append(DayInstance.NumericSuffix(uic[idx]));
                }

                if(idx < uic.Count - 2)
                    sb.Append(", ");
                else
                    if(idx < uic.Count - 1)
                    {
                        if(idx != 0)
                            sb.Append(",");

                        sb.Append(LR.GetString("RDAnd"));
                    }
            }

            if(uic.Count != 0)
                sb.Append(LR.GetString(desc));
        }
        #endregion
    }
}
