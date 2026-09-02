using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Quartz.Serialization.Newtonsoft;

/// <summary>
/// What a job data value may be when this serializer writes one, which is what it may be when the
/// built-in one does — and, since the two halves belong together, how one is written and read.
/// </summary>
/// <remarks>
/// <para>
/// The write side used to accept whatever Json.NET could reflect over, and reflection being able to
/// write a value is not the same as anything being able to read it back. A <see cref="TimeZoneInfo" />
/// in a job data map was the case that showed it: written as
/// <c>{"$type":"System.TimeZoneInfo, …","Id":"Tokyo Standard Time", …}</c> and then unreadable, because
/// <see cref="QuartzContractResolver" /> sets <c>IgnoreSerializableInterface</c> and every public member
/// of a zone is read-only, so there was nothing to rebuild it from. The blob was in the column by then,
/// and the failure belonged to whoever next ran the job.
/// </para>
/// <para>
/// The accepted set is not a copy of the System.Text.Json one, it <em>is</em> that one — the same
/// <c>JobDataValues.Accepted</c> the built-in serializer refuses against, read out of the core package
/// this one already depends on. Two lists meaning to say the same thing is how the write and read sides
/// came to disagree in the first place, and the same trap is open across the two serializers: both are
/// documented store formats and an application is free to switch between them, so a value only one of
/// them accepts is a one-way door nobody is warned about. <c>JobDataMapPortabilityTest</c> is what holds
/// the pair to it.
/// </para>
/// <para>
/// Past that set, a value is the application's own choice, and
/// <see cref="NewtonsoftJsonSerializerRegistry.AddJobDataValueType{T}" /> is how it declares one. That is
/// this package's counterpart to
/// <c>SystemTextJsonSerializerRegistry.AddTypeInfoResolver</c>: Json.NET needs no metadata handed to it,
/// so the declaration carries nothing but the application's word that the type reads back.
/// </para>
/// </remarks>
internal static class JobDataValues
{
    /// <summary>
    /// Throws unless <paramref name="value" /> is one a reader will accept back.
    /// </summary>
    /// <remarks>
    /// The check is by runtime type rather than by trying the write and inspecting what came out,
    /// because refusing has to happen before a single byte reaches the column.
    /// </remarks>
    /// <exception cref="Quartz.JsonSerializationException">
    /// The value is of a type no reader can turn back into it, and the application has not declared one.
    /// </exception>
    public static void Refuse(string key, object? value, NewtonsoftJsonSerializerRegistry registry)
    {
        if (value is null)
        {
            return;
        }

        Type type = value.GetType();

        // Enums are accepted by rule rather than by name, because they are written as their number and
        // read back as one, and no list can enumerate an application's own.
        if (SystemTextJson.JobDataValues.Accepted.Contains(type) || type.IsEnum || registry.DeclaresJobDataValueType(type))
        {
            // Refused against the same declaration as the accepted set, and for the same reason: the one
            // name a stored string map cannot use is the one Json.NET writes a type under.
            SystemTextJson.JobDataValues.RefuseTypeMarker(key, value);
            return;
        }

        throw new Quartz.JsonSerializationException(
            $"Job data entry '{key}' holds a {type.FullName}, which Quartz's JSON format cannot read back. " +
            "A job data value has to be one of the types JobDataMap declares an accessor for " +
            "(string, bool, char, int, long, float, double, decimal, DateTime, DateTimeOffset, TimeSpan, Guid, DateOnly, TimeOnly or an enum), " +
            "a Dictionary<string, string>, or a type the application declares through NewtonsoftJsonSerializerRegistry.AddJobDataValueType. " +
            "Anything with structure of its own has to be serialized by the job and stored as a string.");
    }

    /// <summary>
    /// Refuses on the first unreadable entry of a map, before any of them is written, so a map with one
    /// such value in it puts nothing at all in the column.
    /// </summary>
    public static void Refuse(JobDataMap jobDataMap, NewtonsoftJsonSerializerRegistry registry)
    {
        foreach (KeyValuePair<string, object?> pair in jobDataMap)
        {
            Refuse(pair.Key, pair.Value, registry);
        }
    }

    /// <summary>
    /// Writes a whole map, entry by entry, as the object the System.Text.Json serializer writes for it.
    /// </summary>
    /// <remarks>
    /// Both converters that write a job data map come through here — the one that writes a map on its
    /// own and the one that writes the map inside a trigger — because a map written two ways is a map
    /// that reads back two ways, which is how the trigger converter came to refuse a
    /// <c>Dictionary&lt;string, string&gt;</c> that the write gate had already accepted.
    /// </remarks>
    public static void WriteMap(JsonWriter writer, IDictionary<string, object?> map, JsonSerializer serializer)
    {
        // Entry by entry rather than by handing Json.NET a Dictionary<string, object>, because the slot
        // a value sits in is what decides whether a $type goes beside it - and a string map has to go
        // out as the plain object the built-in serializer writes.
        writer.WriteStartObject();
        foreach (KeyValuePair<string, object?> pair in map)
        {
            writer.WritePropertyName(pair.Key);
            Write(writer, pair.Value, serializer);
        }

        writer.WriteEndObject();
    }

    /// <summary>
    /// Writes one entry's value, as the bytes the System.Text.Json serializer writes for it.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>Dictionary&lt;string, string&gt;</c> is written by hand as a plain JSON object. Handing it to
    /// Json.NET would put a <c>$type</c> beside it — an object-typed slot cannot name a dictionary, and
    /// <see cref="TypeNameHandling.Auto" /> writes the type when the slot cannot — and the built-in
    /// serializer writes no such thing, so a map written here and read there came back carrying an
    /// assembly-qualified type name as an entry of the application's own. Both are documented store
    /// formats and an application is free to switch between them, so the two have to write the same map.
    /// </para>
    /// <para>
    /// Everything else keeps the <see cref="object" />-typed slot it has always been written in. That is
    /// what puts a <c>$type</c> beside a type the application declared through
    /// <see cref="NewtonsoftJsonSerializerRegistry.AddJobDataValueType{T}" />, which is the whole of how
    /// this serializer reads one back as itself.
    /// </para>
    /// </remarks>
    public static void Write(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        // The exact type rather than a derived one: a subclass is only written at all because the
        // application declared it, and a declaration asks for that type back.
        if (value is Dictionary<string, string> stringMap && value.GetType() == typeof(Dictionary<string, string>))
        {
            writer.WriteStartObject();
            foreach (KeyValuePair<string, string> pair in stringMap)
            {
                writer.WritePropertyName(pair.Key);
                writer.WriteValue(pair.Value);
            }

            writer.WriteEndObject();
            return;
        }

        serializer.Serialize(writer, value, typeof(object));
    }

    /// <summary>
    /// Reads a job data map's entries from the object the reader is positioned on.
    /// </summary>
    /// <remarks>
    /// Read entry by entry rather than as a <c>Dictionary&lt;string, object&gt;</c>, because an object with
    /// no <c>$type</c> on it is a shape Json.NET has no answer for: it hands back a <see cref="JObject" />,
    /// so a string map written by the built-in serializer — or by this one, now — came back as something
    /// the job that stored it cannot cast. <see cref="ReadValue" /> is where that is decided.
    /// </remarks>
    public static Dictionary<string, object?> ReadMap(JsonReader reader, JsonSerializer serializer)
    {
        // A converter is handed the value's first token, and a map that is not an object is a blob written
        // by something else. Saying so is the whole diagnostic; reading on would report a map with nothing
        // in it, which is a job that quietly runs without the data it was given.
        if (reader.TokenType != JsonToken.StartObject)
        {
            throw new Quartz.JsonSerializationException(
                $"A job data map is stored as a JSON object, and this payload holds {reader.TokenType} where the map should be.");
        }

        Dictionary<string, object?> map = new();

        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            string name = (string) reader.Value!;
            reader.Read();
            map[name] = ReadValue(reader, serializer);
        }

        return map;
    }

    /// <summary>
    /// Reads one entry's value, which is a string map when the payload holds an object Json.NET wrote no
    /// type beside.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A <c>$type</c> means Json.NET wrote the value and Json.NET builds it back: a type the application
    /// declared, or a string map written before they went out plain. Both keep reading exactly as they
    /// did, which is what an upgrade needs — the payloads are in databases already.
    /// </para>
    /// <para>
    /// The object is loaded with date parsing off, so an entry that merely looks like a timestamp stays
    /// the string the application stored. Json.NET would otherwise hand back a
    /// <see cref="DateTimeOffset" /> and rendering it as a string again would reformat it — in the
    /// reading machine's culture, at that — which is a quieter way to lose a value than any this fixes.
    /// </para>
    /// </remarks>
    private static object? ReadValue(JsonReader reader, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.StartObject)
        {
            return serializer.Deserialize<object>(reader);
        }

        DateParseHandling previous = reader.DateParseHandling;
        reader.DateParseHandling = DateParseHandling.None;

        JObject source;
        try
        {
            source = JObject.Load(reader);
        }
        finally
        {
            reader.DateParseHandling = previous;
        }

        if (source[SystemTextJson.JobDataValues.TypeMarker] is not null)
        {
            return source.ToObject<object>(serializer);
        }

        Dictionary<string, string> stringMap = new(source.Count);
        foreach (JProperty property in source.Properties())
        {
            // Structure inside the map is a blob no reader has an answer for - the built-in one fails on
            // it too - and saying so as Quartz's own exception is what keeps Json.NET's from escaping.
            if (property.Value is not JValue entry)
            {
                throw new Quartz.JsonSerializationException(
                    $"A stored string map holds structure under '{property.Name}', and a string map's values are strings. " +
                    "A value with structure of its own has to be serialized by the job and stored as a string.");
            }

            stringMap[property.Name] = entry.Value<string>()!;
        }

        return stringMap;
    }
}
