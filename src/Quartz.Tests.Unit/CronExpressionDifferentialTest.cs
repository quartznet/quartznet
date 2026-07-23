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

using System;
using System.Collections.Generic;
using System.Linq;

using Quartz.Util;

namespace Quartz.Tests.Unit;

/// <summary>
/// Differential / property tests for the bitmask fast path added to
/// <see cref="CronExpression" />. These guard against the bitmask diverging
/// from the original SortedSet-based behaviour, including the De Bruijn
/// trailing-zero fallback used on frameworks without
/// <c>System.Numerics.BitOperations</c> (net462/net472/netstandard2.0).
/// </summary>
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
            var cron = new CronExpression(expr) { TimeZone = TimeZoneInfo.Utc };

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
            var cron = new CronExpression(expr) { TimeZone = TimeZoneInfo.Utc };

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
            var cron = new CronExpression(expr) { TimeZone = TimeZoneInfo.Utc };

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
