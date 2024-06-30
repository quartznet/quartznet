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

namespace Quartz;

internal readonly struct CronExpressionSummary
{
    public CronExpressionSummary(CronField seconds, CronField minutes, CronField hours, CronField daysOfMonth,
        CronField months, CronField daysOfWeek, bool lastDayOfWeek, bool nearestWeekday, int nthDayOfWeek,
        bool lastDayOfMonth, bool calendarDayOfWeek, bool calendarDayOfMonth, CronField years)
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

    public CronField Seconds { get; }
    public CronField Minutes { get; }
    public CronField Hours { get; }
    public CronField DaysOfMonth { get; }
    public CronField Months { get; }
    public CronField DaysOfWeek { get; }
    public bool LastDayOfWeek { get; }
    public bool NearestWeekday { get; }
    public int NthDayOfWeek { get; }
    public bool LastDayOfMonth { get; }
    public bool CalendarDayOfWeek { get; }
    public bool CalendarDayOfMonth { get; }
    public CronField Years { get; }

    /// <summary>
    /// Gets the expression set summary.
    /// </summary>
    private static string GetExpressionSetSummary(CronField data)
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
                buf.Append(',');
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