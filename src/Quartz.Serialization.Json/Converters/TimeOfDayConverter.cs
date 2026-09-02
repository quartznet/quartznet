using System;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Converters;

/// <summary>
/// A <see cref="TimeOfDay" /> is read back out of the hour, minute and second Json.NET writes for it.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a trigger written as a plain object graph - which is what
/// <c>RegisterTriggerConverters</c> being off means, and off is the default - could not be read back at
/// all. <see cref="TimeOfDay" /> has two public constructors and no
/// parameterless one, so Json.NET had nothing to build <c>EndTimeOfDay</c> with and the read threw
/// <c>Unable to find a constructor to use for type Quartz.TimeOfDay</c>. <c>StartTimeOfDay</c> was the
/// silent half of the same defect: its getter hands out a default <c>00:00:00</c> for Json.NET to
/// populate in place, and every member of a <see cref="TimeOfDay" /> is read-only, so a trigger stored
/// with a start of 03:30 came back starting at midnight and nothing said so (#3508).
/// </para>
/// <para>
/// Only the read side is this converter's: <see cref="CanWrite" /> is <see langword="false" />, so the
/// object form Json.NET has always written is still what goes out, and every blob already sitting in a
/// job store column is a blob this reads. Nothing about the stored bytes changes.
/// </para>
/// <para>
/// <see cref="QuartzContractResolver" /> attaches this per property, to members typed as a
/// <see cref="TimeOfDay" />, the same way it attaches <see cref="TimeZoneInfoConverter" /> and for the
/// same reason: the serializer's converter list is consulted for a value's runtime type wherever the
/// value appears, so a <see cref="TimeOfDay" /> held as an <see cref="object" /> - a job data map value -
/// would lose the <c>$type</c> that path carries. Scoped to typed members, this reaches every trigger's
/// times of day and nothing else.
/// </para>
/// </remarks>
internal sealed class TimeOfDayConverter : JsonConverter
{
    public override bool CanWrite => false;

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        // CanWrite is false, so Json.NET writes the value with the default contract and never calls this.
        throw new NotSupportedException("A time of day is written by the default contract.");
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
                return null;

            case JsonToken.StartObject:
                JObject source = JObject.Load(reader);
                return new TimeOfDay(ReadPart(source, "Hour"), ReadPart(source, "Minute"), ReadPart(source, "Second"));

            default:
                throw new JsonSerializationException($"Could not read a time of day from a {reader.TokenType} token");
        }
    }

    /// <summary>
    /// One part of the time, or zero when the payload does not carry it - which is what populating a
    /// default <see cref="TimeOfDay" /> in place used to leave behind.
    /// </summary>
    private static int ReadPart(JObject source, string name)
    {
        foreach (JProperty property in source.Properties())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return property.Value.Value<int>();
            }
        }

        return 0;
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(TimeOfDay);
}
