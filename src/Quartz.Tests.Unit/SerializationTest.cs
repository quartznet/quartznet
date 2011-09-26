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
            Serialize(new AnnualCalendar());
        }

        [Test]
        public void TestBaseCalendarDeserialization()
        {
            Deserialize<BaseCalendar>();
        }

        [Test]
        public void TestBaseCalendarSerialization()
        {
            Serialize(new BaseCalendar());
        }

        [Test]
        public void TestCronCalendarDeserialization()
        {
            Deserialize<CronCalendar>();
        }

        [Test]
        public void TestCronCalendarSerialization()
        {
            Serialize(new CronCalendar("* * 8-17 ? * *"));
        }

        [Test]
        public void TestDailyCalendarDeserialization()
        {
            Deserialize<DailyCalendar>();
        }

        [Test]
        public void TestDailyCalendarSerialization()
        {
            Serialize(new DailyCalendar("12:00:00:000", "13:14"));
        }

        [Test]
        public void TestHolidayCalendarDeserialization()
        {
            Deserialize<HolidayCalendar>();
        }

        [Test]
        public void TestHolidayCalendarSerialization()
        {
            Serialize(new HolidayCalendar());
        }

        [Test]
        public void TestMonthlyCalendarDeserialization()
        {
            Deserialize<MonthlyCalendar>();
        }

        [Test]
        public void TestMonthlyCalendarSerialization()
        {
            Serialize(new MonthlyCalendar());
        }

        [Test]
        public void TestWeeklyCalendarDeserialization()
        {
            Deserialize<WeeklyCalendar>();
        }

        [Test]
        public void TestWeeklyCalendarSerialization()
        {
            Serialize(new WeeklyCalendar());
        }

        [Test]
        public void TestTreeSetDeserialization()
        {
            Deserialize<TreeSet>();
        }

        [Test]
        public void TestTreeSetSerialization()
        {
            Serialize(new TreeSet<string>());
        }

        [Test]
        public void TestHashSetSerialization()
        {
            Serialize(new HashSet<string>());
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
            Serialize(map);
        }

        [Test]
        public void TestStringKeyDirtyFlagMapSerialization()
        {
            StringKeyDirtyFlagMap map = new StringKeyDirtyFlagMap();
            map["foo"] = "bar";
            map["num"] = 123;
            Serialize(map);
        }

        [Test]
        public void TestSchedulerContextSerialization()
        {
            SchedulerContext map = new SchedulerContext();
            map["foo"] = "bar";
            map["num"] = 123;
            Serialize(map);
        }

        private static void Serialize<T>(T item) where T : class
        {
            BinaryFormatter formatter = new BinaryFormatter();
            formatter.Serialize(new MemoryStream(), item);
        }

        private static T Deserialize<T>() where T : class 
        {
            BinaryFormatter formatter = new BinaryFormatter();
            object o = formatter.Deserialize(File.OpenRead(@"Serialized\" + typeof(T).Name + "_10.ser"));
            return (T) o;
        }
    }
}