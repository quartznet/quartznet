using System;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Text;

using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

namespace Quartz.Tests.Unit.Simpl
{
    public class JsonObjectSerializerTest
    {
        private JsonObjectSerializer newtonsoftSerializer;
        private SystemTextJsonObjectSerializer systemTextJsonSerializer;

        [SetUp]
        public void SetUp()
        {
            newtonsoftSerializer = new JsonObjectSerializer();
            newtonsoftSerializer.Initialize();

            systemTextJsonSerializer = new SystemTextJsonObjectSerializer();
            systemTextJsonSerializer.Initialize();
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
            var collection = new NameValueCollection { { "key", "value" }, { "key2", null } };

            CompareSerialization(collection, (deserialized, original) =>
            {
                original.Count.Should().Be(2);
                deserialized.Count.Should().Be(2);
                deserialized["key"].Should().Be(original["key"]);
                deserialized["key2"].Should().Be(original["key2"]);
            });
        }

        [Test]
        public void SerializeJobDataMap()
        {
            var collection = new JobDataMap
            {
                { "key", "value" },
                { "key2", new DateTime(1982, 6, 28, 1, 1, 1, DateTimeKind.Unspecified) },
                { "key3", true },
                { "key4", 123 },
                { "key5", 12.34 },
            };

            CompareSerialization(collection, (deserialized, original) =>
            {
                original.Should().HaveCount(5);
                deserialized.Should().HaveCount(5);
                deserialized["key"].Should().Be(original["key"]);
                deserialized.GetDateTime("key2").Should().Be(original.GetDateTime("key2"));
                deserialized["key3"].Should().Be(original["key3"]);
                deserialized["key4"].Should().Be(original["key4"]);
                deserialized["key5"].Should().Be(original["key5"]);
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

        [Test]
        public void SerializeCalendarIntervalTrigger()
        {
            var trigger = new CalendarIntervalTriggerImpl("name", "group", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), IntervalUnit.Second, 42);

            CompareSerialization(trigger, systemTextJsonOnly: true);
        }

        [Test]
        public void SerializeCronTrigger()
        {
            var trigger = new CronTriggerImpl("name", "group", "jobName", "jobGroup", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "0/5 * * * * ?", TimeZoneInfo.Local);

            CompareSerialization(trigger, systemTextJsonOnly: true);
        }

        [Test]
        public void SerializeDailyTimeIntervalTrigger()
        {
            var trigger = new DailyTimeIntervalTriggerImpl("name", "group", "jobName", "jobGroup", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), TimeOfDay.HourAndMinuteOfDay(3, 30), TimeOfDay.HourAndMinuteOfDay(4, 40), IntervalUnit.Second, 42);

            CompareSerialization(trigger, systemTextJsonOnly: true);
        }

        [Test]
        public void SerializeSimpleTrigger()
        {
            var trigger = new SimpleTriggerImpl("name", "group", "jobName", "jobGroup", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), 10, TimeSpan.FromSeconds(42));

            CompareSerialization(trigger, systemTextJsonOnly: true);
        }

        private void CompareSerialization<T>(T original, Action<T, T> asserter = null, bool systemTextJsonOnly = false) where T : class
        {
            (IObjectSerializer, IObjectSerializer)[] comparisons = systemTextJsonOnly
                ?
                [
                    (systemTextJsonSerializer, systemTextJsonSerializer)
                ]
                :
                [
                    (newtonsoftSerializer, newtonsoftSerializer),
                    (newtonsoftSerializer, systemTextJsonSerializer),
                    (systemTextJsonSerializer, newtonsoftSerializer),
                    (systemTextJsonSerializer, systemTextJsonSerializer),
                ];

            foreach (var (serializer, deserializer) in comparisons)
            {
                byte[] bytes = serializer.Serialize(original);

                WriteJson(bytes);

                T deserialized = deserializer.DeSerialize<T>(bytes);

                if (asserter != null)
                {
                    asserter(deserialized, original);
                }
                else
                {
                    deserialized.Should().Be(original);
                }
            }
        }

        private static void WriteJson(byte[] bytes)
        {
            if (!Debugger.IsAttached)
            {
                return;
            }

            using StringReader reader = new(Encoding.UTF8.GetString(bytes));
            string json = reader.ReadToEnd();
            Console.WriteLine(json);
        }
    }
}