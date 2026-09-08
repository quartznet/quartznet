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

using System.Diagnostics;
using System.Text;

namespace Quartz.Tests.Unit;

/// <summary>
/// <see cref="CronExpression.GetNextInvalidTimeAfter" /> reads its answer off the expression's field
/// sets. What it used to do was ask <see cref="CronExpression.GetNextValidTimeAfter" /> for one fire
/// after another and stop when two of them were more than a second apart, which is correct and costs a
/// full cron computation per second of the run — a century of them for an expression that fires at every
/// second (#3690).
/// </summary>
/// <remarks>
/// The old walk is the specification, so it is kept here as <see cref="ReferenceNextInvalidTimeAfter" />
/// and the two are compared over a corpus wide enough to reach every branch of the new search: the
/// complement of each field, the union of the two day fields, 'L', 'W' and '#', wrapping ranges, an
/// explicit year, and both daylight saving transitions in four zones. The one answer they do not share
/// is the one the issue is about: where the walk gave up and returned a time the expression <i>does</i>
/// fire at, the search says <see langword="null" />.
/// </remarks>
/// <author>Marko Lahma (.NET)</author>
public class CronExpressionNextInvalidTimeTest
{
    /// <summary>
    /// How far the reference walk goes before it gives up. Every second of it costs a cron computation,
    /// so this is what keeps a corpus of expressions that fire densely from taking minutes to check.
    /// Three days reaches past a day boundary, which is the coarsest step the search takes that any
    /// expression here can produce inside the horizon.
    /// </summary>
    private const int ReferenceWalkLimitSeconds = 3 * 24 * 60 * 60;

    /// <summary>
    /// The same budget for the daylight saving cases, where what is being checked is a few hours either
    /// side of one clock change rather than how far the search can skip.
    /// </summary>
    private const int ClockChangeWalkLimitSeconds = 4 * 60 * 60;

    /// <summary>
    /// And for the cases searched from an expression's own fire times, where the answer is a second or
    /// a minute away whenever there is one at all.
    /// </summary>
    private const int FireTimeWalkLimitSeconds = 60 * 60;

    /// <summary>
    /// The alternatives each field is drawn from to build the corpus. Every special form the day
    /// search has to resolve per month appears here, because those are the ones a bitmask cannot
    /// answer on its own.
    /// </summary>
    private static readonly string[][] FieldAlternatives =
    [
        ["0", "*", "0/15", "0-30", "5,10,45", "30", "*/20"],
        ["0", "*", "0/5", "15-45", "0,30", "59"],
        ["*", "12", "8-18", "0/6", "22-2", "0,23"],
        ["*", "?", "1", "15", "L", "LW", "15W", "L-2", "1-7", "1/10"],
        ["*", "1", "JAN-MAR", "*/3", "2,6,12", "NOV-FEB"],
        ["?", "*", "MON", "MON-FRI", "6#3", "6L", "2/2", "SUN,SAT", "FRI-MON"]
    ];

    /// <summary>
    /// Expressions worth naming rather than generating: the ones the benchmark measures, the ones the
    /// documentation teaches, and the two that have no invalid time at all.
    /// </summary>
    private static readonly string[] NamedExpressions =
    [
        "* * * * * ?",
        "* * * ? * *",
        "0 15 10 * * ?",
        "0 0 12 * * ?",
        "0 0/5 * * * ?",
        "0/15 * * * * ?",
        "0 0-30 9-17 * * ?",
        "0 0 8-18 ? * MON-FRI",
        "0 0,10,20,30,40,50 * * * ?",
        "0 15 10 1,2,3,4,5,10,15,20,25 * ? *",
        "0 15 10 L * ?",
        "0 15 10 L-2 * ?",
        "0 15 10 LW * ?",
        "0 15 10 ? * 6#3 *",
        "0 15 10 ? * 6L",
        "0 15 10 * * ? 2005-2025",
        "* * * * * ? 2026",
        "0 0 0 13 * FRI",
        "* 0-59 * * * ?",
        "* * 0-23 * * ?",
        "0 * * * * ?",
        "* 30 10 * * ?"
    ];

    /// <summary>
    /// The instants the corpus is searched from: a year boundary, a mid-day second with nothing round
    /// about it, the last second of a month, a leap day, and the last second of a year.
    /// </summary>
    private static readonly DateTimeOffset[] SearchInstants =
    [
        new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 3, 15, 13, 47, 23, TimeSpan.Zero),
        new DateTimeOffset(2026, 2, 28, 23, 59, 59, TimeSpan.Zero),
        new DateTimeOffset(2024, 2, 29, 12, 0, 0, TimeSpan.Zero),
        new DateTimeOffset(2026, 12, 31, 23, 59, 30, TimeSpan.Zero)
    ];

    public static IEnumerable<string> Corpus()
    {
        foreach (string expression in NamedExpressions)
        {
            yield return expression;
        }

        // A fixed seed, so a failure names an expression that can be run again.
        Random random = new Random(3690);
        HashSet<string> seen = [.. NamedExpressions];

        while (seen.Count < 220)
        {
            StringBuilder builder = new StringBuilder();
            for (int field = 0; field < FieldAlternatives.Length; field++)
            {
                if (field > 0)
                {
                    builder.Append(' ');
                }

                builder.Append(FieldAlternatives[field][random.Next(FieldAlternatives[field].Length)]);
            }

            string expression = builder.ToString();
            if (!seen.Add(expression))
            {
                continue;
            }

            yield return expression;
        }
    }

    /// <summary>
    /// The whole of the fix: for every expression the corpus can build, and from every instant it is
    /// asked about, the structural search and the second-by-second walk name the same second.
    /// </summary>
    [TestCaseSource(nameof(Corpus))]
    public void TheAnswerIsTheOneASecondBySecondWalkGives(string expression)
    {
        CronExpression cron = new CronExpression(expression, TimeZoneInfo.Utc);

        foreach (DateTimeOffset from in SearchInstants)
        {
            AssertAgreesWithTheWalk(cron, from);
        }
    }

    /// <summary>
    /// The same comparison from the instants that discriminate: an expression's own fire times, and the
    /// day, the week and the month after one of them. A clock reading that fires once is the reading a
    /// day-of-month, day-of-week, 'L', 'W' or '#' rule has an opinion about, so an answer read off those
    /// fields is only really tested where the calendar has moved under an otherwise identical reading —
    /// the same 10:15 on the following Friday, which is a different Friday of the month.
    /// </summary>
    [TestCaseSource(nameof(Corpus))]
    public void TheAnswerIsTheOneASecondBySecondWalkGivesAroundTheExpressionsOwnFireTimes(string expression)
    {
        CronExpression cron = new CronExpression(expression, TimeZoneInfo.Utc);
        DateTimeOffset? fire = cron.GetNextValidTimeAfter(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

        if (fire is null)
        {
            return;
        }

        foreach (DateTimeOffset from in FireTimeNeighbourhood(fire.Value))
        {
            AssertAgreesWithTheWalk(cron, from, FireTimeWalkLimitSeconds);
        }
    }

    private static IEnumerable<DateTimeOffset> FireTimeNeighbourhood(DateTimeOffset fire)
    {
        yield return fire;
        yield return fire.AddSeconds(1);
        yield return fire.AddSeconds(-1);
        yield return fire.AddDays(1);
        yield return fire.AddDays(7);
        yield return fire.AddMonths(1);
    }

    /// <summary>
    /// The same comparison where a clock reading and an instant are not the same thing. A fall-back
    /// replays an hour the search has already stepped through and a spring-forward gap swallows one it
    /// never reaches, so this is where a search that skips whole runs of clock readings can skip a
    /// second that real time does reach.
    /// </summary>
    [TestCaseSource(nameof(DaylightSavingCases))]
    public void TheAnswerIsTheOneASecondBySecondWalkGivesAcrossAClockChange(TimeZoneInfo zone, string expression, DateTimeOffset from)
    {
        CronExpression cron = new CronExpression(expression, zone);

        AssertAgreesWithTheWalk(cron, from, ClockChangeWalkLimitSeconds);
    }

    public static IEnumerable<TestCaseData> DaylightSavingCases()
    {
        (TimeZoneInfo Zone, DateTimeOffset SpringForward, DateTimeOffset FallBack)[] zones =
        [
            // the transition instants themselves, in UTC
            (TestTimeZones.Eastern, new DateTimeOffset(2024, 3, 10, 7, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 11, 3, 6, 0, 0, TimeSpan.Zero)),
            (TestTimeZones.Helsinki, new DateTimeOffset(2024, 3, 31, 1, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 10, 27, 1, 0, 0, TimeSpan.Zero)),
            (TestTimeZones.Sydney, new DateTimeOffset(2024, 10, 5, 16, 0, 0, TimeSpan.Zero), new DateTimeOffset(2024, 4, 6, 16, 0, 0, TimeSpan.Zero)),
            (TestTimeZones.Santiago, new DateTimeOffset(2019, 9, 8, 4, 0, 0, TimeSpan.Zero), new DateTimeOffset(2019, 4, 7, 3, 0, 0, TimeSpan.Zero))
        ];

        string[] expressions =
        [
            "* * * * * ?",          // fires at every second there is, so the run is real time itself
            "* * 1-4 * * ?",        // an interval expression whose run ends inside the transition's day
            "0 30 2 * * ?",         // the fixed time the gap swallows in the classic zones
            "0 0 3 * * ?",          // and the one the gap ends on
            "* 0-30 * * * ?",       // a run that ends every hour, so it straddles the transition itself
            "0 0 0 * * ?",          // midnight, which is the transition in Santiago
            "* * * ? * SAT,SUN"     // whole days, which is the coarsest skip the search can take
        ];

        foreach ((TimeZoneInfo zone, DateTimeOffset springForward, DateTimeOffset fallBack) in zones)
        {
            foreach (string expression in expressions)
            {
                foreach (DateTimeOffset transition in new[] { springForward, fallBack })
                {
                    // an hour before the clocks move, on the move itself, and a second after
                    foreach (TimeSpan offset in new[] { TimeSpan.FromHours(-1), TimeSpan.Zero, TimeSpan.FromSeconds(1) })
                    {
                        yield return new TestCaseData(zone, expression, transition + offset)
                            .SetArgDisplayNames(zone.Id, expression, (transition + offset).ToString("O"));
                    }
                }
            }
        }
    }

    /// <summary>
    /// The issue: <c>* * * * * ?</c> excludes every instant, so there is no next invalid time. The walk
    /// spent one cron computation per second looking for one — some three billion of them — and then
    /// returned a time the expression fires at anyway.
    /// </summary>
    [Test]
    public void AnExpressionThatFiresAtEverySecondHasNoInvalidTime()
    {
        CronExpression cron = new CronExpression("* * * * * ?", TimeZoneInfo.Utc);

        Stopwatch stopwatch = Stopwatch.StartNew();
        DateTimeOffset? next = cron.GetNextInvalidTimeAfter(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
        stopwatch.Stop();

        next.Should().BeNull("the expression fires at every second, so no second after the search instant is invalid");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
            "the answer is read off the fields; walking to the give-up year to find it took minutes");
    }

    /// <summary>
    /// The same, one field at a time: an expression naming every value of every field it has to name
    /// has no invalid time either, however it is spelled.
    /// </summary>
    [TestCase("* * * * * ?", "the plain spelling")]
    [TestCase("* * * ? * *", "'?' restricts nothing, so this is the same expression")]
    [TestCase("0-59 0-59 0-23 * * ?", "ranges covering every value are wildcards written out")]
    [TestCase("*/1 */1 */1 */1 */1 ?", "and so is a step of one")]
    [TestCase("* * * 1-31 1-12 ?", "every day of every month")]
    public void EveryWayOfNamingEverySecondAnswersNull(string expression, string reason)
    {
        CronExpression cron = new CronExpression(expression, TimeZoneInfo.Utc);

        cron.GetNextInvalidTimeAfter(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero)).Should().BeNull(reason);
    }

    /// <summary>
    /// The ordinary answer, which is the one the calendar is normally asking for: a single fire is over
    /// after a second, and the next second is the answer.
    /// </summary>
    [TestCase("0 0 12 * * ?", "2026-06-01T00:00:00Z", "2026-06-01T00:00:00Z", "the search instant does not fire, so it is its own answer")]
    [TestCase("0 0 12 * * ?", "2026-06-01T12:00:00Z", "2026-06-01T12:00:01Z", "the fire lasts one second")]
    [TestCase("0 0 12 * * ?", "2026-06-01T11:59:59.500Z", "2026-06-01T11:59:59Z", "milliseconds are truncated, and that second does not fire")]
    [TestCase("* 0 12 * * ?", "2026-06-01T12:00:30Z", "2026-06-01T12:01:00Z", "a whole minute of fires ends when the minute does")]
    [TestCase("* * 12 * * ?", "2026-06-01T12:30:00Z", "2026-06-01T13:00:00Z", "a whole hour of fires ends when the hour does")]
    [TestCase("* * * 1 * ?", "2026-06-01T12:30:00Z", "2026-06-02T00:00:00Z", "a whole day of fires ends at midnight")]
    [TestCase("* * * ? * MON", "2026-06-01T12:30:00Z", "2026-06-02T00:00:00Z", "and a whole Monday likewise")]
    [TestCase("* * * ? 6 *", "2026-06-15T12:30:00Z", "2026-07-01T00:00:00Z", "a whole June of fires ends when July starts")]
    [TestCase("* * * * * ? 2026", "2026-06-15T12:30:00Z", "2027-01-01T00:00:00Z", "and a whole year when the year does")]
    public void TheFirstSecondAfterTheRunIsTheAnswer(string expression, string from, string expected, string reason)
    {
        CronExpression cron = new CronExpression(expression, TimeZoneInfo.Utc);

        cron.GetNextInvalidTimeAfter(DateTimeOffset.Parse(from)).Should().Be(DateTimeOffset.Parse(expected), reason);
    }

    /// <summary>
    /// A run that has to be stepped over a month at a time, which is the coarsest skip there is, and the
    /// one that used to take longest: every second of every February fires, so the answer is a year away.
    /// </summary>
    [Test]
    public void ARunSteppedOverAMonthAtATimeStillAnswersInBoundedTime()
    {
        CronExpression cron = new CronExpression("* * * ? 2 *", TimeZoneInfo.Utc);

        Stopwatch stopwatch = Stopwatch.StartNew();
        DateTimeOffset? next = cron.GetNextInvalidTimeAfter(new DateTimeOffset(2026, 2, 1, 0, 0, 0, TimeSpan.Zero));
        stopwatch.Stop();

        next.Should().Be(new DateTimeOffset(2026, 3, 1, 0, 0, 0, TimeSpan.Zero), "February's last second is the run's last");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromMilliseconds(100),
            "2.4 million seconds of fires is the kind of run the walk could not step over");
    }

    /// <summary>
    /// Past the give-up year nothing fires, so the search instant is its own answer — which is what the
    /// walk said too, by way of <see cref="CronExpression.GetNextValidTimeAfter" /> returning null.
    /// </summary>
    [Test]
    public void NothingFiresPastTheGiveUpYear()
    {
        CronExpression cron = new CronExpression("* * * * * ?", TimeZoneInfo.Utc);
        DateTimeOffset pastTheEnd = new DateTimeOffset(TriggerConstants.YearToGiveUpSchedulingAt + 1, 6, 1, 12, 0, 0, TimeSpan.Zero);

        cron.GetNextInvalidTimeAfter(pastTheEnd).Should().Be(pastTheEnd,
            "the expression schedules nothing that far out, so the instant asked about is already invalid");
    }

    /// <summary>
    /// An expression whose years have run out has no fires left at all, so every second after them is
    /// invalid — including the one asked about.
    /// </summary>
    [Test]
    public void AnExpressionWhoseYearsHaveRunOutIsInvalidEverywhereAfterThem()
    {
        CronExpression cron = new CronExpression("0 0 12 * * ? 2020", TimeZoneInfo.Utc);
        DateTimeOffset after = new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

        cron.GetNextInvalidTimeAfter(after).Should().Be(after, "2020 is over, so this expression fires never again");
    }

    private static void AssertAgreesWithTheWalk(CronExpression cron, DateTimeOffset from, int limitSeconds = ReferenceWalkLimitSeconds)
    {
        DateTimeOffset? walked = ReferenceNextInvalidTimeAfter(cron, from, limitSeconds, out bool gaveUp);
        DateTimeOffset? searched = cron.GetNextInvalidTimeAfter(from);

        string context = $"'{cron.CronExpressionString}' in {cron.TimeZone.Id} searched from {from:O}";

        if (gaveUp)
        {
            // The walk found nothing within its budget, which is the case the fix is about: the search
            // either says there is no such time or names one past where the walk stopped looking.
            (searched is null || searched.Value >= from.AddSeconds(limitSeconds)).Should().BeTrue(
                $"{context}: the walk saw nothing but fires for {limitSeconds} seconds, so the search must not name one inside them, but it named {searched:O}");
            return;
        }

        searched.Should().Be(walked, $"{context}: the structural search and the second-by-second walk are the same answer");
    }

    /// <summary>
    /// <see cref="CronExpression.GetNextInvalidTimeAfter" /> as it was before #3690: ask for one fire
    /// after another, and stop when two are more than a second apart. Bounded here by
    /// <see cref="ReferenceWalkLimitSeconds" />, which is what the original lacked.
    /// </summary>
    private static DateTimeOffset? ReferenceNextInvalidTimeAfter(CronExpression expression, DateTimeOffset date, int limitSeconds, out bool gaveUp)
    {
        gaveUp = false;
        long difference = 1000;

        DateTimeOffset start = new DateTimeOffset(date.Year, date.Month, date.Day, date.Hour, date.Minute, date.Second, date.Offset);
        DateTimeOffset lastDate = start.AddSeconds(-1);

        while (difference == 1000)
        {
            DateTimeOffset? newDate = expression.GetNextValidTimeAfter(lastDate);

            if (newDate is null)
            {
                break;
            }

            difference = (long) (newDate.Value - lastDate).TotalMilliseconds;

            if (difference == 1000)
            {
                lastDate = newDate.Value;

                if ((lastDate - start).TotalSeconds >= limitSeconds)
                {
                    gaveUp = true;
                    return null;
                }
            }
        }

        return lastDate.AddSeconds(1);
    }
}
