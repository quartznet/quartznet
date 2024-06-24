using System.Collections.Specialized;
using System.Diagnostics;
using System.Text;

using FluentAssertions;

using NUnit.Framework;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Simpl;

namespace Quartz.Tests.Unit.Simpl;

public class JsonObjectSerializerTest
{
    private NewtonsoftJsonObjectSerializer newtonsoftSerializer;
    private SystemTextJsonObjectSerializer systemTextJsonSerializer;

    [SetUp]
    public void SetUp()
    {
        newtonsoftSerializer = new NewtonsoftJsonObjectSerializer();
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
            {"key2", DateTimeOffset.UtcNow.DateTime},
            {"key3", true}
        };

        CompareSerialization(collection, (deserialized, original) =>
        {
            deserialized.Keys.Should().ContainInOrder(original.Keys);
            deserialized.Values.Should().ContainInOrder(original.Values);
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
    [Ignore("Currently trigger serialization isn't being used")]
    public void SerializeCalendarIntervalTrigger()
    {
        var trigger = new CalendarIntervalTriggerImpl("name", "group", DateTimeOffset.UtcNow,  DateTimeOffset.UtcNow.AddDays(1), IntervalUnit.Second, 42);

        CompareSerialization(trigger);
    }

    [Test]
    [Ignore("Currently trigger serialization isn't being used")]
    public void SerializeCronTrigger()
    {
        var trigger = new CronTriggerImpl("name", "group", "jobName", "jobGroup", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), "0/5 * * * * ?", TimeZoneInfo.Local);

        CompareSerialization(trigger);
    }

    [Test]
    [Ignore("Currently trigger serialization isn't being used")]
    public void SerializeDailyTimeIntervalTrigger()
    {
        var trigger = new DailyTimeIntervalTriggerImpl("name", "group", "jobName", "jobGroup", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), TimeOfDay.HourAndMinuteOfDay(3, 30), TimeOfDay.HourAndMinuteOfDay(4, 40), IntervalUnit.Second, 42);

        CompareSerialization(trigger);
    }

    [Test]
    [Ignore("Currently trigger serialization isn't being used")]
    public void SerializeSimpleTrigger()
    {
        var trigger = new SimpleTriggerImpl("name", "group", "jobName", "jobGroup", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(1), 10, TimeSpan.FromSeconds(42));

        CompareSerialization(trigger);
    }

    private void CompareSerialization<T>(T original, Action<T, T> asserter = null) where T : class
    {
        var bytes = newtonsoftSerializer.Serialize(original);

        WriteJson(bytes);

        var deserialized = newtonsoftSerializer.DeSerialize<T>(bytes);

        if (asserter != null)
        {
            asserter(deserialized, original);
        }
        else
        {
            deserialized.Should().Be(original);
        }

        bytes = systemTextJsonSerializer.Serialize(original);

        WriteJson(bytes);

        deserialized = newtonsoftSerializer.DeSerialize<T>(bytes);

        if (asserter != null)
        {
            asserter(deserialized, original);
        }
        else
        {
            deserialized.Should().Be(original);
        }


        bytes = newtonsoftSerializer.Serialize(original);

        WriteJson(bytes);

        deserialized = systemTextJsonSerializer.DeSerialize<T>(bytes);

        if (asserter != null)
        {
            asserter(deserialized, original);
        }
        else
        {
            deserialized.Should().Be(original);
        }

        bytes = systemTextJsonSerializer.Serialize(original);

        WriteJson(bytes);

        deserialized = systemTextJsonSerializer.DeSerialize<T>(bytes);

        if (asserter != null)
        {
            asserter(deserialized, original);
        }
        else
        {
            deserialized.Should().Be(original);
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