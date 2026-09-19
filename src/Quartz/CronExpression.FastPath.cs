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

using System.Diagnostics.CodeAnalysis;

using Quartz.Util;

namespace Quartz;

// The next-fire-time search that asks the time zone nothing.
//
// GetTimeAfterSlow spends most of a call in TimeZoneInfo: it converts the search floor into wall
// clock, walks the fields as DateTimeOffsets, resolves the wall clock it lands on, and then asks
// again whether either end of the search fell in a repeated hour. Eight zone queries for one answer,
// and the BCL recomputes a year's transitions on every one of them.
//
// This file answers the same question with integers. The offset comes from a table rather than from
// the zone, the walk is over the same CronField bitmasks the slow path walks but in ints rather than
// in DateTimeOffsets, and the answer is composed once at the end. It refuses to answer at all unless
// it can prove the answer is the slow path's, and the proof is the SAFE SEGMENT: a stretch of time
// the zone's offset is constant over and that no transition comes within two days of. Inside one,
//
//   - the gap-rewind guard is false, because no wall clock in the segment fell in a gap;
//   - FirstInstantAtOrAfterLocal is new DateTimeOffset(wallClock, segmentOffset), because the wall
//     clock is neither invalid nor ambiguous;
//   - the fall-back demotion is false, for the same reason;
//   - and the second ambiguous pass returns its candidate unchanged, because the search floor is not
//     ambiguous either.
//
// So every branch the slow path takes over a transition is inert, and what is left is the walk and
// one subtraction. Both ends need a safe segment - the search floor, whose wall clock the walk starts
// at, and the wall clock it arrives at, which is what the answer is resolved from - but not the same
// one, because the slow path resolves the wall clock where it finds it and the offset the search
// started with never enters the answer.
//
// None of this is XML documentation on purpose: CronExpression is a public type whose documentation
// a consumer reads, the compiler concatenates the doc comments of every part of a partial type into
// one, and how the search is made fast is not something a consumer has any use for.
public sealed partial class CronExpression
{
    /// <summary>
    /// Days 1, 8, 15, 22 and 29 of a month - the days that share a day of the week with the 1st.
    /// Shifted left by 0-6 it names the days of any one weekday, and days past the 31st fall off the
    /// top of the <see cref="uint" /> on their own.
    /// </summary>
    private const uint WeekPattern = 0x20408102u;

    /// <summary>
    /// Days in each month of a non-leap year, indexed by month. Written as a
    /// <see cref="ReadOnlySpan{T}" /> over a <c>byte</c> literal, which the compiler puts in the
    /// assembly's data section rather than allocating an array.
    /// </summary>
    private static ReadOnlySpan<byte> DaysInMonthTable => [0, 31, 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];

    /// <summary>
    /// The earliest instant the fast path will answer for. A cron year field cannot name a year
    /// before <see cref="TriggerConstants.EarliestYear" />, so nothing is given up by refusing the
    /// centuries below it - and refusing them is what keeps <see cref="GetPreviousValidTimeBefore" />,
    /// whose binary search opens with a probe in year 2, from building an offset table nothing will
    /// ever read.
    /// </summary>
    private static readonly long minSupportedUtcTicks = new DateTime(TriggerConstants.EarliestYear, 1, 1).Ticks;

    /// <summary>
    /// The latest instant the fast path will answer for, which is past every year a cron expression
    /// can name and far enough below <see cref="DateTime.MaxValue" /> that the offset arithmetic
    /// cannot overflow.
    /// </summary>
    private static readonly long maxSupportedUtcTicks = new DateTime(2400, 1, 1).Ticks;

    /// <summary>
    /// The zone this expression last computed against, together with what was learned about it.
    /// Written as one reference so that a reader can never see the zone of one binding beside the
    /// offsets of another; <see cref="TimeZoneInfo.Local" /> can be replaced at run time, and a
    /// replacement is a new instance, so reference equality is what asks the question.
    /// </summary>
    [NonSerialized] private ZoneBinding? zoneBinding;

    /// <summary>
    /// The implementing half of the hook <c>CronExpression.cs</c> declares. It exists only to turn a
    /// <c>Try</c> pattern into the <c>ref</c> parameter an old-style partial method is allowed to
    /// take, which is what lets the call compile away in the assembly that links no fast path.
    /// </summary>
    /// <param name="afterTimeUtc">The UTC time to start searching from.</param>
    /// <param name="result">The next fire time, left null when the fast path declines.</param>
    partial void TryTimeAfterFast(DateTimeOffset afterTimeUtc, ref DateTimeOffset? result)
    {
        if (TryGetTimeAfterFast(afterTimeUtc, out DateTimeOffset found))
        {
            result = found;
        }
    }

    /// <summary>
    /// The next fire time strictly after <paramref name="afterTimeUtc" />, computed without asking
    /// the time zone anything, or <see langword="false" /> when this expression, this instant or this
    /// zone is one the fast path does not answer for.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so that <c>CronExpressionFastPathDifferentialTest</c> can ask
    /// which of the two paths an instant takes, and compare the answers where it takes this one. A
    /// counter in the product would have to be read on the path this exists to keep short.
    /// </remarks>
    /// <param name="afterTimeUtc">The UTC time to start searching from.</param>
    /// <param name="result">The next fire time, in UTC, when this returns <see langword="true" />.</param>
    internal bool TryGetTimeAfterFast(DateTimeOffset afterTimeUtc, out DateTimeOffset result)
    {
        result = default;

        long afterUtcTicks = afterTimeUtc.UtcTicks;
        if (afterUtcTicks < minSupportedUtcTicks || afterUtcTicks >= maxSupportedUtcTicks)
        {
            return false;
        }

        // The whole-second floor the search starts from. GetTimeAfterSlow reaches the same value by
        // adding a second and then dropping the milliseconds; adding a second to the floor is the
        // same arithmetic with the rounding written out.
        long floorUtcTicks = afterUtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond + TimeSpan.TicksPerSecond;

        if (!TryGetOffsetTable(floorUtcTicks, out ZoneOffsetTable? table))
        {
            return false;
        }

        // The wall clock the walk starts at. Reading it is what the gap-rewind guard would otherwise
        // have to be argued about, so the floor has to sit in a safe segment.
        if (!table.TryGetOffsetAt(floorUtcTicks, out long floorOffsetTicks))
        {
            return false;
        }

        if (!TryWalk(floorUtcTicks + floorOffsetTicks, out long foundLocalTicks))
        {
            return false;
        }

        // And the wall clock it ends at, which the slow path resolves against the zone as it finds it
        // - so this is the offset that goes into the answer, and it need not be the floor's.
        if (!table.TryGetOffsetForWallClock(foundLocalTicks, out long foundOffsetTicks))
        {
            return false;
        }

        long foundUtcTicks = foundLocalTicks - foundOffsetTicks;
        if (foundUtcTicks < floorUtcTicks)
        {
            // Unreachable while segments are days apart and offsets hours: a later wall clock in a
            // later segment is a later instant. Cheap enough to state rather than to argue.
            return false;
        }

        result = new DateTimeOffset(foundUtcTicks, TimeSpan.Zero);
        return true;
    }

    /// <summary>
    /// The offset table covering <paramref name="utcTicks" /> in this expression's time zone.
    /// </summary>
    private bool TryGetOffsetTable(long utcTicks, [NotNullWhen(true)] out ZoneOffsetTable? table)
    {
        TimeZoneInfo zone = TimeZone;
        ZoneBinding? binding = zoneBinding;
        if (binding is null || !ReferenceEquals(binding.Zone, zone))
        {
            binding = ZoneBinding.For(zone);
            zoneBinding = binding;
        }

        return binding.TryGetTable(utcTicks, out table);
    }

    /// <summary>
    /// Walks the field bitmasks from <paramref name="startLocalTicks" /> to the first wall clock at
    /// or after it that every field names, in whole seconds and without touching a
    /// <see cref="DateTime" />.
    /// </summary>
    /// <remarks>
    /// The shape is the slow path's read outside in rather than inside out: a field that has no value
    /// at or after the cursor's hands the search back to the field above it, and a field that moves
    /// resets every field below it to the start of its range. That is the same walk
    /// <c>ProgressNextFireTime</c> performs, which reaches the same place by rebuilding the date and
    /// restarting the loop each time a field moves.
    /// </remarks>
    /// <param name="startLocalTicks">The first wall clock the search may answer with, as ticks.</param>
    /// <param name="foundLocalTicks">The wall clock found, as ticks.</param>
    /// <returns><see langword="false" /> when the search ran past the year the scheduler gives up in,
    /// in which case the slow path runs and answers <see langword="null" />, as it always did.</returns>
    private bool TryWalk(long startLocalTicks, out long foundLocalTicks)
    {
        foundLocalTicks = 0;

        long dayNumber = startLocalTicks / TimeSpan.TicksPerDay;
        int secondOfDay = (int) ((startLocalTicks - dayNumber * TimeSpan.TicksPerDay) / TimeSpan.TicksPerSecond);

        CivilFromDayNumber((int) dayNumber, out int year, out int month, out int day);
        int hour = secondOfDay / 3600;
        int minute = secondOfDay / 60 % 60;
        int second = secondOfDay % 60;

        bool dayOfMonthRestricted = IsRestrictedDayField(daysOfMonth);
        bool dayOfWeekRestricted = IsRestrictedDayField(daysOfWeek);
        int giveUpYear = TriggerConstants.YearToGiveUpSchedulingAt;

        // The day candidates of the month the cursor is in, kept across loop passes so that an
        // expression whose time fields roll over does not rebuild them.
        int maskedMonthKey = 0;
        uint dayMask = 0;

        while (true)
        {
            // A day or month step can leave the cursor past December; a month field written '*' would
            // hand a thirteenth month straight back, so it is carried here rather than there.
            if (month > 12)
            {
                month -= 12;
                year++;
            }

            if (year > giveUpYear)
            {
                return false;
            }

            if (!years.TryGetMinValueStartingFrom(year, out int nextYear))
            {
                return false;
            }

            if (nextYear != year)
            {
                year = nextYear;
                month = 1;
                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            if (!months.TryGetMinValueStartingFrom(month, out int nextMonth))
            {
                year++;
                month = 1;
                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            if (nextMonth != month)
            {
                month = nextMonth;
                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
            }

            int lastDayOfMonth = DaysInMonth(year, month);
            int monthKey = year * 16 + month;
            if (monthKey != maskedMonthKey)
            {
                dayMask = DayMask(year, month, lastDayOfMonth, dayOfMonthRestricted, dayOfWeekRestricted);
                maskedMonthKey = monthKey;
            }

            uint daysAtOrAfter = dayMask & (uint.MaxValue << day);
            if (daysAtOrAfter == 0)
            {
                month++;
                day = 1;
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            int nextDay = BitUtil.TrailingZeroCount(daysAtOrAfter);
            if (nextDay != day)
            {
                day = nextDay;
                hour = 0;
                minute = 0;
                second = 0;
            }

            if (!hours.TryGetMinValueStartingFrom(hour, out int nextHour))
            {
                AdvanceDay(ref year, ref month, ref day, lastDayOfMonth);
                hour = 0;
                minute = 0;
                second = 0;
                continue;
            }

            if (nextHour != hour)
            {
                hour = nextHour;
                minute = 0;
                second = 0;
            }

            if (!minutes.TryGetMinValueStartingFrom(minute, out int nextMinute))
            {
                if (++hour > 23)
                {
                    hour = 0;
                    AdvanceDay(ref year, ref month, ref day, lastDayOfMonth);
                }

                minute = 0;
                second = 0;
                continue;
            }

            if (nextMinute != minute)
            {
                minute = nextMinute;
                second = 0;
            }

            if (!seconds.TryGetMinValueStartingFrom(second, out int nextSecond))
            {
                if (++minute > 59)
                {
                    minute = 0;
                    if (++hour > 23)
                    {
                        hour = 0;
                        AdvanceDay(ref year, ref month, ref day, lastDayOfMonth);
                    }
                }

                second = 0;
                continue;
            }

            second = nextSecond;
            break;
        }

        foundLocalTicks = (long) DayNumberFromCivil(year, month, day) * TimeSpan.TicksPerDay
                          + (hour * 3600L + minute * 60L + second) * TimeSpan.TicksPerSecond;
        return true;
    }

    /// <summary>
    /// Moves the cursor to the next day, rolling into the next month - and, from December, into the
    /// next year. A month past December is left for the month field to reject on the next pass, which
    /// is where the fields below it are reset from.
    /// </summary>
    private static void AdvanceDay(ref int year, ref int month, ref int day, int lastDayOfMonth)
    {
        day++;
        if (day <= lastDayOfMonth)
        {
            return;
        }

        day = 1;
        month++;
        if (month > 12)
        {
            month = 1;
            year++;
        }
    }

    /// <summary>
    /// The days of one month this expression names, as a bitmask where bit <c>n</c> is day <c>n</c>.
    /// </summary>
    /// <remarks>
    /// The rule is <c>DayOfMonthOrWeekMatches</c>' and <c>crontab(5)</c>'s: a day field written
    /// exactly '*' or '?' restricts nothing and defers to the other, and two fields that both name
    /// days are unioned. The 'L' and 'W' forms of the day-of-month field come out of
    /// <see cref="CalculateDaysOfMonth" />, which is the same per-month resolution the slow path uses.
    /// </remarks>
    private uint DayMask(int year, int month, int lastDayOfMonth, bool dayOfMonthRestricted, bool dayOfWeekRestricted)
    {
        uint daysInMonth = lastDayOfMonth == 31 ? 0xFFFFFFFEu : ((1u << (lastDayOfMonth + 1)) - 2);

        if (!dayOfMonthRestricted && !dayOfWeekRestricted)
        {
            return daysInMonth;
        }

        uint mask = 0;
        if (dayOfMonthRestricted)
        {
            mask |= lastDaySpecs is null && nearestWeekdays is null
                ? daysOfMonth.GetDayBits()
                : CalculateDaysOfMonth(year, month);
        }

        if (dayOfWeekRestricted)
        {
            mask |= DayOfWeekMask(year, month, lastDayOfMonth);
        }

        return mask & daysInMonth;
    }

    /// <summary>
    /// The days of one month that fall on a day of the week this expression names, as a day bitmask.
    /// </summary>
    /// <remarks>
    /// 'nL' and 'n#m' name one day of the month each - the last such weekday, and the mth one - so
    /// they produce a single bit, or none at all when the month has fewer than <c>m</c> of that
    /// weekday. That is the rule <c>DayOfWeekMatches</c> states and the one
    /// <c>ProgressNextFireTimeDayOfWeek</c> walks.
    /// </remarks>
    private uint DayOfWeekMask(int year, int month, int lastDayOfMonth)
    {
        int firstDayOfWeek = (DayNumberFromCivil(year, month, 1) + 1) % 7;

        if (lastDayOfWeek)
        {
            int first = FirstDayOfWeekInMonth(daysOfWeek.Min, firstDayOfWeek);
            return 1u << (first + (lastDayOfMonth - first) / 7 * 7);
        }

        if (nthdayOfWeek != 0)
        {
            int day = FirstDayOfWeekInMonth(daysOfWeek.Min, firstDayOfWeek) + (nthdayOfWeek - 1) * 7;
            return day <= lastDayOfMonth ? 1u << day : 0u;
        }

        // The field numbers the week 1 = Sunday through 7 = Saturday; shifting down by one puts it in
        // the BCL's numbering, where Sunday is zero.
        uint allowed = daysOfWeek.GetDayBits() >> 1;

        uint mask = 0;
        while (allowed != 0)
        {
            int dayOfWeek = BitUtil.TrailingZeroCount(allowed);
            allowed &= allowed - 1;

            int firstOfThatDay = dayOfWeek - firstDayOfWeek;
            if (firstOfThatDay < 0)
            {
                firstOfThatDay += 7;
            }

            mask |= WeekPattern << firstOfThatDay;
        }

        return mask;
    }

    /// <summary>
    /// The first day of the month that falls on a given day of the week, from the cron field's
    /// numbering of it.
    /// </summary>
    private static int FirstDayOfWeekInMonth(int cronDayOfWeek, int firstDayOfWeek)
    {
        int offset = cronDayOfWeek - 1 - firstDayOfWeek;
        if (offset < 0)
        {
            offset += 7;
        }

        return 1 + offset;
    }

    /// <summary>
    /// The number of days in a month, without <see cref="DateTime.DaysInMonth" />'s argument checks -
    /// the caller has already been through the month field, so the month is in range.
    /// </summary>
    private static int DaysInMonth(int year, int month)
    {
        return month == 2 && IsLeapYear(year) ? 29 : DaysInMonthTable[month];
    }

    private static bool IsLeapYear(int year)
    {
        return (year & 3) == 0 && (year % 100 != 0 || year % 400 == 0);
    }

    /// <summary>
    /// The calendar date of a day number, counting from day zero at 0001-01-01 as
    /// <see cref="DateTime.Ticks" /> does.
    /// </summary>
    /// <remarks>
    /// Howard Hinnant's <c>civil_from_days</c>, with the epoch moved from 1970-01-01 to 0001-01-01:
    /// the year is shifted so that it begins in March, which puts the leap day at the end of it and
    /// leaves the month lengths a straight line to divide by.
    /// </remarks>
    private static void CivilFromDayNumber(int dayNumber, out int year, out int month, out int day)
    {
        int shifted = dayNumber + 306; // days since 0000-03-01
        int era = shifted / 146097; // 400 years
        int dayOfEra = shifted - era * 146097;
        int yearOfEra = (dayOfEra - dayOfEra / 1460 + dayOfEra / 36524 - dayOfEra / 146096) / 365;
        int dayOfYear = dayOfEra - (365 * yearOfEra + yearOfEra / 4 - yearOfEra / 100);
        int marchMonth = (5 * dayOfYear + 2) / 153;

        day = dayOfYear - (153 * marchMonth + 2) / 5 + 1;
        month = marchMonth < 10 ? marchMonth + 3 : marchMonth - 9;
        year = yearOfEra + era * 400 + (month <= 2 ? 1 : 0);
    }

    /// <summary>
    /// The day number of a calendar date, the inverse of <see cref="CivilFromDayNumber" />.
    /// </summary>
    private static int DayNumberFromCivil(int year, int month, int day)
    {
        int shiftedYear = year - (month <= 2 ? 1 : 0);
        int era = shiftedYear / 400;
        int yearOfEra = shiftedYear - era * 400;
        int marchMonth = month > 2 ? month - 3 : month + 9;
        int dayOfYear = (153 * marchMonth + 2) / 5 + day - 1;
        int dayOfEra = yearOfEra * 365 + yearOfEra / 4 - yearOfEra / 100 + dayOfYear;

        return era * 146097 + dayOfEra - 306;
    }

    /// <summary>
    /// What this expression has learned about one <see cref="TimeZoneInfo" />, held as a single
    /// object so that the zone and what was learned of it are read together or not at all.
    /// </summary>
    private sealed class ZoneBinding
    {
        internal readonly TimeZoneInfo Zone;

        private readonly ZoneClock clock;

        private ZoneBinding(TimeZoneInfo zone, ZoneClock clock)
        {
            Zone = zone;
            this.clock = clock;
        }

        internal static ZoneBinding For(TimeZoneInfo zone)
        {
            return new ZoneBinding(zone, ZoneClock.For(zone));
        }

        internal bool TryGetTable(long utcTicks, [NotNullWhen(true)] out ZoneOffsetTable? table)
        {
            return clock.TryGetTable(utcTicks, out table);
        }
    }
}
