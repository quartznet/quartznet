#nullable enable

using System.Collections.Specialized;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

using Quartz.Extensibility;
using Quartz.Impl;
using Quartz.Impl.Calendar;
using Quartz.Impl.Triggers;
using Quartz.Serialization.SystemTextJson;

namespace Quartz.Tests.Unit.Simpl;

/// <summary>
/// The store format resolves every type it writes without reflection, which is what lets a persistent
/// job store survive a trimmed or native-AOT publish.
/// </summary>
/// <remarks>
/// <para>
/// A test host always has reflection on, and <c>JsonSerializer.IsReflectionEnabledByDefault</c> cannot
/// be flipped in a running process — it is a feature switch a publish substitutes away. So the
/// serializer under test here is the real one with the reflection resolver taken back out of its chain,
/// which is precisely the chain a trimmed publish leaves behind. A type nobody named then has no
/// answer at all, and the round trip throws instead of quietly reflecting.
/// </para>
/// <para>
/// <c>Quartz.Trimming.Canary</c> proves the same thing the other way round, out of an actual trimmed
/// publish. This test is the one that fails on a laptop.
/// </para>
/// </remarks>
public class StoreFormatSourceGenerationTest
{
    private static IEnumerable<TestCaseData> BuiltInTriggers()
    {
        yield return Trigger("SimpleTrigger", SimpleScheduleBuilder.Create()
            .WithInterval(TimeSpan.FromMinutes(5))
            .WithRepeatCount(3));

        yield return Trigger("CronTrigger", CronScheduleBuilder.Create("0/5 * * * * ?"));

        yield return Trigger("CalendarIntervalTrigger", CalendarIntervalScheduleBuilder.Create()
            .WithInterval(2, IntervalUnit.Day));

        yield return Trigger("DailyTimeIntervalTrigger", DailyTimeIntervalScheduleBuilder.Create()
            .WithInterval(30, IntervalUnit.Minute)
            .StartingDailyAt(new TimeOnly(8, 0))
            .EndingDailyAt(new TimeOnly(17, 0)));

        yield return Trigger("RecurrenceTrigger", RecurrenceScheduleBuilder.Create("FREQ=DAILY")
            .InTimeZone(TimeZoneInfo.Utc));

        static TestCaseData Trigger(string name, IScheduleBuilder schedule)
        {
            IOperableTrigger trigger = (IOperableTrigger) TriggerBuilder.Create()
                .WithSchedule(schedule)
                .WithIdentity(name, "StoreFormat")
                .ForJob("Job", "StoreFormat")
                .StartAt(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero))
                .UsingJobData("value", "kept")
                .Build();

            return new TestCaseData(trigger).SetArgDisplayNames(name);
        }
    }

    private static IEnumerable<TestCaseData> BuiltInCalendars()
    {
        yield return Calendar("BaseCalendar", new BaseCalendar());
        yield return Calendar("AnnualCalendar", new AnnualCalendar());
        yield return Calendar("CronCalendar", new CronCalendar("0/5 * * * * ?"));
        yield return Calendar("DailyCalendar", new DailyCalendar(new TimeOnly(1, 0), new TimeOnly(2, 0)));
        yield return Calendar("HolidayCalendar", new HolidayCalendar());
        yield return Calendar("MonthlyCalendar", new MonthlyCalendar());
        yield return Calendar("WeeklyCalendar", new WeeklyCalendar());
        yield return Calendar("ChainedCalendars", new CronCalendar("0/5 * * * * ?")
        {
            CalendarBase = new AnnualCalendar { Description = "the base of the chain" }
        });

        static TestCaseData Calendar(string name, ICalendar calendar)
        {
            calendar.Description = calendar.Description ?? name;
            return new TestCaseData(calendar).SetArgDisplayNames(name);
        }
    }

    [TestCaseSource(nameof(BuiltInTriggers))]
    public void BuiltInTriggerRoundTripsWithoutReflection(IOperableTrigger trigger)
    {
        IObjectSerializer serializer = ReflectionlessSerializer();

        IOperableTrigger? restored = serializer.Deserialize<IOperableTrigger>(serializer.Serialize(trigger));

        restored.Should().BeEquivalentTo(trigger,
            $"{trigger.GetType().Name} is written by every persistent job store, so its metadata has to be answerable with reflection off");
    }

    [TestCaseSource(nameof(BuiltInCalendars))]
    public void BuiltInCalendarRoundTripsWithoutReflection(ICalendar calendar)
    {
        IObjectSerializer serializer = ReflectionlessSerializer();

        ICalendar? restored = serializer.Deserialize<ICalendar>(serializer.Serialize(calendar));

        restored.Should().BeEquivalentTo(calendar,
            $"{calendar.GetType().Name} is stored as a blob of its own, and a chained one carries a second calendar inside it");
    }

    [Test]
    public void JobDataMapRoundTripsEveryValueTheReadSideCanProduce()
    {
        // Exactly the set SerializationExtensions.GetJobDataMap can hand back: past these, a stored
        // value has no reading, whatever it was written from.
        JobDataMap map = new()
        {
            { "string", "value" },
            { "bool", true },
            { "int", 42 },
            { "long", 9_000_000_000L },
            { "double", 12.34 },
            { "null", null },
            { "dictionary", new Dictionary<string, string> { ["inner"] = "value" } }
        };

        IObjectSerializer serializer = ReflectionlessSerializer();
        JobDataMap? restored = serializer.Deserialize<JobDataMap>(serializer.Serialize(map));

        restored.Should().NotBeNull();
        restored!["string"].Should().Be("value");
        restored["bool"].Should().Be(true);
        restored["int"].Should().Be(42);
        restored["long"].Should().Be(9_000_000_000L);
        restored["double"].Should().Be(12.34);
        restored["null"].Should().BeNull();
        restored["dictionary"].Should().BeEquivalentTo(new Dictionary<string, string> { ["inner"] = "value" },
            "an object-valued entry comes back as Dictionary<string, string>, which is why that closed form is named in the context");
    }

    [Test]
    public void JobDataMapWritesEveryValueTypeQuartzDeclaresAnAccessorFor()
    {
        // DataMapExtensions is what tells an application which types a job data map holds, so the write
        // side has to answer for each of them with reflection off. Several read back as a string or a
        // number rather than as themselves - that is the store format, not this test's concern.
        JobDataMap map = new()
        {
            { "char", 'q' },
            { "float", 1.5f },
            { "decimal", 9.99m },
            { "dateTime", new DateTime(1982, 6, 28, 1, 1, 1, DateTimeKind.Utc) },
            { "dateTimeOffset", new DateTimeOffset(1982, 6, 28, 1, 1, 1, TimeSpan.FromHours(3)) },
            { "timeSpan", TimeSpan.FromMinutes(90) },
            { "guid", Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff") }
        };

        IObjectSerializer serializer = ReflectionlessSerializer();
        Action write = () => serializer.Serialize(map);

        write.Should().NotThrow(
            "every type DataMapExtensions declares an accessor for is a type Quartz teaches an application to store, so a trimmed application must be able to write it");
    }

    /// <summary>
    /// <c>JobDataValues</c> is the one list the writer refuses against, so every type on it has to be
    /// answerable without reflection too. A type accepted on write and unanswerable in a trimmed
    /// publish would be refused nothing and then fail at the writer anyway, which is the failure the
    /// refusal exists to replace.
    /// </summary>
    [Test]
    public void EveryAcceptedJobDataValueTypeIsAnsweredWithoutReflection()
    {
        JsonSerializerOptions options = new TestSerializer(new SystemTextJsonSerializerRegistry(), withoutReflection: true).Options();

        foreach (Type accepted in JobDataValues.Accepted)
        {
            Action resolve = () => options.GetTypeInfo(accepted);

            resolve.Should().NotThrow(
                $"{accepted.Name} is a value the writer accepts, so QuartzStoreJsonContext has to name it");
        }
    }

    [Test]
    public void CronExpressionAndNameValueCollectionRoundTripWithoutReflection()
    {
        IObjectSerializer serializer = ReflectionlessSerializer();

        CronExpression expression = new("0/5 * * * * ?", TimeZoneInfo.Utc);
        CronExpression? restoredExpression = serializer.Deserialize<CronExpression>(serializer.Serialize(expression));
        restoredExpression.Should().BeEquivalentTo(expression, "a cron expression has a converter of its own and so needs metadata of its own");

        NameValueCollection properties = new() { { "key", "value" } };
        NameValueCollection? restoredProperties = serializer.Deserialize<NameValueCollection>(serializer.Serialize(properties));
        restoredProperties.Should().BeEquivalentTo(properties,
            "under useProperties the store writes a job data map as a NameValueCollection, so that is a blob shape too");
    }

    [Test]
    public void TheChainIsTheContractThenTheRegistryThenReflection()
    {
        JsonSerializerOptions options = new TestSerializer(new SystemTextJsonSerializerRegistry()).Options();

        options.TypeInfoResolverChain[0].Should().BeOfType<QuartzStoreJsonContext>(
            "a type Quartz names must never reach reflection, and the chain is consulted in order");
        options.TypeInfoResolverChain[^1].Should().BeOfType<DefaultJsonTypeInfoResolver>(
            "the values inside a JobDataMap are whatever the application put there, so where reflection exists the chain still ends in it");
        options.TypeInfoResolverChain.Should().HaveCount(3,
            "the registry sits between the two: the trigger and calendar types registered with it, and whatever the application handed to AddTypeInfoResolver");
    }

    [Test]
    public void ApplicationResolversAreAskedAfterQuartzAndBeforeReflection()
    {
        SystemTextJsonSerializerRegistry registry = new();
        registry.AddTypeInfoResolver(JobDataValueContext.Default);

        JsonSerializerOptions options = new TestSerializer(registry).Options();

        options.TypeInfoResolverChain[2].Should().BeSameAs(JobDataValueContext.Default,
            "an application's own metadata answers for what Quartz cannot name, and is still asked before reflection");
        options.TypeInfoResolverChain[^1].Should().BeOfType<DefaultJsonTypeInfoResolver>(
            "handing in a resolver must not take the reflection fallback away from everything else");
    }

    [Test]
    public void CustomTriggerRoundTripsWithoutReflectionAndWithoutASeam()
    {
        SystemTextJsonSerializerRegistry registry = new();
        registry.AddTriggerSerializer<JsonSerializationTestTrigger>(new JsonSerializationTestTrigger.SystemTextJsonSerializer());

        JsonSerializationTestTrigger trigger = new()
        {
            Key = new TriggerKey("Custom", "StoreFormat"),
            JobKey = new JobKey("Job", "StoreFormat"),
            StartTimeUtc = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
            RepeatInterval = TimeSpan.FromHours(1),
            RepeatCount = 5,
            CustomProperty = 56
        };

        IObjectSerializer serializer = ReflectionlessSerializer(registry);
        IOperableTrigger? restored = serializer.Deserialize<IOperableTrigger>(serializer.Serialize(trigger));

        restored.Should().BeEquivalentTo(trigger,
            "AddTriggerSerializer<TTrigger> knows the type statically, so the registry can answer for a custom trigger itself and the application needs no JsonSerializerContext for it");
    }

    [Test]
    public void AJobDataValueTypeQuartzCannotNameNeedsTheApplicationsOwnMetadata()
    {
        JobDataMap map = new() { { "mode", JobDataValue.Fast } };

        Action withoutResolver = () => ReflectionlessSerializer().Serialize(map);

        withoutResolver.Should().Throw<JsonSerializationException>(
                "with reflection off, a value type neither Quartz nor the application named has no metadata at all")
            .WithInnerException<NotSupportedException>()
            .WithMessage($"*{typeof(JobDataValue).FullName}*",
                "the failure has to name the type the application has to answer for");

        SystemTextJsonSerializerRegistry registry = new();
        registry.AddTypeInfoResolver(JobDataValueContext.Default);

        IObjectSerializer serializer = ReflectionlessSerializer(registry);
        JobDataMap? restored = serializer.Deserialize<JobDataMap>(serializer.Serialize(map));

        restored.Should().NotBeNull();
        restored!["mode"].Should().Be((int) JobDataValue.Fast,
            "an enum goes out as its number and comes back as one, which is what GetInt reads - handing in the context is what makes the write possible at all");
    }

    private static IObjectSerializer ReflectionlessSerializer()
    {
        return ReflectionlessSerializer(new SystemTextJsonSerializerRegistry());
    }

    private static IObjectSerializer ReflectionlessSerializer(SystemTextJsonSerializerRegistry registry)
    {
        return new TestSerializer(registry, withoutReflection: true);
    }

    /// <summary>
    /// The production serializer, with its options reachable and — where asked — with the reflection
    /// resolver taken out of the chain, exactly as a trimmed publish takes it out.
    /// </summary>
    private sealed class TestSerializer(SystemTextJsonSerializerRegistry registry, bool withoutReflection = false)
        : SystemTextJsonObjectSerializer(registry)
    {
        public JsonSerializerOptions Options() => CreateSerializerOptions();

        protected override JsonSerializerOptions CreateSerializerOptions()
        {
            JsonSerializerOptions options = base.CreateSerializerOptions();
            if (!withoutReflection)
            {
                return options;
            }

            IList<IJsonTypeInfoResolver> chain = options.TypeInfoResolverChain;
            for (int i = chain.Count - 1; i >= 0; i--)
            {
                if (chain[i] is DefaultJsonTypeInfoResolver)
                {
                    chain.RemoveAt(i);
                }
            }

            return options;
        }
    }
}

/// <summary>A job-data value type of the application's own, which no contract of Quartz's can name.</summary>
public enum JobDataValue
{
    Slow = 0,
    Fast = 1
}

/// <summary>
/// The metadata an application hands to <see cref="SystemTextJsonSerializerRegistry.AddTypeInfoResolver" />
/// for its own job-data value types.
/// </summary>
[JsonSourceGenerationOptions(GenerationMode = JsonSourceGenerationMode.Metadata)]
[JsonSerializable(typeof(JobDataValue))]
internal sealed partial class JobDataValueContext : JsonSerializerContext;
