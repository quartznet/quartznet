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

using System.Diagnostics;

using Quartz.Impl.Calendar;
using Quartz.Impl;

namespace Quartz.Tests.Unit.Impl.Calendar;

/// <author>Marko Lahma (.NET)</author>
[TestFixture(typeof(NewtonsoftJsonObjectSerializer))]
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
        Assert.That(calendar.IsTimeIncluded(tst), Is.False, fault);

        calendar.CronExpression = new CronExpression("0/25 * * * * ?");
        fault = "Time was not included as expected";
        Assert.That(calendar.IsTimeIncluded(tst), Is.True, fault);
    }

    [Test]
    public void TestClone()
    {
        CronCalendar calendar = new CronCalendar("0/15 * * * * ?");
        CronCalendar clone = (CronCalendar)calendar.Clone();
        Assert.That(clone.CronExpression, Is.EqualTo(calendar.CronExpression));
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
    /// that expression — so the argument was silently dropped and the calendar excluded local hours
    /// (#3321). Only the property setter rebuilt the expression correctly.
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
        utc.GetNextIncludedTimeUtc(nineThirtyEastern).Should().Be(nineThirtyEastern.AddMilliseconds(1),
            "14:30Z is outside the excluded hour in UTC, so the very next instant is included");
        calendar.GetNextIncludedTimeUtc(fourThirtyEastern).Should().Be(fourThirtyEastern.AddMilliseconds(1),
            "and the New York calendar says the same of the instant it includes");
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
        CronCalendar calendar = new CronCalendar(null, "* * 9 ? * *", TimeZoneInfo.Utc);
        DateTimeOffset insideTheExcludedHour = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero);

        // Through a task with a deadline so a regression fails the test instead of hanging the run.
        Task<DateTimeOffset> search = Task.Run(() => calendar.GetNextIncludedTimeUtc(insideTheExcludedHour));
        Task finished = await Task.WhenAny(search, Task.Delay(TimeSpan.FromSeconds(10)));

        finished.Should().BeSameAs(search,
            "the search must step to the end of the excluded range, not crawl it millisecond by millisecond");
        (await search).Should().Be(new DateTimeOffset(2026, 1, 1, 10, 0, 0, TimeSpan.Zero),
            "the first included instant after 09:30 is the top of the next hour, where the expression stops matching");
    }

    /// <summary>
    /// The other half of #3690: an expression that excludes every instant has no next included time to
    /// answer with. The search used to look for one a second at a time until <c>GetTimeAfter</c> gave up
    /// a century out, and then returned an instant the calendar excludes — a wrong answer after three
    /// billion cron computations. It is a configuration mistake, so it is said out loud.
    /// </summary>
    [Test]
    public void ACalendarThatExcludesEveryInstantSaysSoRatherThanWalkingACentury()
    {
        CronCalendar calendar = new CronCalendar(null, "* * * * * ?", TimeZoneInfo.Utc);
        DateTimeOffset from = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);

        calendar.IsTimeIncluded(from).Should().BeFalse("the expression matches every instant, and this calendar excludes what it matches");

        Stopwatch stopwatch = Stopwatch.StartNew();
        Action act = () => calendar.GetNextIncludedTimeUtc(from);
        SchedulerException thrown = act.Should().Throw<SchedulerException>(
            "there is no included time to return, and no answer is better than an excluded one").Which;
        stopwatch.Stop();

        thrown.Message.Should().Contain("excludes every instant", "the message has to say what is wrong with the calendar");
        thrown.Message.Should().Contain("* * * * * ?", "and name the expression, which is the thing to change");
        stopwatch.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(1),
            "the answer is read off the expression's fields; walking to the give-up year for it took minutes");
    }

    /// <summary>
    /// The two members have to agree: everything the next-included search steps over is a time
    /// <see cref="CronCalendar.IsTimeIncluded" /> excludes, and the instant it lands on is one it
    /// includes. A search that reads its answer off the expression's fields rather than walking to it is
    /// only right if that holds.
    /// </summary>
    [TestCase("* * 9 ? * *", "2026-01-01T09:30:00Z", "the whole of the excluded hour is stepped over at once")]
    [TestCase("* * 1-3 ? * *", "2026-01-01T02:00:00Z", "and the whole of a three hour range")]
    [TestCase("0/15 * * * * ?", "2026-01-01T09:30:00Z", "an excluded run one second long ends at the next second")]
    [TestCase("0 0 12 * * ?", "2026-01-01T12:00:00Z", "as does a single daily fire")]
    [TestCase("* * * ? * MON", "2026-01-05T13:00:00Z", "a whole excluded Monday ends at midnight")]
    public void EveryInstantTheNextIncludedSearchStepsOverIsOneTheCalendarExcludes(string expression, string from, string reason)
    {
        CronCalendar calendar = new CronCalendar(null, expression, TimeZoneInfo.Utc);
        DateTimeOffset start = DateTimeOffset.Parse(from);

        DateTimeOffset included = calendar.GetNextIncludedTimeUtc(start);

        included.Should().BeAfter(start, reason);
        calendar.IsTimeIncluded(included).Should().BeTrue($"{reason}: the instant the search lands on is included");

        // Sample the stepped-over range rather than walk it: an excluded day is 86,400 instants, and
        // each IsTimeIncluded is a cron computation.
        TimeSpan range = included - start;
        TimeSpan step = range.TotalSeconds > 2000 ? TimeSpan.FromTicks(range.Ticks / 2000) : TimeSpan.FromSeconds(1);

        for (DateTimeOffset excluded = start.AddMilliseconds(1); excluded < included; excluded += step)
        {
            calendar.IsTimeIncluded(excluded).Should().BeFalse(
                $"{reason}: {excluded:O} lies before the first included instant {included:O}, so it must be excluded");
        }

        calendar.IsTimeIncluded(included.AddMilliseconds(-1)).Should().BeFalse(
            $"{reason}: the millisecond before the first included instant is still excluded");
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
        Assert.Multiple(() =>
        {
            Assert.That(deserialized, Is.Not.Null);
            Assert.That(deserialized.Description, Is.EqualTo(original.Description));
            Assert.That(deserialized.CronExpression, Is.EqualTo(original.CronExpression));
            Assert.That(deserialized.TimeZone, Is.EqualTo(original.TimeZone));
        });
    }
}