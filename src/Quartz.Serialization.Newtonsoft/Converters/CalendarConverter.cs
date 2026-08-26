using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Impl.Calendar;
using Quartz.Util;

namespace Quartz.Serialization.Newtonsoft;

internal sealed class CalendarConverter(NewtonsoftJsonSerializerRegistry registry) : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        try
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

            registry.GetCalendarSerializer(type).SerializeFields(writer, calendar);

            writer.WriteEndObject();
        }
        catch (Exception e)
        {
            // Quartz's exception, deliberately qualified - see the note in TriggerConverter, which this
            // mirrors. Without it a calendar naming a serializer the registry lacks came out as the raw
            // ArgumentException the registry throws, past every catch that handles a store read.
            throw new Quartz.JsonSerializationException("Failed to serialize ICalendar to json", e);
        }
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        try
        {
            JObject jObject = JObject.Load(reader);
            string type = jObject["$type"]!.Value<string>()!;

            var calendarConverter = registry.GetCalendarSerializer(type);
            ICalendar calendar = calendarConverter.Create(jObject);
            if (calendar is BaseCalendar target)
            {
                target.Description = jObject["Description"]!.Value<string>();
                target.TimeZone = TimeZones.FindById(jObject["TimeZoneId"]!.Value<string>()!);
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
        catch (Exception e)
        {
            // Quartz's exception, deliberately qualified - see the note on the serialize side above.
            throw new Quartz.JsonSerializationException("Failed to parse ICalendar from json", e);
        }
    }

    public override bool CanConvert(Type objectType)
    {
        return typeof(ICalendar).IsAssignableFrom(objectType);
    }
}