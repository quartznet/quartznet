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

using Quartz.Util;

namespace Quartz.Tests.Unit;

/// <summary>
/// Differential / property tests for <see cref="CronExpression" />: every expectation here is
/// computed by this file rather than borrowed from the class under test, so a parse that disagrees
/// with its own evaluation has somewhere to surface.
/// </summary>
/// <remarks>
/// <para>
/// The first group guards the bitmask fast path against diverging from the original SortedSet-based
/// behaviour, including the De Bruijn trailing-zero fallback used on frameworks without
/// <c>System.Numerics.BitOperations</c> (net462/net472/netstandard2.0).
/// </para>
/// <para>
/// The last one is wider and works on whole expressions rather than one field's mechanics: a random
/// expression is walked minute by minute over a bounded window and the minutes it matches are
/// compared with the sequence <c>GetNextValidTimeAfter</c> chains out of it. That is a genuine
/// difference of method even though both ends are Quartz — the walk only ever asks the search to step
/// one minute, while the chain asks it to jump a gap, and it is jumping that has to work out which
/// day, month or year to land in. A disagreement is a fire time one of the two invented or lost.
/// </para>
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class CronExpressionDifferentialTest
{
    /// <summary>
    /// <see cref="BitUtil.TrailingZeroCount" /> must match a naive reference for
    /// every single-bit value and for a large number of random values. On the
    /// full framework this exercises the De Bruijn table; on .NET it exercises
    /// the BitOperations intrinsic.
    /// </summary>
    [Test]
    public void TrailingZeroCount_MatchesReference()
    {
        BitUtil.TrailingZeroCount(0).Should().Be(64, "zero has no set bit");

        for (int bit = 0; bit < 64; bit++)
        {
            ulong value = 1UL << bit;
            BitUtil.TrailingZeroCount(value).Should().Be(bit);
        }

        var random = new Random(20250623);
        for (int i = 0; i < 50_000; i++)
        {
            ulong value = NextUlong(random);
            if (value == 0)
            {
                continue;
            }

            BitUtil.TrailingZeroCount(value).Should().Be(ReferenceTrailingZeroCount(value));
        }
    }

    /// <summary>
    /// The bitmask "next allowed value" scan must return, for every start in
    /// range, the smallest set member greater than or equal to start — verified
    /// against an independent linear-scan reference.
    /// </summary>
    [Test]
    public void BitmaskNextValue_MatchesReference()
    {
        var random = new Random(1337);

        for (int iteration = 0; iteration < 20_000; iteration++)
        {
            // Build a random set of values in [0, 63] and the equivalent mask.
            var set = new SortedSet<int>();
            ulong mask = 0;
            int count = random.Next(0, 12);
            for (int i = 0; i < count; i++)
            {
                int value = random.Next(0, 64);
                set.Add(value);
                mask |= 1UL << value;
            }

            for (int start = 0; start < 64; start++)
            {
                // Reference: smallest set member >= start, if any.
                int? expectedMin = null;
                foreach (int v in set)
                {
                    if (v >= start)
                    {
                        expectedMin = v;
                        break;
                    }
                }

                bool actual = BitUtil.TryGetMinValueStartingFrom(mask, start, out int actualMin);

                actual.Should().Be(expectedMin.HasValue, "start={0}, set=[{1}]", start, string.Join(",", set));
                if (expectedMin.HasValue)
                {
                    actualMin.Should().Be(expectedMin.Value, "start={0}, set=[{1}]", start, string.Join(",", set));
                }
            }
        }
    }

    /// <summary>
    /// End-to-end property test: for randomly generated time-of-day expressions
    /// (random seconds/minutes/hours, every day) the next fire time computed by
    /// <see cref="CronExpression.GetNextValidTimeAfter" /> must equal the result
    /// of an independent brute-force second-by-second scan.
    /// </summary>
    [Test]
    public void GetNextValidTimeAfter_MatchesBruteForce_TimeOfDay()
    {
        var random = new Random(987654321);

        for (int iteration = 0; iteration < 500; iteration++)
        {
            HashSet<int> secs = RandomSubset(random, 0, 59, maxCount: 4);
            HashSet<int> mins = RandomSubset(random, 0, 59, maxCount: 4);
            HashSet<int> hours = RandomSubset(random, 0, 23, maxCount: 4);

            string expr = $"{Join(secs)} {Join(mins)} {Join(hours)} * * ?";
            var cron = new CronExpression(expr, TimeZoneInfo.Utc);

            // Random start somewhere across a few years, truncated to seconds.
            DateTimeOffset start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddSeconds(random.Next(0, 3 * 365 * 24 * 60 * 60));

            DateTimeOffset? actual = cron.GetNextValidTimeAfter(start);

            // Independent oracle: every day fires, so the next match is within
            // ~26 hours regardless of the start second.
            DateTimeOffset? expected = null;
            for (int i = 1; i <= 26 * 60 * 60; i++)
            {
                DateTimeOffset candidate = start.AddSeconds(i);
                if (secs.Contains(candidate.Second) && mins.Contains(candidate.Minute) && hours.Contains(candidate.Hour))
                {
                    expected = candidate;
                    break;
                }
            }

            expected.Should().NotBeNull("expression {0} fires daily", expr);
            actual.Should().Be(expected, "expression {0}, start {1:O}", expr, start);
        }
    }

    /// <summary>
    /// End-to-end property test for the day-of-month mask: random day sets with
    /// a fixed time of day, verified by an independent day-by-day scan.
    /// </summary>
    [Test]
    public void GetNextValidTimeAfter_MatchesBruteForce_DayOfMonth()
    {
        var random = new Random(424242);

        for (int iteration = 0; iteration < 500; iteration++)
        {
            HashSet<int> days = RandomSubset(random, 1, 28, maxCount: 5); // <=28 to fire every month
            HashSet<int> months = RandomSubset(random, 1, 12, maxCount: 4);

            string expr = $"0 0 12 {Join(days)} {Join(months)} ?";
            var cron = new CronExpression(expr, TimeZoneInfo.Utc);

            DateTimeOffset start = new DateTimeOffset(2024, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddDays(random.Next(0, 2 * 365));

            DateTimeOffset? actual = cron.GetNextValidTimeAfter(start);

            DateTimeOffset? expected = null;
            var noonOnStartDay = new DateTimeOffset(start.Year, start.Month, start.Day, 12, 0, 0, TimeSpan.Zero);
            for (int i = 0; i <= 400; i++)
            {
                DateTimeOffset candidate = noonOnStartDay.AddDays(i);
                if (candidate > start && days.Contains(candidate.Day) && months.Contains(candidate.Month))
                {
                    expected = candidate;
                    break;
                }
            }

            expected.Should().NotBeNull("expression {0} fires within a year", expr);
            actual.Should().Be(expected, "expression {0}, start {1:O}", expr, start);
        }
    }

    /// <summary>
    /// End-to-end property test for the day-of-month 'L'/'LW'/'L-n'/'nW' combined
    /// path: random mixes of plain days, last-day offsets and (possibly multiple)
    /// nearest-weekday tokens anywhere in the month, verified by an independent
    /// day-by-day membership scan. This guards the per-month candidate-mask
    /// construction, per-'W' resolution, and the month-wrap logic (a 'W' can shift
    /// a candidate to an earlier day than the month it belongs to).
    /// </summary>
    [Test]
    public void GetNextValidTimeAfter_MatchesBruteForce_LastDayAndWeekday()
    {
        var random = new Random(20240815);

        for (int iteration = 0; iteration < 2000; iteration++)
        {
            var parts = new List<string>();
            var numericDays = new List<int>();
            var lastDaySpecs = new List<(int offset, bool weekday, int weekdayOffset)>();
            var nearestWeekdayDays = new List<int>();

            // Days span the full 1-31 range so short-month over-run (a day 29-31
            // combined with 'L'/'W') is exercised too.
            // Each 'nW' token shifts its own day, so they can appear anywhere in
            // the month, in any number, mixed freely with plain numeric days.
            int nearestWeekdayCount = random.Next(0, 3);
            for (int i = 0; i < nearestWeekdayCount; i++)
            {
                int d = random.Next(1, 32);
                nearestWeekdayDays.Add(d);
                parts.Add(d + "W");
            }

            int numericCount = random.Next(0, 4);
            for (int i = 0; i < numericCount; i++)
            {
                int d = random.Next(1, 32);
                numericDays.Add(d);
                parts.Add(d.ToString());
            }

            int lastDayCount = random.Next(0, 3);
            for (int i = 0; i < lastDayCount; i++)
            {
                int offset = random.Next(0, 6);
                bool weekday = random.Next(0, 2) == 0;
                // trailing weekday offset ('LW-m' / 'L-nW-m') only applies with 'W'
                int weekdayOffset = weekday && random.Next(0, 2) == 0 ? random.Next(1, 6) : 0;
                lastDaySpecs.Add((offset, weekday, weekdayOffset));
                string token = offset == 0 ? "L" : "L-" + offset;
                if (weekday)
                {
                    token += "W";
                    if (weekdayOffset > 0)
                    {
                        token += "-" + weekdayOffset;
                    }
                }

                parts.Add(token);
            }

            if (parts.Count == 0)
            {
                continue;
            }

            string expr = $"0 0 12 {string.Join(",", parts)} * ?";
            var cron = new CronExpression(expr, TimeZoneInfo.Utc);

            DateTimeOffset start = new DateTimeOffset(2023, 1, 1, 0, 0, 0, TimeSpan.Zero)
                .AddDays(random.Next(0, 3 * 365));

            DateTimeOffset? actual = cron.GetNextValidTimeAfter(start);

            DateTimeOffset? expected = null;
            var noonOnStartDay = new DateTimeOffset(start.Year, start.Month, start.Day, 12, 0, 0, TimeSpan.Zero);
            for (int i = 0; i <= 400; i++)
            {
                DateTimeOffset candidate = noonOnStartDay.AddDays(i);
                if (candidate > start && IsMatchingDay(candidate.Year, candidate.Month, candidate.Day, numericDays, nearestWeekdayDays, lastDaySpecs))
                {
                    expected = candidate;
                    break;
                }
            }

            expected.Should().NotBeNull("expression {0} fires within a year", expr);
            actual.Should().Be(expected, "expression {0}, start {1:O}", expr, start);
        }
    }

    /// <summary>The window every generated expression is walked over — long enough to cross a month boundary.</summary>
    private const int WindowDays = 45;

    /// <summary>
    /// How many fire times are compared per expression. A dense expression fires every minute, and
    /// chaining through all sixty-four thousand of them would dominate the run for no extra reach: a
    /// chain that skips or invents a fire disagrees at that index, whatever comes after it. The day
    /// fields, which are the ones that make the search jump, get the whole window instead — see the
    /// once-a-day comparison in the test.
    /// </summary>
    private const int FiresPerExpression = 300;

    /// <summary>How many minutes per expression are put to <c>IsSatisfiedBy</c> as well as to the walk.</summary>
    private const int SampledMinutes = 120;

    private const int ExpressionsPerSeed = 120;

    /// <summary>
    /// Expression-level property: the minutes a random expression matches, found by walking the window
    /// a minute at a time, are exactly the fire times <see cref="CronExpression.GetNextValidTimeAfter" />
    /// chains out of the same window — and <see cref="CronExpression.IsSatisfiedBy" /> agrees with both
    /// on a sample of the minutes in between.
    /// </summary>
    /// <remarks>
    /// The membership rule is this file's own arithmetic, so a field parsed into the wrong set is caught
    /// as well as a search that lands in the wrong place. The generated grammar is deliberately the part
    /// the worked examples cover thinnest: wrapping ranges in every field, step values, the
    /// day-of-week forms — a plain set, <c>d#n</c> and <c>dL</c> — and expressions that fill
    /// <em>both</em> day fields, which is where the day rule decides between deferring to one field and
    /// unioning the two. The day-of-month special forms are left out because
    /// <see cref="GetNextValidTimeAfter_MatchesBruteForce_LastDayAndWeekday" /> already walks those
    /// against a reference of their own.
    /// </remarks>
    /// <param name="seed">
    /// Fixed, and part of the case name: a fuzz that draws from the clock reports a failure nobody can
    /// reproduce.
    /// </param>
    [TestCase(101)]
    [TestCase(202)]
    [TestCase(303)]
    [TestCase(404)]
    [TestCase(505)]
    [TestCase(606)]
    public void MinuteWalkAndGetNextValidTimeAfterAgreeOnRandomExpressions(int seed)
    {
        Random random = new Random(seed);
        DateTimeOffset windowStart = new DateTimeOffset(2024, 3, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset windowEnd = windowStart.AddDays(WindowDays);

        int expressionsThatFired = 0;

        for (int iteration = 0; iteration < ExpressionsPerSeed; iteration++)
        {
            GeneratedExpression generated = NextExpression(random);
            CronExpression cron = new CronExpression(generated.Text, TimeZoneInfo.Utc);

            // The walk: every minute in the window, tested against this file's own membership rule.
            List<DateTimeOffset> expected = [];
            for (DateTimeOffset minute = windowStart.AddMinutes(1); minute <= windowEnd && expected.Count < FiresPerExpression; minute = minute.AddMinutes(1))
            {
                if (generated.Matches(minute))
                {
                    expected.Add(minute);
                }
            }

            // The chain: each fire time asked for from the one before it, so every step is a jump.
            List<DateTimeOffset> actual = [];
            DateTimeOffset cursor = windowStart;
            while (actual.Count < FiresPerExpression)
            {
                DateTimeOffset? next = cron.GetNextValidTimeAfter(cursor);
                if (next is null || next > windowEnd)
                {
                    break;
                }

                actual.Add(next.Value);
                cursor = next.Value;
            }

            actual.Should().Equal(expected, "expression '{0}' (seed {1}, expression {2})", generated.Text, seed, iteration);

            if (expected.Count > 0)
            {
                expressionsThatFired++;
            }

            // The same day and month fields at a fixed time of day. This fires at most once a day, so the
            // comparison reaches the end of the window however dense the minute and hour fields were - and
            // it is the day fields that make the search jump a gap, so a jump landing in the wrong week or
            // the wrong month has nowhere else to show up.
            CronExpression daily = new CronExpression(generated.DailyText, TimeZoneInfo.Utc);

            DateTimeOffset noonOnTheFirstDay = new DateTimeOffset(windowStart.Year, windowStart.Month, windowStart.Day, 12, 0, 0, TimeSpan.Zero);

            List<DateTimeOffset> expectedDays = [];
            for (int day = 0; day <= WindowDays; day++)
            {
                DateTimeOffset candidate = noonOnTheFirstDay.AddDays(day);
                if (candidate > windowStart && candidate <= windowEnd && generated.MatchesDay(candidate))
                {
                    expectedDays.Add(candidate);
                }
            }

            List<DateTimeOffset> actualDays = [];
            cursor = windowStart;
            while (true)
            {
                DateTimeOffset? next = daily.GetNextValidTimeAfter(cursor);
                if (next is null || next > windowEnd)
                {
                    break;
                }

                actualDays.Add(next.Value);
                cursor = next.Value;
            }

            actualDays.Should().Equal(expectedDays, "expression '{0}' (seed {1}, expression {2})", generated.DailyText, seed, iteration);

            for (int sample = 0; sample < SampledMinutes; sample++)
            {
                DateTimeOffset minute = windowStart.AddMinutes(random.Next(0, WindowDays * 24 * 60));

                cron.IsSatisfiedBy(minute).Should().Be(
                    generated.Matches(minute),
                    "expression '{0}' at {1:O} (seed {2}, expression {3})", generated.Text, minute, seed, iteration);
            }
        }

        // Around half of the draws pick a month the window does not contain, and those still assert
        // something worth having - that the chain finds nothing either. The floor is here for the case
        // where a change to the generator makes every expression fire on nothing.
        expressionsThatFired.Should().BeGreaterThan(
            ExpressionsPerSeed / 3,
            "a generator that stopped producing expressions which fire inside the window would leave this test asserting nothing");
    }

    /// <summary>
    /// A random expression together with the membership rule this file derived it from. The two are
    /// built side by side so the rule can never be read back out of <see cref="CronExpression" />.
    /// </summary>
    private sealed class GeneratedExpression
    {
        public required string Text { get; init; }

        /// <summary>The same day and month fields, fixed at noon.</summary>
        public required string DailyText { get; init; }

        public required HashSet<int> Minutes { get; init; }

        public required HashSet<int> Hours { get; init; }

        public required HashSet<int> Months { get; init; }

        public required Func<DateTimeOffset, bool> DayMatches { get; init; }

        public bool Matches(DateTimeOffset moment)
        {
            return moment.Second == 0
                   && moment.Millisecond == 0
                   && MatchesDay(moment)
                   && Hours.Contains(moment.Hour)
                   && Minutes.Contains(moment.Minute);
        }

        public bool MatchesDay(DateTimeOffset moment)
        {
            return Months.Contains(moment.Month) && DayMatches(moment);
        }
    }

    private static GeneratedExpression NextExpression(Random random)
    {
        (string minuteToken, HashSet<int> minutes) = NextField(random, 0, 59);
        (string hourToken, HashSet<int> hours) = NextField(random, 0, 23);
        (string monthToken, HashSet<int> months) = NextField(random, 1, 12);

        string dayOfMonthToken;
        string dayOfWeekToken;
        Func<DateTimeOffset, bool> dayMatches;

        // Three shapes: a day-of-week with '?' opposite it, a day-of-month with '?' opposite it, and
        // both fields filled. The third is the one that reaches the union branch, and because either
        // field can come out a wildcard it reaches the whole 2x2 of the day rule rather than only the
        // union: a field written '*' or '?' names no days, so the other decides; two fields that both
        // name days are unioned; two that name none match every day.
        switch (random.Next(3))
        {
            case 0:
            {
                (dayOfWeekToken, dayMatches) = NextDayOfWeek(random);
                dayOfMonthToken = "?";
                break;
            }

            case 1:
            {
                (dayOfMonthToken, HashSet<int> daysOfMonth) = NextField(random, 1, 31);
                dayMatches = moment => daysOfMonth.Contains(moment.Day);
                dayOfWeekToken = "?";
                break;
            }

            default:
            {
                (dayOfMonthToken, HashSet<int> daysOfMonth) = NextField(random, 1, 31);
                (dayOfWeekToken, Func<DateTimeOffset, bool> dayOfWeekMatches) = NextDayOfWeek(random);

                bool dayOfMonthRestricted = RestrictsDays(dayOfMonthToken);
                bool dayOfWeekRestricted = RestrictsDays(dayOfWeekToken);

                dayMatches = dayOfMonthRestricted && dayOfWeekRestricted
                    ? moment => daysOfMonth.Contains(moment.Day) || dayOfWeekMatches(moment)
                    : dayOfMonthRestricted
                        ? moment => daysOfMonth.Contains(moment.Day)
                        : dayOfWeekRestricted
                            ? dayOfWeekMatches
                            : _ => true;
                break;
            }
        }

        return new GeneratedExpression
        {
            Text = $"0 {minuteToken} {hourToken} {dayOfMonthToken} {monthToken} {dayOfWeekToken}",
            DailyText = $"0 0 12 {dayOfMonthToken} {monthToken} {dayOfWeekToken}",
            Minutes = minutes,
            Hours = hours,
            Months = months,
            DayMatches = dayMatches
        };
    }

    /// <summary>
    /// Whether a day field names days. Written out here rather than asked of <see cref="CronExpression" />,
    /// because a reference rule that reads its answer out of the class under test proves nothing.
    /// </summary>
    private static bool RestrictsDays(string token) => token is not ("*" or "?");

    private static (string Token, Func<DateTimeOffset, bool> Matches) NextDayOfWeek(Random random)
    {
        int shape = random.Next(5);

        // Quartz numbers the week 1 = Sunday through 7 = Saturday.
        if (shape == 3)
        {
            int day = random.Next(1, 8);
            int nth = random.Next(1, 6);

            // The nth such weekday in the month, and no fallback when the month has fewer than n of them.
            return ($"{day}#{nth}", moment => QuartzDayOfWeek(moment) == day && (moment.Day - 1) / 7 + 1 == nth);
        }

        if (shape == 4)
        {
            int day = random.Next(1, 8);

            return ($"{day}L", moment => QuartzDayOfWeek(moment) == day && moment.Day + 7 > DateTime.DaysInMonth(moment.Year, moment.Month));
        }

        (string token, HashSet<int> values) = NextField(random, 1, 7);
        return (token, moment => values.Contains(QuartzDayOfWeek(moment)));
    }

    private static int QuartzDayOfWeek(DateTimeOffset moment) => (int) moment.DayOfWeek + 1;

    /// <summary>
    /// One field, as a token and as the set of values that token means. Four shapes: the wildcard, a
    /// list, a range that may run backwards through the top of the field, and a step.
    /// </summary>
    private static (string Token, HashSet<int> Values) NextField(Random random, int min, int max)
    {
        int span = max - min + 1;

        switch (random.Next(4))
        {
            case 0:
            {
                HashSet<int> all = [];
                for (int value = min; value <= max; value++)
                {
                    all.Add(value);
                }

                return ("*", all);
            }

            case 1:
            {
                HashSet<int> values = [];
                int count = random.Next(1, 5);
                for (int i = 0; i < count; i++)
                {
                    values.Add(random.Next(min, max + 1));
                }

                return (string.Join(",", values.OrderBy(value => value)), values);
            }

            case 2:
            {
                int from = random.Next(min, max + 1);
                int to = random.Next(min, max + 1);

                HashSet<int> values = [];
                for (int value = from; value <= (from <= to ? to : to + span); value++)
                {
                    values.Add(value > max ? value - span : value);
                }

                return ($"{from}-{to}", values);
            }

            default:
            {
                int from = random.Next(min, max + 1);

                // Quartz rejects an increment that reaches the top of the field, so the draw stops short of it.
                int increment = random.Next(1, span);

                HashSet<int> values = [];
                for (int value = from; value <= max; value += increment)
                {
                    values.Add(value);
                }

                return ($"{from}/{increment}", values);
            }
        }
    }

    private static bool IsMatchingDay(int year, int month, int day, List<int> numericDays, List<int> nearestWeekdayDays, List<(int offset, bool weekday, int weekdayOffset)> lastDaySpecs)
    {
        int lastDay = DateTime.DaysInMonth(year, month);

        if (numericDays.Contains(day) && day <= lastDay)
        {
            return true;
        }

        foreach (int wDay in nearestWeekdayDays)
        {
            if (ReferenceNearestWeekday(year, month, Math.Min(wDay, lastDay)) == day)
            {
                return true;
            }
        }

        foreach ((int offset, bool weekday, int weekdayOffset) in lastDaySpecs)
        {
            int baseDay = lastDay - offset;
            if (baseDay < 1)
            {
                continue;
            }

            int resolved = weekday ? ReferenceNearestWeekday(year, month, baseDay) : baseDay;
            if (weekday && weekdayOffset > 0)
            {
                resolved -= weekdayOffset;
                if (resolved < 1)
                {
                    resolved = 1; // 'LW-m' falls back to the 1st when it underflows the month
                }
            }

            if (resolved == day)
            {
                return true;
            }
        }

        return false;
    }

    // Independent nearest-weekday reference: Saturday shifts back one (or forward
    // two on the 1st), Sunday forward one (or back two on the last day).
    private static int ReferenceNearestWeekday(int year, int month, int day)
    {
        int lastDay = DateTime.DaysInMonth(year, month);
        return new DateTime(year, month, day).DayOfWeek switch
        {
            DayOfWeek.Saturday => day == 1 ? 3 : day - 1,
            DayOfWeek.Sunday => day == lastDay ? day - 2 : day + 1,
            _ => day
        };
    }

    private static HashSet<int> RandomSubset(Random random, int min, int max, int maxCount)
    {
        int count = random.Next(1, maxCount + 1);
        var result = new HashSet<int>();
        while (result.Count < count)
        {
            result.Add(random.Next(min, max + 1));
        }

        return result;
    }

    private static string Join(IEnumerable<int> values)
    {
        return string.Join(",", values.OrderBy(v => v));
    }

    private static int ReferenceTrailingZeroCount(ulong value)
    {
        int count = 0;
        while ((value & 1) == 0)
        {
            count++;
            value >>= 1;
        }

        return count;
    }

    private static ulong NextUlong(Random random)
    {
        // Mix two 32-bit draws and occasionally sparse/dense patterns.
        var bytes = new byte[8];
        random.NextBytes(bytes);
        ulong value = BitConverter.ToUInt64(bytes, 0);

        // Bias some iterations toward few set bits to stress the lowest-bit path.
        if (random.Next(0, 3) == 0)
        {
            value &= NextUlongFewBits(random);
        }

        return value;
    }

    private static ulong NextUlongFewBits(Random random)
    {
        ulong value = 0;
        int bits = random.Next(1, 4);
        for (int i = 0; i < bits; i++)
        {
            value |= 1UL << random.Next(0, 64);
        }

        return value;
    }
}
