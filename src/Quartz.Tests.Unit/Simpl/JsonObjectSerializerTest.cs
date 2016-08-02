using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;

using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl
{
    [TestFixture]
    public class JsonObjectSerializerTest
    {
        private JsonObjectSerializer serializer;

        [SetUp]
        public void SetUp()
        {
            serializer = new JsonObjectSerializer();
        }

        [Test]
        public void SerializeAnnualCalendar()
        {
            var calendar = new AnnualCalendar();
            calendar.SetDayExcluded(DateTime.UtcNow.Date, true);
            CompareSerialization<ICalendar>(calendar);
        }

        [Test]
        public void SerializeCronCalendar()
        {
            var calendar = new CronCalendar("0/5 * * * * ?");
            CompareSerialization<ICalendar>(calendar);
        }

        [Test]
        public void SerializeDailyCalendar()
        {
            var calendar = new DailyCalendar(DateTime.UtcNow.Date, DateTime.UtcNow.Date.AddHours(2));
            CompareSerialization<ICalendar>(calendar);
        }

        [Test]
        public void SerializeHolidayCalendar()
        {
            var calendar = new HolidayCalendar();
            calendar.AddExcludedDate(DateTime.UtcNow.Date);
            CompareSerialization<ICalendar>(calendar);
        }

        [Test]
        public void SerializeMonthlyCalendar()
        {
            var calendar = new MonthlyCalendar();
            calendar.SetDayExcluded(23, true);
            CompareSerialization<ICalendar>(calendar);
        }

        [Test]
        public void SerializeWeeklyCalendar()
        {
            var calendar = new WeeklyCalendar();
            calendar.SetDayExcluded(DayOfWeek.Thursday, true);
            CompareSerialization<ICalendar>(calendar);
        }

        [Test]
        public void SerializeNameValueCollection()
        {
            var collection = new NameValueCollection
            {
                {"key", "value"},
                {"key2", null}
            };

            CompareSerialization(collection);
        }

        private void CompareSerialization<T>(T original) where T : class
        {
            var bytes = serializer.Serialize(original);

            WriteJson(bytes);

            var deserialized = serializer.DeSerialize<T>(bytes);

            Assert.That(deserialized, Is.EqualTo(original));
        }

        private static void WriteJson(byte[] bytes)
        {
            if (!Debugger.IsAttached)
            {
                return;
            }

            using (var reader = new StringReader(Encoding.UTF8.GetString(bytes)))
            {
                var json = reader.ReadToEnd();
                Console.WriteLine(json);
            }
        }
    }
}