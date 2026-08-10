using System.Text.Json;

using AwesomeAssertions.Execution;

using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Triggers;

using OpenApiTrigger = Quartz.AspNetCore.HttpApi.OpenApi.Trigger;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Holds the published OpenAPI trigger schema to what a trigger actually serializes to.
/// </summary>
/// <remarks>
/// The endpoints handle <see cref="ITrigger" />, which OpenAPI cannot describe, so
/// <c>Quartz.AspNetCore.HttpApi.OpenApi.Trigger</c> stands in for it in the generated document. Nothing
/// compiles against that interface and nothing reads it at runtime, so when a property was added to the
/// serialized trigger the schema simply kept describing the older payload — and a client generated from it
/// was missing fields the server sends. Nothing failed. This is what fails now.
/// </remarks>
public class OpenApiTriggerSchemaTest
{
    /// <summary>
    /// The converters as the HTTP API adds them, minus the naming policy. The API's options come from
    /// <see cref="JsonSerializerDefaults.Web" />, whose camelCase policy renames the payload and the
    /// generated schema alike, so it cancels out: comparing the policy-free names compares the same set.
    /// </summary>
    private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions()
        .AddQuartzConverters(new SystemTextJsonSerializerRegistry(), newtonsoftCompatibilityMode: false);

    /// <summary>
    /// One trigger of every type the built-in registry can serialize.
    /// <see cref="CorpusCoversEveryBuiltInTriggerType" /> is what keeps this list complete.
    /// </summary>
    private static ITrigger[] BuiltInTriggers() =>
    [
        TriggerBuilder.Create()
            .WithIdentity("calendar-interval", "group")
            .ForJob("job", "job-group")
            .WithCalendarIntervalSchedule(builder => builder.WithInterval(42, IntervalUnit.Second))
            .Build(),

        TriggerBuilder.Create()
            .WithIdentity("cron", "group")
            .WithCronSchedule("0/5 * * * * ?")
            .Build(),

        TriggerBuilder.Create()
            .WithIdentity("daily-time-interval", "group")
            .WithDailyTimeIntervalSchedule(builder => builder
                .WithInterval(42, IntervalUnit.Second)
                .OnDaysOfTheWeek(DayOfWeek.Monday, DayOfWeek.Friday))
            .Build(),

        TriggerBuilder.Create()
            .WithIdentity("recurrence", "group")
            .WithRecurrenceSchedule("FREQ=WEEKLY;BYDAY=MO,WE,FR")
            .Build(),

        TriggerBuilder.Create()
            .WithIdentity("simple", "group")
            .WithSimpleSchedule(builder => builder.WithInterval(TimeSpan.FromMinutes(1)).WithRepeatCount(10))
            .Build()
    ];

    [Test]
    public void CorpusCoversEveryBuiltInTriggerType()
    {
        string[] covered = BuiltInTriggers().Select(SerializedTriggerTypeOf).Order().ToArray();

        string[] shipped = typeof(ITriggerSerializer).Assembly.GetTypes()
            .Where(static type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                                  && typeof(ITriggerSerializer).IsAssignableFrom(type))
            .Select(static type => ((ITriggerSerializer) Activator.CreateInstance(type, nonPublic: true)!).TriggerTypeName)
            .Order()
            .ToArray();

        covered.Should().Equal(shipped,
            "a trigger type missing from the corpus is a trigger type whose serialized properties this test never looks at");
    }

    [Test]
    public void ShadowInterfaceMatchesWhatATriggerSerializesTo()
    {
        string[] serialized = BuiltInTriggers()
            .SelectMany(SerializedPropertyNamesOf)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] documented = typeof(OpenApiTrigger).GetProperties()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        using (new AssertionScope())
        {
            serialized.Except(documented, StringComparer.Ordinal).Should().BeEmpty(
                "the OpenAPI trigger schema is what clients are generated from, so a property the server sends and the schema omits is a field those clients silently drop — add it to Quartz.AspNetCore.HttpApi.OpenApi.Trigger");

            documented.Except(serialized, StringComparer.Ordinal).Should().BeEmpty(
                "a schema promising a property no trigger sends is the same bug in the other direction — remove it from Quartz.AspNetCore.HttpApi.OpenApi.Trigger");
        }
    }

    private static string SerializedTriggerTypeOf(ITrigger trigger)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(trigger, serializerOptions));
        return document.RootElement.GetProperty("TriggerType").GetString()!;
    }

    private static List<string> SerializedPropertyNamesOf(ITrigger trigger)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(trigger, serializerOptions));
        return document.RootElement.EnumerateObject().Select(static property => property.Name).ToList();
    }
}
