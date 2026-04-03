using System;
using System.Collections.Generic;

namespace Quartz.Impl.Recurrence;

/// <summary>
/// Implements the RFC 5545 BY-rule expansion and limiting logic.
/// Given a recurrence rule and a period start, generates all candidate
/// DateTimes within that period by applying the expand/limit table.
/// </summary>
internal static class ByRuleExpander
{
    /// <summary>
    /// Expand a single period into all candidate DateTimes, sorted ascending.
    /// </summary>
    internal static List<DateTime> ExpandPeriod(RecurrenceRule rule, DateTime dtStart, DateTime periodStart)
    {
        List<DateTime> candidates = new List<DateTime>();

        // Start with the base set for this period
        switch (rule.Frequency)
        {
            case RecurrenceFrequency.Yearly:
                ExpandYearly(rule, dtStart, periodStart, candidates);
                break;
            case RecurrenceFrequency.Monthly:
                ExpandMonthly(rule, dtStart, periodStart, candidates);
                break;
            case RecurrenceFrequency.Weekly:
                ExpandWeekly(rule, dtStart, periodStart, candidates);
                break;
            case RecurrenceFrequency.Daily:
                ExpandDaily(rule, dtStart, periodStart, candidates);
                break;
            case RecurrenceFrequency.Hourly:
                ExpandHourly(rule, dtStart, periodStart, candidates);
                break;
            case RecurrenceFrequency.Minutely:
                ExpandMinutely(rule, dtStart, periodStart, candidates);
                break;
            case RecurrenceFrequency.Secondly:
                ExpandSecondly(rule, dtStart, periodStart, candidates);
                break;
        }

        // Sort and deduplicate to prevent duplicate BY* values (e.g. BYMONTHDAY=1,1)
        // from inflating COUNT or producing wrong BYSETPOS indices.
        candidates.Sort();
        RemoveAdjacentDuplicates(candidates);

        // Apply BYSETPOS if specified
        if (rule.BySetPos != null && candidates.Count > 0)
        {
            HashSet<DateTime> seen = new HashSet<DateTime>();
            List<DateTime> filtered = new List<DateTime>();
            foreach (int pos in rule.BySetPos)
            {
                int idx = pos > 0 ? pos - 1 : candidates.Count + pos;
                if (idx >= 0 && idx < candidates.Count)
                {
                    DateTime val = candidates[idx];
                    if (seen.Add(val))
                    {
                        filtered.Add(val);
                    }
                }
            }
            filtered.Sort();
            return filtered;
        }

        return candidates;
    }

    private static void ExpandYearly(RecurrenceRule rule, DateTime dtStart, DateTime periodStart, List<DateTime> candidates)
    {
        int year = periodStart.Year;

        // YEARLY: start with a set of months, then days, then times
        List<int> months;
        if (rule.ByMonth != null)
        {
            // BYMONTH expands for YEARLY
            months = new List<int>(rule.ByMonth);
        }
        else
        {
            months = new List<int> { dtStart.Month };
        }

        // Apply BYWEEKNO (expands for YEARLY only)
        if (rule.ByWeekNo != null)
        {
            List<DateTime> weekDays = new List<DateTime>();
            foreach (int weekNo in rule.ByWeekNo)
            {
                AddDaysFromWeekNo(year, weekNo, rule.WeekStart, weekDays);
            }

            // If BYDAY is also specified, filter to matching days
            if (rule.ByDay != null)
            {
                weekDays = FilterByDay(weekDays, rule.ByDay);
            }

            List<DateTime> expanded = ExpandTimeComponents(rule, dtStart, weekDays);
            candidates.AddRange(expanded);
            return;
        }

        // Apply BYYEARDAY (expands for YEARLY)
        if (rule.ByYearDay != null)
        {
            List<DateTime> yearDays = new List<DateTime>();
            foreach (int yd in rule.ByYearDay)
            {
                DateTime? dt = GetDayOfYear(year, yd);
                if (dt != null)
                {
                    yearDays.Add(dt.Value);
                }
            }

            List<DateTime> expanded = ExpandTimeComponents(rule, dtStart, yearDays);
            candidates.AddRange(expanded);
            return;
        }

        // For each month, determine which days
        List<DateTime> allDays = new List<DateTime>();
        foreach (int month in months)
        {
            if (month < 1 || month > 12)
            {
                continue;
            }

            List<DateTime> monthDays = GetDaysInMonth(rule, dtStart, year, month);
            allDays.AddRange(monthDays);
        }

        // Expand time components (BYHOUR, BYMINUTE, BYSECOND)
        List<DateTime> withTimes = ExpandTimeComponents(rule, dtStart, allDays);
        candidates.AddRange(withTimes);
    }

    private static void ExpandMonthly(RecurrenceRule rule, DateTime dtStart, DateTime periodStart, List<DateTime> candidates)
    {
        int year = periodStart.Year;
        int month = periodStart.Month;

        // BYMONTH limits for MONTHLY
        if (rule.ByMonth != null && Array.IndexOf(rule.ByMonth, month) < 0)
        {
            return;
        }

        List<DateTime> days = GetDaysInMonth(rule, dtStart, year, month);
        List<DateTime> withTimes = ExpandTimeComponents(rule, dtStart, days);
        candidates.AddRange(withTimes);
    }

    private static void ExpandWeekly(RecurrenceRule rule, DateTime dtStart, DateTime periodStart, List<DateTime> candidates)
    {
        // WEEKLY per RFC 5545 Section 3.3.10:
        //   BYDAY: Expand    BYMONTH: Limit    BYHOUR/BYMINUTE/BYSECOND: Expand
        //   BYMONTHDAY, BYYEARDAY, BYWEEKNO: N/A (ignored if present)
        List<DateTime> days = new List<DateTime>();

        if (rule.ByDay != null)
        {
            // Expand: generate each matching day within this week
            for (int d = 0; d < 7; d++)
            {
                DateTime day = periodStart.Date.AddDays(d);
                foreach ((DayOfWeek dow, int _) in rule.ByDay)
                {
                    if (day.DayOfWeek == dow)
                    {
                        days.Add(day);
                        break;
                    }
                }
            }
        }
        else
        {
            // Default: same day of week as dtStart
            for (int d = 0; d < 7; d++)
            {
                DateTime day = periodStart.Date.AddDays(d);
                if (day.DayOfWeek == dtStart.DayOfWeek)
                {
                    days.Add(day);
                    break;
                }
            }
        }

        // BYMONTH limits
        if (rule.ByMonth != null)
        {
            days = FilterByMonth(days, rule.ByMonth);
        }

        List<DateTime> withTimes = ExpandTimeComponents(rule, dtStart, days);
        candidates.AddRange(withTimes);
    }

    private static void ExpandDaily(RecurrenceRule rule, DateTime dtStart, DateTime periodStart, List<DateTime> candidates)
    {
        DateTime day = periodStart.Date;

        // BYMONTH limits
        if (rule.ByMonth != null && Array.IndexOf(rule.ByMonth, day.Month) < 0)
        {
            return;
        }

        // BYMONTHDAY limits
        if (rule.ByMonthDay != null)
        {
            bool match = false;
            int daysInMonth = DateTime.DaysInMonth(day.Year, day.Month);
            foreach (int md in rule.ByMonthDay)
            {
                int actualDay = md > 0 ? md : daysInMonth + md + 1;
                if (actualDay == day.Day)
                {
                    match = true;
                    break;
                }
            }
            if (!match)
            {
                return;
            }
        }

        // BYDAY limits
        if (rule.ByDay != null)
        {
            bool match = false;
            foreach ((DayOfWeek dow, int _) in rule.ByDay)
            {
                if (day.DayOfWeek == dow)
                {
                    match = true;
                    break;
                }
            }
            if (!match)
            {
                return;
            }
        }

        // BYHOUR expands for DAILY
        List<DateTime> days = new List<DateTime> { day };
        List<DateTime> withTimes = ExpandTimeComponents(rule, dtStart, days);
        candidates.AddRange(withTimes);
    }

    private static void ExpandHourly(RecurrenceRule rule, DateTime dtStart, DateTime periodStart, List<DateTime> candidates)
    {
        DateTime point = periodStart;

        // BYMONTH limits
        if (rule.ByMonth != null && Array.IndexOf(rule.ByMonth, point.Month) < 0)
        {
            return;
        }

        // BYMONTHDAY limits
        if (rule.ByMonthDay != null)
        {
            bool match = false;
            int daysInMonth = DateTime.DaysInMonth(point.Year, point.Month);
            foreach (int md in rule.ByMonthDay)
            {
                int actualDay = md > 0 ? md : daysInMonth + md + 1;
                if (actualDay == point.Day)
                {
                    match = true;
                    break;
                }
            }
            if (!match)
            {
                return;
            }
        }

        // BYDAY limits
        if (rule.ByDay != null)
        {
            bool match = false;
            foreach ((DayOfWeek dow, int _) in rule.ByDay)
            {
                if (point.DayOfWeek == dow)
                {
                    match = true;
                    break;
                }
            }
            if (!match)
            {
                return;
            }
        }

        // BYHOUR limits
        if (rule.ByHour != null && Array.IndexOf(rule.ByHour, point.Hour) < 0)
        {
            return;
        }

        // BYMINUTE expands for HOURLY
        int[] minutes = rule.ByMinute ?? new[] { dtStart.Minute };
        int[] seconds = rule.BySecond ?? new[] { dtStart.Second };

        foreach (int min in minutes)
        {
            foreach (int sec in seconds)
            {
                try
                {
                    DateTime candidate = new DateTime(point.Year, point.Month, point.Day, point.Hour, min, sec);
                    candidates.Add(candidate);
                }
                catch (ArgumentOutOfRangeException)
                {
                    // Skip invalid times
                }
            }
        }
    }

    private static void ExpandMinutely(RecurrenceRule rule, DateTime dtStart, DateTime periodStart, List<DateTime> candidates)
    {
        DateTime point = periodStart;

        // Apply limiting rules
        if (rule.ByMonth != null && Array.IndexOf(rule.ByMonth, point.Month) < 0) return;
        if (rule.ByDay != null && !MatchesByDay(point, rule.ByDay)) return;
        if (rule.ByHour != null && Array.IndexOf(rule.ByHour, point.Hour) < 0) return;
        if (rule.ByMinute != null && Array.IndexOf(rule.ByMinute, point.Minute) < 0) return;

        if (rule.ByMonthDay != null && !MatchesByMonthDay(point, rule.ByMonthDay)) return;

        // BYSECOND expands for MINUTELY
        int[] seconds = rule.BySecond ?? new[] { dtStart.Second };

        foreach (int sec in seconds)
        {
            try
            {
                DateTime candidate = new DateTime(point.Year, point.Month, point.Day, point.Hour, point.Minute, sec);
                candidates.Add(candidate);
            }
            catch (ArgumentOutOfRangeException)
            {
                // Skip invalid times
            }
        }
    }

    private static void ExpandSecondly(RecurrenceRule rule, DateTime dtStart, DateTime periodStart, List<DateTime> candidates)
    {
        DateTime point = periodStart;

        // All BY* rules limit for SECONDLY
        if (rule.ByMonth != null && Array.IndexOf(rule.ByMonth, point.Month) < 0) return;
        if (rule.ByDay != null && !MatchesByDay(point, rule.ByDay)) return;
        if (rule.ByHour != null && Array.IndexOf(rule.ByHour, point.Hour) < 0) return;
        if (rule.ByMinute != null && Array.IndexOf(rule.ByMinute, point.Minute) < 0) return;
        if (rule.BySecond != null && Array.IndexOf(rule.BySecond, point.Second) < 0) return;
        if (rule.ByMonthDay != null && !MatchesByMonthDay(point, rule.ByMonthDay)) return;

        candidates.Add(point);
    }

    /// <summary>
    /// Get the days within a month based on BYMONTHDAY and/or BYDAY rules.
    /// Used by YEARLY and MONTHLY frequencies.
    /// </summary>
    private static List<DateTime> GetDaysInMonth(RecurrenceRule rule, DateTime dtStart, int year, int month)
    {
        int daysInMonth;
        try
        {
            daysInMonth = DateTime.DaysInMonth(year, month);
        }
        catch (ArgumentOutOfRangeException)
        {
            return new List<DateTime>();
        }

        List<DateTime> result = new List<DateTime>();

        bool hasByMonthDay = rule.ByMonthDay != null;
        bool hasByDay = rule.ByDay != null;

        if (hasByMonthDay && hasByDay)
        {
            // Both specified: intersection
            // First expand by BYMONTHDAY
            HashSet<int> monthDaySet = new HashSet<int>();
            foreach (int md in rule.ByMonthDay!)
            {
                int actual = md > 0 ? md : daysInMonth + md + 1;
                if (actual >= 1 && actual <= daysInMonth)
                {
                    monthDaySet.Add(actual);
                }
            }

            // Then filter by BYDAY
            foreach (int day in monthDaySet)
            {
                DateTime dt = new DateTime(year, month, day);
                if (MatchesByDay(dt, rule.ByDay!))
                {
                    result.Add(dt);
                }
            }
        }
        else if (hasByMonthDay)
        {
            foreach (int md in rule.ByMonthDay!)
            {
                int actual = md > 0 ? md : daysInMonth + md + 1;
                if (actual >= 1 && actual <= daysInMonth)
                {
                    result.Add(new DateTime(year, month, actual));
                }
            }
        }
        else if (hasByDay)
        {
            ExpandByDayInMonth(year, month, rule.ByDay!, result);
        }
        else
        {
            // Default: same day as dtStart (capped at days in month)
            int day = Math.Min(dtStart.Day, daysInMonth);
            result.Add(new DateTime(year, month, day));
        }

        return result;
    }

    /// <summary>
    /// Expand BYDAY entries within a specific month.
    /// Handles both simple (MO) and ordinal (2MO, -1FR) forms.
    /// </summary>
    private static void ExpandByDayInMonth(int year, int month, (DayOfWeek Day, int Ordinal)[] byDay, List<DateTime> result)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);

        foreach ((DayOfWeek dow, int ordinal) in byDay)
        {
            if (ordinal == 0)
            {
                // No ordinal: every occurrence of this day in the month
                for (int d = 1; d <= daysInMonth; d++)
                {
                    DateTime dt = new DateTime(year, month, d);
                    if (dt.DayOfWeek == dow)
                    {
                        result.Add(dt);
                    }
                }
            }
            else
            {
                DateTime? dt = GetNthDayOfWeekInMonth(year, month, dow, ordinal);
                if (dt != null)
                {
                    result.Add(dt.Value);
                }
            }
        }
    }

    /// <summary>
    /// Get the Nth occurrence of a day of week in a month.
    /// Positive ordinal counts from start, negative from end.
    /// </summary>
    private static DateTime? GetNthDayOfWeekInMonth(int year, int month, DayOfWeek dow, int ordinal)
    {
        int daysInMonth = DateTime.DaysInMonth(year, month);

        if (ordinal > 0)
        {
            int count = 0;
            for (int d = 1; d <= daysInMonth; d++)
            {
                DateTime dt = new DateTime(year, month, d);
                if (dt.DayOfWeek == dow)
                {
                    count++;
                    if (count == ordinal)
                    {
                        return dt;
                    }
                }
            }
        }
        else
        {
            // Negative: count from end
            int count = 0;
            int target = -ordinal;
            for (int d = daysInMonth; d >= 1; d--)
            {
                DateTime dt = new DateTime(year, month, d);
                if (dt.DayOfWeek == dow)
                {
                    count++;
                    if (count == target)
                    {
                        return dt;
                    }
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Expand time components (BYHOUR, BYMINUTE, BYSECOND) on a set of day-level candidates.
    /// </summary>
    private static List<DateTime> ExpandTimeComponents(RecurrenceRule rule, DateTime dtStart, List<DateTime> days)
    {
        RecurrenceFrequency freq = rule.Frequency;

        // Determine which time rules expand vs use default
        // BYHOUR expands for DAILY and coarser
        // BYMINUTE expands for HOURLY and coarser
        // BYSECOND expands for MINUTELY and coarser
        int[] hours = (rule.ByHour != null && freq >= RecurrenceFrequency.Daily)
            ? rule.ByHour
            : new[] { dtStart.Hour };

        int[] minutes = (rule.ByMinute != null && freq >= RecurrenceFrequency.Hourly)
            ? rule.ByMinute
            : new[] { dtStart.Minute };

        int[] seconds = (rule.BySecond != null && freq >= RecurrenceFrequency.Minutely)
            ? rule.BySecond
            : new[] { dtStart.Second };

        List<DateTime> result = new List<DateTime>();

        foreach (DateTime day in days)
        {
            foreach (int h in hours)
            {
                foreach (int m in minutes)
                {
                    foreach (int s in seconds)
                    {
                        try
                        {
                            DateTime candidate = new DateTime(day.Year, day.Month, day.Day, h, m, s);
                            result.Add(candidate);
                        }
                        catch (ArgumentOutOfRangeException)
                        {
                            // Skip invalid times
                        }
                    }
                }
            }
        }

        return result;
    }

    private static void AddDaysFromWeekNo(int year, int weekNo, DayOfWeek weekStart, List<DateTime> result)
    {
        // ISO 8601: Week 1 is the week containing the first Thursday of the year
        // Adjust for different WKST values
        DateTime jan1 = new DateTime(year, 1, 1);

        // Find the first day of week 1
        int jan1DowOffset = ((int)jan1.DayOfWeek - (int)weekStart + 7) % 7;
        DateTime firstWeekStart = jan1.AddDays(-jan1DowOffset);

        // Check if this week contains at least 4 days of the new year
        DateTime firstThursday = firstWeekStart.AddDays(((int)DayOfWeek.Thursday - (int)weekStart + 7) % 7);
        if (firstThursday.Year < year)
        {
            firstWeekStart = firstWeekStart.AddDays(7);
        }

        int actualWeek = weekNo;
        if (weekNo < 0)
        {
            // Count from end of year: get total weeks
            DateTime dec31 = new DateTime(year, 12, 31);
            int dec31DowOffset = ((int)dec31.DayOfWeek - (int)weekStart + 7) % 7;
            DateTime lastWeekStart = dec31.AddDays(-dec31DowOffset);
            int totalWeeks = (int)((lastWeekStart - firstWeekStart).TotalDays / 7) + 1;
            actualWeek = totalWeeks + weekNo + 1;
        }

        if (actualWeek < 1)
        {
            return;
        }

        DateTime weekStartDate = firstWeekStart.AddDays((actualWeek - 1) * 7);
        for (int d = 0; d < 7; d++)
        {
            DateTime day = weekStartDate.AddDays(d);
            if (day.Year == year)
            {
                result.Add(day);
            }
        }
    }

    private static DateTime? GetDayOfYear(int year, int yearDay)
    {
        int daysInYear = DateTime.IsLeapYear(year) ? 366 : 365;
        int actual = yearDay > 0 ? yearDay : daysInYear + yearDay + 1;
        if (actual < 1 || actual > daysInYear)
        {
            return null;
        }
        return new DateTime(year, 1, 1).AddDays(actual - 1);
    }

    private static List<DateTime> FilterByDay(List<DateTime> days, (DayOfWeek Day, int Ordinal)[] byDay)
    {
        List<DateTime> result = new List<DateTime>();
        foreach (DateTime day in days)
        {
            foreach ((DayOfWeek dow, int _) in byDay)
            {
                if (day.DayOfWeek == dow)
                {
                    result.Add(day);
                    break;
                }
            }
        }
        return result;
    }

    private static List<DateTime> FilterByMonth(List<DateTime> days, int[] months)
    {
        List<DateTime> result = new List<DateTime>();
        foreach (DateTime day in days)
        {
            if (Array.IndexOf(months, day.Month) >= 0)
            {
                result.Add(day);
            }
        }
        return result;
    }

    /// <summary>
    /// Check if the given date's day-of-week matches any entry in the BYDAY array
    /// (ignoring ordinals). Used by RecurrenceRule for fast-forward/rewind skipping.
    /// </summary>
    internal static bool MatchesByDayOfWeek(DateTime dt, (DayOfWeek Day, int Ordinal)[] byDay)
    {
        return MatchesByDay(dt, byDay);
    }

    /// <summary>
    /// Check if the given date's day-of-month matches any entry in the BYMONTHDAY array.
    /// Used by RecurrenceRule for fast-forward/rewind skipping.
    /// </summary>
    internal static bool MatchesByMonthDayValue(DateTime dt, int[] byMonthDay)
    {
        return MatchesByMonthDay(dt, byMonthDay);
    }

    private static bool MatchesByDay(DateTime dt, (DayOfWeek Day, int Ordinal)[] byDay)
    {
        foreach ((DayOfWeek dow, int _) in byDay)
        {
            if (dt.DayOfWeek == dow)
            {
                return true;
            }
        }
        return false;
    }

    private static bool MatchesByMonthDay(DateTime dt, int[] byMonthDay)
    {
        int daysInMonth = DateTime.DaysInMonth(dt.Year, dt.Month);
        foreach (int md in byMonthDay)
        {
            int actual = md > 0 ? md : daysInMonth + md + 1;
            if (actual == dt.Day)
            {
                return true;
            }
        }
        return false;
    }

    /// <summary>
    /// Remove adjacent duplicate values from an already-sorted list in place.
    /// </summary>
    private static void RemoveAdjacentDuplicates(List<DateTime> sorted)
    {
        int write = 0;
        for (int read = 0; read < sorted.Count; read++)
        {
            if (read == 0 || sorted[read] != sorted[write - 1])
            {
                sorted[write] = sorted[read];
                write++;
            }
        }
        if (write < sorted.Count)
        {
            sorted.RemoveRange(write, sorted.Count - write);
        }
    }
}
