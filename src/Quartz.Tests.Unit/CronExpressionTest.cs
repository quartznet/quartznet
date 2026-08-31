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

using System.Collections;
using System.Diagnostics;
using System.Globalization;

using Newtonsoft.Json;

using Quartz.Impl;

namespace Quartz.Tests.Unit;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
[NonParallelizable]
public class CronExpressionTest : SerializationTestSupport<CronExpression>
{
    private static readonly TimeZoneInfo testTimeZone = TimeZoneInfo.Local;

    public CronExpressionTest(Type serializerType) : base(serializerType)
    {
    }

    /// <summary>
    /// Get the object to serialize when generating serialized file for future
    /// tests, and against which to validate deserialized object.
    /// </summary>
    /// <returns></returns>
    protected override CronExpression GetTargetObject()
    {
        CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005", testTimeZone);

        return cronExpression;
    }

    protected override void VerifyMatch(CronExpression original, CronExpression deserialized)
    {
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.CronExpressionString, Is.EqualTo(original.CronExpressionString));
            Assert.That(deserialized.TimeZone, Is.EqualTo(original.TimeZone));
        });
    }

    /// <summary>
    /// Test method for 'CronExpression.IsSatisfiedBy(DateTime)'.
    /// </summary>
    [Test]
    public void TestIsSatisfiedBy()
    {
        CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005");

        DateTime date = new DateTime(2005, 6, 1, 10, 15, 0).ToUniversalTime();
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.True);

        date = date.AddYears(1);
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.False);

        date = new DateTime(2005, 6, 1, 10, 16, 0).ToUniversalTime();
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.False);

        date = new DateTime(2005, 6, 1, 10, 14, 0).ToUniversalTime();
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.False);

        cronExpression = new CronExpression("0 15 10 ? * MON-FRI");

        // weekends
        date = new DateTime(2007, 6, 9, 10, 15, 0).ToUniversalTime();
        Assert.Multiple(() =>
        {
            Assert.That(cronExpression.IsSatisfiedBy(date), Is.False);
            Assert.That(cronExpression.IsSatisfiedBy(date.AddDays(1)), Is.False);
        });
    }

    [Test]
    public void TestLastDayOffset()
    {
        CronExpression cronExpression = new CronExpression("0 15 10 L-2 * ? 2010");

        DateTime date = new DateTime(2010, 10, 29, 10, 15, 0).ToUniversalTime(); // last day - 2
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.True);

        date = new DateTime(2010, 10, 28, 10, 15, 0).ToUniversalTime();
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.False);

        cronExpression = new CronExpression("0 15 10 L-5W * ? 2010");

        date = new DateTime(2010, 10, 26, 10, 15, 0).ToUniversalTime(); // last day - 5
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.True);

        cronExpression = new CronExpression("0 15 10 L-1 * ? 2010");

        date = new DateTime(2010, 10, 30, 10, 15, 0).ToUniversalTime(); // last day - 1
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.True);

        cronExpression = new CronExpression("0 15 10 L-1W * ? 2010");

        date = new DateTime(2010, 10, 29, 10, 15, 0).ToUniversalTime(); // nearest weekday to last day - 1 (29th is a friday in 2010)
        Assert.That(cronExpression.IsSatisfiedBy(date), Is.True);
    }

    [TestCase("0 15 10 6,15 * ? 2010", "0 15 10 6,15 * ? 2010")]
    public void ExpressionToString(string cronExpression, string expected)
    {
        var expr = new CronExpression(cronExpression);
        expr.ToString().Should().Be(expected);
    }

    [TestCase("0 15 10 L-1,L-2 * ? 2010", new[] { 31 - 1, 31 - 2 })] // multiple L offsets
    [TestCase("0 15 10 L,L-1,L-2 * ? 2010", new[] { 31, 30, 29 })]
    [TestCase("0 15 10 L-1W,L-1 * ? 2010", new[] { 29, 30 })] // last-1 (30th, Sat) and its nearest weekday (29th, Fri)
    public void CanUseMultipleLastDayOfMonthInArray(string cronExpression, int[] expectedDays, string scenario = "")
    {
        // Multiple 'L' instances are now supported; each contributes a candidate day.
        var expr = new CronExpression(cronExpression); //10:15am <variable days> October 2010

        foreach (var expectedDay in expectedDays)
        {
            var date = new DateTime(2010, 10, expectedDay, 10, 15, 0).ToUniversalTime();
            expr.IsSatisfiedBy(date).Should().BeTrue($"expected day of {expectedDay}, {scenario}");
        }
    }

    [TestCase("0 15 10 6,15,LW * ? 2010", new[] { 6, 15, 29 })] //31 oct 2010 is a Sunday, week day would be 29
    [TestCase("0 15 10 6,15,L * ? 2010", new[] { 6, 15, 31 })]
    [TestCase("0 15 10 1,L * ? 2010", new[] { 1, 31 })]
    [TestCase("0 15 10 15,L * ? 2010", new[] { 15, 31 })]
    [TestCase("0 15 10 15,31 * ? 2010", new[] { 15, 31 })]
    [TestCase("0 15 10 15,L-2 * ? 2010", new[] { 15, 31 - 2 })]
    [TestCase("0 15 10 31,L-2 * ? 2010", new[] { 29, 31 }, "explicit day + last-2 both fire")]
    [TestCase("0 15 10 1,5,29,L * ? 2010", new[] { 1, 5, 29, 31 }, "QUARTZ-640: previously rejected")]
    [TestCase("0 15 10 1,3,6,15,L * ? 2010", new[] { 1, 3, 6, 15, 31 })]
    [TestCase("0 15 10 15,LW-2 * ? 2010", new[] { 15, 29 - 2 })] //29 is last week day
    [TestCase("0 15 10 2W,16 * ? 2010", new[] { 1, 16 })] // 2nd is a Saturday, nearest weekday is the 1st
    [TestCase("0 15 10 16W,2 * ? 2010", new[] { 2, 15 })] // each W shifts its own day: 16W (Sat)->Fri 15; plain 2 fires raw
    [TestCase("0 15 10 2W,16W * ? 2010", new[] { 1, 15 }, "two nearest-weekday days")] // 2W->1, 16W->15
    public void CanUseLastDayOfMonthInArray(string cronExpression, int[] expectedDays, string scenario = "")
    {
        var expr = new CronExpression(cronExpression); //10:15am <variable days> October 2010

        foreach (var expectedDay in expectedDays)
        {
            var date = new DateTime(2010, 10, expectedDay, 10, 15, 0).ToUniversalTime(); // last day
            expr.IsSatisfiedBy(date).Should().BeTrue($"expected day of {expectedDay}, {scenario}");
        }
    }

    [Test]
    public void TestSerializationRoundTripWithLastDayAndWeekday()
    {
        var original = new CronExpression("0 15 10 1,L-1,LW,2W * ? 2010");

        var data = serializer.Serialize(original);
        var deserialized = serializer.Deserialize<CronExpression>(data);

        // The parsed state (lastDaySpecs, nearestWeekdays) is [NonSerialized] and
        // rebuilt from the expression string, so firing behaviour must survive the
        // round-trip through each serializer.
        foreach (var day in new[] { 1, 29, 30 }) // plain 1 / 2W->1; LW->29; L-1->30
        {
            var date = new DateTime(2010, 10, day, 10, 15, 0).ToUniversalTime();
            deserialized.IsSatisfiedBy(date).Should().BeTrue($"day {day} should fire after round-trip");
        }

        deserialized.IsSatisfiedBy(new DateTime(2010, 10, 3, 10, 15, 0).ToUniversalTime()).Should().BeFalse();
    }

    [Test]
    public void TestZeroNearestWeekdayIsRejectedAtParse()
    {
        // "0W" must fail at construction, not crash later in GetNextValidTimeAfter.
        Action act = () => new CronExpression("0 15 10 0W * ?");
        act.Should().Throw<FormatException>();
    }

    private int[] CreateArrayOfDays(int year, int month)
    {
        var daysInMonth = DateTime.DaysInMonth(year, month);
        var numbers = new List<int>();
        for (var i = 0; i < daysInMonth; i++)
        {
            numbers.Add(i + 1);
        }

        return numbers.ToArray();
    }

    /// <summary>
    /// The whole 2x2 of the day rule. A day field written exactly '*' or '?' names no days, so it
    /// restricts nothing and the other field decides; only when both fields name days are the two
    /// unioned. October 2010 starts on a Friday and ends on a Sunday, which is what makes every
    /// weekday form below land on a day the reason string can spell.
    /// </summary>
    // Both fields name days: the expression fires on the union of the two.
    [TestCase("0 15 10 5/5 * MON 2010", new[] { 4, 11, 18, 25, 5, 10, 15, 20, 25, 30 }, "10:15am every 5th day of the month from 5 to 31, and on Mondays in October 2010")]
    [TestCase("0 15 10 3 * MON,THU,FRI 2010", new[] { 1, 3, 4, 11, 18, 25, 7, 14, 21, 28, 8, 15, 22, 29 }, "10:15am 3rd of month and every mon,thu,fri October 2010")]
    [TestCase("0 15 10 1,2,3,4,5,6 * MON,THU,FRI 2010", new[] { 1, 2, 3, 4, 5, 6, 11, 18, 25, 7, 14, 21, 28, 8, 15, 22, 29 }, "10:15am 1-6th of mon and every Mon,Thu,Fri October 2010")]

    // One field names days and the other does not: the one that does decides.
    [TestCase("0 15 10 * * MON,THU,FRI 2010", new[] { 1, 4, 7, 8, 11, 14, 15, 18, 21, 22, 25, 28, 29 },
        "10:15am every Mon, Thu, Fri in October 2010 - a wildcard day-of-month restricts nothing, so day-of-week decides")]
    [TestCase("0 15 10 1 * * 2010", new[] { 1 },
        "10:15am on the 1st of October 2010 - a wildcard day-of-week restricts nothing, so day-of-month decides")]
    [TestCase("0 15 10 ? * MON,THU,FRI 2010", new[] { 1, 4, 7, 8, 11, 14, 15, 18, 21, 22, 25, 28, 29 },
        "'?' and '*' say the same thing, so this is the wildcard day-of-month case spelled the older way")]
    [TestCase("0 15 10 1 * ? 2010", new[] { 1 },
        "'?' and '*' say the same thing, so this is the wildcard day-of-week case spelled the older way")]

    // Neither field names days: every day.
    [TestCase("0 15 10 * * ? 2010", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 },
        "neither field restricts")]
    [TestCase("0 15 10 ? * * 2010", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 },
        "neither field restricts")]
    [TestCase("0 15 10 * * * 2010", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 },
        "two wildcards spelled the same way")]
    [TestCase("0 15 10 ? * ? 2010", new[] { 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16, 17, 18, 19, 20, 21, 22, 23, 24, 25, 26, 27, 28, 29, 30, 31 },
        "two wildcards spelled the same way")]

    // The special day forms name days, so they restrict, and the wildcard opposite them yields.
    [TestCase("0 15 10 L * * 2010", new[] { 31 }, "'L' restricts, so the wildcard day-of-week yields")]
    [TestCase("0 15 10 15W * * 2010", new[] { 15 }, "'W' restricts - 15 Oct 2010 is a Friday, so it needs no shift")]
    [TestCase("0 15 10 * * 6#3 2010", new[] { 15 }, "'#' restricts - the third Friday of October 2010")]
    [TestCase("0 15 10 * * 6L 2010", new[] { 29 }, "'dL' restricts - the last Friday of October 2010")]
    public void CanUse_DayOfMonth_And_DayOfWeek_Together(string cronExpression, int[] expectedDays, string scenario = "")
    {
        var expr = new CronExpression(cronExpression, TimeZoneInfo.Utc);
        var templateDate = new DateTime(2010, 10, 1, 10, 15, 0, DateTimeKind.Utc);

        foreach (var day in expectedDays)
        {
            var date = new DateTime(templateDate.Year, templateDate.Month, day, templateDate.Hour, templateDate.Minute, templateDate.Second, templateDate.Kind);
            expr.IsSatisfiedBy(date).Should().BeTrue($"expected day of {day}, {scenario}");
        }

        var invalidDays = CreateArrayOfDays(2010, 10).Except(expectedDays);

        foreach (var day in invalidDays)
        {
            var date = new DateTime(templateDate.Year, templateDate.Month, day, templateDate.Hour, templateDate.Minute, templateDate.Second, templateDate.Kind);
            expr.IsSatisfiedBy(date).Should().BeFalse($"invalid day of {day}, {scenario}");
        }
    }

    [TestCase("0 15 10 LW-2 * ? 2010", 27, "31 Oct 2010 is Sunday, last-weekday (LW) is 29 (FRI) -2 Offset")]
    [TestCase("0 15 10 LW-5 * ? 2010", 24, "31 Oct 2010 is Sunday, last-weekday (LW) is 29 (FRI) -5 Offset")]
    [TestCase("0 15 10 LW-7 * ? 2010", 22, "31 Oct 2010 is Sunday, last-weekday (LW) is 29 (FRI) -7 Offset")]
    [TestCase("0 15 10 LW-28 * ? 2010", 1, "31 Oct 2010 is Sunday, last-weekday (LW) is 29 (FRI) -28 Offset")]
    [TestCase("0 15 10 LW-29 * ? 2010", 1, "31 Oct 2010 is Sunday, last-weekday (LW) is 29 (FRI) -29 Offset fallback to 1st of month")]
    [TestCase("0 15 10 LW-30 * ? 2010", 1, "31 Oct 2010 is Sunday, last-weekday (LW) is 29 (FRI) -30 Offset fallback to 1st of month")]
    public void LastWeekDayWithOffset(string cronExpression, int expectedDay, string reason)
    {
        var expr = new CronExpression(cronExpression);
        var date = new DateTime(2010, 10, expectedDay, 10, 15, 0).ToUniversalTime(); // last day
        expr.IsSatisfiedBy(date).Should().BeTrue(reason);
    }

    [TestCase("0 15 10 ? * 1#0 2010", false)]
    [TestCase("0 15 10 ? * 1#1 2010", true)]
    [TestCase("0 15 10 ? * 1#2 2010", true)]
    [TestCase("0 15 10 ? * 1#3 2010", true)]
    [TestCase("0 15 10 ? * 1#4 2010", true)]
    [TestCase("0 15 10 ? * 1#5 2010", true)]
    [TestCase("0 15 10 ? * 1#6 2010", false)]

    public void Ensure_NthWeek_IsBetween1And5(string expression, bool isValid)
    {
        Action act = () => new CronExpression(expression); //10:15am <variable days> October 2010
        if (isValid)
        {
            act.Should().NotThrow();
        }
        else
        {
            act.Should().Throw<FormatException>();
        }
    }

    [TestCase("0 15 10 ? * 0#1 2010", false)]
    [TestCase("0 15 10 ? * 1#1 2010", true, "2010-01-03T10:15:00")]
    [TestCase("0 15 10 ? * 2#1 2010", true, "2010-01-04T10:15:00")]
    [TestCase("0 15 10 ? * 3#1 2010", true, "2010-01-05T10:15:00")]
    [TestCase("0 15 10 ? * 4#1 2010", true, "2010-01-06T10:15:00")]
    [TestCase("0 15 10 ? * 5#1 2010", true, "2010-01-07T10:15:00")]
    [TestCase("0 15 10 ? * 6#1 2010", true, "2010-01-01T10:15:00")]
    [TestCase("0 15 10 ? * 7#1 2010", true, "2010-01-02T10:15:00")]
    [TestCase("0 15 10 ? * 8#1 2010", false)]
    [TestCase("0 15 10 ? * 14#1 2010", false)]

    public void Ensure_NthWeek_Day_IsBetween1And7(string expression, bool isValid, string shouldSatisfyDate = null)
    {
        Action act = () => new CronExpression(expression);
        if (isValid)
        {
            act.Should().NotThrow();
            var exp = new CronExpression(expression);
            if (!string.IsNullOrEmpty(shouldSatisfyDate))
            {
                var dt = DateTime.Parse(shouldSatisfyDate);
                exp.IsSatisfiedBy(new DateTimeOffset(dt)).Should().BeTrue();
            }
        }
        else
        {
            act.Should().Throw<FormatException>();
        }
    }

    [TestCase("0 15 10 6,15,LW * ? 2010")]
    [TestCase("0 15 10 6,15,L * ? 2010")]
    [TestCase("0 15 10 15,L * ? 2010")]
    [TestCase("0 15 10 15,31 * ? 2010")]
    [TestCase("0 15 10 15,L-2 * ? 2010")]
    [TestCase("0 15 10 31,L-2 * ? 2010")]
    [TestCase("0 15 10 1,3,6,15,L * ? 2010")]
    public void ExpressionEquality(string expression)
    {
        var expr1 = new CronExpression(expression);
        var expr2 = new CronExpression(expression);
        expr1.Equals(expr2).Should().BeTrue();

        expr1.Equals((object) expr2).Should().BeTrue();
        expr1.GetHashCode().Should().Be(expr2.GetHashCode());
    }

    [Test]
    public void EqualityIsNullSafe()
    {
        CronExpression expr = new CronExpression("0 15 10 * * ?");

        expr.Equals((CronExpression) null).Should().BeFalse();
        expr.Equals((object) null).Should().BeFalse();
        expr.Equals("0 15 10 * * ?").Should().BeFalse("a string is not a CronExpression");
    }

    [Test]
    public void TryParseAcceptsAValidExpression()
    {
        CronExpression.TryParse("0 15 10 * * ?", out CronExpression parsed).Should().BeTrue();
        parsed.CronExpressionString.Should().Be("0 15 10 * * ?");
    }

    [TestCase(null)]
    [TestCase("")]
    [TestCase("not a cron expression")]
    [TestCase("0 0 15 ? * FRI*")]
    public void TryParseRejectsAnInvalidExpression(string expression)
    {
        CronExpression.TryParse(expression, out CronExpression parsed).Should().BeFalse();
        parsed.Should().BeNull();
    }

    [Test]
    public void ParseThrowsForNullAndForGarbage()
    {
        Action parseNull = () => CronExpression.Parse(null);
        parseNull.Should().Throw<ArgumentNullException>();

        Action parseGarbage = () => CronExpression.Parse("not a cron expression");
        parseGarbage.Should().Throw<FormatException>();
    }

    [Test]
    public void IsParsable()
    {
        CronExpression parsed = ParseThrough<CronExpression>("0 15 10 * * ?");
        parsed.CronExpressionString.Should().Be("0 15 10 * * ?");

        TryParseThrough<CronExpression>("garbage", out CronExpression failed).Should().BeFalse();
        failed.Should().BeNull();

        static T ParseThrough<T>(string s) where T : IParsable<T> => T.Parse(s, CultureInfo.InvariantCulture);
        static bool TryParseThrough<T>(string s, out T result) where T : IParsable<T> => T.TryParse(s, CultureInfo.InvariantCulture, out result);
    }

    [Test]
    public void WithTimeZoneReturnsARetimedCopyAndLeavesTheOriginalAlone()
    {
        CronExpression original = new CronExpression("0 15 10 * * ?");

        CronExpression retimed = original.WithTimeZone(TimeZoneInfo.Utc);

        retimed.Should().NotBeSameAs(original, "CronExpression is immutable; retiming produces a copy");
        retimed.TimeZone.Should().Be(TimeZoneInfo.Utc);
        retimed.CronExpressionString.Should().Be(original.CronExpressionString);
        original.TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    [Test]
    public void WithTimeZoneNullMeansTheLocalTimeZone()
    {
        CronExpression utc = new CronExpression("0 15 10 * * ?", TimeZoneInfo.Utc);

        CronExpression local = utc.WithTimeZone(null);

        local.TimeZone.Should().Be(TimeZoneInfo.Local);
    }

    [Test]
    public void WithTimeZoneReturnsTheSameInstanceWhenNothingChanges()
    {
        CronExpression utc = new CronExpression("0 15 10 * * ?", TimeZoneInfo.Utc);

        utc.WithTimeZone(TimeZoneInfo.Utc).Should().BeSameAs(utc, "an immutable value can be shared when the zone is unchanged");
    }

    [TestCase("0 15 10 15,L-31 * ? 2010")]
    public void OffSetValue_CannontBe_GreaterThan30(string expression)
    {
        Action act = () => new CronExpression(expression);
        act.Should().Throw<FormatException>()
            .WithMessage("Offset from last day must be <= 30");
    }

    [TestCase("L 15 10 15 * ? 2010", false)]
    [TestCase("0 L 10 15 * ? 2010", false)]
    [TestCase("0 15 L 15 * ? 2010", false)]
    [TestCase("0 15 10 L * ? 2010", true, "Valid for day of month")]
    [TestCase("0 15 10 15 L ? 2010", false)]
    [TestCase("0 15 10 ? * L 2010", true, "Valid for day of week")]
    [TestCase("0 15 10 15 * ? L", false)]
    public void Ensure_L_Token_CanOnlyBeUsedIn_DayOfWeek_ORDayOfMonth(string expression, bool isValid, string description = "")
    {
        Action act = () => new CronExpression(expression);
        if (isValid)
            act.Should().NotThrow(description);
        else
            act.Should().Throw<FormatException>(description);
    }

    [Test]
    public void FiveFieldUnixExpressionsAreRejectedWithGuidance()
    {
        Action act = () => new CronExpression("30 4 * * 1");

        act.Should().Throw<FormatException>()
            .WithMessage("*Unix/crontab*", "the most common first-run failure is a 5-field expression copied from crontab or Kubernetes")
            .WithMessage("*\"0 30 4 * * 1\"*", "the error must show the fixed expression, not just the constraint")
            .WithMessage("*days of week 1-7*", "a Unix expression's numeric day-of-week also means a different day in Quartz")
            .WithMessage("*\"0 30 4 ? * MON\"*",
                "prepending a seconds field keeps the digit, and '1' is Monday in crontab but Sunday in Quartz, so advice that stops at the rewrite moves the schedule by a day");
    }

    [TestCase("30 4 1 * 1", "\"0 30 4 1 * MON\"", "a day-of-month the crontab named is carried over as written")]
    [TestCase("0 12 * * 7", "\"0 0 12 ? * SUN\"", "Unix spells Sunday both 0 and 7, and Quartz spells it 1")]
    public void TheFiveFieldMessageRenumbersTheDayOfWeekItWasGiven(string expression, string expectedInMessage, string reason)
    {
        Action act = () => new CronExpression(expression);

        act.Should().Throw<FormatException>(reason).WithMessage($"*{expectedInMessage}*", reason);
    }

    /// <summary>
    /// The hash resolver counts the fields before anything is parsed, so it reaches the five-field
    /// message with expressions the parser never gets far enough to reject - a five-field string's last
    /// field is read as a month, so <c>0 12 * * MON</c> fails on the month long before the count is
    /// looked at. The advice has to be the same on both paths.
    /// </summary>
    [TestCase("H 4 * * 1", "\"0 H 4 ? * MON\"", "the rewrite keeps the H token, because the token is not what is wrong")]
    [TestCase("H 12 * * 0", "\"0 H 12 ? * SUN\"", "Unix numbers Sunday 0 where Quartz numbers it 1")]
    [TestCase("H 12 * * MON", "respell numeric day-of-week values", "a day-of-week already spelled as a name needs no renumbering, so the generic advice stands")]
    public void TheFiveFieldMessageIsTheSameOnTheHashResolvingPath(string expression, string expectedInMessage, string reason)
    {
        Action act = () => CronExpression.ParseWithHash(expression, "a-trigger");

        act.Should().Throw<FormatException>(reason)
            .WithMessage("*Unix/crontab*", "the two paths must not disagree about what a five-field expression is")
            .WithMessage($"*{expectedInMessage}*", reason);
    }

    /// <summary>
    /// Every shape the parser used to accept and then quietly reinterpret. Each one now throws, and the
    /// message has to name the expression that says what the author meant: a rejection that only states
    /// the rule leaves them guessing at a schedule they believed they had already written.
    /// </summary>
    [TestCase("0 15 10 1-5W * ?", "1W,2W,3W,4W,5W", "the 'W' was dropped, so this quietly meant days 1 through 5")]
    [TestCase("0 15 10 1-20W * ?", "1W,2W,...,20W", "a long range is abbreviated rather than spelled out over twenty days")]
    [TestCase("0 15 10 1-5X * ?", "Unexpected character 'X' after the range '1-5'", "anything left over after a range was dropped, whatever it was")]
    [TestCase("0 0 0 ? * L-3", "day-of-month", "everything after the 'L' was discarded, so this quietly meant every Saturday")]
    [TestCase("0 0 0 ? * LW", "day-of-month", "'LW' in day-of-week was read as a bare 'L', which is Saturday")]
    [TestCase("0 0 0 ? * MON,FRI#3", "one trigger per day", "the evaluator reads the smallest day in the field, so the Friday never fired at all")]
    [TestCase("0 15 10 5C * ?", "ModifiedByCalendar", "'C' was never implemented, so '5C' behaved exactly like '5'")]
    [TestCase("0 15 10 ? * 5C", "ModifiedByCalendar", "'C' was equally inert in day-of-week")]
    [TestCase("0 */0 * * * ?", "a step of 1 or more", "a step of zero degenerated to no step, so '*/0' was '*'")]
    [TestCase("0 5/0 * * * ?", "a step of 1 or more", "a step of zero after a value degenerated to the plain value")]
    [TestCase("0 0-10/0 * * * ?", "a step of 1 or more", "a step of zero inside a range degenerated to the range's start")]
    [TestCase("0 0-10/ * * * ?", "'/' must be followed by an integer", "a range with an empty step said nothing at all")]
    [TestCase("0 0 12 ? * MON/2", "MON,WED,FRI", "the numeric twin '2/2' is what a step through the week means")]
    [TestCase("0 0 12 ? * MON/2", "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO", "a fortnight that keeps its phase is a recurrence rule, not a cron expression")]
    [TestCase("0 0 12 ? * MON/2", "not stable", "the message has to say why the extension went, or it reads as an arbitrary removal")]
    [TestCase("0 0 12 ? * MON/X", "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO", "a step that is not a number is still a textual day-of-week step")]
    [TestCase("0 0 12 ? * SUN/9", "FREQ=WEEKLY;INTERVAL=2;BYDAY=SU", "a step outside 1-7 is rejected as the same construct, not as a range error")]
    public void ExpressionsThatSaidOneThingAndDidAnotherAreRejected(string expression, string expectedInMessage, string reason)
    {
        Action act = () => new CronExpression(expression);

        act.Should().Throw<FormatException>(reason).WithMessage($"*{expectedInMessage}*", reason);
    }

    [TestCase("0-10/120 0 8-18 ? * 2-6", "Increment > 59 : 120")]
    [TestCase("0 0-10/120 8-18 ? * 2-6", "Increment > 59 : 120")]
    [TestCase("0 0 0-10/120 ? * 2-6", "Increment > 23 : 120")]
    [TestCase("0 0 0 1-10/120 * 2-6", "Increment > 31 : 120")]
    [TestCase("0 0 0 ? 1-10/120 2-6", "Increment > 12 : 120")]
    [TestCase("0 0 0 ? * 1-6/120", "Increment > 7 : 120")]
    public void AStepInsideARangeIsRangeCheckedLikeAnyOtherStep(string expression, string expectedMessage)
    {
        Action act = () => new CronExpression(expression);

        act.Should().Throw<FormatException>(
                "'0/120' has always thrown, and there is no reading of cron in which putting a range in front of the same step makes it legal")
            .WithMessage(expectedMessage);
    }

    /// <summary>
    /// The legitimate forms the new rejections stand next to. Each reads like one of them, so pin the
    /// pair: bare <c>L</c> in day-of-week is Saturday while <c>L-3</c> there is not a form at all, and
    /// <c>15W</c> shifts a single day while <c>1-5W</c> is a range with a stray character on the end.
    /// </summary>
    [TestCase("0 15 10 ? * L 2010", "'L' on its own in day-of-week is Saturday")]
    [TestCase("0 15 10 ? * 6L 2010", "'6L' is the last Friday of the month")]
    [TestCase("0 15 10 ? * FRIL 2010", "'FRIL' is the same schedule spelled with a name")]
    [TestCase("0 15 10 LW-2 * ? 2010", "'LW-n' counts back from the last weekday of the month")]
    [TestCase("0 15 10 L-5W * ? 2010", "'L-nW' is the nearest weekday to a day counted back from the end")]
    [TestCase("0 15 10 15W * ? 2010", "a 'W' on a single day-of-month still shifts that day")]
    [TestCase("0 15 10 ? * 6#3 2010", "'#' with no other day in the field is the nth day of the month")]
    [TestCase("0 15 10 ? * FRI#3 2010", "and the same written with a name")]
    [TestCase("0 15 10 1-5 * ? 2010", "a range with nothing after it is a range")]
    [TestCase("0 0 18-21/1 ? * MON-FRI", "a step of 1 inside a range is a step, and a textual range is not a dash option")]
    [TestCase("0 0 12 ? * 2/2", "the numeric twin of 'MON/2' is an ordinary step and is untouched")]
    [TestCase("0 15 10 ? DEC * 2010", "'DEC' is a month name that happens to end in 'C', not a calendar option")]
    public void ExpressionsTheNewRejectionsMustNotCatchStillParse(string expression, string reason)
    {
        Action act = () => new CronExpression(expression);

        act.Should().NotThrow(reason);
    }

    [Test]
    public void TooFewFieldsNamesTheRequiredFields()
    {
        Action act = () => new CronExpression("0 30 4");

        act.Should().Throw<FormatException>()
            .WithMessage("*has 3 fields, but 6 or 7 are required*")
            .WithMessage("*seconds, minutes, hours, day-of-month, month, day-of-week*");
    }

    [Test]
    public void CronExpression_Throw_Error_Constructed_With_Null()
    {
        Action act = () => new CronExpression(null);
        act.Should().Throw<ArgumentException>()
            .WithMessage("cronExpression cannot be null*");
    }

    [TestCase('h')]
    [TestCase('?')]
    [TestCase('*')]
    public void Should_Throw_Error_When_Extra_NonWhitespace_Character_After_QuestionMark(char invalidChar)
    {
        Action act = () => new CronExpression($"0 0 * * * ?{invalidChar}");
        act.Should().Throw<FormatException>()
            .WithMessage("Illegal character after '?':*");
    }

    [TestCase(' ')]
    [TestCase('\t')]
    public void QuestionMark_With_ExtraWhitespace_Should_Be_Valid(char allowedChar)
    {
        Action act = () => new CronExpression($"0 0 * * * ?{allowedChar}");
        act.Should().NotThrow();
    }

    /// <summary>
    /// '?' says the same thing as '*' in the two day fields, and is accepted in no other field. Some
    /// other implementations take it anywhere; that is surface with no demand behind it, and this test
    /// is here because the sibling rule - '?' in only one of the two day fields - was dropped when the
    /// two spellings became synonyms, and the survivor should not go with it by accident.
    /// </summary>
    [TestCase("? 0 12 * * ?", "seconds")]
    [TestCase("0 ? 12 * * ?", "minutes")]
    [TestCase("0 0 ? * * ?", "hours")]
    [TestCase("0 0 12 * ? ?", "month")]
    [TestCase("0 0 12 * * ? ?", "year")]
    public void QuestionMark_IsRejectedOutsideTheTwoDayFields(string cronExpression, string field)
    {
        Action act = () => new CronExpression(cronExpression);
        act.Should().Throw<FormatException>("'?' has never been legal in the {0} field", field)
            .WithMessage("*Day-of-Month or Day-of-Week*");
    }

    [Test]
    public void CanGetNextTimeAfterExternal_From_JsonDeserializedExpression()
    {
        // Scenario where external serialization occurrs outside of the provided Quartz Serializers.
        var cronExpression = new CronExpression("0 15 23 * * ?");
        var date = new DateTime(2005, 6, 1, 23, 16, 0).ToUniversalTime();
        var nextExpectedFireTime = new DateTime(2005, 6, 2, 23, 15, 0).ToUniversalTime();
        var jsonCronExpression = JsonConvert.SerializeObject(cronExpression);
        var deSerializedCron = JsonConvert.DeserializeObject<CronExpression>(jsonCronExpression);
        deSerializedCron.GetNextValidTimeAfter(date).Value.Should().Be(nextExpectedFireTime);
    }

    [Test]
    public void TestCronExpressionPassingMidnight()
    {
        CronExpression cronExpression = new CronExpression("0 15 23 * * ?");
        DateTimeOffset date = new DateTime(2005, 6, 1, 23, 16, 0).ToUniversalTime();
        DateTimeOffset nextExpectedFireTime = new DateTime(2005, 6, 2, 23, 15, 0).ToUniversalTime();
        Assert.That(cronExpression.GetTimeAfter(date).Value, Is.EqualTo(nextExpectedFireTime));
    }

    [Test]
    public void TestCronExpressionPassingYear()
    {
        DateTimeOffset start = new DateTime(2007, 12, 1, 23, 59, 59).ToUniversalTime();

        CronExpression ce = new CronExpression("0 55 15 1 * ?");
        DateTimeOffset expected = new DateTime(2008, 1, 1, 15, 55, 0).ToUniversalTime();
        DateTimeOffset d = ce.GetNextValidTimeAfter(start).Value;
        Assert.That(d, Is.EqualTo(expected), "Got wrong date and time when passed year");
    }

    [Test]
    public void TestCronExpressionWeekdaysMonFri()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 ? * MON-FRI");
        int[] arrJuneDaysThatShouldFire =
            [1, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 18, 19, 20, 22, 21, 25, 26, 27, 28, 29];
        List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

        TestCorrectWeekFireDays(cronExpression, juneDays);
    }

    [Test]
    public void TestCronExpressionWeekdaysFriday()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 ? * FRI");
        var nextRunTime = cronExpression.GetTimeAfter(DateTimeOffset.Now);
        var nextRunTime2 = cronExpression.GetTimeAfter((DateTimeOffset) nextRunTime);

        int[] arrJuneDaysThatShouldFire =
            [1, 8, 15, 22, 29];
        List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

        TestCorrectWeekFireDays(cronExpression, juneDays);
    }

    [Test]
    public void TestCronExpressionLastDayOfMonth()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 L * ?");
        int[] arrJuneDaysThatShouldFire = [30];
        List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

        TestCorrectWeekFireDays(cronExpression, juneDays);
    }

    [Test]
    public void TestHourShift()
    {
        // cronexpression that fires every 5 seconds
        CronExpression cronExpression = new CronExpression("0/5 * * * * ?");
        DateTimeOffset date = new DateTimeOffset(2005, 6, 1, 1, 59, 55, TimeSpan.Zero);
        DateTimeOffset nextExpectedFireTime = new DateTimeOffset(2005, 6, 1, 2, 0, 0, TimeSpan.Zero);
        Assert.That(cronExpression.GetTimeAfter(date).Value, Is.EqualTo(nextExpectedFireTime));
    }

    [Test]
    public void TestMonthShift()
    {
        // QRTZNET-28
        CronExpression cronExpression = new CronExpression("* * 1 * * ?");
        DateTimeOffset date = new DateTime(2005, 7, 31, 22, 59, 57).ToUniversalTime();
        DateTimeOffset nextExpectedFireTime = new DateTime(2005, 8, 1, 1, 0, 0).ToUniversalTime();
        Assert.That(cronExpression.GetTimeAfter(date).Value, Is.EqualTo(nextExpectedFireTime));
    }

    [Test]
    public void TestYearChange()
    {
        // QRTZNET-85
        CronExpression cronExpression = new CronExpression("0 12 4 ? * 3");
        cronExpression.GetNextValidTimeAfter(new DateTime(2007, 12, 28));
    }

    [Test]
    public void TestCronExpressionParsingIncorrectDayOfWeek()
    {
        // test failed before because of improper trimming
        try
        {
            string expr = $" * * * * * {DateTime.Now.Year}";
            CronExpression ce = new CronExpression(expr);
            ce.IsSatisfiedBy(DateTime.UtcNow.AddMinutes(2));
            Assert.Fail("Accepted wrong format");
        }
        catch (FormatException fe)
        {
            fe.Message.Should().StartWith("Day-of-Week values must be between 1 and 7")
                .And.Contain("Unix cron numbers days 0-6", "the error must teach the Quartz-vs-Unix numbering difference");
        }
    }

    [Test]
    public void TestCronExpressionWithExtraWhiteSpace()
    {
        // test failed before because of improper trimming
        string expr = " 30 *   * * * ?  ";
        CronExpression cronExpression = new CronExpression(expr);
        Assert.That(cronExpression.IsSatisfiedBy(DateTime.UtcNow.Date.AddMinutes(2)), Is.False, "Time was included");
    }

    private static void TestCorrectWeekFireDays(CronExpression cronExpression, IList<int> correctFireDays)
    {
        List<int> fireDays = [];

        DateTime date = new DateTime(2007, 6, 1, 11, 0, 0).ToUniversalTime();
        DateTimeOffset? nextFireTime = date;

        for (int i = 0; i < DateTime.DaysInMonth(2007, 6); ++i)
        {
            nextFireTime = cronExpression.GetTimeAfter((DateTimeOffset) nextFireTime);
            if (!fireDays.Contains(nextFireTime.Value.Day) && nextFireTime.Value.Month == 6 && nextFireTime.Value.Year == 2007)
            {
                // next fire day may be monday for several days..
                fireDays.Add(nextFireTime.Value.Day);
            }
            //date = date.AddDays(1);
        }

        // check rite dates fired
        for (int i = 0; i < fireDays.Count; ++i)
        {
            int idx = correctFireDays.IndexOf(fireDays[i]);
            Assert.That(idx, Is.GreaterThan(-1), $"CronExpression evaluated true for {fireDays[i]} even when it shouldn't have");
            correctFireDays.RemoveAt(idx);
        }

        // check that all fired
        Assert.That(correctFireDays, Is.Empty, $"CronExpression did not evaluate true for all expected days (count: {correctFireDays.Count}).");
    }

    [Test]
    public void TestNthWeekDayPassingMonth()
    {
        CronExpression ce = new CronExpression("0 30 10-13 ? * FRI#3");
        DateTime start = new DateTime(2008, 12, 19, 0, 0, 0);
        for (int i = 0; i < 200; ++i)
        {
            bool shouldFire = start.Hour >= 10 && start.Hour <= 13 && start.Minute == 30
                              && (start.DayOfWeek == DayOfWeek.Wednesday || start.DayOfWeek == DayOfWeek.Friday);
            shouldFire = shouldFire && start.Day > 15 && start.Day < 28;

            bool satisfied = ce.IsSatisfiedBy(start.ToUniversalTime());
            Assert.That(satisfied, Is.EqualTo(shouldFire));

            // cycle with half hour precision
            start = start.AddHours(0.5);
        }
    }

    [Test]
    public void TestNormal()
    {
        for (int i = 0; i < 6; i++)
        {
            AssertParsesForField("0 15 10 * * ? 2005", i);
        }
    }

    [Test]
    public void TestSecond()
    {
        AssertParsesForField("58-4 5 21 ? * MON-FRI", 0);
    }

    [Test]
    public void TestMinute()
    {
        AssertParsesForField("0 58-4 21 ? * MON-FRI", 1);
    }

    [Test]
    public void TestHour()
    {
        AssertParsesForField("0 0/5 21-3 ? * MON-FRI", 2);
    }

    [Test]
    public void TestDayOfWeekNumber()
    {
        AssertParsesForField("58 5 21 ? * 6-2", 5);
    }

    [Test]
    public void TestDayOfWeek()
    {
        AssertParsesForField("58 5 21 ? * FRI-TUE", 5);
    }

    [Test]
    public void TestDayOfMonth()
    {
        AssertParsesForField("58 5 21 28-5 1 ?", 3);
    }

    [Test]
    public void TestMonth()
    {
        AssertParsesForField("58 5 21 ? 11-2 FRI", 4);
    }

    [Test]
    public void TestAmbiguous()
    {
        AssertParsesForField("0 0 14-6 ? * FRI-MON", 2);
        AssertParsesForField("0 0 14-6 ? * FRI-MON", 5);

        AssertParsesForField("55-3 56-2 6 ? * FRI", 0);
        AssertParsesForField("55-3 56-2 6 ? * FRI", 1);
    }

    private static void AssertParsesForField(string expression, int constant)
    {
        try
        {
            var cronExpression = new CronExpression(expression);
            var set = cronExpression.GetSet(constant);
            if (set.Count == 0)
            {
                Assert.Fail("Empty field [" + constant + "] returned for " + expression);
            }
        }
        catch (FormatException pe)
        {
            Assert.Fail("Exception thrown during parsing: " + pe);
        }
    }

    [Test]
    public void TestQuartz640()
    {
        try
        {
            new CronExpression("0 43 9 ? * SAT,SUN,L");
            Assert.Fail("Expected FormatException did not fire for L combined with other days of the week");
        }
        catch (FormatException pe)
        {
            Assert.That(
                pe.Message,
                Does.StartWith("Support for specifying 'L' with other days of the week is not implemented"),
                "Incorrect FormatException thrown");
        }

        try
        {
            new CronExpression("0 43 9 ? * 6,7,L");
            Assert.Fail("Expected FormatException did not fire for L combined with other days of the week");
        }
        catch (FormatException pe)
        {
            Assert.That(
                pe.Message,
                Does.StartWith("Support for specifying 'L' with other days of the week is not implemented"),
                "Incorrect FormatException thrown");
        }

        try
        {
            new CronExpression("0 43 9 ? * 5L");
        }
        catch (FormatException)
        {
            Assert.Fail("Unexpected ParseException thrown for supported '5L' expression.");
        }
    }

    [Test]
    public void TestGetTimeAfter_QRTZNET149()
    {
        CronExpression expression = new CronExpression("0 0 0 29 * ?");
        DateTimeOffset? after = expression.GetNextValidTimeAfter(new DateTime(2009, 1, 30, 0, 0, 0).ToUniversalTime());
        Assert.Multiple(() =>
        {
            Assert.That(after.HasValue, Is.True);
            Assert.That(after.Value.DateTime, Is.EqualTo(new DateTime(2009, 3, 29, 0, 0, 0).ToUniversalTime()));
        });

        after = expression.GetNextValidTimeAfter(new DateTime(2009, 12, 30).ToUniversalTime());
        Assert.Multiple(() =>
        {
            Assert.That(after.HasValue, Is.True);
            Assert.That(after.Value.DateTime, Is.EqualTo(new DateTime(2010, 1, 29, 0, 0, 0).ToUniversalTime()));
        });
    }

    [Test]
    public void TestQRTZNET152_Nearest_Weekday_Expression_W_Does_Not_Work_In_CronTrigger()
    {
        CronExpression expression = new CronExpression("0 5 13 5W 1-12 ?");
        DateTimeOffset test = new DateTimeOffset(2009, 3, 8, 0, 0, 0, TimeSpan.Zero); //Sunday
        DateTimeOffset d = expression.GetNextValidTimeAfter(test).Value;
        // 2009-04-06 is Monday, Sunday is invalid for W
        Assert.That(d, Is.EqualTo(new DateTimeOffset(2009, 4, 6, 13, 5, 0, TimeZones.GetUtcOffset(d, TimeZoneInfo.Local)).ToUniversalTime()));
        d = expression.GetNextValidTimeAfter(d).Value;
        Assert.That(d, Is.EqualTo(new DateTimeOffset(2009, 5, 5, 13, 5, 0, TimeZones.GetUtcOffset(d, TimeZoneInfo.Local))));
    }

    [Test]
    public void ShouldThrowExceptionIfWParameterMakesNoSense()
    {
        try
        {
            new CronExpression("0/5 * * 32W 1 ?");
            Assert.Fail("Expected FormatException did not fire for W with value larger than 31");
        }
        catch (FormatException pe)
        {
            Assert.That(pe.Message, Does.StartWith("The 'W' option does not make sense with values larger than"), "Incorrect ParseException thrown");
        }
    }

    /// <summary>
    /// QTZ-259 : last day offset causes repeating fire time
    /// </summary>
    [Test]
    public void TestQtz259()
    {
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("test").WithCronSchedule("0 0 0 L-2 * ? *").Build();

        int i = 0;
        DateTimeOffset? pdate = trigger.GetFireTimeAfter(DateTimeOffset.Now);
        while (++i < 26)
        {
            DateTimeOffset? date = trigger.GetFireTimeAfter(pdate);
            Assert.That(pdate, Is.Not.EqualTo(date), "Next fire time is the same as previous fire time!");
            pdate = date;
        }
    }

    /// <summary>
    /// QTZ-259 : last day offset causes repeating fire time
    /// </summary>
    [Test]
    public void TestQtz259Lw()
    {
        ITrigger trigger = TriggerBuilder.Create().WithIdentity("test").WithCronSchedule("0 0 0 LW * ? *").Build();

        int i = 0;
        DateTimeOffset? pdate = trigger.GetFireTimeAfter(DateTimeOffset.Now);
        while (++i < 26)
        {
            DateTimeOffset? date = trigger.GetFireTimeAfter(pdate);
            // Console.WriteLine("fireTime: " + date + ", previousFireTime: " + pdate);
            Assert.That(pdate, Is.Not.EqualTo(date), "Next fire time is the same as previous fire time!");
            pdate = date;
        }
    }

    [Test]
    [Platform("WIN")]
    public void TestDaylightSaving_QRTZNETZ186()
    {
        CronExpression expression = new CronExpression("0 15 * * * ?");
        if (!TimeZoneInfo.Local.SupportsDaylightSavingTime)
        {
            return;
        }

        var daylightChange = TimeZone.CurrentTimeZone.GetDaylightChanges(2012);
        DateTimeOffset before = daylightChange.Start.ToUniversalTime().AddMinutes(-5); // keep outside the potentially undefined interval
        DateTimeOffset? after = expression.GetNextValidTimeAfter(before);
        Assert.That(after.HasValue, Is.True);
        // The :15 of the hour the gap swallowed does not exist, so the fire lands at the end of the
        // gap - the instant the clocks moved, which is the start of the change plus its delta.
        DateTimeOffset expected = daylightChange.Start.Add(daylightChange.Delta).ToUniversalTime();
        Assert.That(after.Value, Is.EqualTo(expected));
    }

    [Test]
    public void TestDaylightSavingsDoesNotMatchAnHourBefore()
    {
        TimeZoneInfo est = TimeZones.FindById("Eastern Standard Time");
        CronExpression expression = new CronExpression("0 15 15 5 11 ?", est);

        DateTimeOffset startTime = new DateTimeOffset(2012, 11, 4, 0, 0, 0, TimeSpan.Zero);

        var actualTime = expression.GetTimeAfter(startTime);
        DateTimeOffset expected = new DateTimeOffset(2012, 11, 5, 15, 15, 0, TimeSpan.FromHours(-5));

        Assert.That(actualTime.Value, Is.EqualTo(expected));
    }

    [Test]
    public void TestDaylightSavingsDoesNotMatchAnHourBefore2()
    {
        //another case
        TimeZoneInfo est = TimeZones.FindById("Eastern Standard Time");
        CronExpression expression = new CronExpression("0 0 0 ? * THU", est);

        DateTimeOffset startTime = new DateTimeOffset(2012, 11, 4, 0, 0, 0, TimeSpan.Zero);

        var actualTime = expression.GetTimeAfter(startTime);
        DateTimeOffset expected = new DateTimeOffset(2012, 11, 8, 0, 0, 0, TimeSpan.FromHours(-5));
        Assert.That(actualTime, Is.EqualTo(expected));
    }

    [Test]
    public void TestSecRangeIntervalAfterSlash()
    {
        // Test case 1
        var e = Assert.Throws<FormatException>(() => new CronExpression("/120 0 8-18 ? * 2-6"), "Cron did not validate bad range interval in '_blank/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 59 : 120"));

        // Test case 2
        e = Assert.Throws<FormatException>(() => new CronExpression("0/120 0 8-18 ? * 2-6"), "Cron did not validate bad range interval in in '0/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 59 : 120"));

        // Test case 3
        e = Assert.Throws<FormatException>(() => new CronExpression("/ 0 8-18 ? * 2-6"), "Cron did not validate bad range interval in '_blank/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));

        // Test case 4
        e = Assert.Throws<FormatException>(() => new CronExpression("0/ 0 8-18 ? * 2-6"), "Cron did not validate bad range interval in '0/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));
    }

    [Test]
    public void TestMinRangeIntervalAfterSlash()
    {
        // Test case 1
        var e = Assert.Throws<FormatException>(() => new CronExpression("0 /120 8-18 ? * 2-6"), "Cron did not validate bad range interval in '_blank/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 59 : 120"));

        // Test case 2
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0/120 8-18 ? * 2-6"), "Cron did not validate bad range interval in in '0/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 59 : 120"));

        // Test case 3
        e = Assert.Throws<FormatException>(() => new CronExpression("0 / 8-18 ? * 2-6"), "Cron did not validate bad range interval in '_blank/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));

        // Test case 4
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0/ 8-18 ? * 2-6"), "Cron did not validate bad range interval in '0/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));
    }

    [Test]
    public void TestHourRangeIntervalAfterSlash()
    {
        // Test case 1
        var e = Assert.Throws<FormatException>(() => new CronExpression("0 0 /120 ? * 2-6"), "Cron did not validate bad range interval in '_blank/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 23 : 120"));

        // Test case 2
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0/120 ? * 2-6"), "Cron did not validate bad range interval in in '0/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 23 : 120"));

        // Test case 3
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 / ? * 2-6"), "Cron did not validate bad range interval in '_blank/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));

        // Test case 4
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0/ ? * 2-6"), "Cron did not validate bad range interval in '0/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));
    }

    [Test]
    public void TestDayOfMonthRangeIntervalAfterSlash()
    {
        // Test case 1
        var e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 /120 * 2-6"), "Cron did not validate bad range interval in '_blank/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 31 : 120"));

        // Test case 2
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 0/120 * 2-6"), "Cron did not validate bad range interval in in '0/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 31 : 120"));

        // Test case 3
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 / * 2-6"), "Cron did not validate bad range interval in '_blank/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));

        // Test case 4
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 0/ * 2-6"), "Cron did not validate bad range interval in '0/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));
    }

    [Test]
    public void TestMonthRangeIntervalAfterSlash()
    {
        // Test case 1
        var e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? /120 2-6"), "Cron did not validate bad range interval in '_blank/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 12 : 120"));

        // Test case 2
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? 0/120 2-6"), "Cron did not validate bad range interval in in '0/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 12 : 120"));

        // Test case 3
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? / 2-6"), "Cron did not validate bad range interval in '_blank/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));

        // Test case 4
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? 0/ 2-6"), "Cron did not validate bad range interval in '0/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));
    }

    [Test]
    public void TestDayOfWeekRangeIntervalAfterSlash()
    {
        // Test case 1
        var e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? * /120"), "Cron did not validate bad range interval in '_blank/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 7 : 120"));

        // Test case 2
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? * 0/120"), "Cron did not validate bad range interval in in '0/xxx' form");
        Assert.That(e.Message, Is.EqualTo("Increment > 7 : 120"));

        // Test case 3
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? * /"), "Cron did not validate bad range interval in '_blank/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));

        // Test case 4
        e = Assert.Throws<FormatException>(() => new CronExpression("0 0 0 ? * 0/"), "Cron did not validate bad range interval in '0/_blank'");
        Assert.That(e.Message, Is.EqualTo("'/' must be followed by an integer."));
    }


    [Test]
    public void TestInvalidCharactersAfterAsterisk()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CronExpression.TryParse("* * * ? * *A&/5:", out _), Is.False);
            Assert.That(CronExpression.TryParse("* * * ? *14 ", out _), Is.False);
            Assert.That(CronExpression.TryParse(" * * ? *A&/5 *", out _), Is.False);
            Assert.That(CronExpression.TryParse("* * ? */5 *", out _), Is.False);
            Assert.That(CronExpression.TryParse("* * ? */52 *", out _), Is.False);

            Assert.That(CronExpression.TryParse("0 0/30 * * * ?", out _), Is.True);
            Assert.That(CronExpression.TryParse("0 0/1 * * * ?", out _), Is.True);
            Assert.That(CronExpression.TryParse("0 0/30 * * */2 ?", out _), Is.True);
        });
    }

    [Test]
    public void TestInvalidCronExpressionCharacters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CronExpression.TryParse("0 0x 0/1 ? * * *", out _), Is.False);
            Assert.That(CronExpression.TryParse("0 1xdds0 0/1 ? * * *", out _), Is.False);
        });
    }

    [Test]
    public void TestExtraCharactersAfterWeekDay()
    {
        Assert.That(CronExpression.TryParse("0 0 15 ? * FRI*", out _), Is.False);
    }

    [Test]
    public void TestHourRangeAndSlash()
    {
        Action act = () => CronExpression.Parse("0 0 18-21/1 ? * MON,TUE,WED,THU,FRI,SAT,SUN");

        act.Should().NotThrow("a stepped hour range beside every day of the week is a valid expression");
    }

    [Test]
    [Explicit]
    public void PerformanceTest()
    {
        var quartz = new CronExpression("* * * * * ?");

        var sw = new Stopwatch();
        sw.Start();

        DateTimeOffset? next = new DateTimeOffset(2012, 1, 1, 0, 0, 0, TimeSpan.Zero);

        for (int i = 0; i < 1000000; i++)
        {
            next = quartz.GetNextValidTimeAfter(next.Value);

            if (next is null)
                break;
        }

        Console.WriteLine("{0}ms", sw.ElapsedMilliseconds);
    }



    [Test]
    public void CanGetNextInvalidTime()
    {
        CronExpression expression = new CronExpression("0 15 15 5 11 ?");
        var sut = expression.GetNextInvalidTimeAfter(new DateTimeOffset(2010, 12, 1, 1, 1, 0, 0, TimeSpan.Zero));
        sut.Should().NotBeNull();
    }

    [Test]
    public void CanGetHashCode()
    {
        CronExpression expression = new CronExpression("0 15 15 5 11 ?");
        CronExpression expression2 = new CronExpression("0 15 15 5 11 ?");
        expression.GetHashCode().Should().Be(expression2.GetHashCode());
    }

    /// <summary>
    /// The reason the hash constructors became <c>ParseWithHash</c>: with <c>(string, string)</c> and
    /// <c>(string, int)</c> beside <c>(string, TimeZoneInfo?)</c>, the null literal was a CS0121
    /// ambiguity, so the one overload whose parameter is documented as nullable could not be handed a
    /// null. This test's whole subject is that it compiles.
    /// </summary>
    [Test]
    public void ATimeZoneOfNullIsUnambiguousAndMeansTheLocalZone()
    {
        CronExpression expression = new CronExpression("0 15 15 5 11 ?", null);

        expression.TimeZone.Should().Be(TimeZoneInfo.Local,
            "a null time zone is the documented way to say 'the system's local zone'");
    }

    [TestCase("OCT", 10)]
    [TestCase("NOV", 11)]
    [TestCase("DEC", 12)]
    public void GivenMonthAbbreviation_ShouldGetTimeAfter(
    string monthAbbr, int monthNumber)
    {
        string expression = $"0 0 0 1 {monthAbbr} ? *";

        CronExpression ce = new(expression, TimeZoneInfo.Utc);
        var startTime = new DateTimeOffset(2024, 7, 22, 12, 0, 0, TimeSpan.Zero);
        var expectedTimeAfter = new DateTimeOffset(2024, monthNumber, 1, 0, 0, 0, TimeSpan.Zero);

        var actualTimeAfter = ce.GetTimeAfter(startTime);

        actualTimeAfter.Should().Be(expectedTimeAfter);
    }

    [Test]
    public void GetPreviousValidTimeBeforeDoesNotThrowForPositiveOffsetTimeZone()
    {
        // Issue #3046: GetPreviousValidTimeBefore threw ArgumentOutOfRangeException when the
        // timezone had a positive UTC offset, because the inverse binary search
        // stepped into year 1 where internal DateTimeOffset constructions inside
        // GetTimeAfter produced a UTC instant before year 1.
        TimeZoneInfo plus11 = TimeZoneInfo.CreateCustomTimeZone(
            "Test+11", TimeSpan.FromHours(11), "Test+11", "Test+11");
        CronExpression cron = new CronExpression("0 0 0 ? * FRI *", plus11);
        DateTimeOffset time = new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero);

        DateTimeOffset? before = null;
        Assert.DoesNotThrow(() => before = cron.GetPreviousValidTimeBefore(time));
        Assert.That(before, Is.Not.Null);
        Assert.That(before!.Value, Is.LessThan(time));
    }

    [TestCaseSource(typeof(CronTestScenarios), nameof(CronTestScenarios.TestCases))]
    public void CronExpressionReturnsExpectedNextFireTime(CronExpression cronExpression, DateTimeOffset timeAfterDate, DateTimeOffset expectedNextFireTime)    {
        var nextFireTime = cronExpression.GetTimeAfter(timeAfterDate);
        nextFireTime.Value.Date.Should().Be(expectedNextFireTime.Date, "NextFireTime was not correct");
    }
}

public class CronTestScenarios
{
    private sealed class TestCaseProps
    {
        public CronExpression CronExpression { get; init; }

        public DateTimeOffset TimeAfterDate { get; init; }

        public DateTimeOffset ExpectedNextFireTime { get; init; }

        public string TestCase { get; init; }
    }

    private static IEnumerable<TestCaseProps> TestCaseData =>
    [
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 15W * ?"),
            TimeAfterDate = new DateTimeOffset(2024, 5, 15, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2024, 6, 14, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on Weekday 15th Every Month - 2024-06-15 is a Sat, schedule should be Fri 14th"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 15W * ?"),
            TimeAfterDate = new DateTimeOffset(2024, 8, 15, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2024, 9, 16, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on Weekday 15th Every Month - 2024-09-15 is a Sunday, expect schedule to be Mon 16th"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 15W * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 12, 15, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on Weekday 15th Every Month - 2024-01-15 is Monday, should run on Monday"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 31W * ?"),
            TimeAfterDate = new DateTimeOffset(2025, 1, 31, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2025, 2, 28, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Test that next fire time to be in next month with less days in month - Issue #2330"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 LW * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 2, 28, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 3, 31, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on last weekday of the month - 2023-03-31 is a Friday"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 L-2 * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 4, 28, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 5, 29, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the second-to-last day of the month"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 ? * 6L"),
            TimeAfterDate = new DateTimeOffset(2023, 6, 24, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 6, 30, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the last Friday of the month - 2023-06-30 is the last Friday"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 ? * 6#3"),
            TimeAfterDate = new DateTimeOffset(2023, 7, 21, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 8, 18, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the third Friday of the month"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 ? * 2/2"),
            TimeAfterDate = new DateTimeOffset(2023, 9, 5, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 9, 6, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run every second day (/2) starting Monday (2)"
        },
        new TestCaseProps
        {
            CronExpression = new CronExpression("0 0 12 1W * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 10, 1, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 10, 2, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the first weekday of the month - 2023-10-01 is a Sunday, expect schedule to be Mon 2nd"
        }
    ];

    public static IEnumerable TestCases => TestCaseData.Select(model => new TestCaseData(model.CronExpression, model.TimeAfterDate, model.ExpectedNextFireTime));

    [Test]
    public void TooManyTokensShouldThrow()
    {
        var act = () => new CronExpression("0 15 10 * * ? 2005 *");
        act.Should().Throw<FormatException>().Which.Message.Should().Contain("too many");
    }

    private const long OneDayInMilliseconds = 24 * 60 * 60 * 1000L;

    /// <summary>
    /// One expression and the gaps its firings sit at, measured from the first firing after a search
    /// instant: back to the firing before that one, and on to the firing after it.
    /// </summary>
    /// <remarks>
    /// Both gaps are <see langword="null" /> for an expression pinned to a single year to one side of
    /// the search instant. Such an expression has firings on one side only, so there is no pair of
    /// neighbouring firings to measure — what is asserted about it is which side is empty.
    /// </remarks>
    private readonly record struct FiringGaps(string Expression, long? MillisecondsBack, long? MillisecondsForward);

    /// <summary>
    /// The expressions <see cref="TestGetPreviousValidTimeBefore" /> walks, and the gaps they describe. The last two
    /// are pinned to a year either side of the year the search starts in: the first of them has its
    /// whole schedule ahead, so the firing found is its very first and nothing precedes it, and the
    /// second has its whole schedule behind, so there is no firing after the instant at all.
    /// </summary>
    private static FiringGaps[] FiringGapsAround(int year) =>
    [
        new FiringGaps("* * * * * ? *", 1000L, 1000L),
        new FiringGaps("0 * * * * ? *", 60_000L, 60_000L),
        new FiringGaps("0/15 * * * * ? *", 15_000L, 15_000L),
        new FiringGaps("0 0 5 * * ? *", OneDayInMilliseconds, OneDayInMilliseconds),
        new FiringGaps("0 0 0 * * ? *", OneDayInMilliseconds, OneDayInMilliseconds),
        new FiringGaps("0/30 1 2 * * ? *", OneDayInMilliseconds - 30_000L, 30_000L),
        new FiringGaps($"* * * * * ? {year + 2}", null, null),
        new FiringGaps($"* * * * * ? {year - 2}", null, null)
    ];

    /// <summary>
    /// The instants <see cref="TestGetPreviousValidTimeBefore" /> searches from: an ordinary one, and one on each
    /// boundary the search could round differently at.
    /// </summary>
    public static IEnumerable TimeBeforeSearchInstants =>
        new[]
        {
            new TestCaseData("mid-minute", new DateTimeOffset(2024, 3, 14, 8, 37, 23, 456, TimeSpan.Zero)),
            new TestCaseData("second boundary", new DateTimeOffset(2024, 3, 14, 8, 37, 23, 0, TimeSpan.Zero)),
            new TestCaseData("minute boundary", new DateTimeOffset(2024, 3, 14, 8, 37, 0, 0, TimeSpan.Zero)),
            new TestCaseData("hour boundary", new DateTimeOffset(2024, 3, 14, 8, 0, 0, 0, TimeSpan.Zero)),
            new TestCaseData("day boundary", new DateTimeOffset(2024, 3, 14, 0, 0, 0, 0, TimeSpan.Zero)),
            new TestCaseData("year boundary", new DateTimeOffset(2024, 1, 1, 0, 0, 0, 0, TimeSpan.Zero))
        };

    /// <summary>
    /// <see cref="CronExpression.GetPreviousValidTimeBefore" /> walks the schedule backwards to the gap the
    /// expression describes: from the first firing after a given instant, back to the one before it.
    /// </summary>
    /// <remarks>
    /// The instants are literals because the answer depends on where the search starts, which is what
    /// made this test flake while it read <c>DateTimeOffset.UtcNow</c>. <c>0/30 1 2 * * ? *</c> fires
    /// twice a day, at 02:01:00 and at 02:01:30, so the gap back from a firing is 30 s for the second
    /// of the pair and 23:59:30 for the first. Search from inside the pair and the forward search lands
    /// on the second of the two, which swaps the row's two figures — for the thirty seconds from
    /// 02:01:00 to 02:01:29 UTC, and only those, this table was wrong. A sweep of a whole day confirms
    /// it is exactly 30 seconds in 86,400, and #3511's Windows leg hit one of them on a branch that
    /// could not reach cron at all.
    /// </remarks>
    [TestCaseSource(nameof(TimeBeforeSearchInstants))]
    public void TestGetPreviousValidTimeBefore(string origin, DateTimeOffset now)
    {
        foreach (FiringGaps gaps in FiringGapsAround(now.Year))
        {
            CronExpression cron = new CronExpression(gaps.Expression, TimeZoneInfo.Utc);
            string searchedFrom = $"the {origin} instant {now.ToString("O", CultureInfo.InvariantCulture)}";

            DateTimeOffset? after = cron.GetTimeAfter(now);
            if (after is null)
            {
                cron.GetPreviousValidTimeBefore(now).Should().NotBeNull(
                    $"'{gaps.Expression}' fires only in a year already past, so every firing it has is before {searchedFrom}");
                continue;
            }

            DateTimeOffset? before = cron.GetPreviousValidTimeBefore(after.Value);
            if (gaps.MillisecondsBack is null)
            {
                before.Should().BeNull(
                    $"'{gaps.Expression}' fires only in a year still ahead, so the firing after {searchedFrom} is its very first");
                continue;
            }

            before.Should().NotBeNull(
                $"'{gaps.Expression}' has firings behind the one that follows {searchedFrom}");
            (after.Value - before!.Value).Should().Be(
                TimeSpan.FromMilliseconds(gaps.MillisecondsBack.Value),
                $"'{gaps.Expression}' puts that gap behind the firing that follows {searchedFrom}");

            DateTimeOffset? next = cron.GetTimeAfter(after.Value);
            next.Should().NotBeNull(
                $"'{gaps.Expression}' repeats forever, so there is a firing beyond the one that follows {searchedFrom}");
            (next!.Value - after.Value).Should().Be(
                TimeSpan.FromMilliseconds(gaps.MillisecondsForward!.Value),
                $"'{gaps.Expression}' puts that gap ahead of the firing that follows {searchedFrom}");
        }
    }

    /// <summary>
    /// The half-minute that used to break <see cref="TestGetPreviousValidTimeBefore" />, pinned rather than avoided:
    /// an expression with two firings a day, searched from between them.
    /// </summary>
    /// <remarks>
    /// Nothing was ever wrong with the answer. <c>0/30 1 2 * * ? *</c> genuinely has 30 seconds behind
    /// the second firing of its pair and 23:59:30 ahead of it; it was the table that assumed the
    /// forward search always landed on the first of the pair, which it does from everywhere but here.
    /// </remarks>
    [Test]
    public void TestGetPreviousValidTimeBeforeSearchedFromInsideAPairOfFirings()
    {
        CronExpression cron = new CronExpression("0/30 1 2 * * ? *", TimeZoneInfo.Utc);
        DateTimeOffset insideThePair = new DateTimeOffset(2024, 3, 14, 2, 1, 10, TimeSpan.Zero);

        DateTimeOffset after = cron.GetTimeAfter(insideThePair)!.Value;

        after.Should().Be(new DateTimeOffset(2024, 3, 14, 2, 1, 30, TimeSpan.Zero),
            "the day's second firing is still ahead when the search starts ten seconds into the pair");
        (after - cron.GetPreviousValidTimeBefore(after)!.Value).Should().Be(TimeSpan.FromSeconds(30),
            "the firing behind it is the first of that same pair, half a minute earlier");
        (cron.GetTimeAfter(after)!.Value - after).Should().Be(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(30),
            "the firing ahead of it is the first of tomorrow's pair, not another 24 hours away");
    }
}