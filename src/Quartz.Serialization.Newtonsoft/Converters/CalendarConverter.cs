using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Calendars;
using Quartz.Impl.Calendar;
using Quartz.Serialization.Newtonsoft;
using Quartz.Util;

namespace Quartz.Converters;

internal sealed class CalendarConverter : JsonConverter
{
    private static readonly Dictionary<string, ICalendarSerializer> converters = new()
    {
        {typeof(BaseCalendar).AssemblyQualifiedNameWithoutVersion(), new BaseCalendarSerializer()},
        {typeof(AnnualCalendar).AssemblyQualifiedNameWithoutVersion(), new AnnualCalendarSerializer()},
        {typeof(CronCalendar).AssemblyQualifiedNameWithoutVersion(), new CronCalendarSerializer()},
        {typeof(DailyCalendar).AssemblyQualifiedNameWithoutVersion(), new DailyCalendarSerializer()},
        {typeof(HolidayCalendar).AssemblyQualifiedNameWithoutVersion(), new HolidayCalendarSerializer()},
        {typeof(MonthlyCalendar).AssemblyQualifiedNameWithoutVersion(), new MonthlyCalendarSerializer()},
        {typeof(WeeklyCalendar).AssemblyQualifiedNameWithoutVersion(), new WeeklyCalendarSerializer()}
    };

    private static ICalendarSerializer GetCalendarConverter(string typeName)
    {
        if (!converters.TryGetValue(typeName, out var converter))
        {
            throw new ArgumentException($"don't know how to handle {typeName}", nameof(typeName));
        }

        return converter;
    }

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        if (value is not ICalendar calendar)
        {
            throw new ArgumentException("The value must implement ICalendar", nameof(value));
        }

        writer.WriteStartObject();
        writer.WritePropertyName("$type");
        var type = value!.GetType().AssemblyQualifiedNameWithoutVersion();
        writer.WriteValue(type);

        if (value is BaseCalendar baseCalendar)
        {
            // handle base properties
            writer.WritePropertyName("Description");
            writer.WriteValue(baseCalendar.Description);

            writer.WritePropertyName("TimeZoneId");
            writer.WriteValue(baseCalendar.TimeZone?.Id);

            writer.WritePropertyName("BaseCalendar");
            if (baseCalendar.CalendarBase is not null)
            {
                serializer.Serialize(writer, baseCalendar.CalendarBase, baseCalendar.CalendarBase.GetType());
            }
            else
            {
                writer.WriteNull();
            }
        }

        GetCalendarConverter(type).SerializeFields(writer, calendar);

        writer.WriteEndObject();
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        JObject jObject = JObject.Load(reader);
        string type = jObject["$type"]!.Value<string>()!;

        var calendarConverter = GetCalendarConverter(type);
        ICalendar calendar = calendarConverter.Create(jObject);
        if (calendar is BaseCalendar target)
        {
            target.Description = jObject["Description"]!.Value<string>();
            target.TimeZone = TimeZoneUtil.FindTimeZoneById(jObject["TimeZoneId"]!.Value<string>()!);
            var baseCalendar = jObject["BaseCalendar"]!.Value<JObject>();
            if (baseCalendar is not null)
            {
                var baseCalendarType = Type.GetType(baseCalendar["$type"]!.Value<string>()!, true);
                var o = baseCalendar.ToObject(baseCalendarType!, serializer);
                target.CalendarBase = (ICalendar?) o;
            }
        }
        calendarConverter.DeserializeFields(calendar, jObject);
        return calendar;
    }

    public override bool CanConvert(Type objectType)
    {
        return typeof(ICalendar).IsAssignableFrom(objectType);
    }

    internal static void AddCalendarConverter<TCalendar>(ICalendarSerializer serializer)
    {
        converters[typeof(TCalendar).AssemblyQualifiedNameWithoutVersion()] = serializer;
    }
}