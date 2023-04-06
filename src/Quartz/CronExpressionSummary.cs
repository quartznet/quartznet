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
using System.Text;

namespace Quartz
{
    internal struct CronExpressionSummary
    {
        public CronExpressionSummary(SortedSet<int> seconds, SortedSet<int> minutes, SortedSet<int> hours, SortedSet<int> daysOfMonth,
            SortedSet<int> months, SortedSet<int> daysOfWeek, bool lastDayOfWeek, bool nearestWeekday, int nthDayOfWeek,
            bool lastDayOfMonth, bool calendarDayOfWeek, bool calendarDayOfMonth, SortedSet<int> years)
        {
            Seconds = seconds;
            Minutes = minutes;
            Hours = hours;
            DaysOfMonth = daysOfMonth;
            Months = months;
            DaysOfWeek = daysOfWeek;
            LastDayOfWeek = lastDayOfWeek;
            NearestWeekday = nearestWeekday;
            NthDayOfWeek = nthDayOfWeek;
            LastDayOfMonth = lastDayOfMonth;
            CalendarDayOfWeek = calendarDayOfWeek;
            CalendarDayOfMonth = calendarDayOfMonth;
            Years = years;
        }

        public SortedSet<int> Seconds { get; set; }
        public SortedSet<int> Minutes { get; set; }
        public SortedSet<int> Hours { get; set; }
        public SortedSet<int> DaysOfMonth { get; set; }
        public SortedSet<int> Months { get; set; }
        public SortedSet<int> DaysOfWeek { get; set; }
        public bool LastDayOfWeek { get; set; }
        public bool NearestWeekday { get; set; }
        public int NthDayOfWeek { get; set; }
        public bool LastDayOfMonth { get; set; }
        public bool CalendarDayOfWeek { get; set; }
        public bool CalendarDayOfMonth { get; set; }
        public SortedSet<int> Years { get; set; }

        /// <summary>
        /// Gets the expression set summary.
        /// </summary>
        /// <param name="data">The data.</param>
        /// <returns></returns>
        private string GetExpressionSetSummary(ICollection<int> data)
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

        public override string ToString()
        {
            var buf = new StringBuilder();

            buf.Append("seconds: ");
            buf.AppendLine(GetExpressionSetSummary(Seconds));
            buf.Append("minutes: ");
            buf.AppendLine(GetExpressionSetSummary(Minutes));
            buf.Append("hours: ");
            buf.AppendLine(GetExpressionSetSummary(Hours));
            buf.Append("daysOfMonth: ");
            buf.AppendLine(GetExpressionSetSummary(DaysOfMonth));
            buf.Append("months: ");
            buf.AppendLine(GetExpressionSetSummary(Months));
            buf.Append("daysOfWeek: ");
            buf.AppendLine(GetExpressionSetSummary(DaysOfWeek));
            buf.Append("lastdayOfWeek: ");
            buf.AppendLine(LastDayOfWeek.ToString());
            buf.Append("nearestWeekday: ");
            buf.AppendLine(NearestWeekday.ToString());
            buf.Append("NthDayOfWeek: ");
            buf.AppendLine(NthDayOfWeek.ToString());
            buf.Append("lastdayOfMonth: ");
            buf.AppendLine(LastDayOfMonth.ToString());
            buf.Append("calendardayOfWeek: ");
            buf.AppendLine(CalendarDayOfWeek.ToString());
            buf.Append("calendardayOfMonth: ");
            buf.AppendLine(CalendarDayOfMonth.ToString());
            buf.Append("years: ");
            buf.AppendLine(GetExpressionSetSummary(Years));
            return buf.ToString();
        }
    }
}