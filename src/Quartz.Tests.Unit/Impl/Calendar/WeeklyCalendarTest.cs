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
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
public class WeeklyCalendarTest : SerializationTestSupport<WeeklyCalendar, ICalendar>
{
    private WeeklyCalendar cal;

    public WeeklyCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [SetUp]
    public void Setup()
    {
        cal = new WeeklyCalendar();
        cal.TimeZone = TimeZoneInfo.Utc; //assume utc if not specified.
    }

    [Test]
    public void TestAddAndRemoveExclusion()
    {
        cal.SetDayExcluded(DayOfWeek.Monday, true);
        Assert.That(cal.IsDayExcluded(DayOfWeek.Monday), Is.True);
        cal.SetDayExcluded(DayOfWeek.Monday, false);
        Assert.That(cal.IsDayExcluded(DayOfWeek.Monday), Is.False);
    }

    [Test]
    public void TestWeekDayExclusion()
    {
        // this is friday
        DateTimeOffset excluded = new DateTimeOffset(2007, 8, 3, 0, 0, 0, TimeSpan.Zero);
        cal.SetDayExcluded(DayOfWeek.Friday, true);
        // next monday should be next possible
        Assert.That(cal.GetNextIncludedTimeUtc(excluded), Is.EqualTo(excluded.AddDays(3)));
    }


    [Test]
    public void TestDaylightSavingTransition()
    {
        cal.TimeZone = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        cal.SetDayExcluded(DayOfWeek.Monday, false); //Monday only
        cal.SetDayExcluded(DayOfWeek.Tuesday, true);
        cal.SetDayExcluded(DayOfWeek.Wednesday, true);
        cal.SetDayExcluded(DayOfWeek.Thursday, true);
        cal.SetDayExcluded(DayOfWeek.Friday, true);
        cal.SetDayExcluded(DayOfWeek.Saturday, true);
        cal.SetDayExcluded(DayOfWeek.Sunday, true);

        //11/5/2012 12:00:00 AM -04:00 will translate into 11/4/2012 11:00:00 PM -05:00, which is a Sunday, not monday
        DateTimeOffset date = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-4));
        Assert.That(cal.IsTimeIncluded(date), Is.False);

        date = cal.GetNextIncludedTimeUtc(date);
        DateTimeOffset expected = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-5));

        Assert.That(date, Is.EqualTo(expected));
    }



    /// <summary>
    /// Get the object to serialize when generating serialized file for future
    /// tests, and against which to validate deserialized object.
    /// </summary>
    /// <returns></returns>
    protected override WeeklyCalendar GetTargetObject()
    {
        WeeklyCalendar c = new WeeklyCalendar();
        c.Description = "description";
        c.SetDayExcluded(DayOfWeek.Thursday, true);
        return c;
    }

    protected override void VerifyMatch(WeeklyCalendar original, WeeklyCalendar deserialized)
    {
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Description, Is.EqualTo(original.Description));
            Assert.That(deserialized.DaysExcluded, Is.EqualTo(original.DaysExcluded));
            Assert.That(deserialized.TimeZone, Is.EqualTo(original.TimeZone));
        });
    }
}