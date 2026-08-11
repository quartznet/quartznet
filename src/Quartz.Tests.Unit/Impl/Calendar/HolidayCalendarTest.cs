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
public class HolidayCalendarTest : SerializationTestSupport<HolidayCalendar, ICalendar>
{
    private HolidayCalendar calendar;

    public HolidayCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [SetUp]
    public void Setup()
    {
        calendar = new HolidayCalendar();
    }

    [Test]
    public void TestAddAndRemoveExclusion()
    {
        calendar.AddExcludedDay(new DateOnly(2007, 10, 20)).Should().BeTrue();
        calendar.RemoveExcludedDay(new DateOnly(2007, 10, 20)).Should().BeTrue();
        calendar.DaysExcluded.Should().BeEmpty();
    }

    [Test]
    public void TestDayExclusion()
    {
        // use end of day to get by with utc offsets
        DateTime excluded = new DateTime(2007, 12, 31);
        calendar.AddExcludedDay(DateOnly.FromDateTime(excluded));

        Assert.That(calendar.GetNextIncludedTimeUtc(excluded), Is.EqualTo(new DateTimeOffset(2008, 1, 1, 0, 0, 0, calendar.TimeZone.BaseUtcOffset)));
    }

    /// <summary>
    /// Get the object to serialize when generating serialized file for future
    /// tests, and against which to validate deserialized object.
    /// </summary>
    /// <returns></returns>
    protected override HolidayCalendar GetTargetObject()
    {
        HolidayCalendar c = new HolidayCalendar();
        c.Description = "description";
        DateOnly date = new DateOnly(2005, 1, 20);
        c.AddExcludedDay(date);
        return c;
    }

    [Test]
    public void TestTimeZone()
    {
        TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        HolidayCalendar c = new HolidayCalendar();
        c.TimeZone = tz;

        DateTimeOffset excludedDay = new DateTimeOffset(2012, 11, 4, 0, 0, 0, TimeSpan.Zero);
        c.AddExcludedDay(DateOnly.FromDateTime(excludedDay.DateTime));

        // 11/5/2012 12:00:00 AM -04:00  translate into 11/4/2012 11:00:00 PM -05:00 (EST)
        DateTimeOffset date = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-4));

        Assert.Multiple(() =>
        {
            Assert.That(c.IsTimeIncluded(date), Is.False, "date was expected to not be included.");
            Assert.That(c.IsTimeIncluded(date.AddDays(1)), Is.True);
        });

        DateTimeOffset expectedNextAvailable = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-5));
        DateTimeOffset actualNextAvailable = c.GetNextIncludedTimeUtc(date);
        Assert.That(actualNextAvailable, Is.EqualTo(expectedNextAvailable));
    }

    protected override void VerifyMatch(HolidayCalendar original, HolidayCalendar deserialized)
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