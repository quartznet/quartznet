using System.Text.Json;

using AwesomeAssertions.Execution;

using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;
using Quartz.Serialization.SystemTextJson.Calendars;

using OpenApiCalendar = Quartz.AspNetCore.HttpApi.OpenApi.Calendar;

namespace Quartz.Tests.AspNetCore.HttpApi;

/// <summary>
/// Holds the published OpenAPI calendar schema to what a calendar actually serializes to.
/// </summary>
/// <remarks>
/// The endpoints handle <see cref="ICalendar" />, which OpenAPI cannot describe, so
/// <c>Quartz.AspNetCore.HttpApi.OpenApi.Calendar</c> stands in for it in the generated document. Nothing
/// compiles against that interface and nothing reads it at runtime, so two of its property names could
/// drift away from the payload — it called the discriminator <c>calendarType</c> and the parent calendar
/// <c>calendarBase</c>, while the server writes <c>type</c> and <c>baseCalendar</c> — and a client
/// generated from it did not round-trip a calendar at all. Nothing failed. This is what fails now.
/// </remarks>
public class OpenApiCalendarSchemaTest
{
    /// <summary>
    /// The converters as the HTTP API adds them, minus the naming policy. The API's options come from
    /// <see cref="JsonSerializerDefaults.Web" />, whose camelCase policy renames the payload and the
    /// generated schema alike, so it cancels out: comparing the policy-free names compares the same set.
    /// <c>newtonsoftCompatibilityMode</c> is the mode the HTTP API registers, and it is what decides
    /// whether the discriminator is written as <c>Type</c> or as <c>$type</c>.
    /// </summary>
    private static readonly JsonSerializerOptions serializerOptions = new JsonSerializerOptions()
        .AddQuartzConverters(new SystemTextJsonSerializerRegistry(), newtonsoftCompatibilityMode: false);

    /// <summary>
    /// One calendar of every type the built-in registry can serialize.
    /// <see cref="CorpusCoversEveryBuiltInCalendarType" /> is what keeps this list complete.
    /// </summary>
    private static ICalendar[] BuiltInCalendars()
    {
        AnnualCalendar annual = new AnnualCalendar { Description = "annual" };
        annual.AddExcludedDay(new DateOnly(2024, 7, 1));

        HolidayCalendar holiday = new HolidayCalendar { Description = "holiday" };
        holiday.AddExcludedDay(new DateOnly(2024, 12, 25));

        MonthlyCalendar monthly = new MonthlyCalendar { Description = "monthly" };
        monthly.AddExcludedDay(13);

        WeeklyCalendar weekly = new WeeklyCalendar { Description = "weekly" };
        weekly.AddExcludedDay(DayOfWeek.Wednesday);

        return
        [
            new BaseCalendar { Description = "base" },
            annual,
            new CronCalendar("0/5 * * * * ?") { Description = "cron" },
            new DailyCalendar(new TimeOnly(1, 1, 1, 1), new TimeOnly(2, 2, 2, 2)) { Description = "daily" },
            holiday,
            monthly,
            weekly
        ];
    }

    [Test]
    public void CorpusCoversEveryBuiltInCalendarType()
    {
        string[] covered = BuiltInCalendars().Select(SerializedCalendarTypeOf).Order().ToArray();

        string[] shipped = typeof(ICalendarSerializer).Assembly.GetTypes()
            .Where(static type => type is { IsAbstract: false, IsInterface: false, IsGenericTypeDefinition: false }
                                  && typeof(ICalendarSerializer).IsAssignableFrom(type))
            .Select(static type => ((ICalendarSerializer) Activator.CreateInstance(type, nonPublic: true)!).CalendarTypeName)
            .Order()
            .ToArray();

        covered.Should().Equal(shipped,
            "a calendar type missing from the corpus is a calendar type whose serialized properties this test never looks at");
    }

    [Test]
    public void ShadowInterfaceMatchesWhatACalendarSerializesTo()
    {
        string[] serialized = BuiltInCalendars()
            .SelectMany(SerializedPropertyNamesOf)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        string[] documented = typeof(OpenApiCalendar).GetProperties()
            .Select(static property => property.Name)
            .Order(StringComparer.Ordinal)
            .ToArray();

        using (new AssertionScope())
        {
            serialized.Except(documented, StringComparer.Ordinal).Should().BeEmpty(
                "the OpenAPI calendar schema is what clients are generated from, so a property the server sends and the schema omits is a field those clients silently drop — add it to Quartz.AspNetCore.HttpApi.OpenApi.Calendar");

            documented.Except(serialized, StringComparer.Ordinal).Should().BeEmpty(
                "a schema promising a property no calendar sends is the same bug in the other direction — remove it from Quartz.AspNetCore.HttpApi.OpenApi.Calendar");
        }
    }

    private static string SerializedCalendarTypeOf(ICalendar calendar)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(calendar, serializerOptions));
        return document.RootElement.GetProperty("Type").GetString()!;
    }

    private static List<string> SerializedPropertyNamesOf(ICalendar calendar)
    {
        using JsonDocument document = JsonDocument.Parse(JsonSerializer.Serialize(calendar, serializerOptions));
        return document.RootElement.EnumerateObject().Select(static property => property.Name).ToList();
    }
}
