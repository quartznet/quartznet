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
using System.Collections;
using System.Globalization;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(BinaryObjectSerializer))]
[TestFixture(typeof(JsonObjectSerializer))]
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
        CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005");
        cronExpression.TimeZone = testTimeZone;

        return cronExpression;
    }

    protected override void VerifyMatch(CronExpression original, CronExpression deserialized)
    {
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.CronExpressionString, deserialized.CronExpressionString);
        Assert.AreEqual(original.TimeZone, deserialized.TimeZone);
    }

    /// <summary>
    /// Test method for 'CronExpression.IsSatisfiedBy(DateTime)'.
    /// </summary>
    [Test]
    public void TestIsSatisfiedBy()
    {
        CronExpression cronExpression = new CronExpression("0 15 10 * * ? 2005");

        DateTime cal = new DateTime(2005, 6, 1, 10, 15, 0).ToUniversalTime();
        Assert.IsTrue(cronExpression.IsSatisfiedBy(cal));

        cal = cal.AddYears(1);
        Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

        cal = new DateTime(2005, 6, 1, 10, 16, 0).ToUniversalTime();
        Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

        cal = new DateTime(2005, 6, 1, 10, 14, 0).ToUniversalTime();
        Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

        cronExpression = new CronExpression("0 15 10 ? * MON-FRI");

        // weekends
        cal = new DateTime(2007, 6, 9, 10, 15, 0).ToUniversalTime();
        Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));
        Assert.IsFalse(cronExpression.IsSatisfiedBy(cal.AddDays(1)));
    }

    [Test]
    public void TestLastDayOffset()
    {
        CronExpression cronExpression = new CronExpression("0 15 10 L-2 * ? 2010");

        DateTime cal = new DateTime(2010, 10, 29, 10, 15, 0).ToUniversalTime(); // last day - 2
        Assert.IsTrue(cronExpression.IsSatisfiedBy(cal));

        cal = new DateTime(2010, 10, 28, 10, 15, 0).ToUniversalTime();
        Assert.IsFalse(cronExpression.IsSatisfiedBy(cal));

        cronExpression = new CronExpression("0 15 10 L-5W * ? 2010");

        cal = new DateTime(2010, 10, 26, 10, 15, 0).ToUniversalTime(); // last day - 5
        Assert.IsTrue(cronExpression.IsSatisfiedBy(cal));

        cronExpression = new CronExpression("0 15 10 L-1 * ? 2010");

        cal = new DateTime(2010, 10, 30, 10, 15, 0).ToUniversalTime(); // last day - 1
        Assert.IsTrue(cronExpression.IsSatisfiedBy(cal));

        cronExpression = new CronExpression("0 15 10 L-1W * ? 2010");

        cal = new DateTime(2010, 10, 29, 10, 15, 0).ToUniversalTime(); // nearest weekday to last day - 1 (29th is a friday in 2010)
        Assert.IsTrue(cronExpression.IsSatisfiedBy(cal));
    }

    // October 2010: 1st is a Friday, 29th is a Friday, 30th a Saturday, 31st a Sunday
    // (so the last weekday of the month is the 29th).
    [TestCase("0 15 10 1,L * ? 2010", new[] { 1, 31 })]
    [TestCase("0 15 10 15,L * ? 2010", new[] { 15, 31 })]
    [TestCase("0 15 10 15,31 * ? 2010", new[] { 15, 31 })]
    [TestCase("0 15 10 15,L-2 * ? 2010", new[] { 15, 31 - 2 })]
    [TestCase("0 15 10 31,L-2 * ? 2010", new[] { 29, 31 }, "explicit day + last-2 both fire")]
    [TestCase("0 15 10 6,15,L * ? 2010", new[] { 6, 15, 31 })]
    [TestCase("0 15 10 1,3,6,15,L * ? 2010", new[] { 1, 3, 6, 15, 31 })]
    [TestCase("0 15 10 6,15,LW * ? 2010", new[] { 6, 15, 29 })] // 31 Oct 2010 is a Sunday, last weekday is 29th
    [TestCase("0 15 10 15,LW-2 * ? 2010", new[] { 15, 29 - 2 })] // last weekday (29th) minus 2
    [TestCase("0 15 10 2W,16 * ? 2010", new[] { 1, 16 })] // 2nd is a Saturday, nearest weekday is the 1st
    [TestCase("0 15 10 16W,2 * ? 2010", new[] { 2, 15 })] // each W shifts its own day: 16W (Sat)->Fri 15; plain 2 fires raw
    [TestCase("0 15 10 2W,16W * ? 2010", new[] { 1, 15 }, "two nearest-weekday days")] // 2W->1, 16W->15
    [TestCase("0 15 10 L-1W,L-1 * ? 2010", new[] { 29, 30 })] // last-1 (30th, Sat) and its nearest weekday (29th, Fri)
    [TestCase("0 15 10 L-1,L-2 * ? 2010", new[] { 29, 30 }, "multiple L offsets")]
    [TestCase("0 15 10 1,5,29,L * ? 2010", new[] { 1, 5, 29, 31 }, "QUARTZ-640: previously rejected")]
    public void TestLastDayOfMonthWithOtherDays(string cronExpression, int[] expectedDays, string scenario = "")
    {
        var expr = new CronExpression(cronExpression); // 10:15am on the given days of October 2010

        foreach (var expectedDay in expectedDays)
        {
            var date = new DateTime(2010, 10, expectedDay, 10, 15, 0).ToUniversalTime();
            expr.IsSatisfiedBy(date).Should().BeTrue($"day {expectedDay} should fire ({scenario})");
        }
    }

    [Test]
    public void TestMultipleLastDayInstancesAreSupported()
    {
        // Previously all 'L' with other days threw; multiple 'L' offsets now parse
        // and each contributes an independent candidate day.
        var expr = new CronExpression("0 15 10 L,L-1,L-2 * ? 2010");
        foreach (var day in new[] { 29, 30, 31 }) // last three days of October 2010
        {
            var date = new DateTime(2010, 10, day, 10, 15, 0).ToUniversalTime();
            expr.IsSatisfiedBy(date).Should().BeTrue($"day {day} should fire");
        }

        // A day that is none of L, L-1, L-2 must not fire.
        expr.IsSatisfiedBy(new DateTime(2010, 10, 28, 10, 15, 0).ToUniversalTime()).Should().BeFalse();
    }

    [Test]
    public void TestLastDayWithOtherDaysGetNextValidTimeAfter()
    {
        // GetNextValidTimeAfter must never return a time before the query, even
        // when a 'W' modifier shifts to an earlier weekday that has already passed.
        var expr = new CronExpression("0 15 10 2W,16 * ? 2010") { TimeZone = TimeZoneInfo.Utc };

        // 2nd is a Saturday -> nearest weekday is the 1st. Querying after the 1st
        // (but before the 16th) must jump forward to the 16th, not back to the 1st.
        var next = expr.GetNextValidTimeAfter(new DateTimeOffset(2010, 10, 5, 0, 0, 0, TimeSpan.Zero));
        next.Should().Be(new DateTimeOffset(2010, 10, 16, 10, 15, 0, TimeSpan.Zero));

        // Before the 1st -> the 1st fires.
        next = expr.GetNextValidTimeAfter(new DateTimeOffset(2010, 9, 30, 0, 0, 0, TimeSpan.Zero));
        next.Should().Be(new DateTimeOffset(2010, 10, 1, 10, 15, 0, TimeSpan.Zero));
    }

    [Test]
    public void TestSerializationRoundTripWithLastDayAndWeekday()
    {
        var original = new CronExpression("0 15 10 1,L-1,LW,2W * ? 2010");

        var data = serializer.Serialize(original);
        var deserialized = serializer.DeSerialize<CronExpression>(data);

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

    [Test]
    public void CronExpression_Throw_Error_Contructed_With_Null()
    {
        Action act = () => new CronExpression(null);
        act.Should().Throw<ArgumentException>()
            .WithMessage($"cronExpression cannot be null*");
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

    [Test]
    public void TestCronExpressionPassingMidnight()
    {
        CronExpression cronExpression = new CronExpression("0 15 23 * * ?");
        DateTimeOffset cal = new DateTime(2005, 6, 1, 23, 16, 0).ToUniversalTime();
        DateTimeOffset nextExpectedFireTime = new DateTime(2005, 6, 2, 23, 15, 0).ToUniversalTime();
        Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
    }

    [Test]
    public void TestCronExpressionPassingYear()
    {
        DateTimeOffset start = new DateTime(2007, 12, 1, 23, 59, 59).ToUniversalTime();

        CronExpression ce = new CronExpression("0 55 15 1 * ?");
        DateTimeOffset expected = new DateTime(2008, 1, 1, 15, 55, 0).ToUniversalTime();
        DateTimeOffset d = ce.GetNextValidTimeAfter(start).Value;
        Assert.AreEqual(expected, d, "Got wrong date and time when passed year");
    }

    [Test]
    public void TestCronExpressionWeekdaysMonFri()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 ? * MON-FRI");
        int[] arrJuneDaysThatShouldFire =
            { 1, 4, 5, 6, 7, 8, 11, 12, 13, 14, 15, 18, 19, 20, 22, 21, 25, 26, 27, 28, 29 };
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
            { 1, 8, 15, 22, 29 };
        List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

        TestCorrectWeekFireDays(cronExpression, juneDays);
    }

    [Test]
    public void TestCronExpressionWeekdaysFridayEveryTwoWeeks()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 ? * FRI/2");
        var nextRunTime = cronExpression.GetTimeAfter(DateTimeOffset.Now);
        var nextRunTime2 = cronExpression.GetTimeAfter((DateTimeOffset) nextRunTime);

        int[] arrJuneDaysThatShouldFire =
            { 1, 15, 29 };
        List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

        TestCorrectWeekFireDays(cronExpression, juneDays);
    }

    [Test]
    public void TestCronExpressionWeekdaysThirsdayAndFridayEveryTwoWeeks()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 ? * THU,FRI/2");
        var nextRunTime = cronExpression.GetTimeAfter(DateTimeOffset.Now);
        var nextRunTime2 = cronExpression.GetTimeAfter((DateTimeOffset) nextRunTime);

        int[] arrJuneDaysThatShouldFire =
            { 1, 14, 15, 28, 29 };
        List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

        TestCorrectWeekFireDays(cronExpression, juneDays);
    }

    [Test]
    public void TestCronExpressionLastDayOfMonth()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 L * ?");
        int[] arrJuneDaysThatShouldFire = { 30 };
        List<int> juneDays = new List<int>(arrJuneDaysThatShouldFire);

        TestCorrectWeekFireDays(cronExpression, juneDays);
    }

    [Test]
    public void TestHourShift()
    {
        // cronexpression that fires every 5 seconds
        CronExpression cronExpression = new CronExpression("0/5 * * * * ?");
        DateTimeOffset cal = new DateTimeOffset(2005, 6, 1, 1, 59, 55, TimeSpan.Zero);
        DateTimeOffset nextExpectedFireTime = new DateTimeOffset(2005, 6, 1, 2, 0, 0, TimeSpan.Zero);
        Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
    }

    [Test]
    public void TestMonthShift()
    {
        // QRTZNET-28
        CronExpression cronExpression = new CronExpression("* * 1 * * ?");
        DateTimeOffset cal = new DateTime(2005, 7, 31, 22, 59, 57).ToUniversalTime();
        DateTimeOffset nextExpectedFireTime = new DateTime(2005, 8, 1, 1, 0, 0).ToUniversalTime();
        Assert.AreEqual(nextExpectedFireTime, cronExpression.GetTimeAfter(cal).Value);
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
            Assert.AreEqual("Day-of-Week values must be between 1 and 7", fe.Message);
        }
    }

    [Test]
    public void TestCronExpressionWithExtraWhiteSpace()
    {
        // test failed before because of improper trimming
        string expr = " 30 *   * * * ?  ";
        CronExpression calendar = new CronExpression(expr);
        Assert.IsFalse(calendar.IsSatisfiedBy(DateTime.UtcNow.Date.AddMinutes(2)), "Time was included");
    }

    private static void TestCorrectWeekFireDays(CronExpression cronExpression, IList<int> correctFireDays)
    {
        List<int> fireDays = new List<int>();

        DateTime cal = new DateTime(2007, 6, 1, 11, 0, 0).ToUniversalTime();
        DateTimeOffset? nextFireTime = cal;

        for (int i = 0; i < DateTime.DaysInMonth(2007, 6); ++i)
        {
            nextFireTime = cronExpression.GetTimeAfter((DateTimeOffset) nextFireTime);
            if (!fireDays.Contains(nextFireTime.Value.Day) && nextFireTime.Value.Month == 6 && nextFireTime.Value.Year == 2007)
            {
                // next fire day may be monday for several days..
                fireDays.Add(nextFireTime.Value.Day);
            }
            //cal = cal.AddDays(1);
        }

        // check rite dates fired
        for (int i = 0; i < fireDays.Count; ++i)
        {
            int idx = correctFireDays.IndexOf(fireDays[i]);
            Assert.Greater(idx, -1, $"CronExpression evaluated true for {fireDays[i]} even when it shouldn't have");
            correctFireDays.RemoveAt(idx);
        }

        // check that all fired
        Assert.IsTrue(correctFireDays.Count == 0, $"CronExpression did not evaluate true for all expected days (count: {correctFireDays.Count}).");
    }

    [Test]
    public void TestFormatExceptionWildCardDayOfMonthAndDayOfWeek()
    {
        Assert.Throws<FormatException>(() => new CronExpression("0 0 * * * *"), "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.");
    }

    [Test]
    public void TestFormatExceptionSpecifiedDayOfMonthAndWildCardDayOfWeek()
    {
        Assert.Throws<FormatException>(() => new CronExpression("0 0 * 4 * *"), "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.");
    }

    [Test]
    public void TestFormatExceptionWildCardDayOfMonthAndSpecifiedDayOfWeek()
    {
        Assert.Throws<FormatException>(() => new CronExpression("0 0 * * * 4"), "Support for specifying both a day-of-week AND a day-of-month parameter is not implemented.");
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
            Assert.AreEqual(shouldFire, satisfied);

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
            SimpleCronExpression cronExpression = new SimpleCronExpression(expression);
            ICollection<int> set = cronExpression.GetSetPublic(constant);
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
        // 'L' combined with other days of the month is now supported (see
        // TestLastDayOfMonthWithOtherDays); only the day-of-week combination
        // remains unsupported.
        try
        {
            new CronExpression("0 43 9 ? * SAT,SUN,L");
            Assert.Fail("Expected FormatException did not fire for L combined with other days of the week");
        }
        catch (FormatException pe)
        {
            Assert.IsTrue(
                pe.Message.StartsWith("Support for specifying 'L' with other days of the week is not implemented"),
                "Incorrect FormatException thrown");
        }

        try
        {
            new CronExpression("0 43 9 ? * 6,7,L");
            Assert.Fail("Expected FormatException did not fire for L combined with other days of the week");
        }
        catch (FormatException pe)
        {
            Assert.IsTrue(
                pe.Message.StartsWith("Support for specifying 'L' with other days of the week is not implemented"),
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
        Assert.IsTrue(after.HasValue);
        Assert.AreEqual(new DateTime(2009, 3, 29, 0, 0, 0).ToUniversalTime(), after.Value.DateTime);

        after = expression.GetNextValidTimeAfter(new DateTime(2009, 12, 30).ToUniversalTime());
        Assert.IsTrue(after.HasValue);
        Assert.AreEqual(new DateTime(2010, 1, 29, 0, 0, 0).ToUniversalTime(), after.Value.DateTime);
    }

    [Test]
    public void TestQRTZNET152()
    {
        CronExpression expression = new CronExpression("0 5 13 5W 1-12 ?");
        DateTimeOffset test = new DateTimeOffset(2009, 3, 8, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset d = expression.GetNextValidTimeAfter(test).Value;
        Assert.AreEqual(new DateTimeOffset(2009, 4, 6, 13, 5, 0, TimeZoneUtil.GetUtcOffset(d, TimeZoneInfo.Local)).ToUniversalTime(), d);
        d = expression.GetNextValidTimeAfter(d).Value;
        Assert.AreEqual(new DateTimeOffset(2009, 5, 5, 13, 5, 0, TimeZoneUtil.GetUtcOffset(d, TimeZoneInfo.Local)), d);
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
            Assert.IsTrue(pe.Message.StartsWith("The 'W' option does not make sense with values larger than"), "Incorrect ParseException thrown");
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
            // Console.WriteLine("fireTime: " + date + ", previousFireTime: " + pdate);
            Assert.False(pdate.Equals(date), "Next fire time is the same as previous fire time!");
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
            Assert.False(pdate.Equals(date), "Next fire time is the same as previous fire time!");
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
        Assert.IsTrue(after.HasValue);
        DateTimeOffset expected = daylightChange.Start.Add(daylightChange.Delta).AddMinutes(15).ToUniversalTime();
        Assert.AreEqual(expected, after.Value);
    }

    [Test]
    public void TestDaylightSavingsDoesNotMatchAnHourBefore()
    {
        TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        CronExpression expression = new CronExpression("0 15 15 5 11 ?");
        expression.TimeZone = est;

        DateTimeOffset startTime = new DateTimeOffset(2012, 11, 4, 0, 0, 0, TimeSpan.Zero);

        var actualTime = expression.GetTimeAfter(startTime);
        DateTimeOffset expected = new DateTimeOffset(2012, 11, 5, 15, 15, 0, TimeSpan.FromHours(-5));

        Assert.AreEqual(expected, actualTime.Value);
    }

    [Test]
    public void TestDaylightSavingsDoesNotMatchAnHourBefore2()
    {
        //another case
        TimeZoneInfo est = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        CronExpression expression = new CronExpression("0 0 0 ? * THU");
        expression.TimeZone = est;

        DateTimeOffset startTime = new DateTimeOffset(2012, 11, 4, 0, 0, 0, TimeSpan.Zero);

        var actualTime = expression.GetTimeAfter(startTime);
        DateTimeOffset expected = new DateTimeOffset(2012, 11, 8, 0, 0, 0, TimeSpan.FromHours(-5));
        Assert.AreEqual(expected, actualTime);
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
        Assert.That(CronExpression.IsValidExpression("* * * ? * *A&/5:"), Is.False);
        Assert.That(CronExpression.IsValidExpression("* * * ? *14 "), Is.False);
        Assert.That(CronExpression.IsValidExpression(" * * ? *A&/5 *"), Is.False);
        Assert.That(CronExpression.IsValidExpression("* * ? */5 *"), Is.False);
        Assert.That(CronExpression.IsValidExpression("* * ? */52 *"), Is.False);

        Assert.That(CronExpression.IsValidExpression("0 0/30 * * * ?"), Is.True);
        Assert.That(CronExpression.IsValidExpression("0 0/1 * * * ?"), Is.True);
        Assert.That(CronExpression.IsValidExpression("0 0/30 * * */2 ?"), Is.True);
    }

    [Test]
    public void TestInvalidCronExpressionCharacters()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CronExpression.IsValidExpression("0 0x 0/1 ? * * *"), Is.False);
            Assert.That(CronExpression.IsValidExpression("0 1xdds0 0/1 ? * * *"), Is.False);
        });
    }

    [Test]
    public void TestExtraCharactersAfterWeekDay()
    {
        Assert.That(CronExpression.IsValidExpression("0 0 15 ? * FRI*"), Is.False);
    }

    [Test]
    public void TestHourRangeAndSlash()
    {
        CronExpression.ValidateExpression("0 0 18-21/1 ? * MON,TUE,WED,THU,FRI,SAT,SUN");
    }

    private class SimpleCronExpression : CronExpression
    {
        public SimpleCronExpression(string cronExpression)
            : base(cronExpression)
        {
        }

        public ISet<int> GetSetPublic(int constant)
        {
            return GetSet(constant);
        }
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

            if (next == null)
                break;
        }

        Console.WriteLine("{0}ms", sw.ElapsedMilliseconds);
    }

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
    private sealed class FiringGaps
    {
        public FiringGaps(string expression, long? millisecondsBack, long? millisecondsForward)
        {
            Expression = expression;
            MillisecondsBack = millisecondsBack;
            MillisecondsForward = millisecondsForward;
        }

        public string Expression { get; }

        public long? MillisecondsBack { get; }

        public long? MillisecondsForward { get; }
    }

    /// <summary>
    /// The expressions <see cref="TestGetTimeBefore" /> walks, and the gaps they describe. The last two
    /// are pinned to a year either side of the year the search starts in: the first of them has its
    /// whole schedule ahead, so the firing found is its very first and nothing precedes it, and the
    /// second has its whole schedule behind, so there is no firing after the instant at all.
    /// </summary>
    private static FiringGaps[] FiringGapsAround(int year) => new[]
    {
        new FiringGaps("* * * * * ? *", 1000L, 1000L),
        new FiringGaps("0 * * * * ? *", 60_000L, 60_000L),
        new FiringGaps("0/15 * * * * ? *", 15_000L, 15_000L),
        new FiringGaps("0 0 5 * * ? *", OneDayInMilliseconds, OneDayInMilliseconds),
        new FiringGaps("0 0 0 * * ? *", OneDayInMilliseconds, OneDayInMilliseconds),
        new FiringGaps("0/30 1 2 * * ? *", OneDayInMilliseconds - 30_000L, 30_000L),
        new FiringGaps($"* * * * * ? {year + 2}", null, null),
        new FiringGaps($"* * * * * ? {year - 2}", null, null)
    };

    /// <summary>
    /// The instants <see cref="TestGetTimeBefore" /> searches from: an ordinary one, and one on each
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
    /// <see cref="CronExpression.GetTimeBefore" /> walks the schedule backwards to the gap the
    /// expression describes: from the first firing after a given instant, back to the one before it.
    /// </summary>
    /// <remarks>
    /// The instants are literals because the answer depends on where the search starts, which is what
    /// made this test flake while it read <c>DateTimeOffset.UtcNow</c>. <c>0/30 1 2 * * ? *</c> fires
    /// twice a day, at 02:01:00 and at 02:01:30, so the gap back from a firing is 30 s for the second
    /// of the pair and 23:59:30 for the first. Search from inside the pair and the forward search lands
    /// on the second of the two, which swaps the row's two figures — for the thirty seconds from
    /// 02:01:00 to 02:01:29 UTC, and only those, this table was wrong: one run in 2,880.
    /// </remarks>
    [TestCaseSource(nameof(TimeBeforeSearchInstants))]
    public void TestGetTimeBefore(string origin, DateTimeOffset now)
    {
        foreach (FiringGaps gaps in FiringGapsAround(now.Year))
        {
            CronExpression cron = new CronExpression(gaps.Expression) { TimeZone = TimeZoneInfo.Utc };
            string searchedFrom = $"the {origin} instant {now.ToString("O", CultureInfo.InvariantCulture)}";

            DateTimeOffset? after = cron.GetTimeAfter(now);
            if (after is null)
            {
                cron.GetTimeBefore(now).Should().NotBeNull(
                    $"'{gaps.Expression}' fires only in a year already past, so every firing it has is before {searchedFrom}");
                continue;
            }

            DateTimeOffset? before = cron.GetTimeBefore(after.Value);
            if (gaps.MillisecondsBack is null)
            {
                before.Should().BeNull(
                    $"'{gaps.Expression}' fires only in a year still ahead, so the firing after {searchedFrom} is its very first");
                continue;
            }

            before.Should().NotBeNull(
                $"'{gaps.Expression}' has firings behind the one that follows {searchedFrom}");
            (after.Value - before.Value).Should().Be(
                TimeSpan.FromMilliseconds(gaps.MillisecondsBack.Value),
                $"'{gaps.Expression}' puts that gap behind the firing that follows {searchedFrom}");

            DateTimeOffset? next = cron.GetTimeAfter(after.Value);
            next.Should().NotBeNull(
                $"'{gaps.Expression}' repeats forever, so there is a firing beyond the one that follows {searchedFrom}");
            (next.Value - after.Value).Should().Be(
                TimeSpan.FromMilliseconds(gaps.MillisecondsForward.Value),
                $"'{gaps.Expression}' puts that gap ahead of the firing that follows {searchedFrom}");
        }
    }

    /// <summary>
    /// The half-minute that used to break <see cref="TestGetTimeBefore" />, pinned rather than avoided:
    /// an expression with two firings a day, searched from between them.
    /// </summary>
    /// <remarks>
    /// Nothing was ever wrong with the answer. <c>0/30 1 2 * * ? *</c> genuinely has 30 seconds behind
    /// the second firing of its pair and 23:59:30 ahead of it; it was the table that assumed the
    /// forward search always landed on the first of the pair, which it does from everywhere but here.
    /// </remarks>
    [Test]
    public void TestGetTimeBeforeSearchedFromInsideAPairOfFirings()
    {
        CronExpression cron = new CronExpression("0/30 1 2 * * ? *") { TimeZone = TimeZoneInfo.Utc };
        DateTimeOffset insideThePair = new DateTimeOffset(2024, 3, 14, 2, 1, 10, TimeSpan.Zero);

        DateTimeOffset after = cron.GetTimeAfter(insideThePair).Value;

        after.Should().Be(new DateTimeOffset(2024, 3, 14, 2, 1, 30, TimeSpan.Zero),
            "the day's second firing is still ahead when the search starts ten seconds into the pair");
        (after - cron.GetTimeBefore(after).Value).Should().Be(TimeSpan.FromSeconds(30),
            "the firing behind it is the first of that same pair, half a minute earlier");
        (cron.GetTimeAfter(after).Value - after).Should().Be(TimeSpan.FromHours(24) - TimeSpan.FromSeconds(30),
            "the firing ahead of it is the first of tomorrow's pair, not another 24 hours away");
    }

    [Test]
    public void GetTimeBeforeDoesNotThrowForPositiveOffsetTimeZone()
    {
        // Issue #3046: GetTimeBefore threw ArgumentOutOfRangeException when the
        // timezone had a positive UTC offset, because the inverse binary search
        // stepped into year 1 where internal DateTimeOffset constructions inside
        // GetTimeAfter produced a UTC instant before year 1.
        TimeZoneInfo plus11 = TimeZoneInfo.CreateCustomTimeZone(
            "Test+11", TimeSpan.FromHours(11), "Test+11", "Test+11");
        CronExpression cron = new CronExpression("0 0 0 ? * FRI *") { TimeZone = plus11 };
        DateTimeOffset time = new DateTimeOffset(2026, 4, 16, 0, 0, 0, TimeSpan.Zero);

        DateTimeOffset? before = null;
        Assert.DoesNotThrow(() => before = cron.GetTimeBefore(time));
        Assert.IsNotNull(before);
        Assert.IsTrue(before!.Value < time);
    }

    [TestCaseSource(typeof(CronTestScenarios), nameof(CronTestScenarios.TestCases))]
    public void CronExpressionReturnsExpectedNextFireTime(CronExpression cronExpression, DateTimeOffset timeAfterDate, DateTimeOffset expectedNextFireTime)
    {
        cronExpression.TimeZone = TimeZoneInfo.Utc;
        var nextFireTime = cronExpression.GetTimeAfter(timeAfterDate);
        nextFireTime.Value.Date.Should().Be(expectedNextFireTime.Date, "NextFireTime was not correct");
    }

    /// <summary>
    /// Test for issue where 31W causes ArgumentOutOfRangeException when transitioning
    /// from a month with 31 days to a month with fewer days (e.g., January to February).
    /// </summary>
    [Test]
    public void Test31W_JanuaryToFebruary_ShouldNotThrow()
    {
        CronExpression cronExpression = new CronExpression("0 0 12 31W * ?");
        DateTime cal = new DateTime(2024, 1, 31, 12, 0, 0).ToUniversalTime();

        var nextFireTime = cronExpression.GetTimeAfter((DateTimeOffset)cal);

        Assert.IsNotNull(nextFireTime);
        // February 2024 has 29 days (leap year), so 31W should fire on Feb 29 (Friday)
        Assert.AreEqual(2024, nextFireTime.Value.Year);
        Assert.AreEqual(2, nextFireTime.Value.Month);
        Assert.AreEqual(29, nextFireTime.Value.Day);
    }
}

public class CronTestScenarios
    {
        private class TestCaseProps
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
    }