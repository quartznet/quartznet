using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Quartz.Impl.Recurrence;

/// <summary>
/// Parses and evaluates RFC 5545 RRULE recurrence rules.
/// Thread-safe after construction (all properties are readonly).
/// </summary>
internal sealed class RecurrenceRule
{
    private const int MaxIterations = 1000;

    private static readonly Dictionary<string, DayOfWeek> DayMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["MO"] = DayOfWeek.Monday,
        ["TU"] = DayOfWeek.Tuesday,
        ["WE"] = DayOfWeek.Wednesday,
        ["TH"] = DayOfWeek.Thursday,
        ["FR"] = DayOfWeek.Friday,
        ["SA"] = DayOfWeek.Saturday,
        ["SU"] = DayOfWeek.Sunday
    };

    private static readonly Dictionary<DayOfWeek, string> ReverseDayMap = new()
    {
        [DayOfWeek.Monday] = "MO",
        [DayOfWeek.Tuesday] = "TU",
        [DayOfWeek.Wednesday] = "WE",
        [DayOfWeek.Thursday] = "TH",
        [DayOfWeek.Friday] = "FR",
        [DayOfWeek.Saturday] = "SA",
        [DayOfWeek.Sunday] = "SU"
    };

    internal RecurrenceFrequency Frequency { get; }
    internal int Interval { get; }
    internal int? Count { get; }
    internal DateTime? Until { get; }
    internal bool UntilIsUtc { get; }
    internal DayOfWeek WeekStart { get; }

    internal int[]? ByMonth { get; }
    internal int[]? ByWeekNo { get; }
    internal int[]? ByYearDay { get; }
    internal int[]? ByMonthDay { get; }
    internal (DayOfWeek Day, int Ordinal)[]? ByDay { get; }
    internal int[]? ByHour { get; }
    internal int[]? ByMinute { get; }
    internal int[]? BySecond { get; }
    internal int[]? BySetPos { get; }

    private RecurrenceRule(
        RecurrenceFrequency frequency,
        int interval,
        int? count,
        DateTime? until,
        bool untilIsUtc,
        DayOfWeek weekStart,
        int[]? byMonth,
        int[]? byWeekNo,
        int[]? byYearDay,
        int[]? byMonthDay,
        (DayOfWeek Day, int Ordinal)[]? byDay,
        int[]? byHour,
        int[]? byMinute,
        int[]? bySecond,
        int[]? bySetPos)
    {
        Frequency = frequency;
        Interval = interval;
        Count = count;
        Until = until;
        UntilIsUtc = untilIsUtc;
        WeekStart = weekStart;
        ByMonth = byMonth;
        ByWeekNo = byWeekNo;
        ByYearDay = byYearDay;
        ByMonthDay = byMonthDay;
        ByDay = byDay;
        ByHour = byHour;
        ByMinute = byMinute;
        BySecond = bySecond;
        BySetPos = bySetPos;
    }

    /// <summary>
    /// Parse an RFC 5545 RRULE string. The "RRULE:" prefix is optional.
    /// </summary>
    internal static RecurrenceRule Parse(string rrule)
    {
        if (string.IsNullOrWhiteSpace(rrule))
        {
            throw new FormatException("RRULE string cannot be null or empty.");
        }

        // Strip optional prefix
        string input = rrule.Trim();
        if (input.StartsWith("RRULE:", StringComparison.OrdinalIgnoreCase))
        {
            input = input.Substring(6);
        }

        RecurrenceFrequency? frequency = null;
        int interval = 1;
        int? count = null;
        DateTime? until = null;
        bool untilIsUtc = false;
        DayOfWeek weekStart = DayOfWeek.Monday;
        int[]? byMonth = null;
        int[]? byWeekNo = null;
        int[]? byYearDay = null;
        int[]? byMonthDay = null;
        (DayOfWeek Day, int Ordinal)[]? byDay = null;
        int[]? byHour = null;
        int[]? byMinute = null;
        int[]? bySecond = null;
        int[]? bySetPos = null;

        string[] parts = input.Split(';');

        foreach (string part in parts)
        {
            if (string.IsNullOrEmpty(part))
            {
                continue;
            }

            int eqIdx = part.IndexOf('=');
            if (eqIdx < 0)
            {
                throw new FormatException($"Invalid RRULE part: '{part}'");
            }

            string name = part.Substring(0, eqIdx).Trim().ToUpperInvariant();
            string value = part.Substring(eqIdx + 1).Trim();

            switch (name)
            {
                case "FREQ":
                    frequency = ParseFrequency(value);
                    break;
                case "INTERVAL":
                    interval = int.Parse(value, CultureInfo.InvariantCulture);
                    if (interval < 1)
                    {
                        throw new FormatException("INTERVAL must be >= 1.");
                    }
                    break;
                case "COUNT":
                    count = int.Parse(value, CultureInfo.InvariantCulture);
                    if (count < 1)
                    {
                        throw new FormatException("COUNT must be >= 1.");
                    }
                    break;
                case "UNTIL":
                    (until, untilIsUtc) = ParseUntil(value);
                    break;
                case "WKST":
                    weekStart = ParseDay(value);
                    break;
                case "BYDAY":
                    byDay = ParseByDay(value);
                    break;
                case "BYMONTHDAY":
                    byMonthDay = ParseIntList(value);
                    break;
                case "BYMONTH":
                    byMonth = ParseIntList(value);
                    break;
                case "BYYEARDAY":
                    byYearDay = ParseIntList(value);
                    break;
                case "BYWEEKNO":
                    byWeekNo = ParseIntList(value);
                    break;
                case "BYHOUR":
                    byHour = ParseIntList(value);
                    break;
                case "BYMINUTE":
                    byMinute = ParseIntList(value);
                    break;
                case "BYSECOND":
                    bySecond = ParseIntList(value);
                    break;
                case "BYSETPOS":
                    bySetPos = ParseIntList(value);
                    break;
                default:
                    throw new FormatException($"Unknown RRULE property: '{name}'");
            }
        }

        if (frequency == null)
        {
            throw new FormatException("RRULE must contain a FREQ property.");
        }

        if (count != null && until != null)
        {
            throw new FormatException("RRULE must not contain both COUNT and UNTIL.");
        }

        return new RecurrenceRule(
            frequency.Value, interval, count, until, untilIsUtc, weekStart,
            byMonth, byWeekNo, byYearDay, byMonthDay, byDay,
            byHour, byMinute, bySecond, bySetPos);
    }

    /// <summary>
    /// Returns the first occurrence of the recurrence strictly after <paramref name="after"/>,
    /// where DTSTART is <paramref name="dtStart"/>. Returns null if no further occurrences exist.
    /// </summary>
    internal DateTimeOffset? GetNextOccurrence(
        DateTimeOffset dtStart,
        DateTimeOffset after,
        TimeZoneInfo? timeZone,
        DateTimeOffset? endTime)
    {
        TimeZoneInfo tz = timeZone ?? TimeZoneInfo.Utc;

        // Convert to wall-clock time for calculations
        DateTime localStart = TimeZoneInfo.ConvertTime(dtStart, tz).DateTime;
        DateTime localAfter = TimeZoneInfo.ConvertTime(after, tz).DateTime;

        // Check UNTIL bound (convert to local if it was UTC)
        DateTime? localUntil = null;
        if (Until != null)
        {
            localUntil = UntilIsUtc
                ? TimeZoneInfo.ConvertTimeFromUtc(Until.Value, tz)
                : Until.Value;
        }

        DateTime? localEnd = null;
        if (endTime != null)
        {
            localEnd = TimeZoneInfo.ConvertTime(endTime.Value, tz).DateTime;
        }

        DateTime? result = FindNextOccurrence(localStart, localAfter, localUntil, localEnd);

        if (result == null)
        {
            return null;
        }

        return ToDateTimeOffset(result.Value, tz);
    }

    private DateTime? FindNextOccurrence(DateTime localStart, DateTime localAfter, DateTime? localUntil, DateTime? localEnd)
    {
        // If COUNT is specified, we must walk forward from the start counting occurrences
        if (Count != null)
        {
            return FindNextOccurrenceWithCount(localStart, localAfter, localUntil, localEnd);
        }

        // For non-COUNT rules, we can jump to the approximate period
        return FindNextOccurrenceNonCount(localStart, localAfter, localUntil, localEnd);
    }

    private DateTime? FindNextOccurrenceNonCount(DateTime localStart, DateTime localAfter, DateTime? localUntil, DateTime? localEnd)
    {
        // Find the period index that contains localAfter (or just before it)
        int startPeriodIdx = GetPeriodIndex(localStart, localAfter);
        if (startPeriodIdx < 0)
        {
            startPeriodIdx = 0;
        }

        for (int i = startPeriodIdx; i < startPeriodIdx + MaxIterations; i++)
        {
            DateTime periodStart = GetPeriodStart(localStart, i);

            // If period start is past bounds, stop
            if (localUntil != null && periodStart > localUntil.Value)
            {
                return null;
            }
            if (localEnd != null && periodStart > localEnd.Value)
            {
                return null;
            }

            List<DateTime> candidates = ByRuleExpander.ExpandPeriod(this, localStart, periodStart);

            foreach (DateTime candidate in candidates)
            {
                if (candidate < localStart)
                {
                    continue;
                }
                if (candidate <= localAfter)
                {
                    continue;
                }
                if (localUntil != null && candidate > localUntil.Value)
                {
                    return null;
                }
                if (localEnd != null && candidate > localEnd.Value)
                {
                    return null;
                }
                return candidate;
            }
        }

        return null;
    }

    private DateTime? FindNextOccurrenceWithCount(DateTime localStart, DateTime localAfter, DateTime? localUntil, DateTime? localEnd)
    {
        int occurrenceCount = 0;
        int countLimit = Count!.Value;

        for (int i = 0; i < MaxIterations; i++)
        {
            DateTime periodStart = GetPeriodStart(localStart, i);

            if (localUntil != null && periodStart > localUntil.Value)
            {
                return null;
            }
            if (localEnd != null && periodStart > localEnd.Value)
            {
                return null;
            }

            List<DateTime> candidates = ByRuleExpander.ExpandPeriod(this, localStart, periodStart);

            foreach (DateTime candidate in candidates)
            {
                if (candidate < localStart)
                {
                    continue;
                }
                if (localUntil != null && candidate > localUntil.Value)
                {
                    return null;
                }
                if (localEnd != null && candidate > localEnd.Value)
                {
                    return null;
                }

                occurrenceCount++;
                if (occurrenceCount > countLimit)
                {
                    return null;
                }

                if (candidate > localAfter)
                {
                    return candidate;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Get the approximate period index that contains the given time.
    /// </summary>
    private int GetPeriodIndex(DateTime start, DateTime target)
    {
        if (target <= start)
        {
            return 0;
        }

        switch (Frequency)
        {
            case RecurrenceFrequency.Yearly:
                return (target.Year - start.Year) / Interval;
            case RecurrenceFrequency.Monthly:
                int months = (target.Year - start.Year) * 12 + (target.Month - start.Month);
                return months / Interval;
            case RecurrenceFrequency.Weekly:
                int days = (int)(target.Date - start.Date).TotalDays;
                return days / (7 * Interval);
            case RecurrenceFrequency.Daily:
                return (int)(target.Date - start.Date).TotalDays / Interval;
            case RecurrenceFrequency.Hourly:
                return (int)(target - start).TotalHours / Interval;
            case RecurrenceFrequency.Minutely:
                return (int)(target - start).TotalMinutes / Interval;
            case RecurrenceFrequency.Secondly:
                return (int)(target - start).TotalSeconds / Interval;
            default:
                return 0;
        }
    }

    /// <summary>
    /// Get the start of the Nth period from the dtStart.
    /// </summary>
    internal DateTime GetPeriodStart(DateTime start, int periodIndex)
    {
        int n = periodIndex * Interval;

        switch (Frequency)
        {
            case RecurrenceFrequency.Yearly:
                return new DateTime(start.Year + n, 1, 1, start.Hour, start.Minute, start.Second);
            case RecurrenceFrequency.Monthly:
                return new DateTime(start.Year, start.Month, 1, start.Hour, start.Minute, start.Second)
                    .AddMonths(n);
            case RecurrenceFrequency.Weekly:
                // Align to the week start day
                DateTime weekBase = start.Date;
                int daysToWeekStart = ((int)weekBase.DayOfWeek - (int)WeekStart + 7) % 7;
                weekBase = weekBase.AddDays(-daysToWeekStart);
                return weekBase.AddDays(n * 7).Add(start.TimeOfDay);
            case RecurrenceFrequency.Daily:
                return start.Date.AddDays(n).Add(start.TimeOfDay);
            case RecurrenceFrequency.Hourly:
                return new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second)
                    .AddHours(n);
            case RecurrenceFrequency.Minutely:
                return new DateTime(start.Year, start.Month, start.Day, start.Hour, start.Minute, start.Second)
                    .AddMinutes(n);
            case RecurrenceFrequency.Secondly:
                return start.AddSeconds(n);
            default:
                return start;
        }
    }

    /// <summary>
    /// Convert local DateTime to DateTimeOffset in the given timezone.
    /// Handles DST gaps by advancing to next valid time.
    /// </summary>
    private static DateTimeOffset ToDateTimeOffset(DateTime local, TimeZoneInfo tz)
    {
        if (tz == TimeZoneInfo.Utc)
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }

        // Check if time falls in a DST gap
        if (tz.IsInvalidTime(local))
        {
            // Advance past the gap
            TimeSpan[] offsets = GetAmbiguousOrGapOffsets(tz, local);
            // Simple approach: add the DST transition delta (typically 1 hour)
            TimeSpan adjustment = tz.GetUtcOffset(local.AddHours(2)) - tz.GetUtcOffset(local.AddHours(-2));
            if (adjustment > TimeSpan.Zero)
            {
                local = local.Add(adjustment);
            }
            else
            {
                local = local.AddHours(1);
            }
        }

        TimeSpan offset = tz.GetUtcOffset(local);

        // For ambiguous times (DST overlap), use the first (earlier/standard) offset
        if (tz.IsAmbiguousTime(local))
        {
            TimeSpan[] offsets = GetAmbiguousOrGapOffsets(tz, local);
            offset = offsets[0]; // Standard time offset (larger value = earlier UTC time)
            for (int i = 1; i < offsets.Length; i++)
            {
                if (offsets[i] > offset)
                {
                    offset = offsets[i];
                }
            }
        }

        return new DateTimeOffset(local, offset);
    }

    private static TimeSpan[] GetAmbiguousOrGapOffsets(TimeZoneInfo tz, DateTime local)
    {
        try
        {
            return tz.GetAmbiguousTimeOffsets(local);
        }
        catch
        {
            return new[] { tz.BaseUtcOffset };
        }
    }

    /// <summary>
    /// Round-trips back to canonical RRULE string (without "RRULE:" prefix).
    /// </summary>
    public override string ToString()
    {
        StringBuilder sb = new StringBuilder();
        sb.Append("FREQ=");
        sb.Append(Frequency.ToString().ToUpperInvariant());

        if (Interval != 1)
        {
            sb.Append(";INTERVAL=");
            sb.Append(Interval);
        }

        if (Count != null)
        {
            sb.Append(";COUNT=");
            sb.Append(Count.Value);
        }

        if (Until != null)
        {
            sb.Append(";UNTIL=");
            sb.Append(Until.Value.ToString("yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture));
            if (UntilIsUtc)
            {
                sb.Append('Z');
            }
        }

        if (WeekStart != DayOfWeek.Monday)
        {
            sb.Append(";WKST=");
            sb.Append(ReverseDayMap[WeekStart]);
        }

        AppendIntList(sb, "BYMONTH", ByMonth);
        AppendIntList(sb, "BYWEEKNO", ByWeekNo);
        AppendIntList(sb, "BYYEARDAY", ByYearDay);
        AppendIntList(sb, "BYMONTHDAY", ByMonthDay);

        if (ByDay != null)
        {
            sb.Append(";BYDAY=");
            for (int i = 0; i < ByDay.Length; i++)
            {
                if (i > 0)
                {
                    sb.Append(',');
                }
                (DayOfWeek day, int ordinal) = ByDay[i];
                if (ordinal != 0)
                {
                    sb.Append(ordinal);
                }
                sb.Append(ReverseDayMap[day]);
            }
        }

        AppendIntList(sb, "BYHOUR", ByHour);
        AppendIntList(sb, "BYMINUTE", ByMinute);
        AppendIntList(sb, "BYSECOND", BySecond);
        AppendIntList(sb, "BYSETPOS", BySetPos);

        return sb.ToString();
    }

    private static void AppendIntList(StringBuilder sb, string name, int[]? values)
    {
        if (values == null)
        {
            return;
        }
        sb.Append(';');
        sb.Append(name);
        sb.Append('=');
        for (int i = 0; i < values.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }
            sb.Append(values[i]);
        }
    }

    private static RecurrenceFrequency ParseFrequency(string value)
    {
        return value.ToUpperInvariant() switch
        {
            "SECONDLY" => RecurrenceFrequency.Secondly,
            "MINUTELY" => RecurrenceFrequency.Minutely,
            "HOURLY" => RecurrenceFrequency.Hourly,
            "DAILY" => RecurrenceFrequency.Daily,
            "WEEKLY" => RecurrenceFrequency.Weekly,
            "MONTHLY" => RecurrenceFrequency.Monthly,
            "YEARLY" => RecurrenceFrequency.Yearly,
            _ => throw new FormatException($"Unknown FREQ value: '{value}'")
        };
    }

    private static (DateTime value, bool isUtc) ParseUntil(string value)
    {
        bool isUtc = value.EndsWith("Z", StringComparison.OrdinalIgnoreCase);
        string datePart = isUtc ? value.Substring(0, value.Length - 1) : value;

        // Try datetime format: yyyyMMddTHHmmss
        if (datePart.Length == 15 && datePart[8] == 'T')
        {
            DateTime dt = DateTime.ParseExact(datePart, "yyyyMMdd'T'HHmmss", CultureInfo.InvariantCulture);
            return (dt, isUtc);
        }
        // Try date-only format: yyyyMMdd
        if (datePart.Length == 8)
        {
            DateTime dt = DateTime.ParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture);
            // Per RFC 5545, date-only UNTIL means end of that day (23:59:59)
            dt = dt.AddDays(1).AddSeconds(-1);
            return (dt, isUtc);
        }

        throw new FormatException($"Invalid UNTIL value: '{value}'");
    }

    private static DayOfWeek ParseDay(string value)
    {
        if (DayMap.TryGetValue(value, out DayOfWeek day))
        {
            return day;
        }
        throw new FormatException($"Unknown day abbreviation: '{value}'");
    }

    private static (DayOfWeek Day, int Ordinal)[] ParseByDay(string value)
    {
        string[] items = value.Split(',');
        (DayOfWeek Day, int Ordinal)[] result = new (DayOfWeek, int)[items.Length];

        for (int i = 0; i < items.Length; i++)
        {
            string item = items[i].Trim();
            if (item.Length < 2)
            {
                throw new FormatException($"Invalid BYDAY value: '{item}'");
            }

            // Last 2 chars are the day abbreviation
            string dayStr = item.Substring(item.Length - 2);
            if (!DayMap.TryGetValue(dayStr, out DayOfWeek day))
            {
                throw new FormatException($"Invalid day abbreviation in BYDAY: '{dayStr}'");
            }

            int ordinal = 0;
            if (item.Length > 2)
            {
                string ordStr = item.Substring(0, item.Length - 2);
                if (!int.TryParse(ordStr, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out ordinal))
                {
                    throw new FormatException($"Invalid ordinal in BYDAY: '{ordStr}'");
                }
            }

            result[i] = (day, ordinal);
        }

        return result;
    }

    private static int[] ParseIntList(string value)
    {
        string[] items = value.Split(',');
        int[] result = new int[items.Length];

        for (int i = 0; i < items.Length; i++)
        {
            result[i] = int.Parse(items[i].Trim(), CultureInfo.InvariantCulture);
        }

        return result;
    }
}
