using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Calendars;
using Quartz.Impl.Calendar;
using Quartz.Serialization.SystemTextJson;
using Quartz.Util;

namespace Quartz.Converters;

internal sealed class CalendarConverter : JsonConverter<ICalendar>
{
    private readonly bool newtonsoftCompatibilityMode;
    private readonly Dictionary<string, ICalendarSerializer> converters = new (StringComparer.OrdinalIgnoreCase);

    internal CalendarConverter(bool newtonsoftCompatibilityMode)
    {
        this.newtonsoftCompatibilityMode = newtonsoftCompatibilityMode;
        AddSerializer<BaseCalendar>(new BaseCalendarSerializer());
        AddSerializer<AnnualCalendar>(new AnnualCalendarSerializer());
        AddSerializer<CronCalendar>(new CronCalendarSerializer());
        AddSerializer<DailyCalendar>(new DailyCalendarSerializer());
        AddSerializer<HolidayCalendar>(new HolidayCalendarSerializer());
        AddSerializer<MonthlyCalendar>(new MonthlyCalendarSerializer());
        AddSerializer<WeeklyCalendar>(new WeeklyCalendarSerializer());
    }

    private void AddSerializer<TCalendar>(ICalendarSerializer serializer) where TCalendar : class, ICalendar
    {
        converters[typeof(TCalendar).AssemblyQualifiedNameWithoutVersion()] = serializer;
        converters[serializer.CalendarTypeName] = serializer;
    }

    public override bool CanConvert(Type objectType)
    {
        return typeof(ICalendar).IsAssignableFrom(objectType);
    }

    public override ICalendar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        try
        {
            var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
            return DeserializeCalendar(rootElement);

            ICalendar DeserializeCalendar(JsonElement rootElement)
            {
                var type = rootElement.GetProperty(newtonsoftCompatibilityMode ? "$type" : "Type").GetString();

                var calendarSerializer = GetCalendarSerializer(type);
                var calendar = calendarSerializer.Create(rootElement);

                calendar.Description = rootElement.GetProperty("Description").GetString();
                if (calendar is BaseCalendar target)
                {
                    target.TimeZone = rootElement.GetProperty("TimeZoneId").GetTimeZone();
                }

                if (rootElement.TryGetProperty("BaseCalendar", out var baseCalendarJsonElement) && baseCalendarJsonElement.ValueKind != JsonValueKind.Null)
                {
                    calendar.CalendarBase = DeserializeCalendar(baseCalendarJsonElement);
                }

                calendarSerializer.DeserializeFields(calendar, rootElement);
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
            var calendarSerializer = GetCalendarSerializer(type);

            string typeProperty = newtonsoftCompatibilityMode ? "$type" : "Type";
            string typeValue = newtonsoftCompatibilityMode ? type : calendarSerializer.CalendarTypeName;
            writer.WriteString(typeProperty, typeValue);

            writer.WriteString("Description", value.Description);

            writer.WritePropertyName("BaseCalendar");
            if (value.CalendarBase != null)
            {
                Write(writer, value.CalendarBase, options);
            }
            else
            {
                writer.WriteNullValue();
            }

            if (value is BaseCalendar baseCalendar)
            {
                writer.WriteString("TimeZoneId", baseCalendar.TimeZone.Id);
            }

            calendarSerializer.SerializeFields(writer, value);
            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            throw new JsonSerializationException("Failed to serialize ICalendar to json", e);
        }
    }

    private ICalendarSerializer GetCalendarSerializer(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || !converters.TryGetValue(typeName!, out var converter))
        {
            throw new ArgumentException($"Don't know how to handle {typeName}", nameof(typeName));
        }

        return converter;
    }

    internal void AddCalendarConverter<TCalendar>(ICalendarSerializer serializer)
    {
        converters[typeof(TCalendar).AssemblyQualifiedNameWithoutVersion()] = serializer;
    }
}