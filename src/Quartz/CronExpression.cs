#region License

/*
 * All content copyright Marko Lahma, unless otherwise indicated. All rights reserved.
 *
 * Licensed under the Apache License, Version 2.0 (the "License"); you may not
 * use this file except in compliance with the License. You may obtain a copy
 * of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS, WITHOUT
 * WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied. See the
 * License for the specific language governing permissions and limitations
 * under the License.
 *
 */

#endregion

using System.Globalization;
using System.Runtime.Serialization;
using System.Text;
using System.Text.RegularExpressions;

using Quartz.Util;

namespace Quartz
{
    /// <summary>
    /// Provides a parser and evaluator for unix-like cron expressions. Cron
    /// expressions provide the ability to specify complex time combinations such as
    /// &quot;At 8:00am every Monday through Friday&quot; or &quot;At 1:30am every
    /// last Friday of the month&quot;.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cron expressions are comprised of 6 required fields and one optional field
    /// separated by white space. The fields respectively are described as follows:
    /// </para>
    /// <table cellspacing="8">
    /// <tr>
    /// <th align="left">Field Name</th>
    /// <th align="left"> </th>
    /// <th align="left">Allowed Values</th>
    /// <th align="left"> </th>
    /// <th align="left">Allowed Special Characters</th>
    /// </tr>
    /// <tr>
    /// <td align="left">Seconds</td>
    /// <td align="left"> </td>
    /// <td align="left">0-59</td>
    /// <td align="left"> </td>
    /// <td align="left">, - /// /</td>
    /// </tr>
    /// <tr>
    /// <td align="left">Minutes</td>
    /// <td align="left"> </td>
    /// <td align="left">0-59</td>
    /// <td align="left"> </td>
    /// <td align="left">, - /// /</td>
    /// </tr>
    /// <tr>
    /// <td align="left">Hours</td>
    /// <td align="left"> </td>
    /// <td align="left">0-23</td>
    /// <td align="left"> </td>
    /// <td align="left">, - /// /</td>
    /// </tr>
    /// <tr>
    /// <td align="left">Day-of-month</td>
    /// <td align="left"> </td>
    /// <td align="left">1-31</td>
    /// <td align="left"> </td>
    /// <td align="left">, - /// ? / L W C</td>
    /// </tr>
    /// <tr>
    /// <td align="left">Month</td>
    /// <td align="left"> </td>
    /// <td align="left">1-12 or JAN-DEC</td>
    /// <td align="left"> </td>
    /// <td align="left">, - /// /</td>
    /// </tr>
    /// <tr>
    /// <td align="left">Day-of-Week</td>
    /// <td align="left"> </td>
    /// <td align="left">1-7 or SUN-SAT</td>
    /// <td align="left"> </td>
    /// <td align="left">, - /// ? / L #</td>
    /// </tr>
    /// <tr>
    /// <td align="left">Year (Optional)</td>
    /// <td align="left"> </td>
    /// <td align="left">empty, 1970-2199</td>
    /// <td align="left"> </td>
    /// <td align="left">, - /// /</td>
    /// </tr>
    /// </table>
    /// <para>
    /// The '*' character is used to specify all values. For example, &quot;*&quot;
    /// in the minute field means &quot;every minute&quot;.
    /// </para>
    /// <para>
    /// The '?' character is allowed for the day-of-month and day-of-week fields. It
    /// is used to specify 'no specific value'. This is useful when you need to
    /// specify something in one of the two fields, but not the other.
    /// </para>
    /// <para>
    /// The '-' character is used to specify ranges For example &quot;10-12&quot; in
    /// the hour field means &quot;the hours 10, 11 and 12&quot;.
    /// </para>
    /// <para>
    /// The ',' character is used to specify additional values. For example
    /// &quot;MON,WED,FRI&quot; in the day-of-week field means &quot;the days Monday,
    /// Wednesday, and Friday&quot;.
    /// </para>
    /// <para>
    /// The '/' character is used to specify increments. For example &quot;0/15&quot;
    /// in the seconds field means &quot;the seconds 0, 15, 30, and 45&quot;. And
    /// &quot;5/15&quot; in the seconds field means &quot;the seconds 5, 20, 35, and
    /// 50&quot;.  Specifying '*' before the  '/' is equivalent to specifying 0 is
    /// the value to start with. Essentially, for each field in the expression, there
    /// is a set of numbers that can be turned on or off. For seconds and minutes,
    /// the numbers range from 0 to 59. For hours 0 to 23, for days of the month 1 to
    /// 31, and for months 1 to 12. The &quot;/&quot; character simply helps you turn
    /// on every &quot;nth&quot; value in the given set. Thus &quot;7/6&quot; in the
    /// month field only turns on month &quot;7&quot;, it does NOT mean every 6th
    /// month, please note that subtlety.
    /// </para>
    /// <para>
    /// The 'L' character is allowed for the day-of-month and day-of-week fields.
    /// This character is short-hand for &quot;last&quot;, but it has different
    /// meaning in each of the two fields. For example, the value &quot;L&quot; in
    /// the day-of-month field means &quot;the last day of the month&quot; - day 31
    /// for January, day 28 for February on non-leap years. If used in the
    /// day-of-week field by itself, it simply means &quot;7&quot; or
    /// &quot;SAT&quot;. But if used in the day-of-week field after another value, it
    /// means &quot;the last xxx day of the month&quot; - for example &quot;6L&quot;
    /// means &quot;the last friday of the month&quot;. You can also specify an offset
    /// from the last day of the month, such as "L-3" which would mean the third-to-last
    /// day of the calendar month. <i>When using the 'L' option, it is important not to
    /// specify lists, or ranges of values, as you'll get confusing/unexpected results.</i>
    /// </para>
    /// <para>
    /// The 'W' character is allowed for the day-of-month field.  This character
    /// is used to specify the weekday (Monday-Friday) nearest the given day.  As an
    /// example, if you were to specify &quot;15W&quot; as the value for the
    /// day-of-month field, the meaning is: &quot;the nearest weekday to the 15th of
    /// the month&quot;. So if the 15th is a Saturday, the trigger will fire on
    /// Friday the 14th. If the 15th is a Sunday, the trigger will fire on Monday the
    /// 16th. If the 15th is a Tuesday, then it will fire on Tuesday the 15th.
    /// However if you specify &quot;1W&quot; as the value for day-of-month, and the
    /// 1st is a Saturday, the trigger will fire on Monday the 3rd, as it will not
    /// 'jump' over the boundary of a month's days.  The 'W' character can only be
    /// specified when the day-of-month is a single day, not a range or list of days.
    /// </para>
    /// <para>
    /// The 'L' and 'W' characters can also be combined for the day-of-month
    /// expression to yield 'LW', which translates to &quot;last weekday of the
    /// month&quot;.
    /// </para>
    /// <para>
    /// The '#' character is allowed for the day-of-week field. This character is
    /// used to specify &quot;the nth&quot; XXX day of the month. For example, the
    /// value of &quot;6#3&quot; in the day-of-week field means the third Friday of
    /// the month (day 6 = Friday and &quot;#3&quot; = the 3rd one in the month).
    /// Other examples: &quot;2#1&quot; = the first Monday of the month and
    /// &quot;4#5&quot; = the fifth Wednesday of the month. Note that if you specify
    /// &quot;#5&quot; and there is not 5 of the given day-of-week in the month, then
    /// no firing will occur that month. If the '#' character is used, there can
    /// only be one expression in the day-of-week field (&quot;3#1,6#3&quot; is
    /// not valid, since there are two expressions).
    /// </para>
    /// <para>
    /// <!--The 'C' character is allowed for the day-of-month and day-of-week fields.
    /// This character is short-hand for "calendar". This means values are
    /// calculated against the associated calendar, if any. If no calendar is
    /// associated, then it is equivalent to having an all-inclusive calendar. A
    /// value of "5C" in the day-of-month field means "the first day included by the
    /// calendar on or after the 5th". A value of "1C" in the day-of-week field
    /// means "the first day included by the calendar on or after Sunday". -->
    /// </para>
    /// <para>
    /// The legal characters and the names of months and days of the week are not
    /// case sensitive.
    /// </para>
    /// <para>
    /// <b>NOTES:</b>
    /// <ul>
    /// <li>Support for specifying both a day-of-week and a day-of-month value is
    /// not complete (you'll need to use the '?' character in one of these fields).
    /// </li>
    /// <li>Overflowing ranges is supported - that is, having a larger number on
    /// the left hand side than the right. You might do 22-2 to catch 10 o'clock
    /// at night until 2 o'clock in the morning, or you might have NOV-FEB. It is
    /// very important to note that overuse of overflowing ranges creates ranges
    /// that don't make sense and no effort has been made to determine which
    /// interpretation CronExpression chooses. An example would be
    /// "0 0 14-6 ? * FRI-MON". </li>
    /// </ul>
    /// </para>
    /// </remarks>
    /// <author>Sharada Jambula</author>
    /// <author>James House</author>
    /// <author>Contributions from Mads Henderson</author>
    /// <author>Refactoring from CronTrigger to CronExpression by Aaron Craven</author>
    /// <author>Marko Lahma (.NET)</author>
    [Serializable]
    public class CronExpression : ISerializable
    {
        private static readonly Dictionary<string, int> monthMap = new Dictionary<string, int>(20);
        private static readonly Dictionary<string, int> dayMap = new Dictionary<string, int>(60);

        private TimeZoneInfo? timeZone;

        /// <summary>
        /// Seconds.
        /// </summary>
        [NonSerialized] protected SortedSet<int> seconds = null!;

        /// <summary>
        /// minutes.
        /// </summary>
        [NonSerialized] protected SortedSet<int> minutes = null!;

        /// <summary>
        /// Hours.
        /// </summary>
        [NonSerialized] protected SortedSet<int> hours = null!;

        /// <summary>
        /// Days of month.
        /// </summary>
        [NonSerialized] protected SortedSet<int> daysOfMonth = null!;

        /// <summary>
        /// Months.
        /// </summary>
        [NonSerialized] protected SortedSet<int> months = null!;

        /// <summary>
        /// Days of week.
        /// </summary>
        [NonSerialized] protected SortedSet<int> daysOfWeek = null!;

        /// <summary>
        /// Years.
        /// </summary>
        [NonSerialized] protected SortedSet<int> years = null!;

        /// <summary>
        /// Last day of week.
        /// </summary>
        [NonSerialized] protected bool lastdayOfWeek;

        /// <summary>
        /// N number of weeks.
        /// </summary>
        [NonSerialized] protected int everyNthWeek;

        /// <summary>
        /// Nth day of week.
        /// </summary>
        [NonSerialized] protected int nthdayOfWeek;

        /// <summary>
        /// Last day of month.
        /// </summary>
        [NonSerialized] protected bool lastdayOfMonth;

        /// <summary>
        /// Nearest weekday.
        /// </summary>
        [NonSerialized] protected bool nearestWeekday;

        [NonSerialized] protected int lastdayOffset;
        [NonSerialized] protected int lastWeekdayOffset;

        /// <summary>
        /// Calendar day of week.
        /// </summary>
        [NonSerialized] protected bool calendardayOfWeek;

        /// <summary>
        /// Calendar day of month.
        /// </summary>
        [NonSerialized] protected bool calendardayOfMonth;

        /// <summary>
        /// Expression parsed.
        /// </summary>
        [NonSerialized] protected bool expressionParsed;

        public static readonly int MaxYear = DateTime.Now.Year + 100;

        private static readonly char[] splitSeparators = { ' ', '\t', '\r', '\n' };
        private static readonly char[] commaSeparator = { ',' };

        static CronExpression()
        {
            monthMap.Add("JAN", 0);
            monthMap.Add("FEB", 1);
            monthMap.Add("MAR", 2);
            monthMap.Add("APR", 3);
            monthMap.Add("MAY", 4);
            monthMap.Add("JUN", 5);
            monthMap.Add("JUL", 6);
            monthMap.Add("AUG", 7);
            monthMap.Add("SEP", 8);
            monthMap.Add("OCT", 9);
            monthMap.Add("NOV", 10);
            monthMap.Add("DEC", 11);

            dayMap.Add("SUN", 1);
            dayMap.Add("MON", 2);
            dayMap.Add("TUE", 3);
            dayMap.Add("WED", 4);
            dayMap.Add("THU", 5);
            dayMap.Add("FRI", 6);
            dayMap.Add("SAT", 7);
        }

        ///<summary>
        /// Constructs a new <see cref="CronExpressionString" /> based on the specified
        /// parameter.
        /// </summary>
        /// <param name="cronExpression">
        /// String representation of the cron expression the new object should represent
        /// </param>
        /// <see cref="CronExpressionString" />
        public CronExpression(string cronExpression)
        {
            if (cronExpression == null)
            {
                ThrowHelper.ThrowArgumentException("cronExpression cannot be null", nameof(cronExpression));
            }

            CronExpressionString = CultureInfo.InvariantCulture.TextInfo.ToUpper(cronExpression);
            BuildExpression(CronExpressionString);
        }

        private int GetVersion(SerializationInfo info)
        {
            try
            {
                return info.GetInt32("version");
            }
            catch
            {
                return 0;
            }
        }

        /// <summary>
        /// Serialization constructor.
        /// </summary>
        /// <param name="info"></param>
        /// <param name="context"></param>
        protected CronExpression(SerializationInfo info, StreamingContext context)
        {
            var version = GetVersion(info);
            switch (version)
            {
                case 0:
                    CronExpressionString = info.GetValue<string>("cronExpressionString")!;
                    TimeZone = info.GetValue<TimeZoneInfo>("timeZone")!;
                    break;
                case 1:
                    CronExpressionString = info.GetValue<string>("cronExpression")!;
                    var timeZoneId = info.GetValue<string>("timeZoneId")!;
                    if (!string.IsNullOrEmpty(timeZoneId))
                    {
                        timeZone = TimeZoneUtil.FindTimeZoneById(timeZoneId);
                    }
                    break;
                default:
                    ThrowHelper.ThrowNotSupportedException($"Unknown serialization version {version}");
                    break;
            }
            BuildExpression(CronExpressionString);
        }

        [System.Security.SecurityCritical]
        public void GetObjectData(SerializationInfo info, StreamingContext context)
        {
            info.AddValue("version", 1);
            info.AddValue("cronExpression", CronExpressionString);
            info.AddValue("timeZoneId", TimeZone.Id);
        }

        /// <summary>
        /// Indicates whether the given date satisfies the cron expression.
        /// </summary>
        /// <remarks>
        /// Note that  milliseconds are ignored, so two Dates falling on different milliseconds
        /// of the same second will always have the same result here.
        /// </remarks>
        /// <param name="dateUtc">The date to evaluate.</param>
        /// <returns>a boolean indicating whether the given date satisfies the cron expression</returns>
        public virtual bool IsSatisfiedBy(DateTimeOffset dateUtc)
        {
            var withoutMilliseconds = new DateTimeOffset(dateUtc.Year, dateUtc.Month, dateUtc.Day, dateUtc.Hour, dateUtc.Minute, dateUtc.Second, dateUtc.Offset);
            var test = withoutMilliseconds.AddSeconds(-1);
            var timeAfter = GetTimeAfter(test);

            return timeAfter.HasValue
                   && timeAfter.Value.Equals(withoutMilliseconds);
        }

        /// <summary>
        /// Returns the next date/time <i>after</i> the given date/time which
        /// satisfies the cron expression.
        /// </summary>
        /// <param name="date">the date/time at which to begin the search for the next valid date/time</param>
        /// <returns>the next valid date/time</returns>
        public virtual DateTimeOffset? GetNextValidTimeAfter(DateTimeOffset date)
        {
            return GetTimeAfter(date);
        }

        /// <summary>
        /// Returns the next date/time <i>after</i> the given date/time which does
        /// <i>not</i> satisfy the expression.
        /// </summary>
        /// <param name="date">the date/time at which to begin the search for the next invalid date/time</param>
        /// <returns>the next valid date/time</returns>
        public virtual DateTimeOffset? GetNextInvalidTimeAfter(DateTimeOffset date)
        {
            long difference = 1000;

            // move back to the nearest second so differences will be accurate
            var lastDate = new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, date.Offset).AddSeconds(-1);

            //TODO: IMPROVE THIS! The following is a BAD solution to this problem. Performance will be very bad here, depending on the cron expression. It is, however A solution.

            //keep getting the next included time until it's farther than one second
            // apart. At that point, lastDate is the last valid fire time. We return
            // the second immediately following it.
            while (difference == 1000)
            {
                var newDate = GetTimeAfter(lastDate);

                if (newDate == null)
                {
                    break;
                }

                difference = (long)(newDate.Value - lastDate).TotalMilliseconds;

                if (difference == 1000)
                {
                    lastDate = newDate.Value;
                }
            }

            return lastDate.AddSeconds(1);
        }

        /// <summary>
        /// Sets or gets the time zone for which the <see cref="CronExpression" /> of this
        /// <see cref="ICronTrigger" /> will be resolved.
        /// </summary>
        public TimeZoneInfo TimeZone
        {
            set => timeZone = value;
            get => timeZone ??= TimeZoneInfo.Local;
        }

        /// <summary>
        /// Returns the string representation of the <see cref="CronExpression" />
        /// </summary>
        /// <returns>The string representation of the <see cref="CronExpression" /></returns>
        public override string ToString()
        {
            return CronExpressionString;
        }

        /// <summary>
        /// Indicates whether the specified cron expression can be parsed into a
        /// valid cron expression
        /// </summary>
        /// <param name="cronExpression">the expression to evaluate</param>
        /// <returns>a boolean indicating whether the given expression is a valid cron
        ///         expression</returns>
        public static bool IsValidExpression(string cronExpression)
        {
            try
            {
                new CronExpression(cronExpression);
            }
            catch (FormatException)
            {
                return false;
            }

            return true;
        }

        public static void ValidateExpression(string cronExpression)
        {
            new CronExpression(cronExpression);
        }

        ////////////////////////////////////////////////////////////////////////////
        //
        // Expression Parsing Functions
        //
        ////////////////////////////////////////////////////////////////////////////

        /// <summary>
        /// Builds the expression.
        /// </summary>
        /// <param name="expression">The expression.</param>
        protected void BuildExpression(string expression)
        {
            try
            {
                seconds ??= new();
                minutes ??= new ();
                hours ??= new ();
                daysOfMonth ??= new ();
                months ??= new ();
                daysOfWeek ??= new ();
                years ??= new ();

                var exprOn = CronExpressionConstants.Second;

                foreach (var expr in expression.Split(splitSeparators, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim()))
                {
                    if (exprOn > CronExpressionConstants.Year)
                    {
                        break;
                    }

                    // throw an exception if L is used with other days of the month
                    if (exprOn == CronExpressionConstants.DayOfMonth && expr.IndexOf('L') != -1 && expr.Length > 1 && expr.IndexOf(",", StringComparison.Ordinal) >= 0)
                    {
                        if (expr.Count(f => (f == 'L')) > 1)
                        {
                            ThrowHelper.ThrowFormatException("Support for specifying 'L' with other days of the month is limited to one instance of L");
                        }
                    }
                    // throw an exception if L is used with other days of the week
                    if (exprOn == CronExpressionConstants.DayOfWeek && expr.IndexOf('L') != -1 && expr.Length > 1 && expr.IndexOf(",", StringComparison.Ordinal) >= 0)
                    {
                        ThrowHelper.ThrowFormatException("Support for specifying 'L' with other days of the week is not implemented");
                    }
                    if (exprOn == CronExpressionConstants.DayOfWeek && expr.IndexOf('#') != -1 && expr.IndexOf('#', expr.IndexOf('#') + 1) != -1)
                    {
                        ThrowHelper.ThrowFormatException("Support for specifying multiple \"nth\" days is not implemented.");
                    }

                    foreach (var v in expr.Split(commaSeparator))
                    {
                        StoreExpressionVals(0, v, exprOn);
                    }

                    exprOn++;
                }

                if (exprOn <= CronExpressionConstants.DayOfWeek)
                {
                    ThrowHelper.ThrowFormatException("Unexpected end of expression.");
                }

                if (exprOn <= CronExpressionConstants.Year)
                {
                    StoreExpressionVals(0, "*", CronExpressionConstants.Year);
                }

                var dow = GetSet(CronExpressionConstants.DayOfWeek);
                var dom = GetSet(CronExpressionConstants.DayOfMonth);

                // Copying the logic from the UnsupportedOperationException below
                var dayOfMSpec = !dom.Contains(CronExpressionConstants.NoSpec);
                var dayOfWSpec = !dow.Contains(CronExpressionConstants.NoSpec);

                if ((dayOfMSpec && !dayOfWSpec) || (dayOfWSpec && !dayOfMSpec))
                {
                    // skip
                }
                else
                {
                    ThrowHelper.ThrowFormatException("Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.");
                }
            }
            catch (FormatException)
            {
                throw;
            }
            catch (Exception e)
            {
                ThrowHelper.ThrowFormatException($"Illegal cron expression format ({e.Message})", e);
            }
        }

        private void StoreExpressionQuestionMark(int type, string s, int i)
        {
            i++;
            if (i + 1 <= s.Length && s[i] != ' ' && s[i] != '\t')
            {
                ThrowHelper.ThrowFormatException("Illegal character after '?': " + s[i]);
            }
            if (type != CronExpressionConstants.DayOfWeek && type != CronExpressionConstants.DayOfMonth)
            {
                ThrowHelper.ThrowFormatException(
                    "'?' can only be specified for Day-of-Month or Day-of-Week.");
            }
            if (type == CronExpressionConstants.DayOfWeek && !lastdayOfMonth)
            {
                var val = daysOfMonth.LastOrDefault();
                if (val == CronExpressionConstants.NoSpecInt)
                {
                    ThrowHelper.ThrowFormatException(
                        "'?' can only be specified for Day-of-Month -OR- Day-of-Week.");
                }
            }

            AddToSet(CronExpressionConstants.NoSpecInt, -1, 0, type);
        }

        private void StoreExpressionStarOrSlash(int type, string s, int i)
        {
            var c = s[i];
            var incr = 0;
            var startsWithAsterisk = c == '*';
            if (startsWithAsterisk && i + 1 >= s.Length)
            {
                AddToSet(CronExpressionConstants.AllSpecInt, -1, incr, type);
                return;
            }
            if (c == '/' && (i + 1 >= s.Length || s[i + 1] == ' ' || s[i + 1] == '\t'))
            {
                ThrowHelper.ThrowFormatException("'/' must be followed by an integer.");
            }
            if (startsWithAsterisk)
            {
                i++;
            }
            c = s[i];
            if (c == '/')
            {
                // is an increment specified?
                i++;
                if (i >= s.Length)
                {
                    ThrowHelper.ThrowFormatException("Unexpected end of string.");
                }

                incr = GetNumericValue(s, i);
                CheckIncrementRange(incr, type);
            }
            else
            {
                if (startsWithAsterisk)
                {
                    ThrowHelper.ThrowFormatException("Illegal characters after asterisk: " + s);
                }
                incr = 1;
            }

            AddToSet(CronExpressionConstants.AllSpecInt, -1, incr, type);
        }

        private void StoreExpressionL(int type, string s, int i)
        {
            i++;
            switch (type)
            {
                case CronExpressionConstants.DayOfMonth:
                    {
                        lastdayOfMonth = true;
                        if (s.Length > i)
                        {
                            var c = s[i];
                            if (c == '-')
                            {
                                var vs = GetValue(0, s, i + 1);
                                lastdayOffset = vs.theValue;
                                if (lastdayOffset > 30)
                                {
                                    ThrowHelper.ThrowFormatException("Offset from last day must be <= 30");
                                }
                                i = vs.pos;
                            }
                            if (s.Length > i)
                            {
                                c = s[i];
                                if (c == 'W')
                                {
                                    nearestWeekday = true;
                                }
                                var offsetRegex = new Regex("LW-(?<offset>[0-9]+)",RegexOptions.Compiled);
                                if (offsetRegex.IsMatch(s))
                                {
                                    var offSetGroup = offsetRegex.Match(s).Groups["offset"];
                                    if (offSetGroup.Success)
                                    {
                                        lastWeekdayOffset = int.Parse(offSetGroup.Value);
                                    }
                                }
                            }
                        }
                        break;
                    }

                case CronExpressionConstants.DayOfWeek:
                    AddToSet(7, 7, 0, type);
                    break;
                default:
                    ThrowHelper.ThrowFormatException($"'L' option is not valid here. (pos={i})");
                    break;
            }
        }

        private void StoreExpressionNumeric(int type, string s, int i)
        {
            var c = s[i];
            var val = Convert.ToInt32(c.ToString(), CultureInfo.InvariantCulture);
            i++;
            if (i >= s.Length)
            {
                AddToSet(val, -1, -1, type);
            }
            else
            {
                c = s[i];
                if (c is >= '0' and <= '9')
                {
                    var vs = GetValue(val, s, i);
                    val = vs.theValue;
                    i = vs.pos;
                }
                CheckNext(i, s, val, type);
            }
        }

        private void StoreExpressionGeneralValue(int type, string s, int i)
        {
            var incr = 0;
            var sub = s.Substring(i, 3);
            int sval;
            var eval = -1;
            if (type == CronExpressionConstants.Month)
            {
                sval = GetMonthNumber(sub) + 1;
                if (sval <= 0)
                {
                    ThrowHelper.ThrowFormatException($"Invalid Month value: '{sub}'");
                }
                if (s.Length > i + 3)
                {
                    if (s[i + 3] == '-')
                    {
                        i += 4;
                        sub = s.Substring(i, 3);
                        eval = GetMonthNumber(sub) + 1;
                        if (eval <= 0)
                        {
                            ThrowHelper.ThrowFormatException(
                                $"Invalid Month value: '{sub}'");
                        }
                    }
                }
            }
            else if (type == CronExpressionConstants.DayOfWeek)
            {
                sval = GetDayOfWeekNumber(sub);
                if (sval < 0)
                {
                    ThrowHelper.ThrowFormatException($"Invalid Day-of-Week value: '{sub}'");
                }
                if (s.Length > i + 3)
                {
                    var c = s[i + 3];
                    switch (c)
                    {
                        case '-':
                            i += 4;
                            sub = s.Substring(i, 3);
                            eval = GetDayOfWeekNumber(sub);
                            if (eval < 0)
                            {
                                ThrowHelper.ThrowFormatException(
                                    $"Invalid Day-of-Week value: '{sub}'");
                            }
                            break;
                        case '#':
                            try
                            {
                                i += 4;
                                nthdayOfWeek = Convert.ToInt32(s.Substring(i), CultureInfo.InvariantCulture);
                                if (nthdayOfWeek is < 1 or > 5)
                                {
                                    ThrowHelper.ThrowFormatException("nthdayOfWeek is < 1 or > 5");
                                }
                            }
                            catch (Exception)
                            {
                                ThrowHelper.ThrowFormatException("A numeric value between 1 and 5 must follow the '#' option");
                            }
                            break;
                        case '/':
                            try
                            {
                                i += 4;
                                everyNthWeek = Convert.ToInt32(s.Substring(i), CultureInfo.InvariantCulture);
                                if (everyNthWeek is < 1 or > 5)
                                {
                                    ThrowHelper.ThrowFormatException("everyNthWeek is < 1 or > 5");
                                }
                            }
                            catch (Exception)
                            {
                                ThrowHelper.ThrowFormatException("A numeric value between 1 and 5 must follow the '/' option");
                            }
                            break;
                        case 'L':
                            lastdayOfWeek = true;
                            break;
                        default:
                            ThrowHelper.ThrowFormatException($"Illegal characters for this position: '{sub}'");
                            break;
                    }
                }
            }
            else
            {
                ThrowHelper.ThrowFormatException($"Illegal characters for this position: '{sub}'");
                return;
            }
            if (eval != -1)
            {
                incr = 1;
            }
            AddToSet(sval, eval, incr, type);
        }


        /// <summary>
        /// Stores the expression values.
        /// </summary>
        /// <param name="pos">The position.</param>
        /// <param name="s">The string to traverse.</param>
        /// <param name="type">The type of value.</param>
        protected virtual void StoreExpressionVals(int pos, string s, int type)
        {
            var i = SkipWhiteSpace(pos, s);
            if (i >= s.Length)
            {
                return;
            }

            var regex = new Regex("^L(-\\d{1,2})?(W(-\\d{1,2})?)?$", RegexOptions.Compiled); //e.g. LW L-0W L-4 L-12W LW-4 LW-12

            switch (s[i])
            {
                case >= 'A' and <= 'Z' when !s.Equals("L") && !regex.IsMatch(s):
                    StoreExpressionGeneralValue(type, s, i);
                    break;

                case '?':
                    StoreExpressionQuestionMark(type, s, i);
                    break;

                case '*':
                case '/':
                    StoreExpressionStarOrSlash(type, s, i);
                    break;

                case 'L':
                    StoreExpressionL(type, s, i);
                    break;

                case >= '0' and <= '9':
                    StoreExpressionNumeric(type, s, i);
                    break;
                default:
                    ThrowHelper.ThrowFormatException($"Unexpected character: {s[i]}");
                    break;
            }
        }

        // ReSharper disable once UnusedParameter.Local
        private static void CheckIncrementRange(int incr, int type)
        {
            if (incr > 59 && (type == CronExpressionConstants.Second || type == CronExpressionConstants.Minute))
            {
                ThrowHelper.ThrowFormatException($"Increment > 60 : {incr}");
            }
            if (incr > 23 && type == CronExpressionConstants.Hour)
            {
                ThrowHelper.ThrowFormatException($"Increment > 24 : {incr}");
            }
            if (incr > 31 && type == CronExpressionConstants.DayOfMonth)
            {
                ThrowHelper.ThrowFormatException($"Increment > 31 : {incr}");
            }
            if (incr > 7 && type == CronExpressionConstants.DayOfWeek)
            {
                ThrowHelper.ThrowFormatException($"Increment > 7 : {incr}");
            }
            if (incr > 12 && type == CronExpressionConstants.Month)
            {
                ThrowHelper.ThrowFormatException($"Increment > 12 : {incr}");
            }
        }

        /// <summary>
        /// Checks the next value.
        /// </summary>
        /// <param name="pos">The position.</param>
        /// <param name="s">The string to check.</param>
        /// <param name="val">The value.</param>
        /// <param name="type">The type to search.</param>
        protected virtual void CheckNext(int pos, string s, int val, int type)
        {
            var end = -1;
            var i = pos;

            if (i >= s.Length)
            {
                AddToSet(val, end, -1, type);
                return;
            }

            switch (s[pos])
            {
                case 'L':
                    {
                        if (type == CronExpressionConstants.DayOfWeek)
                        {
                            if (val is < 1 or > 7)
                            {
                                ThrowHelper.ThrowFormatException("Day-of-Week values must be between 1 and 7");
                            }
                            lastdayOfWeek = true;
                        }
                        else
                        {
                            ThrowHelper.ThrowFormatException($"'L' option is not valid here. (pos={i})");
                        }
                        var data = GetSet(type);
                        data.Add(val);
                        return;
                    }

                case 'W':
                    {
                        if (type == CronExpressionConstants.DayOfMonth)
                        {
                            nearestWeekday = true;
                        }
                        else
                        {
                            ThrowHelper.ThrowFormatException($"'W' option is not valid here. (pos={i})");
                        }
                        if (val > 31)
                        {
                            ThrowHelper.ThrowFormatException("The 'W' option does not make sense with values larger than 31 (max number of days in a month)");
                        }

                        var data = GetSet(type);
                        data.Add(val);
                        return;
                    }

                case '#':
                    {
                        if (type != CronExpressionConstants.DayOfWeek)
                        {
                            ThrowHelper.ThrowFormatException($"'#' option is not valid here. (pos={i})");
                        }
                        i++;
                        try
                        {
                            nthdayOfWeek = Convert.ToInt32(s.Substring(i), CultureInfo.InvariantCulture);
                            if (nthdayOfWeek is < 1 or > 5)
                            {
                                ThrowHelper.ThrowFormatException("nthdayOfWeek is < 1 or > 5");
                            }
                            // check first char is numeric and is a valid Day of week (1-7)
                            var dayOfWeek = s.Split('#')[0];
                            var isFirstValueInt = int.TryParse(dayOfWeek, out val);
                            if (isFirstValueInt)
                            {
                                if (val is < 1 or > 7)
                                    ThrowHelper.ThrowFormatException("Day-of-Week values must be between 1 and 7");
                            }
                        }
                        catch (Exception)
                        {
                            ThrowHelper.ThrowFormatException("A numeric value between 1 and 5 must follow the '#' option");
                        }

                        var data = GetSet(type);
                        data.Add(val);
                        return;
                    }

                case 'C':
                    {
                        switch (type)
                        {
                            case CronExpressionConstants.DayOfWeek:
                                calendardayOfWeek = true;
                                break;
                            case CronExpressionConstants.DayOfMonth:
                                calendardayOfMonth = true;
                                break;
                            default:
                                ThrowHelper.ThrowFormatException($"'C' option is not valid here. (pos={i})");
                                break;
                        }
                        var data = GetSet(type);
                        data.Add(val);
                        return;
                    }

                case '-':
                    {
                        i++;
                        var c = s[i];
                        var v = Convert.ToInt32(c.ToString(), CultureInfo.InvariantCulture);
                        end = v;
                        i++;
                        if (i >= s.Length)
                        {
                            AddToSet(val, end, 1, type);
                            return;
                        }
                        c = s[i];
                        if (c is >= '0' and <= '9')
                        {
                            var vs = GetValue(v, s, i);
                            var v1 = vs.theValue;
                            end = v1;
                            i = vs.pos;
                        }
                        if (i < s.Length && s[i] == '/')
                        {
                            i++;
                            c = s[i];
                            var v2 = Convert.ToInt32(c.ToString(), CultureInfo.InvariantCulture);
                            i++;
                            if (i >= s.Length)
                            {
                                AddToSet(val, end, v2, type);
                                return;
                            }
                            c = s[i];
                            if (c is >= '0' and <= '9')
                            {
                                var vs = GetValue(v2, s, i);
                                var v3 = vs.theValue;
                                AddToSet(val, end, v3, type);
                                return;
                            }
                            AddToSet(val, end, v2, type);
                            return;
                        }
                        AddToSet(val, end, 1, type);
                        return;
                    }

                case '/':
                    {
                        if (i + 1 >= s.Length || s[i + 1] == ' ' || s[i + 1] == '\t')
                        {
                            ThrowHelper.ThrowFormatException("\'/\' must be followed by an integer.");
                        }

                        i++;
                        var c = s[i];
                        var v2 = Convert.ToInt32(c.ToString(), CultureInfo.InvariantCulture);
                        i++;
                        if (i >= s.Length)
                        {
                            CheckIncrementRange(v2, type);
                            AddToSet(val, end, v2, type);
                            return;
                        }
                        c = s[i];
                        if (c is >= '0' and <= '9')
                        {
                            var vs = GetValue(v2, s, i);
                            var v3 = vs.theValue;
                            CheckIncrementRange(v3, type);
                            AddToSet(val, end, v3, type);
                            return;
                        }
                        ThrowHelper.ThrowFormatException($"Unexpected character '{c}' after '/'");
                        break;
                    }
            }

            AddToSet(val, end, 0, type);
        }

        /// <summary>
        /// Gets the cron expression string.
        /// </summary>
        /// <value>The cron expression string.</value>
        public string CronExpressionString { get; }


        /// <summary>
        /// Gets the expression summary.
        /// </summary>
        /// <returns></returns>
        public virtual string GetExpressionSummary()
        {
            return new CronExpressionSummary(
                seconds,
                minutes,
                hours,
                daysOfMonth,
                months,
                daysOfWeek,
                lastdayOfWeek,
                nearestWeekday,
                nthdayOfWeek,
                lastdayOfMonth,
                calendardayOfWeek,
                calendardayOfMonth,
                years
            ).ToString();
        }

        /// <summary>
        /// Gets the expression set summary.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns></returns>
        protected virtual string GetExpressionSetSummary(ICollection<int> data)
        {
            if (data.Contains(CronExpressionConstants.NoSpec))
            {
                return "?";
            }
            if (data.Contains(CronExpressionConstants.AllSpec))
            {
                return "*";
            }

            var buf = new StringBuilder();

            var first = true;
            foreach (var iVal in data)
            {
                var val = iVal.ToString(CultureInfo.InvariantCulture);
                if (!first)
                {
                    buf.Append(",");
                }
                buf.Append(val);
                first = false;
            }

            return buf.ToString();
        }

        /// <summary>
        /// Skips the white space.
        /// </summary>
        /// <param name="position">The starting position</param>
        /// <param name="str">The string</param>
        /// <returns></returns>
        protected virtual int SkipWhiteSpace(int position, string str)
        {
            for (; position < str.Length && (str[position] == ' ' || str[position] == '\t'); position++)
            {
            }

            return position;
        }

        /// <summary>
        /// Finds the next white space.
        /// </summary>
        /// <param name="position">The i.</param>
        /// <param name="str">The s.</param>
        /// <returns></returns>
        protected virtual int FindNextWhiteSpace(int position, string str)
        {
            for (; position < str.Length && (str[position] != ' ' || str[position] != '\t'); position++)
            {
            }

            return position;
        }

        /// <summary>
        /// Adds to set.
        /// </summary>
        /// <param name="val">The val.</param>
        /// <param name="end">The end.</param>
        /// <param name="incr">The incr.</param>
        /// <param name="type">The type.</param>
        protected virtual void AddToSet(int val, int end, int incr, int type)
        {
            var data = GetSet(type);

            if (type == CronExpressionConstants.Second || type == CronExpressionConstants.Minute)
            {
                if ((val < 0 || val > 59 || end > 59) && val != CronExpressionConstants.AllSpecInt)
                {
                    ThrowHelper.ThrowFormatException("Minute and CronExpressionConstants.Second values must be between 0 and 59");
                }
            }
            else if (type == CronExpressionConstants.Hour)
            {
                if ((val < 0 || val > 23 || end > 23) && val != CronExpressionConstants.AllSpecInt)
                {
                    ThrowHelper.ThrowFormatException("Hour values must be between 0 and 23");
                }
            }
            else if (type == CronExpressionConstants.DayOfMonth)
            {
                if ((val < 1 || val > 31 || end > 31) && val != CronExpressionConstants.AllSpecInt
                                                      && val != CronExpressionConstants.NoSpecInt)
                {
                    ThrowHelper.ThrowFormatException("Day of month values must be between 1 and 31");
                }
            }
            else if (type == CronExpressionConstants.Month)
            {
                if ((val < 1 || val > 12 || end > 12) && val != CronExpressionConstants.AllSpecInt)
                {
                    ThrowHelper.ThrowFormatException("Month values must be between 1 and 12");
                }
            }
            else if (type == CronExpressionConstants.DayOfWeek)
            {
                if ((val == 0 || val > 7 || end > 7) && val != CronExpressionConstants.AllSpecInt
                                                     && val != CronExpressionConstants.NoSpecInt)
                {
                    ThrowHelper.ThrowFormatException("Day-of-Week values must be between 1 and 7");
                }
            }

            if ((incr == 0 || incr == -1) && val != CronExpressionConstants.AllSpecInt)
            {
                if (val != -1)
                {
                    data.Add(val);
                }
                else
                {
                    data.Add(CronExpressionConstants.NoSpec);
                }
                return;
            }

            var startAt = val;
            var stopAt = end;

            if (val == CronExpressionConstants.AllSpecInt && incr <= 0)
            {
                incr = 1;
                data.Add(CronExpressionConstants.AllSpec); // put in a marker, but also fill values
            }

            if (type == CronExpressionConstants.Second || type == CronExpressionConstants.Minute)
            {
                if (stopAt == -1)
                {
                    stopAt = 59;
                }
                if (startAt == -1 || startAt == CronExpressionConstants.AllSpecInt)
                {
                    startAt = 0;
                }
            }
            else if (type == CronExpressionConstants.Hour)
            {
                if (stopAt == -1)
                {
                    stopAt = 23;
                }
                if (startAt == -1 || startAt == CronExpressionConstants.AllSpecInt)
                {
                    startAt = 0;
                }
            }
            else if (type == CronExpressionConstants.DayOfMonth)
            {
                if (stopAt == -1)
                {
                    stopAt = 31;
                }
                if (startAt == -1 || startAt == CronExpressionConstants.AllSpecInt)
                {
                    startAt = 1;
                }
            }
            else if (type == CronExpressionConstants.Month)
            {
                if (stopAt == -1)
                {
                    stopAt = 12;
                }
                if (startAt == -1 || startAt == CronExpressionConstants.AllSpecInt)
                {
                    startAt = 1;
                }
            }
            else if (type == CronExpressionConstants.DayOfWeek)
            {
                if (stopAt == -1)
                {
                    stopAt = 7;
                }
                if (startAt == -1 || startAt == CronExpressionConstants.AllSpecInt)
                {
                    startAt = 1;
                }
            }
            else if (type == CronExpressionConstants.Year)
            {
                if (stopAt == -1)
                {
                    stopAt = MaxYear;
                }
                if (startAt == -1 || startAt == CronExpressionConstants.AllSpecInt)
                {
                    startAt = 1970;
                }
            }

            // if the end of the range is before the start, then we need to overflow into
            // the next day, month etc. This is done by adding the maximum amount for that
            // type, and using modulus max to determine the value being added.
            var max = -1;
            if (stopAt < startAt)
            {
                switch (type)
                {
                    case CronExpressionConstants.Second:
                        max = 60;
                        break;
                    case CronExpressionConstants.Minute:
                        max = 60;
                        break;
                    case CronExpressionConstants.Hour:
                        max = 24;
                        break;
                    case CronExpressionConstants.Month:
                        max = 12;
                        break;
                    case CronExpressionConstants.DayOfWeek:
                        max = 7;
                        break;
                    case CronExpressionConstants.DayOfMonth:
                        max = 31;
                        break;
                    case CronExpressionConstants.Year:
                        ThrowHelper.ThrowArgumentException("Start year must be less than stop year");
                        break;
                    default:
                        ThrowHelper.ThrowArgumentException("Unexpected type encountered");
                        break;
                }
                stopAt += max;
            }

            for (var i = startAt; i <= stopAt; i += incr)
            {
                if (max == -1)
                {
                    // ie: there's no max to overflow over
                    data.Add(i);
                }
                else
                {
                    // take the modulus to get the real value
                    var i2 = i % max;

                    // 1-indexed ranges should not include 0, and should include their max
                    if (i2 == 0 && (type == CronExpressionConstants.Month 
                                    || type == CronExpressionConstants.DayOfWeek 
                                    || type == CronExpressionConstants.DayOfMonth))
                    {
                        i2 = max;
                    }

                    data.Add(i2);
                }
            }
        }

        /// <summary>
        /// Gets the set of given type.
        /// </summary>
        /// <param name="type">The type of set to get.</param>
        /// <returns></returns>
        protected virtual SortedSet<int> GetSet(int type)
        {
            switch (type)
            {
                case CronExpressionConstants.Second:
                    return seconds;
                case CronExpressionConstants.Minute:
                    return minutes;
                case CronExpressionConstants.Hour:
                    return hours;
                case CronExpressionConstants.DayOfMonth:
                    return daysOfMonth;
                case CronExpressionConstants.Month:
                    return months;
                case CronExpressionConstants.DayOfWeek:
                    return daysOfWeek;
                case CronExpressionConstants.Year:
                    return years;
                default:
                    ThrowHelper.ThrowArgumentOutOfRangeException();
                    return default;
            }
        }

        /// <summary>
        /// Gets the value.
        /// </summary>
        /// <param name="v">The v.</param>
        /// <param name="s">The s.</param>
        /// <param name="i">The i.</param>
        /// <returns></returns>
        protected virtual ValueSet GetValue(int v, string s, int i)
        {
            var c = s[i];
            var s1 = new StringBuilder(v.ToString(CultureInfo.InvariantCulture));
            while (c >= '0' && c <= '9')
            {
                s1.Append(c);
                i++;
                if (i >= s.Length)
                {
                    break;
                }
                c = s[i];
            }
            var val = new ValueSet();
            if (i < s.Length)
            {
                val.pos = i;
            }
            else
            {
                val.pos = i + 1;
            }
            val.theValue = Convert.ToInt32(s1.ToString(), CultureInfo.InvariantCulture);
            return val;
        }

        /// <summary>
        /// Gets the numeric value from string.
        /// </summary>
        /// <param name="s">The string to parse from.</param>
        /// <param name="i">The i.</param>
        /// <returns></returns>
        protected virtual int GetNumericValue(string s, int i)
        {
            var endOfVal = FindNextWhiteSpace(i, s);
            var val = s.Substring(i, endOfVal - i);
            return Convert.ToInt32(val, CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Gets the month number.
        /// </summary>
        /// <param name="s">The string to map with.</param>
        /// <returns></returns>
        protected virtual int GetMonthNumber(string s)
        {
            if (monthMap.ContainsKey(s))
            {
                return monthMap[s];
            }

            return -1;
        }

        /// <summary>
        /// Gets the day of week number.
        /// </summary>
        /// <param name="s">The s.</param>
        /// <returns></returns>
        protected virtual int GetDayOfWeekNumber(string s)
        {
            if (dayMap.ContainsKey(s))
            {
                return dayMap[s];
            }

            return -1;
        }

        /// <summary>
        /// Gets the time from given time parts.
        /// </summary>
        /// <param name="sc">The seconds.</param>
        /// <param name="mn">The minutes.</param>
        /// <param name="hr">The hours.</param>
        /// <param name="dayofmn">The day of month.</param>
        /// <param name="mon">The month.</param>
        /// <returns></returns>
        protected virtual DateTimeOffset? GetTime(int sc, int mn, int hr, int dayofmn, int mon)
        {
            try
            {
                if (sc == -1)
                {
                    sc = 0;
                }
                if (mn == -1)
                {
                    mn = 0;
                }
                if (hr == -1)
                {
                    hr = 0;
                }
                if (dayofmn == -1)
                {
                    dayofmn = 0;
                }
                if (mon == -1)
                {
                    mon = 0;
                }
                return new DateTimeOffset(SystemTime.UtcNow().Year, mon, dayofmn, hr, mn, sc, TimeSpan.Zero);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Progress next fire time seconds
        /// </summary>
        /// <param name="d">NextFireTimeCheck</param>
        private NextFireTimeCursor ProgressNextFireTimeSecond(DateTimeOffset d)
        {
            var sec = d.Second;
            var st = seconds.TailSet(sec);
            if (st.Count > 0)
            {
                sec = st.First();
            }
            else
            {
                sec = seconds.First();
                d = d.AddMinutes(1);
            }

            return new NextFireTimeCursor(false, new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, d.Minute, sec, d.Millisecond, d.Offset));
        }

        /// <summary>
        /// Progress next Fire time Minutes
        /// </summary>
        /// <param name="d">NextFireTimeCheck</param>
        private NextFireTimeCursor ProgressNextFireTimeMinute(DateTimeOffset d)
        {
            var min = d.Minute;
            var hr = d.Hour;
            var t = -1;

            var st = minutes.TailSet(min);
            if (st.Count > 0)
            {
                t = min;
                min = st.First();
            }
            else
            {
                min = minutes.First();
                hr++;
            }

            if (min != t)
            {
                d = new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, min, 0, d.Millisecond, d.Offset);
                d = SetCalendarHour(d, hr);
                return new NextFireTimeCursor(true, d);
            }

            return new NextFireTimeCursor(false, new DateTimeOffset(d.Year, d.Month, d.Day, d.Hour, min, d.Second, d.Millisecond, d.Offset));
        }

        /// <summary>
        /// Progress next fire time Hour
        /// </summary>
        /// <param name="d">NextFireTimeCheck</param>
        private NextFireTimeCursor ProgressNextFireTimeHour(DateTimeOffset d)
        {
            int hour;
            var day = d.Day;
            var t = -1;

            var st = hours.TailSet(d.Hour);
            if (st.Count > 0)
            {
                t = d.Hour;
                hour = st.First();
            }
            else
            {
                hour = hours.First();
                day++;
            }

            if (hour != t)
            {
                var daysInMonth = DateTime.DaysInMonth(d.Year, d.Month);
                if (day > daysInMonth)
                {
                    d = new DateTimeOffset(d.Year, d.Month, daysInMonth, d.Hour, 0, 0, d.Millisecond, d.Offset).AddDays(day - daysInMonth);
                }
                else
                {
                    d = new DateTimeOffset(d.Year, d.Month, day, d.Hour, 0, 0, d.Millisecond, d.Offset);
                }

                d = SetCalendarHour(d, hour);
                return new NextFireTimeCursor(true, d);
            }

            return new NextFireTimeCursor(false, new DateTimeOffset(d.Year, d.Month, d.Day, hour, d.Minute, d.Second, d.Millisecond, d.Offset));
        }

        private SortedSet<int> CalculateDaysOfMonth(DateTimeOffset dt)
        {
            var results = new SortedSet<int>(daysOfMonth);
            if (lastdayOfMonth)
            {
                var lastDayOfMonth = GetLastDayOfMonth(dt.Month, dt.Year);
                var lastDayOfMonthWithOffset = lastDayOfMonth - lastdayOffset;

                if (nearestWeekday)
                {
                    var checkDay = new DateTimeOffset(dt.Year, dt.Month, lastDayOfMonthWithOffset, dt.Hour, dt.Minute, dt.Second, dt.Millisecond, dt.Offset);
                    var calculatedDay= lastDayOfMonthWithOffset;
                    switch (checkDay.DayOfWeek)
                    {
                        case DayOfWeek.Saturday:
                            calculatedDay -= 1;
                            break;
                        case DayOfWeek.Sunday:
                            calculatedDay -= 2;
                            break;
                    }

                    var calculatedLastDayWithOffset = calculatedDay - lastWeekdayOffset;
                    // If the day has crossed to the prior month, reset to 1st.
                    if (calculatedLastDayWithOffset <= 0)
                    {
                        calculatedLastDayWithOffset = 1;
                    }
                        
                    results.Add(calculatedLastDayWithOffset);
                }
                else
                {
                    results.Add(lastDayOfMonthWithOffset);
                }
            }
            else if (nearestWeekday) //AND not lastDay
            {
                var day = daysOfMonth.First();
                var tcal = new DateTimeOffset(dt.Year, dt.Month, day, 0, 0, 0, dt.Offset);
                var lastDayOfMonth = GetLastDayOfMonth(dt.Month, dt.Year);
                var dayOfWeek = tcal.DayOfWeek;

                // evict original date since it has a weekDayModifier
                results.Remove(day);

                switch (dayOfWeek)
                {
                    case DayOfWeek.Saturday when day == 1:
                        day += 2;
                        break;
                    case DayOfWeek.Saturday:
                        day -= 1;
                        break;
                    case DayOfWeek.Sunday when day == lastDayOfMonth:
                        day -= 2;
                        break;
                    case DayOfWeek.Sunday:
                        day += 1;
                        break;
                }

                results.Add(day);
            }

            return results;
        }

        /// <summary>
        /// Progress next fire time day
        /// </summary>
        /// <param name="d">NextFireTimeCheck</param>
        private NextFireTimeCursor ProgressNextFireTimeDay(DateTimeOffset d)
        {
            var day = d.Day;
            var mon = d.Month;
            var t = -1;
            var tmon = mon;

            // get day
            var dayOfMSpec = !daysOfMonth.Contains(CronExpressionConstants.NoSpec);
            var dayOfWSpec = !daysOfWeek.Contains(CronExpressionConstants.NoSpec);
            SortedSet<int> tailDays;
            if (dayOfMSpec && !dayOfWSpec)
            {
                // get day by day of month rule
                var daysOfMonthCalculated = CalculateDaysOfMonth(d);
                tailDays = daysOfMonthCalculated.TailSet(d.Day);
                var found = tailDays.Any();
                if (found)
                {
                    t = day;
                    day = tailDays.First();

                    // make sure we don't over-run a short month, such as february
                    var lastDay = GetLastDayOfMonth(mon, d.Year);
                    if (day > lastDay)
                    {
                        day = daysOfMonthCalculated.First();
                        mon++;
                    }
                }
                else
                {
                    if (lastdayOfMonth)
                        day = daysOfMonthCalculated.First(); //for lastDayOfMonth use calculated fields
                    else
                        day = daysOfMonth.First(); //if not then initial set of days uncalculated (to avoid issue with stale weekday in wrong month value)
                    mon++;
                }

                if (day != t || mon != tmon)
                {
                    if (mon > 12)
                    {
                        d = new DateTimeOffset(d.Year, 12, day, 0, 0, 0, d.Offset).AddMonths(mon - 12);
                    }
                    else
                    {
                        // This is to avoid a bug when moving from a month
                        //with 30 or 31 days to a month with less. Causes an invalid datetime to be instantiated.
                        // ex. 0 29 0 30 1 ? 2009 with clock set to 1/30/2009
                        var lDay = DateTime.DaysInMonth(d.Year, mon);
                        if (day <= lDay)
                        {
                            d = new DateTimeOffset(d.Year, mon, day, 0, 0, 0, d.Offset);
                        }
                        else
                        {
                            d = new DateTimeOffset(d.Year, mon, lDay, 0, 0, 0, d.Offset).AddDays(day - lDay);
                        }
                    }

                    return new NextFireTimeCursor(true, d);
                }
            }
            else if (dayOfWSpec && !dayOfMSpec)
            {
                // get day by day of week rule
                if (lastdayOfWeek)
                {
                    // are we looking for the last XXX day of
                    // the month?
                    var dow = daysOfWeek.First(); // desired
                                                  // d-o-w
                    var cDow = (int)d.DayOfWeek + 1; // current d-o-w
                    var daysToAdd = 0;
                    if (cDow < dow)
                    {
                        daysToAdd = dow - cDow;
                    }

                    if (cDow > dow)
                    {
                        daysToAdd = dow + (7 - cDow);
                    }

                    var lDay = GetLastDayOfMonth(mon, d.Year);

                    if (day + daysToAdd > lDay)
                    {
                        // did we already miss the
                        // last one?
                        if (mon == 12)
                        {
                            //will we pass the end of the year?
                            d = new DateTimeOffset(d.Year, mon - 11, 1, 0, 0, 0, d.Offset).AddYears(1);
                        }
                        else
                        {
                            d = new DateTimeOffset(d.Year, mon + 1, 1, 0, 0, 0, d.Offset);
                        }

                        // we are promoting the month
                        return new NextFireTimeCursor(true, d);
                    }

                    // find date of last occurrence of this day in this month...
                    while (day + daysToAdd + 7 <= lDay)
                    {
                        daysToAdd += 7;
                    }

                    day += daysToAdd;

                    if (daysToAdd > 0)
                    {
                        // we are not promoting the month
                        return new NextFireTimeCursor(true, new DateTimeOffset(d.Year, mon, day, 0, 0, 0, d.Offset));
                    }
                }
                else if (nthdayOfWeek != 0)
                {
                    // are we looking for the Nth XXX day in the month?
                    var dow = daysOfWeek.First(); // desired
                    // d-o-w
                    var cDow = (int)d.DayOfWeek + 1; // current d-o-w
                    var daysToAdd = 0;
                    if (cDow < dow)
                    {
                        daysToAdd = dow - cDow;
                    }
                    else if (cDow > dow)
                    {
                        daysToAdd = dow + (7 - cDow);
                    }

                    var dayShifted = daysToAdd > 0;

                    day += daysToAdd;
                    var weekOfMonth = day / 7;
                    if (day % 7 > 0)
                    {
                        weekOfMonth++;
                    }

                    daysToAdd = (nthdayOfWeek - weekOfMonth) * 7;
                    day += daysToAdd;
                    if (daysToAdd < 0 || day > GetLastDayOfMonth(mon, d.Year))
                    {
                        if (mon == 12)
                        {
                            d = new DateTimeOffset(d.Year, mon - 11, 1, 0, 0, 0, d.Offset).AddYears(1);
                        }
                        else
                        {
                            d = new DateTimeOffset(d.Year, mon + 1, 1, 0, 0, 0, d.Offset);
                        }

                        // we are promoting the month
                        return new NextFireTimeCursor(true, d);
                    }

                    if (daysToAdd > 0 || dayShifted)
                    {
                        // we are NOT promoting the month
                        return new NextFireTimeCursor(true, new DateTimeOffset(d.Year, mon, day, 0, 0, 0, d.Offset));
                    }
                }
                else if (everyNthWeek != 0)
                {
                    var cDow = (int)d.DayOfWeek + 1; // current d-o-w
                    var dow = daysOfWeek.First(); // desired
                                                  // d-o-w
                    tailDays = daysOfWeek.TailSet(cDow);
                    if (tailDays.Count > 0)
                    {
                        dow = tailDays.First();
                    }

                    var daysToAdd = 0;
                    if (cDow < dow)
                    {
                        daysToAdd = (dow - cDow) + (7 * (everyNthWeek - 1));
                    }

                    if (cDow > dow)
                    {
                        daysToAdd = (dow + (7 - cDow)) + (7 * (everyNthWeek - 1));
                    }

                    var lDay = GetLastDayOfMonth(mon, d.Year);

                    if (daysToAdd > 0)
                    {
                        // are we switching days?
                        d = new DateTimeOffset(d.Year, mon, day, 0, 0, 0, d.Offset);
                        d = d.AddDays(daysToAdd);
                        return new NextFireTimeCursor(true, d);
                    }
                }
                else
                {
                    var cDow = (int)d.DayOfWeek + 1; // current d-o-w
                    var dow = daysOfWeek.First(); // desired
                    // d-o-w
                    tailDays = daysOfWeek.TailSet(cDow);
                    if (tailDays.Count > 0)
                    {
                        dow = tailDays.First();
                    }

                    var daysToAdd = 0;
                    if (cDow < dow)
                    {
                        daysToAdd = dow - cDow;
                    }

                    if (cDow > dow)
                    {
                        daysToAdd = dow + (7 - cDow);
                    }

                    var lDay = GetLastDayOfMonth(mon, d.Year);

                    if (day + daysToAdd > lDay)
                    {
                        // will we pass the end of the month?

                        if (mon == 12)
                        {
                            //will we pass the end of the year?
                            d = new DateTimeOffset(d.Year, mon - 11, 1, 0, 0, 0, d.Offset).AddYears(1);
                        }
                        else
                        {
                            d = new DateTimeOffset(d.Year, mon + 1, 1, 0, 0, 0, d.Offset);
                        }

                        // we are promoting the month
                        return new NextFireTimeCursor(true, d);
                    }

                    if (daysToAdd > 0)
                    {
                        // are we switching days?
                        return new NextFireTimeCursor(true, new DateTimeOffset(d.Year, mon, day + daysToAdd, 0, 0, 0, d.Offset));
                    }
                }
            }
            else
            {
                // dayOfWSpec && !dayOfMSpec
                ThrowHelper.ThrowFormatException("Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.");
            }

            return new NextFireTimeCursor(false, new DateTimeOffset(d.Year, d.Month, day, d.Hour, d.Minute, d.Second, d.Offset));
        }

        /// <summary>
        /// Progress next fire time Month
        /// </summary>
        /// <param name="d">NextFireTimeCheck</param>
        private NextFireTimeCursor ProgressNextFireTimeMonth(DateTimeOffset d)
        {
            var mon = d.Month;
            var year = d.Year;
            var t = -1;

            var st = months.TailSet(mon);
            if (st.Count > 0)
            {
                t = mon;
                mon = st.First();
            }
            else
            {
                mon = months.First();
                year++;
            }

            if (mon != t)
            {
                return new NextFireTimeCursor(true, new DateTimeOffset(year, mon, 1, 0, 0, 0, d.Offset));
            }

            return new NextFireTimeCursor(false, new DateTimeOffset(d.Year, mon, d.Day, d.Hour, d.Minute, d.Second, d.Offset));
        }

        /// <summary>
        /// Progress next fire time Year
        /// </summary>
        /// <param name="d"></param>
        private NextFireTimeCursor ProgressNextFireTimeYear(DateTimeOffset d)
        {
            var year = d.Year;
            var st = years.TailSet(d.Year);
            int t;
            if (st.Count > 0)
            {
                t = year;
                year = st.First();
            }
            else
            {
                // ran out of years...
                return new NextFireTimeCursor(false, null);
            }

            if (year != t)
            {
                return new NextFireTimeCursor(true, new DateTimeOffset(year, 1, 1, 0, 0, 0, d.Offset));
            }

            return new NextFireTimeCursor(false, new DateTimeOffset(year, d.Month, d.Day, d.Hour, d.Minute, d.Second, d.Offset));
        }

        /// <summary>
        /// Gets the next fire time after the given time.
        /// </summary>
        /// <param name="afterTimeUtc">The UTC time to start searching from.</param>
        /// <returns></returns>
        public virtual DateTimeOffset? GetTimeAfter(DateTimeOffset afterTimeUtc)
        {
            // move ahead one second, since we're computing the time *after* the
            // given time
            afterTimeUtc = afterTimeUtc.AddSeconds(1);

            // CronTrigger does not deal with milliseconds
            var d = CreateDateTimeWithoutMilliseconds(afterTimeUtc);

            // change to specified time zone
            d = TimeZoneUtil.ConvertTime(d, TimeZone);

            var nextFireTimeProgressors = new List<Func<DateTimeOffset, NextFireTimeCursor>>()
            {
                ProgressNextFireTimeSecond,
                ProgressNextFireTimeMinute,
                ProgressNextFireTimeHour,
                ProgressNextFireTimeDay,
                ProgressNextFireTimeMonth,
                ProgressNextFireTimeYear
            };

            var nextFireTimeCursor = new NextFireTimeCursor(false, d);
            var foundNextFireTime = false;

            // loop until we've computed the next time, or we've past the endTime
            while (!foundNextFireTime)
            {
                foreach (var progressor in nextFireTimeProgressors)
                {
                    if (nextFireTimeCursor.Date.HasValue)
                        nextFireTimeCursor = progressor(nextFireTimeCursor.Date.Value);
                    else
                        break;
                    if (nextFireTimeCursor.RestartLoop)
                        break;
                }

                // test for expressions that never generate a valid fire date,
                if (nextFireTimeCursor.Date == null || nextFireTimeCursor.Date.Value.Year > MaxYear)
                    return null; // ran out of years

                if (nextFireTimeCursor.RestartLoop)
                    continue;

                // apply the proper offset for this date
                d = new DateTimeOffset(nextFireTimeCursor.Date.Value.DateTime, TimeZoneUtil.GetUtcOffset(nextFireTimeCursor.Date.Value.DateTime, TimeZone));
                foundNextFireTime = true;
            }

            return d.ToUniversalTime();
        }

        /// <summary>
        /// Creates the date time without milliseconds.
        /// </summary>
        /// <param name="time">The time.</param>
        /// <returns></returns>
        protected static DateTimeOffset CreateDateTimeWithoutMilliseconds(DateTimeOffset time)
        {
            return new DateTimeOffset(time.Year, time.Month, time.Day, time.Hour, time.Minute, time.Second, time.Offset);
        }

        /// <summary>
        /// Advance the calendar to the particular hour paying particular attention
        /// to daylight saving problems.
        /// </summary>
        /// <param name="date">The date.</param>
        /// <param name="hour">The hour.</param>
        /// <returns></returns>
        protected static DateTimeOffset SetCalendarHour(DateTimeOffset date, int hour)
        {
            // Java version of Quartz uses lenient calendar
            // so hour 24 creates day increment and zeroes hour
            var hourToSet = hour;
            if (hourToSet == 24)
            {
                hourToSet = 0;
            }

            var d = new DateTimeOffset(date.Year, date.Month, date.Day, hourToSet, date.Minute, date.Second, date.Millisecond, date.Offset);
            if (hour == 24)
            {
                // increment day
                d = d.AddDays(1);
            }

            return d;
        }

        /// <summary>
        /// Gets the time before.
        /// </summary>
        /// <param name="endTime">The end time.</param>
        /// <returns></returns>
        public virtual DateTimeOffset? GetTimeBefore(DateTimeOffset? endTime)
        {
            // TODO: implement
            return null;
        }

        /// <summary>
        /// NOT YET IMPLEMENTED: Returns the final time that the
        /// <see cref="CronExpression" /> will match.
        /// </summary>
        /// <returns></returns>
        public virtual DateTimeOffset? GetFinalFireTime()
        {
            // TODO: implement QUARTZ-423
            return null;
        }

        /// <summary>
        /// Determines whether given year is a leap year.
        /// </summary>
        /// <param name="year">The year.</param>
        /// <returns>
        /// 	<c>true</c> if the specified year is a leap year; otherwise, <c>false</c>.
        /// </returns>
        protected virtual bool IsLeapYear(int year)
        {
            return DateTime.IsLeapYear(year);
        }

        /// <summary>
        /// Gets the last day of month.
        /// </summary>
        /// <param name="monthNum">The month num.</param>
        /// <param name="year">The year.</param>
        /// <returns></returns>
        protected virtual int GetLastDayOfMonth(int monthNum, int year)
        {
            return DateTime.DaysInMonth(year, monthNum);
        }

        /// <summary>
        /// Creates a new object that is a copy of the current instance.
        /// </summary>
        /// <returns>
        /// A new object that is a copy of this instance.
        /// </returns>
        public object Clone()
        {
            var copy = new CronExpression(CronExpressionString);
            copy.TimeZone = TimeZone;
            return copy;
        }

        /// <summary>
        /// Determines whether the specified <see cref="CronExpression"/> is equal to the current <see cref="CronExpression"/>.
        /// </summary>
        /// <returns>
        /// true if the specified <see cref="CronExpression"/> is equal to the current <see cref="CronExpression"/>; otherwise, false.
        /// </returns>
        /// <param name="other">The <see cref="CronExpression"/> to compare with the current <see cref="CronExpression"/>. </param>
        public bool Equals(CronExpression other)
        {
            if (ReferenceEquals(null, other)) return false;
            if (ReferenceEquals(this, other)) return true;
            return Equals(other.CronExpressionString, CronExpressionString) && Equals(other.TimeZone, TimeZone);
        }

        /// <summary>
        /// Determines whether the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>.
        /// </summary>
        /// <returns>
        /// true if the specified <see cref="T:System.Object"/> is equal to the current <see cref="T:System.Object"/>; otherwise, false.
        /// </returns>
        /// <param name="obj">The <see cref="T:System.Object"/> to compare with the current <see cref="T:System.Object"/>. </param>
        public override bool Equals(object? obj)
        {
            if (ReferenceEquals(null, obj)) return false;
            if (ReferenceEquals(this, obj)) return true;
            if (obj.GetType() != typeof(CronExpression)) return false;
            return Equals((CronExpression)obj);
        }

        /// <summary>
        /// Serves as a hash function for a particular type.
        /// </summary>
        /// <returns>
        /// A hash code for the current <see cref="T:System.Object"/>.
        /// </returns>
        /// <filterpriority>2</filterpriority>
        public override int GetHashCode()
        {
            unchecked
            {
                return ((CronExpressionString != null ? CronExpressionString.GetHashCode() : 0) * 397) ^ (timeZone != null ? timeZone.GetHashCode() : 0);
            }
        }
    }

    /// <summary>
    /// Helper class for cron expression handling.
    /// </summary>
    public struct ValueSet
    {
        /// <summary>
        /// The value.
        /// </summary>
        public int theValue;

        /// <summary>
        /// The position.
        /// </summary>
        public int pos;
    }

    internal struct NextFireTimeCursor
    {
        public NextFireTimeCursor(bool restartLoop, DateTimeOffset? date)
        {
            RestartLoop = restartLoop;
            Date = date;
        }

        /// <summary>
        /// Indicate if the Next fire date progressor loop should restart
        /// </summary>
        public bool RestartLoop { get; set; }

        /// <summary>
        /// NextFireDate calculated progress result
        /// </summary>
        public DateTimeOffset? Date { get; set; }
    }
}