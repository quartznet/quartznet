using System.Collections.Generic;
using System.Globalization;
using System.Text;

using Quartz.Util;

namespace Quartz.Impl.Recurrence;

/// <summary>
/// Parses and evaluates RFC 5545 RRULE recurrence rules.
/// Thread-safe after construction: all properties are set once in the private constructor
/// and must not be mutated afterward. Array properties are internal for performance
/// (avoiding copies); callers must treat them as read-only.
/// </summary>
internal sealed class RecurrenceRule
{
    // Search cap for GetNextOccurrence/GetLastOccurrenceBefore. Must be large enough
    // for sub-daily frequencies with restrictive BY* rules (e.g. FREQ=SECONDLY;BYHOUR=23
    // starting at midnight needs ~82,800 iterations). 100K covers >1 day of seconds.
    private const int MaxIterations = 100_000;

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
                    byMonthDay = ParseIntList(value, "BYMONTHDAY", -31, 31, allowZero: false);
                    break;
                case "BYMONTH":
                    byMonth = ParseIntList(value, "BYMONTH", 1, 12);
                    break;
                case "BYYEARDAY":
                    byYearDay = ParseIntList(value, "BYYEARDAY", -366, 366, allowZero: false);
                    break;
                case "BYWEEKNO":
                    byWeekNo = ParseIntList(value, "BYWEEKNO", -53, 53, allowZero: false);
                    break;
                case "BYHOUR":
                    byHour = ParseIntList(value, "BYHOUR", 0, 23);
                    break;
                case "BYMINUTE":
                    byMinute = ParseIntList(value, "BYMINUTE", 0, 59);
                    break;
                case "BYSECOND":
                    bySecond = ParseIntList(value, "BYSECOND", 0, 59);
                    break;
                case "BYSETPOS":
                    bySetPos = ParseIntList(value, "BYSETPOS", -366, 366, allowZero: false);
                    break;
                default:
                    // Ignore unknown properties (RFC 5545 allows X- extension properties)
                    break;
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
    /// <param name="dtStart">The recurrence start time (DTSTART).</param>
    /// <param name="after">Find the next occurrence strictly after this time.</param>
    /// <param name="timeZone">Timezone for wall-clock calculations.</param>
    /// <param name="endTime">Optional end time boundary.</param>
    /// <param name="skipCount">
    /// When true, ignore the COUNT property during evaluation. This is used when
    /// the trigger tracks TimesTriggered externally as the single source of truth
    /// for COUNT enforcement, avoiding an expensive walk-from-start on every call.
    /// </param>
    internal DateTimeOffset? GetNextOccurrence(
        DateTimeOffset dtStart,
        DateTimeOffset after,
        TimeZoneInfo? timeZone,
        DateTimeOffset? endTime,
        bool skipCount = false)
    {
        LocalTimes local = ConvertToLocal(dtStart, after, timeZone, endTime);

        bool useCount = Count != null && !skipCount;
        DateTime? result = useCount
            ? FindNextOccurrenceWithCount(local)
            : FindNextOccurrenceNonCount(local);

        if (result == null)
        {
            return null;
        }

        return ToDateTimeOffset(result.Value, local.TimeZone);
    }

    /// <summary>
    /// Returns the Nth occurrence of the recurrence (1-based), used for computing
    /// <see cref="Quartz.Impl.Triggers.RecurrenceTriggerImpl.FinalFireTimeUtc"/>
    /// on COUNT-based rules. Returns null if the rule ends before the Nth occurrence
    /// (either due to COUNT, UNTIL, or endTime).
    /// </summary>
    /// <summary>
    /// Hard cap for GetNthOccurrence period scanning. FinalFireTimeUtc is an
    /// infrequent admin property, so we allow substantial work but not unbounded.
    /// 10M periods covers ~115 days of SECONDLY or ~19 years of MINUTELY.
    /// </summary>
    private const int MaxNthIterations = 10_000_000;

    internal DateTimeOffset? GetNthOccurrence(
        DateTimeOffset dtStart,
        int n,
        TimeZoneInfo? timeZone,
        DateTimeOffset? endTime)
    {
        LocalTimes local = ConvertToLocal(dtStart, dtStart.AddSeconds(-1), timeZone, endTime);

        int limit = Count != null ? Math.Min(n, Count.Value) : n;
        int occurrenceCount = 0;

        int i = 0;
        int iterations = 0;
        int maxPeriods = (int)Math.Min((long)limit + MaxIterations, MaxNthIterations);

        while (iterations < maxPeriods)
        {
            DateTime periodStart = GetPeriodStart(local.Start, i);

            if (local.Until != null && periodStart > local.Until.Value)
            {
                return null;
            }
            if (local.End != null && periodStart > local.End.Value)
            {
                return null;
            }

            // Apply sub-daily fast-forward to skip non-matching periods
            if (Frequency < RecurrenceFrequency.Daily)
            {
                int? skip = TryFastForwardSubDaily(local.Start, i, periodStart);
                if (skip != null)
                {
                    i = skip.Value;
                    iterations++;
                    continue;
                }
            }

            List<DateTime> candidates = ByRuleExpander.ExpandPeriod(this, local.Start, periodStart);

            foreach (DateTime candidate in candidates)
            {
                if (candidate < local.Start)
                {
                    continue;
                }
                if (local.Until != null && candidate > local.Until.Value)
                {
                    return null;
                }
                if (local.End != null && candidate > local.End.Value)
                {
                    return null;
                }

                occurrenceCount++;
                if (occurrenceCount == limit)
                {
                    return occurrenceCount == n
                        ? ToDateTimeOffset(candidate, local.TimeZone)
                        : (DateTimeOffset?)null;
                }
            }

            i++;
            iterations++;
        }

        return null;
    }

    /// <summary>
    /// Returns the last occurrence of the recurrence on or before the effective boundary
    /// (UNTIL and/or endTime). Searches backward from the boundary for efficiency.
    /// </summary>
    internal DateTimeOffset? GetLastOccurrenceBefore(
        DateTimeOffset dtStart,
        TimeZoneInfo? timeZone,
        DateTimeOffset? endTime)
    {
        LocalTimes local = ConvertToLocal(dtStart, dtStart.AddSeconds(-1), timeZone, endTime);

        // Determine the effective boundary in local time
        DateTime? boundary = local.End;
        if (local.Until != null && (boundary == null || local.Until.Value < boundary.Value))
        {
            boundary = local.Until;
        }
        if (boundary == null)
        {
            return null;
        }

        // Search backward from the boundary: find the period containing the boundary,
        // then scan backward through periods until we find one with valid candidates.
        int boundaryPeriodIdx = GetPeriodIndex(local.Start, boundary.Value);
        int iterations = 0;
        int i = boundaryPeriodIdx;

        while (i >= 0 && iterations < MaxIterations)
        {
            DateTime periodStart = GetPeriodStart(local.Start, i);

            // Fast-backward: for sub-daily frequencies with limiting BY* rules,
            // skip backward instead of iterating every period.
            if (Frequency < RecurrenceFrequency.Daily)
            {
                int? skip = TryFastRewindSubDaily(local.Start, i, periodStart);
                if (skip != null)
                {
                    i = skip.Value;
                    iterations++;
                    continue;
                }
            }

            List<DateTime> candidates = ByRuleExpander.ExpandPeriod(this, local.Start, periodStart);

            // Walk candidates in reverse to find the last one within bounds
            for (int c = candidates.Count - 1; c >= 0; c--)
            {
                DateTime candidate = candidates[c];
                if (candidate < local.Start)
                {
                    continue;
                }
                if (local.Until != null && candidate > local.Until.Value)
                {
                    continue;
                }
                if (local.End != null && candidate > local.End.Value)
                {
                    continue;
                }
                return ToDateTimeOffset(candidate, local.TimeZone);
            }

            i--;
            iterations++;

            if (iterations >= MaxIterations)
            {
                return null;
            }
        }

        return null;
    }

    private readonly struct LocalTimes
    {
        internal readonly DateTime Start;
        internal readonly DateTime After;
        internal readonly DateTime? Until;
        internal readonly DateTime? End;
        internal readonly TimeZoneInfo TimeZone;

        internal LocalTimes(DateTime start, DateTime after, DateTime? until, DateTime? end, TimeZoneInfo timeZone)
        {
            Start = start;
            After = after;
            Until = until;
            End = end;
            TimeZone = timeZone;
        }
    }

    private LocalTimes ConvertToLocal(DateTimeOffset dtStart, DateTimeOffset after, TimeZoneInfo? timeZone, DateTimeOffset? endTime)
    {
        TimeZoneInfo tz = timeZone ?? TimeZoneInfo.Utc;
        DateTime localStart = TimeZoneUtil.ConvertTime(dtStart, tz).DateTime;
        DateTime localAfter = TimeZoneUtil.ConvertTime(after, tz).DateTime;

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
            localEnd = TimeZoneUtil.ConvertTime(endTime.Value, tz).DateTime;
        }

        return new LocalTimes(localStart, localAfter, localUntil, localEnd, tz);
    }

    private DateTime? FindNextOccurrenceNonCount(LocalTimes local)
    {
        int i = GetPeriodIndex(local.Start, local.After);
        if (i < 0)
        {
            i = 0;
        }

        int iterations = 0;
        while (iterations < MaxIterations)
        {
            DateTime periodStart = GetPeriodStart(local.Start, i);

            if (local.Until != null && periodStart > local.Until.Value)
            {
                return null;
            }
            if (local.End != null && periodStart > local.End.Value)
            {
                return null;
            }

            // Fast-forward: for sub-daily frequencies with limiting BY* rules,
            // skip ahead instead of iterating every second/minute/hour.
            if (Frequency < RecurrenceFrequency.Daily)
            {
                int? skip = TryFastForwardSubDaily(local.Start, i, periodStart);
                if (skip != null)
                {
                    i = skip.Value;
                    iterations++;
                    continue;
                }
            }

            List<DateTime> candidates = ByRuleExpander.ExpandPeriod(this, local.Start, periodStart);

            foreach (DateTime candidate in candidates)
            {
                if (candidate < local.Start)
                {
                    continue;
                }
                if (candidate <= local.After)
                {
                    continue;
                }
                if (local.Until != null && candidate > local.Until.Value)
                {
                    return null;
                }
                if (local.End != null && candidate > local.End.Value)
                {
                    return null;
                }
                return candidate;
            }

            i++;
            iterations++;
        }

        return null;
    }

    /// <summary>
    /// For sub-daily frequencies, check if the current period is excluded by a
    /// limiting BY* rule and return a jump-forward index if so. Returns null
    /// if no skip is needed (the period may contain valid candidates).
    /// </summary>
    private int? TryFastForwardSubDaily(DateTime start, int currentPeriodIdx, DateTime periodStart)
    {
        // BYMONTH: skip to next matching month
        if (ByMonth != null && Array.IndexOf(ByMonth, periodStart.Month) < 0)
        {
            for (int attempt = 0; attempt < 13; attempt++)
            {
                DateTime nextMonth = new DateTime(periodStart.Year, periodStart.Month, 1)
                    .AddMonths(attempt + 1);
                if (Array.IndexOf(ByMonth, nextMonth.Month) >= 0)
                {
                    DateTime target = new DateTime(nextMonth.Year, nextMonth.Month, 1, 0, 0, 0);
                    int idx = GetPeriodIndex(start, target);
                    return Math.Max(idx - 1, currentPeriodIdx + 1);
                }
            }
            return currentPeriodIdx + 1;
        }

        // BYDAY: skip to next matching day-of-week
        if (ByDay != null && !ByRuleExpander.MatchesByDayOfWeek(periodStart, ByDay))
        {
            // Jump forward to the start of the next day, then scan up to 7 days
            DateTime nextDay = periodStart.Date.AddDays(1);
            for (int d = 0; d < 7; d++)
            {
                DateTime candidate = nextDay.AddDays(d);
                if (ByRuleExpander.MatchesByDayOfWeek(candidate, ByDay))
                {
                    int idx = GetPeriodIndex(start, candidate);
                    return Math.Max(idx, currentPeriodIdx + 1);
                }
            }
            return currentPeriodIdx + 1;
        }

        // BYMONTHDAY: skip to next matching day-of-month
        if (ByMonthDay != null && !ByRuleExpander.MatchesByMonthDayValue(periodStart, ByMonthDay))
        {
            // Jump to the start of the next day
            DateTime nextDay = periodStart.Date.AddDays(1);
            int idx = GetPeriodIndex(start, nextDay);
            return Math.Max(idx, currentPeriodIdx + 1);
        }

        return null;
    }

    /// <summary>
    /// For sub-daily frequencies, check if the current period is excluded by a
    /// limiting BY* rule and return a jump-backward index if so. Returns null
    /// if no skip is needed.
    /// </summary>
    private int? TryFastRewindSubDaily(DateTime start, int currentPeriodIdx, DateTime periodStart)
    {
        // BYMONTH: skip backward to end of previous matching month
        if (ByMonth != null && Array.IndexOf(ByMonth, periodStart.Month) < 0)
        {
            for (int attempt = 0; attempt < 13; attempt++)
            {
                DateTime prevMonthEnd = new DateTime(periodStart.Year, periodStart.Month, 1)
                    .AddMonths(-attempt)
                    .AddSeconds(-1);
                if (prevMonthEnd < start)
                {
                    return 0;
                }
                if (Array.IndexOf(ByMonth, prevMonthEnd.Month) >= 0)
                {
                    int idx = GetPeriodIndex(start, prevMonthEnd);
                    return Math.Min(idx, currentPeriodIdx - 1);
                }
            }
            return Math.Max(currentPeriodIdx - 1, 0);
        }

        // BYDAY: skip backward to end of previous matching day
        if (ByDay != null && !ByRuleExpander.MatchesByDayOfWeek(periodStart, ByDay))
        {
            DateTime prevDay = periodStart.Date.AddSeconds(-1);
            for (int d = 0; d < 7; d++)
            {
                DateTime candidate = prevDay.AddDays(-d);
                if (candidate < start)
                {
                    return 0;
                }
                if (ByRuleExpander.MatchesByDayOfWeek(candidate, ByDay))
                {
                    int idx = GetPeriodIndex(start, candidate);
                    return Math.Min(idx, currentPeriodIdx - 1);
                }
            }
            return Math.Max(currentPeriodIdx - 1, 0);
        }

        // BYMONTHDAY: skip backward to end of previous day
        if (ByMonthDay != null && !ByRuleExpander.MatchesByMonthDayValue(periodStart, ByMonthDay))
        {
            DateTime prevDay = periodStart.Date.AddSeconds(-1);
            if (prevDay < start)
            {
                return 0;
            }
            int idx = GetPeriodIndex(start, prevDay);
            return Math.Min(idx, currentPeriodIdx - 1);
        }

        return null;
    }

    private DateTime? FindNextOccurrenceWithCount(LocalTimes local)
    {
        int occurrenceCount = 0;
        int countLimit = Count!.Value;

        int i = 0;
        int iterations = 0;
        while (iterations < MaxIterations)
        {
            DateTime periodStart = GetPeriodStart(local.Start, i);

            if (local.Until != null && periodStart > local.Until.Value)
            {
                return null;
            }
            if (local.End != null && periodStart > local.End.Value)
            {
                return null;
            }

            // Apply the same sub-daily fast-forward as the non-COUNT path
            if (Frequency < RecurrenceFrequency.Daily)
            {
                int? skip = TryFastForwardSubDaily(local.Start, i, periodStart);
                if (skip != null)
                {
                    i = skip.Value;
                    iterations++;
                    continue;
                }
            }

            List<DateTime> candidates = ByRuleExpander.ExpandPeriod(this, local.Start, periodStart);

            foreach (DateTime candidate in candidates)
            {
                if (candidate < local.Start)
                {
                    continue;
                }
                if (local.Until != null && candidate > local.Until.Value)
                {
                    return null;
                }
                if (local.End != null && candidate > local.End.Value)
                {
                    return null;
                }

                occurrenceCount++;
                if (occurrenceCount > countLimit)
                {
                    return null;
                }

                if (candidate > local.After)
                {
                    return candidate;
                }
            }

            i++;
            iterations++;
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

        // Use long arithmetic to avoid overflow for large time spans,
        // then clamp to int range (downstream iteration caps at MaxIterations anyway).
        long result;
        switch (Frequency)
        {
            case RecurrenceFrequency.Yearly:
                result = (target.Year - start.Year) / Interval;
                break;
            case RecurrenceFrequency.Monthly:
                long months = (long)(target.Year - start.Year) * 12 + (target.Month - start.Month);
                result = months / Interval;
                break;
            case RecurrenceFrequency.Weekly:
                // Align to WeekStart, matching GetPeriodStart's alignment
                int daysToWeekStart = ((int)start.DayOfWeek - (int)WeekStart + 7) % 7;
                DateTime weekBase = start.Date.AddDays(-daysToWeekStart);
                long weekDays = (long)(target.Date - weekBase).TotalDays;
                result = weekDays / (7 * Interval);
                break;
            case RecurrenceFrequency.Daily:
                result = (long)(target.Date - start.Date).TotalDays / Interval;
                break;
            case RecurrenceFrequency.Hourly:
                result = (long)(target - start).TotalHours / Interval;
                break;
            case RecurrenceFrequency.Minutely:
                result = (long)(target - start).TotalMinutes / Interval;
                break;
            case RecurrenceFrequency.Secondly:
                result = (long)(target - start).TotalSeconds / Interval;
                break;
            default:
                return 0;
        }

        // Clamp to int range — values beyond MaxIterations are capped downstream
        if (result > int.MaxValue)
        {
            return int.MaxValue;
        }
        return (int)result;
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
    /// Handles DST gaps by advancing to next valid time, and DST overlaps
    /// by choosing the earlier (daylight/first) offset.
    /// </summary>
    private static DateTimeOffset ToDateTimeOffset(DateTime local, TimeZoneInfo tz)
    {
        if (ReferenceEquals(tz, TimeZoneInfo.Utc))
        {
            return new DateTimeOffset(local, TimeSpan.Zero);
        }

        // Check if time falls in a DST gap (spring forward)
        if (tz.IsInvalidTime(local))
        {
            // Advance past the gap by computing the DST transition delta
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

        // For ambiguous times (DST overlap / fall back), use the daylight offset
        // (the larger UTC offset = earlier UTC instant), matching CalendarIntervalTriggerImpl behavior
        if (tz.IsAmbiguousTime(local))
        {
            TimeSpan[] offsets = tz.GetAmbiguousTimeOffsets(local);
            offset = offsets[0];
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
            if (isUtc)
            {
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
            return (dt, isUtc);
        }
        // Try date-only format: yyyyMMdd
        if (datePart.Length == 8)
        {
            DateTime dt = DateTime.ParseExact(datePart, "yyyyMMdd", CultureInfo.InvariantCulture);
            // Per RFC 5545, date-only UNTIL means end of that day (23:59:59)
            dt = dt.AddDays(1).AddSeconds(-1);
            if (isUtc)
            {
                dt = DateTime.SpecifyKind(dt, DateTimeKind.Utc);
            }
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
                // RFC 5545: ordinal must be -53..53, excluding 0
                if (ordinal == 0 || ordinal < -53 || ordinal > 53)
                {
                    throw new FormatException($"BYDAY ordinal {ordinal} is out of range (-53 to 53, excluding 0).");
                }
            }

            result[i] = (day, ordinal);
        }

        return result;
    }

    private static int[] ParseIntList(string value, string propertyName, int min, int max, bool allowZero = true)
    {
        string[] items = value.Split(',');
        int[] result = new int[items.Length];

        for (int i = 0; i < items.Length; i++)
        {
            int v = int.Parse(items[i].Trim(), CultureInfo.InvariantCulture);
            if (v < min || v > max || (!allowZero && v == 0))
            {
                throw new FormatException($"{propertyName} value {v} is out of range ({min} to {max}{(allowZero ? "" : ", excluding 0")}).");
            }
            result[i] = v;
        }

        return result;
    }
}
