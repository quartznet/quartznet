
using NUnit.Framework;

using Quartz.Impl.Recurrence;
using Quartz.Util;

namespace Quartz.Tests.Unit.Impl.Recurrence;

public class RecurrenceRuleTest
{
    #region Parser Tests

    [Test]
    public void TestParseSimpleDaily()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");
        Assert.AreEqual(RecurrenceFrequency.Daily, rule.Frequency);
        Assert.AreEqual(1, rule.Interval);
        Assert.IsNull(rule.Count);
        Assert.IsNull(rule.Until);
    }

    [Test]
    public void TestParseWithInterval()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2");
        Assert.AreEqual(RecurrenceFrequency.Weekly, rule.Frequency);
        Assert.AreEqual(2, rule.Interval);
    }

    [Test]
    public void TestParseWithCount()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;COUNT=10");
        Assert.AreEqual(RecurrenceFrequency.Monthly, rule.Frequency);
        Assert.AreEqual(10, rule.Count);
    }

    [Test]
    public void TestParseWithUntilUtc()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20251231T235959Z");
        Assert.IsNotNull(rule.Until);
        Assert.AreEqual(new DateTime(2025, 12, 31, 23, 59, 59), rule.Until);
        Assert.IsTrue(rule.UntilIsUtc);
    }

    [Test]
    public void TestParseWithUntilLocal()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20251231T235959");
        Assert.IsNotNull(rule.Until);
        Assert.IsFalse(rule.UntilIsUtc);
    }

    [Test]
    public void TestParseWithByDay()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,WE,FR");
        Assert.IsNotNull(rule.ByDay);
        Assert.AreEqual(3, rule.ByDay!.Length);
        Assert.AreEqual(DayOfWeek.Monday, rule.ByDay[0].Day);
        Assert.AreEqual(0, rule.ByDay[0].Ordinal);
        Assert.AreEqual(DayOfWeek.Wednesday, rule.ByDay[1].Day);
        Assert.AreEqual(DayOfWeek.Friday, rule.ByDay[2].Day);
    }

    [Test]
    public void TestParseWithByDayOrdinals()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=2MO");
        Assert.IsNotNull(rule.ByDay);
        Assert.AreEqual(1, rule.ByDay!.Length);
        Assert.AreEqual(DayOfWeek.Monday, rule.ByDay[0].Day);
        Assert.AreEqual(2, rule.ByDay[0].Ordinal);
    }

    [Test]
    public void TestParseWithNegativeByDayOrdinals()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR");
        Assert.IsNotNull(rule.ByDay);
        Assert.AreEqual(DayOfWeek.Friday, rule.ByDay![0].Day);
        Assert.AreEqual(-1, rule.ByDay[0].Ordinal);
    }

    [Test]
    public void TestParseWithByMonthDay()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=15");
        Assert.IsNotNull(rule.ByMonthDay);
        Assert.AreEqual(1, rule.ByMonthDay!.Length);
        Assert.AreEqual(15, rule.ByMonthDay[0]);
    }

    [Test]
    public void TestParseWithByMonth()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=1,6,12");
        Assert.IsNotNull(rule.ByMonth);
        Assert.AreEqual(3, rule.ByMonth!.Length);
    }

    [Test]
    public void TestParseWithByHourMinuteSecond()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,17;BYMINUTE=0,30;BYSECOND=0");
        Assert.AreEqual(new[] { 9, 17 }, rule.ByHour);
        Assert.AreEqual(new[] { 0, 30 }, rule.ByMinute);
        Assert.AreEqual(new[] { 0 }, rule.BySecond);
    }

    [Test]
    public void TestParseWithBySetPos()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1");
        Assert.IsNotNull(rule.BySetPos);
        Assert.AreEqual(new[] { -1 }, rule.BySetPos);
    }

    [Test]
    public void TestParseWithWkst()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;WKST=SU");
        Assert.AreEqual(DayOfWeek.Sunday, rule.WeekStart);
    }

    [Test]
    public void TestParseStripsRrulePrefix()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("RRULE:FREQ=DAILY");
        Assert.AreEqual(RecurrenceFrequency.Daily, rule.Frequency);
    }

    [Test]
    public void TestParseRejectsEmptyString()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse(""));
    }

    [Test]
    public void TestParseRejectsMissingFreq()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("INTERVAL=2"));
    }

    [Test]
    public void TestParseRejectsCountAndUntil()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;COUNT=5;UNTIL=20251231T235959Z"));
    }

    [Test]
    public void TestParseAllFrequencies()
    {
        Assert.AreEqual(RecurrenceFrequency.Secondly, RecurrenceRule.Parse("FREQ=SECONDLY").Frequency);
        Assert.AreEqual(RecurrenceFrequency.Minutely, RecurrenceRule.Parse("FREQ=MINUTELY").Frequency);
        Assert.AreEqual(RecurrenceFrequency.Hourly, RecurrenceRule.Parse("FREQ=HOURLY").Frequency);
        Assert.AreEqual(RecurrenceFrequency.Daily, RecurrenceRule.Parse("FREQ=DAILY").Frequency);
        Assert.AreEqual(RecurrenceFrequency.Weekly, RecurrenceRule.Parse("FREQ=WEEKLY").Frequency);
        Assert.AreEqual(RecurrenceFrequency.Monthly, RecurrenceRule.Parse("FREQ=MONTHLY").Frequency);
        Assert.AreEqual(RecurrenceFrequency.Yearly, RecurrenceRule.Parse("FREQ=YEARLY").Frequency);
    }

    #endregion

    #region ToString Round-Trip Tests

    [Test]
    public void TestToStringRoundTrip()
    {
        string rrule = "FREQ=WEEKLY;INTERVAL=2;BYDAY=MO,WE,FR";
        RecurrenceRule rule = RecurrenceRule.Parse(rrule);
        Assert.AreEqual(rrule, rule.ToString());
    }

    [Test]
    public void TestToStringRoundTripComplex()
    {
        string rrule = "FREQ=YEARLY;COUNT=5;BYMONTH=3;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1";
        RecurrenceRule rule = RecurrenceRule.Parse(rrule);
        Assert.AreEqual(rrule, rule.ToString());
    }

    [Test]
    public void TestToStringOmitsDefaultInterval()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=1");
        Assert.AreEqual("FREQ=DAILY", rule.ToString());
    }

    [Test]
    public void TestToStringOmitsDefaultWkst()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;WKST=MO");
        Assert.AreEqual("FREQ=WEEKLY", rule.ToString());
    }

    #endregion

    #region GetNextOccurrence Tests

    [Test]
    public void TestDailySimple()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 2, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Test]
    public void TestDailyWithInterval()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;INTERVAL=3");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 4, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Test]
    public void TestWeeklyOnSpecificDays()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=MO,WE,FR");
        // Start on Wednesday Jan 1 2025
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        // Next after Wed Jan 1 should be Fri Jan 3
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(DayOfWeek.Friday, next!.Value.DayOfWeek);
        Assert.AreEqual(3, next.Value.Day);
    }

    [Test]
    public void TestWeeklyEveryOtherWeek()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;INTERVAL=2;BYDAY=MO");
        // Monday Jan 6 2025
        DateTimeOffset start = new DateTimeOffset(2025, 1, 6, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        // Next should be 2 weeks later: Mon Jan 20
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 20, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Test]
    public void TestMonthlySecondMonday()
    {
        // "Every 2nd Monday of the month"
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=2MO");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        // 2nd Monday of Jan 2025 is Jan 13
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(13, next!.Value.Day);
        Assert.AreEqual(DayOfWeek.Monday, next.Value.DayOfWeek);
    }

    [Test]
    public void TestMonthlyLastFriday()
    {
        // "Last Friday of the month"
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-1FR");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        // Last Friday of Jan 2025 is Jan 31
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(31, next!.Value.Day);
        Assert.AreEqual(DayOfWeek.Friday, next.Value.DayOfWeek);
    }

    [Test]
    public void TestMonthlyByMonthDay()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=15");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(15, next!.Value.Day);
        Assert.AreEqual(1, next.Value.Month);
    }

    [Test]
    public void TestMonthlyLastDayOfMonth()
    {
        // BYMONTHDAY=-1 means last day of month
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-1");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        // Last day of Jan = 31
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(31, next!.Value.Day);

        // Next after Jan 31 should be Feb 28
        DateTimeOffset? next2 = rule.GetNextOccurrence(start, next.Value, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next2);
        Assert.AreEqual(28, next2!.Value.Day);
        Assert.AreEqual(2, next2.Value.Month);
    }

    [Test]
    public void TestYearlyLastWeekdayOfMarch()
    {
        // "Last weekday of March each year"
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=3;BYDAY=MO,TU,WE,TH,FR;BYSETPOS=-1");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        // Last weekday of March 2025: March 31 is a Monday
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(3, next!.Value.Month);
        Assert.AreEqual(31, next.Value.Day);
        Assert.AreEqual(DayOfWeek.Monday, next.Value.DayOfWeek);
    }

    [Test]
    public void TestYearlySpecificDate()
    {
        // Every year on March 15
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=3;BYMONTHDAY=15");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(new DateTimeOffset(2025, 3, 15, 9, 0, 0, TimeSpan.Zero), next);
    }

    [Test]
    public void TestCountStopsAfterN()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        // 1st: Jan 1, 2nd: Jan 2, 3rd: Jan 3
        DateTimeOffset? r1 = rule.GetNextOccurrence(start, start.AddSeconds(-1), TimeZoneInfo.Utc, null);
        Assert.IsNotNull(r1);
        Assert.AreEqual(1, r1!.Value.Day);

        DateTimeOffset? r2 = rule.GetNextOccurrence(start, r1.Value, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(r2);
        Assert.AreEqual(2, r2!.Value.Day);

        DateTimeOffset? r3 = rule.GetNextOccurrence(start, r2.Value, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(r3);
        Assert.AreEqual(3, r3!.Value.Day);

        // 4th should be null (COUNT=3 exhausted)
        DateTimeOffset? r4 = rule.GetNextOccurrence(start, r3.Value, TimeZoneInfo.Utc, null);
        Assert.IsNull(r4);
    }

    [Test]
    public void TestUntilStopsAtCutoff()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;UNTIL=20250105T235959Z");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset? r1 = rule.GetNextOccurrence(start, start, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(r1);

        // Keep going until we hit null
        DateTimeOffset? current = r1;
        int count = 1;
        while (current != null && count < 100)
        {
            DateTimeOffset? next = rule.GetNextOccurrence(start, current.Value, TimeZoneInfo.Utc, null);
            if (next == null)
            {
                break;
            }
            count++;
            Assert.IsTrue(next.Value <= new DateTimeOffset(2025, 1, 5, 23, 59, 59, TimeSpan.Zero));
            current = next;
        }

        // Should have 4 days (Jan 2, 3, 4, 5)
        Assert.AreEqual(4, count);
    }

    [Test]
    public void TestEndTimeRespected()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset endTime = new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero);

        DateTimeOffset? r1 = rule.GetNextOccurrence(start, start, TimeZoneInfo.Utc, endTime);
        Assert.IsNotNull(r1);
        Assert.AreEqual(2, r1!.Value.Day);

        DateTimeOffset? r2 = rule.GetNextOccurrence(start, r1.Value, TimeZoneInfo.Utc, endTime);
        Assert.IsNotNull(r2);
        Assert.AreEqual(3, r2!.Value.Day);

        // Next would be Jan 4 which is past endTime
        DateTimeOffset? r3 = rule.GetNextOccurrence(start, r2.Value, TimeZoneInfo.Utc, endTime);
        Assert.IsNull(r3);
    }

    [Test]
    public void TestHourlyFrequency()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=HOURLY;INTERVAL=2");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 8, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(new DateTimeOffset(2025, 1, 1, 10, 0, 0, TimeSpan.Zero), next);
    }

    [Test]
    public void TestDailyWithByHour()
    {
        // Fire daily at 9am and 5pm
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=9,17;BYMINUTE=0;BYSECOND=0");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(9, next!.Value.Hour);

        DateTimeOffset? next2 = rule.GetNextOccurrence(start, next.Value, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next2);
        Assert.AreEqual(17, next2!.Value.Hour);
    }

    [Test]
    public void TestDailyFilteredByDay()
    {
        // Daily but only on weekdays
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;BYDAY=MO,TU,WE,TH,FR");
        // Jan 3, 2025 is a Friday
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset afterFriday = new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero);

        // Next after Friday should skip weekend to Monday Jan 6
        DateTimeOffset? next = rule.GetNextOccurrence(start, afterFriday, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(6, next!.Value.Day);
        Assert.AreEqual(DayOfWeek.Monday, next.Value.DayOfWeek);
    }

    [Test]
    public void TestMonthlyMultipleByMonthDay()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=1,15");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(15, next!.Value.Day);

        DateTimeOffset? next2 = rule.GetNextOccurrence(start, next.Value, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next2);
        Assert.AreEqual(1, next2!.Value.Day);
        Assert.AreEqual(2, next2.Value.Month);
    }

    [Test]
    public void TestMonthlyByMonthDay31SkipsFebruary()
    {
        // BYMONTHDAY=31 should skip months with fewer than 31 days
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=31");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        // Jan 31
        DateTimeOffset? r1 = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(r1);
        Assert.AreEqual(31, r1!.Value.Day);
        Assert.AreEqual(1, r1.Value.Month);

        // Feb has no 31st, skip to Mar 31
        DateTimeOffset? r2 = rule.GetNextOccurrence(start, r1.Value, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(r2);
        Assert.AreEqual(31, r2!.Value.Day);
        Assert.AreEqual(3, r2.Value.Month);
    }

    [Test]
    public void TestDstSpringForwardGap()
    {
        // US Eastern: March 9, 2025, 2:00 AM doesn't exist (clocks jump to 3:00 AM)
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");
        TimeZoneInfo eastern = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        // Start at 2:30 AM local
        DateTimeOffset start = new DateTimeOffset(2025, 3, 8, 7, 30, 0, TimeSpan.Zero); // 2:30 AM EST = 7:30 UTC
        DateTimeOffset after = start;

        // Next occurrence should be March 9 - time should be adjusted past the gap
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, eastern, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(9, next!.Value.Day);
        // The time should be valid (not 2:30 AM which doesn't exist)
        Assert.IsFalse(eastern.IsInvalidTime(next.Value.DateTime));
    }

    [Test]
    public void TestDstFallBackAmbiguous()
    {
        // US Eastern: Nov 2, 2025, 1:30 AM exists twice (clocks fall back at 2:00 AM)
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY");
        TimeZoneInfo eastern = TimeZoneUtil.FindTimeZoneById("Eastern Standard Time");
        // Start at 1:30 AM local on Nov 1
        DateTimeOffset start = new DateTimeOffset(2025, 11, 1, 5, 30, 0, TimeSpan.Zero); // 1:30 AM EDT = 5:30 UTC
        DateTimeOffset after = start;

        // Nov 2 at 1:30 AM is ambiguous - should resolve to a valid offset
        DateTimeOffset? next = rule.GetNextOccurrence(start, after, eastern, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(2, next!.Value.Day);
    }

    [Test]
    public void TestCountWithSkipCountParameter()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=3");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        // With skipCount=true, COUNT should be ignored
        DateTimeOffset? r1 = rule.GetNextOccurrence(start, new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc, null, skipCount: true);
        Assert.IsNotNull(r1);
        Assert.AreEqual(4, r1!.Value.Day); // Would be null with COUNT enforced

        // With skipCount=false (default), COUNT is enforced
        DateTimeOffset? r2 = rule.GetNextOccurrence(start, new DateTimeOffset(2025, 1, 3, 9, 0, 0, TimeSpan.Zero), TimeZoneInfo.Utc, null, skipCount: false);
        Assert.IsNull(r2); // 3 occurrences already consumed
    }

    [Test]
    public void TestGetNthOccurrence()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;COUNT=5");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);

        // 5th occurrence should be Jan 5
        DateTimeOffset? fifth = rule.GetNthOccurrence(start, 5, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(fifth);
        Assert.AreEqual(5, fifth!.Value.Day);

        // 6th occurrence doesn't exist (COUNT=5)
        DateTimeOffset? sixth = rule.GetNthOccurrence(start, 6, TimeZoneInfo.Utc, null);
        Assert.IsNull(sixth);
    }

    [Test]
    public void TestParseIgnoresUnknownProperties()
    {
        // RFC 5545 allows X- extension properties
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=DAILY;X-CUSTOM=foo;INTERVAL=2");
        Assert.AreEqual(RecurrenceFrequency.Daily, rule.Frequency);
        Assert.AreEqual(2, rule.Interval);
    }

    [Test]
    public void TestParseRejectsOutOfRangeByMonth()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=0"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=13"));
    }

    [Test]
    public void TestParseRejectsOutOfRangeByHour()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=-1"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=DAILY;BYHOUR=24"));
    }

    [Test]
    public void TestParseRejectsOutOfRangeByMonthDay()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=0"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=32"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=MONTHLY;BYMONTHDAY=-32"));
    }

    [Test]
    public void TestParseAcceptsValidByRanges()
    {
        // Boundary values should be accepted
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=YEARLY;BYMONTH=1,12;BYMONTHDAY=-31,31;BYHOUR=0,23;BYMINUTE=0,59;BYSECOND=0,59");
        Assert.AreEqual(new[] { 1, 12 }, rule.ByMonth);
        Assert.AreEqual(new[] { -31, 31 }, rule.ByMonthDay);
        Assert.AreEqual(new[] { 0, 23 }, rule.ByHour);
    }

    [Test]
    public void TestHourlyWithByMonthFastForward()
    {
        // FREQ=HOURLY;BYMONTH=12 starting in January should still find December
        // This tests the fast-forward optimization for sub-daily frequencies with BYMONTH
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=HOURLY;BYMONTH=12");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(12, next!.Value.Month);
        Assert.AreEqual(2025, next.Value.Year);
    }

    [Test]
    public void TestHourlyWithByMonthFastForwardNonMidnightStart()
    {
        // Regression: fast-forward must land at the earliest period in the matching
        // month, not skip to dtStart's hour-of-day within that month.
        // With FREQ=HOURLY and dtStart at 05:30, periods are at :30 past each hour.
        // The first Feb occurrence should be Feb 1 at 00:30 (earliest hourly period).
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=HOURLY;BYMONTH=2");
        DateTimeOffset start = new DateTimeOffset(2025, 1, 15, 5, 30, 0, TimeSpan.Zero);
        DateTimeOffset after = start;

        DateTimeOffset? next = rule.GetNextOccurrence(start, after, TimeZoneInfo.Utc, null);
        Assert.IsNotNull(next);
        Assert.AreEqual(2, next!.Value.Month);
        Assert.AreEqual(1, next.Value.Day);
        // First hourly period in Feb should be in the early hours, not at hour 5
        Assert.IsTrue(next.Value.Hour < 5, $"Expected early hour in Feb, got {next.Value.Hour}");
    }

    [Test]
    public void TestParseRejectsOutOfRangeByDayOrdinal()
    {
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=0MO"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=54MO"));
        Assert.Throws<FormatException>(() => RecurrenceRule.Parse("FREQ=MONTHLY;BYDAY=-54FR"));
    }

    [Test]
    public void TestParseAcceptsValidByDayOrdinalBoundary()
    {
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=YEARLY;BYDAY=53MO,-53FR");
        Assert.AreEqual(53, rule.ByDay![0].Ordinal);
        Assert.AreEqual(-53, rule.ByDay[1].Ordinal);
    }

    [Test]
    public void TestWeeklyPeriodIndexAlignedWithPeriodStart()
    {
        // Regression: GetPeriodIndex WEEKLY branch must align to WeekStart
        // just like GetPeriodStart does. DTSTART on Wednesday, end boundary
        // on the following Monday (WeekStart). GetLastOccurrenceBefore must
        // find the occurrence in that week.
        RecurrenceRule rule = RecurrenceRule.Parse("FREQ=WEEKLY;BYDAY=WE,FR");
        // Wed Jan 1 2025
        DateTimeOffset start = new DateTimeOffset(2025, 1, 1, 9, 0, 0, TimeSpan.Zero);
        // End on Monday Jan 6 (next WeekStart boundary)
        DateTimeOffset endTime = new DateTimeOffset(2025, 1, 6, 0, 0, 0, TimeSpan.Zero);

        // Last fire before Jan 6 should be Friday Jan 3
        DateTimeOffset? last = rule.GetLastOccurrenceBefore(start, TimeZoneInfo.Utc, endTime);
        Assert.IsNotNull(last);
        Assert.AreEqual(3, last!.Value.Day);
        Assert.AreEqual(DayOfWeek.Friday, last.Value.DayOfWeek);
    }

    #endregion
}
