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
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
[TestFixture(typeof(SystemTextJsonObjectSerializer))]
[NonParallelizable]
public class AnnualCalendarTest : SerializationTestSupport<AnnualCalendar, ICalendar>
{
    private AnnualCalendar calendar;

    public AnnualCalendarTest(Type serializerType) : base(serializerType)
    {
    }

    [SetUp]
    public void Setup()
    {
        calendar = new AnnualCalendar();
    }

    [Test]
    public void TestDayExclusion()
    {
        // we're local by default
        DateTime d = new DateTime(2005, 1, 1);
        calendar.AddExcludedDay(new MonthDay(1, 1));

        calendar.IsTimeIncluded(d.ToUniversalTime()).Should().BeFalse("the excluded day's time must not be included");
        calendar.IsDayExcluded(new MonthDay(1, 1)).Should().BeTrue();
        calendar.DaysExcluded.Should().ContainSingle().Which.Should().Be(new MonthDay(1, 1));
    }

    [Test]
    public void TestDayInclusionAfterExclusion()
    {
        MonthDay d = new MonthDay(1, 1);
        calendar.AddExcludedDay(d).Should().BeTrue();
        calendar.RemoveExcludedDay(d).Should().BeTrue();
        calendar.RemoveExcludedDay(d).Should().BeFalse("the day was already included again");

        calendar.IsTimeIncluded(new DateTime(2005, 1, 1)).Should().BeTrue();
        calendar.IsDayExcluded(d).Should().BeFalse();
    }

    [Test]
    public void TestDayExclusionDifferentYears()
    {
        const string ErrMessage = "a MonthDay carries no year, so the same date is excluded every year";
        calendar.AddExcludedDay(MonthDay.From(new DateOnly(2005, 1, 1)));

        calendar.IsDayExcluded(MonthDay.From(new DateOnly(2003, 1, 1))).Should().BeTrue(ErrMessage);
        calendar.IsDayExcluded(MonthDay.From(new DateOnly(2007, 1, 1))).Should().BeTrue(ErrMessage);
        calendar.IsDayExcluded(MonthDay.From(new DateOnly(2105, 1, 1))).Should().BeTrue(ErrMessage);
    }

    [Test]
    public void TestExclusionAndNextIncludedTime()
    {
        calendar.DaysExcluded.Should().BeEmpty();
        DateTimeOffset test = DateTimeOffset.UtcNow.Date;
        Assert.That(calendar.GetNextIncludedTimeUtc(test), Is.EqualTo(test), "Did not get today as date when nothing was excluded");

        calendar.AddExcludedDay(MonthDay.From(DateOnly.FromDateTime(test.Date)));
        Assert.That(calendar.GetNextIncludedTimeUtc(test), Is.EqualTo(test.AddDays(1)), "Did not get next day when current day excluded");
    }

    /// <summary>
    /// QUARTZ-679 Test if the annualCalendar works over years.
    /// </summary>
    [Test]
    public void TestDaysExcludedOverTime()
    {
        AnnualCalendar annualCalendar = new AnnualCalendar();

        annualCalendar.AddExcludedDay(new MonthDay(6, 23));
        annualCalendar.AddExcludedDay(new MonthDay(2, 1));

        annualCalendar.IsDayExcluded(new MonthDay(2, 1)).Should().BeTrue("the day 1 February is expected to be excluded");
    }

    /// <summary>
    /// Part 2 of the tests of QUARTZ-679
    /// </summary>
    [Test]
    public void TestRemoveInTheFuture()
    {
        AnnualCalendar annualCalendar = new AnnualCalendar();

        // days excluded via dates in different years name the same day of every year
        annualCalendar.AddExcludedDay(MonthDay.From(new DateOnly(2005, 6, 23)));

        annualCalendar.RemoveExcludedDay(MonthDay.From(new DateOnly(2008, 6, 23))).Should().BeTrue("a MonthDay carries no year");

        annualCalendar.IsDayExcluded(new MonthDay(6, 23)).Should().BeFalse("the day 23 June is not expected to be excluded");
    }

    [Test]
    public void TestAnnualCalendarTimeZone()
    {
        TimeZoneInfo tz = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        AnnualCalendar c = new AnnualCalendar();
        c.TimeZone = tz;

        c.AddExcludedDay(new MonthDay(11, 4));

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
        var dayToExclude = new MonthDay(1, 1);

        AnnualCalendar a = new AnnualCalendar();
        a.AddExcludedDay(dayToExclude);

        AnnualCalendar b = new AnnualCalendar(a);
        b.AddExcludedDay(dayToExclude);

        b.CalendarBase = null;

        b.IsDayExcluded(dayToExclude).Should().BeTrue("the day must stay excluded after the base calendar was detached");
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
        c.AddExcludedDay(new MonthDay(1, 20));
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