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

using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Simpl;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(BinaryObjectSerializer))]
[TestFixture(typeof(JsonObjectSerializer))]
public class MonthlyCalendarTest : SerializationTestSupport<MonthlyCalendar, ICalendar>
{
    private MonthlyCalendar cal;

    public MonthlyCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [SetUp]
    public void Setup()
    {
        cal = new MonthlyCalendar();
    }

    [Test]
    public void TestAddAndRemoveExclusion()
    {
        cal.SetDayExcluded(15, true);
        Assert.IsTrue(cal.IsDayExcluded(15));
        cal.SetDayExcluded(15, false);
        Assert.IsFalse(cal.IsDayExcluded(15));
    }

    [Test]
    public void TestMonthDayExclusion()
    {
        DateTime excluded = new DateTime(2007, 8, 3);
        cal.SetDayExcluded(3, true);
        Assert.AreEqual(excluded.AddDays(1), cal.GetNextIncludedTimeUtc(excluded).DateTime);
    }

    [Test]
    public void TestForInfiniteLoop()
    {
        MonthlyCalendar monthlyCalendar = new MonthlyCalendar();

        for (int i = 1; i < 9; i++)
        {
            monthlyCalendar.SetDayExcluded(i, true);
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

        monthlyCalendar.SetDayExcluded(4, true);

        // 11/5/2012 12:00:00 AM -04:00  translate into 11/4/2012 11:00:00 PM -05:00 (EST)
        DateTimeOffset date = new DateTimeOffset(2012, 11, 5, 0, 0, 0, TimeSpan.FromHours(-4));

        Assert.IsFalse(monthlyCalendar.IsTimeIncluded(date));
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
        c.SetDayExcluded(4, true);
        return c;
    }

    protected override void VerifyMatch(MonthlyCalendar original, MonthlyCalendar deserialized)
    {
        Assert.IsNotNull(deserialized);
        Assert.AreEqual(original.Description, deserialized.Description);
        Assert.AreEqual(original.DaysExcluded, deserialized.DaysExcluded);
        Assert.AreEqual(original.TimeZone, deserialized.TimeZone);
    }
}