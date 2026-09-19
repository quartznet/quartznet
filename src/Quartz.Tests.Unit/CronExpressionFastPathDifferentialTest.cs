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

#nullable enable

using System.Globalization;

using TimeZoneConverter;

namespace Quartz.Tests.Unit;

/// <summary>
/// The contract of the next-occurrence fast path (#3801): where it answers, it answers exactly what
/// <see cref="CronExpression.GetTimeAfterSlow" /> would have, down to the tick and the offset.
/// </summary>
/// <remarks>
/// <para>
/// The fast path exists to avoid asking <see cref="TimeZoneInfo" /> anything, which it can only do by
/// deciding in advance that the question has one answer over a stretch of time. Everything that can
/// go wrong with that decision goes wrong near a daylight-saving transition, so half the instants
/// here are drawn within three days of one - found by this file's own sweep of the zone's offsets,
/// not from anything the product computed - and the second either side of every gap and overlap edge
/// is sampled outright.
/// </para>
/// <para>
/// The zones are chosen for the shapes that break an offset table: a half-hour delta (Lord Howe), a
/// zone that abolished daylight saving partway through the window (São Paulo), one whose daylight
/// period is the winter under IANA data (Dublin), one with four transitions a year around Ramadan
/// (Morocco), one that skipped a calendar day (Samoa), one that never moves (a custom fixed offset,
/// and UTC), and whichever one this machine is set to.
/// </para>
/// <para>
/// The last assertion is the one that says the work was worth doing: over instants drawn uniformly
/// from the window, at least nine in ten must actually take the fast path. A fast path that is always
/// correct because it always declines would pass everything above it.
/// </para>
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class CronExpressionFastPathDifferentialTest
{
    /// <summary>The window instants are drawn from, and the years the transition sweep covers.</summary>
    private const int WindowFirstYear = 2020;

    private const int WindowLastYear = 2032;

    /// <summary>How many expressions each zone is put through. They are the same for every zone.</summary>
    private const int ExpressionCount = 600;

    /// <summary>How long a chain of "the next one after that" each expression is walked through.</summary>
    private const int ChainLength = 100;

    private const int UniformInstantsPerZone = 24;

    private const int TransitionInstantsPerZone = 24;

    /// <summary>
    /// Zones by the id <see cref="TZConvert" /> knows them under, which resolves on every OS. A zone
    /// this machine's database does not carry ignores its own case rather than failing it.
    /// </summary>
    public static IEnumerable<string> ZoneIds =>
    [
        UtcZoneId,
        "Europe/Helsinki",
        "America/Sao_Paulo",
        "Australia/Lord_Howe",
        "Europe/Dublin",
        "Africa/Casablanca",
        "Pacific/Apia",
        "America/New_York",
        FixedOffsetZoneId,
        LocalZoneId
    ];

    private const string UtcZoneId = "<utc>";

    private const string FixedOffsetZoneId = "<fixed +05:45>";

    private const string LocalZoneId = "<local>";

    /// <summary>
    /// Every expression, every instant: the fast path either declines or agrees exactly, and
    /// <see cref="CronExpression.GetNextValidTimeAfter" /> - which is what the hook actually changed -
    /// answers what the slow path alone answered.
    /// </summary>
    [TestCaseSource(nameof(ZoneIds))]
    public void FastPathAgreesWithSlowPath(string zoneId)
    {
        TimeZoneInfo? zone = TryResolveZone(zoneId);
        if (zone is null)
        {
            Assert.Ignore($"time zone {zoneId} is not available on this system");
            return;
        }

        List<DateTimeOffset> transitions = FindTransitions(zone);

        Random random = new Random(20260919);
        List<DateTimeOffset> uniform = UniformInstants(random);
        List<DateTimeOffset> nearTransitions = TransitionInstants(random, transitions);

        string[] expressions = Expressions();

        int uniformSamples = 0;
        int uniformFastPath = 0;

        foreach (string text in expressions)
        {
            CronExpression cron = new CronExpression(text, zone);

            foreach (DateTimeOffset after in uniform)
            {
                uniformSamples++;
                if (AssertAgrees(cron, text, after, zone))
                {
                    uniformFastPath++;
                }
            }

            foreach (DateTimeOffset after in nearTransitions)
            {
                AssertAgrees(cron, text, after, zone);
            }
        }

        // A chain is what a running trigger does: every call starts from the answer to the last one,
        // so a fast path that is a second out anywhere drags the whole tail of the chain with it.
        foreach (string text in expressions)
        {
            CronExpression cron = new CronExpression(text, zone);

            foreach (DateTimeOffset start in ChainStarts(uniform, nearTransitions))
            {
                DateTimeOffset cursor = start;
                for (int step = 0; step < ChainLength; step++)
                {
                    DateTimeOffset? next = AssertAgreesAndGet(cron, text, cursor, zone);
                    if (next is null)
                    {
                        break;
                    }

                    cursor = next.Value;
                }
            }
        }

        double ratio = uniformFastPath / (double) uniformSamples;
        TestContext.Out.WriteLine($"{zoneId}: fast path took {uniformFastPath}/{uniformSamples} uniform samples ({ratio:P2})");

        ratio.Should().BeGreaterThanOrEqualTo(
            0.90,
            "a fast path that declines is always correct and never faster, so the share of ordinary instants it answers is the measurement that matters");
    }

    /// <summary>
    /// The 'L', 'W', '#' and 'nL' day forms, which resolve per month rather than out of a bitmask
    /// fixed at parse time, over the same zones and the same instants.
    /// </summary>
    [TestCaseSource(nameof(ZoneIds))]
    public void FastPathAgreesWithSlowPathOnPerMonthDayForms(string zoneId)
    {
        TimeZoneInfo? zone = TryResolveZone(zoneId);
        if (zone is null)
        {
            Assert.Ignore($"time zone {zoneId} is not available on this system");
            return;
        }

        List<DateTimeOffset> transitions = FindTransitions(zone);
        Random random = new Random(3801);
        List<DateTimeOffset> uniform = UniformInstants(random);
        List<DateTimeOffset> nearTransitions = TransitionInstants(random, transitions);

        int uniformSamples = 0;
        int uniformFastPath = 0;

        foreach (string text in PerMonthDayExpressions())
        {
            CronExpression cron = new CronExpression(text, zone);

            foreach (DateTimeOffset after in uniform)
            {
                uniformSamples++;
                if (AssertAgrees(cron, text, after, zone))
                {
                    uniformFastPath++;
                }

                // These fire monthly at most, so a chain of a dozen is more than a year of them.
                Chain(cron, text, after, zone, steps: 12);
            }

            foreach (DateTimeOffset after in nearTransitions)
            {
                AssertAgrees(cron, text, after, zone);
                Chain(cron, text, after, zone, steps: 12);
            }
        }

        double ratio = uniformFastPath / (double) uniformSamples;
        TestContext.Out.WriteLine($"{zoneId}: per-month day forms took the fast path {uniformFastPath}/{uniformSamples} uniform samples ({ratio:P2})");

        ratio.Should().BeGreaterThanOrEqualTo(
            0.80,
            "the 'L'/'W'/'#' forms are walked rather than left behind - though a monthly expression is far more likely than a five-minute one to have its next fire land near a transition, so the share is lower than the headline figure");
    }

    /// <summary>
    /// The fast path must never answer where the slow path answers nothing: a chain that keeps
    /// producing fire times for an expression the slow path has run out of years for would schedule a
    /// trigger that should have stopped.
    /// </summary>
    [Test]
    public void FastPathDeclinesWhereSlowPathFindsNothing()
    {
        CronExpression cron = new CronExpression("0 0 0 29 2 ? 2021-2023", TimeZoneInfo.Utc);

        DateTimeOffset after = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);

        cron.GetTimeAfterSlow(after).Should().BeNull("no 29 February falls in 2021-2023");
        cron.TryGetTimeAfterFast(after, out _).Should().BeFalse("nothing to answer with is the slow path's answer to give");
        cron.GetNextValidTimeAfter(after).Should().BeNull();
    }

    /// <summary>
    /// Sub-second input must not move the answer: the search floors to the whole second before it
    /// starts, and the fast path has to floor it the same way the slow path does.
    /// </summary>
    [Test]
    public void FastPathFloorsSubSecondInputTheSameWay()
    {
        CronExpression cron = new CronExpression("0 0/5 * * * ?", TimeZoneInfo.Utc);

        for (int ticks = 0; ticks < 10; ticks++)
        {
            DateTimeOffset after = new DateTimeOffset(2026, 3, 16, 12, 4, 59, TimeSpan.Zero).AddTicks(ticks * 1_000_000);

            cron.TryGetTimeAfterFast(after, out DateTimeOffset fast).Should().BeTrue();
            fast.EqualsExact(cron.GetTimeAfterSlow(after)!.Value).Should().BeTrue("offset {0} ticks", ticks * 1_000_000);
        }
    }

    private static bool AssertAgrees(CronExpression cron, string text, DateTimeOffset after, TimeZoneInfo zone)
    {
        return AssertAgreesCore(cron, text, after, zone, out _);
    }

    private static void Chain(CronExpression cron, string text, DateTimeOffset start, TimeZoneInfo zone, int steps)
    {
        DateTimeOffset cursor = start;
        for (int step = 0; step < steps; step++)
        {
            DateTimeOffset? next = AssertAgreesAndGet(cron, text, cursor, zone);
            if (next is null)
            {
                return;
            }

            cursor = next.Value;
        }
    }

    private static DateTimeOffset? AssertAgreesAndGet(CronExpression cron, string text, DateTimeOffset after, TimeZoneInfo zone)
    {
        AssertAgreesCore(cron, text, after, zone, out DateTimeOffset? slow);
        return slow;
    }

    private static bool AssertAgreesCore(CronExpression cron, string text, DateTimeOffset after, TimeZoneInfo zone, out DateTimeOffset? slow)
    {
        slow = cron.GetTimeAfterSlow(after);
        bool tookFastPath = cron.TryGetTimeAfterFast(after, out DateTimeOffset fast);

        if (tookFastPath)
        {
            slow.Should().NotBeNull("'{0}' in {1} after {2:O} was answered by the fast path, so the search has an answer", text, zone.Id, after);
            fast.EqualsExact(slow!.Value).Should().BeTrue(
                "'{0}' in {1} after {2:O}: fast path said {3:O}, the search says {4:O}",
                text, zone.Id, after, fast, slow.Value);
        }

        DateTimeOffset? actual = cron.GetNextValidTimeAfter(after);
        if (slow is null)
        {
            actual.Should().BeNull("'{0}' in {1} after {2:O}", text, zone.Id, after);
        }
        else
        {
            actual.Should().NotBeNull("'{0}' in {1} after {2:O}", text, zone.Id, after);
            actual!.Value.EqualsExact(slow.Value).Should().BeTrue(
                "'{0}' in {1} after {2:O}: the hooked search said {3:O}, the search alone says {4:O}",
                text, zone.Id, after, actual.Value, slow.Value);
        }

        return tookFastPath;
    }

    private static IEnumerable<DateTimeOffset> ChainStarts(List<DateTimeOffset> uniform, List<DateTimeOffset> nearTransitions)
    {
        yield return uniform[0];
        yield return nearTransitions.Count > 0 ? nearTransitions[0] : uniform[1];
    }

    private static List<DateTimeOffset> UniformInstants(Random random)
    {
        DateTimeOffset start = new DateTimeOffset(WindowFirstYear, 1, 1, 0, 0, 0, TimeSpan.Zero);
        long span = new DateTimeOffset(WindowLastYear, 12, 31, 0, 0, 0, TimeSpan.Zero).Ticks - start.Ticks;

        List<DateTimeOffset> instants = [];
        for (int i = 0; i < UniformInstantsPerZone; i++)
        {
            instants.Add(start.AddTicks((long) (random.NextDouble() * span)));
        }

        return instants;
    }

    private static List<DateTimeOffset> TransitionInstants(Random random, List<DateTimeOffset> transitions)
    {
        List<DateTimeOffset> instants = [];
        if (transitions.Count == 0)
        {
            return instants;
        }

        // The seconds either side of the transition itself, which is where a gap opens and an overlap
        // closes, and where every branch the fast path proves inert actually fires.
        foreach (DateTimeOffset transition in transitions)
        {
            for (int offset = -2; offset <= 2; offset++)
            {
                instants.Add(transition.AddSeconds(offset));
            }
        }

        for (int i = 0; i < TransitionInstantsPerZone; i++)
        {
            DateTimeOffset transition = transitions[random.Next(transitions.Count)];
            instants.Add(transition.AddTicks((long) ((random.NextDouble() * 2 - 1) * 72 * TimeSpan.TicksPerHour)));
        }

        return instants;
    }

    /// <summary>
    /// Every instant in the window at which the zone's offset changes, located to the second by this
    /// file's own scan. Nothing about the product is consulted, so a table that missed a transition
    /// cannot hide it from the instants drawn here.
    /// </summary>
    private static List<DateTimeOffset> FindTransitions(TimeZoneInfo zone)
    {
        List<DateTimeOffset> transitions = [];

        DateTimeOffset cursor = new DateTimeOffset(WindowFirstYear - 1, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset end = new DateTimeOffset(WindowLastYear + 1, 1, 1, 0, 0, 0, TimeSpan.Zero);

        TimeSpan previous = zone.GetUtcOffset(cursor);
        while (cursor < end)
        {
            DateTimeOffset next = cursor.AddDays(1);
            TimeSpan offset = zone.GetUtcOffset(next);
            if (offset != previous)
            {
                transitions.Add(Bisect(zone, cursor, next));
            }

            previous = offset;
            cursor = next;
        }

        return transitions;
    }

    /// <summary>The first second at which the zone's offset differs from the one it had at <paramref name="low" />.</summary>
    private static DateTimeOffset Bisect(TimeZoneInfo zone, DateTimeOffset low, DateTimeOffset high)
    {
        TimeSpan before = zone.GetUtcOffset(low);
        while (high - low > TimeSpan.FromSeconds(1))
        {
            DateTimeOffset mid = low.AddTicks((high - low).Ticks / 2);
            if (zone.GetUtcOffset(mid) == before)
            {
                low = mid;
            }
            else
            {
                high = mid;
            }
        }

        return new DateTimeOffset(high.UtcTicks / TimeSpan.TicksPerSecond * TimeSpan.TicksPerSecond, TimeSpan.Zero);
    }

    internal static TimeZoneInfo? TryResolveZone(string zoneId)
    {
        if (zoneId == UtcZoneId)
        {
            return TimeZoneInfo.Utc;
        }

        if (zoneId == LocalZoneId)
        {
            return TimeZoneInfo.Local;
        }

        if (zoneId == FixedOffsetZoneId)
        {
            return TimeZoneInfo.CreateCustomTimeZone("Quartz/Fixed0545", TimeSpan.FromMinutes(345), "Quartz fixed +05:45", "Quartz fixed +05:45");
        }

        try
        {
            return TZConvert.GetTimeZoneInfo(zoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// The expression grammar, generated once and shared by every zone so that a failure in one zone
    /// and a pass in another are the same expression. The shapes are the ones the walk has to get
    /// right: wildcards, lists, wrapping ranges, steps, seconds, both day fields filled at once, and
    /// an explicit year range.
    /// </summary>
    private static string[] Expressions()
    {
        Random random = new Random(987001);
        List<string> expressions =
        [
            // The shapes the benchmarks and the documentation lead with, pinned rather than drawn.
            "0 0/5 * * * ?",
            "0/15 * * * * ?",
            "0 0 9-17 ? * MON-FRI",
            "0 15 10 * * ?",
            "0 0 12 * * ?",
            "0 0-30 9-17 * * ?",
            "0 0,10,20,30,40,50 * * * ?",
            "0 15 10 1,2,3,4,5,10,15,20,25 * ? *",
            "* * * * * ?",
            "0 0 0 1 1 ?",
            "0 0 0 29 2 ?",
            "0 0 0 31 * ?",
            "0 0 22-2 * * ?",
            "0 0 12 ? * FRI-MON",
            "0 15 10 * * ? 2005-2035",
            "0 0 0 1 * ? 2026",
            "0 0 12 1 * MON"
        ];

        while (expressions.Count < ExpressionCount)
        {
            expressions.Add(NextExpression(random));
        }

        return expressions.ToArray();
    }

    /// <summary>
    /// The day forms that have to be resolved against a particular month: the last day, the nearest
    /// weekday to a day, the nth weekday of the month and the last one of a weekday - alone, mixed
    /// with plain days, and in the months where they collide with a short February.
    /// </summary>
    private static string[] PerMonthDayExpressions()
    {
        return
        [
            "0 15 10 L * ?",
            "0 15 10 L-2 * ?",
            "0 15 10 L-5 * ?",
            "0 15 10 LW * ?",
            "0 15 10 LW-3 * ?",
            "0 15 10 L-4W * ?",
            "0 15 10 1W * ?",
            "0 15 10 15W * ?",
            "0 15 10 31W * ?",
            "0 15 10 1W,15W,L * ?",
            "0 15 10 ? * 6#3",
            "0 15 10 ? * 1#5",
            "0 15 10 ? * 7#1",
            "0 15 10 ? * 6L",
            "0 15 10 ? * 1L",
            "0 0/30 * L * ?",
            "0 0 0 L 2 ?",
            "0 0 0 15W,20 3,6,9 ?",
            "0 0 0 L-1W * ?",
            "0 0 12 10,L * ?"
        ];
    }

    private static string NextExpression(Random random)
    {
        string second = NextField(random, 0, 59, allowWildcard: true);
        string minute = NextField(random, 0, 59, allowWildcard: true);
        string hour = NextField(random, 0, 23, allowWildcard: true);
        string month = NextField(random, 1, 12, allowWildcard: true);

        string dayOfMonth;
        string dayOfWeek;
        switch (random.Next(3))
        {
            case 0:
                dayOfMonth = NextField(random, 1, 31, allowWildcard: true);
                dayOfWeek = "?";
                break;

            case 1:
                dayOfMonth = "?";
                dayOfWeek = NextField(random, 1, 7, allowWildcard: true);
                break;

            default:
                dayOfMonth = NextField(random, 1, 31, allowWildcard: true);
                dayOfWeek = NextField(random, 1, 7, allowWildcard: true);
                break;
        }

        string year = random.Next(4) == 0
            ? $" {WindowFirstYear - random.Next(0, 6)}-{WindowLastYear + random.Next(0, 6)}"
            : "";

        return $"{second} {minute} {hour} {dayOfMonth} {month} {dayOfWeek}{year}";
    }

    private static string NextField(Random random, int min, int max, bool allowWildcard)
    {
        int span = max - min + 1;

        switch (random.Next(allowWildcard ? 5 : 4))
        {
            case 0:
                return min.ToString(CultureInfo.InvariantCulture);

            case 1:
            {
                SortedSet<int> values = [];
                int count = random.Next(1, 5);
                for (int i = 0; i < count; i++)
                {
                    values.Add(random.Next(min, max + 1));
                }

                return string.Join(",", values);
            }

            case 2:
                // Ranges are allowed to run backwards through the top of the field, which is Quartz's
                // own extension and a shape the bitmask has to have got right at parse time.
                return $"{random.Next(min, max + 1)}-{random.Next(min, max + 1)}";

            case 3:
                return $"{random.Next(min, max + 1)}/{random.Next(1, span)}";

            default:
                return "*";
        }
    }
}
