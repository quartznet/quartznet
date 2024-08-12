using System.Collections.Specialized;
using System.Diagnostics;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;

using FluentAssertions;
using FluentAssertions.Execution;

using Microsoft.Extensions.Time.Testing;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Simpl;
using Quartz.Spi;

using StjJsonSerializerOptions = System.Text.Json.JsonSerializerOptions;

namespace Quartz.Tests.Unit.Simpl;

public class JsonObjectSerializerTest
{
    private NewtonsoftJsonObjectSerializer newtonsoftSerializer;
    private SystemTextJsonObjectSerializer systemTextJsonSerializer;

    [SetUp]
    public void SetUp()
    {
        newtonsoftSerializer = new IndentingJsonObjectSerializer();
        newtonsoftSerializer.RegisterTriggerConverters = true;
        newtonsoftSerializer.Initialize();
        NewtonsoftJsonObjectSerializer.AddCalendarSerializer<JsonSerializationTestCalendar>(new JsonSerializationTestCalendar.NewtonsoftSerializer());
        NewtonsoftJsonObjectSerializer.AddTriggerSerializer<JsonSerializationTestTrigger>(new JsonSerializationTestTrigger.NewtonsoftSerializer());

        systemTextJsonSerializer = new IndentingSystemTextJsonObjectSerializer();
        systemTextJsonSerializer.Initialize();
        SystemTextJsonObjectSerializer.AddCalendarSerializer<JsonSerializationTestCalendar>(new JsonSerializationTestCalendar.SystemTextJsonSerializer());
        SystemTextJsonObjectSerializer.AddTriggerSerializer<JsonSerializationTestTrigger>(new JsonSerializationTestTrigger.SystemTextJsonSerializer());
    }

    [Test]
    public async Task SerializeAnnualCalendar()
    {
        var timeProvider = CreateFakeTimeProvider();
        var calendar = new AnnualCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test AnnualCalendar",
            CalendarBase = new BaseCalendar
            {
                TimeZone = TimeZoneInfo.Utc
            }
        };

        calendar.SetDayExcluded(timeProvider.GetUtcNow().Date, true);

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeBaseCalendar()
    {
        var calendar = new BaseCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test BaseCalendar"
        };

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeCronCalendar()
    {
        var calendar = new CronCalendar("0/5 * * * * ?")
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test CronCalendar",
            CalendarBase = null
        };

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeDailyCalendar()
    {
        var timeProvider = CreateFakeTimeProvider();

        var start = timeProvider.GetUtcNow().Date.AddHours(1).AddMinutes(1).AddSeconds(1).AddMilliseconds(1);
        var calendar = new DailyCalendar(start, start.AddHours(1).AddMinutes(1).AddSeconds(1).AddMilliseconds(1))
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = null,
            CalendarBase = new BaseCalendar
            {
                TimeZone = TimeZoneInfo.Utc
            },
            InvertTimeRange = true
        };

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeHolidayCalendar()
    {
        var timeProvider = CreateFakeTimeProvider();

        var calendar = new HolidayCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test HolidayCalendar",
            CalendarBase = null
        };

        calendar.AddExcludedDate(timeProvider.GetUtcNow().Date);

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeMonthlyCalendar()
    {
        var calendar = new MonthlyCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test MonthlyCalendar",
            CalendarBase = new BaseCalendar
            {
                TimeZone = TimeZoneInfo.Utc
            }
        };

        calendar.SetDayExcluded(10, true);
        calendar.SetDayExcluded(20, true);
        calendar.SetDayExcluded(23, true);
        calendar.SetDayExcluded(30, true);

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeWeeklyCalendar()
    {
        var calendar = new WeeklyCalendar
        {
            TimeZone = TimeZoneInfo.Utc,
            Description = "Test WeeklyCalendar",
            CalendarBase = null
        };

        calendar.SetDayExcluded(DayOfWeek.Wednesday, true);
        calendar.SetDayExcluded(DayOfWeek.Thursday, true);
        calendar.SetDayExcluded(DayOfWeek.Friday, true);

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeNameValueCollection()
    {
        var collection = new NameValueCollection
        {
            { "key", "value" },
            { "key2", null }
        };

        CompareSerialization(collection, (deserialized, original) =>
        {
            using (new AssertionScope())
            {
                original.Count.Should().Be(2);
                deserialized.Count.Should().Be(2);
                deserialized["key"].Should().Be(original["key"]);
                deserialized["key2"].Should().Be(original["key2"]);
            }
        });

        await VerifyCreatedJson(collection);
    }

    [Test]
    public async Task SerializeJobDataMap()
    {
        var collection = new JobDataMap
        {
            { "key", "value" },
            { "key2", new DateTime(1982, 6, 28, 1, 1, 1, DateTimeKind.Unspecified) },
            { "key3", true },
            { "key4", 123 },
            { "key5", 12.34 },
            { "key6", new DateTimeOffset(1982, 6, 28, 1, 1, 1, TimeSpan.Zero) },
            { "key7", new DateTimeOffset(1982, 6, 28, 1, 1, 1, TimeSpan.FromHours(3)) }
        };

        CompareSerialization(
            collection,
            (deserialized, original) =>
            {
                using (new AssertionScope())
                {
                    original.Should().HaveCount(7);
                    deserialized.Should().HaveCount(7);
                    deserialized["key"].Should().Be(original["key"]);
                    deserialized.GetDateTime("key2").Should().Be(original.GetDateTime("key2"));
                    deserialized["key3"].Should().Be(original["key3"]);
                    deserialized["key4"].Should().Be(original["key4"]);
                    deserialized["key5"].Should().Be(original["key5"]);
                    deserialized.GetDateTimeOffset("key6").Should().Be(original.GetDateTimeOffset("key6"));
                    deserialized.GetDateTimeOffset("key7").Should().Be(original.GetDateTimeOffset("key7"));
                }
            },
            skipDefaultEqualityCheck: true
        );

        await VerifyCreatedJson(collection);
    }

    [Test]
    public async Task SerializeChainedCalendars()
    {
        var timeProvider = CreateFakeTimeProvider();

        var annualCalendar = new AnnualCalendar();
        annualCalendar.Description = "description";
        annualCalendar.SetDayExcluded(timeProvider.GetUtcNow().Date, true);
        annualCalendar.TimeZone = TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time");

        var cronCalendar = new CronCalendar("0/5 * * * * ?");
        cronCalendar.CalendarBase = annualCalendar;
        cronCalendar.TimeZone = TimeZoneInfo.Utc;

        CompareSerialization(cronCalendar);
        await VerifyCreatedJson(cronCalendar);
    }

    [Test]
    public async Task SerializeCustomCalendars()
    {
        var calendar = new JsonSerializationTestCalendar
        {
            Description = "Custom calendar",
            CustomProperty = 42,
            TimeZone = TimeZoneInfo.Utc,
            CalendarBase = new BaseCalendar
            {
                TimeZone = TimeZoneInfo.Utc,
                Description = "Base calendar"
            }
        };

        CompareSerialization(calendar);
        await VerifyCreatedJson(calendar);
    }

    [Test]
    public async Task SerializeCronExpression()
    {
        var cronExpression = new CronExpression("0/5 * * * * ?")
        {
            TimeZone = TimeZoneInfo.Utc
        };

        CompareSerialization(cronExpression);
        await VerifyCreatedJson(cronExpression);
    }

    [Test]
    public async Task SerializeCalendarIntervalTrigger()
    {
        var timeProvider = CreateFakeTimeProvider();

        var trigger = (IOperableTrigger)TriggerBuilder.Create(timeProvider)
            .WithCalendarIntervalSchedule(builder => builder
                .WithInterval(42, IntervalUnit.Second)
                .InTimeZone(TimeZoneInfo.Utc)
                .PreserveHourOfDayAcrossDaylightSavings(true)
                .SkipDayIfHourDoesNotExist(false)
                .WithMisfireHandlingInstructionFireAndProceed()
            )
            .WithIdentity("CalendarIntervalTriggerKey", "CalendarIntervalTriggerGroup")
            .ForJob("CalendarIntervalJobKey", "CalendarIntervalJobGroup")
            .WithDescription("CalendarIntervalTrigger description")
            .ModifiedByCalendar("SomeCalendar")
            .UsingJobData("TestKey", "TestValue")
            .StartAt(timeProvider.GetUtcNow())
            .EndAt(timeProvider.GetUtcNow().AddDays(1))
            .WithPriority(TriggerConstants.DefaultPriority + 10)
            .Build();

        SetTimeProvider(timeProvider, trigger);

        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());

        CompareSerialization(
            trigger,
            (deserialized, original) =>
            {
                using (new AssertionScope())
                {
                    original.GetNextFireTimeUtc().Should().Be(deserialized.GetNextFireTimeUtc());
                    original.GetPreviousFireTimeUtc().Should().Be(deserialized.GetPreviousFireTimeUtc());
                }
            }
        );

        await VerifyCreatedJson(trigger);
    }

    [Test]
    public async Task SerializeCronTrigger()
    {
        var timeProvider = CreateFakeTimeProvider();

        var trigger = (IOperableTrigger)TriggerBuilder.Create(timeProvider)
            .WithCronSchedule("0/5 * * * * ?", builder => builder
                .InTimeZone(TimeZoneInfo.FindSystemTimeZoneById("Tokyo Standard Time"))
            )
            .WithIdentity("CronTriggerKey", "CronTriggerGroup")
            .ForJob("CronJobKey", "CronJobGroup")
            .WithDescription(null)
            .ModifiedByCalendar("SomeCalendar")
            .StartAt(timeProvider.GetUtcNow())
            .EndAt(timeProvider.GetUtcNow().AddDays(1))
            .WithPriority(1)
            .Build();

        SetTimeProvider(timeProvider, trigger);

        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());

        CompareSerialization(
            trigger,
            (deserialized, original) =>
            {
                using (new AssertionScope())
                {
                    original.GetNextFireTimeUtc().Should().Be(deserialized.GetNextFireTimeUtc());
                    original.GetPreviousFireTimeUtc().Should().Be(deserialized.GetPreviousFireTimeUtc());
                }
            }
        );

        await VerifyCreatedJson(trigger);
    }

    [Test]
    public async Task SerializeDailyTimeIntervalTrigger()
    {
        var timeProvider = CreateFakeTimeProvider();

        var trigger = (IOperableTrigger)TriggerBuilder.Create(timeProvider)
            .WithDailyTimeIntervalSchedule(builder => builder
                .WithRepeatCount(1_000)
                .WithInterval(42, IntervalUnit.Second)
                .StartingDailyAt(TimeOfDay.HourAndMinuteOfDay(3, 30))
                .EndingDailyAt(TimeOfDay.HourAndMinuteOfDay(4, 40))
                .OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Wednesday, DayOfWeek.Friday)
                .InTimeZone(TimeZoneInfo.Utc)
            )
            .WithIdentity("DailyTimeIntervalTriggerKey", "DailyTimeIntervalTriggerGroup")
            .WithDescription("DailyTimeIntervalTrigger description")
            .ModifiedByCalendar(null)
            .StartAt(timeProvider.GetUtcNow())
            .EndAt(timeProvider.GetUtcNow().AddDays(1))
            .Build();

        SetTimeProvider(timeProvider, trigger);

        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());

        CompareSerialization(
            trigger,
            (deserialized, original) =>
            {
                using (new AssertionScope())
                {
                    original.GetNextFireTimeUtc().Should().Be(deserialized.GetNextFireTimeUtc());
                    original.GetPreviousFireTimeUtc().Should().Be(deserialized.GetPreviousFireTimeUtc());
                }
            }
        );

        await VerifyCreatedJson(trigger);
    }

    [Test]
    public async Task SerializeSimpleTrigger()
    {
        var timeProvider = CreateFakeTimeProvider();

        var trigger = (IOperableTrigger)TriggerBuilder.Create(timeProvider)
            .WithSimpleSchedule(builder => builder
                .WithInterval(new TimeSpan(120, 2, 30, 59, 999))
                .WithRepeatCount(10)
            )
            .WithIdentity("SimpleTriggerKey", "SimpleTriggerGroup")
            .ForJob("SimpleJobKey", "SimpleJobGroup")
            .WithDescription("SimpleTrigger description")
            .ModifiedByCalendar("SomeOtherCalendar")
            .UsingJobData("TestKey", "150")
            .StartAt(timeProvider.GetUtcNow())
            .EndAt(timeProvider.GetUtcNow().AddYears(1_000))
            .WithPriority(150_000)
            .Build();

        SetTimeProvider(timeProvider, trigger);

        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());

        CompareSerialization(
            trigger,
            (deserialized, original) =>
            {
                using (new AssertionScope())
                {
                    original.GetNextFireTimeUtc().Should().Be(deserialized.GetNextFireTimeUtc());
                    original.GetPreviousFireTimeUtc().Should().Be(deserialized.GetPreviousFireTimeUtc());
                }
            }
        );

        await VerifyCreatedJson(trigger);
    }

    [Test]
    public async Task SerializeCustomTriggers()
    {
        var timeProvider = CreateFakeTimeProvider();

        var trigger = new JsonSerializationTestTrigger
        {
            RepeatInterval = TimeSpan.FromDays(1),
            RepeatCount = 10,
            Key = new TriggerKey("SimpleTriggerKey", "SimpleTriggerGroup"),
            JobKey = new JobKey("SimpleJobKey", "SimpleJobGroup"),
            Description = "Custom trigger description",
            CalendarName = "SomeRandomCalendar",
            StartTimeUtc = timeProvider.GetUtcNow(),
            EndTimeUtc = timeProvider.GetUtcNow().AddYears(1),
            Priority = 100,
            MisfireInstruction = MisfireInstruction.IgnoreMisfirePolicy,
            CustomProperty = 56
        };

        trigger.JobDataMap.Add("Key", "34");
        SetTimeProvider(timeProvider, trigger);

        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());
        trigger.Triggered(new BaseCalendar());

        CompareSerialization(
            trigger,
            (deserialized, original) =>
            {
                using (new AssertionScope())
                {
                    original.GetNextFireTimeUtc().Should().Be(deserialized.GetNextFireTimeUtc());
                    original.GetPreviousFireTimeUtc().Should().Be(deserialized.GetPreviousFireTimeUtc());
                }
            }
        );

        await VerifyCreatedJson(trigger);
    }

    private void CompareSerialization<T>(
        T original,
        Action<T, T> asserter = null,
        bool skipDefaultEqualityCheck = false) where T : class
    {
        (IObjectSerializer, IObjectSerializer)[] comparisons =
        [
            (newtonsoftSerializer, newtonsoftSerializer),
            (newtonsoftSerializer, systemTextJsonSerializer),
            (systemTextJsonSerializer, newtonsoftSerializer),
            (systemTextJsonSerializer, systemTextJsonSerializer),
        ];

        foreach (var (serializer, deserializer) in comparisons)
        {
            byte[] bytes = serializer.Serialize(original);
            T deserialized = deserializer.DeSerialize<T>(bytes);

            asserter?.Invoke(deserialized, original);

            if (!skipDefaultEqualityCheck)
            {
                deserialized.Should().BeEquivalentTo(original);
            }
        }
    }

    private async Task VerifyCreatedJson(object toSerialize, [CallerMemberName] string testMethod = "")
    {
        foreach (var serializer in (IObjectSerializer[]) [systemTextJsonSerializer, newtonsoftSerializer])
        {
            var data = serializer.Serialize(toSerialize);
            using var reader = new StringReader(Encoding.UTF8.GetString(data));
            var json = await reader.ReadToEndAsync();

            var verifier = Verify(json, extension: "txt")
                .UseDirectory("../Verify")
                .UseFileName($"JsonObjectSerializerTest_{testMethod}")
                .DisableRequireUniquePrefix();

            if (Debugger.IsAttached)
            {
                verifier = verifier.AutoVerify();
            }

            await verifier;
        }
    }

    private static FakeTimeProvider CreateFakeTimeProvider()
    {
        return new FakeTimeProvider(new DateTimeOffset(2024, 7, 1, 0, 0, 0, TimeSpan.Zero))
        {
            AutoAdvanceAmount = TimeSpan.FromMilliseconds(500)
        };
    }

    private static void SetTimeProvider(TimeProvider timeProvider, ITrigger trigger)
    {
        var field = typeof(AbstractTrigger).GetField("timeProvider", BindingFlags.Instance | BindingFlags.NonPublic);
        field!.SetValue(trigger, timeProvider);
    }

    private class IndentingJsonObjectSerializer : NewtonsoftJsonObjectSerializer
    {
        protected override JsonSerializerSettings CreateSerializerSettings()
        {
            var settings = base.CreateSerializerSettings();
            settings.Formatting = Formatting.Indented;
            return settings;
        }
    }

    private class IndentingSystemTextJsonObjectSerializer : SystemTextJsonObjectSerializer
    {
        protected override StjJsonSerializerOptions CreateSerializerOptions()
        {
            var options = base.CreateSerializerOptions();
            options.WriteIndented = true;
            return options;
        }
    }
}

public class JsonSerializationTestCalendar : BaseCalendar
{
    public int CustomProperty { get; set; }

    public sealed class NewtonsoftSerializer : Quartz.Serialization.Newtonsoft.CalendarSerializer<JsonSerializationTestCalendar>
    {
        protected override void SerializeFields(JsonWriter writer, JsonSerializationTestCalendar calendar)
        {
            writer.WritePropertyName("CustomProperty");
            writer.WriteValue(calendar.CustomProperty);
        }

        protected override void DeserializeFields(JsonSerializationTestCalendar calendar, JObject source)
        {
            calendar.CustomProperty = source["CustomProperty"]!.Value<int>()!;
        }

        protected override JsonSerializationTestCalendar Create(JObject source) => new();
    }

    public sealed class SystemTextJsonSerializer : Serialization.Json.Calendars.CalendarSerializer<JsonSerializationTestCalendar>
    {
        protected override JsonSerializationTestCalendar Create(JsonElement jsonElement, StjJsonSerializerOptions options) => new();

        protected override void SerializeFields(Utf8JsonWriter writer, JsonSerializationTestCalendar calendar, StjJsonSerializerOptions options)
        {
            writer.WriteNumber("CustomProperty", calendar.CustomProperty);
        }

        protected override void DeserializeFields(JsonSerializationTestCalendar calendar, JsonElement jsonElement, StjJsonSerializerOptions options)
        {
            calendar.CustomProperty = jsonElement.GetProperty("CustomProperty").GetInt32();
        }

        public override string CalendarTypeName => "TestCalendar";
    }
}

public class JsonSerializationTestTrigger : SimpleTriggerImpl
{
    public int CustomProperty { get; set; }

    public sealed class SystemTextJsonSerializer : Serialization.Json.Triggers.TriggerSerializer<JsonSerializationTestTrigger>
    {
        public override string TriggerTypeForJson => "TestTrigger";

        public override IScheduleBuilder CreateScheduleBuilder(JsonElement jsonElement, StjJsonSerializerOptions options)
        {
            var repeatIntervalString = jsonElement.GetProperty("RepeatIntervalTimeSpan").GetString() ?? "";
            var repeatInterval = TimeSpan.ParseExact(repeatIntervalString, "c", CultureInfo.InvariantCulture);
            var repeatCount = jsonElement.GetProperty("RepeatCount").GetInt32();

            var trigger = new JsonSerializationTestTrigger
            {
                RepeatInterval = repeatInterval,
                RepeatCount = repeatCount
            };

            return new StaticScheduleBuilder(trigger);
        }

        protected override void SerializeFields(Utf8JsonWriter writer, JsonSerializationTestTrigger trigger, StjJsonSerializerOptions options)
        {
            writer.WriteNumber("RepeatCount", trigger.RepeatCount);
            writer.WriteString("RepeatIntervalTimeSpan", trigger.RepeatInterval.ToString("c"));
            writer.WriteNumber("TimesTriggered", trigger.TimesTriggered);
            writer.WriteNumber("CustomProperty", trigger.CustomProperty);
        }

        protected override void DeserializeFields(JsonSerializationTestTrigger trigger, JsonElement jsonElement, StjJsonSerializerOptions options)
        {
            trigger.TimesTriggered = jsonElement.GetProperty("TimesTriggered").GetInt32();
            trigger.CustomProperty = jsonElement.GetProperty("CustomProperty").GetInt32();
        }

        private sealed class StaticScheduleBuilder(IMutableTrigger trigger) : IScheduleBuilder
        {
            public IMutableTrigger Build() => trigger;
        }
    }

    public sealed class NewtonsoftSerializer : Triggers.TriggerSerializer<JsonSerializationTestTrigger>
    {
        public override string TriggerTypeForJson => "TestTrigger";

        public override IScheduleBuilder CreateScheduleBuilder(JObject jsonElement)
        {
            var repeatIntervalString = jsonElement.Value<string>("RepeatIntervalTimeSpan") ?? "";
            var repeatInterval = TimeSpan.ParseExact(repeatIntervalString, "c", CultureInfo.InvariantCulture);
            var repeatCount = jsonElement.Value<int>("RepeatCount");

            var trigger = new JsonSerializationTestTrigger
            {
                RepeatInterval = repeatInterval,
                RepeatCount = repeatCount
            };

            return new StaticScheduleBuilder(trigger);
        }

        protected override void SerializeFields(JsonWriter writer, JsonSerializationTestTrigger trigger)
        {
            writer.WritePropertyName("RepeatCount");
            writer.WriteValue(trigger.RepeatCount);

            writer.WritePropertyName("RepeatIntervalTimeSpan");
            writer.WriteValue(trigger.RepeatInterval.ToString("c"));

            writer.WritePropertyName("TimesTriggered");
            writer.WriteValue(trigger.TimesTriggered);

            writer.WritePropertyName("CustomProperty");
            writer.WriteValue(trigger.CustomProperty);
        }

        protected override void DeserializeFields(JsonSerializationTestTrigger trigger, JObject jsonElement)
        {
            trigger.TimesTriggered = jsonElement.Value<int>("TimesTriggered");
            trigger.CustomProperty = jsonElement.Value<int>("CustomProperty");
        }

        private sealed class StaticScheduleBuilder(IMutableTrigger trigger) : IScheduleBuilder
        {
            public IMutableTrigger Build() => trigger;
        }
    }
}