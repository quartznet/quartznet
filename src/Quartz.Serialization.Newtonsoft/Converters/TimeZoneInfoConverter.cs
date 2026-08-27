using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// A time zone travels as its id, which is the only part of a <see cref="TimeZoneInfo" /> that means
/// anything to another process.
/// </summary>
/// <remarks>
/// <para>
/// Without this, a trigger written as a plain object graph — which is what
/// <see cref="Quartz.Impl.NewtonsoftJsonObjectSerializer.RegisterTriggerConverters" /> being off
/// means, and off is the default — lost its zone outright. Json.NET's default contract writes a
/// <see cref="TimeZoneInfo" /> as its public properties, every one of which is read-only, so reading
/// the object back set nothing and the trigger's getter fell through to
/// <see cref="TimeZoneInfo.Local" />. A trigger stored under Tokyo came back running on whichever
/// zone the reading machine happened to be in, and nothing said so.
/// </para>
/// <para>
/// Both forms are read. The object form is what is sitting in job store blobs written before this
/// converter existed, and it carries the id under <c>Id</c>; the string form is what is written from
/// now on, and it is the same spelling the trigger and calendar serializers have always used for a
/// zone. Reading the object form is not optional even after the fix, because a typed member in a blob
/// written before it still carries one.
/// </para>
/// <para>
/// <c>QuartzContractResolver</c> attaches this per property, to members typed as a
/// <see cref="TimeZoneInfo" />, and it is deliberately <em>not</em> in the serializer's converter list:
/// that list is consulted for a value's runtime type wherever the value appears, so a zone held as an
/// <see cref="object" /> — a job data map value — would be written as a bare string and lose the
/// <c>$type</c> that path carries. Scoped to typed members, this reaches every trigger's
/// <c>TimeZone</c> and nothing else.
/// </para>
/// </remarks>
internal sealed class TimeZoneInfoConverter : JsonConverter
{
    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        writer.WriteValue(((TimeZoneInfo) value!).Id);
    }

    public override object? ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        switch (reader.TokenType)
        {
            case JsonToken.Null:
                return null;

            case JsonToken.String:
                return TimeZones.FindById((string) reader.Value!);

            case JsonToken.StartObject:
                // The shape Json.NET's default contract used to write: every public property of a
                // TimeZoneInfo, of which only the id is portable.
                JObject source = JObject.Load(reader);
                string? id = source.Value<string>("Id");
                if (string.IsNullOrEmpty(id))
                {
                    throw new Quartz.JsonSerializationException($"Could not read a time zone from {source.ToString(Formatting.None)}: no Id");
                }

                return TimeZones.FindById(id);

            default:
                throw new Quartz.JsonSerializationException($"Could not read a time zone from a {reader.TokenType} token");
        }
    }

    public override bool CanConvert(Type objectType) => objectType == typeof(TimeZoneInfo);
}
