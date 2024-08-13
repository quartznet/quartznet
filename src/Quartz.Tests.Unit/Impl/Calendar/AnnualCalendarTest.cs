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
public class AnnualCalendarTest : SerializationTestSupport<AnnualCalendar, ICalendar>
{
    private AnnualCalendar cal;

    public AnnualCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [SetUp]
    public void Setup()
    {
        cal = new AnnualCalendar();
    }

    [Test]
    public void TestDayExclusion()
    {
        // we're local by default
        DateTime d = new DateTime(2005, 1, 1);
        cal.SetDayExcluded(d, true);
        Assert.Multiple(() =>
        {
            Assert.That(cal.IsTimeIncluded(d.ToUniversalTime()), Is.False, "Time was included when it was supposed not to be");
            Assert.That(cal.IsDayExcluded(d), Is.True, "Day was not excluded when it was supposed to be excluded");
            Assert.That(cal.DaysExcluded, Has.Count.EqualTo(1));
            Assert.That(cal.DaysExcluded.First().Day, Is.EqualTo(d.Day));
            Assert.That(cal.DaysExcluded.First().Month, Is.EqualTo(d.Month));
        });
    }

    [Test]
    public void TestDayInclusionAfterExclusion()
    {
        DateTime d = new DateTime(2005, 1, 1);
        cal.SetDayExcluded(d, true);
        cal.SetDayExcluded(d, false);
        cal.SetDayExcluded(d, false);
        Assert.Multiple(() =>
        {
            Assert.That(cal.IsTimeIncluded(d), Is.True, "Time was not included when it was supposed to be");
            Assert.That(cal.IsDayExcluded(d), Is.False, "Day was excluded when it was supposed to be included");
        });
    }

    [Test]
    public void TestDayExclusionDifferentYears()
    {
        string errMessage = "Day was not excluded when it was supposed to be excluded";
        DateTime d = new DateTime(2005, 1, 1);
        cal.SetDayExcluded(d, true);
        Assert.Multiple(() =>
        {
            Assert.That(cal.IsDayExcluded(d), Is.True, errMessage);
            Assert.That(cal.IsDayExcluded(d.AddYears(-2)), Is.True, errMessage);
            Assert.That(cal.IsDayExcluded(d.AddYears(2)), Is.True, errMessage);
            Assert.That(cal.IsDayExcluded(d.AddYears(100)), Is.True, errMessage);
        });
    }

    [Test]
    public void TestExclusionAndNextIncludedTime()
    {
        cal.DaysExcluded = null;
        DateTimeOffset test = DateTimeOffset.UtcNow.Date;
        Assert.That(cal.GetNextIncludedTimeUtc(test), Is.EqualTo(test), "Did not get today as date when nothing was excluded");

        cal.SetDayExcluded(test.Date, true);
        Assert.That(cal.GetNextIncludedTimeUtc(test), Is.EqualTo(test.AddDays(1)), "Did not get next day when current day excluded");
    }

    /// <summary>
    /// QUARTZ-679 Test if the annualCalendar works over years.
    /// </summary>
    [Test]
    public void TestDaysExcludedOverTime()
    {
        AnnualCalendar annualCalendar = new AnnualCalendar();

        DateTime day = new DateTime(2005, 6, 23);
        annualCalendar.SetDayExcluded(day, true);

        day = new DateTime(2008, 2, 1);
        annualCalendar.SetDayExcluded(day, true);

        Assert.That(annualCalendar.IsDayExcluded(day), Is.True, "The day 1 February is expected to be excluded but it is not");
    }

    /// <summary>
    /// Part 2 of the tests of QUARTZ-679
    /// </summary>
    [Test]
    public void TestRemoveInTheFuture()
    {
        AnnualCalendar annualCalendar = new AnnualCalendar();

        DateTime day = new DateTime(2005, 6, 23);
        annualCalendar.SetDayExcluded(day, true);

        // Trying to remove the 23th of June
        day = new DateTime(2008, 6, 23);
        annualCalendar.SetDayExcluded(day, false);

        Assert.That(annualCalendar.IsDayExcluded(day), Is.False, "The day 23 June is not expected to be excluded but it is");
    }

    [Test]
    public void TestAnnualCalendarTimeZone()
    {
        TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        AnnualCalendar c = new AnnualCalendar();
        c.TimeZone = tz;

        DateTime excludedDay = new DateTime(2012, 11, 4, 0, 0, 0);
        c.SetDayExcluded(excludedDay, true);

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

    [Test]
    public void BaseCalendarShouldNotAffectSettingInternalDataStructures()
    {
        var dayToExclude = new DateTime(2015, 1, 1);

        AnnualCalendar a = new AnnualCalendar();
        a.SetDayExcluded(dayToExclude, true);

        AnnualCalendar b = new AnnualCalendar(a);
        b.SetDayExcluded(dayToExclude, true);

        b.CalendarBase = null;

        Assert.That(b.IsDayExcluded(dayToExclude), "day was no longer excluded after base calendar was detached");
    }

    /// <summary>
    /// Get the object to serialize when generating serialized file for future
    /// tests, and against which to validate deserialized object.
    /// </summary>
    /// <returns></returns>
    protected override AnnualCalendar GetTargetObject()
    {
        AnnualCalendar c = new AnnualCalendar();
        c.Description = "description";
        DateTime date = new DateTime(2005, 1, 20, 10, 5, 15);
        c.SetDayExcluded(date, true);
        return c;
    }

    /// <inheritdoc />
    protected override void VerifyMatch(AnnualCalendar original, AnnualCalendar deserialized)
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