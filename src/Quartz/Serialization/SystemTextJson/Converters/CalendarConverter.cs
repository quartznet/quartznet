using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Serialization.SystemTextJson.Converters;

internal sealed class CalendarConverter : JsonConverter<ICalendar>
{
    private readonly SystemTextJsonSerializerRegistry registry;
    private readonly bool newtonsoftCompatibilityMode;

    internal CalendarConverter(SystemTextJsonSerializerRegistry registry, bool newtonsoftCompatibilityMode)
    {
        this.registry = registry;
        this.newtonsoftCompatibilityMode = newtonsoftCompatibilityMode;
    }

    public override bool CanConvert(Type typeToConvert)
    {
        return typeof(ICalendar).IsAssignableFrom(typeToConvert);
    }

    public override ICalendar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
            return DeserializeCalendar(rootElement);

            ICalendar DeserializeCalendar(JsonElement rootElement)
            {
                var type = rootElement.GetProperty(newtonsoftCompatibilityMode ? "$type" : options.GetPropertyName("Type")).GetString();

                var calendarSerializer = registry.GetCalendarSerializer(type);
                var calendar = calendarSerializer.Create(rootElement, options);

                calendar.Description = rootElement.GetProperty(options.GetPropertyName("Description")).GetString();
                if (calendar is BaseCalendar target)
                {
                    target.TimeZone = rootElement.GetProperty(options.GetPropertyName("TimeZoneId")).GetTimeZone();
                }

                if (rootElement.TryGetProperty(options.GetPropertyName("BaseCalendar"), out JsonElement baseCalendarJsonElement)
                    && baseCalendarJsonElement.ValueKind != JsonValueKind.Null)
                {
                    calendar.CalendarBase = DeserializeCalendar(baseCalendarJsonElement);
                }

                calendarSerializer.DeserializeFields(calendar, rootElement, options);
                return calendar;
            }
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to parse ICalendar from json", e);
        }
    }

    public override void Write(Utf8JsonWriter writer, ICalendar value, JsonSerializerOptions options)
    {
        try
        {
            writer.WriteStartObject();
            var type = value.GetType().AssemblyQualifiedNameWithoutVersion();
            var calendarSerializer = registry.GetCalendarSerializer(type);

            string typeProperty = newtonsoftCompatibilityMode ? "$type" : options.GetPropertyName("Type");
            string typeValue = newtonsoftCompatibilityMode ? type : calendarSerializer.CalendarTypeName;
            writer.WriteString(typeProperty, typeValue);

            writer.WriteString(options.GetPropertyName("Description"), value.Description);

            if (value is BaseCalendar baseCalendar)
            {
                writer.WriteString(options.GetPropertyName("TimeZoneId"), baseCalendar.TimeZone.Id);
            }

            writer.WritePropertyName(options.GetPropertyName("BaseCalendar"));
            if (value.CalendarBase is not null)
            {
                Write(writer, value.CalendarBase, options);
            }
            else
            {
                writer.WriteNullValue();
            }

            calendarSerializer.SerializeFields(writer, value, options);
            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to serialize ICalendar to json", e);
        }
    }
}