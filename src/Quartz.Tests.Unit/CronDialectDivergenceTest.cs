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

namespace Quartz.Tests.Unit;

/// <summary>
/// The two ways a six-field expression written for a crontab-derived .NET library — Cronos, NCrontab —
/// means something else here, and the rows that mean the same thing.
/// </summary>
/// <remarks>
/// <para>
/// Both divergences are documented rather than fixed (#3706): a bare numeric day-of-week is one day out
/// because Quartz numbers <c>1-7</c> from Sunday and those libraries number <c>0-6</c> from Sunday, and
/// an expression restricting both day fields fires on their union here and on their intersection there.
/// Neither can be detected — a six-field expression from either dialect is a perfectly valid Quartz
/// expression — so what this fixture does is hold the published table to the parser.
/// </para>
/// <para>
/// The instants are the ones <c>cron-expressions.md</c> and the migration guide print, measured against
/// Cronos 0.11.0. A change here is a change to a documented table, and to somebody's schedule.
/// </para>
/// </remarks>
[TestFixture]
public class CronDialectDivergenceTest
{
    /// <summary>The instant both published tables search from.</summary>
    private static readonly DateTimeOffset SearchFrom = new(2026, 8, 21, 0, 0, 0, TimeSpan.Zero);

    [TestCase("0 0 2 * * MON", "2026-08-24T02:00:00Z", "a named day means the same day in every dialect")]
    [TestCase("0 0 7 * * *", "2026-08-21T07:00:00Z", "no day field restricts anything, so this is 07:00 daily either way")]
    [TestCase("0 0 */1 * * *", "2026-08-21T01:00:00Z", "an hourly step is a step in both")]
    [TestCase("0 */15 * * * *", "2026-08-21T00:15:00Z", "and so is a quarter-hourly one")]
    [TestCase("0 30 6 * * MON,TUE,WED,THU,FRI", "2026-08-21T06:30:00Z", "a list of names carries across; 21 August 2026 is a Friday")]
    public void AnExpressionFromACrontabDerivedLibraryUsuallyMeansTheSameThing(string expression, string expected, string because)
    {
        NextFireAfterTheSearchInstant(expression).Should().Be(DateTimeOffset.Parse(expected), because);
    }

    /// <summary>
    /// The numbering: the same digit is a different day.
    /// </summary>
    [Test]
    public void ANumericDayOfWeekIsOneDayEarlierThanACrontabDerivedLibraryMeansIt()
    {
        DateTimeOffset? next = NextFireAfterTheSearchInstant("0 0 2 * * 1");

        next.Should().Be(
            new DateTimeOffset(2026, 8, 23, 2, 0, 0, TimeSpan.Zero),
            "Quartz numbers the days 1-7 with Sunday 1, so '1' is Sunday - where Cronos and NCrontab "
            + "number 0-6 with Sunday 0 and read the same digit as Monday, 2026-08-24");

        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Sunday, "which is the whole point of the row");
    }

    /// <summary>
    /// The day rule: both fields restrict, and Quartz takes their union.
    /// </summary>
    [Test]
    public void BothDayFieldsRestrictedFiresOnTheirUnionRatherThanTheirIntersection()
    {
        DateTimeOffset? next = NextFireAfterTheSearchInstant("0 0 2 5 * MON");

        next.Should().Be(
            new DateTimeOffset(2026, 8, 24, 2, 0, 0, TimeSpan.Zero),
            "'the 5th, and every Monday' - crontab's rule, which the first Monday after the search "
            + "instant satisfies. Cronos ANDs the two fields and would answer 2026-10-05, the next 5th "
            + "that is a Monday");

        next!.Value.DayOfWeek.Should().Be(DayOfWeek.Monday);

        // Past the last Monday of August, so the next Monday is 7 September and the next 5th is earlier.
        NextFireAfterTheSearchInstant("0 0 2 5 * MON", after: new DateTimeOffset(2026, 8, 31, 3, 0, 0, TimeSpan.Zero))
            .Should().Be(
                new DateTimeOffset(2026, 9, 5, 2, 0, 0, TimeSpan.Zero),
                "and the day-of-month half fires on its own - 5 September 2026 is a Saturday, which the "
                + "intersection reading would skip");
    }

    private static DateTimeOffset? NextFireAfterTheSearchInstant(string expression, DateTimeOffset? after = null)
    {
        return new CronExpression(expression, TimeZoneInfo.Utc).GetNextValidTimeAfter(after ?? SearchFrom);
    }
}
