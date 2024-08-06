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

using FluentAssertions;

using Quartz.Impl.Calendar;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class CronCalendarTest : SerializationTestSupport<CronCalendar, ICalendar>
{
    public CronCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [TestCaseSource(typeof(CronWeekdayModifierTestData), nameof(CronWeekdayModifierTestData.TestCases))]
    public void CronWeekdayModifierReturnsNextExpectedFireTimeSet(CronExpression cronExpression, DateTimeOffset timeAfterDate, DateTimeOffset expectedNextFireTime)    {
        var nextFireTime = cronExpression.GetTimeAfter(timeAfterDate);
        nextFireTime.Value.Date.Should().Be(expectedNextFireTime.Date, "NextFireTime was not correct");
    }

    [Test]
    public void TestTimeIncluded()
    {
        CronCalendar calendar = new CronCalendar("0/15 * * * * ?");
        string fault = "Time was included when it was not supposed to be";
        DateTime tst = DateTime.UtcNow.AddMinutes(2);
        tst = new DateTime(tst.Year, tst.Month, tst.Day, tst.Hour, tst.Minute, 30);
        Assert.IsFalse(calendar.IsTimeIncluded(tst), fault);

        calendar.SetCronExpressionString("0/25 * * * * ?");
        fault = "Time was not included as expected";
        Assert.IsTrue(calendar.IsTimeIncluded(tst), fault);
    }

    [Test]
    public void TestClone()
    {
        CronCalendar calendar = new CronCalendar("0/15 * * * * ?");
        CronCalendar clone = (CronCalendar)calendar.Clone();
        Assert.AreEqual(calendar.CronExpression, clone.CronExpression);
    }

    [Test]
    public void MillisecondsShouldBeIgnored()
    {
        var calendar = new CronCalendar("* * 1-3 ? * *")
        {
            TimeZone = TimeZoneInfo.Utc
        };
        var dateTime = new DateTimeOffset(2017, 7, 27, 2, 0, 1, 123, TimeSpan.Zero);
        Assert.IsFalse(calendar.IsTimeIncluded(dateTime));
    }

    protected override CronCalendar GetTargetObject()
    {
        return new CronCalendar("* * 1-3 ? * *")
        {
            Description = "my description"
        };
    }

    protected override void VerifyMatch(CronCalendar original, CronCalendar deserialized)
    {
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Description, deserialized.Description);
        Assert.AreEqual(original.CronExpression, deserialized.CronExpression);
        Assert.AreEqual(original.TimeZone, deserialized.TimeZone);
    }
}

public class CronWeekdayModifierTestData
{
    private class Model
    {
        public CronExpression CronExpression { get; init; }

        public DateTimeOffset TimeAfterDate { get; init; }

        public DateTimeOffset ExpectedNextFireTime { get; init; }

        public string TestCase { get; init; }
    }

    private static IEnumerable<Model> TestCaseModels => new[]
    {
        new Model
        {
            CronExpression = new CronExpression("0 0 12 15W * ?"),
            TimeAfterDate = new DateTimeOffset(2024, 5, 15, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2024, 6, 14, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on Weekday 15th Every Month - 2024-06-15 is a Sat, schedule should be Fri 14th"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 15W * ?"),
            TimeAfterDate = new DateTimeOffset(2024, 8, 15, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2024, 9, 16, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on Weekday 15th Every Month - 2024-09-15 is a Sunday, expect schedule to be Mon 16th"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 15W * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 12, 15, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2024, 1, 15, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on Weekday 15th Every Month - 2024-01-15 is Monday, should run on Monday"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 31W * ?"),
            TimeAfterDate = new DateTimeOffset(2025, 1, 31, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2025, 2, 28, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Issue #2330 where exception moving to next month with less days in month"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 LW * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 2, 28, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 3, 31, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on last weekday of the month - 2023-03-31 is a Friday"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 L-2 * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 4, 28, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 5, 29, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the second-to-last day of the month"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 ? * 6L"),
            TimeAfterDate = new DateTimeOffset(2023, 6, 24, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 6, 30, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the last Friday of the month - 2023-06-30 is the last Friday"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 ? * 6#3"),
            TimeAfterDate = new DateTimeOffset(2023, 7, 21, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 8, 18, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the third Friday of the month"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 ? * 2/2"),
            TimeAfterDate = new DateTimeOffset(2023, 9, 5, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 9, 6, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run every second day (/2) starting monday (2)"
        },
        new Model
        {
            CronExpression = new CronExpression("0 0 12 1W * ?"),
            TimeAfterDate = new DateTimeOffset(2023, 10, 1, 12, 0, 0, TimeSpan.Zero),
            ExpectedNextFireTime = new DateTimeOffset(2023, 10, 2, 12, 0, 0, TimeSpan.Zero),
            TestCase = "Run on the first weekday of the month - 2023-10-01 is a Sunday, expect schedule to be Mon 2nd"
        }
    };

    public static IEnumerable TestCases => TestCaseModels.Select(model => new TestCaseData(model.CronExpression, model.TimeAfterDate, model.ExpectedNextFireTime));
}