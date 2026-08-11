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

using Quartz.Impl.Calendar;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
[NonParallelizable]
public class MonthlyCalendarTest : SerializationTestSupport<MonthlyCalendar, ICalendar>
{
    private MonthlyCalendar calendar;

    public MonthlyCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [SetUp]
    public void Setup()
    {
        calendar = new MonthlyCalendar();
    }

    [Test]
    public void TestAddAndRemoveExclusion()
    {
        calendar.AddExcludedDay(15).Should().BeTrue();
        calendar.IsDayExcluded(15).Should().BeTrue();
        calendar.RemoveExcludedDay(15).Should().BeTrue();
        calendar.IsDayExcluded(15).Should().BeFalse();
    }

    [Test]
    public void TestMonthDayExclusion()
    {
        DateTime excluded = new DateTime(2007, 8, 3);
        calendar.AddExcludedDay(3);
        Assert.That(calendar.GetNextIncludedTimeUtc(excluded).DateTime, Is.EqualTo(excluded.AddDays(1)));
    }

    [Test]
    public void TestForInfiniteLoop()
    {
        MonthlyCalendar monthlyCalendar = new MonthlyCalendar();

        for (int i = 1; i < 9; i++)
        {
            monthlyCalendar.AddExcludedDay(i);
        }

        DateTime d = new DateTime(2007, 11, 8, 12, 0, 0);

        monthlyCalendar.GetNextIncludedTimeUtc(d.ToUniversalTime());
    }

    [Test]
    public void TestTimeZone()
    {
        TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        MonthlyCalendar monthlyCalendar = new MonthlyCalendar();
        monthlyCalendar.TimeZone = tz;

        monthlyCalendar.AddExcludedDay(4);

        // 11/5/2012 12:00:00 AM -04:00  translate into 11/4/2012 11:00:00 PM -05:00 (EST)
        DateTimeOffset date = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-4));

        Assert.That(monthlyCalendar.IsTimeIncluded(date), Is.False);
    }

    /// <summary>
    /// Get the object to serialize when generating serialized file for future
    /// tests, and against which to validate deserialized object.
    /// </summary>
    /// <returns></returns>
    protected override MonthlyCalendar GetTargetObject()
    {
        MonthlyCalendar c = new MonthlyCalendar();
        c.Description = "description";
        c.AddExcludedDay(4);
        return c;
    }

    protected override void VerifyMatch(MonthlyCalendar original, MonthlyCalendar deserialized)
    {
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Description, Is.EqualTo(original.Description));
            Assert.That(deserialized.DaysExcluded, Is.EquivalentTo(original.DaysExcluded));
            Assert.That(deserialized.TimeZone, Is.EqualTo(original.TimeZone));
        });
    }
}