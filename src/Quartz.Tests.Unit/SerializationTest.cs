using System.Runtime.Serialization.Formatters.Binary;

using Quartz.Impl.Calendar;
using Quartz.Impl.Matchers;
using Quartz.Tests.Unit.Utils;
using Quartz.Util;

namespace Quartz.Tests.Unit;

[TestFixture]
public class SerializationTest
{
    [Test]
    public void TestAnnualCalendarDeserialization()
    {
        Deserialize<AnnualCalendar>();
    }

    [Test]
    public void TestAnnualCalendarSerialization()
    {
        AnnualCalendar annualCalendar = new AnnualCalendar();
        DateTime day = new DateTime(2011, 12, 20, 0, 0, 0);
        annualCalendar.SetDayExcluded(day, true);
        AnnualCalendar clone = annualCalendar.DeepClone();
        Assert.That(clone.IsDayExcluded(day), Is.True);
    }

    [Test]
    public void TestBaseCalendarDeserialization()
    {
        Deserialize<BaseCalendar>();
    }

    [Test]
    public void TestBaseCalendarSerialization()
    {
        BaseCalendar baseCalendar = new BaseCalendar();
        TimeZoneInfo timeZone = TimeZoneInfo.GetSystemTimeZones()[3];
        baseCalendar.TimeZone = timeZone;
        BaseCalendar clone = baseCalendar.DeepClone();
        Assert.That(clone.TimeZone.Id, Is.EqualTo(timeZone.Id));
    }

    [Test]
    public void TestCronCalendarDeserialization()
    {
        Deserialize<CronCalendar>();
    }

    [Test]
    public void TestCronCalendarSerialization()
    {
        CronCalendar cronCalendar = new CronCalendar("* * 8-17 ? * *");
        CronCalendar clone = cronCalendar.DeepClone();
        Assert.That(clone.CronExpression.CronExpressionString, Is.EqualTo("* * 8-17 ? * *"));
    }

    [Test]
    public void TestDailyCalendarDeserialization()
    {
        Deserialize<DailyCalendar>();
    }

    [Test]
    public void TestDailyCalendarSerialization()
    {
        DailyCalendar dailyCalendar = new DailyCalendar("12:13:14:150", "13:14");
        DailyCalendar clone = dailyCalendar.DeepClone();

        DateTimeOffset timeRangeStartTimeUtc = clone.GetTimeRangeStartingTimeUtc(DateTimeOffset.UtcNow);
        Assert.Multiple(() =>
        {
            Assert.That(timeRangeStartTimeUtc.Hour, Is.EqualTo(12));
            Assert.That(timeRangeStartTimeUtc.Minute, Is.EqualTo(13));
            Assert.That(timeRangeStartTimeUtc.Second, Is.EqualTo(14));
            Assert.That(timeRangeStartTimeUtc.Millisecond, Is.EqualTo(150));
        });

        DateTimeOffset timeRangeEndingTimeUtc = clone.GetTimeRangeEndingTimeUtc(DateTimeOffset.UtcNow);

        Assert.Multiple(() =>
        {
            Assert.That(timeRangeEndingTimeUtc.Hour, Is.EqualTo(13));
            Assert.That(timeRangeEndingTimeUtc.Minute, Is.EqualTo(14));
            Assert.That(timeRangeEndingTimeUtc.Second, Is.EqualTo(0));
            Assert.That(timeRangeEndingTimeUtc.Millisecond, Is.EqualTo(0));
        });
    }

    [Test]
    [Ignore("requires binary serilization to be done with 2.4, 2.3 in test is non-compliant")]
    public void TestHolidayCalendarDeserialization()
    {
        var calendar = Deserialize<HolidayCalendar>();
        Assert.That(calendar.ExcludedDates, Has.Count.EqualTo(1));

        calendar = Deserialize<HolidayCalendar>(23);
        Assert.That(calendar.ExcludedDates, Has.Count.EqualTo(1));

        BinaryFormatter formatter = new BinaryFormatter();
        using (var stream = new MemoryStream())
        {
            calendar = new HolidayCalendar();
            calendar.AddExcludedDate(DateTime.Now.Date);
            formatter.Serialize(stream, calendar);

            stream.Seek(0, SeekOrigin.Begin);
            stream.Position = 0;

            calendar = (HolidayCalendar) formatter.Deserialize(stream);
            Assert.That(calendar.ExcludedDates, Has.Count.EqualTo(1));
        }
    }

    [Test]
    public void TestHolidayCalendarSerialization()
    {
        HolidayCalendar holidayCalendar = new HolidayCalendar();
        holidayCalendar.AddExcludedDate(new DateTime(2010, 1, 20));
        HolidayCalendar clone = holidayCalendar.DeepClone();
        Assert.That(clone.ExcludedDates, Has.Count.EqualTo(1));
    }

    [Test]
    public void TestMonthlyCalendarDeserialization()
    {
        Deserialize<MonthlyCalendar>();
    }

    [Test]
    public void TestMonthlyCalendarSerialization()
    {
        MonthlyCalendar monthlyCalendar = new MonthlyCalendar();
        monthlyCalendar.SetDayExcluded(20, true);
        MonthlyCalendar clone = monthlyCalendar.DeepClone();
        Assert.That(clone.IsDayExcluded(20), Is.True);
    }

    [Test]
    public void TestWeeklyCalendarDeserialization()
    {
        Deserialize<WeeklyCalendar>();
    }

    [Test]
    public void TestWeeklyCalendarSerialization()
    {
        WeeklyCalendar weeklyCalendar = new WeeklyCalendar();
        weeklyCalendar.SetDayExcluded(DayOfWeek.Monday, true);
        WeeklyCalendar clone = weeklyCalendar.DeepClone();
        Assert.That(clone.IsDayExcluded(DayOfWeek.Monday), Is.True);
    }

    /* TODO
    [Test]
    public void TestTreeSetDeserialization()
    {
        Deserialize<TreeSet>();
    }

    [Test]
    public void TestTreeSetSerialization()
    {
        new TreeSet<string>().DeepClone();
    }
    */

    [Test]
    public void TestHashSetSerialization()
    {
        new HashSet<string>().DeepClone();
    }

    [Test]
    public void TestJobDataMapDeserialization()
    {
        JobDataMap map = Deserialize<JobDataMap>();
        Assert.Multiple(() =>
        {
            Assert.That(map["foo"], Is.EqualTo("bar"));
            Assert.That(map["num"], Is.EqualTo(123));
        });
    }

    [Test]
    public void TestJobDataMapSerialization()
    {
        JobDataMap map = new JobDataMap
        {
            ["foo"] = "bar",
            ["num"] = 123
        };
        JobDataMap clone = map.DeepClone();
        Assert.Multiple(() =>
        {
            Assert.That(clone["foo"], Is.EqualTo("bar"));
            Assert.That(clone["num"], Is.EqualTo(123));
        });
    }

    [Test]
    public void TestStringKeyDirtyFlagMapSerialization()
    {
        StringKeyDirtyFlagMap map = new StringKeyDirtyFlagMap
        {
            ["foo"] = "bar",
            ["num"] = 123
        };

        StringKeyDirtyFlagMap clone = map.DeepClone();
        Assert.Multiple(() =>
        {
            Assert.That(clone["foo"], Is.EqualTo("bar"));
            Assert.That(clone["num"], Is.EqualTo(123));
        });
    }

    [Test]
    public void TestSchedulerContextSerialization()
    {
        SchedulerContext map = new SchedulerContext
        {
            ["foo"] = "bar",
            ["num"] = 123
        };

        SchedulerContext clone = map.DeepClone();
        Assert.Multiple(() =>
        {
            Assert.That(clone["foo"], Is.EqualTo("bar"));
            Assert.That(clone["num"], Is.EqualTo(123));
        });
    }

    [Test]
    public void TestGroupMatcherSerialization()
    {
        GroupMatcher<TriggerKey> expected = GroupMatcher<TriggerKey>.GroupEquals(SchedulerConstants.DefaultGroup);

        GroupMatcher<TriggerKey> got = SerializeAndDeserialize(expected);

        Assert.Multiple(() =>
        {
            Assert.That(got.CompareToValue, Is.EqualTo(expected.CompareToValue));
            Assert.That(got.CompareWithOperator, Is.EqualTo(expected.CompareWithOperator));
        });
    }

    [Test]
    public void TestNameMatcherSerialization()
    {
        NameMatcher<JobKey> expected = NameMatcher<JobKey>.NameContains("foo");

        NameMatcher<JobKey> got = SerializeAndDeserialize(expected);

        Assert.Multiple(() =>
        {
            Assert.That(got.CompareToValue, Is.EqualTo(expected.CompareToValue));
            Assert.That(got.CompareWithOperator, Is.EqualTo(expected.CompareWithOperator));
        });
    }

    private static T SerializeAndDeserialize<T>(T instance) where T : class
    {
        BinaryFormatter formatter = new();
        using MemoryStream stream = new();

        formatter.Serialize(stream, instance);
        stream.Position = 0;

        return (T)formatter.Deserialize(stream)!;
    }

    private static T Deserialize<T>() where T : class
    {
        return Deserialize<T>(10);
    }

    private static T Deserialize<T>(int version) where T : class
    {
        BinaryFormatter formatter = new BinaryFormatter();
        using var stream = File.OpenRead(Path.Combine("Serialized", typeof(T).Name + "_" + version + ".ser"));
        return (T) formatter.Deserialize(stream);
    }
}