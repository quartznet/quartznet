using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
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
            serializer.Initialize();
        }

        [Test]
        public void SerializeAnnualCalendar()
        {
            var calendar = new AnnualCalendar();
            calendar.Description = "description";
            calendar.SetDayExcluded(DateTime.UtcNow.Date, true);
            CompareSerialization(calendar);
        }

        [Test]
        public void SerializeBaseCalendar()
        {
            var calendar = new BaseCalendar();
            calendar.Description = "description";
            CompareSerialization(calendar);
        }

        [Test]
        public void SerializeCronCalendar()
        {
            var calendar = new CronCalendar("0/5 * * * * ?");
            calendar.Description = "description";
            CompareSerialization(calendar);
        }

        [Test]
        public void SerializeDailyCalendar()
        {
            var start = DateTime.UtcNow.Date.AddHours(1).AddMinutes(1).AddSeconds(1).AddMilliseconds(1);
            var calendar = new DailyCalendar(start, start.AddHours(1).AddMinutes(1).AddSeconds(1).AddMilliseconds(1));
            calendar.Description = "description";
            calendar.InvertTimeRange = true;
            CompareSerialization(calendar);
        }

        [Test]
        public void SerializeHolidayCalendar()
        {
            var calendar = new HolidayCalendar();
            calendar.Description = "description";
            calendar.AddExcludedDate(DateTime.UtcNow.Date);
            CompareSerialization(calendar);
        }

        [Test]
        public void SerializeMonthlyCalendar()
        {
            var calendar = new MonthlyCalendar();
            calendar.Description = "description";
            calendar.SetDayExcluded(23, true);
            CompareSerialization(calendar);
        }

        [Test]
        public void SerializeWeeklyCalendar()
        {
            var calendar = new WeeklyCalendar();
            calendar.Description = "description";
            calendar.SetDayExcluded(DayOfWeek.Thursday, true);
            CompareSerialization(calendar);
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

        [Test]
        public void SerializeJobDataMap()
        {
            var collection = new JobDataMap
            {
                {"key", "value"},
                {"key2", DateTimeOffset.UtcNow},
                {"jobKey", new JobKey("name", "group")}
            };

            CompareSerialization(collection, (deserialized, original) =>
            {
                Assert.That(deserialized.Keys.SequenceEqual(original.Keys));
                Assert.That(deserialized.Values.SequenceEqual(original.Values));
            });
        }

        [Test]
        public void SerializeChainedCalendars()
        {
            var annualCalendar = new AnnualCalendar();
            annualCalendar.Description = "description";
            annualCalendar.SetDayExcluded(DateTime.UtcNow.Date, true);

            var cronCalendar = new CronCalendar("0/5 * * * * ?");
            cronCalendar.CalendarBase = annualCalendar;

            CompareSerialization(cronCalendar);
        }

        [Test]
        public void SerializeCronExpression()
        {
            var cronExpression = new CronExpression("0/5 * * * * ?");

            CompareSerialization(cronExpression);
        }

        private void CompareSerialization<T>(T original, Action<T, T> asserter = null) where T : class
        {
            var bytes = serializer.Serialize(original);

            WriteJson(bytes);

            var deserialized = serializer.DeSerialize<T>(bytes);

            if (asserter != null)
            {
                asserter(deserialized, original);
            }
            else
            {
                Assert.That(deserialized, Is.EqualTo(original));
            }
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