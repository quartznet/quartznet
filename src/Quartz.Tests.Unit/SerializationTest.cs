using System;
using System.IO;
using System.Runtime.Serialization.Formatters.Binary;

using NUnit.Framework;

using Quartz.Collection;
using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Tests.Unit
{
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
            DateTimeOffset day = new DateTimeOffset(2011, 12, 20, 0, 0, 0, TimeSpan.Zero);
            annualCalendar.SetDayExcluded(day, true);
            AnnualCalendar clone = annualCalendar.DeepClone();
            Assert.IsTrue(clone.IsDayExcluded(day));
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
            Assert.AreEqual(timeZone.Id, clone.TimeZone.Id);
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
            Assert.AreEqual("* * 8-17 ? * *", clone.CronExpression.CronExpressionString);
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
            Assert.AreEqual(12, timeRangeStartTimeUtc.Hour);
            Assert.AreEqual(13, timeRangeStartTimeUtc.Minute);
            Assert.AreEqual(14, timeRangeStartTimeUtc.Second);
            Assert.AreEqual(150, timeRangeStartTimeUtc.Millisecond);

            DateTimeOffset timeRangeEndingTimeUtc = clone.GetTimeRangeEndingTimeUtc(DateTimeOffset.UtcNow);

            Assert.AreEqual(13, timeRangeEndingTimeUtc.Hour);
            Assert.AreEqual(14, timeRangeEndingTimeUtc.Minute);
            Assert.AreEqual(0, timeRangeEndingTimeUtc.Second);
            Assert.AreEqual(0, timeRangeEndingTimeUtc.Millisecond);
        }

        [Test]
        public void TestHolidayCalendarDeserialization()
        {
            var calendar = Deserialize<HolidayCalendar>();
            Assert.That(calendar.ExcludedDates.Count, Is.EqualTo(1));

            calendar = Deserialize<HolidayCalendar>(23);
            Assert.That(calendar.ExcludedDates.Count, Is.EqualTo(1));

            BinaryFormatter formatter = new BinaryFormatter();
            using (var stream = new MemoryStream())
            {
                calendar = new HolidayCalendar();
                calendar.AddExcludedDate(DateTime.Now.Date);
                formatter.Serialize(stream, calendar);

                stream.Seek(0, SeekOrigin.Begin);
                stream.Position = 0;

                calendar = (HolidayCalendar) formatter.Deserialize(stream);
                Assert.That(calendar.ExcludedDates.Count, Is.EqualTo(1));
            }
        }

        [Test]
        public void TestHolidayCalendarSerialization()
        {
            HolidayCalendar holidayCalendar = new HolidayCalendar();
            holidayCalendar.AddExcludedDate(new DateTime(2010, 1, 20));
            HolidayCalendar clone = holidayCalendar.DeepClone();
            Assert.AreEqual(1, clone.ExcludedDates.Count);
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
            Assert.IsTrue(clone.IsDayExcluded(20));
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
            Assert.IsTrue(clone.IsDayExcluded(DayOfWeek.Monday));
        }

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

        [Test]
        public void TestHashSetSerialization()
        {
            new HashSet<string>().DeepClone();
        }

        [Test]
        public void TestJobDataMapDeserialization()
        {
            JobDataMap map = Deserialize<JobDataMap>();
            Assert.AreEqual("bar", map["foo"]);
            Assert.AreEqual(123, map["num"]);
        }

        [Test]
        public void TestJobDataMapSerialization()
        {
            JobDataMap map = new JobDataMap();
            map["foo"] = "bar";
            map["num"] = 123;
            JobDataMap clone = map.DeepClone();
            Assert.AreEqual("bar", clone["foo"]);
            Assert.AreEqual(123, clone["num"]);
        }

        [Test]
        public void TestStringKeyDirtyFlagMapSerialization()
        {
            StringKeyDirtyFlagMap map = new StringKeyDirtyFlagMap();
            map["foo"] = "bar";
            map["num"] = 123;

            StringKeyDirtyFlagMap clone = map.DeepClone();
            Assert.AreEqual("bar", clone["foo"]);
            Assert.AreEqual(123, clone["num"]);
        }

        [Test]
        public void TestSchedulerContextSerialization()
        {
            SchedulerContext map = new SchedulerContext();
            map["foo"] = "bar";
            map["num"] = 123;

            SchedulerContext clone = map.DeepClone();
            Assert.AreEqual("bar", clone["foo"]);
            Assert.AreEqual(123, clone["num"]);
        }

        private static T Deserialize<T>() where T : class
        {
            return Deserialize<T>(10);
        }

        private static T Deserialize<T>(int version) where T : class
        {
            BinaryFormatter formatter = new BinaryFormatter();
            object o = formatter.Deserialize(File.OpenRead(@"Serialized\" + typeof(T).Name + "_" + version + ".ser"));
            return (T) o;
        }
    }
}