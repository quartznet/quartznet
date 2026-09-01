using System;
using System.Collections.Generic;
using System.Reflection;

using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

using Quartz.Util;

namespace Quartz.Converters;

internal sealed class StringKeyDirtyFlagMapConverter : JsonConverter
{
    /// <summary>
    /// The property name Json.NET writes a value's type under, which is metadata rather than an entry
    /// of the map it sits in.
    /// </summary>
    private const string TypeMarker = "$type";

    public override void WriteJson(JsonWriter writer, object? value, JsonSerializer serializer)
    {
        var map = (StringKeyDirtyFlagMap) value!;
        serializer.Serialize(writer, map.WrappedMap);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object? existingValue, JsonSerializer serializer)
    {
        IDictionary<string, object> innerMap = ReadMap(reader, serializer);
        JobDataMap map = new JobDataMap(innerMap);
        return map;
    }

    /// <summary>
    /// Reads the map entry by entry rather than as an <c>IDictionary&lt;string, object&gt;</c>, so that
    /// a stored <c>Dictionary&lt;string, string&gt;</c> comes back as one however it was written.
    /// </summary>
    /// <remarks>
    /// A value in an <see cref="object" />-typed slot is written by Json.NET with a <c>$type</c> beside
    /// it, and by <c>Quartz.Serialization.SystemTextJson</c> as a plain JSON object. Handing the plain
    /// form to Json.NET gets a <see cref="JObject" /> back — a shape the job that stored a dictionary
    /// cannot cast — so a map written by the built-in serializer was unreadable here (#3582). Nothing
    /// this branch writes changes: a payload with a <c>$type</c> is still built by Json.NET out of the
    /// type it names, which is what keeps every blob already in a job store column loading.
    /// </remarks>
    private static Dictionary<string, object> ReadMap(JsonReader reader, JsonSerializer serializer)
    {
        if (reader.TokenType != JsonToken.StartObject)
        {
            throw new JsonSerializationException(
                $"A job data map is stored as a JSON object, and this payload holds {reader.TokenType} where the map should be.");
        }

        Dictionary<string, object> map = new Dictionary<string, object>();

        while (reader.Read() && reader.TokenType == JsonToken.PropertyName)
        {
            string name = (string) reader.Value!;
            reader.Read();
            map[name] = ReadValue(reader, serializer)!;
        }

        return map;
    }

    /// <summary>
    /// Reads one entry's value, which is a string map when the payload holds an object Json.NET wrote
    /// no type beside.
    /// </summary>
    /// <remarks>
    /// The object is loaded with date parsing off, so an entry that merely looks like a timestamp stays
    /// the string the application stored. Json.NET would otherwise hand back a
    /// <see cref="DateTimeOffset" /> and rendering it as a string again would reformat it — a quieter
    /// way to lose a value than the one this fixes. Everything that is not an object keeps the reading
    /// it has always had, date parsing included.
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

        if (source[TypeMarker] != null)
        {
            return source.ToObject<object>(serializer);
        }

        Dictionary<string, string> stringMap = new Dictionary<string, string>(source.Count);
        foreach (JProperty property in source.Properties())
        {
            // Structure inside the map is a blob no reader has an answer for - the built-in serializer
            // fails on it too - and saying so as a serialization exception is what keeps a cast
            // exception from escaping from somewhere further away.
            if (!(property.Value is JValue entry))
            {
                throw new JsonSerializationException(
                    $"A stored string map holds structure under '{property.Name}', and a string map's values are strings. " +
                    "A value with structure of its own has to be serialized by the job and stored as a string.");
            }

            stringMap[property.Name] = entry.Value<string>()!;
        }

        return stringMap;
    }

    public override bool CanConvert(Type objectType)
    {
        return typeof(StringKeyDirtyFlagMap).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
    }
}
