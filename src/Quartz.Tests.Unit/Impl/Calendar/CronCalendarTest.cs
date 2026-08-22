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
using System.Threading.Tasks;

using Quartz.Impl.Calendar;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(BinaryObjectSerializer))]
[TestFixture(typeof(JsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
[NonParallelizable]
public class CronCalendarTest : SerializationTestSupport<CronCalendar, ICalendar>
{
    public CronCalendarTest(Type serializerType) : base(serializerType)
    {
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
        Assert.That(calendar.IsTimeIncluded(dateTime), Is.False);
    }

    /// <summary>
    /// The three-argument constructor used to hand the zone to <see cref="BaseCalendar" /> and build
    /// the expression without it, while <see cref="CronCalendar.TimeZone" /> reads the zone back off
    /// that expression - so the argument was silently dropped and the calendar excluded local hours.
    /// Only the property setter rebuilt the expression correctly.
    /// </summary>
    [Test]
    public void ConstructorTimeZoneReachesTheExpression()
    {
        // America/New_York, which is a plain UTC-5 in January: no DST corner is in play here.
        TimeZoneInfo eastern = TestTimeZones.Eastern;

        CronCalendar calendar = new CronCalendar(null, "* * 9 ? * *", eastern);

        calendar.TimeZone.Should().Be(eastern,
            "the constructor's zone is what the calendar reports, not the machine's local zone");
        calendar.CronExpression.TimeZone.Should().Be(eastern,
            "TimeZone reads off the expression, so the expression is where the zone has to land");

        DateTimeOffset nineThirtyEastern = new DateTimeOffset(2026, 1, 1, 14, 30, 0, TimeSpan.Zero);
        DateTimeOffset fourThirtyEastern = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero);

        calendar.IsTimeIncluded(nineThirtyEastern).Should().BeFalse(
            "14:30Z is 09:30 in New York, the hour the expression excludes");
        calendar.IsTimeIncluded(fourThirtyEastern).Should().BeTrue(
            "09:30Z is 04:30 in New York, well outside the excluded hour");

        // The same expression pinned to UTC reads the two instants the other way round, which is
        // what makes the assertions above about the zone rather than about the expression.
        CronCalendar utc = new CronCalendar(null, "* * 9 ? * *", TimeZoneInfo.Utc);

        utc.IsTimeIncluded(fourThirtyEastern).Should().BeFalse("09:30Z is inside the excluded hour in UTC");
        utc.IsTimeIncluded(nineThirtyEastern).Should().BeTrue("14:30Z is outside the excluded hour in UTC");
    }

    /// <summary>
    /// From an instant the calendar EXCLUDES, the next-included search used to walk forward with
    /// <see cref="CronExpression.GetNextValidTimeAfter" /> - which by definition lands on another
    /// satisfied, i.e. excluded, instant - so it crawled the excluded run millisecond by millisecond
    /// and, with no base calendar to leap it forward, never returned at all. The end of the excluded
    /// range is <see cref="CronExpression.GetNextInvalidTimeAfter" />, which is what Java's
    /// CronCalendar always used.
    /// </summary>
    [Test]
    public async Task NextIncludedTimeFromAnExcludedInstantIsTheEndOfTheExcludedRange()
    {
        CronCalendar calendar = new CronCalendar("* * 9 ? * *")
        {
            TimeZone = TimeZoneInfo.Utc
        };
        DateTimeOffset insideTheExcludedHour = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero);

        // Through a task with a deadline so a regression fails the test instead of hanging the run.
        Task<DateTimeOffset> search = Task.Run(() => calendar.GetNextIncludedTimeUtc(insideTheExcludedHour));
        Task finished = await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.Should().BeSameAs(search,
            "the search must step to the end of the excluded range, not crawl it millisecond by millisecond");
        (await search).Should().Be(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            "the first included instant after 09:30 is the top of the next hour, where the expression stops matching");
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