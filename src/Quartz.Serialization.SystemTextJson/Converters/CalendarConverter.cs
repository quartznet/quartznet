using System.Text.Json;
using System.Text.Json.Serialization;

using Quartz.Calendars;
using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Converters;

internal class CalendarConverter : JsonConverter<ICalendar>
{
    private static readonly Dictionary<string, ICalendarSerializer> converters = new()
    {
        { BaseCalendarSerializer.CalendarTypeKey, new BaseCalendarSerializer() },
        { AnnualCalendarSerializer.CalendarTypeKey, new AnnualCalendarSerializer() },
        { CronCalendarSerializer.CalendarTypeKey, new CronCalendarSerializer() },
        { DailyCalendarSerializer.CalendarTypeKey, new DailyCalendarSerializer() },
        { HolidayCalendarSerializer.CalendarTypeKey, new HolidayCalendarSerializer() },
        { MonthlyCalendarSerializer.CalendarTypeKey, new MonthlyCalendarSerializer() },
        { WeeklyCalendarSerializer.CalendarTypeKey, new WeeklyCalendarSerializer() },

        // Support also type name
        { typeof(BaseCalendar).AssemblyQualifiedNameWithoutVersion(), new BaseCalendarSerializer() },
        { typeof(AnnualCalendar).AssemblyQualifiedNameWithoutVersion(), new AnnualCalendarSerializer() },
        { typeof(CronCalendar).AssemblyQualifiedNameWithoutVersion(), new CronCalendarSerializer() },
        { typeof(DailyCalendar).AssemblyQualifiedNameWithoutVersion(), new DailyCalendarSerializer() },
        { typeof(HolidayCalendar).AssemblyQualifiedNameWithoutVersion(), new HolidayCalendarSerializer() },
        { typeof(MonthlyCalendar).AssemblyQualifiedNameWithoutVersion(), new MonthlyCalendarSerializer() },
        { typeof(WeeklyCalendar).AssemblyQualifiedNameWithoutVersion(), new WeeklyCalendarSerializer() }
    };

    public override bool CanConvert(Type objectType) => typeof(ICalendar).IsAssignableFrom(objectType);

    public override ICalendar Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var rootElement = JsonDocument.ParseValue(ref reader).RootElement;
        return DeserializeCalendar(rootElement);

        static ICalendar DeserializeCalendar(JsonElement rootElement)
        {
            var type = rootElement.GetProperty("CalendarType").GetString();

            var calendarSerializer = GetCalendarSerializer(type);
            var calendar = calendarSerializer.Create(rootElement);

            calendar.Description = rootElement.GetProperty("Description").GetString();
            if (calendar is BaseCalendar target)
            {
                target.TimeZone = rootElement.GetProperty("TimeZoneId").GetTimeZone();
            }

            if (rootElement.TryGetProperty("CalendarBase", out var baseCalendarJsonElement) && baseCalendarJsonElement.ValueKind != JsonValueKind.Null)
            {
                calendar.CalendarBase = DeserializeCalendar(baseCalendarJsonElement);
            }

            calendarSerializer.DeserializeFields(calendar, rootElement);
            return calendar;
        }
    }

    public override void Write(Utf8JsonWriter writer, ICalendar value, JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        var type = value.GetType().AssemblyQualifiedNameWithoutVersion();
        var calendarSerializer = GetCalendarSerializer(type);

        writer.WriteString("CalendarType", calendarSerializer.CalendarTypeForJson);
        writer.WriteString("Description", value.Description);

        writer.WritePropertyName("CalendarBase");
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

    private static ICalendarSerializer GetCalendarSerializer(string? typeName)
    {
        if (string.IsNullOrWhiteSpace(typeName) || !converters.TryGetValue(typeName!, out var converter))
        {
            throw new ArgumentException("Don't know how to handle " + typeName);
        }

        return converter;
    }
}